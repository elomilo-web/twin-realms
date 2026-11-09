using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>Data-driven enemy. Melee, ranged, summoning bosses and the dragon. Stats come from data/enemies.json.</summary>
public partial class Enemy : CharacterBody3D, IDamageable
{
    enum State { Sleep, Idle, Chase, Wind, Recover, Stagger, Return, Breath, Dead, Flee }
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
    Vector3 _kb, _home, _recoilDir = Vector3.Back; float _recoilT, _recoilK;
    readonly List<Node3D> _legs = new(); float _legPh, _grazeT; Vector3 _grazeTo;
    Node3D _body = null!;
    readonly List<StandardMaterial3D> _mats = new();
    readonly List<Color> _baseCols = new();
    Label3D _label = null!;
    float _scale = 1f;
    bool _meleeSwing; int _summoned;

    public float ScaleF() => _scale;
    public float HeightM() => Def.Kind == "quad" ? Def.R * (2.4f + 0.75f * (LegK() - 1f)) : Def.Kind == "dragon" ? 5f : 1.9f * _scale;
    float LegK() => Def.Look == "deer" ? 1.8f : Def.Look == "rabbit" ? 0.55f : Def.Look == "boar" ? 0.75f : 1f; // leg length relative to a wolf

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
        float h = quad ? HeightM() : dragon ? 5f : 1.8f * s;
        AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = Mathf.Min(r, h / 2 - 0.01f), Height = h }, Position = new Vector3(0, h / 2, 0) });

        Color col = Def.Color != 0x888888 ? JsonUtil.Hex(Def.Color) : DefaultColor(Type);
        if (Realm_() == 1 && Def.Color == 0x888888) col = col.Lerp(new Color(0.5f, 0.35f, 0.75f), 0.4f);
        _body = new Node3D { Name = "Body" }; AddChild(_body);
        var main = Mat(col); var dark = Mat(col.Darkened(0.5f)); var glow = Mat(wraith || Def.Emissive ? new Color(0.7f, 0.4f, 1f) : new Color(1f, 0.3f, 0.2f), 2f);
        if (quad) BuildQuad(r, main, dark, glow, col);
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
        BakeParts();
        float labelY = quad ? r * 2.6f : dragon ? 6.2f : 2.2f * s;
        _label = new Label3D
        {
            Text = Summoned ? "" : (Def.N + (DefOverride != null ? "" : $"  Lv{Level}")), FontSize = 36, OutlineSize = 8, PixelSize = 0.006f, Modulate = Def.Boss ? new Color(1, 0.5f, 0.4f) : new Color(1, 0.88f, 0.7f),
            // depth-tested (no names through walls or hills) and only within a fair distance, fading out
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = new Vector3(0, labelY + 0.3f, 0), NoDepthTest = false,
            VisibilityRangeEnd = Def.Boss || dragon ? 90f : 26f, VisibilityRangeEndMargin = 5f, VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
        };
        AddChild(_label);
        _yaw = (float)GD.RandRange(0, Mathf.Tau);
        if (Def.Kind == "dragon") _state = State.Sleep;
        if (Arena || Summoned || FromDungeon) { _state = State.Chase; AnyRealm = true; }
        _summonT = (float)GD.RandRange(5, 9);
    }

    // body parts are collected and merged per (parent, material) after building: one draw call per material instead of one per box.
    // Flash and wind-up tint still work because they change the shared materials.
    readonly List<(Node3D parent, Mesh m, Material mat, Transform3D t)> _parts = new();
    void Add(Mesh m, Material mat, Vector3 pos, Vector3? rot = null, Node3D? parent = null)
        => _parts.Add((parent ?? _body, m, mat, new Transform3D(Basis.FromEuler(rot ?? Vector3.Zero), pos)));
    void BakeParts()
    {
        var groups = new Dictionary<(Node3D, Material), SurfaceTool>();
        foreach (var (p, m, mat, t) in _parts) { if (!groups.TryGetValue((p, mat), out var st)) groups[(p, mat)] = st = new SurfaceTool(); st.AppendFrom(m, 0, t); }
        foreach (var kv in groups) kv.Key.Item1.AddChild(new MeshInstance3D { Mesh = kv.Value.Commit(), MaterialOverride = kv.Key.Item2 });
        _parts.Clear();
    }

    /// <summary>Four-legged body: torso, head, eyes, four swinging legs, plus deer antlers, rabbit ears or boar tusks.</summary>
    void BuildQuad(float r, StandardMaterial3D main, StandardMaterial3D dark, StandardMaterial3D glow, Color col)
    {
        string look = Def.Look; bool wild = look != "";
        float legK = LegK();
        float hipY = r * 0.75f * legK, dy = hipY - r * 0.75f;                                       // lift the body to sit on the legs
        if (Def.Emissive) { main.EmissionEnabled = true; main.Emission = col; main.EmissionEnergyMultiplier = 0.6f; }
        var eye = wild && !Def.Emissive ? Mat(new Color(0.05f, 0.04f, 0.04f)) : glow;
        float bodyL = look == "rabbit" ? 2.2f : look == "boar" ? 2.8f : 3.2f, bodyH = look == "boar" ? 1.7f : 1.4f;
        Add(new BoxMesh { Size = new Vector3(r * 1.3f, r * bodyH, r * bodyL) }, main, new Vector3(0, r * 1.1f + dy, 0));
        float headY = r * 1.5f + dy + (look == "deer" ? r * 0.9f : 0), headZ = -r * (bodyL * 0.5f + 0.3f);
        if (look == "deer") Add(new BoxMesh { Size = new Vector3(r * 0.5f, r * 1.4f, r * 0.5f) }, main, new Vector3(0, r * 1.7f + dy, headZ + r * 0.35f), new Vector3(-0.45f, 0, 0)); // neck, leaning forward
        Add(new BoxMesh { Size = new Vector3(r * 0.9f, r * 0.9f, r * 1.1f) }, main, new Vector3(0, headY, headZ));
        Add(new BoxMesh { Size = new Vector3(r * 0.25f, r * 0.25f, r * 0.1f) }, eye, new Vector3(r * 0.3f, headY + r * 0.1f, headZ - r * 0.56f)); Add(new BoxMesh { Size = new Vector3(r * 0.25f, r * 0.25f, r * 0.1f) }, eye, new Vector3(-r * 0.3f, headY + r * 0.1f, headZ - r * 0.56f));
        // legs on hip pivots (front-left/back-right swing together)
        float lw = r * (look == "deer" ? 0.2f : 0.3f), ll = hipY;
        foreach (var (x, z) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
        {
            var hip = new Node3D { Position = new Vector3(x * r * 0.42f, hipY, z * r * bodyL * 0.36f) }; _body.AddChild(hip); _legs.Add(hip);
            Add(new BoxMesh { Size = new Vector3(lw, ll, lw) }, dark, new Vector3(0, -ll / 2, 0), null, hip);
        }
        var light = Mat(col.Lightened(0.55f));
        switch (look)
        {
            case "deer":
                foreach (float s in new[] { -1f, 1f })
                {
                    var bone = Def.Emissive ? glow : Mat(new Color(0.85f, 0.78f, 0.62f));
                    Add(new BoxMesh { Size = new Vector3(r * .08f, r * 1.1f, r * .08f) }, bone, new Vector3(s * r * .28f, headY + r * .95f, headZ + r * .1f), new Vector3(0.2f, 0, s * 0.35f));
                    Add(new BoxMesh { Size = new Vector3(r * .07f, r * .6f, r * .07f) }, bone, new Vector3(s * r * .5f, headY + r * 1.15f, headZ - r * .1f), new Vector3(-0.5f, 0, s * 0.9f));
                    Add(new BoxMesh { Size = new Vector3(r * .07f, r * .55f, r * .07f) }, bone, new Vector3(s * r * .35f, headY + r * 1.45f, headZ + r * .25f), new Vector3(0.6f, 0, s * 0.2f));
                }
                Add(new BoxMesh { Size = new Vector3(r * .3f, r * .35f, r * .2f) }, light, new Vector3(0, r * 1.45f + dy, r * bodyL * .5f));   // white tail
                break;
            case "rabbit":
                foreach (float s in new[] { -1f, 1f }) Add(new BoxMesh { Size = new Vector3(r * .25f, r * 1.3f, r * .12f) }, main, new Vector3(s * r * .22f, headY + r * 1.0f, headZ + r * .2f), new Vector3(0.25f, 0, s * 0.15f));
                Add(new SphereMesh { Radius = r * .3f, Height = r * .6f, RadialSegments = 6, Rings = 3 }, light, new Vector3(0, r * 1.3f + dy, r * bodyL * .5f));
                break;
            case "boar":
                var tusk = Mat(new Color(0.95f, 0.92f, 0.82f));
                foreach (float s in new[] { -1f, 1f }) Add(new CylinderMesh { TopRadius = 0, BottomRadius = r * .07f, Height = r * .55f, RadialSegments = 5 }, tusk, new Vector3(s * r * .35f, headY - r * .15f, headZ - r * .55f), new Vector3(-0.5f, 0, s * 0.4f));
                Add(new BoxMesh { Size = new Vector3(r * .3f, r * .35f, r * bodyL * .8f) }, dark, new Vector3(0, r * 1.1f + dy + r * bodyH * .5f, 0));  // bristle ridge
                Add(new BoxMesh { Size = new Vector3(r * .5f, r * .4f, r * .2f) }, dark, new Vector3(0, headY - r * .15f, headZ - r * .6f));            // snout
                break;
        }
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
    public void Wake() { if (Def.Flee && !Dead) { _state = State.Flee; return; } if (_state == State.Sleep || _state == State.Idle || _state == State.Return) { _state = State.Chase; if (_state == State.Chase && Def.Kind == "dragon") Sfx.Play("roar"); } }

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
        // recoil: the body jolts away from the hit and squashes, then springs back (local space, since the enemy node is yawed)
        Feel.Load();
        if (away.LengthSquared() > 0.001f) _recoilDir = Basis.Inverse() * away.Normalized();
        _recoilK = Mathf.Clamp(h.Knock / 5f, 0.4f, 1.5f) * (Def.Boss || Def.Kind == "dragon" ? Feel.RecoilBossMul : 1f) * (h.Crit ? 1.3f : 1f); _recoilT = Feel.RecoilTime;
        if (h.IsPlayer && h.Attacker is Player) Feel.HitStop(Feel.ForHit(h, Hp <= 0));
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
        if (Def.Drops != null && !Summoned)
            foreach (var kv in Def.Drops) { int lo = kv.Value.Length > 0 ? kv.Value[0] : 1, hi = kv.Value.Length > 1 ? kv.Value[1] : lo; GatherDb.Give(kv.Key, GD.RandRange(lo, hi), GlobalPosition); }
        if (!Summoned && DefOverride == null && !Def.NoLoot)
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
        if (distP > (Def.Look != "" ? 100f : 150f) && !Arena && !Dead) { Visible = false; return; } // small wildlife is not worth drawing far away
        if (!Visible && Active) Visible = true;

        var v = Velocity;
        if (!IsOnFloor()) v.Y -= _gravity * dt; else v.Y = 0;

        if (_recoilT > 0 || _body.Position != Vector3.Zero)
        {
            _recoilT = Mathf.Max(0, _recoilT - dt); float f = _recoilT / Mathf.Max(0.01f, Feel.RecoilTime); f *= f; // snap out, ease back
            float k = f * _recoilK; _body.Position = _recoilDir * Feel.RecoilPush * k;
            _body.Scale = new Vector3(1 + Feel.RecoilSquash * k * 0.5f, 1 - Feel.RecoilSquash * k, 1 + Feel.RecoilSquash * k * 0.5f);
        }
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
                if (dist < Def.Aggro && plOk) { Wake(); break; }
                if (Def.Look != "")
                {
                    // wildlife grazes: amble to a nearby spot, pause, pick another
                    _grazeT -= dt;
                    if (_grazeT <= 0) { _grazeT = (float)GD.RandRange(3.0, 7.0); float a = (float)GD.RandRange(0, Mathf.Tau), d = (float)GD.RandRange(2.0, 8.0); _grazeTo = _home + new Vector3(Mathf.Cos(a) * d, 0, Mathf.Sin(a) * d); }
                    var g = _grazeTo - GlobalPosition; g.Y = 0;
                    if (g.Length() > 0.6f && _grazeT < 4.5f) { move = g.Normalized() * Def.Spd * 0.18f; _yaw = Mathf.LerpAngle(_yaw, Util.YawTo(g.X, g.Z), 1f - Mathf.Exp(-4f * dt)); }
                }
                break;
            case State.Flee:
            {
                if (!plOk || dist > Def.Aggro * 2.4f) { _state = State.Return; break; }
                var away = -toP; if ((GlobalPosition - _home).Length() > 70f) away = away.Normalized() + (_home - GlobalPosition).Normalized() * 0.8f; // do not run off forever
                away.Y = 0; if (away.LengthSquared() < 0.01f) away = Vector3.Forward;
                _yaw = Mathf.LerpAngle(_yaw, Util.YawTo(away.X, away.Z), 1f - Mathf.Exp(-9f * dt));
                move = new Vector3(-Mathf.Sin(_yaw), 0, -Mathf.Cos(_yaw)) * Def.Spd;
                break;
            }
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
                _stun -= dt; if (_stun <= 0) { _state = Def.Flee ? State.Flee : State.Chase; _timer = 0.3f; }
                break;
            case State.Return:
            {
                var back = _home - GlobalPosition; back.Y = 0;
                if (back.Length() < 2f) { _state = Def.Kind == "dragon" ? State.Sleep : State.Idle; Hp = MaxHp; break; }
                Hp = Mathf.Min(MaxHp, Hp + MaxHp * 0.1f * dt);
                _yaw = Mathf.LerpAngle(_yaw, Util.YawTo(back.X, back.Z), 1f - Mathf.Exp(-8f * dt));
                move = back.Normalized() * Def.Spd * 1.2f;
                if (plOk && dist < Def.Aggro * 0.6f) _state = Def.Flee ? State.Flee : State.Chase;
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
        if (_legs.Count > 0)
        {
            // gait: diagonal pairs swing together, stride speed follows ground speed
            float hs = new Vector2(Velocity.X, Velocity.Z).Length(), amt = Mathf.Clamp(hs / Mathf.Max(1f, Def.Spd * 0.6f), 0, 1);
            _legPh = Mathf.Wrap(_legPh + hs * dt * 2.2f / Mathf.Max(0.25f, Def.R), 0, Mathf.Tau);
            for (int i = 0; i < _legs.Count; i++) { float ph = _legPh + ((i == 0 || i == 3) ? 0 : Mathf.Pi); _legs[i].Rotation = new Vector3(Mathf.Sin(ph) * 0.65f * amt, 0, 0); }
        }
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
