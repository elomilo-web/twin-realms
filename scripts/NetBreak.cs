using Godot;

namespace TwinRealms;

/// <summary>
/// A-37: breakable props in multiplayer (Breakables.cs). The server (the online server, or a friend host) owns which shared props are broken
/// (BreakServer: TR_DATA/breakables.json online, the host hero's save when hosting). Messages are small:
///   client -> server  R_BreakAsk(id)            "I broke prop id" (rate limited)
///   server -> asker   R_BreakGrant(id, seed)     the first asker only: their game rolls the loot from data/breakables.json with this seed
///   server -> others  R_BreakSet(id, true)       it is broken (also the answer to a late asker: no loot)
///   server -> all     R_BreakSet(id, false)      its respawn time is up
///   server -> joiner  R_BreakSync(int[] ids)     everything broken now (from R_Hello)
/// The client hides the prop at once and plays the break; the loot only comes with the grant, so two players never both loot one prop.
/// </summary>
public partial class Net
{
    public void BreakAsk(int id) { if (Active && !_server) RpcId(1, MethodName.R_BreakAsk, id); }
    /// <summary>Server: tell every other player a prop broke or came back.</summary>
    public void BreakBroadcast(int id, bool broken, long except = 0)
    {
        if (!_server || !Active) return;
        foreach (var p in Players.Keys) if (p != MyId && p != except) RpcId(p, MethodName.R_BreakSet, id, broken);
    }
    /// <summary>Server: a player got in; send the props broken now.</summary>
    public void SendBreakSync(long peer) { if (_server) RpcId(peer, MethodName.R_BreakSync, Breakables.BrokenIds()); }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void R_BreakAsk(int id)
    {
        if (!_server) return; long s = Multiplayer.GetRemoteSenderId();
        if (Dedicated && !_acct.ContainsKey(s)) return; if (!Players.ContainsKey(s)) return;
        var (ok, seed, why) = Breakables.Auth.Ask(s, id, Breakables.Now(), Time.GetTicksMsec() / 1000.0); Count("breakAsks");
        if (!ok)
        {
            if (why == "already broken") RpcId(s, MethodName.R_BreakSet, id, true);
            return;
        }
        Breakables.HostSaw(id);
        RpcId(s, MethodName.R_BreakGrant, id, seed); Count("breakGrants");
        BreakBroadcast(id, true, s);
        if (Dedicated && Breakables.ById.TryGetValue(id, out var r)) GD.Print($"[SERVER] {NameOf(s)} broke {r.Def.Key} {id}");
    }
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void R_BreakGrant(int id, int seed) { if (!_server) Breakables.Granted(id, seed); }
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void R_BreakSet(int id, bool broken) { if (!_server) Breakables.RemoteSet(id, broken); }
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void R_BreakSync(int[] ids) { if (!_server) Breakables.RemoteSync(ids); }
}
