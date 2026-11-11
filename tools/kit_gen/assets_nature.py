"""Nature: trees, bushes, grass, flowers, rocks, crystals. Painted-gradient colours, soft rounded leaf masses."""
import math
import numpy as np
from kitlib import *

BARK = ('5a3828', '94643f')

def trunk(h, r=.26, lean=0.0, seg=8):
    t = lathe([(r * 1.7, 0), (r * 1.15, h * .12), (r, h * .3), (r * .62, h)], seg).grad(*BARK)
    return t.bend(lean)

def canopy(m, cx, cy, cz, r, seed, lo, hi, n=5, squash=.82):
    rs = np.random.RandomState(seed)
    for i in range(n):
        a = 2 * math.pi * i / max(n - 1, 1) + rs.rand(); k = 0 if i == 0 else 1
        rr = r * (1.0 if i == 0 else .62 + rs.rand() * .2)
        b = ico(rr, 2).noise(rr * .16, seed * 7 + i, 1.3).scale(1, squash, 1)
        b.grad(lo, hi, power=.8).move(cx + math.cos(a) * r * .62 * k, cy + (r * .28 if i == 0 else -r * .18 + rs.rand() * r * .3), cz + math.sin(a) * r * .62 * k)
        m.add(b)

def tree_round(name, seed, h=5.2, lo='3d9b58', hi='c4e86a'):
    rs = np.random.RandomState(seed); lean = (rs.rand() - .5) * .9; m = Model(name)
    m.add(trunk(h * .58, .27, lean)); canopy(m, lean, h * .72, 0, h * .34, seed, lo, hi, 6); return m

def tree_blossom(name, seed, lo='e36fa0', hi='ffd3e6'):
    m = Model(name); h = 4.4; m.add(trunk(h * .5, .24, .5))
    for a, ln in ((40, 1.5), (-35, 1.3)):
        m.add(cyl(.12, .06, ln, 6).grad(*BARK).rot(z=a).move(.4, h * .42, 0))
    canopy(m, .6, h * .72, 0, h * .36, seed, lo, hi, 7, .7)
    for i in range(5):   # fallen petals on the ground
        a = i * 1.9; m.add(ico(.09, 0, hi).scale(1, .15, 1).move(math.cos(a) * (1.2 + i * .25), .02, math.sin(a) * (1.2 + i * .2)))
    return m

def tree_pine(name, seed, h=6.5, lo='1e6a5e', hi='63c08a'):
    m = Model(name); m.add(trunk(h * .3, .2, 0, 7)); n = 4
    for i in range(n):
        t = i / (n - 1); r = h * (.34 - .22 * t); y = h * (.18 + .51 * t); ch = h * .27
        c = lathe([(r, 0), (r * .45, ch * .5), (0, ch)], 9).noise(r * .1, seed + i, 2).grad(lo, hi, power=1.4).rot(y=37 * i).move(0, y, 0); m.add(c)
    return m

def tree_dead(name, seed):
    m = Model(name); c = ('2c2438', '6a5a80'); m.add(lathe([(.42, 0), (.26, .5), (.2, 1.6), (.1, 3.6), (0, 4.3)], 7).grad(*c).bend(.5))
    for i, (a, y, ln) in enumerate(((50, 1.9, 1.5), (-55, 2.4, 1.3), (35, 3.0, .9), (-40, 3.3, .7))):
        m.add(cyl(.09, .02, ln, 5).grad(*c).rot(z=a).rot(y=70 * i).move(.2 + .1 * i, y, 0))
    return m

def bush(name, seed, lo='3a9655', hi='b4e267', flowers=None):
    m = Model(name); rs = np.random.RandomState(seed)
    for i in range(4):
        a = i * 2.1 + rs.rand(); r = .5 - i * .06
        m.add(ico(r, 2).noise(r * .2, seed + i, 1.4).scale(1, .78, 1).grad(lo, hi).move(math.cos(a) * .32 * (i > 0), r * .62, math.sin(a) * .32 * (i > 0)))
    if flowers:
        for i in range(9):
            a = i * 2.4; e = .35 + (i % 3) * .3
            m.add(ico(.065, 1, flowers).move(math.cos(a) * math.cos(e) * .55, .3 + .42 * math.sin(e), math.sin(a) * math.cos(e) * .55))
    return m

def grass(name, seed, lo='3f9a55', hi='c9ee74', n=9, h=.55):
    m = Model(name); rs = np.random.RandomState(seed)
    for i in range(n):
        a = rs.rand() * 6.28; d = rs.rand() * .22; hh = h * (.6 + rs.rand() * .5); w = .035 + rs.rand() * .02; ln = (rs.rand() - .3) * .3
        p = Part([[-w, 0, 0], [w, 0, 0], [w * .7 + ln * .35, hh * .55, 0], [-w * .7 + ln * .35, hh * .55, 0], [ln, hh, 0]], [[0, 1, 2], [0, 2, 3], [3, 2, 4]], mat='matte2s')
        p.grad(lo, hi).rot(x=(rs.rand() - .5) * 30).rot(y=math.degrees(a)).move(math.cos(a) * d, 0, math.sin(a) * d); m.add(p)
    return m

def flower(name, petal, centre='ffd84a', n=3, seed=0):
    m = Model(name); rs = np.random.RandomState(seed)
    for k in range(n):
        ox, oz = (0, 0) if k == 0 else (math.cos(k * 2.3) * .16, math.sin(k * 2.3) * .16); h = .34 + rs.rand() * .16
        m.add(cyl(.012, .01, h, 4).paint('4fa85a').move(ox, 0, oz))
        m.add(Part([[0, 0, 0], [.09, .1, .03], [.02, .16, 0]], [[0, 1, 2]], mat='matte2s').paint('5cb860').rot(y=120 * k).move(ox, h * .3, oz))
        for i in range(5):
            a = i * 72
            m.add(ico(.055, 1).scale(1, .28, .62).grad(mixc(petal, 'ffffff', .1), mixc(petal, 'ffffff', .45), axis=0).move(.055, 0, 0).rot(z=18).rot(y=a).move(ox, h, oz))
        m.add(ico(.032, 1, centre).scale(1, .7, 1).move(ox, h + .012, oz))
    return m

def rock(name, seed, sx=1.0, sy=.7, sz=.85, moss=True, lo='6a6f8a', hi='b9c0d2'):
    p = ico(.6, 2, smooth=False).noise(.16, seed, 1.1).noise(.05, seed + 9, 3.5).scale(sx, sy, sz)
    p.move(0, -p.v[:, 1].min() - .06 * sy, 0); p.grad(lo, hi, power=.9)
    if moss:
        t = np.clip((p.v[:, 1] / p.v[:, 1].max() - .62) / .25, 0, 1)[:, None]; p.c = p.c * (1 - t) + mixc('6fb85a', 'b9e070', .4) * t
    p.v[:, 1] = np.maximum(p.v[:, 1], 0); return Model(name).add(p)

def cliff(name, seed):
    m = Model(name)
    for i, (x, h, w) in enumerate(((-1.3, 3.2, 1.5), (0, 4.4, 1.9), (1.4, 2.6, 1.4))):
        m.add(rock('r', seed + i, w, h / .6 * .5, w * .8, moss=True).parts[0].move(x, 0, (i % 2) * .3))
    return m

def crystal(name, colour, seed=0, n=5, h=1.3):
    m = Model(name); rs = np.random.RandomState(seed); m.add(rock('b', seed, .9, .35, .9, False, '4a4760', '807c98').parts[0])
    for i in range(n):
        hh = h * (1 if i == 0 else .45 + rs.rand() * .4); r = hh * .13
        c = lathe([(r * .7, 0), (r, hh * .72), (0, hh)], 6, smooth=False, mat='glow').paint(colour)
        if i: c.rot(z=(22 + rs.rand() * 22)).rot(y=i * 360 / (n - 1) + rs.rand() * 30).move(math.cos(i * 1.57) * .1, .1, math.sin(i * 1.57) * .1)
        m.add(c.move(0, .1, 0))
    return m

def mushroom(name, cap, glow=False, n=3):
    m = Model(name)
    for k in range(n):
        s = 1 - k * .28; x, z = (0, 0) if k == 0 else (math.cos(k * 2.4) * .22, math.sin(k * 2.4) * .22)
        m.add(lathe([(.05, 0), (.04, .22), (.035, .3)], 6).grad('d8cdb8', 'f6efe2').scale(s).move(x, 0, z))
        cp = lathe([(.2, .0), (.19, .05), (.12, .13), (0, .17)], 9, mat='glow' if glow else 'matte')
        cp.paint(cap) if glow else cp.grad(mixc(cap, '000000', .2), mixc(cap, 'ffffff', .25))
        m.add(cp.scale(s).move(x, .28 * s, z))
    return m

def stump(name):
    m = Model(name); m.add(lathe([(.5, 0), (.36, .12), (.33, .5)], 9).grad(*BARK)); m.add(cyl(.31, .31, .02, 9).paint('d9b98a').move(0, .5, 0))
    m.add(ico(.2, 1).scale(1, .5, 1).grad('3a9655', 'b4e267').move(.25, .5, .1)); return m

def log(name):
    m = Model(name); m.add(cyl(.28, .26, 2.0, 9).grad('5a3828', '8a5c3c', axis=0).rot(z=90).move(1.0, .28, 0))
    for x in (-1.0, 1.0): m.add(cyl(.24, .24, .02, 9).paint('d9b98a').rot(z=90).move(x + (.01 if x > 0 else .01), .28, 0))
    return m

def reeds(name):
    m = Model(name)
    for i in range(7):
        a = i * 2.4; d = .05 + (i % 3) * .08; h = .9 + (i % 4) * .2
        m.add(cyl(.012, .008, h, 4).grad('4c8f52', 'a9d46c').bend(.12).rot(y=a * 57).move(math.cos(a) * d, 0, math.sin(a) * d))
        if i % 2 == 0: m.add(lathe([(0, 0), (.03, .03), (.03, .2), (0, .24)], 6).paint('6e4630').move(math.cos(a) * d + .12 * math.cos(a), h - .06, math.sin(a) * d + .12 * math.sin(a)))
    return m

def lilypad(name):
    m = Model(name)
    for k, (x, z, r) in enumerate(((0, 0, .32), (.5, .25, .22), (-.3, .45, .18))):
        pts = [[0, 0, 0]] + [[r * math.cos(math.radians(a)), 0, r * math.sin(math.radians(a))] for a in range(20, 341, 32)]
        f = [[0, i + 1, i] for i in range(1, len(pts) - 1)]
        m.add(Part(pts, f, mat='matte2s').grad('3f9a55', '8fd46a', axis=0).rot(y=k * 100).move(x, .02, z))
    m.add(ico(.07, 1).scale(1, .8, 1).grad('ff8fc0', 'ffe0ee').move(.05, .07, .05)); return m

def all_models():
    return [
        tree_round('tree_round_a', 1), tree_round('tree_round_b', 5, 6.0), tree_round('tree_autumn', 3, 5.0, 'd9622e', 'ffd45a'),
        tree_blossom('tree_blossom_pink', 2), tree_blossom('tree_blossom_white', 4, 'b9c4f0', 'ffffff'),
        tree_pine('tree_pine_a', 1), tree_pine('tree_pine_b', 2, 8.0), tree_pine('tree_pine_snow', 3, 6.5, '3f7f86', 'f2f8ff'), tree_dead('tree_dead', 1),
        bush('bush_a', 1), bush('bush_flower_pink', 2, flowers='ff8fc0'), bush('bush_flower_blue', 3, flowers='8fc8ff'), bush('bush_shadow', 4, '3a2a5e', '9a6ad8'),
        grass('grass_tuft', 1), grass('grass_tall', 2, n=11, h=.95), grass('grass_shadow', 3, '3a2a5e', 'a884e6'),
        flower('flower_white', 'f4f6ff'), flower('flower_blue', '5aa6ff', 'fff2a8'), flower('flower_red', 'ff5a6e'), flower('flower_gold', 'ffc83a', 'ff8a2a'),
        rock('rock_a', 1), rock('rock_b', 2, 1.3, .6, 1.0), rock('rock_tall', 3, .8, 1.6, .8), rock('boulder', 4, 2.4, 1.6, 2.0), cliff('cliff_spires', 6),
        crystal('crystal_cyan', '6fe8ff'), crystal('crystal_violet', 'b884ff', 2), crystal('crystal_gold', 'ffd25a', 3, 4, .9),
        mushroom('mushroom_red', 'e8523e'), mushroom('mushroom_glow', '7af0e0', True), stump('stump'), log('log'), reeds('reeds'), lilypad('lilypads'),
    ]
