using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>One kind of breakable or lootable prop (data/breakables.json "types").</summary>
public sealed class BreakDef
{
    public string Key = "", Name = "", Interact = "smash", Model = "", Debris = "", Table = "", Surprise = "";
    public float Height = .9f, Radius = .45f; public int Hits = 1, Respawn = 600; public Color? Glow;
    /// <summary>Broken state shared through the server and respawning (respawn &gt; 0); else taken once per hero, or read-only.</summary>
    public bool Shared => Respawn > 0 && Interact is "smash" or "open";
    public bool Smash => Interact == "smash";
    /// <summary>The prop stays in the world once taken (an opened chest, a dug grave, a searched body): it just cannot be used again.</summary>
    public bool Keeps => Interact is "open" or "dig" or "search";
}

/// <summary>A placed prop: a record that exists on every machine (the same id from its world position), made into a node only while a hero is near.</summary>
public sealed class BreakRec
{
    public int Id; public BreakDef Def = null!; public float X, Y = float.NaN, Z, Yaw, Up; public bool Owned; public int Level; public string Text = "", Label = "";
    public Breakable? Node; public bool InDungeon, DungeonTaken;
    public Vector3 Pos => float.IsNaN(Y) ? World.OnGround(X, Z, Up) : new Vector3(X, Y + Up, Z);
}

/// <summary>
/// A-36: crates, barrels, pots, sacks, urns, chests, graves, bodies and relics across the overworld and the dungeons. Smash types break to a
/// weapon blow (polled from the hero's swing: Player.Attacking, the facing and a reach cone, so no combat file changes), a shot or a blast
/// (Projectile.cs) or the Use key, with physics debris, a sound and loot from data/breakables.json by type and area level, and now and then
/// a surprise (rats, a mimic). A shop's prop in a town (Owned: placed inside a town zone) is a crime when an NPC sees it (Crime.Vandalism).
/// Respawn: every prop has a stable id from its world position. Offline the hero's save keeps broken ids with the time (PlayerStats.Broken)
/// and they come back after the type's respawn time. Online the server (or a friend host) owns the table (BreakServer, NetBreak.cs): a
/// client asks, the first asker gets the loot seed, everyone else sees it broken, and the server brings it back and tells everyone.
/// Props are records (placed by Builder, Lands, Wayside dressing, WorldSites, Dungeon) and nodes only within streamRadius of the hero.
/// Dev: "/breakables", "/breaktest" (server table self-test with two fake players).
/// </summary>
public static class Breakables
{
    public static readonly Dictionary<string, BreakDef> Defs = new();
    public static readonly Dictionary<int, BreakRec> ById = new();
    static readonly Dictionary<long, List<BreakRec>> _grid = new();
    public static readonly HashSet<Breakable> Live = new();
    const float GridS = 48f;
    static JsonDocument? _doc; static JsonElement _tables, _debris, _surprise;
    static bool _loaded, _registered;
    public static float StreamR = 70f, Reach = 2.7f, Cone = .25f, Delay = .1f, Window = .4f; static int _bounty = 15, _debrisCap = 48;
    static readonly System.Random _rng = new();

    // ---------------------------------------------------------------- data
    static JsonElement Obj(JsonElement e, string k) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var o) && o.ValueKind == JsonValueKind.Object ? o : default;
    static float F(JsonElement e, string k, float d) => e.ValueKind == JsonValueKind.Object ? JsonUtil.F(e, k, d) : d;
    static string S(JsonElement e, string k, string d = "") => e.ValueKind == JsonValueKind.Object ? JsonUtil.S(e, k, d) : d;
    static (int lo, int hi) Range(JsonElement e, string k, int lo, int hi)
    {
        if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var a) && a.ValueKind == JsonValueKind.Array && a.GetArrayLength() >= 2) { lo = (int)a[0].GetSingle(); hi = Math.Max(lo, (int)a[1].GetSingle()); }
        return (lo, hi);
    }
    static string[] Strs(JsonElement e, string k) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : "").ToArray() : Array.Empty<string>();

    public static void Load()
    {
        if (_loaded) return; _loaded = true;
        Register();
        if (!Godot.FileAccess.FileExists("res://data/breakables.json")) return;
        _doc = JsonUtil.Load("res://data/breakables.json"); var r = _doc.RootElement;
        StreamR = F(r, "streamRadius", 70f); _bounty = (int)F(r, "crimeBounty", 15f);
        var m = Obj(r, "melee"); Reach = F(m, "reach", 2.7f); Cone = F(m, "cone", .25f); Delay = F(m, "delay", .1f); Window = F(m, "window", .4f);
        _tables = Obj(r, "tables"); _debris = Obj(r, "debris"); _surprise = Obj(r, "surprise"); _debrisCap = (int)F(_debris, "cap", 48f);
        var ty = Obj(r, "types");
        if (ty.ValueKind == JsonValueKind.Object) foreach (var p in ty.EnumerateObject())
        {
            var e = p.Value; string glow = S(e, "glow");
            Defs[p.Name] = new BreakDef
            {
                Key = p.Name, Name = S(e, "name", p.Name), Interact = S(e, "interact", "smash"), Model = S(e, "model"), Debris = S(e, "debris"), Table = S(e, "table"), Surprise = S(e, "surprise", "props"),
                Height = F(e, "height", .9f), Radius = F(e, "radius", .45f), Hits = Math.Max(1, (int)F(e, "hits", 1)), Respawn = (int)F(e, "respawn", 600), Glow = glow != "" ? Color.FromHtml(glow) : null,
            };
        }
    }
    public static BreakDef? Def(string key) { Load(); return Defs.TryGetValue(key, out var d) ? d : null; }

    // ---------------------------------------------------------------- the records
    /// <summary>A new world build (Lands.Build, beside Structures.Begin): forget the old props.</summary>
    public static void Begin()
    {
        Load();
        foreach (var b in Live.ToList()) if (GodotObject.IsInstanceValid(b)) b.QueueFree();
        Live.Clear(); ById.Clear(); _grid.Clear(); _authFor = null;
    }
    static long Cell(float x, float z) => ((long)Mathf.FloorToInt(x / GridS) << 32) ^ (uint)Mathf.FloorToInt(z / GridS);
    static int IdAt(float x, float z, int stack)
    {
        uint h = 2166136261u;
        foreach (int v in new[] { Mathf.RoundToInt(x * 4f), Mathf.RoundToInt(z * 4f), stack }) for (int s = 0; s < 32; s += 8) { h ^= (uint)(v >> s) & 255u; h *= 16777619u; }
        int id = (int)(h & 0x7fffffff); if (id == 0) id = 1;
        while (ById.ContainsKey(id)) id = id == int.MaxValue ? 1 : id + 1;   // the same build order on every machine: the same ids
        return id;
    }

    /// <summary>
    /// Places a prop record. y = NaN: it stands on the ground (found when it is made); else a fixed floor height (a block floor, a cellar).
    /// owned null: it is a shop's when it stands in a town zone. stack: a second prop on the same spot (a crate on a crate). Returns null for an unknown type.
    /// </summary>
    public static BreakRec? Add(string type, float x, float z, float yaw = 0f, float y = float.NaN, bool? owned = null, int stack = 0, int level = 0, string text = "", string label = "", float up = 0f)
    {
        var d = Def(type); if (d == null) return null;
        var r = new BreakRec { Id = IdAt(x, z, stack), Def = d, X = x, Z = z, Y = y, Yaw = yaw, Owned = owned ?? TownZones.At(x, z) != null, Level = level, Text = text, Label = label, Up = up + stack * d.Height };
        ById[r.Id] = r; long c = Cell(x, z); if (!_grid.TryGetValue(c, out var l)) _grid[c] = l = new(); l.Add(r);
        return r;
    }

    /// <summary>A dungeon prop (Dungeon.Dress): made at once under the floor's root, local, never saved or shared (floors are made anew).</summary>
    public static void Dungeon(Node3D root, Vector3 local, string type, float yaw, int level)
    {
        if (Net.Dedicated || Def(type) is not { } d) return;
        var r = new BreakRec { Def = d, X = local.X, Y = local.Y, Z = local.Z, Yaw = yaw, Level = Math.Max(1, level), InDungeon = true };
        var b = new Breakable { R = r, Position = local, Rotation = new Vector3(0, yaw, 0) }; r.Node = b; root.AddChild(b);
    }

    /// <summary>The world is built: the field that streams the props, checks swings and runs respawns (under World, so it also runs in dungeons).</summary>
    public static void Finish(Node3D over)
    {
        if (over.GetParent() is Node w) w.AddChild(new BreakField { Name = "BreakField" });
        GD.Print($"[BREAK] {ById.Count} props ({string.Join(", ", ById.Values.GroupBy(r => r.Def.Key).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}"))}), {ById.Values.Count(r => r.Owned)} in towns");
    }

    // ---------------------------------------------------------------- broken state
    public static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    /// <summary>Who decides: the online server's table (Net.Dedicated) or this hero's (offline, or hosting friends; mirrored into PlayerStats.Broken).</summary>
    public static BreakServer Auth { get { EnsureAuth(); return _auth!; } }
    static BreakServer? _auth; static PlayerStats? _authFor; static bool _srvLoaded;
    static readonly HashSet<int> _remote = new();                 // client: what the server says is broken
    static readonly Dictionary<int, double> _pending = new();     // client: asked the server, no answer yet
    public static string ServerPath => Path.Combine(Accounts.Dir, "breakables.json");
    static void EnsureAuth()
    {
        _auth ??= new BreakServer(id => ById.TryGetValue(id, out var r) ? r.Def : null);
        if (Net.Dedicated) { if (!_srvLoaded) { _srvLoaded = true; _auth.Load(ServerPath); } return; }
        var d = Game.D; if (ReferenceEquals(d, _authFor)) return;
        _authFor = d; _auth.Broken.Clear(); d.Broken ??= new();
        foreach (var kv in d.Broken) if (int.TryParse(kv.Key, out int id)) _auth.Broken[id] = kv.Value;
    }
    static void Mirror(int id, long t)
    {
        if (Net.Dedicated) return; var d = Game.D; d.Broken ??= new();
        if (t > 0) d.Broken[id.ToString()] = t; else d.Broken.Remove(id.ToString());
    }
    public static bool IsBroken(BreakRec r)
    {
        if (r.InDungeon) return r.DungeonTaken;
        if (r.Def.Interact == "read") return false;
        if (!r.Def.Shared) return Game.D.Disc.Contains("find:" + r.Id);
        if (Net.IsClient) return _remote.Contains(r.Id);
        return Auth.Broken.ContainsKey(r.Id);
    }
    /// <summary>Ids the server holds broken (sent to a player who joins).</summary>
    public static int[] BrokenIds() => Auth.Broken.Keys.ToArray();

    // ---- client side of the server's messages (NetBreak.cs)
    public static void RemoteSync(int[] ids) { _remote.Clear(); foreach (int i in ids) _remote.Add(i); _pending.Clear(); foreach (var r in ById.Values) Refresh(r, false); }
    public static void RemoteSet(int id, bool broken)
    {
        if (broken) _remote.Add(id); else _remote.Remove(id);
        if (broken && _pending.Remove(id)) Ui.Note("Someone else got to it first.", Ui.Dim);
        if (ById.TryGetValue(id, out var r)) Refresh(r, broken);
    }
    /// <summary>Server / host: a friend broke prop id (already in the table): a host's save keeps it and the host sees it go.</summary>
    public static void HostSaw(int id) { if (Auth.Broken.TryGetValue(id, out long t)) Mirror(id, t); if (ById.TryGetValue(id, out var r)) Refresh(r, true); }
    /// <summary>The server granted our break: roll the loot with its seed.</summary>
    public static void Granted(int id, int seed)
    {
        _pending.Remove(id); _remote.Add(id);
        if (!ById.TryGetValue(id, out var r)) return;
        Reward(r, seed, r.Node != null && GodotObject.IsInstanceValid(r.Node) ? r.Node.GlobalPosition : r.Pos);
    }
    /// <summary>The broken state of a record changed: its node goes (with a puff when someone else broke it in view) or stays as an empty shell.</summary>
    static void Refresh(BreakRec r, bool puff)
    {
        if (r.Node is not { } b || !GodotObject.IsInstanceValid(b)) return;
        bool br = IsBroken(r);
        if (r.Def.Keeps) { b.SetTaken(br); return; }
        if (br) { if (puff && b.IsVisibleInTree()) Effects(r, b.GlobalPosition); b.QueueFree(); r.Node = null; }
    }

    // ---------------------------------------------------------------- using a prop
    /// <summary>how: "use" (the Use key), "melee", "shot", "blast". Smash types need Hits blows (a blast or Use breaks them at once).</summary>
    public static void Use(Breakable b, string how)
    {
        var r = b.R; var d = r.Def; if (b.Gone) return;
        if (how != "use" && !d.Smash) return;
        var p = Player.Instance;
        if (d.Interact == "read")
        {
            Sfx.Play("click"); Ui.Banner(r.Label != "" ? r.Label : d.Name, r.Text);
            if (r.Def.Key == "vista" && !Game.D.Disc.Contains("vista:" + r.Id)) { Game.D.Disc.Add("vista:" + r.Id); Ui.Note("Vista discovered: " + (r.Label != "" ? r.Label : "a view"), Ui.Gold); Sfx.Play("level", .5f); }
            return;
        }
        if (d.Smash && how is "melee" or "shot")
        {
            b.Hits++;
            if (b.Hits < d.Hits) { b.Wobble(); var pre = Obj(_debris, d.Debris); Sfx.Play("wood_hit", .8f, 1f + (float)_rng.NextDouble() * .2f); Fx.Burst(b.GlobalPosition + Vector3.Up * d.Height * .6f, Hex(S(pre, "color", "#6b4a2c")), 5, 2.5f, .35f, .05f, -8f); return; }
        }
        if (IsBroken(r)) return;
        if (r.Owned && p != null && !r.InDungeon) Crime.Vandalism(p, _bounty);
        var at = b.GlobalPosition;
        if (r.InDungeon) { r.DungeonTaken = true; Taken(r, b, at); Reward(r, _rng.Next(), at); return; }
        if (!d.Shared) { Game.D.Disc.Add("find:" + r.Id); Taken(r, b, at); Reward(r, _rng.Next(), at); Game.Save(); return; }
        if (Net.IsClient)
        {
            if (_remote.Contains(r.Id) || _pending.ContainsKey(r.Id)) return;
            _remote.Add(r.Id); _pending[r.Id] = Game.PlayTime; Taken(r, b, at); Net.I!.BreakAsk(r.Id);
            return;
        }
        long now = Now(); Auth.Broken[r.Id] = now; Auth.Dirty = true; Mirror(r.Id, now);
        Taken(r, b, at); Reward(r, _rng.Next(), at);
        if (Net.IsHost) Net.I!.BreakBroadcast(r.Id, true);
    }
    static void Taken(BreakRec r, Breakable b, Vector3 at)
    {
        Effects(r, at);
        if (r.Def.Keeps) b.SetTaken(true);
        else { b.Gone = true; b.QueueFree(); r.Node = null; }
    }
    static Color Hex(string s) { try { return Color.FromHtml(s); } catch { return new Color(.4f, .3f, .2f); } }

    /// <summary>The break: debris pieces (Jolt rigid bodies that shrink away), a burst, the sound. Opening, digging and taking have their own sounds.</summary>
    public static void Effects(BreakRec r, Vector3 at)
    {
        if (Net.Dedicated) return; var d = r.Def;
        switch (d.Interact)
        {
            case "open": Sfx.Play("chest_open"); Fx.Burst(at + Vector3.Up * .7f, new Color(1f, .85f, .3f), 14, 3f, .6f, .08f); return;
            case "dig": Sfx.Play("chop_hit", .8f, .7f); Fx.Dust(at, 1.2f); Fx.Burst(at + Vector3.Up * .2f, new Color(.35f, .27f, .18f), 14, 3f, .5f, .08f, -9f); return;
            case "search": Sfx.Play("pickup"); Fx.Burst(at + Vector3.Up * .6f, d.Glow ?? new Color(1f, .85f, .5f), 10, 1.5f, .6f, .05f, 1f); return;
            case "take": Sfx.Play("pickup"); Sfx.Play("heal", .4f); Fx.Burst(at + Vector3.Up * .6f, d.Glow ?? new Color(1f, .85f, .5f), 18, 2f, .8f, .06f, 1.5f); return;
        }
        var pre = Obj(_debris, d.Debris); var col = Hex(S(pre, "color", "#6b4a2c"));
        Sfx.Play(S(pre, "sound", "chop_hit"), 1f, F(pre, "pitch", 1f) * (.92f + (float)_rng.NextDouble() * .16f));
        Fx.Burst(at + Vector3.Up * d.Height * .5f, col, (int)F(pre, "burst", 10), 3.5f, .5f, .07f, -9f); Fx.Dust(at, .7f);
        int n = (int)F(pre, "pieces", 6); if (n <= 0 || Game.Root == null) return;
        var (s0, s1) = (F(pre, "sizeMin", .08f), F(pre, "sizeMax", .4f));
        if (pre.ValueKind == JsonValueKind.Object && pre.TryGetProperty("size", out var sz) && sz.ValueKind == JsonValueKind.Array && sz.GetArrayLength() >= 2) { s0 = sz[0].GetSingle(); s1 = sz[1].GetSingle(); }
        Debris(at + Vector3.Up * d.Height * .5f, col, n, s0, s1, F(pre, "life", 2.5f), d.Debris == "wood");
    }
    static readonly Queue<RigidBody3D> _pieces = new();
    static readonly Dictionary<Color, StandardMaterial3D> _dmat = new();
    static void Debris(Vector3 at, Color col, int n, float s0, float s1, float life, bool planks)
    {
        if (!_dmat.TryGetValue(col, out var mat)) { _dmat[col] = mat = new StandardMaterial3D { AlbedoColor = col, Roughness = .9f }; Gfx.Tex(mat, planks ? "grain" : "noise", .8f); }
        for (int i = 0; i < n; i++)
        {
            while (_pieces.Count >= _debrisCap) { var o = _pieces.Dequeue(); if (GodotObject.IsInstanceValid(o)) o.QueueFree(); }
            float a = (float)_rng.NextDouble(), L = s0 + (s1 - s0) * a;
            var size = planks ? new Vector3(L, .04f + .03f * (float)_rng.NextDouble(), .07f + .06f * (float)_rng.NextDouble()) : new Vector3(L, L * .6f, L * .8f);
            var rb = new RigidBody3D { CollisionLayer = 0, CollisionMask = 1, Mass = .3f, ContinuousCd = false };
            var mi = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            rb.AddChild(mi); rb.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
            Game.Root!.AddChild(rb);
            float ang = (float)_rng.NextDouble() * Mathf.Tau;
            rb.GlobalPosition = at + new Vector3(Mathf.Cos(ang) * .2f, (float)_rng.NextDouble() * .3f, Mathf.Sin(ang) * .2f);
            rb.Rotation = new Vector3((float)_rng.NextDouble() * 3f, (float)_rng.NextDouble() * 6f, (float)_rng.NextDouble() * 3f);
            rb.LinearVelocity = new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * (1.5f + 2.5f * (float)_rng.NextDouble()) + Vector3.Up * (2f + 2.5f * (float)_rng.NextDouble());
            rb.AngularVelocity = new Vector3((float)_rng.NextDouble() - .5f, (float)_rng.NextDouble() - .5f, (float)_rng.NextDouble() - .5f) * 14f;
            _pieces.Enqueue(rb);
            var tw = rb.CreateTween(); tw.TweenInterval(life + .4f * (float)_rng.NextDouble());
            tw.TweenCallback(Callable.From(() => { if (GodotObject.IsInstanceValid(rb)) rb.Freeze = true; }));
            tw.TweenProperty(mi, "scale", Vector3.One * .02f, .5f);
            tw.TweenCallback(Callable.From(() => { if (GodotObject.IsInstanceValid(rb)) rb.QueueFree(); }));
        }
    }

    // ---------------------------------------------------------------- loot
    static int LevelOf(BreakRec r, Vector3 at) => r.InDungeon || r.Level > 0 ? Math.Max(1, r.Level) : Progression.OverworldLevel(Game.LevelAt(at.X, at.Z, World.Realm), false);
    /// <summary>Loot for the hero who broke it, rolled from the type's table with this seed, at the area level; maybe a surprise.</summary>
    static void Reward(BreakRec r, int seed, Vector3 at)
    {
        if (Net.Dedicated) return;
        var rng = new System.Random(seed); var d = r.Def; int lvl = LevelOf(r, at); var top = at + Vector3.Up * Mathf.Max(.4f, d.Height * .6f);
        var t = Obj(_tables, d.Table);
        if (t.ValueKind == JsonValueKind.Object)
        {
            var (lo, hi) = Range(t, "rolls", 1, 1); int rolls = lo + rng.Next(hi - lo + 1);
            if (rng.NextDouble() < F(t, "empty", 0f)) { if (d.Interact != "smash") Ui.Note("Nothing of worth.", Ui.Dim); rolls = 0; }
            var entries = t.TryGetProperty("entries", out var en) && en.ValueKind == JsonValueKind.Array ? en.EnumerateArray().ToList() : new List<JsonElement>();
            float sum = entries.Sum(e => F(e, "w", 1f));
            var mats = new Dictionary<string, int>();
            Loot.Pop(top, () =>
            {
                for (int i = 0; i < rolls && sum > 0; i++)
                {
                    float x = (float)rng.NextDouble() * sum; JsonElement pick = entries[^1];
                    foreach (var e in entries) { float w = F(e, "w", 1f); if (x < w) { pick = e; break; } x -= w; }
                    if (pick.TryGetProperty("gold", out _))
                    {
                        var (g0, g1) = Range(pick, "gold", 1, 5); int g = g0 + rng.Next(g1 - g0 + 1) + Mathf.RoundToInt(F(pick, "goldPerLevel", 0f) * lvl);
                        g = Mathf.RoundToInt(g * (1 + Mathf.Max(0, Game.TS["goldP"]) / 100f)); if (g > 0) Loot.Drop(top, gold: g);
                    }
                    else if (pick.TryGetProperty("mat", out _))
                    {
                        var ids = Strs(pick, "mat"); if (ids.Length == 0) continue; string id = ids[rng.Next(ids.Length)];
                        var (n0, n1) = Range(pick, "n", 1, 1); int n = n0 + rng.Next(n1 - n0 + 1); mats[id] = mats.GetValueOrDefault(id) + n;
                    }
                    else if (pick.TryGetProperty("pot", out _)) Loot.Drop(top, pot: S(pick, "pot", "hp") == "mp" ? "mp" : "hp");
                    else if (pick.TryGetProperty("gear", out _)) Loot.Drop(top, item: LootTables.Roll(lvl + (int)F(pick, "gearLevel", 0f), F(pick, "gear", 0f)));
                }
            });
            if (mats.Count > 0)
            {
                var D = Game.D;
                foreach (var (id, n) in mats) { D.Mats[id] = D.Mats.GetValueOrDefault(id) + n; var m = GatherDb.Mat(id); Ui.Note($"+{n} {m.Name}", m.Col.Lightened(.2f)); }
                Sfx.Play("pickup"); Game.Notify();
            }
        }
        if (d.Interact == "open") Spellcraft.OnChest(at);   // chests may hold a spell card, as the old ones do
        Surprise(r, rng, at, lvl);
    }

    static void Surprise(BreakRec r, System.Random rng, Vector3 at, int lvl)
    {
        var s = _surprise; if (s.ValueKind != JsonValueKind.Object) return;
        JsonElement set; float chance;
        if (r.Def.Surprise == "grave") { set = Obj(s, "grave"); chance = F(set, "chance", .3f); }
        else { if (!Strs(s, "types").Contains(r.Def.Key) || lvl < F(s, "minLevel", 2f)) return; set = s; chance = F(s, "chance", .025f); }
        if (rng.NextDouble() >= chance || !set.TryGetProperty("picks", out var picks) || picks.ValueKind != JsonValueKind.Array) return;
        var list = picks.EnumerateArray().Where(p => { var only = Strs(p, "only"); return only.Length == 0 || only.Contains(r.Def.Key); }).ToList(); if (list.Count == 0) return;
        float sum = list.Sum(p => F(p, "w", 1f)), x = (float)rng.NextDouble() * sum; var pick = list[^1];
        foreach (var p in list) { float w = F(p, "w", 1f); if (x < w) { pick = p; break; } x -= w; }
        string type = S(pick, "enemy", "giantRat"); var (c0, c1) = Range(pick, "count", 1, 1); int count = c0 + rng.Next(c1 - c0 + 1);
        var parent = r.Node != null && GodotObject.IsInstanceValid(r.Node) ? r.Node.GetParent() as Node3D : null;
        parent ??= r.InDungeon ? TwinRealms.Dungeon.Root : World.I?.Over; if (parent == null) return;
        for (int i = 0; i < count; i++)
        {
            float a = i / (float)Math.Max(1, count) * Mathf.Tau + (float)rng.NextDouble(); var g = at + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (count > 1 ? 1.4f : .2f);
            var pos = r.InDungeon ? new Vector3(g.X, at.Y + .4f, g.Z) : World.OnGround(g.X, g.Z, .3f);
            var e = new Enemy { Type = type, Level = Math.Max(1, lvl + (int)F(pick, "level", 0f)), AnyRealm = true, NoLeash = true, RespawnTime = 0, FromDungeon = r.InDungeon };
            parent.AddChild(e); e.GlobalPosition = pos;
        }
        Ui.Note(S(pick, "text", "Something stirs!"), new Color(1f, .55f, .4f)); Sfx.Play("roar", .7f);
    }

    // ---------------------------------------------------------------- hits from the world
    /// <summary>Projectile.cs: a shot of ours from a to b; the first breakable in its way breaks (one blow). Returns where it struck, or null.</summary>
    public static Vector3? ShotHit(Node3D ctx, Vector3 a, Vector3 b)
    {
        if (Live.Count == 0 || !ctx.IsInsideTree()) return null;
        var q = PhysicsRayQueryParameters3D.Create(a, b, 2); var hit = ctx.GetWorld3D().DirectSpaceState.IntersectRay(q);
        if (hit.Count == 0 || hit["collider"].As<Node>() is not Breakable br || br.Gone) return null;
        Use(br, "shot"); return (Vector3)hit["position"];
    }
    /// <summary>Projectile.cs: a blast of ours at a point breaks every smashable prop within its reach.</summary>
    public static void Blast(Vector3 at, float r)
    {
        foreach (var b in Live.ToList())
            if (GodotObject.IsInstanceValid(b) && !b.Gone && b.R.Def.Smash && b.GlobalPosition.DistanceTo(at) < r + b.R.Def.Radius) Use(b, "blast");
    }

    // ---------------------------------------------------------------- the field: streaming, swings, respawns
    internal static void StreamTick(Vector3 hero)
    {
        var over = World.I?.Over; if (over == null || Net.Dedicated) return;
        float R = StreamR, R2 = (R + 15f) * (R + 15f);
        foreach (var b in Live.ToList())
        {
            if (!GodotObject.IsInstanceValid(b) || b.R.InDungeon) continue;
            var d = b.GlobalPosition - hero; if (d.X * d.X + d.Z * d.Z > R2) { b.R.Node = null; b.QueueFree(); }
        }
        if (TwinRealms.Dungeon.Active) return;
        int c0x = Mathf.FloorToInt((hero.X - R) / GridS), c1x = Mathf.FloorToInt((hero.X + R) / GridS), c0z = Mathf.FloorToInt((hero.Z - R) / GridS), c1z = Mathf.FloorToInt((hero.Z + R) / GridS);
        int made = 0;
        for (int cx = c0x; cx <= c1x; cx++) for (int cz = c0z; cz <= c1z; cz++)
        {
            if (!_grid.TryGetValue(((long)cx << 32) ^ (uint)cz, out var l)) continue;
            foreach (var r in l)
            {
                if (r.Node != null && GodotObject.IsInstanceValid(r.Node)) continue;
                float dx = r.X - hero.X, dz = r.Z - hero.Z; if (dx * dx + dz * dz > R * R) continue;
                bool br = IsBroken(r); if (br && !r.Def.Keeps) continue;
                if (made++ > 40) return;   // spread a big batch over a few ticks
                var b = new Breakable { R = r, Rotation = new Vector3(0, r.Yaw, 0) }; r.Node = b; over.AddChild(b); b.GlobalPosition = r.Pos;
                if (br) b.SetTaken(true);
            }
        }
    }
    /// <summary>Offline or hosting: broken props whose time is up come back (and a host tells its friends). The online server: the same, then saves.</summary>
    internal static void RespawnTick()
    {
        if (Net.IsClient) { foreach (var k in _pending.Where(kv => Game.PlayTime - kv.Value > 10).Select(kv => kv.Key).ToList()) _pending.Remove(k); return; }
        var back = Auth.Tick(Now());
        foreach (int id in back)
        {
            Mirror(id, 0);
            if (Net.Active && (Net.IsHost || Net.Dedicated)) Net.I!.BreakBroadcast(id, false);
            if (ById.TryGetValue(id, out var r) && r.Node is { } b && GodotObject.IsInstanceValid(b) && r.Def.Keeps) b.SetTaken(false);
        }
        if (Net.Dedicated && Auth.Dirty) { Auth.Save(ServerPath); }
    }
    // swing state: a new swing starts when Attacking turns on or the hero's clip changes while attacking
    static bool _wasAtk, _ranged; static string _clip = ""; static float _swingT; static readonly HashSet<Breakable> _swingHit = new();
    internal static void SwingTick(float dt)
    {
        var p = Player.Instance; if (p == null || !GodotObject.IsInstanceValid(p) || p.Dead || Live.Count == 0) { _wasAtk = false; return; }
        bool atk = p.Attacking; string clip = p.AvClip;
        if (atk && (!_wasAtk || clip != _clip)) { _swingT = 0; _swingHit.Clear(); _ranged = Weapons.Get(Game.D.WType()).Ranged; }
        _wasAtk = atk; _clip = clip;
        if (!atk || _ranged) return;
        _swingT += dt; if (_swingT < Delay || _swingT > Delay + Window) return;
        var hp = p.GlobalPosition; var f = p.Facing();
        foreach (var b in Live.ToList())
        {
            if (!GodotObject.IsInstanceValid(b) || b.Gone || !b.R.Def.Smash || _swingHit.Contains(b) || !b.IsVisibleInTree()) continue;
            var d = b.GlobalPosition - hp; float dy = d.Y; d.Y = 0; float dist = d.Length() - b.R.Def.Radius;
            if (dist > Reach || dy < -1.6f || dy > 1.8f) continue;
            if (d.Length() > .3f && d.Normalized().Dot(f) < Cone) continue;
            _swingHit.Add(b); Use(b, "melee");
        }
    }

    // ---------------------------------------------------------------- dev commands
    static void Register()
    {
        if (_registered) return; _registered = true;
        Commands.Register("breakables", "A-36: breakable props: counts by type; breakables near [r]; breakables goto <type>; breakables reset (all back now)", a =>
        {
            var p = Player.Instance;
            if (a.Length > 0 && a[0] == "near" && p != null)
            {
                float rr = a.Length > 1 && float.TryParse(a[1], out var v) ? v : 30f; var hp = p.GlobalPosition;
                var l = ById.Values.Where(r => new Vector2(r.X - hp.X, r.Z - hp.Z).Length() < rr).OrderBy(r => new Vector2(r.X - hp.X, r.Z - hp.Z).Length()).Take(40);
                return string.Join("\n", l.Select(r => $"{r.Id,11} {r.Def.Key,-13} {new Vector2(r.X - hp.X, r.Z - hp.Z).Length(),5:0.0} m  {(IsBroken(r) ? "BROKEN" : "ok")}{(r.Owned ? " (shop)" : "")}{(r.Node != null ? " [node]" : "")}"));
            }
            if (a.Length > 1 && a[0] == "goto" && p != null)
            {
                var hp = p.GlobalPosition; var r = ById.Values.Where(x => x.Def.Key == a[1] && !IsBroken(x)).OrderBy(x => new Vector2(x.X - hp.X, x.Z - hp.Z).Length()).FirstOrDefault();
                if (r == null) return "none of that type";
                p.Teleport(World.OnGround(r.X + 2.5f, r.Z + 2.5f, .6f)); return $"at {r.Def.Key} {r.Id} ({r.X:0},{r.Z:0})";
            }
            if (a.Length > 0 && a[0] == "reset")
            {
                if (Net.IsClient) return "online: the server owns the table";
                foreach (var id in Auth.Broken.Keys.ToList()) { Auth.Broken.Remove(id); Mirror(id, 0); if (Net.IsHost) Net.I!.BreakBroadcast(id, false); }
                return "all props are back (they appear as you walk near)";
            }
            var sb = new StringBuilder();
            sb.AppendLine($"breakables: {ById.Count} props, {ById.Values.Count(r => r.Owned)} shop props, {Live.Count} nodes near, broken {(Net.IsClient ? _remote.Count + " (server)" : Auth.Broken.Count + " (" + (Net.Dedicated ? "server" : "this hero") + ")")}");
            sb.AppendLine("  " + string.Join(", ", ById.Values.GroupBy(r => r.Def.Key).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()} (respawn {(g.First().Def.Respawn > 0 ? g.First().Def.Respawn / 60 + " min" : "never")})")));
            return sb.ToString().TrimEnd();
        });
        Commands.Register("breaktest", "A-36: self-test of the online breakable table (BreakServer) with two fake players: first asker wins, the second is refused, respawn, rate limit, save and load. Prints PASS/FAIL.", a => BreakServer.SelfTest());
    }
}

/// <summary>
/// The table of broken shared props (A-36): on the online server (persisted to TR_DATA/breakables.json) and on a friend host / offline hero
/// (mirrored into the save). Pure logic, no network: NetBreak.cs calls Ask / Tick and sends the results; the "breaktest" command drives it.
/// </summary>
public sealed class BreakServer
{
    public readonly Dictionary<int, long> Broken = new();
    public bool Dirty;
    public int RateMax = 12; public double RateWindow = 3;
    readonly Func<int, BreakDef?> _def;
    readonly Dictionary<long, (double t0, int n)> _rate = new();
    readonly System.Random _rng = new();
    public BreakServer(Func<int, BreakDef?> def) { _def = def; }

    /// <summary>A player asks to break prop id at unix time now (clock = seconds for the rate limit). ok: they get the loot seed; else why.</summary>
    public (bool ok, int seed, string why) Ask(long peer, int id, long now, double clock)
    {
        var d = _def(id); if (d == null || !d.Shared) return (false, 0, "unknown prop");
        if (_rate.TryGetValue(peer, out var rt) && clock - rt.t0 < RateWindow) { if (rt.n >= RateMax) return (false, 0, "too fast"); _rate[peer] = (rt.t0, rt.n + 1); }
        else _rate[peer] = (clock, 1);
        if (Broken.ContainsKey(id)) return (false, 0, "already broken");
        Broken[id] = now; Dirty = true;
        return (true, _rng.Next(1, int.MaxValue), "");
    }
    /// <summary>Props whose respawn time is up (or that no longer exist in the world) are removed; returns the ids that came back.</summary>
    public List<int> Tick(long now)
    {
        var back = new List<int>();
        foreach (var (id, t) in Broken) { var d = _def(id); if (d == null || !d.Shared || now - t >= d.Respawn) back.Add(id); }
        foreach (int id in back) Broken.Remove(id);
        if (back.Count > 0) Dirty = true;
        return back;
    }
    public void Forget(long peer) => _rate.Remove(peer);

    public string ToJson() => JsonSerializer.Serialize(new Dictionary<string, object> { ["v"] = 1, ["broken"] = Broken.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value) });
    public void FromJson(string s)
    {
        Broken.Clear();
        using var doc = JsonDocument.Parse(s);
        if (doc.RootElement.TryGetProperty("broken", out var b) && b.ValueKind == JsonValueKind.Object)
            foreach (var p in b.EnumerateObject()) if (int.TryParse(p.Name, out int id) && p.Value.TryGetInt64(out long t)) Broken[id] = t;
    }
    public bool Save(string path)
    {
        try { string tmp = path + ".tmp"; File.WriteAllText(tmp, ToJson()); File.Move(tmp, path, true); Dirty = false; return true; }
        catch (Exception e) { GD.PushWarning("[BREAK] could not save " + path + ": " + e.Message); return false; }
    }
    public void Load(string path)
    {
        try { if (File.Exists(path)) { FromJson(File.ReadAllText(path)); GD.Print($"[BREAK] server table: {Broken.Count} broken props from {path}"); } }
        catch (Exception e) { GD.PushWarning("[BREAK] could not read " + path + ": " + e.Message); Broken.Clear(); }
    }

    /// <summary>"/breaktest": drives the server functions with two fake players (peers 101 and 102). Not a real network test.</summary>
    public static string SelfTest()
    {
        Breakables.Load();
        var crate = Breakables.Def("crate") ?? new BreakDef { Key = "crate", Respawn = 600 };
        var chest = Breakables.Def("chest") ?? new BreakDef { Key = "chest", Interact = "open", Respawn = 3600 };
        var relic = Breakables.Def("relic") ?? new BreakDef { Key = "relic", Interact = "take", Respawn = -1 };
        var defs = new Dictionary<int, BreakDef> { [11] = crate, [12] = chest, [13] = relic };
        var s = new BreakServer(id => defs.GetValueOrDefault(id));
        var log = new StringBuilder(); int pass = 0, fail = 0;
        void Check(string what, bool ok) { log.AppendLine((ok ? "PASS " : "FAIL ") + what); if (ok) pass++; else fail++; }
        long t = 1_800_000_000; const long A = 101, B = 102;
        var a1 = s.Ask(A, 11, t, 0); Check("player A breaks crate 11 first and gets a loot seed", a1.ok && a1.seed != 0);
        var b1 = s.Ask(B, 11, t + 1, .1); Check("player B is refused the same crate (no second loot)", !b1.ok && b1.why == "already broken");
        var a2 = s.Ask(A, 11, t + 2, .2); Check("player A cannot loot it twice", !a2.ok);
        var b2 = s.Ask(B, 12, t + 3, .3); Check("player B opens chest 12", b2.ok);
        Check("unknown and per-hero props are refused (relic 13, id 99)", !s.Ask(A, 13, t, .4).ok && !s.Ask(A, 99, t, .5).ok);
        Check("join sync lists exactly the broken ids", s.Broken.Keys.OrderBy(x => x).SequenceEqual(new[] { 11, 12 }));
        Check("nothing comes back before its time", s.Tick(t + crate.Respawn - 5).Count == 0);
        var back = s.Tick(t + crate.Respawn + 1); Check($"the crate comes back after {crate.Respawn} s, the chest stays", back.SequenceEqual(new[] { 11 }) && s.Broken.ContainsKey(12));
        var b3 = s.Ask(B, 11, t + crate.Respawn + 2, 1.0); Check("after respawn player B may break the crate", b3.ok);
        Check($"the chest comes back after {chest.Respawn} s", s.Tick(t + 3 + chest.Respawn).Contains(12));
        int ok = 0; for (int i = 0; i < 30; i++) if (s.Ask(A, 12, t + chest.Respawn + 10, 10.0 + i * .01).ok) ok++;
        var spam = s.Ask(A, 11, t + chest.Respawn + 10, 10.5); Check("rate limit: a flood of asks is cut off", ok == 1 && !spam.ok && spam.why == "too fast");
        var late = s.Ask(A, 11, t + chest.Respawn + 20, 20.0); Check("rate limit resets after its window", late.why != "too fast");
        string path = Path.Combine(OS.GetUserDataDir(), "breaktest_table.json");
        bool saved = s.Save(path); var s2 = new BreakServer(id => defs.GetValueOrDefault(id)); s2.Load(path);
        Check("the table saves and loads back the same (" + path + ")", saved && s2.Broken.Count == s.Broken.Count && s2.Broken.All(kv => s.Broken.TryGetValue(kv.Key, out var v) && v == kv.Value));
        try { File.Delete(path); } catch { }
        int ids = 0, dup = 0; var seen = new HashSet<int>(); foreach (var r in Breakables.ById.Values) { ids++; if (!seen.Add(r.Id)) dup++; }
        Check($"world prop ids are unique ({ids} props)", dup == 0);
        log.AppendLine($"breaktest: {pass} passed, {fail} failed. Server logic only: NOT tested with real clients over the network.");
        return log.ToString().TrimEnd();
    }
}

/// <summary>Streams breakable props near the hero, checks swings against them, runs respawns (and the server's table). One per world, under World.</summary>
public partial class BreakField : Node
{
    double _st, _rt;
    public override void _Ready() { ProcessMode = ProcessModeEnum.Always; }
    public override void _Process(double delta)
    {
        if (GetTree().Paused && !Net.Dedicated) return;
        _rt -= delta; if (_rt <= 0) { _rt = Net.Dedicated ? 2 : 3; Breakables.RespawnTick(); }
        if (Net.Dedicated) return;
        _st -= delta; if (_st > 0) return; _st = .25;
        if (Player.Instance is { } p && GodotObject.IsInstanceValid(p)) Breakables.StreamTick(p.GlobalPosition);
    }
    public override void _PhysicsProcess(double delta) { if (!Net.Dedicated && !GetTree().Paused) Breakables.SwingTick((float)delta); }
}

/// <summary>A breakable or lootable prop in the world (layer 2: bodies bump into it, blades and the camera pass, so a swing is never stopped by a crate).</summary>
public partial class Breakable : StaticBody3D, IInteractable
{
    public BreakRec R = null!; public int Hits; public bool Gone; bool _taken;
    Node3D? _glow; float _wob, _t;
    public string Prompt => R.Def.Interact switch
    {
        "smash" => (R.Owned ? "Smash the shop's " : "Smash the ") + R.Def.Name.ToLowerInvariant(),
        "open" => (R.Owned ? "Open the shop's " : "Open the ") + R.Def.Name.ToLowerInvariant(),
        "dig" => "Dig up the grave",
        "search" => "Search the body",
        "take" => R.Label != "" ? "Take: " + R.Label : "Pick it up",
        "read" => R.Def.Key == "vista" ? "Take in the view" : "Read the stone",
        _ => R.Def.Name,
    };
    public bool CanInteract => !Gone && !_taken && !(R.Owned && R.Def.Smash);   // a shop's crate never takes the Use key (no crime by accident beside a shopkeeper): only a blow breaks it
    public override void _Ready()
    {
        var d = R.Def; Breakables.Live.Add(this);
        CollisionLayer = d.Interact is "smash" or "open" || d.Key == "lore" ? 2u : 0u; CollisionMask = 0;
        AddToGroup("interact"); Build();
        if (CollisionLayer != 0) AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = d.Radius, Height = d.Height }, Position = new Vector3(0, d.Height / 2, 0) });
    }
    public override void _ExitTree() { Breakables.Live.Remove(this); if (R.Node == this) R.Node = null; }
    public void Interact() => Breakables.Use(this, "use");
    public void Wobble() { _wob = .35f; SetProcess(true); }
    /// <summary>An opened chest, a dug grave, a searched body: it stays, without its glow, and cannot be used.</summary>
    public void SetTaken(bool on)
    {
        _taken = on; if (_glow != null) _glow.Visible = !on;
        if (R.Def.Key.StartsWith("chest") && _body != null) _body.Transparency = on ? .35f : 0f;
        if (R.Def.Key == "grave" && (_holder ?? (Node3D?)_body) is { } g) g.Scale = on ? new Vector3(1, .4f, 1) : Vector3.One;
        SetProcess(true);
    }
    public override void _Process(double delta)
    {
        float dt = (float)delta; _t += dt;
        if (_wob > 0) { _wob -= dt; float k = _wob / .35f; Rotation = new Vector3(Mathf.Sin(_t * 40f) * .08f * k, R.Yaw, Mathf.Cos(_t * 37f) * .06f * k); if (_wob <= 0) Rotation = new Vector3(0, R.Yaw, 0); }
        if (_glow != null && _glow.Visible) { _glow.Position = new Vector3(0, _glowY + Mathf.Sin(_t * 2f) * .06f, 0); _glow.RotateY(dt * 1.5f); }
        else if (_wob <= 0) SetProcess(false);
    }

    // ---------------------------------------------------------------- looks
    MeshInstance3D? _body; Node3D? _holder; float _glowY = .5f;
    static readonly Dictionary<string, StandardMaterial3D> _mats = new();
    static StandardMaterial3D M(string key, Color c, string tex = "", float rough = .9f)
    {
        if (_mats.TryGetValue(key, out var m)) return m;
        _mats[key] = m = new StandardMaterial3D { AlbedoColor = c, Roughness = rough }; if (tex != "") Gfx.Tex(m, tex, 1f); return m;
    }
    void Part(Mesh mesh, Material mat, Vector3 pos, Vector3? rot = null, Vector3? scl = null)
    {
        var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, Position = pos, Rotation = rot ?? Vector3.Zero, Scale = scl ?? Vector3.One, VisibilityRangeEnd = Breakables.StreamR + 20f };
        if (mesh.GetAabb().Size.Length() < 1.2f) mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        AddChild(mi); _body ??= mi;
    }
    void Build()
    {
        var d = R.Def;
        if (d.Model != "" && Kit.Get(d.Model) is { } kp && kp.Box.Size.Y > .001f)
        {
            float s = d.Height / kp.Box.Size.Y; var c = kp.Box.GetCenter();
            var t = new Transform3D(Basis.Identity.Scaled(Vector3.One * s), new Vector3(-c.X * s, -kp.Box.Position.Y * s, -c.Z * s)) * kp.Xf;
            _body = new MeshInstance3D { Mesh = kp.Mesh, Transform = t, VisibilityRangeEnd = Breakables.StreamR + 20f };
            if (d.Height < 1.3f) _body.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            _holder = new Node3D(); _holder.AddChild(_body); AddChild(_holder);
        }
        else Fallback(d);
        if (d.Glow is { } g) Glint(g);
    }
    void Fallback(BreakDef d)
    {
        var wood = M("wood", new Color(.42f, .3f, .18f), "planks"); var iron = M("iron", new Color(.25f, .25f, .27f), "", .5f);
        var clay = M("clay", new Color(.55f, .34f, .22f), "noise"); var cloth = M("sack", new Color(.62f, .53f, .38f), "cloth");
        var stone = M("stone", new Color(.5f, .49f, .47f), "stone"); var bone = M("bone", new Color(.84f, .8f, .7f), "noise");
        float h = d.Height;
        switch (d.Key)
        {
            case "crate": case "crate_small": case "fruit_crate":
                Part(new BoxMesh { Size = new Vector3(h, h, h) }, wood, new Vector3(0, h / 2, 0)); break;
            case "barrel": case "barrel_apples":
                Part(new CylinderMesh { TopRadius = d.Radius * .9f, BottomRadius = d.Radius * .9f, Height = h, RadialSegments = 12 }, wood, new Vector3(0, h / 2, 0));
                Part(new CylinderMesh { TopRadius = d.Radius * .93f, BottomRadius = d.Radius * .93f, Height = .05f, RadialSegments = 12 }, iron, new Vector3(0, h * .25f, 0));
                Part(new CylinderMesh { TopRadius = d.Radius * .93f, BottomRadius = d.Radius * .93f, Height = .05f, RadialSegments = 12 }, iron, new Vector3(0, h * .75f, 0)); break;
            case "sack":
                Part(new SphereMesh { Radius = d.Radius, Height = h, RadialSegments = 10, Rings = 5 }, cloth, new Vector3(0, h * .45f, 0), null, new Vector3(1.1f, 1, .9f)); break;
            case "pot": case "urn":
                Part(new SphereMesh { Radius = d.Radius, Height = h * .75f, RadialSegments = 12, Rings = 6 }, d.Key == "urn" ? stone : clay, new Vector3(0, h * .38f, 0));
                Part(new CylinderMesh { TopRadius = d.Radius * .55f, BottomRadius = d.Radius * .45f, Height = h * .3f, RadialSegments = 12 }, d.Key == "urn" ? stone : clay, new Vector3(0, h * .82f, 0)); break;
            case "chest": case "chest_rare":
                Part(new BoxMesh { Size = new Vector3(1f, .55f, .65f) }, d.Key == "chest" ? wood : iron, new Vector3(0, .28f, 0));
                Part(new BoxMesh { Size = new Vector3(1f, .2f, .65f) }, d.Key == "chest" ? wood : iron, new Vector3(0, .65f, 0));
                Part(new BoxMesh { Size = new Vector3(.14f, .14f, .06f) }, M("gold", new Color(.9f, .7f, .25f), "", .4f), new Vector3(0, .55f, .34f)); break;
            case "grave":
                Part(new BoxMesh { Size = new Vector3(1f, .3f, 2f) }, M("earth", new Color(.3f, .23f, .16f), "noise"), new Vector3(0, .15f, 0)); break;
            case "corpse": Skeleton(bone, cloth); _glowY = .9f; break;
            case "lore":
                Part(new BoxMesh { Size = new Vector3(.7f, 1.1f, .25f) }, stone, new Vector3(0, .55f, 0), new Vector3(-.08f, 0, .04f));
                var rune = new StandardMaterial3D { AlbedoColor = new Color(1f, .7f, .35f), EmissionEnabled = true, Emission = new Color(1f, .55f, .2f), EmissionEnergyMultiplier = 1.6f };
                Part(new BoxMesh { Size = new Vector3(.34f, .5f, .02f) }, rune, new Vector3(0, .62f, .14f), new Vector3(-.08f, 0, .04f)); break;
            case "vista":
                for (int i = 0; i < 3; i++) Part(new SphereMesh { Radius = .35f - i * .08f, Height = .4f - i * .08f, RadialSegments = 8, Rings = 4 }, stone, new Vector3(0, .15f + i * .3f, 0)); break;
        }
    }
    /// <summary>A traveller's bones slumped against whatever is behind them (local -Z), legs out towards +Z.</summary>
    void Skeleton(Material bone, Material cloth)
    {
        var cap = new CapsuleMesh { Radius = .045f, Height = .42f, RadialSegments = 6, Rings = 1 };
        Part(new SphereMesh { Radius = .13f, Height = .24f, RadialSegments = 8, Rings = 4 }, bone, new Vector3(.05f, .78f, -.28f), new Vector3(.4f, 0, .35f));   // skull, head lolled
        Part(new SphereMesh { Radius = .2f, Height = .36f, RadialSegments = 8, Rings = 4 }, cloth, new Vector3(0, .46f, -.3f), new Vector3(-.3f, 0, 0), new Vector3(1.1f, 1.2f, .8f));   // rags over the ribs
        Part(new BoxMesh { Size = new Vector3(.3f, .12f, .2f) }, bone, new Vector3(0, .12f, -.22f));   // pelvis
        foreach (float s in new[] { -1f, 1f })
        {
            Part(cap, bone, new Vector3(s * .12f, .1f, .05f), new Vector3(Mathf.Pi / 2 - .1f, s * .15f, 0));        // thigh
            Part(cap, bone, new Vector3(s * .16f, .08f, .45f), new Vector3(Mathf.Pi / 2 + .05f, s * .1f, 0));      // shin
            Part(cap, bone, new Vector3(s * .26f, .42f, -.2f), new Vector3(.3f, 0, s * .5f));                      // upper arm
            Part(cap, bone, new Vector3(s * .3f, .2f, -.02f), new Vector3(1.2f, 0, s * .2f));                       // forearm on the lap
        }
    }
    void Glint(Color c)
    {
        _glow = new Node3D { Position = new Vector3(0, _glowY, 0) }; AddChild(_glow);
        var m = new StandardMaterial3D { AlbedoColor = c, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = 3f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        _glow.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = .07f, Height = .14f, RadialSegments = 4, Rings = 2 }, MaterialOverride = m, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        _glow.AddChild(new OmniLight3D { LightColor = c, OmniRange = 3.5f, LightEnergy = 1.1f, ShadowEnabled = false, DistanceFadeEnabled = true, DistanceFadeBegin = 40f, DistanceFadeLength = 10f });
        var pm = new StandardMaterial3D { AlbedoColor = c, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add, VertexColorUseAsAlbedo = true, BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled };
        _glow.AddChild(new CpuParticles3D
        {
            Amount = 10, Lifetime = 1.8f, Direction = Vector3.Up, Spread = 25f, InitialVelocityMin = .15f, InitialVelocityMax = .45f, Gravity = new Vector3(0, .2f, 0),
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere, EmissionSphereRadius = .18f, Mesh = new QuadMesh { Size = new Vector2(.05f, .05f), Material = pm },
            ColorRamp = Vfx.Ramp((0, new Color(c, 0)), (.25f, new Color(c, .9f)), (1, new Color(c, 0))),
        });
    }
}
