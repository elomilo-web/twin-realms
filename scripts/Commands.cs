using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TwinRealms;

/// <summary>Who may use a command. Dev: the debug commands systems register with Commands.Register (admins on their own game, and dev captures).</summary>
public enum CmdPerm { Everyone, Admin, Dev }

/// <summary>Who runs a command and where: the local player, or (on a host / the online server) the player who sent it.</summary>
public sealed class CmdCtx
{
    public long Peer = 1; public string Account = "", Name = "Hero";
    /// <summary>The admin flag as the machine running the command knows it (on the online server: the flag on the sender's account).</summary>
    public bool IsAdmin;
    /// <summary>Commands.Run (dev captures, code): no permission check.</summary>
    public bool Trusted;
    /// <summary>A request from another machine (the server runs it for that player).</summary>
    public bool Remote;
    public Vector3? Pos; public Vector3I? Aim;
    /// <summary>The player on this machine.</summary>
    public static CmdCtx Local(bool trusted = false)
    {
        var c = new CmdCtx { Peer = Net.MyId, Name = Game.D.Name, IsAdmin = Admin.IsAdmin || DevCapture.Wanted(), Trusted = trusted };
        if (Player.Instance is { } p && GodotObject.IsInstanceValid(p)) { c.Pos = p.GlobalPosition; c.Aim = Admin.AimCell(); }
        return c;
    }
}

/// <summary>One command: name, usage line, help, who may use it, where it runs, the handler and (optionally) what its arguments can be.</summary>
public sealed class Cmd
{
    public string Name = "", Usage = "", Help = ""; public CmdPerm Perm = CmdPerm.Admin;
    /// <summary>True: it changes the shared world or other players, so a client sends it to the host / online server, which checks its own admin flag.</summary>
    public bool Server;
    public Func<CmdCtx, string[], string> Run = (_, _) => "";
    /// <summary>Choices for argument number i (0 = first word after the name), given the words typed so far.</summary>
    public Func<int, string[], IEnumerable<string>>? Complete;
}

/// <summary>
/// The one command registry (focus F-02, F-42). A command is one entry: Commands.Add(new Cmd { ... }) (scripts/AdminCommands.cs holds the
/// admin set); debug commands of other systems use the short form Commands.Register(name, help, handler) and are dev-only.
/// Three ways in: Commands.Run(line) (trusted: dev captures "cmd:&lt;line&gt;", code), Commands.Submit(line, print) (the command bar:
/// permission check, and server commands go to the host when we are a client), Commands.Exec(line, ctx) (the host / online server
/// running a player's request after its own admin check).
/// </summary>
public static class Commands
{
    static readonly Dictionary<string, Cmd> _c = new(StringComparer.OrdinalIgnoreCase);
    static bool _builtins;
    public const string NotAdmin = "you are not an admin";

    /// <summary>Debug command of a system (dev-only, runs on this machine). The handler gets the words after the name and returns the text to show.</summary>
    public static void Register(string name, string help, Func<string[], string> run)
    {
        name = name.TrimStart('/');
        _c[name] = new Cmd { Name = name, Usage = "/" + name + " ...", Help = help, Perm = CmdPerm.Dev, Run = (_, a) => run(a) };
    }
    public static void Add(Cmd c) { c.Name = c.Name.TrimStart('/'); if (c.Usage == "") c.Usage = "/" + c.Name; _c[c.Name] = c; }
    public static Cmd? Find(string name) { Builtins(); return _c.TryGetValue(name.TrimStart('/'), out var c) ? c : null; }
    public static IEnumerable<(string name, string help)> All { get { Builtins(); return _c.Select(kv => (kv.Key, kv.Value.Help)); } }
    public static IEnumerable<Cmd> Entries { get { Builtins(); return _c.Values.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase); } }

    public static string[] Words(string? line) => (line ?? "").Trim().TrimStart('/').Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>May this player use the command? Dev commands never run for another machine.</summary>
    public static bool Allowed(Cmd c, CmdCtx x) => c.Perm == CmdPerm.Everyone || x.Trusted || (x.IsAdmin && (c.Perm == CmdPerm.Admin || !x.Remote));

    /// <summary>Runs one command line as the local player with no permission check ("/stats dump" or "stats dump"); returns its output or an error line.</summary>
    public static string Run(string line) => Exec(line, CmdCtx.Local(true));

    /// <summary>Runs one line for this player on this machine after the permission check; returns the output or an error line.</summary>
    public static string Exec(string line, CmdCtx x)
    {
        Builtins();
        var w = Words(line);
        if (w.Length == 0) return "";
        if (!_c.TryGetValue(w[0], out var c)) return Unknown(w[0], x);
        if (!Allowed(c, x)) return NotAdmin;
        if (x.Remote && !c.Server) return $"/{c.Name} runs on your own game, not on the server";
        try { return c.Run(x, w.Skip(1).ToArray()); }
        catch (Exception e) { return $"/{c.Name} failed: {e.Message}"; }
    }

    /// <summary>
    /// The command bar's way in. Local commands print at once; as a client in someone's world the server commands are sent to the host,
    /// whose answer is printed when it arrives (CommandBar.Print). Returns false if the first word is not a command (the bar then tries the admin login).
    /// </summary>
    public static bool Submit(string line, Action<string> print)
    {
        Builtins();
        var w = Words(line); if (w.Length == 0) return true;
        if (!_c.TryGetValue(w[0], out var c)) return false;
        var x = CmdCtx.Local();
        if (!Allowed(c, x)) { print(NotAdmin); return true; }
        if (c.Perm == CmdPerm.Dev && Net.IsClient && !DevCapture.Wanted()) { print($"/{c.Name} is a debug command: it only works in your own world"); return true; }
        if (c.Server && Net.IsClient) { Net.I!.SendCommand(line, x.Aim); return true; }   // the host checks its own admin flag
        print(Exec(line, x));
        return true;
    }

    /// <summary>The error for a first word that is no command (only commands this player may use are suggested).</summary>
    public static string Unknown(string word, CmdCtx x)
    {
        Builtins();
        string near = Near(word, _c.Values.Where(c => Allowed(c, x)).Select(c => c.Name));
        return $"unknown command '/{word}'" + (near != "" ? $"; did you mean /{near}?" : "") + " (try /help)";
    }

    /// <summary>The closest of the options to a mistyped word ("" if nothing is close): a prefix either way, or at most two letters different.</summary>
    public static string Near(string word, IEnumerable<string> options)
    {
        string best = ""; int bd = 3; word = word.ToLowerInvariant();
        foreach (var o in options)
        {
            string l = o.ToLowerInvariant(); if (l == word) return o;
            int d = l.StartsWith(word) || word.StartsWith(l) ? 1 : Distance(word, l);
            if (d < bd) { bd = d; best = o; }
        }
        return best;
    }
    static int Distance(string a, string b)
    {
        if (Math.Abs(a.Length - b.Length) > 2) return 9;
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++) for (int j = 1; j <= b.Length; j++)
            d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }
    /// <summary>"no mob type 'x'; did you mean y?" for a wrong argument.</summary>
    public static string NoSuch(string what, string word, IEnumerable<string> options)
    {
        string near = Near(word, options);
        return $"no {what} '{word}'" + (near != "" ? $"; did you mean {near}?" : "");
    }

    /// <summary>
    /// Autocomplete for the text typed so far: whole lines to put in the bar (first) and what to show in the list (second).
    /// Before the first space: the commands this player may use, by prefix. After it: the command's argument choices, by prefix.
    /// </summary>
    public static List<(string line, string label)> Suggest(string text, int max = 8)
    {
        Builtins(); var res = new List<(string, string)>(); var x = CmdCtx.Local();
        string t = text.TrimStart(); bool slash = t.StartsWith('/'); t = t.TrimStart('/');
        int sp = t.IndexOf(' ');
        if (sp < 0)
        {
            foreach (var c in Entries)
                if (c.Name.StartsWith(t, StringComparison.OrdinalIgnoreCase) && Allowed(c, x)) { res.Add(("/" + c.Name + " ", c.Usage + "   " + Short(c.Help))); if (res.Count >= max) break; }
            return res;
        }
        var cmd = Find(t[..sp]); if (cmd == null || !Allowed(cmd, x)) return res;
        if (cmd.Complete == null) { res.Add((text, cmd.Usage)); return res; }
        string rest = t[(sp + 1)..]; var done = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        string cur = ""; if (!rest.EndsWith(' ') && done.Count > 0) { cur = done[^1]; done.RemoveAt(done.Count - 1); }
        string head = (slash ? "/" : "") + cmd.Name + " " + string.Concat(done.Select(d => d + " "));
        IEnumerable<string> opts; try { opts = cmd.Complete(done.Count, done.ToArray()).ToList(); } catch { opts = Array.Empty<string>(); }
        foreach (var o in opts.Where(o => o.StartsWith(cur, StringComparison.OrdinalIgnoreCase)).Distinct().OrderBy(o => o, StringComparer.OrdinalIgnoreCase))
        { res.Add((head + o + " ", o)); if (res.Count >= max) break; }
        if (res.Count == 0) res.Add((text, cmd.Usage));
        return res;
    }
    static string Short(string s) { int nl = s.IndexOf('\n'); if (nl >= 0) s = s[..nl]; return s.Length > 70 ? s[..67] + "..." : s; }

    static void Builtins()
    {
        if (_builtins) return; _builtins = true;
        Add(new Cmd
        {
            Name = "help", Usage = "/help [command]", Help = "lists the commands you may use; /help <command> shows how to use one", Perm = CmdPerm.Everyone,
            Complete = (i, _) => i == 0 ? Entries.Select(c => c.Name) : Array.Empty<string>(),
            Run = (x, a) =>
            {
                if (a.Length > 0)
                {
                    var c = Find(a[0]); if (c == null) return Unknown(a[0].TrimStart('/'), x);
                    return Allowed(c, x) ? $"usage: {c.Usage}\n{c.Help}" : NotAdmin;
                }
                var mine = Entries.Where(c => Allowed(c, x)).ToList();
                string head = x.IsAdmin || x.Trusted ? "" : "Commands are for admins. You may use:\n";
                return head + string.Join("\n", mine.Select(c => $"{c.Usage}: {Short(c.Help)}"));
            }
        });
        Add(new Cmd
        {
            Name = "stats", Usage = "/stats dump", Help = "every step of the hero's stat pipeline (also written to user://stats_dump.txt)", Perm = CmdPerm.Admin,
            Complete = (i, _) => i == 0 ? new[] { "dump" } : Array.Empty<string>(),
            Run = (_, a) =>
            {
                if (a.Length == 0 || !a[0].Equals("dump", StringComparison.OrdinalIgnoreCase)) return "usage: /stats dump";
                var text = StatPipeline.Dump(Game.D);
                using (var f = FileAccess.Open("user://stats_dump.txt", FileAccess.ModeFlags.Write)) f?.StoreString(text);
                return text;
            }
        });
        AdminCommands.RegisterAll();
    }
}

/// <summary>Added by Main for every game window: the command bar (/ or ~, scripts/CommandBar.cs) and F9 = "/stats dump" to the log.</summary>
public partial class CommandKey : Node
{
    public override void _Ready() { Admin.Load(); AddChild(new CommandBar { Name = "CommandBar" }); }
    public override void _Input(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } k || k.Keycode != Key.F9 || !Ui.InGame || CommandBar.IsOpen) return;
        if (!Admin.IsAdmin && !DevCapture.Wanted()) return;   // F-43: commands are for admins
        foreach (var l in Commands.Run("stats dump").Split('\n')) GD.Print("[STATS] " + l.TrimEnd('\r'));
        Ui.Note("Stats dump: log and user://stats_dump.txt");
    }
}
