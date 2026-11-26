using System.Collections.Generic;
using Godot;

namespace TwinRealms;

// Enemy AI helpers (aggro / noticing, attack telegraph, summoning); the state switch itself stays in Enemy._PhysicsProcess
public partial class Enemy
{
    // ------------------------------------------------------------------ noticing heroes
    float _seeT; bool _saw;
    /// <summary>Within aggro range: outdoors that is enough; in a dungeon the monster must see the hero (no walls between), or hear them up close.</summary>
    bool Notices(Node3D hero, float dist, float dt)
    {
        if (!FromDungeon || dist < 4f) return true;
        _seeT -= dt; if (_seeT > 0) return _saw; _seeT = .25f + GD.Randf() * .1f;
        var from = GlobalPosition + Vector3.Up * Mathf.Min(1.4f, HeightM() * .8f); var to = hero.GlobalPosition + Vector3.Up * 1.2f;
        var q = PhysicsRayQueryParameters3D.Create(from, to, 1); q.Exclude = new Godot.Collections.Array<Rid> { GetRid() };
        _saw = GetWorld3D().DirectSpaceState.IntersectRay(q).Count == 0;
        return _saw;
    }
    /// <summary>This monster noticed a hero (or was struck): the idle monsters around it (9 m) join the fight, and so do its
    /// packmates (same PackId) within zones.json rules.packAlert, even ones still walking home.</summary>
    void Alert()
    {
        float r = IsShade ? Tune.F("raid.shadeAlert", 45f) : 9f, pr = Zones.PackAlert;   // shades call every shade in the hall
        foreach (var n in GetTree().GetNodesInGroup("enemies"))
        {
            if (n is not Enemy e || e == this || e.Dead || e.Puppet || e.Def.Flee || e.FromDungeon != FromDungeon) continue;
            float d2 = e.GlobalPosition.DistanceSquaredTo(GlobalPosition);
            bool mate = PackId > 0 && e.PackId == PackId && d2 < pr * pr && (e._state is State.Idle or State.Return);
            if (mate || (e._state == State.Idle && d2 < r * r)) e.Wake();
        }
    }

    // ------------------------------------------------------------------ attack telegraph
    void ClearTele() { if (_tele != null && IsInstanceValid(_tele)) _tele.QueueFree(); _tele = null; }
    // wind-up telegraph: cold glint at the head, plus a growing dark-red ground ring for heavy / boss / area blows (visuals are local)
    void Telegraph()
    {
        if (Dead || Puppet) return;
        Fx.Burst(GlobalPosition + new Vector3(0, HeightM() * 0.85f, 0), new Color(0.75f, 0.9f, 1f), 7, 1.5f, Mathf.Max(.2f, Def.Wind), 0.14f);
        ClearTele();
        if (!(Def.Slam || Def.Boss || Def.Kind == "dragon") || Tune.F("monsters.telegraphRing", 1) <= 0) return;
        float rad = Def.Reach + 0.8f;
        var mat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, AlbedoColor = new Color(.45f, .02f, .02f, .1f), CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        // a thin ring at the edge of the blow (a solid disc hid the ground and the hero: devloop #4)
        _tele = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = rad - 0.14f, OuterRadius = rad, Rings = 48, RingSegments = 4 }, MaterialOverride = mat, TopLevel = true, Scale = new Vector3(1, 0.05f, 1), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_tele); _tele.GlobalPosition = GlobalPosition + Vector3.Up * 0.08f;
    }
    void TeleTick()
    {
        if (_tele == null || !IsInstanceValid(_tele)) return;
        if (_tele.MaterialOverride is StandardMaterial3D m) m.AlbedoColor = new Color(.6f, .05f, .03f, Mathf.Lerp(.85f, .15f, Mathf.Clamp(_timer / Mathf.Max(.05f, Def.Wind), 0, 1)));
    }

    // ------------------------------------------------------------------ summoning
    void SummonAdds()
    {
        if (Def.Summon == null) return;
        for (int i = 0; i < 2; i++)
        {
            var a = i == 0 ? 1f : -1f;
            var pos = GlobalPosition + new Vector3(a * 3f, 0.3f, 2f);
            var e = new Enemy { Type = Def.Summon, Level = Mathf.Max(1, Level - 1), Summoned = true, FromDungeon = FromDungeon, Position = pos, Owner_ = this };
            GetParent().AddChild(e); _summoned++; if (FromDungeon) Dungeon.Register(e);
            Fx.Burst(pos + Vector3.Up, new Color(0.6f, 0.3f, 0.9f), 14, 4f);
        }
        Ui.Note(Def.N + " calls for aid!", new Color(1, 0.6f, 0.4f));
    }
}

public partial class Enemy
{
    /// <summary>Head tracking / idle variety state read by EnemyLook.</summary>
    internal bool LookAggro => !Puppet && (_state == State.Chase || _state == State.Wind || _state == State.Recover || _state == State.Breath);
    internal bool LookIdle => _state == State.Idle;
    internal bool LookBlocked => Dead || _state == State.Stagger || _stun > 0 || _state == State.Sleep;
    internal Node3D? LookHero => _hero != null && IsInstanceValid(_hero) ? _hero : null;
    internal bool LookQuad => Def.Kind == "quad";
    void AddLook(Skeleton3D? sk) { if (sk != null && sk.GetNodeOrNull("EnemyLook") == null) sk.AddChild(new EnemyLook { Name = "EnemyLook", E = this }); }
}

/// <summary>Post-animation enemy life: head turns toward the hero when aggro (60 deg yaw / 25 deg pitch), idle look-around sweeps and quadruped sniff wobble.</summary>
public partial class EnemyLook : SkeletonModifier3D
{
    public Enemy E = null!;
    int _head = -1, _root = -1; bool _init;
    float _yaw, _pitch, _w, _next = 3f + GD.Randf() * 6f, _sweep = -1f, _wob = -1f, _roll, _rpitch, _chk;
    bool _near = true;

    public override void _ProcessModificationWithDelta(double delta)
    {
        var sk = GetSkeleton(); if (sk == null || E == null || !GodotObject.IsInstanceValid(E)) return;
        if (!_init)
        {
            _init = true;
            foreach (var key in new[] { "head", "neck" }) { for (int i = 0; i < sk.GetBoneCount() && _head < 0; i++) if (sk.GetBoneName(i).Contains(key, System.StringComparison.OrdinalIgnoreCase)) _head = i; if (_head >= 0) break; }
            foreach (var key in new[] { "pelvis", "hip", "root", "spine" }) { for (int i = 0; i < sk.GetBoneCount() && _root < 0; i++) if (sk.GetBoneName(i).Contains(key, System.StringComparison.OrdinalIgnoreCase)) _root = i; if (_root >= 0) break; }
        }
        float dt = Mathf.Clamp((float)delta, 0.0001f, .1f);
        _chk -= dt; if (_chk <= 0) { _chk = .3f; var cam = GetViewport().GetCamera3D(); _near = cam != null && cam.GlobalPosition.DistanceSquaredTo(E.GlobalPosition) < 900f; }
        if (!_near || _head < 0 || E.LookBlocked) { _w = 0; _yaw = _pitch = _roll = _rpitch = 0; _sweep = _wob = -1f; return; }

        float k = 1f - Mathf.Exp(-6f * dt), ty = 0, tp = 0;
        var hero = E.LookHero;
        if (E.LookAggro && hero != null && hero.GlobalPosition.DistanceSquaredTo(E.GlobalPosition) < 225f)
        {
            var gi = sk.GlobalTransform.AffineInverse(); var d = gi * (hero.GlobalPosition + Vector3.Up * 1.3f) - sk.GetBoneGlobalPose(_head).Origin;
            if (d.LengthSquared() > .01f)
            {
                d = d.Normalized(); float wy = Mathf.Atan2(d.X, d.Z);   // skeleton space: model faces +Z
                if (Mathf.Abs(wy) < Mathf.Pi * .75f) { ty = Mathf.Clamp(wy, -Mathf.DegToRad(60f), Mathf.DegToRad(60f)); tp = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(d.Y, -1f, 1f)), -Mathf.DegToRad(25f), Mathf.DegToRad(25f)); }
            }
            _sweep = _wob = -1f; _next = 4f;
        }
        else if (E.LookIdle)
        {
            _next -= dt;
            if (_next <= 0 && _sweep < 0 && _wob < 0)
            {
                _next = 6f + GD.Randf() * 6f;
                if (E.LookQuad && GD.Randf() < .5f) _wob = 0; else _sweep = 0;
            }
            if (_sweep >= 0) { _sweep += dt; if (_sweep >= 2f) _sweep = -1f; else ty = Mathf.DegToRad(40f) * Mathf.Sin(_sweep / 2f * Mathf.Tau); }
            if (_wob >= 0) { _wob += dt; if (_wob >= .4f) _wob = -1f; else { float s = Mathf.Sin(_wob / .4f * Mathf.Pi * 3f) * (1f - _wob / .4f); _roll = s * .07f; _rpitch = s * .05f; } }
        }
        else { _sweep = _wob = -1f; }
        if (_wob < 0) { _roll = Mathf.Lerp(_roll, 0, k); _rpitch = Mathf.Lerp(_rpitch, 0, k); }

        _w = Mathf.MoveToward(_w, 1f, dt * 4f);
        _yaw = Mathf.Lerp(_yaw, ty, k); _pitch = Mathf.Lerp(_pitch, tp, k);
        if (_root >= 0 && (Mathf.Abs(_roll) > .002f || Mathf.Abs(_rpitch) > .002f)) Set(sk, _root, new Quaternion(Vector3.Back, _roll) * new Quaternion(Vector3.Right, _rpitch));
        if (Mathf.Abs(_yaw) > .002f || Mathf.Abs(_pitch) > .002f) Set(sk, _head, new Quaternion(Vector3.Up, _yaw * _w) * new Quaternion(Vector3.Right, -_pitch * _w));
    }

    static void Set(Skeleton3D sk, int b, Quaternion q)
    {
        int par = sk.GetBoneParent(b); var pg = par >= 0 ? sk.GetBoneGlobalPose(par).Basis.Orthonormalized() : Basis.Identity;
        var g = sk.GetBoneGlobalPose(b).Basis.Orthonormalized();
        sk.SetBonePoseRotation(b, (pg.Inverse() * (new Basis(q.Normalized()) * g)).GetRotationQuaternion().Normalized());
    }
}
