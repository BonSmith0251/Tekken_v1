"""Export Tekken 7's battle HUD textures for the Unity remaster.

The in-fight HUD is Scaleform (GFx) movies under UI_common/flash: SW_UI_HUD_Gauge (life gauges,
round counters, name plates, lightning), SW_UI_Timer and SW_UI_GameMessage (ROUND / FIGHT / K.O.
calls). Their `Rep_*` images are placeholders that the game swaps for UI_common/Texture2D/
ReplaceImages at runtime; the default (customization 000) gauge skin and the per-character icons
and names come from there. This tool exports the textures with t7extract and copies the set the
remaster draws into Resources/BattleHud under stable names. Layout is not in the textures: the
GFx timelines are compiled, so BattleHud.cs places them from measurements of gameplay captures.

Usage (from the project root):
  python Tools/BattleHud/export_battle_hud.py --game <TEKKEN.7> --t7extract <workspace t7extract> --work <scratch dir>
"""
import argparse
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Assets/_Project/Resources/BattleHud"
CHARACTERS = ["JIN", "KAZ", "XIA", "ASA", "LTN"]
PATTERNS = [
    "*/flash/SW_UI_HUD/*", "*/flash/SW_UI_HUD_Gauge/*", "*/flash/SW_UI_GameMessage/*",
    "*/ReplaceImages/HUD_GAU_LIFE/*", "*/ReplaceImages/HUD_GAU_TIMER/*",
    "*/HUD_GAU_LIFE_*_000.uasset",
] + [f"*/HUD_CH_ICON_?/HUD_CH_ICON_?_{c}.uasset" for c in CHARACTERS] \
  + [f"*/HUD_CH_NAME_?/HUD_CH_NAME_?_{c}.uasset" for c in CHARACTERS]

# Remaster name -> source texture name (the default gauge skin is customization 000).
NAMES = {
    "FrameNormal": "HUD_GAU_LIFE_FRAME_NORMAL_L_000",
    "FrameRage": "HUD_GAU_LIFE_FRAME_RAGE_L_000",
    "FrameDamage": "HUD_GAU_LIFE_FRAME_DAMAGE_L_000",
    "LifeFill": "GAU_Life_green",
    "LifeDamage": "GAU_Life_red",
    "LifeHit": "GAU_Life_eff_damage",
    "RoundOff": "RNDCounter_off",
    "RoundOn": "RNDCounter_on",
    "RoundFlash": "RNDCounter_flash1",
    "NameBase_L": "TXT_base1_L",
    "NameBase_R": "TXT_base1_R",
    "Thunder_L": "Thunder_L",
    "Thunder_R": "Thunder_R",
    "Infinity": "Num01_mugen",
    "Call_Round": "Moji_Round", "Call_Round_Glow": "Moji_Round_Glow",
    "Call_Ready": "Moji_Ready", "Call_Ready_Glow": "Moji_Ready_Glow",
    "Call_Fight": "Moji_Fight_Set", "Call_Fight_Glow": "Moji_Fight_Glow",
    "Call_KO": "Moji_KO_Set", "Call_KO_Glow": "Moji_KO_Glow",
    "Call_Double": "Moji_Double", "Call_Double_Glow": "Moji_Double_Glow",
    "Call_TimeUp": "Moji_TimeUp", "Call_TimeUp_Glow": "Moji_TimeUp_Glow",
    "Call_Draw": "Moji_Draw", "Call_Draw_Glow": "Moji_Draw_Glow",
    "Call_Perfect": "Moji_Perfect", "Call_Perfect_Glow": "Moji_Perfect_Glow",
    "Call_Final": "Moji_Final", "Call_Final_Glow": "Moji_Final_Glow",
}
NAMES.update({f"Digit{d}": f"Num01_{d}" for d in range(10)})
NAMES.update({f"RoundNum{d}": f"RoundNum{d}" for d in range(10)})
NAMES.update({f"RoundNum{d}_Glow": f"RoundNum{d}_Glow" for d in range(10)})
for c in CHARACTERS:
    for side in "LR":
        NAMES[f"Icon_{side}_{c}"] = f"HUD_CH_ICON_{side}_{c}"
        NAMES[f"Name_{side}_{c}"] = f"HUD_CH_NAME_{side}_{c}"
# Directories whose copy wins when a name exists twice (ReplaceImages replace the flash placeholders).
PREFERRED = ("ReplaceImages",)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--game", required=True, type=Path)
    parser.add_argument("--t7extract", required=True, type=Path)
    parser.add_argument("--work", required=True, type=Path)
    args = parser.parse_args()
    work = args.work / "battle_hud_textures"
    subprocess.run([sys.executable, "-m", "t7extract", "textures", str(args.game.resolve()), *PATTERNS, "-o", str(work.resolve())],
                   cwd=args.t7extract, check=True)
    found = {}
    for png in sorted(work.rglob("*.png")):
        if png.stem not in found or any(p in png.parts for p in PREFERRED):
            found[png.stem] = png
    missing = [source for source in NAMES.values() if source not in found]
    if missing:
        sys.exit("Missing textures: " + ", ".join(missing))
    OUT.mkdir(parents=True, exist_ok=True)
    for name, source in NAMES.items():
        shutil.copyfile(found[source], OUT / f"{name}.png")
    print(f"wrote {len(NAMES)} textures to {OUT}")


if __name__ == "__main__":
    main()
