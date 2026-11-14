using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>
/// The Shadow Duel boss (Crimson Arena): a dark mirror of the hero. It wears the hero's body, gear and weapon, swings the hero's own
/// basic-attack chain, and takes its numbers from the hero (health, damage against the hero's armour, attack speed, crit), growing each round.
/// Fought like a souls boss:
///  - moves with readable tells: chained combos (some with a delayed last swing), a slow heavy blow that staggers and stuns,
///    a lunge, shadow bolts, and from phase 2 a leap slam that marks where it lands;
///  - stance: damage and parries wear it down; when it breaks the Shadow kneels and takes extra damage;
///  - phases at 60% and 30% health: a roar, then the Eclipse, a damage check: break its barrier in time or take a huge blast
///    (a well-timed roll still avoids it) while it heals. Phase 3 is faster and its heavy blows send out shockwaves;
///  - a brain that reads the hero: it sidesteps swings and punishes the opening, punishes casting, potions and heavy recoveries,
///    crushes a raised guard with heavy blows, chases or shoots a hero who keeps away, delays a swing to catch a panic roll,
///    and does not repeat itself.
/// Numbers: data/tuning.json "game.duel".
/// </summary>
public partial class Enemy
{
    public bool Duelist; public int DuelRound = 1;
    enum DM { Idle, Strafe, Combo, Heavy, Lunge, Bolts, Leap, Dodge, Recover, Roar, Eclipse, Broken, Staggered }
    DM _dm = DM.Idle, _dmLast = DM.Idle, _dmLast2 = DM.Idle;
    float _mt, _mDur, _wind, _think = 1.2f, _dodgeCd = 2f, _iframes, _farT, _pBlockT, _healSeen, _punishT, _lastPHp = -1, _overlayT;
    float _stance, _stanceMax = 1, _stanceIdle, _barrier, _barrierMax = 1, _eclipseDur, _dk, _aspd = 1, _crit;
    int _phase = 1, _hitN, _hitI, _boltsLeft; bool _hitDone, _delayed, _delayUsed, _pAtkPrev, _toEclipse; string _wt = "sword";
    Vector3 _dashTo, _leapFrom, _leapTo, _dodgeDir; float _strafeSign = 1;
    readonly List<string> _chainAnims = new(); string _heavyAnim = "bigSwing";
    ShaderMaterial? _shadowMat; SwordTrail? _dTrail; MeshInstance3D? _eclipseOrb;

    /// <summary>For the HUD: stance (0..1), the Eclipse barrier (0..1, -1 when none) and the phase.</summary>
    public float DuelStance => Duelist ? Mathf.Clamp(_stance / _stanceMax, 0, 1) : -1;
    public float DuelBarrier => Duelist && _dm == DM.Eclipse ? Mathf.Clamp(_barrier / _barrierMax, 0, 1) : -1;
    public int DuelPhase => _phase;
    public bool DuelBroken => _dm == DM.Broken;
    /// <summary>For the tests: what it is doing now.</summary>
    public string DuelMove => _dm.ToString();
    public bool DuelMirrorsHero => _av != null && _wt == Game.D.WType();
    public Avatar? Av => _av;

    static float T(string k, float d) => Tune.F("duel." + k, d);

    /// <summary>The Shadow's numbers for a round, taken from the hero now (also used by the tests).</summary>
    public static (float hp, float dmgK, float speed, float windMul) DuelStats(int round)
    {
        var D = Game.D; int r = Mathf.Max(1, round) - 1;
        float hp = Mathf.Max(D.MaxHp() * T("hpPerHeroHp", 2.4f), D.WpnDmg() * T("hpPerWeaponHit", 42f)) * (1 + T("hpPerRound", .3f) * r);
        // damage is measured against the hero's own armour (half credit), so gear helps without making the mirror harmless
        float armor = Progression.ArmorTaken(D.Defense(), D.Level + r);
        float dmgK = D.MaxHp() * (1 + T("damagePerRound", .1f) * r) / Mathf.Sqrt(Mathf.Max(.05f, armor));
        float speed = Mathf.Min(T("maxSpeed", 7.6f), T("speed", 5.6f) * (1 + T("speedPerRound", .03f) * r));
        float wind = Mathf.Max(T("minWindMul", .72f), 1 - T("quickerPerRound", .03f) * r);
        return (hp, dmgK, speed, wind);
    }

    /// <summary>The definition the arena spawns: the hero's name, a boss, no loot.</summary>
    public static EnemyDef DuelDef(int round)
    {
        var D = Game.D; var s = DuelStats(round); string wt = D.WType(); var w = Weapons.Get(wt);
        float reach = w.Ranged ? 2.6f : Mathf.Clamp((w.Combo.Count > 0 ? w.Combo[0].Range : 2.4f) + .3f, 2.3f, 3.6f);
        return new EnemyDef { N = $"Shadow of {D.Name}", Kind = "hum", Boss = true, NoLoot = true, Hp = s.hp, Dmg = s.dmgK * .1f, Spd = s.speed, Aggro = 99, Reach = reach + T("sizeReach", .6f), Wind = .5f, Cd = 1f, Xp = 0, R = .9f, Scale = T("size", 2f), Color = 0x3a1a5a, Realm = 0 };
    }

    // ------------------------------------------------------------------ build
    void BuildDuelist()
    {
        var D = Game.D; var s = DuelStats(DuelRound); _dk = s.dmgK; _aspd = Mathf.Clamp(Mathf.Sqrt(D.AtkSpeed()), .85f, 1.4f) / s.windMul; _crit = Mathf.Clamp(D.CritChance(), 0, .4f);
        _stanceMax = MaxHp * T("stance", .14f); _wt = D.WType();
        // the hero's body and gear
        var ap = Races.Look(D);
        // twice the hero's size (duel.size)
        _av = new Avatar { Name = "Avatar", Scale = Vector3.One * _scale }; _body.AddChild(_av); _av.Build(ap, D.Name + "_shadow");
        _av.Dress(sl => D.Eq.GetValueOrDefault(sl), _wt, Classes.Get(D.Class).Tunic);
        // the hero's own chain of swings and power attack (data/attacks.json)
        var w = Weapons.Get(_wt);
        if (Attacks.HasStyle(_wt)) { var b = Attacks.Build(_wt); for (int i = 0; i < b.Chain.Count; i++) { var a = Attacks.Resolve(w, i, false).Anim; if (a != "") _chainAnims.Add(a); } var pw = Attacks.Resolve(w, 0, true).Anim; if (pw != "") _heavyAnim = pw; }
        if (_chainAnims.Count == 0) _chainAnims.AddRange(new[] { "slashA", "slashB", "thrust" });
        // a shadow over all of it: a near-black body with a violet rim, flushed red on its strongest tells
        _shadowMat = new ShaderMaterial { Shader = new Shader { Code = @"shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_back, shadows_disabled, fog_disabled;
uniform vec4 rim : source_color = vec4(.62, .32, 1.0, 1.0);
uniform vec4 core : source_color = vec4(.04, .015, .08, 1.0);
uniform vec4 tell : source_color = vec4(1.0, .18, .08, 1.0);
uniform float glow = 0.0;
void fragment() {
	float f = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 2.2);
	vec3 c = mix(core.rgb, rim.rgb * 2.2, f);
	c = mix(c, tell.rgb * 2.5, glow * (.35 + .65 * f));
	ALBEDO = c; ALPHA = clamp(.62 + .38 * f + glow * .2, 0.0, 1.0);
}" } };
        ShadowOver(_av);
        _dTrail = new SwordTrail { Col = new Color(.65f, .35f, 1f) }; AddChild(_dTrail);
        if (_av.BladeTip != null) { _dTrail.Base = _av.BladeBase; _dTrail.Tip = _av.BladeTip; }
        AddChild(new OmniLight3D { LightColor = new Color(.6f, .3f, 1f), OmniRange = 5f * _scale, LightEnergy = 1.2f, Position = new Vector3(0, 1.4f * _scale, 0), ShadowEnabled = false });
    }
    void ShadowOver(Node n) { foreach (var c in n.GetChildren()) { if (c is GeometryInstance3D g && g.MaterialOverlay == null) g.MaterialOverlay = _shadowMat; ShadowOver(c); } }
    void Tell(float k) => _shadowMat?.SetShaderParameter("glow", Mathf.Clamp(k, 0, 1));

    // ------------------------------------------------------------------ hits on the Shadow
    /// <summary>From TakeHit: dodges, the barrier, the broken-stance bonus and stance damage. False = the hit does no health damage.</summary>
    bool DuelHit(HitInfo h, ref int dmg)
    {
        var top = GlobalPosition + new Vector3(0, HeightM() + .5f, 0);
        if (_iframes > 0) { FloatText.Spawn(this, top, "Evaded", new Color(.75f, .6f, 1f), .5f); return false; }
        if (_dm == DM.Roar) { FloatText.Spawn(this, top, "Immune", new Color(.75f, .6f, 1f), .5f); return false; }
        if (_dm == DM.Eclipse)
        {
            _barrier -= dmg; _flash = .08f; FloatText.Spawn(this, top, dmg.ToString(), new Color(.8f, .55f, 1f), 1f);
            if (_barrier <= 0) { Ui.Banner("Eclipse broken", "strike now!"); Sfx.Play("block"); Vfx.Sparks(GlobalPosition + Vector3.Up, Vfx.Ramp((0, Colors.White), (1, new Color(.6f, .3f, 1f, 0))), 40, 9f, .8f, .5f, -4f, true, 180f, null, .5f, "star"); Begin(DM.Broken, T("eclipseBrokenTime", 4.5f)); }
            return false;
        }
        if (_dm == DM.Broken) { dmg = Mathf.RoundToInt(dmg * T("brokenTaken", 1.35f)); return true; }
        _stance += dmg * (h.Kind == "power" ? 1.6f : 1f); _stanceIdle = 0;
        if (_stance >= _stanceMax && _dm != DM.Leap) StanceBreak(T("brokenTime", 3f));
        return true;
    }
    /// <summary>From Stun (a parry): a parried Shadow loses stance and reels.</summary>
    void DuelParried(float t)
    {
        if (_dm is DM.Eclipse or DM.Roar or DM.Leap or DM.Broken) return;
        _stance += _stanceMax * T("parryStance", .4f); _stanceIdle = 0;
        FloatText.Spawn(this, GlobalPosition + new Vector3(0, HeightM() + .5f, 0), "Parried", new Color(1f, .88f, .5f), .6f);
        if (_stance >= _stanceMax) StanceBreak(T("brokenTime", 3f)); else Begin(DM.Staggered, Mathf.Max(.6f, t));
    }
    void StanceBreak(float t)
    {
        _stance = 0; Ui.Note("Its stance breaks: strike now!", new Color(1f, .85f, .4f)); Sfx.Play("block");
        FloatText.Spawn(this, GlobalPosition + new Vector3(0, HeightM() + .6f, 0), "STANCE BROKEN", new Color(1f, .8f, .3f), .75f);
        Begin(DM.Broken, t);
    }

    // ------------------------------------------------------------------ the brain
    float Q => (_phase == 1 ? 1f : _phase == 2 ? .86f : .74f) / _aspd;   // wind-up scale: later phases, later rounds and a faster hero all quicken it
    Vector3 Fwd => new(-Mathf.Sin(_yaw), 0, -Mathf.Cos(_yaw));
    void Begin(DM m, float dur = 0)
    {
        if (m is DM.Combo or DM.Heavy or DM.Lunge or DM.Bolts or DM.Leap) { _dmLast2 = _dmLast; _dmLast = m; }
        _dm = m; _mt = 0; _mDur = dur; _hitDone = false; _delayUsed = false; Tell(0); if (_dTrail != null) _dTrail.Emit = false;
        switch (m)
        {
            case DM.Combo:
                _hitN = 2 + (_phase >= 2 ? 1 : 0) + (GD.Randf() < .35f ? 1 : 0); _hitI = 0; _delayed = _phase >= 2 && GD.Randf() < .4f; _wind = .55f * Q; SwingAnim(); break;
            case DM.Heavy: _wind = 1.05f * Q; _av?.PlayAttack(_heavyAnim, _wind, true); break;
            case DM.Lunge: _wind = .5f * Q; _av?.PlayAttack("dashSlash", _wind + .25f, true); break;
            case DM.Bolts: _wind = .65f * Q; _boltsLeft = _phase >= 3 ? 2 : 1; _av?.Play("cast", .08f, _wind, true); break;
            case DM.Leap: _wind = .38f * Q; _av?.PlayAttack("castSlam", _wind + .85f, true); break;
            case DM.Dodge: _av?.Play("roll", .05f, .45f, true); break;
            case DM.Recover: break;
            case DM.Roar: _av?.Play("cast", .1f, 1.6f, true); break;
            case DM.Eclipse: _av?.Play("channel", .2f); break;
            case DM.Broken: case DM.Staggered: _av?.Play("hit", .05f, 0, true); break;
        }
    }
    void SwingAnim() { _av?.PlayAttack(_chainAnims[_hitI % _chainAnims.Count], _wind, true); }

    /// <summary>One frame of the Shadow (from _PhysicsProcess, after statuses).</summary>
    void DuelTick(float dt, Node3D? pl, Vector3 v)
    {
        if (CurrentBoss != this) CurrentBoss = this;
        var p = pl as Player;
        if (_overlayT < 3f) { _overlayT += dt; if (_av != null) { ShadowOver(_av); if (_dTrail != null && _dTrail.Tip == null && _av.BladeTip != null) { _dTrail.Base = _av.BladeBase; _dTrail.Tip = _av.BladeTip; } } }   // gear models that loaded late
        _mt += dt; _think -= dt; _dodgeCd -= dt; _iframes -= dt; _healSeen -= dt; _punishT -= dt;
        // stance recovers once it has not been hit for a while
        _stanceIdle += dt; if (_stanceIdle > T("stanceRegenDelay", 3f) && _dm != DM.Broken) _stance = Mathf.Max(0, _stance - _stanceMax * T("stanceRegen", .1f) * dt);

        Vector3 toP = Vector3.Zero; float dist = 999f; bool ok = p != null && !p.Dead;
        if (ok) { toP = p!.GlobalPosition - GlobalPosition; toP.Y = 0; dist = toP.Length(); }
        float yawTo = ok ? Util.YawTo(toP.X, toP.Z) : _yaw;
        // what the hero is doing
        if (ok)
        {
            float maxHp = Game.D.MaxHp();
            if (_lastPHp >= 0 && p!.Hp - _lastPHp > maxHp * .1f) _healSeen = 1.2f;   // a potion or a heal: punish it
            _lastPHp = p!.Hp;
            _pBlockT = p.Blocking ? _pBlockT + dt : 0;
            _farT = dist > 9f ? _farT + dt : Mathf.Max(0, _farT - dt * 2);
        }
        bool pStartsSwing = ok && p!.Attacking && !_pAtkPrev; _pAtkPrev = ok && p!.Attacking;

        // phases: 60% and 30% health (not in the middle of a leap)
        if (_dm != DM.Leap && _dm != DM.Roar && _dm != DM.Eclipse)
        {
            if (_phase == 1 && Hp <= MaxHp * T("phase2At", .6f)) { _phase = 2; _toEclipse = true; Begin(DM.Roar); Ui.Banner(Def.N, "the shadow deepens"); Sfx.Play("roar"); }
            else if (_phase == 2 && Hp <= MaxHp * T("phase3At", .3f)) { _phase = 3; _toEclipse = true; Begin(DM.Roar); Ui.Banner(Def.N, "it will not fall"); Sfx.Play("roar"); }
        }

        Vector3 move = Vector3.Zero; float spd = Def.Spd * (_phase == 3 ? 1.12f : 1f);
        float reach = Def.Reach;
        switch (_dm)
        {
            case DM.Idle:
            case DM.Strafe:
            {
                if (!ok) break;
                Face(yawTo, 10f, dt);
                // read the hero: sidestep a swing that is about to land, then punish the opening
                float dodgeP = T("dodgeChance", .28f) + .1f * (_phase - 1);
                if (pStartsSwing && dist < reach + 1.8f && _dodgeCd <= 0 && GD.Randf() < dodgeP)
                {
                    var side = new Vector3(-toP.Z, 0, toP.X).Normalized() * (GD.Randf() < .5f ? 1 : -1);
                    _dodgeDir = (dist < 2.2f ? -toP.Normalized() * .7f + side * .5f : side).Normalized();
                    _iframes = .32f; _dodgeCd = T("dodgeCooldown", 3.2f) - .5f * (_phase - 1); _punishT = 1f; Begin(DM.Dodge, .45f); break;
                }
                if (_think <= 0) { Decide(p!, dist); break; }
                if (_dm == DM.Strafe)
                {
                    // circle at a fighting distance, edging in
                    var side = new Vector3(-toP.Z, 0, toP.X).Normalized() * _strafeSign; float want = reach + 1.6f;
                    move = (side * .8f + toP.Normalized() * Mathf.Clamp((dist - want) * .6f, -.6f, 1f)).Normalized() * spd * .5f;
                    if (_mt > _mDur) Begin(DM.Idle);
                }
                else if (dist > reach + .4f) move = toP.Normalized() * spd;   // close in
                break;
            }
            case DM.Combo:
            {
                // track hard through the wind-up, barely in its last moments (a well-timed sidestep beats it)
                Face(yawTo, _mt < _wind * .7f ? 14f : 2.5f, dt);
                if (_mt < _wind * .8f && dist > reach * .75f) move = toP.Normalized() * spd * .55f;
                bool last = _hitI == _hitN - 1;
                Tell(last && _delayed ? Mathf.Clamp(_mt / _wind, 0, 1) * .6f : 0);
                if (_dTrail != null) _dTrail.Emit = _mt > _wind * .6f;
                if (_mt >= _wind)
                {
                    Sfx.Play("swing", .8f);
                    if (ok && InArc(p!, reach, 115f)) HitHero(p!, last ? .1f : .07f, last ? 6f : 3f, last, last ? .3f : 0f);
                    _hitI++;
                    if (_hitI >= _hitN) { Begin(DM.Recover, .55f * Q); break; }
                    _mt = 0; _wind = (_hitI == _hitN - 1 && _delayed ? 1.05f : .34f) * Q; SwingAnim();   // the delayed last swing waits out a panic roll
                }
                break;
            }
            case DM.Heavy:
            {
                Face(yawTo, _mt < _wind * .75f ? 9f : 1.5f, dt);
                float k = _mt / _wind; Tell(k > .45f ? (k - .45f) / .55f : 0);
                if (k > .45f && k - dt / _wind <= .45f && _av?.BladeTip != null) { Vfx.Flash(_av.BladeTip.GlobalPosition, new Color(1f, .3f, .15f), 4f, 4f, .3f); Fx.Burst(_av.BladeTip.GlobalPosition, new Color(1f, .35f, .2f), 8, 2f, .35f, .1f); }   // the glint: a heavy is coming
                // a roll too early is caught: the blow waits for it to end (once)
                if (ok && p!.Rolling && !_delayUsed && k > .6f) { _wind += .32f; _delayUsed = true; }
                if (_dTrail != null) _dTrail.Emit = k > .7f;
                if (_mt >= _wind)
                {
                    Sfx.Play("swing", .6f); Player.Instance?.Rig?.Shake(.25f);
                    var at = GlobalPosition + Fwd * (reach * .8f); Vfx.Ring(at + Vector3.Up * .1f, 2.4f, new Color(.7f, .35f, 1f), .35f);
                    bool hit = ok && InArc(p!, reach + .8f, 140f);
                    if (hit) HitHero(p!, .22f, 9f, true, .8f);
                    if (_phase >= 3)
                    {
                        // phase 3: every heavy sends out a shockwave
                        Vfx.Ring(GlobalPosition + Vector3.Up * .1f, 7f, new Color(.75f, .4f, 1f), .45f); Vfx.GroundMark(GlobalPosition, "circle", 14f, new Color(.7f, .35f, 1f, .7f), .1f, .5f, true);
                        if (ok && !hit && dist < 7f) HitHero(p!, .09f, 7f, true, .3f, noBlock: true);
                    }
                    Begin(DM.Recover, .95f * Q);   // the big opening after a heavy
                }
                break;
            }
            case DM.Lunge:
            {
                if (_mt < _wind)
                {
                    Face(yawTo, 12f, dt); Tell(_mt / _wind * .3f);
                    var pv = ok ? new Vector3(p!.Velocity.X, 0, p.Velocity.Z) : Vector3.Zero;
                    _dashTo = ok ? p!.GlobalPosition + pv * .25f : GlobalPosition;
                    break;
                }
                var d = _dashTo - GlobalPosition; d.Y = 0; float dashT = _mt - _wind;
                if (_dTrail != null) _dTrail.Emit = true;
                if (d.Length() > 1.1f && dashT < .6f) move = d.Normalized() * T("lungeSpeed", 21f);
                if (!_hitDone && ok && dist < reach * .7f + .6f) { _hitDone = true; Sfx.Play("swing", .7f); HitHero(p!, .13f, 6f, true, .4f); }
                if (d.Length() <= 1.1f || dashT >= .6f) Begin(DM.Recover, .6f * Q);
                break;
            }
            case DM.Bolts:
            {
                Face(yawTo, 8f, dt);
                if (_mt >= _wind && ok)
                {
                    int n = _phase >= 2 ? 3 : 1; float spread = .26f;
                    var from = GlobalPosition + new Vector3(0, 1.4f * _scale, 0) + Fwd * (.6f * _scale);
                    for (int i = 0; i < n; i++)
                    {
                        float a = (i - (n - 1) / 2f) * spread; var to = p!.GlobalPosition + new Vector3(0, 1f, 0) + new Vector3(p.Velocity.X, 0, p.Velocity.Z) * (dist / 18f) * .6f;
                        var dir = (to - from).Normalized().Rotated(Vector3.Up, a);
                        var pr = Projectile.Spawn("ebolt", 'e', from, dir * 18f, _dk * .07f, 3f); pr.Source = this;
                    }
                    Sfx.Play("zap", .7f); _boltsLeft--;
                    if (_boltsLeft > 0) { _mt = 0; _wind = .35f * Q; _av?.Play("cast", .05f, _wind, true); }
                    else Begin(DM.Recover, .45f * Q);
                }
                break;
            }
            case DM.Leap:
            {
                if (_mt < _wind)
                {
                    Face(yawTo, 12f, dt);
                    if (ok) { var pv = new Vector3(p!.Velocity.X, 0, p.Velocity.Z); _leapTo = ClampArena(p.GlobalPosition + pv * .5f); }
                    _leapFrom = GlobalPosition;
                    if (_mt + dt >= _wind) Vfx.GroundMark(_leapTo, "circle", 8.5f, new Color(1f, .3f, .2f, .85f), .75f, .25f, true);   // where it will land
                    break;
                }
                float air = .85f, k = Mathf.Clamp((_mt - _wind) / air, 0, 1);
                var flat = _leapFrom.Lerp(_leapTo, k); flat.Y = Mathf.Lerp(_leapFrom.Y, _leapTo.Y, k) + Mathf.Sin(k * Mathf.Pi) * 4.5f;
                GlobalPosition = flat; Velocity = Vector3.Zero;
                if (k >= 1)
                {
                    GlobalPosition = World.OnGround(_leapTo.X, _leapTo.Z, .1f);
                    Player.Instance?.Rig?.Shake(.35f); Sfx.Play("roar", .5f);
                    Vfx.Ring(GlobalPosition + Vector3.Up * .1f, 4.2f, new Color(.75f, .4f, 1f), .4f); Vfx.GroundMark(GlobalPosition, "scorch", 6f, new Color(.1f, .05f, .15f, .8f), 2f, 2f);
                    Fx.Burst(GlobalPosition + Vector3.Up * .3f, new Color(.7f, .6f, .55f), 24, 7f, .6f, .14f, -9f, "dirt");
                    if (ok && GlobalPosition.DistanceTo(p!.GlobalPosition) < 4.2f) HitHero(p!, .18f, 10f, true, .7f);
                    Begin(DM.Recover, 1f * Q);
                }
                Rotation = new Vector3(0, _yaw, 0); return;   // no physics while airborne
            }
            case DM.Dodge:
            {
                move = _dodgeDir * T("dodgeSpeed", 10f) * (1 - _mt / Mathf.Max(.01f, _mDur) * .6f);
                if (_mt >= _mDur) { Begin(DM.Idle); _think = 0; }   // straight into the punish
                break;
            }
            case DM.Recover:
                Face(yawTo, 3f, dt);
                if (_mt >= _mDur) { Begin(DM.Idle); _think = ThinkTime(p); }
                break;
            case DM.Staggered:
                if (_mt >= _mDur) { Begin(DM.Idle); _think = .25f; }
                break;
            case DM.Broken:
                if (_mt > .5f && _av != null && !_av.Busy) _av.Play("sit", .3f);   // down on one knee
                if (_mt >= _mDur) { Begin(DM.Idle); _think = .2f; _stance = 0; }
                break;
            case DM.Roar:
            {
                Face(yawTo, 4f, dt);
                if (_mt >= .5f && _mt - dt < .5f)
                {
                    Vfx.Ring(GlobalPosition + Vector3.Up * .1f, 7f, new Color(.7f, .35f, 1f), .5f); Vfx.Flash(GlobalPosition + Vector3.Up * 1.5f, new Color(.6f, .3f, 1f), 6f, 14f, .5f);
                    Player.Instance?.Rig?.Shake(.3f);
                    if (ok && dist < 7f) HitHero(p!, .04f, 13f, true, .5f, noBlock: true);
                }
                if (_mt >= 1.6f) { if (_toEclipse) { _toEclipse = false; StartEclipse(); } else Begin(DM.Idle); }
                break;
            }
            case DM.Eclipse:
                TickEclipse(dt, p, dist);
                break;
        }
        if (_dm != DM.Eclipse && _eclipseOrb != null) { _eclipseOrb.QueueFree(); _eclipseOrb = null; }

        _kb = _kb.MoveToward(Vector3.Zero, 22f * dt);
        float slow = Mathf.Max(.6f, Status.SpeedMul);
        v.X = move.X * slow + _kb.X; v.Z = move.Z * slow + _kb.Z;
        Velocity = v; MoveAndSlide(); Rotation = new Vector3(0, _yaw, 0);
        // keep it on the sand
        var c = Builder.ArenaPos; var off = GlobalPosition - c; off.Y = 0; if (off.Length() > 21f) GlobalPosition -= off.Normalized() * (off.Length() - 21f);
        DuelAnimate();
    }

    float ThinkTime(Player? p)
    {
        float lo = _phase == 1 ? .35f : _phase == 2 ? .2f : .1f, hi = _phase == 1 ? .75f : _phase == 2 ? .5f : .35f;
        float t = (float)GD.RandRange(lo, hi); if (p != null && p.Hp < Game.D.MaxHp() * .3f) t *= .7f;   // smells blood
        return t;
    }

    /// <summary>Picks the next move from what the hero is doing, how far away it is and what was used last.</summary>
    void Decide(Player p, float dist)
    {
        float reach = Def.Reach; bool close = dist < reach + 1.3f, punish = _punishT > 0 || _healSeen > 0 || p.Casting || (p.Recovering && dist < 7f);
        bool ranged = Weapons.Get(_wt).Ranged;
        var w = new List<(DM m, float wt)>
        {
            (DM.Combo, close ? 5f : dist < 6f ? 1.4f : 0f),
            (DM.Heavy, close ? 2.2f + (_pBlockT > 1f ? 4f : 0f) : 0f),   // a raised guard gets crushed
            (DM.Lunge, dist > 3.8f && dist < 15f ? 2.6f + (_farT > 1.5f ? 3f : 0f) : 0f),
            (DM.Bolts, dist > 6f ? 1.8f + (_farT > 1.5f ? 2f : 0f) + (ranged ? 2f : 0f) : .2f),
            (DM.Leap, _phase >= 2 && dist > 5f && dist < 17f ? 2.6f + (_farT > 1.5f ? 1.5f : 0f) : 0f),
            (DM.Strafe, punish ? 0f : _phase == 1 ? 1.6f : _phase == 2 ? .8f : .35f),
        };
        if (punish)
        {
            // an opening: the fastest thing that reaches
            for (int i = 0; i < w.Count; i++) { var (m, x) = w[i]; if (m == DM.Lunge && dist > 3.5f) x += 5f; if (m == DM.Combo && close) x += 5f; if (m == DM.Bolts && _healSeen > 0 && dist > 8f) x += 4f; w[i] = (m, x); }
            _punishT = 0; _healSeen = 0;
        }
        for (int i = 0; i < w.Count; i++) { var (m, x) = w[i]; if (m == _dmLast) x *= m == _dmLast2 ? .1f : .4f; w[i] = (m, x); }   // no move three times running
        float sum = 0; foreach (var e in w) sum += e.wt;
        if (sum <= 0.01f) { Begin(DM.Idle); _think = .2f; return; }   // too far for anything: close in
        float r = GD.Randf() * sum; DM pick = DM.Strafe; foreach (var e in w) { r -= e.wt; if (r <= 0) { pick = e.m; break; } }
        if (pick == DM.Strafe) { _strafeSign = GD.Randf() < .5f ? 1 : -1; Begin(DM.Strafe, (float)GD.RandRange(.5, 1.2)); _think = _mDur; }
        else Begin(pick);
    }

    // ------------------------------------------------------------------ the Eclipse (damage check)
    void StartEclipse()
    {
        _eclipseDur = _phase >= 3 ? T("eclipseTime3", 9f) : T("eclipseTime2", 7f);
        float heroDps = Mathf.Max(5f, Game.D.WpnDmg()) * T("eclipseHitsPerSecond", 1.5f);
        _barrierMax = _barrier = heroDps * _eclipseDur * T("eclipseShare", .55f) * (_phase >= 3 ? 1.15f : 1f) * (1 + .06f * (DuelRound - 1));
        Begin(DM.Eclipse, _eclipseDur);
        Ui.Banner("Eclipse", "break its barrier before it falls!"); Sfx.Play("roar");
        _eclipseOrb = new MeshInstance3D { Mesh = new SphereMesh { Radius = 1.6f, Height = 3.2f, RadialSegments = 24, Rings = 12 }, Position = new Vector3(0, 1.1f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add, AlbedoColor = new Color(.5f, .25f, 1f, .35f), CullMode = BaseMaterial3D.CullModeEnum.Disabled } };
        _eclipseOrb.Position *= _scale; AddChild(_eclipseOrb);
    }
    void TickEclipse(float dt, Player? p, float dist)
    {
        float k = _mt / _eclipseDur;
        if (_eclipseOrb != null) { _eclipseOrb.Scale = Vector3.One * _scale * (1 + k * .5f + Mathf.Sin(_mt * 9f) * .03f); if (_eclipseOrb.MaterialOverride is StandardMaterial3D m) m.AlbedoColor = new Color(.5f + k * .5f, .25f, 1f - k * .4f, .3f + k * .3f); }
        Tell(k > .75f ? (k - .75f) * 4f : 0);
        if ((int)(_mt * 3) != (int)((_mt - dt) * 3)) Vfx.Ring(GlobalPosition + Vector3.Up * .1f, 3f + k * 20f, new Color(.6f, .3f, 1f, .6f), .3f);   // the gathering dark
        if (k > .85f && (k - dt / _eclipseDur) <= .85f) { Ui.Note("The Eclipse falls!", new Color(1f, .5f, .4f)); Vfx.Flash(GlobalPosition + Vector3.Up * 3, new Color(1f, .3f, .3f), 5f, 30f, .4f); }   // last call: roll now
        if (_mt < _eclipseDur) return;
        // failed: a blast across the whole arena (roll through it), and the Shadow drinks back some health
        Vfx.Flash(GlobalPosition + Vector3.Up * 2, new Color(.7f, .3f, 1f), 10f, 60f, .7f); Vfx.Ring(GlobalPosition + Vector3.Up * .2f, 24f, new Color(.7f, .35f, 1f), .7f);
        Player.Instance?.Rig?.Shake(.5f); Sfx.Play("roar");
        if (p != null && !p.Dead && dist < 40f) HitHero(p, T("eclipseDamage", .6f), 14f, true, 1f, noBlock: true, type: "true");
        Hp = Mathf.Min(MaxHp, Hp + MaxHp * T("eclipseHeal", .15f));
        FloatText.Spawn(this, GlobalPosition + new Vector3(0, HeightM() + .6f, 0), "+" + Mathf.RoundToInt(MaxHp * T("eclipseHeal", .15f)), new Color(.7f, .5f, 1f), 1.3f);
        Begin(DM.Recover, 1.2f);
    }

    // ------------------------------------------------------------------ helpers
    void Face(float yawTo, float rate, float dt) => _yaw = Mathf.LerpAngle(_yaw, yawTo, 1f - Mathf.Exp(-rate * dt));
    bool InArc(Player p, float reach, float arcDeg)
    {
        var d = p.GlobalPosition - GlobalPosition; d.Y = 0; if (d.Length() > reach + .45f) return false;
        return d.Length() < .8f || Mathf.Abs(Util.AngDiff(_yaw, Util.YawTo(d.X, d.Z))) < Mathf.DegToRad(arcDeg / 2);
    }
    /// <summary>pct = share of the hero's health (before armour); a crit uses the hero's own crit chance and damage.</summary>
    void HitHero(Player p, float pct, float knock, bool stagger, float stun, bool noBlock = false, string type = "physical")
    {
        bool crit = type != "true" && GD.Randf() < _crit;
        float dmg = (type == "true" ? Game.D.MaxHp() * pct : _dk * pct) * (crit ? Mathf.Min(2.2f, Game.D.CritMult()) : 1f) * (_phase == 3 ? T("enrageDamage", 1.15f) : 1f);
        var h = OnHit(new HitInfo { Damage = dmg, From = GlobalPosition, Knock = knock, Stagger = stagger, Stun = stun, NoBlock = noBlock, Crit = crit, Attacker = this });
        h.Type = type; p.TakeHit(h);
    }
    static Vector3 ClampArena(Vector3 at) { var c = Builder.ArenaPos; var off = at - c; off.Y = 0; if (off.Length() > 19f) at = c + off.Normalized() * 19f; return World.OnGround(at.X, at.Z, .1f); }

    void DuelAnimate()
    {
        if (_av == null) return;
        if (_dm is DM.Idle or DM.Strafe)
        {
            if (_av.Busy && _av.Cur.StartsWith("atk_")) return;   // let a swing finish its follow-through
            float hs = new Vector2(Velocity.X, Velocity.Z).Length(); var fw = Fwd; bool back = new Vector2(Velocity.X, Velocity.Z).Dot(new Vector2(fw.X, fw.Z)) < -.3f;
            _av.Move(hs, true, true, Game.D.Eq.GetValueOrDefault("shield") != null, back);
        }
    }
}
