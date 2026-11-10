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
            default: Bell(s, 0, C6, .2f, .15f); break;
        }
        var wav = Make(s); Synth[id] = wav; return wav;
    }

    public static void Play(string id, float vol = 1f)
    {
        if (I == null) return;
        bool file = Files.TryGetValue(id, out var l) && l.Count > 0;
        var stream = file ? l![(int)(GD.Randi() % (uint)l.Count)] : Gen(id);
        foreach (var p in I._pool)
            if (!p.Playing) { p.Stream = stream; p.VolumeDb = Mathf.LinearToDb(Game.Set.Sfx * vol * 0.7f + 0.0001f); p.PitchScale = 0.95f + GD.Randf() * 0.1f; p.Play(); return; }
    }

    // ------------------------------------------------------------------ ambience
    sealed class Layer { public AudioStreamPlayer P = null!; public float Gain, Level; }
    readonly Dictionary<string, Layer> _amb = new();
    void BuildAmbience()
    {
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
        T("day_birds", (1 - night) * (1 - shadow) * out_ * .8f);
        T("night_crickets", night * (1 - shadow) * out_ * .6f);
        T("breeze", (1 - shadow) * out_ * .3f);
        T("shadow_wind", shadow * out_ * .45f);
        T("dungeon", inD ? .55f : 0f);
        float a = Game.Set.Ambient;
        foreach (var l in _amb.Values) { l.Level = Mathf.MoveToward(l.Level, l.Gain, dt * .5f); l.P.VolumeDb = Mathf.LinearToDb(Mathf.Max(l.Level * a, 0.0001f)); }
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
    void TickMusic(float dt)
    {
        _music.VolumeDb = Mathf.LinearToDb(Mathf.Max(Game.Set.Music * .55f, 0.0001f));
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
