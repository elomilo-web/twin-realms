using Godot;

namespace TwinRealms;

/// <summary>The hero's side of multiplayer: the pose packet friends draw us from, and the duel safety net.</summary>
public partial class Player
{
    HeroRig.LegPose _nLegs; float _nTwist, _nLean; HandPose _nR, _nL = new() { H = new Vector3(-.42f, .97f, -.12f), Pitch = -60 };

    /// <summary>
    /// 36 numbers, 20 times a second: position, body rotation and height, health, the leg/spine/hand pose the animation just produced,
    /// cape, swing trail, mount, death, zone and level. Friends feed these straight into the same HeroRig, so they see our exact animation.
    /// </summary>
    public float[] NetPacket()
    {
        var p = GlobalPosition; var mr = _model.Rotation; var tc = _trail.Col;
        return new float[]
        {
            p.X, p.Y, p.Z, mr.X, mr.Y, _model.Position.Y, Hp, Game.D.MaxHp(),
            _nLegs.SwingR, _nLegs.SwingL, _nLegs.KneeR, _nLegs.KneeL, _nLegs.Splay, _nTwist, _nLean,
            _nR.H.X, _nR.H.Y, _nR.H.Z, _nR.Yaw, _nR.Pitch, _nL.H.X, _nL.H.Y, _nL.H.Z, _nL.Yaw, _nL.Pitch,
            _capeX, _trail.Emit ? 1 : 0, tc.R, tc.G, tc.B, Mounted ? 1 : 0, Dead ? 1 : 0, Net.MyZone, _rig.SteadyLeft ? 1 : 0, Game.D.Level,
            Dmg.Mask(Statuses),   // 35: our statuses, drawn above our name on friends' screens
        };
    }

    /// <summary>In a duel nobody dies: falling to zero leaves us at 1 health and hands the win to the opponent.</summary>
    bool DuelSave()
    {
        if (!Net.InLiveDuel) return false;
        Hp = 1; _inv = 1.5f; Statuses.Clear(); Net.I!.LoseDuel(); return true;
    }
}
