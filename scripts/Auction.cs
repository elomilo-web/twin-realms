using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace TwinRealms;

/// <summary>One thing for sale (focus F-37): an item or a pet, a start price, an optional buyout, the highest bid and who made it, and when it ends.</summary>
public class Listing
{
    public long Id;
    public string Seller = "", SellerName = "";   // Seller: owner key ("me" offline, "<account>:<slot>" on the server, "npc:<name>" for NPC sellers); views sent to players carry "me" / "npc" / ""
    public Item? Item; public PetState? Pet;
    public int Start, Buyout, Bid, Fee, Hours;    // Buyout 0 = none; Bid 0 = no bid yet; Fee = the listing fee that was paid
    public string Bidder = "", BidderName = "";
    public double End, Left;                      // End on the book's clock (offline: PlayerStats.Played seconds; online: unix seconds); Left = seconds left, filled in views

    public string Title() => Item != null ? Item.Name : Pet != null ? $"{Pet.Name()} (Lv {Pet.Level} {Pet.Sp().Name})" : "?";
    public int Price() => Bid > 0 ? Bid : Start;
    public int Lvl() => Item?.Lvl ?? Pet?.Level ?? 1;
    public int Rar() => Item != null ? Item.Rar : Pet != null ? Auction.PetRar(Pet) : 0;
}
/// <summary>Something waiting in a player's collection box: gold (proceeds, an outbid refund) or an item / pet (won, unsold, cancelled).</summary>
public class BoxEntry { public long Id; public string Owner = "", Note = ""; public int Gold; public Item? Item; public PetState? Pet; }
/// <summary>A change the house made to a hero (gold, items and pets taken or handed over). Online the server applies it to its copy of the save and
/// sends it to the player's game, which applies the same change (Seq orders them; PlayerStats.AhSeq is the last one a save contains).</summary>
public class AhDelta
{
    public long Seq; public int Gold;
    public List<string> RmItems = new(), RmPets = new();   // items by their JSON (Auction.Sig), pets by Id
    public List<Item> AddItems = new(); public List<PetState> AddPets = new();
    // A-30 (player trades, Trade.cs): stacked things given (+) or taken (-) by key ("mat:<id>", "gem:<key>", "pot:hp", "cube:<tier>", TradeStacks); not written when there are none, so older deltas and books read the same
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, int>? Stacks;
    public bool Empty() => Gold == 0 && RmItems.Count == 0 && RmPets.Count == 0 && AddItems.Count == 0 && AddPets.Count == 0 && (Stacks == null || Stacks.Count == 0);
}
/// <summary>The book: open listings, collection boxes, totals. Offline it is saved with the hero (PlayerStats.Ah); online the server keeps one for everybody.</summary>
public class AhBook
{
    public long Seq, Dseq;                        // listing / box ids; delta numbers (server)
    public List<Listing> Open = new();
    public List<BoxEntry> Box = new();
    public double HourAt;                         // offline: the clock up to which NPC hours have been played
    public bool Stocked;                          // offline: the NPC sellers have filled the house once
    public long Fees, Tax;                        // gold the house has kept
    public Dictionary<string, List<AhDelta>> Pending = new();   // server: deltas per owner that an uploaded save may not contain yet
    [JsonIgnore] public List<(string owner, string text)> News = new();   // what just happened to whom (drained into notes)
}
public class AhQuery
{
    public string Text = "", Slot = "", Sort = "price", Scope = "";   // Slot: "" any, an item slot, "petgear", "pet"; Sort: price | priceDesc | time | timeDesc; Scope: "" browse (with the filters), "mine" (your listings and the ones you lead)
    public int Rar = -1, LvMin, LvMax, PrMin, PrMax, Page;            // Rar = lowest rarity shown; 0 = no limit for the ranges
}
public class AhPage { public List<Listing> L = new(); public int Total, Page, Pages = 1, Mine, Bids; public List<BoxEntry> Box = new(); }
public class AhReq { public string Op = "query"; public AhQuery Q = new(); public string Sig = "", Pet = ""; public int Start, Buyout, Hours, Amount; public long Id; }
public class AhReply { public bool Ok; public string Op = "", Msg = ""; public AhDelta? Delta; public AhPage? Page; }

/// <summary>A hero as the auction engine sees it: gold, pack and pets, with every change recorded in Delta.</summary>
public class AhActor
{
    public readonly string Key, Name; public readonly PlayerStats D; public readonly AhDelta Delta = new();
    public AhActor(string key, PlayerStats d) { Key = key; D = d; Name = d.Name; }
    public bool TakeGold(int n) { if (n < 0 || D.Gold < n) return false; D.Gold -= n; Delta.Gold -= n; return true; }
    public void GiveGold(int n) { if (n <= 0) return; n = (int)Math.Min(n, (long)int.MaxValue - D.Gold); D.Gold += n; Delta.Gold += n; }
    public Item? TakeItem(string sig) { int i = D.Inv.FindIndex(it => Auction.Sig(it) == sig); if (i < 0) return null; var it2 = D.Inv[i]; D.Inv.RemoveAt(i); Delta.RmItems.Add(sig); return it2; }
    public bool GiveItem(Item it) { if (D.Inv.Count >= Auction.PackSize) return false; D.Inv.Add(it); Delta.AddItems.Add(it); return true; }
    public PetState? TakePet(string id) { var p = D.Pets.Find(x => x.Id == id); if (p == null) return null; D.Pets.Remove(p); if (D.ActivePet == id) D.ActivePet = ""; Delta.RmPets.Add(id); return p; }
    /// <summary>The pet arrives whole (level, xp, nature, skills, gear, trainer); only its Id is new.</summary>
    public PetState GivePet(PetState p)
    {
        var np = PetTrade.Unpack(PetTrade.Pack(p)) ?? PetState.FromJson(p.ToJson()) ?? p; if (np.Id == p.Id || np.Id == "") np.Id = PetTrade.NewId();
        while (D.Pets.Exists(x => x.Id == np.Id)) np.Id = PetTrade.NewId();
        D.Pets.Add(np); Delta.AddPets.Add(np); return np;
    }
}

/// <summary>
/// The auction house (focus F-37). One model for both ways of playing:
/// - Offline the book is saved with the hero (PlayerStats.Ah). NPC sellers keep it stocked with rolled items and pets; as game time passes
///   (PlayerStats.Played, one game hour = rules.offlineHourSec) NPC buyers bid on or buy out the hero's listings with a chance that falls as the
///   price rises above what the thing is worth (OfflineTick / NpcHour).
/// - Online the server holds the one shared book (NetAuction.cs) and runs the very same functions on its own copy of each hero's save.
/// Rules are data (data/auction.json): listing fee (% of the start price, paid up front), sale tax (% of the price, taken from the proceeds),
/// durations, minimum bid step, listings per hero. Sold: the price minus tax goes to the seller's collection box and the thing to the buyer's.
/// Unsold or cancelled: the thing goes back to the seller's box. Outbid: the gold goes back to the bidder's box. Gold is taken when a bid is made.
/// </summary>
public static class Auction
{
    public const int PackSize = 28; public const string Me = "me";
    static readonly JsonSerializerOptions Opt = new() { IncludeFields = true };
    public static string ToJson<T>(T o) => JsonSerializer.Serialize(o, Opt);
    public static T? FromJson<T>(string s) { try { return JsonSerializer.Deserialize<T>(s, Opt); } catch { return default; } }
    /// <summary>An item's identity on the wire and in a save: its whole JSON.</summary>
    public static string Sig(Item it) => JsonSerializer.Serialize(it, Opt);

    // ------------------------------------------------------------------ rules (data/auction.json)
    static JsonElement _root; static bool _loaded;
    static JsonElement Root { get { if (!_loaded) { _loaded = true; try { using var doc = JsonUtil.Load("res://data/auction.json"); _root = doc.RootElement.Clone(); } catch (Exception e) { GD.PushError("auction.json: " + e.Message); } } return _root; } }
    static JsonElement? Sec(string path) { var e = Root; foreach (var p in path.Split('.')) { if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(p, out e)) return null; } return e; }
    static float F(string sec, string k, float d) => Sec(sec) is { ValueKind: JsonValueKind.Object } s ? JsonUtil.F(s, k, d) : d;
    public static float FeePct => F("rules", "feePct", 5); public static float TaxPct => F("rules", "taxPct", 8);
    public static int MaxListings => (int)F("rules", "maxListings", 12); public static int MaxPrice => (int)F("rules", "maxPrice", 1000000);
    public static int PageSize => Math.Max(1, (int)F("rules", "pageSize", 8)); public static int MaxBook => (int)F("rules", "maxBook", 4000);
    public static double HourSec(bool online) => Math.Max(1, online ? F("rules", "onlineHourSec", 3600) : F("rules", "offlineHourSec", 30));
    public static List<int> Durations
    {
        get { var l = new List<int>(); if (Sec("rules.durations") is { ValueKind: JsonValueKind.Array } a) foreach (var x in a.EnumerateArray()) if (x.ValueKind == JsonValueKind.Number) l.Add((int)x.GetSingle()); if (l.Count == 0) l.AddRange(new[] { 12, 24, 48 }); return l; }
    }
    public static int FeeFor(int start) => Math.Max((int)F("rules", "feeMin", 1), (int)Math.Ceiling(start * (double)FeePct / 100.0));
    public static int TaxFor(int price) => Math.Clamp((int)Math.Floor(price * (double)TaxPct / 100.0), 0, Math.Max(0, price));
    /// <summary>The lowest bid the listing accepts now.</summary>
    public static int MinBid(Listing l) => l.Bid <= 0 ? l.Start : (int)Math.Min(MaxPrice, l.Bid + Math.Max((long)F("rules", "minBidStep", 1), (long)Math.Ceiling(l.Bid * (double)F("rules", "minBidStepPct", 5) / 100.0)));

    // ------------------------------------------------------------------ what things are worth (NPC buyers, suggested prices)
    public static int Fair(Item it) => Math.Max(1, Mathf.RoundToInt(it.Value * F("value", "itemMul", 3) + F("value", "itemAdd", 12)));
    public static int Fair(PetState p)
    {
        string rar = p.Sp().Rarity; float b = Sec("value.pet") is { ValueKind: JsonValueKind.Object } pv ? JsonUtil.F(pv, rar, 100) : 100;
        int v = Mathf.RoundToInt(b * (1 + F("value", "petPerLevel", .12f) * (Math.Max(1, p.Level) - 1)));
        foreach (var g in p.Gear.Values) if (g != null) v += Fair(g);
        return Math.Max(1, v);
    }
    public static int Fair(Listing l) => l.Item != null ? Fair(l.Item) : l.Pet != null ? Fair(l.Pet) : 1;
    /// <summary>A pet's rarity on the item scale 0..4 (for the rarity filter and colours).</summary>
    public static int PetRar(PetState p) => p.Sp().Rarity switch { "common" => 0, "uncommon" => 1, "rare" => 2, "epic" => 3, _ => 4 };

    // ------------------------------------------------------------------ the engine (offline and server alike)
    static void AddBox(AhBook b, string owner, string note, int gold = 0, Item? item = null, PetState? pet = null)
    {
        if (owner == "" || owner.StartsWith("npc")) return;   // NPCs keep their own purse
        b.Box.Add(new BoxEntry { Id = ++b.Seq, Owner = owner, Note = note, Gold = gold, Item = item, Pet = pet }); b.News.Add((owner, note));
    }
    static void Refund(AhBook b, Listing l) { if (l.Bid > 0 && l.Bidder != "") AddBox(b, l.Bidder, $"Outbid on {l.Title()}: your {l.Bid} gold is back", l.Bid); }
    /// <summary>Ends a listing. With a bid: the thing goes to the bidder's box, the price minus tax to the seller's. Without: the thing returns to the seller's box.</summary>
    static void Settle(AhBook b, Listing l)
    {
        b.Open.Remove(l);
        if (l.Bid > 0 && l.Bidder != "")
        {
            int tax = TaxFor(l.Bid); b.Tax += tax;
            AddBox(b, l.Seller, $"Sold: {l.Title()} for {l.Bid} gold to {l.BidderName} (tax {tax})", l.Bid - tax);
            AddBox(b, l.Bidder, $"Won: {l.Title()} for {l.Bid} gold", 0, l.Item, l.Pet);
        }
        else AddBox(b, l.Seller, $"Unsold: {l.Title()} came back", 0, l.Item, l.Pet);
    }
    /// <summary>Settles every listing whose time is up. True when something changed.</summary>
    public static bool Tick(AhBook b, double now)
    {
        bool any = false;
        foreach (var l in b.Open.Where(x => x.End <= now).ToList()) { Settle(b, l); any = true; }
        return any;
    }

    public static (bool ok, string msg) Run(AhBook b, AhActor a, AhReq r, double now, double hourSec) => r.Op switch
    {
        "list" => List(b, a, r.Sig, r.Pet, r.Start, r.Buyout, r.Hours, now, hourSec),
        "cancel" => Cancel(b, a, r.Id),
        "bid" => Bid(b, a, r.Id, r.Amount, now),
        "buyout" => Buy(b, a, r.Id, now),
        "collect" => Collect(b, a, r.Id, false),
        "hello" => Collect(b, a, 0, true),
        _ => (true, ""),
    };

    /// <summary>Lists an item of the pack (by its Sig) or a pet (by Id). The fee is paid now; the thing leaves the hero until it sells or comes back.</summary>
    public static (bool ok, string msg) List(AhBook b, AhActor a, string? sig, string? petId, int start, int buyout, int hours, double now, double hourSec)
    {
        if (start < 1 || start > MaxPrice) return (false, $"The start price must be 1 to {MaxPrice} gold");
        if (buyout != 0 && (buyout < start || buyout > MaxPrice)) return (false, "The buyout must be at least the start price");
        if (!Durations.Contains(hours)) return (false, "Pick one of the listing lengths");
        if (b.Open.Count(l => l.Seller == a.Key) >= MaxListings) return (false, $"At most {MaxListings} listings at a time");
        if (b.Open.Count >= MaxBook) return (false, "The auction house is full. Try again later.");
        int fee = FeeFor(start); if (a.D.Gold < fee) return (false, $"The listing fee is {fee} gold");
        var l = new Listing { Seller = a.Key, SellerName = a.Name, Start = start, Buyout = buyout, Hours = hours, Fee = fee, End = now + hours * hourSec };
        if (!string.IsNullOrEmpty(petId))
        {
            var p = a.D.Pets.Find(x => x.Id == petId); if (p == null) return (false, "That pet is not with you");
            if (a.D.ActivePet == petId) return (false, "Your companion cannot be listed. Pick another companion first.");
            if (PetTrade.Pack(p).Length > PetTrade.MaxJson) return (false, "Take that pet's gear off first");
            l.Pet = a.TakePet(petId);
        }
        else { l.Item = a.TakeItem(sig ?? ""); if (l.Item == null) return (false, "That item is not in your pack"); }
        a.TakeGold(fee); b.Fees += fee; l.Id = ++b.Seq; b.Open.Add(l);
        return (true, $"Listed {l.Title()} (fee {fee} gold)");
    }
    /// <summary>Takes a listing of your own down while nobody has bid; the thing goes to your collection box, the fee is not returned.</summary>
    public static (bool ok, string msg) Cancel(AhBook b, AhActor a, long id)
    {
        var l = b.Open.Find(x => x.Id == id); if (l == null || l.Seller != a.Key) return (false, "That listing is not yours");
        if (l.Bid > 0) return (false, "It already has a bid and cannot be cancelled");
        b.Open.Remove(l); AddBox(b, a.Key, $"Cancelled: {l.Title()} came back", 0, l.Item, l.Pet);
        return (true, $"Cancelled {l.Title()}. Collect it from the Collect tab.");
    }
    public static (bool ok, string msg) Bid(AhBook b, AhActor a, long id, int amount, double now)
    {
        var l = b.Open.Find(x => x.Id == id); if (l == null || l.End <= now) return (false, "That listing has ended");
        if (l.Seller == a.Key) return (false, "You cannot bid on your own listing");
        if (l.Bidder == a.Key) return (false, "You already hold the highest bid");
        if (l.Buyout > 0 && amount >= l.Buyout) return Buy(b, a, id, now);
        int min = MinBid(l); if (amount < min) return (false, $"The lowest bid is now {min} gold");
        if (amount > MaxPrice) return (false, "That bid is too high");
        if (!a.TakeGold(amount)) return (false, "Not enough gold");
        Refund(b, l); l.Bid = amount; l.Bidder = a.Key; l.BidderName = a.Name;
        return (true, $"You bid {amount} gold on {l.Title()}");
    }
    public static (bool ok, string msg) Buy(AhBook b, AhActor a, long id, double now)
    {
        var l = b.Open.Find(x => x.Id == id); if (l == null || l.End <= now) return (false, "That listing has ended");
        if (l.Seller == a.Key) return (false, "You cannot buy your own listing");
        if (l.Buyout <= 0) return (false, "It has no buyout price");
        if (l.Bidder == a.Key)
        {   // already the highest bidder: pay only the difference
            int more = l.Buyout - l.Bid; if (more < 0 || !a.TakeGold(more)) return (false, "Not enough gold");
        }
        else { if (!a.TakeGold(l.Buyout)) return (false, "Not enough gold"); Refund(b, l); }
        l.Bid = l.Buyout; l.Bidder = a.Key; l.BidderName = a.Name; string t = l.Title(); int price = l.Buyout;
        Settle(b, l);
        return (true, $"Bought {t} for {price} gold. Collect it from the Collect tab.");
    }
    /// <summary>Moves the hero's box into the hero: gold always, pets always, items while the pack has room (the rest waits).</summary>
    public static (bool ok, string msg) Collect(AhBook b, AhActor a, long id, bool quiet)
    {
        int gold = 0, things = 0, left = 0;
        foreach (var e in b.Box.Where(x => x.Owner == a.Key && (id == 0 || x.Id == id)).ToList())
        {
            if (e.Item != null && !a.GiveItem(e.Item)) { left++; continue; }
            if (e.Pet != null) a.GivePet(e.Pet);
            if (e.Item != null || e.Pet != null) things++;
            if (e.Gold > 0) { a.GiveGold(e.Gold); gold += e.Gold; }
            b.Box.Remove(e);
        }
        var parts = new List<string>(); if (gold > 0) parts.Add($"{gold} gold"); if (things > 0) parts.Add(things == 1 ? "1 item" : $"{things} items");
        string msg = parts.Count > 0 ? "Auction house: collected " + string.Join(" and ", parts) : quiet ? "" : left > 0 ? "" : "Nothing to collect";
        if (left > 0) msg += (msg == "" ? "" : ". ") + $"Your pack is full: {left} more wait{(left == 1 ? "s" : "")} at the auction house";
        return (gold + things > 0 || left == 0, msg);
    }

    /// <summary>One page of listings for a player, plus that player's collection box and counts. Views never carry other players' owner keys.</summary>
    public static AhPage Search(AhBook b, AhQuery q, string who, double now)
    {
        IEnumerable<Listing> s = b.Open.Where(l => l.End > now);
        if (q.Scope == "mine") s = s.Where(l => l.Seller == who || l.Bidder == who);   // your listings and the ones you lead
        else
        {
            string text = (q.Text ?? "").Trim(); if (text.Length > 40) text = text[..40];
            if (text != "") s = s.Where(l => l.Title().Contains(text, StringComparison.OrdinalIgnoreCase) || (l.Item?.Wt ?? "").Contains(text, StringComparison.OrdinalIgnoreCase) || (l.Pet != null && l.Pet.Sp().Name.Contains(text, StringComparison.OrdinalIgnoreCase)));
            string slot = q.Slot ?? "";
            if (slot == "pet") s = s.Where(l => l.Pet != null);
            else if (slot == "petgear") s = s.Where(l => l.Item != null && PetDb.IsGear(l.Item.Slot));
            else if (slot != "") s = s.Where(l => l.Item != null && l.Item.Slot == slot);
            if (q.Rar > 0) s = s.Where(l => l.Rar() >= q.Rar);
            if (q.LvMin > 0) s = s.Where(l => l.Lvl() >= q.LvMin);
            if (q.LvMax > 0) s = s.Where(l => l.Lvl() <= q.LvMax);
            if (q.PrMin > 0) s = s.Where(l => l.Price() >= q.PrMin);
            if (q.PrMax > 0) s = s.Where(l => l.Price() <= q.PrMax);
        }
        s = q.Sort switch { "priceDesc" => s.OrderByDescending(l => l.Price()).ThenBy(l => l.Id), "time" => s.OrderBy(l => l.End).ThenBy(l => l.Id), "timeDesc" => s.OrderByDescending(l => l.End).ThenBy(l => l.Id), _ => s.OrderBy(l => l.Price()).ThenBy(l => l.Id) };
        var all = s.ToList(); int size = PageSize, pages = Math.Max(1, (all.Count + size - 1) / size), page = Math.Clamp(q.Page, 0, pages - 1);
        var pg = new AhPage { Total = all.Count, Page = page, Pages = pages, Mine = b.Open.Count(l => l.Seller == who), Bids = b.Open.Count(l => l.Bidder == who) };
        foreach (var l in all.Skip(page * size).Take(size)) pg.L.Add(View(l, who, now));
        foreach (var e in b.Box.Where(x => x.Owner == who).Take(60)) pg.Box.Add(new BoxEntry { Id = e.Id, Owner = Me, Note = e.Note, Gold = e.Gold, Item = e.Item, Pet = e.Pet });
        return pg;
    }
    static string Who(string key, string who) => key == who ? Me : key.StartsWith("npc") ? "npc" : "";
    static Listing View(Listing l, string who, double now) => new()
    {
        Id = l.Id, Seller = Who(l.Seller, who), SellerName = l.SellerName, Item = l.Item, Pet = l.Pet, Start = l.Start, Buyout = l.Buyout, Bid = l.Bid, Fee = l.Fee, Hours = l.Hours,
        Bidder = l.Bidder == "" ? "" : Who(l.Bidder, who), BidderName = l.BidderName, End = l.End, Left = Math.Max(0, l.End - now),
    };

    /// <summary>Applies a delta to a hero: the player's game mirroring the server, and the server bringing an uploaded save up to date.</summary>
    public static void Apply(PlayerStats d, AhDelta x)
    {
        foreach (var sig in x.RmItems) { int i = d.Inv.FindIndex(it => Sig(it) == sig); if (i >= 0) d.Inv.RemoveAt(i); }
        foreach (var id in x.RmPets) { d.Pets.RemoveAll(p => p.Id == id); if (d.ActivePet == id) d.ActivePet = ""; }
        foreach (var it in x.AddItems) { if (d.Inv.Count < PackSize) d.Inv.Add(it); else d.Stash.Add(it); }
        foreach (var p in x.AddPets) if (!d.Pets.Exists(q => q.Id == p.Id)) d.Pets.Add(p);
        if (x.Stacks != null) foreach (var kv in x.Stacks) TradeStacks.Add(d, kv.Key, kv.Value);   // A-30: materials, gems, potions, cubes of a player trade
        d.Gold = (int)Math.Clamp((long)d.Gold + x.Gold, 0, int.MaxValue);
    }

    // ------------------------------------------------------------------ offline: the hero's own book, NPC sellers and buyers
    public static AhBook OfflineBook(PlayerStats d) => d.Ah ??= new AhBook();
    static string NpcName()
    {
        var l = new List<string>(); if (Sec("npc.names") is { ValueKind: JsonValueKind.Array } a) foreach (var x in a.EnumerateArray()) l.Add(x.GetString() ?? "");
        return l.Count == 0 ? "A stranger" : l[(int)(GD.Randi() % (uint)l.Count)];
    }
    static float Rand(float a, float b) => a + GD.Randf() * (b - a);
    /// <summary>How eager an NPC is at price / value r: 1 at a fair price, 0 at maxRatio or above, more for bargains.</summary>
    public static float Eager(float r)
    {
        float max = Math.Max(1.01f, F("npc.buyers", "maxRatio", 1.6f));
        return Mathf.Clamp((max - r) / (max - 1f), 0f, F("npc.buyers", "bargainBoost", 2.5f));
    }
    /// <summary>The chance per game hour that an NPC buys out / bids on this listing right now (dev commands and the Sell tab's hint use them).</summary>
    public static float BuyoutChance(Listing l) => l.Buyout <= 0 ? 0 : Mathf.Clamp(F("npc.buyers", "buyoutChance", .1f) * Eager(l.Buyout / (float)Fair(l)), 0, 1);
    public static float BidChance(Listing l) => Mathf.Clamp(F("npc.buyers", "bidChance", .3f) * Eager(MinBid(l) / (float)Fair(l)), 0, 1);

    /// <summary>One NPC listing: a rolled item (or pet gear), or a pet, priced around its value.</summary>
    static void NpcListing(AhBook b, PlayerStats d, double now, bool pet)
    {
        int spread = (int)F("npc", "levelSpread", 3), lvl = Math.Max(1, d.Level + (int)(GD.Randi() % (uint)(spread * 2 + 1)) - spread);
        string name = NpcName(); var l = new Listing { Seller = "npc:" + name, SellerName = name };
        if (pet)
        {
            var pool = DuelDb.Strings("pool").Where(s => s != "" && PetDb.Species(s).Catchable).ToList(); if (pool.Count == 0) pool.Add("wolf");
            var p = PetDb.New(pool[(int)(GD.Randi() % (uint)pool.Count)], lvl, "iron"); p.Ot = name; l.Pet = p;
        }
        else l.Item = GD.Randf() < F("npc", "petGearChance", .1f) ? PetDb.RollGear(lvl, F("npc", "lootBonus", .35f)) : Items.Roll(lvl, F("npc", "lootBonus", .35f));
        int fair = Fair(l); var durs = Durations;
        l.Start = Math.Clamp(Mathf.RoundToInt(fair * Rand(F("npc", "startMin", .4f), F("npc", "startMax", 1f))), 1, MaxPrice);
        l.Buyout = GD.Randf() < F("npc", "noBuyoutChance", .2f) ? 0 : Math.Clamp(Mathf.RoundToInt(fair * Rand(F("npc", "buyoutMin", 1f), F("npc", "buyoutMax", 1.8f))), l.Start + 1, MaxPrice);
        l.Hours = durs[(int)(GD.Randi() % (uint)durs.Count)]; l.End = now + l.Hours * HourSec(false) * Rand(.25f, 1f);
        l.Id = ++b.Seq; b.Open.Add(l);
    }
    /// <summary>Tops the NPC stock up: all of it the first time, a few an hour afterwards.</summary>
    public static void Restock(AhBook b, PlayerStats d, double now, int most)
    {
        int stock = (int)F("npc", "stock", 22), pets = (int)F("npc", "pets", 3);
        for (int i = 0; i < most; i++)
        {
            int npc = b.Open.Count(l => l.Seller.StartsWith("npc:")); if (npc >= stock) break;
            NpcListing(b, d, now, b.Open.Count(l => l.Seller.StartsWith("npc:") && l.Pet != null) < pets);
        }
    }
    /// <summary>One game hour at the offline house: ended listings settle, NPC buyers look at the hero's listings, a rival may outbid the hero, new stock arrives.</summary>
    public static void NpcHour(AhBook b, PlayerStats d, double t)
    {
        Tick(b, t);
        foreach (var l in b.Open.Where(x => x.Seller == Me).ToList())
        {
            if (l.Buyout > 0 && GD.Randf() < BuyoutChance(l))
            {
                string n = NpcName(); l.Bid = l.Buyout; l.Bidder = "npc:" + n; l.BidderName = n; Settle(b, l); continue;
            }
            if (GD.Randf() < BidChance(l))
            {
                string n = NpcName(); int bid = MinBid(l); l.Bid = bid; l.Bidder = "npc:" + n; l.BidderName = n;
                if (l.Buyout > 0 && bid >= l.Buyout) { l.Bid = l.Buyout; Settle(b, l); }
            }
        }
        float rival = F("npc.buyers", "rivalChance", .1f), rmax = F("npc.buyers", "rivalMaxRatio", 1.1f);
        foreach (var l in b.Open.Where(x => x.Bidder == Me).ToList())
        {
            int next = MinBid(l); if (next / (float)Fair(l) > rmax || GD.Randf() >= rival || (l.Buyout > 0 && next >= l.Buyout)) continue;
            string n = NpcName(); Refund(b, l); l.Bid = next; l.Bidder = "npc:" + n; l.BidderName = n;
        }
        Restock(b, d, t, (int)F("npc", "restockPerHour", 3));
    }
    /// <summary>Brings the offline house up to the hero's clock (PlayerStats.Played): one NpcHour per game hour passed, then anything that just ended.
    /// Tells the hero what happened to their listings and bids. Called every second or so in the world and whenever the window opens.</summary>
    public static void OfflineTick(PlayerStats d, bool notes = true, List<string>? log = null)
    {
        var b = OfflineBook(d); double now = d.Played, hour = HourSec(false);
        if (!b.Stocked) { b.Stocked = true; b.HourAt = now; Restock(b, d, now, 999); }
        if (now < b.HourAt) b.HourAt = now;
        if (now - b.HourAt > hour * 96) b.HourAt = now - hour * 96;   // a very old book: four game days of catching up at most
        while (now - b.HourAt >= hour) { b.HourAt += hour; NpcHour(b, d, b.HourAt); }
        Tick(b, now);
        if (b.News.Count == 0) return;
        log?.AddRange(b.News.Select(n => n.text));
        if (notes) foreach (var (owner, text) in b.News.TakeLast(4)) if (owner == Me) Ui.Note("Auction house: " + text, Ui.Gold);
        b.News.Clear();
    }
}

/// <summary>
/// The player's side of the auction house: one place the window talks to. Offline (and in a friend's hosted world) every request runs at once on the
/// hero's own book; logged in to the online server it is sent there and the reply (page, message, delta) comes back through OnReply.
/// </summary>
public static class AhClient
{
    public static AhQuery Q = new(); public static AhPage Page = new(); public static bool Busy; public static ulong BusyAt;
    public static bool Online => Game.Online && Net.Active && !Net.IsHost;
    public static double HourSec => Auction.HourSec(Online);

    public static void Send(AhReq r)
    {
        r.Q = Q;
        if (Online)
        {
            if (r.Op != "query") Game.Save();   // the server checks against its copy of the save: send the latest first
            Busy = true; BusyAt = Time.GetTicksMsec(); Net.I!.AhSend(r); return;
        }
        var d = Game.D; Auction.OfflineTick(d); var b = Auction.OfflineBook(d); var a = new AhActor(Auction.Me, d);
        var (ok, msg) = Auction.Run(b, a, r, d.Played, Auction.HourSec(false));
        b.News.Clear();
        Page = Auction.Search(b, Q, Auction.Me, d.Played); Q.Page = Page.Page;
        if (msg != "") { Ui.Note(msg, ok ? Ui.Gold : new Color(1, .6f, .5f)); if (ok && r.Op != "query") Sfx.Play("coin"); else if (!ok) Sfx.UiError(); }
        if (r.Op != "query") { Game.Save(); Game.Notify(); }
    }
    /// <summary>Online: the server's answer. Its delta is applied to our hero exactly once (by number), then saved (and so uploaded).</summary>
    public static void OnReply(AhReply re)
    {
        Busy = false;
        if (re.Delta != null) ApplyDelta(re.Delta);
        if (re.Page != null) { Page = re.Page; Q.Page = Page.Page; }
        if (re.Msg != "") { Ui.Note(re.Msg, re.Ok ? Ui.Gold : new Color(1, .6f, .5f)); if (re.Ok && re.Delta != null) Sfx.Play("coin"); else if (!re.Ok) Sfx.UiError(); }
        Game.Notify();
    }
    public static void ApplyDelta(AhDelta x)
    {
        var d = Game.D; if (x.Seq <= d.AhSeq) return;   // already in the save we got from the server
        if (d.ActivePet != "" && x.RmPets.Contains(d.ActivePet)) PetWorld.SetActive("");
        Auction.Apply(d, x); d.AhSeq = x.Seq; Game.Save();
    }
    public static void Refresh() => Send(new AhReq { Op = "query" });
}
