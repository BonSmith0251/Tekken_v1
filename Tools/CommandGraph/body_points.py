"""Torso track of every clip in an exported command graph, for body collision in versus.

Tekken 7's push collision is native code and the moveset holds no body sizes, so the remaster
keeps the fighters' torsos apart using the animation itself: for each clip frame, forward
kinematics on the character's bind skeleton gives the hip and head positions relative to the
fighter's floor position (the BASE bone), in the body frame (forward, left), in centimetres.
Lunges, leans and lying bodies then take the space they visibly take.

Frames are stored every STEP frames as [hipF, hipL, headF, headL, feetF, feetL] per sample
(feet = midpoint of the two feet), so a lying body covers feet to head.

    python body_points.py --graph <graph json> --catalog <Museum/<Full>/catalog.json>
        --skeleton <Characters/<Name>/Source/Base/<code>_bdl_1P.json> --asset <Museum/<Name>.asset>
"""
import argparse
import json
import re
import struct
from pathlib import Path

import numpy as np
from scipy.spatial.transform import Rotation as R

STEP = 2
HIP, HEAD, FEET = "Hip", "Head", ("L_Foot", "R_Foot")
FIELDS = 6


def load_skeleton(path):
    bones = json.loads(Path(path).read_text(encoding="utf-8"))["bones"]
    index = {b["name"].lower(): i for i, b in enumerate(bones)}
    return bones, index


def track(data, bones, index, mapped):
    frames, count = struct.unpack_from("<ii", data, 4)
    stride = (3 + count * 4) * 4
    by_name = {n.lower(): k for k, n in enumerate(mapped)}
    base = index["base"]

    def chain(name):
        i = index[name.lower()]; out = []
        while i >= 0:
            out.append(i); i = bones[i]["parent"]
        return out[::-1]

    hip_chain, head_chain = chain(HIP), chain(HEAD)
    feet_chains = [chain(n) for n in FEET]
    # BASE's parent frame: remapped root travel moves BASE along local +y (forward) and -x (left).
    parent_rot = R.identity()
    for i in chain("BASE")[:-1]:
        q = bones[i]["rotation"]; parent_rot = parent_rot * R.from_quat([q[k] for k in "xyzw"])
    forward, left = parent_rot.apply([0, 1, 0]), parent_rot.apply([-1, 0, 0])
    up = np.cross(forward, left); up /= np.linalg.norm(up)
    forward = forward - up * forward.dot(up); forward /= np.linalg.norm(forward)
    left = left - up * left.dot(up); left /= np.linalg.norm(left)

    out = []
    for f in range(0, frames, STEP):
        o = 16 + f * stride
        root = np.array(struct.unpack_from("<3f", data, o))
        quats = {}
        for name in set(hip_chain + head_chain + feet_chains[0] + feet_chains[1]):
            k = by_name.get(bones[name]["name"].lower())
            quats[name] = struct.unpack_from("<4f", data, o + 12 + k * 16) if k is not None else None

        def world(ch):
            pos = np.zeros(3); rot = R.identity()
            for i in ch:
                b = bones[i]
                p = np.array([b["position"][k] for k in "xyz"]) if i != base else root
                q = quats[i] if quats[i] is not None else [b["rotation"][k] for k in "xyzw"]
                pos = pos + rot.apply(p); rot = rot * R.from_quat(q)
            return pos

        base_pos = world(chain("BASE"))
        sample = []
        for ch in (hip_chain, head_chain):
            d = world(ch) - base_pos
            sample += [int(round(d.dot(forward) * 100)), int(round(d.dot(left) * 100))]
        d = (world(feet_chains[0]) + world(feet_chains[1])) / 2 - base_pos
        sample += [int(round(d.dot(forward) * 100)), int(round(d.dot(left) * 100))]
        out.extend(sample)
    return out


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--graph", required=True)
    p.add_argument("--catalog", required=True)
    p.add_argument("--skeleton", required=True)
    p.add_argument("--asset", required=True)
    a = p.parse_args()
    graph = json.loads(Path(a.graph).read_text(encoding="utf-8"))
    project = Path(a.catalog).resolve().parents[4]
    clips = {c["key"]: c["path"] for c in json.loads(Path(a.catalog).read_text(encoding="utf-8"))["clips"]}
    bones, index = load_skeleton(a.skeleton)
    mapped = [n[4:] for n in re.search(r"boneNames:\n((?:  - .*\n)+)", Path(a.asset).read_text(encoding="utf-8")).group(1).splitlines()]
    for clip in graph["clips"]:
        path = project / clips[clip["key"]]
        clip["body"] = track(path.read_bytes(), bones, index, mapped)
    graph["bodyStep"] = STEP
    graph["bodyFields"] = FIELDS
    Path(a.graph).write_text(json.dumps(graph, separators=(",", ":")), encoding="utf-8")
    print(f"{a.graph}: torso tracks for {len(graph['clips'])} clips")


if __name__ == "__main__":
    main()
