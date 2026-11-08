using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>Data-driven enemy. Melee, ranged, summoning bosses and the dragon. Stats come from data/enemies.json.</summary>
public partial class Enemy : CharacterBody3D, IDamageable
{
    enum State { Sleep, Idle, Chase, Wind, Recover, Stagger, Return, Breath, Dead }
    public static Enemy? CurrentBoss;

    public string Type = "wolf";
    public int Level = 1;
    public EnemyDef Def = new();
    public EnemyDef? DefOverride;
    public bool Arena, Summoned, NoLeash, AnyRealm, FromDungeon;
    public float Hp, MaxHp, Damage;
    public bool Dead => _state == State.Dead;
    public bool Active { get; private set; } = true;
    public float RespawnTime;
    public Enemy? Owner_;

    State _state = State.Idle;
    float _timer, _stun, _flash, _gravity, _yaw, _deadT, _burnT, _burnDps, _slow, _summonT, _breathT, _breathCd = 6f, _tick;
    Vector3 _kb, _home;
    Node3D _body = null!;
    readonly List<StandardMaterial3D> _mats = new();
    readonly List<Color> _baseCols = new();
    Label3D _label = null!;
    float _scale = 1f;
    bool _meleeSwing; int _summoned;

    public float ScaleF() => _scale;
    public float HeightM() => Def.Kind == "quad" ? Def.R * 2.4f : Def.Kind == "dragon" ? 5f : 1.9f * _scale;

    StandardMaterial3D Mat(Color c, float emission = 0f)
    {
        var m = new StandardMaterial3D { AlbedoColor = c, Roughness = 0.85f };
        if (emission > 0) { m.EmissionEnabled = true; m.Emission = c; m.EmissionEnergyMultiplier = emission; }
        _mats.Add(m); _baseCols.Add(c); return m;
    }

    public override void _Ready()
    {
        Def = DefOverride ?? EnemyDb.Get(Type);
        MaxHp = Hp = DefOverride != null ? Def.Hp : Scaling.EnemyHp(Def.Hp, Level);
        Damage = DefOverride != null ? Def.Dmg : Scaling.EnemyDmg(Def.Dmg, Level);
        if (Def.Reach <= 0) Def.Reach = 2.2f;
        _home = GlobalPosition;
        _gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity");
        AddToGroup("enemies");
        CollisionLayer = 2; CollisionMask = 1 | 2;
        _scale = Def.Scale <= 0 ? 1f : Def.Scale;
        float r = Def.R, s = _scale;
        bool quad = Def.Kind == "quad", dragon = Def.Kind == "dragon", wraith = Def.Kind == "wraith";
        float h = quad ? r * 2.4f : dragon ? 5f : 1.8f * s;
        AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = Mathf.Min(r, h / 2 - 0.01f), Height = h }, Position = new Vector3(0, h / 2, 0) });

        Color col = Def.Color != 0x888888 ? JsonUtil.Hex(Def.Color) : DefaultColor(Type);
        if (Realm_() == 1 && Def.Color == 0x888888) col = col.Lerp(new Color(0.5f, 0.35f, 0.75f), 0.4f);
        _body = new Node3D { Name = "Body" }; AddChild(_body);
        var main = Mat(col); var dark = Mat(col.Darkened(0.5f)); var glow = Mat(wraith ? new Color(0.7f, 0.4f, 1f) : new Color(1f, 0.3f, 0.2f), 2f);
        if (quad)
        {
            Add(new BoxMesh { Size = new Vector3(r * 1.3f, r * 1.4f, r * 3.2f) }, main, new Vector3(0, r * 1.1f, 0));
            Add(new BoxMesh { Size = new Vector3(r * 0.9f, r * 0.9f, r * 1.1f) }, main, new Vector3(0, r * 1.5f, -r * 1.9f));
            Add(new BoxMesh { Size = new Vector3(r * 0.25f, r * 0.25f, r * 0.1f) }, glow, new Vector3(r * 0.25f, r * 1.6f, -r * 2.5f)); Add(new BoxMesh { Size = new Vector3(r * 0.25f, r * 0.25f, r * 0.1f) }, glow, new Vector3(-r * 0.25f, r * 1.6f, -r * 2.5f));
        }
        else if (dragon)
        {
            Add(new CapsuleMesh { Radius = 1.6f, Height = 5.5f }, main, new Vector3(0, 2.2f, 0.4f), new Vector3(Mathf.Pi / 2, 0, 0));
            Add(new CapsuleMesh { Radius = 0.7f, Height = 3.2f }, main, new Vector3(0, 3.6f, -2.7f), new Vector3(-0.9f, 0, 0));
            Add(new BoxMesh { Size = new Vector3(1.1f, 0.9f, 1.8f) }, main, new Vector3(0, 4.7f, -3.9f));
            Add(new BoxMesh { Size = new Vector3(0.25f, 0.25f, 0.2f) }, glow, new Vector3(0.4f, 4.9f, -4.8f)); Add(new BoxMesh { Size = new Vector3(0.25f, 0.25f, 0.2f) }, glow, new Vector3(-0.4f, 4.9f, -4.8f));
            Add(new BoxMesh { Size = new Vector3(6f, 0.12f, 3f) }, dark, new Vector3(4f, 3.4f, 0.6f), new Vector3(0, 0, 0.5f)); Add(new BoxMesh { Size = new Vector3(6f, 0.12f, 3f) }, dark, new Vector3(-4f, 3.4f, 0.6f), new Vector3(0, 0, -0.5f));
            Add(new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.7f, Height = 5f }, main, new Vector3(0, 1.4f, 4.6f), new Vector3(Mathf.Pi / 2.3f, 0, 0));
        }
        else if (wraith)
        {
            var ghost = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.35f, 0.85f, 0.65f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, EmissionEnabled = true, Emission = new Color(0.4f, 0.2f, 0.8f), EmissionEnergyMultiplier = 0.8f };
            Add(new CylinderMesh { TopRadius = r * 0.7f, BottomRadius = 0.05f, Height = 2.1f }, ghost, new Vector3(0, 1.2f, 0));
            Add(new SphereMesh { Radius = r * 0.6f, Height = r * 1.2f }, ghost, new Vector3(0, 2.3f, 0));
            Add(new BoxMesh { Size = new Vector3(0.12f, 0.1f, 0.1f) }, glow, new Vector3(0.15f, 2.35f, -r * 0.5f)); Add(new BoxMesh { Size = new Vector3(0.12f, 0.1f, 0.1f) }, glow, new Vector3(-0.15f, 2.35f, -r * 0.5f));
        }
        else
        {
            Add(new CapsuleMesh { Radius = r, Height = 1.5f * s }, main, new Vector3(0, 0.85f * s, 0));
            Add(new SphereMesh { Radius = 0.22f * s, Height = 0.44f * s }, Mat(new Color(0.85f, 0.7f, 0.55f).Lerp(col, Type == "skeleton" ? 0.8f : 0.1f)), new Vector3(0, 1.7f * s, 0));
            var steel = Mat(Def.Boss ? new Color(0.65f, 0.7f, 0.8f) : new Color(0.6f, 0.6f, 0.65f));
            float wl = Type == "brute" || Def.Boss ? 1.5f * s : 0.9f * s;
            Add(new BoxMesh { Size = new Vector3(0.08f * s, 0.08f * s, wl) }, steel, new Vector3(0.45f * s, 1.1f * s, -wl / 2 - 0.1f));
            if (Def.Slam || Def.Boss) Add(new BoxMesh { Size = new Vector3(0.6f * s, 0.25f * s, 0.6f * s) }, dark, new Vector3(0.45f * s, 1.1f * s, -wl));
            Add(new BoxMesh { Size = new Vector3(0.2f * s, 0.06f * s, 0.06f * s) }, glow, new Vector3(0, 1.75f * s, -0.2f * s));
        }
        float labelY = quad ? r * 2.6f : dragon ? 6.2f : 2.2f * s;
        _label = new Label3D
        {
            Text = Summoned ? "" : (Def.N + (DefOverride != null ? "" : $"  Lv{Level}")), FontSize = 36, OutlineSize = 8, PixelSize = 0.006f, Modulate = Def.Boss ? new Color(1, 0.5f, 0.4f) : new Color(1, 0.88f, 0.7f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = new Vector3(0, labelY + 0.3f, 0), NoDepthTest = true,
        };
        AddChild(_label);
        _yaw = (float)GD.RandRange(0, Mathf.Tau);
        if (Def.Kind == "dragon") _state = State.Sleep;
        if (Arena || Summoned || FromDungeon) { _state = State.Chase; AnyRealm = true; }
        _summonT = (float)GD.RandRange(5, 9);
    }

    void Add(Mesh m, Material mat, Vector3 pos, Vector3? rot = null)
    {
        var mi = new MeshInstance3D { Mesh = m, MaterialOverride = mat, Position = pos };
        if (rot.HasValue) mi.Rotation = rot.Value;
        _body.AddChild(mi);
    }

    int Realm_() => Def.Realm;
    static Color DefaultColor(string type) => type switch
    {
        "bandit" => new Color(0.45f, 0.3f, 0.2f), "archer" => new Color(0.3f, 0.4f, 0.25f), "goblin" => new Color(0.35f, 0.6f, 0.25f), "brute" => new Color(0.6f, 0.45f, 0.3f),
        "skeleton" => new Color(0.85f, 0.82f, 0.7f), "wraith" => new Color(0.5f, 0.35f, 0.7f), "revenant" => new Color(0.3f, 0.3f, 0.4f), "barrowKing" => new Color(0.5f, 0.55f, 0.6f),
        "warchief" => new Color(0.3f, 0.5f, 0.2f), "golem" => new Color(0.25f, 0.2f, 0.2f), "dragon" => new Color(0.45f, 0.15f, 0.12f), "clone" => new Color(0.4f, 0.2f, 0.7f),
        _ => new Color(0.5f, 0.5f, 0.5f),
    };

    // ------------------------------------------------------------------ public API
    public void Stun(float t) { if (Dead) return; _stun = Mathf.Max(_stun, t); if (_state != State.Sleep) _state = State.Stagger; }
    public void Ignite(float t, float dps) { _burnT = t; _burnDps = Mathf.Max(_burnDps, dps); }
    public void Slow(float t) { _slow = t; }
    public void Wake() { if (_state == State.Sleep || _state == State.Idle || _state == State.Return) { _state = State.Chase; if (_state == State.Chase && Def.Kind == "dragon") Sfx.Play("roar"); } }

    public void TakeHit(HitInfo h)
    {
        if (Dead) return;
        int dmg = Mathf.Max(1, Mathf.RoundToInt(h.Damage));
        Hp -= dmg; _flash = 0.12f;
        if (h.IsPlayer) { var D = Game.D; var p = Player.Instance; if (p != null) { p.Hp = Mathf.Min(D.MaxHp(), p.Hp + dmg * Game.TS["leech"] / 100f); p.Mp = Mathf.Min(D.MaxMp(), p.Mp + dmg * Game.TS["mleech"] / 100f); } }
        FloatText.Spawn(this, GlobalPosition + new Vector3(0, HeightM() + 0.3f, 0), h.Crit ? dmg + "!" : dmg.ToString(), h.Crit ? new Color(1, 0.82f, 0.3f) : h.Fire ? new Color(1, 0.7f, 0.4f) : Colors.White);
        var away = GlobalPosition - h.From; away.Y = 0;
        if (away.LengthSquared() > 0.001f && !Def.Boss) _kb += away.Normalized() * h.Knock * 1.6f;
        if (h.Stun > 0 && !Def.Boss && Def.Kind != "dragon") Stun(h.Stun * 1f);
        else if (_state == State.Idle || _state == State.Sleep || _state == State.Return) Wake();
        Fx.Burst(GlobalPosition + new Vector3(0, HeightM() * 0.6f, 0), h.Crit ? new Color(1, 0.8f, 0.2f) : new Color(1, 0.3f, 0.2f), h.Crit ? 12 : 6, 4f, 0.35f, 0.08f);
        if (Hp <= 0) Die(h);
    }

    void Die(HitInfo? h)
    {
        _state = State.Dead; _deadT = 0; CollisionLayer = 0; CollisionMask = 1; _label.Visible = false;
        if (CurrentBoss == this) CurrentBoss = null;
        var D = Game.D; var p = Player.Instance;
        D.Kills++;
        float lvlF = Level < D.Level - 4 ? 0.5f : 1f;
        int xp = Mathf.RoundToInt(Def.Xp * (1 + 0.12f * (Level - 1)) * lvlF * (Summoned ? 0.3f : 1f));
        if (DefOverride != null) xp = 0;
        if (xp > 0 && p != null) p.AddXp(xp, GlobalPosition);
        Fx.Burst(GlobalPosition + Vector3.Up, Def.Kind == "wraith" ? new Color(0.7f, 0.4f, 1f) : new Color(0.8f, 0.2f, 0.2f), 16, 5f, 0.6f);
        if (!Summoned && DefOverride == null)
        {
            int gold = GD.RandRange(2 + Level, 6 + Level * 2) * (Def.Boss ? 12 : 1);
            float chance = Def.Boss ? 1f : Arena ? .12f : .2f + (Def.Realm == 1 ? .06f : 0f), bonus = (Def.Boss ? 2 : 0) + (Def.Realm == 1 ? 1 : 0);
            Loot.DropFrom(GlobalPosition, Level, chance, bonus, gold, Def.Boss ? .6f : .07f);
            if (Def.Boss) { Loot.DropFrom(GlobalPosition + new Vector3(1, 0, 1), Level, 1f, 3f, 0, 0f); Loot.DropFrom(GlobalPosition + new Vector3(-1, 0, -1), Level, .6f, 2f, 0, 0f); }
        }
        if (Arena) Arenas.OnEnemyDown(this);
        else Quests.OnKill(Type, Def.Realm, Def.Boss);
        if (Def.Boss) { Ui.Banner(Def.N, "has fallen"); Sfx.Play("level"); if (FromDungeon) Dungeon.OnBossDown(this); }
        if (!Arena && !Summoned && !FromDungeon && RespawnTime <= 0) RespawnTime = Def.Boss ? 600f : 260f;
        Game.Save();
    }

    void Respawn()
    {
        Hp = MaxHp; GlobalPosition = _home; _state = Def.Kind == "dragon" ? State.Sleep : State.Idle; _deadT = 0; _stun = 0; _burnT = 0;
        CollisionLayer = 2; CollisionMask = 1 | 2; _label.Visible = true; _body.Rotation = Vector3.Zero; Velocity = Vector3.Zero;
    }

    // ------------------------------------------------------------------ AI
    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        bool act = AnyRealm || Def.Realm == World.Realm;
        if (act != Active) { Active = act; Visible = act; if (!Dead) { CollisionLayer = act ? 2u : 0u; } }
        if (!Active) return;
        var pl = Player.Instance;
        float distP = pl == null ? 999f : (pl.GlobalPosition - GlobalPosition).Length();
        if (distP > 150f && !Arena && !Dead) { Visible = false; return; }
        if (!Visible && Active) Visible = true;

        var v = Velocity;
        if (!IsOnFloor()) v.Y -= _gravity * dt; else v.Y = 0;

        if (_flash > 0) { _flash -= dt; foreach (var m in _mats) m.AlbedoColor = Colors.White; }
        else for (int i = 0; i < _mats.Count; i++) _mats[i].AlbedoColor = _state == State.Wind ? _baseCols[i].Lerp(new Color(1, 0.2f, 0.1f), 1f - Mathf.Clamp(_timer / Mathf.Max(0.01f, Def.Wind), 0, 1)) : _baseCols[i];

        if (_state == State.Dead)
        {
            _deadT += dt;
            _body.Rotation = new Vector3(Mathf.Lerp(0, Mathf.Pi / 2, Mathf.Clamp(_deadT / 0.5f, 0, 1)), 0, 0);
            if (_deadT > 8f)
            {
                if (RespawnTime > 0) { Visible = false; if (_deadT > RespawnTime && distP > 95f) Respawn(); }
                else QueueFree();
            }
            Velocity = new Vector3(0, v.Y, 0); MoveAndSlide();
            return;
        }

        // damage over time / slow
        if (_burnT > 0) { _burnT -= dt; _tick += dt; if (_tick >= 0.5f) { _tick = 0; Hp -= _burnDps * 0.5f; FloatText.Spawn(this, GlobalPosition + new Vector3(0, HeightM() + 0.2f, 0), Mathf.Max(1, Mathf.RoundToInt(_burnDps * 0.5f)).ToString(), new Color(1, 0.5f, 0.2f), 0.7f); if (Hp <= 0) { Die(null); return; } } if (_burnT <= 0) _burnDps = 0; }
        float spdMul = _slow > 0 ? 0.5f : 1f; if (_slow > 0) _slow -= dt;

        Vector3 move = Vector3.Zero, toP = Vector3.Zero; float dist = 999f; bool plOk = pl != null && !pl.Dead;
        if (plOk) { toP = pl!.GlobalPosition - GlobalPosition; toP.Y = 0; dist = toP.Length(); }
        float yawToP = plOk ? Util.YawTo(toP.X, toP.Z) : _yaw;
        bool ranged = Def.Ranged != null;
        float range = Def.Range > 0 ? Def.Range : 20f, keep = Def.Keep > 0 ? Def.Keep : 10f;

        switch (_state)
        {
            case State.Sleep:
                if (dist < 30f) Wake();
                break;
            case State.Idle:
                if (dist < Def.Aggro && plOk) { Wake(); }
                break;
            case State.Chase:
                if (!plOk || (!NoLeash && !Arena && !Summoned && !FromDungeon && (dist > Def.Aggro * 2.5f || (GlobalPosition - _home).Length() > 90f))) { _state = State.Return; break; }
                if (Def.Boss && CurrentBoss != this) CurrentBoss = this;
                _yaw = Mathf.LerpAngle(_yaw, yawToP, 1f - Mathf.Exp(-10f * dt));
                _timer -= dt;
                if (Def.Summon != null) { _summonT -= dt; if (_summonT <= 0 && _summoned < 4) { _summonT = 14f; SummonAdds(); } }
                if (Def.Breath) { _breathCd -= dt; if (_breathCd <= 0 && dist < 24f) { _state = State.Breath; _breathT = 0; _breathCd = 9f; Sfx.Play("roar"); break; } }
                if (ranged)
                {
                    if (dist > range * 0.9f) move = toP.Normalized() * Def.Spd;
                    else if (dist < keep) move = -toP.Normalized() * Def.Spd * 0.8f;
                    else { var side = new Vector3(-toP.Z, 0, toP.X).Normalized() * ((GetInstanceId() % 2 == 0) ? 1 : -1); move = side * Def.Spd * 0.4f; }
                    if (dist <= range && _timer <= 0) { _state = State.Wind; _timer = Def.Wind; }
                }
                else
                {
                    if (dist > Def.Reach * 0.8f) move = toP.Normalized() * Def.Spd;
                    if (dist <= Def.Reach + 0.2f && _timer <= 0) { _state = State.Wind; _timer = Def.Wind; }
                }
                break;
            case State.Wind:
                _yaw = Mathf.LerpAngle(_yaw, yawToP, 1f - Mathf.Exp(-12f * dt));
                _timer -= dt;
                if (_timer <= 0)
                {
                    if (plOk)
                    {
                        if (ranged)
                        {
                            var from = GlobalPosition + new Vector3(0, 1.4f * _scale, 0); var to = pl!.GlobalPosition + new Vector3(0, 1.1f, 0);
                            var dir = (to - from).Normalized();
                            Projectile.Spawn(Def.Ranged == "arrow" ? "arrow" : "ebolt", 'e', from, dir * (Def.Ranged == "arrow" ? 26f : 18f), Damage, 3f);
                            Sfx.Play(Def.Ranged == "arrow" ? "swing" : "zap", 0.6f);
                        }
                        else if (dist - 0.4f <= Def.Reach + 0.5f)
                        {
                            if (Def.Slam) { Fx.Ring(GlobalPosition + Vector3.Up * 0.1f, Def.Reach + 0.8f, new Color(1, 0.5f, 0.2f)); Player.Instance?.Rig?.Shake(0.2f); }
                            pl!.TakeHit(new HitInfo { Damage = Damage, From = GlobalPosition, Knock = Def.Slam ? 9f : 4f, Attacker = this });
                        }
                    }
                    _state = State.Recover; _timer = Def.Cd * 0.6f;
                }
                break;
            case State.Recover:
                _timer -= dt; if (_timer <= 0) { _state = State.Chase; _timer = Def.Cd * 0.4f; }
                break;
            case State.Stagger:
                _stun -= dt; if (_stun <= 0) { _state = State.Chase; _timer = 0.3f; }
                break;
            case State.Return:
            {
                var back = _home - GlobalPosition; back.Y = 0;
                if (back.Length() < 2f) { _state = Def.Kind == "dragon" ? State.Sleep : State.Idle; Hp = MaxHp; break; }
                Hp = Mathf.Min(MaxHp, Hp + MaxHp * 0.1f * dt);
                _yaw = Mathf.LerpAngle(_yaw, Util.YawTo(back.X, back.Z), 1f - Mathf.Exp(-8f * dt));
                move = back.Normalized() * Def.Spd * 1.2f;
                if (plOk && dist < Def.Aggro * 0.6f) _state = State.Chase;
                break;
            }
            case State.Breath:
            {
                _yaw = Mathf.LerpAngle(_yaw, yawToP, 1f - Mathf.Exp(-3f * dt));
                _breathT += dt;
                if (_breathT < 1.0f) { foreach (var m in _mats) m.EmissionEnabled = false; break; }
                if (_breathT < 2.8f)
                {
                    _tick += dt;
                    var fwd = new Vector3(-Mathf.Sin(_yaw), 0, -Mathf.Cos(_yaw));
                    var mouth = GlobalPosition + fwd * 4f + Vector3.Up * 4.4f;
                    for (int i = 0; i < 2; i++) Fx.Burst(mouth + fwd * (float)GD.RandRange(0, 14), new Color(1f, 0.5f + GD.Randf() * 0.3f, 0.1f), 2, 3f, 0.5f, 0.35f, 1f);
                    if (_tick >= 0.25f)
                    {
                        _tick = 0;
                        if (plOk && dist < 22f && Mathf.Abs(Util.AngDiff(_yaw, yawToP)) < 0.55f)
                            pl!.TakeHit(new HitInfo { Damage = Damage * 0.35f, From = GlobalPosition, Knock = 1f, NoBlock = true, IsProjectile = true });
                    }
                    break;
                }
                _state = State.Recover; _timer = 1.2f;
                break;
            }
        }

        _kb = _kb.MoveToward(Vector3.Zero, 22f * dt);
        v.X = move.X * spdMul + _kb.X; v.Z = move.Z * spdMul + _kb.Z;
        Velocity = v;
        MoveAndSlide();
        Rotation = new Vector3(0, _yaw, 0);
    }

    void SummonAdds()
    {
        if (Def.Summon == null) return;
        for (int i = 0; i < 2; i++)
        {
            var a = i == 0 ? 1f : -1f;
            var pos = GlobalPosition + new Vector3(a * 3f, 0.3f, 2f);
            var e = new Enemy { Type = Def.Summon, Level = Mathf.Max(1, Level - 1), Summoned = true, FromDungeon = FromDungeon, Position = pos, Owner_ = this };
            GetParent().AddChild(e); _summoned++;
            Fx.Burst(pos + Vector3.Up, new Color(0.6f, 0.3f, 0.9f), 14, 4f);
        }
        Ui.Note(Def.N + " calls for aid!", new Color(1, 0.6f, 0.4f));
    }
}
