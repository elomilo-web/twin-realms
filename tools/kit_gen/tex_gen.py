"""Painted texture pack for Twin Realms: soft anime-fantasy ground and building textures, all seamless (they tile).
Every texture is made from code, so the whole pack shares one palette and one brush feel.
Writes  assets/textures/terrain_painted/<layer>_{col,nrm,ao}.jpg   (the six ground layers World.cs blends, plus extras)
        assets/textures/painted/<name>_{col,nrm}.jpg               (building and dungeon surfaces)
        docs/kit/textures.png                                      (preview, each texture tiled 2 x 2)
Run from the repo root:  python tools/kit_gen/tex_gen.py"""
import math, os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

S = 1024
ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
def hexc(h): return np.array([int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)])
def sm(a, b, x): t = np.clip((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t)

def fnoise(seed, beta=2.0, stretch=(1, 1), n=S):
    """Seamless noise, 0..1. beta: higher = bigger soft shapes. stretch: (x, y) > 1 smears along that axis (brush strokes, wood grain)."""
    r = np.random.RandomState(seed); f = np.fft.fft2(r.randn(n, n))
    fy = np.fft.fftfreq(n)[:, None] * stretch[1]; fx = np.fft.fftfreq(n)[None, :] * stretch[0]
    k = np.sqrt(fx * fx + fy * fy); k[0, 0] = 1; f = f / k ** (beta / 2); f[0, 0] = 0
    v = np.real(np.fft.ifft2(f)); lo, hi = np.percentile(v, [1, 99]); return np.clip((v - lo) / (hi - lo), 0, 1)

def ramp(v, cols):
    """Map 0..1 through a list of colours."""
    c = np.array([hexc(x) for x in cols]); t = np.clip(v, 0, 1) * (len(c) - 1); i = np.minimum(t.astype(int), len(c) - 2); f = (t - i)[..., None]
    return c[i] * (1 - f) + c[i + 1] * f

def poster(v, levels, soft=.12):
    """Flatten into a few bands with soft edges: the flat shapes of gouache painting."""
    t = v * levels; i = np.floor(t); f = t - i; return np.clip((i + sm(.5 - soft, .5 + soft, f)) / levels, 0, 1)

def warp(img, seed, amt=5.0):
    """Wobble every edge a few pixels so nothing looks ruled."""
    dx = (fnoise(seed, 2.4) - .5) * 2 * amt; dy = (fnoise(seed + 1, 2.4) - .5) * 2 * amt
    yy, xx = np.mgrid[0:S, 0:S]; return img[(yy + dy).astype(int) % S, (xx + dx).astype(int) % S]

def blur(a, r):
    """Seamless gaussian blur (done in frequency space, so it wraps round the edges)."""
    fy = np.fft.fftfreq(a.shape[0])[:, None]; fx = np.fft.fftfreq(a.shape[1])[None, :]; g = np.exp(-2 * (math.pi * r) ** 2 * (fx * fx + fy * fy))
    if a.ndim == 2: return np.real(np.fft.ifft2(np.fft.fft2(a) * g))
    return np.stack([np.real(np.fft.ifft2(np.fft.fft2(a[..., c]) * g)) for c in range(a.shape[2])], -1)

def cells(g, seed, jitter=.8):
    """Seamless cell pattern on a g x g grid: distance to the nearest point, to the second nearest, and the cell id."""
    r = np.random.RandomState(seed); px = r.rand(g, g) * jitter + (1 - jitter) / 2; py = r.rand(g, g) * jitter + (1 - jitter) / 2
    yy, xx = np.mgrid[0:S, 0:S] / S * g; ci, cj = xx.astype(int), yy.astype(int)
    d1 = np.full((S, S), 9.0); d2 = np.full((S, S), 9.0); ident = np.zeros((S, S), int)
    for dj in (-1, 0, 1):
        for di in (-1, 0, 1):
            ni, nj = (ci + di) % g, (cj + dj) % g; d = np.hypot(ci + di + px[nj, ni] - xx, cj + dj + py[nj, ni] - yy)
            closer = d < d1; d2 = np.where(closer, d1, np.minimum(d2, d)); ident = np.where(closer, nj * g + ni, ident); d1 = np.where(closer, d, d1)
    return d1, d2, ident

def strokes(n, seed, length, width, cols, angle=None, spread=math.pi, alpha=255, mask=None, height=False):
    """Seamless layer of short tapered brush strokes (grass blades, leaves, straw). Returns RGBA 0..1."""
    r = np.random.RandomState(seed); im = Image.new('RGBA', (S, S), (0, 0, 0, 0)); dr = ImageDraw.Draw(im); pal = [tuple(int(v * 255) for v in hexc(c)) for c in cols]
    for _ in range(n):
        x, y = r.rand() * S, r.rand() * S
        if mask is not None and r.rand() > mask[int(y) % S, int(x) % S]: continue
        a = (angle if angle is not None else r.rand() * 6.283) + (r.rand() - .5) * spread; L = length * (.6 + r.rand() * .8); w = width * (.6 + r.rand() * .8)
        ex, ey = math.cos(a) * L, math.sin(a) * L; nx, ny = -math.sin(a) * w, math.cos(a) * w; c = pal[r.randint(len(pal))] + (alpha,)
        for ox in (-S, 0, S):
            for oy in (-S, 0, S):
                if -L < x + ox < S + L and -L < y + oy < S + L:
                    dr.polygon([(x + ox - nx, y + oy - ny), (x + ox + ex * .55 - nx * .8, y + oy + ey * .55 - ny * .8), (x + ox + ex, y + oy + ey), (x + ox + ex * .55 + nx * .8, y + oy + ey * .55 + ny * .8), (x + ox + nx, y + oy + ny)], fill=c)
    return np.asarray(im, dtype=float) / 255

def over(base, layer): a = layer[..., 3:4]; return base * (1 - a) + layer[..., :3] * a

def finish(rgb, seed, grain=.035, edge=.10):
    """The shared 'painted' pass: wobble, darker pooled edges where colours meet (like watercolour), soft paper grain, gentle hue drift."""
    rgb = warp(rgb, seed + 50, 2.5)
    lum = rgb.mean(2); e = np.abs(lum - blur(lum, 2.5)); e = np.clip(e / max(np.percentile(e, 98), 1e-6), 0, 1)[..., None]
    rgb = rgb * (1 - edge * e)
    rgb = rgb * (1 + (fnoise(seed + 60, .8)[..., None] - .5) * grain * 2)
    drift = fnoise(seed + 61, 3.2)[..., None] - .5; rgb = rgb + drift * np.array([.035, .01, -.03])       # warm / cool patches
    return np.clip(rgb, 0, 1)

def normal(h, strength=3.0):
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * strength; dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * strength
    n = np.stack([-dx, dy, np.ones_like(h) * .5], -1); n /= np.linalg.norm(n, axis=2, keepdims=True); return n * .5 + .5

def ao(h): a = np.clip(.5 + (h - blur(h, 10)) * 2.2, 0, 1); return np.clip(.62 + a * .5, 0, 1)

# ------------------------------------------------------------------ ground
def grass(seed=1, cols=('5a964f', '79b05c', '9cc86a', 'bfd982'), tips=('b4d878', 'd2e692', '8fc266'), n=5200):
    v = poster(fnoise(seed, 2.6) * .7 + fnoise(seed + 1, 1.6) * .3, 5); rgb = ramp(v, cols)
    dark = strokes(n // 2, seed + 2, 26, 3.2, (cols[0], cols[1]), alpha=150); rgb = over(rgb, dark)
    lay = strokes(n, seed + 3, 30, 2.6, tips, alpha=190, mask=.35 + .65 * v); rgb = over(rgb, lay)
    h = v * .4 + lay[..., 3] * .45 + dark[..., 3] * .15; return rgb, blur(h, 1.2)

def meadow(seed=11):
    rgb, h = grass(seed, n=4200); m = sm(.56, .66, fnoise(seed + 5, 2.8) * .8 + fnoise(seed + 6, 1.4) * .2)[..., None]       # bare earth patches
    dirt = ramp(poster(fnoise(seed + 7, 2.0), 4), ('7c6044', '9a7c56', 'b3966a', 'c9ae84')); rgb = rgb * (1 - m) + dirt * m
    peb = strokes(260, seed + 8, 7, 5, ('c9c2b2', 'a89f90', 'ded8c8'), alpha=235, mask=m[..., 0]); rgb = over(rgb, peb)
    for i, c in enumerate((('fdfdf6', 'fff3b0'), ('f6c8dc', 'ffe9f2'), ('ffe08a', 'fff6c8'))):                                    # tiny flowers
        fl = strokes(70, seed + 20 + i, 5, 5, c, alpha=255, mask=(1 - m[..., 0]) * sm(.45, .7, fnoise(seed + 30 + i, 2.4))); rgb = over(rgb, fl)
    return rgb, h * (1 - m[..., 0] * .6) + peb[..., 3] * .3

def gravel(seed=21):
    base = ramp(poster(fnoise(seed, 2.2), 4), ('86755a', '9c8a6c', 'b3a182', 'c6b596')); d1, d2, ident = cells(15, seed + 1)
    r = np.random.RandomState(seed + 2); tone = r.rand(15 * 15)[ident]; size = .34 + .2 * r.rand(15 * 15)[ident]; keep = r.rand(15 * 15)[ident] > .22
    stone = sm(0, .07, size - d1) * keep; yy, xx = np.mgrid[0:S, 0:S]
    lit = (.82 + .3 * sm(0, .5, size - d1)) * (.9 + .2 * fnoise(seed + 3, 1.2))
    pal = ramp(tone, ('8d8a88', 'a8a49c', 'c8c2b4', 'ad9f8c', 'd8d0c0')); rgb = base * (1 - stone[..., None]) + pal * lit[..., None] * stone[..., None]
    rgb = rgb * (1 - .28 * sm(.0, .1, size + .08 - d1)[..., None] * (1 - stone[..., None]))                                         # contact shadow round each stone
    return rgb, base.mean(2) * .25 + stone * (.45 + .4 * np.clip(size - d1, 0, 1) * 2)

def rock(seed=31):
    strata = fnoise(seed, 2.4, (3.2, 1)) * .6 + fnoise(seed + 1, 1.8) * .4; v = poster(strata, 5, .08); rgb = ramp(v, ('6e6a70', '8a8582', 'a8a298', 'c4bcae', 'ddd5c2'))
    d1, d2, _ = cells(7, seed + 2, 1.0); crack = 1 - sm(.0, .045, d2 - d1); crack2 = 1 - sm(0, .03, cells(15, seed + 3, 1.0)[1] - cells(15, seed + 3, 1.0)[0])
    c = np.clip(crack + crack2 * .45, 0, 1)[..., None]; rgb = rgb * (1 - .5 * c) + hexc('4a4448') * .5 * c
    moss = sm(.68, .8, fnoise(seed + 4, 2.6))[..., None] * (v[..., None] > .5) * .4; rgb = rgb * (1 - moss) + ramp(fnoise(seed + 5, 1.5), ('6f9a58', '94b86a')) * moss
    return rgb, v * .7 + .3 - c[..., 0] * .5

def sand(seed=41):
    w = fnoise(seed, 2.6); yy, xx = np.mgrid[0:S, 0:S] / S; rip = .5 + .5 * np.sin((yy * 22 + w * 2.6 + np.sin(xx * 6.283 * 2) * .25) * 6.283)
    v = poster(rip * .45 + w * .55, 4, .2); rgb = ramp(v, ('cdb88a', 'dfcca0', 'ecddb6', 'f6edd0'))
    sp = strokes(500, seed + 1, 3, 2.5, ('b79f74', 'fff8e2', 'a89070'), alpha=200); rgb = over(rgb, sp); return rgb, v * .6 + sp[..., 3] * .15

def forest(seed=51):
    v = poster(fnoise(seed, 2.3), 4); rgb = ramp(v, ('3f422c', '545434', '6b643e', '80744a'))
    twig = strokes(240, seed + 1, 46, 1.6, ('3a2c20', '5a4630'), alpha=220); rgb = over(rgb, twig)
    leaf = np.zeros((S, S, 4))
    for i, c in enumerate((('a07c38', 'b8923f'), ('86522c', 'a0683a'), ('6a8840', '86a04c'), ('c9a850', 'd8bd6a'))):
        l = strokes(120, seed + 2 + i, 30, 12, c, alpha=245); a = l[..., 3:4]; leaf = np.concatenate([leaf[..., :3] * (1 - a) + l[..., :3] * a, np.maximum(leaf[..., 3:4], a)], 2)
    rgb = over(rgb * (1 - .25 * blur(leaf[..., 3], 3)[..., None]), leaf); moss = sm(.6, .72, fnoise(seed + 9, 2.6))[..., None] * .5 * (1 - leaf[..., 3:4])
    rgb = rgb * (1 - moss) + ramp(fnoise(seed + 10, 1.4), ('4f7a40', '7aa052')) * moss; return rgb, v * .35 + leaf[..., 3] * .45 + twig[..., 3] * .3

def snow(seed=61):
    v = poster(fnoise(seed, 2.9) * .8 + fnoise(seed + 1, 1.5) * .2, 4, .25); rgb = ramp(v, ('b9cae6', 'd3dff2', 'e9f0fa', 'fbfdff'))
    sp = strokes(380, seed + 2, 3, 2.2, ('ffffff', 'f4f8ff'), alpha=230); return over(rgb, sp), v

def path(seed=71):
    v = poster(fnoise(seed, 2.2) * .7 + fnoise(seed + 1, 1.2, (3, 1)) * .3, 4); rgb = ramp(v, ('86694a', '9f8058', 'b89a6c', 'cdb286'))
    peb = strokes(420, seed + 2, 8, 5.5, ('c6beae', 'a39a8a', 'ddd6c6', '8f8576'), alpha=240); rut = strokes(60, seed + 3, 120, 5, ('7a5f44',), angle=0, spread=.25, alpha=90)
    return over(over(rgb, rut), peb), v * .5 + peb[..., 3] * .4 - rut[..., 3] * .15

def cobble(seed=81, g=9, pal=('85808a', 'a09a98', 'bcb5aa', 'ab9f90', 'cfc8ba'), mortar='5f584f', moss=True):
    d1, d2, ident = cells(g, seed, .7); r = np.random.RandomState(seed + 1); tone = r.rand(g * g)[ident]; edge = d2 - d1
    stone = sm(.05, .12, edge); bev = sm(.05, .3, edge); rgb = ramp(tone * .8 + fnoise(seed + 2, 1.6) * .2, pal) * (.8 + .26 * bev[..., None])
    m = ramp(fnoise(seed + 3, 1.8), (mortar, '7a7266')); rgb = m * (1 - stone[..., None]) + rgb * stone[..., None]
    if moss:
        g_ = sm(.5, .7, fnoise(seed + 4, 2.4))[..., None] * (1 - stone[..., None]) * .8; rgb = rgb * (1 - g_) + hexc('6f9a58') * g_
    return rgb, stone * (.55 + .45 * bev)

# ------------------------------------------------------------------ buildings
def bricks(seed, bw, bh, pal, mortar, gap=7, round_=10, chip=.5):
    yy, xx = np.mgrid[0:S, 0:S]; row = yy // bh; x2 = (xx + (row % 2) * (bw // 2)) % S; col = x2 // bw
    r = np.random.RandomState(seed); nrow, ncol = S // bh, S // bw; tone = r.rand(nrow, ncol)[row % nrow, col % ncol]
    lx = x2 % bw; ly = yy % bh; dx = np.minimum(lx, bw - 1 - lx); dy = np.minimum(ly, bh - 1 - ly)
    wob = (fnoise(seed + 1, 1.8) - .5) * chip * 14; d = np.minimum(dx, dy) + wob
    corner = np.hypot(np.maximum(round_ - dx, 0), np.maximum(round_ - dy, 0)); d = np.where((dx < round_) & (dy < round_), round_ - corner + wob, d)
    face = sm(gap * .5, gap * .5 + 3, d); bev = sm(gap * .5, gap * .5 + 16, d)
    rgb = ramp(np.clip(tone * .75 + fnoise(seed + 2, 1.5) * .25, 0, 1), pal) * (.84 + .2 * bev[..., None]); stain = sm(.55, .8, fnoise(seed + 3, 2.6))[..., None] * .12
    rgb = rgb * (1 - stain); m = ramp(fnoise(seed + 4, 1.6), (mortar, mortar)) * (.9 + .2 * fnoise(seed + 5, 1.0)[..., None])
    return m * (1 - face[..., None]) + rgb * face[..., None], face * (.5 + .5 * bev)

def plaster(seed=101):
    v = poster(fnoise(seed, 2.8) * .6 + fnoise(seed + 1, 1.4, (2.5, 1)) * .4, 4, .3); rgb = ramp(v, ('dccdb2', 'e9dcc4', 'f2e8d4', 'f9f3e6'))
    tr = strokes(180, seed + 2, 150, 16, ('fbf6ea', 'd9cab0'), alpha=38); rgb = over(rgb, tr)
    crack = 1 - sm(0, .012, cells(4, seed + 3, 1.0)[1] - cells(4, seed + 3, 1.0)[0]); crack = crack * sm(.68, .82, fnoise(seed + 4, 2.2))
    return rgb * (1 - .28 * crack[..., None]), v * .5 + tr[..., 3] * .5 - crack * .4

def planks(seed=111, pw=128, pal=('6e4a30', '86603a', '9f7648', 'b58c5a')):
    yy, xx = np.mgrid[0:S, 0:S]; col = xx // pw; n = S // pw; r = np.random.RandomState(seed); tone = r.rand(n)[col]; off = r.randint(0, S, n)[col]
    grain = fnoise(seed + 1, 1.7, (1, 9)); g = grain[(yy + off) % S, xx]; knots = cells(5, seed + 2)[0]; g = np.clip(g + .35 * np.sin(knots * 40) * sm(.28, .0, knots), 0, 1)
    v = poster(np.clip(g * .7 + tone * .3, 0, 1), 5, .2); lx = xx % pw; edge = sm(0, 5, np.minimum(lx, pw - 1 - lx))
    jl = r.randint(0, S, n)[col]; joint = sm(0, 4, np.abs(((yy - jl) % S) - 2)); e = (edge * joint)[..., None]
    rgb = ramp(v, pal) * (.55 + .45 * e) * (.93 + .1 * sm(0, 26, np.minimum(lx, pw - 1 - lx))[..., None])
    nail = np.zeros((S, S))
    for dy_ in (18, -18):
        nail = np.maximum(nail, sm(5, 3, np.hypot(np.minimum(lx, pw - lx) - 16, ((yy - jl - dy_) % S))))
    rgb = rgb * (1 - .55 * nail[..., None]); return rgb, v * .25 + e[..., 0] * .6 - nail * .2

def roof(seed, pal, tw=128, th=128):
    yy, xx = np.mgrid[0:S, 0:S]; row = yy // th; x2 = (xx + (row % 2) * (tw // 2)) % S; col = x2 // tw; r = np.random.RandomState(seed)
    tone = r.rand(S // th, S // tw)[row % (S // th), col % (S // tw)]; lx = (x2 % tw) / tw - .5; ly = (yy % th) / th            # ly 0 top .. 1 bottom
    rad = .5; dist = np.hypot(lx, np.maximum(ly - (1 - rad), 0) * (th / tw)); inside = sm(.5, .47, dist)                          # rounded lower edge
    shade = .7 + .3 * ly + .1 * sm(.5, .2, np.abs(lx)); under = sm(0, .28, ly)                                                    # shadow cast by the row above
    v = np.clip(tone * .7 + fnoise(seed + 1, 1.6) * .3, 0, 1); rgb = ramp(poster(v, 4, .25), pal) * (shade * (.62 + .38 * under))[..., None]
    gapc = hexc(pal[0]) * .45; rgb = rgb * inside[..., None] + gapc * (1 - inside[..., None])
    moss = sm(.66, .8, fnoise(seed + 2, 2.6))[..., None] * .35; rgb = rgb * (1 - moss) + hexc('7a9a5a') * moss
    return rgb, inside * (.35 + .65 * ly)

def thatch(seed=131):
    yy, xx = np.mgrid[0:S, 0:S]; th = 256; ly = (yy % th) / th; rgb = ramp(poster(fnoise(seed, 1.8, (1, 6)), 4, .3), ('8f7238', 'a88a48', 'c2a45e', 'd9c07a'))
    st = strokes(5200, seed + 1, 70, 2.2, ('d9c27c', 'b8994f', 'e8d596', '94783c'), angle=math.pi / 2, spread=.22, alpha=215); rgb = over(rgb, st)
    wob = (fnoise(seed + 2, 1.6, (4, 1)) - .5) * .12; band = sm(.0, .16, (ly + wob) % 1.0); rgb = rgb * (.55 + .45 * band[..., None]) * (.86 + .2 * ly[..., None])
    return rgb, st[..., 3] * .4 + ly * .5 + band * .1

def slabs(seed=141, n=4, pal=('6d6982', '85819a', '9f9bb0', 'b6b2c4'), mortar='3c3850'):
    rgb, h = bricks(seed, S // n, S // n, pal, mortar, gap=9, round_=14, chip=.8); yy, xx = np.mgrid[0:S, 0:S]
    crack = 1 - sm(0, .02, cells(6, seed + 7, 1.0)[1] - cells(6, seed + 7, 1.0)[0]); crack = crack * sm(.5, .7, fnoise(seed + 8, 2.4))
    return rgb * (1 - .45 * crack[..., None]), h - crack * .3

TERRAIN = {'grass': (grass, {}), 'meadow': (meadow, {}), 'gravel': (gravel, {}), 'rock': (rock, {}), 'sand': (sand, {}), 'forest': (forest, {}),
           'snow': (snow, {}), 'path': (path, {}), 'cobble': (cobble, {})}
BUILD = {'plaster': (plaster, {}), 'planks': (planks, {}), 'planks_dark': (planks, dict(seed=112, pal=('3f2c24', '54392c', '6a4a36', '805c42'))),
         'stone_bricks': (bricks, dict(seed=151, bw=256, bh=128, pal=('9a9eac', 'b4b7c2', 'cbcdd6', 'dedfe4', 'c9c2b6'), mortar='7d8090')),
         'dungeon_bricks': (bricks, dict(seed=161, bw=256, bh=128, pal=('383450', '4a4566', '5f5a7c', '747092'), mortar='24212f', chip=.9)),
         'roof_red': (roof, dict(seed=171, pal=('8f3a2a', 'b04e34', 'cc6642', 'e0845a'))), 'roof_blue': (roof, dict(seed=172, pal=('2e4680', '40609f', '587fc0', '7a9edb'))),
         'roof_teal': (roof, dict(seed=173, pal=('2a6a62', '3a8a7c', '55a896', '7cc4ae'))), 'roof_violet': (roof, dict(seed=174, pal=('4a3a80', '63509f', '806cc0', 'a28fdb'))),
         'roof_gold': (roof, dict(seed=175, pal=('8f6a26', 'b08634', 'cca24a', 'e0bd6a'))), 'thatch': (thatch, {}), 'floor_slabs': (slabs, {}),
         'cobble_dark': (cobble, dict(seed=181, g=7, pal=('3c3852', '4e4a66', '625e7c', '555070'), mortar='22202c', moss=False))}
STRENGTH = {'grass': 2.2, 'meadow': 2.4, 'sand': 1.6, 'snow': 1.2, 'plaster': 1.4, 'thatch': 3.0}

if __name__ == '__main__':
    only = sys.argv[1:]; tiles = []
    for group, folder, defs in (('terrain', 'assets/textures/terrain_painted', TERRAIN), ('build', 'assets/textures/painted', BUILD)):
        os.makedirs(os.path.join(ROOT, folder), exist_ok=True)
        for name, (fn, kw) in defs.items():
            if only and name not in only: continue
            rgb, h = fn(**kw); seed = kw.get('seed', abs(hash(name)) % 1000); rgb = finish(rgb, 900 + len(tiles)); h = np.clip(h, 0, 1)
            col = Image.fromarray((rgb * 255).astype('uint8')); col.save(os.path.join(ROOT, folder, name + '_col.jpg'), quality=93)
            Image.fromarray((normal(blur(h, .8), STRENGTH.get(name, 3.0)) * 255).astype('uint8')).save(os.path.join(ROOT, folder, name + '_nrm.jpg'), quality=93)
            if group == 'terrain': Image.fromarray((ao(h) * 255).astype('uint8')).save(os.path.join(ROOT, folder, name + '_ao.jpg'), quality=90)
            tiles.append((name, col)); print('made', name)
    if not only:
        cols, sz = 5, 300; rows = (len(tiles) + cols - 1) // cols; sheet = Image.new('RGB', (cols * sz, rows * (sz + 22)), (236, 232, 222)); dr = ImageDraw.Draw(sheet)
        for i, (name, col) in enumerate(tiles):
            t = col.resize((sz // 2 - 2, sz // 2 - 2), Image.LANCZOS); x, y = (i % cols) * sz, (i // cols) * (sz + 22)
            for a in (0, 1):
                for b in (0, 1): sheet.paste(t, (x + 4 + a * (sz // 2 - 2), y + 4 + b * (sz // 2 - 2)))
            dr.text((x + 6, y + sz + 4), name, fill=(60, 50, 60))
        sheet.save(os.path.join(ROOT, 'docs/kit/textures.png'))
