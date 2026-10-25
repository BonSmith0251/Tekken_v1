"""Reference command interpreter for an exported Tekken 7 command graph.

Mirrors Assets/_Project/Scripts/Runtime/CommandInterpreter.cs so the runtime can be
checked against the original movelist demo inputs outside Unity. Semantics:
  * Inputs are relative to facing: numpad directions as bit flags (1 d/b ... 9 u/f)
    and buttons 1-4 as bits 1, 2, 4, 8.
  * Each 60 Hz frame the current move's cancels are scanned in order; a group
    cancel (0x800B) scans its group-cancel list in place, up to 0x800C.
  * A cancel matches when its requirements hold, the move frame is inside its
    detection window and its command matches the input history. A match is
    buffered and taken on the cancel's starting frame (or at once if later).
  * The list's end entry (0x8000) is itself a cancel: from its start frame the
    move recovers into its target. Otherwise the animation end takes the transition.
  * The latest press group (buttons pressed within PRESS_MERGE frames of each
    other) and the direction held when it was pressed stay usable for
    PRESS_BUFFER frames, across move changes, until a cancel consumes them.
    While an input cancel is queued no other cancel is read, so a follow-up press
    waits for the next move instead of being consumed twice.
"""
import json
import random
from pathlib import Path

DIR_BITS = {1: 0x2, 2: 0x4, 3: 0x8, 4: 0x10, 5: 0x20, 6: 0x40, 7: 0x80, 8: 0x100, 9: 0x200}
NAMES = {"d/b": 1, "d": 2, "d/f": 3, "b": 4, "n": 5, "f": 6, "u/b": 7, "u": 8, "u/f": 9}
END, DOUBLE_F, DOUBLE_B, GROUP, GROUP_END, SEQUENCE_BASE = 0x8000, 0x8001, 0x8002, 0x800B, 0x800C, 0x800D
PRESS, ANY_OF = 0x40000000, 0x20000000
DOUBLE_TAP_FRAMES = 12
PRESS_BUFFER = 60            # frames a press group stays usable until a cancel consumes it
BACKTURNED_YAW = 90          # degrees of hip yaw from the stance that count as backturned
PRESS_MERGE = 2              # presses this close together form one group (1+2)
OPEN = 32767


def parse_input(text):
    """'d/f+1+2' -> (numpad direction, button mask). Upper case means held."""
    parts = text.strip().lower().split("+")
    direction, buttons = 5, 0
    for part in parts:
        if part in NAMES:
            direction = NAMES[part]
        elif part in ("1", "2", "3", "4"):
            buttons |= 1 << (int(part) - 1)
    return direction, buttons


class Context:
    """Match state outside the move graph. Defaults describe a plain versus round."""

    def __init__(self, character_id, opponent_id):
        self.character_id, self.opponent_id = character_id, opponent_id
        self.distance = 2000          # original units (millimetres)
        self.rage = False
        self.backturned = False
        self.opponent_down = False
        self.opponent_crouching = False
        self.opponent_airborne = False
        self.player_side = 0          # 0 = P1 (left), 1 = P2
        self.health = self.opponent_health = 170
        self.max_health = 170
        self.hit_state = "hit"        # result of the current move's attack: hit, block, whiff
        self.counter_hit = False
        self.random = random.Random(1)
        self.unknown = {}             # condition -> bool for unlabeled booleans
        self.frame = 0                # current move frame, set by the fighter before each scan
        self.body_yaw = 0.0           # fighter's body turn (degrees), set before each scan


# Unlabeled boolean conditions observed in Jin/Kazuya graphs. Values were chosen by
# replaying the original movelist demos (see test_movelist_demos.py), not guessed per move.
UNKNOWN_DEFAULTS = {136: False, 167: False, 230: False,
                    251: False, 254: False, 289: True, 356: False, 545: False, 554: False,
                    810: False, 812: False}


def requirement(c, p, ctx, vars_):
    if c >= 0x8000:
        return vars_.get(c, 0) == p
    if c == 0:
        return True
    if c == 1 or 2 <= c <= 30:
        return False                                  # practice demo, throw outcomes
    if c == 35:
        return ctx.distance <= p
    if c == 36:
        return ctx.distance >= p
    if c in (43, 44, 51):
        return ctx.hit_state == "hit"
    if c == 47:
        return ctx.hit_state == "block"
    if c == 48:
        return ctx.hit_state == "whiff"
    if c == 49:
        return ctx.hit_state in ("hit", "block")
    if c == 80:
        return ctx.backturned and ctx.body_yaw < 0    # backturned, turned to one side (side fitted)
    if c == 81:
        return ctx.backturned and ctx.body_yaw >= 0
    if c == 61:
        return False
    if c == 66:
        return not ctx.opponent_crouching
    if c == 67:
        return ctx.opponent_crouching
    if c == 114:
        return ctx.frame <= p                         # own move frame <= X (sidestep taps; fitted)
    if 68 <= c <= 71 or 100 <= c <= 113:
        return False                                  # incoming-attack checks: no attack incoming
    if c == 72:
        return ctx.backturned
    if c == 73:
        return not ctx.backturned
    if c in (92, 96):
        return True
    if 93 <= c <= 99:
        return False
    if c == 126:
        return ctx.opponent_down
    if c == 127:
        return not ctx.opponent_down
    if c == 130:
        return ctx.counter_hit
    if c == 135:
        return ctx.health <= 0                        # on death
    if c in (131, 132, 133):
        return False
    if c == 137:
        return ctx.opponent_health * 2 < ctx.max_health
    if c == 138:
        return ctx.health >= p
    if c == 139:
        return ctx.health < p
    if c == 140:
        return ctx.opponent_health >= p
    if c == 141:
        return ctx.opponent_health > p
    if c == 142:
        return ctx.health * 100 >= p * ctx.max_health
    if c == 143:
        return ctx.health * 100 < p * ctx.max_health
    if c == 144:
        return ctx.opponent_health <= p
    if c == 145:
        return ctx.health <= p
    if c == 146:
        return ctx.player_side == 0
    if c == 147:
        return ctx.player_side == 1
    if c == 150:
        return ctx.player_side == 0                   # on the screen's left side: u steps to own left
    if c == 151:
        return ctx.player_side == 1
    if c in (148, 149, 155, 156):
        return False
    if c == 157:
        return ctx.rage
    if c in (217, 221):
        return ctx.character_id == p
    if c in (218, 222):
        return ctx.character_id != p
    if c in (219, 223):
        return ctx.opponent_id == p
    if c in (220, 224):
        return ctx.opponent_id != p
    if c == 225:
        return p == 0                                 # 0 = human controller
    if c == 552:
        return ctx.random.random() * 100 < p
    if c == 553:
        return ctx.random.random() * 1000 < p
    if c in (556, 558, 559, 560, 563, 564, 566, 569, 570, 573, 634, 636, 637, 765, 811):
        return False                                  # arcade/story/treasure/intro/assist modes
    if c == 579:
        return ctx.player_side == p
    if c == 583:
        return True
    if c == 584:
        return False
    if c == 607:
        return ctx.opponent_airborne
    if c == 608:
        return not ctx.opponent_airborne
    if c in (609, 614, 615, 629, 643):
        return False
    if c == 807:
        return vars_.get(0x8414, 0) >= p
    if c == 808:
        return vars_.get(0x8414, 0) <= p
    if c == 809:
        return vars_.get(0x8414, 0) == p
    if 813 <= c <= 874:
        return False                                  # parries, power crush absorption
    return ctx.unknown.get(c, UNKNOWN_DEFAULTS.get(c, False))


class Fighter:
    def __init__(self, graph, ctx):
        self.g, self.ctx = graph, ctx
        self.move, self.frame = graph["stance"], 0
        self.vars = {}
        self.history = []          # [(direction, held, pressed)] newest last
        self.pending = None        # (target, starting frame, entered by input)
        self.press = (0, 5, 99)    # (buttons, direction at press, age)
        self.turn = 0.0            # body turn carried between moves (degrees)
        self.hold_facing = False   # property 0x81A3: keep the turn instead of re-facing the opponent
        self.log = []              # [(frame index, move id, entered by input)]
        self.tick = 0

    # -- input helpers ----------------------------------------------------
    def dir_matches(self, mask, direction):
        return mask == 0 or bool(mask & DIR_BITS[direction])

    def double_tap(self, numpad):
        """Current frame starts a press of `numpad` that repeats one within the tap window."""
        h = self.history
        if len(h) < 2 or h[-1][0] != numpad or h[-2][0] == numpad:
            return False
        released = False
        for d, _, _ in reversed(h[-DOUBLE_TAP_FRAMES - 1:-1]):
            if d != numpad:
                released = True
            elif released:
                return True
        return False

    def sequence(self, index):
        seq = self.g["sequences"][index]
        steps = self.g["inputs"][seq["inputStart"]:seq["inputStart"] + seq["inputCount"]]
        frames = self.history[-max(1, seq["window"]):]
        if not steps:
            return False
        # The final step must be satisfied now (with its buttons pressed this frame).
        last = steps[-1]
        d, held, _ = frames[-1]
        pressed, press_dir, age = self.press
        lb = (last >> 32) & 0xF
        if lb:
            if age >= PRESS_BUFFER or pressed != lb or age >= len(frames):
                return False
            d = press_dir
            frames = frames[:len(frames) - age]
        if not self.dir_matches(last & 0xFFFFFFFF, d):
            return False
        k = len(steps) - 2
        for d, held, _ in reversed(frames[:-1]):
            if k < 0:
                break
            mask, b = steps[k] & 0xFFFFFFFF, (steps[k] >> 32) & 0xF
            if self.dir_matches(mask, d) and (held & b) == b:
                k -= 1
        return k < 0

    def command(self, cmd):
        direction, word = cmd & 0xFFFFFFFF, cmd >> 32
        buttons = word & 0x1F
        d, held, _ = self.history[-1]
        pressed, press_dir, age = self.press
        live = age < PRESS_BUFFER
        if direction >= 0x8000:
            if direction == DOUBLE_F:
                ok = self.double_tap(6)
            elif direction == DOUBLE_B:
                ok = self.double_tap(4)
            elif direction >= SEQUENCE_BASE and direction - SEQUENCE_BASE < len(self.g["sequences"]):
                ok = self.sequence(direction - SEQUENCE_BASE)
            else:
                return False
            if not ok:
                return False
            direction = 0
        elif not self.dir_matches(direction, press_dir if buttons and word & (PRESS | ANY_OF) else d):
            return False
        if word & PRESS:
            return live and pressed == buttons
        if word & ANY_OF:
            return live and bool(pressed & buttons)
        if buttons:
            return (held & buttons) == buttons
        return True

    def clip_yaws(self, move):
        mv = self.g["moves"][move]
        return self.g["clips"][mv["clip"]].get("yaw", []) if mv.get("clip", -1) >= 0 else []

    @staticmethod
    def authored_backturned(yaws):
        """Backturned moves are animated already turned around, relative to a front-facing body."""
        return bool(yaws) and abs(yaws[0]) > BACKTURNED_YAW

    def body_yaw(self):
        """Body turn from facing the opponent (degrees): carried turn plus the current clip's turn."""
        yaws = self.clip_yaws(self.move)
        if not yaws:
            return self.turn
        now = yaws[min(max(self.frame - 1, 0), len(yaws) - 1)]
        base = 0 if self.authored_backturned(yaws) else yaws[0]
        return (self.turn + now - base + 180) % 360 - 180

    def backturned(self):
        return abs(self.body_yaw()) > BACKTURNED_YAW

    def consume(self):
        self.press = (0, 5, 99)

    def by_input(self, cmd):
        """True when the cancel needs a button press (directly or as a sequence's last step)."""
        low = cmd & 0xFFFFFFFF
        if low >= SEQUENCE_BASE and low - SEQUENCE_BASE < len(self.g["sequences"]):
            seq = self.g["sequences"][low - SEQUENCE_BASE]
            if seq["inputCount"] and (self.g["inputs"][seq["inputStart"] + seq["inputCount"] - 1] >> 32) & 0xF:
                return True
        return bool((cmd >> 32) & 0x1F)

    # -- graph -------------------------------------------------------------
    def requirements(self, start, count):
        req = self.g["requirements"]
        return all(requirement(req[2 * i], req[2 * i + 1], self.ctx, self.vars) for i in range(start, start + count))

    def scan(self, table, index, depth=0):
        entries = self.g[table]
        while 0 <= index < len(entries):
            c = entries[index]
            cmd = c["command"]
            low = cmd & 0xFFFFFFFF
            if low == GROUP_END:
                return None
            if low == END:
                if depth == 0 and c["start"] <= self.frame <= min(c["end"], OPEN) and c["start"] > 0:
                    return c["target"], c["startingFrame"], False, 0
                return None
            index += 1
            if c["reqCount"] and not self.requirements(c["reqStart"], c["reqCount"]):
                continue
            if low == GROUP:
                if depth < 4:
                    hit = self.scan("groupCancels", c["target"], depth + 1)
                    if hit:
                        return hit
                continue
            end = OPEN if c["end"] >= OPEN else c["end"]
            if not (c["start"] <= self.frame <= end):
                continue
            if self.command(cmd):
                return c["target"], c["startingFrame"], self.by_input(cmd), cmd
        return None

    def enter(self, move, by_input=False):
        # A move's rotation carries into the next only while the 0x81A3 hold keeps the fighter
        # turned away; facing front again (or no hold) re-faces the opponent.
        body = self.body_yaw()
        if self.hold_facing and abs(body) > BACKTURNED_YAW:
            self.turn = body
        else:
            self.turn, self.hold_facing = 0.0, False
        self.move, self.frame, self.pending = move, 0, None
        if self.authored_backturned(self.clip_yaws(move)):
            self.turn = 0.0
        self.log.append((self.tick, move, by_input))

    def step(self, direction, held):
        prev = self.history[-1][1] if self.history else 0
        self.history.append((direction, held, held & ~prev))
        new = held & ~prev & 0xF
        buttons, _, age = self.press
        if new:
            self.press = ((buttons | new) if age < PRESS_MERGE else new, direction, 0)
        else:
            self.press = (buttons, self.press[1], min(99, age + 1))
        del self.history[:-120]
        self.tick += 1
        self.frame += 1
        mv = self.g["moves"][self.move]
        for i in range(0, len(mv["properties"]), 3):
            f, pid, value = mv["properties"][i:i + 3]
            if (f & 0x3FFF) == self.frame and pid == 0x81A3:
                self.hold_facing = value != 0
            if f == self.frame and pid >= 0x8000:
                if pid == 0x8415:                     # "Add to 0x8414"
                    self.vars[0x8414] = self.vars.get(0x8414, 0) + value
                else:
                    self.vars[pid] = value
        self.ctx.frame = self.frame
        self.ctx.backturned = self.backturned()
        self.ctx.body_yaw = self.body_yaw()
        # Once an input cancel is queued, later presses stay buffered for the next move.
        if mv["cancelStart"] >= 0 and not (self.pending and self.pending[2]):
            hit = self.scan("cancels", mv["cancelStart"])
            # A buffered input cancel is not replaced by an automatic one.
            if hit and (hit[2] or not self.pending or not self.pending[2]):
                self.pending = hit
                if hit[2]:
                    self.consume()
        if self.pending and self.frame >= self.pending[1]:
            self.enter(self.pending[0], self.pending[2])
            return
        if mv["frames"] and self.frame >= mv["frames"]:
            self.enter(mv["transition"])


def load(path):
    return json.loads(Path(path).read_text(encoding="utf-8"))
