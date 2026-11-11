using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace TwinRealms;

public class Poi { public string Id = "", Name = "", Icon = ""; public float X, Z, R; public int Lvl; public string? Dun; public Vector3 Door; public float DoorYaw; public bool Sky; public float Y; }
public class Zone { public string Id = ""; public float X, Z, R, B, H; }

/// <summary>The overworld: heightfield terrain, sky and day/night, the Light/Shadow realm shift, vegetation and shared materials.</summary>
public partial class World : Node3D
{
    public const int N = 512; public const float Half = 256f, Cell = 1f;   // 1 m terrain grid (was 2 m: big flat facets)
    public static World? I;
    public static int Realm;                 // target realm (0 light, 1 shadow)
    public static float RealmT;              // 0..1 visual blend
    public static bool Shifting => Mathf.Abs(RealmT - Realm) > 0.001f;
    public static float TimeOfDay = 0.33f, Clock, Night, ShiftCd;
    public static float[] HG = new float[(N + 1) * (N + 1)];
    public static List<Poi> Pois = new();
    public static List<Zone> Zones = new();
    public static List<Vector2[]> Roads = new();
    static List<(float x, float z, float r, float h)> _mts = new();
    public static Poi? GetPoi(string id) => Pois.Find(p => p.Id == id);

    public Node3D Over = null!;                // everything belonging to the overworld
    public WorldEnvironment Env = null!;
    public DirectionalLight3D Sun = null!;
    ShaderMaterial _sky = null!, _water = null!;
    Godot.Environment _env = null!;
    ShaderMaterial _terrMat = null!;
    // data/look.json
    static readonly Dictionary<string, float> LookF = new(); static readonly Dictionary<string, Color> LookC = new();
    static float LF(string k, float d) => LookF.TryGetValue(k, out var v) ? v : d;
    static Color LC(string k, string d) => LookC.TryGetValue(k, out var v) ? v : Color.FromHtml(d);
    float _cloudT;
    Image? _mapImg;
    public static ImageTexture? MapTex;
    public const int MapSize = 2048;
    public static float MapExtent => Lands.Extent;
    /// <summary>World x/z to 0..1 on the map image.</summary>
    public static Vector2 MapUV(float x, float z) => new((x + MapExtent) / (2 * MapExtent), (z + MapExtent) / (2 * MapExtent));
    /// <summary>Where fast travel to a point of interest puts the player.</summary>
    public static Vector3 TravelPoint(Poi p) => p.Sky ? new Vector3(p.X, p.Y + 1.5f, p.Z + 3f) : OnGround(p.X, p.Z + (p.Id == "village" ? 9 : 6), 0.6f);
    public static ShaderMaterial? TerrainMaterial => I?._terrMat;
    public static ShaderMaterial? WaterMaterial => I?._water;

    // ---- realm-aware materials and lights
    static readonly List<(StandardMaterial3D m, Color l, Color d)> Mats = new();
    static readonly List<(OmniLight3D lt, float e, int realm)> Lamps = new();
    public static StandardMaterial3D Mat(Color light, Color? dark = null, float rough = 0.9f, float metal = 0f)
    {
        var m = new StandardMaterial3D { AlbedoColor = light, Roughness = rough, Metallic = metal };
        var d = dark ?? new Color(light.R * 0.42f + 0.06f, light.G * 0.34f + 0.04f, light.B * 0.55f + 0.13f);
        if (dark != null) d = d.Lerp(new Color(.42f, .36f, .55f), .25f); // lift hand-picked Shadow colours so buildings read as shapes, not black holes
        Mats.Add((m, light, d)); return m;
    }
    /// <summary>Realm tint for a material made elsewhere (imported models): its current colour in the Light, a darker violet one in the Shadow.</summary>
    public static void Track(StandardMaterial3D m)
    {
        var l = m.AlbedoColor; if (Mats.Exists(x => x.m == m)) return;
        Mats.Add((m, l, new Color(l.R * .55f + .06f, l.G * .45f + .04f, l.B * .7f + .13f)));
    }
    public static StandardMaterial3D WindowMat = new() { AlbedoColor = new Color(0.2f, 0.22f, 0.26f), EmissionEnabled = true, Emission = new Color(1, .75f, .4f), EmissionEnergyMultiplier = 0f };
    /// <summary>Lantern glass on lamp posts: lit like windows, but fades out when the camera is right next to it.</summary>
    public static StandardMaterial3D LampMat = new() { AlbedoColor = new Color(0.35f, 0.3f, 0.22f), EmissionEnabled = true, Emission = new Color(1, .75f, .4f), EmissionEnergyMultiplier = 0.2f,
        DistanceFadeMode = BaseMaterial3D.DistanceFadeModeEnum.PixelAlpha, DistanceFadeMinDistance = 1.5f, DistanceFadeMaxDistance = 4.5f };
    /// <summary>Shader materials that take a realm_t parameter (water, sky...).</summary>
    public static readonly List<ShaderMaterial> RealmShaders = new();
    public static OmniLight3D Lamp(Node parent, Vector3 pos, Color col, float range = 14f, float energy = 1.5f, int realmOnly = -1, bool nightOnly = true)
    {
        var l = new OmniLight3D { LightColor = col, OmniRange = range, LightEnergy = nightOnly ? 0f : energy, ShadowEnabled = false };
        parent.AddChild(l); l.Position = pos; Lamps.Add((l, nightOnly ? energy : -energy, realmOnly)); return l;
    }

    // =================================================================== math / noise (ported exactly from the prototype)
    static readonly byte[] Perm = new byte[512];
    static Func<float> Mul32(uint a) => () => { unchecked { a += 0x6D2B79F5u; uint t = (a ^ (a >> 15)) * (1u | a); t = (t + (t ^ (t >> 7)) * (61u | t)) ^ t; return (float)((t ^ (t >> 14)) / 4294967296.0); } };
    public static Func<float> Rng(uint seed) => Mul32(seed);
    static World()
    {
        var r = Mul32(7); var p = new int[256]; for (int i = 0; i < 256; i++) p[i] = i;
        for (int i = 255; i > 0; i--) { int j = (int)Math.Floor(r() * (i + 1)); (p[i], p[j]) = (p[j], p[i]); }
        for (int i = 0; i < 512; i++) Perm[i] = (byte)p[i & 255];
    }
    static float Smooth(float a, float b, float x) { float t = Mathf.Clamp((x - a) / (b - a), 0f, 1f); return t * t * (3 - 2 * t); }
    static float VNoise(float x, float y)
    {
        int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y); float xf = x - xi, yf = y - yi, u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf);
        float a = Perm[(Perm[xi & 255] + yi) & 511] / 255f, b = Perm[(Perm[(xi + 1) & 255] + yi) & 511] / 255f;
        float c = Perm[(Perm[xi & 255] + yi + 1) & 511] / 255f, d = Perm[(Perm[(xi + 1) & 255] + yi + 1) & 511] / 255f;
        return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
    }
    public static float Fbm(float x, float y, int o = 5) { float s = 0, a = .5f, f = 1, n = 0; for (int i = 0; i < o; i++) { s += a * VNoise(x * f, y * f); n += a; a *= .5f; f *= 2.03f; } return s / n; }
    static float DistSeg(float px, float pz, float ax, float az, float bx, float bz)
    {
        float dx = bx - ax, dz = bz - az, l2 = dx * dx + dz * dz; float t = l2 > 0 ? Mathf.Clamp(((px - ax) * dx + (pz - az) * dz) / l2, 0, 1) : 0;
        return Mathf.Sqrt((px - ax - dx * t) * (px - ax - dx * t) + (pz - az - dz * t) * (pz - az - dz * t));
    }
    public static float RoadDist(float x, float z)
    {
        float m = 1e9f;
        foreach (var r in Roads) for (int i = 0; i < r.Length - 1; i++) m = Mathf.Min(m, DistSeg(x, z, r[i].X, r[i].Y, r[i + 1].X, r[i + 1].Y));
        return m;
    }
    static float BaseHeight(float x, float z)
    {
        float d = Mathf.Sqrt(x * x + z * z) / Half;
        float mask = 1 - Smooth(.62f, .98f, d + (Fbm(x * .01f + 30, z * .01f - 8, 3) - .5f) * .25f);
        float n = Fbm(x * .0075f, z * .0075f, 5), hills = Mathf.Pow(n, 1.35f) * 34f, m = 0;
        foreach (var mt in _mts) { float dd = Mathf.Sqrt((x - mt.x) * (x - mt.x) + (z - mt.z) * (z - mt.z)) / mt.r; if (dd < 1) m += mt.h * Mathf.Pow(1 - dd, 1.7f) * (.8f + Fbm(x * .03f, z * .03f, 3) * .5f); }
        return mask * (hills + m) - (1 - mask) * 10f + 1.5f;
    }
    static float HeightFn(float x, float z)
    {
        float h = BaseHeight(x, z);
        // small natural bumps and hollows (flattened along roads), so open ground is not one smooth sheet
        if (h > 1.2f) h += (Fbm(x * .09f + 51, z * .09f - 17, 3) - .5f) * .7f * Smooth(2.5f, 6f, RoadDist(x, z)) * Smooth(1.2f, 3f, h);
        foreach (var zn in Zones) { float d = Mathf.Sqrt((x - zn.X) * (x - zn.X) + (z - zn.Z) * (z - zn.Z)); if (d < zn.R + zn.B) h = Mathf.Lerp(h, zn.H, 1 - Smooth(zn.R, zn.R + zn.B, d)); }
        return h;
    }
    public static float GroundAt(float x, float z)
    {
        float fx = (x + Half) / Cell, fz = (z + Half) / Cell;
        if (fx < 0 || fz < 0 || fx >= N || fz >= N) return Lands.GroundAt(x, z);   // the other lands (data/lands.json), or -9 over open sea
        int ix = (int)fx, iz = (int)fz; float u = fx - ix, v = fz - iz; int i0 = iz * (N + 1) + ix;
        float h0 = HG[i0], h1 = HG[i0 + 1], h2 = HG[i0 + N + 1], h3 = HG[i0 + N + 2];
        return u + v <= 1 ? h0 + u * (h1 - h0) + v * (h2 - h0) : h3 + (1 - u) * (h2 - h3) + (1 - v) * (h1 - h3);
    }
    public static Vector3 OnGround(float x, float z, float up = 0f) => new Vector3(x, GroundAt(x, z) + up, z);

    // =================================================================== data
    static void LoadData()
    {
        if (Pois.Count > 0) return;
        using var doc = JsonUtil.Load("res://data/world.json"); var r = doc.RootElement;
        foreach (var p in r.GetProperty("pois").EnumerateArray())
        {
            var o = new Poi { Id = JsonUtil.S(p, "id"), Name = JsonUtil.S(p, "name"), Icon = JsonUtil.S(p, "icon"), X = JsonUtil.F(p, "x"), Z = JsonUtil.F(p, "z"), R = JsonUtil.F(p, "r"), Lvl = (int)JsonUtil.F(p, "lvl"), Dun = p.TryGetProperty("dun", out var d) ? d.GetString() : null };
            if (p.TryGetProperty("door", out var dr)) { o.Door = new Vector3(JsonUtil.F(dr, "x"), 0, JsonUtil.F(dr, "z")); o.DoorYaw = JsonUtil.F(dr, "yaw"); }
            Pois.Add(o);
        }
        foreach (var m in r.GetProperty("mountains").EnumerateArray()) _mts.Add((JsonUtil.F(m, "x"), JsonUtil.F(m, "z"), JsonUtil.F(m, "r"), JsonUtil.F(m, "h")));
        foreach (var rd in r.GetProperty("roads").EnumerateArray()) { var l = new List<Vector2>(); foreach (var pt in rd.EnumerateArray()) l.Add(new Vector2(pt[0].GetSingle(), pt[1].GetSingle())); Roads.Add(l.ToArray()); }
        foreach (var z in r.GetProperty("zones").EnumerateArray()) Zones.Add(new Zone { Id = JsonUtil.S(z, "id"), X = JsonUtil.F(z, "x"), Z = JsonUtil.F(z, "z"), R = JsonUtil.F(z, "r"), B = JsonUtil.F(z, "b"), H = JsonUtil.F(z, "h") });
        for (int iz = 0; iz <= N; iz++) for (int ix = 0; ix <= N; ix++) HG[iz * (N + 1) + ix] = HeightFn(-Half + ix * Cell, -Half + iz * Cell);
        Lands.Load();
    }

    // =================================================================== build
    public override void _Ready()
    {
        I = this; LoadData();
        Lamps.RemoveAll(l => !IsInstanceValid(l.lt));   // lamps of a world freed by "Quit to title" (the list is static)
        Over = new Node3D { Name = "Overworld" }; AddChild(Over);
        BuildEnvironment(); BuildTerrain(); BuildWater(); BuildBounds(); BuildVegetation(); Flora.Build(Over); Lands.Build(Over); BuildMapImage(); GatherDb.Spawn(Over);
        Builder.BuildAll(Over); Wildlife.Spawn(Over); IndexEnemies();
        Sfx.I?.SetRealmMix(0);
        TimeOfDay = Game.D.Time;
        UpdateVisuals(0, true);
    }

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
float h21(vec2 p){ vec3 q = fract(vec3(p.xyx) * 0.1031); q += dot(q, q.yzx + 33.33); return fract((q.x + q.y) * q.z); }
float vn(vec2 p){ vec2 i = floor(p); vec2 f = fract(p); f = f*f*(3.0-2.0*f); return mix(mix(h21(i), h21(i+vec2(1.0,0.0)), f.x), mix(h21(i+vec2(0.0,1.0)), h21(i+vec2(1.0,1.0)), f.x), f.y); }
float fbm(vec2 p){ float s = 0.0; float a = 0.5; mat2 r = mat2(vec2(0.8, 0.6), vec2(-0.6, 0.8)); for (int i = 0; i < 5; i++){ s += a * vn(p); p = r * p * 2.03 + vec2(1.7, 9.2); a *= 0.5; } return s; }
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
  col += mix(sun_col, vec3(0.6, 0.7, 1.0), night) * halo * (1.0 - realm_t * 0.6);
  float cl = 0.0;
  if (!AT_CUBEMAP_PASS) {
    if (y > 0.0 && night > 0.02) {
      vec3 g = floor(d * 520.0); float r = h21(g.xy + g.z * 17.31);
      float tw = 0.6 + 0.4 * sin(r * 600.0 + wind.x * 900.0);
      col += vec3(0.85, 0.9, 1.0) * step(0.9975, r) * night * tw * smoothstep(0.0, 0.25, y) * 1.6;
    }
    vec2 uv = d.xz / (y + 0.12) * 0.55 + wind;
    float n = fbm(uv * 1.3);
    cl = smoothstep(1.0 - cover - 0.08, 1.0 - cover + 0.28, n) * smoothstep(0.0, 0.14, y);
    float n2 = fbm(uv * 1.3 + sh * 0.12);
    float lit = clamp(0.65 + (n - n2) * 5.0, 0.25, 1.25);
    vec3 cday = mix(vec3(0.6, 0.65, 0.75), vec3(1.05), lit) + dusk_col * dusk * (0.3 + 0.7 * az) * 0.8;
    vec3 cnight = vec3(0.05, 0.06, 0.11) * (0.5 + lit * 0.5);
    vec3 ccol = mix(cday, cnight, night);
    ccol = mix(ccol, vec3(0.3, 0.1, 0.4) * (0.5 + lit * 0.6), realm_t);
    col = mix(col, ccol, cl * 0.92);
    float neb = fbm(d.xz / (y + 0.35) * 0.45 - wind * 0.4);
    col += mix(vec3(0.3, 0.05, 0.4), vec3(0.03, 0.35, 0.4), neb) * neb * realm_t * smoothstep(0.0, 0.45, y) * 0.7;
    float rift = 1.0 - smoothstep(0.0, 0.01, abs(fbm(d.xz / (y + 0.5) * 0.7 + 3.0) - 0.5));
    col += vec3(0.95, 0.45, 1.0) * rift * realm_t * smoothstep(0.15, 0.5, y) * 0.9;
  }
  float disc = smoothstep(mix(0.99955, 0.9997, night), mix(0.9998, 0.99985, night), sdot);
  col += mix(sun_col * 14.0, vec3(0.85, 0.9, 1.0) * 2.5, night) * disc * (1.0 - cl) * (1.0 - realm_t * 0.7);
  COLOR = col;
}";

    void BuildTerrain()
    {
        int V = (N + 1) * (N + 1);
        var verts = new Vector3[V]; var norms = new Vector3[V]; var cols = new Color[V]; var uvs = new Vector2[V];
        for (int iz = 0; iz <= N; iz++) for (int ix = 0; ix <= N; ix++)
        {
            int i = iz * (N + 1) + ix; float x = -Half + ix * Cell, z = -Half + iz * Cell, h = HG[i];
            verts[i] = new Vector3(x, h, z); uvs[i] = new Vector2(ix * Cell / 7f, iz * Cell / 7f);
            float hl = HG[iz * (N + 1) + Math.Max(ix - 1, 0)], hr = HG[iz * (N + 1) + Math.Min(ix + 1, N)], hd = HG[Math.Max(iz - 1, 0) * (N + 1) + ix], hu = HG[Math.Min(iz + 1, N) * (N + 1) + ix];
            var nrm = new Vector3(hl - hr, 2 * Cell, hd - hu).Normalized(); norms[i] = nrm;
            // ground layer weights for the terrain shader (it blends the textures in assets/textures/terrain):
            // R = road, G = sand by the water, B = forest floor (same noise that places the trees), A = snow on the peaks.
            // Rock on steep slopes, grass/dirt patches and gravel patches are worked out in the shader.
            float ny = nrm.Y, rd = RoadDist(x, z);
            float road = 1 - Smooth(1.6f, 3.6f, rd), sand = 1 - Smooth(.6f, 2.0f, h);
            float forest = Smooth(.5f, .68f, Fbm(x * .02f + 3, z * .02f + 9, 3)) * .75f * (1 - Smooth(40, 46, h)) * Smooth(1.6f, 2.6f, h);
            float snow = Smooth(52, 60, h) * Smooth(.5f, .78f, ny);
            cols[i] = new Color(road, sand, forest, snow);
        }
        var idx = new int[N * N * 6]; int k = 0;
        for (int iz = 0; iz < N; iz++) for (int ix = 0; ix < N; ix++)
        {
            int i0 = iz * (N + 1) + ix, i1 = i0 + 1, i2 = i0 + N + 1, i3 = i2 + 1;
            idx[k++] = i0; idx[k++] = i1; idx[k++] = i2; idx[k++] = i1; idx[k++] = i3; idx[k++] = i2;
        }
        var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts; arrays[(int)Mesh.ArrayType.Normal] = norms; arrays[(int)Mesh.ArrayType.Color] = cols;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs; arrays[(int)Mesh.ArrayType.TexUV2] = new Vector2[verts.Length]; arrays[(int)Mesh.ArrayType.Index] = idx;
        var mesh = new ArrayMesh(); mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        var sh = new Shader { Code = TerrainShader };
        _terrMat = new ShaderMaterial { Shader = sh }; _terrMat.SetShaderParameter("realm_t", 0f);
        foreach (var layer in new[] { "grass", "meadow", "gravel", "rock", "sand", "forest" })
        {
            string col = $"res://assets/textures/terrain/{layer}_col.jpg", nrm = $"res://assets/textures/terrain/{layer}_nrm.jpg", ao = $"res://assets/textures/terrain/{layer}_ao.jpg";
            if (ResourceLoader.Exists(ao)) _terrMat.SetShaderParameter(layer + "_ao", GD.Load<Texture2D>(ao));
            if (ResourceLoader.Exists(col)) _terrMat.SetShaderParameter(layer + "_col", GD.Load<Texture2D>(col));
            if (ResourceLoader.Exists(nrm)) _terrMat.SetShaderParameter(layer + "_nrm", GD.Load<Texture2D>(nrm));
        }
        // data/look.json "terrain": tile sizes and colour grading of the ground textures
        using (var ld = JsonUtil.Load("res://data/look.json"))
            if (ld.RootElement.TryGetProperty("terrain", out var tc))
                foreach (var p in tc.EnumerateObject())
                {
                    if (p.Name.StartsWith("_")) continue;
                    if (p.Value.ValueKind == JsonValueKind.Number) _terrMat.SetShaderParameter(p.Name, p.Value.GetSingle());
                    else if (p.Value.ValueKind == JsonValueKind.String) _terrMat.SetShaderParameter(p.Name, Color.FromHtml(p.Value.GetString()!));
                }
        var body = new StaticBody3D { Name = "Terrain", CollisionLayer = 1 };
        body.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = _terrMat });
        body.AddChild(new CollisionShape3D { Shape = mesh.CreateTrimeshShape() });
        Over.AddChild(body);
    }

    const string TerrainShader = @"
shader_type spatial;
render_mode specular_schlick_ggx;
// Textured ground: grass, patchy grass/dirt, gravel, rock on slopes, sand by the water, forest floor under trees, snow on peaks.
// Vertex COLOR carries the weights worked out on the CPU (R road, G sand, B forest, A snow); the rest comes from noise and slope.
// Each texture is read twice at two scales and rotated, and the two are mixed by noise, so the tiling does not show.
uniform float realm_t = 0.0;
uniform sampler2D grass_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D meadow_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D gravel_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D rock_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D sand_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D forest_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D grass_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D meadow_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D gravel_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D rock_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D sand_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D forest_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D grass_ao : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D meadow_ao : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D gravel_ao : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D rock_ao : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D sand_ao : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D forest_ao : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float ao_strength = 0.8;
uniform float grass_tile = 3.5;      // metres per texture repeat
uniform float meadow_tile = 5.0;
uniform float gravel_tile = 2.5;
uniform float rock_tile = 6.0;
uniform float sand_tile = 4.0;
uniform float forest_tile = 3.0;
uniform vec3 grass_tint : source_color = vec3(1.0);
uniform vec3 meadow_tint : source_color = vec3(1.0);
uniform vec3 forest_tint : source_color = vec3(1.0);
uniform vec3 road_tint : source_color = vec3(1.0);
uniform vec3 rock_tint : source_color = vec3(1.0);
uniform vec3 sand_tint : source_color = vec3(1.0);
uniform float saturation = 1.0;
uniform float brightness = 1.0;
uniform float normal_depth = 0.8;
uniform float meadow_amount = 0.55;   // how much of the open grass has dirt patches
uniform float gravel_amount = 0.5;    // loose gravel patches in the open
varying vec3 wpos;
varying vec3 wnrm;
float h21(vec2 p){ return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float vn(vec2 p){ vec2 i = floor(p); vec2 f = fract(p); f = f*f*(3.0-2.0*f); return mix(mix(h21(i), h21(i+vec2(1.0,0.0)), f.x), mix(h21(i+vec2(0.0,1.0)), h21(i+vec2(1.0,1.0)), f.x), f.y); }
float fb(vec2 p){ return vn(p)*0.5 + vn(p*2.07)*0.25 + vn(p*4.13)*0.125 + vn(p*8.3)*0.0625 + 0.03; }
const mat2 ROT = mat2(vec2(0.80, 0.60), vec2(-0.60, 0.80));
vec3 tex2(sampler2D s, vec2 p, float k){ return mix(texture(s, p).rgb, texture(s, ROT * p * 0.43 + vec2(0.37, 0.71)).rgb, k); }
// height-aware blend: where a layer is thin, its bright/raised bits show first (gravel between grass, grass tufts on dirt)
float hb(float w, float h){ return smoothstep(0.0, 1.0, clamp(w * 1.6 - 0.3 + (h - 0.5) * 0.6, 0.0, 1.0)); }
float lum(vec3 c){ return dot(c, vec3(0.3, 0.59, 0.11)); }
void vertex(){
  wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
  wnrm = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
  // the terrain mesh has no tangents: build them from the world X/Z axes the textures are laid along
  TANGENT = normalize(vec3(1.0, 0.0, 0.0) - NORMAL * NORMAL.x);
  BINORMAL = normalize(cross(TANGENT, NORMAL));
}
void fragment(){
  vec2 p = wpos.xz;
  float k = smoothstep(0.35, 0.65, fb(p * 0.05 + 3.1));                 // which of the two texture reads shows
  float macro = fb(p * 0.012), patchy = fb(p * 0.06 + 11.0), grit = fb(p * 0.045 + 31.0);
  float slope = 1.0 - clamp(wnrm.y, 0.0, 1.0);

  vec3 grass = tex2(grass_col, p / grass_tile, k) * grass_tint;
  vec3 meadow = tex2(meadow_col, p / meadow_tile, k) * meadow_tint;
  vec3 gravel = tex2(gravel_col, p / gravel_tile, k);
  vec3 sand = tex2(sand_col, p / sand_tile, k) * sand_tint;
  vec3 forest = tex2(forest_col, p / forest_tile, k) * forest_tint;
  // rock: three projections so steep faces are not stretched
  vec3 bw = pow(abs(wnrm), vec3(4.0)); bw /= (bw.x + bw.y + bw.z);
  vec3 rock = (texture(rock_col, wpos.zy / rock_tile).rgb * bw.x + texture(rock_col, p / rock_tile).rgb * bw.y + texture(rock_col, wpos.xy / rock_tile).rgb * bw.z) * rock_tint;
  vec3 road = mix(gravel, sand * 0.85, 0.45 + 0.3 * (patchy - 0.5)) * road_tint;

  float wMeadow = hb(smoothstep(0.42, 0.72, patchy + (macro - 0.5) * 0.4) * meadow_amount, lum(meadow) * 2.0);
  float wGravel = hb(smoothstep(0.6, 0.74, grit) * gravel_amount, lum(gravel) * 1.6);
  float wForest = hb(COLOR.b, lum(forest) * 2.2);
  float wRoad = hb(COLOR.r, lum(road) * 1.8);
  float wSand = hb(COLOR.g, lum(sand) * 1.5);
  float wRock = hb(max(smoothstep(0.2, 0.36, slope + (patchy - 0.5) * 0.12), smoothstep(34.0, 46.0, wpos.y) * 0.85), lum(rock) * 2.0);
  float wSnow = COLOR.a;
  // UV2 (the other lands): x = extra rock (rocky biomes), y = volcanic ash
  wRock = max(wRock, hb(UV2.x * (0.55 + patchy * 0.9), lum(rock) * 2.0));

  vec3 c = grass;
  c = mix(c, meadow, wMeadow);
  c = mix(c, forest, wForest);
  c = mix(c, gravel, wGravel * (1.0 - COLOR.b));
  c = mix(c, road, wRoad);
  c = mix(c, sand, wSand);
  c = mix(c, rock, wRock);
  c = mix(c, vec3(0.9, 0.93, 0.97), wSnow);
  c = mix(c, c * vec3(0.36, 0.33, 0.32), UV2.y * (0.55 + 0.45 * patchy));
  c *= (0.84 + 0.32 * macro);                                             // large soft light/dark patches seen from afar
  c = mix(vec3(lum(c)), c, saturation) * brightness;

  // normal and ambient-occlusion maps follow the same layer weights as the colour
  float wG = wGravel * (1.0 - COLOR.b);
  vec3 nG = texture(grass_nrm, p / grass_tile).rgb; float ao = texture(grass_ao, p / grass_tile).r;
  nG = mix(nG, texture(meadow_nrm, p / meadow_tile).rgb, wMeadow); ao = mix(ao, texture(meadow_ao, p / meadow_tile).r, wMeadow);
  nG = mix(nG, texture(forest_nrm, p / forest_tile).rgb, wForest); ao = mix(ao, texture(forest_ao, p / forest_tile).r, wForest);
  nG = mix(nG, texture(gravel_nrm, p / gravel_tile).rgb, max(wG, wRoad)); ao = mix(ao, texture(gravel_ao, p / gravel_tile).r, max(wG, wRoad));
  nG = mix(nG, texture(sand_nrm, p / sand_tile).rgb, wSand); ao = mix(ao, texture(sand_ao, p / sand_tile).r, wSand);
  nG = mix(nG, texture(rock_nrm, p / rock_tile).rgb, wRock * bw.y); ao = mix(ao, texture(rock_ao, p / rock_tile).r, wRock);
  nG = mix(nG, vec3(0.5, 0.5, 1.0), max(wRock * (1.0 - bw.y), wSnow));
  ao = mix(1.0, ao, ao_strength * (1.0 - wSnow));
  c *= mix(1.0, ao, 0.5); AO = ao; AO_LIGHT_AFFECT = 0.35;
  NORMAL_MAP = nG; NORMAL_MAP_DEPTH = normal_depth;

  float l = lum(c);
  vec3 sh = vec3(l * 0.5, l * 0.38, l * 0.85) + vec3(0.03, 0.01, 0.06);
  float vein = 1.0 - smoothstep(0.0, 0.03, abs(fb(p * 0.028 + vec2(5.0, -9.0)) - 0.5));
  sh = mix(sh, vec3(0.78, 0.22, 0.6), vein * 0.5 * step(2.0, wpos.y));
  ALBEDO = mix(c, sh, realm_t);
  ROUGHNESS = mix(0.92, 0.6, wSnow); SPECULAR = 0.25;
}";

    void BuildMapImage()
    {
        // the whole world, -MapExtent..MapExtent on both axes (home island, the other lands, sky islands, bridges)
        _mapImg = Lands.MapImage(MapSize);
        MapTex = ImageTexture.CreateFromImage(_mapImg);
    }

    void BuildWater()
    {
        // animated waves, fresnel, depth-tinted shallows and shore foam (reads the depth buffer)
        _water = new ShaderMaterial { Shader = new Shader { Code = WaterShader } }; RealmShaders.Add(_water);
        foreach (var k in new[] { "shallow", "deep", "foam", "shadowShallow", "shadowDeep" })
            _water.SetShaderParameter(k switch { "shallow" => "shallow", "deep" => "deep", "foam" => "foam", "shadowShallow" => "s_shallow", _ => "s_deep" }, LC(k, "#3080a0"));
        Over.AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(30000, 30000) }, MaterialOverride = _water, Position = new Vector3(0, 0.25f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
    }

    const string WaterShader = @"
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_disabled, specular_schlick_ggx;
uniform vec3 shallow : source_color; uniform vec3 deep : source_color; uniform vec3 foam : source_color;
uniform vec3 s_shallow : source_color; uniform vec3 s_deep : source_color;
uniform float realm_t = 0.0;
uniform sampler2D depth_tex : hint_depth_texture, filter_linear_mipmap;
varying vec3 wp;
float h21(vec2 p){ return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float vn(vec2 p){ vec2 i = floor(p); vec2 f = fract(p); f = f*f*(3.0-2.0*f); return mix(mix(h21(i), h21(i+vec2(1.0,0.0)), f.x), mix(h21(i+vec2(0.0,1.0)), h21(i+vec2(1.0,1.0)), f.x), f.y); }
float waves(vec2 p, float t){
  return sin(p.x * 0.42 + t * 1.1) * 0.35 + sin(dot(p, vec2(0.23, 0.31)) - t * 0.8) * 0.4 + sin(dot(p, vec2(-0.5, 0.17)) * 1.3 + t * 1.4) * 0.18
       + vn(p * 0.9 + vec2(t * 0.35, t * 0.2)) * 0.5 + vn(p * 2.7 - vec2(t * 0.6, 0.0)) * 0.18;
}
void vertex(){ wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
void fragment(){
  float t = TIME; vec2 p = wp.xz; float e = 0.12;
  float w0 = waves(p, t), wx = waves(p + vec2(e, 0.0), t), wz = waves(p + vec2(0.0, e), t);
  vec3 nw = normalize(vec3((w0 - wx) / e * 0.35, 1.0, (w0 - wz) / e * 0.35));
  NORMAL = normalize((VIEW_MATRIX * vec4(nw, 0.0)).xyz);
  float dr = textureLod(depth_tex, SCREEN_UV, 0.0).r;
  vec4 up = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, dr, 1.0); up.xyz /= up.w;
  float thick = clamp(VERTEX.z - up.z, 0.0, 50.0);
  vec3 sh = mix(shallow, s_shallow, realm_t), dp = mix(deep, s_deep, realm_t);
  vec3 c = mix(sh, dp, smoothstep(0.0, 6.0, thick));
  float fr = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 4.0);
  float fn = vn(p * 3.0 + vec2(t * 0.5, -t * 0.3));
  float fo = (1.0 - smoothstep(0.0, 0.9 + fn * 0.6, thick)) * (0.55 + 0.45 * sin(thick * 9.0 - t * 2.5 + fn * 4.0));
  fo = clamp(fo + smoothstep(0.82, 0.95, w0 * 0.55 + fn * 0.4) * 0.35, 0.0, 1.0);
  ALBEDO = mix(c, foam, fo);
  ROUGHNESS = mix(0.04, 0.5, fo); SPECULAR = 0.6; METALLIC = 0.0;
  EMISSION = mix(vec3(0.0), vec3(0.25, 0.05, 0.35), realm_t * 0.4 * (1.0 - fo));
  ALPHA = clamp(mix(0.55, 0.95, smoothstep(0.0, 3.0, thick)) + fr * 0.3 + fo, 0.0, 1.0);
}";

    void BuildBounds()
    {
        var b = new StaticBody3D { Name = "Bounds", CollisionLayer = 1 };
        // the edge of the whole world (home island, the lands, the sky), and a sea floor under the open sea between the lands
        float e = MapExtent - 6f, t = 4f, h = 1200f;
        void Wall(Vector3 pos, Vector3 size) { b.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = pos + Vector3.Up * 300f }); }
        Wall(new Vector3(0, 0, -e), new Vector3(2 * e, h, t)); Wall(new Vector3(0, 0, e), new Vector3(2 * e, h, t));
        Wall(new Vector3(-e, 0, 0), new Vector3(t, h, 2 * e)); Wall(new Vector3(e, 0, 0), new Vector3(t, h, 2 * e));
        b.AddChild(new CollisionShape3D { Shape = new WorldBoundaryShape3D { Plane = new Plane(Vector3.Up, -13f) } });
        Over.AddChild(b);
    }

    // =================================================================== vegetation (MultiMesh)
    static ArrayMesh Merge(params (Mesh m, Transform3D t)[] parts)
    {
        var st = new SurfaceTool();
        foreach (var p in parts) st.AppendFrom(p.m, 0, p.t);
        return st.Commit();
    }
    static Transform3D T(Vector3 pos, Vector3? scale = null, Vector3? rot = null)
    {
        var basis = Basis.Identity; if (rot.HasValue) basis = Basis.FromEuler(rot.Value); if (scale.HasValue) basis = basis.Scaled(scale.Value);
        return new Transform3D(basis, pos);
    }

    void BuildVegetation()
    {
        var rg = Mul32(20261107u);
        float R(float a, float b) => a + rg() * (b - a);
        var pines = new List<Transform3D>(); var oaks = new List<Transform3D>(); var rocks = new List<Transform3D>(); var rockKit = new List<Transform3D>(); var flowers = new List<(Transform3D, Color)>();
        var col = new StaticBody3D { Name = "TreeColliders", CollisionLayer = 1 }; Over.AddChild(col);
        int tries = 0;
        while ((pines.Count + oaks.Count) < 1100 && tries++ < 9000)
        {
            float x = R(-Half + 14, Half - 14), z = R(-Half + 14, Half - 14); float h = GroundAt(x, z);
            if (h < 1.6f || h > 44f || FreeSpot(x, z, 3f) == false) continue;
            float n = Fbm(x * .02f + 3, z * .02f + 9, 3); if (n < .38f && rg() < .85f) continue;
            float ny = (GroundAt(x + 2, z) - GroundAt(x - 2, z)); if (Mathf.Abs(ny) > 3.5f) continue;
            float sc = R(.8f, 1.4f); var tf = T(new Vector3(x, h - 0.1f, z), new Vector3(sc, sc * R(.9f, 1.25f), sc), new Vector3(0, R(0, Mathf.Tau), 0));
            if (h > 11f || rg() < .35f) pines.Add(tf); else oaks.Add(tf);
            col.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 0.45f * sc, Height = 3f }, Position = new Vector3(x, h + 1.4f, z) });
        }
        for (int i = 0; i < 260; i++)
        {
            float x = R(-Half + 12, Half - 12), z = R(-Half + 12, Half - 12), h = GroundAt(x, z); if (h < 1.2f || !FreeSpot(x, z, 1.5f)) continue;
            float s = R(.5f, 2.2f); rocks.Add(T(new Vector3(x, h + s * .15f, z), new Vector3(s * R(.8f, 1.4f), s * R(.5f, 1f), s * R(.8f, 1.4f)), new Vector3(R(0, 3), R(0, 6), R(0, 3))));
            rockKit.Add(T(new Vector3(x, h - s * .12f, z), Vector3.One * s, new Vector3(0, x * 1.7f + z, 0)));   // kit rocks: flat bottom, upright
        }
        for (int i = 0; i < 700; i++)
        {
            float x = R(-110, 110), z = R(-110, 110), h = GroundAt(x, z); if (h < 1.5f || h > 14f || RoadDist(x, z) < 3f) continue;
            var c = new[] { new Color(1, .45f, .55f), new Color(1, .9f, .3f), Colors.White, new Color(.7f, .5f, 1f), new Color(1, .6f, .2f) }[(int)(rg() * 5) % 5];
            flowers.Add((T(new Vector3(x, h + .12f, z), new Vector3(.12f, .12f, .12f)), c));
        }
        if (Kit.Available) { KitVegetation(pines, oaks, rockKit, flowers); return; }
        // pines: four jagged drooping tiers; oaks: clusters of faceted noise blobs (scripts/Gfx.cs)
        var pineFol = Gfx.Pine(3);
        Mesh Blob(float r, uint seed) => Gfx.Lumpy(new SphereMesh { Radius = r, Height = r * 2, RadialSegments = 9, Rings = 6 }, r * .22f, 1.1f / r, seed, .9f);
        var oakFol = Merge(
            (Blob(1.6f, 1), T(new Vector3(0, 4.4f, 0), new Vector3(1.2f, 1.0f, 1.2f))),
            (Blob(1.15f, 2), T(new Vector3(1.35f, 3.7f, 0.5f))),
            (Blob(1.1f, 3), T(new Vector3(-1.15f, 3.95f, -0.7f))),
            (Blob(.95f, 4), T(new Vector3(.2f, 5.4f, -.5f))),
            (Blob(.9f, 5), T(new Vector3(-.4f, 3.5f, 1.2f))));
        var trunkP = new CylinderMesh { TopRadius = 0.18f, BottomRadius = 0.3f, Height = 2.6f, RadialSegments = 6 };
        var trunkO = new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.45f, Height = 3.2f, RadialSegments = 6 };
        var bark = Gfx.Tex(Mat(new Color(.36f, .25f, .17f), new Color(.1f, .08f, .13f)), "bark", 1.5f, true, 1.5f);
        var pineMat = Mat(new Color(.2f, .42f, .21f), new Color(.1f, .12f, .2f)); pineMat.VertexColorUseAsAlbedo = true;
        var oakMat = Mat(new Color(.34f, .58f, .23f), new Color(.2f, .12f, .3f)); oakMat.VertexColorUseAsAlbedo = true;
        var rockMat = Gfx.Tex(Mat(new Color(.6f, .59f, .56f), new Color(.2f, .17f, .26f), 1f), "rock", 4f, true, 1.2f);
        // per-tree tint so a forest is not one flat green (yellowish, bluish, darker)
        Color Tint(int i) { float k = (float)i * 0.618034f % 1f, j = (float)i * 0.414213f % 1f; return new Color(.82f + .3f * k, .86f + .22f * j, .8f + .2f * (1 - k)); }
        Multi(Merge((trunkP, T(new Vector3(0, 1.3f, 0)))), bark, pines);
        Multi(pineFol, pineMat, pines, Tint);
        Multi(Merge((trunkO, T(new Vector3(0, 1.6f, 0)))), bark, oaks);
        Multi(oakFol, oakMat, oaks, Tint);
        var rA = new List<Transform3D>(); var rB = new List<Transform3D>(); for (int i = 0; i < rocks.Count; i++) (i % 2 == 0 ? rA : rB).Add(rocks[i]);
        Multi(Gfx.Boulder(0), rockMat, rA); Multi(Gfx.Boulder(1), rockMat, rB);
        // flowers with per-instance colour
        var fm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 5, Rings = 3 }, InstanceCount = flowers.Count };
        for (int i = 0; i < flowers.Count; i++) { fm.SetInstanceTransform(i, flowers[i].Item1); fm.SetInstanceColor(i, flowers[i].Item2); }
        Over.AddChild(new MultiMeshInstance3D { Multimesh = fm, MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true } });
    }

    void Multi(Mesh mesh, Material mat, List<Transform3D> list, Func<int, Color>? tint = null)
    {
        if (list.Count == 0) return;
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = tint != null, Mesh = mesh, InstanceCount = list.Count };
        for (int i = 0; i < list.Count; i++) { mm.SetInstanceTransform(i, list[i]); if (tint != null) mm.SetInstanceColor(i, tint(i)); }
        Over.AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = mat });
    }

    /// <summary>Kit models (data/env.json) in the same spots the procedural trees, rocks and flowers would use, plus bushes and ferns.</summary>
    void KitVegetation(List<Transform3D> pines, List<Transform3D> oaks, List<Transform3D> rocks, List<(Transform3D t, Color c)> flowers)
    {
        var cfg = Kit.Cfg; float treeVis = cfg.TryGetProperty("trees", out var tr) ? JsonUtil.F(tr, "visEnd", 300f) : 300f;
        // per-tree tint so a forest is not one flat green
        // (red kept lower: the lime-topped kit canopies turned orange under the warm sun)
        Color Tint(int i) { float k = (float)i * 0.618034f % 1f, j = (float)i * 0.414213f % 1f; return new Color(.68f + .2f * k, .86f + .16f * j, .74f + .16f * (1 - k)); }
        void Spread(List<Kit.Model> models, List<Transform3D> list, float vis, Func<int, Color>? tint = null)
        {
            if (models.Count == 0 || list.Count == 0) return;
            for (int v = 0; v < models.Count; v++)
            {
                var p = Kit.Get(models[v].Name); if (p == null) continue;
                var local = new Transform3D(Basis.Identity.Scaled(Vector3.One * models[v].Scale), Vector3.Zero) * p.Xf;
                var mine = new List<Transform3D>(); var cols = new List<Color>();
                for (int i = v; i < list.Count; i += models.Count) { mine.Add(list[i] * local); cols.Add(tint?.Invoke(i) ?? Colors.White); }
                Chunked(p.Mesh, mine, tint != null ? cols : null, vis);
            }
        }
        // leafy trees in the lowlands, pines higher up (same spots; chosen by a hash, so the seed sequence is untouched)
        float oakBelow = JsonUtil.F(tr, "oakBelow", 18f), oakShare = JsonUtil.F(tr, "oakShare", .6f);
        var kp = new List<Transform3D>(); var ko = new List<Transform3D>(); int n = 0;
        // size variety per tree (env.json trees.variety): own width and height, a few giants, a slight lean; from a hash of the spot, so nothing else moves
        float[] wR = { .8f, 1.3f }, hR = { .75f, 1.45f }, gR = { 1.7f, 2.4f }; float giantShare = .06f, lean = 4f;
        if (tr.TryGetProperty("variety", out var va))
        {
            float[] A(string k, float[] d) => va.TryGetProperty(k, out var a) && a.GetArrayLength() == 2 ? new[] { a[0].GetSingle(), a[1].GetSingle() } : d;
            wR = A("width", wR); hR = A("height", hR); gR = A("giant", gR); giantShare = JsonUtil.F(va, "giantShare", giantShare); lean = JsonUtil.F(va, "lean", lean);
        }
        Transform3D Vary(Transform3D t)
        {
            float x = t.Origin.X, z = t.Origin.Z;
            float H(float s) { float v = Mathf.Sin(x * 12.9898f + z * 78.233f + s) * 43758.5453f; return v - Mathf.Floor(v); }
            float w = Mathf.Lerp(wR[0], wR[1], H(1)), hgt = Mathf.Lerp(hR[0], hR[1], H(2));
            if (H(3) < giantShare) { float g = Mathf.Lerp(gR[0], gR[1], H(4)); w *= g; hgt *= g; }
            var tilt = new Basis(new Vector3(Mathf.Cos(H(5) * Mathf.Tau), 0, Mathf.Sin(H(5) * Mathf.Tau)), Mathf.DegToRad(lean * H(6)));
            return new Transform3D(tilt * t.Basis * Basis.FromScale(new Vector3(w, hgt, w)), t.Origin - new Vector3(0, .15f * w, 0));
        }
        foreach (var t0 in pines.Concat(oaks)) { var t = Vary(t0); float hsh = (n++ * 0.754877f) % 1f; (t.Origin.Y < oakBelow && hsh < oakShare ? ko : kp).Add(t); }
        Spread(Kit.Models("trees", "pine"), kp, treeVis, Tint);
        Spread(Kit.Models("trees", "oak"), ko, treeVis, Tint);
        Spread(Kit.Models("rocks"), rocks.FindAll(t => FreeSpot(t.Origin.X, t.Origin.Z, 1.5f + t.Basis.Scale.X * 2.2f)), treeVis);   // kit rocks are wider than the old boulders: keep them off gates and roads
        var fl = new List<Transform3D>(); for (int i = 0; i < flowers.Count; i++) { var o = flowers[i].t.Origin; fl.Add(new Transform3D(new Basis(Vector3.Up, i * 2.4f), new Vector3(o.X, o.Y - .12f, o.Z))); }
        Spread(Kit.Models("flowers"), fl, 120f);
        // bushes, ferns and mushrooms: their own seed, so nothing else moves
        if (cfg.TryGetProperty("bushes", out var bu))
        {
            var rg = Mul32(20261110u); float R(float a, float b) => a + rg() * (b - a);
            var list = new List<Transform3D>(); int want = (int)JsonUtil.F(bu, "count", 400), tries = 0;
            while (list.Count < want && tries++ < want * 8)
            {
                float x = R(-Half + 14, Half - 14), z = R(-Half + 14, Half - 14), h = GroundAt(x, z);
                if (h < 1.6f || h > 30f || !FreeSpot(x, z, 1.5f)) continue;
                if (Fbm(x * .02f + 3, z * .02f + 9, 3) < .42f && rg() < .7f) continue;   // mostly at forest edges
                float s = R(.75f, 1.25f); list.Add(T(new Vector3(x, h - .05f, z), Vector3.One * s, new Vector3(0, R(0, Mathf.Tau), 0)));
            }
            Spread(Kit.Models("bushes", "models"), list, JsonUtil.F(bu, "visEnd", 160f), Tint);
        }
    }

    /// <summary>A MultiMesh split into 64 m patches, so off-screen and far patches are skipped.</summary>
    public void Chunked(Mesh mesh, List<Transform3D> list, List<Color>? cols, float visEnd)
    {
        const float C = 64f;
        var groups = new Dictionary<(int, int), List<int>>();
        for (int i = 0; i < list.Count; i++)
        {
            var key = ((int)Mathf.Floor((list[i].Origin.X + Half) / C), (int)Mathf.Floor((list[i].Origin.Z + Half) / C));
            if (!groups.TryGetValue(key, out var g)) groups[key] = g = new List<int>();
            g.Add(i);
        }
        foreach (var (key, idx) in groups)
        {
            var centre = new Vector3(-Half + (key.Item1 + .5f) * C, 0, -Half + (key.Item2 + .5f) * C);
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = cols != null, Mesh = mesh, InstanceCount = idx.Count };
            for (int j = 0; j < idx.Count; j++) { var t = list[idx[j]]; t.Origin -= centre; mm.SetInstanceTransform(j, t); if (cols != null) mm.SetInstanceColor(j, cols[idx[j]]); }
            Over.AddChild(new MultiMeshInstance3D { Multimesh = mm, Position = centre, VisibilityRangeEnd = visEnd, VisibilityRangeEndMargin = 12f });
        }
    }

    /// <summary>True when (x,z) is clear of roads, points of interest and the village.</summary>
    public static bool FreeSpot(float x, float z, float pad)
    {
        if (RoadDist(x, z) < 4f + pad * .3f) return false;
        foreach (var p in Pois) if (new Vector2(x - p.X, z - p.Z).Length() < p.R + pad) return false;
        return true;
    }

    // =================================================================== runtime
    public override void _Process(double delta)
    {
        float dt = (float)delta;
        Clock += dt; ShiftCd = Mathf.Max(0, ShiftCd - dt);
        if (!Dungeon.Active) TimeOfDay = (TimeOfDay + dt / 720f) % 1f;
        float target = Realm; if (Mathf.Abs(RealmT - target) > 0.0005f) { RealmT = Mathf.MoveToward(RealmT, target, dt * 1.2f); Sfx.I?.SetRealmMix(RealmT); }
        else RealmT = target;
        UpdateVisuals(dt, false);
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
        Genshin.SetNight(Night);   // characters switch to their cooler night shadow colours
        Sun.RotationDegrees = new Vector3(-Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(Mathf.Abs(elev) * 0.9f + 0.1f, 0.06f, 1f))), Mathf.RadToDeg(ang) + 90f, 0);
        // warm sunlight, more golden while the sun is low (morning and evening), orange at dusk (data/look.json sunNoon / sunLow / sunDusk)
        var dayCol = LC("sunNoon", "fff1dc").Lerp(LC("sunLow", "ffc98a"), 1f - Smooth(.12f, .6f, elev)).Lerp(LC("sunDusk", "ff8c4a"), dusk); var moon = new Color(.55f, .62f, 1f); var shadowCol = new Color(.6f, .35f, .9f);
        Sun.LightColor = dayCol.Lerp(moon, Smooth(.35f, 0f, day)).Lerp(shadowCol, RealmT);
        Sun.LightEnergy = Mathf.Lerp(LF("sunNight", .22f), LF("sunDay", 1.55f), day) * (1 - RealmT * 0.45f) * (1f - dusk * .25f * day);
        var top = LC("nightTop", "02040d").Lerp(LC("dayTop", "2a64b8"), day).Lerp(LC("duskTop", "2d2f6e"), dusk * .55f).Lerp(LC("shadowTop", "0d0318"), RealmT);
        var hor = LC("nightHorizon", "0b1430").Lerp(LC("dayHorizon", "b5d3ec"), day).Lerp(LC("duskHorizon", "ff8048"), dusk * .5f).Lerp(LC("shadowHorizon", "4a1450"), RealmT);
        float skyNight = Mathf.Clamp(1 - day, 0, 1) * (1 - RealmT);
        _sky.SetShaderParameter("top_col", top); _sky.SetShaderParameter("hor_col", hor); _sky.SetShaderParameter("ground_col", hor.Darkened(.55f));
        _sky.SetShaderParameter("dusk_col", LC("duskGlow", "ff6a3a")); _sky.SetShaderParameter("dusk", dusk * (1 - RealmT)); _sky.SetShaderParameter("night", skyNight);
        _sky.SetShaderParameter("sun_col", dayCol);
        _cloudT += dt * LF("cloudSpeed", .006f); _sky.SetShaderParameter("wind", new Vector2(_cloudT, _cloudT * .35f));
        _env.FogLightColor = hor.Lerp(top, .25f); _env.FogDensity = Mathf.Lerp(LF("fogDay", .0017f), LF("fogShadow", .0045f), RealmT);
        _env.AmbientLightColor = LC("ambientDayColor", "9eafc8").Lerp(LC("ambientNightColor", "38426f"), 1 - day).Lerp(new Color(.9f, .55f, .5f), dusk * .45f).Lerp(new Color(.5f, .38f, .68f), RealmT);
        _env.AmbientLightEnergy = Mathf.Lerp(LF("ambientNight", .45f), LF("ambientDay", .62f), day) * Mathf.Lerp(1f, LF("ambientShadow", 1.25f), RealmT);
        _env.AdjustmentSaturation = Mathf.Lerp(LF("saturation", 1.2f), LF("saturationShadow", .9f), RealmT);
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
            WindowMat.EmissionEnergyMultiplier = Night * 2.2f; WindowMat.Emission = new Color(1, .75f, .4f).Lerp(new Color(.7f, .45f, 1f), RealmT);
            LampMat.EmissionEnergyMultiplier = .15f + Night * 3f; LampMat.Emission = WindowMat.Emission;
        }
        foreach (var (lt, e, realm) in Lamps)
        {
            bool realmOk = realm < 0 || (realm == 1 && RealmT > .5f) || (realm == 0 && RealmT < .5f);
            lt.LightEnergy = !realmOk ? 0f : e >= 0 ? e * Mathf.Clamp(Night * 1.6f, 0, 1) : -e;
        }
    }

    // =================================================================== realm
    public static void TryShiftRealm()
    {
        if (Dungeon.Active || Arenas.Active) { Ui.Note("The Rift is sealed here", new Color(0.8f, 0.7f, 1f)); return; }
        if (Shifting) return;
        if (ShiftCd > 0) { Ui.Note("The rift is recovering (" + Mathf.CeilToInt(ShiftCd) + "s)", new Color(0.8f, 0.7f, 1f)); return; }
        if (Net.Active) { Net.I!.RequestShift(); return; }   // in multiplayer the whole world shifts together
        ApplyShift(Realm ^ 1);
    }
    /// <summary>Shift to realm r with the flash, banner and quest hook.</summary>
    public static void ApplyShift(int r)
    {
        if (r == Realm) return;
        Realm = r; ShiftCd = 5f; Sfx.Play("shift"); Ui.Flash(Realm == 1 ? new Color(.6f, .3f, 1f) : Colors.White);
        Ui.Banner(Realm == 1 ? "The Shadow" : "The Light", Realm == 1 ? "the world turns violet" : "you return to the living world");
        Quests.OnRealmShift(Realm);
    }
    public static void ForceRealm(int r) { Realm = r; RealmT = r; I?.UpdateVisuals(0, true); Sfx.I?.SetRealmMix(r); }

    /// <summary>Overworld enemies in build order: the same list on every machine, so a client's puppet and the host's enemy share an index.</summary>
    public static List<Enemy> NetEnemies = new();
    void IndexEnemies()
    {
        NetEnemies = new List<Enemy>();
        foreach (var n in Over.FindChildren("*", "", true, false)) if (n is Enemy e) { e.NetIdx = NetEnemies.Count; e.Puppet = Net.IsClient; NetEnemies.Add(e); }
        GD.Print($"[WORLD] {NetEnemies.Count} overworld enemies, fingerprint {Fingerprint()}");
    }
    /// <summary>Type and rounded position of every overworld enemy, hashed: host and friends must match or their enemies would not line up.</summary>
    public static int Fingerprint()
    {
        // (string.GetHashCode differs between runs in .NET, so hash the characters ourselves)
        unchecked { int h = 17; foreach (var e in NetEnemies) { var p = e.Position; foreach (char c in e.Type) h = h * 31 + c; h = h * 31 + Mathf.RoundToInt(p.X) * 7 + Mathf.RoundToInt(p.Z); } return h; }
    }

    /// <summary>Overworld on/off, used when entering dungeons. A host keeps it running (hidden) so friends outside still have a living world.</summary>
    public void SetOverworldActive(bool on)
    {
        Over.Visible = on; Over.ProcessMode = on || Net.IsHost ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
        Sun.Visible = on;
    }
    public void SetDungeonLook(bool on, Color? ambient = null)
    {
        if (on) { _env.BackgroundMode = Godot.Environment.BGMode.Color; _env.BackgroundColor = new Color(.02f, .015f, .03f); _env.AmbientLightColor = ambient ?? new Color(.25f, .25f, .35f); _env.AmbientLightEnergy = LF("dungeonAmbient", 1.3f); _env.FogDensity = 0.01f; _env.FogLightColor = new Color(.04f, .035f, .06f); _suspend = true; }
        else { _env.BackgroundMode = Godot.Environment.BGMode.Sky; _suspend = false; }
    }
    bool _suspend;
}
