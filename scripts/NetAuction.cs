using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>
/// The online server's side of the auction house (focus F-37) and of server-run pet trades (F-38).
/// The one shared book is a file next to the accounts (TR_DATA/auction.json, written to a temporary file and swapped in, the previous copy kept
/// as .bak). Players only send requests; every check runs here against the server's own copy of the hero's save (Accounts.LoadSave: the slot
/// last played), never against numbers the player's game reports:
/// - list: the item (or pet) must be in that save; it is taken out of it and the fee is paid from its gold;
/// - bid / buyout: the gold is taken from that save (reserved); an outbid bidder's gold goes to their collection box;
/// - ended listings settle on the server's clock whether or not anybody is online; boxes are handed over on "collect" and when a hero enters
///   the world ("hello").
/// Every change to a hero is an AhDelta with a rising number. The server (1) writes it to the book as pending, (2) stores the changed save,
/// (3) sends it to the player's game, which applies the same change and remembers the number (PlayerStats.AhSeq). An uploaded save that does
/// not contain a pending delta yet (it was on its way, or the game never got the message) has it applied before it is stored
/// (Reconcile, called from Accounts.StoreSave), and after a crash between (1) and (2) the next start applies it (Recover). So a listing,
/// bid, payout or trade is never lost or doubled by timing or a disconnect.
/// Limit (docs/ONLINE.md "Known limits"): the save itself is still uploaded by the player's game, so a modified game could forge AhSeq.
/// </summary>
public static class AuctionServer
{
    static AhBook? _book; static string _bookDir = ""; static bool _own;
    static readonly object Gate = new();
    /// <summary>Seconds added to the server clock (the self-test uses it to let listings run out).</summary>
    public static double TimeShift;
    public static double Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 + TimeShift;
    static string BookPath => Path.Combine(Accounts.Dir, "auction.json");
    /// <summary>Drops the book held in memory (the self-test switches data folders).</summary>
    public static void Forget() { lock (Gate) { _book = null; _bookDir = ""; } }

    public static AhBook Book
    {
        get
        {
            lock (Gate)
            {
                if (_book != null && _bookDir == Accounts.Dir) return _book;
                _bookDir = Accounts.Dir; AhBook? b = null; string p = BookPath;
                foreach (var path in new[] { p, p + ".bak" })
                {
                    if (b != null || !File.Exists(path)) continue;
                    try { b = JsonSerializer.Deserialize<AhBook>(File.ReadAllText(path), new JsonSerializerOptions { IncludeFields = true }); }
                    catch (Exception e) { GD.PrintErr($"[AUCTION] {Path.GetFileName(path)} unreadable: {e.Message}"); try { File.Copy(path, path + ".bad", true); } catch { } }
                }
                // delta numbers start at the clock so they keep rising even if the book file is ever lost
                b ??= new AhBook { Dseq = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
                b.Open ??= new(); b.Box ??= new(); b.Pending ??= new(); b.News ??= new();
                _book = b; Recover();
                return b;
            }
        }
    }
    static void SaveBook()
    {
        if (_book == null) return;
        try
        {
            string p = BookPath, tmp = p + ".tmp"; File.WriteAllText(tmp, Auction.ToJson(_book));
            if (File.Exists(p)) File.Copy(p, p + ".bak", true);
            File.Move(tmp, p, true);
        }
        catch (Exception e) { GD.PrintErr("[AUCTION] could not write the book: " + e.Message); }
    }

    class Hero { public Game.SaveFile File = new(); public PlayerStats D = new(); public string Acct = "", Owner = ""; }
    /// <summary>The hero an account is playing, from the server's copy of its save (the slot it last saved).</summary>
    static Hero? LoadHero(string acct)
    {
        string json = Accounts.LoadSave(acct); if (json == "") return null;
        var f = Auction.FromJson<Game.SaveFile>(json); if (f?.Slots == null || f.Slots.Length != Game.MaxSlots) return null;
        int slot = Math.Clamp(f.Last, 0, Game.MaxSlots - 1); var d = f.Slots[slot]; if (d == null) return null;
        d.Inv ??= new(); d.Pets ??= new(); d.Stash ??= new();
        return new Hero { File = f, D = d, Acct = acct, Owner = acct + ":" + slot };
    }
    static void Store(string acct, string json)
    {
        _own = true; string? err;
        try { err = Accounts.StoreSave(acct, json); } finally { _own = false; }
        if (err != null) GD.PrintErr($"[AUCTION] could not store a save: {err} (the change stays pending and is applied to the next upload)");
    }
    /// <summary>Makes the changes of one request (or of both sides of a trade) permanent: pending deltas into the book first, then the saves.</summary>
    static string? Commit(params (Hero h, AhActor a)[] parts)
    {
        var b = _book!; var jsons = new List<(string acct, string json)>();
        foreach (var (h, a) in parts)
        {
            if (a.Delta.Empty()) continue;
            a.Delta.Seq = ++b.Dseq; h.D.AhSeq = a.Delta.Seq;
            if (!b.Pending.TryGetValue(a.Key, out var l)) b.Pending[a.Key] = l = new();
            l.Add(a.Delta);
            string j = Auction.ToJson(h.File);
            if (j.Length > OnlineCfg.MaxSaveKb * 1024) { _book = null; return "the save would be too large"; }   // nothing was written: the book is read again from its file
            jsons.Add((h.Acct, j));
        }
        SaveBook();
        foreach (var (acct, j) in jsons) Store(acct, j);
        return null;
    }

    /// <summary>One request of a logged-in account. The same engine functions as offline (Auction.Run), on the server's book and the server's copy of the save.</summary>
    public static AhReply Handle(string acct, AhReq r)
    {
        lock (Gate)
        {
            var re = new AhReply { Op = r.Op ?? "", Ok = true };
            try
            {
                var b = Book; double now = Now; bool dirty = Auction.Tick(b, now);
                r.Q ??= new AhQuery(); r.Op ??= "query";
                var h = LoadHero(acct); string owner = h?.Owner ?? acct + ":-";
                if (r.Op != "query")
                {
                    if (h == null) { re.Ok = false; re.Msg = "The server has no save of this hero yet. Try again in a moment."; }
                    else
                    {
                        var a = new AhActor(owner, h.D); long mark = b.Seq; int was = b.Open.Count + b.Box.Count;
                        var (ok, msg) = Auction.Run(b, a, r, now, Auction.HourSec(true));
                        re.Ok = ok; re.Msg = msg;
                        if (!a.Delta.Empty())
                        {
                            string? err = Commit((h, a)); dirty = false;
                            if (err != null) { re.Ok = false; re.Msg = "The server could not store the change: " + err; b = Book; }
                            else re.Delta = a.Delta;
                        }
                        else if (b.Seq != mark || b.Open.Count + b.Box.Count != was) dirty = true;   // e.g. a cancel: the book changed, the hero did not
                    }
                }
                if (dirty) SaveBook();
                re.Page = Auction.Search(b, r.Q, owner, now);
            }
            catch (Exception e) { GD.PrintErr("[AUCTION] request failed: " + e); _book = null; re.Ok = false; re.Msg = "The auction house hit a problem. Nothing was changed."; }
            return re;
        }
    }
    /// <summary>The server's clock tick: listings that ran out are settled even with nobody online.</summary>
    public static void TickNow() { lock (Gate) { try { if (Auction.Tick(Book, Now)) SaveBook(); } catch (Exception e) { GD.PrintErr("[AUCTION] tick failed: " + e.Message); } } }
    /// <summary>What happened to whom since the last call (owner keys "account:slot"), for notes to players who are online.</summary>
    public static List<(string owner, string text)> TakeNews() { lock (Gate) { var l = new List<(string, string)>(); if (_book != null) { l.AddRange(_book.News); _book.News.Clear(); } return l; } }

    /// <summary>
    /// Called for every save a player's game uploads (Accounts.StoreSave): changes the server made that this save does not contain yet are applied
    /// to it, and pending deltas the save already contains are forgotten. Returns the save to store.
    /// </summary>
    public static string Reconcile(string acct, string json)
    {
        lock (Gate)
        {
            if (_own) return json;
            try
            {
                if (_book == null && !File.Exists(BookPath)) return json;
                var b = Book; string pre = acct + ":";
                if (!b.Pending.Keys.Any(k => k.StartsWith(pre))) return json;
                var f = Auction.FromJson<Game.SaveFile>(json); if (f?.Slots == null) return json;
                bool changed = false, dirty = false;
                for (int i = 0; i < f.Slots.Length && i < Game.MaxSlots; i++)
                {
                    string owner = pre + i; if (!b.Pending.TryGetValue(owner, out var list)) continue;
                    var d = f.Slots[i];
                    if (d == null) { b.Pending.Remove(owner); dirty = true; continue; }   // the hero was deleted
                    long had = d.AhSeq; if (list.RemoveAll(x => x.Seq <= had) > 0) dirty = true;
                    foreach (var x in list.OrderBy(x => x.Seq)) { Auction.Apply(d, x); d.AhSeq = x.Seq; changed = true; }
                    if (list.Count == 0) b.Pending.Remove(owner);
                }
                if (dirty) SaveBook();
                return changed ? Auction.ToJson(f) : json;
            }
            catch (Exception e) { GD.PrintErr("[AUCTION] reconcile failed: " + e.Message); return json; }
        }
    }
    /// <summary>After a restart: pending deltas whose save was never written (a crash between the book and the save) are applied now.</summary>
    static void Recover()
    {
        var b = _book!; bool dirty = false;
        foreach (var owner in b.Pending.Keys.ToList())
        {
            int c = owner.LastIndexOf(':');
            if (c <= 0 || !int.TryParse(owner[(c + 1)..], out int slot)) { b.Pending.Remove(owner); dirty = true; continue; }
            string acct = owner[..c]; var f = Auction.FromJson<Game.SaveFile>(Accounts.LoadSave(acct));
            var d = f?.Slots != null && slot >= 0 && slot < f.Slots.Length ? f.Slots[slot] : null;
            if (d == null) { b.Pending.Remove(owner); dirty = true; continue; }
            bool ch = false; foreach (var x in b.Pending[owner].OrderBy(x => x.Seq)) if (x.Seq > d.AhSeq) { Auction.Apply(d, x); d.AhSeq = x.Seq; ch = true; }
            if (ch) { Store(acct, Auction.ToJson(f!)); GD.Print("[AUCTION] applied a pending change left by a restart"); }
        }
        if (dirty) SaveBook();
    }

    // ------------------------------------------------------------------ pet trades run by the server (F-38)
    /// <summary>
    /// A trade offer as the server knows it: the named pets must be in the account's stored save (their data is taken from there, not from the
    /// player's message) and the gold is capped to what that save holds. Null when a pet is not there.
    /// </summary>
    public static (string json, int gold, List<string> ids)? Offer(string acct, List<string> ids, int gold)
    {
        lock (Gate)
        {
            var h = LoadHero(acct); if (h == null) return null;
            var l = new List<string>(); var keep = new List<string>();
            foreach (var id in ids.Distinct().Take(PetTrade.MaxPets))
            {
                var p = h.D.Pets.Find(x => x.Id == id); if (p == null) return null;
                string pj = PetTrade.Pack(p); if (pj.Length > PetTrade.MaxJson) return null;
                l.Add(pj); keep.Add(id);
            }
            return (JsonSerializer.Serialize(l), Math.Clamp(gold, 0, Math.Min(h.D.Gold, PetTrade.MaxGold)), keep);
        }
    }
    /// <summary>
    /// Both players confirmed: the swap, in one step on the server's copies of both saves. Each side's pets and gold must still be exactly what the
    /// other was shown (shownA / shownB: the offers as Offer returned them). Pets move whole (level, xp, nature, skills, gear); only their Id is
    /// new. Both deltas are written to the book before either save, so a crash or a disconnect cannot leave a pet on both sides or on neither.
    /// </summary>
    public static (bool ok, string msg, AhDelta? a, AhDelta? b) Swap(string acctA, List<string> idsA, int goldA, string shownA, string acctB, List<string> idsB, int goldB, string shownB)
    {
        lock (Gate)
        {
            try
            {
                var book = Book;
                if (acctA == acctB) return (false, "You cannot trade with yourself", null, null);
                var ha = LoadHero(acctA); var hb = LoadHero(acctB);
                if (ha == null || hb == null) return (false, "The server has no save of one of the heroes yet", null, null);
                var oa = Offer(acctA, idsA, goldA); var ob = Offer(acctB, idsB, goldB);
                if (oa == null || ob == null || oa.Value.json != shownA || ob.Value.json != shownB || oa.Value.ids.Count != idsA.Count || ob.Value.ids.Count != idsB.Count)
                    return (false, "An offered pet changed or is no longer there. Confirm again.", null, null);
                if (goldA < 0 || goldB < 0 || ha.D.Gold < goldA || hb.D.Gold < goldB) return (false, "The offered gold is no longer there. Confirm again.", null, null);
                var a = new AhActor(ha.Owner, ha.D); var b = new AhActor(hb.Owner, hb.D);
                var pa = idsA.Select(id => a.TakePet(id)!).ToList(); var pb = idsB.Select(id => b.TakePet(id)!).ToList();
                a.TakeGold(goldA); b.TakeGold(goldB);
                foreach (var p in pb) a.GivePet(p); foreach (var p in pa) b.GivePet(p);
                a.GiveGold(goldB); b.GiveGold(goldA);
                string? err = Commit((ha, a), (hb, b));
                if (err != null) return (false, "The server could not store the trade: " + err, null, null);
                return (true, "", a.Delta.Empty() ? null : a.Delta, b.Delta.Empty() ? null : b.Delta);
            }
            catch (Exception e) { GD.PrintErr("[AUCTION] trade failed: " + e); _book = null; return (false, "The trade hit a problem. Nothing was changed.", null, null); }
        }
    }
}

/// <summary>Auction house messages. Client to server: R_Ah(request JSON). Server to client: R_AhRe(reply JSON: ok, message, delta, page), R_AhDelta (a change from a trade).</summary>
public partial class Net
{
    readonly Dictionary<long, (ulong t, int n)> _ahRate = new();

    /// <summary>Client: one request to the online auction house (AhReq: op query|list|cancel|bid|buyout|collect|hello plus the current search).</summary>
    public void AhSend(AhReq r) { if (!Active || _server) return; RpcId(1, MethodName.R_Ah, ToJson(r)); }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    void R_Ah(string json)
    {
        if (!Dedicated) return; long id = Sender;
        if (!_acct.TryGetValue(id, out var key) || !Players.ContainsKey(id) || json.Length > 8000) return;
        // at most 8 requests a second from one player
        ulong now = Time.GetTicksMsec(); var rt = _ahRate.GetValueOrDefault(id); if (now - rt.t > 1000) rt = (now, 0);
        rt.n++; _ahRate[id] = rt; if (rt.n > 8) return;
        if (_ahRate.Count > 512) foreach (var k in new List<long>(_ahRate.Keys)) if (!Players.ContainsKey(k)) _ahRate.Remove(k);
        var r = FromJson<AhReq>(json); if (r == null) return;
        var re = AuctionServer.Handle(key, r);
        RpcId(id, MethodName.R_AhRe, ToJson(re)); Count("auction");
        if (r.Op is "list" or "bid" or "buyout" && re.Ok) GD.Print($"[SERVER] auction {r.Op} by {NameOf(id)}: {re.Msg}");
        AhNews(key);
    }
    [Rpc(MultiplayerApi.RpcMode.Authority)]
    void R_AhRe(string json) { var re = FromJson<AhReply>(json); if (re != null) AhClient.OnReply(re); }
    [Rpc(MultiplayerApi.RpcMode.Authority)]
    void R_AhDelta(string json) { var x = FromJson<AhDelta>(json); if (x == null) return; AhClient.ApplyDelta(x); Game.Notify(); }

    /// <summary>Server: tells players who are online what just happened to their listings and bids (not the one who asked: they got the reply).</summary>
    void AhNews(string skipAcct)
    {
        foreach (var (owner, text) in AuctionServer.TakeNews())
        {
            int c = owner.LastIndexOf(':'); string acct = c > 0 ? owner[..c] : owner; if (acct == skipAcct) continue;
            foreach (var kv in _acct) if (kv.Value == acct && Players.ContainsKey(kv.Key)) RpcId(kv.Key, MethodName.R_Note, "Auction house: " + text);
        }
    }
    /// <summary>Server: every few seconds, listings that ran out are settled and their owners told.</summary>
    public void AhServerTick() { if (!Dedicated || !_server) return; AuctionServer.TickNow(); AhNews(""); }
    // (AcctOf(peer), the account key of a peer, is in NetAdmin.cs)
}

/// <summary>The auction house's clock (a child of Main): offline it plays the NPC hours, on the server it settles ended listings, and online it asks
/// the server for the hero's collection box once on entering the world. Also registers the dev commands.</summary>
public partial class AuctionNode : Node
{
    float _t; bool _hello;
    public override void _Ready() => AuctionDev.Register();
    public override void _Process(double delta)
    {
        _t -= (float)delta; if (_t > 0) return;
        if (Net.Dedicated) { _t = 10f; Net.I?.AhServerTick(); return; }
        _t = 1f;
        if (!Ui.InGame || Player.Instance == null) { _hello = false; return; }
        if (AhClient.Online)
        {
            if (!_hello && Net.OnlineState == "in") { _hello = true; AhClient.Send(new AhReq { Op = "hello" }); }
            if (AhClient.Busy && Time.GetTicksMsec() - AhClient.BusyAt > 8000) { AhClient.Busy = false; Game.Notify(); }
            return;
        }
        _hello = false;
        if (!Game.Online && Game.D.Ah != null) Auction.OfflineTick(Game.D);   // the book exists once the hero has visited an auctioneer
    }
}
