using System;
using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>
/// A-16: the hero in an animal or spirit form (data/forms.json, Forms in Transform.cs).
/// Shifting: a form skill ("form_wolf", kind "form") is cast like any skill (mana, cast time: PlayerSkills' one cast gate); when it
/// goes off FormShift hides the hero's own body and puts the form's creature model in its place (an Enemy "PetBody": the model only,
/// animated from the speed we give it, the way a ridden pet is), adds the form's stats as a buff (stat stage "buffs") and starts the
/// timer. While transformed FormTick runs the hero's frame instead of the usual walking / fighting code: the form's own speed and
/// jump (flight for forms that fly, as with the Golden Wings; "ghost" forms walk through monsters), no rolling, blocking, gear
/// skills, macros, auto-cast modifiers or mounting, and the hotbar is the form's abilities: ability 0 on Attack, 1 on Heavy and key 1,
/// 2 and 3 on keys 2 and 3; key 6 (forms.json rules.revertKey) changes back. Potions, interacting, the realm shift and target lock work.
/// The form ends when its time runs out, on the revert key, on death, in deep water (forms that cannot swim), in a boat, and when the
/// hero is saved and loaded (it is never saved). Its skill's cooldown starts when the form ends (cut by the formCdr stat).
/// Friends see the form: the pose packet's last number is the form's index (PlayerNet.cs, RemoteHeroForm.cs).
/// </summary>
public partial class Player
{
    /// <summary>The form the hero wears now (null = their own body).</summary>
    public FormDef? Form { get; private set; }
    public float FormLeft => _formT; public float FormTime => _formDur;
    /// <summary>For the pose packet: 1-based index of the form in data/forms.json, 0 = none.</summary>
    public int FormIndex => Form == null ? 0 : Forms.IndexOf(Form.Id);
    public float FormCd(int i) => i >= 0 && i < _fCd.Length ? _fCd[i] : 0; public float FormCdMax(int i) => i >= 0 && i < _fCdMax.Length ? Mathf.Max(.01f, _fCdMax[i]) : 1;
    float _formT, _formDur, _formSync, _formLock, _fDashT; Vector3 _fDashV; FormAbility? _fDashAb;
    Node3D? _formHolder; Enemy? _formBody; bool _formFlying; CanvasLayer? _formUi;
    readonly float[] _fCd = new float[8], _fCdMax = new float[8];

    static float FK(string k) => StatPipeline.Key(k);

    /// <summary>Called when a form skill goes off (PlayerSkillsExtra.ExecuteExtra, kind "form").</summary>
    void FormShift(SkillCalc c)
    {
        var f = Forms.Get(c.Def.Id); if (f == null) return;
        if (Form != null) FormRevert(true);
        if (InBoat != null || (Swimming && !f.Swim))
        {   // not here: the cost comes back and the skill is ready again
            Ui.Note(InBoat != null ? "You cannot change shape in a boat" : $"A {f.Name} cannot swim: find solid ground first", new Color(1f, .7f, .5f));
            Mp = Mathf.Min(Game.D.MaxMp(), Mp + c.Mp); _skillCd[f.Key] = 0; _skillCdMax[f.Key] = 0; return;
        }
        if (Mounted) ToggleMount(true);
        Form = f; _formDur = _formT = Mathf.Max(5f, f.Dur * (1 + FK("formDurP") / 100f)); _formLock = .25f; _fDashT = 0; _formFlying = false;
        Array.Clear(_fCd, 0, _fCd.Length); Array.Clear(_fCdMax, 0, _fCdMax.Length);
        _skillCd[f.Key] = 0; _skillCdMax[f.Key] = 0;   // the cooldown runs from the moment the form ends (FormRevert)
        // the body: the creature's model where the hero stood
        _formHolder = new Node3D { Name = "FormBody", Scale = Vector3.One * Mathf.Max(.1f, f.Scale), Rotation = new Vector3(0, _modelYaw, 0) }; AddChild(_formHolder);
        _formBody = new Enemy { Type = f.Body, PetBody = true, Level = Game.D.Level, AnyRealm = true }; _formHolder.AddChild(_formBody);
        Callable.From(() => FormTint(f)).CallDeferred();
        _model.Visible = false; _trail.Emit = false; _horse.Visible = false;
        if (f.Ghost) CollisionMask = 1;   // spirits drift through monsters (layer 2), not through walls
        // the form's stats, for as long as it lasts
        Buffs.RemoveAll(b => b.id.StartsWith("form_")); Buffs.Add((f.Key, new Dictionary<string, float>(f.Mods), _formDur + 2f)); Game.RecalcTree();
        Hp = Mathf.Min(Hp, Game.D.MaxHp()); Mp = Mathf.Min(Mp, Game.D.MaxMp());
        // nothing of the hero's own fighting carries into the form
        _atkT = 0; _recT = 0; _recPending = 0; _hasQueue = false; _heavyT = 0; _blocking = false; EndHold(); StopMacro();
        FormFx(f, true);
        if (_formUi == null || !IsInstanceValid(_formUi)) { _formUi = new CanvasLayer { Layer = 2, Name = "FormUi" }; AddChild(_formUi); _formUi.AddChild(new FormBar()); }
        Ui.Note($"{f.Name} form for {_formDur:0} s.  {Inputs.KeyOf(Inputs.Attack)}: {f.Abilities[0].Name}" + (f.Fly ? $"   {Inputs.KeyOf(Inputs.Jump)} in the air: fly" : "") + $"   {Inputs.KeyOf("spell" + (int)Forms.Rule("revertKey", 6))}: change back", f.Col.Lightened(.25f));
        Flag.Event("FORM " + f.Id);
    }

    /// <summary>Tints the form's model and makes spirits glow (the model has its own material copies, Enemy.BuildModel).</summary>
    void FormTint(FormDef f)
    {
        if (_formBody == null || !IsInstanceValid(_formBody) || (f.Tint == "" && !f.Glow)) return;
        Color tint = f.Tint != "" && Color.HtmlIsValid(f.Tint) ? Color.FromHtml(f.Tint) : Colors.White;
        foreach (var n in _formBody.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (n is not MeshInstance3D mi || mi.Mesh == null) continue;
            for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
            {
                if (mi.GetSurfaceOverrideMaterial(s) is not BaseMaterial3D m) continue;
                if (f.Tint != "") m.AlbedoColor *= tint;
                if (f.Glow) { m.EmissionEnabled = true; m.Emission = f.Col.Darkened(.15f); m.EmissionEnergyMultiplier = .9f; }
            }
        }
    }

    /// <summary>The flash, ring, sparks and sound of changing shape (both ways).</summary>
    void FormFx(FormDef f, bool into)
    {
        var at = GlobalPosition + Vector3.Up * .9f;
        Fx.Ring(GlobalPosition, 3.2f, f.Col, .55f); Fx.Burst(at, f.Col, 28, 5f, .7f, .14f, 1.5f, "twirl");
        Vfx.Flash(at, f.Col, 3.5f, 7f, .4f); Fx.Dust(GlobalPosition, 1f);
        Sfx.Play("shift", .7f, into ? 1.25f : .9f);
        if (into && f.Kind == "animal") Sfx.Play("roar", .6f, f.Speed > 1.2f ? 1.5f : .8f);
        Rig?.Shake(.08f);
    }

    /// <summary>Back to the hero's own body. silent: no effect or note (death, shifting straight into another form).</summary>
    public void FormRevert(bool silent = false)
    {
        var f = Form; if (f == null) return; Form = null;
        if (_formHolder != null && IsInstanceValid(_formHolder)) _formHolder.QueueFree(); _formHolder = null; _formBody = null;
        if (_formUi != null && IsInstanceValid(_formUi)) _formUi.QueueFree(); _formUi = null;
        _model.Visible = true; CollisionMask = 1 | 2;
        if (_formFlying) { _formFlying = false; Flying = false; }
        _fDashT = 0; _fDashAb = null; _formLock = 0;
        Buffs.RemoveAll(b => b.id.StartsWith("form_")); Game.RecalcTree();
        Hp = Mathf.Min(Hp, Game.D.MaxHp()); Mp = Mathf.Min(Mp, Game.D.MaxMp());
        float cd = f.Cd * Mathf.Clamp(1 - FK("formCdr") / 100f, .2f, 2f); StartCd(f.Key, cd);
        if (!silent) { FormFx(f, false); Ui.Note($"You are yourself again. {f.Name} form ready in {cd:0} s.", f.Col.Lightened(.25f)); }
        Flag.Event("FORM end " + f.Id);
    }

    /// <summary>
    /// One physics frame of the hero. Not transformed: keeps the learned forms in step with the tree and gear (about once a second)
    /// and returns false. Transformed: runs the whole frame (input, movement, regeneration, the body's animation) and returns true.
    /// </summary>
    bool FormTick(float dt, ref Vector3 v)
    {
        if (Form == null)
        {
            _formSync -= dt; if (_formSync <= 0) { _formSync = Mathf.Max(.2f, Forms.Rule("syncSeconds", 1f)); Forms.Sync(Game.D); }
            return false;
        }
        var f = Form; var D = Game.D; var TS = Game.TS;
        _formT -= dt; _formLock = Mathf.Max(0, _formLock - dt);
        for (int i = 0; i < _fCd.Length; i++) if (_fCd[i] > 0) _fCd[i] = Mathf.Max(0, _fCd[i] - dt);
        if (_formT <= 0) { FormRevert(); return false; }
        if (Swimming && !f.Swim) { Ui.Note($"The water breaks the {f.Name} form", new Color(.7f, .85f, 1f)); FormRevert(); return false; }
        if (Mounted) ToggleMount(true);
        // gear skills stay quiet: auto-cast modifiers (Arcane Loop, Retaliation...) wait until we are ourselves again
        foreach (var id in D.Bar) if (!string.IsNullOrEmpty(id)) _autoCd[id] = Mathf.Max(_autoCd.GetValueOrDefault(id), .5f);
        _atkT = 0; _blocking = false; _hasQueue = false;

        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;
        var input = Input.GetVector(Inputs.Left, Inputs.Right, Inputs.Forward, Inputs.Back);
        float camYaw = Rig?.Yaw ?? 0f;
        var dir = new Vector3(input.X, 0, input.Y).Rotated(Vector3.Up, camYaw);
        if (captured)
        {
            int rk = (int)Forms.Rule("revertKey", 6);
            if (Input.IsActionJustPressed("spell" + rk)) { FormRevert(); return false; }
            if (Input.IsActionPressed(Inputs.Attack)) FormUse(0, true);
            if (Input.IsActionJustPressed(Inputs.Heavy)) FormUse(1, false);
            for (int i = 1; i < f.Abilities.Count && i <= 5; i++) if (i != rk && Input.IsActionJustPressed("spell" + i)) FormUse(i, false);
            if (Input.IsActionJustPressed(Inputs.PotHp)) UsePotion(true);
            if (Input.IsActionJustPressed(Inputs.PotMp)) UsePotion(false);
            if (Input.IsActionJustPressed(Inputs.Mount)) Ui.Note("You cannot ride while transformed", new Color(.7f, .8f, 1f));
            if (Input.IsActionJustPressed(Inputs.Realm)) World.TryShiftRealm();
            if (Input.IsActionJustPressed(Inputs.Lock)) { if (Inputs.UsingPad && Targeting.Current != null) Targeting.Clear(); else Targeting.Toggle(Targeting.Current != null); }
            if (Input.IsActionJustPressed(Inputs.Unlock)) Targeting.Clear();
            ScanInteract();
        }
        else Prompt = "";
        Targeting.Tick(this); AutoPotions(dt);

        // ---- movement
        float moveMul = 1f + TS["move"] / 100f;
        bool canFly = f.Fly && (!Dungeon.Active || Forms.Rule("dungeonFlight", 0) > 0);
        if (_formFlying && (!canFly || !Flying || _stagger > 0 || (IsOnFloor() && !(captured && Input.IsActionPressed(Inputs.Jump))))) { _formFlying = false; Flying = false; }
        else if (!_formFlying && canFly && captured && Input.IsActionJustPressed(Inputs.Jump) && !IsOnFloor() && St > 5f) { _formFlying = true; Flying = true; Sfx.Play("swing", .6f); }
        if (_fDashT > 0)
        {   // a pounce or charge in flight: carried along, the blow lands where it ends
            _fDashT -= dt; v.X = _fDashV.X; v.Z = _fDashV.Z; _inv = Mathf.Max(_inv, .05f);
            if (_fDashT <= 0) { var a = _fDashAb; _fDashAb = null; if (a != null) { FormArea(f, a, GlobalPosition, Mathf.Max(1.5f, a.Radius)); Fx.Dust(GlobalPosition, 1.1f); Rig?.Shake(.15f); } }
        }
        else if (_formFlying) FlyMove(dir, moveMul, captured, dt, ref v);   // wings: as with the Golden Wings (hold Jump to climb, Roll to dive; drains stamina)
        else
        {
            if (captured && Input.IsActionJustPressed(Inputs.Jump) && IsOnFloor() && _stagger <= 0) v.Y = JumpVelocity * JumpMul * f.Jump;
            if (!captured) Inputs.ForceRelease(Inputs.Sprint);
            bool sprintHeld = captured && Input.IsActionPressed(Inputs.Sprint);
            if (_sprTired && St >= Mathf.Min(Tune.F("stamina.sprintResume", 25f), D.MaxSt() * .5f)) _sprTired = false;
            bool sprint = sprintHeld && !_sprTired && St > 1f && dir.LengthSquared() > .01f && _formLock <= 0 && _stagger <= 0;
            if (sprintHeld && !_sprTired && St <= 1f) { _sprTired = true; sprint = false; }
            _sprinting = sprint;
            float speed = (sprint ? SprintSpeed : RunSpeed) * f.Speed * moveMul * Statuses.SpeedMul * _fluidSpeed;
            if (_formLock > 0) speed *= .3f;
            if (_stagger > 0) speed = 0;
            if (sprint) { St = Mathf.Max(0, St - Feel.SprintDrain * SprintCostMul * dt); _stLockT = Mathf.Max(_stLockT, Feel.SprintRegenDelay); }
            if (Rig != null) Rig.Sprinting = sprint && IsOnFloor();
            var target = dir * speed; float acc = (IsOnFloor() ? Accel : AirAccel) * dt * _accelMul;
            v.X = Mathf.MoveToward(v.X, target.X, acc); v.Z = Mathf.MoveToward(v.Z, target.Z, acc);
        }
        _kb = _kb.MoveToward(Vector3.Zero, 25f * dt); _hitKb = _hitKb.MoveToward(Vector3.Zero, 12f * dt);
        _walkV = new Vector3(v.X, 0, v.Z);
        var push = _kb * .3f + _hitKb; v.X += push.X; v.Z += push.Z;

        // ---- regeneration (the same rules as in the hero's own body)
        if (_stLockT <= 0) St = Mathf.Min(D.MaxSt(), St + Feel.StRegen * (1 + FK("stRegenP") / 100f) * dt);
        Hp = Mathf.Min(D.MaxHp(), Hp + (TS["regenHp"] + Tune.F("player.hpRegenBase", 0) + D.Vit * Tune.F("player.hpRegenPerVit", .02f)) * HealIn * dt);
        Mp = Mathf.Min(D.MaxMp(), Mp + (Tune.F("player.mpRegenBase", 1.5f) + TS["regenMp"] + D.Mag * Tune.F("player.mpRegenPerMag", .05f)) * dt);

        // ---- facing: the way we move; the view in first person; held on the target while an ability goes off
        var flat = new Vector3(v.X, 0, v.Z);
        if (_formLock <= 0 && flat.LengthSquared() > .5f) _modelYaw = Mathf.LerpAngle(_modelYaw, Util.YawTo(flat.X, flat.Z), 1f - Mathf.Exp(-14f * dt));
        if (Rig != null && Rig.FirstPerson) _modelYaw = camYaw;

        Velocity = v; MoveAndSlide();
        if (_formHolder != null && IsInstanceValid(_formHolder))
        {
            _formHolder.Rotation = new Vector3(0, _modelYaw, 0);
            if (_formBody != null && IsInstanceValid(_formBody)) _formBody.PetVel = new Vector3(Velocity.X, 0, Velocity.Z) / Mathf.Max(.3f, f.Scale);   // its gait follows our speed
        }
        _model.Rotation = new Vector3(0, _modelYaw, 0);
        _combatT -= dt; Game.PlayTime += dt;
        return true;
    }

    /// <summary>The better of the hero's weapon damage and spell strength, x formP and the form skill's rank: what form abilities multiply.</summary>
    float FormPower(FormDef f)
    {
        var d = Game.D; int rank = Math.Clamp(d.Skills.GetValueOrDefault(f.Key)?.Rank ?? 1, 1, Math.Max(1, Skills.MaxRank));
        float spell = (8 + 1.6f * d.Level) * d.SpellPow();
        return Mathf.Max(d.WpnDmg(), spell) * Mathf.Max(.1f, 1 + FK("formP") / 100f) * (1 + Skills.RankP * (rank - 1) / 100f);
    }
    HitInfo FormHit(FormDef f, FormAbility a, float mul = 1f)
    {
        bool crit = a.Crit || GD.Randf() < Game.D.CritChance(); float dmg = FormPower(f) * a.Dmg * mul * (float)GD.RandRange(.92, 1.08);
        var h = new HitInfo { Damage = crit ? dmg * Game.D.CritMult() : dmg, From = GlobalPosition, Knock = a.Knock, Stun = a.Stun, Crit = crit, Attacker = this, IsPlayer = true, Type = a.Type, Leech = a.Leech, Fire = a.Type == "fire", Stagger = a.Stun > 0 || a.Knock >= 6f, Kind = a.Kind == "melee" ? "light" : "" };
        foreach (var kv in a.Status) h.With(kv.Key, kv.Value);
        return h;
    }

    /// <summary>Uses ability i if it is ready and paid for. held: the attack button is held down (no "not enough" notes every frame).</summary>
    void FormUse(int i, bool held)
    {
        var f = Form; if (f == null || i < 0 || i >= f.Abilities.Count || Dead || _stagger > 0 || _formLock > 0 || _fDashT > 0 || _fCd[i] > 0) return;
        var a = f.Abilities[i];
        if (Mp < a.Mp) { if (!held) Ui.Note("Not enough mana", new Color(.4f, .65f, 1f)); return; }
        if (St < a.St) { if (!held) Ui.Note("Not enough stamina", new Color(.6f, .9f, .7f)); return; }
        Mp -= a.Mp; if (a.St > 0) UseStamina(a.St);
        float cdMul = i == 0 ? 1f / Mathf.Clamp(Game.D.AtkSpeed(), .5f, 2.5f) : Mathf.Max(.4f, 1 - FK("cdr") / 100f);
        _fCd[i] = _fCdMax[i] = a.Cd * cdMul;
        float wind = Mathf.Clamp(Forms.Rule("attackWind", .18f), .05f, 1f); _formLock = wind + .12f;
        if (_formBody != null && IsInstanceValid(_formBody)) _formBody.PetSwing(wind);
        if (Targeting.Aim is Enemy t && IsInstanceValid(t)) { var d = t.GlobalPosition - GlobalPosition; _modelYaw = Util.YawTo(d.X, d.Z); }
        else _modelYaw = Rig?.Yaw ?? _modelYaw;
        Fought();
        var form = f;
        GetTree().CreateTimer(wind, false).Timeout += () => { if (IsInstanceValid(this) && Form == form && !Dead) FormDo(form, i, a); };
    }

    void FormDo(FormDef f, int idx, FormAbility a)
    {
        var fw = Facing(); var col = a.Type == "physical" ? f.Col : Dmg.TypeColor(a.Type);
        switch (a.Kind)
        {
            case "melee":
            {
                int hits = 0; float half = Mathf.DegToRad(Mathf.Clamp(a.Arc, 10, 180));
                foreach (var n in GetTree().GetNodesInGroup("enemies"))
                {
                    if (n is not Enemy e || e.Dead || !e.Active) continue;
                    var d = e.GlobalPosition - GlobalPosition; d.Y = 0; float dist = d.Length() - e.Def.R * e.ScaleF();
                    if (dist > a.Range || Mathf.Abs(e.GlobalPosition.Y - GlobalPosition.Y) > 3.5f + e.Def.R) continue;
                    if (dist > .6f && half < Mathf.Pi - .01f && Mathf.Abs(Util.AngDiff(_modelYaw, Util.YawTo(d.X, d.Z))) > half) continue;
                    e.TakeHit(FormHit(f, a)); hits++;
                    SpellFx.Play("hit_spark", e.GlobalPosition + new Vector3(0, e.HeightM() * .55f, 0) - d.Normalized() * e.Def.R * e.ScaleF() * .8f, 1f);
                }
                if (a.Fx == "breath" || a.Range > 5f) for (int k = 1; k <= 4; k++) Fx.Burst(GlobalPosition + Vector3.Up * 1.1f + fw * (a.Range * k / 4f), col, 10, 3f, .45f, .16f, 1f, "twirl");
                if (hits > 0) { Sfx.Play("hit"); Rig?.Shake(.07f); } else Sfx.Play("swing", .7f, 1.2f);
                break;
            }
            case "pounce":
            {
                var tgt = Targeting.Aim ?? AimPoint(a.Range).enemy ?? NearestFoe(a.Range);
                Vector3 d = tgt != null && IsInstanceValid(tgt) && !tgt.Dead ? tgt.GlobalPosition - GlobalPosition : fw * a.Range * .8f; d.Y = 0;
                float dist = Mathf.Min(a.Range, d.Length()); if (tgt != null && IsInstanceValid(tgt)) dist = Mathf.Max(0, dist - (tgt.Def.R * tgt.ScaleF() + .9f));
                if (d.LengthSquared() > .01f) { d = d.Normalized(); _modelYaw = Util.YawTo(d.X, d.Z); } else d = fw;
                float time = Mathf.Clamp(dist / 26f, .12f, .45f);
                _fDashV = d * (dist / time); _fDashT = time; _fDashAb = a; _formLock = time + .1f;
                if (IsOnFloor()) Velocity = new Vector3(Velocity.X, 3.2f, Velocity.Z);
                Fx.Dust(GlobalPosition, .8f); Sfx.Play("swing", .9f, .8f);
                break;
            }
            case "aoe":
                FormArea(f, a, GlobalPosition, a.Radius); Fx.Ring(GlobalPosition, a.Radius, col, .45f); Vfx.Flash(GlobalPosition + Vector3.Up, col, 3f, a.Radius * 1.6f, .35f); Rig?.Shake(.14f); Sfx.Play(a.Type == "fire" ? "fire" : "hit");
                break;
            case "bolt":
            {
                var origin = GlobalPosition + Vector3.Up * Mathf.Max(.6f, (_formBody != null && IsInstanceValid(_formBody) ? _formBody.HeightM() * f.Scale : 1.2f) * .7f) + fw * .6f;
                var (tp, _) = AimPoint(80f); var dir = (tp - origin).Normalized(); if (dir.LengthSquared() < .5f) dir = fw;
                var hit = FormHit(f, a);
                var calc = new SkillCalc { Def = new SkillDef { Id = f.Key, Name = a.Name, Kind = "projectile", Type = a.Type }, Type = a.Type, Knock = a.Knock, Stun = a.Stun, Leech = a.Leech, Radius = a.Aoe };
                foreach (var kv in a.Status) calc.Status.Add((kv.Key, kv.Value, 1, 0));
                float baseDmg = hit.Crit ? hit.Damage / Mathf.Max(1f, Game.D.CritMult()) : hit.Damage;   // (the projectile applies the crit multiplier itself, SkillCalc.Hit)
                var p = Projectile.Spawn(a.Type == "fire" ? "fire" : "bolt", 'p', origin, dir * (a.Aoe > 0 ? 30f : 42f), baseDmg, 2.4f, hit.Crit, false, a.Aoe, a.Aoe > 0, col);
                p.Calc = calc; p.Tint = col;
                Vfx.Flash(origin, col, 1.6f, 3f, .12f); Sfx.Play(a.Type == "fire" ? "fire" : a.Type == "physical" ? "swing" : "zap", .8f);
                break;
            }
            case "drain":
            {
                var tgt = Targeting.Aim ?? AimPoint(a.Range).enemy ?? NearestFoe(a.Range);
                if (tgt == null || !IsInstanceValid(tgt) || tgt.Dead || tgt.GlobalPosition.DistanceTo(GlobalPosition) > a.Range * 1.15f)
                {   // nothing to drain: the cost comes back
                    Ui.Note("No foe in reach", new Color(.8f, .85f, 1f)); Mp = Mathf.Min(Game.D.MaxMp(), Mp + a.Mp); _fCd[idx] = 0; break;
                }
                var from = GlobalPosition + Vector3.Up * 1.1f; var to = tgt.GlobalPosition + Vector3.Up * tgt.HeightM() * .55f;
                Vfx.Lightning(from, to, col, .35f); tgt.TakeHit(FormHit(f, a)); Fx.Burst(from, col, 10, 2f, .5f, .12f, 1f, "twirl"); Sfx.Play("zap", .7f);
                break;
            }
            case "heal":
            {
                float pot = Mathf.Max(0, 1 + FK("healP") / 100f), amt = Game.D.MaxHp() * a.Heal * pot * HealIn;
                if (amt > 0) { Hp = Mathf.Min(Game.D.MaxHp(), Hp + amt); FloatText.Spawn(this, GlobalPosition + new Vector3(0, 2.2f, 0), "+" + Mathf.RoundToInt(amt), new Color(.5f, 1f, .6f), .8f); }
                FormBuff(f, idx, a); int n = 0;
                if (a.Radius > 0 && Net.Active && Net.I != null)
                {
                    var party = Net.I.MyParty;
                    foreach (var kv in Net.I.Heroes)
                    {
                        var h = kv.Value; if (!IsInstanceValid(h) || !h.HasPose || h.Dead || h.Zone != Net.MyZone) continue;
                        if (party != null && !party.Members.Contains(kv.Key)) continue;
                        var hd = h.GlobalPosition - GlobalPosition; hd.Y = 0; if (hd.Length() > a.Radius) continue;
                        Net.I.SendEnchant(kv.Key, Forms.AllyId(f, idx), pot, Mathf.Max(1f, a.Dur)); n++;
                    }
                }
                var hc = new Color(.55f, 1f, .65f);
                Fx.Ring(GlobalPosition, Mathf.Max(2.5f, a.Radius), hc, .5f); Vfx.Flash(GlobalPosition + Vector3.Up * 1.5f, hc, 3f, 6f, .4f); Sfx.Play("heal");
                if (n > 0) Ui.Note($"{a.Name} reaches {n} {(n == 1 ? "ally" : "allies")}", hc);
                break;
            }
            case "buff":
                FormBuff(f, idx, a); Fx.Ring(GlobalPosition, 3f, f.Col, .5f); Sfx.Play(f.Kind == "animal" ? "roar" : "shift", .6f, 1.3f);
                FloatText.Spawn(this, GlobalPosition + new Vector3(0, 2.3f, 0), a.Name, f.Col.Lightened(.3f));
                break;
            case "blink":
            {
                var d = _walkV.LengthSquared() > 1f ? _walkV.Normalized() : fw; var before = GlobalPosition;
                var motion = d * a.Range; var hit = MoveAndCollide(motion, true);
                float travel = hit != null ? Mathf.Max(0, hit.GetTravel().Length() - .2f) : a.Range;
                Fx.Burst(before + Vector3.Up, col, 20, 3f, .5f, .14f, 1f, "twirl");
                GlobalPosition = before + d * travel; _modelYaw = Util.YawTo(d.X, d.Z); _inv = Mathf.Max(_inv, .25f);
                Fx.Burst(GlobalPosition + Vector3.Up, col, 20, 3f, .5f, .14f, 1f, "twirl"); Vfx.Lightning(before + Vector3.Up, GlobalPosition + Vector3.Up, col, .2f); Sfx.Play("shift", .5f, 1.6f);
                FormBuff(f, idx, a);
                break;
            }
            case "track":
            {
                Enemy? best = null; float bd = a.Range;
                foreach (var n in GetTree().GetNodesInGroup("enemies"))
                    if (n is Enemy e && !e.Dead && e.Active) { float dd = e.GlobalPosition.DistanceTo(GlobalPosition); if (dd < bd) { bd = dd; best = e; } }
                if (best == null) { Ui.Note($"No scent within {a.Range:0} m", new Color(.8f, .85f, .9f)); break; }
                var to = best.GlobalPosition - GlobalPosition; string[] pts = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
                int oct = Mathf.PosMod(Mathf.RoundToInt(Mathf.Atan2(to.X, -to.Z) / (Mathf.Pi / 4)), 8);
                Ui.Note($"Scent: {best.Def.N} (level {best.Level}), {bd:0} m to the {pts[oct]}", f.Col.Lightened(.3f));
                Fx.Ring(best.GlobalPosition, 2.5f, f.Col, 1.2f); Vfx.Flash(best.GlobalPosition + Vector3.Up * 1.5f, f.Col, 2.5f, 6f, 1.2f);
                break;
            }
        }
    }
    /// <summary>Everything within r of a point takes the ability's hit.</summary>
    void FormArea(FormDef f, FormAbility a, Vector3 at, float r)
    {
        foreach (var n in GetTree().GetNodesInGroup("enemies"))
            if (n is Enemy e && !e.Dead && e.Active && new Vector2(e.GlobalPosition.X - at.X, e.GlobalPosition.Z - at.Z).Length() - e.Def.R * e.ScaleF() < r && Mathf.Abs(e.GlobalPosition.Y - at.Y) < 4f)
                e.TakeHit(FormHit(f, a));
    }
    /// <summary>An ability's timed stats on the hero (they end with the form).</summary>
    void FormBuff(FormDef f, int idx, FormAbility a)
    {
        if (a.Buff.Count == 0 || a.Dur <= 0) return; string id = f.Key + ".b" + idx;
        Buffs.RemoveAll(b => b.id == id); Buffs.Add((id, new Dictionary<string, float>(a.Buff), a.Dur)); Game.RecalcTree();
    }
}

/// <summary>The form's bar, drawn above the hotbar while transformed: the form's name and time left, and its abilities with their
/// keys and cooldowns (the hotbar's own skills are off until the hero changes back).</summary>
public partial class FormBar : Control
{
    static Font F => ThemeDB.FallbackFont;
    public override void _Ready() { MouseFilter = MouseFilterEnum.Ignore; SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); }
    public override void _Process(double delta) { QueueRedraw(); }
    public override void _Draw()
    {
        var p = Player.Instance; var f = p?.Form; if (p == null || f == null || !Ui.InGame) return;
        var sz = GetViewportRect().Size; int n = f.Abilities.Count; const float bw = 132, bh = 40, gap = 6;
        float w = n * bw + (n + 1) * gap + 96, x0 = (sz.X - w) / 2, y0 = sz.Y - 196;
        DrawRect(new Rect2(x0, y0, w, bh + 40), new Color(.04f, .035f, .03f, .82f)); DrawRect(new Rect2(x0, y0, w, bh + 40), f.Col with { A = .7f }, false, 1.5f);
        string head = $"{f.Name.ToUpperInvariant()} FORM   {Mathf.CeilToInt(p.FormLeft)} s";
        DrawString(F, new Vector2(x0 + 8, y0 + 15), head, HorizontalAlignment.Left, -1, 13, f.Col.Lightened(.35f));
        float frac = Mathf.Clamp(p.FormLeft / Mathf.Max(1f, p.FormTime), 0, 1);
        DrawRect(new Rect2(x0 + 8, y0 + 21, w - 16, 4), new Color(0, 0, 0, .6f)); DrawRect(new Rect2(x0 + 8, y0 + 21, (w - 16) * frac, 4), f.Col);
        for (int i = 0; i < n; i++)
        {
            var a = f.Abilities[i]; var r = new Rect2(x0 + gap + i * (bw + gap), y0 + 32, bw, bh); float cd = p.FormCd(i);
            bool can = cd <= 0 && p.Mp >= a.Mp && p.St >= a.St;
            DrawRect(r, new Color(.09f, .08f, .07f, .95f)); DrawRect(r, (can ? f.Col : new Color(.4f, .4f, .42f)) with { A = .8f }, false, 1f);
            if (cd > 0 && i > 0) DrawRect(new Rect2(r.Position, new Vector2(bw * Mathf.Clamp(cd / p.FormCdMax(i), 0, 1), bh)), new Color(0, 0, 0, .55f));
            string key = i == 0 ? Inputs.KeyOf(Inputs.Attack) : i == 1 ? Inputs.KeyOf("spell1") + " / " + Inputs.KeyOf(Inputs.Heavy) : Inputs.KeyOf("spell" + i);
            DrawString(F, r.Position + new Vector2(5, 14), key, HorizontalAlignment.Left, bw - 10, 11, UiTheme.Dim);
            DrawString(F, r.Position + new Vector2(5, 31), cd > 0 && i > 0 ? $"{a.Name}  {cd:0.0}" : a.Name, HorizontalAlignment.Left, bw - 10, 13, can ? Colors.White : new Color(.6f, .6f, .62f));
        }
        var rr = new Rect2(x0 + w - 90, y0 + 32, 84, bh);
        DrawRect(rr, new Color(.09f, .08f, .07f, .95f)); DrawRect(rr, new Color(.6f, .55f, .45f, .8f), false, 1f);
        DrawString(F, rr.Position + new Vector2(5, 14), Inputs.KeyOf("spell" + (int)Forms.Rule("revertKey", 6)), HorizontalAlignment.Left, 74, 11, UiTheme.Dim);
        DrawString(F, rr.Position + new Vector2(5, 31), "Change back", HorizontalAlignment.Left, 74, 13, Colors.White);
    }
}
