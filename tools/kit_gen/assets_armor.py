"""Head and shoulder pieces that are not skinned: attach them to a bone (head, shoulder). The origin is the centre of the head
(head radius about 0.12 m) or the top of the shoulder. Fitted body armour comes from character packs, not from here."""
import math
from kitlib import *

STEEL = ('8792b0', 'f0f4ff'); GOLD = 'ffd25a'

def helm_knight(name):
    m = Model(name); m.add(lathe([(.14, -.1), (.145, 0), (.13, .08), (.07, .14), (0, .155)], 12, mat='metal').grad(*STEEL))
    m.add(box(.2, .03, .04, '20203a').move(0, .0, .125)); m.add(box(.03, .14, .04, GOLD, 'metal').move(0, -.06, .135)); m.add(torus(.146, .012, 14, 4, GOLD, 'metal').move(0, -.1, 0))
    m.add(extrude([(0, 0), (.03, .1), (-.06, .2), (-.16, .16), (-.1, .05)], .03).grad('c8423c', 'ff8a7a').rot(y=90).move(0, .13, -.02)); return m

def helm_winged(name):
    m = Model(name); m.add(torus(.128, .016, 14, 5, GOLD, 'metal').scale(1, 1.6, 1).move(0, .03, 0)); m.add(lathe([(0, 0), (.022, .03), (0, .06)], 4, mat='glow', smooth=False).paint('6fe8ff').move(0, .01, .135))
    for sx in (-1, 1):
        w = extrude([(0, 0), (.05, .02), (.16, .14), (.1, .1), (.13, .2), (.06, .12), (.06, .22), (0, .1)], .012, 'f6faff', 'metal').rot(y=90 * sx - 90 + (20 * sx)).move(sx * .135, .0, -.02); m.add(w)
    return m

def hat_mage(name, c='4a3a9a'):
    m = Model(name); m.add(lathe([(.3, .04), (.29, .05), (.13, .08), (.1, .2), (.04, .36), (0, .46)], 12).grad(mixc(c, '000000', .2), mixc(c, 'ffffff', .25)).bend(.09, 2))
    m.add(lathe([(.3, .04), (.0, .03)], 12, mixc(c, '000000', .35))); m.add(torus(.128, .014, 12, 4, GOLD, 'metal').move(0, .1, 0)); m.add(lathe([(0, 0), (.03, .04), (0, .08)], 4, mat='glow', smooth=False).paint('ffd25a').move(0, .08, .135)); return m

def crown(name):
    m = Model(name); m.add(lathe([(.125, 0), (.13, .05)], 12, GOLD, 'metal', caps=False))
    for i in range(6):
        a = i * 60; m.add(lathe([(.02, 0), (0, .07 if i % 2 else .1)], 4, GOLD, 'metal', smooth=False).move(0, .05, .127).rot(y=a))
        if i % 2 == 0: m.add(ico(.014, 1, mat='glow').paint('ff5a6e' if i else '6fe8ff').move(0, .025, .133).rot(y=a))
    return m

def hood(name, c='3a6a5a'):
    m = Model(name); m.add(lathe([(.16, -.14), (.165, -.02), (.15, .1), (.08, .17), (0, .18)], 10).grad(mixc(c, '000000', .25), mixc(c, 'ffffff', .15)).scale(1, 1, 1.12).move(0, 0, -.02))
    m.add(lathe([(.0, 0), (.06, .0), (.02, .14)], 6).grad(c, mixc(c, 'ffffff', .1)).rot(x=-110).move(0, .14, -.14)); return m

def pauldron(name, c=STEEL, spike=False, trim=GOLD):
    m = Model(name)
    for i, (r, y) in enumerate(((.11, 0), (.095, -.035), (.08, -.07))):
        m.add(lathe([(0, .05), (r * .7, .04), (r, 0)], 10, mat='metal').grad(*c).scale(1.15, 1, 1).move(i * .012, y, 0)); m.add(torus(r, .006, 12, 4, trim, 'metal').scale(1.15, 1, 1).move(i * .012, y, 0))
    if spike: m.add(lathe([(.025, 0), (0, .12)], 6, trim, 'metal').move(0, .04, 0))
    else: m.add(ico(.02, 1, mat='glow').paint('6fe8ff').move(0, .05, 0))
    return m

def cape(name, c='b8423c'):
    m = Model(name); n = 6; w = .5; pts = []; f = []
    for j in range(5):
        y = -j * .26; ww = w * (1 + j * .12)
        for i in range(n + 1): t = i / n; pts.append([(t - .5) * ww, y, .03 * math.sin(t * 9 + j) * j * .5 + j * .035])
    for j in range(4):
        for i in range(n):
            a = j * (n + 1) + i; f += [[a, a + n + 1, a + 1], [a + 1, a + n + 1, a + n + 2]]
    p = Part(pts, f, mat='matte2s', smooth=True).grad(mixc(c, '000000', .3), mixc(c, 'ffffff', .1)); m.add(p)
    for sx in (-1, 1): m.add(ico(.03, 1, GOLD, 'metal').move(sx * .22, 0, 0))
    return m

def all_models():
    return [helm_knight('helm_knight'), helm_winged('circlet_winged'), hat_mage('hat_mage'), hat_mage('hat_mage_red', 'a8323c'), crown('crown'), hood('hood_ranger'),
            pauldron('pauldron_plate'), pauldron('pauldron_spiked', ('3a3d52', '9aa0c0'), True, 'c8423c'), cape('cape_red'), cape('cape_blue', '3f66b0')]
