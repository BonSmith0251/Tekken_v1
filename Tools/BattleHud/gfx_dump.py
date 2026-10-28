"""Dump a Scaleform GFX (SWF) movie's sprites, frame labels and per-frame placements as JSON."""
import json, struct, sys, zlib


class Bits:
    def __init__(self, b, pos):
        self.b, self.byte, self.bit = b, pos, 0

    def ub(self, n):
        v = 0
        for _ in range(n):
            v = (v << 1) | ((self.b[self.byte] >> (7 - self.bit)) & 1)
            self.bit += 1
            if self.bit == 8:
                self.bit = 0; self.byte += 1
        return v

    def sb(self, n):
        v = self.ub(n)
        return v - (1 << n) if n and v & (1 << (n - 1)) else v

    def fb(self, n):
        return self.sb(n) / 65536.0

    def align(self):
        if self.bit:
            self.bit = 0; self.byte += 1
        return self.byte


def rect(b, pos):
    r = Bits(b, pos); n = r.ub(5)
    v = [r.sb(n) / 20 for _ in range(4)]
    return v, r.align()


def matrix(b, pos):
    r = Bits(b, pos); m = {"sx": 1, "sy": 1, "r0": 0, "r1": 0}
    if r.ub(1):
        n = r.ub(5); m["sx"] = r.fb(n); m["sy"] = r.fb(n)
    if r.ub(1):
        n = r.ub(5); m["r0"] = r.fb(n); m["r1"] = r.fb(n)
    n = r.ub(5); m["tx"] = r.sb(n) / 20; m["ty"] = r.sb(n) / 20
    return m, r.align()


def cxform_alpha(b, pos):
    r = Bits(b, pos); add = r.ub(1); mul = r.ub(1); n = r.ub(4)
    c = {}
    if mul:
        c["mul"] = [r.sb(n) / 256 for _ in range(4)]
    if add:
        c["add"] = [r.sb(n) for _ in range(4)]
    return c, r.align()


def cstring(b, pos):
    e = b.index(0, pos)
    return b[pos:e].decode("utf-8", "replace"), e + 1


def tags(b, pos, end):
    while pos < end:
        code_len, = struct.unpack_from("<H", b, pos); pos += 2
        code, length = code_len >> 6, code_len & 0x3F
        if length == 0x3F:
            length, = struct.unpack_from("<I", b, pos); pos += 4
        yield code, pos, length
        pos += length
        if code == 0:
            return


def place(b, pos, length, code):
    end = pos + length
    flags = b[pos]; pos += 1
    flags2 = 0
    if code == 70:
        flags2 = b[pos]; pos += 1
    depth, = struct.unpack_from("<H", b, pos); pos += 2
    out = {"depth": depth, "move": bool(flags & 1)}
    if code == 70 and (flags2 & 0x08 or flags2 & 0x10 and flags & 0x02):
        if flags2 & 0x08:
            out["className"], pos = cstring(b, pos)
    if flags & 0x02:
        out["char"], = struct.unpack_from("<H", b, pos); pos += 2
    if flags & 0x04:
        out["m"], pos = matrix(b, pos)
    if flags & 0x08:
        out["cx"], pos = cxform_alpha(b, pos)
    if flags & 0x10:
        out["ratio"], = struct.unpack_from("<H", b, pos); pos += 2
    if flags & 0x20:
        out["name"], pos = cstring(b, pos)
    if flags & 0x40:
        out["clipDepth"], = struct.unpack_from("<H", b, pos); pos += 2
    if code == 70 and flags2 & 0x01:
        blur, pos = skip_filters(b, pos)
        out["blur"] = blur  # [blurX, blurY] stage pixels; [0, 0] when the list has no blur
    if code == 70 and flags2 & 0x02 and pos < end:
        out["blend"] = b[pos]; pos += 1  # 1 normal, 8 add, ...
    if code == 70 and flags2 & 0x04 and pos < end:
        pos += 1                          # cacheAsBitmap
    if code == 70 and flags2 & 0x20 and pos < end:
        out["visible"] = b[pos]; pos += 1 # HasVisible: 0 hides the instance (scripts may show it)
    return out


def skip_filters(b, pos):
    """Read a PlaceObject3 FILTERLIST: returns the blur filter's [blurX, blurY] (the only filter the battle
    message clips use) and skips the others (fixed sizes per filter id; gradient filters carry colour lists)."""
    count = b[pos]; pos += 1
    blur = [0.0, 0.0]
    for _ in range(count):
        fid = b[pos]; pos += 1
        if fid == 0: pos += 23        # drop shadow
        elif fid == 1:                # blur: FIXED blurX, blurY, passes
            bx, by = struct.unpack_from("<ii", b, pos); blur = [bx / 65536, by / 65536]; pos += 9
        elif fid == 2: pos += 15      # glow
        elif fid == 3: pos += 27      # bevel
        elif fid in (4, 7):           # gradient glow / bevel
            n = b[pos]; pos += 1 + n * 5 + 19
        elif fid == 5:                # convolution
            x, y = b[pos], b[pos + 1]; pos += 2 + 8 + x * y * 4 + 4 + 1
        elif fid == 6: pos += 80      # colour matrix
    return blur, pos


def images_and_masks(b, pos, end):
    """GFX external images (tag 1009: id, format, size, export name) and shape bounds. In the battle
    message movie every shape is a solid black rectangle used as a mask (clip layer)."""
    images, shapes = {}, {}
    for code, p, length in tags(b, pos, end):
        if code == 1009:
            cid, fmt, w, h = struct.unpack_from("<IHHH", b, p)
            n = b[p + 10]
            images[cid] = {"name": b[p + 11:p + 11 + n].decode("ascii", "replace"), "w": w, "h": h}
        elif code in (2, 22, 32, 83):
            sid, = struct.unpack_from("<H", b, p)
            shapes[sid] = rect(b, p + 2)[0]
    return images, shapes


def timeline(b, pos, end):
    frames, labels, frame, current = [], {}, 0, []
    for code, p, length in tags(b, pos, end):
        if code in (26, 70):
            current.append(place(b, p, length, code))
        elif code in (5, 28):
            depth, = struct.unpack_from("<H", b, p + (2 if code == 5 else 0))
            current.append({"depth": depth, "remove": True})
        elif code == 43:
            labels[cstring(b, p)[0]] = frame
        elif code == 1:
            frames.append(current); current = []; frame += 1
    return {"frames": len(frames), "labels": labels, "events": {i: f for i, f in enumerate(frames) if f}}


def dump(path):
    data = open(path, "rb").read()
    start = data.find(b"GFX")
    if start < 0:
        start = data.find(b"FWS")
    version = data[start + 3]; size, = struct.unpack_from("<I", data, start + 4)
    b = data[start:start + size]
    r, pos = rect(b, 8)
    rate = b[pos + 1] + b[pos] / 256; count, = struct.unpack_from("<H", b, pos + 2); pos += 4
    movie = {"version": version, "rect": r, "fps": rate, "frames": count, "sprites": {}, "exports": {}, "symbols": {}, "tagCounts": {}}
    root_end = len(b)
    for code, p, length in tags(b, pos, root_end):
        movie["tagCounts"][code] = movie["tagCounts"].get(code, 0) + 1
        if code == 39:
            sid, frames = struct.unpack_from("<HH", b, p)
            movie["sprites"][sid] = timeline(b, p + 4, p + length)
        elif code in (56, 76):
            n, = struct.unpack_from("<H", b, p); q = p + 2
            for _ in range(n):
                cid, = struct.unpack_from("<H", b, q); name, q = cstring(b, q + 2)
                movie["exports" if code == 56 else "symbols"][cid] = name
        elif code == 82:
            movie.setdefault("abc", []).append((p, length))
    movie["root"] = timeline(b, pos, root_end)
    movie["images"], movie["shapes"] = images_and_masks(b, pos, root_end)
    return movie, b


if __name__ == "__main__":
    movie, _ = dump(sys.argv[1])
    movie.pop("abc", None)
    print(json.dumps(movie, indent=1, default=str))


def shape_paths(b, p, code):
    """Decode a DefineShape/2/3/4 into closed paths (lists of (x, y) stage pixels), quadratic curves
    subdivided. Fill sides are ignored: the battle message masks are single solid fills, so an even-odd
    rasterisation of the outlines reproduces them (letter counters stay open)."""
    pos = p + 2
    bounds, pos = rect(b, pos)
    if code in (83,):
        _, pos = rect(b, pos); pos += 1
    alpha = code in (32, 83)

    def styles(pos, first):
        n = b[pos]; pos += 1
        if n == 0xFF:
            n, = struct.unpack_from("<H", b, pos); pos += 2
        for _ in range(n):
            t = b[pos]; pos += 1
            if t == 0x00:
                pos += 4 if alpha else 3
            elif t in (0x10, 0x12, 0x13):
                _, pos = matrix(b, pos); cnt = b[pos] & 15; pos += 1 + cnt * (5 if alpha else 4)
                if t == 0x13: pos += 2
            elif t in (0x40, 0x41, 0x42, 0x43):
                pos += 2; _, pos = matrix(b, pos)
        n = b[pos]; pos += 1
        if n == 0xFF:
            n, = struct.unpack_from("<H", b, pos); pos += 2
        for _ in range(n):
            if code == 83:
                pos += 2; f = struct.unpack_from("<H", b, pos)[0]; pos += 2
                if (f >> 8) & 0x30 == 0x20: pos += 2  # miter
                if f & 0x8: pos += 2  # wait, hasFill handled below
            else:
                pos += 2 + (4 if alpha else 3)
        return pos

    pos = styles(pos, True)
    r = Bits(b, pos)
    fill_bits, line_bits = r.ub(4), r.ub(4)
    paths, current, x, y = [], [], 0.0, 0.0
    while True:
        if r.ub(1) == 0:
            flags = r.ub(5)
            if flags == 0:
                break
            if flags & 1:
                n = r.ub(5); x, y = r.sb(n) / 20, r.sb(n) / 20
                if len(current) > 1: paths.append(current)
                current = [(x, y)]
            if flags & 2: r.ub(fill_bits)
            if flags & 4: r.ub(fill_bits)
            if flags & 8: r.ub(line_bits)
            if flags & 16:  # new styles: not used by these masks
                break
        else:
            straight = r.ub(1); n = r.ub(4) + 2
            if straight:
                if r.ub(1):
                    dx, dy = r.sb(n) / 20, r.sb(n) / 20
                elif r.ub(1):
                    dx, dy = 0, r.sb(n) / 20
                else:
                    dx, dy = r.sb(n) / 20, 0
                x, y = x + dx, y + dy
                if not current: current = [(x - dx, y - dy)]
                current.append((x, y))
            else:
                cx, cy = x + r.sb(n) / 20, y + r.sb(n) / 20
                ax, ay = cx + r.sb(n) / 20, cy + r.sb(n) / 20
                if not current: current = [(x, y)]
                for k in range(1, 9):
                    t = k / 8
                    current.append(((1 - t) ** 2 * x + 2 * (1 - t) * t * cx + t * t * ax, (1 - t) ** 2 * y + 2 * (1 - t) * t * cy + t * t * ay))
                x, y = ax, ay
    if len(current) > 1: paths.append(current)
    return bounds, paths
