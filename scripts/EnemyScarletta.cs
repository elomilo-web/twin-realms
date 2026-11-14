using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>
/// Scarletta, The Rotting Blade: the raid boss of the Rotting Hollow (DungeonLair.cs), fought solo or by a party.
/// A giant woman in golden armour with long flowing hair and a single angel wing, wielding an enormous katana.
///  - She reads the challengers when they first come near (Assess): her health comes from every hero's level and weapon,
///    her blows from their health and armour, her armour and magic resist lean toward the damage they deal, and she
///    favours the moves that punish their make-up (whirls against crowds of melee, gap-closers against archers and casters,
///    and she hunts healers).
///  - Several health bars (more with more challengers).
///  - She walks slowly, judging; then strikes suddenly with long, sweeping, flowing attacks; she closes gaps in a blink.
///  - The Bloom: at 75%, 50% and 25% health she plants her blade and a rot flower grows: deal enough damage before it opens,
///    or a wave of rot poisons everyone, herself too, and poison heals her.
/// Owner-side brain (the party's dungeon owner runs her); friends see her moves through the snapshot (ScarNetRead).
/// Numbers: data/tuning.json "game.raid".
/// </summary>
public partial class Enemy
{
    public bool Scarletta => Type == "scarletta";
    enum SM { Sleep, Judge, Sweep, Flurry, Whirl, Lunge, Ascend, Bloom, Wave, Broken, Recover }
    SM _sm = SM.Sleep, _smLast = SM.Sleep; float _smT, _smWind, _smDur, _smThink; int _smHit, _smHitN; bool _smDone, _assessed;
    Node3D? _smTgt; Vector3 _smTo, _smFrom; float _smYaw0;
    int _bloomIdx; float _bloomNeed = 1, _bloomDone, _bloomLeft; MeshInstance3D? _bloomMesh; float _waveR; readonly HashSet<Node3D> _waveHit = new();
    float _sDk = 10, _wMelee, _wRanged, _wMagic; int _heroes = 1; bool _healers;
    ShaderMaterial? _goldMat; SwordTrail? _sTrail; readonly List<Node3D> _hair = new();
    /// <summary>Health bars shown for her (her health is split evenly between them).</summary>
    public int HpBars = 1;
    /// <summary>For the HUD: the Bloom's remaining damage share (0..1) and seconds left; -1 when none.</summary>
    public float BloomFrac => Scarletta && _sm == SM.Bloom ? Mathf.Clamp(1 - _bloomDone / Mathf.Max(1, _bloomNeed), 0, 1) : -1;
    public float BloomLeft => _bloomLeft;
    public bool ScarBroken => _sm == SM.Broken;
    public string ScarMove => _sm.ToString();
    static readonly float[] BloomAt = { .75f, .5f, .25f };

    static float RT(string k, float d) => Tune.F("raid." + k, d);
    int ScarPhase => Hp > MaxHp * .66f ? 1 : Hp > MaxHp * .33f ? 2 : 3;
    float SQ => ScarPhase == 1 ? 1f : ScarPhase == 2 ? .88f : .78f;

    // ------------------------------------------------------------------ body
    void BuildScarletta()
    {
        Def = Def.Clone();   // her defences are rewritten per fight (Assess)
        var gold = new Color(1f, .78f, .34f);
        var ap = new Appearance { Female = true, Skin = new Color(.96f, .8f, .45f), Hair = new Color(.9f, .88f, .8f), GlowEyes = true, Eyes = new Color(.6f, 1f, .4f) };
        _av = new Avatar { Name = "Avatar", Scale = Vector3.One * _scale }; _body.AddChild(_av); _av.Build(ap, "scarletta");
        var outfit = new Dictionary<string, Item?> { ["armor"] = new Item { Name = "Plate", Slot = "armor" }, ["hands"] = new Item { Name = "Gauntlets", Slot = "hands" }, ["feet"] = new Item { Name = "Greaves", Slot = "feet" } };
        _av.Dress(s => outfit.GetValueOrDefault(s), "none", gold, gold);
        // gold over everything: a bright rim and a warm sheen; rot-green when she gathers a heavy blow
        _goldMat = new ShaderMaterial { Shader = new Shader { Code = @"shader_type spatial;
render_mode blend_add, depth_draw_never, unshaded, cull_back, shadows_disabled;
uniform vec4 rim : source_color = vec4(1.0, .8, .35, 1.0);
uniform vec4 rot : source_color = vec4(.45, 1.0, .25, 1.0);
uniform float glow = 0.0;
void fragment() {
	float f = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 2.5);
	ALBEDO = mix(rim.rgb * (.12 + .55 * f), rot.rgb * (.3 + .9 * f), glow);
}" } };
        void Over(Node n) { foreach (var c in n.GetChildren()) { if (c is GeometryInstance3D g && g.MaterialOverlay == null) g.MaterialOverlay = _goldMat; Over(c); } }
        Over(_av);
        // the blade: an enormous katana with a slight curve and a rot-green edge
        var hold = _av.HandSocket("sword");
        var steel = new StandardMaterial3D { AlbedoColor = new Color(.86f, .88f, .9f), Metallic = .95f, Roughness = .18f };
        var edge = new StandardMaterial3D { AlbedoColor = new Color(.5f, 1f, .35f), EmissionEnabled = true, Emission = new Color(.45f, 1f, .3f), EmissionEnergyMultiplier = 2.2f };
        var wrap = new StandardMaterial3D { AlbedoColor = new Color(.35f, .08f, .12f), Roughness = .8f }; var guard = new StandardMaterial3D { AlbedoColor = gold, Metallic = .9f, Roughness = .3f };
        hold.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(.045f, .045f, .42f) }, MaterialOverride = wrap, Position = new Vector3(0, 0, .08f) });
        hold.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = .09f, BottomRadius = .09f, Height = .03f, RadialSegments = 12 }, MaterialOverride = guard, Position = new Vector3(0, 0, .3f), Rotation = new Vector3(Mathf.Pi / 2, 0, 0) });
        float L = RT("bladeLength", 2.4f); int segs = 6; var at = new Vector3(0, 0, .32f); float bend = 0;
        for (int i = 0; i < segs; i++)
        {
            float sl = L / segs; bend += .022f; var d = new Vector3(0, Mathf.Sin(bend), Mathf.Cos(bend));
            var seg = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(.018f, .075f * (1 - i * .06f), sl * 1.02f) }, MaterialOverride = steel, Position = at + d * sl / 2, Rotation = new Vector3(-bend, 0, 0) };
            hold.AddChild(seg); seg.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(.02f, .012f, sl * 1.02f) }, MaterialOverride = edge, Position = new Vector3(0, .04f * (1 - i * .06f), 0) });
            at += d * sl;
        }
        var bb = new Node3D { Position = new Vector3(0, 0, .6f) }; var bt = new Node3D { Position = at }; hold.AddChild(bb); hold.AddChild(bt);
        _sTrail = new SwordTrail { Col = new Color(.75f, 1f, .45f), Life = .35f }; AddChild(_sTrail); _sTrail.Base = bb; _sTrail.Tip = bt;
        // a single angel wing on her back
        _av.AddWings(new Color(.97f, .96f, .92f), gold, left: false, right: true, span: 1.45f, glow: .25f);
        // long flowing hair: three strands from the back of the head down past the waist, swaying as she moves
        var hairMat = new StandardMaterial3D { AlbedoColor = ap.Hair, Roughness = .7f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        var tipMat = new StandardMaterial3D { AlbedoColor = new Color(.6f, .8f, .45f), Roughness = .7f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        for (int s = -1; s <= 1; s++)
        {
            Node3D parent = _av.Socket("Head", new Vector3(s * .06f, .02f, -.09f), new Vector3(15, 0, s * 6), "hair");
            for (int i = 0; i < 8; i++)
            {
                float w = (.16f - i * .01f) * (s == 0 ? 1.2f : .9f), len = .16f;
                var seg = new Node3D { Name = "Hair" + i, Position = i == 0 ? Vector3.Zero : new Vector3(0, -len, 0) }; parent.AddChild(seg);
                seg.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(w, len * 1.05f, .03f) }, MaterialOverride = i >= 6 ? tipMat : hairMat, Position = new Vector3(0, -len / 2, 0) });
                _hair.Add(seg); parent = seg;
            }
        }
        // drifting motes of rot around her
        var pm = new ParticleProcessMaterial { EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, EmissionSphereRadius = 2.5f, Direction = Vector3.Up, Spread = 60, InitialVelocityMin = .2f, InitialVelocityMax = .8f, Gravity = new Vector3(0, .3f, 0), ScaleMin = .5f, ScaleMax = 1.4f };
        AddChild(new GpuParticles3D { Position = new Vector3(0, 2.5f, 0), Amount = 40, Lifetime = 2.5f, ProcessMaterial = pm, DrawPass1 = new SphereMesh { Radius = .06f, Height = .12f, RadialSegments = 6, Rings = 3, Material = new StandardMaterial3D { AlbedoColor = new Color(.6f, 1f, .4f), EmissionEnabled = true, Emission = new Color(.5f, 1f, .3f), EmissionEnergyMultiplier = 2f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded } } });
        AddChild(new OmniLight3D { LightColor = new Color(1f, .82f, .45f), OmniRange = 10f, LightEnergy = 1.2f, Position = new Vector3(0, 4f, 0), ShadowEnabled = false });
        _av.Play("idleArmed");
    }
    void ScarTell(float k) => _goldMat?.SetShaderParameter("glow", Mathf.Clamp(k, 0, 1));

    /// <summary>Hair sways with time and leans back when she moves fast.</summary>
    void HairSway(float dt)
    {
        float sp = new Vector2(Velocity.X, Velocity.Z).Length(), t = Game.PlayTime;
        for (int i = 0; i < _hair.Count; i++)
        {
            int k = i % 8; float lean = Mathf.Clamp(sp / 12f, 0, 1) * .14f * (k + 1) / 8f;
            _hair[i].Rotation = new Vector3(lean + Mathf.Sin(t * 1.6f + k * .55f + i) * .06f, 0, Mathf.Sin(t * 1.1f + k * .4f) * .05f);
        }
    }

    // ------------------------------------------------------------------ reading the challengers
    IEnumerable<Node3D> Challengers()
    {
        if (Dungeon.Shared) { foreach (var h in Net.DungeonHeroes()) yield return h; yield break; }
        var p = Player.Instance; if (p != null && !p.Dead) yield return p;
    }
    /// <summary>Called when the challengers first come near: health, damage, defences and tactics from who they are.</summary>
    void Assess()
    {
        _assessed = true; var D = Game.D;
        var heroes = new List<(int lvl, float hp, float wpn, string cls, string wt, float def)>();
        foreach (var h in Challengers())
        {
            if (h is Player) heroes.Add((D.Level, D.MaxHp(), D.WpnDmg(), D.Class, D.WType(), D.Defense()));
            else if (h is RemoteHero rh && Net.I?.Players.GetValueOrDefault(rh.Peer) is { } pi)
                heroes.Add((pi.Level, pi.MaxHp, (pi.Eq.GetValueOrDefault("weapon")?.Dmg ?? 0) + pi.Level * 2.5f, pi.Class, pi.Eq.GetValueOrDefault("weapon")?.Wt ?? "sword", D.Defense()));
        }
        if (heroes.Count == 0) heroes.Add((D.Level, D.MaxHp(), D.WpnDmg(), D.Class, D.WType(), D.Defense()));
        _heroes = heroes.Count;
        float hp = 0, hpSum = 0, lvlSum = 0; _wMelee = _wRanged = _wMagic = 0; _healers = false;
        foreach (var (lvl, mhp, wpn, cls, wt, _) in heroes)
        {
            hp += Mathf.Max(Scaling.EnemyHp(Def.Hp, lvl), Progression.PowerHp(Def.Hp, wpn));
            hpSum += mhp; lvlSum += lvl;
            bool magic = wt == "wand" || wt == "staff", ranged = magic || Weapons.Get(wt).Ranged;
            if (magic) _wMagic++; else if (ranged) _wRanged++; else _wMelee++;
            if (cls == "cleric" || cls == "druid") _healers = true;
        }
        int n = heroes.Count; float avgHp = hpSum / n;
        Level = Mathf.RoundToInt(lvlSum / n) + Progression.DunBoss;
        MaxHp = Hp = hp * RT("hpMul", 1.6f);
        HpBars = Mathf.Clamp((int)RT("barsBase", 3) + n, 2, 9);
        // blows sized to the party's health, against the hero's armour (half credit), like the Shadow Duel
        _sDk = avgHp / Mathf.Sqrt(Mathf.Max(.05f, Progression.ArmorTaken(D.Defense(), Level))) * RT("damageMul", 1f);
        // defences lean toward the damage the party deals
        float physK = (_wMelee + _wRanged) / n, total = Def.Armor + Def.Mr;
        Def.Armor = total * (.25f + .5f * physK); Def.Mr = total * (.25f + .5f * (1 - physK));
        if (_label != null) _label.Text = Def.N + $"  Lv{Level}";
        // what she has learned, in words
        var tells = new List<string> { physK > .6f ? "her gold hardens against blades" : physK < .4f ? "her gold drinks in magic" : "her guard is even" };
        if (_wMelee >= n * .5f) tells.Add("she will sweep those who crowd her");
        if (_wRanged + _wMagic >= n * .5f) tells.Add("no distance will save you");
        if (_healers) tells.Add("she has marked your healer");
        Ui.Banner(Def.N, n > 1 ? $"she judges {n} challengers" : "she judges you"); Ui.Note("Scarletta studies you: " + string.Join(", ", tells) + ".", new Color(.8f, 1f, .6f));
        Sfx.Play("roar");
    }

    // ------------------------------------------------------------------ the brain (owner)
    void ScarBegin(SM m, float dur = 0)
    {
        if (m is SM.Sweep or SM.Flurry or SM.Whirl or SM.Lunge or SM.Ascend) _smLast = m;
        _sm = m; _smT = 0; _smDur = dur; _smDone = false; _smHit = 0; ScarTell(0); if (_sTrail != null) _sTrail.Emit = false;
        float q = SQ;
        switch (m)
        {
            case SM.Sweep: _smWind = .95f * q; break;
            case SM.Flurry: _smHitN = 2 + ScarPhase; _smWind = .55f * q; break;
            case SM.Whirl: _smWind = .75f * q; break;
            case SM.Lunge: _smWind = .32f * q; break;
            case SM.Ascend: _smWind = .5f * q; break;
        }
        ScarVisualStart(m);
    }
    /// <summary>What a move looks like as it starts: on every machine (the owner and friends mirroring her).</summary>
    void ScarVisualStart(SM m)
    {
        if (_av == null) return; float q = SQ;
        switch (m)
        {
            case SM.Sweep: _av.PlayAttack("bigSwing", .95f * q, true); break;
            case SM.Flurry: _av.PlayAttack("slashA", .55f * q, true); break;
            case SM.Whirl: _av.PlayAttack("hook", .75f * q + .3f, true); break;
            case SM.Lunge: _av.PlayAttack("dashSlash", .32f * q + .3f, true); Vfx.Flash(GlobalPosition + Vector3.Up * 3, new Color(1f, .85f, .4f), 5f, 10f, .2f); break;
            case SM.Ascend: _av.PlayAttack("castSlam", .5f * q + .8f, true); break;
            case SM.Bloom: _av.Play("channel", .3f); BloomFlower(true); Ui.Banner("The Bloom", "strike her before it opens!"); Sfx.Play("roar"); break;
            case SM.Wave: _av.Play("cast", .1f, 1.2f, true); BloomFlower(false); Vfx.Ring(GlobalPosition + Vector3.Up * .2f, 46f, new Color(.5f, 1f, .3f), 46f / RT("waveSpeed", 16f)); Vfx.Flash(GlobalPosition + Vector3.Up * 3, new Color(.5f, 1f, .3f), 8f, 40f, .6f); Ui.Note("The Bloom bursts: rot washes over everyone!", new Color(.6f, 1f, .4f)); break;
            case SM.Broken: _av.Play("sit", .3f); BloomFlower(false); Ui.Banner("The Bloom withers", "she falters: strike now!"); Sfx.Play("block"); break;
            case SM.Sleep: _av.Play("idleArmed"); break;
        }
    }

    void ScarTick(float dt, Vector3 v)
    {
        HairSway(dt);
        _smT += dt; _smThink -= dt;
        // the nearest challenger, or the one she has marked
        float best = float.MaxValue; Node3D? near = null; var all = new List<Node3D>();
        foreach (var h in Challengers()) { all.Add(h); float d = h.GlobalPosition.DistanceSquaredTo(GlobalPosition); if (d < best) { best = d; near = h; } }
        if (_smTgt == null || !IsInstanceValid(_smTgt) || _smTgt is IDamageable { Dead: true } || !all.Contains(_smTgt)) _smTgt = near;
        var tgt = _smTgt; float dist = tgt == null ? 999 : Flat(tgt.GlobalPosition - GlobalPosition).Length();
        var toT = tgt == null ? Vector3.Zero : Flat(tgt.GlobalPosition - GlobalPosition); float yawTo = tgt == null ? _yaw : Util.YawTo(toT.X, toT.Z);
        Vector3 move = Vector3.Zero;

        if (_sm == SM.Sleep)
        {
            if (near != null && Mathf.Sqrt(best) < RT("wakeRange", 26f)) { if (!_assessed) Assess(); CurrentBoss = this; ScarBegin(SM.Judge, 1.2f); }
            Velocity = new Vector3(0, v.Y, 0); MoveAndSlide(); return;
        }
        if (CurrentBoss != this) CurrentBoss = this;
        // the Bloom at 75%, 50% and 25% health (not in the air)
        if (_bloomIdx < BloomAt.Length && Hp <= MaxHp * BloomAt[_bloomIdx] && _sm is not (SM.Ascend or SM.Bloom or SM.Wave or SM.Lunge))
        {
            _bloomIdx++; _smFrom = GlobalPosition; _smTo = ArenaCentre();
            _bloomNeed = MaxHp * RT("bloomShare", .09f) * (1 + .1f * (_bloomIdx - 1)); _bloomDone = 0; _bloomLeft = RT("bloomTime", 10f);
            ScarBegin(SM.Bloom);
        }

        switch (_sm)
        {
            case SM.Judge:
            {
                // she walks slowly, sizing you up; inside her reach she circles
                Face(yawTo, 3f, dt);
                if (dist > 9f) move = toT.Normalized() * Def.Spd;
                else move = new Vector3(-toT.Z, 0, toT.X).Normalized() * Def.Spd * .5f;
                if (_smT >= _smDur) ScarDecide(all, dist);
                break;
            }
            case SM.Sweep:
            {
                Face(yawTo, _smT < _smWind * .75f ? 5f : .8f, dt);
                ScarTell(_smT / _smWind > .55f ? (_smT / _smWind - .55f) / .45f : 0);
                if (_sTrail != null) _sTrail.Emit = _smT > _smWind * .7f;
                if (!_smDone && _smT >= _smWind)
                {
                    _smDone = true; Sfx.Play("swing", .5f); Player.Instance?.Rig?.Shake(.25f); SwingArc(RT("sweepReach", 11f), 210f);
                    foreach (var h in all) if (InArcOf(h, RT("sweepReach", 11f), 210f)) ScarHit(h, .34f, 10f);
                }
                if (_smT >= _smWind) _yaw += dt * 2.4f;   // the blade carries her round
                if (_smT >= _smWind + .35f) ScarBegin(SM.Recover, .9f * SQ);
                break;
            }
            case SM.Flurry:
            {
                Face(yawTo, _smT < _smWind * .6f ? 9f : 1.5f, dt);
                if (_sTrail != null) _sTrail.Emit = _smT > _smWind * .5f;
                if (_smT < _smWind && _smT > _smWind * .5f) move = Fwd2 * RT("flurryStep", 7f);   // each slash flows forward
                if (_smT >= _smWind)
                {
                    Sfx.Play("swing", .7f); SwingArc(9f, 150f);
                    foreach (var h in all) if (InArcOf(h, 9f, 150f)) ScarHit(h, .15f, 5f);
                    _smHit++; _yaw += (_smHit % 2 == 0 ? .5f : -.5f);
                    if (_smHit >= _smHitN) { ScarBegin(SM.Recover, .75f * SQ); break; }
                    _smT = 0; _smWind = .34f * SQ;
                    string[] anims = { "backhand", "rising", "slashB", "slashA" };
                    _av?.PlayAttack(anims[_smHit % anims.Length], _smWind, true); NetFlourish = _smHit;
                }
                break;
            }
            case SM.Whirl:
            {
                if (_smT < _smWind) { Face(yawTo, 6f, dt); ScarTell(_smT / _smWind * .7f); break; }
                float k = (_smT - _smWind) / .9f; _yaw += dt * Mathf.Pi * 3f / .9f; if (_sTrail != null) _sTrail.Emit = true;
                if (!_smDone && k >= .45f) { _smDone = true; Sfx.Play("swing", .4f); Vfx.Ring(GlobalPosition + Vector3.Up * .2f, RT("whirlReach", 9.5f), new Color(.8f, 1f, .5f), .35f); foreach (var h in all) if (Flat(h.GlobalPosition - GlobalPosition).Length() < RT("whirlReach", 9.5f)) ScarHit(h, .3f, 14f); }
                if (k >= 1) ScarBegin(SM.Recover, 1f * SQ);
                break;
            }
            case SM.Lunge:
            {
                // she closes the gap in a blink and thrusts
                if (_smT < _smWind) { Face(yawTo, 14f, dt); ScarTell(.5f); if (tgt != null) _smTo = tgt.GlobalPosition + Flat(tgt is CharacterBody3D cb ? cb.Velocity : Vector3.Zero) * .2f; break; }
                var d = Flat(_smTo - GlobalPosition); if (_sTrail != null) _sTrail.Emit = true;
                if (d.Length() > 3f && _smT - _smWind < .55f) { move = d.Normalized() * RT("lungeSpeed", 42f); Face(Util.YawTo(d.X, d.Z), 20f, dt); }
                else if (!_smDone)
                {
                    _smDone = true; Sfx.Play("swing", .8f); SwingArc(6.5f, 100f);
                    foreach (var h in all) if (InArcOf(h, 6.5f, 100f)) ScarHit(h, .26f, 8f);
                    if (ScarPhase >= 2 && GD.Randf() < .4f) { ScarBegin(SM.Flurry); break; }   // straight into a flurry
                    ScarBegin(SM.Recover, .6f * SQ);
                }
                break;
            }
            case SM.Ascend:
            {
                if (_smT < _smWind) { Face(yawTo, 10f, dt); if (tgt != null) _smTo = ClampLair(tgt.GlobalPosition); _smFrom = GlobalPosition; if (_smT + dt >= _smWind) Vfx.GroundMark(_smTo, "circle", 13f, new Color(.5f, 1f, .3f, .85f), .8f, .25f, true); break; }
                float k = Mathf.Clamp((_smT - _smWind) / .8f, 0, 1);
                var p = _smFrom.Lerp(_smTo, k); p.Y = Mathf.Lerp(_smFrom.Y, _smTo.Y, k) + Mathf.Sin(k * Mathf.Pi) * 9f; GlobalPosition = p; Velocity = Vector3.Zero;
                if (k >= 1)
                {
                    GlobalPosition = _smTo; Player.Instance?.Rig?.Shake(.4f); Sfx.Play("roar", .5f);
                    Vfx.Ring(GlobalPosition + Vector3.Up * .1f, 6.5f, new Color(.6f, 1f, .4f), .4f); Fx.Burst(GlobalPosition + Vector3.Up * .3f, new Color(.55f, .8f, .35f), 30, 8f, .7f, .15f, -9f, "dirt");
                    foreach (var h in all) if (Flat(h.GlobalPosition - GlobalPosition).Length() < 6.5f) ScarHit(h, .3f, 12f, poison: 2);
                    ScarBegin(SM.Recover, 1.1f * SQ);
                }
                Rotation = new Vector3(0, _yaw, 0); return;
            }
            case SM.Bloom:
            {
                // she leaps to the centre, plants the blade and the rot flower grows: break it with damage before it opens
                if (_smT < .6f) { var p = _smFrom.Lerp(_smTo, _smT / .6f); p.Y += Mathf.Sin(_smT / .6f * Mathf.Pi) * 5f; GlobalPosition = p; Velocity = Vector3.Zero; Rotation = new Vector3(0, _yaw, 0); return; }
                _bloomLeft = Mathf.Max(0, _bloomLeft - dt); BloomGrow(1 - _bloomLeft / RT("bloomTime", 10f)); ScarTell(.4f + .6f * (1 - _bloomLeft / RT("bloomTime", 10f)));
                if (_bloomDone >= _bloomNeed) { ScarBegin(SM.Broken, RT("brokenTime", 5f)); Fx.Burst(GlobalPosition + Vector3.Up * 2, new Color(1f, .85f, .4f), 40, 8f, .8f, .12f); }
                else if (_bloomLeft <= 0) { _waveR = 0; _waveHit.Clear(); ScarBegin(SM.Wave); RotSelf(); }
                break;
            }
            case SM.Wave:
            {
                // the rot wave rolls out across the chamber: everyone it passes is poisoned (and so is she, which heals her)
                _waveR += RT("waveSpeed", 16f) * dt;
                foreach (var h in all) if (!_waveHit.Contains(h) && Flat(h.GlobalPosition - GlobalPosition).Length() <= _waveR) { _waveHit.Add(h); ScarHit(h, .08f, 6f, noBlock: true, poison: RT("wavePoison", 5)); }
                if (_waveR > 46f) ScarBegin(SM.Recover, .8f);
                break;
            }
            case SM.Broken:
                if (_smT >= _smDur) { ScarBegin(SM.Judge, .6f); }
                break;
            case SM.Recover:
                Face(yawTo, 2f, dt);
                if (_smT >= _smDur) ScarBegin(SM.Judge, (float)GD.RandRange(.9, 2.2) * SQ);
                break;
        }
        float slow = Mathf.Max(.6f, Status.SpeedMul);
        v.X = move.X * slow; v.Z = move.Z * slow; Velocity = v; MoveAndSlide(); Rotation = new Vector3(0, _yaw, 0);
        ScarAnimate();
    }

    /// <summary>Picks the next move from the challengers' make-up, the distance, and what she just did.</summary>
    void ScarDecide(List<Node3D> all, float dist)
    {
        if (all.Count == 0) { ScarBegin(SM.Judge, 1f); return; }   // nobody left standing: she waits
        // the healer is marked: she turns to them now and then
        static bool Healer(string? c) => c is "cleric" or "druid";
        if (_healers && GD.Randf() < .35f) foreach (var h in all) if ((h is Player && Healer(Game.D.Class)) || (h is RemoteHero rh && Healer(Net.I?.Players.GetValueOrDefault(rh.Peer)?.Class))) { _smTgt = h; dist = Flat(h.GlobalPosition - GlobalPosition).Length(); break; }
        int crowd = 0; foreach (var h in all) if (Flat(h.GlobalPosition - GlobalPosition).Length() < 8f) crowd++;
        float n = Mathf.Max(1, _heroes), meleeK = _wMelee / n, farK = (_wRanged + _wMagic) / n;
        var w = new List<(SM m, float wt)>
        {
            (SM.Sweep, dist < 11f ? 3f : 0f),
            (SM.Flurry, dist < 9f ? 3f : dist < 14f ? 1f : 0f),
            (SM.Whirl, crowd >= 1 ? 1f + crowd * 1.5f * (1 + meleeK) : 0f),
            (SM.Lunge, dist > 9f ? 4f + 3f * farK : dist > 5f ? 1f : 0f),
            (SM.Ascend, ScarPhase >= 2 && dist > 6f ? 2.5f : 0f),
        };
        for (int i = 0; i < w.Count; i++) if (w[i].m == _smLast) w[i] = (w[i].m, w[i].wt * .25f);
        float sum = 0; foreach (var e in w) sum += e.wt;
        if (sum <= .01f) { ScarBegin(SM.Judge, .8f); return; }
        float r = GD.Randf() * sum; foreach (var e in w) { r -= e.wt; if (r <= 0) { ScarBegin(e.m); return; } }
        ScarBegin(SM.Judge, .8f);
    }

    /// <summary>Owner: a hit on her counts toward breaking the Bloom; broken, she takes extra damage.</summary>
    void ScarHitTaken(ref int dmg)
    {
        if (_sm == SM.Broken) dmg = Mathf.RoundToInt(dmg * RT("brokenTaken", 1.25f));
        if (_sm == SM.Bloom && _smT >= .6f) _bloomDone += dmg;
        if (_sm == SM.Sleep) { if (!_assessed) Assess(); ScarBegin(SM.Judge, .5f); }
    }
    /// <summary>The rot wave turns on her too: poison that heals her (see the status tick in Enemy.cs).</summary>
    void RotSelf() { for (int i = 0; i < 5; i++) Status.Add("poison", 2f, .1f, MaxHp * RT("selfPoison", .02f)); }

    void ScarHit(Node3D h, float pct, float knock, bool noBlock = false, float poison = 0)
    {
        if (h is not IDamageable dmg || dmg.Dead) return;
        var hi = OnHit(new HitInfo { Damage = _sDk * pct * (ScarPhase == 3 ? RT("enrage", 1.12f) : 1f), From = GlobalPosition, Knock = knock, Stagger = true, Stun = pct >= .25f ? .45f : 0, NoBlock = noBlock, Attacker = this });
        // poison: our own hero gets it directly (the rot reaches you even through a roll); a friend's game applies it from the hit
        if (h is Player p) for (int i = 0; i < (int)poison; i++) p.Statuses.Add("poison", 2f, 0, hi.Damage);
        else for (int i = 0; i < (int)poison; i++) hi.With("poison", 1, 2f, 0);
        dmg.TakeHit(hi);
    }

    // ------------------------------------------------------------------ friends: mirror her from the snapshot
    public int NetFlourish;
    void ScarWrite(List<float> b)
    {
        var p = GlobalPosition; b.Add(p.X); b.Add(p.Y); b.Add(p.Z); b.Add(_yaw); b.Add(Hp);
        b.Add(1000 + (int)_sm + 32 * HpBars);
        b.Add(_sm == SM.Bloom ? 2 * Mathf.Ceil(_bloomLeft) + BloomFrac : NetFlourish);
        b.Add(MaxHp); b.Add(Level); b.Add(Dmg.Mask(Status));
    }
    void ScarRead(float[] f, int k)
    {
        _netPos = new Vector3(f[k], f[k + 1], f[k + 2]); _netYaw = f[k + 3]; Hp = f[k + 4]; MaxHp = Mathf.Max(1, f[k + 7]);
        int lv = (int)f[k + 8]; if (lv > 0 && lv != Level) { Level = lv; if (_label != null) _label.Text = Def.N + $"  Lv{Level}"; }
        Dmg.FromMask(f[k + 9], Status);
        if (!_netHas) { _netHas = true; GlobalPosition = _netPos; _yaw = _netYaw; }
        float st = f[k + 5];
        if (st < 1000) { if ((State)(int)st == State.Dead && !Dead) PuppetDie(); return; }
        int code = (int)st - 1000; var m = (SM)(code % 32); HpBars = Mathf.Max(1, code / 32);
        if (m != SM.Sleep) CurrentBoss = this;
        if (m != _sm) { _sm = m; _smT = 0; ScarVisualStart(m); }
        if (m == SM.Bloom) { float w = f[k + 6]; _bloomLeft = Mathf.Floor(w / 2); float frac = w - 2 * _bloomLeft; _bloomDone = (1 - frac); _bloomNeed = 1; BloomGrow(1 - _bloomLeft / RT("bloomTime", 10f)); }
        else if (m == SM.Flurry && (int)f[k + 6] != NetFlourish) { NetFlourish = (int)f[k + 6]; string[] anims = { "backhand", "rising", "slashB", "slashA" }; _av?.PlayAttack(anims[NetFlourish % anims.Length], .34f, true); }
    }
    void ScarPuppetTick(float dt)
    {
        _smT += dt; HairSway(dt); if (_sTrail != null) _sTrail.Emit = _sm is SM.Sweep or SM.Flurry or SM.Whirl or SM.Lunge && _smT > .3f;
        ScarTell(_sm == SM.Bloom ? .6f : _sm is SM.Sweep or SM.Whirl ? Mathf.Clamp(_smT, 0, 1) * .6f : 0);
        ScarAnimate();
    }
    void ScarAnimate()
    {
        if (_av == null) return;
        if (_sm is SM.Judge or SM.Recover && !_av.Busy) _av.Move(new Vector2(Velocity.X, Velocity.Z).Length() / Mathf.Max(.5f, _scale * .6f), true, true, false);
        _av.FlapWings(Game.PlayTime * .25f, false);
    }

    // ------------------------------------------------------------------ helpers
    static Vector3 Flat(Vector3 v) => new(v.X, 0, v.Z);
    Vector3 Fwd2 => new(-Mathf.Sin(_yaw), 0, -Mathf.Cos(_yaw));
    bool InArcOf(Node3D h, float reach, float arcDeg)
    {
        var d = Flat(h.GlobalPosition - GlobalPosition); if (d.Length() > reach + .5f) return false;
        return d.Length() < 2f || Mathf.Abs(Util.AngDiff(_yaw, Util.YawTo(d.X, d.Z))) < Mathf.DegToRad(arcDeg / 2);
    }
    /// <summary>The arc of a big swing drawn in rot-gold sparks.</summary>
    void SwingArc(float reach, float arcDeg)
    {
        for (int i = 0; i <= 8; i++)
        {
            float a = _yaw + Mathf.DegToRad(-arcDeg / 2 + arcDeg * i / 8f); var p = GlobalPosition + new Vector3(-Mathf.Sin(a), 0, -Mathf.Cos(a)) * reach * .8f + Vector3.Up * 1.2f;
            Fx.Burst(p, i % 2 == 0 ? new Color(1f, .85f, .4f) : new Color(.55f, 1f, .35f), 5, 3f, .35f, .1f);
        }
    }
    /// <summary>The middle of the great chamber (DungeonLair.LairRooms: centre cell 20, 16).</summary>
    Vector3 ArenaCentre() => new(Dungeon.Origin.X + 20.5f * Dungeon.Tile, GlobalPosition.Y, Dungeon.Origin.Z + 16.5f * Dungeon.Tile);
    Vector3 ClampLair(Vector3 at) { var c = ArenaCentre(); var off = Flat(at - c); if (off.Length() > 40f) off = off.Normalized() * 40f; return new Vector3(c.X + off.X, GlobalPosition.Y, c.Z + off.Z); }

    /// <summary>The rot flower around her feet: petals that unfold as the Bloom runs.</summary>
    void BloomFlower(bool on)
    {
        if (!on) { _bloomMesh?.QueueFree(); _bloomMesh = null; return; }
        if (_bloomMesh != null) return;
        _bloomMesh = new MeshInstance3D { Name = "Bloom" }; AddChild(_bloomMesh);
        var petal = new StandardMaterial3D { AlbedoColor = new Color(.65f, .2f, .45f), EmissionEnabled = true, Emission = new Color(.6f, 1f, .3f), EmissionEnergyMultiplier = .8f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        for (int i = 0; i < 10; i++)
        {
            float a = i / 10f * Mathf.Tau; var piv = new Node3D { Rotation = new Vector3(0, a, 0) }; _bloomMesh.AddChild(piv);
            var leaf = new Node3D { Name = "P", Position = new Vector3(0, .1f, -1.2f) }; piv.AddChild(leaf);
            leaf.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 1.2f, Height = .3f, RadialSegments = 10, Rings = 4 }, MaterialOverride = petal, Scale = new Vector3(.7f, 1, 2.2f), Position = new Vector3(0, 0, -1.6f) });
        }
        BloomGrow(0);
    }
    void BloomGrow(float k)
    {
        if (_bloomMesh == null) BloomFlower(true); if (_bloomMesh == null) return;
        _bloomMesh.Scale = Vector3.One * (.4f + 1.4f * k);
        foreach (var c in _bloomMesh.GetChildren()) if (c is Node3D piv && piv.GetChildCount() > 0 && piv.GetChild(0) is Node3D leaf) leaf.Rotation = new Vector3(Mathf.Lerp(1.2f, -.15f, k), 0, 0);
    }
}
