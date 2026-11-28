using System;
using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>
/// Partial: Dark Souls style melee for the hero (data/tuning.json game.hitbox / poise / roll / backstep / parry / backstab / attack.step*).
/// - Swept weapon hitbox: during each swing's active window the held blade (grip to tip, from the weapon's real nodes; fists = forearm to
///   knuckles) is swept from last physics frame to this one against every foe's body capsule. Each foe is hit at most once per swing, at
///   the contact point. A wall or the ground stops the blade where it touches; heavy weapons recoil off stone. Thrusts cut with the tip only.
/// - Poise: blows wear it down; when it breaks the hero staggers (Hit_Chest, Hit_Head, Hit_Knockback by direction and strength). Heavy and
///   two-handed swings have hyper-armour through their active frames. Poise refills after a pause.
/// - Critical attacks: a riposte on a parried humanoid, a backstab on one that is not hunting the hero.
/// - Roll i-frames are a window inside the roll; a no-direction dodge is a backstep with its own smaller window.
/// </summary>
public partial class Player
{

    // ------------------------------------------------------------------ hurtbox, i-frames
    /// <summary>The hero's body for monster hitboxes: a capsule from the feet (lower while crouching).</summary>
    public Hitbox.Body HurtBody() => new(GlobalPosition, Sneaking ? Tune.F("hitbox.heroCrouchHeight", 1.3f) : Tune.F("hitbox.heroHeight", 1.75f), Tune.F("hitbox.heroRadius", .36f));
    float RollTime => Tune.F("roll.time", .6f);
    float _rollEl, _bsT, _bsEl, _bsDur; Vector3 _bsDir; float _rmPrev;
    /// <summary>In a dodge's invincible window (or a short grace after a hit / parry): monster blows pass through.</summary>
    public bool IFrames => _inv > 0 || (_rollT > 0 && _rollEl >= Tune.F("roll.iStart", .06f) && _rollEl <= Tune.F("roll.iEnd", .4f))
                         || (_bsT > 0 && _bsEl >= Tune.F("backstep.iStart", .03f) && _bsEl <= Tune.F("backstep.iEnd", .2f));
    /// <summary>A monster blow passed through the dodge: a perfect dodge when it came early in the roll.</summary>
    public void Dodged(Enemy e) { if (_rollT > 0 && _rollEl <= Tune.F("perfectDodge.window", .18f) + Tune.F("roll.iStart", .06f)) PerfectDodge(); }

    // ------------------------------------------------------------------ poise
    public float Poise = -1;
    float _poiseT;
    public float MaxPoise => Mathf.Min(Tune.F("poise.heroMax", 90f), Tune.F("poise.heroBase", 25f) + Game.D.Defense() * Tune.F("poise.heroPerDefense", .35f));
    string _hurtKind = "hit";
    void PoiseTick(float dt)
    {
        if (Poise < 0) Poise = MaxPoise;
        _poiseT -= dt; if (_poiseT <= 0) Poise = Mathf.MoveToward(Poise, MaxPoise, MaxPoise * Tune.F("poise.regenShare", .35f) * dt);
    }
    /// <summary>Hyper-armour: a heavy, two-handed or power swing from late in its wind-up to the end of its active window.</summary>
    bool Hyper()
    {
        if (_atkT <= 0 || _atk == null || Ranged || !(_atkPower || W.Hands > 1 || W.Heavy)) return false;
        float prog = 1f - _atkT / _atkDur; ActiveWindow(_atk, out _, out float e1);
        return prog >= _atk.Wind * Tune.F("poise.hyperFrom", .5f) && prog <= e1;
    }
    /// <summary>Poise damage of a monster's blow on the hero; returns true when it breaks (the hero staggers), with the reaction to play.</summary>
    bool HeroPoiseHit(HitInfo h, float baseDmg)
    {
        float pd = h.Poise >= 0 ? h.Poise : h.IsProjectile ? Tune.F("poise.projectile", 6f) : h.Attacker is Enemy en && IsInstanceValid(en) ? en.PoiseDamage() : Tune.F("poise.enemyHitBase", 14f);
        if (Hyper()) pd *= Tune.F("poise.hyperMul", .3f);
        if (Game.TS["ccImmune"] > 0) pd = 0;
        Poise = (Poise < 0 ? MaxPoise : Poise) - pd; _poiseT = Tune.F("poise.regenDelay", 2.5f);
        if (Poise > 0) return false;
        Poise = MaxPoise;   // broken: it resets with the stagger
        bool heavy = pd >= Tune.F("poise.heavyHit", 26f) || h.Knock >= 8f;
        float headY = GlobalPosition.Y + Tune.F("hitbox.heroHeight", 1.75f) * .82f;
        bool head = !heavy && ((h.At is { } at && at.Y >= headY) || h.From.Y > GlobalPosition.Y + Tune.F("poise.headFromAbove", .9f));
        _hurtKind = heavy ? "hitHeavy" : head ? "hitHead" : "hit";
        if (heavy) { _rmPrev = 0; _knockEl = 0; }
        return true;
    }
    float _knockEl = -1;

    // ------------------------------------------------------------------ active window and blade
    /// <summary>The swing's active window as fractions of it: its own data, else Hit minus / plus hitbox.activeBefore / activeAfter.</summary>
    static void ActiveWindow(AttackDef a, out float s, out float e)
    {
        s = a.Act0 >= 0 ? a.Act0 : a.Hit - Tune.F("hitbox.activeBefore", .1f);
        e = a.Act1 >= 0 ? a.Act1 : a.Hit + Tune.F("hitbox.activeAfter", .12f);
        if (a.Spin != 0 && a.Act1 < 0) e = Mathf.Max(e, Mathf.Min(.95f, a.Wind + (1 - a.Wind) / 1.4f));   // spinning attacks cut all the way round
        s = Mathf.Clamp(s, 0, .98f); e = Mathf.Clamp(e, s + .02f, 1f);
    }
    bool Thrust => W.Id is "rapier" or "spear" or "javelin" || (_step != null && _step.Anim is "thrust" or "dashSlash" or "charge" && W.Id != "unarmed");
    /// <summary>The swinging weapon's hitting segments now (world): the blade grip-to-tip (tip part only for thrusts), or both forearms for fists.</summary>
    int BladeSegs(Span<(Vector3 a, Vector3 b, float r)> o)
    {
        float rad = Tune.F("hitbox.bladeRadius", .07f) + (W.Heavy ? Tune.F("hitbox.heavyHeadExtra", .08f) : 0f) + Tune.F("hitbox.slack", .05f);
        if (W.Id == "unarmed" || _trail.Tip == null || _trail.Base == null || !IsInstanceValid(_trail.Tip))
        {
            var sk = _av?.Skel; if (sk == null) { var f = Facing(); var c = GlobalPosition + Vector3.Up * 1.3f; o[0] = (c, c + f * .75f, Tune.F("hitbox.fistRadius", .1f) + Tune.F("hitbox.slack", .05f)); return 1; }
            int n = 0;
            foreach (var (fa, hd) in new[] { ("lowerarm_l", "hand_l"), ("lowerarm_r", "hand_r") })
            {
                int b0 = sk.FindBone(fa), b1 = sk.FindBone(hd); if (b0 < 0 || b1 < 0) continue;
                var p0 = sk.GlobalTransform * sk.GetBoneGlobalPose(b0).Origin; var p1 = sk.GlobalTransform * sk.GetBoneGlobalPose(b1).Origin;
                o[n++] = (p0.Lerp(p1, .5f), p1 + (p1 - p0).Normalized() * Tune.F("hitbox.knuckles", .1f), Tune.F("hitbox.fistRadius", .1f) + Tune.F("hitbox.slack", .05f));
            }
            return n;
        }
        var grip = _trail.Base.GlobalPosition; var tip = _trail.Tip.GlobalPosition; var d = tip - grip; float len = d.Length();
        if (len > .01f)
        {
            float ext = len * Game.TS["reach"] / 100f + (Game.TS.Has("cleave") && (_atkPower || (_atk?.Finisher ?? false)) ? Tune.F("hitbox.cleaveExtra", .4f) : 0f);
            tip += d / len * ext;   // reach passives lengthen the blade's cut
            grip -= d / len * Tune.F("hitbox.gripBack", .08f);   // the hilt end of the blade's edge
            if (Thrust) grip = grip.Lerp(tip, 1f - Tune.F("hitbox.thrustTip", .35f));
        }
        o[0] = (grip, tip, rad); return 1;
    }

    // ------------------------------------------------------------------ the sweep
    bool _swOn, _swEnded, _swHas; int _swN; readonly (Vector3 a, Vector3 b, float r)[] _swPrev = new (Vector3, Vector3, float)[2];
    readonly HashSet<ulong> _swHit = new(); int _swHits; bool _swWall, _swPdOn;
    float _swMult, _swStun, _swUp, _swKnock; bool _swFin;
    Enemy? _critTgt; float _critMul = 1; string _critKind = "";
    float _hpT, _hpMul = 1;   // the attack timeline's own hit pause (kept in step with the model's)

    void BeginSweep(AttackDef a)
    {
        var TS = Game.TS; _swOn = true; _swEnded = false; _swHas = false; _swHit.Clear(); _swHits = 0; _swWall = false;
        _swFin = a.Finisher;
        _swMult = a.Dmg * (_swFin ? 1f + TS["finisherP"] / 100f : 1f) * (_atkPower ? 1f + TS["powerP"] / 100f : 1f);
        _swStun = a.Stun + (_swFin ? TS["finisherStun"] : 0f); _swUp = a.Up + (_swFin ? TS["finisherUp"] : 0f); _swKnock = a.Knock * (1f + TS["knockP"] / 100f);
        _swPdOn = _pdT > 0; if (_swPdOn) _swMult *= Tune.F("perfectDodge.counterMul", 1.5f);
        // a critical attack (riposte / backstab) is a locked blow: it lands on its target at the start of the window if still close
        if (_critTgt != null && IsInstanceValid(_critTgt) && !_critTgt.Dead && _critTgt.GlobalPosition.DistanceTo(GlobalPosition) - _critTgt.Def.R * _critTgt.ScaleF() <= Tune.F("parry.riposteRange", 2.2f) + .6f)
        {
            var e = _critTgt; HitEnemy(e, e.GlobalPosition + Vector3.Up * e.HeightM() * .55f + (GlobalPosition - e.GlobalPosition).Normalized() * e.Def.R * e.ScaleF()); _swHit.Add(e.GetInstanceId());
        }
        SweepTick();
    }

    /// <summary>One physics frame of the active window: sweep the blade from last frame's place to this frame's against every foe near.</summary>
    void SweepTick()
    {
        if (!_swOn || _swEnded) return;
        System.Span<(Vector3 a, Vector3 b, float r)> cur = stackalloc (Vector3, Vector3, float)[2];
        int n = BladeSegs(cur);
        // the world stops the blade: grip -> tip ray against solid ground and walls (layer 1)
        for (int i = 0; i < n; i++)
            if (Hitbox.WallCut(this, cur[i].a, cur[i].b, out var nrm, GetRid()) is { } wp)
            {
                cur[i].b = wp;
                if (!_swWall)
                {
                    _swWall = true; SpellFx.Play("hit_spark", wp, .55f); Sfx.Play("block", .45f, 1.5f);
                    bool heavy = W.Heavy || W.Hands > 1 || _atkPower;
                    if (heavy && Mathf.Abs(nrm.Y) < .5f && _swHits == 0 && Tune.F("hitbox.wallRecoil", 1) > .5f)
                    {   // a heavy blade bites into stone: the swing rebounds (a short flinch) and ends
                        _swEnded = true; _atkT = 0; _stagger = Mathf.Max(_stagger, Tune.F("hitbox.wallRecoilTime", .35f)); _hurtKind = "hit"; _hurtSerial++;
                        Rig?.Shake(.12f); Inputs.Rumble(.4f, .3f, .12f); HitboxDebug.Sphere(wp, .12f, new Color(1, .5f, .1f), .6f); return;
                    }
                }
            }
        if (!_swHas) { for (int i = 0; i < n; i++) _swPrev[i] = cur[i]; _swN = n; _swHas = true; }
        var red = new Color(1f, .25f, .2f);
        for (int i = 0; i < n && i < _swN; i++) { HitboxDebug.Seg(cur[i].a, cur[i].b, red); HitboxDebug.Seg(_swPrev[i].b, cur[i].b, red with { A = .6f }); }
        var center = GlobalPosition;
        foreach (var node in GetTree().GetNodesInGroup("enemies"))
        {
            if (node is not Enemy e || e.Dead || !e.Active || _swHit.Contains(e.GetInstanceId())) continue;
            if (e.GlobalPosition.DistanceSquaredTo(center) > 100f + e.Def.R * e.Def.R * 4f) continue;
            var body = e.HurtBody();
            for (int i = 0; i < n && i < _swN; i++)
                if (Hitbox.Sweep(_swPrev[i].a, _swPrev[i].b, cur[i].a, cur[i].b, cur[i].r, body, out var at)) { _swHit.Add(e.GetInstanceId()); HitEnemy(e, at); break; }
        }
        if (_swHits == 0)
            foreach (var node in GetTree().GetNodesInGroup("npcs"))
            {   // F-32: a blow that lands on a villager (and on no monster: a fight in the street is not an assault) is a crime (Crime.cs)
                if (node is not Npc npc || npc.Down || !npc.IsVisibleInTree() || _swHit.Contains(npc.GetInstanceId()) || npc.GlobalPosition.DistanceSquaredTo(center) > 64f) continue;
                var body = new Hitbox.Body(npc.GlobalPosition, 1.8f, .4f);
                for (int i = 0; i < n && i < _swN; i++)
                    if (Hitbox.Sweep(_swPrev[i].a, _swPrev[i].b, cur[i].a, cur[i].b, cur[i].r, body, out var at))
                    { _swHit.Add(npc.GetInstanceId()); Crime.Struck(npc, Game.D.WpnDmg() * _swMult, this); SpellFx.Play("hit_spark", at, .8f); break; }
            }
        for (int i = 0; i < n; i++) _swPrev[i] = cur[i]; _swN = n;
    }

    /// <summary>The active window closed: sounds, finisher flourish, wear.</summary>
    void EndSweep()
    {
        if (!_swOn) return; _swOn = false; var w = W;
        if (_swHits > 0) { Durability.WeaponHit(SwingItem); Rig?.Shake(_atkPower ? Tune.F("heavy.shake", .32f) : _swFin ? 0.15f : 0.08f); if (_atkPower) Fx.Dust(GlobalPosition + Facing() * 1.2f, Tune.F("heavy.dust", 1.4f)); }
        else if (!_swWall) Sfx.Weapon(w.Id, "miss");   // missing means missing: the swing cut only air
        if (_swFin && _swHits > 0 && !_atkPower)
        {   // finisher lands: gold shockwave + dust + low thump
            var gp = GlobalPosition + Vector3.Up * .08f;
            Vfx.Ring(gp, Mathf.Max(2f, (_atk?.Range ?? 3f) * Feel.ComboF("ringScale", 1.1f)), new Color(1f, .82f, .4f), Feel.ComboF("ringTime", .45f));
            Fx.Dust(GlobalPosition + Facing() * 1f, Feel.ComboF("dust", 1.2f)); Rig?.Shake(Feel.ComboF("shake", .22f));
            Sfx.Weapon(w.Id, "hit", Feel.ComboF("thumpPitch", .75f), .8f);
            if (_allPerfect && _combo > 0) FloatText.Spawn(this, GlobalPosition + new Vector3(0, 2.4f, 0), "Rhythm", new Color(1f, .85f, .4f), 1f);
        }
        _critTgt = null; _critKind = "";
    }

    /// <summary>One foe struck by the blade at a point: damage, poise, statuses, sparks there, hit-stop and the model's bite.</summary>
    void HitEnemy(Enemy e, Vector3 at)
    {
        var D = Game.D; var w = W; var a = _atk!;
        if (_swPdOn && _pdT > 0) { _pdT = 0; Sfx.Play("block", 1.2f); }
        bool critAtk = _critTgt == e;
        bool crit = critAtk || GD.Randf() < D.CritChance() + (w.Crit + a.CritBonus) / 100f;
        float mult = _swMult * (critAtk ? _critMul : 1f);
        float dmg = SwingDmg * (float)GD.RandRange(0.9, 1.1) * mult * (crit ? D.CritMult() : 1f) * (critAtk ? 1f : RiposteMul());
        var d = e.GlobalPosition - GlobalPosition; d.Y = 0;
        var hi = new HitInfo { Damage = dmg, From = GlobalPosition, Knock = _swKnock, Stun = _swStun, Up = _swUp, Crit = crit, Attacker = this, IsPlayer = true, Kind = _atkPower ? "power" : _swFin ? "combo" : "light",
            Poise = PoiseDmg(w, critAtk), Critical = critAtk, At = at };
        foreach (var kv in w.OnHit) hi.With(kv.Key, kv.Value * (_swFin || _atkPower ? 1.5f : 1f));
        if (GearFx.Get(SwingElem) is { } el && el.Status != "") hi.With(el.Status, el.Proc * (_swFin || _atkPower ? 1.5f : 1f) * (_step?.ElemMul ?? 1f));
        if (_step != null) foreach (var (sid, ch) in _step.Status) hi.With(sid, ch);
        e.TakeHit(hi); if (crit) TriggerAuto("crit");
        if (critAtk) FloatText.Spawn(this, GlobalPosition + new Vector3(0, 2.4f, 0), _critKind == "backstab" ? "Backstab!" : "Riposte!", new Color(1f, .8f, .35f), 1.3f);
        bool heavy = w.Heavy || _atkPower || _swFin || critAtk;
        Feel.Impact(dmg, e.MaxHp, heavy, crit, e.Dead, new Vector3(d.X, 0, d.Z), Rig);
        // the weapon bites: the swing (model and timeline together) slows for a moment, longer for heavier weapons (works online too)
        float wt = (w.Hands > 1 ? Tune.F("feel.pauseTwoHanded", 1.6f) : w.Light ? Tune.F("feel.pauseLight", .6f) : 1f) * (heavy ? Tune.F("feel.pauseHeavyMul", 1.3f) : 1f);
        float pt = Tune.F("feel.pauseTime", .055f) * wt, pm = Tune.F("feel.pauseSlow", .12f);
        _hpT = Mathf.Max(_hpT, pt); _hpMul = pm; _av?.HitPause(pt, pm);
        float big = crit ? 1.6f : _swFin || _atkPower ? 1.4f : 1f;
        SpellFx.Play("hit_spark", at, big); HitboxDebug.Sphere(at, .1f, Colors.White, .5f);
        if (crit && d.LengthSquared() > .001f) CritFx(e, at, d.Normalized());
        if (_swHits == 0) Sfx.Weapon(w.Id, "hit");
        _swHits++;
        if ((_atkPower || _swFin) && (a.Up > 0 || a.Arc >= 180 || _swStun > .5f) && _swHits == 1) Vfx.Ring(GlobalPosition + Vector3.Up * .08f, Mathf.Max(1.5f, a.Range * .9f), w.Trail, .35f);
    }

    /// <summary>Poise damage of the hero's swing (data/tuning.json game.poise.weapon.&lt;class&gt;, then by attack type).</summary>
    float PoiseDmg(WeaponDef w, bool crit)
    {
        if (crit) return Tune.F("poise.critical", 999f);
        float b = Tune.F("poise.weapon." + w.Id, w.Id == "unarmed" ? 5f : w.Hands > 1 ? 26f : w.Light ? 7f : w.Heavy ? 18f : 12f);
        b *= _atkPower ? Tune.F("poise.powerMul", 1.8f) : _swFin ? Tune.F("poise.finisherMul", 1.4f) : 1f;
        if (OffNow) b *= Tune.F("poise.offHandMul", .7f);
        return b;
    }

    // ------------------------------------------------------------------ the body carried through the swing (root motion / step curve)
    /// <summary>Forward travel of the swing this frame (m/s along the facing): the clip's root motion where the clip has it, else the data step curve.</summary>
    Vector3 SwingStep(float prog, float dt)
    {
        if (_atk == null || Ranged || dt <= 0) return Vector3.Zero;
        var w = W; ActiveWindow(_atk, out _, out float e1);
        float dist = _atk.Step >= 0 ? _atk.Step : _atkPower ? Tune.F("attack.stepPower", .8f) : _atk.Finisher ? Tune.F("attack.stepFinisher", .6f) : Tune.F("attack.stepLight", .4f);
        dist *= w.Hands > 1 ? Tune.F("attack.stepTwoHandedMul", 1.2f) : w.Light ? Tune.F("attack.stepLightWeaponMul", .7f) : 1f;
        float f;
        if (_av != null && _av.Cur.StartsWith("atk_") && _av.RootAt(_av.Cur, _av.GaitPhase, out float tr, out float rd))
        {   // a root-motion clip: its own timing, its own distance scaled by data (capped)
            f = tr; dist = Mathf.Min(rd * Tune.F("rm.attackScale", 1f), Tune.F("rm.attackMax", 2.5f));
        }
        else { float u = Mathf.Clamp((prog - _atk.Wind * .5f) / Mathf.Max(.05f, e1 - _atk.Wind * .5f), 0, 1); f = u * u * (3 - 2 * u); }
        float df = Mathf.Max(0, f - _rmPrev); _rmPrev = f;
        // keep the measure: with a target, the step carries the body to where the weapon's strong part meets it (MeasureGap), and backs
        // off when already closer (it used to stop at a fixed 0.7 m for every weapon, so later swings landed with the fist and the hilt)
        if (_magnet != null && IsInstanceValid(_magnet))
        {
            var dm = _magnet.GlobalPosition - GlobalPosition; dm.Y = 0; float edge = dm.Length() - _magnet.Def.R * _magnet.ScaleF();
            dist = Mathf.Clamp(edge - MeasureGap(), -dist * Tune.F("attack.stepBackMul", 1f), dist);
        }
        var fw = new Vector3(-Mathf.Sin(_spinBase), 0, -Mathf.Cos(_spinBase));
        return fw * (df * dist / dt);
    }

    // ------------------------------------------------------------------ critical attacks
    /// <summary>A foe open to a critical blow right now: a parried humanoid in front (riposte), or a humanoid not hunting us, seen from behind (backstab).</summary>
    Enemy? CritTarget(out string kind)
    {
        kind = ""; Enemy? best = null; float bd = 99f; var me = GlobalPosition; float camYaw = Rig?.Yaw ?? _modelYaw;
        foreach (var node in GetTree().GetNodesInGroup("enemies"))
        {
            if (node is not Enemy e || e.Dead || !e.Active || !e.Humanoid) continue;
            var d = e.GlobalPosition - me; d.Y = 0; float dist = d.Length() - e.Def.R * e.ScaleF(); if (dist > bd) continue;
            if (Mathf.Abs(e.GlobalPosition.Y - me.Y) > 1.2f) continue;
            float look = Mathf.Abs(Util.AngDiff(Targeting.Aim == e ? Util.YawTo(d.X, d.Z) : camYaw, Util.YawTo(d.X, d.Z)));
            if (e.RiposteOpen && dist <= Tune.F("parry.riposteRange", 2.2f) && look < Mathf.DegToRad(Tune.F("parry.riposteCone", 60f))) { best = e; bd = dist; kind = "riposte"; continue; }
            if (!e.Hunting && dist <= Tune.F("backstab.range", 1.6f) && look < 1.1f)
            {
                var ef = new Vector3(-Mathf.Sin(e.Yaw), 0, -Mathf.Cos(e.Yaw)); var toMe = (-d).Normalized();
                if (ef.Dot(toMe) < -Mathf.Cos(Mathf.DegToRad(Tune.F("backstab.cone", 50f)))) { best = e; bd = dist; kind = "backstab"; }
            }
        }
        return best;
    }

    // ------------------------------------------------------------------ small helpers
    bool _bsFresh; float _torsoYaw, _deathRate = 1f, _deathBlend = .15f, _lastFwd, _accF;
    /// <summary>How far the swinging weapon reaches from the body now (m): arm plus the blade's real length.</summary>
    /// <summary>
    /// The measure (hitbox.measureBase + measureK x the weapon's length, fist to tip): how far from a foe's edge the hero stands so the
    /// swing meets it with the blade's percussion point (the last third), not the hilt. AnimeTool measured: a sword at 0.94 m met with
    /// 88 % of its length, a greatsword's wide swing at 1.24 m with 65 %, a spear's thrust at 1.27 m with 63 %.
    /// </summary>
    float MeasureGap()
    {
        float len = 0;
        if (_av?.BladeTip != null && IsInstanceValid(_av.BladeTip) && _av.HandPos(false) is Vector3 hp && W.Id != "unarmed") len = hp.DistanceTo(_av.BladeTip.GlobalPosition);
        else if (_trail.Tip != null && _trail.Base != null && IsInstanceValid(_trail.Tip) && W.Id != "unarmed") len = _trail.Tip.GlobalPosition.DistanceTo(_trail.Base.GlobalPosition) / .7f;
        return Tune.F("hitbox.measureBase", .2f) + Tune.F("hitbox.measureK", .55f) * len;
    }
    float ReachNow()
    {
        float blade = _trail.Tip != null && _trail.Base != null && IsInstanceValid(_trail.Tip) && W.Id != "unarmed" ? _trail.Tip.GlobalPosition.DistanceTo(_trail.Base.GlobalPosition) : 0f;
        return Tune.F("hitbox.armReach", .6f) + blade;
    }
    /// <summary>A swing may be rolled out of only after its active window (data attack.rollCancelAfter, a fraction of the swing past it).</summary>
    bool RollCancelOk()
    {
        if (_atk == null || _atkDur <= 0) return false; if (_atkSkill != null || Ranged) return true;
        ActiveWindow(_atk, out _, out float e); return 1f - _atkT / _atkDur > e + Tune.F("attack.rollCancelAfter", 0f);
    }
    /// <summary>The procedural life layer's inputs (HeroPose): forward acceleration, speed, overspeed, lock-on twist, bend toward a low foe.</summary>
    void LifeLayer(float dt)
    {
        if (_av == null || PoseLayer() is not { } pl) return;
        var f = Facing(); float fwd = new Vector2(Velocity.X, Velocity.Z).Dot(new Vector2(f.X, f.Z));
        bool walking = IsOnFloor() && _rollT <= 0 && _bsT <= 0 && _atkT <= 0 && !Mounted && !Flying && !Swimming;
        _accF = Mathf.Lerp(_accF, walking && dt > 0 ? (fwd - _lastFwd) / dt : 0f, 1f - Mathf.Exp(-12f * dt)); _lastFwd = fwd;
        pl.Accel = _accF; pl.Speed = new Vector2(Velocity.X, Velocity.Z).Length(); pl.Over = walking ? _av.OverSpeed : 0f;
        pl.TorsoYaw = Targeting.Current != null && walking && !_backpedal ? _torsoYaw : 0f; if (Targeting.Current == null) _torsoYaw = 0;
        float bend = 0;
        if (_atkT > 0 && !Ranged && _magnet != null && IsInstanceValid(_magnet))
        {   // a low foe (a wolf, a goblin): the back bends so the blade comes down to it
            var m = _magnet; float mid = m.GlobalPosition.Y + m.HeightM() * .5f, chest = GlobalPosition.Y + Tune.F("hitbox.bendChest", 1.25f);
            float dist = Mathf.Max(.5f, new Vector2(m.GlobalPosition.X - GlobalPosition.X, m.GlobalPosition.Z - GlobalPosition.Z).Length());
            bend = Mathf.Clamp(Mathf.Atan2(mid - chest, dist), -Tune.F("hitbox.bendMax", .45f), Tune.F("hitbox.bendUp", .15f));
        }
        pl.MeleePitch = bend;
        // the tucked roll and the arms in the air (HeroPose.TuckRoll / AirArms)
        pl.Roll = _rollT > 0 && _av.HasClip("rollBase") ? Mathf.Clamp(_rollEl / Mathf.Max(.05f, RollTime), 0, 1) : -1f;
        pl.Armed = Game.D.Weapon != null && !W.Ranged && W.Id != "unarmed";
        // both hands on the weapon when the off hand is free (no shield, no second weapon), fighting, on the ground and not running hard
        bool freeOff = Game.D.Eq.GetValueOrDefault("shield") == null && !OffNow;
        bool twoOk = pl.Armed && freeOff && _av.OffGrip != null && IsOnFloor() && _rollT <= 0 && _bsT <= 0 && _stagger <= 0 && !Mounted && !Swimming && _castT <= 0 && (InCombat || _recT > 0);
        float run = new Vector2(Velocity.X, Velocity.Z).Length();
        pl.OffGrip = _av.OffGrip; pl.TwoHand = twoOk ? Mathf.Clamp(1f - (run - Tune.F("anim.twoHandRunFrom", 1.5f)) / 2f, 0, 1) : 0f;
        pl.Air = !IsOnFloor() && _airT > .06f && !Flying && !Swimming && !Mounted && _rollT <= 0 && _atkT <= 0 && _castT <= 0 ? 1f : 0f;
    }

    // ------------------------------------------------------------------ backstep
    void StartBackstep()
    {
        _bsDur = Tune.F("backstep.time", .38f); _bsT = _bsDur; _bsEl = 0; _bsDir = -Facing(); _rollCd = Tune.F("backstep.cooldown", .35f); _recT = 0; _recPending = 0;
        UseStamina(Tune.F("backstep.stamina", 9f) * RollCostMul); Fx.Dust(GlobalPosition, .3f); Sfx.Play("swing", .6f, 1.2f);
        NetAtkAnim = Avatar.AttackIndex("backstep"); NetAtkHit = _bsDur * .5f; _atkSerial++;   // friends see the hop back (an attack animation slot)
    }
    /// <summary>The backstep's motion this frame: a quick hop back that eases out (data backstep.dist metres).</summary>
    void BackstepTick(float dt, ref Vector3 v)
    {
        _bsEl += dt; _bsT -= dt; float u0 = Mathf.Clamp((_bsEl - dt) / _bsDur, 0, 1), u1 = Mathf.Clamp(_bsEl / _bsDur, 0, 1);
        static float E(float u) => 1f - (1f - u) * (1f - u);   // fast out, easing to a stop
        float sp = (E(u1) - E(u0)) * Tune.F("backstep.dist", 1.7f) / Mathf.Max(dt, .001f);
        v.X = _bsDir.X * sp; v.Z = _bsDir.Z * sp;
    }

    /// <summary>The roll's speed this frame: Roll_RM's own travel timing where the clip carries it, else even; roll.dist metres in all.</summary>
    float RollSpeedNow(float dt)
    {
        float len = RollTime, dist = Tune.F("roll.dist", 4.6f), u = Mathf.Clamp(_rollEl / len, 0, 1);
        float f = u * (2 - u) * .35f + u * .65f;   // a little faster out of the crouch, then even
        if (_av != null && _av.RootAt(Avatar.Clip("rollRM"), u, out float tr, out _)) f = tr;
        float df = f - _rmPrev; _rmPrev = f; return Mathf.Max(0, df) * dist / Mathf.Max(dt, .001f);
    }
}
