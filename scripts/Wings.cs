using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>
/// Golden Wings (Scarletta's raid chest): a cape-slot item. Worn, they let the hero fly: press Jump in the air to take off,
/// hold Jump to climb and Roll to dive; flying drains stamina like sprinting and moves three times as fast as running.
/// Each pair rolls its own random set of bonus stats (tree stat keys, shown on the item and added to the stat table).
/// </summary>
public static class Wings
{
    public const string Name = "Golden Wings";
    /// <summary>The bonus pool: stat key and the range it rolls in at level 1 and its growth per level.</summary>
    static readonly (string key, float lo, float hi, float perLvl)[] Pool =
    {
        ("allP", 4, 8, .12f), ("crit", 3, 6, .05f), ("critD", 10, 20, .3f), ("aspd", 4, 8, .08f), ("move", 4, 8, .05f), ("hpP", 5, 10, .12f),
        ("mpP", 6, 12, .12f), ("cdr", 4, 8, .06f), ("leech", 1, 2, .02f), ("regenHp", 1, 3, .15f), ("evade", 3, 6, .05f), ("tenacity", 8, 15, .15f),
        ("skillP", 5, 10, .12f), ("healP", 6, 12, .12f), ("res_poison", 10, 20, .2f), ("armorPenP", 5, 10, .1f), ("magicPenP", 5, 10, .1f), ("castSpd", 5, 10, .08f),
    };

    public static bool Worn => Game.D.Eq.GetValueOrDefault("cape")?.Wings == true;

    public static Item Make(int level)
    {
        var it = Items.Roll(level, 4f, "cape", 4, "");   // a legendary cape's defense and mana
        it.Name = Name; it.Wings = true; it.Elem = null; it.Mods = new Dictionary<string, float>();
        var pool = new List<int>(); for (int i = 0; i < Pool.Length; i++) pool.Add(i);
        int n = GD.RandRange(3, 5);
        for (int k = 0; k < n && pool.Count > 0; k++)
        {
            int pi = pool[GD.RandRange(0, pool.Count - 1)]; pool.Remove(pi); var (key, lo, hi, per) = Pool[pi];
            it.Mods[key] = Mathf.Round(((float)GD.RandRange(lo, hi) + per * level) * 10f) / 10f;
        }
        return it;
    }
}
