using System.Collections.Generic;
using Godot;

namespace TwinRealms;

public class MatDef { public string Id = "", Name = ""; public int Value; public Color Col = Colors.White; }
public class NodeDef { public string Id = "", Shape = "herb", Verb = "Gather", Mat = ""; public int A0 = 1, A1 = 1, Count, Realm = -1; public string Where = "meadow"; public float MinH, MaxH = 99, Respawn = 180; }

/// <summary>Materials and gatherable node types from data/gather.json, and the scattering of nodes over the island.</summary>
public static class GatherDb
{
    public static readonly Dictionary<string, MatDef> Mats = new();
    public static readonly List<NodeDef> Nodes = new();
    static int _seed = 9137;

    public static void Load()
    {
        if (Mats.Count > 0) return;
        using var doc = JsonUtil.Load("res://data/gather.json"); var r = doc.RootElement; _seed = (int)JsonUtil.F(r, "seed", 9137);
        foreach (var p in r.GetProperty("materials").EnumerateObject())
            Mats[p.Name] = new MatDef { Id = p.Name, Name = JsonUtil.S(p.Value, "n", p.Name), Value = (int)JsonUtil.F(p.Value, "value", 1), Col = Color.FromHtml(JsonUtil.S(p.Value, "color", "#ffffff")) };
        foreach (var p in r.GetProperty("nodes").EnumerateObject())
        {
            var e = p.Value; var d = new NodeDef { Id = p.Name, Shape = JsonUtil.S(e, "shape", "herb"), Verb = JsonUtil.S(e, "verb", "Gather"), Mat = JsonUtil.S(e, "mat"), Count = (int)JsonUtil.F(e, "count"), Where = JsonUtil.S(e, "where", "meadow"),
                MinH = JsonUtil.F(e, "minH"), MaxH = JsonUtil.F(e, "maxH", 99), Realm = (int)JsonUtil.F(e, "realm", -1), Respawn = JsonUtil.F(e, "respawn", 180) };
            if (e.TryGetProperty("amount", out var a)) { d.A0 = a[0].GetInt32(); d.A1 = a[1].GetInt32(); }
            Nodes.Add(d);
        }
    }
    public static MatDef Mat(string id) { Load(); return Mats.TryGetValue(id, out var m) ? m : new MatDef { Id = id, Name = id }; }

    /// <summary>Gives the player materials, with a floating "+2 Sunleaf" and a note.</summary>
    public static void Give(string mat, int n, Vector3 at)
    {
        if (n <= 0) return; var m = Mat(mat); var d = Game.D.Mats; d[mat] = d.GetValueOrDefault(mat) + n;
        FloatText.Spawn(Game.Root ?? (Node)Player.Instance!, at + Vector3.Up * 1.2f, $"+{n} {m.Name}", m.Col.Lightened(0.25f), 0.85f);
        Ui.Note($"+{n} {m.Name}  ({d[mat]})", m.Col.Lightened(0.3f)); Game.Notify();
    }

    public static void Spawn(Node3D parent)
    {
        Load(); var rg = World.Rng((uint)_seed); float R(float a, float b) => a + rg() * (b - a);
        var holder = new Node3D { Name = "Gatherables" }; parent.AddChild(holder); int idx = 0;
        foreach (var d in Nodes)
        {
            int placed = 0, tries = 0;
            while (placed < d.Count && tries++ < d.Count * 40)
            {
                float x = R(-World.Half + 16, World.Half - 16), z = R(-World.Half + 16, World.Half - 16), h = World.GroundAt(x, z);
                if (h < d.MinH || h > d.MaxH || !World.FreeSpot(x, z, 2f)) continue;
                float slope = Mathf.Max(Mathf.Abs(World.GroundAt(x + 1, z) - World.GroundAt(x - 1, z)), Mathf.Abs(World.GroundAt(x, z + 1) - World.GroundAt(x, z - 1)));
                bool forest = World.Fbm(x * .02f + 3, z * .02f + 9, 3) >= .42f;
                bool ok = d.Where switch { "forest" => forest && slope < 1.2f, "meadow" => !forest && slope < 1.0f, "rock" => slope > 0.6f && slope < 2.6f, "mountain" => slope < 2.8f, _ => true };
                if (!ok) continue;
                var g = new Gatherable { Def = d, Name = "G" + idx++, Position = new Vector3(x, h - 0.05f, z), Rotation = new Vector3(0, R(0, Mathf.Tau), 0) };
                holder.AddChild(g); placed++;
            }
        }
    }
}

/// <summary>A plant, mushroom patch, berry bush, ore vein, crystal or fallen log. Press E to collect; it regrows after a while.</summary>
public partial class Gatherable : Node3D, IInteractable
{
    public NodeDef Def = new();
    Node3D _yield = null!; float _regrow; bool _ready = true; OmniLight3D? _glow;
    public string Prompt => $"{Def.Verb} {GatherDb.Mat(Def.Mat).Name}";
    public bool CanInteract => _ready && RealmOk;
    bool RealmOk => Def.Realm < 0 || (Def.Realm == 1) == (World.RealmT > 0.5f);

    // shared materials, one set for all nodes (realm-aware through World.Mat)
    static StandardMaterial3D? _leaf, _stem, _flower, _petal, _cap, _dot, _bush, _berry, _rock, _ore, _cry, _bark, _wood;
    static StandardMaterial3D Glow(Color c, float e) { var m = World.Mat(c); m.EmissionEnabled = true; m.Emission = c; m.EmissionEnergyMultiplier = e; return m; }
    static void Mats()
    {
        if (_leaf != null) return;
        _leaf = World.Mat(new Color(.3f, .62f, .22f)); _stem = World.Mat(new Color(.36f, .55f, .2f)); _flower = Glow(new Color(1f, .9f, .25f), .35f);
        _petal = Glow(new Color(.75f, .5f, 1f), 2.2f); _cap = World.Mat(new Color(.82f, .2f, .16f)); _dot = World.Mat(new Color(.97f, .94f, .88f));
        _bush = World.Mat(new Color(.24f, .45f, .2f)); _berry = Glow(new Color(.85f, .1f, .3f), .25f); _rock = World.Mat(new Color(.5f, .49f, .47f), null, 1f);
        _ore = World.Mat(new Color(.72f, .43f, .22f), null, .45f, .6f); _cry = Glow(new Color(.37f, .88f, 1f), 1.6f); _cry.Transparency = BaseMaterial3D.TransparencyEnum.Alpha; _cry.AlbedoColor = new Color(.37f, .88f, 1f, .8f);
        _bark = World.Mat(new Color(.36f, .25f, .15f)); _wood = World.Mat(new Color(.7f, .55f, .35f));
    }

    // parts are collected, then merged into one mesh per (base/yield, material) so a node costs 1-3 draw calls, not 20
    readonly List<(Node3D target, Mesh m, Material mat, Transform3D t)> _parts = new();
    void Part(Node3D parent, Mesh m, Material mat, Vector3 pos, Vector3? rot = null, Vector3? scale = null)
        => _parts.Add((parent, m, mat, new Transform3D(Basis.FromEuler(rot ?? Vector3.Zero) * Basis.FromScale(scale ?? Vector3.One), pos)));
    void Bake()
    {
        var groups = new Dictionary<(Node3D, Material), SurfaceTool>();
        foreach (var (target, m, mat, t) in _parts) { if (!groups.TryGetValue((target, mat), out var st)) groups[(target, mat)] = st = new SurfaceTool(); st.AppendFrom(m, 0, t); }
        foreach (var kv in groups) kv.Key.Item1.AddChild(new MeshInstance3D { Mesh = kv.Value.Commit(), MaterialOverride = kv.Key.Item2, VisibilityRangeEnd = 110f, VisibilityRangeEndMargin = 10f });
        _parts.Clear();
    }
    void Solid(float r, float h) { var b = new StaticBody3D { CollisionLayer = 1 }; b.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = r, Height = h }, Position = new Vector3(0, h / 2, 0) }); AddChild(b); }

    public override void _Ready()
    {
        Mats(); AddToGroup("interact"); AddToGroup("gather");
        var bas = new Node3D { Name = "Base" }; AddChild(bas); _yield = new Node3D { Name = "Yield" }; AddChild(_yield);
        var rnd = new System.Random(Name.GetHashCode()); float F() => (float)rnd.NextDouble();
        switch (Def.Shape)
        {
            case "herb":
            case "herbGlow":
            {
                bool glow = Def.Shape == "herbGlow";
                for (int i = 0; i < 6; i++) { float a = i / 6f * Mathf.Tau; Part(_yield, new PrismMesh { Size = new Vector3(.12f, .38f, .02f) }, _leaf!, new Vector3(Mathf.Cos(a) * .08f, .17f, Mathf.Sin(a) * .08f), new Vector3(Mathf.Sin(a) * .5f, -a, Mathf.Cos(a) * .5f)); }
                for (int i = 0; i < 3; i++)
                {
                    var p = new Vector3((F() - .5f) * .25f, .42f + F() * .18f, (F() - .5f) * .25f);
                    Part(_yield, new CylinderMesh { TopRadius = .012f, BottomRadius = .015f, Height = p.Y }, _stem!, new Vector3(p.X, p.Y / 2, p.Z));
                    Part(_yield, new SphereMesh { Radius = glow ? .07f : .06f, Height = glow ? .1f : .12f, RadialSegments = 6, Rings = 3 }, glow ? _petal! : _flower!, p);
                }
                if (glow) { _glow = new OmniLight3D { LightColor = new Color(.7f, .45f, 1f), OmniRange = 3.5f, LightEnergy = .8f, Position = new Vector3(0, .5f, 0), DistanceFadeEnabled = true, DistanceFadeBegin = 35f, DistanceFadeLength = 10f }; _yield.AddChild(_glow); }
                break;
            }
            case "mushroom":
                for (int i = 0; i < 4; i++)
                {
                    float s = .6f + F() * .7f; var p = new Vector3((F() - .5f) * .7f, 0, (F() - .5f) * .7f);
                    Part(_yield, new CylinderMesh { TopRadius = .04f * s, BottomRadius = .055f * s, Height = .22f * s }, _dot!, p + new Vector3(0, .11f * s, 0));
                    Part(_yield, new SphereMesh { Radius = .14f * s, Height = .14f * s, RadialSegments = 8, Rings = 3, IsHemisphere = true }, _cap!, p + new Vector3(0, .21f * s, 0));
                    for (int k = 0; k < 3; k++) { float a = F() * Mathf.Tau; Part(_yield, new SphereMesh { Radius = .018f * s, Height = .036f * s, RadialSegments = 4, Rings = 2 }, _dot!, p + new Vector3(Mathf.Cos(a) * .08f * s, .3f * s, Mathf.Sin(a) * .08f * s)); }
                }
                break;
            case "berry":
                Part(bas, new SphereMesh { Radius = .55f, Height = .9f, RadialSegments = 8, Rings = 4 }, _bush!, new Vector3(0, .42f, 0));
                Part(bas, new SphereMesh { Radius = .4f, Height = .7f, RadialSegments = 8, Rings = 4 }, _bush!, new Vector3(.38f, .32f, .1f));
                for (int i = 0; i < 18; i++) { float a = F() * Mathf.Tau, y = .2f + F() * .6f, rr = .5f + (1f - Mathf.Abs(y - .45f) * 1.6f) * .1f; Part(_yield, new SphereMesh { Radius = .075f, Height = .15f, RadialSegments = 6, Rings = 3 }, _berry!, new Vector3(Mathf.Cos(a) * rr + (Mathf.Cos(a) > 0 ? .12f : 0), y, Mathf.Sin(a) * rr)); }
                break;
            case "ore":
                Part(bas, new SphereMesh { Radius = .8f, Height = 1.1f, RadialSegments = 7, Rings = 4 }, _rock!, new Vector3(0, .35f, 0), new Vector3(F(), F() * 3, F() * .5f), new Vector3(1.2f, .9f, 1f));
                for (int i = 0; i < 10; i++) { float a = i / 10f * Mathf.Tau + F() * .4f, y = .15f + F() * .65f, k = Mathf.Sqrt(Mathf.Max(0.05f, 1f - Mathf.Pow((y - .35f) / .55f, 2))); Part(_yield, new BoxMesh { Size = new Vector3(.3f, .2f, .22f) }, _ore!, new Vector3(Mathf.Cos(a) * .9f * k, y, Mathf.Sin(a) * .75f * k), new Vector3(F() * 2, a, F() * 2)); }
                Solid(.85f, 1.1f);
                break;
            case "crystal":
                Part(bas, new SphereMesh { Radius = .5f, Height = .5f, RadialSegments = 7, Rings = 3 }, _rock!, new Vector3(0, .1f, 0));
                for (int i = 0; i < 5; i++) { float a = i / 5f * Mathf.Tau, tilt = .25f + F() * .35f, len = .7f + F() * .8f; Part(_yield, new CylinderMesh { TopRadius = 0f, BottomRadius = .13f, Height = len, RadialSegments = 5, Rings = 1 }, _cry!, new Vector3(Mathf.Cos(a) * .18f, len / 2, Mathf.Sin(a) * .18f), new Vector3(Mathf.Sin(a) * tilt, 0, -Mathf.Cos(a) * tilt)); }
                _glow = new OmniLight3D { LightColor = new Color(.4f, .85f, 1f), OmniRange = 5f, LightEnergy = 1f, Position = new Vector3(0, .8f, 0), DistanceFadeEnabled = true, DistanceFadeBegin = 45f, DistanceFadeLength = 10f }; _yield.AddChild(_glow);
                Solid(.5f, 1.2f);
                break;
            case "log":
                Part(_yield, new CylinderMesh { TopRadius = .28f, BottomRadius = .32f, Height = 2.6f, RadialSegments = 8 }, _bark!, new Vector3(0, .3f, 0), new Vector3(0, 0, Mathf.Pi / 2));
                Part(_yield, new CylinderMesh { TopRadius = .26f, BottomRadius = .26f, Height = .02f, RadialSegments = 8 }, _wood!, new Vector3(1.31f, .3f, 0), new Vector3(0, 0, Mathf.Pi / 2));
                Part(_yield, new CylinderMesh { TopRadius = .05f, BottomRadius = .08f, Height = .7f, RadialSegments = 5 }, _bark!, new Vector3(.3f, .62f, .1f), new Vector3(.5f, 0, -.4f));
                break;
        }
        Bake(); UpdateRealm(true);
    }

    bool _lastRealm;
    void UpdateRealm(bool force) { bool ok = RealmOk; if (force || ok != _lastRealm) { _lastRealm = ok; Visible = ok; } }

    public override void _Process(double delta)
    {
        UpdateRealm(false);
        if (_ready) { if (_glow != null) _glow.LightEnergy = .8f + Mathf.Sin(World.Clock * 2.5f + Position.X) * .3f; return; }
        _regrow -= (float)delta;
        if (_regrow <= 0) { _ready = true; _yield.Visible = true; _yield.Scale = Vector3.One * 0.05f; _yield.CreateTween().TweenProperty(_yield, "scale", Vector3.One, 0.6f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out); }
    }

    public void Interact()
    {
        if (!CanInteract) return;
        int n = Def.A0 + (int)(GD.Randi() % (uint)(Def.A1 - Def.A0 + 1));
        var m = GatherDb.Mat(Def.Mat);
        Sfx.Play(Def.Shape == "ore" || Def.Shape == "crystal" || Def.Shape == "log" ? "hit" : "click");
        Fx.Burst(GlobalPosition + Vector3.Up * 0.5f, m.Col, 12, 3f, 0.5f, 0.06f);
        _ready = false; _regrow = Def.Respawn;
        var tw = _yield.CreateTween(); tw.TweenProperty(_yield, "scale", Vector3.One * 0.05f, 0.18f); tw.TweenCallback(Callable.From(() => _yield.Visible = false));
        GatherDb.Give(Def.Mat, n, GlobalPosition);
    }
}
