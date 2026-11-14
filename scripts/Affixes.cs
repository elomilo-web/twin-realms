using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>
/// Bonus stats on epic and legendary gear (Item.Mods, in tree stat keys, added to the stat table while worn and stacking across
/// items): epic rolls 2 to 4, legendary 3 to 5. The pool covers damage of every kind, defences, health and mana, cooldowns, speeds,
/// crits, life steal, resistances, experience, gold find and luck. Values grow with item level and are higher on legendaries.
/// The smith can reroll them (same number of stats). Luck is pooled across the party (Luck.Party).
/// </summary>
public static class Affixes
{
    /// <summary>key, value range at level 1, growth per item level.</summary>
    static readonly (string key, float lo, float hi, float per)[] Pool =
    {
        // damage
        ("allP", 3, 6, .1f), ("meleeP", 4, 8, .14f), ("rangedP", 4, 8, .14f), ("spellP", 4, 8, .14f), ("skillP", 4, 8, .12f), ("powerP", 6, 12, .16f), ("finisherP", 6, 12, .16f),
        ("physicalP", 5, 10, .15f), ("fireP", 5, 10, .15f), ("frostP", 5, 10, .15f), ("lightningP", 5, 10, .15f), ("poisonP", 5, 10, .15f), ("shadowP", 5, 10, .15f), ("holyP", 5, 10, .15f),
        ("crit", 2, 4, .05f), ("critD", 8, 16, .25f), ("armorPenP", 4, 9, .1f), ("magicPenP", 4, 9, .1f), ("aoeP", 5, 10, .12f), ("statusP", 6, 12, .15f),
        // speed and cooldowns
        ("aspd", 3, 7, .08f), ("castSpd", 4, 8, .08f), ("cdr", 3, 6, .06f), ("move", 3, 6, .05f), ("reach", 4, 8, .06f),
        // defences and resources
        ("hp", 6, 14, 2.2f), ("hpP", 3, 7, .1f), ("mp", 5, 12, 1.6f), ("mpP", 3, 7, .1f), ("st", 5, 12, .4f), ("def", 2, 5, .6f), ("mr", 3, 7, .5f),
        ("regenHp", 1, 2.5f, .12f), ("regenMp", .8f, 2, .08f), ("leech", .5f, 1.5f, .02f), ("mleech", .5f, 1.5f, .02f), ("evade", 2, 4, .04f), ("tenacity", 5, 12, .15f), ("block", 4, 8, .1f), ("thorns", 4, 10, .12f),
        ("res_fire", 6, 14, .15f), ("res_frost", 6, 14, .15f), ("res_lightning", 6, 14, .15f), ("res_poison", 6, 14, .15f), ("res_shadow", 6, 14, .15f), ("res_holy", 6, 14, .15f),
        ("healP", 5, 10, .12f), ("healIn", 4, 8, .08f),
        // fortune
        ("xpP", 3, 8, .08f), ("goldP", 6, 15, .2f), ("luck", 4, 10, .12f),
    };

    /// <summary>How many bonus stats an item of this rarity rolls: epic 2-4, legendary 3-5 (none below epic).</summary>
    public static int CountFor(int rar) => rar >= 4 ? GD.RandRange(3, 5) : rar == 3 ? GD.RandRange(2, 4) : 0;

    /// <summary>Gives a new item its bonus stats.</summary>
    public static void Roll(Item it) { int n = CountFor(it.Rar); if (n > 0) Fill(it, n); }
    /// <summary>The smith's reroll: the same number of stats, freshly picked and rolled (honing still applies on top).</summary>
    public static void Reroll(Item it) { int n = it.Mods?.Count ?? 0; if (n > 0) Fill(it, n); }

    static void Fill(Item it, int n)
    {
        it.Mods = new Dictionary<string, float>(); var idx = new List<int>(); for (int i = 0; i < Pool.Length; i++) idx.Add(i);
        float rarMul = it.Rar >= 4 ? 1.3f : 1f, honed = Mathf.Pow(1 + HonePer, it.Up);
        for (int k = 0; k < n && idx.Count > 0; k++)
        {
            int pi = idx[GD.RandRange(0, idx.Count - 1)]; idx.Remove(pi); var (key, lo, hi, per) = Pool[pi];
            float v = ((float)GD.RandRange(lo, hi) + per * Mathf.Max(0, it.Lvl - 1)) * rarMul * honed;
            it.Mods[key] = Round(key, v);
        }
    }
    /// <summary>Flat stats round to whole numbers; percentages to one decimal.</summary>
    public static float Round(string key, float v) => key is "hp" or "mp" or "st" or "def" or "mr" ? Mathf.Round(v) : Mathf.Round(v * 10f) / 10f;

    /// <summary>Each smith hone (+1 to +10) raises the item's base stats and bonus stats by this share.</summary>
    public const float HonePer = .08f;
}

/// <summary>
/// Luck: more items drop, and rarer ones. Every hero's luck (from their gear) is pooled across the party: everyone's drops use the
/// party's total, so luck stacks per item and per player.
/// </summary>
public static class Luck
{
    /// <summary>Our own luck plus every party member's (or just ours outside a party).</summary>
    public static float Party()
    {
        float l = Game.TS["luck"]; var net = Net.I;
        if (Net.Active && net != null && net.MyParty is { } p) foreach (var id in p.Members) if (id != Net.MyId && net.Players.GetValueOrDefault(id) is { } pi) l += pi.Luck;
        return Mathf.Max(0, l);
    }
    /// <summary>Luck for a drop the host rolls on behalf of a hero (their party's total).</summary>
    public static float ForPeer(long peer)
    {
        var net = Net.I; if (!Net.Active || net == null || peer == Net.MyId) return Party();
        var party = net.PartyOf(peer); float l = 0;
        if (party != null) { foreach (var id in party.Members) l += id == Net.MyId ? Game.TS["luck"] : net.Players.GetValueOrDefault(id)?.Luck ?? 0; }
        else l = net.Players.GetValueOrDefault(peer)?.Luck ?? 0;
        return Mathf.Max(0, l);
    }
    /// <summary>Item drop chance with luck (each point of luck = +1% more often).</summary>
    public static float Chance(float c, float luck) => Mathf.Min(1f, c * (1 + luck / 100f));
    /// <summary>Loot bonus with luck (rarer items: 25 luck = one point of loot bonus).</summary>
    public static float Bonus(float b, float luck) => b + luck / 25f;
}
