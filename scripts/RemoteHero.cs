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
    string _title = ""; float _featherT;   // their worn title (Titles.cs) and the feather trail timer
    HeroRig _rig = null!; SwordTrail _trail = null!; Label3D _tag = null!; Node3D _horse = null!;
    float[]? _p; Vector3 _to; string _lookSig = "", _gearSig = "";
    HandPose _r, _l; float _yaw;

    public bool HasPose => _p != null;
    public bool Dead => _p != null && _p[31] > .5f;
    public int Zone => _p == null ? -1 : (int)_p[32];
    public float Hp => _p?[6] ?? 1; public float MaxHp => _p?[7] ?? 1;
    public string PlayerName => Net.I?.Players.GetValueOrDefault(Peer)?.Name ?? "Friend";

    public string Prompt => Revivable ? $"Revive {PlayerName} (hold still 15 s; gives them half your health)" : "Meet " + PlayerName + " (party, duel)";
    public bool CanInteract => Net.Active && Visible && (!Dead || Revivable);
    /// <summary>Downed in a party lair, in our party, on our floor: we can kneel and share our life (PlayerDowned.cs).</summary>
    bool Revivable => Dead && Player.LairRules && Zone == Net.MyZone && Net.I!.SameParty(Net.MyId, Peer) && Player.Instance is { Dead: false, Channeling: false };
    public void Interact() { if (Revivable) Player.Instance!.BeginRevive(Peer, PlayerName); else Ui.I?.OpenPlayerMenu(Peer); }
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
        var ps = new PlayerStats { Name = p.Name, Race = p.Race, Class = p.Class, Body = p.Body, Eq = new System.Collections.Generic.Dictionary<string, Item?>(p.Eq) };
        string look = p.Race + "|" + p.Class + "|" + p.Body;
        if (look != _lookSig)
        {
            if (_lookSig != "") { _rig.QueueFree(); _rig = new HeroRig { Name = "Model2" }; AddChild(_rig); _av = null; }   // a rig is built once, so a new look gets a new rig
            _lookSig = look; _gearSig = ""; var ap = Races.Look(ps); _rig.Build(ap);
            if (Avatar.Available) { _av = new Avatar { Name = "Avatar", Scale = new Vector3(1 / ap.Bulk, 1 / ap.Height, 1 / ap.Bulk) }; _rig.AddChild(_av); _av.Build(ap, p.Name); }
            _tag.Position = new Vector3(0, 2.05f * ap.Height + .1f, 0);   // just above the head, for a tall elf or a short dwarf
        }
        string gear = ps.WType(); foreach (var s in PlayerStats.Slots) { var it = ps.Eq.GetValueOrDefault(s); gear += "|" + (it == null ? "-" : it.Name + it.Rar + it.Up + it.Glow); }
        if (gear != _gearSig)
        {
            _gearSig = gear; _rig.ApplyGear(ps); _trail.Base = _rig.BladeBase; _trail.Tip = _rig.BladeTip;
            if (_av != null)
            {
                void H(Node n) { foreach (var c in n.GetChildren()) { if (c == _av) continue; if (c is GeometryInstance3D g) g.Visible = false; H(c); } } H(_rig);
                _av.Dress(s => ps.Eq.GetValueOrDefault(s), ps.WType(), Classes.Get(ps.Class).Tunic); _wt = ps.WType();
                if (_av.BladeTip != null) { _trail.Base = _av.BladeBase; _trail.Tip = _av.BladeTip; }
            }
        }
        bool party = Net.I != null && Net.I.SameParty(Net.MyId, Peer), duel = Net.I?.Duel is { } d && (d.A == Peer || d.B == Peer);
        _tag.Text = $"{Titles.WithTitle(p.Name, p.Title)}  Lv {p.Level}" + (party ? "  [party]" : ""); _title = p.Title;
        _tag.Modulate = duel ? new Color(1f, .55f, .4f) : party ? new Color(.55f, 1f, .6f) : new Color(.75f, .9f, 1f);
        SetPet(p.Pet);
    }

    // ---- the friend's companion: their pet's model (an Enemy PetBody, no AI) trotting behind them; from PeerInfo.Pet "species|level|nick"
    string _petSig = ""; Node3D? _pet; Enemy? _petBody; Label3D? _petTag; float _petYaw, _petSide = 1;
    void SetPet(string sig)
    {
        if (sig == _petSig) return; _petSig = sig;
        if (_pet != null && IsInstanceValid(_pet)) _pet.QueueFree(); _pet = null; _petBody = null; _petTag = null;
        var parts = sig.Split('|'); if (sig == "" || parts.Length < 2) return;
        string species = parts[0]; int.TryParse(parts[1], out int lvl); string nick = parts.Length > 2 ? parts[2] : "";
        var sp = PetDb.Species(species);
        _pet = new Node3D { Name = "FriendPet", TopLevel = true }; AddChild(_pet); _pet.GlobalPosition = GlobalPosition - Basis.Z * -2f;
        var vis = new Node3D { Scale = Vector3.One * sp.PetScale }; _pet.AddChild(vis);
        _petBody = new Enemy { Type = species, PetBody = true, Level = Mathf.Max(1, lvl), AnyRealm = true }; vis.AddChild(_petBody);
        _petTag = new Label3D { Text = $"{(nick != "" ? nick : sp.Name)}  Lv{lvl}", FontSize = 22, OutlineSize = 5, PixelSize = .005f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Modulate = new Color(.65f, .95f, .7f), VisibilityRangeEnd = 30 };
        _pet.AddChild(_petTag); _petTag.Position = new Vector3(0, Mathf.Max(.8f, _petBody.HeightM() * sp.PetScale) + .35f, 0);
        _petSide = (Peer & 1) == 0 ? 1 : -1;
    }
    void PetFollow(float dt)
    {
        if (_pet == null || !IsInstanceValid(_pet) || _petBody == null) return;
        _pet.Visible = Visible && !Dead && !(_p != null && _p[30] > .5f);
        if (!_pet.Visible) return;
        var f = new Vector3(-Mathf.Sin(_yaw), 0, -Mathf.Cos(_yaw)); var side = f.Cross(Vector3.Up) * _petSide;
        var goal = GlobalPosition - f * 2.6f + side * 1.4f; goal.Y = World.GroundAt(goal.X, goal.Z);
        if (Mathf.Abs(goal.Y - GlobalPosition.Y) > 3f) goal.Y = GlobalPosition.Y;   // a bridge, a dungeon floor: stay at the hero's height
        var at = _pet.GlobalPosition; var to = goal - at; float dist = new Vector2(to.X, to.Z).Length();
        if (dist > 25f) { _pet.GlobalPosition = goal; return; }
        float sp = dist > .6f ? Mathf.Min(dist * 3f, 11f) : 0;
        var mv = dist > .01f ? new Vector3(to.X, 0, to.Z) / dist * sp : Vector3.Zero;
        _pet.GlobalPosition = new Vector3(at.X + mv.X * dt, Mathf.Lerp(at.Y, goal.Y, 1f - Mathf.Exp(-10f * dt)), at.Z + mv.Z * dt);
        var face = sp > .1f ? mv : f; _petYaw = Mathf.LerpAngle(_petYaw, Util.YawTo(face.X, face.Z), 1f - Mathf.Exp(-8f * dt));
        _pet.Rotation = new Vector3(0, _petYaw, 0);
        _petBody.PetVel = mv / Mathf.Max(.25f, PetDb.Species(_petBody.Type).PetScale);
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
        if (_pet != null && IsInstanceValid(_pet) && !show) _pet.Visible = false;
        if (!show || _p == null) return;
        PetFollow(dt);
        var p = _p; float k = 1f - Mathf.Exp(-14f * dt);
        var before = GlobalPosition; GlobalPosition = GlobalPosition.Lerp(_to, k);
        if (Titles.Get(_title)?.Feathers == true && !Dead) FeatherTrail.Tick(ref _featherT, GlobalPosition, new Vector2(GlobalPosition.X - before.X, GlobalPosition.Z - before.Z).Length() / Mathf.Max(dt, .001f), dt);   // Felled Angel
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
        if (_av != null) AnimateAvatar(dt, p);
        ShowStatuses(p.Length > 35 ? p[35] : 0);
    }

    // ---- the friend's statuses: a coloured letter each, in a row above the name (Settings > Game > Friends' status icons)
    readonly List<Label3D> _icons = new(); float _mask = -1; readonly StatusSet _st = new();
    /// <summary>How many status icons are showing (the multiplayer bot checks it).</summary>
    public int StatusIcons => _st.List.Count;
    void ShowStatuses(float mask)
    {
        if (!Game.Set.OthersStatus) mask = 0;
        if (mask == _mask) return; _mask = mask; Dmg.FromMask(mask, _st);
        while (_icons.Count < _st.List.Count)
        {
            var l = new Label3D { FontSize = 34, OutlineSize = 9, PixelSize = .005f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = false, VisibilityRangeEnd = 45f };
            AddChild(l); _icons.Add(l);
        }
        float w = .2f, x0 = -(_st.List.Count - 1) * w / 2f;
        for (int i = 0; i < _icons.Count; i++)
        {
            var l = _icons[i]; l.Visible = i < _st.List.Count; if (!l.Visible) continue;
            var d = _st.List[i].Def; l.Text = d.Name[..1].ToUpper(); l.Modulate = d.Col.Lightened(.15f);
            l.Position = _tag.Position + new Vector3(x0 + i * w, .22f, 0);
        }
    }

    // the model is animated from what the pose packet says: movement, swing (trail on), roll, mount, death
    Avatar? _av; string _wt = "sword"; Vector3 _lastPos; bool _swinging; int _swing, _atkSerial = -1;
    void AnimateAvatar(float dt, float[] p)
    {
        var a = _av!; float sp = (GlobalPosition - _lastPos).Length() / Mathf.Max(dt, .001f); _lastPos = GlobalPosition;
        _rig.Rotation = new Vector3(0, _yaw, 0); _rig.Position = new Vector3(0, p[30] > .5f ? .9f : 0f, 0);
        bool swing = p[26] > .5f; bool start = swing && !_swinging; _swinging = swing;
        if (p[31] > .5f) { a.Play("die", .15f); return; }
        if (p[30] > .5f) { a.Play("sit"); return; }
        if (Mathf.Abs(p[3]) > .2f) { a.Play("roll", .06f); return; }
        // the friend's own attack animation for this swing (each chain step looks different), started when a new swing begins
        if (p.Length > 38)
        {
            int serial = (int)p[38]; bool newSwing = _atkSerial >= 0 && serial != _atkSerial; _atkSerial = serial;
            if (newSwing && p[36] > 0 && a.PlayAttack(Avatar.AttackIdAt((int)p[36]), Mathf.Max(.05f, p[37]), true)) { _swinging = swing; return; }
            if (a.Cur.StartsWith("atk_") && a.Busy) { _swinging = swing; return; }
        }
        if (start) { _swing++; a.Play(Weapons.Get(_wt).Hands > 1 ? "heavy" : "melee", .06f, .45f, true, _swing); return; }
        if (a.Busy) return;
        a.Move(sp, true, true, false);
    }
}
