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
def _brow_attr(body, eyes_o, sex):
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
                thick = 0.0042 * (1.15 - 0.6 * t)
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
    attr = N.new("ShaderNodeVertexColor"); attr.layer_name = "Paint"
    sep = N.new("ShaderNodeSeparateColor"); L.new(attr.outputs["Color"], sep.inputs["Color"])
    c = mix(c, (0.9, 0.66, 0.64), mul(sep.outputs["Blue"], 0.55))          # cheeks
    c = mix(c, (0.9, 0.78, 0.74), mul(mask("mpfb_ears.jpg"), 0.5))
    c = mix(c, (0.78, 0.5, 0.5), mask("mpfb_lips.jpg"))
    c = mix(c, (0.8, 0.66, 0.64), mul(mask("mpfb_eyelids.jpg"), 0.6))
    c = mix(c, (0.98, 0.92, 0.88), mask("mpfb_fingernails.jpg"))
    c = mix(c, (0.98, 0.92, 0.88), mask("mpfb_toenails.jpg"))
    c = mix(c, (0.72, 0.52, 0.48), mask("mpfb_aureolae.jpg"))
    c = mix(c, (0.62, 0.55, 0.52), mul(sep.outputs["Green"], 0.7))        # stubble shadow
    c = mix(c, (0.2, 0.15, 0.13), mul(sep.outputs["Red"], 0.92))          # brows
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


def export(sex, objs):
    path = os.path.join(OUT, "TR_%s_Body.glb" % sex.capitalize())
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
