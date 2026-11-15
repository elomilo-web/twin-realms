"""Build Twin Realms gothic models with Blender.

  blender -b -P tools/blender_gothic/build.py -- [name ...] [--no-ao]
      builds the named models (default: all weapons and armor) into assets/models/kit/<group>/<name>.glb
Inside a running Blender (Text editor or the MCP bridge):
  exec(open(r"<repo>/tools/blender_gothic/build.py").read()); build(["sword_knight", "helm_knight"])
The models stay in the scene 'GothicBuild', lined up along X, for a look.
"""
import importlib, os, sys

HERE = os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals() else os.path.join(os.path.expanduser("~"), "Documents", "GitHub", "twin-realms", "tools", "blender_gothic")
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import gothic, weapons, armor  # noqa: E402
importlib.reload(gothic); importlib.reload(weapons); importlib.reload(armor)

KIT = os.path.join(gothic.ROOT, "assets", "models", "kit")
GROUPS = {"weapons": weapons.WEAPONS, "armor": armor.ARMOR}   # group folder -> {name: builder}


def _flat(x):
    for i in x:
        if isinstance(i, (list, tuple)):
            yield from _flat(i)
        elif i is not None:
            yield i


def find(name):
    for g, d in GROUPS.items():
        if name in d:
            return g, d[name]
    raise KeyError("no gothic model named " + name)


def build(names=None, ao=True, export=True, spacing=0.6):
    gothic.new_scene()
    names = names or [n for d in GROUPS.values() for n in d]
    report = []
    for k, n in enumerate(names):
        g, fn = find(n)
        parts = list(_flat(fn()))
        o, tris = gothic.finish(n, parts, os.path.join(KIT, g, n + ".glb") if export else None, ao=ao)
        o.location.x += k * spacing
        report.append(f"{g}/{n}: {tris} tris")
        print("[gothic]", report[-1], flush=True)
    return report


if __name__ == "__main__" and "--" in sys.argv:
    args = sys.argv[sys.argv.index("--") + 1:]
    build([a for a in args if not a.startswith("--")] or None, ao="--no-ao" not in args)
