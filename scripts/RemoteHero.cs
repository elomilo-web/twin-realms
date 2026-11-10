using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>
/// A friend's hero as we see it: the same HeroRig (race look, class colours, every equipped piece of gear), posed from their
/// pose packets and smoothed between them. Walk up and press E to invite them to a party or challenge them to a duel.
/// On the host, enemies chase and strike remote heroes too; those hits are sent to the friend's game, which applies them.
/// </summary>
public partial class RemoteHero : Node3D, IDamageable, IInteractable
{
    public long Peer;
    HeroRig _rig = null!; SwordTrail _trail = null!; Label3D _tag = null!; Node3D _horse = null!;
    float[]? _p; Vector3 _to; string _lookSig = "", _gearSig = "";
    HandPose _r, _l; float _yaw;

    public bool HasPose => _p != null;
    public bool Dead => _p != null && _p[31] > .5f;
    public int Zone => _p == null ? -1 : (int)_p[32];
    public float Hp => _p?[6] ?? 1; public float MaxHp => _p?[7] ?? 1;
    public string PlayerName => Net.I?.Players.GetValueOrDefault(Peer)?.Name ?? "Friend";

    public string Prompt => "Meet " + PlayerName + " (party, duel)";
    public bool CanInteract => Net.Active && !Dead && Visible;
    public void Interact() => Ui.I?.OpenPlayerMenu(Peer);
    /// <summary>Only the host's enemies call this; the friend's own game applies the hit to their hero.</summary>
    public void TakeHit(HitInfo h)
    {
        if (Dungeon.IsOwner && Zone == Net.MyZone) Net.I!.HitPeerDun(Peer, h);   // our party dungeon's monsters hit a friend on this floor
        else if (Net.IsHost && Zone == 0) Net.I!.HitPeer(Peer, h);
    }

    public override void _Ready()
    {
        AddToGroup("interact"); AddToGroup("remote_heroes"); Visible = false;
        _rig = new HeroRig { Name = "Model" }; AddChild(_rig);
        _trail = new SwordTrail(); AddChild(_trail);
        _tag = new Label3D { FontSize = 30, OutlineSize = 8, PixelSize = .005f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = new Vector3(0, 2.35f, 0), NoDepthTest = false,
            VisibilityRangeEnd = 60f, VisibilityRangeEndMargin = 6f, VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self };
        AddChild(_tag);
        _horse = new Node3D { Visible = false }; AddChild(_horse);
        var hm = new StandardMaterial3D { AlbedoColor = new Color(0.4f, 0.26f, 0.16f) };
        _horse.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.7f, 0.8f, 2.0f) }, MaterialOverride = hm, Position = new Vector3(0, 1.0f, 0) });
        _horse.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.35f, 0.7f, 0.9f) }, MaterialOverride = hm, Position = new Vector3(0, 1.5f, -1.2f), Rotation = new Vector3(-0.5f, 0, 0) });
        for (int i = 0; i < 4; i++) _horse.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.18f, 0.9f, 0.18f) }, MaterialOverride = hm, Position = new Vector3(i % 2 == 0 ? 0.25f : -0.25f, 0.45f, i < 2 ? -0.8f : 0.8f) });
        if (Net.I?.Players.GetValueOrDefault(Peer) is PeerInfo pi) Refresh(pi);
    }

    /// <summary>New roster info: rebuild the body if race or class changed, the gear if any equipped piece changed, and the name tag.</summary>
    public void Refresh(PeerInfo p)
    {
        if (_rig == null || !IsInsideTree()) return;
        var ps = new PlayerStats { Name = p.Name, Race = p.Race, Class = p.Class, Eq = new System.Collections.Generic.Dictionary<string, Item?>(p.Eq) };
        string look = p.Race + "|" + p.Class;
        if (look != _lookSig)
        {
            if (_lookSig != "") { _rig.QueueFree(); _rig = new HeroRig { Name = "Model2" }; AddChild(_rig); }   // a rig is built once, so a new look gets a new rig
            _lookSig = look; _gearSig = ""; var ap = Races.Look(ps); _rig.Build(ap);
            _tag.Position = new Vector3(0, 2.05f * ap.Height + .1f, 0);   // just above the head, for a tall elf or a short dwarf
        }
        string gear = ps.WType(); foreach (var s in PlayerStats.Slots) { var it = ps.Eq.GetValueOrDefault(s); gear += "|" + (it == null ? "-" : it.Name + it.Rar + it.Up); }
        if (gear != _gearSig) { _gearSig = gear; _rig.ApplyGear(ps); _trail.Base = _rig.BladeBase; _trail.Tip = _rig.BladeTip; }
        bool party = Net.I != null && Net.I.SameParty(Net.MyId, Peer), duel = Net.I?.Duel is { } d && (d.A == Peer || d.B == Peer);
        _tag.Text = $"{p.Name}  Lv {p.Level}" + (party ? "  [party]" : "");
        _tag.Modulate = duel ? new Color(1f, .55f, .4f) : party ? new Color(.55f, 1f, .6f) : new Color(.75f, .9f, 1f);
    }

    public void Push(float[] p)
    {
        _p = p; _to = new Vector3(p[0], p[1], p[2]);
        if (!Visible || GlobalPosition.DistanceTo(_to) > 12f) { GlobalPosition = _to; _yaw = p[4]; }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        // drawn when we are both in the overworld or on the same floor of our party's dungeon (private dungeons and the arena are solo)
        bool show = _p != null && Zone == Net.MyZone && (Zone == 0 || Zone >= 1000);
        if (Visible != show) Visible = show;
        if (!show || _p == null) return;
        var p = _p; float k = 1f - Mathf.Exp(-14f * dt);
        GlobalPosition = GlobalPosition.Lerp(_to, k);
        _yaw = Mathf.LerpAngle(_yaw, p[4], k);
        _rig.Rotation = new Vector3(Mathf.Lerp(_rig.Rotation.X, p[3], k), _yaw, 0);
        _rig.Position = new Vector3(0, p[5], 0);
        var legs = new HeroRig.LegPose { SwingR = p[8], SwingL = p[9], KneeR = p[10], KneeL = p[11], Splay = p[12] };
        var r = new HandPose { H = new Vector3(p[15], p[16], p[17]), Yaw = p[18], Pitch = p[19] };
        var l = new HandPose { H = new Vector3(p[20], p[21], p[22]), Yaw = p[23], Pitch = p[24] };
        float hk = 1f - Mathf.Exp(-25f * dt); _r = HandPose.Lerp(_r, r, hk); _l = HandPose.Lerp(_l, l, hk);
        if (_r.H == Vector3.Zero) { _r = r; _l = l; }
        _rig.SteadyLeft = p[33] > .5f;
        _rig.Pose(dt, legs, p[13], p[14], _r, _l, null);
        _rig.Cape.Rotation = new Vector3(p[25] + p[14], 0, 0);
        _trail.Emit = p[26] > .5f; _trail.Col = new Color(p[27], p[28], p[29]);
        _horse.Visible = p[30] > .5f; _horse.Rotation = new Vector3(0, _yaw, 0);
    }
}
