"""MakeHuman (CC0) hair, ear shapes and face skin for the realistic hero bodies (A-34).

The game bodies (assets/models/char/TR_<Sex>_Body.glb, tools/blender_gothic/humans.py) are the MakeHuman base mesh (MPFB),
so MakeHuman's own CC0 assets fit them exactly. This script needs no MPFB and no scene: it reads files and writes files.

Run headless (Blender only for its Python + numpy + image IO):
    blender -b --factory-startup --python-exit-code 1 --python tools/blender_gothic/mh_head_assets.py -- <src> [--dry]
<src> = a folder holding the unpacked asset packs (see SOURCES): makehuman_system_assets_cc0.zip -> dl_mh_system/x,
hair01_cc0.zip -> dl_hair01/x, animal01_cc0.zip -> dl_animal01/x, ears01_cc0.zip -> dl_ears01/x
(https://files2.makehumancommunity.org/asset_packs/<pack>/<pack>_cc0.zip). The MakeHuman base mesh and ear targets come
from the MPFB extension's data folder (CC0 since 2020).

How the fit works: every body vertex of the game mesh is found in the MakeHuman base mesh by its UV (both use MakeHuman's
default UV map). A least-squares affine A maps the default MakeHuman head (decimetres) onto the game head (metres). The game
body is lifted back through A, MakeHuman's helper vertices (the hair helper the proxies hang on, deleted from the game body)
follow their nearest body vertices, and each proxy (.mhclo: 3 reference vertices, weights and a scaled offset) is fitted
exactly the way MakeHuman fits it, then mapped back through A. Hair is written relative to the Head bone's rest position in
model axes (+Y up, +Z = the face), so it hangs on a BoneAttachment3D on "Head" (Avatar.cs HairPiece).

Writes:
  assets/models/hair/mh/TR_MH_<style>_<Sex>.glb + greyscale strand textures (the game tints them with the hair colour)
  data/ear_morphs.json  per body: ear shape deltas keyed by vertex position (Avatar.EarMesh blends them per race)
  assets/models/char/TR_<Sex>_skin_mh.png  MakeHuman skin, neutralised like the old baked map, MakeHuman brows/lash line painted in
"""
import bpy, sys, os, json, struct, gzip, math
import numpy as np
from mathutils import kdtree

ROOT = os.path.join(os.path.expanduser("~"), "Documents", "GitHub", "twin-realms")
MPFB = os.path.join(os.environ.get("APPDATA", ""), "Blender Foundation", "Blender", "5.3", "extensions", "user_default", "mpfb", "data")
ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
SRC = ARGS[0] if ARGS else ""
DRY = "--dry" in ARGS
SOURCES = {"sys": os.path.join(SRC, "dl_mh_system", "x"), "com": os.path.join(SRC, "dl_hair01", "x"),
           "ani": os.path.join(SRC, "dl_animal01", "x"), "ears": os.path.join(SRC, "dl_ears01", "x"), "mpfb": MPFB}
NBODY = 13380   # hm08: vertices 0..13379 are the body, the rest helpers

# style id -> (source, mhclo path, subdivision levels, twin = mirror the hanging part to the other side)
STYLES = {
    "short01": ("sys", "hair/short01/short01.mhclo", 0, False),
    "short02": ("sys", "hair/short02/short02.mhclo", 0, False),
    "short03": ("sys", "hair/short03/short03.mhclo", 0, False),
    "short04": ("sys", "hair/short04/short04.mhclo", 0, False),
    "long01": ("sys", "hair/long01/long01.mhclo", 0, False),
    "ponytail01": ("sys", "hair/ponytail01/ponytail01.mhclo", 0, False),
    "braid01": ("sys", "hair/braid01/braid01.mhclo", 0, False),
    "twinbraids": ("sys", "hair/braid01/braid01.mhclo", 0, True),
    "bob01": ("sys", "hair/bob01/bob01.mhclo", 0, False),
    "bob02": ("sys", "hair/bob02/bob02.mhclo", 0, False),
    "afro01": ("sys", "hair/afro01/afro01.mhclo", 0, False),
    "shaggy": ("com", "hair/cortu_shaggy_green_hair/cortu_shaggy_green_hair.mhclo", 0, False),
    "straightbangs": ("com", "hair/cortu_straight_bangs/cortu_straight_bangs.mhclo", 0, False),
    "bluntbob": ("com", "hair/toigo_blunt_bob_with_bangs/toigo_blunt_bob_with_bangs.mhclo", 1, False),
    "curledbob": ("com", "hair/toigo_curled_under_bob_with_bangs/toigo_curled_under_bob_with_bangs.mhclo", 1, False),
    "invertedbob": ("com", "hair/toigo_inverted_bob/toigo_inverted_bob.mhclo", 1, False),
}
BODY_STYLES = {
    "male": ["short02", "short01", "short04", "shaggy", "short03", "ponytail01", "braid01", "long01", "afro01", "twinbraids"],
    "female": ["long01", "ponytail01", "straightbangs", "bob01", "bluntbob", "braid01", "twinbraids", "bob02", "short02",
               "curledbob", "invertedbob", "shaggy", "afro01", "short03"],
}
EAR_TARGETS = {   # name -> target files (both sides); deltas in MakeHuman decimetres
    "pointed": [("mpfb", "targets/ears/l-ear-shape-pointed.target.gz"), ("mpfb", "targets/ears/r-ear-shape-pointed.target.gz")],
    "tall": [("mpfb", "targets/ears/l-ear-scale-vert-incr.target.gz"), ("mpfb", "targets/ears/r-ear-scale-vert-incr.target.gz")],
    "back": [("mpfb", "targets/ears/l-ear-rot-backward.target.gz"), ("mpfb", "targets/ears/r-ear-rot-backward.target.gz")],
    "up": [("mpfb", "targets/ears/l-ear-trans-up.target.gz"), ("mpfb", "targets/ears/r-ear-trans-up.target.gz")],
    "big": [("mpfb", "targets/ears/l-ear-scale-incr.target.gz"), ("mpfb", "targets/ears/r-ear-scale-incr.target.gz")],
    "wing": [("mpfb", "targets/ears/l-ear-wing-incr.target.gz"), ("mpfb", "targets/ears/r-ear-wing-incr.target.gz")],
    "flat": [("mpfb", "targets/ears/l-ear-wing-decr.target.gz"), ("mpfb", "targets/ears/r-ear-wing-decr.target.gz")],
    "flap": [("ears", "targets/ears/elvs_flap_ears_1.target")],
    "cat": [("ani", "targets/animal/titleknown_catgirl_ears.target")],
}
SKINS = {"male": ("sys", "skins/young_caucasian_male/young_lightskinned_male_diffuse.png"),
         "female": ("sys", "skins/young_caucasian_female/young_lightskinned_female_diffuse.png")}
PAINT = {"male": [("sys", "eyebrows/eyebrow001/eyebrow001.mhclo", 0.92), ("sys", "eyelashes/eyelashes01/eyelashes01.mhclo", 0.35)],
         "female": [("sys", "eyebrows/eyebrow010/eyebrow010.mhclo", 0.9), ("sys", "eyelashes/eyelashes02/eyelashes02.mhclo", 0.5)]}
HAIR_TEX = 1024
TWIN = {"from": -0.03, "to": -0.17, "side": 0.08}   # twin braids: metres below the Head bone where the braid starts to swing out, where it is fully out, how far


def P(*a):
    print("[mh]", *a, flush=True)


def src(s, rel):
    return os.path.join(SOURCES[s], *rel.split("/"))


# ------------------------------------------------------------------ readers
def read_glb(path):
    b = open(path, "rb").read()
    jl = struct.unpack_from("<I", b, 12)[0]
    j = json.loads(b[20:20 + jl].decode("utf-8"))
    off = 20 + jl
    bl = struct.unpack_from("<I", b, off)[0]
    return j, b[off + 8:off + 8 + bl]


def accessor(j, binb, i):
    a = j["accessors"][i]; bv = j["bufferViews"][a["bufferView"]]
    dt = {5126: np.float32, 5123: np.uint16, 5125: np.uint32, 5121: np.uint8}[a["componentType"]]
    n = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}[a["type"]]
    start = bv.get("byteOffset", 0) + a.get("byteOffset", 0); stride = bv.get("byteStride", 0); isz = np.dtype(dt).itemsize * n
    if stride and stride != isz:
        raw = np.frombuffer(binb, dtype=np.uint8, count=stride * a["count"], offset=start).reshape(a["count"], stride)[:, :isz]
        return np.ascontiguousarray(raw).view(dt).reshape(a["count"], n)
    return np.frombuffer(binb, dtype=dt, count=a["count"] * n, offset=start).reshape(a["count"], n)


def read_obj(path):
    V, VT, F = [], [], []
    for ln in open(path, encoding="utf-8", errors="replace"):
        if ln.startswith("v "):
            V.append([float(x) for x in ln.split()[1:4]])
        elif ln.startswith("vt "):
            VT.append([float(x) for x in ln.split()[1:3]])
        elif ln.startswith("f "):
            vs, ts = [], []
            for c in ln.split()[1:]:
                p = c.split("/"); vs.append(int(p[0]) - 1); ts.append(int(p[1]) - 1 if len(p) > 1 and p[1] else -1)
            F.append((vs, ts))
    return np.array(V, np.float64), np.array(VT, np.float64) if VT else np.zeros((0, 2)), F


def read_mhclo(path):
    d = os.path.dirname(path); refs, w, off = [], [], []; sc = {}; obj = mat = None; inv = False
    for ln in open(path, encoding="utf-8", errors="replace"):
        t = ln.split()
        if not t or t[0].startswith("#"):
            continue
        try:
            nums = [float(x) for x in t]
        except ValueError:
            nums = None
        if nums is not None:   # numbers: a vertex line inside the verts section (delete_verts ranges are skipped)
            if inv and len(nums) == 1:
                refs.append([int(nums[0])] * 3); w.append([1, 0, 0]); off.append([0, 0, 0])
            elif inv and len(nums) == 9:
                refs.append([int(x) for x in nums[:3]]); w.append(nums[3:6]); off.append(nums[6:9])
            continue
        if t[0] == "verts":
            inv = True
        elif t[0] == "delete_verts":
            inv = False
        elif t[0] in ("x_scale", "y_scale", "z_scale"):
            sc[t[0][0]] = (int(t[1]), int(t[2]), float(t[3]))
        elif t[0] == "obj_file":
            obj = os.path.join(d, t[1])
        elif t[0] == "material":
            mat = os.path.join(d, t[1])
    tex = None
    if mat and os.path.exists(mat):
        for ln in open(mat, encoding="utf-8", errors="replace"):
            t = ln.split()
            if len(t) >= 2 and t[0] == "diffuseTexture":
                tex = os.path.join(d, os.path.basename(t[1]))
    return dict(refs=np.array(refs, np.int64), w=np.array(w, np.float64), off=np.array(off, np.float64), sc=sc, obj=obj, tex=tex)


def read_target(path):
    op = gzip.open if path.endswith(".gz") else open
    out = {}
    with op(path, "rt", encoding="utf-8", errors="replace") as f:
        for ln in f:
            t = ln.split()
            if len(t) == 4 and not t[0].startswith("#"):
                try:
                    out[int(t[0])] = np.array([float(x) for x in t[1:]])
                except ValueError:
                    pass
    return out


def load_pixels(path, size=None):
    im = bpy.data.images.load(path, check_existing=False)
    if size and (im.size[0] > size or im.size[1] > size):
        im.scale(size, size)
    w, h = im.size; px = np.empty(w * h * 4, np.float32); im.pixels.foreach_get(px)
    bpy.data.images.remove(im)
    return px.reshape(h, w, 4)   # row 0 = bottom (v = 0)


def save_pixels(arr, path):
    h, w, _ = arr.shape
    im = bpy.data.images.new(os.path.basename(path), w, h, alpha=True)
    im.pixels.foreach_set(np.clip(arr, 0, 1).astype(np.float32).ravel())
    im.filepath_raw = path; im.file_format = "PNG"; im.save(); bpy.data.images.remove(im)


# ------------------------------------------------------------------ the body: MakeHuman vertex -> game vertex
BASE = read_obj(os.path.join(MPFB, "3dobjs", "base.obj"))


class Body:
    def __init__(self, sex):
        self.sex = sex
        j, binb = read_glb(os.path.join(ROOT, "assets", "models", "char", "TR_%s_Body.glb" % sex.capitalize()))
        mi = next(m for m in j["meshes"] if m["name"].startswith("Body"))
        pr = mi["primitives"][0]
        self.P = accessor(j, binb, pr["attributes"]["POSITION"]).astype(np.float64)
        self.UV = accessor(j, binb, pr["attributes"]["TEXCOORD_0"]).astype(np.float64)
        sk = j["skins"][0]; names = [j["nodes"][n]["name"] for n in sk["joints"]]
        ibm = accessor(j, binb, sk["inverseBindMatrices"]).astype(np.float64)
        hi = names.index("Head"); self.headM = np.linalg.inv(ibm[hi].reshape(4, 4).T); self.head = self.headM[:3, 3].copy()
        # MakeHuman vertex -> game vertex by UV (glTF v = 1 - obj v)
        V, VT, F = BASE
        vt_of = np.full(len(V), -1, np.int64)
        for vs, ts in F:
            for v, t in zip(vs, ts):
                if vt_of[v] < 0 and t >= 0:
                    vt_of[v] = t
        kd = kdtree.KDTree(len(self.UV))
        for i, (u, v) in enumerate(self.UV):
            kd.insert((u, 1.0 - v, 0.0), i)
        kd.balance()
        self.match = np.full(NBODY, -1, np.int64); far = 0
        for i in range(NBODY):
            if vt_of[i] < 0:
                continue
            u, v = VT[vt_of[i]]; co, gi, dist = kd.find((u, v, 0.0))
            if dist < 3e-4:
                self.match[i] = gi
            else:
                far += 1
        m = self.match >= 0
        P("%s: %d game verts, %d/%d MakeHuman body verts matched by UV (%d without a close UV)" % (sex, len(self.P), m.sum(), NBODY, far))
        # head affine: default MakeHuman (dm) -> game (m), least squares over the head
        idx = np.nonzero(m)[0]; gp = self.P[self.match[idx]]
        sel = (np.linalg.norm(gp - self.head, axis=1) < 0.17) & (gp[:, 1] > self.head[1] - 0.04)
        X = np.hstack([V[idx[sel]], np.ones((sel.sum(), 1))])
        sol, *_ = np.linalg.lstsq(X, gp[sel], rcond=None)
        self.A = sol[:3].T; self.t = sol[3]
        res = np.linalg.norm(X @ sol - gp[sel], axis=1)
        sv = np.linalg.svd(self.A, compute_uv=False)
        P("%s: Head bone rest at %s; head affine over %d verts: scale %s (m per MakeHuman dm), residual mean %.4f max %.4f m"
          % (sex, np.round(self.head, 4).tolist(), sel.sum(), np.round(sv, 4).tolist(), res.mean(), res.max()))
        # lift the game body back into MakeHuman space; helpers follow their nearest matched body vertices
        Ainv = np.linalg.inv(self.A)
        W = V.copy()
        W[idx] = (Ainv @ (gp - self.t).T).T
        kd2 = kdtree.KDTree(len(idx))
        for k, i in enumerate(idx):
            kd2.insert(V[i], k)
        kd2.balance()
        resid = W[idx] - V[idx]
        for i in range(len(V)):
            if i < NBODY and self.match[i] >= 0:
                continue
            nb = kd2.find_n(V[i], 8); ws = np.array([1.0 / (d * d + 1e-6) for _, _, d in nb]); ks = [k for _, k, _ in nb]
            W[i] = V[i] + (resid[ks] * ws[:, None]).sum(0) / ws.sum()
        self.W = W
        self.uvOf = np.array([VT[t] if t >= 0 else (0.0, 0.0) for t in vt_of])   # one UV per MakeHuman vertex (obj, v up)

    def to_game(self, w):
        return (self.A @ w.T).T + self.t

    def fit(self, clo):
        """MakeHuman proxy fitting (proxy.py getCoords) on the lifted game body, then back to game space."""
        W = self.W; r = clo["refs"]; w = clo["w"]
        c = W[r[:, 0]] * w[:, 0:1] + W[r[:, 1]] * w[:, 1:2] + W[r[:, 2]] * w[:, 2:3]
        s = np.ones(3)
        for k, ax in (("x", 0), ("y", 1), ("z", 2)):
            if k in clo["sc"]:
                a, b, d = clo["sc"][k]; s[ax] = abs(W[a][ax] - W[b][ax]) / d
        c = c + clo["off"] * s
        return self.to_game(c)


# ------------------------------------------------------------------ hair
def grey_hair_texture(path, out):
    px = load_pixels(path, HAIR_TEX)
    a = px[..., 3]; lum = px[..., 0] * .299 + px[..., 1] * .587 + px[..., 2] * .114
    on = a > .3
    hi = np.percentile(lum[on], 96) if on.any() else 1.0
    g = np.clip(lum / max(hi, 1e-3), 0, 1)
    g = .3 + .7 * g ** .85   # light strands with their own contrast; the game multiplies them by the hair colour
    o = np.empty_like(px); o[..., 0] = o[..., 1] = o[..., 2] = g; o[..., 3] = a
    if not DRY:
        save_pixels(o, out)
    return out


def tris_of(F):
    out = []
    for vs, ts in F:
        for k in range(1, len(vs) - 1):
            out.append(((vs[0], ts[0]), (vs[k], ts[k]), (vs[k + 1], ts[k + 1])))
    return out


def subdivide(pos, VT, F, levels):
    """Catmull-Clark through a Blender mesh (the toigo bobs are low-poly shells meant to be subdivided)."""
    me = bpy.data.meshes.new("h"); me.from_pydata(pos.tolist(), [], [vs for vs, _ in F])
    uvl = me.uv_layers.new(name="UV")
    li = 0
    for vs, ts in F:
        for t in ts:
            uvl.data[li].uv = tuple(VT[t]) if t >= 0 else (0.0, 0.0); li += 1
    ob = bpy.data.objects.new("h", me); bpy.context.scene.collection.objects.link(ob)
    md = ob.modifiers.new("s", "SUBSURF"); md.levels = levels; md.render_levels = levels; md.uv_smooth = "PRESERVE_BOUNDARIES"
    dg = bpy.context.evaluated_depsgraph_get(); ev = ob.evaluated_get(dg); m2 = ev.to_mesh()
    P2 = np.array([v.co[:] for v in m2.vertices]); uv2 = m2.uv_layers.active.data
    VT2, F2, seen = [], [], {}
    for p in m2.polygons:
        vs = [m2.loops[l].vertex_index for l in p.loop_indices]; ts = []
        for l in p.loop_indices:
            u = (round(uv2[l].uv[0], 6), round(uv2[l].uv[1], 6))
            if u not in seen:
                seen[u] = len(VT2); VT2.append(u)
            ts.append(seen[u])
        F2.append((vs, ts))
    ev.to_mesh_clear(); bpy.data.objects.remove(ob); bpy.data.meshes.remove(me)
    return P2, np.array(VT2), F2


def write_hair_glb(path, pos, VT, tris, tex_uri, mat_name):
    # vertices = unique (position, uv) pairs; normals area-weighted per position
    key = {}; vp, vu, idx = [], [], []
    for tri in tris:
        for v, t in tri:
            k = (v, t)
            if k not in key:
                key[k] = len(vp); vp.append(v); vu.append(VT[t] if t >= 0 else (0, 0))
            idx.append(key[k])
    vp = np.array(vp); pp = pos[vp].astype(np.float32); uv = np.array(vu, np.float32); uv[:, 1] = 1.0 - uv[:, 1]
    ii = np.array(idx, np.uint32).reshape(-1, 3)
    fn = np.cross(pos[vp[ii[:, 1]]] - pos[vp[ii[:, 0]]], pos[vp[ii[:, 2]]] - pos[vp[ii[:, 0]]])
    acc = np.zeros((len(pos), 3))
    for c in range(3):
        np.add.at(acc, vp[ii[:, c]], fn)
    n = acc[vp]; ln = np.linalg.norm(n, axis=1, keepdims=True); n = np.where(ln > 1e-12, n / np.maximum(ln, 1e-12), [0, 1, 0]).astype(np.float32)
    blobs = [pp.tobytes(), n.tobytes(), uv.tobytes(), ii.astype(np.uint32).tobytes()]
    views, off = [], 0; binb = b""
    for k, bl in enumerate(blobs):
        pad = (-len(bl)) % 4
        views.append({"buffer": 0, "byteOffset": off, "byteLength": len(bl), "target": 34963 if k == 3 else 34962})
        binb += bl + b"\0" * pad; off += len(bl) + pad
    j = {"asset": {"version": "2.0", "generator": "twin-realms mh_head_assets.py"},
         "scene": 0, "scenes": [{"nodes": [0]}], "nodes": [{"name": "Hair", "mesh": 0}],
         "meshes": [{"name": "Hair", "primitives": [{"attributes": {"POSITION": 0, "NORMAL": 1, "TEXCOORD_0": 2}, "indices": 3, "material": 0}]}],
         "materials": [{"name": mat_name, "doubleSided": True, "alphaMode": "MASK", "alphaCutoff": 0.42,
                        "pbrMetallicRoughness": {"baseColorTexture": {"index": 0}, "metallicFactor": 0.0, "roughnessFactor": 0.7}}],
         "textures": [{"source": 0, "sampler": 0}], "samplers": [{"magFilter": 9729, "minFilter": 9987, "wrapS": 10497, "wrapT": 10497}],
         "images": [{"uri": tex_uri}],
         "buffers": [{"byteLength": len(binb)}], "bufferViews": views,
         "accessors": [
             {"bufferView": 0, "componentType": 5126, "count": len(pp), "type": "VEC3", "min": pp.min(0).tolist(), "max": pp.max(0).tolist()},
             {"bufferView": 1, "componentType": 5126, "count": len(n), "type": "VEC3"},
             {"bufferView": 2, "componentType": 5126, "count": len(uv), "type": "VEC2"},
             {"bufferView": 3, "componentType": 5125, "count": ii.size, "type": "SCALAR"}]}
    js = json.dumps(j, separators=(",", ":")).encode("utf-8"); js += b" " * ((-len(js)) % 4)
    out = struct.pack("<III", 0x46546C67, 2, 12 + 8 + len(js) + 8 + len(binb)) + struct.pack("<II", len(js), 0x4E4F534A) + js + struct.pack("<II", len(binb), 0x004E4942) + binb
    if not DRY:
        open(path, "wb").write(out)
    return len(pp), len(ii), len(out)


def build_hair(body, sid, outdir, report):
    s, rel, levels, twin = STYLES[sid]
    clo = read_mhclo(src(s, rel)); V, VT, F = read_obj(clo["obj"])
    if len(V) != len(clo["refs"]):
        raise RuntimeError("%s: %d obj verts, %d mhclo verts" % (sid, len(V), len(clo["refs"])))
    pos = body.fit(clo) - body.head          # model axes, relative to the Head bone's rest position
    if levels:
        pos, VT, F = subdivide(pos, VT, F, levels)
    tris = tris_of(F)
    if twin:   # twin braids: the braid hangs at the middle of the back, so it is swung out to one side below the nape
        # and a mirrored copy hangs on the other side (both start together at the nape and part as they fall)
        y0, y1, side = TWIN["from"], TWIN["to"], TWIN["side"]
        k = np.clip((y0 - pos[:, 1]) / (y0 - y1), 0, 1); k = k * k * (3 - 2 * k)
        low = [t for t in tris if all(pos[v][1] < y0 for v, _ in t)]
        cx = np.mean([pos[v][0] for t in low for v, _ in t]) if low else 0.0
        a = pos.copy(); a[:, 0] = a[:, 0] + (side - cx) * k      # this side (the cap above y0 stays where it is)
        b = a * [-1, 1, 1]                                         # the other side
        n0 = len(pos); pos = np.vstack([a, b])
        tris = tris + [((p[0] + n0, p[1]), (r[0] + n0, r[1]), (q[0] + n0, q[1])) for p, q, r in low]
        report.append("    twin: %d hanging triangles mirrored (braid centre x %.3f m, swung %.3f m to each side from y %.2f to %.2f)" % (len(low), cx, side, y0, y1))
    texname = "TR_MH_" + os.path.splitext(os.path.basename(clo["tex"]))[0] + ".png"
    grey_hair_texture(clo["tex"], os.path.join(outdir, texname))
    nv, nt, size = write_hair_glb(os.path.join(outdir, "TR_MH_%s_%s.glb" % (sid, body.sex.capitalize())), pos, VT, tris, texname, "TR_Hair_MH_" + sid)
    used = np.unique([v for t in tris for v, _ in t]); q = pos[used]
    report.append("  %-14s %-6s verts %6d tris %6d  %5.0f KB  box x %+.3f..%+.3f  y %+.3f..%+.3f  z %+.3f..%+.3f  helper refs %d%%"
                  % (sid, body.sex, nv, nt, size / 1024, q[:, 0].min(), q[:, 0].max(), q[:, 1].min(), q[:, 1].max(), q[:, 2].min(), q[:, 2].max(),
                     100 * np.mean(clo["refs"] >= NBODY)))
    return texname


# ------------------------------------------------------------------ ears
def ear_morphs(body):
    out = {}
    for name, files in EAR_TARGETS.items():
        d = {}
        for s, rel in files:
            d.update(read_target(src(s, rel)))
        rows = []; mx = 0.0
        for i, dv in d.items():
            if i >= NBODY or body.match[i] < 0:
                continue
            g = body.A @ dv
            if np.linalg.norm(g) < 2e-5:
                continue
            p = body.P[body.match[i]]; mx = max(mx, float(np.linalg.norm(g)))
            rows.append([round(float(x), 5) for x in (*p, *g)])
        out[name] = rows
        P("  ear target %-8s %s: %4d verts, largest move %.1f mm" % (name, body.sex, len(rows), mx * 1000))
    return out


# ------------------------------------------------------------------ face skin
def paint_proxy(img, body, clo_path, strength):
    clo = read_mhclo(clo_path); V, VT, F = read_obj(clo["obj"])
    tex = load_pixels(clo["tex"]); th, tw, _ = tex.shape; h, w, _ = img.shape
    r = clo["refs"]; wt = clo["w"]
    suv = body.uvOf[r[:, 0]] * wt[:, 0:1] + body.uvOf[r[:, 1]] * wt[:, 1:2] + body.uvOf[r[:, 2]] * wt[:, 2:3]   # skin UV under each card vertex
    n = 0
    for tri in tris_of(F):
        a = np.array([suv[v] * (w, h) for v, _ in tri]); c = np.array([VT[t] for _, t in tri])
        x0, y0 = np.floor(a.min(0)).astype(int); x1, y1 = np.ceil(a.max(0)).astype(int)
        x0, y0 = max(x0, 0), max(y0, 0); x1, y1 = min(x1, w - 1), min(y1, h - 1)
        if x1 < x0 or y1 < y0:
            continue
        ys, xs = np.mgrid[y0:y1 + 1, x0:x1 + 1]; px = np.stack([xs + .5, ys + .5], -1).reshape(-1, 2)
        T = np.array([a[1] - a[0], a[2] - a[0]]).T
        if abs(np.linalg.det(T)) < 1e-9:
            continue
        l = np.linalg.solve(T, (px - a[0]).T).T; l1, l2 = l[:, 0], l[:, 1]; l0 = 1 - l1 - l2
        inside = (l0 >= -1e-3) & (l1 >= -1e-3) & (l2 >= -1e-3)
        if not inside.any():
            continue
        cu = (c[0] * l0[:, None] + c[1] * l1[:, None] + c[2] * l2[:, None])[inside]
        tx = np.clip((cu[:, 0] * tw).astype(int), 0, tw - 1); ty = np.clip((cu[:, 1] * th).astype(int), 0, th - 1)
        smp = tex[ty, tx]; al = np.clip(smp[:, 3], 0, 1) ** .6 * strength   # ^.6: the sparse strand cards fill in to a solid brow
        tone = (smp[:, 0] * .299 + smp[:, 1] * .587 + smp[:, 2] * .114) * .55   # dark, neutral: the race skin colour multiplies it
        yy = ys.reshape(-1)[inside]; xx = xs.reshape(-1)[inside]
        for ch in range(3):
            img[yy, xx, ch] = img[yy, xx, ch] * (1 - al) + tone * al
        n += inside.sum()
    P("  painted %s: %d texels" % (os.path.basename(clo_path), n))


def face_skin(body):
    sex = body.sex
    cur = load_pixels(os.path.join(ROOT, "assets", "models", "char", "TR_%s_Body_TR_%s_skin_col.png" % (sex.capitalize(), sex)))
    mh = load_pixels(src(*SKINS[sex]), cur.shape[0])
    lc = cur[..., :3].mean(-1); on = lc > .2
    ref_cur = np.median(cur[on][:, :3], axis=0); ref_mh = np.median(mh[on][:, :3], axis=0)
    out = mh.copy(); out[..., :3] = np.clip(mh[..., :3] / ref_mh * ref_cur, 0, 1); out[..., 3] = 1
    P("  skin %s: old map median %s, MakeHuman median %s" % (sex, np.round(ref_cur, 3).tolist(), np.round(ref_mh, 3).tolist()))
    for s, rel, k in PAINT[sex]:
        paint_proxy(out, body, src(s, rel), k)
    if not DRY:
        save_pixels(out, os.path.join(ROOT, "assets", "models", "char", "TR_%s_skin_mh.png" % sex.capitalize()))
        # a crop of the face for checking by eye (not used by the game)
        h, w, _ = out.shape; fu = body.uvOf[[i for i in range(NBODY) if body.match[i] >= 0 and abs(body.P[body.match[i]][0]) < .05 and body.P[body.match[i]][1] > body.head[1] + .02]]
        u0, v0 = (fu.min(0) * (w, h)).astype(int); u1, v1 = (fu.max(0) * (w, h)).astype(int)
        save_pixels(out[max(v0 - 20, 0):v1 + 20, max(u0 - 20, 0):u1 + 20], os.path.join(SRC, "face_%s.png" % sex))


def main():
    outdir = os.path.join(ROOT, "assets", "models", "hair", "mh"); os.makedirs(outdir, exist_ok=True)
    report = []; ears = {"_help": "Generated by tools/blender_gothic/mh_head_assets.py (MakeHuman CC0 ear targets fitted to the TR bodies). Per body and target: [x, y, z, dx, dy, dz] = a body vertex's bind-pose position and its full move in metres (model space). Avatar.EarMesh blends them by data/avatar.json earMorphs."}
    for sex in ("male", "female"):
        b = Body(sex)
        for sid in BODY_STYLES[sex]:
            build_hair(b, sid, outdir, report)
        ears[sex] = ear_morphs(b)
        face_skin(b)
    for r in report:
        P(r)
    if not DRY:
        json.dump(ears, open(os.path.join(ROOT, "data", "ear_morphs.json"), "w"), separators=(",", ":"))
    P("done" + (" (dry run, nothing written)" if DRY else ""))


main()
