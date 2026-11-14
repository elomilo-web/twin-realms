using System;
using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>What a loot pickup gives: gold, a potion or an item.</summary>
public class LootData { public int Gold; public string? Pot; public Item? Item; }

public class DuelInfo { public long A, B; public Vector3 Center; public float Countdown = 3.5f; public bool Live; }

public partial class Net
{
    // ================================================================== loot
    /// <summary>
    /// Loot rules (the party leader picks; solo players always own their own drops for a minute):
    ///  personal     every party member near the kill gets their own private roll (only they can see it)
    ///  roundrobin   one roll; each item belongs to the next member in turn
    ///  leader       one roll; items belong to the party leader
    ///  ffa          one roll; first to touch it takes it
    /// Gold from a party kill is split evenly and paid straight into everyone's purse. Owned drops become free for all after 60 s.
    /// </summary>
    public static readonly string[] LootModes = { "personal", "roundrobin", "leader", "ffa" };
    public static string LootModeName(string m) => m switch { "personal" => "Personal (each gets their own)", "roundrobin" => "Round robin (take turns)", "leader" => "Leader takes items", _ => "Free for all" };

    class LootRec { public int Id; public Vector3 Pos; public LootData Data = new(); public long Owner; public bool Private; public float Age; }
    readonly Dictionary<int, LootRec> _lootRecs = new();
    public readonly Dictionary<int, Loot> LootNodes = new();
    int _lootId;

    /// <summary>Host: an overworld enemy died; roll its drops for the killer and their party.</summary>
    public void DropLoot(Vector3 pos, int level, float chance, float bonus, int gold, float potChance, long killer)
    {
        var rec = Recipients(killer, pos); var party = PartyOf(killer); string mode = party?.Loot ?? "solo";
        if (rec.Count > 1) chance = Mathf.Min(1f, chance * Progression.PartyDropMul);   // parties find a little more
        float luck = Luck.ForPeer(killer); chance = Luck.Chance(chance, luck); bonus = Luck.Bonus(bonus, luck);   // the killer's party's pooled luck (Affixes.cs)
        if (gold > 0)
        {
            if (rec.Count > 1) { int each = Mathf.CeilToInt(gold / (float)rec.Count); foreach (var r in rec) Give(r, new LootData { Gold = each }, "shared gold"); }
            else Spawn(pos, new LootData { Gold = gold }, killer, false);
        }
        if (mode == "personal") { foreach (var r in rec) if (GD.Randf() < chance) Spawn(pos, new LootData { Item = Items.Roll(level, bonus) }, r, true); }
        else if (GD.Randf() < chance)
        {
            long owner = mode switch { "ffa" => 0, "leader" => party!.Leader, "roundrobin" => NextInTurn(party!, rec), _ => killer };
            Spawn(pos, new LootData { Item = Items.Roll(level, bonus) }, owner, false);
        }
        if (GD.Randf() < potChance) Spawn(pos, new LootData { Pot = GD.Randf() < .6f ? "hp" : "mp" }, rec.Count > 1 ? 0 : killer, false);
    }
    static long NextInTurn(PartyInfo p, List<long> near) { for (int k = 0; k < p.Members.Count; k++) { var m = p.Members[(p.Rr + k) % p.Members.Count]; if (near.Contains(m)) { p.Rr = (p.Rr + k + 1) % p.Members.Count; return m; } } return p.Leader; }

    void Spawn(Vector3 pos, LootData data, long owner, bool priv)
    {
        var r = new LootRec { Id = ++_lootId, Pos = pos + new Vector3((GD.Randf() - .5f) * 1.5f, .6f, (GD.Randf() - .5f) * 1.5f), Data = data, Owner = owner, Private = priv };
        _lootRecs[r.Id] = r; string json = ToJson(data), on = owner == 0 ? "" : NameOf(owner);
        if (!priv || owner == MyId) MakeLootNode(r.Id, r.Pos, data, owner, on);
        foreach (var id in Players.Keys) if (id != MyId && (!priv || owner == id)) RpcId(id, MethodName.R_LootSpawn, r.Id, r.Pos, json, owner, on);
    }
    void MakeLootNode(int id, Vector3 pos, LootData d, long owner, string ownerName)
    {
        if (Game.Root == null) return;
        var l = new Loot { Gold = d.Gold, Pot = d.Pot, Item = d.Item, NetId = id, Owner = owner, OwnerName = ownerName };
        Game.Root.AddChild(l); l.GlobalPosition = pos; LootNodes[id] = l;
    }
    [Rpc(MultiplayerApi.RpcMode.Authority)]
    void R_LootSpawn(int id, Vector3 pos, string json, long owner, string ownerName) { var d = FromJson<LootData>(json); if (d != null) MakeLootNode(id, pos, d, owner, ownerName); }

    /// <summary>We dropped an item from our pack (right-click): the host puts it on the ground for anyone to take, us included.</summary>
    public void DropItem(Item it, Vector3 pos)
    {
        string json = ToJson(new LootData { Item = it });
        if (_server) HostDropItem(json, pos); else RpcId(1, MethodName.R_DropItem, json, pos);
    }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)] void R_DropItem(string json, Vector3 pos) { if (_server) HostDropItem(json, pos); }
    void HostDropItem(string json, Vector3 pos) { var d = FromJson<LootData>(json); if (d?.Item != null) { Spawn(pos, d, 0, false); Count("drops"); } }

    /// <summary>Walking over a shared drop asks the host for it; the host checks who may take it.</summary>
    public void Claim(Loot l)
    {
        if (l.Item != null && Game.D.Inv.Count >= 28 && !(Game.D.PickScrap && !Loot.Wanted(l.Item))) { Ui.Note("Pack is full", new Color(1, .5f, .5f)); return; }
        if (_server) HostClaim(MyId, l.NetId); else RpcId(1, MethodName.R_LootClaim, l.NetId);
    }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)] void R_LootClaim(int id) { if (_server) HostClaim(Sender, id); }
    void HostClaim(long peer, int id)
    {
        if (!_lootRecs.TryGetValue(id, out var r)) return;
        if (r.Owner != 0 && r.Owner != peer && r.Age < 60f) { if (peer == MyId) LootDenied(NameOf(r.Owner)); else RpcId(peer, MethodName.R_LootDenied, NameOf(r.Owner)); return; }
        _lootRecs.Remove(id); Give(peer, r.Data, "");
        RemoveLootNode(id); Broadcast(MethodName.R_LootGone, id); Count("claims");
    }
    void Give(long peer, LootData d, string why) { if (peer == MyId) LocalGive(ToJson(d), why); else RpcId(peer, MethodName.R_LootGive, ToJson(d), why); }
    [Rpc(MultiplayerApi.RpcMode.Authority)] void R_LootGive(string json, string why) => LocalGive(json, why);
    void LocalGive(string json, string why)
    {
        var d = FromJson<LootData>(json); if (d == null) return; var D = Game.D;
        if (d.Gold > 0) { D.Gold += d.Gold; Ui.Note("+" + d.Gold + " gold" + (why != "" ? " (" + why + ")" : ""), new Color(1, .85f, .3f)); Sfx.Play("coin"); }
        if (d.Pot != null) { if (d.Pot == "hp") D.HpPot++; else D.MpPot++; Ui.Note(d.Pot == "hp" ? "Health potion" : "Mana potion", d.Pot == "hp" ? new Color(1, .5f, .5f) : new Color(.5f, .7f, 1f)); Sfx.Play("pickup"); }
        if (d.Item != null && D.PickScrap && !Loot.Wanted(d.Item)) Ui.Note($"Scrapped {d.Item.Name}: {LootTables.Disassemble(d.Item)}", Ui.Dim);   // pickup filter: auto-disassemble
        else if (d.Item != null) { if (D.Inv.Count < 28) D.Inv.Add(d.Item); else D.Stash.Add(d.Item); Ui.Note(d.Item.Describe(), d.Item.Color); Sfx.Play("pickup"); }
        Count("gotLoot"); Game.Notify();
    }
    float _deniedT;
    [Rpc(MultiplayerApi.RpcMode.Authority)] void R_LootDenied(string owner) => LootDenied(owner);
    void LootDenied(string owner) { if (World.Clock - _deniedT > 2.5f) { _deniedT = World.Clock; Ui.Note($"That belongs to {owner} for now", new Color(.8f, .8f, .9f)); } }
    [Rpc(MultiplayerApi.RpcMode.Authority)] void R_LootGone(int id) => RemoveLootNode(id);
    void RemoveLootNode(int id) { if (LootNodes.Remove(id, out var l) && IsInstanceValid(l)) l.QueueFree(); }
    void TickLoot(float dt)
    {
        var gone = new List<int>();
        foreach (var r in _lootRecs.Values) { r.Age += dt; if (r.Age > 300f) gone.Add(r.Id); }
        foreach (var id in gone) { _lootRecs.Remove(id); RemoveLootNode(id); Broadcast(MethodName.R_LootGone, id); }
    }

    // ================================================================== parties
    public void Invite(long target) { if (_server) HostInvite(MyId, target); else RpcId(1, MethodName.R_PartyInvite, target); }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)] void R_PartyInvite(long target) { if (_server) HostInvite(Sender, target); }
    void HostInvite(long from, long to)
    {
        if (!Players.ContainsKey(to) || from == to || SameParty(from, to)) return;
        if (to == MyId) AskParty(from, NameOf(from)); else RpcId(to, MethodName.R_PartyAsk, from, NameOf(from));
    }
    [Rpc(MultiplayerApi.RpcMode.Authority)] void R_PartyAsk(long from, string name) => AskParty(from, name);
    void AskParty(long from, string name)
    {
        if (AutoAccept) { AnswerParty(from, true); return; }
        Ui.I?.Ask($"{name} invites you to their party. Party members share kills, experience and gold.", "Join party", "No thanks", ok => AnswerParty(from, ok));
    }
    void AnswerParty(long from, bool ok) { if (_server) HostAnswerParty(MyId, from, ok); else RpcId(1, MethodName.R_PartyAnswer, from, ok); }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)] void R_PartyAnswer(long from, bool ok) { if (_server) HostAnswerParty(Sender, from, ok); }
    void HostAnswerParty(long joiner, long inviter, bool ok)
    {
        if (!ok) { Tell(inviter, NameOf(joiner) + " declined the invite"); return; }
        if (!Players.ContainsKey(inviter) || !Players.ContainsKey(joiner)) return;
        var p = PartyOf(inviter);
        if (p == null) { int id = 1; while (Parties.ContainsKey(id)) id++; p = new PartyInfo { Id = id, Leader = inviter }; p.Members.Add(inviter); Parties[id] = p; Players[inviter].Party = id; }
        if (p.Members.Count >= 6) { Tell(joiner, "That party is full"); return; }
        HostLeaveParty(joiner, false); p.Members.Add(joiner); Players[joiner].Party = p.Id;
        foreach (var m in p.Members) Tell(m, NameOf(joiner) + " joined the party");
        SendRoster(); Count("parties");
    }
    public void LeaveParty() { if (_server) HostLeaveParty(MyId, true); else RpcId(1, MethodName.R_PartyLeave); }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)] void R_PartyLeave() { if (_server) HostLeaveParty(Sender, true); }
    void HostLeaveParty(long who, bool send)
    {
        var p = PartyOf(who); if (p == null) return;
        p.Members.Remove(who); if (Players.TryGetValue(who, out var pi)) pi.Party = 0;
        foreach (var m in p.Members) Tell(m, NameOf(who) + " left the party");
        if (p.Members.Count < 2) { foreach (var m in p.Members) if (Players.TryGetValue(m, out var x)) x.Party = 0; Parties.Remove(p.Id); }
        else if (p.Leader == who) p.Leader = p.Members[0];
        if (send) SendRoster();
    }
    public void SetLootMode(string mode) { if (_server) HostLootMode(MyId, mode); else RpcId(1, MethodName.R_LootMode, mode); }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)] void R_LootMode(string mode) { if (_server) HostLootMode(Sender, mode); }
    void HostLootMode(long who, string mode)
    {
        var p = PartyOf(who); if (p == null || p.Leader != who || Array.IndexOf(LootModes, mode) < 0) return;
        p.Loot = mode; foreach (var m in p.Members) Tell(m, "Loot: " + LootModeName(mode)); SendRoster();
    }
    void Tell(long peer, string text) { if (peer == MyId) Ui.Note(text, Ui.Gold); else if (Players.ContainsKey(peer)) RpcId(peer, MethodName.R_Tell, text); }
    [Rpc(MultiplayerApi.RpcMode.Authority)] void R_Tell(string text) => Ui.Note(text, Ui.Gold);

    // ================================================================== party chest (kept by the host in user://party_chest.json)
    public const int ChestCap = 56;
    public List<Item> PartyChest = new();         // the host's real chest, or the client's latest copy of it
    public static string ChestPath = "user://party_chest.json";
    void LoadPartyChest()
    {
        try { using var f = FileAccess.Open(ChestPath, FileAccess.ModeFlags.Read); PartyChest = f != null ? FromJson<List<Item>>(f.GetAsText()) ?? new() : new(); } catch { PartyChest = new(); }
    }
    void SavePartyChest() { try { using var f = FileAccess.Open(ChestPath, FileAccess.ModeFlags.Write); f.StoreString(ToJson(PartyChest)); } catch { } }
    /// <summary>Opens the shared chest. Solo, it is your own second chest that your lobby will share once you host.</summary>
    public void OpenChest()
    {
        if (!Active) { LoadPartyChest(); Ui.I?.OpenPartyChest(); return; }
        if (_server) Ui.I?.OpenPartyChest(); else { RpcId(1, MethodName.R_ChestReq); Ui.I?.OpenPartyChest(); }
    }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)] void R_ChestReq() { if (_server) RpcId(Sender, MethodName.R_Chest, ToJson(PartyChest)); }
    [Rpc(MultiplayerApi.RpcMode.Authority)] void R_Chest(string json) { PartyChest = FromJson<List<Item>>(json) ?? new(); Game.Notify(); }
    public void ChestPut(int invIndex)
    {
        var d = Game.D; if (invIndex < 0 || invIndex >= d.Inv.Count) return; var it = d.Inv[invIndex];
        if (PartyChest.Count >= ChestCap) { Ui.Note("The party chest is full", new Color(1, .5f, .5f)); return; }
        d.Inv.RemoveAt(invIndex);
        if (!Active || _server) { PartyChest.Add(it); SavePartyChest(); ChestChanged(); }
        else { PartyChest.Add(it); RpcId(1, MethodName.R_ChestPut, ToJson(it)); }
        Game.Notify();
    }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    void R_ChestPut(string json)
    {
        if (!_server) return; var it = FromJson<Item>(json); if (it == null) return;
        if (PartyChest.Count >= ChestCap) { RpcId(Sender, MethodName.R_ChestGive, json, "The party chest is full; the item is back in your pack"); return; }
        PartyChest.Add(it); SavePartyChest(); ChestChanged(); Count("chestPuts");
    }
    public void ChestTake(int index)
    {
        if (index < 0 || index >= PartyChest.Count) return; if (Game.D.Inv.Count >= 28) { Ui.Note("Pack is full", new Color(1, .5f, .5f)); return; }
        var it = PartyChest[index];
        if (!Active || _server) { PartyChest.RemoveAt(index); SavePartyChest(); Game.D.Inv.Add(it); ChestChanged(); Game.Notify(); }
        else RpcId(1, MethodName.R_ChestTake, index, Sig(it));
    }
    static string Sig(Item it) => it.Name + "|" + it.Rar + "|" + it.Lvl + "|" + it.Dmg + "|" + it.Def + "|" + it.Up;
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    void R_ChestTake(int index, string sig)
    {
        if (!_server) return; long who = Sender;
        if (index < 0 || index >= PartyChest.Count || Sig(PartyChest[index]) != sig) { RpcId(who, MethodName.R_Chest, ToJson(PartyChest)); return; }   // someone else took it first
        var it = PartyChest[index]; PartyChest.RemoveAt(index); SavePartyChest();
        RpcId(who, MethodName.R_ChestGive, ToJson(it), ""); ChestChanged(); Count("chestTakes");
    }
    [Rpc(MultiplayerApi.RpcMode.Authority)]
    void R_ChestGive(string json, string note) { var it = FromJson<Item>(json); if (it == null) return; if (Game.D.Inv.Count < 28) Game.D.Inv.Add(it); else Game.D.Stash.Add(it); if (note != "") Ui.Note(note, new Color(1, .7f, .5f)); Game.Notify(); }
    void ChestChanged() { Broadcast(MethodName.R_Chest, ToJson(PartyChest)); Game.Notify(); }

    // ================================================================== duels
    public DuelInfo? Duel;
    public const float DuelRadius = 22f, PvpScale = .5f;
    public static bool InLiveDuel => I?.Duel is { Live: true } d && (d.A == MyId || d.B == MyId);
    public static bool InDuel => I?.Duel is { } d && (d.A == MyId || d.B == MyId);
    public long Opponent => Duel == null ? 0 : Duel.A == MyId ? Duel.B : Duel.A;
    public void Challenge(long target) { if (_server) HostDuelAsk(MyId, target); else RpcId(1, MethodName.R_DuelAsk, target); }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)] void R_DuelAsk(long target) { if (_server) HostDuelAsk(Sender, target); }
    void HostDuelAsk(long from, long to)
    {
        if (Duel != null) { Tell(from, "A duel is already under way"); return; }
        if (PosOf(from) == null || PosOf(to) == null) { Tell(from, "Both of you must be out in the world to duel"); return; }
        if (to == MyId) AskDuel(from, NameOf(from)); else RpcId(to, MethodName.R_DuelPrompt, from, NameOf(from));
    }
    [Rpc(MultiplayerApi.RpcMode.Authority)] void R_DuelPrompt(long from, string name) => AskDuel(from, name);
    void AskDuel(long from, string name)
    {
        if (AutoAccept) { AnswerDuel(from, true); return; }
        Ui.I?.Ask($"{name} challenges you to a duel. Nobody dies: the first to fall to 1 health loses. Leaving the ring forfeits.", "Accept", "Decline", ok => AnswerDuel(from, ok));
    }
    void AnswerDuel(long from, bool ok) { if (_server) HostDuelAnswer(MyId, from, ok); else RpcId(1, MethodName.R_DuelAnswer, from, ok); }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)] void R_DuelAnswer(long from, bool ok) { if (_server) HostDuelAnswer(Sender, from, ok); }
    void HostDuelAnswer(long b, long a, bool ok)
    {
        if (!ok) { Tell(a, NameOf(b) + " declined the duel"); return; }
        if (Duel != null || PosOf(a) is not Vector3 pa || PosOf(b) is not Vector3 pb) return;
        var c = (pa + pb) / 2; Broadcast(MethodName.R_DuelStart, a, b, c); StartDuel(a, b, c);
    }
    [Rpc(MultiplayerApi.RpcMode.Authority)] void R_DuelStart(long a, long b, Vector3 c) => StartDuel(a, b, c);
    Node3D? _ring;
    void StartDuel(long a, long b, Vector3 c)
    {
        Duel = new DuelInfo { A = a, B = b, Center = c };
        Ui.Note($"Duel: {NameOf(a)} against {NameOf(b)}", new Color(1f, .6f, .4f));
        if (!InDuel || Game.Root == null) return;
        Ui.Banner("Duel", $"against {NameOf(Opponent)}"); Sfx.Play("roar", .5f);
        // a glowing ring marks the arena; stepping out of it forfeits
        _ring = new Node3D(); Game.Root.AddChild(_ring); _ring.GlobalPosition = World.OnGround(c.X, c.Z, .05f);
        var m = new StandardMaterial3D { AlbedoColor = new Color(1f, .45f, .25f), EmissionEnabled = true, Emission = new Color(1f, .4f, .2f), EmissionEnergyMultiplier = 2f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        _ring.AddChild(new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = DuelRadius - .15f, OuterRadius = DuelRadius + .15f, Rings = 96, RingSegments = 4 }, MaterialOverride = m, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        MakeProxy(Opponent);
    }
    readonly Dictionary<long, Enemy> _proxies = new();
    /// <summary>An invisible stand-in that follows the opponent's hero, so every weapon, skill, lock-on and projectile can target them.</summary>
    void MakeProxy(long peer)
    {
        if (Game.Root == null || !Heroes.TryGetValue(peer, out var h)) return;
        var info = Players.GetValueOrDefault(peer);
        var def = new EnemyDef { N = info?.Name ?? "Rival", Kind = "proxy", Hp = Mathf.Max(1, info?.MaxHp ?? 100), Dmg = 0, Spd = 0, Aggro = 0, Xp = 0, R = .45f, Scale = 1f, NoLoot = true, Reach = 2.2f };
        var e = new Enemy { Type = "proxy", DefOverride = def, Proxy = true, Puppet = true, DuelPeer = peer, AnyRealm = true, NoLeash = true, Level = info?.Level ?? 1, Position = h.GlobalPosition };
        Game.Root.AddChild(e); _proxies[peer] = e;
    }
    void ClearProxies() { foreach (var e in _proxies.Values) if (IsInstanceValid(e)) e.QueueFree(); _proxies.Clear(); if (_ring != null && IsInstanceValid(_ring)) _ring.QueueFree(); _ring = null; }
    public RemoteHero? HeroOf(long peer) => Heroes.TryGetValue(peer, out var h) && IsInstanceValid(h) ? h : null;

    void TickDuel(float dt)
    {
        if (Duel == null || !InDuel) return;
        if (!Duel.Live) { float before = Duel.Countdown; Duel.Countdown -= dt; if (Mathf.CeilToInt(before) != Mathf.CeilToInt(Duel.Countdown) && Duel.Countdown > 0) Ui.Banner(Mathf.CeilToInt(Duel.Countdown).ToString(), "get ready"); if (Duel.Countdown <= 0) { Duel.Live = true; Ui.Banner("Fight!", ""); Sfx.Play("level"); } return; }
        var p = Player.Instance; if (p == null) return;
        var flat = p.GlobalPosition - Duel.Center; flat.Y = 0;
        if (flat.Length() > DuelRadius + 1.5f || MyZone != 0) { Ui.Note("You left the ring and forfeit the duel", new Color(1, .6f, .5f)); LoseDuel(); }
    }
    /// <summary>The opponent's hit on us (their game already applied their damage bonuses; ours applies our defenses).</summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    void R_DuelHit(float[] f, string type, string statuses)
    {
        long from = Sender; var p = Player.Instance; if (p == null || !InLiveDuel || Opponent != from) return;
        var h = Unpack(f, type, statuses); h.Damage *= PvpScale; h.Stun = Mathf.Min(h.Stun, .6f); h.Up = 0; h.Duel = true;
        if (_proxies.TryGetValue(from, out var px) && IsInstanceValid(px)) h.Attacker = px;
        p.TakeHit(h); Count("duelHitsTaken");
    }
    /// <summary>We fell to 1 health (or left the ring): the opponent wins.</summary>
    public void LoseDuel()
    {
        if (Duel == null || !InDuel) return; long w = Opponent, l = MyId; Duel.Live = false;
        if (_server) HostDuelOver(w, l); else RpcId(1, MethodName.R_DuelOver, w, l);
    }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)] void R_DuelOver(long w, long l) { if (_server && Duel != null && Sender == l) HostDuelOver(w, l); }
    void HostDuelOver(long w, long l) { if (Duel == null) return; Broadcast(MethodName.R_DuelEnd, w, l); EndDuel(w, l); }
    [Rpc(MultiplayerApi.RpcMode.Authority)] void R_DuelEnd(long w, long l) => EndDuel(w, l);
    void EndDuel(long w, long l)
    {
        bool mine = InDuel; Duel = null; ClearProxies();
        Ui.Note($"{NameOf(w)} defeated {NameOf(l)} in a duel", new Color(1f, .8f, .4f)); Count("duels");
        if (!mine) return;
        var p = Player.Instance; if (p != null) { p.Hp = Game.D.MaxHp(); p.Statuses.Clear(); }
        if (w == MyId) { Game.D.DuelWins++; Ui.Banner("Victory", $"you beat {NameOf(l)}"); Sfx.Play("level"); } else Ui.Banner("Defeat", $"{NameOf(w)} wins the duel");
        Game.Notify();
    }

    // ================================================================== addresses and router port
    /// <summary>This computer's addresses that a friend on the same network (or VPN) can use.</summary>
    public static List<string> LocalAddresses()
    {
        var l = new List<string>();
        foreach (var a in IP.GetLocalAddresses()) if (!a.Contains(':') && !a.StartsWith("127.") && !a.StartsWith("169.254")) l.Add(a);
        return l;
    }
    public static string PublicAddress = "", UpnpStatus = "";
    /// <summary>Asks the home router (UPnP) to forward the port so friends over the internet can reach us. Runs in the background.</summary>
    void OpenRouterPort(int port)
    {
        UpnpStatus = "Asking your router to open the port ..."; PublicAddress = "";
        var t = new System.Threading.Thread(() =>
        {
            try
            {
                var u = new Upnp(); int r = u.Discover(2000, 2, "InternetGatewayDevice");
                if (r != (int)Upnp.UpnpResult.Success || u.GetGateway() == null || !u.GetGateway().IsValidGateway()) { UpnpStatus = $"Your router did not answer (UPnP is off), so the internet code only works after you forward UDP port {port} to this computer, or use a VPN (help below)."; PublicAddress = LookUpPublicAddress(); return; }
                int a = u.AddPortMapping(port, port, "Twin Realms", "UDP");
                PublicAddress = u.QueryExternalAddress();
                if (!IsPublic(PublicAddress)) PublicAddress = LookUpPublicAddress();
                UpnpStatus = a == (int)Upnp.UpnpResult.Success ? $"Router port {port} opened: friends over the internet can join with your internet code." : $"Router refused to open the port ({(Upnp.UpnpResult)a}). Forward UDP {port} by hand or use a VPN (help below).";
            }
            catch (Exception e) { UpnpStatus = "Router check failed: " + e.Message; }
            if (PublicAddress == "" || !IsPublic(PublicAddress)) PublicAddress = LookUpPublicAddress();
            if (PublicAddress != "" && IsSharedByProvider(PublicAddress)) UpnpStatus += " Your internet provider shares one address between many homes (CGNAT), so direct joining over the internet will not work: use Tailscale or Radmin VPN.";
        }) { IsBackground = true };
        t.Start();
    }

    /// <summary>The router did not tell us our internet address: ask a public "what is my IP" service (one small HTTPS request).</summary>
    static string LookUpPublicAddress()
    {
        foreach (var url in new[] { "https://api.ipify.org", "https://icanhazip.com" })
        {
            try
            {
                using var c = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                string ip = c.GetStringAsync(url).GetAwaiter().GetResult().Trim();
                if (System.Net.IPAddress.TryParse(ip, out var a) && a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) return ip;
            }
            catch { }
        }
        return "";
    }
    static bool IsPublic(string ip)
    {
        if (!System.Net.IPAddress.TryParse(ip, out var a) || a.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
        var b = a.GetAddressBytes();
        return !(b[0] == 10 || b[0] == 127 || b[0] == 0 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254) || IsSharedByProvider(ip));
    }
    static bool IsSharedByProvider(string ip) { var p = ip.Split('.'); return p.Length == 4 && p[0] == "100" && int.TryParse(p[1], out int x) && x >= 64 && x <= 127; }
}

/// <summary>
/// Join codes: the host's address and port packed into 10 letters and digits (like "4K7QM-2XH9D"), so a friend can type or paste
/// a short code instead of an IP address and port. Same Wi-Fi: the home code; over the internet: the internet code.
/// </summary>
public static class JoinCode
{
    const string A = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";   // no I, L, O, U: nothing to misread
    public static string Make(string ip, int port)
    {
        var p = ip.Split('.'); if (p.Length != 4) return "";
        ulong v = 0; foreach (var s in p) { if (!byte.TryParse(s, out var b)) return ""; v = (v << 8) | b; }
        v = (v << 16) | (ushort)port;
        var c = new char[10]; for (int i = 9; i >= 0; i--) { c[i] = A[(int)(v & 31)]; v >>= 5; }
        return new string(c, 0, 5) + "-" + new string(c, 5, 5);
    }
    public static bool TryRead(string code, out string ip, out int port)
    {
        ip = ""; port = 0; var s = code.Trim().ToUpperInvariant().Replace("-", "").Replace(" ", "").Replace('O', '0').Replace('I', '1').Replace('L', '1');
        if (s.Length != 10) return false; ulong v = 0;
        foreach (var ch in s) { int k = A.IndexOf(ch); if (k < 0) return false; v = (v << 5) | (uint)k; }
        port = (int)(v & 0xffff); v >>= 16; ip = $"{(v >> 24) & 255}.{(v >> 16) & 255}.{(v >> 8) & 255}.{v & 255}";
        return port > 0;
    }
}
