using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TwinRealms;

/// <summary>
/// A-13 at the smith (hooks: one line each in UiSmith.cs): the anvil's Durability section (repair one piece, or everything worn) and the
/// Smelting section (scraps, ores and other ingredients into metal bars). Recipes and costs: data/durability.json.
/// </summary>
public partial class Ui
{
    static string BarCostText(Dictionary<string, int> mats) => string.Join(", ", mats.Select(kv => $"{kv.Value} {GatherDb.Mat(kv.Key).Name}"));

    void SmithRepair(VBoxContainer box, Item it)
    {
        var d = Game.D; Header("Durability", box);
        if (!Durability.Has(it)) { CraftHint(box, "This piece does not wear out."); }
        else
        {
            float f = Durability.Frac(it); bool broken = Durability.Broken(it);
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10); box.AddChild(row);
            var bar = new ProgressBar { MinValue = 0, MaxValue = 1, Value = f, ShowPercentage = false, CustomMinimumSize = new Vector2(180, 10), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore };
            bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = f <= .2f ? Bad : f <= .5f ? new Color(.95f, .85f, .35f) : Good }); bar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color(0, 0, 0, .55f) });
            row.AddChild(bar);
            Lbl($"{MathF.Ceiling(Durability.Cur(it)):0} / {Durability.Max(it):0}" + (broken ? "   WORN OUT: gives no stats" : ""), 14, broken ? Bad : Colors.White, row);
            var (gold, mats) = Durability.RepairCost(it);
            if (mats.Count == 0) CraftHint(box, $"In good repair. It wears with use; at 0 it gives no stats until repaired. Repairs take {GatherDb.Mat(Durability.BarFor(it)).Name}s.");
            else
            {
                gold = Cost(gold);
                var cr = new HBoxContainer(); cr.AddThemeConstantOverride("separation", 10); box.AddChild(cr);
                Lbl($"Cost  {gold} G", 15, d.Gold >= gold ? UiTheme.Highlight : Bad, cr);
                foreach (var (id, need) in mats)
                {
                    int got = d.Mats.GetValueOrDefault(id); var cell = new VBoxContainer { TooltipText = GatherDb.Mat(id).Name }; cell.AddThemeConstantOverride("separation", 0); cr.AddChild(cell);
                    var w = new CenterContainer(); w.AddChild(IngSlot(id, 38)); cell.AddChild(w); var cl = Lbl($"{got}/{need}", 12, got >= need ? Good : Bad, cell); cl.HorizontalAlignment = HorizontalAlignment.Center;
                }
                var rb = Btn("Repair", () =>
                {
                    if (d.Gold < gold) { Note("Not enough gold", new Color(1, .5f, .5f)); Sfx.UiError(); return; }
                    if (!CanPay(mats)) { Note("Not enough materials: " + BarCostText(mats) + " (smelt bars on the right)", new Color(1, .5f, .5f)); Sfx.UiError(); return; }
                    d.Gold -= gold; Pay(mats); Durability.Repair(it); Traded(); LifeSkills.Award("smithing", 4 + it.Rar * 2); Sfx.Play("hit"); Sfx.Play("coin"); Sfx.UiConfirm();
                    Note($"{it.Name} repaired", it.Color); Game.Notify(); Game.Save(); OpenSmith();
                }); rb.CustomMinimumSize = new Vector2(120, 34); rb.AddThemeColorOverride("font_color", UiTheme.Highlight); cr.AddChild(rb);
            }
        }
        // everything worn at once
        var wornList = d.Eq.Values.Where(x => x != null && Durability.Has(x) && Durability.RepairCost(x).mats.Count > 0).Cast<Item>().ToList();
        if (wornList.Count > 1 || (wornList.Count == 1 && wornList[0] != it))
        {
            int tg = 0; var tm = new Dictionary<string, int>();
            foreach (var w in wornList) { var (g1, m1) = Durability.RepairCost(w); tg += Cost(g1); foreach (var (id, n) in m1) tm[id] = tm.GetValueOrDefault(id) + n; }
            bool can = d.Gold >= tg && CanPay(tm);
            var all = Btn($"Repair all worn gear ({wornList.Count})   {tg} G, {BarCostText(tm)}", () =>
            {
                if (d.Gold < tg) { Note("Not enough gold", new Color(1, .5f, .5f)); Sfx.UiError(); return; }
                if (!CanPay(tm)) { Note("Not enough materials: " + BarCostText(tm), new Color(1, .5f, .5f)); Sfx.UiError(); return; }
                d.Gold -= tg; Pay(tm); foreach (var w in wornList) Durability.Repair(w); Traded(); LifeSkills.Award("smithing", 6 * wornList.Count); Sfx.Play("hit"); Sfx.Play("coin"); Sfx.UiConfirm();
                Note($"Repaired {wornList.Count} pieces", UiTheme.Highlight); Game.Notify(); Game.Save(); OpenSmith();
            }, true);
            all.ClipText = true; all.TooltipText = $"{tg} G, {BarCostText(tm)}"; if (!can) all.AddThemeColorOverride("font_color", Bad);
            box.AddChild(all);
        }
    }

    /// <summary>Smelting: every recipe of data/durability.json "smelt" with what you hold; one bar at a time or as many as you can.</summary>
    void SmithSmelt(VBoxContainer box)
    {
        var d = Game.D; Durability.Load(); if (Durability.Smelts.Count == 0) return;
        Header("Smelt metal bars", box);
        var sc = new ScrollContainer { CustomMinimumSize = new Vector2(280, 250), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; box.AddChild(sc);
        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; list.AddThemeConstantOverride("separation", 4); sc.AddChild(list);
        foreach (var s0 in Durability.Smelts)
        {
            var s = s0; var m = GatherDb.Mat(s.Out); int gold = Cost(s.Gold);
            int can = s.In.Min(kv => d.Mats.GetValueOrDefault(kv.Key) / Math.Max(1, kv.Value)); if (gold > 0) can = Math.Min(can, d.Gold / gold);
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 6); list.AddChild(row);
            var tv = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; tv.AddThemeConstantOverride("separation", -2); row.AddChild(tv);
            Lbl($"{m.Name}{(s.N > 1 ? " x" + s.N : "")}   (have {d.Mats.GetValueOrDefault(s.Out)})", 13, m.Col.Lightened(.25f), tv);
            var cl = Lbl($"{BarCostText(s.In)}, {gold} G", 11, can > 0 ? Dim : Bad, tv); cl.ClipText = true; cl.TooltipText = cl.Text; cl.MouseFilter = Control.MouseFilterEnum.Pass;
            void Smelt(int times)
            {
                times = Math.Min(times, can); if (times <= 0) { Note("You lack the ingredients: " + BarCostText(s.In) + $", {gold} G", new Color(1, .5f, .5f)); Sfx.UiError(); return; }
                for (int i = 0; i < times; i++) Pay(s.In);
                d.Gold -= gold * times; d.Mats[s.Out] = d.Mats.GetValueOrDefault(s.Out) + s.N * times; Traded(); LifeSkills.Award("smithing", 2 * times);
                Note($"Smelted {s.N * times} {m.Name}", m.Col.Lightened(.25f)); Sfx.Play("hit"); Sfx.UiConfirm(); Game.Notify(); Game.Save(); OpenSmith();
            }
            var b1 = Btn("1", () => Smelt(1), true); b1.CustomMinimumSize = new Vector2(30, 0); row.AddChild(b1);
            var bn = Btn("All", () => Smelt(int.MaxValue), true); bn.CustomMinimumSize = new Vector2(38, 0); row.AddChild(bn);
            if (can <= 0) { b1.Modulate = new Color(1, 1, 1, .5f); bn.Modulate = new Color(1, 1, 1, .5f); }
        }
    }
}
