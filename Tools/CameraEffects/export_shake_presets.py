"""Recovers Tekken 7's battle camera shake presets from the original executable into
Resources/CameraEffects/catalog.json ("shakePresets", "shakeUnitRadians", "shakeEvidence").

Native path (executable SHA-256 pinned below; addresses are this build only):
  0x1406FF050  shake setter: stores the preset in three camera objects and resets their frame
               (move properties 0x8001-0x8004 / 0x8006-0x8009 call it with presets 3-6).
  0x1406EDCE0  camB update (vtable 0x1428BBFA0, slot 1): while the preset at +0x158 is nonzero it
               calls the evaluator with the frame at +0x154, clears the preset when it returns -1,
               otherwise advances the frame (once per 60 Hz battle tick).
  0x140701FD0  evaluator: builds ten per-frame tables on the stack from .rdata constants, switches
               on preset-1 (jump table 0x1407024C0) to (table, length). frame >= length: return -1;
               frame < length-1 and value v != 0: pitch(eye->target) += 2*v*K with
               K = 2*pi/131072 (so one unit = 2*pi/65536 rad), target height rebuilt with tan.
               Pitch only: no lateral or positional motion.
The tables are recovered by replaying the evaluator's own constant loads, its 5x2-vector scale
loop and its scalar scale loop, then reading each case's (table, length) from the jump table.

    python Tools/CameraEffects/export_shake_presets.py --workspace ../..
"""
import argparse
import hashlib
import json
import math
import re
import struct
import sys
from pathlib import Path

EXE_SHA256 = "f46f9ff526fe5d00a0d1ee52126f61628cdc13c402264e2764e4010d3b9c1892"
EVALUATOR, SWITCH, JUMP_TABLE, SCALE_K = 0x140701FD0, 0x140702347, 0x1407024C0, 0x140702434 + 0x21B9C74
RBP = 0x100  # push rbp; lea rbp, [rsp - 0x2f0]; sub rsp, 0x3f0  ->  rbp = rsp + 0x100


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--workspace", type=Path, required=True)
    p.add_argument("--catalog", type=Path, default=Path(__file__).resolve().parents[2] / "Assets/_Project/Resources/CameraEffects/catalog.json")
    a = p.parse_args()
    sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "CharacterEffects"))
    from dispatch_native import Image
    exe = a.workspace / "TEKKEN.7/TekkenGame/Binaries/Win64/TekkenGame-Win64-Shipping.exe"
    im = Image(exe)
    sha = hashlib.sha256(im.data).hexdigest()
    if sha != EXE_SHA256:
        raise SystemExit("Executable differs from the inspected build; rediscover the shake evaluator first.")

    def rd(va, n):
        o = im.offset(va)
        return bytes(im.data[o:o + n])

    def rip(i):
        m = re.search(r"rip ([+-]) (0x[0-9a-f]+)", i["args"])
        nxt = int(i["va"], 16) + len(bytes.fromhex(i["bytes"]))
        return nxt + (int(m.group(2), 16) if m.group(1) == "+" else -int(m.group(2), 16))

    def addr(s):
        m = re.search(r"\[(rbp|rsp)(?: ([+-]) (0x[0-9a-f]+))?\]", s)
        off = int(m.group(3), 16) if m.group(3) else 0
        return (RBP if m.group(1) == "rbp" else 0) + (-off if m.group(2) == "-" else off)

    mem, xmm = {}, {}

    def store(at, data):
        for k, v in enumerate(data):
            mem[at + k] = v

    def load(at, n):
        return bytes(mem.get(at + k, 0) for k in range(n))

    scaled = False
    for i in im.instructions(EVALUATOR, SWITCH):
        va, op, args = int(i["va"], 16), i["op"], i["args"]
        if 0x140702082 <= va <= 0x1407020CD:
            if not scaled:
                # mulps loop: ten vectors from rbp-0x50 scaled by xmm2; the scalar loop that follows
                # scales two zeros at rbp+0x50 and changes nothing.
                k = struct.unpack("<4f", xmm["xmm2"])
                for j in range(10):
                    at = RBP - 0x50 + 16 * j
                    store(at, struct.pack("<4f", *[x * y for x, y in zip(struct.unpack("<4f", load(at, 16)), k)]))
                scaled = True
            continue
        dst = args.split(",")[0].strip()
        src = args.split(",")[1].strip() if "," in args else ""
        if op in ("movaps", "movups") and dst.startswith("xmm") and "rip" in src:
            xmm[dst] = rd(rip(i), 16)
        elif op == "movss" and dst.startswith("xmm") and "rip" in src:
            xmm[dst] = rd(rip(i), 4) + bytes(12)
        elif op in ("movaps", "movups") and dst.startswith("xmmword ptr ["):
            store(addr(dst), xmm[src])
        elif op == "movss" and dst.startswith("dword ptr ["):
            store(addr(dst), xmm[src][:4])
        elif op == "mov" and dst.startswith(("qword ptr [rbp", "qword ptr [rsp")):
            store(addr(dst), bytes(8))  # rcx is 0 after the scale loop
        elif op == "mov" and dst.startswith(("dword ptr [rbp", "dword ptr [rsp")):
            store(addr(dst), bytes(4))
        elif op == "xorps":
            xmm[dst] = bytes(16)

    presets = []
    for k in range(10):
        case = struct.unpack("<i", rd(JUMP_TABLE + 4 * k, 4))[0] + im.base
        length, table = None, None
        for b in im.instructions(case, case + 0x20):
            if b["op"] == "mov" and b["args"].startswith("eax,"):
                length = int(b["args"].split(",")[1], 16)
            if b["op"] == "lea" and b["args"].startswith("rcx,"):
                table = addr(b["args"])
            if b["op"] == "jmp":
                if length is None:
                    length = 6  # shared tail: mov eax, 6
                break
        values = list(struct.unpack(f"<{length}f", load(table, 4 * length)))
        presets.append({"preset": k + 1, "frames": length, "values": [round(v, 4) for v in values]})

    k = struct.unpack("<f", rd(SCALE_K, 4))[0]
    if abs(2 * k - 2 * math.pi / 65536) > 1e-9:
        raise SystemExit(f"Unexpected angle scale {k}")
    catalog = json.loads(a.catalog.read_text(encoding="utf-8"))
    catalog["shakeUnitRadians"] = 2 * math.pi / 65536
    catalog["shakePresets"] = presets
    catalog["shakeEvidence"] = {
        "executableSha256": sha, "setter": "0x1406FF050", "update": "0x1406EDCE0 (camB vtable 0x1428BBFA0 slot 1)",
        "evaluator": "0x140701FD0", "jumpTable": "0x1407024C0",
        "rule": "frame f (60 Hz battle tick, 0 at the setter): f >= frames ends the shake; f < frames-1 adds values[f] * shakeUnitRadians to the eye->target pitch (positive looks up). A new shake replaces the current one and restarts at frame 0.",
        "nativeCallers": "Move properties 0x8001-4/0x8006-9 -> presets 3-6 and 0x8005/0x800A -> min(value, 9) (interpreter case 0x14071999B; 0 stops the shake) (wired). Native requests not wired (event identity unverified): preset 6 at 0x1406DEF4B, preset 4 at 0x140711298, preset 3 at 0x140714B78 and 0x1407911C2."}
    a.catalog.write_text(json.dumps(catalog, indent=2) + "\n", encoding="utf-8")
    for row in presets:
        peak = max(abs(v) for v in row["values"]) * 360 / 65536
        print(f"preset {row['preset']:2d}: {row['frames']:2d} frames, peak {peak:.2f} deg")


if __name__ == "__main__":
    main()
