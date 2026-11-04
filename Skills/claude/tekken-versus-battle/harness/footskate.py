"""Foot skating of converted JRT1 clips: FK on the bind skeleton with full root motion."""
import json, struct, re, sys
import numpy as np
from scipy.spatial.transform import Rotation as R
rig_path, asset_path, catalog_path = sys.argv[1:4]
remap = len(sys.argv) > 5 and sys.argv[5] == 'remap'
ids = [int(x) for x in sys.argv[4].split(',')]
bones = json.load(open(rig_path))['bones']
names = [b['name'] for b in bones]
mapped = [n[4:] for n in re.search(r'boneNames:\n((?:  - .*\n)+)', open(asset_path, encoding='utf-8').read()).group(1).splitlines()]
cat = json.load(open(catalog_path, encoding='utf-8'))
clips = {c['key']: c['path'] for c in cat['clips']}
moves = {m['id']: m for m in cat['moves']}
def chain(n):
    i = names.index(n); out = []
    while i >= 0: out.append(i); i = bones[i]['parent']
    return out[::-1]
def local(i, f, pose, root):
    b = bones[i]; p = np.array([b['position'][k] for k in 'xyz'])
    q = [b['rotation'][k] for k in 'xyzw']
    if b['name'] in pose: q = pose[b['name']]
    if b['name'] == 'BASE': p = root
    return p, R.from_quat(q)
def foot_world(name, pose, root):
    pos = np.zeros(3); rot = R.identity()
    for i in chain(name):
        p, q = local(i, 0, pose, root)
        pos = pos + rot.apply(p); rot = rot * q
    return pos
for mid in ids:
    m = moves[mid]; data = open(clips[m['key']], 'rb').read()
    frames, count = struct.unpack_from('<ii', data, 4)
    stride = (3 + count * 4) * 4
    feet = {'L_Foot': [], 'R_Foot': []}
    for f in range(frames):
        o = 16 + f * stride
        root = np.array(struct.unpack_from('<3f', data, o))
        if f == 0: r0 = root.copy()
        if remap:   # root travel into the body frame: local -x forward -> +y local, +y left -> -x local
            d = root - r0; root = np.array([r0[0] - d[1], r0[1] - d[0], root[2]])
        pose = {mapped[b]: struct.unpack_from('<4f', data, o + 12 + b * 16) for b in range(count)}
        for k in feet: feet[k].append(foot_world(k, pose, root))
    report = []
    for k, ps in feet.items():
        ps = np.array(ps)
        # Unity world: y up. A planted foot is within 2 cm of its lowest height.
        y = ps[:, 1]; low = y.min()
        planted = y < low + 0.02
        step = np.linalg.norm(np.diff(ps[:, [0, 2]], axis=0), axis=1)
        skate = step[planted[1:] & planted[:-1]]
        report.append(f"{k}: planted {planted.mean()*100:4.0f}%  slide while planted {skate.sum():.3f} m (mean {skate.mean()*60 if len(skate) else 0:.2f} m/s)")
    travel = np.linalg.norm((np.array(feet['L_Foot'][-1]) - np.array(feet['L_Foot'][0]))[[0, 2]])
    print(f"{mid} {m['name'][:22]:22s} frames {frames:3d} | " + " | ".join(report) + f" | foot travel {travel:.2f} m")
