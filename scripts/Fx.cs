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
