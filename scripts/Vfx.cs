using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>
/// Spell and combat visuals made in code: soft glowing particles, sword trails, fire explosions, frost novas with ice shards,
/// healing pillars and jagged lightning. All effects free themselves.
/// </summary>
public static class Vfx
{
    static GradientTexture2D? _soft;
    /// <summary>Round soft-edged sprite texture (white centre fading to clear), generated once.</summary>
    public static GradientTexture2D Soft
    {
        get
        {
            if (_soft != null) return _soft;
            var g = new Gradient(); g.SetColor(0, new Color(1, 1, 1, 1)); g.SetColor(1, new Color(1, 1, 1, 0)); g.AddPoint(0.35f, new Color(1, 1, 1, .55f));
            return _soft = new GradientTexture2D { Gradient = g, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(.5f, .5f), FillTo = new Vector2(1f, .5f), Width = 64, Height = 64 };
        }
    }
    /// <summary>Billboard particle material: additive glow, or normal alpha for smoke.</summary>
    public static StandardMaterial3D SpriteMat(bool additive = true) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = additive ? BaseMaterial3D.BlendModeEnum.Add : BaseMaterial3D.BlendModeEnum.Mix,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, VertexColorUseAsAlbedo = true, AlbedoTexture = Soft, NoDepthTest = false,
    };
    static StandardMaterial3D Glow(Color c, float alpha = 1f, bool add = true) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = add ? BaseMaterial3D.BlendModeEnum.Add : BaseMaterial3D.BlendModeEnum.Mix,
        AlbedoColor = new Color(c, alpha), CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };
    static Gradient Ramp(params (float t, Color c)[] pts)
    {
        var g = new Gradient(); g.SetColor(0, pts[0].c); g.SetOffset(0, pts[0].t); g.SetColor(1, pts[^1].c); g.SetOffset(1, pts[^1].t);
        for (int i = 1; i < pts.Length - 1; i++) g.AddPoint(pts[i].t, pts[i].c); return g;
    }
    static Curve Shrink() { var c = new Curve(); c.AddPoint(new Vector2(0, 1)); c.AddPoint(new Vector2(1, 0)); return c; }
    static Curve Grow() { var c = new Curve(); c.AddPoint(new Vector2(0, .4f)); c.AddPoint(new Vector2(1, 1)); return c; }

    static void Later(Node n, float t) { var root = Game.Root; if (root == null) return; root.GetTree().CreateTimer(t, false).Timeout += () => { if (GodotObject.IsInstanceValid(n)) n.QueueFree(); }; }
    static T Add<T>(T n, Vector3 pos) where T : Node3D { Game.Root?.AddChild(n); n.GlobalPosition = pos; return n; }

    /// <summary>One-shot burst of soft glowing sprites.</summary>
    public static CpuParticles3D Sparks(Vector3 pos, Gradient ramp, int count, float speed, float life, float size, float gravity = -4f, bool additive = true, float spread = 180f, Vector3? dir = null, float emitR = 0.1f)
    {
        var p = new CpuParticles3D
        {
            Amount = count, Lifetime = life, OneShot = true, Explosiveness = .95f, Emitting = true, Direction = dir ?? Vector3.Up, Spread = spread,
            InitialVelocityMin = speed * .35f, InitialVelocityMax = speed, Gravity = new Vector3(0, gravity, 0), DampingMin = 1f, DampingMax = 3f,
            Mesh = new QuadMesh { Size = new Vector2(size, size), Material = SpriteMat(additive) }, ColorRamp = ramp, ScaleAmountMin = .5f, ScaleAmountMax = 1.3f, ScaleAmountCurve = additive ? Shrink() : Grow(),
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere, EmissionSphereRadius = emitR, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        Add(p, pos); Later(p, life + .4f); return p;
    }

    /// <summary>A light that flashes and fades.</summary>
    public static void Flash(Vector3 pos, Color c, float energy, float range, float time)
    {
        var l = Add(new OmniLight3D { LightColor = c, LightEnergy = energy, OmniRange = range, ShadowEnabled = false }, pos);
        l.CreateTween().TweenProperty(l, "light_energy", 0f, time).SetEase(Tween.EaseType.Out); Later(l, time + .05f);
    }

    /// <summary>Fireball impact: flash, expanding fire core, embers, rising smoke and a scorch ring.</summary>
    public static void Explosion(Vector3 pos, float radius)
    {
        Flash(pos + Vector3.Up * .5f, new Color(1f, .55f, .2f), 7f, radius * 3.5f, .45f);
        var core = Add(new MeshInstance3D { Mesh = new SphereMesh { Radius = 1, Height = 2, RadialSegments = 16, Rings = 8 }, MaterialOverride = Glow(new Color(1f, .6f, .2f), .9f), Scale = Vector3.One * .3f, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off }, pos);
        var tw = core.CreateTween().SetParallel(); tw.TweenProperty(core, "scale", Vector3.One * radius * .75f, .22f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        tw.TweenProperty(core.MaterialOverride, "albedo_color:a", 0f, .4f); Later(core, .45f);
        Sparks(pos, Ramp((0, new Color(1, .95f, .7f)), (.3f, new Color(1, .55f, .15f)), (1, new Color(.6f, .1f, .02f, 0))), 46, 11f, .8f, .5f, -6f);
        Sparks(pos + Vector3.Up * .4f, Ramp((0, new Color(.25f, .22f, .2f, 0)), (.2f, new Color(.2f, .18f, .17f, .55f)), (1, new Color(.3f, .3f, .3f, 0))), 14, 2.2f, 1.6f, 1.6f, 1.2f, false, 60f, null, radius * .3f);
        Ring(pos + Vector3.Up * .08f, radius, new Color(1f, .5f, .15f), .5f);
    }

    /// <summary>Flat ring on the ground that expands and fades.</summary>
    public static void Ring(Vector3 pos, float radius, Color c, float time)
    {
        var m = Add(new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = .82f, OuterRadius = 1f, Rings = 40, RingSegments = 4 }, MaterialOverride = Glow(c, .85f), Scale = new Vector3(.2f, .05f, .2f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off }, pos);
        var tw = m.CreateTween().SetParallel(); tw.TweenProperty(m, "scale", new Vector3(radius, .08f, radius), time).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quart);
        tw.TweenProperty(m.MaterialOverride, "albedo_color:a", 0f, time).SetDelay(time * .3f); Later(m, time + .05f);
    }

    /// <summary>Frost nova: ice shards burst out of the ground in a ring, frost disc, snow motes and a cold flash.</summary>
    public static void FrostNova(Vector3 pos, float radius)
    {
        Flash(pos + Vector3.Up, new Color(.5f, .85f, 1f), 5f, radius * 2.5f, .6f);
        Ring(pos + Vector3.Up * .1f, radius, new Color(.55f, .9f, 1f), .45f);
        var disc = Add(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 1, BottomRadius = 1, Height = .02f, RadialSegments = 40 }, MaterialOverride = Glow(new Color(.45f, .75f, 1f), .1f), Scale = new Vector3(radius, 1, radius), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off }, pos + Vector3.Up * .05f);
        disc.CreateTween().TweenProperty(disc.MaterialOverride, "albedo_color:a", 0f, 1.4f).SetDelay(.4f); Later(disc, 1.9f);
        var ice = new StandardMaterial3D { AlbedoColor = new Color(.7f, .92f, 1f, .85f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = .1f, Metallic = .1f, EmissionEnabled = true, Emission = new Color(.35f, .75f, 1f), EmissionEnergyMultiplier = 1.4f, RimEnabled = true, Rim = 1f };
        var shard = new CylinderMesh { TopRadius = 0, BottomRadius = .3f, Height = 1.7f, RadialSegments = 4, Rings = 1 };
        var rg = new RandomNumberGenerator();
        for (int i = 0; i < 22; i++)
        {
            float a = i / 22f * Mathf.Tau + rg.RandfRange(-.1f, .1f), r = radius * rg.RandfRange(.45f, .95f), s = rg.RandfRange(.6f, 1.5f);
            var p = pos + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r); p.Y = World.GroundAt(p.X, p.Z) > -5 && !Dungeon.Active ? World.GroundAt(p.X, p.Z) : pos.Y;
            var m = Add(new MeshInstance3D { Mesh = shard, MaterialOverride = ice, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off }, p);
            m.Rotation = new Vector3(Mathf.Sin(a) * .45f, 0, -Mathf.Cos(a) * .45f); // lean outward from the centre
            m.Scale = new Vector3(s, .01f, s);
            var tw = m.CreateTween(); tw.TweenProperty(m, "scale", new Vector3(s, s, s), .12f + i % 3 * .03f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
            tw.TweenInterval(.55f); tw.TweenProperty(m, "scale", new Vector3(s * .6f, .01f, s * .6f), .35f).SetEase(Tween.EaseType.In); Later(m, 1.2f);
        }
        Sparks(pos + Vector3.Up * .5f, Ramp((0, new Color(1, 1, 1, .7f)), (1, new Color(.5f, .85f, 1f, 0))), 30, 7f, .9f, .16f, -1f, true, 70f, Vector3.Up, radius * .4f);
    }

    /// <summary>Healing: a rising pillar of green-gold light with motes spiralling up.</summary>
    public static void HealPillar(Node3D follow)
    {
        var pos = follow.GlobalPosition;
        Flash(pos + Vector3.Up * 1.5f, new Color(.6f, 1f, .5f), 2.2f, 7f, .6f);
        // thin bright core beam inside a faint wide shell, both narrowing to the top and fading fast
        foreach (var (rad, a, t) in new[] { (.25f, .5f, .45f), (.85f, .14f, .6f) })
        {
            var col = Add(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = rad * .3f, BottomRadius = rad, Height = 4.5f, RadialSegments = 20, CapTop = false, CapBottom = false }, MaterialOverride = Glow(new Color(.6f, 1f, .5f), a), Scale = new Vector3(.3f, .2f, .3f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off }, pos + Vector3.Up * 2.25f);
            var tw = col.CreateTween().SetParallel(); tw.TweenProperty(col, "scale", new Vector3(1f, 1f, 1f), .18f).SetEase(Tween.EaseType.Out); tw.TweenProperty(col.MaterialOverride, "albedo_color:a", 0f, t).SetDelay(.12f); Later(col, t + .2f);
        }
        Ring(pos + Vector3.Up * .1f, 2.2f, new Color(1f, .9f, .45f), .6f);
        Sparks(pos + Vector3.Up * .3f, Ramp((0, new Color(1, 1, .7f)), (.5f, new Color(.5f, 1f, .5f)), (1, new Color(.4f, 1f, .4f, 0))), 36, 4f, 1.2f, .22f, 2.5f, true, 25f, Vector3.Up, .8f);
    }

    /// <summary>Jagged lightning between two points (core and glow), re-forking a few times before it fades.</summary>
    public static void Lightning(Vector3 a, Vector3 b, Color c, float time = .25f)
    {
        var bolt = new LightningBolt { A = a, B = b, Col = c, Life = time }; Game.Root?.AddChild(bolt);
        Flash(b, c, 3f, 6f, time); Sparks(b, Ramp((0, Colors.White), (1, new Color(c, 0))), 10, 6f, .3f, .18f, -3f);
    }
}

/// <summary>Ribbon of light that follows a blade (base and tip markers) while Emit is on, fading over Life seconds.</summary>
public partial class SwordTrail : MeshInstance3D
{
    public Node3D? Base, Tip; public bool Emit; public Color Col = new(.6f, .9f, 1f); public float Life = .22f;
    readonly List<(Vector3 a, Vector3 b, float t)> _pts = new(); readonly ImmediateMesh _im = new(); float _now;
    public override void _Ready()
    {
        Mesh = _im; TopLevel = true; GlobalTransform = Transform3D.Identity; CastShadow = ShadowCastingSetting.Off;
        MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add, VertexColorUseAsAlbedo = true, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
    }
    public override void _Process(double delta)
    {
        _now += (float)delta;
        if (Emit && Base != null && Tip != null) _pts.Add((Base.GlobalPosition, Tip.GlobalPosition, _now));
        while (_pts.Count > 0 && _now - _pts[0].t > Life) _pts.RemoveAt(0);
        _im.ClearSurfaces(); if (_pts.Count < 2) return;
        _im.SurfaceBegin(Mesh.PrimitiveType.TriangleStrip);
        foreach (var (a, b, t) in _pts)
        {
            float k = 1f - (_now - t) / Life; k *= k;
            _im.SurfaceSetColor(new Color(Col, .15f * k)); _im.SurfaceAddVertex(a);
            _im.SurfaceSetColor(new Color(Col.Lerp(Colors.White, .6f), 1.6f * k)); _im.SurfaceAddVertex(b.Lerp(b + (b - a) * .08f, 1f));
        }
        _im.SurfaceEnd();
    }
}

/// <summary>Camera-facing jagged bolt; re-randomises its path every 50 ms, then fades.</summary>
public partial class LightningBolt : MeshInstance3D
{
    public Vector3 A, B; public Color Col; public float Life = .25f;
    readonly ImmediateMesh _im = new(); float _t, _next; readonly RandomNumberGenerator _rg = new();
    public override void _Ready()
    {
        Mesh = _im; TopLevel = true; GlobalTransform = Transform3D.Identity; CastShadow = ShadowCastingSetting.Off;
        MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add, VertexColorUseAsAlbedo = true, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        Build();
    }
    public override void _Process(double delta)
    {
        _t += (float)delta; if (_t > Life) { QueueFree(); return; }
        if (_t > _next) Build();
    }
    void Build()
    {
        _next = _t + .05f; float fade = 1f - _t / Life;
        var cam = GetViewport()?.GetCamera3D(); var eye = cam?.GlobalPosition ?? A + Vector3.Up * 5;
        var d = B - A; float len = d.Length(); if (len < .05f) return; int n = Mathf.Clamp((int)(len / .55f), 3, 40);
        var pts = new Vector3[n + 1]; var side0 = d.Cross(Vector3.Up).Normalized(); if (side0 == Vector3.Zero) side0 = Vector3.Right; var up0 = side0.Cross(d / len);
        for (int i = 0; i <= n; i++)
        {
            float f = i / (float)n, env = Mathf.Sin(f * Mathf.Pi);
            pts[i] = A + d * f + (side0 * _rg.RandfRange(-1, 1) + up0 * _rg.RandfRange(-1, 1)) * .45f * env;
        }
        _im.ClearSurfaces();
        foreach (var (w, a) in new[] { (.32f, .35f), (.07f, 1f) })
        {
            _im.SurfaceBegin(Mesh.PrimitiveType.TriangleStrip);
            for (int i = 0; i <= n; i++)
            {
                var dir = (pts[Mathf.Min(i + 1, n)] - pts[Mathf.Max(i - 1, 0)]).Normalized(); var side = dir.Cross(eye - pts[i]).Normalized() * w;
                var c = a > .5f ? new Color(Colors.White.Lerp(Col, .2f), a * fade) : new Color(Col, a * fade);
                _im.SurfaceSetColor(c); _im.SurfaceAddVertex(pts[i] - side); _im.SurfaceSetColor(c); _im.SurfaceAddVertex(pts[i] + side);
            }
            _im.SurfaceEnd();
        }
    }
}
