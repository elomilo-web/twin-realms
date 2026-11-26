using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>One overworld level zone (data/zones.json): a circle with a fixed level range, reward multipliers and the packs that live there.</summary>
public sealed class ZoneDef
{
    public string Id = "", Name = "", Land = "home", Boss = "";
    public float X, Z, R, XpMul = 1, GoldMul = 1, LootBonus, ItemChance;
    public int MinLvl = 1, MaxLvl = 1, Density, ShadowDensity, BossLvl;
    public string[] Packs = Array.Empty<string>(), ShadowPacks = Array.Empty<string>();
    public bool Home => Land == "home";
}

/// <summary>A pack template: a leader (named with a prefix, a level or two higher) plus its members, around a camp point.</summary>
public sealed class PackDef
{
    public string Id = "", Name = "", Leader = "", Prefix = "";
    public readonly List<(string type, int min, int max)> Members = new();
    public bool Camp; public float Spread = 6;
}

/// <summary>
/// Overworld level zones and the monster-pack rules (data/zones.json): which zone a spot is in, its fixed level there,
/// aggro / leash / evade / respawn tunables, and the cap on how many idle overworld monsters run near the heroes at once.
/// </summary>
public static class Zones
{
    static bool _loaded;
    public static readonly List<ZoneDef> All = new();
    public static readonly Dictionary<string, PackDef> Packs = new();
    public static float AggroMul = .6f, Leash = 32, LeashHeroMul = 2, LeashHeroMin = 26, EvadeTime = 4, ReturnRegen = .25f, PackAlert = 12;
    public static int LeaderLvlMin = 1, LeaderLvlMax = 2, MaxActive = 120, MaxEnemies = 900, ShadowAdd = 3;
    public static float LeaderHp = 1.6f, LeaderDmg = 1.2f, LeaderReward = 2f, LeaderItemChance = .25f, RespawnMin = 90, RespawnMax = 180, RespawnClear = 60, PackSpacing = 30;

    public static void Load()
    {
        if (_loaded) return; _loaded = true;
        using var doc = JsonUtil.Load("res://data/zones.json"); var root = doc.RootElement;
        if (root.TryGetProperty("rules", out var r))
        {
            AggroMul = JsonUtil.F(r, "aggroMul", AggroMul); Leash = JsonUtil.F(r, "leash", Leash); LeashHeroMul = JsonUtil.F(r, "leashHeroMul", LeashHeroMul); LeashHeroMin = JsonUtil.F(r, "leashHeroMin", LeashHeroMin);
            EvadeTime = JsonUtil.F(r, "evadeTime", EvadeTime); ReturnRegen = JsonUtil.F(r, "returnRegen", ReturnRegen); PackAlert = JsonUtil.F(r, "packAlert", PackAlert);
            LeaderHp = JsonUtil.F(r, "leaderHp", LeaderHp); LeaderDmg = JsonUtil.F(r, "leaderDmg", LeaderDmg); LeaderReward = JsonUtil.F(r, "leaderReward", LeaderReward); LeaderItemChance = JsonUtil.F(r, "leaderItemChance", LeaderItemChance);
            RespawnClear = JsonUtil.F(r, "respawnClear", RespawnClear); PackSpacing = JsonUtil.F(r, "packSpacing", PackSpacing);
            MaxActive = (int)JsonUtil.F(r, "maxActive", MaxActive); MaxEnemies = (int)JsonUtil.F(r, "maxEnemies", MaxEnemies); ShadowAdd = (int)JsonUtil.F(r, "shadowAdd", ShadowAdd);
            if (r.TryGetProperty("leaderLevels", out var ll) && ll.ValueKind == JsonValueKind.Array && ll.GetArrayLength() >= 2) { LeaderLvlMin = ll[0].GetInt32(); LeaderLvlMax = Math.Max(LeaderLvlMin, ll[1].GetInt32()); }
            if (r.TryGetProperty("respawn", out var rs) && rs.ValueKind == JsonValueKind.Array && rs.GetArrayLength() >= 2) { RespawnMin = rs[0].GetSingle(); RespawnMax = Mathf.Max(RespawnMin, rs[1].GetSingle()); }
        }
        if (root.TryGetProperty("packs", out var ps))
            foreach (var p in ps.EnumerateObject())
            {
                if (p.Name.StartsWith("_") || p.Value.ValueKind != JsonValueKind.Object) continue;
                var e = p.Value; var d = new PackDef { Id = p.Name, Name = JsonUtil.S(e, "name", p.Name), Leader = JsonUtil.S(e, "leader"), Prefix = JsonUtil.S(e, "prefix"), Spread = JsonUtil.F(e, "spread", 6) };
                d.Camp = e.TryGetProperty("camp", out var c) && c.ValueKind == JsonValueKind.True;
                if (e.TryGetProperty("members", out var ms))
                    foreach (var m in ms.EnumerateArray()) { int lo = m.GetArrayLength() > 1 ? m[1].GetInt32() : 1, hi = m.GetArrayLength() > 2 ? m[2].GetInt32() : lo; d.Members.Add((m[0].GetString() ?? "wolf", lo, Math.Max(lo, hi))); }
                Packs[p.Name] = d;
            }
        if (root.TryGetProperty("zones", out var zs))
            foreach (var e in zs.EnumerateArray())
            {
                var z = new ZoneDef
                {
                    Id = JsonUtil.S(e, "id"), Name = JsonUtil.S(e, "name"), Land = JsonUtil.S(e, "land", "home"), Boss = JsonUtil.S(e, "boss"),
                    X = JsonUtil.F(e, "x"), Z = JsonUtil.F(e, "z"), R = JsonUtil.F(e, "r", 100), XpMul = JsonUtil.F(e, "xpMul", 1), GoldMul = JsonUtil.F(e, "goldMul", 1),
                    LootBonus = JsonUtil.F(e, "lootBonus"), ItemChance = JsonUtil.F(e, "itemChance"), MinLvl = (int)JsonUtil.F(e, "minLvl", 1), MaxLvl = (int)JsonUtil.F(e, "maxLvl", 1),
                    Density = (int)JsonUtil.F(e, "density"), ShadowDensity = (int)JsonUtil.F(e, "shadowDensity"), BossLvl = (int)JsonUtil.F(e, "bossLvl"),
                };
                z.MaxLvl = Math.Max(z.MinLvl, z.MaxLvl);
                z.Packs = Strs(e, "packs"); z.ShadowPacks = Strs(e, "shadowPacks");
                All.Add(z);
            }
    }

    static string[] Strs(JsonElement e, string k)
    {
        if (!e.TryGetProperty(k, out var a) || a.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
        var l = new List<string>(); foreach (var s in a.EnumerateArray()) l.Add(s.GetString() ?? ""); return l.ToArray();
    }

    /// <summary>The zone at a world spot: the smallest circle that holds it (null outside every zone).</summary>
    public static ZoneDef? At(float x, float z)
    {
        Load(); ZoneDef? best = null;
        foreach (var zn in All) { float dx = x - zn.X, dz = z - zn.Z; if (dx * dx + dz * dz <= zn.R * zn.R && (best == null || zn.R < best.R)) best = zn; }
        return best;
    }

    /// <summary>The zone's level at a spot: minLvl on its side nearest Oakhaven rising to maxLvl on the far side; Shadow realm adds shadowAdd on the home island.</summary>
    public static int Level(ZoneDef zn, float x, float z, int realm)
    {
        float dc = new Vector2(zn.X, zn.Z).Length(), lo = Mathf.Max(0, dc - zn.R), hi = dc + zn.R, d = new Vector2(x, z).Length();
        float t = Mathf.Clamp((d - lo) / Mathf.Max(1, hi - lo), 0, 1);
        int lvl = zn.MinLvl + Mathf.FloorToInt(t * (zn.MaxLvl - zn.MinLvl + .999f));
        return Math.Min(zn.MaxLvl, lvl) + (realm == 1 && zn.Home ? ShadowAdd : 0);
    }

    /// <summary>"level 8-12" for the HUD (Shadow levels in the Shadow realm).</summary>
    public static string RangeText(ZoneDef zn, int realm) { int add = realm == 1 && zn.Home ? ShadowAdd : 0; return $"level {zn.MinLvl + add}-{zn.MaxLvl + add}"; }

    public static float RespawnRoll() { Load(); return (float)GD.RandRange(RespawnMin, RespawnMax); }

    /// <summary>A stable per-name seed (string.GetHashCode differs between runs; every machine must place the same packs).</summary>
    public static uint Seed(string s) { uint h = 2166136261u; foreach (char c in s) { h ^= c; h *= 16777619u; } return h == 0 ? 1u : h; }

    // ---- the active cap: each physics frame the idle overworld monsters count themselves into 5 m distance bins; the next frame
    // only the nearest maxActive run (the rest sleep like far-away ones). Fighting monsters always run.
    const float BinW = 5f; static readonly int[] _bins = new int[40]; static ulong _frame; static float _cut = 1e9f;
    /// <summary>Counts this monster (at distP from its hero) and says whether it is beyond the active cap this frame.</summary>
    public static bool Culled(float distP)
    {
        ulong f = Engine.GetPhysicsFrames();
        if (f != _frame)
        {
            Load(); _frame = f; int sum = 0; _cut = 1e9f;
            for (int i = 0; i < _bins.Length; i++) { sum += _bins[i]; if (sum >= MaxActive) { _cut = (i + 1) * BinW; break; } }
            Array.Clear(_bins, 0, _bins.Length);
        }
        int b = (int)(distP / BinW); if (b >= 0 && b < _bins.Length) _bins[b]++;
        return distP > _cut;
    }

    // ---- camp virtualization: pack monsters are data records (CampSlot) built with the world in a fixed order, so the network numbers
    // (World.NetEnemies: slots first) match on every machine. A camp's Enemy nodes exist only while a hero is within CampIn metres of it,
    // and are freed past CampOut (hysteresis). Dead members stay dead (with their respawn timer) while the camp is parked.
    public static readonly List<CampSlot> Slots = new();
    public static readonly List<Camp> Camps = new();
    public static float CampIn = 140, CampOut = 190; public static int CampSpawnsPerTick = 2;
    /// <summary>True once World.IndexEnemies gave the slots their network numbers: only then does the streamer make nodes.</summary>
    public static bool Indexed;
    public static double Now => Time.GetTicksMsec() / 1000.0;

    public static void ResetCamps()
    {
        foreach (var s in Slots) if (s.Node != null && GodotObject.IsInstanceValid(s.Node)) s.Node.QueueFree();
        Slots.Clear(); Camps.Clear(); Indexed = false;
    }
    public static Camp NewCamp(float x, float z) { var c = new Camp { C = new Vector2(x, z) }; Camps.Add(c); return c; }
    public static CampSlot AddSlot(Camp c, CampSlot s) { s.Camp = c; c.M.Add(s); Slots.Add(s); return s; }

    /// <summary>World.IndexEnemies: the slots take network numbers 0..Slots.Count-1 (a parked slot's entry is null).</summary>
    public static void IndexSlots(List<Enemy> list)
    {
        foreach (var s in Slots)
        {
            s.Idx = list.Count; list.Add(Live(s) ? s.Node! : null!);
            if (Live(s)) { s.Node!.NetIdx = s.Idx; s.Node.Puppet = Net.IsClient; }
        }
        Indexed = true;
    }
    static bool Live(CampSlot s) => s.Node != null && GodotObject.IsInstanceValid(s.Node);

    /// <summary>Manifest flags: 4 = camp slot, 8 = pack leader (on top of 1 = any realm, 2 = no leash).</summary>
    public static float SlotFlags(CampSlot s) => (s.AnyRealm ? 1 : 0) + (s.NoLeash ? 2 : 0) + 4 + (s.Leader ? 8 : 0);

    /// <summary>
    /// Friend: line our slots up with the host's (its leading entries flagged 4). Matching slots are kept; differing ones take the host's
    /// record (their node is freed and remade); missing ones are added as one-monster camps; extras are dropped. Returns (matched, made, removed).
    /// </summary>
    public static (int matched, int made, int removed) ReconcileSlots(string[] types, float[] d, int count, List<Enemy> into)
    {
        int matched = 0, made = 0, removed = 0;
        for (int i = 0; i < count; i++)
        {
            var home = new Vector3(d[i * 5], d[i * 5 + 1], d[i * 5 + 2]); int fl = (int)d[i * 5 + 4]; string ty = types[i] == "" ? "wolf" : types[i];
            CampSlot s;
            if (i < Slots.Count)
            {
                s = Slots[i];
                if (s.Type == ty && new Vector2(s.Pos.X - home.X, s.Pos.Z - home.Z).LengthSquared() < 16f) { matched++; s.Idx = i; into.Add(Live(s) ? s.Node! : null!); if (Live(s)) { s.Node!.NetIdx = i; s.Node.Puppet = Net.IsClient; } continue; }
                Park(s); made++;
            }
            else { s = AddSlot(NewCamp(home.X, home.Z), new CampSlot()); made++; }
            s.Type = ty; s.Pos = home; s.Level = Math.Max(1, (int)d[i * 5 + 3]); s.AnyRealm = (fl & 1) != 0; s.NoLeash = (fl & 2) != 0; s.Leader = (fl & 8) != 0; s.Dead = false;
            s.Idx = i; into.Add(null!);
        }
        while (Slots.Count > count) { var s = Slots[^1]; Park(s); s.Camp.M.Remove(s); Slots.RemoveAt(Slots.Count - 1); removed++; }
        Camps.RemoveAll(c => c.M.Count == 0);
        Indexed = true;
        return (matched, made, removed);
    }

    static Enemy Make(CampSlot s)
    {
        var e = new Enemy { Type = s.Type, Level = s.Level, PackId = s.PackId, AnyRealm = s.AnyRealm, NoLeash = s.NoLeash, Position = s.Pos, NetIdx = s.Idx, Puppet = Net.IsClient };
        if (s.Leader) { e.Leader = true; e.Prefix = s.Prefix == "" ? "" : s.Prefix + " "; e.TierHp = LeaderHp; e.TierDmg = LeaderDmg; }
        if (s.Dead)
        {
            double el = Now - s.DeadAt;
            if (Net.IsClient) { e.StartDead = 99f; e.RespawnTime = 1e9f; }   // the host's snapshots bring it back
            else if (el < s.RespawnT) { e.StartDead = Mathf.Max(8.5f, (float)el); e.RespawnTime = s.RespawnT; }
            else s.Dead = false;   // its respawn came while nobody was near
        }
        return e;
    }

    /// <summary>Free a slot's node, keeping alive/dead and the respawn timer in the record.</summary>
    static void Park(CampSlot s)
    {
        var e = s.Node; s.Node = null;
        if (e != null && GodotObject.IsInstanceValid(e))
        {
            s.Dead = e.Dead; if (s.Dead) { s.DeadAt = Now - e.DeadFor; s.RespawnT = e.RespawnTime; }
            e.QueueFree();
        }
        if (s.Idx >= 0 && s.Idx < World.NetEnemies.Count) World.NetEnemies[s.Idx] = null!;
    }

    static readonly List<Vector2> _heroes = new();
    /// <summary>Make the camps near a hero, park the far ones (host and solo: our hero and the friends' out in the overworld; a friend: our own hero).</summary>
    public static void StreamTick(Node parent)
    {
        if (!Indexed || Camps.Count == 0) return;
        _heroes.Clear();
        var me = Player.Instance;
        if (me != null && GodotObject.IsInstanceValid(me) && me.IsInsideTree() && !Net.Dedicated && Net.MyZone == 0) _heroes.Add(new Vector2(me.GlobalPosition.X, me.GlobalPosition.Z));
        if (Net.IsHost && Net.I != null)
            foreach (var h in Net.I.Heroes.Values) if (GodotObject.IsInstanceValid(h) && h.HasPose && h.Zone == 0) _heroes.Add(new Vector2(h.GlobalPosition.X, h.GlobalPosition.Z));
        float in2 = CampIn * CampIn, out2 = CampOut * CampOut; int budget = CampSpawnsPerTick;
        foreach (var c in Camps)
        {
            float best = float.MaxValue; foreach (var h in _heroes) best = Mathf.Min(best, h.DistanceSquaredTo(c.C));
            bool missing = !c.Live; if (!missing && best < in2) foreach (var s in c.M) if (!Live(s)) { missing = true; break; }   // (a slot a host's list replaced)
            if (missing && best < in2 && budget > 0)
            {
                c.Live = true; budget--;
                foreach (var s in c.M)
                {
                    if (Live(s)) continue;
                    var e = Make(s); s.Node = e; parent.AddChild(e);
                    if (s.Idx >= 0 && s.Idx < World.NetEnemies.Count) World.NetEnemies[s.Idx] = e;
                }
            }
            else if (c.Live && best > out2) { c.Live = false; foreach (var s in c.M) Park(s); }
        }
    }
    public static int LiveCount() { int n = 0; foreach (var s in Slots) if (Live(s)) n++; return n; }
}

/// <summary>One pack monster as data: what to build, where, and (while parked) whether it is dead and when it comes back.</summary>
public sealed class CampSlot
{
    public string Type = "wolf", Prefix = ""; public int Level = 1, PackId, Idx = -1; public bool Leader, AnyRealm, NoLeash; public Vector3 Pos;
    public Camp Camp = null!; public Enemy? Node; public bool Dead; public double DeadAt; public float RespawnT;
}
/// <summary>A camp point and its members; Live while its nodes exist.</summary>
public sealed class Camp { public Vector2 C; public readonly List<CampSlot> M = new(); public bool Live; }

/// <summary>Ticks the camp streamer a few times a second (a child of the overworld root, so it pauses with it).</summary>
public partial class CampStreamer : Node
{
    double _t;
    public override void _Process(double delta) { _t -= delta; if (_t > 0) return; _t = .15; Zones.StreamTick(GetParent()); }
}
