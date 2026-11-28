"""A-35: turn downloaded CC0 models (Poly Haven, Poly Pizza) into Twin Realms kit weapons.

  blender -b --factory-startup -P tools/blender_gothic/import_props.py -- --src <downloads folder> [name ...]

Each entry of SPEC: the source file (relative to --src), which source axis becomes the weapon's long axis (game +Y,
head / blade end), which becomes the striking side (game +X), where the grip is (source coordinate along the
long axis, source units: becomes the origin; the handle is centred on it), a uniform scale, and the material mode:
  "keep":   the model's own textures, scaled down to tex px and written into the GLB as JPEG; metal = True renames the
            materials TR_Metal_* so the item's element tints them (scripts/Weapons.cs BuildKit);
  {..}:     source material name (or "*") -> a shared gothic material G_* (assets/materials/gothic, triplanar), plus
            optional "split": [[source long-axis coordinate, G_*], ...] for faces beyond that point; ambient occlusion is
            baked into vertex colours like the gothic kit (tools/blender_gothic/gothic.py finish()).
keep / drop: object names to keep or drop. Output: assets/models/kit/weapons/<name>.glb (see assets/ASSETS.md)."""
import bpy, bmesh, os, sys, math
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import gothic  # noqa: E402

OUT = os.path.join(gothic.ROOT, "assets", "models", "kit", "weapons")

SPEC = {
    # ---- real weapons (Poly Haven, CC0)
    "ph_estoc_cathedral": dict(src="dl_ph_antique_estoc/antique_estoc.gltf", up="+Z", lead="+X", grip=0.03, scale=1.0, mode="keep", metal=True, tex=512),
    "ph_katana_mist": dict(src="dl_ph_antique_katana_01/antique_katana_01.gltf", up="+Z", lead="+X", grip=-0.075, scale=1.12, mode="keep", metal=True, tex=512),
    "ph_mace_bishop": dict(src="dl_ph_ornate_medieval_mace/ornate_medieval_mace.gltf", up="+Z", lead="+X", grip=-0.01, scale=1.3, mode="keep", metal=True, tex=512),
    "ph_hammer_bell": dict(src="dl_ph_ornate_war_hammer/ornate_war_hammer.gltf", up="+Z", lead="+X", grip=-0.07, scale=1.2, mode="keep", metal=True, tex=512),
    "ph_axe_woodsman": dict(src="dl_ph_wooden_axe_02/wooden_axe_02.gltf", up="+Z", lead="-Y", grip=-0.2, scale=1.2, mode="keep", tex=512),
    "ph_greatshield_kite": dict(src="dl_ph_kite_shield/kite_shield.gltf", up="+Z", lead="+X", grip=-0.05, scale=1.0, mode="keep", metal=True, tex=512, shield=True),
    # ---- unexpected items (Poly Haven, CC0)
    "odd_pan": dict(src="dl_ph_brass_pan_01/brass_pan_01.gltf", up="-Y", lead="+Z", grip=0.3, scale=1.5, mode="keep", tex=512),
    "odd_spade": dict(src="dl_ph_rusted_spade_01/rusted_spade_01.gltf", up="-Z", lead="+X", grip=0.32, scale=1.25, mode="keep", tex=512),
    "odd_broom": dict(src="dl_ph_wooden_broom/wooden_broom.gltf", up="-Z", lead="+Y", grip=1.15, scale=1.1, mode="keep", tex=512),
    "odd_door": dict(src="dl_ph_large_castle_door/large_castle_door.gltf", keep=["large_castle_door_left"], up="+Z", lead="+Y", grip=0.12, scale=0.7, mode="keep", tex=512),
    "odd_lamppost": dict(src="dl_ph_street_lamp_01/street_lamp_01.gltf", up="+Z", lead="+X", grip=1.05, scale=0.62, mode="keep", metal=True, tex=512),
    "odd_chair": dict(src="dl_ph_WoodenChair_01/WoodenChair_01.gltf", up="-Z", lead="+X", grip=2.12, scale=0.55, mode="keep", tex=512),
    "odd_cittern": dict(src="dl_ph_Ukulele_01/Ukulele_01.gltf", up="-Z", lead="+Y", grip=0.36, scale=1.8, mode="keep", tex=512),
    # the same door leaf as a greatshield (shields: centred, front +Z like the kit shields; drawn by avatar.json kitShieldSize)
    "odd_door_shield": dict(src="dl_ph_large_castle_door/large_castle_door.gltf", keep=["large_castle_door_left"], up="+Z", lead="+X", grip=1.38, scale=0.5, mode="keep", tex=512, shield=True),
    # ---- unexpected items (Poly Pizza, CC0), re-skinned with the gothic shared materials
    "odd_anchor": dict(src="dl_pp_anchor_nikdane12/anchor_nikdane12.glb", up="-Z", lead="+Y", grip=0.78, scale=0.7, mode={"*": "G_Metal_Rust"}),
    "odd_fish": dict(src="dl_pp_fish_quaternius/fish_quaternius.glb", drop=["Icosphere"], up="-Y", lead="+X", grip=3.1, scale=0.2,
                     mode={"Top": "G_Scales", "Bottom": "G_Scales", "Fins": "G_Leather", "*": "G_Scales"}),
    "odd_pitchfork": dict(src="dl_pp_pitchfork_creativetrio/pitchfork_creativetrio.glb", up="+Y", lead="+X", grip=-0.18, scale=0.65,
                          mode={"*": "G_Wood", "split": [[0.88, "G_Metal_Iron"]]}),
    "odd_torch": dict(src="dl_pp_torch_quaternius/torch_quaternius.glb", up="+Z", lead="+X", grip=-0.3, scale=0.3,
                      mode={"DarkWood": "G_Wood", "LightGrey": "G_Cloth_Black", "Yellow": "G_Glow_ff9a3a", "*": "G_Wood"}),
}

AX = {"X": 0, "Y": 1, "Z": 2}


def axis_vec(s):
    v = Vector((0, 0, 0)); v[AX[s[1]]] = -1.0 if s[0] == "-" else 1.0
    return v


def load(name, e, src_root):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=os.path.join(src_root, e["src"]))
    objs = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if "keep" in e:
        objs = [o for o in objs if any(o.name.startswith(k) for k in e["keep"])]
    if "drop" in e:
        objs = [o for o in objs if not any(o.name.startswith(k) for k in e["drop"])]
    for o in list(bpy.context.scene.objects):
        if o not in objs:
            bpy.data.objects.remove(o, do_unlink=True)
    # bake each object's world transform into its mesh, then join
    for o in objs:
        mw = o.matrix_world.copy(); o.parent = None
        o.data = o.data.copy(); o.data.transform(mw); o.matrix_world = Matrix.Identity(4)
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1:
        bpy.ops.object.join()
    o = bpy.context.active_object; o.name = name; o.data.name = name
    return o


def orient(o, e):
    """Source axes -> game: up -> Blender +Z (glTF/game +Y), lead -> +X; grip point -> origin; scale."""
    up, lead = axis_vec(e["up"]), axis_vec(e["lead"])
    y = up.cross(lead)
    R = Matrix((lead, y, up))   # rows: new x, y, z in source coords
    me = o.data
    # the grip point: along the up axis at e["grip"]; across, the middle of the handle's section there
    g = e["grip"] * (1 if e["up"][0] == "+" else -1)   # grip in the new z (unscaled)
    zs = [(R @ v.co) for v in me.vertices]
    span = max(p.z for p in zs) - min(p.z for p in zs)
    near = [p for p in zs if abs(p.z - g) < span * 0.03] or zs
    cx = (min(p.x for p in near) + max(p.x for p in near)) / 2
    cy = (min(p.y for p in near) + max(p.y for p in near)) / 2
    if e.get("shield"):
        cx = (min(p.x for p in zs) + max(p.x for p in zs)) / 2; cy = 0.0   # shields: centred on the board, front as modelled
    T = Matrix.Translation(Vector((-cx, -cy, -g)))
    s = e["scale"]
    me.transform(Matrix.Scale(s, 4) @ T @ R.to_4x4())
    me.update()
    return s


def gothic_materials(o, e):
    mode = e["mode"]; me = o.data
    names = [m.name.split(".")[0] if m else "" for m in me.materials]
    target = [mode.get(n, mode.get("*", "G_Wood")) for n in names]
    split = mode.get("split", [])
    sgn = 1 if e["up"][0] == "+" else -1
    # new material list
    used = sorted(set(target) | {m for _, m in split})
    me.materials.clear()
    for m in used:
        me.materials.append(gothic.material(m))
    for p in me.polygons:
        tm = target[p.material_index] if p.material_index < len(target) else target[0]
        zc = p.center.z / e["scale"] + e["grip"] * sgn   # back to the source long-axis coordinate (signed along up)
        for thr, m in split:
            if zc * sgn > thr:
                tm = m
        p.material_index = used.index(tm)
    # drop source colours and UVs: the shared materials are triplanar, the AO bake writes 'Col'
    for a in list(me.color_attributes):
        me.color_attributes.remove(a)
    while me.uv_layers:
        me.uv_layers.remove(me.uv_layers[0])


def export_keep(o, e, path):
    me = o.data
    for p in me.polygons:
        p.use_smooth = p.use_smooth   # keep the source shading
    if e.get("metal"):
        for m in me.materials:
            if m and not m.name.startswith("TR_Metal_"):
                m.name = "TR_Metal_" + m.name
    tex = e.get("tex", 512)
    for im in bpy.data.images:
        if im.size[0] > tex or im.size[1] > tex:
            im.scale(min(tex, im.size[0]), min(tex, im.size[1]))
    for a in list(me.color_attributes):   # no stray vertex colours (the game multiplies albedo by them)
        me.color_attributes.remove(a)
    bpy.ops.object.select_all(action="DESELECT"); o.select_set(True)
    kw = dict(filepath=path, export_format="GLB", use_selection=True, export_yup=True, export_apply=True,
              export_image_format="JPEG", export_materials="EXPORT")
    try:
        bpy.ops.export_scene.gltf(**kw, export_jpeg_quality=85)
    except TypeError:
        bpy.ops.export_scene.gltf(**kw)


def run(names, src_root):
    for name in names:
        e = SPEC[name]
        o = load(name, e, src_root)
        orient(o, e)
        path = os.path.join(OUT, name + ".glb")
        if isinstance(e["mode"], dict):
            gothic_materials(o, e)
            gothic.finish(name, [o], path, ao=True)
        else:
            export_keep(o, e, path)
        pts = [v.co for v in bpy.data.objects[name].data.vertices] if name in bpy.data.objects else []
        if pts:
            mn = [min(p[i] for p in pts) for i in range(3)]; mx = [max(p[i] for p in pts) for i in range(3)]
            print(f"[props] {name}: x {mn[0]:.2f}..{mx[0]:.2f}  y {mn[1]:.2f}..{mx[1]:.2f}  up {mn[2]:.2f}..{mx[2]:.2f} m  ({os.path.getsize(path) // 1024} KB)", flush=True)


if __name__ == "__main__" and "--" in sys.argv:
    a = sys.argv[sys.argv.index("--") + 1:]
    src = a[a.index("--src") + 1]
    rest = [x for i, x in enumerate(a) if not x.startswith("--") and (i == 0 or a[i - 1] != "--src")]
    run(rest or list(SPEC), src)
