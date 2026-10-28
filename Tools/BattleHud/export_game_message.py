"""Export Tekken 7's round calls (READY, FIGHT, K.O., DOUBLE K.O., ROUND, TIME UP, DRAW, PERFECT)
from the Scaleform movie UI_common/flash/SW_UI_GameMessage for the Unity remaster.

The calls are timelines, not single images: every word is several layers (the lettering, a tinted
copy, a pulsing glow, flares, sparks, highlight sweeps) animated per frame at 60 fps on a 1920x1080
stage. This tool decodes the movie with gfx_dump.py and writes, for the call clips only:
  Resources/BattleHud/GameMessage.json   images (id, name, size), mask rectangles, and each reachable
                                         sprite's frame events (placements, matrices, colour transforms,
                                         blend modes, clip depths), plus which sprites stop on their last
                                         frame (the named *_mc classes call stop() there; the others loop)
  Resources/BattleHud/GameMessage/*.png  the images those sprites place
BattleHud's GfxClip replays it with Resources/BattleHud/GfxLayer.shader (colour transform, additive blend,
blur and letter-shaped masks, rasterised here to GameMessage/Mask_<id>.png); see
Docs/BATTLE_HUD.txt.

Usage (from the project root):
  python Tools/BattleHud/export_game_message.py --game <TEKKEN.7> --t7extract <workspace t7extract> --work <scratch dir>
"""
import argparse
import json
import shutil
import struct
import subprocess
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

sys.path.insert(0, str(Path(__file__).resolve().parent))
import gfx_dump  # noqa: E402
import motion_dump  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Assets/_Project/Resources/BattleHud"
MOVIE = "*/flash/SW_UI_GameMessage/SW_UI_GameMessage.uasset"
# Remaster call -> top-level clip class (instance names in the root timeline).
ENTRIES = ["ReadyFight_mc", "KO_mc", "WKO_mc", "Round_mc", "TimeUp_mc", "Draw_mc", "Perfect_mc"]


def matrix(m):
    return [m["sx"], m["r0"], m["r1"], m["sy"], m["tx"], m["ty"]]


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--game", required=True, type=Path)
    parser.add_argument("--t7extract", required=True, type=Path)
    parser.add_argument("--work", required=True, type=Path)
    args = parser.parse_args()
    work = (args.work / "game_message").resolve()
    game = str(args.game.resolve())
    subprocess.run([sys.executable, "-m", "t7extract", "unpack", game, MOVIE, "-o", str(work / "raw")], cwd=args.t7extract, check=True)
    subprocess.run([sys.executable, "-m", "t7extract", "textures", game, "*/flash/SW_UI_GameMessage/*", "-o", str(work / "png")],
                   cwd=args.t7extract, check=True)
    movie, body = gfx_dump.dump(str(next((work / "raw").rglob("SW_UI_GameMessage.uasset"))))
    start = gfx_dump.rect(body, 8)[1] + 4
    sprites, symbols = movie["sprites"], movie["symbols"]
    roots = {e["name"]: e["char"] for e in movie["root"]["events"][0] if e.get("name") in ENTRIES}

    reachable, todo = set(), list(roots.values())
    while todo:
        sid = todo.pop()
        if sid in reachable or sid not in sprites:
            continue
        reachable.add(sid)
        for events in sprites[sid]["events"].values():
            todo += [e["char"] for e in events if "char" in e]

    used_images, used_masks, out_sprites = set(), set(), []
    for sid in sorted(reachable):
        s = sprites[sid]
        frames = []
        for frame, events in sorted(s["events"].items()):
            evs = []
            for e in events:
                ev = {"depth": e["depth"], "remove": e.get("remove", False), "move": e.get("move", False),
                      "character": e.get("char", -1), "clipDepth": e.get("clipDepth", 0), "blend": e.get("blend", 0),
                      "name": e.get("name", "")}
                if "m" in e:
                    ev["hasMatrix"] = True; ev["matrix"] = matrix(e["m"])
                if "visible" in e:
                    ev["hasVisible"] = True; ev["visible"] = e["visible"]
                if "blur" in e:
                    ev["hasFilter"] = True; ev["blur"] = e["blur"]
                if "cx" in e:
                    ev["hasColor"] = True
                    ev["mul"] = e["cx"].get("mul", [1, 1, 1, 1]); ev["add"] = e["cx"].get("add", [0, 0, 0, 0])
                c = ev["character"]
                if c in movie["images"]:
                    used_images.add(c)
                elif c in movie["shapes"]:
                    used_masks.add(c)
                evs.append(ev)
            frames.append({"frame": int(frame), "events": evs})
        # Named timeline classes stop on their last frame (frame scripts call stop()); the rest loop.
        out_sprites.append({"id": sid, "frames": s["frames"], "stops": symbols.get(sid, "").endswith(tuple(f"_{n}" for n in range(1000))) and "_mc_" in symbols.get(sid, ""),
                            "labels": [{"name": k, "frame": v} for k, v in s["labels"].items()], "frameEvents": frames})

    pngs = {p.stem: p for p in (work / "png").rglob("*.png")}
    missing = [movie["images"][i]["name"] for i in used_images if movie["images"][i]["name"] not in pngs]
    if missing:
        sys.exit("Missing images: " + ", ".join(missing))
    (OUT / "GameMessage").mkdir(parents=True, exist_ok=True)
    for i in used_images:
        shutil.copyfile(pngs[movie["images"][i]["name"]], OUT / "GameMessage" / (movie["images"][i]["name"] + ".png"))
    # Masks are the letter outlines (vector shapes); rasterise each at stage resolution, even-odd.
    shape_tags = {struct.unpack_from("<H", body, p)[0]: (p, code) for code, p, _ in gfx_dump.tags(body, start, len(body))
                  if code in (2, 22, 32, 83)}
    for i in used_masks:
        p, code = shape_tags[i]
        (x0, x1, y0, y1), paths = gfx_dump.shape_paths(body, p, code)
        w, h = int(np.ceil(x1 - x0)) + 1, int(np.ceil(y1 - y0)) + 1
        acc = np.zeros((h, w), bool)
        for path in paths:
            layer = Image.new("L", (w, h), 0)
            ImageDraw.Draw(layer).polygon([(x - x0, y - y0) for x, y in path], fill=255)
            acc ^= np.asarray(layer) > 0
        rgba = np.zeros((h, w, 4), np.uint8); rgba[..., :3] = 255; rgba[..., 3] = acc * 255
        Image.fromarray(rgba, "RGBA").save(OUT / "GameMessage" / f"Mask_{i}.png")
    # Motion tweens the Flash IDE exported as ActionScript (fl.motion MotionBase + AnimatorFactory3D in each
    # timeline class's constructor): they drive named children's 3D transform, colour, visibility, blend and blur.
    motions = []
    by_class = {name.split(".", 1)[-1]: sid for sid, name in symbols.items()}
    blend_codes = {"normal": 1, "add": 8}
    for cls, entries in motion_dump.dump(str(next((work / "raw").rglob("SW_UI_GameMessage.uasset")))).items():
        sid = by_class.get(cls)
        if sid not in reachable:
            continue
        for motion in entries:
            segments = []
            for seg in motion["segments"]:
                props = []
                for name, values in seg["props"].items():
                    if name == "blendMode":
                        values = [blend_codes.get(v, 1) for v in values]
                    elif name in ("visible", "cacheAsBitmap", "opaqueBackground"):
                        if name != "visible":
                            continue
                        values = [1 if v else 0 for v in values]
                    if not values or not all(isinstance(v, (int, float)) and not isinstance(v, bool) for v in values):
                        continue
                    props.append({"name": name, "values": [float(v) for v in values]})
                mat3d = next((m for m in (seg["matrix3D"] or []) if m), None)
                segments.append({"spanStart": seg["spanStart"], "duration": seg["duration"], "is3D": seg["is3D"],
                                 "hasInitialPosition": seg["initialPosition"] is not None,
                                 "initialPosition": [float(v) for v in (seg["initialPosition"] or [0, 0, 0])],
                                 "hasMatrix3D": mat3d is not None, "matrix3D": mat3d or [], "props": props})
            motions.append({"sprite": sid, "target": motion["target"], "segments": segments})
    data = {
        "stageWidth": movie["rect"][1], "stageHeight": movie["rect"][3], "fps": movie["fps"],
        "motions": motions,
        "entries": [{"name": n, "sprite": c} for n, c in roots.items()],
        "images": [{"id": i, **movie["images"][i]} for i in sorted(used_images)],
        "masks": [{"id": i, "xMin": movie["shapes"][i][0], "xMax": movie["shapes"][i][1], "yMin": movie["shapes"][i][2], "yMax": movie["shapes"][i][3]}
                  for i in sorted(used_masks)],
        "sprites": out_sprites,
    }
    (OUT / "GameMessage.json").write_text(json.dumps(data, separators=(",", ":")), encoding="utf-8")
    print(f"{len(roots)} calls, {len(out_sprites)} sprites, {len(used_images)} images, {len(used_masks)} masks -> {OUT}")


if __name__ == "__main__":
    main()
