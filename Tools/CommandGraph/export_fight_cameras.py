"""Export the cinematic camera tracks a fighter's moves reference (Rage Art cameras).

Move properties 0x8435-0x8438, 0x8439/0x843A (left/right screen side) and 0x8445-0x8449 name a
track in bank 4 of mask/<chr>rotsm.arc when (value & 0xC000) == 0x4000. Tracks are 0xC8 MOTA
(big-endian floats). Written to Assets/_Project/Resources/FightCameras/<chr>_<track>.bytes as
"T7CF", frames, channels, channel descriptors, then frames x channels x 3 little-endian floats in
native units (millimetres, radians). Rage Art tracks use descriptors (11, 11, 15, 3, 21): look-at
point, camera position, lens (horizontal FOV), and a blend back to the gameplay camera.
"""
import argparse
import json
import struct
from pathlib import Path

CAMERA_PROPERTIES = {0x8435, 0x8436, 0x8437, 0x8438, 0x8439, 0x843A, 0x8445, 0x8448, 0x8449}
# Look-at tracks (Rage Art cameras). Position/Euler tracks are entrance, victory and story
# cameras, exported by Tools/SecondaryMotion with their body actions instead.
LOOK_AT = (11, 11, 15, 3, 21)


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--graph", required=True, help="exported command graph JSON")
    p.add_argument("--archive", required=True, help="mask/<chr>rotsm.arc")
    p.add_argument("--code", required=True)
    p.add_argument("--out", required=True)
    a = p.parse_args()
    graph = json.loads(Path(a.graph).read_text(encoding="utf-8"))
    raw = Path(a.archive).read_bytes()
    offset, size = struct.unpack_from("<II", raw, 4 + 4 * 8)
    if raw[offset:offset + 4] != b"MOTA":
        raise SystemExit("bank 4 is not a MOTA bank")
    count = struct.unpack_from(">I", raw, offset + 12)[0]
    tracks = set()
    for move in graph["moves"]:
        props = move["properties"]
        for i in range(0, len(props), 3):
            if props[i + 1] in CAMERA_PROPERTIES and props[i + 2] & 0xC000 == 0x4000:
                tracks.add(props[i + 2] & 0x3FFF)
    out = Path(a.out)
    out.mkdir(parents=True, exist_ok=True)
    for index in sorted(tracks):
        if index >= count:
            raise SystemExit(f"track {index} beyond bank count {count}")
        start = offset + struct.unpack_from(">I", raw, offset + 20 + index * 4)[0]
        kind, channels, frames = struct.unpack_from(">HHI", raw, start)
        if kind != 0xC8:
            raise SystemExit(f"track {index}: unsupported format {kind:#x}")
        descriptors = struct.unpack_from(f">{channels}I", raw, start + 8)
        if descriptors != LOOK_AT:
            continue
        data = start + 8 + channels * 4
        if data + frames * channels * 12 > offset + size:
            raise SystemExit(f"track {index}: truncated")
        values = struct.unpack_from(f">{frames * channels * 3}f", raw, data)
        target = out / f"{a.code}_{index}.bytes"
        target.write_bytes(struct.pack("<4sII", b"T7CF", frames, channels) + struct.pack(f"<{channels}I", *descriptors)
                           + struct.pack(f"<{len(values)}f", *values))
        print(f"{target.name}: {frames} frames, descriptors {descriptors}")


if __name__ == "__main__":
    main()
