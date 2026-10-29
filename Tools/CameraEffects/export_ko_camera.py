"""Recovers Tekken 7's knockout presentation (game speed, KO camera and KO shake) from the original executable
into Resources/CameraEffects/catalog.json ("koCamera", "tradeCamera").

Native path (executable SHA-256 pinned below; addresses are this build only):
  0x1406DE720  fight state (state 4 of 0x1406E2530, jump table 0x1406E2788). When the round is decided by a KO
               it records the result (0x1406E9C50: game +0x260 = 1 when the winner's fighter +0xC02 is set) and:
    0x1406DED0B  slow table entry 2 = {speed, frames} (0x140740A30): speed 0x1434DF5FC; frames 0x1434DF600, or
                 0x1434DF604 when game +0x260 is set. The slow table (0x1403F8580) is ticked once per frame
                 (0x140741F20): the first entry with frames left sets the game speed in percent, and a fighter's
                 per-frame advance is multiplied by speed / 100 (0x140758790). Speed 1 is a near freeze.
    0x1406DEF35  camera mode 7 (manager +0x188, vtable 0x1428BBD80) set up by 0x140700B50 with blend-in
                 0x1434DF608, hold 0x1434DF60C, blend-out 0x1434DF610 frames and FOV 0x1434DF614: it copies the
                 current fight view, replaces its FOV, and its update (0x1406EF580) weights it against the live
                 fight view: frame/in, then 1, then 1 - (frame - in - hold)/out, one frame per camera update.
                 With game +0x260 set the game instead runs camera mode 8 (0x1407008A0), an orbiting shot.
    0x1406DEF4B  shake preset 6 (0x1406FF050), on every such KO.
  0x140713DD0  hit resolution: a trade (both fighters struck on the same frame, either for at least 17 damage) uses
               the same slow table entry, a shake and camera mode 7 with its own settings (catalog "tradeCamera").
  0x140719CBA  move property 0x801B (Rage Drive, the "_EX" moves) sets fighter +0xC02 (value != 0) or clears it.
  0x140701A3D  static initializer of the 0x1434DF5C0.. settings; nothing else writes the ones read here.
The FOV is the horizontal FOV in degrees: a read-only capture of the running game recorded 65 in normal play
and 35 in 8 of 120 samples (Tools/EntranceCamera/gameplay-camera-capture.json).

    python Tools/CameraEffects/export_ko_camera.py --workspace ../..
"""
import argparse
import hashlib
import json
import re
import struct
import sys
from pathlib import Path

EXE_SHA256 = "f46f9ff526fe5d00a0d1ee52126f61628cdc13c402264e2764e4010d3b9c1892"
SETTINGS_INIT, SETTINGS_INIT_END = 0x140701A3D, 0x140701B23
SLOW_SPEED, SLOW_FRAMES, DRIVE_SLOW_FRAMES = 0x1434DF5FC, 0x1434DF600, 0x1434DF604
BLEND_IN, HOLD, BLEND_OUT, FOV = 0x1434DF608, 0x1434DF60C, 0x1434DF610, 0x1434DF614
GETTERS = {0x1406FCE50: SLOW_SPEED, 0x1406FCE70: SLOW_FRAMES, 0x1406FCE80: DRIVE_SLOW_FRAMES,
           0x1406FD010: BLEND_IN, 0x1406FD030: HOLD, 0x1406FD050: BLEND_OUT, 0x1406FCFF0: FOV}
FIGHT_STATE = 0x1406DE720
SLOW_CALL, MODE7_CALL, SHAKE_CALL = 0x1406DED0B, 0x1406DEF35, 0x1406DEF4B
DRIVE_FLAG = 0x1406EA26D
MODE7_UPDATE = 0x1406EF580
DRIVE_PROPERTY, DRIVE_CASE = 0x801B, 0x140719CBA


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
        raise SystemExit("Executable differs from the inspected build; rediscover the KO camera first.")

    def rip(i):
        m = re.search(r"rip ([+-]) (0x[0-9a-f]+)", i["args"])
        nxt = int(i["va"], 16) + len(bytes.fromhex(i["bytes"]))
        return nxt + (int(m.group(2), 16) if m.group(1) == "+" else -int(m.group(2), 16))

    def text(lo, hi):
        return " | ".join(f"{i['op']} {i['args']}" for i in im.instructions(lo, hi))

    def expect(lo, hi, *parts):
        body = text(lo, hi)
        for part in parts:
            if part not in body:
                raise SystemExit(f"Code at {lo:#x} changed ({part!r} missing): {body}")

    # Each setting has one writer, the static initializer, and one getter (mov eax, [rip + setting]; ret).
    values = {}
    for i in im.instructions(SETTINGS_INIT, SETTINGS_INIT_END):
        if i["op"] == "mov" and i["args"].startswith("dword ptr [rip"):
            values[rip(i)] = int(i["args"].split(",")[1], 16)
    for getter, setting in GETTERS.items():
        i = im.instructions(getter, getter + 8)[0]
        if i["op"] != "mov" or rip(i) != setting:
            raise SystemExit(f"Getter {getter:#x} no longer reads {setting:#x}")

    # The fight state's KO branch: slow table entry 2 = {speed, frames}, camera mode 7, shake preset 6.
    expect(0x1406DECDC, SLOW_CALL + 5, "call 0x1406fce70", "call 0x1406fce80", "call 0x1406fce50",
           "mov edx, 2", "call 0x140740a30")
    expect(0x1406DEEDF, MODE7_CALL + 5, "mov edx, 7", "call 0x1406fcff0", "call 0x1406fd050",
           "call 0x1406fd030", "call 0x1406fd010", "mov dword ptr [rsp + 0x38], 2", "call 0x140700b50")
    shake = im.instructions(SHAKE_CALL - 12, SHAKE_CALL + 5)
    if shake[-1]["args"] != "0x1406ff050" or not any(i["args"] == "edx, 6" for i in shake):
        raise SystemExit("KO shake call changed: " + text(SHAKE_CALL - 12, SHAKE_CALL + 5))
    expect(0x140740A30, 0x140740A3D, "dword ptr [rcx + rax*8], r8d", "dword ptr [rcx + rax*8 + 4], r9d")
    expect(0x140758790, 0x14075883A, "call 0x1406fbf60", "mov eax, 0x51eb851f", "sar edx, 5")
    expect(MODE7_UPDATE, MODE7_UPDATE + 0x2D0, "add dword ptr [rdi + 0x1e0], 0x1000", "dword ptr [rdi + 0x1e8]",
           "dword ptr [rdi + 0x1ec]", "dword ptr [rdi + 0x1f0]", "call 0x1406e7300")
    # Rage Drive: the 0x801B case sets the winner's +0xC02, which selects the longer slow-down and mode 8.
    table = im.base + struct.unpack_from("<I", im.data, im.offset(im.base + 0x71FC98) + 4 * (DRIVE_PROPERTY - 0x8001))[0]
    if table != DRIVE_CASE:
        raise SystemExit(f"0x801B case moved to {table:#x}")
    expect(DRIVE_CASE, DRIVE_CASE + 0x120, "mov word ptr [r14 + 0xc02], 1", "mov word ptr [r14 + 0xc02], 0")
    expect(DRIVE_FLAG, DRIVE_FLAG + 0x2C, "cmp byte ptr [r14 + 0xc02], 0", "mov byte ptr [rax + 0x260], 1")

    # Rage Drive KO: camera mode 8 (manager +0x190, vtable 0x1428BBDD8). The fight state picks variant 0 or 1 at
    # random (0x1406DEE72..0x1406DEED8) and calls setup 0x1407008A0, which fills the shot and calls 0x140700580.
    def one(va):
        return im.instructions(va, va + 16)[0]

    def ripf(va):
        return struct.unpack_from("<f", im.data, im.offset(rip(one(va))))[0]

    def immf(va, field):
        i = one(va)
        if not i["args"].startswith(f"dword ptr [{'rcx' if 'rcx' in i['args'] else 'rbx'} + {field}],"):
            raise SystemExit(f"Mode 8 setup changed at {va:#x}: {i['op']} {i['args']}")
        return struct.unpack("<f", struct.pack("<I", int(i["args"].split(",")[1], 16)))[0]

    def immi(va, field):
        i = one(va)
        if field not in i["args"]:
            raise SystemExit(f"Mode 8 setup changed at {va:#x}: {i['op']} {i['args']}")
        return int(i["args"].split(",")[1], 16)

    expect(0x1406DEE53, 0x1406DEEDD, "mov edx, 7", "call qword ptr [rdx + 0x18]", "imul ecx, dword ptr [rax], 0x21131993",
           "call 0x1407008a0")
    expect(0x1407008A0, 0x1407008E0, "sub edx, 1", "je 0x140700a91")
    expect(0x140700580, 0x1407006C6, "mov dword ptr [rbx + 0x204], ecx", "mov qword ptr [rbx + 0x228], rcx",
           "cmp dword ptr [rcx + 0x14d4], 1", "movss dword ptr [rbx + 0x194], xmm0", "movss dword ptr [rbx + 0x190], xmm7",
           "movss dword ptr [rbx + 0x20c], xmm9", "movss dword ptr [rbx + 0x214], xmm7", "call 0x1406f3bf0")
    expect(0x1406EF2C0, 0x1406EF510, "dword ptr [rdi + 0x1f8]", "call 0x1406f3bf0", "call 0x1406e7300")
    expect(0x1406F3BF0, 0x1406F3F50, "mulss xmm10, dword ptr [rbx + 0x208]", "addss xmm9, dword ptr [rbx + 0x1fc]",
           "movss xmm0, dword ptr [rbx + 0x200]", "cmp eax, 3")
    expect(0x1406EBC91, 0x1406EBCEA, "mov dword ptr [rax + 0x268], ebx")
    frames = immi(0x1407008E7, "0x1ec")
    if frames != immi(0x1407008FC, "0x1f4") or frames != immi(0x140700AA3, "0x1ec"):
        raise SystemExit("Mode 8 KO length changed")
    drive = {
        "frames": frames, "distanceMm": immf(0x1407008DD, "0x1fc"), "heightMm": immf(0x1407008AA, "0x200"),
        "startFov": ripf(0x140700B2E), "rollDegrees": round(ripf(0x140700B23) * 180 / 3.141592653589793, 3),
        "easeDegrees": round(ripf(0x1406F3C53) * 180 / 3.141592653589793, 3),
        "variants": [
            {"centre": "fighter", "startAngle": round(ripf(0x140700952) * 180 / 3.141592653589793, 3),
             "endAngle": round(immf(0x140700920, "0x210") * 180 / 3.141592653589793, 3), "endFov": immf(0x14070092A, "0x218")},
            {"centre": "opponent", "startAngle": round(ripf(0x140700B0E) * 180 / 3.141592653589793, 3),
             "endAngle": round(immf(0x140700ADC, "0x210") * 180 / 3.141592653589793, 3), "endFov": immf(0x140700AE6, "0x218")}],
    }
    if immf(0x140700A93, "0x1fc") != drive["distanceMm"]:
        raise SystemExit("Mode 8 KO variants disagree on distance")

    ko = {
        "slowSpeedPercent": values[SLOW_SPEED], "slowFrames": values[SLOW_FRAMES],
        "rageDriveSlowFrames": values[DRIVE_SLOW_FRAMES], "blendInFrames": values[BLEND_IN],
        "holdFrames": values[HOLD], "blendOutFrames": values[BLEND_OUT], "horizontalFov": float(values[FOV]),
        "shakePreset": 6, "rageDriveProperty": DRIVE_PROPERTY, "rageDrive": drive,
    }
    # Trade (both fighters hit on the same frame), hit resolution 0x140713DD0: when each fighter took a hit this frame
    # (+0x7CC > 0, struck: +0x754 & 0x20, not in a throw) and either took at least the threshold, slow table entry 2 =
    # {speed, frames}, shake preset 2 + level, and camera mode 7 with its own blend frames and FOV.
    trade_settings = {0x1406FCE40: 0x1434DF5D0, 0x1406FCE60: 0x1434DF5D4, 0x1406FCE30: 0x1434DF5D8, 0x1406FCE20: 0x1434DF5DC,
                      0x1406FD000: 0x1434DF5E0, 0x1406FD020: 0x1434DF5E4, 0x1406FD040: 0x1434DF5E8, 0x1406FCFE0: 0x1434DF5EC}
    for getter, setting in trade_settings.items():
        i = im.instructions(getter, getter + 8)[0]
        if i["op"] != "mov" or rip(i) != setting:
            raise SystemExit(f"Getter {getter:#x} no longer reads {setting:#x}")
    expect(0x1407147B0, 0x14071489B, "cmp dword ptr [rax + 0x7cc], 0", "test byte ptr [rax + 0x754], 0x20",
           "mov byte ptr [rsp + rax + 0xb0], 1", "and ecx, 0x220")
    expect(0x140714988, 0x140714A3A, "mov byte ptr [rsi + 0xd7d], 1", "call 0x1406fce60", "call 0x1406fce30",
           "cmp dword ptr [rcx + 0x7cc], eax", "call 0x1406fce40", "mov edx, 2", "call 0x140740a30", "call 0x1406fce20")
    expect(0x140714B4F, 0x140714BE6, "mov edx, 5", "mov edx, 4", "mov edx, 3", "call 0x1406ff050", "call 0x1406fcfe0",
           "call 0x1406fd040", "call 0x1406fd020", "call 0x1406fd000", "mov edx, 7", "call 0x140700b50")
    level = values[0x1434DF5DC]
    if level not in (1, 2, 3):
        raise SystemExit(f"Trade shake level {level} has no preset branch")
    trade = {
        "slowSpeedPercent": values[0x1434DF5D0], "slowFrames": values[0x1434DF5D4], "damageThreshold": values[0x1434DF5D8],
        "shakePreset": 2 + level, "blendInFrames": values[0x1434DF5E0], "holdFrames": values[0x1434DF5E4],
        "blendOutFrames": values[0x1434DF5E8], "horizontalFov": float(values[0x1434DF5EC]),
        "evidence": "0x140713DD0: both fighters struck on the same frame (+0x7CC damage taken > 0 with +0x754 & 0x20, or both "
                    "+0x754 & 0x220; a fighter in a throw, +0x314 < 0, cancels it); either damage >= damageThreshold -> slow "
                    "table entry 2 (0x140714A12), shake 2 + 0x1434DF5DC (0x140714B78), camera mode 7 (0x140714BE1). The KO "
                    "uses the same slow entry and mode 7 later in the frame, so a KO replaces it.",
    }

    catalog = json.loads(a.catalog.read_text(encoding="utf-8"))
    catalog["tradeCamera"] = trade
    catalog["koCamera"] = dict(ko, evidence={
        "executableSha256": sha, "fightState": "0x1406DE720 (state 4 of 0x1406E2530)",
        "slowTable": "0x140740A30 entry 2; ticked by 0x140741F20; fighter advance x speed/100 at 0x140758790",
        "camera": "mode 7 (0x140700B50 setup, 0x1406EF580 update): copy of the current view, FOV replaced, "
                  "weighted against the live view by blend-in / hold / blend-out frames",
        "shake": "preset 6 at 0x1406DEF4B", "rageDrive": "0x801B -> fighter +0xC02 (0x140719CBA) -> game +0x260 (0x1406EA292)",
        "settings": "static initializer 0x140701A3D (0x1434DF5FC..0x1434DF614)",
        "fovUnits": "horizontal degrees: live capture 65 normal, 35 in 8 of 120 samples",
        "rageDriveCamera": "mode 8, variant 0 or 1 at random (LCG 0x1406E3720). Setup 0x1407008A0 / 0x140700580: centre "
                           "fighter C = P1, or the match winner (game +0x268, 0x1406EBCE4) on the deciding KO; variant 1 "
                           "uses C's opponent (+0x1548). sign = -1 when C +0x14D4 == 1, else 1; roll (+0x194) = sign * "
                           "rollDegrees. Update 0x1406EF2C0 holds it at full weight for its frames (no blend), then cuts "
                           "back. Each frame 0x1406F3BF0: s = sin(easeDegrees * t) / sin(easeDegrees), t = frame / frames; "
                           "FOV = lerp(startFov, endFov, s); angle = lerp(startAngle, endAngle, s) * sign; position = C + "
                           "R_y(angle) * (horizontal unit C->opponent) * (|C->opponent| + distanceMm), height heightMm above "
                           "C's base (+0x1B0); target = the live fight camera's target (view 1 +0x10). Millimetres, y up.",
        "notWired": "no KO camera when the KO comes from a throw (fighter +0x314) or either fighter is flagged +0x490 & 2; "
                    "the alternative mode 7 call when game +0x22C is set."})
    a.catalog.write_text(json.dumps(catalog, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(ko))
    print(json.dumps({k: v for k, v in trade.items() if k != "evidence"}))


if __name__ == "__main__":
    main()
