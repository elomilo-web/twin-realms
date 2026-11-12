using Godot;

namespace TwinRealms;

/// <summary>Game feel: hit-stop (a tiny freeze on impact) and enemy recoil settings. Tuning lives in data/feel.json.</summary>
public static class Feel
{
    public static float Light = .035f, Combo = .055f, Power = .085f, Spell = .025f, CritBonus = .03f, KillBonus = .03f, Taken = .05f, MaxStop = .12f, StopScale = .05f;
    public static float RecoilTime = .2f, RecoilPush = .22f, RecoilSquash = .16f, RecoilBossMul = .4f;
    public static float Stride = 1.9f, LegSwing = .7f, ArmSwing = .2f, Bob = .07f, Lean = .16f, Breath = .012f;
    public static float GlowEnergy = .9f, GlowRange = 10f, LockTurn = 6f, Shoulder = .65f, ShoulderUp = .25f;
    // hero movement (feel.json "move"): ground speeds in m/s, sprint stamina drain per second, pause before stamina refills, refill per second
    public static float Run = 5.6f, Sprint = 8f, SprintDrain = 4f, SprintRegenDelay = .3f, StRegen = 24f;
    static bool _loaded, _on;
    static ulong _until;

    public static void Load()
    {
        if (_loaded) return; _loaded = true;
        using var doc = JsonUtil.Load("res://data/feel.json"); var r = doc.RootElement;
        if (r.TryGetProperty("hitstop", out var h))
        {
            Light = JsonUtil.F(h, "light", Light); Combo = JsonUtil.F(h, "combo", Combo); Power = JsonUtil.F(h, "power", Power); Spell = JsonUtil.F(h, "spell", Spell);
            CritBonus = JsonUtil.F(h, "critBonus", CritBonus); KillBonus = JsonUtil.F(h, "killBonus", KillBonus); Taken = JsonUtil.F(h, "taken", Taken);
            MaxStop = JsonUtil.F(h, "max", MaxStop); StopScale = JsonUtil.F(h, "timeScale", StopScale);
        }
        if (r.TryGetProperty("recoil", out var c))
        {
            RecoilTime = JsonUtil.F(c, "time", RecoilTime); RecoilPush = JsonUtil.F(c, "push", RecoilPush); RecoilSquash = JsonUtil.F(c, "squash", RecoilSquash); RecoilBossMul = JsonUtil.F(c, "bossMul", RecoilBossMul);
        }
        if (r.TryGetProperty("move", out var mv))
        {
            Run = JsonUtil.F(mv, "run", Run); Sprint = JsonUtil.F(mv, "sprint", Sprint); SprintDrain = JsonUtil.F(mv, "sprintDrain", SprintDrain);
            SprintRegenDelay = JsonUtil.F(mv, "sprintRegenDelay", SprintRegenDelay); StRegen = JsonUtil.F(mv, "staminaRegen", StRegen);
        }
        if (r.TryGetProperty("glow", out var g)) { GlowEnergy = JsonUtil.F(g, "energy", GlowEnergy); GlowRange = JsonUtil.F(g, "range", GlowRange); }
        if (r.TryGetProperty("camera", out var cm)) { LockTurn = JsonUtil.F(cm, "lockTurn", LockTurn); Shoulder = JsonUtil.F(cm, "shoulder", Shoulder); ShoulderUp = JsonUtil.F(cm, "shoulderUp", ShoulderUp); }
        if (r.TryGetProperty("walk", out var w))
        {
            Stride = JsonUtil.F(w, "stride", Stride); LegSwing = JsonUtil.F(w, "legSwing", LegSwing); ArmSwing = JsonUtil.F(w, "armSwing", ArmSwing);
            Bob = JsonUtil.F(w, "bob", Bob); Lean = JsonUtil.F(w, "lean", Lean); Breath = JsonUtil.F(w, "breath", Breath);
        }
    }

    /// <summary>Hit-stop length for a hit the player landed.</summary>
    public static float ForHit(HitInfo h, bool killed)
    {
        Load();
        float t = h.Kind switch { "light" => Light, "combo" => Combo, "power" => Power, _ => Spell };
        if (h.Crit) t += CritBonus; if (killed) t += KillBonus;
        return t;
    }

    /// <summary>Freeze the game for a few hundredths of a second. Overlapping calls extend to the latest end, never stack.</summary>
    public static void HitStop(float seconds)
    {
        Load(); seconds = Mathf.Min(seconds, MaxStop); if (seconds <= 0 || Net.Active) return;   // never slow time in a shared world
        if (!_on && !Mathf.IsEqualApprox((float)Engine.TimeScale, 1f)) return; // someone else owns time scale
        ulong end = Time.GetTicksMsec() + (ulong)(seconds * 1000f);
        if (end > _until) _until = end;
        _on = true; Engine.TimeScale = StopScale;
    }

    /// <summary>Called every frame (real time) to end the freeze.</summary>
    public static void Tick() { if (_on && Time.GetTicksMsec() >= _until) { _on = false; Engine.TimeScale = 1.0; } }
}
