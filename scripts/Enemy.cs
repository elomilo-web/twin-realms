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
    public List<MobMod>? Mods;                 // dungeon gear tier and upgrades (data/progression.json); applied to a private copy of Def
    public float TierHp = 1f, TierDmg = 1f;    // dungeon tier multipliers (every 5 floors)
    public bool Ambusher;                      // spawned by Threat around our own hero: never networked, hunts only us
    int _areaLvl = -1;                         // overworld: the level the area gave this monster (it follows the heroes from there)
    float _xpMul = 1f, _lootBonus; OmniLight3D? _aura;
    bool Overworld => !Arena && !FromDungeon && !Summoned && DefOverride == null;

    /// <summary>
    /// Health and damage for the current level (data/progression.json "scaling"): the level curve, a floor tied to the hero's own power
    /// (so gear never makes monsters trivial), then the multiplayer, play-time threat and dungeon tier multipliers.
    /// Overworld monsters also re-pick their level from the heroes' level. keepRatio keeps a wounded monster's health share.
    /// </summary>
    public void Rescale(bool keepRatio)
    {
        if (DefOverride != null) return;
        if (Overworld) { if (_areaLvl < 0) _areaLvl = Level; Level = Progression.OverworldLevel(_areaLvl, Def.Boss); }
        var D = Game.D; float hp = Scaling.EnemyHp(Def.Hp, Level), dmg = Scaling.EnemyDmg(Def.Dmg, Level);
        if (Player.Instance != null)
        {
            hp = Mathf.Max(hp, Progression.PowerHp(Def.Hp, D.WpnDmg()));
            dmg = Mathf.Max(dmg, Progression.PowerDmg(Def.Dmg, D.MaxHp(), Progression.ArmorTaken(D.Defense(), Level)));
        }
        float party = Progression.PartyMul(), th = Overworld || Ambusher ? Threat.Level : 0;
        float hpMul = party * TierHp * (1 + th * Threat.HpP / 100f), dmgMul = party * TierDmg * (1 + th * Threat.DmgP / 100f);
        if (Summoned) { hpMul *= .6f; dmgMul *= .8f; }
        float ratio = keepRatio && MaxHp > 0 ? Mathf.Clamp(Hp / MaxHp, 0, 1) : 1;
        MaxHp = Mathf.Max(1, hp * hpMul); Damage = dmg * dmgMul; Hp = MaxHp * ratio;
        if (_label != null && IsInstanceValid(_label) && !Summoned) _label.Text = UpgradeLine() + Def.N + $"  Lv{Level}";
    }
    public float Hp, MaxHp, Damage;
    public bool Dead => _state == State.Dead;
    public bool Active { get; private set; } = true;
    public float RespawnTime;
    public Enemy? Owner_;
    // ---- multiplayer: NetIdx = index in World.NetEnemies (overworld enemies, same order on every machine).
    // A puppet mirrors the host's copy and forwards hits to it; a proxy is the invisible stand-in for a duel opponent.
    public int NetIdx = -1; public bool Puppet, Proxy; public long DuelPeer;
    long _lastPeer = -1; Vector3 _netPos; float _netYaw; bool _netHas; float _heroT; Node3D? _hero;

    State _state = State.Idle;
    float _timer, _stun, _flash, _gravity, _yaw, _deadT, _summonT, _breathT, _breathCd = 6f, _tick;
    Vector3 _kb, _home, _recoilDir = Vector3.Back; float _recoilT, _recoilK;
    readonly List<Node3D> _legs = new(); float _legPh, _grazeT; Vector3 _grazeTo;
    Node3D _body = null!;
    readonly List<StandardMaterial3D> _mats = new();
    readonly List<Color> _baseCols = new();
    Label3D _label = null!;
    float _scale = 1f, _launch;
    bool _meleeSwing; int _summoned;

    public float ScaleF() => _scale;
    /// <summary>Upgrades rolled in a dungeon (gear tiers have depth > 0 and are not counted).</summary>
    public int UpgradeCount() { int n = 0; if (Mods != null) foreach (var m in Mods) if (m.Id != "") n++; return n; }
    /// <summary>"Frenzied, Venomous" plus the gear tier, shown above the name.</summary>
    public string UpgradeLine()
    {
        if (Mods == null || Mods.Count == 0) return "";
        var up = new List<string>(); string gear = "";
        foreach (var m in Mods) if (m.Id != "") up.Add(m.Name); else gear = m.Name;
        return (up.Count > 0 ? string.Join(", ", up) : "") + (gear != "" ? (up.Count > 0 ? "  [" + gear + "]" : "[" + gear + "]") : "") + "\n";
    }
    public float HeightM() => Def.Model != "" ? (Def.H > 0 ? Def.H : 1.6f) * _scale + Def.Lift : Def.Kind == "quad" ? Def.R * (2.4f + 0.75f * (LegK() - 1f)) : Def.Kind == "dragon" ? 5f : 1.9f * _scale;
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
        if (DefOverride == null && Mods != null && Mods.Count > 0) Def = Progression.Apply(Def, Mods, out _xpMul, out _lootBonus);
        if (DefOverride != null) { MaxHp = Hp = Def.Hp; Damage = Def.Dmg; }
        else Rescale(false);
        if (Def.Reach <= 0) Def.Reach = 2.2f;
        _home = GlobalPosition;
        _gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity");
        AddToGroup("enemies");
        CollisionLayer = 2; CollisionMask = 1 | 2;
        _scale = Def.Scale <= 0 ? 1f : Def.Scale;
        float r = Def.R, s = _scale;
        bool quad = Def.Kind == "quad", dragon = Def.Kind == "dragon", wraith = Def.Kind == "wraith";
        float h = Def.Model != "" ? Mathf.Max(.6f, (Def.H > 0 ? Def.H : 1.6f) * s) : quad ? HeightM() : dragon ? 5f : 1.8f * s;
        AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = Mathf.Min(r, h / 2 - 0.01f), Height = h }, Position = new Vector3(0, h / 2, 0) });

        Color col = Def.Color != 0x888888 ? JsonUtil.Hex(Def.Color) : DefaultColor(Type);
        if (Realm_() == 1 && Def.Color == 0x888888) col = col.Lerp(new Color(0.5f, 0.35f, 0.75f), 0.4f);
        _body = new Node3D { Name = "Body" }; AddChild(_body);
        var main = Mat(col); var dark = Mat(col.Darkened(0.5f)); var glow = Mat(wraith || Def.Emissive ? new Color(0.7f, 0.4f, 1f) : new Color(1f, 0.3f, 0.2f), 2f);
        if (Proxy) { }   // a duel stand-in has no body of its own: the remote hero is drawn instead
        else if (Duelist && Avatar.Available) BuildDuelist();   // the Shadow Duel boss wears the hero (EnemyDuelist.cs)
        else if (Scarletta && Avatar.Available) BuildScarletta();   // the raid boss of the Rotting Hollow (EnemyScarletta.cs)
        else if (Def.Model != "" && BuildModel(col)) { }   // an animated monster model (data/enemies.json "model")
        else if (quad) BuildQuad(r, main, dark, glow, col);
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
        else if (Avatar.Available && Avatar.Foe(Type) is { } fc) BuildAvatar(fc, s);
        else BuildHumanoid(r, s, col, main, dark, glow);
        BakeParts();
        float labelY = _mdl != null ? HeightM() + .1f : quad ? r * 2.6f : dragon ? 6.2f : 2.2f * s;
        _label = new Label3D
        {
            Text = Summoned ? "" : (UpgradeLine() + Def.N + (DefOverride != null ? "" : $"  Lv{Level}")), FontSize = 36, OutlineSize = 8, PixelSize = 0.006f,
            // like loot rarity: upgraded dungeon monsters are blue (one upgrade) or gold (two or more)
            Modulate = Def.Boss ? new Color(1, 0.5f, 0.4f) : UpgradeCount() >= 2 ? new Color(1f, .82f, .3f) : UpgradeCount() == 1 ? new Color(.55f, .75f, 1f) : new Color(1, 0.88f, 0.7f),
            // depth-tested (no names through walls or hills) and only within a fair distance, fading out
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = new Vector3(0, labelY + 0.3f, 0), NoDepthTest = false,
            VisibilityRangeEnd = Def.Boss || dragon ? 90f : 26f, VisibilityRangeEndMargin = 5f, VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
        };
        AddChild(_label); if (Proxy) _label.Visible = false;
        // upgraded dungeon monsters glow in their rarity colour, so they read in dark halls and you can pick your fights
        if (FromDungeon && UpgradeCount() > 0 && !Def.Boss)
        {
            _aura = new OmniLight3D { LightColor = _label.Modulate, OmniRange = 3.4f, LightEnergy = 1.1f + .25f * UpgradeCount(), ShadowEnabled = false, Position = new Vector3(0, Mathf.Min(1.2f, HeightM() * .5f), 0), DistanceFadeEnabled = true, DistanceFadeBegin = 40, DistanceFadeLength = 10 };
            AddChild(_aura);
        }
        _yaw = (float)GD.RandRange(0, Mathf.Tau);
        if (Def.Kind == "dragon") _state = State.Sleep;
        // arena waves, summoned adds and ambushers come for you at once; everything else (dungeon monsters too) wanders until it notices you
        if (Arena || Summoned) { _state = State.Chase; AnyRealm = true; }
        if (FromDungeon) AnyRealm = true;
        if (Ambusher) _state = State.Chase;
        _summonT = (float)GD.RandRange(5, 9);
    }

    // body parts are collected and merged per (parent, material) after building: one draw call per material instead of one per box.
    // Flash and wind-up tint still work because they change the shared materials.
    readonly List<(Node3D parent, Mesh m, Material mat, Transform3D t)> _parts = new();
    void Add(Mesh m, Material mat, Vector3 pos, Vector3? rot = null, Node3D? parent = null, Vector3? scl = null)
        => _parts.Add((parent ?? _body, m, mat, new Transform3D(Basis.FromEuler(rot ?? Vector3.Zero).Scaled(scl ?? Vector3.One), pos)));

    /// <summary>
    /// Two-legged foe: tapered torso and hips, walking legs on hip pivots, arms, head with glowing eyes, and gear by type
    /// (goblin ears and nose, skeleton ribs and skull sockets, bandit hood and mask, archer bow and quiver, brute belly, horns and club, boss crown and cape).
    /// </summary>
    void BuildHumanoid(float r, float s, Color col, StandardMaterial3D main, StandardMaterial3D dark, StandardMaterial3D glow)
    {
        bool skel = Type == "skeleton", gob = Type == "goblin", brute = Type == "brute" || Type == "warchief" || Type == "golem", archer = Type == "archer", bandit = Type == "bandit";
        float w = brute ? 1.35f : gob ? .9f : 1f;                                           // girth
        var skinC = skel ? new Color(.86f, .83f, .72f) : gob || Type == "warchief" ? new Color(.42f, .62f, .28f) : brute ? new Color(.62f, .48f, .36f) : new Color(.84f, .66f, .52f);
        var skin = Mat(skinC); var steel = Mat(Def.Boss ? new Color(.72f, .76f, .86f) : new Color(.6f, .62f, .66f)); var leather = Mat(new Color(.3f, .2f, .12f));
        var cloth = skel ? Mat(new Color(.25f, .22f, .2f)) : main;
        float hipY = .9f * s;
        // legs
        foreach (float sx in new[] { -1f, 1f })
        {
            var hip = new Node3D { Position = new Vector3(sx * .15f * s * w, hipY, 0) }; _body.AddChild(hip); _legs.Add(hip);
            Add(new CylinderMesh { TopRadius = (skel ? .06f : .12f) * s * w, BottomRadius = (skel ? .05f : .09f) * s * w, Height = .5f * s, RadialSegments = 8 }, skel ? skin : dark, new Vector3(0, -.25f * s, 0), null, hip);
            Add(new CylinderMesh { TopRadius = (skel ? .05f : .09f) * s * w, BottomRadius = (skel ? .045f : .08f) * s * w, Height = .38f * s, RadialSegments = 8 }, skel ? skin : leather, new Vector3(0, -.66f * s, 0), null, hip);
            Add(new BoxMesh { Size = new Vector3(.15f, .1f, .26f) * s * w }, leather, new Vector3(0, -.85f * s, -.05f * s), null, hip);
        }
        // torso
        if (skel)
        {
            Add(new CylinderMesh { TopRadius = .05f * s, BottomRadius = .05f * s, Height = .6f * s, RadialSegments = 6 }, skin, new Vector3(0, 1.2f * s, .05f * s));
            for (int i = 0; i < 4; i++) Add(new TorusMesh { InnerRadius = .17f * s - i * .012f * s, OuterRadius = .2f * s - i * .012f * s, Rings = 12, RingSegments = 4 }, skin, new Vector3(0, (1.42f - i * .1f) * s, 0), null, null, new Vector3(1, 1, .8f));
            Add(new CylinderMesh { TopRadius = .2f * s, BottomRadius = .16f * s, Height = .14f * s, RadialSegments = 8 }, skin, new Vector3(0, .98f * s, 0));
        }
        else
        {
            Add(new CylinderMesh { TopRadius = .3f * s * w, BottomRadius = .23f * s * w, Height = .55f * s, RadialSegments = 12 }, cloth, new Vector3(0, 1.3f * s, 0));
            Add(new SphereMesh { Radius = .29f * s * w, Height = .24f * s, RadialSegments = 12, Rings = 4 }, cloth, new Vector3(0, 1.56f * s, 0));
            Add(new CylinderMesh { TopRadius = .23f * s * w, BottomRadius = .26f * s * w, Height = .28f * s, RadialSegments = 12 }, dark, new Vector3(0, .98f * s, 0));
            Add(new CylinderMesh { TopRadius = .245f * s * w, BottomRadius = .255f * s * w, Height = .07f * s, RadialSegments = 12 }, leather, new Vector3(0, 1.08f * s, 0));
            if (brute) Add(new SphereMesh { Radius = .3f * s, Height = .55f * s, RadialSegments = 12, Rings = 6 }, Mat(skinC.Darkened(.05f)), new Vector3(0, 1.2f * s, -.14f * s));
            foreach (float sx in new[] { -1f, 1f }) if (!gob) Add(new SphereMesh { Radius = .15f * s * w, Height = .18f * s, RadialSegments = 8, Rings = 3, IsHemisphere = true }, Def.Boss || bandit ? steel : leather, new Vector3(sx * .33f * s * w, 1.5f * s, 0), new Vector3(0, 0, -sx * .4f));
        }
        // arms (right arm forward holding the weapon, left arm hanging)
        foreach (float sx in new[] { -1f, 1f })
        {
            var sh = new Vector3(sx * .36f * s * w, 1.48f * s, 0); var hand = sx > 0 ? new Vector3(.45f * s * w, 1.1f * s, -.25f * s) : new Vector3(-.42f * s * w, .98f * s, .05f * s);
            var d = hand - sh; var mid = (sh + hand) / 2; var basis = Basis.LookingAt(d.Normalized(), Mathf.Abs(d.Normalized().Y) > .95f ? Vector3.Right : Vector3.Up);
            _parts.Add((_body, new CapsuleMesh { Radius = (skel ? .045f : .08f) * s * w, Height = d.Length() + .1f * s, RadialSegments = 8, Rings = 2 }, skel ? skin : cloth, new Transform3D(basis * Basis.FromEuler(new Vector3(Mathf.Pi / 2, 0, 0)), mid)));
            Add(new SphereMesh { Radius = .075f * s * w, Height = .15f * s * w, RadialSegments = 8, Rings = 4 }, skel ? skin : leather, hand);
        }
        // head
        var hp = new Vector3(0, 1.8f * s, 0); float hr = (gob ? .2f : .18f) * s * (brute ? 1.1f : 1f);
        Add(new CylinderMesh { TopRadius = .07f * s, BottomRadius = .09f * s, Height = .14f * s, RadialSegments = 8 }, skin, new Vector3(0, 1.64f * s, 0));
        Add(new SphereMesh { Radius = hr, Height = hr * (skel ? 2.1f : 2f), RadialSegments = 12, Rings = 6 }, skin, hp);
        foreach (float sx in new[] { -1f, 1f })
        {
            if (skel) Add(new SphereMesh { Radius = hr * .28f, Height = hr * .5f, RadialSegments = 8, Rings = 4 }, Mat(new Color(.05f, .04f, .04f)), hp + new Vector3(sx * hr * .38f, hr * .1f, -hr * .8f));
            Add(new SphereMesh { Radius = hr * .13f, Height = hr * .26f, RadialSegments = 6, Rings = 3 }, glow, hp + new Vector3(sx * hr * .38f, hr * .1f, -hr * .88f));
            if (gob) Add(new CylinderMesh { TopRadius = 0, BottomRadius = hr * .3f, Height = hr * 1.5f, RadialSegments = 6 }, skin, hp + new Vector3(sx * hr * 1.2f, hr * .25f, 0), new Vector3(0, 0, -sx * 1.25f));
            if (brute || Type == "warchief") Add(new CylinderMesh { TopRadius = 0, BottomRadius = hr * .22f, Height = hr * 1.1f, RadialSegments = 6 }, Mat(new Color(.9f, .86f, .75f)), hp + new Vector3(sx * hr * .7f, hr * .75f, -hr * .1f), new Vector3(-.3f, 0, -sx * .6f));
        }
        if (gob) Add(new CylinderMesh { TopRadius = hr * .05f, BottomRadius = hr * .2f, Height = hr * .7f, RadialSegments = 6 }, skin, hp + new Vector3(0, -hr * .15f, -hr * 1.05f), new Vector3(-Mathf.Pi / 2.2f, 0, 0));
        if (bandit || archer)
        {
            var hood = Mat(archer ? new Color(.22f, .32f, .18f) : new Color(.25f, .2f, .17f));
            Add(new SphereMesh { Radius = hr * 1.18f, Height = hr * 1.5f, RadialSegments = 12, Rings = 5, IsHemisphere = true }, hood, hp + new Vector3(0, hr * .05f, hr * .1f), new Vector3(-.35f, 0, 0));
            Add(new CylinderMesh { TopRadius = 0, BottomRadius = hr * .55f, Height = hr * 1.2f, RadialSegments = 8 }, hood, hp + new Vector3(0, hr * .25f, hr * .9f), new Vector3(1.2f, 0, 0));
            if (bandit) Add(new BoxMesh { Size = new Vector3(hr * 1.7f, hr * .55f, hr * .3f) }, Mat(new Color(.12f, .1f, .1f)), hp + new Vector3(0, -hr * .35f, -hr * .78f));
        }
        if (Def.Boss) for (int k = 0; k < 6; k++) { float a = k / 6f * Mathf.Tau; Add(new CylinderMesh { TopRadius = 0, BottomRadius = hr * .16f, Height = hr * .7f, RadialSegments = 5 }, Mat(new Color(.9f, .72f, .3f), .4f), hp + new Vector3(Mathf.Cos(a) * hr * .75f, hr * .9f, Mathf.Sin(a) * hr * .75f)); }
        if (Def.Boss || Type == "revenant") Add(new BoxMesh { Size = new Vector3(.62f * s * w, 1.1f * s, .04f) }, Mat(col.Darkened(.6f)), new Vector3(0, 1.0f * s, .3f * s), new Vector3(-.12f, 0, 0));
        // weapon in the right hand
        var rh = new Vector3(.45f * s * w, 1.1f * s, -.25f * s);
        if (archer)
        {
            var wood = Mat(new Color(.45f, .3f, .16f));
            Add(new TorusMesh { InnerRadius = .5f * s, OuterRadius = .54f * s, Rings = 16, RingSegments = 4 }, wood, rh + new Vector3(0, 0, .25f * s), new Vector3(0, 0, Mathf.Pi / 2), null, new Vector3(1, 1, .45f));
            Add(new CylinderMesh { TopRadius = .09f * s, BottomRadius = .08f * s, Height = .55f * s, RadialSegments = 8 }, leather, new Vector3(.12f * s, 1.35f * s, .27f * s), new Vector3(.35f, 0, -.3f));
        }
        else if (brute || Def.Slam)
        {
            float cl = 1.3f * s; var wood = Mat(new Color(.4f, .27f, .15f));
            Add(new CylinderMesh { TopRadius = .14f * s, BottomRadius = .05f * s, Height = cl, RadialSegments = 8 }, wood, rh + new Vector3(0, 0, -cl / 2), new Vector3(-Mathf.Pi / 2, 0, 0));
            for (int k = 0; k < 4; k++) Add(new CylinderMesh { TopRadius = 0, BottomRadius = .04f * s, Height = .12f * s, RadialSegments = 4 }, steel, rh + new Vector3(Mathf.Cos(k * 1.57f) * .13f * s, Mathf.Sin(k * 1.57f) * .13f * s, -cl * .85f), new Vector3(0, 0, k * 1.57f - Mathf.Pi / 2));
        }
        else
        {
            float wl = (Def.Boss ? 1.3f : .85f) * s;
            Add(Gfx.Blade(wl, .08f * s, .025f * s), steel, rh + new Vector3(0, 0, -.12f * s));
            Add(new BoxMesh { Size = new Vector3(.28f, .05f, .05f) * s }, leather, rh + new Vector3(0, 0, -.08f * s));
        }
    }
    // ------------------------------------------------------------------ noticing heroes
    float _seeT; bool _saw;
    /// <summary>Within aggro range: outdoors that is enough; in a dungeon the monster must see the hero (no walls between), or hear them up close.</summary>
    bool Notices(Node3D hero, float dist, float dt)
    {
        if (!FromDungeon || dist < 4f) return true;
        _seeT -= dt; if (_seeT > 0) return _saw; _seeT = .25f + GD.Randf() * .1f;
        var from = GlobalPosition + Vector3.Up * Mathf.Min(1.4f, HeightM() * .8f); var to = hero.GlobalPosition + Vector3.Up * 1.2f;
        var q = PhysicsRayQueryParameters3D.Create(from, to, 1); q.Exclude = new Godot.Collections.Array<Rid> { GetRid() };
        _saw = GetWorld3D().DirectSpaceState.IntersectRay(q).Count == 0;
        return _saw;
    }
    /// <summary>This monster noticed a hero (or was struck): the idle pack around it (9 m) joins the fight.</summary>
    void Alert()
    {
        foreach (var n in GetTree().GetNodesInGroup("enemies"))
            if (n is Enemy e && e != this && !e.Dead && !e.Puppet && e._state == State.Idle && !e.Def.Flee && e.FromDungeon == FromDungeon && e.GlobalPosition.DistanceSquaredTo(GlobalPosition) < 81f) e.Wake();
    }

    // ------------------------------------------------------------------ animated monster models (assets/models/monsters, CC0)
    Node3D? _mdl; AnimationPlayer? _mdlAp; string _aIdle = "", _aMove = "", _aAtk = "", _aHit = "", _aDie = "", _aCur = ""; int _aSerial = -1, _aSerialHit = -1, _swingSerial, _hitSerial;
    static readonly Dictionary<string, PackedScene?> _models = new(); public static bool LogModels;
    /// <summary>Loads Def.Model, scales it to Def.H metres, turns it to face forward (-Z), gives it its own materials (hit flash, status tint) and finds its clips.</summary>
    bool BuildModel(Color col)
    {
        string path = $"res://assets/models/monsters/{Def.Model}.glb";
        if (!_models.TryGetValue(path, out var ps)) _models[path] = ps = ResourceLoader.Exists(path) ? GD.Load<PackedScene>(path) : null;
        if (ps == null) { GD.PushWarning($"[ENEMY] model missing: {path}"); return false; }
        var inst = ps.Instantiate<Node3D>(); _mdl = new Node3D { Name = "Model" }; _mdl.AddChild(inst); _body.AddChild(_mdl);
        // size: measure the meshes at rest (in the model's own space), scale to the wanted height, stand on the ground
        var box = new Aabb(); bool any = false;
        foreach (var n in inst.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (n is not MeshInstance3D mi) continue;
            var xf = Transform3D.Identity; Node? c = mi; while (c != null && c != inst) { if (c is Node3D n3) xf = n3.Transform * xf; c = c.GetParent(); }
            var tb = xf * mi.GetAabb(); box = any ? box.Merge(tb) : tb; any = true;
        }
        float want = (Def.H > 0 ? Def.H : 1.6f) * _scale, k = any && box.Size.Y > .01f ? want / box.Size.Y : 1f;
        if (Def.Size > 0) k *= Def.Size;   // per-monster correction (data/enemies.json "size") where the model's bounds mislead
        if (LogModels) GD.Print($"[MODEL] {Type}: bounds {box.Size.X:0.00} x {box.Size.Y:0.00} x {box.Size.Z:0.00}, scale {k:0.000}");
        _mdl.Scale = Vector3.One * k; _mdl.Rotation = new Vector3(0, Mathf.Pi, 0);   // glTF faces +Z, our monsters face -Z
        _mdl.Position = new Vector3(0, -(any ? box.Position.Y : 0) * k + Def.Lift, 0);
        // own copies of the materials, registered for the hit flash and status tint; tint / the Shadow realm colour the model
        Color? tint = Def.Tint != "" ? Color.FromHtml(Def.Tint) : Realm_() == 1 ? new Color(.85f, .78f, 1f) : null;
        foreach (var n in inst.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (n is not MeshInstance3D mi || mi.Mesh == null) continue;
            for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
            {
                if (mi.GetActiveMaterial(s) is not BaseMaterial3D m0) continue;
                var m = (BaseMaterial3D)m0.Duplicate(); var c = tint is { } tc ? m.AlbedoColor * tc : m.AlbedoColor;
                if (Def.Emissive) { m.EmissionEnabled = true; m.Emission = (tint ?? c).Darkened(.2f); m.EmissionEnergyMultiplier = .7f; }
                m.AlbedoColor = c; mi.SetSurfaceOverrideMaterial(s, m);
                if (m is StandardMaterial3D sm) { _mats.Add(sm); _baseCols.Add(c); }
            }
        }
        _mdlAp = inst.FindChild("AnimationPlayer", true, false) as AnimationPlayer;
        if (_mdlAp != null)
        {
            var names = new List<string>(); foreach (var x in _mdlAp.GetAnimationList()) names.Add(x.ToString());
            string Pick(params string[] keys) { foreach (var key in keys) foreach (var nm in names) { var b = nm.Contains('|') ? nm[(nm.IndexOf('|') + 1)..] : nm; if (b.Contains(key, System.StringComparison.OrdinalIgnoreCase)) return nm; } return ""; }
            _aIdle = Pick("Flying_Idle", "Idle"); _aMove = Pick("Run", "Gallop", "Fast_Flying", "Flying", "Walk");
            _aAtk = Pick("Attack", "Bite_Front", "Weapon", "Punch", "Headbutt"); _aHit = Pick("HitRecieve", "HitReact", "Hit"); _aDie = Pick("Death");
            foreach (var nm in new[] { _aIdle, _aMove }) if (nm != "" && _mdlAp.GetAnimation(nm) is { } a) a.LoopMode = Animation.LoopModeEnum.Linear;
            if (_aIdle != "") _mdlAp.Play(_aIdle, 0); _aCur = _aIdle;
        }
        return true;
    }
    /// <summary>For the monster sheet tool: hold a clip (idle, move, attack, hit, death) at a fraction of its length.</summary>
    public bool ModelPose(string role, float frac)
    {
        if (_mdlAp == null) return false; string n = role switch { "move" => _aMove, "attack" => _aAtk, "hit" => _aHit, "death" => _aDie, _ => _aIdle }; if (n == "") n = _aIdle; if (n == "") return false;
        _mdlAp.ProcessMode = ProcessModeEnum.Always; _mdlAp.Play(n, 0); _mdlAp.Seek(_mdlAp.GetAnimation(n).Length * frac, true); _mdlAp.Pause(); return true;
    }
    public bool HasModel => _mdl != null;
    /// <summary>Hunting a hero right now (chasing, winding up or recovering from a blow).</summary>
    public bool Hunting => _state == State.Chase || _state == State.Wind || _state == State.Recover || _state == State.Breath;
    /// <summary>The model's clip from the AI state: attack through the wind-up (stretched to it), flinch, death, move or idle.</summary>
    void AnimateModel()
    {
        var ap = _mdlAp; if (ap == null) return;
        string want; float speed = 1f; bool restart = false; float hs = new Vector2(Velocity.X, Velocity.Z).Length();
        if (_state == State.Dead) want = _aDie;
        else if (_state == State.Wind && _aAtk != "")
        {
            want = _aAtk; if (_aSerial != _swingSerial) { _aSerial = _swingSerial; restart = true; }
            speed = Mathf.Clamp((float)ap.GetAnimation(want).Length * .5f / Mathf.Max(.15f, Def.Wind), .5f, 3f);   // the bite or blow lands as the wind-up ends
        }
        else if (_state == State.Stagger && _aHit != "") { want = _aHit; if (_aSerialHit != _hitSerial) { _aSerialHit = _hitSerial; restart = true; } }
        else
        {
            if ((_aCur == _aAtk || _aCur == _aHit) && ap.IsPlaying() && ap.CurrentAnimationPosition < ap.CurrentAnimationLength * .95f && hs < .4f) return;   // let the blow play out
            if (hs > .4f && _aMove != "") { want = _aMove; speed = Mathf.Clamp(hs / Mathf.Max(1f, Def.Spd * .7f), .6f, 2f); }
            else want = _aIdle;
        }
        if (want == "") return;
        if (want != _aCur || restart) { ap.Play(want, restart ? .05f : .15f); if (restart) ap.Seek(0, true); _aCur = want; }
        ap.SpeedScale = speed;
    }

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
        // rounded body: a horizontal capsule torso with a deeper chest, neck, skull, tapered snout and nose, ears, tail
        bool canine = look == "" ; float by = r * 1.1f + dy, bw = r * .68f * (bodyH / 1.4f);
        Add(new CapsuleMesh { Radius = bw, Height = r * bodyL, RadialSegments = 12, Rings = 4 }, main, new Vector3(0, by, 0), new Vector3(Mathf.Pi / 2, 0, 0), null, new Vector3(.95f, 1f, 1.05f));
        Add(new SphereMesh { Radius = bw * 1.08f, Height = bw * 2.3f, RadialSegments = 12, Rings = 6 }, main, new Vector3(0, by + r * .05f, -r * bodyL * .28f));
        float headY = r * 1.55f + dy + (look == "deer" ? r * 0.9f : 0), headZ = -r * (bodyL * 0.5f + 0.25f);
        var neckFrom = new Vector3(0, by + r * .15f, -r * bodyL * .38f); var neckTo = new Vector3(0, headY - r * .1f, headZ + r * .15f); var nd = neckTo - neckFrom;
        _parts.Add((_body, new CylinderMesh { TopRadius = r * .28f, BottomRadius = r * .42f, Height = nd.Length() + r * .2f, RadialSegments = 10 }, main,
            new Transform3D(Basis.LookingAt(nd.Normalized(), Vector3.Right) * Basis.FromEuler(new Vector3(-Mathf.Pi / 2, 0, 0)), (neckFrom + neckTo) / 2)));
        Add(new SphereMesh { Radius = r * .42f, Height = r * .84f, RadialSegments = 12, Rings = 6 }, main, new Vector3(0, headY, headZ), null, null, new Vector3(1f, .9f, 1.1f));
        float snL = r * (look == "rabbit" ? .25f : look == "boar" ? .5f : look == "deer" ? .55f : .7f);
        Add(new CylinderMesh { TopRadius = r * .12f, BottomRadius = r * .26f, Height = snL, RadialSegments = 10 }, canine ? dark : main, new Vector3(0, headY - r * .12f, headZ - r * .35f - snL / 2), new Vector3(-Mathf.Pi / 2, 0, 0));
        Add(new SphereMesh { Radius = r * .11f, Height = r * .18f, RadialSegments = 8, Rings = 4 }, Mat(new Color(.08f, .06f, .06f)), new Vector3(0, headY - r * .06f, headZ - r * .35f - snL));
        foreach (float sx in new[] { -1f, 1f })
        {
            Add(new SphereMesh { Radius = r * .09f, Height = r * .18f, RadialSegments = 6, Rings = 3 }, eye, new Vector3(sx * r * .25f, headY + r * .1f, headZ - r * .36f));
            if (canine) Add(new CylinderMesh { TopRadius = 0, BottomRadius = r * .17f, Height = r * .5f, RadialSegments = 4 }, dark, new Vector3(sx * r * .24f, headY + r * .45f, headZ + r * .08f), new Vector3(.15f, 0, -sx * .25f));
            if (look == "deer") Add(new SphereMesh { Radius = r * .16f, Height = r * .5f, RadialSegments = 6, Rings = 3 }, main, new Vector3(sx * r * .38f, headY + r * .3f, headZ + r * .12f), new Vector3(0, 0, -sx * 1.1f));
        }
        if (canine)
        {
            // bushy tail and a dark ruff of fur along the spine
            Add(new SphereMesh { Radius = r * .22f, Height = r * 1.3f, RadialSegments = 8, Rings = 4 }, main, new Vector3(0, by + r * .2f, r * bodyL * .62f), new Vector3(-1.0f, 0, 0));
            for (int k = 0; k < 4; k++) Add(new SphereMesh { Radius = r * .22f, Height = r * .3f, RadialSegments = 8, Rings = 3 }, dark, new Vector3(0, by + bw * .8f, -r * bodyL * .32f + k * r * bodyL * .12f), new Vector3(.3f, 0, 0), null, new Vector3(.8f, .6f, 1.4f));
        }
        else if (look == "boar") Add(new CylinderMesh { TopRadius = r * .02f, BottomRadius = r * .06f, Height = r * .5f, RadialSegments = 4 }, dark, new Vector3(0, by + r * .2f, r * bodyL * .55f), new Vector3(-.9f, 0, 0));
        // legs on hip pivots (front-left/back-right swing together): thigh, shin, paw
        float lw = r * (look == "deer" ? 0.2f : 0.3f), ll = hipY;
        foreach (var (x, z) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
        {
            var hip = new Node3D { Position = new Vector3(x * r * 0.42f, hipY, z * r * bodyL * 0.36f) }; _body.AddChild(hip); _legs.Add(hip);
            bool hind = z > 0;
            Add(new CylinderMesh { TopRadius = lw * (hind ? .85f : .7f), BottomRadius = lw * .4f, Height = ll * .55f, RadialSegments = 8 }, main, new Vector3(0, -ll * .25f, hind ? lw * .2f : 0), new Vector3(hind ? .25f : 0, 0, 0), hip);
            Add(new CylinderMesh { TopRadius = lw * .4f, BottomRadius = lw * .32f, Height = ll * .55f, RadialSegments = 6 }, dark, new Vector3(0, -ll * .72f, 0), null, hip);
            Add(new SphereMesh { Radius = lw * .45f, Height = lw * .6f, RadialSegments = 6, Rings = 3 }, dark, new Vector3(0, -ll + lw * .2f, -lw * .15f), null, hip);
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
    public void Stun(float t) { if (Dead) return; if (Duelist) { DuelParried(t); return; } _hitSerial++; if (Puppet) { if (DuelPeer == 0) Net.I?.SendEnemyStun(this, t); return; } _stun = Mathf.Max(_stun, t); if (_state != State.Sleep) _state = State.Stagger; }
    public void Ignite(float t, float dps) => Status.Add("burn", t / 3f, 1, dps);
    public void Slow(float t) => Status.Add("chill", t / 3f, 0, 0);
    public void Wake() { if (Def.Flee && !Dead) { _state = State.Flee; return; } if (_state == State.Sleep || _state == State.Idle || _state == State.Return) { if (Overworld && !Puppet && Hp >= MaxHp - .5f) Rescale(false); _state = State.Chase; if (_state == State.Chase && Def.Kind == "dragon") Sfx.Play("roar"); } }

    // ------------------------------------------------------------------ defenses and statuses
    public readonly StatusSet Status = new();
    float LvlK => 1f + .04f * (Level - 1);
    public float ArmorNow => Mathf.Max(-50, Def.Armor * LvlK - Status.Shred);
    public float MrNow => Def.Mr * LvlK;
    public float ResPct(string type) => Def.Res != null && Def.Res.TryGetValue(type, out var r) ? r : 0;
    /// <summary>Final damage of a hit from the player: type bonus, the target's amplifiers, the player's weaken, execute, then armor/magic resist with penetration.</summary>
    public float Incoming(HitInfo h)
    {
        var TS = Game.TS; float raw = AttackerRaw(h) * Status.TakenMul;
        if (h.Remote) return Dmg.Mitigate(raw, h.Type, ArmorNow, MrNow, ResPct(h.Type), h.APen, h.APenP, h.MPen, h.MPenP);   // a friend's hit: their penetration came with it
        return h.IsPlayer ? Dmg.Mitigate(raw, h.Type, ArmorNow, MrNow, ResPct(h.Type), TS["armorPen"], TS["armorPenP"], TS["magicPen"], TS["magicPenP"]) : Dmg.Mitigate(raw, h.Type, ArmorNow, MrNow, ResPct(h.Type), 0, 0, 0, 0);
    }
    /// <summary>The attacker's side of the damage: our type bonus, weaken on us, execute and rage. The target's side (amplify, armour, resist) comes after.</summary>
    float AttackerRaw(HitInfo h)
    {
        float raw = h.Damage; var TS = Game.TS;
        if (h.IsPlayer)
        {
            raw *= 1 + TS[h.Type + "P"] / 100f;
            if (Player.Instance != null) raw *= Player.Instance.Statuses.DealtMul;
            if (h.Execute > 0 && Hp < MaxHp * .35f) raw *= 1 + h.Execute / 100f;
            if (TS["rage"] > 0 && Player.Instance != null) raw *= 1 + (1 - Player.Instance.Hp / Mathf.Max(1, Game.D.MaxHp())) * .5f * TS["rage"];
        }
        return raw;
    }
    /// <summary>Every status this hit may cause, with our duration and strength bonuses already applied (sent with hits on puppets).</summary>
    List<(string id, float chance, float durMul, float pow)> BakedStatuses(HitInfo h)
    {
        var TS = Game.TS; var l = new List<(string, float, float, float)>(); float durMul = 1 + TS["statusP"] / 100f;
        if (h.Statuses != null) foreach (var (id, c, dm, pw) in h.Statuses) l.Add((id, c, dm * durMul, pw * (1 + TS[id + "P"] / 100f)));
        foreach (var sd in Dmg.Statuses) { float c = TS[sd.Id + "Chance"] / 100f; if (c > 0) l.Add((sd.Id, c, durMul, TS[sd.Id + "P"] / 100f)); }
        return l;
    }
    void ApplyStatuses(HitInfo h, float dealt)
    {
        var TS = Game.TS; float durMul = h.IsPlayer ? 1 + TS["statusP"] / 100f : 1;
        void Try(string id, float chance, float dm, float pow)
        {
            if (Def.Boss && (id == "freeze" || id == "stun" || id == "root")) return;
            if (GD.Randf() < chance) Status.Add(id, dm * durMul, pow * (h.IsPlayer ? 1 + TS[id + "P"] / 100f : 1), Mathf.Max(h.Damage, dealt));
        }
        if (h.Statuses != null) foreach (var (id, c, dm, pw) in h.Statuses) Try(id, c, dm, pw);
        if (h.IsPlayer) foreach (var sd in Dmg.Statuses) { float c = TS[sd.Id + "Chance"] / 100f; if (c > 0) Try(sd.Id, c, 1, 0); }   // passives like "bleedChance"
    }

    /// <summary>
    /// A hit on a puppet (a client's copy of a host enemy, or a duel stand-in): show the impact here at once,
    /// then send our side of the damage to whoever owns the target.
    /// </summary>
    void PuppetHit(HitInfo h)
    {
        if (DuelPeer != 0 && !Net.InLiveDuel) return;
        float est = Incoming(h); int dmg = Mathf.Max(1, Mathf.RoundToInt(est * (DuelPeer != 0 ? Net.PvpScale : 1f)));
        if (h.IsPlayer) { var D = Game.D; var p = Player.Instance; if (p != null) { p.Hp = Mathf.Min(D.MaxHp(), p.Hp + dmg * (Game.TS["leech"] + h.Leech) / 100f); p.Mp = Mathf.Min(D.MaxMp(), p.Mp + dmg * Game.TS["mleech"] / 100f); } }
        _flash = .12f;
        if (!h.NoText) FloatText.Spawn(this, GlobalPosition + new Vector3(0, HeightM() + 0.3f, 0), h.Crit ? dmg + "!" : dmg.ToString(), h.Crit ? new Color(1, 0.82f, 0.3f) : h.Type != "physical" ? Dmg.TypeColor(h.Type) : Colors.White, h.Crit ? 1.25f : 1f);
        Fx.Burst(GlobalPosition + new Vector3(0, HeightM() * 0.6f, 0), h.Crit ? new Color(1, 0.8f, 0.2f) : new Color(1, 0.3f, 0.2f), h.Crit ? 12 : 6, 4f, 0.35f, 0.08f);
        var away = GlobalPosition - h.From; away.Y = 0; Feel.Load();
        if (away.LengthSquared() > 0.001f) _recoilDir = Basis.Inverse() * away.Normalized();
        _recoilK = Mathf.Clamp(h.Knock / 5f, 0.4f, 1.5f) * (h.Crit ? 1.3f : 1f); _recoilT = Feel.RecoilTime;
        Net.I?.SendEnemyHit(this, h, h.IsPlayer ? AttackerRaw(h) : h.Damage, Net.PackStatuses(h.IsPlayer ? BakedStatuses(h) : h.Statuses));
    }

    /// <summary>Stamps this enemy's damage type and on-hit statuses onto a hit against the player.</summary>
    public HitInfo OnHit(HitInfo h) { h.Type = Def.DmgType; h.Attacker ??= this; if (Def.OnHit != null) foreach (var kv in Def.OnHit) h.With(kv.Key, kv.Value); return h; }

    public void TakeHit(HitInfo h)
    {
        if (Dead) return;
        if (h.IsPlayer && !h.Remote) Targeting.NoteHostile(this);   // auto target "monsters you hit"
        if (Puppet) { PuppetHit(h); return; }
        if (h.Remote) _lastPeer = h.Peer; else if (h.IsPlayer) _lastPeer = Net.MyId;
        float dealt = Incoming(h); int dmg = Mathf.Max(1, Mathf.RoundToInt(dealt));
        if (Duelist && !DuelHit(h, ref dmg)) return;   // dodged, immune, or soaked by the Eclipse barrier
        if (Scarletta) ScarHitTaken(ref dmg);          // the Bloom counts it; broken, she takes more
        Hp -= dmg; _flash = 0.12f;
        if (h.IsPlayer && !h.Remote) Flag.Event($"hit {Def.N} Lv{Level} for {dmg}{(h.Crit ? " crit" : "")} {h.Type}, its hp {Mathf.Max(0, Hp):0}/{MaxHp:0}");
        if (h.IsPlayer) { var D = Game.D; var p = Player.Instance; if (p != null) { p.Hp = Mathf.Min(D.MaxHp(), p.Hp + dmg * (Game.TS["leech"] + h.Leech) / 100f); p.Mp = Mathf.Min(D.MaxMp(), p.Mp + dmg * Game.TS["mleech"] / 100f); } }
        ApplyStatuses(h, dealt);
        var tc = Dmg.TypeColor(h.Type);
        if (!h.NoText) FloatText.Spawn(this, GlobalPosition + new Vector3(0, HeightM() + 0.3f, 0), h.Crit ? dmg + "!" : dmg.ToString(), h.Crit ? new Color(1, 0.82f, 0.3f) : h.Type != "physical" ? tc : h.Fire ? new Color(1, 0.7f, 0.4f) : Colors.White, h.Crit ? 1.25f : 1f);
        var away = GlobalPosition - h.From; away.Y = 0;
        if (away.LengthSquared() > 0.001f && !Def.Boss) _kb += away.Normalized() * h.Knock * 1.6f;
        if (h.Up > 0 && !Def.Boss && Def.Kind != "dragon") { _launch = h.Up; Stun(Mathf.Max(h.Stun, h.Up * .12f)); }
        if (h.Stun > 0 && !Def.Boss && Def.Kind != "dragon") Stun(h.Stun * 1f);
        else if (_state == State.Idle || _state == State.Sleep || _state == State.Return) { bool idle = _state == State.Idle; Wake(); if (idle) Alert(); }   // struck: the pack joins in
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
        _state = State.Dead; _deadT = 0; CollisionLayer = 0; CollisionMask = 1; _label.Visible = false; if (_aura != null) _aura.Visible = false;
        if (CurrentBoss == this) CurrentBoss = null;
        var D = Game.D; var p = Player.Instance;
        // shared overworld kill in multiplayer: the host pays the killer and their party (experience, quests, materials, loot by party rules)
        bool shared = Net.IsHost && NetIdx >= 0 && !Arena && !FromDungeon;
        // party dungeon: the member running the monsters pays everyone on the floor (Net.DunKill)
        bool dshared = FromDungeon && Dungeon.IsOwner && NetIdx >= 0, netKill = shared || dshared;
        if (dshared) RespawnTime = 1e9f;   // keep the dead body (hidden) so its index stays valid for friends who arrive later
        long killer = _lastPeer < 0 ? Net.MyId : _lastPeer;
        float lvlF = Level < D.Level - Tune.F("monsters.lowLevelGap", 4) ? Tune.F("monsters.lowLevelXp", .5f) : 1f;   // data/tuning.json "game.monsters"
        int xp = Mathf.RoundToInt(Def.Xp * (1 + Tune.F("monsters.xpPerLevel", .12f) * (Level - 1)) * Tune.F("monsters.xpMul", 1f) * (netKill ? 1f : lvlF) * (Summoned ? Tune.F("monsters.summonedXp", .3f) : 1f) * _xpMul);
        if (DefOverride != null || Arena) xp = 0;   // no experience from Wave Survival or the Shadow Duel
        Fx.Burst(GlobalPosition + Vector3.Up, Def.Kind == "wraith" ? new Color(0.7f, 0.4f, 1f) : new Color(0.8f, 0.2f, 0.2f), 16, 5f, 0.6f, 0.12f, -6f, "magic");
        var mats = new System.Text.StringBuilder();
        bool noDrops = Arenas.NoDrops(this);   // Wave Survival pays valor only
        if (Def.Drops != null && !Summoned && !noDrops)
            foreach (var kv in Def.Drops)
            {
                int lo = kv.Value.Length > 0 ? kv.Value[0] : 1, hi = kv.Value.Length > 1 ? kv.Value[1] : lo, n = GD.RandRange(lo, hi);
                if (netKill) mats.Append(kv.Key).Append(':').Append(n).Append(';'); else GatherDb.Give(kv.Key, n, GlobalPosition);
            }
        bool lootable = !Summoned && DefOverride == null && !Def.NoLoot && !noDrops;
        int gold = Mathf.RoundToInt(GD.RandRange(Tune.F("monsters.goldMin", 2) + Level * Tune.F("monsters.goldMinPerLevel", 1), Tune.F("monsters.goldMax", 6) + Level * Tune.F("monsters.goldMaxPerLevel", 2)) * (Def.Boss ? Tune.F("monsters.bossGoldMul", 12) : 1) * Tune.F("monsters.goldMul", 1));
        float chance = Def.Boss ? 1f : Mathf.Min(1f, Tune.F("monsters.itemChance", .2f) + (Def.Realm == 1 ? Tune.F("monsters.shadowItemChance", .06f) : 0f) + Tune.F("monsters.upgradeItemChance", .06f) * UpgradeCount()), bonus = (Def.Boss ? Tune.F("monsters.bossLootBonus", 2) : 0) + (Def.Realm == 1 ? Tune.F("monsters.shadowLootBonus", 1) : 0) + _lootBonus;
        if (dshared) { Net.I!.DunKill(this, xp, killer, mats.ToString(), lootable, gold, chance, bonus, Def.Boss ? Tune.F("monsters.bossPotionChance", .6f) : Tune.F("monsters.potionChance", .07f)); lootable = false; }
        else if (shared) Net.I!.KillRewards(this, xp, killer, mats.ToString());
        else { D.Kills++; if (xp > 0 && p != null) p.AddXp(xp, GlobalPosition); }
        if (lootable)
        {
            if (shared)
            {
                Net.I!.DropLoot(GlobalPosition, Level, chance, bonus, gold, Def.Boss ? Tune.F("monsters.bossPotionChance", .6f) : Tune.F("monsters.potionChance", .07f), killer);
                if (Def.Boss) { Net.I.DropLoot(GlobalPosition, Level, 1f, 3f, 0, 0f, killer); Net.I.DropLoot(GlobalPosition, Level, .6f, 2f, 0, 0f, killer); }
            }
            else
            {
                Loot.DropFrom(GlobalPosition, Level, chance, bonus, gold, Def.Boss ? Tune.F("monsters.bossPotionChance", .6f) : Tune.F("monsters.potionChance", .07f));
                if (Def.Boss) { Loot.DropFrom(GlobalPosition + new Vector3(1, 0, 1), Level, 1f, 3f, 0, 0f); Loot.DropFrom(GlobalPosition + new Vector3(-1, 0, -1), Level, .6f, 2f, 0, 0f); }
                if (FromDungeon) LootTables.TrySetDrop(GlobalPosition, Level, UpgradeCount() > 0, Def.Boss);   // set gear: deep floors only
            }
        }
        _lastPeer = -1;
        if (Arena) Arenas.OnEnemyDown(this);
        else if (!netKill) Quests.OnKill(Type, Def.Realm, Def.Boss);
        if (Def.Boss) { Ui.Banner(Def.N, "has fallen"); Sfx.Play("level"); if (FromDungeon) Dungeon.OnBossDown(this); }
        if (!Arena && !Summoned && !FromDungeon && !Ambusher && RespawnTime <= 0) RespawnTime = (Def.Boss ? Tune.F("monsters.bossRespawn", 600) : Tune.F("monsters.respawn", 260)) * Threat.RespawnMul;   // the land refills faster the longer you play
        Game.Save();
    }

    void Respawn()
    {
        Hp = MaxHp; Rescale(false); GlobalPosition = _home; _state = Def.Kind == "dragon" ? State.Sleep : State.Idle; _deadT = 0; _stun = 0; Status.Clear();
        CollisionLayer = 2; CollisionMask = 1 | 2; _label.Visible = true; _body.Rotation = Vector3.Zero; Velocity = Vector3.Zero;
        _body.Position = Vector3.Zero; _body.Scale = Vector3.One; for (int i = 0; i < _mats.Count; i++) { _mats[i].Transparency = BaseMaterial3D.TransparencyEnum.Disabled; _mats[i].AlbedoColor = _baseCols[i]; }
    }

    // ------------------------------------------------------------------ AI
    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        bool act = AnyRealm || Def.Realm == World.Realm;
        if (act != Active) { Active = act; Visible = act; if (!Dead) { CollisionLayer = act ? 2u : 0u; } }
        if (!Active) return;
        // who to chase: our own hero, or in a hosted world the nearest living hero out in the overworld
        _heroT -= dt; if (_heroT <= 0 || _hero == null || !IsInstanceValid(_hero)) { _heroT = .3f; _hero = PickHero(); }
        var pl = _hero;
        float distP = pl == null ? 999f : (pl.GlobalPosition - GlobalPosition).Length();
        if (distP > (Def.Look != "" ? 100f : 150f) && !Arena && !Dead) { Visible = false; if (_mdlAp != null) _mdlAp.Active = false; return; } // small wildlife is not worth drawing far away
        if (!Visible && Active) { Visible = true; if (_mdlAp != null) _mdlAp.Active = true; }

        var v = Velocity;
        if (!IsOnFloor()) v.Y -= _gravity * dt; else v.Y = 0;
        if (_launch > 0) { v.Y = _launch; _launch = 0; }   // knock-up

        if (_recoilT > 0 || _body.Position != Vector3.Zero)
        {
            _recoilT = Mathf.Max(0, _recoilT - dt); float f = _recoilT / Mathf.Max(0.01f, Feel.RecoilTime); f *= f; // snap out, ease back
            float k = f * _recoilK; _body.Position = _recoilDir * Feel.RecoilPush * k;
            _body.Scale = new Vector3(1 + Feel.RecoilSquash * k * 0.5f, 1 - Feel.RecoilSquash * k, 1 + Feel.RecoilSquash * k * 0.5f);
        }
        if (_flash > 0) { _flash -= dt; foreach (var m in _mats) m.AlbedoColor = Colors.White; }
        else { var st = Status.Tint; for (int i = 0; i < _mats.Count; i++) _mats[i].AlbedoColor = (_state == State.Wind ? _baseCols[i].Lerp(new Color(1, 0.2f, 0.1f), 1f - Mathf.Clamp(_timer / Mathf.Max(0.01f, Def.Wind), 0, 1)) : _baseCols[i]).Lerp(new Color(st.R, st.G, st.B), st.A); }

        if (_state == State.Dead)
        {
            _deadT += dt;
            if (_av == null && _mdlAp == null) _body.Rotation = new Vector3(Mathf.Lerp(0, Mathf.Pi / 2, Mathf.Clamp(_deadT / 0.5f, 0, 1)), 0, 0);   // models play their own death
            AnimateAvatar();
            // dissolve: after the fall the body releases motes of light, then sinks, shrinks and fades away
            const float D0 = 1.4f, D1 = 2.8f;
            if (_deadT - dt < D0 && _deadT >= D0 && Visible)
            {
                var sc = Def.Realm == 1 || Def.Kind == "wraith" ? new Color(.75f, .45f, 1f) : Def.Look != "" ? new Color(1f, .95f, .8f) : new Color(1f, .7f, .35f);
                var g = new Gradient(); g.SetColor(0, new Color(1, 1, 1, .9f)); g.SetColor(1, new Color(sc, 0)); g.AddPoint(.3f, new Color(sc, .8f));
                Vfx.Sparks(GlobalPosition + Vector3.Up * HeightM() * .3f, g, Def.Boss ? 60 : 22, 1.6f, 1.4f, .2f, 2.2f, true, 35f, Vector3.Up, HeightM() * .45f);
                foreach (var m in _mats) m.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            }
            if (_deadT >= D0)
            {
                float k = Mathf.Clamp((_deadT - D0) / (D1 - D0), 0, 1);
                _body.Position = new Vector3(0, -k * HeightM() * .5f, 0); _body.Scale = Vector3.One * (1 - k * .35f);
                for (int i = 0; i < _mats.Count; i++) _mats[i].AlbedoColor = _baseCols[i] with { A = 1 - k };
                if (k >= 1 && RespawnTime <= 0) { QueueFree(); return; }
                if (k >= 1) Visible = false;
            }
            if (_deadT > 8f)
            {
                if (Puppet) Visible = false;   // the host decides when it comes back
                else if (RespawnTime > 0) { Visible = false; if (_deadT > RespawnTime && distP > 95f) Respawn(); }
                else QueueFree();
            }
            Velocity = new Vector3(0, v.Y, 0); MoveAndSlide();
            return;
        }
        if (Puppet) { PuppetMove(dt); if (Scarletta) ScarPuppetTick(dt); return; }

        // damage over time / slow
        // statuses: damage over time (mitigated by resistances only), freeze/stun, slows and roots
        bool died = false;
        Status.Tick(dt, (type, amount, col) =>
        {
            if (died) return;
            if (Scarletta && type == "poison") { int hn = Mathf.Max(1, Mathf.RoundToInt(amount)); Hp = Mathf.Min(MaxHp, Hp + hn); FloatText.Spawn(this, GlobalPosition + new Vector3(GD.Randf() - .5f, HeightM() + 0.1f, 0), "+" + hn, new Color(.5f, 1f, .4f), 0.7f); return; }   // rot feeds her
            float d = type == "true" ? amount : amount * (1 - Mathf.Clamp(ResPct(type), -100, 90) / 100f) * Status.TakenMul;
            int n = Mathf.Max(1, Mathf.RoundToInt(d)); Hp -= n; FloatText.Spawn(this, GlobalPosition + new Vector3(GD.Randf() - .5f, HeightM() + 0.1f, 0), n.ToString(), col, 0.7f);
            if (Hp <= 0) { died = true; Die(null); }
        });
        if (died) return;
        if (Duelist) { DuelTick(dt, pl, v); return; }   // the Shadow Duel boss has its own brain (EnemyDuelist.cs)
        if (Scarletta) { ScarTick(dt, v); return; }    // so does Scarletta (EnemyScarletta.cs)
        if (Status.Stunned && !Def.Boss && _state != State.Sleep) { _stun = Mathf.Max(_stun, .1f); _state = State.Stagger; }
        float spdMul = Def.Boss ? Mathf.Max(.6f, Status.SpeedMul) : Status.SpeedMul;

        Vector3 move = Vector3.Zero, toP = Vector3.Zero; float dist = 999f; bool plOk = pl is IDamageable pd && !pd.Dead;
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
                // notice a hero only when close enough and (indoors) in plain sight; then the pack nearby joins in
                if (plOk && dist < Def.Aggro && Notices(pl!, dist, dt)) { Wake(); Alert(); break; }
                {
                    // wander: amble to a spot near home, pause, pick another (wildlife grazes slower and closer)
                    bool wild = Def.Look != "";
                    _grazeT -= dt;
                    if (_grazeT <= 0) { _grazeT = (float)GD.RandRange(3.0, 8.0); float a = (float)GD.RandRange(0, Mathf.Tau), d = (float)GD.RandRange(wild ? 2.0 : 1.5, wild ? 8.0 : FromDungeon ? 5.0 : 9.0); _grazeTo = _home + new Vector3(Mathf.Cos(a) * d, 0, Mathf.Sin(a) * d); }
                    var g = _grazeTo - GlobalPosition; g.Y = 0;
                    if (g.Length() > 0.6f && _grazeT < 4.5f) { move = g.Normalized() * Def.Spd * (wild ? 0.18f : 0.28f); _yaw = Mathf.LerpAngle(_yaw, Util.YawTo(g.X, g.Z), 1f - Mathf.Exp(-4f * dt)); }
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
                // dungeon monsters give up when you get well away (or they are dragged too far from their room), and go back to wandering
                if (FromDungeon && !Def.Boss && !Summoned && (dist > Def.Aggro * 3f || (GlobalPosition - _home).Length() > 45f)) { _state = State.Return; break; }
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
                    if (dist <= range && _timer <= 0) { _state = State.Wind; _timer = Def.Wind; _swingSerial++; }
                }
                else
                {
                    if (dist > Def.Reach * 0.8f) move = toP.Normalized() * Def.Spd;
                    if (dist <= Def.Reach + 0.2f && _timer <= 0) { _state = State.Wind; _timer = Def.Wind; _swingSerial++; }
                }
                break;
            case State.Wind:
                _yaw = Mathf.LerpAngle(_yaw, yawToP, 1f - Mathf.Exp(-12f * dt));
                _timer -= dt;
                // melee: lunge in over the last third of the wind-up so the blow lands in contact, not from a step away
                if (!ranged && _timer < Def.Wind * .35f && dist - Def.R * _scale > .9f)
                    move = new Vector3(-Mathf.Sin(_yaw), 0, -Mathf.Cos(_yaw)) * Mathf.Min(Def.Spd * 2.2f, (dist - Def.R * _scale - .7f) / Mathf.Max(.05f, _timer));
                if (_timer <= 0)
                {
                    if (plOk)
                    {
                        if (ranged)
                        {
                            var from = GlobalPosition + new Vector3(0, 1.4f * _scale, 0); var to = pl!.GlobalPosition + new Vector3(0, 1.1f, 0);
                            var dir = (to - from).Normalized();
                            { var pr = Projectile.Spawn(Def.Ranged == "arrow" ? "arrow" : "ebolt", 'e', from, dir * (Def.Ranged == "arrow" ? 26f : 18f), Damage, 3f); pr.Source = this; }
                            Sfx.Play(Def.Ranged == "arrow" ? "swing" : "zap", 0.6f);
                        }
                        else if (dist - 0.4f <= Def.Reach + 0.5f)
                        {
                            if (Def.Slam) { Fx.Ring(GlobalPosition + Vector3.Up * 0.1f, Def.Reach + 0.8f, new Color(1, 0.5f, 0.2f)); if (pl is Player) Player.Instance?.Rig?.Shake(0.2f); }
                            ((IDamageable)pl!).TakeHit(OnHit(new HitInfo { Damage = Damage, From = GlobalPosition, Knock = Def.Slam ? 9f : 4f, Stagger = Def.Slam, Attacker = this }));
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
                    for (int i = 0; i < 2; i++) Fx.Burst(mouth + fwd * (float)GD.RandRange(0, 14), new Color(1f, 0.5f + GD.Randf() * 0.3f, 0.1f), 2, 3f, 0.5f, 0.35f, 1f, "fire");
                    if (_tick >= 0.25f)
                    {
                        _tick = 0;
                        if (plOk && dist < 22f && Mathf.Abs(Util.AngDiff(_yaw, yawToP)) < 0.55f)
                            ((IDamageable)pl!).TakeHit(OnHit(new HitInfo { Damage = Damage * 0.35f, From = GlobalPosition, Knock = 1f, NoBlock = true, IsProjectile = true, Attacker = this }));
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
        AnimateAvatar();
    }

    // ------------------------------------------------------------------ character model (humanoid foes listed in data/avatar.json "enemies")
    Avatar? _av;
    void BuildAvatar(System.Text.Json.JsonElement fc, float s)
    {
        float k = JsonUtil.F(fc, "scale", 1f);
        var ap = new Appearance { Female = Avatar.Str(fc, "body") == "female", Skin = Avatar.Str(fc, "skin") != "" ? Color.FromHtml(Avatar.Str(fc, "skin")) : new Color(.86f, .66f, .5f), Hair = new Color(.18f, .12f, .08f) };
        if (Realm_() == 1) ap.Skin = ap.Skin.Lerp(new Color(.55f, .45f, .7f), .5f);   // Shadow-realm foes are tinted like the realm
        // s = the type's size from enemies.json, k = an extra factor from avatar.json
        _av = new Avatar { Name = "Avatar", Scale = Vector3.One * k * s }; _body.AddChild(_av); _av.Build(ap, Type + GetInstanceId());
        var tint = Avatar.Str(fc, "tint") != "" ? Color.FromHtml(Avatar.Str(fc, "tint")) : (Color?)null;
        string wt = Avatar.Str(fc, "weapon", "sword");
        _av.Dress(Avatar.Outfit(fc), wt, tint ?? Colors.White, tint);
    }
    /// <summary>Clip from the AI state: wind-up plays the attack (stretched to the wind-up), stagger a hit, death the death clip.</summary>
    void AnimateAvatar()
    {
        AnimateModel();
        if (_av == null) return;
        float hs = new Vector2(Velocity.X, Velocity.Z).Length();
        switch (_state)
        {
            case State.Dead: _av.Play("die", .1f); return;
            case State.Wind: _av.Play(Def.Ranged != null ? "shoot" : Def.Slam ? "powerHeavy" : "melee", .1f, Mathf.Max(.3f, Def.Wind / .6f), _av.Cur == "" || !_av.Busy && _windStart, _swing); _windStart = false; return;
            case State.Stagger: _av.Play("hit", .05f); return;
        }
        if (_state != State.Recover) { _windStart = true; if (_state == State.Chase && _av.Busy) return; }
        if (_av.Busy) return;
        _av.Move(hs, true, true, false);
        if (_state == State.Recover) _swing++;
    }
    bool _windStart = true; int _swing;

    // ------------------------------------------------------------------ multiplayer
    Node3D? PickHero()
    {
        var me = Player.Instance;
        if (!Net.Active || Puppet || Arena || (Ambusher && NetIdx < 0) || (FromDungeon && !Dungeon.Shared)) return me;   // shared ambushers hunt any hero
        Node3D? best = null; float bd = float.MaxValue;
        foreach (var h in FromDungeon ? Net.DungeonHeroes() : Net.WorldHeroes()) { float d = h.GlobalPosition.DistanceSquaredTo(GlobalPosition); if (d < bd) { bd = d; best = h; } }
        return best ?? me;
    }

    public const int NetStride = 11;   // index + x, y, z, yaw, hp, state, wind-up progress, max hp, level, statuses (bits)
    /// <summary>Enemies per snapshot packet: keeps each packet under one network frame (~1100 of ~1400 bytes); bigger unreliable packets broke the connection.</summary>
    public const int NetPerPacket = 1100 / (4 * NetStride);
    /// <summary>Host: this enemy's state for a snapshot.</summary>
    public void NetWrite(List<float> b)
    {
        if (Scarletta && !Dead) { ScarWrite(b); return; }   // her move, health bars and the Bloom ride in the state slots
        var p = GlobalPosition; b.Add(p.X); b.Add(p.Y); b.Add(p.Z); b.Add(_yaw); b.Add(Hp); b.Add((float)_state);
        b.Add(_state == State.Wind ? Mathf.Clamp(_timer / Mathf.Max(.01f, Def.Wind), 0, 1) : 1f);
        b.Add(MaxHp); b.Add(Level);   // scaling depends on the host's hero and the lobby, so friends take the host's numbers
        b.Add(Dmg.Mask(Status));      // statuses live on the host's copy: friends see the icons (and the tint) too
    }
    /// <summary>Client: apply a snapshot from the host.</summary>
    public void NetRead(float[] f, int k)
    {
        if (Scarletta) { ScarRead(f, k); return; }
        _netPos = new Vector3(f[k], f[k + 1], f[k + 2]); _netYaw = f[k + 3]; Hp = f[k + 4]; var st = (State)(int)f[k + 5];
        MaxHp = Mathf.Max(1, f[k + 7]); int lv = (int)f[k + 8];
        if (lv != Level && lv > 0) { Level = lv; if (!Summoned && DefOverride == null && _label != null) _label.Text = UpgradeLine() + Def.N + $"  Lv{Level}"; }
        Dmg.FromMask(f[k + 9], Status);   // display only: puppets never tick statuses
        if (!_netHas) { _netHas = true; GlobalPosition = _netPos; _yaw = _netYaw; }
        if (st == State.Dead) { if (!Dead) PuppetDie(); return; }
        if (Dead) { Respawn(); GlobalPosition = _netPos; }
        if (st == State.Wind && _state != State.Wind) _swingSerial++;   // a new blow: the model restarts its attack
        _state = st; if (st == State.Wind) _timer = f[k + 6] * Def.Wind;
        if (st == State.Breath) _breathT = 1.2f;
    }
    void PuppetDie()
    {
        _state = State.Dead; _deadT = 0; CollisionLayer = 0; _label.Visible = false; Hp = 0;
        if (CurrentBoss == this) CurrentBoss = null; RespawnTime = Ambusher ? 0 : 1e9f; if (_aura != null) _aura.Visible = false;   // ambushers never come back: free them after the fade
        Status.Clear();
        Fx.Burst(GlobalPosition + Vector3.Up, Def.Kind == "wraith" ? new Color(0.7f, 0.4f, 1f) : new Color(0.8f, 0.2f, 0.2f), 16, 5f, 0.6f, 0.12f, -6f, "magic");
        if (FromDungeon && Def.Boss) { Ui.Banner(Def.N, "has fallen"); Sfx.Play("level"); Dungeon.OnBossDown(this); }   // party dungeon: the way down opens for everyone
    }
    /// <summary>Client puppet: glide toward the host's position and heading; legs follow the actual speed.</summary>
    void PuppetMove(float dt)
    {
        if (DuelPeer != 0)
        {
            var h = Net.I?.HeroOf(DuelPeer);
            if (h != null) { GlobalPosition = h.GlobalPosition; Hp = h.Hp; MaxHp = Mathf.Max(1, h.MaxHp); Visible = true; }
            return;
        }
        if (!_netHas) return;   // no word from the host yet (too far away): stay where the world placed us
        var before = GlobalPosition;
        GlobalPosition = before.DistanceTo(_netPos) > 8f ? _netPos : before.Lerp(_netPos, 1f - Mathf.Exp(-10f * dt));
        _yaw = Mathf.LerpAngle(_yaw, _netYaw, 1f - Mathf.Exp(-10f * dt)); Rotation = new Vector3(0, _yaw, 0);
        if (_state == State.Wind) _timer = Mathf.Max(0, _timer - dt);
        if (_mdlAp != null) { Velocity = (GlobalPosition - before) / Mathf.Max(dt, .001f); AnimateModel(); }   // models animate from the host's movement
        if (_legs.Count > 0)
        {
            float hs = new Vector2(GlobalPosition.X - before.X, GlobalPosition.Z - before.Z).Length() / Mathf.Max(dt, .001f), amt = Mathf.Clamp(hs / Mathf.Max(1f, Def.Spd * 0.6f), 0, 1);
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
            GetParent().AddChild(e); _summoned++; if (FromDungeon) Dungeon.Register(e);
            Fx.Burst(pos + Vector3.Up, new Color(0.6f, 0.3f, 0.9f), 14, 4f);
        }
        Ui.Note(Def.N + " calls for aid!", new Color(1, 0.6f, 0.4f));
    }
}
