using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>
/// Shared realistic materials (assets/materials/gothic/G_*.tres, CC0 ambientCG textures, triplanar so models need no UVs).
/// Models made with tools/blender_gothic name their materials G_Metal_Steel, G_Leather, ...; the loaders swap in the shared
/// material by that name, so every model uses the same textures. The model's vertex colours (baked dirt and wear) multiply it.
/// </summary>
public static class GothicMats
{
    const string Dir = "res://assets/materials/gothic/";
    static readonly Dictionary<string, BaseMaterial3D?> _cache = new();

    /// <summary>The shared material for a model material name ("G_Leather", "G_Leather.001"), or null if it is not a G_ material.</summary>
    public static BaseMaterial3D? For(string? name)
    {
        if (string.IsNullOrEmpty(name) || !name.StartsWith("G_")) return null;
        int dot = name.IndexOf('.'); if (dot > 0) name = name[..dot];
        if (_cache.TryGetValue(name, out var m)) return m;
        string path = Dir + name + ".tres";
        m = ResourceLoader.Exists(path) ? GD.Load<BaseMaterial3D>(path) : null;
        if (m == null) GD.PushWarning("[GOTHIC] no shared material " + path);
        return _cache[name] = m;
    }
}
