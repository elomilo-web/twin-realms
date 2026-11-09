using System.Collections.Generic;
using Godot;

namespace TwinRealms;

public class HitInfo
{
    public float Damage;
    public Vector3 From;
    public float Knock;
    public float Stun;
    public float Up;           // knock-up: upward launch speed (m/s); bosses and the dragon ignore it
    public bool Crit;
    public Node3D? Attacker;
    public bool IsPlayer, IsProjectile, NoBlock, Fire;
    public string Kind = "";   // melee kind for game feel: light, combo, power ("" = spell or arrow)
    public string Type = "physical";   // damage type (Dmg.Types or "true")
    public List<(string id, float chance, float durMul, float pow)>? Statuses;
    public float Leech;        // % of damage returned as health (skill supports)
    public float Execute;      // +% damage against targets below 35% health
    public bool NoText;        // damage-over-time ticks use their own small numbers
    public HitInfo With(string id, float chance, float durMul = 1, float pow = 0) { (Statuses ??= new()).Add((id, chance, durMul, pow)); return this; }
}

public interface IDamageable
{
    bool Dead { get; }
    void TakeHit(HitInfo h);
}

public static class Util
{
    /// <summary>Signed shortest angle from a to b.</summary>
    public static float AngDiff(float a, float b) => Mathf.Wrap(b - a, -Mathf.Pi, Mathf.Pi);
    /// <summary>Yaw (Godot convention, forward = -Z) that faces the flat direction (dx, dz).</summary>
    public static float YawTo(float dx, float dz) => Mathf.Atan2(-dx, -dz);
}

/// <summary>Floating damage numbers and notices.</summary>
public static class FloatText
{
    public static void Spawn(Node context, Vector3 pos, string text, Color col, float size = 1f)
    {
        var scene = context.GetTree().CurrentScene;
        if (scene == null) return;
        var l = new Label3D
        {
            Text = text, Modulate = col, FontSize = (int)(48 * size), OutlineSize = 10, PixelSize = 0.006f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true,
        };
        scene.AddChild(l);
        l.GlobalPosition = pos;
        var tw = l.CreateTween();
        tw.SetParallel(true);
        tw.TweenProperty(l, "position:y", pos.Y + 1.3f, 0.9f);
        tw.TweenProperty(l, "modulate:a", 0f, 0.9f).SetDelay(0.3f);
        tw.Chain().TweenCallback(Callable.From(l.QueueFree));
    }
}
