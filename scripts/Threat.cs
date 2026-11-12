using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>Test hook (MpBot): skip the wait and spawn an ambush now (threat counted as at least 1).</summary>
    public static bool ForceNext;
    /// <summary>Test hook (MpBot): an ambush on this spot now, announced to this player.</summary>
    public static void SpawnFor(Vector3 at, long peer) { Load(); if (World.I != null) Ambush(Mathf.Max(1, Level), at, peer); }

    public static void Tick(float dt)
    {
        Load(); var p = Player.Instance; if (p == null || World.I == null) return;
        // multiplayer: the host runs the ambushes for everyone (they are shared monsters); friends only see them
        if (Net.IsClient) return;
        bool hosting = Net.IsHost && Net.I!.Players.Count > 1;
        if (!hosting && (Dungeon.Active || Arenas.Active || p.Dead)) return;   // only time out in the world counts
        _t += dt;
        int lv = Level;
        if (lv > _shown) { _shown = lv; Ui.Note($"The land grows restless (threat {lv}): monsters are stronger and roam in greater numbers", new Color(1f, .55f, .4f)); }
        _live.RemoveAll(e => !GodotObject.IsInstanceValid(e) || e.Dead);
        foreach (var k in World.NetExtra.Where(kv => !GodotObject.IsInstanceValid(kv.Value)).Select(kv => kv.Key).ToList()) World.NetExtra.Remove(k);   // gone ambushers
        _spawnT -= dt; if (_spawnT > 0 && !ForceNext) return;
        _spawnT = Mathf.Max(_intMin, _int - _intPer * lv);
        bool force = ForceNext; ForceNext = false; if (force) lv = Mathf.Max(1, lv);
        if (!force && (lv <= 0 || Bot.Wanted() || Observer.Wanted() || Shots.Wanted() || MpBot.Wanted() != null)) return;   // the test bots need a calm world
        // who gets ambushed: any hero out in the world, away from Oakhaven (ours, or in a hosted world any player's)
        var spots = new List<(Vector3 at, long peer)>();
        if (hosting) { foreach (var h in Net.WorldHeroes()) spots.Add((h.GlobalPosition, h is RemoteHero rh ? rh.Peer : Net.MyId)); }
        else if (!p.Dead) spots.Add((p.GlobalPosition, Net.MyId));
        spots.RemoveAll(s => new Vector2(s.at.X, s.at.Z).Length() < _safe && !force);   // Oakhaven stays safe
        if (spots.Count == 0) return;
        if (_live.Count >= (_aliveBase + _alivePer * lv) * Mathf.Max(1, spots.Count)) return;
        var (at, peer) = spots[(int)(GD.Randi() % (uint)spots.Count)];
        Ambush(lv, at, peer);
    }

    static void Ambush(int lv, Vector3 at, long peer)
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
            // hosting: a shared monster with its own network number; every friend gets a copy they can see, hit and kill
            if (Net.IsHost) World.AddNetExtra(e);
            over.AddChild(e); _live.Add(e);
            if (Net.IsHost) Net.I!.AmbushSpawn(e);
        }
        string msg = $"An ambush! {n} foes close in";
        if (Net.IsHost) Net.I!.NoteTo(peer, msg); else Ui.Note(msg, new Color(1f, .5f, .35f));
    }
}
