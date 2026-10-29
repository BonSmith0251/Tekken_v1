"""Export Tekken 7's battle sounds (Wwise) for the Unity remaster's versus scene.

The game plays Wwise events from its banks. The sound managers name them:
BP_TekkenCommonSoundManager holds the shared battle events (announcer, hits, guards, falls) and
BP_<code>CharacterSoundManager each fighter's voice list (SoundID + event, in order). Every event
here is Event -> Play action -> Sound (or a random/switch container of sounds) -> one media
file; event IDs are the FNV-1 hash of the lower-case event name, which the tool verifies.

Moves name their voices with property 0x84C4 ("play player sound") / 0x84C6 ("play opponent
sound"): value = category << 24 | index. The common helper moves SE_KIAI_n, SE_YARARE_n and
SE_DEAD_n show the categories: 2 = the n-th attack shout, 3 = the n-th pain cry, 4 = the n-th
death cry of the fighter's own list (sounds.json "voices"); category 12 with index 0x2000 + n is
the fighter's "_item<nn>" line (Alisa's chainsaws and thrusters).

Categories 1, 6, 7 and 8 index the common battle table TONES: the executable's static list of
/Game/Sound/Battle references, in its order (whooshes RES_BANK_WND_*, punches RES_BANK_PCH_*, bone
cracks, sparks, FX_HIT_*). The data fits it throughout: category 6 is only ever WND_MD01/02 on attack
startups, 7 the impacts on reaction moves' first frame, 1 mostly whooshes, guards and falls. Category 15
indexes the common manager's BattleSysTones (Rage Art and Devil Kazuya cues); category 9 (Hit01 / floor)
and 10 stay with the scene's own handling.

Each move also has a sound list (the moveset's voiceclip block, move offset 0x78), in the same encoding:
attacks list their swing (6), impact (7) and shout (2:n, 999 = any); guard reactions list GRD_MD01 (1:26).
Written per fighter to moves_<code>.json when --movesets is given.

Usage (from the project root):
  python -m t7extract unpack <game> "*/WwiseAudio/Windows/AkBank*.bnk" "*/Sound/Common/*" "*/Sound/Character/*" -o <src>
  python -m t7extract audio <src> -o <wav> --format wav
  python Tools/BattleSound/export_battle_sound.py --source <src> --wav <wav>
      [--t7extract <t7> --movesets <decrypted/tkdata/mothead/bin>] [--subtitles <decrypted/tkdata/subtitles>]
Writes Assets/_Project/Resources/BattleSound/<event>[__<n>].wav and sounds.json (events with their
variation files, each fighter's attack / damage / death voice lists in manager order, the tone tables,
and the English text of the entrance and victory lines).
"""
import argparse
import json
import re
import shutil
import time
import sys
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Assets/_Project/Resources/BattleSound"
BANKS = "TekkenGame/Content/WwiseAudio/Windows"
COMMON = "TekkenGame/Content/Sound/Common/BP_TekkenCommonSoundManager.uasset"
CHARACTER = "TekkenGame/Content/Sound/Character/{0}/BP_{0}CharacterSoundManager.uasset"
# Fighter code -> voice bank suffix (the banks keep the game's own casing).
FIGHTERS = {"jin": "Jin", "kaz": "KAZ", "xia": "XIA", "asa": "ASA", "ltn": "LTN", "nsd": "NSD", "bob": "BOB",
            "leo": "LEO"}
# Common battle table for categories 1/6/7/8: the order of the executable's /Game/Sound/Battle references
# (TekkenGame-Win64-Shipping.exe, UTF-16 strings from 0x27EF1D8; interleaved there with old voice paths).
TONES = ["Hit01", "RES_BANK_SYVO_CHICKEN", "syvo_great"] + ["RES_BANK_SYVO_CNT%02d" % i for i in range(11)] + [
    "RES_BANK_PCH_WK02", "RES_BANK_PCH_MD01", "RES_BANK_PCH_SG01", "RES_BANK_PCH_LT01", "RES_BANK_KCK_WK01",
    "RES_BANK_KCK_SG01", "RES_BANK_KCK_LT01", "RES_BANK_PCH_FC01", "RES_BANK_PCH_FC02", "RES_BANK_PCH_FC03",
    "RES_BANK_PCH_LT02", "RES_BANK_PCH_HONE02", "RES_BANK_GRD_MD01", "RES_BANK_DWN_HVY01", "RES_BANK_DWN_HVY02",
    "RES_BANK_BNE_BRK03", "RES_BANK_WND_LT01", "RES_BANK_WND_WK01", "RES_BANK_WND_WK02", "RES_BANK_WND_WK03",
    "RES_BANK_WND_MD01", "RES_BANK_WND_MD02", "RES_BANK_DWN_HVY03", "RES_BANK_BNE_BRK01", "RES_BANK_BNE_BRK02",
    "RES_BANK_BNE_BRK04", "RES_BANK_PCH_HONE01", "RES_BANK_SYVO_OOPS", "RES_BANK_DWMK", "RES_BANK_SPK_L01",
    "RES_BANK_SPK_M01", "RES_BANK_SPK_S01"] + ["FX_HIT_M%02d" % i for i in range(1, 7)] + [
    "FX_HIT_L%02d" % i for i in range(1, 6)] + ["C04YOS_YOS_ROUND"]
# The announcer's name call, when it isn't syvo_name_<code> (Leo's lives in Sound/Voice/leo as syvo_name_uni).
NAME_CALLS = {"leo": "syvo_name_uni"}
SOUND_PROPERTIES = (0x84C4, 0x84C6)
SYSTEM_CATEGORY = 15
GRAPHS = ROOT / "Assets/_Project/Resources/CommandGraph"
# Subtitle lines of the entrance (xx_s##) and victory (xx_w##) takes, from the US English tkdata set.
LINE = re.compile(r"^[a-z]{2}_[sw]\d\d$")
# Shared events the scene plays (announcer, impacts, Rage); RES_BANK_* and FX_HIT_* are added whole.
SHARED = ["Play_Round%d" % i for i in range(1, 10)] + [
    "Play_FinalRound", "Play_Fight", "Play_KO", "Play_DoubleKO", "Play_TimeUp", "Play_Perfect", "Play_Great",
    "Play_Draw", "Play_Win", "Play_Wins", "Hit01", "Guard01", "se_rage", "SE_RageArts_Start",
    "SE_battle_rageattack_start", "SE_battle_rageattack_hit", "SE_Combo_Finish", "SE_ChrSel1", "SE_ChrSel2", "SE_VS1",
    "SE_Select", "SE_Decide", "SE_Cancel",
    "SE_VS2", "SE_VS3"]
SHARED_PREFIXES = ("RES_BANK_PCH", "RES_BANK_KCK", "RES_BANK_WND", "RES_BANK_DWN", "RES_BANK_GRD", "RES_BANK_BNE",
                   "RES_BANK_SPK", "RES_BANK_DWMK", "FX_HIT_")
FOOTSTEPS = "PlayerOne_FootSteps"
FOOTSTEP_VARIANTS = 6        # the footstep switch holds every floor surface; the first ones are kept


def fnv1(name):
    h = 2166136261
    for b in name.lower().encode():
        h = (h * 16777619) & 0xFFFFFFFF
        h ^= b
    return h


def u32(b, o):
    return struct.unpack_from("<I", b, o)[0]


def hirc(path):
    """Wwise bank (version 118): HIRC objects by id as (type, body)."""
    d = path.read_bytes()
    objs, p = {}, 0
    while p < len(d):
        tag, size = d[p:p + 4], u32(d, p + 4)
        if tag == b"HIRC":
            q = p + 12
            for _ in range(u32(d, p + 8)):
                t, sz, oid = d[q], u32(d, q + 1), u32(d, q + 5)
                objs[oid] = (t, d[q + 9:q + 5 + sz])
                q += 5 + sz
        p += 8 + size
    return objs


def sources(objs, oid, seen=None, depth=0):
    """Media ids an object plays: a Sound's source, or every sound under a random/switch/layer container
    (children are found by their parent id, which follows the node's FX block)."""
    seen = set() if seen is None else seen
    if oid in seen or depth > 6:
        return []
    seen.add(oid)
    t, b = objs.get(oid, (None, b""))
    if t == 2:
        return [u32(b, 5)]
    if t in (5, 6, 9):
        out = []
        for cid, (ct, cb) in objs.items():
            if ct in (2, 5, 6, 9) and cid != oid and struct.pack("<I", oid) in cb[:80]:
                out += sources(objs, cid, seen, depth + 1)
        return out
    return []


def event_media(objs, eid):
    t, b = objs[eid]
    media = []
    for k in range(u32(b, 0)):
        at, ab = objs.get(u32(b, 4 + 4 * k), (None, b""))
        if at == 3 and struct.unpack_from("<H", ab, 0)[0] == 0x0403:      # Play
            media += sources(objs, u32(ab, 2))
    return list(dict.fromkeys(media))


def package(path):
    """Names and import object names (with FName numbers) of a cooked package, and its raw bytes."""
    d = path.read_bytes()
    o = 24 + struct.unpack_from("<i", d, 20)[0] * 20 + 4
    o += 4 + struct.unpack_from("<i", d, o)[0] + 4
    ncount, noff = struct.unpack_from("<ii", d, o)
    names, q = [], noff
    for _ in range(ncount):
        n = struct.unpack_from("<i", d, q)[0]
        names.append(d[q + 4:q + 3 + n].decode("latin1"))
        q += 4 + n + 4
    _, _, icount, ioff = struct.unpack_from("<iiii", d, o + 16)
    imports = []
    for k in range(icount):
        f = struct.unpack_from("<iiiiiii", d, ioff + 28 * k)
        imports.append(names[f[5]] + ("_%d" % (f[6] - 1) if f[6] > 0 else ""))
    return d, names, imports


def voice_list(path):
    """A fighter's sound manager: its Sounds array as (SoundID, event name), in order."""
    d, names, imports = package(path)
    sid, ev, intp = names.index("SoundID"), names.index("SoundEvent"), names.index("IntProperty")
    out, p = [], 0
    while True:
        p = d.find(struct.pack("<iiii", sid, 0, intp, 0), p)
        if p < 0:
            return out
        q = d.find(struct.pack("<ii", ev, 0), p)
        ref = struct.unpack_from("<i", d, q + 25)[0]
        out.append((struct.unpack_from("<i", d, p + 25)[0], imports[-ref - 1]))
        p += 1


def object_array(path, prop):
    """An ObjectProperty array of a cooked package's class defaults, as import object names (None for empty)."""
    d, names, imports = package(path)
    tag = struct.pack("<ii", names.index(prop), 0) + struct.pack("<ii", names.index("ArrayProperty"), 0)
    p = d.find(tag)
    if p < 0:
        return []
    p += 16 + 8 + 8 + 1          # size, array index, inner type, HasPropertyGuid
    out = []
    for k in range(struct.unpack_from("<i", d, p)[0]):
        ref = struct.unpack_from("<i", d, p + 4 + 4 * k)[0]
        out.append(imports[-ref - 1] if ref < 0 else None)
    return out


def used_system_tones():
    """BattleSysTones indices that the exported command graphs play (0x84C4/0x84C6 category 15)."""
    used = set()
    for path in GRAPHS.glob("*.json"):
        for move in json.loads(path.read_text(encoding="utf-8")).get("moves", []):
            p = move.get("properties") or []
            for k in range(0, len(p) - 2, 3):
                if p[k + 1] in SOUND_PROPERTIES and p[k + 2] >> 24 == SYSTEM_CATEGORY:
                    used.add(p[k + 2] & 0xFFFFFF)
    return used


def move_lists(code, movesets):
    """A fighter's per-move sound lists (voiceclip block): [{"id": move, "clips": [value, ...]}]."""
    from t7extract.moveset import NONE, parse_moveset
    ms = parse_moveset((Path(movesets) / ("%smot.bin" % code)).read_bytes())
    rows = []
    for move in range(ms.count("moves")):
        ptr = struct.unpack_from("<Q", ms.record("moves", move), 0x78)[0]
        clips = []
        while ptr != NONE and ptr < ms.count("voiceclips"):
            v = struct.unpack("<I", ms.record("voiceclips", ptr))[0]
            if v == 0xFFFFFFFF:
                break
            if v:
                clips.append(v)
            ptr += 1
        if clips:
            rows.append({"id": move, "clips": clips})
    return rows


def subtitles(code, folder):
    """English text of a fighter's entrance and victory lines: {line name: text}."""
    path = Path(folder) / code.upper() / "us" / ("%s_sub.bin" % code.upper())
    if not path.exists():
        return {}
    words = [m.group().decode("utf-8", "replace") for m in re.finditer(rb"[\x20-\x7e\x80-\xff]{2,}", path.read_bytes())]
    out = {}
    for k in range(len(words) - 2):
        if LINE.match(words[k]) and words[k + 1] == words[k] and not LINE.match(words[k + 2]):
            out[words[k]] = words[k + 2]
    return out


def role(event):
    e = event.lower()
    if "dmg" in e or "damage" in e:
        return "damage"
    if "die" in e or "_ko" in e:
        return "death"
    return "attack"


def prune(out, start):
    """Removes outputs this run didn't rewrite (modified before start), with their .meta files."""
    for f in list(out.iterdir()):
        if f.suffix != ".meta" and f.stat().st_mtime < start:
            f.unlink()
            meta = f.with_name(f.name + ".meta")
            if meta.exists():
                meta.unlink()


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--source", required=True, help="folder unpacked with t7extract (contains TekkenGame/)")
    ap.add_argument("--wav", required=True, help="t7extract audio --format wav output for that folder")
    ap.add_argument("--t7extract", help="workspace t7extract folder (needed for --movesets)")
    ap.add_argument("--movesets", help="decrypted tkdata mothead/bin folder: writes the per-move sound lists")
    ap.add_argument("--subtitles", help="decrypted tkdata subtitles folder: adds the entrance/victory line text")
    args = ap.parse_args()
    src, wav = Path(args.source), Path(args.wav)
    media_files = {int(f.stem): f for f in wav.rglob("*.wav")}

    _, _, common = package(src / COMMON)
    system_tones = object_array(src / COMMON, "BattleSysTones")
    used_system = {system_tones[i] for i in used_system_tones() if i < len(system_tones) and system_tones[i]}
    # Fighter machinery lines named <...>_itemNN (Alisa's chainsaws, thrusters): property 0x84C4 category 12,
    # index 0x2000 + NN.
    wanted = {n for n in common if n in SHARED or n.startswith(SHARED_PREFIXES) or re.search(r"_item\d+$", n)}
    wanted |= {NAME_CALLS.get(c, "syvo_name_" + c) for c in FIGHTERS} | {FOOTSTEPS} | set(TONES) | used_system
    voices, lines = {}, {}
    for code in FIGHTERS:
        listed = voice_list(src / CHARACTER.format(code))
        voices[code] = {r: [e for _, e in listed if role(e) == r] for r in ("attack", "damage", "death")}
        _, _, imports = package(src / CHARACTER.format(code))
        wanted |= set(imports)      # voices, entrance (xx_s##) and victory (xx_w##) lines; non-events match no hash
        # Entrance / victory lines: the manager's Streams, plus the subtitle keys (Lidia's manager still names
        # hers pl_s##, while her bank and subtitles use nd_s##).
        said = subtitles(code, args.subtitles) if args.subtitles else {}
        lines.update(said)
        named = {n for n in imports if LINE.match(n)} | set(said)
        # Every take number for those prefixes too (nd_s00 exists without a subtitle); unexported ones drop out.
        named |= {"%s_%s%02d" % (n[:2], kind, k) for n in named for kind in "sw" for k in range(10)}
        wanted |= named
        voices[code]["lines"] = sorted(named)

    by_hash = {fnv1(n): n for n in wanted}
    banks = ["AkBankBattle", "AkBankFoot"] + ["AkBankVoice" + b for b in FIGHTERS.values()]
    # Files are replaced in place and stale ones pruned at the end, so Unity keeps each clip's .meta (and GUID).
    OUT.mkdir(parents=True, exist_ok=True)
    start = time.time() - 1
    events, missing = {}, []
    for bank in banks:
        objs = hirc(src / BANKS / (bank + ".bnk"))
        for eid, (t, _) in objs.items():
            if t != 4 or eid not in by_hash:
                continue
            name = by_hash[eid]
            media = [m for m in event_media(objs, eid) if m in media_files]
            if name == FOOTSTEPS:
                media = media[:FOOTSTEP_VARIANTS]
            files = []
            for k, m in enumerate(media):
                stem = name if k == 0 else "%s__%d" % (name, k + 1)
                shutil.copyfile(media_files[m], OUT / (stem + ".wav"))
                files.append(stem)
            if files:
                events[name] = files
            else:
                missing.append(name)
    for code in voices:
        for r in voices[code]:
            voices[code][r] = [e for e in voices[code][r] if e in events]
        call = NAME_CALLS.get(code, "syvo_name_" + code)
        voices[code]["name"] = call if call in events else ""
    # Arrays, for Unity's JsonUtility: events [{name, files}], voices [{code, attack, damage, death}],
    # tones / systemTones [event name or "" by index], lines [{name, text}].
    table = {"events": [{"name": n, "files": f} for n, f in sorted(events.items())],
             "voices": [dict(code=c, **v) for c, v in voices.items()],
             "tones": [t if t in events else "" for t in TONES],
             "systemTones": [t if t and t in events else "" for t in system_tones],
             "lines": [{"name": n, "text": t} for n, t in sorted(lines.items())]}
    (OUT / "sounds.json").write_text(json.dumps(table, indent=1), encoding="utf-8")
    if args.movesets:
        sys.path.insert(0, args.t7extract or "")
        for code in FIGHTERS:
            rows = move_lists(code, args.movesets)
            (OUT / ("moves_%s.json" % code)).write_text(json.dumps({"moves": rows}), encoding="utf-8")
            print(code, "moves with sound lists", len(rows))
    prune(OUT, start)
    print("events %d, files %d, without media %d %s" % (len(events), sum(len(v) for v in events.values()),
                                                       len(missing), sorted(missing)[:12]))
    print("tones %d/%d, system tones used %d %s, lines %d" % (sum(1 for t in table["tones"] if t), len(TONES),
          len(used_system), sorted(used_system), len(lines)))
    for code, v in voices.items():
        print(code, {r: len(l) for r, l in v.items()})


if __name__ == "__main__":
    main()
