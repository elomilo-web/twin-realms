using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>
/// Who a monster, boss or raid boss fights (runs where the monster is run: the host overworld, the party dungeon's owner, or solo):
///  - by default the closest hero;
///  - a hero whose recent damage passes a share of its health (data/tuning.json "game.aggro.damageShare", bosses "bossShare")
///    pulls it onto them for at least 15 s, and it ignores taunts for that time;
///  - otherwise taunts pull it: the hero who last cast the strongest taunt still running holds it (a weaker taunt cannot take it away);
///  - raid lair bosses also turn on a random challenger now and then, and ignore taunts for 10 s after.
/// Taunts cast on a friend's copy of a monster are sent to whoever runs it (NetAggro.cs).
/// </summary>
public partial class Enemy
{
    long _aggroPeer = -1; float _aggroT, _tauntImmT, _tauntStr, _lairRollT = 8f; bool _aggroTaunt;
    readonly Dictionary<long, float> _dmgWin = new();
    static float AT(string k, float d) => Tune.F("aggro." + k, d);

    /// <summary>For the HUD and tests: taunts do nothing right now.</summary>
    public bool TauntImmune => _tauntImmT > 0;
    /// <summary>The hero this monster is locked onto (null = it fights the closest).</summary>
    public Node3D? AggroHero => _aggroT > 0 ? HeroOf(_aggroPeer) : null;

    /// <summary>A hero by peer id, if they are alive where we are (our own hero is Net.MyId, or 1 offline).</summary>
    public static Node3D? HeroOf(long peer)
    {
        if (peer < 0) return null;
        if (!Net.Active || peer == Net.MyId) { var p = Player.Instance; return p != null && !p.Dead ? p : null; }
        var h = Net.I?.HeroOf(peer); return h != null && h.HasPose && !h.Dead && h.Zone == Net.MyZone ? h : null;
    }
    public static long PeerOf(Node3D h) => h is RemoteHero rh ? rh.Peer : Net.MyId;

    /// <summary>Damage from a hero: past the threshold within a few seconds, that hero takes the monster's attention for a while.</summary>
    void NoteDamage(HitInfo h, int dmg)
    {
        long peer = h.Remote ? h.Peer : h.IsPlayer ? Net.MyId : -1; if (peer < 0 || dmg <= 0) return;
        _dmgWin[peer] = _dmgWin.GetValueOrDefault(peer) + dmg;
        float need = MaxHp * (Def.Boss ? AT("bossShare", .04f) : AT("damageShare", .25f));
        if (_dmgWin[peer] < need || (_aggroPeer == peer && _aggroT > 0 && !_aggroTaunt)) return;
        _dmgWin.Clear();
        float t = AT("damageLock", 15f);
        _aggroPeer = peer; _aggroT = t; _tauntImmT = Mathf.Max(_tauntImmT, t); _aggroTaunt = false; _tauntStr = 0; _heroT = 0;
        if (Def.Boss) FloatText.Spawn(this, GlobalPosition + new Vector3(0, HeightM() + .8f, 0), "Enraged!", new Color(1f, .45f, .35f), .6f);
    }

    /// <summary>A taunt from a hero: wins unless the monster is taunt immune or a stronger taunt is still holding it. Returns whether it took.</summary>
    public bool Taunt(long peer, float strength, float dur)
    {
        if (Dead) return false;
        if (Puppet) { Net.I?.SendTaunt(this, strength, dur); return true; }   // whoever runs it decides
        if (_tauntImmT > 0) { if (peer == Net.MyId) FloatText.Spawn(this, GlobalPosition + new Vector3(0, HeightM() + .6f, 0), "Taunt immune", new Color(.8f, .8f, .9f), .5f); return false; }
        if (_aggroT > 0 && _aggroTaunt && peer != _aggroPeer && strength < _tauntStr) return false;   // a stronger taunt still holds it
        _aggroPeer = peer; _aggroT = dur; _aggroTaunt = true; _tauntStr = strength; _heroT = 0;
        if (_state is State.Idle or State.Sleep or State.Return) Wake();
        FloatText.Spawn(this, GlobalPosition + new Vector3(0, HeightM() + .6f, 0), "Taunted", new Color(1f, .75f, .35f), .5f);
        return true;
    }

    /// <summary>Timers, the fading damage memory, and the raid boss's sudden changes of target. Only where the monster is run.</summary>
    void AggroTick(float dt)
    {
        _aggroT = Mathf.Max(0, _aggroT - dt); _tauntImmT = Mathf.Max(0, _tauntImmT - dt);
        if (_aggroT > 0 && HeroOf(_aggroPeer) == null) _aggroT = 0;   // the hero died or left: back to the closest
        if (_dmgWin.Count > 0) { float k = Mathf.Exp(-dt / AT("damageWindow", 6f)); foreach (var key in new List<long>(_dmgWin.Keys)) { _dmgWin[key] *= k; if (_dmgWin[key] < 1) _dmgWin.Remove(key); } }
        // raid lair bosses: now and then she turns on someone else, and taunts do nothing for a while
        if (!Def.Boss || !FromDungeon || !Dungeon.IsLair(Dungeon.Key) || (Scarletta && _sm == SM.Sleep)) return;
        _lairRollT -= dt; if (_lairRollT > 0) return;
        _lairRollT = (float)GD.RandRange(AT("lairRollMin", 7f), AT("lairRollMax", 12f));
        if (GD.Randf() > AT("lairSwitchChance", .3f)) return;
        var cur = AggroHero ?? (Scarletta ? _smTgt : _hero); var pick = new List<Node3D>();
        foreach (var h in Net.DungeonHeroes()) if (h != cur) pick.Add(h);
        if (pick.Count == 0) return;
        var to = pick[GD.RandRange(0, pick.Count - 1)]; float t = AT("lairImmune", 10f);
        _aggroPeer = PeerOf(to); _aggroT = t; _tauntImmT = Mathf.Max(_tauntImmT, t); _aggroTaunt = false; _tauntStr = 0; _heroT = 0;
        FloatText.Spawn(this, GlobalPosition + new Vector3(0, HeightM() + .8f, 0), "Turns on " + (to is RemoteHero rh ? rh.PlayerName : Game.D.Name) + "!", new Color(1f, .5f, .4f), .6f);
    }
}
