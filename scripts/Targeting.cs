using Godot;

namespace TwinRealms;

/// <summary>
/// Lock-on targeting. Tab (or middle mouse) locks the enemy nearest the screen centre; Tab or middle mouse again cycles to the next one, Q releases.
/// While locked the camera turns to keep the target in view, attacks and spells aim at it, and a ring marks it on the ground.
/// </summary>
public static class Targeting
{
    public static Enemy? Current { get; private set; }
    public const float MaxRange = 32f;
    static MeshInstance3D? _ring;

    public static void Toggle(bool cycle)
    {
        var p = Player.Instance; if (p == null) return;
        var next = Pick(p, cycle ? Current : null);
        if (next == null) { if (!cycle) Clear(); return; }
        Set(next);
    }
    public static void Clear() { Current = null; if (_ring != null && GodotObject.IsInstanceValid(_ring)) _ring.Visible = false; }
    public static void Set(Enemy e)
    {
        Current = e; Sfx.Play("click");
        if (_ring == null || !GodotObject.IsInstanceValid(_ring))
        {
            _ring = new MeshInstance3D
            {
                Mesh = new TorusMesh { InnerRadius = .9f, OuterRadius = 1f, Rings = 40, RingSegments = 3 }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(1f, .35f, .25f, .85f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, NoDepthTest = true },
            };
            Game.Root?.AddChild(_ring);
        }
        _ring.Visible = true;
    }

    /// <summary>Best candidate: alive, active, in range, in front of the camera; scored by angle from screen centre and distance. Skips "after" when cycling.</summary>
    static Enemy? Pick(Player p, Enemy? after)
    {
        var cam = p.Rig?.Camera; if (cam == null) return null;
        var fwd = -cam.GlobalTransform.Basis.Z; Enemy? best = null; float bs = 1e9f; Enemy? first = null; float fs = 1e9f;
        foreach (var n in p.GetTree().GetNodesInGroup("enemies"))
        {
            if (n is not Enemy e || e.Dead || !e.Active || !e.Visible || e.Def.Flee) continue;
            var d = e.GlobalPosition + Vector3.Up - cam.GlobalPosition; float dist = (e.GlobalPosition - p.GlobalPosition).Length();
            if (dist > MaxRange) continue; float ang = fwd.AngleTo(d.Normalized()); if (ang > 1.1f) continue;
            float score = ang * 12f + dist * .25f;
            if (score < fs) { fs = score; first = e; }
            if (e == after) continue;
            if (after != null) { float as_ = fwd.AngleTo((after.GlobalPosition + Vector3.Up - cam.GlobalPosition).Normalized()) * 12f + (after.GlobalPosition - p.GlobalPosition).Length() * .25f; if (score <= as_) continue; }
            if (score < bs) { bs = score; best = e; }
        }
        return best ?? (after != null ? first : null);
    }

    /// <summary>Called every frame by the player: drops dead or far targets and moves the ring.</summary>
    public static void Tick(Player p)
    {
        if (Current == null) return;
        if (!GodotObject.IsInstanceValid(Current) || Current.Dead || !Current.Active || (Current.GlobalPosition - p.GlobalPosition).Length() > MaxRange * 1.3f) { var old = Current; Clear(); if (old != null && GodotObject.IsInstanceValid(old) && old.Dead) Toggle(false); return; }
        if (_ring != null) { float r = Current.Def.R * Current.ScaleF() * 1.6f + .4f; _ring.GlobalPosition = Current.GlobalPosition + Vector3.Up * .08f; _ring.Scale = new Vector3(r, .4f, r); _ring.RotateY(.02f); }
    }
}
