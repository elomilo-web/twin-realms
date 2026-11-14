using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>Pack extras: the pickup filter and crafting with disassembly scraps (recipes in data/loot.json "crafting").</summary>
public partial class Ui
{
    // ---------------------------------------------------------------- pickup filter
    static readonly string[] FilterSlots = { "weapon", "shield", "head", "armor", "hands", "feet", "cape", "trinket" };
    public void OpenPickFilter()
    {
        var d = Game.D; var v = Frame("pickfilter", "Pickup Filter", new Vector2(640, 470), OpenPickFilter);
        Row(v, "Choose which items you pick up when you walk over them. Gold, potions, named uniques and set pieces are always picked up.", Dim, 14);
        Lbl("PICK UP ITEMS OF AT LEAST", 15, UiTheme.Accent, v);
        var rr = new HBoxContainer(); rr.AddThemeConstantOverride("separation", 4); v.AddChild(rr);
        for (int r = 0; r < 5; r++)
        {
            int k = r; var b = Btn(Items.RarityName(r), () => { d.PickRar = k; OpenPickFilter(); }, true);
            b.AddThemeColorOverride("font_color", Items.RarityColor(r)); b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; Mark(b, d.PickRar == r); rr.AddChild(b);
        }
        Lbl("SLOTS TO PICK UP", 15, UiTheme.Accent, v);
        var sg = new GridContainer { Columns = 4 }; sg.AddThemeConstantOverride("h_separation", 6); sg.AddThemeConstantOverride("v_separation", 6); v.AddChild(sg);
        foreach (var s in FilterSlots)
        {
            string sl = s; bool on = !d.PickSkip.Contains(s);
            var b = Btn((on ? "[x] " : "[  ] ") + Cap(s), () => { if (!d.PickSkip.Remove(sl)) d.PickSkip.Add(sl); OpenPickFilter(); }, true);
            b.CustomMinimumSize = new Vector2(140, 32); Mark(b, on); sg.AddChild(b);
        }
        Lbl("ITEMS THAT DO NOT PASS", 15, UiTheme.Accent, v);
        var mr = new HBoxContainer(); mr.AddThemeConstantOverride("separation", 4); v.AddChild(mr);
        var b0 = Btn("Leave them on the ground", () => { d.PickScrap = false; OpenPickFilter(); }, true); Mark(b0, !d.PickScrap); b0.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; mr.AddChild(b0);
        var b1 = Btn("Disassemble them into scraps", () => { d.PickScrap = true; OpenPickFilter(); }, true); Mark(b1, d.PickScrap); b1.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; mr.AddChild(b1);
        var bot = new HBoxContainer(); bot.AddThemeConstantOverride("separation", 10); v.AddChild(bot);
        bot.AddChild(Btn("Pick up everything", () => { d.PickRar = 0; d.PickSkip.Clear(); d.PickScrap = false; OpenPickFilter(); }));
        bot.AddChild(Btn("Back to the pack", OpenPack));
    }

    // ---------------------------------------------------------------- crafting
    static string _craftSlot = "weapon", _craftElem = "fire";
    static Dictionary<string, int> Cost(JsonElement e)
    {
        var c = new Dictionary<string, int>(); if (e.TryGetProperty("cost", out var k)) foreach (var p in k.EnumerateObject()) c[p.Name] = p.Value.GetInt32(); return c;
    }
    static bool CanPay(Dictionary<string, int> cost) { foreach (var (id, n) in cost) if (Game.D.Mats.GetValueOrDefault(id) < n) return false; return true; }
    static void Pay(Dictionary<string, int> cost) { var m = Game.D.Mats; foreach (var (id, n) in cost) { m[id] = m.GetValueOrDefault(id) - n; if (m[id] <= 0) m.Remove(id); } }
    static string CostText(Dictionary<string, int> cost) => string.Join(",  ", cost.Select(kv => $"{kv.Value} {GatherDb.Mat(kv.Key).Name} ({Game.D.Mats.GetValueOrDefault(kv.Key)})"));

    public void OpenCraft(string tab)
    {
        var d = Game.D; var v = Frame("craft", "Crafting", new Vector2(820, 600), () => OpenCraft(tab));
        var top = new HBoxContainer(); v.AddChild(top);
        Tabs(top, tab, ("gear", "Gear", () => OpenCraft("gear")), ("elem", "Elemental", () => OpenCraft("elem")), ("sets", "Set pieces", () => OpenCraft("sets")), ("pots", "Potions", () => OpenCraft("pots")));
        var back = Btn("Back to the pack", OpenPack, true); top.AddChild(back);
        // scraps on hand
        var have = new HBoxContainer(); have.AddThemeConstantOverride("separation", 6); v.AddChild(have); int shown = 0;
        foreach (var kv in d.Mats.OrderBy(k => k.Key))
        {
            var m = GatherDb.Mat(kv.Key); if (!m.Scrap || kv.Value <= 0) continue;
            have.AddChild(new ItemSlot { IconId = m.Name, Count = kv.Value, Tint = m.Col, CustomMinimumSize = new Vector2(46, 46), TooltipText = $"{m.Name} x{kv.Value}" }); shown++;
        }
        if (shown == 0) Row(v, "No scraps yet. Disassemble items in your pack (the Disassemble button) or set the pickup filter to scrap what you do not want.", Dim, 14);
        var c = LootTables.Root.GetProperty("crafting");
        bool Room() { if (d.Inv.Count >= 28) { Note("Pack is full", new Color(1, .5f, .5f)); return false; } return true; }
        void Give(Item it) { d.Inv.Add(it); Note("Crafted " + it.Name, it.Color); Sfx.Play("level"); Game.Notify(); }
        void SlotPicker(Node parent)
        {
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 4); parent.AddChild(row);
            foreach (var s in FilterSlots) { string sl = s; var b = Btn(Cap(s), () => { _craftSlot = sl; OpenCraft(tab); }, true); Mark(b, _craftSlot == s); b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(b); }
        }
        var sc = Scroll(v, 380); var list = Inner(sc);
        switch (tab)
        {
            case "elem":
            {
                var e = c.GetProperty("elemental"); int rar = (int)JsonUtil.F(e, "rar", 3), lb = (int)JsonUtil.F(e, "levelBonus", 1), cores = (int)JsonUtil.F(e, "cores", 4);
                Row(list, $"Forge {Items.RarityName(rar).ToLower()} gear of the element you choose (level {d.Level + lb}). Needs {cores} cores of that element.", Colors.White, 15);
                SlotPicker(list);
                var er = new HBoxContainer(); er.AddThemeConstantOverride("separation", 4); list.AddChild(er);
                var elemCores = LootTables.Root.GetProperty("scraps").GetProperty("element");
                foreach (var p in elemCores.EnumerateObject()) { string el = p.Name; var b = Btn(GearFx.Get(el)?.Word ?? el, () => { _craftElem = el; OpenCraft(tab); }, true); Mark(b, _craftElem == el); b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; er.AddChild(b); }
                var cost = Cost(e); string core = elemCores.TryGetProperty(_craftElem, out var cj) ? cj.GetString() ?? "" : ""; if (core != "") cost[core] = cost.GetValueOrDefault(core) + cores;
                bool trinket = _craftSlot == "trinket";
                AddRow(list, trinket ? "Trinkets carry no element: pick another slot" : $"{Items.RarityName(rar)} {GearFx.Get(_craftElem)?.Word ?? _craftElem} {_craftSlot}:  {CostText(cost)}", trinket ? Dim : Items.RarityColor(rar), "Craft", () =>
                {
                    if (trinket || !Room()) return; if (!CanPay(cost)) { Note("Not enough materials", new Color(1, .5f, .5f)); return; }
                    Pay(cost); Give(Items.Roll(d.Level + lb, 0, _craftSlot, rar, _craftElem)); OpenCraft(tab);
                });
                break;
            }
            case "sets":
            {
                var e = c.GetProperty("sets"); var cost = Cost(e); int lb = (int)JsonUtil.F(e, "levelBonus", 0);
                // the five forged sets: they never drop; choose the piece you want
                Lbl("FORGED SETS   only made here, never found", 15, UiTheme.Accent, list);
                foreach (var s in LootTables.Sets.Values.Where(x => x.Craft))
                {
                    var sd = s; Row(list, $"{s.Name}   ({Cap(s.Elem)})   {CostText(s.CraftCost)}", s.Col, 15);
                    var pr = new HBoxContainer(); pr.AddThemeConstantOverride("separation", 4); list.AddChild(pr);
                    foreach (var piece in s.Pieces)
                    {
                        string pc = piece; var b = Btn("Forge " + Cap(piece), () =>
                        {
                            if (!Room()) return; if (!CanPay(sd.CraftCost)) { Note("Not enough materials", new Color(1, .5f, .5f)); return; }
                            Pay(sd.CraftCost); Give(LootTables.MakeSetPiece(sd, d.Level + lb, pc)); OpenCraft(tab);
                        }, true);
                        b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; b.AddThemeColorOverride("font_color", s.Col); pr.AddChild(b);
                    }
                    foreach (var (n, mods) in s.Bonuses) Row(list, $"      {n} pieces: " + string.Join(", ", mods.Select(kv => TreeDb.StatText(kv.Key, kv.Value))), Dim, 13);
                }
                Lbl("DUNGEON SETS", 15, UiTheme.Accent, list);
                Row(list, "Forge a random piece of a set. You need to own at least one piece of that set already (worn, in your pack or in storage). Set pieces drop from floor 10 and deeper of their dungeon.", Colors.White, 15);
                foreach (var s in LootTables.Sets.Values.Where(x => !x.Craft))
                {
                    bool owns = d.Eq.Values.Any(i => i?.Set == s.Id) || d.Inv.Any(i => i.Set == s.Id) || d.Stash.Any(i => i.Set == s.Id);
                    var sd = s;
                    AddRow(list, $"{s.Name}  ({string.Join(", ", s.Pieces)}){(owns ? "" : "  - find a piece first")}\n{CostText(cost)}", owns ? s.Col : Dim, "Craft", () =>
                    {
                        if (!owns) { Note("Find a piece of this set first", new Color(1, .6f, .5f)); return; }
                        if (!Room()) return; if (!CanPay(cost)) { Note("Not enough materials", new Color(1, .5f, .5f)); return; }
                        Pay(cost); Give(LootTables.MakeSetPiece(sd, d.Level + lb)); OpenCraft(tab);
                    });
                    foreach (var (n, mods) in s.Bonuses) Row(list, $"      {n} pieces: " + string.Join(", ", mods.Select(kv => TreeDb.StatText(kv.Key, kv.Value))), Dim, 13);
                }
                break;
            }
            case "pots":
                foreach (var e in c.GetProperty("potions").EnumerateArray())
                {
                    string pot = JsonUtil.S(e, "pot", "hp"); int n = (int)JsonUtil.F(e, "n", 1); var cost = Cost(e);
                    AddRow(list, $"{n} {(pot == "hp" ? "health" : "mana")} potions  (have {(pot == "hp" ? d.HpPot : d.MpPot)}):  {CostText(cost)}", pot == "hp" ? new Color("ff8f86") : new Color("8fb8ff"), "Brew", () =>
                    {
                        if (!CanPay(cost)) { Note("Not enough materials", new Color(1, .5f, .5f)); return; }
                        Pay(cost); if (pot == "hp") d.HpPot += n; else d.MpPot += n; Sfx.Play("pickup"); Game.Notify(); OpenCraft(tab);
                    });
                }
                break;
            default:
                Row(list, $"Forge gear for the slot you choose at your level (weapons get a random weapon type).", Colors.White, 15);
                SlotPicker(list);
                foreach (var e in c.GetProperty("gear").EnumerateArray())
                {
                    int rar = (int)JsonUtil.F(e, "rar", 1), lb = (int)JsonUtil.F(e, "levelBonus", 0); var cost = Cost(e);
                    AddRow(list, $"{Items.RarityName(rar)} {_craftSlot}, level {d.Level + lb}:  {CostText(cost)}", Items.RarityColor(rar), "Craft", () =>
                    {
                        if (!Room()) return; if (!CanPay(cost)) { Note("Not enough materials", new Color(1, .5f, .5f)); return; }
                        Pay(cost); Give(Items.Roll(d.Level + lb, 0, _craftSlot, rar)); OpenCraft(tab);
                    });
                }
                break;
        }
    }
}
