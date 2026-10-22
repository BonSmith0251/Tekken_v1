"""Audit Xiaoyu's native staging references without assuming another fighter's rig."""
import hashlib
import json
import math
import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[3]
MOVESET = HERE / "xia" / "moveset.json"
ARCHIVE = ROOT / "decrypted" / "tkdata" / "mothead" / "mask" / "xiarotsm.arc"
sys.path.insert(0, str(ROOT / "t7extract"))
from t7extract.motion import decode_motion
EVENTS = {0x8435: ("camera", 4, 4), 0x84C2: ("face", 2, 90),
          0x842F: ("right_hand", 1, 19), 0x842E: ("left_hand", 1, 19)}


def main():
    moveset = json.loads(MOVESET.read_text(encoding="utf-8"))
    archive = ARCHIVE.read_bytes()
    results = []
    for move in moveset["moves"]:
        stem = move["animation"].lower()
        if not stem.startswith(("fu_s", "fu_w")):
            continue
        frames = move["anim"]["frames"]
        assert move["anim"]["bank"] == "character"
        events = []
        for prop in move.get("properties", []):
            if prop["id"] not in EVENTS:
                continue
            kind, bank, expected_channels = EVENTS[prop["id"]]
            reference = prop["value"]
            assert reference & 0xC000 == 0x4000, (move["id"], kind, "reference kind")
            index = reference & 0x3FFF
            offset, size = struct.unpack_from("<II", archive, 4 + bank * 8)
            assert archive[offset:offset + 4] == b"MOTA"
            count, = struct.unpack_from(">I", archive, offset + 12)
            assert index < count
            relative, = struct.unpack_from(">I", archive, offset + 20 + index * 4)
            start = offset + relative
            offsets = struct.unpack_from(f">{count}I", archive, offset + 20)
            next_offsets = [value for value in offsets if value > relative]
            stop = offset + (min(next_offsets) if next_offsets else size)
            fmt, channels = struct.unpack_from(">HH", archive, start)
            assert channels == expected_channels, (move["id"], kind, channels)
            if fmt == 0xC8:
                track_frames, = struct.unpack_from(">I", archive, start + 4)
                payload_start = start + 8 + channels * 4
                payload_end = payload_start + track_frames * channels * 12
                values = struct.unpack_from(f">{track_frames * channels * 3}f", archive, payload_start)
                assert all(math.isfinite(value) for value in values), (move["id"], kind)
            elif fmt == 0x64:
                track_frames, = struct.unpack_from(">H", archive, start + 4 + channels * 2)
                payload_end = stop
            else:
                raise ValueError((move["id"], kind, hex(fmt)))
            assert track_frames == frames, (move["id"], kind, track_frames, frames)
            assert payload_end <= offset + size
            decoded = decode_motion(archive[start:payload_end], ">")
            samples = decoded.euler_samples()
            assert len(samples) == frames, (move["id"], kind, "decoded frames")
            assert all(math.isfinite(value) for frame in samples for sample in frame for value in sample), (
                move["id"], kind, "nonfinite decoded sample")
            events.append({"kind": kind, "bank": bank, "index": index,
                           "format": hex(fmt), "channels": channels, "frames": track_frames,
                           "propertyFrame": prop["frame"],
                           "sourceSha256": hashlib.sha256(archive[start:payload_end]).hexdigest(),
                           "decoded": True})
        results.append({"moveId": move["id"], "animation": move["animation"],
                        "kind": "entrance" if stem.startswith("fu_s") else "victory",
                        "body": move["anim"], "events": events})
    assert len(results) == 9
    report = {"character": "Xiaoyu", "code": "xia", "animationPrefix": "fu_",
              "movesetSha256": hashlib.sha256(MOVESET.read_bytes()).hexdigest(),
              "archiveSha256": hashlib.sha256(archive).hexdigest(), "actions": results,
              "limits": ["Source references do not establish Unity rig or visual compatibility."]}
    (HERE / "staging-source-validation.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(f"Validated {len(results)} actions and {sum(len(a['events']) for a in results)} references")


if __name__ == "__main__":
    main()
