using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>
/// The newer skill kinds (data/skills.json "kind"):
///  rush     - gap closer: dash to a foe, leaving a trail of fading afterimages, and strike it
///  boomerang- a spinning blade flies out and comes back, hitting on both legs
///  orbit    - blades or embers circle you, cutting what they touch
///  turret   - a sentry that fires at the nearest foe for a while
///  beam     - a ray from your hands toward the crosshair that burns everything along it
///  vortex   - a well that drags foes in, then implodes
///  mark     - a sigil on a foe that stores part of the damage it takes, then detonates for it (brands also spread)
///  wall     - a line of fire, ice or thorns across your aim
///  blinkto  - teleport to the aimed spot, with a burst where you left and where you land
///  trap     - a trap that springs when a foe steps on it
///  ward     - a shield that soaks damage, with a short buff
///  hook     - a chain that strikes the first foe in line and drags it to you
///  enchant  - an aura for the whole party: buffs, heals, shields, cleansing and immunities (sent to friends nearby)
/// Plus the hero's side of it: shields (Ward), heals over time and status immunities.
/// </summary>
public partial class Player
{
    // ---------------------------------------------------------------- shields, heals over time, immunity
    public float Ward, WardT; readonly List<(float pct, float t)> _hots = new(); MeshInstance3D? _wardFx;
    Enemy? _rushTgt; float _ghostT;

    /// <summary>Statuses the hero ignores: everything under Purifying Light, stuns, roots, freezes and chills under Unbreakable Will.</summary>
    bool ImmuneTo(string id) => Game.TS["statusImmune"] > 0 || (Game.TS["ccImmune"] > 0 && id is "stun" or "root" or "freeze" or "chill");

    void ExtraTick(float dt)
    {
        if (WardT > 0) { WardT -= dt; if (WardT <= 0) Ward = 0; }
        if (_wardFx != null) { _wardFx.Visible = Ward > 0; if (Ward > 0) _wardFx.Scale = Vector3.One * (1 + Mathf.Sin(Game.PlayTime * 4f) * .03f); }
        for (int i = _hots.Count - 1; i >= 0; i--)
        {
            var (pct, t) = _hots[i]; Hp = Mathf.Min(Game.D.MaxHp(), Hp + Game.D.MaxHp() * pct / 100f * HealIn * dt);
            t -= dt; if (t <= 0) _hots.RemoveAt(i); else _hots[i] = (pct, t);
        }
    }
    void ShowWard()
    {
        if (_wardFx == null)
        {
            _wardFx = new MeshInstance3D { Mesh = new SphereMesh { Radius = 1.1f, Height = 2.3f, RadialSegments = 24, Rings = 12 }, Position = Vector3.Up * 1f, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(.6f, .85f, 1f, .16f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add, CullMode = BaseMaterial3D.CullModeEnum.Disabled } };
            AddChild(_wardFx);
        }
        _wardFx.Visible = true;
    }

    // ---------------------------------------------------------------- execution of the newer kinds
    void ExecuteExtra(SkillCalc c, Enemy? forced, bool echo)
    {
        var k = c.Def; var col = Dmg.TypeColor(c.Type); var origin = MuzzlePos();
        switch (k.Kind)
        {
            case "rush":
            {
                var tgt = forced ?? Targeting.Aim ?? AimPoint(c.Range).enemy ?? NearestFoe(c.Range);
                if (tgt == null || !IsInstanceValid(tgt) || tgt.Dead || tgt.GlobalPosition.DistanceTo(GlobalPosition) > c.Range * 1.2f) { Ui.Note("No foe in reach to rush", new Color(.8f, .85f, 1f)); break; }
                var d = tgt.GlobalPosition - GlobalPosition; d.Y = 0; float stop = tgt.Def.R * tgt.ScaleF() + 1.1f, dist = Mathf.Max(0, d.Length() - stop);
                _moveFrom = GlobalPosition; _moveTo = Ground(GlobalPosition + d.Normalized() * dist) + Vector3.Up * .1f;
                _moveDur = Mathf.Clamp(dist / k.XOr("speed", 48f), .08f, .4f) * c.MoveMul; _moveT = _moveDur; _moveArc = .1f; _moveSkill = c; _rushTgt = tgt; _ghostT = 0;
                _modelYaw = Util.YawTo(d.X, d.Z); Sfx.Play("swing", .8f);
                break;
            }
            case "boomerang":
            {
                var dir = Flat(AimPoint(60f).point - origin); if (dir.LengthSquared() < .01f) dir = Facing(); dir = dir.Normalized();
                int n = Mathf.Max(1, c.Count); for (int i = 0; i < n; i++) { float a = n > 1 ? (i / (float)(n - 1) - .5f) * .6f : 0; var g = new Glaive { C = c, Dir = dir.Rotated(Vector3.Up, a), Range = c.Range, Col = col }; Game.Root!.AddChild(g); g.GlobalPosition = origin; }
                break;
            }
            case "orbit":
            {
                foreach (var n in GetTree().GetNodesInGroup("orbits")) if (n is OrbitRing o && o.C.Def.Id == k.Id) o.QueueFree();   // recasting renews it
                var r = new OrbitRing { C = c, Col = col }; Game.Root!.AddChild(r); r.GlobalPosition = GlobalPosition;
                break;
            }
            case "turret":
            {
                var at = c.At ?? Ground(GlobalPosition + Facing() * 2.5f);
                for (int i = 0; i < Mathf.Max(1, c.Count - (k.Count - 1)); i++) { var s = new Sentry { C = c, Col = col }; Game.Root!.AddChild(s); s.GlobalPosition = i == 0 ? at : Ground(Around(at, i, c.Count, 2.5f)); }
                break;
            }
            case "beam": { var b = new SkillBeam { C = c, Col = col }; Game.Root!.AddChild(b); break; }
            case "vortex":
            {
                var at = c.At ?? Ground(Aim(40f, forced).pt);
                for (int i = 0; i < Mathf.Max(1, c.Count); i++) { var v = new Vortex { C = c, Col = col }; Game.Root!.AddChild(v); v.GlobalPosition = i == 0 ? at : Ground(Around(at, i, c.Count, c.Radius * 2f)); }
                break;
            }
            case "mark":
            {
                var first = forced ?? Targeting.Aim ?? Aim(40f).e ?? NearestFoe(30f);
                if (first == null) { Ui.Note("No foe to mark", new Color(.8f, .85f, 1f)); break; }
                var seen = new HashSet<Enemy>(); var cur = first;
                for (int i = 0; i < Mathf.Max(1, c.Count) && cur != null; i++) { var m = new SkillMark { C = c, Target = cur, Col = col }; Game.Root!.AddChild(m); seen.Add(cur); cur = NearestFoe(18f, cur.GlobalPosition, seen); }
                break;
            }
            case "wall":
            {
                var at = c.At ?? Ground(GlobalPosition + Facing() * 8f); var fw = Facing(); var side = new Vector3(fw.Z, 0, -fw.X);
                int segs = 5 + 2 * Mathf.Max(0, c.Count - 1); float gap = c.Radius * 1.45f;
                for (int i = 0; i < segs; i++) { var z = new SkillZone { Calc = c, Col = col }; Game.Root!.AddChild(z); z.GlobalPosition = Ground(at + side * (i - (segs - 1) / 2f) * gap); }
                Sfx.Play(c.Type == "frost" ? "ice" : "fire");
                break;
            }
            case "blinkto":
            {
                var from = GlobalPosition; var to = c.At ?? AimGround(k);
                Vfx.Flash(from + Vector3.Up, col, 4f, 6f, .3f); Vfx.Ring(from + Vector3.Up * .1f, c.Radius, col, .3f); AreaHit(c, from, c.Radius, k.XOr("start", .5f), true);
                GlobalPosition = to + Vector3.Up * .2f; Velocity = Vector3.Zero; _inv = Mathf.Max(_inv, .3f);
                Vfx.Flash(to + Vector3.Up, col, 5f, 8f, .35f); Vfx.Ring(to + Vector3.Up * .1f, c.Radius, col, .35f); AreaHit(c, to, c.Radius, k.XOr("end", 1f), true);
                if (c.Type == "lightning") Vfx.Lightning(from + Vector3.Up, to + Vector3.Up, col, .3f);
                Rig?.Shake(.12f);
                break;
            }
            case "trap":
            {
                var at = c.At ?? Ground(GlobalPosition + Facing() * 1.6f);
                for (int i = 0; i < Mathf.Max(1, c.Count); i++) { var t = new SkillTrap { C = c, Col = col }; Game.Root!.AddChild(t); t.GlobalPosition = i == 0 ? at : Ground(Around(at, i, c.Count, 2.2f)); }
                break;
            }
            case "ward":
            {
                Ward = Mathf.Max(Ward, Game.D.MaxHp() * c.Heal); WardT = c.Dur; ShowWard();
                if (k.Buff.Count > 0) { Buffs.RemoveAll(b => b.id == k.Id); Buffs.Add((k.Id, new Dictionary<string, float>(k.Buff), c.Dur)); Game.RecalcTree(); }
                Vfx.Ring(GlobalPosition + Vector3.Up * .1f, 2.5f, col.Lerp(new Color(.7f, .9f, 1f), .5f), .4f); FloatText.Spawn(this, GlobalPosition + new Vector3(0, 2.4f, 0), $"{k.Name} ({Mathf.RoundToInt(Ward)})", new Color(.7f, .9f, 1f), .8f);
                if (c.Burst) Burst(c, GlobalPosition);
                break;
            }
            case "hook":
            {
                var from = origin; var aim = AimPoint(c.Range).point; var dir = Flat(aim - from); if (dir.LengthSquared() < .01f) dir = Facing(); dir = dir.Normalized();
                Enemy? first = null; float fd = 1e9f;
                foreach (var n in GetTree().GetNodesInGroup("enemies"))
                {
                    if (n is not Enemy e || e.Dead || !e.Active) continue; var rel = Flat(e.GlobalPosition - from); float along = rel.Dot(dir);
                    if (along < 0 || along > c.Range) continue; if ((rel - dir * along).Length() > 1.3f + e.Def.R * e.ScaleF()) continue;
                    if (along < fd) { fd = along; first = e; }
                }
                var end = first != null ? first.GlobalPosition + Vector3.Up * first.HeightM() * .5f : from + dir * c.Range;
                Vfx.Lightning(from, end, new Color(.75f, .7f, .65f), .35f); Vfx.Lightning(from, end, col, .25f);
                if (first != null)
                {
                    first.TakeHit(c.Hit(c.Dmg, from, Crit(c)));
                    if (!first.Def.Boss && !first.Puppet && !first.Dead)
                    {
                        var to = GlobalPosition + dir * (1.4f + first.Def.R); to.Y = first.GlobalPosition.Y;
                        first.CreateTween().TweenProperty(first, "global_position", to, .22f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
                    }
                    Sfx.Play("hit");
                }
                break;
            }
            case "enchant": Enchant(c); break;
        }
    }
    static Vector3 Flat(Vector3 v) => new(v.X, 0, v.Z);

    // ---------------------------------------------------------------- the gap closer
    void AfterimageTick(float dt)
    {
        _ghostT -= dt; if (_ghostT > 0) return; _ghostT = .035f;
        Node3D? src = _av != null ? _av : _model; if (src != null) Afterimage.Spawn(src, _moveSkill != null ? Dmg.TypeColor(_moveSkill.Type).Lerp(new Color(.55f, .75f, 1f), .5f) : new Color(.55f, .75f, 1f));
    }
    void RushLand(SkillCalc c)
    {
        var t = _rushTgt; _rushTgt = null;
        if (t == null || !IsInstanceValid(t) || t.Dead) return;
        var d = t.GlobalPosition - GlobalPosition; _modelYaw = Util.YawTo(d.X, d.Z);
        t.TakeHit(c.Hit(c.Dmg, GlobalPosition, Crit(c))); Rig?.Shake(.12f); Sfx.Play("hit");
        SpellFx.Skill(c.Def.Id, "hit", t.GlobalPosition + Vector3.Up * t.HeightM() * .55f);
        Vfx.Sparks(t.GlobalPosition + Vector3.Up * t.HeightM() * .5f, Ramp(new Color(.6f, .8f, 1f)), 16, 4f, .35f, .5f, 0f, true, 120f, null, .2f);
        for (int i = 1; i < c.Count; i++) GetTree().CreateTimer(.1f * i).Timeout += () => { if (IsInstanceValid(t) && !t.Dead) t.TakeHit(c.Hit(c.Dmg * .5f, GlobalPosition, Crit(c))); };
        if (c.Echo > 0) GetTree().CreateTimer(.3f).Timeout += () => { if (IsInstanceValid(t) && !t.Dead) t.TakeHit(c.Hit(c.Dmg, GlobalPosition, Crit(c))); };
        if (c.Chain > 0) ChainFrom(c, t.GlobalPosition, c.Chain);
    }

    // ---------------------------------------------------------------- enchantments (party auras)
    void Enchant(SkillCalc c)
    {
        var k = c.Def; float pot = 1 + Skills.RankP * ((Game.D.Skills.GetValueOrDefault(k.Id)?.Rank ?? 1) - 1) / 100f;
        ApplyEnchant(k.Id, pot, c.Dur, Game.D.Name); int n = 1;
        if (Net.Active && Net.I != null)
        {
            var party = Net.I.MyParty;
            foreach (var kv in Net.I.Heroes)
            {
                var h = kv.Value; if (!IsInstanceValid(h) || !h.HasPose || h.Dead || h.Zone != Net.MyZone) continue;
                if (party != null && !party.Members.Contains(kv.Key)) continue;
                if (Flat(h.GlobalPosition - GlobalPosition).Length() > c.Radius) continue;
                Net.I.SendEnchant(kv.Key, k.Id, pot, c.Dur); n++;
            }
        }
        var col = new Color(1f, .85f, .45f).Lerp(Dmg.TypeColor(c.Type), .4f);
        Vfx.Ring(GlobalPosition + Vector3.Up * .15f, c.Radius, col, .7f); Vfx.Flash(GlobalPosition + Vector3.Up * 2f, col, 4f, c.Radius, .5f);
        SpellFx.Skill(k.Id, "ground", GlobalPosition, FxFit(k.Id, "ground", c.Radius));
        if (n > 1) Ui.Note($"{k.Name} reaches {n - 1} {(n == 2 ? "ally" : "allies")}", col);
    }
    /// <summary>An enchantment lands on our hero (cast by us or by a friend): buffs scaled by the caster's rank, heal, shield, cleanse, heal over time.</summary>
    public void ApplyEnchant(string id, float pot, float dur, string from)
    {
        var k = Skills.Get(id); if (k == null || Dead) return;
        if (k.Buff.Count > 0)
        {
            var mods = new Dictionary<string, float>(); foreach (var kv in k.Buff) mods[kv.Key] = kv.Key is "statusImmune" or "ccImmune" ? kv.Value : kv.Value * pot;
            Buffs.RemoveAll(b => b.id == id); Buffs.Add((id, mods, dur)); Game.RecalcTree();
        }
        if (k.Heal > 0) { float a = Game.D.MaxHp() * k.Heal * pot * HealIn; Hp = Mathf.Min(Game.D.MaxHp(), Hp + a); FloatText.Spawn(this, GlobalPosition + new Vector3(0, 2.2f, 0), "+" + Mathf.RoundToInt(a), new Color(.5f, 1f, .6f), .8f); }
        if (k.X.TryGetValue("ward", out var w)) { Ward = Mathf.Max(Ward, Game.D.MaxHp() * w * pot); WardT = Mathf.Max(WardT, dur); ShowWard(); }
        if (k.X.ContainsKey("cleanse")) Statuses.Clear();
        if (k.X.TryGetValue("hot", out var hot)) _hots.Add((hot * pot, dur));
        Vfx.Ring(GlobalPosition + Vector3.Up * .1f, 1.8f, new Color(1f, .9f, .55f), .4f);
        FloatText.Spawn(this, GlobalPosition + new Vector3(0, 2.6f, 0), k.Name + (from != Game.D.Name ? $" ({from})" : ""), new Color(1f, .88f, .5f), .7f);
    }
}

/// <summary>Helpers shared by the skill objects below.</summary>
static class SkillHits
{
    public static IEnumerable<Enemy> Near(SceneTree tree, Vector3 at, float r)
    {
        foreach (var n in tree.GetNodesInGroup("enemies"))
            if (n is Enemy e && !e.Dead && e.Active && new Vector2(e.GlobalPosition.X - at.X, e.GlobalPosition.Z - at.Z).Length() - e.Def.R * e.ScaleF() < r && Mathf.Abs(e.GlobalPosition.Y + e.HeightM() * .5f - at.Y) < 4f + e.HeightM() * .5f)
                yield return e;
    }
    public static void Hit(SkillCalc c, Enemy e, Vector3 from, float mul)
    {
        bool cr = c.Def.Crit || GD.Randf() < Game.D.CritChance() + c.CritC / 100f;
        e.TakeHit(c.Hit(c.Dmg * mul, from, cr));
    }
    public static StandardMaterial3D Glow(Color c, float e = 2f, float alpha = 1f) => new()
    {
        AlbedoColor = new Color(c, alpha), EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = e, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = alpha < 1 ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };
}

/// <summary>A frozen, glowing copy of the hero's pose that fades away (Phantom Rush).</summary>
public static class Afterimage
{
    static Shader? _sh;
    public static void Spawn(Node3D src, Color col)
    {
        if (Game.Root == null || !GodotObject.IsInstanceValid(src)) return;
        var g = (Node3D)src.Duplicate(0);   // no scripts: a plain copy of the body in its current pose
        void Strip(Node n) { foreach (var c in n.GetChildren()) { if (c is AnimationPlayer or AnimationTree) { n.RemoveChild(c); c.Free(); continue; } Strip(c); } }
        Strip(g);
        _sh ??= new Shader { Code = @"shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_back, shadows_disabled;
uniform vec4 col : source_color = vec4(.55, .75, 1.0, 1.0);
uniform float alpha = .6;
void fragment() { float f = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 1.6); ALBEDO = col.rgb * (.25 + 1.2 * f) * alpha; }" };
        var mat = new ShaderMaterial { Shader = _sh }; mat.SetShaderParameter("col", col);
        void Paint(Node n) { foreach (var c in n.GetChildren()) { if (c is GeometryInstance3D gi) { gi.MaterialOverride = mat; gi.MaterialOverlay = null; gi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off; } Paint(c); } }
        Paint(g);
        Game.Root.AddChild(g); g.GlobalTransform = src.GlobalTransform;
        var tw = g.CreateTween(); tw.TweenMethod(Callable.From<float>(a => mat.SetShaderParameter("alpha", a)), .6f, 0f, .45f); tw.TweenCallback(Callable.From(g.QueueFree));
    }
}

/// <summary>Boomerang blade: out to its range, then back to the hero; each foe is cut once each way.</summary>
public partial class Glaive : Node3D
{
    public SkillCalc C = null!; public Vector3 Dir; public float Range = 18f; public Color Col;
    float _d, _life; bool _back; readonly HashSet<Enemy> _hit = new(); Node3D _spin = null!;
    public override void _Ready()
    {
        _spin = new Node3D(); AddChild(_spin);
        _spin.AddChild(new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = .3f, OuterRadius = .62f, Rings = 24, RingSegments = 6 }, MaterialOverride = SkillHits.Glow(Col, 1.8f), Scale = new Vector3(1, .25f, 1) });
        for (int i = 0; i < 3; i++) _spin.AddChild(new MeshInstance3D { Mesh = new PrismMesh { Size = new Vector3(.25f, .5f, .05f) }, MaterialOverride = SkillHits.Glow(Col.Lightened(.3f), 2.4f), Position = new Vector3(Mathf.Cos(i * 2.1f) * .7f, 0, Mathf.Sin(i * 2.1f) * .7f), Rotation = new Vector3(Mathf.Pi / 2, -i * 2.1f, 0) });
        AddChild(new OmniLight3D { LightColor = Col, OmniRange = 4f, LightEnergy = 1f, ShadowEnabled = false });
    }
    public override void _Process(double delta)
    {
        float dt = (float)delta; _life += dt; var p = Player.Instance; if (p == null || _life > 6f) { QueueFree(); return; }
        float spd = Mathf.Max(10f, C.Speed);
        if (!_back) { GlobalPosition += Dir * spd * dt; _d += spd * dt; if (_d >= Range) { _back = true; _hit.Clear(); } }
        else { var to = p.GlobalPosition + Vector3.Up * 1.1f - GlobalPosition; if (to.Length() < 1.2f) { QueueFree(); return; } GlobalPosition += to.Normalized() * spd * 1.2f * dt; }
        _spin.RotateY(dt * 20f);
        foreach (var e in SkillHits.Near(GetTree(), GlobalPosition, 1.1f)) if (_hit.Add(e)) SkillHits.Hit(C, e, GlobalPosition, _back ? .8f : 1f);
    }
}

/// <summary>Blades or embers that circle the hero for the skill's duration; each cuts a foe at most every half second.</summary>
public partial class OrbitRing : Node3D
{
    public SkillCalc C = null!; public Color Col; float _t; readonly List<Node3D> _orbs = new(); readonly Dictionary<Enemy, float> _cd = new();
    public override void _Ready()
    {
        AddToGroup("orbits");
        int n = Mathf.Max(2, C.Count);
        for (int i = 0; i < n; i++)
        {
            var o = new Node3D(); AddChild(o); _orbs.Add(o);
            if (C.Type == "physical") o.AddChild(new MeshInstance3D { Mesh = new PrismMesh { Size = new Vector3(.18f, .9f, .05f) }, MaterialOverride = SkillHits.Glow(new Color(.85f, .9f, 1f), 1.2f), Rotation = new Vector3(0, 0, Mathf.Pi / 2) });
            else o.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = .28f, Height = .56f, RadialSegments = 12, Rings = 6 }, MaterialOverride = SkillHits.Glow(Col, 3f) });
            if (i == 0) o.AddChild(new OmniLight3D { LightColor = Col, OmniRange = 4f, LightEnergy = .9f, ShadowEnabled = false });
        }
    }
    public override void _Process(double delta)
    {
        float dt = (float)delta; _t += dt; var p = Player.Instance; if (p == null || p.Dead) { QueueFree(); return; }
        GlobalPosition = p.GlobalPosition + Vector3.Up * 1.1f;
        float r = Mathf.Max(2f, C.Radius), spin = C.Def.XOr("spin", 3.2f);
        foreach (var k in new List<Enemy>(_cd.Keys)) { _cd[k] -= dt; if (_cd[k] <= 0) _cd.Remove(k); }
        for (int i = 0; i < _orbs.Count; i++)
        {
            float a = _t * spin + i * Mathf.Tau / _orbs.Count; var o = _orbs[i];
            o.Position = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(_t * 2f + i) * .25f, Mathf.Sin(a) * r); o.Rotation = new Vector3(0, -a, 0);
            foreach (var e in SkillHits.Near(GetTree(), o.GlobalPosition, .9f)) if (!_cd.ContainsKey(e)) { _cd[e] = .5f; SkillHits.Hit(C, e, o.GlobalPosition, 1f); }
        }
        if (_t > C.Dur) { Scale = Scale.Lerp(Vector3.Zero, .25f); if (_t > C.Dur + .4f) QueueFree(); }
    }
}

/// <summary>A sentry that fires at the nearest foe in range every tick while it lasts.</summary>
public partial class Sentry : Node3D
{
    public SkillCalc C = null!; public Color Col; float _t, _fire; Node3D _top = null!;
    public override void _Ready()
    {
        var stone = new StandardMaterial3D { AlbedoColor = new Color(.3f, .3f, .34f), Roughness = .8f };
        AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = .15f, BottomRadius = .45f, Height = 1.3f, RadialSegments = 6 }, MaterialOverride = stone, Position = Vector3.Up * .65f });
        _top = new Node3D { Position = Vector3.Up * 1.8f }; AddChild(_top);
        _top.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = .32f, Height = .64f, RadialSegments = 4, Rings = 2 }, MaterialOverride = SkillHits.Glow(Col, 2.5f) });
        _top.AddChild(new OmniLight3D { LightColor = Col, OmniRange = 5f, LightEnergy = 1.2f, ShadowEnabled = false });
        Vfx.Ring(GlobalPosition + Vector3.Up * .1f, 1.5f, Col, .3f);
    }
    public override void _Process(double delta)
    {
        float dt = (float)delta; _t += dt; _fire -= dt; _top.RotateY(dt * 3f); _top.Position = Vector3.Up * (1.8f + Mathf.Sin(_t * 3f) * .08f);
        if (_t > C.Dur) { Scale = Scale.Lerp(Vector3.Zero, .2f); if (_t > C.Dur + .4f) QueueFree(); return; }
        if (_fire > 0) return; _fire = Mathf.Max(.15f, C.Tick);
        Enemy? best = null; float bd = C.Range;
        foreach (var n in GetTree().GetNodesInGroup("enemies")) if (n is Enemy e && !e.Dead && e.Active) { float d = e.GlobalPosition.DistanceTo(GlobalPosition); if (d < bd) { bd = d; best = e; } }
        if (best == null) return;
        var from = _top.GlobalPosition; var dir = (best.GlobalPosition + Vector3.Up * best.HeightM() * .5f - from).Normalized(); int n2 = Mathf.Max(1, (int)C.Def.XOr("volley", 1));
        for (int i = 0; i < n2; i++)
        {
            var d = dir.Rotated(Vector3.Up, n2 > 1 ? (i / (float)(n2 - 1) - .5f) * .35f : 0);
            var p = Projectile.Spawn(C.Type == "fire" ? "fire" : "bolt", 'p', from, d * C.Speed, C.Dmg, 1.6f, GD.Randf() < Game.D.CritChance() + C.CritC / 100f, false, 0f, false);
            p.Calc = C; p.Tint = Col; p.PierceLeft = C.Pierce;
        }
        if (C.Type == "lightning") Vfx.Lightning(from, from + dir * 1.5f, Col, .1f);
    }
}

/// <summary>A ray from the hero toward the crosshair for the skill's duration, burning everything along it each tick.</summary>
public partial class SkillBeam : Node3D
{
    public SkillCalc C = null!; public Color Col; float _t, _tick; MeshInstance3D _m = null!, _core = null!; OmniLight3D _l = null!;
    public override void _Ready()
    {
        _m = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = .5f, BottomRadius = .5f, Height = 1f, RadialSegments = 12 }, MaterialOverride = SkillHits.Glow(Col, 2.5f, .45f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off }; AddChild(_m);
        _core = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = .5f, BottomRadius = .5f, Height = 1f, RadialSegments = 8 }, MaterialOverride = SkillHits.Glow(Colors.White, 3f, .85f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off }; AddChild(_core);
        _l = new OmniLight3D { LightColor = Col, OmniRange = 8f, LightEnergy = 2f, ShadowEnabled = false }; AddChild(_l);
    }
    public override void _Process(double delta)
    {
        float dt = (float)delta; _t += dt; var p = Player.Instance;
        if (p == null || p.Dead || _t > C.Dur) { QueueFree(); return; }
        float range = C.Range > 3 ? C.Range : 28f;
        var from = p.GlobalPosition + Vector3.Up * 1.35f + p.Facing() * .5f; var to = p.AimPoint(range).point; var dir = to - from;
        if (dir.Length() < 1f) dir = p.Facing(); float len = Mathf.Min(range, Mathf.Max(1f, dir.Length())); dir = dir.Normalized();
        var z = Mathf.Abs(dir.Y) > .95f ? Vector3.Right : Vector3.Up.Cross(dir).Normalized(); var x = dir.Cross(z).Normalized(); z = x.Cross(dir).Normalized();
        float w = (.32f + .08f * Mathf.Sin(_t * 30f)) * (1 + .3f * (C.Radius > 0 ? C.Radius - 1 : 0)), fade = Mathf.Clamp((C.Dur - _t) * 4f, 0, 1);
        var mid = from + dir * len / 2;
        _m.GlobalTransform = new Transform3D(new Basis(x * w * fade, dir * len, z * w * fade), mid); _core.GlobalTransform = new Transform3D(new Basis(x * w * .35f * fade, dir * len, z * w * .35f * fade), mid);
        _l.GlobalPosition = from + dir * Mathf.Min(len, 3f);
        _tick -= dt; if (_tick > 0) return; _tick = Mathf.Max(.1f, C.Tick);
        foreach (var n in GetTree().GetNodesInGroup("enemies"))
        {
            if (n is not Enemy e || e.Dead || !e.Active) continue; var c = e.GlobalPosition + Vector3.Up * e.HeightM() * .5f; float along = (c - from).Dot(dir);
            if (along < 0 || along > len) continue; if ((c - (from + dir * along)).Length() < .9f + e.Def.R * e.ScaleF() + e.HeightM() * .3f) SkillHits.Hit(C, e, from, 1f);
        }
        Fx.Burst(from + dir * len, Col, 4, 3f, .3f, .08f);
    }
}

/// <summary>A well that drags foes in (not bosses), grinds them, and implodes at the end.</summary>
public partial class Vortex : Node3D
{
    public SkillCalc C = null!; public Color Col; float _t, _tick; MeshInstance3D _core = null!; CpuParticles3D _fx = null!;
    public override void _Ready()
    {
        _core = new MeshInstance3D { Mesh = new SphereMesh { Radius = .8f, Height = 1.6f, RadialSegments = 16, Rings = 8 }, MaterialOverride = SkillHits.Glow(Col.Darkened(.3f), 1.5f, .8f), Position = Vector3.Up * 1.2f }; AddChild(_core);
        var g = new Gradient(); g.SetColor(0, new Color(Col, 0)); g.AddPoint(.4f, new Color(Col, .9f)); g.SetColor(1, new Color(Col, 0));
        _fx = new CpuParticles3D { Amount = 60, Lifetime = .9f, EmissionShape = CpuParticles3D.EmissionShapeEnum.Ring, EmissionRingRadius = C.Radius * 1.5f, EmissionRingInnerRadius = C.Radius, EmissionRingHeight = .3f, EmissionRingAxis = Vector3.Up,
            RadialAccelMin = -C.Radius * 3f, RadialAccelMax = -C.Radius * 3f, TangentialAccelMin = 14f, TangentialAccelMax = 14f, Gravity = Vector3.Zero, Position = Vector3.Up * .8f, ColorRamp = g, Mesh = new QuadMesh { Size = new Vector2(.3f, .3f), Material = Vfx.SpriteMat(true, "magic") } };
        AddChild(_fx); AddChild(new OmniLight3D { LightColor = Col, OmniRange = C.Radius * 2.5f, LightEnergy = 1.5f, Position = Vector3.Up * 1.5f, ShadowEnabled = false });
    }
    public override void _Process(double delta)
    {
        float dt = (float)delta; _t += dt; float dur = Mathf.Max(1f, C.Dur);
        _core.Scale = Vector3.One * (1 + _t / dur * .8f + Mathf.Sin(_t * 12f) * .05f); _core.RotateY(dt * 6f);
        float pull = C.Def.XOr("pull", 7f);
        foreach (var e in SkillHits.Near(GetTree(), GlobalPosition, C.Radius * 1.8f))
        {
            if (e.Def.Boss || e.Puppet) continue; var to = GlobalPosition - e.GlobalPosition; to.Y = 0;
            if (to.Length() > .9f) e.GlobalPosition += to.Normalized() * Mathf.Min(to.Length() - .8f, pull * dt);
        }
        _tick -= dt; if (_tick <= 0) { _tick = .5f; foreach (var e in SkillHits.Near(GetTree(), GlobalPosition, C.Radius)) SkillHits.Hit(C, e, GlobalPosition, C.Def.XOr("grind", .15f)); }
        if (_t < dur) return;
        Vfx.Flash(GlobalPosition + Vector3.Up, Col, 6f, C.Radius * 3f, .4f); Vfx.Ring(GlobalPosition + Vector3.Up * .1f, C.Radius * 1.4f, Col, .4f); Player.Instance?.Rig?.Shake(.2f);
        foreach (var e in SkillHits.Near(GetTree(), GlobalPosition, C.Radius * 1.3f)) SkillHits.Hit(C, e, GlobalPosition, 1f);
        QueueFree();
    }
}

/// <summary>A sigil over a foe: when it runs out (or the foe dies) it detonates for the skill's damage plus a share of the damage the foe took meanwhile.</summary>
public partial class SkillMark : Node3D
{
    public SkillCalc C = null!; public Enemy Target = null!; public Color Col; float _t, _hp0; Vector3 _last;
    public override void _Ready()
    {
        _hp0 = Target.Hp;
        AddChild(new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = .45f, OuterRadius = .6f, Rings = 24, RingSegments = 4 }, MaterialOverride = SkillHits.Glow(Col, 2.5f) });
        AddChild(new MeshInstance3D { Mesh = new PrismMesh { Size = new Vector3(.4f, .4f, .05f) }, MaterialOverride = SkillHits.Glow(Col.Lightened(.3f), 3f), Rotation = new Vector3(Mathf.Pi, 0, 0) });
        FloatText.Spawn(Target, Target.GlobalPosition + Vector3.Up * (Target.HeightM() + .8f), "Marked", Col, .6f);
    }
    public override void _Process(double delta)
    {
        _t += (float)delta; bool ok = IsInstanceValid(Target);
        if (ok) { _last = Target.GlobalPosition; GlobalPosition = _last + Vector3.Up * (Target.HeightM() + .7f + Mathf.Sin(_t * 4f) * .08f); }
        RotateY((float)delta * 3f); Scale = Vector3.One * (1 + _t / Mathf.Max(.1f, C.Dur) * .6f);
        if (ok && !Target.Dead && _t < C.Dur) return;
        float stored = Mathf.Max(0, _hp0 - (ok ? Mathf.Max(0, Target.Hp) : 0)) * C.Def.XOr("store", .4f);
        if (ok && !Target.Dead) Target.TakeHit(C.Hit(C.Dmg + stored, _last, GD.Randf() < Game.D.CritChance() + C.CritC / 100f));
        float spread = C.Def.XOr("spread", 0);
        if (spread > 0) foreach (var e in SkillHits.Near(GetTree(), _last, Mathf.Max(3f, C.Radius))) if (e != Target) SkillHits.Hit(C, e, _last, spread);
        if (C.Type == "fire") Vfx.Explosion(_last + Vector3.Up * .8f, Mathf.Max(2f, C.Radius)); else { Vfx.Flash(_last + Vector3.Up, Col, 5f, 8f, .35f); Vfx.Ring(_last + Vector3.Up * .1f, Mathf.Max(2f, C.Radius), Col, .35f); }
        QueueFree();
    }
}

/// <summary>A trap on the ground: armed after a moment, it springs on the first foe to step in, hitting everything around.</summary>
public partial class SkillTrap : Node3D
{
    public SkillCalc C = null!; public Color Col; float _t; bool _sprung; Node3D _jaws = null!; MeshInstance3D _glow = null!;
    public override void _Ready()
    {
        var iron = new StandardMaterial3D { AlbedoColor = new Color(.3f, .29f, .28f), Metallic = .7f, Roughness = .45f };
        AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = .55f, BottomRadius = .6f, Height = .06f, RadialSegments = 16 }, MaterialOverride = iron, Position = Vector3.Up * .03f });
        _jaws = new Node3D { Position = Vector3.Up * .08f }; AddChild(_jaws);
        for (int i = 0; i < 10; i++) { float a = i / 10f * Mathf.Tau; _jaws.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0, BottomRadius = .05f, Height = .22f, RadialSegments = 4 }, MaterialOverride = iron, Position = new Vector3(Mathf.Cos(a) * .5f, .1f, Mathf.Sin(a) * .5f), Rotation = new Vector3(Mathf.Sin(a) * .5f, 0, -Mathf.Cos(a) * .5f) }); }
        _glow = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = C.Radius * .92f, OuterRadius = C.Radius, Rings = 32, RingSegments = 3 }, MaterialOverride = SkillHits.Glow(Col, 1.5f, .5f), Position = Vector3.Up * .05f, Visible = false }; AddChild(_glow);
    }
    public override void _Process(double delta)
    {
        _t += (float)delta; if (_sprung) return;
        if (_t > Mathf.Max(5f, C.Dur)) { QueueFree(); return; }
        if (_t < .7f) return; _glow.Visible = true;
        foreach (var _ in SkillHits.Near(GetTree(), GlobalPosition, C.Radius)) { Spring(); return; }
    }
    void Spring()
    {
        _sprung = true; Sfx.Play("block");
        foreach (var e in SkillHits.Near(GetTree(), GlobalPosition, C.Radius * 1.5f)) SkillHits.Hit(C, e, GlobalPosition, 1f);
        _jaws.CreateTween().TweenProperty(_jaws, "scale", new Vector3(.3f, 1.6f, .3f), .08f);
        if (C.Type == "fire") Vfx.Explosion(GlobalPosition + Vector3.Up * .4f, C.Radius * 1.5f); else { Vfx.Flash(GlobalPosition + Vector3.Up * .5f, Col, 4f, C.Radius * 3f, .3f); Vfx.Ring(GlobalPosition + Vector3.Up * .1f, C.Radius * 1.5f, Col, .3f); }
        GetTree().CreateTimer(.6f).Timeout += QueueFree;
    }
}
