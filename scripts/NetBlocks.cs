using System.Collections.Generic;
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
        // F-18/F-44: the host rejects edits inside a protected town zone (unless the sender is an admin) and tells the sender the real blocks
        long sender = Multiplayer.GetRemoteSenderId();
        if (area == "overworld" && !Admin.IsPeerAdmin(sender))
        {
            var keep = new List<(int x, int y, int z, byte id, byte meta)>(); var back = new List<(int x, int y, int z, byte id, byte meta)>();
            foreach (var e in BlockArea.Unpack(packed))
            {
                if (TownZones.AtBlock(e.x, e.z) is not { } zone) { keep.Add(e); continue; }
                GD.Print($"[town] rejected block edit from peer {sender} at {e.x},{e.y},{e.z} (id {e.id}) inside {zone.Name}");
                if (BlockWorld.I?.Overworld() is { } ow) back.Add((e.x, e.y, e.z, ow.Get(e.x, e.y, e.z), ow.GetMeta(e.x, e.y, e.z)));
            }
            if (back.Count > 0) RpcId(sender, MethodName.R_Blocks, area, BlockArea.Pack(back));
            if (keep.Count != BlockArea.Unpack(packed).Count) { if (keep.Count == 0) return; packed = BlockArea.Pack(keep); }
        }
        BlockWorld.I?.ApplyRemote(area, packed);
        if (Dedicated) BlockWorld.KeepServerDiff(area, packed);
        Rpc(MethodName.R_Blocks, area, packed);   // to everyone (the sender re-applies the same values: no harm)
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
}
