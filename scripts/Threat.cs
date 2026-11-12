using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>
/// The longer you play in one sitting, the harder the overworld pushes back (data/progression.json "threat"):
/// one threat level every few minutes makes overworld monsters tougher and respawn sooner, and ambush packs appear around you
/// and hunt you. Ambushers belong to this machine only (in multiplayer each hero draws their own). Ticked from Main.
/// </summary>
public static class Threat
{
    static bool _loaded; static float _minPer = 8, _hpP = 8, _dmgP = 5, _resP = 7, _int = 90, _intPer = 6, _intMin = 30, _dist = 38, _safe = 70;
    static int _max = 10, _packBase = 2, _packPer = 1, _aliveBase = 6, _alivePer = 4;
    static readonly Dictionary<int, string[]> _pools = new();
    static float _t, _spawnT = 60f; static int _shown;
    static readonly List<Enemy> _live = new();

    public static int Level { get { Load(); return Math.Min(_max, (int)(_t / 60f / Math.Max(.1f, _minPer))); } }
    public static float HpP { get { Load(); return _hpP; } }
    public static float DmgP { get { Load(); return _dmgP; } }
    public static float RespawnMul => Mathf.Max(.3f, 1f - Level * _resP / 100f);
    public static float Minutes => _t / 60f;

    static void Load()
    {
        if (_loaded) return; _loaded = true;
        var r = JsonUtil.Load("res://data/progression.json").RootElement; if (!r.TryGetProperty("threat", out var t)) return;
        _minPer = JsonUtil.F(t, "minutesPerLevel", _minPer); _max = (int)JsonUtil.F(t, "max", _max); _hpP = JsonUtil.F(t, "hpP", _hpP); _dmgP = JsonUtil.F(t, "dmgP", _dmgP); _resP = JsonUtil.F(t, "respawnP", _resP);
        _int = JsonUtil.F(t, "interval", _int); _intPer = JsonUtil.F(t, "intervalPerLevel", _intPer); _intMin = JsonUtil.F(t, "minInterval", _intMin);
        _packBase = (int)JsonUtil.F(t, "packBase", _packBase); _packPer = (int)JsonUtil.F(t, "packPerLevel", _packPer); _aliveBase = (int)JsonUtil.F(t, "aliveBase", _aliveBase); _alivePer = (int)JsonUtil.F(t, "alivePerLevel", _alivePer);
        _dist = JsonUtil.F(t, "ambushDist", _dist); _safe = JsonUtil.F(t, "safeRadius", _safe);
        if (t.TryGetProperty("pools", out var p)) foreach (var o in p.EnumerateObject()) { var l = new List<string>(); foreach (var x in o.Value.EnumerateArray()) l.Add(x.GetString() ?? "wolf"); _pools[int.Parse(o.Name)] = l.ToArray(); }
    }

    /// <summary>A new session (scene start): calm again.</summary>
    public static void Reset() { _t = 0; _spawnT = 60f; _shown = 0; _live.Clear(); }

    public static void Tick(float dt)
    {
        Load(); var p = Player.Instance; if (p == null || World.I == null) return;
        if (Dungeon.Active || Arenas.Active || p.Dead) return;   // only time out in the world counts
        _t += dt;
        int lv = Level;
        if (lv > _shown) { _shown = lv; Ui.Note($"The land grows restless (threat {lv}): monsters are stronger and roam in greater numbers", new Color(1f, .55f, .4f)); }
        _live.RemoveAll(e => !GodotObject.IsInstanceValid(e) || e.Dead);
        _spawnT -= dt; if (_spawnT > 0) return;
        _spawnT = Mathf.Max(_intMin, _int - _intPer * lv);
        if (lv <= 0 || Bot.Wanted() || Observer.Wanted() || Shots.Wanted() || MpBot.Wanted() != null) return;   // the test bots need a calm world
        var at = p.GlobalPosition; if (new Vector2(at.X, at.Z).Length() < _safe) return;   // Oakhaven stays safe
        if (_live.Count >= _aliveBase + _alivePer * lv) return;
        Ambush(lv, at);
    }

    static void Ambush(int lv, Vector3 at)
    {
        var over = World.I!.Over;
        if (!_pools.TryGetValue(World.Realm, out var pool) || pool.Length == 0) return;
        int n = _packBase + _packPer * lv; float a0 = GD.Randf() * Mathf.Tau;
        var c = at + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * _dist;
        if (Mathf.Abs(c.X) > World.Half - 20 || Mathf.Abs(c.Z) > World.Half - 20) c = at - (c - at);   // turn back from the island's edge
        int lvl = Progression.OverworldLevel(Game.LevelAt(c.X, c.Z, World.Realm), false);
        for (int i = 0; i < n; i++)
        {
            float a = i / (float)n * Mathf.Tau, r = 2f + GD.Randf() * 3f;
            var e = new Enemy { Type = pool[(int)(GD.Randi() % (uint)pool.Length)], Level = lvl, Ambusher = true, NoLeash = true, AnyRealm = false, RespawnTime = 0, Position = World.OnGround(c.X + Mathf.Cos(a) * r, c.Z + Mathf.Sin(a) * r, .3f) };
            over.AddChild(e); _live.Add(e);
        }
        Ui.Note($"An ambush! {n} foes close in", new Color(1f, .5f, .35f));
    }
}
