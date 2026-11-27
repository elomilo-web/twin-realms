using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace TwinRealms;

public sealed class MusicTrack
{
    public string Id = "", Title = "", File = ""; public string[] Cats = Array.Empty<string>(); public int Loops = 1; public float Vol = 1f;
    public AudioStream? Stream; public float Len; public bool Tried;
}
public sealed class MusicCat { public string Id = "", Name = "", Hint = ""; public bool Loop, Urgent; }

/// <summary>
/// Focus F-39: the music picker. Songs and categories come from data/music.json. Where the hero is decides the category
/// (boss lair > dungeon > home > town > night / Shadow > exploring); the first one that is switched on and has a song switched on
/// plays (Settings > Audio > Music, saved as Settings.MusicOff), otherwise silence. A change of place crossfades to a song of the new
/// category once it has held for "settle" seconds; a song never follows itself when its category has another. Between songs there is
/// a quiet gap. Two players on the "Music" bus do the crossfade; Sfx owns them (Sfx._Ready calls Build, Sfx._Process calls Tick).
/// Dev command: "/music" (status), "music list|skip|sim|check", "music ctx &lt;category|auto&gt;", "music on|off &lt;song id | cat:id&gt;".
/// </summary>
public static class Music
{
    public static readonly List<MusicTrack> Tracks = new();
    public static readonly List<MusicCat> Cats = new();
    static bool _loaded; static string _dir = "res://assets/audio/music/";
    static float _gap0 = 20, _gap1 = 70, _first0 = 4, _first1 = 9, _fade = 3.5f, _settle = 5, _minPlay = 14, _homeR = 9, _nightAt = .55f;
    /// <summary>Loudness of the Music bus at 100% (data/music.json "volume").</summary>
    public static float BusGain = .55f;

    sealed class Slot { public AudioStreamPlayer P = null!; public float G, Target, Vol = 1f, Secs = 3f; }
    static Slot? _a, _b; static Node? _host;
    static MusicTrack? _cur; static string _curCat = "", _lastId = "", _wantCat = "", _lastCat = "";
    static float _played, _gap, _wantT, _ctxT; static bool _ending;
    static List<string> _ctx = new(); static string _want = "";
    static Node3D? _home;
    /// <summary>Dev: a category the picker is held to ("" = by place).</summary>
    public static string Force = "";
    /// <summary>Prints every pick ("[MUSIC] town: Hearthside").</summary>
    public static bool Log = true;

    public static void Load()
    {
        if (_loaded) return; _loaded = true;
        Commands.Register("music", "F-39: music: what plays and why; music list; music skip; music sim (what each place would pick); music check (loads every song); music ctx <category|auto>; music on|off <song id | cat:id>", Cmd);
        if (!FileAccess.FileExists("res://data/music.json")) return;
        using var doc = JsonUtil.Load("res://data/music.json"); var r = doc.RootElement;
        _dir = JsonUtil.S(r, "dir", _dir);
        void Pair(string k, ref float a, ref float b) { if (r.TryGetProperty(k, out var e) && e.ValueKind == JsonValueKind.Array && e.GetArrayLength() >= 2) { a = (float)e[0].GetDouble(); b = (float)e[1].GetDouble(); } }
        Pair("gap", ref _gap0, ref _gap1); Pair("firstGap", ref _first0, ref _first1);
        _fade = Mathf.Max(.2f, JsonUtil.F(r, "fade", _fade)); _settle = JsonUtil.F(r, "settle", _settle); _minPlay = JsonUtil.F(r, "minPlay", _minPlay);
        _homeR = JsonUtil.F(r, "homeRadius", _homeR); _nightAt = JsonUtil.F(r, "nightAt", _nightAt); BusGain = JsonUtil.F(r, "volume", BusGain);
        if (r.TryGetProperty("categories", out var cs) && cs.ValueKind == JsonValueKind.Array)
            foreach (var c in cs.EnumerateArray())
                Cats.Add(new MusicCat { Id = JsonUtil.S(c, "id"), Name = JsonUtil.S(c, "name", JsonUtil.S(c, "id")), Hint = JsonUtil.S(c, "hint"), Loop = c.TryGetProperty("loop", out var lp) && lp.ValueKind == JsonValueKind.True, Urgent = c.TryGetProperty("urgent", out var ur) && ur.ValueKind == JsonValueKind.True });
        if (r.TryGetProperty("tracks", out var ts) && ts.ValueKind == JsonValueKind.Array)
            foreach (var t in ts.EnumerateArray())
            {
                var tr = new MusicTrack { Id = JsonUtil.S(t, "id"), Title = JsonUtil.S(t, "title", JsonUtil.S(t, "id")), File = JsonUtil.S(t, "file"), Loops = Math.Max(1, (int)JsonUtil.F(t, "loops", 1)), Vol = JsonUtil.F(t, "vol", 1f) };
                if (t.TryGetProperty("category", out var c))
                    tr.Cats = c.ValueKind == JsonValueKind.Array ? c.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x != "").ToArray() : c.ValueKind == JsonValueKind.String ? new[] { c.GetString() ?? "" } : tr.Cats;
                if (tr.Id != "" && tr.File != "") Tracks.Add(tr);
            }
    }
    public static MusicCat? Cat(string id) => Cats.Find(c => c.Id == id);
    public static string CatName(string id) => Cat(id)?.Name ?? id;

    // ------------------------------------------------------------------ files
    /// <summary>
    /// A song's stream: Godot's imported resource when there is one, else (a .wav the editor has not imported yet, e.g. fresh from
    /// tools/music_gen.ps1) the file is read directly. An exported game only contains imported files.
    /// </summary>
    static AudioStream? LoadStream(string path)
    {
        if (ResourceLoader.Exists(path)) { try { return GD.Load<AudioStream>(path); } catch (Exception) { return null; } }
        if (path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) && FileAccess.FileExists(path)) return ReadWav(path);
        return null;
    }
    /// <summary>Plain PCM .wav (8 or 16 bit, mono or stereo) into an AudioStreamWav, without the editor's import step.</summary>
    public static AudioStreamWav? ReadWav(string path)
    {
        var b = FileAccess.GetFileAsBytes(path);
        if (b == null || b.Length < 44 || b[0] != 'R' || b[1] != 'I' || b[2] != 'F' || b[3] != 'F' || b[8] != 'W' || b[9] != 'A' || b[10] != 'V' || b[11] != 'E') return null;
        int pos = 12, fmt = 0, ch = 1, rate = 22050, bits = 16; byte[]? data = null;
        while (pos + 8 <= b.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(b, pos, 4); long size = BitConverter.ToUInt32(b, pos + 4); int body = pos + 8;
            if (size > b.Length - body) size = b.Length - body;
            if (id == "fmt " && size >= 16) { fmt = BitConverter.ToUInt16(b, body); ch = BitConverter.ToUInt16(b, body + 2); rate = BitConverter.ToInt32(b, body + 4); bits = BitConverter.ToUInt16(b, body + 14); }
            else if (id == "data") { data = new byte[size]; Array.Copy(b, body, data, 0, (int)size); }
            pos = body + (int)size + (int)(size & 1);
        }
        if (data == null || fmt != 1 || ch is < 1 or > 2 || bits is not (8 or 16)) return null;
        if (bits == 8) for (int i = 0; i < data.Length; i++) data[i] = (byte)(data[i] - 128);   // .wav 8 bit is unsigned, Godot's is signed
        return new AudioStreamWav { Format = bits == 16 ? AudioStreamWav.FormatEnum.Format16Bits : AudioStreamWav.FormatEnum.Format8Bits, MixRate = rate, Stereo = ch == 2, Data = data };
    }
    static bool Ensure(MusicTrack t)
    {
        if (t.Tried) return t.Stream != null; t.Tried = true;
        t.Stream = LoadStream(_dir + t.File);
        if (t.Stream == null) { GD.Print($"[MUSIC] song '{t.Id}' not loaded ({_dir + t.File}): skipped"); return false; }
        t.Len = (float)t.Stream.GetLength(); return true;
    }
    static void SetLoop(AudioStream s, bool on)
    {
        switch (s)
        {
            case AudioStreamWav w:
                w.LoopMode = on ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled;
                if (on) { w.LoopBegin = 0; w.LoopEnd = Math.Max(1, (int)Math.Round(w.GetLength() * w.MixRate)); }
                break;
            case AudioStreamOggVorbis o: o.Loop = on; break;
            case AudioStreamMP3 m: m.Loop = on; break;
        }
    }

    // ------------------------------------------------------------------ on / off (Settings.MusicOff: "cat:<id>" or a song id)
    static List<string> Off => Game.Set.MusicOff ??= new();
    public static bool CatOn(string id) => !Off.Contains("cat:" + id);
    public static bool TrackOn(string id) => !Off.Contains(id);
    public static void SetCat(string id, bool on) => SetKey("cat:" + id, on);
    public static void SetTrack(string id, bool on) => SetKey(id, on);
    static void SetKey(string key, bool on) { Off.RemoveAll(x => x == key); if (!on) Off.Add(key); Game.SaveSettings(); _ctxT = 0; _wantT = 1e6f; }
    static List<MusicTrack> Usable(string cat) => CatOn(cat) ? Tracks.Where(t => t.Cats.Contains(cat) && TrackOn(t.Id) && Ensure(t)).ToList() : new List<MusicTrack>();

    // ------------------------------------------------------------------ where are we
    static bool AtHome(Vector3 p)
    {
        if (Dungeon.Active) return false;
        if (_home == null || !GodotObject.IsInstanceValid(_home))
        {
            _home = null; if (_host == null || !_host.IsInsideTree()) return false;
            foreach (var n in _host.GetTree().GetNodesInGroup("interact")) if (n is StashChest sc) { _home = sc; break; }
            if (_home == null) return false;
        }
        if (!_home.IsInsideTree() || !_home.IsVisibleInTree()) return false;
        var h = _home.GlobalPosition; return new Vector2(h.X - p.X, h.Z - p.Z).Length() < _homeR && Mathf.Abs(h.Y - p.Y) < 8f;
    }
    /// <summary>The categories that fit this moment, best first (home > town > night > exploring; dungeons and the lair stand alone).</summary>
    public static List<string> Context()
    {
        var l = new List<string>();
        if (Force != "") { l.Add(Force); return l; }
        if (Dungeon.Active) { if (Dungeon.IsLair(Dungeon.Key)) l.Add("boss"); l.Add("dungeon"); l.Add("night"); return l; }
        bool dark = World.RealmT > .5f || World.Night > _nightAt;
        var pl = Player.Instance;
        if (pl != null && GodotObject.IsInstanceValid(pl) && Ui.InGame)
        {
            var p = pl.GlobalPosition;
            if (AtHome(p)) l.Add("home");
            if (World.RealmT <= .5f && TownZones.At(p.X, p.Z) != null) l.Add("town");   // the Shadow realm's towns are not cosy
        }
        if (dark) l.Add("night");
        l.Add("explore");
        return l;
    }
    static string Want(List<string> ctx) { foreach (var c in ctx) if (Usable(c).Count > 0) return c; return ""; }
    static MusicTrack? Pick(string cat)
    {
        var l = Usable(cat); if (l.Count == 0) return null;
        if (l.Count > 1) l.RemoveAll(t => t.Id == _lastId);   // never the same song twice in a row when the category has another
        return l[(int)(GD.Randi() % (uint)l.Count)];
    }

    // ------------------------------------------------------------------ playing
    public static void Build(Node host)
    {
        Load(); _host = host; _home = null;
        Slot Make() { var p = new AudioStreamPlayer { Bus = Sfx.BusMusic, VolumeDb = -80f }; host.AddChild(p); return new Slot { P = p }; }
        _a = Make(); _b = Make(); _cur = null; _curCat = ""; _wantCat = ""; _lastCat = ""; _ending = false; _played = 0; _wantT = 0; _ctxT = 0; _ctx = new();
        _gap = (float)GD.RandRange(_first0, _first1); _want = "";
        foreach (var t in Tracks) Ensure(t);   // loaded up front (as before F-39), so a change of place never hitches on a file read
    }
    /// <summary>The host node left the tree (scene reload): forget its players.</summary>
    public static void Forget(Node host) { if (_host == host) { _a = _b = null; _host = null; _cur = null; _home = null; } }

    static void Start(MusicTrack t, string cat, bool cross)
    {
        if (_a == null || _b == null || t.Stream == null) return;
        (_a, _b) = (_b, _a);   // the old song fades out on _b
        _b.Target = 0; _b.Secs = _fade;
        bool loop = (Cat(cat)?.Loop ?? false) || t.Loops > 1; SetLoop(t.Stream, loop);
        _a.P.Stop(); _a.P.Stream = t.Stream; _a.Vol = t.Vol; _a.G = 0; _a.Target = 1; _a.Secs = cross ? _fade : 1.5f; _a.P.VolumeDb = -80f; _a.P.Play();
        _cur = t; _curCat = cat; _lastId = t.Id; _lastCat = cat; _played = 0; _ending = false;
        if (Log) GD.Print($"[MUSIC] {cat}: {t.Title} ({t.Id}, {t.Len:0} s x{(Cat(cat)?.Loop ?? false ? "loop" : t.Loops.ToString())}){(cross ? " crossfade" : "")}");
    }
    static void FadeOut(float secs) { if (_a == null) return; _a.Target = 0; _a.Secs = Mathf.Max(.2f, secs); _ending = true; }
    static void Step(Slot s, float dt)
    {
        s.G = Mathf.MoveToward(s.G, s.Target, dt / Mathf.Max(.05f, s.Secs));
        if (s.G <= 0 && s.Target <= 0) { if (s.P.Playing) s.P.Stop(); s.P.VolumeDb = -80f; return; }
        s.P.VolumeDb = Mathf.LinearToDb(Mathf.Max(s.G * s.Vol, .0001f));
    }

    public static void Tick(float dt)
    {
        if (_a == null || _b == null) return;
        dt = Mathf.Min(dt, .25f);
        Step(_a, dt); Step(_b, dt);
        _ctxT -= dt; if (_ctxT <= 0) { _ctxT = .5f; _ctx = Context(); _want = Want(_ctx); }   // twice a second is plenty
        string want = _want;
        if (want != _wantCat) { _wantCat = want; _wantT = 0; } else _wantT += dt;
        bool urgent = (Cat(want)?.Urgent ?? false) || (Cat(_curCat)?.Urgent ?? false) || Force != "";

        if (_cur != null)
        {
            _played += dt;
            bool loopCat = Cat(_curCat)?.Loop ?? false;
            // the song ended by itself, or its fade-out is done
            if (!_a.P.Playing || _ending && _a.G <= 0)
            {
                if (_a.P.Playing) _a.P.Stop();
                _cur = null; _ending = false; _gap = loopCat ? 2f : (float)GD.RandRange(_gap0, _gap1); return;
            }
            if (_ending) return;
            bool allowed = TrackOn(_cur.Id) && CatOn(_curCat);
            bool fits = want != "" && want == _curCat || want != "" && _cur.Cats.Contains(want) && allowed;
            if (!allowed || !fits && _wantT >= (urgent ? 1f : _settle) && (urgent || _played >= _minPlay))
            {
                var next = want != "" ? Pick(want) : null;
                if (next != null) Start(next, want, true); else { FadeOut(_fade); _gap = 3f; }
                return;
            }
            // a seamless song has played its loops: fade it out over the last seconds
            if (!loopCat && _cur.Loops > 1 && _cur.Len > 0 && _played >= _cur.Len * _cur.Loops - _fade) FadeOut(_fade);
            return;
        }
        // quiet: wait out the gap; a new kind of place cuts it short
        _gap -= dt;
        if (want != "" && want != _lastCat && _wantT >= (urgent ? 1f : _settle)) _gap = Mathf.Min(_gap, 0f);
        if (_gap > 0 || want == "") return;
        var t = Pick(want); if (t == null) { _gap = 5f; return; }
        Start(t, want, false);
    }

    /// <summary>Skips the wait so a song starts soon (entering a dungeon, a mood change).</summary>
    public static void Soon() { if (_cur == null) _gap = Mathf.Min(_gap, 3f); }
    /// <summary>The next song of this place, now (Settings > Audio > Music > Skip).</summary>
    public static void Skip()
    {
        _ctx = Context(); string want = _want = Want(_ctx); _wantCat = want; _wantT = 0;
        var t = want != "" ? Pick(want) : null;
        if (t != null) Start(t, want, _cur != null); else if (_cur != null) FadeOut(1f);
    }
    public static string NowTitle => _cur?.Title ?? "";
    public static string NowCat => _cur != null ? _curCat : "";
    /// <summary>The "now playing" line of the settings window.</summary>
    public static string NowPlaying()
    {
        if (_cur != null) return $"{_cur.Title}   ({CatName(_curCat)})";
        string want = Want(_ctx.Count > 0 ? _ctx : Context());
        if (Game.Set.Music <= 0 || Game.Set.Master <= 0) return "The music volume is at zero";
        return want == "" ? "Silence: nothing is switched on for this place" : $"A quiet moment (next: {CatName(want)})";
    }

    // ------------------------------------------------------------------ dev command
    static string Cmd(string[] a)
    {
        Load(); string sub = a.Length > 0 ? a[0].ToLowerInvariant() : "";
        switch (sub)
        {
            case "list":
                return string.Join("\n", Tracks.Select(t => $"{t.Id,-26} [{string.Join(",", t.Cats)}] \"{t.Title}\" {t.File} x{t.Loops} {(TrackOn(t.Id) ? "on" : "OFF")}{(t.Tried ? t.Stream != null ? $" {t.Len:0.0} s" : " NOT LOADED" : "")}"));
            case "check":
            {
                int ok = 0; var sb = new System.Text.StringBuilder();
                foreach (var t in Tracks)
                {
                    bool had = ResourceLoader.Exists(_dir + t.File); bool good = Ensure(t); if (good) ok++;
                    sb.AppendLine($"{t.Id,-26} {(good ? $"ok {t.Len,6:0.0} s {t.Stream!.GetClass()}" : "FAILED")} {(had ? "imported" : "read from the file (not imported yet)")}");
                }
                foreach (var c in Cats) sb.AppendLine($"category {c.Id,-8} {Tracks.Count(t => t.Cats.Contains(c.Id))} songs, {Usable(c.Id).Count} usable{(CatOn(c.Id) ? "" : " (category OFF)")}");
                return sb + $"{ok}/{Tracks.Count} songs load";
            }
            case "skip": Skip(); return "now: " + NowPlaying();
            case "ctx":
                if (a.Length < 2) return "usage: music ctx <" + string.Join("|", Cats.Select(c => c.Id)) + "|auto>";
                Force = a[1].ToLowerInvariant() == "auto" ? "" : a[1].ToLowerInvariant();
                if (Force != "" && Cat(Force) == null) { string bad = Force; Force = ""; return $"no category '{bad}'"; }
                _ctxT = 0; Skip(); return $"context {(Force == "" ? "by place" : "held to " + Force)}; now: {NowPlaying()}";
            case "on": case "off":
            {
                if (a.Length < 2) return "usage: music on|off <song id | cat:id>";
                string key = a[1]; bool on = sub == "on";
                if (key.StartsWith("cat:")) { if (Cat(key[4..]) == null) return $"no category '{key[4..]}'"; SetCat(key[4..], on); }
                else { if (Tracks.Find(t => t.Id == key) == null) return $"no song '{key}'"; SetTrack(key, on); }
                return $"{key} {(on ? "on" : "off")}; off list: [{string.Join(", ", Off)}]";
            }
            case "sim":
            {   // what each kind of place would play with today's switches (three picks each: no song twice in a row)
                var sb = new System.Text.StringBuilder(); string keep = _lastId;
                var places = new (string name, string[] ctx)[] { ("home (in town, day)", new[] { "home", "town", "explore" }), ("town, day", new[] { "town", "explore" }), ("town, night", new[] { "town", "night", "explore" }),
                    ("wilds, night", new[] { "night", "explore" }), ("wilds, day", new[] { "explore" }), ("dungeon", new[] { "dungeon", "night" }), ("boss lair", new[] { "boss", "dungeon", "night" }) };
                foreach (var (name, ctx) in places)
                {
                    string want = Want(ctx.ToList()); var picks = new List<string>();
                    for (int i = 0; i < 3 && want != ""; i++) { var t = Pick(want); if (t == null) break; _lastId = t.Id; picks.Add(t.Title); }
                    sb.AppendLine($"{name,-20} -> {(want == "" ? "silence" : want + ": " + string.Join(" > ", picks))}");
                }
                _lastId = keep; return sb.ToString().TrimEnd();
            }
            default:
                return $"now: {NowPlaying()}\ncontext: [{string.Join(" > ", Context())}] wants '{Want(Context())}'{(Force != "" ? " (held)" : "")}; played {_played:0} s{(_cur != null ? $" of {(Cat(_curCat)?.Loop ?? false ? "a loop" : (_cur.Len * _cur.Loops).ToString("0") + " s")}" : $", next song in {Mathf.Max(0, _gap):0} s")}\n"
                    + $"volumes: master {Game.Set.Master:0.00} music {Game.Set.Music:0.00} effects {Game.Set.Sfx:0.00} ambient {Game.Set.Ambient:0.00} voice {Game.Set.Voice:0.00}; buses: {Sfx.BusInfo()}\n"
                    + $"off: [{string.Join(", ", Off)}]\n"
                    + string.Join("\n", Cats.Select(c => $"  {c.Id,-8} {(CatOn(c.Id) ? "on " : "OFF")} {Tracks.Count(t => t.Cats.Contains(c.Id) && TrackOn(t.Id))}/{Tracks.Count(t => t.Cats.Contains(c.Id))} songs on"));
        }
    }
}
