using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TwinRealms;

/// <summary>
/// Chat over the network (focus A-31) and the small party additions. The host decides everything (the online server, or the host of a friend's world):
///   R_ChatSay (client -> host): channel, whisper target (a peer id) and the text. Nothing else: the sender is the connection it came
///     from and the name shown is the one in the host's roster for that connection (ChatName), never a name in the message.
///   R_ChatMsg (host -> client): one delivered line (channel, speaker's peer id and name, text, and on the echo of a whisper who it went to).
///   R_ChatSys (host -> client): a line from the host ("You are muted", "X is not online").
///   R_SocHello (client -> host, once per session): "I am in the world": the host loads the account's friends and block list
///     (online server) or takes the block list the game keeps itself (friend's world).
/// World: everybody. Local: players in the same zone within chat.localRadius metres. Party: the sender's party. Whisper: one player
/// (and the echo). A receiver who blocks the sender gets nothing. ChatServer.Check (Chat.cs) cleans, rate limits and enforces the mute
/// (the account's MuteUntil, read with the account flags and kept current by /mute). The online server logs every line to
/// TR_DATA/chatlog/chat.log (rotated; ChatServer.Log).
/// Party: /party kick and /party leader (leader only), the size limit from data (party.max), invites from a blocked player are dropped.
/// </summary>
public partial class Net
{
    sealed class SocCache { public string Key = ""; public HashSet<string> Friends = new(), Blocked = new(); }
    readonly Dictionary<long, SocCache> _soc = new();        // server: per connection, the friends and block list
    long _socHelloFor;                                       // client: the session we said hello in
    /// <summary>Most players in one party (data/social.json party.max).</summary>
    public static int PartyMax => Math.Clamp(SocialCfg.I("party.max", 6), 2, 40);

    /// <summary>Server: who a connection is for block lists: the account on the online server, the hero's name in a friend's world.</summary>
    string SocId(long peer) => Dedicated ? AcctOf(peer) : "n:" + NameOf(peer).Trim().ToLowerInvariant();
    /// <summary>Server: the speaker's name, from the roster of this connection.</summary>
    string ChatName(long peer) => ChatServer.CleanName(Players.GetValueOrDefault(peer)?.Name);
    /// <summary>Server: does `who` block `sender`?</summary>
    public bool Ignores(long who, long sender)
    {
        if (who == sender) return false;
        if (who == MyId && !Dedicated) return Friends.IsBlocked(NameOf(sender));
        return _soc.TryGetValue(who, out var c) && c.Blocked.Contains(SocId(sender));
    }
    /// <summary>Server: where a player stands and in which zone (0 overworld, 1 a private dungeon, 2 the arena, 1000+ a party dungeon).</summary>
    (Vector3 at, int zone)? WhereOf(long id)
    {
        if (id == MyId && !Dedicated) return Player.Instance is { } p && IsInstanceValid(p) ? (p.GlobalPosition, MyZone) : null;
        return Heroes.TryGetValue(id, out var h) && IsInstanceValid(h) && h.HasPose ? (h.GlobalPosition, h.Zone) : null;
    }
    /// <summary>Server: are two players in the same place (shared zone) within r metres? Private dungeons and the arena are never shared.</summary>
    public bool Near(long a, long b, float r)
    {
        if (a == b) return true;
        if (WhereOf(a) is not { } wa || WhereOf(b) is not { } wb || wa.zone != wb.zone || !(wa.zone == 0 || wa.zone >= 1000)) return false;
        return wa.at.DistanceTo(wb.at) <= r;
    }

    // ================================================================== chat
    public void ChatSay(int ch, long target, string text)
    {
        if (!Active) return;
        if (text.Length > 1000) text = text[..1000];
        if (_server) HostChat(MyId, ch, target, text); else RpcId(1, MethodName.R_ChatSay, ch, target, text);
    }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    void R_ChatSay(int ch, long target, string text) { if (_server && text.Length <= 1000) HostChat(Sender, ch, target, text); }

    void HostChat(long from, int ch, long target, string text)
    {
        if (!Players.ContainsKey(from) || (Dedicated && from == MyId) || ch < ChatServer.World || ch > ChatServer.Whisper) return;
        if (Dedicated && !_flagsOf.ContainsKey(from)) PeerIsAdmin(from);   // the account's flags (the mute among them) are read once per session
        string id = SocId(from), name = ChatName(from);
        var v = ChatServer.Check(id, text, _muteUntil.GetValueOrDefault(from), Time.GetTicksMsec() / 1000.0);
        if (!v.ok) { ChatSys(from, v.msg); return; }
        var to = new List<long>(); string toName = "";
        switch (ch)
        {
            case ChatServer.World: to.AddRange(Players.Keys); break;
            case ChatServer.Local:
            {
                float r = SocialCfg.F("chat.localRadius", 40);
                foreach (var p in Players.Keys) if (p == from || Near(from, p, r)) to.Add(p);
                break;
            }
            case ChatServer.Party:
            {
                var pt = PartyOf(from); if (pt == null) { ChatSys(from, SocialCfg.T("noParty", "You are not in a party.")); return; }
                to.AddRange(pt.Members); break;
            }
            default:
            {
                if (target == from || !Players.ContainsKey(target) || (Dedicated && target == MyId)) { ChatSys(from, SocialCfg.T("notHere", "That player is not online.")); return; }
                toName = ChatName(target);
                if (Ignores(target, from)) { ChatSys(from, SocialCfg.T("ignored", "{name} is not receiving your messages.").Replace("{name}", toName)); return; }
                to.Add(target); to.Add(from); break;
            }
        }
        foreach (var r in to)
        {
            if (!Players.ContainsKey(r) || (Dedicated && r == MyId) || (r != from && Ignores(r, from))) continue;
            string echo = ch == ChatServer.Whisper && r == from ? toName : "";
            if (r == MyId) Chat.Receive(ch, from, name, v.text, echo); else RpcId(r, MethodName.R_ChatMsg, ch, from, name, v.text, echo);
        }
        Count("chat");
        if (Dedicated || SocialLog.Dir != "") ChatServer.Log(ch, name, Dedicated ? id : "", toName, v.text);
    }
    [Rpc(MultiplayerApi.RpcMode.Authority)]
    void R_ChatMsg(int ch, long from, string name, string text, string to) => Chat.Receive(ch, from, ChatServer.CleanName(name), text, to);
    [Rpc(MultiplayerApi.RpcMode.Authority)]
    void R_ChatSys(string text) => Chat.System(text);
    /// <summary>Server: a line from the host to one player's chat box.</summary>
    void ChatSys(long peer, string text)
    {
        if (text == "") return;
        if (peer == MyId && !Dedicated) Chat.System(text); else if (peer != MyId && Players.ContainsKey(peer)) RpcId(peer, MethodName.R_ChatSys, text);
    }

    // ================================================================== session start, end, tick
    /// <summary>Client, every frame from the chat box: once our hero is in a shared world, tell the host (it loads the friends and block lists).</summary>
    public void SocialHelloTick()
    {
        if (!Active) { _socHelloFor = 0; return; }
        if (_socHelloFor == _myId || !Players.ContainsKey(_myId) || Player.Instance == null) return;
        _socHelloFor = _myId; Friends.OnJoined();
        if (IsClient) RpcId(1, MethodName.R_SocHello, Game.Online ? "" : Friends.LocalBlockedLines());
    }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    void R_SocHello(string blocked)
    {
        if (!_server) return; long id = Sender; if (id == MyId || !Players.ContainsKey(id) || blocked.Length > 4000) return;
        var c = _soc[id] = new SocCache();
        if (!Dedicated) { foreach (var n in blocked.Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(200)) c.Blocked.Add("n:" + n.Trim().ToLowerInvariant()); return; }
        FriendsHello(id, c);   // NetFriends.cs
    }
    /// <summary>Server, each physics frame: trade countdowns and time-outs.</summary>
    void SocialTick(float dt) => TxTick(dt);
    /// <summary>Server: a player left (before they leave the roster): their trade ends, friends are told, their lists are forgotten.</summary>
    void SocialPeerLeft(long id)
    {
        TxPeerLeft(id); FriendsBye(id);
        if (Players.ContainsKey(id)) ChatServer.Forget(SocId(id));
        _soc.Remove(id); _socRate.Remove(id); _pqRate.Remove(id);
    }
    /// <summary>We left the world (or stopped hosting): nothing of the session is kept.</summary>
    void SocialReset()
    {
        _soc.Clear(); _tx.Clear(); _txReq.Clear(); _socRate.Clear(); _pqRate.Clear(); _invites.Clear(); _socHelloFor = 0;
        Trade.Cancelled(""); Friends.Left(); PartyQuests.Reset();
    }

    // ================================================================== party: invites the host delivered, remove a member, hand over the lead
    readonly Dictionary<(long from, long to), ulong> _invites = new();
    void InviteNoted(long from, long to)
    {
        ulong now = Time.GetTicksMsec(); _invites[(from, to)] = now;
        if (_invites.Count > 256) foreach (var k in _invites.Where(kv => now - kv.Value > 120000).Select(kv => kv.Key).ToList()) _invites.Remove(k);
    }
    /// <summary>Server: was `to` invited by `from` within the last two minutes? An invite answers once.</summary>
    bool InviteTaken(long from, long to) => _invites.Remove((from, to), out ulong at) && Time.GetTicksMsec() - at < 120000;

    public void PartyKick(long target) { if (_server) HostPartyKick(MyId, target); else RpcId(1, MethodName.R_PartyKick, target); }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)] void R_PartyKick(long target) { if (_server) HostPartyKick(Sender, target); }
    void HostPartyKick(long by, long target)
    {
        var p = PartyOf(by); if (p == null || p.Leader != by || by == target || !p.Members.Contains(target)) return;
        Tell(target, "You were removed from the party"); HostLeaveParty(target, true);
    }
    public void PartyLead(long target) { if (_server) HostPartyLead(MyId, target); else RpcId(1, MethodName.R_PartyLead, target); }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)] void R_PartyLead(long target) { if (_server) HostPartyLead(Sender, target); }
    void HostPartyLead(long by, long target)
    {
        var p = PartyOf(by); if (p == null || p.Leader != by || by == target || !p.Members.Contains(target)) return;
        p.Leader = target; foreach (var m in p.Members) Tell(m, NameOf(target) + " now leads the party"); SendRoster();
    }
}
