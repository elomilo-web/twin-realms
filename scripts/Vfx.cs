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
    /// <summary>
    /// Particle looks: sets of white-on-clear textures from Kenney's Particle Pack (CC0, assets/vfx/particles/), tinted by each effect's colours.
    /// "" = the plain soft dot made in code. A set picks one of its textures at random per effect, so repeated bursts do not look stamped.
    /// </summary>
    public static readonly Dictionary<string, string[]> Sets = new()
    {
        ["fire"] = new[] { "flame_01", "flame_02", "flame_03", "flame_04", "fire_01", "fire_02" },
        ["smoke"] = new[] { "smoke_01", "smoke_02", "smoke_03", "smoke_04", "smoke_05" },
        ["spark"] = new[] { "spark_01", "spark_02", "spark_03", "spark_04" },
        ["star"] = new[] { "star_01", "star_02", "star_04", "star_06" },
        ["magic"] = new[] { "magic_01", "magic_02", "magic_03", "magic_04", "magic_05" },
        ["twirl"] = new[] { "twirl_01", "twirl_02", "twirl_03" },
        ["light"] = new[] { "light_01", "light_02", "light_03" },
        ["scorch"] = new[] { "scorch_01", "scorch_02", "scorch_03" },
        ["dirt"] = new[] { "dirt_01", "dirt_02", "dirt_03" },
        ["circle"] = new[] { "circle_02", "circle_03", "circle_05" },
        ["slash"] = new[] { "slash_01", "slash_02", "slash_03" },
    };
    static readonly Dictionary<string, Texture2D?> _texCache = new();
    /// <summary>A random texture of a set (or Soft when the set is "" or its files are missing).</summary>
    public static Texture2D Tex(string set)
    {
        if (set == "" || !Sets.TryGetValue(set, out var names)) return Soft;
        string n = names[(int)(GD.Randi() % (uint)names.Length)];
        if (!_texCache.TryGetValue(n, out var t)) { string p = $"res://assets/vfx/particles/{n}.png"; _texCache[n] = t = ResourceLoader.Exists(p) ? GD.Load<Texture2D>(p) : null; }
        return t ?? Soft;
    }
    static readonly Dictionary<(Texture2D, bool), StandardMaterial3D> _mats = new();
    /// <summary>Billboard particle material: additive glow, or normal alpha for smoke. set = a texture set (see Sets). Shared per texture.</summary>
    public static StandardMaterial3D SpriteMat(bool additive = true, string set = "")
    {
        var tex = Tex(set);
        if (_mats.TryGetValue((tex, additive), out var m)) return m;
        return _mats[(tex, additive)] = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = additive ? BaseMaterial3D.BlendModeEnum.Add : BaseMaterial3D.BlendModeEnum.Mix,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, VertexColorUseAsAlbedo = true, AlbedoTexture = tex, NoDepthTest = false,
        };
    }
    /// <summary>Particles drawn with a texture set turn each sprite a random way (a field of identical flames looks fake).</summary>
    public static void Spin(CpuParticles3D p, string set, float spin = 0f)
    {
        if (set == "") return;
        p.AngleMin = 0; p.AngleMax = 360; if (spin != 0) { p.AngularVelocityMin = -spin; p.AngularVelocityMax = spin; }
    }

    /// <summary>A texture lying flat on the ground (scorch mark, frost rime, rune circle) that fades out. Lit like the ground around it.</summary>
    public static void GroundMark(Vector3 pos, string set, float size, Color c, float hold, float fade, bool glow = false)
    {
        if (!Dungeon.Active && World.GroundAt(pos.X, pos.Z) > -50f) pos.Y = Mathf.Max(pos.Y, World.GroundAt(pos.X, pos.Z));
        var mat = new StandardMaterial3D
        {
            AlbedoTexture = Tex(set), AlbedoColor = c, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 1f, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            ShadingMode = glow ? BaseMaterial3D.ShadingModeEnum.Unshaded : BaseMaterial3D.ShadingModeEnum.PerPixel, BlendMode = glow ? BaseMaterial3D.BlendModeEnum.Add : BaseMaterial3D.BlendModeEnum.Mix,
        };
        // tilted to the slope (sampled across the mark) and lifted over the grass, so hillsides and meadows do not swallow it
        var up = Vector3.Up; float lift = .06f;
        if (!Dungeon.Active && World.GroundAt(pos.X, pos.Z) > -50f)
        {
            float e = size * .4f, hx = World.GroundAt(pos.X + e, pos.Z) - World.GroundAt(pos.X - e, pos.Z), hz = World.GroundAt(pos.X, pos.Z + e) - World.GroundAt(pos.X, pos.Z - e);
            up = new Vector3(-hx, 2 * e, -hz).Normalized(); lift = .18f;
        }
        var m = Add(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(size, size) }, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off }, pos + up * lift);
        float spin = GD.Randf() * Mathf.Tau; var fwd = up.Cross(new Vector3(Mathf.Cos(spin), 0, Mathf.Sin(spin))).Normalized();
        if (fwd.LengthSquared() > .5f) m.Basis = Basis.LookingAt(fwd, up);
        m.CreateTween().TweenProperty(mat, "albedo_color:a", 0f, fade).SetDelay(hold); Later(m, hold + fade + .1f);
    }
    static StandardMaterial3D Glow(Color c, float alpha = 1f, bool add = true) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = add ? BaseMaterial3D.BlendModeEnum.Add : BaseMaterial3D.BlendModeEnum.Mix,
        AlbedoColor = new Color(c, alpha), CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };
    public static Gradient Ramp(params (float t, Color c)[] pts)
    {
        var g = new Gradient(); g.SetColor(0, pts[0].c); g.SetOffset(0, pts[0].t); g.SetColor(1, pts[^1].c); g.SetOffset(1, pts[^1].t);
        for (int i = 1; i < pts.Length - 1; i++) g.AddPoint(pts[i].t, pts[i].c); return g;
    }
    static Curve Shrink() { var c = new Curve(); c.AddPoint(new Vector2(0, 1)); c.AddPoint(new Vector2(1, 0)); return c; }
    static Curve Grow() { var c = new Curve(); c.AddPoint(new Vector2(0, .4f)); c.AddPoint(new Vector2(1, 1)); return c; }

    static void Later(Node n, float t) { var root = Game.Root; if (root == null) return; root.GetTree().CreateTimer(t, false).Timeout += () => { if (GodotObject.IsInstanceValid(n)) n.QueueFree(); }; }
    static T Add<T>(T n, Vector3 pos) where T : Node3D { Game.Root?.AddChild(n); n.GlobalPosition = pos; return n; }

    /// <summary>One-shot burst of soft glowing sprites.</summary>
    public static CpuParticles3D Sparks(Vector3 pos, Gradient ramp, int count, float speed, float life, float size, float gravity = -4f, bool additive = true, float spread = 180f, Vector3? dir = null, float emitR = 0.1f, string set = "")
    {
        var p = new CpuParticles3D
        {
            Amount = count, Lifetime = life, OneShot = true, Explosiveness = .95f, Emitting = true, Direction = dir ?? Vector3.Up, Spread = spread,
            InitialVelocityMin = speed * .35f, InitialVelocityMax = speed, Gravity = new Vector3(0, gravity, 0), DampingMin = 1f, DampingMax = 3f,
            Mesh = new QuadMesh { Size = new Vector2(size, size), Material = SpriteMat(additive, set) }, ColorRamp = ramp, ScaleAmountMin = .5f, ScaleAmountMax = 1.3f, ScaleAmountCurve = set is "smoke" or "fire" || (set == "" && !additive) ? Grow() : Shrink(),   // smoke and flames swell; sparks, stars and wisps shrink away
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere, EmissionSphereRadius = emitR, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        Spin(p, set, set is "smoke" or "fire" ? 90f : 0f);
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
        var core = Add(new MeshInstance3D { Mesh = new SphereMesh { Radius = 1, Height = 2, RadialSegments = 16, Rings = 8 }, MaterialOverride = Glow(new Color(1f, .6f, .2f), .25f), Scale = Vector3.One * .3f, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off }, pos);
        var tw = core.CreateTween().SetParallel(); tw.TweenProperty(core, "scale", Vector3.One * radius * .75f, .22f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        tw.TweenProperty(core.MaterialOverride, "albedo_color:a", 0f, .4f); Later(core, .45f);
        Sparks(pos, Ramp((0, new Color(1, .82f, .4f)), (.3f, new Color(1, .5f, .12f)), (1, new Color(.5f, .08f, .02f, 0))), 30, 7f, .7f, radius * .45f + .4f, 1.5f, false, 180f, null, radius * .25f, "fire");   // rolling flames (unshaded, normal blend: they read on bright ground too)
        Sparks(pos, Ramp((0, new Color(1, .95f, .7f)), (.4f, new Color(1, .6f, .2f)), (1, new Color(.6f, .1f, .02f, 0))), 26, 13f, .55f, .35f, -9f, true, 180f, null, .1f, "spark");   // flying embers
        Sparks(pos + Vector3.Up * .4f, Ramp((0, new Color(.25f, .22f, .2f, 0)), (.2f, new Color(.2f, .18f, .17f, .55f)), (1, new Color(.3f, .3f, .3f, 0))), 12, 2.2f, 1.8f, 2.2f, 1.2f, false, 60f, null, radius * .3f, "smoke");
        Ring(pos + Vector3.Up * .08f, radius, new Color(1f, .5f, .15f), .5f);
        GroundMark(pos, "scorch", radius * 1.6f, new Color(.08f, .06f, .05f, .85f), 4f, 3f);   // a burnt patch that lingers
        Aftermath(pos, radius);
    }

    // ---- fire aftermath: scorch decal, heat shimmer, lingering embers and smoke ----
    static ImageTexture? _scorchTex;
    static Shader? _shimmerShader;
    static QuadMesh? _shimmerMesh;
    static readonly Queue<Decal> _scorches = new();
    const int MaxScorches = 12;

    static ImageTexture ScorchTex()
    {
        if (_scorchTex != null) return _scorchTex;
        const int N = 128; var img = Image.CreateEmpty(N, N, false, Image.Format.Rgba8);
        var noise = new FastNoiseLite { Frequency = .05f, Seed = 7 };
        for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
        {
            float dx = (x + .5f) / N * 2 - 1, dy = (y + .5f) / N * 2 - 1, d = Mathf.Sqrt(dx * dx + dy * dy);
            float edge = .72f + noise.GetNoise2D(x * 2.2f, y * 2.2f) * .28f;   // ragged rim
            float a = Mathf.Clamp((edge - d) / .35f, 0f, 1f); a *= a * (.75f + .25f * noise.GetNoise2D(x * 5f + 40, y * 5f));
            float soot = .02f + .05f * (noise.GetNoise2D(x * 3f, y * 3f) * .5f + .5f);
            img.SetPixel(x, y, new Color(soot, soot * .85f, soot * .75f, Mathf.Clamp(a, 0f, 1f)));
        }
        return _scorchTex = ImageTexture.CreateFromImage(img);
    }

    /// <summary>Burn mark on the ground that fades over ~6 s. At most 12 alive; the oldest is recycled.</summary>
    public static void ScorchDecal(Vector3 pos, float radius)
    {
        while (_scorches.Count > 0 && (!GodotObject.IsInstanceValid(_scorches.Peek()) || _scorches.Count >= MaxScorches))
        { var old = _scorches.Dequeue(); if (GodotObject.IsInstanceValid(old)) old.QueueFree(); }
        float gy = World.GroundAt(pos.X, pos.Z); if (!Dungeon.Active && gy > -50f) pos.Y = gy;
        float d = Mathf.Max(1.5f, radius * 2.2f);
        var dec = new Decal { TextureAlbedo = ScorchTex(), Size = new Vector3(d, 3f, d), CullMask = 1, LowerFade = .3f, UpperFade = .3f };
        Add(dec, pos); dec.Rotation = new Vector3(0, GD.Randf() * Mathf.Tau, 0); _scorches.Enqueue(dec);
        dec.CreateTween().TweenProperty(dec, "modulate:a", 0f, 6f).SetDelay(.5f).SetEase(Tween.EaseType.In);
        Later(dec, 6.8f);
    }

    /// <summary>Short heat haze above an impact (screen-distortion quad, ~0.6 s).</summary>
    public static void HeatShimmer(Vector3 pos, float radius)
    {
        _shimmerShader ??= GD.Load<Shader>("res://assets/shaders/heat_shimmer.gdshader");
        if (_shimmerShader == null) return;
        _shimmerMesh ??= new QuadMesh { Size = Vector2.One, Material = new ShaderMaterial { Shader = _shimmerShader } };
        float s = Mathf.Max(2f, radius * 2.4f);
        var q = Add(new MeshInstance3D { Mesh = _shimmerMesh, Scale = new Vector3(s, s, s), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, ExtraCullMargin = 2f }, pos + Vector3.Up * (radius * .5f + .4f));
        q.SetInstanceShaderParameter("life", 1f);
        q.CreateTween().TweenMethod(Callable.From<float>(v => { if (GodotObject.IsInstanceValid(q)) q.SetInstanceShaderParameter("life", v); }), 1f, 0f, .6f);
        Later(q, .65f);
    }

    /// <summary>Everything that lingers after a fire blast: scorch decal, heat haze, drifting embers, a puff of black smoke.</summary>
    public static void Aftermath(Vector3 pos, float radius)
    {
        ScorchDecal(pos, radius);
        HeatShimmer(pos, radius);
        Sparks(pos + Vector3.Up * .2f, Ramp((0, new Color(1, .6f, .2f)), (.5f, new Color(1, .3f, .06f)), (1, new Color(.4f, .06f, .02f, 0))), 14, 1.6f, 1.5f, .12f, 1.2f, true, 40f, null, radius * .4f, "spark");
        Sparks(pos + Vector3.Up * .3f, Ramp((0, new Color(.03f, .03f, .03f, 0)), (.2f, new Color(.04f, .035f, .035f, .6f)), (1, new Color(.08f, .08f, .08f, 0))), 6, 1.8f, 2.2f, radius * .9f + .8f, 1.4f, false, 25f, null, radius * .25f, "smoke");
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
        Sparks(pos + Vector3.Up * .5f, Ramp((0, new Color(1, 1, 1, .7f)), (1, new Color(.5f, .85f, 1f, 0))), 30, 7f, .9f, .3f, -1f, true, 70f, Vector3.Up, radius * .4f, "star");
        Sparks(pos + Vector3.Up * .3f, Ramp((0, new Color(.8f, .95f, 1f, 0)), (.2f, new Color(.75f, .9f, 1f, .4f)), (1, new Color(.6f, .8f, 1f, 0))), 14, radius * 1.4f, 1.1f, 1.8f, 0f, false, 180f, Vector3.Right, .3f, "smoke").Flatness = 1f;   // cold mist rolling outward along the ground
        GroundMark(pos, "circle", radius * 2.1f, new Color(.6f, .9f, 1f, .7f), .6f, 1.2f, true);   // frost rime on the ground
        FrostAftermath(pos, radius);
    }

    // ---- frost aftermath: lingering ice shard ring, frost decal, cold mist ----
    static ImageTexture? _frostTex;
    static StandardMaterial3D? _iceMat;
    static CylinderMesh[]? _iceMeshes;
    static readonly Queue<Decal> _frosts = new();
    const int MaxFrosts = 8, MaxShardRings = 4;
    static int _shardRings;

    static ImageTexture FrostTex()
    {
        if (_frostTex != null) return _frostTex;
        const int N = 128; var img = Image.CreateEmpty(N, N, false, Image.Format.Rgba8);
        var noise = new FastNoiseLite { Frequency = .06f, Seed = 21 };
        for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
        {
            float dx = (x + .5f) / N * 2 - 1, dy = (y + .5f) / N * 2 - 1, d = Mathf.Sqrt(dx * dx + dy * dy);
            float n = noise.GetNoise2D(x * 2f, y * 2f), edge = .8f + n * .18f;
            float ring = Mathf.Clamp(1f - Mathf.Abs(d - edge * .75f) / .28f, 0f, 1f) * .8f + Mathf.Clamp((edge - d) / .6f, 0f, 1f) * .3f;   // crusted rim over a thin rime disc
            float a = Mathf.Clamp(ring * (.65f + .35f * noise.GetNoise2D(x * 6f + 30, y * 6f)), 0f, 1f); if (d > 1f) a = 0;
            img.SetPixel(x, y, new Color(.8f, .93f, 1f, a));
        }
        return _frostTex = ImageTexture.CreateFromImage(img);
    }

    /// <summary>Pale frost rime on the ground, fading over ~6 s. At most 8 alive.</summary>
    public static void FrostDecal(Vector3 pos, float radius)
    {
        while (_frosts.Count > 0 && (!GodotObject.IsInstanceValid(_frosts.Peek()) || _frosts.Count >= MaxFrosts))
        { var old = _frosts.Dequeue(); if (GodotObject.IsInstanceValid(old)) old.QueueFree(); }
        float gy = World.GroundAt(pos.X, pos.Z); if (!Dungeon.Active && gy > -50f) pos.Y = gy;
        float d = Mathf.Max(2f, radius * 2.2f);
        var dec = new Decal { TextureAlbedo = FrostTex(), Size = new Vector3(d, 3f, d), CullMask = 1, LowerFade = .3f, UpperFade = .3f };
        Add(dec, pos); dec.Rotation = new Vector3(0, GD.Randf() * Mathf.Tau, 0); _frosts.Enqueue(dec);
        dec.CreateTween().TweenProperty(dec, "modulate:a", 0f, 6f).SetDelay(.5f).SetEase(Tween.EaseType.In);
        Later(dec, 6.8f);
    }

    /// <summary>6-10 jagged ice shards bursting up in a ring (0.15 s), standing 2 s, then shrinking away. At most 4 rings alive.</summary>
    public static void IceShardRing(Vector3 pos, float radius)
    {
        if (_shardRings >= MaxShardRings) return;
        _shardRings++;
        _iceMat ??= new StandardMaterial3D { AlbedoColor = new Color(.78f, .94f, 1f), Roughness = .75f, EmissionEnabled = true, Emission = new Color(.45f, .8f, 1f), EmissionEnergyMultiplier = .6f };
        _iceMeshes ??= new[] { new CylinderMesh { TopRadius = .01f, BottomRadius = .16f, Height = 1f, RadialSegments = 4, Rings = 1 }, new CylinderMesh { TopRadius = .03f, BottomRadius = .1f, Height = 1f, RadialSegments = 3, Rings = 1 } };
        var rg = new RandomNumberGenerator(); int n = rg.RandiRange(6, 10);
        var holder = Add(new Node3D(), pos);
        for (int i = 0; i < n; i++)
        {
            float a = i / (float)n * Mathf.Tau + rg.RandfRange(-.2f, .2f), r = radius * rg.RandfRange(.55f, .95f), h = rg.RandfRange(.9f, 2.0f);
            var p = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
            float gy = World.GroundAt(pos.X + p.X, pos.Z + p.Z); if (!Dungeon.Active && gy > -50f) p.Y = gy - pos.Y;
            var m = new MeshInstance3D { Mesh = _iceMeshes[i % 2], MaterialOverride = _iceMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Position = p + Vector3.Up * h * .5f };
            holder.AddChild(m);
            float tilt = rg.RandfRange(.25f, .6f); m.Rotation = new Vector3(Mathf.Sin(a) * tilt, rg.Randf() * Mathf.Tau, -Mathf.Cos(a) * tilt);
            var full = new Vector3(rg.RandfRange(.7f, 1.3f), h, rg.RandfRange(.7f, 1.3f)); m.Scale = new Vector3(full.X, .01f, full.Z);
            var tw = m.CreateTween(); tw.TweenProperty(m, "scale", full, .15f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
            tw.TweenInterval(2f); tw.TweenProperty(m, "scale", new Vector3(full.X * .3f, .01f, full.Z * .3f), .5f).SetEase(Tween.EaseType.In);
        }
        Root3Release(holder, 2.8f);
    }
    static void Root3Release(Node3D n, float t)
    {
        var root = Game.Root; if (root == null) { _shardRings--; return; }
        root.GetTree().CreateTimer(t, false).Timeout += () => { _shardRings = Mathf.Max(0, _shardRings - 1); if (GodotObject.IsInstanceValid(n)) n.QueueFree(); };
    }

    /// <summary>Everything that lingers after a frost blast: ice shard ring, frost decal, slow cold mist.</summary>
    public static void FrostAftermath(Vector3 pos, float radius)
    {
        IceShardRing(pos, radius);
        FrostDecal(pos, radius);
        Sparks(pos + Vector3.Up * .3f, Ramp((0, new Color(.85f, .95f, 1f, 0)), (.25f, new Color(.8f, .93f, 1f, .35f)), (1, new Color(.7f, .85f, 1f, 0))), 8, .8f, 2.4f, radius * .8f + .6f, .1f, false, 60f, null, radius * .4f, "smoke");
    }

    // ---- holy / shadow / poison impacts: shared meshes + materials, capped decals and live effects ----
    static ImageTexture? _sigilTex, _crackTex, _puddleTex;
    static StandardMaterial3D? _holyMat, _voidMat, _wispMat, _bubbleMat;
    static CylinderMesh? _columnMesh; static SphereMesh? _ballMesh, _bubbleMesh;
    static readonly Queue<Decal> _sigils = new(), _cracks = new(), _puddles = new();
    const int MaxGroundMarks = 6, MaxColumns = 4, MaxVoids = 4;
    static int _columns, _voids;

    static ImageTexture MarkTex(ref ImageTexture? slot, System.Func<int, int, float, float, FastNoiseLite, Color> px, int seed, float freq)
    {
        if (slot != null) return slot;
        const int N = 128; var img = Image.CreateEmpty(N, N, false, Image.Format.Rgba8); var noise = new FastNoiseLite { Frequency = freq, Seed = seed };
        for (int y = 0; y < N; y++) for (int x = 0; x < N; x++) img.SetPixel(x, y, px(x, y, (x + .5f) / N * 2 - 1, (y + .5f) / N * 2 - 1, noise));
        return slot = ImageTexture.CreateFromImage(img);
    }
    static void MarkDecal(Queue<Decal> q, ImageTexture tex, Vector3 pos, float d, float fade)
    {
        while (q.Count > 0 && (!GodotObject.IsInstanceValid(q.Peek()) || q.Count >= MaxGroundMarks)) { var old = q.Dequeue(); if (GodotObject.IsInstanceValid(old)) old.QueueFree(); }
        float gy = World.GroundAt(pos.X, pos.Z); if (!Dungeon.Active && gy > -50f) pos.Y = gy;
        var dec = new Decal { TextureAlbedo = tex, Size = new Vector3(d, 3f, d), CullMask = 1, LowerFade = .3f, UpperFade = .3f };
        Add(dec, pos); dec.Rotation = new Vector3(0, GD.Randf() * Mathf.Tau, 0); q.Enqueue(dec);
        dec.CreateTween().TweenProperty(dec, "modulate:a", 0f, fade).SetDelay(.4f).SetEase(Tween.EaseType.In); Later(dec, fade + .6f);
    }

    /// <summary>Holy impact: tall pale-gold light column (0.6 s), rising gold motes, a warm flash and a faint sigil on the ground (4 s). Max 4 columns alive.</summary>
    public static void HolyImpact(Vector3 pos, float radius)
    {
        radius = Mathf.Clamp(radius, 1f, 5f);
        Flash(pos + Vector3.Up * 1.2f, new Color(1f, .88f, .55f), 3f, radius * 3f, .3f);
        MarkDecal(_sigils, MarkTex(ref _sigilTex, (x, y, dx, dy, n) =>
        {
            float d = Mathf.Sqrt(dx * dx + dy * dy), ring = Mathf.Clamp(1f - Mathf.Abs(d - .78f) / .05f, 0f, 1f), ring2 = Mathf.Clamp(1f - Mathf.Abs(d - .55f) / .03f, 0f, 1f);
            float cross = Mathf.Max(Mathf.Clamp(1f - Mathf.Abs(dx) / .035f, 0f, 1f) * (Mathf.Abs(dy) < .78f ? 1f : 0f), Mathf.Clamp(1f - Mathf.Abs(dy) / .035f, 0f, 1f) * (Mathf.Abs(dx) < .78f ? 1f : 0f));
            float a = Mathf.Clamp(Mathf.Max(ring, Mathf.Max(ring2 * .7f, cross)) * (.75f + .25f * n.GetNoise2D(x * 4f, y * 4f)), 0f, 1f) * .55f;
            return new Color(1f, .88f, .5f, a);
        }, 5, .05f), pos, radius * 2f, 4f);
        if (_columns >= MaxColumns) return;
        _columns++;
        _columnMesh ??= new CylinderMesh { TopRadius = .55f, BottomRadius = .75f, Height = 1f, RadialSegments = 14, Rings = 1, CapTop = false, CapBottom = false };
        _holyMat ??= new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add, AlbedoColor = new Color(1f, .9f, .6f, .45f), CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        float h = 7f, w = Mathf.Max(.8f, radius * .45f);
        var col = Add(new MeshInstance3D { Mesh = _columnMesh, MaterialOverride = _holyMat, Scale = new Vector3(w * .4f, h, w * .4f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off }, pos + Vector3.Up * h * .5f);
        var tw = col.CreateTween().SetParallel();
        tw.TweenProperty(col, "scale", new Vector3(w, h, w), .15f).SetEase(Tween.EaseType.Out);
        tw.TweenProperty(col, "transparency", 1f, .45f).SetDelay(.15f);
        tw.Chain().TweenCallback(Callable.From(() => { _columns = Mathf.Max(0, _columns - 1); if (GodotObject.IsInstanceValid(col)) col.QueueFree(); }));
        Sparks(pos + Vector3.Up * .2f, Ramp((0, new Color(1f, .95f, .7f, 0)), (.2f, new Color(1f, .9f, .55f, .9f)), (1, new Color(1f, .8f, .35f, 0))), 18, 2.6f, 1.3f, .16f, 1.2f, true, 12f, Vector3.Up, radius * .6f, "spark");
    }

    /// <summary>Shadow impact: a violet-black sphere collapsing 1.4 -> .2 (0.4 s), wisps spiralling in, a dark crack on the ground (5 s), a dim violet pulse. Max 4 alive.</summary>
    public static void ShadowImpact(Vector3 pos, float radius)
    {
        radius = Mathf.Clamp(radius, 1f, 5f);
        Flash(pos + Vector3.Up * .8f, new Color(.4f, .15f, .65f), .6f, radius * 2.5f, .4f);
        MarkDecal(_cracks, MarkTex(ref _crackTex, (x, y, dx, dy, n) =>
        {
            float d = Mathf.Sqrt(dx * dx + dy * dy), ang = Mathf.Atan2(dy, dx), arms = Mathf.Abs(Mathf.Sin(ang * 3.5f + n.GetNoise2D(x * 2f, y * 2f) * 3f));
            float a = Mathf.Clamp(1f - arms * 7f * (.4f + d), 0f, 1f) * Mathf.Clamp((.95f - d) / .5f, 0f, 1f) + Mathf.Clamp((.22f - d) / .12f, 0f, 1f);
            return new Color(.02f, 0f, .04f, Mathf.Clamp(a * (.7f + .3f * n.GetNoise2D(x * 5f, y * 5f)), 0f, 1f) * .9f);
        }, 33, .06f), pos, radius * 2.2f, 5f);
        if (_voids >= MaxVoids) return;
        _voids++;
        _ballMesh ??= new SphereMesh { Radius = 1, Height = 2, RadialSegments = 14, Rings = 7 };
        _voidMat ??= new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, AlbedoColor = new Color(.1f, .03f, .17f, .8f), CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        float r0 = Mathf.Max(.7f, radius * .5f);
        var ball = Add(new MeshInstance3D { Mesh = _ballMesh, MaterialOverride = _voidMat, Scale = Vector3.One * r0 * 1.4f, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off }, pos + Vector3.Up * .8f);
        var tw = ball.CreateTween().SetParallel();
        tw.TweenProperty(ball, "scale", Vector3.One * r0 * .2f, .4f).SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Cubic);
        tw.TweenProperty(ball, "transparency", 1f, .4f).SetDelay(.15f);
        tw.Chain().TweenCallback(Callable.From(() => { _voids = Mathf.Max(0, _voids - 1); if (GodotObject.IsInstanceValid(ball)) ball.QueueFree(); }));
        _wispMat ??= new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add, AlbedoColor = new Color(.45f, .2f, .8f, .8f) };
        _bubbleMesh ??= new SphereMesh { Radius = .5f, Height = 1f, RadialSegments = 8, Rings = 4 };
        for (int i = 0; i < 6; i++)
        {
            float a0 = i / 6f * Mathf.Tau + GD.Randf() * .5f, rr = radius * (1.1f + GD.Randf() * .4f), y0 = .3f + GD.Randf() * 1.2f, ay = .8f;
            var wp = Add(new MeshInstance3D { Mesh = _bubbleMesh, MaterialOverride = _wispMat, Scale = Vector3.One * .22f, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off }, pos);
            var c0 = pos;
            wp.CreateTween().TweenMethod(Callable.From<float>(t =>
            {
                if (!GodotObject.IsInstanceValid(wp)) return;
                float a = a0 + t * 3.2f, r = rr * (1f - t);
                wp.GlobalPosition = c0 + new Vector3(Mathf.Cos(a) * r, Mathf.Lerp(y0, ay, t), Mathf.Sin(a) * r); wp.Transparency = t * t;
            }), 0f, 1f, .4f);
            Later(wp, .45f);
        }
    }

    /// <summary>Poison impact: green bubbling puddle decal (5 s), a few rising bubbles that pop, and a low drifting green mist.</summary>
    public static void PoisonImpact(Vector3 pos, float radius)
    {
        radius = Mathf.Clamp(radius, 1f, 5f);
        MarkDecal(_puddles, MarkTex(ref _puddleTex, (x, y, dx, dy, n) =>
        {
            float d = Mathf.Sqrt(dx * dx + dy * dy), edge = .78f + n.GetNoise2D(x * 2.5f, y * 2.5f) * .22f, a = Mathf.Clamp((edge - d) / .2f, 0f, 1f);
            float bub = Mathf.Clamp(n.GetNoise2D(x * 7f + 90, y * 7f) * 3f - 1.1f, 0f, 1f);   // lighter bubble blotches
            return new Color(.35f + bub * .3f, .62f + bub * .25f, .12f + bub * .1f, a * (.7f + bub * .25f));
        }, 61, .05f), pos, radius * 2f, 5f);
        _bubbleMesh ??= new SphereMesh { Radius = .5f, Height = 1f, RadialSegments = 8, Rings = 4 };
        _bubbleMat ??= new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, AlbedoColor = new Color(.5f, .85f, .2f, .65f) };
        float gy = World.GroundAt(pos.X, pos.Z); if (!Dungeon.Active && gy > -50f) pos.Y = gy;
        for (int i = 0; i < 5; i++)
        {
            float a = GD.Randf() * Mathf.Tau, r = radius * .7f * Mathf.Sqrt(GD.Randf()), s = .12f + GD.Randf() * .12f, rise = .5f + GD.Randf() * .6f, dl = i * .12f;
            var b = Add(new MeshInstance3D { Mesh = _bubbleMesh, MaterialOverride = _bubbleMat, Scale = Vector3.One * .01f, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off }, pos + new Vector3(Mathf.Cos(a) * r, .05f, Mathf.Sin(a) * r));
            var tw = b.CreateTween(); tw.TweenInterval(dl);
            tw.SetParallel().TweenProperty(b, "scale", Vector3.One * s, .5f); tw.TweenProperty(b, "position:y", b.Position.Y + rise, .5f);
            tw.Chain().TweenProperty(b, "scale", Vector3.One * s * 1.5f, .06f); tw.TweenProperty(b, "transparency", 1f, .06f);   // pop
            Later(b, dl + .7f);
        }
        Sparks(pos + Vector3.Up * .25f, Ramp((0, new Color(.4f, .7f, .2f, 0)), (.25f, new Color(.4f, .7f, .15f, .4f)), (1, new Color(.3f, .5f, .1f, 0))), 8, .7f, 2.2f, radius * .8f + .5f, .05f, false, 70f, null, radius * .4f, "smoke");
    }

    /// <summary>One entry point for element impacts without a dedicated effect of their own (fire/frost/lightning have theirs).</summary>
    public static void ElementImpact(string type, Vector3 pos, float radius)
    {
        switch (type) { case "holy": HolyImpact(pos, radius); break; case "shadow": ShadowImpact(pos, radius); break; case "poison": PoisonImpact(pos, radius); break; }
    }

    // ---- lightning strike impact: sky bolt, flash, small scorch ----
    static StandardMaterial3D? _boltMat;
    static int _strikes; static ulong _lastStrikeMs;
    const int MaxStrikes = 3;

    /// <summary>Forked bolt falling from ~12 m with a bright flash, small scorch and sparks. Lives 0.12 s, flickers twice. Capped at 3 alive.</summary>
    public static void StormStrike(Vector3 pos, float radius = 1.5f)
    {
        ulong now = Time.GetTicksMsec(); if (_strikes >= MaxStrikes || now - _lastStrikeMs < 60) return;
        _lastStrikeMs = now; _strikes++;
        _boltMat ??= new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add, VertexColorUseAsAlbedo = true, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        var rg = new RandomNumberGenerator(); var im = new ImmediateMesh();
        var top = new Vector3(0, 12f, 0); var main = new List<Vector3> { Vector3.Zero };
        for (int i = 1; i < 9; i++) { float t = i / 9f; main.Add(Vector3.Zero.Lerp(top, t) + (i < 8 ? new Vector3(rg.RandfRange(-.8f, .8f), 0, rg.RandfRange(-.8f, .8f)) * (1f - t * .3f) : Vector3.Zero)); }
        main.Add(top);
        Bolt(im, main, .22f, new Color(.85f, .75f, 1f, 1f), new Color(1f, 1f, 1f, 1f));
        for (int b = 0; b < 2; b++)
        {
            int s = rg.RandiRange(3, 6); var start = main[s]; var br = new List<Vector3> { start }; var dir = new Vector3(rg.RandfRange(-1, 1), -.6f, rg.RandfRange(-1, 1)).Normalized();
            for (int k = 1; k <= 3; k++) br.Add(start + dir * k * 1.3f + new Vector3(rg.RandfRange(-.4f, .4f), 0, rg.RandfRange(-.4f, .4f)));
            Bolt(im, br, .1f, new Color(.6f, .45f, 1f, .8f), new Color(.9f, .85f, 1f, .8f));
        }
        var m = Add(new MeshInstance3D { Mesh = im, MaterialOverride = _boltMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, ExtraCullMargin = 14f }, pos);
        var tw = m.CreateTween();
        tw.TweenInterval(.04f); tw.TweenCallback(Callable.From(() => m.Visible = false));
        tw.TweenInterval(.015f); tw.TweenCallback(Callable.From(() => m.Visible = true));
        tw.TweenInterval(.03f); tw.TweenCallback(Callable.From(() => m.Visible = false));
        tw.TweenInterval(.01f); tw.TweenCallback(Callable.From(() => m.Visible = true));
        tw.TweenInterval(.025f); tw.TweenCallback(Callable.From(() => { if (GodotObject.IsInstanceValid(m)) m.QueueFree(); _strikes = Mathf.Max(0, _strikes - 1); }));
        Flash(pos + Vector3.Up * .8f, new Color(.75f, .65f, 1f), 6f, 12f, .1f);
        ScorchDecal(pos, radius * .6f);
        Sparks(pos + Vector3.Up * .1f, Ramp((0, Colors.White), (.4f, new Color(.7f, .6f, 1f)), (1, new Color(.4f, .3f, 1f, 0))), 16, 8f, .4f, .3f, -8f, true, 90f, Vector3.Up, .1f, "spark");
    }
    static void Bolt(ImmediateMesh im, List<Vector3> pts, float w, Color outer, Color core)
    {
        // crossed pair of ribbons per segment so the bolt reads from any side; wide violet halo plus a thin white core
        for (int pass = 0; pass < 2; pass++)
        {
            float hw = pass == 0 ? w : w * .35f; var col = pass == 0 ? new Color(outer, outer.A * .55f) : core;
            im.SurfaceBegin(Mesh.PrimitiveType.Triangles);
            for (int i = 0; i + 1 < pts.Count; i++)
                foreach (var off in new[] { Vector3.Right * hw, Vector3.Back * hw })
                {
                    Vector3 a = pts[i], b = pts[i + 1];
                    im.SurfaceSetColor(col); im.SurfaceAddVertex(a - off); im.SurfaceSetColor(col); im.SurfaceAddVertex(a + off); im.SurfaceSetColor(col); im.SurfaceAddVertex(b + off);
                    im.SurfaceSetColor(col); im.SurfaceAddVertex(a - off); im.SurfaceSetColor(col); im.SurfaceAddVertex(b + off); im.SurfaceSetColor(col); im.SurfaceAddVertex(b - off);
                }
            im.SurfaceEnd();
        }
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
        Sparks(pos + Vector3.Up * .3f, Ramp((0, new Color(1, 1, .7f)), (.5f, new Color(.5f, 1f, .5f)), (1, new Color(.4f, 1f, .4f, 0))), 36, 4f, 1.2f, .4f, 2.5f, true, 25f, Vector3.Up, .8f, "star");
        Sparks(pos + Vector3.Up * 1f, Ramp((0, new Color(1, 1, .8f, 0)), (.3f, new Color(.6f, 1f, .5f, .6f)), (1, new Color(.4f, 1f, .4f, 0))), 4, .5f, .8f, 2.6f, 1f, true, 20f, Vector3.Up, .2f, "light");   // a soft bloom around the hero
    }

    /// <summary>A column of light striking down at a point (Smite), with a ground ring.</summary>
    public static void HealPillarAt(Vector3 pos, Color c, float radius)
    {
        Flash(pos + Vector3.Up * 2f, c, 5f, radius * 4, .5f);
        var col = Add(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = radius * .35f, BottomRadius = radius * .55f, Height = 9f, RadialSegments = 20, CapTop = false, CapBottom = false }, MaterialOverride = Glow(c, .6f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off }, pos + Vector3.Up * 4.5f);
        var tw = col.CreateTween().SetParallel(); tw.TweenProperty(col, "scale", new Vector3(.2f, 1, .2f), .35f).SetEase(Tween.EaseType.In); tw.TweenProperty(col.MaterialOverride, "albedo_color:a", 0f, .35f); Later(col, .4f);
        Ring(pos + Vector3.Up * .1f, radius, c, .4f);
        Sparks(pos + Vector3.Up * .3f, Ramp((0, Colors.White), (1, new Color(c, 0))), 30, 6f, .6f, .4f, -2f, true, 180f, null, .1f, "star");
        GroundMark(pos, "circle", radius * 2.2f, new Color(c, .8f), .3f, .8f, true);
    }

    /// <summary>Jagged lightning between two points (core and glow), re-forking a few times before it fades.</summary>
    public static void Lightning(Vector3 a, Vector3 b, Color c, float time = .25f)
    {
        var bolt = new LightningBolt { A = a, B = b, Col = c, Life = time }; Game.Root?.AddChild(bolt);
        Flash(b, c, 3f, 6f, time); Sparks(b, Ramp((0, Colors.White), (1, new Color(c, 0))), 12, 6f, .3f, .45f, -3f, true, 180f, null, .1f, "spark");
        if (time >= .2f) StormStrike(b);   // sky bolt + scorch at the impact point (beams/short zaps skip it)
    }
}

/// <summary>Ribbon of light that follows a blade (base and tip markers) while Emit is on, fading over Life seconds.</summary>
public partial class SwordTrail : MeshInstance3D
{
    public Node3D? Base, Tip; public bool Emit; public Color Col = new(.6f, .9f, 1f); public float Life = .22f;
    readonly List<(Vector3 a, Vector3 b, float t)> _pts = new(); readonly ImmediateMesh _im = new(); float _now;
    static ShaderMaterial? _mat; static StandardMaterial3D? _sparkMat; static QuadMesh? _sparkMesh;
    CpuParticles3D? _sparks; Vector3 _lastTip; bool _hasTip;
    public override void _Ready()
    {
        Mesh = _im; TopLevel = true; GlobalTransform = Transform3D.Identity; CastShadow = ShadowCastingSetting.Off;
        // one shared material for every trail; the element tint rides in vertex colour
        _mat ??= new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/sword_trail.gdshader") };
        MaterialOverride = _mat;
        _sparkMat ??= new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add, VertexColorUseAsAlbedo = true, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        _sparkMesh ??= new QuadMesh { Size = new Vector2(.045f, .045f), Material = _sparkMat };
        _sparks = new CpuParticles3D
        {
            Emitting = false, Amount = 20, Lifetime = .35, Mesh = _sparkMesh, TopLevel = true, LocalCoords = false,
            Direction = Vector3.Up, Spread = 70f, InitialVelocityMin = .8f, InitialVelocityMax = 3f, Gravity = new Vector3(0, -7f, 0),
            ScaleAmountMin = .5f, ScaleAmountMax = 1.4f, CastShadow = ShadowCastingSetting.Off,
            ColorRamp = new Gradient { Colors = new[] { new Color(1f, .95f, .8f, 1f), new Color(1f, .6f, .25f, .8f), new Color(.4f, .2f, .1f, 0f) }, Offsets = new[] { 0f, .35f, 1f } },
        };
        AddChild(_sparks);
    }
    public override void _Process(double delta)
    {
        float dt = (float)delta; _now += dt;
        if (Emit && Base != null && Tip != null) _pts.Add((Base.GlobalPosition, Tip.GlobalPosition, _now));
        while (_pts.Count > 0 && _now - _pts[0].t > Life) _pts.RemoveAt(0);
        // sparks only on fast swings, at the blade tip
        if (_sparks != null)
        {
            bool fast = false;
            if (Emit && Tip != null && IsInstanceValid(Tip))
            {
                var tp = Tip.GlobalPosition;
                fast = _hasTip && dt > 0f && tp.DistanceTo(_lastTip) / dt > 8f; _lastTip = tp; _hasTip = true; _sparks.GlobalPosition = tp;
            }
            else _hasTip = false;
            _sparks.Emitting = fast;
        }
        _im.ClearSurfaces(); if (_pts.Count < 2) return;
        var edgeCol = Col.Lerp(Colors.White, .35f);
        _im.SurfaceBegin(Mesh.PrimitiveType.TriangleStrip);
        foreach (var (a, b, t) in _pts)
        {
            float age = Mathf.Clamp((_now - t) / Life, 0f, 1f);
            _im.SurfaceSetColor(new Color(edgeCol, 1f)); _im.SurfaceSetUV(new Vector2(age, 0f)); _im.SurfaceAddVertex(a);
            _im.SurfaceSetColor(new Color(edgeCol, 1f)); _im.SurfaceSetUV(new Vector2(age, 1f)); _im.SurfaceAddVertex(b + (b - a) * .08f);
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
