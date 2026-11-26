using Godot;
using System.Collections.Generic;

namespace TwinRealms;

/// <summary>First-use cost removal: after the world is built, fire every lazily-built effect once in front of the camera
/// (builds the static materials, meshes and generated textures and compiles their pipelines), hold two frames, then free it all.</summary>
public static class Warmup
{
    static bool _done;

    static bool Skip() => Bot.Wanted() || MpBot.Wanted() != null || OnlineBot.Wanted() || Observer.Wanted() || Shots.Wanted() || Showcase.Wanted() || FitBot.Wanted();

    public static void Run(Node root)
    {
        if (_done || Skip() || Game.Root == null) return;
        _done = true;
        _ = Go(root);
    }

    static async System.Threading.Tasks.Task Go(Node root)
    {
        var tree = root.GetTree();
        await root.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        var cam = root.GetViewport()?.GetCamera3D(); var parent = Game.Root;
        if (cam == null || parent == null) return;
        ulong t0 = Time.GetTicksMsec();
        var before = new HashSet<Node>(parent.GetChildren());
        var fwd = -cam.GlobalTransform.Basis.Z; var p = cam.GlobalPosition + fwd * 2f;
        try
        {
            var white = new Color(1f, .9f, .6f);
            Vfx.Explosion(p, 1.5f); Vfx.ScorchDecal(p, 1.5f); Vfx.HeatShimmer(p, 1.5f); Vfx.Aftermath(p, 1.5f);
            Vfx.FrostNova(p, 1.5f); Vfx.FrostDecal(p, 1.5f); Vfx.IceShardRing(p, 1.5f); Vfx.FrostAftermath(p, 1.5f);
            Vfx.HolyImpact(p, 1.5f); Vfx.ShadowImpact(p, 1.5f); Vfx.PoisonImpact(p, 1.5f);
            Vfx.StormStrike(p, 1.5f); Vfx.Lightning(p + Vector3.Up * 2f, p, white); Vfx.HealPillarAt(p, white, 1f);
            Vfx.Ring(p, 1f, white, .3f); Vfx.Flash(p, white, 1f, 3f, .2f);
            Vfx.Sparks(p, Vfx.Ramp((0f, white), (1f, new Color(1f, .3f, .1f, 0f))), 8, 2f, .4f, .1f);
            Fx.Burst(p, white); Fx.Dust(p, 1f); Fx.Ring(p, 1f, white); Fx.Line(p, p + Vector3.Up, white);
            foreach (var t in new[] { "fire", "frost", "holy", "shadow", "poison", "lightning" })
            { try { Vfx.ElementImpact(t, p, 1.5f); } catch { } }
            SpellFx.SizeOf("fire_burst");   // loads data/spellfx.json now: its audit (flipbooks + effect-library styles) lands in the startup log
            SpellStyles.WarmAll(p);   // F-12: every element x shape of the effect library (shaders, GPU particle process shaders, meshes)
        }
        catch (System.Exception e) { GD.Print($"[WARMUP] effect failed: {e.Message}"); }
        // only what the calls above made (synchronously): enemies, loot or anything else added in the next frames stay
        var made = new List<Node>(); foreach (var c in parent.GetChildren()) if (!before.Contains(c)) made.Add(c);
        await root.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await root.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        foreach (var c in made) if (GodotObject.IsInstanceValid(c)) c.QueueFree();
        GD.Print($"[WARMUP] effects warmed in {(Time.GetTicksMsec() - t0) / 1000f:0.00} s");
    }
}
