using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TwinRealms;

public partial class Ui
{
    // ---------------------------------------------------------------- shop and smith
    static int _stockLvl = -1; static List<Item> _stock = new();
    static int BuyPrice(Item it) => Cost(Mathf.RoundToInt(it.Value * Tune.F("shop.buyMul", 3) + Tune.F("shop.buyAdd", 12)));   // data/tuning.json "game.shop"
    // F-28: the NPC whose shop / forge is open ("" = none: plain prices). Standing with it and its town moves every price (NpcMemory).
    static string _shopNpc = "", _shopName = "";
    public static void DevShopNpc(string id) { _shopNpc = id; _shopName = NpcDb.Get(id)?.Name ?? ""; }
    /// <summary>What the open shop asks for something of this base price / pays for something of this value.</summary>
    static int Cost(int g) => Math.Max(1, Mathf.RoundToInt(g * NpcMemory.BuyMul(_shopNpc)));
    static int Pays(int g) => g <= 0 ? 0 : Math.Max(1, Mathf.RoundToInt(g * NpcMemory.SellMul(_shopNpc)));
    static void Traded() => NpcMemory.Trade(_shopNpc);
    public void OpenShop(string tab)
    {
        var d = Game.D; if (_stockLvl != d.Level) { _stockLvl = d.Level; _stock.Clear(); for (int i = 0; i < 6; i++) _stock.Add(Items.Roll(d.Level + 1, .2f)); }
        var v = Frame("shop", NpcDb.Get(_shopNpc) != null ? _shopName + ": wares" : "Brynn's Wares", new Vector2(720, 560), () => OpenShop(tab));
        if (NpcDb.Get(_shopNpc) is { } keeper && Mathf.Abs(NpcMemory.BuyMul(_shopNpc) - 1f) > .004f)
        { float bm = NpcMemory.BuyMul(_shopNpc); Row(v, $"{NpcMemory.Summary(keeper)}   (you pay {(bm < 1 ? "less" : "more")}, your goods fetch {(bm < 1 ? "more" : "less")})", bm < 1 ? new Color(.6f, .9f, .6f) : new Color(1f, .6f, .5f), 13); }
        var top = new HBoxContainer(); v.AddChild(top); Tabs(top, tab, ("buy", "Buy", () => OpenShop("buy")), ("sell", "Sell", () => OpenShop("sell"))); top.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }); Lbl($"{d.Gold} gold", 20, Gold, top);
        var sc = Scroll(v, 420); var list = Inner(sc); list.AddThemeConstantOverride("separation", 6); sc.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        if (tab == "buy")
        {
            int hpc = Cost(Tune.I("shop.healthPotion", 30)), mpc = Cost(Tune.I("shop.manaPotion", 35)), hc = Cost(Tune.I("shop.horse", 350));
            AddRow(list, "Health potion  (have " + d.HpPot + ")", Colors.White, $"Buy {hpc}g", () => { if (d.Gold < hpc) { Sfx.UiError(); Note("Not enough gold", new Color(1, .5f, .5f)); return; } d.Gold -= hpc; d.HpPot++; Traded(); Sfx.Play("coin"); OpenShop(tab); }, d.Gold >= hpc);
            AddRow(list, "Mana potion  (have " + d.MpPot + ")", Colors.White, $"Buy {mpc}g", () => { if (d.Gold < mpc) { Sfx.UiError(); Note("Not enough gold", new Color(1, .5f, .5f)); return; } d.Gold -= mpc; d.MpPot++; Traded(); Sfx.Play("coin"); OpenShop(tab); }, d.Gold >= mpc);
            if (!d.Horse) AddRow(list, "Horse (ride with T)", Colors.White, $"Buy {hc}g", () => { if (d.Gold < hc) { Sfx.UiError(); Note("Not enough gold", new Color(1, .5f, .5f)); return; } d.Gold -= hc; d.Horse = true; Traded(); Sfx.Play("coin"); Note("You bought a horse. Press T to ride.", Gold); OpenShop(tab); }, d.Gold >= hc);
            int rodc = Cost((int)JsonUtil.F(FishDb.RodData(), "price", 120));   // the fishing rod (Fishing.cs, data/fishing.json "rod")
            if (d.Rod <= 0) AddRow(list, "Fishing rod (take out with 7, attack casts)", Colors.White, $"Buy {rodc}g", () => { if (d.Gold < rodc) { Sfx.UiError(); Note("Not enough gold", new Color(1, .5f, .5f)); return; } d.Gold -= rodc; d.Rod = 1; Traded(); Sfx.Play("coin"); Note("You bought a fishing rod. Press 7 to take it out.", Gold); OpenShop(tab); }, d.Gold >= rodc);
            LifeShopRows(list, tab);   // F-24/F-25: lockpicks and cooking recipe books (UiLife.cs)
            foreach (var cb in PetDb.Cubes)
            {   // Soul Cubes (data/pets.json "cubes"): bind a weakened monster as a pet
                var cd = cb; int cc = Cost(cd.Cost); AddRow(list, $"{cd.Name}  (have {Cubes.Count(cd.Id)})  x{cd.Mul:0.#} catch", cd.Col.Lightened(.2f), $"Buy {cc}g",
                    () => { if (d.Gold < cc) { Sfx.UiError(); Note("Not enough gold", new Color(1, .5f, .5f)); return; } d.Gold -= cc; Cubes.Give(cd.Id, 1, false); Traded(); Sfx.Play("coin"); OpenShop(tab); }, d.Gold >= cc);
            }
            for (int i = 0; i < _stock.Count; i++) { int k = i; var it = _stock[i]; ItemRow(list, it, BuyPrice(it) + "g", () => { int c = BuyPrice(it); if (d.Gold < c) { Sfx.UiError(); Note("Not enough gold", new Color(1, .5f, .5f)); return; } if (d.Inv.Count >= 28) { Note("Pack is full", new Color(1, .5f, .5f)); return; } d.Gold -= c; d.Inv.Add(it); _stock.RemoveAt(k); Traded(); Sfx.Play("coin"); OpenShop(tab); }, d.Gold >= BuyPrice(it)); }
        }
        else
        {
            if (d.Inv.Count == 0 && d.Mats.Count == 0) Empty(list, "Your pack is empty. Nothing here to sell.");
            // gathered materials sell by the stack
            // "sell all" keeps crafting scraps (sell those one stack at a time below)
            int matGold = 0; foreach (var kv in d.Mats) if (!GatherDb.Mat(kv.Key).Scrap) matGold += Pays(kv.Value * GatherDb.Mat(kv.Key).Value);
            if (matGold > 0) list.AddChild(Btn($"Sell all gathered materials ({matGold}g, keeps scraps)", () => { d.Gold += matGold; foreach (var k in d.Mats.Keys.ToList()) if (!GatherDb.Mat(k).Scrap) d.Mats.Remove(k); Traded(); Sfx.Play("coin"); Game.Notify(); OpenShop(tab); }, true));
            foreach (var kv in new List<KeyValuePair<string, int>>(d.Mats))
            {
                if (kv.Value <= 0) continue; var m = GatherDb.Mat(kv.Key); string id = kv.Key; int n = kv.Value;
                AddRow(list, $"{m.Name}  x{n}", m.Col.Lightened(0.25f), $"Sell {Pays(n * m.Value)}g", () => { d.Gold += Pays(n * m.Value); d.Mats.Remove(id); Traded(); Sfx.Play("coin"); Game.Notify(); OpenShop(tab); });
            }
            list.AddChild(Btn("Sell all Common", () => { int g = 0; d.Inv.RemoveAll(it => { if (it.Rar == 0) { g += Pays(it.Value); GemDb.ReturnAll(d, it); return true; } return false; }); d.Gold += g; if (g > 0) { Traded(); Sfx.Play("coin"); } OpenShop(tab); }, true));
            for (int i = 0; i < d.Inv.Count; i++) { int k = i; var it = d.Inv[i]; ItemRow(list, it, "Sell " + Pays(it.Value) + "g", () => { d.Gold += Pays(it.Value); GemDb.ReturnAll(d, it); d.Inv.RemoveAt(k); Traded(); Sfx.Play("coin"); OpenShop(tab); }); }
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
        { var dd = Game.D; int rk = Math.Clamp(dd.DuelRank, 0, DuelDb.RankCount - 1); Row(v, $"Soul Duels ladder: rank {rk + 1}/{DuelDb.RankCount}, next a {DuelDb.RankTitle(rk)}   (won {dd.SoulWins}, lost {dd.SoulLosses})", GoldC, 14); }
        var sc = Scroll(v, 420); sc.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled; var tl = Inner(sc); tl.AddThemeConstantOverride("separation", 4);
        var keys = new List<string>(); foreach (var k in TreeDb.Known) if (Mathf.Abs(Game.TS[k]) > .001f) keys.Add(k); keys.Sort();
        if (keys.Count == 0) Empty(tl, "No bonuses yet. Spend points in the skill tree.");
        foreach (var k in keys) { var h = new HBoxContainer(); CardBox(tl).AddChild(h); Row(h, TreeDb.StatText(k, Game.TS[k]), Parch, 15).SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; }
        v.AddChild(Btn("Back to tree", OpenTree));
    }
}
