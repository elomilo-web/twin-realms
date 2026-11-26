using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>
/// F-12 spell effect library: one look per element x shape, drawn with shaders (instance uniforms on shared materials) and GPU particles
/// in a clean anime style (white-hot cores, soft gradients, sharp spikes and runes, saturated element colours).
/// data/spellfx.json "styles" gives each element x shape its colours, particle sprites and shader numbers.
///   SpellStyles.Projectile(node, el, power)          core + glow + trail on a flying node
///   SpellStyles.Beam(a, b, el, power, life)          scrolling ribbon (life 0 = until Stop; call Set(a, b) to move it)
///   SpellStyles.Nova(at, r, el, power)               expanding shock ring + spokes + flung particles
///   SpellStyles.Aoe(at, r, el, power)                one-shot rune circle on the ground + impact
///   SpellStyles.Linger(at, r, dur, el, power, parent) lingering ground circle; its rim drains and the circle fades as time runs out.
///                                                    Returns a LingerFx: SetTimeLeft(seconds), SetRadius(r), Stop(). (F-13 uses this.)
///   SpellStyles.Buff(target, el, power, tint)        rising aura + ring and sigil at the feet
///   SpellStyles.Debuff(target, el, dur, height)      dark swirl over the target's head
///   SpellStyles.Heal(target, power)                  light pillar + rising motes
///   SpellStyles.Summon(at, el, power)                sigil circle + flash column
///   SpellStyles.Impact(at, el, power)                flash + burst + sparks scaled by damage
/// Power: 1 = a hit of refDmg; log scale, clamped (Power / PowerOf). Particle counts follow Game.Set.Quality.
/// </summary>
public static class SpellStyles
{
    public sealed class Style
    {
        public Color Core = Colors.White, Glow = Colors.White, Edge = Colors.White, Dark = Colors.Black;
        public string Set = "magic"; public bool Add = true;
        public float Gravity, Spin = 90, Spikes = 4, Runes = 8, Scroll = 6, Amount = 16, Life = .5f, Speed = 3, Size = .3f;
        public Style Copy() => (Style)MemberwiseClone();
    }
    public static readonly string[] Elements = { "physical", "fire", "frost", "lightning", "poison", "shadow", "holy", "arcane" };
    public static readonly string[] Shapes = { "projectile", "beam", "nova", "aoe", "linger", "buff", "debuff", "heal", "summon", "impact" };
    static readonly Dictionary<string, Style> _styles = new();
    static readonly Dictionary<string, Color> _buffCols = new();
    static float _refDmg = 30, _pMin = .7f, _pMax = 2.2f; static float[] _q = { .35f, .65f, 1f, 1f };
    static bool _loaded; static int _missing;

    // ------------------------------------------------------------------ data
    static void Load()
    {
        if (_loaded) return; _loaded = true;
        try
        {
            using var doc = JsonUtil.Load("res://data/spellfx.json");
            if (!doc.RootElement.TryGetProperty("styles", out var st)) { GD.PrintErr("[SPELLFX] data/spellfx.json has no \"styles\""); _missing = Elements.Length * Shapes.Length; return; }
            _refDmg = JsonUtil.F(st, "refDmg", 30); _pMin = JsonUtil.F(st, "powerMin", .7f); _pMax = JsonUtil.F(st, "powerMax", 2.2f);
            if (st.TryGetProperty("quality", out var q) && q.ValueKind == JsonValueKind.Array) { int i = 0; foreach (var x in q.EnumerateArray()) if (i < 4) _q[i++] = x.GetSingle(); }
            if (st.TryGetProperty("buffColors", out var bc)) foreach (var p in bc.EnumerateObject()) if (p.Value.GetString() is { } h) _buffCols[p.Name] = Color.FromHtml(h);
            st.TryGetProperty("defaults", out var def); st.TryGetProperty("elements", out var els); st.TryGetProperty("shapes", out var shs); st.TryGetProperty("overrides", out var ovs);
            foreach (var el in Elements)
                foreach (var sh in Shapes)
                {
                    var s = new Style();
                    if (def.ValueKind == JsonValueKind.Object) Apply(s, def);
                    bool hasEl = els.ValueKind == JsonValueKind.Object && els.TryGetProperty(el, out var ej); if (hasEl) Apply(s, els.GetProperty(el));
                    bool hasSh = shs.ValueKind == JsonValueKind.Object && shs.TryGetProperty(sh, out var sj); if (hasSh) Apply(s, shs.GetProperty(sh));
                    if (ovs.ValueKind == JsonValueKind.Object && ovs.TryGetProperty(el + "." + sh, out var oj)) Apply(s, oj);
                    if (!hasEl || !hasSh) { _missing++; GD.PrintErr($"[SPELLFX] style {el}.{sh}: no {(hasEl ? "shape" : "element")} entry in data/spellfx.json styles"); }
                    _styles[el + "." + sh] = s;
                }
        }
        catch (System.Exception e) { GD.PrintErr("[SPELLFX] styles failed to load: " + e.Message); }
    }
    static void Apply(Style s, JsonElement j)
    {
        foreach (var p in j.EnumerateObject())
        {
            var v = p.Value;
            switch (p.Name)
            {
                case "core": s.Core = Color.FromHtml(v.GetString()!); break;
                case "glow": s.Glow = Color.FromHtml(v.GetString()!); break;
                case "edge": s.Edge = Color.FromHtml(v.GetString()!); break;
                case "dark": s.Dark = Color.FromHtml(v.GetString()!); break;
                case "set": s.Set = v.GetString() ?? "magic"; break;
                case "add": s.Add = v.ValueKind == JsonValueKind.True; break;
                case "gravity": s.Gravity = v.GetSingle(); break;
                case "spin": s.Spin = v.GetSingle(); break;
                case "spikes": s.Spikes = v.GetSingle(); break;
                case "runes": s.Runes = v.GetSingle(); break;
                case "scroll": s.Scroll = v.GetSingle(); break;
                case "amount": s.Amount = v.GetSingle(); break;
                case "life": s.Life = v.GetSingle(); break;
                case "speed": s.Speed = v.GetSingle(); break;
                case "size": s.Size = v.GetSingle(); break;
            }
        }
    }
    /// <summary>The element a damage type draws as ("true" and unknown types: arcane).</summary>
    public static string El(string? type) => type != null && System.Array.IndexOf(Elements, type) >= 0 ? type : "arcane";
    public static Style Get(string el, string shape) { Load(); return _styles.TryGetValue(El(el) + "." + shape, out var s) ? s : _styles.GetValueOrDefault("arcane." + shape) ?? new Style(); }

    /// <summary>Which shapes each skill kind plays (the audit checks every skill resolves to at least one).</summary>
    public static readonly Dictionary<string, string[]> KindShapes = new()
    {
        ["projectile"] = new[] { "projectile", "impact" }, ["boomerang"] = new[] { "projectile", "impact" },
        ["chain"] = new[] { "beam", "impact" }, ["hook"] = new[] { "beam", "impact" }, ["beam"] = new[] { "beam", "impact" },
        ["nova"] = new[] { "nova", "impact" }, ["spin"] = new[] { "nova", "impact" },
        ["strike"] = new[] { "linger", "aoe", "nova", "impact" }, ["trap"] = new[] { "aoe", "impact" }, ["mark"] = new[] { "debuff", "aoe", "impact" },
        ["leap"] = new[] { "aoe", "impact" }, ["dash"] = new[] { "impact" }, ["rush"] = new[] { "impact" }, ["blinkto"] = new[] { "aoe", "impact" }, ["melee"] = new[] { "impact" },
        ["zone"] = new[] { "linger", "debuff", "impact" }, ["wall"] = new[] { "linger" }, ["vortex"] = new[] { "linger", "nova" },
        ["buff"] = new[] { "buff" }, ["ward"] = new[] { "buff" }, ["enchant"] = new[] { "buff" },
        ["heal"] = new[] { "heal" }, ["resurrect"] = new[] { "heal" }, ["turret"] = new[] { "summon", "projectile" }, ["orbit"] = new[] { "summon" },
    };
    /// <summary>Style coverage (element x shape) and every skill kind's shapes; returns the number of problems (logged).</summary>
    public static int Audit()
    {
        Load(); int bad = _missing, n = 0;
        foreach (var el in Elements) foreach (var sh in Shapes) { n++; if (!_styles.ContainsKey(el + "." + sh)) bad++; }
        foreach (var k in Skills.All) if (!KindShapes.ContainsKey(k.Kind)) { bad++; GD.PrintErr($"[SPELLFX] style audit: skill {k.Id} kind {k.Kind} has no effect shapes"); }
        GD.Print($"[SPELLFX] style audit: {n} element x shape styles ({Elements.Length} x {Shapes.Length}), {bad} missing");
        return bad;
    }
    public static string Describe(string kind, string type) => KindShapes.TryGetValue(kind, out var s) ? "style:" + El(type) + "." + string.Join("+", s) : "";

    // ------------------------------------------------------------------ power
    static readonly Dictionary<string, SkillCalc> _calc = new();
    /// <summary>Remember the resolved numbers of a skill being cast (impacts and beams look up its power by id).</summary>
    public static void Note(SkillCalc c) { _calc[c.Def.Id] = c; var f = Spellcraft.FxOf(c.Def.Id); if (f != c.Def.Id) _calc[f] = c; }
    /// <summary>1 = a hit of refDmg; +35% per doubling (log scale), clamped.</summary>
    public static float Power(float dmg) { Load(); if (dmg <= 0) return 1f; return Mathf.Clamp(1f + .35f * Mathf.Log(dmg / _refDmg) / Mathf.Log(2f), _pMin, _pMax); }
    public static float PowerOf(SkillCalc? c) => c == null ? 1f : Mathf.Clamp((c.Dmg > 0 ? Power(c.Dmg) : 1f + c.Heal) * (1f + .4f * c.Charge), _pMin, _pMax * 1.2f);
    public static float PowerOf(string id) => _calc.TryGetValue(id, out var c) ? PowerOf(c) : _calc.TryGetValue(Spellcraft.FxOf(id), out c) ? PowerOf(c) : 1f;
    public static float Q => _q[Mathf.Clamp(Game.Set?.Quality ?? 3, 0, 3)];
    public static Color BuffColor(SkillDef k)
    {
        Load(); foreach (var key in k.Buff.Keys) if (_buffCols.TryGetValue(key, out var c)) return c;
        return Get(k.Type, "buff").Glow;
    }

    // ------------------------------------------------------------------ shaders, materials, meshes (static, shared; per-effect numbers are instance uniforms)
    const string Head = "render_mode unshaded, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled, ";
    const string Inst = @"
instance uniform vec4 ca : source_color = vec4(1.0);
instance uniform vec4 cb : source_color = vec4(1.0);
instance uniform vec4 pa = vec4(1.0, 1.0, 1.0, 1.0);
instance uniform vec4 pb = vec4(0.08, 0.3, 0.0, 8.0);
";
    const string GroundBody = @"
float poly(vec2 p, float n, float r, float rot) { float an = PI / n; float a = atan(p.x, p.y) + rot; float bn = mod(a, 2.0 * an) - an; return length(p) * cos(bn) - r * cos(an); }
float ln(float sd, float w) { return 1.0 - smoothstep(0.0, w, abs(sd)); }
void fragment() {
	vec2 p = UV * 2.0 - 1.0;
	float d = length(p);
	float aa = max(fwidth(d), 0.002) * 1.5;
	float a = atan(p.y, p.x);
	float rot = TIME * pb.y + pb.z;
	int mode = int(pa.w + 0.5);
	vec3 col = vec3(0.0); float al = 0.0;
	if (mode == 0) {
		float r = pa.x; float w = max(pb.x, aa * 2.0);
		float band = smoothstep(r - w, r - w * 0.25, d) * (1.0 - smoothstep(r, r + aa, d));
		float hot = smoothstep(r - w * 0.35, r - w * 0.1, d) * (1.0 - smoothstep(r - w * 0.05, r + aa, d));
		float fill = step(d, r) * 0.25 * smoothstep(0.0, r, d) * smoothstep(0.0, r * 0.6, d);
		float spokes = pow(abs(sin(a * pb.w * 0.5 + pb.z)), 24.0) * band;
		col = cb.rgb * (band * 1.2 + fill) + ca.rgb * (hot * 1.8 + spokes * 1.2);
		al = clamp(band + fill + hot, 0.0, 1.0);
	} else if (mode == 1 || mode == 2) {
		float inside = 1.0 - smoothstep(1.0 - aa, 1.0, d);
		float rim = smoothstep(0.91 - aa, 0.91, d) * inside;
		float rim2 = ln(d - 0.80, aa * 1.5 + 0.006);
		float seg = (a + rot) / (2.0 * PI) * pb.w * 3.0;
		float cell = fract(seg); float h = fract(sin(floor(seg) * 12.9898 + pb.z * 7.0) * 43758.5453);
		float band = step(0.835, d) * step(d, 0.885);
		float glyph = min(band * step(0.18, cell) * step(cell, 0.82) * step(0.3, h) * (step(0.55, h) + step(0.5, fract((d - 0.835) * 40.0 + h))), 1.0);
		float af = fract(atan(p.x, p.y) / (2.0 * PI) + 1.0);
		float left = step(af, pa.z);
		float n = mode == 2 ? 3.0 : 4.0;
		float star = max(ln(poly(p, n, 0.62, rot), 0.012 + aa), ln(poly(p, n, 0.62, rot + PI / n), 0.012 + aa));
		float inner = ln(d - 0.30, 0.01 + aa) * (mode == 2 ? 1.0 : 0.6);
		float fill = (0.06 + 0.32 * d * d) * inside * (0.3 + 0.7 * pa.z);
		float lit = 0.12 + 0.88 * left;
		col = cb.rgb * (rim * lit * 1.7 + rim2 * 0.9 + glyph * 1.2 + star + inner * 0.8 + fill) + ca.rgb * (rim * left * 0.9 + fill * 0.6);
		al = clamp(rim * lit + rim2 * 0.7 + glyph + star * 0.8 + inner * 0.6 + fill, 0.0, 1.0);
		float tick = (1.0 - smoothstep(0.0, 0.015, abs(af - pa.z))) * smoothstep(0.80, 0.86, d) * inside * step(0.001, pa.z) * step(pa.z, 0.999);
		col += ca.rgb * tick * 2.5; al = max(al, tick);
	} else {
		float sp = sin(a * 3.0 + d * 9.0 - rot * 4.0);
		float arms = smoothstep(0.1, 0.8, sp) * (1.0 - smoothstep(0.55, 1.0, d)) * smoothstep(0.05, 0.2, d);
		float hole = 1.0 - smoothstep(0.0, 0.3, d);
		float rimg = ln(d - 0.9, 0.05);
		col = mix(ca.rgb, cb.rgb, smoothstep(0.2, 0.9, d) * arms) * (1.0 - hole * 0.6) + cb.rgb * rimg;
		al = clamp(arms * 0.85 + hole * 0.75 + rimg * 0.6, 0.0, 1.0);
	}
	ALBEDO = ADD ? col * pa.y : col; ALPHA = al * pa.y;
}";
    const string Billboard = @"
void vertex() {
	vec3 sc = vec3(length(MODEL_MATRIX[0].xyz), length(MODEL_MATRIX[1].xyz), length(MODEL_MATRIX[2].xyz));
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0] * sc.x, INV_VIEW_MATRIX[1] * sc.y, INV_VIEW_MATRIX[2] * sc.z, MODEL_MATRIX[3]);
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
}";
    const string FlareBody = @"
void fragment() {
	vec2 p = UV * 2.0 - 1.0; float d = length(p); float a = atan(p.y, p.x) + pa.w;
	float aa = max(fwidth(d), 0.002);
	float core = 1.0 - smoothstep(pa.x - aa * 1.5, pa.x, d);
	float glow = pow(max(0.0, 1.0 - d), 2.4);
	float spk = pa.y > 0.5 ? pow(abs(cos(a * pa.y * 0.5)), 60.0) * pow(max(0.0, 1.0 - d), 1.2) : 0.0;
	float ringf = pa.x > 0.01 ? (1.0 - smoothstep(0.0, aa * 2.0 + 0.02, abs(d - pa.x * 1.7))) * 0.45 : 0.0;
	vec3 c = ca.rgb * core * 2.2 + cb.rgb * (glow * 1.3 + spk * 2.0 + ringf);
	ALBEDO = c * pa.z; ALPHA = clamp(core + glow + spk + ringf, 0.0, 1.0) * pa.z;
}";
    const string PillarBody = @"
void fragment() {
	float h = UV.y;
	float av = pow(1.0 - h, 1.4) * smoothstep(0.0, 0.06, h) * 0.55;
	float f = 1.0 - abs(dot(NORMAL, VIEW));
	float s = fract(UV.x * pa.z + pa.w);
	float lines = smoothstep(0.0, 0.05, s) * (1.0 - smoothstep(0.1, 0.18, s));
	float mv = fract(h * 1.5 - TIME * pa.y + floor(UV.x * pa.z) * 0.37);
	float dash = smoothstep(0.55, 0.75, mv) * (1.0 - smoothstep(0.85, 1.0, mv));
	vec3 c = cb.rgb * (0.25 + pow(f, 2.0) * 1.4) + ca.rgb * lines * dash * 1.8;
	float al = clamp(0.18 + pow(f, 2.0) * 0.9 + lines * dash, 0.0, 1.0) * av * pa.x;
	ALBEDO = c * av * pa.x; ALPHA = al;
}";
    const string BeamBody = @"
void fragment() {
	float len = max(pa.y, 0.01); float y = UV.y * len;
	float x = UV.x * 2.0 - 1.0;
	x += pb.x * (sin(y * 6.0 + TIME * 37.0) * 0.22 + sin(y * 15.0 - TIME * 53.0) * 0.12);
	float ax = abs(x);
	float core = 1.0 - smoothstep(pb.y, pb.y + 0.08, ax);
	float glow = pow(max(0.0, 1.0 - ax), 2.2);
	float wave = 0.5 + 0.5 * sin(y * 2.4 + TIME * pa.z + pa.w);
	float pulse = smoothstep(0.7, 1.0, 0.5 + 0.5 * sin(y * 0.8 + TIME * pa.z * 0.5));
	float ends = smoothstep(0.0, 0.35, y) * smoothstep(0.0, 0.35, len - y);
	vec3 c = ca.rgb * core * 2.2 + cb.rgb * glow * (0.55 + 0.55 * wave + pulse);
	float al = clamp(core + glow * (0.45 + 0.45 * wave + pulse), 0.0, 1.0) * ends * pa.x;
	ALBEDO = c * ends * pa.x; ALPHA = al;
}";
    static ShaderMaterial? _groundAdd, _groundMix, _flare, _pillar, _beam;
    static ShaderMaterial Mat(string code) => new() { Shader = new Shader { Code = code } };
    static ShaderMaterial GroundAdd => _groundAdd ??= Mat("shader_type spatial;\n" + Head + "blend_add;\n" + Inst + "const bool ADD = true;\n" + GroundBody);
    static ShaderMaterial GroundMix => _groundMix ??= Mat("shader_type spatial;\n" + Head + "blend_mix;\n" + Inst + "const bool ADD = false;\n" + GroundBody);
    static ShaderMaterial FlareMat => _flare ??= Mat("shader_type spatial;\n" + Head + "blend_add;\n" + Inst + Billboard + FlareBody);
    static ShaderMaterial PillarMat => _pillar ??= Mat("shader_type spatial;\n" + Head + "blend_add;\n" + Inst + PillarBody);
    static ShaderMaterial BeamMat => _beam ??= Mat("shader_type spatial;\n" + Head + "blend_add;\n" + Inst + BeamBody);
    static PlaneMesh? _plane; static QuadMesh? _quad, _beamQuad; static CylinderMesh? _cyl;
    static PlaneMesh Plane => _plane ??= new PlaneMesh { Size = new Vector2(2, 2) };
    static QuadMesh Quad => _quad ??= new QuadMesh { Size = new Vector2(2, 2) };
    static QuadMesh BeamQuad => _beamQuad ??= new QuadMesh { Size = new Vector2(1, 1) };
    static CylinderMesh Cyl => _cyl ??= new CylinderMesh { TopRadius = 1, BottomRadius = 1, Height = 1, RadialSegments = 24, Rings = 1, CapTop = false, CapBottom = false };

    static MeshInstance3D Mi(Mesh m, Material mat) => new() { Mesh = m, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    static void P(GeometryInstance3D g, string n, Vector4 v) => g.SetInstanceShaderParameter(n, v);
    static void C(GeometryInstance3D g, Color a, Color b) { g.SetInstanceShaderParameter("ca", a); g.SetInstanceShaderParameter("cb", b); }

    /// <summary>A flat circle decal of radius r (mode 0 shock ring, 1 rune circle, 2 sigil, 3 swirl).</summary>
    static MeshInstance3D Decal(Node3D parent, Color a, Color b, float r, int mode, float seed, float runes, float spin, float thick = .1f, bool mix = false)
    {
        var m = Mi(Plane, mix ? GroundMix : GroundAdd); parent.AddChild(m); m.Scale = new Vector3(r, 1, r);
        C(m, a, b); P(m, "pa", new Vector4(mode == 0 ? .1f : 1f, 1f, 1f, mode)); P(m, "pb", new Vector4(thick, spin, seed, runes));
        return m;
    }
    static MeshInstance3D Flare(Node3D parent, Color a, Color b, float size, float spikes, float core = .22f)
    {
        var m = Mi(Quad, FlareMat); parent.AddChild(m); m.Scale = Vector3.One * size;
        C(m, a, b); P(m, "pa", new Vector4(core, spikes, 1f, GD.Randf() * 3f)); return m;
    }
    static MeshInstance3D Pillar(Node3D parent, Color a, Color b, float r, float h, float scroll = 1.5f)
    {
        var m = Mi(Cyl, PillarMat); parent.AddChild(m); m.Scale = new Vector3(r, h, r); m.Position = Vector3.Up * h * .5f;
        C(m, a, b); P(m, "pa", new Vector4(1f, scroll, 9f, GD.Randf())); return m;
    }

    // particles: process materials and sprite quads cached by key
    static readonly Dictionary<string, ParticleProcessMaterial> _pms = new();
    static readonly Dictionary<string, QuadMesh> _sprites = new();
    static readonly Dictionary<string, GradientTexture1D> _ramps = new();
    static CurveTexture? _shrink, _swell;
    static CurveTexture Shrink { get { if (_shrink != null) return _shrink; var c = new Curve(); c.AddPoint(new Vector2(0, 1)); c.AddPoint(new Vector2(1, 0)); return _shrink = new CurveTexture { Curve = c }; } }
    static CurveTexture Swell { get { if (_swell != null) return _swell; var c = new Curve(); c.AddPoint(new Vector2(0, .35f)); c.AddPoint(new Vector2(1, 1)); return _swell = new CurveTexture { Curve = c }; } }
    static GradientTexture1D Ramp(Style s, bool dark = false)
    {
        string key = (dark ? "d" : "") + s.Core.ToHtml() + s.Glow.ToHtml() + s.Edge.ToHtml() + s.Dark.ToHtml() + s.Add;
        if (_ramps.TryGetValue(key, out var t)) return t;
        var g = new Gradient();
        if (dark) { g.SetColor(0, new Color(s.Dark, .9f)); g.AddPoint(.5f, new Color(s.Edge.Darkened(.4f), .6f)); g.SetColor(1, new Color(s.Dark, 0)); }
        else if (!s.Add) { g.SetColor(0, new Color(s.Core, 1)); g.AddPoint(.35f, new Color(s.Glow, .85f)); g.SetColor(1, new Color(s.Edge.Darkened(.3f), 0)); }
        else { g.SetColor(0, new Color(s.Core, 1)); g.AddPoint(.3f, new Color(s.Glow, 1)); g.SetColor(1, new Color(s.Edge, 0)); }
        return _ramps[key] = new GradientTexture1D { Gradient = g, Width = 64 };
    }
    static QuadMesh Sprite(string set, bool add, float size)
    {
        string key = $"{set}|{add}|{Mathf.RoundToInt(size * 20)}";
        if (_sprites.TryGetValue(key, out var q)) return q;
        return _sprites[key] = new QuadMesh { Size = Vector2.One * Mathf.Max(.05f, Mathf.Round(size * 20) / 20f), Material = Vfx.SpriteMat(add, set) };
    }
    enum Emit { Sphere, Ring, Disc }
    static ParticleProcessMaterial Pm(string key, Style s, Emit e, float r, float vel, float spread, Vector3 dir, float radial = 0, bool dark = false, float tangent = 0)
    {
        key = $"{key}|{e}|{Mathf.RoundToInt(r * 4)}|{Mathf.RoundToInt(vel * 4)}|{Mathf.RoundToInt(radial * 4)}|{dark}|{s.Glow.ToHtml()}";
        if (_pms.TryGetValue(key, out var pm)) return pm;
        pm = new ParticleProcessMaterial
        {
            Direction = dir, Spread = spread, InitialVelocityMin = vel * .4f, InitialVelocityMax = vel, Gravity = new Vector3(0, s.Gravity, 0),
            DampingMin = 1f, DampingMax = 3f, ScaleMin = .55f, ScaleMax = 1.25f, ScaleCurve = s.Add && !dark ? Shrink : Swell, ColorRamp = Ramp(s, dark),
            AngleMin = 0, AngleMax = s.Spin > 0 ? 360 : 0, AngularVelocityMin = -s.Spin, AngularVelocityMax = s.Spin,
        };
        if (radial != 0) { pm.RadialVelocityMin = radial * .6f; pm.RadialVelocityMax = radial; }
        if (tangent != 0) { pm.TangentialAccelMin = tangent * .7f; pm.TangentialAccelMax = tangent; }
        switch (e)
        {
            case Emit.Sphere: pm.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere; pm.EmissionSphereRadius = Mathf.Max(.02f, r); break;
            case Emit.Ring: pm.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Ring; pm.EmissionRingAxis = Vector3.Up; pm.EmissionRingRadius = r; pm.EmissionRingInnerRadius = r * .85f; pm.EmissionRingHeight = .05f; break;
            case Emit.Disc: pm.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Ring; pm.EmissionRingAxis = Vector3.Up; pm.EmissionRingRadius = r; pm.EmissionRingInnerRadius = 0; pm.EmissionRingHeight = .05f; break;
        }
        return _pms[key] = pm;
    }
    static GpuParticles3D Parts(Node3D parent, ParticleProcessMaterial pm, Style s, float amount, float life, float size, bool oneShot, float extent = 4f, bool dark = false)
    {
        var p = new GpuParticles3D
        {
            Amount = Mathf.Max(2, Mathf.RoundToInt(amount * Q)), Lifetime = Mathf.Max(.05f, life), OneShot = oneShot, Explosiveness = oneShot ? .92f : 0f, Randomness = .3f,
            ProcessMaterial = pm, DrawPass1 = Sprite(s.Set, s.Add && !dark, size), LocalCoords = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityAabb = new Aabb(-Vector3.One * extent, Vector3.One * extent * 2), Emitting = true,
        };
        parent.AddChild(p); return p;
    }

    static T Spawn<T>(T n, Vector3 at, Node3D? parent = null) where T : Node3D
    {
        n.AddToGroup("spellstyles");
        var host = parent ?? Game.Root; if (host == null) { n.Free(); return n; }
        host.AddChild(n); if (parent == null) n.GlobalPosition = at; else n.Position = at;
        Made++; return n;
    }
    /// <summary>The floor under p (world layer 1, the hero excluded; a ray from 1.2 m above to 3 m below), or p.Y if none: ground circles sit on blocks and slopes.</summary>
    public static float FloorY(Vector3 p)
    {
        var space = Game.Root?.GetWorld3D()?.DirectSpaceState; if (space == null) return p.Y;
        var q = PhysicsRayQueryParameters3D.Create(p + Vector3.Up * 1.2f, p + Vector3.Down * 3f, 1);
        if (Player.Instance is { } pl && GodotObject.IsInstanceValid(pl)) q.Exclude = new Godot.Collections.Array<Rid> { pl.GetRid() };
        var hit = space.IntersectRay(q);
        return hit.Count > 0 && ((Vector3)hit["normal"]).Y > .5f ? ((Vector3)hit["position"]).Y : p.Y;
    }
    static Vector3 OnFloor(Vector3 p) => new(p.X, FloorY(p) + .12f, p.Z);
    /// <summary>Effects started (tests and DevCapture).</summary>
    public static int Made;
    static float Sz(float power) => Mathf.Pow(Mathf.Max(.3f, power), .8f);
    static float Ease(float k) => 1f - (1f - k) * (1f - k) * (1f - k);

    // ------------------------------------------------------------------ effects
    /// <summary>Burst + sparks + flash ring at a hit; bigger and brighter with power.</summary>
    public static FxNode? Impact(Vector3 at, string el, float power = 1f)
    {
        if (Game.Root == null) return null;
        var s = Get(el, "impact"); float sz = Sz(power);
        var n = Spawn(new FxNode { Life = Mathf.Max(.45f, s.Life + .1f) }, at);
        var fl = Flare(n, s.Core, s.Glow, .8f * sz, s.Spikes, .2f);
        var ring = Decal(n, s.Core, s.Edge, 1.1f * sz, 0, GD.Randf() * 6f, Mathf.Max(4, s.Spikes * 2), 0, .18f);
        float rot0 = GD.Randf() * 3f;
        n.Tick = (f, k) =>
        {
            float e = Ease(Mathf.Min(1f, k * 1.6f)), a = Mathf.Clamp(1f - k * 1.5f, 0, 1);
            fl.Scale = Vector3.One * .8f * sz * (.45f + .9f * e);
            P(fl, "pa", new Vector4(.2f * a, s.Spikes, a, rot0 + k * .8f));
            P(ring, "pa", new Vector4(.15f + .85f * e, a, 1, 0));
        };
        Parts(n, Pm("imp" + el, s, Emit.Sphere, .08f * sz, s.Speed * sz, 180, Vector3.Up), s, s.Amount * Mathf.Sqrt(power), s.Life, s.Size * sz, true);
        if (power > 1.4f) Vfx.Flash(at, s.Glow, 2.5f * power, 3f * sz, .25f);
        return n;
    }

    /// <summary>Expanding shock ring with spokes, an inner rune flash and particles flung outward along the ground.</summary>
    public static FxNode? Nova(Vector3 at, float r, string el, float power = 1f)
    {
        if (Game.Root == null) return null;
        var s = Get(el, "nova"); float sz = Sz(power); r = Mathf.Max(1f, r);
        var n = Spawn(new FxNode { Life = .7f }, OnFloor(at));
        var ring = Decal(n, s.Core, s.Edge, r * 1.04f, 0, GD.Randf() * 6f, s.Runes * 2, 0, Mathf.Clamp(.5f / r, .05f, .2f) * sz);
        var rune = Decal(n, s.Core, s.Glow, r * .7f, 1, GD.Randf() * 9f, s.Runes, 1.5f);
        var fl = Flare(n, s.Core, s.Glow, 1.1f * sz, s.Spikes, .2f); fl.Position = Vector3.Up * .7f;
        n.Tick = (f, k) =>
        {
            float e = Ease(Mathf.Min(1f, k * 1.9f)), a = Mathf.Clamp(1.25f - k * 1.3f, 0, 1);
            P(ring, "pa", new Vector4(.05f + .95f * e, a, 1, 0));
            float ra = Mathf.Clamp(1f - k * 2.2f, 0, 1); P(rune, "pa", new Vector4(1, ra, 1, 1)); rune.Scale = new Vector3(r * (.5f + .3f * e), 1, r * (.5f + .3f * e));
            P(fl, "pa", new Vector4(.2f * ra, s.Spikes, ra, k));
        };
        float life = s.Life; Parts(n, Pm("nova" + el, s, Emit.Ring, r * .15f, 0, 10, Vector3.Up, r * 1.5f / life), s, s.Amount * Mathf.Sqrt(r / 3f) * power, life, s.Size * sz, true, r + 2);
        return n;
    }

    /// <summary>One-shot rune circle on the ground (a strike landing, a slam, a trap springing) with particles rising off it and an impact at the centre.</summary>
    public static FxNode? Aoe(Vector3 at, float r, string el, float power = 1f, float hold = .3f)
    {
        if (Game.Root == null) return null;
        var s = Get(el, "aoe"); float sz = Sz(power); r = Mathf.Max(.8f, r);
        var n = Spawn(new FxNode { Life = hold + .5f }, OnFloor(at));
        var rune = Decal(n, s.Core, s.Edge, r, 1, GD.Randf() * 9f, s.Runes, 1.2f);
        var shock = Decal(n, s.Core, s.Glow, r * 1.1f, 0, GD.Randf() * 6f, s.Runes * 2, 0, .12f);
        n.Tick = (f, k) =>
        {
            float t = f.T, pop = Ease(Mathf.Min(1f, t / .14f)), a = t < hold ? 1f : Mathf.Clamp(1f - (t - hold) / .5f, 0, 1);
            rune.Scale = new Vector3(r * (.8f + .2f * pop), 1, r * (.8f + .2f * pop)); P(rune, "pa", new Vector4(1, a * Mathf.Min(1f, .6f + .4f * power), 1, 1));
            float e = Ease(Mathf.Min(1f, t / .35f)); P(shock, "pa", new Vector4(.1f + .9f * e, Mathf.Clamp(1f - t / .4f, 0, 1), 1, 0));
        };
        Parts(n, Pm("aoe" + el, s, Emit.Disc, r * .9f, s.Speed * sz, 15, Vector3.Up), s, s.Amount * Mathf.Sqrt(r / 2.5f) * power, s.Life, s.Size * sz, true, r + 2);
        Impact(at + Vector3.Up * .5f, el, power);
        return n;
    }

    /// <summary>
    /// A lingering ground effect: rune circle at the radius with a soft inner fill and motes rising. Its rim lights only the part of the
    /// circle that matches the time left (it drains around like a clock) and the whole circle fades as the time runs out.
    /// The handle counts down from dur by itself; SetTimeLeft overrides it, Stop fades it out. parent: follows that node (position is local).
    /// </summary>
    public static LingerFx? Linger(Vector3 at, float radius, float dur, string el, float power = 1f, Node3D? parent = null)
    {
        if (Game.Root == null) return null;
        var s = Get(el, "linger");
        var n = Spawn(new LingerFx { Dur = Mathf.Max(.1f, dur), Left = Mathf.Max(.1f, dur), R = Mathf.Max(.5f, radius), S = s, Power = power, El = El(el) }, parent == null ? OnFloor(at) : at + Vector3.Up * .12f, parent);
        return n;
    }

    /// <summary>Rising aura column, a ring bursting out at the feet and a sigil under them; colour by buff (tint) or element.</summary>
    public static FxNode? Buff(Node3D target, string el, float power = 1f, Color? tint = null)
    {
        if (Game.Root == null || !GodotObject.IsInstanceValid(target)) return null;
        var s = Get(el, "buff"); if (tint is { } tc) { s = s.Copy(); s.Glow = tc; s.Edge = tc.Darkened(.15f); }
        float sz = Sz(power);
        var n = Spawn(new FxNode { Life = 2f }, Vector3.Up * .08f, target);
        var col = Pillar(n, s.Core, s.Glow, .7f * sz, 2.4f * sz, 1.8f);
        var ring = Decal(n, s.Core, s.Edge, 1.5f * sz, 0, GD.Randf() * 6f, 12, 0, .2f);
        var sig = Decal(n, s.Core, s.Glow, 1.05f * sz, 2, GD.Randf() * 9f, s.Runes, 1.2f);
        GpuParticles3D? pp = null;
        n.Tick = (f, k) =>
        {
            float t = f.T, env = Mathf.Clamp(Mathf.Min(t / .15f, (1.9f - t) / .6f), 0, 1);
            P(col, "pa", new Vector4(env, 1.8f, 9f, .3f)); col.Scale = new Vector3(.7f * sz * (.8f + .2f * env), 2.4f * sz * (.6f + .4f * Ease(Mathf.Min(1, t / .3f))), .7f * sz * (.8f + .2f * env));
            float e = Ease(Mathf.Min(1f, t / .45f)); P(ring, "pa", new Vector4(.1f + .9f * e, Mathf.Clamp(1f - t / .6f, 0, 1), 1, 0));
            P(sig, "pa", new Vector4(1, env, 1, 2));
            if (pp != null && t > 1.1f) pp.Emitting = false;
        };
        pp = Parts(n, Pm("buff" + el, s, Emit.Ring, .6f * sz, s.Speed, 12, Vector3.Up), s, s.Amount * power, s.Life, s.Size * sz, false);
        return n;
    }

    /// <summary>A dark swirl over the target's head (and smoke dripping from it) for dur seconds (max 6).</summary>
    public static FxNode? Debuff(Node3D target, string el, float dur = 2f, float height = 2f)
    {
        if (Game.Root == null || !GodotObject.IsInstanceValid(target)) return null;
        var s = Get(el, "debuff"); float life = Mathf.Clamp(dur, 1f, 6f);
        var n = Spawn(new FxNode { Life = life }, Vector3.Up * (height + .35f), target);
        var sw = Decal(n, s.Dark, s.Edge, .6f, 3, GD.Randf() * 6f, 0, 1.4f, .1f, true);
        var glow = Decal(n, s.Core, s.Glow, .75f, 0, GD.Randf() * 6f, 6, 0, .25f); glow.Position = Vector3.Up * .02f;
        var pp = Parts(n, Pm("debuff" + el, s, Emit.Ring, .45f, s.Speed, 30, Vector3.Down, 0, true, 2f), s, s.Amount, s.Life, s.Size, false, 3f, true);
        n.Tick = (f, k) =>
        {
            float t = f.T, env = Mathf.Clamp(Mathf.Min(t / .25f, (life - t) / .4f), 0, 1);
            P(sw, "pa", new Vector4(1, env * .95f, 1, 3)); P(glow, "pa", new Vector4(.95f, env * .7f, 1, 0));
            sw.Scale = new Vector3(.6f, 1, .6f) * (.7f + .3f * env);
            if (t > life - s.Life) pp.Emitting = false;
        };
        return n;
    }

    /// <summary>Soft green/gold pillar of light with motes and leaves of light rising, a rune ring at the feet.</summary>
    public static FxNode? Heal(Node3D target, float power = 1f)
    {
        if (Game.Root == null || !GodotObject.IsInstanceValid(target)) return null;
        var s = Get("holy", "heal"); float sz = Sz(power);
        var n = Spawn(new FxNode { Life = 1.9f }, Vector3.Up * .08f, target);
        var col = Pillar(n, s.Core, s.Glow, .85f * sz, 3.6f * Mathf.Sqrt(power), 1.1f);
        var rune = Decal(n, s.Core, s.Edge, 1.2f * sz, 1, GD.Randf() * 9f, s.Runes, .8f);
        GpuParticles3D? pp = null;
        n.Tick = (f, k) =>
        {
            float t = f.T, env = Mathf.Clamp(Mathf.Min(t / .2f, (1.8f - t) / .7f), 0, 1);
            P(col, "pa", new Vector4(env * .9f, 1.1f, 7f, .1f)); P(rune, "pa", new Vector4(1, env, 1, 1));
            if (pp != null && t > 1f) pp.Emitting = false;
        };
        pp = Parts(n, Pm("heal", s, Emit.Disc, .8f * sz, s.Speed, 20, Vector3.Up, 0, false, 1.5f), s, s.Amount * power, s.Life, s.Size * sz, false);
        return n;
    }
    public static FxNode? Heal(Vector3 at, float power = 1f)
    {
        var host = Game.Root; if (host == null) return null;
        var a = new Node3D(); host.AddChild(a); a.GlobalPosition = at; a.AddToGroup("spellstyles");
        var fx = Heal(a, power); a.GetTree().CreateTimer(2.0).Timeout += () => { if (GodotObject.IsInstanceValid(a)) a.QueueFree(); };
        return fx;
    }

    /// <summary>Sigil circle (hexagram) that spins up, a flash column and sparks thrown up: something is called into the world.</summary>
    public static FxNode? Summon(Vector3 at, string el, float power = 1f)
    {
        if (Game.Root == null) return null;
        var s = Get(el, "summon"); float sz = Sz(power), r = 1.5f * Mathf.Sqrt(power);
        var n = Spawn(new FxNode { Life = 1.3f }, OnFloor(at));
        var sig = Decal(n, s.Core, s.Edge, r, 2, GD.Randf() * 9f, s.Runes, 2.2f);
        var col = Pillar(n, s.Core, s.Glow, .55f * sz, 3.2f * sz, 3f);
        var fl = Flare(n, s.Core, s.Glow, 1.6f * sz, s.Spikes, .25f); fl.Position = Vector3.Up * 1.3f;
        n.Tick = (f, k) =>
        {
            float t = f.T, pop = Ease(Mathf.Min(1f, t / .25f)), a = Mathf.Clamp((1.3f - t) / .5f, 0, 1);
            sig.Scale = new Vector3(r * pop, 1, r * pop); P(sig, "pa", new Vector4(1, a, 1, 2));
            float fa = Mathf.Clamp(1f - t / .45f, 0, 1); P(col, "pa", new Vector4(fa, 3f, 6f, 0)); P(fl, "pa", new Vector4(.25f * fa, s.Spikes, fa, t));
        };
        Parts(n, Pm("summon" + el, s, Emit.Ring, r * .8f, s.Speed * sz, 15, Vector3.Up), s, s.Amount * power, s.Life, s.Size * sz, true, r + 3);
        return n;
    }

    /// <summary>Core + glow + trail on a flying node (spell bolts, glaives). Freed with it.</summary>
    public static FxNode? Projectile(Node3D proj, string el, float power = 1f, float scale = 1f)
    {
        if (!GodotObject.IsInstanceValid(proj)) return null;
        var s = Get(el, "projectile"); float sz = Sz(power) * scale;
        var n = new FxNode { Life = 0 }; n.AddToGroup("spellstyles"); proj.AddChild(n); Made++;
        var halo = Flare(n, s.Core, s.Glow, 1.25f * sz, 0, 0f); P(halo, "pa", new Vector4(0, 0, .8f, 0));
        var core = Flare(n, s.Core, s.Glow, .6f * sz, s.Spikes, .3f);
        float rot = GD.Randf() * 3f;
        n.Tick = (f, k) => P(core, "pa", new Vector4(.3f + .03f * Mathf.Sin(f.T * 30f), s.Spikes, 1, rot + f.T * 2.5f));
        Parts(n, Pm("proj" + el, s, Emit.Sphere, .1f * sz, s.Speed, 180, Vector3.Up), s, s.Amount * Mathf.Sqrt(power), s.Life, s.Size * sz, false, 6f);
        return n;
    }

    /// <summary>A scrolling ribbon from a to b. life &gt; 0: fades out and frees after that long; 0: stays until Stop (move it with Set).</summary>
    public static BeamFx? Beam(Vector3 a, Vector3 b, string el, float power = 1f, float life = .3f, Node3D? parent = null)
    {
        if (Game.Root == null) return null;
        var s = Get(el, "beam");
        var n = new BeamFx { S = s, Life = life, Width = .85f * Sz(power), El = El(el), Power = power };
        n.AddToGroup("spellstyles"); (parent ?? Game.Root).AddChild(n); Made++; n.Set(a, b);
        return n;
    }
    /// <summary>A chain or hook segment of a skill: its element and power (as last cast).</summary>
    public static BeamFx? BeamFor(string skillId, Vector3 a, Vector3 b, float life = .3f) => Beam(a, b, Skills.Get(Spellcraft.FxOf(skillId))?.Type ?? "arcane", PowerOf(skillId), life);

    // ------------------------------------------------------------------ dev: rows of effects, the 10-effect load, warm-up
    /// <summary>One effect of a shape per element in a row along `right` (DevCapture "fxshow:&lt;shape&gt;").</summary>
    public static void Show(string shape, Vector3 center, Vector3 right, Vector3 fwd, Node3D? dummyParent = null)
    {
        int n = Elements.Length;
        for (int i = 0; i < n; i++)
        {
            string el = Elements[i]; float pw = .8f + 1.2f * i / (n - 1f);   // power rises along the row: stronger spells look stronger
            var at = center + right * ((i - (n - 1) / 2f) * 2.4f); at.Y = Ground(at);
            switch (shape)
            {
                case "projectiles":
                {   // each bolt flies a small loop so its trail shows
                    var o = at + Vector3.Up * 1.3f; var d = Dummy(o, 8f); Projectile(d, el, pw); float ph = i;
                    Spawn(new FxNode { Life = 8f, Tick = (f, k) => { if (GodotObject.IsInstanceValid(d)) d.GlobalPosition = o + new Vector3(Mathf.Cos(f.T * 5f + ph) * .9f, Mathf.Sin(f.T * 10f) * .15f, Mathf.Sin(f.T * 5f + ph) * .9f); } }, o);
                    break;
                }
                case "novas": Nova(at, 1.1f + .2f * pw, el, pw); break;
                case "aoe_linger": Linger(at, 1.05f, 6f, el, pw)?.SetTimeLeft(6f * (1f - i / (float)n)); break;   // time left shown from full to nearly gone
                case "buff_heal": { var d = Dummy(at, 8f); if (i % 2 == 0) Buff(d, el, pw); else Heal(d, pw); Debuff(Dummy(at + fwd * 2.6f, 8f), el, 6f, 1.2f); break; }
                case "beam_summon": Beam(at + Vector3.Up * .3f + fwd * 4f, at + Vector3.Up * 2.6f - fwd * .5f, el, pw, 0f); Summon(at + fwd * 1.4f, el, pw); break;
                case "impacts": Impact(at + Vector3.Up, el, pw); Aoe(at + fwd * 2f, 1f, el, pw, 4f); break;
            }
        }
    }
    /// <summary>The perf load: 10 mixed effects (3 lingering AoEs, a beam, a buff, a heal, a debuff, a projectile, a summon, a nova).</summary>
    public static void Fx10(Vector3 c, Vector3 right, Vector3 fwd)
    {
        Vector3 G(Vector3 p) { p.Y = Ground(p); return p; }
        Linger(G(c - right * 3f), 2.2f, 30f, "poison", 1.2f); Linger(G(c), 2.6f, 30f, "fire", 1.6f); Linger(G(c + right * 3f), 2.2f, 30f, "frost", 1f);
        Beam(G(c - right * 4f + fwd * 3f) + Vector3.Up, G(c + right * 4f + fwd * 3f) + Vector3.Up * 1.5f, "lightning", 1.4f, 0f);
        Buff(Dummy(G(c - right * 1.5f - fwd * 1.5f), 30f), "holy", 1.2f, null); Heal(Dummy(G(c + right * 1.5f - fwd * 1.5f), 30f), 1.2f);
        Debuff(Dummy(G(c + fwd * 1.5f), 30f), "shadow", 30f, 1.4f);
        Projectile(Dummy(G(c + fwd * .5f) + Vector3.Up * 1.4f, 30f), "lightning", 1.5f);
        // the two one-shot effects go off again every 0.6 s, so all 10 stay on screen while the frame rate is measured
        float next = 0f; var sp = G(c - right * 4.5f); var np = G(c + right * 4.5f);
        Spawn(new FxNode { Life = 30f, Tick = (f, k) => { if (f.T < next) return; next = f.T + .6f; Summon(sp, "shadow", 1.3f); Nova(np, 2.5f, "fire", 1.5f); } }, c);
    }
    /// <summary>Every element x shape once (Warmup: builds every cached material, mesh and particle shader before play).</summary>
    public static void WarmAll(Vector3 p)
    {
        foreach (var el in Elements)
        {
            var d = Dummy(p, 1f);
            Projectile(d, el); Beam(p, p + Vector3.Up, el); Nova(p, 2f, el); Aoe(p, 2f, el); Linger(p, 2f, 1f, el); Buff(d, el); Debuff(d, el); Summon(p, el); Impact(p, el);
        }
        Heal(p);
    }
    static float Ground(Vector3 p) => Dungeon.Active || World.GroundAt(p.X, p.Z) < -50f ? p.Y : World.GroundAt(p.X, p.Z);
    /// <summary>A bare node to hang target effects on (dev rows); freed after `life` s.</summary>
    static Node3D Dummy(Vector3 at, float life)
    {
        var d = new Node3D(); d.AddToGroup("spellstyles"); Game.Root!.AddChild(d); d.GlobalPosition = at;
        d.GetTree().CreateTimer(life).Timeout += () => { if (GodotObject.IsInstanceValid(d)) d.QueueFree(); };
        return d;
    }
    /// <summary>Free every effect of the library (DevCapture between views).</summary>
    public static void ClearAll(SceneTree tree) { foreach (var n in tree.GetNodesInGroup("spellstyles")) if (n is Node nn && GodotObject.IsInstanceValid(nn)) nn.QueueFree(); }

    internal static void DecalSet(MeshInstance3D m, Vector4 pa) => P(m, "pa", pa);
    internal static MeshInstance3D MakeDecal(Node3D parent, Style s, float r, int mode, float runes, float spin, float thick = .1f) => Decal(parent, s.Core, s.Edge, r, mode, GD.Randf() * 9f, runes, spin, thick);
    internal static MeshInstance3D MakeFlare(Node3D parent, Style s, float size, float spikes, float core) => Flare(parent, s.Core, s.Glow, size, spikes, core);
    internal static MeshInstance3D MakeBeamMesh(Node3D parent, Style s) { var m = Mi(BeamQuad, BeamMat); parent.AddChild(m); C(m, s.Core, s.Glow); return m; }
    internal static void SetP(GeometryInstance3D g, string n, Vector4 v) => P(g, n, v);
    internal static GpuParticles3D MakeLingerParts(Node3D parent, Style s, string el, float r, float power) =>
        Parts(parent, Pm("linger" + el, s, Emit.Disc, r * .9f, s.Speed, 15, Vector3.Up, 0, false, 0), s, s.Amount * Mathf.Clamp(r * r / 4f, .6f, 3f) * Mathf.Sqrt(power), s.Life, s.Size * Sz(power), false, r + 2);
    internal static GpuParticles3D MakeBeamParts(Node3D parent, Style s, string el, float power) =>
        Parts(parent, Pm("beamend" + el, s, Emit.Sphere, .15f, s.Speed, 180, Vector3.Up), s, s.Amount * Mathf.Sqrt(power), s.Life, s.Size * Sz(power), false, 3f);
}

/// <summary>A library effect: runs its Tick (t / life 0..1) each frame and frees itself after Life seconds (0 = lives with its parent).</summary>
public partial class FxNode : Node3D
{
    public float Life = 1f, T; public System.Action<FxNode, float>? Tick;
    public override void _Process(double delta)
    {
        T += (float)delta; float k = Life > 0 ? Mathf.Clamp(T / Life, 0, 1) : 0;
        Tick?.Invoke(this, k);
        if (Life > 0 && T >= Life) QueueFree();
    }
}

/// <summary>
/// A lingering AoE's ground effect (SpellStyles.Linger). The rim drains around like a clock as the time runs out, the fill and the whole
/// circle fade with it, motes rise while it lasts. Counts down by itself; SetTimeLeft takes over, Stop fades it out and frees it.
/// </summary>
public partial class LingerFx : Node3D
{
    public float Dur = 5f, Left = 5f, R = 2f, Power = 1f; public string El = "arcane"; public SpellStyles.Style S = null!;
    /// <summary>False: the owner drives the time left with SetTimeLeft (it does not count down by itself).</summary>
    public bool AutoCount = true;
    MeshInstance3D _decal = null!, _shock = null!; GpuParticles3D _parts = null!; float _t, _stopT = -1, _snap;
    public float TimeLeft => Left;
    public override void _Ready()
    {
        _decal = SpellStyles.MakeDecal(this, S, R, 1, S.Runes, .35f);
        _shock = SpellStyles.MakeDecal(this, S, R * 1.05f, 0, S.Runes * 2, 0, .1f);
        _parts = SpellStyles.MakeLingerParts(this, S, El, R, Power);
    }
    public void SetTimeLeft(float seconds) { AutoCount = false; Left = Mathf.Clamp(seconds, 0, Dur); }
    public void SetRadius(float r) { R = Mathf.Max(.3f, r); }
    public void Stop() { if (_stopT < 0) { _stopT = 0; if (IsInstanceValid(_parts)) _parts.Emitting = false; } }
    public override void _Process(double delta)
    {
        float dt = (float)delta; _t += dt;
        // on a skill object (zone, well) that may move: keep sitting on the floor under it (checked twice a second)
        _snap -= dt;
        if (_snap <= 0 && GetParent() is Node3D par && par != Game.Root) { _snap = .5f; var g = par.GlobalPosition; Position = Position with { Y = SpellStyles.FloorY(g) + .12f - g.Y }; }
        if (AutoCount) Left = Mathf.Max(0, Left - dt);
        if (Left <= 0 && AutoCount) Stop();
        float frac = Mathf.Clamp(Left / Dur, 0, 1), fadeIn = Mathf.Clamp(_t / .2f, 0, 1), stop = 1f;
        if (_stopT >= 0) { _stopT += dt; stop = Mathf.Clamp(1f - _stopT / .35f, 0, 1); if (_stopT > .4f) { QueueFree(); return; } }
        float pop = 1f - (1f - fadeIn) * (1f - fadeIn);
        _decal.Scale = new Vector3(R * (.85f + .15f * pop), 1, R * (.85f + .15f * pop));
        SpellStyles.DecalSet(_decal, new Vector4(1, (.45f + .55f * frac) * fadeIn * stop * Mathf.Min(1.1f, .8f + .2f * Power), frac, 1));
        _shock.Scale = new Vector3(R * 1.05f, 1, R * 1.05f);
        float e = Mathf.Clamp(_t / .4f, 0, 1); SpellStyles.DecalSet(_shock, new Vector4(.2f + .8f * (1f - (1f - e) * (1f - e)), (1f - e) * stop, 1, 0));
    }
}

/// <summary>A scrolling ribbon between two points that turns to face the camera (SpellStyles.Beam), with a flare at each end.</summary>
public partial class BeamFx : Node3D
{
    public SpellStyles.Style S = null!; public float Life = .3f, Width = .55f, Power = 1f; public string El = "arcane";
    Vector3 _a, _b; float _t, _stopT = -1; MeshInstance3D _m = null!, _fa = null!, _fb = null!; GpuParticles3D? _sparks;
    public override void _Ready()
    {
        _m = SpellStyles.MakeBeamMesh(this, S);
        _fa = SpellStyles.MakeFlare(this, S, Width * 1.4f, S.Spikes, .25f); _fb = SpellStyles.MakeFlare(this, S, Width * 2f, S.Spikes, .25f);
        if (Life <= 0) _sparks = SpellStyles.MakeBeamParts(this, S, El, Power);
        Orient();
    }
    public void Set(Vector3 a, Vector3 b) { _a = a; _b = b; if (IsInsideTree()) Orient(); }
    public void Stop() { if (_stopT < 0) _stopT = 0; }
    void Orient()
    {
        if (_m == null) return;
        var d = _b - _a; float len = d.Length(); if (len < .05f) { _m.Visible = false; return; }
        _m.Visible = true; var y = d / len;
        var cam = GetViewport()?.GetCamera3D(); var toCam = (cam?.GlobalPosition ?? _a + Vector3.Back) - (_a + _b) / 2;
        var x = y.Cross(toCam).Normalized(); if (x.LengthSquared() < .5f) x = y.Cross(Vector3.Right).Normalized();
        var z = x.Cross(y);
        _m.GlobalTransform = new Transform3D(new Basis(x * Width, y * len, z), (_a + _b) / 2);
        _fa.GlobalPosition = _a; _fb.GlobalPosition = _b; if (_sparks != null) _sparks.GlobalPosition = _b;
        float fade = Life > 0 ? Mathf.Clamp((Life - _t) / (Life * .5f), 0, 1) : 1f;
        if (_stopT >= 0) fade *= Mathf.Clamp(1f - _stopT / .25f, 0, 1);
        float jag = El == "lightning" ? 1f : El == "shadow" ? .3f : 0f;
        SpellStyles.SetP(_m, "pa", new Vector4(fade, len / Width, S.Scroll, 0)); SpellStyles.SetP(_m, "pb", new Vector4(jag, .16f, 0, 0));
        SpellStyles.SetP(_fa, "pa", new Vector4(.25f * fade, S.Spikes, fade, _t * 2f)); SpellStyles.SetP(_fb, "pa", new Vector4(.25f * fade, S.Spikes, fade, -_t * 3f));
    }
    public override void _Process(double delta)
    {
        _t += (float)delta; if (_stopT >= 0) _stopT += (float)delta;
        if ((Life > 0 && _t >= Life) || _stopT > .3f) { QueueFree(); return; }
        Orient();
    }
}
