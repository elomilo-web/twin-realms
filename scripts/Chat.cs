using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>Numbers and texts of chat, parties, friends, trading and party quests (data/social.json), by path: SocialCfg.F("chat.localRadius", 40).</summary>
public static class SocialCfg
{
    static JsonElement _root; static bool _loaded;
    static JsonElement Root { get { if (!_loaded) { _loaded = true; try { using var doc = JsonUtil.Load("res://data/social.json"); _root = doc.RootElement.Clone(); } catch (Exception e) { GD.PushError("social.json: " + e.Message); } } return _root; } }
    static JsonElement? At(string path)
    {
        var e = Root; foreach (var p in path.Split('.')) { if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(p, out e)) return null; }
        return e;
    }
    public static float F(string path, float d) => At(path) is { ValueKind: JsonValueKind.Number } e ? e.GetSingle() : d;
    public static int I(string path, int d) => (int)MathF.Round(F(path, d));
    public static bool B(string path, bool d) => At(path) is { } e ? e.ValueKind == JsonValueKind.True || (e.ValueKind != JsonValueKind.False && d) : d;
    public static string S(string path, string d) => At(path) is { ValueKind: JsonValueKind.String } e ? e.GetString() ?? d : d;
    public static List<string> List(string path)
    {
        var l = new List<string>(); if (At(path) is { ValueKind: JsonValueKind.Array } a) foreach (var x in a.EnumerateArray()) if (x.ValueKind == JsonValueKind.String) l.Add(x.GetString() ?? "");
        return l;
    }
    /// <summary>A text players see ("text.&lt;key&gt;"), with the built-in wording as the fallback.</summary>
    public static string T(string key, string d) => S("text." + key, d);
    public static Color C(string key, string d) { try { return Color.FromHtml(S("chat.colors." + key, d)); } catch { return Colors.White; } }
    public static string Hex(string key, string d) => S("chat.colors." + key, d);
}

/// <summary>Moderation logs on the server: one line per event, rotated by size (chat: TR_DATA/chatlog/chat.log, trades: TR_DATA/tradelog/trade.log).</summary>
public static class SocialLog
{
    static readonly object Gate = new();
    /// <summary>"" = the server's data folder (the online server) or user:// (a friend's host). The self-tests point it at a scratch folder.</summary>
    public static string Dir = "";
    static string Base() => Dir != "" ? Dir : Net.Dedicated ? Accounts.Dir : ProjectSettings.GlobalizePath("user://");
    public static string PathOf(string name) => Path.Combine(Base(), name + "log", name + ".log");
    public static void Write(string name, string line, int maxKb, int files)
    {
        try
        {
            lock (Gate)
            {
                string p = PathOf(name); Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                if (File.Exists(p) && new FileInfo(p).Length > Math.Max(1, maxKb) * 1024L)
                {
                    files = Math.Max(2, files);
                    for (int i = files - 1; i >= 1; i--) { string a = p + "." + i; if (!File.Exists(a)) continue; if (i >= files - 1) File.Delete(a); else File.Move(a, p + "." + (i + 1), true); }
                    File.Move(p, p + ".1", true);
                }
                File.AppendAllText(p, $"{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}Z\t{line}\n");
            }
        }
        catch (Exception e) { GD.PrintErr($"[{name.ToUpperInvariant()}] log not written: {e.Message}"); }
    }
}

/// <summary>
/// The server's rules for a chat message (focus A-31), with no networking in them so the self-test can run them: the text is cleaned
/// (control characters out, spaces collapsed, cut at chat.maxLen), a muted account is refused, a sender is rate limited (at most
/// rate.maxInWindow messages per rate.windowSec, rate.minGapSec apart, the same line not twice within rate.repeatSec) and, when
/// chat.profanity.enabled is set in data (off by default), listed words are masked. Net.HostChat (NetChat.cs) decides who receives it.
/// </summary>
public static class ChatServer
{
    public const int World = 0, Local = 1, Party = 2, Whisper = 3, Sys = 4;
    public static readonly string[] Names = { "World", "Local", "Party", "Whisper", "System" };
    sealed class Rate { public readonly Queue<double> Times = new(); public string Last = ""; public double LastAt = -1e9; }
    static readonly Dictionary<string, Rate> _rate = new();
    static List<string>? _words;
    public static int MaxLen => Math.Max(8, SocialCfg.I("chat.maxLen", 200));

    public static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        int max = MaxLen; var sb = new StringBuilder(Math.Min(text.Length, max + 8)); bool gap = false;
        foreach (char ch in text)
        {
            if (char.IsControl(ch) || char.IsWhiteSpace(ch) || ch == (char)0x200B || ch == (char)0x2028 || ch == (char)0x2029 || ch == (char)0xFEFF) { gap = sb.Length > 0; continue; }
            if (gap) { sb.Append(' '); gap = false; }
            sb.Append(ch); if (sb.Length >= max) break;
        }
        if (sb.Length > 0 && char.IsHighSurrogate(sb[^1])) sb.Length--;
        return sb.ToString().TrimEnd();
    }
    /// <summary>A hero name as the chat shows it: no control characters or line breaks, at most 24 characters.</summary>
    public static string CleanName(string? name)
    {
        var sb = new StringBuilder(); foreach (char ch in name ?? "") { if (char.IsControl(ch) || ch == '[' || ch == ']') continue; sb.Append(ch); if (sb.Length >= 24) break; }
        string s = sb.ToString().Trim(); return s == "" ? "Hero" : s;
    }
    /// <summary>The listed words replaced by the mask character (data/social.json chat.profanity).</summary>
    public static string Mask(string text)
    {
        _words ??= SocialCfg.List("chat.profanity.words").Where(w => w.Length > 1).ToList();
        if (_words.Count == 0 || text == "") return text;
        string mk = SocialCfg.S("chat.profanity.mask", "*"); char m = mk.Length > 0 ? mk[0] : '*'; var sb = new StringBuilder(text);
        foreach (var w in _words)
        {
            int at = 0;
            while (at < text.Length && (at = text.IndexOf(w, at, StringComparison.OrdinalIgnoreCase)) >= 0) { for (int i = 0; i < w.Length; i++) sb[at + i] = m; at += w.Length; }
        }
        return sb.ToString();
    }
    public static string MuteText(long until)
    {
        string u = "";
        if (until != long.MaxValue) { try { u = $" until {DateTimeOffset.FromUnixTimeSeconds(until):yyyy-MM-dd HH:mm} UTC"; } catch { } }
        return SocialCfg.T("muted", "You are muted{until} and cannot use the chat.").Replace("{until}", u);
    }

    /// <summary>One message of sender `id` at time `now` (seconds on any steady clock). ok: deliver `text`; otherwise `msg` goes back to the sender ("" = say nothing).</summary>
    public static (bool ok, string text, string msg) Check(string id, string? raw, long muteUntil, double now)
    {
        if (muteUntil > DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return (false, "", MuteText(muteUntil));
        string text = Clean(raw); if (text == "") return (false, "", "");
        if (!_rate.TryGetValue(id, out var r)) _rate[id] = r = new Rate();
        double win = SocialCfg.F("chat.rate.windowSec", 8), gap = SocialCfg.F("chat.rate.minGapSec", .4f), rep = SocialCfg.F("chat.rate.repeatSec", 4); int max = Math.Max(1, SocialCfg.I("chat.rate.maxInWindow", 6));
        while (r.Times.Count > 0 && now - r.Times.Peek() > win) r.Times.Dequeue();
        if (r.Times.Count >= max || now - r.LastAt < gap) return (false, "", SocialCfg.T("tooFast", "You are sending messages too fast. Wait a moment."));
        if (text == r.Last && now - r.LastAt < rep) return (false, "", SocialCfg.T("repeat", "You just said that."));
        r.Times.Enqueue(now); r.Last = text; r.LastAt = now;
        if (SocialCfg.B("chat.profanity.enabled", false)) text = Mask(text);
        if (_rate.Count > 4096) foreach (var k in _rate.Where(kv => now - kv.Value.LastAt > 600).Select(kv => kv.Key).ToList()) _rate.Remove(k);
        return (true, text, "");
    }
    public static void Forget(string id) => _rate.Remove(id);
    public static void Log(int ch, string who, string acct, string to, string text)
    {
        if (ch == Whisper && !SocialCfg.B("chat.logWhispers", true)) return;
        SocialLog.Write("chat", $"{Names[Math.Clamp(ch, 0, 4)].ToUpperInvariant()}\t{who} [{(acct.Length > 8 ? acct[..8] : acct)}]\t{(to != "" ? "-> " + to : "")}\t{text}", SocialCfg.I("chat.logMaxKb", 1024), SocialCfg.I("chat.logFiles", 5));
    }
}

public class ChatLine { public int Ch; public long Peer; public string From = "", To = "", Text = ""; public double At; public string Link = ""; }

/// <summary>
/// The player's side of the chat (focus A-31): the lines received (World, Local, Party, Whisper, System), the channel typed into, the
/// preferences (user://chat.json: timestamps, own profanity mask, channel) and the commands /g /l /p /w /r /ignore /chat /who /invite
/// /party /friend /trade /quest, which everyone may use (they run on this game and send ordinary requests; the host checks each one).
/// The box itself is ChatBox (UiChat.cs). Messages go to the host with Net.ChatSay; names always come back from the host.
/// </summary>
public static class Chat
{
    public static readonly List<ChatLine> Lines = new(); public static int Rev; public static double LastAt = -1e9;
    public static int Channel = ChatServer.World, Tab = -1;   // Tab: -1 everything, otherwise one channel
    public static bool Stamps, Filter;
    public static long ReplyPeer; public static string ReplyName = "";
    const string PrefPath = "user://chat.json"; static bool _prefs, _registered;
    class Prefs { public bool Stamps, Filter; public int Channel; }
    static readonly JsonSerializerOptions Opt = new() { IncludeFields = true };

    public static void LoadPrefs()
    {
        if (_prefs) return; _prefs = true;
        try { using var f = Godot.FileAccess.Open(PrefPath, Godot.FileAccess.ModeFlags.Read); if (f == null) return; var p = JsonSerializer.Deserialize<Prefs>(f.GetAsText(), Opt); if (p == null) return; Stamps = p.Stamps; Filter = p.Filter; Channel = Math.Clamp(p.Channel, 0, 2); }
        catch { }
    }
    public static void SavePrefs() { try { using var f = Godot.FileAccess.Open(PrefPath, Godot.FileAccess.ModeFlags.Write); f?.StoreString(JsonSerializer.Serialize(new Prefs { Stamps = Stamps, Filter = Filter, Channel = Channel }, Opt)); } catch { } }

    static void Add(ChatLine l)
    {
        l.At = Time.GetUnixTimeFromSystem(); Lines.Add(l); int max = Math.Max(20, SocialCfg.I("chat.scrollback", 300));
        if (Lines.Count > max) Lines.RemoveRange(0, Lines.Count - max);
        Rev++; LastAt = Time.GetTicksMsec() / 1000.0;
    }
    /// <summary>A line from the game itself (answers of chat commands, friend and trade notes). link: what clicking it does ("" = nothing).</summary>
    public static void System(string text, string link = "")
    {
        if (string.IsNullOrEmpty(text)) return;
        foreach (var s in text.Split('\n')) if (s.Trim() != "") Add(new ChatLine { Ch = ChatServer.Sys, Text = s.TrimEnd('\r'), Link = link });
    }
    /// <summary>A message the host delivered. `to` is set on the echo of our own whisper (the name it went to).</summary>
    public static void Receive(int ch, long from, string name, string text, string to)
    {
        if (ch < 0 || ch > ChatServer.Whisper) return;
        if (from != Net.MyId && Friends.IsBlocked(name)) return;   // our own list as well (in a friend's world the host only knows it for this session)
        if (Filter) text = ChatServer.Mask(text);
        Add(new ChatLine { Ch = ch, Peer = from, From = name, To = to, Text = text });
        if (ch == ChatServer.Whisper && from != Net.MyId) { ReplyPeer = from; ReplyName = name; Sfx.Play("click"); }
        if (ch == ChatServer.Local) ChatBubble.Say(from, text);
    }
    public static void Clear() { Lines.Clear(); Rev++; }

    // ------------------------------------------------------------------ sending
    static string Offline => SocialCfg.T("offline", "Chat needs a shared world: Play Online, or host or join a friend.");
    /// <summary>A line typed into the CHAT box: "/..." is a command (never the admin login, which belongs to the command bar), anything else is said on the current channel.</summary>
    public static void Submit(string line)
    {
        line = (line ?? "").Trim(); if (line == "") return;
        if (line.StartsWith('/'))
        {
            var w = Commands.Words(line); if (w.Length == 0) return;
            if (Commands.Find(w[0]) == null) { System(Commands.Unknown(w[0], CmdCtx.Local())); return; }
            Commands.Submit(line, s => System(s)); return;
        }
        Say(Channel, line);
    }
    public static string Say(int ch, string text)
    {
        if (text.Trim() == "") return $"usage: say something after the command";
        if (!Net.Active) return Offline;
        Net.I!.ChatSay(ch, 0, text); return "";
    }
    public static string WhisperTo(long peer, string text)
    {
        if (!Net.Active) return Offline;
        if (peer == 0 || !Net.I!.Players.ContainsKey(peer)) return SocialCfg.T("notHere", "That player is not online.");
        if (peer == Net.MyId) return "You mutter to yourself.";
        if (text.Trim() == "") return "usage: /w <player> <text>";
        Net.I.ChatSay(ChatServer.Whisper, peer, text); return "";
    }

    // ------------------------------------------------------------------ finding players by name
    static string Norm(string s) => s.Replace('_', ' ').Trim().ToLowerInvariant();
    public static IEnumerable<string> PlayerNames() => Net.Active ? Net.I!.Players.Values.Where(p => p.Id != Net.MyId).Select(p => p.Name.Replace(' ', '_')) : Array.Empty<string>();
    public static long FindExact(string name)
    {
        if (!Net.Active) return 0; string n = Norm(name);
        foreach (var p in Net.I!.Players.Values) if (p.Id != Net.MyId && Norm(p.Name) == n) return p.Id;
        return 0;
    }
    /// <summary>The player with this name, or the only one whose name starts so; 0 if none (or several).</summary>
    public static long FindPlayer(string name)
    {
        long id = FindExact(name); if (id != 0 || !Net.Active) return id; string n = Norm(name); if (n == "") return 0;
        var l = Net.I!.Players.Values.Where(p => p.Id != Net.MyId && Norm(p.Name).StartsWith(n)).ToList();
        return l.Count == 1 ? l[0].Id : 0;
    }
    /// <summary>The words of "/w Two Words hello": the longest run of leading words that is a player's name (or "quoted name"), and the rest.</summary>
    public static (long peer, string name, string rest) Target(string[] a)
    {
        if (a.Length == 0) return (0, "", "");
        if (a[0].StartsWith('"'))
        {
            int end = Array.FindIndex(a, s => s.EndsWith('"') && s.Length > (s == a[0] ? 1 : 0));
            if (end >= 0) { string n = string.Join(' ', a.Take(end + 1)).Trim('"'); return (FindPlayer(n), n, string.Join(' ', a.Skip(end + 1))); }
        }
        for (int k = Math.Min(a.Length, 4); k >= 1; k--) { string n = string.Join(' ', a.Take(k)); long p = FindExact(n); if (p != 0) return (p, Net.I!.NameOf(p), string.Join(' ', a.Skip(k))); }
        long q = FindPlayer(a[0]); return (q, q != 0 ? Net.I!.NameOf(q) : a[0].Replace('_', ' '), string.Join(' ', a.Skip(1)));
    }

    // ------------------------------------------------------------------ commands (everyone)
    static void Cmd(string name, string usage, string help, Func<string[], string> run, bool players = false, params string[] first)
    {
        Commands.Add(new Cmd
        {
            Name = name, Usage = usage, Help = help, Perm = CmdPerm.Everyone, Run = (_, a) => run(a),
            Complete = (i, _) => i == 0 ? (first.Length > 0 ? first : players ? PlayerNames() : Array.Empty<string>()) : i == 1 && first.Length > 0 ? PlayerNames() : Array.Empty<string>(),
        });
    }
    public static void Register()
    {
        if (_registered) return; _registered = true; LoadPrefs();
        foreach (var n in new[] { "g", "world" }) Cmd(n, "/" + n + " <text>", "World chat: everyone on the server", a => { Channel = ChatServer.World; return a.Length == 0 ? "Now talking in World." : Say(ChatServer.World, string.Join(' ', a)); });
        foreach (var n in new[] { "l", "say" }) Cmd(n, "/" + n + " <text>", $"Local chat: players within {SocialCfg.I("chat.localRadius", 40)} m (also shown above your head)", a => { Channel = ChatServer.Local; return a.Length == 0 ? "Now talking in Local." : Say(ChatServer.Local, string.Join(' ', a)); });
        Cmd("p", "/p <text>", "Party chat: your party", a => { Channel = ChatServer.Party; return a.Length == 0 ? "Now talking in Party." : Say(ChatServer.Party, string.Join(' ', a)); });
        foreach (var n in new[] { "w", "whisper", "tell" }) Cmd(n, "/" + n + " <player> <text>", "Whisper: only that player reads it. /r answers the last whisper", a =>
        {
            if (a.Length < 2) return "usage: /w <player> <text>";
            var (peer, name, rest) = Target(a); if (peer == 0) return $"No player named {name} is online.";
            return WhisperTo(peer, rest);
        }, true);
        Cmd("r", "/r <text>", "Reply to the last whisper you received", a => ReplyPeer == 0 ? "Nobody has whispered to you yet." : a.Length == 0 ? "usage: /r <text>" : WhisperTo(ReplyPeer, string.Join(' ', a)));
        Cmd("who", "/who", "Lists the players in this world", _ =>
        {
            if (!Net.Active) return Offline; var net = Net.I!;
            return $"{net.Players.Count} online: " + string.Join(", ", net.Players.Values.OrderBy(p => p.Name).Select(p => $"{p.Name} (Lv {p.Level}{(net.SameParty(Net.MyId, p.Id) && p.Id != Net.MyId ? ", party" : "")})"));
        });
        Cmd("ignore", "/ignore [player]", "Blocks a player: no chat, whispers, invites, trade or friend requests from them. No name: lists who you block. The same list as Friends > Blocked", a => a.Length == 0 ? Friends.BlockedText() : Friends.Block(string.Join(' ', a).Replace('_', ' ')), true);
        Cmd("unignore", "/unignore <player>", "Takes a player off your block list", a => a.Length == 0 ? "usage: /unignore <player>" : Friends.Unblock(string.Join(' ', a).Replace('_', ' ')));
        Cmd("chat", "/chat time|filter on|off, /chat clear", "Chat options: timestamps, your own profanity mask (off by default), clear the box", a =>
        {
            if (a.Length == 0) return $"timestamps {(Stamps ? "on" : "off")}, profanity mask {(Filter ? "on" : "off")}. usage: /chat time|filter on|off, /chat clear";
            bool on = a.Length > 1 && a[1].Equals("on", StringComparison.OrdinalIgnoreCase);
            switch (a[0].ToLowerInvariant())
            {
                case "clear": Clear(); return "";
                case "time": if (a.Length < 2) on = !Stamps; Stamps = on; SavePrefs(); Rev++; return "Timestamps " + (on ? "on" : "off");
                case "filter": if (a.Length < 2) on = !Filter; Filter = on; SavePrefs(); return "Profanity mask " + (on ? "on (new messages)" : "off");
            }
            return "usage: /chat time|filter on|off, /chat clear";
        }, false, "time", "filter", "clear");
        Cmd("invite", "/invite <player>", "Invites a player to your party", a =>
        {
            if (!Net.Active) return Offline; if (a.Length == 0) return "usage: /invite <player>";
            var (peer, name, _) = Target(a); if (peer == 0) return $"No player named {name} is online.";
            Net.I!.Invite(peer); return "Party invite sent to " + name;
        }, true);
        Cmd("party", "/party [leave | kick <player> | leader <player>]", $"Your party (up to {Net.PartyMax}): who is in it; leave it; the leader can remove a member or hand over the lead", a =>
        {
            if (!Net.Active) return Offline; var net = Net.I!; var pt = net.MyParty;
            if (a.Length == 0) return pt == null ? "You are not in a party. /invite <player> starts one." : "Party: " + string.Join(", ", pt.Members.Select(m => net.NameOf(m) + (m == pt.Leader ? " (leader)" : "")));
            if (pt == null) return SocialCfg.T("noParty", "You are not in a party.");
            string op = a[0].ToLowerInvariant();
            if (op == "leave") { net.LeaveParty(); return "You left the party."; }
            if (op is "kick" or "leader")
            {
                if (pt.Leader != Net.MyId) return "Only the party leader can do that.";
                var (peer, name, _) = Target(a.Skip(1).ToArray()); if (peer == 0 || !pt.Members.Contains(peer)) return $"{name} is not in your party.";
                if (op == "kick") net.PartyKick(peer); else net.PartyLead(peer);
                return "";
            }
            return "usage: /party [leave | kick <player> | leader <player>]";
        }, false, "leave", "kick", "leader");
        Cmd("friend", "/friend add|remove|accept|decline <player>, /friend list", "Friends list (A-32): kept on your account on the online server; in a friend's world a list of recent players", a =>
        {
            if (a.Length == 0 || a[0].Equals("list", StringComparison.OrdinalIgnoreCase)) return Friends.ListText();
            if (a.Length < 2) return "usage: /friend add|remove|accept|decline <player>";
            string name = string.Join(' ', a.Skip(1)).Replace('_', ' ').Trim('"');
            return a[0].ToLowerInvariant() switch { "add" => Friends.Add(name), "remove" => Friends.Remove(name), "accept" => Friends.Answer(name, true), "decline" => Friends.Answer(name, false), _ => "usage: /friend add|remove|accept|decline <player>, /friend list" };
        }, false, "add", "remove", "accept", "decline", "list");
        Cmd("friends", "/friends", "Opens the Friends window", _ => { Callable.From(() => Ui.I?.OpenFriends()).CallDeferred(); return ""; });
        Cmd("trade", "/trade <player>", $"Asks a player within {SocialCfg.I("trade.radius", 12)} m to trade items and gold", a =>
        {
            if (!Net.Active) return Offline; if (a.Length == 0) return "usage: /trade <player>";
            var (peer, name, _) = Target(a); if (peer == 0) return $"No player named {name} is online.";
            return Trade.Request(peer);
        }, true);
        Cmd("quest", "/quest accept [n], /quest offers", "Quests your party shared with you (A-29): take one, or list them", a => PartyQuests.Command(a), false, "accept", "offers");
        Commands.Register("chattest", "A-31: the server's chat rules on fake senders (clean, length cap, rate limit, repeat, mute, profanity mask, log rotation, name parsing): PASS / FAIL", _ => SocialTests.Chat());
        Commands.Register("friendtest", "A-32: the server's friends list on two fake accounts (request, accept, decline, remove, block): PASS / FAIL", _ => SocialTests.Friends());
        Commands.Register("tradetest", "A-30: the server-run item / gold / pet swap on two fake accounts (offer check, pack space, swap, late save, no duplicates): PASS / FAIL", _ => SocialTests.Trade());
        Commands.Register("partytest", "A-29: party quest sharing (who the server relays to, event checks, progress / completion applied to this hero and put back): PASS / FAIL", _ => SocialTests.Party());
    }
}
