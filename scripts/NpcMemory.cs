using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>What one NPC remembers of this hero (saved in PlayerStats.Npcs by NPC id).</summary>
public class NpcMem
{
    public bool Met; public int Rep, Talks, Quests, Gifts, BadGifts, Insults, Crimes, Trades, TradeRep;
    public string LastGift = "", Last = "";   // Last: the latest thing remembered (gift, badgift, insult, crime, quest, trade)
    public float GiftAt = -1e9f;              // PlayerStats.Played when the last gift was taken
}

/// <summary>
/// Focus F-28: NPC memory and reputation. Every NPC of data/npcs.json remembers whether it met the hero, quests done for it, gifts,
/// insults, crimes it saw and trades (NpcMem, saved with the hero). Standing runs -100..100 per NPC and per town; the numbers and the
/// lines are in data/npcs.json "reputation" and "memory_lines". The dialog opens with a line chosen from what the NPC remembers
/// (Greet), prices follow standing (BuyMul / SellMul: UiShop, UiSmith), and at "refuseBelow" an NPC will not deal with the hero.
/// Crime(...) is the hook for F-32 (guards). Dev command: "/rep".
/// </summary>
public static class NpcMemory
{
    static bool _loaded; static JsonElement _cfg, _lines; static JsonDocument? _doc;
    static readonly List<(int at, string name)> _tiers = new();
    static readonly System.Random _rng = new();

    static void Load()
    {
        if (_loaded) return; _loaded = true; NpcDb.Load();
        Commands.Register("rep", "F-28: rep [id]: what an NPC remembers and its standing; rep <id> gift <mat>|insult|quest|crime|trade|set <n>|greet; rep town <id> [n]; rep test: the save round trip", Cmd);
        if (!FileAccess.FileExists("res://data/npcs.json")) return;
        _doc = JsonUtil.Load("res://data/npcs.json"); var r = _doc.RootElement;
        if (r.TryGetProperty("reputation", out _cfg) && _cfg.TryGetProperty("tiers", out var ts))
            foreach (var t in ts.EnumerateArray()) _tiers.Add(((int)JsonUtil.F(t, "at"), JsonUtil.S(t, "name")));
        r.TryGetProperty("memory_lines", out _lines);
        _tiers.Sort((a, b) => a.at.CompareTo(b.at));
    }
    public static void Init() => Load();
    static float N(string group, string key, float def)
    {
        Load(); if (_cfg.ValueKind != JsonValueKind.Object) return def;
        if (key == "") return JsonUtil.F(_cfg, group, def);
        return _cfg.TryGetProperty(group, out var g) && g.ValueKind == JsonValueKind.Object ? JsonUtil.F(g, key, def) : def;
    }

    // ------------------------------------------------------------------ the memory
    public static NpcMem Of(string id) { var d = Game.D; d.Npcs ??= new(); if (!d.Npcs.TryGetValue(id, out var m) || m == null) d.Npcs[id] = m = new NpcMem(); return m; }
    public static NpcMem? Peek(string id) => Game.D.Npcs != null && Game.D.Npcs.TryGetValue(id, out var m) ? m : null;
    public static int TownRep(string town) => town != "" && Game.D.TownRep != null && Game.D.TownRep.TryGetValue(town, out int v) ? v : 0;
    static void AddTown(string town, int n) { if (town == "" || n == 0) return; Game.D.TownRep ??= new(); Game.D.TownRep[town] = Math.Clamp(TownRep(town) + n, -100, 100); }
    static void Add(NpcRec r, int npc, int town)
    {
        var m = Of(r.Id); int was = m.Rep; string tw = Tier(was);
        m.Rep = Math.Clamp(m.Rep + npc, -100, 100); AddTown(r.Town, town);
        if (m.Rep != was)
        {
            string t = Tier(m.Rep); int dlt = m.Rep - was;
            Ui.Note($"{r.Name}: standing {(dlt > 0 ? "+" : "")}{dlt}{(t != tw ? $" (now {t})" : "")}", dlt > 0 ? new Color(.6f, .9f, .6f) : new Color(1f, .6f, .5f));
        }
        if (town != 0 && r.Town != "") Ui.Note($"{TownName(r.Town)}: standing {(town > 0 ? "+" : "")}{town}", town > 0 ? new Color(.6f, .9f, .6f) : new Color(1f, .6f, .5f));
        Game.Notify();
    }
    public static string TownName(string town) => NpcDb.Towns.GetValueOrDefault(town)?.Name ?? town;
    public static string Tier(int rep) { Load(); string n = "Neutral"; foreach (var (at, name) in _tiers) if (rep >= at) n = name; return n; }
    /// <summary>Standing used for prices: the NPC's own and its town's, weighted (-100..100).</summary>
    public static float Standing(NpcRec r) => Mathf.Clamp(N("npcWeight", "", .6f) * (Peek(r.Id)?.Rep ?? 0) + N("townWeight", "", .4f) * TownRep(r.Town), -100f, 100f);
    public static float BuyMul(string npcId) => NpcDb.Get(npcId) is { } r ? 1f - N("priceMax", "", .2f) * Standing(r) / 100f : 1f;
    public static float SellMul(string npcId) => NpcDb.Get(npcId) is { } r ? 1f + N("priceMax", "", .2f) * Standing(r) / 100f : 1f;
    public static bool Refuses(NpcRec r) => (Peek(r.Id)?.Rep ?? 0) <= N("refuseBelow", "", -50f);
    public static string Summary(NpcRec r)
    {
        var m = Of(r.Id); int pct = Mathf.RoundToInt((BuyMul(r.Id) - 1f) * 100f);
        string s = $"Standing: {Tier(m.Rep)} ({(m.Rep > 0 ? "+" : "")}{m.Rep})";
        if (r.Town != "") s += $"   {TownName(r.Town)}: {(TownRep(r.Town) > 0 ? "+" : "")}{TownRep(r.Town)}";
        if (pct != 0 && (NpcDb.Trades(r) || NpcDb.Smiths(r) || r.Talk is "shop" or "smith")) s += $"   Prices: {(pct > 0 ? "+" : "")}{pct}%";
        return s;
    }

    // ------------------------------------------------------------------ lines
    static string Line(NpcRec r, string sit, string gift = "")
    {
        Load(); var m = Of(r.Id); string s = "...";
        if (_lines.ValueKind == JsonValueKind.Object)
        {
            JsonElement a;
            if ((_lines.TryGetProperty(sit + "." + r.Speech, out a) || _lines.TryGetProperty(sit, out a)) && a.ValueKind == JsonValueKind.Array && a.GetArrayLength() > 0)
                s = a[Math.Abs(m.Talks + r.Idx) % a.GetArrayLength()].GetString() ?? "...";
        }
        return s.Replace("{me}", r.Name).Replace("{name}", Game.D.Name).Replace("{gift}", gift != "" ? gift : m.LastGift).Replace("{town}", r.Town != "" ? TownName(r.Town) : "these parts");
    }
    /// <summary>Which memory the opening line draws on (also for the dev command and the capture log).</summary>
    public static string Situation(NpcRec r)
    {
        var m = Of(r.Id); string t = Tier(m.Rep);
        if (!m.Met) return "first";
        if (Refuses(r)) return "hated";
        if (m.Last == "crime") return "crime";     // the latest thing remembered wins, even with a friend: a gift, a quest or a trade after it makes amends
        if (m.Last == "insult") return "insult";
        if (m.Rep <= -15) return "disliked";
        if (m.Last == "badgift") return "badgift";
        if (m.Last == "gift") return "gift";
        if (m.Last == "quest") return "quest";
        if (m.Rep >= 50) return "trusted";
        if (m.Last == "trade" && m.Trades >= 5) return "trade";
        if (m.Rep >= 15) return "liked";
        return "known";
    }
    /// <summary>The dialog's opening line from what the NPC remembers; own is its next ordinary line. Marks the NPC as met.</summary>
    public static string Greet(NpcRec r, string own)
    {
        var m = Of(r.Id); string sit = Situation(r), s = Line(r, sit);
        bool cold = sit is "hated" or "crime" or "insult" or "disliked" or "badgift";
        if (!m.Met) { m.Met = true; Add(r, (int)N("meet", "", 1), 0); }
        m.Talks++;
        return cold || own == "" || own == "..." ? s : s + " " + own;
    }
    public static string RefuseLine(NpcRec r) => Line(r, "refuse");

    // ------------------------------------------------------------------ things that are remembered
    static bool Match(string group, string role, string mat)
    {
        if (_cfg.ValueKind != JsonValueKind.Object || !_cfg.TryGetProperty(group, out var g) || !g.TryGetProperty(role, out var a) || a.ValueKind != JsonValueKind.Array) return false;
        foreach (var x in a.EnumerateArray()) { string p = x.GetString() ?? ""; if (p.EndsWith('*') ? mat.StartsWith(p[..^1]) : p == mat) return true; }
        return false;
    }
    static bool Words(string[] phrases, string matName)
    {
        foreach (var w in matName.ToLowerInvariant().Split(' ', '-'))
        {
            string k = w.TrimEnd('s'); if (k.Length < 4) continue;
            foreach (var p in phrases) if (p.ToLowerInvariant().Contains(k)) return true;
        }
        return false;
    }
    /// <summary>1 liked, 0 neutral, -1 disliked: the NPC's own likes and dislikes first, then its role's, then everyone's.</summary>
    public static int Taste(NpcRec r, string mat)
    {
        Load(); string name = GatherDb.Mat(mat).Name;
        if (Words(r.Dislikes, name)) return -1;
        if (Words(r.Likes, name)) return 1;
        if (Match("giftLikes", r.Role, mat)) return 1;
        if (Match("giftDislikes", r.Role, mat)) return -1;
        bool own = _cfg.ValueKind == JsonValueKind.Object && _cfg.TryGetProperty("giftDislikes", out var gd) && gd.TryGetProperty(r.Role, out _);   // a role with its own list (even empty) skips everyone's dislikes
        if (!own && Match("giftDislikes", "default", mat)) return -1;
        return Match("giftLikes", "default", mat) ? 1 : 0;
    }
    public static bool CanGift(NpcRec r) => Game.D.Played - Of(r.Id).GiftAt >= N("giftEvery", "", 720f);
    /// <summary>Hands one of a pack material to the NPC. Returns what it says.</summary>
    public static string Gift(NpcRec r, string mat)
    {
        var d = Game.D; var m = Of(r.Id); string name = GatherDb.Mat(mat).Name;
        if (d.Mats.GetValueOrDefault(mat) <= 0) return "...";
        if (!CanGift(r)) return Line(r, "took.soon", name);
        if (--d.Mats[mat] <= 0) d.Mats.Remove(mat);
        int taste = Taste(r, mat); m.GiftAt = d.Played; m.LastGift = name;
        if (taste < 0) { m.BadGifts++; m.Last = "badgift"; } else { m.Gifts++; m.Last = "gift"; }
        string say = Line(r, taste > 0 ? "took.liked" : taste < 0 ? "took.disliked" : "took.neutral", name);
        Add(r, (int)N("gift", taste > 0 ? "liked" : taste < 0 ? "disliked" : "neutral", 3), taste >= 0 ? (int)N("gift", "town", 1) : 0);
        Sfx.Play(taste < 0 ? "click" : "coin"); Game.Save();
        return say;
    }
    public static string Insult(NpcRec r)
    {
        var m = Of(r.Id); m.Insults++; m.Last = "insult"; string say = Line(r, "insulted");
        Add(r, (int)N("insult", "npc", -12), (int)N("insult", "town", 0)); Game.Save();
        return say;
    }
    /// <summary>A quest turned in to this NPC.</summary>
    public static void QuestDone(string npcId)
    {
        if (NpcDb.Get(npcId) is not { } r) return; var m = Of(r.Id); m.Quests++; m.Last = "quest";
        Add(r, (int)N("quest", "npc", 15), (int)N("quest", "town", 6));
    }
    /// <summary>A purchase or sale with this NPC: every few trades it warms a little, up to a cap.</summary>
    public static void Trade(string npcId)
    {
        if (NpcDb.Get(npcId) is not { } r) return; var m = Of(r.Id); m.Trades++; m.Last = "trade";
        int every = Math.Max(1, (int)N("trade", "every", 3)), cap = (int)N("trade", "max", 15), gain = (int)N("trade", "npc", 1);
        if (m.Trades % every == 0 && m.TradeRep < cap) { m.TradeRep += gain; Add(r, gain, 0); }
    }
    /// <summary>A crime against victim (attack, theft) seen by the witnesses (F-32 calls this): all of them remember, the town's standing drops once.</summary>
    public static void Crime(string victimId, IEnumerable<string>? witnessIds = null)
    {
        bool town = false;
        if (NpcDb.Get(victimId) is { } v) { var m = Of(v.Id); m.Crimes++; m.Last = "crime"; Add(v, (int)N("crime", "npc", -30), (int)N("crime", "town", -12)); town = true; }
        if (witnessIds == null) return;
        foreach (var w in witnessIds)
        {
            if (w == victimId || NpcDb.Get(w) is not { } r) continue; var m = Of(r.Id); m.Crimes++; m.Last = "crime";
            Add(r, (int)N("crime", "witness", -8), town ? 0 : (int)N("crime", "town", -12)); town = true;
        }
        Game.Save();
    }

    // ------------------------------------------------------------------ dev command
    static string Show(NpcRec r)
    {
        var m = Of(r.Id);
        return $"{r.Id} {r.Name}: {Tier(m.Rep)} {m.Rep}, town {r.Town} {TownRep(r.Town)}, standing {Standing(r):0.#}, buy x{BuyMul(r.Id):0.00} sell x{SellMul(r.Id):0.00}; met {m.Met} talks {m.Talks} quests {m.Quests} gifts {m.Gifts} badgifts {m.BadGifts} insults {m.Insults} crimes {m.Crimes} trades {m.Trades}; last '{m.Last}' gift '{m.LastGift}'; situation {Situation(r)}{(Refuses(r) ? " REFUSES" : "")}";
    }
    static string Cmd(string[] a)
    {
        Load(); var d = Game.D;
        if (a.Length == 0)
        {
            var sb = new StringBuilder($"remembered NPCs: {d.Npcs.Count}; towns: {string.Join(", ", d.TownRep.Select(kv => kv.Key + " " + kv.Value))}");
            foreach (var kv in d.Npcs) if (NpcDb.Get(kv.Key) is { } r) sb.Append('\n').Append(Show(r));
            return sb.ToString();
        }
        if (a[0] == "test") return Test();
        if (a[0] == "town" && a.Length > 1) { if (a.Length > 2 && int.TryParse(a[2], out int tv)) { d.TownRep[a[1]] = Math.Clamp(tv, -100, 100); Game.Notify(); } return $"{a[1]}: {TownRep(a[1])}"; }
        if (NpcDb.Get(a[0]) is not { } rec) return "no NPC " + a[0];
        if (a.Length > 1)
            switch (a[1])
            {
                case "gift": { string mat = a.Length > 2 ? a[2] : "berries"; d.Mats[mat] = d.Mats.GetValueOrDefault(mat) + 1; Of(rec.Id).GiftAt = -1e9f; return $"taste {Taste(rec, mat)}: \"{Gift(rec, mat)}\"\n{Show(rec)}"; }
                case "insult": return $"\"{Insult(rec)}\"\n{Show(rec)}";
                case "quest": QuestDone(rec.Id); break;
                case "crime": Crime(rec.Id); break;
                case "trade": Trade(rec.Id); break;
                case "greet": return $"[{Situation(rec)}] \"{Greet(rec, rec.Greet.Length > 0 ? rec.Greet[0] : "")}\"\n{Show(rec)}";
                case "set": if (a.Length > 2 && int.TryParse(a[2], out int v)) { var m = Of(rec.Id); m.Met = true; m.Rep = Math.Clamp(v, -100, 100); Game.Notify(); } break;
                case "forget": d.Npcs.Remove(rec.Id); return "forgotten";
            }
        return Show(rec);
    }
    /// <summary>The save round trip: every kind of memory on one NPC, written and read back the way Game.Save / Game.Load do it.</summary>
    static string Test()
    {
        var d = Game.D; var opt = new JsonSerializerOptions { IncludeFields = true };
        string bakN = JsonSerializer.Serialize(d.Npcs, opt), bakT = JsonSerializer.Serialize(d.TownRep, opt); var bakM = new Dictionary<string, int>(d.Mats);
        var sb = new StringBuilder();
        try
        {
            var r = NpcDb.Get("oak_pip")!; d.Npcs.Remove(r.Id); d.TownRep.Remove(r.Town);
            sb.AppendLine($"[first]   \"{Greet(r, r.Greet[0])}\"");
            d.Mats["berries"] = d.Mats.GetValueOrDefault("berries") + 1; sb.AppendLine($"[gift]    \"{Gift(r, "berries")}\" -> next: \"{Greet(r, "")}\"");
            QuestDone(r.Id); sb.AppendLine($"[quest]   \"{Greet(r, "")}\"");
            for (int i = 0; i < 6; i++) Trade(r.Id); sb.AppendLine($"[trade]   \"{Greet(r, "")}\"  buy x{BuyMul(r.Id):0.00} sell x{SellMul(r.Id):0.00}");
            sb.AppendLine($"[insult]  \"{Insult(r)}\" -> next: \"{Greet(r, "")}\"");
            Crime(r.Id); Crime(r.Id); Crime(r.Id); sb.AppendLine($"[crime]   \"{Greet(r, "")}\"  refuses {Refuses(r)}  buy x{BuyMul(r.Id):0.00}");
            string before = JsonSerializer.Serialize(d.Npcs[r.Id], opt) + "|" + TownRep(r.Town);
            var back = JsonSerializer.Deserialize<PlayerStats>(JsonSerializer.Serialize(d, opt), opt)!;   // what Game.Save writes and Game.Load reads
            string after = JsonSerializer.Serialize(back.Npcs[r.Id], opt) + "|" + back.TownRep.GetValueOrDefault(r.Town);
            sb.AppendLine("memory: " + before);
            sb.Append(before == after ? "SAVE ROUND TRIP OK: the reloaded hero has the same memory and town standing" : "SAVE ROUND TRIP FAILED: " + after);
        }
        finally
        {
            d.Npcs = JsonSerializer.Deserialize<Dictionary<string, NpcMem>>(bakN, opt) ?? new(); d.TownRep = JsonSerializer.Deserialize<Dictionary<string, int>>(bakT, opt) ?? new(); d.Mats = bakM; Game.Notify();
        }
        return sb.ToString();
    }
}
