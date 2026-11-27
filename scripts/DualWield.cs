using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>
/// A-12 dual wield: any one-handed weapon can sit in the off-hand slot (the "shield" equipment slot) instead of a shield; a two-handed
/// main weapon still blocks that slot. Numbers in data/weapons.json "dual". While dual wielding (Player.cs / PlayerDual.cs):
///  - light attacks alternate main hand, off hand; each hand walks its own weapon class's chain and uses that class's numbers,
///    the off-hand item's own damage (derived stat "offDmg", x offDamageMul), element, size and sounds; power attacks use the main hand;
///  - attack speed +aspdP%, basic attacks cost x staminaMul stamina;
///  - no shield block: holding Guard crosses the blades: a longer parry window (parryWindow) but only guardReduction of a blow stopped;
///  - both weapons' bonus stats, gems and spell power count (they are worn items like any other).
/// </summary>
public static class DualWield
{
    /// <summary>Can this item go in the off hand: a one-handed weapon.</summary>
    public static bool CanOffHand(Item? it) => it != null && it.Slot == "weapon" && it.Wt != null && !it.Two && Weapons.Get(it.Wt).Hands < 2;
    /// <summary>The off-hand weapon, when the hero is dual wielding (a one-handed main weapon and a weapon in the shield slot).</summary>
    public static Item? Off(PlayerStats d)
    {
        var o = d.Eq.GetValueOrDefault("shield"); if (o == null || o.Slot != "weapon" || o.Wt == null) return null;
        var m = d.Weapon; if (m == null || m.Two || Weapons.Get(m.Wt ?? "sword").Hands > 1) return null;
        return o;
    }
    public static bool Active(PlayerStats d) => Off(d) != null;

    /// <summary>
    /// Pack click on a one-handed weapon: Shift+click puts it in the off hand; on a controller it goes there by itself when the main hand
    /// holds a one-handed weapon and the off hand is free. True = handled (the caller refreshes); false = equip to the main hand as before.
    /// </summary>
    public static bool TryEquipOff(PlayerStats d, int idx)
    {
        if (idx < 0 || idx >= d.Inv.Count) return false; var it = d.Inv[idx]; if (!CanOffHand(it)) return false;
        var main = d.Weapon; var cur = d.Eq.GetValueOrDefault("shield");
        bool mainOne = main != null && !main.Two && Weapons.Get(main.Wt ?? "sword").Hands < 2;
        bool want = Input.IsKeyPressed(Key.Shift) || (Inputs.UsingPad && mainOne && cur == null);
        if (!want) return false;
        if (main == null) return false;   // nothing in the main hand: it goes there
        if (!mainOne) { Ui.Note("Your weapon needs both hands", new Color(1, .6f, .5f)); return true; }
        d.Inv.RemoveAt(idx); d.Eq["shield"] = it; if (cur != null) d.Inv.Add(cur);
        Ui.Note($"{it.Name} in the off hand: attacks alternate hands, +{Weapons.Dual("aspdP", 15):0}% attack speed, off-hand hits x{Weapons.Dual("offDamageMul", .7f):0.##}", new Color(1f, .85f, .5f));
        return true;
    }
}
