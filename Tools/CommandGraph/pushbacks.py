"""Hit pushbacks (knockback travel) of a Tekken 7 moveset, for the exported command graph.

Each reaction names seven pushback records (front, backturned, left side, right side, front
counter hit, downed, block). A record is {duration, displacement, num_of_loops, extradata}
(TKMovesets Structs_t7.h) with num_of_loops 16-bit "horizontal offset" values. The reaction
clips themselves carry no root travel, so this is the whole fly-back of a hit.

Reading used by the remaster (inferred from the data; the game's code is native). Two independent
parts, both pushing away from the attacker (negative pulls in):
  * the offsets push num_of_loops frames, one value per frame, in millimetres. Plain hits and
    blocks use only this (Jin's jab: 135 mm on hit, 530 mm on block; harder hits push more);
  * displacement (signed) is millimetres pushed every frame for duration frames; some records use
    only this (70 mm a frame for 20 frames, a 40 mm-a-frame pull over 60).
A face knockdown (sDw_KAO00_) then travels 0.65 + 1.9 m and an air hit (Dw_AIR_ponF) 0.42 +
3.0 m. (First read as centimetres in total, which gave ~1 m; the user found falls and air hits
too short with that, 2026-10-26.)

Run on its own to add the fields to an already exported graph:
    python pushbacks.py --moveset <tkdata>/mothead/bin/jinmot.bin --graph <graph json>
"""
import argparse
import hashlib
import json
import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "CombatPoseConverter" / "vendor"))
from t7extract.moveset import NONE, parse_moveset  # noqa: E402


def export(ms):
    """Returns (pushback table, per-reaction [7 table indices or -1])."""
    table = []
    for i in range(ms.count("pushbacks")):
        duration, displacement, loops, extra = struct.unpack("<HhIQ", ms.record("pushbacks", i))
        curve = []
        if extra != NONE:
            curve = [struct.unpack("<h", ms.record("pushback_extra", extra + k))[0] for k in range(loops)]
        table.append({"duration": duration, "displacement": displacement, "curve": curve})
    per_reaction = []
    for i in range(ms.count("reactions")):
        refs = struct.unpack_from("<7Q", ms.record("reactions", i), 0)
        per_reaction.append([-1 if r == NONE or r >= len(table) else r for r in refs])
    return table, per_reaction


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--moveset", required=True)
    p.add_argument("--graph", required=True)
    a = p.parse_args()
    raw = Path(a.moveset).read_bytes()
    graph = json.loads(Path(a.graph).read_text(encoding="utf-8"))
    if graph.get("sha256") != hashlib.sha256(raw).hexdigest():
        sys.exit(f"{a.graph} was exported from a different {graph.get('source')}")
    table, per_reaction = export(parse_moveset(raw))
    if len(per_reaction) != len(graph["reactions"]):
        sys.exit("reaction count differs")
    graph["pushbacks"] = table
    for reaction, refs in zip(graph["reactions"], per_reaction):
        reaction["pushbacks"] = refs
    Path(a.graph).write_text(json.dumps(graph, separators=(",", ":")), encoding="utf-8")
    print(f"{a.graph}: {len(table)} pushbacks")


if __name__ == "__main__":
    main()
