"""Spell props: 3D projectiles and summoned shapes that go with the flipbook animations. Projectiles point along +Z."""
import math
import numpy as np
from kitlib import *

def spear(l, r, c, mat='glow', seg=6): return lathe([(0, 0), (r, l * .2), (r * .7, l * .6), (0, l)], seg, mat=mat, smooth=False).paint(c).rot(x=90).move(0, 0, -l / 2)

def ice_lance(name):
    m = Model(name); m.add(lathe([(0, 0), (.09, .25), (.06, .8), (0, 1.3)], 6, smooth=False).grad('3f8fe0', 'e6fbff').rot(x=90).move(0, 0, -.65)); m.add(spear(1.0, .035, 'ffffff').move(0, 0, .05))
    for i in range(4): m.add(lathe([(0, 0), (.035, .1), (0, .4)], 4, smooth=False).grad('4fb4f0', 'ffffff').rot(x=60).rot(z=i * 90 + 45).move(0, 0, -.3))
    return m

def ice_spikes(name):
    m = Model(name); rs = np.random.RandomState(3)
    for i in range(9):
        a = i * 40 + rs.rand() * 20; d = 0 if i == 0 else .35 + rs.rand() * .45; h = 1.6 if i == 0 else .5 + rs.rand() * .8
        s = lathe([(h * .16, 0), (h * .12, h * .5), (0, h)], 5, smooth=False).grad('2f6fc8', 'e6fbff')
        if i: s.rot(z=-(18 + rs.rand() * 22)).rot(y=-a)
        m.add(s.move(math.cos(math.radians(a)) * d, 0, math.sin(math.radians(a)) * d))
    return m

def earth_spikes(name):
    m = ice_spikes(name)
    for p in m.parts: p.grad('4a3220', 'c8a070')
    return m

def meteor(name):
    m = Model(name); m.add(ico(.7, 2, smooth=False).noise(.16, 4, 1.2).grad('2a1a1a', '6a3a2a')); m.add(ico(.6, 2, mat='glow', smooth=False).noise(.22, 4, 1.2).paint('ff7a2a'))
    for i in range(5): m.add(lathe([(.25 - i * .03, 0), (0, .9 + i * .2)], 6, mat='glow', smooth=False).paint('ffb04a' if i % 2 else 'ff5a1e').rot(x=-90).rot(y=(i - 2) * 9).rot(x=(i % 3 - 1) * 8).move(0, 0, -.4))
    return m

def arrow(name, glow=None):
    m = Model(name); m.add(cyl(.008, .008, .7, 5).grad('8a5a3a', 'c89a6a').rot(x=90).move(0, 0, -.35))
    m.add(lathe([(0, 0), (.022, .03), (0, .1)], 4, mat='glow' if glow else 'metal', smooth=False).paint(glow or 'e0e6f4').rot(x=90).move(0, 0, .33))
    for i in range(3): m.add(Part([[0, 0, -.34], [0, .04, -.36], [0, .04, -.24], [0, 0, -.2]], [[0, 1, 2], [0, 2, 3]], mat='matte2s').paint(glow or 'f4f6ff').rot(z=i * 120))
    if glow: m.add(spear(.9, .02, glow).move(0, 0, -.1))
    return m

def thorn(name):
    m = Model(name); m.add(lathe([(0, 0), (.04, .12), (.025, .4), (0, .6)], 5, smooth=False).grad('2a6a3a', 'c8f08a').rot(x=90).move(0, 0, -.3))
    for i in range(3): m.add(lathe([(.012, 0), (0, .1)], 4, smooth=False).paint('8fd46a').rot(x=40).rot(z=i * 120).move(0, 0, -.1))
    return m

def bolt_orb(name, c):
    m = Model(name); m.add(lathe([(0, 0), (.16, .2), (0, .4)], 6, mat='glow', smooth=False).paint(c).rot(x=90).move(0, 0, -.2)); m.add(ico(.09, 1, mat='glow').paint(mixc(c, 'ffffff', .7)))
    m.add(lathe([(.1, 0), (0, .7)], 6, mat='glow', smooth=False).paint(mixc(c, '000000', .15)).rot(x=-90).move(0, 0, -.1)); m.add(torus(.2, .012, 14, 4, mat='glow').paint(mixc(c, 'ffffff', .4)).rot(x=90)); return m

def light_sword(name, c='ffe9a0'):
    m = Model(name); m.add(blade([(.12, 0), (.14, .4), (.12, 2.4), (.001, 3.0)], .05, mat='glow').paint(c).move(0, .5, 0)); m.add(extrude([(-.5, 0), (-.4, -.08), (0, -.12), (.4, -.08), (.5, 0), (.35, .1), (0, .06), (-.35, .1)], .08, mat='glow').paint('ffd25a').move(0, .5, 0))
    m.add(cyl(.05, .05, .5, 6, mat='glow').paint(c)); m.rot(z=180).move(0, 3.5, 0); return m

def vines(name):
    m = Model(name)
    for i in range(6):
        a = i * 60; h = 1.0 + (i % 3) * .3; v = lathe([(.07, 0), (.05, h * .5), (0, h)], 6).grad('2a6a3a', '9ad86a'); y = v.v[:, 1] / h
        v.v[:, 0] += .18 * np.sin(y * 6 + i); v.v[:, 2] += .18 * np.cos(y * 6 + i) - .18; m.add(v.rot(y=a).move(math.cos(math.radians(a)) * .5, 0, math.sin(math.radians(a)) * .5))
        m.add(lathe([(.02, 0), (0, .16)], 4, smooth=False).paint('d8f8a0').rot(z=70).rot(y=a).move(math.cos(math.radians(a)) * .5, h * .5, math.sin(math.radians(a)) * .5))
    return m

def ward(name, c='6fe8ff'):
    m = Model(name); m.add(lathe([(.25, 0), (.2, .1), (.1, .2)], 8, smooth=False).grad('3e3a55', '8a86a6')); m.add(lathe([(0, 0), (.14, .3), (0, .9)], 6, mat='glow', smooth=False).paint(c).move(0, .45, 0))
    for i in range(3): m.add(torus(.3 - i * .04, .012, 18, 4, 'ffd25a', 'metal').rot(x=(i - 1) * 35).move(0, .9, 0))
    return m

def shield_dome(name, c='6fe8ff'):
    m = Model(name); n = 6; R = 1.4
    for j in range(3):
        for i in range(n * (1 if j == 2 else 2)):
            k = n * (1 if j == 2 else 2); a0, a1 = TAU_ * i / k, TAU_ * (i + 1) / k; e0, e1 = j * .5, (j + 1) * .5
            P = lambda a, e: [R * math.cos(e) * math.cos(a), R * math.sin(e), R * math.cos(e) * math.sin(a)]
            q = Part([P(a0, e0), P(a1, e0), P(a1, min(e1, 1.5)), P(a0, min(e1, 1.5))], [[0, 2, 1], [0, 3, 2]], mat='glow').paint(c); ctr = q.v.mean(0); q.v = ctr + (q.v - ctr) * .9; m.add(q)
    return m
TAU_ = 2 * math.pi

def all_models():
    return [ice_lance('proj_ice_lance'), bolt_orb('proj_fire', 'ff7a2a'), bolt_orb('proj_shadow', 'a05af0'), bolt_orb('proj_arcane', 'ff7ae8'), bolt_orb('proj_holy', 'ffd86a'),
            arrow('proj_arrow'), arrow('proj_arrow_spirit', '6fe8ff'), arrow('proj_arrow_fire', 'ff8a3a'), thorn('proj_thorn'), meteor('proj_meteor'),
            ice_spikes('spell_ice_spikes'), earth_spikes('spell_earth_spikes'), vines('spell_vines'), light_sword('spell_light_sword'), ward('spell_ward'), ward('spell_ward_violet', 'b884ff'),
            shield_dome('spell_shield_dome'), shield_dome('spell_shield_dome_gold', 'ffd86a')]
