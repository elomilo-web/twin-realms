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

    public static Projectile Spawn(string kind, char own, Vector3 pos, Vector3 vel, float dmg, float life, bool crit = false, bool pierce = false, float aoe = 0f, bool big = false)
    {
        var p = new Projectile { Kind = kind, Own = own, Vel = vel, Dmg = dmg, Life = life, Crit = crit, Pierce = pierce, Aoe = aoe, Big = big };
        Game.Root!.AddChild(p); p.GlobalPosition = pos; return p;
    }

    public override void _Ready()
    { AddToGroup("projectiles");
        Color c = Kind switch { "arrow" => new Color(0.85f, 0.8f, 0.6f), "fire" => new Color(1f, 0.55f, 0.15f), "ebolt" => new Color(0.8f, 0.3f, 1f), _ => new Color(0.55f, 0.75f, 1f) };
        var mat = new StandardMaterial3D { AlbedoColor = c, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        if (Kind == "arrow")
        {
            // shaft, steel head and fletching (forward is -Z after LookAt)
            AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = .018f, BottomRadius = .018f, Height = .85f, RadialSegments = 5 }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(.55f, .4f, .25f) }, Rotation = new Vector3(Mathf.Pi / 2, 0, 0) });
            AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0, BottomRadius = .045f, Height = .14f, RadialSegments = 4 }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(.75f, .78f, .82f), Metallic = .8f, Roughness = .3f }, Position = new Vector3(0, 0, -.48f), Rotation = new Vector3(-Mathf.Pi / 2, 0, 0) });
            foreach (float a in new[] { 0f, 2.1f, 4.2f }) AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(.005f, .07f, .16f) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(.9f, .9f, .85f) }, Position = new Vector3(0, 0, .36f), Rotation = new Vector3(0, 0, a) });
            var tr = new CpuParticles3D { Amount = 14, Lifetime = .18f, LocalCoords = false, Gravity = Vector3.Zero, InitialVelocityMax = 0, Mesh = new QuadMesh { Size = new Vector2(.08f, .08f), Material = Vfx.SpriteMat() }, Color = new Color(1, 1, .9f, .35f), ScaleAmountCurve = null };
            AddChild(tr);
        }
        else
        {
            // hot core, soft glow shell and a particle trail that stays in the world
            float r = Big ? 0.32f : Kind == "fire" ? 0.28f : 0.16f; bool fire = Kind == "fire" || Big;
            var core = new StandardMaterial3D { AlbedoColor = c.Lerp(Colors.White, .55f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
            AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = r * .6f, Height = r * 1.2f, RadialSegments = 10, Rings = 5 }, MaterialOverride = core, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            AddChild(new MeshInstance3D { Mesh = new QuadMesh { Size = new Vector2(r * 4.5f, r * 4.5f) }, MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add, BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled, AlbedoTexture = Vfx.Soft, AlbedoColor = new Color(c, .9f) }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            var g = new Gradient(); g.SetColor(0, fire ? new Color(1, .9f, .5f, .9f) : new Color(c.Lerp(Colors.White, .4f), .8f)); g.SetColor(1, fire ? new Color(.5f, .08f, .02f, 0) : new Color(c, 0)); g.AddPoint(.4f, fire ? new Color(1, .45f, .1f, .7f) : new Color(c, .5f));
            var sc = new Curve(); sc.AddPoint(new Vector2(0, 1)); sc.AddPoint(new Vector2(1, .1f));
            AddChild(new CpuParticles3D { Amount = fire ? 48 : 26, Lifetime = fire ? .45f : .3f, LocalCoords = false, Gravity = new Vector3(0, fire ? 2.5f : 0f, 0), Spread = 180, InitialVelocityMin = .1f, InitialVelocityMax = fire ? 1.2f : .5f,
                Mesh = new QuadMesh { Size = new Vector2(r * 2.4f, r * 2.4f), Material = Vfx.SpriteMat() }, ColorRamp = g, ScaleAmountCurve = sc, EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere, EmissionSphereRadius = r * .5f });
            AddChild(new OmniLight3D { LightColor = c, OmniRange = fire ? 8f : 6f, LightEnergy = fire ? 2.2f : 1.2f });
        }
        if (Vel.LengthSquared() > 0.01f && Kind == "arrow") LookAt(GlobalPosition + Vel, Vector3.Up);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        Life -= dt;
        if (Kind == "arrow") Vel.Y -= 7f * dt;
        var from = GlobalPosition; var to = from + Vel * dt;
        bool end = Life <= 0;

        // world collision (ground, walls)
        var q = PhysicsRayQueryParameters3D.Create(from, to, 1);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(q);
        if (hit.Count > 0) { to = (Vector3)hit["position"]; end = true; }
        GlobalPosition = to;
        if (Kind == "arrow" && Vel.LengthSquared() > 0.01f) LookAt(to + Vel, Vector3.Up);

        Enemy? hitE = null;
        if (!end && Own == 'p')
        {
            foreach (var n in GetTree().GetNodesInGroup("enemies"))
            {
                if (n is not Enemy e || e.Dead || !e.Active || Hits.Contains(e)) continue;
                var d = e.GlobalPosition - to; float dy = to.Y - e.GlobalPosition.Y;
                if (new Vector2(d.X, d.Z).Length() < e.Def.R * e.ScaleF() + 0.5f && dy > -0.3f && dy < e.HeightM() + 0.5f)
                {
                    if (Pierce && Aoe <= 0) { e.TakeHit(new HitInfo { Damage = Dmg, From = to - Vel, Knock = 1.5f, Crit = Crit, Attacker = Player.Instance, IsPlayer = true }); Hits.Add(e); continue; }
                    hitE = e; end = true; break;
                }
            }
        }
        else if (!end && Own == 'e')
        {
            var pl = Player.Instance;
            if (pl != null && !pl.Dead)
            {
                var d = pl.GlobalPosition - to; float dy = to.Y - pl.GlobalPosition.Y;
                if (new Vector2(d.X, d.Z).Length() < 0.75f && dy > -0.2f && dy < 2.1f) { pl.TakeHit(new HitInfo { Damage = Dmg, From = to - Vel, Knock = 3f, IsProjectile = true }); end = true; Fx.Burst(to, new Color(1, 0.5f, 0.2f), 8); }
            }
        }

        if (!end) return;
        if (Own == 'p')
        {
            if (Aoe > 0)
            {
                Vfx.Explosion(to, Aoe); Sfx.Play("hit"); Player.Instance?.Rig?.Shake(0.18f);
                foreach (var n in GetTree().GetNodesInGroup("enemies"))
                {
                    if (n is not Enemy e || e.Dead || !e.Active) continue;
                    var d = e.GlobalPosition - to; d.Y = 0; float dist = d.Length() - e.Def.R;
                    if (dist < Aoe)
                    {
                        e.TakeHit(new HitInfo { Damage = Dmg * (1f - Mathf.Clamp(dist / Aoe, 0, 1) * 0.4f), From = to, Knock = 4f, Stun = 0.25f, Attacker = Player.Instance, IsPlayer = true, Fire = true });
                        e.Ignite(3f, Dmg * 0.12f * (1f + Game.TS["burn"] / 100f));
                    }
                }
            }
            else if (hitE != null) hitE.TakeHit(new HitInfo { Damage = Dmg, From = to - Vel, Knock = 2.2f, Crit = Crit, Attacker = Player.Instance, IsPlayer = true });
            else Fx.Burst(to, new Color(0.8f, 0.8f, 0.9f), 4, 2f, 0.25f, 0.06f);
        }
        else if (Aoe > 0)
        {
            Fx.Burst(to, new Color(1, 0.4f, 0.1f), 24, 6f, 0.6f, 0.2f);
            var pl = Player.Instance;
            if (pl != null && !pl.Dead && pl.GlobalPosition.DistanceTo(to) < Aoe) pl.TakeHit(new HitInfo { Damage = Dmg, From = to, Knock = 5f, NoBlock = true });
        }
        QueueFree();
    }
}
