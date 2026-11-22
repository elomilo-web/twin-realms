"""Icons and menu pieces for Twin Realms, in the same painted anime-fantasy style as the textures.
Writes  assets/ui/icons/skill/<skill id>.png   one per skill in data/skills.json (glyph from its kind, colour from its damage type)
        assets/ui/icons/item/<id>.png          weapons, armour slots, potions, materials, misc
        assets/ui/frames/*.png                 panels, buttons, slots, bars, rarity frames, markers (9-slice margins in docs/kit/README.md)
        docs/kit/icons.png, docs/kit/ui_mock.png   previews
Run from the repo root:  python tools/kit_gen/ui_gen.py"""
import json, math, os
from PIL import Image, ImageDraw, ImageFilter, ImageChops

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
N, SS = 128, 4          # icon size, supersample
def hx(h, a=255): h = h.lstrip('#'); return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), a)
def mix(a, b, t): return tuple(int(a[i] * (1 - t) + b[i] * t) for i in range(3)) + (255,)
ELEM = {'fire': 'f0702a', 'frost': '58bff0', 'lightning': '8f9cff', 'shadow': '9a58e8', 'holy': 'f2c24a', 'poison': '6cc846', 'physical': '8a93ab'}

# ---- glyphs: lists of shapes in a 0..1 box (y down). P polygon, C disc, R ring, L polyline, A arc (degrees, clockwise from east)
def rot(pr, deg, c=(.5, .5)):
    a = math.radians(deg); ca, sa = math.cos(a), math.sin(a)
    def r(p): x, y = p[0] - c[0], p[1] - c[1]; return (c[0] + x * ca - y * sa, c[1] + x * sa + y * ca)
    out = []
    for s in pr:
        if s[0] == 'P': out.append(('P', [r(p) for p in s[1]]))
        elif s[0] == 'L': out.append(('L', [r(p) for p in s[1]], s[2]))
        elif s[0] == 'C': out.append(('C',) + r((s[1], s[2])) + (s[3],))
        elif s[0] == 'R': out.append(('R',) + r((s[1], s[2])) + (s[3], s[4]))
        elif s[0] == 'A': out.append(('A',) + r((s[1], s[2])) + (s[3], s[4] + deg, s[5] + deg, s[6]))
    return out
def move(pr, dx, dy, k=1.0):
    def m(p): return (.5 + (p[0] - .5) * k + dx, .5 + (p[1] - .5) * k + dy)
    out = []
    for s in pr:
        if s[0] == 'P': out.append(('P', [m(p) for p in s[1]]))
        elif s[0] == 'L': out.append(('L', [m(p) for p in s[1]], s[2] * k))
        elif s[0] == 'C': out.append(('C',) + m((s[1], s[2])) + (s[3] * k,))
        elif s[0] == 'R': out.append(('R',) + m((s[1], s[2])) + (s[3] * k, s[4] * k))
        elif s[0] == 'A': out.append(('A',) + m((s[1], s[2])) + (s[3] * k, s[4], s[5], s[6] * k))
        elif s[0] == 'E': out.append(('E',) + m((s[1], s[2])) + (s[3] * k, s[4] * k, s[5] * k))
    return out
def star(cx, cy, r, n=4, inner=.38, a0=-90): return ('P', [(cx + math.cos(math.radians(a0 + i * 180 / n)) * (r if i % 2 == 0 else r * inner), cy + math.sin(math.radians(a0 + i * 180 / n)) * (r if i % 2 == 0 else r * inner)) for i in range(n * 2)])
def arrow(x0, y0, x1, y1, w=.05, head=.13):
    a = math.atan2(y1 - y0, x1 - x0); bx, by = x1 - math.cos(a) * head, y1 - math.sin(a) * head; nx, ny = -math.sin(a) * head * .55, math.cos(a) * head * .55
    return [('L', [(x0, y0), (bx, by)], w), ('P', [(x1, y1), (bx + nx, by + ny), (bx - nx, by - ny)])]
def blade_v(): return [('P', [(.5, .08), (.57, .2), (.56, .62), (.44, .62), (.43, .2)]), ('P', [(.3, .62), (.7, .62), (.68, .69), (.32, .69)]), ('P', [(.46, .69), (.54, .69), (.54, .86), (.46, .86)]), ('C', .5, .89, .045)]
def shield_g(): return [('P', [(.5, .14), (.8, .24), (.78, .52), (.66, .74), (.5, .88), (.34, .74), (.22, .52), (.2, .24)])]

G = {}
G['bolt'] = [('C', .66, .34, .15), ('L', [(.56, .46), (.2, .82)], .07), ('L', [(.48, .36), (.24, .58)], .045), ('L', [(.64, .54), (.42, .8)], .045)]
G['multi'] = move(G['bolt'], -.12, -.1, .62) + move(G['bolt'], .14, -.02, .62) + move(G['bolt'], -.02, .16, .62)
G['nova'] = [('R', .5, .5, .27, .075), ('C', .5, .5, .08)] + [('L', [(.5 + math.cos(a) * .36, .5 + math.sin(a) * .36), (.5 + math.cos(a) * .45, .5 + math.sin(a) * .45)], .05) for a in [i * math.pi / 4 for i in range(8)]]
G['chain'] = [('L', [(.16, .26), (.44, .4), (.32, .56), (.66, .6), (.54, .76), (.86, .76)], .075), ('C', .16, .26, .06), ('C', .86, .76, .06)]
G['strike'] = [('C', .56, .58, .15), ('L', [(.5, .44), (.3, .1)], .07), ('L', [(.62, .42), (.5, .16)], .045), ('L', [(.44, .52), (.2, .22)], .045), ('L', [(.2, .88), (.86, .88)], .05)]
G['zone'] = [('E', .5, .74, .4, .15, .06), ('P', [(.5, .1), (.6, .5), (.5, .66), (.4, .5)]), ('P', [(.26, .34), (.33, .56), (.26, .68), (.2, .56)]), ('P', [(.74, .34), (.8, .56), (.74, .68), (.67, .56)])]
G['heal'] = [('P', [(.4, .16), (.6, .16), (.6, .4), (.84, .4), (.84, .6), (.6, .6), (.6, .84), (.4, .84), (.4, .6), (.16, .6), (.16, .4), (.4, .4)])]
G['slash'] = [('P', [(.14, .7)] + [(.5 + math.cos(math.radians(a)) * .42, .66 + math.sin(math.radians(a)) * .5) for a in range(200, 345, 12)] + [(.5 + math.cos(math.radians(a)) * .36, .74 + math.sin(math.radians(a)) * .36) for a in range(332, 205, -12)])]
G['slam'] = [('P', [(.5, .56), (.36, .2), (.64, .2)]), ('L', [(.14, .8), (.36, .68), (.5, .78), (.64, .68), (.86, .8)], .06), ('L', [(.5, .6), (.5, .7)], .06)]
G['spin'] = [('A', .5, .5, .3, 20, 170, .08), ('A', .5, .5, .3, 200, 350, .08), ('P', [(.2, .58), (.1, .4), (.3, .42)]), ('P', [(.8, .42), (.9, .6), (.7, .58)])]
G['leap'] = [('A', .46, .62, .34, 190, 330, .065), ('P', [(.76, .46), (.86, .26), (.62, .32)]), ('L', [(.14, .84), (.5, .84)], .05), star(.24, .74, .09)]
G['dash'] = arrow(.3, .5, .9, .5, .08, .2) + [('L', [(.1, .32), (.5, .32)], .045), ('L', [(.1, .68), (.5, .68)], .045)]
G['rush'] = arrow(.34, .36, .9, .36, .065, .17) + arrow(.34, .64, .9, .64, .065, .17) + [('L', [(.1, .5), (.44, .5)], .045)]
G['shout'] = [('C', .22, .5, .08), ('A', .22, .5, .26, -50, 50, .06), ('A', .22, .5, .44, -50, 50, .06), ('A', .22, .5, .62, -50, 50, .06)]
G['volley'] = arrow(.2, .8, .8, .2) + arrow(.14, .58, .52, .14, .04, .11) + arrow(.44, .86, .86, .48, .04, .11)
G['rain'] = arrow(.28, .1, .28, .62) + arrow(.5, .26, .5, .86) + arrow(.72, .1, .72, .62)
G['fan'] = [s for a in (-60, -30, 0, 30, 60) for s in rot([('P', [(.5, .08), (.55, .3), (.5, .56), (.45, .3)])], a, (.5, .82))] + [('C', .5, .82, .06)]
G['boomerang'] = [('A', .5, .5, .3, 30, 330, .09)] + [star(.5 + math.cos(math.radians(a)) * .3, .5 + math.sin(math.radians(a)) * .3, .1) for a in (30, 180, 330)]
G['orbit'] = [('A', .5, .5, .33, 0, 360, .035), ('C', .5, .5, .1)] + [('C', .5 + math.cos(math.radians(a)) * .33, .5 + math.sin(math.radians(a)) * .33, .075) for a in (-90, 30, 150)]
G['turret'] = [('P', [(.36, .86), (.4, .4), (.6, .4), (.64, .86)]), ('P', [(.3, .4), (.7, .4), (.64, .3), (.36, .3)]), star(.5, .17, .12), ('L', [(.26, .86), (.74, .86)], .06)]
G['beam'] = [('P', [(.1, .82), (.18, .9), (.86, .26), (.74, .14)]), ('C', .8, .2, .13), ('L', [(.3, .5), (.14, .46)], .04), ('L', [(.5, .7), (.54, .86)], .04)]
G['vortex'] = [('L', [(.5 + math.cos(t * .5) * (.04 + t * .013), .5 + math.sin(t * .5) * (.04 + t * .013)) for t in range(0, 29)], .065), ('C', .5, .5, .06)]
G['mark'] = [('R', .5, .5, .28, .06), ('L', [(.5, .08), (.5, .34)], .06), ('L', [(.5, .66), (.5, .92)], .06), ('L', [(.08, .5), (.34, .5)], .06), ('L', [(.66, .5), (.92, .5)], .06), ('C', .5, .5, .07)]
G['wall'] = [('P', [(.14, .86), (.14, .4), (.24, .22), (.34, .4), (.34, .86)]), ('P', [(.4, .86), (.4, .3), (.5, .1), (.6, .3), (.6, .86)]), ('P', [(.66, .86), (.66, .4), (.76, .22), (.86, .4), (.86, .86)])]
G['blink'] = [star(.26, .7, .16), star(.72, .28, .22), ('C', .42, .56, .03), ('C', .5, .48, .03), ('C', .58, .4, .03)]
G['trap'] = [('P', [(.12, .62), (.22, .36), (.32, .62), (.42, .36), (.5, .62), (.58, .36), (.68, .62), (.78, .36), (.88, .62), (.88, .74), (.12, .74)]), ('L', [(.2, .84), (.8, .84)], .05)]
G['ward'] = shield_g()
G['hook'] = [('L', [(.2, .14), (.56, .5)], .06), ('A', .56, .66, .16, -90, 180, .08), ('P', [(.4, .66), (.3, .52), (.48, .56)]), ('C', .2, .14, .06)]
G['enchant'] = [('P', [(.5, .12), (.78, .42), (.62, .42), (.62, .5), (.38, .5), (.38, .42), (.22, .42)]), ('P', [(.5, .46), (.74, .72), (.6, .72), (.6, .88), (.4, .88), (.4, .72), (.26, .72)])]
G['resurrect'] = [('P', [(.44, .3), (.56, .3), (.56, .44), (.72, .44), (.72, .56), (.56, .56), (.56, .9), (.44, .9), (.44, .56), (.28, .56), (.28, .44), (.44, .44)]), ('A', .5, .18, .16, 0, 360, .045)]
G['axe'] = rot([('P', [(.47, .1), (.53, .1), (.53, .9), (.47, .9)]), ('P', [(.53, .16), (.78, .12), (.86, .3), (.78, .48), (.53, .44)])], 20)
G['note'] = [('L', [(.42, .74), (.42, .2), (.74, .14), (.74, .64)], .06), ('C', .33, .76, .11), ('C', .65, .66, .11)]
G['wing'] = [('P', [(.14, .7), (.3, .3), (.86, .16), (.62, .36), (.8, .4), (.56, .52), (.7, .58), (.44, .68)])]
G['leaf'] = [('P', [(.2, .8), (.24, .4), (.5, .16), (.8, .2), (.76, .5), (.52, .76)]), ('L', [(.16, .86), (.7, .3)], .035)]
G['drop'] = [('P', [(.5, .1), (.72, .5), (.74, .64), (.66, .8), (.5, .88), (.34, .8), (.26, .64), (.28, .5)])]
G['hourglass'] = [('P', [(.24, .14), (.76, .14), (.54, .5), (.76, .86), (.24, .86), (.46, .5)]), ('L', [(.18, .12), (.82, .12)], .06), ('L', [(.18, .88), (.82, .88)], .06)]
G['star'] = [star(.5, .5, .4, 4, .3), star(.5, .5, .2, 4, .4, -45)]
G['taunt'] = [('P', [(.26, .12), (.42, .12), (.39, .62), (.29, .62)]), ('C', .34, .8, .08), ('P', [(.58, .12), (.74, .12), (.71, .62), (.61, .62)]), ('C', .66, .8, .08)]
G['quake'] = [('L', [(.1, .3), (.34, .44), (.3, .62), (.56, .58), (.6, .8), (.9, .74)], .07), ('P', [(.2, .2), (.34, .06), (.44, .24)]), ('P', [(.66, .36), (.84, .26), (.84, .46)])]
G['dagger_drop'] = rot([('P', [(.5, .1), (.56, .28), (.55, .6), (.45, .6), (.44, .28)]), ('P', [(.36, .6), (.64, .6), (.62, .66), (.38, .66)]), ('P', [(.46, .66), (.54, .66), (.54, .84), (.46, .84)])], 35) + move(G['drop'], .26, .24, .3)
# items
G['sword'] = rot(blade_v(), 45); G['greatsword'] = rot(move([('P', [(.5, .02), (.6, .16), (.6, .6), (.4, .6), (.4, .16)]), ('P', [(.24, .6), (.76, .6), (.72, .68), (.28, .68)]), ('P', [(.455, .68), (.545, .68), (.545, .92), (.455, .92)]), ('C', .5, .94, .05)], 0, 0), 45)
G['katana'] = rot([('P', [(.52, .06), (.57, .2), (.55, .64), (.47, .64), (.49, .2)]), ('P', [(.4, .64), (.62, .64), (.62, .68), (.4, .68)]), ('P', [(.47, .68), (.55, .68), (.54, .92), (.48, .92)])], 45)
G['dagger'] = rot(move(blade_v(), 0, .04, .72), 45)
G['mace'] = rot([('P', [(.46, .4), (.54, .4), (.54, .9), (.46, .9)]), ('C', .5, .27, .17)] + [star(.5, .27, .25, 6, .62)], 45)
G['spear'] = rot([('P', [(.48, .28), (.52, .28), (.52, .96), (.48, .96)]), ('P', [(.5, .02), (.6, .2), (.5, .32), (.4, .2)])], 45)
G['bow'] = [('A', .3, .5, .46, -62, 62, .065), ('L', [(.52, .1), (.52, .9)], .025)] + arrow(.2, .5, .9, .5, .035, .1)
G['wand'] = rot([('P', [(.47, .34), (.53, .34), (.54, .92), (.46, .92)]), star(.5, .2, .18, 5, .45)], 45)
G['staff'] = rot([('P', [(.475, .3), (.525, .3), (.53, .96), (.47, .96)]), ('A', .5, .18, .13, 120, 420, .05), ('C', .5, .18, .07)], 30)
G['shield'] = shield_g() ; G['armor'] = [('P', [(.3, .14), (.42, .2), (.58, .2), (.7, .14), (.88, .3), (.78, .44), (.7, .38), (.72, .86), (.28, .86), (.3, .38), (.22, .44), (.12, .3)])]
G['head'] = [('P', [(.22, .58), (.24, .34), (.36, .18), (.5, .13), (.64, .18), (.76, .34), (.78, .58), (.7, .84), (.58, .84), (.58, .56), (.42, .56), (.42, .84), (.3, .84)]), ('P', [(.47, .02), (.53, .02), (.56, .14), (.44, .14)])]
G['hands'] = [('P', [(.3, .86), (.3, .5), (.26, .34), (.36, .32), (.4, .44), (.42, .2), (.5, .2), (.52, .42), (.56, .16), (.64, .18), (.64, .44), (.72, .26), (.78, .3), (.72, .56), (.7, .86)])]
G['feet'] = [('P', [(.3, .12), (.54, .12), (.54, .56), (.84, .7), (.86, .86), (.26, .86)])]
G['cape'] = [('P', [(.34, .12), (.66, .12), (.8, .84), (.64, .78), (.5, .88), (.36, .78), (.2, .84)]), ('C', .34, .14, .05), ('C', .66, .14, .05)]
G['trinket'] = [('R', .5, .58, .24, .07), star(.5, .24, .14, 4, .45)]; G['amulet'] = [('L', [(.18, .12), (.3, .38), (.5, .5), (.7, .38), (.82, .12)], .04), ('P', [(.5, .46), (.64, .66), (.5, .9), (.36, .66)])]
G['potion'] = [('P', [(.42, .12), (.58, .12), (.58, .36), (.76, .58), (.76, .74), (.66, .88), (.34, .88), (.24, .74), (.24, .58), (.42, .36)]), ('P', [(.38, .06), (.62, .06), (.62, .13), (.38, .13)])]
G['ore'] = [('P', [(.14, .7), (.24, .4), (.46, .22), (.7, .26), (.86, .5), (.8, .78), (.5, .86), (.26, .84)])]
G['crystal'] = [('P', [(.5, .06), (.66, .3), (.62, .86), (.38, .86), (.34, .3)]), ('P', [(.24, .5), (.32, .42), (.34, .86), (.2, .8)]), ('P', [(.76, .46), (.82, .56), (.8, .8), (.66, .86)])]
G['log'] = [('P', [(.14, .4), (.74, .28), (.86, .5), (.26, .72)]), ('C', .2, .56, .17)]
G['hide'] = [('P', [(.2, .14), (.4, .22), (.6, .22), (.8, .14), (.74, .4), (.86, .56), (.72, .6), (.78, .86), (.56, .76), (.44, .76), (.22, .86), (.28, .6), (.14, .56), (.26, .4)])]
G['meat'] = [('C', .44, .42, .26), ('L', [(.6, .6), (.82, .84)], .08), ('C', .84, .82, .07), ('C', .8, .88, .07)]
G['berry'] = [('C', .36, .62, .17), ('C', .62, .66, .17), ('C', .5, .42, .17), ('L', [(.5, .26), (.56, .1)], .04)]
G['mushroom'] = [('P', [(.12, .5), (.2, .26), (.5, .12), (.8, .26), (.88, .5)]), ('P', [(.4, .5), (.6, .5), (.64, .88), (.36, .88)])]
G['scrap'] = [('R', .5, .5, .2, .1)] + [s for a in range(0, 360, 45) for s in rot([('P', [(.44, .1), (.56, .1), (.58, .24), (.42, .24)])], a)]
G['dust'] = [star(.36, .4, .22), star(.7, .3, .12), star(.66, .7, .16), ('C', .3, .76, .04)]
G['core'] = [('R', .5, .5, .34, .05), star(.5, .5, .24, 4, .4)]
G['feather'] = rot([('P', [(.5, .06), (.66, .3), (.64, .6), (.5, .78), (.36, .6), (.34, .3)]), ('L', [(.5, .2), (.5, .94)], .03)], 30)
G['coin'] = [('R', .5, .5, .32, .07), star(.5, .5, .16, 4, .45)]
G['key'] = rot([('R', .5, .24, .13, .06), ('L', [(.5, .38), (.5, .9)], .06), ('L', [(.5, .74), (.64, .74)], .055), ('L', [(.5, .86), (.62, .86)], .055)], 45)
G['scroll'] = [('P', [(.26, .2), (.74, .2), (.74, .8), (.26, .8)]), ('L', [(.2, .2), (.8, .2)], .09), ('L', [(.2, .8), (.8, .8)], .09)]
G['card'] = rot([('P', [(.28, .12), (.72, .12), (.72, .88), (.28, .88)])], 12)
G['fragment'] = [('P', [(.3, .14), (.64, .1), (.8, .4), (.58, .56), (.7, .86), (.3, .8), (.2, .46)])]

def raster(prims, size):
    m = Image.new('L', (size, size), 0); d = ImageDraw.Draw(m); s = size
    for p in prims:
        if p[0] == 'P': d.polygon([(x * s, y * s) for x, y in p[1]], fill=255)
        elif p[0] == 'C': d.ellipse([(p[1] - p[3]) * s, (p[2] - p[3]) * s, (p[1] + p[3]) * s, (p[2] + p[3]) * s], fill=255)
        elif p[0] == 'R': d.ellipse([(p[1] - p[3]) * s, (p[2] - p[3]) * s, (p[1] + p[3]) * s, (p[2] + p[3]) * s], outline=255, width=int(p[4] * s))
        elif p[0] == 'E': d.ellipse([(p[1] - p[3]) * s, (p[2] - p[4]) * s, (p[1] + p[3]) * s, (p[2] + p[4]) * s], outline=255, width=int(p[5] * s))
        elif p[0] == 'A':
            r = p[3] + p[6] / 2; d.arc([(p[1] - r) * s, (p[2] - r) * s, (p[1] + r) * s, (p[2] + r) * s], p[4], p[5], fill=255, width=int(p[6] * s))
        elif p[0] == 'L':
            pts = [(x * s, y * s) for x, y in p[1]]; w = max(1, int(p[2] * s)); d.line(pts, fill=255, width=w, joint='curve')
            for x, y in (pts[0], pts[-1]): d.ellipse([x - w / 2, y - w / 2, x + w / 2, y + w / 2], fill=255)
    return m

def rrect_mask(size, rad, inset=0):
    m = Image.new('L', (size, size), 0); ImageDraw.Draw(m).rounded_rectangle([inset, inset, size - 1 - inset, size - 1 - inset], rad, fill=255); return m

def vgrad(size, top, bottom, w=None):
    w = w or size; g = Image.new('RGBA', (w, size)); px = ImageDraw.Draw(g)
    for y in range(size): px.line([(0, y), (w, y)], fill=mix(top, bottom, y / max(size - 1, 1)))
    return g

def icon(glyph, base, accent=None, glyph_col='fff8ea', size=N):
    """A rounded tile: painted colour wash, soft light from the top left, pale rim, cream glyph with a drop shadow."""
    S = size * SS; b = hx(base); dark = mix(b, (14, 16, 34), .62); lite = mix(b, (255, 255, 255), .12)
    tile = vgrad(S, lite, dark); glow = Image.new('L', (S, S), 0); ImageDraw.Draw(glow).ellipse([-S * .25, -S * .45, S * .95, S * .6], fill=110)
    glow = glow.filter(ImageFilter.GaussianBlur(S * .12)); tile = Image.composite(Image.new('RGBA', (S, S), mix(b, (255, 255, 255), .45)), tile, glow)
    wash = raster(move(glyph, 0, 0, 1.45), S).filter(ImageFilter.GaussianBlur(S * .07)).point(lambda v: v * .22)          # big soft echo of the glyph behind it
    tile = Image.composite(Image.new('RGBA', (S, S), mix(b, (255, 255, 255), .6)), tile, wash)
    g = raster(move(glyph, 0, 0, .78), S); sh = ImageChops.offset(g, int(S * .018), int(S * .03)).filter(ImageFilter.GaussianBlur(S * .014)).point(lambda v: v * .6)
    tile = Image.composite(Image.new('RGBA', (S, S), mix(dark, (0, 0, 0), .5)), tile, sh)
    gc = vgrad(S, hx(glyph_col), mix(hx(glyph_col), b, .3)); tile = Image.composite(gc, tile, g)
    if accent: tile = Image.composite(Image.new('RGBA', (S, S), hx(accent[1])), tile, raster(move(accent[0], 0, 0, .78), S))
    out = Image.new('RGBA', (S, S), (0, 0, 0, 0)); out.paste(tile, (0, 0), rrect_mask(S, int(S * .17), int(S * .03)))
    rim = Image.new('L', (S, S), 0); ImageDraw.Draw(rim).rounded_rectangle([S * .03, S * .03, S * .97 - 1, S * .97 - 1], int(S * .17), outline=255, width=int(S * .028))
    out = Image.composite(Image.new('RGBA', (S, S), mix(b, (255, 250, 230), .62)), out, rim)
    return out.resize((size, size), Image.LANCZOS)

KIND = {'projectile': 'bolt', 'nova': 'nova', 'heal': 'heal', 'chain': 'chain', 'zone': 'zone', 'strike': 'strike', 'melee': 'slash', 'spin': 'spin', 'leap': 'leap', 'dash': 'dash', 'rush': 'rush',
        'boomerang': 'boomerang', 'orbit': 'orbit', 'turret': 'turret', 'beam': 'beam', 'vortex': 'vortex', 'mark': 'mark', 'wall': 'wall', 'blinkto': 'blink', 'trap': 'trap', 'ward': 'ward', 'hook': 'hook',
        'enchant': 'enchant', 'buff': 'shout', 'resurrect': 'resurrect'}
BY_ID = {'thornvolley': 'multi', 'volley': 'volley', 'rain': 'rain', 'fan': 'fan', 'ricochet': 'chain', 'shieldbash': 'shield', 'groundslam': 'slam', 'executioner': 'axe', 'earthquake': 'quake', 'bloodrite': 'drop',
         'hymnvalor': 'note', 'aegischorus': 'shield', 'swiftwind': 'wing', 'renewal': 'leaf', 'purify': 'drop', 'unbreakable': 'ward', 'arcanesurge': 'star', 'elementward': 'ward', 'bloodpact': 'drop',
         'chronoblessing': 'hourglass', 'chronofield': 'hourglass', 'taunt': 'taunt', 'poisonblade': 'dagger_drop', 'shadowstep': 'blink', 'lunge': 'dash', 'starfall': 'star', 'soulsiphon': 'vortex', 'meteor': 'strike',
         'cometdive': 'strike', 'icelance': 'spear', 'smite': 'strike', 'consecrate': 'zone', 'drain': 'hook', 'arcanestrike': 'slash', 'beartrap': 'trap', 'runemine': 'mark', 'stoneskin': 'ore', 'mirrorward': 'ward'}
SLATE = '4a5578'
ITEMS = {  # id: (glyph, tile colour, accent (shapes, colour) or None)
 'sword': ('sword', SLATE), 'katana': ('katana', SLATE), 'greatsword': ('greatsword', SLATE), 'axe': ('axe', SLATE), 'mace': ('mace', SLATE), 'dagger': ('dagger', SLATE), 'spear': ('spear', SLATE),
 'bow': ('bow', '6a5a48'), 'wand': ('wand', '6a58a0'), 'staff': ('staff', '6a58a0'), 'shield': ('shield', '4a6a8a'), 'armor': ('armor', '5a6078'), 'head': ('head', '5a6078'), 'hands': ('hands', '5a6078'),
 'feet': ('feet', '5a6078'), 'cape': ('cape', '8a4048'), 'trinket': ('trinket', '8a7038'), 'amulet': ('amulet', '8a7038'),
 'hp': ('potion', '8a3040', ([('P', [(.3, .6), (.7, .6), (.72, .74), (.64, .84), (.36, .84), (.28, .74)])], 'ff5a6a')), 'mp': ('potion', '30508a', ([('P', [(.3, .6), (.7, .6), (.72, .74), (.64, .84), (.36, .84), (.28, .74)])], '5aa8ff')),
 'st': ('potion', '3a7040', ([('P', [(.3, .6), (.7, .6), (.72, .74), (.64, .84), (.36, .84), (.28, .74)])], '7ae07a')),
 'sunleaf': ('leaf', '7a8a30'), 'moonpetal': ('leaf', '5060a0'), 'redcap': ('mushroom', '8a4038'), 'berries': ('berry', '7a3a6a'), 'ironore': ('ore', '5a5e6e'), 'crystal': ('crystal', '3a80a0'), 'timber': ('log', '6a4a30'),
 'hide': ('hide', '7a5a3a'), 'meat': ('meat', '8a4a40'), 'ironscrap': ('scrap', '5a5e6e'), 'leatherstrip': ('hide', '6a4a30'), 'arcanedust': ('dust', '6a58a0'), 'runeshard': ('fragment', '3a80a0'), 'voidessence': ('vortex', '5a3a8a'),
 'riftprism': ('crystal', '8a4aa0'), 'embercore': ('core', ELEM['fire']), 'frostcore': ('core', ELEM['frost']), 'stormcore': ('core', ELEM['lightning']), 'venomcore': ('core', ELEM['poison']), 'holycore': ('core', ELEM['holy']),
 'shadowcore': ('core', ELEM['shadow']), 'setfragment': ('fragment', '8a7038'), 'angelFeather': ('feather', 'a09a70'), 'gold': ('coin', '8a7038'), 'key': ('key', '8a7038'), 'scroll': ('scroll', '7a6a50'), 'card': ('card', '4a6a8a', ([star(.5, .5, .16)], 'ffd86a'))}

def nine(w, h, rad, fill_top, fill_bot, border, bw=2, inner=None, corners=None, ss=4):
    W, H = w * ss, h * ss; img = Image.new('RGBA', (W, H), (0, 0, 0, 0)); m = Image.new('L', (W, H), 0); ImageDraw.Draw(m).rounded_rectangle([0, 0, W - 1, H - 1], rad * ss, fill=255)
    g = vgrad(H, fill_top, fill_bot, W); g.putalpha(Image.eval(m, lambda v: int(v * fill_top[3] / 255) if len(fill_top) > 3 else v)); img = Image.alpha_composite(img, g); d = ImageDraw.Draw(img)
    d.rounded_rectangle([bw * ss // 2, bw * ss // 2, W - 1 - bw * ss // 2, H - 1 - bw * ss // 2], rad * ss, outline=border, width=bw * ss)
    if inner: d.rounded_rectangle([5 * ss, 5 * ss, W - 1 - 5 * ss, H - 1 - 5 * ss], max(rad - 4, 1) * ss, outline=inner, width=ss)
    if corners:
        for cx, cy in ((9, 9), (w - 9, 9), (9, h - 9), (w - 9, h - 9)):
            d.polygon([((cx) * ss, (cy - 5) * ss), ((cx + 5) * ss, cy * ss), (cx * ss, (cy + 5) * ss), ((cx - 5) * ss, cy * ss)], fill=corners)
    return img.resize((w, h), Image.LANCZOS)

def frames(out):
    gold, navy = hx('e2cf9c'), (16, 24, 46)
    F = {'panel': nine(96, 96, 12, navy + (236,), (10, 14, 30, 236), gold, 2, hx('e2cf9c', 70), gold), 'panel_light': nine(96, 96, 12, hx('f8f1e0', 245), hx('eadfc6', 245), hx('8a6a48'), 2, hx('8a6a48', 80), hx('8a6a48')),
         'tooltip': nine(64, 64, 8, (22, 30, 56, 245), (14, 18, 36, 245), hx('9fb4d8'), 1), 'button': nine(96, 40, 10, hx('2c3c66', 245), hx('1c2848', 245), gold, 2),
         'button_hover': nine(96, 40, 10, hx('40588e', 250), hx('2a3a66', 250), hx('fff0c0'), 2), 'button_pressed': nine(96, 40, 10, hx('1a2440', 250), hx('2a3a66', 250), hx('ffd86a'), 2),
         'slot': nine(72, 72, 10, (8, 12, 26, 210), (20, 28, 52, 210), hx('5a6a94'), 2), 'slot_selected': nine(72, 72, 10, (20, 28, 52, 230), (36, 50, 90, 230), hx('ffe7a0'), 3),
         'bar_frame': nine(256, 22, 10, (8, 12, 26, 220), (16, 22, 42, 220), gold, 2)}
    for n, (a, b) in {'hp': ('ff7a86', 'c8283c'), 'mp': ('7ac0ff', '2a5ac8'), 'st': ('a8f08a', '3a9a48'), 'xp': ('ffe9a0', 'd89a2a')}.items():
        f = nine(256, 14, 6, hx(a), hx(b), hx(b), 1); gl = Image.new('RGBA', (256, 14), (0, 0, 0, 0)); ImageDraw.Draw(gl).rounded_rectangle([3, 2, 252, 5], 2, fill=(255, 255, 255, 80)); F['bar_' + n] = Image.alpha_composite(f, gl)
    for i, c in enumerate(json.load(open(os.path.join(ROOT, 'data/items.json'), encoding='utf-8-sig'))['rarityColors']):
        S = 136 * 4; fr = Image.new('RGBA', (S, S), (0, 0, 0, 0)); d = ImageDraw.Draw(fr); col = hx(c)
        glow = Image.new('L', (S, S), 0); ImageDraw.Draw(glow).rounded_rectangle([10, 10, S - 11, S - 11], 96, outline=255, width=28 + i * 8); glow = glow.filter(ImageFilter.GaussianBlur(10 + i * 4)).point(lambda v: v * (.35 + i * .12))
        fr = Image.composite(Image.new('RGBA', (S, S), col), fr, glow); d = ImageDraw.Draw(fr); d.rounded_rectangle([14, 14, S - 15, S - 15], 92, outline=col, width=14)
        if i >= 3:
            for cx, cy in ((S // 2, 16), (S // 2, S - 16), (16, S // 2), (S - 16, S // 2)): d.polygon([(cx, cy - 22), (cx + 22, cy), (cx, cy + 22), (cx - 22, cy)], fill=mix(col, (255, 255, 255), .5))
        F['rarity_%d' % i] = fr.resize((136, 136), Image.LANCZOS)
    for n, (g, c) in {'marker_quest': ([('P', [(.44, .1), (.56, .1), (.54, .62), (.46, .62)]), ('C', .5, .8, .08)], 'ffd86a'), 'marker_turnin': ([('A', .5, .32, .16, 180, 450, .1), ('L', [(.5, .48), (.5, .62)], .1), ('C', .5, .8, .08)], '7ae07a'),
                      'marker_pin': ([('C', .5, .36, .26), ('P', [(.3, .5), (.7, .5), (.5, .94)])], 'ff7a86'), 'cursor': ([('P', [(.12, .06), (.8, .56), (.48, .6), (.36, .9)])], 'fff8ea')}.items():
        S = 64 * 4; m = raster(g, S); sh = ImageChops.offset(m, 6, 10).filter(ImageFilter.GaussianBlur(6)).point(lambda v: v * .6); im = Image.new('RGBA', (S, S), (0, 0, 0, 0))
        im = Image.composite(Image.new('RGBA', (S, S), (10, 12, 30, 255)), im, sh); edge = m.filter(ImageFilter.MaxFilter(13)); im = Image.composite(Image.new('RGBA', (S, S), (30, 34, 60, 255)), im, edge)
        im = Image.composite(vgrad(S, mix(hx(c), (255, 255, 255), .4), hx(c)), im, m); F[n] = im.resize((64, 64), Image.LANCZOS)
    os.makedirs(out, exist_ok=True)
    for n, im in F.items(): im.save(os.path.join(out, n + '.png'))
    return F

if __name__ == '__main__':
    skills = json.load(open(os.path.join(ROOT, 'data/skills.json'), encoding='utf-8-sig'))['skills']; sk = {}; it = {}
    for d in ('assets/ui/icons/skill', 'assets/ui/icons/item'): os.makedirs(os.path.join(ROOT, d), exist_ok=True)
    for sid, s in skills.items():
        g = BY_ID.get(sid) or KIND.get(s['kind'], 'star'); sk[sid] = icon(G[g], ELEM.get(s.get('type', 'physical'), ELEM['physical'])); sk[sid].save(os.path.join(ROOT, 'assets/ui/icons/skill', sid + '.png'))
    for iid, v in ITEMS.items():
        it[iid] = icon(G[v[0]], v[1], v[2] if len(v) > 2 else None); it[iid].save(os.path.join(ROOT, 'assets/ui/icons/item', iid + '.png'))
    F = frames(os.path.join(ROOT, 'assets/ui/frames'))
    # preview 1: every icon
    allic = list(sk.items()) + list(it.items()); cols = 12; cell = 150; sheet = Image.new('RGBA', (cols * cell, ((len(allic) + cols - 1) // cols) * (cell + 8)), (22, 26, 44, 255)); dr = ImageDraw.Draw(sheet)
    for i, (n, im) in enumerate(allic):
        x, y = (i % cols) * cell + 11, (i // cols) * (cell + 8) + 6; sheet.alpha_composite(im, (x, y)); dr.text((x, y + 130), n[:20], fill=(200, 205, 225))
    sheet.convert('RGB').save(os.path.join(ROOT, 'docs/kit/icons.png'))
    # preview 2: a mock pack window and HUD built only from these pieces
    def stretch(im, w, h, m):
        o = Image.new('RGBA', (w, h), (0, 0, 0, 0)); sw, sh_ = im.size; xs = [(0, m, 0, m), (m, sw - m, m, w - m), (sw - m, sw, w - m, w)]; ys = [(0, m, 0, m), (m, sh_ - m, m, h - m), (sh_ - m, sh_, h - m, h)]
        for sx0, sx1, dx0, dx1 in xs:
            for sy0, sy1, dy0, dy1 in ys:
                if dx1 > dx0 and dy1 > dy0: o.paste(im.crop((sx0, sy0, sx1, sy1)).resize((dx1 - dx0, dy1 - dy0)), (dx0, dy0))
        return o
    bg = Image.open(os.path.join(ROOT, 'assets/textures/terrain_painted/grass_col.jpg')).convert('RGBA').resize((1100, 1100)).crop((0, 0, 1100, 760)); mock = bg.point(lambda v: int(v * .55)); mock.putalpha(255); dr = ImageDraw.Draw(mock)
    mock.alpha_composite(stretch(F['panel'], 560, 470, 24), (40, 40)); dr = ImageDraw.Draw(mock); dr.text((70, 62), 'PACK', fill=(255, 240, 200)); dr.line([(64, 84), (576, 84)], fill=(226, 207, 156, 255), width=1)
    order = ['sword', 'katana', 'greatsword', 'bow', 'staff', 'wand', 'shield', 'armor', 'head', 'hands', 'feet', 'cape', 'amulet', 'trinket', 'hp', 'mp', 'st', 'crystal', 'ironore', 'sunleaf', 'embercore', 'frostcore', 'card', 'key', 'gold', 'scroll', 'hide', 'timber']
    rar = [4, 2, 3, 1, 3, 0, 2, 4, 1, 0, 2, 3, 4, 1] + [0] * 20
    for i, n in enumerate(order):
        x, y = 66 + (i % 7) * 72, 98 + (i // 7) * 72; mock.alpha_composite(F['slot_selected' if i == 2 else 'slot'].resize((68, 68)), (x, y)); mock.alpha_composite(it[n].resize((56, 56), Image.LANCZOS), (x + 6, y + 6))
        if i < 14: mock.alpha_composite(F['rarity_%d' % rar[i]].resize((64, 64), Image.LANCZOS), (x + 2, y + 2))
    for j, (t, im) in enumerate((('Equip', 'button_hover'), ('Drop', 'button'), ('Sort', 'button'))):
        mock.alpha_composite(stretch(F[im], 120, 38, 12), (66 + j * 132, 450)); ImageDraw.Draw(mock).text((66 + j * 132 + 44, 463), t, fill=(255, 245, 215))
    mock.alpha_composite(stretch(F['panel_light'], 300, 150, 24), (630, 60)); dr = ImageDraw.Draw(mock); dr.text((656, 84), 'Greatsword of Embers', fill=(120, 60, 30)); dr.text((656, 108), 'Epic two-handed sword', fill=(90, 70, 50)); dr.text((656, 132), '+18 fire damage', fill=(90, 70, 50)); dr.text((656, 152), 'Burns foes for 3 seconds', fill=(90, 70, 50))
    for j, (n, frac) in enumerate((('hp', .8), ('mp', .55), ('st', .9))):
        mock.alpha_composite(F['bar_frame'], (40, 560 + j * 28)); mock.alpha_composite(F['bar_' + n].crop((0, 0, int(248 * frac), 14)), (44, 564 + j * 28))
    hot = ['fire', 'nova', 'zap', 'heal', 'cleave', 'whirlwind', 'leap', 'shadowstep', 'meteor', 'volley', 'blink', 'firewall']
    for j, n in enumerate(hot):
        x = 340 + j * 62; mock.alpha_composite(F['slot'].resize((60, 60)), (x, 680)); mock.alpha_composite(sk[n].resize((50, 50), Image.LANCZOS), (x + 5, 685))
    for j, n in enumerate(('marker_quest', 'marker_turnin', 'marker_pin', 'cursor')): mock.alpha_composite(F[n], (640 + j * 70, 240))
    mock.convert('RGB').save(os.path.join(ROOT, 'docs/kit/ui_mock.png')); print(len(sk), 'skill icons,', len(it), 'item icons,', len(F), 'frame pieces')
