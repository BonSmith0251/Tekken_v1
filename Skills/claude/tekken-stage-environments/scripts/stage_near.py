"""Find the meshes whose world bounds contain a point, to identify a wrong object in a capture.

  python stage_near.py stg15 0 -16                 # x z in Unity metres, map space
  python stage_near.py stg01 0 0 --ymax 40 --maxheight 3

Map space is before StageEnvironment.Spawn's start-marker shift and yaw turn, so use the
stage's "start" (printed first) as the point when the problem is at the fighters' feet.
Prints mesh asset, level (L0 _geom, L1 _reload, L2 _demo), node name, world min/max and materials.
"""
import argparse
from _common import catalog, stage, slots, world_matrices, mesh_positions


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('stage')
    ap.add_argument('x', type=float)
    ap.add_argument('z', type=float)
    ap.add_argument('--pad', type=float, default=.5)
    ap.add_argument('--ymax', type=float, default=1e9, help='skip meshes whose bottom is above this')
    ap.add_argument('--maxheight', type=float, default=1e9, help='skip meshes taller than this')
    a = ap.parse_args()
    cat = catalog()
    mats = {m['key']: m for m in cat['materials']}
    meshes = {m['key']: m for m in cat['meshes']}
    st = stage(cat, a.stage)
    world = world_matrices(st)
    print('start', st.get('start'), 'yaw', st.get('startYaw'))
    for n in st['nodes']:
        if not n['mesh']:
            continue
        try:
            p = mesh_positions(n['mesh'])
        except (OSError, ValueError):
            continue
        m = world[n['id']]
        wp = p @ m[:3, :3].T + m[:3, 3]
        lo, hi = wp.min(0), wp.max(0)
        if not (lo[0] - a.pad <= a.x <= hi[0] + a.pad and lo[2] - a.pad <= a.z <= hi[2] + a.pad):
            continue
        if lo[1] > a.ymax or hi[1] - lo[1] > a.maxheight:
            continue
        names = [mats[s]['path'].rsplit('/', 1)[-1][:28] if s in mats else '-' for s in slots(n, meshes)]
        asset = meshes[n['mesh']]['path'].rsplit('/', 1)[-1][:44]
        print(f"{asset:44} L{n['id'] // 100000} {n['name'][:24]:24} min={lo.round(1)} max={hi.round(1)} {names[:3]}")


if __name__ == '__main__':
    main()
