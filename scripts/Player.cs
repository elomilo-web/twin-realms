using Godot;

namespace TwinRealms;

/// <summary>Walk, sprint, jump, roll, sword combo, power attack, block and parry. Movement is relative to the camera.</summary>
public partial class Player : CharacterBody3D, IDamageable
{
    public static Player? Instance;
    public CameraRig? Rig;
    public PlayerStats Stats = new();
    public float Hp, St;
    public bool Dead { get; private set; }

    [Export] public float WalkSpeed = 5.2f;
    [Export] public float SprintSpeed = 8.4f;
    [Export] public float JumpVelocity = 6.0f;
    [Export] public float RollSpeed = 11f;
    [Export] public float RollTime = 0.45f;
    [Export] public float RollCooldown = 0.6f;
    const float Accel = 40f, AirAccel = 10f;

    Node3D _model = null!, _hand = null!, _shield = null!;
    float _gravity, _modelYaw;
    float _rollT, _rollCd, _stLockT, _inv, _stagger;
    Vector3 _rollDir, _kb;

    // combat state
    bool _lmbPrev; float _lmbHold;
    float _atkT, _atkDur, _comboT, _blockT, _queueT; bool _queuePower, _hasQueue;
    int _combo; bool _atkPower, _hitDone, _blocking;
    float _deadT;

    public override void _Ready()
    {
        Instance = this;
        AddToGroup("player");
        CollisionMask = 1 | 2;
        _gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity");
        Hp = Stats.MaxHp; St = Stats.MaxSt;
        AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 0.4f, Height = 1.8f }, Position = new Vector3(0, 0.9f, 0) });
        BuildModel();
    }

    public override void _ExitTree() { if (Instance == this) Instance = null; }

    void BuildModel()
    {
        _model = new Node3D { Name = "Model" };
        AddChild(_model);
        var body = new StandardMaterial3D { AlbedoColor = new Color(0.75f, 0.2f, 0.2f), Roughness = 0.8f };
        var nose = new StandardMaterial3D { AlbedoColor = new Color(0.95f, 0.85f, 0.4f) };
        var steel = new StandardMaterial3D { AlbedoColor = new Color(0.8f, 0.82f, 0.88f), Metallic = 0.8f, Roughness = 0.35f };
        var wood = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.3f, 0.15f), Roughness = 0.9f };
        _model.AddChild(new MeshInstance3D { Mesh = new CapsuleMesh { Radius = 0.4f, Height = 1.8f }, MaterialOverride = body, Position = new Vector3(0, 0.9f, 0) });
        _model.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.2f, 0.2f, 0.4f) }, MaterialOverride = nose, Position = new Vector3(0, 1.4f, -0.4f) });

        // sword: pivot at the right hand, blade points along -Z
        _hand = new Node3D { Position = new Vector3(0.5f, 1.1f, -0.1f) };
        _model.AddChild(_hand);
        _hand.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.08f, 0.04f, 1.0f) }, MaterialOverride = steel, Position = new Vector3(0, 0, -0.65f) });
        _hand.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.3f, 0.06f, 0.08f) }, MaterialOverride = wood, Position = new Vector3(0, 0, -0.12f) });
        _hand.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.06f, 0.06f, 0.22f) }, MaterialOverride = wood, Position = new Vector3(0, 0, 0.05f) });

        // shield on the left arm
        _shield = new Node3D { Position = new Vector3(-0.5f, 1.1f, -0.15f) };
        _model.AddChild(_shield);
        _shield.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.08f, 0.65f, 0.5f) }, MaterialOverride = wood });
    }

    // ---------------------------------------------------------------- stamina
    void UseStamina(float n) { St = Mathf.Max(0, St - n); _stLockT = 0.7f; }

    // ---------------------------------------------------------------- damage in
    public void OnEnemyKilled(Enemy e)
    {
        Stats.Kills++;
        int lv = Stats.Level;
        if (Stats.GainXp(e.Def.Xp) > 0)
        {
            Hp = Stats.MaxHp; St = Stats.MaxSt;
            FloatText.Spawn(this, GlobalPosition + new Vector3(0, 2.6f, 0), "LEVEL UP " + Stats.Level, new Color(1, 0.85f, 0.3f), 1.4f);
        }
        FloatText.Spawn(this, e.GlobalPosition + new Vector3(0, 1.2f, 0), "+" + e.Def.Xp + " xp", new Color(0.6f, 0.9f, 1f), 0.8f);
    }

    public void TakeHit(HitInfo h)
    {
        if (Dead || _inv > 0 || _rollT > 0) return;
        float baseDmg = h.Damage * 100f / (100f + Stats.Defense * 5f);
        var toSrc = h.From - GlobalPosition; toSrc.Y = 0;
        float srcYaw = Util.YawTo(toSrc.X, toSrc.Z);
        bool front = Mathf.Abs(Util.AngDiff(_modelYaw, srcYaw)) < 1.25f;
        float dmg = baseDmg;
        var away = -toSrc; away.Y = 0;
        if (_blocking && front)
        {
            if (_blockT < 0.22f && h.Attacker is Enemy en)
            {
                en.Stun(0.9f);
                FloatText.Spawn(this, GlobalPosition + new Vector3(0, 2.3f, 0), "PARRY", new Color(1, 0.88f, 0.5f), 1.3f);
                _inv = 0.3f;
                return;
            }
            float red = Mathf.Clamp(0.55f + Stats.ShieldDef * 0.012f, 0.5f, 0.92f);
            dmg = baseDmg * (1f - red);
            UseStamina(h.Damage * 0.3f);
            if (St <= 0)
            {
                _blocking = false; _stagger = 0.7f;
                FloatText.Spawn(this, GlobalPosition + new Vector3(0, 2.3f, 0), "Guard broken", new Color(1, 0.6f, 0.45f));
            }
        }
        else
        {
            if (away.LengthSquared() > 0.001f) _kb += away.Normalized() * h.Knock;
            _stagger = Mathf.Max(_stagger, 0.18f);
            _atkT = 0; // getting hit interrupts a swing
        }
        int final = Mathf.Max(1, Mathf.RoundToInt(dmg));
        Hp -= final; _inv = 0.2f;
        FloatText.Spawn(this, GlobalPosition + new Vector3(0, 2.1f, 0), "-" + final, new Color(1, 0.4f, 0.35f));
        if (Hp <= 0) Die();
    }

    void Die()
    {
        Dead = true; Hp = 0; _deadT = 0; _atkT = 0; _blocking = false;
    }

    // ---------------------------------------------------------------- attacks
    bool TryAttack(bool power)
    {
        if (Dead || _atkT > 0 || _rollT > 0 || _stagger > 0) return false;
        if (St < 4f) { FloatText.Spawn(this, GlobalPosition + new Vector3(0, 2.2f, 0), "Out of stamina", new Color(0.6f, 0.8f, 1f), 0.8f); return false; }
        UseStamina(power ? 26f : 9f);
        if (!power) _combo = _comboT > 0 ? (_combo + 1) % 3 : 0; else _combo = 0;
        _atkPower = power;
        _atkDur = power ? 0.85f : 0.5f;
        _atkT = _atkDur; _hitDone = false;
        _comboT = _atkDur + 0.55f;
        // face the nearest enemy in front of the camera, otherwise face where the camera looks
        float camYaw = Rig?.Yaw ?? _modelYaw;
        float face = camYaw; float best = 5.5f;
        foreach (var n in GetTree().GetNodesInGroup("enemies"))
        {
            if (n is not Enemy e || e.Dead) continue;
            var d = e.GlobalPosition - GlobalPosition; d.Y = 0;
            float dd = d.Length() - e.Def.R;
            if (dd < best && Mathf.Abs(Util.AngDiff(camYaw, Util.YawTo(d.X, d.Z))) < 0.9f) { best = dd; face = Util.YawTo(d.X, d.Z); }
        }
        _modelYaw = face;
        return true;
    }

    void ResolveHit()
    {
        float range = _atkPower ? 3.6f : 3.0f;
        float arc = _atkPower ? 1.35f : 1.0f;
        bool comboHit = !_atkPower && _combo == 2;
        int hits = 0;
        foreach (var n in GetTree().GetNodesInGroup("enemies"))
        {
            if (n is not Enemy e || e.Dead) continue;
            var d = e.GlobalPosition - GlobalPosition; d.Y = 0;
            float dist = d.Length() - e.Def.R;
            if (dist > range) continue;
            if (dist > 0.6f && Mathf.Abs(Util.AngDiff(_modelYaw, Util.YawTo(d.X, d.Z))) > arc) continue;
            bool crit = GD.Randf() < 0.05f;
            float dmg = Stats.SwordDamage * (float)GD.RandRange(0.9, 1.1) * (_atkPower ? 2.2f : comboHit ? 1.35f : 1f) * (crit ? 1.7f : 1f);
            e.TakeHit(new HitInfo { Damage = dmg, From = GlobalPosition, Knock = _atkPower ? 9f : comboHit ? 5f : 2.5f, Stun = _atkPower ? 0.6f : 0f, Crit = crit, Attacker = this });
            hits++;
        }
        if (hits > 0) Rig?.Shake(_atkPower ? 0.25f : 0.08f);
    }

    // ---------------------------------------------------------------- main loop
    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        var v = Velocity;
        if (!IsOnFloor()) v.Y -= _gravity * dt;
        _rollCd = Mathf.Max(0, _rollCd - dt); _inv = Mathf.Max(0, _inv - dt); _stagger = Mathf.Max(0, _stagger - dt);
        _comboT = Mathf.Max(0, _comboT - dt); _stLockT = Mathf.Max(0, _stLockT - dt);

        if (Dead)
        {
            _deadT += dt;
            v.X = v.Z = 0; Velocity = v; MoveAndSlide();
            _model.Rotation = new Vector3(Mathf.Lerp(0, -Mathf.Pi / 2, Mathf.Clamp(_deadT / 0.5f, 0, 1)), _modelYaw, 0);
            if (_deadT > 3f) GetTree().ReloadCurrentScene();
            return;
        }

        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;
        var input = Input.GetVector(Inputs.Left, Inputs.Right, Inputs.Forward, Inputs.Back);
        float camYaw = Rig?.Yaw ?? 0f;
        var dir = new Vector3(input.X, 0, input.Y).Rotated(Vector3.Up, camYaw);

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
        bool wantBlock = captured && Input.IsMouseButtonPressed(MouseButton.Right) && _atkT <= 0 && _rollT <= 0 && _stagger <= 0 && St > 0;
        if (wantBlock && !_blocking) _blockT = 0;
        _blocking = wantBlock;
        if (_blocking) { _blockT += dt; _modelYaw = camYaw; }

        // ---- attack timeline
        if (_atkT > 0)
        {
            _atkT -= dt;
            float prog = 1f - _atkT / _atkDur;
            if (!_hitDone && prog >= 0.45f) { _hitDone = true; ResolveHit(); }
        }

        // ---- movement
        if (_rollT > 0)
        {
            _rollT -= dt; _inv = Mathf.Max(_inv, 0.05f);
            v.X = _rollDir.X * RollSpeed; v.Z = _rollDir.Z * RollSpeed;
        }
        else
        {
            if (Input.IsActionJustPressed(Inputs.Roll) && IsOnFloor() && _rollCd <= 0 && _atkT <= 0 && _stagger <= 0 && St >= 12f)
            {
                _rollDir = dir.LengthSquared() > 0.01f ? dir.Normalized() : new Vector3(-Mathf.Sin(_modelYaw), 0, -Mathf.Cos(_modelYaw));
                _rollT = RollTime; _rollCd = RollCooldown; UseStamina(14f);
                _modelYaw = Util.YawTo(_rollDir.X, _rollDir.Z);
            }
            if (Input.IsActionJustPressed(Inputs.Jump) && IsOnFloor() && _atkT <= 0 && _stagger <= 0) v.Y = JumpVelocity;

            bool sprint = Input.IsActionPressed(Inputs.Sprint) && St > 1f && dir.LengthSquared() > 0.01f && _atkT <= 0 && !_blocking;
            float speed = sprint ? SprintSpeed : WalkSpeed;
            if (_atkT > 0) speed *= 0.25f;
            if (_blocking) speed *= 0.5f;
            if (_stagger > 0) speed = 0;
            if (sprint) { St = Mathf.Max(0, St - 10f * dt); _stLockT = 0.5f; }
            var target = dir * speed;
            float a = (IsOnFloor() ? Accel : AirAccel) * dt;
            v.X = Mathf.MoveToward(v.X, target.X, a);
            v.Z = Mathf.MoveToward(v.Z, target.Z, a);
        }
        _kb = _kb.MoveToward(Vector3.Zero, 25f * dt);
        v.X += _kb.X * 0.3f; v.Z += _kb.Z * 0.3f;

        // ---- stamina regen
        if (_stLockT <= 0 && !_blocking && _atkT <= 0) St = Mathf.Min(Stats.MaxSt, St + 24f * dt);

        // ---- facing
        var flat = new Vector3(v.X, 0, v.Z);
        if (_atkT <= 0 && !_blocking && _rollT <= 0 && flat.LengthSquared() > 0.5f)
            _modelYaw = Mathf.LerpAngle(_modelYaw, Util.YawTo(flat.X, flat.Z), 1f - Mathf.Exp(-14f * dt));

        Velocity = v;
        MoveAndSlide();
        Animate(dt);
    }

    // ---------------------------------------------------------------- placeholder animation
    void Animate(float dt)
    {
        float roll = _rollT > 0 ? (1f - _rollT / RollTime) * Mathf.Tau : 0f;
        _model.Rotation = new Vector3(roll, _modelYaw, 0);

        // sword
        Vector3 target = new Vector3(0.5f, 0, 0);            // idle: tip up a little
        float k = 1f - Mathf.Exp(-18f * dt);
        if (_atkT > 0)
        {
            float p = 1f - _atkT / _atkDur;
            float wind = 0.35f;
            float t = p < wind ? p / wind : (p - wind) / (1f - wind);
            t = Mathf.Clamp(t, 0, 1);
            float e = t * t * (3f - 2f * t);
            if (_atkPower || _combo == 0)               // overhead chop
                target = p < wind ? new Vector3(Mathf.Lerp(0.5f, 1.9f, e), 0, 0) : new Vector3(Mathf.Lerp(1.9f, -0.9f, e), 0, 0);
            else if (_combo == 1)                        // right-to-left slash
                target = p < wind ? new Vector3(0.1f, Mathf.Lerp(0, -1.3f, e), 0) : new Vector3(0.1f, Mathf.Lerp(-1.3f, 1.3f, e), 0);
            else                                         // left-to-right slash
                target = p < wind ? new Vector3(0.1f, Mathf.Lerp(0, 1.3f, e), 0) : new Vector3(0.1f, Mathf.Lerp(1.3f, -1.3f, e), 0);
            k = 1f; // snap to keyframes during a swing
        }
        else if (_blocking) target = new Vector3(0.9f, 0.3f, 0);
        _hand.Rotation = _hand.Rotation.Lerp(target, k);

        var shieldPos = _blocking ? new Vector3(-0.15f, 1.15f, -0.65f) : new Vector3(-0.5f, 1.1f, -0.15f);
        _shield.Position = _shield.Position.Lerp(shieldPos, 1f - Mathf.Exp(-16f * dt));
        _shield.Rotation = new Vector3(0, _blocking ? Mathf.Pi / 2 : 0, 0);
    }
}
