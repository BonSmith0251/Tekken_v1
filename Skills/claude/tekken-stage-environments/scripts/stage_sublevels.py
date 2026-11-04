"""Survey a stage's umaps: mesh-bearing component classes per map, and meshes near a map point.

  python stage_sublevels.py stg01                                  # every stgNN_*.umap
  python stage_sublevels.py stg01 --map stg01_reload --near 0 0 2802   # UE cm, Z up

Use it when something visible in the game is missing. Breakable floors, rings and props live
in _reload / _demo (the exporter reads those, see SUBLEVELS in import_museum_stages.py) or in
other sublevels it does not read. Reads the paks; a large map takes ~10 s.
"""
import argparse
import collections
from _common import game

KEEP = ('Mesh', 'Destruct', 'Break', 'Floor', 'Light', 'Fog', 'Sky')
MESH_COMPONENTS = ('StaticMeshComponent', 'DestructibleComponent', 'SkeletalMeshComponent')


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('stage')
    ap.add_argument('--map', help='only this umap, e.g. stg01_reload')
    ap.add_argument('--near', nargs=3, type=float, metavar=('X', 'Y', 'Z'))
    ap.add_argument('--radius', type=float, default=1500)
    a = ap.parse_args()
    src, resolve, exporter = game()
    from t7extract.package import Package
    maps = sorted(i.path for i in src.items()
                  if f'/maps/{a.stage}_' in i.path.lower() and i.path.lower().endswith('.umap'))
    for path in maps:
        name = path.rsplit('/', 1)[-1][:-5]
        if a.map and name != a.map:
            continue
        p = Package(resolve(path), path)
        classes = collections.Counter(e.class_name for e in p.exports)
        print(f'{name:22} {len(p.data) // 1024:7} KB', {k: v for k, v in classes.items() if any(w in k for w in KEEP)})
        if not a.near:
            continue
        for e in p.exports:
            if e.class_name not in MESH_COMPONENTS:
                continue
            v, _ = exporter.properties(p, p.reader(e.serial_offset))
            ref = (v.get('StaticMesh') or v.get('SkeletalMesh') or {}).get('object', '') or ''
            ref = ref.split('.')[0].rsplit('/', 1)[-1]
            loc = v.get('RelativeLocation', {'x': 0, 'y': 0, 'z': 0})
            if all(abs(loc[k] - c) < a.radius for k, c in zip('xyz', a.near)):
                hidden = (v.get('bHiddenInGame', False) or not v.get('bVisible', True)
                          or v.get('bCastHiddenShadow', False) or exporter.actor_hidden(p, e))
                where = {k: round(x) for k, x in loc.items()}
                print(f"   {e.class_name[:14]:14} {ref[:46]:46} {where} {'hidden' if hidden else ''}")


if __name__ == '__main__':
    main()
