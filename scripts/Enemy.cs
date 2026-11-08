using Godot;

namespace TwinRealms;

/// <summary>Melee enemy driven by data/enemies.json. States: Idle, Chase, Wind (telegraph), Recover, Stagger, Dead.</summary>
public partial class Enemy : CharacterBody3D, IDamageable
{
    enum State { Idle, Chase, Wind, Recover, Stagger, Dead }

    public string Type = "wolf";
    public int Level = 1;
    public EnemyDef Def = new();
    public float Hp, MaxHp, Damage;
    public bool Dead => _state == State.Dead;

    State _state = State.Idle;
    float _timer, _stun, _flash, _gravity, _yaw, _deadT;
    Vector3 _kb, _home;
    Node3D _body = null!;
    StandardMaterial3D _mat = null!;
    Label3D _label = null!;
    Color _baseColor;

    public override void _Ready()
    {
        Def = EnemyDb.Get(Type);
        MaxHp = Hp = Scaling.EnemyHp(Def.Hp, Level);
        Damage = Scaling.EnemyDmg(Def.Dmg, Level);
        _home = GlobalPosition;
        _gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity");
        AddToGroup("enemies");
        CollisionLayer = 2; CollisionMask = 1 | 2;

        float s = Def.Scale <= 0 ? 1f : Def.Scale;
        float r = Def.R;
        AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = r, Height = Def.Kind == "quad" ? r * 2.4f : 1.8f * s }, Position = new Vector3(0, (Def.Kind == "quad" ? r * 1.2f : 0.9f * s), 0) });

        _baseColor = Def.Color != 0x888888 ? new Color(((Def.Color >> 16) & 255) / 255f, ((Def.Color >> 8) & 255) / 255f, (Def.Color & 255) / 255f) : DefaultColor(Type);
        _mat = new StandardMaterial3D { AlbedoColor = _baseColor, Roughness = 0.85f };
        _body = new Node3D { Name = "Body" };
        AddChild(_body);
        if (Def.Kind == "quad")
        {
            _body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(r * 1.3f, r * 1.4f, r * 3.2f) }, MaterialOverride = _mat, Position = new Vector3(0, r * 1.1f, 0) });
            _body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(r * 0.9f, r * 0.9f, r * 1.1f) }, MaterialOverride = _mat, Position = new Vector3(0, r * 1.5f, -r * 1.9f) });
        }
        else
        {
            _body.AddChild(new MeshInstance3D { Mesh = new CapsuleMesh { Radius = r, Height = 1.8f * s }, MaterialOverride = _mat, Position = new Vector3(0, 0.9f * s, 0) });
            _body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.18f, 0.18f, 0.35f) * s }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.1f, 0.1f, 0.1f) }, Position = new Vector3(0, 1.45f * s, -r) });
        }
        _label = new Label3D
        {
            Text = $"{Def.N}  Lv{Level}", FontSize = 36, OutlineSize = 8, PixelSize = 0.006f, Modulate = new Color(1, 0.85f, 0.7f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = new Vector3(0, (Def.Kind == "quad" ? r * 2.6f : 2.1f * s) + 0.3f, 0),
        };
        AddChild(_label);
        _yaw = (float)GD.RandRange(0, Mathf.Tau);
    }

    static Color DefaultColor(string type) => type switch
    {
        "bandit" => new Color(0.45f, 0.3f, 0.2f),
        "goblin" => new Color(0.35f, 0.6f, 0.25f),
        "brute" => new Color(0.6f, 0.45f, 0.3f),
        "skeleton" => new Color(0.85f, 0.82f, 0.7f),
        "wraith" => new Color(0.5f, 0.35f, 0.7f),
        _ => new Color(0.5f, 0.5f, 0.5f),
    };

    public void Stun(float t)
    {
        if (Dead) return;
        _stun = Mathf.Max(_stun, t);
        _state = State.Stagger;
    }

    public void TakeHit(HitInfo h)
    {
        if (Dead) return;
        int dmg = Mathf.Max(1, Mathf.RoundToInt(h.Damage));
        Hp -= dmg; _flash = 0.12f;
        FloatText.Spawn(this, GlobalPosition + new Vector3(0, 2.0f, 0), h.Crit ? dmg + "!" : dmg.ToString(), h.Crit ? new Color(1, 0.82f, 0.3f) : Colors.White);
        var away = GlobalPosition - h.From; away.Y = 0;
        if (away.LengthSquared() > 0.001f && !Def.Boss) _kb += away.Normalized() * h.Knock * 1.6f;
        if (h.Stun > 0 && !Def.Boss) Stun(h.Stun);
        else if (_state == State.Idle) _state = State.Chase;
        if (Hp <= 0) Die();
    }

    void Die()
    {
        _state = State.Dead; _deadT = 0;
        CollisionLayer = 0; CollisionMask = 1;
        _label.Visible = false;
        var p = Player.Instance;
        if (p != null) p.OnEnemyKilled(this);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        var v = Velocity;
        if (!IsOnFloor()) v.Y -= _gravity * dt; else v.Y = 0;

        if (_flash > 0) { _flash -= dt; _mat.AlbedoColor = Colors.White; }
        else if (_state != State.Wind) _mat.AlbedoColor = _baseColor;

        if (_state == State.Dead)
        {
            _deadT += dt;
            _body.Rotation = new Vector3(Mathf.Lerp(0, Mathf.Pi / 2, Mathf.Clamp(_deadT / 0.5f, 0, 1)), 0, 0);
            if (_deadT > 8f) QueueFree();
            Velocity = new Vector3(0, v.Y, 0); MoveAndSlide();
            return;
        }

        var pl = Player.Instance;
        Vector3 move = Vector3.Zero;
        float dist = 999f; Vector3 toP = Vector3.Zero;
        if (pl != null && !pl.Dead)
        {
            toP = pl.GlobalPosition - GlobalPosition; toP.Y = 0; dist = toP.Length();
        }

        switch (_state)
        {
            case State.Idle:
                if (dist < Def.Aggro) _state = State.Chase;
                break;
            case State.Chase:
                if (dist > Def.Aggro * 2.5f || pl == null || pl.Dead) { _state = State.Idle; break; }
                _yaw = Mathf.LerpAngle(_yaw, Util.YawTo(toP.X, toP.Z), 1f - Mathf.Exp(-10f * dt));
                _timer -= dt;
                if (dist > Def.Reach * 0.8f) move = toP.Normalized() * Def.Spd;
                if (dist <= Def.Reach + 0.2f && _timer <= 0) { _state = State.Wind; _timer = Def.Wind; }
                break;
            case State.Wind:
                _yaw = Mathf.LerpAngle(_yaw, Util.YawTo(toP.X, toP.Z), 1f - Mathf.Exp(-12f * dt));
                _mat.AlbedoColor = _baseColor.Lerp(new Color(1, 0.2f, 0.1f), 1f - Mathf.Clamp(_timer / Def.Wind, 0, 1));
                _body.Position = new Vector3(0, 0, 0);
                _timer -= dt;
                if (_timer <= 0)
                {
                    if (pl != null && !pl.Dead && dist - 0.4f <= Def.Reach + 0.5f)
                    {
                        pl.TakeHit(new HitInfo { Damage = Damage, From = GlobalPosition, Knock = Def.Slam ? 9f : 4f, Attacker = this });
                    }
                    _state = State.Recover; _timer = Def.Cd * 0.6f;
                }
                break;
            case State.Recover:
                _timer -= dt;
                if (_timer <= 0) { _state = State.Chase; _timer = Def.Cd * 0.4f; }
                break;
            case State.Stagger:
                _stun -= dt;
                if (_stun <= 0) { _state = State.Chase; _timer = 0.3f; }
                break;
        }

        _kb = _kb.MoveToward(Vector3.Zero, 22f * dt);
        v.X = move.X + _kb.X; v.Z = move.Z + _kb.Z;
        Velocity = v;
        MoveAndSlide();
        Rotation = new Vector3(0, _yaw, 0);
    }
}
