"""Castle: pale stone walls, round towers with blue cone roofs, gatehouse, keep. Pieces snap on a 4 m grid."""
import math
from kitlib import *

STONE = ('8e94aa', 'dfe3ec'); DARK = '6d7289'; ROOF = ('2c4a8c', '5c8fe0'); WOOD = ('5e3b2a', '8f6142')

def merlons_line(length, y, z, n, w=.6, h=.55, t=.4):
    step = length / n; return [box(w, h, t).grad(STONE[0], STONE[1]).move(-length / 2 + step * (i + .5), y, z) for i in range(n)]

def merlons_ring(r, y, n, w=.55, h=.55):
    out = []
    for i in range(n):
        a = 360 * i / n; out.append(box(w, h, .35).grad(*STONE).move(0, y, r).rot(y=a))
    return out

def wall(name, length=4.0, h=5.0, t=1.2):
    m = Model(name); m.add(box(length, h, t).grad(*STONE)); m.add(box(length, .5, t + .3, DARK).grad(DARK, STONE[0]))
    m.add(box(length, .25, t + .3).grad(*STONE).move(0, h, 0)); m.add(merlons_line(length, h + .25, t / 2 - .05, 4)); m.add(merlons_line(length, h + .25, -t / 2 + .05, 4))
    for i in range(3): m.add(box(.5, .22, .04, DARK).move(-1.2 + i * 1.2 + (.3 if i % 2 else 0), 1.5 + i * 1.0, t / 2))
    return m

def flag(colour='e2644a', h=1.6):
    return [cyl(.03, .02, h, 5, 'd9d9e4', 'metal'), Part([[0, h, 0], [0, h - .5, 0], [.8, h - .35, .05], [.8, h - .12, .05]], [[0, 1, 2], [0, 2, 3]], mat='matte2s').grad(colour, mixc(colour, 'ffffff', .3), axis=0),
            ico(.05, 1, 'ffd25a', 'metal').move(0, h, 0)]

def tower(name, r=2.2, h=8.0, roofed=True, flagc='e2644a'):
    m = Model(name); m.add(lathe([(r * 1.12, 0), (r * 1.05, .6), (r, .7), (r, h), (r * 1.14, h + .3), (r * 1.14, h + .6)], 12, smooth=False).grad(*STONE))
    m.add(merlons_ring(r * 1.14 - .17, h + .6, 10))
    for a in (0, 120, 240):
        m.add(box(.22, .9, .1, '2a2d44').move(0, h * .45, r - .02).rot(y=a)); m.add(box(.22, .9, .1, '2a2d44').move(0, h * .75, r - .02).rot(y=a + 60))
    if roofed:
        m.add(lathe([(r * .8, 0), (r * .8, .9)], 12, smooth=False).grad(*STONE).move(0, h + .6, 0))
        m.add(lathe([(r * 1.0, 0), (r * .5, r * .9), (0, r * 2.1)], 12, smooth=False).grad(*ROOF).move(0, h + 1.5, 0)); m.add([p.move(0, h + 1.5 + r * 2.05, 0) for p in flag(flagc)])
    return m

def gatehouse(name):
    m = Model(name); h = 6.0
    for sx in (-1, 1):
        m.add(box(2.6, h + 1.5, 2.6).grad(*STONE).move(sx * 3.3, 0, 0)); m.add(box(2.9, .3, 2.9).grad(*STONE).move(sx * 3.3, h + 1.5, 0))
        for k in range(4): m.add(merlons_line(2.9, h + 1.8, 1.27, 3)[0:3:2] if False else [p.rot(y=90 * k).move(sx * 3.3, 0, 0) for p in merlons_line(2.9, h + 1.8, 1.27, 3)])
        m.add(lathe([(1.9, 0), (0, 2.6)], 4, smooth=False).grad(*ROOF).rot(y=45).move(sx * 3.3, h + 2.35, 0))
        m.add(box(.25, 1.0, .1, '2a2d44').move(sx * 3.3, h * .7, 1.3))
    m.add(box(4.0, h - 3.6, 1.6).grad(*STONE).move(0, 3.6, 0)); m.add(box(4.0, .25, 1.9).grad(*STONE).move(0, h, 0)); m.add(merlons_line(4.0, h + .25, .75, 4)); m.add(merlons_line(4.0, h + .25, -.75, 4))
    m.add(torus(2.0, .22, 12, 4, DARK, arc=180).rot(x=-90).move(0, 1.75, .82))
    for i in range(7): m.add(cyl(.05, .05, 1.6, 4, '3a3d52', 'metal').move(-1.5 + i * .5, 2.2, 0))    # raised portcullis
    for y in (2.4, 3.1): m.add(box(3.4, .08, .08, '3a3d52', 'metal').move(0, y, 0))
    m.add(extrude([(-.5, 0), (.5, 0), (.5, -1.3), (0, -1.7), (-.5, -1.3)][::-1], .05).grad('e2644a', 'ff9a7a').move(0, 5.6, .86))
    return m

def keep(name):
    m = Model(name); m.add(box(9, 9, 9).grad(*STONE)); m.add(box(9.4, .7, 9.4, DARK).grad(DARK, STONE[0])); m.add(box(9.4, .3, 9.4).grad(*STONE).move(0, 9, 0))
    for k in range(4): m.add([p.rot(y=90 * k) for p in merlons_line(9.4, 9.3, 4.5, 8)])
    for sx in (-1, 1):
        for sz in (-1, 1):
            t = tower('t', 1.5, 11.5); m.add([p.move(sx * 4.5, 0, sz * 4.5) for p in t.parts])
    m.add(box(5, 5, 5).grad(*STONE).move(0, 9.3, 0)); m.add(lathe([(3.9, 0), (1.6, 3.2), (0, 7.5)], 4, smooth=False).grad(*ROOF).rot(y=45).move(0, 14.3, 0)); m.add([p.move(0, 21.7, 0) for p in flag('ffd25a', 2.2)])
    m.add(box(2.2, 3.2, .3, WOOD[0]).grad(*WOOD).move(0, .7, 4.5)); m.add(torus(1.1, .16, 10, 4, DARK, arc=180).rot(x=-90).move(0, 3.9, 4.6))
    for x in (-2.8, 2.8):
        for y in (3.5, 6.3): m.add(box(.5, 1.3, .12, '8fd0ee').move(x, y, 4.5)); m.add(box(.66, .12, .2, DARK).move(x, y - .1, 4.52))
    for x in (-1.3, 1.3): m.add(box(.5, 1.5, .12, 'ffe6a0', 'glow').move(x, 11.0, 2.5))
    return m

def stairs(name):
    m = Model(name)
    for i in range(10): m.add(box(2.0, .5 * (i + 1), .5).grad(*STONE).move(0, 0, -2.25 + i * .5))
    return m

def banner(name, colour='4a78c8'):
    m = Model(name); m.add(lathe([(.14, 0), (.05, .12), (.04, 3.4), (0, 3.5)], 6, 'd9d9e4', 'metal')); m.add(cyl(.025, .025, 1.0, 5, 'd9d9e4', 'metal').rot(z=90).move(.95, 3.2, 0))
    m.add(extrude([(0, 0), (.85, 0), (.85, -1.5), (.425, -1.85), (0, -1.5)][::-1], .03).grad(mixc(colour, '000000', .25), mixc(colour, 'ffffff', .2)).move(.08, 3.17, 0))
    m.add(lathe([(0, 0), (.14, .14), (0, .28)], 4, 'ffd25a', 'metal', smooth=False).move(.5, 2.35, .03).scale(1, 1, .3)); return m

def all_models():
    return [wall('castle_wall'), wall('castle_wall_low', 4.0, 3.0, .9), tower('castle_tower'), tower('castle_tower_tall', 2.0, 12.0, flagc='4a78c8'), tower('castle_tower_open', 2.4, 6.0, False),
            gatehouse('castle_gatehouse'), keep('castle_keep'), stairs('castle_stairs'), banner('banner_blue'), banner('banner_red', 'c8423c')]
