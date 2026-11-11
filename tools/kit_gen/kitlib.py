"""Tiny mesh library for the Twin Realms asset kit: build shapes in code, colour them per vertex, save as .glb.
Units are metres, Y is up, every model sits on y = 0 with its centre at x = z = 0. Colours are written as sRGB hex."""
import json, math, struct
import numpy as np

def hexc(h):
    h = h.lstrip('#'); return np.array([int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)])
def lin(c): c = np.asarray(c, float); return np.where(c <= .04045, c / 12.92, ((c + .055) / 1.055) ** 2.4)
def col(c): return hexc(c) if isinstance(c, str) else np.asarray(c, float)
def mixc(a, b, t): return col(a) * (1 - t) + col(b) * t

class Part:
    """Triangles with a colour per vertex. mat: matte | metal | glow | matte2s (two-sided)."""
    def __init__(s, v, f, c=None, mat='matte', smooth=False):
        s.v = np.asarray(v, float).reshape(-1, 3); s.f = np.asarray(f, int).reshape(-1, 3)
        s.c = np.ones((len(s.v), 3)) if c is None else np.asarray(c, float); s.mat = mat; s.smooth = smooth
    def copy(s): return Part(s.v.copy(), s.f.copy(), s.c.copy(), s.mat, s.smooth)
    def move(s, x=0, y=0, z=0): s.v = s.v + [x, y, z]; return s
    def scale(s, x=1, y=None, z=None):
        y = x if y is None else y; z = x if z is None else z; s.v = s.v * [x, y, z]
        if x * y * z < 0: s.f = s.f[:, ::-1]
        return s
    def rot(s, x=0, y=0, z=0):
        for ax, a in ((0, x), (1, y), (2, z)):
            if a:
                a = math.radians(a); c_, s_ = math.cos(a), math.sin(a); i, j = [(1, 2), (2, 0), (0, 1)][ax]
                vi, vj = s.v[:, i].copy(), s.v[:, j].copy(); s.v[:, i] = vi * c_ - vj * s_; s.v[:, j] = vi * s_ + vj * c_
        return s
    def paint(s, c): s.c = np.tile(col(c), (len(s.v), 1)); return s
    def grad(s, bottom, top, axis=1, power=1.0):
        a = s.v[:, axis]; t = (a - a.min()) / max(a.max() - a.min(), 1e-9); t = t[:, None] ** power
        s.c = col(bottom) * (1 - t) + col(top) * t; return s
    def noise(s, amt, seed=0, freq=1.0):
        """Push vertices in and out along their direction from the centre (lumpy rocks, leafy blobs)."""
        ctr = s.v.mean(0); d = s.v - ctr; n = d / np.maximum(np.linalg.norm(d, axis=1, keepdims=True), 1e-9)
        r = np.random.RandomState(seed); ph = r.rand(3, 3) * 6.28; k = r.rand(3, 3) * 2 + 1
        w = sum(np.sin(n @ (k[i] * freq * 2.2) + ph[i, 0]) * np.cos(n @ (k[(i + 1) % 3] * freq * 1.7) + ph[i, 1]) for i in range(3)) / 3
        s.v = s.v + n * (w[:, None] * amt); return s
    def bend(s, amt, axis=0):
        """Lean sideways more the higher it goes (curved trunks, blades)."""
        y = s.v[:, 1]; t = (y - y.min()) / max(y.max() - y.min(), 1e-9); s.v[:, axis] += amt * t * t; return s

def box(sx, sy, sz, c='ffffff', mat='matte'):
    x, y, z = sx / 2, sy, sz / 2
    v = [[-x, 0, -z], [x, 0, -z], [x, y, -z], [-x, y, -z], [-x, 0, z], [x, 0, z], [x, y, z], [-x, y, z]]
    f = [[0, 2, 1], [0, 3, 2], [4, 5, 6], [4, 6, 7], [0, 1, 5], [0, 5, 4], [3, 6, 2], [3, 7, 6], [0, 4, 7], [0, 7, 3], [1, 2, 6], [1, 6, 5]]
    return Part(v, f, mat=mat).paint(c)

def cyl(r0, r1, h, seg=10, c='ffffff', mat='matte', smooth=True, caps=True):
    """Tube along +Y from y=0 (radius r0) to y=h (radius r1). r1=0 gives a cone."""
    return lathe([(r0, 0), (r1, h)], seg, c, mat, smooth, caps)

def lathe(profile, seg=12, c='ffffff', mat='matte', smooth=True, caps=True):
    """Spin a list of (radius, height) points round the Y axis."""
    v = []; f = []; n = len(profile)
    for r, y in profile:
        for i in range(seg): a = 2 * math.pi * i / seg; v.append([r * math.cos(a), y, r * math.sin(a)])
    for j in range(n - 1):
        for i in range(seg):
            a, b = j * seg + i, j * seg + (i + 1) % seg; c2, d = a + seg, b + seg
            if profile[j][0] > 1e-9: f.append([a, c2, b])
            if profile[j + 1][0] > 1e-9: f.append([b, c2, d])
    if caps:
        for j, flip in ((0, False), (n - 1, True)):
            if profile[j][0] > 1e-9:
                k = len(v); v.append([0, profile[j][1], 0])
                for i in range(seg):
                    a, b = j * seg + i, j * seg + (i + 1) % seg; f.append([k, b, a] if flip else [k, a, b])
    p = Part(v, f, mat=mat, smooth=smooth).paint(c)
    return p

def ico(r=1.0, sub=2, c='ffffff', mat='matte', smooth=True):
    t = (1 + 5 ** .5) / 2
    v = [[-1, t, 0], [1, t, 0], [-1, -t, 0], [1, -t, 0], [0, -1, t], [0, 1, t], [0, -1, -t], [0, 1, -t], [t, 0, -1], [t, 0, 1], [-t, 0, -1], [-t, 0, 1]]
    f = [[0, 11, 5], [0, 5, 1], [0, 1, 7], [0, 7, 10], [0, 10, 11], [1, 5, 9], [5, 11, 4], [11, 10, 2], [10, 7, 6], [7, 1, 8], [3, 9, 4], [3, 4, 2], [3, 2, 6], [3, 6, 8], [3, 8, 9], [4, 9, 5], [2, 4, 11], [6, 2, 10], [8, 6, 7], [9, 8, 1]]
    v = [np.array(p, float) / np.linalg.norm(p) for p in v]
    for _ in range(sub):
        cache = {}; nf = []
        def mid(a, b):
            k = (min(a, b), max(a, b))
            if k not in cache: m = v[a] + v[b]; v.append(m / np.linalg.norm(m)); cache[k] = len(v) - 1
            return cache[k]
        for a, b, c2 in f:
            ab, bc, ca = mid(a, b), mid(b, c2), mid(c2, a); nf += [[a, ab, ca], [b, bc, ab], [c2, ca, bc], [ab, bc, ca]]
        f = nf
    return Part(np.array(v) * r, f, mat=mat, smooth=smooth).paint(c)

def extrude(poly, depth, c='ffffff', mat='matte'):
    """A flat outline (list of (x, y), anticlockwise, roughly convex) given thickness along Z, centred on z=0."""
    n = len(poly); z = depth / 2
    v = [[x, y, z] for x, y in poly] + [[x, y, -z] for x, y in poly]; f = []
    for i in range(1, n - 1): f.append([0, i, i + 1]); f.append([n, n + i + 1, n + i])
    for i in range(n): j = (i + 1) % n; f += [[i, n + i, j], [j, n + i, n + j]]
    return Part(v, f, mat=mat).paint(c)

def blade(pts, thick, c='ffffff', mat='metal'):
    """A sword blade: outline points (x, y) down the left edge mirrored to the right, with a raised centre ridge."""
    v = []; f = []; n = len(pts)
    for x, y in pts: v += [[-x, y, 0], [0, y, thick / 2], [x, y, 0], [0, y, -thick / 2]]
    for j in range(n - 1):
        for i in range(4):
            a, b = j * 4 + i, j * 4 + (i + 1) % 4; f += [[a, b, a + 4], [b, b + 4, a + 4]]
    f += [[0, 2, 1], [0, 3, 2]]; k = (n - 1) * 4; f += [[k, k + 1, k + 2], [k, k + 2, k + 3]]
    return Part(v, f, mat=mat).paint(c)

def quad(a, b, c2, d, c='ffffff', mat='matte2s'):
    return Part([a, b, c2, d], [[0, 1, 2], [0, 2, 3]], mat=mat).paint(c)

def torus(R, r, seg=16, tube=6, c='ffffff', mat='matte', arc=360):
    v = []; f = []; n = seg if arc == 360 else seg + 1
    for i in range(n):
        a = math.radians(arc) * i / seg
        for j in range(tube):
            b = 2 * math.pi * j / tube; rr = R + r * math.cos(b); v.append([rr * math.cos(a), r * math.sin(b), rr * math.sin(a)])
    for i in range(seg if arc != 360 else n):
        for j in range(tube):
            a = i * tube + j; b = i * tube + (j + 1) % tube; i2 = (i + 1) % n; c2 = i2 * tube + j; d = i2 * tube + (j + 1) % tube
            f += [[a, b, c2], [b, d, c2]]
    return Part(v, f, mat=mat, smooth=True).paint(c)

class Model:
    def __init__(s, name): s.name = name; s.parts = []
    def add(s, *ps):
        for p in ps:
            if isinstance(p, Model): s.parts += [q.copy() for q in p.parts]
            elif isinstance(p, (list, tuple)): s.add(*p)
            else: s.parts.append(p)
        return s
    def move(s, x=0, y=0, z=0): [p.move(x, y, z) for p in s.parts]; return s
    def rot(s, x=0, y=0, z=0): [p.rot(x, y, z) for p in s.parts]; return s
    def scale(s, x=1, y=None, z=None): [p.scale(x, y, z) for p in s.parts]; return s
    def tris(s): return sum(len(p.f) for p in s.parts)
    def bounds(s): a = np.vstack([p.v for p in s.parts]); return a.min(0), a.max(0)

    def baked(s):
        """Per material: positions, normals, colours (sRGB), indices; flat parts get one normal per face."""
        out = {}
        for p in s.parts:
            if len(p.f) == 0: continue
            fn = np.cross(p.v[p.f[:, 1]] - p.v[p.f[:, 0]], p.v[p.f[:, 2]] - p.v[p.f[:, 0]])
            if p.smooth:
                key = np.round(p.v, 4); _, inv = np.unique(key, axis=0, return_inverse=True); inv = inv.ravel()
                acc = np.zeros((inv.max() + 1, 3))
                for k in range(3): np.add.at(acc, inv[p.f[:, k]], fn)
                n = acc[inv]; v, c, f = p.v, p.c, p.f
            else:
                v = p.v[p.f].reshape(-1, 3); c = p.c[p.f].reshape(-1, 3); n = np.repeat(fn, 3, axis=0); f = np.arange(len(v)).reshape(-1, 3)
            n = n / np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-12)
            mat = p.mat if p.mat != 'glow' else 'glow_' + ''.join('%02x' % int(round(x * 255)) for x in np.clip(p.c[0], 0, 1))
            o = out.setdefault(mat, [[], [], [], [], 0])
            o[0].append(v); o[1].append(n); o[2].append(c); o[3].append(f + o[4]); o[4] += len(v)
        return {m: (np.vstack(o[0]), np.vstack(o[1]), np.vstack(o[2]), np.vstack(o[3])) for m, o in out.items()}

    def save(s, path):
        prims = []; mats = []; accs = []; views = []; blob = bytearray()
        def push(arr, ctype, typ, target, mm=False):
            while len(blob) % 4: blob.append(0)
            b = arr.tobytes(); views.append({"buffer": 0, "byteOffset": len(blob), "byteLength": len(b), "target": target}); blob.extend(b)
            a = {"bufferView": len(views) - 1, "componentType": ctype, "count": len(arr), "type": typ}
            if mm: a["min"] = arr.min(0).tolist(); a["max"] = arr.max(0).tolist()
            accs.append(a); return len(accs) - 1
        for m, (v, n, c, f) in sorted(s.baked().items()):
            mat = {"name": "TR_" + m.split('_')[0].capitalize(), "pbrMetallicRoughness": {"baseColorFactor": [1, 1, 1, 1], "metallicFactor": 0.0, "roughnessFactor": 0.9}}
            if m == 'metal': mat["pbrMetallicRoughness"].update(metallicFactor=0.8, roughnessFactor=0.35)
            if m == 'matte2s': mat["doubleSided"] = True; mat["name"] = "TR_Matte2S"
            if m.startswith('glow_'): mat["name"] = "TR_Glow_" + m[5:]; mat["emissiveFactor"] = [float(x) for x in lin(hexc(m[5:]))]
            rgba = np.hstack([lin(np.clip(c, 0, 1)), np.ones((len(c), 1))]).astype('<f4')
            prims.append({"attributes": {"POSITION": push(v.astype('<f4'), 5126, "VEC3", 34962, True), "NORMAL": push(n.astype('<f4'), 5126, "VEC3", 34962),
                                         "COLOR_0": push(rgba, 5126, "VEC4", 34962)}, "indices": push(f.astype('<u4').reshape(-1), 5125, "SCALAR", 34963), "material": len(mats)})
            mats.append(mat)
        g = {"asset": {"version": "2.0", "generator": "Twin Realms kit_gen"}, "scene": 0, "scenes": [{"nodes": [0]}], "nodes": [{"name": s.name, "mesh": 0}],
             "meshes": [{"name": s.name, "primitives": prims}], "materials": mats, "accessors": accs, "bufferViews": views, "buffers": [{"byteLength": len(blob)}]}
        js = json.dumps(g, separators=(',', ':')).encode(); js += b' ' * (-len(js) % 4); blob += b'\0' * (-len(blob) % 4)
        with open(path, 'wb') as fh:
            fh.write(struct.pack('<III', 0x46546C67, 2, 28 + len(js) + len(blob))); fh.write(struct.pack('<II', len(js), 0x4E4F534A)); fh.write(js)
            fh.write(struct.pack('<II', len(blob), 0x004E4942)); fh.write(blob)
