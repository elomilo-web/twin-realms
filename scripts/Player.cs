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
    AttackStep? _step;   // the basic-attack chain step being swung (data/attacks.json)
    public AttackStep? CurrentStep => _step; public int ComboIndex => _combo;
    float _undyReady; Item? _shownWeapon; string _shownType = "";
    float _regenAcc;

    public override void _Ready()
    {
        Instance = this;
        AddToGroup("player");
        CollisionMask = 1 | 2;
        _gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity");
        Hp = Game.D.MaxHp(); Mp = Game.D.MaxMp(); St = Game.D.MaxSt();
        AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 0.4f, Height = 1.8f }, Position = new Vector3(0, 0.9f, 0) });
        BuildModel();
        Game.Changed += OnGearChanged;
        RefreshWeapon();
    }

    public override void _ExitTree() { Game.Changed -= OnGearChanged; if (Instance == this) Instance = null; }
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
            _rig.AddChild(_av); _av.Build(ap, Game.D.Name); HideRig();   // the cape is an equipment slot now (Avatar.Dress)
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
        foreach (var s in PlayerStats.Slots) { var it = d.Eq.GetValueOrDefault(s); sig += "|" + (it == null ? "-" : it.Name + it.Rar + it.Up); }
        if (sig == _gearSig) return; _gearSig = sig;
        // the weapon class's basic attack goes on the attack button automatically (data/attacks.json)
        string wt = d.WType();
        if (wt != _atkClass && Attacks.HasStyle(wt))
        {
            bool first = _atkClass != ""; _atkClass = wt; var b = Attacks.Build(wt); _combo = 0; _comboT = 0;
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
    public string AvClip => _av?.Cur ?? ""; public float ModelYaw => _modelYaw; public bool Backpedal => _backpedal;
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
    public void TakeHit(HitInfo h)
    {
        if (Dead || _inv > 0 || _rollT > 0 || World.Shifting || _moveT > 0) return;
        var D = Game.D; var TS = Game.TS;
        // evade: a clean dodge (Halfling, Ranger passives)
        if (GD.Randf() < Mathf.Clamp(TS["evade"], 0, 60) / 100f) { FloatText.Spawn(this, GlobalPosition + new Vector3(0, 2.2f, 0), "Evade", new Color(.75f, .95f, 1f), .9f); _inv = .15f; return; }
        // physical damage against defense; other types against magic resist (half defense + mr), then the per-type resistance (capped at 75%)
        float baseDmg = h.Type == "physical" || h.Type == "" ? h.Damage * 100f / (100f + D.Defense() * 5f)
            : h.Type == "true" ? h.Damage : h.Damage * 100f / (100f + (D.Defense() * .5f + TS["mr"]) * 5f) * (1 - Mathf.Clamp(TS["res_" + h.Type], -100, 75) / 100f);
        baseDmg *= Statuses.TakenMul;
        if (h.Attacker is Enemy src && IsInstanceValid(src)) baseDmg *= src.Status.DealtMul;
        var toSrc = h.From - GlobalPosition; toSrc.Y = 0;
        float srcYaw = Util.YawTo(toSrc.X, toSrc.Z);
        bool front = toSrc.LengthSquared() < 0.01f || Mathf.Abs(Util.AngDiff(_modelYaw, srcYaw)) < 1.25f;
        float dmg = baseDmg; var away = -toSrc; away.Y = 0;
        if (_blocking && front && !h.NoBlock)
        {
            if (_blockT < 0.22f && h.Attacker is Enemy en && !h.IsProjectile)
            {
                en.Stun(0.9f); _inv = 0.3f; Sfx.Play("block");
                FloatText.Spawn(this, GlobalPosition + new Vector3(0, 2.3f, 0), "PARRY", new Color(1, 0.88f, 0.5f), 1.3f);
                Fx.Burst(GlobalPosition + new Vector3(0, 1.3f, 0) + new Vector3(-Mathf.Sin(_modelYaw), 0, -Mathf.Cos(_modelYaw)) * 0.7f, new Color(1, .95f, .6f), 14, 5f, 0.35f, 0.07f);
                return;
            }
            float red = Mathf.Clamp(0.55f + D.ShieldDef * 0.012f + TS["block"] / 100f, 0.5f, 0.92f);
            dmg = baseDmg * (1f - red); UseStamina(h.Damage * 0.3f); Sfx.Play("block");
            if (St <= 0) { _blocking = false; _stagger = 0.7f; FloatText.Spawn(this, GlobalPosition + new Vector3(0, 2.3f, 0), "Guard broken", new Color(1, 0.6f, 0.45f)); }
        }
        else
        {
            Sfx.Play("hurt");
            if (away.LengthSquared() > 0.001f) _kb += away.Normalized() * h.Knock;
            if (!h.IsProjectile) _stagger = Mathf.Max(_stagger, 0.18f);
            _atkT = 0; _castT = 0; _castSkill = null; _atkSkill = null;
        }
        if (TS.Has("manaShield") && Mp > 0) { float m = Mathf.Min(Mp, dmg * 0.35f); Mp -= m; dmg -= m; }
        if (TS["thorns"] > 0 && h.Attacker is Enemy te && !h.IsProjectile && te.GlobalPosition.DistanceTo(GlobalPosition) < 6f)
            te.TakeHit(new HitInfo { Damage = h.Damage * TS["thorns"] / 100f, From = GlobalPosition, IsPlayer = true, Attacker = this });
        int final = Mathf.Max(1, Mathf.RoundToInt(dmg));
        Hp -= final; _inv = 0.2f;
        // the attacker's on-hit statuses (tenacity shortens them), then "when hit" triggers
        if (h.Statuses != null) foreach (var (id, chance, durMul, pow) in h.Statuses) if (GD.Randf() < chance) Statuses.Add(id, durMul, pow, h.Damage, TS["tenacity"]);
        TriggerAuto("hit");
        Rig?.Shake(0.18f); Feel.HitStop(Feel.Taken);
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
        Dead = true; Hp = 0; _deadT = 0; _atkT = 0; _blocking = false; if (Mounted) ToggleMount(true);
        Sfx.Play("die");
        if (Arenas.Active) Arenas.OnPlayerDeath();
    }

    public void Revive(Vector3 pos)
    {
        Dead = false; Hp = Game.D.MaxHp(); Mp = Game.D.MaxMp(); St = Game.D.MaxSt(); _model.Rotation = new Vector3(0, _modelYaw, 0);
        GlobalPosition = pos; Velocity = Vector3.Zero; _inv = 2f;
    }

    public void Teleport(Vector3 pos, float yaw = 0f)
    {
        GlobalPosition = pos; Velocity = Vector3.Zero; _kb = Vector3.Zero;
        if (Rig != null) { Rig.GlobalPosition = pos + new Vector3(0, 1.6f, 0); Rig.Rotation = new Vector3(0, yaw, 0); }
        _modelYaw = yaw;
    }

    // ------------------------------------------------------------------ mount
    public void ToggleMount(bool forceOff = false)
    {
        if (forceOff || Mounted) { Mounted = false; _horse.Visible = false; return; }
        if (!Game.D.Horse) { Ui.Note("You do not own a horse. Brynn sells one.", new Color(0.7f, 0.8f, 1)); return; }
        if (Dungeon.Active || Arenas.Active) { Ui.Note("No riding here", new Color(0.7f, 0.8f, 1)); return; }
        Mounted = true; _horse.Visible = true; Ui.Note("You mount your horse", new Color(0.8f, 0.9f, 1));
    }

    // ------------------------------------------------------------------ aiming
    public (Vector3 point, Enemy? enemy) AimPoint(float maxDist)
    {
        if (Targeting.Current is Enemy lt && IsInstanceValid(lt) && !lt.Dead) return (lt.GlobalPosition + new Vector3(0, lt.HeightM() * .55f, 0), lt);
        if (Rig == null) return (GlobalPosition + new Vector3(0, 1.4f, 0) + new Vector3(-Mathf.Sin(_modelYaw), 0, -Mathf.Cos(_modelYaw)) * maxDist, null);
        var cam = Rig.Camera; var from = cam.GlobalPosition; var dir = -cam.GlobalTransform.Basis.Z;
        var q = PhysicsRayQueryParameters3D.Create(from, from + dir * maxDist, 3);
        q.Exclude = new Godot.Collections.Array<Rid> { GetRid() };
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(q);
        if (hit.Count > 0) return ((Vector3)hit["position"], hit["collider"].As<GodotObject>() as Enemy);
        return (from + dir * maxDist, null);
    }

    Vector3 MuzzlePos() => GlobalPosition + new Vector3(0, 1.45f + (Mounted ? 0.9f : 0f), 0) + new Vector3(-Mathf.Sin(_modelYaw), 0, -Mathf.Cos(_modelYaw)) * 0.6f;

    // ------------------------------------------------------------------ attacks
    bool TryAttack(bool power)
    {
        if (Dead || _atkT > 0 || _rollT > 0 || _stagger > 0 || _castT > 0) return false;
        if (Mounted) { Ui.Note("Dismount to fight (T)", new Color(0.7f, 0.8f, 1)); return false; }
        var w = W; bool ranged = w.Ranged;
        float cost = ranged ? (power ? 16f : 7f) : (power ? 26f : 9f) * (w.Hands > 1 ? 1.25f : w.Id == "dagger" ? .6f : 1f);
        if (St < 4f) { FloatText.Spawn(this, GlobalPosition + new Vector3(0, 2.2f, 0), "Out of stamina", new Color(0.6f, 0.8f, 1f), 0.8f); return false; }
        // the basic attack of the equipped weapon class (data/attacks.json): light attacks walk the player's chain, power attacks reset it
        int chainLen = Attacks.HasStyle(w.Id) ? Attacks.Build(w.Id).Chain.Count : w.Combo.Count;
        if (!power) _combo = _comboT > 0 ? (_combo + 1) % chainLen : 0; else _combo = 0;
        if (Attacks.HasStyle(w.Id)) { _step = Attacks.Resolve(w, _combo, power); _atk = _step.Def; }
        else { _step = null; _atk = power ? w.Power : w.Combo[Mathf.Min(_combo, w.Combo.Count - 1)]; }
        UseStamina(cost * (_step?.Stamina ?? 1f));
        _atkPower = power; _atkDur = _atk.Dur / (Game.D.AtkSpeed() * w.Speed * (_step?.Speed ?? 1f)); _atkT = _atkDur; _hitDone = false; _comboT = _atkDur + 0.55f; _atkSerial++;
        float camYaw = Rig?.Yaw ?? _modelYaw; float face = camYaw;
        var tgt = Targeting.Current;
        if (tgt != null) { var d = tgt.GlobalPosition - GlobalPosition; face = Util.YawTo(d.X, d.Z); }
        else if (!ranged)
        {
            float best = 5.5f;
            foreach (var n in GetTree().GetNodesInGroup("enemies"))
            {
                if (n is not Enemy e || e.Dead || !e.Active) continue;
                var d = e.GlobalPosition - GlobalPosition; d.Y = 0; float dd = d.Length() - e.Def.R;
                if (dd < best && Mathf.Abs(Util.AngDiff(camYaw, Util.YawTo(d.X, d.Z))) < 0.9f) { best = dd; face = Util.YawTo(d.X, d.Z); }
            }
        }
        _modelYaw = face; _spinBase = face; Sfx.Play("swing");
        return true;
    }

    /// <summary>Melee hit for the current attack: arc, reach, knockback, stun and knock-up come from the attack data, then passives modify them.</summary>
    void ResolveMelee(AttackDef a)
    {
        var D = Game.D; var TS = Game.TS; var w = W;
        bool fin = a.Finisher, cleave = TS.Has("cleave") && (_atkPower || fin);
        float range = a.Range + (cleave ? .8f : 0f) + TS["reach"] / 100f * a.Range, arc = cleave ? Mathf.Pi : Mathf.DegToRad(Mathf.Min(180, a.Arc));
        float mult = a.Dmg * (fin ? 1f + TS["finisherP"] / 100f : 1f) * (_atkPower ? 1f + TS["powerP"] / 100f : 1f);
        float stun = a.Stun + (fin ? TS["finisherStun"] : 0f), up = a.Up + (fin ? TS["finisherUp"] : 0f), knock = a.Knock * (1f + TS["knockP"] / 100f);
        int hits = 0;
        foreach (var n in GetTree().GetNodesInGroup("enemies"))
        {
            if (n is not Enemy e || e.Dead || !e.Active) continue;
            var d = e.GlobalPosition - GlobalPosition; d.Y = 0; float dist = d.Length() - e.Def.R * e.ScaleF();
            if (dist > range) continue;
            if (dist > 0.6f && arc < Mathf.Pi - .01f && Mathf.Abs(Util.AngDiff(_modelYaw, Util.YawTo(d.X, d.Z))) > arc + (e.Def.R > 1 ? 0.5f : 0f)) continue;
            if (Mathf.Abs(e.GlobalPosition.Y - GlobalPosition.Y) > 3.5f + e.Def.R) continue;
            bool crit = GD.Randf() < D.CritChance() + (w.Crit + a.CritBonus) / 100f;
            float dmg = D.WpnDmg() * (float)GD.RandRange(0.9, 1.1) * mult * (crit ? D.CritMult() : 1f);
            var hi = new HitInfo { Damage = dmg, From = GlobalPosition, Knock = knock, Stun = stun, Up = up, Crit = crit, Attacker = this, IsPlayer = true, Kind = _atkPower ? "power" : fin ? "combo" : "light" };
            foreach (var kv in w.OnHit) hi.With(kv.Key, kv.Value * (fin || _atkPower ? 1.5f : 1f));
            if (GearFx.Get(D.Weapon?.Elem) is { } el && el.Status != "") hi.With(el.Status, el.Proc * (fin || _atkPower ? 1.5f : 1f) * (_step?.ElemMul ?? 1f));   // elemental weapon
            if (_step != null) foreach (var (sid, ch) in _step.Status) hi.With(sid, ch);   // the chain step and its modifiers
            e.TakeHit(hi); if (crit) TriggerAuto("crit");
            hits++;
        }
        if (hits > 0) { Rig?.Shake(_atkPower ? 0.25f : fin ? 0.15f : 0.08f); Sfx.Play("hit"); }
        if ((_atkPower || fin) && (a.Up > 0 || a.Arc >= 180 || stun > .5f)) { Vfx.Ring(GlobalPosition + Vector3.Up * .08f, range * .9f, w.Trail, .35f); }
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
        bool crit = GD.Randf() < D.CritChance() + (_atk?.CritBonus ?? 0) / 100f; float dmg = D.WpnDmg() * (float)GD.RandRange(0.9, 1.1) * (crit ? D.CritMult() : 1f);
        // the chain step (data/attacks.json): damage, shots in a fan, piercing, blast radius, statuses
        var st = _step; float sm = st != null && st.Id != "power" ? st.Def.Dmg : 1f; int shots = st?.Shots ?? 1; float spreadR = Mathf.DegToRad(st?.Spread ?? 0); bool sPierce = st?.Pierce ?? false;
        void Shoot(string kind, Vector3 d, float speed, float pdmg, float life, bool pierce, float aoe = 0, bool big = false)
        {
            var p = Projectile.Spawn(kind, 'p', origin, d * speed + (kind == "arrow" ? Vector3.Up * 0.6f : Vector3.Zero), pdmg, life, crit, pierce, aoe, big);
            if (st != null && st.Status.Count > 0) p.Statuses = st.Status;
        }
        Vector3 Fan(int k, int n) => n <= 1 ? dir : dir.Rotated(Vector3.Up, (k / (float)(n - 1) - .5f) * Mathf.Max(spreadR, .11f * (n - 1)));
        if (wt == "bow")
        {
            int n = Mathf.Max(shots, TS.Has("multi") ? 3 : 1); float sp = _atkPower ? 64f : 50f, pd = dmg * sm * (_atkPower ? 2f : 1f) * (TS.Has("multi") && shots <= 1 ? 0.55f : 1f);
            for (int k = 0; k < n; k++) Shoot("arrow", Fan(k, n), sp, pd, 2.4f, _atkPower || sPierce || TS.Has("pierce"));
            Sfx.Play("swing");
        }
        else if (wt == "wand")
        {
            float cost = (_atkPower ? 8f : 3f) * Mathf.Max(1, shots * .6f); bool ok = Mp >= cost; Mp = Mathf.Max(0, Mp - cost); if (!ok) dmg *= 0.5f;
            for (int k = 0; k < shots; k++) Shoot("bolt", Fan(k, shots), 44f, dmg * sm * (_atkPower ? 1.9f : 1f), 1.6f, sPierce || TS.Has("pierce"), st?.Aoe ?? 0);
            Sfx.Play("zap");
        }
        else
        {
            float cost = (_atkPower ? 16f : 8f) * Mathf.Max(1, shots * .6f); bool ok = Mp >= cost; Mp = Mathf.Max(0, Mp - cost); if (!ok) dmg *= 0.5f;
            float aoe = st != null && st.Aoe > 0 ? st.Aoe : _atkPower ? 3.4f : 2.2f;
            for (int k = 0; k < shots; k++) Shoot("bolt", Fan(k, shots), 28f, dmg * sm * (_atkPower ? 2.6f : 1.35f), 2.2f, false, aoe, true);
            Sfx.Play("fire");
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
            Prompt = "[" + Inputs.KeyOf(Inputs.Use) + "] " + best.Prompt;
            if (Input.IsActionJustPressed(Inputs.Use)) best.Interact();
        }
    }

    void UsePotion(bool hp)
    {
        var D = Game.D;
        if (hp) { if (D.HpPot <= 0) { Ui.Note("No health potions", new Color(1, .5f, .5f)); return; } if (Hp >= D.MaxHp()) return; D.HpPot--; float a = D.MaxHp() * 0.5f; Hp = Mathf.Min(D.MaxHp(), Hp + a); FloatText.Spawn(this, GlobalPosition + new Vector3(0, 2.2f, 0), "+" + Mathf.RoundToInt(a), new Color(0.5f, 1f, 0.6f)); }
        else { if (D.MpPot <= 0) { Ui.Note("No mana potions", new Color(.5f, .6f, 1f)); return; } if (Mp >= D.MaxMp()) return; D.MpPot--; Mp = Mathf.Min(D.MaxMp(), Mp + D.MaxMp() * 0.6f); }
        Sfx.Play("heal"); Game.Notify();
    }

    // ------------------------------------------------------------------ main loop
    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta; var D = Game.D; var TS = Game.TS;
        var v = Velocity;
        if (!IsOnFloor()) v.Y -= _gravity * dt;
        _rollCd = Mathf.Max(0, _rollCd - dt); _inv = Mathf.Max(0, _inv - dt); _stagger = Mathf.Max(0, _stagger - dt);
        _comboT = Mathf.Max(0, _comboT - dt); _stLockT = Mathf.Max(0, _stLockT - dt);

        if (Dead)
        {
            _deadT += dt; v.X = v.Z = 0; Velocity = v; MoveAndSlide();
            if (_av != null) _av.Play("die", .15f);   // the model has a death clip; the procedural hero tips over
            else _model.Rotation = new Vector3(Mathf.Lerp(0, -Mathf.Pi / 2, Mathf.Clamp(_deadT / 0.5f, 0, 1)), _modelYaw, 0);
            if (_deadT > 3f && !Arenas.Active) Respawner.PlayerDied();
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
        if (Statuses.Stunned) _stagger = Mathf.Max(_stagger, .1f);
        if (SkillTick(dt)) { Animate(dt); return; }   // leap or dash in progress

        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;
        var input = Input.GetVector(Inputs.Left, Inputs.Right, Inputs.Forward, Inputs.Back);
        float camYaw = Rig?.Yaw ?? 0f;
        var dir = new Vector3(input.X, 0, input.Y).Rotated(Vector3.Up, camYaw);

        // ---- hotkeys
        for (int i = 0; i < 6; i++) if (captured && Input.IsActionJustPressed("spell" + (i + 1))) TryCast(i);
        if (captured && Input.IsActionJustPressed(Inputs.PotHp)) UsePotion(true);
        if (captured && Input.IsActionJustPressed(Inputs.PotMp)) UsePotion(false);
        if (captured && Input.IsActionJustPressed(Inputs.Mount)) ToggleMount();
        if (captured && Input.IsActionJustPressed(Inputs.Realm)) World.TryShiftRealm();
        if (captured) ScanInteract(); else Prompt = "";
        if (captured && Input.IsActionJustPressed(Inputs.Lock)) Targeting.Toggle(Targeting.Current != null);
        if (captured && Input.IsActionJustPressed(Inputs.Unlock)) Targeting.Clear();
        Targeting.Tick(this);
        // hero light: off in daylight, a warm pool of light at night, in the Shadow realm and underground
        float dark = Dungeon.Active ? 1f : Mathf.Clamp((World.Night - .25f) / .5f, 0, 1);
        _glow.LightEnergy = Mathf.Lerp(_glow.LightEnergy, dark * Feel.GlowEnergy, 1f - Mathf.Exp(-3f * dt)); _glow.OmniRange = Feel.GlowRange; _glow.Visible = _glow.LightEnergy > .02f;

        // ---- attack input: tap = light, hold = power (fires on release)
        bool lmb = captured && Input.IsActionPressed(Inputs.Attack);   // left mouse by default (Settings > Controls)
        if (lmb) _lmbHold = _lmbPrev ? _lmbHold + dt : 0f;
        if (!lmb && _lmbPrev)
        {
            bool power = _lmbHold >= 0.35f;
            if (!TryAttack(power)) { _hasQueue = true; _queuePower = power; _queueT = 0.3f; }
        }
        _lmbPrev = lmb;
        if (_hasQueue) { _queueT -= dt; if (_queueT <= 0) _hasQueue = false; else if (_atkT <= 0 && TryAttack(_queuePower)) _hasQueue = false; }

        // ---- block
        bool wantBlock = captured && HasShield && Input.IsActionPressed(Inputs.Guard) && _atkT <= 0 && _rollT <= 0 && _stagger <= 0 && _castT <= 0 && St > 0 && !Mounted;
        if (wantBlock && !_blocking) _blockT = 0;
        _blocking = wantBlock;
        if (_blocking) { _blockT += dt; _modelYaw = camYaw; }

        // ---- attack + cast timelines
        if (_atkT > 0)
        {
            _atkT -= dt; float prog = 1f - _atkT / _atkDur; var a = _atk ?? W.Combo[0];
            if (!_hitDone && prog >= a.Hit) { _hitDone = true; if (_atkSkill != null) FireWeaponSkill(_atkSkill); else if (Ranged) FireWeapon(); else ResolveMelee(a); }
            if (Ranged) _modelYaw = Targeting.Current != null ? Util.YawTo(Targeting.Current.GlobalPosition.X - GlobalPosition.X, Targeting.Current.GlobalPosition.Z - GlobalPosition.Z) : camYaw;
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
            if (!_fired && _castT <= 0 && _castSkill != null) { _fired = true; var cs = _castSkill; _castSkill = null; Execute(cs); }
        }

        // ---- movement
        float moveMul = 1f + TS["move"] / 100f;
        if (_rollT > 0)
        {
            _rollT -= dt; _inv = Mathf.Max(_inv, 0.05f); v.X = _rollDir.X * RollSpeed; v.Z = _rollDir.Z * RollSpeed;
        }
        else
        {
            if (captured && Input.IsActionJustPressed(Inputs.Roll) && IsOnFloor() && _rollCd <= 0 && _atkT <= 0 && _stagger <= 0 && St >= 12f && !Mounted)
            {
                _rollDir = dir.LengthSquared() > 0.01f ? dir.Normalized() : new Vector3(-Mathf.Sin(_modelYaw), 0, -Mathf.Cos(_modelYaw));
                _rollT = RollTime; _rollCd = 0.6f; UseStamina(14f); _modelYaw = Util.YawTo(_rollDir.X, _rollDir.Z);
            }
            if (captured && Input.IsActionJustPressed(Inputs.Jump) && IsOnFloor() && _atkT <= 0 && _stagger <= 0) v.Y = JumpVelocity;

            bool sprint = Input.IsActionPressed(Inputs.Sprint) && St > 1f && dir.LengthSquared() > 0.01f && _atkT <= 0 && !_blocking && !Mounted;
            float speed = (sprint ? 8.4f : 5.2f) * moveMul;
            if (Mounted) speed = 10.5f * moveMul;
            if (_atkT > 0 || _castT > 0) speed *= 0.3f;
            if (_blocking) speed *= 0.5f;
            if (_backpedal) speed *= 0.75f;   // backing away from a locked foe is slower
            speed *= Statuses.SpeedMul;
            if (_spinLeft > 0) speed = Mathf.Max(speed, 3.5f);
            if (_stagger > 0) speed = 0;
            if (sprint) { St = Mathf.Max(0, St - 10f * dt); _stLockT = 0.5f; }
            var target = dir * speed; float a = (IsOnFloor() ? Accel : AirAccel) * dt;
            v.X = Mathf.MoveToward(v.X, target.X, a); v.Z = Mathf.MoveToward(v.Z, target.Z, a);
        }
        _kb = _kb.MoveToward(Vector3.Zero, 25f * dt);
        v.X += _kb.X * 0.3f; v.Z += _kb.Z * 0.3f;

        // ---- regeneration
        if (_stLockT <= 0 && !_blocking && _atkT <= 0) St = Mathf.Min(D.MaxSt(), St + 24f * dt);
        Hp = Mathf.Min(D.MaxHp(), Hp + (TS["regenHp"] + D.Vit * 0.02f) * dt);
        Mp = Mathf.Min(D.MaxMp(), Mp + (1.5f + TS["regenMp"] + D.Mag * 0.05f) * dt);

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

        Velocity = v; MoveAndSlide();
        Animate(dt);
        Game.PlayTime += dt;
    }

    // ------------------------------------------------------------------ animation (procedural, on the HeroRig joints)
    void Animate(float dt)
    {
        Feel.Load(); var w = W;
        // ---- locomotion: stride phase advances with ground speed, so feet roughly match the distance covered
        float hv = new Vector2(Velocity.X, Velocity.Z).Length(); bool floor = IsOnFloor();
        if (floor && !_wasFloor) _landT = .22f; _wasFloor = floor; _landT = Mathf.Max(0, _landT - dt);
        float moveAmt = floor && !Mounted && _rollT <= 0 ? Mathf.Clamp(hv / 8.4f, 0, 1) : 0f;
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
        _model.Position = new Vector3(0, (Mounted ? 0.9f : 0f) + bob + breath - crouch * .12f, 0);
        _horse.Rotation = new Vector3(0, _modelYaw, 0);

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
        if (atk && !w.Ranged)
        {
            _trail.Emit = prog > _atk!.Wind * .9f && prog < Mathf.Min(.95f, _atk.Hit + .3f);
            _trail.Col = _atkPower ? new Color(1f, .8f, .35f) : _atk.Finisher ? w.Trail.Lerp(new Color(1f, .45f, .2f), .6f) : w.Trail;
        }
        else _trail.Emit = false;
        AnimateAvatar(hv, floor, atk);
    }

    /// <summary>Picks the model's clip from the hero's state (data/avatar.json "clips"). Attacks and casts are stretched to their real duration.</summary>
    void AnimateAvatar(float hv, bool floor, bool atk)
    {
        var a = _av; if (a == null) return; var w = W;
        bool rolling = _rollT > 0; bool rollStart = rolling && !_avRolling; _avRolling = rolling;
        if (Mounted) { a.Play("sit"); return; }
        if (rolling) { a.Play("roll", .06f, RollTime, rollStart); return; }
        if (_stagger > 0) { a.Play("hit", .05f); return; }
        if (atk)
        {
            bool fresh = _atkSerial != _avAtk; _avAtk = _atkSerial;
            string key = w.Ranged ? (w.LeftHand ? "shoot" : "cast") : _atkPower ? (w.Hands > 1 ? "powerHeavy" : "power") : w.Hands > 1 ? "heavy" : "melee";
            a.Play(key, .06f, _atkDur, fresh, _step != null ? _step.AnimStep : Mathf.Max(0, _combo - 1)); return;
        }
        if (_castT > 0) { bool fresh = _castSerial != _avCast; _avCast = _castSerial; a.Play("cast", .08f, _castDur, fresh); return; }
        if (_blocking) { a.Play("block", .1f); return; }
        if (a.Busy && floor && (a.Cur.Contains("Sword") || a.Cur.Contains("Spell") || a.Cur.Contains("Punch") || a.Cur.Contains("Melee"))) return;   // let a swing finish
        a.Move(hv, floor, Game.D.Weapon != null, HasShield, _backpedal);
    }
}
