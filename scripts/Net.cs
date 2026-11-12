using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>What everyone in the lobby knows about one player (sent by the host as the "roster").</summary>
public class PeerInfo
{
    public long Id; public string Name = "Hero", Race = "human", Class = "warrior", Body = "male"; public int Level = 1;
    public Dictionary<string, Item?> Eq = new(); public int Party; public int Zone; public float Hp = 1, MaxHp = 1;
}
/// <summary>A party: members share kills, experience and gold; the leader picks how item drops are shared.</summary>
public class PartyInfo
{
    public int Id; public long Leader; public string Loot = "personal"; public int Rr; public List<long> Members = new();
    // the party's shared dungeon (DunKey empty when nobody is inside): which dungeon, floor, seed, monster level and who runs the monsters
    public string DunKey = ""; public int DunDepth, DunSeed, DunLvl; public long DunOwner;
}

/// <summary>
/// Drop-in co-op over the internet or a home network (Godot ENet, UDP port 24565 by default).
/// The host runs the world: overworld enemies, loot, parties, the party chest and duels. Everybody keeps their own hero
/// (their own save: stats, pack, skills, home storage) and simulates their own movement and attacks.
/// Remote heroes are drawn from pose packets; enemies on a client are "puppets" that mirror the host and forward hits to it.
/// Dungeons and the arena stay private instances for whoever enters them.
/// </summary>
public partial class Net : Node
{
    public static Net? I;
    public const int DefaultPort = 24565, Protocol = 5, MaxPlayers = 8;   // Protocol 5: shared ambushes, friends' effects, numbers and statuses (snapshots carry statuses); older builds are told to update
    const int Channels = 8;   // 0 reliable events, 1 poses, 2 enemy snapshots, 3 clock, 4 shots (both sides must open the same number)
    public static bool Active => I != null && I._live;
    public static bool IsHost => Active && I!._server;
    public static bool IsClient => Active && !I!._server;
    public static long MyId => Active ? I!._myId : 1;
    /// <summary>0 overworld, 1 a dungeon, 2 the arena. Heroes are only drawn and only interact when they share a zone.</summary>
    public static int MyZone => Dungeon.Active ? (Dungeon.Inst != 0 ? DunZone(Dungeon.Inst) : 1) : Arenas.Active ? 2 : 0;
    /// <summary>Zone number of a party's shared dungeon (1 is a private dungeon).</summary>
    public static int DunZone(int partyId) => 1000 + partyId;
    public static bool InDungeonZone(int zone) => zone == 1 || zone >= 1000;
    public static string Status = "";
    public static bool AutoAccept;                 // the multiplayer bot accepts every invite and duel
    public static string LastNote = "";            // shown on the title screen after a disconnect
    public static int HostPort = DefaultPort;      // the port we are hosting on (put into the join codes)

    ENetMultiplayerPeer? _peer; bool _live, _server; long _myId = 1; Action? _onWelcome;
    public readonly Dictionary<long, PeerInfo> Players = new();
    public readonly Dictionary<int, PartyInfo> Parties = new();
    public readonly Dictionary<long, RemoteHero> Heroes = new();
    public int Realm;                              // lobby-wide realm
    public Vector3 WelcomePos; public float WelcomeTime;
    float _poseT, _snapT, _worldT, _meT; string _meSig = "";
    public readonly Dictionary<string, int> Stats = new();  // counters for the self-test bot
    public void Count(string k) => Stats[k] = Stats.GetValueOrDefault(k) + 1;
    static readonly JsonSerializerOptions Opt = new() { IncludeFields = true };
    public static string ToJson<T>(T o) => JsonSerializer.Serialize(o, Opt);
    public static T? FromJson<T>(string s) { try { return JsonSerializer.Deserialize<T>(s, Opt); } catch { return default; } }

    public override void _Ready()
    {
        I = this; ProcessMode = ProcessModeEnum.Always;
        Multiplayer.PeerConnected += OnPeerConnected; Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += OnConnected; Multiplayer.ConnectionFailed += OnFailed; Multiplayer.ServerDisconnected += OnServerGone;
    }
    public override void _ExitTree()
    {
        Multiplayer.PeerConnected -= OnPeerConnected; Multiplayer.PeerDisconnected -= OnPeerDisconnected;
        Multiplayer.ConnectedToServer -= OnConnected; Multiplayer.ConnectionFailed -= OnFailed; Multiplayer.ServerDisconnected -= OnServerGone;
        Shutdown(); if (I == this) I = null;
    }

    // ================================================================== connect
    public PeerInfo MeInfo()
    {
        var d = Game.D; var p = Player.Instance;
        return new PeerInfo { Id = MyId, Name = d.Name, Race = d.Race, Class = d.Class, Body = d.Body, Level = d.Level, Eq = new Dictionary<string, Item?>(d.Eq), Zone = MyZone, Hp = p?.Hp ?? d.MaxHp(), MaxHp = d.MaxHp(), Party = Players.GetValueOrDefault(MyId)?.Party ?? 0 };
    }

    /// <summary>Starts hosting this world. Friends join with your address and the port.</summary>
    public bool Host(int port)
    {
        Shutdown();
        var p = new ENetMultiplayerPeer(); var err = p.CreateServer(port, MaxPlayers, Channels);
        if (err != Error.Ok) { Status = $"Could not host on port {port} ({err}). Is another game already using it?"; return false; }
        Multiplayer.MultiplayerPeer = p; _peer = p; _live = true; _server = true; _myId = 1; HostPort = port;
        Players.Clear(); Parties.Clear(); Players[1] = MeInfo(); Realm = World.Realm;
        LoadPartyChest();
        Status = $"Hosting on port {port}. Friends join with one of your addresses below.";
        OpenRouterPort(port);
        Ui.Note("You are hosting. Friends can now join your world.", Ui.Gold);
        return true;
    }

    /// <summary>Connects to a host. onWelcome runs once the host has accepted us and sent the world state.</summary>
    public bool Join(string address, int port, Action onWelcome)
    {
        Shutdown();
        var p = new ENetMultiplayerPeer(); var err = p.CreateClient(address.Trim(), port, Channels);
        if (err != Error.Ok) { Status = $"Could not start connecting ({err})"; return false; }
        Multiplayer.MultiplayerPeer = p; _peer = p; _server = false; _onWelcome = onWelcome;
        Status = $"Connecting to {address}:{port} ...";
        return true;
    }

    /// <summary>Leaves the lobby (clients) or stops hosting (host). The world keeps running locally.</summary>
    public void Shutdown()
    {
        if (_peer != null) { _peer.Close(); }
        if (IsInsideTree()) Multiplayer.MultiplayerPeer = new OfflineMultiplayerPeer();
        _peer = null; _live = false; _server = false; _myId = 1; Players.Clear(); Parties.Clear(); Duel = null;
        foreach (var h in Heroes.Values) if (IsInstanceValid(h)) h.QueueFree(); Heroes.Clear();
        foreach (var l in LootNodes.Values) if (IsInstanceValid(l)) l.QueueFree(); LootNodes.Clear(); _lootRecs.Clear();
        ClearProxies(); Dungeon.Detach(); _askedAmbush = false;
        foreach (var e in World.NetExtra.Values) if (IsInstanceValid(e) && e.Puppet) e.Puppet = false;   // left the lobby: their ambushers fight on locally
        if (World.I != null) foreach (var e in World.NetEnemies) if (IsInstanceValid(e)) e.Puppet = false;
    }

    void OnConnected()
    {
        _live = true; _myId = Multiplayer.GetUniqueId(); Status = "Connected, waiting for the host ...";
        _peer?.GetPeer(1)?.SetTimeout(4096, 90000, 120000);   // the host may also stall (saving, building); same patience on our side
        RpcId(1, MethodName.R_Hello, Protocol, ToJson(MeInfo()));
    }
    void OnFailed() { Status = "Could not reach the host. Check the address, the port, and that the host's firewall allows the game."; _peer = null; _live = false; Multiplayer.MultiplayerPeer = new OfflineMultiplayerPeer(); }
    void OnServerGone()
    {
        GD.Print("[NET] lost the connection to the host");
        LastNote = "The host closed the game. Your hero was saved.";
        Game.Save(); Shutdown();
        // back to the title (the test bot keeps its window so it can finish its report)
        if (Ui.InGame && !AutoAccept) { Ui.InGame = false; GetTree().Paused = false; GetTree().CallDeferred(SceneTree.MethodName.ReloadCurrentScene); }
        else Status = LastNote;
    }
    void OnPeerConnected(long id)
    {
        GD.Print($"[NET] peer {id} connected");
        // building the (1.1, much bigger) world blocks a joining game for well over 15 s without network traffic. The old limit (32 retries,
        // 15 s minimum) dropped the friend while their world was still loading: they then played on alone, unheard. Allow two minutes of silence.
        _peer?.GetPeer((int)id)?.SetTimeout(4096, 90000, 120000);
    }
    void OnPeerDisconnected(long id)
    {
        GD.Print($"[NET] peer {id} disconnected");
        string n = Players.GetValueOrDefault(id)?.Name ?? "A player";
        if (Heroes.Remove(id, out var h) && IsInstanceValid(h)) h.QueueFree();
        if (!_server) return;
        if (Duel != null && (Duel.A == id || Duel.B == id)) HostDuelOver(Duel.A == id ? Duel.B : Duel.A, id);
        HostLeaveParty(id, false); Players.Remove(id);
        Broadcast(MethodName.R_Note, n + " left the game"); Ui.Note(n + " left the game", Ui.Dim);
        SendRoster();
    }

    void Broadcast(StringName m, params Variant[] args) { if (Active) Rpc(m, args); }
    long Sender => Multiplayer.GetRemoteSenderId() == 0 ? MyId : Multiplayer.GetRemoteSenderId();

    // ---- handshake
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    void R_Hello(int protocol, string infoJson)
    {
        if (!_server) return; long id = Sender;
        if (protocol != Protocol) { RpcId(id, MethodName.R_Kick, $"The host runs a different version of Twin Realms (host: {Game.Version}). Both players need the newest download from the beta folder."); return; }
        if (Players.Count >= MaxPlayers) { RpcId(id, MethodName.R_Kick, "The host's world is full."); return; }
        var info = FromJson<PeerInfo>(infoJson) ?? new PeerInfo(); info.Id = id; info.Party = 0; Players[id] = info;
        var me = Player.Instance; var at = me != null && MyZone == 0 ? me.GlobalPosition : World.OnGround(0, 9, .6f);
        RpcId(id, MethodName.R_Welcome, World.Realm, World.TimeOfDay, at);
        foreach (var other in Players.Keys) if (other != id && other != MyId) RpcId(other, MethodName.R_Note, info.Name + " joined the world");
        Ui.Note(info.Name + " joined your world", Ui.Gold); Sfx.Play("portal");
        SendRoster(); Count("joins");
    }
    [Rpc(MultiplayerApi.RpcMode.Authority)]
    void R_Welcome(int realm, float time, Vector3 hostPos)
    {
        Realm = realm; WelcomePos = hostPos; WelcomeTime = time; Status = "Joined.";
        var cb = _onWelcome; _onWelcome = null; cb?.Invoke();
        World.TimeOfDay = time; World.ForceRealm(realm);
    }
    [Rpc(MultiplayerApi.RpcMode.Authority)]
    void R_Kick(string why) { LastNote = why; Status = why; Shutdown(); }
    [Rpc(MultiplayerApi.RpcMode.Authority)]
    void R_Note(string text) => Ui.Note(text, Ui.Dim.Lightened(.3f));

    // ---- roster
    void SendRoster()
    {
        if (!_server) return;
        if (Players.ContainsKey(1)) { var me = MeInfo(); me.Party = Players[1].Party; Players[1] = me; }
        var roster = ToJson(new List<PeerInfo>(Players.Values)); var parties = ToJson(new List<PartyInfo>(Parties.Values));
        Broadcast(MethodName.R_Roster, roster, parties); SyncHeroes();
    }
    [Rpc(MultiplayerApi.RpcMode.Authority)]
    void R_Roster(string roster, string parties)
    {
        var l = FromJson<List<PeerInfo>>(roster); var pl = FromJson<List<PartyInfo>>(parties); if (l == null || pl == null) return;
        Players.Clear(); foreach (var p in l) Players[p.Id] = p;
        Parties.Clear(); foreach (var p in pl) Parties[p.Id] = p;
        SyncHeroes(); Game.Notify();
    }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    void R_Me(string infoJson)
    {
        if (!_server) return; long id = Sender; var info = FromJson<PeerInfo>(infoJson); if (info == null || !Players.TryGetValue(id, out var old)) return;
        info.Id = id; info.Party = old.Party; Players[id] = info; SendRoster();
    }
    /// <summary>Creates or refreshes the drawn hero of every other player once our own world exists.</summary>
    void SyncHeroes()
    {
        if (Game.Root == null || Player.Instance == null) return;
        foreach (var p in Players.Values)
        {
            if (p.Id == MyId) continue;
            if (!Heroes.TryGetValue(p.Id, out var h) || !IsInstanceValid(h)) { h = new RemoteHero { Peer = p.Id, Name = "Remote" + p.Id }; Heroes[p.Id] = h; Game.Root.AddChild(h); }
            h.Refresh(p);
        }
        foreach (var id in new List<long>(Heroes.Keys)) if (!Players.ContainsKey(id)) { if (IsInstanceValid(Heroes[id])) Heroes[id].QueueFree(); Heroes.Remove(id); }
    }
    public PeerInfo? Me => Players.GetValueOrDefault(MyId);
    public PartyInfo? MyParty => Me != null && Me.Party != 0 ? Parties.GetValueOrDefault(Me.Party) : null;
    public PartyInfo? PartyOf(long id) => Players.TryGetValue(id, out var p) && p.Party != 0 ? Parties.GetValueOrDefault(p.Party) : null;
    public string NameOf(long id) => Players.GetValueOrDefault(id)?.Name ?? "someone";
    public bool SameParty(long a, long b) { var pa = PartyOf(a); return pa != null && pa.Members.Contains(b); }

    // ================================================================== per-frame sync
    public override void _PhysicsProcess(double delta)
    {
        if (!Active) return; float dt = (float)delta; var me = Player.Instance;
        if (me == null) return;
        if (Heroes.Count != Math.Max(0, Players.Count - 1)) SyncHeroes();
        if (!_askedAmbush) AskAmbushes();   // once our world exists: the ambushers already roaming
        // our pose 20 times a second to everyone
        _poseT -= dt; if (_poseT <= 0) { _poseT = .05f; Rpc(MethodName.R_Pose, me.NetPacket()); if (Players.TryGetValue(MyId, out var mi)) { mi.Hp = me.Hp; mi.MaxHp = Game.D.MaxHp(); mi.Zone = MyZone; } }
        // level, gear or zone changed: tell the host (it re-sends the roster)
        _meT -= dt; if (_meT <= 0)
        {
            _meT = .5f; var d = Game.D; string sig = d.Level + "|" + MyZone + "|" + d.Name; foreach (var s in PlayerStats.Slots) { var it = d.Eq.GetValueOrDefault(s); sig += "|" + (it == null ? "-" : it.Name + it.Rar + it.Up); }
            if (sig != _meSig) { _meSig = sig; if (_server) SendRoster(); else RpcId(1, MethodName.R_Me, ToJson(MeInfo())); }
        }
        if (_server)
        {
            _snapT -= dt; if (_snapT <= 0) { _snapT = .1f; SendEnemySnapshots(); }
            _worldT -= dt; if (_worldT <= 0) { _worldT = 2f; Broadcast(MethodName.R_World, World.TimeOfDay, Realm); TickLoot(2f); }
            _dunT -= dt; if (_dunT <= 0) { _dunT = .5f; HostTickDungeons(); }
        }
        if (Dungeon.IsOwner) { _dunSnapT -= dt; if (_dunSnapT <= 0) { _dunSnapT = .1f; SendDungeonSnapshots(); } }
        TickDuel(dt);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 1)]
    void R_Pose(float[] p)
    {
        long id = Sender; if (p.Length < 35) return;
        if (!Heroes.ContainsKey(id) || !Heroes[id].HasPose) GD.Print($"[NET] first pose from {id}, hero known: {Heroes.ContainsKey(id)}");
        if (Players.TryGetValue(id, out var info)) { info.Hp = p[6]; info.MaxHp = p[7]; info.Zone = (int)p[32]; info.Level = (int)p[34]; }
        if (Heroes.TryGetValue(id, out var h) && IsInstanceValid(h)) h.Push(p);
    }
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 3)]
    void R_World(float time, int realm)
    {
        if (Mathf.Abs(Mathf.Wrap(time - World.TimeOfDay, -.5f, .5f)) > .01f) World.TimeOfDay = time;
        if (realm != World.Realm && !Dungeon.Active && !Arenas.Active) World.ApplyShift(realm);
        Realm = realm;
    }

    // ================================================================== realm
    /// <summary>R in multiplayer shifts the whole lobby (the host decides; it keeps the shared enemies consistent).</summary>
    public void RequestShift() { if (_server) HostShift(); else RpcId(1, MethodName.R_ShiftReq); }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)] void R_ShiftReq() { if (_server) HostShift(); }
    void HostShift()
    {
        if (World.ShiftCd > 0) return;
        Realm = World.Realm ^ 1; if (!Dungeon.Active && !Arenas.Active) World.ApplyShift(Realm);
        Broadcast(MethodName.R_Realm, Realm); Count("shifts");
    }
    [Rpc(MultiplayerApi.RpcMode.Authority)]
    void R_Realm(int realm) { Realm = realm; if (!Dungeon.Active && !Arenas.Active) World.ApplyShift(realm); }

    // ================================================================== enemies
    /// <summary>Host: positions, health and state of the overworld enemies near each client, 10 times a second.</summary>
    void SendEnemySnapshots()
    {
        var list = World.NetEnemies; if (list.Count == 0) return;
        foreach (var kv in Heroes)
        {
            var h = kv.Value; if (!IsInstanceValid(h) || !h.HasPose || h.Zone != 0) continue;
            // packets stay under one network frame (~1400 bytes): Enemy.NetPerPacket enemies each
            var buf = new List<float>(300); var at = h.GlobalPosition;
            void Put(int i, Enemy e)
            {
                if (!IsInstanceValid(e) || e.GlobalPosition.DistanceSquaredTo(at) > 120f * 120f) return;
                buf.Add(i); e.NetWrite(buf);
                if (buf.Count >= Enemy.NetPerPacket * Enemy.NetStride) { RpcId(kv.Key, MethodName.R_Enemies, buf.ToArray()); buf.Clear(); }
            }
            for (int i = 0; i < list.Count; i++) Put(i, list[i]);
            foreach (var (i, e) in World.NetExtra) Put(i, e);   // ambushes
            if (buf.Count > 0) RpcId(kv.Key, MethodName.R_Enemies, buf.ToArray());
        }
    }
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 2)]
    void R_Enemies(float[] f)
    {
        var list = World.NetEnemies;
        for (int k = 0; k + Enemy.NetStride <= f.Length; k += Enemy.NetStride)
        {
            var e = World.NetGet((int)f[k]); if (e == null) continue;   // built with the world, or an ambush
            e.NetRead(f, k + 1);
        }
    }

    /// <summary>Client: our hit on a puppet goes to the host, which applies the target's armour and resistances and decides kills.</summary>
    public void SendEnemyHit(Enemy e, HitInfo h, float raw, string statuses)
    {
        var TS = Game.TS;
        var f = new float[] { raw, h.Knock, h.Stun, h.Up, h.From.X, h.From.Y, h.From.Z, TS["armorPen"], TS["armorPenP"], TS["magicPen"], TS["magicPenP"], h.Crit ? 1 : 0 };
        if (e.FromDungeon) { if (Dungeon.Shared && !Dungeon.IsOwner) RpcId(Dungeon.Owner, MethodName.R_DunHit, Dungeon.Seed, e.NetIdx, f, h.Type, statuses); return; }
        if (e.DuelPeer != 0) { RpcId(e.DuelPeer, MethodName.R_DuelHit, f, h.Type, statuses); return; }
        RpcId(1, MethodName.R_EnemyHit, e.NetIdx, f, h.Type, statuses);
    }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    void R_EnemyHit(int idx, float[] f, string type, string statuses)
    {
        if (!_server || World.NetGet(idx) is not Enemy e || e.Dead) return;
        // NoText: the friend shows (and shares) their own damage number, so we do not draw a second one
        var h = Unpack(f, type, statuses); h.Remote = true; h.Peer = Sender; h.NoText = true; e.TakeHit(h); Count("remoteHits");
    }
    static HitInfo Unpack(float[] f, string type, string statuses)
    {
        var h = new HitInfo { Damage = f[0], Knock = f[1], Stun = f[2], Up = f[3], From = new Vector3(f[4], f[5], f[6]), APen = f[7], APenP = f[8], MPen = f[9], MPenP = f[10], Crit = f[11] > .5f, Type = type };
        foreach (var s in statuses.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = s.Split(':'); if (p.Length < 4) continue;
            h.With(p[0], float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture), float.Parse(p[3], System.Globalization.CultureInfo.InvariantCulture));
        }
        return h;
    }
    public static string PackStatuses(List<(string id, float chance, float durMul, float pow)>? l)
    {
        if (l == null || l.Count == 0) return ""; var ci = System.Globalization.CultureInfo.InvariantCulture; var sb = new System.Text.StringBuilder();
        foreach (var (id, c, d, p) in l) sb.Append(id).Append(':').Append(c.ToString(ci)).Append(':').Append(d.ToString(ci)).Append(':').Append(p.ToString(ci)).Append(';');
        return sb.ToString();
    }
    public void SendEnemyStun(Enemy e, float t)
    {
        if (e.FromDungeon) { if (Dungeon.Shared && !Dungeon.IsOwner) RpcId(Dungeon.Owner, MethodName.R_DunStun, Dungeon.Seed, e.NetIdx, t); return; }
        if (e.DuelPeer == 0 && e.NetIdx >= 0) RpcId(1, MethodName.R_EnemyStun, e.NetIdx, t);
    }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)] void R_EnemyStun(int idx, float t) { if (_server && World.NetGet(idx) is Enemy e) e.Stun(Mathf.Min(t, 2f)); }

    /// <summary>Host: one of our enemies hit a friend's hero. Their game applies it to their hero (their armour, block, parry and evade).</summary>
    public void HitPeer(long peer, HitInfo h)
    {
        int src = h.Attacker is Enemy e ? e.NetIdx : -1; float mul = h.Attacker is Enemy se && IsInstanceValid(se) ? se.Status.DealtMul : 1f;
        var f = new float[] { h.Damage * mul, h.Knock, h.From.X, h.From.Y, h.From.Z, h.NoBlock ? 1 : 0, h.IsProjectile ? 1 : 0 };
        RpcId(peer, MethodName.R_HitMe, src, f, h.Type, PackStatuses(h.Statuses));
    }
    [Rpc(MultiplayerApi.RpcMode.Authority)]
    void R_HitMe(int src, float[] f, string type, string statuses)
    {
        var p = Player.Instance; if (p == null || MyZone != 0) return;
        var h = Unpack(new float[] { f[0], f[1], 0, 0, f[2], f[3], f[4], 0, 0, 0, 0, 0 }, type, statuses);
        h.NoBlock = f[5] > .5f; h.IsProjectile = f[6] > .5f;
        if (World.NetGet(src) is Enemy a) h.Attacker = a;
        p.TakeHit(h);
    }

    /// <summary>Living heroes in the overworld that the host's enemies can chase: our own hero and every remote hero.</summary>
    public static IEnumerable<Node3D> WorldHeroes()
    {
        var me = Player.Instance; if (me != null && !me.Dead && MyZone == 0) yield return me;
        if (I == null) yield break;
        foreach (var h in I.Heroes.Values) if (IsInstanceValid(h) && h.HasPose && h.Zone == 0 && !h.Dead) yield return h;
    }

    // ================================================================== visual projectiles
    /// <summary>Arrows and bolts are drawn for everyone in the same zone (the shooter's game decides what they hit).</summary>
    public void Shot(string kind, Vector3 pos, Vector3 vel, float life, bool big, float aoe)
    {
        if (!Active) return; Rpc(MethodName.R_Shot, kind, pos, vel, life, big, aoe, MyZone);
    }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable, TransferChannel = 4)]
    void R_Shot(string kind, Vector3 pos, Vector3 vel, float life, bool big, float aoe, int zone)
    {
        if (zone != MyZone || (zone != 0 && zone < 1000) || Game.Root == null || Player.Instance == null) return;   // overworld, or the same party dungeon
        var p = Projectile.Spawn(kind, 'x', pos, vel, 0, life, false, false, 0, big); p.NoNet = true;
    }

    // ================================================================== kills
    /// <summary>Host: who shares a kill: the killer plus party members in the overworld within 90 m.</summary>
    public List<long> Recipients(long killer, Vector3 at)
    {
        var l = new List<long> { killer }; var party = PartyOf(killer); if (party == null) return l;
        foreach (var m in party.Members) if (m != killer && PosOf(m) is Vector3 p && p.DistanceTo(at) < 90f) l.Add(m);
        return l;
    }
    public Vector3? PosOf(long id)
    {
        if (id == MyId) return Player.Instance != null && MyZone == 0 ? Player.Instance.GlobalPosition : null;
        return Heroes.TryGetValue(id, out var h) && IsInstanceValid(h) && h.HasPose && h.Zone == 0 ? h.GlobalPosition : null;
    }
    /// <summary>Host: hands out experience, kill counts, quest progress and materials for a shared overworld kill.</summary>
    public void KillRewards(Enemy e, int xp, long killer, string mats)
    {
        // party kill: 90% of the experience (data/progression.json "party") split evenly between the members nearby
        var rec = Recipients(killer, e.GlobalPosition); int share = Progression.PartyShare(xp, rec.Count);
        foreach (var r in rec)
        {
            string m = r == killer ? mats : "";
            if (r == MyId) LocalKill(e.Type, e.Def.Realm, e.Def.Boss, share, e.GlobalPosition, m);
            else RpcId(r, MethodName.R_Kill, e.Type, e.Def.Realm, e.Def.Boss, share, e.GlobalPosition, m);
        }
        if (killer != MyId) Count("remoteKills");
    }
    [Rpc(MultiplayerApi.RpcMode.Authority)]
    void R_Kill(string type, int realm, bool boss, int xp, Vector3 at, string mats) { LocalKill(type, realm, boss, xp, at, mats); Count("kills"); }
    static void LocalKill(string type, int realm, bool boss, int xp, Vector3 at, string mats)
    {
        Game.D.Kills++; Player.Instance?.AddXp(xp, at); Quests.OnKill(type, realm, boss);
        foreach (var s in mats.Split(';', StringSplitOptions.RemoveEmptyEntries)) { var p = s.Split(':'); if (p.Length == 2 && int.TryParse(p[1], out int n)) GatherDb.Give(p[0], n, at); }
    }
}
