using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>
/// Life and action skills (focus F-23/F-24/F-25; data/life.json): XP per skill id in PlayerStats.Life, levels from a curve per skill.
/// Every level past 1 adds the skill's "perLevel" stat-table keys through the stat pipeline's life stage (StatPipeline.Rebuild,
/// .devloop/systems/stats.md); gems with "life_&lt;id&gt;" keys add levels. Use <see cref="Award"/> for XP from play: it applies diminishing
/// returns per skill (soft XP per minute) and keeps fractions, so nothing can be leveled by spamming a key. Distance skills are fed by
/// Player.LifeTick (PlayerLife.cs) from real movement only.
/// </summary>
public static class LifeSkills
{
    public sealed class Curve { public float Base = 40, Growth = 1.35f; public int Max = 50; }
    public sealed class Def
    {
        public string Id = "", Name = "", Group = "life", Icon = "", Desc = "", Perk = ""; public float Soft = 40;
        public readonly Dictionary<string, float> PerLevel = new();
    }
    public sealed class Track { public float Per = 10, Xp = 1; }
    static readonly Dictionary<string, Curve> Curves = new();
    static readonly HashSet<string> _fromOwner = new();
    public static readonly Dictionary<string, Def> Defs = new();
    public static readonly List<string> Order = new();
    static readonly Dictionary<string, Track> _track = new();
    static JsonElement _root; static JsonDocument? _doc;
    static string Title(string id) => Defs.TryGetValue(id, out var d) ? d.Name : id.Length == 0 ? id : char.ToUpper(id[0]) + id[1..];

    public static void Load()
    {
        if (_doc != null) return;
        _doc = JsonUtil.Load("res://data/life.json"); _root = _doc.RootElement;
        var df = _root.TryGetProperty("defaults", out var dd) ? dd : default;
        float b0 = df.ValueKind == JsonValueKind.Object ? JsonUtil.F(df, "base", 40) : 40, g0 = df.ValueKind == JsonValueKind.Object ? JsonUtil.F(df, "growth", 1.35f) : 1.35f, s0 = df.ValueKind == JsonValueKind.Object ? JsonUtil.F(df, "soft", 40) : 40;
        int m0 = df.ValueKind == JsonValueKind.Object ? (int)JsonUtil.F(df, "max", 50) : 50;
        foreach (var p in _root.GetProperty("skills").EnumerateObject())
        {
            var e = p.Value; var d = new Def { Id = p.Name, Name = JsonUtil.S(e, "n", p.Name), Group = JsonUtil.S(e, "group", "life"), Icon = JsonUtil.S(e, "icon"), Desc = JsonUtil.S(e, "desc"), Perk = JsonUtil.S(e, "perk"), Soft = JsonUtil.F(e, "soft", s0) };
            if (e.TryGetProperty("perLevel", out var pl)) foreach (var k in pl.EnumerateObject()) d.PerLevel[k.Name] = k.Value.GetSingle();
            Defs[p.Name] = d; Order.Add(p.Name);
            if (!_fromOwner.Contains(p.Name)) Curves[p.Name] = new Curve { Base = JsonUtil.F(e, "base", b0), Growth = JsonUtil.F(e, "growth", g0), Max = (int)JsonUtil.F(e, "max", m0) };
        }
        if (_root.TryGetProperty("track", out var tr))
            foreach (var p in tr.EnumerateObject()) if (p.Value.ValueKind == JsonValueKind.Object) _track[p.Name] = new Track { Per = JsonUtil.F(p.Value, "per", 10), Xp = JsonUtil.F(p.Value, "xp", 1) };
    }
    /// <summary>A number under data/life.json "track" ("jumping.cooldown").</summary>
    public static float T(string path, float def)
    {
        Load(); var e = _root.GetProperty("track");
        foreach (var part in path.Split('.')) { if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(part, out var n)) return def; e = n; }
        return e.ValueKind == JsonValueKind.Number ? e.GetSingle() : def;
    }

    /// <summary>Registers a skill's curve (called by the system that owns the skill, e.g. FishDb); it wins over life.json.</summary>
    public static void Define(string id, Curve c) { Curves[id] = c; _fromOwner.Add(id); }
    static Curve C(string id) { Load(); return Curves.TryGetValue(id, out var c) ? c : new Curve(); }

    /// <summary>XP needed to go from level lv to lv + 1.</summary>
    public static int Need(string id, int lv) { var c = C(id); return (int)Mathf.Round(c.Base * Mathf.Pow(lv, c.Growth)); }
    public static int Xp(PlayerStats d, string id) { d.Life ??= new(); return d.Life.GetValueOrDefault(id); }
    /// <summary>Level 1.. from the total XP (without gem levels).</summary>
    public static int Level(PlayerStats d, string id)
    {
        int xp = Xp(d, id), lv = 1, max = C(id).Max;
        while (lv < max) { int n = Need(id, lv); if (xp < n) break; xp -= n; lv++; }
        return lv;
    }
    public static int Level(string id) => Level(Game.D, id);
    /// <summary>The level the bonuses use: earned level + gem levels ("life_&lt;id&gt;" keys, F-10).</summary>
    public static int Effective(string id) => Level(Game.D, id) + Mathf.Max(0, (int)Game.TS["life_" + id]);
    /// <summary>0..1 progress into the current level.</summary>
    public static float Progress(PlayerStats d, string id)
    {
        int xp = Xp(d, id), lv = 1, max = C(id).Max;
        while (lv < max) { int n = Need(id, lv); if (xp < n) return n > 0 ? (float)xp / n : 0; xp -= n; lv++; }
        return 1;
    }
    /// <summary>XP into the current level and what the level needs (max level: need 0).</summary>
    public static (int into, int need) Into(PlayerStats d, string id)
    {
        int xp = Xp(d, id), lv = 1, max = C(id).Max;
        while (lv < max) { int n = Need(id, lv); if (xp < n) return (xp, n); xp -= n; lv++; }
        return (0, 0);
    }
    public static int Max(string id) => C(id).Max;

    /// <summary>The stat keys a skill at this level gives (perLevel x (level - 1)).</summary>
    public static Dictionary<string, float> Mods(string id, int lv)
    {
        Load(); var m = new Dictionary<string, float>(); if (!Defs.TryGetValue(id, out var d) || lv <= 1) return m;
        foreach (var kv in d.PerLevel) m[kv.Key] = kv.Value * (lv - 1);
        return m;
    }

    /// <summary>Adds XP straight (no diminishing returns); on a level-up a note, a fanfare and a stat rebuild. Returns the new level.</summary>
    public static int Gain(string id, int xp, PlayerStats? d = null, bool quiet = false)
    {
        d ??= Game.D; d.Life ??= new(); if (xp <= 0) return Level(d, id);
        int before = Level(d, id); d.Life[id] = d.Life.GetValueOrDefault(id) + xp; int after = Level(d, id);
        if (after > before)
        {
            if (!quiet) { Ui.Note($"{Title(id)} level {after}!", UiTheme.Highlight); Sfx.Play("level"); }
            if (d == Game.D) Game.RecalcTree();   // the new level's bonus goes through the stat pipeline at once
            if (id == Cooking.Skill) Cooking.LearnByLevel(d, after, quiet);
        }
        return after;
    }

    // ---------------------------------------------------------------- XP from play: diminishing returns, fractions kept
    static readonly Dictionary<string, Queue<(double t, float xp)>> _recent = new();
    static readonly Dictionary<string, float> _frac = new();
    /// <summary>For the dev capture: total raw and awarded XP per skill since the last reset.</summary>
    public static readonly Dictionary<string, (float raw, float got)> Log = new();
    static double Now => Time.GetTicksMsec() / 1000.0;
    /// <summary>XP from play: scaled by soft / (soft + XP already got in the last 60 s); fractions carry over. Returns whole XP added.</summary>
    public static int Award(string id, float xp)
    {
        Load(); if (xp <= 0 || !Defs.TryGetValue(id, out var def)) return 0;
        if (!_recent.TryGetValue(id, out var q)) _recent[id] = q = new();
        double now = Now; while (q.Count > 0 && now - q.Peek().t > 60) q.Dequeue();
        float recent = 0; foreach (var r in q) recent += r.xp;
        float got = xp * def.Soft / (def.Soft + recent);
        q.Enqueue((now, got));
        var lg = Log.GetValueOrDefault(id); Log[id] = (lg.raw + xp, lg.got + got);
        float f = _frac.GetValueOrDefault(id) + got; int whole = (int)f; _frac[id] = f - whole;
        if (whole > 0) Gain(id, whole);
        return whole;
    }
    /// <summary>A distance skill: metres of real movement (life.json track) turned into XP.</summary>
    public static void Moved(string id, float metres)
    {
        if (metres <= 0) return; Load(); var t = _track.GetValueOrDefault(id) ?? new Track();
        Award(id, metres / Mathf.Max(.1f, t.Per) * t.Xp);
    }
    public static void DevReset() { _recent.Clear(); _frac.Clear(); Log.Clear(); }

    /// <summary>The stats dump's life skill lines: level (+gem levels), XP, progress and the keys each gives.</summary>
    public static string DumpLines(PlayerStats d)
    {
        Load(); var sb = new StringBuilder("[life skills] level (+gem levels): xp into the level / needed; bonus keys = perLevel x (level - 1) (life stage)\n");
        foreach (var id in Order)
        {
            int lv = Level(d, id), gem = d == Game.D ? Mathf.Max(0, (int)Game.TS["life_" + id]) : 0; var (into, need) = Into(d, id);
            var m = Mods(id, lv + gem);
            sb.AppendLine($"    {id} {lv}{(gem > 0 ? $" (+{gem})" : "")}: {into}/{need}  {(m.Count == 0 ? "-" : string.Join(", ", m.Select(kv => $"{kv.Key} {kv.Value:+0.##;-0.##}")))}");
        }
        return sb.ToString().TrimEnd();
    }
}
