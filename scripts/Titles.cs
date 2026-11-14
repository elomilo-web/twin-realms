using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>
/// Titles: earned names a hero can wear (Character menu). The worn title shows beside the name on the HUD and over the hero for friends,
/// adds its stats to the stat table, and may leave a trail. Felled Angel (for beating Scarletta): +15% movement speed and a trail of
/// fading white angel feathers while moving.
/// </summary>
public static class Titles
{
    public const string FelledAngel = "felledAngel";
    public sealed class Def { public string Id = "", Name = "", Desc = ""; public Dictionary<string, float> Mods = new(); public bool Feathers; }
    static readonly Dictionary<string, Def> _all = new()
    {
        [FelledAngel] = new Def { Id = FelledAngel, Name = "Felled Angel", Desc = "Earned by defeating Scarletta, The Rotting Blade.", Mods = new() { ["move"] = 15 }, Feathers = true },
    };
    public static Def? Get(string? id) => id != null && _all.TryGetValue(id, out var d) ? d : null;
    public static IEnumerable<Def> All => _all.Values;

    /// <summary>Gives our hero a title; false when they already had it.</summary>
    public static bool Grant(string id) { var d = Game.D; if (d.Titles.Contains(id) || Get(id) == null) return false; d.Titles.Add(id); Game.Notify(); return true; }
    /// <summary>Wears a title ("" = none).</summary>
    public static void Wear(string id) { var d = Game.D; d.Title = id == "" || d.Titles.Contains(id) ? id : ""; Game.Notify(); Game.Save(); }
    /// <summary>The worn title's stats (Game.RecalcTree).</summary>
    public static void Apply(PlayerStats d, TreeStats ts) { if (Get(d.Title) is { } t) foreach (var kv in t.Mods) ts[kv.Key] += kv.Value; }
    /// <summary>Name with the title, for name bars and tags.</summary>
    public static string WithTitle(string name, string? title) => Get(title) is { } t ? $"{name}, {t.Name}" : name;
}

/// <summary>White angel feathers that drift down and fade behind a hero wearing Felled Angel (ours and friends').</summary>
public static class FeatherTrail
{
    static Material? _mat; static QuadMesh? _mesh;
    /// <summary>Call every frame with the hero's position and ground speed; drops a feather every so often while moving.</summary>
    public static void Tick(ref float timer, Vector3 at, float speed, float dt)
    {
        if (speed < 1f || Game.Root == null) { timer = 0; return; }
        timer -= dt; if (timer > 0) return; timer = .07f;
        _mat ??= new StandardMaterial3D { AlbedoColor = new Color(1f, 1f, .98f, .95f), EmissionEnabled = true, Emission = new Color(1f, .98f, .9f), EmissionEnergyMultiplier = .6f, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        _mesh ??= new QuadMesh { Size = new Vector2(.09f, .3f) };
        var f = new MeshInstance3D { Mesh = _mesh, MaterialOverride = (Material)_mat.Duplicate(), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        Game.Root.AddChild(f);
        f.GlobalPosition = at + new Vector3((GD.Randf() - .5f) * .6f, .6f + GD.Randf() * 1.1f, (GD.Randf() - .5f) * .6f);
        f.Rotation = new Vector3(GD.Randf() * Mathf.Tau, GD.Randf() * Mathf.Tau, GD.Randf() * Mathf.Tau);
        float life = 1.3f + GD.Randf() * .6f; var drift = new Vector3((GD.Randf() - .5f) * 1.2f, -.9f - GD.Randf() * .6f, (GD.Randf() - .5f) * 1.2f);
        var tw = f.CreateTween().SetParallel(true);
        tw.TweenProperty(f, "global_position", f.GlobalPosition + drift, life).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        tw.TweenProperty(f, "rotation", f.Rotation + new Vector3(GD.Randf() * 3f, GD.Randf() * 4f, GD.Randf() * 3f), life);
        tw.TweenProperty(f.MaterialOverride, "albedo_color:a", 0f, life * .7f).SetDelay(life * .3f);
        tw.Chain().TweenCallback(Callable.From(f.QueueFree));
    }
}
