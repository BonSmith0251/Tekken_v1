"""Export Tekken 7's arcade attract textures for the remaster's cabinet screens.

The title movie (UI_common/flash/SW_UI_Title) swaps its Rep_TIT_* placeholders at runtime from
UI_common/Texture2D/ReplaceImages: TIT_OPERATIONMSG_<message> (the arcade operation messages left over
from the cabinet version: INSERT COIN(S), PRESS P1 START BUTTON, ...) and TIT_COPYRIGHT. The logo is
the staff roll's Logo_TK7, and the credit footer uses the information bar's InfoBack3 strip.

Usage (from the project root):
  python Tools/BattleHud/export_arcade_ui.py --game <TEKKEN.7> --t7extract <workspace t7extract> --work <scratch dir>
Output: Assets/_Project/Resources/BattleHud/Arcade (the BattleHud import rule applies there).
"""
import argparse
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Assets/_Project/Resources/BattleHud/Arcade"
REGION = "us"
MESSAGES = ["InsertCoins", "InsertMoreCoins", "PressStartButton", "PressP1StartButton", "PressP2StartButton",
            "PressP1orP2StartButton", "PressP1AndorP2StartButton"]
PATTERNS = [f"*/ReplaceImages/TIT_OPERATIONMSG/{REGION}/TIT_OPERATIONMSG_*", "*/ReplaceImages/TIT_COPYRIGHT/*",
            "*/StaffRollLogo/Logo_TK7.uasset", "*/flash/SW_UI_InformationBar/*"]
# Remaster name -> (source texture name, required path fragment).
NAMES = {f"Msg_{m}": (f"TIT_OPERATIONMSG_{m}", f"/{REGION}/") for m in MESSAGES}
NAMES.update({"Logo": ("Logo_TK7", ""), "Copyright": ("TIT_COPYRIGHT", "ReplaceImages"), "InfoBar": ("InfoBack3", "")})


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--game", required=True, type=Path)
    parser.add_argument("--t7extract", required=True, type=Path)
    parser.add_argument("--work", required=True, type=Path)
    args = parser.parse_args()
    work = args.work / "arcade_ui_textures"
    subprocess.run([sys.executable, "-m", "t7extract", "textures", str(args.game.resolve()), *PATTERNS, "-o", str(work.resolve())],
                   cwd=args.t7extract, check=True)
    pngs = sorted(work.rglob("*.png"))
    OUT.mkdir(parents=True, exist_ok=True)
    missing = []
    for name, (source, fragment) in NAMES.items():
        match = next((p for p in pngs if p.stem == source and fragment in p.as_posix()), None)
        if match is None:
            missing.append(source)
        else:
            shutil.copyfile(match, OUT / f"{name}.png")
    if missing:
        sys.exit("Missing textures: " + ", ".join(missing))
    print(f"wrote {len(NAMES)} textures to {OUT}")


if __name__ == "__main__":
    main()
