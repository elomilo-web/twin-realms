# Focus F-39: synthesises the game's own calm, loopable music (no downloads, nothing copied) into assets/audio/music/gen/*.wav.
#   powershell -File tools/music_gen.ps1            -> writes every track and prints duration, peak and RMS of each
# Needs nothing but Windows PowerShell (the C# below is compiled by Add-Type, so it is written in old C# 5 style). No Godot.
# Each piece: a key and a pentatonic scale, a 4-bar chord loop, a slow pad, a bass note per bar, a plucked arpeggio and a melody
# (flute / lute / harp / bell) built from seeded 4-bar phrases in a song form ("AaBa"). Everything is written into a circular
# buffer (notes that ring past the end wrap to the start) and the reverb runs twice around it, so the file loops without a seam.
# Tracks are listed in data/music.json; scripts/Music.cs loads the .wav directly when Godot has not imported it yet.
param([string]$out = "")
$ErrorActionPreference = "Stop"
if (-not $out) { $out = Join-Path (Join-Path $PSScriptRoot "..") "assets\audio\music\gen" }
$out = [IO.Path]::GetFullPath($out)
if (-not (Test-Path $out)) { New-Item -ItemType Directory -Force $out | Out-Null }

Add-Type -Language CSharp -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.IO;

public class MTrack
{
    public string File; public int Seed; public double Bpm; public int Bpb = 4, Bars = 16, Root = 62;
    public int[] Scale; public int[][] Prog;
    public string Arp = "lute", Mel = "flute", Form = "AaBa";
    public int[] ArpPat = new int[] { 0, 1, 2, 3, 2, 1, 2, 1 };
    public double ArpEvery = .5, ArpProb = .9, ArpVol = .15, MelProb = .6, MelVol = .2, Pad = .08, Bass = .16, Wet = .3;
    public int MelOct = 12, ArpOct = 0;
}

public static class MusicGen
{
    public const int SR = 22050;
    static double[] buf; static int N; static Random rng;

    static double Hz(double midi) { return 440.0 * Math.Pow(2, (midi - 69) / 12.0); }
    static void Add(int i, double v) { int k = i % N; if (k < 0) k += N; buf[k] += v; }

    // plucked string (lute / harp): decaying harmonics, the high ones die first; a second string a hair sharp for width
    static void Pluck(double at, double f, double dur, double vol, double bright)
    {
        int i0 = (int)(at * SR), n = (int)(dur * 1.6 * SR); double k = 3.2 / dur;
        for (int h = 1; h <= 8; h++)
        {
            double fh = f * h; if (fh > 8500) break;
            double amp = vol * Math.Pow(bright, h - 1) / Math.Pow(h, 1.05), dk = k * (1 + .4 * (h - 1));
            double w1 = 2 * Math.PI * fh / SR, w2 = w1 * 1.0028;
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)SR, env = Math.Min(1.0, i / (SR * .003)) * Math.Exp(-t * dk);
                if (env < .0004 && i > 200) break;
                Add(i0 + i, (Math.Sin(w1 * i) * .7 + Math.Sin(w2 * i) * .3) * env * amp);
            }
        }
    }
    // soft bell / celesta: a pure tone with two quiet, slightly inharmonic partials
    static void Bell(double at, double f, double dur, double vol)
    {
        int i0 = (int)(at * SR), n = (int)(dur * 1.8 * SR);
        double[] ratio = new double[] { 1, 2.0, 3.01, 4.2 }, amp = new double[] { 1, .3, .1, .04 };
        for (int p = 0; p < 4; p++)
        {
            double fp = f * ratio[p]; if (fp > 9000) break; double w = 2 * Math.PI * fp / SR, dk = (2.6 + 1.8 * p) / dur;
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)SR, env = Math.Min(1.0, i / (SR * .004)) * Math.Exp(-t * dk);
                if (env < .0004 && i > 200) break;
                Add(i0 + i, Math.Sin(w * i) * env * vol * amp[p]);
            }
        }
    }
    // flute: a breathy sustained tone, soft attack, vibrato that comes in late
    static void Flute(double at, double f, double dur, double vol)
    {
        int i0 = (int)(at * SR), rel = (int)(.22 * SR), n = (int)(dur * SR) + rel; double ph = 0, lp = 0, vph = rng.NextDouble() * 6.28;
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)SR;
            double env = Math.Min(1.0, t / .09); if (i > n - rel) env *= (n - i) / (double)rel;
            double vib = 1 + .0045 * Math.Min(1.0, Math.Max(0.0, (t - .25) / .5)) * Math.Sin(vph + 2 * Math.PI * 5.1 * t);
            ph += 2 * Math.PI * f * vib / SR;
            double nz = rng.NextDouble() * 2 - 1; lp += (nz - lp) * .3;
            double s = Math.Sin(ph) + .26 * Math.Sin(2 * ph) + .09 * Math.Sin(3 * ph) + .03 * Math.Sin(4 * ph);
            s += lp * (.05 + .2 * Math.Exp(-t * 28));
            Add(i0 + i, s * env * vol * (.92 + .08 * Math.Sin(2 * Math.PI * .7 * t)));
        }
    }
    // pad: three detuned soft voices, slow in and out
    static void PadTone(double at, double f, double dur, double vol)
    {
        double a = 1.3, r = 1.8; int i0 = (int)((at - a * .5) * SR), n = (int)((dur + a * .5 + r) * SR);
        double[] det = new double[] { 1, 1.0035, .997 }; double p0 = rng.NextDouble() * 6.28;
        for (int d = 0; d < 3; d++)
        {
            double w = 2 * Math.PI * f * det[d] / SR, ph0 = p0 + d * 2.1;
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)SR, env = Math.Min(1.0, t / a); double left = (n - i) / (double)SR; if (left < r) env *= left / r;
                env = env * env * (3 - 2 * env);
                double x = w * i + ph0;
                Add(i0 + i, (Math.Sin(x) + .22 * Math.Sin(2 * x) + .07 * Math.Sin(3 * x)) * env * vol / 3 * (.9 + .1 * Math.Sin(2 * Math.PI * .23 * t + d)));
            }
        }
    }
    static void BassTone(double at, double f, double dur, double vol)
    {
        int i0 = (int)(at * SR), n = (int)(dur * SR); double w = 2 * Math.PI * f / SR, dk = 2.2 / dur;
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)SR, env = Math.Min(1.0, t / .025) * Math.Exp(-t * dk); double left = (n - i) / (double)SR; if (left < .08) env *= left / .08;
            Add(i0 + i, (Math.Sin(w * i) + .3 * Math.Sin(2 * w * i)) * env * vol);
        }
    }
    static void Voice(string kind, double at, double midi, double beats, double beat, double vol)
    {
        double f = Hz(midi); at += (rng.NextDouble() - .5) * .012; vol *= .85 + rng.NextDouble() * .3;
        if (kind == "flute") Flute(at, f, Math.Max(.25, beats * beat * .92), vol);
        else if (kind == "harp") Pluck(at, f, Math.Max(1.4, beats * beat * 1.3), vol, .5);
        else if (kind == "bell") Bell(at, f, Math.Max(1.6, beats * beat * 1.4), vol);
        else Pluck(at, f, Math.Max(.9, beats * beat), vol, .72);
    }

    static int Pc(int x) { return ((x % 12) + 12) % 12; }
    static int Semi(int[] scale, int idx) { int o = (int)Math.Floor(idx / (double)scale.Length); return scale[idx - o * scale.Length] + 12 * o; }
    static int Snap(int[] scale, int[] chord, int idx)
    {
        for (int d = 0; d <= 3; d++)
            foreach (int s in new int[] { d, -d })
            {
                int pc = Pc(Semi(scale, idx + s)); foreach (int c in chord) if (Pc(c) == pc) return idx + s;
            }
        return idx;
    }
    class Note { public double Pos, Dur; public int Idx; }

    static List<Note> Phrase(MTrack t, int start, bool closing)
    {
        List<Note> l = new List<Note>(); int idx = start; double pos = 0, len = 4 * t.Bpb; int ns = t.Scale.Length;
        double[] durs = new double[] { 1, 1, 2, 2, 3, .5, 1.5 }; int[] steps = new int[] { -2, -1, -1, 0, 1, 1, 2 };
        while (pos < len - .01)
        {
            bool strong = Math.Abs(pos % t.Bpb) < .01;
            if (rng.NextDouble() < t.MelProb + (strong ? .25 : 0))
            {
                double d = durs[rng.Next(durs.Length)]; if (pos + d > len) d = len - pos;
                idx += steps[rng.Next(steps.Length)]; if (idx < 0) idx += 2; if (idx > 2 * ns - 1) idx -= 2;
                int bar = (int)(pos / t.Bpb); if (strong) idx = Snap(t.Scale, t.Prog[bar % t.Prog.Length], idx);
                Note n = new Note(); n.Pos = pos; n.Dur = d; n.Idx = idx; l.Add(n); pos += d;
            }
            else pos += (rng.NextDouble() < .5 ? 1 : .5);
            pos = Math.Round(pos * 2) / 2;
        }
        if (closing)
        {   // the last bar becomes one long home note
            double cut = 3 * t.Bpb; l.RemoveAll(delegate(Note n) { return n.Pos >= cut - .01; });
            Note e = new Note(); e.Pos = cut; e.Dur = t.Bpb; e.Idx = Snap(t.Scale, new int[] { 0 }, l.Count > 0 ? l[l.Count - 1].Idx : ns); l.Add(e);
        }
        return l;
    }

    public static string Render(MTrack t, string dir)
    {
        rng = new Random(t.Seed); double beat = 60.0 / t.Bpm;
        N = (int)Math.Round(t.Bars * t.Bpb * beat * SR); buf = new double[N];
        Dictionary<char, List<Note>> phrases = new Dictionary<char, List<Note>>(); int ns = t.Scale.Length;
        foreach (char c in t.Form)
        {
            if (c == '-' || phrases.ContainsKey(c)) continue;
            char up = char.ToUpper(c);
            if (c != up && phrases.ContainsKey(up))
            {   // lower case: the same phrase with a closing bar
                List<Note> v = new List<Note>(); double cut = 3 * t.Bpb; int last = ns;
                foreach (Note n in phrases[up]) if (n.Pos < cut - .01) { v.Add(n); last = n.Idx; }
                Note e = new Note(); e.Pos = cut; e.Dur = t.Bpb; e.Idx = Snap(t.Scale, new int[] { 0 }, last); v.Add(e); phrases[c] = v;
            }
            else phrases[c] = Phrase(t, ns + rng.Next(3), c != up);
        }
        for (int bar = 0; bar < t.Bars; bar++)
        {
            int[] ch = t.Prog[bar % t.Prog.Length]; double t0 = bar * t.Bpb * beat, barLen = t.Bpb * beat;
            if (t.Pad > 0) foreach (int c in ch) PadTone(t0, Hz(t.Root - 12 + c), barLen, t.Pad);
            if (t.Pad > 0) PadTone(t0, Hz(t.Root + ch[0]), barLen, t.Pad * .5);
            if (t.Bass > 0) { BassTone(t0, Hz(t.Root - 24 + ch[0]), barLen * .95, t.Bass); if (t.Bpb == 4 && bar % 2 == 1) BassTone(t0 + 2.5 * beat, Hz(t.Root - 24 + ch[2] - 12), beat * 1.4, t.Bass * .6); }
            if (t.Arp != "none")
            {
                int steps = (int)Math.Round(t.Bpb / t.ArpEvery);
                for (int s = 0; s < steps; s++)
                {
                    if (s > 0 && rng.NextDouble() > t.ArpProb) continue;
                    int v = t.ArpPat[(s + (bar % 2) * 0) % t.ArpPat.Length]; int semi = ch[v % ch.Length] + 12 * (v / ch.Length);
                    Voice(t.Arp, t0 + s * t.ArpEvery * beat, t.Root + t.ArpOct + semi, t.ArpEvery * 2, beat, t.ArpVol * (s == 0 ? 1.15 : .85));
                }
            }
            int sec = bar / 4; char f = t.Form[sec % t.Form.Length];
            if (f != '-' && bar % 4 == 0)
                foreach (Note n in phrases[f])
                {
                    if (bar + (int)(n.Pos / t.Bpb) >= t.Bars) continue;
                    Voice(t.Mel, t0 + n.Pos * beat, t.Root + t.MelOct + Semi(t.Scale, n.Idx) - 12, n.Dur, beat, t.MelVol);
                }
        }
        Reverb(t.Wet, beat);
        // soften the top a little, then normalise to -3 dB
        double lp = 0; for (int pass = 0; pass < 2; pass++) for (int i = 0; i < N; i++) { lp += (buf[i] - lp) * .62; if (pass == 1) buf[i] = lp; }
        double peak = 0; for (int i = 0; i < N; i++) peak = Math.Max(peak, Math.Abs(buf[i]));
        double g = peak > 0 ? .7 / peak : 0, sum = 0;
        byte[] pcm = new byte[N * 2];
        for (int i = 0; i < N; i++) { double v = buf[i] * g; sum += v * v; short sv = (short)Math.Round(v * 32767); pcm[i * 2] = (byte)(sv & 255); pcm[i * 2 + 1] = (byte)((sv >> 8) & 255); }
        string path = Path.Combine(dir, t.File + ".wav");
        using (BinaryWriter w = new BinaryWriter(System.IO.File.Create(path)))
        {
            w.Write(new char[] { 'R', 'I', 'F', 'F' }); w.Write(36 + pcm.Length); w.Write(new char[] { 'W', 'A', 'V', 'E' });
            w.Write(new char[] { 'f', 'm', 't', ' ' }); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(SR); w.Write(SR * 2); w.Write((short)2); w.Write((short)16);
            w.Write(new char[] { 'd', 'a', 't', 'a' }); w.Write(pcm.Length); w.Write(pcm);
        }
        double rms = Math.Sqrt(sum / N);
        // the seam: the jump from the last sample to the first, next to the largest jump anywhere (a click would stand out)
        double seam = Math.Abs(buf[0] - buf[N - 1]) * g, big = 0; for (int i = 1; i < N; i++) big = Math.Max(big, Math.Abs(buf[i] - buf[i - 1]) * g);
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0,-26} {1,6:0.0} s  peak {2,5:0.0} dBFS  rms {3,5:0.0} dBFS  seam step {4:0.0000} (largest step {5:0.0000})  {6:0.00} MB",
            t.File + ".wav", N / (double)SR, 20 * Math.Log10(.7), 20 * Math.Log10(Math.Max(rms, 1e-9)), seam, big, (pcm.Length + 44) / 1048576.0);
    }

    // a small hall: an echo on the beat, four damped combs and two all-passes. Run twice around the loop so the tail of the end is under the start.
    static void Reverb(double wet, double beat)
    {
        int[] cd = new int[] { (int)(.0297 * SR), (int)(.0371 * SR), (int)(.0411 * SR), (int)(.0437 * SR) }; double[] fb = new double[] { .82, .8, .79, .77 };
        double[][] cb = new double[4][]; int[] ci = new int[4]; double[] cl = new double[4]; for (int k = 0; k < 4; k++) cb[k] = new double[cd[k]];
        int[] ad = new int[] { (int)(.005 * SR), (int)(.0017 * SR) }; double[][] ab = new double[2][]; int[] ai = new int[2]; for (int k = 0; k < 2; k++) ab[k] = new double[ad[k]];
        int ed = (int)(beat * .75 * SR); double[] eb = new double[ed]; int ei = 0;
        double[] outp = new double[N];
        for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < N; i++)
            {
                double x = buf[i];
                double e = eb[ei]; eb[ei] = x + e * .32; ei = (ei + 1) % ed; double xin = x + e * .22;
                double s = 0;
                for (int k = 0; k < 4; k++) { double y = cb[k][ci[k]]; cl[k] += (y - cl[k]) * .45; cb[k][ci[k]] = xin + cl[k] * fb[k]; ci[k] = (ci[k] + 1) % cd[k]; s += y; }
                s *= .25;
                for (int k = 0; k < 2; k++) { double bo = ab[k][ai[k]]; double y = -s * .5 + bo; ab[k][ai[k]] = s + bo * .5; ai[k] = (ai[k] + 1) % ad[k]; s = y; }
                if (pass == 1) outp[i] = x + e * .22 + s * wet;
            }
        buf = outp;
    }
}
'@

function Chords([string]$kind) {
    switch ($kind) {
        "major"  { return ,@(@(0,4,7), @(-3,0,4), @(5,9,12), @(7,11,14)) }      # I vi IV V
        "major2" { return ,@(@(0,4,7), @(5,9,12), @(-3,0,4), @(7,11,14)) }      # I IV vi V
        "home"   { return ,@(@(0,4,7), @(5,9,12), @(0,4,7), @(7,11,14)) }       # I IV I V
        "minor"  { return ,@(@(0,3,7), @(-4,0,3), @(3,7,10), @(-2,2,5)) }       # i VI III VII
        "dorian" { return ,@(@(0,3,7), @(5,9,12), @(0,3,7), @(-2,2,5)) }        # i IV i VII
        "night"  { return ,@(@(0,3,7), @(-4,0,3), @(0,3,7), @(-5,-2,2)) }       # i VI i v
    }
}
$pentMaj = @(0,2,4,7,9); $pentMin = @(0,3,5,7,10)
function T($file, $seed, $bpm, $bpb, $bars, $root, $scale, $prog, $arp, $mel, $form, $o) {
    $t = New-Object MTrack; $t.File = $file; $t.Seed = $seed; $t.Bpm = $bpm; $t.Bpb = $bpb; $t.Bars = $bars; $t.Root = $root
    $t.Scale = [int[]]$scale; $t.Prog = [int[][]](Chords $prog); $t.Arp = $arp; $t.Mel = $mel; $t.Form = $form
    foreach ($k in $o.Keys) { $t.$k = $o[$k] }
    return $t
}
$tracks = @(
    (T "town_hearthside"      11 72 4 24 62 $pentMaj "major"  "lute" "flute" "AaBaAb" @{ ArpPat = [int[]](0,1,2,3,2,1,2,1); MelProb = .55 }),
    (T "town_market_morning"  23 84 4 24 67 $pentMaj "major2" "harp" "lute"  "AaBbAa" @{ ArpPat = [int[]](0,2,1,3,2,4,3,1); ArpVol = .13; MelVol = .2; MelProb = .7; Pad = .06 }),
    (T "explore_open_road"    37 66 4 24 57 $pentMin "dorian" "harp" "flute" "A-AaBa" @{ ArpEvery = 1.0; ArpPat = [int[]](0,2,3,1); ArpProb = .8; MelProb = .5; Pad = .1; Wet = .36 }),
    (T "explore_far_hills"    41 60 4 20 64 $pentMin "minor"  "lute" "harp"  "AaB-a" @{ ArpEvery = 1.0; ArpPat = [int[]](0,1,3,2); ArpVol = .12; MelVol = .22; MelProb = .55; Pad = .11; Wet = .36 }),
    (T "night_starlit"        53 50 4 16 66 $pentMin "night"  "none" "bell"  "AaBa"   @{ MelProb = .5; MelVol = .2; Pad = .13; Bass = .1; Wet = .42 }),
    (T "night_lantern_watch"  67 54 4 20 62 $pentMin "dorian" "harp" "flute" "-AaBa"  @{ ArpEvery = 2.0; ArpPat = [int[]](0,3); ArpVol = .12; MelOct = 0; MelProb = .42; MelVol = .17; Pad = .12; Bass = .12; Wet = .4 }),
    (T "home_by_the_fire"     79 66 3 32 60 $pentMaj "home"   "lute" "harp"  "AaBaAaBb" @{ ArpEvery = 1.0; ArpPat = [int[]](0,1,2); ArpProb = 1.0; ArpVol = .14; MelVol = .2; MelProb = .6; Pad = .09; Bass = .13 })
)
"music_gen: writing $($tracks.Count) tracks to $out ($([MusicGen]::SR) Hz, 16-bit mono)"
foreach ($t in $tracks) { [MusicGen]::Render($t, $out) }
