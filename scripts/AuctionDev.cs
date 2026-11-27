using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;

namespace TwinRealms;

/// <summary>
/// Dev commands for the auction house (F-37) and pet trading (F-38), registered with Commands (run them with the capture action "cmd:...").
/// "auction test" and "pettrade test" drive the online server's own functions (AuctionServer.Handle / Offer / Swap, Accounts.StoreSave with its
/// reconcile step) on fake accounts in a scratch data folder and print PASS / FAIL per step. They do not open a connection: two real clients
/// talking to a real server is a separate test.
/// </summary>
public static class AuctionDev
{
    static bool _done;
    public static void Register()
    {
        if (_done) return; _done = true;
        Commands.Register("auction", "F-37: auction: the offline book; auction open [browse|sell|mine|collect]; auction give [n]: n rolled items, 2 pets, 500 gold; auction list <pack n> [start x worth] [buyout x worth, 0 none] [hours]; auction listpet <pet n> [..]; auction bid|buy <listing id>; auction collect; auction hours <n>: play n game hours of NPC buyers; auction odds: sale chances by price; auction test: the server logic on fake accounts", Cmd);
        Commands.Register("pettrade", "F-38: pettrade test: the server-run pet swap on two fake accounts (offer check, swap, stats kept, late save, repeat)", a => a.Length > 0 && a[0] == "test" ? TradeTest() : "usage: pettrade test");
    }

    static string Cmd(string[] a)
    {
        string sub = a.Length > 0 ? a[0].ToLowerInvariant() : "";
        if (sub == "test") return Test();
        if (sub == "odds") return Odds();
        if (sub == "open") { Ui.I?.OpenAuction(a.Length > 1 ? a[1] : "browse"); return "opened"; }
        if (AhClient.Online) return "online: the book is the server's (use the window)";
        var d = Game.D; var b = Auction.OfflineBook(d); double hour = Auction.HourSec(false);
        float Num(int i, float def) => a.Length > i && float.TryParse(a[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) ? x : def;
        string Run(AhReq r) { Auction.OfflineTick(d, false); var (ok, msg) = Auction.Run(b, new AhActor(Auction.Me, d), r, d.Played, hour); b.News.Clear(); Game.Notify(); return (ok ? "ok: " : "refused: ") + msg; }
        switch (sub)
        {
            case "give":
            {
                int n = (int)Num(1, 4); for (int i = 0; i < n && d.Inv.Count < Auction.PackSize; i++) d.Inv.Add(Items.Roll(Math.Max(1, d.Level), .3f, null, Math.Min(4, 1 + i % 4)));
                while (d.Pets.Count < 2) { var pool = DuelDb.Strings("pool"); d.Pets.Add(PetDb.New(pool.Count > d.Pets.Count ? pool[d.Pets.Count] : "wolf", Math.Max(3, d.Level), "iron")); }
                d.Gold += 500; Game.Notify(); return $"pack {d.Inv.Count}/{Auction.PackSize}, pets {d.Pets.Count}, gold {d.Gold}";
            }
            case "list":
            case "listpet":
            {
                int i = (int)Num(1, 0); float sm = Num(2, .7f), bm = Num(3, 1.3f); int hrs = (int)Num(4, Auction.Durations[0]);
                if (sub == "list")
                {
                    if (i < 0 || i >= d.Inv.Count) return "no such pack item"; var it = d.Inv[i]; int fair = Auction.Fair(it);
                    return $"{it.Name} worth {fair}: " + Run(new AhReq { Op = "list", Sig = Auction.Sig(it), Start = Math.Max(1, Mathf.RoundToInt(fair * sm)), Buyout = bm <= 0 ? 0 : Math.Max(Mathf.RoundToInt(fair * sm), Mathf.RoundToInt(fair * bm)), Hours = hrs });
                }
                if (i < 0 || i >= d.Pets.Count) return "no such pet"; var p = d.Pets[i]; int pf = Auction.Fair(p);
                return $"{p.Name()} worth {pf}: " + Run(new AhReq { Op = "list", Pet = p.Id, Start = Math.Max(1, Mathf.RoundToInt(pf * sm)), Buyout = bm <= 0 ? 0 : Math.Max(Mathf.RoundToInt(pf * sm), Mathf.RoundToInt(pf * bm)), Hours = hrs });
            }
            case "bid": { var l = b.Open.Find(x => x.Id == (long)Num(1, 0)); return l == null ? "no such listing" : Run(new AhReq { Op = "bid", Id = l.Id, Amount = Auction.MinBid(l) }); }
            case "buy": return Run(new AhReq { Op = "buyout", Id = (long)Num(1, 0) });
            case "cancel": return Run(new AhReq { Op = "cancel", Id = (long)Num(1, 0) });
            case "collect": return Run(new AhReq { Op = "collect" });
            case "stock": Auction.OfflineTick(d, false); Auction.Restock(b, d, d.Played, 999); return $"{b.Open.Count} listings";
            case "hours":
            {
                int n = Math.Clamp((int)Num(1, 1), 1, 96); var log = new List<string>(); int gold0 = d.Gold;
                Auction.OfflineTick(d, false, log); d.Played += (float)(n * hour); Auction.OfflineTick(d, false, log); Game.Notify();
                return $"{n} game hours passed ({n * hour:0} s of play)\n" + (log.Count == 0 ? "nothing happened to your listings or bids" : string.Join("\n", log)) + "\n" + Status(d, b);
            }
        }
        Auction.OfflineTick(d, false); return Status(d, b);
    }
    static string Status(PlayerStats d, AhBook b)
    {
        var sb = new System.Text.StringBuilder(); double hour = Auction.HourSec(false);
        sb.AppendLine($"offline book: {b.Open.Count} listings ({b.Open.Count(l => l.Seller.StartsWith("npc:"))} NPC, {b.Open.Count(l => l.Pet != null)} pets), box {b.Box.Count(e => e.Owner == Auction.Me)}, fees kept {b.Fees}, tax kept {b.Tax}, hero gold {d.Gold}, pack {d.Inv.Count}/{Auction.PackSize}");
        foreach (var l in b.Open.Where(x => x.Seller == Auction.Me))
            sb.AppendLine($"  yours #{l.Id} {l.Title()}: start {l.Start} buyout {l.Buyout} bid {l.Bid} ({l.BidderName}) worth {Auction.Fair(l)}, {(l.End - d.Played) / hour:0.#} h left; per hour: bid {Auction.BidChance(l) * 100:0}% buyout {Auction.BuyoutChance(l) * 100:0}%");
        foreach (var l in b.Open.Where(x => x.Bidder == Auction.Me)) sb.AppendLine($"  you lead #{l.Id} {l.Title()} at {l.Bid}");
        foreach (var e in b.Box.Where(x => x.Owner == Auction.Me)) sb.AppendLine($"  box #{e.Id}: {(e.Gold > 0 ? e.Gold + " gold" : e.Item?.Name ?? e.Pet?.Name() ?? "?")}  ({e.Note})");
        foreach (var l in b.Open.Where(x => x.Seller.StartsWith("npc:")).OrderBy(x => x.Price()).Take(5)) sb.AppendLine($"  npc #{l.Id} {l.Title()}: start {l.Start} buyout {l.Buyout} bid {l.Bid}");
        return sb.ToString().TrimEnd();
    }
    /// <summary>How price against worth moves an NPC sale (the numbers of data/auction.json npc.buyers), and the chance to sell within 24 game hours.</summary>
    static string Odds()
    {
        var sb = new System.Text.StringBuilder("price / worth -> NPC bid chance per game hour (at the start price), buyout chance per hour, chance of at least one of them within 24 h\n");
        foreach (float r in new[] { .5f, .8f, 1f, 1.2f, 1.4f, 1.6f, 2f })
        {
            var it = new Item { Name = "probe", Dmg = 25, Rar = 2, Lvl = 10 }; int fair = Auction.Fair(it);
            var l = new Listing { Item = it, Start = Math.Max(1, Mathf.RoundToInt(fair * r)), Buyout = Math.Max(1, Mathf.RoundToInt(fair * r)) };
            float pb = Auction.BidChance(l), py = Auction.BuyoutChance(l), none = Mathf.Pow((1 - pb) * (1 - py), 24);
            sb.AppendLine($"  x{r:0.0}: bid {pb * 100:0.#}%  buyout {py * 100:0.#}%  sells within 24 h: {(1 - none) * 100:0.#}%");
        }
        return sb.ToString().TrimEnd();
    }

    // ------------------------------------------------------------------ fake accounts in a scratch data folder
    static string _oldDir = "", _dir = "";
    static void Scratch()
    {
        _oldDir = Accounts.Dir; _dir = Path.Combine(ProjectSettings.GlobalizePath("user://"), "ah_selftest");
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        Directory.CreateDirectory(Path.Combine(_dir, "accounts")); Directory.CreateDirectory(Path.Combine(_dir, "saves"));
        Accounts.Dir = _dir; AuctionServer.Forget(); AuctionServer.TimeShift = 0; OnlineCfg.Load();
    }
    static void Unscratch()
    {
        Accounts.Dir = _oldDir; AuctionServer.Forget(); AuctionServer.TimeShift = 0;
        try { if (_dir != "" && Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
    }
    static string? Make(string key, PlayerStats d) { var f = new Game.SaveFile(); f.Slots[0] = d; f.Last = 0; return Accounts.StoreSave(key, Auction.ToJson(f)); }
    static PlayerStats Hero(string key) => Auction.FromJson<Game.SaveFile>(Accounts.LoadSave(key))!.Slots[0]!;
    static void Edit(string key, Action<PlayerStats> change) { var f = Auction.FromJson<Game.SaveFile>(Accounts.LoadSave(key))!; change(f.Slots[0]!); Accounts.StoreSave(key, Auction.ToJson(f)); }

    /// <summary>"auction test": the server's auction logic end to end on three fake accounts (a seller and two bidders).</summary>
    public static string Test()
    {
        if (Net.Dedicated || Game.Online) return "auction test: run it in an offline game (it switches the account folder to a scratch one)";
        var log = new List<string>(); int fails = 0;
        void Check(string name, bool ok, string detail = "") { log.Add($"{(ok ? "PASS" : "FAIL")}  {name}{(detail != "" ? "   [" + detail + "]" : "")}"); if (!ok) fails++; }
        try
        {
            Scratch();
            const string S = "sella", A = "alba", B = "bram"; const int Gold0 = 1000;
            var sd = new PlayerStats { Name = "Sella", Gold = Gold0 };
            for (int i = 0; i < 5; i++) sd.Inv.Add(Items.Roll(6 + i, 0, i % 2 == 0 ? "weapon" : "armor", 2));
            sd.Pets.Add(PetDb.New("wolf", 9, "iron")); sd.Pets.Add(PetDb.New("boar", 12, "iron")); sd.Pets[1].Xp = 17; sd.Pets[1].Nick = "Truffle"; sd.Pets[1].Bond = 40; sd.ActivePet = sd.Pets[0].Id;
            Check("three fake accounts stored", Make(S, sd) == null && Make(A, new PlayerStats { Name = "Alba", Gold = Gold0 }) == null && Make(B, new PlayerStats { Name = "Bram", Gold = Gold0 }) == null);
            AhReply Req(string acct, string op, Action<AhReq>? set = null) { var r = new AhReq { Op = op }; set?.Invoke(r); return AuctionServer.Handle(acct, r); }
            int expFees = 0, expTax = 0;

            // 1. list (fee up front, item leaves the server's save)
            string sig0 = Auction.Sig(Hero(S).Inv[0]); int fee1 = Auction.FeeFor(100);
            var re = Req(S, "list", r => { r.Sig = sig0; r.Start = 100; r.Buyout = 400; r.Hours = Auction.Durations[0]; }); expFees += fee1;
            long id1 = AuctionServer.Book.Open.LastOrDefault()?.Id ?? 0;
            Check("list: accepted, fee taken, item out of the seller's save", re.Ok && Hero(S).Gold == Gold0 - fee1 && Hero(S).Inv.Count == 4 && !Hero(S).Inv.Exists(i => Auction.Sig(i) == sig0) && AuctionServer.Book.Open.Count == 1, $"{re.Msg}; seller gold {Hero(S).Gold}");
            Check("list: the reply carries the same change for the player's game", re.Delta != null && re.Delta.Gold == -fee1 && re.Delta.RmItems.Count == 1 && re.Delta.Seq == Hero(S).AhSeq);
            re = Req(S, "list", r => { r.Sig = "{\"Name\":\"Forged Blade\",\"Dmg\":999}"; r.Start = 100; r.Hours = Auction.Durations[0]; });
            Check("list: an item that is not in the server's save is refused", !re.Ok && re.Delta == null && Hero(S).Gold == Gold0 - fee1 && AuctionServer.Book.Open.Count == 1, re.Msg);
            re = Req(S, "bid", r => { r.Id = id1; r.Amount = 100; });
            Check("bid: the seller cannot bid on their own listing", !re.Ok, re.Msg);
            Edit(A, d => d.Gold = 50); re = Req(A, "bid", r => { r.Id = id1; r.Amount = 100; });
            Check("bid: refused without the gold in the server's save (whatever the game claims)", !re.Ok && Hero(A).Gold == 50, re.Msg);
            Edit(A, d => d.Gold = Gold0);

            // 2. bid from A, outbid from B, refund
            re = Req(A, "bid", r => { r.Id = id1; r.Amount = 100; });
            Check("bid from A: gold reserved (taken from A's save)", re.Ok && Hero(A).Gold == Gold0 - 100 && AuctionServer.Book.Open[0].Bid == 100, re.Msg);
            int min = Auction.MinBid(AuctionServer.Book.Open[0]);
            re = Req(B, "bid", r => { r.Id = id1; r.Amount = min - 1; });
            Check("bid below the minimum step refused", !re.Ok && Hero(B).Gold == Gold0, re.Msg);
            re = Req(B, "bid", r => { r.Id = id1; r.Amount = min; });
            Check($"outbid from B at {min}: B pays, A's 100 waits in A's box", re.Ok && Hero(B).Gold == Gold0 - min && AuctionServer.Book.Box.Count(e => e.Owner == A + ":0" && e.Gold == 100) == 1 && Hero(A).Gold == Gold0 - 100, re.Msg);
            re = Req(A, "collect");
            Check("refund collected: A has all the gold back", re.Ok && Hero(A).Gold == Gold0 && re.Delta != null && re.Delta.Gold == 100, re.Msg);

            // 3. a late save from A (sent before the server's change) must not undo it
            long seqBefore = Hero(A).AhSeq;
            re = Req(A, "bid", r => { r.Id = id1; r.Amount = Auction.MinBid(AuctionServer.Book.Open[0]); }); int bidA2 = AuctionServer.Book.Open[0].Bid;
            var lateFile = Auction.FromJson<Game.SaveFile>(Accounts.LoadSave(A))!; lateFile.Slots[0]!.Gold += bidA2; lateFile.Slots[0]!.AhSeq = seqBefore;   // A's save as it was just before the bid (an upload already on its way)
            string? ue = Accounts.StoreSave(A, Auction.ToJson(lateFile));
            Check("a save uploaded from before the bid is brought up to date (gold stays reserved)", ue == null && Hero(A).Gold == Gold0 - bidA2, $"A gold {Hero(A).Gold}, expected {Gold0 - bidA2}");
            re = Req(B, "collect"); Check("B's outbid gold collected", Hero(B).Gold == Gold0, re.Msg);

            // 4. buyout
            re = Req(B, "buyout", r => r.Id = id1); int tax1 = Auction.TaxFor(400); expTax += tax1;
            Check("buyout by B: listing closed, B paid 400, A's bid refunded to A's box", re.Ok && AuctionServer.Book.Open.Count == 0 && Hero(B).Gold == Gold0 - 400 && AuctionServer.Book.Box.Exists(e => e.Owner == A + ":0" && e.Gold == bidA2), re.Msg);
            Req(A, "collect"); Req(B, "collect"); Req(S, "collect");
            Check("buyout: B holds the item, exactly as listed", Hero(B).Inv.Count == 1 && Auction.Sig(Hero(B).Inv[0]) == sig0);
            Check($"buyout: seller got 400 minus {tax1} tax", Hero(S).Gold == Gold0 - fee1 + 400 - tax1, $"seller gold {Hero(S).Gold}");
            Check("buyout: A is whole again", Hero(A).Gold == Gold0);

            // 5. expiry without a bid: the item returns
            string sig1 = Auction.Sig(Hero(S).Inv[0]); int fee2 = Auction.FeeFor(50), sGold = Hero(S).Gold;
            re = Req(S, "list", r => { r.Sig = sig1; r.Start = 50; r.Hours = Auction.Durations[0]; }); expFees += fee2;
            AuctionServer.TimeShift += (Auction.Durations[0] + 1) * Auction.HourSec(true); AuctionServer.TickNow();
            Check("expiry, no bid: listing gone, the item waits in the seller's box", re.Ok && AuctionServer.Book.Open.Count == 0 && AuctionServer.Book.Box.Exists(e => e.Owner == S + ":0" && e.Item != null && Auction.Sig(e.Item) == sig1));
            re = Req(S, "collect");
            Check("expiry return collected: item back in the pack, only the fee was lost", Hero(S).Inv.Exists(i => Auction.Sig(i) == sig1) && Hero(S).Gold == sGold - fee2, re.Msg);

            // 6. expiry with a bid: sold to the bidder while both are "offline"
            int fee3 = Auction.FeeFor(200), tax3 = Auction.TaxFor(200); sGold = Hero(S).Gold;
            re = Req(S, "list", r => { r.Sig = sig1; r.Start = 200; r.Hours = Auction.Durations[0]; }); expFees += fee3; long id3 = AuctionServer.Book.Open[0].Id;
            Req(A, "bid", r => { r.Id = id3; r.Amount = 200; });
            AuctionServer.TimeShift += (Auction.Durations[0] + 1) * Auction.HourSec(true); AuctionServer.TickNow(); expTax += tax3;
            Check("expiry with a bid: paid out to the seller's box and the item to the bidder's, nobody online", AuctionServer.Book.Open.Count == 0 && AuctionServer.Book.Box.Exists(e => e.Owner == S + ":0" && e.Gold == 200 - tax3) && AuctionServer.Book.Box.Exists(e => e.Owner == A + ":0" && e.Item != null));
            re = Req(S, "hello"); var re2 = Req(A, "hello");
            Check("delivered on entering the world (hello): seller's gold, bidder's item", Hero(S).Gold == sGold - fee3 + 200 - tax3 && Hero(A).Inv.Exists(i => Auction.Sig(i) == sig1) && Hero(A).Gold == Gold0 - 200, $"{re.Msg} / {re2.Msg}");

            // 7. cancel
            string sig2 = Auction.Sig(Hero(S).Inv[0]); int fee4 = Auction.FeeFor(80); sGold = Hero(S).Gold;
            Req(S, "list", r => { r.Sig = sig2; r.Start = 80; r.Hours = Auction.Durations[0]; }); expFees += fee4; long id4 = AuctionServer.Book.Open[0].Id;
            re = Req(A, "cancel", r => r.Id = id4); Check("cancel: only the seller can", !re.Ok, re.Msg);
            re = Req(S, "cancel", r => r.Id = id4); Req(S, "collect");
            Check("cancel: item back, fee kept", re.Ok && Hero(S).Inv.Exists(i => Auction.Sig(i) == sig2) && Hero(S).Gold == sGold - fee4, re.Msg);

            // 8. a pet listing: the companion is refused, another pet sells whole
            var pet = Hero(S).Pets[1]; var st0 = pet.Stats(); string petId = pet.Id, compId = Hero(S).Pets[0].Id; int feeP = Auction.FeeFor(300), taxP = Auction.TaxFor(300); sGold = Hero(S).Gold; int bGold = Hero(B).Gold;
            re = Req(S, "list", r => { r.Pet = compId; r.Start = 300; r.Hours = Auction.Durations[0]; });
            Check("pet listing: the active companion is refused", !re.Ok && Hero(S).Pets.Count == 2, re.Msg);
            re = Req(S, "list", r => { r.Pet = petId; r.Start = 300; r.Buyout = 300; r.Hours = Auction.Durations[0]; }); expFees += feeP;
            Check("pet listing: accepted, the pet left the seller's save", re.Ok && Hero(S).Pets.Count == 1 && AuctionServer.Book.Open.Exists(l => l.Pet != null), re.Msg);
            var page = Req(A, "query", r => r.Q.Slot = "pet").Page;
            Check("search: the pet filter finds it, other filters do not", page != null && page.L.Count == 1 && page.L[0].Pet != null && page.L[0].Seller == "" && Req(A, "query", r => r.Q.Slot = "weapon").Page!.L.Count == 0 && Req(A, "query", r => r.Q.Text = "truffle").Page!.L.Count == 1 && Req(A, "query", r => r.Q.PrMin = 5000).Page!.L.Count == 0);
            re = Req(B, "buyout", r => r.Id = AuctionServer.Book.Open[0].Id); expTax += taxP; Req(B, "collect"); Req(S, "collect");
            var got = Hero(B).Pets.FirstOrDefault(); var st1 = got?.Stats() ?? default;
            Check("pet sold: level, xp, nature, name, skills and stats unchanged; new Id; seller no longer has it", got != null && got.Id != petId && got.Species == pet.Species && got.Level == pet.Level && got.Xp == pet.Xp && got.Nature == pet.Nature && got.Nick == pet.Nick && got.Bond == pet.Bond && got.Skills.SequenceEqual(pet.Skills) && got.Learned.SequenceEqual(pet.Learned) && st0.Hp == st1.Hp && st0.Atk == st1.Atk && st0.Def == st1.Def && st0.Mag == st1.Mag && st0.Spd == st1.Spd && !Hero(S).Pets.Exists(p => p.Id == petId), got == null ? "no pet arrived" : $"Lv{got.Level} {got.Name()} HP {st1.Hp}");
            Check("pet sold: gold moved (price minus tax to the seller)", Hero(S).Gold == sGold - feeP + 300 - taxP && Hero(B).Gold == bGold - 300);

            // 9. a full pack: the item waits in the box
            string sig3 = Auction.Sig(Hero(S).Inv[0]); int fee5 = Auction.FeeFor(10), tax5 = Auction.TaxFor(10);
            Req(S, "list", r => { r.Sig = sig3; r.Start = 10; r.Buyout = 10; r.Hours = Auction.Durations[0]; }); expFees += fee5;
            Edit(A, d => { while (d.Inv.Count < Auction.PackSize) d.Inv.Add(Items.Roll(1, 0, "trinket", 0)); });
            Req(A, "buyout", r => r.Id = AuctionServer.Book.Open[0].Id); expTax += tax5; re = Req(A, "collect");
            Check("collect with a full pack: the item stays in the box, the pack stays at 28", Hero(A).Inv.Count == Auction.PackSize && AuctionServer.Book.Box.Exists(e => e.Owner == A + ":0" && e.Item != null), re.Msg);
            Edit(A, d => d.Inv.RemoveAt(d.Inv.Count - 1)); re = Req(A, "collect");
            Check("collect after making room: the item arrives", Hero(A).Inv.Exists(i => Auction.Sig(i) == sig3) && !AuctionServer.Book.Box.Exists(e => e.Owner == A + ":0"), re.Msg);
            Req(S, "collect");

            // 10. totals: no gold made or lost, fees and tax as the rules say
            var bk = AuctionServer.Book; long held = bk.Box.Sum(e => (long)e.Gold) + bk.Open.Sum(l => (long)l.Bid), total = Hero(S).Gold + Hero(A).Gold + Hero(B).Gold + bk.Fees + bk.Tax + held;
            Check($"fees kept by the house = {expFees}", bk.Fees == expFees, $"book says {bk.Fees}");
            Check($"tax kept by the house = {expTax}", bk.Tax == expTax, $"book says {bk.Tax}");
            Check($"gold conserved: heroes + fees + tax + held = {3 * Gold0}", total == 3 * Gold0, $"heroes {Hero(S).Gold}+{Hero(A).Gold}+{Hero(B).Gold}, fees {bk.Fees}, tax {bk.Tax}, held {held}");
            int gS = Hero(S).Gold, gA = Hero(A).Gold, gB = Hero(B).Gold;
            Edit(S, d => { }); Edit(A, d => { }); Edit(B, d => { });   // each game uploads a save that contains every change
            Check("nothing left pending once every save has caught up, and nothing applied twice", bk.Pending.Count == 0 && Hero(S).Gold == gS && Hero(A).Gold == gA && Hero(B).Gold == gB, $"{bk.Pending.Values.Sum(l => l.Count)} pending");
            // the book survives a restart of the server
            long seq = bk.Seq; AuctionServer.Forget();
            Check("the book file reads back after a restart", AuctionServer.Book.Seq == seq && AuctionServer.Book.Fees == expFees);
        }
        catch (Exception e) { Check("no exception", false, e.GetType().Name + ": " + e.Message); }
        finally { Unscratch(); }
        log.Add(fails == 0 ? $"auction test: ALL {log.Count} STEPS PASS (server logic on fake accounts; not a test with two real clients)" : $"auction test: {fails} FAILED");
        return string.Join("\n", log);
    }

    /// <summary>"pettrade test": the server-run swap of F-38 on two fake accounts.</summary>
    public static string TradeTest()
    {
        if (Net.Dedicated || Game.Online) return "pettrade test: run it in an offline game (it switches the account folder to a scratch one)";
        var log = new List<string>(); int fails = 0;
        void Check(string name, bool ok, string detail = "") { log.Add($"{(ok ? "PASS" : "FAIL")}  {name}{(detail != "" ? "   [" + detail + "]" : "")}"); if (!ok) fails++; }
        try
        {
            Scratch();
            const string A = "pa", B = "pb";
            var da = new PlayerStats { Name = "Ansel", Gold = 500 }; da.Pets.Add(PetDb.New("wolf", 7, "iron")); da.Pets.Add(PetDb.New("boar", 4, "iron")); da.Pets[0].Nick = "Grey"; da.Pets[0].Xp = 23; da.Pets[0].Bond = 55; da.Pets[0].Ot = "Ansel"; da.ActivePet = da.Pets[0].Id;
            var db = new PlayerStats { Name = "Brida", Gold = 300 }; db.Pets.Add(PetDb.New("viper", 12, "silver")); db.Pets[0].Ot = "Brida"; db.Pets[0].Xp = 5;
            Check("two fake accounts stored", Make(A, da) == null && Make(B, db) == null);
            var a0 = Hero(A).Pets[0]; var b0 = Hero(B).Pets[0]; var sa = a0.Stats(); var sb = b0.Stats();
            var idsA = new List<string> { a0.Id }; var idsB = new List<string> { b0.Id };

            Check("offer: a pet that is not in the server's save is refused", AuctionServer.Offer(A, new List<string> { "nope" }, 0) == null);
            var oa = AuctionServer.Offer(A, idsA, 9999); var ob = AuctionServer.Offer(B, idsB, 0);
            Check("offer: the server builds the view from its own copy; gold capped to what the save holds", oa != null && ob != null && oa.Value.gold == 500 && PetTrade.ParseOffer(oa.Value.json) is { Count: 1 } va && va[0].Level == 7 && va[0].Nick == "Grey");
            oa = AuctionServer.Offer(A, idsA, 50);
            var r = AuctionServer.Swap(A, idsA, 50, "[\"tampered\"]", B, idsB, 0, ob!.Value.json);
            Check("swap refused when an offer is not what the partner was shown; nothing changed", !r.ok && Hero(A).Pets.Count == 2 && Hero(B).Pets.Count == 1 && Hero(A).Gold == 500, r.msg);
            r = AuctionServer.Swap(A, idsA, 50, oa!.Value.json, A, idsA, 0, oa.Value.json);
            Check("swap with yourself refused", !r.ok, r.msg);
            string lateA = Accounts.LoadSave(A);   // A's save from before the trade, as an upload already on its way would be

            r = AuctionServer.Swap(A, idsA, 50, oa.Value.json, B, idsB, 0, ob.Value.json);
            var ha = Hero(A); var hb = Hero(B); var gotA = ha.Pets.Find(p => p.Species == "viper"); var gotB = hb.Pets.Find(p => p.Species == "wolf");
            Check("swap: done in one step, both deltas returned", r.ok && r.a != null && r.b != null, r.msg);
            Check("swap: each pet has exactly one owner", ha.Pets.Count == 2 && hb.Pets.Count == 1 && gotA != null && gotB != null && !ha.Pets.Exists(p => p.Id == a0.Id) && !hb.Pets.Exists(p => p.Id == b0.Id) && !ha.Pets.Select(p => p.Id).Intersect(hb.Pets.Select(p => p.Id)).Any());
            var ga = gotB?.Stats() ?? default; var gb = gotA?.Stats() ?? default;
            Check("swap: A's wolf arrived at B with level, xp, bond, nature, name, trainer, skills and stats intact", gotB != null && gotB.Level == 7 && gotB.Xp == 23 && gotB.Bond == 55 && gotB.Nick == "Grey" && gotB.Ot == "Ansel" && gotB.Nature == a0.Nature && gotB.Skills.SequenceEqual(a0.Skills) && gotB.Learned.SequenceEqual(a0.Learned) && ga.Hp == sa.Hp && ga.Atk == sa.Atk && ga.Def == sa.Def && ga.Mag == sa.Mag && ga.Spd == sa.Spd, gotB == null ? "missing" : $"Lv{gotB.Level} HP {ga.Hp}/{sa.Hp}");
            Check("swap: B's viper arrived at A the same way", gotA != null && gotA.Level == 12 && gotA.Xp == 5 && gotA.Ot == "Brida" && gotA.Nature == b0.Nature && gb.Hp == sb.Hp && gb.Atk == sb.Atk && gb.Spd == sb.Spd);
            Check("swap: gold moved (A 450, B 350); the traded companion is no longer A's active pet", ha.Gold == 450 && hb.Gold == 350 && ha.ActivePet == "", $"A {ha.Gold} B {hb.Gold} active '{ha.ActivePet}'");

            // a disconnect: A's game never got the message and its old save arrives afterwards
            string? e2 = Accounts.StoreSave(A, lateA); ha = Hero(A);
            Check("a late save from before the trade cannot bring the pet back (no duplicate)", e2 == null && ha.Pets.Count == 2 && !ha.Pets.Exists(p => p.Id == a0.Id) && ha.Pets.Exists(p => p.Species == "viper") && ha.Gold == 450, $"A pets {string.Join(",", ha.Pets.Select(p => p.Species))} gold {ha.Gold}");
            // the player's game applying the delta itself gives the same hero
            var local = Auction.FromJson<Game.SaveFile>(lateA)!.Slots[0]!; Auction.Apply(local, r.a!);
            Check("the delta applied by the player's game gives the same hero as the server's save", local.Gold == ha.Gold && local.Pets.Select(p => p.Id).OrderBy(x => x).SequenceEqual(ha.Pets.Select(p => p.Id).OrderBy(x => x)));
            r = AuctionServer.Swap(A, idsA, 0, oa.Value.json, B, idsB, 0, ob.Value.json);
            Check("the same swap again is refused (the pets are gone)", !r.ok && Hero(A).Pets.Count == 2 && Hero(B).Pets.Count == 1, r.msg);
            Check("pets total unchanged (3) and gold total unchanged (800)", Hero(A).Pets.Count + Hero(B).Pets.Count == 3 && Hero(A).Gold + Hero(B).Gold == 800);
        }
        catch (Exception e) { Check("no exception", false, e.GetType().Name + ": " + e.Message); }
        finally { Unscratch(); }
        log.Add(fails == 0 ? $"pettrade test: ALL {log.Count} STEPS PASS (server swap on fake accounts; not a test with two real clients)" : $"pettrade test: {fails} FAILED");
        return string.Join("\n", log);
    }
}
