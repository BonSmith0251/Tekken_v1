"""Adds the movelist's Rage Art demo input to an existing command graph as "rageArt".

export_command_graph.py leaves "During Rage" entries out of the CPU's random demos (they need Rage).
The versus scene's Rage Art button (VersusGame.TriggerRageArt -> CpuPlayer.RequestRageArt) replays
this input instead. The graph text is edited in place (only the "rageArt" member is added or
replaced), so later passes such as pushbacks.py and body_points.py keep their data.

    python Tools/CommandGraph/add_rage_art_demo.py --graph Assets/_Project/Resources/CommandGraph/jin.json
        --movelist References/CommandGraph/jin-movelist.json
"""
import argparse
import json
import re
from pathlib import Path

NAMES = {"d/b": 1, "d": 2, "d/f": 3, "b": 4, "n": 5, "f": 6, "u/b": 7, "u": 8, "u/f": 9}


def steps_of(entry):
    # Same conversion as export_command_graph.py: (direction 1-9, button bits, frames) triples.
    steps = []
    for step in entry["demo"]["inputs"]:
        direction, buttons = 5, 0
        for part in step["input"].lower().split("+"):
            if part in NAMES:
                direction = NAMES[part]
            elif part in "1234" and part:
                buttons |= 1 << (int(part) - 1)
        steps.extend((direction, buttons, step["frames"]))
    return steps


def rage_art(movelist):
    entries = [e for e in movelist if e.get("name") == "Rage Art" and e.get("demo")]
    if len(entries) != 1:
        raise SystemExit(f"Expected one Rage Art movelist entry with a demo, found {len(entries)}")
    e = entries[0]
    return {"name": e["name"], "command": e["command"], "steps": steps_of(e)}


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--graph", required=True, type=Path)
    p.add_argument("--movelist", required=True, type=Path)
    a = p.parse_args()
    demo = rage_art(json.loads(a.movelist.read_text(encoding="utf-8")))
    text = a.graph.read_text(encoding="utf-8")
    member = '"rageArt": ' + json.dumps(demo, ensure_ascii=False)
    if '"rageArt"' in text:
        text = re.sub(r'"rageArt": \{[^{}]*\}', lambda m: member, text, count=1)
    else:
        end = text.rstrip().rfind("}")
        text = text[:end].rstrip() + ", " + member + "}" + text[end + 1:]
    json.loads(text)  # still valid JSON
    a.graph.write_text(text, encoding="utf-8", newline="")
    print(f"{a.graph.name}: rageArt {demo['command']!r}, {len(demo['steps']) // 3} input steps")


if __name__ == "__main__":
    main()
