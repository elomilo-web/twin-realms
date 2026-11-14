using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>
/// "--perf": a performance probe. Builds the world with default settings (like a fresh download), stands the hero in Oakhaven
/// and measures where the frame time goes: script (process) and physics time with everything on, then with one system at a
/// time switched off, so the cost of each shows as the difference. Writes nothing to your saves.
/// </summary>
public partial class Perf : Node
{
    public static bool Wanted() => System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--perf") >= 0;
    float _t; int _phase = -1; double _proc, _phys; int _n;
    static readonly string[] Phases = { "baseline", "no enemies", "no gatherables", "no hud", "no world tick", "no flora tick", "no ambient", "no lights flicker", "no player", "baseline" };
    readonly List<Node> _off = new();

    public override void _Process(double delta)
    {
        _t += (float)delta;
        if (_phase >= 0 && _t > 1.5f) { _proc += Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000; _phys += Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000; _n++; }
        if (_t < (_phase < 0 ? 8f : 6f)) return;
        if (_phase >= 0)
            GD.Print($"[PERF] {Phases[_phase],-18} process {_proc / _n:0.00} ms   physics {_phys / _n:0.00} ms   fps {Performance.GetMonitor(Performance.Monitor.TimeFps):0}");
        foreach (var n in _off) if (IsInstanceValid(n)) { n.SetProcess(true); n.SetPhysicsProcess(true); if (n is CanvasItem ci) ci.Visible = true; }
        _off.Clear(); _t = 0; _proc = _phys = 0; _n = 0; _phase++;
        if (_phase >= Phases.Length) { GD.Print($"[PERF] nodes {Performance.GetMonitor(Performance.Monitor.ObjectNodeCount):0}, draws {Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame):0}"); GetTree().Quit(); return; }
        void Off(Node n) { n.SetProcess(false); n.SetPhysicsProcess(false); _off.Add(n); }
        void Walk(Node n, System.Func<Node, bool> f) { if (f(n)) Off(n); foreach (var c in n.GetChildren()) Walk(c, f); }
        var root = GetTree().Root;
        switch (Phases[_phase])
        {
            case "no enemies": Walk(root, n => n is Enemy); break;
            case "no gatherables": Walk(root, n => n is Gatherable); break;
            case "no hud": Walk(root, n => n is HudCanvas || n is Hud); break;
            case "no world tick": Walk(root, n => n is World); break;
            case "no flora tick": Walk(root, n => n is FloraField); break;
            case "no ambient": Walk(root, n => n is Ambient); break;
            case "no lights flicker": Walk(root, n => n is Torch || n is RealmGate || n is Spinner); break;
            case "no player": Walk(root, n => n is Player || n is CameraRig); break;
        }
    }
}
