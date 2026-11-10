"""Weapons and shields: original anime-fantasy designs. The hand holds the grip at the origin; the business end points up (+Y).
Blades are 'metal' (the character shader gives them bright bands); crystals and runes glow."""
import math
from kitlib import *

STEEL = ('aab6cc', 'f6faff'); GOLD = 'ffd25a'; LEATHER = ('3a2a3a', '6a4a5a'); WOOD = ('5e3b2a', '9a6a44')

def grip(l=.24, r=.022, c=LEATHER):
    out = [cyl(r, r, l, 8).grad(*c).move(0, -l / 2, 0)]
    for i in range(4): out.append(cyl(r + .004, r + .004, .012, 8, mixc(c[1], 'ffffff', .2), caps=False).move(0, -l / 2 + .03 + i * (l - .06) / 3, 0))
    return out

def pommel(y, c=GOLD, gem=None, r=.035):
    out = [lathe([(0, 0), (r, r * .8), (r * .6, r * 1.6), (r * .5, r * 2)], 8, c, 'metal').move(0, y - r * 2, 0)]
    if gem: out.append(ico(r * .6, 1, mat='glow').paint(gem).move(0, y - r * 1.1, 0))
    return out

def guard(w, y, c=GOLD, style='cross', gem=None):
    out = []
    if style == 'cross':
        out.append(extrude([(-w, .0), (-w * .8, -.02), (0, -.035), (w * .8, -.02), (w, .0), (w, .03), (w * .85, .02), (0, .04), (-w * .85, .02), (-w, .03)], .05, c, 'metal').move(0, y, 0))
    elif style == 'wing':
        for sx in (-1, 1):
            out.append(extrude([(0, -.03), (w * .5, -.05), (w, .06), (w * .9, .1), (w * .55, .02), (0, .04)][::sx], .04, c, 'metal').scale(sx, 1, 1).move(0, y, 0))
    elif style == 'disc':
        out.append(cyl(w, w, .016, 12, c, 'metal').move(0, y, 0))
    elif style == 'crescent':
        out.append(torus(w, .02, 12, 5, c, 'metal', 180).rot(x=-90).scale(1, .6, 1).move(0, y - .01, 0))
    if gem: out.append(lathe([(0, 0), (.028, .035), (0, .07)], 4, mat='glow', smooth=False).paint(gem).scale(1, 1, 1.4).move(0, y - .01, 0))
    return out

def sword(name, length=.95, width=.045, style='cross', blade_c=STEEL, gem='6fe8ff', gcol=GOLD, glow_line=None, curve=0, tip=.14, grip_l=.24, gw=.13):
    m = Model(name); y0 = grip_l / 2 + .02
    b = blade([(width * .9, 0), (width, length * .12), (width * .92, length - tip), (width * .5, length - tip * .45), (.002, length)], width * .32).grad(*blade_c)
    if curve: b.bend(curve)
    m.add(b.move(0, y0, 0))
    if glow_line: m.add(box(width * .28, length * .62, width * .36, glow_line, 'glow').bend(curve * .5 if curve else 0).move(0, y0 + length * .06, 0))
    m.add(grip(grip_l)); m.add(pommel(-grip_l / 2, gcol, gem)); m.add(guard(gw, y0 - .02, gcol, style, gem)); return m

def rapier(name):
    m = Model(name); m.add(blade([(.012, 0), (.011, .7), (.001, 1.0)], .012).grad(*STEEL).move(0, .12, 0)); m.add(grip(.2, .018)); m.add(pommel(-.1, GOLD, 'ff8fc0', .028))
    m.add(lathe([(.02, 0), (.075, .03), (.085, .07)], 10, GOLD, 'metal').move(0, .09, 0)); m.add(torus(.11, .008, 12, 4, GOLD, 'metal', 180).rot(z=90).scale(.75, 1, 1).move(.0, .0, .0).rot(y=90).move(0, .0, .0)); return m

def dagger(name): return sword(name, .34, .034, 'crescent', gem='b884ff', tip=.1, grip_l=.16, gw=.06)

def greatsword(name):
    m = sword(name, 1.45, .085, 'wing', ('8792b0', 'eef3ff'), 'ffd25a', glow_line='ffd25a', tip=.26, grip_l=.42, gw=.24); return m

def katana(name):
    m = Model(name); b = blade([(.02, 0), (.02, .8), (.012, .9), (.001, .95)], .012).grad(*STEEL).bend(.09); m.add(b.move(0, .16, 0))
    m.add(cyl(.02, .018, .3, 8).grad('2a1f3a', '4a3566').move(0, -.15, 0))
    for i in range(6): m.add(lathe([(.023, 0), (.023, .018)], 4, 'e8e2f4', caps=False, smooth=False).rot(y=45).move(0, -.12 + i * .045, 0))
    m.add(guard(.055, .155, '3a3d52', 'disc')); m.add(cyl(.022, .022, .03, 8, GOLD, 'metal').move(0, .165, 0)); m.add(cyl(.021, .021, .02, 8, GOLD, 'metal').move(0, -.17, 0)); return m

def axe(name, double=False):
    m = Model(name); m.add(lathe([(.02, 0), (.018, .95), (.026, 1.0)], 7).grad(*WOOD).move(0, -.2, 0)); m.add(grip(.2, .024))
    head = [(.0, -.09), (.2, -.2), (.26, -.1), (.28, 0), (.26, .1), (.2, .2), (0, .09)]
    for sx in ((1, -1) if double else (1,)):
        h = extrude(head, .03, mat='metal').grad(STEEL[0], STEEL[1], axis=0)
        if sx < 0: h.scale(-1, 1, 1)
        m.add(h.move(sx * .015, .62, 0))
    m.add(cyl(.04, .04, .22, 8, GOLD, 'metal').move(0, .51, 0)); m.add(lathe([(.03, 0), (0, .1)], 6, GOLD, 'metal').move(0, .8, 0)); return m

def hammer(name):
    m = Model(name); m.add(lathe([(.022, 0), (.02, 1.0)], 7).grad(*WOOD).move(0, -.2, 0)); m.add(grip(.24, .026))
    m.add(box(.34, .2, .2, mat='metal').grad(*STEEL).move(0, .72, 0))
    for sx in (-1, 1): m.add(box(.05, .24, .24, GOLD, 'metal').move(sx * .15, .7, 0))
    m.add(box(.08, .08, .22, '6fe8ff', 'glow').move(0, .78, 0)); m.add(pommel(-.2, GOLD)); return m

def spear(name):
    m = Model(name); m.add(lathe([(.016, 0), (.016, 1.9)], 6).grad(*WOOD).move(0, -.9, 0)); m.add(blade([(.02, 0), (.055, .1), (.045, .22), (.001, .4)], .02).grad(*STEEL).move(0, 1.0, 0))
    m.add(lathe([(.03, 0), (.022, .06), (.03, .1)], 8, GOLD, 'metal').move(0, .93, 0)); m.add(Part([[0, .98, 0], [0, .9, 0], [.22, .82, .02], [.2, .96, .02]], [[0, 1, 2], [0, 2, 3]], mat='matte2s').grad('c8423c', 'ff7a6a', axis=0))
    m.add(lathe([(0, 0), (.018, .05)], 6, GOLD, 'metal').move(0, -.95, 0)); return m

def bow(name, c=WOOD, tipc=GOLD, glow=None):
    m = Model(name); R = .62
    limb = torus(R, .016, 16, 5, arc=140).rot(y=70).rot(x=90).scale(.45, 1, 1).grad(*c)   # arc in the XY plane, belly toward +X
    limb.move(-limb.v[:, 0].max() + .016, 0, 0); m.add(limb)
    lo, hi = limb.v[:, 1].min(), limb.v[:, 1].max(); xs = limb.v[limb.v[:, 1] > hi - .02][:, 0].mean()
    m.add(cyl(.003, .003, hi - lo - .02, 3, 'f4f0e0').move(xs, lo + .01, 0)); m.add(cyl(.022, .022, .16, 8).grad(*LEATHER).move(limb.v[:, 0].max() - .016, -.08, 0))
    for y in (lo, hi): m.add(ico(.022, 1, tipc, 'metal').move(xs, y, 0))
    if glow: m.add(lathe([(0, 0), (.02, .03), (0, .06)], 4, mat='glow', smooth=False).paint(glow).move(limb.v[:, 0].max() + .02, -.03, 0))
    return m

def staff(name, glow='6fe8ff', c=WOOD):
    m = Model(name); m.add(lathe([(.014, 0), (.02, .3), (.018, 1.5), (.03, 1.6)], 7).grad(*c).bend(.06).move(0, -.8, 0)); m.add(grip(.22, .024))
    m.add(torus(.13, .016, 14, 5, GOLD, 'metal', 250).rot(y=-125).rot(x=90).rot(z=-90).move(.06, .95, 0))
    m.add(lathe([(0, 0), (.055, .08), (0, .2)], 6, mat='glow', smooth=False).paint(glow).move(.06, .86, 0))
    for i in range(3): m.add(ico(.014, 0, mat='glow').paint(glow).move(.06 + math.cos(i * 2.1) * .2, .96 + math.sin(i * 2.1) * .2, 0))
    m.add(lathe([(0, 0), (.02, .06)], 6, GOLD, 'metal').move(0, -.86, 0)); return m

def wand(name, glow='ff8fc0'):
    m = Model(name); m.add(lathe([(.012, 0), (.016, .08), (.007, .42)], 7).grad('e8e2f4', 'ffffff').move(0, -.08, 0)); m.add(cyl(.018, .016, .1, 7, GOLD, 'metal').move(0, -.09, 0))
    for a in range(0, 360, 72): m.add(lathe([(0, 0), (.014, .02), (0, .07)], 4, mat='glow', smooth=False).paint(glow).rot(z=a).move(0, .38, 0))
    m.add(ico(.02, 1, mat='glow').paint(mixc(glow, 'ffffff', .5)).move(0, .38, 0)); return m

def shield_round(name, c='3f66b0'):
    m = Model(name); m.add(lathe([(0, .07), (.2, .05), (.34, 0)], 14).grad(mixc(c, '000000', .2), mixc(c, 'ffffff', .25), axis=1).rot(x=90))
    m.add(torus(.34, .022, 16, 5, GOLD, 'metal').rot(x=90)); m.add(lathe([(0, .05), (.07, .03), (.09, 0)], 10, GOLD, 'metal').rot(x=90).move(0, 0, .055))
    for a in range(0, 360, 90): m.add(lathe([(0, 0), (.03, .08), (0, .2)], 4, 'f6faff', 'metal', smooth=False).scale(1, 1, .3).move(0, .1, .05).rot(z=a))
    return m

def shield_kite(name, c='c8423c'):
    m = Model(name); out = [(-.26, .3), (-.28, .05), (-.2, -.25), (0, -.5), (.2, -.25), (.28, .05), (.26, .3), (0, .36)]
    m.add(extrude(out, .035).grad(mixc(c, '000000', .25), mixc(c, 'ffffff', .2))); m.add(extrude([(x * 1.07, y * 1.06) for x, y in out], .02, GOLD, 'metal').move(0, 0, -.012))
    m.add(lathe([(0, 0), (.08, .1), (0, .26)], 4, GOLD, 'metal', smooth=False).scale(1, 1, .25).move(0, -.12, .025)); m.add(ico(.03, 1, mat='glow').paint('6fe8ff').move(0, .01, .035)); return m

def all_models():
    return [sword('sword_knight'), sword('sword_azure', 1.0, .042, 'wing', ('6fa8e8', 'e6f6ff'), '6fe8ff', 'd9d9e4', glow_line='9af0ff'),
            sword('sword_ember', .9, .05, 'crescent', ('5a2030', 'ff9a6a'), 'ff9a3a', '3a3d52', glow_line='ffb04a', curve=.05),
            sword('sword_verdant', .88, .04, 'cross', ('7fd0a0', 'f0fff4'), '8fffb0', GOLD), sword('sword_shadow', .98, .046, 'wing', ('3a2a5e', 'c8a8ff'), 'b884ff', '5a5d78', glow_line='b884ff'),
            greatsword('greatsword_sun'), katana('katana'), rapier('rapier'), dagger('dagger'), axe('axe'), axe('axe_double', True), hammer('warhammer'), spear('spear'),
            bow('bow_wood'), bow('bow_spirit', ('e8e2f4', 'ffffff'), GOLD, '6fe8ff'), staff('staff_azure'), staff('staff_ember', 'ff9a3a', ('3a2a3a', '6a4a5a')), wand('wand_star'), wand('wand_frost', '9af0ff'),
            shield_round('shield_round'), shield_kite('shield_kite')]
