using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>
/// Crafted spells (Spellcraft.cs): the second section. Like the chained sections of a Two Worlds II amulet, it goes off where the first
/// section ends: where a missile lands, a strike falls, a chain stops, a leap lands, a swing connects, or around you for bursts and
/// blessings. It uses the spell's own rank and sockets.
/// </summary>
public partial class Player
{
    Vector3? _fuAt; float _fuDelay;
    /// <summary>Kinds whose second section goes off around the hero rather than at the aim point.</summary>
    static readonly HashSet<string> SelfKinds = new() { "nova", "spin", "orbit", "buff", "heal", "enchant", "ward", "blinkto", "boomerang" };

    void FollowUp(SkillCalc c, Vector3 at, float delay = 0)
    {
        var then = c.Def.Then; if (then == "" || Skills.Get(then) == null || Dead) return;
        if (delay > 0) { GetTree().CreateTimer(delay).Timeout += () => { if (!Dead) FollowUp(c, at, 0); }; return; }
        var f = Skills.Compute(then, c.Def.Id); f.At = at;
        if (c.Charge > 0) { f.Charge = c.Charge; f.Dmg *= 1 + .8f * c.Charge; f.Radius *= 1 + .3f * c.Charge; }   // an empowered spell empowers both sections
        var near = NearestFoe(14f, at);
        switch (f.Def.Kind)
        {
            case "nova": NovaAt(f, at, false); if (f.Chain > 0) ChainFrom(f, at, f.Chain); break;
            case "projectile": Shrapnel(f, at); break;
            case "chain": ArcFrom(f, at, near); break;
            default:
            {
                // the placed kinds (strike, zone, wall, trap, vortex, altar) read f.At; no cast animation of their own
                bool keep = _castFxDone; _castFxDone = true; Execute(f, near, true); _castFxDone = keep;
                break;
            }
        }
    }

    /// <summary>A missile as a second section: shards fly out in a ring from where the first section ended.</summary>
    void Shrapnel(SkillCalc f, Vector3 at)
    {
        int n = Mathf.Max(3, f.Count + 2); var col = Dmg.TypeColor(f.Type); var k = f.Def;
        string vis = f.Type == "fire" ? "fire" : k.Arrow || (k.Scale == "weapon" && f.Type == "physical") ? "arrow" : "bolt";
        float a0 = GD.Randf() * Mathf.Tau;
        for (int i = 0; i < n; i++)
        {
            float a = a0 + i * Mathf.Tau / n; var d = new Vector3(-Mathf.Sin(a), .08f, -Mathf.Cos(a)).Normalized();
            var p = Projectile.Spawn(vis, 'p', at + Vector3.Up * .9f + d * .4f, d * f.Speed * .8f, f.Dmg, 1.1f, Crit(f), false, f.Radius * .6f, false);
            p.Calc = f; p.PierceLeft = f.Pierce; p.ChainLeft = f.Chain; p.Homing = f.Homing; p.Tint = col;
            SpellFx.SkillAttach(k.Id, "travel", p);
        }
    }

    /// <summary>A chain as a second section: it leaps from where the first section ended to the foes nearby.</summary>
    void ArcFrom(SkillCalc f, Vector3 at, Enemy? first)
    {
        var col = Dmg.TypeColor(f.Type); var seen = new HashSet<Enemy>(); var prev = at + Vector3.Up; var cur = first; float dm = f.Dmg;
        int hits = Mathf.Max(1, f.Chain);
        for (int j = 0; j < hits && cur != null; j++)
        {
            var pt = cur.GlobalPosition + new Vector3(0, cur.HeightM() * .55f, 0); Vfx.Lightning(prev, pt, col, .28f); seen.Add(cur);
            BeamFx(f.Def.Id, prev, pt); SpellFx.Skill(f.Def.Id, "hit", pt);
            cur.TakeHit(f.Hit(dm, at, Crit(f))); dm *= .82f; prev = pt; cur = NearestFoe(f.Jump, cur.GlobalPosition, seen);
        }
    }
}
