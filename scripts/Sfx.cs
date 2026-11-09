using System;
using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>Synthesised sound effects and ambient drones. No audio files needed.</summary>
public partial class Sfx : Node
{
    public static Sfx? I;
    static readonly Dictionary<string, AudioStreamWav> Cache = new();
    readonly List<AudioStreamPlayer> _pool = new();
    AudioStreamPlayer _ambL = null!, _ambD = null!;
    const int Rate = 22050;

    public override void _Ready()
    {
        I = this; ProcessMode = ProcessModeEnum.Always;
        for (int i = 0; i < 12; i++) { var p = new AudioStreamPlayer(); AddChild(p); _pool.Add(p); }
        _ambL = new AudioStreamPlayer { Stream = Drone(false) }; _ambD = new AudioStreamPlayer { Stream = Drone(true) };
        AddChild(_ambL); AddChild(_ambD); _ambL.Play(); _ambD.Play();
        ApplyVolumes();
    }
    // "Quit to title" reloads the scene; forget the old players so nothing touches them after they are freed
    public override void _ExitTree() { if (I == this) I = null; }

    static AudioStreamWav Make(float[] s, bool loop = false)
    {
        var b = new byte[s.Length * 2];
        for (int i = 0; i < s.Length; i++) { short v = (short)(Mathf.Clamp(s[i], -1f, 1f) * 32000); b[i * 2] = (byte)(v & 255); b[i * 2 + 1] = (byte)((v >> 8) & 255); }
        var w = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = false, Data = b };
        if (loop) { w.LoopMode = AudioStreamWav.LoopModeEnum.Forward; w.LoopBegin = 0; w.LoopEnd = s.Length; }
        return w;
    }

    /// <summary>Tone with pitch slide, noise mix and exponential decay.</summary>
    static AudioStreamWav Tone(float f0, float f1, float dur, float noise = 0f, float vol = 0.5f, int wave = 0)
    {
        int n = (int)(Rate * dur); var s = new float[n]; float ph = 0; var rng = new Random((int)(f0 * 7 + dur * 1000));
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n, f = f0 + (f1 - f0) * t; ph += f / Rate;
            float w = wave == 0 ? Mathf.Sin(ph * Mathf.Tau) : wave == 1 ? (ph % 1f < .5f ? 1f : -1f) : 2f * (ph % 1f) - 1f;
            float nz = (float)(rng.NextDouble() * 2 - 1);
            s[i] = (w * (1 - noise) + nz * noise) * Mathf.Exp(-4f * t) * Mathf.Min(1f, i / 60f) * vol;
        }
        return Make(s);
    }

    static AudioStreamWav Drone(bool dark)
    {
        int n = Rate * 6; var s = new float[n]; var rng = new Random(dark ? 5 : 3); float lp = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate, nz = (float)(rng.NextDouble() * 2 - 1); lp += (nz - lp) * (dark ? .01f : .03f);
            float tone = dark ? Mathf.Sin(t * Mathf.Tau * 55f) * .25f + Mathf.Sin(t * Mathf.Tau * 82.4f) * .12f : Mathf.Sin(t * Mathf.Tau * 220f) * .03f;
            s[i] = lp * (dark ? 0.5f : 0.35f) + tone;
        }
        // crossfade the loop seam
        int x = Rate / 4; for (int i = 0; i < x; i++) { float k = i / (float)x; s[i] = s[i] * k + s[n - x + i] * (1 - k) * 0f + s[i] * 0f + s[i] * (1 - k) * 0f; }
        return Make(s, true);
    }

    static AudioStreamWav Get(string id)
    {
        if (Cache.TryGetValue(id, out var w)) return w;
        w = id switch
        {
            "swing" => Tone(600, 180, .18f, .6f, .35f), "hit" => Tone(180, 60, .16f, .5f, .6f), "hurt" => Tone(220, 90, .25f, .3f, .6f, 2),
            "block" => Tone(900, 500, .12f, .5f, .5f, 1), "coin" => Tone(1400, 1900, .15f, 0f, .35f), "level" => Tone(520, 1040, .6f, 0f, .45f),
            "fire" => Tone(300, 90, .35f, .7f, .55f), "ice" => Tone(1200, 400, .4f, .4f, .4f), "zap" => Tone(1500, 200, .25f, .6f, .45f, 1),
            "heal" => Tone(500, 900, .5f, 0f, .35f), "roar" => Tone(110, 50, .9f, .5f, .7f, 2), "portal" => Tone(200, 800, .5f, .2f, .4f), "click" => Tone(800, 700, .05f, 0f, .25f, 1),
            "die" => Tone(300, 40, .8f, .3f, .6f, 2), "pickup" => Tone(900, 1200, .1f, 0f, .3f), "shift" => Tone(150, 900, .7f, .4f, .5f),
            _ => Tone(440, 440, .1f),
        };
        Cache[id] = w; return w;
    }

    public static void Play(string id, float vol = 1f)
    {
        if (I == null) return;
        foreach (var p in I._pool)
            if (!p.Playing) { p.Stream = Get(id); p.VolumeDb = Mathf.LinearToDb(Game.Set.Sfx * vol * 0.9f + 0.0001f); p.PitchScale = 0.92f + GD.Randf() * 0.16f; p.Play(); return; }
    }

    public static void ApplyVolumes()
    {
        AudioServer.SetBusVolumeDb(0, Mathf.LinearToDb(Mathf.Max(Game.Set.Master, 0.0001f)));
        if (I != null) I.SetRealmMix(I._realmT);
    }

    float _realmT;
    public void SetRealmMix(float t)
    {
        _realmT = t; float a = Game.Set.Ambient * 0.5f;
        _ambL.VolumeDb = Mathf.LinearToDb(Mathf.Max(a * (1 - t), 0.0001f)); _ambD.VolumeDb = Mathf.LinearToDb(Mathf.Max(a * t, 0.0001f));
    }
}
