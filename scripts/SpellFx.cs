using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>
/// Plays the hand-drawn-style spell and skill animations in assets/vfx/flipbooks (4x4 sprite sheets made by tools/kit_gen/fx_gen.py).
/// data/spellfx.json lists every animation (frames, speed, loop, kind, size) and which ones each skill uses.
///   SpellFx.Play("fire_burst", pos)            one animation at a point (billboard or ground, as the data says)
///   SpellFx.Attach("fire_orb", projectileNode) a looping animation that follows a node and is freed with it
///   SpellFx.Beam("lightning_bolt", a, b)       a strip stretched between two points
///   SpellFx.Skill("fire", "hit", pos)          whatever animation data/spellfx.json gives that skill for that moment (cast, travel, hit, ground, beam, after, finisher)
/// </summary>
public static class SpellFx
{
    record Def(int Frames, float Fps, bool Loop, string Kind, float Size);
    static readonly Dictionary<string, Def> _defs = new();
    static readonly Dictionary<string, Dictionary<string, string>> _skills = new();
    static readonly Dictionary<string, Texture2D?> _tex = new();
    static Shader? _shader;
    static bool _loaded;

    const string ShaderCode = @"
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, shadows_disabled, blend_mix;
uniform sampler2D sheet : source_color, filter_linear, repeat_enable;
uniform float frame = 0.0;        // which of the 16 frames (4 x 4)
uniform float glow = 1.6;         // brightness, above 1 so the bloom picks it up
uniform float fade = 1.0;
uniform float tile = 1.0;         // strips: how many times the picture repeats along the length
uniform bool billboard = true;
void vertex() {
	if (billboard) {
		vec3 sc = vec3(length(MODEL_MATRIX[0].xyz), length(MODEL_MATRIX[1].xyz), length(MODEL_MATRIX[2].xyz));
		MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0] * sc.x, INV_VIEW_MATRIX[1] * sc.y, INV_VIEW_MATRIX[2] * sc.z, MODEL_MATRIX[3]);
		MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
	}
}
void fragment() {
	float f = floor(frame + 0.001);
	vec2 cell = vec2(mod(f, 4.0), floor(f / 4.0));
	vec2 uv = vec2(UV.x, fract(UV.y * tile));
	uv = clamp(uv, 0.004, 0.996);
	vec4 c = texture(sheet, (cell + uv) / 4.0);
	ALBEDO = c.rgb * glow;
	ALPHA = c.a * fade;
}";

    static void Load()
    {
        if (_loaded) return; _loaded = true;
        _shader = new Shader { Code = ShaderCode };
        using var doc = JsonUtil.Load("res://data/spellfx.json");
        if (doc.RootElement.TryGetProperty("fx", out var fx))
            foreach (var p in fx.EnumerateObject())
                _defs[p.Name] = new Def((int)JsonUtil.F(p.Value, "frames", 16), JsonUtil.F(p.Value, "fps", 24), p.Value.TryGetProperty("loop", out var lp) && lp.ValueKind == JsonValueKind.True,
                                        JsonUtil.S(p.Value, "kind", "billboard"), JsonUtil.F(p.Value, "size", 2f));
        if (doc.RootElement.TryGetProperty("skills", out var sk))
            foreach (var p in sk.EnumerateObject())
            {
                var d = new Dictionary<string, string>();
                foreach (var q in p.Value.EnumerateObject()) if (q.Value.ValueKind == JsonValueKind.String) d[q.Name] = q.Value.GetString() ?? "";
                _skills[p.Name] = d;
            }
    }

    static Texture2D? Sheet(string name)
    {
        if (_tex.TryGetValue(name, out var t)) return t;
        string path = $"res://assets/vfx/flipbooks/{name}.png";
        t = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
        if (t == null) GD.PrintErr($"[SPELLFX] missing sheet {path}");
        return _tex[name] = t;
    }

    /// <summary>The animation a skill uses at a given moment, or "" if it has none.</summary>
    public static string For(string skillId, string moment)
    {
        Load(); return _skills.TryGetValue(skillId, out var d) && d.TryGetValue(moment, out var n) ? n : "";
    }

    /// <summary>Play the animation data/spellfx.json gives this skill for this moment. Does nothing if there is none.</summary>
    public static SpellFxSprite? Skill(string skillId, string moment, Vector3 pos, float scale = 1f, float life = 0f)
    {
        string n = For(skillId, moment); return n == "" ? null : Play(n, pos, scale, life);
    }

    /// <summary>Like Skill, but the animation follows a node (see Attach).</summary>
    public static SpellFxSprite? SkillAttach(string skillId, string moment, Node3D to, Vector3? offset = null, float scale = 1f, float life = 0f)
    {
        string n = For(skillId, moment); return n == "" ? null : Attach(n, to, offset, scale, life);
    }

    /// <summary>The drawn size in metres of an animation (0 if unknown); divide a radius by half of it to get a scale.</summary>
    public static float SizeOf(string name) { Load(); return _defs.TryGetValue(name, out var d) ? d.Size : 0f; }

    /// <summary>
    /// One animation at a world position. Ground kinds lie flat just above the floor; the rest face the camera.
    /// life > 0 fades it out after that many seconds. A looping animation with nothing to free it gets 2 s, so it cannot loop forever.
    /// </summary>
    public static SpellFxSprite? Play(string name, Vector3 pos, float scale = 1f, float life = 0f)
    {
        var s = Make(name, scale); if (s == null) return null;
        var root = Game.Root; if (root == null) { s.Free(); return null; }
        s.Life = life > 0 ? life : s.Loop ? 2f : 0f;
        root.AddChild(s); s.GlobalPosition = pos + (s.Ground ? new Vector3(0, .06f, 0) : Vector3.Zero);
        return s;
    }

    /// <summary>An animation that follows a node (a projectile, the hero) and is freed with it. offset is in the node's own space. life > 0 fades it out sooner.</summary>
    public static SpellFxSprite? Attach(string name, Node3D to, Vector3? offset = null, float scale = 1f, float life = 0f)
    {
        var s = Make(name, scale); if (s == null) return null;
        s.Life = life; to.AddChild(s); s.Position = offset ?? Vector3.Zero;
        return s;
    }

    /// <summary>A strip animation stretched from a to b (lightning, drain beam). Non-looping strips free themselves; looping ones need Free().</summary>
    public static SpellFxSprite? Beam(string name, Vector3 a, Vector3 b)
    {
        Load(); if (!_defs.TryGetValue(name, out var d)) return null;
        var s = Make(name, 1f); if (s == null) return null;
        var root = Game.Root; if (root == null) { s.Free(); return null; }
        root.AddChild(s); s.Stretch(a, b, d.Size);
        return s;
    }

    static SpellFxSprite? Make(string name, float scale)
    {
        Load();
        if (!_defs.TryGetValue(name, out var d)) { GD.PrintErr($"[SPELLFX] unknown animation {name}"); return null; }
        var tex = Sheet(name); if (tex == null) return null;
        bool ground = d.Kind == "ground", strip = d.Kind == "strip";
        var mat = new ShaderMaterial { Shader = _shader };
        mat.SetShaderParameter("sheet", tex); mat.SetShaderParameter("billboard", !ground && !strip);
        var quad = new QuadMesh { Size = Vector2.One * d.Size * scale };
        if (ground) quad.Orientation = PlaneMesh.OrientationEnum.Y;
        return new SpellFxSprite { Mesh = quad, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Frames = d.Frames, Fps = d.Fps, Loop = d.Loop, Ground = ground, Mat = mat };
    }
}

/// <summary>One playing flipbook. Steps through the frames, then frees itself (unless it loops).</summary>
public partial class SpellFxSprite : MeshInstance3D
{
    public int Frames = 16; public float Fps = 24; public bool Loop, Ground; public ShaderMaterial? Mat;
    public float Life;   // > 0: fade out and free after this many seconds (ends looping animations)
    double _t; bool _fading;

    /// <summary>Lay the quad between two points, as wide as `width`, turned to face the camera as well as it can.</summary>
    public void Stretch(Vector3 a, Vector3 b, float width)
    {
        Vector3 d = b - a; float len = d.Length(); if (len < .01f) { QueueFree(); return; }
        Vector3 y = d / len, toCam = (GetViewport().GetCamera3D()?.GlobalPosition ?? a + Vector3.Back) - (a + b) / 2;
        Vector3 x = y.Cross(toCam).Normalized(); if (x.LengthSquared() < .5f) x = y.Cross(Vector3.Right).Normalized();
        Vector3 z = x.Cross(y);
        if (Mesh is QuadMesh q) q.Size = new Vector2(width, len);
        GlobalTransform = new Transform3D(new Basis(x, y, z), (a + b) / 2);
        Mat?.SetShaderParameter("tile", Mathf.Max(1f, Mathf.Round(len / (width * 2.5f))));
    }

    /// <summary>Fade out over `time` seconds and free (use to end a looping animation gently).</summary>
    public void FadeOut(float time = .2f)
    {
        if (_fading) return; _fading = true;
        if (Mat == null) { QueueFree(); return; }
        var tw = CreateTween(); tw.TweenMethod(Callable.From<float>(v => Mat.SetShaderParameter("fade", v)), 1f, 0f, time); tw.TweenCallback(Callable.From(QueueFree));
    }

    /// <summary>
    /// Ground animations are flat quads: on a slope half of one would sink into the hill. Tilt it to the ground under it
    /// (world layer 1, like projectiles), keeping its height above the ground and its turn. Every frame, as zones and whirlwinds move.
    /// </summary>
    void HugGround()
    {
        var at = GlobalPosition; var space = GetWorld3D()?.DirectSpaceState; if (space == null) return;
        var q = PhysicsRayQueryParameters3D.Create(at + Vector3.Up * 1.5f, at + Vector3.Down * 2.5f, 1);
        if (Player.Instance is { } pl && IsInstanceValid(pl)) q.Exclude = new Godot.Collections.Array<Rid> { pl.GetRid() };   // the hero is on layer 1 too
        var hit = space.IntersectRay(q);
        if (hit.Count == 0) return;
        var n = ((Vector3)hit["normal"]).Normalized(); if (n.Y < .5f) return;   // a wall, not a floor
        var fwd = GlobalBasis.Z; fwd = (fwd - n * fwd.Dot(n)); if (fwd.LengthSquared() < 1e-4f) fwd = Vector3.Back; fwd = fwd.Normalized();
        GlobalBasis = new Basis(n.Cross(fwd), n, fwd);
        GlobalPosition = (Vector3)hit["position"] + n * .06f;
    }

    public override void _Process(double delta)
    {
        if (Ground) HugGround();
        _t += delta; if (Life > 0 && _t >= Life) FadeOut();
        int f = (int)(_t * Fps);
        if (f >= Frames) { if (!Loop) { QueueFree(); return; } f %= Frames; }
        Mat?.SetShaderParameter("frame", (float)f);
    }
}
