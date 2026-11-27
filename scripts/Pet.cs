using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>
/// The companion out in the world: the species' own enemy model (an Enemy with PetBody = true, no AI) on a light body that follows the
/// hero (leash 4-8 m, teleports past 40 m), fights by its aggro order with its four equipped skills (hits go through Enemy.TakeHit as a
/// player-side HitInfo whose Attacker is this pet, so a struck monster may turn on it), picks up drops by its auto-loot order,
/// is knocked out back into its cube (revives after 60 s) and learns from kills nearby. Tuning: data/pets.json "rules" / "stats.world".
/// </summary>
public partial class Pet : CharacterBody3D, IDamageable
{
    public PetState S = null!;
    public float Hp, MaxHp;
    public bool Dead => _ko;
    public Enemy Body = null!;
    Node3D _vis = null!; Label3D _label = null!;
    bool _ko; float _koT, _gravity, _yaw, _think, _hurtT = 99, _guardT, _buffT, _buffMul = 1;
    Enemy? _foe; Node3D? _lastHitBy; Loot? _loot; readonly HashSet<ulong> _skipLoot = new();
    readonly Dictionary<string, float> _cd = new();
    (Enemy e, PetSkillDef sk, float t)? _pending;
    Vector3 _stayAt; float _side = 1;

    public override void _Ready()
    {
        AddToGroup("pets"); CollisionLayer = 0; CollisionMask = 1;
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.On;
        _gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity");
        AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = .3f, Height = 1f }, Position = new Vector3(0, .5f, 0) });
        var sp = S.Sp();
        _vis = new Node3D { Name = "Vis", Scale = Vector3.One * sp.PetScale }; AddChild(_vis);
        Body = new Enemy { Type = S.Species, PetBody = true, Level = S.Level, AnyRealm = true, Name = "Body" }; _vis.AddChild(Body);
        _label = new Label3D { FontSize = 22, OutlineSize = 5, PixelSize = .005f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Modulate = new Color(.65f, .95f, .7f), OutlineModulate = new Color(.03f, .04f, .03f, .9f), VisibilityRangeEnd = 30 };
        AddChild(_label);
        Refresh(); Hp = MaxHp * Mathf.Clamp(S.HpFrac, .05f, 1);
        _yaw = Player.Instance?.ModelYaw ?? 0; _side = GD.Randf() < .5f ? -1 : 1; _stayAt = GlobalPosition;
    }
    /// <summary>Stats changed (level up, gear): max health, label.</summary>
    public void Refresh()
    {
        var st = S.Stats(); float frac = MaxHp > 0 ? Hp / MaxHp : 1;
        MaxHp = Mathf.Max(st.Hp * PetDb.World("hpMul", 4), Game.D.MaxHp() * PetDb.World("hpFloor", .6f));
        if (MaxHp > 0 && Hp > 0) Hp = MaxHp * frac;
        _label.Text = $"{S.Name()}  Lv{S.Level}";
        _label.Position = new Vector3(0, Mathf.Max(.8f, Body.HeightM() * S.Sp().PetScale) + .35f, 0);
    }
    float Spd => Mathf.Max(3f, Body.Def.Spd);
    public void Teleport(Vector3 at) { GlobalPosition = at; Velocity = Vector3.Zero; ResetPhysicsInterpolation(); Body.ResetPhysicsInterpolation(); _stayAt = at; }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta; var p = Player.Instance; if (p == null || !IsInstanceValid(p)) return;
        S.HpFrac = MaxHp > 0 ? Mathf.Clamp(Hp / MaxHp, 0, 1) : 1;
        if (_ko) { _koT += dt; Body.PetVel = Vector3.Zero; if (_koT > 1.6f) Visible = false; return; }
        _hurtT += dt; _guardT -= dt; _buffT -= dt; if (_buffT <= 0) _buffMul = 1;
        foreach (var k in new List<string>(_cd.Keys)) _cd[k] -= dt;
        if (_hurtT > PetDb.World("regenDelay", 5)) Hp = Mathf.Min(MaxHp, Hp + MaxHp * PetDb.World("regen", .03f) * (1 + PetTree.Mod(S, "regenP") / 100f) * dt);
        // a blow in flight lands
        if (_pending is { } pd) { var pv = pd; pv.t -= dt; _pending = pv; if (pv.t <= 0) { _pending = null; Land(pv.e, pv.sk); } }

        var hero = p.GlobalPosition; var me = GlobalPosition; float dHero = new Vector2(hero.X - me.X, hero.Z - me.Z).Length();
        if (!S.Stay && (dHero > PetDb.R("teleport", 40) || me.Y < hero.Y - 25)) { Teleport(Behind(p)); return; }

        _think -= dt;
        if (_think <= 0) { _think = .25f; Think(p); }

        Vector3 goal = me; float speed = 0; bool face = false; Vector3 faceTo = me;
        if (_foe != null)
        {
            var sk = BestSkill(_foe, out float range);
            var to = _foe.GlobalPosition - me; to.Y = 0; float dist = to.Length() - _foe.Def.R * _foe.ScaleF();
            face = true; faceTo = _foe.GlobalPosition;
            if (dist > range * .85f) { goal = _foe.GlobalPosition; speed = Spd * 1.15f; }
            if (sk != null && dist <= range + .3f && _pending == null) Use(sk, _foe);
        }
        else if (_loot != null && IsInstanceValid(_loot))
        {
            var to = _loot.GlobalPosition - me; to.Y = 0;
            if (to.Length() < 1.3f) { _loot.PetPickup(p); _skipLoot.Add(_loot.GetInstanceId()); _loot = null; }
            else { goal = _loot.GlobalPosition; speed = Spd * 1.1f; }
        }
        else if (S.Stay) { if ((_stayAt - me).Length() > 1f) { goal = _stayAt; speed = Spd * .6f; } }
        else
        {
            float lmin = PetDb.R("leashMin", 4), lmax = PetDb.R("leashMax", 8);
            var spot = Behind(p);
            if (dHero > lmax) { goal = spot; speed = Mathf.Max(Spd * 1.2f, new Vector2(p.Velocity.X, p.Velocity.Z).Length() * 1.1f); }
            else if (dHero > lmin || (spot - me).Length() > 2.5f) { goal = spot; speed = Mathf.Max(Spd * .45f, new Vector2(p.Velocity.X, p.Velocity.Z).Length()); }
        }
        var v = Velocity; if (!IsOnFloor()) v.Y -= _gravity * dt; else v.Y = -.5f;
        var dir = goal - me; dir.Y = 0; float sm = Body.Status.SpeedMul;
        if (speed > 0 && dir.Length() > .3f) { dir = dir.Normalized(); v.X = dir.X * speed * sm; v.Z = dir.Z * speed * sm; if (!face) faceTo = goal; face = true; }
        else { v.X = Mathf.MoveToward(v.X, 0, 30 * dt); v.Z = Mathf.MoveToward(v.Z, 0, 30 * dt); }
        // a ledge or a wall in the way: hop up (pets have no climbing)
        bool stepped = speed > 0 && BlockWorld.Enabled && BlockStep.StepUp(this, v, .45f, 1.2f) > 0;   // stairs and slabs: walked up, not hopped
        if (!stepped && IsOnWall() && IsOnFloor() && speed > 0) v.Y = 6f;
        Velocity = v; MoveAndSlide();
        if (face) { var f = faceTo - GlobalPosition; if (f.X * f.X + f.Z * f.Z > .01f) _yaw = Mathf.LerpAngle(_yaw, Util.YawTo(f.X, f.Z), 1f - Mathf.Exp(-10f * dt)); }
        else _yaw = Mathf.LerpAngle(_yaw, p.ModelYaw, 1f - Mathf.Exp(-2f * dt));
        Rotation = new Vector3(0, _yaw, 0);
        Body.PetVel = new Vector3(Velocity.X, 0, Velocity.Z) / Mathf.Max(.25f, S.Sp().PetScale);   // the body's gait is sized to its full-size self
    }
    Vector3 Behind(Player p)
    {
        var f = p.Facing(); var side = f.Cross(Vector3.Up) * _side;
        var at = p.GlobalPosition - f * 3f + side * 1.6f; return new Vector3(at.X, p.GlobalPosition.Y + .3f, at.Z);
    }

    // ------------------------------------------------------------------ choosing what to do
    void Think(Player p)
    {
        var hero = p.GlobalPosition; float drop = PetDb.R("dropFoeRange", 30);
        bool Ok(Enemy? e) => e != null && IsInstanceValid(e) && !e.Dead && e.Visible && !e.CatchBusy && e.Active && !e.PetBody && e.GlobalPosition.DistanceTo(hero) < drop;
        if (!Ok(_foe)) _foe = null;
        Enemy? want = null;
        switch (S.Aggro)
        {
            case "passive": _foe = null; break;
            case "assist": if (Targeting.Current is Enemy t && Ok(t) && t.GlobalPosition.DistanceTo(hero) < PetDb.R("assistRange", 25)) want = t; goto case "defensive";
            case "aggressive":
                if (want == null)
                {
                    float best = PetDb.R("aggroRange", 12);
                    foreach (var n in GetTree().GetNodesInGroup("enemies"))
                    {
                        if (n is not Enemy e || !Ok(e) || e.Def.Flee || e.Def.Realm != World.Realm && !e.AnyRealm) continue;
                        float d = Mathf.Min(e.GlobalPosition.DistanceTo(GlobalPosition), e.GlobalPosition.DistanceTo(hero)); if (d < best) { best = d; want = e; }
                    }
                }
                goto case "defensive";
            case "defensive":
                if (want == null && PetWorld.HeroFoe is Enemy hf && Ok(hf)) want = hf;
                if (want == null && _lastHitBy is Enemy lh && Ok(lh) && _hurtT < 10) want = lh;
                break;
        }
        if (S.Aggro != "passive" && want != null && (_foe == null || S.Aggro == "assist")) _foe = want;
        // auto-loot when not fighting
        if (_foe != null || S.Loot == 0) { _loot = null; return; }
        if (_loot != null && IsInstanceValid(_loot) && !_loot.IsQueuedForDeletion()) return;
        _loot = null; float lr = PetDb.R("lootRange", 15) + PetTree.Mod(S, "lootRange"), bl = lr;
        foreach (var n in GetTree().GetNodesInGroup("loot"))
        {
            if (n is not Loot l || l.Hold || l.IsQueuedForDeletion() || _skipLoot.Contains(l.GetInstanceId())) continue;
            if (!Wants(l)) continue;
            float d = l.GlobalPosition.DistanceTo(hero); if (d < bl) { bl = d; _loot = l; }
        }
    }
    bool Wants(Loot l) => S.Loot switch
    {
        1 => l.Item == null,
        2 => l.Item == null || l.Item.Rar >= S.LootRar || l.Item.Set != null || l.Item.Unique,
        3 => true,
        _ => false,
    };

    PetSkillDef? BestSkill(Enemy foe, out float range)
    {
        PetSkillDef? best = null; range = 2.4f; float score = -1;
        float hpF = MaxHp > 0 ? Hp / MaxHp : 1;
        foreach (var id in S.Skills)
        {
            var sk = PetDb.Skill(id); if (sk == null || _cd.GetValueOrDefault(id) > 0) continue;
            float s = sk.Kind switch
            {
                "attack" => sk.Power * (sk.Acc / 100f) + (sk.Effect != "" ? 6 : 0),
                "heal" => hpF < .5f ? 200 : -1,
                "shield" => hpF < .6f && _guardT <= 0 ? 150 : -1,
                "buff" => _buffT <= 0 ? 120 : -1,
                "debuff" => 90,
                _ => -1,
            };
            if (s > score) { score = s; best = sk; }
        }
        // range: the reach of the strongest attack ready (or a melee bite) so the pet closes in as needed
        if (best != null && best.Kind == "attack") range = best.Range; else if (best != null && best.Kind == "debuff") range = best.Range * .8f;
        else { range = 2.4f; foreach (var id in S.Skills) if (PetDb.Skill(id) is { Kind: "attack" } a) range = Mathf.Max(range, a.Range); }
        return best;
    }

    // ------------------------------------------------------------------ skills
    void Use(PetSkillDef sk, Enemy foe)
    {
        _cd[sk.Id] = sk.Cd * Mathf.Max(.3f, 1 + PetTree.Mod(S, "cdP") / 100f); var col = sk.Elem == "physical" ? new Color(1f, .85f, .7f) : Dmg.TypeColor(sk.Elem);
        switch (sk.Kind)
        {
            case "heal":
            {
                float a = MaxHp * sk.Power / 100f; Hp = Mathf.Min(MaxHp, Hp + a);
                FloatText.Spawn(this, GlobalPosition + Vector3.Up * 1.6f, "+" + Mathf.RoundToInt(a), new Color(.5f, 1f, .6f));
                var pl = Player.Instance; if (pl != null && pl.GlobalPosition.DistanceTo(GlobalPosition) < 12) pl.Hp = Mathf.Min(Game.D.MaxHp(), pl.Hp + Game.D.MaxHp() * .08f);
                Fx.Burst(GlobalPosition + Vector3.Up, new Color(.5f, 1f, .6f), 16, 3f, .6f, .1f, 2f, "magic"); Sfx.Play("heal", .7f); return;
            }
            case "shield": _guardT = sk.Dur; Fx.Ring(GlobalPosition + Vector3.Up * .1f, 1.6f, new Color(.7f, .85f, 1f), .5f); FloatText.Spawn(this, GlobalPosition + Vector3.Up * 1.6f, sk.Name, new Color(.7f, .85f, 1f), .7f); return;
            case "buff": _buffT = sk.Dur; _buffMul = sk.BuffMul; Fx.Ring(GlobalPosition + Vector3.Up * .1f, 2.4f, new Color(1f, .7f, .35f), .5f); Sfx.Play("roar", .5f, 1.4f); FloatText.Spawn(this, GlobalPosition + Vector3.Up * 1.6f, sk.Name, new Color(1f, .75f, .4f), .7f); return;
        }
        Body.PetSwing(.3f);
        if (sk.Fx == "bolt") { Bolt(foe, col, .32f); _pending = (foe, sk, .32f); }
        else _pending = (foe, sk, sk.Fx == "melee" ? .25f : .3f);
    }
    void Bolt(Enemy foe, Color col, float t)
    {
        var orb = new MeshInstance3D { Mesh = new SphereMesh { Radius = .16f, Height = .32f, RadialSegments = 8, Rings = 4 }, MaterialOverride = new StandardMaterial3D { AlbedoColor = col, EmissionEnabled = true, Emission = col, EmissionEnergyMultiplier = 3f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        Game.Root!.AddChild(orb); var a = GlobalPosition + Vector3.Up * Mathf.Max(.6f, Body.HeightM() * S.Sp().PetScale * .7f); orb.GlobalPosition = a;
        var b = foe.GlobalPosition + Vector3.Up * foe.HeightM() * .55f;
        var tw = orb.CreateTween(); tw.TweenProperty(orb, "global_position", b, t); tw.TweenCallback(Callable.From(orb.QueueFree));
    }
    /// <summary>The blow arrives: one foe (melee, bolt) or all in front (breath) / around (nova).</summary>
    void Land(Enemy foe, PetSkillDef sk)
    {
        if (_ko) return;
        var col = sk.Elem == "physical" ? new Color(1f, .85f, .7f) : Dmg.TypeColor(sk.Elem);
        var targets = new List<Enemy>();
        if (sk.Fx is "breath" or "nova")
        {
            var f = new Vector3(-Mathf.Sin(_yaw), 0, -Mathf.Cos(_yaw));
            foreach (var n in GetTree().GetNodesInGroup("enemies"))
            {
                if (n is not Enemy e || e.Dead || !e.Visible || e.CatchBusy || e.PetBody) continue; var d = e.GlobalPosition - GlobalPosition; d.Y = 0;
                if (d.Length() > sk.Range + e.Def.R) continue;
                if (sk.Fx == "breath" && d.Length() > .5f && d.Normalized().Dot(f) < .5f) continue;
                targets.Add(e);
            }
            if (sk.Fx == "nova") Fx.Ring(GlobalPosition + Vector3.Up * .1f, sk.Range, col, .45f);
            else for (int i = 1; i <= 5; i++) Fx.Burst(GlobalPosition + Vector3.Up + f * (sk.Range * i / 5f), col, 4, 2.5f, .4f, .14f, 1f, sk.Elem == "fire" ? "fire" : "magic");
        }
        else if (IsInstanceValid(foe) && !foe.Dead && foe.GlobalPosition.DistanceTo(GlobalPosition) < sk.Range + foe.Def.R * foe.ScaleF() + 2f) targets.Add(foe);
        if (sk.Kind == "debuff")
        {
            foreach (var e in targets) if (sk.Effect != "" && GD.Randf() < sk.Chance) { e.Status.Add(sk.Effect, sk.Turns, 0, 0); FloatText.Spawn(e, e.GlobalPosition + Vector3.Up * (e.HeightM() + .3f), Dmg.Status(sk.Effect)?.Name ?? sk.Effect, col, .6f); }
            Sfx.Play("roar", .5f, 1.2f); return;
        }
        float dmg = PetWorld.WorldDamage(this, sk) * _buffMul;
        foreach (var e in targets)
        {
            if (GD.Randf() * 100 > sk.Acc) { FloatText.Spawn(e, e.GlobalPosition + Vector3.Up * (e.HeightM() + .2f), "Miss", Dim, .6f); continue; }
            bool crit = GD.Randf() < .06f + PetTree.Mod(S, "critP") / 100f;
            float leech = PetTree.Mod(S, "leechP"); if (leech > 0) Hp = Mathf.Min(MaxHp, Hp + dmg * leech / 100f);
            var h = new HitInfo { Damage = dmg * (float)GD.RandRange(.9, 1.1) * (crit ? 1.6f : 1), From = GlobalPosition, Knock = sk.Fx == "nova" ? 4 : 2, IsPlayer = true, Attacker = this, Type = sk.Elem, Crit = crit, IsProjectile = sk.Fx == "bolt" };
            if (sk.Effect != "" && sk.Chance > 0) h.With(sk.Effect, sk.Chance);
            e.TakeHit(h);
            if (sk.Fx is "melee" or "bolt") Fx.Burst(e.GlobalPosition + Vector3.Up * e.HeightM() * .55f, col, 6, 3f, .3f, .08f);
        }
        if (targets.Count > 0) Sfx.Play(sk.Fx == "bolt" ? "zap" : "hit", .55f, 1.15f);
    }
    static readonly Color Dim = new(.75f, .75f, .8f);

    // ------------------------------------------------------------------ damage in
    public void TakeHit(HitInfo h)
    {
        if (_ko || !Visible) return;
        float def = S.Stats().Def * PetDb.World("defK", 1.5f);
        float dmg = h.Damage * 100f / (100f + def) * (_guardT > 0 ? .5f : 1f) * Mathf.Max(.3f, 1 + PetTree.Mod(S, "guardP") / 100f);
        int n = Mathf.Max(1, Mathf.RoundToInt(dmg)); Hp -= n; _hurtT = 0; _lastHitBy = h.Attacker;
        FloatText.Spawn(this, GlobalPosition + Vector3.Up * 1.5f, "-" + n, new Color(1f, .6f, .45f), .75f);
        Body.PetFlinch(h.From);
        if (h.Statuses != null) foreach (var (id, c, dm, pw) in h.Statuses) if (GD.Randf() < c) Body.Status.Add(id, dm, pw, h.Damage);
        if (Hp <= 0) KnockOut();
    }
    public void KnockOut()
    {
        if (_ko) return; _ko = true; Hp = 0; S.HpFrac = 0; S.KoT = PetDb.R("reviveTime", 60); _foe = null; _pending = null;
        Body.PetDown(true); CollisionMask = 1;
        Fx.Burst(GlobalPosition + Vector3.Up, PetDb.Cube(S.Cube).Col.Lerp(new Color(1f, .85f, .5f), .5f), 20, 3f, .8f, .1f, 2f, "magic");
        Ui.Note($"{S.Name()} is knocked out and returns to its cube ({S.KoT:0} s)", new Color(1f, .6f, .5f));
        Game.Notify();
    }
    public void Heal(float frac) { Hp = Mathf.Min(MaxHp, Hp + MaxHp * frac); }
    public void ClearFoe() { _foe = null; _loot = null; }
}

/// <summary>The active pet in the world: spawning it, knock-out timers, orders, experience, riding (Player.ToggleMount).</summary>
public static class PetWorld
{
    public static Pet? Out;
    public static bool Riding;                      // the hero rides the active pet (Player.Mounted with a pet body)
    public static Enemy? HeroFoe; static float _heroFoeT;

    public static PetState? Active { get { var d = Game.D; return d.ActivePet == "" ? null : d.Pets.Find(p => p.Id == d.ActivePet); } }
    public static bool CanMount => Active is { } a && a.Mountable() && !a.Ko();
    public static float MountSpeed => Active is { } a ? PetDb.Mount(a.Species, a.Level, a).Speed : 10.5f;
    /// <summary>The active pet is out and within rules.mountNear metres of the hero (riding starts from beside it).</summary>
    public static bool NearHero(Player p) => Out != null && GodotObject.IsInstanceValid(Out) && !Out.Dead && Out.Visible && Out.GlobalPosition.DistanceTo(p.GlobalPosition) <= PetDb.R("mountNear", 7);

    // ---- the rider's pet skill (A-19): while riding, the pet order key makes the mount use its strongest ready attack on your target
    static float _riderCd;
    public static void RiderSkill(Player p)
    {
        var a = Active; if (a == null || !Riding) return;
        if (_riderCd > 0) { Ui.Note($"{a.Name()} is not ready ({_riderCd:0.0} s)", new Color(.8f, .8f, .9f)); return; }
        PetSkillDef? best = null; foreach (var id in a.Skills) if (PetDb.Skill(id) is { Kind: "attack" } sk && (best == null || sk.Power > best.Power)) best = sk;
        if (best == null) { Ui.Note($"{a.Name()} knows no attack", new Color(.8f, .8f, .9f)); return; }
        float range = Mathf.Max(best.Range, 3.2f) + 1.5f; Enemy? foe = Targeting.Aim is Enemy t && GodotObject.IsInstanceValid(t) && !t.Dead ? t : p.AimPoint(range + 4).enemy;
        if (foe == null || foe.Dead || foe.PetBody || foe.GlobalPosition.DistanceTo(p.GlobalPosition) > range + foe.Def.R * foe.ScaleF())
        {   // nothing aimed at: the nearest foe in front
            foe = null; float bd = range; var f = p.Facing();
            foreach (var n in p.GetTree().GetNodesInGroup("enemies"))
            {
                if (n is not Enemy e || e.Dead || !e.Visible || !e.Active || e.PetBody || e.Def.Flee) continue; var to = e.GlobalPosition - p.GlobalPosition; to.Y = 0; float d = to.Length() - e.Def.R * e.ScaleF();
                if (d < bd && (d < 1.5f || to.Normalized().Dot(f) > .3f)) { bd = d; foe = e; }
            }
        }
        if (foe == null) { Ui.Note("No foe in reach", new Color(.8f, .8f, .9f)); return; }
        _riderCd = Mathf.Max(PetDb.R("mountSkillCd", 2.5f), best.Cd * (1 + PetTree.Mod(a, "cdP") / 100f));
        var col = best.Elem == "physical" ? new Color(1f, .85f, .7f) : Dmg.TypeColor(best.Elem);
        float dmg = WorldDamage(a, best); bool crit = GD.Randf() < .06f + PetTree.Mod(a, "critP") / 100f;
        var h = new HitInfo { Damage = dmg * (float)GD.RandRange(.9, 1.1) * (crit ? 1.6f : 1), From = p.GlobalPosition, Knock = 3, IsPlayer = true, Attacker = p, Type = best.Elem, Crit = crit, NoText = false };
        if (best.Effect != "" && best.Chance > 0) h.With(best.Effect, best.Chance);
        p.MountSwing(); foe.TakeHit(h);
        Fx.Burst(foe.GlobalPosition + Vector3.Up * foe.HeightM() * .55f, col, 8, 3f, .3f, .08f); Sfx.Play(best.Fx == "bolt" ? "zap" : "hit", .6f, 1.1f);
        FloatText.Spawn(p, p.GlobalPosition + Vector3.Up * 2.6f, best.Name, col, .7f);
    }
    static bool Allowed => !Arenas.Active;   // arena fights stay hero against monsters (Soul Duels bring pets in their own way)

    /// <summary>Every physics tick from Player: knock-out timers, bond, and keeping the right pet out.</summary>
    public static void Tick(Player p, float dt)
    {
        var d = Game.D;
        p.MountTick(dt); if (_riderCd > 0) _riderCd -= dt;   // riding: stamina, and the places and states that end a ride (PlayerMount.cs)
        if (!PetTrade.Open && (d.TradeEscrow.Count > 0 || d.TradeEscrowGold > 0)) PetTrade.RestoreEscrow();   // a trade cut off (crash, quit): its pets come home
        foreach (var pet in d.Pets) if (pet.KoT > 0) { pet.KoT -= dt; if (pet.KoT <= 0) { pet.KoT = 0; pet.HpFrac = 1; if (pet.Id == d.ActivePet) Ui.Note($"{pet.Name()} has recovered", new Color(.6f, 1f, .7f)); } }
        _heroFoeT -= dt; if (_heroFoeT <= 0) HeroFoe = null;
        var a = Active; bool valid = Out != null && GodotObject.IsInstanceValid(Out);
        if (a != null && valid && Out!.Visible && !Out.Dead) { a.Bond = Mathf.Min(100, a.Bond + dt / 60f * PetDb.R("bondPerMinute", .5f)); }
        bool want = a != null && !a.Ko() && !Riding && Allowed && !p.Dead && Game.Root != null;
        if (valid && (!want || Out!.S != a)) { if (!(Out!.Dead && Out.S == a && a!.Ko() && Out.Visible)) Despawn(); valid = Out != null && GodotObject.IsInstanceValid(Out); }
        if (want && !valid) Spawn(p, a!);
    }
    static void Spawn(Player p, PetState s)
    {
        var pet = new Pet { S = s, Name = "Pet" }; Game.Root!.AddChild(pet);
        var f = p.Facing(); var at = p.GlobalPosition - f * 2.5f + f.Cross(Vector3.Up) * 1.2f + Vector3.Up * .4f; pet.Teleport(at);
        Out = pet;
    }
    public static void Despawn() { if (Out != null && GodotObject.IsInstanceValid(Out)) Out.QueueFree(); Out = null; }
    /// <summary>Make this pet the active one (null id: none out).</summary>
    public static void SetActive(string id) { Game.D.ActivePet = id; Despawn(); Game.Notify(); }
    public static void HeroHitBy(Enemy e) { HeroFoe = e; _heroFoeT = 10f; }
    /// <summary>The order key: follow / stay (and calls a pet that is in its cube out again).</summary>
    public static void ToggleStay()
    {
        if (Riding && Player.Instance != null) { RiderSkill(Player.Instance); return; }   // riding: the key is the mount's attack
        var a = Active; if (a == null) { Ui.Note($"No pet. Throw a Soul Cube ({Inputs.KeyOf(Inputs.ThrowCube)}) at a weakened foe.", new Color(.8f, .75f, .9f)); return; }
        if (a.Ko()) { Ui.Note($"{a.Name()} is still in its cube ({a.KoT:0} s)", new Color(1f, .7f, .6f)); return; }
        a.Stay = !a.Stay; if (Out != null && GodotObject.IsInstanceValid(Out)) { Out.Teleport(Out.GlobalPosition); Out.ClearFoe(); }
        Ui.Note(a.Stay ? $"{a.Name()}: stay" : $"{a.Name()}: follow", new Color(.7f, 1f, .75f));
    }

    /// <summary>A skill's damage in the world: the pet's attack (or magic for elements) against the hero's own level of power.</summary>
    public static float WorldDamage(Pet pet, PetSkillDef sk) => WorldDamage(pet.S, sk);
    public static float WorldDamage(PetState s, PetSkillDef sk)
    {
        var st = s.Stats(); float stat = sk.Elem == "physical" ? st.Atk : Mathf.Max(st.Mag, st.Atk * .8f);
        float b = Mathf.Max(stat * PetDb.World("dmgMul", .9f), Game.D.WpnDmg() * PetDb.World("dmgFloor", .35f));
        return b * sk.Power / 45f * PetTree.SkillPower(s, sk.Id);   // (the pet's skill tree: power of all skills and of this one)
    }

    /// <summary>A monster died: the pet out nearby takes a share of its experience.</summary>
    public static void OnKill(Enemy e, int xp)
    {
        if (xp <= 0 || Out == null || !GodotObject.IsInstanceValid(Out) || Out.Dead) return;
        if (Out.GlobalPosition.DistanceTo(e.GlobalPosition) > PetDb.R("xpRange", 30)) return;
        int n = Mathf.Max(1, Mathf.RoundToInt(xp * PetDb.R("xpShare", .6f) * (1 + PetTree.Mod(Out.S, "xpP") / 100f)));
        var s = Out.S; int lv0 = s.Learned.Count; int ups = s.GainXp(n);
        if (ups > 0)
        {
            Out.Refresh(); Out.Heal(1);
            FloatText.Spawn(Out, Out.GlobalPosition + Vector3.Up * 2f, $"{s.Name()} Lv{s.Level}!", new Color(1f, .85f, .35f), 1.1f);
            Fx.Ring(Out.GlobalPosition, 2.5f, new Color(1f, .85f, .35f), .6f); Sfx.Play("level", .6f);
            string learnt = ""; for (int i = lv0; i < s.Learned.Count; i++) learnt += (learnt == "" ? "" : ", ") + (PetDb.Skill(s.Learned[i])?.Name ?? s.Learned[i]);
            Ui.Note($"{s.Name()} reached level {s.Level}" + (learnt != "" ? $" and learned {learnt}" : ""), new Color(1f, .85f, .4f));
            Game.Notify();
        }
    }
}
