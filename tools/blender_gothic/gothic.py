"""Twin Realms gothic asset library for Blender (5.x).

Builds realistic, dark-fantasy models from code so every asset shares one look:
  - parts are made in GAME coordinates (metres, +Y up, weapons: grip at the origin, blade up +Y);
  - surfaces use the shared materials G_Metal_Steel, G_Metal_Iron, G_Metal_Rust, G_Metal_Brass, G_Leather,
    G_Wood, G_Cloth, G_Cloth_Black (Godot swaps in assets/materials/gothic/<name>.tres, see scripts/GothicMats.cs)
    and G_Glow_<hex> for emissive accents;
  - finish() bevels, merges, bakes ambient occlusion into vertex colours (dirt in the crevices) and exports a GLB.
Textures are not written into the GLB: the shared materials carry them (triplanar), so models need no UVs.
"""
import bpy, bmesh, math, os, random
from mathutils import Vector, Matrix

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
TEX = os.path.join(ROOT, "assets", "textures", "gothic")

# name: (texture set, tint, metallic, roughness, triplanar repeats per metre)  -- keep in step with assets/materials/gothic
MATS = {
    "G_Metal_Steel": ("steel", (0.72, 0.72, 0.74), 1.0, 0.5, 2.5),
    "G_Metal_Iron": ("iron", (1.0, 0.97, 0.94), 0.8, 0.9, 2.0),
    "G_Metal_Rust": ("rust", (0.55, 0.47, 0.42), 1.0, 1.0, 2.0),
    "G_Metal_Brass": ("brass", (0.72, 0.6, 0.42), 1.0, 0.9, 2.5),
    "G_Leather": ("leather", (0.75, 0.65, 0.6), 0.0, 1.0, 4.0),
    "G_Wood": ("wood", (1.0, 0.92, 0.85), 0.0, 1.0, 1.5),
    "G_Cloth": ("cloth", (0.34, 0.07, 0.06), 0.0, 1.0, 3.0),
    "G_Cloth_Black": ("cloth", (0.1, 0.095, 0.095), 0.0, 1.0, 3.0),
    "G_Parchment": ("cloth", (0.78, 0.7, 0.56), 0.0, 1.0, 5.0),
    "G_Cloth_Blue": ("cloth", (0.07, 0.09, 0.16), 0.0, 1.0, 3.0),
}
STEEL, IRON, RUST, BRASS, LEATHER, WOOD, CLOTH, CLOTH_B, PARCH, CLOTH_BLUE = MATS.keys()


def G(x, y, z):
    """Game coords (Y up, -Z forward) -> Blender coords (Z up). glTF export (+Y up) maps it back exactly."""
    return Vector((x, -z, y))


# ------------------------------------------------------------------ scene / materials
def new_scene(name="GothicBuild"):
    sc = bpy.data.scenes.get(name) or bpy.data.scenes.new(name)
    for o in list(sc.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    bpy.context.window.scene = sc if bpy.context.window else sc
    return sc


def _img(path):
    im = bpy.data.images.get(os.path.basename(path))
    return im or bpy.data.images.load(path, check_existing=True)


def material(name):
    m = bpy.data.materials.get(name)
    if m:
        return m
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    p = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    if name.startswith("G_Glow_"):
        c = tuple(int(name[7:][i:i + 2], 16) / 255 for i in (0, 2, 4))
        p.inputs["Base Color"].default_value = (0.02, 0.02, 0.02, 1)   # purely emissive: the colour comes from the glow
        p.inputs["Emission Color"].default_value = (*c, 1)
        p.inputs["Emission Strength"].default_value = 1.0
        p.inputs["Roughness"].default_value = 0.3
        m.diffuse_color = (*c, 1)
        return m
    tex, tint, metal, rough, rep = MATS[name]
    p.inputs["Metallic"].default_value = metal
    m.diffuse_color = (*tint, 1)
    # preview only (not exported): box-projected textures x tint x vertex colour, like the Godot material
    co = nt.nodes.new("ShaderNodeTexCoord")
    mp = nt.nodes.new("ShaderNodeMapping"); mp.inputs["Scale"].default_value = (rep, rep, rep)
    nt.links.new(co.outputs["Object"], mp.inputs["Vector"])

    def tx(suffix, non_color):
        path = os.path.join(TEX, f"{tex}_{suffix}.jpg")
        if not os.path.exists(path):
            return None
        n = nt.nodes.new("ShaderNodeTexImage"); n.image = _img(path)
        n.projection = "BOX"; n.projection_blend = 0.25
        if non_color:
            n.image.colorspace_settings.name = "Non-Color"
        nt.links.new(mp.outputs["Vector"], n.inputs["Vector"])
        return n
    col = tx("col", False)
    vc = nt.nodes.new("ShaderNodeVertexColor"); vc.layer_name = "Col"
    mul = nt.nodes.new("ShaderNodeMix"); mul.data_type = "RGBA"; mul.blend_type = "MULTIPLY"
    mul.inputs["Factor"].default_value = 1.0
    mul2 = nt.nodes.new("ShaderNodeMix"); mul2.data_type = "RGBA"; mul2.blend_type = "MULTIPLY"
    mul2.inputs["Factor"].default_value = 1.0
    mul2.inputs["B"].default_value = (*tint, 1)
    if col:
        nt.links.new(col.outputs["Color"], mul2.inputs["A"])
    nt.links.new(mul2.outputs["Result"], mul.inputs["A"])
    nt.links.new(vc.outputs["Color"], mul.inputs["B"])
    nt.links.new(mul.outputs["Result"], p.inputs["Base Color"])
    r = tx("rough", True)
    if r:
        mr = nt.nodes.new("ShaderNodeMath"); mr.operation = "MULTIPLY"; mr.inputs[1].default_value = rough
        nt.links.new(r.outputs["Color"], mr.inputs[0]); nt.links.new(mr.outputs[0], p.inputs["Roughness"])
    mt = tx("metal", True)
    if mt:
        nt.links.new(mt.outputs["Color"], p.inputs["Metallic"])
    nr = tx("nrm", True)
    if nr:
        nm = nt.nodes.new("ShaderNodeNormalMap"); nt.links.new(nr.outputs["Color"], nm.inputs["Color"])
        nt.links.new(nm.outputs["Normal"], p.inputs["Normal"])
    return m


def glow(hexcol):
    return "G_Glow_" + hexcol.lower()


# ------------------------------------------------------------------ mesh building
class Part:
    """One mesh being built: verts in game coords, faces with a material name each."""

    def __init__(self, name="part"):
        self.name = name; self.bm = bmesh.new(); self.mats = []; self.bevel = 0.0; self.sharp = 35

    def v(self, p):
        return self.bm.verts.new(G(*p))

    def face(self, verts, mat):
        if mat not in self.mats:
            self.mats.append(mat)
        try:
            f = self.bm.faces.new(verts)
        except ValueError:
            return None
        f.material_index = self.mats.index(mat)
        return f

    def obj(self):
        me = bpy.data.meshes.new(self.name)
        bmesh.ops.remove_doubles(self.bm, verts=self.bm.verts, dist=1e-6)
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)
        self.bm.to_mesh(me); self.bm.free()
        for m in self.mats:
            me.materials.append(material(m))
        o = bpy.data.objects.new(self.name, me)
        bpy.context.scene.collection.objects.link(o)
        if self.bevel > 0:
            b = o.modifiers.new("Bevel", "BEVEL"); b.width = self.bevel; b.segments = 2
            b.limit_method = "ANGLE"; b.angle_limit = math.radians(self.sharp); b.harden_normals = True
            b.miter_outer = "MITER_ARC"
        return o


def loft(name, rings, mat, cap0=True, cap1=True, tip=None, tip_mat=None, ring_mats=None, bevel=0.0):
    """Skin closed rings (lists of game-space points, same count) into a tube. tip: a point that closes the last ring.
    ring_mats(i_ring, j_side) -> material name, to colour edge bands differently."""
    P = Part(name); P.bevel = bevel
    R = [[P.v(p) for p in ring] for ring in rings]
    n = len(rings[0])
    for i in range(len(R) - 1):
        for j in range(n):
            jn = (j + 1) % n
            P.face((R[i][j], R[i][jn], R[i + 1][jn], R[i + 1][j]), ring_mats(i, j) if ring_mats else mat)
    if tip is not None:
        t = P.v(tip)
        for j in range(n):
            P.face((R[-1][j], R[-1][(j + 1) % n], t), (ring_mats(len(R) - 1, j) if ring_mats else tip_mat or mat))
    elif cap1:
        P.face(R[-1], mat)
    if cap0:
        P.face(list(reversed(R[0])), mat)
    return P.obj()


def lathe(name, profile, mat, seg=24, sx=1.0, sz=1.0, y0=0.0, bevel=0.0, mats=None, twist=0.0, x=0.0, z=0.0, caps=True):
    """Revolve a (radius, y) profile around the Y axis. sx/sz squash the section into an ellipse.
    mats: per profile segment material list. twist: radians per metre (for spiral grips)."""
    P = Part(name); P.bevel = bevel
    rings = []
    for (r, y) in profile:
        ring = []
        for k in range(seg):
            a = 2 * math.pi * k / seg + twist * y
            if r <= 1e-6:
                ring = [P.v((x, y + y0, z))] * seg; break
            ring.append(P.v((x + r * math.cos(a) * sx, y + y0, z + r * math.sin(a) * sz)))
        rings.append(ring)
    for i in range(len(rings) - 1):
        m = mats[i] if mats else mat
        for k in range(seg):
            kn = (k + 1) % seg
            vs = [rings[i][k], rings[i][kn], rings[i + 1][kn], rings[i + 1][k]]
            uniq = []
            for vv in vs:
                if vv not in uniq:
                    uniq.append(vv)
            if len(uniq) >= 3:
                P.face(uniq, m)
    if caps and profile[0][0] > 1e-6:
        P.face(list(reversed(rings[0])), mats[0] if mats else mat)
    if caps and profile[-1][0] > 1e-6:
        P.face(rings[-1], mats[-1] if mats else mat)
    return P.obj()


def slab(name, outline, mat, t_edge, t_core, inset=None, z=0.0, bevel=0.0, edge_mat=None, rot=None, pos=(0, 0, 0)):
    """A flat shape (outline = list of (x, y) in its plane, counter-clockwise) with sharpened edges:
    thickness t_edge at the rim rising to t_core inside (inset metres in). For axe heads, guards, shields, blades.
    rot: Matrix (3x3) applied in game space, pos: offset."""
    P = Part(name); P.bevel = bevel
    M = rot or Matrix.Identity(3)
    off = Vector(pos)

    def gp(x, y, zz):
        return tuple(M @ Vector((x, y, zz + z)) + off)
    front = [P.v(gp(x, y, t_edge / 2)) for x, y in outline]
    back = [P.v(gp(x, y, -t_edge / 2)) for x, y in outline]
    n = len(outline)
    em = edge_mat or mat
    for j in range(n):
        jn = (j + 1) % n
        P.face((back[j], back[jn], front[jn], front[j]), em)
    ff = P.face(front, mat); fb = P.face(list(reversed(back)), mat)
    if inset and ff and fb:
        for f, s in ((ff, 1), (fb, -1)):
            r = bmesh.ops.inset_region(P.bm, faces=[f], thickness=inset, depth=0, use_even_offset=True)
            d = G(*(M @ Vector((0, 0, s * (t_core - t_edge) / 2))))
            for v in f.verts:   # the original face is now the inner one: raise it to the core thickness
                v.co += d
            for fa in r.get("faces", []):
                fa.material_index = P.mats.index(em)
    return P.obj()


def tube(name, pts, radius, mat, sides=8, cap=True, radii=None, bevel=0.0):
    """A tube along a polyline of game-space points (cords, rings, bow limbs, strings, knuckle bows)."""
    rings = []
    up0 = Vector((0, 0, 1))
    for i, p in enumerate(pts):
        p = Vector(p)
        a = Vector(pts[max(i - 1, 0)]); b = Vector(pts[min(i + 1, len(pts) - 1)])
        t = (b - a).normalized()
        up = up0 if abs(t.dot(up0)) < 0.95 else Vector((1, 0, 0))
        nx = t.cross(up).normalized(); ny = t.cross(nx).normalized()
        r = radii[i] if radii else radius
        rings.append([tuple(p + (nx * math.cos(2 * math.pi * k / sides) + ny * math.sin(2 * math.pi * k / sides)) * r) for k in range(sides)])
    return loft(name, rings, mat, cap0=cap, cap1=cap, bevel=bevel)


def ring(name, center, radius, thick, mat, axis="z", seg=32, sides=8):
    """A torus ring around an axis through center (game coords)."""
    pts = []
    for k in range(seg + 1):
        a = 2 * math.pi * k / seg
        c, s = math.cos(a) * radius, math.sin(a) * radius
        d = {"z": (c, s, 0), "y": (c, 0, s), "x": (0, c, s)}[axis]
        pts.append((center[0] + d[0], center[1] + d[1], center[2] + d[2]))
    o = tube(name, pts, thick, mat, sides=sides, cap=False)
    return o


def ball(name, center, r, mat, sx=1.0, sy=1.0, sz=1.0, seg=20, rings_=12):
    prof = [(r * math.sin(math.pi * i / rings_) * 1.0, -r * math.cos(math.pi * i / rings_) * sy) for i in range(rings_ + 1)]
    return lathe(name, prof, mat, seg=seg, sx=sx, sz=sz, y0=center[1], x=center[0], z=center[2])


def spike(name, base, tip, r, mat, sides=6):
    base, tip = Vector(base), Vector(tip)
    t = (tip - base).normalized()
    up = Vector((0, 1, 0)) if abs(t.y) < 0.9 else Vector((1, 0, 0))
    nx = t.cross(up).normalized(); ny = t.cross(nx).normalized()
    rr = [tuple(base + (nx * math.cos(2 * math.pi * k / sides) + ny * math.sin(2 * math.pi * k / sides)) * r) for k in range(sides)]
    return loft(name, [rr], mat, cap0=True, cap1=False, tip=tuple(tip))


def cutter_box(lo, hi):
    """An invisible box (game-space corners) used by cut(); removed after finish()."""
    P = Part("cut_box")
    xs, ys, zs = (lo[0], hi[0]), (lo[1], hi[1]), (lo[2], hi[2])
    v = [[[P.v((x, y, z)) for z in zs] for y in ys] for x in xs]
    for f in ((v[0][0][0], v[1][0][0], v[1][1][0], v[0][1][0]), (v[0][0][1], v[0][1][1], v[1][1][1], v[1][0][1]),
              (v[0][0][0], v[0][0][1], v[1][0][1], v[1][0][0]), (v[0][1][0], v[1][1][0], v[1][1][1], v[0][1][1]),
              (v[0][0][0], v[0][1][0], v[0][1][1], v[0][0][1]), (v[1][0][0], v[1][0][1], v[1][1][1], v[1][1][0])):
        P.face(f, "G_Metal_Iron")
    o = P.obj(); o.hide_render = True; o.display_type = "WIRE"
    return o


def cut(o, cutter):
    """Boolean-subtract cutter from o (applied when the model is finished), before any bevel."""
    m = o.modifiers.new("Cut", "BOOLEAN"); m.operation = "DIFFERENCE"; m.object = cutter
    try:
        m.solver = "EXACT"
    except TypeError:
        pass
    o.modifiers.move(len(o.modifiers) - 1, 0)
    return o


def thicken(o, t, offset=-1.0):
    """Give a shell real thickness (cloth, hoods, capes)."""
    m = o.modifiers.new("Solid", "SOLIDIFY"); m.thickness = t; m.offset = offset
    return o


_C = Matrix(((1, 0, 0), (0, 0, -1), (0, 1, 0)))   # game axes -> Blender axes


def place(o, rot=(0, 0, 0), loc=(0, 0, 0)):
    """Rotate (game-space Euler XYZ, degrees) about the game origin, then move (game coords)."""
    from mathutils import Euler
    rg = Euler([math.radians(a) for a in rot], "XYZ").to_matrix()
    o.matrix_world = Matrix.Translation(G(*loc)) @ (_C @ rg @ _C.transposed()).to_4x4() @ o.matrix_world
    return o


# ------------------------------------------------------------------ finishing / export
def _ao_to_vertex_colors(o, glow_idx, lo=0.5, noise=0.12, seed=1):
    """Ambient occlusion baked by Cycles into the 'Col' colour attribute, remapped so crevices go dark,
    plus a little low-frequency grime. Glow faces stay white."""
    me = o.data
    if "Col" not in me.color_attributes:
        me.color_attributes.new("Col", "FLOAT_COLOR", "POINT")
    me.color_attributes.active_color = me.color_attributes["Col"]
    sc = bpy.context.scene
    prev = sc.render.engine
    try:
        sc.render.engine = "CYCLES"
    except TypeError as e:
        print("no cycles:", e); return
    sc.cycles.samples = 48
    sc.cycles.device = "CPU"
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True); bpy.context.view_layer.objects.active = o
    sc.render.bake.target = "VERTEX_COLORS"
    try:
        bpy.ops.object.bake(type="AO", target="VERTEX_COLORS")
    except Exception as e:
        print("bake failed:", e)
    sc.render.engine = prev
    attr = me.color_attributes["Col"]
    rnd = random.Random(seed)
    ph = [rnd.uniform(0, 6.28) for _ in range(3)]
    glow_verts = set()
    for p in me.polygons:
        if p.material_index in glow_idx:
            glow_verts.update(p.vertices)
    for i, d in enumerate(attr.data):
        if i in glow_verts:
            d.color = (1, 1, 1, 1); continue
        ao = d.color[0]
        c = me.vertices[i].co
        g = 1 - noise * (0.5 + 0.5 * math.sin(c.x * 23 + ph[0]) * math.sin(c.y * 17 + ph[1]) * math.sin(c.z * 29 + ph[2]))
        v = (lo + (1 - lo) * ao) * g
        d.color = (v, v, v, 1)


def finish(name, parts, out_path, ao=True):
    """Apply modifiers, join the parts into one mesh named `name`, shade, bake dirt, export GLB to out_path."""
    bpy.ops.object.select_all(action="DESELECT")
    dg = bpy.context.evaluated_depsgraph_get()
    objs = []
    for o in parts:
        me = bpy.data.meshes.new_from_object(o.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
        n = bpy.data.objects.new(o.name + "_e", me)
        n.matrix_world = o.matrix_world.copy()
        bpy.context.scene.collection.objects.link(n)
        for i, m in enumerate(o.data.materials):
            if i < len(me.materials):
                me.materials[i] = m
        objs.append(n)
    for o in parts:
        me = o.data; bpy.data.objects.remove(o); bpy.data.meshes.remove(me)
    for o in [o for o in bpy.context.scene.objects if o.name.startswith("cut_")]:
        me = o.data; bpy.data.objects.remove(o); bpy.data.meshes.remove(me)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1:
        bpy.ops.object.join()
    o = bpy.context.active_object
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    o.name = name; o.data.name = name
    me = o.data
    for p in me.polygons:
        p.use_smooth = True
    try:
        me.set_sharp_from_angle(angle=math.radians(40))
    except Exception:
        pass
    # merge duplicate material slots (joined parts each brought their own)
    bpy.ops.object.material_slot_remove_unused()
    glow_idx = {i for i, m in enumerate(me.materials) if m and m.name.startswith("G_Glow_")}
    if ao:
        _ao_to_vertex_colors(o, glow_idx)
    tris = sum(len(p.vertices) - 2 for p in me.polygons)
    if out_path:
        os.makedirs(os.path.dirname(out_path), exist_ok=True)
        bpy.ops.object.select_all(action="DESELECT"); o.select_set(True)
        kw = dict(filepath=out_path, export_format="GLB", use_selection=True, export_yup=True, export_apply=True,
                  export_image_format="NONE", export_materials="EXPORT")
        try:
            bpy.ops.export_scene.gltf(**kw, export_vertex_color="ACTIVE", export_all_vertex_colors=False)
        except TypeError:
            bpy.ops.export_scene.gltf(**kw)
    return o, tris
