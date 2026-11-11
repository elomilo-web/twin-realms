using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>
/// Environment models from the Quaternius kits (assets/models/env, listed in data/env.json).
/// Each glTF becomes one cached mesh; materials with the same name are shared between models, toon-lit (Toon.cs) and tinted for the Shadow realm (World.Track).
/// </summary>
public static class Kit
{
    public sealed class Piece { public Mesh Mesh = null!; public Transform3D Xf = Transform3D.Identity; public Aabb Box; }
    public readonly record struct Model(string Name, float Scale);

    const string Dir = "res://assets/models/env/";
    static readonly Dictionary<string, Piece?> _cache = new();
    static readonly Dictionary<string, Material> _mats = new();
    static JsonDocument? _cfg;
    public static JsonElement Cfg => (_cfg ??= JsonUtil.Load("res://data/env.json")).RootElement;
    static bool? _ok;
    /// <summary>True when the kit files are present and data/env.json does not disable them.</summary>
    public static bool Available => _ok ??= ResourceLoader.Exists(Dir + "village/Wall_Plaster_Straight.gltf") && !(Cfg.TryGetProperty("enabled", out var e) && e.ValueKind == JsonValueKind.False);

    /// <summary>A model by its env path, e.g. "nature/Pine_1", or a Twin Realms kit model, e.g. "kit/nature/tree_round_a" (assets/models/kit, .glb). Null if missing.</summary>
    public static Piece? Get(string name)
    {
        if (_cache.TryGetValue(name, out var p)) return p;
        var path = name.StartsWith("kit/") ? "res://assets/models/" + name + ".glb" : Dir + name + ".gltf";
        if (ResourceLoader.Exists(path) && GD.Load<PackedScene>(path)?.Instantiate() is Node root)
        {
            var parts = new List<(MeshInstance3D mi, Transform3D t)>();
            void Walk(Node n, Transform3D t)
            {
                foreach (var c in n.GetChildren())
                {
                    var ct = c is Node3D c3 ? t * c3.Transform : t;
                    if (c is MeshInstance3D mi && mi.Mesh != null) parts.Add((mi, ct));
                    Walk(c, ct);
                }
            }
            Walk(root, Transform3D.Identity);
            // one mesh: use it as imported (keeps its automatic LODs); several: merge them, one surface per material
            if (parts.Count == 1) p = new Piece { Mesh = parts[0].mi.Mesh, Xf = parts[0].t };
            else if (parts.Count > 1) p = new Piece { Mesh = Merge(parts) };
            if (p != null)
            {
                for (int i = 0; i < p.Mesh.GetSurfaceCount(); i++) if (p.Mesh.SurfaceGetMaterial(i) is { } m && p.Mesh is ArrayMesh am) am.SurfaceSetMaterial(i, Shared(m));
                p.Box = p.Xf * p.Mesh.GetAabb();
            }
            root.Free();
        }
        if (p == null) GD.PushWarning("[KIT] missing model " + path);
        _cache[name] = p; return p;
    }

    static ArrayMesh Merge(List<(MeshInstance3D mi, Transform3D t)> parts)
    {
        var order = new List<string>(); var sts = new Dictionary<string, SurfaceTool>(); var mats = new Dictionary<string, Material?>();
        foreach (var (mi, t) in parts)
            for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
            {
                var m = mi.GetActiveMaterial(s); var k = m?.ResourceName ?? "";
                if (!sts.TryGetValue(k, out var st)) { sts[k] = st = new SurfaceTool(); order.Add(k); mats[k] = m; }
                st.AppendFrom(mi.Mesh, s, t);
            }
        var am = new ArrayMesh();
        foreach (var k in order) { sts[k].SetMaterial(mats[k]); sts[k].Commit(am); }
        return am;
    }

    /// <summary>One material per glTF material name across all models, with the tweaks from env.json "materials".</summary>
    static Material Shared(Material m)
    {
        var k = m.ResourceName;
        if (k != "" && _mats.TryGetValue(k, out var s)) return s;
        s = m;
        Cfg.TryGetProperty("materials", out var all);
        JsonElement tw = default; bool has = all.ValueKind == JsonValueKind.Object && all.TryGetProperty(k, out tw);
        if (has && tw.TryGetProperty("glow", out var g) && g.ValueKind == JsonValueKind.True) s = World.WindowMat;
        else if (m is StandardMaterial3D sm)
        {
            if (has) sm.Metallic = JsonUtil.F(tw, "metallic", sm.Metallic);
            if (has && tw.TryGetProperty("vertexColor", out var vc) && vc.ValueKind == JsonValueKind.True) sm.VertexColorUseAsAlbedo = true;   // lets per-instance tints show
            sm.Metallic = Mathf.Min(sm.Metallic, .6f);
            World.Track(sm);
        }
        if (k != "") _mats[k] = s;
        return s;
    }

    // ---------------------------------------------------------------- placing
    static Transform3D Place(Vector3 pos, float yaw, float scale) => new(new Basis(Vector3.Up, yaw).Scaled(Vector3.One * scale), pos);

    /// <summary>Adds a model to parent; solid = a box collider around it.</summary>
    public static MeshInstance3D? Add(Node parent, string name, Vector3 pos, float yaw = 0f, float scale = 1f, bool solid = false)
    {
        var p = Get(name); if (p == null) return null;
        var t = Place(pos, yaw, scale);
        var mi = new MeshInstance3D { Mesh = p.Mesh, Transform = t * p.Xf }; parent.AddChild(mi);
        if (solid)
        {
            var b = new StaticBody3D { Transform = t, CollisionLayer = 1 };
            b.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = p.Box.Size }, Position = p.Box.GetCenter() });
            parent.AddChild(b);
        }
        return mi;
    }

    // ---------------------------------------------------------------- data helpers
    public static string Str(string section, string key, string d = "") => Cfg.TryGetProperty(section, out var s) ? JsonUtil.S(s, key, d) : d;
    /// <summary>A list of [name, scale] pairs.</summary>
    public static List<Model> Models(JsonElement arr)
    {
        var l = new List<Model>();
        if (arr.ValueKind == JsonValueKind.Array)
            foreach (var e in arr.EnumerateArray())
                if (e.ValueKind == JsonValueKind.Array && e.GetArrayLength() > 0) l.Add(new Model(e[0].GetString() ?? "", e.GetArrayLength() > 1 ? e[1].GetSingle() : 1f));
                else if (e.ValueKind == JsonValueKind.String) l.Add(new Model(e.GetString() ?? "", 1f));
        return l;
    }
    public static List<Model> Models(string section, string? sub = null)
    {
        if (!Cfg.TryGetProperty(section, out var s)) return new();
        if (sub != null) return s.TryGetProperty(sub, out var ss) ? Models(ss) : new();
        return Models(s);
    }
}
