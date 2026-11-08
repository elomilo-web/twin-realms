using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace TwinRealms;

public class Poi { public string Id = "", Name = "", Icon = ""; public float X, Z, R; public int Lvl; public string? Dun; public Vector3 Door; public float DoorYaw; }
public class Zone { public string Id = ""; public float X, Z, R, B, H; }

/// <summary>The overworld: heightfield terrain, sky and day/night, the Light/Shadow realm shift, vegetation and shared materials.</summary>
public partial class World : Node3D
{
    public const int N = 256; public const float Half = 256f, Cell = 2f;
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
    ProceduralSkyMaterial _sky = null!;
    Godot.Environment _env = null!;
    ShaderMaterial _terrMat = null!;
    Image? _mapImg;
    public static ImageTexture? MapTex;

    // ---- realm-aware materials and lights
    static readonly List<(StandardMaterial3D m, Color l, Color d)> Mats = new();
    static readonly List<(OmniLight3D lt, float e, int realm)> Lamps = new();
    public static StandardMaterial3D Mat(Color light, Color? dark = null, float rough = 0.9f, float metal = 0f)
    {
        var m = new StandardMaterial3D { AlbedoColor = light, Roughness = rough, Metallic = metal };
        var d = dark ?? new Color(light.R * 0.35f + 0.04f, light.G * 0.28f + 0.02f, light.B * 0.5f + 0.1f);
        Mats.Add((m, light, d)); return m;
    }
    public static StandardMaterial3D WindowMat = new() { AlbedoColor = new Color(0.2f, 0.22f, 0.26f), EmissionEnabled = true, Emission = new Color(1, .75f, .4f), EmissionEnergyMultiplier = 0f };
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
        foreach (var zn in Zones) { float d = Mathf.Sqrt((x - zn.X) * (x - zn.X) + (z - zn.Z) * (z - zn.Z)); if (d < zn.R + zn.B) h = Mathf.Lerp(h, zn.H, 1 - Smooth(zn.R, zn.R + zn.B, d)); }
        return h;
    }
    public static float GroundAt(float x, float z)
    {
        float fx = (x + Half) / Cell, fz = (z + Half) / Cell;
        if (fx < 0 || fz < 0 || fx >= N || fz >= N) return -9f;
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
    }

    // =================================================================== build
    public override void _Ready()
    {
        I = this; LoadData();
        Over = new Node3D { Name = "Overworld" }; AddChild(Over);
        BuildEnvironment(); BuildTerrain(); BuildWater(); BuildBounds(); BuildVegetation(); Flora.Build(Over);
        Builder.BuildAll(Over);
        Sfx.I?.SetRealmMix(0);
        TimeOfDay = Game.D.Time;
        UpdateVisuals(0, true);
    }

    void BuildEnvironment()
    {
        _sky = new ProceduralSkyMaterial();
        _env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky, Sky = new Sky { SkyMaterial = _sky },
            AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.55f, 0.6f, 0.7f), AmbientLightEnergy = 0.8f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic, FogEnabled = true, FogDensity = 0.0035f,
            GlowEnabled = true, GlowIntensity = 0.5f, GlowBloom = 0.05f,
        };
        Env = new WorldEnvironment { Environment = _env }; AddChild(Env);
        Sun = new DirectionalLight3D { ShadowEnabled = true, DirectionalShadowMaxDistance = 140f, LightEnergy = 1.2f };
        AddChild(Sun);
    }

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
            float ny = nrm.Y, rd = RoadDist(x, z), n1 = Fbm(x * .05f, z * .05f, 3), n2 = VNoise(x * .45f, z * .45f);
            float r = Mathf.Lerp(.27f, .37f, n1) + (n2 - .5f) * .05f, g = Mathf.Lerp(.47f, .56f, n1) + (n2 - .5f) * .06f, b = Mathf.Lerp(.16f, .21f, n1);
            float t = Smooth(8, 26, h) * .5f; r = Mathf.Lerp(r, .4f, t); g = Mathf.Lerp(g, .46f, t); b = Mathf.Lerp(b, .22f, t);
            t = 1 - Smooth(.5f, 1.9f, h); r = Mathf.Lerp(r, .79f, t); g = Mathf.Lerp(g, .72f, t); b = Mathf.Lerp(b, .52f, t);
            t = Mathf.Max(Smooth(.84f, .64f, ny), Smooth(34, 46, h) * .8f); float rv = .46f + (n2 - .5f) * .1f; r = Mathf.Lerp(r, rv, t); g = Mathf.Lerp(g, rv * .97f, t); b = Mathf.Lerp(b, rv * .94f, t);
            t = Smooth(52, 60, h) * Smooth(.5f, .78f, ny); r = Mathf.Lerp(r, .93f, t); g = Mathf.Lerp(g, .95f, t); b = Mathf.Lerp(b, .98f, t);
            t = 1 - Smooth(1.4f, 3.4f, rd); r = Mathf.Lerp(r, .47f, t * .9f); g = Mathf.Lerp(g, .38f, t * .9f); b = Mathf.Lerp(b, .25f, t * .9f);
            cols[i] = new Color(r, g, b);
        }
        var idx = new int[N * N * 6]; int k = 0;
        for (int iz = 0; iz < N; iz++) for (int ix = 0; ix < N; ix++)
        {
            int i0 = iz * (N + 1) + ix, i1 = i0 + 1, i2 = i0 + N + 1, i3 = i2 + 1;
            idx[k++] = i0; idx[k++] = i1; idx[k++] = i2; idx[k++] = i1; idx[k++] = i3; idx[k++] = i2;
        }
        var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts; arrays[(int)Mesh.ArrayType.Normal] = norms; arrays[(int)Mesh.ArrayType.Color] = cols;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs; arrays[(int)Mesh.ArrayType.Index] = idx;
        var mesh = new ArrayMesh(); mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        var sh = new Shader { Code = TerrainShader };
        _terrMat = new ShaderMaterial { Shader = sh }; _terrMat.SetShaderParameter("realm_t", 0f);
        var body = new StaticBody3D { Name = "Terrain", CollisionLayer = 1 };
        body.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = _terrMat });
        body.AddChild(new CollisionShape3D { Shape = mesh.CreateTrimeshShape() });
        Over.AddChild(body);
        BuildMapImage();
    }

    const string TerrainShader = @"
shader_type spatial;
uniform float realm_t = 0.0;
varying vec3 wpos;
float h21(vec2 p){ return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float vn(vec2 p){ vec2 i = floor(p); vec2 f = fract(p); f = f*f*(3.0-2.0*f); return mix(mix(h21(i), h21(i+vec2(1.0,0.0)), f.x), mix(h21(i+vec2(0.0,1.0)), h21(i+vec2(1.0,1.0)), f.x), f.y); }
float fb(vec2 p){ return vn(p)*0.5 + vn(p*2.07)*0.25 + vn(p*4.13)*0.125 + vn(p*8.3)*0.0625 + 0.03; }
void vertex(){ wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
void fragment(){
  vec3 c = COLOR.rgb;
  float macro = fb(wpos.xz * 0.012);
  float meso = fb(wpos.xz * 0.09 + 17.0);
  float d = length(wpos - CAMERA_POSITION_WORLD);
  float fd = 1.0 - smoothstep(30.0, 110.0, d);
  float micro = mix(0.5, vn(wpos.xz * 2.7) * 0.5 + vn(wpos.xz * 8.9) * 0.5, fd);
  c *= (0.74 + 0.56 * macro) * (0.88 + 0.24 * meso) * (0.86 + 0.28 * micro);
  float gr = clamp((c.g - max(c.r, c.b)) * 5.0, 0.0, 1.0);
  c = mix(c, c * vec3(1.14, 1.02, 0.72), gr * smoothstep(0.5, 0.8, macro) * 0.65);
  float l = dot(c, vec3(0.33));
  vec3 sh = vec3(l * 0.5, l * 0.38, l * 0.85) + vec3(0.03, 0.01, 0.06);
  float vein = 1.0 - smoothstep(0.0, 0.03, abs(fb(wpos.xz * 0.028 + vec2(5.0, -9.0)) - 0.5));
  sh = mix(sh, vec3(0.78, 0.22, 0.6), vein * 0.5 * step(2.0, wpos.y));
  ALBEDO = mix(c, sh, realm_t);
  ROUGHNESS = 0.95;
}";

    void BuildMapImage()
    {
        const int S = 256; _mapImg = Image.CreateEmpty(S, S, false, Image.Format.Rgb8);
        for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
        {
            float h = HG[y * (N + 1) + x]; Color c;
            if (h < 0.4f) c = new Color(.12f, .25f, .45f); else if (h < 2f) c = new Color(.75f, .68f, .5f);
            else if (h < 18f) c = new Color(.3f, .5f, .22f).Lerp(new Color(.4f, .46f, .24f), h / 18f); else if (h < 45f) c = new Color(.5f, .48f, .45f); else c = new Color(.92f, .94f, .97f);
            _mapImg.SetPixel(x, y, c);
        }
        MapTex = ImageTexture.CreateFromImage(_mapImg);
    }

    void BuildWater()
    {
        var wm = Mat(new Color(0.1f, 0.3f, 0.5f, 0.85f), new Color(0.05f, 0.03f, 0.12f, 0.9f), 0.1f, 0.3f);
        wm.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        Over.AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(3000, 3000) }, MaterialOverride = wm, Position = new Vector3(0, 0.25f, 0) });
    }

    void BuildBounds()
    {
        var b = new StaticBody3D { Name = "Bounds", CollisionLayer = 1 };
        float e = Half - 6f, t = 4f, h = 200f;
        void Wall(Vector3 pos, Vector3 size) { b.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = pos }); }
        Wall(new Vector3(0, 0, -e), new Vector3(2 * e, h, t)); Wall(new Vector3(0, 0, e), new Vector3(2 * e, h, t));
        Wall(new Vector3(-e, 0, 0), new Vector3(t, h, 2 * e)); Wall(new Vector3(e, 0, 0), new Vector3(t, h, 2 * e));
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
        var pineFol = Merge(
            (new CylinderMesh { TopRadius = 0f, BottomRadius = 2.1f, Height = 3.4f, RadialSegments = 8, Rings = 1 }, T(new Vector3(0, 3.2f, 0))),
            (new CylinderMesh { TopRadius = 0f, BottomRadius = 1.6f, Height = 3f, RadialSegments = 8, Rings = 1 }, T(new Vector3(0, 5f, 0))),
            (new CylinderMesh { TopRadius = 0f, BottomRadius = 1.05f, Height = 2.6f, RadialSegments = 8, Rings = 1 }, T(new Vector3(0, 6.7f, 0))));
        var oakFol = Merge(
            (new SphereMesh { Radius = 1.6f, Height = 3.2f, RadialSegments = 8, Rings = 5 }, T(new Vector3(0, 4.3f, 0), new Vector3(1.2f, 1.0f, 1.2f))),
            (new SphereMesh { Radius = 1.1f, Height = 2.2f, RadialSegments = 8, Rings = 4 }, T(new Vector3(1.3f, 3.6f, 0.5f))),
            (new SphereMesh { Radius = 1.1f, Height = 2.2f, RadialSegments = 8, Rings = 4 }, T(new Vector3(-1.1f, 3.9f, -0.7f))));
        var trunkP = new CylinderMesh { TopRadius = 0.18f, BottomRadius = 0.3f, Height = 2.6f, RadialSegments = 6 };
        var trunkO = new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.45f, Height = 3.2f, RadialSegments = 6 };
        var pines = new List<Transform3D>(); var oaks = new List<Transform3D>(); var rocks = new List<Transform3D>(); var flowers = new List<(Transform3D, Color)>();
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
        }
        for (int i = 0; i < 700; i++)
        {
            float x = R(-110, 110), z = R(-110, 110), h = GroundAt(x, z); if (h < 1.5f || h > 14f || RoadDist(x, z) < 3f) continue;
            var c = new[] { new Color(1, .45f, .55f), new Color(1, .9f, .3f), Colors.White, new Color(.7f, .5f, 1f), new Color(1, .6f, .2f) }[(int)(rg() * 5) % 5];
            flowers.Add((T(new Vector3(x, h + .12f, z), new Vector3(.12f, .12f, .12f)), c));
        }
        Multi(Merge((trunkP, T(new Vector3(0, 1.3f, 0)))), Mat(new Color(.29f, .2f, .13f), new Color(.1f, .08f, .13f)), pines);
        Multi(pineFol, Mat(new Color(.17f, .38f, .18f), new Color(.1f, .12f, .2f)), pines);
        Multi(Merge((trunkO, T(new Vector3(0, 1.6f, 0)))), Mat(new Color(.35f, .24f, .14f), new Color(.1f, .08f, .13f)), oaks);
        Multi(oakFol, Mat(new Color(.3f, .54f, .21f), new Color(.2f, .12f, .3f)), oaks);
        Multi(new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 6, Rings = 4 }, Mat(new Color(.55f, .54f, .52f), new Color(.2f, .17f, .26f), 1f), rocks);
        // flowers with per-instance colour
        var fm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 5, Rings = 3 }, InstanceCount = flowers.Count };
        for (int i = 0; i < flowers.Count; i++) { fm.SetInstanceTransform(i, flowers[i].Item1); fm.SetInstanceColor(i, flowers[i].Item2); }
        Over.AddChild(new MultiMeshInstance3D { Multimesh = fm, MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true } });
    }

    void Multi(Mesh mesh, Material mat, List<Transform3D> list)
    {
        if (list.Count == 0) return;
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = list.Count };
        for (int i = 0; i < list.Count; i++) mm.SetInstanceTransform(i, list[i]);
        Over.AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = mat });
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
        float day = Mathf.Clamp(elev * 2.5f + 0.2f, 0f, 1f), dusk = Mathf.Clamp(1f - Mathf.Abs(elev) * 5f, 0f, 1f);
        Night = (1 - day) * (1 - RealmT) + RealmT * 0.7f;
        Sun.RotationDegrees = new Vector3(-Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(Mathf.Abs(elev) * 0.9f + 0.12f, 0.1f, 1f))), Mathf.RadToDeg(ang) + 90f, 0);
        var dayCol = new Color(1f, .95f, .85f).Lerp(new Color(1f, .6f, .35f), dusk); var moon = new Color(.55f, .62f, 1f); var shadowCol = new Color(.6f, .35f, .9f);
        Sun.LightColor = dayCol.Lerp(moon, 1 - day).Lerp(shadowCol, RealmT);
        Sun.LightEnergy = Mathf.Lerp(0.18f, 1.25f, day) * (1 - RealmT * 0.5f) + (elev < 0 ? 0.12f : 0f);
        var top = new Color(.25f, .52f, .85f).Lerp(new Color(.02f, .03f, .1f), 1 - day).Lerp(new Color(.07f, .02f, .15f), RealmT);
        var hor = new Color(.74f, .85f, .95f).Lerp(new Color(1f, .6f, .4f), dusk * day).Lerp(new Color(.03f, .04f, .12f), 1 - day).Lerp(new Color(.25f, .08f, .3f), RealmT);
        _sky.SkyTopColor = top; _sky.SkyHorizonColor = hor; _sky.GroundHorizonColor = hor; _sky.GroundBottomColor = hor.Darkened(0.5f);
        _env.FogLightColor = hor; _env.FogDensity = Mathf.Lerp(0.0035f, 0.0065f, RealmT);
        _env.AmbientLightColor = new Color(.6f, .66f, .78f).Lerp(new Color(.14f, .16f, .3f), 1 - day).Lerp(new Color(.32f, .18f, .5f), RealmT);
        _env.AmbientLightEnergy = Mathf.Lerp(0.35f, 0.9f, day) * Mathf.Lerp(1f, 0.8f, RealmT);
        if (force || Mathf.Abs(RealmT - _lastRealmT) > 0.001f)
        {
            _lastRealmT = RealmT; _terrMat.SetShaderParameter("realm_t", RealmT);
            foreach (var fm in Flora.Materials) fm.SetShaderParameter("realm_t", RealmT);
            foreach (var (m, l, d) in Mats) m.AlbedoColor = l.Lerp(d, RealmT);
        }
        if (force || Mathf.Abs(Night - _lastNight) > 0.002f)
        {
            _lastNight = Night;
            WindowMat.EmissionEnergyMultiplier = Night * 2.2f; WindowMat.Emission = new Color(1, .75f, .4f).Lerp(new Color(.7f, .45f, 1f), RealmT);
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
        Realm ^= 1; ShiftCd = 5f; Sfx.Play("shift"); Ui.Flash(Realm == 1 ? new Color(.6f, .3f, 1f) : Colors.White);
        Ui.Banner(Realm == 1 ? "The Shadow" : "The Light", Realm == 1 ? "the world turns violet" : "you return to the living world");
        Quests.OnRealmShift(Realm);
    }
    public static void ForceRealm(int r) { Realm = r; RealmT = r; I?.UpdateVisuals(0, true); Sfx.I?.SetRealmMix(r); }

    /// <summary>Overworld on/off, used when entering dungeons.</summary>
    public void SetOverworldActive(bool on)
    {
        Over.Visible = on; Over.ProcessMode = on ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
        Sun.Visible = on;
    }
    public void SetDungeonLook(bool on, Color? ambient = null)
    {
        if (on) { _env.BackgroundMode = Godot.Environment.BGMode.Color; _env.BackgroundColor = new Color(.02f, .015f, .03f); _env.AmbientLightColor = ambient ?? new Color(.25f, .25f, .35f); _env.AmbientLightEnergy = .7f; _env.FogDensity = 0.012f; _env.FogLightColor = new Color(.04f, .035f, .06f); _suspend = true; }
        else { _env.BackgroundMode = Godot.Environment.BGMode.Sky; _suspend = false; }
    }
    bool _suspend;
}
