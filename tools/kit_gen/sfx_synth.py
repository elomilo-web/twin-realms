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


def hover(rng, i):   # 25 ms soft tick: filtered noise + faint metallic ping
    t = t_arr(0.025)
    n = bandpass(rng.standard_normal(t.size), 1200 + 300 * i, 5000) * np.exp(-t * 150)
    ping = np.sin(2 * np.pi * (2400 + 90 * i) * t) * np.exp(-t * 110)
    return n * 0.6 + ping * 0.35


def click(rng, i):   # 60 ms heavier wood/metal clack
    t = t_arr(0.06)
    wood = np.sin(2 * np.pi * (180 + 40 * i) * t * (1 + 0.8 * np.exp(-t * 60))) * np.exp(-t * 55)
    tick = bandpass(rng.standard_normal(t.size), 600, 4000) * np.exp(-t * 120)
    metal = (np.sin(2 * np.pi * (1650 + 200 * i) * t) + 0.5 * np.sin(2 * np.pi * 2750 * t)) * np.exp(-t * 90) * 0.25
    return wood * 0.9 + tick * 0.7 + metal


def ui_open(rng):   # low whoosh + page/cloth rustle, 300 ms
    t = t_arr(0.3)
    env = np.sin(np.pi * np.clip(t / 0.3, 0, 1) ** 0.7) ** 2
    whoosh = lowpass(rng.standard_normal(t.size), 420) * env * 3
    rustle = bandpass(rng.standard_normal(t.size), 1500, 6000) * env * (0.4 + 0.6 * (rng.random(t.size) > 0.6)) * 0.35
    return whoosh + rustle * (t > 0.04)


def ui_close(rng):   # soft thump with a short reversed-swell lead-in, 200 ms
    t = t_arr(0.2)
    swell = lowpass(rng.standard_normal(t.size), 500) * (t < 0.08) * (t / 0.08) ** 2 * 1.5
    k = np.clip(t - 0.08, 0, None)
    thump = np.sin(2 * np.pi * 85 * (1 + 0.5 * np.exp(-k * 40)) * k) * np.exp(-k * 28) * (t >= 0.08)
    return swell * 0.5 + thump


def ui_confirm():   # warm bell, two partials, 600 ms
    t = t_arr(0.6)
    return (np.sin(2 * np.pi * 392 * t) * np.exp(-t * 6) + 0.55 * np.sin(2 * np.pi * 587.3 * t) * np.exp(-t * 7.5)
            + 0.12 * np.sin(2 * np.pi * 784 * t) * np.exp(-t * 12)) * (1 - np.exp(-t * 900))


def ui_error(rng):   # dull low thud, 150 ms
    t = t_arr(0.15)
    return np.sin(2 * np.pi * 70 * (1 + 0.4 * np.exp(-t * 50)) * t) * np.exp(-t * 26) + lowpass(rng.standard_normal(t.size), 300) * np.exp(-t * 60) * 0.5


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    rng = np.random.default_rng(1234)
    ur = np.random.default_rng(777)   # separate stream so the older sounds stay byte-identical
    for i in range(3): save(f"ui_hover_{i + 1}", hover(ur, i), 0.25)
    for i in range(2): save(f"ui_click_{i + 1}", click(ur, i), 0.55)
    save("ui_open", ui_open(ur), 0.45); save("ui_close", ui_close(ur), 0.45)
    save("ui_confirm", ui_confirm(), 0.5); save("ui_error", ui_error(ur), 0.5)
    for i in range(3): save(f"land_thud_{i + 1}", thud(rng, i), 0.9)
    for i in range(4): save(f"splash_{i + 1}", splash(rng, i), 0.75)
    for i in range(4): save(f"step_water_{i + 1}", wade(rng, i), 0.5)


if __name__ == "__main__":
    main()
