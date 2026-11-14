using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>
/// Raid rules for the hero (party lairs, DungeonLair.cs):
///  - falling in a party lair does not send you out: you lie downed until a party member raises you, the whole party falls,
///    or you give up (Interact);
///  - Resurrection (skill kind "resurrect"): channel 15 s standing still; it raises every downed party member on the floor at full
///    health. Moving breaks it and refunds it; its 30 s cooldown starts only when it goes off;
///  - anyone can kneel by a downed party member (Interact) and channel 15 s: half of the reviver's health goes to them. No cooldown.
/// Also the healing-received multiplier ("healIn", lowered by Challenging Roar).
/// </summary>
public partial class Player
{
    public float ChannelT, ChannelDur; public string ChannelName = ""; string _chanKind = "", _chanSkill = ""; long _chanPeer; float _chanCd, _wipeT;
    /// <summary>For the HUD: 0..1 while channelling, -1 otherwise.</summary>
    public float ChannelFrac => ChannelT > 0 && ChannelDur > 0 ? 1 - ChannelT / ChannelDur : -1;
    public bool Channeling => ChannelT > 0;
    /// <summary>Healing received multiplier (data stat "healIn", e.g. -35 = 35% less).</summary>
    public static float HealIn => Mathf.Max(0, 1 + Game.TS["healIn"] / 100f);
    /// <summary>A party lair: the fallen stay down and can be raised.</summary>
    public static bool LairRules => Dungeon.Active && Dungeon.IsLair(Dungeon.Key) && Dungeon.Shared;
    public bool Downed => Dead && LairRules;

    /// <summary>Party members on this floor who lie downed.</summary>
    public static List<long> DownedAllies()
    {
        var l = new List<long>(); var net = Net.I; if (!Net.Active || net == null) return l;
        foreach (var kv in net.Heroes) { var h = kv.Value; if (IsInstanceValid(h) && h.HasPose && h.Dead && h.Zone == Net.MyZone && net.SameParty(Net.MyId, kv.Key)) l.Add(kv.Key); }
        return l;
    }
    /// <summary>Kneel by a downed friend and channel: they get half of our health when it completes.</summary>
    public void BeginRevive(long peer, string name)
    {
        if (Dead || Channeling) return;
        StartChannel("revive", Tune.F("raid.reviveTime", 15f), "Reviving " + name, peer);
    }
    void StartChannel(string kind, float dur, string name, long peer = 0, string skill = "")
    {
        _chanKind = kind; ChannelT = ChannelDur = dur; ChannelName = name; _chanPeer = peer; _chanSkill = skill;
        Ui.Note($"{name}: hold still for {dur:0} s", new Color(.7f, 1f, .8f));
    }
    /// <summary>One frame of a channel. Moving, jumping, rolling or attacking breaks it (a spell is refunded). True while it runs (the hero stands still).</summary>
    bool ChannelTick(float dt, Vector3 dir, bool captured)
    {
        if (ChannelT <= 0) return false;
        bool moved = dir.LengthSquared() > .01f || (captured && (Input.IsActionJustPressed(Inputs.Jump) || Input.IsActionJustPressed(Inputs.Roll) || Input.IsActionJustPressed(Inputs.Attack) || Input.IsActionJustPressed(Inputs.Heavy)));
        if (moved) { CancelChannel("You moved: the channel breaks" + (_chanKind == "res" ? " (refunded)" : "")); return false; }
        if (_chanKind == "revive" && (Net.I?.HeroOf(_chanPeer) is not { Dead: true } h || h.GlobalPosition.DistanceTo(GlobalPosition) > 4f)) { CancelChannel("They are no longer there to revive"); return false; }
        ChannelT -= dt;
        if ((int)(ChannelT * 2) != (int)((ChannelT + dt) * 2)) Fx.Burst(GlobalPosition + Vector3.Up * 1.2f, new Color(.7f, 1f, .8f), 4, 1.5f, .5f, .08f);
        if (ChannelT <= 0) FinishChannel();
        return true;
    }
    public void CancelChannel(string why = "")
    {
        if (ChannelT <= 0) return;
        if (_chanKind == "res") CancelCast();   // the mana comes back and no cooldown starts
        ChannelT = 0; _chanKind = "";
        if (why != "") Ui.Note(why, new Color(1f, .7f, .5f));
    }
    void FinishChannel()
    {
        string kind = _chanKind; ChannelT = 0; _chanKind = "";
        if (kind == "res")
        {
            CastWentOff(); StartCd(_chanSkill, _chanCd);
            var down = DownedAllies(); foreach (var peer in down) Net.I?.ReviveRemote(peer, 0);
            Vfx.Ring(GlobalPosition + Vector3.Up * .2f, 40f, new Color(1f, .95f, .6f), 1f); Vfx.Flash(GlobalPosition + Vector3.Up * 2, new Color(1f, .95f, .7f), 6f, 30f, .6f); Sfx.Play("heal");
            Ui.Note(down.Count > 0 ? $"Resurrection raises {down.Count} fallen" : "Resurrection: nobody was left to raise", new Color(1f, .95f, .6f));
        }
        else if (kind == "revive")
        {
            if (Net.I?.HeroOf(_chanPeer) is not { Dead: true }) return;
            float half = Mathf.Floor(Hp / 2f); if (half < 1) { Ui.Note("Too weak to share your life", new Color(1f, .6f, .5f)); return; }
            Hp -= half; Net.I.ReviveRemote(_chanPeer, half); Sfx.Play("heal");
            FloatText.Spawn(this, GlobalPosition + new Vector3(0, 2.2f, 0), "-" + (int)half, new Color(.6f, 1f, .7f), .7f);
        }
    }
    /// <summary>Resurrection cast from the hotbar: pays now, starts the channel; the cooldown waits for the end.</summary>
    bool TryResurrect(string id, SkillCalc c)
    {
        if (DownedAllies().Count == 0) { Ui.Note("No fallen allies to raise", new Color(.8f, .85f, 1f)); return false; }
        if (Mp < c.Mp) { Ui.Note("Not enough mana", new Color(0.4f, 0.65f, 1f)); return false; }
        Mp -= c.Mp; PendCast(id, c.Mp, 0); _chanCd = c.Cd;
        StartChannel("res", c.Def.Dur > 0 ? c.Def.Dur : 15f, "Resurrection", 0, id); Sfx.Play("heal");
        return true;
    }

    /// <summary>Raised by a friend where we lie, with this much health.</summary>
    public void ReviveHere(float hp)
    {
        Dead = false; Statuses.Clear(); _stagger = 0; _deadT = 0; _wipeT = 0; Hp = Mathf.Clamp(hp, 1, Game.D.MaxHp()); _inv = 2f;
        _model.Rotation = new Vector3(0, _modelYaw, 0); _av?.Play("idle", .3f);
        Vfx.Flash(GlobalPosition + Vector3.Up, new Color(1f, .95f, .7f), 4f, 8f, .5f); Fx.Burst(GlobalPosition + Vector3.Up, new Color(1f, .95f, .6f), 24, 4f, .7f, .1f);
    }

    /// <summary>Downed in a party lair: wait to be raised; if the whole party falls, or we give up, we wake outside.</summary>
    void DownedTick(float dt)
    {
        bool anyUp = false; foreach (var h in Net.DungeonHeroes()) if (h != this) { anyUp = true; break; }
        _wipeT = anyUp ? 0 : _wipeT + dt;
        bool giveUp = Input.MouseMode == Input.MouseModeEnum.Captured && Input.IsActionJustPressed(Inputs.Use) && _deadT > 2f;
        if (_wipeT > 4f) { Ui.Note("Your whole party has fallen", new Color(1f, .6f, .5f)); Respawner.PlayerDied(); }
        else if (giveUp) Respawner.PlayerDied();
    }
}
