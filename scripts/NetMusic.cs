using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>
/// Focus F-40: instrument notes between players. One small unreliable message per note, like the shared spell effects and damage
/// numbers (NetFx.cs, channel 5): the instrument's index, the key and the octave packed into one number, plus the sender's zone.
/// The receiver plays it at the sender's hero (Instruments.PlayRemote), if that hero is shown and within hearing range.
/// Limits: the sender drops notes past data/instruments.json net.perSecond; the receiver drops a peer's notes past twice that.
/// </summary>
public partial class Net
{
    float _tuneT; int _tuneN; public int TunesSent;
    readonly Dictionary<long, (float t, int n)> _tuneIn = new();

    /// <summary>instrument index (0..63), key (0..15) and octave (0 / 1) in one number.</summary>
    public static int PackTune(int instr, int note, bool high) => (instr & 63) | ((note & 15) << 6) | (high ? 1 << 10 : 0);

    /// <summary>We played a note: friends in the same shared zone hear it at our hero.</summary>
    public void Tune(int instr, int note, bool high)
    {
        if (!Active) return;
        float now = World.Clock; if (now - _tuneT > 1f || now < _tuneT) { _tuneT = now; _tuneN = 0; }
        if (++_tuneN > InstrumentDb.NetPerSecond) return;
        TunesSent++; Rpc(MethodName.R_Tune, PackTune(instr, note, high), MyZone);
    }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable, TransferChannel = 5)]
    void R_Tune(int packed, int zone) => TuneIn(Sender, packed, zone);

    /// <summary>A note from a peer (also called by the dev command "play remote", with our own id: it then sounds at our hero).</summary>
    public void TuneIn(long from, int packed, int zone)
    {
        if (Instruments.I == null || zone != MyZone || (zone != 0 && zone < 1000) || Player.Instance == null) return;   // (a dedicated server has no Instruments node)
        float now = World.Clock; var (t, n) = _tuneIn.GetValueOrDefault(from, (now, 0));
        if (now - t > 1f || now < t) { t = now; n = 0; }
        _tuneIn[from] = (t, ++n); if (n > InstrumentDb.NetPerSecond * 2) return;
        Node3D? at = from == MyId ? Player.Instance : Heroes.TryGetValue(from, out var rh) && IsInstanceValid(rh) && rh.Visible ? rh : null;
        if (at == null || at.GlobalPosition.DistanceTo(Player.Instance.GlobalPosition) > InstrumentDb.HearRange) return;
        Instruments.I.PlayRemote(at, packed & 63, (packed >> 6) & 15, (packed & (1 << 10)) != 0); Count("tunesIn");
    }
}
