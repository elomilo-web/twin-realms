using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>
/// Valor: the Wave Survival currency (data/valor.json). A run pays valor the first time it clears each milestone wave;
/// Sera, Valor Quartermaster, trades it for gold, chosen gear and gacha pulls.
/// </summary>
public static class Valor
{
    static JsonDocument? _doc; static JsonElement C => (_doc ??= JsonUtil.Load("res://data/valor.json")).RootElement;

    /// <summary>Valor for clearing this wave (0 if it is not a milestone).</summary>
    public static int ForWave(int wave)
    {
        int last = 0;
        foreach (var m in C.GetProperty("milestones").EnumerateArray()) { int w = m.GetProperty("wave").GetInt32(); last = Math.Max(last, w); if (w == wave) return m.GetProperty("valor").GetInt32(); }
        if (C.TryGetProperty("every", out var ev)) { int step = ev.GetProperty("every").GetInt32(); if (step > 0 && wave > last && (wave - last) % step == 0) return ev.GetProperty("valor").GetInt32(); }
        return 0;
    }
    /// <summary>The next milestone wave after this one (for the HUD and the arena master).</summary>
    public static int NextMilestone(int wave) { for (int w = wave + 1; w < wave + 200; w++) if (ForWave(w) > 0) return w; return 0; }

    /// <summary>Valor for winning a Shadow Duel round (duelPerRound x streak, capped at duelMax).</summary>
    public static int ForDuel(int streak) => Math.Min((int)JsonUtil.F(C, "duelMax", 60), (int)JsonUtil.F(C, "duelPerRound", 5) * Math.Max(1, streak));

    public static void Give(int v) { if (v <= 0) return; Game.D.Valor += v; Ui.Note($"+{v} valor  ({Game.D.Valor})", new Color(1f, .78f, .35f)); Sfx.Play("coin"); }

    public static IEnumerable<(int valor, int gold)> GoldTrades() { foreach (var g in C.GetProperty("gold").EnumerateArray()) yield return (g.GetProperty("valor").GetInt32(), g.GetProperty("gold").GetInt32()); }
    public static IReadOnlyList<string> GearSlots => C.GetProperty("gear").GetProperty("slots").EnumerateArray().Select(x => x.GetString() ?? "").ToList();
    public static int GearCost(int rar) { var c = C.GetProperty("gear").GetProperty("cost"); return rar < c.GetArrayLength() ? c[rar].GetInt32() : 0; }
    public static int PullCost => C.GetProperty("gacha").GetProperty("cost").GetInt32();
    public static int PullTenCost => C.GetProperty("gacha").GetProperty("costTen").GetInt32();
    public static int Pity => C.GetProperty("gacha").GetProperty("pity").GetInt32();

    public static Item Gear(string slot, int rar) => Items.Roll(Game.D.Level + (int)JsonUtil.F(C.GetProperty("gear"), "levelBonus", 1), 0, slot, rar);

    /// <summary>One gacha pull: rarity by weight, with a pity floor of epic.</summary>
    public static Item Pull()
    {
        var g = C.GetProperty("gacha"); var w = g.GetProperty("weights").EnumerateArray().Select(x => x.GetSingle()).ToArray();
        float r = GD.Randf() * w.Sum(); int rar = 0; for (; rar < w.Length - 1; rar++) { if (r < w[rar]) break; r -= w[rar]; }
        var d = Game.D; d.GachaPity++;
        if (d.GachaPity >= Pity && rar < 3) rar = 3;
        if (rar >= 3) d.GachaPity = 0;
        return Items.Roll(d.Level + (int)JsonUtil.F(g, "levelBonus", 2), 0, null, rar);
    }
}
