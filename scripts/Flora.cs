using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>Scenery ground cover: wind-swept grass, ferns, bushes and pebbles. Scattered per chunk from data/flora.json,
/// drawn as MultiMeshes that only render near the camera (visibility range), no collisions.</summary>
public static class Flora
{
    const string GrassShader = @"
shader_type spatial;
render_mode cull_disabled;
uniform float realm_t = 0.0;
uniform float wind = 1.0;
uniform float sway = 0.22;
varying float h;
void vertex(){
  h = UV.y;
  vec3 wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
  float t = TIME * 1.7;
  float gust = sin(t * 0.37 + wp.x * 0.045 + wp.z * 0.03) * 0.5 + 0.5;
  float s = sin(t + wp.x * 0.35 + wp.z * 0.22) * 0.6 + sin(t * 2.3 + wp.x * 1.1 + wp.z * 0.7) * 0.25;
  vec3 off = vec3(s * 0.8 + 0.35, 0.0, s * 0.45) * h * h * sway * wind * (0.5 + gust);
  VERTEX += inverse(mat3(MODEL_MATRIX)) * off;
  NORMAL = normalize(inverse(mat3(MODEL_MATRIX)) * vec3(0.0, 1.0, 0.0));
}
void fragment(){
  vec3 base = COLOR.rgb * mix(0.45, 1.0, h);   // dark roots, tips no brighter than the colour (brighter tips burned out to pale yellow in sunlight)
  float l = dot(base, vec3(0.33));
  vec3 sh = vec3(l * 0.45, l * 0.32, l * 0.9) + vec3(0.02, 0.0, 0.06);
  ALBEDO = mix(base, sh, realm_t);
  EMISSION = realm_t * vec3(0.25, 0.75, 0.85) * h * h * 0.35;
  ROUGHNESS = 0.95; SPECULAR = 0.05;   // matte blades (the default highlight made the tips look pale and glossy)
  BACKLIGHT = vec3(0.1, 0.16, 0.04) * h * (1.0 - realm_t);
}";

    // Grass blades: curved, tapered blades whose roots take the colour of the ground texture under them (so a meadow reads as one
    // surface from afar), darker at the base, lit through from behind, with travelling wind gusts. Far blades shrink into the ground
    // instead of popping out.
    const string BladeShader = @"
shader_type spatial;
render_mode cull_disabled;
uniform float realm_t = 0.0;
uniform float wind = 1.0;
uniform float sway = 0.3;
uniform float range = 100.0;
uniform sampler2D ground_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform float ground_tile = 3.5;
uniform vec3 ground_tint : source_color = vec3(1.0);
uniform float root_blend = 0.75;
varying float h;
varying vec3 root_col;
float h21(vec2 p){ return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float vn(vec2 p){ vec2 i = floor(p); vec2 f = fract(p); f = f*f*(3.0-2.0*f); return mix(mix(h21(i), h21(i+vec2(1.0,0.0)), f.x), mix(h21(i+vec2(0.0,1.0)), h21(i+vec2(1.0,1.0)), f.x), f.y); }
void vertex(){
  h = UV.y;
  vec3 origin = MODEL_MATRIX[3].xyz;
  float d = distance(origin, CAMERA_POSITION_WORLD);
  float keep = 1.0 - smoothstep(range * 0.55, range, d);
  VERTEX.y *= keep;
  vec3 wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
  float t = TIME;
  // big slow gusts rolling across the field + small fast flutter per blade
  float gust = vn(wp.xz * 0.05 + vec2(t * 0.35, t * 0.12));
  float flutter = sin(t * 3.1 + wp.x * 1.7 + wp.z * 1.3 + UV.x * 6.0) * 0.18;
  vec2 dir = normalize(vec2(1.0, 0.45));
  float bend = (0.25 + gust * 1.1 + flutter) * sway * wind;
  vec3 off = vec3(dir.x, 0.0, dir.y) * bend * h * h;
  off.y = -bend * bend * h * h * 0.35;
  VERTEX += inverse(mat3(MODEL_MATRIX)) * off;
  // soft normals: mostly up, a little of the blade's own facing (no harsh per-blade facets)
  vec3 up = normalize(inverse(mat3(MODEL_MATRIX)) * vec3(0.0, 1.0, 0.0));
  NORMAL = normalize(mix(NORMAL, up, 0.7));
  root_col = textureLod(ground_tex, origin.xz / ground_tile, 7.0).rgb * ground_tint;
}
void fragment(){
  vec3 tip = COLOR.rgb;
  vec3 base = mix(root_col, tip, smoothstep(0.0, 1.0, h) * (1.0 - root_blend * (1.0 - h)));
  base *= mix(0.62, 1.0, smoothstep(0.0, 0.6, h));                       // self-shadow deep in the clump
  float l = dot(base, vec3(0.33));
  vec3 sh = vec3(l * 0.45, l * 0.32, l * 0.9) + vec3(0.02, 0.0, 0.06);
  ALBEDO = mix(base, sh, realm_t);
  EMISSION = realm_t * vec3(0.25, 0.75, 0.85) * h * h * 0.35;
  ROUGHNESS = 0.85; SPECULAR = 0.12;
  BACKLIGHT = base * 0.55 * h * (1.0 - realm_t);                          // sun shining through the blades
  AO = mix(0.5, 1.0, h); AO_LIGHT_AFFECT = 0.4;
}";

    public static readonly List<ShaderMaterial> Materials = new();

    static ShaderMaterial BladeMat(JsonElement e)
    {
        var m = new ShaderMaterial { Shader = new Shader { Code = BladeShader } };
        m.SetShaderParameter("sway", JsonUtil.F(e, "sway", .3f)); m.SetShaderParameter("wind", JsonUtil.F(e, "wind", 1f)); m.SetShaderParameter("range", JsonUtil.F(e, "range", 100f));
        const string tex = "res://assets/textures/terrain/grass_col.jpg";
        if (ResourceLoader.Exists(tex)) m.SetShaderParameter("ground_tex", GD.Load<Texture2D>(tex));
        // match the terrain: same tile size and tint as data/look.json terrain.grass_*
        using (var ld = JsonUtil.Load("res://data/look.json"))
            if (ld.RootElement.TryGetProperty("terrain", out var tc))
            {
                m.SetShaderParameter("ground_tile", JsonUtil.F(tc, "grass_tile", 3.5f));
                string tint = JsonUtil.S(tc, "grass_tint"); if (tint != "") m.SetShaderParameter("ground_tint", Color.FromHtml(tint));
            }
        Materials.Add(m); return m;
    }

    /// <summary>A clump of curved, tapered blades (segments bend more toward the tip). UV.y = 0 root .. 1 tip, UV.x = a per-blade random.</summary>
    static ArrayMesh Blades(int blades, float h0, float h1, float radius, System.Random r)
    {
        var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
        const int Seg = 5;
        float F() => (float)r.NextDouble();
        for (int i = 0; i < blades; i++)
        {
            float a = F() * Mathf.Tau, rr = Mathf.Sqrt(F()) * radius, face = F() * Mathf.Tau, id = F();
            float ht = Mathf.Lerp(h0, h1, F() * F() * .6f + F() * .4f), w = .012f + F() * .014f, lean = .15f + F() * .45f;
            var root = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * rr;
            var dir = new Vector3(Mathf.Cos(face), 0, Mathf.Sin(face)); var side = new Vector3(-dir.Z, 0, dir.X);
            Vector3 P(float t) => root + dir * lean * ht * t * t + Vector3.Up * ht * (t - lean * lean * t * t * .3f);
            float W(float t) => w * Mathf.Pow(1 - t, .7f);
            for (int s = 0; s < Seg; s++)
            {
                float t0 = s / (float)Seg, t1 = (s + 1) / (float)Seg;
                Vector3 a0 = P(t0) - side * W(t0), b0 = P(t0) + side * W(t0), a1 = P(t1) - side * W(t1), b1 = P(t1) + side * W(t1);
                void V(Vector3 v, float t) { st.SetUV(new Vector2(id, t)); st.AddVertex(v); }
                if (s == Seg - 1) { V(a0, t0); V(b0, t0); V(P(1), 1); }
                else { V(a0, t0); V(b0, t0); V(a1, t1); V(b0, t0); V(b1, t1); V(a1, t1); }
            }
        }
        st.GenerateNormals(); return st.Commit();
    }

    static ShaderMaterial Mat(float sway, float wind)
    {
        var m = new ShaderMaterial { Shader = new Shader { Code = GrassShader } };
        m.SetShaderParameter("sway", sway); m.SetShaderParameter("wind", wind); Materials.Add(m); return m;
    }

    // ---------------------------------------------------------------- meshes (UV.y = 0 at the root, 1 at the tip, for sway and shading)
    static ArrayMesh GrassClump(int blades, System.Random r)
    {
        var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
        for (int i = 0; i < blades; i++)
        {
            float a = (float)(r.NextDouble() * Mathf.Tau), off = (float)r.NextDouble() * 0.22f, w = 0.022f + (float)r.NextDouble() * 0.02f, ht = 0.7f + (float)r.NextDouble() * 0.5f, lean = 0.12f + (float)r.NextDouble() * 0.25f;
            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)); var side = new Vector3(-dir.Z, 0, dir.X);
            var root = dir * off; var tip = root + dir * lean * ht + Vector3.Up * ht;
            st.SetUV(new Vector2(0, 0)); st.AddVertex(root - side * w);
            st.SetUV(new Vector2(1, 0)); st.AddVertex(root + side * w);
            st.SetUV(new Vector2(.5f, 1)); st.AddVertex(tip);
        }
        st.GenerateNormals(); return st.Commit();
    }
    static ArrayMesh Fern(int leaves, System.Random r)
    {
        var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
        for (int i = 0; i < leaves; i++)
        {
            float a = i / (float)leaves * Mathf.Tau + (float)r.NextDouble() * 0.4f; var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)); var side = new Vector3(-dir.Z, 0, dir.X);
            float len = 0.8f + (float)r.NextDouble() * 0.3f;
            var p0 = Vector3.Zero; var p1 = dir * len * 0.45f + Vector3.Up * 0.45f; var p2 = dir * len + Vector3.Up * 0.2f;
            // a frond: two triangles that widen in the middle and arch down at the tip
            st.SetUV(new Vector2(0, 0)); st.AddVertex(p0); st.SetUV(new Vector2(0, .6f)); st.AddVertex(p1 - side * 0.13f); st.SetUV(new Vector2(0, 1)); st.AddVertex(p2);
            st.SetUV(new Vector2(0, 0)); st.AddVertex(p0); st.SetUV(new Vector2(0, 1)); st.AddVertex(p2); st.SetUV(new Vector2(0, .6f)); st.AddVertex(p1 + side * 0.13f);
        }
        st.GenerateNormals(); return st.Commit();
    }
    static ArrayMesh Bush()
    {
        var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
        void Ball(Vector3 c, float rad) { var s = new SphereMesh { Radius = rad, Height = rad * 1.6f, RadialSegments = 7, Rings = 4 }; st.AppendFrom(s, 0, new Transform3D(Basis.Identity, c)); }
        Ball(new Vector3(0, .45f, 0), .6f); Ball(new Vector3(.45f, .35f, .15f), .42f); Ball(new Vector3(-.4f, .33f, -.1f), .45f); Ball(new Vector3(.05f, .7f, -.2f), .38f);
        return st.Commit();
    }

    // ---------------------------------------------------------------- build
    public class Layer { public string Key = ""; public int Per; public float Range, MinH, MaxH, S0 = 1, S1 = 1; public Color[] Cols = { Colors.Green }; public bool ForestOnly, Shadows, Flat, Grass; public JsonElement E; public Mesh Mesh = null!; public Material Mat = null!; }
    public static readonly List<Layer> Layers = new();
    public static float Chunk = 32f; static int _seed = 4242;
    /// <summary>The grass clump and its material (sky islands scatter the same grass).</summary>
    public static Mesh? GrassMesh; public static ShaderMaterial? GrassMat;

    /// <summary>Loads the layers and adds the streamer: ground cover is built in 32 m chunks around the camera and dropped behind it,
    /// so it covers every land without holding the whole world in memory.</summary>
    public static void Build(Node3D parent)
    {
        Layers.Clear();
        using var doc = JsonUtil.Load("res://data/flora.json"); var root = doc.RootElement;
        Chunk = JsonUtil.F(root, "chunk", 32); _seed = (int)JsonUtil.F(root, "seed", 4242); var rnd = new System.Random(_seed);
        Layer L(string k, string sizeKey)
        {
            var e = root.GetProperty(k); var l = new Layer { Key = k, Per = (int)JsonUtil.F(e, "per"), Range = JsonUtil.F(e, "range", 60), MinH = JsonUtil.F(e, "minH", 1.5f), MaxH = JsonUtil.F(e, "maxH", 30), ForestOnly = e.TryGetProperty("forestOnly", out var fo) && fo.GetBoolean(), E = e.Clone() };
            if (e.TryGetProperty(sizeKey, out var sz)) { l.S0 = sz[0].GetSingle(); l.S1 = sz[1].GetSingle(); }
            var cl = new List<Color>(); if (e.TryGetProperty("colors", out var cs)) foreach (var c in cs.EnumerateArray()) cl.Add(Color.FromHtml(c.GetString()!)); if (cl.Count > 0) l.Cols = cl.ToArray();
            return l;
        }
        var grass = L("grass", "height");
        grass.Mesh = Blades((int)JsonUtil.F(grass.E, "blades", 24), grass.S0, grass.S1, JsonUtil.F(grass.E, "radius", .3f), rnd); grass.S0 = grass.S1 = 1f;   // heights are built into the blades
        grass.Mat = GrassMat = BladeMat(grass.E); GrassMesh = grass.Mesh; grass.Grass = true; Layers.Add(grass);
        if (root.TryGetProperty("grassTall", out _))
        {
            var tall = L("grassTall", "height"); tall.Mesh = Blades((int)JsonUtil.F(tall.E, "blades", 14), tall.S0, tall.S1, JsonUtil.F(tall.E, "radius", .22f), rnd); tall.S0 = tall.S1 = 1f;
            tall.Mat = BladeMat(tall.E); tall.Grass = true; Layers.Add(tall);
        }
        var fern = L("fern", "size"); fern.Mesh = Fern((int)JsonUtil.F(fern.E, "leaves", 7), rnd); fern.Mat = Mat(0.12f, 1f); if (fern.Per > 0) Layers.Add(fern);
        var bush = L("bush", "size"); bush.Mesh = Bush(); bush.Mat = Mat(0.04f, 1f); bush.Shadows = true; if (bush.Per > 0) Layers.Add(bush);
        var stone = L("stone", "size"); stone.Mesh = new SphereMesh { Radius = 1, Height = 1.3f, RadialSegments = 6, Rings = 3 }; stone.Flat = true;
        var sMat = World.Mat(new Color(.55f, .54f, .52f), new Color(.2f, .17f, .26f), 1f); sMat.VertexColorUseAsAlbedo = true; stone.Mat = sMat; if (stone.Per > 0) Layers.Add(stone);
        parent.AddChild(new FloraField { Name = "Flora" });
    }

    /// <summary>One chunk of ground cover: every layer scattered with a seed of its own (the same on every machine and every visit).</summary>
    public static Node3D BuildChunk(int cx, int cz)
    {
        var holder = new Node3D { Name = $"c{cx}_{cz}" };
        float x0 = cx * Chunk, z0 = cz * Chunk;
        if (World.GroundAt(x0 + Chunk / 2, z0 + Chunk / 2) < -2f && World.GroundAt(x0, z0) < -2f && World.GroundAt(x0 + Chunk, z0 + Chunk) < -2f) return holder;   // open sea
        var r = new System.Random(unchecked(_seed * 73856093 ^ cx * 19349663 ^ cz * 83492791));
        foreach (var l in Layers) Scatter(holder, l, x0, z0, Chunk, r);
        return holder;
    }

    static void Scatter(Node3D holder, Layer l, float x0, float z0, float chunk, System.Random r)
    {
        var xs = new List<Transform3D>(); var cs = new List<Color>(); float F() => (float)r.NextDouble();
        bool home = Mathf.Abs(x0) < World.Half && Mathf.Abs(z0) < World.Half;
        for (int i = 0; i < l.Per; i++)
        {
            float x = x0 + F() * chunk, z = z0 + F() * chunk, keep = F(), h = World.GroundAt(x, z);
            if (h < l.MinH || h > l.MaxH) continue;
            if (Mathf.Abs(World.GroundAt(x + 1, z) - World.GroundAt(x - 1, z)) > 1.3f || Mathf.Abs(World.GroundAt(x, z + 1) - World.GroundAt(x, z - 1)) > 1.3f) continue; // cliffs
            var tint = Colors.White;
            if (home) { if (World.RoadDist(x, z) < 2.6f || InPlaza(x, z)) continue; }
            else { float dens = Lands.CoverAt(x, z, h, l.Grass, out tint); if (keep > dens) continue; }
            if (l.ForestOnly && World.Fbm(x * .02f + 3, z * .02f + 9, 3) < .42f) continue;          // same noise that places the trees
            float s = Mathf.Lerp(l.S0, l.S1, F());
            var sc = l.Flat ? new Vector3(s * (0.8f + F() * 0.6f), s * 0.6f, s * (0.8f + F() * 0.6f)) : new Vector3(s, s * (0.85f + F() * 0.3f), s);
            xs.Add(new Transform3D(Basis.FromEuler(new Vector3(0, F() * Mathf.Tau, 0)).Scaled(sc), new Vector3(x, h - 0.03f, z)));
            var c = l.Cols[r.Next(l.Cols.Length)]; float v = 0.9f + F() * 0.2f; cs.Add(new Color(c.R * v * tint.R, c.G * v * tint.G, c.B * v * tint.B));
        }
        if (xs.Count == 0) return;
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = l.Mesh, InstanceCount = xs.Count };
        for (int i = 0; i < xs.Count; i++) { mm.SetInstanceTransform(i, xs[i]); mm.SetInstanceColor(i, cs[i]); }
        holder.AddChild(new MultiMeshInstance3D
        {
            Multimesh = mm, MaterialOverride = l.Mat, CastShadow = l.Shadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityRangeEnd = l.Range, VisibilityRangeEndMargin = l.Range * 0.15f, VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
        });
    }
    /// <summary>Keep the village plaza and the arena floor clear.</summary>
    static bool InPlaza(float x, float z)
    {
        foreach (var p in World.Pois) if ((p.Id == "village" || p.Id == "arena") && new Vector2(x - p.X, z - p.Z).Length() < p.R * 0.45f) return true;
        return false;
    }
}

/// <summary>Streams the ground cover: keeps the chunks within reach of the camera built, nearest first, and frees the ones left behind.</summary>
public partial class FloraField : Node3D
{
    readonly Dictionary<(int, int), Node3D> _chunks = new();
    readonly List<(int, int)> _want = new();
    float _reach;
    public override void _Ready() { foreach (var l in Flora.Layers) _reach = Mathf.Max(_reach, l.Range); _reach += Flora.Chunk * .75f; }

    public override void _Process(double delta)
    {
        if (Dungeon.Active) return;
        var cam = GetViewport()?.GetCamera3D(); if (cam == null) return;
        var p = cam.GlobalPosition; float C = Flora.Chunk;
        int pcx = Mathf.FloorToInt(p.X / C), pcz = Mathf.FloorToInt(p.Z / C), rad = Mathf.CeilToInt(_reach / C);
        _want.Clear();
        for (int dz = -rad; dz <= rad; dz++) for (int dx = -rad; dx <= rad; dx++)
        {
            int cx = pcx + dx, cz = pcz + dz; if (_chunks.ContainsKey((cx, cz))) continue;
            float ddx = (cx + .5f) * C - p.X, ddz = (cz + .5f) * C - p.Z; if (ddx * ddx + ddz * ddz > _reach * _reach) continue;
            _want.Add((cx, cz));
        }
        if (_want.Count > 0)
        {
            _want.Sort((a, b) => D2(a, p, C).CompareTo(D2(b, p, C)));
            // a few per frame; many at once after a teleport (nothing built near the camera yet)
            int budget = _want.Count > 60 || D2(_want[0], p, C) < (C * 1.5f) * (C * 1.5f) ? 24 : 3;
            for (int i = 0; i < _want.Count && i < budget; i++) { var k = _want[i]; var n = Flora.BuildChunk(k.Item1, k.Item2); AddChild(n); _chunks[k] = n; }
        }
        // drop chunks well behind the reach
        float drop = (_reach + C * 1.5f) * (_reach + C * 1.5f);
        List<(int, int)>? gone = null;
        foreach (var kv in _chunks) if (D2(kv.Key, p, C) > drop) (gone ??= new()).Add(kv.Key);
        if (gone != null) foreach (var k in gone) { _chunks[k].QueueFree(); _chunks.Remove(k); }
    }
    static float D2((int, int) k, Vector3 p, float C) { float dx = (k.Item1 + .5f) * C - p.X, dz = (k.Item2 + .5f) * C - p.Z; return dx * dx + dz * dz; }
}