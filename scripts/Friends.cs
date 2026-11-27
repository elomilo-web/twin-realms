using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>A friend (or a request, or a blocked player) as the server shows it to the player: never an account key.</summary>
public class PalView { public string Name = "", Where = ""; public bool On; public int Level; public long Peer; }
public class SocialView { public List<PalView> Friends = new(), In = new(), Blocked = new(); }

/// <summary>
/// The friends list on the online server (focus A-32). It lives on the account file (Accounts.Account: Friends, FriendIn, Blocked; each
/// entry the other account's key and the hero name it had), so it follows the account to any computer and every hero. A friendship is
/// mutual: add = a request the other accepts or declines; asking someone who already asked you makes you friends at once. Remove takes
/// it off both lists. Block (the chat's /ignore) also ends the friendship and drops requests either way; a blocked player's requests are
/// answered as if they were sent, so a block is not revealed. Limits in data/social.json "friends". These functions only touch the
/// account files (under their lock), so the self-test runs them on fake accounts; Net (NetFriends.cs) finds accounts by hero name.
/// </summary>
public static class FriendsServer
{
    static int Max => Math.Max(1, SocialCfg.I("friends.max", 100));
    static int MaxReq => Math.Max(1, SocialCfg.I("friends.maxRequests", 30));
    static int MaxBlocked => Math.Max(1, SocialCfg.I("friends.maxBlocked", 100));
    public static void Fix(Accounts.Account a) { a.Friends ??= new(); a.FriendIn ??= new(); a.Blocked ??= new(); }
    public static bool Has(List<Accounts.Pal> l, string key) => l.Exists(p => p.Key == key);
    static Accounts.Account? Get(string key) { var a = Accounts.Info(key); if (a != null) Fix(a); return a; }

    public static (bool ok, string msg, bool mutual) Request(string from, string fromName, string to, string toName)
    {
        if (from == "" || to == "") return (false, "No such player.", false);
        if (from == to) return (false, "You cannot befriend yourself.", false);
        var me = Get(from); var them = Get(to); if (me == null || them == null) return (false, "No such player.", false);
        if (Has(me.Friends, to)) return (false, $"{toName} is already your friend.", false);
        if (Has(me.Blocked, to)) return (false, $"You block {toName}. /unignore them first.", false);
        if (me.Friends.Count >= Max) return (false, "Your friends list is full.", false);
        if (Has(me.FriendIn, to)) { var r = Answer(from, fromName, to, true); return (r.ok, r.msg, r.ok); }   // they asked first
        string sent = $"Friend request sent to {toName}.";
        if (Has(them.Blocked, from)) return (true, sent, false);   // the same answer: a block is not revealed (and nothing is stored)
        if (Has(them.FriendIn, from)) return (true, sent, false);
        if (them.FriendIn.Count >= MaxReq) return (false, $"{toName} has too many open friend requests.", false);
        bool ok = Accounts.Update(to, a => { Fix(a); if (!Has(a.FriendIn, from)) a.FriendIn.Add(new Accounts.Pal { Key = from, Name = fromName }); });
        return ok ? (true, sent, false) : (false, "The request could not be stored.", false);
    }
    /// <summary>Accept or decline a request `other` sent to `key`.</summary>
    public static (bool ok, string msg) Answer(string key, string name, string other, bool accept)
    {
        var me = Get(key); var req = me?.FriendIn.Find(p => p.Key == other); if (me == null || req == null) return (false, "There is no such friend request.");
        if (accept && me.Friends.Count >= Max) return (false, "Your friends list is full.");
        if (accept && (Get(other)?.Friends.Count ?? Max) >= Max) return (false, $"{req.Name}'s friends list is full.");
        Accounts.Update(key, a => { Fix(a); a.FriendIn.RemoveAll(p => p.Key == other); if (accept && !Has(a.Friends, other)) a.Friends.Add(new Accounts.Pal { Key = other, Name = req.Name }); });
        if (!accept) return (true, $"You declined {req.Name}'s friend request.");
        Accounts.Update(other, a => { Fix(a); a.FriendIn.RemoveAll(p => p.Key == key); if (!Has(a.Friends, key)) a.Friends.Add(new Accounts.Pal { Key = key, Name = name }); });
        return (true, $"{req.Name} is now your friend.");
    }
    public static (bool ok, string msg) Remove(string key, string other, string otherName)
    {
        bool had = false;
        Accounts.Update(key, a => { Fix(a); had = a.Friends.RemoveAll(p => p.Key == other) > 0; });
        Accounts.Update(other, a => { Fix(a); a.Friends.RemoveAll(p => p.Key == key); });
        return had ? (true, $"{otherName} is no longer your friend.") : (false, $"{otherName} is not on your friends list.");
    }
    public static (bool ok, string msg) Block(string key, string other, string otherName)
    {
        if (key == other || other == "") return (false, "You cannot block yourself.");
        bool full = false;
        if (!Accounts.Update(key, a =>
        {
            Fix(a); a.Friends.RemoveAll(p => p.Key == other); a.FriendIn.RemoveAll(p => p.Key == other);
            if (Has(a.Blocked, other)) return; if (a.Blocked.Count >= MaxBlocked) { full = true; return; }
            a.Blocked.Add(new Accounts.Pal { Key = other, Name = otherName });
        })) return (false, "The block could not be stored.");
        if (full) return (false, "Your block list is full.");
        Accounts.Update(other, a => { Fix(a); a.Friends.RemoveAll(p => p.Key == key); a.FriendIn.RemoveAll(p => p.Key == key); });
        return (true, $"You now block {otherName}: no chat, whispers, invites, trades or friend requests from them.");
    }
    public static (bool ok, string msg) Unblock(string key, string other, string otherName)
    {
        bool had = false; Accounts.Update(key, a => { Fix(a); had = a.Blocked.RemoveAll(p => p.Key == other) > 0; });
        return had ? (true, $"You no longer block {otherName}.") : (false, $"{otherName} is not blocked.");
    }
}

/// <summary>
/// The player's side of friends and the block list. Online server: the lists come from the server (View) and every change is a request
/// to it (Net.Soc). A friend's hosted world has no accounts, so the game keeps its own file (user://social.json): the players met there
/// lately ("recent players") and a block list by hero name, which the game applies itself and also hands to the host for the session.
/// </summary>
public static class Friends
{
    public static SocialView View = new();
    public class Recent { public string Name = ""; public long Last; }
    public class LocalData { public List<Recent> Recent = new(); public List<string> Blocked = new(); }
    const string LocalPath = "user://social.json"; static LocalData? _local; static float _t;
    static readonly JsonSerializerOptions Opt = new() { IncludeFields = true };
    public static LocalData Local
    {
        get
        {
            if (_local != null) return _local;
            try { using var f = Godot.FileAccess.Open(LocalPath, Godot.FileAccess.ModeFlags.Read); if (f != null) _local = JsonSerializer.Deserialize<LocalData>(f.GetAsText(), Opt); } catch { }
            _local ??= new LocalData(); _local.Recent ??= new(); _local.Blocked ??= new();
            return _local;
        }
    }
    static void SaveLocal() { try { using var f = Godot.FileAccess.Open(LocalPath, Godot.FileAccess.ModeFlags.Write); f?.StoreString(JsonSerializer.Serialize(Local, Opt)); } catch { } }

    /// <summary>True while the lists are the account's on the online server.</summary>
    public static bool ServerSide => Game.Online && Net.IsClient;
    static string Low(string s) => (s ?? "").Trim().ToLowerInvariant();
    public static bool IsBlocked(string name)
    {
        string n = Low(name); if (n == "") return false;
        return ServerSide ? View.Blocked.Exists(b => Low(b.Name) == n) : Local.Blocked.Contains(n);
    }
    public static bool IsFriend(string name) { string n = Low(name); return ServerSide && View.Friends.Exists(b => Low(b.Name) == n); }
    public static string LocalBlockedLines() => string.Join('\n', Local.Blocked.Take(200));
    const string OnlineOnly = "Friends lists are kept on the online server (Play Online). In a friend's world the Friends window lists the players you met lately.";

    public static string Add(string name) { if (name == "") return "usage: /friend add <player>"; if (!ServerSide) return OnlineOnly; Net.I!.Soc("add", name); return ""; }
    public static string Remove(string name) { if (!ServerSide) return OnlineOnly; Net.I!.Soc("remove", name); return ""; }
    public static string Answer(string name, bool accept) { if (!ServerSide) return OnlineOnly; Net.I!.Soc(accept ? "accept" : "decline", name); return ""; }
    public static string Block(string name)
    {
        name = name.Trim(); if (name == "") return "usage: /ignore <player>";
        if (ServerSide) { Net.I!.Soc("block", name); return ""; }
        if (Low(name) == Low(Game.D.Name)) return "You cannot block yourself.";
        if (!Local.Blocked.Contains(Low(name))) { if (Local.Blocked.Count >= SocialCfg.I("friends.maxBlocked", 100)) return "Your block list is full."; Local.Blocked.Add(Low(name)); SaveLocal(); }
        if (Net.IsClient) Net.I!.Soc("block", name);
        Ui.I?.RefreshFriends(); return $"You now block {name} (on this computer, in friends' worlds).";
    }
    public static string Unblock(string name)
    {
        name = name.Trim(); if (ServerSide) { Net.I!.Soc("unblock", name); return ""; }
        bool had = Local.Blocked.Remove(Low(name)); if (had) SaveLocal(); if (Net.IsClient) Net.I!.Soc("unblock", name);
        Ui.I?.RefreshFriends(); return had ? $"You no longer block {name}." : $"{name} is not blocked.";
    }
    public static string BlockedText()
    {
        var l = ServerSide ? View.Blocked.Select(b => b.Name).ToList() : Local.Blocked.ToList();
        return l.Count == 0 ? "You block nobody. /ignore <player> blocks one." : "Blocked: " + string.Join(", ", l);
    }
    public static string ListText()
    {
        if (!ServerSide) return Local.Recent.Count == 0 ? OnlineOnly : "Recent players: " + string.Join(", ", Local.Recent.OrderByDescending(r => r.Last).Select(r => r.Name));
        Net.I!.Soc("list");
        var sb = new System.Text.StringBuilder();
        if (View.In.Count > 0) sb.Append("Requests: " + string.Join(", ", View.In.Select(p => p.Name)) + "\n");
        sb.Append(View.Friends.Count == 0 ? "No friends yet. /friend add <player>" : "Friends: " + string.Join(", ", View.Friends.OrderByDescending(p => p.On).Select(p => p.On ? $"{p.Name} (online, {p.Where})" : $"{p.Name} (offline)")));
        return sb.ToString();
    }

    // ---- from Net
    public static void Got(string json) { var v = Net.FromJson<SocialView>(json); if (v == null) return; v.Friends ??= new(); v.In ??= new(); v.Blocked ??= new(); View = v; Ui.I?.RefreshFriends(); }
    public static void Note(string text) { if (text == "") return; Chat.System(text); Ui.Note(text, UiTheme.Highlight); }
    public static void OnJoined() { _t = 0; Tick(1f); }
    public static void Left() { View = new SocialView(); }
    /// <summary>In a friend's world: remember who we played with (every few seconds).</summary>
    public static void Tick(float dt)
    {
        _t -= dt; if (_t > 0) return; _t = 6f;
        if (!Net.Active || Game.Online || Net.I == null) return;
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds(); bool changed = false;
        foreach (var p in Net.I.Players.Values)
        {
            if (p.Id == Net.MyId || p.Name.Trim() == "") continue;
            var r = Local.Recent.Find(x => Low(x.Name) == Low(p.Name));
            if (r == null) { Local.Recent.Add(new Recent { Name = p.Name, Last = now }); changed = true; }
            else if (now - r.Last > 300) { r.Last = now; changed = true; }
        }
        int max = Math.Max(1, SocialCfg.I("friends.recentMax", 30));
        if (Local.Recent.Count > max) { Local.Recent = Local.Recent.OrderByDescending(r => r.Last).Take(max).ToList(); changed = true; }
        if (changed) SaveLocal();
    }
}

public partial class Ui
{
    static string _friendName = "";
    /// <summary>Rebuilds the Friends window if it is the one open (the server sent the lists again).</summary>
    public void RefreshFriends() { if (_panelName == "friends" && _panel != null) OpenFriends(); }

    /// <summary>The Friends window (pause menu > Friends, /friends): requests, friends with where they are, add by name, the block list.
    /// In a friend's hosted world: the players met lately instead.</summary>
    public void OpenFriends()
    {
        bool fresh = _panelName != "friends";
        var v = Frame("friends", "Friends", new Vector2(760, 620), OpenFriends);
        var net = Net.I; bool server = Friends.ServerSide;
        if (server && fresh) net!.Soc("list");
        void Line(Node parent, string text, Color col, params (string label, Action act, bool on)[] btns)
        {
            var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 8); CardBox(parent).AddChild(h);
            var l = Lbl(text, 15, col, h); l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.VerticalAlignment = VerticalAlignment.Center;
            foreach (var (label, act, on) in btns) { var b = Btn(label, act, true); b.Disabled = !on; h.AddChild(b); }
        }
        void Whisper(string name) { Close(); ChatBox.OpenWith("/w " + (name.Contains(' ') ? "\"" + name + "\"" : name) + " "); }
        if (!server)
        {
            Row(v, Net.Active ? "A friend's world has no accounts, so this is the list of players you met lately (kept on this computer). Friends lists with requests and online status are part of Play Online."
                              : "Friends lists are kept on your account on the online server (Play Online). Below: players you met lately in friends' worlds.", Dim, 14);
            Header("Recent players", v);
            var rec = Friends.Local.Recent.OrderByDescending(r => r.Last).ToList();
            if (rec.Count == 0) Empty(v, "Nobody yet. Host or join a friend to meet players.");
            foreach (var r in rec)
            {
                string name = r.Name; long peer = Net.Active ? Chat.FindExact(name) : 0; bool blocked = Friends.IsBlocked(name);
                string when = DateTimeOffset.FromUnixTimeSeconds(r.Last).ToLocalTime().ToString("yyyy-MM-dd");
                Line(v, name + (peer != 0 ? "   in this world" : "   last met " + when) + (blocked ? "   (blocked)" : ""), peer != 0 ? new Color(.55f, 1f, .6f) : Parch,
                    ("Whisper", () => Whisper(name), peer != 0), ("Invite", () => { net!.Invite(peer); Note("Invite sent to " + name, Gold); }, peer != 0 && !net!.SameParty(Net.MyId, peer)),
                    (blocked ? "Unblock" : "Block", () => { Note(blocked ? Friends.Unblock(name) : Friends.Block(name), Dim); OpenFriends(); }, true));
            }
            if (Friends.Local.Blocked.Count > 0)
            {
                Header("Blocked (also /ignore)", v);
                foreach (var b in Friends.Local.Blocked.ToList()) { string name = b; Line(v, name, Muted, ("Unblock", () => { Friends.Unblock(name); OpenFriends(); }, true)); }
            }
            return;
        }
        var view = Friends.View;
        // ---- add by name
        var add = new HBoxContainer(); add.AddThemeConstantOverride("separation", 8); v.AddChild(add);
        var edit = new LineEdit { Text = _friendName, PlaceholderText = "Hero name", MaxLength = 24, CustomMinimumSize = new Vector2(260, 34) }; add.AddChild(edit);
        edit.TextChanged += s => _friendName = s;
        void Send() { string n = _friendName.Trim(); if (n == "") return; _friendName = ""; Friends.Add(n); OpenFriends(); }
        edit.TextSubmitted += _ => Send(); add.AddChild(Btn("Add friend", Send, true));
        add.AddChild(Btn("Refresh", () => net!.Soc("list"), true));
        Lbl("They get a request to accept.", 13, Dim, add);
        // ---- requests
        if (view.In.Count > 0)
        {
            Header($"Friend requests ({view.In.Count})", v);
            foreach (var p in view.In) { string name = p.Name; Line(v, name + (p.On ? "   online" : ""), UiTheme.Highlight, ("Accept", () => Friends.Answer(name, true), true), ("Decline", () => Friends.Answer(name, false), true), ("Block", () => Friends.Block(name), true)); }
        }
        // ---- friends, online first
        Header($"Friends ({view.Friends.Count(p => p.On)} of {view.Friends.Count} online)", v);
        if (view.Friends.Count == 0) Empty(v, "No friends yet. Type a hero's name above, or click a name in the chat.");
        foreach (var p in view.Friends.OrderByDescending(p => p.On).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            string name = p.Name; long peer = p.On && net!.Players.ContainsKey(p.Peer) ? p.Peer : 0;
            Line(v, p.On ? $"{name}   Lv {p.Level}   {p.Where}" : $"{name}   offline", p.On ? new Color(.55f, 1f, .6f) : Dim,
                ("Whisper", () => Whisper(name), peer != 0), ("Invite", () => { net!.Invite(peer); Note("Invite sent to " + name, Gold); }, peer != 0 && !net!.SameParty(Net.MyId, peer)),
                ("Remove", () => Friends.Remove(name), true));
        }
        // ---- blocked
        Header($"Blocked ({view.Blocked.Count}) (also /ignore)", v);
        if (view.Blocked.Count == 0) Row(v, "Nobody. A blocked player's chat, whispers, invites, trade and friend requests do not reach you.", Dim, 14);
        foreach (var p in view.Blocked) { string name = p.Name; Line(v, name, Muted, ("Unblock", () => Friends.Unblock(name), true)); }
    }

    /// <summary>E on another player (Ui.OpenPlayerMenu): trade items and gold, whisper, add as a friend, block.</summary>
    void SocialButtons(Node v, long peer)
    {
        var net = Net.I; if (net == null || !net.Players.TryGetValue(peer, out var p)) return; string name = p.Name;
        var tr = Btn(Trade.Open ? "Already trading" : "Trade items and gold", () => { Close(); string r = Trade.Request(peer); if (r != "") Note(r, Gold); }); tr.Disabled = Trade.Open || PetTrade.Open; v.AddChild(tr);
        var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 8); v.AddChild(h);
        h.AddChild(Btn("Whisper", () => { Close(); ChatBox.OpenWith("/w " + (name.Contains(' ') ? "\"" + name + "\"" : name) + " "); }, true));
        if (Friends.ServerSide) { var fb = Btn(Friends.IsFriend(name) ? "Friend" : "Add friend", () => { Friends.Add(name); Close(); }, true); fb.Disabled = Friends.IsFriend(name); h.AddChild(fb); }
        bool blocked = Friends.IsBlocked(name);
        h.AddChild(Btn(blocked ? "Unblock" : "Block", () => { string r = blocked ? Friends.Unblock(name) : Friends.Block(name); if (r != "") Note(r, Dim); Close(); }, true));
    }
}
