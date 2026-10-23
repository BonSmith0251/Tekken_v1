"""Export a fighter's original command graph for the Unity command interpreter.

Reads the installed <chr>mot.bin and the museum catalog that already holds each
move's decoded animation length. Writes the raw cancel and group-cancel tables,
requirements, input sequences and move properties, indexed exactly as in the
source so move ids, cancel offsets and group offsets need no remapping.
Nothing is interpreted here; the runtime and reference interpreters own that.
"""
import argparse
import hashlib
import json
import math
import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "CombatPoseConverter" / "vendor"))
from t7extract.moveset import NONE, REQ_END, parse_moveset  # noqa: E402


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--moveset", required=True)
    p.add_argument("--catalog", required=True)
    p.add_argument("--character-id", type=int, required=True)
    p.add_argument("--stance", type=int, required=True)
    p.add_argument("--movelist", help="movelist JSON with demos; plain commands are embedded for the CPU")
    p.add_argument("--out", required=True)
    a = p.parse_args()
    raw = Path(a.moveset).read_bytes()
    ms = parse_moveset(raw)
    catalog = {m["id"]: m for m in json.loads(Path(a.catalog).read_text(encoding="utf-8"))["moves"]}

    requirements, req_index = [], {}

    def reqs(index):
        if index == NONE:
            return -1, 0
        if index not in req_index:
            start = len(requirements) // 2
            for r in ms.requirements(index):
                requirements.extend((r["condition"], r["param"]))
            req_index[index] = (start, len(requirements) // 2 - start)
        return req_index[index]

    def extra_value(index):
        if index == NONE or index >= ms.count("cancel_extra"):
            return 0
        return struct.unpack("<I", ms.record("cancel_extra", index))[0]

    def table(block):
        out = []
        for index in range(ms.count(block)):
            cmd, req, extra, s, e, sf, move_id, _ = struct.unpack("<QQQIIIHH", ms.record(block, index))
            rs, rc = reqs(req)
            # Group cancel (0x800B) targets are group-cancel table indices, not moves.
            target = move_id if (cmd & 0xFFFFFFFF) == 0x800B else ms.resolve_move(move_id)
            out.append({"command": cmd, "reqStart": rs, "reqCount": rc, "extra": extra_value(extra),
                        "start": s, "end": e, "startingFrame": sf, "target": target})
        return out

    cancels, group_cancels = table("cancels"), table("group_cancels")
    # Hit conditions are scanned in order; the first whose requirements hold applies.
    hit_conditions = []
    for index in range(ms.count("hit_conditions")):
        req, damage, _, reaction = struct.unpack("<QIIQ", ms.record("hit_conditions", index))
        rs, rc = reqs(req)
        hit_conditions.append({"reqStart": rs, "reqCount": rc, "damage": damage,
                               "reaction": -1 if reaction == NONE else reaction})
    # Reaction move ids, in moveset.py's order: standing, default, crouch, counterhit,
    # crouch_counterhit, left/right side (+crouch), backturned (+crouch), block,
    # crouch_block, wallslump, downed.
    reactions = [[ms.resolve_move(m) for m in struct.unpack_from("<16H", ms.record("reactions", i), 0x4E)]
                 for i in range(ms.count("reactions"))]
    # Root travel per converted clip, cumulative millimetres from the clip's first frame.
    # JRT1 root: -x is the fighter's forward, +y its left (see the walk and sidestep clips).
    # The body heading lives in the hip (JRT1 bone 2): its twist about the vertical axis,
    # relative to the stance clip's first frame, is the fighter's body yaw (backturned beyond 90).
    project = Path(a.catalog).resolve().parents[4]
    catalog_clips = json.loads(Path(a.catalog).read_text(encoding="utf-8"))["clips"]
    HIP = 2

    def hip(data, frame, bones):
        return struct.unpack_from("<4f", data, 16 + frame * (3 + bones * 4) * 4 + 12 + HIP * 16)

    stance_key = catalog[a.stance]["key"]
    stance_data = (project / next(c["path"] for c in catalog_clips if c["key"] == stance_key)).read_bytes()
    rx, ry, rz, rw = hip(stance_data, 0, struct.unpack_from("<i", stance_data, 8)[0])

    def yaw(q):
        x, y, z, w = q
        # relative = q * inverse(reference); twist about z = 2 * atan2(z, w)
        iw, ix, iy, iz = rw, -rx, -ry, -rz
        qw = w * iw - x * ix - y * iy - z * iz
        qz = w * iz + x * iy - y * ix + z * iw
        angle = math.degrees(2 * math.atan2(qz, qw))
        return round((angle + 180) % 360 - 180)

    clip_rows, clip_index, clip_frames = [], {}, {}
    for clip in catalog_clips:
        data = (project / clip["path"]).read_bytes()
        frames, bones = struct.unpack_from("<ii", data, 4)
        stride = (3 + bones * 4) * 4
        x0, y0, _ = struct.unpack_from("<3f", data, 16)
        forward, lateral, yaws = [], [], []
        for f in range(frames):
            x, y, _ = struct.unpack_from("<3f", data, 16 + f * stride)
            forward.append(round((x0 - x) * 1000))
            lateral.append(round((y - y0) * 1000))
            yaws.append(yaw(hip(data, f, bones)))
        clip_index[clip["key"]] = len(clip_rows)
        clip_frames[clip["key"]] = frames
        clip_rows.append({"key": clip["key"], "forward": forward, "lateral": lateral, "yaw": yaws})
    moves = []
    for i in range(ms.count("moves")):
        r = ms.record("moves", i)
        cancel = struct.unpack_from("<Q", r, 0x20)[0]
        hit = struct.unpack_from("<Q", r, 0x60)[0]
        extra = struct.unpack_from("<Q", r, 0x80)[0]
        info = json.loads(catalog[i]["details"]) if i in catalog else {}
        props = []
        if extra != NONE:
            for prop in ms.extra_props(extra):
                props.extend((prop["frame"], prop["id"], prop["value"]))
        active = info.get("active_frames", [0, 0])
        # Some catalogs (Xiaoyu's) omit the decoded length; the converted clip has it.
        frames = info.get("anim", {}).get("frames") or (clip_frames.get(catalog[i]["key"], 0) if i in catalog else 0)
        moves.append({"id": i, "name": info.get("name", ""), "frames": frames,
                      "transition": info.get("transition", i), "firstActive": active[0], "lastActive": active[1],
                      "damage": info.get("damage", 0), "hitlevel": info.get("hitlevel", 0),
                      # Vulnerability state (low byte: 0x80 downed, 0x20 crouching) and airborne window.
                      "vuln": info.get("vuln", 0), "airStart": info.get("airborne", {}).get("start", 0),
                      "airEnd": info.get("airborne", {}).get("end", 0),
                      "cancelStart": -1 if cancel == NONE else cancel,
                      "hitConditionStart": -1 if hit in (NONE, 0) else hit, "properties": props,
                      "clip": clip_index.get(catalog[i]["key"], -1) if i in catalog else -1})

    # Bit 63 of a sequence step is a flag the interpreters do not use; it is kept apart so
    # every step fits a signed 64-bit value for Unity's JsonUtility.
    sequences, inputs, input_flags = [], [], []
    for i in range(ms.count("input_sequences")):
        window, amount, _, first = struct.unpack("<HHiQ", ms.record("input_sequences", i))
        sequences.append({"window": window, "inputStart": len(inputs), "inputCount": amount})
        for j in range(first, min(first + amount, ms.count("inputs"))):
            value = struct.unpack("<Q", ms.record("inputs", j))[0]
            inputs.append(value & 0x7FFFFFFFFFFFFFFF)
            input_flags.append(value >> 63)

    demos = []
    if a.movelist:
        names = {"d/b": 1, "d": 2, "d/f": 3, "b": 4, "n": 5, "f": 6, "u/b": 7, "u": 8, "u/f": 9}
        for e in json.loads(Path(a.movelist).read_text(encoding="utf-8")):
            setup = any(t in e["command"] for t in ("Approach", "Time with", "enemy is down", ">", "During Rage"))
            if not e.get("demo") or setup or e["type"] not in ("move", "0x20000"):
                continue
            steps = []
            for step in e["demo"]["inputs"]:
                direction, buttons = 5, 0
                for part in step["input"].lower().split("+"):
                    if part in names:
                        direction = names[part]
                    elif part in "1234" and part:
                        buttons |= 1 << (int(part) - 1)
                steps.extend((direction, buttons, step["frames"]))
            demos.append({"name": e["name"], "command": e["command"], "steps": steps})

    graph = {"source": Path(a.moveset).name, "sha256": hashlib.sha256(raw).hexdigest(),
             "characterId": a.character_id, "stance": a.stance, "requirementEnd": REQ_END,
             # Standard states (stance, crouch, downed...) by alias slot: the same slot names each
             # character's own equivalent, which is how a borrowed reaction returns home.
             "aliases": [m if m != 0xFFFF else -1 for m in ms.aliases],
             "moves": moves, "cancels": cancels, "groupCancels": group_cancels,
             "hitConditions": hit_conditions, "clips": clip_rows, "reactions": [{"moves": r} for r in reactions],
             "requirements": requirements, "sequences": sequences, "inputs": inputs, "inputFlags": input_flags, "demos": demos}
    Path(a.out).parent.mkdir(parents=True, exist_ok=True)
    Path(a.out).write_text(json.dumps(graph, separators=(",", ":")), encoding="utf-8")
    print(f"{a.out}: {len(moves)} moves, {len(cancels)} cancels, "
          f"{len(group_cancels)} group cancels, {len(sequences)} sequences")


if __name__ == "__main__":
    main()
