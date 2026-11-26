"""Twin Realms gothic weapons: one function per model in assets/models/kit/weapons/<name>.glb.
Game coordinates: grip at the origin, blade or head up +Y; the hand covers about y -0.1..0.1.
Sizes follow the old kit models so the hero's holds (data/avatar.json) still fit."""
import math
from gothic import *   # noqa: F401,F403

WEAPONS = {}


def weapon(fn):
    WEAPONS[fn.__name__] = fn
    return fn


# ------------------------------------------------------------------ parts
def blade(y0, length, w0, w1, t, tip=0.16, fuller=0.0, fuller_end=0.65, mat=STEEL, single=False, curve=0.0,
          n=36, name="blade", glow_fuller=None, clip=0.0, wave=0.0, waves=8):
    """A forged blade from y0 up. Double-edged: lenticular section with sharpened bevels and an optional fuller
    (groove) down both faces. single=True: one edge on -X, thick spine on +X (katana, saber); curve bends the
    tip towards the spine. clip: single-edged clipped point (cutlass)."""
    rings = []
    tipS = 1 - tip / length
    for i in range(n + 1):
        s = i / n
        y = y0 + s * length
        w = w0 + (w1 - w0) * s
        th = t * (1 - 0.4 * s)
        f = 1.0
        if s > tipS:
            u = (s - tipS) / (1 - tipS)
            f = max(0.0, math.cos(u * math.pi / 2)) ** 0.75
        xo = curve * s * s
        if wave:   # flamberge: the whole blade snakes, fading out towards the point
            xo += wave * math.sin(2 * math.pi * waves * s) * min(1.0, s * 8) * (1 - s)
        if single:
            pts = [(-w / 2, 0), (w * 0.05, th / 2), (w / 2 - th * 0.3, th * 0.38), (w / 2, 0), (w / 2 - th * 0.3, -th * 0.38), (w * 0.05, -th / 2)]
            a = w * (0.5 - clip) * 0.6
            pts = [(a + (x - a) * f, zz * f) for x, zz in pts]
        else:
            fd = th * 0.28 * (1 if s < fuller_end else max(0, 1 - (s - fuller_end) / 0.08)) if fuller else 0
            fw = min(fuller / 2, w * 0.16) if fuller else w * 0.05
            pts = [(w / 2, 0), (w * 0.27, th / 2), (fw, th / 2), (0, th / 2 - fd), (-fw, th / 2), (-w * 0.27, th / 2),
                   (-w / 2, 0), (-w * 0.27, -th / 2), (-fw, -th / 2), (0, -th / 2 + fd), (fw, -th / 2), (w * 0.27, -th / 2)]
            pts = [(x * f, zz * f) for x, zz in pts]
        rings.append([(x + xo, y, zz) for x, zz in pts])
    ty = y0 + length
    tipx = (w1 * (0.5 - clip) * 0.6 if single else 0) + curve
    parts = [loft(name, rings, mat, cap0=True, tip=(tipx, ty, 0))]
    if glow_fuller and fuller:
        # a thin glowing inlay down the fuller (both faces)
        for sgn in (1, -1):
            pts = []
            for i in range(0, int(n * fuller_end) + 1):
                s = i / n; y = y0 + 0.04 + s * length * 0.97
                th = t * (1 - 0.4 * s)
                pts.append((0, y, sgn * (th / 2 - th * 0.28 + 0.0012)))
            parts.append(tube(name + "_rune", pts, 0.0022, glow(glow_fuller), sides=5))
    return parts


def grip(y0, y1, r, mat=LEATHER, wrap=True, sx=1.0, sz=0.8, turns=None, cord=0.003, name="grip"):
    """Grip core with a spiral leather cord wrap."""
    L = y1 - y0
    prof = [(r * 0.96, 0), (r, 0.01), (r * 1.04, L * 0.5), (r, L - 0.01), (r * 0.96, L)]
    parts = [lathe(name, prof, mat, seg=16, sx=sx, sz=sz, y0=y0)]
    if wrap:
        turns = turns or max(4, int(L / 0.015))
        pts = []
        steps = turns * 10
        for i in range(steps + 1):
            s = i / steps; a = s * turns * 2 * math.pi
            y = y0 + 0.006 + s * (L - 0.012)
            rr = r * (1.0 + 0.04 * math.sin(math.pi * s)) + cord * 0.4
            pts.append((rr * math.cos(a) * sx, y, rr * math.sin(a) * sz))
        parts.append(tube(name + "_cord", pts, cord, mat, sides=5))
    return parts


def ferrule(y, h, r, mat=IRON, sx=1.0, sz=1.0, name="ferrule"):
    prof = [(r * 0.92, 0), (r, h * 0.2), (r, h * 0.8), (r * 0.92, h)]
    return [lathe(name, prof, mat, seg=20, sx=sx, sz=sz, y0=y, bevel=0.0)]


def pommel(y, kind="wheel", r=0.035, mat=IRON, gem=None, name="pommel"):
    """Pommel below the grip, top at y. kinds: wheel, ball, pear, ring, scent (faceted gothic), spike."""
    parts = []
    if kind == "wheel":
        prof = [(0, -r * 0.55), (r * 0.6, -r * 0.55), (r, -r * 0.3), (r, r * 0.3), (r * 0.6, r * 0.55), (0, r * 0.55)]
        o = lathe(name, prof, mat, seg=28, bevel=0.002)
        place(o, rot=(90, 0, 0), loc=(0, y - r, 0)); parts.append(o)
        parts += ferrule(y - 0.012, 0.014, r * 0.38, mat, name=name + "_neck")
        if gem:
            parts.append(ball(name + "_gem", (0, y - r, r * 0.5), r * 0.32, glow(gem), sz=0.6))
            parts.append(ball(name + "_gem2", (0, y - r, -r * 0.5), r * 0.32, glow(gem), sz=0.6))
    elif kind == "ball":
        parts.append(ball(name, (0, y - r, 0), r, mat))
    elif kind == "pear":
        prof = [(0, -r * 1.6), (r * 0.5, -r * 1.5), (r * 0.95, -r * 1.0), (r, -r * 0.5), (r * 0.6, 0), (r * 0.35, r * 0.15), (0, r * 0.15)]
        parts.append(lathe(name, prof, mat, seg=20, y0=y))
    elif kind == "scent":
        prof = [(0, -r * 2.0), (r * 0.25, -r * 1.95), (r * 0.6, -r * 1.5), (r, -r * 0.9), (r * 0.7, -r * 0.3), (r * 0.3, 0), (0, 0.002)]
        parts.append(lathe(name, prof, mat, seg=8, y0=y, bevel=0.0015))
        if gem:
            parts.append(ball(name + "_gem", (0, y - r * 0.95, 0), r * 0.55, glow(gem), seg=8, rings_=6))
    elif kind == "ring":
        parts.append(ring(name, (0, y - r, 0), r * 0.75, r * 0.22, mat, axis="z"))
    elif kind == "spike":
        prof = [(0, -r * 2.2), (r * 0.35, -r * 0.9), (r * 0.6, -r * 0.3), (r * 0.3, 0), (0, 0.002)]
        parts.append(lathe(name, prof, mat, seg=6, y0=y, bevel=0.001))
    return parts


def crossguard(y, half, h=0.022, t=0.026, droop=0.0, flare=0.0, mat=IRON, ends="ball", langet=0.0, name="guard"):
    """A crossguard centred on the blade root (y = its top). droop bends the quillons down (gothic), flare widens
    their ends; langet = length of the tongue that runs up over the blade."""
    out = []
    N = 10
    top, bot = [], []
    for i in range(N + 1):
        s = i / N
        x = -half + 2 * half * s
        d = droop * (abs(x) / half) ** 2
        hh = h * (1 + flare * (abs(x) / half) ** 3)
        top.append((x, y - d)); bot.append((x, y - d - hh))
    outline = bot + list(reversed(top))
    out.append(slab(name, outline, mat, t * 0.8, t, inset=0.004, bevel=0.0015))
    if ends == "ball":
        for sx in (-1, 1):
            out.append(ball(name + "_end", (sx * (half + 0.006), y - droop - h * 0.5 * (1 + flare), 0), h * 0.6 * (1 + flare * 0.5), mat))
    elif ends == "curl":
        for sx in (-1, 1):
            cx = sx * (half + 0.004); cy = y - droop - h * 0.5
            pts = [(cx + sx * 0.016 * math.sin(a), cy + 0.016 * (1 - math.cos(a)) - 0.016, 0) for a in [k * 0.35 for k in range(16)]]
            out.append(tube(name + "_curl", pts, h * 0.28, mat, sides=6))
    elif ends == "spike":
        for sx in (-1, 1):
            out.append(spike(name + "_sp", (sx * (half - 0.01), y - droop - h * 0.5, 0), (sx * (half + 0.05), y - droop - h * 0.5 + 0.035, 0), h * 0.42, mat))
    if langet:
        lo = [(-0.012, y - 0.004), (0.012, y - 0.004), (0.008, y + langet), (-0.008, y + langet)]
        out.append(slab(name + "_langet", lo, mat, t * 1.05, t * 1.05, bevel=0.001))
    # the block in the middle
    blk = [(-0.022, y - h * 1.25), (0.022, y - h * 1.25), (0.026, y + 0.004), (-0.026, y + 0.004)]
    out.append(slab(name + "_block", blk, mat, t * 1.15, t * 1.25, inset=0.003, bevel=0.0015))
    return out


def haft(y0, y1, r, mat=WOOD, bands=(), band_mat=IRON, grip_at=None, butt=True, name="haft"):
    """A wooden haft with iron bands (list of y), an optional leather grip (y0, y1) and an iron butt cap."""
    L = y1 - y0
    prof = [(r * 0.9, 0), (r, 0.01)] + [(r * (1 - 0.06 * (k / 6)), L * k / 6) for k in range(1, 6)] + [(r * 0.94, L - 0.01), (r * 0.85, L)]
    parts = [lathe(name, prof, mat, seg=12, y0=y0)]
    for b in bands:
        parts += ferrule(b - 0.012, 0.024, r * 1.18, band_mat, name=name + "_band")
    if grip_at:
        parts += grip(grip_at[0], grip_at[1], r * 1.08, LEATHER, name=name + "_grip")
    if butt:
        prof = [(r * 1.15, 0.03), (r * 1.2, 0.0), (r * 0.6, -0.03), (0, -0.04)]
        parts.append(lathe(name + "_butt", prof, band_mat, seg=12, y0=y0))
    return parts


def rivets(points, r=0.006, mat=IRON, axis_z=1):
    return [ball("rivet", p, r, mat, sz=0.55, seg=8, rings_=5) for p in points]


# ------------------------------------------------------------------ weapons
@weapon
def sword_knight():
    p = blade(0.02, 1.07, 0.058, 0.044, 0.009, tip=0.16, fuller=0.016)
    p += crossguard(0.02, 0.12, droop=0.008, flare=0.4, ends="ball")
    p += grip(-0.17, -0.005, 0.0145, sz=0.75)
    p += ferrule(-0.012, 0.012, 0.016, sz=0.75)
    p += pommel(-0.17, "wheel", r=0.028)
    return p


@weapon
def katana():
    p = blade(0.035, 1.075, 0.033, 0.028, 0.0075, tip=0.07, single=True, curve=0.055, n=44)
    hab = [(-0.018, 0.0), (0.018, 0.0), (0.019, 0.036), (-0.017, 0.036)]
    p.append(slab("habaki", hab, BRASS, 0.014, 0.014, bevel=0.0015))
    o = lathe("tsuba", [(0.0, -0.004), (0.045, -0.004), (0.05, -0.002), (0.05, 0.002), (0.045, 0.004), (0.0, 0.004)], IRON, seg=32, sx=1.0, sz=0.85)
    p.append(o)
    p += ferrule(-0.03, 0.026, 0.0165, IRON, sz=0.78, name="fuchi")
    p += grip(-0.17, -0.03, 0.0145, LEATHER, sz=0.78, turns=6, cord=0.0045)
    p += ferrule(-0.172, 0.02, 0.0165, IRON, sz=0.78, name="kashira")
    return p


@weapon
def axe():
    p = haft(-0.2, 0.86, 0.017, WOOD, bands=(0.62, 0.86), grip_at=(-0.17, 0.12))
    # bearded head on +X, a short hammer poll on -X
    p.append(axe_bit(1, 0.0, 0.0))
    socket = lathe("socket", [(0.026, 0.0), (0.03, 0.02), (0.03, 0.2), (0.026, 0.22)], IRON, seg=8, y0=0.65, sz=1.4)
    p.append(socket)
    poll = [(-0.06, 0.70), (0.0, 0.69), (0.0, 0.83), (-0.06, 0.82)]
    p.append(slab("poll", poll, IRON, 0.04, 0.045, bevel=0.003))
    p += rivets([(0.02, 0.73, 0.016), (0.02, 0.79, 0.016), (0.02, 0.73, -0.016), (0.02, 0.79, -0.016)])
    return p


@weapon
def mace():
    p = haft(-0.18, 0.5, 0.016, IRON, bands=(0.14,), band_mat=IRON, grip_at=(-0.16, 0.1))
    p += ferrule(0.36, 0.04, 0.026, IRON)
    core = lathe("core", [(0.0, 0.4), (0.03, 0.41), (0.04, 0.47), (0.036, 0.56), (0.02, 0.6), (0.0, 0.62)], IRON, seg=16)
    p.append(core)
    for k in range(7):
        a = 2 * math.pi * k / 7
        fl = [(0.0, 0.41), (0.05, 0.43), (0.085, 0.5), (0.08, 0.55), (0.045, 0.6), (0.0, 0.61)]
        o = slab("flange", fl, IRON, 0.006, 0.014, inset=0.006, bevel=0.001)
        place(o, rot=(0, math.degrees(a), 0)); p.append(o)
    p += pommel(-0.18, "ball", r=0.022)
    return p


@weapon
def shield_round():
    # wooden planks (face +Z), iron rim, central boss, studs
    R, T = 0.36, 0.022
    parts = []
    for k in range(-3, 4):
        x0, x1 = k * 0.1 - 0.05, k * 0.1 + 0.05
        out = []
        for i in range(9):
            y = -R + 2 * R * i / 8
            if abs(y) >= R: continue
        # plank outline = intersection of the strip with the circle
        pts = []
        N = 10
        for i in range(N + 1):
            x = x0 + (x1 - x0) * i / N
            if abs(x) < R: pts.append((x, -math.sqrt(R * R - x * x) * 0.985))
        for i in range(N + 1):
            x = x1 - (x1 - x0) * i / N
            if abs(x) < R: pts.append((x, math.sqrt(R * R - x * x) * 0.985))
        if len(pts) >= 4:
            o = slab("plank", [(max(-R, min(R, x)) * 0.995, y) for x, y in pts], WOOD, T, T, bevel=0.003)
            parts.append(o)
    rim = []
    for i in range(49):
        a = 2 * math.pi * i / 48; rim.append((R * math.cos(a), R * math.sin(a), 0.0))
    parts.append(tube("rim", rim, 0.016, IRON, sides=8))
    boss = lathe("boss", [(0.11, 0.0), (0.105, 0.012), (0.085, 0.02), (0.075, 0.05), (0.05, 0.075), (0.0, 0.085)], IRON, seg=28)
    place(boss, rot=(90, 0, 0), loc=(0, 0, T / 2 - 0.002)); parts.append(boss)
    pts = [(0.25 * math.cos(2 * math.pi * k / 12), 0.25 * math.sin(2 * math.pi * k / 12), T / 2 + 0.002) for k in range(12)]
    parts += rivets(pts, r=0.009)
    # two iron straps
    for yy in (0.17, -0.17):
        w = math.sqrt(R * R - yy * yy) * 0.98
        o = slab("strap", [(-w, yy - 0.022), (w, yy - 0.022), (w, yy + 0.022), (-w, yy + 0.022)], IRON, 0.006, 0.006, bevel=0.0015, pos=(0, 0, T / 2 + 0.002))
        parts.append(o)
    return parts


@weapon
def staff_crystal():
    p = haft(-0.75, 0.86, 0.019, WOOD, bands=(-0.4, 0.3, 0.84), grip_at=(-0.12, 0.14))
    # iron claws gripping a crystal
    for k in range(4):
        a = 2 * math.pi * k / 4 + 0.4
        pts = []
        for i in range(12):
            s = i / 11
            r = 0.02 + 0.075 * math.sin(math.pi * s * 0.85)
            pts.append((r * math.cos(a), 0.86 + s * 0.27, r * math.sin(a)))
        p.append(tube("claw", pts, 0.008, IRON, sides=6, radii=[0.011 - 0.006 * i / 11 for i in range(12)]))
    cry = lathe("crystal", [(0.0, 0.0), (0.035, 0.04), (0.05, 0.12), (0.03, 0.2), (0.0, 0.25)], glow("b884ff"), seg=6, y0=0.9)
    p.append(cry)
    p += ferrule(0.84, 0.05, 0.026, IRON)
    return p


# ------------------------------------------------------------------ more parts
def placeall(parts, rot=(0, 0, 0), loc=(0, 0, 0)):
    for o in parts:
        place(o, rot, loc)
    return parts


def axe_bit(side=1, y_shift=0.0, sx=1.0, sy=1.0, mat=IRON, base=0.6, name="axe_bit", reach=1.0):
    """A bearded axe blade on side +1 (+X) or -1, socket at x = 0. Sharpened steel bevel along the outline."""
    pts = [(0.02, 0.80), (0.10, 0.805), (0.18, 0.83), (0.245, 0.885), (0.27, 0.90), (0.285, 0.87), (0.293, 0.82),
           (0.29, 0.76), (0.278, 0.70), (0.258, 0.645), (0.235, 0.615), (0.21, 0.63), (0.16, 0.68), (0.1, 0.715),
           (0.05, 0.73), (0.02, 0.735)]
    pts = [(side * x * sx * reach if x > 0.05 else side * x * sx, base + (y - 0.6) * sy + y_shift) for x, y in pts]
    if side < 0:
        pts = list(reversed(pts))
    return slab(name, pts, mat, 0.004, 0.028, inset=0.028 * sx, bevel=0.0012, edge_mat=STEEL)


def sword(blade_kw, guard_kw, grip_y=-0.16, grip_r=0.0145, pommel_kind="wheel", pommel_r=0.028, fit=IRON, gem=None,
          grip_mat=LEATHER):
    p = blade(**blade_kw)
    p += crossguard(**dict(dict(mat=fit), **guard_kw))
    p += grip(grip_y, -0.005, grip_r, grip_mat, sz=0.75)
    p += ferrule(-0.012, 0.012, grip_r + 0.0015, fit, sz=0.75)
    p += pommel(grip_y, pommel_kind, r=pommel_r, mat=fit, gem=gem)
    return p


def knuckle_bow(side=-1, top=0.0, bottom=-0.15, out=0.065, r=0.006, mat=IRON):
    pts = []
    for i in range(13):
        s = i / 12
        y = top + (bottom - top) * s
        x = side * (0.02 + out * math.sin(math.pi * s) ** 0.8)
        pts.append((x, y, 0))
    return tube("knuckle", pts, r, mat, sides=6)


def flat_ring(name, R0, R1, t, mat, seg=48, center=(0, 0, 0), sharp_out=True):
    """A flat annulus facing +Z (chakram, shield rims)."""
    prof = [(R0, -t / 2), (R1 - (0.02 if sharp_out else 0), -t / 2), (R1, 0), (R1 - (0.02 if sharp_out else 0), t / 2), (R0, t / 2), (R0, -t / 2)]
    o = lathe(name, prof, mat, seg=seg, caps=False)
    return place(o, rot=(90, 0, 0), loc=center)


# ------------------------------------------------------------------ swords
@weapon
def shortsword():
    return sword(dict(y0=0.02, length=0.72, w0=0.06, w1=0.05, t=0.009, tip=0.13, fuller=0.014),
                 dict(y=0.02, half=0.09, ends="ball"), grip_y=-0.14, pommel_r=0.026)


@weapon
def sword_royal():
    p = sword(dict(y0=0.03, length=1.16, w0=0.062, w1=0.045, t=0.009, tip=0.17, fuller=0.018),
              dict(y=0.03, half=0.125, droop=0.03, flare=0.6, ends="curl", langet=0.045), grip_y=-0.16,
              pommel_kind="scent", pommel_r=0.022, fit=BRASS, gem="ff5a6e")
    p.append(ball("guard_gem", (0, 0.012, 0.016), 0.008, glow("ff5a6e"), sz=0.5))
    p.append(ball("guard_gem_b", (0, 0.012, -0.016), 0.008, glow("ff5a6e"), sz=0.5))
    return p


@weapon
def sword_shadow():
    p = sword(dict(y0=0.03, length=1.09, w0=0.064, w1=0.04, t=0.01, tip=0.2, fuller=0.016, mat=IRON, glow_fuller="b884ff"),
              dict(y=0.03, half=0.12, droop=-0.035, ends="spike", flare=0.2), grip_y=-0.16,
              pommel_kind="spike", pommel_r=0.022, grip_mat=CLOTH_B)
    # barbs near the root
    for sx in (-1, 1):
        for k in range(2):
            y = 0.1 + k * 0.07
            p.append(spike("barb", (sx * 0.028, y, 0), (sx * 0.05, y + 0.04, 0), 0.006, IRON, sides=4))
    return p


@weapon
def sword_azure():
    return sword(dict(y0=0.03, length=1.11, w0=0.06, w1=0.045, t=0.009, tip=0.16, fuller=0.016, glow_fuller="6fe8ff"),
                 dict(y=0.03, half=0.125, droop=-0.025, flare=0.3, ends="curl"), grip_y=-0.16, gem="6fe8ff")


@weapon
def sword_ember():
    return sword(dict(y0=0.03, length=1.01, w0=0.06, w1=0.064, t=0.01, tip=0.2, fuller=0.016, glow_fuller="ff9a3a"),
                 dict(y=0.03, half=0.14, flare=0.8, ends="ball", droop=0.01), grip_y=-0.16, pommel_kind="pear",
                 pommel_r=0.024, fit=BRASS)


@weapon
def sword_frost():
    p = sword(dict(y0=0.03, length=1.13, w0=0.058, w1=0.042, t=0.009, tip=0.18, fuller=0.014, glow_fuller="e6fbff"),
              dict(y=0.03, half=0.13, ends="spike", droop=0.015), grip_y=-0.16, pommel_kind="scent", pommel_r=0.022,
              gem="9af0ff")
    for sx in (-1, 1):
        p.append(spike("frost_shard", (sx * 0.02, 0.02, 0.0), (sx * 0.05, 0.075, 0.0), 0.007, glow("9af0ff"), sides=5))
    return p


@weapon
def sword_verdant():
    p = sword(dict(y0=0.03, length=0.99, w0=0.062, w1=0.046, t=0.009, tip=0.16, fuller=0.016),
              dict(y=0.03, half=0.11, ends="curl", droop=0.02), grip_y=-0.16, pommel_kind="ring", pommel_r=0.03)
    # a thorned vine coiled round the ricasso, and a green stone in the guard
    pts = [(0.022 * math.cos(a), 0.035 + a * 0.008, 0.012 * math.sin(a)) for a in [k * 0.4 for k in range(24)]]
    p.append(tube("vine", pts, 0.0035, WOOD, sides=5))
    p.append(ball("guard_gem", (0, 0.012, 0.016), 0.009, glow("8fffb0"), sz=0.5))
    p.append(ball("guard_gem_b", (0, 0.012, -0.016), 0.009, glow("8fffb0"), sz=0.5))
    return p


@weapon
def greatsword_sun():
    p = blade(0.05, 1.63, 0.11, 0.08, 0.013, tip=0.22, fuller=0.03)
    p += crossguard(0.05, 0.22, h=0.03, t=0.034, flare=0.5, ends="ball", mat=BRASS, droop=0.02)
    p += grip(-0.24, -0.005, 0.0175, LEATHER, sz=0.78)
    p += ferrule(-0.012, 0.016, 0.019, BRASS, sz=0.78)
    p += pommel(-0.24, "wheel", r=0.036, mat=BRASS, gem="ffd25a")
    # sun disc on the guard block
    o = lathe("sun", [(0, -0.004), (0.026, -0.004), (0.03, 0), (0.026, 0.004), (0, 0.004)], BRASS, seg=16)
    p.append(place(o, rot=(90, 0, 0), loc=(0, 0.03, 0.02)))
    p.append(ball("sun_gem", (0, 0.03, 0.025), 0.012, glow("ffd25a"), sz=0.5))
    return p


@weapon
def claymore_storm():
    p = blade(0.04, 1.7, 0.078, 0.058, 0.011, tip=0.2, fuller=0.02, glow_fuller="c8d4ff")
    p += crossguard(0.04, 0.2, h=0.024, t=0.03, droop=-0.07, ends="ball")
    for sx in (-1, 1):   # quatrefoil rings at the quillon ends
        p.append(ring("foil", (sx * 0.215, 0.04 + 0.07 - 0.012, 0), 0.022, 0.005, IRON, axis="z", seg=20, sides=6))
    p += grip(-0.26, -0.005, 0.017, LEATHER, sz=0.78)
    p += ferrule(-0.012, 0.016, 0.0185, IRON, sz=0.78)
    p += pommel(-0.26, "wheel", r=0.032, gem="b0c0ff")
    return p


@weapon
def saber():
    p = blade(0.03, 0.9, 0.034, 0.03, 0.008, tip=0.1, single=True, curve=0.07)
    p += crossguard(0.03, 0.05, h=0.016, t=0.022, ends="ball")
    p.append(knuckle_bow(-1, 0.02, -0.15))
    p += grip(-0.15, -0.005, 0.0135, LEATHER, sz=0.75)
    p += pommel(-0.15, "pear", r=0.018)
    return p


@weapon
def saber_moon():
    p = blade(0.03, 0.9, 0.036, 0.032, 0.008, tip=0.12, single=True, curve=0.075, mat=IRON)
    p += crossguard(0.03, 0.05, h=0.016, t=0.022, ends="spike")
    p.append(knuckle_bow(-1, 0.02, -0.15))
    p += grip(-0.15, -0.005, 0.0135, CLOTH_B, sz=0.75)
    p += pommel(-0.15, "pear", r=0.018)
    o = ring("moon", (0.0, 0.018, 0.0), 0.012, 0.003, glow("b884ff"), axis="y", seg=16, sides=5)
    p.append(o)
    return p


@weapon
def cutlass():
    p = blade(0.03, 0.77, 0.045, 0.058, 0.008, tip=0.13, single=True, curve=0.045, clip=0.25)
    shell = lathe("shell", [(0.0, 0.0), (0.05, -0.004), (0.075, -0.018), (0.08, -0.03), (0.078, -0.034)], IRON, seg=24, sx=1.0, sz=0.7, y0=0.03, caps=False)
    p.append(shell)
    p.append(knuckle_bow(-1, 0.0, -0.14, out=0.06))
    p += grip(-0.14, -0.005, 0.014, LEATHER, sz=0.75)
    p += pommel(-0.14, "ball", r=0.018)
    return p


@weapon
def rapier():
    p = blade(0.03, 1.09, 0.022, 0.012, 0.008, tip=0.25, fuller=0.006)
    p += crossguard(0.0, 0.11, h=0.012, t=0.014, ends="ball")
    for sz in (-1, 1):
        p.append(ring("swept", (0, 0.03, sz * 0.03), 0.028, 0.004, IRON, axis="y", seg=20, sides=6))
    p.append(knuckle_bow(-1, -0.005, -0.15, out=0.07, r=0.005))
    p.append(ring("pas", (0, 0.03, 0), 0.03, 0.004, IRON, axis="z", seg=20, sides=6))
    p += grip(-0.15, -0.012, 0.0125, LEATHER, sz=0.85)
    p += pommel(-0.15, "pear", r=0.022)
    p.append(ball("gem", (0, -0.005, 0.012), 0.006, glow("ff8fc0"), sz=0.6))
    return p


@weapon
def dagger():
    return sword(dict(y0=0.015, length=0.42, w0=0.045, w1=0.032, t=0.008, tip=0.12, fuller=0.01),
                 dict(y=0.015, half=0.06, h=0.016, t=0.02, droop=0.012, ends="spike"), grip_y=-0.115, grip_r=0.013,
                 pommel_kind="scent", pommel_r=0.018, gem="b884ff")


@weapon
def kunai():
    p = blade(0.02, 0.29, 0.03, 0.042, 0.007, tip=0.13)
    p += grip(-0.09, 0.02, 0.0095, CLOTH_B, sz=0.9, cord=0.0025)
    p.append(ring("ring", (0, -0.112, 0), 0.02, 0.0045, IRON, axis="z", seg=20, sides=6))
    p += ferrule(-0.095, 0.008, 0.01, IRON)
    return p


@weapon
def claws():
    # a leather grip bar across the fist (along Z), an iron knuckle plate, three hooked blades
    p = [tube("bar", [(0, 0.0, -0.05), (0, 0.0, 0.05)], 0.013, LEATHER, sides=10)]
    for zz in (-0.055, 0.055):
        p.append(slab("side", [(-0.012, -0.012), (0.012, -0.012), (0.014, 0.06), (-0.014, 0.06)], IRON, 0.006, 0.006, bevel=0.001, pos=(0, 0, zz)))
    pl = [(-0.06, 0.045), (0.06, 0.045), (0.065, 0.075), (-0.065, 0.075)]
    p.append(place(slab("plate", pl, IRON, 0.03, 0.034, inset=0.004, bevel=0.0015), rot=(0, 90, 0)))
    for zz in (-0.04, 0.0, 0.04):
        b = blade(0.07, 0.25, 0.022, 0.014, 0.006, tip=0.08, single=True, curve=0.06, name="claw", n=20)
        p += placeall(b, rot=(0, 90, 0), loc=(0, 0, zz))
    return p


@weapon
def chakram():
    p = [flat_ring("chakram", 0.2, 0.28, 0.012, STEEL)]
    p.append(flat_ring("inner", 0.17, 0.205, 0.016, IRON, sharp_out=False))
    for k in range(8):   # teeth
        a = 2 * math.pi * k / 8
        c, s = math.cos(a), math.sin(a)
        p.append(spike("tooth", (c * 0.26, s * 0.26, 0), (c * 0.3 - s * 0.03, s * 0.3 + c * 0.03, 0), 0.008, STEEL, sides=4))
    arc = [(0.185 * math.cos(a), 0.185 * math.sin(a), 0) for a in [math.pi * 0.6 + k * 0.1 for k in range(9)]]
    p.append(tube("wrap", arc, 0.016, LEATHER, sides=8))
    for k in range(4):
        a = 2 * math.pi * k / 4 + 0.785
        p.append(ball("rune", (0.19 * math.cos(a), 0.19 * math.sin(a), 0.009), 0.007, glow("ff9a3a"), sz=0.4))
    return p


# ------------------------------------------------------------------ axes, hammers, maces
@weapon
def axe_double():
    p = haft(-0.2, 0.86, 0.017, WOOD, bands=(0.62, 0.86), grip_at=(-0.17, 0.12))
    p.append(axe_bit(1)); p.append(axe_bit(-1))
    p.append(lathe("socket", [(0.026, 0.0), (0.03, 0.02), (0.03, 0.2), (0.026, 0.22)], IRON, seg=8, y0=0.65, sz=1.4))
    p.append(spike("top", (0, 0.86, 0), (0, 0.92, 0), 0.016, IRON, sides=6))
    return p


@weapon
def greataxe_void():
    p = haft(-0.5, 0.95, 0.021, WOOD, bands=(-0.2, 0.55, 0.95), grip_at=(-0.45, 0.2))
    for s in (1, -1):
        p.append(axe_bit(s, y_shift=0.0, sx=1.38, sy=1.45, base=0.55))
    p.append(lathe("socket", [(0.03, 0.0), (0.035, 0.03), (0.035, 0.33), (0.03, 0.36)], IRON, seg=8, y0=0.58, sz=1.4))
    p.append(spike("top", (0, 0.94, 0), (0, 1.01, 0), 0.02, IRON, sides=6))
    for s in (1, -1):
        pts = [(s * (0.08 + 0.2 * t), 0.72 + 0.04 * math.sin(t * 3), 0.016) for t in [k / 8 for k in range(9)]]
        p.append(tube("rune", pts, 0.0028, glow("b884ff"), sides=5))
        p.append(tube("rune_b", [(x, y, -z) for x, y, z in pts], 0.0028, glow("b884ff"), sides=5))
    return p


@weapon
def greathammer():
    p = haft(-0.5, 0.78, 0.022, WOOD, bands=(-0.2, 0.5), grip_at=(-0.45, 0.15))
    blk = lathe("block", [(0.0, -0.12), (0.1, -0.12), (0.115, -0.1), (0.115, 0.1), (0.1, 0.12), (0.0, 0.12)], IRON, seg=8, bevel=0.004)
    p.append(place(blk, rot=(0, 0, 90), loc=(0, 0.86, 0)))
    for s in (1, -1):
        face = lathe("face", [(0.0, 0.0), (0.105, 0.0), (0.12, 0.03), (0.13, 0.2), (0.14, 0.215), (0.0, 0.22)], IRON, seg=8, sz=1.15, bevel=0.003)
        p.append(place(face, rot=(0, 0, -90 * s), loc=(s * 0.12, 0.86, 0)))
    p.append(spike("top", (0, 0.97, 0), (0, 1.03, 0), 0.03, IRON, sides=8))
    for s in (1, -1):
        p.append(ring("band_glow", (s * 0.2, 0.86, 0), 0.142, 0.004, glow("ffb04a"), axis="x", seg=8, sides=4))
    return p


@weapon
def warhammer():
    p = haft(-0.27, 0.84, 0.017, WOOD, bands=(0.6,), grip_at=(-0.24, 0.08))
    for s in (1, -1):   # langets
        p.append(slab("langet", [(-0.006, 0.55), (0.006, 0.55), (0.006, 0.8), (-0.006, 0.8)], IRON, 0.004, 0.004, pos=(0, 0, s * 0.017)))
    core = lathe("core", [(0.0, -0.05), (0.034, -0.05), (0.038, -0.04), (0.038, 0.04), (0.034, 0.05), (0.0, 0.05)], IRON, seg=8, bevel=0.002)
    p.append(place(core, rot=(90, 0, 0), loc=(0, 0.8, 0)))
    face = lathe("face", [(0.0, 0.0), (0.03, 0.0), (0.034, 0.06), (0.045, 0.12), (0.045, 0.14), (0.0, 0.142)], IRON, seg=4, bevel=0.002)
    p.append(place(face, rot=(0, 45, -90), loc=(0.0, 0.8, 0)))
    p.append(spike("beak", (-0.03, 0.8, 0), (-0.17, 0.75, 0), 0.022, IRON, sides=4))
    p.append(spike("top", (0, 0.84, 0), (0, 0.94, 0), 0.016, IRON, sides=4))
    p.append(ball("gem", (0, 0.8, 0.036), 0.008, glow("6fe8ff"), sz=0.5))
    return p


@weapon
def morningstar():
    p = haft(-0.18, 0.46, 0.017, WOOD, bands=(0.1, 0.44), grip_at=(-0.16, 0.08))
    p.append(ball("head", (0, 0.535, 0), 0.075, IRON, seg=16, rings_=10))
    pts = []
    n = 14
    for k in range(n):   # Fibonacci spread of spikes
        yv = 1 - 2 * (k + 0.5) / n; rr = math.sqrt(1 - yv * yv); a = k * 2.39996
        d = Vector((rr * math.cos(a), yv, rr * math.sin(a)))
        if d.y < -0.8:
            continue
        c = Vector((0, 0.535, 0))
        p.append(spike("spike", tuple(c + d * 0.065), tuple(c + d * 0.145), 0.016, IRON, sides=5))
    return p


@weapon
def scepter():
    p = [lathe("rod", [(0.011, 0.0), (0.012, 0.4), (0.011, 0.78)], BRASS, seg=10, y0=-0.16)]
    p += grip(-0.12, 0.08, 0.0135, LEATHER, sz=1.0)
    for y in (0.1, 0.3, 0.48):
        p += ferrule(y, 0.02, 0.017, BRASS)
    p += pommel(-0.16, "pear", r=0.02, mat=BRASS)
    for k in range(6):   # crown of gothic blades around the stone
        a = 360 * k / 6
        bl = [(0.0, 0.0), (0.02, 0.0), (0.05, 0.06), (0.04, 0.12), (0.02, 0.15), (0.0, 0.08)]
        o = slab("crown", bl, BRASS, 0.004, 0.008, inset=0.004, bevel=0.001)
        p.append(place(o, rot=(0, a, 0), loc=(0, 0.5, 0)))
    p.append(ball("stone", (0, 0.575, 0), 0.03, glow("ffd25a"), seg=8, rings_=6))
    return p


# ------------------------------------------------------------------ polearms
@weapon
def spear():
    p = haft(-0.95, 1.12, 0.016, WOOD, bands=(-0.5, 0.4), grip_at=(-0.15, 0.15))
    p += ferrule(1.08, 0.08, 0.02, IRON)
    p += blade(1.15, 0.26, 0.05, 0.04, 0.012, tip=0.14, fuller=0.008)
    for k in range(5):   # a short red tassel under the head
        a = 2 * math.pi * k / 5
        pts = [(0.012 * math.cos(a) + 0.004 * t, 1.08 - t * 0.12, 0.012 * math.sin(a)) for t in [i / 5 for i in range(6)]]
        p.append(tube("tassel", pts, 0.004, CLOTH, sides=4))
    return p


@weapon
def lance():
    p = [lathe("shaft", [(0.02, 0.0), (0.05, 0.05), (0.045, 0.5), (0.03, 1.0), (0.016, 1.35), (0.0, 1.45)], WOOD, seg=16, y0=0.2)]
    for y in (0.4, 0.8, 1.15):
        r = 0.045 - (y - 0.2) * 0.025
        p += ferrule(y, 0.02, r + 0.004, IRON)
    p.append(lathe("tip", [(0.02, 0.0), (0.024, 0.02), (0.0, 0.1)], IRON, seg=8, y0=1.55))
    p.append(lathe("vamplate", [(0.02, 0.0), (0.1, 0.03), (0.098, 0.04), (0.03, 0.18), (0.0, 0.2)], IRON, seg=24, y0=0.06, caps=True))
    p += grip(-0.18, 0.06, 0.02, LEATHER, sz=1.0)
    p.append(lathe("butt", [(0.024, 0.0), (0.035, -0.08), (0.03, -0.24), (0.0, -0.27)], WOOD, seg=12, y0=-0.18))
    return p


@weapon
def halberd():
    p = haft(-0.9, 1.22, 0.017, WOOD, bands=(-0.4, 0.4), grip_at=(-0.2, 0.15))
    p += ferrule(0.95, 0.27, 0.022, IRON)
    p.append(axe_bit(1, sx=0.8, sy=0.95, base=0.4 + 0.6))
    hook = [(-0.02, 1.08), (-0.08, 1.1), (-0.14, 1.14), (-0.165, 1.18), (-0.13, 1.15), (-0.07, 1.14), (-0.02, 1.15)]
    p.append(slab("hook", hook, IRON, 0.006, 0.014, inset=0.006, bevel=0.001))
    p += blade(1.2, 0.22, 0.032, 0.026, 0.012, tip=0.12)
    return p


@weapon
def glaive_tide():
    p = haft(-0.8, 0.95, 0.017, WOOD, bands=(-0.3, 0.5), grip_at=(-0.2, 0.15))
    p += ferrule(0.92, 0.06, 0.021, IRON)
    p += blade(0.97, 0.45, 0.07, 0.085, 0.01, tip=0.16, single=True, curve=-0.1)
    for k in range(4):
        pts = [(-0.02 - 0.02 * t - 0.01 * k, 0.94 - t * 0.2, 0.008 * (k - 1.5)) for t in [i / 5 for i in range(6)]]
        p.append(tube("tassel", pts, 0.005, CLOTH, sides=4))
    return p


@weapon
def trident():
    p = haft(-0.85, 1.0, 0.017, WOOD, bands=(-0.3, 0.6), grip_at=(-0.2, 0.15))
    fork = [(-0.14, 1.0), (0.14, 1.0), (0.15, 1.05), (0.0, 1.03), (-0.15, 1.05)]
    p.append(slab("fork", fork, IRON, 0.02, 0.02, bevel=0.002))
    p += ferrule(0.96, 0.05, 0.022, IRON)
    p += blade(1.02, 0.35, 0.03, 0.022, 0.012, tip=0.1)
    for s in (1, -1):
        pts = [(s * (0.14 - 0.02 * t * t), 1.04 + t * 0.24, 0) for t in [i / 8 for i in range(9)]]
        p.append(tube("prong", pts, 0.009, IRON, sides=6, radii=[0.01 - 0.007 * i / 8 for i in range(9)]))
        p.append(spike("barb", (s * 0.125, 1.24, 0), (s * 0.1, 1.2, 0), 0.006, IRON, sides=4))
    p.append(ball("gem", (0, 1.025, 0.012), 0.009, glow("6fe8ff"), sz=0.5))
    return p


@weapon
def scythe_reaper():
    pts = [(0.0, -0.7 + 1.8 * t, 0.0) for t in [i / 10 for i in range(11)]]
    pts = [(0.03 * math.sin(t * math.pi), y, z) for (x, y, z), t in zip(pts, [i / 10 for i in range(11)])]
    p = [tube("snath", pts, 0.018, WOOD, sides=10)]
    for y in (-0.2, 0.4):
        p += ferrule(y, 0.025, 0.021, IRON)
    p += grip(-0.1, 0.12, 0.02, LEATHER, sz=1.0)
    b = blade(0.0, 0.76, 0.09, 0.05, 0.01, tip=0.2, single=True, curve=-0.16, n=30, name="scythe_blade")
    p += placeall(b, rot=(0, 0, -96), loc=(0.0, 1.06, 0))
    p += ferrule(1.0, 0.1, 0.024, IRON)
    p.append(ball("gem", (0.02, 1.06, 0.02), 0.012, glow("b884ff"), sz=0.5))
    return p


# ------------------------------------------------------------------ bows
def _bow(mat=WOOD, tip_mat=IRON, gem=None, length=0.62, back=0.2):
    pts, radii = [], []
    N = 24
    for i in range(N + 1):
        s = -1 + 2 * i / N
        y = s * length
        x = -back * abs(s) ** 1.6 + 0.03 * abs(s) ** 6   # tips recurve forward
        pts.append((x, y, 0)); radii.append(0.019 - 0.011 * abs(s))
    p = [tube("limbs", pts, 0.01, mat, sides=8, radii=radii)]
    p += grip(-0.07, 0.07, 0.021, LEATHER, sz=0.8)
    tips = (pts[0], pts[-1])
    for t in tips:
        p.append(ball("nock", t, 0.009, tip_mat))
    p.append(tube("string", [tips[0], tips[1]], 0.0018, CLOTH_B, sides=4))
    if gem:
        p.append(ball("gem", (0.02, 0.0, 0.0), 0.012, glow(gem), sz=0.6))
    return p


@weapon
def bow_wood():
    return _bow()


@weapon
def bow_spirit():
    p = _bow(mat=WOOD, gem="6fe8ff")
    for s in (1, -1):
        pts = [(-0.2 * t ** 1.6 + 0.012, s * t * 0.62, 0) for t in [0.15 + i * 0.05 for i in range(10)]]
        p.append(tube("rune", pts, 0.0025, glow("6fe8ff"), sides=4))
    return p


@weapon
def longbow_gilded():
    p = _bow(mat=WOOD, tip_mat=BRASS, gem="ffd25a")
    for y in (0.09, -0.09, 0.35, -0.35):
        p += ferrule(y - 0.01, 0.02, 0.019 - 0.011 * abs(y) / 0.62 + 0.002, BRASS, sx=1.0, sz=1.0)
    return p


@weapon
def crossbow():
    stock = [(-0.2, -0.1), (-0.05, -0.15), (0.0, -0.12), (0.06, -0.02), (0.48, -0.02), (0.48, 0.03), (-0.2, 0.03)]
    p = [place(slab("stock", stock, WOOD, 0.04, 0.045, inset=0.006, bevel=0.003), rot=(0, -90, 0))]
    pts, radii = [], []
    for i in range(17):
        s = -1 + 2 * i / 16
        pts.append((s * 0.28, 0.0, 0.42 - 0.07 * s * s)); radii.append(0.012 - 0.005 * abs(s))
    p.append(tube("prod", pts, 0.01, IRON, sides=6, radii=radii))
    p.append(tube("string", [pts[0], (0, 0.03, 0.18), pts[-1]], 0.002, CLOTH_B, sides=4))
    p.append(ferrule(-0.01, 0.02, 0.006, IRON)[0])
    p.append(slab("stirrup", [(-0.05, 0.0), (0.05, 0.0), (0.04, 0.08), (-0.04, 0.08)], IRON, 0.008, 0.008, bevel=0.001, rot=None, pos=(0, 0, 0)))
    place(p[-1], rot=(90, 0, 0), loc=(0, 0.0, 0.48))
    return p


# ------------------------------------------------------------------ staffs, wands, books, orbs
def _crook_staff(gem):
    p = haft(-0.86, 0.9, 0.019, WOOD, bands=(-0.4, 0.3), grip_at=(-0.12, 0.14))
    pts = [(0.15 * (1 - math.cos(a)), 0.9 + 0.17 * math.sin(a), 0) for a in [k * 0.21 for k in range(16)]]
    p.append(tube("crook", pts, 0.014, IRON, sides=8, radii=[0.016 - 0.008 * k / 15 for k in range(16)]))
    tip = pts[-1]
    p.append(tube("chain", [tip, (tip[0], tip[1] - 0.08, 0)], 0.003, IRON, sides=4))
    cy = tip[1] - 0.14
    for k in range(4):   # cage around the hanging stone
        a = 2 * math.pi * k / 4
        cg = [(tip[0] + 0.035 * math.sin(math.pi * t) * math.cos(a), cy + 0.06 - 0.12 * t, 0.035 * math.sin(math.pi * t) * math.sin(a)) for t in [i / 8 for i in range(9)]]
        p.append(tube("cage", cg, 0.0035, IRON, sides=4))
    p.append(ball("stone", (tip[0], cy, 0), 0.026, glow(gem), seg=8, rings_=6))
    p += ferrule(0.86, 0.05, 0.024, IRON)
    return p


@weapon
def staff_azure():
    return _crook_staff("6fe8ff")


@weapon
def staff_ember():
    return _crook_staff("ff9a3a")


@weapon
def staff_verdant():
    p = haft(-0.75, 0.8, 0.019, WOOD, bands=(-0.4,), grip_at=(-0.12, 0.14))
    for k in range(3):   # living branches twisting up round a green stone
        a0 = 2 * math.pi * k / 3
        pts = []
        for i in range(16):
            t = i / 15
            r = 0.02 + 0.07 * math.sin(math.pi * min(1, t * 1.1))
            a = a0 + t * 2.2
            pts.append((r * math.cos(a), 0.75 + t * 0.38, r * math.sin(a)))
        p.append(tube("branch", pts, 0.01, WOOD, sides=6, radii=[0.016 - 0.012 * i / 15 for i in range(16)]))
    p.append(lathe("stone", [(0.0, 0.0), (0.03, 0.04), (0.04, 0.1), (0.025, 0.16), (0.0, 0.2)], glow("8fffb0"), seg=6, y0=0.86))
    return p


def _wand(gem_kind, gem):
    p = [lathe("rod", [(0.0, -0.09), (0.012, -0.08), (0.013, 0.0), (0.009, 0.3), (0.006, 0.38), (0.0, 0.39)], WOOD, seg=10)]
    p += grip(-0.07, 0.07, 0.0135, LEATHER, sz=1.0, cord=0.0025)
    p += ferrule(0.07, 0.015, 0.0145, IRON)
    if gem_kind == "frost":
        for k in range(5):
            a = 2 * math.pi * k / 5
            p.append(spike("shard", (0.0, 0.36, 0.0), (0.03 * math.cos(a), 0.44 + 0.01 * k, 0.03 * math.sin(a)), 0.008, glow(gem), sides=4))
        p.append(spike("shard_c", (0, 0.36, 0), (0, 0.46, 0), 0.01, glow(gem), sides=4))
    else:
        for k in range(4):
            a = 360 * k / 4
            o = slab("prong", [(0.0, 0.0), (0.012, 0.0), (0.03, 0.04), (0.012, 0.08), (0.0, 0.07)], BRASS, 0.003, 0.004)
            p.append(place(o, rot=(0, a, 0), loc=(0, 0.35, 0)))
        p.append(ball("stone", (0, 0.4, 0), 0.02, glow(gem), seg=8, rings_=6))
    return p


@weapon
def wand_frost():
    return _wand("frost", "9af0ff")


@weapon
def wand_star():
    return _wand("star", "ff8fc0")


def _tome(cover, gem):
    W, H, T = 0.13, 0.17, 0.08
    p = [slab("pages", [(-W + 0.01, -H + 0.008), (W - 0.004, -H + 0.008), (W - 0.004, H - 0.008), (-W + 0.01, H - 0.008)], PARCH, T - 0.012, T - 0.012, pos=(0, 0, 0.01))]
    for z in (0.01 + T / 2 - 0.003, 0.01 - T / 2 + 0.003):
        p.append(slab("cover", [(-W, -H), (W, -H), (W, H), (-W, H)], cover, 0.008, 0.008, bevel=0.002, pos=(0, 0, z)))
    p.append(slab("spine", [(-0.012, -H), (0.0, -H), (0.0, H), (-0.012, H)], cover, T, T, bevel=0.003, pos=(-W, 0, 0.01)))
    for sx in (-1, 1):
        for sy in (-1, 1):
            c = [(0, 0), (0.04, 0), (0, 0.04)]
            o = slab("corner", c, BRASS, 0.01, 0.01, bevel=0.001)
            p.append(place(o, rot=(0, 0, 0 if (sx, sy) == (1, 1) else 90 if (sx, sy) == (-1, 1) else 180 if (sx, sy) == (-1, -1) else 270), loc=(sx * (W - 0.003), sy * (H - 0.003), 0.01 + T / 2)))
    p.append(slab("clasp", [(W - 0.03, -0.015), (W + 0.012, -0.015), (W + 0.012, 0.015), (W - 0.03, 0.015)], BRASS, T + 0.006, T + 0.006, bevel=0.002, pos=(0, 0, 0.01)))
    p.append(place(ring("sigil", (0, 0, 0), 0.05, 0.003, glow(gem), axis="z", seg=24, sides=4), loc=(0, 0, 0.01 + T / 2 + 0.002)))
    for k in range(3):
        a = 2 * math.pi * k / 3 + math.pi / 2
        b = a + 2 * math.pi / 3
        p.append(tube("sigil_l", [(0.05 * math.cos(a), 0.05 * math.sin(a), 0.01 + T / 2 + 0.002), (0.05 * math.cos(b), 0.05 * math.sin(b), 0.01 + T / 2 + 0.002)], 0.0025, glow(gem), sides=4))
    return p


@weapon
def tome_azure():
    return _tome(LEATHER, "6fe8ff")


@weapon
def tome_crimson():
    return _tome(CLOTH, "ffb04a")


def _orb(core, halo):
    p = [ball("orb", (0.04, 0.0, 0.0), 0.075, glow(halo), seg=20, rings_=12)]
    p.append(ball("core", (0.04, 0.0, 0.0), 0.04, glow(core), seg=12, rings_=8))
    for ax in ("x", "y", "z"):
        p.append(ring("cage", (0.04, 0.0, 0.0), 0.085, 0.005, IRON, axis=ax, seg=28, sides=6))
    p.append(tube("stem", [(-0.13, 0.0, 0.0), (-0.045, 0.0, 0.0)], 0.014, IRON, sides=8))
    p += placeall(grip(-0.06, 0.06, 0.015, LEATHER, sz=1.0), rot=(0, 0, 90), loc=(-0.075, 0, 0))
    return p


@weapon
def orb_catalyst():
    return _orb("d4f8ff", "6fe8ff")


@weapon
def orb_catalyst_ember():
    return _orb("ffe1c4", "ff9a3a")


# ------------------------------------------------------------------ shields (face +Z)
@weapon
def buckler():
    p = [place(lathe("dish", [(0.0, 0.07), (0.08, 0.06), (0.17, 0.03), (0.21, 0.0), (0.2, -0.01), (0.0, 0.02)], IRON, seg=36, bevel=0.002), rot=(90, 0, 0))]
    p.append(place(lathe("boss", [(0.06, 0.0), (0.05, 0.02), (0.03, 0.04), (0.0, 0.045)], IRON, seg=20), rot=(90, 0, 0), loc=(0, 0, 0.06)))
    p.append(ring("rim", (0, 0, 0.0), 0.205, 0.008, IRON, axis="z", seg=48, sides=6))
    p += rivets([(0.14 * math.cos(2 * math.pi * k / 8), 0.14 * math.sin(2 * math.pi * k / 8), 0.044) for k in range(8)], r=0.008)
    return p


@weapon
def shield_kite():
    outline = []
    for i in range(13):   # rounded top
        a = math.pi * i / 12
        outline.append((0.3 * math.cos(a), 0.2 + 0.18 * math.sin(a)))
    for i in range(1, 12):   # sides curving to the point
        t = i / 12
        outline.append((-0.3 * (1 - t) ** 0.8, 0.2 - 0.73 * t))
    outline.append((0.0, -0.53))
    for i in range(11, 0, -1):
        t = i / 12
        outline.append((0.3 * (1 - t) ** 0.8, 0.2 - 0.73 * t))
    outline = list(reversed(outline))
    p = [slab("board", outline, CLOTH, 0.03, 0.03, bevel=0.004)]
    rim = [(x * 1.0, y, 0.0) for x, y in outline] + [(outline[0][0], outline[0][1], 0.0)]
    p.append(tube("rim", rim, 0.014, IRON, sides=6))
    # an iron cross charge
    p.append(slab("cross_v", [(-0.03, -0.38), (0.03, -0.38), (0.03, 0.3), (-0.03, 0.3)], IRON, 0.008, 0.008, bevel=0.002, pos=(0, 0, 0.018)))
    p.append(slab("cross_h", [(-0.2, 0.08), (0.2, 0.08), (0.2, 0.14), (-0.2, 0.14)], IRON, 0.008, 0.008, bevel=0.002, pos=(0, 0, 0.018)))
    p.append(ball("gem", (0, 0.11, 0.026), 0.016, glow("6fe8ff"), sz=0.5))
    p += rivets([(x * 0.88, y * 0.92 - 0.01, 0.017) for x, y in outline[::3]], r=0.007)
    return p


@weapon
def shield_tower():
    W, H = 0.27, 0.49
    rows = []
    for i in range(11):   # gently curved board
        s = -1 + 2 * i / 10
        rows.append(s)
    p = []
    for i in range(10):
        x0, x1 = rows[i] * W, rows[i + 1] * W
        z0, z1 = -0.06 * rows[i] ** 2, -0.06 * rows[i + 1] ** 2
        q = Part("plate"); q.bevel = 0.0
        a = [q.v((x0, -H, z0 + 0.01)), q.v((x1, -H, z1 + 0.01)), q.v((x1, H, z1 + 0.01)), q.v((x0, H, z0 + 0.01))]
        b = [q.v((x0, -H, z0 - 0.01)), q.v((x1, -H, z1 - 0.01)), q.v((x1, H, z1 - 0.01)), q.v((x0, H, z0 - 0.01))]
        q.face(a, IRON); q.face(list(reversed(b)), IRON)
        for j in range(4):
            jn = (j + 1) % 4
            q.face((a[j], b[j], b[jn], a[jn]), IRON)
        p.append(q.obj())
    rim = [(s * W, H, -0.06 * s * s) for s in rows] + [(W, -H + 0.0, -0.06)] + [(s * W, -H, -0.06 * s * s) for s in reversed(rows)] + [(-W, H, -0.06)]
    p.append(tube("rim", rim, 0.016, IRON, sides=6))
    for yy in (-0.3, 0.0, 0.3):   # riveted bands
        pts = [(s * W, yy, -0.06 * s * s + 0.012) for s in rows]
        p.append(tube("band", pts, 0.012, IRON, sides=4))
        p += rivets([(s * W * 0.9, yy, -0.06 * (s * 0.9) ** 2 + 0.024) for s in rows[1:-1:2]], r=0.007)
    p.append(ball("gem", (0, 0.15, 0.022), 0.022, glow("ff5a6e"), sz=0.5))
    p.append(ring("gem_ring", (0, 0.15, 0.018), 0.03, 0.006, IRON, axis="z", seg=20, sides=6))
    return p


# ------------------------------------------------------------------ more gothic arms (second wave)
def ricasso(y0, y1, w, t, mat=IRON, hooks=0.0):
    """The blunt square shoulder of a blade above the guard, with optional parrying hooks (parierhaken) at its top."""
    p = [slab("ricasso", [(-w / 2, y0), (w / 2, y0), (w / 2, y1), (-w / 2, y1)], mat, t, t, bevel=0.0012)]
    if hooks:
        for sx in (-1, 1):
            p.append(spike("hook", (sx * w * 0.4, y1 - 0.01, 0), (sx * (w / 2 + hooks), y1 + 0.012, 0), t * 0.45, mat, sides=4))
    return p


@weapon
def bastard_sword():
    p = blade(0.1, 1.0, 0.05, 0.036, 0.009, tip=0.18, fuller=0.014)
    p += ricasso(0.02, 0.11, 0.046, 0.011, hooks=0.018)
    p += crossguard(0.02, 0.13, h=0.02, t=0.026, droop=0.012, flare=0.3, ends="ball")
    p += grip(-0.23, -0.005, 0.015, LEATHER, sz=0.75)
    p += ferrule(-0.012, 0.012, 0.0165, IRON, sz=0.75)
    p += ferrule(-0.125, 0.01, 0.0158, IRON, sz=0.75, name="grip_band")   # the hand-and-a-half split
    p += pommel(-0.23, "scent", r=0.024)
    return p


@weapon
def estoc():
    p = blade(0.03, 1.16, 0.021, 0.011, 0.012, tip=0.32)   # stiff diamond-section thrusting blade
    p += crossguard(0.03, 0.105, h=0.014, t=0.018, droop=-0.012, ends="ball")
    p.append(ring("pas", (0, 0.045, 0), 0.026, 0.0035, IRON, axis="z", seg=20, sides=6))
    p += grip(-0.22, -0.005, 0.0135, LEATHER, sz=0.85)
    p += ferrule(-0.012, 0.012, 0.015, IRON, sz=0.85)
    p += pommel(-0.22, "pear", r=0.022)
    return p


@weapon
def great_cleaver():
    # a raw slab of iron: too big to be called a sword
    out = [(-0.082, 0.05), (0.082, 0.05), (0.086, 0.6), (0.074, 0.635), (0.086, 0.67), (0.088, 1.15), (0.08, 1.19),
           (0.088, 1.22), (0.088, 1.43), (0.03, 1.6), (-0.06, 1.56), (-0.084, 1.5), (-0.084, 0.9), (-0.076, 0.87),
           (-0.084, 0.84)]
    p = [slab("slab", out, IRON, 0.008, 0.03, inset=0.03, bevel=0.002, edge_mat=STEEL)]
    p.append(slab("block", [(-0.1, -0.03), (0.1, -0.03), (0.1, 0.05), (-0.1, 0.05)], RUST, 0.05, 0.05, bevel=0.004))
    p += rivets([(sx * 0.06, 0.18 + k * 0.08, sz * 0.016) for sx in (-1, 1) for k in range(2) for sz in (-1, 1)], r=0.009, mat=RUST)
    p += grip(-0.32, -0.03, 0.019, LEATHER, sz=0.8)
    p += ferrule(-0.335, 0.02, 0.021, RUST, sz=0.8)
    p += pommel(-0.335, "ball", r=0.03, mat=RUST)
    return p


@weapon
def flamberge_wavy():
    p = blade(0.15, 1.48, 0.07, 0.048, 0.011, tip=0.2, fuller=0.016, fuller_end=0.55, wave=0.011, waves=9, n=96)
    p += ricasso(0.04, 0.16, 0.05, 0.013, hooks=0.03)
    p += crossguard(0.04, 0.23, h=0.024, t=0.03, droop=0.03, flare=0.5, ends="curl")
    p += grip(-0.27, -0.005, 0.0175, LEATHER, sz=0.78)
    p += ferrule(-0.012, 0.016, 0.019, IRON, sz=0.78)
    p += pommel(-0.27, "scent", r=0.03, gem="ff5a3a")
    return p


@weapon
def bearded_axe_runed():
    p = haft(-0.2, 0.9, 0.017, WOOD, bands=(0.6,), grip_at=(-0.17, 0.1))
    p.append(axe_bit(1, sx=1.12, sy=1.3, base=0.54))
    p.append(lathe("socket", [(0.026, 0.0), (0.03, 0.02), (0.03, 0.24), (0.026, 0.26)], IRON, seg=8, y0=0.64, sz=1.4))
    p.append(spike("top", (0, 0.9, 0), (0, 0.95, 0), 0.016, IRON, sides=6))
    for s in (1, -1):   # a runic line along the beard
        pts = [(0.07 + 0.17 * t, 0.75 - 0.02 * t + 0.012 * math.sin(t * 14), s * 0.0145) for t in [k / 10 for k in range(11)]]
        p.append(tube("rune", pts, 0.0026, glow("9fc8ff"), sides=5))
    p += grip(0.42, 0.6, 0.0185, CLOTH_B, sz=1.0, cord=0.0035, name="wrap")
    return p


@weapon
def bardiche():
    p = haft(-0.95, 1.12, 0.018, WOOD, bands=(-0.45, 0.45), grip_at=(-0.2, 0.16))
    out = [(0.016, 0.58), (0.05, 0.56), (0.09, 0.64), (0.13, 0.8), (0.155, 1.0), (0.15, 1.2), (0.12, 1.36),
           (0.07, 1.46), (0.05, 1.38), (0.055, 1.2), (0.045, 1.0), (0.016, 0.92)]
    p.append(slab("bardiche", out, IRON, 0.004, 0.02, inset=0.024, bevel=0.0012, edge_mat=STEEL))
    for y in (0.6, 0.9):
        p += ferrule(y - 0.02, 0.04, 0.022, IRON, name="lash")
    p.append(spike("top", (0, 1.12, 0), (0, 1.2, 0), 0.016, IRON, sides=4))
    return p


@weapon
def spiked_club():
    prof = [(0.0, -0.215), (0.016, -0.2), (0.018, -0.17), (0.017, 0.05), (0.025, 0.2), (0.038, 0.38), (0.048, 0.52),
            (0.046, 0.6), (0.03, 0.645), (0.0, 0.655)]
    p = [lathe("club", prof, WOOD, seg=14)]
    p += grip(-0.18, 0.06, 0.019, LEATHER, sz=1.0)
    p += ferrule(0.29, 0.024, 0.034, IRON, name="band")
    p += ferrule(0.555, 0.022, 0.05, IRON, name="band")

    def rad(y):
        for (r0, y0), (r1, y1) in zip(prof, prof[1:]):
            if y0 <= y <= y1:
                return r0 + (r1 - r0) * (y - y0) / (y1 - y0)
        return 0.02
    for i, y in enumerate((0.36, 0.42, 0.48, 0.53, 0.6)):
        for k in range(6):
            a = 2 * math.pi * (k + 0.5 * (i % 2)) / 6
            r = rad(y); c, s = math.cos(a), math.sin(a)
            p.append(spike("nail", (c * r * 0.85, y, s * r * 0.85), (c * (r + 0.038), y + 0.012, s * (r + 0.038)), 0.0055, RUST, sides=4))
    return p


@weapon
def flail_spiked():
    p = haft(-0.18, 0.4, 0.016, WOOD, bands=(0.1,), grip_at=(-0.16, 0.08))
    p += ferrule(0.37, 0.05, 0.02, IRON, name="cap")
    p.append(ring("eye", (0, 0.43, 0), 0.012, 0.0035, IRON, axis="x", seg=12, sides=5))
    for k in range(7):   # the chain, drawn taut as if mid-swing
        p.append(ring("link", (0, 0.45 + k * 0.0235, 0), 0.012, 0.003, IRON, axis="z" if k % 2 == 0 else "x", seg=12, sides=5))
    c = Vector((0, 0.66, 0))
    p.append(ring("eye2", (0, 0.61, 0), 0.011, 0.0035, IRON, axis="x", seg=12, sides=5))
    p.append(ball("head", tuple(c), 0.05, IRON, seg=16, rings_=10))
    n = 14
    for k in range(n):
        yv = 1 - 2 * (k + 0.5) / n; rr = math.sqrt(1 - yv * yv); a = k * 2.39996
        d = Vector((rr * math.cos(a), yv, rr * math.sin(a)))
        if d.y < -0.75:
            continue
        p.append(spike("spike", tuple(c + d * 0.044), tuple(c + d * 0.1), 0.012, IRON, sides=5))
    return p


@weapon
def parrying_dagger():
    p = sword(dict(y0=0.02, length=0.4, w0=0.05, w1=0.022, t=0.009, tip=0.12, fuller=0.008),
              dict(y=0.02, half=0.11, h=0.014, t=0.018, droop=-0.035, ends="ball"), grip_y=-0.12, grip_r=0.013,
              pommel_kind="scent", pommel_r=0.018)
    p.append(ring("side_ring", (0, 0.01, 0.034), 0.028, 0.0038, IRON, axis="x", seg=20, sides=6))
    return p


@weapon
def seax():
    p = blade(0.02, 0.44, 0.04, 0.05, 0.009, tip=0.17, single=True, clip=0.35)
    p += ferrule(-0.012, 0.016, 0.017, BRASS, sz=0.75)
    p += grip(-0.13, -0.012, 0.0145, WOOD, wrap=False, sz=0.75)
    for y in (-0.1, -0.06):
        p += ferrule(y, 0.006, 0.0152, BRASS, sz=0.75, name="band")
    p += ferrule(-0.142, 0.014, 0.016, BRASS, sz=0.75, name="cap")
    return p


@weapon
def winged_spear():
    p = haft(-0.95, 1.1, 0.017, WOOD, bands=(-0.5, 0.4), grip_at=(-0.15, 0.15))
    p += ferrule(1.04, 0.1, 0.021, IRON)
    p += blade(1.15, 0.38, 0.072, 0.03, 0.012, tip=0.2, fuller=0.01)
    for sx in (-1, 1):   # the lugs that stop a boar on the shaft
        p.append(spike("wing", (sx * 0.012, 1.1, 0), (sx * 0.095, 1.15, 0), 0.012, IRON, sides=4))
    p += rivets([(0, 1.07, 0.021), (0, 1.07, -0.021)], r=0.005)
    return p


@weapon
def war_scythe():
    p = haft(-0.95, 0.98, 0.018, WOOD, bands=(-0.45, 0.4), grip_at=(-0.2, 0.15))
    p += ferrule(0.88, 0.12, 0.023, IRON, name="collar")
    p += rivets([(0, 0.92, 0.023), (0, 0.96, 0.023), (0, 0.92, -0.023), (0, 0.96, -0.023)], r=0.005)
    p += blade(0.98, 0.6, 0.06, 0.055, 0.009, tip=0.2, single=True, curve=0.11, n=40)
    return p


@weapon
def recurve_horn():
    pts, radii = [], []
    L, back = 0.56, 0.17
    for i in range(29):
        s = -1 + 2 * i / 28
        a = abs(s)
        x = -back * a ** 1.5 + 0.16 * max(0.0, a - 0.72) ** 1.6   # siyahs flick hard forward
        pts.append((x, s * L, 0)); radii.append(0.02 - 0.012 * a)
    p = [tube("limbs", pts, 0.01, BARK, sides=8, radii=radii)]
    p += grip(-0.07, 0.07, 0.022, LEATHER, sz=0.8)
    for s in (1, -1):
        p += ferrule(s * 0.1 - 0.01, 0.02, 0.0195, BRASS)
        yb = s * 0.42; ab = 0.42 / L
        xb = -back * ab ** 1.5 + 0.16 * max(0.0, ab - 0.72) ** 1.6
        p.append(lathe("horn_band", [(0.0118, -0.008), (0.0128, -0.005), (0.0128, 0.005), (0.0118, 0.008)], BRASS, seg=12, y0=yb, x=xb))
    tips = (pts[0], pts[-1])
    for t in tips:
        p.append(spike("horn_tip", (t[0] - 0.01, t[1] * 0.96, 0), (t[0] + 0.012, t[1] * 1.03, 0), 0.008, PARCH, sides=6))
    p.append(tube("string", [(tips[0][0], tips[0][1] * 0.985, 0), (tips[1][0], tips[1][1] * 0.985, 0)], 0.0018, CLOTH_B, sides=4))
    return p


@weapon
def bone_wand():
    prof = [(0.0, -0.1), (0.011, -0.096), (0.017, -0.085), (0.012, -0.07), (0.0105, 0.0), (0.0085, 0.2), (0.008, 0.3),
            (0.012, 0.325), (0.017, 0.34), (0.012, 0.355), (0.0, 0.362)]
    p = [lathe("bone", prof, PARCH, seg=12)]
    p += grip(-0.06, 0.07, 0.0125, CLOTH_B, sz=1.0, cord=0.0025)
    for y in (0.13, 0.2, 0.26):   # knuckle-like knobs
        p.append(ring("knob", (0, y, 0), 0.0092, 0.0028, PARCH, axis="y", seg=12, sides=5))
    for k in range(3):   # finger-bone claws round the mote
        a = 2 * math.pi * k / 3
        pts = [((0.008 + 0.02 * math.sin(math.pi * t * 0.8)) * math.cos(a), 0.35 + t * 0.07, (0.008 + 0.02 * math.sin(math.pi * t * 0.8)) * math.sin(a)) for t in [i / 6 for i in range(7)]]
        p.append(tube("claw", pts, 0.004, PARCH, sides=5, radii=[0.0045 - 0.003 * i / 6 for i in range(7)]))
    p.append(ball("mote", (0, 0.39, 0), 0.016, glow("c8ffd8"), seg=10, rings_=6))
    return p


@weapon
def rune_rod():
    p = [lathe("rod", [(0.0, -0.1), (0.013, -0.09), (0.012, 0.0), (0.0105, 0.36), (0.014, 0.37), (0.02, 0.39), (0.017, 0.4), (0.0, 0.405)], IRON, seg=10)]
    p += grip(-0.07, 0.07, 0.0135, LEATHER, sz=1.0, cord=0.0025)
    for y in (0.07, 0.2, 0.33):
        p += ferrule(y, 0.016, 0.0145, BRASS, name="band")
    pts = []
    for i in range(61):   # a spiral of glowing runes up the rod
        t = i / 60; a = t * 5 * 2 * math.pi; y = 0.09 + t * 0.24
        pts.append((0.0118 * math.cos(a), y, 0.0118 * math.sin(a)))
    p.append(tube("runes", pts, 0.0018, glow("ffb04a"), sides=4))
    p.append(spike("crystal", (0, 0.39, 0), (0, 0.49, 0), 0.012, glow("ffb04a"), sides=5))
    for k in range(4):
        a = 2 * math.pi * k / 4
        p.append(spike("prong", (0.012 * math.cos(a), 0.39, 0.012 * math.sin(a)), (0.022 * math.cos(a), 0.43, 0.022 * math.sin(a)), 0.003, IRON, sides=4))
    return p


@weapon
def gnarled_staff():
    pts, radii = [], []
    for i in range(31):   # a crooked root-wood shaft
        t = i / 30; y = -0.86 + t * 1.68
        pts.append((0.012 * math.sin(t * 9.0) + 0.006 * math.sin(t * 23), y, 0.01 * math.cos(t * 7.0)))
        radii.append(0.019 + 0.004 * math.sin(t * 17) ** 2 + 0.006 * max(0.0, t - 0.85) / 0.15)
    p = [tube("shaft", pts, 0.02, BARK, sides=9, radii=radii)]
    for t in (0.2, 0.47, 0.71):   # knots
        x, y, z = pts[int(t * 30)]
        p.append(ball("knot", (x + 0.015, y, z), 0.014, BARK, sx=1.0, sy=1.4, seg=8, rings_=6))
    p += grip(-0.12, 0.14, 0.024, LEATHER, sz=1.0)
    top = pts[-1]
    cy = top[1] + 0.17
    for k in range(5):   # roots twisting up into a cage round the crystal
        a0 = 2 * math.pi * k / 5
        rp, rr = [], []
        for i in range(14):
            t = i / 13
            r = 0.018 + 0.06 * math.sin(math.pi * t) ** 0.8
            a = a0 + t * 1.6
            rp.append((top[0] * (1 - t) + r * math.cos(a), top[1] - 0.02 + t * 0.34, top[2] * (1 - t) + r * math.sin(a)))
            rr.append(0.011 - 0.007 * t)
        p.append(tube("root", rp, 0.01, BARK, sides=6, radii=rr))
    p.append(lathe("crystal", [(0.0, 0.0), (0.03, 0.05), (0.038, 0.11), (0.024, 0.18), (0.0, 0.23)], glow("9af0ff"), seg=6, y0=cy - 0.11, x=top[0] * 0.3, z=top[2] * 0.3))
    return p


@weapon
def bishop_crozier():
    p = haft(-0.86, 0.82, 0.018, WOOD, bands=(-0.4, 0.3), band_mat=BRASS, grip_at=(-0.12, 0.14))
    p.append(ball("knop", (0, 0.86, 0), 0.036, BRASS, sy=0.75, seg=16, rings_=10))
    p += ferrule(0.8, 0.04, 0.022, BRASS)
    pts, radii = [(0.0, 0.88, 0.0), (0.0, 0.96, 0.0)], [0.014, 0.014]
    c = (0.068, 1.03)
    N = 30
    for i in range(N + 1):   # the volute: up over the top, round and inwards
        t = i / N; a = math.pi - t * 2 * math.pi * 1.1
        R = 0.068 - 0.04 * t
        pts.append((c[0] + R * math.cos(a), c[1] + R * math.sin(a), 0.0)); radii.append(0.014 - 0.007 * t)
    p.append(tube("crook", pts, 0.012, BRASS, sides=8, radii=radii))
    for i in range(2, N - 6, 4):   # crockets: little leaf spurs on the outside of the curl
        t = i / N; a = math.pi - t * 2 * math.pi * 1.1; R = 0.068 - 0.04 * t
        b = (c[0] + R * math.cos(a), c[1] + R * math.sin(a), 0.0)
        e = (c[0] + (R + 0.03) * math.cos(a + 0.25), c[1] + (R + 0.03) * math.sin(a + 0.25), 0.0)
        p.append(spike("crocket", b, e, 0.006, BRASS, sides=4))
    p.append(ball("gem", (c[0] + 0.004, c[1] - 0.002, 0), 0.017, glow("ffd25a"), seg=10, rings_=6))
    return p


@weapon
def skull_staff():
    p = haft(-0.86, 0.86, 0.019, WOOD, bands=(-0.4, 0.3), grip_at=(-0.12, 0.14))
    p += ferrule(0.84, 0.05, 0.024, IRON)
    cy = 0.97   # the skull faces +Z
    p.append(lathe("neck", [(0.022, 0.0), (0.026, 0.03), (0.018, 0.07), (0.024, 0.09), (0.0, 0.1)], IRON, seg=12, y0=0.84))
    p.append(ball("cranium", (0, cy + 0.012, -0.004), 0.054, PARCH, sx=0.9, sy=1.0, sz=1.08, seg=18, rings_=12))
    p.append(ball("jaw", (0, cy - 0.04, 0.018), 0.036, PARCH, sx=0.8, sy=0.55, sz=0.95, seg=14, rings_=8))
    for sx in (-1, 1):
        p.append(ball("socket", (sx * 0.02, cy, 0.043), 0.014, CLOTH_B, sz=0.55, seg=10, rings_=6))
        p.append(ball("eye", (sx * 0.02, cy, 0.047), 0.0065, glow("7dff9a"), seg=8, rings_=5))
    p.append(spike("nose", (0, cy - 0.022, 0.05), (0, cy - 0.008, 0.052), 0.007, CLOTH_B, sides=3))
    for k in range(6):   # teeth
        x = -0.0125 + 0.005 * k
        p.append(ball("tooth", (x, cy - 0.036, 0.046), 0.0032, PARCH, sy=1.5, seg=6, rings_=4))
    for k in range(5):   # an iron crown of thorns round the skull
        a = 2 * math.pi * k / 5 + 0.3
        b = (0.04 * math.cos(a), cy + 0.03, 0.044 * math.sin(a))
        p.append(spike("thorn", b, (0.07 * math.cos(a), cy + 0.1, 0.075 * math.sin(a)), 0.006, IRON, sides=4))
    p.append(ring("crown", (0, cy + 0.03, -0.002), 0.048, 0.004, IRON, axis="y", seg=20, sides=5))
    return p


# ------------------------------------------------------------------ pickaxes (build mode tools, data/blocks.json dig.pickaxes)
def _pick_head(mat, y=0.6, half=0.27, droop=0.11, r0=0.026, tip=0.004, sides=8, flat=None, name="pick"):
    """A pick head across the haft top along X: an arc drooping towards both ends, thick at the eye, tapering to
    a point on +X; flat = width of a chisel end on -X (None = a second point)."""
    parts = []
    for s in (1, -1):
        pts, radii = [], []
        N = 12
        for i in range(N + 1):
            t = i / N
            x = s * half * t
            pts.append((x, y - droop * t * t, 0.0))
            end = tip if (s > 0 or flat is None) else r0 * 0.55
            radii.append(r0 + (end - r0) * t ** 1.3)
        parts.append(tube(name + ("_point" if s > 0 else "_tail"), pts, r0, mat, sides=sides, radii=radii))
        if s < 0 and flat:
            ex, ey = -half, y - droop
            parts.append(slab(name + "_chisel", [(ex + 0.02, ey + 0.018), (ex - 0.03, ey + flat / 2 - 0.01), (ex - 0.035, ey - flat / 2),
                                                 (ex + 0.02, ey - 0.02)], mat, 0.004, 0.02, inset=0.008, bevel=0.001))
    return parts


@weapon
def pickaxe_wood():
    # a hardened-wood pick lashed on with rawhide: crude and dark
    p = haft(-0.16, 0.62, 0.017, WOOD, bands=(), grip_at=(-0.14, 0.06), butt=False)
    p += _pick_head(BARK, y=0.6, half=0.23, droop=0.08, r0=0.03, tip=0.008, sides=6)
    for k in range(3):   # lashing round the eye
        p.append(ring("lash", (0, 0.57 + k * 0.025, 0), 0.022, 0.004, LEATHER, axis="y", seg=12, sides=4))
    for sx in (-1, 1):   # the lashing crossing over the head
        p.append(tube("lash_x", [(sx * 0.035, 0.63, 0.012), (0, 0.6, 0.026), (-sx * 0.035, 0.56, 0.012)], 0.004, LEATHER, sides=4))
    return p


@weapon
def pickaxe_stone():
    # knapped stone head bound into a split haft
    p = haft(-0.16, 0.64, 0.018, WOOD, bands=(), grip_at=(-0.14, 0.07), butt=False)
    p += _pick_head(ROCK, y=0.61, half=0.25, droop=0.09, r0=0.034, tip=0.007, sides=5)
    p.append(ball("knap", (0, 0.61, 0), 0.042, ROCK, sx=1.2, sy=0.9, sz=0.8, seg=7, rings_=5))
    for k in range(4):
        p.append(ring("bind", (0, 0.53 + k * 0.022, 0), 0.021, 0.0045, LEATHER, axis="y", seg=12, sides=4))
    p.append(spike("haft_top", (0, 0.64, 0), (0, 0.67, 0), 0.016, WOOD, sides=6))
    return p


@weapon
def pickaxe_iron():
    # a forged miner's pick: socketed iron head, point and chisel, banded haft
    p = haft(-0.18, 0.64, 0.017, WOOD, bands=(0.5,), grip_at=(-0.16, 0.08))
    p += _pick_head(IRON, y=0.62, half=0.28, droop=0.1, r0=0.024, tip=0.003, sides=8, flat=0.05)
    p.append(lathe("socket", [(0.024, 0.0), (0.028, 0.012), (0.028, 0.07), (0.024, 0.082)], IRON, seg=8, y0=0.58, sz=1.25, bevel=0.002))
    for sx in (-1, 1):   # langets riveted down the haft
        p.append(slab("langet", [(-0.006, 0.47), (0.006, 0.47), (0.006, 0.59), (-0.006, 0.59)], IRON, 0.004, 0.004, pos=(0, 0, sx * 0.0175)))
    p += rivets([(0, 0.5, 0.021), (0, 0.555, 0.021), (0, 0.5, -0.021), (0, 0.555, -0.021)], r=0.005)
    return p


@weapon
def pickaxe_rune():
    # dark steel with a glowing rune channel along the point and a blue stone in the eye
    p = haft(-0.18, 0.66, 0.017, WOOD, bands=(0.1, 0.5), band_mat=BRASS, grip_at=(-0.16, 0.08))
    p += _pick_head(STEEL, y=0.64, half=0.3, droop=0.11, r0=0.025, tip=0.003, sides=8, flat=0.055)
    p.append(lathe("socket", [(0.025, 0.0), (0.03, 0.012), (0.03, 0.075), (0.025, 0.088)], BRASS, seg=8, y0=0.6, sz=1.25, bevel=0.002))
    for sz_ in (1, -1):   # rune inlay on both faces of the point
        pts = []
        for i in range(9):
            t = 0.12 + 0.7 * i / 8; x = 0.3 * t
            pts.append((x, 0.64 - 0.11 * t * t, sz_ * (0.025 + (0.003 - 0.025) * t ** 1.3 + 0.0015)))
        p.append(tube("rune", pts, 0.0022, glow("6f9bff"), sides=4))
    p.append(ball("gem", (0, 0.645, 0.03), 0.012, glow("5b86ff"), sz=0.5, seg=10, rings_=6))
    p.append(ball("gem_b", (0, 0.645, -0.03), 0.012, glow("5b86ff"), sz=0.5, seg=10, rings_=6))
    p.append(spike("top", (0, 0.69, 0), (0, 0.75, 0), 0.014, STEEL, sides=4))
    return p
