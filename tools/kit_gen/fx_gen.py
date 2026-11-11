"""Spell and skill animations as flipbook sprite sheets (4x4 = 16 frames, 256 px each, straight alpha).
Anime look: every effect is a brightness field cut into four flat colour bands (dark edge, colour, bright, white core).
Run from the repo root:  python tools/kit_gen/fx_gen.py   -> assets/vfx/flipbooks/*.png, data/spellfx.json, docs/kit/spellfx.gif"""
import json, math, os, sys
import numpy as np
from PIL import Image

S, SS, N = 256, 2, 16
n_ = S * SS; a_ = (np.arange(n_) + .5) / n_ * 2 - 1
X, Y = np.meshgrid(a_, -a_); R = np.hypot(X, Y); TH = np.arctan2(Y, X)
TAU = 2 * math.pi

def hx(h): return np.array([int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)])
PAL = {k: [hx(c) for c in v.split()] for k, v in {
    'fire': '8a1a08 f25a1e ffb42e fff6c8', 'ice': '1c4f9c 4fb4f0 b8eeff ffffff', 'bolt': '3a2a9c 7a8cff d4e2ff ffffff', 'holy': 'b86a14 ffc83a ffeea0 ffffff',
    'shadow': '1a0828 5a1e9a b46af0 f0d8ff', 'nature': '14522a 3fae52 b4e86a f6ffd8', 'arcane': '4a148c c83ae0 ff9af0 ffffff', 'blood': '4a0a1e c81e46 ff7a8c ffe0e0',
    'wind': '6a8aa8 c8e0f0 f0faff ffffff', 'poison': '2a3a0a 6a9a14 c8e83a f8ffb0', 'slash': '1a6ad8 4fd0ff c8f6ff ffffff', 'earth': '4a3220 9a6e44 d8b888 fff0d8'}.items()}

def sm(a, b, x): t = np.clip((x - a) / (b - a + 1e-9), 0, 1); return t * t * (3 - 2 * t)
def eo(t, p=3): return 1 - (1 - min(max(t, 0), 1)) ** p

_rs = np.random.RandomState(7); _tab = _rs.rand(64, 64)
def vnoise(x, y, per=8):
    x = np.asarray(x, float); y = np.asarray(y, float); xi = np.floor(x).astype(int); yi = np.floor(y).astype(int); fx = x - xi; fy = y - yi
    fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy); g = lambda i, j: _tab[(j % per) % 64, (i % per) % 64]
    return (g(xi, yi) * (1 - fx) + g(xi + 1, yi) * fx) * (1 - fy) + (g(xi, yi + 1) * (1 - fx) + g(xi + 1, yi + 1) * fx) * fy
def fbm(x, y, per=8, octs=3):
    """Tileable noise in 0..1: repeats every `per` units."""
    v = 0; a = .5; tot = 0
    for o in range(octs): v = v + vnoise(x * 2 ** o + 17 * o, y * 2 ** o + 5 * o, per * 2 ** o) * a; tot += a; a *= .5
    return v / tot

def cel(v, pal, cuts=(.08, .38, .68, .9), aa=.035):
    """Brightness field -> RGBA in four flat bands with thin anti-aliased edges."""
    v = np.asarray(v, float); rgb = np.tile(pal[0], v.shape + (1,))
    for c, t in zip(pal[1:], cuts[1:]): k = sm(t - aa, t + aa, v)[..., None]; rgb = rgb * (1 - k) + c * k
    return np.dstack([rgb, sm(cuts[0] - aa, cuts[0] + aa, v)])

def over(a, b):
    """b drawn on top of a (straight alpha)."""
    aa = a[..., 3:]; ba = b[..., 3:]; oa = ba + aa * (1 - ba); rgb = (b[..., :3] * ba + a[..., :3] * aa * (1 - ba)) / np.maximum(oa, 1e-6)
    return np.dstack([np.where(oa > 1e-6, rgb, b[..., :3]), oa])

def spikes(n, length, width, r0=0.0, seed=1, rot=0.0, jitter=.5, th=TH, r=R):
    """Sharp radial spikes. length/width are scalars or per-spike arrays; r0 = where each spike starts."""
    rs = np.random.RandomState(seed); L = np.broadcast_to(np.asarray(length, float) * (1 - jitter * rs.rand(n)), (n,)); step = TAU / n
    k = np.round((th - rot) / step).astype(int); d = (th - rot) - k * step; Lk = L[k % n]; rr = (r - r0) / np.maximum(Lk, 1e-6)
    w = width * (1 - rr); v = 1 - np.abs(d * r) / np.maximum(w, 1e-6)
    return np.where((rr > 0) & (rr < 1), np.clip(v, 0, 1), 0)

def star4(x, y, size, sharp=.5): return 1 - (np.abs(x / size) ** sharp + np.abs(y / size) ** sharp) ** (1 / sharp)

# ---------------------------------------------------------------- effects: f(t) with t in [0,1)
def explosion(pal, hollow=1.0, rough=.7):
    def f(t):
        rad = .2 + .7 * eo(t, 3); n = fbm(X * 3 + 9, Y * 3 - t * 2, 8); na = fbm(TH / TAU * 16, R * 3 - t * 3, 16)
        v = (1 - R / rad) * 1.6 + (n - .5) * rough + (na - .5) * .5
        v -= np.clip(t * 1.5 - R / rad * .7 - n * .6, 0, 1) * 2.2 * hollow          # burns out from the middle
        v -= sm(.55, 1, t) * 1.2
        v = np.maximum(v, (1 - R / (.55 * (1 - t * 5))) * 3 if t < .19 else -1)      # opening flash
        return cel(v, pal)
    return f

def orb(pal, tail=0.0):
    def f(t):
        n = fbm(TH / TAU * 8 + t * 8, R * 4 - t * 8, 8); n2 = fbm(X * 4, Y * 4 + t * 8, 8)
        yy = Y + tail * .25; rr = np.hypot(X * (1 + tail * .6), np.where(yy > 0, yy, yy * (1 - tail * .55)))
        v = 1.25 - rr / .5 + (n - .5) * .7 * sm(.15, .5, rr) + (n2 - .5) * .3
        return cel(v, pal, (.1, .4, .72, .95))
    return f

def nova(pal, n=14, seed=3):
    def f(t):
        g = eo(t / .45, 3); v = spikes(n, .95 * g, .16, .08, seed, .1) * 1.3; v = np.maximum(v, spikes(n, .6 * g, .22, .0, seed + 1, .1 + math.pi / n) * 1.1)
        v = np.maximum(v, 1 - R / (.3 * g + .02)); er = fbm(X * 5, Y * 5, 8); v = v - sm(.5, 1, t) * (er * 1.6 + .2) - sm(.85, 1, t)
        ring = 1 - np.abs(R - (.15 + .8 * eo(t, 2))) / (.05 * (1 - t) + .005); v = np.maximum(v, ring * (1 - t))
        return cel(v, pal)
    return f

def shards(pal, n=9, seed=5):
    def f(t):
        r0 = .08 + .75 * eo(t, 2.5); v = spikes(n, .32 * (1 - t * .7), .11 * (1 - t), r0 * (.5 + .5 * np.random.RandomState(seed).rand()), seed, .3, .6)
        v = np.maximum(v, spikes(n, .2 * (1 - t), .08 * (1 - t), r0 * .55, seed + 2, .3 + .35, .5)); v = np.maximum(v, (1 - R / (.42 * (1 - t * 3.5))) * 2 if t < .28 else 0)
        return cel(v * 1.25, pal)
    return f

def hit_spark(pal):
    def f(t):
        s = (.25 + .7 * eo(t / .3, 3)) * (1 - sm(.45, 1, t)); c, s_ = math.cos(.4), math.sin(.4); xr, yr = X * c - Y * s_, X * s_ + Y * c
        v = star4(xr, yr, s + .001, .55) * 1.6; v = np.maximum(v, star4(X, Y, s * .6 + .001, .6) * 1.3)
        v = np.maximum(v, spikes(12, .5 * (1 - t), .035, .25 + .5 * eo(t, 2), 4, .2, .6) * (1 - t) * 1.4)
        return cel(v, pal)
    return f

def bolt(pal, forks=2):
    def f(t):
        k = int(t * N) // 2; rs = np.random.RandomState(40 + k); yn = (Y + 1) / 2; env = np.sin(np.pi * yn) ** .35
        def path(amp, sd):
            r2 = np.random.RandomState(sd); return sum(np.sin(yn * TAU * fq * .5 + r2.rand() * TAU) * amp / fq for fq in (2, 3, 5, 9)) * env
        w = .05 * (1 - .5 * t) + .012; v = 1 - np.abs(X - path(.34, 40 + k)) / w
        for j in range(forks):
            y0 = rs.rand() * .8 - .4; sgn = 1 if j % 2 else -1; live = sm(y0, y0 + .02, Y) * (1 - sm(y0 + .45, y0 + .6, Y))
            v = np.maximum(v, (1 - np.abs(X - path(.34, 40 + k) - sgn * (Y - y0) * .7) / (w * .6)) * live)
        return cel(v * (1.25 - t * .9), pal, (.1, .45, .75, .93))
    return f

def ribbon(pal):
    def f(t):
        yn = (Y + 1) / 2; v = 0
        for ph, amp, w in ((0, .22, .1), (.5, .16, .06)):
            xc = amp * np.sin(TAU * (yn * 2 - t + ph)); v = np.maximum(v, 1 - np.abs(X - xc) / (w * (.7 + .3 * np.sin(TAU * (yn * 3 + t)))))
        dots = fbm(X * 6, yn * 8 - t * 8, 8); v = np.maximum(v, (dots - .72) * 6 * (np.abs(X) < .4))
        return cel(v * 1.15, pal)
    return f

def heal(pal):
    def f(t):
        v = np.zeros_like(X); rs = np.random.RandomState(11)
        for i in range(9):
            x0 = rs.rand() * 1.3 - .65; y0 = rs.rand() * .9 - .8; ph = rs.rand() * .35; sz = .07 + rs.rand() * .07; tt = (t - ph) / (1 - ph)
            if tt <= 0: continue
            yy = Y - (y0 + tt * .9); xx = X - x0; s = sz * math.sin(math.pi * min(tt, 1)) ** .6 + 1e-4
            plus = 1 - np.minimum(np.maximum(np.abs(xx) / s, np.abs(yy) / (s * .32)), np.maximum(np.abs(xx) / (s * .32), np.abs(yy) / s)); v = np.maximum(v, plus * 2.2)
        ey = (Y + .72) / .3; ring = 1 - np.abs(np.hypot(X, ey) - (.25 + .7 * eo(t, 2))) / .07; v = np.maximum(v, ring * (1 - t) * 1.5)
        col = (1 - np.abs(X) / (.5 * (1 - t) + .05)) * sm(-.9, -.5, Y) * (1 - sm(.0, .9, Y)) * (1 - t) * .5; v = np.maximum(v, col)
        return cel(v, pal, (.12, .4, .7, .92))
    return f

def slash(pal, arc=160, thick=.2, radius=.62):
    def f(t):
        a0 = math.radians(90 + arc / 2); u = (a0 - np.mod(TH - a0 + math.pi, TAU) - (a0 - math.pi)) ; u = (a0 - TH) % TAU / math.radians(arc)   # 0 at the start of the swing, 1 at the end
        ins = (u >= 0) & (u <= 1); prof = np.sin(np.pi * np.clip(u, 0, 1)) ** .6; th_ = thick * prof + 1e-4
        d = R - (radius - th_ * .25); v = np.where(d > 0, 1 - d / (th_ * .45), 1 + d / (th_ * 1.1))      # sharp outer edge, long soft inner edge
        streak = fbm(u * 6, R * 22, 8, 2); v = v * 1.35 - (streak - .35) * .9 * sm(0, 1, -d / th_ + .2)
        head = eo(t / .3, 2) * 1.05; tail = sm(.25, .95, t) * 1.15; v = np.where(ins & (u < head) & (u > tail - .05), v, -1)
        v = v - sm(.5, 1, t) * 1.0 - sm(tail, tail + .25, u) * 0 + sm(head - .12, head, u) * .0
        return cel(v, pal, (.1, .36, .6, .82))
    return f

def thrust(pal):
    def f(t):
        c = -.7 + 1.5 * eo(t / .5, 3); L = .75 * (1 - sm(.5, 1, t)) + .02; xx = (X - c + L * .5) / L; h = .3 * np.clip(xx, 0, 1) ** 1.6 * (1 - sm(.92, 1.0, xx)) * (1 - sm(.6, 1, t))
        v = np.where((xx > 0) & (xx < 1), 1 - np.abs(Y) / (h + 1e-4), -1) * 1.4
        for i, yo in enumerate((-.3, .22, -.12, .36)):
            xs = -.9 + ((t * 2.2 + i * .23) % 1) * 1.6; v = np.maximum(v, (1 - np.abs(Y - yo) / .012) * sm(xs - .35, xs, X) * (1 - sm(xs, xs + .02, X)) * (1 - t))
        v = np.maximum(v, star4(X - c, Y, .3 * (1 - sm(.3, .8, t)) + .001, .5) * 1.5)
        return cel(v, pal)
    return f

def shockwave(pal, dusty=1.0):
    def f(t):
        rad = .1 + .82 * eo(t, 2.5); w = .16 * (1 - t) + .015; na = fbm(TH / TAU * 16, R * 2 + t * 2, 16)
        v = (1 - np.abs(R - rad) / w) * 1.4 + (na - .5) * dusty * 1.2 * sm(rad - w * 2, rad, R) * (R < rad + w)
        v = np.maximum(v, (1 - np.abs(R - rad * .6) / (w * .5)) * (1 - t) * .9); v = np.maximum(v, spikes(16, .22 * (1 - t), .05, rad, 8, 0, .7) * (1 - t) * dusty)
        return cel(v - sm(.7, 1, t), pal)
    return f

def line(d, w): return 1 - np.abs(d) / w
def seg(px, py, ax, ay, bx, by, w):
    vx, vy = bx - ax, by - ay; h = np.clip(((px - ax) * vx + (py - ay) * vy) / (vx * vx + vy * vy), 0, 1); return 1 - np.hypot(px - ax - vx * h, py - ay - vy * h) / w

def circle(pal, points=6, seed=21):
    rs = np.random.RandomState(seed); glyphs = [[tuple(rs.rand(4) * [.7, .7, .7, .7] + .15) for _ in range(3 + rs.randint(2))] for _ in range(12)]
    def f(t):
        w = .012; a1 = TH - t * TAU / 12; a2 = TH + t * TAU / points
        v = np.maximum(line(R - .93, w), line(R - .86, w * .7)); v = np.maximum(v, line(R - .58, w)); v = np.maximum(v, line(R - .26, w * .8))
        tick = np.abs(((a1 / (TAU / 36)) % 1) - .5) * (TAU / 36) * R; v = np.maximum(v, np.where((R > .86) & (R < .93), line(tick, w * .6), -1))
        cell = (a1 / (TAU / 12)) % 12; ci = np.floor(cell).astype(int); u = cell - ci; vv = (R - .62) / .2      # rune band: 12 cells of made-up glyphs
        for gi, g in enumerate(glyphs):
            m = (ci == gi) & (vv > 0) & (vv < 1)
            if not m.any(): continue
            d = np.full(X.shape, -1.0)
            for ax, ay, bx, by in g: d = np.maximum(d, seg(u, vv, ax, ay, bx, by, .09))
            v = np.maximum(v, np.where(m, d, -1))
        px, py = R * np.cos(a2), R * np.sin(a2)                                                              # star polygon turning the other way
        pts = [(math.cos(TAU * i / points) * .58, math.sin(TAU * i / points) * .58) for i in range(points)]; step = 2 if points % 2 == 0 else 2
        for i in range(points): a, b = pts[i], pts[(i + step) % points]; v = np.maximum(v, seg(px, py, a[0], a[1], b[0], b[1], w))
        for i in range(points): v = np.maximum(v, line(np.hypot(px - pts[i][0], py - pts[i][1]) - .05, w * .8))
        glow = np.maximum(v, 0) ; halo = np.maximum.reduce([1 - np.abs(R - rr) / .05 for rr in (.93, .58, .26)]) * .34
        pulse = .85 + .15 * math.sin(TAU * t); return cel(np.maximum(v * 1.2, halo) * pulse, pal, (.12, .3, .62, .9))
    return f

def pillar(pal):
    def f(t):
        w = (.05 + .3 * eo(t / .25, 3)) * (1 - sm(.4, 1, t)) + .004; st = fbm(X * 10, Y * 1.5 - t * 6, 8, 2)
        v = (1 - np.abs(X) / (w * (1 + .25 * (st - .5)))) * 1.5 * sm(-.95, -.8, Y)
        ey = (Y + .8) / .28; v = np.maximum(v, (1 - np.hypot(X, ey) / (.2 + .65 * eo(t, 2))) * 1.6 * (1 - t)); v = np.maximum(v, (1 - np.abs(np.hypot(X, ey) - (.2 + .7 * eo(t, 2))) / .05) * (1 - t) * 1.3)
        v = np.maximum(v, spikes(10, .5 * (1 - t), .05, .15 + .3 * t, 9, 0, .5, np.arctan2(Y + .8, X), np.hypot(X, Y + .8)) * (Y > -.8) * (1 - t))
        return cel(v, pal)
    return f

def cloud(pal, bubbles=True):
    def f(t):
        v = np.zeros_like(X); rs = np.random.RandomState(31)
        for i in range(9):
            a = rs.rand() * TAU; rr = rs.rand() * .42; s = .2 + rs.rand() * .16; ph = rs.rand() * TAU; dirn = 1 if i % 2 else -1
            cx = math.cos(a) * rr + .07 * math.cos(TAU * t * dirn + ph); cy = math.sin(a) * rr * .8 + .07 * math.sin(TAU * t * dirn + ph); v += np.exp(-((X - cx) ** 2 + (Y - cy) ** 2) / (s * s * .5))
        v = v * .8 - .1 + (fbm(X * 4 + math.cos(TAU * t) * .3, Y * 4 + math.sin(TAU * t) * .3, 8) - .5) * .25
        if bubbles:
            for i in range(6):
                tt = (t + i / 6) % 1; bx = rs.rand() * 1.1 - .55; by = -.4 + tt * .9 + rs.rand() * .2; br = .03 + .04 * tt
                v = np.maximum(v, (1 - np.abs(np.hypot(X - bx, Y - by) - br) / .014) * (1 - tt) * 1.3)
        return cel(v, pal, (.2, .42, .72, .97))
    return f

def swirl(pal, arms=3):
    def f(t):
        s = np.sin(arms * TH + R * 7 - TAU * t); band = sm(.25, .45, R) * (1 - sm(.8, .97, R)); v = sm(.35, 1, s) * band * 1.3
        s2 = np.sin(arms * TH + R * 7 - TAU * t + 1.4); v = np.maximum(v, sm(.8, 1, s2) * band * .7); return cel(v, pal, (.15, .45, .8, .97))
    return f

def aura(pal):
    def f(t):
        v = np.zeros_like(X); rs = np.random.RandomState(51); yn = (Y + 1) / 2
        for i in range(11):
            x0 = rs.rand() * 1.3 - .65; ph = rs.rand(); sp = 1 + rs.randint(2); ln = .25 + rs.rand() * .25; w = .012 + rs.rand() * .014
            u = (yn - t * sp - ph) % 1; dash = sm(0, ln * .8, u) * (1 - sm(ln * .8, ln, u)); v = np.maximum(v, (1 - np.abs(X - x0) / w) * dash * 1.4)
        v = v * sm(0, .15, yn) * (1 - sm(.7, 1, yn)); ey = (Y + .8) / .25; v = np.maximum(v, (1 - np.abs(np.hypot(X, ey) - .7) / .04) * (.8 + .2 * math.sin(TAU * t)))
        return cel(v, pal, (.12, .4, .7, .95))
    return f

def puff(pal):
    def f(t):
        v = np.zeros_like(X); rs = np.random.RandomState(61)
        for i in range(10):
            a = rs.rand() * TAU; d = (.1 + rs.rand() * .5) * eo(t, 2.5); s = (.16 + rs.rand() * .14) * (1 - t * .75)
            v += np.exp(-((X - math.cos(a) * d) ** 2 + (Y - math.sin(a) * d * .8) ** 2) / (s * s * .6 + 1e-5))
        return cel(v * .8 - .1 - t * .25, pal, (.22, .45, .75, .98))
    return f

def gather(pal):
    def f(t):
        r0 = .95 * (1 - eo(t, 1.6)); v = spikes(14, .3 * (1 - t) + .03, .04, r0, 71, .2, .6) * 1.4; v = np.maximum(v, spikes(10, .2 * (1 - t) + .02, .03, r0 * .6 + .05, 72, .5, .5))
        v = np.maximum(v, (1 - R / (.06 + .28 * t ** 2)) * 1.6); v = np.maximum(v, (1 - np.abs(R - (.9 * (1 - t))) / .02) * .8 * t); return cel(v, pal)
    return f

def sparkle(pal):
    def f(t):
        v = np.zeros_like(X); rs = np.random.RandomState(81)
        for i in range(8):
            x0, y0 = rs.rand(2) * 1.5 - .75; ph = rs.rand(); s = (.1 + rs.rand() * .14) * max(math.sin(math.pi * ((t + ph) % 1)), 0) ** 1.5 + 1e-4; v = np.maximum(v, star4(X - x0, Y - y0, s, .5) * 2)
        return cel(v, pal, (.1, .3, .55, .8))
    return f

def roots(pal):
    def f(t):
        g = eo(t / .5, 2.5); v = np.zeros_like(X); rs = np.random.RandomState(91)
        for i in range(7):
            x0 = -.7 + i * .23 + rs.rand() * .06; h = (.6 + rs.rand() * .7) * g; lean = (rs.rand() - .5) * .5; yy = (Y + .85) / max(h, 1e-3)
            xc = x0 + lean * yy * h + .05 * np.sin(yy * 7 + i); w = .075 * (1 - yy) ** .7; v = np.maximum(v, np.where((yy > 0) & (yy < 1), 1 - np.abs(X - xc) / (w + 1e-4), -1))
        v = v * 1.3 - sm(.7, 1, t) * (fbm(X * 5, Y * 5, 8) * 1.5 + .3); return cel(v, pal)
    return f

# name: (function, loop?, kind, size in metres, fps)   kind: billboard | ground | strip
FX = {
    'fire_burst': (explosion(PAL['fire']), 0, 'billboard', 3.0, 24), 'fire_orb': (orb(PAL['fire'], 1.0), 1, 'billboard', 1.0, 20), 'meteor_blast': (explosion(PAL['fire'], .6, 1.0), 0, 'billboard', 7.0, 20),
    'frost_nova': (nova(PAL['ice']), 0, 'ground', 6.0, 24), 'ice_shatter': (shards(PAL['ice']), 0, 'billboard', 2.2, 28), 'ice_orb': (orb(PAL['ice'], .6), 1, 'billboard', .8, 20),
    'lightning_bolt': (bolt(PAL['bolt']), 0, 'strip', 1.2, 30), 'lightning_hit': (hit_spark(PAL['bolt']), 0, 'billboard', 2.0, 30), 'heal_rise': (heal(PAL['nature']), 0, 'billboard', 2.6, 20),
    'shadow_orb': (orb(PAL['shadow'], .8), 1, 'billboard', .9, 20), 'shadow_burst': (explosion(PAL['shadow'], 1.3, .9), 0, 'billboard', 2.6, 24), 'drain_beam': (ribbon(PAL['blood']), 1, 'strip', .9, 20),
    'slash_arc': (slash(PAL['slash']), 0, 'billboard', 3.2, 36), 'slash_heavy': (slash(PAL['holy'], 250, .26, .6), 0, 'billboard', 4.2, 30), 'slash_shadow': (slash(PAL['shadow'], 140, .18), 0, 'billboard', 3.0, 36),
    'thrust_streak': (thrust(PAL['slash']), 0, 'billboard', 3.6, 36), 'hit_spark': (hit_spark(PAL['holy']), 0, 'billboard', 1.6, 32), 'shockwave': (shockwave(PAL['earth']), 0, 'ground', 7.0, 24),
    'shockwave_gold': (shockwave(PAL['holy'], .3), 0, 'ground', 8.0, 22), 'circle_arcane': (circle(PAL['arcane'], 6), 1, 'ground', 5.0, 12), 'circle_holy': (circle(PAL['holy'], 8, 5), 1, 'ground', 6.0, 12),
    'circle_hex': (circle(PAL['poison'], 5, 9), 1, 'ground', 6.0, 12), 'smite_pillar': (pillar(PAL['holy']), 0, 'billboard', 6.0, 24), 'poison_cloud': (cloud(PAL['poison']), 1, 'billboard', 3.0, 12),
    'whirlwind': (swirl(PAL['wind']), 1, 'ground', 5.0, 24), 'buff_aura': (aura(PAL['holy']), 1, 'billboard', 2.4, 16), 'poison_aura': (aura(PAL['poison']), 1, 'billboard', 2.4, 16),
    'smoke_puff': (puff(PAL['wind']), 0, 'billboard', 2.2, 24), 'shadow_puff': (puff(PAL['shadow']), 0, 'billboard', 2.4, 24), 'cast_gather': (gather(PAL['arcane']), 0, 'billboard', 2.0, 28),
    'cast_gather_fire': (gather(PAL['fire']), 0, 'billboard', 2.0, 28), 'sparkle': (sparkle(PAL['holy']), 1, 'billboard', 1.4, 14), 'thorn_roots': (roots(PAL['nature']), 0, 'billboard', 3.4, 20),
    'arcane_burst': (nova(PAL['arcane'], 10, 12), 0, 'billboard', 2.6, 28), 'earth_spikes': (roots(PAL['earth']), 0, 'billboard', 3.4, 24), 'thorn_hit': (shards(PAL['nature'], 7, 15), 0, 'billboard', 1.6, 28),
}
# which animations each game skill uses (ids from data/skills.json): cast = at the hero, travel = on the projectile, hit = on impact, ground = lying on the floor
SKILLS = {
    'fire': dict(cast='cast_gather_fire', travel='fire_orb', hit='fire_burst'), 'nova': dict(ground='frost_nova', hit='ice_shatter'), 'heal': dict(cast='heal_rise'),
    'zap': dict(beam='lightning_bolt', hit='lightning_hit'), 'shadowbolt': dict(cast='cast_gather', travel='shadow_orb', hit='shadow_burst'), 'drain': dict(beam='drain_beam', hit='shadow_puff'),
    'hex': dict(ground='circle_hex', hit='poison_cloud'), 'meteor': dict(ground='circle_arcane', hit='meteor_blast', after='shockwave'), 'icelance': dict(travel='ice_orb', hit='ice_shatter'),
    'thornvolley': dict(hit='thorn_hit'), 'entangle': dict(ground='circle_hex', hit='thorn_roots'), 'consecrate': dict(ground='circle_holy', cast='buff_aura'), 'smite': dict(ground='circle_holy', hit='smite_pillar'),
    'cleave': dict(cast='slash_heavy', hit='hit_spark'), 'whirlwind': dict(ground='whirlwind', hit='hit_spark'), 'leap': dict(hit='shockwave', cast='smoke_puff'), 'lunge': dict(cast='thrust_streak', hit='hit_spark'),
    'shieldbash': dict(hit='hit_spark', after='shockwave_gold'), 'warcry': dict(ground='shockwave_gold', cast='buff_aura'), 'groundslam': dict(hit='earth_spikes', ground='shockwave'),
    'shadowstep': dict(cast='shadow_puff', hit='slash_shadow'), 'poisonblade': dict(cast='poison_aura'), 'fan': dict(hit='hit_spark'), 'arcanestrike': dict(cast='slash_arc', hit='arcane_burst'),
    'volley': dict(hit='hit_spark'), 'rain': dict(ground='circle_arcane', hit='hit_spark'), '_melee_combo': dict(cast='slash_arc', hit='hit_spark', finisher='slash_heavy'), '_pickup': dict(cast='sparkle'),
}

def frames(name):
    f = FX[name][0]; out = []
    for i in range(N):
        a = np.clip(f(i / N), 0, 1); im = Image.fromarray((a * 255).astype('uint8'), 'RGBA').resize((S, S), Image.LANCZOS); out.append(im)
    return out

if __name__ == '__main__':
    root = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..')); od = os.path.join(root, 'assets/vfx/flipbooks'); os.makedirs(od, exist_ok=True)
    names = [n for n in FX if not sys.argv[1:] or n in sys.argv[1:]]; allf = {}
    for n in names:
        fr = frames(n); allf[n] = fr; sh = Image.new('RGBA', (S * 4, S * 4), (0, 0, 0, 0))
        for i, im in enumerate(fr): sh.paste(im, ((i % 4) * S, (i // 4) * S))
        sh.save(os.path.join(od, n + '.png'), optimize=True); print(n, flush=True)
    if not sys.argv[1:]:
        json.dump({"_help": "Spell and skill animations (tools/kit_gen/fx_gen.py). Sheets are res://assets/vfx/flipbooks/<name>.png, 4x4 frames. kind: billboard (faces the camera), ground (lies flat on the floor), strip (stretched between two points, tiles along its length). size = metres across. skills = which animations each skill in data/skills.json should play; played by scripts/SpellFx.cs.",
                   "fx": {n: {"frames": N, "fps": v[4], "loop": bool(v[1]), "kind": v[2], "size": v[3]} for n, v in FX.items()}, "skills": SKILLS}, open(os.path.join(root, 'data/spellfx.json'), 'w'), indent=1)
    # previews on a dark background: an animated grid and a still sheet of 4 frames per effect
    cols = 6; T = 128; rows = (len(names) + cols - 1) // cols; bg = (26, 24, 40)
    from PIL import ImageDraw
    gif = []
    for i in range(N):
        im = Image.new('RGB', (cols * T, rows * (T + 14)), bg); dr = ImageDraw.Draw(im)
        for k, n in enumerate(names):
            fr = allf[n][i].resize((T, T), Image.LANCZOS); x, y = (k % cols) * T, (k // cols) * (T + 14); im.paste(fr, (x, y), fr); dr.text((x + 4, y + T), n, fill=(200, 200, 220))
        gif.append(im)
    tag = '' if not sys.argv[1:] else '_part'
    gif[0].save(os.path.join(root, 'docs/kit', f'spellfx{tag}.gif'), save_all=True, append_images=gif[1:], duration=70, loop=0)
    still = Image.new('RGB', (4 * 2 * 150, ((len(names) + 1) // 2) * 164), bg); dr = ImageDraw.Draw(still)
    for k, n in enumerate(names):
        for j, fi in enumerate((1, 5, 9, 13)):
            fr = allf[n][fi].resize((150, 150), Image.LANCZOS); x, y = (k % 2) * 600 + j * 150, (k // 2) * 164; still.paste(fr, (x, y), fr)
        dr.text(((k % 2) * 600 + 4, (k // 2) * 164 + 150), n, fill=(200, 200, 220))
    still.save(os.path.join(root, 'docs/kit', f'spellfx_frames{tag}.png'))
