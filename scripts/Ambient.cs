using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>
/// Scenery life that follows the player (no gameplay): butterflies over meadows by day, fireflies at night,
/// drifting violet spores in the Shadow realm, and bird flocks circling high overhead. Tuning in data/ambient.json.
/// </summary>
public partial class Ambient : Node3D
{
    class Fly { public Node3D N = null!; public Node3D L = null!, R = null!; public Vector3 Home, Target; public float Ph, Speed; }
    class Bird { public Node3D N = null!; public Node3D L = null!, R = null!; public float Ang, Off, Ph; public Vector3 Centre; }
    readonly List<Fly> _flies = new(); readonly List<Bird> _birds = new();
    CpuParticles3D _fireflies = null!, _spores = null!;
    float _r = 22, _maxH = 14, _night = .45f, _bH = 34, _bR = 45, _bSpeed = .12f;
    readonly RandomNumberGenerator _rg = new();

    public override void _Ready()
    {
        using var doc = JsonUtil.Load("res://data/ambient.json"); var r = doc.RootElement;
        int nFly = 14, nFire = 90, nSpore = 70, flocks = 3, per = 6; float box = 22, sbox = 24; var cols = new List<Color>();
        if (r.TryGetProperty("butterflies", out var b)) { nFly = (int)JsonUtil.F(b, "count", 14); _r = JsonUtil.F(b, "radius", 22); _maxH = JsonUtil.F(b, "maxHeight", 14); if (b.TryGetProperty("colors", out var cs)) foreach (var c in cs.EnumerateArray()) cols.Add(Color.FromHtml(c.GetString() ?? "ffffff")); }
        if (r.TryGetProperty("fireflies", out var f)) { nFire = (int)JsonUtil.F(f, "amount", 90); box = JsonUtil.F(f, "box", 22); _night = JsonUtil.F(f, "night", .45f); }
        if (r.TryGetProperty("spores", out var s)) { nSpore = (int)JsonUtil.F(s, "amount", 70); sbox = JsonUtil.F(s, "box", 24); }
        if (r.TryGetProperty("birds", out var bd)) { flocks = (int)JsonUtil.F(bd, "flocks", 3); per = (int)JsonUtil.F(bd, "perFlock", 6); _bH = JsonUtil.F(bd, "height", 34); _bR = JsonUtil.F(bd, "radius", 45); _bSpeed = JsonUtil.F(bd, "speed", .12f); }
        if (cols.Count == 0) cols.Add(Colors.White);

        // butterflies: a body and two wing quads that flap
        var wing = new QuadMesh { Size = new Vector2(.26f, .22f), CenterOffset = new Vector3(.13f, 0, 0) };
        var body = new StandardMaterial3D { AlbedoColor = new Color(.1f, .08f, .06f) };
        for (int i = 0; i < nFly; i++)
        {
            var c = cols[i % cols.Count];
            var wm = new StandardMaterial3D { AlbedoColor = c, CullMode = BaseMaterial3D.CullModeEnum.Disabled, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = .25f };
            var n = new Node3D(); AddChild(n);
            n.AddChild(new MeshInstance3D { Mesh = new CapsuleMesh { Radius = .015f, Height = .1f }, MaterialOverride = body, Rotation = new Vector3(Mathf.Pi / 2, 0, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            var L = new Node3D(); var R = new Node3D(); n.AddChild(L); n.AddChild(R);
            L.AddChild(new MeshInstance3D { Mesh = wing, MaterialOverride = wm, Rotation = new Vector3(-Mathf.Pi / 2, 0, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            R.AddChild(new MeshInstance3D { Mesh = wing, MaterialOverride = wm, Rotation = new Vector3(-Mathf.Pi / 2, Mathf.Pi, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            _flies.Add(new Fly { N = n, L = L, R = R, Ph = _rg.Randf() * 10, Speed = _rg.RandfRange(1.2f, 2.2f) });
        }
        // birds: dark chevrons circling
        var bm = new StandardMaterial3D { AlbedoColor = new Color(.12f, .12f, .14f), CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        var bw = new QuadMesh { Size = new Vector2(.7f, .22f), CenterOffset = new Vector3(.35f, 0, 0) };
        for (int fl = 0; fl < flocks; fl++)
        {
            var centre = new Vector3(_rg.RandfRange(-60, 60), 0, _rg.RandfRange(-60, 60)); float a0 = _rg.Randf() * Mathf.Tau;
            for (int i = 0; i < per; i++)
            {
                var n = new Node3D(); AddChild(n); var L = new Node3D(); var R = new Node3D(); n.AddChild(L); n.AddChild(R);
                L.AddChild(new MeshInstance3D { Mesh = bw, MaterialOverride = bm, Rotation = new Vector3(-Mathf.Pi / 2, 0, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
                R.AddChild(new MeshInstance3D { Mesh = bw, MaterialOverride = bm, Rotation = new Vector3(-Mathf.Pi / 2, Mathf.Pi, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
                _birds.Add(new Bird { N = n, L = L, R = R, Ang = a0 - i * .07f, Off = (i % 2 == 0 ? 1 : -1) * (i / 2) * 1.6f, Ph = _rg.Randf() * 6, Centre = centre });
            }
        }
        // fireflies and spores: soft additive particles in a box that moves with the player
        CpuParticles3D Motes(int amount, float bx, Color col, float size, float life)
        {
            var g = new Gradient(); g.SetColor(0, new Color(col, 0)); g.SetColor(1, new Color(col, 0)); g.AddPoint(.3f, new Color(col, 1)); g.AddPoint(.7f, new Color(col, .9f));
            var p = new CpuParticles3D
            {
                Amount = amount, Lifetime = life, Emitting = false, LocalCoords = false, Preprocess = life, EmissionShape = CpuParticles3D.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(bx, 2.2f, bx),
                Gravity = new Vector3(0, .05f, 0), Direction = Vector3.Up, Spread = 180, InitialVelocityMin = .1f, InitialVelocityMax = .5f, ColorRamp = g,
                Mesh = new QuadMesh { Size = new Vector2(size, size), Material = Vfx.SpriteMat() }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(p); return p;
        }
        _fireflies = Motes(nFire, box, new Color(1f, .9f, .4f), .14f, 4f);
        _spores = Motes(nSpore, sbox, new Color(.8f, .45f, 1f), .2f, 6f);
        foreach (var fl in _flies) Reseat(fl, Player.Instance?.GlobalPosition ?? Vector3.Zero, true);
    }

    void Reseat(Fly f, Vector3 around, bool snap)
    {
        float a = _rg.Randf() * Mathf.Tau, d = _rg.RandfRange(4, _r); var p = around + new Vector3(Mathf.Cos(a) * d, 0, Mathf.Sin(a) * d);
        p.Y = World.GroundAt(p.X, p.Z) + _rg.RandfRange(.4f, 1.6f); f.Home = p; f.Target = p; if (snap) f.N.GlobalPosition = p;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta; var pl = Player.Instance; if (pl == null) return;
        bool over = !Dungeon.Active && World.I != null && World.I.Over.Visible; Visible = over; if (!over) { _fireflies.Emitting = _spores.Emitting = false; return; }
        var pp = pl.GlobalPosition; float t = (float)Time.GetTicksMsec() / 1000f;
        bool shadow = World.RealmT > .5f; float night = World.Night;
        // particles follow the player
        _fireflies.GlobalPosition = pp + Vector3.Up * 1.2f; _spores.GlobalPosition = pp + Vector3.Up * 1.5f;
        _fireflies.Emitting = !shadow && night > _night; _spores.Emitting = shadow;
        // butterflies: daytime in the Light, on low meadows only; they wander around a home point and get reseated near the player
        bool day = !shadow && night < .35f;
        foreach (var f in _flies)
        {
            f.N.Visible = day && f.Home.Y < _maxH + 2;
            if ((f.Home - pp).Length() > _r * 1.6f || !f.N.Visible && day) { Reseat(f, pp, true); continue; }
            if (!f.N.Visible) continue;
            f.Ph += dt; var cur = f.N.GlobalPosition;
            if ((f.Target - cur).Length() < .3f) { var o = new Vector3(_rg.RandfRange(-2.5f, 2.5f), 0, _rg.RandfRange(-2.5f, 2.5f)); f.Target = f.Home + o; f.Target.Y = World.GroundAt(f.Target.X, f.Target.Z) + _rg.RandfRange(.3f, 1.5f); }
            var dir = (f.Target - cur).Normalized(); var np = cur + dir * f.Speed * dt + new Vector3(0, Mathf.Sin(f.Ph * 9f) * .6f * dt, 0);
            f.N.GlobalPosition = np; if (dir.LengthSquared() > .01f) f.N.Rotation = new Vector3(0, Mathf.Atan2(-dir.X, -dir.Z), 0);
            float flap = Mathf.Sin(f.Ph * 28f) * 1.1f; f.L.Rotation = new Vector3(0, 0, flap + .2f); f.R.Rotation = new Vector3(0, 0, -flap - .2f);
        }
        // birds: wide circles high above, wings beating then gliding; hidden at night and in the Shadow
        bool birds = !shadow && night < .55f;
        foreach (var b in _birds)
        {
            b.N.Visible = birds; if (!birds) continue;
            b.Ang += _bSpeed * dt; b.Ph += dt;
            var c = new Vector3(pp.X + b.Centre.X * .5f, 0, pp.Z + b.Centre.Z * .5f); float rr = _bR + b.Off;
            var pos = c + new Vector3(Mathf.Cos(b.Ang) * rr, _bH + Mathf.Sin(b.Ang * 3 + b.Off) * 2f + Mathf.Abs(b.Off) * .4f, Mathf.Sin(b.Ang) * rr);
            b.N.GlobalPosition = pos; var tan = new Vector3(-Mathf.Sin(b.Ang), 0, Mathf.Cos(b.Ang)); b.N.Rotation = new Vector3(0, Mathf.Atan2(-tan.X, -tan.Z), -.25f);
            float beat = Mathf.Sin(b.Ph * 7f) > -.2f ? Mathf.Sin(b.Ph * 14f) * .6f : .1f; b.L.Rotation = new Vector3(0, 0, beat); b.R.Rotation = new Vector3(0, 0, -beat);
        }
    }
}
