using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>
/// One look for everything (procedural meshes and imported models alike), in an anime / Genshin-like style:
/// 1. every StandardMaterial3D that enters the scene gets toon diffuse + toon specular (flat light/shadow bands) and a soft rim light;
/// 2. a full-screen pass draws soft coloured outlines where depth or surface direction changes, fading out with distance.
/// Settings: data/look.json "toon".
/// </summary>
public partial class Toon : Node3D
{
    static bool _on = true; static float _rim = .3f, _rimTint = .6f, _soft = .55f;
    readonly HashSet<ulong> _done = new();
    MeshInstance3D _outline = null!;

    static void LoadCfg(out float width, out float depthT, out float normalT, out float fade, out Color col, out float alpha)
    {
        width = 1.2f; depthT = .07f; normalT = .45f; fade = 90f; col = new Color(.16f, .11f, .12f); alpha = .75f;
        using var doc = JsonUtil.Load("res://data/look.json");
        if (!doc.RootElement.TryGetProperty("toon", out var t)) return;
        float F(string k, float d) => t.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : d;
        _on = !t.TryGetProperty("enabled", out var en) || en.ValueKind != JsonValueKind.False;
        _rim = F("rim", _rim); _rimTint = F("rimTint", _rimTint); _soft = F("bandSoftness", _soft);
        width = F("outlineWidth", width); depthT = F("outlineDepth", depthT); normalT = F("outlineNormal", normalT); fade = F("outlineFade", fade); alpha = F("outlineAlpha", alpha);
        if (t.TryGetProperty("outlineColor", out var c) && c.ValueKind == JsonValueKind.String) col = Color.FromHtml(c.GetString()!);
    }

    public override void _Ready()
    {
        LoadCfg(out var width, out var depthT, out var normalT, out var fade, out var col, out var alpha);
        if (!_on) return;
        GetTree().NodeAdded += OnNode;
        Sweep(GetTree().Root);
        var mat = new ShaderMaterial { Shader = new Shader { Code = OutlineShader } };
        mat.SetShaderParameter("width", width); mat.SetShaderParameter("depth_t", depthT); mat.SetShaderParameter("normal_t", normalT);
        mat.SetShaderParameter("fade", fade); mat.SetShaderParameter("line_col", new Color(col, alpha));
        _outline = new MeshInstance3D { Name = "Outline", Mesh = new QuadMesh { Size = new Vector2(2, 2) }, MaterialOverride = mat, ExtraCullMargin = 16384f, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_outline); _inst = this; _outline.Visible = _lines;
    }
    public override void _ExitTree() { if (_on) GetTree().NodeAdded -= OnNode; if (_inst == this) _inst = null; }

    static Toon? _inst; static bool _lines = true;
    /// <summary>Settings > Display > Outlines.</summary>
    public static void SetOutlines(bool on) { _lines = on; if (_inst != null && IsInstanceValid(_inst._outline)) _inst._outline.Visible = on; }

    void Sweep(Node n) { OnNode(n); foreach (var c in n.GetChildren()) Sweep(c); }
    void OnNode(Node n)
    {
        if (n is MeshInstance3D mi && mi != _outline)
        {
            Fix(mi.MaterialOverride);
            if (mi.Mesh != null) for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++) { Fix(mi.Mesh.SurfaceGetMaterial(i)); Fix(mi.GetSurfaceOverrideMaterial(i)); }
        }
        else if (n is MultiMeshInstance3D mm) { Fix(mm.MaterialOverride); var m = mm.Multimesh?.Mesh; if (m != null) for (int i = 0; i < m.GetSurfaceCount(); i++) Fix(m.SurfaceGetMaterial(i)); }
    }

    /// <summary>Makes one material toon-lit. Unshaded materials (effects, UI in 3D) are left alone.</summary>
    void Fix(Material? m)
    {
        if (m is not BaseMaterial3D b || !_done.Add(b.GetInstanceId())) return;
        Apply(b);
        if (m.NextPass is BaseMaterial3D np) Fix(np);
    }

    /// <summary>The toon look for one material; also used by code that makes materials after its mesh is in the tree.</summary>
    public static void Apply(Material m)
    {
        if (!_on || m is not BaseMaterial3D b || b.ShadingMode == BaseMaterial3D.ShadingModeEnum.Unshaded) return;
        b.DiffuseMode = BaseMaterial3D.DiffuseModeEnum.Toon; b.SpecularMode = BaseMaterial3D.SpecularModeEnum.Toon;
        b.Roughness = Mathf.Max(b.Roughness, _soft);   // toon roughness = how soft the light/shadow edge is
        if (_rim > 0 && b.Transparency == BaseMaterial3D.TransparencyEnum.Disabled) { b.RimEnabled = true; b.Rim = _rim; b.RimTint = _rimTint; }
    }

    const string OutlineShader = @"
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, depth_test_disabled, cull_disabled, fog_disabled, shadows_disabled;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest;
uniform sampler2D normal_tex : hint_normal_roughness_texture, filter_nearest;
uniform float width = 1.2;
uniform float depth_t = 0.07;
uniform float normal_t = 0.45;
uniform float fade = 90.0;
uniform vec4 line_col : source_color = vec4(0.16, 0.11, 0.12, 0.75);
varying mat4 inv_proj;

void vertex() { POSITION = vec4(VERTEX.xy, 1.0, 1.0); inv_proj = INV_PROJECTION_MATRIX; }

float lin(vec2 uv) {
    vec4 v = inv_proj * vec4(uv * 2.0 - 1.0, texture(depth_tex, uv).r, 1.0);
    return -v.z / v.w;
}
vec3 nrm(vec2 uv) { return texture(normal_tex, uv).xyz * 2.0 - 1.0; }

void fragment() {
    vec2 px = width / VIEWPORT_SIZE; vec2 uv = SCREEN_UV;
    float d = lin(uv); vec3 n = nrm(uv);
    vec2 o[4] = vec2[](vec2(px.x, 0.0), vec2(-px.x, 0.0), vec2(0.0, px.y), vec2(0.0, -px.y));
    float de = 0.0, ne = 0.0;
    for (int i = 0; i < 4; i++) {
        float dn = lin(uv + o[i]);
        de = max(de, (dn - d) / max(dn, 0.001));         // only the near side of an edge draws, so lines sit on the object (and against the sky)
        ne = max(ne, 1.0 - dot(n, nrm(uv + o[i])));
    }
    float e = max(smoothstep(depth_t, depth_t * 1.6, de), smoothstep(normal_t, normal_t + 0.25, ne) * step(d, fade * 0.6));
    e *= 1.0 - smoothstep(fade * 0.5, fade, d);         // far scenery stays clean
    ALBEDO = line_col.rgb; ALPHA = e * line_col.a;
}
";
}
