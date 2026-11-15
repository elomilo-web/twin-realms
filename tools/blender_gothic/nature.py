"""Twin Realms gothic vegetation: assets/models/kit/nature/<name>.glb.
Scenery convention: sits on y = 0, centred on x = z = 0. Bark uses the shared G_Bark (world triplanar in the game);
foliage is alpha-cut cards with G_Leaves (beech leaf clusters) or G_Needles (fir sprays), both two-sided in Godot.
Placed by World.KitVegetation through data/env.json (trees.oak / trees.pine / bushes / flowers)."""
import math, random
from gothic import *   # noqa: F401,F403

NATURE = {}


def plant(fn):
    NATURE[fn.__name__] = fn
    return fn


# fir spray bands in needles.png (v from the top), from foliage_tex.ps1
SPRAYS = [(0.078, 0.258), (0.336, 0.602), (0.664, 0.977)]


def card(P, base, along, side, length, width, mat, uv=(0.0, 0.0, 1.0, 1.0)):
    """One quad: grows from base along `along` for `length`, `width` across `side`. Texture bottom (v=1) at the base."""
    a, s = Vector(along).normalized(), Vector(side).normalized()
    b = Vector(base)
    p0 = b - s * width / 2; p1 = b + s * width / 2
    p2 = p1 + a * length; p3 = p0 + a * length
    u0, v0, u1, v1 = uv
    vs = [P.v(tuple(p)) for p in (p0, p1, p2, p3)]
    P.face(vs, mat, uvs=[(u0, 1 - v1), (u1, 1 - v1), (u1, 1 - v0), (u0, 1 - v0)])


def cross_cards(P, base, along, length, width, mat, rnd, uv=(0.0, 0.0, 1.0, 1.0)):
    a = Vector(along).normalized()
    ref = Vector((0, 1, 0)) if abs(a.y) < 0.9 else Vector((1, 0, 0))
    s1 = a.cross(ref).normalized()
    s1 = Matrix.Rotation(rnd.uniform(-0.6, 0.6), 3, a) @ s1
    s2 = a.cross(s1).normalized()
    card(P, base, along, s1, length, width, mat, uv)
    card(P, base, along, s2, length, width * 0.85, mat, uv)


def limb(pts, r0, r1, name="limb"):
    radii = [r0 + (r1 - r0) * i / (len(pts) - 1) for i in range(len(pts))]
    return tube(name, pts, r0, BARK, sides=8 if r0 > 0.05 else 5, radii=radii)


def grow(start, direction, length, steps, rnd, wander=0.25, up=0.15):
    pts = [Vector(start)]
    d = Vector(direction).normalized()
    for i in range(steps):
        d = (d + Vector((rnd.uniform(-wander, wander), up + rnd.uniform(-wander, wander) * 0.5, rnd.uniform(-wander, wander)))).normalized()
        pts.append(pts[-1] + d * (length / steps))
    return [tuple(p) for p in pts]


# ------------------------------------------------------------------ broadleaf
def _broadleaf(seed, H=8.0, crown=3.4, leaves=True, gnarl=0.25):
    rnd = random.Random(seed)
    parts = []
    trunk_top = H * 0.55
    trunk = grow((0, -0.3, 0), (0, 1, 0), trunk_top + 0.3, 8, rnd, wander=0.06, up=0.4)
    rr = [0.34 if i == 0 else 0.26 - 0.12 * i / 8 for i in range(9)]
    parts.append(tube("trunk", trunk, 0.26, BARK, sides=10, radii=rr))
    for k in range(4):   # root flare
        a = 2 * math.pi * k / 4 + rnd.uniform(-0.3, 0.3)
        parts.append(limb([(0.15 * math.cos(a), 0.5, 0.15 * math.sin(a)), (0.45 * math.cos(a), 0.05, 0.45 * math.sin(a)), (0.75 * math.cos(a), -0.15, 0.75 * math.sin(a))], 0.12, 0.03, "root"))
    P = Part("leaves")
    nb = rnd.randint(5, 7)
    for k in range(nb):
        a = 2 * math.pi * k / nb + rnd.uniform(-0.3, 0.3)
        y0 = H * rnd.uniform(0.38, 0.55)
        i0 = min(8, int(y0 / (trunk_top + 0.3) * 8))
        base = trunk[i0]
        d = (math.cos(a), rnd.uniform(0.5, 0.9), math.sin(a))
        L = crown * rnd.uniform(0.85, 1.15)
        br = grow(base, d, L, 6, rnd, wander=gnarl, up=0.12)
        parts.append(limb(br, 0.11, 0.02, "branch"))
        for j in (3, 4, 5):   # sub-branches
            sd = Vector(br[j + 1]) - Vector(br[j]) + Vector((rnd.uniform(-0.8, 0.8), rnd.uniform(0.1, 0.6), rnd.uniform(-0.8, 0.8)))
            sb = grow(br[j], sd, L * 0.45, 3, rnd, wander=gnarl, up=0.2)
            parts.append(limb(sb, 0.04, 0.012, "twig"))
            if leaves:
                for q in (1, 2, 3, 3):
                    out = Vector(sb[q]) - Vector((0, H * 0.62, 0))
                    cross_cards(P, sb[q], out.normalized() + Vector((0, 0.6, 0)), rnd.uniform(1.3, 2.0), rnd.uniform(1.3, 2.0), LEAVES, rnd)
        if leaves:
            for q in (4, 5, 6):
                out = Vector(br[q]) - Vector((0, H * 0.62, 0))
                cross_cards(P, br[q], out.normalized() + Vector((0, 0.5, 0)), rnd.uniform(1.5, 2.3), rnd.uniform(1.5, 2.2), LEAVES, rnd)
    if leaves:   # fill the top of the crown
        for q in range(12):
            a = rnd.uniform(0, 2 * math.pi); r = rnd.uniform(0.3, crown * 0.6)
            b = (r * math.cos(a), H * rnd.uniform(0.72, 0.9), r * math.sin(a))
            cross_cards(P, b, (math.cos(a) * 0.4, 1.0, math.sin(a) * 0.4), rnd.uniform(1.6, 2.4), rnd.uniform(1.6, 2.4), LEAVES, rnd)
        parts.append(P.obj())
    return parts


@plant
def tree_oak_a():
    return _broadleaf(11, H=8.0, crown=3.4)


@plant
def tree_oak_b():
    return _broadleaf(23, H=9.5, crown=3.0)


@plant
def tree_oak_c():
    return _broadleaf(37, H=7.0, crown=3.8)


@plant
def tree_dead():
    """A gnarled leafless tree for the dark places."""
    return _broadleaf(51, H=7.5, crown=3.2, leaves=False, gnarl=0.45)


# ------------------------------------------------------------------ fir
def _fir(seed, H=11.0, R=2.6):
    rnd = random.Random(seed)
    parts = [lathe("trunk", [(0.3, -0.3), (0.27, 0.3), (0.2, H * 0.4), (0.1, H * 0.8), (0.02, H)], BARK, seg=10)]
    P = Part("needles")
    y = 1.6
    while y < H - 0.3:
        t = (y - 1.6) / (H - 1.9)
        L = R * (1 - t) ** 0.85 + 0.35
        n = max(4, int(7 - t * 3))
        off = rnd.uniform(0, 2 * math.pi)
        for k in range(n):
            a = off + 2 * math.pi * k / n + rnd.uniform(-0.2, 0.2)
            droop = math.radians(rnd.uniform(12, 28)) * (1 - t * 0.5)
            d = Vector((math.cos(a) * math.cos(droop), -math.sin(droop) + 0.05, math.sin(a) * math.cos(droop)))
            base = Vector((0.08 * math.cos(a), y, 0.08 * math.sin(a)))
            if L > 0.9:
                parts.append(limb([tuple(base), tuple(base + d * L * 0.85)], 0.035, 0.01, "bough"))
            v0, v1 = SPRAYS[rnd.randrange(3)]
            w = L * (v1 - v0) * 1.5
            for rot in (0.0, math.pi / 2):   # crossed: one flat, one upright
                ref = Vector((0, 1, 0))
                s = d.cross(ref).normalized()
                s = Matrix.Rotation(rot + rnd.uniform(-0.3, 0.3), 3, d) @ s
                # spray texture runs along u: map the card so its length follows u
                b = base
                p0 = b - s * w / 2; p1 = b + s * w / 2; p2 = p1 + d * L; p3 = p0 + d * L
                vs = [P.v(tuple(p)) for p in (p0, p1, p2, p3)]
                P.face(vs, NEEDLES, uvs=[(0.0, 1 - v1), (0.0, 1 - v0), (1.0, 1 - v0), (1.0, 1 - v1)])
        y += rnd.uniform(0.38, 0.55)
    # a tuft at the top
    for k in range(3):
        a = 2 * math.pi * k / 3
        s = Vector((math.cos(a), 0, math.sin(a)))
        v0, v1 = SPRAYS[k % 3]
        b = Vector((0, H - 0.6, 0))
        p0 = b - s * 0.25; p1 = b + s * 0.25; p2 = p1 + Vector((0, 1.0, 0)); p3 = p0 + Vector((0, 1.0, 0))
        vs = [P.v(tuple(p)) for p in (p0, p1, p2, p3)]
        P.face(vs, NEEDLES, uvs=[(0.0, 1 - v1), (0.0, 1 - v0), (1.0, 1 - v0), (1.0, 1 - v1)])
    parts.append(P.obj())
    return parts


@plant
def tree_fir_a():
    return _fir(5, H=11.0, R=2.6)


@plant
def tree_fir_b():
    return _fir(17, H=13.0, R=2.9)


@plant
def tree_fir_c():
    return _fir(29, H=9.0, R=2.3)


# ------------------------------------------------------------------ undergrowth
def _bush(seed, size=1.0, cards=12):
    rnd = random.Random(seed)
    parts = []
    P = Part("leaves")
    for k in range(5):
        a = 2 * math.pi * k / 5 + rnd.uniform(-0.4, 0.4)
        st = grow((0, -0.05, 0), (math.cos(a) * 0.6, 1.0, math.sin(a) * 0.6), 0.7 * size, 3, rnd, wander=0.3, up=0.1)
        parts.append(limb(st, 0.025 * size, 0.008, "stem"))
    for k in range(cards):
        a = rnd.uniform(0, 2 * math.pi); r = rnd.uniform(0.0, 0.35) * size
        b = (r * math.cos(a), rnd.uniform(0.0, 0.35) * size, r * math.sin(a))
        cross_cards(P, b, (math.cos(a) * 0.7, 1.0, math.sin(a) * 0.7), rnd.uniform(0.7, 1.1) * size, rnd.uniform(0.7, 1.1) * size, LEAVES, rnd)
    parts.append(P.obj())
    return parts


@plant
def bush_a():
    return _bush(3, 1.0, 12)


@plant
def bush_b():
    return _bush(9, 1.3, 16)


@plant
def shrub_small():
    return _bush(13, 0.45, 5)
