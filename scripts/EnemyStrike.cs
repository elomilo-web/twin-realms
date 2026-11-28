using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>
/// Partial: Dark Souls rules for monsters (data/tuning.json game.enemyAtk / poise / parry, per monster in data/enemies.json poise,
/// poiseDmg, windPause, active).
/// - The blow is a hitbox, not a distance check: after the wind-up (with a readable held top, windPause) the Strike state keeps a hitbox
///   live for "active" seconds, swept every physics frame against the hero's body capsule: the held weapon's blade for armed humanoids,
///   the jaws for procedural animals, the head bone of an animated model, else a sphere ahead of the body at its reach and height.
///   A hero inside a dodge's invincible window is passed through (the hitbox stays live, so a late or early roll is still struck).
///   Only the host / owner runs this; a friend's copy only animates (host-authoritative damage, as before).
/// - Poise: the hero's blows wear it down; when it breaks the monster staggers (light, head or heavy reaction), losing its swing.
///   Heavy hitters have hyper-armour late in the wind-up and through the strike.
/// - Parry: a parried humanoid reels for a riposte window.
/// </summary>
public partial class Enemy
{
    public float Yaw => _yaw;
    /// <summary>Drawn with the character model (bandits, knights, shades, orcs...): rides the hero's sword clips, can be riposted and backstabbed.</summary>
    public bool Humanoid => _av != null && !Duelist && !Scarletta && !Def.Boss && !Proxy;
    /// <summary>The body for weapon hitboxes: a capsule from the feet, the monster's radius and height.</summary>
    public Hitbox.Body HurtBody() => new(GlobalPosition, Mathf.Max(.3f, HeightM()), Mathf.Max(.15f, Def.R * _scale));

    // ------------------------------------------------------------------ timing
    /// <summary>Seconds it holds the top of the wind-up: its own windPause, else enemyAtk.windPause (humanoids) / beastPause.</summary>
    float WindPause => Def.WindPause >= 0 ? Def.WindPause : _av != null ? Tune.F("enemyAtk.windPause", .28f) : Tune.F("enemyAtk.beastPause", .1f);
    float WindTotal => Def.Wind + (Def.Ranged != null ? 0f : WindPause);
    float ActiveTime => Def.Active > 0 ? Def.Active : Tune.F("enemyAtk.active", .24f) * (Def.Slam ? Tune.F("enemyAtk.slamActiveMul", 1.3f) : 1f);
    int _avSerial = -1, _avHit = -1; string _hitKind = "hit";
    float _deathRate = (float)GD.RandRange(.85, 1.15), _deathBlend = (float)GD.RandRange(.08, .22);

    // ------------------------------------------------------------------ poise
    float _poise = -1, _poiseT, _ripT;
    public float MaxPoise => Def.Poise > 0 ? Def.Poise : Tune.F("poise.enemyBase", 20f) * Mathf.Pow(Mathf.Clamp(HeightM() / 1.9f, .4f, 4f), 1.5f) * (Def.Boss ? Tune.F("poise.bossMul", 4f) : 1f) * (Def.Slam ? Tune.F("poise.slamMul", 1.5f) : 1f);
    /// <summary>Poise damage of this monster's blow on the hero.</summary>
    public float PoiseDamage() => Def.PoiseDmg > 0 ? Def.PoiseDmg : Tune.F("poise.enemyHitBase", 14f) * Mathf.Clamp(HeightM() / 1.9f, .5f, 3f) * (Def.Slam ? Tune.F("poise.slamHitMul", 1.8f) : 1f);
    bool HyperNow => (Def.Slam || Def.Boss) && (_state == State.Strike || (_state == State.Wind && _timer < WindTotal * Tune.F("poise.enemyHyperFrom", .4f)));
    void PoiseTick(float dt) { if (_poise < 0) _poise = MaxPoise; _poiseT -= dt; if (_poiseT <= 0) _poise = Mathf.MoveToward(_poise, MaxPoise, MaxPoise * Tune.F("poise.regenShare", .35f) * dt); }
    void PoiseHit(HitInfo h)
    {
        float pd = h.Poise >= 0 ? h.Poise : h.Kind switch { "power" => Tune.F("poise.weapon.sword", 12f) * 1.8f, "combo" => 16f, "light" => 12f, _ => Tune.F("poise.spell", 6f) };
        if (HyperNow && !h.Critical) pd *= Tune.F("poise.hyperMul", .3f);
        if (_poise < 0) _poise = MaxPoise; _poise -= pd; _poiseT = Tune.F("poise.regenDelay", 2.5f);
        if (_poise > 0 && !h.Critical) return;
        _poise = MaxPoise; if (_state == State.Stagger) return;
        bool heavy = h.Critical || pd >= Tune.F("poise.heavyHit", 26f) || h.Knock >= 8f;
        bool head = !heavy && h.At is { } at && at.Y >= GlobalPosition.Y + HeightM() * .8f;
        _hitKind = heavy ? "hitHeavy" : head ? "hitHead" : "hit";
        Stun(heavy ? Tune.F("poise.staggerHeavy", .8f) * (Def.Boss ? .6f : 1f) : Tune.F("poise.staggerLight", .45f)); _staggerLean = 1f;
        if (heavy && !Def.Boss) { var away = GlobalPosition - h.From; away.Y = 0; if (away.LengthSquared() > .001f) _kb += away.Normalized() * Tune.F("poise.knockback", 5f); }
    }
    /// <summary>Parried by the hero's guard: it reels; a humanoid is open to a riposte for that long.</summary>
    public void Parried()
    {
        float t = Humanoid ? Tune.F("parry.stun", 1.6f) : Tune.F("parry.beastStun", .6f);
        _hitKind = "hitHeavy"; Stun(t); if (Humanoid) _ripT = t;
    }
    /// <summary>Reeling from a parry right now: a riposte lands as a critical blow.</summary>
    public bool RiposteOpen => _ripT > 0 && _state == State.Stagger;

    // ------------------------------------------------------------------ the strike's hitbox
    bool _stHas, _stDodged; readonly HashSet<ulong> _stHit = new(); Vector3 _stA, _stB; float _stR; int _headBone = -2; Skeleton3D? _mdlSk;
    void BeginStrike() { _state = State.Strike; _timer = ActiveTime; _stHas = false; _stDodged = false; _stHit.Clear(); StrikeTick(_hero); }

    /// <summary>This frame's hitbox: a segment (a == b for a sphere) and its radius, in world space.</summary>
    void StrikeShape(float k, out Vector3 a, out Vector3 b, out float r)
    {
        float slack = Tune.F("hitbox.slack", .05f);
        var fw = new Vector3(-Mathf.Sin(_yaw), 0, -Mathf.Cos(_yaw));
        if (_av?.BladeTip != null && _av.BladeBase != null && IsInstanceValid(_av.BladeTip))
        {   // an armed humanoid: its real blade
            a = _av.BladeBase.GlobalPosition; b = _av.BladeTip.GlobalPosition; r = Tune.F("hitbox.bladeRadius", .07f) + slack; return;
        }
        if (_cm != null && _cm.BitePoint(out var bp, out float br)) { a = b = bp; r = Mathf.Max(br, Tune.F("hitbox.biteMin", .12f)) + slack; return; }
        if (_mdl != null)
        {   // an animated model: its head bone, pushed a little ahead (the jaws)
            if (_headBone == -2) { _mdlSk = _mdl.FindChild("Skeleton3D", true, false) as Skeleton3D; _headBone = -1; if (_mdlSk != null) for (int i = 0; i < _mdlSk.GetBoneCount(); i++) if (_mdlSk.GetBoneName(i).ToLower().Contains("head")) { _headBone = i; break; } }
            if (_headBone >= 0 && _mdlSk != null && IsInstanceValid(_mdlSk))
            {
                var hp = _mdlSk.GlobalTransform * _mdlSk.GetBoneGlobalPose(_headBone).Origin;
                a = b = hp + fw * Def.R * _scale * Tune.F("enemyAtk.headAhead", .4f); r = Mathf.Max(.2f, Def.R * _scale * Tune.F("enemyAtk.headRadius", .55f)) + slack; return;
            }
        }
        // anything else: a strike sphere that lunges from near the body out to the full reach during the window, at the body's striking height
        float near = Def.R * _scale, reach = Mathf.Max(near + .3f, Def.Reach);
        float d = Mathf.Lerp(near + (reach - near) * Tune.F("enemyAtk.sphereFrom", .45f), reach, Mathf.Clamp(k, 0, 1));
        r = Mathf.Max(Tune.F("enemyAtk.sphereMin", .3f), Def.R * _scale * Tune.F("enemyAtk.sphereScale", .6f)) + slack;
        a = b = GlobalPosition + fw * (d - r * .5f) + Vector3.Up * Mathf.Clamp(HeightM() * Tune.F("enemyAtk.strikeHeight", .55f), .3f, 6f);
    }

    /// <summary>One physics frame of the strike: sweep the hitbox from last frame against the heroes (and a pet it fights). Host / owner only.</summary>
    void StrikeTick(Node3D? target)
    {
        if (Puppet) return;
        float k = 1f - _timer / Mathf.Max(.01f, ActiveTime);
        StrikeShape(k, out var a, out var b, out float r);
        if (!_stHas) { _stA = a; _stB = b; _stR = r; _stHas = true; }
        var red = new Color(1f, .35f, .15f);
        if (a.DistanceSquaredTo(b) > 1e-4f) HitboxDebug.Seg(a, b, red); else HitboxDebug.Sphere(a, r, red, .15f);
        HitboxDebug.Seg(_stB, b, red with { A = .5f });
        var targets = new List<Node3D>(3);
        if (target != null && IsInstanceValid(target)) targets.Add(target);
        if (Player.Instance is { } me && me != target && me.GlobalPosition.DistanceSquaredTo(GlobalPosition) < 100f + Def.Reach * Def.Reach) targets.Add(me);
        foreach (var t in targets)
        {
            if (_stHit.Contains(t.GetInstanceId()) || t is not IDamageable dm || dm.Dead) continue;
            var body = t is Player p ? p.HurtBody() : t is Pet ? new Hitbox.Body(t.GlobalPosition, Tune.F("hitbox.petHeight", 1f), Tune.F("hitbox.petRadius", .45f))
                     : new Hitbox.Body(t.GlobalPosition, Tune.F("hitbox.heroHeight", 1.75f), Tune.F("hitbox.heroRadius", .36f));
            if (!Hitbox.Sweep(_stA, _stB, a, b, Mathf.Max(r, _stR), body, out var at)) continue;
            if (t is Player hero && hero.IFrames) { if (!_stDodged) { _stDodged = true; hero.Dodged(this); } continue; }   // through the dodge: the blow is still live
            _stHit.Add(t.GetInstanceId());
            dm.TakeHit(OnHit(new HitInfo { Damage = Damage, From = GlobalPosition, Knock = Def.Slam ? 9f : 4f, Stagger = Def.Slam, Attacker = this, Poise = PoiseDamage(), At = at }));
            HitboxDebug.Sphere(at, .1f, Colors.White, .5f);
        }
        _stA = a; _stB = b; _stR = r;
    }
}
