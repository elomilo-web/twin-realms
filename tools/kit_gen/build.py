"""Builds every model in the kit to assets/models/kit/<group>/<name>.glb and preview sheets to docs/kit/.
Run from the repo root:  python tools/kit_gen/build.py [group ...]"""
import importlib, json, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import preview
ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
GROUPS = ['nature', 'village', 'castle', 'dungeon', 'weapons', 'armor']
VIEW = {'weapons': dict(yaw=20, pitch=8), 'armor': dict(yaw=30, pitch=12)}
if __name__ == '__main__':
    want = sys.argv[1:] or GROUPS; manifest = {}
    mp = os.path.join(ROOT, 'data', 'kit.json')
    if os.path.exists(mp): manifest = json.load(open(mp)).get('models', {})
    for g in want:
        models = importlib.import_module('assets_' + g).all_models(); os.makedirs(os.path.join(ROOT, 'assets/models/kit', g), exist_ok=True)
        manifest[g] = {}
        for m in models:
            m.save(os.path.join(ROOT, 'assets/models/kit', g, m.name + '.glb')); lo, hi = m.bounds()
            manifest[g][m.name] = {"tris": m.tris(), "size": [round(float(x), 2) for x in (hi - lo)]}
        os.makedirs(os.path.join(ROOT, 'docs/kit'), exist_ok=True)
        preview.sheet(models, os.path.join(ROOT, 'docs/kit', g + '.png'), title='Twin Realms kit: ' + g, **VIEW.get(g, {}))
        print(g, len(models), 'models', sum(m.tris() for m in models), 'tris')
    json.dump({"_help": "Models made by tools/kit_gen (original, CC0). Path: res://assets/models/kit/<group>/<name>.glb. size = width, height, depth in metres.", "models": manifest},
              open(mp, 'w'), indent=1)
