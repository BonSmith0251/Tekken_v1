"""Replay the original movelist demo inputs through the reference interpreter.

Each demo is the game's own frame-by-frame input for a movelist entry. A demo
passes when every button press group in it starts exactly one move through a
button cancel (1,2,3 -> three moves; multi-part moves chain without a press). Entries needing a setup the demo cannot
express (combos on counter hit, throws, parries) are reported separately.
"""
import argparse
import json
import re
from pathlib import Path

from command_reference import Context, Fighter, load, parse_input

SETUP = ("Approach", "Time with", "(While enemy is down)", ">")


def press_groups(demo):
    """Frames with new button presses; demos press simultaneous buttons on one frame."""
    groups, prev = 0, 0
    for step in demo["inputs"]:
        _, held = parse_input(step["input"])
        for _ in range(step["frames"]):
            groups += bool(held & ~prev)
            prev = held
    return groups


def run(graph, entry, opponent_id, verbose=False):
    ctx = Context(graph["characterId"], opponent_id)
    ctx.rage = entry["command"].startswith("During Rage")
    ctx.distance = entry["demo"].get("distance", 2000)
    f = Fighter(graph, ctx)
    for _ in range(30):
        f.step(5, 0)
    start = len(f.log)
    for step in entry["demo"]["inputs"]:
        d, b = parse_input(step["input"])
        for _ in range(step["frames"]):
            f.step(d, b)
    for _ in range(90):
        f.step(5, 0)
    moves = [m for _, m, _ in f.log[start:]]
    # Multi-part moves chain through automatic cancels; count only button-driven moves.
    # Stances (Zenshin, Mental Alertness) and Rage Arts have no damage but are commands.
    attacks = [m for _, m, by_input in f.log[start:] if by_input]
    if verbose:
        print("   ", " > ".join(graph["moves"][m]["name"] for m in moves))
    return attacks, moves


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--graph", required=True)
    p.add_argument("--movelist", required=True)
    p.add_argument("--opponent", type=int, default=9)
    p.add_argument("--verbose", action="store_true")
    p.add_argument("--report")
    a = p.parse_args()
    graph = load(a.graph)
    entries = json.loads(Path(a.movelist).read_text(encoding="utf-8"))
    results = []
    for e in entries:
        if not e.get("demo") or e["type"] in ("category", "0xF") or e["type"].startswith("0x1000"):
            continue
        expected = press_groups(e["demo"])
        setup = any(s in e["command"] for s in SETUP) or e["type"] == "combo"
        attacks, moves = run(graph, e, a.opponent)
        ok = len(attacks) == expected
        results.append({"index": e["index"], "name": e["name"], "command": e["command"], "setup": setup,
                        "rage": e["command"].startswith("During Rage"),
                        "distance": e["demo"].get("distance", 2000), "inputs": e["demo"]["inputs"],
                        "expected": expected, "attacks": [graph["moves"][m]["name"] for m in attacks],
                        "trace": moves, "passed": ok})
        if a.verbose or not ok:
            print(("PASS " if ok else "FAIL ") + ("[setup] " if setup else "") +
                  f"{e['index']:3d} {e['name'][:30]:30s} {e['command'][:34]:34s} expect {expected} got "
                  + ", ".join(graph["moves"][m]["name"] for m in attacks))
            if a.verbose and not ok:
                run(graph, e, a.opponent, verbose=True)
    plain = [r for r in results if not r["setup"]]
    print(f"plain commands: {sum(r['passed'] for r in plain)}/{len(plain)} passed; "
          f"setup-dependent: {sum(r['passed'] for r in results if r['setup'])}/{len(results) - len(plain)}")
    if a.report:
        Path(a.report).write_text(json.dumps({"opponent": a.opponent, "demos": results}, indent=1,
                                             ensure_ascii=False), encoding="utf-8")


if __name__ == "__main__":
    main()
