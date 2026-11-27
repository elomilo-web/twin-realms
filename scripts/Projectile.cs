using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>Arrows, magic bolts and fireballs. Owner 'p' hits enemies, 'e' hits the player.</summary>
public partial class Projectile : Node3D
{
    public string Kind = "bolt";      // arrow, bolt, fire, ebolt
    public char Own = 'p';
    public Vector3 Vel;
    public float Life = 2f, Dmg = 10f, Aoe = 0f;
    public bool Crit, Pierce, Big;
    public HashSet<Enemy> Hits = new();
    // skill projectiles: resolved skill (type, statuses, leech), pierce and chain counts, homing, colour
    public SkillCalc? Calc; public int PierceLeft, ChainLeft; public bool Homing; public Color? Tint; public Enemy? Source;
    public System.Action<Vector3>? OnEnd;   // crafted spells: the second section goes off where the missile ends (PlayerSpellcraft.cs)
    public List<(string id, float chance)>? Statuses;   // basic-attack chain step statuses (data/attacks.json)
    public string Wpn = "";   // a basic-attack shot: the weapon type whose hit / miss sounds it plays (data/weapons.json "sfx")
    HitInfo HitFor(Vector3 from)
    {
        var h = Calc != null ? Calc.Hit(Dmg, from, Crit) : new HitInfo { Damage = Dmg, From = from, Knock = 2.2f, Crit = Crit, Attacker = Player.Instance, IsPlayer = true };
        if (Statuses != null) foreach (var (id, ch) in Statuses) h.With(id, ch);
        return h;
    }

    public static Projectile Spawn(string kind, char own, Vector3 pos, Vector3 vel, float dmg, float life, bool crit = false, bool pierce = false, float aoe = 0f, bool big = false, Color? tint = null)
    {
        var p = new Projectile { Kind = kind, Own = own, Vel = vel, Dmg = dmg, Life = life, Crit = crit, Pierce = pierce, Aoe = aoe, Big = big, Tint = tint };   // tint before _Ready: it colours the visuals
        if (own == 'p') SpawnedByPlayer++;
        Game.Root!.AddChild(p); p.GlobalPosition = pos;
        // everyone in the zone sees it fly: the overworld (host's foes) or our party dungeon (the owner's foes)
        if (own != 'x' && Net.Active && ((Net.MyZone == 0 && (own == 'p' || Net.IsHost)) || (Net.MyZone >= 1000 && (own == 'p' || Dungeon.IsOwner)))) Net.I!.Shot(kind, pos, vel, life, big, aoe);
        return p;
    }
    public static int SpawnedByPlayer;   // count for tests
    public bool NoNet;   // Own 'x': a picture of someone else's shot; it hits nothing

    public override void _Ready()
    { AddToGroup("projectiles");
        Color c = Tint ?? Kind switch { "arrow" => new Color(0.85f, 0.8f, 0.6f), "fire" => new Color(1f, 0.55f, 0.15f), "ebolt" => new Color(0.8f, 0.3f, 1f), _ => new Color(0.55f, 0.75f, 1f) };
        if (Kind == "arrow" && Tint != null) c = new Color(0.85f, 0.8f, 0.6f);
        var mat = new StandardMaterial3D { AlbedoColor = c, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        if (Kind == "arrow")
        {
            // shaft, steel head and fletching (forward is -Z after LookAt)
            _shaftMesh ??= new CylinderMesh { TopRadius = .018f, BottomRadius = .018f, Height = .85f, RadialSegments = 5 };
            _headMesh ??= new CylinderMesh { TopRadius = 0, BottomRadius = .045f, Height = .14f, RadialSegments = 4 };
            _fletchMesh ??= new BoxMesh { Size = new Vector3(.005f, .07f, .16f) };
            _shaftMat ??= new StandardMaterial3D { AlbedoColor = new Color(.55f, .4f, .25f) };
            _headMat ??= new StandardMaterial3D { AlbedoColor = new Color(.75f, .78f, .82f), Metallic = .8f, Roughness = .3f };
            _fletchMat ??= new StandardMaterial3D { AlbedoColor = new Color(.9f, .9f, .85f) };
            AddChild(new MeshInstance3D { Mesh = _shaftMesh, MaterialOverride = _shaftMat, Rotation = new Vector3(Mathf.Pi / 2, 0, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            AddChild(new MeshInstance3D { Mesh = _headMesh, MaterialOverride = _headMat, Position = new Vector3(0, 0, -.48f), Rotation = new Vector3(-Mathf.Pi / 2, 0, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            foreach (float a in new[] { 0f, 2.1f, 4.2f }) AddChild(new MeshInstance3D { Mesh = _fletchMesh, MaterialOverride = _fletchMat, Position = new Vector3(0, 0, .36f), Rotation = new Vector3(0, 0, a), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            // faint thin pale-grey motion trail that stays in the world
            _trail = new CpuParticles3D { Amount = 10, Lifetime = .22f, LocalCoords = false, Gravity = Vector3.Zero, InitialVelocityMax = 0, Mesh = new QuadMesh { Size = new Vector2(.05f, .05f), Material = Vfx.SpriteMat() }, Color = new Color(.78f, .78f, .8f, .22f), ScaleAmountCurve = null, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(_trail);
        }
        else
        {
            // hot core, soft glow shell and a particle trail that stays in the world
            float r = Big ? 0.32f : Kind == "fire" ? 0.28f : 0.16f; bool fire = Kind == "fire" || Big;
            var bm = BoltMats(c);
            if (!_sphereMeshes.TryGetValue(r, out var sph)) _sphereMeshes[r] = sph = new SphereMesh { Radius = r * .6f, Height = r * 1.2f, RadialSegments = 10, Rings = 5 };
            float gs = fire ? 4.5f : 5.5f;   // small bolts get a larger halo so they read at range
            if (!_glowMeshes.TryGetValue(r * gs, out var glq)) _glowMeshes[r * gs] = glq = new QuadMesh { Size = new Vector2(r * gs, r * gs) };
            AddChild(new MeshInstance3D { Mesh = sph, MaterialOverride = fire ? bm[0] : bm[1], CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            AddChild(new MeshInstance3D { Mesh = glq, MaterialOverride = bm[2], CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            if (!fire)
            {
                // a comet streak along the flight path: two crossed additive quads (vertical + horizontal) trailing ~0.8 m behind the head,
                // with a thinner white-hot inner streak; aimed along Vel (forward = -Z) in _PhysicsProcess
                _streakMesh ??= new QuadMesh { Size = new Vector2(.85f, .2f) };
                _streakCoreMesh ??= new QuadMesh { Size = new Vector2(.55f, .07f) };
                _streak = new Node3D(); AddChild(_streak);
                foreach (var rot in new[] { new Vector3(0, Mathf.Pi / 2, 0), new Vector3(Mathf.Pi / 2, Mathf.Pi / 2, 0) })
                {
                    _streak.AddChild(new MeshInstance3D { Mesh = _streakMesh, MaterialOverride = bm[3], Position = new Vector3(0, 0, .34f), Rotation = rot, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
                    _streak.AddChild(new MeshInstance3D { Mesh = _streakCoreMesh, MaterialOverride = bm[4], Position = new Vector3(0, 0, .22f), Rotation = rot, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
                }
                AimStreak();
            }
            var g = new Gradient(); g.SetColor(0, fire ? new Color(1, .9f, .5f, .9f) : new Color(c.Lerp(Colors.White, .4f), .8f)); g.SetColor(1, fire ? new Color(.5f, .08f, .02f, 0) : new Color(c, 0)); g.AddPoint(.4f, fire ? new Color(1, .45f, .1f, .7f) : new Color(c, .5f));
            var sc = new Curve(); sc.AddPoint(new Vector2(0, 1)); sc.AddPoint(new Vector2(1, .1f));
            string look = fire ? "fire" : "magic";   // Kenney flame sprites behind fire, swirling magic wisps behind everything else
            var trail = new CpuParticles3D { Amount = fire ? 48 : 26, Lifetime = fire ? .45f : .3f, LocalCoords = false, Gravity = new Vector3(0, fire ? 2.5f : 0f, 0), Spread = 180, InitialVelocityMin = .1f, InitialVelocityMax = fire ? 1.2f : .5f,
                Mesh = new QuadMesh { Size = new Vector2(r * 3.6f, r * 3.6f), Material = Vfx.SpriteMat(!fire, look) }, ColorRamp = g, ScaleAmountCurve = sc, EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere, EmissionSphereRadius = r * .5f };
            Vfx.Spin(trail, look, 180f); AddChild(trail); if (!fire) _boltTrail = trail;
            if (fire) AddChild(new OmniLight3D { LightColor = c, OmniRange = 8f, LightEnergy = 2.2f });
            else if (Own == 'p' && _lights < 6) { _lights++; _hasLight = true; AddChild(new OmniLight3D { LightColor = c, OmniRange = 3f, LightEnergy = .8f, ShadowEnabled = false }); }
        }
        if (Vel.LengthSquared() > 0.01f && Kind == "arrow") LookAt(GlobalPosition + Vel, Vector3.Up);
    }

    static CylinderMesh? _shaftMesh, _headMesh; static BoxMesh? _fletchMesh; static StandardMaterial3D? _shaftMat, _headMat, _fletchMat;
    // bolt visuals, cached per colour: [fire core, bright core, halo, streak, white-hot streak]
    static readonly Dictionary<Color, StandardMaterial3D[]> _boltMats = new();
    static readonly Dictionary<float, SphereMesh> _sphereMeshes = new();
    static readonly Dictionary<float, QuadMesh> _glowMeshes = new();
    static QuadMesh? _streakMesh, _streakCoreMesh;
    Node3D? _streak;
    static StandardMaterial3D[] BoltMats(Color c)
    {
        if (_boltMats.TryGetValue(c, out var m)) return m;
        StandardMaterial3D Add(Color col, bool bill) => new() { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            BillboardMode = bill ? BaseMaterial3D.BillboardModeEnum.Enabled : BaseMaterial3D.BillboardModeEnum.Disabled, CullMode = BaseMaterial3D.CullModeEnum.Disabled, AlbedoTexture = Vfx.Soft, AlbedoColor = col };
        m = new[]
        {
            new StandardMaterial3D { AlbedoColor = c.Lerp(Colors.White, .55f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
            new StandardMaterial3D { AlbedoColor = c.Lerp(Colors.White, .8f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
            Add(new Color(c, .9f), true),
            Add(new Color(c.Lerp(Colors.White, .2f), 1f), false),
            Add(new Color(c.Lerp(Colors.White, .75f), 1f), false),
        };
        if (_boltMats.Count > 64) _boltMats.Clear();
        return _boltMats[c] = m;
    }
    void AimStreak()
    {
        if (_streak == null || Vel.LengthSquared() < .01f || !IsInsideTree()) return;
        var d = Vel.Normalized();
        _streak.LookAt(_streak.GlobalPosition + d, Mathf.Abs(d.Y) > .98f ? Vector3.Right : Vector3.Up);
    }
    static int _lights; bool _hasLight; float _age; bool _worldHit; CpuParticles3D? _trail, _boltTrail;

    /// <summary>
    /// The particle sprites behind a spell bolt, by element (Vfx texture sets): frost stars, lightning sparks, shadow and poison smoke,
    /// holy light, swirling magic otherwise. Spells fly as this orb + comet + trail, never as a flipbook (the upright orb sheets read as a
    /// flying bottle, FOCUS F-08; data/spellfx.json "banned").
    /// </summary>
    public static string TrailSet(string type) => type switch { "frost" => "star", "lightning" => "spark", "shadow" or "poison" => "smoke", "holy" => "light", "fire" => "fire", _ => "magic" };
    /// <summary>The id of the look a shot of this kind and element flies with (logged by the SpellFx audit and DevCapture).</summary>
    public static string TravelFx(string kind, string type) => kind == "arrow" ? "proj:arrow" : "style:" + SpellStyles.El(type) + ".projectile" + (kind == "fire" ? "" : "+comet");
    /// <summary>A skill's element is known only after Spawn (Calc is set after _Ready): its trail sprites are swapped in on the first tick.</summary>
    void ElementTrail()
    {
        if (Calc == null) return;
        if (Kind != "arrow" && Own == 'p')
        {
            // F-12: skill bolts fly as the effect library draws their element (core + glow + GPU trail, SpellStyles.Projectile);
            // the stand-in orb, halo and CPU trail go (the comet streak and the light stay)
            foreach (var ch in GetChildren()) if (ch is MeshInstance3D or CpuParticles3D) ch.QueueFree();
            _boltTrail = null;
            SpellStyles.Projectile(this, Calc.Type, SpellStyles.PowerOf(Calc) * (Big ? 1.3f : 1f), Kind == "fire" ? 1.25f : 1f);
            return;
        }
        if (_boltTrail == null) return;
        string set = TrailSet(Calc.Type); if (set == "magic" || set == "fire") return;
        if (_boltTrail.Mesh is QuadMesh q) { q.Material = Vfx.SpriteMat(true, set); Vfx.Spin(_boltTrail, set, 180f); }
    }
    static readonly List<Projectile> Stuck = new();
    public override void _ExitTree() { if (_hasLight) { _hasLight = false; _lights--; } }

    /// <summary>Arrow lands in the world: dust on earth, splinters on wood/blocks, sparks on stone; the arrow stays 6 s (20 max).</summary>
    void ArrowLand(Vector3 to)
    {
        var dir = Vel.LengthSquared() > .01f ? Vel.Normalized() : Vector3.Down;
        string step = "";
        try { byte b = BlockWorld.At(to + dir * .15f); if (b != 255 && b != 0) step = Blocks.Def(b).Step; } catch { }
        if (step == "wood") Fx.Burst(to, new Color(.5f, .33f, .18f), 6, 2.5f, .35f, .05f, -8f);
        else if (step is "stone" or "ice" or "gravel") Fx.Burst(to, new Color(1f, .85f, .45f), 5, 3.5f, .25f, .04f, -8f);
        else Fx.Dust(to, .4f);
        foreach (var s in Stuck.ToArray()) if (!IsInstanceValid(s)) Stuck.Remove(s);
        while (Stuck.Count >= 20) { var o = Stuck[0]; Stuck.RemoveAt(0); if (IsInstanceValid(o)) o.QueueFree(); }
        Stuck.Add(this);
        RemoveFromGroup("projectiles"); SetPhysicsProcess(false);
        if (_trail != null) { _trail.QueueFree(); _trail = null; }
        GlobalPosition = to - dir * .3f;
        GetTree().CreateTimer(6.0).Timeout += () => { if (IsInstanceValid(this)) QueueFree(); };
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (_age == 0) ElementTrail();
        Life -= dt; _age += dt;
        if (Kind == "arrow") Vel.Y -= 7f * dt;
        if (Homing && Own == 'p')
        {
            // seek the nearest living enemy ahead (within 22 m and 80 degrees), turning a few radians per second
            Enemy? best = null; float bd = 22f; var dirN = Vel.Normalized();
            foreach (var n in GetTree().GetNodesInGroup("enemies")) if (n is Enemy e && !e.Dead && e.Active && !Hits.Contains(e)) { var d = e.GlobalPosition + Vector3.Up * e.HeightM() * .5f - GlobalPosition; float l = d.Length(); if (l < bd && dirN.AngleTo(d / l) < 1.4f) { bd = l; best = e; } }
            if (best != null) { var want = (best.GlobalPosition + Vector3.Up * best.HeightM() * .5f - GlobalPosition).Normalized() * Vel.Length(); Vel = Vel.Lerp(want, Mathf.Clamp(6f * dt, 0, 1)); }
        }
        var from = GlobalPosition; var to = from + Vel * dt;
        bool end = Life <= 0;
        // the block world's water: everything slows in it, and fire goes out
        if (BlockWorld.FluidAt(from) == "water") { Vel *= Mathf.Pow(.08f, dt); if (Calc?.Type == "fire" || Kind == "fire") { Fx.Burst(from, new Color(.8f, .85f, .9f), 6, 1.5f, .4f, .1f, 2f); QueueFree(); return; } }

        // world collision (ground, walls)
        var q = PhysicsRayQueryParameters3D.Create(from, to, 1);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(q);
        if (hit.Count > 0)
        {
            to = (Vector3)hit["position"]; end = true; _worldHit = true;
            if (Own == 'p' && hit["collider"].As<Node>()?.GetParent() is Npc shot && Player.Instance is { } shooter) Crime.Struck(shot, Game.D.WpnDmg(), shooter);   // F-32: a shot into a villager is a crime
        }
        GlobalPosition = to;
        if (_streak != null && (Homing || _age < dt * 1.5f || BlockWorld.FluidAt(to) == "water")) AimStreak();   // straight shots keep their first aim
        if (Kind == "arrow" && Vel.LengthSquared() > 0.01f)
        {
            LookAt(to + Vel, Vector3.Up);
            if (_age < .3f) { float w = (1f - _age / .3f) * .07f; RotateObjectLocal(Vector3.Right, Mathf.Sin(_age * 55f) * w); RotateObjectLocal(Vector3.Up, Mathf.Cos(_age * 47f) * w); }   // slight wobble off the string
        }

        Enemy? hitE = null;
        if (!end && Own == 'p')
        {
            foreach (var n in GetTree().GetNodesInGroup("enemies"))
            {
                if (n is not Enemy e || e.Dead || !e.Active || Hits.Contains(e)) continue;
                var d = e.GlobalPosition - to; float dy = to.Y - e.GlobalPosition.Y;
                if (new Vector2(d.X, d.Z).Length() < e.Def.R * e.ScaleF() + 0.5f && dy > -0.3f && dy < e.HeightM() + 0.5f)
                {
                    if ((Pierce || PierceLeft > 0) && Aoe <= 0) { var ph = HitFor(to - Vel); ph.Knock = 1.5f; e.TakeHit(ph); Hits.Add(e); if (PierceLeft > 0) PierceLeft--; if (Calc != null) SpellFx.Skill(Calc.Def.Id, "hit", to); continue; }
                    hitE = e; end = true; break;
                }
            }
        }
        else if (!end && Own == 'e')
        {
            // any hero can be struck: ours, or (on a host) a friend's, whose game then applies the hit
            foreach (var hero in Heroes())
            {
                var d = hero.GlobalPosition - to; float dy = to.Y - hero.GlobalPosition.Y;
                if (new Vector2(d.X, d.Z).Length() < 0.75f && dy > -0.2f && dy < 2.1f) { var eh = new HitInfo { Damage = Dmg, From = to - Vel, Knock = 3f, IsProjectile = true }; if (Source != null && IsInstanceValid(Source)) Source.OnHit(eh); ((IDamageable)hero).TakeHit(eh); end = true; Fx.Burst(to, new Color(1, 0.5f, 0.2f), 8); break; }
            }
        }

        if (!end) return;
        // (A-08: shots and spell projectiles no longer change blocks, the hero's or a monster's; blocks are only mined)
        if (Own == 'x') { if (Aoe > 0) Vfx.Explosion(to, Aoe); else Fx.Burst(to, new Color(0.8f, 0.8f, 0.9f), 4, 2f, 0.25f, 0.06f); QueueFree(); return; }
        if (Own == 'p')
        {
            if (OnEnd != null) { var cb = OnEnd; OnEnd = null; cb(to); }
            if (Calc != null) SpellFx.Skill(Calc.Def.Id, "hit", to, Aoe > 0 ? Mathf.Max(1f, Aoe * 2f / Mathf.Max(.1f, SpellFx.SizeOf(SpellFx.For(Calc.Def.Id, "hit")))) : 1f);
            if (Wpn != "") Sfx.Weapon(Wpn, Aoe > 0 || hitE != null ? "hit" : "miss");   // the weapon type's own impact, or its miss
            if (Aoe > 0)
            {
                Vfx.Explosion(to, Aoe); if (Wpn == "") Sfx.Play("hit");
                if (Calc != null) SpellStyles.Aoe(to, Aoe, Calc.Type, SpellStyles.PowerOf(Calc), .15f);   // F-12: the blast's rune circle shows its reach Player.Instance?.Rig?.Shake(0.18f);
                foreach (var n in GetTree().GetNodesInGroup("enemies"))
                {
                    if (n is not Enemy e || e.Dead || !e.Active) continue;
                    var d = e.GlobalPosition - to; d.Y = 0; float dist = d.Length() - e.Def.R;
                    if (dist < Aoe)
                    {
                        var ah = HitFor(to); ah.Damage = Dmg * (1f - Mathf.Clamp(dist / Aoe, 0, 1) * 0.4f); ah.Knock = Mathf.Max(ah.Knock, 4f); ah.Stun = Mathf.Max(ah.Stun, .25f);
                        if (Calc == null) { ah.Type = "fire"; ah.Fire = true; ah.With("burn", 1, 1, .12f * (1f + Game.TS["burn"] / 100f) / .25f); }
                        e.TakeHit(ah);
                    }
                }
            }
            else if (hitE != null)
            {
                hitE.TakeHit(HitFor(to - Vel)); Hits.Add(hitE);
                if (Kind == "arrow") Fx.Burst(to, new Color(.4f, .03f, .03f), 5, 2.5f, .3f, .05f, -9f);
                if (ChainLeft > 0)
                {
                    // chain: a new projectile leaps from this foe to the nearest one not yet hit
                    Enemy? nx = null; float bd = 14f;
                    foreach (var n in GetTree().GetNodesInGroup("enemies")) if (n is Enemy e && !e.Dead && e.Active && !Hits.Contains(e)) { float d = (e.GlobalPosition - hitE.GlobalPosition).Length(); if (d < bd) { bd = d; nx = e; } }
                    if (nx != null)
                    {
                        var start = hitE.GlobalPosition + Vector3.Up * hitE.HeightM() * .6f; var dir = (nx.GlobalPosition + Vector3.Up * nx.HeightM() * .5f - start).Normalized();
                        var p2 = Spawn(Kind, 'p', start, dir * Vel.Length(), Dmg * .85f, 1.5f, Crit, Pierce, 0); p2.Calc = Calc; p2.ChainLeft = ChainLeft - 1; p2.Homing = true; p2.Tint = Tint; p2.Hits = new HashSet<Enemy>(Hits);
                        if (Calc != null) SpellFx.SkillAttach(Calc.Def.Id, "travel", p2);
                    }
                }
            }
            else if (Kind == "arrow") { if (_worldHit) { ArrowLand(to); return; } Fx.Burst(to, new Color(0.8f, 0.8f, 0.9f), 3, 2f, 0.25f, 0.05f); }
            else
            {
                // small element-tinted ring and sparks (skills already have their own impact fx)
                var ec = Tint ?? (Calc != null ? TwinRealms.Dmg.TypeColor(Calc.Type) : Kind == "fire" ? new Color(1f, .55f, .15f) : new Color(.55f, .75f, 1f));
                if (Calc == null) Fx.Ring(to + Vector3.Up * .05f, .9f, ec, .3f);
                Fx.Burst(to, ec, 5, 3f, .3f, .05f);
                if (Kind == "bolt" && !Big) Vfx.Sparks(to, Vfx.Ramp((0, new Color(ec.Lerp(Colors.White, .7f), 1)), (.4f, new Color(ec, .8f)), (1, new Color(ec, 0))), 12, 4.5f, .3f, .22f, -1f, true, 180f, null, .08f);   // short bright impact pop
            }
        }
        else if (Aoe > 0)
        {
            Fx.Burst(to, new Color(1, 0.4f, 0.1f), 24, 6f, 0.6f, 0.2f, -6f, "fire");
            foreach (var hero in Heroes()) if (hero.GlobalPosition.DistanceTo(to) < Aoe) ((IDamageable)hero).TakeHit(new HitInfo { Damage = Dmg, From = to, Knock = 5f, NoBlock = true, Stagger = true });
        }
        QueueFree();
    }
    /// <summary>Heroes this enemy shot can strike: everyone out in the overworld for an overworld foe in a hosted world, otherwise our own hero.</summary>
    IEnumerable<Node3D> Heroes()
    {
        bool overworldFoe = Source != null && IsInstanceValid(Source) ? !Source.FromDungeon && !Source.Arena : Net.MyZone == 0;
        if (Net.Active && overworldFoe) { foreach (var h in Net.WorldHeroes()) yield return h; yield break; }
        if (Dungeon.IsOwner && Source != null && IsInstanceValid(Source) && Source.FromDungeon) { foreach (var h in Net.DungeonHeroes()) yield return h; yield break; }
        var pl = Player.Instance; if (pl != null && !pl.Dead) yield return pl;
    }
}
