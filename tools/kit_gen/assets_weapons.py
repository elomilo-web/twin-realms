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

# ---------------------------------------------------------------- second batch
def pole(l, y0, c=WOOD, r=.017): return lathe([(r, 0), (r, l)], 7).grad(*c).move(0, y0, 0)

def scythe(name, glow='b884ff'):
    m = Model(name); m.add(pole(1.7, -.7, ('2a1f3a', '5a4a7a')).bend(.05)); m.add(grip(.24, .022))
    b = extrude([(0, 0), (.1, .06), (.3, .08), (.55, .0), (.72, -.16), (.5, -.04), (.28, .0), (.08, -.03)], .018, mat='metal').grad('5a5d78', 'f0f4ff', axis=0)
    m.add(b.move(.03, .98, 0)); m.add(lathe([(.035, 0), (.045, .06), (.03, .12)], 8, GOLD, 'metal').move(.05, .93, 0)); m.add(ico(.03, 1, mat='glow').paint(glow).move(.05, 1.07, 0)); return m

def halberd(name):
    m = Model(name); m.add(pole(1.9, -.9)); m.add(grip(.26, .021)); m.add(blade([(.02, 0), (.04, .1), (.03, .3), (.001, .42)], .02).grad(*STEEL).move(0, 1.0, 0))
    m.add(extrude([(0, -.08), (.16, -.16), (.22, -.04), (.2, .1), (.12, .16), (0, .06)], .025, mat='metal').grad(*STEEL, axis=0).move(.015, .9, 0))
    m.add(extrude([(0, -.03), (.14, 0), (0, .03)], .02, mat='metal').grad(*STEEL, axis=0).scale(-1, 1, 1).move(-.015, .9, 0)); m.add(cyl(.028, .028, .2, 8, GOLD, 'metal').move(0, .8, 0)); return m

def glaive(name):
    m = Model(name); m.add(pole(1.6, -.8, ('3a2a3a', '6a4a5a'))); m.add(grip(.26, .021)); b = blade([(.03, 0), (.06, .2), (.05, .45), (.001, .62)], .02).grad('7fd0e8', 'f0ffff').bend(.09)
    m.add(b.move(0, .8, 0)); m.add(lathe([(.03, 0), (.04, .05), (.02, .1)], 8, GOLD, 'metal').move(0, .76, 0)); m.add(Part([[0, .8, 0], [0, .72, 0], [-.2, .55, .02], [-.16, .7, .02]], [[0, 1, 2], [0, 2, 3]], mat='matte2s').grad('4a78c8', '9ac0ff', axis=0)); return m

def lance(name):
    m = Model(name); m.add(lathe([(.02, 0), (.02, .5), (.1, .55), (.05, .7), (.0, 2.1)], 10, mat='metal').grad(*STEEL).move(0, -.45, 0)); m.add(grip(.24, .024))
    for y in (.5, .8, 1.1): m.add(torus(.06 - (y - .5) * .03, .008, 10, 4, GOLD, 'metal').move(0, y, 0))
    return m

def trident(name):
    m = Model(name); m.add(pole(1.7, -.85, ('2c5a6a', '5aa8b8'))); m.add(grip(.26, .021)); m.add(box(.34, .035, .03, GOLD, 'metal').move(0, .85, 0))
    for x, h in ((-.15, .34), (0, .5), (.15, .34)): m.add(blade([(.016, 0), (.022, h * .6), (.001, h)], .016).grad('9ad8e8', 'f0ffff').move(x, .87, 0))
    m.add(ico(.028, 1, mat='glow').paint('6fe8ff').move(0, .86, .02)); return m

def mace(name, spiked=False):
    m = Model(name); m.add(pole(.6, -.12, r=.018)); m.add(grip(.22, .024)); m.add(pommel(-.11, GOLD))
    if spiked:
        m.add(ico(.08, 1, mat='metal', smooth=False).grad(*STEEL).move(0, .55, 0))
        for i in range(8): a = i * 45; m.add(lathe([(.02, 0), (0, .07)], 5, 'f6faff', 'metal').move(0, .07, 0).rot(z=90 if i < 4 else 55).rot(y=a * 2 if i < 4 else a * 2 + 45).move(0, .55, 0))
    else:
        m.add(lathe([(.03, 0), (.05, .03), (.03, .12), (0, .16)], 8, GOLD, 'metal').move(0, .46, 0))
        for i in range(6): m.add(extrude([(0, 0), (.07, .03), (.08, .09), (0, .13)], .012, mat='metal').grad(*STEEL, axis=0).move(.02, .46, 0).rot(y=i * 60))
    return m

def greataxe(name):
    m = Model(name); m.add(pole(1.4, -.5, ('3a2a3a', '6a4a5a'), .022)); m.add(grip(.3, .026)); head = [(.0, -.12), (.26, -.3), (.36, -.14), (.38, 0), (.36, .14), (.26, .3), (0, .12)]
    for sx in (1, -1):
        h = extrude(head, .035, mat='metal').grad('6a5a80', 'e6e0ff', axis=0)
        if sx < 0: h.scale(-1, 1, 1)
        m.add(h.move(sx * .02, .7, 0)); m.add(box(.2, .02, .04, 'b884ff', 'glow').move(sx * .16, .69, 0))
    m.add(cyl(.045, .045, .3, 8, '3a3d52', 'metal').move(0, .55, 0)); m.add(lathe([(.035, 0), (0, .16)], 6, '3a3d52', 'metal').move(0, .85, 0)); return m

def greathammer(name):
    m = Model(name); m.add(pole(1.3, -.5, WOOD, .024)); m.add(grip(.3, .028)); m.add(box(.5, .3, .3, mat='metal').grad('8792b0', 'eef3ff').move(0, .68, 0))
    for sx in (-1, 1): m.add(box(.06, .36, .36, GOLD, 'metal').move(sx * .23, .65, 0)); m.add(lathe([(.1, 0), (.0, .08)], 4, GOLD, 'metal', smooth=False).rot(z=-90 * sx).move(sx * .26, .83, 0))
    m.add(box(.12, .12, .32, 'ffb04a', 'glow').move(0, .77, 0)); m.add(pommel(-.15, GOLD)); return m

def saber(name, c=('c8d0e4', 'ffffff'), gem='ff8fc0'):
    m = Model(name); b = blade([(.03, 0), (.034, .3), (.03, .62), (.001, .8)], .014).grad(*c).bend(.12); m.add(b.move(0, .13, 0)); m.add(grip(.2, .02)); m.add(pommel(-.1, GOLD, gem, .03))
    m.add(guard(.07, .12, GOLD, 'disc')); m.add(torus(.1, .008, 12, 4, GOLD, 'metal', 170).rot(z=90).rot(y=90).scale(1, 1.1, .8).move(0, 0, .0)); return m

def cutlass(name):
    m = Model(name); b = blade([(.035, 0), (.05, .3), (.055, .5), (.001, .68)], .016).grad(*STEEL).bend(.07); m.add(b.move(0, .12, 0)); m.add(grip(.19, .022, ('5a2a2a', '8a4a3a'))); m.add(pommel(-.095, GOLD))
    m.add(lathe([(.02, 0), (.09, .03), (.1, .08)], 10, GOLD, 'metal').scale(1, 1, .6).move(0, .06, 0)); return m

def kunai(name):
    m = Model(name); m.add(blade([(.004, 0), (.035, .07), (.001, .24)], .012).grad('5a5d78', 'e0e6f4').move(0, .07, 0)); m.add(cyl(.01, .01, .14, 6).grad('3a2a3a', '6a4a5a').move(0, -.07, 0)); m.add(torus(.025, .006, 10, 4, '5a5d78', 'metal').rot(x=90).move(0, -.095, 0)); return m

def chakram(name):
    m = Model(name); m.add(torus(.2, .022, 20, 4, mat='metal').grad(*STEEL, axis=0).scale(1, .35, 1).rot(x=90))
    for i in range(8): m.add(extrude([(-.03, 0), (.03, 0), (0, .07)], .008, GOLD, 'metal').move(0, .215, 0).rot(z=i * 45))
    m.add(cyl(.012, .012, .36, 6).grad(*LEATHER).move(0, -.18, 0)); m.add(ico(.025, 1, mat='glow').paint('ff9a3a').move(0, 0, .01)); return m

def crossbow(name):
    m = Model(name); m.add(box(.06, .07, .6).grad(*WOOD, axis=2).move(0, -.035, .1)); m.add(box(.04, .12, .08).grad(*WOOD).rot(x=20).move(0, -.14, -.1))
    limb = torus(.3, .014, 14, 5, arc=130).rot(y=65 - 90).grad('5a5d78', 'c8d0e4', axis=0); m.add(limb.scale(1, 1, .5).move(0, .0, .38 - limb.v[:, 2].max() * .5 + .02))
    zs = limb.v[np.abs(limb.v[:, 0]) > .25][:, 2].mean(); m.add(cyl(.003, .003, .54, 3, 'f4f0e0').rot(z=90).move(.27, .0, zs)); m.add(blade([(.006, 0), (.012, .04), (.001, .1)], .008).grad(*STEEL).rot(x=90).move(0, .045, .36))
    m.add(cyl(.006, .006, .3, 5).grad(*WOOD).rot(x=90).move(0, .045, .06)); return m

def tome(name, c='3a5aa8', glow='6fe8ff'):
    m = Model(name); m.add(box(.26, .34, .05).grad(mixc(c, '000000', .25), mixc(c, 'ffffff', .15)).move(0, -.17, 0)); m.add(box(.23, .31, .04, 'f6ead2').move(.012, -.155, 0))
    for z in (-.027, .027): m.add(box(.265, .345, .006).grad(mixc(c, '000000', .25), mixc(c, 'ffffff', .15)).move(0, -.1725, z))
    for sx in (-1, 1):
        for sy in (-1, 1): m.add(extrude([(0, 0), (.05, 0), (0, .05)], .064, GOLD, 'metal').scale(sx, sy, 1).move(sx * -.134, sy * -.174, 0))
    m.add(lathe([(0, 0), (.04, .05), (0, .1)], 4, mat='glow', smooth=False).paint(glow).scale(1, 1, .3).move(0, -.05, .034)); m.add(torus(.065, .006, 14, 4, GOLD, 'metal').rot(x=90).move(0, 0, .033)); return m

def orb_catalyst(name, glow='6fe8ff'):
    m = Model(name); m.add(ico(.075, 2, mat='glow').paint(glow)); m.add(ico(.04, 1, mat='glow').paint(mixc(glow, 'ffffff', .7)))
    m.add(torus(.12, .008, 20, 4, GOLD, 'metal').rot(x=70)); m.add(torus(.14, .006, 20, 4, GOLD, 'metal').rot(x=-40).rot(y=60))
    for i in range(3): a = i * 120; m.add(lathe([(0, 0), (.014, .02), (0, .05)], 4, mat='glow', smooth=False).paint(glow).move(.17, -.02, 0).rot(y=a))
    return m

def scepter(name, glow='ffd25a'):
    m = Model(name); m.add(lathe([(.014, 0), (.018, .5), (.03, .56)], 8, 'f6ead2', 'metal').move(0, -.12, 0)); m.add(grip(.2, .02, ('5a2a4a', '9a4a7a'))); m.add(pommel(-.1, GOLD, glow, .03))
    for i in range(4): m.add(extrude([(0, 0), (.05, .04), (.06, .14), (0, .2)], .01, GOLD, 'metal').move(.01, .44, 0).rot(y=i * 90))
    m.add(lathe([(0, 0), (.035, .06), (0, .13)], 6, mat='glow', smooth=False).paint(glow).move(0, .5, 0)); return m

def staff_crystal(name, glow='b884ff'):
    m = Model(name); m.add(lathe([(.014, 0), (.02, .3), (.018, 1.45), (.04, 1.5)], 7).grad('e8e2f4', 'ffffff').move(0, -.75, 0)); m.add(grip(.22, .024, ('4a3a9a', '7a6ad8')))
    for i in range(3): m.add(extrude([(0, 0), (.05, .06), (.035, .2), (.0, .12)], .012, GOLD, 'metal').move(.02, .74, 0).rot(y=i * 120))
    m.add(lathe([(0, 0), (.05, .1), (.04, .22), (0, .34)], 6, mat='glow', smooth=False).paint(glow).move(0, .8, 0)); m.add(torus(.1, .006, 16, 4, GOLD, 'metal').rot(x=25).move(0, .97, 0)); return m

def shield_tower(name, c='3a3d52'):
    m = Model(name); out = [(-.24, .42), (-.26, -.3), (-.14, -.46), (.14, -.46), (.26, -.3), (.24, .42), (0, .48)]
    m.add(extrude(out, .04, mat='metal').grad(mixc(c, '000000', .2), mixc(c, 'ffffff', .3))); m.add(extrude([(x * 1.06, y * 1.05) for x, y in out], .02, GOLD, 'metal').move(0, 0, -.014))
    m.add(box(.05, .7, .02, GOLD, 'metal').move(0, -.36, .025)); m.add(box(.36, .05, .02, GOLD, 'metal').move(0, .12, .025)); m.add(ico(.04, 1, mat='glow').paint('ff5a6e').move(0, .145, .04)); return m

def buckler(name):
    m = Model(name); m.add(lathe([(0, .06), (.12, .045), (.2, 0)], 12, mat='metal').grad('7a5a3a', 'e0b080', axis=1).rot(x=90)); m.add(torus(.2, .016, 14, 5, '5a5d78', 'metal').rot(x=90))
    m.add(lathe([(0, .07), (.04, .03), (.06, 0)], 8, 'f6faff', 'metal').rot(x=90).move(0, 0, .04))
    for i in range(6): m.add(ico(.012, 0, GOLD, 'metal').move(.15, 0, .03).rot(z=i * 60))
    return m

def claws(name):
    m = Model(name); m.add(box(.1, .05, .12, '3a3d52', 'metal').move(0, -.025, 0)); m.add(cyl(.045, .045, .1, 8).grad(*LEATHER).rot(x=90).move(0, 0, -.11))
    for i, x in enumerate((-.035, 0, .035)): m.add(blade([(.008, 0), (.012, .14), (.001, .3)], .01).grad(*STEEL).bend(.04, 2).move(x, .02, 0))
    return m

_all1 = all_models
def all_models():
    return _all1() + [sword('sword_frost', 1.02, .05, 'crescent', ('9ad8f8', 'ffffff'), '9af0ff', 'd9d9e4', glow_line='e6fbff', tip=.2), sword('sword_royal', 1.05, .048, 'wing', STEEL, 'ff5a6e', GOLD, glow_line='ffd25a'),
            sword('claymore_storm', 1.5, .08, 'cross', ('6a78c8', 'e6ecff'), 'b0c0ff', '5a5d78', glow_line='c8d4ff', tip=.24, grip_l=.44, gw=.22), sword('shortsword', .6, .045, 'cross', gw=.1),
            saber('saber'), saber('saber_moon', ('8a7ad8', 'f0e8ff'), 'b884ff'), cutlass('cutlass'), kunai('kunai'), claws('claws'), chakram('chakram'),
            scythe('scythe_reaper'), halberd('halberd'), glaive('glaive_tide'), lance('lance'), trident('trident'), mace('mace'), mace('morningstar', True), greataxe('greataxe_void'), greathammer('greathammer'),
            bow('longbow_gilded', ('8a5a3a', 'c89a6a'), GOLD, 'ffd25a'), crossbow('crossbow'), tome('tome_azure'), tome('tome_crimson', 'a8323c', 'ffb04a'), orb_catalyst('orb_catalyst'), orb_catalyst('orb_catalyst_ember', 'ff9a3a'),
            scepter('scepter'), staff_crystal('staff_crystal'), staff_crystal('staff_verdant', '8fffb0'), shield_tower('shield_tower'), buckler('buckler')]
import numpy as np
