"""Builds the gothic garments (humans.py GARMENTS) from the shipped bodies, without MPFB or a .blend.

    blender -b --factory-startup -P tools/blender_gothic/outfits_from_glb.py -- male female [Garment ...] [--verify] [--render] [--out DIR]

Imports assets/models/char/TR_<Sex>_Body.glb (vertices merged, so the shells have no seams), takes the skinned body and its
armature and runs the same garment functions as humans.build_outfits, so every garment is lifted off the real game body
and keeps its skin weights: it fits by construction. Exports assets/models/outfit/TR_<Sex>_<Garment>.glb (or --out DIR).
--verify  rebuilds Gambeson into %TEMP% and compares it with the shipped file (vertex/face count, bounds, bind matrices).
--render  Workbench front/back check images of the layered sets: %TEMP%/tr_outfits_<sex>.png (skin = pink, so any skin
          poking through a set shows).
No garment name = the NEW list below (the shipped ones are not overwritten unless named).
"""
import bpy, os, sys, math

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import humans as H
from mathutils import Vector

NEW = ["Chainmail", "Surcoat", "Vambraces", "Greaves", "Gauntlets", "Tassets", "Gorget", "Jerkin", "Coat", "Mantle"]
TEMP = os.environ.get("TEMP", "/tmp")
SETS = [   # layered looks for the check render (the data/avatar.json rules), plus the hands and feet pieces
    ("Chainmail", ["Gambeson", "Trousers", "Chainmail", "Gauntlets", "Greaves", "Boots"]),
    ("Plate", ["Gambeson", "Trousers", "Chainmail", "Cuirass", "Pauldrons", "Tassets", "Gorget", "Gauntlets", "Boots", "Greaves"]),
    ("Brigandine", ["Gambeson", "Trousers", "Jerkin", "Cuirass", "Gloves", "Vambraces", "Boots"]),
    ("Surcoat", ["Chainmail", "Trousers", "Surcoat", "Gloves", "Vambraces", "Boots", "Greaves"]),
    ("Coat", ["Tunic", "Trousers", "Coat", "Gloves", "Vambraces", "Boots"]),
    ("Jerkin", ["Tunic", "Trousers", "Jerkin", "Gloves", "Boots"]),
    ("Vestments", ["Robe", "Mantle", "Hood", "Boots"]),
]
COLORS = {"TR_Skin": (1.0, 0.25, 0.6), "G_Metal_Iron": (0.55, 0.57, 0.6), "G_Metal_Steel": (0.75, 0.78, 0.82),
          "G_Leather": (0.42, 0.26, 0.14), "G_Cloth_Wool": (0.45, 0.12, 0.12), "G_Cloth_Linen": (0.8, 0.74, 0.6)}


def _args():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out = None
    if "--out" in a:
        i = a.index("--out"); out = a[i + 1]; del a[i:i + 2]
    flags = {x for x in a if x.startswith("--")}
    a = [x for x in a if not x.startswith("--")]
    sexes = [x for x in a if x in ("male", "female")] or ["male", "female"]
    names = [x for x in a if x not in ("male", "female")]
    return sexes, names, flags, out


def _clear():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.armatures, bpy.data.materials, bpy.data.images, bpy.data.cameras, bpy.data.lights):
        for d in list(coll):
            coll.remove(d)


def _import(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path, merge_vertices=True)
    return [o for o in bpy.data.objects if o not in before]


def load_body(sex):
    """The shipped body: (body mesh, armature, R, lm) ready for the garment functions."""
    _clear()
    objs = _import(os.path.join(H.OUT, "TR_%s_Body.glb" % sex.capitalize()))
    arm = next(o for o in objs if o.type == "ARMATURE")
    meshes = [o for o in objs if o.type == "MESH" and any(m.type == "ARMATURE" for m in o.modifiers)]
    body = max(meshes, key=lambda o: len(o.data.vertices))
    body.name = "Body"
    bones = {b.name for b in arm.data.bones}
    named = [vg.name for vg in body.vertex_groups if vg.name in bones]
    assert len(named) > 20, "vertex groups are not bone names: %s" % [vg.name for vg in body.vertex_groups][:10]
    for m in body.modifiers:
        if m.type == "ARMATURE":
            m.object = arm
    eyes = next((o for o in meshes if o is not body), None)
    if eyes is not None:   # FACE (the Hood keeps the face open) from the eyeballs, like build_outfits
        H._brow_attr(body, eyes, sex)
    R = H._regions(body); lm = H._landmarks(arm)
    mw = arm.matrix_world
    lm["wrist_x"] = abs((mw @ arm.data.bones["hand_l"].head_local).x)
    lm["elbow_x"] = abs((mw @ arm.data.bones["lowerarm_l"].head_local).x)
    lm["shoulder_x"] = abs((mw @ arm.data.bones["upperarm_l"].head_local).x)
    return body, arm, R, lm


def build(sex, names, out_dir=None, keep=False):
    body, arm, R, lm = load_body(sex)
    made = {}
    for n in names:
        o = H.GARMENTS[n](body, R, lm, sex)
        path = os.path.join(out_dir or os.path.join(H.ROOT, "assets", "models", "outfit"), "TR_%s_%s.glb" % (sex.capitalize(), n))
        H._export_objs(path, [arm, o])
        print("[outfits] %s %s %d verts %d faces -> %s" % (sex, n, len(o.data.vertices), len(o.data.polygons), path))
        if keep:
            made[n] = o
        else:
            bpy.data.objects.remove(o)
    return body, arm, made


# ------------------------------------------------------------------ verify against a shipped garment
def _stats(path):
    objs = _import(path)
    ms = [o for o in objs if o.type == "MESH" and any(m.type == "ARMATURE" for m in o.modifiers)]   # not the bone-shape sphere
    vs = [o.matrix_world @ v.co for o in ms for v in o.data.vertices]
    lo = Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs)))
    hi = Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))
    nv = sum(len(o.data.vertices) for o in ms); nf = sum(len(o.data.polygons) for o in ms)
    for o in objs:
        bpy.data.objects.remove(o, do_unlink=True)
    return nv, nf, lo, hi


def _ibms(path):
    """joint name -> inverse bind matrix (16 floats) from a glb."""
    import json, struct
    with open(path, "rb") as f:
        data = f.read()
    jl = struct.unpack_from("<I", data, 12)[0]
    js = json.loads(data[20:20 + jl]); bin0 = 20 + jl + 8
    sk = js["skins"][0]; acc = js["accessors"][sk["inverseBindMatrices"]]; bv = js["bufferViews"][acc["bufferView"]]
    off = bin0 + bv.get("byteOffset", 0) + acc.get("byteOffset", 0)
    out = {}
    for k, j in enumerate(sk["joints"]):
        out[js["nodes"][j]["name"]] = struct.unpack_from("<16f", data, off + 64 * k)
    return out


def verify(sex):
    tmp = os.path.join(TEMP, "tr_outfits_verify"); os.makedirs(tmp, exist_ok=True)
    build(sex, ["Gambeson"], tmp)
    p_new = os.path.join(tmp, "TR_%s_Gambeson.glb" % sex.capitalize())
    p_old = os.path.join(H.ROOT, "assets", "models", "outfit", "TR_%s_Gambeson.glb" % sex.capitalize())
    _clear()
    a = _stats(p_old); b = _stats(p_new)
    print("[verify] %s shipped  %d verts %d faces bounds %s .. %s" % (sex, a[0], a[1], tuple(round(x, 4) for x in a[2]), tuple(round(x, 4) for x in a[3])))
    print("[verify] %s rebuilt  %d verts %d faces bounds %s .. %s" % (sex, b[0], b[1], tuple(round(x, 4) for x in b[2]), tuple(round(x, 4) for x in b[3])))
    io, inew, ib = _ibms(p_old), _ibms(p_new), _ibms(os.path.join(H.OUT, "TR_%s_Body.glb" % sex.capitalize()))
    worst = max(max(abs(x - y) for x, y in zip(io[k], inew[k])) for k in io if k in inew)
    worstb = max(max(abs(x - y) for x, y in zip(ib[k], inew[k])) for k in ib if k in inew)
    print("[verify] %s bind matrices: %d/%d joints shared, max diff vs shipped garment %.5f, vs body %.5f"
          % (sex, len(set(io) & set(inew)), len(io), worst, worstb))


# ------------------------------------------------------------------ check render
def _flat_copy(o, dx, name):
    c = o.copy(); c.data = o.data.copy(); c.name = name
    bpy.context.scene.collection.objects.link(c)
    for m in list(c.modifiers):
        c.modifiers.remove(m)
    c.parent = None; c.matrix_world = o.matrix_world.copy()
    c.location.x += dx
    return c


def render(sex):
    need = sorted({g for _, gs in SETS for g in gs})
    body, arm, made = build(sex, need, os.path.join(TEMP, "tr_outfits_verify"), keep=True)
    shown = []
    for k, (label, gs) in enumerate(SETS):
        dx = (k - (len(SETS) - 1) / 2) * 2.0
        shown.append(_flat_copy(body, dx, "B%d" % k))
        for g in gs:
            shown.append(_flat_copy(made[g], dx, "%s%d" % (g, k)))
    for o in list(bpy.context.scene.objects):
        if o not in shown:
            o.hide_render = True
    for m in bpy.data.materials:
        base = m.name.split(".")[0]
        c = COLORS.get(base, (0.3, 0.6, 0.3))
        m.diffuse_color = (*c, 1.0)
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "STUDIO"; sc.display.shading.color_type = "MATERIAL"
    sc.display.shading.show_cavity = True
    import numpy as np
    T = 700
    sc.render.resolution_x = T; sc.render.resolution_y = T
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); sc.collection.objects.link(cam); sc.camera = cam
    cam.data.type = "ORTHO"
    imgs = []
    views = (("front", -6, 0, 1.0, 1.9), ("back", 6, math.pi, 1.0, 1.9), ("torso", -6, 0, 1.3, 0.9), ("torsoback", 6, math.pi, 1.3, 0.9))
    for side, y, rz, z, scale in views:   # one tile per set, gathered in a grid per view
        cam.data.ortho_scale = scale
        tiles = []
        for k in range(len(SETS)):
            dx = (k - (len(SETS) - 1) / 2) * 2.0
            cam.location = (dx, y, z); cam.rotation_euler = (math.pi / 2, 0, rz)
            p = os.path.join(TEMP, "tr_tile.png"); sc.render.filepath = p; bpy.ops.render.render(write_still=True)
            im = bpy.data.images.load(p); a = np.array(im.pixels[:], np.float32).reshape(T, T, 4); bpy.data.images.remove(im)
            tiles.append(a)
        cols = 4; rows = (len(tiles) + cols - 1) // cols
        grid = np.zeros((rows * T, cols * T, 4), np.float32); grid[..., 3] = 1
        for k, a in enumerate(tiles):
            r = rows - 1 - k // cols; c = k % cols   # Blender pixels start at the bottom row
            grid[r * T:(r + 1) * T, c * T:(c + 1) * T] = a
        out = bpy.data.images.new("grid", cols * T, rows * T, alpha=False)
        out.pixels.foreach_set(grid.reshape(-1))
        p = os.path.join(TEMP, "tr_outfits_%s_%s.png" % (sex, side))
        out.filepath_raw = p; out.file_format = "PNG"; out.save(); bpy.data.images.remove(out); imgs.append(p)
    print("[render] sets (left to right, top to bottom):", [s for s, _ in SETS], imgs)


def main():
    sexes, names, flags, out = _args()
    for sex in sexes:
        if "--verify" in flags:
            verify(sex)
            continue
        if "--check" not in flags:   # --check = only the render (garments go to %TEMP%), for tuning
            build(sex, names or NEW, out)
        if "--render" in flags or "--check" in flags:
            render(sex)


main()
