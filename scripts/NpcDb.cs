using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>A place an NPC goes: house n of a town ("house"), an offset in a town's / POI's frame ("dx", "dz") or a world point ("x", "z").</summary>
public sealed class NpcSpot
{
    public string Poi = ""; public int House = -1; public float Dx, Dz, X, Z; public bool Abs;
    public string Describe(string town) => Abs ? $"({X:0}, {Z:0})" : House >= 0 ? $"house {House} of {(Poi == "" ? town : Poi)}" : $"{(Poi == "" ? town : Poi)} ({Dx:0}, {Dz:0})";
}
public sealed class NpcAct { public float From, To; public string Act = "work", Place = "work"; }
/// <summary>A stretch of road: road n of a land ("home" = the Verdant Isle's data/world.json roads), between fractions A and B of its length.</summary>
public sealed class NpcRoute { public string Land = "home"; public int Road; public float A, B = 1f; }
public sealed class NpcTown { public string Id = "", Name = ""; public float Sq0 = 3.5f, Sq1 = 7f, Patrol = 12f, Cx, Cz; public NpcSpot? Inn; }   // Cx, Cz: square / patrol centre in the town frame

/// <summary>One NPC of data/npcs.json (focus F-27). Idx is the file order: a stable index for F-28..F-32 (memory, quests, guards).</summary>
public sealed class NpcRec
{
    public int Idx; public string Id = "", Name = "", Role = "", Talk = "talk", Area = "town", Town = "", Sex = "", Speech = "plain", Outfit = "talk", Color = "";
    public NpcSpot Home = new(), Work = new(); public NpcRoute? Route; public float Wander;
    public List<NpcAct> Schedule = new();
    public string[] Traits = Array.Empty<string>(), Likes = Array.Empty<string>(), Dislikes = Array.Empty<string>(), Greet = Array.Empty<string>(), Quests = Array.Empty<string>();
    public bool Core;   // also in data/quests.json "npcs": Builder spawns it once and keeps the node (quests, shops); the schedule moves and hides it
    public float Phase => (Math.Abs(Id.GetHashCode()) % 1000) / 1000f;
}

/// <summary>
/// F-27: about 100 named NPCs (data/npcs.json) with role, home, work spot, daily schedule and personality. NpcDb.Get(id) for the
/// later Phase 6 items. Townsfolk are client-local like before (Builder.Npcs: the online server has none); where each one is follows
/// only from the synced time of day (World.TimeOfDay), so every player sees the same people in the same places. NpcSchedule streams
/// the nodes near the hero. The command "/npcs" lists the counts and checks every entry.
/// </summary>
public static class NpcDb
{
    public static readonly List<NpcRec> All = new();
    public static readonly Dictionary<string, NpcTown> Towns = new();
    static readonly Dictionary<string, NpcRec> _by = new();
    static bool _loaded;
    public static NpcRec? Get(string id) => _by.TryGetValue(id, out var r) ? r : null;

    static string[] Arr(JsonElement e, string k) => e.TryGetProperty(k, out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().Select(x => x.GetString() ?? "").ToArray() : Array.Empty<string>();
    static NpcSpot Spot(JsonElement e, string k)
    {
        var s = new NpcSpot(); if (!e.TryGetProperty(k, out var o) || o.ValueKind != JsonValueKind.Object) return s;
        s.Poi = JsonUtil.S(o, "poi"); s.House = o.TryGetProperty("house", out var h) ? h.GetInt32() : -1;
        s.Dx = JsonUtil.F(o, "dx"); s.Dz = JsonUtil.F(o, "dz");
        if (o.TryGetProperty("x", out _)) { s.Abs = true; s.X = JsonUtil.F(o, "x"); s.Z = JsonUtil.F(o, "z"); }
        return s;
    }
    static NpcAct Act(JsonElement a, float shift)
    {
        float W(float h) => ((h + shift) % 24f + 24f) % 24f;
        return new NpcAct { From = W(JsonUtil.F(a, "from")), To = JsonUtil.F(a, "to") >= 24f && shift == 0 ? 24f : W(JsonUtil.F(a, "to")), Act = JsonUtil.S(a, "act", "work"), Place = JsonUtil.S(a, "place", "work") };
    }

    public static void Load()
    {
        if (_loaded) return; _loaded = true; NpcMemory.Init();   // F-28: memory and standing (registers "/rep")
        Commands.Register("npcs", "F-27: NPC counts per town / road / wilderness, a check of every entry, and what they are doing now; npcs <town|road|wild>: each one", a =>
            a.Length == 0 ? Report() : string.Join("\n", All.Where(r => r.Town == a[0] && r.Area == "town" || r.Area == a[0]).Select(r => { var t = Target(r, Hour); return $"{r.Id,-14} {t.act.Act,-9} {t.act.Place,-7} want {t.at.X:0.0},{t.at.Z:0.0}  {NpcSchedule.NodeInfo(r.Id)}"; })));
        Commands.Register("npc", "npc <id> [goto]: where the NPC's schedule has it now; goto puts the hero 6 m away, facing it", a =>
        {
            if (a.Length == 0 || Get(a[0]) is not { } r) return "usage: /npc <id> [goto]";
            var (at, act, hidden, _, _) = Target(r, Hour); var g = float.IsNaN(at.Y) ? World.OnGround(at.X, at.Z) : at;
            string s = $"{r.Id} {r.Name} ({r.Role}, {r.Area} {r.Town}): {act.Act} at {act.Place} {g.X:0.0},{g.Y:0.0},{g.Z:0.0}{(hidden ? " (hidden)" : "")}; {NpcSchedule.NodeInfo(r.Id)}";
            if (a.Length > 1 && a[1] == "goto" && Player.Instance is { } p)
            {
                var from = World.OnGround(g.X + 4.5f, g.Z + 4f, .5f); var d = g - from;
                p.Teleport(from, Mathf.Atan2(-d.X, -d.Z));
                if (p.Rig != null) { p.Rig.Rotation = new Vector3(-.22f, Mathf.Atan2(-d.X, -d.Z) + Mathf.Pi, 0); p.Rig.Distance = 4.5f; }
            }
            return s;
        });
        if (!FileAccess.FileExists("res://data/npcs.json")) return;
        using var doc = JsonUtil.Load("res://data/npcs.json"); var r = doc.RootElement;
        if (r.TryGetProperty("towns", out var ts)) foreach (var t in ts.EnumerateObject())
        {
            var e = t.Value; var tw = new NpcTown { Id = t.Name, Name = JsonUtil.S(e, "name", t.Name), Patrol = JsonUtil.F(e, "patrol", 12f) };
            if (e.TryGetProperty("square", out var sq)) { tw.Sq0 = sq[0].GetSingle(); tw.Sq1 = sq[1].GetSingle(); }
            if (e.TryGetProperty("center", out var ce)) { tw.Cx = ce[0].GetSingle(); tw.Cz = ce[1].GetSingle(); }
            if (e.TryGetProperty("inn", out _)) tw.Inn = Spot(e, "inn");
            Towns[t.Name] = tw;
        }
        var tpl = new Dictionary<string, JsonElement>();
        if (r.TryGetProperty("schedules", out var ss)) foreach (var s in ss.EnumerateObject()) tpl[s.Name] = s.Value.Clone();
        var core = new HashSet<string>();
        if (FileAccess.FileExists("res://data/quests.json")) { using var q = JsonUtil.Load("res://data/quests.json"); foreach (var n in q.RootElement.GetProperty("npcs").EnumerateArray()) core.Add(JsonUtil.S(n, "id")); }
        foreach (var n in r.GetProperty("npcs").EnumerateArray())
        {
            var rec = new NpcRec { Idx = All.Count, Id = JsonUtil.S(n, "id"), Name = JsonUtil.S(n, "name"), Role = JsonUtil.S(n, "role"), Talk = JsonUtil.S(n, "talk", "talk"), Area = JsonUtil.S(n, "area", "town"),
                Town = JsonUtil.S(n, "town"), Sex = JsonUtil.S(n, "sex"), Home = Spot(n, "home"), Work = Spot(n, "work"), Wander = JsonUtil.F(n, "wander"), Quests = Arr(n, "quests") };
            rec.Core = core.Contains(rec.Id);
            if (n.TryGetProperty("route", out var ro)) rec.Route = new NpcRoute { Land = JsonUtil.S(ro, "land", "home"), Road = ro.TryGetProperty("road", out var ri) ? ri.GetInt32() : 0, A = JsonUtil.F(ro, "from", 0f), B = JsonUtil.F(ro, "to", 1f) };
            float shift = JsonUtil.F(n, "shift");
            if (n.TryGetProperty("schedule", out var sc))
            {
                var list = sc.ValueKind == JsonValueKind.String && tpl.TryGetValue(sc.GetString() ?? "", out var t) ? t : sc;
                if (list.ValueKind == JsonValueKind.Array) foreach (var a in list.EnumerateArray()) rec.Schedule.Add(Act(a, shift));
            }
            if (n.TryGetProperty("personality", out var p))
            { rec.Traits = Arr(p, "traits"); rec.Likes = Arr(p, "likes"); rec.Dislikes = Arr(p, "dislikes"); rec.Speech = JsonUtil.S(p, "speech", "plain"); rec.Greet = Arr(p, "greetings"); }
            if (n.TryGetProperty("outfit", out var o)) { rec.Outfit = JsonUtil.S(o, "role", "talk"); rec.Color = JsonUtil.S(o, "color"); }
            if (rec.Id == "" || _by.ContainsKey(rec.Id)) { GD.PushWarning("npcs.json: empty or duplicate id " + rec.Id); continue; }
            All.Add(rec); _by[rec.Id] = rec;
        }
    }

    // ------------------------------------------------------------------ the schedule
    public static float Hour => World.TimeOfDay * 24f;
    static bool In(NpcAct a, float h) => a.From <= a.To ? h >= a.From && h < a.To : h >= a.From || h < a.To;
    public static NpcAct Now(NpcRec r, float h)
    {
        foreach (var a in r.Schedule) if (In(a, h)) return a;
        return r.Schedule.Count > 0 ? r.Schedule[0] : new NpcAct();
    }
    /// <summary>Hours spent on "travel"/road "patrol" so far today: where a traveller is along its road follows from it.</summary>
    static float RoadHours(NpcRec r, float h)
    {
        float t = 0;
        foreach (var a in r.Schedule)
        {
            if (a.Place != "road" || !Roams(a)) continue;
            if (a.From <= a.To) t += Mathf.Clamp(h, a.From, a.To) - a.From;
            else t += Mathf.Max(0f, h - a.From) + Mathf.Min(h, a.To);
        }
        return t;
    }

    // ------------------------------------------------------------------ places
    static (float x, float z) RotF(float x, float z, int r) => (r & 3) switch { 0 => (x, z), 1 => (z, -x), 2 => (-x, -z), _ => (-z, x) };
    static float TownR(string poi)
    {
        if (poi == "village") return 24f;
        foreach (var L in Lands.All) foreach (var p in L.Places) if (L.Id + "_" + p.Id == poi) return p.R;
        return World.GetPoi(poi)?.R ?? 20f;
    }
    /// <summary>Origin and quarter turn of a town / POI frame (the block structure's origin when it was stamped, else its centre).</summary>
    public static (Vector3 o, int rot) Frame(string poi)
    {
        if (NpcHomes.Frames.TryGetValue(poi, out var f)) return (new Vector3(f.x + .5f, f.y, f.z + .5f), f.rot);
        if (poi == "village") return (new Vector3(.5f, float.NaN, .5f), 0);
        foreach (var L in Lands.All) foreach (var p in L.Places) if (L.Id + "_" + p.Id == poi) return (new Vector3(Mathf.Floor(p.X) + .5f, float.NaN, Mathf.Floor(p.Z) + .5f), 0);
        if (World.GetPoi(poi) is { } pp) return (new Vector3(pp.X, float.NaN, pp.Z), 0);
        return (new Vector3(0, float.NaN, 0), 0);
    }
    static Vector3 Local(string poi, float dx, float dz) { var (o, rot) = Frame(poi); var (x, z) = RotF(dx, dz, rot); return new Vector3(o.X + x, float.NaN, o.Z + z); }

    /// <summary>The house a spot means (null: not a house, or the town has no block houses).</summary>
    public static NpcHomes.House? HouseOf(NpcRec r, NpcSpot s)
    {
        if (s.House < 0 || s.Abs) return null;
        var hs = NpcHomes.Of(s.Poi == "" ? r.Town : s.Poi); if (hs == null || hs.Count == 0) return null;
        return hs[s.House % hs.Count];
    }
    /// <summary>World point of a spot for this NPC (Y = NaN: stand on the ground there; a number: a house floor).</summary>
    public static Vector3 Resolve(NpcRec r, NpcSpot s)
    {
        if (s.Abs) return new Vector3(s.X, float.NaN, s.Z);
        string poi = s.Poi == "" ? r.Town : s.Poi;
        if (s.House >= 0)
        {
            if (HouseOf(r, s) is { } h) { int k = r.Idx % 4; return h.In + new Vector3(k % 2 == 0 ? -.7f : .7f, 0, k < 2 ? -.6f : .6f); }
            float a = s.House * 2.39996f, rr = TownR(poi) * .7f; return Local(poi, Mathf.Cos(a) * rr, Mathf.Sin(a) * rr);   // no block houses: a ring round the town
        }
        return Local(poi, s.Dx, s.Dz);
    }
    public static Vector3 Square(NpcRec r)
    {
        var t = Towns.GetValueOrDefault(r.Town); float a = r.Phase * Mathf.Tau, rr = Mathf.Lerp(t?.Sq0 ?? 3.5f, t?.Sq1 ?? 7f, (r.Idx * .37f) % 1f);
        return Local(r.Town, (t?.Cx ?? 0) + Mathf.Cos(a) * rr, (t?.Cz ?? 0) + Mathf.Sin(a) * rr);
    }
    static bool Roams(NpcAct a) => a.Act is "travel" or "patrol";
    public static NpcSpot InnSpot(NpcRec r) => Towns.GetValueOrDefault(r.Town)?.Inn ?? new NpcSpot { House = 0 };
    public static Vector2[]? RoadPts(NpcRoute ro)
    {
        if (ro.Land == "home") return ro.Road < World.Roads.Count ? World.Roads[ro.Road] : null;
        var L = Lands.All.Find(l => l.Id == ro.Land); return L != null && ro.Road < L.Roads.Count ? L.Roads[ro.Road].P : null;
    }
    /// <summary>A point along a road stretch: s metres travelled, back and forth between A and B.</summary>
    public static Vector3 RoadAt(NpcRoute ro, float s)
    {
        var p = RoadPts(ro); if (p == null || p.Length < 2) return Vector3.Zero;
        float len = 0; for (int i = 0; i < p.Length - 1; i++) len += (p[i + 1] - p[i]).Length();
        float a = ro.A * len, b = Mathf.Max(ro.B * len, a + 1f), span = b - a, q = s % (2 * span); if (q < 0) q += 2 * span;
        float at = a + (q < span ? q : 2 * span - q);
        for (int i = 0; i < p.Length - 1; i++)
        {
            float l = (p[i + 1] - p[i]).Length();
            if (at <= l || i == p.Length - 2) { var v = p[i].Lerp(p[i + 1], Mathf.Clamp(at / Mathf.Max(l, .01f), 0, 1)); var side = (p[i + 1] - p[i]).Normalized().Orthogonal() * 1.4f; return new Vector3(v.X + side.X, float.NaN, v.Y + side.Y); }
            at -= l;
        }
        return Vector3.Zero;
    }
    public const float RoadSpeed = 42f;   // metres of road per game hour (1.4 m/s: one game hour is 30 s)

    /// <summary>Where the NPC should be now and whether it is out of sight (asleep indoors / in its bedroll); moving = patrol or road.</summary>
    public static (Vector3 at, NpcAct act, bool hidden, bool moving, NpcHomes.House? house) Target(NpcRec r, float h)
    {
        var a = Now(r, h); bool hid = a.Act == "sleep";
        switch (a.Place)
        {
            case "home": return (Resolve(r, r.Home), a, hid, false, HouseOf(r, r.Home));
            case "inn": { var s = InnSpot(r); return (Resolve(r, s), a, hid, false, HouseOf(r, s)); }
            case "square": return (Square(r), a, hid, false, null);
            case "road":
                if (r.Route != null) return (RoadAt(r.Route, RoadHours(r, h) * RoadSpeed + r.Phase * 300f), a, hid, Roams(a), null);
                break;
            case "patrol":
            {
                var tw = Towns.GetValueOrDefault(r.Town); float rad = tw?.Patrol ?? 12f, th = r.Phase * Mathf.Tau + h * 36f / Mathf.Max(rad, 4f) * (r.Idx % 2 == 0 ? 1 : -1);
                return (Local(r.Town, (tw?.Cx ?? 0) + Mathf.Cos(th) * rad, (tw?.Cz ?? 0) + Mathf.Sin(th) * rad), a, hid, true, null);
            }
        }
        var w = Resolve(r, r.Work);
        if (r.Wander > 0 && HouseOf(r, r.Work) == null)
        {   // wander: a new point near the work spot every game hour
            int slot = (int)h; float an = (slot * 1.7f + r.Phase * 9f), dd = r.Wander * (((slot * 7 + r.Idx) % 5) / 5f);
            w += new Vector3(Mathf.Cos(an) * dd, 0, Mathf.Sin(an) * dd);
        }
        return (w, a, hid, false, HouseOf(r, r.Work));
    }

    // ------------------------------------------------------------------ talk
    static string Pick(string[] a, int k, string d) => a.Length == 0 ? d : a[Math.Abs(k) % a.Length];
    static string Doing(string act) => act switch { "sleep" => "should be asleep", "eat" => "am having my meal", "socialize" => "am catching up with the neighbours", "patrol" => "am on my rounds", "travel" => "am on the road", _ => "am at my work" };
    /// <summary>"Tell me about yourself": who they are, what they like, in their speech style.</summary>
    public static string About(NpcRec r)
    {
        string town = Towns.GetValueOrDefault(r.Town)?.Name ?? (r.Area == "road" ? "the roads" : "the wilds");
        string like = Pick(r.Likes, r.Idx, "a quiet day"), like2 = Pick(r.Likes, r.Idx + 1, like), hate = Pick(r.Dislikes, r.Idx, "trouble"), trait = Pick(r.Traits, r.Idx, "ordinary");
        string doing = Doing(Now(r, Hour).Act);
        return r.Speech switch
        {
            "formal" => $"I am {r.Name}, {r.Role} of {town}. I hold {like} and {like2} in high regard, and I confess {hate} tries my patience. At this hour I {doing}.",
            "gruff" => $"{r.Name}. {Cap(r.Role)}. {town}. Like {like}. Hate {hate}. Now let me be, I {doing}.",
            "cheerful" => $"Oh, hello! I'm {r.Name}, the {r.Role} round {town}! I adore {like}, and {like2} too! {Cap(hate)}? Ugh, no thank you. Right now I {doing}!",
            "poetic" => $"They call me {r.Name}, a {r.Role} beneath the skies of {town}. My heart leans toward {like} as flowers lean to light; {hate} is a winter I would rather not know.",
            "terse" => $"{r.Name}. {r.Role}. I like {like}. Not {hate}.",
            "nervous" => $"M-me? I'm {r.Name}... just the {r.Role}, in {town}. I like {like}, I suppose. Please don't mention {hate}. I {doing}, if that's all right.",
            "sly" => $"Name's {r.Name}. Officially I'm a {r.Role} in {town}. Unofficially? I appreciate {like}, and people who bring it. {Cap(hate)} I avoid. Mostly.",
            "warm" => $"Sit a moment, friend. I'm {r.Name}, {r.Role} here in {town}. Folk say I'm {trait}. I'm partial to {like} and {like2}, and I've no love for {hate}.",
            _ => $"I'm {r.Name}, {r.Role} in {town}. I like {like}; I don't care for {hate}. Right now I {doing}.",
        };
    }
    static string Cap(string s) => s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];
    public static bool Trades(NpcRec r) => r.Role is "merchant" or "innkeeper" or "baker" or "trader" or "pedlar" or "fishmonger" or "alchemist" or "tailor";
    public static bool Smiths(NpcRec r) => r.Role is "blacksmith" or "armourer";

    // ------------------------------------------------------------------ the check ("/npcs" and the dev capture log)
    public static string Report()
    {
        Load(); var sb = new StringBuilder(); float h = Hour;
        sb.AppendLine($"npcs: {All.Count} (town {All.Count(r => r.Area == "town")}, road {All.Count(r => r.Area == "road")}, wild {All.Count(r => r.Area == "wild")}), hour {h:0.0}, spawned {NpcSchedule.Spawned}");
        foreach (var g in All.GroupBy(r => r.Area == "town" ? r.Town : r.Area))
        {
            var acts = g.GroupBy(r => Now(r, h).Act).Select(x => $"{x.Key} {x.Count()}");
            sb.AppendLine($"  {g.Key,-18} {g.Count(),3}  houses {NpcHomes.Of(g.Key)?.Count ?? 0,3}  roles: {string.Join(",", g.Select(r => r.Role).Distinct())}  now: {string.Join(", ", acts)}");
        }
        int bad = 0;
        foreach (var r in All)
        {
            var miss = new List<string>();
            if (r.Name == "") miss.Add("name"); if (r.Role == "") miss.Add("role");
            if (r.Area == "town" && !Towns.ContainsKey(r.Town)) miss.Add("town " + r.Town);
            if (!r.Home.Abs && r.Home.House < 0 && r.Home.Poi == "" && r.Town == "") miss.Add("home");
            if (r.Schedule.Count == 0) miss.Add("schedule");
            else { float cover = 0; for (float t = .25f; t < 24f; t += .5f) if (r.Schedule.Any(a => In(a, t))) cover += .5f; if (cover < 24f) miss.Add($"schedule covers {cover:0}h"); }
            if (r.Schedule.Any(a => a.Place == "road") && (r.Route == null || RoadPts(r.Route) == null)) miss.Add("route");
            if (r.Traits.Length == 0 || r.Likes.Length == 0 || r.Dislikes.Length == 0 || r.Greet.Length == 0) miss.Add("personality");
            if (miss.Count > 0) { bad++; sb.AppendLine($"  BAD {r.Id}: missing {string.Join(", ", miss)}"); }
        }
        sb.Append(bad == 0 ? "  every entry has role, home, schedule (24 h) and personality" : $"  {bad} entries incomplete");
        return sb.ToString();
    }
}

/// <summary>F-27: the block houses of every town (registered while the structures are stamped) and each town's frame, for NPC homes.</summary>
public static class NpcHomes
{
    public sealed class House { public Vector3 In, Door, Out; }
    static readonly Dictionary<string, List<House>> _h = new();
    public static readonly Dictionary<string, (int x, int y, int z, int rot)> Frames = new();
    public static List<House>? Of(string town) => _h.TryGetValue(town, out var l) ? l : null;
    public static void SetFrame(string town, int ox, int oy, int oz, int rot) => Frames[town] = (ox, oy, oz, rot);
    /// <summary>A house of a town: town origin (ox, oy, oz), house centre (px, pz) and quarter turn there, half depth hd (the door is at +hd),
    /// floor lift (stilts) and how far in front of the door the street point is.</summary>
    public static void Add(string town, int ox, int oy, int oz, int px, int pz, int rot, int hd, int lift = 0, int front = 2)
    {
        if (!_h.TryGetValue(town, out var l)) _h[town] = l = new List<House>();
        Vector3 W(int lx, int lz, float y) { var (rx, rz) = Structures.Rot(lx, lz, rot); return new Vector3(ox + px + rx + .5f, y, oz + pz + rz + .5f); }
        var h = new House { In = W(0, 0, oy + lift), Door = W(0, hd, oy + lift), Out = W(0, hd + front, lift > 0 ? float.NaN : oy) };
        if (l.Exists(o => o.In.DistanceTo(h.In) < .5f)) return;   // the world was built again: same houses
        l.Add(h);
    }
}
