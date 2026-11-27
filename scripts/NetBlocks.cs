using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TwinRealms;

/// <summary>
/// The block world in multiplayer: changes go through the host like loot. A player's own digging, building and spell damage is shown at
/// once and sent to the host, who applies it and passes it to everyone; water and fire run on the host only and are sent the same way.
/// A friend joining gets the host's overworld changes; while visiting, your own world's changes are left alone (not overwritten or saved).
/// Edits are tagged with their area (the overworld, or one dungeon floor) and ignored by anyone not in it.
/// </summary>
public partial class Net
{
    /// <summary>Sends this machine's block changes in an area.</summary>
    public void BlockEdits(string area, byte[] packed)
    {
        if (!Active || packed.Length == 0) return;
        if (_server) Rpc(MethodName.R_Blocks, area, packed);
        else RpcId(1, MethodName.R_BlocksUp, area, packed);
    }
    /// <summary>A friend asks the host to set flammable blocks burning (the host runs fire).</summary>
    public void BlockFire(string area, byte[] packed) { if (Active && !_server) RpcId(1, MethodName.R_BlockFire, area, packed); }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void R_BlocksUp(string area, byte[] packed)
    {
        if (!_server) return;
        // F-18: the host rejects edits inside a protected town zone (unless the sender is an admin) and tells the sender the real blocks.
        // F-44: the online server also refuses mining (BlockAudit.Judge) and writes every accepted change to the block log.
        long sender = Multiplayer.GetRemoteSenderId();
        if (Dedicated && !_acct.ContainsKey(sender)) return;
        if (area == BlockAudit.Area)
        {
            bool admin = PeerIsAdmin(sender); string acct = AcctOf(sender), who = NameOf(sender), why = "";
            var all = BlockArea.Unpack(packed);
            var keep = new List<(int x, int y, int z, byte id, byte meta)>(); var back = new List<(int x, int y, int z, byte id, byte meta)>(); var regen = new List<(int x, int y, int z, byte id, byte meta)>();
            foreach (var e in all)
            {
                string? deny = !admin && TownZones.AtBlock(e.x, e.z) is { } zone ? TownZones.Message(zone) : Dedicated ? BlockAudit.Judge(acct, admin, e.x, e.y, e.z, e.id) : null;
                if (deny == null) { keep.Add(e); if (Dedicated) BlockAudit.Accept(acct, who, e.x, e.y, e.z, e.id, e.meta); continue; }
                why = deny;
                if (Dedicated) { if (BlockWorld.ServerCell(area, e.x, e.y, e.z) is { } known) back.Add((e.x, e.y, e.z, known.id, known.meta)); else regen.Add((e.x, e.y, e.z, 0, 0)); }
                else if (BlockWorld.I?.Overworld() is { } ow && ow.Get(e.x, e.y, e.z) != 255) back.Add((e.x, e.y, e.z, ow.Get(e.x, e.y, e.z), ow.GetMeta(e.x, e.y, e.z)));
            }
            if (Dedicated) BlockAudit.Flush();
            if (keep.Count != all.Count)
            {
                GD.Print($"[blocks] refused {all.Count - keep.Count} of {all.Count} block changes from {who} (peer {sender}): {why}");
                RpcId(sender, MethodName.R_BlockTruth, area, BlockArea.Pack(back), BlockArea.Pack(regen), why);
                if (keep.Count == 0) return; packed = BlockArea.Pack(keep);
            }
        }
        BlockWorld.I?.ApplyRemote(area, packed);
        if (Dedicated) BlockWorld.KeepServerDiff(area, packed);
        Rpc(MethodName.R_Blocks, area, packed);   // to everyone (the sender re-applies the same values: no harm)
    }
    /// <summary>The host's answer to refused changes (and a rollback's result): these cells hold `known`; the `regen` cells are as the world was generated.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void R_BlockTruth(string area, byte[] known, byte[] regen, string why)
    {
        if (_server) return;
        if (known.Length > 0) BlockWorld.I?.ApplyRemote(area, known);
        if (regen.Length > 0) BlockWorld.I?.RevertToGen(area, BlockArea.Unpack(regen));
        if (why != "") TownZones.Notify(why);
    }
    /// <summary>Server: a rollback's result to everyone (BlockAudit.Rollback already updated what newcomers get).</summary>
    public void SendBlockTruth(List<(int x, int y, int z, byte id, byte meta)> set, List<(int x, int y, int z, byte id, byte meta)> regen)
    {
        if (!_server || set.Count + regen.Count == 0) return;
        const int per = 4000;   // cells per message
        for (int i = 0; i < Math.Max(set.Count, regen.Count); i += per)
            Rpc(MethodName.R_BlockTruth, BlockAudit.Area, BlockArea.Pack(set.Skip(i).Take(per).ToList()), BlockArea.Pack(regen.Skip(i).Take(per).ToList()), "");
        if (set.Count > 0) BlockWorld.I?.ApplyRemote(BlockAudit.Area, BlockArea.Pack(set));
    }
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void R_Blocks(string area, byte[] packed) { if (!_server) BlockWorld.I?.ApplyRemote(area, packed); }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void R_BlockFire(string area, byte[] packed)
    {
        if (!_server || BlockWorld.I?.Area?.Key != area) return;
        foreach (var (x, y, z, _, _) in BlockArea.Unpack(packed)) BlockWorld.I.Ignite(x, y, z);
    }
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void R_BlockSync(string area, byte[] packed) { if (!_server) BlockWorld.I?.SyncFromHost(area, packed); }

    /// <summary>Host: a friend got in; send the world's block changes (called from the welcome).</summary>
    public void SendBlockSync(long peer)
    {
        if (!_server) return;
        byte[] all = Dedicated ? BlockWorld.ServerDiff("overworld") : (BlockWorld.I?.OverworldChanges() ?? System.Array.Empty<byte>());
        RpcId(peer, MethodName.R_BlockSync, "overworld", all);
    }
}

public partial class BlockWorld
{
    readonly List<(int x, int y, int z, byte id, byte meta)> _out = new();
    readonly List<(int x, int y, int z)> _wantFire = new();
    bool _foreign;   // the overworld shown is a friend's (their changes): never saved into our hero

    /// <summary>Sends what changed since the last tick (batched).</summary>
    void FlushNet(BlockArea a)
    {
        if (!Net.Active) { _out.Clear(); _wantFire.Clear(); return; }
        if (_out.Count > 0) { Net.I!.BlockEdits(a.Key, BlockArea.Pack(_out)); _out.Clear(); }
        if (_wantFire.Count > 0) { var l = new List<(int, int, int, byte, byte)>(); foreach (var (x, y, z) in _wantFire) l.Add((x, y, z, 0, 0)); Net.I!.BlockFire(a.Key, BlockArea.Pack(l)); _wantFire.Clear(); }
    }
    /// <summary>Changes from another machine: applied if we are in that area, otherwise kept for the overworld.</summary>
    public void ApplyRemote(string area, byte[] packed)
    {
        var list = BlockArea.Unpack(packed);
        if (Area != null && Area.Key == area) { foreach (var (x, y, z, id, m) in list) Change(x, y, z, id, m, false); return; }
        if (area == "overworld") { var o = Overworld(); foreach (var (x, y, z, id, m) in list) o.Set(x, y, z, id, m); }
    }
    /// <summary>Joined a friend: their overworld replaces ours on screen (ours stays in our save).</summary>
    public void SyncFromHost(string area, byte[] packed)
    {
        if (area != "overworld") return;
        _foreign = true; bool cur = Area == _over && Area != null;
        if (cur) DropArea();
        _over = new BlockArea("overworld", new OverworldGen());
        foreach (var (x, y, z, id, m) in BlockArea.Unpack(packed)) _over.Set(x, y, z, id, m);
        if (cur) { Area = _over; _center = (int.MinValue, 0); }
    }
    /// <summary>Left a friend's world: back to our own changes.</summary>
    public static void LeftHost()
    {
        var w = I; if (w == null || !w._foreign) return;
        w._foreign = false; w.ReloadOverworld();
    }
    public byte[] OverworldChanges() => BlockArea.Pack(Overworld().AllChanges());

    // the online server has no block world; it only remembers the changes players send, to hand them to newcomers (until it restarts)
    static readonly Dictionary<string, Dictionary<long, (int, int, int, byte, byte)>> _serverDiff = new();
    public static void KeepServerDiff(string area, byte[] packed)
    {
        if (!_serverDiff.TryGetValue(area, out var d)) _serverDiff[area] = d = new();
        foreach (var e in BlockArea.Unpack(packed)) d[BlockArea.BKey(e.x, e.y, e.z)] = e;
    }
    public static byte[] ServerDiff(string area) => _serverDiff.TryGetValue(area, out var d) ? BlockArea.Pack(new List<(int, int, int, byte, byte)>(d.Values)) : System.Array.Empty<byte>();
    /// <summary>What the server knows of a cell: the last change a player sent, or null (the generated world, which the server does not have).</summary>
    public static (byte id, byte meta)? ServerCell(string area, int x, int y, int z)
        => _serverDiff.TryGetValue(area, out var d) && d.TryGetValue(BlockArea.BKey(x, y, z), out var e) ? (e.Item4, e.Item5) : null;
    public static void KeepServerCell(string area, int x, int y, int z, byte id, byte meta)
    {
        if (!_serverDiff.TryGetValue(area, out var d)) _serverDiff[area] = d = new();
        d[BlockArea.BKey(x, y, z)] = (x, y, z, id, meta);
    }
    /// <summary>The cell is as generated again (a rollback): newcomers get no change for it.</summary>
    public static void ServerForget(string area, int x, int y, int z) { if (_serverDiff.TryGetValue(area, out var d)) d.Remove(BlockArea.BKey(x, y, z)); }

    /// <summary>Puts cells back to what the generator makes there (the server refused a change to the generated world, or rolled one back).</summary>
    public void RevertToGen(string area, List<(int x, int y, int z, byte id, byte meta)> cells)
    {
        var a = Area != null && Area.Key == area ? Area : area == "overworld" ? Overworld() : null; if (a == null) return;
        var made = new Dictionary<long, BlockChunk>();
        foreach (var (x, y, z, _, _) in cells)
        {
            int cx = a.CFloor(x), cz = a.CFloor(z); long ck = BlockArea.CKey(cx, cz);
            if (!made.TryGetValue(ck, out var c))
            {
                c = new BlockChunk(cx, cz, a.S, a.Gen.MinY, a.Gen.MaxY);
                try { a.Gen.Fill(c); } catch (System.Exception e) { GD.PushWarning("[BLOCKS] could not regenerate a chunk: " + e.Message); }
                made[ck] = c;
            }
            if (!c.Has(y)) continue;
            int i = c.Index(x - cx * a.S, y, z - cz * a.S); byte id = c.Id[i], meta = c.Meta[i];
            if (a == Area) Change(x, y, z, id, meta, false); else a.Set(x, y, z, id, meta);
        }
    }
}
