"""Extract Flash IDE motion tweens ("fl.motion" MotionBase / AnimatorFactory3D code that Flash writes
into timeline-class constructors) from a GFX movie's ActionScript, using abc_dis.py's disassembly.

For each <init> that builds __motion_<Instance>af<n>, it returns
  {class: [{target, segments: [{spanStart, duration, props: {name: [values per frame]}, matrix3D: [[16]|None per frame]}]}]}
Values are what the constructor pushes (numbers, booleans, strings, None). Usage:
  python Tools/BattleHud/motion_dump.py <SW_UI_GameMessage.uasset> > motions.json
"""
import json
import re
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
PUSH = re.compile(r"^\s*\d+ (pushbyte|pushshort|pushint|pushuint|pushdouble|pushstring|pushtrue|pushfalse|pushnull|pushnan) ?(.*)$")


def value(op, arg):
    if op == "pushbyte":
        v = int(arg); return v - 256 if v > 127 else v
    if op in ("pushshort", "pushint", "pushuint"):
        v = int(arg); return v - (1 << 32) if v >= 1 << 31 else v
    if op == "pushdouble": return float(arg)
    if op == "pushstring": return arg.strip("'")
    if op == "pushtrue": return True
    if op == "pushfalse": return False
    if op == "pushnan": return float("nan")
    return None


def parse(lines):
    motions, segment, stack, mat_vec, mat_array, current = [], None, [], [0.0] * 16, [], None
    pending_index = None
    for line in lines:
        m = PUSH.match(line)
        if m:
            stack.append(value(m.group(1), m.group(2))); continue
        s = line.split(None, 1)[1] if len(line.split(None, 1)) > 1 else ""
        if s.startswith("initproperty __motion_") and "matArray" not in s and "mat3DVec" not in s:
            target = re.match(r"initproperty __motion_(.+?)af\d+", s).group(1)
            if current is None or current["target"] != target:
                current = {"target": target, "segments": []}; motions.append(current)
            segment = {"spanStart": 0, "duration": 0, "props": {}, "matrix3D": None, "is3D": False, "initialPosition": None}
            current["segments"].append(segment); stack = []
        elif s.startswith("setproperty duration") and segment is not None and stack:
            segment["duration"] = stack[-1]; stack = []
        elif s.startswith("setproperty spanStart") and segment is not None and stack:
            segment["spanStart"] = stack[-1]; stack = []
        elif s.startswith("initproperty ____") and "matArray__" in s:
            mat_array = []; stack = []
        elif s.startswith("setproperty [late]") and len(stack) >= 2:
            index, v = stack[-2], stack[-1]
            if isinstance(index, int) and 0 <= index < 16: mat_vec[index] = float(v)
            stack = stack[:-2]
        elif s.startswith("constructprop Matrix3D"):
            stack.append(("M3", list(mat_vec)))
        elif s.startswith("callpropvoid push 1") and stack and segment is not None:
            item = stack.pop()
            if isinstance(item, tuple) or item is None:
                mat_array.append(item[1] if isinstance(item, tuple) else None)
            stack = []
        elif s.startswith("newarray"):
            n = int(s.split()[1]); arr = stack[-n:] if n else []; stack = stack[:-n] if n else stack; stack.append(arr)
        elif s.startswith("callpropvoid addPropertyArray"):
            if len(stack) >= 2 and isinstance(stack[-2], str) and isinstance(stack[-1], list):
                segment["props"][stack[-2]] = stack[-1]
            elif stack and isinstance(stack[-1], str) and stack[-1] == "matrix3D":
                segment["matrix3D"] = mat_array
            stack = []
        elif s.startswith("callpropvoid addFilterPropertyArray"):
            # (filterIndex, property, values, startFrame, endFrame)
            if len(stack) >= 5 and isinstance(stack[-4], str) and isinstance(stack[-3], list):
                segment["props"][stack[-4]] = stack[-3]
            stack = []
        elif s.startswith("setproperty initialPosition") and stack and isinstance(stack[-1], list):
            segment["initialPosition"] = stack[-1]; stack = []
        elif s.startswith("setproperty is3D") and stack:
            segment["is3D"] = bool(stack[-1]); stack = []
        elif s.startswith("callpropvoid addTargetInfo"):
            stack = []
        elif s.startswith(("getlocal", "getproperty", "findpropstrict", "getlex", "applytype", "construct", "pushscope", "callpropvoid", "initproperty", "setproperty", "coerce", "convert", "ifne", "ifeq", "jump", "pop", "dup", "returnvoid", "constructsuper", "newfunction")):
            if s.startswith("getproperty") and "matArray__" in s and stack == []:
                pass
            continue
    return motions


def dump(path, pattern=r"\.<init>"):
    out = subprocess.run([sys.executable, "-I", str(HERE / "abc_dis.py"), path, pattern], capture_output=True, text=True, check=True).stdout
    result, cls, lines = {}, None, []
    for line in out.splitlines():
        if line.startswith("### "):
            if cls and lines: 
                m = parse(lines)
                if m: result[cls] = m
            cls = line[4:].split(".<init>")[0]; lines = []
        else:
            lines.append(line)
    if cls and lines:
        m = parse(lines)
        if m: result[cls] = m
    return result


if __name__ == "__main__":
    print(json.dumps(dump(sys.argv[1]), indent=0))
