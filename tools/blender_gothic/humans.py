"""Realistic hero bodies for Twin Realms, made with MPFB (MakeHuman for Blender, CC0 base mesh) on the game's own skeleton.

Run inside Blender with the MPFB extension enabled (Blender MCP bridge or Text editor):
    exec(open(r"<repo>/tools/blender_gothic/humans.py").read()); build_body("male")
Writes assets/models/char/TR_<Sex>_Body.glb: an armature named "Armature" with the bone names and bone orientations of the
Quaternius skeleton the animation clips were made for (assets/models/anim/clips.res), so every clip plays unchanged.
How: the MPFB body is posed into the game's T-pose bone by bone, the game skeleton's limb joints are moved onto the MPFB
joints (orientations kept; the clips only rotate bones, plus the pelvis position), and MPFB's own skin weights are used.
Skin and eyes are baked from procedural materials (MPFB region masks, noise) into textures; the skin texture is a light,
neutral detail map that the game multiplies by the race's skin colour (Avatar.cs).
"""
import bpy, bmesh, math, os, importlib, random
from mathutils import Vector, Matrix

ROOT = os.path.join(os.path.expanduser("~"), "Documents", "GitHub", "twin-realms")
OUT = os.path.join(ROOT, "assets", "models", "char")
TMP = os.path.join(os.environ.get("TEMP", "/tmp"), "tr_humans")
M = "bl_ext.user_default.mpfb."
MPFB_TEX = os.path.join(os.environ.get("APPDATA", ""), "Blender Foundation", "Blender", "5.3", "extensions", "user_default", "mpfb", "data", "textures")

SHAPES = {   # MPFB macro details (0..1)
    "male": dict(gender=1.0, age=0.62, muscle=0.8, weight=0.55, proportions=0.75, height=0.62),
    "female": dict(gender=0.0, age=0.47, muscle=0.58, weight=0.48, proportions=0.85, height=0.55, cupsize=0.52, firmness=0.6),
}
GAME = {"male": "Superhero_Male_FullBody.gltf", "female": "Superhero_Female_FullBody.gltf"}
NAME = {"Head": "head", "root": "Root"}             # game bone -> MPFB bone
KEEP = {"root", "pelvis", "spine_01", "spine_02", "spine_03"}   # joints that stay where the game has them (attachments ride on them)


def _mods():
    ts = importlib.import_module(M + "services.targetservice").TargetService
    hs = importlib.import_module(M + "services.humanservice").HumanService
    hop = importlib.import_module(M + "entities.objectproperties").HumanObjectProperties
    return ts, hs, hop


def _depth(b):
    n = 0
    while b.parent:
        b = b.parent; n += 1
    return n


def _scene(name):
    sc = bpy.data.scenes.get(name) or bpy.data.scenes.new(name)
    bpy.context.window.scene = sc
    for o in list(sc.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    return sc


def _game_rig(sex):
    bpy.ops.import_scene.gltf(filepath=os.path.join(OUT, GAME[sex]))
    arm = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
    for o in [o for o in bpy.context.scene.objects if o.type != "ARMATURE"]:
        bpy.data.objects.remove(o, do_unlink=True)
    arm.name = "GameRig"
    return arm


def _mpfb_human(sex):
    ts, hs, hop = _mods()
    bpy.ops.object.select_all(action="DESELECT")
    bpy.ops.mpfb.create_human()
    h = bpy.context.active_object; h.name = "MPFB_" + sex
    for k, v in SHAPES[sex].items():
        hop.set_value(k, v, entity_reference=h)
    ts.reapply_macro_details(h)
    rig = hs.add_builtin_rig(h, "game_engine", import_weights=True)
    return h, rig


def _pose_match(rig, game):
    """Scale the MPFB rig to the game's pelvis/head heights and rotate each bone onto the game bone's direction."""
    gb = game.data.bones
    mp = rig.matrix_world @ rig.data.bones["pelvis"].head_local
    mh = rig.matrix_world @ rig.data.bones["head"].head_local
    s = (gb["pelvis"].head_local.z / mp.z + gb["Head"].head_local.z / mh.z) / 2
    rig.scale = (s, s, s); bpy.context.view_layer.update()
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="POSE")
    for pb in rig.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()
    inv = rig.matrix_world.inverted().to_3x3()
    for pb in sorted(rig.pose.bones, key=lambda p: _depth(p.bone)):
        g = {v: k for k, v in NAME.items()}.get(pb.name, pb.name)
        if g not in gb or pb.name in ("Root", "pelvis"):
            continue
        b = gb[g]
        dg = inv @ ((game.matrix_world @ b.tail_local) - (game.matrix_world @ b.head_local))
        dc = pb.tail - pb.head
        if dc.length < 1e-6 or dg.length < 1e-6:
            continue
        q = dc.normalized().rotation_difference(dg.normalized())
        pb.matrix = Matrix.Translation(pb.head) @ q.to_matrix().to_4x4() @ Matrix.Translation(-pb.head) @ pb.matrix
        bpy.context.view_layer.update()
    bpy.ops.object.mode_set(mode="OBJECT")
    d = (game.matrix_world @ gb["pelvis"].head_local) - (rig.matrix_world @ rig.pose.bones["pelvis"].head)
    rig.location += d; bpy.context.view_layer.update()


def _skeleton(rig, game):
    """The game skeleton with its limb joints moved onto the MPFB joints; every bone keeps its orientation."""
    gb = game.data.bones
    targets = {b.name: rig.matrix_world @ rig.pose.bones[NAME.get(b.name, b.name)].head for b in gb if NAME.get(b.name, b.name) in rig.pose.bones}
    new = game.copy(); new.data = game.data.copy(); new.name = "Armature"; new.data.name = "Armature"
    bpy.context.scene.collection.objects.link(new)
    bpy.ops.object.select_all(action="DESELECT"); bpy.context.view_layer.objects.active = new; new.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    eb = new.data.edit_bones
    for e in eb:
        e.use_connect = False
    inv = new.matrix_world.inverted()
    delta = {}
    for e in sorted(eb, key=_depth):
        if e.name in KEEP:
            dd = Vector()
        elif e.name in targets:
            dd = (inv @ targets[e.name]) - e.head
        else:
            dd = delta.get(e.parent.name, Vector()) if e.parent else Vector()
        e.head += dd; e.tail += dd; delta[e.name] = dd
    bpy.ops.object.mode_set(mode="OBJECT")
    return new


def _body_mesh(h, new):
    saved = {}
    for m in h.modifiers:
        if m.type == "MASK":
            saved[m.name] = m.show_viewport; m.show_viewport = False
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(h.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
    for n, v in saved.items():
        h.modifiers[n].show_viewport = v
    body = bpy.data.objects.new("Body", me); me.name = "Body"
    bpy.context.scene.collection.objects.link(body)
    body.matrix_world = h.matrix_world.copy()
    for vg in h.vertex_groups:
        body.vertex_groups.new(name=vg.name)
    gi = {vg.name: vg.index for vg in body.vertex_groups}
    bm = bmesh.new(); bm.from_mesh(me); dl = bm.verts.layers.deform.verify()
    eyes = {gi["helper-l-eye"], gi["helper-r-eye"]}; helper = gi["HelperGeometry"]
    cubes = {gi[n] for n in gi if n.startswith("JointCubes")}
    kill = [v for v in bm.verts if (helper in v[dl] and v[dl][helper] > 0.5 and not any(k in v[dl] for k in eyes))
            or any(c in v[dl] and v[dl][c] > 0.5 for c in cubes)]
    bmesh.ops.delete(bm, geom=kill, context="VERTS"); bm.to_mesh(me); bm.free()
    body.vertex_groups["head"].name = "Head"
    if "Root" in body.vertex_groups:
        body.vertex_groups["Root"].name = "root"
    # split the eyeballs into their own mesh ("Eyes": Avatar.cs leaves it alone or makes it glow)
    bpy.ops.object.select_all(action="DESELECT"); bpy.context.view_layer.objects.active = body; body.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="DESELECT")
    for n in ("helper-l-eye", "helper-r-eye"):
        body.vertex_groups.active_index = body.vertex_groups[n].index; bpy.ops.object.vertex_group_select()
    bpy.ops.mesh.separate(type="SELECTED"); bpy.ops.object.mode_set(mode="OBJECT")
    eyes_o = next(o for o in bpy.context.selected_objects if o != body)
    for o in bpy.data.objects:   # free the names for the exported nodes
        if o.name in ("Eyes", "Body") and o not in (body, eyes_o):
            o.name = o.name + "_old"
    eyes_o.name = "Eyes"; eyes_o.data.name = "Eyes"; body.name = "Body"
    for o in (body, eyes_o):
        o.parent = new; o.matrix_parent_inverse = new.matrix_world.inverted()
        am = o.modifiers.new("Armature", "ARMATURE"); am.object = new
        for p in o.data.polygons:
            p.use_smooth = True
    return body, eyes_o


# ------------------------------------------------------------------ textures
FACE = {}   # eye centre and sex for _face_masks, set by build_body


def _face_masks(nt, body):
    """Shader-math masks from the surface position (per pixel in the bake): arched brows with a hair-like breakup,
    a stubble shadow on the jaw (male), and cheek colour."""
    N, L = nt.nodes, nt.links
    geo = N.new("ShaderNodeNewGeometry")
    xyz = N.new("ShaderNodeSeparateXYZ"); L.new(geo.outputs["Position"], xyz.inputs[0])
    X, Y, Z = xyz.outputs
    cx, cy, cz = FACE["centre"]

    def op(o, a, b=None):
        n = N.new("ShaderNodeMath"); n.operation = o
        for i, v in enumerate((a, b)):
            if v is None:
                continue
            if isinstance(v, (int, float)):
                n.inputs[i].default_value = v
            else:
                L.new(v, n.inputs[i])
        return n.outputs[0]

    def gauss(d, w):
        return op("EXPONENT", op("MULTIPLY", op("POWER", op("DIVIDE", d, w), 2.0), -1.0))

    def clamp01(v):
        n = N.new("ShaderNodeClamp"); L.new(v, n.inputs[0]); return n.outputs[0]

    nz = N.new("ShaderNodeTexNoise"); nz.inputs["Scale"].default_value = 700.0; nz.inputs["Detail"].default_value = 3.0
    L.new(geo.outputs["Position"], nz.inputs["Vector"])
    front = op("LESS_THAN", Y, cy)
    brow = cheek = None
    for s in (1, -1):
        lx = op("MULTIPLY", op("SUBTRACT", X, cx * s), float(s))            # outwards from the nose
        t = clamp01(op("DIVIDE", op("ADD", lx, 0.024), 0.056))
        inside = op("MULTIPLY", op("GREATER_THAN", lx, -0.024), op("LESS_THAN", lx, 0.032))
        zc = op("ADD", cz + 0.019, op("MULTIPLY", op("SINE", op("MULTIPLY", op("MINIMUM", op("MULTIPLY", t, 1.2), 1.0), math.pi)), 0.005))
        th = op("MULTIPLY", op("SUBTRACT", 1.15, op("MULTIPLY", t, 0.5)), 0.0042)
        fade = clamp01(op("MINIMUM", op("DIVIDE", t, 0.12), op("DIVIDE", op("SUBTRACT", 1.0, t), 0.2)))
        b = op("MULTIPLY", op("MULTIPLY", gauss(op("SUBTRACT", Z, zc), th), fade), op("MULTIPLY", inside, front))
        b = op("MULTIPLY", b, op("ADD", 0.6, op("MULTIPLY", nz.outputs["Fac"], 0.6)))
        brow = b if brow is None else op("MAXIMUM", brow, b)
        cd = op("SQRT", op("ADD", op("POWER", op("SUBTRACT", X, cx * s * 1.25), 2.0), op("POWER", op("SUBTRACT", Z, cz - 0.045), 2.0)))
        ch = op("MULTIPLY", gauss(cd, 0.025), op("LESS_THAN", Y, cy + 0.02))
        cheek = ch if cheek is None else op("MAXIMUM", cheek, ch)
    if FACE.get("male"):
        jaw = op("MULTIPLY", op("LESS_THAN", Z, cz - 0.045), op("GREATER_THAN", Z, cz - 0.13))
        jaw = op("MULTIPLY", jaw, op("LESS_THAN", op("ABSOLUTE", X), 0.065))
        jaw = op("MULTIPLY", jaw, op("LESS_THAN", Y, cy + 0.06))
        beard = op("MULTIPLY", op("MULTIPLY", jaw, clamp01(op("DIVIDE", op("SUBTRACT", cz - 0.045, Z), 0.02))), 0.55)
    else:
        beard = op("MULTIPLY", X, 0.0)
    return {"brow": clamp01(brow), "cheek": cheek, "beard": beard}


def _brow_attr(body, eyes_o, sex):
    ev = [eyes_o.matrix_world @ v.co for v in eyes_o.data.vertices]
    right = [v for v in ev if v.x > 0]
    FACE["centre"] = tuple(sum(right, Vector()) / max(1, len(right)))
    FACE["male"] = sex == "male"
    return


def _brow_attr_old(body, eyes_o, sex):
    """Per-vertex brow / stubble / cheek weights for the skin bake (brows painted, not modelled)."""
    me = body.data
    ev = [eyes_o.matrix_world @ v.co for v in eyes_o.data.vertices]
    cl = sum((v for v in ev if v.x > 0), Vector()) / max(1, len([v for v in ev if v.x > 0]))
    cr = Vector((-cl.x, cl.y, cl.z))
    a = me.color_attributes.new("Paint", "FLOAT_COLOR", "POINT")
    for i, v in enumerate(me.vertices):
        p = body.matrix_world @ v.co
        brow = beard = cheek = 0.0
        for c in (cl, cr):
            lx = (p.x - c.x) * (1 if c.x > 0 else -1)        # outwards from the nose is positive
            if p.y < c.y and -0.024 < lx < 0.032:
                t = (lx + 0.024) / 0.056                        # 0 at the inner end, 1 at the tail
                zc = c.z + 0.019 + 0.005 * math.sin(math.pi * min(1.0, t * 1.2))
                thick = 0.0058 * (1.15 - 0.55 * t)
                fade = min(1.0, t / 0.12, (1 - t) / 0.2)
                brow = max(brow, math.exp(-((p.z - zc) / thick) ** 2) * max(0.0, fade))
            cd = ((p.x - c.x * 1.25) ** 2 + (p.z - (c.z - 0.045)) ** 2) ** 0.5
            if p.y < c.y + 0.02:
                cheek = max(cheek, math.exp(-(cd / 0.025) ** 2))
        if sex == "male" and p.y < cl.y + 0.06 and cl.z - 0.13 < p.z < cl.z - 0.045 and abs(p.x) < 0.06:
            beard = 0.5 * min(1.0, (cl.z - 0.045 - p.z) / 0.02)
        a.data[i].color = (brow, beard, cheek, 1.0)


def _image(name, size, non_color=False):
    im = bpy.data.images.get(name)
    if im:
        bpy.data.images.remove(im)
    im = bpy.data.images.new(name, size, size, alpha=False, float_buffer=False)
    if non_color:
        im.colorspace_settings.name = "Non-Color"
    return im


def _skin_material(body, col_img, nrm_img):
    """Procedural skin for the bake: neutral light tone, mottling, region masks (MPFB), painted brows."""
    m = bpy.data.materials.new("TR_Skin_bake"); m.use_nodes = True
    nt = m.node_tree; N = nt.nodes; L = nt.links
    for n in list(N):
        N.remove(n)
    out = N.new("ShaderNodeOutputMaterial")
    bsdf = N.new("ShaderNodeBsdfPrincipled")
    em = N.new("ShaderNodeEmission")
    co = N.new("ShaderNodeTexCoord")
    uvn = N.new("ShaderNodeUVMap"); uvn.uv_map = "UVMap"

    def mask(fname):
        t = N.new("ShaderNodeTexImage"); t.image = bpy.data.images.load(os.path.join(MPFB_TEX, fname), check_existing=True)
        t.image.colorspace_settings.name = "Non-Color"; L.new(uvn.outputs["UV"], t.inputs["Vector"])
        return t.outputs["Color"]

    def noise(scale, detail=6.0, rough=0.6):
        t = N.new("ShaderNodeTexNoise"); t.inputs["Scale"].default_value = scale
        t.inputs["Detail"].default_value = detail; t.inputs["Roughness"].default_value = rough
        L.new(co.outputs["Object"], t.inputs["Vector"])
        return t.outputs["Fac"]

    def mix(a, b, fac, blend="MIX"):
        x = N.new("ShaderNodeMix"); x.data_type = "RGBA"; x.blend_type = blend
        if isinstance(fac, float):
            x.inputs[0].default_value = fac
        else:
            L.new(fac, x.inputs[0])
        for sock, val in ((6, a), (7, b)):
            if isinstance(val, tuple):
                x.inputs[sock].default_value = (*val, 1)
            else:
                L.new(val, x.inputs[sock])
        return x.outputs[2]

    def mul(a, f):
        x = N.new("ShaderNodeMath"); x.operation = "MULTIPLY"; L.new(a, x.inputs[0]); x.inputs[1].default_value = f
        return x.outputs[0]

    base = (0.93, 0.85, 0.81)
    c = mix(base, (0.86, 0.72, 0.70), noise(9.0, 4.0))                    # blotches
    c = mix(c, (0.97, 0.9, 0.86), mul(noise(55.0, 8.0, 0.7), 0.6))         # fine mottling
    face = _face_masks(nt, body)                                            # brows / stubble / cheeks, per pixel
    c = mix(c, (0.9, 0.66, 0.64), mul(face["cheek"], 0.55))                # cheeks
    c = mix(c, (0.9, 0.78, 0.74), mul(mask("mpfb_ears.jpg"), 0.5))
    c = mix(c, (0.78, 0.5, 0.5), mask("mpfb_lips.jpg"))
    c = mix(c, (0.8, 0.66, 0.64), mul(mask("mpfb_eyelids.jpg"), 0.6))
    c = mix(c, (0.98, 0.92, 0.88), mask("mpfb_fingernails.jpg"))
    c = mix(c, (0.98, 0.92, 0.88), mask("mpfb_toenails.jpg"))
    c = mix(c, (0.72, 0.52, 0.48), mask("mpfb_aureolae.jpg"))
    c = mix(c, (0.62, 0.55, 0.52), mul(face["beard"], 0.7))                # stubble shadow
    c = mix(c, (0.12, 0.09, 0.08), mul(face["brow"], 0.95))                # brows
    L.new(c, em.inputs["Color"])
    # pores and fine wrinkles for the normal bake
    bump = N.new("ShaderNodeBump"); bump.inputs["Strength"].default_value = 0.35; bump.inputs["Distance"].default_value = 0.0006
    vor = N.new("ShaderNodeTexVoronoi"); vor.inputs["Scale"].default_value = 900.0; L.new(co.outputs["Object"], vor.inputs["Vector"])
    hsum = N.new("ShaderNodeMath"); hsum.operation = "ADD"
    L.new(vor.outputs["Distance"], hsum.inputs[0]); L.new(mul(noise(140.0, 6.0, 0.7), 0.6), hsum.inputs[1])
    L.new(hsum.outputs[0], bump.inputs["Height"]); L.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
    tc = N.new("ShaderNodeTexImage"); tc.image = col_img
    tn = N.new("ShaderNodeTexImage"); tn.image = nrm_img
    for t in (tc, tn):
        L.new(uvn.outputs["UV"], t.inputs["Vector"])
    body.data.materials.clear(); body.data.materials.append(m)
    return m, out, em, bsdf, tc, tn


def _bake_skin(body, sex, size=2048):
    col = _image("TR_%s_skin_col" % sex, size); nrm = _image("TR_%s_skin_nrm" % sex, size, True)
    m, out, em, bsdf, tc, tn = _skin_material(body, col, nrm)
    nt = m.node_tree; sc = bpy.context.scene
    try:
        sc.render.engine = "CYCLES"
    except TypeError as e:
        print("no cycles", e); return None
    sc.cycles.samples = 1; sc.cycles.device = "CPU"
    sc.render.bake.target = "IMAGE_TEXTURES"; sc.render.bake.margin = 8
    bpy.ops.object.select_all(action="DESELECT"); body.select_set(True); bpy.context.view_layer.objects.active = body
    for (kind, src, img_node) in (("EMIT", em, tc), ("NORMAL", bsdf, tn)):
        for l in list(out.inputs["Surface"].links):
            nt.links.remove(l)
        nt.links.new(src.outputs[0], out.inputs["Surface"])
        nt.nodes.active = img_node; img_node.select = True
        if kind == "NORMAL":
            sc.render.bake.normal_space = "TANGENT"
        bpy.ops.object.bake(type=kind)
    os.makedirs(TMP, exist_ok=True)
    nrm.scale(size // 2, size // 2)   # pores read fine at half size; keeps the file small
    for im in (col, nrm):
        im.filepath_raw = os.path.join(TMP, im.name + ".png"); im.file_format = "PNG"; im.save()
    # the exported material: Principled with the baked maps
    fm = bpy.data.materials.new("TR_Skin"); fm.use_nodes = True
    fn = fm.node_tree.nodes; fp = next(n for n in fn if n.type == "BSDF_PRINCIPLED")
    a = fn.new("ShaderNodeTexImage"); a.image = col
    b = fn.new("ShaderNodeTexImage"); b.image = nrm
    nmap = fn.new("ShaderNodeNormalMap")
    fm.node_tree.links.new(a.outputs["Color"], fp.inputs["Base Color"])
    fm.node_tree.links.new(b.outputs["Color"], nmap.inputs["Color"]); fm.node_tree.links.new(nmap.outputs["Normal"], fp.inputs["Normal"])
    fp.inputs["Roughness"].default_value = 0.52
    body.data.materials.clear(); body.data.materials.append(fm)
    bpy.data.materials.remove(m)
    return fm


def _eyes(eyes_o, sex, size=512, iris=(0.33, 0.27, 0.2)):
    """Front-centred UVs (azimuthal projection per eyeball) and a painted eye texture: sclera, iris streaks, limbus, pupil."""
    import numpy as np
    me = eyes_o.data
    if not me.uv_layers:
        me.uv_layers.new(name="UVMap")
    uv = me.uv_layers[0].data
    ws = [eyes_o.matrix_world @ v.co for v in me.vertices]
    centres = {}
    for side in (1, -1):
        pts = [w for w in ws if w.x * side > 0]
        centres[side] = sum(pts, Vector()) / len(pts)
    fwd = Vector((0, -1, 0))
    for p in me.polygons:
        for li in p.loop_indices:
            w = ws[me.loops[li].vertex_index]; c = centres[1 if w.x > 0 else -1]
            d = (w - c).normalized(); th = math.acos(max(-1, min(1, d.dot(fwd))))
            ph = math.atan2(d.z, d.x); r = 0.5 * th / math.pi
            uv[li].uv = (0.5 + r * math.cos(ph), 0.5 + r * math.sin(ph))
    rnd = np.random.default_rng(3)
    yy, xx = np.mgrid[0:size, 0:size] / size - 0.5
    rr = np.sqrt(xx * xx + yy * yy) * 2 * math.pi       # angle from the eye's axis (rad)
    ang = np.arctan2(yy, xx)
    img = np.zeros((size, size, 3), np.float32)
    sclera = np.array([0.86, 0.82, 0.78]) - 0.08 * np.clip((rr - 0.9) / 1.2, 0, 1)[..., None] * np.array([0.0, 0.2, 0.2])
    img[:] = sclera
    streak = 0.75 + 0.25 * np.sin(ang * 60 + rnd.normal(0, 1, (size, size)) * 0.6)
    ir = np.array(iris)[None, None, :] * (0.7 + 0.5 * streak[..., None]) * (0.75 + 0.5 * (rr[..., None] / 0.5))
    m_iris = rr < 0.5
    img[m_iris] = ir[m_iris]
    limb = (rr > 0.44) & (rr < 0.53)
    img[limb] *= 0.45
    img[rr < 0.17] = (0.02, 0.02, 0.02)
    im = _image("TR_%s_eyes" % sex, size)
    rgba = np.concatenate([img, np.ones((size, size, 1), np.float32)], axis=2)[::-1].reshape(-1)
    im.pixels.foreach_set(rgba.astype(np.float32))
    im.filepath_raw = os.path.join(TMP, im.name + ".png"); im.file_format = "PNG"; im.save()
    em = bpy.data.materials.new("TR_Eye"); em.use_nodes = True
    fn = em.node_tree.nodes; fp = next(n for n in fn if n.type == "BSDF_PRINCIPLED")
    t = fn.new("ShaderNodeTexImage"); t.image = im
    em.node_tree.links.new(t.outputs["Color"], fp.inputs["Base Color"]); fp.inputs["Roughness"].default_value = 0.12
    me.materials.clear(); me.materials.append(em)


# ------------------------------------------------------------------ hair
HAIR_STYLES = {
    # length (m) at the crown and at the hairline, cards, card width, gravity, comb back, how long it hugs the head
    "short": dict(L=0.07, Lfront=0.05, n=900, w=0.016, grav=0.25, back=0.55, hug=1.0),
    "buzzed": dict(L=0.016, Lfront=0.012, n=900, w=0.012, grav=0.05, back=0.4, hug=1.0),
    "swept": dict(L=0.13, Lfront=0.11, n=520, w=0.026, grav=0.35, back=0.75, hug=0.75),
    "long": dict(L=0.42, Lfront=0.24, n=460, w=0.03, grav=1.0, back=0.55, hug=0.4),
    "flowing": dict(L=0.72, Lfront=0.36, n=560, w=0.034, grav=1.0, back=0.6, hug=0.35),   # Scarletta
}


def _hair_texture(size=512):
    """Strand atlas (white-ish, tinted in game by the hair colour): dense at the root (top), thinning tips."""
    import numpy as np
    W, H = size, size * 2
    rnd = np.random.default_rng(11)
    col = np.zeros((H, W), np.float32); alpha = np.zeros((H, W), np.float32)
    v = np.linspace(0, 1, H)[:, None]
    for _ in range(1400):
        x = rnd.uniform(0, W); end = rnd.uniform(0.5, 1.0); wv = rnd.uniform(0.5, 1.1); bright = rnd.uniform(0.55, 1.0)
        wave = np.sin(v[:, 0] * rnd.uniform(4, 9) + rnd.uniform(0, 6.3)) * rnd.uniform(0.5, 3.0)
        xs = x + wave
        cols = np.arange(W)[None, :]
        d = np.abs(cols - xs[:, None])
        a = np.clip(1 - d / wv, 0, 1) * (v < end) * np.clip((end - v) / 0.08, 0, 1)
        alpha = np.maximum(alpha, a * 0.95)
        col = np.where(a > 0.3, bright, col)
    root = np.clip(1 - v / 0.1, 0, 1) * 0.15
    alpha = np.clip(alpha + root, 0, 1)
    # each quarter of the atlas is one card: feather its sides so card edges dissolve into loose strands
    u = (np.arange(W) % (W // 4)) / (W // 4)
    side = np.clip(np.sin(np.pi * u), 0, 1) ** 0.7
    gaps = (rnd.uniform(0, 1, W) > 0.25).astype(np.float32) * 0.35 + 0.65
    alpha = alpha * (side * gaps)[None, :]
    shade = (0.55 + 0.45 * col) * (0.8 + 0.2 * v)                     # a little darker at the root
    rgba = np.stack([shade * 0.92, shade * 0.88, shade * 0.84, alpha], axis=2)
    im = _image("TR_hair_strands", 4)
    im.scale(W, H)
    im.pixels.foreach_set(rgba[::-1].astype(np.float32).reshape(-1))
    im.alpha_mode = "STRAIGHT"
    im.filepath_raw = os.path.join(TMP, "TR_hair_strands.png"); im.file_format = "PNG"; im.save()
    return im


def build_hair(sex, style, seed=5):
    """Hair cards for the body built last in scene Humans_<sex>; exports assets/models/hair/TR_Hair_<Sex>_<style>.glb."""
    import numpy as np
    S = HAIR_STYLES[style]
    sc = bpy.data.scenes["Humans_" + sex]; bpy.context.window.scene = sc
    body = next(o for o in sc.objects if o.type == "MESH" and "scalp" in o.vertex_groups and o.parent and o.parent.type == "ARMATURE"
                and o.parent.name.startswith("Armature"))   # the exported game skeleton, not the MPFB rig
    arm = body.parent
    me = body.data; mw = body.matrix_world; nmw = mw.to_3x3().inverted().transposed()
    g = body.vertex_groups["scalp"].index
    roots = [(mw @ v.co, (nmw @ v.normal).normalized()) for v in me.vertices if any(x.group == g and x.weight > 0.5 for x in v.groups)]
    pts = np.array([r[0] for r in roots]); nrm = np.array([r[1] for r in roots])
    centre = Vector((pts - nrm * 0.09).mean(axis=0))
    R = float(np.mean(np.linalg.norm(pts - np.array(centre), axis=1)))
    fwd = Vector((0, -1, 0)); up = Vector((0, 0, 1))
    from mathutils.bvhtree import BVHTree
    bmt = bmesh.new(); bmt.from_mesh(me); bmt.transform(mw)
    tree = BVHTree.FromBMesh(bmt); bmt.free()
    rnd = random.Random(seed)
    P = bmesh.new(); uvl = P.loops.layers.uv.verify()
    cards = []
    for k in range(S["n"]):
        i = rnd.randrange(len(roots)); p0, n0 = roots[i]
        # jitter the root across the scalp a little
        p0 = p0 + Vector((rnd.uniform(-0.006, 0.006), rnd.uniform(-0.006, 0.006), rnd.uniform(-0.004, 0.004)))
        front = max(0.0, (-(p0 - centre).normalized().dot(fwd)))          # 1 at the hairline, 0 at the back
        L = (S["L"] * (1 - front) + S["Lfront"] * front) * rnd.uniform(0.8, 1.15)
        layer = rnd.uniform(0.0, 1.0)
        segs = 8 if L > 0.1 else 4
        d = (n0 * 0.35 + (Vector((0, 0, 0)) - fwd) * S["back"] + Vector((rnd.uniform(-0.15, 0.15), 0, 0))).normalized()
        guide = [p0.copy()]
        for s in range(segs):
            t = (s + 1) / segs
            grav = S["grav"] * min(1.0, t / max(0.05, S["hug"]))
            d = (d + (-up) * grav * 0.6 + (-fwd) * S["back"] * 0.15).normalized()
            q = guide[-1] + d * (L / segs)
            # stay on / outside the real head surface, a little further out for the outer layers
            loc, nn, _, dist = tree.find_nearest(q)
            if loc is not None:
                lift = 0.003 + 0.007 * layer
                above = (q - loc).dot(nn)
                if t <= S["hug"] or above < lift:
                    q = loc + nn * lift if t <= S["hug"] else q + nn * (lift - above)
            # long hair falls behind the shoulders, not through the neck
            if q.z < centre.z - 0.08 and q.y < centre.y + 0.05:
                q.y = centre.y + 0.05 + (centre.z - 0.08 - q.z) * 0.15
            guide.append(q)
        cards.append((guide, layer))
    for guide, layer in cards:
        w0 = S["w"] * rnd.uniform(0.8, 1.2)
        u0 = rnd.choice((0.0, 0.25, 0.5, 0.75))
        prev = None
        n = len(guide)
        for j, q in enumerate(guide):
            t = j / (n - 1)
            dirv = (guide[min(j + 1, n - 1)] - guide[max(j - 1, 0)]).normalized()
            radial = (q - centre).normalized()
            side = dirv.cross(radial).normalized() * (w0 * (1 - 0.55 * t) / 2)
            a = P.verts.new(q - side); b = P.verts.new(q + side)
            if prev:
                f = P.faces.new((prev[0], prev[1], b, a))
                for loop, uv in zip(f.loops, ((u0, 1 - (j - 1) / (n - 1)), (u0 + 0.25, 1 - (j - 1) / (n - 1)), (u0 + 0.25, 1 - t), (u0, 1 - t))):
                    loop[uvl].uv = uv
            prev = (a, b)
    # a cap over the scalp so no skin shows between the cards
    bm = bmesh.new(); bm.from_mesh(me); dl = bm.verts.layers.deform.verify()
    keep = {v for v in bm.verts if g in v[dl] and v[dl][g] > 0.3}
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if v not in keep], context="VERTS")
    for v in bm.verts:
        v.co = v.co + v.normal * 0.0025
    capm = bpy.data.meshes.new("cap"); bm.to_mesh(capm); bm.free()
    capuv = capm.uv_layers[0].data if capm.uv_layers else None
    hm = bpy.data.meshes.new("Hair"); P.to_mesh(hm); P.free()
    hair = bpy.data.objects.new("Hair", hm); sc.collection.objects.link(hair)
    cap = bpy.data.objects.new("HairCap", capm); sc.collection.objects.link(cap); cap.matrix_world = mw.copy()
    if capm.uv_layers:
        for d in capm.uv_layers[0].data:
            d.uv = (0.1 + 0.8 * (d.uv.x % 1.0), 0.97)          # the dense root rows of the atlas
    for o in (hair, cap):
        if not o.data.uv_layers:
            o.data.uv_layers.new(name="UVMap")
        o.data.uv_layers[0].name = "UVMap"
    bpy.ops.object.select_all(action="DESELECT"); hair.select_set(True); cap.select_set(True); bpy.context.view_layer.objects.active = hair
    bpy.ops.object.join()
    for vg in list(hair.vertex_groups):
        hair.vertex_groups.remove(vg)
    vg = hair.vertex_groups.new(name="Head"); vg.add(list(range(len(hair.data.vertices))), 1.0, "REPLACE")
    im = bpy.data.images.get("TR_hair_strands") or _hair_texture()
    m = bpy.data.materials.get("TR_Hair") or bpy.data.materials.new("TR_Hair")
    m.use_nodes = True; nt = m.node_tree; bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    t = next((n for n in nt.nodes if n.type == "TEX_IMAGE"), None) or nt.nodes.new("ShaderNodeTexImage"); t.image = im
    nt.links.new(t.outputs["Color"], bsdf.inputs["Base Color"]); nt.links.new(t.outputs["Alpha"], bsdf.inputs["Alpha"])
    bsdf.inputs["Roughness"].default_value = 0.45
    hair.data.materials.clear(); hair.data.materials.append(m)
    for p in hair.data.polygons:
        p.use_smooth = True
    hair.parent = arm; hair.matrix_parent_inverse = arm.matrix_world.inverted()
    am = hair.modifiers.new("Armature", "ARMATURE"); am.object = arm
    os.makedirs(os.path.join(ROOT, "assets", "models", "hair"), exist_ok=True)
    path = os.path.join(ROOT, "assets", "models", "hair", "TR_Hair_%s_%s.glb" % (sex.capitalize(), style))
    _export_objs(path, [arm, hair])
    print("[humans] hair", sex, style, len(hair.data.polygons), "faces ->", path)
    bpy.data.objects.remove(hair)
    return path


# ------------------------------------------------------------------ outfits (shells lifted off the body)
REG = {
    "torso": {"spine_01", "spine_02", "spine_03", "clavicle_l", "clavicle_r", "pelvis"},
    "neck": {"neck_01"}, "head": {"Head"},
    "uarm": {"upperarm_l", "upperarm_r"}, "farm": {"lowerarm_l", "lowerarm_r"},
    "hand": {"hand_l", "hand_r"} | {f"{f}_0{i}_{s}" for f in ("index", "middle", "ring", "pinky", "thumb") for i in (1, 2, 3) for s in "lr"},
    "thigh": {"thigh_l", "thigh_r"}, "calf": {"calf_l", "calf_r"}, "foot": {"foot_l", "foot_r", "ball_l", "ball_r"},
}
G_LINEN, G_WOOL = "G_Cloth_Linen", "G_Cloth_Wool"


def _regions(body):
    """Per vertex: dominant region name, world position and normal."""
    me = body.data; mw = body.matrix_world; nmw = mw.to_3x3().inverted().transposed()
    gname = {vg.index: vg.name for vg in body.vertex_groups}
    out = []
    for v in me.vertices:
        acc = {}
        for x in v.groups:
            for r, bones in REG.items():
                if gname.get(x.group) in bones:
                    acc[r] = acc.get(r, 0.0) + x.weight
        dom = max(acc, key=acc.get) if acc else ""
        out.append((dom, mw @ v.co, (nmw @ v.normal).normalized()))
    return out


def _landmarks(arm):
    b = arm.data.bones; mw = arm.matrix_world
    z = lambda n: (mw @ b[n].head_local).z
    return dict(pelvis=z("pelvis"), neck=z("neck_01"), head=z("Head"), knee=z("calf_l"), ankle=z("foot_l"),
                shoulder=z("upperarm_l"), elbow=z("lowerarm_l"), wrist=z("hand_l"), chest=z("spine_03"))


def _shell(body, keep, offset, thick, mat, name, extra=None, smooth=None):
    """Copy of the body's faces whose vertices all pass keep(i), pushed out along the normals, made solid.
    The copy keeps the body's skin weights and UVs, so it deforms and maps exactly like the skin under it.
    smooth = (predicate on world position, iterations): rounds that part off first (boots lose the toes).
    The cut edges are relaxed so hems and collars do not zig-zag along the body's triangles."""
    o = body.copy(); o.data = body.data.copy(); o.name = name; o.data.name = name
    bpy.context.scene.collection.objects.link(o)
    for m in list(o.modifiers):
        if m.type != "ARMATURE":
            o.modifiers.remove(m)
    bm = bmesh.new(); bm.from_mesh(o.data)
    bm.verts.ensure_lookup_table()
    dead = [f for f in bm.faces if not all(keep(v.index) for v in f.verts)]
    bmesh.ops.delete(bm, geom=dead, context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    mw = body.matrix_world
    if smooth:
        pred, its = smooth
        sv = [v for v in bm.verts if pred(mw @ v.co)]
        for _ in range(its):
            bmesh.ops.smooth_vert(bm, verts=sv, factor=0.6, use_axis_x=True, use_axis_y=True, use_axis_z=True)
        bm.normal_update()
    edge = [v for v in bm.verts if v.is_boundary]
    for _ in range(4):
        bmesh.ops.smooth_vert(bm, verts=edge, factor=0.5, use_axis_x=True, use_axis_y=True, use_axis_z=True)
    bm.normal_update()
    for v in bm.verts:
        v.co = v.co + v.normal * (offset + (extra(v) if extra else 0.0))
    bm.to_mesh(o.data); bm.free()
    for c in [c for c in o.data.color_attributes]:
        o.data.color_attributes.remove(c)
    sol = o.modifiers.new("Solid", "SOLIDIFY"); sol.thickness = thick; sol.offset = 1.0; sol.use_rim = True
    o.modifiers.move(len(o.modifiers) - 1, 0)
    bpy.ops.object.select_all(action="DESELECT"); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.modifier_apply(modifier="Solid")
    o.data.materials.clear()
    for mname in (mat if isinstance(mat, (list, tuple)) else [mat]):
        m = bpy.data.materials.get(mname) or bpy.data.materials.new(mname)
        o.data.materials.append(m)
    for p in o.data.polygons:
        p.use_smooth = True
    return o


GARMENTS = {}


def garment(fn):
    GARMENTS[fn.__name__] = fn
    return fn


@garment
def Tunic(body, R, lm, sex):
    return _shell(body, lambda i: R[i][0] in ("torso", "uarm") and R[i][1].z < lm["neck"] + 0.01
                  or (R[i][0] == "thigh" and R[i][1].z > lm["pelvis"] - 0.2), 0.008, 0.004, G_LINEN, "Tunic")


def _skirt(body, lm, mat, top_z, bottom_z, flare=0.55, folds=14, name="Skirt"):
    """A free-hanging skirt: a flared, folded cone from top_z to bottom_z around the hips, weighted from the pelvis at the
    waist to the thighs lower down (so the legs swing it). Cylindrical UVs."""
    import numpy as np
    mw = body.matrix_world
    pts = np.array([tuple(mw @ v.co) for v in body.data.vertices])
    band = pts[(pts[:, 2] > lm["pelvis"] - 0.12) & (pts[:, 2] < lm["pelvis"] + 0.02)]
    cx, cy = float(band[:, 0].mean()), float(band[:, 1].mean())
    rx = float(np.abs(band[:, 0] - cx).max()) + 0.045; ry = float(np.abs(band[:, 1] - cy).max()) + 0.05
    P = bmesh.new(); uvl = P.loops.layers.uv.verify(); dl = P.verts.layers.deform.verify()
    o = bpy.data.objects.new(name, bpy.data.meshes.new(name)); bpy.context.scene.collection.objects.link(o)
    gi = {}
    for bn in ("pelvis", "thigh_l", "thigh_r", "calf_l", "calf_r"):
        gi[bn] = o.vertex_groups.new(name=bn).index
    N, rows = 56, 18
    grid = []
    for j in range(rows + 1):
        t = j / rows
        z = top_z + (bottom_z - top_z) * t
        k = 1 + flare * t ** 1.3
        row = []
        for i in range(N):
            a = 2 * math.pi * i / N
            fold = 1 + 0.06 * t * math.sin(a * folds + 1.3 * math.sin(a * 3))
            x = cx + rx * k * fold * math.cos(a); y = cy + ry * k * fold * math.sin(a)
            v = P.verts.new(mw.inverted() @ Vector((x, y, z)))
            s = min(1.0, max(0.0, 0.5 + (x - cx) / 0.16))
            leg = min(1.0, t * 1.4) * 0.85
            v[dl][gi["pelvis"]] = 1 - leg
            if leg > 0:
                v[dl][gi["thigh_l"]] = leg * s * (1 - 0.3 * t); v[dl][gi["thigh_r"]] = leg * (1 - s) * (1 - 0.3 * t)
                if t > 0.6:
                    v[dl][gi["calf_l"]] = leg * s * 0.3 * t; v[dl][gi["calf_r"]] = leg * (1 - s) * 0.3 * t
            row.append(v)
        grid.append(row)
    for j in range(rows):
        for i in range(N):
            i2 = (i + 1) % N
            f = P.faces.new((grid[j][i], grid[j][i2], grid[j + 1][i2], grid[j + 1][i]))
            for loop, uv in zip(f.loops, ((i / N, 1 - j / rows), ((i + 1) / N, 1 - j / rows), ((i + 1) / N, 1 - (j + 1) / rows), (i / N, 1 - (j + 1) / rows))):
                loop[uvl].uv = (uv[0] * 3, uv[1])
    bmesh.ops.recalc_face_normals(P, faces=P.faces)
    P.to_mesh(o.data); P.free()
    o.data.uv_layers[0].name = "UVMap"
    o.matrix_world = mw.copy()
    sol = o.modifiers.new("Solid", "SOLIDIFY"); sol.thickness = 0.005; sol.offset = 1.0
    bpy.ops.object.select_all(action="DESELECT"); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.modifier_apply(modifier="Solid")
    am = o.modifiers.new("Armature", "ARMATURE"); am.object = body.parent
    o.parent = body.parent; o.matrix_parent_inverse = body.parent.matrix_world.inverted()
    o.data.materials.append(bpy.data.materials.get(mat) or bpy.data.materials.new(mat))
    for p in o.data.polygons:
        p.use_smooth = True
    return o


@garment
def Robe(body, R, lm, sex):
    top = _shell(body, lambda i: R[i][0] in ("torso", "uarm", "farm") and R[i][1].z < lm["neck"] + 0.01
                 or (R[i][0] == "thigh" and R[i][1].z > lm["pelvis"] - 0.08), 0.012, 0.004, G_WOOL, "RobeTop")
    skirt = _skirt(body, lm, G_WOOL, lm["pelvis"] + 0.02, lm["ankle"] + 0.04, name="RobeSkirt")
    # the same cloth on the legs under the skirt: a knee pushing through in a wide stance shows fabric, not skin
    legs = _shell(body, lambda i: R[i][0] in ("thigh", "calf") and R[i][1].z > lm["ankle"] + 0.05, 0.007, 0.003, G_WOOL, "RobeLegs")
    return _join([top, skirt, legs], "Robe")


@garment
def Gambeson(body, R, lm, sex):
    return _shell(body, lambda i: R[i][0] in ("torso", "uarm", "farm") and R[i][1].z < lm["neck"] + 0.015
                  or (R[i][0] == "thigh" and R[i][1].z > lm["pelvis"] - 0.24), 0.014, 0.006, G_WOOL, "Gambeson")


@garment
def Cuirass(body, R, lm, sex):
    plate = _shell(body, lambda i: R[i][0] == "torso" and lm["pelvis"] + 0.06 < R[i][1].z < lm["shoulder"] - 0.01,
                   0.032, 0.006, "G_Metal_Iron", "Cuirass")
    belt = _shell(body, lambda i: R[i][0] in ("torso", "thigh") and lm["pelvis"] - 0.01 < R[i][1].z < lm["pelvis"] + 0.07,
                  0.036, 0.008, "G_Leather", "Belt")
    return _join([plate, belt], "Cuirass")


@garment
def Pauldrons(body, R, lm, sex):
    return _shell(body, lambda i: R[i][0] in ("uarm", "torso") and R[i][1].z > lm["shoulder"] - 0.07 and abs(R[i][1].x) > 0.13,
                  0.04, 0.006, "G_Metal_Iron", "Pauldrons")


@garment
def Trousers(body, R, lm, sex):
    return _shell(body, lambda i: R[i][0] in ("thigh", "calf") and R[i][1].z > lm["ankle"] + 0.05
                  or (R[i][0] == "torso" and R[i][1].z < lm["pelvis"] + 0.05), 0.006, 0.004, G_WOOL, "Trousers")


@garment
def Boots(body, R, lm, sex):
    sole = lambda v: 0.012 if v.normal.z < -0.6 else 0.0
    return _shell(body, lambda i: R[i][0] == "foot" or (R[i][0] == "calf" and R[i][1].z < lm["knee"] - 0.1),
                  0.016, 0.005, "G_Leather", "Boots", extra=sole, smooth=(lambda p: p.z < lm["ankle"] + 0.02, 40))


@garment
def Gloves(body, R, lm, sex):
    return _shell(body, lambda i: R[i][0] == "hand" or (R[i][0] == "farm" and _near_wrist(R[i][1], lm)),
                  0.004, 0.003, "G_Leather", "Gloves")


def _near_wrist(p, lm):
    return abs(abs(p.x) - lm.get("wrist_x", 0.0)) < 0.09


@garment
def Hood(body, R, lm, sex):
    c = Vector(FACE["centre"])
    face = lambda p: p.y < c.y + 0.03 and lm["head"] - 0.06 < p.z < c.z + 0.05 and abs(p.x) < 0.075
    return _shell(body, lambda i: (R[i][0] in ("head", "neck") and not face(R[i][1]))
                  or (R[i][0] in ("torso", "uarm") and R[i][1].z > lm["shoulder"] - 0.05), 0.022, 0.004, G_WOOL, "Hood")


def _join(objs, name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join(); o = bpy.context.active_object; o.name = name; o.data.name = name
    return o


def build_outfits(sex, names=None):
    """Writes assets/models/outfit/TR_<Sex>_<Garment>.glb for the body in scene Humans_<sex>."""
    sc = bpy.data.scenes["Humans_" + sex]; bpy.context.window.scene = sc
    body = next(o for o in sc.objects if o.type == "MESH" and "scalp" in o.vertex_groups and o.parent and o.parent.name.startswith("Armature"))
    arm = body.parent
    eyes = next(o for o in sc.objects if o.parent == arm and o.type == "MESH" and o.name.startswith("Eyes"))
    _brow_attr(body, eyes, sex)
    R = _regions(body); lm = _landmarks(arm)
    lm["wrist_x"] = abs((arm.matrix_world @ arm.data.bones["hand_l"].head_local).x)
    out = []
    for n in names or list(GARMENTS):
        o = GARMENTS[n](body, R, lm, sex)
        path = os.path.join(ROOT, "assets", "models", "outfit", "TR_%s_%s.glb" % (sex.capitalize(), n))
        _export_objs(path, [arm, o])
        out.append("%s %d faces" % (n, len(o.data.polygons)))
        bpy.data.objects.remove(o)
    print("[humans] outfits", sex, out)
    return out


def _claim_armature_name(objs):
    """The clips address 'Armature/Skeleton3D:<bone>', so the exported armature must be called exactly Armature
    (Blender names are global: another scene's rig may hold the name)."""
    for o in objs:
        if o.type == "ARMATURE" and o.name != "Armature":
            other = bpy.data.objects.get("Armature")
            if other:
                other.name = "Armature_other"
            o.name = "Armature"


def _export_objs(path, objs):
    _claim_armature_name(objs)
    for s in bpy.data.scenes:
        for vl in s.view_layers:
            for o in s.objects:
                o.select_set(False, view_layer=vl)
    for o in objs:
        o.hide_set(False); o.select_set(True)
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, use_active_scene=True, export_yup=True,
                              export_apply=False, export_skins=True, export_animations=False, export_morph=False,
                              export_def_bones=False, export_tangents=True, export_image_format="AUTO")


def export(sex, objs):
    path = os.path.join(OUT, "TR_%s_Body.glb" % sex.capitalize())
    _claim_armature_name(objs)
    for s in bpy.data.scenes:   # the exporter also looks at selections in other scenes
        for vl in s.view_layers:
            for o in s.objects:
                o.select_set(False, view_layer=vl)
    for o in objs:
        o.hide_set(False); o.select_set(True)
        if o.type == "ARMATURE":
            for pb in o.pose.bones:
                pb.custom_shape = None
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, use_active_scene=True, export_yup=True, export_apply=False,
                              export_skins=True, export_animations=False, export_morph=False, export_def_bones=False,
                              export_tangents=True, export_image_format="AUTO")
    return path


def build_body(sex="male", bake=True):
    _scene("Humans_" + sex)
    os.makedirs(TMP, exist_ok=True)
    game = _game_rig(sex)
    h, rig = _mpfb_human(sex)
    _pose_match(rig, game)
    new = _skeleton(rig, game)
    body, eyes_o = _body_mesh(h, new)
    if bake:
        _brow_attr(body, eyes_o, sex)
        _bake_skin(body, sex)
        _eyes(eyes_o, sex)
    for o in (h, rig, game):
        o.hide_set(True)
    path = export(sex, [new, body, eyes_o])
    print("[humans]", sex, "->", path, len(body.data.vertices), "verts")
    return new, body, eyes_o
