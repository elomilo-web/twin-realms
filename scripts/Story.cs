using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>One story quest's saved state. Status: 0 not started, 1 active, 2 finished, 3 failed.</summary>
public class StoryState
{
    public int Status; public string Stage = "", End = "";       // End: the ending reached ("" = the plain one) or why it failed
    public Dictionary<string, int> N = new();                    // progress per objective of the current stage (by its index)
    public float Since;                                          // PlayerStats.Played when the current stage began (time limits)
    public List<string> Picks = new();                           // the choices made, as "stage:choice index"
}

/// <summary>
/// Focus F-31: story quests written in data (data/story.json), not in code. A quest is a set of stages; a stage has objectives (kill,
/// have, reach, flag, rep, skill, level), an optional time limit and fail condition, and conversations with NPCs whose choices branch to
/// other stages, finish the quest (with a named ending) or fail it. Conditions gate quests, conversations and choices (reputation,
/// items, skills, time of day, other quests, world flags). Effects pay out and change the world: gold, xp, materials, an item,
/// standing with NPCs and towns, world flags, and NPC behaviour (gone from the world, another daily schedule, another greeting).
/// State is saved with the hero (PlayerStats.Story, PlayerStats.Flags). The journal lists active, finished and failed quests.
/// The old kill / wave / duel quests (Quests.cs, data/quests.json) keep working beside these. Dev command: "/story".
/// </summary>
public static class Story
{
    public sealed class Obj { public string Type = "kill", Enemy = "", Mat = "", Poi = "", Flag = "", Npc = "", Skill = "", Text = ""; public int N = 1, Realm = -1; public bool Boss; }
    public sealed class Choice { public string Text = "", Reply = "", Goto = ""; public JsonElement If, Do; }
    public sealed class Talk { public string Npc = "", Ask = "", Say = ""; public bool Early; public JsonElement If; public List<Choice> Choices = new(); }
    public sealed class Stage
    {
        public string Id = "", Text = "", Then = "", OnTimeout = "fail", FailTo = "fail", FailWhy = ""; public float Hours;   // Hours: game hours allowed (0 = no limit)
        public List<Obj> Objs = new(); public List<Talk> Talks = new(); public JsonElement Enter, FailIf;
    }
    public sealed class Quest
    {
        public string Id = "", Name = "", Arc = "", Giver = "", Summary = "", Offer = "", Start = "", Motive = ""; public int Order;
        public JsonElement Requires, Rewards, OnFail; public Dictionary<string, Stage> Stages = new(); public Dictionary<string, string> Endings = new();
    }
    public static readonly List<Quest> All = new(); static readonly Dictionary<string, Quest> _by = new();
    public static readonly Dictionary<string, (string name, string desc)> Arcs = new();
    static bool _loaded; static JsonDocument? _doc;
    public static Quest? Get(string id) { Load(); return _by.GetValueOrDefault(id); }

    static string S(JsonElement e, string k, string d = "") => JsonUtil.S(e, k, d);
    static JsonElement P(JsonElement e, string k) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) ? v : default;

    public static void Load()
    {
        if (_loaded) return; _loaded = true;
        Commands.Register("story", "F-31: story: every quest and its state; story <id>: detail; story <id> start|done|fail|reset|goto <stage>|pick <n>|talk; story check: validates data/story.json; story flags", Cmd);
        if (!FileAccess.FileExists("res://data/story.json")) return;
        _doc = JsonUtil.Load("res://data/story.json"); var r = _doc.RootElement;
        if (r.TryGetProperty("arcs", out var arcs)) foreach (var a in arcs.EnumerateObject()) Arcs[a.Name] = (S(a.Value, "name", a.Name), S(a.Value, "desc"));
        if (!r.TryGetProperty("quests", out var qs)) return;
        foreach (var e in qs.EnumerateArray())
        {
            var q = new Quest { Id = S(e, "id"), Name = S(e, "name"), Arc = S(e, "arc"), Giver = S(e, "giver"), Summary = S(e, "summary"), Offer = S(e, "offer"), Start = S(e, "start"), Motive = S(e, "motive"),
                Order = All.Count, Requires = P(e, "requires"), Rewards = P(e, "rewards"), OnFail = P(e, "onFail") };
            if (e.TryGetProperty("endings", out var en)) foreach (var x in en.EnumerateObject()) q.Endings[x.Name] = x.Value.GetString() ?? "";
            if (e.TryGetProperty("stages", out var ss)) foreach (var sp in ss.EnumerateObject())
            {
                var se = sp.Value; var st = new Stage { Id = sp.Name, Text = S(se, "text"), Then = S(se, "then"), Hours = JsonUtil.F(se, "hours"), OnTimeout = S(se, "onTimeout", "fail"), FailTo = S(se, "failTo", "fail"), FailWhy = S(se, "failWhy"), Enter = P(se, "enter"), FailIf = P(se, "failIf") };
                if (se.TryGetProperty("objectives", out var os)) foreach (var o in os.EnumerateArray())
                    st.Objs.Add(new Obj { Type = S(o, "type", "kill"), Enemy = S(o, "enemy"), Mat = S(o, "mat"), Poi = S(o, "poi"), Flag = S(o, "flag"), Npc = S(o, "npc"), Skill = S(o, "skill"), Text = S(o, "text"), N = (int)JsonUtil.F(o, "n", 1), Realm = (int)JsonUtil.F(o, "realm", -1), Boss = o.TryGetProperty("boss", out var bb) && bb.ValueKind == JsonValueKind.True });
                if (se.TryGetProperty("talks", out var ts)) foreach (var t in ts.EnumerateArray())
                {
                    var tk = new Talk { Npc = S(t, "npc"), Ask = S(t, "ask"), Say = S(t, "say"), Early = t.TryGetProperty("early", out var ea) && ea.ValueKind == JsonValueKind.True, If = P(t, "if") };
                    if (t.TryGetProperty("choices", out var cs)) foreach (var c in cs.EnumerateArray()) tk.Choices.Add(new Choice { Text = S(c, "text"), Reply = S(c, "reply"), Goto = S(c, "goto"), If = P(c, "if"), Do = P(c, "do") });
                    st.Talks.Add(tk);
                }
                q.Stages[st.Id] = st;
            }
            if (q.Start == "" && q.Stages.Count > 0) q.Start = q.Stages.Keys.First();
            if (q.Id == "" || _by.ContainsKey(q.Id)) { GD.PushWarning("story.json: empty or duplicate quest id " + q.Id); continue; }
            All.Add(q); _by[q.Id] = q;
        }
    }

    // ------------------------------------------------------------------ state
    public static StoryState St(string id) { var d = Game.D; d.Story ??= new(); if (!d.Story.TryGetValue(id, out var s) || s == null) d.Story[id] = s = new StoryState(); return s; }
    public static int Status(string id) => Game.D.Story != null && Game.D.Story.TryGetValue(id, out var s) && s != null ? s.Status : 0;
    public static bool Flag(string f) => Game.D.Flags != null && Game.D.Flags.ContainsKey(f);
    public static string FlagValue(string f) => Game.D.Flags != null && Game.D.Flags.TryGetValue(f, out var v) ? v : "";
    static void SetFlag(string f, string v = "1") { Game.D.Flags ??= new(); Game.D.Flags[f] = v; }
    public static IEnumerable<Quest> With(int status) { Load(); return All.Where(q => Status(q.Id) == status); }
    public static Stage? Now(Quest q) { var s = St(q.Id); return s.Status == 1 && q.Stages.TryGetValue(s.Stage, out var st) ? st : null; }

    // world consequences on NPCs (flags "npc:<id>:gone|schedule|line"), read by NpcSchedule, NpcDb and the dialog
    public static bool NpcGone(string id) => Flag("npc:" + id + ":gone");
    public static string NpcDays(string id) => FlagValue("npc:" + id + ":schedule");
    public static string NpcLine(string id) => FlagValue("npc:" + id + ":line");

    // ------------------------------------------------------------------ conditions
    static IEnumerable<string> Strs(JsonElement e) { if (e.ValueKind == JsonValueKind.String) yield return e.GetString() ?? ""; else if (e.ValueKind == JsonValueKind.Array) foreach (var x in e.EnumerateArray()) yield return x.GetString() ?? ""; }
    /// <summary>True when every key of the condition holds (an empty or missing condition is true). Why: the first thing missing, for the journal.</summary>
    public static bool Cond(JsonElement c) => Cond(c, out _);
    public static bool Cond(JsonElement c, out string why)
    {
        why = ""; if (c.ValueKind != JsonValueKind.Object) return true; var d = Game.D;
        foreach (var p in c.EnumerateObject())
        {
            var v = p.Value;
            switch (p.Name)
            {
                case "level": if (d.Level < v.GetInt32()) { why = $"level {v.GetInt32()}"; return false; } break;
                case "gold": if (d.Gold < v.GetInt32()) { why = $"{v.GetInt32()} gold"; return false; } break;
                case "mat": foreach (var m in v.EnumerateObject()) if (d.Mats.GetValueOrDefault(m.Name) < m.Value.GetInt32()) { why = $"{m.Value.GetInt32()} {GatherDb.Mat(m.Name).Name}"; return false; } break;
                case "rep": foreach (var m in v.EnumerateObject()) if ((NpcMemory.Peek(m.Name)?.Rep ?? 0) < m.Value.GetInt32()) { why = $"standing {m.Value.GetInt32()} with {NpcDb.Get(m.Name)?.Name ?? m.Name}"; return false; } break;
                case "repBelow": foreach (var m in v.EnumerateObject()) if ((NpcMemory.Peek(m.Name)?.Rep ?? 0) >= m.Value.GetInt32()) { why = $"standing under {m.Value.GetInt32()} with {NpcDb.Get(m.Name)?.Name ?? m.Name}"; return false; } break;
                case "townRep": foreach (var m in v.EnumerateObject()) if (NpcMemory.TownRep(m.Name) < m.Value.GetInt32()) { why = $"standing {m.Value.GetInt32()} in {NpcMemory.TownName(m.Name)}"; return false; } break;
                case "skill": foreach (var m in v.EnumerateObject()) if (LifeSkills.Level(d, m.Name) < m.Value.GetInt32()) { why = $"{m.Name} level {m.Value.GetInt32()}"; return false; } break;
                case "quest": foreach (var m in v.EnumerateObject())
                    {
                        int st = Status(m.Name); string want = m.Value.GetString() ?? "done";
                        bool ok = want switch { "done" => st == 2, "failed" => st == 3, "active" => st == 1, "none" => st == 0, "over" => st >= 2, _ => St(m.Name).End == want && st == 2 };
                        if (!ok) { why = $"\"{Get(m.Name)?.Name ?? m.Name}\" {want}"; return false; }
                    }
                    break;
                case "oldQuest": foreach (var id in Strs(v)) if (Game.Q(id).S != 3) { why = $"\"{Quests.ById(id)?.Name ?? id}\" done"; return false; } break;
                case "flag": foreach (var f in Strs(v)) if (!Flag(f)) { why = "something that has not happened yet"; return false; } break;
                case "notFlag": foreach (var f in Strs(v)) if (Flag(f)) { why = "too late for that now"; return false; } break;
                case "hour": { float a = v[0].GetSingle(), b = v[1].GetSingle(), h = NpcDb.Hour; bool inn = a <= b ? h >= a && h < b : h >= a || h < b; if (!inn) { why = $"between {a:0}:00 and {b:0}:00"; return false; } break; }
                case "any": { bool one = false; string w = ""; foreach (var x in v.EnumerateArray()) if (Cond(x, out w)) { one = true; break; } if (!one) { why = w; return false; } break; }
                case "not": if (Cond(v)) { why = "too late for that now"; return false; } break;
            }
        }
        return true;
    }

    // ------------------------------------------------------------------ effects
    /// <summary>Applies an effects object (rewards, a choice's "do", a stage's "enter"). Returns a short text of what the hero received.</summary>
    public static string Apply(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) return ""; var d = Game.D; var got = new List<string>();
        foreach (var p in e.EnumerateObject())
        {
            var v = p.Value;
            switch (p.Name)
            {
                case "gold": d.Gold = Math.Max(0, d.Gold + v.GetInt32()); got.Add($"{(v.GetInt32() > 0 ? "+" : "")}{v.GetInt32()} gold"); break;
                case "xp": Player.Instance?.AddXp(v.GetInt32(), Player.Instance.GlobalPosition); got.Add($"+{v.GetInt32()} xp"); break;
                case "valor": d.Valor += v.GetInt32(); got.Add($"+{v.GetInt32()} valor"); break;
                case "mat": foreach (var m in v.EnumerateObject())
                    {
                        int n = m.Value.GetInt32(), have = d.Mats.GetValueOrDefault(m.Name) + n;
                        if (have > 0) d.Mats[m.Name] = have; else d.Mats.Remove(m.Name);
                        got.Add($"{(n > 0 ? "+" : "")}{n} {GatherDb.Mat(m.Name).Name}");
                    }
                    break;
                case "take":   // n of any material whose id starts so ("fish_*"), the biggest stacks first
                    foreach (var m in v.EnumerateObject())
                    {
                        int need = m.Value.GetInt32(); string pre = m.Name.TrimEnd('*');
                        foreach (var kv in d.Mats.Where(kv => kv.Key.StartsWith(pre)).OrderByDescending(kv => kv.Value).ToList()) { int t = Math.Min(need, kv.Value); need -= t; if (kv.Value - t > 0) d.Mats[kv.Key] = kv.Value - t; else d.Mats.Remove(kv.Key); if (need <= 0) break; }
                    }
                    break;
                case "item": { var it = Items.Roll(Math.Max(2, d.Level + 1), v.GetInt32()); if (d.Inv.Count < 28) { d.Inv.Add(it); got.Add(it.Name); Ui.Note("Reward: " + it.Describe(), it.Color); } break; }
                case "rep": foreach (var m in v.EnumerateObject()) NpcMemory.Change(m.Name, m.Value.GetInt32(), 0); break;
                case "townRep": foreach (var m in v.EnumerateObject()) NpcMemory.ChangeTown(m.Name, m.Value.GetInt32()); break;
                case "flag": foreach (var f in Strs(v)) SetFlag(f); break;
                case "clearFlag": foreach (var f in Strs(v)) d.Flags?.Remove(f); break;
                case "note": Ui.Note(v.GetString() ?? "", new Color(.89f, .69f, .29f)); break;
                case "start": foreach (var id in Strs(v)) if (Get(id) is { } q && Status(id) == 0) Accept(q); break;
                case "fail": foreach (var id in Strs(v)) if (Get(id) is { } q && Status(id) == 1) Fail(q, "events overtook it"); break;
                case "npc":   // world consequences: { "<npc id>": { "gone": true | false, "schedule": "<template>", "line": "..." } }
                    foreach (var n in v.EnumerateObject()) foreach (var k in n.Value.EnumerateObject())
                        {
                            string key = "npc:" + n.Name + ":" + k.Name;
                            if (k.Value.ValueKind == JsonValueKind.False || k.Value.ValueKind == JsonValueKind.String && k.Value.GetString() == "") d.Flags?.Remove(key);
                            else SetFlag(key, k.Value.ValueKind == JsonValueKind.String ? k.Value.GetString() ?? "1" : "1");
                        }
                    break;
            }
        }
        Game.Notify(); return string.Join(", ", got);
    }

    // ------------------------------------------------------------------ flow
    public static bool Available(Quest q) => Status(q.Id) == 0 && Cond(q.Requires) && !(NpcDb.Get(q.Giver) is { } g && NpcMemory.Refuses(g));
    public static void Accept(Quest q)
    {
        var s = St(q.Id); if (s.Status == 1) return; s.Status = 1; s.End = ""; s.Picks.Clear();
        Ui.Banner(q.Name, "quest started"); Sfx.Play("click"); Enter(q, q.Start, false);
    }
    static void Enter(Quest q, string to, bool note = true)
    {
        var s = St(q.Id);
        if (to == "" || to == "done" || to.StartsWith("done:")) { Finish(q, to.StartsWith("done:") ? to[5..] : ""); return; }
        if (to == "fail" || to.StartsWith("fail:")) { Fail(q, to.StartsWith("fail:") ? to[5..] : ""); return; }
        if (!q.Stages.TryGetValue(to, out var st)) { GD.PushWarning($"story {q.Id}: no stage {to}"); Finish(q, ""); return; }
        s.Stage = to; s.N.Clear(); s.Since = Game.D.Played;
        Apply(st.Enter);
        if (note) { Ui.Note(q.Name + ": " + st.Text, new Color(.89f, .69f, .29f)); Sfx.Play("click"); }
        Game.Notify(); Game.Save();
    }
    static void Finish(Quest q, string ending)
    {
        var s = St(q.Id); s.Status = 2; s.End = ending; s.Stage = "";
        Ui.Banner(q.Name, "quest complete"); Sfx.Play("level");
        string got = Apply(q.Rewards); if (got != "") Ui.Note(got, new Color(1f, .82f, .29f));
        if (NpcDb.Get(q.Giver) != null) NpcMemory.QuestDone(q.Giver);
        Game.Notify(); Game.Save();
    }
    public static void Fail(Quest q, string why)
    {
        var s = St(q.Id); if (s.Status != 1) return; s.Status = 3; s.End = why; s.Stage = "";
        Ui.Banner(q.Name, "quest failed"); if (why != "") Ui.Note(q.Name + ": " + why, new Color(1f, .6f, .5f)); Sfx.UiError();
        Apply(q.OnFail); Game.Notify(); Game.Save();
    }

    // ------------------------------------------------------------------ objectives
    static (Vector3 at, float r)? Place(string id)
    {
        foreach (var L in Lands.All) foreach (var p in L.Places) if (L.Id + "_" + p.Id == id) return (new Vector3(p.X, 0, p.Z), p.R + 6f);
        if (World.GetPoi(id) is { } poi) return (new Vector3(poi.X, 0, poi.Z), poi.R + 4f);
        if (NpcDb.Get(id) is { } r && NpcSchedule.NodeOf(r.Id) is { } n) return (n.GlobalPosition, 5f);
        return null;
    }
    public static string PlaceName(string id)
    {
        foreach (var L in Lands.All) foreach (var p in L.Places) if (L.Id + "_" + p.Id == id) return p.Name;
        return World.GetPoi(id)?.Name ?? NpcDb.Get(id)?.Name ?? id;
    }
    public static int Have(Quest q, Stage st, int i)
    {
        var o = st.Objs[i]; var d = Game.D; var s = St(q.Id);
        return o.Type switch
        {
            "have" => Math.Min(o.N, o.Mat.EndsWith('*') ? d.Mats.Where(kv => kv.Key.StartsWith(o.Mat[..^1])).Sum(kv => kv.Value) : d.Mats.GetValueOrDefault(o.Mat)),
            "flag" => Flag(o.Flag) ? o.N : 0,
            "rep" => (NpcMemory.Peek(o.Npc)?.Rep ?? 0) >= o.N ? o.N : Math.Max(0, NpcMemory.Peek(o.Npc)?.Rep ?? 0),
            "skill" => Math.Min(o.N, LifeSkills.Level(d, o.Skill)),
            "level" => Math.Min(o.N, d.Level),
            "gold" => Math.Min(o.N, d.Gold),
            _ => Math.Min(o.N, s.N.GetValueOrDefault(i.ToString())),   // kill, reach: counted as they happen
        };
    }
    public static bool ObjsDone(Quest q, Stage st) { for (int i = 0; i < st.Objs.Count; i++) if (Have(q, st, i) < st.Objs[i].N) return false; return true; }
    public static string ObjText(Quest q, Stage st, int i)
    {
        var o = st.Objs[i]; int n = Have(q, st, i);
        string t = o.Text != "" ? o.Text : o.Type switch
        {
            "kill" => $"Defeat {(o.Enemy == "" ? (o.Realm == 1 ? "creatures of the Shadow" : "foes") : EnemyDb.Get(o.Enemy.Split('|')[0]).N)}", "have" => $"Gather {GatherDb.Mat(o.Mat.TrimEnd('*')).Name}",
            "reach" => $"Go to {PlaceName(o.Poi)}", "rep" => $"Earn the trust of {NpcDb.Get(o.Npc)?.Name ?? o.Npc}", "skill" => $"Reach {o.Skill} level {o.N}", "level" => $"Reach level {o.N}", "gold" => $"Have {o.N} gold", _ => o.Type,
        };
        return o.N > 1 && o.Type != "rep" && o.Type != "skill" && o.Type != "level" && o.Type != "gold" ? $"{t} ({n}/{o.N})" : t;
    }

    public static void OnKill(string type, int realm, bool boss)
    {
        Load(); bool any = false;
        foreach (var q in All)
        {
            if (Now(q) is not { } st) continue; var s = St(q.Id);
            for (int i = 0; i < st.Objs.Count; i++)
            {
                var o = st.Objs[i]; if (o.Type != "kill" || s.N.GetValueOrDefault(i.ToString()) >= o.N) continue;
                if (o.Enemy != "" && Array.IndexOf(o.Enemy.Split('|'), type) < 0 || o.Realm >= 0 && o.Realm != realm || o.Boss && !boss) continue;
                s.N[i.ToString()] = s.N.GetValueOrDefault(i.ToString()) + 1; any = true;
                Ui.Note(q.Name + ": " + ObjText(q, st, i), new Color(.89f, .69f, .29f));
            }
        }
        if (any) { Game.Notify(); Advance(); }
    }

    static float _poll;
    /// <summary>Called each frame by the overworld (NpcSchedule): twice a second the places reached, time limits, fail conditions and stages whose work is done.</summary>
    public static void Tick(float dt)
    {
        _poll -= dt; if (_poll > 0) return; _poll = .5f; Load();
        var p = Player.Instance; if (p == null || All.Count == 0 || Game.D.Story == null || Game.D.Story.Count == 0) return;
        foreach (var q in All)
        {
            if (Now(q) is not { } st) continue; var s = St(q.Id);
            for (int i = 0; i < st.Objs.Count; i++)
            {
                var o = st.Objs[i]; if (o.Type != "reach" || s.N.GetValueOrDefault(i.ToString()) > 0 || Place(o.Poi) is not { } pl) continue;
                if (new Vector2(p.GlobalPosition.X - pl.at.X, p.GlobalPosition.Z - pl.at.Z).Length() < pl.r) { s.N[i.ToString()] = o.N; Ui.Note(q.Name + ": reached " + PlaceName(o.Poi), new Color(.89f, .69f, .29f)); Game.Notify(); }
            }
        }
        Advance();
    }
    /// <summary>Game hours left on the current stage's limit (negative: no limit).</summary>
    public static float HoursLeft(Quest q) => Now(q) is { Hours: > 0 } st ? st.Hours - (Game.D.Played - St(q.Id).Since) / 30f : -1f;
    static void Advance()
    {
        foreach (var q in All)
        {
            if (Now(q) is not { } st) continue;
            if (st.FailIf.ValueKind == JsonValueKind.Object && Cond(st.FailIf)) { if (st.FailTo == "fail") Fail(q, st.FailWhy); else Enter(q, st.FailTo); continue; }
            if (st.Hours > 0 && HoursLeft(q) <= 0) { if (st.OnTimeout == "fail") Fail(q, st.FailWhy != "" ? st.FailWhy : "you ran out of time"); else Enter(q, st.OnTimeout); continue; }
            if (st.Talks.Count == 0 && ObjsDone(q, st)) Enter(q, st.Then);   // nobody to report to: straight on (or done)
        }
    }

    // ------------------------------------------------------------------ dialog (Ui.Talk asks for this NPC's options)
    /// <summary>What this NPC can be asked about now: quests it offers and the conversations of active quests.</summary>
    public static List<(string label, Quest q, Talk? talk)> For(string npcId)
    {
        Load(); var l = new List<(string, Quest, Talk?)>();
        foreach (var q in All)
        {
            if (q.Giver == npcId && Available(q)) l.Add(("Quest: " + q.Name, q, null));
            if (Now(q) is not { } st) continue; bool done = ObjsDone(q, st);
            foreach (var t in st.Talks) if (t.Npc == npcId && (done || t.Early) && Cond(t.If)) l.Add((t.Ask != "" ? t.Ask : "About \"" + q.Name + "\"", q, t));
        }
        return l;
    }
    public static bool Visible(Choice c) => Cond(c.If);
    /// <summary>The hero picked a choice (index into talk.Choices; -1 = a talk without choices). Returns the NPC's reply ("" = none).</summary>
    public static string Pick(Quest q, Talk t, int i)
    {
        if (Now(q) is not { } st) return ""; var s = St(q.Id);
        if (i < 0 || i >= t.Choices.Count) { Enter(q, st.Then); return ""; }
        var c = t.Choices[i]; s.Picks.Add(st.Id + ":" + i);
        string got = Apply(c.Do); if (got != "") Ui.Note(got, new Color(1f, .82f, .29f));
        if (c.Goto != "stay") Enter(q, c.Goto == "" ? st.Then : c.Goto);
        return c.Reply;
    }
    public static string Fill(string s) => s.Replace("{name}", Game.D.Name);

    // ------------------------------------------------------------------ dev command and data check
    public static string Check()
    {
        Load(); var sb = new StringBuilder(); int bad = 0; void Bad(string s) { bad++; sb.AppendLine("  BAD " + s); }
        bool Target(string g) => g is "" or "done" or "fail" or "stay" || g.StartsWith("done:") || g.StartsWith("fail:");
        var givers = new HashSet<string>();
        foreach (var q in All)
        {
            if (q.Name == "" || q.Summary == "") Bad($"{q.Id}: name / summary"); if (q.Giver != "" && NpcDb.Get(q.Giver) == null) Bad($"{q.Id}: giver {q.Giver} is not in npcs.json"); givers.Add(q.Giver);
            if (q.Arc != "" && !Arcs.ContainsKey(q.Arc)) Bad($"{q.Id}: arc {q.Arc}");
            if (!q.Stages.ContainsKey(q.Start)) Bad($"{q.Id}: start stage {q.Start}");
            foreach (var st in q.Stages.Values)
            {
                if (!Target(st.Then) && !q.Stages.ContainsKey(st.Then)) Bad($"{q.Id}.{st.Id}: then {st.Then}");
                if (st.OnTimeout != "fail" && !Target(st.OnTimeout) && !q.Stages.ContainsKey(st.OnTimeout)) Bad($"{q.Id}.{st.Id}: onTimeout {st.OnTimeout}");
                if (st.FailTo != "fail" && !Target(st.FailTo) && !q.Stages.ContainsKey(st.FailTo)) Bad($"{q.Id}.{st.Id}: failTo {st.FailTo}");
                foreach (var o in st.Objs)
                {
                    if (o.Type == "kill" && o.Enemy != "") foreach (var en in o.Enemy.Split('|')) if (EnemyDb.Get(en).N == "Enemy") Bad($"{q.Id}.{st.Id}: enemy {en}");
                    if (o.Type == "have" && !o.Mat.EndsWith('*') && GatherDb.Mat(o.Mat).Name == o.Mat) Bad($"{q.Id}.{st.Id}: material {o.Mat}");
                    if (o.Type == "reach" && PlaceName(o.Poi) == o.Poi) Bad($"{q.Id}.{st.Id}: place {o.Poi}");
                    if (o.Type == "rep" && NpcDb.Get(o.Npc) == null) Bad($"{q.Id}.{st.Id}: npc {o.Npc}");
                }
                foreach (var t in st.Talks)
                {
                    if (NpcDb.Get(t.Npc) == null) Bad($"{q.Id}.{st.Id}: talk npc {t.Npc}");
                    foreach (var c in t.Choices) if (!Target(c.Goto) && !q.Stages.ContainsKey(c.Goto)) Bad($"{q.Id}.{st.Id}: choice goto {c.Goto}");
                }
                if (st.Talks.Count == 0 && st.Objs.Count == 0 && st.Hours <= 0 && st.FailIf.ValueKind != JsonValueKind.Object) Bad($"{q.Id}.{st.Id}: a stage with nothing to do");
            }
        }
        int multi = All.Count(q => q.Stages.Count > 1), branch = All.Count(q => q.Stages.Values.Any(s => s.Talks.Any(t => t.Choices.Count > 1))), cond = All.Count(q => q.Requires.ValueKind == JsonValueKind.Object || q.Stages.Values.Any(s => s.Talks.Any(t => t.If.ValueKind == JsonValueKind.Object || t.Choices.Any(c => c.If.ValueKind == JsonValueKind.Object)))),
            fails = All.Count(q => q.Stages.Values.Any(s => s.Hours > 0 || s.FailIf.ValueKind == JsonValueKind.Object || s.Talks.Any(t => t.Choices.Any(c => c.Goto.StartsWith("fail"))))), world = All.Count(q => q.Stages.Values.Any(s => s.Talks.Any(t => t.Choices.Any(c => c.Do.ValueKind == JsonValueKind.Object && (c.Do.TryGetProperty("npc", out _) || c.Do.TryGetProperty("flag", out _) || c.Do.TryGetProperty("townRep", out _))))));
        sb.Insert(0, $"story: {All.Count} quests ({All.Count(q => q.Arc != "")} in {Arcs.Count} arcs, {All.Count(q => q.Arc == "")} side), {givers.Count} quest givers; {multi} with several stages, {branch} with branching choices, {cond} with conditions, {fails} that can fail, {world} that change the world\n");
        foreach (var a in Arcs) sb.AppendLine($"  arc {a.Key} \"{a.Value.name}\": {string.Join(" > ", All.Where(q => q.Arc == a.Key).Select(q => q.Id))}");
        sb.Append(bad == 0 ? "  every quest checks out (stages, targets, NPCs, enemies, materials, places)" : $"  {bad} problems");
        return sb.ToString();
    }
    static string Show(Quest q)
    {
        var s = St(q.Id); var sb = new StringBuilder($"{q.Id} \"{q.Name}\" [{(q.Arc == "" ? "side" : q.Arc)}] giver {q.Giver}: {new[] { "not started", "active", "finished", "failed" }[s.Status]}{(s.End != "" ? " (" + s.End + ")" : "")}{(s.Status == 0 ? (Cond(q.Requires, out var w) ? ", available" : ", needs " + w) : "")}");
        if (Now(q) is { } st)
        {
            sb.Append($"\n  stage {st.Id}: {st.Text}{(st.Hours > 0 ? $"  [{HoursLeft(q):0.0} h left]" : "")}");
            for (int i = 0; i < st.Objs.Count; i++) sb.Append($"\n   {(Have(q, st, i) >= st.Objs[i].N ? "[x]" : "[ ]")} {ObjText(q, st, i)}");
            foreach (var t in st.Talks) { sb.Append($"\n   talk to {t.Npc}{(t.Early ? " (any time)" : "")}: {t.Ask}"); for (int i = 0; i < t.Choices.Count; i++) sb.Append($"\n     {i}. {(Visible(t.Choices[i]) ? "" : "(hidden) ")}{t.Choices[i].Text} -> {(t.Choices[i].Goto == "" ? st.Then : t.Choices[i].Goto)}"); }
        }
        if (s.Picks.Count > 0) sb.Append("\n  choices made: " + string.Join(", ", s.Picks));
        return sb.ToString();
    }
    static string Cmd(string[] a)
    {
        Load(); var d = Game.D;
        if (a.Length == 0) return string.Join("\n", All.Select(q => { var s = St(q.Id); return $"{q.Id,-22} {new[] { "-", "ACTIVE", "done", "FAILED" }[s.Status],-7} {(s.Status == 1 ? s.Stage : s.End)}"; }));
        if (a[0] == "check") return Check();
        if (a[0] == "flags") return d.Flags.Count == 0 ? "no flags" : string.Join("\n", d.Flags.Select(kv => kv.Key + " = " + kv.Value));
        if (a[0] == "resetall") { d.Story.Clear(); d.Flags.Clear(); Game.Notify(); return "every story quest and flag reset"; }
        if (Get(a[0]) is not { } q0) return "no quest " + a[0];
        if (a.Length > 1)
            switch (a[1])
            {
                case "start": d.Story.Remove(q0.Id); Accept(q0); break;
                case "reset": d.Story.Remove(q0.Id); break;
                case "done": if (Status(q0.Id) != 1) Accept(q0); Enter(q0, a.Length > 2 ? "done:" + a[2] : "done"); break;
                case "fail": if (Status(q0.Id) != 1) Accept(q0); Fail(q0, "dev"); break;
                case "late": if (Now(q0) != null) { St(q0.Id).Since -= 1e6f; Advance(); } break;   // the stage's time limit runs out now
                case "goto": if (a.Length > 2) { if (Status(q0.Id) != 1) Accept(q0); Enter(q0, a[2]); } break;
                case "objs": if (Now(q0) is { } so) { for (int i = 0; i < so.Objs.Count; i++) { var o = so.Objs[i]; if (o.Type == "have") d.Mats[o.Mat.TrimEnd('*')] = Math.Max(d.Mats.GetValueOrDefault(o.Mat.TrimEnd('*')), o.N); else if (o.Type == "flag") SetFlag(o.Flag); else if (o.Type == "rep") NpcMemory.Of(o.Npc).Rep = Math.Max(NpcMemory.Of(o.Npc).Rep, o.N); else St(q0.Id).N[i.ToString()] = o.N; } Game.Notify(); Advance(); } break;
                case "pick":
                    if (Now(q0) is { } sp && sp.Talks.Count > 0)
                    {
                        int ti = a.Length > 3 && int.TryParse(a[3], out int tv) ? tv : 0, ci = a.Length > 2 && int.TryParse(a[2], out int cv) ? cv : -1; var t = sp.Talks[Math.Clamp(ti, 0, sp.Talks.Count - 1)];
                        if (ci >= 0 && ci < t.Choices.Count && !Visible(t.Choices[ci])) return "that choice is hidden (condition not met)\n" + Show(q0);
                        string reply = Pick(q0, t, ci); return $"reply: \"{reply}\"\n{Show(q0)}";
                    }
                    break;
            }
        return Show(q0);
    }
}
