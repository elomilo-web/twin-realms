using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TwinRealms;

/// <summary>One gem kind from data/gems.json "types".</summary>
public class GemType { public string Id = "", Name = "", Stat = "", Arg = ""; public Color Col = Colors.White; public float[] Values = new float[6]; public float Weight = 1; }

/// <summary>
/// Gems (focus F-10; data/gems.json). A gem is a key "type:tier" or "type.arg:tier" (arg = a skill id, a school tag or a life skill),
/// tier 0 Chipped .. 5 Radiant. Loose gems are stacks in PlayerStats.Gems (key -> count); gem dust is the material "gemdust" in
/// PlayerStats.Mats. Gear rolls 1-4 sockets (Item.Sockets); Item.Socketed holds the gem key per socket ("" = empty), and Item.Gems
/// turns them into stat tables for the pipeline's "gems" stage (StatPipeline.cs). Gems never vanish: unsocketing, selling or
/// scrapping gear gives them back to the bag.
/// </summary>
public static class GemDb
{
    public const string Dust = "gemdust";
    static bool _loaded;
    public static readonly Dictionary<string, GemType> Types = new();
    public static string[] TierNames = { "Chipped", "Flawed", "Normal", "Flawless", "Perfect", "Radiant" };
    static int[] _dust = { 1, 3, 8, 20, 45, 100 }, _fuseDust = { 2, 4, 8, 15, 30 }, _removeGold = { 0, 5, 15, 40, 90, 200 };
    static float[] _sockW = { 45, 30, 18, 7 }; static float _sockShift = .25f;
    static float[] _tierW = { 62, 26, 9, 2.5f, .5f, 0 }; static float _tierPerDepth = .06f;
    static float _chance = .03f, _dunChance = .07f, _perDepth = .004f, _bossChance = .6f;
    public static int MaxRankBonus = 3, CutDust = 10;
    public static readonly List<string> Schools = new(), LifeSkills = new();
    public static int MaxTier => TierNames.Length - 1;
    /// <summary>Bumped on every socket change: Item.Gems caches its stat tables against it.</summary>
    public static int Version { get; private set; }

    public static void Load()
    {
        if (_loaded) return; _loaded = true;
        using var doc = JsonUtil.Load("res://data/gems.json"); var r = doc.RootElement;
        float[] Fa(System.Text.Json.JsonElement e) => e.EnumerateArray().Select(x => x.GetSingle()).ToArray();
        int[] Ia(System.Text.Json.JsonElement e) => e.EnumerateArray().Select(x => (int)x.GetSingle()).ToArray();
        if (r.TryGetProperty("sockets", out var s)) { if (s.TryGetProperty("weights", out var w)) _sockW = Fa(w); _sockShift = JsonUtil.F(s, "rarityShift", _sockShift); }
        if (r.TryGetProperty("tiers", out var t))
        {
            if (t.TryGetProperty("names", out var n)) TierNames = n.EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
            if (t.TryGetProperty("dust", out var dd)) _dust = Ia(dd);
            if (t.TryGetProperty("fuseDust", out var fd)) _fuseDust = Ia(fd);
            if (t.TryGetProperty("removeGold", out var rg)) _removeGold = Ia(rg);
        }
        MaxRankBonus = (int)JsonUtil.F(r, "maxRankBonus", 3); CutDust = (int)JsonUtil.F(r, "cutDust", 10);
        if (r.TryGetProperty("schools", out var sc)) foreach (var x in sc.EnumerateArray()) Schools.Add(x.GetString() ?? "");
        if (r.TryGetProperty("lifeSkills", out var ls)) foreach (var x in ls.EnumerateArray()) LifeSkills.Add(x.GetString() ?? "");
        if (r.TryGetProperty("drop", out var dr))
        {
            _chance = JsonUtil.F(dr, "chance", _chance); _dunChance = JsonUtil.F(dr, "dungeonChance", _dunChance); _perDepth = JsonUtil.F(dr, "perDepth", _perDepth);
            _bossChance = JsonUtil.F(dr, "bossChance", _bossChance); _tierPerDepth = JsonUtil.F(dr, "tierPerDepth", _tierPerDepth);
            if (dr.TryGetProperty("tierWeights", out var tw)) _tierW = Fa(tw);
        }
        foreach (var p in r.GetProperty("types").EnumerateObject())
        {
            var g = new GemType { Id = p.Name, Name = JsonUtil.S(p.Value, "n", p.Name), Stat = JsonUtil.S(p.Value, "stat"), Arg = JsonUtil.S(p.Value, "arg"), Col = Color.FromHtml(JsonUtil.S(p.Value, "color", "#ffffff")), Weight = JsonUtil.F(p.Value, "weight", 1) };
            if (p.Value.TryGetProperty("values", out var v)) g.Values = Fa(v);
            Types[g.Id] = g;
        }
        // every key a gem can raise is a known stat key (the character sheet lists them, the bot checks them)
        foreach (var k in new[] { "str", "dex", "vit", "mag" }) TreeDb.Known.Add(k);
        foreach (var g in Types.Values)
        {
            if (g.Arg == "") TreeDb.Known.Add(g.Stat);
            else foreach (var a in Args(g)) TreeDb.Known.Add(g.Stat.Replace("{arg}", a));
        }
    }

    /// <summary>The possible args of a gem kind: skill ids (not crafted spells), schools, or life skills.</summary>
    public static IEnumerable<string> Args(GemType g) => g.Arg switch
    {
        "skill" => Skills.All.Where(s => !s.Crafted).Select(s => s.Id),
        "school" => Schools, "life" => LifeSkills, _ => Enumerable.Empty<string>(),
    };

    // ---- gem keys
    public static string Key(string type, string arg, int tier) => (arg == "" ? type : type + "." + arg) + ":" + Math.Clamp(tier, 0, MaxTier);
    public static (GemType? type, string arg, int tier) Parse(string key)
    {
        Load(); int c = key.LastIndexOf(':'); if (c < 0) return (null, "", 0);
        int tier = int.TryParse(key[(c + 1)..], out var t) ? Math.Clamp(t, 0, MaxTier) : 0;
        string head = key[..c]; int dot = head.IndexOf('.'); string id = dot < 0 ? head : head[..dot], arg = dot < 0 ? "" : head[(dot + 1)..];
        return (Types.GetValueOrDefault(id), arg, tier);
    }
    public static int Tier(string key) => Parse(key).tier;
    public static string Next(string key) { var (g, a, t) = Parse(key); return g == null ? key : Key(g.Id, a, t + 1); }
    public static bool Valid(string key) => Parse(key).type != null;
    static string ArgName(GemType g, string a) => g.Arg == "skill" ? Skills.Get(a)?.Name ?? a : char.ToUpper(a[0]) + a[1..];
    public static string Name(string key)
    {
        var (g, a, t) = Parse(key); if (g == null) return key;
        return $"{TierNames[t]} {g.Name}" + (a != "" ? $" of {ArgName(g, a)}" : "");
    }
    public static Color Col(string key) => Parse(key).type?.Col ?? Colors.White;
    /// <summary>The stat-table keys and amounts this gem adds while socketed.</summary>
    public static Dictionary<string, float> Stats(string key)
    {
        var (g, a, t) = Parse(key); var m = new Dictionary<string, float>(); if (g == null || g.Values.Length == 0) return m;
        m[g.Stat.Replace("{arg}", a)] = g.Values[Math.Min(t, g.Values.Length - 1)]; return m;
    }
    public static string StatText(string key) => string.Join(", ", Stats(key).Select(kv => TreeDb.StatText(kv.Key, kv.Value)));
    public static int DustOf(string key) { int t = Tier(key); return _dust[Math.Min(t, _dust.Length - 1)]; }
    public static int FuseDust(string key) { int t = Tier(key); return t < _fuseDust.Length ? _fuseDust[t] : 0; }
    public static int RemoveGold(string key) { int t = Tier(key); return _removeGold[Math.Min(t, _removeGold.Length - 1)]; }

    // ---- sockets on gear
    /// <summary>1-4 sockets: data weights, shifted toward more sockets by rarity.</summary>
    public static int RollSockets(int rar)
    {
        Load(); float tot = 0; var w = new float[_sockW.Length];
        for (int i = 0; i < w.Length; i++) { w[i] = Mathf.Max(0, _sockW[i] * (1 + _sockShift * rar * i)); tot += w[i]; }
        float x = GD.Randf() * tot; for (int i = 0; i < w.Length; i++) { x -= w[i]; if (x <= 0) return i + 1; }
        return w.Length;
    }
    /// <summary>The socketed gems' stat tables (Item.Gems), cached until a socket changes.</summary>
    public static List<Dictionary<string, float>>? Tables(Item it)
    {
        if (it.Socketed == null || it.Socketed.Count == 0) return null;
        if (it.GemVer == Version && it.GemCache != null) return it.GemCache;
        var l = new List<Dictionary<string, float>>(); foreach (var k in it.Socketed) if (!string.IsNullOrEmpty(k)) l.Add(Stats(k));
        it.GemCache = l; it.GemVer = Version; return l;
    }
    public static string At(Item it, int i) => it.Socketed != null && i < it.Socketed.Count ? it.Socketed[i] ?? "" : "";
    public static int Filled(Item it) => it.Socketed?.Count(k => !string.IsNullOrEmpty(k)) ?? 0;
    public static int FirstEmpty(Item it) { for (int i = 0; i < it.Sockets; i++) if (At(it, i) == "") return i; return -1; }

    // ---- the bag
    public static int Count(PlayerStats d, string key) => d.Gems.GetValueOrDefault(key);
    public static int Total(PlayerStats d) => d.Gems.Values.Sum();
    public static void Give(PlayerStats d, string key, int n = 1) { if (n > 0 && Valid(key)) d.Gems[key] = d.Gems.GetValueOrDefault(key) + n; }
    public static bool Take(PlayerStats d, string key, int n = 1)
    {
        if (d.Gems.GetValueOrDefault(key) < n) return false;
        d.Gems[key] -= n; if (d.Gems[key] <= 0) d.Gems.Remove(key); return true;
    }
    /// <summary>Bag stacks in a stable order: highest tier first, then by name.</summary>
    public static List<string> Stacks(PlayerStats d) => d.Gems.Where(kv => kv.Value > 0 && Valid(kv.Key)).Select(kv => kv.Key).OrderByDescending(Tier).ThenBy(Name, StringComparer.Ordinal).ToList();

    /// <summary>A gem from the bag into socket i of an item (an empty socket, or the first empty one when i &lt; 0). Null = done, else why not.</summary>
    public static string? Socket(PlayerStats d, Item it, int i, string key)
    {
        if (it.Sockets <= 0) return "This item has no sockets";
        if (i < 0) i = FirstEmpty(it); if (i < 0 || i >= it.Sockets) return "Every socket is full: take a gem out first";
        if (At(it, i) != "") return "That socket holds a gem already";
        if (!Take(d, key)) return "You have no such gem";
        it.Socketed ??= new List<string>(); while (it.Socketed.Count < it.Sockets) it.Socketed.Add("");
        it.Socketed[i] = key; Version++; return null;
    }
    /// <summary>A gem out of its socket back into the bag, for its tier's gold cost (data). Null = done, else why not.</summary>
    public static string? Unsocket(PlayerStats d, Item it, int i)
    {
        var key = At(it, i); if (key == "") return "That socket is empty";
        int cost = RemoveGold(key); if (d.Gold < cost) return $"Taking it out costs {cost} gold";
        d.Gold -= cost; it.Socketed![i] = ""; Give(d, key); Version++; return null;
    }
    /// <summary>Gear leaving the hero for good (sold, scrapped): every socketed gem goes back to the bag, free.</summary>
    public static int ReturnAll(PlayerStats d, Item it)
    {
        if (it.Socketed == null) return 0; int n = 0;
        for (int i = 0; i < it.Socketed.Count; i++) if (!string.IsNullOrEmpty(it.Socketed[i])) { Give(d, it.Socketed[i]); it.Socketed[i] = ""; n++; }
        if (n > 0) { Version++; Ui.Note(n == 1 ? "A socketed gem went back to your gem bag" : $"{n} socketed gems went back to your gem bag", new Color("e07ad0")); }
        return n;
    }

    // ---- fuse, break, cut
    /// <summary>3 of a kind + gem dust -> 1 of the next tier.</summary>
    public static string? Fuse(PlayerStats d, string key)
    {
        if (Tier(key) >= MaxTier) return "Already the highest tier";
        if (Count(d, key) < 3) return "Needs 3 of the same gem";
        int dust = FuseDust(key); if (d.Mats.GetValueOrDefault(Dust) < dust) return $"Needs {dust} Gem Dust";
        Take(d, key, 3); d.Mats[Dust] = d.Mats.GetValueOrDefault(Dust) - dust; if (d.Mats[Dust] <= 0) d.Mats.Remove(Dust);
        Give(d, Next(key)); return null;
    }
    /// <summary>One gem into gem dust (its tier's amount).</summary>
    public static int Break(PlayerStats d, string key)
    {
        if (!Take(d, key)) return 0; int n = DustOf(key); d.Mats[Dust] = d.Mats.GetValueOrDefault(Dust) + n; return n;
    }
    /// <summary>A random gem of a tier (drops, cutting); skill gems prefer the hero's own skills.</summary>
    public static string Roll(int tier)
    {
        Load(); var all = Types.Values.ToList(); float tot = all.Sum(g => g.Weight), x = GD.Randf() * tot; var pick = all[^1];
        foreach (var g in all) { x -= g.Weight; if (x <= 0) { pick = g; break; } }
        string arg = "";
        if (pick.Arg != "")
        {
            var opts = pick.Arg == "skill" ? Game.D.Skills.Keys.Where(k => Skills.Get(k) is { Crafted: false }).ToList() : new List<string>();
            if (opts.Count == 0) opts = Args(pick).ToList();
            arg = opts.Count > 0 ? opts[(int)(GD.Randf() * opts.Count) % opts.Count] : "";
            if (arg == "") return Roll(tier);
        }
        return Key(pick.Id, arg, tier);
    }
    /// <summary>A dropped gem's tier: data weights, shifted toward better tiers on deeper floors.</summary>
    public static int RollTier(int depth)
    {
        Load(); float tot = 0; var w = new float[_tierW.Length];
        for (int i = 0; i < w.Length; i++) { w[i] = Mathf.Max(0, _tierW[i] * (1 + _tierPerDepth * depth * i)); tot += w[i]; }
        float x = GD.Randf() * tot; for (int i = 0; i < w.Length; i++) { x -= w[i]; if (x <= 0 && w[i] > 0) return i; }
        return 0;
    }
    /// <summary>Monster kill: maybe a gem (Loot.DropFrom). Bosses drop one more often; dungeon floors drop more and better.</summary>
    public static string? RollDrop(bool boss)
    {
        Load(); int depth = Dungeon.Active ? Dungeon.Depth : 0;
        float c = boss ? _bossChance : Dungeon.Active ? _dunChance + _perDepth * depth : _chance;
        return GD.Randf() < Luck.Chance(c, Luck.Party()) ? Roll(RollTier(depth + (boss ? 3 : 0))) : null;
    }

    // ---- drawing
    /// <summary>A faceted gem: bigger and more facets for higher tiers, a glow from Flawless up.</summary>
    public static void Draw(CanvasItem ci, string key, Vector2 c, float size, float alpha = 1f)
    {
        var (g, _, t) = Parse(key); var col = (g?.Col ?? Colors.White) with { A = alpha }; float s = size / 2f * (.62f + .07f * t);
        if (t >= 3) for (int i = 4; i >= 1; i--) ci.DrawCircle(c, s * (0.7f + i * 0.16f), col with { A = alpha * .06f * (t - 2) });
        int sides = 4 + Math.Min(t, 4); var pts = new Vector2[sides];
        for (int i = 0; i < sides; i++) { float a = -Mathf.Pi / 2 + i * Mathf.Tau / sides; pts[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 1.08f) * s; }
        ci.DrawColoredPolygon(pts, col.Darkened(.15f));
        var top = new Vector2[sides]; for (int i = 0; i < sides; i++) top[i] = c + (pts[i] - c) * .5f + new Vector2(0, -s * .08f);
        ci.DrawColoredPolygon(top, col.Lightened(.3f));
        for (int i = 0; i < sides; i++) ci.DrawLine(pts[i], top[i], new Color(0, 0, 0, .35f * alpha), 1f, true);
        var ring = new Vector2[sides + 1]; pts.CopyTo(ring, 0); ring[^1] = pts[0]; ci.DrawPolyline(ring, new Color(0, 0, 0, .7f * alpha), 1.5f, true);
        ci.DrawCircle(c + new Vector2(-s * .22f, -s * .3f), Mathf.Max(1.2f, s * .1f), new Color(1, 1, 1, .7f * alpha));
        if (t == 0) ci.DrawLine(c + new Vector2(s * .1f, -s * .6f), c + new Vector2(s * .35f, s * .1f), new Color(0, 0, 0, .55f * alpha), 1.2f, true);   // the chip
    }
    /// <summary>Icons.cs fallback: "gem:&lt;key&gt;" ids draw in the gem's colour.</summary>
    public static Color? IconColor(string id) => id.StartsWith("gem:") && Valid(id[4..]) ? Col(id[4..]) : null;

    /// <summary>"/stats dump": every worn item's sockets and gems.</summary>
    public static string DumpLines(PlayerStats d)
    {
        var sb = new System.Text.StringBuilder(); int n = 0;
        sb.AppendLine($"[gems] bag: {Total(d)} gems in {d.Gems.Count} stacks, {d.Mats.GetValueOrDefault(Dust)} Gem Dust; socketed stats go in at the 'gems' stage");
        foreach (var kv in d.Eq)
        {
            var it = kv.Value; if (it == null || it.Sockets <= 0) continue;
            var parts = new List<string>(); for (int i = 0; i < it.Sockets; i++) { var k = At(it, i); parts.Add(k == "" ? "empty" : $"{Name(k)} ({string.Join(", ", Stats(k).Select(s => $"{s.Key} +{s.Value:0.##}"))})"); }
            sb.AppendLine($"    {kv.Key} {it.Name}: {Filled(it)}/{it.Sockets} sockets: {string.Join("; ", parts)}"); n++;
        }
        if (n == 0) sb.AppendLine("    no worn item has sockets");
        return sb.ToString().TrimEnd();
    }
}
