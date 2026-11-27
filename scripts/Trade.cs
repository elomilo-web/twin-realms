using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TwinRealms;

/// <summary>One side of a player trade: pack items, stacks (materials, blocks, food, gems, potions, soul cubes) by key and count, pets and gold.</summary>
public class TradeOffer
{
    public List<Item> Items = new(); public Dictionary<string, int> Stacks = new(); public List<PetState> Pets = new(); public int Gold;
    public bool Empty() => Items.Count == 0 && Stacks.Count == 0 && Pets.Count == 0 && Gold <= 0;
}

/// <summary>
/// The stackable things a hero's save holds, by one key: "mat:&lt;id&gt;" (PlayerStats.Mats: materials, scraps, blocks "blk_*", fish, food),
/// "gem:&lt;gem key&gt;" (loose gems), "pot:hp" / "pot:mp" (potions), "cube:&lt;tier&gt;" (Soul Cubes). Used by trades and by the deltas the
/// server applies to saves (AhDelta.Stacks: signed counts).
/// </summary>
public static class TradeStacks
{
    static (string kind, string id) Split(string key) { int c = key.IndexOf(':'); return c <= 0 ? ("", "") : (key[..c], key[(c + 1)..]); }
    static Dictionary<string, int>? Bag(PlayerStats d, string kind) => kind switch { "mat" => d.Mats ??= new(), "gem" => d.Gems ??= new(), "cube" => d.Cubes ??= new(), _ => null };
    public static int Get(PlayerStats d, string key)
    {
        var (k, id) = Split(key); if (k == "pot") return id == "hp" ? d.HpPot : id == "mp" ? d.MpPot : 0;
        return Bag(d, k)?.GetValueOrDefault(id) ?? 0;
    }
    /// <summary>Adds (or with a negative n takes) a count; a stack never goes below zero.</summary>
    public static void Add(PlayerStats d, string key, int n)
    {
        var (k, id) = Split(key);
        if (k == "pot") { if (id == "hp") d.HpPot = (int)Math.Clamp((long)d.HpPot + n, 0, int.MaxValue); else if (id == "mp") d.MpPot = (int)Math.Clamp((long)d.MpPot + n, 0, int.MaxValue); return; }
        var bag = Bag(d, k); if (bag == null || id == "") return;
        long v = (long)bag.GetValueOrDefault(id) + n; if (v > 0) bag[id] = (int)Math.Min(v, int.MaxValue); else bag.Remove(id);
    }
    static List<string>? _noMats;
    /// <summary>A key a trade may carry: a known kind, and not a material listed in data/social.json trade.untradableMats ("id" or "prefix*").</summary>
    public static bool Valid(string key)
    {
        var (k, id) = Split(key ?? ""); if (id == "" || id.Length > 48) return false;
        switch (k)
        {
            case "pot": return id is "hp" or "mp";
            case "gem": return GemDb.Valid(id);
            case "cube": return true;
            case "mat":
                _noMats ??= SocialCfg.List("trade.untradableMats");
                foreach (var p in _noMats) if (p.EndsWith('*') ? id.StartsWith(p[..^1]) : id == p) return false;
                return true;
        }
        return false;
    }
    public static string Name(string key)
    {
        var (k, id) = Split(key);
        return k switch { "mat" => GatherDb.Mat(id).Name, "gem" => GemDb.Name(id), "pot" => id == "hp" ? "Health potion" : "Mana potion", "cube" => (id.Length > 0 ? char.ToUpper(id[0]) + id[1..] : id) + " Soul Cube", _ => key };
    }
    public static Color Col(string key) { var (k, id) = Split(key); return k == "mat" ? GatherDb.Mat(id).Col : k == "gem" ? GemDb.Col(id) : k == "pot" ? (id == "hp" ? new Color(1, .5f, .5f) : new Color(.5f, .7f, 1f)) : new Color(.8f, .8f, .9f); }
    public static string Tip(string key) { var (k, id) = Split(key); return k == "gem" ? GemDb.StatText(id) : k == "mat" && id.StartsWith("blk_") ? "Building block" : ""; }
    /// <summary>Everything stackable the hero carries that can be traded.</summary>
    public static List<(string key, int n)> Owned(PlayerStats d)
    {
        var l = new List<(string, int)>();
        if (d.HpPot > 0) l.Add(("pot:hp", d.HpPot)); if (d.MpPot > 0) l.Add(("pot:mp", d.MpPot));
        foreach (var kv in (d.Gems ??= new()).OrderBy(kv => kv.Key)) if (kv.Value > 0 && Valid("gem:" + kv.Key)) l.Add(("gem:" + kv.Key, kv.Value));
        foreach (var kv in (d.Cubes ??= new()).OrderBy(kv => kv.Key)) if (kv.Value > 0) l.Add(("cube:" + kv.Key, kv.Value));
        foreach (var kv in (d.Mats ??= new()).OrderBy(kv => GatherDb.Mat(kv.Key).Name)) if (kv.Value > 0 && Valid("mat:" + kv.Key)) l.Add(("mat:" + kv.Key, kv.Value));
        return l;
    }
}

/// <summary>
/// Player trading of items and gold (focus A-30): the player's side. One two-sided window (Ui.OpenTrade, UiTrade.cs): your offer and the
/// partner's; any change of an offer clears both confirmations; when both confirm, a short countdown runs on the host and then the
/// trade goes through.
/// What can be offered: gear in the pack (weapons, armour, pet gear, uniques, set pieces), materials, building blocks, fish and food,
/// loose gems, potions, Soul Cubes, pets (not the companion that is out) and gold. Not tradable: anything you wear (take it off first),
/// home storage and chests (carry it first), spell cards, instruments, the pickaxe and rod, titles; plus what data/social.json
/// "trade" excludes (untradableMats, tradeUniques, tradeWings).
/// - Online server: nothing is held on the players' machines. The server builds each side's offer from its own copy of that hero's
///   save (AuctionServer.TradeView) and, when the countdown ends, swaps everything in one step on both stored saves
///   (AuctionServer.TradeSwap: numbered deltas, written to the book before the saves, reconciled with late uploads). Pack space is checked first.
/// - A friend's hosted world (no accounts): the host relays offers and runs the two phases of the older pet trade: "prepare" moves each
///   side's offer into escrow in its own save (PlayerStats.TradeHold; a hero that is loaded with something still in escrow gets it back),
///   "done" hands each side the other's offer.
/// </summary>
public static class Trade
{
    public static bool Open, Busy, MyOk, TheirOk; public static long Partner; public static string PartnerName = ""; public static int Rev; public static float Count = -1;
    public static TradeOffer Mine = new(), Theirs = new();
    public static float Radius => Math.Max(2, SocialCfg.F("trade.radius", 12));
    public static float CountdownSec => Mathf.Clamp(SocialCfg.F("trade.countdownSec", 3), 0, 30);
    public static int MaxItems => Math.Clamp(SocialCfg.I("trade.maxItems", 12), 1, Auction.PackSize);
    public static int MaxStacks => Math.Clamp(SocialCfg.I("trade.maxStacks", 12), 1, 64);
    public static int MaxPets => Math.Clamp(SocialCfg.I("trade.maxPets", 3), 0, 12);
    public static int MaxGold => Math.Max(0, SocialCfg.I("trade.maxGold", 1000000));
    public static int MaxJson => Math.Max(8, SocialCfg.I("trade.maxJsonKb", 96)) * 1024;
    public static bool CanTrade(Item it) => !(it.Unique && !SocialCfg.B("trade.tradeUniques", true)) && !(it.Wings && !SocialCfg.B("trade.tradeWings", true))
        && !(Uniques.Of(it) != null && !SocialCfg.B("trade.tradeQuestUniques", true));   // quest uniques (Uniques.cs) are ordinary items; a data switch can bind them
    public static bool CanTrade(PlayerStats d, PetState p) => p.Id != "" && p.Id != d.ActivePet && PetTrade.Pack(p).Length <= PetTrade.MaxJson;

    /// <summary>An offer from a message: null when it is unreadable or over the limits; stacks with unknown keys or no count are dropped.</summary>
    public static TradeOffer? Parse(string json)
    {
        if (string.IsNullOrEmpty(json) || json.Length > MaxJson) return null;
        var o = Auction.FromJson<TradeOffer>(json); if (o == null) return null;
        o.Items ??= new(); o.Stacks ??= new(); o.Pets ??= new();
        if (o.Items.Count > MaxItems || o.Stacks.Count > MaxStacks || o.Pets.Count > MaxPets || o.Items.Contains(null!) || o.Pets.Contains(null!)) return null;
        foreach (var p in o.Pets) if (p.Species == "" || PetTrade.Pack(p).Length > PetTrade.MaxJson) return null;
        foreach (var k in o.Stacks.Keys.ToList()) if (o.Stacks[k] <= 0 || !TradeStacks.Valid(k)) o.Stacks.Remove(k);
        o.Gold = Math.Clamp(o.Gold, 0, MaxGold);
        return o;
    }
    public static string Describe(TradeOffer o)
    {
        var l = new List<string>(); foreach (var it in o.Items) l.Add(it.Name);
        foreach (var kv in o.Stacks) l.Add($"{kv.Value}x {TradeStacks.Name(kv.Key)}");
        foreach (var p in o.Pets) l.Add($"{p.Name()} Lv{p.Level}");
        if (o.Gold > 0) l.Add($"{o.Gold} gold");
        return l.Count == 0 ? "nothing" : string.Join(", ", l);
    }
    static void Reset() { Mine = new(); Theirs = new(); MyOk = TheirOk = Busy = false; Rev = 0; Count = -1; }

    // ------------------------------------------------------------------ asking
    public static string Request(long peer)
    {
        if (!Net.Active || Net.I == null || !Net.I.Players.ContainsKey(peer)) return SocialCfg.T("notHere", "That player is not online.");
        if (peer == Net.MyId) return "You cannot trade with yourself.";
        if (Open || PetTrade.Open) return "You are already trading.";
        Net.I.TxRequest(peer); return "Trade request sent to " + Net.I.NameOf(peer);
    }

    // ------------------------------------------------------------------ my offer
    /// <summary>Which pack cells are in my offer (the same item twice in the pack: as many cells as were offered).</summary>
    public static bool[] OfferedMask(List<Item> inv)
    {
        var mask = new bool[inv.Count]; var sigs = Mine.Items.Select(Auction.Sig).ToList();
        for (int i = 0; i < inv.Count && sigs.Count > 0; i++) { int k = sigs.IndexOf(Auction.Sig(inv[i])); if (k >= 0) { mask[i] = true; sigs.RemoveAt(k); } }
        return mask;
    }
    public static void ToggleItem(int invIndex)
    {
        var d = Game.D; if (!Open || Busy || invIndex < 0 || invIndex >= d.Inv.Count) return; var it = d.Inv[invIndex];
        if (OfferedMask(d.Inv)[invIndex]) { string sig = Auction.Sig(it); int k = Mine.Items.FindIndex(x => Auction.Sig(x) == sig); if (k >= 0) Mine.Items.RemoveAt(k); }
        else
        {
            if (!CanTrade(it)) { Ui.Note(it.Name + " cannot be traded", new Color(1, .6f, .5f)); return; }
            if (Mine.Items.Count >= MaxItems) { Ui.Note($"At most {MaxItems} items a trade", new Color(1, .6f, .5f)); return; }
            Mine.Items.Add(it);
        }
        Changed();
    }
    public static void SetStack(string key, int n)
    {
        if (!Open || Busy || !TradeStacks.Valid(key)) return; n = Math.Clamp(n, 0, TradeStacks.Get(Game.D, key));
        if (n == Mine.Stacks.GetValueOrDefault(key)) return;
        if (n == 0) Mine.Stacks.Remove(key);
        else { if (!Mine.Stacks.ContainsKey(key) && Mine.Stacks.Count >= MaxStacks) { Ui.Note($"At most {MaxStacks} kinds of stacked things a trade", new Color(1, .6f, .5f)); return; } Mine.Stacks[key] = n; }
        Changed();
    }
    public static void TogglePet(string id)
    {
        var d = Game.D; if (!Open || Busy) return; var p = d.Pets.Find(x => x.Id == id); if (p == null) return;
        int k = Mine.Pets.FindIndex(x => x.Id == id);
        if (k >= 0) Mine.Pets.RemoveAt(k);
        else
        {
            if (id == d.ActivePet) { Ui.Note("Your companion is out with you. Pick another companion first.", new Color(1, .6f, .5f)); return; }
            if (!CanTrade(d, p)) { Ui.Note("Take that pet's gear off first", new Color(1, .6f, .5f)); return; }
            if (Mine.Pets.Count >= MaxPets) { Ui.Note($"At most {MaxPets} pets a trade", new Color(1, .6f, .5f)); return; }
            Mine.Pets.Add(p);
        }
        Changed();
    }
    public static void SetGold(int g) { if (!Open || Busy) return; g = Math.Clamp(g, 0, Math.Min(MaxGold, Game.D.Gold)); if (g == Mine.Gold) return; Mine.Gold = g; Changed(); }
    static void Changed()
    {
        MyOk = false; TheirOk = false; Count = -1;
        if (Game.Online) Game.Save();   // the online server checks the offer against its copy of our save: send the latest first
        Net.I?.TxOffer(Auction.ToJson(Mine)); Game.Notify();
    }
    /// <summary>Would the pack hold what the partner offers once my own items are gone?</summary>
    public static bool Room() => Game.D.Inv.Count - Mine.Items.Count + Theirs.Items.Count <= Auction.PackSize;
    public static void Confirm()
    {
        if (!Open || Busy) return;
        if (!MyOk && !Room()) { Ui.Note("Your pack has no room for these items", new Color(1, .6f, .5f)); return; }
        MyOk = !MyOk; if (MyOk && Game.Online) Game.Save();   // the server swaps on its copy of the saves
        if (!MyOk) Count = -1;
        Net.I?.TxOk(Rev, MyOk); Game.Notify();
    }
    public static void Cancel(bool tell = true)
    {
        if (!Open) return; if (tell) Net.I?.TxCancel();
        Open = false; Reset(); Game.Notify();
    }

    // ------------------------------------------------------------------ from the host (Net, NetTrade.cs)
    public static void Opened(long partner, string name)
    {
        Reset(); Open = true; Partner = partner; PartnerName = name; Ui.Note($"Trading with {name}", Ui.Gold); Chat.System($"Trading with {name}."); Ui.I?.OpenTrade();
    }
    public static void View(string theirJson, bool myOk, bool theirOk, int rev, float count)
    {
        if (!Open) return; Theirs = Parse(theirJson) ?? new TradeOffer(); MyOk = myOk; TheirOk = theirOk; Rev = rev; Count = count; Game.Notify();
    }
    /// <summary>Online: my offer as the server accepted it (only what its copy of my save holds).</summary>
    public static void MineSet(string json)
    {
        if (!Open) return; var o = Parse(json) ?? new TradeOffer();
        bool less = o.Items.Count < Mine.Items.Count || o.Pets.Count < Mine.Pets.Count || o.Gold < Mine.Gold || Mine.Stacks.Any(kv => o.Stacks.GetValueOrDefault(kv.Key) < kv.Value);
        Mine = o; if (less) Ui.Note("The server did not find everything you offered in your saved hero; it was left out. Offer it again in a moment.", new Color(1, .7f, .55f));
        Game.Notify();
    }
    /// <summary>Friend's world, the host's "prepare": my offer goes into escrow (out of the save, kept in PlayerStats.TradeHold) if all of it is
    /// still there and the pack has room for what comes back.</summary>
    public static bool Prepare(int rev)
    {
        var d = Game.D; if (!Open || rev != Rev || d.TradeHold is { } old && !old.Empty()) return false;
        var inv = new List<Item>(d.Inv); var take = new List<Item>();
        foreach (var it in Mine.Items) { string sig = Auction.Sig(it); int i = inv.FindIndex(x => Auction.Sig(x) == sig); if (i < 0) return false; take.Add(inv[i]); inv.RemoveAt(i); }
        foreach (var kv in Mine.Stacks) if (kv.Value <= 0 || TradeStacks.Get(d, kv.Key) < kv.Value) return false;
        var pets = Mine.Pets.Select(p => d.Pets.Find(x => x.Id == p.Id)).ToList();
        if (pets.Any(p => p == null) || Mine.Gold < 0 || d.Gold < Mine.Gold || inv.Count + Theirs.Items.Count > Auction.PackSize) return false;
        var hold = new TradeOffer { Gold = Mine.Gold };
        foreach (var it in take) { d.Inv.Remove(it); hold.Items.Add(it); }
        foreach (var kv in Mine.Stacks) { TradeStacks.Add(d, kv.Key, -kv.Value); hold.Stacks[kv.Key] = kv.Value; }
        foreach (var p in pets) { d.Pets.Remove(p!); if (d.ActivePet == p!.Id) PetWorld.SetActive(""); hold.Pets.Add(p!); }
        d.Gold -= Mine.Gold; d.TradeHold = hold; Busy = true; Game.Save(); Game.Notify(); return true;
    }
    /// <summary>Friend's world: the swap went through: the partner's offer arrives, the escrow is gone.</summary>
    public static void Done(string json)
    {
        var d = Game.D; var o = Parse(json) ?? new TradeOffer();
        foreach (var it in o.Items) { if (d.Inv.Count < Auction.PackSize) d.Inv.Add(it); else d.Stash.Add(it); }
        foreach (var kv in o.Stacks) TradeStacks.Add(d, kv.Key, kv.Value);
        foreach (var p in o.Pets) { var np = PetTrade.Unpack(PetTrade.Pack(p)); if (np != null) d.Pets.Add(np); }
        d.Gold = (int)Math.Min((long)d.Gold + o.Gold, int.MaxValue); d.TradeHold = null;
        Finished("Trade complete: you received " + Describe(o));
    }
    /// <summary>The trade is done (online: the server's changes to our hero came just before this, Net.R_AhDelta).</summary>
    public static void Finished(string note)
    {
        Ui.Note(note, new Color(.7f, 1f, .75f)); Chat.System(note); Sfx.Play("portal");
        Open = false; Reset(); Game.Save(); Game.Notify(); if (Ui.I?.PanelName == "trade") Ui.I.Close();
    }
    /// <summary>The trade fell through (cancelled, a check failed, the partner left, we lost the connection): anything in escrow comes back.</summary>
    public static void Cancelled(string why)
    {
        RestoreHold();
        if (!Open) return; Open = false; Reset();
        if (why != "") { Ui.Note(why, new Color(1f, .7f, .55f)); Chat.System(why); }
        Game.Notify(); if (Ui.I?.PanelName == "trade") Ui.I.Close();
    }
    /// <summary>Puts an escrowed offer back into the hero (also for a hero loaded with one: the game closed in the middle of a trade).</summary>
    public static void RestoreHold()
    {
        var d = Game.D; var h = d.TradeHold; if (h == null) return; d.TradeHold = null; Busy = false; if (h.Empty()) return;
        foreach (var it in h.Items ?? new()) { if (d.Inv.Count < Auction.PackSize) d.Inv.Add(it); else d.Stash.Add(it); }
        foreach (var kv in h.Stacks ?? new()) TradeStacks.Add(d, kv.Key, kv.Value);
        foreach (var p in h.Pets ?? new()) if (!d.Pets.Exists(x => x.Id == p.Id)) d.Pets.Add(p);
        d.Gold = (int)Math.Min((long)d.Gold + Math.Max(0, h.Gold), int.MaxValue);
        Ui.Note("A trade did not finish: your offer is back with you", new Color(1f, .7f, .55f)); Game.Save(); Game.Notify();
    }
    /// <summary>Each frame from the chat box: the countdown shown in the window, closing the window cancels, an escrow left by a crash comes back.</summary>
    public static void Tick(float dt)
    {
        if (!Open) { if (Ui.InGame && Player.Instance != null && Game.D.TradeHold != null) RestoreHold(); return; }
        if (Count > 0) { int was = Mathf.CeilToInt(Count); Count = Math.Max(0, Count - dt); if (Mathf.CeilToInt(Count) != was) Ui.I?.RefreshTrade(); }
        if (!Busy && Ui.I != null && Ui.I.PanelName != "trade" && Ui.I.PanelName != "ask") Cancel();
    }
}

/// <summary>The server's side of item / gold / pet trades (A-30), next to the pet swap it generalises (NetAuction.cs): the same lock, book and deltas.</summary>
public static partial class AuctionServer
{
    /// <summary>What the server accepts of an offer: only what its own copy of the hero's save holds (items by their data, pets by Id, stacks
    /// and gold capped). The data shown to the partner comes from that save, never from the player's message.</summary>
    static TradeOffer ViewOf(PlayerStats d, TradeOffer ask)
    {
        var v = new TradeOffer(); var pool = new List<Item>(d.Inv);
        foreach (var it in (ask.Items ?? new()).Take(Trade.MaxItems))
        {
            if (it == null) continue; string sig = Auction.Sig(it); int i = pool.FindIndex(x => Auction.Sig(x) == sig);
            if (i < 0 || !Trade.CanTrade(pool[i])) continue; v.Items.Add(pool[i]); pool.RemoveAt(i);
        }
        foreach (var kv in (ask.Stacks ?? new()).OrderBy(kv => kv.Key, StringComparer.Ordinal).Take(Trade.MaxStacks))
        {
            if (!TradeStacks.Valid(kv.Key)) continue; int n = Math.Min(kv.Value, TradeStacks.Get(d, kv.Key)); if (n > 0) v.Stacks[kv.Key] = n;
        }
        foreach (var p in (ask.Pets ?? new()).Take(Trade.MaxPets))
        {
            var mine = p == null ? null : d.Pets.Find(x => x.Id == p.Id);
            if (mine == null || v.Pets.Contains(mine) || !Trade.CanTrade(d, mine)) continue; v.Pets.Add(mine);
        }
        v.Gold = Math.Clamp(ask.Gold, 0, Math.Min(d.Gold, Trade.MaxGold));
        return v;
    }
    /// <summary>The offer of a logged-in account as the server sees it (null: the server has no save of this hero yet).</summary>
    public static TradeOffer? TradeView(string acct, TradeOffer ask)
    {
        lock (Gate) { try { var h = LoadHero(acct); return h == null ? null : ViewOf(h.D, ask); } catch (Exception e) { GD.PrintErr("[TRADE] view failed: " + e.Message); return null; } }
    }
    static void MoveStack(AhActor a, string key, int n)
    {
        TradeStacks.Add(a.D, key, n); a.Delta.Stacks ??= new();
        int v = a.Delta.Stacks.GetValueOrDefault(key) + n; if (v == 0) a.Delta.Stacks.Remove(key); else a.Delta.Stacks[key] = v;
    }
    /// <summary>
    /// Both players confirmed and the countdown ran out: the swap, in one step on the server's copies of both saves. Each side's offer must
    /// still be exactly what the other was shown (shownA / shownB: the JSON of TradeView at offer time); both packs must have room. Both
    /// deltas go into the book before either save is written, so a crash, a disconnect or a save upload that was already on its way
    /// cannot duplicate or lose anything (AuctionServer.Reconcile / Recover).
    /// </summary>
    public static (bool ok, string msg, AhDelta? a, AhDelta? b) TradeSwap(string acctA, TradeOffer askA, string shownA, string acctB, TradeOffer askB, string shownB)
    {
        lock (Gate)
        {
            try
            {
                var book = Book;
                if (acctA == "" || acctB == "" || acctA == acctB) return (false, "You cannot trade with yourself", null, null);
                var ha = LoadHero(acctA); var hb = LoadHero(acctB);
                if (ha == null || hb == null) return (false, "The server has no save of one of the heroes yet", null, null);
                var va = ViewOf(ha.D, askA); var vb = ViewOf(hb.D, askB);
                if (Auction.ToJson(va) != shownA || Auction.ToJson(vb) != shownB) return (false, "An offer changed or is no longer there. Confirm again.", null, null);
                if (va.Empty() && vb.Empty()) return (false, "Nothing is offered", null, null);
                if (ha.D.Inv.Count - va.Items.Count + vb.Items.Count > Auction.PackSize) return (false, $"{ha.D.Name}'s pack has no room for the items", null, null);
                if (hb.D.Inv.Count - vb.Items.Count + va.Items.Count > Auction.PackSize) return (false, $"{hb.D.Name}'s pack has no room for the items", null, null);
                if ((long)ha.D.Gold - va.Gold + vb.Gold > int.MaxValue || (long)hb.D.Gold - vb.Gold + va.Gold > int.MaxValue) return (false, "That is more gold than a purse holds", null, null);
                var a = new AhActor(ha.Owner, ha.D); var b = new AhActor(hb.Owner, hb.D);
                void Take(AhActor x, TradeOffer v)
                {
                    foreach (var it in v.Items) if (x.TakeItem(Auction.Sig(it)) == null) throw new InvalidOperationException("an offered item is gone");
                    foreach (var kv in v.Stacks) { if (TradeStacks.Get(x.D, kv.Key) < kv.Value) throw new InvalidOperationException("an offered stack is gone"); MoveStack(x, kv.Key, -kv.Value); }
                    foreach (var p in v.Pets) if (x.TakePet(p.Id) == null) throw new InvalidOperationException("an offered pet is gone");
                    if (!x.TakeGold(v.Gold)) throw new InvalidOperationException("the offered gold is gone");
                }
                void Give(AhActor x, TradeOffer v)
                {
                    foreach (var it in v.Items) if (!x.GiveItem(it)) throw new InvalidOperationException("a pack is full");
                    foreach (var kv in v.Stacks) MoveStack(x, kv.Key, kv.Value);
                    foreach (var p in v.Pets) x.GivePet(p);
                    x.GiveGold(v.Gold);
                }
                Take(a, va); Take(b, vb); Give(a, vb); Give(b, va);
                string? err = Commit((ha, a), (hb, b));
                if (err != null) return (false, "The server could not store the trade: " + err, null, null);
                return (true, "", a.Delta.Empty() ? null : a.Delta, b.Delta.Empty() ? null : b.Delta);
            }
            catch (Exception e) { GD.PrintErr("[TRADE] swap failed: " + e.Message); _book = null; return (false, "The trade hit a problem. Nothing was changed.", null, null); }
        }
    }
    public static void TradeLog(string line) => SocialLog.Write("trade", line, SocialCfg.I("trade.logMaxKb", 1024), SocialCfg.I("trade.logFiles", 5));
}
