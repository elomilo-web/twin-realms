"""Twin Realms gothic armor pieces: assets/models/kit/armor/<name>.glb (rigid pieces put on a bone, data/avatar.json).
Game coordinates, front = +Z (like the old kit). Head pieces: origin = centre of the head.
Pauldrons: origin = top of the shoulder. Capes hang down from the origin."""
import math
from gothic import *   # noqa: F401,F403

ARMOR = {}


def armor(fn):
    ARMOR[fn.__name__] = fn
    return fn


def ellipse(cx, y, rx, rz, n=40):
    return [(cx + rx * math.cos(2 * math.pi * k / n), y, rz * math.sin(2 * math.pi * k / n)) for k in range(n + 1)]


# ------------------------------------------------------------------ head
@armor
def helm_knight():
    prof = [(0.128, -0.11), (0.14, -0.06), (0.148, 0.0), (0.146, 0.06), (0.135, 0.12), (0.11, 0.17), (0.07, 0.2), (0.0, 0.215)]
    shell = lathe("shell", prof, IRON, seg=40, sx=0.93, sz=1.06, caps=False)
    thicken(shell, 0.007)
    shell.modifiers.new("Bevel", "BEVEL").width = 0.0015
    cut(shell, cutter_box((-0.115, 0.03, 0.0), (0.115, 0.05, 0.3)))     # eye slit
    for i in range(5):                                                    # breaths on the right cheek
        x0 = 0.02 + i * 0.018
        cut(shell, cutter_box((x0, -0.07, 0.03), (x0 + 0.007, -0.015, 0.3)))
    p = [shell]
    # raised ridge over the crown, front to back
    pts = []
    for k in range(25):
        t = k / 24
        ang = math.pi * (0.08 + 0.84 * t)       # 0 = front, pi = back, measured in the YZ plane
        pts.append((0.0, 0.215 * math.sin(ang) * 0.98 + 0.004 - 0.02 * (1 - math.sin(ang)), 0.154 * math.cos(ang) + 0.0))
    p.append(tube("ridge", pts, 0.006, IRON, sides=6))
    p.append(tube("brow", ellipse(0, 0.06, 0.141, 0.16, 48), 0.005, BRASS, sides=6))
    p.append(tube("rim", ellipse(0, -0.105, 0.121, 0.138, 48), 0.006, BRASS, sides=6))
    p += [ball("rivet", (0.137 * math.cos(a) * 0.93, -0.07, 0.15 * math.sin(a)), 0.006, IRON, sz=0.6, seg=8, rings_=5)
          for a in [math.radians(d) for d in range(0, 360, 30)]]
    # a short crimson crest of horsehair, sweeping back
    for k in range(7):
        off = (k - 3) * 0.006
        pts = [(off, 0.2 + 0.07 * math.sin(math.pi * t * 0.8), 0.02 - 0.24 * t) for t in [i / 10 for i in range(11)]]
        p.append(tube("crest", pts, 0.009, CLOTH, sides=5, radii=[0.011 - 0.007 * i / 10 for i in range(11)]))
    return p


@armor
def circlet_winged():
    band = lathe("band", [(0.1, 0.0), (0.108, 0.004), (0.108, 0.026), (0.1, 0.03), (0.1, 0.0)], STEEL, seg=40, sz=1.15, caps=False)
    p = [band]
    p.append(tube("trim", ellipse(0, 0.015, 0.109, 0.125, 40), 0.003, BRASS, sides=5))
    for s in (1, -1):   # swept iron feathers
        for k in range(5):
            L = 0.19 - k * 0.025
            f = [(0.0, -0.008), (L * 0.3, -0.014), (L, 0.0), (L * 0.3, 0.014), (0.0, 0.008)]
            if s < 0:   # mirrored for the left wing
                f = [(-x, y) for x, y in reversed(f)]
            o = slab("feather", f, STEEL, 0.003, 0.005, inset=0.004)
            place(o, rot=(0, s * 30, s * (18 + k * 13)), loc=(s * 0.1, 0.02, -0.02 - k * 0.01))
            p.append(o)
    p.append(ball("gem", (0, 0.015, 0.127), 0.011, glow("6fe8ff"), sz=0.5, seg=8, rings_=6))
    p.append(ring("setting", (0, 0.015, 0.125), 0.013, 0.003, BRASS, axis="z", seg=16, sides=4))
    return p


@armor
def crown():
    p = [lathe("band", [(0.112, 0.0), (0.12, 0.005), (0.12, 0.05), (0.112, 0.055), (0.112, 0.0)], BRASS, seg=48, sz=1.12, caps=False)]
    for k in range(8):
        a = 360 * k / 8
        h = 0.1 if k % 2 == 0 else 0.07
        o = slab("tine", [(-0.02, 0.0), (0.02, 0.0), (0.008, h * 0.6), (0.0, h), (-0.008, h * 0.6)], BRASS, 0.004, 0.007, inset=0.004)
        rad = math.radians(a)
        place(o, rot=(-8, a, 0), loc=(0.118 * math.sin(rad), 0.05, 0.132 * math.cos(rad)))
        p.append(o)
        gem = "ff5a6e" if k % 2 == 0 else "6fe8ff"
        p.append(ball("gem", (0.122 * math.sin(rad), 0.027, 0.137 * math.cos(rad)), 0.009, glow(gem), sz=0.5, seg=8, rings_=6))
    return p


def _hat(mat):
    brim = lathe("brim", [(0.13, 0.04), (0.22, 0.035), (0.29, 0.04), (0.305, 0.052), (0.3, 0.056), (0.22, 0.047), (0.13, 0.052), (0.13, 0.04)],
                 mat, seg=48, caps=False)
    p = [brim]
    rings = []
    for i in range(14):
        t = i / 13
        r = 0.13 * (1 - t) ** 0.85 + 0.006
        cz = -0.12 * t ** 2.6; cy = 0.05 + 0.4 * t - 0.05 * t ** 3
        rings.append([(r * math.cos(2 * math.pi * k / 20), cy, cz + r * math.sin(2 * math.pi * k / 20)) for k in range(20)])
    p.append(loft("cone", rings, mat, cap0=False, tip=(0, 0.44, -0.16)))
    p.append(lathe("band", [(0.128, 0.05), (0.132, 0.055), (0.126, 0.09), (0.122, 0.092)], LEATHER, seg=32, caps=False))
    p.append(slab("buckle", [(-0.02, 0.05), (0.02, 0.05), (0.02, 0.09), (-0.02, 0.09)], BRASS, 0.006, 0.006, bevel=0.001, pos=(0, 0, 0.13)))
    p.append(ball("gem", (0, 0.07, 0.136), 0.007, glow("ffd25a"), sz=0.5, seg=8, rings_=6))
    return p


@armor
def hat_mage():
    return _hat(CLOTH_B)


@armor
def hat_mage_red():
    return _hat(CLOTH)


@armor
def hood_ranger():
    prof = [(0.16, -0.14), (0.172, -0.05), (0.166, 0.05), (0.14, 0.13), (0.09, 0.18), (0.0, 0.195)]
    shell = lathe("hood", prof, CLOTH_B, seg=36, sz=1.12, caps=False)
    thicken(shell, 0.008)
    cut(shell, cutter_box((-0.095, -0.16, 0.02), (0.095, 0.11, 0.3)))
    p = [shell]
    p.append(spike("cowl", (0.0, 0.08, -0.13), (0.0, -0.06, -0.28), 0.07, CLOTH_B, sides=10))
    p.append(tube("hem", ellipse(0, -0.138, 0.162, 0.18, 40), 0.006, LEATHER, sides=5))
    return p


# ------------------------------------------------------------------ shoulders
def _pauldron(spikes=False):
    dome = lathe("dome", [(0.0, 0.07), (0.06, 0.066), (0.1, 0.046), (0.124, 0.012), (0.13, -0.02)], IRON, seg=36, sz=0.95, caps=False)
    thicken(dome, 0.006)
    p = [dome]
    for k, (r0, y0) in enumerate(((0.128, -0.012), (0.132, -0.042))):
        lame = lathe("lame", [(r0, y0), (r0 + 0.012, y0 - 0.038)], IRON, seg=36, sz=0.95, caps=False)
        thicken(lame, 0.005); p.append(lame)
        p.append(tube("lame_trim", ellipse(0, y0 - 0.038, r0 + 0.012, (r0 + 0.012) * 0.95, 36), 0.003, BRASS, sides=4))
    p.append(tube("trim", ellipse(0, -0.02, 0.131, 0.124, 36), 0.004, BRASS, sides=5))
    p += [ball("rivet", (0.11 * math.cos(a), 0.035, 0.104 * math.sin(a)), 0.006, IRON, sz=0.6, seg=8, rings_=5)
          for a in [math.radians(d) for d in range(0, 360, 45)]]
    if spikes:
        for k, (x, z) in enumerate(((0.0, 0.0), (0.05, 0.04), (-0.05, 0.04))):
            p.append(spike("spike", (x, 0.06, z), (x * 1.6, 0.16 - 0.03 * (k > 0), z * 1.6 - 0.01), 0.018, IRON, sides=6))
    return p


@armor
def pauldron_plate():
    return _pauldron(False)


@armor
def pauldron_spiked():
    return _pauldron(True)


# ------------------------------------------------------------------ wings
def feather_texture(size=256):
    """White feather atlas (tinted in game): one feather, shaft up the middle, barbs, soft splits. assets/textures/gothic/feather.png"""
    import numpy as np, os
    W, H = size, size * 4
    rnd = np.random.default_rng(5)
    u = (np.arange(W) / (W - 1))[None, :] * 2 - 1          # -1..1 across
    v = (np.arange(H) / (H - 1))[:, None]                    # 0 at the quill, 1 at the tip
    half = np.clip(np.sin(np.pi * np.clip(v * 1.05, 0, 1)) ** 0.55, 0, 1) * (0.92 - 0.25 * v)
    inside = np.abs(u) < half
    barb = 0.5 + 0.5 * np.sin((v * 140 + np.abs(u) * 30) + rnd.normal(0, 0.4, (H, W)))
    split = np.ones((H, W), np.float32)
    for _ in range(14):   # gaps where the barbs part
        y0 = rnd.uniform(0.15, 0.95); side = rnd.choice([-1, 1])
        gap = np.exp(-((v - y0 - np.abs(u) * 0.25) / 0.006) ** 2) * (u * side > 0.15)
        split -= gap * 0.9
    alpha = (inside * np.clip((half - np.abs(u)) / 0.06, 0, 1) * np.clip(split, 0, 1) * (v > 0.04)).astype(np.float32)
    shaft = np.exp(-(u / 0.025) ** 2)
    col = 0.82 + 0.12 * barb - 0.25 * np.abs(u) ** 2 - 0.12 * shaft + 0.08 * v
    alpha = np.maximum(alpha, (shaft > 0.35) * (v < 0.98) * 1.0)
    col = np.clip(col, 0, 1)
    rgba = np.stack([col, col * 0.985, col * 0.96, alpha], axis=2).astype(np.float32)
    im = bpy.data.images.get("feather") or bpy.data.images.new("feather", W, H, alpha=True)
    if tuple(im.size) != (W, H):
        im.scale(W, H)
    im.pixels.foreach_set(rgba[::-1].reshape(-1))
    im.filepath_raw = os.path.join(TEX, "feather.png"); im.file_format = "PNG"; im.save()
    return im


def _feather(P, base, along, out, L, W, bend=0.08):
    """A feather card: 4 segments, curling toward `out` (the back), texture quill at the base."""
    a = along.normalized(); o = out.normalized(); s = a.cross(o).normalized()
    prev = None
    for k in range(5):
        t = k / 4
        c = base + a * (L * t) + o * (bend * L * t * t)
        w = W * (0.6 + 0.4 * math.sin(math.pi * min(1.0, t * 1.1 + 0.05)))
        l = P.v(tuple(c - s * w / 2)); r = P.v(tuple(c + s * w / 2))
        if prev:
            P.face((prev[0], prev[1], r, l), "TR_Feather", uvs=[(0, 1 - (k - 1) / 4), (1, 1 - (k - 1) / 4), (1, 1 - t), (0, 1 - t)])   # quill at the image top
        prev = (l, r)


@armor
def wing_angel():
    """One feathered wing for the +X side (Avatar mirrors it for the other side): origin = the shoulder joint on the back,
    the arm rises outward, feathers hang and fan down from it, their faces toward the back (-Z)."""
    if not bpy.data.images.get("feather"):
        feather_texture()
    rnd = random.Random(9)
    rise = math.radians(32)
    arm_dir = Vector((math.cos(rise), math.sin(rise), -0.15)).normalized()
    ARM = 0.66
    joints = [Vector((0, 0, 0)), arm_dir * ARM * 0.38, arm_dir * ARM * 0.75 + Vector((0, -0.02, 0)), arm_dir * ARM]
    p = [tube("wing_arm", [tuple(j) for j in joints], 0.03, PARCH, sides=8,
              radii=[0.04, 0.032, 0.024, 0.012])]
    P = Part("feathers")
    back = Vector((0, 0, -1))
    # primaries off the hand, secondaries off the forearm, coverts and scapulars over them
    rows = [("primary", 10, 0.62, 1.0, 0.36, 0.95, (-95, -20)), ("secondary", 12, 0.18, 0.62, 0.30, 0.42, (-108, -88)),
            ("covert", 14, 0.08, 0.98, 0.16, 0.2, (-100, -50)), ("covert2", 12, 0.05, 0.9, 0.09, 0.11, (-95, -55)),
            ("scapular", 6, 0.0, 0.18, 0.22, 0.28, (-120, -100))]
    for r, (name, n, t0, t1, l0, l1, (a0, a1)) in enumerate(rows):
        for i in range(n):
            f = i / max(1, n - 1)
            t = t0 + (t1 - t0) * f
            seg = min(2, int(t * 3)); local = t * 3 - seg
            root = joints[seg].lerp(joints[seg + 1], local) + back * (0.012 * r) + Vector((0, -0.01 * r, 0))
            ang = math.radians(a0 + (a1 - a0) * f + rnd.uniform(-3, 3))
            d = Vector((math.cos(ang) * 0.55 + 0.25 * t, math.sin(ang), -0.05)).normalized()
            L = (l0 + (l1 - l0) * (f ** 1.3 if name == "primary" else f)) * rnd.uniform(0.93, 1.05)
            if name == "secondary":
                L = l0 + (l1 - l0) * (1 - abs(f - 0.6))
            _feather(P, root, d, back, L, 0.06 + 0.09 * L, bend=0.06 + 0.03 * r)
    p.append(P.obj())
    return p


# ------------------------------------------------------------------ capes
def _cape(mat, seed=3):
    import random
    rnd = random.Random(seed)
    nx, ny = 16, 26
    P = Part("cape")
    grid = []
    rag = [rnd.uniform(-0.06, 0.0) for _ in range(nx + 1)]
    for j in range(ny + 1):
        t = j / ny
        row = []
        for i in range(nx + 1):
            s = -1 + 2 * i / nx
            half = 0.24 + 0.13 * t
            x = s * half
            y = 0.03 - 1.0 * t + (rag[i] * t ** 6 if j == ny else 0)
            z = 0.02 + 0.11 * t + 0.03 * (1 - s * s) + 0.022 * math.sin(s * 9.5 + t * 1.5) * t ** 0.7
            row.append(P.v((x, y, z)))
        grid.append(row)
    for j in range(ny):
        for i in range(nx):
            P.face((grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i]), mat)
    o = P.obj(); thicken(o, 0.006, 0.0)
    p = [o]
    for s in (1, -1):
        clasp = lathe("clasp", [(0.0, -0.006), (0.024, -0.006), (0.028, 0.0), (0.024, 0.006), (0.0, 0.006)], BRASS, seg=20)
        p.append(place(clasp, rot=(90, 0, 0), loc=(s * 0.21, 0.0, 0.03)))
    p.append(tube("chain", [(-0.21, 0.0, 0.04), (0.0, -0.035, 0.05), (0.21, 0.0, 0.04)], 0.004, IRON, sides=4))
    return p


@armor
def cape_blue():
    return _cape(CLOTH_BLUE, seed=3)


@armor
def cape_red():
    return _cape(CLOTH, seed=7)
