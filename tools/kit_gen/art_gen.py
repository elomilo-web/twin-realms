"""Title and loading-screen paintings for Twin Realms: the same valley twice, once in the Light realm and once in Shadow.
Writes assets/ui/art/{title_light,title_shadow}.jpg (1920 x 1080, no text), logo.png (transparent), and docs/kit/title_*.jpg previews with the logo on.
Run from the repo root:  python tools/kit_gen/art_gen.py"""
import math, os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from tex_gen import fnoise, hexc, sm, ramp, poster

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
W, H = 1920, 1080
def n1(seed, octaves=5, base=3):
    """Smooth 1D noise across the picture width, 0..1."""
    r = np.random.RandomState(seed); x = np.linspace(0, 1, W); out = np.zeros(W); amp = 1; tot = 0
    for o in range(octaves):
        k = base * 2 ** o + 1; pts = r.rand(k + 1); t = x * k; i = np.minimum(t.astype(int), k - 1); f = t - i; f = f * f * (3 - 2 * f)
        out += amp * (pts[i] * (1 - f) + pts[i + 1] * f); tot += amp; amp *= .5
    return out / tot
def c255(h, a=255): return tuple(int(v * 255) for v in hexc(h)) + (a,)

PAL = {
 'light': dict(sky=('3f7fd0', '7fb4ea', 'bfdcf4', 'fbeed2'), cloud='ffffff', cloud_sh='c9d8ee', sun=('fff4c8', 1410, 610, 520), far=('8fa6d2', 'a9bfe2', 'c6d6ee'), snow='f4f8ff',
               hills=('7fb07a', '6aa262', '5a9654', '4a8848'), hill_hi=('b6d68a', 'a4cc7c', '92c26e', '80b660'), tree=('2f6a44', '4a8a52', '7ab868'), trunk='5a3c2a',
               wall=('e9e4dc', 'bfc4d4'), roof=('4a6fb8', '2f4a8c'), window='ffe9a0', flag='e2644a', flowers=('ffffff', 'ffe08a', 'f6c8dc'), haze='dcebf8', vign=.22),
 'shadow': dict(sky=('120a2a', '2c1650', '6a2a78', 'e06a8a'), cloud='b06ac0', cloud_sh='4a2a6a', sun=('ffd6f0', 1410, 300, 260), far=('3a2a66', '4c3680', '64489a'), snow='b8a0e8',
                hills=('2c2a5a', '26244e', '201e44', '18163a'), hill_hi=('4a4690', '423e82', '3a3674', '322e66'), tree=('120f2c', '1e1a44', '3a2f70'), trunk='0c0a1c',
                wall=('5a548a', '2e2a56'), roof=('2a1e52', '160f30'), window='6fe8ff', flag='b884ff', flowers=('6fe8ff', 'b884ff', 'ff8fc0'), haze='4a2a70', vign=.4)}

def paint(realm):
    P = PAL[realm]; yy, xx = np.mgrid[0:H, 0:W]; img = ramp(np.clip(yy / (H * .72), 0, 1), P['sky'])
    # sun or moon glow
    c, sx, sy, sr = P['sun']; d = np.hypot(xx - sx, yy - sy); img = img + hexc(c)[None, None] * (np.exp(-(d / sr) ** 2) * (.3 if realm == 'light' else .5) + (d < (38 if realm == 'light' else 54)) * .9)[..., None]
    # clouds: flat-bottomed, stacked, lit from above
    nz = fnoise(7, 2.6, (3.2, 1), 2048)[:H, :W] * .75 + fnoise(8, 1.7, (2.2, 1), 2048)[:H, :W] * .25; band = sm(.62, .1, yy / H) * sm(-.02, .1, yy / H)
    cm = sm(.5, .62, nz * (.55 + .6 * band)); lit = sm(.45, .7, np.roll(nz, 18, 0)); cl = hexc(P['cloud_sh'])[None, None] * (1 - lit[..., None]) + hexc(P['cloud'])[None, None] * lit[..., None]
    img = img * (1 - cm[..., None] * .92) + cl * cm[..., None] * .92
    # far mountains, three ranges fading into haze
    for i, colr in enumerate(P['far']):
        ridge = H * (.5 + i * .045) - (n1(20 + i, 6, 4) ** 1.4) * H * (.3 - i * .06) - 30 * np.abs(np.sin(np.linspace(0, 9 + i * 3, W) + i))
        m = sm(-1.5, 1.5, yy - ridge[None, :]); shade = .92 + .16 * fnoise(30 + i, 2.2, (2.5, 1), 2048)[:H, :W]; col = hexc(colr)[None, None] * shade[..., None]
        if i == 0: sn = sm(26, 0, yy - ridge[None, :] - 22 * fnoise(33, 1.8, n=2048)[:H, :W]) * sm(H * .42, H * .3, ridge)[None, :]; col = col * (1 - sn[..., None]) + hexc(P['snow']) * sn[..., None]
        img = img * (1 - m[..., None]) + col * m[..., None]
    hz = sm(H * .44, H * .62, yy) * sm(H * .75, H * .58, yy); img = img * (1 - .35 * hz[..., None]) + hexc(P['haze']) * .35 * hz[..., None]
    # rolling hills
    ridges = []
    for i in range(4):
        ridge = H * (.6 + i * .085) - n1(40 + i, 4, 2) * H * .11 + (i == 1) * -70 * np.exp(-((np.arange(W) - 1250) / 260) ** 2); ridges.append(ridge)
        m = sm(-1.5, 1.5, yy - ridge[None, :]); t = sm(0, 120, yy - ridge[None, :]); v = poster(fnoise(50 + i, 2.4, n=2048)[:H, :W], 4, .2)
        col = hexc(P['hill_hi'][i])[None, None] * (1 - t[..., None]) + hexc(P['hills'][i])[None, None] * t[..., None]; col = col * (.9 + .2 * v[..., None])
        img = img * (1 - m[..., None]) + col * m[..., None]
        if i == 1: castle_layer = (np.clip(img, 0, 1) * 255).astype('uint8')
    im = Image.fromarray((np.clip(img, 0, 1) * 255).astype('uint8')).convert('RGBA'); dr = ImageDraw.Draw(im)
    return im, ridges

def castle(dr, cx, base, s, P):
    lit, dark = c255(P['wall'][0]), c255(P['wall'][1]); rl, rd = c255(P['roof'][0]), c255(P['roof'][1])
    def tower(x, w, h, roof):
        dr.rectangle([cx + x * s, base - h * s, cx + (x + w * .55) * s, base], fill=lit); dr.rectangle([cx + (x + w * .55) * s, base - h * s, cx + (x + w) * s, base], fill=dark)
        dr.polygon([(cx + (x - w * .12) * s, base - h * s), (cx + (x + w * .5) * s, base - (h + roof) * s), (cx + (x + w * .55) * s, base - h * s)], fill=rl)
        dr.polygon([(cx + (x + w * .5) * s, base - (h + roof) * s), (cx + (x + w * 1.12) * s, base - h * s), (cx + (x + w * .55) * s, base - h * s)], fill=rd)
        dr.line([(cx + (x + w * .5) * s, base - (h + roof) * s), (cx + (x + w * .5) * s, base - (h + roof + 14) * s)], fill=dark, width=max(1, int(s)))
        dr.polygon([(cx + (x + w * .5) * s, base - (h + roof + 14) * s), (cx + (x + w * .5 + 12) * s, base - (h + roof + 10) * s), (cx + (x + w * .5) * s, base - (h + roof + 6) * s)], fill=c255(P['flag']))
        for wy in (h * .45, h * .72): dr.rectangle([cx + (x + w * .3) * s, base - wy * s, cx + (x + w * .46) * s, base - (wy - 9) * s], fill=c255(P['window']))
    dr.rectangle([cx - 70 * s, base - 46 * s, cx + 10 * s, base], fill=lit); dr.rectangle([cx + 10 * s, base - 46 * s, cx + 78 * s, base], fill=dark)
    for i in range(-70, 78, 12): dr.rectangle([cx + i * s, base - 52 * s, cx + (i + 7) * s, base - 46 * s], fill=lit if i < 10 else dark)
    tower(-96, 30, 78, 40); tower(64, 30, 70, 38); tower(-22, 44, 118, 62); tower(-52, 22, 60, 30); tower(30, 22, 64, 30)
    dr.pieslice([cx - 12 * s, base - 30 * s, cx + 12 * s, base + 6 * s], 180, 360, fill=c255(P['roof'][1])); dr.rectangle([cx - 12 * s, base - 12 * s, cx + 12 * s, base], fill=c255(P['roof'][1]))

def tree(dr, x, y, s, P, seed):
    r = np.random.RandomState(seed); dr.polygon([(x - 7 * s, y), (x - 3 * s, y - 60 * s), (x + 3 * s, y - 60 * s), (x + 8 * s, y)], fill=c255(P['trunk']))
    blobs = [(0, -92, 46), (0, -64, 40)] + [(math.cos(a) * (30 + r.rand() * 14), -90 + math.sin(a) * (26 + r.rand() * 12), 24 + r.rand() * 14) for a in np.linspace(0, 6.28, 9)[:-1] + r.rand() * .6]
    for t, col in enumerate(P['tree']):
        for bx, by, br in blobs:
            k = 1 - t * .26; ox, oy = (-t * 9, -t * 10); dr.ellipse([x + (bx + ox - br * k) * s, y + (by + oy - br * k * .86) * s, x + (bx + ox + br * k) * s, y + (by + oy + br * k * .86) * s], fill=c255(col))

def make(realm):
    P = PAL[realm]; im, ridges = paint(realm); dr = ImageDraw.Draw(im)
    castle(dr, 1250, int(ridges[1][1250]) + 8, 1.5, P)
    r = np.random.RandomState(5)
    for i in range(46):          # distant trees along the second and third ridge
        x = r.randint(0, W); k = r.choice([1, 2]); y = ridges[k][x] + 6
        if abs(x - 1250) > 210 or k == 2: tree(dr, x, y, .28 + k * .16 + r.rand() * .1, P, i)
    for x, s in ((90, 2.6), (300, 1.5), (1800, 2.9), (1600, 1.3)): tree(dr, x, ridges[3][x] + 40 * s, s, P, x)
    for i in range(900):         # meadow flowers and grass flecks in the foreground
        x = r.randint(0, W); y = r.randint(int(ridges[3][x]) + 10, H); s = 1.5 + (y - ridges[3][x]) / 60; col = P['flowers'][r.randint(3)] if r.rand() < .45 else P['hill_hi'][0]
        dr.ellipse([x - s, y - s * .7, x + s, y + s * .7], fill=c255(col, 230))
    a = np.asarray(im.convert('RGB'), dtype=float) / 255
    g = fnoise(99, .8, n=2048)[:H, :W, None]; a = a * (1 + (g - .5) * .05)                       # paper grain
    yy, xx = np.mgrid[0:H, 0:W]; v = np.hypot((xx - W / 2) / (W / 2), (yy - H / 2) / (H / 2)); a = a * (1 - P['vign'] * sm(.6, 1.5, v))[..., None]
    soft = np.asarray(Image.fromarray((np.clip(a, 0, 1) * 255).astype('uint8')).filter(ImageFilter.GaussianBlur(1.1)), dtype=float) / 255
    return Image.fromarray((np.clip(a * .55 + soft * .45, 0, 1) * 255).astype('uint8'))

def logo():
    f = ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSerif-Bold.ttf', 128); f2 = ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSerif-Bold.ttf', 40)
    Wl, Hl = 1400, 330; m = Image.new('L', (Wl, Hl), 0); d = ImageDraw.Draw(m); text = 'TWIN  REALMS'; x = 0; widths = [d.textlength(ch, font=f) + 14 for ch in text]; x = (Wl - sum(widths)) / 2
    for ch, w in zip(text, widths): d.text((x, 30), ch, font=f, fill=255); x += w
    d.line([(Wl / 2 - 420, 232), (Wl / 2 - 40, 232)], fill=255, width=3); d.line([(Wl / 2 + 40, 232), (Wl / 2 + 420, 232)], fill=255, width=3)
    d.polygon([(Wl / 2, 216), (Wl / 2 + 16, 232), (Wl / 2, 248), (Wl / 2 - 16, 232)], fill=255); sub = 'L I G H T   A N D   S H A D O W'; d.text(((Wl - d.textlength(sub, font=f2)) / 2, 262), sub, font=f2, fill=255)
    grad = Image.new('RGBA', (Wl, Hl)); gd = ImageDraw.Draw(grad)
    for y in range(Hl): t = y / Hl; gd.line([(0, y), (Wl, y)], fill=(int(255 - 20 * t), int(246 - 70 * t), int(214 - 120 * t), 255))
    out = Image.new('RGBA', (Wl, Hl), (0, 0, 0, 0)); sh = m.filter(ImageFilter.GaussianBlur(7)).point(lambda v: min(255, int(v * 1.5)))
    out = Image.composite(Image.new('RGBA', (Wl, Hl), (14, 16, 40, 235)), out, sh); out = Image.composite(grad, out, m); return out

if __name__ == '__main__':
    os.makedirs(os.path.join(ROOT, 'assets/ui/art'), exist_ok=True); lg = logo(); lg.save(os.path.join(ROOT, 'assets/ui/art/logo.png'))
    for realm in ('light', 'shadow'):
        im = make(realm); im.save(os.path.join(ROOT, f'assets/ui/art/title_{realm}.jpg'), quality=92)
        pv = im.convert('RGBA'); pv.alpha_composite(lg, ((W - lg.width) // 2, 70)); pv.convert('RGB').resize((1280, 720), Image.LANCZOS).save(os.path.join(ROOT, f'docs/kit/title_{realm}.jpg'), quality=88); print('made', realm)
