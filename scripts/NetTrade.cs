using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TwinRealms;

/// <summary>
/// Host side of player trading (focus A-30): sessions, offers, confirmations, the countdown and the swap. Runs on the online server and
/// on a friend's host alike; the older pet-only trade (PetTrade.cs, R_Trade*) is untouched beside it.
///   R_TxReq (client -> host): ask a player to trade. Both must be in the same place within trade.radius metres; neither may be trading;
///     a player who blocks the asker is not asked. R_TxAsk (host -> target) shows the question, R_TxAnswer brings the answer (only an
///     answer to a request the host itself delivered within the last minute opens a session).
///   R_TxOpen (host -> both): the window opens.
///   R_TxOffer (client -> host): my offer. Online server: rebuilt from the server's copy of the save (AuctionServer.TradeView) and sent
///     back as R_TxMine; the partner sees the server's data. Friend's host: the offer is size-checked and relayed.
///     Any offer clears both confirmations and stops a countdown. R_TxView (host -> client): the partner's offer, both confirmations,
///     the revision and the seconds left on the countdown (-1: none).
///   R_TxOk (client -> host): confirm / withdraw, for a revision. Both confirmed: the countdown starts (trade.countdownSec). At zero:
///     online server: AuctionServer.TradeSwap on both stored saves, R_AhDelta to each game, R_TxFin; friend's host: R_TxPrep to both
///     (each game moves its offer into escrow and answers R_TxReady), then R_TxDone hands each the other's offer.
///   R_TxCancel (client -> host), R_TxEnd (host -> client): the trade is off; escrow comes back.
/// Every finished trade is logged (TR_DATA/tradelog/trade.log on the server, user://tradelog/ on a friend's host).
/// </summary>
public partial class Net
{
    sealed class TxSess
    {
        public long A, B; public TradeOffer AskA = new(), AskB = new(); public string ViewA = Empty, ViewB = Empty;
        public int Rev; public bool OkA, OkB, Prep, ReadyA, ReadyB; public float Count = -1, PrepT;
        public const string EmptyJson = "{\"Items\":[],\"Stacks\":{},\"Pets\":[],\"Gold\":0}";
        public static string Empty => EmptyJson;
    }
    readonly Dictionary<long, TxSess> _tx = new();
    readonly Dictionary<long, (long from, ulong at)> _txReq = new();   // target -> who asked and when

    bool Trading(long id) => _tx.ContainsKey(id) || _trades.ContainsKey(id);

    // ---- client API
    public void TxRequest(long target) => ToHost(MethodName.R_TxReq, target);
    public void TxOffer(string json) => ToHost(MethodName.R_TxOffer, json);
    public void TxOk(int rev, bool ok) => ToHost(MethodName.R_TxOk, rev, ok);
    public void TxCancel() => ToHost(MethodName.R_TxCancel);

    // ---- request and answer
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    void R_TxReq(long target)
    {
        if (!_server) return; long from = Sender;
        if (from == target || !Players.ContainsKey(target) || !Players.ContainsKey(from) || (Dedicated && (from == MyId || target == MyId))) return;
        if (Trading(from)) { Tell(from, "You are already trading"); return; }
        if (Trading(target)) { Tell(from, NameOf(target) + " is already trading"); return; }
        if (!Near(from, target, Trade.Radius)) { Tell(from, $"{NameOf(target)} is too far away: stand within {Trade.Radius:0} m to trade"); return; }
        if (_txReq.TryGetValue(target, out var rq) && rq.from == from && Time.GetTicksMsec() - rq.at < 5000) return;   // asked a moment ago
        if (Ignores(target, from)) return;   // blocked: the request goes nowhere
        _txReq[target] = (from, Time.GetTicksMsec());
        TSend(target, MethodName.R_TxAsk, from, NameOf(from));
    }
    [Rpc(MultiplayerApi.RpcMode.Authority)]
    void R_TxAsk(long from, string name)
    {
        if (AutoAccept) { ToHost(MethodName.R_TxAnswer, from, true); return; }
        if (Trade.Open || PetTrade.Open) { ToHost(MethodName.R_TxAnswer, from, false); return; }
        Chat.System($"{name} wants to trade with you.");
        Ui.I?.Ask($"{name} wants to trade items and gold with you. Both of you see the offers and must confirm.", "Trade", "No thanks", ok => ToHost(MethodName.R_TxAnswer, from, ok));
    }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    void R_TxAnswer(long from, bool ok)
    {
        if (!_server) return; long to = Sender;
        if (!_txReq.TryGetValue(to, out var rq) || rq.from != from || Time.GetTicksMsec() - rq.at > 60000) return;   // nobody asked
        _txReq.Remove(to);
        if (!Players.ContainsKey(from) || !Players.ContainsKey(to)) return;
        if (!ok) { Tell(from, NameOf(to) + " declined the trade"); return; }
        if (Trading(from) || Trading(to)) { Tell(to, "One of you is already trading"); return; }
        if (!Near(from, to, Trade.Radius * 1.5f)) { Tell(from, "You moved too far apart to trade"); Tell(to, "You moved too far apart to trade"); return; }
        var s = new TxSess { A = from, B = to }; _tx[from] = s; _tx[to] = s;
        TSend(from, MethodName.R_TxOpen, to, NameOf(to)); TSend(to, MethodName.R_TxOpen, from, NameOf(from)); Count("tx");
    }
    [Rpc(MultiplayerApi.RpcMode.Authority)] void R_TxOpen(long partner, string name) => Trade.Opened(partner, name);

    // ---- offers and confirmations
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    void R_TxOffer(string json)
    {
        if (!_server) return; long who = Sender; if (!_tx.TryGetValue(who, out var s) || s.Prep) return;
        var ask = Trade.Parse(json);
        if (ask == null) { Tell(who, "That offer is too large"); ask = new TradeOffer(); }
        if (Dedicated)
        {   // the online server shows the partner its own copy of what is offered (from the stored save), never the data in the message
            var v = AuctionServer.TradeView(AcctOf(who), ask);
            if (v == null) { Tell(who, "The server has no save of your hero yet. Offer again in a moment."); v = new TradeOffer(); }
            ask = v; TSend(who, MethodName.R_TxMine, ToJson(v));
        }
        string view = ToJson(ask);
        if (who == s.A) { s.AskA = ask; s.ViewA = view; } else { s.AskB = ask; s.ViewB = view; }
        s.OkA = s.OkB = false; s.Count = -1; s.Rev++; TxViews(s);
    }
    [Rpc(MultiplayerApi.RpcMode.Authority)] void R_TxMine(string json) => Trade.MineSet(json);
    void TxViews(TxSess s)
    {
        TSend(s.A, MethodName.R_TxView, s.ViewB, s.OkA, s.OkB, s.Rev, s.Count);
        TSend(s.B, MethodName.R_TxView, s.ViewA, s.OkB, s.OkA, s.Rev, s.Count);
    }
    [Rpc(MultiplayerApi.RpcMode.Authority)] void R_TxView(string theirs, bool myOk, bool theirOk, int rev, float count) => Trade.View(theirs, myOk, theirOk, rev, count);
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    void R_TxOk(int rev, bool ok)
    {
        if (!_server) return; long who = Sender; if (!_tx.TryGetValue(who, out var s) || s.Prep) return;
        if (rev != s.Rev) { TxViews(s); return; }   // an offer changed meanwhile: confirm again on what is shown now
        if (who == s.A) s.OkA = ok; else s.OkB = ok;
        s.Count = -1;
        if (s.OkA && s.OkB)
        {
            if (s.AskA.Empty() && s.AskB.Empty()) { s.OkA = s.OkB = false; }
            else if (!Near(s.A, s.B, Trade.Radius * 1.5f)) { s.OkA = s.OkB = false; Tell(s.A, "You are too far apart to trade"); Tell(s.B, "You are too far apart to trade"); }
            else s.Count = Trade.CountdownSec;   // both confirmed: a short countdown, then the swap (TxTick)
        }
        TxViews(s);
    }
    /// <summary>Host, every physics frame: countdowns that ran out, and a "prepare" a game never answered.</summary>
    void TxTick(float dt)
    {
        if (_tx.Count == 0) return;
        foreach (var s in _tx.Values.Distinct().ToList())
        {
            if (s.Prep) { s.PrepT -= dt; if (s.PrepT <= 0) TxEnd(s, "The trade timed out"); continue; }
            if (s.Count < 0 || !(s.OkA && s.OkB)) continue;
            s.Count -= dt; if (s.Count > 0) continue;
            s.Count = -1;
            if (!Players.ContainsKey(s.A) || !Players.ContainsKey(s.B)) { TxEnd(s, "The trade is off"); continue; }
            if (Dedicated) TxServerSwap(s);
            else { s.Prep = true; s.PrepT = 12f; TSend(s.A, MethodName.R_TxPrep, s.Rev); TSend(s.B, MethodName.R_TxPrep, s.Rev); }   // friend hosting: both games put their offer in escrow first
        }
    }
    /// <summary>Online server: the swap on the server's copies of both saves; each game then gets the change to its hero and the end of the trade.</summary>
    void TxServerSwap(TxSess s)
    {
        string ka = AcctOf(s.A), kb = AcctOf(s.B);
        var r = AuctionServer.TradeSwap(ka, s.AskA, s.ViewA, kb, s.AskB, s.ViewB);
        if (!r.ok)
        {   // something changed since the offers were shown: both see the offers as the server has them now and confirm again
            foreach (bool isA in new[] { true, false })
            {
                long who = isA ? s.A : s.B; var v = AuctionServer.TradeView(isA ? ka : kb, isA ? s.AskA : s.AskB) ?? new TradeOffer(); string j = ToJson(v);
                if (isA) { s.AskA = v; s.ViewA = j; } else { s.AskB = v; s.ViewB = j; }
                TSend(who, MethodName.R_TxMine, j); Tell(who, r.msg);
            }
            s.OkA = s.OkB = false; s.Rev++; TxViews(s); return;
        }
        _tx.Remove(s.A); _tx.Remove(s.B);
        if (r.a != null) TSend(s.A, MethodName.R_AhDelta, ToJson(r.a)); if (r.b != null) TSend(s.B, MethodName.R_AhDelta, ToJson(r.b));
        TSend(s.A, MethodName.R_TxFin, "Trade complete: you received " + Trade.Describe(s.AskB)); TSend(s.B, MethodName.R_TxFin, "Trade complete: you received " + Trade.Describe(s.AskA));
        Count("txDone"); TxLog(s, ka, kb);
    }
    void TxLog(TxSess s, string ka, string kb)
    {
        static string K(string k) => k.Length > 8 ? k[..8] : k;
        string line = $"{NameOf(s.A)} [{K(ka)}] gave: {Trade.Describe(s.AskA)}\t{NameOf(s.B)} [{K(kb)}] gave: {Trade.Describe(s.AskB)}";
        GD.Print("[TRADE] " + line.Replace('\t', ' ')); AuctionServer.TradeLog(line);
    }
    [Rpc(MultiplayerApi.RpcMode.Authority)] void R_TxFin(string note) => Trade.Finished(note);

    // ---- friend's host: both put their offer in escrow, then each gets the other's
    [Rpc(MultiplayerApi.RpcMode.Authority)] void R_TxPrep(int rev) => ToHost(MethodName.R_TxReady, rev, Trade.Prepare(rev));
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    void R_TxReady(int rev, bool ok)
    {
        if (!_server || Dedicated) return; long who = Sender; if (!_tx.TryGetValue(who, out var s) || !s.Prep || rev != s.Rev) return;
        if (!ok) { TxEnd(s, "The trade fell through: an offer was no longer there, or a pack is full"); return; }
        if (who == s.A) s.ReadyA = true; else s.ReadyB = true;
        if (!(s.ReadyA && s.ReadyB)) return;
        _tx.Remove(s.A); _tx.Remove(s.B);
        TSend(s.A, MethodName.R_TxDone, s.ViewB); TSend(s.B, MethodName.R_TxDone, s.ViewA);
        Count("txDone"); TxLog(s, "", "");
    }
    [Rpc(MultiplayerApi.RpcMode.Authority)] void R_TxDone(string json) => Trade.Done(json);
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)] void R_TxCancel() { if (_server && _tx.TryGetValue(Sender, out var s)) TxEnd(s, NameOf(Sender) + " cancelled the trade"); }
    [Rpc(MultiplayerApi.RpcMode.Authority)] void R_TxEnd(string why) => Trade.Cancelled(why);
    void TxEnd(TxSess s, string why)
    {
        _tx.Remove(s.A); _tx.Remove(s.B);
        TSend(s.A, MethodName.R_TxEnd, why); TSend(s.B, MethodName.R_TxEnd, why);
    }
    /// <summary>A trader left: the trade is off and the partner's escrow comes back.</summary>
    void TxPeerLeft(long id)
    {
        _txReq.Remove(id);
        if (!_tx.TryGetValue(id, out var s)) return;
        _tx.Remove(s.A); _tx.Remove(s.B); long o = s.A == id ? s.B : s.A; TSend(o, MethodName.R_TxEnd, NameOf(id) + " left; the trade is off");
    }
}
