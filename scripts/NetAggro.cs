using Godot;

namespace TwinRealms;

/// <summary>Taunts and revives over the network: a taunt on a friend's copy of a monster goes to whoever runs it; a revive goes to the downed hero's game.</summary>
public partial class Net
{
    /// <summary>A taunt on a puppet: the overworld host or the party dungeon's owner applies it, from us.</summary>
    public void SendTaunt(Enemy e, float strength, float dur)
    {
        if (e.FromDungeon) { if (Dungeon.Shared && !Dungeon.IsOwner) RpcId(Dungeon.Owner, MethodName.R_DunTaunt, Dungeon.Seed, e.NetIdx, strength, dur); return; }
        if (e.DuelPeer == 0 && e.NetIdx >= 0) RpcId(1, MethodName.R_EnemyTaunt, e.NetIdx, strength, dur);
    }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    void R_EnemyTaunt(int idx, float strength, float dur) { if (_server && World.NetGet(idx) is Enemy e) e.Taunt(Sender, strength, Mathf.Min(dur, 30f)); }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    void R_DunTaunt(int seed, int idx, float strength, float dur)
    {
        if (!Dungeon.IsOwner || seed != Dungeon.Seed || idx < 0 || idx >= Dungeon.NetList.Count) return;
        var e = Dungeon.NetList[idx]; if (e != null && IsInstanceValid(e)) e.Taunt(Sender, strength, Mathf.Min(dur, 30f));
    }

    /// <summary>An enchantment (party aura skill) reaches a friend: their game applies it to their hero.</summary>
    public void SendEnchant(long peer, string id, float pot, float dur) { if (peer != MyId) RpcId(peer, MethodName.R_Enchant, id, pot, dur); }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    void R_Enchant(string id, float pot, float dur) { Player.Instance?.ApplyEnchant(id, Mathf.Clamp(pot, 0, 3), Mathf.Clamp(dur, 0, 60), NameOf(Sender)); Count("enchants"); }

    /// <summary>Raises a downed friend: hp &lt;= 0 = full health (the Resurrection spell), otherwise that much health (given from our own).</summary>
    public void ReviveRemote(long peer, float hp) { if (peer == MyId) return; RpcId(peer, MethodName.R_Revive, hp); }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    void R_Revive(float hp)
    {
        var p = Player.Instance; if (p == null || !p.Dead) return;
        p.ReviveHere(hp > 0 ? hp : Game.D.MaxHp());
        Ui.Note($"{NameOf(Sender)} brought you back", new Color(.6f, 1f, .7f)); Count("revives");
    }
}
