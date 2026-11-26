using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>
/// Pet trading. One two-sided window (Ui.OpenPetTrade): your offer (pets, gold) on the left, the partner's on the right; any change of
/// an offer clears both confirmations; when both confirm the trade goes through.
/// - Multiplayer (LAN host or the online server, which is the host there): the host relays offers and runs the swap in two phases:
///   "prepare" asks both sides to put their offered pets / gold into escrow (PlayerStats.TradeEscrow: taken out of the save first, so a pet is
///   never in two places), "done" hands each side the other's offer. A failed check or a cancel returns the escrow. Offers are size-limited
///   (data/pets.json duel.trade). Pets keep stats, level and their original trainer (Ot); the receiver gives them a new Id.
/// - Alone: Odile the Pet Trader (by the arena) takes one pet for a random pool pet of the same rarity and about the same level, for a fee.
/// Auction house hook (F-37, later): Pack(pet) / Unpack(json, from) are the serialisation a listing would use.
/// </summary>
public static class PetTrade
{
    public static bool Open; public static long Partner; public static string PartnerName = ""; public static bool Npc;
    public static readonly List<string> MyIds = new(); public static int MyGold;
    public static readonly List<PetState> Theirs = new(); public static int TheirGold;
    public static bool MyOk, TheirOk; public static int Rev; public static bool Busy;   // Busy: the swap is under way (escrow taken)
    static readonly Dictionary<string, PetState> _npcOffers = new();                       // Odile's offer per pet of yours (fixed for the visit)

    static JsonElement T => DuelDb.Sub("trade");
    public static int MaxPets => (int)JsonUtil.F(T, "maxPets", 3);
    public static int MaxJson => (int)JsonUtil.F(T, "maxPetJson", 4096);
    public static int MaxGold => (int)JsonUtil.F(T, "maxGold", 1000000);

    public static string NewId() => $"{(long)Time.GetUnixTimeFromSystem():x}{GD.Randi() % 0xffff:x4}";
    /// <summary>A pet as sent in a trade (or, later, an auction listing).</summary>
    public static string Pack(PetState p) => p.ToJson();
    /// <summary>A traded pet arriving: a new Id, its original trainer kept; null if the data is bad or too big.</summary>
    public static PetState? Unpack(string json)
    {
        if (json.Length > MaxJson) return null; var p = PetState.FromJson(json); if (p == null || p.Species == "") return null;
        p.Id = NewId(); if (p.Ot == "") p.Ot = "unknown"; p.KoT = 0; p.HpFrac = 1; p.Level = Math.Clamp(p.Level, 1, (int)PetDb.R("maxLevel", 60));
        if (p.Skills.Count > 4) p.Skills = p.Skills.Take(4).ToList();
        return p;
    }
    public static string OfferJson() { var d = Game.D; var l = MyIds.Select(id => d.Pets.Find(p => p.Id == id)).Where(p => p != null).Select(p => Pack(p!)).ToList(); return JsonSerializer.Serialize(l); }
    public static List<PetState>? ParseOffer(string json)
    {
        if (json.Length > MaxPets * (MaxJson + 8) + 16) return null;
        try { var l = JsonSerializer.Deserialize<List<string>>(json); if (l == null || l.Count > MaxPets) return null; var o = new List<PetState>(); foreach (var s in l) { if (s.Length > MaxJson) return null; var p = PetState.FromJson(s); if (p == null) return null; o.Add(p); } return o; }
        catch { return null; }
    }

    static void Reset() { MyIds.Clear(); MyGold = 0; Theirs.Clear(); TheirGold = 0; MyOk = false; TheirOk = false; Rev = 0; Busy = false; }

    // ------------------------------------------------------------------ alone: Odile, the Pet Trader
    public static string TraderName => JsonUtil.S(DuelDb.Sub("trader"), "name", "Odile");
    public static void StartNpc() { Reset(); _npcOffers.Clear(); Open = true; Npc = true; Partner = 0; PartnerName = TraderName; }
    public static int Fee(PetState p)
    {
        var t = DuelDb.Sub("trader"); float rf = t.TryGetProperty("rarityFee", out var r) ? JsonUtil.F(r, p.Sp().Rarity, 1) : 1;
        return Mathf.RoundToInt((JsonUtil.F(t, "feeBase", 40) + JsonUtil.F(t, "feePerLevel", 8) * p.Level) * rf);
    }
    static PetState NpcOffer(PetState mine)
    {
        if (_npcOffers.TryGetValue(mine.Id, out var o)) return o;
        var pool = DuelDb.Strings("pool").Where(s => s != mine.Species).ToList(); string rar = mine.Sp().Rarity;
        var same = pool.Where(s => PetDb.Species(s).Rarity == rar && PetDb.Species(s).Catchable).ToList(); if (same.Count == 0) same = pool.Count > 0 ? pool : new List<string> { "wolf" };
        int spread = (int)JsonUtil.F(DuelDb.Sub("trader"), "levelSpread", 1);
        var p = PetDb.New(same[(int)(GD.Randi() % (uint)same.Count)], mine.Level + (int)(GD.Randi() % (uint)(spread * 2 + 1)) - spread, mine.Cube);
        p.Ot = TraderName; p.Bond = 0; _npcOffers[mine.Id] = p; return p;
    }
    /// <summary>The fake partner used by the capture tool: Odile's window with a pet already offered.</summary>
    public static void DevStart() { StartNpc(); var d = Game.D; if (d.Pets.Count > 0) Toggle(d.Pets[0].Id); }

    // ------------------------------------------------------------------ both kinds
    public static void Toggle(string id)
    {
        if (!Open || Busy) return; var d = Game.D; if (d.Pets.Find(p => p.Id == id) == null) return;
        if (MyIds.Contains(id)) MyIds.Remove(id);
        else
        {
            if (Npc) MyIds.Clear();
            if (MyIds.Count >= (Npc ? 1 : MaxPets)) { Ui.Note($"At most {MaxPets} pets a trade", new Color(1, .6f, .5f)); return; }
            if (OfferJson().Length > MaxPets * MaxJson) return;
            MyIds.Add(id);
        }
        Changed();
    }
    public static void SetGold(int g) { if (!Open || Busy || Npc) return; MyGold = Math.Clamp(g, 0, Math.Min(MaxGold, Game.D.Gold)); Changed(); }
    static void Changed()
    {
        MyOk = false; TheirOk = false;
        if (Npc)
        {
            Theirs.Clear(); TheirGold = 0;
            var mine = MyIds.Count > 0 ? Game.D.Pets.Find(p => p.Id == MyIds[0]) : null;
            if (mine != null) { Theirs.Add(NpcOffer(mine)); TheirGold = -Fee(mine); TheirOk = true; }
        }
        else Net.I?.TradeOffer(OfferJson(), MyGold);
        Game.Notify();
    }
    public static void Confirm()
    {
        if (!Open || Busy) return;
        if (Npc)
        {
            var d = Game.D; var mine = MyIds.Count > 0 ? d.Pets.Find(p => p.Id == MyIds[0]) : null; if (mine == null || Theirs.Count == 0) return;
            int fee = -TheirGold; if (d.Gold < fee) { Ui.Note($"Odile wants {fee} gold for the swap", new Color(1, .6f, .5f)); return; }
            d.Gold -= fee; foreach (var g in mine.Gear.Values) if (g != null) { if (d.Inv.Count < 28) d.Inv.Add(g); else d.Stash.Add(g); }   // its gear comes back to you
            mine.Gear.Clear();
            d.Pets.Remove(mine); if (d.ActivePet == mine.Id) PetWorld.SetActive("");
            var got = Theirs[0]; got.Id = NewId(); d.Pets.Add(got);
            Ui.Note($"{mine.Name()} goes with {TraderName}. {got.Name()} (Lv {got.Level}) is yours.", new Color(.7f, 1f, .75f)); Sfx.Play("portal");
            _npcOffers.Clear(); MyIds.Clear(); Theirs.Clear(); TheirGold = 0; Game.Save(); Game.Notify(); return;
        }
        MyOk = !MyOk; Net.I?.TradeOk(Rev, MyOk); Game.Notify();
    }
    public static void Cancel(bool tell = true)
    {
        if (!Open) return; if (tell && !Npc) Net.I?.TradeCancel();
        Open = false; Npc = false; Reset(); Game.Notify();
    }

    // ------------------------------------------------------------------ multiplayer client side (called from Net)
    public static void Opened(long partner, string name) { Reset(); Open = true; Npc = false; Partner = partner; PartnerName = name; Ui.Note($"Trading with {name}", Ui.Gold); Ui.I?.OpenPetTrade(); }
    public static void View(string theirJson, int theirGold, bool myOk, bool theirOk, int rev)
    {
        if (!Open || Npc) return; Theirs.Clear(); var l = ParseOffer(theirJson); if (l != null) Theirs.AddRange(l);
        TheirGold = Math.Max(0, theirGold); MyOk = myOk; TheirOk = theirOk; Rev = rev; Game.Notify();
    }
    /// <summary>The host's "prepare": our offer goes into escrow (out of the pet list, gold taken) if we still have all of it.</summary>
    public static bool Prepare(int rev)
    {
        var d = Game.D; if (!Open || rev != Rev) return false;
        var pets = MyIds.Select(id => d.Pets.Find(p => p.Id == id)).ToList();
        if (pets.Any(p => p == null) || d.Gold < MyGold) return false;
        foreach (var p in pets) { d.Pets.Remove(p!); if (d.ActivePet == p!.Id) PetWorld.SetActive(""); d.TradeEscrow.Add(p!); }
        d.Gold -= MyGold; d.TradeEscrowGold = MyGold; Busy = true; Game.Save(); return true;
    }
    public static void Done(string json, int gold)
    {
        var d = Game.D; var got = new List<string>();
        var l = ParseOffer(json) ?? new List<PetState>();
        foreach (var p in l) { var np = Unpack(p.ToJson()); if (np == null) continue; d.Pets.Add(np); got.Add($"{np.Name()} Lv{np.Level}"); }
        d.Gold += Math.Clamp(gold, 0, MaxGold);
        d.TradeEscrow.Clear(); d.TradeEscrowGold = 0;
        Ui.Note("Trade complete" + (got.Count > 0 ? ": " + string.Join(", ", got) : "") + (gold > 0 ? $" and {gold} gold" : ""), new Color(.7f, 1f, .75f)); Sfx.Play("portal");
        Open = false; Reset(); Game.Save(); Game.Notify(); if (Ui.I?.PanelName == "pettrade") Ui.I.Close();
    }
    /// <summary>The trade fell through (cancelled, a check failed, the partner left, we lost the connection): escrowed pets and gold come back.</summary>
    public static void Cancelled(string why)
    {
        RestoreEscrow();
        if (Open) { Open = false; Reset(); if (why != "") Ui.Note(why, new Color(1f, .7f, .55f)); Game.Notify(); if (Ui.I?.PanelName == "pettrade") Ui.I.Close(); }
    }
    public static void RestoreEscrow()
    {
        var d = Game.D; if (d.TradeEscrow.Count == 0 && d.TradeEscrowGold == 0) return;
        foreach (var p in d.TradeEscrow) if (!d.Pets.Exists(x => x.Id == p.Id)) d.Pets.Add(p);
        d.Gold += d.TradeEscrowGold; d.TradeEscrow.Clear(); d.TradeEscrowGold = 0; Busy = false; Game.Save(); Game.Notify();
    }
}

/// <summary>Host side of pet trading: sessions, relaying offers, the two-phase swap. Runs on a LAN host and on the online server alike.</summary>
public partial class Net
{
    class TradeSess { public long A, B; public string OfferA = "[]", OfferB = "[]"; public int GoldA, GoldB, Rev; public bool OkA, OkB, Prep, ReadyA, ReadyB; }
    readonly Dictionary<long, TradeSess> _trades = new();

    void TSend(long peer, StringName m, params Variant[] a) { if (peer == MyId && !Dedicated) Call(m, a); else if (Players.ContainsKey(peer)) RpcId(peer, m, a); }
    void ToHost(StringName m, params Variant[] a) { if (_server) Call(m, a); else RpcId(1, m, a); }

    // ---- client API
    public void TradeRequest(long target) => ToHost(MethodName.R_TradeReq, target);
    public void TradeOffer(string json, int gold) => ToHost(MethodName.R_TradeOffer, json, gold);
    public void TradeOk(int rev, bool ok) => ToHost(MethodName.R_TradeOk, rev, ok);
    public void TradeCancel() => ToHost(MethodName.R_TradeCancel);

    // ---- request and answer
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    void R_TradeReq(long target)
    {
        if (!_server) return; long from = Sender;
        if (from == target || !Players.ContainsKey(target) || !Players.ContainsKey(from)) return;
        if (_trades.ContainsKey(from) || _trades.ContainsKey(target)) { Tell(from, NameOf(target) + " is already trading"); return; }
        TSend(target, MethodName.R_TradeAsk, from, NameOf(from));
    }
    [Rpc(MultiplayerApi.RpcMode.Authority)]
    void R_TradeAsk(long from, string name)
    {
        if (AutoAccept) { ToHost(MethodName.R_TradeAnswer, from, true); return; }
        Ui.I?.Ask($"{name} wants to trade pets with you. Both of you see the offers and must confirm.", "Trade", "No thanks", ok => ToHost(MethodName.R_TradeAnswer, from, ok));
    }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    void R_TradeAnswer(long from, bool ok)
    {
        if (!_server) return; long to = Sender;
        if (!ok) { Tell(from, NameOf(to) + " declined the trade"); return; }
        if (_trades.ContainsKey(from) || _trades.ContainsKey(to) || !Players.ContainsKey(from)) return;
        var s = new TradeSess { A = from, B = to }; _trades[from] = s; _trades[to] = s;
        TSend(from, MethodName.R_TradeOpen, to, NameOf(to)); TSend(to, MethodName.R_TradeOpen, from, NameOf(from)); Count("trades");
    }
    [Rpc(MultiplayerApi.RpcMode.Authority)] void R_TradeOpen(long partner, string name) => PetTrade.Opened(partner, name);

    // ---- offers and confirmations: any change clears both confirmations
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    void R_TradeOffer(string json, int gold)
    {
        if (!_server || !_trades.TryGetValue(Sender, out var s) || s.Prep) return; long who = Sender;
        if (PetTrade.ParseOffer(json) == null) { Tell(who, "That offer is too large"); return; }
        gold = Math.Clamp(gold, 0, PetTrade.MaxGold);
        if (who == s.A) { s.OfferA = json; s.GoldA = gold; } else { s.OfferB = json; s.GoldB = gold; }
        s.OkA = s.OkB = false; s.Rev++; TradeViews(s);
    }
    void TradeViews(TradeSess s)
    {
        TSend(s.A, MethodName.R_TradeView, s.OfferB, s.GoldB, s.OkA, s.OkB, s.Rev);
        TSend(s.B, MethodName.R_TradeView, s.OfferA, s.GoldA, s.OkB, s.OkA, s.Rev);
    }
    [Rpc(MultiplayerApi.RpcMode.Authority)] void R_TradeView(string theirs, int gold, bool myOk, bool theirOk, int rev) => PetTrade.View(theirs, gold, myOk, theirOk, rev);
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    void R_TradeOk(int rev, bool ok)
    {
        if (!_server || !_trades.TryGetValue(Sender, out var s) || s.Prep) return;
        if (rev != s.Rev) { TradeViews(s); return; }   // an offer changed meanwhile: confirm again on what is shown now
        if (Sender == s.A) s.OkA = ok; else s.OkB = ok;
        if (s.OkA && s.OkB && (s.OfferA != "[]" || s.OfferB != "[]" || s.GoldA + s.GoldB > 0))
        {
            s.Prep = true; TSend(s.A, MethodName.R_TradePrep, s.Rev); TSend(s.B, MethodName.R_TradePrep, s.Rev);
        }
        else TradeViews(s);
    }
    // ---- the swap: both put their offer in escrow, then each gets the other's
    [Rpc(MultiplayerApi.RpcMode.Authority)] void R_TradePrep(int rev) => ToHost(MethodName.R_TradeReady, rev, PetTrade.Prepare(rev));
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    void R_TradeReady(int rev, bool ok)
    {
        if (!_server || !_trades.TryGetValue(Sender, out var s) || !s.Prep || rev != s.Rev) return;
        if (!ok) { EndTrade(s, "The trade fell through: an offer was no longer there"); return; }
        if (Sender == s.A) s.ReadyA = true; else s.ReadyB = true;
        if (!(s.ReadyA && s.ReadyB)) return;
        _trades.Remove(s.A); _trades.Remove(s.B);
        TSend(s.A, MethodName.R_TradeDone, s.OfferB, s.GoldB); TSend(s.B, MethodName.R_TradeDone, s.OfferA, s.GoldA);
        Count("tradesDone"); if (Dedicated) GD.Print($"[SERVER] pet trade {NameOf(s.A)} <-> {NameOf(s.B)}");
    }
    [Rpc(MultiplayerApi.RpcMode.Authority)] void R_TradeDone(string json, int gold) => PetTrade.Done(json, gold);
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)] void R_TradeCancel() { if (_server && _trades.TryGetValue(Sender, out var s)) EndTrade(s, NameOf(Sender) + " cancelled the trade"); }
    [Rpc(MultiplayerApi.RpcMode.Authority)] void R_TradeEnd(string why) => PetTrade.Cancelled(why);
    void EndTrade(TradeSess s, string why)
    {
        _trades.Remove(s.A); _trades.Remove(s.B);
        TSend(s.A, MethodName.R_TradeEnd, why); TSend(s.B, MethodName.R_TradeEnd, why);
    }
    /// <summary>A trader left the lobby: the partner's escrow comes back.</summary>
    void TradePeerLeft(long id) { if (_trades.TryGetValue(id, out var s)) { _trades.Remove(s.A); _trades.Remove(s.B); long o = s.A == id ? s.B : s.A; TSend(o, MethodName.R_TradeEnd, NameOf(id) + " left; the trade is off"); } }
}

public partial class Ui
{
    /// <summary>The trade window: your offer (pick pets, gold) and the partner's (Odile, or a friend), confirmations, Confirm / Cancel.</summary>
    public void OpenPetTrade()
    {
        if (!PetTrade.Open) return; var d = Game.D;
        var v = Frame("pettrade", PetTrade.Npc ? $"{PetTrade.PartnerName}, Pet Trader" : $"Trade with {PetTrade.PartnerName}", new Vector2(1120, 620), OpenPetTrade);
        Row(v, PetTrade.Npc ? "Pick one of your pets. Odile offers a soul of the same rarity and about the same level, for a fee. Its gear comes back to your pack."
                            : "Pick up to " + PetTrade.MaxPets + " pets (and gold) to offer. Any change clears both confirmations. When both confirm, the pets change hands with their levels and stats.", Dim, 14);
        var row = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; row.AddThemeConstantOverride("separation", 16); v.AddChild(row);
        // ---- left: your pets
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(520, 0) }; left.AddThemeConstantOverride("separation", 4); row.AddChild(left);
        Header($"Your offer ({PetTrade.MyIds.Count}/{(PetTrade.Npc ? 1 : PetTrade.MaxPets)})" + (PetTrade.MyOk ? "   CONFIRMED" : ""), left);
        var ls = Scroll(left, 380); ls.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled; var lv = Inner(ls); lv.AddThemeConstantOverride("separation", 4);
        if (d.Pets.Count == 0) Empty(lv, "No pets to offer.");
        foreach (var p in d.Pets)
        {
            var pp = p; bool on = PetTrade.MyIds.Contains(p.Id); var sp = p.Sp();
            AddRow(lv, $"{p.Name()}  Lv{p.Level}  {PetDb.RarityName(sp.Rarity)} {sp.Name}" + (p.Id == d.ActivePet ? "  (companion)" : "") + (PetTrade.Npc ? $"\nfee {PetTrade.Fee(p)} gold" : ""),
                on ? Gold : PetDb.RarityColor(sp.Rarity).Lerp(Parch, .4f), on ? "Take back" : "Offer", () => { PetTrade.Toggle(pp.Id); OpenPetTrade(); }, !PetTrade.Busy);
        }
        if (!PetTrade.Npc)
        {
            var gh = new HBoxContainer(); gh.AddThemeConstantOverride("separation", 6); left.AddChild(gh);
            Lbl($"Gold {PetTrade.MyGold} (you have {d.Gold})", 15, GoldC, gh);
            foreach (var (t, n) in new[] { ("-100", -100), ("-10", -10), ("+10", 10), ("+100", 100) }) { int k = n; gh.AddChild(Btn(t, () => { PetTrade.SetGold(PetTrade.MyGold + k); OpenPetTrade(); }, true)); }
        }
        // ---- right: theirs
        var right = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; right.AddThemeConstantOverride("separation", 4); row.AddChild(right);
        Header($"{PetTrade.PartnerName}'s offer" + (PetTrade.TheirOk ? "   CONFIRMED" : ""), right);
        var rs = Scroll(right, 380); rs.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled; var rv = Inner(rs); rv.AddThemeConstantOverride("separation", 4);
        if (PetTrade.Theirs.Count == 0) Empty(rv, PetTrade.Npc ? "Offer a pet to see what Odile gives for it." : "Nothing offered yet.");
        foreach (var p in PetTrade.Theirs)
        {
            var sp = p.Sp(); var st = p.Stats(); var box = CardBox(rv); var c = new VBoxContainer(); box.AddChild(c);
            Lbl($"{p.Name()}  Lv{p.Level}  {PetDb.RarityName(sp.Rarity)} {sp.Name}", 17, PetDb.RarityColor(sp.Rarity), c);
            Lbl($"{(sp.Elem == "physical" ? "" : sp.Elem + " ")}{sp.Arch}   nature {PetDb.Nature(p.Nature).Name}   trainer {p.Ot}", 13, Dim, c);
            Lbl($"HP {st.Hp}  ATK {st.Atk}  DEF {st.Def}  MAG {st.Mag}  SPD {st.Spd}", 14, Parch, c);
            Lbl("Skills: " + string.Join(", ", p.Skills.Select(s => PetDb.Skill(s)?.Name ?? s)), 13, Dim, c).AutowrapMode = TextServer.AutowrapMode.WordSmart;
        }
        if (PetTrade.Npc && PetTrade.TheirGold < 0) Lbl($"Fee: {-PetTrade.TheirGold} gold (you have {d.Gold})", 16, d.Gold >= -PetTrade.TheirGold ? GoldC : ErrRed, right);
        else if (PetTrade.TheirGold > 0) Lbl($"+ {PetTrade.TheirGold} gold", 16, GoldC, right);
        // ---- bottom
        var bot = new HBoxContainer(); bot.AddThemeConstantOverride("separation", 12); v.AddChild(bot);
        bool any = PetTrade.MyIds.Count > 0 || PetTrade.Theirs.Count > 0 || PetTrade.MyGold > 0 || PetTrade.TheirGold > 0;
        var cb = Btn(PetTrade.Busy ? "Trading..." : PetTrade.Npc ? "Make the swap" : PetTrade.MyOk ? "Withdraw confirmation" : "Confirm trade", () => { PetTrade.Confirm(); if (PetTrade.Open) OpenPetTrade(); });
        cb.Disabled = PetTrade.Busy || !any; cb.CustomMinimumSize = new Vector2(260, 42); bot.AddChild(cb);
        var xb = Btn(PetTrade.Npc ? "Leave" : "Cancel trade", () => { PetTrade.Cancel(); Close(); }); xb.CustomMinimumSize = new Vector2(200, 42); xb.Disabled = PetTrade.Busy; bot.AddChild(xb);
        if (!PetTrade.Npc) Lbl(PetTrade.MyOk && PetTrade.TheirOk ? "Both confirmed: swapping..." : PetTrade.MyOk ? $"Waiting for {PetTrade.PartnerName}" : PetTrade.TheirOk ? $"{PetTrade.PartnerName} has confirmed" : "", 15, Gold, bot);
    }
}
