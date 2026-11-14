using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>
/// Raid lairs: instanced boss fights that reuse the dungeon instances (solo, or shared by a party: the owner runs the boss,
/// everyone else mirrors it). The first is Scarletta's Rotting Hollow, entered from the shrine on Lumen Aerie above Oakhaven:
/// an entry hall, a wide passage and one great vaulted chamber overgrown with giant mushrooms and oozing slime.
/// Beating the boss raises a chest for every challenger on the floor; only its owner can open it (data/dungeons.json "scarletta").
/// </summary>
public static partial class Dungeon
{
    public const string ScarlettaLair = "scarletta";
    public static bool IsLair(string key) => key == ScarlettaLair;
    /// <summary>Where you come out: in front of the shrine you entered by.</summary>
    static Vector3 LairReturn(string key)
    {
        var s = Lands.SkyGet("lumen"); if (s == null) return new Vector3(0, 3, 9);
        float y = Lands.SkyTop(s, s.X, s.Z + 7); if (float.IsNaN(y)) y = s.Y;
        return new Vector3(s.X, y + 1f, s.Z + 7);
    }

    /// <summary>
    /// The lair grid is 8 m a cell (Dungeon.Tile): an entry hall, two halls haunted by shades of the party, 8 m passages between them,
    /// and the great chamber, 1080 x 1000 m. Rooms are joined in this order; the chamber is the farthest, so it is the boss room.
    /// </summary>
    static void LairRooms()
    {
        _rooms.Add(new Room { X = 68, Z = 176, W = 10, H = 8, Cx = 73, Cz = 180 });    // entry hall (the exit pad)
        _rooms.Add(new Room { X = 30, Z = 156, W = 16, H = 12, Cx = 38, Cz = 162 });   // the first hall of shades
        _rooms.Add(new Room { X = 100, Z = 136, W = 18, H = 12, Cx = 109, Cz = 142 }); // the second hall of shades
        _rooms.Add(new Room { X = 8, Z = 4, W = 135, H = 125, Cx = 75, Cz = 66 });     // the great chamber
    }
    /// <summary>The lair's boss on this machine (the owner's real one, or a friend's mirrored copy).</summary>
    public static Enemy? Boss => _boss != null && GodotObject.IsInstanceValid(_boss) ? _boss : null;
    /// <summary>The boss room's intro has played on this floor (the first hero to step in starts it for the whole party).</summary>
    public static bool LairIntroDone;
    /// <summary>Every frame (Main): the first time our hero steps into the boss room, the whole party gets the intro and is brought in.</summary>
    public static void LairTick()
    {
        if (!InLair || LairIntroDone) return; var p = Player.Instance; if (p == null || p.Dead || !InBossRoom(p.GlobalPosition)) return;
        LairIntroDone = true;
        if (Shared) Net.I?.SendLairIntro(Seed);
        LairIntro.Play();
    }
    /// <summary>A friend stepped into the boss room: our intro (and our hero is brought in too).</summary>
    public static void LairIntroFromFriend(int seed) { if (!InLair || seed != Seed || LairIntroDone) return; LairIntroDone = true; LairIntro.Play(); }
    /// <summary>Where the party stands for the intro: a line facing her, 70 m from the middle of the room.</summary>
    public static Vector3 IntroSpot()
    {
        int slot = (int)(Net.Active ? (Net.MyId % 7) : 0); var c = BossRoomCentre;
        return new Vector3(c.X + (slot - 3) * 3f, c.Y + 1f, c.Z + 70f);
    }
    /// <summary>The middle of the boss room, in world space (ground level).</summary>
    public static Vector3 BossRoomCentre => _rooms.Count > _bossRoom ? Origin + new Vector3((_rooms[_bossRoom].Cx + .5f) * Tile, 0, (_rooms[_bossRoom].Cz + .5f) * Tile) : Origin;
    /// <summary>Is this point inside the boss room?</summary>
    public static bool InBossRoom(Vector3 p)
    {
        if (_rooms.Count <= _bossRoom) return false; var r = _rooms[_bossRoom];
        float x = (p.X - Origin.X) / Tile, z = (p.Z - Origin.Z) / Tile;
        return x >= r.X && x < r.X + r.W && z >= r.Z && z < r.Z + r.H;
    }

    static void LairBuild(JsonElement th, int bossRoom)
    {
        var r = _rooms[bossRoom]; float cx = (r.Cx + .5f) * Tile, cz = (r.Cz + .5f) * Tile;
        LairDecor(r, th);
        // the halls before her: overgrown, and haunted by black shades of the party (EnemyShade.cs)
        var heroes = Enemy.ShadeModels();
        for (int i = 1; i < _rooms.Count; i++) if (i != bossRoom) { HallDecor(_rooms[i], i); SpawnShades(_rooms[i], i, heroes); }
        // the boss: no rolled upgrades or gear tier (she reads the challengers instead, EnemyScarletta.cs)
        var e = new Enemy { Type = "scarletta", Level = (int)_baseLvl + Progression.DunBoss, FromDungeon = true, NoLeash = true, AnyRealm = true, Position = new Vector3(cx, 0.4f, cz) };
        LairIntroDone = false;
        if (Inst != 0) { e.NetIdx = NetList.Count; e.Puppet = Owner != Net.MyId; NetList.Add(e); }
        Root!.AddChild(e); _boss = e;
    }

    /// <summary>
    /// The great chamber (about a kilometre across): a forest of giant glowing mushrooms, meadows of small ones, slime lakes that
    /// bubble, ooze running down the walls and dripping from the vault. The middle (around the boss) and the way in stay open.
    /// </summary>
    static void LairDecor(Room r, JsonElement th)
    {
        var rnd = new Random(Seed + 31); float R() => (float)rnd.NextDouble();
        var deco = new Node3D { Name = "LairDecor" }; Root!.AddChild(deco);
        void Put(Mesh m, Material mt, Vector3 p, Vector3? rot = null, Vector3? scl = null) => deco.AddChild(new MeshInstance3D { Mesh = m, MaterialOverride = mt, Position = p, Rotation = rot ?? Vector3.Zero, Scale = scl ?? Vector3.One });
        var stem = Gfx.Tex(DMat(new Color(.82f, .78f, .66f)), "noise", .6f);
        Color[] capCols = { new(.55f, .22f, .5f), new(.25f, .5f, .45f), new(.62f, .3f, .2f), new(.35f, .25f, .55f) };
        StandardMaterial3D Glow(Color c, float e) => new() { AlbedoColor = c, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = e, Roughness = .6f };
        var capMats = new StandardMaterial3D[capCols.Length]; for (int i = 0; i < capCols.Length; i++) capMats[i] = Glow(capCols[i], .4f);
        var smallMats = new StandardMaterial3D[capCols.Length]; for (int i = 0; i < capCols.Length; i++) smallMats[i] = Glow(capCols[i].Lightened(.2f), .9f);
        var spot = Glow(new Color(.75f, 1f, .55f), 2.2f); var gill = Glow(new Color(.9f, .55f, .9f), 1.1f);
        float x0 = r.X * Tile, z0 = r.Z * Tile, x1 = (r.X + r.W) * Tile, z1 = (r.Z + r.H) * Tile, cx = (x0 + x1) / 2, cz = (z0 + z1) / 2;
        int lights = 0;
        // open ground: the fighting ring around the boss, and the path from the passage to it
        bool Open(Vector3 p, float pad) => new Vector2(p.X - cx, p.Z - cz).Length() < 130f + pad || (Mathf.Abs(p.X - cx) < 45f + pad && p.Z > cz);
        Vector3 Spot(float edge) => new(x0 + edge + R() * (x1 - x0 - 2 * edge), 0, z0 + edge + R() * (z1 - z0 - 2 * edge));
        void Giant(Vector3 p, float h, float cr, int ci)
        {
            float lean = (R() - .5f) * .22f;
            Put(new CylinderMesh { TopRadius = .35f + cr * .12f, BottomRadius = .55f + cr * .2f, Height = h, RadialSegments = 12 }, stem, p + Vector3.Up * h / 2, new Vector3(lean, 0, lean * .6f));
            var top = p + new Vector3(Mathf.Sin(lean * .6f) * h * .5f, h, -Mathf.Sin(lean) * h * .5f);
            Put(new SphereMesh { Radius = cr, Height = cr * .9f, RadialSegments = 20, Rings = 6, IsHemisphere = true }, capMats[ci], top);
            Put(new CylinderMesh { TopRadius = cr * .96f, BottomRadius = cr * .3f, Height = cr * .06f, RadialSegments = 20 }, gill, top - Vector3.Up * cr * .03f);
            for (int k = 0; k < 6; k++) { float sa = R() * Mathf.Tau, sr = R() * cr * .7f; Put(new SphereMesh { Radius = cr * (.05f + R() * .05f), Height = cr * .06f, RadialSegments = 8, Rings = 3 }, spot, top + new Vector3(Mathf.Cos(sa) * sr, cr * .42f * (1 - sr / cr * .6f), Mathf.Sin(sa) * sr)); }
            if (lights < 36 && R() < .3f) { lights++; Root!.AddChild(new OmniLight3D { Position = top - Vector3.Up * cr * .3f, LightColor = capCols[ci].Lightened(.3f), OmniRange = cr * 5f, LightEnergy = 1.4f, ShadowEnabled = false }); }
        }
        // a ring of colossal mushrooms around the fighting ground, then a forest of them through the rest of the chamber
        for (int i = 0; i < 18; i++) { float a = i / 18f * Mathf.Tau + R() * .1f; var p = new Vector3(cx + Mathf.Cos(a) * (150f + R() * 25f), 0, cz + Mathf.Sin(a) * (150f + R() * 25f)); if (!Open(p, -20f)) Giant(p, 26f + R() * 12f, 9f + R() * 5f, i % capCols.Length); }
        for (int i = 0, made = 0; i < 900 && made < 170; i++) { var p = Spot(12f); if (Open(p, 20f)) continue; Giant(p, 8f + R() * 20f, 3f + R() * 7f, i % capCols.Length); made++; }
        // meadows of small mushrooms (knee to head high)
        for (int i = 0, made = 0; i < 1500 && made < 420; i++)
        {
            var c = Spot(6f); if (new Vector2(c.X - cx, c.Z - cz).Length() < 40f) continue; made++; var m = smallMats[i % smallMats.Length];
            for (int k = 0; k < 4; k++) { var q = c + new Vector3(R() * 4f - 2f, 0, R() * 4f - 2f); float s = 1f + R() * 2.5f; Put(new CylinderMesh { TopRadius = .05f * s, BottomRadius = .08f * s, Height = .6f * s, RadialSegments = 6 }, stem, q + Vector3.Up * .3f * s); Put(new SphereMesh { Radius = .28f * s, Height = .26f * s, RadialSegments = 10, Rings = 3, IsHemisphere = true }, m, q + Vector3.Up * .6f * s); }
        }
        // slime pools: glowing, slowly breathing, with bubbles rising
        var slime = new ShaderMaterial { Shader = new Shader { Code = @"shader_type spatial;
render_mode cull_disabled;
uniform vec4 col : source_color = vec4(.35, .9, .25, 1.0);
void vertex() { VERTEX.y += sin(TIME * 1.7 + VERTEX.x * 1.3 + VERTEX.z * .9) * .04; }
void fragment() {
	float w = sin(TIME * 2.0 + UV.x * 18.0) * sin(TIME * 1.3 + UV.y * 15.0);
	ALBEDO = col.rgb * (.55 + .25 * w); EMISSION = col.rgb * (.9 + .5 * w); ROUGHNESS = .15; METALLIC = .1;
}" } };
        var bubble = Glow(new Color(.5f, 1f, .35f), 1.6f);
        for (int i = 0, made = 0; i < 400 && made < 46; i++)
        {
            var p = Spot(30f); p.Y = .04f; if (Open(p, 15f)) continue; made++; float pr = 6f + R() * 20f;
            deco.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = pr, BottomRadius = pr, Height = .08f, RadialSegments = 32 }, MaterialOverride = slime, Position = p, Scale = new Vector3(1, 1, .6f + R() * .6f) });
            var pm = new ParticleProcessMaterial { EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, EmissionSphereRadius = pr * .8f, Direction = Vector3.Up, Spread = 10, InitialVelocityMin = .4f, InitialVelocityMax = 1.2f, Gravity = Vector3.Zero, ScaleMin = .6f, ScaleMax = 2.2f };
            Root.AddChild(new GpuParticles3D { Position = p, Amount = 30, Lifetime = 2f, ProcessMaterial = pm, VisibilityAabb = new Aabb(new Vector3(-pr, -1, -pr), new Vector3(pr * 2, 6, pr * 2)), DrawPass1 = new SphereMesh { Radius = .14f, Height = .28f, RadialSegments = 6, Rings = 3, Material = bubble } });
            if (lights < 60 && made % 2 == 0) { lights++; Root.AddChild(new OmniLight3D { Position = p + Vector3.Up * 2f, LightColor = new Color(.45f, 1f, .3f), OmniRange = pr * 3f, LightEnergy = 1f, ShadowEnabled = false }); }
        }
        // ooze: streaks down the chamber walls and dripping slime hanging from the vault
        var ooze = Glow(new Color(.4f, .85f, .25f), .9f);
        for (int i = 0; i < 280; i++)
        {
            int side = i % 4; float u = R(); float len = 6f + R() * (WallH - 8f);
            var p = side switch { 0 => new Vector3(x0 + .3f, WallH - len / 2, z0 + u * (z1 - z0)), 1 => new Vector3(x1 - .3f, WallH - len / 2, z0 + u * (z1 - z0)), 2 => new Vector3(x0 + u * (x1 - x0), WallH - len / 2, z0 + .3f), _ => new Vector3(x0 + u * (x1 - x0), WallH - len / 2, z1 - .3f) };
            if (side == 3 && Mathf.Abs(p.X - cx) < 30) continue;
            Put(new BoxMesh { Size = side < 2 ? new Vector3(.5f, len, 1f + R() * 3f) : new Vector3(1f + R() * 3f, len, .5f) }, ooze, p);
            Put(new SphereMesh { Radius = .8f, Height = 2f, RadialSegments = 8, Rings = 4 }, ooze, p - Vector3.Up * (len / 2 + .4f));
        }
        for (int i = 0; i < 180; i++)
        {
            var p = Spot(10f); p.Y = WallH; float len = 4f + R() * 14f;
            Put(new CylinderMesh { TopRadius = 1.2f + R() * 1.5f, BottomRadius = .05f, Height = len, RadialSegments = 8 }, ooze, p - Vector3.Up * len / 2);
        }
        Gfx.MergeChildren(deco);
        // a sickly green haze of light over the fighting ground and across the vault
        Root.AddChild(new OmniLight3D { Position = new Vector3(cx, WallH - 6, cz), LightColor = new Color(.55f, .9f, .45f), OmniRange = 260f, LightEnergy = 1.1f, ShadowEnabled = false });
        foreach (var (dx, dz) in new[] { (-350f, -300f), (350f, -300f), (-350f, 300f), (350f, 300f) })
            Root.AddChild(new OmniLight3D { Position = new Vector3(cx + dx, WallH - 8, cz + dz), LightColor = new Color(.5f, .8f, .5f), OmniRange = 300f, LightEnergy = .6f, ShadowEnabled = false });
    }

    /// <summary>
    /// Shades in a hall: a fixed number per hall (so every machine numbers the same monsters, whoever is in the party), each the
    /// mirror of one party member in turn, fighting with that hero's kind of weapon.
    /// </summary>
    static void SpawnShades(Room h, int hall, List<PlayerStats> heroes)
    {
        var rnd = new Random(Seed + 101 * hall); int n = hall == 1 ? 8 : 10;
        for (int i = 0; i < n; i++)
        {
            var hero = heroes[i % heroes.Count]; string wt = hero.WType();
            string type = wt == "bow" ? "shadeArcher" : wt is "wand" or "staff" ? "shadeCaster" : "shade";
            float x = (h.X + 1.5f + (float)rnd.NextDouble() * (h.W - 3)) * Tile, z = (h.Z + 1.5f + (float)rnd.NextDouble() * (h.H - 3)) * Tile;
            var e = new Enemy { Type = type, Level = (int)_baseLvl, FromDungeon = true, NoLeash = true, AnyRealm = true, ShadeOf = hero, Position = new Vector3(x, .4f, z) };
            if (Inst != 0) { e.NetIdx = NetList.Count; e.Puppet = Owner != Net.MyId; NetList.Add(e); }
            Root!.AddChild(e);
        }
    }
    /// <summary>The halls before the chamber: glowing mushrooms along the walls and a slime pool.</summary>
    static void HallDecor(Room h, int hall)
    {
        var rnd = new Random(Seed + 57 * hall); float R() => (float)rnd.NextDouble();
        var deco = new Node3D { Name = "HallDecor" + hall }; Root!.AddChild(deco);
        StandardMaterial3D Glow(Color c, float e) => new() { AlbedoColor = c, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = e, Roughness = .6f };
        var stem = Gfx.Tex(DMat(new Color(.82f, .78f, .66f)), "noise", .6f); Color[] cols = { new(.55f, .22f, .5f), new(.25f, .5f, .45f), new(.62f, .3f, .2f) };
        float x0 = h.X * Tile, z0 = h.Z * Tile, x1 = (h.X + h.W) * Tile, z1 = (h.Z + h.H) * Tile;
        for (int i = 0; i < 14; i++)
        {
            bool alongX = R() < .5f; float u = R(), side = R() < .5f ? 3f : -3f;
            var p = alongX ? new Vector3(x0 + 4 + u * (x1 - x0 - 8), 0, side > 0 ? z0 + 3 + R() * 4 : z1 - 3 - R() * 4) : new Vector3(side > 0 ? x0 + 3 + R() * 4 : x1 - 3 - R() * 4, 0, z0 + 4 + u * (z1 - z0 - 8));
            float ht = 3f + R() * 7f, cr = 1.2f + R() * 2.4f; var cap = Glow(cols[i % cols.Length], .5f);
            deco.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = .25f + cr * .12f, BottomRadius = .4f + cr * .18f, Height = ht, RadialSegments = 10 }, MaterialOverride = stem, Position = p + Vector3.Up * ht / 2 });
            deco.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = cr, Height = cr * .9f, RadialSegments = 16, Rings = 5, IsHemisphere = true }, MaterialOverride = cap, Position = p + Vector3.Up * ht });
            if (i % 5 == 0) Root.AddChild(new OmniLight3D { Position = p + Vector3.Up * (ht - .8f), LightColor = cols[i % cols.Length].Lightened(.3f), OmniRange = cr * 6f, LightEnergy = 1.3f, ShadowEnabled = false });
        }
        var pool = new Vector3((x0 + x1) / 2 + (R() - .5f) * 20f, .04f, (z0 + z1) / 2 + (R() - .5f) * 14f);
        deco.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 5f, BottomRadius = 5f, Height = .06f, RadialSegments = 24 }, MaterialOverride = Glow(new Color(.35f, .9f, .25f), 1.2f), Position = pool, Scale = new Vector3(1, 1, .7f) });
        Root.AddChild(new OmniLight3D { Position = pool + Vector3.Up * 2f, LightColor = new Color(.45f, 1f, .3f), OmniRange = 18f, LightEnergy = 1f, ShadowEnabled = false });
        Gfx.MergeChildren(deco);
    }

    /// <summary>Scarletta fell: a chest rises for every challenger on the floor. Each machine makes its own hero's chest openable; the others only show whose they are.</summary>
    static void LairBossDown(Enemy e)
    {
        var c = (e.GlobalPosition - Origin) with { Y = 0 };   // where she fell (the chamber is a kilometre wide)
        var names = new List<(string name, bool mine)> { (Game.D.Name, true) };
        if (Shared && Net.I != null) foreach (var id in Net.I.DunPeers()) names.Add((Net.I.NameOf(id), false));
        for (int i = 0; i < names.Count; i++)
        {
            float a = Mathf.Pi * (.15f + .7f * (names.Count == 1 ? .5f : i / (float)(names.Count - 1)));
            var at = c + new Vector3(Mathf.Cos(a) * 6f, 0, Mathf.Sin(a) * 6f);
            Root!.AddChild(new RaidChest { Mine = names[i].mine, OwnerName = names[i].name, Level = e.Level, Position = at, Rotation = new Vector3(0, -a + Mathf.Pi / 2, 0) });
        }
        Ui.Banner("Scarletta has fallen", names.Count > 1 ? "a chest for every challenger" : "her hoard is yours"); Sfx.Play("level");
        // every challenger (each on their own game): a great deal of experience, and the title Felled Angel
        var p = Player.Instance;
        if (p != null)
        {
            int xp = Mathf.RoundToInt(Game.D.XpNeed() * Tune.F("raid.bossXpLevels", 3f));   // about three levels' worth at your level
            p.AddXp(xp, p.GlobalPosition);
            if (Titles.Grant(Titles.FelledAngel)) Ui.Note($"Title earned: {Titles.Get(Titles.FelledAngel)!.Name}. Wear it from the Character menu.", new Color(1f, .95f, .75f));
        }
        Game.Save();
    }
}

/// <summary>The shrine on Lumen Aerie: challenge Scarletta alone, or with your party (everyone in the party can follow you in).</summary>
public partial class LairGate : Node3D, IInteractable
{
    public string Key = Dungeon.ScarlettaLair;
    public string Prompt => "Challenge Scarletta, The Rotting Blade" + (Net.Active && Net.I!.MyParty is { Members.Count: >= 2 } ? " (with your party)" : " (solo)");
    public bool CanInteract => !Dungeon.Active && !Arenas.Active && World.Realm == 0 && Player.Instance is { Dead: false };
    public override void _Ready() { AddToGroup("interact"); }
    public void Interact() => Dungeon.Enter(Key, 1);
}

/// <summary>A raid reward chest. Mine = this hero's (opens once: rare loot and a pair of Golden Wings); otherwise it only shows whose it is.</summary>
public partial class RaidChest : Node3D, IInteractable
{
    public bool Mine; public string OwnerName = ""; public int Level = 1; bool _open; Node3D _lid = null!;
    public string Prompt => Mine ? "Open your chest" : $"{OwnerName}'s chest (only they can open it)";
    public bool CanInteract => Mine && !_open;
    public override void _Ready()
    {
        AddToGroup("interact");
        var gold = new StandardMaterial3D { AlbedoColor = new Color(1f, .78f, .3f), Metallic = .9f, Roughness = .25f, EmissionEnabled = true, Emission = new Color(.6f, .4f, .1f), EmissionEnergyMultiplier = .4f };
        var dark = new StandardMaterial3D { AlbedoColor = new Color(.25f, .12f, .22f), Roughness = .6f };
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.5f, .8f, 1f) }, MaterialOverride = dark, Position = new Vector3(0, .4f, 0) });
        foreach (float x in new[] { -.68f, 0f, .68f }) AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(.1f, .84f, 1.04f) }, MaterialOverride = gold, Position = new Vector3(x, .4f, 0) });
        _lid = new Node3D { Position = new Vector3(0, .8f, -.5f) }; AddChild(_lid);
        _lid.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.5f, .32f, 1f) }, MaterialOverride = dark, Position = new Vector3(0, .16f, .5f) });
        _lid.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.54f, .08f, 1.04f) }, MaterialOverride = gold, Position = new Vector3(0, .32f, .5f) });
        AddChild(new OmniLight3D { LightColor = Mine ? new Color(1f, .82f, .4f) : new Color(.6f, .65f, .8f), OmniRange = 6f, LightEnergy = Mine ? 1.6f : .6f, Position = new Vector3(0, 1.4f, 0), ShadowEnabled = false });
        AddChild(new Label3D { Text = Mine ? "Your reward" : OwnerName + "'s reward", FontSize = 34, OutlineSize = 8, PixelSize = .006f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = new Vector3(0, 1.9f, 0), Modulate = Mine ? new Color(1f, .85f, .45f) : new Color(.75f, .8f, .95f) });
        Fx.Burst(GlobalPosition + Vector3.Up, new Color(1f, .82f, .35f), 24, 5f, .8f, .12f);
    }
    public void Interact()
    {
        if (!Mine || _open) return; _open = true; Sfx.Play("coin");
        var tw = _lid.CreateTween(); tw.TweenProperty(_lid, "rotation:x", -1.9f, .5f);
        var at = GlobalPosition + Vector3.Up * .6f;
        // rare loot: several rare to legendary pieces and the lair's own rolls, plus the wings
        int n = Tune.I("raid.chestItems", 5);
        for (int i = 0; i < n; i++) Loot.Drop(at, item: Items.Roll(Level, 4f, null, GD.Randf() < .25f ? 4 : GD.Randf() < .55f ? 3 : 2));
        Loot.Drop(at, gold: GD.RandRange(300, 500) + Level * 40);
        Loot.Drop(at, item: Wings.Make(Level));
        Fx.Burst(at + Vector3.Up * .4f, new Color(1f, .85f, .3f), 40, 6f, .9f, .12f); Ui.Note("The chest holds a pair of Golden Wings!", new Color(1f, .85f, .4f));
        Game.Save();
    }
}
