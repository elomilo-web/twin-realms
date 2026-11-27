using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>
/// A-33 the Starlight Tavern in Oakhaven (data/tavern.json; format in its "_help"): a block building west of the square (a bar, a
/// kitchen corner, tables and benches, a stage with lamps and a cloth backdrop), stamped like every other structure, registered as a
/// house of the village and made the town's inn, so the villagers whose schedule says "inn" eat and spend their evenings there.
/// Its staff are the elf maids of data/npcs.json (tav_*): <see cref="TavernShow"/> has them serve the tables by day and dance and
/// sing on the stage in the evening; the dialogue gets a menu, a tip and a song request (Ui.TavernOpts).
/// Also here: the stations of the town (a forge block, a smithing table and a crafting table at Dorrin's forge; a crafting table and
/// an alchemy table in the tavern), stamped as structure blocks.
/// AUDIO HOOK: <see cref="ShowOn"/> and <see cref="HeroInside"/> are for the music system (a tavern track while the hero is inside);
/// until it reads them the songs are played note by note on a lute through Instruments.Emit.
/// </summary>
public static class Tavern
{
    public static JsonElement Cfg; static JsonDocument? _doc;
    public static readonly List<string> Staff = new();
    public static int Bx, By, Bz, Rot = 1; public static bool Built;
    /// <summary>The evening show is running (music system: play a tavern track).</summary>
    public static bool ShowOn;
    /// <summary>The hero stands in the tavern.</summary>
    public static bool HeroInside => Built && Player.Instance is { } p && Mathf.Abs(p.GlobalPosition.X - Centre.X) < 7.5f && Mathf.Abs(p.GlobalPosition.Z - Centre.Z) < 7.5f && Mathf.Abs(p.GlobalPosition.Y - By) < 5f;
    public static Vector3 Centre => new(Bx + .5f, By, Bz + .5f);
    public static string Name => _doc != null ? JsonUtil.S(Cfg, "name", "The Tavern") : "The Tavern";
    static byte I(string k) => Blocks.Id(k);

    public static void Load()
    {
        if (_doc != null) return;
        _doc = JsonUtil.Load("res://data/tavern.json"); Cfg = _doc.RootElement; Staff.Clear();
        if (Cfg.TryGetProperty("staff", out var s)) foreach (var x in s.EnumerateArray()) Staff.Add(x.GetString() ?? "");
    }
    public static float N(string path, float d)
    {
        Load(); var e = Cfg;
        foreach (var part in path.Split('.')) { if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(part, out var n)) return d; e = n; }
        return e.ValueKind == JsonValueKind.Number ? e.GetSingle() : d;
    }
    public static string[] Lines(string key) { Load(); return Cfg.TryGetProperty(key, out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().Select(x => x.GetString() ?? "").ToArray() : Array.Empty<string>(); }
    public static bool IsStaff(string id) { Load(); return Staff.Contains(id); }
    /// <summary>World point of a spot in the tavern's own frame (local +Z = the door side), on its floor.</summary>
    public static Vector3 W(float lx, float lz, float up = 0)
    {
        var (rx, rz) = (Rot & 3) switch { 0 => (lx, lz), 1 => (lz, -lx), 2 => (-lx, -lz), _ => (-lz, lx) };
        return new Vector3(Bx + .5f + rx, By + up, Bz + .5f + rz);
    }

    /// <summary>Called once per world build (Builder.BuildAll, before Structures.Finish).</summary>
    public static void Build(Node3D over)
    {
        try
        {
            Load(); Built = false;
            Bx = Mathf.FloorToInt(JsonUtil.F(Cfg, "x", -34)); Bz = Mathf.FloorToInt(JsonUtil.F(Cfg, "z", -4)); Rot = (int)JsonUtil.F(Cfg, "rot", 1) & 3;
            By = Structures.Floor(World.GroundAt(Bx + .5f, Bz + .5f));
            if (Structures.On)
            {
                Structures.Place(MakeBlueprint(), Bx, By, Bz, Rot);
                NpcHomes.Add("village", 0, By, 0, Bx, Bz, Rot, 5);
                TownStations();
                // the tavern is the town's inn, and where its staff live and work (the house index follows from the build order)
                var hs = NpcHomes.Of("village"); int idx = hs?.FindIndex(h => new Vector2(h.In.X - Centre.X, h.In.Z - Centre.Z).Length() < .6f) ?? -1;
                if (idx >= 0 && !Net.Dedicated)
                {
                    NpcDb.Load();
                    if (NpcDb.Towns.TryGetValue("village", out var tw)) tw.Inn = new NpcSpot { House = idx };
                    foreach (var id in Staff) if (NpcDb.Get(id) is { } r) { r.Home = new NpcSpot { House = idx }; r.Work = new NpcSpot { House = idx }; }
                    Built = true;
                }
            }
            if (!Net.Dedicated) over.AddChild(new TavernShow { Name = "TavernShow" });
        }
        catch (Exception e) { GD.PushWarning("[TAVERN] " + e.Message); }
    }

    /// <summary>
    /// The building: a 13 x 11 one-floor house shell (door in the middle of the front), inside x -5..5, z -4..4. Stage: a half-block
    /// platform along the back wall (x -2..2, z -4..-3) under a red cloth backdrop with two wall lamps and two rune lamps. Bar: a dark
    /// counter down the left side (x -3) with the kitchen behind it (cooking station, a chest). Two tables (raised slabs) with stair
    /// benches on the right. Stations in the front corners: a crafting table and an alchemy table.
    /// </summary>
    static Blueprint MakeBlueprint()
    {
        var b = new Blueprint(); const uint seed = 7;
        b.Merge(StructGen.House(StructGen.StyleFor("home", seed), 13, 11, 1, seed), 0, 0, 0);
        byte slab = I("planks_slab"), stairs = I("planks_stairs"), dark = I("dark_planks"), torch = I("torch"), cloth = I("cloth_red"), rune = I("rune_stone");
        for (int x = -2; x <= 2; x++) { b.Set(x, 0, -4, slab); b.Set(x, 0, -3, slab); b.Set(x, 1, -5, cloth); b.Set(x, 2, -5, cloth); }
        b.Set(-3, 1, -5, rune); b.Set(3, 1, -5, rune);
        b.Set(-2, 2, -4, torch, 1 + 3); b.Set(2, 2, -4, torch, 1 + 3);
        for (int z = -1; z <= 3; z++) b.Set(-3, 0, z, dark);
        b.Set(-4, 0, -1, dark);
        b.Set(-5, 0, 1, I("cooking_station")); b.Set(-5, 0, 3, I("chest"), 0); b.Set(-5, 2, 0, torch, 1 + 1);
        foreach (int tz in new[] { 0, 2 })
        {
            b.Set(2, 0, tz, slab, 4);                 // the table top (the upper half of the cell)
            b.Set(1, 0, tz, stairs, 1); b.Set(3, 0, tz, stairs, 0);   // benches: the backs away from the table
        }
        b.Set(5, 0, 4, I("crafting_table")); b.Set(-5, 0, 4, I("alchemy_table"));
        b.Set(5, 2, 0, torch, 1 + 0); b.Set(5, 2, 3, torch, 1 + 0);
        return b;
    }
    /// <summary>Stations at Dorrin's forge: a forge block by the hearth, a smithing table and a crafting table (single structure blocks on the ground).</summary>
    static void TownStations()
    {
        foreach (var (key, x, z) in new[] { ("forge", 25, 1), ("smithing_table", 26, 4), ("crafting_table", 21, 0) })
        {
            byte id = I(key); if (id == 0) continue; var b = new Blueprint(); b.Set(0, 0, 0, id); b.Set(0, 1, 0, 0);
            Structures.Place(b, x, Structures.Floor(World.GroundAt(x + .5f, z + .5f)), z);
        }
    }

    // ---------------------------------------------------------------- tips and requests
    static readonly Dictionary<string, float> _tipAt = new();
    public static int WantSong = -1;
    public static string Tip(Npc n)
    {
        var d = Game.D; int g = (int)N("tip.gold", 15);
        if (d.Gold < g) return "You do not have the gold";
        d.Gold -= g; Sfx.Play("coin");
        if (!_tipAt.TryGetValue(n.Id, out float at) || d.Played - at >= N("tip.every", 180) || d.Played < at)
        { _tipAt[n.Id] = d.Played; NpcMemory.Change(n.Id, (int)N("tip.rep", 3), (int)N("tip.town", 1)); }
        var th = Lines("thanks"); n.Emote("cheer", 2f); Game.Notify();
        return th.Length > 0 ? th[(int)(GD.Randi() % (uint)th.Length)] : "Thank you!";
    }
    public static List<string> Songs()
    {
        Load(); var l = new List<string>();
        if (Cfg.TryGetProperty("songs", out var s)) foreach (var x in s.EnumerateArray()) l.Add(JsonUtil.S(x, "n", "A song"));
        return l;
    }
}

/// <summary>Runs the tavern's staff while the hero is near: serving by day, the stage show in the evening.</summary>
public partial class TavernShow : Node
{
    float _clock, _gap = 4f, _lineT, _noteT; int _song = -1, _line, _note, _turn, _lastCheer = -1;
    readonly Dictionary<string, float> _serveT = new(); readonly HashSet<string> _toSay = new(); readonly HashSet<string> _onStage = new();
    static readonly (float x, float z)[] Spots = { (0f, .6f), (0f, 2.2f), (4f, 1f), (-2f, 1f), (0f, -1.6f), (4f, -1f) };
    static bool InHours(float h, float a, float b) => a <= b ? h >= a && h < b : h >= a || h < b;

    public override void _Process(double delta)
    {
        float dt = (float)delta; _clock += dt;
        var p = Player.Instance; if (p == null || !Tavern.Built) { Tavern.ShowOn = false; return; }
        if (p.GlobalPosition.DistanceTo(Tavern.Centre) > 70f) { Tavern.ShowOn = false; return; }
        var staff = new List<NpcLive>();
        foreach (var id in Tavern.Staff) if (NpcSchedule.LiveOf(id) is { } L && !L.Hidden && !L.N.Down && L.Act.Act == "work") staff.Add(L);
        bool show = staff.Count > 0 && InHours(NpcDb.Hour, Tavern.N("show.from", 19), Tavern.N("show.to", 23.5f));
        if (show != Tavern.ShowOn) { Tavern.ShowOn = show; _song = -1; _gap = 5f; _onStage.Clear(); foreach (var L in staff) { L.N.FaceAt = null; L.N.Goal = null; } }
        for (int i = 0; i < staff.Count; i++)
        {
            var L = staff[i]; L.Busy = NpcSocial.Clock + 1.5f;   // the schedule leaves them to us while we direct them
            if (show) Dance(i, staff.Count, L, dt); else Serve(L, dt);
        }
        if (show) Sing(staff, dt);
    }

    // ---------------------------------------------------------------- by day: from table to table with a word for the guests
    void Serve(NpcLive L, float dt)
    {
        var n = L.N; string id = L.Rec.Id; _onStage.Remove(id);
        float t = _serveT.GetValueOrDefault(id, 2f + (Math.Abs(id.GetHashCode()) % 60) / 10f) - dt; _serveT[id] = t;
        if (n.Goal == null && _toSay.Remove(id))
        {
            var lines = Tavern.Lines("serveLines");
            if (lines.Length > 0 && n.GlobalPosition.DistanceTo(Player.Instance!.GlobalPosition) < 14f) { n.Say(lines[(int)(GD.Randi() % (uint)lines.Length)]); n.Emote("talk", 2.2f); }
        }
        if (t > 0) return;
        float lo = 9, hi = 16; if (Tavern.Cfg.TryGetProperty("serve", out var sv) && sv.TryGetProperty("every", out var ev) && ev.GetArrayLength() >= 2) { lo = ev[0].GetSingle(); hi = ev[1].GetSingle(); }
        _serveT[id] = lo + GD.Randf() * Mathf.Max(0, hi - lo);
        var sp = Spots[(int)(GD.Randi() % (uint)Spots.Length)];
        n.Stopped = ""; n.WalkSpeed = 1.5f; n.WalkTo(Tavern.W(sp.x + (GD.Randf() - .5f) * .6f, sp.z + (GD.Randf() - .5f) * .6f)); _toSay.Add(id);
    }

    // ---------------------------------------------------------------- the evening: in a row on the stage, dancing in step
    void Dance(int i, int count, NpcLive L, float dt)
    {
        var n = L.N; string id = L.Rec.Id;
        var home = Tavern.W((i - (count - 1) / 2f) * 1.15f, -3.5f, .5f);
        if (!_onStage.Contains(id))
        {
            float d = NpcSchedule.Flat(n.GlobalPosition, home);
            if (d < .45f) { _onStage.Add(id); n.Goal = null; }
            else if (n.Stopped != "" || d > 30f) { n.Stopped = ""; n.Goal = null; n.GlobalPosition = home; _onStage.Add(id); }
            else if (n.Goal == null) { n.WalkSpeed = 2f; n.WalkTo(home); }
            return;
        }
        // side steps in time, all together; every few seconds a spin (the look-at point circles her) and a cheer
        float rate = Tavern.N("show.stepRate", 2.4f), amp = Tavern.N("show.step", .45f), spinEvery = Mathf.Max(3f, Tavern.N("show.spinEvery", 8f));
        var right = Tavern.W(1, 0) - Tavern.W(0, 0); var front = Tavern.W(0, 1) - Tavern.W(0, 0);
        float s = Mathf.Sin(_clock * rate), hop = Mathf.Abs(Mathf.Sin(_clock * rate)) * .07f;
        var pos = home + right * (s * amp) + Vector3.Up * hop; n.Goal = null; n.GlobalPosition = pos;
        float ph = _clock % spinEvery, turn = ph < 1.2f ? ph / 1.2f * Mathf.Tau : 0f;
        var look = front.Rotated(Vector3.Up, turn + s * .25f);
        n.FaceAt = pos + look * 3f;
        int beat = (int)(_clock / Mathf.Max(2f, Tavern.N("show.cheerEvery", 6f)));
        if (beat != _lastCheer) { n.Emote("cheer", 1.4f); if (i == count - 1) _lastCheer = beat; }   // everyone on the same beat; the last dancer closes it
    }

    // ---------------------------------------------------------------- songs: one singer at a time, lines as bubbles, notes, the tune on a lute
    void Sing(List<NpcLive> staff, float dt)
    {
        if (!Tavern.Cfg.TryGetProperty("songs", out var songs) || songs.ValueKind != JsonValueKind.Array || songs.GetArrayLength() == 0) return;
        int nSongs = songs.GetArrayLength();
        if (_song < 0)
        {
            _gap -= dt; if (Tavern.WantSong >= 0) _gap = Mathf.Min(_gap, .5f);
            if (_gap > 0 || staff.Any(L => !_onStage.Contains(L.Rec.Id))) return;
            _song = Tavern.WantSong >= 0 ? Tavern.WantSong % nSongs : (int)(GD.Randi() % (uint)nSongs); Tavern.WantSong = -1;
            _line = -1; _lineT = 1.2f; _note = 0; _noteT = 0; _turn++;
            staff[_turn % staff.Count].N.Say("♪ " + JsonUtil.S(songs[_song], "n", "A song") + " ♪", 2.6f);
            return;
        }
        var song = songs[_song]; var singer = staff[_turn % staff.Count].N;
        var lines = song.TryGetProperty("lines", out var lj) && lj.ValueKind == JsonValueKind.Array ? lj : default;
        int nLines = lines.ValueKind == JsonValueKind.Array ? lines.GetArrayLength() : 0; float gapL = Mathf.Max(1.5f, Tavern.N("show.lineGap", 3.6f));
        // the tune, note by note (AUDIO HOOK: replace with a tavern music track when the music system has one)
        _noteT -= dt;
        if (_noteT <= 0 && _line >= 0 && song.TryGetProperty("tune", out var tune) && tune.ValueKind == JsonValueKind.Array && tune.GetArrayLength() > 0)
        {
            _noteT = Mathf.Max(.15f, Tavern.N("show.noteGap", .42f));
            string insId = Tavern.Cfg.TryGetProperty("show", out var sh) ? JsonUtil.S(sh, "instrument", "lute") : "lute";
            if (InstrumentDb.Get(insId) is { } ins)
            {
                int step = tune[_note++ % tune.GetArrayLength()].GetInt32();
                try { Instruments.Emit(singer, ins, Math.Clamp(step, 0, ins.Scale.Length - 1), false); } catch (Exception e) { GD.PushWarning("[TAVERN] note: " + e.Message); _noteT = 9999f; }
            }
            if (_note % 3 == 0) FloatText.Spawn(singer, singer.GlobalPosition + new Vector3((GD.Randf() - .5f) * .8f, 2.3f, (GD.Randf() - .5f) * .8f), "♪", Color.FromHsv(GD.Randf(), .45f, 1f), .8f);
        }
        _lineT -= dt; if (_lineT > 0) return;
        _line++; _lineT = gapL;
        if (_line < nLines) { singer.Say("♪ " + (lines[_line].GetString() ?? "") + " ♪", gapL + .5f); singer.Emote("talk", gapL); return; }
        // the song is over: the room claps and calls for more
        var cheers = Tavern.Lines("cheers"); int called = 0;
        foreach (var L in NpcSchedule.Live)
        {
            if (L.Hidden || Tavern.IsStaff(L.Rec.Id) || NpcSchedule.Flat(L.N.GlobalPosition, Tavern.Centre) > 9f) continue;
            L.N.FaceAt = singer.GlobalPosition; L.N.Emote("cheer", 2.6f);
            if (cheers.Length > 0 && called < 3 && GD.Randf() < .5f) { L.N.Say(cheers[(int)(GD.Randi() % (uint)cheers.Length)], 2.4f); called++; }
        }
        Sfx.Play("level", .25f, 1.3f);
        _song = -1; _gap = Mathf.Max(4f, Tavern.N("show.songEvery", 26f));
    }
}

public partial class Ui
{
    /// <summary>The dialogue options of the tavern's maids (Talk calls this): the menu, a tip, a song request.</summary>
    void TavernOpts(Node v, ref int k, Npc n)
    {
        if (!Tavern.IsStaff(n.Id)) return;
        DlgOpt(v, ++k, "What is on the menu?", () => OpenTavernMenu(n));
        DlgOpt(v, ++k, $"Leave a tip ({(int)Tavern.N("tip.gold", 15)} gold)", () => Say(n, Tavern.Tip(n), ("Back", () => Talk(n))));
        DlgOpt(v, ++k, "Request a song", () =>
        {
            if (!Tavern.ShowOn) { Say(n, $"We sing from dusk, around {(int)Tavern.N("show.from", 19)} o'clock. Come back this evening!", ("Back", () => Talk(n))); return; }
            int g = (int)Tavern.N("request.gold", 30); var songs = Tavern.Songs(); var opts = new List<(string, Action)>();
            for (int i = 0; i < songs.Count; i++)
            {
                int si = i; opts.Add(($"{songs[i]} ({g} gold)", () =>
                {
                    if (Game.D.Gold < g) { Say(n, "Oh... that is not quite enough, I am sorry.", ("Back", () => Talk(n))); return; }
                    Game.D.Gold -= g; Tavern.WantSong = si; NpcMemory.Change(n.Id, (int)Tavern.N("request.rep", 1)); Sfx.Play("coin"); Game.Notify();
                    Say(n, $"{songs[si]}! Watch the stage.", ("Thank you", Close));
                }));
            }
            opts.Add(("Never mind", () => Talk(n)));
            Say(n, "Which song would you like to hear?", opts.ToArray());
        });
    }

    /// <summary>The tavern's menu: cafe dishes and drinks (data/tavern.json "menu": dishes of data/cooking.json), bought into the pack as food.</summary>
    void OpenTavernMenu(Npc n)
    {
        Cooking.Load(); var d = Game.D; var v = Frame("tavern", Tavern.Name, new Vector2(700, 480), () => OpenTavernMenu(n));
        var top = new HBoxContainer(); v.AddChild(top); var hl = Lbl($"{n.NpcName} brings the menu. Food goes into your pack (Materials): click it there to eat.", 13, Dim, top); hl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; hl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        Lbl($"{d.Gold} gold", 18, Gold, top);
        if (Tavern.Cfg.TryGetProperty("menu", out var menu) && menu.ValueKind == System.Text.Json.JsonValueKind.Array)
            foreach (var e in menu.EnumerateArray())
            {
                if (!Cooking.All.TryGetValue(JsonUtil.S(e, "food"), out var r)) continue; int price = Cost((int)JsonUtil.F(e, "price", 20)); var rr = r;
                AddRow(v, $"{r.Name}  (have {d.Mats.GetValueOrDefault(r.Food)})\n{Cooking.BuffText(r)}", r.Col.Lightened(.35f), $"Buy {price}g", () =>
                {
                    if (d.Gold < price) { Sfx.UiError(); Note("Not enough gold", new Color(1, .5f, .5f)); return; }
                    d.Gold -= price; d.Mats[rr.Food] = d.Mats.GetValueOrDefault(rr.Food) + 1; Traded(); Sfx.Play("coin"); Game.Notify(); OpenTavernMenu(n);
                }, d.Gold >= price);
            }
        v.AddChild(Btn("Back", () => Talk(n)));
    }
}
