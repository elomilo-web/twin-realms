"""Twin Realms A-35: grand weapons (colossal swords, axes and hammers, a curved greatsword, a sacred seal, a siege ladder).
Original models made for this game (CC0), same conventions as weapons.py: game coordinates, grip at the origin, blade
or head up +Y, the striking edge / head along X. Built with tools/blender_gothic/build.py like the other kit weapons:
  blender -b -P tools/blender_gothic/build.py -- colossal_sword_moon colossal_slab_ember ...
Lengths (before the game's grand scale x1.35, data/weapons.json size.grandScale):
  colossal_sword_moon 2.30 m, colossal_slab_ember 2.12 m, dune_reaver_sun 1.98 m, giant_axe_storm 2.06 m,
  starfall_maul_void 1.86 m, seal_hallowed 0.42 m, siege_ladder 2.55 m."""
import math
from gothic import *   # noqa: F401,F403
from weapons import blade, grip, ferrule, pommel, crossguard, haft, rivets, axe_bit, ricasso

WEAPONS = {}


def weapon(fn):
    WEAPONS[fn.__name__] = fn
    return fn


@weapon
def colossal_sword_moon():
    """Grave Warden Greatsword: a slab of a blade as wide as a man's chest, a pale rune down its fuller."""
    p = blade(0.12, 1.62, 0.27, 0.23, 0.032, tip=0.34, fuller=0.07, fuller_end=0.7, mat=IRON, glow_fuller="b8d8ff", n=40)
    p += ricasso(0.03, 0.13, 0.25, 0.036, mat=IRON, hooks=0.045)
    p += crossguard(0.03, 0.36, h=0.05, t=0.06, droop=0.06, flare=0.6, ends="spike", mat=IRON)
    p += grip(-0.5, -0.01, 0.022, LEATHER, sz=0.85)
    p += ferrule(-0.03, 0.03, 0.026, IRON, sz=0.85)
    p += ferrule(-0.27, 0.02, 0.025, IRON, sz=0.85, name="grip_band")
    p += pommel(-0.5, "scent", r=0.05, mat=IRON, gem="b8d8ff")
    # skull-and-wing boss on the guard
    p.append(ball("boss", (0, 0.0, 0.035), 0.045, IRON, sz=0.5))
    p.append(ball("boss_gem", (0, 0.0, 0.055), 0.02, glow("b8d8ff"), sz=0.5))
    p.append(ball("boss_b", (0, 0.0, -0.035), 0.045, IRON, sz=0.5))
    return p


@weapon
def colossal_slab_ember():
    """Ruinous Slab: a single-edged ingot of a sword, cracked so the forge-fire still shows through."""
    out = [(-0.15, 0.06), (0.15, 0.06), (0.16, 0.4), (0.15, 0.43), (0.165, 0.46), (0.17, 1.2), (0.16, 1.24), (0.17, 1.27),
           (0.17, 1.5), (0.06, 1.72), (-0.12, 1.66), (-0.16, 1.55), (-0.16, 0.95), (-0.15, 0.92), (-0.16, 0.89)]
    p = [slab("slab", out, RUST, 0.01, 0.05, inset=0.05, bevel=0.003, edge_mat=STEEL)]
    p.append(slab("block", [(-0.19, -0.05), (0.19, -0.05), (0.19, 0.07), (-0.19, 0.07)], IRON, 0.08, 0.08, bevel=0.006))
    p += rivets([(sx * 0.12, 0.2 + k * 0.12, sz * 0.027) for sx in (-1, 1) for k in range(3) for sz in (-1, 1)], r=0.014, mat=IRON)
    for sz in (-1, 1):   # glowing cracks on both faces
        for k, (x0, y0, x1, y1) in enumerate([(-0.08, 0.5, 0.05, 0.78), (0.05, 0.78, -0.02, 1.05), (-0.02, 1.05, 0.08, 1.38)]):
            pts = [(x0 + (x1 - x0) * t + 0.012 * math.sin(t * 9 + k), y0 + (y1 - y0) * t, sz * 0.027) for t in [i / 8 for i in range(9)]]
            p.append(tube("crack", pts, 0.005, glow("ff7a2a"), sides=5))
    p += grip(-0.46, -0.05, 0.024, LEATHER, sz=0.85)
    p += ferrule(-0.48, 0.03, 0.028, IRON, sz=0.85)
    p += pommel(-0.48, "ball", r=0.045, mat=RUST)
    return p


@weapon
def dune_reaver_sun():
    """Dune Reaver: a curved greatsword of the sand kingdoms, single-edged, a brass sun on the guard."""
    p = blade(0.06, 1.48, 0.13, 0.17, 0.016, tip=0.34, single=True, curve=0.24, mat=STEEL, n=48)
    p += crossguard(0.06, 0.2, h=0.03, t=0.04, droop=-0.04, flare=0.5, ends="curl", mat=BRASS)
    o = lathe("sun", [(0, -0.006), (0.04, -0.006), (0.046, 0), (0.04, 0.006), (0, 0.006)], BRASS, seg=12)
    p.append(place(o, rot=(90, 0, 0), loc=(0, 0.04, 0.026)))
    p.append(ball("sun_gem", (0, 0.04, 0.034), 0.016, glow("ffd25a"), sz=0.5))
    p += grip(-0.42, -0.005, 0.02, CLOTH, sz=0.8)
    p += ferrule(-0.02, 0.022, 0.023, BRASS, sz=0.8)
    p += pommel(-0.42, "pear", r=0.034, mat=BRASS)
    return p


@weapon
def giant_axe_storm():
    """Giant-Hewn Axe: two crescent bits the size of shields on a haft cut from a young oak."""
    p = haft(-0.72, 1.24, 0.032, WOOD, bands=(-0.35, 0.7, 1.24), grip_at=(-0.68, 0.2))
    for s in (1, -1):
        p.append(axe_bit(s, y_shift=0.0, sx=2.1, sy=2.3, base=0.98, reach=1.05))
    p.append(lathe("socket", [(0.042, 0.0), (0.05, 0.04), (0.05, 0.5), (0.042, 0.54)], IRON, seg=8, y0=0.8, sz=1.4))
    p.append(spike("top", (0, 1.22, 0), (0, 1.34, 0), 0.03, IRON, sides=6))
    for s in (1, -1):   # pale storm runes along both bits
        pts = [(s * (0.12 + 0.32 * t), 1.02 + 0.06 * math.sin(t * 3.5), 0.03) for t in [k / 10 for k in range(11)]]
        p.append(tube("rune", pts, 0.004, glow("c8d4ff"), sides=5))
        p.append(tube("rune_b", [(x, y, -z) for x, y, z in pts], 0.004, glow("c8d4ff"), sides=5))
    return p


@weapon
def starfall_maul_void():
    """Starfall Maul: a fallen star's stone bound in iron on a great haft; violet light leaks from the seams."""
    p = haft(-0.66, 0.92, 0.032, WOOD, bands=(-0.3, 0.55), grip_at=(-0.62, 0.18))
    rock = ball("star_stone", (0, 1.0, 0), 0.24, ROCK, sx=1.25, sy=0.9, sz=0.95, seg=12, rings_=8)
    p.append(rock)
    for yy in (0.9, 1.1):   # iron straps round the stone
        p.append(ring("strap", (0, yy, 0), 0.25 if yy < 1 else 0.23, 0.02, IRON, axis="y", seg=16, sides=6))
    p.append(ring("strap_v", (0, 1.0, 0), 0.235, 0.018, IRON, axis="z", seg=16, sides=6))
    for k in range(6):   # violet seams
        a = k * math.pi / 3
        pts = [(0.27 * math.cos(a + t * 0.6), 1.0 + 0.16 * (t - 0.5), 0.25 * math.sin(a + t * 0.6)) for t in [i / 6 for i in range(7)]]
        p.append(tube("seam", pts, 0.008, glow("b884ff"), sides=5))
    p.append(lathe("collar", [(0.04, 0.0), (0.06, 0.03), (0.06, 0.1), (0.04, 0.12)], IRON, seg=10, y0=0.74))
    return p


@weapon
def seal_hallowed():
    """Hallowed Seal: a sacred seal for prayers, a gilded ring round a glowing tree-and-sun emblem on a short haft."""
    p = [lathe("rod", [(0.012, 0.0), (0.014, 0.2), (0.012, 0.3)], WOOD, seg=10, y0=-0.12)]
    p += grip(-0.1, 0.1, 0.015, LEATHER, sz=1.0)
    p += pommel(-0.12, "pear", r=0.02, mat=BRASS)
    p += ferrule(0.16, 0.03, 0.018, BRASS)
    p.append(ring("seal_ring", (0, 0.3, 0), 0.085, 0.012, BRASS, axis="z", seg=32, sides=8))
    p.append(ring("seal_ring_in", (0, 0.3, 0), 0.06, 0.005, BRASS, axis="z", seg=28, sides=6))
    # the emblem: a trunk, three branches and a sun disc, glowing gold
    p.append(tube("trunk", [(0, 0.23, 0), (0, 0.32, 0)], 0.007, glow("ffe08a"), sides=6))
    for s in (-1, 0, 1):
        p.append(tube("branch", [(0, 0.3, 0), (s * 0.04, 0.35 - abs(s) * 0.01, 0)], 0.005, glow("ffe08a"), sides=5))
    p.append(ring("sun", (0, 0.33, 0), 0.05, 0.004, glow("ffe08a"), axis="z", seg=24, sides=5))
    for k in range(8):   # rays out to the ring
        a = k * math.pi / 4
        p.append(spike("ray", (0.085 * math.cos(a), 0.3 + 0.085 * math.sin(a), 0), (0.115 * math.cos(a), 0.3 + 0.115 * math.sin(a), 0), 0.008, BRASS, sides=4))
    return p


@weapon
def siege_ladder():
    """Siege Ladder: two rails and nine rungs, iron shod at the top for hooking over walls (and over heads)."""
    # held by its near rail (x = 0); the far rail runs at x = W, so the ladder swings like a very wide blade
    W = 0.42
    p = []
    for x in (0.0, W):
        p.append(lathe("rail", [(0.03, 0.0), (0.032, 0.02), (0.03, 2.5), (0.028, 2.55)], WOOD, seg=6, sx=1.0, sz=0.8, x=x, y0=-0.55))
        p.append(lathe("shoe", [(0.034, 0.0), (0.036, 0.04), (0.036, 0.16), (0.03, 0.2)], IRON, seg=6, x=x, y0=1.8))
        p.append(spike("hook", (x, 1.96, 0), (x, 2.1, 0.1), 0.024, IRON, sides=4))
    for k in range(9):
        y = -0.4 + k * 0.27
        p.append(place(lathe("rung", [(0.017, 0.0), (0.019, W / 2), (0.017, W)], WOOD, seg=6), rot=(0, 0, -90), loc=(0, y, 0)))
        p += rivets([(x, y, 0.026) for x in (0.0, W)], r=0.007)
    p += grip(-0.5, 0.1, 0.034, LEATHER, sz=0.85, name="wrap")   # a rag wound round the near rail to hold it by
    return p
