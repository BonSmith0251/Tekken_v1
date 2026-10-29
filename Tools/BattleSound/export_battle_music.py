"""Export Tekken 7's stage music (Wwise AkBankBGM) for the Unity remaster's versus scene.

In the game, event BGM_Container plays a switch container on the BGM_Selector state. Its values name
every track (stg01_iseki ... stg32_beach, CharaSelect, the older games' T1_bgm_01 ... jukebox; the names
were recovered by FNV-1 hashing strings from the executable and tkdata). A stage's value leads to a switch on
the FinalRound state, Normal or Final, which is how a match changes to the final-round arrangement. The
selector's other targets (the older-game jukebox) live in other banks and aren't exported here.

Usage (from the project root; <t7> = the workspace t7extract folder):
  1. python Tools/BattleSound/export_battle_music.py --t7extract <t7> --source <src> --list-media > media.txt
     (<src> holds TekkenGame/Content/WwiseAudio/Windows/AkBankBGM.bnk, from t7extract unpack)
  2. python -m t7extract unpack <game> --paths-file media.txt -o <src>
     python -m t7extract audio <src> -o <ogg>        (Vorbis -> .ogg, a lossless rebuild)
  3. python Tools/BattleSound/export_battle_music.py --t7extract <t7> --source <src> --audio <ogg>
Writes Assets/_Project/Resources/BattleMusic/<track>__normal.ogg / __final.ogg and music.json.
"""
import argparse
import json
import shutil
import time
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Assets/_Project/Resources/BattleMusic"
BANK = "TekkenGame/Content/WwiseAudio/Windows/AkBankBGM.bnk"
MEDIA = "TekkenGame/Content/WwiseAudio/Windows/{}.wem"
# BGM_Selector values exported: every Tekken 7 stage track (stage code = the prefix before "_") and the
# character select track. stg08a, stg17, stg25 and stg90 have no track of their own.
TRACKS = ["stg00_wanoma", "stg01_iseki", "stg02_jungle", "stg03_russia", "stg04_manhattan", "stg05_bridge",
          "stg06_market", "stg07_volcano", "stg08_misimabldg", "stg09_haidera", "stg10_seidou", "stg11_arena",
          "stg12_Gcorp", "stg12a_Gcorp", "stg13_misimadojo", "stg14_volcano_op", "stg15_cliff", "stg16_lab",
          "stg18_base", "stg19_practice", "stg20_uyuni", "stg21_gimmick", "stg22_grid", "stg23_mry", "stg24_mrz",
          "stg26_jungle_ac", "stg27_manhattan_ac", "stg28_nsa", "stg29_uyuni_vr", "stg30_thai", "stg31_japan",
          "stg32_beach", "CharaSelect"]
SELECTOR_EVENT = "BGM_Container"


def tracks(bank_path):
    """Track name -> {"normal": [media ids], "final": [media ids]} (or {"any": [...]} without a FinalRound switch)."""
    from t7extract.wwise_hirc import Bank, SWITCH, fnv1
    bank = Bank(Path(bank_path).read_bytes())
    selector = next(bank.nodes[t] for t in bank.event_targets(fnv1(SELECTOR_EVENT))
                    if bank.nodes.get(t) and bank.nodes[t].type == SWITCH and bank.nodes[t].group_id == fnv1("BGM_Selector"))
    final_round = fnv1("FinalRound")
    out = {}
    for name in TRACKS:
        variants = {}
        for target in selector.switches.get(fnv1(name), []):
            node = bank.nodes.get(target)
            if node is None:
                continue
            if node.type == SWITCH and node.group_id == final_round:
                for state in ("Normal", "Final"):
                    media = [s.source_id for i in node.switches.get(fnv1(state), []) for s in bank.sounds_under(i)]
                    if media:
                        variants[state.lower()] = media
            else:
                variants["any"] = [s.source_id for s in bank.sounds_under(target)]
        if variants:
            out[name] = variants
    return out


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
    ap.add_argument("--t7extract", required=True, help="workspace t7extract folder (its wwise_hirc reads the bank)")
    ap.add_argument("--source", required=True, help="folder unpacked with t7extract (contains TekkenGame/)")
    ap.add_argument("--audio", help="t7extract audio output for the listed media (.ogg)")
    ap.add_argument("--list-media", action="store_true", help="print the virtual paths of the media to unpack")
    args = ap.parse_args()
    sys.path.insert(0, args.t7extract)
    table = tracks(Path(args.source) / BANK)
    if args.list_media:
        for media in sorted({m for v in table.values() for l in v.values() for m in l}):
            print(MEDIA.format(media))
        return
    if not args.audio:
        ap.error("--audio is required unless --list-media")
    files = {int(f.stem): f for f in Path(args.audio).rglob("*.ogg") if f.stem.isdigit()}
    # Files are replaced in place and stale ones pruned at the end, so Unity keeps each clip's .meta (and GUID).
    OUT.mkdir(parents=True, exist_ok=True)
    start = time.time() - 1
    rows, missing = [], []
    for name, variants in table.items():
        row = {"name": name, "stage": name.split("_")[0] if name.startswith("stg") else "", "normal": "", "final": ""}
        for state, media in variants.items():
            if media[0] not in files:
                missing.append("%s/%s" % (name, state))
                continue
            stem = "%s__%s" % (name, "normal" if state == "any" else state)
            shutil.copyfile(files[media[0]], OUT / (stem + ".ogg"))
            row["final" if state == "final" else "normal"] = stem
        if not row["final"]:
            row["final"] = row["normal"]          # stg21/stg22 have one arrangement for every round
        rows.append(row)
    (OUT / "music.json").write_text(json.dumps({"tracks": rows}, indent=1), encoding="utf-8")
    prune(OUT, start)
    print("tracks %d, files %d, missing %d %s" % (len(rows), len(list(OUT.glob("*.ogg"))), len(missing), missing))


if __name__ == "__main__":
    main()
