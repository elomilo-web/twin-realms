"""Village: timber-framed houses with bright roofs, and street props."""
import math
import numpy as np
from kitlib import *

WOOD = ('5e3b2a', '8f6142'); PLASTER = ('e6d8c2', 'fbf3e4'); STONE = ('7d8298', 'b7bccb')

def roof(w, d, h, colour, y, over=.45):
    lo, hi = mixc(colour, '20143a', .35), mixc(colour, 'ffffff', .18)
    r = extrude([(-w / 2 - over, 0), (w / 2 + over, 0), (0, h)], d + over * 2).grad(lo, hi).move(0, y, 0)
    ridge = box(.16, .12, d + over * 2 + .1, mixc(colour, '20143a', .5)).move(0, y + h - .06, 0)
    eaves = [box(.1, .1, d + over * 2 + .06, WOOD[0]).rot(z=0).move(sx * (w / 2 + over - .02), y - .04, 0) for sx in (-1, 1)]
    return [r, ridge] + eaves

def window(w=.7, h=.9, lit=False):
    out = [box(w + .14, h + .14, .08, WOOD[0]), box(w, h, .1, 'ffe6a0' if lit else '8fd0ee', 'glow' if lit else 'matte').move(0, .07, 0),
           box(.05, h, .12, WOOD[0]).move(0, .07, 0), box(w, .05, .12, WOOD[0]).move(0, .07 + h / 2, 0), box(w + .3, .07, .2, WOOD[1]).move(0, -.04, .05)]
    return out

def door(w=.95, h=1.8):
    return [box(w + .2, h + .1, .1, WOOD[0]), box(w, h, .14, '7a4a2c').grad('6a3f26', '99623a'), box(.06, .06, .2, 'ffd25a', 'metal').move(w * .32, h * .5, 0)]

def put(parts, x, y, z, ry=0):
    return [p.rot(y=ry).move(x, y, z) for p in parts]

def house(name, w=5.0, d=4.2, floors=1, colour='e2644a', lit=False, chimney=True, shop=None):
    m = Model(name); fh = 2.5; y = .4
    m.add(box(w + .25, .4, d + .25, STONE[0]).grad(*STONE))
    for f in range(floors):
        o = .3 * f; ww, dd = w + o, d + o
        m.add(box(ww, fh, dd).grad(*PLASTER).move(0, y, 0))
        for sx in (-1, 1):
            for sz in (-1, 1): m.add(box(.2, fh, .2, WOOD[0]).grad(*WOOD).move(sx * ww / 2, y, sz * dd / 2))
        for yy in (0, fh - .18):
            for sz in (-1, 1): m.add(box(ww + .2, .18, .2, WOOD[0]).move(0, y + yy, sz * dd / 2))
            for sx in (-1, 1): m.add(box(.2, .18, dd + .2, WOOD[0]).move(sx * ww / 2, y + yy, 0))
        for sx in (-1, 1):   # diagonal braces on the front
            m.add(box(.12, 1.2, .1, WOOD[0]).rot(z=sx * 40).move(sx * (ww / 2 - .5), y + fh - 1.25, dd / 2 + .03))
        z = dd / 2 + .04
        if f == 0:
            m.add(put(door(), -w * .22 if w > 4 else 0, y, z))
            if w > 4: m.add(put(window(lit=lit), w * .22, y + .95, z))
        else:
            for x in (-ww * .25, ww * .25): m.add(put(window(lit=lit), x, y + .85, z))
        m.add(put(window(.6, .8, lit), -ww / 2 - .04, y + .95, 0, -90)); m.add(put(window(.6, .8, lit), ww / 2 + .04, y + .95, 0, 90))
        y += fh
    ww, dd = w + .3 * (floors - 1), d + .3 * (floors - 1); rh = ww * .42
    m.add(roof(ww, dd, rh, colour, y))
    for sz in (-1, 1):   # gable ends
        m.add(extrude([(-ww / 2, 0), (ww / 2, 0), (0, rh - .2)], .12).grad(*PLASTER).move(0, y, sz * (dd / 2 - .06)))
        m.add(box(.14, rh - .25, .16, WOOD[0]).move(0, y, sz * dd / 2))
    if chimney:
        m.add(box(.6, rh + .5, .6).grad(*STONE).move(ww * .26, y + .1, -dd * .2)); m.add(box(.74, .14, .74, STONE[0]).move(ww * .26, y + rh + .56, -dd * .2))
    if shop:
        z = d / 2
        m.add(extrude([(0, 0), (1.3, -.45), (1.3, -.38), (0, .08)], w * .8).rot(y=-90).move(0, 2.55, z + .02).grad(shop, mixc(shop, 'ffffff', .5), axis=0))
        m.add(box(w * .7, .9, .5).grad(*WOOD).move(w * .08, .4, z + .5))
        m.add(cyl(.02, .02, .7, 4, WOOD[0]).rot(z=90).move(-w / 2 + .1, 3.1, z + .5)); m.add(box(.7, .5, .06, 'f4e3b8').move(-w / 2 - .25, 2.5, z + .5))
    return m

def well(name):
    m = Model(name); m.add(lathe([(1.0, 0), (1.0, .8), (.78, .8), (.78, .25)], 12, smooth=False).grad(*STONE)); m.add(cyl(.78, .78, .02, 12, '3f8fd0').move(0, .3, 0))
    for sx in (-1, 1): m.add(box(.14, 1.7, .14).grad(*WOOD).move(sx * .9, .8, 0))
    m.add(roof(1.6, 1.2, .6, '4a78c8', 2.45, .25)); m.add(cyl(.07, .07, 1.7, 6, WOOD[1]).rot(z=90).move(.85, 2.0, 0)); m.add(lathe([(.16, 0), (.2, .25), (.2, .28)], 8).grad(*WOOD).move(0, 1.2, 0)); return m

def stall(name, c1='e2644a', c2='fff1d8'):
    m = Model(name)
    for sx in (-1, 1):
        for sz in (-1, 1): m.add(box(.1, 2.3 if sz < 0 else 2.0, .1).grad(*WOOD).move(sx * 1.2, 0, sz * .7))
    m.add(box(2.6, .1, 1.5).grad(*WOOD).move(0, .85, 0)); m.add(box(2.5, .8, .06, WOOD[0]).move(0, .05, .7))
    for i in range(6):
        x = -1.25 + i * .5 + .25; m.add(Part([[-.25, 2.32, -.85], [.25, 2.32, -.85], [.25, 2.0, .95], [-.25, 2.0, .95], [-.25, 1.82, .97], [.25, 1.82, .97]], [[0, 2, 1], [0, 3, 2], [3, 5, 2], [3, 4, 5]], mat='matte2s').paint(c1 if i % 2 else c2).move(x, 0, 0))
    for i, c in enumerate(('ff7a4a', '8fd46a', 'ffd25a', 'c86ad8')): m.add(ico(.14, 1, c).move(-.8 + i * .5, 1.05, .15))
    return m

def fence(name): 
    m = Model(name)
    for x in (-1, 0, 1): m.add(lathe([(.07, 0), (.07, .95), (0, 1.05)], 5).grad(*WOOD).move(x, 0, 0))
    for y in (.35, .7): m.add(box(2.1, .09, .05).grad(*WOOD, axis=0).move(0, y, 0))
    return m

def lamp(name):
    m = Model(name); iron = '3a3d52'
    m.add(lathe([(.2, 0), (.12, .15), (.06, .3), (.05, 2.6)], 8, iron, 'metal')); m.add(torus(.3, .025, 10, 5, iron, 'metal', 180).rot(x=90).rot(y=0).move(.3, 2.6, 0))
    m.add(lathe([(.03, 0), (.14, .08), (.11, .36), (.16, .4), (0, .52)], 6, iron, 'metal', smooth=False).move(.6, 2.05, 0)); m.add(lathe([(.1, 0), (.09, .26)], 6, mat='glow').paint('ffd98a').move(.6, 2.14, 0)); return m

def signpost(name):
    m = Model(name); m.add(lathe([(.08, 0), (.07, 2.2), (0, 2.3)], 6).grad(*WOOD))
    for i, (a, y) in enumerate(((0, 1.8), (150, 1.45))):
        m.add(extrude([(0, 0), (.9, 0), (1.1, .14), (.9, .28), (0, .28)], .05).grad('b98a5a', 'e0b684', axis=0).move(.05, y, 0).rot(y=a))
    return m

def barrel(name): 
    m = Model(name); m.add(lathe([(.3, 0), (.38, .25), (.4, .5), (.38, .75), (.3, 1.0)], 10).grad(*WOOD))
    for y in (.18, .82): m.add(cyl(.375 if True else 0, .375, .06, 10, '4a4d62', 'metal', caps=False).scale(1.0).move(0, y, 0))
    return m

def crate(name):
    m = Model(name); m.add(box(.9, .9, .9).grad('9a6c44', 'c8996a'))
    for sx in (-1, 1):
        for sz in (-1, 1): m.add(box(.1, .92, .1, WOOD[0]).move(sx * .42, 0, sz * .42))
    for y in (0, .84): m.add(box(.96, .08, .96, WOOD[0]).move(0, y, 0))
    return m

def bench(name):
    m = Model(name); m.add(box(1.6, .08, .45).grad(*WOOD).move(0, .42, 0)); m.add(box(1.6, .35, .06).grad(*WOOD).rot(x=-10).move(0, .62, -.22))
    for sx in (-1, 1): m.add(box(.08, .42, .4, WOOD[0]).move(sx * .68, 0, 0))
    return m

def bridge(name):
    m = Model(name); n = 9; L = 8.0; W = 3.0
    for i in range(n):
        t0 = i / n; x0 = -L / 2 + L * t0; y = lambda t: math.sin(t * math.pi) * 1.1
        ya, yb = y(t0), y(t0 + 1 / n); seg = L / n; ang = math.degrees(math.atan2(yb - ya, seg))
        m.add(box(seg * 1.06, .5, W).grad(*STONE).rot(z=ang).move(x0 + seg / 2, (ya + yb) / 2 - .1, 0))
        for sz in (-1, 1): m.add(box(seg * 1.06, .5, .25, STONE[0]).grad(STONE[0], 'cfd3dc').rot(z=ang).move(x0 + seg / 2, (ya + yb) / 2 + .4, sz * (W / 2 - .12)))
    for sx in (-1, 1):
        m.add(box(.9, 1.6, W + .3).grad(*STONE).move(sx * 1.6, -1.0, 0))
        for sz in (-1, 1): m.add(lathe([(.2, 0), (.2, .9), (.28, .95), (0, 1.25)], 6, smooth=False).grad(*STONE).move(sx * (L / 2 - .2), .1, sz * (W / 2 - .12)))
    return m

def windmill(name):
    m = Model(name); m.add(lathe([(2.0, 0), (1.9, .5), (1.35, 6.0)], 10, smooth=False).grad(*PLASTER)); m.add(lathe([(2.1, 0), (2.05, .6)], 10, smooth=False).grad(*STONE))
    m.add(lathe([(1.7, 0), (0, 2.0)], 10, smooth=False).grad('8a3a2e', 'e2644a').move(0, 6.0, 0)); m.add(put(door(), 0, 0, 1.93)); m.add(put(window(.6, .8), 0, 3.4, 1.62))
    hub = [cyl(.18, .18, .9, 8, WOOD[0]).rot(x=90).move(0, 0, 0)]
    for i in range(4):
        hub.append(box(.12, 3.6, .08, WOOD[0]).move(0, .2, .85).rot(z=i * 90 + 20))
        hub.append(Part([[.1, .7, .86], [.95, .7, .9], [.95, 3.7, .9], [.1, 3.7, .86]], [[0, 1, 2], [0, 2, 3]], mat='matte2s').paint('f6ead2').rot(z=i * 90 + 20))
    m.add([p.move(0, 5.6, 1.1) for p in hub]); return m

def waystone(name):
    m = Model(name); m.add(lathe([(1.3, 0), (1.3, .2), (1.0, .2), (1.0, .4)], 8, smooth=False).grad(*STONE))
    m.add(lathe([(.5, 0), (.36, 2.6), (0, 3.2)], 4, smooth=False).grad('5d6480', 'c9cfe0').rot(y=45).move(0, .4, 0))
    for y in (1.0, 1.7, 2.4): m.add(lathe([(.5 - (y - .0) * .054 + .02, 0), (.5 - (y + .12) * .054 + .02, .12)], 4, mat='glow', smooth=False, caps=False).paint('6fe8ff').rot(y=45).move(0, .4 + y, 0))
    m.add(lathe([(0, 0), (.16, .22), (0, .44)], 4, mat='glow', smooth=False).paint('6fe8ff').move(0, 4.0, 0))
    m.add(torus(.42, .03, 14, 4, 'ffd25a', 'metal').rot(x=20).move(0, 4.22, 0)); return m

def all_models():
    return [house('house_cottage', 4.6, 4.0, 1, 'e2644a'), house('house_town_blue', 5.2, 4.4, 2, '4a78c8'), house('house_town_teal', 4.0, 4.6, 2, '3fae9a', chimney=False),
            house('house_shop', 5.4, 4.2, 2, 'd9a23a', shop='e2644a'), house('house_inn_lit', 7.0, 5.0, 2, 'b8423c', lit=True), house('house_hut', 3.2, 3.0, 1, '8a5fb8', chimney=False),
            windmill('windmill'), well('well'), stall('market_stall_red'), stall('market_stall_blue', '4a78c8'), bridge('bridge_stone'), waystone('waystone'),
            lamp('street_lamp'), signpost('signpost'), fence('fence'), bench('bench'), barrel('barrel'), crate('crate')]
