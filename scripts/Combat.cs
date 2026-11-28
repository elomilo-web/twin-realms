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
    public bool NoText;
    /// <summary>The blow staggers the hero: pushes them, plays the flinch and interrupts what they are doing. Only abilities set it (slams, blasts, skills that stun or knock back);
    /// ordinary attacks just deal damage. A hit with Stun above 0 staggers too.</summary>
    public bool Stagger;        // damage-over-time ticks use their own small numbers
    // multiplayer: a friend's hit forwarded to the host (their bonuses already applied, their penetration carried along), or a duel hit
    public bool Remote, Duel; public long Peer; public float APen, APenP, MPen, MPenP;
    /// <summary>Poise damage of the blow (data/tuning.json game.poise); below 0 = worked out by the target from Kind / Knock.</summary>
    public float Poise = -1;
    /// <summary>A critical blow (riposte, backstab): breaks poise outright and always plays the heavy reaction.</summary>
    public bool Critical;
    /// <summary>Where the blow touched the body (hitbox contact), or null; picks the hit reaction (head height = Hit_Head).</summary>
    public Vector3? At;
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

/// <summary>
/// A-04: can a melee blow reach across a height difference? (data/tuning.json game.melee). Feet heights and body heights in metres.
/// A blow reaches when the two stand within stepTolerance of each other's level (slopes, slabs, a stair lip: less than one block).
/// A taller attacker reaches further up and down: reachPerHeight for every metre it stands above bodyHeight. Striking down gets
/// downExtra more, and a tall target standing lower still rises into reach: targetPerHeight for every metre above bodyHeight.
/// So a man-sized monster one block below (or above) the hero cannot hit, and the hero cannot hit it; a golem or a dragon can.
/// Used by Enemy (starting a wind-up and landing the blow) and by the hero's own swings (Player.ResolveMelee, the step-in).
/// </summary>
public static class Melee
{
    public static bool Reaches(float atkFeet, float atkHeight, float tgtFeet, float tgtHeight)
    {
        float dy = tgtFeet - atkFeet, refH = Tune.F("melee.bodyHeight", 1.9f);
        float reach = Tune.F("melee.stepTolerance", .75f) + Mathf.Max(0f, atkHeight - refH) * Tune.F("melee.reachPerHeight", .6f);
        if (dy >= 0f) return dy <= reach;
        return -dy <= reach + Tune.F("melee.downExtra", .35f) + Mathf.Max(0f, tgtHeight - refH) * Tune.F("melee.targetPerHeight", .8f);
    }
}

/// <summary>Floating damage numbers and notices.</summary>
public static class FloatText
{
    static readonly System.Text.RegularExpressions.Regex Num = new(@"^[+\-]?\d+!?$");
    public static void Spawn(Node context, Vector3 pos, string text, Color col, float size = 1f)
    {
        // damage and healing numbers we show are shared with friends nearby (they can turn them off: Settings > Game)
        if (Net.Active && Num.IsMatch(text)) Net.I!.Number(pos, text, col, size);
        SpawnRemote(context, pos, text, col, size);
    }
    /// <summary>Draws a number without sharing it (a friend's number, or anything local only).</summary>
    public static void SpawnRemote(Node context, Vector3 pos, string text, Color col, float size = 1f)
    {
        if (!GodotObject.IsInstanceValid(context) || !context.IsInsideTree()) return;
        var scene = context.GetTree().CurrentScene;
        if (scene == null) return;
        bool isNum = Num.IsMatch(text), crit = isNum && text.EndsWith("!"), hurt = isNum && text.StartsWith("-");
        if (isNum)
        {
            bool plain = col.R > .93f && col.G > .93f && col.B > .93f;   // white = physical
            if (hurt) col = new Color(.75f, .13f, .1f);                  // damage taken by the hero
            else if (crit) col = new Color(1f, .75f, .2f);               // amber-gold
            else if (plain) col = new Color(.93f, .88f, .75f);           // parchment
        }
        float px = isNum ? 0.0045f : 0.006f;
        var l = new Label3D
        {
            Text = text, Modulate = col, FontSize = (int)(48 * size), OutlineSize = 10, PixelSize = px,
            OutlineModulate = new Color(.05f, .03f, .03f, .95f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true,
        };
        scene.AddChild(l);
        float dx = isNum ? (GD.Randf() - .5f) * .9f : 0f;
        l.GlobalPosition = pos + new Vector3(dx, 0, 0);
        var tw = l.CreateTween();
        tw.SetParallel(true);
        if (crit)
        {
            l.Scale = Vector3.One * 1.4f;
            tw.TweenProperty(l, "scale", Vector3.One, 0.12f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
            var shake = l.CreateTween();
            for (int i = 0; i < 4; i++) shake.TweenProperty(l, "position:x", pos.X + dx + (i % 2 == 0 ? .04f : -.04f), 0.03f);
        }
        tw.TweenProperty(l, "position:y", pos.Y + (isNum ? 1.0f : 1.3f), 0.8f);
        tw.TweenProperty(l, "modulate:a", 0f, 0.5f).SetDelay(0.3f);
        tw.Chain().TweenCallback(Callable.From(l.QueueFree));
    }
}
