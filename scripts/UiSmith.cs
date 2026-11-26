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

    public void OpenSmith()
    {
        var d = Game.D; var v = Frame("smith", "Dorrin the Smith", new Vector2(1120, 660), OpenSmith); HideTip();
        if (_anvil != null && !d.Inv.Contains(_anvil) && !d.Eq.Values.Contains(_anvil)) _anvil = null;   // sold, dropped or scrapped since
        var top = new HBoxContainer(); top.AddThemeConstantOverride("separation", 18); v.AddChild(top);
        Lbl($"{d.Gold} G", 18, Gold, top);
        CraftHint(top, "Put a piece on the anvil: hone it, reroll its bonus stats, rename it, or change its glow.", 14).SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var cols = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; cols.AddThemeConstantOverride("separation", 18); v.AddChild(cols);

        // ---- left: your gear (worn first, then the pack)
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(330, 0) }; left.AddThemeConstantOverride("separation", 6); cols.AddChild(left);
        Header("Your gear", left); Lbl("Click a piece to put it on the anvil.", 12, Dim, left);
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
        Header("The anvil", mid);
        if (_anvil == null) CraftHint(mid, "Nothing on the anvil yet: pick a piece on the left.", 15);
        else
        {
            var ms = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 420), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; mid.AddChild(ms);
            var mb = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; mb.AddThemeConstantOverride("separation", 8); ms.AddChild(mb);
            SmithAnvil(mb, _anvil);
        }

        // ---- right: material trading
        var right = new VBoxContainer { CustomMinimumSize = new Vector2(280, 0) }; right.AddThemeConstantOverride("separation", 6); cols.AddChild(right);
        SmithMaterials(right);
        v.AddChild(Btn("Leave", Close));
    }

    static Item? _smithFlash;
    static int HoneUp(int x) { float k = 1 + Affixes.HonePer; return x <= 0 ? x : Math.Max(x + 1, Mathf.RoundToInt(x * k)); }

    void SmithAnvil(VBoxContainer box, Item it)
    {
        var d = Game.D; bool worn = d.Eq.Values.Contains(it);
        var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 12); box.AddChild(head);
        var stage = new Control { CustomMinimumSize = new Vector2(96, 96) }; head.AddChild(stage);
        var big = new ItemSlot { Item = it }; big.SetAnchorsPreset(Control.LayoutPreset.FullRect); stage.AddChild(big);
        var hv = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; hv.AddThemeConstantOverride("separation", 2); head.AddChild(hv);
        Lbl(it.Name, 19, it.Color.Lightened(.15f), hv); Lbl($"{Items.RarityName(it.Rar)} {it.Slot}   Lv {it.Lvl}   +{it.Up}/{MaxHone}" + (worn ? "   (worn)" : ""), 13, Dim, hv);
        if (_smithFlash == it)
        {
            // success: a short gold flash over the slot and a rising "Upgraded to +N"
            _smithFlash = null;
            var fl = new ColorRect { Color = new Color(1f, .82f, .3f, .75f), MouseFilter = Control.MouseFilterEnum.Ignore }; fl.SetAnchorsPreset(Control.LayoutPreset.FullRect); stage.AddChild(fl);
            var ft = fl.CreateTween(); ft.TweenProperty(fl, "color:a", 0f, .7); ft.TweenCallback(Callable.From(fl.QueueFree));
            var up = Lbl($"Upgraded to +{it.Up}", 17, UiTheme.Highlight, hv);
            var ut = up.CreateTween(); ut.TweenInterval(1.6); ut.TweenProperty(up, "modulate:a", 0f, .8);
        }

        // hone: current vs next stats side by side
        Header("Hone", box);
        bool maxed = it.Up >= MaxHone; float k = 1 + Affixes.HonePer;
        var sg = new GridContainer { Columns = 4 }; sg.AddThemeConstantOverride("h_separation", 12); sg.AddThemeConstantOverride("v_separation", 2); box.AddChild(sg);
        void Stat(string n, string cur, string? next, string? delta)
        {
            Lbl(n, 13, Dim, sg); Lbl(cur, 14, Colors.White, sg);
            if (next == null) { Lbl("", 13, Dim, sg); Lbl("", 13, Dim, sg); return; }
            Lbl("->", 13, Dim, sg); Lbl(next + (delta != null ? "  " + delta : ""), 14, Good, sg);
        }
        void Num(string n, int cur) { if (cur <= 0) return; if (maxed) Stat(n, $"{cur}", null, null); else { int nx = HoneUp(cur); Stat(n, $"{cur}", $"{nx}", $"(+{nx - cur})"); } }
        Num("Damage", it.Dmg); Num("Defense", it.Def); Num("Health", it.Hp); Num("Mana", it.Mp);
        if (it.Sp > 0) { if (maxed) Stat("Spell power", $"+{it.Sp * 100:0}%", null, null); else { double nx = Math.Round(it.Sp * k, 3); Stat("Spell power", $"+{it.Sp * 100:0}%", $"+{nx * 100:0}%", $"(+{(nx - it.Sp) * 100:0.#}%)"); } }
        if (it.Mods != null) foreach (var kv in it.Mods)
        {
            if (maxed) Stat("Bonus", TreeDb.StatText(kv.Key, kv.Value), null, null);
            else Stat("Bonus", TreeDb.StatText(kv.Key, kv.Value), TreeDb.StatText(kv.Key, Affixes.Round(kv.Key, kv.Value * k)), null);
        }
        if (maxed) CraftHint(box, "Honed as far as steel allows (+10).");
        else
        {
            var (gold, mats) = HoneCost(it);
            var cr = new HBoxContainer(); cr.AddThemeConstantOverride("separation", 10); box.AddChild(cr);
            Lbl($"Cost  {gold} G", 15, d.Gold >= gold ? UiTheme.Highlight : Bad, cr);
            foreach (var (id, need) in mats)
            {
                int got = d.Mats.GetValueOrDefault(id); var cell = new VBoxContainer(); cell.AddThemeConstantOverride("separation", 0); cr.AddChild(cell);
                var w = new CenterContainer(); w.AddChild(IngSlot(id, 38)); cell.AddChild(w); var cl = Lbl($"{got}/{need}", 12, got >= need ? Good : Bad, cell); cl.HorizontalAlignment = HorizontalAlignment.Center;
            }
            var hr = new HBoxContainer(); hr.AddThemeConstantOverride("separation", 8); box.AddChild(hr);
            var hb = Btn($"Hone to +{it.Up + 1}", () =>
            {
                if (d.Gold < gold) { Note("Not enough gold", new Color(1, .5f, .5f)); Sfx.UiError(); return; }
                if (!CanPay(mats)) { Note("Not enough materials", new Color(1, .5f, .5f)); Sfx.UiError(); return; }
                d.Gold -= gold; Pay(mats); Hone(it); LifeSkills.Award("smithing", 8 + it.Up * 3); Sfx.Play("hit"); Sfx.Play("coin"); Sfx.UiConfirm(); Note($"{it.Name}: +{Mathf.RoundToInt(Affixes.HonePer * 100)}% to its stats", it.Color); Game.Notify(); Game.Save(); _smithFlash = it; OpenSmith();
            }); hb.CustomMinimumSize = new Vector2(150, 36); hb.AddThemeColorOverride("font_color", UiTheme.Highlight); hr.AddChild(hb);
            Lbl($"+{Mathf.RoundToInt(Affixes.HonePer * 100)}% to base and bonus stats", 12, Dim, hr);
        }

        // reroll
        Header("Reroll bonus stats", box);
        int nm = it.Mods?.Count ?? 0;
        if (nm == 0) CraftHint(box, "This piece has no bonus stats (epic and legendary gear rolls them).");
        else
        {
            int cost = RerollCost();
            box.AddChild(Btn($"Reroll its {nm} bonus stats  ({cost}g, 3% of your gold)", () =>
            {
                if (d.Gold < cost) { Note("Not enough gold", new Color(1, .5f, .5f)); Sfx.UiError(); return; }
                d.Gold -= cost; Affixes.Reroll(it); Sfx.Play("level"); Sfx.UiConfirm(); Note($"New bonus stats on {it.Name}", it.Color); Game.Notify(); Game.Save(); OpenSmith();
            }, true));
        }

        // rename
        Header("Rename", box);
        var rr = new HBoxContainer(); rr.AddThemeConstantOverride("separation", 6); box.AddChild(rr);
        var name = new LineEdit { Text = BareName(it.Name), MaxLength = 32, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; rr.AddChild(name);
        void DoRename() { var t = name.Text.Trim(); if (t == "") return; it.Name = t + (it.Up > 0 ? $" +{it.Up}" : ""); Sfx.Play("click"); Game.Notify(); Game.Save(); OpenSmith(); }
        name.TextSubmitted += _ => DoRename(); rr.AddChild(Btn("Rename", DoRename, true));

        // glow colour
        Header("Glow colour", box); Lbl("How strongly it glows comes from its tier, bonus stats and hone.", 12, Dim, box);
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
        int up = it.Up; int gold = (int)((60 * Math.Pow(up + 1, 1.6) + it.Lvl * 12 * (1 + it.Rar * .5f)) * Math.Max(.5f, 1 + StatPipeline.Key("smithCost") / 100f));   // F-25: smithing levels lower the price
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
        Header("Trade materials   10 for 1", box);
        var have = all.Where(m => d.Mats.GetValueOrDefault(m.Id) >= 10).ToList();
        if (have.Count == 0) { CraftHint(box, "You need at least 10 of a material to trade it."); return; }
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
