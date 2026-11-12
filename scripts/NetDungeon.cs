using System;
using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>
/// Party dungeons. The host keeps one shared instance per party (PartyInfo.Dun*): which dungeon, floor, seed and monster level
/// (average party level + 3). Every member builds the same floor from the seed. One member, the owner, runs the monsters and sends
/// snapshots to the others, whose copies are puppets that forward hits to the owner. Kills pay everyone inside: 90% of the experience
/// split evenly, and each member rolls their own personal loot with a slightly better drop chance. If the owner leaves, the host
/// hands the monsters to the next member inside.
/// </summary>
public partial class Net
{
    float _dunT, _dunSnapT;
    readonly Dictionary<int, float> _dunOpened = new();   // party id -> clock when its floor was opened (members need a moment to arrive)

    // ================================================================== instance (host)
    public void DunRequest(string key, int depth, bool descend) { if (_server) HostDunReq(MyId, key, depth, descend); else RpcId(1, MethodName.R_DunReq, key, depth, descend); }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)] void R_DunReq(string key, int depth, bool descend) { if (_server) HostDunReq(Sender, key, depth, descend); }

    List<long> Inside(PartyInfo p) { var l = new List<long>(); int z = DunZone(p.Id); foreach (var m in p.Members) if (Players.TryGetValue(m, out var pi) && pi.Zone == z) l.Add(m); return l; }
    bool DunLive(PartyInfo p, long except) { if (p.DunKey == "") return false; var l = Inside(p); l.Remove(except); return l.Count > 0 || World.Clock - _dunOpened.GetValueOrDefault(p.Id) < 8f; }

    void HostDunReq(long who, string key, int depth, bool descend)
    {
        var p = PartyOf(who);
        if (p == null || p.Members.Count < 2) { SendGo(who, key, depth, 0, 0, 0, 0); return; }   // not in a party any more: private dungeon
        bool live = DunLive(p, who);
        // the party is already down there: join them on their floor (also when someone else already took the stairs)
        if (live && (!descend || depth <= p.DunDepth)) { SendGo(who, p.DunKey, p.DunDepth, p.DunSeed, p.DunLvl, p.DunOwner, p.Id); Count("dunJoins"); return; }
        // open a new floor: the first member in, or the next floor down (everyone inside follows)
        var follow = descend ? Inside(p) : new List<long>(); follow.Remove(who);
        float sum = 0; foreach (var m in p.Members) sum += Players.GetValueOrDefault(m)?.Level ?? 1;
        p.DunKey = live ? p.DunKey : key; p.DunDepth = depth; p.DunSeed = (int)(GD.Randi() & 0x3fffffff) + 1; p.DunOwner = who;
        p.DunLvl = Progression.PartyDungeonLevel(sum / p.Members.Count, depth); _dunOpened[p.Id] = World.Clock;
        SendGo(who, p.DunKey, p.DunDepth, p.DunSeed, p.DunLvl, p.DunOwner, p.Id);
        foreach (var m in follow) SendGo(m, p.DunKey, p.DunDepth, p.DunSeed, p.DunLvl, p.DunOwner, p.Id);
        if (!descend) { string place = World.GetPoi(p.DunKey)?.Name ?? "a dungeon"; foreach (var m in p.Members) if (m != who) Tell(m, $"{NameOf(who)} entered {place}. Use any dungeon door to join them."); }
        Count("dunOpened"); SendRoster();
    }
    void SendGo(long peer, string key, int depth, int seed, int lvl, long owner, int inst)
    {
        if (peer == MyId) Dungeon.EnterShared(key, depth, seed, lvl, owner, inst);
        else RpcId(peer, MethodName.R_DunGo, key, depth, seed, lvl, owner, inst);
    }
    [Rpc(MultiplayerApi.RpcMode.Authority)]
    void R_DunGo(string key, int depth, int seed, int lvl, long owner, int inst) { if (seed == 0) Dungeon.EnterShared(key, depth, 0, 0, 0, 0); else Dungeon.EnterShared(key, depth, seed, lvl, owner, inst); }

    /// <summary>Host, twice a second: close empty instances and hand the monsters on when their owner has left.</summary>
    void HostTickDungeons()
    {
        foreach (var p in Parties.Values)
        {
            if (p.DunKey == "" || World.Clock - _dunOpened.GetValueOrDefault(p.Id) < 8f) continue;
            var inside = Inside(p);
            if (inside.Count == 0) { p.DunKey = ""; p.DunOwner = 0; continue; }
            if (inside.Contains(p.DunOwner)) continue;
            p.DunOwner = inside[0]; Count("dunOwnerMoves");
            foreach (var m in inside) { if (m == MyId) Dungeon.SetOwner(p.DunSeed, p.DunOwner); else RpcId(m, MethodName.R_DunOwner, p.DunSeed, p.DunOwner); }
        }
    }
    [Rpc(MultiplayerApi.RpcMode.Authority)] void R_DunOwner(int seed, long owner) => Dungeon.SetOwner(seed, owner);

    // ================================================================== monsters (owner <-> members)
    /// <summary>Remote heroes inside the same party dungeon as us.</summary>
    public List<long> DunPeers() { var l = new List<long>(); int z = MyZone; foreach (var kv in Heroes) if (IsInstanceValid(kv.Value) && kv.Value.HasPose && kv.Value.Zone == z) l.Add(kv.Key); return l; }
    /// <summary>Heroes the dungeon's monsters can chase: ours and every friend on this floor.</summary>
    public static IEnumerable<Node3D> DungeonHeroes()
    {
        var me = Player.Instance; if (me != null && !me.Dead && Dungeon.Active) yield return me;
        if (I == null || !Dungeon.Shared) yield break; int z = MyZone;
        foreach (var h in I.Heroes.Values) if (IsInstanceValid(h) && h.HasPose && h.Zone == z && !h.Dead) yield return h;
    }

    void SendDungeonSnapshots()
    {
        var peers = DunPeers(); if (peers.Count == 0) return;
        var list = Dungeon.NetList; var buf = new List<float>(300); var chunks = new List<float[]>();
        for (int i = 0; i < list.Count; i++)
        {
            var e = list[i]; if (e == null || !IsInstanceValid(e)) continue;
            buf.Add(i); e.NetWrite(buf);
            if (buf.Count >= Enemy.NetPerPacket * Enemy.NetStride) { chunks.Add(buf.ToArray()); buf.Clear(); }
        }
        if (buf.Count > 0) chunks.Add(buf.ToArray());
        foreach (var p in peers) foreach (var c in chunks) RpcId(p, MethodName.R_DunEnemies, Dungeon.Seed, c);
    }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 2)]
    void R_DunEnemies(int seed, float[] f)
    {
        if (!Dungeon.Shared || Dungeon.IsOwner || seed != Dungeon.Seed) return;
        var list = Dungeon.NetList;
        for (int k = 0; k + Enemy.NetStride <= f.Length; k += Enemy.NetStride)
        {
            int i = (int)f[k]; if (i < 0 || i >= list.Count) continue; var e = list[i];
            if (e != null && IsInstanceValid(e)) e.NetRead(f, k + 1);
        }
        Count("dunSnaps");
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    void R_DunHit(int seed, int idx, float[] f, string type, string statuses)
    {
        if (!Dungeon.IsOwner || seed != Dungeon.Seed || idx < 0 || idx >= Dungeon.NetList.Count) return;
        var e = Dungeon.NetList[idx]; if (e == null || !IsInstanceValid(e) || e.Dead) return;
        var h = Unpack(f, type, statuses); h.Remote = true; h.Peer = Sender; h.NoText = true; e.TakeHit(h); Count("dunRemoteHits");   // the friend shares their own number
    }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    void R_DunStun(int seed, int idx, float t)
    {
        if (!Dungeon.IsOwner || seed != Dungeon.Seed || idx < 0 || idx >= Dungeon.NetList.Count) return;
        var e = Dungeon.NetList[idx]; if (e != null && IsInstanceValid(e)) e.Stun(Mathf.Min(t, 2f));
    }

    /// <summary>Owner: one of our monsters hit a friend on this floor; their game applies it to their hero.</summary>
    public void HitPeerDun(long peer, HitInfo h)
    {
        int src = h.Attacker is Enemy e && e.FromDungeon ? e.NetIdx : -1; float mul = h.Attacker is Enemy se && IsInstanceValid(se) ? se.Status.DealtMul : 1f;
        var f = new float[] { h.Damage * mul, h.Knock, h.From.X, h.From.Y, h.From.Z, h.NoBlock ? 1 : 0, h.IsProjectile ? 1 : 0 };
        RpcId(peer, MethodName.R_DunHitMe, Dungeon.Seed, src, f, h.Type, PackStatuses(h.Statuses));
    }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    void R_DunHitMe(int seed, int src, float[] f, string type, string statuses)
    {
        var p = Player.Instance; if (p == null || !Dungeon.Shared || seed != Dungeon.Seed) return;
        var h = Unpack(new float[] { f[0], f[1], 0, 0, f[2], f[3], f[4], 0, 0, 0, 0, 0 }, type, statuses);
        h.NoBlock = f[5] > .5f; h.IsProjectile = f[6] > .5f;
        if (src >= 0 && src < Dungeon.NetList.Count && Dungeon.NetList[src] is Enemy a && IsInstanceValid(a)) h.Attacker = a;
        p.TakeHit(h); Count("dunHitsTaken");
    }

    /// <summary>Owner: a boss called adds; everyone on the floor gets a copy with the same index.</summary>
    public void DunSpawn(int idx, string type, int lvl, Vector3 pos) { foreach (var p in DunPeers()) RpcId(p, MethodName.R_DunSpawn, Dungeon.Seed, idx, type, lvl, pos); }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    void R_DunSpawn(int seed, int idx, string type, int lvl, Vector3 pos) { if (Dungeon.Shared && !Dungeon.IsOwner && seed == Dungeon.Seed) Dungeon.SpawnPuppet(idx, type, lvl, pos); }

    // ================================================================== kills
    /// <summary>
    /// Owner: a monster on this floor died. Everyone inside gets an equal share of 90% of its experience, the kill and quest credit,
    /// and their own personal loot roll (drop chance x1.15 in a group, gold split evenly). The killer also gets its materials.
    /// </summary>
    public void DunKill(Enemy e, int xp, long killer, string mats, bool loot, int gold, float chance, float bonus, float pot)
    {
        var rec = new List<long> { MyId }; rec.AddRange(DunPeers()); int n = rec.Count;
        int share = Progression.PartyShare(xp, n); float ch = n > 1 ? Mathf.Min(1f, chance * Progression.PartyDropMul) : chance; int g = n > 1 ? Mathf.CeilToInt(gold / (float)n) : gold;
        var at = e.GlobalPosition;
        foreach (var r in rec)
        {
            string m = r == killer ? mats : "";
            if (r == MyId) LocalDunKill(e.Type, e.Def.Realm, e.Def.Boss, share, at, m, loot, e.Level, ch, bonus, g, pot);
            else RpcId(r, MethodName.R_DunKill, e.Type, e.Def.Realm, e.Def.Boss, share, at, m, loot, e.Level, ch, bonus, g, pot);
        }
        Count("dunKills");
    }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    void R_DunKill(string type, int realm, bool boss, int xp, Vector3 at, string mats, bool loot, int lvl, float chance, float bonus, int gold, float pot)
    {
        if (!Dungeon.Shared || Sender != Dungeon.Owner) return;
        LocalDunKill(type, realm, boss, xp, at, mats, loot, lvl, chance, bonus, gold, pot); Count("kills");
    }
    static void LocalDunKill(string type, int realm, bool boss, int xp, Vector3 at, string mats, bool loot, int lvl, float chance, float bonus, int gold, float pot)
    {
        LocalKill(type, realm, boss, xp, at, mats);
        if (!loot) return;
        Loot.DropFrom(at, lvl, chance, bonus, gold, pot);
        if (boss) { Loot.DropFrom(at + new Vector3(1, 0, 1), lvl, 1f, 3f, 0, 0f); Loot.DropFrom(at + new Vector3(-1, 0, -1), lvl, .6f, 2f, 0, 0f); }
        LootTables.TrySetDrop(at, lvl, chance >= .3f, boss);   // everyone rolls their own set drop (upgraded monsters have a higher drop chance)
    }
}
