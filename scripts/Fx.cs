using Godot;

namespace TwinRealms;

/// <summary>Cheap visual effects: particle bursts and expanding rings.</summary>
public static class Fx
{
    /// <summary>A burst of glowing flashes (Kenney star textures, tinted). set = another texture set from Vfx.Sets.</summary>
    public static void Burst(Vector3 pos, Color col, int count = 12, float speed = 4f, float life = 0.5f, float size = 0.12f, float gravity = -6f, string set = "star")
    {
        if (Game.Root == null) return;
        var g = new Gradient(); g.SetColor(0, col.Lerp(Colors.White, .2f)); g.SetColor(1, new Color(col, 0)); g.AddPoint(.35f, col);
        Vfx.Sparks(pos, g, count, speed, life, size * 6f, gravity, false, 180f, null, .1f, set);   // unshaded, normal blend: bright on dark ground and on sunny grass alike
    }

    static QuadMesh? _dustMesh; static StandardMaterial3D? _dustMat;
    /// <summary>Ground dust puff at the feet after a hard landing; size 0..1+ scales with the fall. Shares one mesh and material.</summary>
    public static void Dust(Vector3 pos, float size)
    {
        if (Game.Root == null) return;
        if (_dustMat == null)
        {
            _dustMat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, VertexColorUseAsAlbedo = true, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
            _dustMesh = new QuadMesh { Size = new Vector2(.5f, .5f), Material = _dustMat };
        }
        var col = new Color(.5f, .45f, .38f, .7f);
        try
        {
            byte b = BlockWorld.At(pos + Vector3.Down * .1f);
            if (b != 255 && b != 0)
            {
                string st = Blocks.Def(b).Step;
                if (st == "sand") col = new Color(.78f, .7f, .5f, .7f); else if (st == "snow") col = new Color(.95f, .97f, 1f, .75f);
            }
        }
        catch { }
        size = Mathf.Clamp(size, .4f, 1.6f);
        var p = new CpuParticles3D
        {
            Mesh = _dustMesh, Amount = 14, OneShot = true, Explosiveness = 1f, Lifetime = .6f, EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere, EmissionSphereRadius = .15f,
            Direction = new Vector3(0, .25f, 0), Spread = 180f, Gravity = Vector3.Zero, InitialVelocityMin = 1f * size, InitialVelocityMax = 2.2f * size,
            ScaleAmountMin = .7f * size, ScaleAmountMax = 1.3f * size, Color = col, Emitting = true,
        };
        var ramp = new Gradient(); ramp.SetColor(0, Colors.White); ramp.SetColor(1, new Color(1, 1, 1, 0)); p.ColorRamp = ramp;
        Game.Root.AddChild(p); p.GlobalPosition = pos + Vector3.Up * .05f;
        p.GetTree().CreateTimer(1.0).Timeout += () => { if (GodotObject.IsInstanceValid(p)) p.QueueFree(); };
    }

    public static void Ring(Vector3 pos, float radius, Color col, float time = 0.45f)
    {
        var root = Game.Root; if (root == null) return;
        var m = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.85f, OuterRadius = 1f, Rings = 24, RingSegments = 6 },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(col, 0.8f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha },
            Scale = new Vector3(0.2f, 0.2f, 0.2f),
        };
        root.AddChild(m); m.GlobalPosition = pos;
        var tw = m.CreateTween(); tw.SetParallel(true);
        tw.TweenProperty(m, "scale", new Vector3(radius, radius * 0.3f, radius), time);
        tw.Chain().TweenCallback(Callable.From(m.QueueFree));
    }

    public static void Line(Vector3 a, Vector3 b, Color col, float time = 0.15f)
    {
        var root = Game.Root; if (root == null) return;
        var len = a.DistanceTo(b); if (len < 0.01f) return;
        var m = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.08f, 0.08f, len) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = col, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
        };
        root.AddChild(m); m.GlobalPosition = (a + b) / 2; m.LookAt(b, Vector3.Up);
        root.GetTree().CreateTimer(time).Timeout += m.QueueFree;
    }
}
