"""Recovers Tekken 7's fight-camera follow rate and the per-fighter camera requests that scale it (move
property 0x84CC) from the original executable into Resources/CameraEffects/catalog.json ("followRate").

0x84CC is not a zoom. Native path (executable SHA-256 pinned below; addresses are this build only):
  0x140719875  interpreter case 0x84CC (small jump table 0x71FC70, cases 0x84C3-0x84CC): stores the
               property value's low 16 bits in the fighter's camera request (+0x36C), unless the fighter's
               +0x314 is set and the request is already 0x8FD (throw escape). The request stays until the
               next 0x84CC (or a throw camera, 0x140722320) replaces it; it is cleared at fighter reset.
  0x1406EE020  fight camera view update (vtable 0x1428BBF50 slot 0, run for both views): looks both
               fighters' requests up in a 7-entry {request, rate} table (static init 0x1406EE7A8, rates are
               the base rate times a constant) and takes the largest match; with no match (< 0.001) the
               target is the base rate. The view's current rate steps toward the target by +0.003 per
               frame or -0.002 per frame (settings mode 0; mode 1 holds the base, mode 2 doubles the steps),
               then the view moves that fraction of the way to its framing goal (0x1406E7300).
  0x140740700  camera settings defaults: base rate (+0x14) = 0.1, mode (+0x18) = 0.

    python Tools/CameraEffects/export_follow_rates.py --workspace ../..
"""
import argparse
import hashlib
import json
import re
import struct
import sys
from pathlib import Path

EXE_SHA256 = "f46f9ff526fe5d00a0d1ee52126f61628cdc13c402264e2764e4010d3b9c1892"
CASE_TABLE, CASE_FIRST, CASE_PROPERTY = 0x71FC70, 0x84C3, 0x84CC
TABLE_INIT, TABLE_INIT_END = 0x1406EE7C1, 0x1406EE833
LOOKUP = 0x1406EE344          # lea rbx, [rip + ...] -> first rate; the request sits 4 bytes before it
RISE, FALL, MINIMUM = 0x1406EE3C8, 0x1406EE3DD, 0x1406EE38F
DEFAULTS = 0x140740700


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
        raise SystemExit("Executable differs from the inspected build; rediscover the camera follow rate first.")

    def f32(va):
        return struct.unpack_from("<f", im.data, im.offset(va))[0]

    def rip(i):
        m = re.search(r"rip ([+-]) (0x[0-9a-f]+)", i["args"])
        nxt = int(i["va"], 16) + len(bytes.fromhex(i["bytes"]))
        return nxt + (int(m.group(2), 16) if m.group(1) == "+" else -int(m.group(2), 16))

    def first(va, op):
        i = im.instructions(va, va + 16)[0]
        if i["op"] != op:
            raise SystemExit(f"Unexpected instruction at {va:#x}: {i['op']} {i['args']}")
        return i

    # The 0x84CC case: request = low 16 bits of the value, kept while a throw escape (0x8FD) is guarded.
    case = im.base + struct.unpack_from("<I", im.data, im.offset(im.base + CASE_TABLE) + 4 * (CASE_PROPERTY - CASE_FIRST))[0]
    body = " | ".join(f"{i['op']} {i['args']}" for i in im.instructions(case, case + 0x22))
    for part in ("dword ptr [rcx + 0x314], 0", "dword ptr [rcx + 0x36c], 0x8fd", "movzx eax, bx", "mov dword ptr [rcx + 0x36c], eax"):
        if part not in body:
            raise SystemExit(f"0x84CC case changed ({part!r} missing): {body}")

    # Rates: replay the static initialiser in units of the base rate (xmm7 = base).
    xmm, rates = {}, {}
    for i in im.instructions(TABLE_INIT, TABLE_INIT_END):
        dst, src = (s.strip() for s in (i["args"].split(",") + [""])[:2])
        if i["op"] == "movaps" and src == "xmm7":
            xmm[dst] = 1.0
        elif i["op"] == "mulss" and "rip" in src:
            xmm[dst] *= f32(rip(i))
        elif i["op"] == "addss" and src == "xmm7":
            xmm[dst] += 1.0
        elif i["op"] == "movss" and dst.startswith("dword ptr [rip") and src == "xmm7":
            rates[rip(i)] = 1.0
        elif i["op"] == "movss" and dst.startswith("dword ptr [rip"):
            rates[rip(i)] = xmm[src]
    table = rip(first(LOOKUP, "lea"))
    scales = []
    for k in range(7):
        request = struct.unpack_from("<i", im.data, im.offset(table - 4 + 8 * k))[0]
        scales.append({"request": request, "scale": round(rates[table + 8 * k], 6)})

    defaults = im.instructions(DEFAULTS, DEFAULTS + 0x50)
    base = next(f32(rip(defaults[k - 1])) for k, i in enumerate(defaults) if i["args"] == "dword ptr [rax + 0x14], xmm0")
    mode = next(i["args"].split(",")[1].strip() for i in defaults if i["args"].startswith("dword ptr [rax + 0x18],"))
    if mode != "r8d":
        raise SystemExit("Camera settings mode default changed")
    rise, fall, minimum = f32(rip(first(RISE, "mulss"))), f32(rip(first(FALL, "mulss"))), f32(rip(first(MINIMUM, "comiss")))

    catalog = json.loads(a.catalog.read_text(encoding="utf-8"))
    catalog["followRate"] = {
        "baseRate": round(base, 6), "risePerFrame": round(rise, 6), "fallPerFrame": round(fall, 6),
        "minimumMatch": round(minimum, 6), "requestProperty": CASE_PROPERTY, "throwEscapeRequest": 0x8FD,
        "scales": scales,
        "evidence": {
            "executableSha256": sha, "propertyCase": hex(case), "viewUpdate": "0x1406EE020 (vtable 0x1428BBF50 slot 0)",
            "rateTableInit": "0x1406EE7A8", "defaults": "0x140740700 (+0x14 base rate, +0x18 mode 0)",
            "rule": "Each fighter keeps a camera request (low 16 bits of the latest 0x84CC value; cleared at round reset). Each 60 Hz frame the target rate is baseRate * the largest scale whose request either fighter holds (baseRate when none matches); the current rate steps toward it by risePerFrame up or fallPerFrame down, and the camera moves that fraction of the way to its framing goal.",
            "notWired": "Throw cameras (0x140722320) also overwrite the request from the moveset's throw_cameras block; requests 1-6 and 25-41 additionally start a special camera (0x1406E7B00) in battle states 4/21/22; request 0x8FD is kept while fighter +0x314 is set."}}
    a.catalog.write_text(json.dumps(catalog, indent=2) + "\n", encoding="utf-8")
    print(f"base rate {base} per frame, rise {rise}, fall {fall}")
    for row in scales:
        print(f"request {row['request']:3d}: x{row['scale']}")


if __name__ == "__main__":
    main()
