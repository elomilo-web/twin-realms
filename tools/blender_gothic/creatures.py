"""Twin Realms gothic creatures: rigged, animated monster models -> assets/models/monsters/<name>.glb.

A creature is a set of bone chains (game coordinates: +Y up, facing +Z, feet on y = 0). From it we build
  - an armature (bone names as given),
  - an organic body: Blender's Skin modifier grown over the bone graph, subdivided, roughened, UV-unwrapped,
    skinned with automatic weights,
  - rigid extras (eyes, teeth, claws, horns, rocks) bound 100 % to their bone,
  - baked ambient occlusion (dirt) in vertex colours,
  - clips the game looks for by name (Enemy.BuildModel): Idle, Run, Attack, HitReact, Death.
Surfaces use the shared gothic materials (G_Fur, G_Scales, G_Leather, G_Rock, G_Moss ...), swapped in by Enemy.cs.

Run inside Blender: exec(open(r"<repo>/tools/blender_gothic/creatures.py").read()); build_creature("giantRat")
"""
import bpy, bmesh, math, os, random, importlib, sys
from mathutils import Vector, Matrix, Euler

HERE = os.path.join(os.path.expanduser("~"), "Documents", "GitHub", "twin-realms", "tools", "blender_gothic")
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import gothic  # noqa: E402
importlib.reload(gothic)
from gothic import G, material, ball, spike, lathe, tube, place, Part, _ao_to_vertex_colors  # noqa: E402

OUT = os.path.join(gothic.ROOT, "assets", "models", "monsters")
CREATURES = {}


def creature(fn):
    CREATURES[fn.__name__] = fn
    return fn


class Body:
    def __init__(self, mat="G_Leather"):
        self.bones = {}; self.order = []; self.extras = []; self.mat = mat; self.rough = 0.012; self.kind = "quad"
        self.skin = True; self.meshes = []; self.wide = 1.0; self.subdiv = 2   # skin radius across (x) vs up/down; smoothing

    def bone(self, name, head, tail, parent=None, r0=0.05, r1=None, skin=True):
        self.bones[name] = dict(head=Vector(head), tail=Vector(tail), parent=parent, r0=r0, r1=r0 if r1 is None else r1, skin=skin)
        self.order.append(name)
        return name

    def chain(self, prefix, pts, radii, parent=None, skin=True):
        names = []
        for i in range(len(pts) - 1):
            n = f"{prefix}{i + 1}" if len(pts) > 2 else prefix
            self.bone(n, pts[i], pts[i + 1], parent, radii[i], radii[i + 1], skin)
            parent = n; names.append(n)
        return names

    def extra(self, obj, bone):
        self.extras.append((obj, bone))


# ------------------------------------------------------------------ build
def _scene(name):
    sc = bpy.data.scenes.get(name) or bpy.data.scenes.new(name)
    bpy.context.window.scene = sc
    for o in list(sc.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    return sc


def _armature(B, name):
    ad = bpy.data.armatures.new(name); arm = bpy.data.objects.new(name, ad)
    bpy.context.scene.collection.objects.link(arm)
    bpy.ops.object.select_all(action="DESELECT"); bpy.context.view_layer.objects.active = arm; arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    for n in B.order:
        b = B.bones[n]; e = ad.edit_bones.new(n)
        e.head = G(*b["head"]); e.tail = G(*b["tail"])
        if (e.tail - e.head).length < 1e-4:
            e.tail = e.head + Vector((0, 0, 0.02))
        e.roll = 0.0
    for n in B.order:
        if B.bones[n]["parent"]:
            ad.edit_bones[n].parent = ad.edit_bones[B.bones[n]["parent"]]
    bpy.ops.object.mode_set(mode="OBJECT")
    return arm


def _skin_body(B, name, subdiv=None):
    """Skin modifier over the bone graph -> one connected organic mesh."""
    subdiv = subdiv or B.subdiv
    pos = []; rad = []

    def vid(p, r):
        for i, q in enumerate(pos):
            if (q - p).length < 1e-4:
                rad[i] = max(rad[i], r); return i
        pos.append(p.copy()); rad.append(r); return len(pos) - 1
    edges = []
    for n in B.order:
        b = B.bones[n]
        if not b["skin"]:
            continue
        a = vid(b["head"], b["r0"]); c = vid(b["tail"], b["r1"])
        if a != c:
            edges.append((a, c))
    me = bpy.data.meshes.new(name); me.from_pydata([tuple(G(*p)) for p in pos], edges, []); me.update()
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
    sk = o.modifiers.new("Skin", "SKIN"); sk.use_smooth_shade = True; sk.branch_smoothing = 0.6
    # one root per connected piece, or the Skin modifier skips the pieces without one (legs that do not touch the body)
    parent = list(range(len(pos)))

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]; i = parent[i]
        return i
    for a, c in edges:
        parent[find(a)] = find(c)
    roots = {}
    for i in range(len(pos)):
        roots.setdefault(find(i), i)
    rootset = set(roots.values())
    for i, d in enumerate(me.skin_vertices[0].data):
        d.radius = (rad[i] * B.wide, rad[i]); d.use_root = i in rootset
    ss = o.modifiers.new("Sub", "SUBSURF"); ss.levels = subdiv; ss.render_levels = subdiv
    dg = bpy.context.evaluated_depsgraph_get()
    m2 = bpy.data.meshes.new_from_object(o.evaluated_get(dg), depsgraph=dg)
    o.modifiers.clear(); old = o.data; o.data = m2; bpy.data.meshes.remove(old); m2.name = name
    # an uneven, living surface
    bm = bmesh.new(); bm.from_mesh(m2); rnd = random.Random(len(m2.vertices))
    ph = [rnd.uniform(0, 6.3) for _ in range(3)]
    for v in bm.verts:
        c = v.co
        n = math.sin(c.x * 31 + ph[0]) * math.sin(c.y * 27 + ph[1]) * math.sin(c.z * 35 + ph[2])
        v.co += v.normal * n * B.rough
    bm.to_mesh(m2); bm.free()
    for p in m2.polygons:
        p.use_smooth = True
    m2.materials.append(material(B.mat))
    return o


def _uv(o):
    bpy.ops.object.select_all(action="DESELECT"); bpy.context.view_layer.objects.active = o; o.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.02)
    bpy.ops.object.mode_set(mode="OBJECT")
    o.data.uv_layers[0].name = "UVMap"


def _bind(o, arm):
    bpy.ops.object.select_all(action="DESELECT"); o.select_set(True); arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")


def _fix_weights(o, arm):
    """Vertices the heat weighting left unweighted (thin legs, wings) go to their nearest bone, so nothing collapses."""
    bones = [(b.name, arm.matrix_world @ b.head_local, arm.matrix_world @ b.tail_local) for b in arm.data.bones]
    groups = {vg.name: vg for vg in o.vertex_groups}

    def seg_dist(p, a, b):
        ab = b - a; t = max(0.0, min(1.0, (p - a).dot(ab) / max(1e-9, ab.dot(ab)))); return (a + ab * t - p).length
    fixed = 0
    for v in o.data.vertices:
        if sum(g.weight for g in v.groups if o.vertex_groups[g.group].name in groups and g.weight > 0) > 1e-3:
            continue
        p = o.matrix_world @ v.co
        name = min(bones, key=lambda b: seg_dist(p, b[1], b[2]))[0]
        vg = groups.get(name) or o.vertex_groups.new(name=name); groups[name] = vg
        vg.add([v.index], 1.0, "REPLACE"); fixed += 1
    return fixed


def _bind_rigid(o, arm, bone):
    for vg in list(o.vertex_groups):
        o.vertex_groups.remove(vg)
    vg = o.vertex_groups.new(name=bone); vg.add(list(range(len(o.data.vertices))), 1.0, "REPLACE")


def build_body(B, name):
    arm = _armature(B, "Armature")
    parts = []
    if B.skin:
        body = _skin_body(B, name); _uv(body); _bind(body, arm); parts.append(body)
    for o, bone in B.extras:
        if not o.data.uv_layers:
            o.data.uv_layers.new(name="UVMap")
        _bind_rigid(o, arm, bone); parts.append(o)
    for o in B.meshes:
        parts.append(o)
    # one mesh: join everything into the first part
    bpy.ops.object.select_all(action="DESELECT")
    for o in parts:
        o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    if len(parts) > 1:
        bpy.ops.object.join()
    body = bpy.context.active_object; body.name = name; body.data.name = name
    body.parent = arm; body.matrix_parent_inverse = arm.matrix_world.inverted()
    if not any(m.type == "ARMATURE" for m in body.modifiers):
        body.modifiers.new("Armature", "ARMATURE")
    for m in body.modifiers:
        if m.type == "ARMATURE":
            m.object = arm
    n_fix = _fix_weights(body, arm)
    for p in body.data.polygons:
        p.use_smooth = True
    glow = {i for i, m in enumerate(body.data.materials) if m and m.name.startswith(("G_Glow_", "TR_Eye", "TR_Slime"))}
    _ao_to_vertex_colors(body, glow, lo=0.45)
    return arm, body


# ------------------------------------------------------------------ animation
def _clip(arm, name, frames, pose, loop=True):
    """pose(pb, t) sets rotations (degrees, XYZ), locations and scales for t in 0..1; keyed every 2 frames."""
    act = bpy.data.actions.new(name)
    arm.animation_data_create(); arm.animation_data.action = act
    pbs = arm.pose.bones
    for pb in pbs:
        pb.rotation_mode = "XYZ"
    for f in range(0, frames + 1, 2):
        t = f / frames
        for pb in pbs:
            pb.rotation_euler = (0, 0, 0); pb.location = (0, 0, 0); pb.scale = (1, 1, 1)
        pose(pbs, t)
        for pb in pbs:
            pb.keyframe_insert("rotation_euler", frame=f); pb.keyframe_insert("location", frame=f); pb.keyframe_insert("scale", frame=f)
    tr = arm.animation_data.nla_tracks.new(); tr.name = name
    tr.strips.new(name, 1, act)
    arm.animation_data.action = None
    return act


def R(pb, x=0.0, y=0.0, z=0.0):
    e = pb.rotation_euler; pb.rotation_euler = (e[0] + math.radians(x), e[1] + math.radians(y), e[2] + math.radians(z))


def L(pb, x=0.0, y=0.0, z=0.0):
    """Move in game space (pose-bone location is in the bone's own space; for our root bones this is close enough)."""
    l = pb.location; pb.location = (l[0] + x, l[1] + y, l[2] + z)


S2 = lambda t: math.sin(2 * math.pi * t)


def export(name, arm, body):
    path = os.path.join(OUT, name + ".glb")
    for s in bpy.data.scenes:
        for vl in s.view_layers:
            for o in s.objects:
                o.select_set(False, view_layer=vl)
    arm.select_set(True); body.select_set(True)
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, use_active_scene=True, export_yup=True,
                              export_apply=False, export_skins=True, export_animations=True, export_animation_mode="NLA_TRACKS",
                              export_image_format="NONE", export_vertex_color="ACTIVE", export_all_vertex_colors=False,
                              export_def_bones=False, export_force_sampling=True)
    return path


def build_creature(name):
    _scene("Creature_" + name)          # first: the builders make their extra parts in this scene
    B, anim = CREATURES[name]()
    arm, body = build_body(B, name)
    anim(arm)
    path = export(name, arm, body)
    print("[creatures]", name, len(body.data.polygons), "faces,", len(arm.data.bones), "bones ->", path)
    return path


# ------------------------------------------------------------------ shared pieces
def eyes(B, head_bone, at, sep, r, colour="TR_EyeDark"):
    for s in (1, -1):
        B.extra(ball("eye", (at[0] + s * sep, at[1], at[2]), r, colour, seg=12, rings_=8), head_bone)


def teeth(B, bone, base_y, z, half_w, n, length, r, mat="TR_Bone", up=False):
    for i in range(n):
        x = -half_w + 2 * half_w * i / max(1, n - 1)
        tip = (x, base_y + (length if up else -length), z + 0.004)
        B.extra(spike("tooth", (x, base_y, z), tip, r, mat, sides=5), bone)


def _plain(name, col, rough, metal=0.0, alpha=1.0, emit=None):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True; p = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    p.inputs["Base Color"].default_value = (*col, 1); p.inputs["Roughness"].default_value = rough; p.inputs["Metallic"].default_value = metal
    if alpha < 1:
        p.inputs["Alpha"].default_value = alpha
        try:
            m.surface_render_method = "BLENDED"
        except Exception:
            pass
    if emit:
        p.inputs["Emission Color"].default_value = (*emit, 1); p.inputs["Emission Strength"].default_value = 1.0
    m.diffuse_color = (*col, alpha)
    return m


def _materials():
    _plain("TR_EyeDark", (0.02, 0.015, 0.012), 0.05)
    _plain("TR_Bone", (0.78, 0.72, 0.6), 0.55)
    _plain("TR_Claw", (0.12, 0.1, 0.09), 0.4)
    _plain("TR_Slime", (0.25, 0.42, 0.12), 0.08, alpha=0.72)
    _plain("TR_SlimeCore", (0.08, 0.14, 0.04), 0.3)


_materials()


# ------------------------------------------------------------------ quadruped
def quadruped(B, L_body, H, r_body, r_leg, head_len, neck=0.25, tail=(0.6, 4), leg_spread=None, mat=None):
    """A four-legged beast: hips -> spine -> chest -> neck -> head, a tail, four 3-part legs. Returns the leg names."""
    hz, cz = -L_body / 2, L_body / 2
    hips = Vector((0, H, hz)); chest = Vector((0, H * 1.04, cz))
    B.bone("hips", hips, hips.lerp(chest, 0.5), None, r_body * 0.95, r_body)
    B.bone("chest", hips.lerp(chest, 0.5), chest, "hips", r_body, r_body * 1.05)
    nk = chest + Vector((0, neck * 0.7, neck * 0.7))
    B.bone("neck", chest, nk, "chest", r_body * 0.75, r_body * 0.55)
    hd = nk + Vector((0, -head_len * 0.25, head_len))
    B.bone("head", nk, hd, "neck", r_body * 0.6, r_body * 0.22)
    tl, tn = tail
    if tl > 0:
        pts = [hips + Vector((0, -0.02 * i * tl - 0.04 * i, -tl * i / tn)) for i in range(tn + 1)]
        B.chain("tail", pts, [r_body * 0.4 * (1 - i / (tn + 1)) + 0.008 for i in range(tn + 1)], "hips")
    spread = leg_spread or r_body * 0.8
    legs = []
    for side, s in (("l", 1), ("r", -1)):
        for end, z, par in (("f", cz * 0.9, "chest"), ("b", hz * 0.9, "hips")):
            top = Vector((s * spread, H * 0.8, z)); knee = Vector((s * spread * 1.05, H * 0.42, z + (0.06 if end == "f" else -0.12) * H))
            ankle = Vector((s * spread * 1.05, H * 0.12, z + (-0.02 if end == "f" else 0.08) * H)); toe = Vector((s * spread * 1.05, 0.01, z + 0.07 * H))
            root = (hips if end == "b" else chest)
            n = f"{end}leg_{side}"
            B.bone(n + "0", root, top, par, r_body * 0.75, r_leg * 1.9)
            B.chain(n, [top, knee, ankle, toe], [r_leg * 1.9, r_leg * 1.15, r_leg * 0.8, r_leg * 0.75], n + "0")
            legs.append(n)
    return legs


def quad_anims(arm, legs, stride=32, attack="bite"):
    P = lambda pbs, n: pbs[n] if n in pbs else None
    phase = {"fleg_l": 0.0, "bleg_r": 0.0, "fleg_r": 0.5, "bleg_l": 0.5}

    def idle(p, t):
        R(p["chest"], x=2 * S2(t)); R(p["neck"], x=-3 * S2(t + .2)); R(p["head"], x=3 * S2(t + .4), z=4 * S2(t * .5))
        for i in range(1, 5):
            if f"tail{i}" in p: R(p[f"tail{i}"], z=8 * S2(t + i * .12))

    def run(p, t):
        R(p["hips"], x=3 * S2(t * 2)); R(p["head"], x=-4 * S2(t * 2))
        for lg in legs:
            ph = phase[lg[:6]]; a = S2(t + ph)
            R(p[lg + "1"], x=stride * a); R(p[lg + "2"], x=-28 * max(0.0, math.sin(2 * math.pi * (t + ph) + 1.2)))
            R(p[lg + "3"], x=18 * max(0.0, -a))
        for i in range(1, 5):
            if f"tail{i}" in p: R(p[f"tail{i}"], x=-6, z=10 * S2(t + i * .1))

    def atk(p, t):
        w = min(1.0, t / 0.45); s = max(0.0, min(1.0, (t - 0.45) / 0.12)); r_ = max(0.0, (t - 0.6) / 0.4)
        k = w * (1 - s) - 0.0
        R(p["neck"], x=-25 * k + 40 * s * (1 - r_)); R(p["head"], x=-20 * k + 30 * s * (1 - r_))
        R(p["hips"], x=-6 * k + 4 * s * (1 - r_))
        for lg in legs:
            if lg.startswith("b"): R(p[lg + "1"], x=-15 * k + 20 * s * (1 - r_))

    def hit(p, t):
        k = math.sin(math.pi * t)
        R(p["neck"], x=-25 * k); R(p["head"], x=-20 * k); R(p["chest"], x=-8 * k)

    def death(p, t):
        k = min(1.0, t / 0.6) ** 1.5
        R(p["hips"], y=95 * k); R(p["chest"], y=10 * k); R(p["neck"], x=25 * k); R(p["head"], x=20 * k)
        L(p["hips"], z=-0.25 * k)
        for lg in legs: R(p[lg + "1"], x=20 * k); R(p[lg + "2"], x=-30 * k)

    _clip(arm, "Idle", 48, idle); _clip(arm, "Run", 20, run); _clip(arm, "Attack", 30, atk, False)
    _clip(arm, "HitReact", 14, hit, False); _clip(arm, "Death", 40, death, False)


@creature
def giantRat():
    B = Body("G_Fur"); B.rough = 0.006; B.wide = 1.15
    legs = quadruped(B, 0.58, 0.36, 0.2, 0.05, 0.24, neck=0.1, tail=(0.75, 5))
    B.bones["head"]["r1"] = 0.03
    hd = B.bones["head"]; tip = hd["tail"]
    eyes(B, "head", (0, tip.y + 0.06, tip.z - 0.12), 0.045, 0.018)
    teeth(B, "head", tip.y - 0.01, tip.z - 0.005, 0.008, 2, 0.03, 0.006, "TR_Bone")
    for s in (1, -1):   # ears
        B.extra(lathe("ear", [(0.0, 0.0), (0.04, 0.005), (0.035, 0.03), (0.0, 0.05)], "G_Leather", seg=10, sz=0.35, x=s * 0.07, z=tip.z - 0.2, y0=tip.y + 0.07), "head")
    for lg in legs:   # claws
        toe = B.bones[lg + "3"]["tail"]
        for k in (-1, 0, 1):
            B.extra(spike("claw", (toe.x + k * 0.012, 0.012, toe.z), (toe.x + k * 0.014, 0.0, toe.z + 0.035), 0.005, "TR_Claw", 4), lg + "3")
    return B, lambda arm: quad_anims(arm, legs, 34)


@creature
def giantSpider():
    B = Body("G_Leather"); B.rough = 0.004; B.subdiv = 1
    H = 0.55
    B.bone("body", (0, H, -0.05), (0, H + 0.02, 0.22), None, 0.2, 0.17)
    B.bone("abdomen", (0, H, -0.05), (0, H + 0.12, -0.6), "body", 0.22, 0.12)
    B.bone("head", (0, H + 0.02, 0.22), (0, H - 0.02, 0.36), "body", 0.15, 0.09)
    legs = []
    for side, s in (("l", 1), ("r", -1)):
        for i, a in enumerate((50, 20, -15, -45)):
            ang = math.radians(a)
            base = Vector((s * 0.12, H, 0.1 - i * 0.07))
            outd = Vector((s * math.cos(ang), 0, math.sin(ang)))
            knee = base + outd * 0.45 + Vector((0, 0.38, 0)); mid = base + outd * 0.85 + Vector((0, 0.2, 0)); foot = base + outd * 1.15 + Vector((0, -H + 0.01, 0))
            n = f"leg{i}_{side}"
            B.chain(n, [base, knee, mid, foot], [0.06, 0.05, 0.035, 0.016], "body"); legs.append(n)
    for s in (1, -1):   # fangs
        B.extra(spike("fang", (s * 0.035, H - 0.03, 0.34), (s * 0.02, H - 0.13, 0.36), 0.016, "TR_Claw", 5), "head")
    for k in range(4):   # eye cluster
        for s in (1, -1):
            B.extra(ball("eye", (s * (0.025 + 0.025 * (k % 2)), H + 0.06 + 0.02 * (k // 2), 0.33 - 0.02 * k), 0.014 - 0.002 * k, "TR_EyeDark", seg=10, rings_=6), "head")

    def anims(arm):
        grpA = {"leg0_l", "leg2_l", "leg1_r", "leg3_r"}

        def idle(p, t):
            R(p["abdomen"], x=3 * S2(t)); R(p["body"], x=1.5 * S2(t + .3))
            for lg in legs: R(p[lg + "1"], x=2 * S2(t + hash(lg) % 7 / 7))

        def run(p, t):
            for lg in legs:
                ph = 0 if lg in grpA else 0.5; a = S2(t + ph)
                R(p[lg + "1"], x=-18 * max(0.0, a), z=22 * math.cos(2 * math.pi * (t + ph)) * (1 if lg.endswith("l") else -1))
            R(p["abdomen"], z=4 * S2(t * 2))

        def atk(p, t):
            k = math.sin(math.pi * min(1.0, t / 0.5)); s = max(0.0, math.sin(math.pi * max(0.0, (t - 0.4) / 0.4)))
            R(p["body"], x=-25 * k + 10 * s); R(p["head"], x=-15 * k + 30 * s)
            for lg in ("leg0_l", "leg0_r", "leg1_l", "leg1_r"): R(p[lg + "1"], x=-35 * k)

        def hit(p, t):
            k = math.sin(math.pi * t); R(p["body"], x=-12 * k); R(p["abdomen"], x=10 * k)

        def death(p, t):
            k = min(1.0, t / 0.7)
            L(p["body"], z=-0.35 * k)
            for lg in legs: R(p[lg + "1"], x=-60 * k); R(p[lg + "2"], x=70 * k); R(p[lg + "3"], x=60 * k)
        _clip(arm, "Idle", 48, idle); _clip(arm, "Run", 16, run); _clip(arm, "Attack", 30, atk, False)
        _clip(arm, "HitReact", 14, hit, False); _clip(arm, "Death", 40, death, False)
    return B, anims


def _blob(B, R0, spikes=0, mat="TR_Slime"):
    B.skin = False
    B.bone("root", (0, 0, 0), (0, R0 * 0.8, 0), None, 0.1, 0.1, False)
    B.bone("top", (0, R0 * 0.8, 0), (0, R0 * 1.7, 0), "root", 0.1, 0.1, False)
    o = ball("slime", (0, R0 * 0.85, 0), R0, mat, sy=0.85, seg=28, rings_=18)
    me = o.data; rnd = random.Random(3)
    for v in me.vertices:
        c = v.co; n = math.sin(c.x * 9) * math.sin(c.y * 7 + 1) * math.sin(c.z * 8 + 2)
        v.co = c + c.normalized() * n * R0 * 0.06 if c.length > 0 else c
        if c.z < 0.02:   # flatten where it rests on the ground (Blender z = game y)
            v.co.z = max(v.co.z, 0.0)
    vg_r = o.vertex_groups.new(name="root"); vg_t = o.vertex_groups.new(name="top")
    for v in me.vertices:
        h = max(0.0, min(1.0, v.co.z / (R0 * 1.7)))
        vg_r.add([v.index], 1 - h, "REPLACE"); vg_t.add([v.index], h, "REPLACE")
    am = o.modifiers.new("Armature", "ARMATURE")
    B.meshes.append(o)
    core = ball("core", (0, R0 * 0.75, 0), R0 * 0.38, "TR_SlimeCore", seg=12, rings_=8)
    vg = core.vertex_groups.new(name="root"); vg.add(list(range(len(core.data.vertices))), 1.0, "REPLACE")
    B.meshes.append(core)
    for k in range(spikes):
        yv = 1 - 2 * (k + 0.5) / spikes; rr = math.sqrt(max(0, 1 - yv * yv)); a = k * 2.39996
        d = Vector((rr * math.cos(a), abs(yv) * 0.85 + 0.15, rr * math.sin(a))).normalized()
        c = Vector((0, R0 * 0.85, 0))
        s = spike("spike", tuple(c + d * R0 * 0.9), tuple(c + d * R0 * 1.35), R0 * 0.08, "TR_Claw", 5)
        vg = s.vertex_groups.new(name="top"); vg.add(list(range(len(s.data.vertices))), 1.0, "REPLACE")
        B.meshes.append(s)

    def anims(arm):
        def idle(p, t):
            k = 0.04 * S2(t); p["top"].scale = (1 - k, 1 + k, 1 - k)
        def run(p, t):
            h = abs(math.sin(math.pi * t)); L(p["root"], y=0.25 * R0 * h)
            sq = 0.12 * (1 - h); p["root"].scale = (1 + sq, 1 - sq, 1 + sq)
        def atk(p, t):
            k = math.sin(math.pi * min(1.0, t / 0.6)); p["top"].scale = (1 - 0.2 * k, 1 + 0.45 * k, 1 - 0.2 * k); R(p["top"], x=25 * k)
        def hit(p, t):
            k = math.sin(math.pi * t); p["root"].scale = (1 + 0.15 * k, 1 - 0.2 * k, 1 + 0.15 * k)
        def death(p, t):
            k = min(1.0, t / 0.6); p["root"].scale = (1 + 0.6 * k, max(0.08, 1 - 0.92 * k), 1 + 0.6 * k)
        _clip(arm, "Idle", 40, idle); _clip(arm, "Run", 20, run); _clip(arm, "Attack", 24, atk, False)
        _clip(arm, "HitReact", 12, hit, False); _clip(arm, "Death", 30, death, False)
    return anims


@creature
def slime():
    B = Body(); _plain("TR_Slime", (0.22, 0.05, 0.07), 0.06, alpha=0.6); a = _blob(B, 0.45)
    return B, a


@creature
def toxicBlob():
    B = Body(); _plain("TR_Slime", (0.12, 0.2, 0.05), 0.06, alpha=0.6); a = _blob(B, 0.5)
    return B, a


@creature
def spikeSlime():
    B = Body(); _plain("TR_Slime", (0.1, 0.17, 0.06), 0.06, alpha=0.6); a = _blob(B, 0.5, spikes=18)
    return B, a


@creature
def viper():
    B = Body("G_Scales"); B.rough = 0.002
    N = 14; pts = [Vector((0, 0.07, 0.9 - i * 0.16)) for i in range(N + 1)]
    radii = [0.05 + 0.03 * math.sin(math.pi * min(1, (i + 2) / (N * 0.8))) if i < N * 0.85 else 0.02 for i in range(N + 1)]
    radii[-1] = 0.006
    B.chain("spine", pts[::-1], radii[::-1], None)   # spine1 at the tail end ... spineN behind the head
    nb = f"spine{N}"
    B.bone("head", pts[0], pts[0] + Vector((0, 0.0, 0.16)), nb, 0.06, 0.03)
    eyes(B, "head", (0, 0.105, 0.98), 0.035, 0.012)
    teeth(B, "head", 0.06, 1.04, 0.012, 2, 0.03, 0.005, "TR_Bone")

    def anims(arm):
        sp = [f"spine{i}" for i in range(1, N + 1)]
        def idle(p, t):
            for i, n in enumerate(sp):
                R(p[n], z=4 * S2(t + i * .06))
            for n in sp[-3:]: R(p[n], x=-14)
            R(p["head"], x=10)
        def run(p, t):
            for i, n in enumerate(sp):
                R(p[n], z=22 * S2(t - i * .09))
        def atk(p, t):
            k = math.sin(math.pi * min(1.0, t / 0.5)); s = max(0.0, math.sin(math.pi * max(0.0, (t - 0.35) / 0.35)))
            for n in sp[-4:]: R(p[n], x=-22 * k + 18 * s)
            R(p["head"], x=15 * k - 10 * s)
        def hit(p, t):
            k = math.sin(math.pi * t)
            for n in sp[-4:]: R(p[n], x=-10 * k, z=8 * k)
        def death(p, t):
            k = min(1.0, t / 0.6)
            for i, n in enumerate(sp): R(p[n], y=12 * k, z=10 * S2(i * .1) * (1 - k))
        _clip(arm, "Idle", 48, idle); _clip(arm, "Run", 24, run); _clip(arm, "Attack", 26, atk, False)
        _clip(arm, "HitReact", 12, hit, False); _clip(arm, "Death", 36, death, False)
    return B, anims


def _rock(name, centre, size, seed, mat="G_Rock"):
    o = ball(name, centre, size, mat, sx=1.0, sy=0.85, sz=0.9, seg=10, rings_=7)
    rnd = random.Random(seed); c = G(*centre)
    for v in o.data.vertices:
        d = v.co - c
        v.co = c + d * rnd.uniform(0.82, 1.12)
    bm = bmesh.new(); bm.from_mesh(o.data)
    bmesh.ops.bevel(bm, geom=list(bm.edges), offset=size * 0.06, segments=1, affect="EDGES")
    bm.to_mesh(o.data); bm.free()
    return o


@creature
def earthGolem():
    """A hulking golem of mossed stone: rock slabs on a heavy humanoid frame."""
    B = Body("G_Rock"); B.skin = True; B.rough = 0.02
    H = 1.0
    B.bone("hips", (0, H, 0), (0, H + 0.35, 0.02), None, 0.28, 0.32)
    B.bone("chest", (0, H + 0.35, 0.02), (0, H + 0.8, 0.08), "hips", 0.36, 0.42)
    B.bone("head", (0, H + 0.8, 0.08), (0, H + 1.02, 0.16), "chest", 0.18, 0.14)
    for side, s in (("l", 1), ("r", -1)):
        sh = Vector((s * 0.45, H + 0.72, 0.06))
        B.bone(f"shoulder_{side}", (0, H + 0.72, 0.06), sh, "chest", 0.25, 0.24)
        B.chain(f"arm_{side}", [sh, sh + Vector((s * 0.12, -0.45, 0.02)), sh + Vector((s * 0.14, -0.92, 0.12)), sh + Vector((s * 0.14, -1.12, 0.18))],
                [0.22, 0.2, 0.24, 0.26], f"shoulder_{side}")
        hp = Vector((s * 0.2, H, 0))
        B.bone(f"hip_{side}", (0, H, 0), hp, "hips", 0.24, 0.22)
        B.chain(f"leg_{side}", [hp, hp + Vector((0, -0.5, 0.04)), hp + Vector((0, -0.92, 0.0)), hp + Vector((0, -0.99, 0.12))],
                [0.22, 0.2, 0.2, 0.16], f"hip_{side}")
    rnd = random.Random(7)
    for n in ("chest", "hips", "arm_l1", "arm_r1", "arm_l2", "arm_r2", "leg_l1", "leg_r1", "head"):
        b = B.bones[n]
        for k in range(3 if n in ("chest", "hips") else 2):
            c = b["head"].lerp(b["tail"], rnd.uniform(0.2, 0.8)) + Vector((rnd.uniform(-.12, .12), rnd.uniform(-.05, .08), rnd.uniform(.05, .2)))
            B.extra(_rock("slab", tuple(c), (b["r0"] + b["r1"]) * 0.42, rnd.randrange(999), "G_Rock" if k % 2 == 0 else "G_Moss"), n)
    eyes(B, "head", (0, H + 0.93, 0.27), 0.06, 0.025, "G_Glow_ffb04a")

    def anims(arm):
        def idle(p, t):
            R(p["chest"], x=2 * S2(t)); R(p["head"], z=4 * S2(t * .5))
            for s in "lr": R(p[f"arm_{s}1"], x=3 * S2(t + .2))
        def run(p, t):
            for s, ph in (("l", 0), ("r", .5)):
                a = S2(t + ph); R(p[f"leg_{s}1"], x=26 * a); R(p[f"leg_{s}2"], x=-30 * max(0.0, -a))
                R(p[f"arm_{s}1"], x=-20 * a)
            R(p["hips"], z=4 * S2(t)); R(p["chest"], x=6)
        def atk(p, t):
            w = min(1.0, t / 0.5); s = max(0.0, min(1.0, (t - 0.5) / 0.12)); r_ = max(0.0, (t - 0.65) / 0.35)
            for sd in "lr": R(p[f"arm_{sd}1"], x=-150 * w * (1 - s) + 40 * s * (1 - r_))
            R(p["chest"], x=-12 * w * (1 - s) + 22 * s * (1 - r_))
        def hit(p, t):
            k = math.sin(math.pi * t); R(p["chest"], x=-10 * k); R(p["head"], x=-12 * k)
        def death(p, t):
            k = min(1.0, t / 0.7) ** 1.4
            R(p["hips"], x=80 * k); L(p["hips"], y=-0.6 * k)
            for s in "lr": R(p[f"leg_{s}1"], x=-70 * k); R(p[f"leg_{s}2"], x=90 * k); R(p[f"arm_{s}1"], x=-40 * k)
        _clip(arm, "Idle", 60, idle); _clip(arm, "Run", 30, run); _clip(arm, "Attack", 36, atk, False)
        _clip(arm, "HitReact", 14, hit, False); _clip(arm, "Death", 44, death, False)
    return B, anims


@creature
def goleling():
    B, anims = earthGolem()
    for n, b in B.bones.items():   # a smaller, squatter version
        b["head"] = b["head"] * 0.55; b["tail"] = b["tail"] * 0.55; b["r0"] *= 0.55; b["r1"] *= 0.55
    for o, _ in B.extras:
        o.scale = (0.55, 0.55, 0.55)
        bpy.context.view_layer.update()
        o.data.transform(o.matrix_world); o.matrix_world = Matrix.Identity(4)
    return B, anims


# ------------------------------------------------------------------ more body plans
def _more_materials():
    _plain("TR_Wing", (0.55, 0.5, 0.42), 0.3, alpha=0.35)
    _plain("TR_Membrane", (0.22, 0.14, 0.12), 0.6, alpha=0.92)
    _plain("TR_Cap", (0.3, 0.13, 0.09), 0.6)
    _plain("TR_Gills", (0.75, 0.68, 0.55), 0.8)
    _plain("TR_Ghost", (0.62, 0.66, 0.7), 0.7, alpha=0.45)


_more_materials()


def biped(B, H, r_body, lean=0.35, tail=(0.0, 0), arms=True, neck=0.25, head_len=0.25, r_leg=None):
    """A two-legged body: hips forward-leaning spine, neck, head, digitigrade legs, optional arms and tail."""
    r_leg = r_leg or r_body * 0.4
    hips = Vector((0, H, 0)); chest = Vector((0, H + 0.45 * H * (1 - lean), 0.45 * H * lean))
    B.bone("hips", hips, hips.lerp(chest, 0.5), None, r_body * 0.95, r_body)
    B.bone("chest", hips.lerp(chest, 0.5), chest, "hips", r_body, r_body * 0.95)
    nk = chest + Vector((0, neck * (1 - lean * 0.5), neck * lean))
    B.bone("neck", chest, nk, "chest", r_body * 0.6, r_body * 0.45)
    hd = nk + Vector((0, -head_len * 0.1, head_len))
    B.bone("head", nk, hd, "neck", r_body * 0.5, r_body * 0.25)
    tl, tn = tail
    if tl > 0:
        pts = [hips + Vector((0, -0.03 * i, -tl * i / tn)) for i in range(tn + 1)]
        B.chain("tail", pts, [r_body * 0.75 * (1 - i / (tn + 0.6)) + 0.01 for i in range(tn + 1)], "hips")
    for side, s in (("l", 1), ("r", -1)):
        hp = Vector((s * r_body * 0.6, H, 0))
        B.bone(f"hip_{side}", hips, hp, "hips", r_body * 0.7, r_leg * 1.6)
        B.chain(f"leg_{side}", [hp, hp + Vector((s * 0.02, -H * 0.45, H * 0.12)), hp + Vector((s * 0.02, -H * 0.82, -H * 0.06)), hp + Vector((s * 0.02, -H + 0.01, H * 0.12))],
                [r_leg * 1.6, r_leg, r_leg * 0.7, r_leg * 0.6], f"hip_{side}")
        if arms:
            sh = chest + Vector((s * r_body * 0.9, -0.05, 0))
            B.bone(f"shoulder_{side}", chest, sh, "chest", r_body * 0.5, r_leg)
            B.chain(f"arm_{side}", [sh, sh + Vector((s * 0.05, -H * 0.3, H * 0.08)), sh + Vector((s * 0.06, -H * 0.55, H * 0.15)), sh + Vector((s * 0.06, -H * 0.62, H * 0.2))],
                    [r_leg * 0.9, r_leg * 0.7, r_leg * 0.55, r_leg * 0.4], f"shoulder_{side}")


def biped_anims(arm, stride=30, flying=False):
    names = {pb.name for pb in arm.pose.bones}
    wings = [n for n in names if n.startswith("wing")]

    def flap(p, t, amp=40, speed=1):
        for n in wings:
            s = 1 if "_l" in n else -1
            R(p[n], y=s * amp * S2(t * speed) if n.endswith("1") else s * amp * 0.3 * S2(t * speed + .1), z=s * amp * 0.25 * S2(t * speed + .15))

    def idle(p, t):
        R(p["chest"], x=2 * S2(t)); R(p["head"], x=-3 * S2(t + .3), z=5 * S2(t * .5))
        for i in range(1, 6):
            if f"tail{i}" in p: R(p[f"tail{i}"], z=6 * S2(t + i * .1))
        if wings: flap(p, t, 8 if not flying else 40, 1 if not flying else 3)

    def run(p, t):
        for s, ph in (("l", 0), ("r", .5)):
            a = S2(t + ph); R(p[f"leg_{s}1"], x=stride * a); R(p[f"leg_{s}2"], x=-35 * max(0.0, math.sin(2 * math.pi * (t + ph) + 1.2)))
            if f"arm_{s}1" in p: R(p[f"arm_{s}1"], x=-15 * a)
        R(p["hips"], z=4 * S2(t)); R(p["chest"], x=5)
        for i in range(1, 6):
            if f"tail{i}" in p: R(p[f"tail{i}"], z=8 * S2(t + i * .1))
        if wings: flap(p, t, 30 if flying else 10, 2)

    def atk(p, t):
        w = min(1.0, t / 0.45); s = max(0.0, min(1.0, (t - 0.45) / 0.12)); r_ = max(0.0, (t - 0.6) / 0.4)
        k = w * (1 - s)
        R(p["neck"], x=-25 * k + 35 * s * (1 - r_)); R(p["head"], x=-15 * k + 25 * s * (1 - r_)); R(p["chest"], x=-10 * k + 15 * s * (1 - r_))
        for sd in "lr":
            if f"arm_{sd}1" in p: R(p[f"arm_{sd}1"], x=-90 * k + 40 * s * (1 - r_))
        if wings: flap(p, t, 50, 1)

    def hit(p, t):
        k = math.sin(math.pi * t); R(p["chest"], x=-12 * k); R(p["head"], x=-15 * k)

    def death(p, t):
        k = min(1.0, t / 0.65) ** 1.4
        R(p["hips"], y=85 * k); L(p["hips"], y=-0.3 * k)
        for s in "lr": R(p[f"leg_{s}1"], x=-30 * k); R(p[f"leg_{s}2"], x=40 * k)
        R(p["neck"], x=20 * k)
    _clip(arm, "Idle", 48, idle); _clip(arm, "Run", 22, run); _clip(arm, "Attack", 30, atk, False)
    _clip(arm, "HitReact", 14, hit, False); _clip(arm, "Death", 40, death, False)


def membrane(B, bones, name="membrane", mat="TR_Membrane"):
    """A wing membrane between a chain of wing bones and the body: a fan of quads, each vertex bound to its nearest bone."""
    P = Part(name)
    pts = [B.bones[b]["tail"] for b in bones]
    root = B.bones[bones[0]]["head"]; body = root + Vector((0, -0.25, -0.35))
    rim = [root] + pts
    trail = [body.lerp(p, 0.2) + Vector((0, -0.05 * i, -0.08 * i)) for i, p in enumerate(rim)]
    for i in range(len(rim) - 1):
        vs = [P.v(tuple(rim[i])), P.v(tuple(rim[i + 1])), P.v(tuple(trail[i + 1])), P.v(tuple(trail[i]))]
        P.face(vs, mat, uvs=[(0, 0), (1, 0), (1, 1), (0, 1)])
    o = P.obj()
    bpy.ops.object.select_all(action="DESELECT"); o.select_set(True); bpy.context.view_layer.objects.active = o
    sol = o.modifiers.new("Solid", "SOLIDIFY"); sol.thickness = 0.01; bpy.ops.object.modifier_apply(modifier="Solid")
    groups = {b: o.vertex_groups.new(name=b) for b in bones}
    for v in o.data.vertices:
        p = Vector((v.co.x, v.co.z, -v.co.y))   # back to game space
        best = min(bones, key=lambda b: (B.bones[b]["head"].lerp(B.bones[b]["tail"], 0.5) - p).length)
        groups[best].add([v.index], 1.0, "REPLACE")
    B.meshes.append(o)


def _bat_membrane(B, side, s, pts, tips):
    """Membrane panels: body -> finger 3 -> finger 2 -> finger 1 -> arm; vertices bound to their nearest wing bone."""
    P = Part("membrane"); body_pt = B.bones["hips"]["head"] + Vector((s * 0.2, 0.15, 0.05))
    rim = [pts[1], tips[0], tips[1], tips[2], body_pt]
    centre = pts[2]
    for i in range(len(rim) - 1):
        a, b = rim[i], rim[i + 1]
        sag = (a + b) / 2 + (centre - (a + b) / 2) * 0.18          # scalloped trailing edge
        vs = [P.v(tuple(centre)), P.v(tuple(a)), P.v(tuple(sag)), P.v(tuple(b))]
        P.face(vs, "TR_Membrane", uvs=[(0.5, 0.5), (0, 0), (0.5, 1), (1, 0)])
    o = P.obj()
    bpy.ops.object.select_all(action="DESELECT"); o.select_set(True); bpy.context.view_layer.objects.active = o
    sol = o.modifiers.new("Solid", "SOLIDIFY"); sol.thickness = 0.012; bpy.ops.object.modifier_apply(modifier="Solid")
    names = [f"wing_{side}1", f"wing_{side}2", f"finger0_{side}", f"finger1_{side}", f"finger2_{side}", "hips"]
    groups = {n: o.vertex_groups.new(name=n) for n in names}
    for v in o.data.vertices:
        p = Vector((v.co.x, v.co.z, -v.co.y))
        best = min(names, key=lambda n: (B.bones[n]["head"].lerp(B.bones[n]["tail"], 0.6) - p).length)
        groups[best].add([v.index], 1.0, "REPLACE")
    B.meshes.append(o)


# ------------------------------------------------------------------ insects and crabs
@creature
def giantWasp():
    B = Body("G_Leather"); B.rough = 0.003; B.subdiv = 1
    H = 0.9
    B.bone("body", (0, H, -0.05), (0, H + 0.02, 0.2), None, 0.13, 0.12)
    B.bone("abdomen", (0, H, -0.05), (0, H - 0.12, -0.55), "body", 0.15, 0.04)
    B.bone("head", (0, H + 0.02, 0.2), (0, H, 0.33), "body", 0.1, 0.06)
    B.extra(spike("stinger", (0, H - 0.12, -0.55), (0, H - 0.17, -0.7), 0.012, "TR_Claw", 5), "abdomen")
    legs = []
    for side, s in (("l", 1), ("r", -1)):
        for i, z in enumerate((0.12, 0.04, -0.04)):
            base = Vector((s * 0.06, H - 0.05, z))
            B.chain(f"leg{i}_{side}", [base, base + Vector((s * 0.18, -0.12, 0.04)), base + Vector((s * 0.24, -0.38, 0.0))], [0.018, 0.012, 0.006], "body")
            legs.append(f"leg{i}_{side}")
        for j, (ln, ang) in enumerate(((0.55, 12), (0.4, -10))):
            w0 = Vector((s * 0.06, H + 0.1, 0.08 - j * 0.08)); B.bone(f"wing{j + 1}_{side}", w0, w0 + Vector((s * ln, 0.05, -ln * 0.3)), "body", 0.0, 0.0, skin=False)
            P = Part("wing"); a = w0; b = w0 + Vector((s * ln, 0.05, -ln * 0.3)); c = w0 + Vector((s * ln * 0.7, 0.04, -ln * 0.5)); d = w0 + Vector((0, 0, -0.08))
            vs = [P.v(tuple(x)) for x in (a, b, c, d)]; P.face(vs, "TR_Wing", uvs=[(0, 0), (1, 0), (1, 1), (0, 1)])
            B.extra(P.obj(), f"wing{j + 1}_{side}")
    eyes(B, "head", (0, H + 0.04, 0.3), 0.05, 0.035)

    def anims(arm):
        def wing(p, t, sp):
            for s in "lr":
                sg = 1 if s == "l" else -1
                for j in (1, 2): R(p[f"wing{j}_{s}"], y=sg * 55 * S2(t * sp + j * .1))
        def idle(p, t):
            wing(p, t, 8); L(p["body"], y=0.04 * S2(t)); R(p["abdomen"], x=5 * S2(t * 2))
        def run(p, t):
            wing(p, t, 8); R(p["body"], x=12); R(p["abdomen"], x=-8 * S2(t * 2))
        def atk(p, t):
            k = math.sin(math.pi * min(1.0, t / 0.5)); s = max(0.0, math.sin(math.pi * max(0.0, (t - 0.4) / 0.4)))
            wing(p, t, 6); R(p["abdomen"], x=60 * k - 30 * s); R(p["body"], x=-20 * k + 25 * s)
        def hit(p, t):
            wing(p, t, 4); R(p["body"], x=-20 * math.sin(math.pi * t))
        def death(p, t):
            k = min(1.0, t / 0.6); L(p["body"], y=-0.6 * k); R(p["body"], y=120 * k)
            for lg in legs: R(p[lg + "1"], x=-50 * k)
        _clip(arm, "Flying_Idle", 24, idle); _clip(arm, "Fast_Flying", 16, run); _clip(arm, "Attack", 26, atk, False)
        _clip(arm, "HitReact", 12, hit, False); _clip(arm, "Death", 36, death, False)
    return B, anims


@creature
def giantCrab():
    B = Body("G_Leather"); B.rough = 0.004; B.wide = 1.9; B.subdiv = 1
    H = 0.45
    B.bone("body", (0, H, -0.18), (0, H + 0.04, 0.18), None, 0.22, 0.2)
    legs = []
    for side, s in (("l", 1), ("r", -1)):
        for i in range(4):
            z = 0.06 - i * 0.1; base = Vector((s * 0.25, H, z)); outd = Vector((s, 0, -0.15 * i))
            B.chain(f"leg{i}_{side}", [base, base + outd * 0.28 + Vector((0, 0.2, 0)), base + outd * 0.55 + Vector((0, -H + 0.01, 0))], [0.045, 0.035, 0.015], "body")
            legs.append(f"leg{i}_{side}")
        sh = Vector((s * 0.22, H + 0.02, 0.15))
        B.chain(f"claw_{side}", [sh, sh + Vector((s * 0.22, 0.05, 0.18)), sh + Vector((s * 0.2, 0.12, 0.42))], [0.06, 0.07, 0.09], "body")
        tip = sh + Vector((s * 0.2, 0.12, 0.42))
        B.extra(spike("pincer", tuple(tip + Vector((s * 0.03, 0.03, 0))), tuple(tip + Vector((s * 0.02, 0.04, 0.2))), 0.04, "TR_Claw", 6), f"claw_{side}2")
        B.extra(spike("pincer", tuple(tip + Vector((-s * 0.03, -0.03, 0))), tuple(tip + Vector((-s * 0.01, -0.03, 0.16))), 0.03, "TR_Claw", 6), f"claw_{side}2")
        B.extra(tube("stalk", [(s * 0.06, H + 0.12, 0.17), (s * 0.08, H + 0.22, 0.2)], 0.012, "G_Leather", sides=6), "body")
        B.extra(ball("eye", (s * 0.08, H + 0.24, 0.2), 0.022, "TR_EyeDark", seg=10, rings_=6), "body")

    def anims(arm):
        def idle(p, t):
            for s in "lr": R(p[f"claw_{s}1"], x=-6 * S2(t + (0 if s == "l" else .5))); R(p[f"claw_{s}2"], x=5 * S2(t))
        def run(p, t):   # crabs scuttle sideways
            for lg in legs:
                ph = (int(lg[3]) % 2) * .5 + (0 if lg.endswith("l") else .25)
                R(p[lg + "1"], x=-15 * max(0.0, S2(t + ph)), z=18 * S2(t + ph + .25))
            R(p["body"], z=4 * S2(t * 2))
        def atk(p, t):
            k = math.sin(math.pi * min(1.0, t / 0.5)); s = max(0.0, math.sin(math.pi * max(0.0, (t - 0.4) / 0.4)))
            for sd in "lr": R(p[f"claw_{sd}1"], x=-40 * k + 30 * s); R(p[f"claw_{sd}2"], x=-20 * k + 25 * s)
        def hit(p, t):
            R(p["body"], x=-10 * math.sin(math.pi * t))
        def death(p, t):
            k = min(1.0, t / 0.6); R(p["body"], y=175 * k); L(p["body"], y=0.15 * math.sin(math.pi * k))
            for lg in legs: R(p[lg + "1"], x=-40 * k)
        _clip(arm, "Idle", 40, idle); _clip(arm, "Run", 16, run); _clip(arm, "Attack", 28, atk, False)
        _clip(arm, "HitReact", 12, hit, False); _clip(arm, "Death", 40, death, False)
    return B, anims


# ------------------------------------------------------------------ reptiles
@creature
def raptor():
    B = Body("G_Scales"); B.rough = 0.003
    biped(B, 0.85, 0.2, lean=0.8, tail=(1.2, 6), arms=True, neck=0.35, head_len=0.35, r_leg=0.06)
    hd = B.bones["head"]["tail"]
    eyes(B, "head", (0, hd.y + 0.06, hd.z - 0.16), 0.07, 0.02)
    teeth(B, "head", hd.y - 0.02, hd.z - 0.08, 0.04, 6, 0.03, 0.006)
    for s in "lr":
        toe = B.bones[f"leg_{s}3"]["tail"]
        B.extra(spike("sickle", (toe.x, 0.06, toe.z - 0.02), (toe.x, 0.1, toe.z + 0.08), 0.015, "TR_Claw", 5), f"leg_{s}3")
    return B, lambda arm: biped_anims(arm, 34)


@creature
def wyvern():
    B = Body("G_Scales"); B.rough = 0.005
    biped(B, 1.3, 0.38, lean=0.55, tail=(2.2, 7), arms=False, neck=0.7, head_len=0.55, r_leg=0.11)
    ch = B.bones["chest"]["tail"]
    for side, s in (("l", 1), ("r", -1)):
        w0 = ch + Vector((s * 0.25, 0.05, -0.1))
        pts = [w0, w0 + Vector((s * 0.9, 0.5, -0.2)), w0 + Vector((s * 1.8, 0.2, -0.5)), w0 + Vector((s * 2.6, -0.4, -0.9))]
        B.chain(f"wing_{side}", pts[:3], [0.08, 0.06, 0.05], "chest", skin=False)
        wrist = pts[2]; tips = []
        for f, (dx, dy, dz) in enumerate(((0.9, 0.1, -0.15), (0.75, -0.5, -0.45), (0.35, -0.85, -0.6))):
            tip = wrist + Vector((s * dx, dy, dz)) * 1.1; tips.append(tip)
            B.bone(f"finger{f}_{side}", wrist, tip, f"wing_{side}2", 0.0, 0.0, skin=False)
            B.extra(tube("finger", [tuple(wrist), tuple(tip)], 0.025, "G_Scales", sides=6, radii=[0.03, 0.012]), f"finger{f}_{side}")
        B.extra(tube("arm", [tuple(p) for p in pts[:3]], 0.05, "G_Scales", sides=8, radii=[0.07, 0.05, 0.04]), f"wing_{side}1")
        _bat_membrane(B, side, s, pts, tips)
    hd = B.bones["head"]["tail"]
    eyes(B, "head", (0, hd.y + 0.12, hd.z - 0.25), 0.12, 0.035, "G_Glow_ff9a3a")
    teeth(B, "head", hd.y - 0.04, hd.z - 0.12, 0.07, 8, 0.05, 0.01)
    for s in (1, -1):
        B.extra(spike("horn", (s * 0.08, hd.y + 0.15, hd.z - 0.35), (s * 0.16, hd.y + 0.3, hd.z - 0.7), 0.035, "TR_Bone", 6), "head")

    return B, lambda arm: biped_anims(arm, 26)


@creature
def kitsune():
    B = Body("G_Fur"); B.rough = 0.004; B.wide = 1.05
    legs = quadruped(B, 0.7, 0.55, 0.16, 0.04, 0.3, neck=0.25, tail=(0.0, 0))
    hips = B.bones["hips"]["head"]
    for k, a in enumerate((-35, 0, 35)):   # three great tails
        d = Vector((math.sin(math.radians(a)) * 0.5, 0.5, -1.0)).normalized()
        pts = [hips + d * 0.22 * i + Vector((0, 0.05 * i * i * 0.1, 0)) for i in range(6)]
        B.chain(f"tail{k}_", pts, [0.06, 0.1, 0.12, 0.11, 0.08, 0.02], "hips")
    hd = B.bones["head"]["tail"]
    eyes(B, "head", (0, hd.y + 0.06, hd.z - 0.12), 0.05, 0.016, "G_Glow_ffd25a")
    for s in (1, -1):
        B.extra(lathe("ear", [(0.0, 0.0), (0.05, 0.005), (0.04, 0.06), (0.0, 0.12)], "G_Fur", seg=8, sz=0.4, x=s * 0.06, z=hd.z - 0.2, y0=hd.y + 0.08), "head")
    return B, lambda arm: quad_anims(arm, legs, 36)


# ------------------------------------------------------------------ fungus, thorns, undead
def _fungus(cap_r, H, mat_body="G_Leather"):
    B = Body(mat_body); B.rough = 0.01; B.wide = 1.1
    biped(B, H * 0.45, 0.22 * H, lean=0.1, arms=True, neck=0.08 * H, head_len=0.1 * H, r_leg=0.07 * H)
    hd = B.bones["head"]["tail"]
    ch = cap_r * 0.55   # a domed cap sitting on the head
    cap = lathe("cap", [(0.0, ch), (cap_r * 0.45, ch * 0.92), (cap_r * 0.8, ch * 0.6), (cap_r, ch * 0.15), (cap_r * 1.03, 0.0), (cap_r * 0.92, -0.03 * H), (0.0, -0.02 * H)],
                "TR_Cap", seg=36, y0=hd.y + 0.02 * H, z=hd.z - 0.05 * H)
    B.extra(cap, "head")
    gills = lathe("gills", [(cap_r * 0.9, 0.0), (0.0, 0.04 * H)], "TR_Gills", seg=36, y0=hd.y - 0.008 * H, z=hd.z - 0.05 * H)
    B.extra(gills, "head")
    rnd = random.Random(4)
    for k in range(9):   # pale spots on the cap
        a = rnd.uniform(0, 6.28); rr = rnd.uniform(0.2, 0.8) * cap_r
        B.extra(ball("spot", (math.cos(a) * rr, hd.y + 0.02 * H + ch * (1 - (rr / cap_r) ** 2) + 0.005, hd.z - 0.05 * H + math.sin(a) * rr), 0.025 * H, "TR_Gills", sy=0.35, seg=8, rings_=5), "head")
    eyes(B, "head", (0, hd.y + 0.02, hd.z + 0.02), 0.05 * H, 0.012 * H, "G_Glow_8fffb0")
    return B


@creature
def myconid():
    return _fungus(0.35, 1.4), lambda arm: biped_anims(arm, 24)


@creature
def mushroomKing():
    return _fungus(0.9, 2.6), lambda arm: biped_anims(arm, 22)


@creature
def cactusFiend():
    """Reworked as a thorn fiend: a gaunt bark-skinned creature bristling with thorns."""
    B = Body("G_Bark"); B.rough = 0.02
    biped(B, 0.9, 0.16, lean=0.25, arms=True, neck=0.15, head_len=0.2, r_leg=0.06)
    rnd = random.Random(2)
    for n in ("chest", "hips", "arm_l1", "arm_r1", "arm_l2", "arm_r2", "leg_l1", "leg_r1", "head"):
        b = B.bones[n]
        for k in range(5):
            c = b["head"].lerp(b["tail"], rnd.uniform(0.1, 0.9)); d = Vector((rnd.uniform(-1, 1), rnd.uniform(-.3, 1), rnd.uniform(-1, 1))).normalized()
            B.extra(spike("thorn", tuple(c + d * b["r0"] * 0.8), tuple(c + d * (b["r0"] + 0.1)), 0.012, "TR_Claw", 4), n)
    hd = B.bones["head"]["tail"]
    eyes(B, "head", (0, hd.y + 0.03, hd.z - 0.04), 0.04, 0.014, "G_Glow_ff5a6e")
    return B, lambda arm: biped_anims(arm, 28)


@creature
def flameSkull():
    B = Body(); B.skin = False
    B.bone("skull", (0, 0.5, 0), (0, 0.8, 0), None, 0.1, 0.1, False)
    B.bone("jaw", (0, 0.48, 0.02), (0, 0.45, 0.14), "skull", 0.1, 0.1, False)
    cr = ball("cranium", (0, 0.62, 0), 0.16, "TR_Bone", sx=0.9, sy=1.0, sz=1.1, seg=20, rings_=14); B.extra(cr, "skull")
    face = ball("face", (0, 0.53, 0.07), 0.11, "TR_Bone", sx=0.95, sy=0.8, sz=0.9, seg=16, rings_=10); B.extra(face, "skull")
    jaw = ball("jawbone", (0, 0.45, 0.08), 0.09, "TR_Bone", sx=0.9, sy=0.45, sz=0.9, seg=14, rings_=8); B.extra(jaw, "jaw")
    for s in (1, -1):
        B.extra(ball("socket", (s * 0.055, 0.58, 0.155), 0.035, "G_Glow_ff9a3a", seg=10, rings_=6), "skull")
    teeth(B, "skull", 0.5, 0.17, 0.05, 6, 0.025, 0.007, "TR_Bone")

    def anims(arm):
        def idle(p, t):
            L(p["skull"], y=0.05 * S2(t)); R(p["skull"], z=6 * S2(t * .5)); R(p["jaw"], x=6 * max(0.0, S2(t * 2)))
        def run(p, t):
            R(p["skull"], x=15); L(p["skull"], y=0.04 * S2(t * 2))
        def atk(p, t):
            k = math.sin(math.pi * min(1.0, t / 0.6)); R(p["jaw"], x=40 * k); R(p["skull"], x=-15 * k)
        def hit(p, t):
            R(p["skull"], x=-20 * math.sin(math.pi * t))
        def death(p, t):
            k = min(1.0, t / 0.6); L(p["skull"], y=-0.45 * k); R(p["skull"], x=90 * k); R(p["jaw"], x=30 * k)
        _clip(arm, "Flying_Idle", 40, idle); _clip(arm, "Fast_Flying", 20, run); _clip(arm, "Attack", 24, atk, False)
        _clip(arm, "HitReact", 12, hit, False); _clip(arm, "Death", 32, death, False)
    return B, anims


@creature
def ghost():
    """A wraith: a hooded, tattered shroud with glowing eyes and long thin hands."""
    B = Body(); B.skin = False
    B.bone("root", (0, 0.2, 0), (0, 1.1, 0), None, 0.1, 0.1, False)
    B.bone("head", (0, 1.1, 0), (0, 1.4, 0.05), "root", 0.1, 0.1, False)
    for side, s in (("l", 1), ("r", -1)):
        B.chain(f"arm_{side}", [(s * 0.2, 1.05, 0), (s * 0.35, 0.8, 0.15), (s * 0.4, 0.6, 0.3)], [0.1, 0.1, 0.1], "root", skin=False)
    rnd = random.Random(8)
    prof = [(0.0, 1.45), (0.12, 1.43), (0.19, 1.3), (0.2, 1.12), (0.27, 1.02), (0.3, 0.7), (0.36, 0.35), (0.42, 0.05)]
    shroud = lathe("shroud", prof, "TR_Ghost", seg=36, caps=False)
    for v in shroud.data.vertices:   # tattered hem and folds
        h = v.co.z
        if h < 0.4:
            a = math.atan2(v.co.y, v.co.x); v.co.z += (0.12 * abs(math.sin(a * 7 + rnd.uniform(0, 1)))) * (0.4 - h) / 0.4
        a = math.atan2(v.co.y, v.co.x); f = 1 + 0.06 * math.sin(a * 11) * (1.2 - h)
        v.co.x *= f; v.co.y *= f
    vgr = shroud.vertex_groups.new(name="root"); vgh = shroud.vertex_groups.new(name="head")
    for v in shroud.data.vertices:
        k = max(0.0, min(1.0, (v.co.z - 1.05) / 0.25)); vgr.add([v.index], 1 - k, "REPLACE"); vgh.add([v.index], k, "REPLACE")
    shroud.modifiers.new("Armature", "ARMATURE"); B.meshes.append(shroud)
    hole = ball("hood_dark", (0, 1.27, 0.1), 0.13, "TR_EyeDark", sx=0.9, sy=1.1, sz=0.6, seg=12, rings_=8); B.extra(hole, "head")
    for s in (1, -1):
        B.extra(ball("eye", (s * 0.045, 1.29, 0.19), 0.016, "G_Glow_9af0ff", seg=8, rings_=6), "head")
        for f in range(3):
            B.extra(spike("finger", (s * 0.4, 0.6, 0.3), (s * (0.42 + 0.02 * f), 0.45, 0.36 + 0.03 * f), 0.008, "TR_Bone", 4), f"arm_{'l' if s > 0 else 'r'}2")

    def anims(arm):
        def idle(p, t):
            L(p["root"], y=0.06 * S2(t)); R(p["root"], z=3 * S2(t * .5)); R(p["head"], x=4 * S2(t + .3))
            for s in "lr": R(p[f"arm_{s}1"], x=6 * S2(t + .2))
        def run(p, t):
            R(p["root"], x=18); L(p["root"], y=0.04 * S2(t * 2))
            for s in "lr": R(p[f"arm_{s}1"], x=30)
        def atk(p, t):
            k = math.sin(math.pi * min(1.0, t / 0.6))
            for s in "lr": R(p[f"arm_{s}1"], x=-70 * k)
            R(p["root"], x=15 * k)
        def hit(p, t):
            R(p["root"], x=-15 * math.sin(math.pi * t))
        def death(p, t):
            k = min(1.0, t / 0.7); p["root"].scale = (1 + 0.3 * k, max(0.05, 1 - k), 1 + 0.3 * k)
        _clip(arm, "Flying_Idle", 48, idle); _clip(arm, "Fast_Flying", 24, run); _clip(arm, "Attack", 28, atk, False)
        _clip(arm, "HitReact", 12, hit, False); _clip(arm, "Death", 36, death, False)
    return B, anims
