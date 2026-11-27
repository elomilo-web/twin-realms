using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TwinRealms;

/// <summary>
/// Party quest sharing over the network (focus A-29). Quests stay where they are: each hero's quest state lives in that hero's save and
/// that hero's game decides it. What is new: a game tells its party what just happened to a quest, and every member's game applies the
/// same step to its own hero if it has that quest in the matching state.
///   R_PQ (client -> host): kind, quest id, two short texts and a number (see PartyQuests). The host only checks and relays: the sender
///     must be in a party, the event must be one of the known kinds with short fields, at most quests.eventsPerSec a second; it goes to
///     the other members of the SENDER'S OWN party who are online, and to nobody else (PartyQuests.Recipients).
///   R_PQIn (host -> member): the event with the sender's peer id and roster name.
/// Range: anywhere on the server (a party member in a dungeon or another land still gets the progress). Experience and loot keep
/// their own rule (party members within 90 m of the kill, Net.Recipients).
/// </summary>
public partial class Net
{
    readonly Dictionary<long, (ulong sec, int n)> _pqRate = new();

    /// <summary>Client: tell the party about a quest step of ours.</summary>
    public void PartyQuest(string kind, string id, string a, string b, int n)
    {
        if (!Active || MyParty == null) return;
        if (_server) HostPartyQuest(MyId, kind, id, a, b, n); else RpcId(1, MethodName.R_PQ, kind, id, a, b, n);
    }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    void R_PQ(string kind, string id, string a, string b, int n) { if (_server) HostPartyQuest(Sender, kind, id, a, b, n); }
    void HostPartyQuest(long from, string kind, string id, string a, string b, int n)
    {
        if (!Players.ContainsKey(from) || !PartyQuests.ValidEvent(kind, id, a, b, n)) return;
        var p = PartyOf(from); if (p == null) return;
        ulong sec = Time.GetTicksMsec() / 1000; var rt = _pqRate.GetValueOrDefault(from); rt = rt.sec == sec ? (sec, rt.n + 1) : (sec, 1); _pqRate[from] = rt;
        if (rt.n > Math.Max(1, SocialCfg.I("quests.eventsPerSec", 20))) return;
        string name = ChatName(from);
        foreach (var m in PartyQuests.Recipients(p.Members, from, Players.Keys))
        {
            if (m == MyId) { if (!Dedicated) PartyQuests.Apply(from, name, kind, id, a, b, n); }
            else RpcId(m, MethodName.R_PQIn, from, name, kind, id, a, b, n);
        }
        Count("partyQuest");
    }
    [Rpc(MultiplayerApi.RpcMode.Authority)]
    void R_PQIn(long from, string name, string kind, string id, string a, string b, int n)
    {
        if (!SameParty(MyId, from)) return;   // the roster we hold must agree that they are in our party
        PartyQuests.Apply(from, ChatServer.CleanName(name), kind, id, a, b, n);
    }
}

/// <summary>
/// Party quest sharing, the player's side (A-29). Quests.cs and Story.cs call the "we did this" functions; Apply is a party member's
/// step arriving. Events (kind, id, a, b, n):
///   qa  old quest accepted                      qp  old quest progress: n = the count reached (kills, waves, duels)
///   qd  old quest turned in at its giver        sa  story quest accepted
///   so  story objective: a = stage, b = objective index, n = the count reached (kill and reach objectives)
///   ss  story stage finished its objectives: a = stage, b = the next stage (must be that stage's "then")
///   sp  story conversation: a = stage, b = talk index, n = choice index (-1: a talk without choices)
/// Rules: progress is a count, and a member takes the higher of its own and the sender's, so the party ends on the same number and a kill
/// that reached us twice (once shared by the host, once here) is never counted twice. A stage change or a choice applies to members who
/// are at that same stage: they get the stage's and the choice's effects on their own save (gold, items, standing, world flags) and a note
/// saying who chose what. Turning in an old quest completes it for members who have it active or ready, each with their own reward roll.
/// A member who has not started the quest is OFFERED it (a chat line to click, or /quest accept) and nothing is completed for them.
/// Objectives read from a hero's own save (materials held, standing, skill, level, gold, flags) are not shared; the stage change that
/// follows them is. Failing (time limits, fail conditions) is not shared; a choice that fails the quest is, like every choice.
/// </summary>
public static class PartyQuests
{
    /// <summary>True while a member's step is applied here: nothing it causes is sent on again.</summary>
    public static bool Applying;
    static readonly HashSet<string> Kinds = new() { "qa", "qp", "qd", "sa", "so", "ss", "sp" };
    public sealed class Offer { public bool Story; public string Id = "", Name = "", Who = ""; public bool Taken; }
    public static readonly List<Offer> Offers = new();
    static readonly Color Col = new(.89f, .69f, .29f);

    // ------------------------------------------------------------------ the host's checks (no networking: the self-test runs them)
    public static bool ValidEvent(string? kind, string? id, string? a, string? b, int n) =>
        kind != null && id != null && a != null && b != null && Kinds.Contains(kind) && id.Length is > 0 and <= 64 && a.Length <= 64 && b.Length <= 64 && n >= -1 && n <= 100000;
    /// <summary>Who an event of `from` goes to: the other members of the party it was found in who are online. Not a member: nobody.</summary>
    public static List<long> Recipients(IEnumerable<long> members, long from, IEnumerable<long> online)
    {
        var mem = members.ToList(); var l = new List<long>(); if (!mem.Contains(from)) return l;
        var on = new HashSet<long>(online); foreach (var m in mem) if (m != from && on.Contains(m)) l.Add(m);
        return l;
    }
    /// <summary>A stage event may only move a quest along its own data: to the "then" of the stage it is at.</summary>
    public static bool StageOk(Story.Stage st, string from, string to) => st.Id == from && to == st.Then && !to.StartsWith("fail");

    // ------------------------------------------------------------------ our own steps (called by Quests.cs / Story.cs)
    static bool On => !Applying && Net.Active && Net.I?.MyParty != null && SocialCfg.B("quests.share", true);
    /// <summary>Sent a moment later, so the messages of this frame (a party dungeon's kill going to the members) arrive before it.</summary>
    static void Send(string kind, string id, string a = "", string b = "", int n = 0) { if (On) Callable.From(() => Net.I?.PartyQuest(kind, id, a, b, n)).CallDeferred(); }
    public static void OldAccepted(string id) => Send("qa", id);
    public static void OldProgress(string id, int n) => Send("qp", id, "", "", n);
    public static void OldDone(string id) => Send("qd", id);
    public static void StoryAccepted(string id) => Send("sa", id);
    public static void StoryObj(string id, string stage, int obj, int n) => Send("so", id, stage, obj.ToString(), n);
    public static void StoryStage(string id, string from, string to) => Send("ss", id, from, to);
    public static void StoryPick(string id, string stage, int talk, int choice) => Send("sp", id, stage, talk.ToString(), choice);

    // ------------------------------------------------------------------ a member's step arrives
    public static void Apply(long from, string who, string kind, string id, string a, string b, int n)
    {
        if (Applying || !ValidEvent(kind, id, a, b, n) || !SocialCfg.B("quests.share", true) || Player.Instance == null) return;
        Applying = true;
        try { if (kind[0] == 'q') ApplyOld(who, kind, id, n); else ApplyStory(who, kind, id, a, b, n); }
        catch (Exception e) { GD.PrintErr("[PARTY] quest event failed: " + e.Message); }
        finally { Applying = false; }
    }
    static void ApplyOld(string who, string kind, string id, int n)
    {
        var q = Quests.ById(id); if (q == null) return; var s = Game.Q(id);
        if (s.S == 0) { if (Quests.Available(q)) Offered(false, id, q.Name, who); return; }
        if (kind == "qp" && s.S == 1 && n > s.N)
        {
            s.N = Math.Min(n, q.Need);
            if (s.N >= q.Need) { s.S = 2; Ui.Banner(q.Name, "objective complete"); Ui.Note($"{who} finished it for the party. Return to {Quests.GiverName(q.Giver)} to claim your reward", Col); Sfx.Play("level"); }
            else Ui.Note($"{q.Name}: {s.N} / {q.Need}  (party: {who})", Col);
            Game.Notify();
        }
        else if (kind == "qd" && (s.S == 1 || s.S == 2))
        {
            Ui.Note($"{who} turned in \"{q.Name}\" for the party", Col); Chat.System($"{who} turned in \"{q.Name}\": it is complete for you too.");
            Quests.Reward(q); Game.Notify();
        }
    }
    static void ApplyStory(string who, string kind, string id, string a, string b, int n)
    {
        var q = Story.Get(id); if (q == null) return; int status = Story.Status(id);
        if (status == 0) { if (Story.Available(q)) Offered(true, id, q.Name, who); return; }
        if (kind == "sa" || Story.Now(q) is not { } st || st.Id != a) return;   // not at that stage: nothing of theirs applies to us
        switch (kind)
        {
            case "so":
            {
                if (!int.TryParse(b, out int i) || i < 0 || i >= st.Objs.Count) return; var o = st.Objs[i]; if (o.Type != "kill" && o.Type != "reach") return;
                var s = Story.St(id); int v = Math.Min(n, o.N); if (v <= s.N.GetValueOrDefault(i.ToString())) return;
                s.N[i.ToString()] = v; Ui.Note($"{q.Name}: {Story.ObjText(q, st, i)}  (party: {who})", Col); Game.Notify(); Story.PartyAdvance();
                break;
            }
            case "ss":
                if (!StageOk(st, a, b)) return;
                Ui.Note($"{who} moved \"{q.Name}\" on for the party", Col); Story.PartyEnter(q, b);
                break;
            case "sp":
            {
                if (!int.TryParse(b, out int ti) || ti < 0 || ti >= st.Talks.Count) return; var t = st.Talks[ti];
                if (n >= t.Choices.Count || (n < 0 && t.Choices.Count > 0) || (n >= 0 && t.Choices[n].Goto == "stay")) return;   // "stay" choices (asking around) are each hero's own
                string what = n >= 0 ? $"{who} chose \"{t.Choices[n].Text}\" in \"{q.Name}\"" : $"{who} spoke for the party in \"{q.Name}\"";
                Ui.Note(what, Col); Chat.System(what + ". The quest moves on for you too.");
                Story.Pick(q, t, n);
                break;
            }
        }
    }

    // ------------------------------------------------------------------ offers to members who have not started the quest
    static void Offered(bool story, string id, string name, string who)
    {
        if (!SocialCfg.B("quests.offer", true) || Offers.Exists(o => o.Story == story && o.Id == id)) return;   // once per session
        Offers.Add(new Offer { Story = story, Id = id, Name = name, Who = who }); int n = Offers.Count;
        Ui.Note($"{who} shares the quest \"{name}\": /quest accept {n}", Col);
        Chat.System($"{who} is on the quest \"{name}\". Take it too: /quest accept {n}", $"/quest accept {n}");
    }
    public static void Reset() => Offers.Clear();
    public static string Command(string[] a)
    {
        if (a.Length == 0 || a[0].Equals("offers", StringComparison.OrdinalIgnoreCase))
            return Offers.Count == 0 ? "Your party has shared no quests with you. Quests a member starts or works on are offered here." : string.Join("\n", Offers.Select((o, i) => $"{i + 1}. \"{o.Name}\" from {o.Who}{(o.Taken ? " (taken)" : "")}"));
        if (!a[0].Equals("accept", StringComparison.OrdinalIgnoreCase)) return "usage: /quest accept [n], /quest offers";
        int n = a.Length > 1 && int.TryParse(a[1], out int v) ? v : Offers.FindLastIndex(o => !o.Taken) + 1;
        if (n < 1 || n > Offers.Count) return "There is no such offer. /quest offers lists them.";
        var of = Offers[n - 1];
        if (Player.Instance == null || Player.Instance.Dead) return "Not now.";
        if (of.Story)
        {
            var q = Story.Get(of.Id); if (q == null) return "That quest no longer exists.";
            if (Story.Status(of.Id) != 0) { of.Taken = true; return $"You already have \"{q.Name}\"."; }
            if (!Story.Available(q)) return $"You cannot take \"{q.Name}\" yet (its requirements are not met).";
            of.Taken = true; Story.Accept(q); return $"Quest started: {q.Name}. You begin at its start; steps your party takes at your stage count for you.";
        }
        var oq = Quests.ById(of.Id); if (oq == null) return "That quest no longer exists.";
        if (Game.Q(of.Id).S != 0) { of.Taken = true; return $"You already have \"{oq.Name}\"."; }
        if (!Quests.Available(oq)) return $"You cannot take \"{oq.Name}\" yet.";
        of.Taken = true; Quests.Accept(oq); return "";
    }
}
