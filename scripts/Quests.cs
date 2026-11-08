using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace TwinRealms;

public class QuestDef { public string Id = "", Name = "", Giver = "", Req = "", Kind = "kill", Goal = "", Offer = "", Done = ""; public int Need = 1, Lvl; public int Xp, Gold, Bonus; public string MatchType = ""; public int MatchRealm = -1, MinWave, MinStreak; }

/// <summary>Quest log, progress events and rewards. Definitions come from data/quests.json.</summary>
public static class Quests
{
    public static List<QuestDef> All = new(); static bool _loaded;
    public static QuestDef? ById(string id) { Load(); return All.Find(q => q.Id == id); }

    static void Load()
    {
        if (_loaded) return; _loaded = true;
        using var doc = JsonUtil.Load("res://data/quests.json");
        foreach (var q in doc.RootElement.GetProperty("quests").EnumerateArray())
        {
            var d = new QuestDef { Id = JsonUtil.S(q, "id"), Name = JsonUtil.S(q, "name"), Giver = JsonUtil.S(q, "giver"), Kind = JsonUtil.S(q, "kind", "kill"), Goal = JsonUtil.S(q, "goal"), Offer = JsonUtil.S(q, "offer"), Done = JsonUtil.S(q, "done"), Need = (int)JsonUtil.F(q, "need", 1), Lvl = (int)JsonUtil.F(q, "lvl", 0), MatchType = JsonUtil.S(q, "matchType"), MatchRealm = (int)JsonUtil.F(q, "matchRealm", -1), MinWave = (int)JsonUtil.F(q, "minWave", 0), MinStreak = (int)JsonUtil.F(q, "minStreak", 0) };
            if (q.TryGetProperty("req", out var rq) && rq.ValueKind == JsonValueKind.String) d.Req = rq.GetString() ?? "";
            if (q.TryGetProperty("rw", out var rw)) { d.Xp = (int)JsonUtil.F(rw, "xp"); d.Gold = (int)JsonUtil.F(rw, "gold"); d.Bonus = (int)JsonUtil.F(rw, "bonus"); }
            All.Add(d);
        }
    }

    public static bool Available(QuestDef q) { var s = Game.Q(q.Id); return s.S == 0 && (q.Req == "" || Game.Q(q.Req).S == 3); }
    public static List<QuestDef> Active() { Load(); return All.FindAll(q => { var s = Game.Q(q.Id).S; return s == 1 || s == 2; }); }
    public static string GiverName(string id) => id switch { "maren" => "Elder Maren", "kael" => "Kael", _ => "Guard Tomas" };

    static void Progress(QuestDef q)
    {
        var s = Game.Q(q.Id); s.N++;
        if (s.N >= q.Need) { s.S = 2; Ui.Banner(q.Name, "objective complete"); Ui.Note("Return to " + GiverName(q.Giver) + " to claim your reward", new Color(.89f, .69f, .29f)); Sfx.Play("level"); }
        else Ui.Note(q.Name + ": " + s.N + " / " + q.Need, new Color(.89f, .69f, .29f));
    }
    public static void OnKill(string type, int realm, bool boss)
    {
        Load(); foreach (var q in All) { if (Game.Q(q.Id).S != 1 || q.Kind != "kill") continue; if (q.MatchType != "" && q.MatchType != type) continue; if (q.MatchRealm >= 0 && q.MatchRealm != realm) continue; Progress(q); }
    }
    public static void OnWave(int wave) { Load(); foreach (var q in All) if (Game.Q(q.Id).S == 1 && q.Kind == "wave" && wave >= q.MinWave) Progress(q); }
    public static void OnDuel(int streak) { Load(); foreach (var q in All) if (Game.Q(q.Id).S == 1 && q.Kind == "duel" && streak >= q.MinStreak) Progress(q); }
    public static void OnRealmShift(int realm) { }

    public static void Accept(QuestDef q) { var s = Game.Q(q.Id); s.S = 1; s.N = 0; Ui.Note("Quest started: " + q.Name, new Color(.89f, .69f, .29f)); Sfx.Play("click"); Game.Save(); }
    public static void Reward(QuestDef q)
    {
        var s = Game.Q(q.Id); s.S = 3; var d = Game.D; d.Gold += q.Gold; Player.Instance?.AddXp(q.Xp, Player.Instance.GlobalPosition);
        var it = Items.Roll(Math.Max(2, d.Level + 1), q.Bonus + 1);
        if (d.Inv.Count < 28) { d.Inv.Add(it); Ui.Note("Reward: " + it.Describe(), it.Color); }
        Ui.Note("+" + q.Gold + " gold, +" + q.Xp + " xp", new Color(1f, .82f, .29f)); Sfx.Play("coin"); Game.Save();
    }
}

/// <summary>What happens when the player dies outside the arena.</summary>
public static class Respawner
{
    public static void PlayerDied()
    {
        var p = Player.Instance; if (p == null) return;
        if (Dungeon.Active) { Dungeon.Leave(true); Ui.Note("You were dragged out of the dungeon", new Color(1f, .6f, .5f)); return; }
        World.ForceRealm(0);
        var pos = World.OnGround(Game.D.PosX, Game.D.PosZ, 0.5f); if (new Vector2(Game.D.PosX, Game.D.PosZ).Length() < 1f) pos = World.OnGround(0, 9, 0.5f);
        p.Revive(pos); p.Teleport(pos, 0f); Ui.Note("You wake at your last resting place", new Color(.8f, .8f, 1f));
    }
}
