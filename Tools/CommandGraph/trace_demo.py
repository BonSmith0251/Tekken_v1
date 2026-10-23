"""Frame trace of one movelist demo: python trace_demo.py <graph> <movelist> <index>"""
import json, sys
from command_reference import Context, Fighter, load, parse_input
g = load(sys.argv[1]); e = [x for x in json.load(open(sys.argv[2], encoding="utf-8")) if x["index"] == int(sys.argv[3])][0]
ctx = Context(g["characterId"], 9); ctx.rage = e["command"].startswith("During Rage"); f = Fighter(g, ctx)
for _ in range(30): f.step(5, 0)
names = lambda m: g["moves"][m]["name"]
def cancels(m):
    out, i = [], g["moves"][m]["cancelStart"]
    while i >= 0:
        c = g["cancels"][i]; out.append(c); i += 1
        if c["command"] & 0xFFFFFFFF == 0x8000: break
    return out
t = 0
for st in e["demo"]["inputs"]:
    d, b = parse_input(st["input"])
    for k in range(st["frames"]):
        before = f.move; f.step(d, b); t += 1
        if (b and k == 0) or f.move != before:
            print(f"t{t:3d} in {st['input']:8s} {names(before):18s} frame {f.frame if f.move == before else '-':>3} -> {names(f.move)} pending={f.pending and names(f.pending[0])}")
for m in sorted({x for _, x, _ in f.log[30:]}):
    print("==", names(m), "frames", g["moves"][m]["frames"])
    for c in cancels(m):
        if c["command"] >> 32 & 0xF or c["command"] & 0xFFFFFFFF == 0x8000:
            low = c["command"] & 0xFFFFFFFF
            print("   ", hex(c["command"]), c["start"], c["end"], "sf", c["startingFrame"], "->", names(c["target"]) if low != 0x800B else c["target"])
