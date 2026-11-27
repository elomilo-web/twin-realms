using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TwinRealms;

public partial class Ui
{
    static string _tradeGold = "";
    /// <summary>Rebuilds the trade window if it is the one open (the countdown moved).</summary>
    public void RefreshTrade() { if (_panelName == "trade" && _panel != null && Trade.Open) OpenTrade(); }

    /// <summary>
    /// The two-sided trade window (focus A-30): left your pack, stacks, pets and gold (click to offer or take back), right the partner's
    /// offer with the full item tooltips. Any change clears both confirmations; both confirm, a short countdown, then the host swaps.
    /// Closing the window cancels the trade.
    /// </summary>
    public void OpenTrade()
    {
        if (!Trade.Open) return; var d = Game.D; var mine = Trade.Mine; var theirs = Trade.Theirs; bool busy = Trade.Busy;
        var v = Frame("trade", $"Trade with {Trade.PartnerName}", new Vector2(1180, 680), OpenTrade);
        Row(v, $"Click things in your pack to offer them (up to {Trade.MaxItems} items, {Trade.MaxStacks} kinds of materials, gems or potions, {Trade.MaxPets} pets) and set gold. What you wear cannot be offered: take it off first. Any change clears both confirmations. Closing this window cancels the trade.", Dim, 13);
        // ---- status and buttons on top, so they never scroll away
        var bot = new HBoxContainer(); bot.AddThemeConstantOverride("separation", 12); v.AddChild(bot);
        bool any = !mine.Empty() || !theirs.Empty(), room = Trade.Room();
        var cb = Btn(busy ? "Trading..." : Trade.MyOk ? "Withdraw confirmation" : "Confirm trade", () => { Trade.Confirm(); if (Trade.Open) OpenTrade(); });
        cb.Disabled = busy || !any || (!Trade.MyOk && !room); cb.CustomMinimumSize = new Vector2(240, 40); bot.AddChild(cb);
        var xb = Btn("Cancel trade", () => { Trade.Cancel(); Close(); }); xb.CustomMinimumSize = new Vector2(170, 40); xb.Disabled = busy; bot.AddChild(xb);
        string status = busy ? "Swapping..." : Trade.MyOk && Trade.TheirOk ? (Trade.Count > 0 ? $"Both confirmed: trading in {Mathf.CeilToInt(Trade.Count)} ..." : "Both confirmed: trading ...")
            : Trade.MyOk ? $"Waiting for {Trade.PartnerName}" : Trade.TheirOk ? $"{Trade.PartnerName} has confirmed" : !room ? "Your pack has no room for their items" : "";
        Lbl(status, 16, !room && !busy ? ErrRed : Gold, bot);

        var row = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; row.AddThemeConstantOverride("separation", 18); v.AddChild(row);
        // ================================================================ left: mine
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(560, 0) }; left.AddThemeConstantOverride("separation", 5); row.AddChild(left);
        Header("Your offer" + (Trade.MyOk ? "   CONFIRMED" : ""), left);
        Lbl($"Pack {d.Inv.Count}/{Auction.PackSize}   offered {mine.Items.Count}/{Trade.MaxItems}", 13, Dim, left);
        var grid = new GridContainer { Columns = 7 }; grid.AddThemeConstantOverride("h_separation", 4); grid.AddThemeConstantOverride("v_separation", 4); left.AddChild(grid);
        var mask = Trade.OfferedMask(d.Inv);
        for (int i = 0; i < d.Inv.Count; i++)
        {
            int k = i; var it = d.Inv[i]; bool on = mask[i];
            var cell = new ItemSlot { Item = it, Selected = on, CustomMinimumSize = new Vector2(54, 54) }; if (!on) cell.Modulate = Trade.CanTrade(it) ? new Color(1, 1, 1, .8f) : new Color(.5f, .5f, .5f, .6f);
            grid.AddChild(cell);
            cell.Hover += h => ShowTip(h ? it : null, cell, d.Eq.GetValueOrDefault(it.Slot));
            if (!busy) cell.Clicked += _ => { HideTip(); Trade.ToggleItem(k); if (Trade.Open) OpenTrade(); };
        }
        if (d.Inv.Count == 0) Lbl("Your pack is empty.", 14, Dim, left);
        if (mine.Items.Count > 0) Row(left, "Offered: " + string.Join(", ", mine.Items.Select(x => x.Name)), Gold, 13);
        // gold
        var gh = new HBoxContainer(); gh.AddThemeConstantOverride("separation", 5); left.AddChild(gh);
        Lbl($"Gold {mine.Gold}", 15, GoldC, gh).CustomMinimumSize = new Vector2(110, 0);
        foreach (var (t, n) in new[] { ("-100", -100), ("-10", -10), ("+10", 10), ("+100", 100), ("+1000", 1000) }) { int add = n; var b = Btn(t, () => { Trade.SetGold(Trade.Mine.Gold + add); if (Trade.Open) OpenTrade(); }, true); b.Disabled = busy; gh.AddChild(b); }
        var ge = new LineEdit { Text = _tradeGold, PlaceholderText = "amount", MaxLength = 9, CustomMinimumSize = new Vector2(90, 30), Editable = !busy }; gh.AddChild(ge);
        ge.TextChanged += s => _tradeGold = s;
        ge.TextSubmitted += s => { if (int.TryParse(s.Trim(), out int g)) { _tradeGold = ""; Trade.SetGold(g); if (Trade.Open) OpenTrade(); } };
        Lbl($"of {d.Gold}", 13, Dim, gh);
        // stacks
        var owned = TradeStacks.Owned(d);
        if (owned.Count > 0) Header($"Materials, blocks, gems, potions ({mine.Stacks.Count}/{Trade.MaxStacks} kinds offered)", left);
        foreach (var (key, have) in owned)
        {
            string sk = key; int off = mine.Stacks.GetValueOrDefault(key);
            var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 4); left.AddChild(h);
            var l = Lbl($"{TradeStacks.Name(key)}  x{have}", 14, off > 0 ? Gold : TradeStacks.Col(key).Lerp(Parch, .5f), h); l.CustomMinimumSize = new Vector2(250, 0); l.ClipText = true;
            string tip = TradeStacks.Tip(key); if (tip != "") { l.MouseFilter = Control.MouseFilterEnum.Stop; l.TooltipText = tip; }
            Lbl(off > 0 ? $"offering {off}" : "", 14, Gold, h).CustomMinimumSize = new Vector2(96, 0);
            foreach (var (t, n) in new[] { ("0", -have), ("-10", -10), ("-1", -1), ("+1", 1), ("+10", 10), ("All", have) })
            {
                int add = n; var b = Btn(t, () => { Trade.SetStack(sk, Trade.Mine.Stacks.GetValueOrDefault(sk) + add); if (Trade.Open) OpenTrade(); }, true);
                b.Disabled = busy || (n < 0 ? off <= 0 : off >= have); h.AddChild(b);
            }
        }
        // pets
        if (d.Pets.Count > 0 && Trade.MaxPets > 0) Header($"Pets ({mine.Pets.Count}/{Trade.MaxPets})", left);
        if (Trade.MaxPets > 0) foreach (var p in d.Pets)
        {
            var pp = p; bool on = mine.Pets.Exists(x => x.Id == p.Id), comp = p.Id == d.ActivePet; var sp = p.Sp();
            AddRow(left, $"{p.Name()}  Lv{p.Level}  {PetDb.RarityName(sp.Rarity)} {sp.Name}" + (comp ? "  (companion: not tradable while out)" : ""), on ? Gold : PetDb.RarityColor(sp.Rarity).Lerp(Parch, .4f),
                on ? "Take back" : "Offer", () => { Trade.TogglePet(pp.Id); if (Trade.Open) OpenTrade(); }, !busy && !comp);
        }

        // ================================================================ right: theirs
        var right = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; right.AddThemeConstantOverride("separation", 5); row.AddChild(right);
        Header($"{Trade.PartnerName}'s offer" + (Trade.TheirOk ? "   CONFIRMED" : ""), right);
        if (theirs.Empty()) Empty(right, "Nothing offered yet.");
        if (theirs.Items.Count > 0)
        {
            Lbl("Hover an item for its full details.", 13, Dim, right);
            var tg = new GridContainer { Columns = 2 }; tg.AddThemeConstantOverride("h_separation", 4); tg.AddThemeConstantOverride("v_separation", 4); right.AddChild(tg);
            foreach (var it0 in theirs.Items)
            {
                var it = it0; var cell = new ItemSlot { Item = it, Wide = true, CustomMinimumSize = new Vector2(262, 56) }; tg.AddChild(cell);
                cell.Hover += h => ShowTip(h ? it : null, cell, d.Eq.GetValueOrDefault(it.Slot));
            }
        }
        foreach (var kv in theirs.Stacks)
        {
            var l = Lbl($"{kv.Value}x  {TradeStacks.Name(kv.Key)}", 15, TradeStacks.Col(kv.Key).Lerp(Parch, .4f), right);
            string tip = TradeStacks.Tip(kv.Key); if (tip != "") { l.MouseFilter = Control.MouseFilterEnum.Stop; l.TooltipText = tip; }
        }
        foreach (var p in theirs.Pets)
        {
            var sp = p.Sp(); var st = p.Stats(); var box = CardBox(right); var c = new VBoxContainer(); box.AddChild(c);
            Lbl($"{p.Name()}  Lv{p.Level}  {PetDb.RarityName(sp.Rarity)} {sp.Name}", 16, PetDb.RarityColor(sp.Rarity), c);
            Lbl($"{(sp.Elem == "physical" ? "" : sp.Elem + " ")}{sp.Arch}   nature {PetDb.Nature(p.Nature).Name}   trainer {p.Ot}", 13, Dim, c);
            Lbl($"HP {st.Hp}  ATK {st.Atk}  DEF {st.Def}  MAG {st.Mag}  SPD {st.Spd}", 14, Parch, c);
            Lbl("Skills: " + string.Join(", ", p.Skills.Select(s => PetDb.Skill(s)?.Name ?? s)), 13, Dim, c).AutowrapMode = TextServer.AutowrapMode.WordSmart;
            var gear = p.Gear.Values.Where(g => g != null).Select(g => $"{g!.Name} ({PetDb.GearText(g)})").ToList();
            if (gear.Count > 0) Lbl("Gear: " + string.Join(", ", gear), 13, Dim, c).AutowrapMode = TextServer.AutowrapMode.WordSmart;
        }
        if (theirs.Gold > 0) Lbl($"+ {theirs.Gold} gold", 17, GoldC, right);
    }
}
