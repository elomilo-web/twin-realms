"""Gothic realism particle sprites (white on clear, tinted by Vfx.cs): smoke, flames, embers, dirt, glints, glows, scorch.
Overwrites those files in assets/vfx/particles/ (the Kenney originals are in git history; magic, twirl, circle, slash and
trace sprites are kept). Run from the repo root:
    uv run --no-project --with numpy --with pillow python tools/kit_gen/particles_gen.py
"""
import os
import numpy as np
from PIL import Image

S = 256
root = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
OUT = os.path.join(root, "assets", "vfx", "particles")
a_ = (np.arange(S) + .5) / S * 2 - 1
X, Y = np.meshgrid(a_, -a_)
R = np.hypot(X, Y)


def vnoise(x, y, rs):
    tab = rs.rand(256, 256)
    xi = np.floor(x).astype(int); yi = np.floor(y).astype(int); fx = x - xi; fy = y - yi
    fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy)
    g = lambda i, j: tab[j % 256, i % 256]
    return (g(xi, yi) * (1 - fx) + g(xi + 1, yi) * fx) * (1 - fy) + (g(xi, yi + 1) * (1 - fx) + g(xi + 1, yi + 1) * fx) * fy


def fbm(x, y, seed, octs=5):
    rs = np.random.RandomState(seed); v = 0; a = .5; t = 0
    for o in range(octs):
        v = v + vnoise(x * 2 ** o + 31 * o, y * 2 ** o + 17 * o, rs) * a; t += a; a *= .5
    return v / t


def sm(a, b, x):
    t = np.clip((x - a) / (b - a + 1e-9), 0, 1); return t * t * (3 - 2 * t)


def save(name, alpha, bright=None):
    alpha = np.clip(alpha, 0, 1); rgb = np.clip(bright if bright is not None else np.ones_like(alpha), 0, 1)
    img = np.dstack([rgb, rgb, rgb, alpha]); Image.fromarray((img * 255).astype("uint8"), "RGBA").save(os.path.join(OUT, name + ".png"), optimize=True)
    print(name)


def smoke(seed):
    wx = fbm(X * 2 + 3, Y * 2, seed) - .5; wy = fbm(X * 2, Y * 2 + 7, seed + 1) - .5
    n = fbm((X + wx * .6) * 3.2, (Y + wy * .6) * 3.2, seed + 2)
    edge = 1 - sm(.35, .95, R + (n - .5) * .5)
    a = edge * sm(.32, .72, n) * .85
    return a, .75 + .25 * n


def flame(seed, tall=1.6):
    yy = (Y + .9) / 1.8   # 0 at the base, 1 at the top
    wx = (fbm(X * 3, Y * 2 - seed, seed) - .5) * .35 * yy
    w = .42 * (1 - yy) ** .7 * (.6 + .4 * np.sin(np.pi * np.clip(yy * 1.4, 0, 1)))
    d = np.abs(X - wx) / np.maximum(w, 1e-3)
    n = fbm(X * 4, Y * 3 * tall - seed * .3, seed + 3)
    a = (1 - sm(.55, 1.05, d + (n - .5) * .6)) * sm(0, .08, yy) * (1 - sm(.85, 1.0, yy + (n - .5) * .3))
    core = (1 - sm(.0, .6, d)) * (1 - sm(.2, .8, yy))
    return a * (.75 + .25 * n), .7 + .3 * core


def ember(seed, streak=.18):
    rs = np.random.RandomState(seed); a = np.zeros_like(R)
    for i in range(1 + seed % 3):
        cx, cy = rs.uniform(-.25, .25), rs.uniform(-.25, .25); ang = rs.uniform(0, np.pi)
        u = (X - cx) * np.cos(ang) + (Y - cy) * np.sin(ang); v = -(X - cx) * np.sin(ang) + (Y - cy) * np.cos(ang)
        a = np.maximum(a, np.exp(-(u / streak) ** 2 - (v / .035) ** 2) * (1 if i == 0 else .6))
    a = np.maximum(a, np.exp(-(R / .09) ** 2))
    return a, np.ones_like(a)


def dirt(seed):
    rs = np.random.RandomState(seed); a = np.zeros_like(R)
    for i in range(40):
        cx, cy, r = rs.normal(0, .3), rs.normal(0, .3), rs.uniform(.015, .06)
        n = fbm(X * 12 + i, Y * 12, seed + i, 3)
        a = np.maximum(a, 1 - sm(r * .6, r * 1.2, np.hypot(X - cx, Y - cy) + (n - .5) * r))
    return a * (1 - sm(.6, .95, R)), .55 + .2 * fbm(X * 6, Y * 6, seed)


def glint(rays=4, length=.95, core=.12, seed=0):
    th = np.arctan2(Y, X); a = np.exp(-(R / core) ** 2)
    for k in range(rays):
        ang = k * np.pi / (rays / 2 if rays > 2 else 1) + seed * .3
        u = X * np.cos(ang) + Y * np.sin(ang); v = -X * np.sin(ang) + Y * np.cos(ang)
        a = np.maximum(a, np.exp(-(v / (.012 + .02 * np.exp(-(u / .15) ** 2))) ** 2) * np.exp(-(u / (length * .5)) ** 2) * .9)
    return a, np.ones_like(a)


def glow(size):
    a = np.exp(-(R / size) ** 2); return a, np.ones_like(a)


def scorch(seed):
    n = fbm(X * 3, Y * 3, seed); n2 = fbm(X * 9, Y * 9, seed + 5, 3)
    a = (1 - sm(.45, .95, R + (n - .5) * .45)) * (.55 + .45 * n2)
    return a * .9, .25 + .2 * n2


if __name__ == "__main__":
    for i in range(1, 6):
        save(f"smoke_0{i}", *smoke(10 + i * 7))
    for i, name in enumerate(("fire_01", "fire_02", "flame_01", "flame_02", "flame_03", "flame_04")):
        save(name, *flame(i * 3 + 1, tall=1.4 + .2 * (i % 3)))
    for i in range(1, 5):
        save(f"spark_0{i}", *ember(i * 5, streak=.12 + .05 * i))
    for i in range(1, 4):
        save(f"dirt_0{i}", *dirt(i * 11))
    for name, (rays, ln, core) in {"star_01": (4, .95, .1), "star_02": (6, .8, .09), "star_04": (4, .7, .14), "star_06": (8, .6, .08)}.items():
        save(name, *glint(rays, ln, core))
    for name, size in {"light_01": .55, "light_02": .35, "light_03": .2}.items():
        save(name, *glow(size))
    for i in range(1, 4):
        save(f"scorch_0{i}", *scorch(i * 13))
