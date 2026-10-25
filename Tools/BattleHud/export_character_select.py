"""Export Tekken 7's character select textures for the Unity remaster.

The select screen is the Scaleform movie UI_common/flash/SW_UI_CharacterSelect: slot frames per
player colour, the random (?) panel, player tags and flares. Its Rep_CS_CH_* placeholders are
replaced at runtime from UI_common/Texture2D/ReplaceImages: CS_CH_PANEL_<code> (grid portrait),
CS_CH_L_/CS_CH_R_<code> (large art for each side) and CS_CH_NAME_<code> (name lettering). The
animated shard background is a movie (Movies/Ui/CharacterSelect_loop) and is not exported.
The VS screen shown while the match loads (SW_UI_Makuai: panels, frames, VS lettering, lightning)
is exported here too; its fire-burst flipbook is black-backed (additive) and is skipped.

Usage (from the project root):
  python Tools/BattleHud/export_character_select.py --game <TEKKEN.7> --t7extract <workspace t7extract> --work <scratch dir>
Add a fighter by adding its three-letter code to CHARACTERS (and to CharacterSelect.Roster).
"""
import argparse
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Assets/_Project/Resources/CharacterSelect"
CHARACTERS = ["JIN", "KAZ", "XIA", "ASA", "LTN"]
PATTERNS = ["*/flash/SW_UI_CharacterSelect/*", "*/flash/SW_UI_Makuai/*"] + [
    f"*/ReplaceImages/{kind}/{kind}_{c}.uasset" for c in CHARACTERS for kind in ("CS_CH_PANEL", "CS_CH_L", "CS_CH_R", "CS_CH_NAME")] + [
    f"*/ReplaceImages/{kind}/{kind}_{c}_TK7_DEF.uasset" for c in CHARACTERS for kind in ("VS_CH_L", "VS_CH_R")]

# Remaster name -> source texture name.
NAMES = {
    "SlotNormal": "PanelNormal_Base", "SlotShadow": "PanelNormal_Shadow",
    "SlotRed": "PanelRed_Base", "SlotRedHover": "PanelRed_OnGradient",
    "SlotBlue": "PanelBlue_Base", "SlotBlueHover": "PanelBlue_OnGradient",
    "SlotPurple": "PanelPurple_Base", "SlotPurpleHover": "PanelPurple_OnGradient",
    "Random": "Hatena_Normal", "RandomRed": "Hatena_Red", "RandomBlue": "Hatena_Blue", "RandomPurple": "Hatena_Purple",
    "Tag1": "Moji_1_Red", "Tag2": "Moji_2_Blue", "TagCpu": "Moji_CPU",
    "Light": "Light", "FlashLight": "FlashLight",
    "FlareRed": "GlassFlare_Red_Base", "FlareBlue": "GlassFlare_Blue_Base",
    "FlareRedVolume": "GlassFlare_Red_Volume", "FlareBlueVolume": "GlassFlare_Blue_Volume",
    "Arrow": "Arrow",
    # The VS (loading) screen, SW_UI_Makuai; character art is VS_CH_L/R_<code>_TK7_DEF.
    "VsBaseL": "BaseL", "VsBaseR": "BaseR", "VsFrameL": "frameL", "VsFrameR": "frameR",
    "VsLogo": "VS", "VsGlow": "VS_ADD", "VsLightning": "Lightning_01", "VsLightningFlare": "Lightning_Flare",
}
for c in CHARACTERS:
    NAMES[f"Panel_{c}"] = f"CS_CH_PANEL_{c}"
    NAMES[f"Art_L_{c}"] = f"CS_CH_L_{c}"
    NAMES[f"Art_R_{c}"] = f"CS_CH_R_{c}"
    NAMES[f"Name_{c}"] = f"CS_CH_NAME_{c}"
    NAMES[f"VsArt_L_{c}"] = f"VS_CH_L_{c}_TK7_DEF"
    NAMES[f"VsArt_R_{c}"] = f"VS_CH_R_{c}_TK7_DEF"


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--game", required=True, type=Path)
    parser.add_argument("--t7extract", required=True, type=Path)
    parser.add_argument("--work", required=True, type=Path)
    args = parser.parse_args()
    work = args.work / "character_select_textures"
    subprocess.run([sys.executable, "-m", "t7extract", "textures", str(args.game.resolve()), *PATTERNS, "-o", str(work.resolve())],
                   cwd=args.t7extract, check=True)
    found = {}
    for png in sorted(work.rglob("*.png")):
        if png.stem not in found or "ReplaceImages" in png.parts:
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
