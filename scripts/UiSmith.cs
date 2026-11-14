using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;

namespace TwinRealms;

/// <summary>
/// Dorrin the Smith: put any piece of gear (worn or in the pack) on the anvil, then
///  - hone it, up to +10: each step raises its base stats and its bonus stats (Affixes.HonePer), for gold and scraps;
///  - reroll its bonus stats: new stats, the same number of them, for 3% of your gold;
///  - rename it (the +N stays);
///  - choose its glow colour (how much it glows still comes from its tier, bonus stats and hone);
/// and trade any crafting material for another, ten for one.
/// </summary>
public partial class Ui
{
    static Item? _anvil; static string _matFrom = "", _matTo = "";
    public const int MaxHone = 10;
    static readonly (string name, string hex)[] GlowColors =
    {
        ("Gold", "ffd24a"), ("Crimson", "ff3a4a"), ("Ember", "ff8a2a"), ("Verdant", "5cff7a"), ("Frost", "8ae4ff"), ("Azure", "4a8aff"),
        ("Violet", "b86bff"), ("Rose", "ff7ad2"), ("Pearl", "f4f2ea"), ("Void", "6a3aff"),
    };

    void OpenSmith()
    {
        var d = Game.D; var v = Frame("smith", "Dorrin the Smith", new Vector2(1080, 660), OpenSmith); HideTip();
        if (_anvil != null && !d.Inv.Contains(_anvil) && !d.Eq.Values.Contains(_anvil)) _anvil = null;   // sold, dropped or scrapped since
        var top = new HBoxContainer(); top.AddThemeConstantOverride("separation", 18); v.AddChild(top);
        Lbl($"{d.Gold} G", 18, Gold, top);
        Lbl("Put a piece on the anvil: hone it, reroll its bonus stats, rename it, or change its glow.", 14, Dim, top);
        var cols = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; cols.AddThemeConstantOverride("separation", 18); v.AddChild(cols);

        // ---- left: your gear (worn first, then the pack)
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(330, 0) }; left.AddThemeConstantOverride("separation", 6); cols.AddChild(left);
        Lbl("YOUR GEAR   click to put it on the anvil", 15, UiTheme.Accent, left);
        var gs = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(330, 440), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; left.AddChild(gs);
        var grid = new GridContainer { Columns = 5 }; grid.AddThemeConstantOverride("h_separation", 5); grid.AddThemeConstantOverride("v_separation", 5); gs.AddChild(grid);
        foreach (var it in d.Eq.Values.Where(x => x != null).Cast<Item>().Concat(d.Inv))
        {
            var item = it; var cell = new ItemSlot { Item = item, CustomMinimumSize = new Vector2(60, 60), TooltipText = item == _anvil ? "On the anvil" : "" }; grid.AddChild(cell);
            if (item == _anvil) cell.Modulate = new Color(1.2f, 1.15f, .8f);
            cell.Hover += on => ShowTip(on ? item : null, cell, null);
            cell.Clicked += _ => { _anvil = item; Sfx.Play("click"); OpenSmith(); };
        }

        // ---- middle: the anvil
        var mid = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; mid.AddThemeConstantOverride("separation", 8); cols.AddChild(mid);
        Lbl("THE ANVIL", 15, UiTheme.Accent, mid);
        if (_anvil == null) Row(mid, "Nothing on the anvil yet: pick a piece on the left.", Dim, 15);
        else SmithAnvil(mid, _anvil);

        // ---- right: material trading
        var right = new VBoxContainer { CustomMinimumSize = new Vector2(280, 0) }; right.AddThemeConstantOverride("separation", 6); cols.AddChild(right);
        SmithMaterials(right);
        v.AddChild(Btn("Leave", Close));
    }

    void SmithAnvil(VBoxContainer box, Item it)
    {
        var d = Game.D; bool worn = d.Eq.Values.Contains(it);
        var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 10); box.AddChild(head);
        head.AddChild(new ItemSlot { Item = it, CustomMinimumSize = new Vector2(72, 72) });
        var hv = new VBoxContainer(); head.AddChild(hv);
        Lbl(it.Name, 18, it.Color.Lightened(.15f), hv); Lbl($"{Items.RarityName(it.Rar)} {it.Slot}   Lv {it.Lvl}   +{it.Up}/{MaxHone}" + (worn ? "   (worn)" : ""), 13, Dim, hv);
        var stats = new List<string>(); if (it.Dmg > 0) stats.Add($"{it.Dmg} damage"); if (it.Def > 0) stats.Add($"{it.Def} defense"); if (it.Hp > 0) stats.Add($"{it.Hp} health"); if (it.Mp > 0) stats.Add($"{it.Mp} mana"); if (it.Sp > 0) stats.Add($"+{it.Sp * 100:0}% spell");
        if (stats.Count > 0) Lbl(string.Join("   ", stats), 14, Colors.White, hv);
        if (it.Mods != null) foreach (var kv in it.Mods) Lbl(TreeDb.StatText(kv.Key, kv.Value), 13, new Color(.75f, .92f, 1f), box);

        // hone
        Lbl("HONE", 14, UiTheme.Accent, box);
        if (it.Up >= MaxHone) Row(box, "Honed as far as steel allows (+10).", Dim, 14);
        else
        {
            var (gold, mats) = HoneCost(it);
            var hr = new HBoxContainer(); hr.AddThemeConstantOverride("separation", 8); box.AddChild(hr);
            hr.AddChild(Btn($"Hone to +{it.Up + 1}  ({gold}g{(mats.Count > 0 ? ", " + CostText(mats) : "")})", () =>
            {
                if (d.Gold < gold) { Note("Not enough gold", new Color(1, .5f, .5f)); return; }
                if (!CanPay(mats)) { Note("Not enough materials", new Color(1, .5f, .5f)); return; }
                d.Gold -= gold; Pay(mats); Hone(it); Sfx.Play("hit"); Sfx.Play("coin"); Note($"{it.Name}: +{Mathf.RoundToInt(Affixes.HonePer * 100)}% to its stats", it.Color); Game.Notify(); Game.Save(); OpenSmith();
            }, true));
            Lbl($"+{Mathf.RoundToInt(Affixes.HonePer * 100)}% to base and bonus stats", 12, Dim, hr);
        }

        // reroll
        Lbl("REROLL BONUS STATS", 14, UiTheme.Accent, box);
        int n = it.Mods?.Count ?? 0;
        if (n == 0) Row(box, "This piece has no bonus stats (epic and legendary gear rolls them).", Dim, 14);
        else
        {
            int cost = RerollCost();
            box.AddChild(Btn($"Reroll its {n} bonus stats  ({cost}g, 3% of your gold)", () =>
            {
                if (d.Gold < cost) { Note("Not enough gold", new Color(1, .5f, .5f)); return; }
                d.Gold -= cost; Affixes.Reroll(it); Sfx.Play("level"); Note($"New bonus stats on {it.Name}", it.Color); Game.Notify(); Game.Save(); OpenSmith();
            }, true));
        }

        // rename
        Lbl("RENAME", 14, UiTheme.Accent, box);
        var rr = new HBoxContainer(); rr.AddThemeConstantOverride("separation", 6); box.AddChild(rr);
        var name = new LineEdit { Text = BareName(it.Name), MaxLength = 32, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; rr.AddChild(name);
        void DoRename() { var t = name.Text.Trim(); if (t == "") return; it.Name = t + (it.Up > 0 ? $" +{it.Up}" : ""); Sfx.Play("click"); Game.Notify(); Game.Save(); OpenSmith(); }
        name.TextSubmitted += _ => DoRename(); rr.AddChild(Btn("Rename", DoRename, true));

        // glow colour
        Lbl("GLOW COLOUR   how strongly it glows comes from its tier, bonus stats and hone", 14, UiTheme.Accent, box);
        var gr = new GridContainer { Columns = 6 }; gr.AddThemeConstantOverride("h_separation", 4); gr.AddThemeConstantOverride("v_separation", 4); box.AddChild(gr);
        var def = Btn("Default", () => { it.Glow = null; Game.Notify(); Game.Save(); OpenSmith(); }, true); Mark(def, string.IsNullOrEmpty(it.Glow)); gr.AddChild(def);
        foreach (var (cn, hex) in GlowColors)
        {
            string h = hex; var b = Btn(cn, () => { it.Glow = h; Sfx.Play("click"); Game.Notify(); Game.Save(); OpenSmith(); }, true);
            b.AddThemeColorOverride("font_color", Color.FromHtml(hex)); Mark(b, it.Glow == hex); gr.AddChild(b);
        }
    }

    /// <summary>Gold and scraps for the next hone (steeper with every step, more for higher level and rarity).</summary>
    static (int gold, Dictionary<string, int> mats) HoneCost(Item it)
    {
        int up = it.Up; int gold = (int)(60 * Math.Pow(up + 1, 1.6) + it.Lvl * 12 * (1 + it.Rar * .5f));
        var m = new Dictionary<string, int> { ["ironscrap"] = 2 + 2 * up };
        if (up >= 4) m["runeshard"] = up - 2; if (up >= 8) m["riftprism"] = 1;
        return (gold, m);
    }
    /// <summary>A reroll costs 3% of the gold you carry (at least 25).</summary>
    static int RerollCost() => Math.Max(25, Mathf.CeilToInt(Game.D.Gold * .03f));
    static string BareName(string n) => Regex.Replace(n, @" \+\d+$", "");
    /// <summary>One step of honing: base stats and bonus stats grow by Affixes.HonePer; the +N on the name follows.</summary>
    static void Hone(Item it)
    {
        float k = 1 + Affixes.HonePer;
        int Up(int x) => x <= 0 ? x : Math.Max(x + 1, Mathf.RoundToInt(x * k));
        it.Dmg = Up(it.Dmg); it.Def = Up(it.Def); it.Hp = Up(it.Hp); it.Mp = Up(it.Mp); if (it.Sp > 0) it.Sp = (float)Math.Round(it.Sp * k, 3);
        if (it.Mods != null) foreach (var key in it.Mods.Keys.ToList()) it.Mods[key] = Affixes.Round(key, it.Mods[key] * k);
        it.Up++; it.Name = BareName(it.Name) + $" +{it.Up}";
    }

    /// <summary>Trade any crafting material (gathered or scrap) for another: ten for one.</summary>
    void SmithMaterials(VBoxContainer box)
    {
        var d = Game.D; GatherDb.Mat(""); var all = GatherDb.Mats.Values.OrderBy(m => m.Name).ToList();
        Lbl("TRADE MATERIALS   10 for 1", 15, UiTheme.Accent, box);
        var have = all.Where(m => d.Mats.GetValueOrDefault(m.Id) >= 10).ToList();
        if (have.Count == 0) { Row(box, "You need at least 10 of a material to trade it.", Dim, 14); return; }
        if (!have.Any(m => m.Id == _matFrom)) _matFrom = have[0].Id;
        if (_matTo == "" || _matTo == _matFrom || !all.Any(m => m.Id == _matTo)) _matTo = all.First(m => m.Id != _matFrom).Id;
        Lbl("Give", 13, Dim, box);
        var from = new OptionButton(); for (int i = 0; i < have.Count; i++) { from.AddItem($"{have[i].Name}  ({d.Mats.GetValueOrDefault(have[i].Id)})"); from.SetItemMetadata(i, have[i].Id); if (have[i].Id == _matFrom) from.Select(i); }
        from.ItemSelected += i => { _matFrom = from.GetItemMetadata((int)i).AsString(); OpenSmith(); }; box.AddChild(from);
        Lbl("Get", 13, Dim, box);
        var targets = all.Where(m => m.Id != _matFrom).ToList();
        var to = new OptionButton(); for (int i = 0; i < targets.Count; i++) { to.AddItem($"{targets[i].Name}  ({d.Mats.GetValueOrDefault(targets[i].Id)})"); to.SetItemMetadata(i, targets[i].Id); if (targets[i].Id == _matTo) to.Select(i); }
        to.ItemSelected += i => { _matTo = to.GetItemMetadata((int)i).AsString(); OpenSmith(); }; box.AddChild(to);
        void Trade(int lots)
        {
            int can = d.Mats.GetValueOrDefault(_matFrom) / 10; lots = Math.Min(lots, can); if (lots <= 0) { Note("You need 10 to trade", new Color(1, .5f, .5f)); return; }
            d.Mats[_matFrom] -= lots * 10; if (d.Mats[_matFrom] <= 0) d.Mats.Remove(_matFrom); d.Mats[_matTo] = d.Mats.GetValueOrDefault(_matTo) + lots;
            Note($"Traded {lots * 10} {GatherDb.Mat(_matFrom).Name} for {lots} {GatherDb.Mat(_matTo).Name}", UiTheme.Highlight); Sfx.Play("coin"); Game.Notify(); Game.Save(); OpenSmith();
        }
        box.AddChild(Btn("Trade 10 for 1", () => Trade(1), true));
        box.AddChild(Btn("Trade all I can", () => Trade(int.MaxValue), true));
    }
}
