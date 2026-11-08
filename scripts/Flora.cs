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
  vec3 base = COLOR.rgb * mix(0.42, 1.18, h);
  float l = dot(base, vec3(0.33));
  vec3 sh = vec3(l * 0.45, l * 0.32, l * 0.9) + vec3(0.02, 0.0, 0.06);
  ALBEDO = mix(base, sh, realm_t);
  EMISSION = realm_t * vec3(0.25, 0.75, 0.85) * h * h * 0.35;
  ROUGHNESS = 0.9;
  BACKLIGHT = vec3(0.25, 0.35, 0.1) * h * (1.0 - realm_t);
}";

    public static readonly List<ShaderMaterial> Materials = new();

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
            float a = (float)(r.NextDouble() * Mathf.Tau), off = (float)r.NextDouble() * 0.22f, w = 0.035f + (float)r.NextDouble() * 0.03f, ht = 0.7f + (float)r.NextDouble() * 0.5f, lean = 0.12f + (float)r.NextDouble() * 0.25f;
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
    class Layer { public string Key = ""; public int Per; public float Range, MinH, MaxH, S0 = 1, S1 = 1; public Color[] Cols = { Colors.Green }; public bool ForestOnly; public JsonElement E; }

    public static void Build(Node3D parent)
    {
        using var doc = JsonUtil.Load("res://data/flora.json"); var root = doc.RootElement;
        float chunk = JsonUtil.F(root, "chunk", 32); var rnd = new System.Random((int)JsonUtil.F(root, "seed", 4242));
        Layer L(string k, string sizeKey)
        {
            var e = root.GetProperty(k); var l = new Layer { Key = k, Per = (int)JsonUtil.F(e, "per"), Range = JsonUtil.F(e, "range", 60), MinH = JsonUtil.F(e, "minH", 1.5f), MaxH = JsonUtil.F(e, "maxH", 30), ForestOnly = e.TryGetProperty("forestOnly", out var fo) && fo.GetBoolean(), E = e.Clone() };
            if (e.TryGetProperty(sizeKey, out var sz)) { l.S0 = sz[0].GetSingle(); l.S1 = sz[1].GetSingle(); }
            var cl = new List<Color>(); if (e.TryGetProperty("colors", out var cs)) foreach (var c in cs.EnumerateArray()) cl.Add(Color.FromHtml(c.GetString()!)); if (cl.Count > 0) l.Cols = cl.ToArray();
            return l;
        }
        var grass = L("grass", "height"); var fern = L("fern", "size"); var bush = L("bush", "size"); var stone = L("stone", "size");
        var gMat = Mat(0.22f, JsonUtil.F(grass.E, "wind", 1f)); var fMat = Mat(0.12f, 1f); var bMat = Mat(0.04f, 1f);
        var sMat = World.Mat(new Color(.55f, .54f, .52f), new Color(.2f, .17f, .26f), 1f); sMat.VertexColorUseAsAlbedo = true;
        var gMesh = GrassClump((int)JsonUtil.F(grass.E, "blades", 6), rnd); var fMesh = Fern((int)JsonUtil.F(fern.E, "leaves", 7), rnd); var bMesh = Bush();
        var sMesh = new SphereMesh { Radius = 1, Height = 1.3f, RadialSegments = 6, Rings = 3 };

        var holder = new Node3D { Name = "Flora" }; parent.AddChild(holder);
        int n = Mathf.CeilToInt(World.Half * 2 / chunk);
        for (int cz = 0; cz < n; cz++) for (int cx = 0; cx < n; cx++)
        {
            float x0 = -World.Half + cx * chunk, z0 = -World.Half + cz * chunk; var center = new Vector3(x0 + chunk / 2, 0, z0 + chunk / 2);
            if (World.GroundAt(center.X, center.Z) < -2f && World.GroundAt(x0, z0) < -2f) continue; // open sea
            Scatter(holder, grass, gMesh, gMat, x0, z0, chunk, rnd, false);
            Scatter(holder, fern, fMesh, fMat, x0, z0, chunk, rnd, false);
            Scatter(holder, bush, bMesh, bMat, x0, z0, chunk, rnd, true);
            Scatter(holder, stone, sMesh, sMat, x0, z0, chunk, rnd, false, flat: true);
        }
    }

    static void Scatter(Node3D holder, Layer l, Mesh mesh, Material mat, float x0, float z0, float chunk, System.Random r, bool shadows, bool flat = false)
    {
        var xs = new List<Transform3D>(); var cs = new List<Color>(); float F() => (float)r.NextDouble();
        for (int i = 0; i < l.Per; i++)
        {
            float x = x0 + F() * chunk, z = z0 + F() * chunk, h = World.GroundAt(x, z);
            if (h < l.MinH || h > l.MaxH) continue;
            if (Mathf.Abs(World.GroundAt(x + 1, z) - World.GroundAt(x - 1, z)) > 1.3f || Mathf.Abs(World.GroundAt(x, z + 1) - World.GroundAt(x, z - 1)) > 1.3f) continue; // cliffs
            if (World.RoadDist(x, z) < 2.6f || InPlaza(x, z)) continue;
            if (l.ForestOnly && World.Fbm(x * .02f + 3, z * .02f + 9, 3) < .42f) continue;          // same noise that places the trees
            float s = Mathf.Lerp(l.S0, l.S1, F());
            var sc = flat ? new Vector3(s * (0.8f + F() * 0.6f), s * 0.6f, s * (0.8f + F() * 0.6f)) : new Vector3(s, s * (0.85f + F() * 0.3f), s);
            xs.Add(new Transform3D(Basis.FromEuler(new Vector3(0, F() * Mathf.Tau, 0)).Scaled(sc), new Vector3(x, h - 0.03f, z)));
            var c = l.Cols[r.Next(l.Cols.Length)]; float v = 0.9f + F() * 0.2f; cs.Add(new Color(c.R * v, c.G * v, c.B * v));
        }
        if (xs.Count == 0) return;
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = mesh, InstanceCount = xs.Count };
        for (int i = 0; i < xs.Count; i++) { mm.SetInstanceTransform(i, xs[i]); mm.SetInstanceColor(i, cs[i]); }
        holder.AddChild(new MultiMeshInstance3D
        {
            Multimesh = mm, MaterialOverride = mat, CastShadow = shadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
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
