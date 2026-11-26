using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace TwinRealms;



/// <summary>The hero: movement, roll, four weapon types (sword, bow, wand, staff), block/parry, four spells, potions, interaction, horse.</summary>
public partial class Player : CharacterBody3D, IDamageable
{
    public static Player? Instance;

    public CameraRig? Rig;
    bool _sprWas; float _skidT;   // sprint start/stop feel
    public float Hp, Mp, St;
    public bool Dead { get; private set; }
    public bool Mounted { get; private set; }
    public string Prompt = "";
    public float ShiftFlash;
    public readonly StatusSet Statuses = new();   // statuses on the hero (enemy on-hit effects come in the next round)

    const float Accel = 40f, AirAccel = 10f;
    const float JumpVelocity = 6.0f, RollSpeed = 11f, RollTime = 0.45f;

    SwordTrail _trail = null!;
    Node3D _model = null!, _horse = null!;
    AttackDef? _atk; float _spinBase; float _capeX, _landT; bool _wasFloor = true;
    float _walkPh, _walkAmt, _idleT;
    float _gravity, _modelYaw, _rollT, _rollCd, _stLockT, _inv, _stagger, _deadT;
    Vector3 _rollDir, _kb;
    bool _lmbPrev; float _lmbHold;
    float _atkT, _atkDur, _comboT, _blockT, _queueT, _castT, _castDur; bool _queuePower, _hasQueue;
    int _combo; bool _atkPower, _hitDone, _blocking, _fired;
    ulong _noStMsg; float _airT; FootPlant? _feet;
    float _riposteT;   // after a parry: the next hit within this many seconds is a x2 riposte
    float RiposteMul() { if (_riposteT <= 0) return 1f; _riposteT = 0; FloatText.Spawn(this, GlobalPosition + new Vector3(0, 2.3f, 0), "Riposte!", new Color(1, .85f, .4f), 1.2f); return Tune.F("guard.riposteMul", 2f); }
    Vector3 _hitKb, _walkV; int _hurtSerial, _avHurt; Enemy? _magnet;   // knockback from blows, hit-clip restarts, the foe a melee swing steps in on
    public static float RunSpeed { get { Feel.Load(); return Feel.Run; } }        // data/feel.json "move"
    public static float SprintSpeed { get { Feel.Load(); return Feel.Sprint; } }
    float _heavyT; const float HeavyGather = .25f;   // heavy attack: seconds between pressing and the swing
    AttackStep? _step;   // the basic-attack chain step being swung (data/attacks.json)
    bool _pressPerfect, _stepPerfect, _allPerfect = true; float _stepTotal;   // combo rhythm: press landed in the last part of the previous step's recovery
    float _recT, _recDur, _recPending; string _recClip = "";   // weight: the pause after a swing before the next one (attacks.json "recovery")
    /// <summary>For the pose packet: which attack animation is playing (index in avatar.json attackAnims + 1, 0 = none) and when its blow lands (s).</summary>
    public int NetAtkAnim; public float NetAtkHit;
    /// <summary>For the test bots: a swing is playing / the pause after one is running.</summary>
    public bool Attacking => _atkT > 0;
    public bool Blocking => _blocking; public bool Rolling => _rollT > 0;   // read by the Shadow Duel boss
    public FootPlant? Feet => _feet;
    /// <summary>Fighting right now (the guarded stance); otherwise the hero stands relaxed.</summary>
    public bool InCombat => _combatT > 0 || _atkT > 0 || _castT > 0 || _blocking;
    float _combatT, _combatScan;
    void Fought() => _combatT = Mathf.Max(_combatT, Tune.F("player.combatStanceSeconds", 6)); public bool Recovering => _recT > 0 || (_atkT <= 0 && _recPending > 0);
    public AttackStep? CurrentStep => _step; public int ComboIndex => _combo;
    float _undyReady; string _shownType = "";
    float _regenAcc;

    public override void _Ready()
    {
        Instance = this;
        AddToGroup("player");
        CollisionMask = 1 | 2;
        _gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity");
        Hp = Game.D.MaxHp(); Mp = Game.D.MaxMp(); St = Game.D.MaxSt();
        AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 0.4f, Height = 1.8f }, Position = new Vector3(0, 0.9f, 0) });
        // stay on the ground over bumps and down hills (the default 0.1 m snap launched the hero off every downslope), same speed up and down
        FloorSnapLength = .6f; FloorMaxAngle = Mathf.DegToRad(50); FloorConstantSpeed = true; FloorStopOnSlope = true;
        BuildModel();
        Game.Changed += OnGearChanged;
        RefreshWeapon();
    }

    public override void _ExitTree() { Game.Changed -= OnGearChanged; if (Instance == this) Instance = null; }
    /// <summary>Alt-tab while holding a key: the release never arrives, so drop held movement and sprint.</summary>
    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusOut || what == NotificationWMWindowFocusOut)
            foreach (var a in new[] { Inputs.Sprint, Inputs.Forward, Inputs.Back, Inputs.Left, Inputs.Right, Inputs.Attack, Inputs.Guard }) Input.ActionRelease(a);
    }
    void OnGearChanged() { Hp = Mathf.Min(Hp, Game.D.MaxHp()); Mp = Mathf.Min(Mp, Game.D.MaxMp()); St = Mathf.Min(St, Game.D.MaxSt()); RefreshWeapon(); }

    // ------------------------------------------------------------------ model
    HeroRig _rig = null!; OmniLight3D _glow = null!;
    public HeroRig Rig3D => _rig;
    void BuildModel()
    {
        _rig = new HeroRig { Name = "Model" }; AddChild(_rig); _model = _rig;
        _rig.Build(Races.Look(Game.D));
        if (Avatar.Available)
        {
            // the drawn hero: base body + separate gear models. HeroRig stays as the invisible pose source (hand targets, look-at).
            var ap = Races.Look(Game.D); _av = new Avatar { Name = "Avatar", Scale = new Vector3(1 / ap.Bulk, 1 / ap.Height, 1 / ap.Bulk) };
            _rig.AddChild(_av); _av.Build(ap, Game.D.Name); _av.OwnHeadLayer = true; HideRig();   // head pieces on their own layer: the first-person camera skips them _feet = FootPlant.Attach(_av, GetRid());   // feet on slopes
            if (_feet != null) _feet.Stepped = at => { float sp = new Vector2(Velocity.X, Velocity.Z).Length(); if (sp > .6f && IsOnFloor() && !Mounted) { Sfx.Step(at, this, sp); Rig?.StepBob(); } };   // footsteps on the real footfalls   // the cape is an equipment slot now (Avatar.Dress)
        }
        _trail = new SwordTrail(); AddChild(_trail);
        // a soft light around the hero that grows in the dark (night, Shadow realm, dungeons)
        _glow = new OmniLight3D { LightColor = new Color(1f, .86f, .65f), OmniRange = 9f, LightEnergy = 0f, ShadowEnabled = false, Position = new Vector3(0, 2.3f, .3f), LightSpecular = .1f };
        AddChild(_glow);

        _horse = new Node3D { Visible = false }; AddChild(_horse);
        var hm = new StandardMaterial3D { AlbedoColor = new Color(0.4f, 0.26f, 0.16f) };
        _horse.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.7f, 0.8f, 2.0f) }, MaterialOverride = hm, Position = new Vector3(0, 1.0f, 0) });
        _horse.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.35f, 0.7f, 0.9f) }, MaterialOverride = hm, Position = new Vector3(0, 1.5f, -1.2f), Rotation = new Vector3(-0.5f, 0, 0) });
        for (int i = 0; i < 4; i++) _horse.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.18f, 0.9f, 0.18f) }, MaterialOverride = hm, Position = new Vector3(i % 2 == 0 ? 0.25f : -0.25f, 0.45f, i < 2 ? -0.8f : 0.8f) });
    }

    string _gearSig = "", _atkClass = "";
    /// <summary>Rebuilds the visible gear when anything equipped changes (weapon, shield, armor, helmet, gloves, boots, trinket).</summary>
    void RefreshWeapon()
    {
        var d = Game.D; var sig = d.WType();
        foreach (var s in PlayerStats.Slots) { var it = d.Eq.GetValueOrDefault(s); sig += "|" + (it == null ? "-" : it.Name + it.Rar + it.Up + it.Glow); }
        if (sig == _gearSig) return; _gearSig = sig;
        // the weapon class's basic attack goes on the attack button automatically (data/attacks.json)
        string wt = d.WType();
        if (wt != _atkClass && Attacks.HasStyle(wt))
        {
            bool first = _atkClass != ""; _atkClass = wt; var b = Attacks.Build(wt); _combo = 0; _comboT = 0; _hasQueue = false;   // a click buffered for the old weapon does not carry over
            if (first) Ui.Note($"{Attacks.StyleName(wt)} on {Inputs.KeyOf(Inputs.Attack)}: {string.Join(" > ", b.Chain.Select(s => Attacks.Step(wt, s)?.Name ?? s))}  ({Inputs.KeyOf(Inputs.AttacksUi)} to edit)", new Color(1f, .85f, .5f));
        }
        _rig.ApplyGear(d);
        _trail.Base = _rig.BladeBase; _trail.Tip = _rig.BladeTip; _trail.Col = Weapons.Get(d.WType()).Trail;
        if (_av != null)
        {
            HideRig();
            _av.Dress(s => d.Eq.GetValueOrDefault(s), d.WType(), Classes.Get(d.Class).Tunic);
            if (_av.BladeTip != null) { _trail.Base = _av.BladeBase; _trail.Tip = _av.BladeTip; }
            if (d.Weapon is { } wi && GearFx.Get(wi.Elem) is { } el) _trail.Col = el.Col;   // elemental weapons swing in their element's colour
        }
    }
    Avatar? _av; int _atkSerial, _avAtk, _castSerial, _avCast; bool _avRolling;
    /// <summary>For test bots: the clip the model is playing and the model's facing (radians).</summary>
    public Avatar? Av => _av; public string AvClip => _av?.Cur ?? ""; public float ModelYaw => _modelYaw; public bool Backpedal => _backpedal;
    /// <summary>The way the hero faces, flat.</summary>
    public Vector3 Facing() => new(-Mathf.Sin(_modelYaw), 0, -Mathf.Cos(_modelYaw));
    bool _backpedal;
    /// <summary>Hides the procedural hero meshes when the model is drawn (HeroRig still runs for hand targets).</summary>
    void HideRig() { void H(Node n) { foreach (var c in n.GetChildren()) { if (c == _av) continue; if (c is GeometryInstance3D g) g.Visible = false; H(c); } } H(_rig); }
    bool HasShield => Game.D.Eq.GetValueOrDefault("shield") != null && Weapons.Get(Game.D.WType()).Hands < 2;
    WeaponDef W => Weapons.Get(Game.D.WType());
    bool Ranged => W.Ranged;

    void UseStamina(float n) { St = Mathf.Max(0, St - n); _stLockT = 0.7f; }

    // ------------------------------------------------------------------ progression
    public void AddXp(int xp, Vector3 at)
    {
        int ups = Game.D.GainXp(xp);
        FloatText.Spawn(this, at + new Vector3(0, 1.2f, 0), "+" + xp + " xp", new Color(0.6f, 0.9f, 1f), 0.8f);
        if (ups > 0)
        {
            Hp = Game.D.MaxHp(); Mp = Game.D.MaxMp(); St = Game.D.MaxSt();
            FloatText.Spawn(this, GlobalPosition + new Vector3(0, 2.6f, 0), "LEVEL UP " + Game.D.Level, new Color(1, 0.85f, 0.3f), 1.4f);
            Ui.Banner("Level " + Game.D.Level, $"+{Progression.AttrPerLevel * ups} attribute, +{Progression.RankPerLevel * ups} skill, +{Progression.TreePerLevel * ups} skill tree points"); Sfx.Play("level");
            Fx.Ring(GlobalPosition, 4f, new Color(1, 0.85f, 0.3f), 0.7f);
        }
        Game.Notify();
    }

    // ------------------------------------------------------------------ damage in
    float _pdT, _pdCd;   // perfect dodge: counter window left, cooldown left (real seconds)
    float _rollTrailT; bool _rollPd; Vector3 _rollFrom;   // roll smoke trail timer, perfect-dodge tint flag, roll start position
    void PerfectDodge()
    {
        double now = Time.GetTicksMsec() / 1000.0; if (now < _pdCd) return;
        _rollPd = true; Fx.Ring(_rollFrom + Vector3.Up * .1f, 2.2f, new Color(.85f, .95f, 1f), .3f);
        _pdCd = (float)now + Tune.F("perfectDodge.cooldown", 2f); _pdT = Tune.F("perfectDodge.counterTime", 1.2f);
        Feel.SlowMo(Tune.F("perfectDodge.slowScale", .35f), Tune.F("perfectDodge.slowTime", .35f));
        Ui.Flash(new Color(.85f, .95f, 1f)); Sfx.Play("block");
        FloatText.Spawn(this, GlobalPosition + new Vector3(0, 2.3f, 0), "Perfect dodge!", new Color(.8f, .95f, 1f), 1.3f);
    }

    public void TakeHit(HitInfo h)
    {
        if (Dead || World.Shifting || _moveT > 0) return;
        if (h.Attacker is Enemy byFoe) PetWorld.HeroHitBy(byFoe);   // a defensive pet answers whoever strikes its master
        if (_rollT > 0 && h.Attacker is Enemy && RollTime - _rollT <= Tune.F("perfectDodge.window", .18f)) PerfectDodge();
        if (_inv > 0 || _rollT > 0) return;
        var D = Game.D; var TS = Game.TS;
        // evade: a clean dodge (Halfling, Ranger passives)
        if (GD.Randf() < Mathf.Clamp(TS["evade"], 0, 60) / 100f) { FloatText.Spawn(this, GlobalPosition + new Vector3(0, 2.2f, 0), "Evade", new Color(.75f, .95f, 1f), .9f); _inv = .15f; return; }
        // physical damage against defense; other types against magic resist (half defense + mr), then the per-type resistance (capped at 75%)
        // armor is measured against the attacker's level (data/progression.json scaling.armor), so it keeps its value without stacking to immunity
        int atkLvl = h.Attacker is Enemy al && IsInstanceValid(al) ? al.Level : Mathf.RoundToInt(Progression.RefLevel());
        float baseDmg = h.Type == "physical" || h.Type == "" ? h.Damage * Progression.ArmorTaken(D.Defense(), atkLvl)
            : h.Type == "true" ? h.Damage : h.Damage * Progression.ArmorTaken(D.Defense() * .5f + TS["mr"], atkLvl) * (1 - Mathf.Clamp(TS["res_" + h.Type], -100, 75) / 100f);
        if (h.Attacker is Enemy ae && IsInstanceValid(ae) && !ae.Dead) Targeting.NoteHostile(ae);   // auto target "monsters that hit you"
        baseDmg *= Statuses.TakenMul;
        if (h.Attacker is Enemy src && IsInstanceValid(src)) baseDmg *= src.Status.DealtMul;
        var toSrc = h.From - GlobalPosition; toSrc.Y = 0;
        float srcYaw = Util.YawTo(toSrc.X, toSrc.Z);
        bool front = toSrc.LengthSquared() < 0.01f || Mathf.Abs(Util.AngDiff(_modelYaw, srcYaw)) < 1.25f;
        float dmg = baseDmg; var away = -toSrc; away.Y = 0;
        if (_blocking && front && !h.NoBlock)
        {
            var shieldPos = GlobalPosition + new Vector3(0, 1.3f, 0) + new Vector3(-Mathf.Sin(_modelYaw), 0, -Mathf.Cos(_modelYaw)) * 0.7f;
            if (_blockT < Tune.F("guard.parryWindow", .15f) && h.Attacker is Enemy en && !h.IsProjectile)
            {
                en.Stun(Tune.F("guard.parryStun", .8f)); _inv = 0.3f; Sfx.Play("block"); Inputs.Rumble(.6f, .8f, .2f); _riposteT = Tune.F("guard.riposteTime", 1.5f);
                FloatText.Spawn(this, GlobalPosition + new Vector3(0, 2.3f, 0), "Parry!", new Color(1, 0.88f, 0.5f), 1.3f);
                Fx.Burst(shieldPos, new Color(1, .97f, .8f), (int)Tune.F("guard.parrySparks", 22f), 6f, 0.4f, 0.08f);
                Feel.HitStop(Tune.F("guard.hitStop", .04f) * 1.5f);
                return;
            }
            float red = Mathf.Clamp(0.55f + D.ShieldDef * 0.012f + TS["block"] / 100f, 0.5f, 0.92f);
            dmg = baseDmg * (1f - red); UseStamina(h.Damage * Tune.F("stamina.blockPerDamage", .3f)); Sfx.Play("block"); Inputs.Rumble(.3f, .15f, .1f);
            Fx.Burst(shieldPos, new Color(1, .95f, .8f), (int)Tune.F("guard.sparks", 10f), 4f, 0.25f, 0.05f);
            Feel.HitStop(Tune.F("guard.hitStop", .04f));
            if (away.LengthSquared() > 0.001f) _hitKb += away.Normalized() * Tune.F("guard.pushBack", .3f) * 1.4f;
            if (St <= 0) { _blocking = false; _stagger = Tune.F("guard.breakStagger", .6f); FloatText.Spawn(this, GlobalPosition + new Vector3(0, 2.3f, 0), "Guard broken", new Color(1, 0.6f, 0.45f)); }
        }
        else
        {
            Sfx.Play("hurt"); Inputs.Rumble(.35f, h.Stagger || h.Stun > 0 ? .9f : .45f, h.Stagger || h.Stun > 0 ? .3f : .16f);
            // the blow lands on the body: a real shove, a stagger long enough for the hit clip, a spark where it struck
            var hitDir = toSrc.LengthSquared() > .001f ? toSrc.Normalized() : Vector3.Forward;
            // only abilities (slams, blasts, skills that stun or knock back) stagger: a shove, the flinch clip, and whatever the hero was doing is interrupted.
            // Ordinary blows just hurt: the spark shows where they struck, attacks and casts carry on.
            bool stagger = (h.Stagger || h.Stun > 0) && Game.TS["ccImmune"] <= 0;   // Unbreakable Will: no flinch
            if (stagger)
            {
                if (away.LengthSquared() > 0.001f) _hitKb += away.Normalized() * h.Knock * .9f;
                _stagger = Mathf.Max(_stagger, Mathf.Max(h.Stun, h.Knock >= 8f ? .55f : .35f)); _hurtSerial++;
                _atkT = 0; CancelCast();
            }
            SpellFx.Play("hit_spark", GlobalPosition + new Vector3(0, 1.25f, 0) + hitDir * .3f, stagger ? 1.6f : 1f);
        }
        if (TS.Has("manaShield") && Mp > 0) { float m = Mathf.Min(Mp, dmg * 0.35f); Mp -= m; dmg -= m; }
        if (TS["thorns"] > 0 && h.Attacker is Enemy te && !h.IsProjectile && te.GlobalPosition.DistanceTo(GlobalPosition) < 6f)
            te.TakeHit(new HitInfo { Damage = h.Damage * TS["thorns"] / 100f, From = GlobalPosition, IsPlayer = true, Attacker = this });
        if (Ward > 0 && dmg > 0)
        {
            // a shield (Stoneskin, Aegis Chorus) soaks the blow first
            float soak = Mathf.Min(Ward, dmg); Ward -= soak; dmg -= soak;
            if (dmg <= .5f) { FloatText.Spawn(this, GlobalPosition + new Vector3(0, 2.1f, 0), "Absorbed " + Mathf.RoundToInt(soak), new Color(.7f, .9f, 1f), .7f); _inv = .2f; Fought(); return; }
        }
        int final = Mathf.Max(1, Mathf.RoundToInt(dmg));
        Hp -= final; _inv = 0.2f; Fought();
        Flag.Event($"took {final} {h.Type} ({h.Damage:0} before armor, {100f * final / Mathf.Max(1, D.MaxHp()):0}% of max hp) from {(h.Attacker is Enemy fe && IsInstanceValid(fe) ? fe.Def.N + " Lv" + fe.Level : "?")}{(_blocking ? ", blocked" : "")}, hp left {Mathf.Max(0, Hp):0}");
        // the attacker's on-hit statuses (tenacity shortens them), then "when hit" triggers
        if (h.Statuses != null) foreach (var (id, chance, durMul, pow) in h.Statuses) if (GD.Randf() < chance) Statuses.Add(id, durMul, pow, h.Damage, TS["tenacity"]);
        TriggerAuto("hit");
        bool hard = h.Stagger || h.Stun > 0;
        Rig?.Shake(hard ? 0.18f : 0.07f); if (hard) Feel.HitStop(Feel.Taken);
        FloatText.Spawn(this, GlobalPosition + new Vector3(0, 2.1f, 0), "-" + final, h.Type is "physical" or "" ? new Color(1, 0.4f, 0.35f) : Dmg.TypeColor(h.Type).Lerp(new Color(1, .3f, .3f), .3f));
        if (Hp <= 0 && !DuelSave())
        {
            if (TS.Has("undying") && World.Clock > _undyReady)
            {
                Hp = Mathf.Max(1, D.MaxHp() * 0.3f); _undyReady = World.Clock + 180f; _inv = 2f;
                Ui.Note("Undying: you refuse to fall", new Color(1, 0.85f, 0.3f)); Fx.Ring(GlobalPosition, 5f, new Color(1, 0.85f, 0.3f), 0.6f);
            }
            else Die();
        }
    }

    void Die()
    {
        Flag.Event("DIED"); CancelChannel(); CancelCast(); Dead = true; Statuses.Clear(); Flying = false;   // death cleanses: nothing burns or slows the hero after respawning
        Hp = 0; _deadT = 0; _atkT = 0; _blocking = false; if (Mounted) ToggleMount(true);
        if (LairRules) Ui.Note($"You are down. A party member can revive you; press {Inputs.KeyOf(Inputs.Use)} to give up.", new Color(1f, .7f, .5f));
        Sfx.Play("die");
        if (Arenas.Active) Arenas.OnPlayerDeath();
    }

    public void Revive(Vector3 pos)
    {
        Dead = false; Statuses.Clear(); _stagger = 0; Hp = Game.D.MaxHp(); Mp = Game.D.MaxMp(); St = Game.D.MaxSt(); _model.Rotation = new Vector3(0, _modelYaw, 0);
        GlobalPosition = pos; Velocity = Vector3.Zero; _inv = 2f;
        ResetActions(); _grabBy = null; Flying = false;   // nothing from before the death carries over (a stuck action locked the skills)
    }

    float _liftT, _liftTop;
    /// <summary>Called every physics frame by an updraft the player stands in.</summary>
    public void Lift(float top) { _liftT = .15f; _liftTop = top; }

    public void Teleport(Vector3 pos, float yaw = 0f)
    {
        GlobalPosition = pos; Velocity = Vector3.Zero; _kb = Vector3.Zero; _hitKb = Vector3.Zero; _walkV = Vector3.Zero; _hasQueue = false; if (_castT > 0 || _atkSkill != null || _moveT > 0 || _spinLeft > 0) { CancelCast(); _moveT = 0; _moveSkill = null; }   // a leap in flight would drag us back
        if (Rig != null) { Rig.GlobalPosition = pos + new Vector3(0, 1.6f, 0); Rig.Rotation = new Vector3(0, yaw, 0); }
        _modelYaw = yaw;
    }

    // ------------------------------------------------------------------ mount
    public void ToggleMount(bool forceOff = false)
    {
        if (forceOff || Mounted)
        {
            Mounted = false; _horse.Visible = false;
            if (_petMount != null) { _petMount.QueueFree(); _petMount = null; _petBody = null; _mountY = .9f; PetWorld.Riding = false; foreach (var c in _horse.GetChildren()) if (c is MeshInstance3D mi) mi.Visible = true; }
            return;
        }
        bool pet = PetWorld.CanMount;
        if (!Game.D.Horse && !pet) { Ui.Note("You do not own a horse. Brynn sells one. (A mountable pet can carry you too.)", new Color(0.7f, 0.8f, 1)); return; }
        if (Dungeon.Active || Arenas.Active) { Ui.Note("No riding here", new Color(0.7f, 0.8f, 1)); return; }
        Mounted = true; _horse.Visible = true;
        if (pet)
        {   // ride the active pet: its own model (at riding size) instead of the box horse, which stays as the fallback
            var s = PetWorld.Active!; var sp = s.Sp(); PetWorld.Riding = true; PetWorld.Despawn();
            foreach (var c in _horse.GetChildren()) if (c is MeshInstance3D mi) mi.Visible = false;
            var holder = new Node3D { Scale = Vector3.One * sp.MountScale }; _horse.AddChild(holder);
            _petBody = new Enemy { Type = s.Species, PetBody = true, Level = s.Level, AnyRealm = true }; holder.AddChild(_petBody); _petMount = holder;
            _mountY = Mathf.Clamp(_petBody.HeightM() * sp.MountScale * .62f, .5f, 2.2f);
            Ui.Note($"You ride {s.Name()}", new Color(0.8f, 0.9f, 1));
        }
        else Ui.Note("You mount your horse", new Color(0.8f, 0.9f, 1));
    }
    Node3D? _petMount; Enemy? _petBody; float _mountY = .9f;   // a ridden pet (PetWorld.Riding)

    // ------------------------------------------------------------------ aiming
    public (Vector3 point, Enemy? enemy) AimPoint(float maxDist)
    {
        if (Targeting.Aim is Enemy lt && IsInstanceValid(lt) && !lt.Dead) return (lt.GlobalPosition + new Vector3(0, lt.HeightM() * .55f, 0), lt);
        if (Rig == null) return (GlobalPosition + new Vector3(0, 1.4f, 0) + new Vector3(-Mathf.Sin(_modelYaw), 0, -Mathf.Cos(_modelYaw)) * maxDist, null);
        // the screen-centre ray (includes the over-the-shoulder offset)
        var cam = Rig.Camera; var mid = cam.GetViewport().GetVisibleRect().Size / 2f; var from = cam.ProjectRayOrigin(mid); var dir = cam.ProjectRayNormal(mid);
        var q = PhysicsRayQueryParameters3D.Create(from, from + dir * maxDist, 3);
        q.Exclude = new Godot.Collections.Array<Rid> { GetRid() };
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(q);
        if (hit.Count > 0) return ((Vector3)hit["position"], hit["collider"].As<GodotObject>() as Enemy);
        return (from + dir * maxDist, null);
    }

    /// <summary>
    /// Where shots and spells leave from: the casting hand. Every ranged clip (spell cast, bow aim and release) reaches out with the
    /// left hand, and bows and wands are held in it, so it is the left hand a little ahead of the fingers.
    /// </summary>
    Vector3 MuzzlePos()
    {
        var fw = new Vector3(-Mathf.Sin(_modelYaw), 0, -Mathf.Cos(_modelYaw));
        // bows: the arrow rest on the held bow (just above the grip hand), not the pistol hand position
        if (_av != null && !Mounted && W.Id == "bow" && _av.BowNock() is { } nk && nk.DistanceTo(GlobalPosition) < 2.5f) return nk;
        if (_av != null && !Mounted && _av.HandPos(true) is { } h && h.DistanceTo(GlobalPosition) < 2.5f) return h + fw * .25f;
        return GlobalPosition + new Vector3(0, 1.45f + (Mounted ? _mountY : 0f), 0) + fw * 0.6f;
    }
    /// <summary>World direction the bow pose aims (from the shoulders to the aim point; pitch kept within 50 degrees).</summary>
    Vector3 BowAim()
    {
        var (tp, _) = AimPoint(80f); var d = tp - (GlobalPosition + new Vector3(0, 1.45f, 0));
        var fw = new Vector3(-Mathf.Sin(_modelYaw), 0, -Mathf.Cos(_modelYaw));
        if (d.LengthSquared() < .01f) return fw;
        d = d.Normalized(); float flat = new Vector2(d.X, d.Z).Length(); float pitch = Mathf.Clamp(Mathf.Atan2(d.Y, flat), -.87f, .87f);
        return fw * Mathf.Cos(pitch) + Vector3.Up * Mathf.Sin(pitch);   // along the body's facing, so the arms never twist across it
    }

    // ------------------------------------------------------------------ attacks
    bool TryAttack(bool power)
    {
        // a swing that just ended starts its recovery first (input is read before the attack timeline each frame)
        if (_atkT <= 0 && _recPending > 0) { _recT = _recDur = _recPending; _recPending = 0; _recClip = _step?.Rec ?? ""; }
        if (Dead || _atkT > 0 || _rollT > 0 || _stagger > 0 || _castT > 0 || _recT > 0) return false;   // _recT: still recovering from a heavy swing
        if (Mounted) { Ui.Note("Dismount to fight (T)", new Color(0.7f, 0.8f, 1)); return false; }
        var w = W; bool ranged = w.Ranged;
        float cost = ranged ? (power ? Tune.F("stamina.heavyRanged", 16) : Tune.F("stamina.lightRanged", 7)) : (power ? Tune.F("stamina.heavyMelee", 26) : Tune.F("stamina.lightMelee", 9)) * (w.Hands > 1 ? Tune.F("stamina.twoHandedMul", 1.25f) : w.Id == "dagger" ? Tune.F("stamina.daggerMul", .6f) : w.Id == "unarmed" ? Tune.F("stamina.unarmedMul", .55f) : 1f);   // data/tuning.json "game.stamina"
        if (St < 4f)
        {   // one message at a time: queued and repeated presses used to stack several on top of each other
            if (Time.GetTicksMsec() - _noStMsg > 1000) { _noStMsg = Time.GetTicksMsec(); FloatText.Spawn(this, GlobalPosition + new Vector3(0, 2.2f, 0), "Out of stamina", new Color(0.6f, 0.8f, 1f), 0.8f); }
            return false;
        }
        // the basic attack of the equipped weapon class (data/attacks.json): light attacks walk the player's chain, power attacks reset it
        int chainLen = Attacks.HasStyle(w.Id) ? Attacks.Build(w.Id).Chain.Count : w.Combo.Count;
        if (!power) _combo = _comboT > 0 ? (_combo + 1) % chainLen : 0; else _combo = 0;
        if (Attacks.HasStyle(w.Id)) { _step = Attacks.Resolve(w, _combo, power); _atk = _step.Def; }
        else { _step = null; _atk = power ? w.Power : w.Combo[Mathf.Min(_combo, w.Combo.Count - 1)]; }
        UseStamina(cost * (_step?.Stamina ?? 1f));
        _stepPerfect = !power && _combo > 0 && _pressPerfect; _pressPerfect = false;
        if (_combo == 0 || power) _allPerfect = true; else _allPerfect &= _stepPerfect;
        Fought(); _atkPower = power; _atkDur = _atk.Dur / (Game.D.AtkSpeed() * w.Speed * (_step?.Speed ?? 1f) * (_stepPerfect ? Feel.ComboF("perfectSpeed", 1.1f) : 1f)); _atkT = _atkDur;
        if (_stepPerfect) SpellFx.Play("hit_spark", _trail.Tip?.GlobalPosition ?? GlobalPosition + Vector3.Up, .5f);   // faint gold glint at the blade
        _hitDone = false; _atkSerial++;   // (devloop #18 swallowed this line into the comment above: every attack after the first never landed)
        _recPending = (_step?.Recover ?? (power ? .5f : .05f)) / Game.D.AtkSpeed(); _stepTotal = _atkDur + _recPending; _comboT = _stepTotal + Tune.F("player.chainWindow", 1.3f);   // recovery counts toward keeping the chain going; then this long to press again before it starts over
        NetAtkAnim = _step != null && _step.Anim != "" ? Avatar.AttackIndex(_step.Anim) : 0; NetAtkHit = _atk.Hit * _atkDur;
        float camYaw = Rig?.Yaw ?? _modelYaw; float face = camYaw;
        var tgt = Targeting.Aim; _magnet = null;
        if (tgt != null) { var d = tgt.GlobalPosition - GlobalPosition; face = Util.YawTo(d.X, d.Z); if (!ranged) _magnet = tgt; }
        else if (!ranged)
        {
            // the foe in front within reach + 2.2 m: face it, and step in on it during the wind-up (Magnet)
            float best = _atk.Range + 2.2f;
            foreach (var n in GetTree().GetNodesInGroup("enemies"))
            {
                if (n is not Enemy e || e.Dead || !e.Active) continue;
                var d = e.GlobalPosition - GlobalPosition; d.Y = 0; float dd = d.Length() - e.Def.R * e.ScaleF();
                if (dd < best && Mathf.Abs(Util.AngDiff(camYaw, Util.YawTo(d.X, d.Z))) < 1.05f) { best = dd; face = Util.YawTo(d.X, d.Z); _magnet = e; }
            }
        }
        if (_magnet != null && _magnet.GlobalPosition.DistanceTo(GlobalPosition) - _magnet.Def.R * _magnet.ScaleF() > _atk.Range + 2.4f) _magnet = null;
        _modelYaw = face; _spinBase = face;
        Sfx.Weapon(w.Id, "swing");   // the weapon type's own swing (data/weapons.json "sfx"): blade whoosh, heavy whoosh, bow creak...
        return true;
    }

    /// <summary>
    /// Melee step-in: during the wind-up the hero closes on the chosen foe to the swing's sweet spot (60% of its reach from the
    /// foe's edge), arriving at the impact frame, and keeps facing it. Swings at the edge of range connect instead of whiffing.
    /// </summary>
    void Magnet(float prog, float dt, ref Vector3 v)
    {
        if (_magnet == null || !IsInstanceValid(_magnet) || _magnet.Dead || _atk == null || prog >= _atk.Hit) return;
        var d = _magnet.GlobalPosition - GlobalPosition; d.Y = 0; float edge = d.Length() - _magnet.Def.R * _magnet.ScaleF();
        _modelYaw = Mathf.LerpAngle(_modelYaw, Util.YawTo(d.X, d.Z), 1f - Mathf.Exp(-18f * dt)); _spinBase = _modelYaw;
        float want = _atk.Range * .6f, gap = edge - want; if (gap <= .05f) return;
        float left = Mathf.Max(.04f, (_atk.Hit - prog) * _atkDur), sp = Mathf.Min(gap / left, 11f);
        var dir = d.Normalized(); v.X = dir.X * sp; v.Z = dir.Z * sp;
    }

    /// <summary>Melee hit for the current attack: arc, reach, knockback, stun and knock-up come from the attack data, then passives modify them.</summary>
    void ResolveMelee(AttackDef a)
    {
        var D = Game.D; var TS = Game.TS; var w = W;
        bool fin = a.Finisher, cleave = TS.Has("cleave") && (_atkPower || fin);
        float range = a.Range + (cleave ? .8f : 0f) + TS["reach"] / 100f * a.Range, arc = cleave ? Mathf.Pi : Mathf.DegToRad(Mathf.Min(180, a.Arc));
        float mult = a.Dmg * (fin ? 1f + TS["finisherP"] / 100f : 1f) * (_atkPower ? 1f + TS["powerP"] / 100f : 1f);
        float stun = a.Stun + (fin ? TS["finisherStun"] : 0f), up = a.Up + (fin ? TS["finisherUp"] : 0f), knock = a.Knock * (1f + TS["knockP"] / 100f);
        int hits = 0; bool pdOn = _pdT > 0;   // perfect dodge counter: this swing hits harder
        if (pdOn) mult *= Tune.F("perfectDodge.counterMul", 1.5f);
        foreach (var n in GetTree().GetNodesInGroup("enemies"))
        {
            if (n is not Enemy e || e.Dead || !e.Active) continue;
            var d = e.GlobalPosition - GlobalPosition; d.Y = 0; float dist = d.Length() - e.Def.R * e.ScaleF();
            if (dist > range) continue;
            if (pdOn && _pdT > 0) { _pdT = 0; Sfx.Play("block", 1.2f); }   // counter spent on first target (re-check range checks below are fine: bonus applies this swing)
            if (dist > 0.6f && arc < Mathf.Pi - .01f && Mathf.Abs(Util.AngDiff(_modelYaw, Util.YawTo(d.X, d.Z))) > arc + (e.Def.R > 1 ? 0.5f : 0f)) continue;
            if (Mathf.Abs(e.GlobalPosition.Y - GlobalPosition.Y) > 3.5f + e.Def.R) continue;
            bool crit = GD.Randf() < D.CritChance() + (w.Crit + a.CritBonus) / 100f;
            float dmg = D.WpnDmg() * (float)GD.RandRange(0.9, 1.1) * mult * (crit ? D.CritMult() : 1f) * RiposteMul();
            var hi = new HitInfo { Damage = dmg, From = GlobalPosition, Knock = knock, Stun = stun, Up = up, Crit = crit, Attacker = this, IsPlayer = true, Kind = _atkPower ? "power" : fin ? "combo" : "light" };
            foreach (var kv in w.OnHit) hi.With(kv.Key, kv.Value * (fin || _atkPower ? 1.5f : 1f));
            if (GearFx.Get(D.Weapon?.Elem) is { } el && el.Status != "") hi.With(el.Status, el.Proc * (fin || _atkPower ? 1.5f : 1f) * (_step?.ElemMul ?? 1f));   // elemental weapon
            if (_step != null) foreach (var (sid, ch) in _step.Status) hi.With(sid, ch);   // the chain step and its modifiers
            e.TakeHit(hi); if (crit) TriggerAuto("crit");
            Feel.Impact(dmg, e.MaxHp, w.Id is "greatsword" or "mace" or "axe" or "hammer" || _atkPower || fin, crit, e.Dead, new Vector3(d.X, 0, d.Z), Rig);
            // where steel meets the foe: a spark on its near side, bigger for crits, finishers and heavies
            var toMe = -d.Normalized(); float big = crit ? 1.6f : fin || _atkPower ? 1.4f : 1f;
            var hitPt = e.GlobalPosition + new Vector3(0, e.HeightM() * .55f, 0) + toMe * e.Def.R * e.ScaleF() * .8f;
            SpellFx.Play("hit_spark", hitPt, big);
            if (crit) CritFx(e, hitPt, new Vector3(d.X, 0, d.Z).Normalized());
            hits++;
        }
        if (hits > 0) { Rig?.Shake(_atkPower ? Tune.F("heavy.shake", .32f) : fin ? 0.15f : 0.08f); Sfx.Weapon(W.Id, "hit"); if (_atkPower) Fx.Dust(GlobalPosition + GlobalTransform.Basis.Z * -1.2f, Tune.F("heavy.dust", 1.4f)); }   // a connecting heavy blow kicks up dust
        else Sfx.Weapon(W.Id, "miss");   // the swing connected nothing
        if (fin && hits > 0 && !_atkPower)
        {   // finisher lands: gold shockwave + dust + low thump
            var gp = GlobalPosition + Vector3.Up * .08f;
            Vfx.Ring(gp, Mathf.Max(2f, range * Feel.ComboF("ringScale", 1.1f)), new Color(1f, .82f, .4f), Feel.ComboF("ringTime", .45f));
            Fx.Dust(GlobalPosition + GlobalTransform.Basis.Z * -1f, Feel.ComboF("dust", 1.2f)); Rig?.Shake(Feel.ComboF("shake", .22f));
            Sfx.Weapon(W.Id, "hit", Feel.ComboF("thumpPitch", .75f), .8f);   // the finisher: the weapon's own hit, lower
            if (_allPerfect && _combo > 0) FloatText.Spawn(this, GlobalPosition + new Vector3(0, 2.4f, 0), "Rhythm", new Color(1f, .85f, .4f), 1f);
        }
        if ((_atkPower || fin) && (a.Up > 0 || a.Arc >= 180 || stun > .5f)) { Vfx.Ring(GlobalPosition + Vector3.Up * .08f, range * .9f, w.Trail, .35f); }
        // the block world: the blade meets whatever block is in front at chest height (axes cut wood and leaves, weak swings do nothing to stone)
        var fwd = new Vector3(-Mathf.Sin(_modelYaw), 0, -Mathf.Cos(_modelYaw));
        string? bel = GearFx.Get(D.Weapon?.Elem) is { } ge && ge.Status != "" ? D.Weapon!.Elem : "physical";
        BlockFx.Basic(GlobalPosition + Vector3.Up * .9f + fwd * Mathf.Min(range, 1.4f), D.WpnDmg() * mult, bel ?? "physical");
    }

    /// <summary>Critical hit flourish (numbers in feel.json "crit"): body flash, gold sparks + streak, metallic layer, slow-mo and bigger kick on kills.</summary>
    void CritFx(Enemy e, Vector3 pt, Vector3 dir)
    {
        float Cf(string k, float def) { using var doc = JsonUtil.Load("res://data/feel.json"); return doc.RootElement.TryGetProperty("crit", out var c) ? JsonUtil.F(c, k, def) : def; }
        e.Flash(Cf("flash", .06f));
        Vfx.Sparks(pt, Vfx.Ramp((0, new Color(1, .98f, .85f)), (.4f, new Color(1, .8f, .3f)), (1, new Color(1, .6f, .1f, 0))), (int)Cf("sparks", 14), Cf("sparkSpeed", 11f), .3f, .12f, -5f, true, 35f, dir, .05f, "spark");
        var mat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BlendMode = BaseMaterial3D.BlendModeEnum.Add, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, AlbedoColor = new Color(1, .92f, .6f, 1), CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        var streak = new MeshInstance3D { Mesh = new QuadMesh { Size = new Vector2(Cf("streakLen", 1.2f), .06f), Orientation = PlaneMesh.OrientationEnum.Z }, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        GetParent().AddChild(streak); streak.GlobalPosition = pt;
        streak.LookAtFromPosition(pt, pt + dir, Vector3.Up); streak.RotateObjectLocal(Vector3.Up, Mathf.Pi / 2);   // quad's long axis (local X) along the swing
        var tw = streak.CreateTween(); tw.TweenProperty(mat, "albedo_color:a", 0f, Cf("streakTime", .12f)); tw.TweenCallback(Callable.From(streak.QueueFree));
        Sfx.Weapon(W.Id, "hit", 1f, Cf("hitPitch", 1.05f)); Sfx.Play("block", Cf("clashVol", .7f), Cf("clashPitch", 1.6f));
        if (e.Dead)
        {
            Feel.SlowMo(Cf("killSlowScale", .25f), Cf("killSlowTime", .12f));
            Rig?.Kick(dir, Cf("killKick", .22f), true);
        }
    }

    /// <summary>Hand poses for the current attack at progress p (0..1): rest -> wind-up -> mid-swing -> follow-through.</summary>
    HandPose AttackPose(AttackDef a, HandPose rest, float p)
    {
        if (a.Keys.Length < 3) return rest;
        static float S(float t) => t * t * (3 - 2 * t);
        if (p < a.Wind) return HandPose.Lerp(rest, a.Keys[0], S(p / Mathf.Max(.01f, a.Wind)));
        float q = (p - a.Wind) / Mathf.Max(.01f, 1 - a.Wind);
        float strikeEnd = Mathf.Clamp((a.Hit - a.Wind) / Mathf.Max(.01f, 1 - a.Wind) * 1.6f, .3f, .85f); // fast through the hit, then ease out
        if (q < strikeEnd) { float u = S(q / strikeEnd); return u < .5f ? HandPose.Lerp(a.Keys[0], a.Keys[1], u * 2) : HandPose.Lerp(a.Keys[1], a.Keys[2], u * 2 - 1); }
        float r = (q - strikeEnd) / (1 - strikeEnd); return HandPose.Lerp(a.Keys[2], rest, S(Mathf.Clamp(r * 1.3f - .3f, 0, 1)));
    }

    void FireWeapon()
    {
        var D = Game.D; var TS = Game.TS; string wt = D.WType();
        var origin = MuzzlePos(); var (target, _) = AimPoint(80f);
        var dir = (target - origin).Normalized();
        bool crit = GD.Randf() < D.CritChance() + (_atk?.CritBonus ?? 0) / 100f; float dmg = D.WpnDmg() * (float)GD.RandRange(0.9, 1.1) * (crit ? D.CritMult() : 1f) * RiposteMul();   // a riposte after a parry also works for bows and wands
        // the chain step (data/attacks.json): damage, shots in a fan, piercing, blast radius, statuses
        // magic shots take the weapon's element colour, else its type's trail colour; Muzzle() flashes at the casting hand
        Color tint = GearFx.Get(D.Weapon?.Elem)?.Col ?? Weapons.Get(wt).Trail;
        var st = _step; float sm = st != null && st.Id != "power" ? st.Def.Dmg : 1f; int shots = st?.Shots ?? 1; float spreadR = Mathf.DegToRad(st?.Spread ?? 0); bool sPierce = st?.Pierce ?? false;
        void Shoot(string kind, Vector3 d, float speed, float pdmg, float life, bool pierce, float aoe = 0, bool big = false)
        {
            var p = Projectile.Spawn(kind, 'p', origin, d * speed + (kind == "arrow" ? Vector3.Up * 0.6f : Vector3.Zero), pdmg, life, crit, pierce, aoe, big, kind == "bolt" && !big ? tint : null);
            if (st != null && st.Status.Count > 0) p.Statuses = st.Status;
            p.Wpn = wt;   // its hit / miss sounds come from the weapon type
        }
        void Muzzle(bool big)
        {
            var mc = big ? new Color(1f, .6f, .2f) : tint;
            Vfx.Sparks(origin, Vfx.Ramp((0, new Color(mc.Lerp(Colors.White, .75f), 1)), (.35f, new Color(mc, .85f)), (1, new Color(mc, 0))), big ? 14 : 9, big ? 3.5f : 2.6f, big ? .26f : .18f, big ? .38f : .24f, 0f, true, 35f, dir, .05f);
            Vfx.Sparks(origin, Vfx.Ramp((0, new Color(1, 1, 1, .9f)), (1, new Color(mc, 0))), 1, .01f, big ? .16f : .1f, big ? .9f : .55f, 0f, true, 0f, dir, 0f);   // the bright pop itself
            Vfx.Flash(origin, mc, big ? 2.2f : 1.4f, big ? 4.5f : 3f, .12f);   // unshadowed, gone in 0.12 s
        }
        Vector3 Fan(int k, int n) => n <= 1 ? dir : dir.Rotated(Vector3.Up, (k / (float)(n - 1) - .5f) * Mathf.Max(spreadR, .11f * (n - 1)));
        if (wt == "bow")
        {
            int n = Mathf.Max(shots, TS.Has("multi") ? 3 : 1); float sp = _atkPower ? 64f : 50f, pd = dmg * sm * (_atkPower ? 2f : 1f) * (TS.Has("multi") && shots <= 1 ? 0.55f : 1f);
            for (int k = 0; k < n; k++) Shoot("arrow", Fan(k, n), sp, pd, 2.4f, _atkPower || sPierce || TS.Has("pierce"));
            Sfx.Weapon(wt, "shoot");   // string twang + arrow whoosh
        }
        else if (wt == "wand")
        {
            float cost = (_atkPower ? 8f : 3f) * Mathf.Max(1, shots * .6f); bool ok = Mp >= cost; Mp = Mathf.Max(0, Mp - cost); if (!ok) dmg *= 0.5f;
            for (int k = 0; k < shots; k++) Shoot("bolt", Fan(k, shots), 44f, dmg * sm * (_atkPower ? 1.9f : 1f), 1.6f, sPierce || TS.Has("pierce"), st?.Aoe ?? 0);
            Muzzle(false); Sfx.Weapon(wt, "shoot");
        }
        else
        {
            float cost = (_atkPower ? 16f : 8f) * Mathf.Max(1, shots * .6f); bool ok = Mp >= cost; Mp = Mathf.Max(0, Mp - cost); if (!ok) dmg *= 0.5f;
            float aoe = st != null && st.Aoe > 0 ? st.Aoe : _atkPower ? 3.4f : 2.2f;
            for (int k = 0; k < shots; k++) Shoot("bolt", Fan(k, shots), 28f, dmg * sm * (_atkPower ? 2.6f : 1.35f), 2.2f, false, aoe, true);
            Muzzle(true); Sfx.Weapon(wt, "shoot");
        }
    }

    // ------------------------------------------------------------------ interaction + potions
    void ScanInteract()
    {
        Prompt = ""; IInteractable? best = null; float bd = 3.4f;
        foreach (var n in GetTree().GetNodesInGroup("interact"))
        {
            if (n is not Node3D n3 || n is not IInteractable it || !n3.IsVisibleInTree() || !it.CanInteract) continue;
            float d = n3.GlobalPosition.DistanceTo(GlobalPosition);
            if (d < bd) { bd = d; best = it; }
        }
        if (best != null)
        {
            Prompt = best is Gatherable ? "" : "[" + Inputs.KeyOf(Inputs.Use) + "] " + best.Prompt;   // gather nodes show their own world prompt (Gather.cs)
            if (Input.IsActionJustPressed(Inputs.Use)) best.Interact();
        }
    }

    void UsePotion(bool hp)
    {
        var D = Game.D;
        if (hp) { if (D.HpPot <= 0) { Ui.Note("No health potions", new Color(1, .5f, .5f)); return; } if (Hp >= D.MaxHp()) return; D.HpPot--; float a = D.MaxHp() * Tune.F("player.healthPotion", .5f) * HealIn; Hp = Mathf.Min(D.MaxHp(), Hp + a); FloatText.Spawn(this, GlobalPosition + new Vector3(0, 2.2f, 0), "+" + Mathf.RoundToInt(a), new Color(0.5f, 1f, 0.6f)); }
        else { if (D.MpPot <= 0) { Ui.Note("No mana potions", new Color(.5f, .6f, 1f)); return; } if (Mp >= D.MaxMp()) return; D.MpPot--; Mp = Mathf.Min(D.MaxMp(), Mp + D.MaxMp() * Tune.F("player.manaPotion", .6f)); }
        Sfx.Play("heal"); Game.Notify();
    }

    // ------------------------------------------------------------------ flight (Golden Wings, Wings.cs)
    public bool Flying;
    /// <summary>Flying: three times running speed where you steer, hold Jump to climb and Roll to dive; drains stamina like sprinting.</summary>
    void FlyMove(Vector3 dir, float moveMul, bool captured, float dt, ref Vector3 v)
    {
        float speed = RunSpeed * Tune.F("wings.speedMul", 3f) * moveMul * Statuses.SpeedMul;
        var target = dir * speed; float a = Accel * 1.4f * dt;
        v.X = Mathf.MoveToward(v.X, target.X, a); v.Z = Mathf.MoveToward(v.Z, target.Z, a);
        v.Y += _gravity * dt;   // the wings hold us up: undo this frame's gravity
        float vy = captured && Input.IsActionPressed(Inputs.Jump) ? Tune.F("wings.climb", 9f) : captured && Input.IsActionPressed(Inputs.Roll) ? -Tune.F("wings.dive", 14f) : 0f;
        v.Y = Mathf.MoveToward(v.Y, vy, 30f * dt);
        St = Mathf.Max(0, St - Feel.SprintDrain * Tune.F("wings.drainMul", 1f) * dt); _stLockT = Mathf.Max(_stLockT, Feel.SprintRegenDelay);
        if (dir.LengthSquared() > .01f) _modelYaw = Mathf.LerpAngle(_modelYaw, Util.YawTo(dir.X, dir.Z), 1f - Mathf.Exp(-8f * dt));
        if (St <= 0) { Flying = false; Ui.Note("Too tired to keep flying", new Color(1f, .7f, .5f)); }
    }

    // ------------------------------------------------------------------ main loop
    /// <summary>Held still (no gravity, no movement) while the loading screen covers a long teleport (LoadScreen.cs).</summary>
    public static bool Frozen;
    public override void _PhysicsProcess(double delta)
    {
        PetWorld.Tick(this, (float)delta);   // the active pet: knock-out timers, spawning it beside us (Pet.cs)
        if (Frozen) { Velocity = Vector3.Zero; _walkV = Vector3.Zero; return; }
        float dt = (float)delta; var D = Game.D; var TS = Game.TS;
        var v = Velocity; v.X = _walkV.X; v.Z = _walkV.Z;   // steer the walking velocity only; pushes are added for the move below
        if (!IsOnFloor()) v.Y -= _gravity * dt;
        BlockMove(dt, ref v);   // the block world: swimming, lava, ice (PlayerBlocks.cs)
        if (_liftT > 0)   // an updraft (Lands.Updraft): rise to its top, then hover there so the player can step off onto the sky island
        {
            _liftT -= dt;
            v.Y = GlobalPosition.Y < _liftTop ? Mathf.MoveToward(v.Y, 15f, 40f * dt) : Mathf.MoveToward(v.Y, (_liftTop + .5f - GlobalPosition.Y) * 2f, 30f * dt);
            _airT = 0f;
        }
        _rollCd = Mathf.Max(0, _rollCd - dt); _pdT = Mathf.Max(0, _pdT - dt); _inv = Mathf.Max(0, _inv - dt); _stagger = Mathf.Max(0, _stagger - dt);
        _comboT = Mathf.Max(0, _comboT - dt); _stLockT = Mathf.Max(0, _stLockT - dt);

        if (Dead)
        {
            _deadT += dt; v.X = v.Z = 0; Velocity = v; MoveAndSlide();
            if (_av != null) _av.Play("die", .15f);   // the model has a death clip; the procedural hero tips over
            else _model.Rotation = new Vector3(Mathf.Lerp(0, -Mathf.Pi / 2, Mathf.Clamp(_deadT / 0.5f, 0, 1)), _modelYaw, 0);
            if (Downed) DownedTick(dt);   // a party lair: lie here until raised (PlayerDowned.cs)
            else if (_deadT > 3f && !Arenas.Active) Respawner.PlayerDied();
            return;
        }
        // statuses on the hero: damage over time (resisted by type), stuns stagger, slows apply below
        Statuses.Tick(dt, (type, amount, col) =>
        {
            float d = type == "true" ? amount : amount * (1 - Mathf.Clamp(TS["res_" + type], -100, 75) / 100f);
            int n = Mathf.Max(1, Mathf.RoundToInt(d)); Hp -= n; FloatText.Spawn(this, GlobalPosition + new Vector3(0, 2.0f, 0), "-" + n, col, 0.7f);
            if (Hp <= 0 && !Dead && !DuelSave()) Die();
        });
        if (Dead) return;
        if (GrabTick(dt)) return;   // in Scarletta's grip: carried, thrown and impaled (PlayerDowned.cs)
        if (LairIntro.Active) { v.X = v.Z = 0; Velocity = v; MoveAndSlide(); Animate(dt); return; }   // the boss intro: stand and watch
        if (Statuses.Stunned) _stagger = Mathf.Max(_stagger, .1f);
        if (SkillTick(dt)) { Animate(dt); return; }   // leap or dash in progress

        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;
        var input = Input.GetVector(Inputs.Left, Inputs.Right, Inputs.Forward, Inputs.Back);
        float camYaw = Rig?.Yaw ?? 0f;
        var dir = new Vector3(input.X, 0, input.Y).Rotated(Vector3.Up, camYaw);

        // ---- hotkeys
        if (!(BuildMode && BlockWorld.Enabled)) SkillKeys(captured, dt);   // (build mode: keys 1-9 pick hotbar blocks instead, PlayerBlocks.cs) press casts, or hold to aim (ground spells) / charge (projectiles), release casts
        if (captured && _mRun >= 0 && (Input.IsActionJustPressed(Inputs.Roll) || _holdSlot >= 0)) StopMacro();   // dodging or using a hotbar skill stops a macro
        MacroKeys(captured, dt);   // Macro 1 / 2: chains of skills (PlayerMacros.cs)
        AutoPotions(dt);
        if (captured && Input.IsActionJustPressed(Inputs.PotHp)) UsePotion(true);
        if (captured && Input.IsActionJustPressed(Inputs.PotMp)) UsePotion(false);
        if (captured && Input.IsActionJustPressed(Inputs.Mount)) ToggleMount();
        if (captured && !BuildMode && Input.IsActionJustPressed(Inputs.ThrowCube)) Cubes.Throw(this);   // Soul Cubes (Cube.cs)
        if (captured && Input.IsActionJustPressed(Inputs.CubeNext)) Cubes.Cycle();
        if (captured && Input.IsActionJustPressed(Inputs.PetStay)) PetWorld.ToggleStay();
        if (captured && Input.IsActionJustPressed(Inputs.View))   // first / third person (CameraRig.FirstPerson), saved in the settings
        {
            Game.Set.FirstPerson = !Game.Set.FirstPerson; Game.SaveSettings();
            Ui.Note(Game.Set.FirstPerson ? (Mounted ? "First person (after you dismount)" : "First person") : "Third person", new Color(.8f, .85f, 1f));
        }
        if (captured && Input.IsActionJustPressed(Inputs.Realm)) World.TryShiftRealm();
        if (captured) ScanInteract(); else Prompt = "";
        if (captured && Input.IsActionJustPressed(Inputs.Lock)) { if (Inputs.UsingPad && Targeting.Current != null) Targeting.Clear(); else Targeting.Toggle(Targeting.Current != null); }   // pad: R3 toggles, a right-stick flick switches
        if (captured && Input.IsActionJustPressed(Inputs.Unlock)) Targeting.Clear();
        Targeting.Tick(this);
        // in combat: recently fought, locked on, or a monster nearby is hunting us (checked twice a second)
        _combatT -= dt; _combatScan -= dt; if (Targeting.Current != null) _combatT = Mathf.Max(_combatT, 1f);
        if (_combatScan <= 0)
        {
            _combatScan = .5f;
            foreach (var n in GetTree().GetNodesInGroup("enemies")) if (n is Enemy ce && ce.Hunting && !ce.Dead && ce.GlobalPosition.DistanceSquaredTo(GlobalPosition) < 18f * 18f) { _combatT = Mathf.Max(_combatT, 1.5f); break; }
        }
        // hero light: off in daylight, a warm pool of light at night, in the Shadow realm and underground
        float dark = Dungeon.Active ? 1f : Mathf.Clamp((World.Night - .25f) / .5f, 0, 1);
        _glow.LightEnergy = Mathf.Lerp(_glow.LightEnergy, dark * Feel.GlowEnergy, 1f - Mathf.Exp(-3f * dt)); _glow.OmniRange = Feel.GlowRange; _glow.Visible = _glow.LightEnergy > .02f;

        // ---- attack input: tap = light, hold = power (fires on release)
        // attack (left mouse): tap = next step of the chain, hold = keep swinging the chain.
        // heavy (right mouse): pressing starts a short gather; the heavy attack fires by itself when it ends (not on release)
        BlockInput(dt, captured);   // build mode: dig and place instead of attacking (PlayerBlocks.cs)
        bool building = BuildMode && BlockWorld.Enabled;
        bool lmb = captured && !building && Input.IsActionPressed(Inputs.Attack);
        // a press during a swing or its recovery waits for them (it used to give up after 0.3 s, so a press early in a slow swing was lost)
        if (building) { }
        else if (captured && Input.IsActionJustPressed(Inputs.Attack)) { float rhyLeft = _comboT - Tune.F("player.chainWindow", 1.3f); _pressPerfect = _comboT > 0 && rhyLeft <= _stepTotal * Feel.ComboF("perfectWindow", .25f) && rhyLeft > -.2f; if (!TryAttack(false)) { _hasQueue = true; _queuePower = false; _queueT = Mathf.Max(0.3f, Mathf.Min(_atkT, .8f) + Mathf.Max(_recT, _recPending) + .15f); } }
        else if (lmb && !_hasQueue && _atkT <= 0 && _heavyT <= 0) TryAttack(false);   // held: the chain on repeat
        // pressing heavy drops a buffered light press, so the light swing cannot start during the gather and take the heavy's turn
        if (captured && !building && Input.IsActionJustPressed(Inputs.Heavy) && _heavyT <= 0 && !Dead) { _heavyT = HeavyGather; if (!_queuePower) _hasQueue = false; FloatText.Spawn(this, GlobalPosition + new Vector3(0, 2.1f, 0), "Heavy", new Color(1f, .8f, .45f), .35f); }
        if (_heavyT > 0) { _heavyT -= dt; if (_heavyT <= 0 && !TryAttack(true)) { _hasQueue = true; _queuePower = true; _queueT = Mathf.Max(0.3f, Mathf.Min(_atkT, .8f) + Mathf.Max(_recT, _recPending) + .15f); } }
        if (_hasQueue && _heavyT > 0 && !_queuePower) _hasQueue = false;   // no light swing while a heavy gathers
        if (_hasQueue) { _queueT -= dt; if (_queueT <= 0) _hasQueue = false; else if (_atkT <= 0 && TryAttack(_queuePower)) _hasQueue = false; }

        // ---- block
        bool wantBlock = captured && HasShield && Input.IsActionPressed(Inputs.Guard) && _atkT <= 0 && _rollT <= 0 && _stagger <= 0 && _castT <= 0 && St > 0 && !Mounted;
        if (wantBlock && !_blocking) _blockT = 0;
        _blocking = wantBlock;
        if (_riposteT > 0) _riposteT -= dt;
        if (_blocking) { _blockT += dt; _modelYaw = camYaw; }

        // ---- attack + cast timelines
        // recovery: once a swing ends (or is cut short by moving), the next one waits; heavier swings and finishers wait longer
        if (_recT > 0) _recT -= dt;
        if (_atkT <= 0 && _recPending > 0) { _recT = _recDur = _recPending; _recPending = 0; _recClip = _step?.Rec ?? ""; }
        if (_atkT > 0)
        {
            _atkT -= dt; float prog = 1f - _atkT / _atkDur; var a = _atk ?? W.Combo[0];
            if (!_hitDone && prog >= a.Hit) { _hitDone = true; if (_atkSkill != null) { CastWentOff(); FireWeaponSkill(_atkSkill); } else if (Ranged) FireWeapon(); else ResolveMelee(a); }
            if (Ranged) _modelYaw = Targeting.Aim is Enemy at ? Util.YawTo(at.GlobalPosition.X - GlobalPosition.X, at.GlobalPosition.Z - GlobalPosition.Z) : camYaw;
            else
            {
                // lunge forward between wind-up and impact; spinning attacks turn the whole body
                if (a.Lunge > 0 && prog > a.Wind && prog < a.Hit) { var fw = new Vector3(-Mathf.Sin(_spinBase), 0, -Mathf.Cos(_spinBase)); float span = Mathf.Max(.05f, (a.Hit - a.Wind) * _atkDur); _kb += fw * (a.Lunge / span) * dt * 3.3f; }
                if (a.Spin != 0 && prog > a.Wind) { float q = Mathf.Clamp((prog - a.Wind) / (1 - a.Wind) * 1.4f, 0, 1); _modelYaw = _spinBase + Mathf.DegToRad(a.Spin) * (q * q * (3 - 2 * q)); }
            }
        }
        if (_castT > 0)
        {
            _castT -= dt; _modelYaw = camYaw;
            if (!_fired && _castT <= 0 && _castSkill != null) { _fired = true; var cs = _castSkill; _castSkill = null; CastWentOff(); Execute(cs); }
        }

        // ---- movement
        float moveMul = 1f + TS["move"] / 100f;
        if (_rollT > 0)
        {
            _rollT -= dt; _inv = Mathf.Max(_inv, 0.05f); v.X = _rollDir.X * RollSpeed; v.Z = _rollDir.Z * RollSpeed;
            _rollTrailT -= dt; if (_rollTrailT <= 0 && _rollT > 0) { _rollTrailT = RollTime * .35f; Fx.RollPuff(GlobalPosition, _rollPd); }   // smoke trail along the i-frame window
            if (_rollT <= 0) { Fx.Dust(GlobalPosition, .4f); Rig?.Shake(.025f); }   // roll end: small puff + tiny camera settle
        }
        else
        {
            // Golden Wings: Jump in the air takes off; landing, tiring out or taking the wings off ends the flight
            if (Flying && ((IsOnFloor() && !(captured && Input.IsActionPressed(Inputs.Jump))) || !Wings.Worn || Mounted || _stagger > 0)) Flying = false;
            else if (!Flying && captured && Input.IsActionJustPressed(Inputs.Jump) && !IsOnFloor() && _airT > .12f && Wings.Worn && !Mounted && St > 5f) { Flying = true; Sfx.Play("swing", .6f); }
            if (ChannelTick(dt, dir, captured)) { v.X = Mathf.MoveToward(v.X, 0, Accel * dt); v.Z = Mathf.MoveToward(v.Z, 0, Accel * dt); }   // channelling: feet planted
            else if (Flying) FlyMove(dir, moveMul, captured, dt, ref v);
            else {
            if (captured && Input.IsActionJustPressed(Inputs.Roll) && IsOnFloor() && _rollCd <= 0 && _atkT <= 0 && _stagger <= 0 && St >= Tune.F("stamina.roll", 14) * .85f && !Mounted)
            {
                _rollDir = dir.LengthSquared() > 0.01f ? dir.Normalized() : new Vector3(-Mathf.Sin(_modelYaw), 0, -Mathf.Cos(_modelYaw));
                _rollT = RollTime; _rollCd = 0.6f; _recT = 0; _recPending = 0; UseStamina(Tune.F("stamina.roll", 14));
                _rollPd = false; _rollFrom = GlobalPosition; _rollTrailT = RollTime * .3f;   // roll start: dust kick behind, cloth whoosh, first smoke puff
                Fx.Dust(GlobalPosition - _rollDir * .6f, .4f); Sfx.Play("swing", .8f); Fx.RollPuff(GlobalPosition, false);
                _modelYaw = Util.YawTo(_rollDir.X, _rollDir.Z);   // face the roll (a roll also cancels the recovery, above); this line was lost in a comment since 217a461
            }
            if (captured && Input.IsActionJustPressed(Inputs.Jump) && IsOnFloor() && _atkT <= 0 && _stagger <= 0) v.Y = JumpVelocity;

            if (!captured) Input.ActionRelease(Inputs.Sprint);   // a menu or another window took the Shift release: never keep sprinting after it
            bool sprint = Input.IsActionPressed(Inputs.Sprint) && St > 1f && dir.LengthSquared() > 0.01f && _atkT <= 0 && !_blocking && !Mounted;
            float speed = (sprint ? SprintSpeed : RunSpeed) * moveMul;   // matched to the clips' own stride (Avatar gait speeds)
            if (Mounted) speed = (_petMount != null ? PetWorld.MountSpeed : 10.5f) * moveMul;
            if (_atkT > 0 && !Ranged && _atk != null)
            {
                // melee: feet planted until the blow lands (lunges move the body themselves); after it, moving cancels the recovery
                float prog = 1f - _atkT / _atkDur;
                if (prog < _atk.Hit) speed = 0;
                else if (dir.LengthSquared() > .01f && _hitDone && _spinLeft <= 0) _atkT = 0;
            }
            else if (_atkT > 0 || _castT > 0) speed *= 0.3f;
            if (_blocking) speed *= 0.5f;
            if (_backpedal) speed *= 0.75f;   // backing away from a locked foe is slower
            speed *= Statuses.SpeedMul * _fluidSpeed;   // swimming and wading in lava are slower
            if (_spinLeft > 0) speed = Mathf.Max(speed, 3.5f);
            if (_stagger > 0) speed = 0;
            if (sprint) { St = Mathf.Max(0, St - Feel.SprintDrain * dt); _stLockT = Mathf.Max(_stLockT, Feel.SprintRegenDelay); }   // data/feel.json "move"
            {   // sprint feel: camera FOV/bob flag, start dust + whoosh, 0.15 s skid with a puff when stopping from full speed
                float hs = new Vector2(v.X, v.Z).Length(); bool fast = sprint && hs > SprintSpeed * .7f && IsOnFloor();
                if (Rig != null) Rig.Sprinting = fast;
                if (sprint && !_sprWas && IsOnFloor()) { Fx.Dust(GlobalPosition, .5f); Sfx.Play("swing"); }
                else if (!sprint && _sprWas && hs > SprintSpeed * .8f && IsOnFloor() && !Mounted) { _skidT = .15f; Fx.Dust(GlobalPosition, .4f); }
                _sprWas = sprint; _skidT = Mathf.Max(0, _skidT - dt);
            }
            var target = dir * speed; float a = (IsOnFloor() ? Accel : AirAccel) * dt * _accelMul * (_skidT > 0 ? .35f : 1f);   // ice: slow to start and to stop
            v.X = Mathf.MoveToward(v.X, target.X, a); v.Z = Mathf.MoveToward(v.Z, target.Z, a);
            if (_atkT > 0 && !Ranged && _atk != null) Magnet(1f - _atkT / _atkDur, dt, ref v);   // melee step-in overrides the planted feet
            }
        }
        // pushes (lunges, blows) ride on top of the walking velocity; take last frame's push off first, or it piles up every frame
        _kb = _kb.MoveToward(Vector3.Zero, 25f * dt);
        _hitKb = _hitKb.MoveToward(Vector3.Zero, 12f * dt);   // being struck: about 0.5 m for a normal blow
        _walkV = new Vector3(v.X, 0, v.Z);
        var push = _kb * .3f + _hitKb; v.X += push.X; v.Z += push.Z;

        // ---- regeneration
        if (_stLockT <= 0 && !_blocking && _atkT <= 0) St = Mathf.Min(D.MaxSt(), St + Feel.StRegen * dt);
        Hp = Mathf.Min(D.MaxHp(), Hp + (TS["regenHp"] + Tune.F("player.hpRegenBase", 0) + D.Vit * Tune.F("player.hpRegenPerVit", .02f)) * HealIn * dt);
        Mp = Mathf.Min(D.MaxMp(), Mp + (Tune.F("player.mpRegenBase", 1.5f) + TS["regenMp"] + D.Mag * Tune.F("player.mpRegenPerMag", .05f)) * dt);

        // ---- facing
        var flat = new Vector3(v.X, 0, v.Z);
        _backpedal = false;
        if (Targeting.Current is Enemy lk && IsInstanceValid(lk) && _atkT <= 0 && _rollT <= 0 && !Mounted)
        {
            // moving around a locked foe. The clips only run forward, so: sideways = turn into the step (keeping an eye on the foe),
            // backwards = keep facing the foe and play the run in reverse (backpedal)
            var td = lk.GlobalPosition - GlobalPosition; float ty = Util.YawTo(td.X, td.Z), want = ty;
            if (flat.LengthSquared() > .5f)
            {
                float my = Util.YawTo(flat.X, flat.Z), diff = Mathf.Abs(Util.AngDiff(ty, my));
                if (diff >= 2.1f) _backpedal = true;
                else if (diff > 1.0f) want = Mathf.LerpAngle(my, ty, .3f);
            }
            _modelYaw = Mathf.LerpAngle(_modelYaw, want, 1f - Mathf.Exp(-12f * dt));
        }
        else if (_atkT <= 0 && !_blocking && _rollT <= 0 && _castT <= 0 && flat.LengthSquared() > 0.5f)
            _modelYaw = Mathf.LerpAngle(_modelYaw, Util.YawTo(flat.X, flat.Z), 1f - Mathf.Exp(-14f * dt));
        // first person: the body always faces the view (mouse look turns the hero directly), so the head stays behind the camera
        if (Rig != null && Rig.FirstPerson) { _modelYaw = camYaw; _spinBase = camYaw; }

        Velocity = v; MoveAndSlide();
        Animate(dt);
        Game.PlayTime += dt;
    }

    // ------------------------------------------------------------------ animation (procedural, on the HeroRig joints)
    float _peakY;
    void Animate(float dt)
    {
        Feel.Load(); var w = W;
        // ---- locomotion: stride phase advances with ground speed, so feet roughly match the distance covered
        float hv = new Vector2(Velocity.X, Velocity.Z).Length();
        // grounded for animation: a moment in the air over a bump is not a fall (coyote time); a jump is
        _airT = IsOnFloor() ? 0 : _airT + dt; bool floor = IsOnFloor() || (_airT < .2f && Velocity.Y < 1f);
        if (_feet != null) _feet.Enabled = floor && !Mounted;
        if (!floor) _peakY = Mathf.Max(_peakY, GlobalPosition.Y);
        if (floor && !_wasFloor)
        {
            _landT = .22f; float fall = _peakY - GlobalPosition.Y;
            if (fall > 1.2f && !Mounted && _liftT <= 0)
            {
                float k = Mathf.Clamp((fall - 1.2f) / 6f, 0, 1);
                Fx.Dust(GlobalPosition, .5f + k * 1.1f); Rig?.Shake(.03f + k * .1f);
                Sfx.Play("land_thud", .6f + k * .6f, 1f - k * .2f); _landT = .3f;
            }
        }
        if (floor) _peakY = GlobalPosition.Y;
        _wasFloor = floor; _landT = Mathf.Max(0, _landT - dt);
        float moveAmt = floor && !Mounted && _rollT <= 0 ? Mathf.Clamp(hv / SprintSpeed, 0, 1) : 0f;
        _walkAmt = Mathf.Lerp(_walkAmt, moveAmt, 1f - Mathf.Exp(-10f * dt));
        _walkPh = Mathf.Wrap(_walkPh + hv * Feel.Stride * dt, 0, Mathf.Tau); _idleT += dt;
        float s = Mathf.Sin(_walkPh), amt = _walkAmt;
        bool atk = _atkT > 0 && _atk != null; float prog = atk ? 1f - _atkT / _atkDur : 0f;
        // crouch: wind-ups load the legs, landings absorb the fall
        float crouch = _landT / .22f * .8f + (atk && !w.Ranged ? Mathf.Sin(Mathf.Clamp(prog / Mathf.Max(.05f, _atk!.Hit), 0, 1) * Mathf.Pi) * (_atkPower ? .55f : .3f) : 0f) + (_blocking ? .25f : 0f);
        var legs = new HeroRig.LegPose
        {
            SwingR = s * Feel.LegSwing * amt + crouch * .35f, SwingL = -s * Feel.LegSwing * amt - crouch * .1f,
            KneeR = Mathf.Max(0, Mathf.Sin(_walkPh + 1.3f)) * 1.1f * amt + .06f + crouch * .7f, KneeL = Mathf.Max(0, Mathf.Sin(_walkPh + 1.3f + Mathf.Pi)) * 1.1f * amt + .06f + crouch * .45f,
        };
        if (Mounted) { legs.SwingR = legs.SwingL = 1.2f; legs.KneeR = legs.KneeL = 1.4f; legs.Splay = .35f; }
        else if (!floor && _rollT <= 0) { legs.SwingR = .7f; legs.KneeR = 1.3f; legs.SwingL = -.15f; legs.KneeL = .5f; }
        else if (_rollT > 0) { legs.SwingR = legs.SwingL = 1.4f; legs.KneeR = legs.KneeL = 2f; }
        float bob = Mathf.Abs(Mathf.Cos(_walkPh)) * Feel.Bob * amt - Feel.Bob * 0.5f * amt;     // two bounces per stride
        float breath = Mathf.Sin(_idleT * 2.2f) * Feel.Breath * (1f - amt);                      // idle breathing
        float lean = Feel.Lean * amt * amt;                                                       // forward lean grows with speed
        float roll = _rollT > 0 ? (1f - _rollT / RollTime) * Mathf.Tau : 0f;
        if (_av != null) { roll = 0; bob = breath = crouch = 0; }   // the model's clips do the roll, bob and crouch
        _model.Rotation = new Vector3(roll, _modelYaw, 0);
        _model.Position = new Vector3(0, (Mounted ? _mountY : 0f) + bob + breath - crouch * .12f, 0);
        _horse.Rotation = new Vector3(0, _modelYaw, 0);
        if (_petBody != null && _petMount != null && GodotObject.IsInstanceValid(_petBody)) _petBody.PetVel = new Vector3(Velocity.X, 0, Velocity.Z) / Mathf.Max(.3f, _petMount!.Scale.X);   // the ridden pet's gait follows our speed

        // ---- hands
        var restR = w.Rest; var lift = new Vector3(0, breath * .6f, 0);
        bool free = !atk && _castT <= 0 && !_blocking && !Mounted;
        float armSwing = free ? s * Feel.ArmSwing * amt * .3f : 0f;
        restR.H += new Vector3(0, 0, armSwing) + lift;
        var r = restR; float twist = -s * .12f * amt, spineLean = lean * .6f;
        var restL = new HandPose { H = new Vector3(-.42f, .97f, -.12f) + new Vector3(0, 0, -armSwing) + lift, Yaw = 0, Pitch = -60 };
        HandPose l = restL;
        if (atk && !w.Ranged)
        {
            r = AttackPose(_atk!, restR, prog);
            twist = Mathf.Clamp(Mathf.DegToRad(r.Yaw) * .4f, -.7f, .7f);                     // the chest turns into the swing
            spineLean = lean * .6f + Mathf.Clamp(-(r.H.Y - 1.3f) * .5f, -.15f, .35f);          // and folds over low strikes
            l = new HandPose { H = new Vector3(-.38f, 1.05f, -.25f) - new Vector3(twist * .3f, 0, 0), Yaw = 0, Pitch = -40 };
        }
        else if (atk && w.Ranged)
        {
            float a = Mathf.Clamp(prog / Mathf.Max(.05f, _atk!.Hit), 0, 1); float e = a * a * (3 - 2 * a);
            if (w.LeftHand) // bow: left arm extends the bow, right hand draws to the cheek, then releases
            {
                l = HandPose.Lerp(new HandPose { H = new Vector3(-.4f, 1.05f, -.2f), Yaw = 0, Pitch = 40 }, w.Aim, Mathf.Min(1, e * 1.6f));
                r = prog < _atk!.Hit ? HandPose.Lerp(w.Aim, w.Draw, e) : HandPose.Lerp(w.Draw, w.Aim, Mathf.Clamp((prog - _atk.Hit) * 6, 0, 1));
                r.H.X = Mathf.Max(r.H.X, .05f); twist = -.25f;
            }
            else { r = HandPose.Lerp(restR, w.Aim, prog < _atk!.Hit ? e : 1f - Mathf.Clamp((prog - _atk.Hit) * 2.5f, 0, 1)); twist = .15f; }
        }
        else if (_castT > 0)
        {
            float p = 1f - _castT / Mathf.Max(0.05f, _castDur), e = p * p * (3 - 2 * p);
            r = HandPose.Lerp(restR, w.Ranged ? w.Aim : new HandPose { H = new Vector3(.3f, 1.55f, -.45f), Yaw = 0, Pitch = 60 }, e);
            l = HandPose.Lerp(restL, new HandPose { H = new Vector3(-.2f, 1.45f, -.52f), Yaw = -20, Pitch = 10 }, e);
            spineLean = -.1f * e;
        }
        if (_blocking) l = new HandPose { H = new Vector3(-.12f, 1.3f, -.46f), Yaw = -90, Pitch = 0 };
        // two-handed weapons: the off hand holds the grip (behind the main hand, or ahead of it on a spear or staff shaft)
        if (w.Hands > 1 && !w.LeftHand) l = new HandPose { H = r.H - r.Dir * w.Grip, Yaw = r.Yaw, Pitch = r.Pitch };
        if (w.LeftHand && !atk) { l = new HandPose { H = new Vector3(-.4f, 1.0f, -.18f) + new Vector3(0, 0, -armSwing), Yaw = 0, Pitch = -65 }; r = new HandPose { H = r.H, Yaw = -6, Pitch = -60 }; }
        _rig.SteadyLeft = HasShield || w.LeftHand;
        Vector3? look = Targeting.Current != null && IsInstanceValid(Targeting.Current) ? _rig.ToLocal(Targeting.Current.GlobalPosition + Vector3.Up * 1.2f) : null;
        _rig.Pose(dt, legs, twist, spineLean, r, l, look);
        _nLegs = legs; _nTwist = twist; _nLean = spineLean; _nR = r; _nL = l;   // sent to friends in multiplayer

        // cape streams back with speed and flutters
        _capeX = Mathf.Lerp(_capeX, -(.05f + amt * .75f + Mathf.Sin(_idleT * 7f) * .05f * amt + (_rollT > 0 ? .4f : 0) + (!floor ? .5f : 0)), 1f - Mathf.Exp(-6f * dt));
        _rig.Cape.Rotation = new Vector3(_capeX + spineLean, 0, 0);
        if (_av?.Cape != null) _av.Cape.Rotation = new Vector3(-_capeX * .8f, 0, 0);   // the model's back is +Z of the old rig's
        // swing trail during the cutting part of a melee attack: weapon colour, gold for power attacks, hot orange for finishers
        if (atk && !w.Ranged && w.Id != "unarmed")   // fists leave no swing trail
        {
            _trail.Emit = prog > _atk!.Wind * .9f && prog < Mathf.Min(.95f, _atk.Hit + .3f);
            int ts = _atkPower ? 0 : Mathf.Max(0, _combo);
            _trail.Col = (_atkPower ? new Color(1f, .8f, .35f) : _atk.Finisher ? w.Trail.Lerp(new Color(1f, .82f, .4f), Feel.ComboF("finisherGold", .7f)) : w.Trail.Lerp(Colors.White, ts * Feel.ComboF("trailBrightStep", .1f)));
            _trail.Life = .22f * (1f + ts * Feel.ComboF("trailLifeStep", .12f));
        }
        else _trail.Emit = false;
        AnimateAvatar(hv, floor, atk);
    }

    /// <summary>Picks the model's clip from the hero's state (data/avatar.json "clips"). Attacks and casts are stretched to their real duration.</summary>
    void AnimateAvatar(float hv, bool floor, bool atk)
    {
        var a = _av; if (a == null) return; var w = W;
        a.FlapWings(Game.PlayTime, Flying);   // Golden Wings beat in flight, fold at rest
        if (Channeling) { a.Play("channel", .2f); return; }
        bool rolling = _rollT > 0; bool rollStart = rolling && !_avRolling; _avRolling = rolling;
        if (Mounted) { a.Play("sit"); return; }
        if (rolling) { a.Play("roll", .06f, RollTime, rollStart); return; }
        if (_stagger > 0) { bool fresh = _hurtSerial != _avHurt; _avHurt = _hurtSerial; a.Play("hit", .04f, 0, fresh); return; }   // every blow restarts the flinch
        if (BuildSwing(a)) return;   // build mode: the pickaxe swing (PlayerBlocks.cs)
        if (atk)
        {
            bool fresh = _atkSerial != _avAtk; _avAtk = _atkSerial;
            var wa = Avatar.WeaponAnim(w.Id);   // data/avatar.json "weaponAnims": how this weapon type's attack is animated
            if (wa is { } bw && Avatar.Str(bw, "pose") == "bow" && _atk != null)
            {
                // bow (procedural, scripts/AvatarBow.cs): raise, draw to the cheek through the wind-up, hold, release at the shot, then lower
                float t = (1f - _atkT / Mathf.Max(.01f, _atkDur)) * _atkDur, tHit = _atk.Hit * _atkDur;
                float ds = Mathf.Min(JsonUtil.F(bw, "raise", .2f) * .6f, tHit * .4f), de = Mathf.Max(ds + .02f, tHit * JsonUtil.F(bw, "drawEnd", .88f));
                float u = Mathf.Clamp((t - ds) / (de - ds), 0, 1); u = u * u * (3 - 2 * u);
                bool released = t >= tHit;
                if (!released || t < tHit + JsonUtil.F(bw, "hold", .12f))
                    a.BowPose(released ? 1f : u, released ? 1f : 0f, BowAim(), released ? "release" : t < ds ? "raise" : u < 1f ? "draw" : "hold");
                a.Move(hv, floor, false, false, _backpedal);   // the legs keep their own clip under the arms
                return;
            }
            // the step's own animation (data/attacks.json "anim"), timed so its blow lands when the damage does; else the old clips
            if (_step != null && _step.Anim != "" && _atk != null && a.PlayAttack(_step.Anim, _atk.Hit * _atkDur, fresh)) return;
            string shootKey = wa is { } sw && Avatar.Str(sw, "shoot") != "" ? Avatar.Str(sw, "shoot") : w.LeftHand ? "shoot" : "cast";
            string key = w.Ranged ? shootKey : _atkPower ? (w.Hands > 1 ? "powerHeavy" : "power") : w.Hands > 1 ? "heavy" : "melee";
            a.Play(key, .06f, _atkDur, fresh, _step != null ? _step.AnimStep : Mathf.Max(0, _combo - 1)); return;
        }
        // the follow-through of a heavy swing or finisher, while standing (moving hands the legs back at once)
        if (_recT > 0 && _recDur > .2f && _recClip != "" && hv < .8f && floor) { a.PlayRecovery(_recClip, _recT + .15f); return; }
        if (_castT > 0) { bool fresh = _castSerial != _avCast; _avCast = _castSerial; a.Play("cast", .08f, _castDur, fresh); return; }
        if (_blocking) { a.Play("block", .1f); return; }
        // let a finished swing play out only while standing still; once the hero moves, the legs take over at once (no sliding)
        if (hv < .8f && a.Busy && floor && (a.Cur.Contains("Sword") || a.Cur.Contains("Spell") || a.Cur.Contains("Punch") || a.Cur.Contains("Melee") || a.Cur.StartsWith("atk_"))) return;
        // standing still: a relaxed idle out of combat; the guarded weapon / shield stance only while fighting
        a.Move(hv, floor, Game.D.Weapon != null && InCombat, HasShield && InCombat, _backpedal);
    }
}
