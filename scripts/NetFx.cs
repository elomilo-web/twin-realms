using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>
/// Multiplayer extras: ambushes the host spawns (shared like every overworld monster), and what players see of each other:
/// spell and skill effects, damage and healing numbers. Each can be turned off per player (Settings > Game).
/// </summary>
public partial class Net
{
    // ================================================================== ambushes
    /// <summary>Host: an ambusher appeared; every friend in the world gets a copy with the same network number.</summary>
    public void AmbushSpawn(Enemy e) { foreach (var id in Players.Keys) if (id != MyId) RpcId(id, MethodName.R_Ambush, e.NetIdx, e.Type, e.Level, e.GlobalPosition); }
    [Rpc(MultiplayerApi.RpcMode.Authority)]
    void R_Ambush(int idx, string type, int lvl, Vector3 pos)
    {
        if (World.I == null || World.NetExtra.ContainsKey(idx)) return;
        var e = new Enemy { Type = type, Level = lvl, Ambusher = true, NoLeash = true, Puppet = true, NetIdx = idx, RespawnTime = 0, Position = pos };
        World.NetExtra[idx] = e; World.I.Over.AddChild(e); Count("ambushes");
    }
    /// <summary>Client, once our world exists: ask the host for the ambushers already roaming (we were building the world when they came).</summary>
    bool _askedAmbush;
    void AskAmbushes() { if (_askedAmbush || _server || World.I == null) return; _askedAmbush = true; RpcId(1, MethodName.R_WantAmbushes); }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    void R_WantAmbushes()
    {
        if (!_server) return; long to = Sender;
        foreach (var (i, e) in World.NetExtra) if (IsInstanceValid(e) && !e.Dead) RpcId(to, MethodName.R_Ambush, i, e.Type, e.Level, e.GlobalPosition);
    }
    /// <summary>Host: tell one player something (their game shows it as a note).</summary>
    public void NoteTo(long peer, string text) { if (peer == MyId) Ui.Note(text, new Color(1f, .5f, .35f)); else RpcId(peer, MethodName.R_Note, text); }

    // ================================================================== spell and skill effects
    /// <summary>We played a skill effect: friends in the same zone play it too (attached to our hero when it follows us).</summary>
    public void Fx(string skill, string moment, Vector3 pos, float scale, float life, bool onMe)
    {
        if (!Active) return; Rpc(MethodName.R_Fx, skill, moment, pos, scale, life, onMe, MyZone);
    }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable, TransferChannel = 5)]
    void R_Fx(string skill, string moment, Vector3 pos, float scale, float life, bool onMe, int zone)
    {
        if (!Game.Set.OthersFx || zone != MyZone || (zone != 0 && zone < 1000) || Player.Instance == null) return;
        // onMe: pos is the offset from the caster's hero
        RemoteHero? h = onMe && Heroes.TryGetValue(Sender, out var rh) && IsInstanceValid(rh) && rh.Visible ? rh : null;
        if (onMe && h == null) return;
        if ((h?.GlobalPosition ?? pos).DistanceTo(Player.Instance.GlobalPosition) > 90f) return;
        SpellFx.Remote = true; Count("fxIn");
        try
        {
            if (h != null) SpellFx.SkillAttach(skill, moment, h, pos, scale, life);
            else SpellFx.Skill(skill, moment, pos, scale, life);
        }
        finally { SpellFx.Remote = false; }
    }

    // ================================================================== damage and healing numbers
    float _numT; int _numN;
    /// <summary>A number we showed (our hit, a hit we took, a heal): friends nearby see it too, at most ~25 a second.</summary>
    public void Number(Vector3 pos, string text, Color col, float size)
    {
        if (!Active) return;
        float now = World.Clock; if (now - _numT > 1f) { _numT = now; _numN = 0; } if (++_numN > 25) return;
        Rpc(MethodName.R_Number, pos, text, col, size, MyZone);
    }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable, TransferChannel = 5)]
    void R_Number(Vector3 pos, string text, Color col, float size, int zone)
    {
        if (!Game.Set.OthersNumbers || zone != MyZone || (zone != 0 && zone < 1000) || Player.Instance == null || Game.Root == null) return;
        if (Player.Instance.GlobalPosition.DistanceTo(pos) > 60f) return;
        FloatText.SpawnRemote(Game.Root, pos, text, col with { A = .85f }, size * .9f); Count("numbersIn");   // a little softer than our own
    }
}
