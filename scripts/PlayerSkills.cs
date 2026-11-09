using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>
/// The hero's active skills (data/skills.json via Skills.Compute): hotbar keys 1-6, costs and cooldowns, cast or attack animation,
/// and execution per kind. Supports add auto-casting (timer, when hit, on crit) and echo. Buffs from skills feed Game.TS while they last.
/// </summary>
public partial class Player
{
    readonly Dictionary<string, float> _skillCd = new(), _skillCdMax = new(), _autoCd = new();
    SkillCalc? _castSkill, _atkSkill;
    public readonly List<(string id, Dictionary<string, float> mods, float t)> Buffs = new();
    // leap / dash movement
    Vector3 _moveFrom, _moveTo; float _moveT, _moveDur, _moveArc; SkillCalc? _moveSkill; float _spinLeft, _spinStep; int _spinHits;

    public float CdOf(string? id) => id != null && _skillCd.TryGetValue(id, out var v) ? v : 0;
    public float CdMaxOf(string? id) => id != null && _skillCdMax.TryGetValue(id, out var v) ? Mathf.Max(.01f, v) : 1;
    public bool Busy => _castT > 0 || _atkT > 0 || _moveT > 0 || _spinLeft > 0;

    static readonly HashSet<string> WeaponKinds = new() { "melee", "spin", "leap", "dash" };

    void TryCast(int slot)
    {
        var D = Game.D; if (slot >= D.Bar.Length) return; var id = D.Bar[slot];
        if (string.IsNullOrEmpty(id) || !D.Skills.ContainsKey(id) || Skills.Get(id) == null) { Ui.Note($"No skill on key {slot + 1}. Press L to set one.", new Color(.7f, .8f, 1)); return; }
        if (Dead || Busy || _rollT > 0 || _stagger > 0) return;
        if (Mounted) { Ui.Note("You cannot use skills while mounted", new Color(0.7f, 0.8f, 1)); return; }
        if (CdOf(id) > 0) return;
        var c = Skills.Compute(id); var k = c.Def;
        if (k.Tags.Contains("bow") && D.WType() != "bow") { Ui.Note(k.Name + " needs a bow", new Color(1, .6f, .5f)); return; }
        if (Mp < c.Mp) { Ui.Note("Not enough mana", new Color(0.4f, 0.65f, 1f)); return; }
        if (St < c.St) { Ui.Note("Not enough stamina", new Color(0.6f, 0.9f, .7f)); return; }
        Mp -= c.Mp; if (c.St > 0) UseStamina(c.St); StartCd(id, c.Cd);
        FaceAim();
        if (WeaponKinds.Contains(k.Kind) && !W.Ranged)
        {
            // weapon skills reuse the weapon's power swing as the body motion; the skill fires at its impact frame
            _atk = k.Kind == "spin" ? W.Combo[^1] : W.Power; _atkPower = true; _atkDur = Mathf.Max(.25f, c.Cast); _atkT = _atkDur; _hitDone = false; _atkSkill = c; _spinBase = _modelYaw;
        }
        else { _castSkill = c; _castT = _castDur = Mathf.Max(.12f, c.Cast); _atkT = 0; _fired = false; }
        Sfx.Play(k.Type == "physical" ? "swing" : k.Type == "frost" ? "ice" : k.Type == "lightning" ? "zap" : "fire");
    }
    void StartCd(string id, float cd) { _skillCd[id] = cd; _skillCdMax[id] = cd; }
    void FaceAim()
    {
        if (Targeting.Current is Enemy t && IsInstanceValid(t)) { var d = t.GlobalPosition - GlobalPosition; _modelYaw = Util.YawTo(d.X, d.Z); }
        else _modelYaw = Rig?.Yaw ?? _modelYaw;
    }

    /// <summary>Per-frame skill upkeep: cooldowns, buffs, auto-cast timers, leap/dash movement and whirlwind spins. Returns true when movement is scripted this frame.</summary>
    bool SkillTick(float dt)
    {
        foreach (var k in new List<string>(_skillCd.Keys)) _skillCd[k] = Mathf.Max(0, _skillCd[k] - dt);
        foreach (var k in new List<string>(_autoCd.Keys)) _autoCd[k] = Mathf.Max(0, _autoCd[k] - dt);
        bool changed = false;
        for (int i = Buffs.Count - 1; i >= 0; i--) { var b = Buffs[i]; b.t -= dt; Buffs[i] = b; if (b.t <= 0) { Buffs.RemoveAt(i); changed = true; } }
        if (changed) Game.RecalcTree();
        // auto-cast on a timer while foes are near
        foreach (var id in Game.D.Bar)
        {
            if (string.IsNullOrEmpty(id) || !Game.D.Skills.ContainsKey(id)) continue; var st = Game.D.Skills[id]; if (!st.Sup.Exists(s => Skills.Sup(s)?.Auto == "timer")) continue;
            if (_autoCd.GetValueOrDefault(id) > 0 || NearestFoe(18f) == null) continue;
            AutoFire(id, "timer");
        }
        if (_spinLeft > 0 && _atkSkill != null)
        {
            // whirlwind: hits all around once per turn
            _spinLeft -= dt; _modelYaw += dt * Mathf.Tau * 2.2f; _spinStep -= dt;
            if (_spinStep <= 0 && _spinHits > 0) { _spinStep = .26f; _spinHits--; AreaHit(_atkSkill, GlobalPosition, _atkSkill.Radius, 1f); Vfx.Ring(GlobalPosition + Vector3.Up * .1f, _atkSkill.Radius, W.Trail, .25f); }
            if (_spinLeft <= 0) _atkSkill = null;
        }
        if (_moveT > 0 && _moveSkill != null)
        {
            _moveT -= dt; float t = 1 - Mathf.Max(0, _moveT) / _moveDur;
            var p = _moveFrom.Lerp(_moveTo, t) + Vector3.Up * Mathf.Sin(t * Mathf.Pi) * _moveArc;
            GlobalPosition = p; Velocity = Vector3.Zero; _inv = Mathf.Max(_inv, .05f);
            if (_moveT <= 0) { var c = _moveSkill; _moveSkill = null; Land(c); }
            return true;
        }
        return false;
    }

    Enemy? NearestFoe(float r, Vector3? at = null, HashSet<Enemy>? skip = null)
    {
        Enemy? best = null; float bd = r; var o = at ?? GlobalPosition;
        foreach (var n in GetTree().GetNodesInGroup("enemies"))
            if (n is Enemy e && !e.Dead && e.Active && !e.Def.Flee && (skip == null || !skip.Contains(e))) { float d = (e.GlobalPosition - o).Length(); if (d < bd) { bd = d; best = e; } }
        return best;
    }

    /// <summary>Support-driven casting with no animation (Arcane Loop, Retaliation, Critical Trigger). Pays the cost; has its own short cooldown.</summary>
    public void AutoFire(string id, string trigger)
    {
        if (Dead || !Game.D.Skills.TryGetValue(id, out var st)) return;
        if (!st.Sup.Exists(s => Skills.Sup(s)?.Auto == trigger) || _autoCd.GetValueOrDefault(id) > 0) return;
        var c = Skills.Compute(id); if (Mp < c.Mp || St < c.St) return;
        if (c.Def.Tags.Contains("bow") && Game.D.WType() != "bow") return;
        Mp -= c.Mp; if (c.St > 0) St -= c.St; _autoCd[id] = Mathf.Max(.5f, c.AutoCd);
        var foe = NearestFoe(25f); if (foe != null) { var d = foe.GlobalPosition - GlobalPosition; _modelYaw = Util.YawTo(d.X, d.Z); }
        if (c.Def.Kind is "leap" or "dash") return; // movement skills never fire by themselves
        Execute(c, foe);
    }
    void TriggerAuto(string trigger) { foreach (var id in Game.D.Bar) if (!string.IsNullOrEmpty(id)) AutoFire(id, trigger); }

    // ---------------------------------------------------------------- execution
    (Vector3 pt, Enemy? e) Aim(float range, Enemy? forced = null)
    {
        if (forced != null && IsInstanceValid(forced)) return (forced.GlobalPosition + Vector3.Up * forced.HeightM() * .5f, forced);
        return AimPoint(range);
    }
    Vector3 Ground(Vector3 p) { if (Dungeon.Active) return new Vector3(p.X, GlobalPosition.Y, p.Z); return new Vector3(p.X, World.GroundAt(p.X, p.Z), p.Z); }
    bool Crit(SkillCalc c) => c.Def.Crit || GD.Randf() < Game.D.CritChance();

    void Execute(SkillCalc c, Enemy? forced = null, bool echo = false)
    {
        var k = c.Def; var origin = MuzzlePos(); var col = Dmg.TypeColor(c.Type);
        switch (k.Kind)
        {
            case "projectile":
            {
                var (t, _) = Aim(80f, forced); var dir = (t - origin).Normalized();
                int n = Mathf.Max(1, c.Count); float spread = Mathf.DegToRad(c.Spread > 0 ? c.Spread : n > 1 ? 10f * (n - 1) : 0);
                for (int i = 0; i < n; i++)
                {
                    float a = spread >= Mathf.Tau - .01f ? i * Mathf.Tau / n : n > 1 ? (i / (float)(n - 1) - .5f) * spread : 0;
                    var d = dir.Rotated(Vector3.Up, a);
                    string vis = c.Type == "fire" ? "fire" : k.Arrow || (k.Scale == "weapon" && c.Type == "physical") ? "arrow" : "bolt";
                    var p = Projectile.Spawn(vis, 'p', origin, d * c.Speed + (vis == "arrow" ? Vector3.Up * .5f : Vector3.Zero), c.Dmg, 2.4f, Crit(c), false, c.Radius);
                    p.Calc = c; p.PierceLeft = c.Pierce; p.ChainLeft = c.Chain; p.Homing = c.Homing; p.Tint = col;
                }
                break;
            }
            case "nova":
                if (c.Type == "frost") Vfx.FrostNova(GlobalPosition, c.Radius); else { Vfx.Ring(GlobalPosition + Vector3.Up * .1f, c.Radius, col, .45f); Vfx.Flash(GlobalPosition + Vector3.Up, col, 4f, c.Radius * 2, .5f); }
                Rig?.Shake(.15f); AreaHit(c, GlobalPosition, c.Radius, 1f); break;
            case "chain":
            {
                var (t, cur) = Aim(k.Range, forced); cur ??= NearestFoe(28f);
                if (cur == null) { Vfx.Lightning(origin, t, col); break; }
                var prev = origin; float dm = c.Dmg; var seen = new HashSet<Enemy>();
                int hitsN = System.Math.Max(1, c.Chain) + (k.Id == "zap" ? (int)Game.TS["chain"] : 0);
                for (int j = 0; j < hitsN && cur != null; j++)
                {
                    var pt = cur.GlobalPosition + new Vector3(0, cur.HeightM() * .55f, 0); Vfx.Lightning(prev, pt, col, .28f); seen.Add(cur);
                    cur.TakeHit(c.Hit(dm, GlobalPosition, Crit(c))); dm *= .82f; prev = pt; cur = NearestFoe(10f, cur.GlobalPosition, seen);
                }
                break;
            }
            case "heal":
            {
                float amt = Game.D.MaxHp() * c.Heal * (1 + Game.TS["healP"] / 100f); Hp = Mathf.Min(Game.D.MaxHp(), Hp + amt); Statuses.List.RemoveAll(s => s.Def.Kind is "dot" or "slow" or "weaken");
                Vfx.HealPillar(this); FloatText.Spawn(this, GlobalPosition + new Vector3(0, 2.2f, 0), "+" + Mathf.RoundToInt(amt), new Color(0.5f, 1f, 0.6f)); break;
            }
            case "zone":
            {
                var at = k.Self ? GlobalPosition : Ground(Aim(40f, forced).pt);
                var z = new SkillZone { Calc = c, Col = col, Rain = k.Id == "rain" }; Game.Root!.AddChild(z); z.GlobalPosition = at; break;
            }
            case "strike":
            {
                var at = Ground(Aim(45f, forced).pt);
                var s = new SkillZone { Calc = c, Col = col, Strike = true }; Game.Root!.AddChild(s); s.GlobalPosition = at; break;
            }
            case "melee":
            {
                var fw = new Vector3(-Mathf.Sin(_modelYaw), 0, -Mathf.Cos(_modelYaw)); var hit = ArcHit(c, fw, Mathf.DegToRad(Mathf.Min(180, k.Arc)), k.Range);
                if (k.Arc >= 120 || k.Range > 4) Vfx.Ring(GlobalPosition + Vector3.Up * .1f + (k.Range > 4 ? fw * k.Range * .5f : Vector3.Zero), k.Range > 4 ? k.Range * .5f : k.Range, W.Trail, .3f);
                if (c.Chain > 0 && hit != null)
                {
                    // arcs jump from the struck foe to the next ones
                    var seen = new HashSet<Enemy> { hit }; var prev = hit.GlobalPosition + Vector3.Up; var cur = NearestFoe(9f, hit.GlobalPosition, seen);
                    for (int j = 0; j < c.Chain && cur != null; j++) { var pt = cur.GlobalPosition + Vector3.Up; Vfx.Lightning(prev, pt, col, .25f); cur.TakeHit(c.Hit(c.Dmg * .6f, GlobalPosition, false)); seen.Add(cur); prev = pt; cur = NearestFoe(9f, cur.GlobalPosition, seen); }
                }
                break;
            }
            case "spin": _spinLeft = .26f * c.Count + .05f; _spinStep = 0; _spinHits = c.Count; _atkSkill = c; break;
            case "leap":
            {
                var target = forced ?? Targeting.Current; var to = target != null && IsInstanceValid(target) ? target.GlobalPosition : Aim(k.Range).pt;
                var d = to - GlobalPosition; d.Y = 0; if (d.Length() > k.Range) d = d.Normalized() * k.Range; if (d.Length() > 1.4f) d -= d.Normalized() * 1.2f;
                _moveFrom = GlobalPosition; _moveTo = Ground(GlobalPosition + d) + Vector3.Up * .1f; _moveDur = .45f; _moveT = _moveDur; _moveArc = 2.6f; _moveSkill = c; break;
            }
            case "dash":
            {
                var target = forced ?? Targeting.Current ?? (k.Behind ? NearestFoe(k.Range) : null);
                if (k.Behind && target != null && IsInstanceValid(target))
                {
                    // shadow step: appear behind the target facing it, then strike with a guaranteed crit
                    var back = new Vector3(Mathf.Sin(target.Rotation.Y), 0, Mathf.Cos(target.Rotation.Y)); var to = target.GlobalPosition + back * (target.Def.R + 1.1f);
                    Vfx.Sparks(GlobalPosition + Vector3.Up, Ramp(new Color(.6f, .3f, 1f)), 24, 3, .5f, .3f, 1f);
                    GlobalPosition = Ground(to) + Vector3.Up * .2f; var dd = target.GlobalPosition - GlobalPosition; _modelYaw = Util.YawTo(dd.X, dd.Z);
                    Vfx.Sparks(GlobalPosition + Vector3.Up, Ramp(new Color(.6f, .3f, 1f)), 24, 3, .5f, .3f, 1f);
                    target.TakeHit(c.Hit(c.Dmg, GlobalPosition, true)); Rig?.Shake(.15f);
                }
                else
                {
                    var fw = new Vector3(-Mathf.Sin(_modelYaw), 0, -Mathf.Cos(_modelYaw));
                    _moveFrom = GlobalPosition; _moveTo = Ground(GlobalPosition + fw * k.Range) + Vector3.Up * .1f; _moveDur = .22f; _moveT = _moveDur; _moveArc = .15f; _moveSkill = c;
                }
                break;
            }
            case "buff":
            {
                Buffs.RemoveAll(b => b.id == k.Id); Buffs.Add((k.Id, new Dictionary<string, float>(k.Buff), c.Dur)); Game.RecalcTree();
                Vfx.Ring(GlobalPosition + Vector3.Up * .1f, Mathf.Max(3, c.Radius), col.Lerp(new Color(1, .8f, .4f), .4f), .5f);
                FloatText.Spawn(this, GlobalPosition + new Vector3(0, 2.4f, 0), k.Name, new Color(1, .85f, .45f));
                if (c.Radius > 0) AreaHit(c, GlobalPosition, c.Radius, 0f);
                break;
            }
        }
        if (c.Echo > 0 && !echo && k.Kind is not ("leap" or "dash" or "buff" or "heal")) GetTree().CreateTimer(.3f).Timeout += () => { if (!Dead) Execute(c, forced, true); };
    }
    static Gradient Ramp(Color c) { var g = new Gradient(); g.SetColor(0, Colors.White); g.SetColor(1, new Color(c, 0)); return g; }

    /// <summary>Damage everything within r of a point (mul 0 = statuses only, as for war cries).</summary>
    void AreaHit(SkillCalc c, Vector3 at, float r, float mul)
    {
        foreach (var n in GetTree().GetNodesInGroup("enemies"))
            if (n is Enemy e && !e.Dead && e.Active && new Vector2(e.GlobalPosition.X - at.X, e.GlobalPosition.Z - at.Z).Length() - e.Def.R * e.ScaleF() < r && Mathf.Abs(e.GlobalPosition.Y - at.Y) < 4f)
            {
                var h = c.Hit(c.Dmg * mul, at, mul > 0 && Crit(c)); if (mul <= 0) { h.Damage = 0.01f; h.NoText = true; h.Knock = 0; }
                e.TakeHit(h);
            }
    }
    Enemy? ArcHit(SkillCalc c, Vector3 fw, float arc, float range)
    {
        Enemy? first = null; float fd = 1e9f; int hits = 0;
        foreach (var n in GetTree().GetNodesInGroup("enemies"))
        {
            if (n is not Enemy e || e.Dead || !e.Active) continue;
            var d = e.GlobalPosition - GlobalPosition; d.Y = 0; float dist = d.Length() - e.Def.R * e.ScaleF();
            if (dist > range || Mathf.Abs(e.GlobalPosition.Y - GlobalPosition.Y) > 3.5f + e.Def.R) continue;
            if (dist > .6f && arc < Mathf.Pi - .01f && Mathf.Abs(Util.AngDiff(_modelYaw, Util.YawTo(d.X, d.Z))) > arc) continue;
            bool cr = Crit(c); e.TakeHit(c.Hit(c.Dmg * (float)GD.RandRange(.9, 1.1) * (cr ? Game.D.CritMult() : 1f), GlobalPosition, cr)); hits++;
            if (cr) TriggerAuto("crit");
            if (dist < fd) { fd = dist; first = e; }
        }
        if (hits > 0) { Rig?.Shake(.2f); Sfx.Play("hit"); }
        return first;
    }
    void Land(SkillCalc c)
    {
        if (c.Def.Kind == "leap") { AreaHit(c, GlobalPosition, c.Radius, 1f); Vfx.Ring(GlobalPosition + Vector3.Up * .1f, c.Radius, W.Trail, .35f); Vfx.Sparks(GlobalPosition, Ramp(new Color(.7f, .6f, .5f)), 30, 6, .5f, .3f, -8f); Rig?.Shake(.3f); _landT = .22f; }
        else
        {
            // lunge: everything along the dash line takes the hit
            var a = _moveFrom; var b = GlobalPosition;
            foreach (var n in GetTree().GetNodesInGroup("enemies"))
                if (n is Enemy e && !e.Dead && e.Active)
                {
                    var p = e.GlobalPosition; var ab = b - a; ab.Y = 0; float t = ab.LengthSquared() > .01f ? Mathf.Clamp(new Vector3(p.X - a.X, 0, p.Z - a.Z).Dot(ab) / ab.LengthSquared(), 0, 1) : 0;
                    var cp = a + ab * t; if (new Vector2(p.X - cp.X, p.Z - cp.Z).Length() < 1.6f + e.Def.R) { bool cr = Crit(c); e.TakeHit(c.Hit(c.Dmg * (cr ? Game.D.CritMult() : 1f), a, cr)); }
                }
            Rig?.Shake(.12f);
        }
    }

    /// <summary>Called from the attack timeline at the impact frame of a weapon skill.</summary>
    void FireWeaponSkill(SkillCalc c) { var keep = c; if (c.Def.Kind != "spin") _atkSkill = null; Execute(keep); }
}

/// <summary>A lingering ground effect (Hex, Entangle, Consecrate, Rain of Arrows) or a delayed strike from above (Smite, Meteor).</summary>
public partial class SkillZone : Node3D
{
    public SkillCalc Calc = null!; public Color Col; public bool Strike, Rain;
    float _t, _tick; MeshInstance3D? _disc; CpuParticles3D? _fx;
    public override void _Ready()
    {
        float r = Calc.Radius;
        _disc = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = r, BottomRadius = r, Height = .04f, RadialSegments = 40 }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Position = Vector3.Up * .06f,
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(Col, Strike ? .18f : .22f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add } };
        AddChild(_disc);
        AddChild(new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = r * .93f, OuterRadius = r, Rings = 48, RingSegments = 3 }, Position = Vector3.Up * .08f, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(Col, .8f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add } });
        if (!Strike)
        {
            var g = new Gradient(); g.SetColor(0, new Color(Col, 0)); g.AddPoint(.3f, new Color(Col, .8f)); g.SetColor(1, new Color(Col, 0));
            _fx = new CpuParticles3D { Amount = Rain ? 60 : 30, Lifetime = Rain ? .5f : 1.2f, EmissionShape = CpuParticles3D.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(r * .8f, .1f, r * .8f), Position = Vector3.Up * (Rain ? 9f : .2f),
                Gravity = new Vector3(0, Rain ? -60f : 1.5f, 0), Direction = Rain ? Vector3.Down : Vector3.Up, InitialVelocityMin = Rain ? 15 : .3f, InitialVelocityMax = Rain ? 20 : 1f, ColorRamp = g,
                Mesh = new QuadMesh { Size = Rain ? new Vector2(.06f, .7f) : new Vector2(.3f, .3f), Material = Vfx.SpriteMat() }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(_fx);
        }
        else if (Calc.Type == "fire")
        {
            // meteor: a burning rock falls during the delay
            var rock = new MeshInstance3D { Mesh = Gfx.Boulder(2), Scale = Vector3.One * .9f, Position = new Vector3(6, 22, 6), MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(.3f, .15f, .1f), EmissionEnabled = true, Emission = new Color(1, .4f, .1f), EmissionEnergyMultiplier = 2f } };
            AddChild(rock); rock.CreateTween().TweenProperty(rock, "position", Vector3.Up * .5f, Mathf.Max(.1f, Calc.Def.Delay)).SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Quad);
        }
    }
    public override void _Process(double delta)
    {
        float dt = (float)delta; _t += dt; var p = Player.Instance;
        if (Strike)
        {
            if (_t < Calc.Def.Delay) return;
            if (Calc.Type == "fire") Vfx.Explosion(GlobalPosition + Vector3.Up * .4f, Calc.Radius);
            else { Vfx.HealPillarAt(GlobalPosition, Col, Calc.Radius); }
            Hit(1f); p?.Rig?.Shake(.2f); QueueFree(); return;
        }
        _tick -= dt;
        if (_tick <= 0) { _tick = Calc.Tick; Hit(1f); if (Calc.Heal > 0 && p != null && (p.GlobalPosition - GlobalPosition).Length() < Calc.Radius) p.Hp = Mathf.Min(Game.D.MaxHp(), p.Hp + Game.D.MaxHp() * Calc.Heal * (1 + Game.TS["healP"] / 100f)); }
        if (Calc.Def.Self && p != null) GlobalPosition = p.GlobalPosition;
        if (_t > Calc.Dur) { if (_fx != null) _fx.Emitting = false; Scale = Scale.Lerp(Vector3.Zero, .2f); if (_t > Calc.Dur + .4f) QueueFree(); }
    }
    void Hit(float mul)
    {
        foreach (var n in GetTree().GetNodesInGroup("enemies"))
            if (n is Enemy e && !e.Dead && e.Active && new Vector2(e.GlobalPosition.X - GlobalPosition.X, e.GlobalPosition.Z - GlobalPosition.Z).Length() - e.Def.R < Calc.Radius && Mathf.Abs(e.GlobalPosition.Y - GlobalPosition.Y) < 4f)
            { var h = Calc.Hit(Calc.Dmg * mul, GlobalPosition, false); if (!Strike) h.Knock = 0; e.TakeHit(h); }
    }
}
