"""Shared helpers for the stage inspection scripts (read-only; nothing here edits the project)."""
import json, math, os, struct, sys
from pathlib import Path

WORKSPACE = Path(os.environ.get('T7_WORKSPACE', r'D:\games\Tekken.7.Ultimate.Edition.v5.10'))
PROJECT = WORKSPACE / 'UnityRemaster' / 'TekkenVersusRemaster'
SOURCE = PROJECT / 'Assets/_Project/Stages/Source'
TEXTURES = PROJECT / 'Assets/_Project/Stages/Textures'


def catalog(path=None):
    return json.load(open(path or SOURCE / 'stages.json', encoding='utf-8'))


def stage(cat, name):
    for s in cat['stages']:
        if s['name'] == name:
            return s
    sys.exit(f'no stage {name}; have ' + ' '.join(s['name'] for s in cat['stages']))


def slots(node, meshes):
    """Material keys a placed node draws: the mesh's slots with the node's overrides applied."""
    out = list(meshes[node['mesh']]['materials'])
    for i, o in enumerate(node.get('materials') or []):
        if o and i < len(out):
            out[i] = o
    return out


def _qa(axis, deg):
    a = math.radians(deg) / 2
    s = math.sin(a)
    return (math.cos(a), axis[0] * s, axis[1] * s, axis[2] * s)


def _qm(a, b):
    w1, x1, y1, z1 = a; w2, x2, y2, z2 = b
    return (w1*w2-x1*x2-y1*y2-z1*z2, w1*x2+x1*w2+y1*z2-z1*y2, w1*y2-x1*z2+y1*w2+z1*x2, w1*z2+x1*y2-y1*x2+z1*w2)


def _mat(q):
    import numpy as np
    w, x, y, z = q
    return np.array([[1-2*(y*y+z*z), 2*(x*y-z*w), 2*(x*z+y*w)],
                     [2*(x*y+z*w), 1-2*(x*x+z*z), 2*(y*z-x*w)],
                     [2*(x*z-y*w), 2*(y*z+x*w), 1-2*(x*x+y*y)]])


def world_matrices(st):
    """Unity-space 4x4 world matrix per node id, exactly as MuseumStageImporter places nodes
    (before StageEnvironment.Spawn's start-marker shift and yaw turn)."""
    import numpy as np
    nodes = {n['id']: n for n in st['nodes']}

    def local(n):
        p, s, r = n['position'], n['scale'], n['rotation']
        q = _qm(_qm(_qa((0, 1, 0), r['yaw']), _qa((0, 0, 1), r['pitch'])), _qa((1, 0, 0), -r['roll']))
        m = np.eye(4)
        m[:3, :3] = _mat(q) @ np.diag([s['x'], s['z'], s['y']])
        m[:3, 3] = np.array([p['x'], p['z'], -p['y']]) * .01
        return m
    out = {}
    for n in st['nodes']:
        m, cur = local(n), n
        while cur['parent'] in nodes:
            cur = nodes[cur['parent']]
            m = local(cur) @ m
        out[n['id']] = m
    return out


def mesh_positions(key):
    import numpy as np
    d = (SOURCE / f'{key}.bytes').read_bytes()
    if d[:4] != b'STG2':
        raise ValueError(f'{key} is not STG2')
    vc = struct.unpack_from('<i', d, 4)[0]
    return np.frombuffer(d, '<f4', vc * 3, 16).reshape(-1, 3)


def game():
    """t7extract source + resolver + the exporter module (for its properties() and Materials)."""
    sys.path[:0] = [str(WORKSPACE / 't7extract'), str(PROJECT / 'Tools'), str(PROJECT / 'Tools/OriginalLighting')]
    argv, sys.argv = sys.argv, ['x']
    import import_museum_stages as exporter
    sys.argv = argv
    from t7extract.cli import Source
    from t7extract.pak import TEKKEN_ASSET_KEY
    from t7extract.export_cli import _resolver
    src = Source(str(WORKSPACE / 'TEKKEN.7'), TEKKEN_ASSET_KEY)
    return src, _resolver(src), exporter
