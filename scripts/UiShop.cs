using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TwinRealms;

public partial class Ui
{
    // ---------------------------------------------------------------- shop and smith
    static int _stockLvl = -1; static List<Item> _stock = new();
    static int BuyPrice(Item it) => Mathf.RoundToInt(it.Value * Tune.F("shop.buyMul", 3) + Tune.F("shop.buyAdd", 12));   // data/tuning.json "game.shop"
    public void OpenShop(string tab)
    {
        var d = Game.D; if (_stockLvl != d.Level) { _stockLvl = d.Level; _stock.Clear(); for (int i = 0; i < 6; i++) _stock.Add(Items.Roll(d.Level + 1, .2f)); }
        var v = Frame("shop", "Brynn's Wares", new Vector2(720, 560), () => OpenShop(tab));
        var top = new HBoxContainer(); v.AddChild(top); Tabs(top, tab, ("buy", "Buy", () => OpenShop("buy")), ("sell", "Sell", () => OpenShop("sell"))); top.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }); Lbl($"{d.Gold} gold", 20, Gold, top);
        var sc = Scroll(v, 420); var list = Inner(sc); list.AddThemeConstantOverride("separation", 6); sc.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        if (tab == "buy")
        {
            int hpc = Tune.I("shop.healthPotion", 30), mpc = Tune.I("shop.manaPotion", 35), hc = Tune.I("shop.horse", 350);
            AddRow(list, "Health potion  (have " + d.HpPot + ")", Colors.White, $"Buy {hpc}g", () => { if (d.Gold < hpc) { Sfx.UiError(); Note("Not enough gold", new Color(1, .5f, .5f)); return; } d.Gold -= hpc; d.HpPot++; Sfx.Play("coin"); OpenShop(tab); }, d.Gold >= hpc);
            AddRow(list, "Mana potion  (have " + d.MpPot + ")", Colors.White, $"Buy {mpc}g", () => { if (d.Gold < mpc) { Sfx.UiError(); Note("Not enough gold", new Color(1, .5f, .5f)); return; } d.Gold -= mpc; d.MpPot++; Sfx.Play("coin"); OpenShop(tab); }, d.Gold >= mpc);
            if (!d.Horse) AddRow(list, "Horse (ride with T)", Colors.White, $"Buy {hc}g", () => { if (d.Gold < hc) { Sfx.UiError(); Note("Not enough gold", new Color(1, .5f, .5f)); return; } d.Gold -= hc; d.Horse = true; Sfx.Play("coin"); Note("You bought a horse. Press T to ride.", Gold); OpenShop(tab); }, d.Gold >= hc);
            for (int i = 0; i < _stock.Count; i++) { int k = i; var it = _stock[i]; ItemRow(list, it, BuyPrice(it) + "g", () => { int c = BuyPrice(it); if (d.Gold < c) { Sfx.UiError(); Note("Not enough gold", new Color(1, .5f, .5f)); return; } if (d.Inv.Count >= 28) { Note("Pack is full", new Color(1, .5f, .5f)); return; } d.Gold -= c; d.Inv.Add(it); _stock.RemoveAt(k); Sfx.Play("coin"); OpenShop(tab); }, d.Gold >= BuyPrice(it)); }
        }
        else
        {
            if (d.Inv.Count == 0 && d.Mats.Count == 0) Empty(list, "Your pack is empty. Nothing here to sell.");
            // gathered materials sell by the stack
            // "sell all" keeps crafting scraps (sell those one stack at a time below)
            int matGold = 0; foreach (var kv in d.Mats) if (!GatherDb.Mat(kv.Key).Scrap) matGold += kv.Value * GatherDb.Mat(kv.Key).Value;
            if (matGold > 0) list.AddChild(Btn($"Sell all gathered materials ({matGold}g, keeps scraps)", () => { d.Gold += matGold; foreach (var k in d.Mats.Keys.ToList()) if (!GatherDb.Mat(k).Scrap) d.Mats.Remove(k); Sfx.Play("coin"); Game.Notify(); OpenShop(tab); }, true));
            foreach (var kv in new List<KeyValuePair<string, int>>(d.Mats))
            {
                if (kv.Value <= 0) continue; var m = GatherDb.Mat(kv.Key); string id = kv.Key; int n = kv.Value;
                AddRow(list, $"{m.Name}  x{n}", m.Col.Lightened(0.25f), $"Sell {n * m.Value}g", () => { d.Gold += n * m.Value; d.Mats.Remove(id); Sfx.Play("coin"); Game.Notify(); OpenShop(tab); });
            }
            list.AddChild(Btn("Sell all Common", () => { int g = 0; d.Inv.RemoveAll(it => { if (it.Rar == 0) { g += it.Value; return true; } return false; }); d.Gold += g; if (g > 0) Sfx.Play("coin"); OpenShop(tab); }, true));
            for (int i = 0; i < d.Inv.Count; i++) { int k = i; var it = d.Inv[i]; ItemRow(list, it, "Sell " + it.Value + "g", () => { d.Gold += it.Value; d.Inv.RemoveAt(k); Sfx.Play("coin"); OpenShop(tab); }); }
        }
    }

    // ---------------------------------------------------------------- valor quartermaster (data/valor.json)
    static readonly List<Item> _lastPulls = new();
    public void OpenValor(string tab)
    {
        var d = Game.D; var v = Frame("valor", "Sera, Valor Quartermaster", new Vector2(760, 580), () => OpenValor(tab));
        var top = new HBoxContainer(); v.AddChild(top);
        Tabs(top, tab, ("gacha", "Pulls", () => OpenValor("gacha")), ("gear", "Gear", () => OpenValor("gear")), ("gold", "Gold", () => OpenValor("gold")));
        top.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }); Lbl($"{d.Valor} valor", 20, new Color(1f, .78f, .35f), top); Lbl($"   {d.Gold} gold", 18, Gold, top);
        var sc = Scroll(v, 430); var list = Inner(sc); list.AddThemeConstantOverride("separation", 6); sc.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        bool Pay(int c) { if (d.Valor < c) { Sfx.UiError(); Note("Not enough valor", new Color(1, .5f, .5f)); return false; } d.Valor -= c; return true; }
        bool Room(int n) { if (d.Inv.Count + n > 28) { Note("Pack is full", new Color(1, .5f, .5f)); return false; } return true; }
        if (tab == "gacha")
        {
            Row(list, $"A sealed cache of arena spoils: one random item at your level. Every {Valor.Pity} pulls without an epic, the next is at least epic ({d.GachaPity}/{Valor.Pity}).", Parch, 16); Header("Pulls", list);
            AddRow(list, "One pull", Colors.White, $"{Valor.PullCost} valor", () => { if (!Room(1) || !Pay(Valor.PullCost)) return; _lastPulls.Clear(); var it = Valor.Pull(); d.Inv.Add(it); _lastPulls.Add(it); Sfx.Play("level"); Game.Notify(); OpenValor(tab); }, d.Valor >= Valor.PullCost);
            AddRow(list, "Ten pulls", Colors.White, $"{Valor.PullTenCost} valor", () => { if (!Room(10) || !Pay(Valor.PullTenCost)) return; _lastPulls.Clear(); for (int i = 0; i < 10; i++) { var it = Valor.Pull(); d.Inv.Add(it); _lastPulls.Add(it); } Sfx.Play("level"); Game.Notify(); OpenValor(tab); }, d.Valor >= Valor.PullTenCost);
            if (_lastPulls.Count > 0) { Header("You received", list); foreach (var it in _lastPulls) ItemRow(list, it, "", null); }
        }
        else if (tab == "gear")
        {
            Row(list, "Pick a slot and a rarity: a new item at your level.", Parch, 16); Header("Gear", list);
            foreach (var slot in Valor.GearSlots)
                for (int rar = 2; rar <= 4; rar++)
                {
                    int c = Valor.GearCost(rar); if (c <= 0) continue; string s = slot; int r = rar;
                    AddRow(list, $"{Items.RarityName(rar)} {slot}", Items.RarityColor(rar), $"{c} valor", () => { if (!Room(1) || !Pay(c)) return; var it = Valor.Gear(s, r); d.Inv.Add(it); Note("Received " + it.Name, it.Color); Sfx.Play("coin"); Game.Notify(); OpenValor(tab); }, d.Valor >= c);
                }
        }
        else
        {
            Row(list, "Trade valor for gold.", Parch, 16); Header("Gold", list); int nTr = 0;
            foreach (var (val, gold) in Valor.GoldTrades())
            { nTr++; AddRow(list, $"{gold} gold", Gold, $"{val} valor", () => { if (!Pay(val)) return; d.Gold += gold; Sfx.Play("coin"); Game.Notify(); OpenValor(tab); }, d.Valor >= val); }
            if (nTr == 0) Empty(list, "No trades on offer.");
        }
        v.AddChild(Btn("Leave", Close));
    }

    /// <summary>Panel listing every bonus you currently have, with where the numbers come from.</summary>
    public void OpenTotals()
    {
        var v = Frame("totals", "All bonuses", new Vector2(620, 600), OpenTotals);
        Header($"{Races.Get(Game.D.Race).Name} {Classes.Get(Game.D.Class).Name}", v);
        Row(v, $"Skill tree ({Game.D.Tree.Count} nodes) + race + class + active buffs", Dim, 14);
        var sc = Scroll(v, 440); sc.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled; var tl = Inner(sc); tl.AddThemeConstantOverride("separation", 4);
        var keys = new List<string>(); foreach (var k in TreeDb.Known) if (Mathf.Abs(Game.TS[k]) > .001f) keys.Add(k); keys.Sort();
        if (keys.Count == 0) Empty(tl, "No bonuses yet. Spend points in the skill tree.");
        foreach (var k in keys) { var h = new HBoxContainer(); CardBox(tl).AddChild(h); Row(h, TreeDb.StatText(k, Game.TS[k]), Parch, 15).SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; }
        v.AddChild(Btn("Back to tree", OpenTree));
    }
}
