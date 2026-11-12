using Godot;

namespace TwinRealms;

/// <summary>
/// Lock-on targeting. Tab (or middle mouse) locks the enemy nearest the screen centre; Tab or middle mouse again cycles to the next one, Q releases.
/// While locked the camera turns to keep the target in view, attacks and spells aim at it, and a ring marks it on the ground.
/// Auto target (Settings > Game): with nothing locked, attacks and spells aim at a soft target picked by the chosen rule
/// (monsters that hit you or that you hit, the nearest, or the one with the least health). The camera stays free; a thinner ring marks it.
/// </summary>
public static class Targeting
{
    public static Enemy? Current { get; private set; }
    /// <summary>The auto target (only while nothing is locked).</summary>
    public static Enemy? Soft { get; private set; }
    /// <summary>What attacks and spells aim at: the lock, else the auto target.</summary>
    public static Enemy? Aim => Current ?? Soft;
    public const float MaxRange = 32f, AutoRange = 22f;
    public static readonly string[] AutoModes = { "Off", "Monsters that hit you", "Nearest", "Lowest health" };
    static MeshInstance3D? _ring; static StandardMaterial3D? _ringMat;
    static Enemy? _hostile; static float _hostileT, _pickT;

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
        Current = e; Soft = null; Sfx.Play("click");
        Ring(); _ring!.Visible = true;
    }
    static void Ring()
    {
        if (_ring != null && GodotObject.IsInstanceValid(_ring)) return;
        _ringMat = new StandardMaterial3D { AlbedoColor = new Color(1f, .35f, .25f, .85f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, NoDepthTest = true };
        _ring = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = .9f, OuterRadius = 1f, Rings = 40, RingSegments = 3 }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, MaterialOverride = _ringMat };
        Game.Root?.AddChild(_ring);
    }

    /// <summary>A monster hit us or we hit it (auto target "monsters that hit you" follows the latest one).</summary>
    public static void NoteHostile(Enemy e) { _hostile = e; _hostileT = World.Clock; if (Game.Set.AutoTarget == 1 && Current == null) _pickT = 0; }

    static bool Valid(Enemy? e, Player p, float range) => e != null && GodotObject.IsInstanceValid(e) && !e.Dead && e.Active && e.Visible && !e.Def.Flee && (e.GlobalPosition - p.GlobalPosition).Length() <= range;

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

    /// <summary>The auto target by the chosen rule (Settings > Game > Auto target).</summary>
    static Enemy? PickAuto(Player p, int mode)
    {
        if (mode == 1)
        {
            // the monster that last hit us or that we last hit, while the fight is fresh; otherwise the nearest one that is chasing us
            if (Valid(_hostile, p, AutoRange) && World.Clock - _hostileT < 8f) return _hostile;
        }
        Enemy? best = null; float bs = 1e9f;
        foreach (var n in p.GetTree().GetNodesInGroup("enemies"))
        {
            if (n is not Enemy e || !Valid(e, p, AutoRange) || e.Proxy) continue;
            float dist = (e.GlobalPosition - p.GlobalPosition).Length();
            float score = mode == 3 ? e.Hp + dist * .01f : dist;
            if (score < bs) { bs = score; best = e; }
        }
        // "hit you" mode with nothing fresh: only the closest foe within a few steps (so it never pulls you toward idle camps)
        if (mode == 1 && best != null && (best.GlobalPosition - p.GlobalPosition).Length() > 8f) return null;
        return best;
    }

    /// <summary>Called every frame by the player: drops dead or far targets, picks the auto target and moves the ring.</summary>
    public static void Tick(Player p)
    {
        if (Current != null && (!GodotObject.IsInstanceValid(Current) || Current.Dead || !Current.Active || (Current.GlobalPosition - p.GlobalPosition).Length() > MaxRange * 1.3f))
        { var old = Current; Clear(); if (old != null && GodotObject.IsInstanceValid(old) && old.Dead) Toggle(false); }
        int mode = Game.Set.AutoTarget;
        if (Current != null || mode <= 0) Soft = null;
        else
        {
            if (Soft != null && !Valid(Soft, p, AutoRange * 1.2f)) { Soft = null; _pickT = 0; }
            _pickT -= (float)p.GetPhysicsProcessDeltaTime();
            if (_pickT <= 0) { _pickT = mode == 3 ? .25f : .4f; Soft = PickAuto(p, mode); }
        }
        var t = Aim;
        if (t == null) { if (_ring != null && GodotObject.IsInstanceValid(_ring)) _ring.Visible = false; return; }
        Ring(); _ring!.Visible = true;
        _ringMat!.AlbedoColor = Current != null ? new Color(1f, .35f, .25f, .85f) : new Color(1f, .8f, .35f, .5f);   // locked: red; auto: soft gold
        float r = t.Def.R * t.ScaleF() * 1.6f + .4f; _ring.GlobalPosition = t.GlobalPosition + Vector3.Up * .08f; _ring.Scale = new Vector3(r, Current != null ? .4f : .25f, r); _ring.RotateY(.02f);
    }
}
