using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>
/// Genshin-Impact-style look for characters: turns a StandardMaterial3D into a ShaderMaterial that uses
/// assets/shaders/genshin_toon.gdshader (ramp-coloured shadows, hard highlight, metal bands, rim light)
/// plus assets/shaders/genshin_outline.gdshader as a second pass (the dark line round the character).
/// Used by Avatar.Tint. Everything else in the world keeps the simpler Toon.cs look.
/// Settings: data/look.json "genshin".
/// </summary>
public static class Genshin
{
    public enum Kind { Cloth, Skin, Hair }

    const string ToonPath = "res://assets/shaders/genshin_toon.gdshader", OutlinePath = "res://assets/shaders/genshin_outline.gdshader";
    static bool _loaded, _on = true, _hull = true;
    static float _rim = .35f, _rimWidth = .28f, _threshold = .5f, _feather = .015f, _ambient = .55f, _shadows = 1f, _lineWidth = 1.8f, _lineMax = .012f, _lineFade = 60f, _hairSpec = .35f, _metalSpec = .8f;
    static Color _line = new(.32f, .2f, .22f);
    static Shader? _toon, _toonTwoSided, _outline;
    static readonly Dictionary<Kind, (Texture2D day, Texture2D night)> _ramps = new();
    static Texture2D? _metalRamp;
    static readonly List<WeakReference<ShaderMaterial>> _all = new();
    static float _night = -1f;

    static void Load()
    {
        if (_loaded) return; _loaded = true;
        using var doc = JsonUtil.Load("res://data/look.json");
        if (doc.RootElement.TryGetProperty("genshin", out var g))
        {
            _on = !g.TryGetProperty("enabled", out var en) || en.ValueKind != JsonValueKind.False;
            _hull = !g.TryGetProperty("outline", out var ol) || ol.ValueKind != JsonValueKind.False;
            _rim = JsonUtil.F(g, "rim", _rim); _rimWidth = JsonUtil.F(g, "rimWidth", _rimWidth);
            _threshold = JsonUtil.F(g, "shadowThreshold", _threshold); _feather = JsonUtil.F(g, "shadowFeather", _feather);
            _ambient = JsonUtil.F(g, "ambient", _ambient); _shadows = JsonUtil.F(g, "receiveShadows", _shadows);
            _lineWidth = JsonUtil.F(g, "outlineWidth", _lineWidth); _lineMax = JsonUtil.F(g, "outlineMaxWidth", _lineMax); _lineFade = JsonUtil.F(g, "outlineFade", _lineFade);
            _hairSpec = JsonUtil.F(g, "hairHighlight", _hairSpec); _metalSpec = JsonUtil.F(g, "metalHighlight", _metalSpec);
            string lc = JsonUtil.S(g, "outlineColor"); if (lc != "") _line = Color.FromHtml(lc);
        }
        if (!ResourceLoader.Exists(ToonPath) || !ResourceLoader.Exists(OutlinePath)) { GD.PrintErr("[GENSHIN] shaders missing, using the plain toon look"); _on = false; return; }
        _toon = GD.Load<Shader>(ToonPath); _outline = GD.Load<Shader>(OutlinePath);
        _toonTwoSided = new Shader { Code = _toon.Code.Replace("render_mode cull_back", "render_mode cull_disabled") };

        // Ramps, read left (deep shadow) to right (full light): deep shadow, shadow, the saturated band at the shadow edge, light.
        // Genshin tints shadows instead of greying them: warm red-brown on skin, cool violet on cloth and hair; night ramps are bluer and darker.
        _ramps[Kind.Skin] = (Ramp(new Color(.80f, .56f, .56f), new Color(.90f, .68f, .65f), new Color(.98f, .66f, .58f)),
                             Ramp(new Color(.58f, .52f, .72f), new Color(.70f, .62f, .80f), new Color(.80f, .64f, .78f)));
        _ramps[Kind.Cloth] = (Ramp(new Color(.60f, .56f, .74f), new Color(.72f, .68f, .84f), new Color(.80f, .70f, .84f)),
                              Ramp(new Color(.46f, .48f, .72f), new Color(.58f, .60f, .82f), new Color(.66f, .64f, .84f)));
        _ramps[Kind.Hair] = (Ramp(new Color(.62f, .54f, .68f), new Color(.74f, .66f, .78f), new Color(.86f, .70f, .74f)),
                             Ramp(new Color(.48f, .48f, .70f), new Color(.60f, .60f, .80f), new Color(.68f, .64f, .82f)));
        // Metal: dark and bright stripes, looked up by which way the surface faces the camera.
        var mg = new Gradient { Offsets = new[] { 0f, .22f, .30f, .48f, .56f, .78f, .86f, 1f }, Colors = new[] { G(.25f), G(.35f), G(.75f), G(.55f), G(1f), G(.6f), G(.9f), G(.45f) } };
        _metalRamp = new GradientTexture1D { Gradient = mg, Width = 256 };
    }

    static Color G(float v) => new(v, v, v);
    static Texture2D Ramp(Color deep, Color shade, Color edge)
    {
        var g = new Gradient { Offsets = new[] { 0f, .6f, .85f, .97f }, Colors = new[] { deep, shade, edge, Colors.White } };
        return new GradientTexture1D { Gradient = g, Width = 256 };
    }

    /// <summary>True for a material made by From.</summary>
    public static bool Is(Material? m) => m is ShaderMaterial s && s.Shader != null && (s.Shader == _toon || s.Shader == _toonTwoSided);

    /// <summary>The Genshin version of a standard material. Glass-like and unshaded materials come back unchanged.</summary>
    public static Material From(BaseMaterial3D b, Kind kind = Kind.Cloth)
    {
        Load();
        if (!_on || b.ShadingMode == BaseMaterial3D.ShadingModeEnum.Unshaded) return b;
        bool cut = b.Transparency == BaseMaterial3D.TransparencyEnum.AlphaScissor;
        if (b.Transparency != BaseMaterial3D.TransparencyEnum.Disabled && !cut) return b;

        float growBy = b.Grow ? b.GrowAmount : 0f, alphaCut = cut ? b.AlphaScissorThreshold : 0f;
        var m = new ShaderMaterial { Shader = b.CullMode == BaseMaterial3D.CullModeEnum.Disabled ? _toonTwoSided : _toon, ResourceName = b.ResourceName };
        m.SetShaderParameter("albedo", b.AlbedoColor);
        if (b.AlbedoTexture != null) m.SetShaderParameter("albedo_tex", b.AlbedoTexture);
        m.SetShaderParameter("use_vertex_color", b.VertexColorUseAsAlbedo);
        m.SetShaderParameter("alpha_cut", alphaCut); m.SetShaderParameter("grow", growBy);
        if (b.EmissionEnabled) { m.SetShaderParameter("emission", b.Emission); m.SetShaderParameter("emission_energy", b.EmissionEnergyMultiplier); }
        var (day, nightRamp) = _ramps[kind];
        m.SetShaderParameter("ramp_day", day); m.SetShaderParameter("ramp_night", nightRamp); m.SetShaderParameter("night", Mathf.Max(_night, 0f));
        m.SetShaderParameter("shadow_threshold", _threshold); m.SetShaderParameter("shadow_feather", _feather);
        m.SetShaderParameter("ambient_strength", _ambient); m.SetShaderParameter("receive_shadows", _shadows);
        m.SetShaderParameter("metal", b.Metallic); m.SetShaderParameter("metal_ramp", _metalRamp!);
        m.SetShaderParameter("spec_strength", b.Metallic > .3f ? _metalSpec : kind == Kind.Hair ? _hairSpec : 0f);
        m.SetShaderParameter("shininess", kind == Kind.Hair ? 24f : 64f);
        m.SetShaderParameter("rim_strength", _rim); m.SetShaderParameter("rim_width", _rimWidth);

        Material? next = b.NextPass;
        if (_hull)
        {
            var o = new ShaderMaterial { Shader = _outline, NextPass = next };
            o.SetShaderParameter("albedo", b.AlbedoColor);
            if (b.AlbedoTexture != null) o.SetShaderParameter("albedo_tex", b.AlbedoTexture);
            o.SetShaderParameter("alpha_cut", alphaCut); o.SetShaderParameter("grow", growBy);
            o.SetShaderParameter("line_color", _line); o.SetShaderParameter("width", _lineWidth);
            o.SetShaderParameter("max_width", _lineMax); o.SetShaderParameter("fade_distance", _lineFade);
            next = o;
        }
        m.NextPass = next;
        _all.Add(new WeakReference<ShaderMaterial>(m));
        return m;
    }

    /// <summary>A copy of a Genshin material with one more pass drawn on top (the gear edge glow), and optionally a glow colour.</summary>
    public static ShaderMaterial WithPass(ShaderMaterial src, Material pass, Color? emit = null, float energy = 0f)
    {
        var d = (ShaderMaterial)src.Duplicate();
        if (emit != null) { d.SetShaderParameter("emission", emit.Value); d.SetShaderParameter("emission_energy", energy); }
        if (d.NextPass is ShaderMaterial o && o.Shader == _outline) { var o2 = (ShaderMaterial)o.Duplicate(); o2.NextPass = pass; d.NextPass = o2; }
        else d.NextPass = pass;
        _all.Add(new WeakReference<ShaderMaterial>(d));
        return d;
    }

    /// <summary>0 = day ramps, 1 = night ramps. Called by World every frame; only does work when the value has moved.</summary>
    public static void SetNight(float v)
    {
        v = Mathf.Clamp(v, 0f, 1f);
        if (Mathf.Abs(v - _night) < .02f) return;
        _night = v;
        for (int i = _all.Count - 1; i >= 0; i--)
        {
            if (_all[i].TryGetTarget(out var m) && GodotObject.IsInstanceValid(m)) m.SetShaderParameter("night", v);
            else _all.RemoveAt(i);
        }
    }
}
