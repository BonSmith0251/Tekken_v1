"""Tekken 7 movesets (tkdata mothead/bin/<chr>mot.bin) and their animation banks.

File layout, verified on all installed characters:

    0x000  MovesetInfo (0x2E8 bytes): name/creator/date string offsets at
           0x08-0x20, 56 original move aliases (u16) at 0x28, 56 current
           aliases at 0x98, the block table (19 x {u64 offset, u64 count})
           at 0x150 and six MOTA offsets at 0x280.
    0x2E8  data area: every offset in the file is relative to this point.
           It starts with the name/animation-name strings, then the
           record blocks.

Unlike the in-memory moveset, references between records are list indices
(0xFFFFFFFFFFFFFFFF = none), not addresses. Record layouts follow the
in-memory Tekken 7 structures documented by TKMovesets.

Animation references (Move.anim) are 0x4000|i for the character bank
(mask/<chr>rotsm.arc, first MOTA), 0x8000|i for the shared bank
(mask/common.arc) and plain i for the small bank inside the moveset file.
"""

from __future__ import annotations

import struct
from dataclasses import dataclass, field

DATA_BASE = 0x2E8
NONE = 0xFFFFFFFFFFFFFFFF
REQ_END = 881
CANCEL_END = 0x8000
AUTO = 0x800B
SEQUENCE_BASE = 0x800D

BLOCKS = [  # name, record size
    ("reactions", 112), ("requirements", 8), ("hit_conditions", 24), ("projectiles", 168),
    ("pushbacks", 16), ("pushback_extra", 2), ("cancels", 40), ("group_cancels", 40),
    ("cancel_extra", 4), ("extra_props", 12), ("start_props", 16), ("end_props", 16),
    ("moves", 176), ("voiceclips", 4), ("input_sequences", 16), ("inputs", 8),
    ("parry_related", 4), ("throw_cameras", 12), ("throws", 16)]

DIRECTIONS = [(0x2, "d/b"), (0x4, "d"), (0x8, "d/f"), (0x10, "b"), (0x20, "n"),
              (0x40, "f"), (0x80, "u/b"), (0x100, "u"), (0x200, "u/f")]
BUTTONS = [(0x1, "1"), (0x2, "2"), (0x4, "3"), (0x8, "4"), (0x10, "RA")]
SPECIAL = {0x8000: "end", 0x8001: "f,f", 0x8002: "b,b", AUTO: "auto"}


class MovesetError(Exception):
    pass


def _directions(bits: int) -> str:
    names = [n for b, n in DIRECTIONS if bits & b]
    return "|".join(names)


def _buttons(bits: int) -> str:
    names = [n for b, n in BUTTONS if bits & b]
    return "+".join(names)


@dataclass
class Moveset:
    data: bytes
    character: str
    creator: str
    date: str
    build: str
    aliases: list[int]
    blocks: dict = field(default_factory=dict)
    motas: list[int] = field(default_factory=list)

    # -- low level ---------------------------------------------------------
    def string(self, offset: int) -> str:
        start = DATA_BASE + offset
        end = self.data.index(b"\0", start)
        return self.data[start:end].decode("utf-8", "replace")

    def record(self, block: str, index: int) -> bytes:
        offset, count, size = self.blocks[block]
        if not 0 <= index < count:
            raise MovesetError(f"{block} index {index} out of range ({count})")
        start = DATA_BASE + offset + index * size
        return self.data[start:start + size]

    def count(self, block: str) -> int:
        return self.blocks[block][1]

    def resolve_move(self, move_id: int) -> int:
        """Alias ids (0x8000 + n) point at the n-th original alias."""
        if 0x8000 <= move_id < 0x8000 + len(self.aliases):
            return self.aliases[move_id - 0x8000]
        return move_id

    # -- decoded records ---------------------------------------------------
    def requirements(self, index: int) -> list[dict]:
        out = []
        while index != NONE and index < self.count("requirements") and len(out) < 256:
            cond, param = struct.unpack("<II", self.record("requirements", index))
            if cond == REQ_END:
                break
            out.append({"condition": cond, "param": param})
            index += 1
        return out

    def command(self, cmd: int) -> str:
        direction, button = cmd & 0xFFFFFFFF, cmd >> 32
        if direction >= 0x8000:
            if direction in SPECIAL:
                text = SPECIAL[direction]
            elif SEQUENCE_BASE <= direction < SEQUENCE_BASE + self.count("input_sequences"):
                text = self.sequence(direction - SEQUENCE_BASE)
            else:
                text = f"special:0x{direction:X}"
        else:
            text = _directions(direction)
        pressed = _buttons(button & 0xFFFF)
        if pressed:
            text = f"{text}+{pressed}" if text else pressed
        return text

    def sequence(self, index: int) -> str:
        window, amount, _, first = struct.unpack("<HHiQ", self.record("input_sequences", index))
        parts = []
        for i in range(first, min(first + amount, self.count("inputs"))):
            (cmd,) = struct.unpack("<Q", self.record("inputs", i))
            parts.append(_directions(cmd & 0xFFFFFFFF) +
                         (("+" + _buttons(cmd >> 32 & 0xFFFF)) if cmd >> 32 & 0xFFFF else ""))
        return ",".join(p for p in parts if p) + f" (within {window}f)"

    def cancels(self, index: int, block: str = "cancels") -> list[dict]:
        out = []
        while index != NONE and index < self.count(block) and len(out) < 4096:
            cmd, req, extra, start, end, starting, move_id, option = struct.unpack(
                "<QQQIIIHH", self.record(block, index))
            if cmd == CANCEL_END:
                break
            target = self.resolve_move(move_id)
            entry = {"input": self.command(cmd), "command": f"0x{cmd:016X}",
                     "window": [start, end], "starting_frame": starting,
                     "move": target, "option": option}
            if move_id >= 0x8000 and move_id - 0x8000 >= len(self.aliases):
                entry["move"] = None
                entry["group_or_special"] = move_id
            reqs = self.requirements(req) if req != NONE else []
            if reqs:
                entry["requirements"] = reqs
            if extra != NONE and extra < self.count("cancel_extra"):
                entry["extra"] = struct.unpack("<I", self.record("cancel_extra", extra))[0]
            out.append(entry)
            index += 1
        return out

    def hit_conditions(self, index: int) -> list[dict]:
        out = []
        while index != NONE and index < self.count("hit_conditions") and len(out) < 64:
            req, damage, _, reaction = struct.unpack("<QIIQ", self.record("hit_conditions", index))
            reqs = self.requirements(req) if req != NONE else []
            out.append({"damage": damage, "requirements": reqs,
                        "reaction": None if reaction == NONE else reaction})
            if not reqs:            # the unconditional entry closes the list
                break
            index += 1
        return out

    def extra_props(self, index: int) -> list[dict]:
        out = []
        while index != NONE and index < self.count("extra_props") and len(out) < 512:
            frame, prop, value = struct.unpack("<III", self.record("extra_props", index))
            if prop == 0:
                break
            out.append({"frame": frame, "id": prop, "value": value})
            index += 1
        return out

    def reaction(self, index: int) -> dict:
        r = self.record("reactions", index)
        pushbacks = struct.unpack_from("<7Q", r, 0)
        moves = struct.unpack_from("<16H", r, 0x4E)
        names = ["standing", "default", "crouch", "counterhit", "crouch_counterhit", "left_side",
                 "crouch_left_side", "right_side", "crouch_right_side", "backturned",
                 "crouch_backturned", "block", "crouch_block", "wallslump", "downed"]
        return {"pushbacks": [None if p == NONE else p for p in pushbacks],
                "vertical_pushback": struct.unpack_from("<H", r, 0x4C)[0],
                "moves": {n: m for n, m in zip(names, moves)}}

    def move(self, index: int, *, cancels: bool = True) -> dict:
        r = self.record("moves", index)
        name, anim_name, anim = struct.unpack_from("<QQQ", r, 0)
        vuln, hitlevel = struct.unpack_from("<II", r, 0x18)
        cancel = struct.unpack_from("<Q", r, 0x20)[0]
        transition = struct.unpack_from("<H", r, 0x54)[0]
        hitcond = struct.unpack_from("<Q", r, 0x60)[0]
        airborne = struct.unpack_from("<III", r, 0x6C)
        voice, extra, start_p, end_p = struct.unpack_from("<QQQQ", r, 0x78)
        hitbox, first, last = struct.unpack_from("<III", r, 0x9C)
        distance = struct.unpack_from("<H", r, 0xAA)[0]
        bank = "character" if anim & 0xC000 == 0x4000 else "common" if anim & 0x8000 else "moveset"
        out = {"id": index, "name": self.string(name), "animation": self.string(anim_name),
               "anim": {"bank": bank, "index": anim & 0x3FFF, "ref": anim},
               "vuln": vuln, "hitlevel": hitlevel, "transition": self.resolve_move(transition),
               "airborne": {"start": airborne[0], "end": airborne[1], "ground_fall": airborne[2]},
               "hitbox_location": hitbox, "distance": distance}
        if first:
            out["active_frames"] = [first, last]
        if hitcond != NONE and hitcond:
            hits = self.hit_conditions(hitcond)
            out["hit_conditions"] = hits
            if hits:
                out["damage"] = hits[-1]["damage"]
        if voice != NONE:
            out["voiceclip"] = struct.unpack("<I", self.record("voiceclips", voice))[0]
        if extra != NONE:
            props = self.extra_props(extra)
            if props:
                out["properties"] = props
        if cancels and cancel != NONE:
            out["cancels"] = self.cancels(cancel)
        return out

    def moves(self) -> list[dict]:
        return [self.move(i) for i in range(self.count("moves"))]


def parse_moveset(data: bytes) -> Moveset:
    if len(data) < DATA_BASE + 16:
        raise MovesetError("file is too small for a Tekken 7 moveset")
    blocks = {}
    for i, (name, size) in enumerate(BLOCKS):
        offset, count = struct.unpack_from("<QQ", data, 0x150 + 16 * i)
        if count and DATA_BASE + offset + count * size > len(data):
            raise MovesetError(f"{name} block exceeds the file")
        blocks[name] = (offset, count, size)
    if not blocks["moves"][1]:
        raise MovesetError("moveset has no moves")
    ms = Moveset(data, "", "", "", "", list(struct.unpack_from("<56H", data, 0x28)), blocks,
                 [struct.unpack_from("<Q", data, 0x280 + 16 * i)[0] for i in range(6)])
    ms.character, ms.creator, ms.date, ms.build = (
        ms.string(struct.unpack_from("<Q", data, o)[0]) for o in (0x08, 0x10, 0x18, 0x20))
    # Sanity: a real moveset's first cancel list resolves to known moves.
    if any(a >= blocks["moves"][1] for a in ms.aliases if a != 0xFFFF):
        raise MovesetError("move aliases point outside the move table")
    return ms


# -- animation banks -----------------------------------------------------------

@dataclass
class AnimInfo:
    format: str          # "0x64" (keyframe-compressed) or "0xC8" (raw floats)
    frames: int
    channels: int
    offset: int
    size: int


class MotaBank:
    """A MOTA animation list (big- or little-endian) inside a larger buffer."""

    def __init__(self, data: bytes, offset: int, end: int | None = None):
        if data[offset:offset + 4] != b"MOTA":
            raise MovesetError("missing MOTA header")
        self.data, self.offset = data, offset
        self.end = len(data) if end is None else end
        big = data[offset + 5] == 1 or data[offset + 4] == 0
        self.endian = ">" if big else "<"
        (count,) = struct.unpack_from(self.endian + "I", data, offset + 0x0C)
        if count > 100_000:
            raise MovesetError("implausible MOTA animation count")
        self.offsets = [offset + o for o in struct.unpack_from(f"{self.endian}{count}I", data, offset + 0x14)]

    def __len__(self) -> int:
        return len(self.offsets)

    def info(self, index: int) -> AnimInfo:
        start = self.offsets[index]
        following = sorted({o for o in self.offsets if o > start})
        stop = following[0] if following else self.end
        e = self.endian
        kind = self.data[start + 1] if e == ">" else self.data[start]
        if kind == 0xC8:
            channels = struct.unpack_from(e + "H", self.data, start + 2)[0]
            frames = struct.unpack_from(e + "I", self.data, start + 4)[0]
            size = 8 + channels * 4 + channels * 12 * frames
            return AnimInfo("0xC8", frames, channels, start, size)
        if kind == 0x64:
            channels = struct.unpack_from(e + "H", self.data, start + 2)[0]
            frames = struct.unpack_from(e + "H", self.data, start + 4 + 2 * channels)[0]
            return AnimInfo("0x64", frames, channels, start, stop - start)
        raise MovesetError(f"unknown animation type 0x{kind:02X}")

    def raw(self, index: int) -> bytes:
        i = self.info(index)
        return self.data[i.offset:i.offset + i.size]

    def decode_c8(self, index: int) -> list[list[tuple]]:
        """Raw 0xC8 frames: per frame, per channel, three floats (as stored)."""
        i = self.info(index)
        if i.format != "0xC8":
            raise MovesetError("only 0xC8 animations are decoded; 0x64 is compressed")
        base = i.offset + 8 + 4 * i.channels
        values = struct.unpack_from(f"{self.endian}{i.channels * 3 * i.frames}f", self.data, base)
        per = i.channels * 3
        return [[tuple(values[f * per + c * 3:f * per + c * 3 + 3]) for c in range(i.channels)]
                for f in range(i.frames)]


def character_bank(arc: bytes) -> MotaBank:
    """First MOTA of a <chr>rotsm.arc archive (count, then offset/size pairs)."""
    (count,) = struct.unpack_from("<I", arc, 0)
    if not 0 < count < 64:
        raise MovesetError("not a MOTA archive")
    first, size = struct.unpack_from("<II", arc, 4)
    return MotaBank(arc, first, first + size)


def moveset_bank(ms: Moveset) -> MotaBank | None:
    offset = ms.motas[0]
    if not offset:
        return None
    return MotaBank(ms.data, DATA_BASE + offset)
