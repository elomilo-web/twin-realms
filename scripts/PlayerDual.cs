using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>
/// Partial: which weapon is swinging (A-12 dual wield: main hand or off hand), its item, element and damage; the guard rules with two
/// weapons; weapon requirements of skills ("bow", "dual" tags); attribute-driven multipliers (A-14: roll cost, buff duration).
/// </summary>
public partial class Player
{
    bool _offSwing;   // the basic swing in progress (or just finished) belongs to the off-hand weapon
    Item? OffItem => DualWield.Off(Game.D);
    /// <summary>A one-handed weapon in each hand.</summary>
    public bool DualWielding => OffItem != null;
    /// <summary>For bots and the HUD: the swing in progress is the off hand's.</summary>
    public bool OffHandSwing => OffNow;
    bool OffNow => _offSwing && _atkSkill == null && OffItem != null;
    /// <summary>The item doing the hitting right now: the off-hand weapon during its swings, else the main weapon.</summary>
    Item? SwingItem => OffNow ? OffItem : Game.D.Weapon;
    /// <summary>Its element (none when it is worn out: a broken weapon hits as bare hands).</summary>
    string? SwingElem { get { var it = SwingItem; return it == null || Durability.Broken(it) ? null : it.Elem; } }
    float SwingDmg => OffNow ? Game.D.OffDmg() : Game.D.WpnDmg();

    /// <summary>Guard can be raised: a shield, or two weapons to cross.</summary>
    bool CanGuard => HasShield || DualWielding;
    float ParryWindow => DualWielding ? Weapons.Dual("parryWindow", .22f) : Tune.F("guard.parryWindow", .15f);
    /// <summary>Share of a blocked blow that is stopped: the shield formula, or the weaker crossed-blades guard when dual wielding.</summary>
    float GuardReduction()
    {
        float blk = Game.TS["block"] / 100f;
        if (DualWielding) return Mathf.Clamp(Weapons.Dual("guardReduction", .3f) + blk, .1f, .6f);
        var sh = Game.D.Eq.GetValueOrDefault("shield"); float sd = sh == null || Durability.Broken(sh) ? 0 : sh.Def;   // a worn-out shield adds nothing of its own
        return Mathf.Clamp(0.55f + sd * 0.012f + blk, 0.5f, 0.92f);
    }

    static int ChainLenOf(WeaponDef w) => Attacks.HasStyle(w.Id) ? Attacks.Build(w.Id).Chain.Count : w.Combo.Count;

    /// <summary>A skill's weapon requirement (its tags): "bow" needs a bow or crossbow, "dual" needs a weapon in each hand.</summary>
    public bool WeaponTagOk(SkillDef k, out string why)
    {
        why = "";
        if (k.Tags.Contains("bow") && Game.D.WType() is not ("bow" or "crossbow")) { why = k.Name + " needs a bow"; return false; }
        if (k.Tags.Contains("dual") && !DualWielding) { why = k.Name + " needs a weapon in each hand (Shift+click a one-handed weapon to put it in the off hand)"; return false; }
        return true;
    }

    /// <summary>A-14 Agility: the roll's stamina cost (rollCostP, capped in data/stats.json).</summary>
    public static float RollCostMul => Mathf.Clamp(1 + StatPipeline.Key("rollCostP") / 100f, .3f, 2f);
    /// <summary>A-14 Wisdom: how long the hero's own buffs last (buffDurP).</summary>
    public static float BuffDurMul => Mathf.Max(.3f, 1 + StatPipeline.Key("buffDurP") / 100f);
}
