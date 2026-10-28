"""Minimal AVM2 (ABC) disassembler: prints the bytecode of methods whose names match a pattern."""
import re, struct, sys, glob
sys.path.insert(0, __import__("os").path.dirname(__import__("os").path.abspath(__file__)))
from gfx_dump import dump


class R:
    def __init__(s, d, i=0): s.d, s.i = d, i
    def u8(s): v = s.d[s.i]; s.i += 1; return v
    def u16(s): v, = struct.unpack_from("<H", s.d, s.i); s.i += 2; return v
    def s24(s): v = int.from_bytes(s.d[s.i:s.i + 3], "little", signed=True); s.i += 3; return v
    def u30(s):
        r = sh = 0
        while True:
            x = s.u8(); r |= (x & 0x7F) << sh; sh += 7
            if not x & 0x80: return r
    def d64(s): v, = struct.unpack_from("<d", s.d, s.i); s.i += 8; return v


def parse(abc):
    r = R(abc, 4)
    ints = [0] + [r.u30() for _ in range(max(0, r.u30() - 1))]
    ints = [i - (1 << 32) if i & 0x80000000 else i for i in ints]
    uints = [0] + [r.u30() for _ in range(max(0, r.u30() - 1))]
    doubles = [0.0] + [r.d64() for _ in range(max(0, r.u30() - 1))]
    strings = [""]
    for _ in range(max(0, r.u30() - 1)):
        n = r.u30(); strings.append(abc[r.i:r.i + n].decode("utf-8", "replace")); r.i += n
    ns = [None] + [(r.u8(), r.u30()) for _ in range(max(0, r.u30() - 1))]
    nss = [None] + [[r.u30() for _ in range(r.u30())] for _ in range(max(0, r.u30() - 1))]
    mn = [None]
    for _ in range(max(0, r.u30() - 1)):
        k = r.u8()
        if k in (0x07, 0x0D): mn.append(("q", r.u30(), r.u30()))
        elif k in (0x0F, 0x10): mn.append(("rt", r.u30()))
        elif k in (0x11, 0x12): mn.append(("rtl", None))
        elif k in (0x09, 0x0E): mn.append(("m", r.u30(), r.u30()))
        elif k in (0x1B, 0x1C): mn.append(("ml", r.u30()))
        elif k == 0x1D:
            base = r.u30(); params = [r.u30() for _ in range(r.u30())]; mn.append(("t", base, params))
        else: raise ValueError(hex(k))
    def mname(i):
        if i == 0 or i >= len(mn) or mn[i] is None: return "*"
        e = mn[i]
        if e[0] in ("q", "rt", "m", "ml"): return strings[e[1] if e[0] != "q" else e[2]] if e[0] != "ml" else "[late]"
        if e[0] == "t": return mname(e[1]) + ".<" + ",".join(mname(p) for p in e[2]) + ">"
        return "?"
    methods = []
    for _ in range(r.u30()):
        pc = r.u30(); r.u30(); [r.u30() for _ in range(pc)]; name = r.u30(); flags = r.u8()
        if flags & 0x08:
            for _ in range(r.u30()): r.u30(); r.u8()
        if flags & 0x80: [r.u30() for _ in range(pc)]
        methods.append({"name": strings[name], "params": pc})
    for _ in range(r.u30()):
        r.u30(); [(r.u30(), r.u30()) for _ in range(r.u30())]
    def traits(owner):
        for _ in range(r.u30()):
            name = r.u30(); kind = r.u8(); k = kind & 0x0F
            if k in (0, 6):
                r.u30(); r.u30(); vi = r.u30()
                if vi: r.u8()
            elif k in (1, 2, 3):
                r.u30(); m = r.u30()
                methods[m]["full"] = owner + "." + mname(name)
            elif k in (4, 5): r.u30(); r.u30()
            if kind & 0x40: [r.u30() for _ in range(r.u30())]
    ic = r.u30(); inst = []
    for _ in range(ic):
        name = r.u30(); r.u30(); flags = r.u8()
        if flags & 0x08: r.u30()
        [r.u30() for _ in range(r.u30())]
        iinit = r.u30(); cname = mname(name); methods[iinit]["full"] = cname + ".<init>"
        traits(cname); inst.append(cname)
    for c in range(ic):
        cinit = r.u30(); methods[cinit]["full"] = inst[c] + ".<cinit>"; traits(inst[c])
    for _ in range(r.u30()):
        r.u30(); traits("script")
    bodies = {}
    for _ in range(r.u30()):
        m = r.u30(); r.u30(); r.u30(); r.u30(); r.u30()
        n = r.u30(); code = abc[r.i:r.i + n]; r.i += n
        for _ in range(r.u30()): r.u30(); r.u30(); r.u30(); r.u30(); r.u30()
        traits("body")
        bodies[m] = code
    return dict(ints=ints, uints=uints, doubles=doubles, strings=strings, mname=mname, methods=methods, bodies=bodies)


OPS = {
    0x02: ("nop",), 0x03: ("throw",), 0x08: ("kill", "u30"), 0x09: ("label",),
    0x0C: ("ifnlt", "s24"), 0x0D: ("ifnle", "s24"), 0x0E: ("ifngt", "s24"), 0x0F: ("ifnge", "s24"),
    0x10: ("jump", "s24"), 0x11: ("iftrue", "s24"), 0x12: ("iffalse", "s24"), 0x13: ("ifeq", "s24"),
    0x14: ("ifne", "s24"), 0x15: ("iflt", "s24"), 0x16: ("ifle", "s24"), 0x17: ("ifgt", "s24"),
    0x18: ("ifge", "s24"), 0x19: ("ifstricteq", "s24"), 0x1A: ("ifstrictne", "s24"), 0x1B: ("lookupswitch",),
    0x1C: ("pushwith",), 0x1D: ("popscope",), 0x1E: ("nextname",), 0x1F: ("hasnext",), 0x20: ("pushnull",),
    0x21: ("pushundefined",), 0x23: ("nextvalue",), 0x24: ("pushbyte", "u8"), 0x25: ("pushshort", "u30"),
    0x26: ("pushtrue",), 0x27: ("pushfalse",), 0x28: ("pushnan",), 0x29: ("pop",), 0x2A: ("dup",),
    0x2B: ("swap",), 0x2C: ("pushstring", "str"), 0x2D: ("pushint", "int"), 0x2E: ("pushuint", "uint"),
    0x2F: ("pushdouble", "dbl"), 0x30: ("pushscope",), 0x31: ("pushnamespace", "u30"), 0x32: ("hasnext2", "u30", "u30"),
    0x40: ("newfunction", "u30"), 0x41: ("call", "u30"), 0x42: ("construct", "u30"), 0x43: ("callmethod", "u30", "u30"),
    0x44: ("callstatic", "u30", "u30"), 0x45: ("callsuper", "mn", "u30"), 0x46: ("callproperty", "mn", "u30"),
    0x47: ("returnvoid",), 0x48: ("returnvalue",), 0x49: ("constructsuper", "u30"), 0x4A: ("constructprop", "mn", "u30"),
    0x4C: ("callproplex", "mn", "u30"), 0x4E: ("callsupervoid", "mn", "u30"), 0x4F: ("callpropvoid", "mn", "u30"),
    0x53: ("applytype", "u30"), 0x55: ("newobject", "u30"), 0x56: ("newarray", "u30"), 0x57: ("newactivation",),
    0x58: ("newclass", "u30"), 0x59: ("getdescendants", "mn"), 0x5A: ("newcatch", "u30"), 0x5D: ("findpropstrict", "mn"),
    0x5E: ("findproperty", "mn"), 0x5F: ("finddef", "mn"), 0x60: ("getlex", "mn"), 0x61: ("setproperty", "mn"),
    0x62: ("getlocal", "u30"), 0x63: ("setlocal", "u30"), 0x64: ("getglobalscope",), 0x65: ("getscopeobject", "u8"),
    0x66: ("getproperty", "mn"), 0x68: ("initproperty", "mn"), 0x6A: ("deleteproperty", "mn"), 0x6C: ("getslot", "u30"),
    0x6D: ("setslot", "u30"), 0x6E: ("getglobalslot", "u30"), 0x6F: ("setglobalslot", "u30"), 0x70: ("convert_s",),
    0x71: ("esc_xelem",), 0x72: ("esc_xattr",), 0x73: ("convert_i",), 0x74: ("convert_u",), 0x75: ("convert_d",),
    0x76: ("convert_b",), 0x77: ("convert_o",), 0x78: ("checkfilter",), 0x80: ("coerce", "mn"), 0x81: ("coerce_b",),
    0x82: ("coerce_a",), 0x83: ("coerce_i",), 0x84: ("coerce_d",), 0x85: ("coerce_s",), 0x86: ("astype", "mn"),
    0x87: ("astypelate",), 0x88: ("coerce_u",), 0x89: ("coerce_o",), 0x90: ("negate",), 0x91: ("increment",),
    0x92: ("inclocal", "u30"), 0x93: ("decrement",), 0x94: ("declocal", "u30"), 0x95: ("typeof",), 0x96: ("not",),
    0x97: ("bitnot",), 0xA0: ("add",), 0xA1: ("subtract",), 0xA2: ("multiply",), 0xA3: ("divide",), 0xA4: ("modulo",),
    0xA5: ("lshift",), 0xA6: ("rshift",), 0xA7: ("urshift",), 0xA8: ("bitand",), 0xA9: ("bitor",), 0xAA: ("bitxor",),
    0xAB: ("equals",), 0xAC: ("strictequals",), 0xAD: ("lessthan",), 0xAE: ("lessequals",), 0xAF: ("greaterthan",),
    0xB0: ("greaterequals",), 0xB1: ("instanceof",), 0xB2: ("istype", "mn"), 0xB3: ("istypelate",), 0xB4: ("in",),
    0xC0: ("increment_i",), 0xC1: ("decrement_i",), 0xC2: ("inclocal_i", "u30"), 0xC3: ("declocal_i", "u30"),
    0xC4: ("negate_i",), 0xC5: ("add_i",), 0xC6: ("subtract_i",), 0xC7: ("multiply_i",), 0xD0: ("getlocal_0",),
    0xD1: ("getlocal_1",), 0xD2: ("getlocal_2",), 0xD3: ("getlocal_3",), 0xD4: ("setlocal_0",), 0xD5: ("setlocal_1",),
    0xD6: ("setlocal_2",), 0xD7: ("setlocal_3",), 0xEF: ("debug", "u8", "u30", "u8", "u30"), 0xF0: ("debugline", "u30"),
    0xF1: ("debugfile", "u30"),
}


def disasm(a, code):
    r = R(code); out = []
    while r.i < len(code):
        at = r.i; op = r.u8(); spec = OPS.get(op)
        if spec is None: out.append(f"{at:4d} ?{op:02x}"); break
        args = []
        if op == 0x1B:
            base = r.s24(); n = r.u30(); cases = [r.s24() for _ in range(n + 1)]
            args = [f"default {at + base}", f"cases {[at + c for c in cases]}"]
        for kind in spec[1:]:
            if kind == "u8": args.append(r.u8())
            elif kind == "u30": args.append(r.u30())
            elif kind == "s24": off = r.s24(); args.append(f"-> {r.i + off}")
            elif kind == "str": args.append(repr(a["strings"][r.u30()]))
            elif kind == "int": args.append(a["ints"][r.u30()])
            elif kind == "uint": args.append(a["uints"][r.u30()])
            elif kind == "dbl": args.append(a["doubles"][r.u30()])
            elif kind == "mn": args.append(a["mname"](r.u30()))
        if spec[0] in ("debugline", "debugfile", "debug"): continue
        out.append(f"{at:4d} {spec[0]} {' '.join(str(x) for x in args)}")
    return out


if __name__ == "__main__":
    movie, b = dump(glob.glob(sys.argv[1], recursive=True)[0])
    p, length = movie["abc"][0]
    q = b.index(0, p + 4) + 1
    a = parse(b[q:p + length])
    pat = re.compile(sys.argv[2])
    for i, m in enumerate(a["methods"]):
        full = m.get("full", m["name"])
        if pat.search(full) and i in a["bodies"]:
            print(f"\n### {full} (method {i}, {m['params']} params)")
            print("\n".join(disasm(a, a["bodies"][i])))
