using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>One instrument of data/instruments.json. Idx is the file order (sent over the network).</summary>
public sealed class InstrumentDef
{
    public int Idx; public string Id = "", Name = "", Sub = "", Kind = "pluck"; public int Price, Root = 60; public int[] Scale = { 0, 2, 4, 5, 7, 9, 11, 12 };
    public Dictionary<string, int> Cost = new(); public Color Col = new(.55f, .36f, .18f);
    public float Bright = .7f, Decay = 1.5f, Detune = .003f, Vol = .8f, Breath = .06f, Hold = .7f, Drop = .6f, Noise = .35f;
    static readonly string[] Names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
    public int Midi(int note, bool high) => Root + Scale[Math.Clamp(note, 0, Scale.Length - 1)] + (high ? 12 : 0);
    public string NoteName(int note, bool high) => Names[((Midi(note, high) % 12) + 12) % 12];
}

/// <summary>Focus F-40: the instrument table, what the hero owns, and the runtime synthesiser (one cached one-shot per instrument, key and octave).</summary>
public static class InstrumentDb
{
    public static readonly List<InstrumentDef> All = new();
    public static string[] NoteKeys = { "1", "2", "3", "4", "5", "6", "7", "8" }; public static string NextKey = "0";
    public static float HearRange = 45f, NetPerSecond = 14f;
    // reactions
    public static float Range = 10f, Window = 6f, Cooldown = 22f, RoleChance = .6f, RepEvery = 240f, CoinChance = .25f, CoinEvery = 45f;
    public static int After = 4, RepNotes = 24, Rep = 1, RepMax = 40, Coins0 = 1, Coins1 = 4;
    public static readonly Dictionary<string, string[]> Lines = new(), RoleLines = new();
    public static readonly HashSet<string> Grumble = new();
    static bool _loaded;

    public static void Load()
    {
        if (_loaded) return; _loaded = true;
        if (!FileAccess.FileExists("res://data/instruments.json")) return;
        using var doc = JsonUtil.Load("res://data/instruments.json"); var r = doc.RootElement;
        NextKey = JsonUtil.S(r, "nextKey", NextKey); HearRange = JsonUtil.F(r, "hearRange", HearRange);
        static string[] Arr(JsonElement e) => e.ValueKind == JsonValueKind.Array ? e.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x != "").ToArray() : Array.Empty<string>();
        if (r.TryGetProperty("noteKeys", out var nk) && Arr(nk) is { Length: > 0 } keys) NoteKeys = keys;
        if (r.TryGetProperty("net", out var net)) NetPerSecond = JsonUtil.F(net, "perSecond", NetPerSecond);
        if (r.TryGetProperty("instruments", out var ins) && ins.ValueKind == JsonValueKind.Array)
            foreach (var e in ins.EnumerateArray())
            {
                var d = new InstrumentDef { Idx = All.Count, Id = JsonUtil.S(e, "id"), Name = JsonUtil.S(e, "name", JsonUtil.S(e, "id")), Sub = JsonUtil.S(e, "sub"), Price = (int)JsonUtil.F(e, "price", 100), Root = (int)JsonUtil.F(e, "root", 60) };
                if (e.TryGetProperty("scale", out var sc) && sc.ValueKind == JsonValueKind.Array && sc.GetArrayLength() > 0) d.Scale = sc.EnumerateArray().Select(x => x.GetInt32()).ToArray();
                if (e.TryGetProperty("cost", out var c) && c.ValueKind == JsonValueKind.Object) foreach (var k in c.EnumerateObject()) d.Cost[k.Name] = k.Value.GetInt32();
                if (e.TryGetProperty("timbre", out var t))
                {
                    d.Kind = JsonUtil.S(t, "kind", d.Kind); d.Bright = JsonUtil.F(t, "bright", d.Bright); d.Decay = Mathf.Clamp(JsonUtil.F(t, "decay", d.Decay), .1f, 4f); d.Detune = JsonUtil.F(t, "detune", d.Detune);
                    d.Vol = JsonUtil.F(t, "vol", d.Vol); d.Breath = JsonUtil.F(t, "breath", d.Breath); d.Hold = JsonUtil.F(t, "hold", d.Hold); d.Drop = JsonUtil.F(t, "drop", d.Drop); d.Noise = JsonUtil.F(t, "noise", d.Noise);
                }
                string col = JsonUtil.S(e, "color"); if (col != "") { try { d.Col = Color.FromHtml(col); } catch (Exception) { } }
                if (d.Id != "") All.Add(d);
            }
        if (r.TryGetProperty("reactions", out var re))
        {
            Range = JsonUtil.F(re, "range", Range); After = (int)JsonUtil.F(re, "after", After); Window = JsonUtil.F(re, "window", Window); Cooldown = JsonUtil.F(re, "cooldown", Cooldown);
            RoleChance = JsonUtil.F(re, "roleChance", RoleChance); RepNotes = (int)JsonUtil.F(re, "repNotes", RepNotes); Rep = (int)JsonUtil.F(re, "rep", Rep); RepEvery = JsonUtil.F(re, "repEvery", RepEvery); RepMax = (int)JsonUtil.F(re, "repMax", RepMax);
            CoinChance = JsonUtil.F(re, "coinChance", CoinChance); CoinEvery = JsonUtil.F(re, "coinEvery", CoinEvery);
            if (re.TryGetProperty("coins", out var cn) && cn.ValueKind == JsonValueKind.Array && cn.GetArrayLength() >= 2) { Coins0 = cn[0].GetInt32(); Coins1 = cn[1].GetInt32(); }
            if (re.TryGetProperty("grumble", out var gr)) foreach (var g in Arr(gr)) Grumble.Add(g);
            if (re.TryGetProperty("lines", out var ln) && ln.ValueKind == JsonValueKind.Object)
                foreach (var p in ln.EnumerateObject())
                {
                    if (p.Name == "roles") { if (p.Value.ValueKind == JsonValueKind.Object) foreach (var rp in p.Value.EnumerateObject()) RoleLines[rp.Name] = Arr(rp.Value); }
                    else Lines[p.Name] = Arr(p.Value);
                }
        }
    }
    public static InstrumentDef? Get(string id) { Load(); return All.Find(d => d.Id == id); }
    public static InstrumentDef? At(int idx) { Load(); return idx >= 0 && idx < All.Count ? All[idx] : null; }
    public static List<string> Owned(PlayerStats d) => d.Instruments ??= new();
    public static bool Has(string id) => Owned(Game.D).Contains(id);
    public static void Give(string id) { var o = Owned(Game.D); if (Get(id) != null && !o.Contains(id)) o.Add(id); if (Game.D.Instrument == "" || Get(Game.D.Instrument) == null) Game.D.Instrument = id; }

    // ------------------------------------------------------------------ the synthesiser
    const int Rate = 22050;
    static readonly Dictionary<int, AudioStreamWav> _tones = new();
    /// <summary>Peak and RMS (0..1) and seconds of every tone built, for the dev check ("play tones").</summary>
    public static readonly Dictionary<int, (float peak, float rms, float secs)> ToneStats = new();
    public static (float peak, float rms, float secs) StatsOf(InstrumentDef d, int note, bool high) { Tone(d, note, high); return ToneStats.GetValueOrDefault(d.Idx * 100 + Math.Clamp(note, 0, d.Scale.Length - 1) * 2 + (high ? 1 : 0)); }
    public static double Hz(int midi) => 440.0 * Math.Pow(2, (midi - 69) / 12.0);

    /// <summary>
    /// The one-shot for a key of an instrument: a plucked string (harmonics that die away, the high ones first, a second string a hair
    /// sharp), a blown pipe (soft attack, late vibrato, breath noise; long, so a held key keeps sounding) or a drum skin (a pitch that
    /// falls at the hit, a second mode, a burst of stick noise). Built once and kept.
    /// </summary>
    public static AudioStreamWav Tone(InstrumentDef d, int note, bool high)
    {
        note = Math.Clamp(note, 0, d.Scale.Length - 1); int key = d.Idx * 100 + note * 2 + (high ? 1 : 0);
        if (_tones.TryGetValue(key, out var have)) return have;
        double f = Hz(d.Midi(note, high)); var rng = new Random(key * 7919 + 13);
        float secs = d.Kind switch { "blow" => 2.6f, "drum" => Mathf.Clamp(d.Decay * 1.8f, .25f, 2f), _ => Mathf.Clamp(d.Decay * 1.6f, .5f, 4f) };
        int n = (int)(secs * Rate); var s = new float[n];
        switch (d.Kind)
        {
            case "blow":
            {
                double ph = 0, lp = 0, vph = rng.NextDouble() * 6.28;
                for (int i = 0; i < n; i++)
                {
                    double t = i / (double)Rate, env = Math.Min(1.0, t / .055); if (t > 1.5) env *= Math.Max(0.0, 1 - (t - 1.5) / (secs - 1.5));
                    double vib = 1 + .005 * Math.Clamp((t - .25) / .5, 0, 1) * Math.Sin(vph + Math.Tau * 5.2 * t);
                    ph += Math.Tau * f * vib / Rate;
                    lp += (rng.NextDouble() * 2 - 1 - lp) * .35;
                    double x = Math.Sin(ph) + .28 * Math.Sin(2 * ph) + .1 * Math.Sin(3 * ph) + .035 * Math.Sin(4 * ph);
                    s[i] = (float)((x + lp * (d.Breath * 1.2 + .25 * Math.Exp(-t * 30))) * env);
                }
                break;
            }
            case "drum":
            {
                double ph = 0, ph2 = 0, lp = 0, k = 4.2 / d.Decay;
                for (int i = 0; i < n; i++)
                {
                    double t = i / (double)Rate, fall = 1 + d.Drop * Math.Exp(-t * 38);
                    ph += Math.Tau * f * fall / Rate; ph2 += Math.Tau * f * 1.59 * fall / Rate;
                    lp += (rng.NextDouble() * 2 - 1 - lp) * .5;
                    double env = Math.Min(1.0, i / (Rate * .0015)) * Math.Exp(-t * k);
                    s[i] = (float)((Math.Sin(ph) + .35 * Math.Sin(ph2) * Math.Exp(-t * k * 1.5)) * env + lp * d.Noise * Math.Exp(-t * 55));
                }
                break;
            }
            default:
            {
                double k = 3.2 / d.Decay;
                for (int h = 1; h <= 10; h++)
                {
                    double fh = f * h; if (fh > 9000) break;
                    double amp = Math.Pow(d.Bright, h - 1) / Math.Pow(h, 1.05), dk = k * (1 + .4 * (h - 1)), w1 = Math.Tau * fh / Rate, w2 = w1 * (1 + d.Detune);
                    for (int i = 0; i < n; i++)
                    {
                        double t = i / (double)Rate, env = Math.Min(1.0, i / (Rate * .003)) * Math.Exp(-t * dk);
                        if (env < .0003 && i > 200) break;
                        s[i] += (float)((Math.Sin(w1 * i) * .7 + Math.Sin(w2 * i) * .3) * env * amp);
                    }
                }
                break;
            }
        }
        // even loudness across keys: peak to 0.8, and a short fade at the very end so nothing clicks
        float peak = 0; foreach (var v in s) peak = Mathf.Max(peak, Mathf.Abs(v));
        float g = peak > 0 ? .8f / peak : 0; int tail = Math.Min(n, (int)(.03f * Rate)); double sum = 0;
        var b = new byte[n * 2];
        for (int i = 0; i < n; i++)
        {
            float v = s[i] * g; if (i >= n - tail) v *= (n - i) / (float)tail; sum += v * v;
            short sv = (short)Mathf.RoundToInt(Mathf.Clamp(v, -1f, 1f) * 32000); b[i * 2] = (byte)(sv & 255); b[i * 2 + 1] = (byte)((sv >> 8) & 255);
        }
        ToneStats[key] = (peak > 0 ? .8f : 0f, (float)Math.Sqrt(sum / Math.Max(1, n)), secs);
        var wav = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = false, Data = b };
        _tones[key] = wav; return wav;
    }
}

/// <summary>
/// Focus F-40: playable instruments. Owned ones are on the hero (PlayerStats.Instruments; bought in shops or crafted). The instrument
/// key (Inputs.Instrument, default 9) takes the chosen one out and puts it away, 0 switches to the next one owned. While it is out the
/// hero holds its block-style model, the note keys (1-8) play the steps of its scale and Shift plays them an octave up; the skill keys,
/// attacks and the fishing rod wait (Player.cs / Fishing.cs ask Instruments.Busy). A strip above the hotbar shows key > note and lights
/// what is played. Every note is a positional one-shot at the hero on the Effects bus, is sent to the other players (NetMusic.cs:
/// instrument, key, octave) and counts toward the reactions of NPCs within ~10 m: they turn, speak a line by standing and role, cheer,
/// toss a coin now and then and, after a longer piece, like the hero a little more (data/instruments.json "reactions").
/// Dev command: "/play" (see Cmd).
/// </summary>
public partial class Instruments : Node3D
{
    public static Instruments? I;
    public bool Holding { get; private set; }
    public InstrumentDef? Cur { get; private set; }
    /// <summary>An instrument is in hand: the hotbar keys play notes, attacks and the rod wait.</summary>
    public static bool Busy => I != null && I.Holding;

    Key[] _keys = Array.Empty<Key>(); Key _next = Key.Key0;
    bool[] _down = Array.Empty<bool>(); AudioStreamPlayer3D?[] _held = Array.Empty<AudioStreamPlayer3D?>();
    public float[] Lit = Array.Empty<float>(); public bool[] LitHigh = Array.Empty<bool>();
    InstrumentStrip _ui = null!; int _warm = -1; bool _nextDown;
    readonly List<(float at, Action run)> _queue = new();
    float _clock;
    public int NotesPlayed, NotesHeard, Reactions; public string LastReaction = "";

    static Key KeyOf(string k) => k.Length == 1 && char.IsDigit(k[0]) ? Key.Key0 + (k[0] - '0') : OS.FindKeycodeFromString(k);

    public override void _Ready()
    {
        I = this; InstrumentDb.Load(); RegisterCmd();
        _keys = InstrumentDb.NoteKeys.Select(KeyOf).ToArray(); _next = KeyOf(InstrumentDb.NextKey);
        _down = new bool[_keys.Length]; _held = new AudioStreamPlayer3D?[_keys.Length]; Lit = new float[_keys.Length]; LitHigh = new bool[_keys.Length];
        foreach (var d in InstrumentDb.All) { var def = d; Avatar.CodeTools["code:inst_" + d.Id] = () => Model(def); }
        var layer = new CanvasLayer { Layer = 6, Name = "InstrumentUi" }; AddChild(layer);
        _ui = new InstrumentStrip { Host = this }; layer.AddChild(_ui); _ui.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); _ui.MouseFilter = Control.MouseFilterEnum.Ignore;
    }
    public override void _ExitTree() { if (I == this) I = null; }

    // ------------------------------------------------------------------ the models (block style; pointing -Z, grip at the origin)
    static Node3D Model(InstrumentDef d)
    {
        var root = new Node3D { Name = "Instrument_" + d.Id };
        StandardMaterial3D M(Color c, float rough = .7f) => new() { AlbedoColor = c, Roughness = rough };
        var wood = M(d.Col); var dark = M(d.Col.Darkened(.45f)); var pale = M(new Color(.86f, .8f, .68f), .9f); var str = M(new Color(.9f, .88f, .8f), .4f);
        void Box(Vector3 size, Vector3 pos, Material m, Vector3? rot = null) => root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, Position = pos, Rotation = rot ?? Vector3.Zero, MaterialOverride = m, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        switch (d.Id)
        {
            case "flute":
                Box(new Vector3(.03f, .03f, .56f), new Vector3(0, 0, -.14f), wood);
                for (int i = 0; i < 5; i++) Box(new Vector3(.014f, .006f, .014f), new Vector3(0, .017f, -.02f - i * .075f), dark);
                Box(new Vector3(.036f, .036f, .03f), new Vector3(0, 0, .13f), dark);
                break;
            case "drum":
                Box(new Vector3(.3f, .2f, .3f), new Vector3(0, -.06f, -.22f), wood);
                Box(new Vector3(.31f, .025f, .31f), new Vector3(0, .05f, -.22f), pale);
                Box(new Vector3(.32f, .03f, .32f), new Vector3(0, -.14f, -.22f), dark);
                break;
            case "harp":
                Box(new Vector3(.035f, .035f, .44f), new Vector3(0, 0, -.2f), wood);                                   // the base in the hand
                Box(new Vector3(.035f, .56f, .035f), new Vector3(0, .27f, -.42f), wood);                               // the pillar
                Box(new Vector3(.035f, .035f, .6f), new Vector3(0, .3f, -.2f), dark, new Vector3(-.95f, 0, 0));        // the neck, sloping back to the hand
                for (int i = 0; i < 6; i++) { float z = -.36f + i * .06f, top = .5f - i * .075f; Box(new Vector3(.005f, top, .005f), new Vector3(0, top / 2, z), str); }
                break;
            default:   // lute (and anything new in the data): a neck with a head, a deep body with a sound hole and strings
                Box(new Vector3(.035f, .03f, .46f), new Vector3(0, 0, -.1f), dark);
                Box(new Vector3(.05f, .03f, .09f), new Vector3(0, -.015f, .17f), dark, new Vector3(.35f, 0, 0));
                Box(new Vector3(.26f, .1f, .34f), new Vector3(0, -.035f, -.5f), wood);
                Box(new Vector3(.2f, .1f, .08f), new Vector3(0, -.035f, -.7f), wood);
                Box(new Vector3(.07f, .006f, .07f), new Vector3(0, .017f, -.46f), M(new Color(.08f, .06f, .05f)));
                for (int i = 0; i < 3; i++) Box(new Vector3(.004f, .004f, .78f), new Vector3((i - 1) * .012f, .02f, -.27f), str);
                break;
        }
        return root;
    }

    // ------------------------------------------------------------------ taking it out, putting it away
    void Say(string s) => Ui.Note(s, new Color(.95f, .85f, .6f));
    InstrumentDef? Chosen()
    {
        var own = InstrumentDb.Owned(Game.D).Where(id => InstrumentDb.Get(id) != null).ToList(); if (own.Count == 0) return null;
        return InstrumentDb.Get(own.Contains(Game.D.Instrument) ? Game.D.Instrument : own[0]);
    }
    public void Toggle()
    {
        if (Holding) { PutAway(); return; }
        var pl = Player.Instance; if (pl == null) return;
        var d = Chosen();
        if (d == null) { Say("You own no instrument. Buy one from a merchant or craft one (Crafting > Blocks > Instruments)."); Sfx.UiError(); return; }
        if (Player.BuildMode || pl.Mounted || pl.Dead || pl.Swimming) { Say("You cannot play right now."); Sfx.UiError(); return; }
        TakeOut(d);
    }
    void TakeOut(InstrumentDef d)
    {
        Fishing.I?.DevReset();   // the rod goes away first: one thing in the hand
        Cur = d; Holding = true; Game.D.Instrument = d.Id; _warm = 0; ReleaseAll();
        string keys = InstrumentDb.NoteKeys.Length > 1 ? InstrumentDb.NoteKeys[0] + "-" + InstrumentDb.NoteKeys[^1] : "the note keys";
        Say($"{d.Name} out: {keys} play its scale, Shift an octave up, {Inputs.KeyOf(Inputs.Instrument)} puts it away{(InstrumentDb.Owned(Game.D).Count > 1 ? $", {InstrumentDb.NextKey} switches instrument" : "")}");
    }
    public void PutAway()
    {
        if (!Holding) return; Holding = false; ReleaseAll(); _queue.Clear();
        if (Player.Instance?.Av is { } av && av.HeldOverride != null && av.HeldOverride.StartsWith("code:inst_")) av.HeldOverride = null;
    }
    public void Next()
    {
        var own = InstrumentDb.Owned(Game.D).Where(id => InstrumentDb.Get(id) != null).ToList(); if (own.Count < 2 || Cur == null) return;
        TakeOut(InstrumentDb.Get(own[(own.IndexOf(Cur.Id) + 1) % own.Count])!);
    }

    // ------------------------------------------------------------------ notes
    /// <summary>A positional one-shot of this key at the anchor (the hero, or another player's hero). hold: fade it out after so many seconds (blown notes from others).</summary>
    public static AudioStreamPlayer3D Emit(Node3D anchor, InstrumentDef d, int note, bool high, float hold = -1f)
    {
        var p = new AudioStreamPlayer3D
        {
            Stream = InstrumentDb.Tone(d, note, high), Bus = Sfx.BusEffects, VolumeDb = Mathf.LinearToDb(Mathf.Max(d.Vol, .001f)), UnitSize = 8f, MaxDistance = InstrumentDb.HearRange,
            AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance, Position = new Vector3(0, 1.3f, 0),
        };
        anchor.AddChild(p); p.Finished += p.QueueFree; p.Play();
        if (hold > 0) FadeAway(p, hold);
        return p;
    }
    static void FadeAway(AudioStreamPlayer3D p, float after)
    {
        if (!IsInstanceValid(p) || !p.IsInsideTree()) return;
        var tw = p.CreateTween(); if (after > 0) tw.TweenInterval(after);
        tw.TweenProperty(p, "volume_db", -60f, .18f); tw.TweenCallback(Callable.From(() => { if (IsInstanceValid(p)) p.QueueFree(); }));
    }
    /// <summary>The hero plays a key of the instrument in hand: sound, the strip lights, friends hear it, NPCs notice. held: a key is down (a blown note lasts until Release).</summary>
    public void PlayNote(int note, bool high, bool held = false)
    {
        var pl = Player.Instance; var d = Cur; if (pl == null || d == null || note < 0 || note >= Lit.Length) return;
        note = Math.Min(note, d.Scale.Length - 1);
        bool blow = d.Kind == "blow";
        var p = Emit(pl, d, note, high, blow && !held ? d.Hold : -1f);
        if (blow && held) { Release(note); _held[note] = p; }
        Lit[note] = .4f; LitHigh[note] = high; NotesPlayed++;
        Net.I?.Tune(d.Idx, note, high);
        _lastNote = _clock; if (_sessNotes == 0 || _clock - _sessAt > InstrumentDb.Window) _sessNotes = 0; _sessNotes++; _sessAt = _clock;
    }
    void Release(int note) { if (note < _held.Length && _held[note] is { } p) { _held[note] = null; FadeAway(p, 0f); } }
    void ReleaseAll() { for (int i = 0; i < _held.Length; i++) { Release(i); _down[i] = false; } }
    /// <summary>A note another player played (NetMusic.cs), at their hero.</summary>
    public void PlayRemote(Node3D anchor, int instr, int note, bool high)
    {
        if (InstrumentDb.At(instr) is not { } d || note < 0 || note >= d.Scale.Length) return;
        Emit(anchor, d, note, high, d.Kind == "blow" ? d.Hold : -1f); NotesHeard++;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta; _clock += dt; var pl = Player.Instance; if (pl == null) return;
        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;
        if (captured && Ui.InGame && Input.IsActionJustPressed(Inputs.Instrument)) Toggle();
        if (Holding && (Cur == null || Player.BuildMode || pl.Mounted || pl.Dead || pl.Swimming || !InstrumentDb.Has(Cur.Id))) PutAway();
        for (int i = 0; i < Lit.Length; i++) if (Lit[i] > 0) Lit[i] -= dt;
        for (int i = _queue.Count - 1; i >= 0; i--) if (_queue[i].at <= _clock) { var run = _queue[i].run; _queue.RemoveAt(i); run(); }
        FaceTick();
        _ui.QueueRedraw();
        if (!Holding || Cur == null) return;
        if (pl.Av is { } av) av.HeldOverride = "code:inst_" + Cur.Id;   // (Fishing.cs leaves the override alone while we are Busy)
        if (_warm >= 0)
        {   // build the instrument's tones one a frame, so the first notes do not hitch
            int per = Cur.Scale.Length * 2; if (_warm < per) { InstrumentDb.Tone(Cur, _warm / 2, _warm % 2 == 1); _warm++; } else _warm = -1;
        }
        bool nx = captured && Input.IsPhysicalKeyPressed(_next); if (nx && !_nextDown) Next(); _nextDown = nx;
        if (Cur == null) return;
        bool shift = Input.IsKeyPressed(Key.Shift);
        for (int i = 0; i < _keys.Length; i++)
        {
            bool down = captured && Input.IsPhysicalKeyPressed(_keys[i]);
            if (down && !_down[i]) PlayNote(i, shift, true);
            else if (!down && _down[i]) Release(i);
            _down[i] = down;
        }
        ReactTick(dt, pl);
    }

    // ------------------------------------------------------------------ NPC reactions
    float _lastNote = -99f, _sessAt = -99f, _reactT, _coinAt = -999f; int _sessNotes;
    readonly Dictionary<string, float> _npcNext = new(), _repAt = new(), _facing = new();
    static readonly Random _rng = new();
    static string PickLine(string[] l) => l.Length == 0 ? "" : l[_rng.Next(l.Length)];

    void ReactTick(float dt, Player pl)
    {
        _reactT -= dt; if (_reactT > 0) return; _reactT = .5f;
        if (Dungeon.Active || _clock - _lastNote > 2.5f || _sessNotes < InstrumentDb.After) return;
        var pp = pl.GlobalPosition; bool night = World.Night > .55f && World.RealmT < .5f;
        NpcLive? pick = null; float best = 1e9f;
        foreach (var L in NpcSchedule.Live)
        {
            if (L.Hidden || !L.N.Visible || L.N.Down) continue;
            float dist = L.N.GlobalPosition.DistanceTo(pp); if (dist > InstrumentDb.Range) continue;
            bool free = L.Partner == null && L.Busy <= NpcSocial.Clock;
            if (free && L.N.Goal == null) { L.N.FaceAt = pp; _facing[L.Rec.Id] = _clock + 3f; }   // listening: turned to the player
            if (!free || L.N.Speaking || _npcNext.GetValueOrDefault(L.Rec.Id) > _clock) continue;
            if (dist < best) { best = dist; pick = L; }
        }
        if (pick != null) React(pick, night);   // one new reaction per half second, the nearest first
    }
    /// <summary>NPCs we turned toward the music look away again a few seconds after the last note (unless a chat took them over).</summary>
    void FaceTick()
    {
        if (_facing.Count == 0) return;
        foreach (var id in _facing.Where(kv => kv.Value <= _clock).Select(kv => kv.Key).ToList())
        {
            _facing.Remove(id);
            if (NpcSchedule.LiveOf(id) is { } L && L.Partner == null && L.Busy <= NpcSocial.Clock) L.N.FaceAt = null;
        }
    }
    void React(NpcLive L, bool night)
    {
        var r = L.Rec; int rep = NpcMemory.Peek(r.Id)?.Rep ?? 0; string line; bool happy = true; string nk = r.Role + "_night";
        if (night && InstrumentDb.RoleLines.TryGetValue(nk, out var nl) && nl.Length > 0) { line = PickLine(nl); happy = !InstrumentDb.Grumble.Contains(nk); }
        else if (rep <= -15) { line = PickLine(InstrumentDb.Lines.GetValueOrDefault("disliked") ?? Array.Empty<string>()); happy = false; }
        else if (InstrumentDb.RoleLines.TryGetValue(r.Role, out var rl) && rl.Length > 0 && _rng.NextDouble() < InstrumentDb.RoleChance) line = PickLine(rl);
        else line = PickLine(InstrumentDb.Lines.GetValueOrDefault(rep >= 15 ? "liked" : "neutral") ?? Array.Empty<string>());
        _npcNext[r.Id] = _clock + InstrumentDb.Cooldown * (.8f + (float)_rng.NextDouble() * .6f);
        L.N.Say(line); L.N.Emote(happy ? "cheer" : "guard", 1.8f);   // "guard": folded arms
        Reactions++; LastReaction = $"{r.Name} ({r.Role}, standing {rep}{(night ? ", night" : "")}): \"{line}\"{(happy ? " cheers" : " grumbles")}";
        if (!happy) return;
        if (_clock - _coinAt > InstrumentDb.CoinEvery && _rng.NextDouble() < InstrumentDb.CoinChance)
        {
            int n = _rng.Next(InstrumentDb.Coins0, Math.Max(InstrumentDb.Coins0, InstrumentDb.Coins1) + 1); _coinAt = _clock;
            Game.D.Gold += n; Sfx.Play("coin"); FloatText.Spawn(this, L.N.GlobalPosition + Vector3.Up * 2.1f, $"+{n} gold", new Color(1f, .85f, .3f));
            Ui.Note($"{r.Name} tosses you {n} gold", new Color(1f, .85f, .3f)); LastReaction += $", tossed {n} gold"; Game.Notify();
        }
        // a longer piece near someone who enjoyed it: they like the hero a little more (with a long cooldown, and only up to "repMax")
        if (_sessNotes >= InstrumentDb.RepNotes && rep < InstrumentDb.RepMax && InstrumentDb.Rep != 0 && _clock - _repAt.GetValueOrDefault(r.Id, -1e9f) >= InstrumentDb.RepEvery)
        { _repAt[r.Id] = _clock; NpcMemory.Change(r.Id, InstrumentDb.Rep); LastReaction += $", standing +{InstrumentDb.Rep}"; }
    }

    // ------------------------------------------------------------------ dev command
    static bool _cmd;
    public static void RegisterCmd()
    {
        if (_cmd) return; _cmd = true;
        Commands.Register("play", "F-40: play: instruments owned and what happened; play give [id|all]; play <instrument> <notes> (keys 1-8, ^3 = octave up, - = rest: played by the hero as if typed); play remote <instrument> <notes> (fed through the network receive handler); play react (nearby NPCs react now); play tones (builds every tone: seconds, peak, RMS)", Cmd);
    }
    static List<(int note, bool high)> Parse(IEnumerable<string> words, InstrumentDef d)
    {
        var l = new List<(int, bool)>();
        foreach (var w0 in words)
        {
            string w = w0; bool high = false; if (w.StartsWith("^") || w.StartsWith("+")) { high = true; w = w[1..]; }
            if (w == "-") { l.Add((-1, false)); continue; }
            if (int.TryParse(w, out int k) && k >= 1 && k <= d.Scale.Length) l.Add((k - 1, high));
        }
        return l;
    }
    static string Cmd(string[] a)
    {
        InstrumentDb.Load(); var me = I; string sub = a.Length > 0 ? a[0].ToLowerInvariant() : "";
        if (sub == "tones")
        {
            var sb = new System.Text.StringBuilder();
            foreach (var d in InstrumentDb.All)
                for (int hi = 0; hi < 2; hi++)
                {
                    var parts = new List<string>();
                    for (int k = 0; k < d.Scale.Length; k++) { var (_, rms, secs) = InstrumentDb.StatsOf(d, k, hi == 1); parts.Add($"{d.NoteName(k, hi == 1)}{d.Midi(k, hi == 1) / 12 - 1} {InstrumentDb.Hz(d.Midi(k, hi == 1)):0}Hz {secs:0.0}s rms {(rms > 0 ? 20 * Math.Log10(rms) : -99):0}dB"); }
                    sb.AppendLine($"{d.Id} ({d.Kind}){(hi == 1 ? " +Shift" : "")}: {string.Join(", ", parts)}");
                }
            return sb.ToString().TrimEnd();
        }
        if (sub == "give")
        {
            string id = a.Length > 1 ? a[1].ToLowerInvariant() : "all";
            foreach (var d in InstrumentDb.All) if (id == "all" || d.Id == id) InstrumentDb.Give(d.Id);
            Game.Notify(); return "owned: " + string.Join(", ", InstrumentDb.Owned(Game.D));
        }
        if (me == null) return "no world yet (start a game first)";
        if (sub == "react")
        {
            if (Player.Instance == null) return "no hero"; int before = me.Reactions;
            me._sessNotes = Math.Max(me._sessNotes, a.Length > 1 && a[1] == "long" ? InstrumentDb.RepNotes : InstrumentDb.After); me._sessAt = me._lastNote = me._clock; me._reactT = 0; me._npcNext.Clear();
            if (a.Length > 1 && a[1] == "coin") me._coinAt = -999f;
            int near = NpcSchedule.Live.Count(L => !L.Hidden && L.N.GlobalPosition.DistanceTo(Player.Instance.GlobalPosition) <= InstrumentDb.Range);
            me.ReactTick(1f, Player.Instance);
            return $"{near} NPCs within {InstrumentDb.Range:0} m; {(me.Reactions > before ? "reaction: " + me.LastReaction : "nobody reacted (none free and near, or in a dungeon)")}";
        }
        bool remote = sub == "remote"; int at = remote ? 1 : 0;
        if (a.Length > at && InstrumentDb.Get(a[at].ToLowerInvariant()) is { } def)
        {
            var notes = Parse(a.Skip(at + 1), def); if (notes.Count == 0) notes = Parse(new[] { "1", "3", "5", "8", "^3", "^1", "5", "3" }, def);
            if (remote)
            {
                if (Net.I == null) return "no Net node";
                int n = 0; foreach (var (note, high) in notes) { int k = n++; if (note < 0) continue; int packed = Net.PackTune(def.Idx, note, high); me._queue.Add((me._clock + k * .28f, () => Net.I?.TuneIn(Net.MyId, packed, Net.MyZone))); }
                return $"remote: {notes.Count(x => x.note >= 0)} notes of {def.Id} go through Net.TuneIn as if sent by peer {Net.MyId} (heard so far: {me.NotesHeard})";
            }
            InstrumentDb.Give(def.Id);
            if (me.Cur != def || !me.Holding) me.TakeOut(def);
            int i = 0; foreach (var (note, high) in notes) { int k = i++; if (note < 0) continue; me._queue.Add((me._clock + .1f + k * .28f, () => { if (me.Holding) me.PlayNote(note, high); })); }
            return $"playing {notes.Count(x => x.note >= 0)} notes on the {def.Name}: {string.Join(" ", notes.Select(x => x.note < 0 ? "-" : def.NoteName(x.note, x.high) + (x.high ? "'" : "")))}";
        }
        return $"owned: [{string.Join(", ", InstrumentDb.Owned(Game.D))}] chosen '{Game.D.Instrument}' {(me.Holding ? "OUT: " + me.Cur?.Name : "put away")}; notes played {me.NotesPlayed}, heard from others {me.NotesHeard}, sent {Net.I?.TunesSent ?? 0}; "
            + $"this piece {me._sessNotes} notes; reactions {me.Reactions}{(me.LastReaction != "" ? "; last: " + me.LastReaction : "")}\ninstruments: {string.Join(", ", InstrumentDb.All.Select(d => $"{d.Id} ({d.Kind}, {d.Price}g, {string.Join("+", d.Cost.Select(kv => kv.Value + " " + kv.Key))})"))}";
    }
}

/// <summary>The strip above the hotbar while an instrument is out: one box per note key with the key and the note name; a played key lights up (brighter and marked for the upper octave).</summary>
public partial class InstrumentStrip : Control
{
    public Instruments Host = null!;
    public override void _Draw()
    {
        if (Host == null || !Host.Holding || Host.Cur is not { } d || !Ui.InGame) return;
        var font = ThemeDB.FallbackFont; var sz = Size; int n = Math.Min(InstrumentDb.NoteKeys.Length, d.Scale.Length); if (n == 0) return;
        const float W = 46, H = 50, Gap = 6; float total = n * W + (n - 1) * Gap; var o = new Vector2((sz.X - total) / 2, sz.Y - 236);
        void Text(string s, Vector2 mid, int size, Color c) { var w = font.GetStringSize(s, HorizontalAlignment.Left, -1, size).X; DrawString(font, mid + new Vector2(-w / 2 + 1, 1), s, HorizontalAlignment.Left, -1, size, new Color(0, 0, 0, .8f)); DrawString(font, mid - new Vector2(w / 2, 0), s, HorizontalAlignment.Left, -1, size, c); }
        bool shift = Input.IsKeyPressed(Key.Shift);
        DrawRect(new Rect2(o - new Vector2(12, 30), new Vector2(total + 24, H + 62)), new Color(.06f, .05f, .04f, .72f));
        DrawRect(new Rect2(o - new Vector2(12, 30), new Vector2(total + 24, H + 62)), new Color(.75f, .62f, .38f, .85f), false, 1.5f);
        Text($"{d.Name}{(shift ? "   (upper octave)" : "")}", o + new Vector2(total / 2, -10), 16, new Color(.95f, .86f, .62f));
        for (int i = 0; i < n; i++)
        {
            var r = new Rect2(o + new Vector2(i * (W + Gap), 0), new Vector2(W, H)); float lit = i < Host.Lit.Length ? Mathf.Clamp(Host.Lit[i] / .4f, 0, 1) : 0;
            bool hi = lit > 0 && i < Host.LitHigh.Length && Host.LitHigh[i];
            var baseC = new Color(.16f, .13f, .1f, .9f); var glow = hi ? new Color(.55f, .85f, 1f) : new Color(1f, .8f, .35f);
            DrawRect(r, baseC.Lerp(glow, lit * .85f));
            DrawRect(r, lit > 0 ? glow : new Color(.6f, .5f, .32f, .9f), false, lit > 0 ? 2.5f : 1f);
            Text(InstrumentDb.NoteKeys[i], r.Position + new Vector2(W / 2, 17), 14, new Color(.8f, .76f, .66f));
            Text(d.NoteName(i, shift || hi) + (shift || hi ? "'" : ""), r.Position + new Vector2(W / 2, 41), 18, lit > 0 ? new Color(.1f, .08f, .05f) : Colors.White);
        }
        string more = InstrumentDb.Owned(Game.D).Count > 1 ? $"   {InstrumentDb.NextKey}: next instrument" : "";
        Text($"Shift: octave up   {Inputs.KeyOf(Inputs.Instrument)}: put away{more}", o + new Vector2(total / 2, H + 22), 13, new Color(.82f, .8f, .74f, .9f));
    }
}

public partial class Ui
{
    /// <summary>F-40: instruments not owned yet as shop rows (called from OpenShop's buy list; the price follows standing like every other).</summary>
    void InstrumentShopRows(VBoxContainer list, string tab)
    {
        InstrumentDb.Load(); var d = Game.D;
        foreach (var def in InstrumentDb.All)
        {
            if (InstrumentDb.Has(def.Id)) continue; var ins = def; int c = Cost(ins.Price);
            AddRow(list, $"{ins.Name}  ({ins.Sub}; take it out with {Inputs.KeyOf(Inputs.Instrument)})", Colors.White, $"Buy {c}g", () =>
            {
                if (d.Gold < c) { Sfx.UiError(); Note("Not enough gold", new Color(1, .5f, .5f)); return; }
                d.Gold -= c; InstrumentDb.Give(ins.Id); Traded(); Sfx.Play("coin"); Note($"You bought a {ins.Name}. Press {Inputs.KeyOf(Inputs.Instrument)} to take it out.", Gold); Game.Notify(); OpenShop(tab);
            }, d.Gold >= c);
        }
    }

    /// <summary>F-40: instruments not owned yet as crafting recipes (Crafting > Blocks, group "Instruments"; materials of data/instruments.json "cost").</summary>
    void InstrumentRecipes(List<CraftRec> recs)
    {
        InstrumentDb.Load(); var d = Game.D;
        foreach (var def in InstrumentDb.All)
        {
            if (InstrumentDb.Has(def.Id)) continue; var ins = def; var cost = new Dictionary<string, int>(ins.Cost);
            recs.Add(new CraftRec
            {
                Group = "Instruments", Title = ins.Name, Sub = $"{ins.Sub}. Take it out with {Inputs.KeyOf(Inputs.Instrument)}; {InstrumentDb.NoteKeys[0]}-{InstrumentDb.NoteKeys[^1]} play, Shift an octave up", Cost = cost, Col = UiTheme.Highlight, Btn = "Craft", Icon = "instrument_" + ins.Id,
                Do = () =>
                {
                    if (InstrumentDb.Has(ins.Id)) return false; if (!CanPay(cost)) { Note("Not enough materials", new Color(1, .5f, .5f)); return false; }
                    Pay(cost); InstrumentDb.Give(ins.Id); Note($"Crafted a {ins.Name}: press {Inputs.KeyOf(Inputs.Instrument)} to take it out", UiTheme.Highlight); Sfx.Play("level"); Game.Notify(); return true;
                },
            });
        }
    }
}
