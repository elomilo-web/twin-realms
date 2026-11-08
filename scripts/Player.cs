using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace TwinRealms;

public class SpellDef { public string Id = "", Name = ""; public float Mp, Cd, Cast; }

/// <summary>The hero: movement, roll, four weapon types (sword, bow, wand, staff), block/parry, four spells, potions, interaction, horse.</summary>
public partial class Player : CharacterBody3D, IDamageable
{
    public static Player? Instance;
    public static List<SpellDef> Spells = new();

    public CameraRig? Rig;
    public float Hp, Mp, St;
    public bool Dead { get; private set; }
    public bool Mounted { get; private set; }
    public string Prompt = "";
    public float[] SpellCd = new float[4];
    public float ShiftFlash;

    const float Accel = 40f, AirAccel = 10f;
    const float JumpVelocity = 6.0f, RollSpeed = 11f, RollTime = 0.45f;

    Node3D _model = null!, _hand = null!, _shield = null!, _horse = null!, _weaponRoot = null!;
    float _gravity, _modelYaw, _rollT, _rollCd, _stLockT, _inv, _stagger, _deadT;
    Vector3 _rollDir, _kb;
    bool _lmbPrev; float _lmbHold;
    float _atkT, _atkDur, _comboT, _blockT, _queueT, _castT, _castDur; bool _queuePower, _hasQueue;
    int _combo, _castIdx = -1; bool _atkPower, _hitDone, _blocking, _fired;
    float _undyReady; Item? _shownWeapon; string _shownType = "";
    float _regenAcc;

    static void LoadSpells()
    {
        if (Spells.Count > 0) return;
        using var doc = JsonUtil.Load("res://data/classes.json");
        foreach (var s in doc.RootElement.GetProperty("spells").EnumerateArray())
            Spells.Add(new SpellDef { Id = JsonUtil.S(s, "id"), Name = JsonUtil.S(s, "n"), Mp = JsonUtil.F(s, "mp"), Cd = JsonUtil.F(s, "cd"), Cast = JsonUtil.F(s, "cast") });
    }

    public override void _Ready()
    {
        Instance = this; LoadSpells();
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
    void BuildModel()
    {
        _model = new Node3D { Name = "Model" }; AddChild(_model);
        var body = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.18f, 0.18f), Roughness = 0.8f };
        var skin = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.7f, 0.55f) };
        var cloth = new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.2f, 0.18f) };
        _model.AddChild(new MeshInstance3D { Mesh = new CapsuleMesh { Radius = 0.32f, Height = 1.2f }, MaterialOverride = body, Position = new Vector3(0, 1.15f, 0) });
        _model.AddChild(new MeshInstance3D { Mesh = new CapsuleMesh { Radius = 0.16f, Height = 0.9f }, MaterialOverride = cloth, Position = new Vector3(0.14f, 0.45f, 0) });
        _model.AddChild(new MeshInstance3D { Mesh = new CapsuleMesh { Radius = 0.16f, Height = 0.9f }, MaterialOverride = cloth, Position = new Vector3(-0.14f, 0.45f, 0) });
        _model.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.2f, Height = 0.4f }, MaterialOverride = skin, Position = new Vector3(0, 1.8f, 0) });
        _model.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.1f, 0.1f, 0.2f) }, MaterialOverride = skin, Position = new Vector3(0, 1.78f, -0.2f) });
        _hand = new Node3D { Position = new Vector3(0.5f, 1.1f, -0.1f) }; _model.AddChild(_hand);
        _shield = new Node3D { Position = new Vector3(-0.5f, 1.1f, -0.15f) }; _model.AddChild(_shield);
        _weaponRoot = new Node3D(); _hand.AddChild(_weaponRoot);
        // arms: static upper arms from the shoulders, forearms ride on the weapon hand and the shield
        Limb(_model, new Vector3(0.42f, 1.5f, 0), new Vector3(0.5f, 1.1f, -0.1f), 0.1f, body);
        Limb(_model, new Vector3(-0.42f, 1.5f, 0), new Vector3(-0.5f, 1.1f, -0.15f), 0.1f, body);
        Limb(_hand, new Vector3(0, 0, 0.05f), new Vector3(0, 0, -0.38f), 0.085f, skin);
        Limb(_shield, new Vector3(0.1f, 0, 0.3f), new Vector3(0.1f, 0, -0.05f), 0.085f, skin);
        _hand.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.1f, Height = 0.2f }, MaterialOverride = skin, Position = new Vector3(0, 0, -0.38f) });

        _horse = new Node3D { Visible = false }; AddChild(_horse);
        var hm = new StandardMaterial3D { AlbedoColor = new Color(0.4f, 0.26f, 0.16f) };
        _horse.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.7f, 0.8f, 2.0f) }, MaterialOverride = hm, Position = new Vector3(0, 1.0f, 0) });
        _horse.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.35f, 0.7f, 0.9f) }, MaterialOverride = hm, Position = new Vector3(0, 1.5f, -1.2f), Rotation = new Vector3(-0.5f, 0, 0) });
        for (int i = 0; i < 4; i++) _horse.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.18f, 0.9f, 0.18f) }, MaterialOverride = hm, Position = new Vector3(i % 2 == 0 ? 0.25f : -0.25f, 0.45f, i < 2 ? -0.8f : 0.8f) });
    }

    static void Limb(Node3D parent, Vector3 a, Vector3 b, float r, Material mat)
    {
        var dir = (b - a).Normalized(); var up = Mathf.Abs(dir.Y) > 0.95f ? Vector3.Right : Vector3.Up;
        var n = new Node3D { Transform = new Transform3D(Basis.LookingAt(dir, up), (a + b) / 2) }; parent.AddChild(n);
        n.AddChild(new MeshInstance3D { Mesh = new CapsuleMesh { Radius = r, Height = (b - a).Length() + r * 2 }, MaterialOverride = mat, Rotation = new Vector3(Mathf.Pi / 2, 0, 0) });
    }

    void RefreshWeapon()
    {
        var w = Game.D.Weapon; string wt = Game.D.WType();
        if (w == _shownWeapon && wt == _shownType) { UpdateShieldVisible(); return; }
        _shownWeapon = w; _shownType = wt;
        foreach (var c in _weaponRoot.GetChildren()) c.QueueFree();
        var steel = new StandardMaterial3D { AlbedoColor = new Color(0.8f, 0.82f, 0.88f), Metallic = 0.8f, Roughness = 0.35f };
        var wood = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.3f, 0.15f), Roughness = 0.9f };
        var orbCol = wt == "staff" ? new Color(0.5f, 0.9f, 1f) : new Color(0.75f, 0.45f, 1f);
        var glow = new StandardMaterial3D { AlbedoColor = orbCol, EmissionEnabled = true, Emission = orbCol, EmissionEnergyMultiplier = 2f };
        void A(Mesh m, Material mat, Vector3 p, Vector3? r = null) { var mi = new MeshInstance3D { Mesh = m, MaterialOverride = mat, Position = p }; if (r.HasValue) mi.Rotation = r.Value; _weaponRoot.AddChild(mi); }
        if (wt == "bow")
        {
            for (int i = -2; i <= 2; i++) { float a = i * 0.25f; A(new BoxMesh { Size = new Vector3(0.04f, 0.04f, 0.42f) }, wood, new Vector3(0, i * 0.3f, -0.45f + Mathf.Abs(i) * 0.1f), new Vector3(a, 0, 0)); }
            A(new BoxMesh { Size = new Vector3(0.01f, 1.4f, 0.01f) }, steel, new Vector3(0, 0, -0.1f));
        }
        else if (wt == "wand") { A(new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.03f, Height = 0.55f }, wood, new Vector3(0, 0, -0.3f), new Vector3(Mathf.Pi / 2, 0, 0)); A(new SphereMesh { Radius = 0.06f, Height = 0.12f }, glow, new Vector3(0, 0, -0.62f)); }
        else if (wt == "staff") { A(new CylinderMesh { TopRadius = 0.03f, BottomRadius = 0.035f, Height = 2.0f }, wood, new Vector3(0, 0, -0.5f), new Vector3(Mathf.Pi / 2, 0, 0)); A(new SphereMesh { Radius = 0.11f, Height = 0.22f }, glow, new Vector3(0, 0, -1.55f)); }
        else
        {
            A(new BoxMesh { Size = new Vector3(0.08f, 0.04f, 1.0f) }, steel, new Vector3(0, 0, -0.65f));
            A(new BoxMesh { Size = new Vector3(0.3f, 0.06f, 0.08f) }, wood, new Vector3(0, 0, -0.12f));
            A(new BoxMesh { Size = new Vector3(0.06f, 0.06f, 0.22f) }, wood, new Vector3(0, 0, 0.05f));
        }
        foreach (var c in _shield.GetChildren()) c.QueueFree();
        _shield.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.08f, 0.65f, 0.5f) }, MaterialOverride = wood });
        UpdateShieldVisible();
    }
    bool HasShield => Game.D.Eq.GetValueOrDefault("shield") != null && !(Game.D.Weapon?.Two ?? false);
    void UpdateShieldVisible() => _shield.Visible = HasShield;
    bool Ranged => Game.D.WType() != "sword";

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
            Ui.Banner("Level " + Game.D.Level, "+2 attribute points, +2 skill points"); Sfx.Play("level");
            Fx.Ring(GlobalPosition, 4f, new Color(1, 0.85f, 0.3f), 0.7f);
        }
        Game.Notify();
    }

    // ------------------------------------------------------------------ damage in
    public void TakeHit(HitInfo h)
    {
        if (Dead || _inv > 0 || _rollT > 0 || World.Shifting) return;
        var D = Game.D; var TS = Game.TS;
        float baseDmg = h.Damage * 100f / (100f + D.Defense() * 5f);
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
            _atkT = 0; _castT = 0; _castIdx = -1;
        }
        if (TS.Has("manaShield") && Mp > 0) { float m = Mathf.Min(Mp, dmg * 0.35f); Mp -= m; dmg -= m; }
        if (TS["thorns"] > 0 && h.Attacker is Enemy te && !h.IsProjectile && te.GlobalPosition.DistanceTo(GlobalPosition) < 6f)
            te.TakeHit(new HitInfo { Damage = h.Damage * TS["thorns"] / 100f, From = GlobalPosition, IsPlayer = true, Attacker = this });
        int final = Mathf.Max(1, Mathf.RoundToInt(dmg));
        Hp -= final; _inv = 0.2f;
        Rig?.Shake(0.18f); Feel.HitStop(Feel.Taken);
        FloatText.Spawn(this, GlobalPosition + new Vector3(0, 2.1f, 0), "-" + final, new Color(1, 0.4f, 0.35f));
        if (Hp <= 0)
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
        string wt = Game.D.WType(); bool ranged = wt != "sword";
        float cost = ranged ? (power ? 16f : 7f) : (power ? 26f : 9f);
        if (St < 4f) { FloatText.Spawn(this, GlobalPosition + new Vector3(0, 2.2f, 0), "Out of stamina", new Color(0.6f, 0.8f, 1f), 0.8f); return false; }
        UseStamina(cost);
        if (!ranged) { if (!power) _combo = _comboT > 0 ? (_combo + 1) % 3 : 0; else _combo = 0; } else _combo = 0;
        float baseT = wt == "sword" ? (power ? .85f : .5f) : wt == "bow" ? (power ? 1.0f : .6f) : wt == "wand" ? (power ? .7f : .4f) : (power ? 1.1f : .85f);
        _atkPower = power; _atkDur = baseT / Game.D.AtkSpeed(); _atkT = _atkDur; _hitDone = false; _comboT = _atkDur + 0.55f;
        float camYaw = Rig?.Yaw ?? _modelYaw; float face = camYaw;
        if (!ranged)
        {
            float best = 5.5f;
            foreach (var n in GetTree().GetNodesInGroup("enemies"))
            {
                if (n is not Enemy e || e.Dead || !e.Active) continue;
                var d = e.GlobalPosition - GlobalPosition; d.Y = 0; float dd = d.Length() - e.Def.R;
                if (dd < best && Mathf.Abs(Util.AngDiff(camYaw, Util.YawTo(d.X, d.Z))) < 0.9f) { best = dd; face = Util.YawTo(d.X, d.Z); }
            }
        }
        _modelYaw = face; Sfx.Play("swing");
        return true;
    }

    void ResolveSword()
    {
        var D = Game.D; var TS = Game.TS;
        bool comboHit = !_atkPower && _combo == 2, cleave = TS.Has("cleave") && (_atkPower || comboHit);
        float range = (_atkPower ? 3.6f : 3.0f) + (cleave ? .8f : 0f), arc = cleave ? Mathf.Pi : _atkPower ? 1.35f : 1.0f;
        int hits = 0;
        foreach (var n in GetTree().GetNodesInGroup("enemies"))
        {
            if (n is not Enemy e || e.Dead || !e.Active) continue;
            var d = e.GlobalPosition - GlobalPosition; d.Y = 0; float dist = d.Length() - e.Def.R * e.ScaleF();
            if (dist > range) continue;
            if (dist > 0.6f && Mathf.Abs(Util.AngDiff(_modelYaw, Util.YawTo(d.X, d.Z))) > arc + (e.Def.R > 1 ? 0.5f : 0f)) continue;
            if (Mathf.Abs(e.GlobalPosition.Y - GlobalPosition.Y) > 3.5f + e.Def.R) continue;
            bool crit = GD.Randf() < D.CritChance();
            float dmg = D.WpnDmg() * (float)GD.RandRange(0.9, 1.1) * (_atkPower ? 2.2f : comboHit ? 1.35f : 1f) * (crit ? D.CritMult() : 1f);
            e.TakeHit(new HitInfo { Damage = dmg, From = GlobalPosition, Knock = _atkPower ? 9f : comboHit ? 5f : 2.5f, Stun = _atkPower ? 0.6f : 0f, Crit = crit, Attacker = this, IsPlayer = true, Kind = _atkPower ? "power" : comboHit ? "combo" : "light" });
            hits++;
        }
        if (hits > 0) { Rig?.Shake(_atkPower ? 0.25f : 0.08f); Sfx.Play("hit"); }
    }

    void FireWeapon()
    {
        var D = Game.D; var TS = Game.TS; string wt = D.WType();
        var origin = MuzzlePos(); var (target, _) = AimPoint(80f);
        var dir = (target - origin).Normalized();
        bool crit = GD.Randf() < D.CritChance(); float dmg = D.WpnDmg() * (float)GD.RandRange(0.9, 1.1) * (crit ? D.CritMult() : 1f);
        if (wt == "bow")
        {
            int n = TS.Has("multi") ? 3 : 1; float sp = _atkPower ? 64f : 50f, pd = dmg * (_atkPower ? 2f : 1f) * (n > 1 ? 0.55f : 1f);
            for (int k = 0; k < n; k++)
            {
                float a = (k - (n - 1) / 2f) * 0.11f;
                var d = dir.Rotated(Vector3.Up, -a);
                Projectile.Spawn("arrow", 'p', origin, d * sp + Vector3.Up * 0.6f, pd, 2.4f, crit, _atkPower || TS.Has("pierce"));
            }
            Sfx.Play("swing");
        }
        else if (wt == "wand")
        {
            float cost = _atkPower ? 8f : 3f; bool ok = Mp >= cost; Mp = Mathf.Max(0, Mp - cost); if (!ok) dmg *= 0.5f;
            Projectile.Spawn("bolt", 'p', origin, dir * 44f, dmg * (_atkPower ? 1.9f : 1f), 1.6f, crit, TS.Has("pierce")); Sfx.Play("zap");
        }
        else
        {
            float cost = _atkPower ? 16f : 8f; bool ok = Mp >= cost; Mp = Mathf.Max(0, Mp - cost); if (!ok) dmg *= 0.5f;
            Projectile.Spawn("bolt", 'p', origin, dir * 28f, dmg * (_atkPower ? 2.6f : 1.35f), 2.2f, crit, false, _atkPower ? 3.4f : 2.2f, true); Sfx.Play("fire");
        }
    }

    // ------------------------------------------------------------------ spells
    void TryCast(int i)
    {
        if (Dead || _castT > 0 || _rollT > 0 || _stagger > 0 || i >= Spells.Count) return;
        if (Mounted) { Ui.Note("You cannot cast while mounted", new Color(0.7f, 0.8f, 1)); return; }
        if (SpellCd[i] > 0) return;
        var sp = Spells[i]; var D = Game.D;
        int mc = Mathf.RoundToInt(sp.Mp * (1f + Game.TS["mcost"] / 100f) * (1f - (D.Weapon?.Mc ?? 0f)));
        if (Mp < mc) { Ui.Note("Not enough mana", new Color(0.4f, 0.65f, 1f)); return; }
        Mp -= mc; SpellCd[i] = sp.Cd * (1f - Game.TS["cdr"] / 100f); _castIdx = i; _castT = _castDur = sp.Cast; _atkT = 0;
        _modelYaw = Rig?.Yaw ?? _modelYaw; _fired = false;
    }

    void FireSpell(int i)
    {
        var sp = Spells[i]; var D = Game.D; float pow = D.SpellPow(); var origin = MuzzlePos();
        if (sp.Id == "fire")
        {
            var (t, _) = AimPoint(70f); var dir = (t - origin).Normalized();
            Projectile.Spawn("fire", 'p', origin, dir * 38f, (20 + D.Level * 3.2f) * pow, 2.2f, false, false, 3f); Sfx.Play("fire");
        }
        else if (sp.Id == "nova")
        {
            float dmg = (14 + D.Level * 2.6f) * pow; Sfx.Play("ice");
            Fx.Ring(GlobalPosition + Vector3.Up * 0.1f, 7.5f, new Color(0.55f, 0.85f, 1f), 0.5f); Fx.Burst(GlobalPosition + Vector3.Up, new Color(0.7f, 0.9f, 1f), 40, 9f, 0.7f);
            foreach (var n in GetTree().GetNodesInGroup("enemies"))
                if (n is Enemy e && !e.Dead && e.Active && (e.GlobalPosition - GlobalPosition).Length() - e.Def.R < 7.5f)
                { e.Slow(4f); e.TakeHit(new HitInfo { Damage = dmg, From = GlobalPosition, Knock = 4f, Stun = 0.3f, Attacker = this, IsPlayer = true }); }
        }
        else if (sp.Id == "heal")
        {
            float amt = D.MaxHp() * 0.35f; Hp = Mathf.Min(D.MaxHp(), Hp + amt); Sfx.Play("heal");
            Fx.Burst(GlobalPosition + Vector3.Up, new Color(0.5f, 1f, 0.6f), 30, 2.5f, 1f, 0.1f, 1f);
            FloatText.Spawn(this, GlobalPosition + new Vector3(0, 2.2f, 0), "+" + Mathf.RoundToInt(amt), new Color(0.5f, 1f, 0.6f));
        }
        else if (sp.Id == "zap")
        {
            var (t, cur) = AimPoint(40f);
            if (cur == null)
            {
                float bd = 28f;
                foreach (var n in GetTree().GetNodesInGroup("enemies"))
                {
                    if (n is not Enemy e || e.Dead || !e.Active) continue;
                    var d = e.GlobalPosition - GlobalPosition; d.Y = 0; float dist = d.Length();
                    if (dist < bd && Mathf.Abs(Util.AngDiff(Rig?.Yaw ?? _modelYaw, Util.YawTo(d.X, d.Z))) < 1.2f) { bd = dist; cur = e; }
                }
            }
            Sfx.Play("zap");
            if (cur == null) { Fx.Line(origin, t, new Color(0.7f, 0.8f, 1f)); return; }
            var prev = origin; float dm = (24 + D.Level * 3) * pow; var seen = new HashSet<Enemy>();
            for (int j = 0; j < 4 + (int)Game.TS["chain"] && cur != null; j++)
            {
                var pt = cur.GlobalPosition + new Vector3(0, cur.HeightM() * 0.55f, 0);
                Fx.Line(prev, pt, new Color(0.75f, 0.85f, 1f), 0.2f); seen.Add(cur);
                cur.TakeHit(new HitInfo { Damage = dm, From = GlobalPosition, Knock = 1.5f, Stun = 0.25f, Attacker = this, IsPlayer = true });
                dm *= 0.82f; prev = pt; Enemy? nx = null; float bd2 = 10f;
                foreach (var n in GetTree().GetNodesInGroup("enemies"))
                {
                    if (n is not Enemy e || e.Dead || !e.Active || seen.Contains(e)) continue;
                    float dist = (e.GlobalPosition - cur.GlobalPosition).Length();
                    if (dist < bd2) { bd2 = dist; nx = e; }
                }
                cur = nx;
            }
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
            Prompt = "[E] " + best.Prompt;
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
        for (int i = 0; i < 4; i++) SpellCd[i] = Mathf.Max(0, SpellCd[i] - dt);

        if (Dead)
        {
            _deadT += dt; v.X = v.Z = 0; Velocity = v; MoveAndSlide();
            _model.Rotation = new Vector3(Mathf.Lerp(0, -Mathf.Pi / 2, Mathf.Clamp(_deadT / 0.5f, 0, 1)), _modelYaw, 0);
            if (_deadT > 3f && !Arenas.Active) Respawner.PlayerDied();
            return;
        }

        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;
        var input = Input.GetVector(Inputs.Left, Inputs.Right, Inputs.Forward, Inputs.Back);
        float camYaw = Rig?.Yaw ?? 0f;
        var dir = new Vector3(input.X, 0, input.Y).Rotated(Vector3.Up, camYaw);

        // ---- hotkeys
        for (int i = 0; i < 4; i++) if (captured && Input.IsActionJustPressed("spell" + (i + 1))) TryCast(i);
        if (captured && Input.IsActionJustPressed(Inputs.PotHp)) UsePotion(true);
        if (captured && Input.IsActionJustPressed(Inputs.PotMp)) UsePotion(false);
        if (captured && Input.IsActionJustPressed(Inputs.Mount)) ToggleMount();
        if (captured && Input.IsActionJustPressed(Inputs.Realm)) World.TryShiftRealm();
        if (captured) ScanInteract(); else Prompt = "";

        // ---- attack input: tap = light, hold = power (fires on release)
        bool lmb = captured && Input.IsMouseButtonPressed(MouseButton.Left);
        if (lmb) _lmbHold = _lmbPrev ? _lmbHold + dt : 0f;
        if (!lmb && _lmbPrev)
        {
            bool power = _lmbHold >= 0.35f;
            if (!TryAttack(power)) { _hasQueue = true; _queuePower = power; _queueT = 0.3f; }
        }
        _lmbPrev = lmb;
        if (_hasQueue) { _queueT -= dt; if (_queueT <= 0) _hasQueue = false; else if (_atkT <= 0 && TryAttack(_queuePower)) _hasQueue = false; }

        // ---- block
        bool wantBlock = captured && HasShield && Input.IsMouseButtonPressed(MouseButton.Right) && _atkT <= 0 && _rollT <= 0 && _stagger <= 0 && _castT <= 0 && St > 0 && !Mounted;
        if (wantBlock && !_blocking) _blockT = 0;
        _blocking = wantBlock;
        if (_blocking) { _blockT += dt; _modelYaw = camYaw; }

        // ---- attack + cast timelines
        if (_atkT > 0)
        {
            _atkT -= dt; float prog = 1f - _atkT / _atkDur;
            if (!_hitDone && prog >= (Ranged ? 0.4f : 0.45f)) { _hitDone = true; if (Ranged) FireWeapon(); else ResolveSword(); }
            if (Ranged) _modelYaw = camYaw;
        }
        if (_castT > 0)
        {
            _castT -= dt; _modelYaw = camYaw;
            if (!_fired && _castT <= 0 && _castIdx >= 0) { _fired = true; FireSpell(_castIdx); _castIdx = -1; }
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
        if (_atkT <= 0 && !_blocking && _rollT <= 0 && _castT <= 0 && flat.LengthSquared() > 0.5f)
            _modelYaw = Mathf.LerpAngle(_modelYaw, Util.YawTo(flat.X, flat.Z), 1f - Mathf.Exp(-14f * dt));

        Velocity = v; MoveAndSlide();
        Animate(dt);
        Game.PlayTime += dt;
    }

    // ------------------------------------------------------------------ placeholder animation
    void Animate(float dt)
    {
        float roll = _rollT > 0 ? (1f - _rollT / RollTime) * Mathf.Tau : 0f;
        _model.Rotation = new Vector3(roll, _modelYaw, 0);
        _model.Position = new Vector3(0, Mounted ? 0.9f : 0f, 0);
        _horse.Rotation = new Vector3(0, _modelYaw, 0);

        string wt = Game.D.WType();
        Vector3 target = wt == "sword" ? new Vector3(0.5f, 0, 0) : new Vector3(0.1f, 0, 0);
        float k = 1f - Mathf.Exp(-18f * dt);
        if (_castT > 0) { float p = 1f - _castT / Mathf.Max(0.05f, _castDur); target = new Vector3(Mathf.Lerp(0.2f, 1.2f, p), 0, 0); }
        else if (_atkT > 0)
        {
            float p = 1f - _atkT / _atkDur, wind = 0.35f;
            float t = Mathf.Clamp(p < wind ? p / wind : (p - wind) / (1f - wind), 0, 1), e = t * t * (3f - 2f * t);
            if (wt != "sword") target = p < wind ? new Vector3(Mathf.Lerp(0.1f, 0.6f, e), 0, 0) : new Vector3(Mathf.Lerp(0.6f, -0.3f, e), 0, 0);
            else if (_atkPower || _combo == 0) target = p < wind ? new Vector3(Mathf.Lerp(0.5f, 1.9f, e), 0, 0) : new Vector3(Mathf.Lerp(1.9f, -0.9f, e), 0, 0);
            else if (_combo == 1) target = p < wind ? new Vector3(0.1f, Mathf.Lerp(0, -1.3f, e), 0) : new Vector3(0.1f, Mathf.Lerp(-1.3f, 1.3f, e), 0);
            else target = p < wind ? new Vector3(0.1f, Mathf.Lerp(0, 1.3f, e), 0) : new Vector3(0.1f, Mathf.Lerp(1.3f, -1.3f, e), 0);
            k = 1f;
        }
        else if (_blocking) target = new Vector3(0.9f, 0.3f, 0);
        _hand.Rotation = _hand.Rotation.Lerp(target, k);
        var shieldPos = _blocking ? new Vector3(-0.15f, 1.15f, -0.65f) : new Vector3(-0.5f, 1.1f, -0.15f);
        _shield.Position = _shield.Position.Lerp(shieldPos, 1f - Mathf.Exp(-16f * dt));
        _shield.Rotation = new Vector3(0, _blocking ? Mathf.Pi / 2 : 0, 0);
    }
}
