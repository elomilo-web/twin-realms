"""Procedural SFX: land_thud_1..3, splash_1..4, step_water_1..4 -> assets/audio/sfx (44.1 kHz 16-bit mono WAV).
Run: uv run --with numpy python tools/kit_gen/sfx_synth.py
"""
import wave
from pathlib import Path
import numpy as np

SR = 44100
OUT = Path(__file__).resolve().parents[2] / "assets" / "audio" / "sfx"


def t_arr(dur):
    return np.arange(int(dur * SR)) / SR


def lowpass(x, fc):
    a = 1 - np.exp(-2 * np.pi * fc / SR)
    y = np.zeros_like(x); s = 0.0
    for i, v in enumerate(x):
        s += a * (v - s); y[i] = s
    return y


def bandpass(x, lo, hi):
    return lowpass(x, hi) - lowpass(x, lo)


def fade(x, ms=4):
    n = int(ms * SR / 1000); x = x.copy(); x[-n:] *= np.linspace(1, 0, n); x[:2] *= 0.5
    return x


def save(name, x, peak=0.8):
    x = fade(x); x = x / (np.abs(x).max() + 1e-9) * peak
    with wave.open(str(OUT / f"{name}.wav"), "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes((x * 32767).astype("<i2").tobytes())
    print(f"{name}.wav  rms={np.sqrt(np.mean(x ** 2)):.3f}")


def thud(rng, i):
    t = t_arr(0.35)
    f = (60 + 25 * i + rng.uniform(-6, 6)) * (1 + 1.2 * np.exp(-t * 28))   # fast pitch drop to ~60-120 Hz
    body = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t * 14)
    click = lowpass(rng.standard_normal(t.size), 1500 + 400 * i) * np.exp(-t * 90)
    rustle = bandpass(rng.standard_normal(t.size), 400, 2500) * np.exp(-t * 11) * (t > 0.03)
    rustle *= 0.5 + 0.5 * (rng.random(t.size) > 0.7)   # gravelly crackle
    return body * 1.0 + click * 0.9 + rustle * 0.35


def splash(rng, i):
    dur = rng.uniform(0.4, 0.6); t = t_arr(dur)
    x = np.zeros(t.size)
    n = rng.standard_normal(t.size)
    x += bandpass(n, 300 + 100 * i, 3000 + 600 * i) * (1 - np.exp(-t * 200)) * np.exp(-t * 7) * 0.9   # main wash
    for _ in range(int(rng.integers(10, 18))):   # droplet clicks
        s = int(rng.uniform(0.02, dur * 0.8) * SR); L = int(0.012 * SR)
        k = np.arange(L) / SR; f0 = rng.uniform(1500, 4500)
        d = np.sin(2 * np.pi * f0 * k) * np.exp(-k * 300) * rng.uniform(0.15, 0.4) * np.exp(-s / SR * 3)
        x[s:s + L] += d[:max(0, min(L, t.size - s))]
    for _ in range(int(rng.integers(4, 8))):   # bubbly tail: rising chirps
        s = int(rng.uniform(0.12, dur * 0.9) * SR); L = int(0.05 * SR)
        k = np.arange(L) / SR; f = rng.uniform(300, 700) * (1 + 6 * k * 4)
        d = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-k * 70) * 0.18
        x[s:s + L] += d[:max(0, min(L, t.size - s))]
    return x


def wade(rng, i):
    t = t_arr(0.25)
    n = bandpass(rng.standard_normal(t.size), 250 + 60 * i, 2200 + 300 * i)
    x = n * (1 - np.exp(-t * 300)) * np.exp(-t * 14)
    for _ in range(3):
        s = int(rng.uniform(0.01, 0.12) * SR); L = int(0.01 * SR); k = np.arange(L) / SR
        x[s:s + L] += np.sin(2 * np.pi * rng.uniform(1500, 3500) * k) * np.exp(-k * 350) * 0.2
    return x


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    rng = np.random.default_rng(1234)
    for i in range(3): save(f"land_thud_{i + 1}", thud(rng, i), 0.9)
    for i in range(4): save(f"splash_{i + 1}", splash(rng, i), 0.75)
    for i in range(4): save(f"step_water_{i + 1}", wade(rng, i), 0.5)


if __name__ == "__main__":
    main()
