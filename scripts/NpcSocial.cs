using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>
/// Focus F-29 / F-30: the NPCs' life between each other. Runs on NpcSchedule's tick over the NPCs that have a node near the hero:
/// - chats: two NPCs close by stop, face each other and trade short lines (gossip, an argument when their tastes clash, a trade when one
///   is a trader, a quick greeting when one is only walking past); people socializing in the same square walk up to each other first.
///   Someone standing near an argument comments on it. The schedule waits while they talk, then they walk on.
/// - the hero: an NPC greets a hero who walks up, by what it remembers (NpcMemory, F-28), with a nod for people it does not dislike.
/// - weather and time: lines when rain starts or stops, at dawn, dusk and nightfall, and when the schedule moves on (work, meal, bed);
///   in the rain the people of the square shelter at the inn (NpcDb.Shelter).
/// Every line is a speech bubble above the head (Npc.Say): short, fading with distance. Lines and numbers: data/npcs.json "bubbles",
/// "social". Client-local like the NPCs themselves. Dev command: "/social".
/// </summary>
public static class NpcSocial
{
    public static float Clock, RainAt = .35f;
    public static bool Log;
    static bool _loaded; static JsonDocument? _doc; static JsonElement _cfg, _bub;
    static readonly System.Random _rng = new();
    static readonly Dictionary<string, float> _pair = new();
    static string _phase = "";

    sealed class Chat { public NpcLive A = null!, B = null!; public List<(int who, string line)> Lines = new(); public int Next; public float At, Deadline; public string Kind = "gossip"; public bool Approach, Onlooked; }
    static readonly List<Chat> _chats = new();

    public static void Init() => Load();
    static void Load()
    {
        if (_loaded) return; _loaded = true;
        Commands.Register("social", "F-29: social: chats running and who is free; social chat [gossip|argue|trade|pass]: the two NPCs nearest the hero talk now; social greet: greetings off cooldown; social log: print what happens", Cmd);
        if (!FileAccess.FileExists("res://data/npcs.json")) return;
        _doc = JsonUtil.Load("res://data/npcs.json"); var r = _doc.RootElement;
        r.TryGetProperty("social", out _cfg); r.TryGetProperty("bubbles", out _bub);
        RainAt = F("rainAt", .35f);
    }
    static float F(string k, float d) => _cfg.ValueKind == JsonValueKind.Object ? JsonUtil.F(_cfg, k, d) : d;
    static float Range(string k, float a, float b)
    {
        if (_cfg.ValueKind == JsonValueKind.Object && _cfg.TryGetProperty(k, out var e) && e.ValueKind == JsonValueKind.Array && e.GetArrayLength() == 2) { a = e[0].GetSingle(); b = e[1].GetSingle(); }
        return a + (float)_rng.NextDouble() * (b - a);
    }
    static float Rnd(float a, float b) => a + (float)_rng.NextDouble() * (b - a);
    static void Print(string s) { if (Log) GD.Print("[SOCIAL] " + s); }

    // ------------------------------------------------------------------ lines
    static string RoleKey(NpcRec r) => r.Role.Contains("guard") || r.Role.Contains("warden") || r.Role.Contains("patrol") ? "guard" : r.Role;
    static string Pick(string key, NpcRec r)
    {
        if (_bub.ValueKind != JsonValueKind.Object) return "";
        JsonElement a;
        bool own = _bub.TryGetProperty(key + "." + RoleKey(r), out a) && _rng.Next(3) > 0;   // the role's own lines two times in three
        if (!own && !_bub.TryGetProperty(key, out a)) return "";
        return a.ValueKind == JsonValueKind.Array && a.GetArrayLength() > 0 ? a[_rng.Next(a.GetArrayLength())].GetString() ?? "" : "";
    }
    static readonly HashSet<string> Titles = new() { "Guard", "Captain", "Elder", "Old", "Sister", "Brother", "Lord", "Lady", "Warden", "Steward", "Vizier", "Archivist", "Master" };
    public static string FirstName(NpcRec r)
    {
        foreach (var w in r.Name.Split(' ')) { string t = w.Trim(','); if (t.Length > 1 && !Titles.Contains(t)) return t is "Villager" or "Farmhand" ? "neighbour" : t; }
        return r.Name;
    }
    static string Any(string[] a, string d) => a.Length == 0 ? d : a[_rng.Next(a.Length)];
    static string Fill(string s, NpcRec me, NpcRec? other)
    {
        if (s.Contains("{other}")) s = s.Replace("{other}", other != null ? FirstName(other) : "friend");
        s = s.Replace("{like}", Any(me.Likes, "a quiet day")).Replace("{dislike}", Any(me.Dislikes, "trouble")).Replace("{name}", Game.D.Name)
            .Replace("{town}", NpcDb.Towns.GetValueOrDefault(me.Town)?.Name ?? "these parts");
        return s.Length > 0 && char.IsLower(s[0]) ? char.ToUpper(s[0]) + s[1..] : s;
    }
    static void Queue(NpcLive L, string line, float delay) { if (line == "") return; L.SayText = Fill(line, L.Rec, null); L.SayAt = Clock + delay; L.SayBy = L.SayAt + 8f; }
    /// <summary>Under the sky (an NPC on its way into a house still counts until it is at the door).</summary>
    static bool Outdoors(NpcLive L) => L.Inside == null || NpcSchedule.Flat(L.N.GlobalPosition, L.Inside.In) > 3.5f;

    // ------------------------------------------------------------------ the tick
    static bool Ok(NpcLive L) => GodotObject.IsInstanceValid(L.N) && !L.Hidden;
    static bool Free(NpcLive L) => Ok(L) && L.Busy <= Clock && L.Partner == null && L.Act.Act != "sleep";
    static float Flat(NpcLive a, NpcLive b) => NpcSchedule.Flat(a.N.GlobalPosition, b.N.GlobalPosition);

    public static void Tick(IEnumerable<NpcLive> all, Player p, float dt)
    {
        Load(); Clock += dt;
        var hp = p.GlobalPosition; float hear = F("hearDist", 30f);
        var vis = new List<NpcLive>(); foreach (var L in all) if (Ok(L) && NpcSchedule.Flat(L.N.GlobalPosition, hp) < hear) vis.Add(L);
        int speaking = vis.Count(l => l.N.Speaking);

        // lines waiting for their moment (at most three bubbles up besides the chats, so the square never fills with text)
        foreach (var L in vis)
        {
            if (L.SayAt < 0 || Clock < L.SayAt) continue;
            if (L.Partner == null && !L.N.Speaking && speaking < 3) { L.SayAt = -1; L.N.Say(L.SayText); speaking++; Print($"{L.Rec.Id}: {L.SayText}"); }
            else L.SayAt = Clock + 1.2f > L.SayBy ? -1 : Clock + 1.2f;   // still talking (or too many bubbles up): a little later, or not at all
        }

        // weather, time of day, the schedule moving on
        bool rain = Weather.Rain > RainAt; float h = NpcDb.Hour;
        string phase = h >= 5 && h < 7 ? "dawn" : h >= 18 && h < 20 ? "dusk" : h >= 21 || h < 4 ? "night" : "day";
        bool turned = _phase != "" && phase != _phase && phase != "day"; _phase = phase;
        foreach (var L in vis)
        {
            if (L.Wet != rain) { L.Wet = rain; if (Outdoors(L) && _rng.NextDouble() < F("weatherChance", .75f)) Queue(L, Pick(rain ? "rain" : "rainstop", L.Rec), Rnd(.3f, 4f)); }
            if (turned && Outdoors(L) && L.SayAt < 0 && _rng.NextDouble() < .4) Queue(L, Pick(phase, L.Rec), Rnd(.5f, 6f));
            if (L.LastAct != L.Act.Act) { L.LastAct = L.Act.Act; if (L.SayAt < 0 && _rng.NextDouble() < F("actChance", .6f)) Queue(L, Pick("act." + L.Act.Act, L.Rec), Rnd(.2f, 2f)); }
        }

        // the hero walks up: one greeting a tick, by what the NPC remembers
        if (!p.Dead && speaking < 3)
            foreach (var L in vis)
            {
                if (!Free(L) || L.GreetCd > Clock || L.N.Speaking) continue; var d = L.N.GlobalPosition - hp;
                if (new Vector2(d.X, d.Z).Length() > F("greetDist", 4.5f) || Mathf.Abs(d.Y) > 2.5f) continue;
                L.GreetCd = Clock + Range("greetEvery", 50, 90); Greet(L, rain, phase == "night"); break;
            }

        // chats
        for (int i = _chats.Count - 1; i >= 0; i--) Run(_chats[i], vis);
        if (_chats.Count < 3) TryStart(vis);
    }

    static void Greet(NpcLive L, bool rain, bool night)
    {
        var r = L.Rec; var m = NpcMemory.Peek(r.Id); int rep = m?.Rep ?? 0;
        string key = m == null || !m.Met ? "unknown" : NpcMemory.Refuses(r) ? "hated" : rep <= -15 ? "disliked" : rep >= 50 ? "trusted" : rep >= 15 ? "liked" : "known";
        string line;
        var own = r.Greet.Where(g => g.Length <= 56).ToArray();
        if (key == "unknown" && own.Length > 0 && _rng.Next(5) < 3) line = own[_rng.Next(own.Length)];   // a stranger hears their usual patter
        else if (rep > -15 && (rain || night) && _rng.Next(4) == 0) line = Pick(rain ? "player.rain" : "player.night", r);
        else line = Pick("player." + key, r);
        L.N.Say(Fill(line, r, null)); if (rep > -15) L.N.Emote("cheer", 1.6f);
        Print($"{r.Id} greets the hero ({key}): {line}");
    }

    static bool Clash(NpcRec a, NpcRec b)
    {
        foreach (var l in a.Likes) foreach (var w in l.ToLowerInvariant().Split(' ')) { string k = w.TrimEnd('s'); if (k.Length >= 4 && b.Dislikes.Any(x => x.ToLowerInvariant().Contains(k))) return true; }
        return false;
    }
    static string KindOf(NpcLive a, NpcLive b)
    {
        if ((Clash(a.Rec, b.Rec) || Clash(b.Rec, a.Rec)) && _rng.Next(2) == 0 || _rng.NextDouble() < F("argueChance", .12f)) return "argue";
        if ((NpcDb.Trades(a.Rec) || NpcDb.Trades(b.Rec)) && _rng.Next(2) == 0) return "trade";
        return "gossip";
    }
    static string PairKey(NpcLive a, NpcLive b) => string.CompareOrdinal(a.Rec.Id, b.Rec.Id) < 0 ? a.Rec.Id + "|" + b.Rec.Id : b.Rec.Id + "|" + a.Rec.Id;

    static void TryStart(List<NpcLive> vis)
    {
        float near = F("chatDist", 2.8f), far = F("approachDist", 9f);
        for (int i = 0; i < vis.Count; i++)
        {
            var a = vis[i]; if (!Free(a) || a.ChatCd > Clock) continue;
            for (int j = i + 1; j < vis.Count; j++)
            {
                var b = vis[j]; if (!Free(b) || b.ChatCd > Clock) continue;
                float d = Flat(a, b); if (d > far || Mathf.Abs(a.N.GlobalPosition.Y - b.N.GlobalPosition.Y) > 1.6f) continue;
                if (_pair.TryGetValue(PairKey(a, b), out float t) && t > Clock) continue;
                bool aw = a.N.Goal != null, bw = b.N.Goal != null;
                if (d <= near) { Start(a, b, aw || bw ? "pass" : KindOf(a, b), false); return; }
                if (!aw && !bw && !a.Moving && !b.Moving && a.Inside == null && b.Inside == null && a.Act.Act == "socialize" && b.Act.Act == "socialize") { Start(a, b, KindOf(a, b), true); return; }
            }
        }
    }

    static void Start(NpcLive a, NpcLive b, string kind, bool approach)
    {
        if (kind == "trade" && NpcDb.Trades(a.Rec) && !NpcDb.Trades(b.Rec)) (a, b) = (b, a);   // the customer speaks first
        float walk = approach ? Flat(a, b) / 1.5f + 3f : 0f;
        var c = new Chat { A = a, B = b, Kind = kind, Approach = approach, At = Clock + .4f, Deadline = Clock + walk };
        if (_bub.ValueKind == JsonValueKind.Object && _bub.TryGetProperty(kind, out var set) && set.ValueKind == JsonValueKind.Array && set.GetArrayLength() > 0)
            foreach (var ln in set[_rng.Next(set.GetArrayLength())].EnumerateArray()) c.Lines.Add((ln[0].GetInt32(), ln[1].GetString() ?? ""));
        if (c.Lines.Count == 0) return;
        float len = c.Lines.Count * F("lineGap", 2.9f) + 1f + walk;
        a.Busy = b.Busy = Clock + len; a.Partner = b; b.Partner = a; a.SayAt = b.SayAt = -1;
        b.N.Goal = null; a.N.Goal = null;
        if (approach)
        {   // a walks up to b and stops a step short
            var pa = a.N.GlobalPosition; var pb = b.N.GlobalPosition; var dir = (pa - pb) with { Y = 0 }; dir = dir.LengthSquared() > .01f ? dir.Normalized() : Vector3.Forward;
            a.N.WalkSpeed = 1.6f; a.N.WalkTo(pb + dir * 1.5f); b.N.FaceAt = pa;
        }
        _chats.Add(c); Print($"chat {kind}{(approach ? " (walks up)" : "")}: {a.Rec.Id} + {b.Rec.Id}, {c.Lines.Count} lines");
    }

    static void End(Chat c)
    {
        foreach (var L in new[] { c.A, c.B })
        {
            L.Partner = null; L.Busy = Clock; L.ChatCd = Clock + Range("chatEvery", 70, 150);
            if (GodotObject.IsInstanceValid(L.N)) L.N.FaceAt = null;
        }
        _pair[PairKey(c.A, c.B)] = Clock + F("pairEvery", 240f); _chats.Remove(c);
    }
    /// <summary>The schedule moved this NPC on (new activity): whatever it was saying ends.</summary>
    public static void Break(NpcLive L) { if (L.Partner == null) return; var c = _chats.Find(x => x.A == L || x.B == L); if (c != null) End(c); else L.Partner = null; }

    static void Run(Chat c, List<NpcLive> vis)
    {
        var a = c.A; var b = c.B;
        if (!Ok(a) || !Ok(b)) { End(c); return; }
        if (c.Approach)
        {
            if (Flat(a, b) <= F("chatDist", 2.8f)) { c.Approach = false; a.N.Goal = null; c.At = Clock + .4f; float len = (c.Lines.Count - c.Next) * F("lineGap", 2.9f) + 1.4f; a.Busy = b.Busy = Clock + len; }
            else if (Clock > c.Deadline || a.N.Stopped != "") { a.N.Stopped = ""; End(c); return; }
            else return;
        }
        a.N.FaceAt = b.N.GlobalPosition; b.N.FaceAt = a.N.GlobalPosition;
        if (Clock < c.At) return;
        if (c.Next >= c.Lines.Count) { End(c); return; }
        var (who, line) = c.Lines[c.Next++]; var sp = who == 0 ? a : b; var ot = who == 0 ? b : a; float gap = F("lineGap", 2.9f);
        string text = Fill(line, sp.Rec, ot.Rec);
        sp.N.Say(text, gap + .5f); sp.N.Emote(c.Kind == "pass" ? "cheer" : "talk", gap); if (c.Kind == "argue") ot.N.Emote("guard", gap);
        c.At = Clock + gap; Print($"  {sp.Rec.Id}: {text}");
        if (c.Kind == "argue" && c.Next >= 2 && !c.Onlooked)
        {   // someone standing by has an opinion
            c.Onlooked = true; float od = F("onlookerDist", 7f);
            var on = vis.Where(l => l != a && l != b && Free(l) && !l.N.Speaking && Flat(l, a) < od).OrderBy(l => Flat(l, a)).FirstOrDefault();
            if (on != null) { Queue(on, Pick("onlooker", on.Rec), 1.3f); on.N.FaceAt = a.N.GlobalPosition; on.Busy = Clock + 4f; var o = on; _late.Add((Clock + 4.2f, () => { if (GodotObject.IsInstanceValid(o.N) && o.Partner == null) o.N.FaceAt = null; })); }
        }
        for (int i = _late.Count - 1; i >= 0; i--) if (Clock >= _late[i].at) { _late[i].run(); _late.RemoveAt(i); }
    }
    static readonly List<(float at, Action run)> _late = new();

    // ------------------------------------------------------------------ dev command
    static string Cmd(string[] a)
    {
        Load(); var p = Player.Instance; if (p == null) return "no hero";
        var near = NpcSchedule.Live.Where(Ok).OrderBy(l => NpcSchedule.Flat(l.N.GlobalPosition, p.GlobalPosition)).ToList();
        if (a.Length > 0 && a[0] == "log") { Log = !Log; return "social log " + (Log ? "on" : "off"); }
        if (a.Length > 0 && a[0] == "greet")
        {   // the nearest NPC greets the hero now, wherever it stands; the others come off cooldown
            foreach (var l in near) l.GreetCd = 0; if (near.Count == 0) return "nobody near";
            Log = true; near[0].GreetCd = Clock + 30f; Greet(near[0], Weather.Rain > RainAt, _phase == "night"); return near[0].Rec.Id + " greets the hero";
        }
        if (a.Length > 0 && a[0] == "stage")
        {   // the two nearest free NPCs are stood face to face in front of the hero and talk (for pictures)
            var two = near.Where(l => l.Partner == null && l.Act.Act != "sleep").Take(2).ToList(); if (two.Count < 2) return "fewer than two NPCs free near the hero";
            var cam = p.GetViewport().GetCamera3D(); var fwd = -cam.GlobalTransform.Basis.Z with { Y = 0 }; fwd = fwd.Normalized(); var right = fwd.Cross(Vector3.Up); var mid = cam.GlobalPosition + fwd * 6.5f;   // in front of the camera
            for (int i = 0; i < 2; i++) { var l = two[i]; var at = mid + right * (i == 0 ? -1.1f : 1.1f); l.N.Goal = null; l.N.GlobalPosition = World.OnGround(at.X, at.Z); l.Busy = 0; l.ChatCd = 0; l.SayAt = -1; }
            Log = true; Start(two[0], two[1], a.Length > 1 ? a[1] : "gossip", false);
            return $"staged: {two[0].Rec.Id} + {two[1].Rec.Id}";
        }
        if (a.Length > 0 && a[0] == "say") { if (near.Count == 0) return "nobody near"; near[0].N.Say(string.Join(" ", a.Skip(1))); return near[0].Rec.Id + " says it"; }
        if (a.Length > 0 && a[0] == "chat")
        {
            var free = near.Where(l => l.Partner == null && l.Act.Act != "sleep").Take(1).ToList();   // the nearest one and whoever is nearest to it
            if (free.Count == 1 && near.Where(l => l != free[0] && l.Partner == null && l.Act.Act != "sleep").OrderBy(l => Flat(l, free[0])).FirstOrDefault() is { } nb) free.Add(nb);
            if (free.Count < 2) return "fewer than two NPCs free near the hero";
            foreach (var l in free) { l.Busy = 0; l.ChatCd = 0; }
            Log = true; Start(free[0], free[1], a.Length > 1 ? a[1] : KindOf(free[0], free[1]), Flat(free[0], free[1]) > F("chatDist", 2.8f));
            return $"chat: {free[0].Rec.Id} + {free[1].Rec.Id}, {Flat(free[0], free[1]):0.0} m apart";
        }
        var sb = new StringBuilder($"social: clock {Clock:0}, phase {_phase}, rain {Weather.Rain:0.00} (shelter {NpcDb.Shelter}), chats {_chats.Count}, NPCs with a node {near.Count}");
        foreach (var c in _chats) sb.Append($"\n  {c.Kind}: {c.A.Rec.Id} + {c.B.Rec.Id}, line {c.Next}/{c.Lines.Count}{(c.Approach ? " (walking up)" : "")}");
        foreach (var l in near.Take(12)) sb.Append($"\n  {l.Rec.Id,-14} {l.Act.Act,-9} {(l.Inside != null ? "indoors" : "outdoors"),-8} {(l.Partner != null ? "with " + l.Partner.Rec.Id : l.Busy > Clock ? "busy" : "free")} {NpcSchedule.Flat(l.N.GlobalPosition, p.GlobalPosition):0} m");
        return sb.ToString();
    }
}
