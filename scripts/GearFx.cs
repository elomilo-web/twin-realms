using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>
/// Elemental and rarity glow on worn gear and held weapons, so one model can read as many different items.
/// Colour: the item's element (fire, frost, storm, venom, holy, shadow) or, without one, its rarity colour.
/// Strength grows with rarity: a fresnel edge glow (breathing on higher tiers), a little emission, and on epic/legendary
/// weapons a small light and drifting sparks. All numbers in data/gear_fx.json.
/// </summary>
public static class GearFx
{
    public class Elem { public string Id = "", Word = "", Status = ""; public Color Col; public float Proc; }
    struct Tier { public float Edge, Emit, Pulse, Light; public int Sparks; }

    static Tier[] _tiers = new Tier[5]; static float[] _chance = { 0, .12f, .35f, .65f, 1f }; static float _armorScale = .45f;
    static readonly Dictionary<string, Elem> _el = new(); static readonly List<string> _order = new();
    static readonly Dictionary<string, ShaderMaterial> _edge = new();

    static void Load()
    {
        if (_el.Count > 0) return;
        using var doc = JsonUtil.Load("res://data/gear_fx.json"); var r = doc.RootElement;
        if (r.TryGetProperty("rarity", out var ra)) { int i = 0; foreach (var t in ra.EnumerateArray()) if (i < 5) _tiers[i++] = new Tier { Edge = JsonUtil.F(t, "edge"), Emit = JsonUtil.F(t, "emit"), Pulse = JsonUtil.F(t, "pulse"), Light = JsonUtil.F(t, "light"), Sparks = (int)JsonUtil.F(t, "sparks") }; }
        if (r.TryGetProperty("elementChance", out var ec)) { int i = 0; foreach (var x in ec.EnumerateArray()) if (i < 5) _chance[i++] = x.GetSingle(); }
        _armorScale = JsonUtil.F(r, "armorScale", .45f);
        foreach (var p in r.GetProperty("elements").EnumerateObject())
        {
            var e = p.Value; _el[p.Name] = new Elem { Id = p.Name, Word = JsonUtil.S(e, "word", p.Name), Status = JsonUtil.S(e, "status"), Col = Color.FromHtml(JsonUtil.S(e, "color", "ffffff")), Proc = JsonUtil.F(e, "proc", .1f) };
            _order.Add(p.Name);
        }
    }

    public static Elem? Get(string? id) { Load(); return id != null && _el.TryGetValue(id, out var e) ? e : null; }
    /// <summary>Rolls an element for a new item of this rarity (or none).</summary>
    public static string? Roll(int rar) { Load(); return GD.Randf() < _chance[Mathf.Clamp(rar, 0, 4)] ? _order[(int)(GD.Randi() % (uint)_order.Count)] : null; }
    public static Color ColorOf(Item it) => Get(it.Elem)?.Col ?? Items.RarityColor(it.Rar);

    /// <summary>Applies the glow to every mesh under root. weapon = full strength plus light and sparks (length = blade length along +Z, from 'from').</summary>
    public static void Apply(Node3D root, Item? it, bool weapon, float from = 0f, float length = 0f)
    {
        if (it == null) return; Load();
        var t = _tiers[Mathf.Clamp(it.Rar, 0, 4)]; var col = ColorOf(it); bool elem = it.Elem != null;
        float edge = t.Edge * (weapon ? 1f : _armorScale); if (elem) edge = Mathf.Max(edge, weapon ? .5f : .25f);
        if (edge <= 0.001f && t.Emit <= 0) return;
        var edgeMat = EdgeMat(col, edge, t.Pulse);
        foreach (var mi in Meshes(root))
        {
            for (int s = 0; s < (mi.Mesh?.GetSurfaceCount() ?? 0); s++)
            {
                if (mi.GetActiveMaterial(s) is not BaseMaterial3D b) continue;
                var d = (BaseMaterial3D)b.Duplicate(); Toon.Apply(d);
                if (t.Emit > 0 && weapon) { d.EmissionEnabled = true; d.Emission = col; d.EmissionEnergyMultiplier = t.Emit; }   // armor only gets the edge glow, so it keeps its colours
                d.NextPass = edgeMat;
                if (mi.MaterialOverride != null) { mi.MaterialOverride = d; break; } else mi.SetSurfaceOverrideMaterial(s, d);
            }
        }
        if (!weapon || length <= 0) return;
        var mid = new Vector3(0, 0, from + length * .6f);
        if (t.Light > 0) root.AddChild(new OmniLight3D { LightColor = col, LightEnergy = t.Light, OmniRange = 1.8f, ShadowEnabled = false, Position = mid, LightSpecular = 0 });
        if (t.Sparks > 0) root.AddChild(Sparks(col, t.Sparks, from, length));
    }

    static IEnumerable<MeshInstance3D> Meshes(Node n) { if (n is MeshInstance3D m) yield return m; foreach (var c in n.GetChildren()) foreach (var x in Meshes(c)) yield return x; }

    static ShaderMaterial EdgeMat(Color c, float strength, float pulse)
    {
        string key = c.ToHtml() + strength.ToString("0.00") + pulse.ToString("0.00");
        if (_edge.TryGetValue(key, out var m)) return m;
        m = new ShaderMaterial { Shader = EdgeShader, RenderPriority = 1 };
        m.SetShaderParameter("col", c); m.SetShaderParameter("strength", strength); m.SetShaderParameter("pulse", pulse);
        _edge[key] = m; return m;
    }
    static Shader? _edgeShader;
    static Shader EdgeShader => _edgeShader ??= new Shader { Code = @"
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_back, shadows_disabled, fog_disabled;
uniform vec4 col : source_color;
uniform float strength = 1.0;
uniform float pulse = 0.0;
void fragment() {
    float f = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 2.2);
    float p = 1.0 + pulse * sin(TIME * 2.4);
    ALBEDO = col.rgb * f * strength * p;
}" };

    static GpuParticles3D Sparks(Color c, int rate, float from, float length)
    {
        var pm = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(.03f, .03f, length * .45f),
            Direction = Vector3.Up, Spread = 40f, InitialVelocityMin = .05f, InitialVelocityMax = .25f, Gravity = new Vector3(0, .35f, 0),
            ScaleMin = .5f, ScaleMax = 1f, Color = c,
        };
        var fade = new Gradient(); fade.SetColor(0, new Color(1, 1, 1, 0)); fade.SetColor(1, new Color(1, 1, 1, 0)); fade.AddPoint(.25f, Colors.White);
        pm.ColorRamp = new GradientTexture1D { Gradient = fade };
        var mat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BlendMode = BaseMaterial3D.BlendModeEnum.Add, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, VertexColorUseAsAlbedo = true, AlbedoColor = new Color(1, 1, 1, 1) };
        return new GpuParticles3D
        {
            Amount = Mathf.Max(2, rate * 2), Lifetime = 1.4, ProcessMaterial = pm, LocalCoords = false, Position = new Vector3(0, 0, from + length * .5f),
            DrawPass1 = new QuadMesh { Size = new Vector2(.035f, .035f), Material = mat }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityAabb = new Aabb(new Vector3(-1, -1, -length), new Vector3(2, 3, length * 2)),
        };
    }
}
