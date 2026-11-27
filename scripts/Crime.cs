using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>
/// Focus F-32: crime and the town watch. Striking an NPC (a weapon blow or a shot) or picking its pocket (interact while sneaking) is
/// a crime when someone sees it: an awake NPC within reach with a clear line of sight, or the victim itself unless it never noticed.
/// A seen crime is remembered by the witnesses (NpcMemory.Crime, F-28), adds to that town's bounty (saved; it falls by the hour) and
/// calls the watch: guards run to the hero and stop them. The hero pays the fine, resists (the guards turn hostile: real fights, and
/// the bounty grows) or runs (they chase, and give up when outrun; the bounty stays, and any guard who spots the hero later calls the
/// watch again). NPCs needed for a quest cannot die: they are knocked down for a while. Others can be killed, for good.
/// Numbers and lines: data/npcs.json "crime". Client-local like the NPCs. Dev command: "/crime".
/// </summary>
public static class Crime
{
    static bool _loaded; static JsonDocument? _doc; static JsonElement _cfg;
    static readonly System.Random _rng = new();
    static HashSet<string>? _essential;

    // the watch after the hero: guards walking up to arrest, or enemies once the hero resists
    static string _town = ""; static readonly List<Npc> _guards = new(); static readonly List<Enemy> _foes = new();
    static float _chase, _cool, _spotCd, _decay; static bool _fight, _talked; static int _seq;
    public static bool Pursuit => _town != "";

    static void Load()
    {
        if (_loaded) return; _loaded = true;
        Commands.Register("crime", "F-32: crime: bounties and the watch; crime bounty <town> <n>; crime hit <npc> [dmg]; crime pick <npc>; crime alert; crime pay|resist|run; crime clear", Cmd);
        if (!FileAccess.FileExists("res://data/npcs.json")) return;
        _doc = JsonUtil.Load("res://data/npcs.json"); _doc.RootElement.TryGetProperty("crime", out _cfg);
    }
    public static void Init() => Load();
    static float F(string k, float d) => _cfg.ValueKind == JsonValueKind.Object ? JsonUtil.F(_cfg, k, d) : d;
    static float F(string g, string k, float d) => _cfg.ValueKind == JsonValueKind.Object && _cfg.TryGetProperty(g, out var e) && e.ValueKind == JsonValueKind.Object ? JsonUtil.F(e, k, d) : d;
    static string Text(string k, string d) => _cfg.ValueKind == JsonValueKind.Object ? JsonUtil.S(_cfg, k, d) : d;
    static string Line(string k)
    {
        if (_cfg.ValueKind == JsonValueKind.Object && _cfg.TryGetProperty("lines", out var l) && l.TryGetProperty(k, out var a) && a.ValueKind == JsonValueKind.Array && a.GetArrayLength() > 0) return a[_rng.Next(a.GetArrayLength())].GetString() ?? "";
        return "";
    }

    // ------------------------------------------------------------------ who matters, who sees
    /// <summary>An NPC a quest needs (giver or contact of a story quest, or one of the old quest NPCs): knocked down, never killed.</summary>
    public static bool Essential(string id)
    {
        if (_essential == null)
        {
            _essential = new HashSet<string>(); Story.Load();
            foreach (var q in Story.All) { _essential.Add(q.Giver); foreach (var st in q.Stages.Values) { foreach (var t in st.Talks) _essential.Add(t.Npc); foreach (var o in st.Objs) if (o.Npc != "") _essential.Add(o.Npc); } }
            foreach (var r in NpcDb.All) if (r.Core || r.Quests.Length > 0) _essential.Add(r.Id);
        }
        return _essential.Contains(id);
    }
    static bool IsGuard(NpcRec r) => r.Role.Contains("guard") || r.Role.Contains("warden") || r.Role.Contains("patrol");
    static bool Sees(Npc n, Player p)
    {
        var from = n.GlobalPosition + Vector3.Up * 1.6f; var to = p.GlobalPosition + Vector3.Up * 1.2f;
        var q = PhysicsRayQueryParameters3D.Create(from, to, 1); var ex = new Godot.Collections.Array<Rid>();
        foreach (var c in n.GetChildren()) if (c is CollisionObject3D co) ex.Add(co.GetRid());
        ex.Add(p.GetRid()); q.Exclude = ex;
        var hit = p.GetWorld3D().DirectSpaceState.IntersectRay(q);
        if (hit.Count == 0) return true;
        return hit["collider"].As<Node>()?.GetParent() is Npc;   // another villager in the way is no wall
    }
    /// <summary>The awake NPCs who can see the hero now (not the excluded one).</summary>
    static List<NpcLive> Witnesses(Player p, Npc? except)
    {
        float wd = F("witnessDist", 14f); var l = new List<NpcLive>();
        foreach (var L in NpcSchedule.Live)
        {
            if (L.Hidden || L.N == except || L.N.Down || L.Act.Act == "sleep" || !L.N.Visible) continue;
            if (L.N.GlobalPosition.DistanceTo(p.GlobalPosition) > wd || Mathf.Abs(L.N.GlobalPosition.Y - p.GlobalPosition.Y) > 5f) continue;
            if (Sees(L.N, p)) l.Add(L);
        }
        return l;
    }
    /// <summary>The town a crime against this NPC counts in: its own, else the town zone the hero stands in ("" = the wilds: no bounty).</summary>
    static string TownOf(NpcRec? r, Player p)
    {
        if (r != null && r.Area == "town" && r.Town != "") return r.Town;
        return TownZones.At(p.GlobalPosition.X, p.GlobalPosition.Z)?.Id ?? (r?.Town ?? "");
    }
    public static int Bounty(string town) => town != "" && Game.D.Bounty != null && Game.D.Bounty.TryGetValue(town, out int v) ? v : 0;
    static void AddBounty(string town, int n)
    {
        if (town == "" || n == 0) return; Game.D.Bounty ??= new(); int v = Math.Max(0, Bounty(town) + n);
        if (v > 0) Game.D.Bounty[town] = v; else Game.D.Bounty.Remove(town);
    }

    // ------------------------------------------------------------------ crimes
    /// <summary>A crime of this kind against victim. aware: the victim noticed (it then counts as a witness). Returns true when it was seen.</summary>
    static bool Report(string kind, Npc victim, bool aware, Player p)
    {
        var rec = NpcDb.Get(victim.Id); var seen = Witnesses(p, victim);
        if (!aware && seen.Count == 0) { Ui.Note("Nobody saw that.", new Color(.75f, .8f, .9f)); return false; }
        string town = TownOf(rec, p); int add = (int)F("bounty", kind, 40f);
        if (rec != null && aware) NpcMemory.Crime(rec.Id, seen.Select(w => w.Rec.Id)); else foreach (var w in seen.Take(1)) NpcMemory.Crime(w.Rec.Id, seen.Skip(1).Select(x => x.Rec.Id));
        foreach (var w in seen.Take(3))
        {   // they shout; townsfolk back away, guards come at once
            w.N.Say(Line(IsGuard(w.Rec) ? "spot" : kind == "theft" ? "thief" : "saw"), 3f);
            if (!IsGuard(w.Rec)) Flee(w.N, p);
        }
        if (town != "")
        {
            AddBounty(town, add); Ui.Note($"Crime seen ({kind}): bounty in {NpcMemory.TownName(town)} {Bounty(town)} gold", new Color(1f, .5f, .4f)); Sfx.UiError();
            Alert(town, p);
        }
        else Ui.Note($"Crime seen ({kind}). There is no watch out here, but they will remember.", new Color(1f, .6f, .5f));
        Game.Notify(); Game.Save(); return true;
    }
    static void Flee(Npc n, Player p)
    {
        var d = (n.GlobalPosition - p.GlobalPosition) with { Y = 0 }; if (d.LengthSquared() < .01f) d = Vector3.Forward;
        if (NpcSchedule.LiveOf(n.Id) is { } L) { NpcSocial.Break(L); L.Busy = NpcSocial.Clock + 6f; }
        n.FaceAt = null; n.WalkSpeed = 3.4f; n.WalkTo(n.GlobalPosition + d.Normalized() * 9f);
    }

    /// <summary>The hero's blow or shot landed on an NPC.</summary>
    public static void Struck(Npc n, float dmg, Player p)
    {
        Load(); if (n.Down || n.Gone) return;
        if (n.Role == "arrest") { Resist(p); return; }   // hitting a guard who came to arrest you
        var rec = NpcDb.Get(n.Id); if (rec == null) return;
        if (n.MaxHp <= 0) { n.MaxHp = (IsGuard(rec) ? F("guardHp", 160f) : F("npcHp", 70f)) + F("hpPerLevel", 8f) * Game.D.Level; n.Hp = n.MaxHp; }
        bool unaware = p.Sneaking && !Facing(n, p) && dmg >= n.Hp;   // struck down from behind, unseen by the victim
        n.Hp -= dmg; n.HurtAt = NpcSocial.Clock;
        FloatText.Spawn(n, n.GlobalPosition + Vector3.Up * 1.9f, "-" + Mathf.RoundToInt(dmg), new Color(1f, .85f, .5f));
        Sfx.Play("hit"); if (NpcSchedule.LiveOf(n.Id) is { } L) NpcSocial.Break(L);
        if (n.Hp > 0)
        {
            n.Say(Line(_rng.Next(2) == 0 ? "hurt" : "help"), 2.6f); Report("assault", n, true, p);
            if (!IsGuard(rec)) Flee(n, p);
            return;
        }
        if (Essential(n.Id))
        {   // needed for a quest: beaten down, gets up later
            n.KnockDown(F("knockdownSecs", 30f)); n.Say(Line("down"), 2.5f); Report("knockdown", n, !unaware, p);
            return;
        }
        n.Die(); Report("murder", n, !unaware, p);
        Game.D.Flags ??= new(); Game.D.Flags["npc:" + n.Id + ":gone"] = "dead";
        int gold = _rng.Next(4, 22); Game.D.Gold += gold; Ui.Note($"{rec.Name} is dead. (+{gold} gold from the body)", new Color(1f, .5f, .4f));
        if (rec.Town != "") NpcMemory.ChangeTown(rec.Town, -15);
        Game.Save();
    }
    static bool Facing(Npc n, Player p)
    {
        var to = (p.GlobalPosition - n.GlobalPosition) with { Y = 0 }; if (to.LengthSquared() < .01f) return true;
        var fwd = -n.GlobalTransform.Basis.Z with { Y = 0 };
        return fwd.Normalized().Dot(to.Normalized()) > .25f;
    }

    public static bool CanPick(Npc n) => NpcDb.Get(n.Id) != null && !n.Down && n.Role != "arrest" && Game.D.Played - NpcMemory.Of(n.Id).PickAt >= F("pickpocket", "every", 720f);
    public static float PickChance(Npc n, Player p)
    {
        float c = F("pickpocket", "base", .4f) + F("pickpocket", "perLevel", .06f) * LifeSkills.Level(Game.D, "sneaking");
        if (Facing(n, p)) c -= F("pickpocket", "facing", .3f);
        return Mathf.Clamp(c, .05f, F("pickpocket", "max", .92f));
    }
    /// <summary>Interact while sneaking: a hand in the NPC's purse.</summary>
    public static void Pickpocket(Npc n, Player p)
    {
        Load(); var rec = NpcDb.Get(n.Id); if (rec == null) return; var m = NpcMemory.Of(n.Id);
        if (!CanPick(n)) { Ui.Note($"{rec.Name}'s purse is empty for today.", new Color(.75f, .8f, .9f)); return; }
        m.PickAt = Game.D.Played; float chance = PickChance(n, p); bool ok = _rng.NextDouble() < chance;
        if (ok)
        {
            bool rich = NpcDb.Trades(rec) || NpcDb.Smiths(rec) || rec.Outfit == "noble";
            int a = (int)F("pickpocket", "gold", 6), b = 30;
            if (_cfg.ValueKind == JsonValueKind.Object && _cfg.TryGetProperty("pickpocket", out var pk) && pk.TryGetProperty(rich ? "richGold" : "gold", out var gr) && gr.ValueKind == JsonValueKind.Array) { a = gr[0].GetInt32(); b = gr[1].GetInt32(); }
            int gold = _rng.Next(a, b + 1); Game.D.Gold += gold; Sfx.Play("coin");
            Ui.Note($"You lift {gold} gold from {rec.Name} ({chance * 100:0}% chance)", new Color(1f, .82f, .29f));
            LifeSkills.Award("sneaking", (int)F("pickpocket", "xp", 14f));
            Report("theft", n, false, p);   // unnoticed by the victim, but anyone watching saw
        }
        else
        {
            n.Say(Line("thief"), 3f); Ui.Note($"{rec.Name} catches your hand! ({chance * 100:0}% chance)", new Color(1f, .5f, .4f));
            Report("theft", n, true, p);
        }
        Game.Notify();
    }

    // ------------------------------------------------------------------ the watch
    /// <summary>Guards come for the hero in this town (no effect while they are already coming or just gave up).</summary>
    public static void Alert(string town, Player p)
    {
        if (town == "" || Bounty(town) <= 0) return;
        if (Pursuit) { if (!_fight) _chase = 0; return; }
        if (_cool > 0) return;
        _town = town; _fight = false; _talked = false; _chase = 0;
        int n = (int)F("guards", "count", 2f); float sd = F("guards", "spawnDist", 16f); var root = p.GetParent();
        for (int i = 0; i < n; i++)
        {
            float a = (float)_rng.NextDouble() * Mathf.Tau; var at = p.GlobalPosition + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * sd;
            var g = new Npc { Id = "watch_" + (++_seq), NpcName = "Town Guard", Role = "arrest", Outfit = "guard", Sex = i % 2 == 0 ? "m" : "f", Lines = new[] { "Stop right there." }, Position = World.OnGround(at.X, at.Z) };
            root.AddChild(g); g.WalkSpeed = F("guards", "speed", 4.4f); _guards.Add(g);
        }
        Ui.Banner("The watch is coming", NpcMemory.TownName(town) + ": bounty " + Bounty(town) + " gold"); Sfx.Play("block", 1.1f);
    }
    static void EndPursuit(bool cooldown)
    {
        foreach (var g in _guards) if (GodotObject.IsInstanceValid(g)) g.QueueFree();
        _guards.Clear(); _foes.RemoveAll(e => !GodotObject.IsInstanceValid(e) || e.Dead);
        foreach (var e in _foes) e.QueueFree(); _foes.Clear();
        _town = ""; _fight = false; if (cooldown) _cool = 20f;
    }

    /// <summary>Called twice a second from the overworld tick: guards close in, give up, spot a wanted hero; bounties fall by the hour.</summary>
    public static void Tick(float dt)
    {
        Load(); var p = Player.Instance; if (p == null) return;
        if (_cool > 0) _cool -= dt; if (_spotCd > 0) _spotCd -= dt;
        // bounties fall with time played (30 s = one game hour)
        _decay += dt;
        if (_decay >= 30f && Game.D.Bounty != null && Game.D.Bounty.Count > 0)
        {
            _decay = 0; int per = (int)F("decayPerHour", 8f);
            foreach (var t in Game.D.Bounty.Keys.ToList())
            {
                if (t == _town) continue;   // not while the watch is after you
                AddBounty(t, -per); if (Bounty(t) <= 0) Ui.Note($"Your bounty in {NpcMemory.TownName(t)} has lapsed.", new Color(.6f, .9f, .6f));
            }
        }
        else if (_decay >= 30f) _decay = 0;

        if (!Pursuit)
        {   // wanted, and a guard of that town sees you: the watch is called again
            if (_spotCd > 0 || _cool > 0 || Game.D.Bounty == null || Game.D.Bounty.Count == 0 || p.Dead) return; _spotCd = 1f;
            float sp = F("guards", "spotDist", 11f);
            foreach (var L in NpcSchedule.Live)
            {
                if (L.Hidden || !IsGuard(L.Rec) || L.Act.Act == "sleep" || L.N.Down || Bounty(L.Rec.Town) <= 0) continue;
                if (L.N.GlobalPosition.DistanceTo(p.GlobalPosition) > sp || !Sees(L.N, p)) continue;
                L.N.Say(Line("spot"), 3f); Alert(L.Rec.Town, p); break;
            }
            return;
        }
        if (p.Dead) { EndPursuit(true); return; }
        if (_fight)
        {
            _foes.RemoveAll(e => !GodotObject.IsInstanceValid(e) || e.Dead);
            bool far = _foes.Count > 0 && _foes.All(e => e.GlobalPosition.DistanceTo(p.GlobalPosition) > F("guards", "giveUpDist", 70f));
            if (_foes.Count == 0) { Ui.Note("The guards are down. The bounty stands, and it has grown.", new Color(1f, .6f, .5f)); EndPursuit(true); }
            else if (far) { Ui.Note("You have lost the watch. For now.", new Color(.75f, .8f, .9f)); EndPursuit(true); }
            return;
        }
        _guards.RemoveAll(g => !GodotObject.IsInstanceValid(g));
        if (_guards.Count == 0) { EndPursuit(false); return; }
        float best = 1e9f;
        foreach (var g in _guards)
        {
            float d = NpcSchedule.Flat(g.GlobalPosition, p.GlobalPosition); best = Mathf.Min(best, d);
            if (d > 2.6f) { if (g.Stopped != "") { g.Stopped = ""; var dir = (p.GlobalPosition - g.GlobalPosition).Normalized(); var np = g.GlobalPosition + dir * 2.5f; g.GlobalPosition = World.OnGround(np.X, np.Z); } g.WalkTo(p.GlobalPosition); }
            else g.Goal = null;
        }
        _chase += dt;
        if (best > F("guards", "giveUpDist", 70f) || _talked && _chase > F("guards", "giveUpSecs", 25f) && best > 12f)
        {
            if (_guards.Count > 0) _guards[0].Say(Line("lost"), 3f);
            Ui.Note("You have lost the watch. The bounty stands.", new Color(.75f, .8f, .9f)); EndPursuit(true); return;
        }
        if (best <= 3f && (!_talked || _chase > 6f) && Ui.I != null && !Ui.AnyOpen) { _talked = true; _chase = 0; Confront(); }
    }

    /// <summary>The guards have the hero: the arrest window (Ui.Arrest). Also opened by talking to a guard who came for you.</summary>
    public static void Confront()
    {
        if (!Pursuit || _fight) return;
        Ui.I?.Arrest(NpcMemory.TownName(_town), Bounty(_town), Text("arrest", "Stop! The bounty on your head is {bounty} gold.").Replace("{town}", NpcMemory.TownName(_town)).Replace("{bounty}", Bounty(_town).ToString()));
    }
    public static string Pay()
    {
        if (!Pursuit) return "nobody is after you"; var d = Game.D; int b = Bounty(_town); string town = NpcMemory.TownName(_town), say;
        if (d.Gold >= b) { d.Gold -= b; say = Text("paid", "Your fine is paid.").Replace("{town}", town); Sfx.Play("coin"); }
        else
        {   // cannot pay: everything you carry in coin, and out of town you go
            d.Gold = 0; say = Text("broke", "We take what you have.").Replace("{town}", town); NpcMemory.ChangeTown(_town, -5);
            if (TownZones.All.Find(z => z.Id == _town) is { } z && Player.Instance is { } p) { var at = World.OnGround(z.X, z.Z + z.R + 6f, .5f); p.Teleport(at, 0f); }
        }
        d.Bounty?.Remove(_town); Ui.Note(say, new Color(.9f, .85f, .7f)); EndPursuit(false); Game.Notify(); Game.Save(); return say;
    }
    public static void Resist(Player p)
    {
        if (!Pursuit || _fight) return; _fight = true;
        AddBounty(_town, (int)F("bounty", "resist", 150f)); Ui.Note($"Resisting arrest! Bounty in {NpcMemory.TownName(_town)}: {Bounty(_town)} gold", new Color(1f, .5f, .4f)); Sfx.UiError();
        foreach (var g in _guards)
        {
            if (!GodotObject.IsInstanceValid(g)) continue;
            var e = new Enemy { Type = "townGuard", Level = Math.Max(1, Game.D.Level), AnyRealm = true, NoLeash = true, Position = g.GlobalPosition };
            p.GetParent().AddChild(e); _foes.Add(e); g.QueueFree();
        }
        _guards.Clear(); Game.Notify();
    }
    public static void Run() { _chase = 0; _talked = true; Ui.Note("You run for it!", new Color(.75f, .8f, .9f)); }
    /// <summary>A kill (Quests.OnKill): a guard of the watch killed adds to the bounty.</summary>
    public static void OnKill(string type)
    {
        if (type != "townGuard" || _town == "") return;
        AddBounty(_town, (int)F("bounty", "guardKill", 250f)); NpcMemory.ChangeTown(_town, -10);
        Ui.Note($"You killed a guard of the watch. Bounty in {NpcMemory.TownName(_town)}: {Bounty(_town)} gold", new Color(1f, .5f, .4f)); Game.Notify();
    }

    // ------------------------------------------------------------------ dev command
    static string Cmd(string[] a)
    {
        Load(); var p = Player.Instance; if (p == null) return "no hero"; var d = Game.D; d.Bounty ??= new();
        string State() => $"bounties: {(d.Bounty.Count == 0 ? "none" : string.Join(", ", d.Bounty.Select(kv => kv.Key + " " + kv.Value)))}; watch: {(Pursuit ? (_fight ? $"fighting {_foes.Count} guards" : $"{_guards.Count} guards coming, nearest {(_guards.Count > 0 ? _guards.Min(g => NpcSchedule.Flat(g.GlobalPosition, p.GlobalPosition)) : 0):0.0} m") + " in " + _town : "quiet")}; gold {d.Gold}";
        if (a.Length == 0) return State();
        switch (a[0])
        {
            case "bounty": if (a.Length > 2 && int.TryParse(a[2], out int bv)) { d.Bounty.Remove(a[1]); AddBounty(a[1], bv); Game.Notify(); } break;
            case "clear": d.Bounty.Clear(); EndPursuit(false); _cool = 0; foreach (var k in d.Flags.Keys.Where(k => d.Flags[k] == "dead").ToList()) d.Flags.Remove(k); Game.Notify(); break;
            case "alert": { string t = a.Length > 1 ? a[1] : TownZones.At(p.GlobalPosition.X, p.GlobalPosition.Z)?.Id ?? "village"; if (Bounty(t) <= 0) AddBounty(t, 100); _cool = 0; Alert(t, p); break; }
            case "pay": return Pay() + "\n" + State();
            case "resist": Resist(p); break;
            case "run": Run(); break;
            case "confront": Confront(); break;
            case "near": foreach (var g in _guards) { var at = p.GlobalPosition + new Vector3(1.6f + _guards.IndexOf(g) * 1.2f, 0, -2.2f); g.GlobalPosition = World.OnGround(at.X, at.Z); g.Goal = null; } break;   // the guards are stood in front of the hero (pictures)
            case "hit":
            case "pick":
                {
                    if (a.Length < 2 || NpcSchedule.NodeOf(a[1]) is not { } n) return "no node for that NPC (must be near)";
                    var seen = Witnesses(p, n); string who = $"witnesses: {(seen.Count == 0 ? "none" : string.Join(", ", seen.Select(w => w.Rec.Id)))}; essential {Essential(n.Id)}";
                    if (a[0] == "pick") { NpcMemory.Of(n.Id).PickAt = -1e9f; float ch = PickChance(n, p); Pickpocket(n, p); return $"pickpocket {a[1]} at {ch * 100:0}%; {who}\n{State()}"; }
                    Struck(n, a.Length > 2 && float.TryParse(a[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float dm) ? dm : 30f, p);
                    return $"hit {a[1]}: hp {n.Hp:0}/{n.MaxHp:0}{(n.Down ? " DOWN" : "")}{(n.Gone ? " DEAD" : "")}; {who}\n{State()}";
                }
        }
        return State();
    }
}
