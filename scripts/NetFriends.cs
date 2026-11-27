using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace TwinRealms;

/// <summary>
/// Friends and block lists over the network (focus A-32).
///   R_Soc (client -> host): op list | add | accept | decline | remove | block | unblock, and a hero name. The online server turns
///     the name into an account (a player online by the roster; otherwise the account whose last hero had that name, searched off the
///     game thread) and runs FriendsServer on the account files. Players never see account keys.
///   R_SocList (host -> client): the lists as names with online status, level and place (SocialView).
///   R_SocNote (host -> client): "X came online", "X wants to be your friend" ...
/// A friend's hosted world has no accounts: there the host only keeps each player's block list (by hero name) for the session.
/// At most friends.opsPerSec requests a second from one player.
/// </summary>
public partial class Net
{
    readonly Dictionary<long, (ulong sec, int n)> _socRate = new();

    public void Soc(string op, string arg = "")
    {
        if (!Active) return; if (arg.Length > 64) arg = arg[..64];
        if (_server) HostSoc(MyId, op, arg); else RpcId(1, MethodName.R_Soc, op, arg);
    }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    void R_Soc(string op, string arg) { if (_server && op.Length <= 16 && arg.Length <= 64) HostSoc(Sender, op, arg); }

    long PeerOfAcct(string key) { foreach (var kv in _acct) if (kv.Value == key && Players.ContainsKey(kv.Key)) return kv.Key; return 0; }
    void SocNote(long peer, string text) { if (text != "" && peer != 0 && peer != MyId && Players.ContainsKey(peer)) RpcId(peer, MethodName.R_SocNote, text); }
    [Rpc(MultiplayerApi.RpcMode.Authority)] void R_SocNote(string text) => Friends.Note(text);
    [Rpc(MultiplayerApi.RpcMode.Authority)] void R_SocList(string json) => Friends.Got(json);

    void HostSoc(long id, string op, string arg)
    {
        if (!Players.ContainsKey(id) || (Dedicated && id == MyId)) return;
        ulong sec = Time.GetTicksMsec() / 1000; var rt = _socRate.GetValueOrDefault(id); rt = rt.sec == sec ? (sec, rt.n + 1) : (sec, 1); _socRate[id] = rt;
        if (rt.n > Math.Max(1, SocialCfg.I("friends.opsPerSec", 4))) return;
        arg = arg.Replace('_', ' ').Trim().Trim('"').Trim();
        if (!Dedicated)
        {   // a friend's world: only the block list, by name, for this session (the host's own list is its game's)
            if (id == MyId || arg == "") return;
            if (!_soc.TryGetValue(id, out var hc)) _soc[id] = hc = new SocCache();
            if (op == "block" && hc.Blocked.Count < 200) hc.Blocked.Add("n:" + arg.ToLowerInvariant()); else if (op == "unblock") hc.Blocked.Remove("n:" + arg.ToLowerInvariant());
            return;
        }
        string key = AcctOf(id); if (key == "") return; string me = NameOf(id);
        if (op == "list") { SocSendList(id); return; }
        if (arg == "" || arg.StartsWith('@')) return;
        switch (op)
        {
            case "add":
                SocResolve(id, key, arg, (k, n) =>
                {
                    var r = FriendsServer.Request(key, me, k, n); SocNote(id, r.msg); if (!r.ok) return;
                    long other = PeerOfAcct(k); bool hidden = !r.mutual && (Accounts.Info(k)?.Blocked?.Exists(p => p.Key == key) ?? false);
                    if (r.mutual) SocNote(other, $"{me} is now your friend."); else if (!hidden) SocNote(other, $"{me} wants to be your friend. Open Friends, or type /friend accept {me}");
                    SocRefresh(key); SocRefresh(k);
                });
                break;
            case "block":
                SocResolve(id, key, arg, (k, n) => { var r = FriendsServer.Block(key, k, n); SocNote(id, r.msg); SocRefresh(key); SocRefresh(k); });
                break;
            case "accept": case "decline": case "remove": case "unblock":
            {
                var acc = Accounts.Info(key); if (acc == null) return; FriendsServer.Fix(acc);
                var list = op is "accept" or "decline" ? acc.FriendIn : op == "remove" ? acc.Friends : acc.Blocked;
                var pal = list.Find(p => p.Name.Trim().Equals(arg, StringComparison.OrdinalIgnoreCase));
                if (pal == null)
                {   // they may play another hero now: by the roster
                    foreach (var p in Players.Values) if (p.Name.Trim().Equals(arg, StringComparison.OrdinalIgnoreCase) && AcctOf(p.Id) is { Length: > 0 } ak && list.Find(x => x.Key == ak) is { } hit) { pal = hit; break; }
                }
                if (pal == null) { SocNote(id, $"{arg} is not on that list."); return; }
                var r = op switch { "accept" => FriendsServer.Answer(key, me, pal.Key, true), "decline" => FriendsServer.Answer(key, me, pal.Key, false), "remove" => FriendsServer.Remove(key, pal.Key, pal.Name), _ => FriendsServer.Unblock(key, pal.Key, pal.Name) };
                SocNote(id, r.msg);
                if (r.ok && op == "accept") SocNote(PeerOfAcct(pal.Key), $"{me} accepted your friend request.");
                SocRefresh(key); SocRefresh(pal.Key);
                break;
            }
        }
    }

    /// <summary>Server: the account behind a hero name, then `then(account key, hero name)` on the game thread. Online players by the roster; others by the last hero of each account.</summary>
    void SocResolve(long id, string key, string name, Action<string, string> then)
    {
        foreach (var p in Players.Values)
            if (p.Id != id && p.Id != MyId && p.Name.Trim().Equals(name, StringComparison.OrdinalIgnoreCase) && AcctOf(p.Id) is { Length: > 0 } k) { if (k == key) break; then(k, p.Name); return; }
        Task.Run(() =>
        {
            var l = Accounts.FindByHero(name);
            Callable.From(() =>
            {
                if (!_server || !Dedicated || !Players.ContainsKey(id) || AcctOf(id) != key) return;   // they left meanwhile
                var hit = l.Where(x => x.key != key).OrderByDescending(x => x.acc.LastLogin).ToList();
                if (hit.Count == 0) { SocNote(id, l.Count > 0 ? "That is your own hero." : $"No hero named {name} was found."); return; }
                then(hit[0].key, hit[0].acc.Hero != "" ? hit[0].acc.Hero : name);
            }).CallDeferred();
        });
    }

    void SocLoad(SocCache c)
    {
        c.Friends.Clear(); c.Blocked.Clear(); var a = Accounts.Info(c.Key); if (a == null) return; FriendsServer.Fix(a);
        foreach (var f in a.Friends) c.Friends.Add(f.Key); foreach (var b in a.Blocked) c.Blocked.Add(b.Key);
    }
    /// <summary>Server: an account's lists changed: its connection (if online) gets the new lists.</summary>
    void SocRefresh(string key)
    {
        long peer = PeerOfAcct(key); if (peer == 0) return;
        if (!_soc.TryGetValue(peer, out var c)) _soc[peer] = c = new SocCache();
        c.Key = key; SocLoad(c); SocSendList(peer);
    }
    string WhereText(long peer)
    {
        if (WhereOf(peer) is not { } w) return "entering the world";
        return w.zone >= 1000 ? "in a party dungeon" : w.zone == 1 ? "in a dungeon" : w.zone == 2 ? "in the arena" : HudCanvas.PlaceName(w.at);
    }
    void SocSendList(long id)
    {
        string key = AcctOf(id); var a = Accounts.Info(key); if (a == null || !Players.ContainsKey(id)) return; FriendsServer.Fix(a);
        PalView V(Accounts.Pal p, bool where)
        {
            long peer = PeerOfAcct(p.Key); var info = peer != 0 ? Players.GetValueOrDefault(peer) : null;
            return new PalView { Name = info != null ? ChatServer.CleanName(info.Name) : ChatServer.CleanName(p.Name), On = info != null, Peer = where ? peer : 0, Level = where ? info?.Level ?? 0 : 0, Where = where && info != null ? WhereText(peer) : "" };
        }
        var v = new SocialView();
        foreach (var p in a.Friends) v.Friends.Add(V(p, true));
        foreach (var p in a.FriendIn) v.In.Add(V(p, false));
        foreach (var p in a.Blocked) v.Blocked.Add(new PalView { Name = ChatServer.CleanName(p.Name) });   // no status of a blocked player
        RpcId(id, MethodName.R_SocList, ToJson(v));
    }

    /// <summary>Online server: a player entered the world: load their lists, tell their friends, show open requests.</summary>
    void FriendsHello(long id, SocCache c)
    {
        string key = AcctOf(id); if (key == "") return; c.Key = key; SocLoad(c); string name = ChatName(id);
        foreach (var kv in _soc) if (kv.Key != id && kv.Value.Friends.Contains(key) && c.Friends.Contains(kv.Value.Key)) SocNote(kv.Key, $"Your friend {name} came online.");
        SocSendList(id);
        int open = Accounts.Info(key)?.FriendIn?.Count ?? 0; if (open > 0) SocNote(id, open == 1 ? "You have a friend request. Open Friends in the pause menu." : $"You have {open} friend requests. Open Friends in the pause menu.");
    }
    void FriendsBye(long id)
    {
        if (!Dedicated || !_soc.TryGetValue(id, out var c) || c.Key == "") return; string name = ChatName(id);
        foreach (var kv in _soc) if (kv.Key != id && kv.Value.Friends.Contains(c.Key) && c.Friends.Contains(kv.Value.Key)) SocNote(kv.Key, $"Your friend {name} went offline.");
    }
}
