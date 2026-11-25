using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>World: environment, sky, fog, sun/moon light, day/night and realm-blend look, water.</summary>
public partial class World
{
    public WorldEnvironment Env = null!;
    public DirectionalLight3D Sun = null!;
    ShaderMaterial _sky = null!, _water = null!;
    Godot.Environment _env = null!;
    // data/look.json
    static readonly Dictionary<string, float> LookF = new(); static readonly Dictionary<string, Color> LookC = new();
    static float LF(string k, float d) => LookF.TryGetValue(k, out var v) ? v : d;
    static Color LC(string k, string d) => LookC.TryGetValue(k, out var v) ? v : Color.FromHtml(d);
    float _cloudT;
    static void LoadLook()
    {
        if (LookF.Count > 0) return;
        using var doc = JsonUtil.Load("res://data/look.json");
        foreach (var sec in new[] { "env", "sky", "water" })
            if (doc.RootElement.TryGetProperty(sec, out var o))
                foreach (var p in o.EnumerateObject())
                {
                    if (p.Name.StartsWith("_")) continue;   // _help notes
                    if (p.Value.ValueKind == JsonValueKind.Number) LookF[p.Name] = p.Value.GetSingle();
                    else if (p.Value.ValueKind == JsonValueKind.String) LookC[p.Name] = Color.FromHtml(p.Value.GetString() ?? "#ffffff");
                }
    }

    void BuildEnvironment()
    {
        LoadLook();
        _sky = new ShaderMaterial { Shader = new Shader { Code = SkyShader } }; RealmShaders.Add(_sky);
        _sky.SetShaderParameter("cover", LF("cloudCover", .48f));
        _env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky, Sky = new Sky { SkyMaterial = _sky, ProcessMode = Sky.ProcessModeEnum.Incremental, RadianceSize = Sky.RadianceSizeEnum.Size64 },
            AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.55f, 0.6f, 0.7f), AmbientLightEnergy = 0.8f,
            TonemapMode = (Godot.Environment.ToneMapper)(int)LF("tonemapMode", 3), TonemapExposure = LF("exposure", 1f), TonemapWhite = LF("white", 7f),
            FogEnabled = true, FogDensity = LF("fogDay", .0017f), FogAerialPerspective = LF("aerial", .55f), FogSkyAffect = LF("skyAffect", .45f),
            FogHeight = LF("fogHeight", 30f), FogHeightDensity = LF("fogHeightDensity", .0035f),
            GlowEnabled = true, GlowIntensity = LF("glowIntensity", .7f), GlowBloom = LF("glowBloom", .04f),
            SsaoEnabled = true, SsaoIntensity = LF("ssaoIntensity", 1.8f), SsaoRadius = LF("ssaoRadius", 1.3f),
            AdjustmentEnabled = true, AdjustmentContrast = LF("contrast", 1.12f), AdjustmentSaturation = LF("saturation", 1.2f), AdjustmentBrightness = LF("brightness", 1f),
        };
        // high-end lighting (data/look.json env): bounce light (SDFGI), screen-space indirect light, light shafts in a thin haze, sharper sky light
        ((Sky)_env.Sky).RadianceSize = Sky.RadianceSizeEnum.Size256;
        _env.SdfgiEnabled = LF("gi", 1f) > 0; _env.SdfgiUseOcclusion = true; _env.SdfgiEnergy = LF("giEnergy", .9f); _env.SdfgiBounceFeedback = .5f;
        _env.SdfgiCascades = 6; _env.SdfgiMinCellSize = .2f;
        _env.SsilEnabled = LF("ssil", 1f) > 0; _env.SsilIntensity = LF("ssil", 1f); _env.SsilRadius = 5f;
        _env.SsaoDetail = .6f; _env.SsaoHorizon = .06f; _env.SsaoLightAffect = .15f;
        _env.VolumetricFogEnabled = LF("volFog", .004f) > 0; _env.VolumetricFogDensity = LF("volFog", .004f); _env.VolumetricFogAnisotropy = .6f;
        _env.VolumetricFogLength = 160f; _env.VolumetricFogGIInject = .5f; _env.VolumetricFogSkyAffect = 0f; _env.VolumetricFogTemporalReprojectionEnabled = true;
        Env = new WorldEnvironment { Environment = _env }; AddChild(Env);
        // crisp shadows suit the toon look; soft (PCSS) shadows add sampling noise that toon banding turns into a checker pattern
        Sun = new DirectionalLight3D { ShadowEnabled = true, DirectionalShadowMaxDistance = LF("shadowDistance", 260f), LightEnergy = 1.2f, LightAngularDistance = LF("sunSoftness", 0f),
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits, DirectionalShadowBlendSplits = true, DirectionalShadowSplit1 = .04f, DirectionalShadowSplit2 = .12f, DirectionalShadowSplit3 = .35f,
            ShadowBias = .03f, ShadowNormalBias = 1.2f, LightVolumetricFogEnergy = LF("sunShafts", 1.2f) };
        AddChild(Sun);
    }

    const string SkyShader = @"
shader_type sky;
render_mode use_debanding;
uniform vec3 top_col : source_color = vec3(0.2, 0.4, 0.8);
uniform vec3 hor_col : source_color = vec3(0.7, 0.8, 0.9);
uniform vec3 ground_col : source_color = vec3(0.3, 0.3, 0.3);
uniform vec3 dusk_col : source_color = vec3(1.0, 0.5, 0.3);
uniform vec3 sun_col : source_color = vec3(1.0);
uniform float dusk = 0.0;
uniform float night = 0.0;
uniform float realm_t = 0.0;
uniform float cover = 0.48;
uniform vec2 wind = vec2(0.0);
uniform float gold = 0.0;
float h21(vec2 p){ vec3 q = fract(vec3(p.xyx) * 0.1031); q += dot(q, q.yzx + 33.33); return fract((q.x + q.y) * q.z); }
float vn(vec2 p){ vec2 i = floor(p); vec2 f = fract(p); f = f*f*(3.0-2.0*f); return mix(mix(h21(i), h21(i+vec2(1.0,0.0)), f.x), mix(h21(i+vec2(0.0,1.0)), h21(i+vec2(1.0,1.0)), f.x), f.y); }
float fbm(vec2 p){ float s = 0.0; float a = 0.5; mat2 r = mat2(vec2(0.8, 0.6), vec2(-0.6, 0.8)); for (int i = 0; i < 5; i++){ s += a * vn(p); p = r * p * 2.03 + vec2(1.7, 9.2); a *= 0.5; } return s; }
float fbm3(vec2 p){ float s = 0.0; float a = 0.5; mat2 r = mat2(vec2(0.8, 0.6), vec2(-0.6, 0.8)); for (int i = 0; i < 3; i++){ s += a * vn(p); p = r * p * 2.03 + vec2(1.7, 9.2); a *= 0.5; } return s / 0.875; }
void sky(){
  vec3 d = EYEDIR; float y = d.y;
  vec3 col = mix(hor_col, top_col, pow(clamp(y, 0.0, 1.0), 0.45));
  vec3 sd = LIGHT0_DIRECTION;
  vec2 sh = normalize(sd.xz + vec2(1e-4, 0.0));
  float az = dot(normalize(d.xz + vec2(1e-4, 0.0)), sh) * 0.5 + 0.5;
  col = mix(col, dusk_col, clamp(dusk * (0.25 + 0.75 * pow(az, 3.0)) * exp(-max(y, 0.0) * 5.0), 0.0, 1.0));
  if (y < 0.0) col = mix(col, ground_col, clamp(-y * 5.0, 0.0, 1.0));
  float sdot = max(dot(d, sd), 0.0);
  float halo = pow(sdot, mix(12.0, 80.0, night)) * mix(0.35, 0.12, night) + pow(sdot, 400.0) * mix(1.0, 0.25, night);
  col += mix(sun_col, vec3(0.6, 0.7, 1.0), night) * halo * (1.0 - realm_t * 0.2) + vec3(0.62, 0.58, 0.9) * (pow(sdot, 30.0) * 0.5 + pow(sdot, 220.0) * 0.8) * realm_t;
  float cl = 0.0;
  if (!AT_CUBEMAP_PASS) {
    if (y > 0.0 && night > 0.02) {
      vec3 g = floor(d * 520.0); float r = h21(g.xy + g.z * 17.31);
      float tw = 0.6 + 0.4 * sin(r * 600.0 + wind.x * 900.0);
      col += vec3(0.85, 0.9, 1.0) * step(0.9975, r) * night * tw * smoothstep(0.0, 0.25, y) * 1.6;
    }
    vec2 uv = d.xz / (y + 0.15) * 0.5 + wind;
    uv += (vec2(vn(uv * 0.9 + 4.0), vn(uv * 0.9 + 9.0)) - 0.5) * 0.9;   // domain warp
    float n = fbm3(uv * 1.3);
    float thr = 1.0 - cover;
    float hfade = smoothstep(0.0, 0.22, y);
    cl = smoothstep(thr - 0.06, thr + 0.2, n) * hfade;
    float n2 = fbm3(uv * 1.3 + sh * 0.14);
    float lit = clamp(0.5 + (n - n2) * 6.0 + (n - thr) * 0.8, 0.0, 1.0);
    float thin = (1.0 - smoothstep(thr, thr + 0.28, n));
    float sdc = max(dot(d, sd), 0.0);
    vec3 cbase = vec3(0.36, 0.40, 0.50);
    vec3 cday = mix(cbase, vec3(1.0, 0.98, 0.95), lit);
    cday += sun_col * pow(sdc, 5.0) * thin * 1.2 + sun_col * pow(sdc, 24.0) * thin * 1.4;   // silver lining
    cday += dusk_col * dusk * (0.3 + 0.7 * az) * 0.8;
    cday = mix(cday, cday * vec3(1.15, 0.8, 0.55) + vec3(1.0, 0.5, 0.15) * (0.15 + 0.5 * az) * lit * thin, gold * 0.8);
    vec3 cnight = vec3(0.2, 0.25, 0.36) * (0.55 + lit * 0.5);   // dusty blue night clouds
    vec3 ccol = mix(cday, cnight, night);
    vec3 cshadow = vec3(0.03, 0.025, 0.05) * (0.6 + lit) + vec3(0.5, 0.2, 0.75) * pow(thin, 2.0) * (0.25 + lit * 0.5);
    ccol = mix(ccol, cshadow, realm_t);
    ccol = mix(ccol, hor_col, (1.0 - smoothstep(0.0, 0.35, y)) * 0.7);   // fade into the haze
    col = mix(col, ccol, cl * mix(0.95, 0.97, realm_t));
    float neb = fbm(d.xz / (y + 0.35) * 0.45 - wind * 0.4);
    col += mix(vec3(0.2, 0.12, 0.3), vec3(0.08, 0.2, 0.26), neb) * neb * realm_t * smoothstep(0.0, 0.45, y) * 0.5;
    float rift = 1.0 - smoothstep(0.0, 0.01, abs(fbm(d.xz / (y + 0.5) * 0.7 + 3.0) - 0.5));
    col += vec3(0.95, 0.45, 1.0) * rift * realm_t * smoothstep(0.15, 0.5, y) * 0.9;
  }
  float disc = smoothstep(mix(0.99955, 0.9997, night), mix(0.9998, 0.99985, night), sdot);
  col += mix(sun_col * 14.0, vec3(0.85, 0.9, 1.0) * 2.5, night) * disc * (1.0 - cl) * (1.0 - realm_t * 0.5);
  COLOR = col;
}";

    void BuildWater()
    {
        // animated waves, fresnel, depth-tinted shallows and shore foam (reads the depth buffer)
        _water = new ShaderMaterial { Shader = new Shader { Code = WaterShader } }; RealmShaders.Add(_water);
        foreach (var k in new[] { "shallow", "deep", "foam", "shadowShallow", "shadowDeep" })
            _water.SetShaderParameter(k switch { "shallow" => "shallow", "deep" => "deep", "foam" => "foam", "shadowShallow" => "s_shallow", _ => "s_deep" }, LC(k, "#3080a0"));
        _water.SetShaderParameter("sky_tint", LC("horizon", "#4d5659"));
        Over.AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(30000, 30000) }, MaterialOverride = _water, Position = new Vector3(0, 0.25f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
    }

    const string WaterShader = @"
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_disabled, specular_schlick_ggx;
uniform vec3 shallow : source_color; uniform vec3 deep : source_color; uniform vec3 foam : source_color;
uniform vec3 s_shallow : source_color; uniform vec3 s_deep : source_color;
uniform float realm_t = 0.0;
uniform vec3 sky_tint : source_color = vec3(0.30, 0.34, 0.36);
uniform sampler2D depth_tex : hint_depth_texture, filter_linear_mipmap;
varying vec3 wp;
global uniform vec4 vox_rect;   // the block world's square: water blocks are drawn there instead
uniform float vox_cut = 1.0;    // 0 on the water blocks' own material
float h21(vec2 p){ return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float vn(vec2 p){ vec2 i = floor(p); vec2 f = fract(p); f = f*f*(3.0-2.0*f); return mix(mix(h21(i), h21(i+vec2(1.0,0.0)), f.x), mix(h21(i+vec2(0.0,1.0)), h21(i+vec2(1.0,1.0)), f.x), f.y); }
const mat2 R1 = mat2(vec2(0.8660, 0.5), vec2(-0.5, 0.8660)); const mat2 R2 = mat2(vec2(0.3624, 0.9320), vec2(-0.9320, 0.3624));
float waves(vec2 p, float t){
  return sin(p.x * 0.42 + t * 1.1) * 0.35 + sin(dot(p, vec2(0.23, 0.31)) - t * 0.8) * 0.4 + sin(dot(p, vec2(-0.5, 0.17)) * 1.3 + t * 1.4) * 0.18
       + vn(R1 * p * 0.9137 + vec2(t * 0.35, t * 0.2) + 17.3) * 0.5 + vn(R2 * p * 2.6483 - vec2(t * 0.6, 0.0) + 41.7) * 0.18;
}
void vertex(){ wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
void fragment(){
  float t = TIME; vec2 p = wp.xz; float e = 0.12;
  if (vox_cut > 0.5 && p.x > vox_rect.x && p.y > vox_rect.y && p.x < vox_rect.z && p.y < vox_rect.w) discard;
  float dist = length(VERTEX);
  float calm = 1.0 / (1.0 + dist / 140.0); calm *= calm * (1.0 - smoothstep(250.0, 1200.0, dist));   // far water is calm and flat: no tiling glints
  vec2 pa = p + vec2(t * 0.6, t * 0.25);
  vec2 pb = vec2(p.x * 0.8 - p.y * 0.6, p.x * 0.6 + p.y * 0.8) * 0.3713 + 5.9 + vec2(-t * 0.18, t * 0.11);   // rotated, larger, other direction
  float w0 = waves(pa, t) + vn(pb) * 0.7, wx = waves(pa + vec2(e, 0.0), t) + vn(pb + vec2(e * 0.3, 0.0)) * 0.7, wz = waves(pa + vec2(0.0, e), t) + vn(pb + vec2(0.0, e * 0.3)) * 0.7;
  vec3 nw = normalize(vec3((w0 - wx) / e * 0.35 * calm, 1.0, (w0 - wz) / e * 0.35 * calm));
  NORMAL = normalize((VIEW_MATRIX * vec4(nw, 0.0)).xyz);
  float dr = textureLod(depth_tex, SCREEN_UV, 0.0).r;
  vec4 up = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, dr, 1.0); up.xyz /= up.w;
  float thick = clamp(VERTEX.z - up.z, 0.0, 50.0);
  vec3 sh = mix(shallow, s_shallow, realm_t), dp = mix(deep, s_deep, realm_t);
  vec3 c = mix(sh, dp, smoothstep(0.0, 6.0, thick));
  float fr = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 4.0);
  float fn = vn(p * 3.0 + vec2(t * 0.5, -t * 0.3));
  float fo = (1.0 - smoothstep(0.0, 0.9 + fn * 0.6, thick)) * (0.55 + 0.45 * sin(thick * 9.0 - t * 2.5 + fn * 4.0));
  fo = clamp(fo + smoothstep(0.82, 0.95, w0 * 0.55 + fn * 0.4) * 0.35 * calm, 0.0, 1.0);
  c *= mix(1.0, 0.7, clamp(1.0 - fr * 2.0, 0.0, 1.0));   // darker looking straight down
  c = mix(c, sky_tint, clamp(fr * 1.3, 0.0, 0.85));       // reflect the horizon at grazing angles
  ALBEDO = mix(c, foam, fo);
  float macro = vn(p * 0.0061 + 3.1) * 0.6 + vn(R1 * p * 0.0173) * 0.4;
  ALBEDO *= 0.88 + 0.24 * macro;
  ROUGHNESS = mix(mix(0.08, 0.3, smoothstep(150.0, 900.0, dist)) + (macro - 0.5) * 0.08, 0.5, fo); SPECULAR = mix(0.9, 0.5, smoothstep(150.0, 900.0, dist)); METALLIC = 0.0;
  EMISSION = mix(vec3(0.0), vec3(0.25, 0.05, 0.35), realm_t * 0.4 * (1.0 - fo));
  ALPHA = clamp(mix(0.55, 0.95, smoothstep(0.0, 3.0, thick)) + fr * 0.3 + fo, 0.0, 1.0);
  if (COLOR.b < 0.5) {   // block-water side face: falling water, lighter, streaks scrolling down
    float st = vn(vec2((wp.x + wp.z) * 5.0, wp.y * 2.5 + t * 3.0)) * 0.6 + vn(vec2((wp.x - wp.z) * 11.0, wp.y * 5.0 + t * 5.0)) * 0.4;
    ALBEDO = mix(ALBEDO, foam, 0.25 + 0.5 * st);
    ALPHA = 0.3 + 0.35 * st;
    ROUGHNESS = 0.3;
  }
}";

    /// <summary>
    /// Settings > Display: graphics quality preset and view distance. Low: no bounce light, haze or SSAO, short soft shadows, FXAA;
    /// Medium: SSAO, 4K shadows, 2x MSAA; High: bounce light, haze, TAA; Ultra: also screen-space indirect light, 8K shadows to 260 m, 4x MSAA.
    /// </summary>
    public void ApplyQuality()
    {
        var s = Game.Set; int q = s.Quality; float dist = s.ViewDist;
        _env.SdfgiEnabled = q >= 2 && LF("gi", 1f) > 0; _env.SsilEnabled = q >= 3 && LF("ssil", 1f) > 0; _env.SsaoEnabled = q >= 1;
        _env.VolumetricFogEnabled = q >= 2 && LF("volFog", .004f) > 0; _env.GlowEnabled = true;
        Sun.DirectionalShadowMaxDistance = Mathf.Min(dist, q switch { 0 => 80f, 1 => 140f, 2 => 200f, _ => LF("shadowDistance", 260f) });
        Sun.DirectionalShadowMode = q == 0 ? DirectionalLight3D.ShadowMode.Parallel2Splits : DirectionalLight3D.ShadowMode.Parallel4Splits;
        RenderingServer.DirectionalShadowAtlasSetSize(q switch { 0 => 2048, 1 => 4096, _ => 8192 }, true);
        RenderingServer.DirectionalSoftShadowFilterSetQuality(q switch { 0 => RenderingServer.ShadowQuality.Hard, 1 => RenderingServer.ShadowQuality.SoftLow, 2 => RenderingServer.ShadowQuality.SoftMedium, _ => RenderingServer.ShadowQuality.SoftHigh });
        var vp = GetViewport();
        vp.Msaa3D = q switch { 0 => Viewport.Msaa.Disabled, 1 or 2 => Viewport.Msaa.Msaa2X, _ => Viewport.Msaa.Msaa4X };
        vp.UseTaa = q >= 2; vp.ScreenSpaceAA = q <= 1 ? Viewport.ScreenSpaceAAEnum.Fxaa : Viewport.ScreenSpaceAAEnum.Disabled;
        vp.MeshLodThreshold = q switch { 0 => 4f, 1 => 2f, 2 => 1f, _ => .5f };
        // view distance: the camera far plane, and tree/rock patches fade out before it (scaled from their own range)
        foreach (var c in GetTree().Root.FindChildren("*", "Camera3D", true, false)) if (c is Camera3D cam) cam.Far = Mathf.Max(dist + 200f, 600f);
        float k = dist / 1200f;
        for (int i = Scenery.Count - 1; i >= 0; i--)
        {
            var (n, end) = Scenery[i]; if (!IsInstanceValid(n)) { Scenery.RemoveAt(i); continue; }
            n.VisibilityRangeEnd = Mathf.Min(end * Mathf.Clamp(k, .3f, 1.6f), dist);
        }
    }

    float _lastRealmT = -1f, _lastNight = -1f;
    void UpdateVisuals(float dt, bool force)
    {
        if (_suspend) return;
        // sun angle: noon at 0.5, midnight 0.0/1.0
        float ang = (TimeOfDay - 0.25f) * Mathf.Tau; float elev = Mathf.Sin(ang);
        // day fades out over a long twilight (sun from +0.25 to -0.12 elevation); dusk peaks as the sun touches the horizon
        float day = Smooth(-0.12f, 0.25f, elev), dusk = 1f - Smooth(0f, 0.3f, Mathf.Abs(elev - 0.02f));
        Night = (1 - day) * (1 - RealmT) + RealmT * 0.7f;
        CharRim.Set(Night, RealmT);
        Genshin.SetNight(Night);   // characters switch to their cooler night shadow colours
        // golden hour: bell over low-sun elevations (dawn/dusk bands only; zero at midday, night and in the shadow realm)
        float gold = (1f - Smooth(0f, .42f, Mathf.Abs(elev - .1f))) * (1f - RealmT);
        float sunS = Mathf.Clamp(Mathf.Abs(elev) * 0.9f + 0.1f, 0.06f, 1f) * (1f - gold * LF("goldLowSun", .3f));
        Sun.RotationDegrees = new Vector3(-Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(sunS, 0.05f, 1f))), Mathf.RadToDeg(ang) + 90f, 0);
        // warm sunlight, more golden while the sun is low (morning and evening), orange at dusk (data/look.json sunNoon / sunLow / sunDusk)
        var dayCol = LC("sunNoon", "fff1dc").Lerp(LC("sunLow", "ffc98a"), 1f - Smooth(.12f, .6f, elev)).Lerp(LC("sunDusk", "ff8c4a"), dusk).Lerp(LC("goldSun", "ff9a40"), gold * day * .85f); var moon = LC("moonColor", "9fb2dc"); var shadowCol = LC("sunShadow", "b8b0ec");
        Sun.LightColor = dayCol.Lerp(moon, Smooth(.35f, 0f, day)).Lerp(shadowCol, RealmT);
        Sun.LightEnergy = Mathf.Lerp(LF("sunNight", .22f), LF("sunDay", 1.55f), day) * Mathf.Lerp(1f, LF("sunShadowMul", 1.05f), RealmT) * (1f - dusk * .25f * day) * Mathf.Lerp(1f, LF("goldSunMul", 1.45f), gold * day);
        var top = LC("nightTop", "02040d").Lerp(LC("dayTop", "2a64b8"), day).Lerp(LC("duskTop", "2d2f6e"), dusk * .55f).Lerp(LC("goldTop", "1e3a82"), gold * day * .6f).Lerp(LC("shadowTop", "0d0318"), RealmT);
        var hor = LC("nightHorizon", "0b1430").Lerp(LC("dayHorizon", "b5d3ec"), day).Lerp(LC("duskHorizon", "ff8048"), dusk * .5f).Lerp(LC("goldHorizon", "ff9a5c"), gold * day * .6f).Lerp(LC("shadowHorizon", "4a1450"), RealmT);
        float skyNight = Mathf.Clamp(1 - day, 0, 1) * (1 - RealmT);
        _sky.SetShaderParameter("top_col", top); _sky.SetShaderParameter("hor_col", hor); _sky.SetShaderParameter("ground_col", hor.Darkened(.55f));
        _sky.SetShaderParameter("dusk_col", LC("duskGlow", "ff6a3a")); _sky.SetShaderParameter("dusk", dusk * (1 - RealmT)); _sky.SetShaderParameter("night", skyNight);
        _sky.SetShaderParameter("sun_col", dayCol); _sky.SetShaderParameter("gold", gold * day);
        _cloudT += dt * LF("cloudSpeed", .006f); _sky.SetShaderParameter("wind", new Vector2(_cloudT, _cloudT * .35f));
        _env.FogLightColor = hor.Lerp(top, .25f).Lerp(new Color(.52f, .6f, .72f), .4f * day * (1 - RealmT)).Lerp(LC("goldFog", "f0a070"), gold * day * .55f).Lerp(LC("fogShadowColor", "5f5c7e"), RealmT);
        _env.FogSunScatter = LF("goldFogScatter", .55f) * gold * day;
        _env.GlowIntensity = LF("glowIntensity", .7f) + LF("goldGlow", .35f) * gold * day; _env.GlowHdrThreshold = Mathf.Lerp(1f, LF("goldHdrThreshold", .9f), gold * day);
        _env.FogDensity = Mathf.Lerp(LF("fogDay", .0017f), LF("fogShadow", .0045f), RealmT);
        _env.AmbientLightColor = LC("ambientDayColor", "9eafc8").Lerp(LC("ambientNightColor", "38426f"), 1 - day).Lerp(new Color(.9f, .55f, .5f), dusk * .45f * (1f - gold * .7f)).Lerp(LC("goldAmbient", "7f93c4"), gold * day * .6f).Lerp(LC("ambientShadowColor", "8088b0"), RealmT);
        _env.AmbientLightEnergy = Mathf.Lerp(LF("ambientNight", .45f), LF("ambientDay", .62f), day) * Mathf.Lerp(1f, Mathf.Max(LF("ambientShadow", 1.25f), 0.6f * LF("ambientDay", .62f) / Mathf.Max(.01f, Mathf.Lerp(LF("ambientNight", .45f), LF("ambientDay", .62f), day))), RealmT);
        _env.AdjustmentSaturation = Mathf.Lerp(LF("saturation", 1.2f), LF("saturationShadow", .9f), RealmT);
        { float wr = Weather.Rain, wc = Weather.Cloud, wg = Mathf.Max(wr, wc * .5f);   // weather mood (Weather.cs): heavier cloud, dimmer cooler light, thicker fog
          _sky.SetShaderParameter("cover", Mathf.Lerp(LF("cloudCover", .48f), .95f, wc)); Sun.LightEnergy *= 1f - .25f * wg; Sun.LightColor = Sun.LightColor.Lerp(new Color(.62f, .68f, .76f), .3f * wg);
          _env.AmbientLightEnergy *= 1f - .2f * wg; _env.AmbientLightColor = _env.AmbientLightColor.Lerp(new Color(.5f, .55f, .62f), .3f * wg); _env.FogDensity *= 1f + .8f * wg;
          _env.FogLightColor = _env.FogLightColor.Lerp(new Color(.4f, .46f, .54f), .45f * wg); _env.AdjustmentSaturation *= 1f - .15f * wg;
          // lightning before thunder (Weather.Flash 0..1, decays): a brief cold-white wash over the ambient light
          if (Weather.Flash > .01f) { _env.AmbientLightEnergy += 1.6f * Weather.Flash; _env.AmbientLightColor = _env.AmbientLightColor.Lerp(new Color(.85f, .9f, 1f), Weather.Flash); } }
        { // god rays: forward-scattering volumetric haze + stronger sun volumetric energy in the gold band (quality >= 2, fades with rain)
          float baseVol = LF("volFog", .004f);
          float gr = (Game.Set.Quality >= 2 && baseVol > 0 && LF("godrays", 1f) > 0) ? gold * day * (TimeOfDay < .5f ? LF("godraysMorning", .6f) : 1f) * (1f - Mathf.Clamp(Weather.Rain, 0f, 1f) * .8f) : 0f;
          _env.VolumetricFogDensity = baseVol + LF("godraysDensity", .004f) * gr;
          _env.VolumetricFogAnisotropy = Mathf.Lerp(.6f, LF("godraysAniso", .7f), gr);
          _env.VolumetricFogLength = Mathf.Lerp(160f, LF("godraysLength", 120f), gr);
          Sun.LightVolumetricFogEnergy = LF("sunShafts", 1.2f) + LF("godraysEnergy", 2f) * gr; }
        if (force || Mathf.Abs(RealmT - _lastRealmT) > 0.001f)
        {
            _lastRealmT = RealmT; _terrMat.SetShaderParameter("realm_t", RealmT);
            foreach (var fm in Flora.Materials) fm.SetShaderParameter("realm_t", RealmT);
            foreach (var sm in RealmShaders) sm.SetShaderParameter("realm_t", RealmT);
            foreach (var (m, l, d) in Mats) m.AlbedoColor = l.Lerp(d, RealmT);
        }
        if (force || Mathf.Abs(Night - _lastNight) > 0.002f)
        {
            _lastNight = Night;
            WindowMat.EmissionEnergyMultiplier = Night * 3.4f; WindowMat.Emission = new Color(1, .75f, .4f).Lerp(new Color(.7f, .45f, 1f), RealmT);
            LampMat.EmissionEnergyMultiplier = .15f + Night * 3f; LampMat.Emission = WindowMat.Emission;
        }
        foreach (var (lt, e, realm) in Lamps)
        {
            bool realmOk = realm < 0 || (realm == 1 && RealmT > .5f) || (realm == 0 && RealmT < .5f);
            float en = !realmOk ? 0f : e >= 0 ? e * Mathf.Clamp(Night * 1.6f, 0, 1) : -e;
            bool vis = en > 0.01f;
            if (lt.Visible != vis) lt.Visible = vis;   // hidden lights drop out of the light clusters entirely
            if (vis && Mathf.Abs(lt.LightEnergy - en) > 0.005f) lt.LightEnergy = en;
        }
    }

    public void SetDungeonLook(bool on, Color? ambient = null)
    {
        CharRim.Set(1f, 0f, on);
        if (on)
        {
            if (!_dungSaved)
            {
                _dungSaved = true;
                _sv = (_env.VolumetricFogEnabled, _env.VolumetricFogDensity, _env.VolumetricFogAlbedo, _env.VolumetricFogEmission, _env.VolumetricFogLength, _env.VolumetricFogDetailSpread, _env.VolumetricFogGIInject, _env.FogHeight, _env.FogHeightDensity);
            }
            _env.BackgroundMode = Godot.Environment.BGMode.Color; _env.BackgroundColor = new Color(.02f, .015f, .03f); _env.AmbientLightSource = Godot.Environment.AmbientSource.Color; _env.AmbientLightColor = LC("dungeonAmbientColor", "8fa0d0").Lerp((ambient ?? new Color(.5f, .5f, .6f)).Lightened(.5f), .2f); _env.AmbientLightEnergy = LF("dungeonAmbient", 3.2f);
            _env.FogDensity = LF("dungeonFogDensity", .004f); _env.FogLightColor = LC("dungeonFogColor", "3a3a52"); _suspend = true;
            if (!_ssaoSaved) { _ssaoSaved = true; _ssaoSv = _env.SsaoIntensity; }
            _env.SsaoIntensity = LF("dungeonSsao", .5f);
            if (Game.Set.Quality >= 2)   // volumetric fog is costly: only on medium+ quality
            {
                _env.VolumetricFogEnabled = true; _env.VolumetricFogDensity = LF("dungeonVolDensity", .008f); _env.VolumetricFogAlbedo = new Color(.75f, .75f, .85f); _env.VolumetricFogEmission = new Color(0, 0, 0);
                _env.VolumetricFogLength = 40f; _env.VolumetricFogDetailSpread = .8f; _env.VolumetricFogGIInject = 0f;
            }
            _env.FogHeight = 1.2f; _env.FogHeightDensity = .08f;   // ground-hugging mist
        }
        else
        {
            _env.BackgroundMode = Godot.Environment.BGMode.Sky; _suspend = false;
            if (_ssaoSaved) { _ssaoSaved = false; _env.SsaoIntensity = _ssaoSv; }
            if (_dungSaved)
            {
                _dungSaved = false;
                _env.VolumetricFogEnabled = _sv.en; _env.VolumetricFogDensity = _sv.dens; _env.VolumetricFogAlbedo = _sv.alb; _env.VolumetricFogEmission = _sv.emi; _env.VolumetricFogLength = _sv.len;
                _env.VolumetricFogDetailSpread = _sv.det; _env.VolumetricFogGIInject = _sv.gi; _env.FogHeight = _sv.fh; _env.FogHeightDensity = _sv.fhd;
            }
        }
    }
    bool _dungSaved, _ssaoSaved; float _ssaoSv;
    (bool en, float dens, Color alb, Color emi, float len, float det, float gi, float fh, float fhd) _sv;
    bool _suspend;
}
