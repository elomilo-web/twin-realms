using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TwinRealms;

/// <summary>
/// Debug command registry (focus F-02; the command bar of F-42 reuses it). Register(name, help, handler) and Run("stats dump").
/// A handler gets the words after the name and returns the text to show. Until the command bar exists: F9 runs "stats dump"
/// (CommandKey below), and the dev capture runs any line with the action "cmd:&lt;line&gt;" (scripts/dev/DevCapture.cs).
/// </summary>
public static class Commands
{
    static readonly Dictionary<string, (string help, Func<string[], string> run)> _c = new(StringComparer.OrdinalIgnoreCase);
    static bool _builtins;
    public static void Register(string name, string help, Func<string[], string> run) => _c[name.TrimStart('/')] = (help, run);
    public static IEnumerable<(string name, string help)> All { get { Builtins(); return _c.Select(kv => (kv.Key, kv.Value.help)); } }

    /// <summary>Runs one command line ("/stats dump" or "stats dump"); returns its output or an error line.</summary>
    public static string Run(string line)
    {
        Builtins();
        var w = (line ?? "").Trim().TrimStart('/').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (w.Length == 0) return "";
        if (!_c.TryGetValue(w[0], out var c)) return $"unknown command '{w[0]}' (try 'help')";
        try { return c.run(w.Skip(1).ToArray()); }
        catch (Exception e) { return $"{w[0]} failed: {e.Message}"; }
    }

    static void Builtins()
    {
        if (_builtins) return; _builtins = true;
        Register("help", "lists the commands", _ => string.Join("\n", _c.OrderBy(kv => kv.Key).Select(kv => $"/{kv.Key}: {kv.Value.help}")));
        Register("stats", "stats dump: every step of the hero's stat pipeline (also written to user://stats_dump.txt)", a =>
        {
            if (a.Length == 0 || !a[0].Equals("dump", StringComparison.OrdinalIgnoreCase)) return "usage: /stats dump";
            var text = StatPipeline.Dump(Game.D);
            using (var f = FileAccess.Open("user://stats_dump.txt", FileAccess.ModeFlags.Write)) f?.StoreString(text);
            return text;
        });
    }
}

/// <summary>Temporary key until the command bar (F-42): F9 prints "/stats dump" to the log and user://stats_dump.txt.</summary>
public partial class CommandKey : Node
{
    public override void _Input(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } k || k.Keycode != Key.F9 || !Ui.InGame) return;
        foreach (var l in Commands.Run("stats dump").Split('\n')) GD.Print("[STATS] " + l.TrimEnd('\r'));
        Ui.Note("Stats dump: log and user://stats_dump.txt");
    }
}
