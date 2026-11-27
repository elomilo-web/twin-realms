using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TwinRealms;

/// <summary>
/// The auction house window (focus F-37), opened from an auctioneer (dialog role "auctioneer"). Tabs: Browse (search by name, filters for type,
/// rarity, level and price, sort, pages; Bid / Buy), Sell (pick a pack item or a pet, start price, buyout, duration, the fee shown), My listings
/// (cancel while nobody has bid; the bids you lead), Collect (gold and things waiting; items need room in the 28-slot pack).
/// All of it goes through AhClient: the hero's own book offline, the server's when logged in. Every control is a button (cycle filters,
/// percent steps), so the controller reaches everything; LB / RB switch tabs.
/// </summary>
public partial class Ui
{
    static string _ahTab = "browse", _ahSellSig = "", _ahSellPet = "";
    static int _ahStart, _ahBuyout, _ahHours;

    static readonly (string id, string label)[] AhSlots = { ("", "Any"), ("weapon", "Weapons"), ("armor", "Armor"), ("shield", "Shields"), ("head", "Head"), ("hands", "Hands"), ("feet", "Feet"), ("cape", "Capes"), ("trinket", "Trinkets"), ("petgear", "Pet gear"), ("pet", "Pets") };
    static readonly (int min, int max, string label)[] AhLevels = { (0, 0, "Any"), (1, 10, "1-10"), (11, 20, "11-20"), (21, 30, "21-30"), (31, 45, "31-45"), (46, 0, "46+") };
    static readonly (int min, int max, string label)[] AhPrices = { (0, 0, "Any"), (0, 100, "up to 100"), (100, 500, "100-500"), (500, 2000, "500-2000"), (2000, 0, "2000+") };
    static readonly (string id, string label)[] AhSorts = { ("price", "Lowest price"), ("priceDesc", "Highest price"), ("time", "Ending soon"), ("timeDesc", "Most time left") };

    /// <summary>Opens the auction house on a tab (browse | sell | mine | collect) and asks the house for a fresh page.</summary>
    public void OpenAuction(string tab = "browse")
    {
        if (tab is not ("browse" or "sell" or "mine" or "collect")) tab = "browse";
        var q = AhClient.Q; string scope = tab == "mine" ? "mine" : "";
        if (tab != _ahTab || q.Scope != scope) q.Page = 0;
        _ahTab = tab; q.Scope = scope;
        AhClient.Refresh(); BuildAuction();
    }
    /// <summary>Dev captures: "ui:auction:&lt;tab&gt;[/command;command]" runs the dev commands, then opens the tab.</summary>
    public string DevAuction(string arg)
    {
        var p = (arg ?? "").Split('/', 2); var sb = new System.Text.StringBuilder();
        if (p.Length > 1) foreach (var c in p[1].Split(';')) if (c.Trim() != "") sb.AppendLine(Commands.Run(c.Trim()));
        OpenAuction(p[0] == "" ? "browse" : p[0]); return sb.ToString();
    }
    void AhGo() { AhClient.Refresh(); BuildAuction(); }
    static string AhLeft(double sec)
    {
        double h = sec / AhClient.HourSec;
        if (!AhClient.Online) return h < 1 ? "under 1 h left" : $"{Math.Floor(h):0} h left";
        int m = (int)(sec / 60); return m >= 60 ? $"{m / 60} h {m % 60} m left" : $"{Math.Max(1, m)} m left";
    }
    Button AhCycle(Node parent, string label, string[] opts, int cur, Action<int> set)
    {
        cur = Math.Clamp(cur, 0, opts.Length - 1);
        var b = Btn($"{label}: {opts[cur]}", () => { set((cur + 1) % opts.Length); AhClient.Q.Page = 0; AhGo(); }, true);
        if (cur != 0) b.AddThemeColorOverride("font_color", Gold);
        b.TooltipText = "Press to step through: " + string.Join(", ", opts); parent.AddChild(b); return b;
    }

    void BuildAuction()
    {
        var d = Game.D; var pg = AhClient.Page; var q = AhClient.Q;
        var v = Frame("auction", "Auction House", new Vector2(1180, 640), BuildAuction);
        var top = new HBoxContainer(); top.AddThemeConstantOverride("separation", 4); v.AddChild(top);
        Tabs(top, _ahTab, ("browse", "Browse", () => OpenAuction("browse")), ("sell", "Sell", () => OpenAuction("sell")),
            ("mine", $"My listings ({pg.Mine}/{Auction.MaxListings})" + (pg.Bids > 0 ? $" + {pg.Bids} bids" : ""), () => OpenAuction("mine")),
            ("collect", pg.Box.Count > 0 ? $"Collect ({pg.Box.Count})" : "Collect", () => OpenAuction("collect")));
        top.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        if (AhClient.Busy) Lbl("asking the house ...   ", 14, Dim, top);
        Lbl(AhClient.Online ? "shared house (online)   " : "", 13, Dim, top);
        Lbl($"{d.Gold} gold", 20, Gold, top);
        switch (_ahTab)
        {
            case "sell": AhSell(v); break;
            case "mine": AhMine(v); break;
            case "collect": AhCollect(v); break;
            default: AhBrowse(v); break;
        }
    }

    // ---------------------------------------------------------------- browse
    void AhBrowse(VBoxContainer v)
    {
        var q = AhClient.Q; var pg = AhClient.Page;
        var fr = new HBoxContainer(); fr.AddThemeConstantOverride("separation", 6); v.AddChild(fr);
        var search = new LineEdit { Text = q.Text, PlaceholderText = "Search by name: sword, wolf, runed ...", MaxLength = 40, CustomMinimumSize = new Vector2(250, 32), ClearButtonEnabled = true }; fr.AddChild(search);
        search.TextSubmitted += s => { q.Text = s.Trim(); q.Page = 0; search.ReleaseFocus(); AhGo(); };
        fr.AddChild(Btn("Search", () => { q.Text = search.Text.Trim(); q.Page = 0; AhGo(); }, true));
        AhCycle(fr, "Type", AhSlots.Select(s => s.label).ToArray(), Array.FindIndex(AhSlots, s => s.id == q.Slot), i => q.Slot = AhSlots[i].id);
        AhCycle(fr, "Rarity", new[] { "Any", "Fine+", "Rare+", "Epic+", "Legendary" }, Math.Max(0, q.Rar), i => q.Rar = i == 0 ? -1 : i);
        AhCycle(fr, "Level", AhLevels.Select(s => s.label).ToArray(), Array.FindIndex(AhLevels, s => s.min == q.LvMin && s.max == q.LvMax), i => { q.LvMin = AhLevels[i].min; q.LvMax = AhLevels[i].max; });
        AhCycle(fr, "Price", AhPrices.Select(s => s.label).ToArray(), Array.FindIndex(AhPrices, s => s.min == q.PrMin && s.max == q.PrMax), i => { q.PrMin = AhPrices[i].min; q.PrMax = AhPrices[i].max; });
        AhCycle(fr, "Sort", AhSorts.Select(s => s.label).ToArray(), Array.FindIndex(AhSorts, s => s.id == q.Sort), i => q.Sort = AhSorts[i].id);
        bool filtered = q.Text != "" || q.Slot != "" || q.Rar > 0 || q.LvMin + q.LvMax + q.PrMin + q.PrMax > 0;
        if (filtered) fr.AddChild(Btn("Clear", () => { q.Text = ""; q.Slot = ""; q.Rar = -1; q.LvMin = q.LvMax = q.PrMin = q.PrMax = 0; q.Page = 0; AhGo(); }, true));

        var sc = Scroll(v, 430); sc.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled; var list = Inner(sc); list.AddThemeConstantOverride("separation", 5);
        if (pg.L.Count == 0) Empty(list, AhClient.Busy ? "Asking the auction house ..." : filtered ? "Nothing for sale matches. Clear a filter or search for something else." : "Nothing is for sale right now.");
        foreach (var l in pg.L) AhRow(list, l);

        var nav = new HBoxContainer(); nav.AddThemeConstantOverride("separation", 10); v.AddChild(nav);
        var prev = Btn("< Previous", () => { q.Page = Math.Max(0, pg.Page - 1); AhGo(); }, true); prev.Disabled = pg.Page <= 0; nav.AddChild(prev);
        Lbl($"Page {pg.Page + 1} of {pg.Pages}   ({pg.Total} for sale)", 14, Parch, nav);
        var next = Btn("Next >", () => { q.Page = pg.Page + 1; AhGo(); }, true); next.Disabled = pg.Page >= pg.Pages - 1; nav.AddChild(next);
        nav.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        Lbl($"A bid takes its gold at once; outbid gold and anything won wait in Collect. Sellers pay {Auction.TaxPct:0.#}% tax on a sale.", 12, Dim, nav);
    }

    /// <summary>The thing for sale: icon (hover for the full item card) and two or three lines of text. Returns the text column.</summary>
    VBoxContainer AhThing(HBoxContainer h, Item? it, PetState? p)
    {
        var d = Game.D;
        if (it != null)
        {
            var cell = new ItemSlot { Item = it, CustomMinimumSize = new Vector2(46, 46), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter }; h.AddChild(cell);
            cell.Hover += on => ShowTip(on ? it : null, cell, d.Eq.GetValueOrDefault(it.Slot));
        }
        else
        {
            var col = p != null ? PetDb.RarityColor(p.Sp().Rarity) : Dim;
            var g = Lbl("PET", 13, col, h); g.CustomMinimumSize = new Vector2(46, 46); g.HorizontalAlignment = HorizontalAlignment.Center; g.VerticalAlignment = VerticalAlignment.Center;
        }
        var c = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; c.AddThemeConstantOverride("separation", 0); h.AddChild(c);
        if (it != null)
        {
            var l = Lbl(PetDb.IsGear(it.Slot) ? $"{it.Name} (L{it.Lvl}) {PetDb.GearText(it)}" : it.Describe(), 15, it.Color, c); l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            string kind = PetDb.IsGear(it.Slot) ? "pet " + PetDb.GearName(it.Slot).ToLower() : it.Slot == "weapon" ? (it.Wt ?? "weapon") : it.Slot;
            Lbl($"{Items.RarityName(it.Rar)} {kind}" + (it.Sockets > 0 ? $"   {it.Sockets} socket{(it.Sockets == 1 ? "" : "s")}" : "") + (it.Mods is { Count: > 0 } ? $"   {it.Mods.Count} bonus stats" : "") + (it.Up > 0 ? $"   +{it.Up}" : ""), 12, Dim, c);
        }
        else if (p != null)
        {
            var sp = p.Sp(); var st = p.Stats();
            Lbl($"{p.Name()}  Lv{p.Level}  {PetDb.RarityName(sp.Rarity)} {sp.Name}" + (sp.Mountable ? "  (mount)" : ""), 15, PetDb.RarityColor(sp.Rarity), c);
            Lbl($"HP {st.Hp}  ATK {st.Atk}  DEF {st.Def}  MAG {st.Mag}  SPD {st.Spd}   {(sp.Elem == "physical" ? "" : sp.Elem + " ")}{sp.Arch}, {PetDb.Nature(p.Nature).Name}, trainer {p.Ot}", 12, Parch, c);
            var sk = Lbl("Skills: " + string.Join(", ", p.Skills.Select(s => PetDb.Skill(s)?.Name ?? s)), 12, Dim, c); sk.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        }
        return c;
    }

    void AhRow(Node parent, Listing l)
    {
        var d = Game.D; var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 10); CardBox(parent).AddChild(h);
        var c = AhThing(h, l.Item, l.Pet);
        bool mine = l.Seller == Auction.Me, lead = l.Bidder == Auction.Me;
        Lbl((mine ? "Your listing" : "Seller: " + l.SellerName) + (l.Bid > 0 ? $"   highest bid: {(lead ? "you" : l.BidderName)}" : "   no bids yet"), 12, mine || lead ? Gold : Dim, c);
        var pc = new VBoxContainer { CustomMinimumSize = new Vector2(150, 0), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter }; pc.AddThemeConstantOverride("separation", 0); h.AddChild(pc);
        Lbl(l.Bid > 0 ? $"Bid {l.Bid} g" : $"Starts at {l.Start} g", 15, GoldC, pc);
        Lbl(l.Buyout > 0 ? $"Buyout {l.Buyout} g" : "No buyout", 12, Parch, pc);
        Lbl(AhLeft(l.Left), 12, Dim, pc);
        var bc = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter }; bc.AddThemeConstantOverride("separation", 3); h.AddChild(bc);
        if (mine)
        {
            var cb = Btn(l.Bid > 0 ? "Has a bid" : "Cancel", () => { HideTip(); AhClient.Send(new AhReq { Op = "cancel", Id = l.Id }); }, true); PriceStyle(cb, l.Bid <= 0); cb.Disabled = l.Bid > 0; bc.AddChild(cb);
            return;
        }
        int min = Auction.MinBid(l); bool overBuyout = l.Buyout > 0 && min >= l.Buyout;
        if (lead) { var lb = Btn("You lead", () => { }, true); PriceStyle(lb, true); lb.Disabled = true; bc.AddChild(lb); }
        else if (!overBuyout)
        {
            var bb = Btn($"Bid {min} g", () => { HideTip(); if (d.Gold < min) { Sfx.UiError(); Note("Not enough gold", new Color(1, .5f, .5f)); return; } AhClient.Send(new AhReq { Op = "bid", Id = l.Id, Amount = min }); }, true);
            PriceStyle(bb, d.Gold >= min); bb.CustomMinimumSize = new Vector2(120, 30); bc.AddChild(bb);
        }
        if (l.Buyout > 0)
        {
            int cost = lead ? l.Buyout - l.Bid : l.Buyout;
            var yb = Btn($"Buy {l.Buyout} g", () => { HideTip(); if (d.Gold < cost) { Sfx.UiError(); Note("Not enough gold", new Color(1, .5f, .5f)); return; } AhClient.Send(new AhReq { Op = "buyout", Id = l.Id }); }, true);
            PriceStyle(yb, d.Gold >= cost); yb.CustomMinimumSize = new Vector2(120, 30); bc.AddChild(yb);
        }
    }

    // ---------------------------------------------------------------- sell
    static int AhStep(int v, float pct) { int n = (int)Math.Round(v * (1 + pct)); if (n == v) n += pct > 0 ? 1 : -1; return Math.Clamp(n, 1, Auction.MaxPrice); }
    void AhSell(VBoxContainer v)
    {
        var d = Game.D; var pg = AhClient.Page; var durs = Auction.Durations;
        Item? selItem = _ahSellSig != "" ? d.Inv.Find(it => Auction.Sig(it) == _ahSellSig) : null;
        PetState? selPet = _ahSellPet != "" ? d.Pets.Find(p => p.Id == _ahSellPet) : null;
        if (selItem == null) _ahSellSig = ""; if (selPet == null) _ahSellPet = "";
        if (!durs.Contains(_ahHours)) _ahHours = durs[Math.Min(1, durs.Count - 1)];
        void Pick(Item? it, PetState? p)
        {
            _ahSellSig = it != null ? Auction.Sig(it) : ""; _ahSellPet = p?.Id ?? "";
            int fair = it != null ? Auction.Fair(it) : p != null ? Auction.Fair(p) : 1;
            _ahStart = Math.Max(1, Mathf.RoundToInt(fair * .7f)); _ahBuyout = Math.Max(_ahStart + 1, Mathf.RoundToInt(fair * 1.3f)); BuildAuction();
        }
        Row(v, $"Pick something from your pack or a pet, set the prices and how long it runs. The fee ({Auction.FeePct:0.#}% of the start price) is paid now and is not returned; a sale costs {Auction.TaxPct:0.#}% tax. Unsold things come back to the Collect tab.", Dim, 13);
        var row = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; row.AddThemeConstantOverride("separation", 16); v.AddChild(row);
        // ---- left: what can be listed
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(600, 0) }; left.AddThemeConstantOverride("separation", 4); row.AddChild(left);
        Header($"Your pack ({d.Inv.Count}/{Auction.PackSize}) and pets ({d.Pets.Count})", left);
        var ls = Scroll(left, 430); ls.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled; var lv = Inner(ls); lv.AddThemeConstantOverride("separation", 4);
        if (d.Inv.Count == 0 && d.Pets.Count == 0) Empty(lv, "Your pack is empty and you have no pets. Nothing to sell.");
        foreach (var it in d.Inv)
        {
            var item = it; bool on = selItem == it;
            var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 10); CardBox(lv, on).AddChild(h);
            AhThing(h, it, null);
            var b = Btn(on ? "Selected" : "Select", () => { HideTip(); Pick(item, null); }, true); PriceStyle(b, true); b.Disabled = on; h.AddChild(b);
        }
        foreach (var p in d.Pets)
        {
            var pet = p; bool on = selPet == p, comp = p.Id == d.ActivePet, trading = PetTrade.Open && PetTrade.MyIds.Contains(p.Id);
            var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 10); CardBox(lv, on).AddChild(h);
            AhThing(h, null, p);
            var b = Btn(comp ? "Companion" : trading ? "In a trade" : on ? "Selected" : "Select", () => Pick(null, pet), true); PriceStyle(b, !comp && !trading); b.Disabled = on || comp || trading; h.AddChild(b);
        }
        // ---- right: the listing being made
        var right = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; right.AddThemeConstantOverride("separation", 8); row.AddChild(right);
        Header("New listing", right);
        if (selItem == null && selPet == null) { Empty(right, "Select an item or a pet on the left.\nYour companion cannot be listed: pick another companion first."); return; }
        { var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 10); CardBox(right, true).AddChild(h); AhThing(h, selItem, selPet); }
        int fairV = selItem != null ? Auction.Fair(selItem) : Auction.Fair(selPet!);
        if (_ahStart < 1) _ahStart = 1; if (_ahBuyout != 0 && _ahBuyout < _ahStart) _ahBuyout = _ahStart;
        Lbl($"Worth about {fairV} gold to a buyer.", 13, Dim, right);
        void PriceRow(string name, int val, Action<int> set, bool none)
        {
            var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 5); right.AddChild(h);
            var nl = Lbl(name, 15, Parch, h); nl.CustomMinimumSize = new Vector2(92, 0);
            var vl = Lbl(val > 0 ? $"{val} g" : "none", 18, GoldC, h); vl.CustomMinimumSize = new Vector2(96, 0);
            foreach (var (t, k) in new[] { ("-25%", -.25f), ("-5%", -.05f), ("+5%", .05f), ("+25%", .25f) })
            { float kk = k; h.AddChild(Btn(t, () => { set(AhStep(val > 0 ? val : _ahStart, kk)); BuildAuction(); }, true)); }
            if (none) h.AddChild(Btn(val > 0 ? "None" : "Set", () => { set(val > 0 ? 0 : Math.Max(_ahStart + 1, Mathf.RoundToInt(fairV * 1.3f))); BuildAuction(); }, true));
            else h.AddChild(Btn("Worth", () => { set(fairV); BuildAuction(); }, true));
        }
        PriceRow("Start price", _ahStart, x => { _ahStart = x; if (_ahBuyout != 0 && _ahBuyout < x) _ahBuyout = x; }, false);
        PriceRow("Buyout", _ahBuyout, x => _ahBuyout = x == 0 ? 0 : Math.Max(x, _ahStart), true);
        {
            var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 5); right.AddChild(h);
            var nl = Lbl("Runs for", 15, Parch, h); nl.CustomMinimumSize = new Vector2(92, 0);
            foreach (int hrs in durs) { int hh = hrs; var b = Btn($"{hrs} h", () => { _ahHours = hh; BuildAuction(); }, true); if (hrs == _ahHours) b.AddThemeColorOverride("font_color", Gold); h.AddChild(b); }
            Lbl(AhClient.Online ? "   (real hours)" : $"   (game hours: {_ahHours * AhClient.HourSec / 60:0.#} min of play)", 12, Dim, h);
        }
        int fee = Auction.FeeFor(_ahStart); bool canFee = d.Gold >= fee, room = pg.Mine < Auction.MaxListings;
        Lbl($"Listing fee: {fee} gold  ({Auction.FeePct:0.#}% of the start price, paid now)", 16, canFee ? GoldC : ErrRed, right);
        int best = _ahBuyout > 0 ? _ahBuyout : _ahStart;
        Lbl($"If it sells at {best} g: tax {Auction.TaxFor(best)} g, you receive {best - Auction.TaxFor(best)} g.", 13, Dim, right);
        if (!AhClient.Online)
        {   // offline: how the NPC buyers see these prices (data/auction.json npc.buyers)
            string Mood(float r) { float k = Auction.Eager(r); return k <= 0 ? "nobody will pay this" : k < .4f ? "buyers are reluctant" : k <= 1.05f ? "buyers are interested" : "buyers are eager"; }
            Lbl($"Start price: {Mood(_ahStart / (float)fairV)}." + (_ahBuyout > 0 ? $"  Buyout: {Mood(_ahBuyout / (float)fairV)}." : ""), 13, Auction.Eager(_ahStart / (float)fairV) <= 0 ? ErrRed : Parch, right);
        }
        if (!room) Lbl($"You already have {Auction.MaxListings} listings.", 13, ErrRed, right);
        var go = Btn($"List for sale  (fee {fee} g)", () =>
        {
            HideTip();
            if (d.Gold < fee) { Sfx.UiError(); Note($"The listing fee is {fee} gold", new Color(1, .5f, .5f)); return; }
            AhClient.Send(new AhReq { Op = "list", Sig = _ahSellSig, Pet = _ahSellPet, Start = _ahStart, Buyout = _ahBuyout, Hours = _ahHours });
        });
        go.CustomMinimumSize = new Vector2(280, 42); go.Disabled = !canFee || !room || AhClient.Busy; right.AddChild(go);
    }

    // ---------------------------------------------------------------- my listings and bids
    void AhMine(VBoxContainer v)
    {
        var pg = AhClient.Page; var q = AhClient.Q;
        var sc = Scroll(v, 480); sc.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled; var list = Inner(sc); list.AddThemeConstantOverride("separation", 5);
        var mine = pg.L.Where(l => l.Seller == Auction.Me).ToList(); var bids = pg.L.Where(l => l.Seller != Auction.Me && l.Bidder == Auction.Me).ToList();
        Header($"Your listings ({pg.Mine}/{Auction.MaxListings})", list);
        if (mine.Count == 0) Empty(list, pg.Mine == 0 ? "You have nothing for sale. List something on the Sell tab." : "(on another page)");
        foreach (var l in mine) AhRow(list, l);
        Header($"Bids you lead ({pg.Bids})", list);
        if (bids.Count == 0) Empty(list, pg.Bids == 0 ? "You lead no auction. Your gold comes back to Collect when someone outbids you." : "(on another page)");
        foreach (var l in bids) AhRow(list, l);
        if (pg.Pages > 1)
        {
            var nav = new HBoxContainer(); nav.AddThemeConstantOverride("separation", 10); v.AddChild(nav);
            var prev = Btn("< Previous", () => { q.Page = Math.Max(0, pg.Page - 1); AhGo(); }, true); prev.Disabled = pg.Page <= 0; nav.AddChild(prev);
            Lbl($"Page {pg.Page + 1} of {pg.Pages}", 14, Parch, nav);
            var next = Btn("Next >", () => { q.Page = pg.Page + 1; AhGo(); }, true); next.Disabled = pg.Page >= pg.Pages - 1; nav.AddChild(next);
        }
        Row(v, "A listing can be cancelled while nobody has bid (the fee is not returned). When it ends, the gold or the unsold thing waits in Collect.", Dim, 13);
    }

    // ---------------------------------------------------------------- collect
    void AhCollect(VBoxContainer v)
    {
        var d = Game.D; var pg = AhClient.Page; int gold = pg.Box.Sum(e => e.Gold), items = pg.Box.Count(e => e.Item != null), pets = pg.Box.Count(e => e.Pet != null);
        var hd = new HBoxContainer(); hd.AddThemeConstantOverride("separation", 14); v.AddChild(hd);
        Lbl($"Waiting for you: {gold} gold, {items} item{(items == 1 ? "" : "s")}, {pets} pet{(pets == 1 ? "" : "s")}", 16, Parch, hd);
        Lbl($"Pack {d.Inv.Count}/{Auction.PackSize}", 14, d.Inv.Count + items > Auction.PackSize ? ErrRed : Dim, hd);
        hd.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        var all = Btn("Collect all", () => { HideTip(); AhClient.Send(new AhReq { Op = "collect" }); }); all.CustomMinimumSize = new Vector2(180, 38); all.Disabled = pg.Box.Count == 0 || AhClient.Busy; hd.AddChild(all);
        var sc = Scroll(v, 470); sc.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled; var list = Inner(sc); list.AddThemeConstantOverride("separation", 5);
        if (pg.Box.Count == 0) Empty(list, "Nothing is waiting. Gold from sales, outbid gold, things you won and unsold things arrive here.");
        foreach (var e in pg.Box)
        {
            var en = e; var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 10); CardBox(list).AddChild(h);
            VBoxContainer c;
            if (e.Item != null || e.Pet != null) c = AhThing(h, e.Item, e.Pet);
            else
            {
                var g = Lbl("GOLD", 13, GoldC, h); g.CustomMinimumSize = new Vector2(46, 46); g.HorizontalAlignment = HorizontalAlignment.Center; g.VerticalAlignment = VerticalAlignment.Center;
                c = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; h.AddChild(c); Lbl($"{e.Gold} gold", 16, GoldC, c);
            }
            var nl = Lbl(e.Note, 12, Dim, c); nl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            bool fits = e.Item == null || d.Inv.Count < Auction.PackSize;
            var b = Btn(fits ? "Collect" : "Pack full", () => { HideTip(); AhClient.Send(new AhReq { Op = "collect", Id = en.Id }); }, true); PriceStyle(b, fits); b.Disabled = !fits; h.AddChild(b);
        }
    }
}
