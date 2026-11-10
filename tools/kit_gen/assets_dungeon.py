"""Dungeon: dark violet stone on a 4 m grid, with glowing accents. Three glow colours for the three dungeons."""
import math
import numpy as np
from kitlib import *

ST = ('3e3a55', '8a86a6'); DK = '2c2940'; GOLD = 'ffd25a'; WOOD = ('4a2f24', '7d553a')

def floor(name, seed=1, rune=None):
    m = Model(name); rs = np.random.RandomState(seed); m.add(box(4, .1, 4, DK).move(0, -.1, 0))
    for i in range(4):
        for j in range(4):
            c = mixc(ST[0], ST[1], .25 + rs.rand() * .45); m.add(box(.94, .06, .94, c).move(-1.5 + i, -.02 - rs.rand() * .02, -1.5 + j))
    if rune: m.add(torus(1.2, .05, 20, 3, mat='glow').paint(rune).scale(1, .3, 1).move(0, .05, 0)); m.add(torus(.7, .04, 6, 3, mat='glow').paint(rune).scale(1, .3, 1).move(0, .05, 0))
    return m

def wall(name, torch=None, seed=1):
    m = Model(name); rs = np.random.RandomState(seed); m.add(box(4, 4, .5).grad(*ST).move(0, 0, -.25)); m.add(box(4, .45, .7, DK).grad(DK, ST[0]).move(0, 0, -.25)); m.add(box(4, .3, .66).grad(*ST).move(0, 3.7, -.25))
    for r in range(4):
        for i in range(3 + r % 2):
            w = 1.1; x = -2 + (i + (.0 if r % 2 else .5)) * 4 / (3.5) + .1
            if abs(x) < 1.6 and rs.rand() < .6: m.add(box(w, .5, .06, mixc(ST[0], ST[1], .3 + rs.rand() * .5)).move(x, .65 + r * .75, .0))
    for sx in (-1, 1): m.add(box(.45, 4, .7).grad(*ST).move(sx * 1.78, 0, -.22))
    if torch:
        m.add(box(.1, .5, .3, '2a2d44', 'metal').rot(x=-30).move(0, 1.9, .15)); m.add(lathe([(.04, 0), (.1, .3), (.12, .34)], 6, '2a2d44', 'metal').move(0, 2.2, .38))
        m.add(lathe([(.1, 0), (.12, .12), (0, .42)], 6, mat='glow').paint(torch).move(0, 2.52, .38))
    return m

def doorway(name, glow='6fe8ff'):
    m = Model(name)
    for sx in (-1, 1): m.add(box(1.1, 4, .7).grad(*ST).move(sx * 1.45, 0, -.25)); m.add(box(.5, 3.0, .9).grad(*ST).move(sx * 1.0, 0, -.25)); m.add(box(.62, .25, 1.0, DK).move(sx * 1.0, 3.0, -.25))
    m.add(box(4, 1.0, .7).grad(*ST).move(0, 3.0, -.25)); m.add(torus(.85, .14, 12, 4, DK, arc=180).rot(x=-90).move(0, 2.3, .2))
    m.add(lathe([(0, 0), (.16, .2), (0, .4)], 4, mat='glow', smooth=False).paint(glow).scale(1, 1, .4).move(0, 3.3, .12)); return m

def pillar(name, broken=False):
    m = Model(name); h = 1.7 if broken else 4.0
    m.add(lathe([(.62, 0), (.62, .25), (.48, .35), (.4, .5), (.36, h - (.0 if broken else .5))] + ([] if broken else [(.48, 3.65), (.62, 3.75), (.62, 4.0)]), 8, smooth=False).grad(*ST))
    if broken:
        m.add(cyl(.36, .37, .9, 8, smooth=False).grad(*ST).rot(z=90).rot(y=25).move(1.3, .37, .3))
    else:
        for y in (1.3, 2.6): m.add(cyl(.39, .39, .08, 8, GOLD, 'metal', caps=False).move(0, y, 0))
    return m

def stairs(name):
    m = Model(name)
    for i in range(8): m.add(box(4, .5 * (i + 1), .5).grad(*ST).move(0, 0, -1.75 + i * .5))
    return m

def chest(name, colour='6b4630', trim=GOLD, glow=None):
    m = Model(name); m.add(box(1.0, .5, .65).grad(mixc(colour, '000000', .3), colour))
    lid = [(-.5, 0)] + [(-.5 * math.cos(math.radians(a)), .3 * math.sin(math.radians(a))) for a in range(0, 181, 30)][1:-1] + [(.5, 0)]
    m.add(extrude(lid[::-1], .65).grad(colour, mixc(colour, 'ffffff', .25)).move(0, .5, 0))
    for x in (-.42, 0, .42): m.add(box(.1, .52, .68, trim, 'metal').move(x, 0, 0)); m.add(extrude([(p[0] * .2 / 1.0 * 0 + (p[0] > 0) * .1 - .05 if False else p[0], p[1]) for p in lid][::-1], .68, trim, 'metal').scale(.1, 1.04, 1).move(x, .5, 0))
    m.add(box(1.04, .07, .69, trim, 'metal').move(0, .46, 0)); m.add(box(.16, .2, .06, trim, 'metal').move(0, .38, .34))
    if glow: m.add(box(.9, .04, .5, glow, 'glow').move(0, .5, .02)); m.add(lathe([(0, 0), (.08, .1), (0, .2)], 4, mat='glow', smooth=False).paint(glow).move(0, .34, .38))
    return m

def brazier(name, flame='ff9a3a'):
    m = Model(name); m.add(lathe([(.35, 0), (.1, .12), (.07, .8), (.42, 1.05), (.46, 1.12), (.36, 1.12), (.3, 1.0)], 8, '3a3d52', 'metal'))
    m.add(ico(.3, 1, mat='glow').paint(flame).noise(.08, 2, 2).scale(.9, 1.5, .9).move(0, 1.35, 0)); m.add(ico(.14, 1, mat='glow').paint(mixc(flame, 'ffffff', .6)).scale(1, 1.6, 1).move(0, 1.3, 0)); return m

def altar(name, glow='b884ff'):
    m = Model(name)
    for i, (w, h) in enumerate(((3.2, .25), (2.6, .25), (2.0, .25))): m.add(box(w, h, w * .7).grad(*ST).move(0, i * .25, 0))
    m.add(box(1.5, .7, .8).grad(*ST).move(0, .75, 0)); m.add(box(1.7, .14, 1.0, DK).move(0, 1.45, 0)); m.add(box(1.2, .03, .6, glow, 'glow').move(0, 1.59, 0))
    m.add(lathe([(0, 0), (.2, .3), (0, .7)], 6, mat='glow', smooth=False).paint(glow).move(0, 2.0, 0)); return m

def obelisk(name, glow='6fe8ff'):
    m = Model(name); m.add(box(1.3, .3, 1.3).grad(*ST)); m.add(lathe([(.55, 0), (.4, 3.0), (0, 3.7)], 4, smooth=False).grad(DK, ST[1]).rot(y=45).move(0, .3, 0))
    for i, y in enumerate((.9, 1.5, 2.1, 2.7)):
        r = .55 - y * .05; m.add(box(r * .8, .28, .04, glow, 'glow').move(0, .3 + y, r * .71 + .0).rot(y=90 * i))
    return m

def portal(name, glow='6fe8ff'):
    m = Model(name); m.add(box(3.4, .3, 1.2).grad(*ST)); m.add(torus(1.5, .26, 16, 6, ST[0]).grad(*ST).rot(x=90).move(0, 1.95, 0))
    for i in range(8):
        a = math.radians(i * 45 + 22.5); m.add(lathe([(0, 0), (.1, .12), (0, .24)], 4, mat='glow', smooth=False).paint(glow).scale(1, 1, .5).move(math.cos(a) * 1.5, 1.83 + math.sin(a) * 1.5, .27))
    pts = [[0, 1.95, 0]] + [[1.28 * math.cos(math.radians(a)), 1.95 + 1.28 * math.sin(math.radians(a)), 0] for a in range(0, 360, 30)]
    m.add(Part(pts, [[0, i, i % 12 + 1] for i in range(1, 13)], mat='glow').paint(mixc(glow, 'ffffff', .25))); m.parts[-1].mat = 'glow'
    return m

def gate(name):
    m = Model(name)
    for sx in (-1, 1): m.add(box(.3, 3.2, .3).grad(*ST).move(sx * 1.35, 0, 0))
    m.add(box(3.0, .3, .3).grad(*ST).move(0, 3.2, 0))
    for i in range(7): m.add(lathe([(.045, 0), (.045, 2.9), (.09, 3.0), (0, 3.2)], 5, '3a3d52', 'metal').move(-1.02 + i * .34, 0, 0))
    for y in (.6, 2.2): m.add(box(2.5, .08, .1, '3a3d52', 'metal').move(0, y, 0))
    return m

def spikes(name):
    m = Model(name); m.add(box(2, .08, 2, DK))
    for i in range(4):
        for j in range(4): m.add(lathe([(.07, 0), (0, .7)], 5, 'c9cfe0', 'metal').move(-.75 + i * .5, .08, -.75 + j * .5))
    return m

def sarcophagus(name):
    m = Model(name); m.add(box(1.3, .25, 2.6, DK)); m.add(box(1.0, .7, 2.3).grad(*ST).move(0, .25, 0))
    m.add(extrude([(-.58, 0), (.58, 0), (.4, .22), (-.4, .22)], 2.45).grad(ST[0], ST[1]).move(0, .95, 0)); m.add(box(.12, .03, 1.2, GOLD, 'metal').move(0, 1.17, 0)); m.add(box(.6, .03, .12, GOLD, 'metal').move(0, 1.17, .3)); return m

def all_models():
    return [floor('dun_floor_a', 1), floor('dun_floor_b', 2), floor('dun_floor_rune', 3, '6fe8ff'), wall('dun_wall', None, 1), wall('dun_wall_torch', 'ff9a3a', 2), wall('dun_wall_torch_blue', '6fe8ff', 3),
            doorway('dun_doorway'), pillar('dun_pillar'), pillar('dun_pillar_broken', True), stairs('dun_stairs'), gate('dun_gate'), spikes('dun_spikes'),
            chest('chest_wood'), chest('chest_rare', '3a5aa8', GOLD, '6fe8ff'), chest('chest_epic', '6a3a9a', GOLD, 'ffd25a'), brazier('brazier'), brazier('brazier_blue', '6fe8ff'),
            altar('altar'), obelisk('rune_obelisk'), obelisk('rune_obelisk_violet', 'b884ff'), portal('portal_ring'), portal('portal_ring_violet', 'b884ff'), sarcophagus('sarcophagus')]
