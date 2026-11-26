using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>
/// F-13 lingering zones in Spellcraft. A crafted-spell section whose carrier is AoE-shaped (data/spellcards.json "zones.carriers") and
/// that holds a duration card in the same section ("zones.durationCards", e.g. Time) becomes a ZONE: a lingering F-12 circle
/// (SpellStyles.Linger) that re-runs that section's own hit every tick and sets off every section after it from its centre, until its
/// duration ends. Sections before it went off once. Tick, radius and duration come from the section's cards and stats
/// (Spellcraft.ZoneTiming). Limits: zones.maxPerCaster (the oldest ends when a new one starts), zones.maxTicks, and zones.noNested
/// (a zone's follow-ups cannot make zones unless the section holds a "nestCards" card). Every tick is part of the root cast (F-01):
/// it costs nothing and starts no cooldown; it is logged as CastPath.Chain "zone tick n".
/// </summary>
public partial class Player
{
    readonly List<SpellZone> _spellZones = new();
    /// <summary>Active crafted-spell zones of this hero (tests).</summary>
    public int SpellZoneCount { get { _spellZones.RemoveAll(z => !IsInstanceValid(z) || z.Ended); return _spellZones.Count; } }

    /// <summary>True when this section should go off as a lingering zone (it is one, and the no-nesting rule allows it here).</summary>
    bool ZoneCheck(SkillCalc f, int sec)
    {
        if (f.Def.XOr("zone", 0) <= 0) return false;
        if (f.InZone && Spellcraft.ZoneNoNested && f.Def.XOr("nestZones", 0) <= 0)
        {
            LogCast(CastPath.Chain, Spellcraft.RootOf(f.Def.Id), $"section {sec}: nested zone blocked (zones.noNested): it goes off once on this tick");
            return false;
        }
        return true;
    }

    /// <summary>Starts a lingering zone for section <paramref name="sec"/> (1-based) of a crafted spell at <paramref name="at"/>.</summary>
    void StartZone(SkillCalc f, Vector3 at, int sec, bool main)
    {
        string root = Spellcraft.RootOf(f.Def.Id);
        int max = Mathf.Max(1, (int)Spellcraft.ZoneNum("maxPerCaster", 3));
        _spellZones.RemoveAll(z => !IsInstanceValid(z) || z.Ended);
        while (_spellZones.Count >= max) { var old = _spellZones[0]; _spellZones.RemoveAt(0); LogCast(CastPath.Chain, root, $"zone limit {max} per caster: oldest zone ({old.Calc.Def.Id}) ends early after {old.Ticks} ticks"); old.End(); }
        var (tick, dur) = Spellcraft.ZoneTiming(f.DurMul, f.TickMul, f.Rank);
        int maxTicks = Mathf.Max(1, (int)Spellcraft.ZoneNum("maxTicks", 20));
        int n = Mathf.Clamp(Mathf.CeilToInt(dur / tick - .001f), 1, maxTicks);
        f.InZone = true;   // what this zone sets off cannot make zones of its own (unless allowed)
        var z = new SpellZone { Calc = f, Caster = this, Sec = sec, Tick = tick, Dur = dur, MaxTicks = n, Main = main };
        Game.Root!.AddChild(z); z.GlobalPosition = at; _spellZones.Add(z);
        LogCast(CastPath.Chain, root, $"zone start: section {sec} ({f.Def.Id}) at ({at.X:0.0},{at.Z:0.0}) r {f.Radius:0.0} m, every {tick:0.00}s for {dur:0.0}s = {n} ticks; repeats section {sec} and after; active zones {_spellZones.Count}/{max}");
    }

    /// <summary>One tick of a zone: the section's own hit again at the zone, then the sections after it from the zone's centre.</summary>
    internal void ZoneTick(SpellZone z, int n)
    {
        if (Dead) return;
        var f = z.Calc; var at = z.GlobalPosition; string root = Spellcraft.RootOf(f.Def.Id);
        LogCast(CastPath.Chain, root, $"zone tick {n}/{z.MaxTicks} (section {z.Sec}: {f.Def.Id}) at ({at.X:0.0},{at.Z:0.0}) (part of the root cast: no cost, no new cooldown)");
        SpellStyles.Note(f);
        NovaAt(f, at, z.Main && n == 1);
        if (f.Chain > 0) ChainFrom(f, at, f.Chain);
        if (f.Def.Then != "") FollowUp(f, at, .25f, z.Sec);
    }
}

/// <summary>A crafted spell's lingering zone (F-13): ticks its section and the ones after it; shows an F-12 Linger circle that drains.</summary>
public partial class SpellZone : Node3D
{
    public SkillCalc Calc = null!; public Player Caster = null!;
    public int Sec, MaxTicks, Ticks; public float Tick = 1, Dur = 4; public bool Main, Ended;
    float _t, _next;
    LingerFx? _fx;
    bool _fxMade;
    public override void _Process(double delta)
    {
        if (Ended) return;
        // the circle lives in the world (not under this node) so it can fade out after the zone ends; made on the first frame (placed by then)
        if (!_fxMade) { _fxMade = true; _fx = SpellStyles.Linger(GlobalPosition, Mathf.Max(.5f, Calc.Radius), Mathf.Max(.2f, Dur), Calc.Type, SpellStyles.PowerOf(Calc), null); }
        _t += (float)delta;
        if (_fx != null && IsInstanceValid(_fx)) _fx.SetTimeLeft(Mathf.Max(0, Dur - _t));
        while (!Ended && _t >= _next && Ticks < MaxTicks)
        {
            Ticks++; _next += Tick;
            if (!IsInstanceValid(Caster)) { End(); return; }
            Caster.ZoneTick(this, Ticks);
        }
        if (Ticks >= MaxTicks && _t >= Mathf.Min(Dur, _next)) { Caster.LogCast(Player.CastPath.Chain, Spellcraft.RootOf(Calc.Def.Id), $"zone end: section {Sec} after {Ticks} ticks ({_t:0.0}s)"); End(); }
    }
    /// <summary>Ends the zone now (duration over, or the caster's zone limit): the circle fades out.</summary>
    public void End()
    {
        if (Ended) return; Ended = true;
        if (_fx != null && IsInstanceValid(_fx)) _fx.Stop();
        QueueFree();
    }
}
