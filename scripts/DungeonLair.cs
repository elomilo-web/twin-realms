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

    static void LairRooms()
    {
        _rooms.Add(new Room { X = 17, Z = 33, W = 7, H = 5, Cx = 20, Cz = 35 });   // entry hall (the exit pad)
        _rooms.Add(new Room { X = 7, Z = 4, W = 27, H = 25, Cx = 20, Cz = 16 });   // the great chamber
    }

    static void LairBuild(JsonElement th, int bossRoom)
    {
        var r = _rooms[bossRoom]; float cx = (r.Cx + .5f) * Tile, cz = (r.Cz + .5f) * Tile;
        LairDecor(r, th);
        // the boss: no rolled upgrades or gear tier (she reads the challengers instead, EnemyScarletta.cs)
        var e = new Enemy { Type = "scarletta", Level = (int)_baseLvl + Progression.DunBoss, FromDungeon = true, NoLeash = true, AnyRealm = true, Position = new Vector3(cx, 0.4f, cz - 6f) };
        if (Inst != 0) { e.NetIdx = NetList.Count; e.Puppet = Owner != Net.MyId; NetList.Add(e); }
        Root!.AddChild(e); _boss = e;
    }

    /// <summary>Giant glowing mushrooms along the walls, clusters of small ones, slime pools that bubble, and ooze running down the walls.</summary>
    static void LairDecor(Room r, JsonElement th)
    {
        var rnd = new Random(Seed + 31); float R() => (float)rnd.NextDouble();
        var deco = new Node3D { Name = "LairDecor" }; Root!.AddChild(deco);
        void Put(Mesh m, Material mt, Vector3 p, Vector3? rot = null, Vector3? scl = null) => deco.AddChild(new MeshInstance3D { Mesh = m, MaterialOverride = mt, Position = p, Rotation = rot ?? Vector3.Zero, Scale = scl ?? Vector3.One });
        var stem = Gfx.Tex(DMat(new Color(.82f, .78f, .66f)), "noise", .6f);
        Color[] capCols = { new(.55f, .22f, .5f), new(.25f, .5f, .45f), new(.62f, .3f, .2f), new(.35f, .25f, .55f) };
        StandardMaterial3D Glow(Color c, float e) => new() { AlbedoColor = c, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = e, Roughness = .6f };
        var spot = Glow(new Color(.75f, 1f, .55f), 2.2f); var gill = Glow(new Color(.9f, .55f, .9f), 1.1f);
        float x0 = r.X * Tile, z0 = r.Z * Tile, x1 = (r.X + r.W) * Tile, z1 = (r.Z + r.H) * Tile;
        // giant mushrooms: around the edge of the chamber, leaving the middle clear for the fight
        for (int i = 0; i < 26; i++)
        {
            float a = i / 26f * Mathf.Tau + R() * .15f, ex = (x1 - x0) / 2 - 3.5f - R() * 3f, ez = (z1 - z0) / 2 - 3.5f - R() * 3f;
            var p = new Vector3((x0 + x1) / 2 + Mathf.Cos(a) * ex, 0, (z0 + z1) / 2 + Mathf.Sin(a) * ez);
            if (p.Z > z1 - 8 && Mathf.Abs(p.X - (x0 + x1) / 2) < 8) continue;   // keep the way in clear
            float h = 4f + R() * 8f, cr = 1.6f + R() * 3f, lean = (R() - .5f) * .25f;
            var capMat = Glow(capCols[i % capCols.Length], .35f);
            Put(new CylinderMesh { TopRadius = .35f + cr * .12f, BottomRadius = .55f + cr * .18f, Height = h, RadialSegments = 10 }, stem, p + Vector3.Up * h / 2, new Vector3(lean, 0, lean * .6f));
            var top = p + new Vector3(Mathf.Sin(lean * .6f) * h * .5f, h, -Mathf.Sin(lean) * h * .5f);
            Put(new SphereMesh { Radius = cr, Height = cr * .9f, RadialSegments = 18, Rings = 6, IsHemisphere = true }, capMat, top);
            Put(new CylinderMesh { TopRadius = cr * .96f, BottomRadius = cr * .3f, Height = .25f, RadialSegments = 18 }, gill, top - Vector3.Up * .12f);
            for (int k = 0; k < 5; k++) { float sa = R() * Mathf.Tau, sr = R() * cr * .7f; Put(new SphereMesh { Radius = .18f + R() * .2f, Height = .2f, RadialSegments = 8, Rings = 3 }, spot, top + new Vector3(Mathf.Cos(sa) * sr, cr * .42f * (1 - sr / cr * .6f), Mathf.Sin(sa) * sr)); }
            if (i % 4 == 0) Root.AddChild(new OmniLight3D { Position = top - Vector3.Up * 1.2f, LightColor = capCols[i % capCols.Length].Lightened(.3f), OmniRange = cr * 4.5f, LightEnergy = 1.2f, ShadowEnabled = false });
        }
        // clusters of small mushrooms
        for (int i = 0; i < 40; i++)
        {
            var c = new Vector3(x0 + 2 + R() * (x1 - x0 - 4), 0, z0 + 2 + R() * (z1 - z0 - 4)); var m = Glow(capCols[i % capCols.Length].Lightened(.2f), .8f);
            for (int k = 0; k < 4; k++) { var q = c + new Vector3(R() * 1.6f - .8f, 0, R() * 1.6f - .8f); float s = .4f + R() * 1.1f; Put(new CylinderMesh { TopRadius = .05f * s, BottomRadius = .08f * s, Height = .6f * s, RadialSegments = 6 }, stem, q + Vector3.Up * .3f * s); Put(new SphereMesh { Radius = .28f * s, Height = .26f * s, RadialSegments = 10, Rings = 3, IsHemisphere = true }, m, q + Vector3.Up * .6f * s); }
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
        for (int i = 0; i < 9; i++)
        {
            float a = R() * Mathf.Tau, d = 6f + R() * 18f; var p = new Vector3((x0 + x1) / 2 + Mathf.Cos(a) * d, .03f, (z0 + z1) / 2 + Mathf.Sin(a) * d * .8f); float pr = 1.8f + R() * 2.8f;
            deco.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = pr, BottomRadius = pr, Height = .06f, RadialSegments = 24 }, MaterialOverride = slime, Position = p, Scale = new Vector3(1, 1, .7f + R() * .5f) });
            var pm = new ParticleProcessMaterial { EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, EmissionSphereRadius = pr * .8f, Direction = Vector3.Up, Spread = 10, InitialVelocityMin = .3f, InitialVelocityMax = .9f, Gravity = Vector3.Zero, ScaleMin = .4f, ScaleMax = 1.2f };
            Root.AddChild(new GpuParticles3D { Position = p, Amount = 14, Lifetime = 1.6f, ProcessMaterial = pm, DrawPass1 = new SphereMesh { Radius = .09f, Height = .18f, RadialSegments = 6, Rings = 3, Material = Glow(new Color(.5f, 1f, .35f), 1.6f) } });
            if (i % 2 == 0) Root.AddChild(new OmniLight3D { Position = p + Vector3.Up * .8f, LightColor = new Color(.45f, 1f, .3f), OmniRange = pr * 3.5f, LightEnergy = .9f, ShadowEnabled = false });
        }
        // ooze: streaks down the chamber walls and dripping slime hanging from the vault
        var ooze = Glow(new Color(.4f, .85f, .25f), .9f);
        for (int i = 0; i < 70; i++)
        {
            int side = i % 4; float u = R(); float len = 3f + R() * (WallH - 4f);
            var p = side switch { 0 => new Vector3(x0 + .1f, WallH - len / 2, z0 + u * (z1 - z0)), 1 => new Vector3(x1 - .1f, WallH - len / 2, z0 + u * (z1 - z0)), 2 => new Vector3(x0 + u * (x1 - x0), WallH - len / 2, z0 + .1f), _ => new Vector3(x0 + u * (x1 - x0), WallH - len / 2, z1 - .1f) };
            if (side == 3 && Mathf.Abs(p.X - (x0 + x1) / 2) < 7) continue;
            Put(new BoxMesh { Size = side < 2 ? new Vector3(.12f, len, .25f + R() * .5f) : new Vector3(.25f + R() * .5f, len, .12f) }, ooze, p);
            Put(new SphereMesh { Radius = .18f, Height = .45f, RadialSegments = 8, Rings = 4 }, ooze, p - Vector3.Up * (len / 2 + .1f));
        }
        for (int i = 0; i < 30; i++)
        {
            var p = new Vector3(x0 + 2 + R() * (x1 - x0 - 4), WallH, z0 + 2 + R() * (z1 - z0 - 4)); float len = 1.5f + R() * 4f;
            Put(new CylinderMesh { TopRadius = .35f + R() * .3f, BottomRadius = .02f, Height = len, RadialSegments = 8 }, ooze, p - Vector3.Up * len / 2);
        }
        Gfx.MergeChildren(deco);
        // a sickly green haze of light over the whole chamber
        Root.AddChild(new OmniLight3D { Position = new Vector3((x0 + x1) / 2, WallH - 3, (z0 + z1) / 2), LightColor = new Color(.55f, .9f, .45f), OmniRange = 60f, LightEnergy = .8f, ShadowEnabled = false });
    }

    /// <summary>Scarletta fell: a chest rises for every challenger on the floor. Each machine makes its own hero's chest openable; the others only show whose they are.</summary>
    static void LairBossDown(Enemy e)
    {
        var r = _rooms[_bossRoom]; var c = new Vector3((r.Cx + .5f) * Tile, 0, (r.Cz + .5f) * Tile);
        var names = new List<(string name, bool mine)> { (Game.D.Name, true) };
        if (Shared && Net.I != null) foreach (var id in Net.I.DunPeers()) names.Add((Net.I.NameOf(id), false));
        for (int i = 0; i < names.Count; i++)
        {
            float a = Mathf.Pi * (.15f + .7f * (names.Count == 1 ? .5f : i / (float)(names.Count - 1)));
            var at = c + new Vector3(Mathf.Cos(a) * 6f, 0, Mathf.Sin(a) * 6f);
            Root!.AddChild(new RaidChest { Mine = names[i].mine, OwnerName = names[i].name, Level = e.Level, Position = at, Rotation = new Vector3(0, -a + Mathf.Pi / 2, 0) });
        }
        Ui.Banner("Scarletta has fallen", names.Count > 1 ? "a chest for every challenger" : "her hoard is yours"); Sfx.Play("level");
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
