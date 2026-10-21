"""Decode Tekken body motion samples (0x64 and 0xC8), without the game process.

Format reference: Kiloutre/TKMovesets, Utils/Animations_T_x64.cpp.
The 0x64 stream stores a base pose, independent 16-frame blocks, and
run-length-coded signed acceleration residuals. Samples retain native units
and channel order; retargeting is deliberately separate from decompression.
"""
from dataclasses import dataclass
import math
import struct

from .moveset import MovesetError

ANGLE_UNIT = 0.000095873802


@dataclass
class Motion:
    format: str
    descriptors: list[int]
    samples: list[list[tuple[float, float, float]]]

    def to_c8(self) -> bytes:
        """Portable, uncompressed little-endian Tekken clip (not a Unity .anim)."""
        channels = len(self.descriptors)
        header = struct.pack("<HHI", 0xC8, channels, len(self.samples))
        header += struct.pack(f"<{channels}I", *self.descriptors)
        # 0x64 rotations are spherical axis-angle, not Euler triples. C8 uses
        # Euler angles. Copying the decoded triples verbatim twists the rig.
        samples = self.euler_samples()
        values = [v for frame in samples for channel in frame for v in channel]
        return header + struct.pack(f"<{len(values)}f", *values)

    def euler_samples(self):
        if self.format != "0x64":
            return self.samples
        return [[spherical_rotation_to_euler(v) if 4 <= d <= 7 else v
                 for d, v in zip(self.descriptors, frame)] for frame in self.samples]


def spherical_rotation_to_euler(value):
    """Native 0x64 axis elevation, azimuth, angle -> C8 Euler XYZ.

    The game's quaternion builder at RVA 0x7308D2 (installed 5.10 image)
    constructs axis=(cos(a)*sin(b), -sin(a), cos(a)*cos(b)) and uses c/2.
    C8's Euler convention represents the inverse of that quaternion.
    """
    a, b, c = value
    sine = math.sin(-c / 2)
    x = math.cos(a) * math.sin(b) * sine
    y = -math.sin(a) * sine
    z = math.cos(a) * math.cos(b) * sine
    w = math.cos(c / 2)
    return (
        math.atan2(2*(w*x+y*z), 1-2*(x*x+y*y)),
        math.asin(max(-1.0, min(1.0, 2*(w*y-z*x)))),
        math.atan2(2*(w*z+x*y), 1-2*(y*y+z*z)),
    )


class _Bits:
    def __init__(self, data):
        if not data:
            raise MovesetError("empty motion residual")
        self.data, self.bit = data, 6  # first six bits encode chunk byte length

    def read(self, count):
        if self.bit + count > len(self.data) * 8:
            raise MovesetError("motion residual exceeds its chunk")
        value = 0
        for _ in range(count):
            value = (value << 1) | ((self.data[self.bit // 8] >> (7-self.bit % 8)) & 1)
            self.bit += 1
        return value


def _signed16(value):
    return ((value + 32768) & 65535) - 32768


def decode_motion(data: bytes, endian: str = "<") -> Motion:
    """Return every original sample. Reject truncation and invalid headers.

    Native body clips use 23 channels; other channel counts are decoded but
    are not automatically interpreted as a human skeleton.
    """
    if endian not in ("<", ">"):
        raise ValueError("endian must be < or >")

    def read(fmt, offset):
        size = struct.calcsize(endian + fmt)
        if offset < 0 or offset + size > len(data):
            raise MovesetError("motion data truncated")
        return struct.unpack_from(endian + fmt, data, offset)

    kind, channels = read("HH", 0)
    if not 0 < channels < 256:
        raise MovesetError("invalid motion channel count")
    if kind == 0xC8:
        frames, = read("I", 4)
        if not 0 < frames <= 65535:
            raise MovesetError("invalid motion frame count")
        descriptors = list(read(f"{channels}I", 8))
        values = read(f"{frames*channels*3}f", 8+channels*4)
        samples = [[tuple(values[(f*channels+c)*3:(f*channels+c+1)*3])
                    for c in range(channels)] for f in range(frames)]
    elif kind == 0x64:
        descriptors = list(read(f"{channels}H", 4))
        header = 4 + channels*2
        frames, shift, float_shift, floats = read("HBBH", header)
        shift &= 127
        is_short = [4 <= d <= 7 for d in descriptors]
        if not frames or floats != is_short.count(False) or max(shift, float_shift) > 15:
            raise MovesetError("invalid compressed motion header")
        steps = read(f"{floats}f", header+6)
        pos = header+6+floats*4
        base, integer_base, multipliers = [], [], []
        fi = 0
        for short in is_short:
            v = read("3h" if short else "3f", pos)
            pos += 6 if short else 12
            base.extend([x*ANGLE_UNIT for x in v] if short else v)
            integer_base.extend([x >> shift for x in v] if short else [0]*3)
            multipliers.extend([(1 << shift)*ANGLE_UNIT if short else (1 << float_shift)*steps[fi]]*3)
            if not short:
                fi += 1
        flat = [base.copy() for _ in range(frames)]
        if frames > 1:
            # The base pose is sample zero; blocks encode the remaining N-1.
            blocks = (frames+14)//16
            offsets = read(f"{blocks}I", pos)
            width = bias = 0
            for block, offset in enumerate(offsets):
                if offset < 4*blocks or pos+offset >= len(data):
                    raise MovesetError("invalid motion block offset")
                cursor = pos+offset
                count = min(16, frames-1-block*16)
                for axis in range(channels*3):
                    if cursor >= len(data):
                        raise MovesetError("truncated motion block")
                    size = data[cursor] >> 2
                    if not size or cursor+size > len(data):
                        raise MovesetError("invalid motion chunk size")
                    bits = _Bits(data[cursor:cursor+size])
                    cursor += size
                    repeat = 0

                    def residual():
                        nonlocal repeat, width, bias
                        if repeat:
                            repeat -= 1
                        else:
                            code = bits.read(4)
                            if code == 14:
                                run = bits.read(4)
                                if run == 0:
                                    raise MovesetError("zero motion repeat length")
                                repeat = run-1
                            elif code == 15:
                                width, bias = 99, 0
                            elif code == 13:
                                width, bias = 16, 0
                            elif code == 0:
                                width, bias = 0, 0
                            else:
                                width = 2*((code+1)//2)
                                bias = ((1 << width)-1)//3 if code % 2 else -4*((1 << width)-1)//3
                        if width == 99:
                            return None
                        raw = bits.read(width) if width else 0
                        return (_signed16(raw) if width == 16 else raw)+bias

                    first = residual()
                    if first is None:
                        raise MovesetError("missing motion block anchor")
                    value = _signed16(integer_base[axis]+first)
                    velocity = 0
                    ended = False
                    for sub in range(count):
                        if sub and not ended:
                            delta = residual()
                            if delta is None:
                                ended = True
                            else:
                                velocity += delta
                                value = _signed16(value+velocity)
                        frame = 1+block*16+sub
                        flat[frame][axis] = value*multipliers[axis] + (0 if is_short[axis//3] else base[axis])
        samples = [[tuple(frame[c*3:c*3+3]) for c in range(channels)] for frame in flat]
    else:
        raise MovesetError(f"unsupported motion type 0x{kind:X}")
    if not all(math.isfinite(v) for frame in samples for channel in frame for v in channel):
        raise MovesetError("non-finite motion sample")
    return Motion(f"0x{kind:02X}", descriptors, samples)
