using System;
using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>
/// Sound effects, ambience and music.
/// Effects: Play("id") picks a random file res://assets/audio/sfx/id_N.(wav|ogg|mp3); ids with no file fall back to a soft synthesised bell/whoosh.
/// Ambience: looping nature layers (day birds, night crickets, breeze, shadow wind, dungeon) cross-faded by time of day, realm and place.
/// Music: calm tracks from assets/audio/music/day|night, played with long quiet gaps between them (Minecraft style).
/// </summary>
public partial class Sfx : Node
{
    public static Sfx? I;
    static readonly Dictionary<string, AudioStream> Synth = new();
    static readonly Dictionary<string, List<AudioStream>> Files = new();
    readonly List<AudioStreamPlayer> _pool = new();
    const int Rate = 22050;
    const string SfxDir = "res://assets/audio/sfx/", AmbDir = "res://assets/audio/ambient/", MusicDir = "res://assets/audio/music/";

    public override void _Ready()
    {
        I = this; ProcessMode = ProcessModeEnum.Always;
        for (int i = 0; i < 12; i++) { var p = new AudioStreamPlayer(); AddChild(p); _pool.Add(p); }
        if (Files.Count == 0) ScanSfx();
        BuildAmbience(); BuildMusic();
        ApplyVolumes();
    }
    // "Quit to title" reloads the scene; forget the old players so nothing touches them after they are freed
    public override void _ExitTree() { if (I == this) I = null; }

    // ------------------------------------------------------------------ files
    /// <summary>Lists audio files in a folder. Exported builds only keep "x.wav.import", so the suffix is stripped.</summary>
    static List<string> List(string dir)
    {
        var res = new List<string>(); if (!DirAccess.DirExistsAbsolute(dir)) return res;
        foreach (var f0 in DirAccess.GetFilesAt(dir))
        {
            var f = f0.Replace(".import", "").Replace(".remap", "");
            if ((f.EndsWith(".wav") || f.EndsWith(".ogg") || f.EndsWith(".mp3")) && !res.Contains(f)) res.Add(f);
        }
        res.Sort(); return res;
    }
    static AudioStream? Load(string path) => ResourceLoader.Exists(path) ? GD.Load<AudioStream>(path) : null;

    static void ScanSfx()
    {
        foreach (var f in List(SfxDir))
        {
            var id = System.IO.Path.GetFileNameWithoutExtension(f); int us = id.LastIndexOf('_'); if (us > 0 && int.TryParse(id[(us + 1)..], out _)) id = id[..us];
            var s = Load(SfxDir + f); if (s == null) continue;
            if (!Files.TryGetValue(id, out var l)) Files[id] = l = new List<AudioStream>(); l.Add(s);
        }
    }

    // ------------------------------------------------------------------ gentle synthesis (fallback for ids without a file)
    static AudioStreamWav Make(float[] s)
    {
        var b = new byte[s.Length * 2];
        for (int i = 0; i < s.Length; i++) { short v = (short)(Mathf.Clamp(s[i], -1f, 1f) * 32000); b[i * 2] = (byte)(v & 255); b[i * 2 + 1] = (byte)((v >> 8) & 255); }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = false, Data = b };
    }
    /// <summary>Soft bell: a sine with a quiet octave and twelfth, quick attack, long exponential decay.</summary>
    static void Bell(float[] s, float at, float f, float dur, float vol)
    {
        int i0 = (int)(at * Rate), n = (int)(dur * Rate);
        for (int i = 0; i < n && i0 + i < s.Length; i++)
        {
            float t = i / (float)Rate, env = Mathf.Min(1f, i / (Rate * .006f)) * Mathf.Exp(-t * 5f / dur);
            s[i0 + i] += (Mathf.Sin(Mathf.Tau * f * t) + .25f * Mathf.Sin(Mathf.Tau * f * 2f * t) + .08f * Mathf.Sin(Mathf.Tau * f * 3f * t)) * env * vol;
        }
    }
    /// <summary>Airy whoosh: low-passed noise with a rise-and-fall envelope.</summary>
    static void Whoosh(float[] s, float at, float dur, float vol, float bright, int seed)
    {
        var rng = new Random(seed); int i0 = (int)(at * Rate), n = (int)(dur * Rate); float lp = 0, lp2 = 0;
        for (int i = 0; i < n && i0 + i < s.Length; i++)
        {
            float k = i / (float)n, env = Mathf.Sin(Mathf.Pi * Mathf.Pow(k, .6f)); float nz = (float)(rng.NextDouble() * 2 - 1);
            lp += (nz - lp) * bright; lp2 += (lp - lp2) * bright; s[i0 + i] += lp2 * env * vol * 3f;
        }
    }
    /// <summary>Plucked string (Karplus-Strong): a noise burst through a damped delay line at f Hz.</summary>
    static void Pluck(float[] s, float at, float f, float dur, float vol, int seed)
    {
        var rng = new Random(seed); int i0 = (int)(at * Rate), n = (int)(dur * Rate), p = Math.Max(2, (int)(Rate / f)); var buf = new float[p];
        for (int i = 0; i < p; i++) buf[i] = (float)(rng.NextDouble() * 2 - 1);
        for (int i = 0; i < n && i0 + i < s.Length; i++)
        {
            int k = i % p; float v = buf[k]; buf[k] = (v + buf[(k + 1) % p]) * .5f * .996f;
            s[i0 + i] += v * vol * Mathf.Exp(-i / (float)n * 3f);
        }
    }
    /// <summary>Wood creak: stick-slip clicks whose rate rises (40 to 110 a second), low-passed.</summary>
    static void Creak(float[] s, float at, float dur, float vol, int seed)
    {
        var rng = new Random(seed); int i0 = (int)(at * Rate), n = (int)(dur * Rate); float lp = 0, next = 0, ph = 0;
        for (int i = 0; i < n && i0 + i < s.Length; i++)
        {
            float k = i / (float)n; ph += 1f / Rate;
            float x = 0; if (ph >= next) { x = (float)(rng.NextDouble() * .6 + .7); ph = 0; next = 1f / Mathf.Lerp(40f, 110f, k) * (float)(rng.NextDouble() * .4 + .8); }
            lp += (x - lp) * .25f; s[i0 + i] += lp * vol * 4f * Mathf.Sin(Mathf.Pi * k);
        }
    }
    /// <summary>Sine sweep from f0 to f1 Hz (magic zaps).</summary>
    static void Chirp(float[] s, float at, float f0, float f1, float dur, float vol)
    {
        int i0 = (int)(at * Rate), n = (int)(dur * Rate); float ph = 0;
        for (int i = 0; i < n && i0 + i < s.Length; i++) { float k = i / (float)n; ph += Mathf.Tau * Mathf.Lerp(f0, f1, k) / Rate; s[i0 + i] += Mathf.Sin(ph) * vol * Mathf.Min(1f, i / (Rate * .004f)) * (1f - k); }
    }
    /// <summary>Sparse bright crackle (electric sparks).</summary>
    static void Crackle(float[] s, float at, float dur, float vol, int seed)
    {
        var rng = new Random(seed); int i0 = (int)(at * Rate), n = (int)(dur * Rate);
        for (int i = 0; i < n && i0 + i < s.Length; i++) if (rng.NextDouble() < .03) s[i0 + i] += (float)(rng.NextDouble() * 2 - 1) * vol * 3f * (1f - i / (float)n);
    }

    // ------------------------------------------------------------------ weapon sounds (data/weapons.json "sfx")
    /// <summary>Dev (DevCapture "sfxlog:"): every played id is printed and kept in Played.</summary>
    public static bool Log; public static readonly List<string> Played = new();
    static readonly HashSet<string> _warned = new();
    /// <summary>
    /// A weapon type's sound for an event ("swing", "shoot", "hit", "miss"): a random pick from its data/weapons.json "sfx" list, at its volume
    /// and a random pitch in its range. A type with no sounds for the event plays the generic one, with a warning once per type and event.
    /// </summary>
    public static void Weapon(string wt, string ev, float vol = 1f, float pitch = 1f)
    {
        var w = Weapons.Get(wt);
        if (!w.Sfx.TryGetValue(ev, out var l) || l.Count == 0)
        {
            if (_warned.Add(wt + "/" + ev)) GD.PushWarning($"[SFX] weapon type '{wt}' has no '{ev}' sounds in data/weapons.json; playing the fallback");
            string fb = ev switch { "hit" => "hit", "miss" => "whiff", "shoot" => "swing", _ => "swing" };
            if (Log) Note($"{wt}.{ev} FALLBACK {fb}");
            Play(fb, vol); return;
        }
        var p = l[(int)(GD.Randi() % (uint)l.Count)];
        if (p.Id == "") return;   // an explicit silence
        if (Log) Note($"{wt}.{ev} -> {p.Id}");
        Play(p.Id, vol * p.Vol, pitch * (p.P0 + GD.Randf() * (p.P1 - p.P0)));
    }
    static void Note(string s) { Played.Add(s); GD.Print("[SFX] " + s); }

    static AudioStream Gen(string id)
    {
        if (Synth.TryGetValue(id, out var w)) return w;
        float len = id switch { "shift" => 1.8f, "level" => 1.4f, "die" => 1.6f, "portal" => 1.4f, "roar" => 1.2f, "heal" => 1.2f, _ => .7f };
        var s = new float[(int)(len * Rate)];
        // C major pentatonic, so any overlap of sounds stays consonant
        float C5 = 523.25f, D5 = 587.33f, E5 = 659.25f, G5 = 783.99f, A5 = 880f, C6 = 1046.5f;
        switch (id)
        {
            case "swing": Whoosh(s, 0, .22f, .35f, .18f, 1); break;
            case "hit": Bell(s, 0, 150f, .18f, .5f); Whoosh(s, 0, .08f, .25f, .08f, 2); break;
            case "punch_swing": Whoosh(s, 0, .13f, .26f, .07f, 7); break;   // fists: a short, dark cloth whoosh (no blade ring)
            case "punch_hit": Bell(s, 0, 92f, .13f, .55f); Whoosh(s, 0, .05f, .35f, .05f, 8); break;   // a low body thump
            case "hurt": Bell(s, 0, 220f, .3f, .35f); Bell(s, .02f, 330f, .25f, .15f); break;
            case "block": Bell(s, 0, 1174.7f, .25f, .25f); break;
            case "coin": Bell(s, 0, G5 * 2, .3f, .22f); Bell(s, .06f, C6 * 2, .35f, .2f); break;
            case "pickup": Bell(s, 0, E5 * 2, .3f, .2f); break;
            case "click": Bell(s, 0, C6, .06f, .15f); break;
            case "level": { float[] n = { C5, E5, G5, C6 }; for (int k = 0; k < 4; k++) Bell(s, k * .1f, n[k], 1f, .22f); break; }
            case "fire": Whoosh(s, 0, .45f, .3f, .06f, 3); Bell(s, .02f, G5 / 2, .5f, .16f); break;
            case "ice": Bell(s, 0, C6 * 1.5f, .5f, .18f); Bell(s, .05f, E5 * 2, .5f, .12f); break;
            case "zap": { float[] n = { E5 * 2, G5 * 2, A5 * 2 }; for (int k = 0; k < 3; k++) Bell(s, k * .035f, n[k], .35f, .14f); break; }
            case "heal": { float[] n = { C5, D5, E5, G5, A5 }; for (int k = 0; k < 5; k++) Bell(s, k * .07f, n[k], .9f, .14f); break; }
            case "portal": { float[] n = { G5 / 2, C5, E5, G5, C6 }; for (int k = 0; k < 5; k++) Bell(s, k * .09f, n[k], 1f, .15f); Whoosh(s, 0, 1.2f, .2f, .04f, 4); break; }
            case "shift": Bell(s, 0, 196f, 1.8f, .25f); Bell(s, .15f, 293.66f, 1.6f, .18f); Bell(s, .3f, 392f, 1.4f, .12f); Whoosh(s, 0, 1.5f, .18f, .03f, 5); break;
            case "die": { float[] n = { G5, E5, C5, G5 / 2 }; for (int k = 0; k < 4; k++) Bell(s, k * .2f, n[k], 1f, .2f); break; }
            case "roar": Whoosh(s, 0, 1.1f, .5f, .02f, 6); Bell(s, 0, 98f, 1.1f, .3f); break;
            // ---- weapon families (data/weapons.json "sfx"; a file with the same id overrides these)
            case "whoosh_quick": Whoosh(s, 0, .12f, .3f, .38f, 11); break;                                   // dagger / katana: short, high
            case "whoosh_heavy": Whoosh(s, 0, .4f, .42f, .045f, 12); Bell(s, .05f, 62f, .3f, .12f); break;     // greatsword / axe: long, deep
            case "whoosh_blunt": Whoosh(s, 0, .27f, .36f, .07f, 13); break;                                 // mace: dark, round
            case "swish_wood": Whoosh(s, 0, .24f, .3f, .12f, 14); Bell(s, .03f, 196f, .12f, .06f); break;      // staff / spear: airy, a hollow wooden note
            case "whiff": Whoosh(s, 0, .16f, .14f, .25f, 15); break;                                        // a miss: a thin trailing tail of air
            case "whiff_heavy": Whoosh(s, 0, .3f, .2f, .05f, 16); break;
            case "slash_hit": Bell(s, 0, 210f, .1f, .35f); Whoosh(s, 0, .06f, .35f, .4f, 17); break;          // quick blades: a bright cut
            case "chop_hit": Bell(s, 0, 104f, .22f, .6f); Whoosh(s, 0, .09f, .45f, .1f, 18); break;           // heavy blades: a deep chop
            case "blunt_hit": Bell(s, 0, 70f, .26f, .75f); Bell(s, 0, 140f, .1f, .2f); Whoosh(s, 0, .07f, .5f, .04f, 19); break;   // mace: a heavy thud
            case "wood_hit": Bell(s, 0, 262f, .09f, .4f); Bell(s, 0, 392f, .07f, .15f); Whoosh(s, 0, .04f, .25f, .2f, 20); break;   // staff / spear shaft: a wooden knock
            case "bow_draw": Creak(s, 0, .34f, .22f, 21); break;                                             // the bow bends: a soft creak
            case "bow_twang": Pluck(s, 0, 98f, .45f, .55f, 22); Whoosh(s, .01f, .16f, .3f, .3f, 23); break;   // the string snaps and the arrow leaves
            case "arrow_hit": Bell(s, 0, 175f, .08f, .5f); Whoosh(s, 0, .035f, .4f, .3f, 24); break;          // a tight thunk
            case "arrow_miss": Bell(s, 0, 120f, .07f, .2f); Whoosh(s, 0, .05f, .2f, .1f, 25); break;          // into the ground
            case "wand_zap": Chirp(s, 0, 1900f, 620f, .16f, .16f); Crackle(s, 0, .12f, .12f, 26); break;         // a falling magic chirp
            case "wand_hit": { float[] n = { G5 * 2, C6 * 2, E5 * 4 }; for (int k = 0; k < 3; k++) Bell(s, k * .02f, n[k], .2f, .1f); Crackle(s, 0, .1f, .18f, 27); break; }
            case "wand_fizzle": Crackle(s, 0, .3f, .1f, 28); Chirp(s, 0, 900f, 300f, .25f, .05f); break;
            case "orb_hit": Bell(s, 0, 82f, .4f, .5f); Whoosh(s, 0, .35f, .4f, .05f, 29); break;               // staff orb bursting
            case "punch_whiff": Whoosh(s, 0, .1f, .12f, .1f, 30); break;
            // ---- placed blocks (BlockUse.cs): doors swing with a hinge creak, shut with a wooden knock; iron doors clang; chests creak; locks click
            case "door_open": Bell(s, 0, 180f, .05f, .3f); Creak(s, .02f, .28f, .2f, 31); break;
            case "door_close": Creak(s, 0, .16f, .14f, 32); Bell(s, .17f, 98f, .18f, .6f); Bell(s, .17f, 196f, .08f, .2f); Whoosh(s, .16f, .05f, .3f, .1f, 33); break;
            case "door_iron": Bell(s, 0, 146.8f, .5f, .3f); Bell(s, 0, 311f, .35f, .12f); Bell(s, .01f, 587f, .2f, .05f); Creak(s, 0, .22f, .08f, 34); break;
            case "chest_open": Creak(s, 0, .32f, .18f, 35); Bell(s, .3f, 130f, .1f, .25f); break;
            case "lock": Bell(s, 0, 1568f, .05f, .2f); Bell(s, .04f, 1175f, .06f, .18f); break;
            default: Bell(s, 0, C6, .2f, .15f); break;
        }
        var wav = Make(s); Synth[id] = wav; return wav;
    }

    /// <summary>Per-sound loudness (1 = as recorded). Pickup and the wand's zap were far too loud next to everything else.</summary>
    static readonly Dictionary<string, float> Gain = new() { ["pickup"] = .3f, ["zap"] = .35f, ["coin"] = .6f };

    public static void Play(string id, float vol = 1f) => Play(id, vol, 1f);
    /// <summary>Which volume slider a sound follows: Footsteps (step_*), Interface (menu clicks, level-ups, coins, pickups, portals) or Combat and spells (everything else).</summary>
    static readonly HashSet<string> UiSounds = new() { "click", "level", "coin", "pickup", "portal", "shift" };
    static float Channel(string id) => id.StartsWith("step_") ? Game.Set.Steps : id.StartsWith("ui_") || UiSounds.Contains(id) ? Game.Set.UiVol : Game.Set.Sfx;
    // Gothic menu sounds (tools/kit_gen/sfx_synth.py): every "click" uses the heavier ui_click clack when the files exist.
    static ulong _hoverAt; static string _openName = "";
    /// <summary>Soft tick when the mouse enters a button; at most one per 60 ms.</summary>
    public static void UiHover() { ulong n = Time.GetTicksMsec(); if (n - _hoverAt < 60) return; _hoverAt = n; Play("ui_hover"); }
    /// <summary>A menu panel was (re)built: the open sound plays only when a different panel replaces the previous one.</summary>
    public static void UiPanel(string name) { if (name == _openName) return; _openName = name; Play("ui_open"); }
    public static void UiClosed() { if (_openName == "") return; _openName = ""; Play("ui_close"); }
    public static void UiConfirm() => Play("ui_confirm");
    public static void UiError() => Play("ui_error");
    /// <summary>Plays a sound at a pitch (1 = as recorded, a little random spread is added).</summary>
    public static void Play(string id, float vol, float pitch)
    {
        if (I == null) return;
        vol *= Gain.GetValueOrDefault(id, 1f);
        if (id == "click" && Files.ContainsKey("ui_click")) id = "ui_click";
        bool file = Files.TryGetValue(id, out var l) && l.Count > 0;
        var stream = file ? l![(int)(GD.Randi() % (uint)l.Count)] : Gen(id);
        foreach (var p in I._pool)
            if (!p.Playing) { p.Stream = stream; p.VolumeDb = Mathf.LinearToDb(Channel(id) * vol * 0.7f + 0.0001f); p.PitchScale = pitch * (0.95f + GD.Randf() * 0.1f); p.Play(); return; }
    }

    // ------------------------------------------------------------------ footsteps
    /// <summary>
    /// A soft footstep for the ground at p: grass, forest floor, dirt road, sand, snow, stone (plazas, castles, sea bridges, dungeons) or wood
    /// (river and rope bridges, stilt decks). The collider under the foot decides built ground; open ground asks the terrain.
    /// Quiet on purpose; louder when running. Numbers: Steps table below.
    /// </summary>
    public static void Step(Vector3 p, Node3D who, float speed)
    {
        if (I == null || Game.Set.Steps <= 0) return;
        string kind = BlockStep(p) ?? StepKind(p, who); StepCount++; LastStep = kind;
        var (id, pitch, vol) = kind switch
        {
            "gravel" => ("step_gravel", 1f, .5f), "ice" => ("step_stone", 1.25f, .45f),
            "stone" => ("step_stone", 1f, .55f), "wood" => ("step_wood", 1f, .5f), "leaves" => ("step_leaves", 1.05f, .32f), "dirt" => ("step_dirt", 1.1f, .4f),
            "water" => ("step_water", 1f, .5f),
            "sand" => ("step_soft", .92f, .55f), "snow" => ("step_soft", .68f, .6f), _ => ("step_soft", 1.18f, .32f),   // grass: the soft steps, lighter
        };
        if (who is Player) Fx.Footfall(p, kind, speed);
        vol *= Mathf.Lerp(.75f, 1.15f, Mathf.Clamp((speed - 1f) / 5f, 0, 1));
        Play(id, vol, pitch * (.94f + GD.Randf() * .12f));
    }
    /// <summary>Surface class of the 1 m block under the foot (null outside the block area, over air, or in water: no splash sound exists, so today's logic decides).</summary>
    static string? BlockStep(Vector3 p)
    {
        if (BlockWorld.Enabled && BlockWorld.FluidAt(p + Vector3.Up * .3f) == "water") return "water";   // wading
        byte b = BlockWorld.At(p + Vector3.Down * .1f);
        if (b == 255 || b == 0) return null;
        string s = Blocks.Def(b).Step;
        return s == "" ? null : s;
    }
    public static string LastStep = ""; public static int StepCount;
    static string StepKind(Vector3 p, Node3D who)
    {
        if (Dungeon.Active) return LastStep = "stone";
        // what is under the foot: a building, bridge or deck, or the terrain
        var q = PhysicsRayQueryParameters3D.Create(p + Vector3.Up * .6f, p + Vector3.Down * .6f, 1);
        if (who is CollisionObject3D co) q.Exclude = new Godot.Collections.Array<Rid> { co.GetRid() };
        var hit = who.GetWorld3D().DirectSpaceState.IntersectRay(q);
        if (hit.Count > 0 && hit["collider"].As<Node>() is { } n && n.Name != "Terrain")
        {
            for (Node? x = n; x != null; x = x.GetParent())
            {
                string nm = x.Name;
                if (nm.Contains("RiverBridge") || nm.Contains("RopeBridge") || nm.Contains("Stilt")) return LastStep = "wood";
                if (nm.StartsWith("Bridge_") || nm == "Dungeon") return LastStep = "stone";
                if (nm == "Overworld") break;
            }
            return LastStep = "stone";
        }
        return LastStep = World.SurfaceAt(p.X, p.Z, p.Y);
    }

    // ------------------------------------------------------------------ ambience
    sealed class Layer { public AudioStreamPlayer P = null!; public float Gain, Level; }
    readonly Dictionary<string, Layer> _amb = new();
    /// <summary>data/ambient.json "sound": false = no ambience layers at all, only music (the park recording had traffic in it).</summary>
    public static bool AmbienceOn
    {
        get { if (_ambOn == null) { using var doc = JsonUtil.Load("res://data/ambient.json"); _ambOn = !(doc.RootElement.TryGetProperty("sound", out var s) && s.ValueKind == System.Text.Json.JsonValueKind.False); } return _ambOn.Value; }
    }
    static bool? _ambOn;
    void BuildAmbience()
    {
        if (!AmbienceOn) return;
        foreach (var f in List(AmbDir))
        {
            var s = Load(AmbDir + f); if (s == null) continue;
            var key = System.IO.Path.GetFileNameWithoutExtension(f);
            var p = new AudioStreamPlayer { Stream = s, VolumeDb = -80f }; AddChild(p);
            p.Finished += () => p.Play();   // loop every format the same way
            p.Play(); _amb[key] = new Layer { P = p };
        }
    }
    void MixAmbience(float dt)
    {
        bool inD = Dungeon.Active; float shadow = World.RealmT, out_ = inD ? 0f : 1f;
        float night = Mathf.Clamp((World.Night - shadow * .7f) / Mathf.Max(.01f, 1f - shadow), 0, 1);   // night without the realm's darkening
        void T(string k, float g) { if (_amb.TryGetValue(k, out var l)) l.Gain = g; }
        if (_ext.Count == 0) { T("day_birds", (1 - night) * (1 - shadow) * out_ * .8f); T("night_crickets", night * (1 - shadow) * out_ * .6f); T("breeze", (1 - shadow) * out_ * .3f); T("shadow_wind", shadow * out_ * .45f); }   // fallback until Ambient.cs drives the layers
        else foreach (var kv in _ext) T(kv.Key, inD ? 0f : kv.Value);
        T("dungeon", inD ? .55f : 0f);
        float a = Game.Set.Ambient;
        foreach (var l in _amb.Values) { l.Level = Mathf.MoveToward(l.Level, l.Gain, dt / Mathf.Max(.2f, FadeSecs)); l.P.VolumeDb = Mathf.LinearToDb(Mathf.Max(l.Level * a, 0.0001f)); }
    }
    readonly Dictionary<string, float> _ext = new();
    /// <summary>Crossfade time of the ambience layers (data/ambient.json soundscape.fade).</summary>
    public static float FadeSecs = 2.5f;
    /// <summary>Target loudness (0..1, before the Ambient slider) of a looping layer from scripts/Ambient.cs; the mixer fades to it.</summary>
    public static void SetLayer(string key, float gain) { if (I != null) I._ext[key] = gain; }
    /// <summary>True when a one-shot sound file exists for this id (no synth fallback).</summary>
    public static bool Has(string id) => Files.TryGetValue(id, out var l) && l.Count > 0;
    /// <summary>A spatial one-shot at a world position, following the Ambient slider. Silently skipped when the id has no file.</summary>
    public static void PlayAt(Node parent, string id, Vector3 pos, float vol, float pitch)
    {
        if (!Has(id) || !AmbienceOn) return; var l = Files[id];
        var p = new AudioStreamPlayer3D { Stream = l[(int)(GD.Randi() % (uint)l.Count)], VolumeDb = Mathf.LinearToDb(Mathf.Max(vol * Game.Set.Ambient, 0.0001f)), PitchScale = pitch, UnitSize = 12f, MaxDistance = 90f, AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance };
        parent.AddChild(p); p.GlobalPosition = pos; p.Finished += p.QueueFree; p.Play();
    }

    // ------------------------------------------------------------------ music
    readonly List<AudioStream> _day = new(), _night = new();
    AudioStreamPlayer _music = null!; float _gap; AudioStream? _last;
    void BuildMusic()
    {
        foreach (var f in List(MusicDir + "day")) if (Load(MusicDir + "day/" + f) is AudioStream s) { Unloop(s); _day.Add(s); }
        foreach (var f in List(MusicDir + "night")) if (Load(MusicDir + "night/" + f) is AudioStream s) { Unloop(s); _night.Add(s); }
        _music = new AudioStreamPlayer(); AddChild(_music);
        _music.Finished += () => _gap = (float)GD.RandRange(60.0, 150.0);   // a quiet stretch before the next song
        _gap = (float)GD.RandRange(6.0, 14.0);
    }
    static void Unloop(AudioStream s) { if (s is AudioStreamMP3 m) m.Loop = false; else if (s is AudioStreamOggVorbis o) o.Loop = false; }
    // the raid lair's own song (assets/audio/music/lair): plays, looping, only while we are inside that instance
    AudioStream? _lairSong; bool _inLair;
    void TickMusic(float dt)
    {
        _music.VolumeDb = Mathf.LinearToDb(Mathf.Max(Game.Set.Music * .55f, 0.0001f));
        bool lair = Dungeon.Active && Dungeon.IsLair(Dungeon.Key);
        if (lair != _inLair)
        {
            _inLair = lair; _music.Stop();
            if (lair) { _lairSong ??= Load(MusicDir + "lair/scarletta_sadness.ogg") as AudioStream; if (_lairSong is AudioStreamOggVorbis o) o.Loop = true; if (_lairSong != null) { _music.Stream = _lairSong; _music.Play(); } }
            else _gap = (float)GD.RandRange(8.0, 20.0);   // back to the world's own music after a while
        }
        if (_inLair) { if (!_music.Playing && _lairSong != null) _music.Play(); return; }
        if (_music.Playing) return;
        _gap -= dt; if (_gap > 0) return;
        bool dark = Dungeon.Active || World.RealmT > .5f || World.Night > .55f;
        var list = dark && _night.Count > 0 ? _night : _day.Count > 0 ? _day : _night; if (list.Count == 0) { _gap = 9999; return; }
        AudioStream pick; int guard = 0; do pick = list[(int)(GD.Randi() % (uint)list.Count)]; while (pick == _last && list.Count > 1 && ++guard < 8);
        _last = pick; _music.Stream = pick; _music.Play();
    }
    /// <summary>Skips the wait so a song starts now (used when the scene changes mood, e.g. entering a dungeon).</summary>
    public static void MusicSoon() { if (I != null && !I._music.Playing) I._gap = Mathf.Min(I._gap, 3f); }

    public override void _Process(double delta) { float dt = (float)delta; MixAmbience(dt); TickMusic(dt); }

    public static void ApplyVolumes()
    {
        AudioServer.SetBusVolumeDb(0, Mathf.LinearToDb(Mathf.Max(Game.Set.Master, 0.0001f)));
    }
    /// <summary>Kept for World: realm blending is now read from World.RealmT every frame.</summary>
    public void SetRealmMix(float t) { }
}
