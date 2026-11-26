using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>
/// Crafted spells (Spellcraft.cs): the chained sections (II and III). Like the chained sections of a Two Worlds II amulet, each goes off
/// where the one before it ends: where a missile lands, a strike falls, a chain stops, a leap lands, a swing connects, or around you for
/// bursts and blessings. They use the spell's own rank and sockets.
/// </summary>
public partial class Player
{
    Vector3? _fuAt; float _fuDelay;
    /// <summary>Kinds whose second section goes off around the hero rather than at the aim point.</summary>
    static readonly HashSet<string> SelfKinds = new() { "nova", "spin", "orbit", "buff", "heal", "enchant", "ward", "blinkto", "boomerang" };

    /// <summary>Hard stop on chained sections, whatever the data says (a spell holds at most Spellcraft.MaxSections).</summary>
    const int MaxChainDepth = 6;

    /// <summary>The next section of a crafted spell goes off at <paramref name="at"/>; depth = sections already gone off. Each section
    /// triggers the one after it (section I -> II -> III) where it ends, and the chain stops at the last section or at the hard cap.</summary>
    void FollowUp(SkillCalc c, Vector3 at, float delay = 0, int depth = 1)
    {
        var then = c.Def.Then; if (then == "" || then == c.Def.Id || depth >= Mathf.Min(MaxChainDepth, Spellcraft.MaxSections) || Skills.Get(then) == null || Dead) return;
        if (delay > 0) { GetTree().CreateTimer(delay).Timeout += () => { if (!Dead) FollowUp(c, at, 0, depth); }; return; }
        var f = Skills.Compute(then, Spellcraft.RootOf(c.Def.Id)); f.At = at;   // every section uses the spell's own rank and sockets
        if (c.Charge > 0) { f.Charge = c.Charge; f.Dmg *= 1 + .8f * c.Charge; f.Radius *= 1 + .3f * c.Charge; }   // an empowered spell empowers every section
        var near = NearestFoe(14f, at); int next = depth + 1; bool more = f.Def.Then != "";
        LogCast(CastPath.Chain, Spellcraft.RootOf(c.Def.Id), $"section {depth + 1} ({then}) (part of the root cast: no cost, no new cooldown)");
        switch (f.Def.Kind)
        {
            case "nova": NovaAt(f, at, false); if (f.Chain > 0) ChainFrom(f, at, f.Chain); if (more) FollowUp(f, at, .25f, next); break;
            case "projectile": Shrapnel(f, at, more ? next : 0); break;
            case "chain": { var end = ArcFrom(f, at, near); if (more) FollowUp(f, end, .12f, next); break; }
            default:
            {
                // the placed kinds (strike, zone, wall, trap, vortex, altar) read f.At; no cast animation of their own
                bool keep = _castFxDone; _castFxDone = true; Execute(f, near, true); _castFxDone = keep;
                var end = _fuAt ?? at; float dl = _fuDelay;
                if (more) FollowUp(f, end, dl + .05f, next);
                break;
            }
        }
    }

    /// <summary>A missile as a chained section: shards fly out in a ring from where the section before it ended. next &gt; 0: the first
    /// shard to end sets off the section after this one (only once).</summary>
    void Shrapnel(SkillCalc f, Vector3 at, int next = 0)
    {
        bool fired = false;
        int n = Mathf.Max(3, f.Count + 2); var col = Dmg.TypeColor(f.Type); var k = f.Def;
        string vis = f.Type == "fire" ? "fire" : k.Arrow || (k.Scale == "weapon" && f.Type == "physical") ? "arrow" : "bolt";
        float a0 = GD.Randf() * Mathf.Tau;
        for (int i = 0; i < n; i++)
        {
            float a = a0 + i * Mathf.Tau / n; var d = new Vector3(-Mathf.Sin(a), .08f, -Mathf.Cos(a)).Normalized();
            var p = Projectile.Spawn(vis, 'p', at + Vector3.Up * .9f + d * .4f, d * f.Speed * .8f, f.Dmg, 1.1f, Crit(f), false, f.Radius * .6f, false);
            p.Calc = f; p.PierceLeft = f.Pierce; p.ChainLeft = f.Chain; p.Homing = f.Homing; p.Tint = col;
            if (next > 0) p.OnEnd = pt => { if (fired) return; fired = true; FollowUp(f, pt, 0, next); };
            SpellFx.SkillAttach(k.Id, "travel", p);
        }
    }

    /// <summary>A chain as a chained section: it leaps from where the section before it ended to the foes nearby. Returns where it stopped.</summary>
    Vector3 ArcFrom(SkillCalc f, Vector3 at, Enemy? first)
    {
        var col = Dmg.TypeColor(f.Type); var seen = new HashSet<Enemy>(); var prev = at + Vector3.Up; var cur = first; float dm = f.Dmg; var end = at;
        int hits = Mathf.Max(1, f.Chain);
        for (int j = 0; j < hits && cur != null; j++)
        {
            var pt = cur.GlobalPosition + new Vector3(0, cur.HeightM() * .55f, 0); Vfx.Lightning(prev, pt, col, .28f); seen.Add(cur);
            BeamFx(f.Def.Id, prev, pt); SpellFx.Skill(f.Def.Id, "hit", pt);
            end = cur.GlobalPosition;
            cur.TakeHit(f.Hit(dm, at, Crit(f))); dm *= .82f; prev = pt; cur = NearestFoe(f.Jump, cur.GlobalPosition, seen);
        }
        return end;
    }
}
