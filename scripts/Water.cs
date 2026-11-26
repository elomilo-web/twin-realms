using Godot;

namespace TwinRealms;

/// <summary>
/// One answer to "is there water here, and where is its surface?" for swimming, floating loot, boats and the underwater camera (F-20, F-21).
/// Inside the block world's square (BlockWorld.Drawn) it reads the water blocks (a still block's top is drawn at .88 of the cell);
/// outside it, the smooth lakes, rivers and sea (Lands.WaterAt over World.GroundAt).
/// </summary>
public static class Water
{
    /// <summary>Water surface height above or around point p (null when p is not in or just above water).</summary>
    public static float? SurfaceAt(Vector3 p)
    {
        if (BlockWorld.Enabled && BlockWorld.Drawn(p.X, p.Z) && BlockWorld.At(p) != 255)
        {
            int x = Mathf.FloorToInt(p.X), z = Mathf.FloorToInt(p.Z), y0 = Mathf.FloorToInt(p.Y);
            // the top water cell of the column near p: from a little above p downwards to the first water, then up to its top
            for (int y = y0 + 1; y >= y0 - 3; y--)
            {
                if (BlockWorld.FluidAt(new Vector3(x + .5f, y + .5f, z + .5f)) != "water") continue;
                int t = y; while (t < y + 40 && BlockWorld.FluidAt(new Vector3(x + .5f, t + 1.5f, z + .5f)) == "water") t++;
                return t + .88f;
            }
            return null;
        }
        float w = Lands.WaterAt(p.X, p.Z), g = World.GroundAt(p.X, p.Z);
        if (w > g + .05f && p.Y < w + 1.2f && p.Y > g - 1f) return w;
        return null;
    }
    /// <summary>Water depth at (x, z) under a surface (0 when no water): how far down the bed is.</summary>
    public static float DepthAt(float x, float z, float surf)
    {
        if (BlockWorld.Enabled && BlockWorld.Drawn(x, z))
        {
            for (int k = 0; k < 12; k++)
            {
                var q = new Vector3(x, surf - .5f - k, z); byte b = BlockWorld.At(q);
                if (b == 255) return 0; if (BlockWorld.FluidAt(q) != "water") return k;
            }
            return 12;
        }
        return Mathf.Max(0, Lands.WaterAt(x, z) - World.GroundAt(x, z));
    }
    /// <summary>Open water a boat can sit on at (x, z) next to a surface at about surf (deep enough, nothing solid just above it).</summary>
    public static bool Floatable(float x, float z, float surf)
    {
        var s = SurfaceAt(new Vector3(x, surf, z)); if (s == null || Mathf.Abs(s.Value - surf) > .6f) return false;
        if (BlockWorld.Enabled && BlockWorld.Drawn(x, z)) { byte up = BlockWorld.At(new Vector3(x, s.Value + .3f, z)); if (up != 0 && up != 255 && BlockWorld.FluidAt(new Vector3(x, s.Value + .3f, z)) == "") return false; }
        return DepthAt(x, z, s.Value) >= .7f;
    }
    /// <summary>Point p is under the water surface.</summary>
    public static bool Under(Vector3 p) => SurfaceAt(p) is { } s && p.Y < s;
    /// <summary>The slow surface current at (x, z): a drift of about .3 m/s whose direction wanders over the map.</summary>
    public static Vector3 Current(float x, float z)
    {
        float a = Mathf.Sin(x * .013f + 1.7f) * 1.6f + Mathf.Cos(z * .011f) * 1.3f + .6f;
        return new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * .3f;
    }
}

/// <summary>
/// The underwater camera (F-20): while the active camera is below the water surface, a blue tint with a soft wobble over the screen
/// (a screen-texture shader on its own layer under the HUD) and thick blue fog (World.UpdateVisuals calls <see cref="Env"/>).
/// </summary>
public partial class Underwater : CanvasLayer
{
    public static float K;   // 0 above water .. 1 fully under (smoothed)
    public static bool CamUnder;
    static Underwater? _i;
    ColorRect _r = null!;
    public static void Ensure(Node parent) { if (_i != null && IsInstanceValid(_i)) return; _i = new Underwater(); parent.AddChild(_i); }
    public override void _Ready()
    {
        Layer = 4; ProcessMode = ProcessModeEnum.Always;
        _r = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false, Material = new ShaderMaterial { Shader = new Shader { Code = Code } } };
        _r.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); AddChild(_r);
    }
    public override void _Process(double delta)
    {
        var cam = GetViewport()?.GetCamera3D();
        CamUnder = cam != null && !Dungeon.Active && Water.Under(cam.GlobalPosition);
        K = Mathf.MoveToward(K, CamUnder ? 1f : 0f, (float)delta * 6f);
        _r.Visible = K > .01f;
        if (_r.Visible) ((ShaderMaterial)_r.Material).SetShaderParameter("k", K);
    }
    /// <summary>Fog and light under water (after the normal sky/fog update).</summary>
    static float _sky = -1, _hd;
    public static void Env(Godot.Environment e)
    {
        // sky affect and height fog are not reset every frame by the world: keep their own values and blend from those
        if (_sky < 0) { _sky = e.FogSkyAffect; _hd = e.FogHeightDensity; }
        e.FogSkyAffect = Mathf.Lerp(_sky, 1f, K); e.FogHeightDensity = Mathf.Lerp(_hd, 0f, K);
        if (K <= .01f) { _sky = -1; return; }
        e.FogDensity = Mathf.Lerp(e.FogDensity, .09f, K); e.FogLightColor = e.FogLightColor.Lerp(new Color(.06f, .22f, .3f), K);
        e.AdjustmentSaturation *= 1f - .25f * K;
    }
    const string Code = @"
shader_type canvas_item;
uniform sampler2D scr : hint_screen_texture, filter_linear;
uniform float k = 1.0;
void fragment() {
  vec2 uv = SCREEN_UV;
  float t = TIME;
  uv += vec2(sin(uv.y * 22.0 + t * 1.9), cos(uv.x * 18.0 + t * 1.5)) * 0.0045 * k;   // the slight wobble
  vec3 c = texture(scr, uv).rgb;
  float lum = dot(c, vec3(0.3, 0.55, 0.15));
  vec3 deep = vec3(0.05, 0.22, 0.30), lit = vec3(0.30, 0.62, 0.70);
  vec3 tint = mix(deep, lit, clamp(lum * 1.3, 0.0, 1.0));
  c = mix(c, tint, 0.62 * k);
  float v = length(SCREEN_UV - 0.5);
  c *= 1.0 - smoothstep(0.35, 0.85, v) * 0.45 * k;   // darker at the edges
  float caust = sin(SCREEN_UV.x * 40.0 + t * 2.0) * sin(SCREEN_UV.y * 30.0 - t * 1.6);
  c += vec3(0.03, 0.06, 0.06) * max(caust, 0.0) * k * (1.0 - SCREEN_UV.y);
  COLOR = vec4(c, 1.0);
}";
}
