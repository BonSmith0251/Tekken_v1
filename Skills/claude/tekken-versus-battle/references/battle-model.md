# Original Tekken 7 battle semantics used by the remaster

Each item below was checked against the game's own movelist demos (the frame-by-frame inputs the
game ships in `<chr>.mvl`). Several were wrong guesses first. Don't change one without re-running
`test_movelist_demos.py` for every fighter and the Unity validation.

## Command graph (exported per fighter)

- **Moves:**
  - fields: `id`, `name`, `frames` (falls back to the clip length; Xiaoyu's catalog has none), `transition`, `firstActive`, `lastActive`, `damage`, `hitlevel`, `vuln`, `airStart`, `airEnd`, `cancelStart`, `hitConditionStart`, `properties` (frame, id, value triples), `clip`.
- **Raw tables:** `cancels`, `groupCancels`, `hitConditions`, `reactions` (resolved through aliases), `requirements`, `sequences`.
- **Inputs:** bit 63 is stripped (it overflows signed int64 JSON) and kept in `inputFlags`.
- **Other tables:** `aliases` (56 slots), `demos`, and `clips` (`forward`/`lateral` in mm, plus hip `yaw` in degrees).

## Cancels and input

- **Group cancels:** cancel command `0x800B` is a group cancel. Its move field indexes `groupCancels`, and the group list ends at command `0x800C`, not `0x8000`. Group lists overlap.
- **List-end entry:** the `0x8000` entry ending a list is a real cancel. From its start frame the move recovers into its target, often before the animation ends.
- **Timing:** each cancel has a detection window `[start, end]` (32767 = open). It executes on `startingFrame`, so an input read early is queued. While an input cancel is queued, no other cancel is read.
- **Button flags:** `0x40000000` means exactly these buttons; `0x20000000` means any of them. Held-button bits sit at `(command >> 40) & 0xF` (1, 2, 4, 8 = buttons 1–4).
- **Input buffer:** the latest press group stays usable for 60 frames across move changes until it's used up. Presses at most 1 frame apart merge (1+2), and the group keeps the direction held at press time.
- **Demo direction bits:** 0x10 up, 0x20 forward, 0x40 down, 0x80 back.

## Requirements

Names come from the TKMovesets editor labels (`editor_t7_requirements.txt`).

- 217/221 self character id; 218/222 not self (Jin 6, Kazuya 9, Xiaoyu 5; Alisa 19 per her profile, numbering not cross-checked; Katarina 21 by roster order only, since her moveset never tests her own id); 223 opponent id.
- 225 controller (0 = human).
- 157 rage (a quarter of health or less).
- 35/36 distance (mm).
- 150/151 and 146/147/579 screen side (150/151 inferred from the sidestep cancels, not confirmed).
- 289 true and 114 own frame ≤ param (the "sidestep tap" reading of 289/114 is inferred).
- 72/73 backturned / not backturned; 80/81 backturned toward one side or the other (NOT "while rising"). Which side is which was chosen arbitrarily.
- 130 counter hit; 135 on death (own health ≤ 0).
- 607/614 opponent airborne / juggle; 126 opponent downed.
- 588 random index (victory take choice).
- IDs ≥ 0x8000 compare a variable that move properties set (0x8415 adds to 0x8414).

## Hits and reactions

- **Which frame hits:** hits use the move and frame that played this tick (`ActedMove`/`ActedFrame`), before that frame's cancels.
- **Hit levels:** hitlevel low byte 0x12 high, 0x17 mid, 0x0F low, 0x1F special low. Highs whiff on crouchers, a downed body is reached only by lows, and lows pass under a jump (not under a juggled body). Guard: back = high/mid, down-back = low; downed and airborne bodies can't guard.
- **Borrowed reactions:** reactions belong to the ATTACKER's moveset. The defender `Borrow`s the reaction in the attacker's graph and returns home through the alias table (`ReturnFromBorrow`). It is held while the attacker stays in the hitting move (`ReleaseHolds`). Always pair a move id with the graph it belongs to. Looking up the defender's old move id in the attacker's graph threw `IndexOutOfRangeException` in `ClipYaws` (via `Borrow`→`Enter`→`BodyYaw`) when Jin hit Xiaoyu, and the borrow time-out path had the same bug. The `Jitter` harness caught it.
- **Reaction slots:** 1 standing, 2 crouch, 3 counter, 4 crouch counter, 9/10 backturned standing/crouching, 11 block, 12 crouch block, 13 downed face up (`dDw_OUC00_`, clip lies head away from the attacker), 14 downed face down (`dDwrOUC00_`, head toward). `BattleModel.LyingHeadForward` picks by the clip's body track (head vs feet forward). Using 14 for every downed body turned face-up bodies end over end, and its tech roll (`Co_DUkm*`, which starts face up) flipped them back: the user's "double fall" (2026-10-29). An airborne body hit by a non-juggle reaction plays alias 30 (`sDw_AIR00_`).
- **Alias slots:** 1 stance, 2 crouch, 3 death, 4–12 downed states, 7/10 face-up and face-down lying loops.
- **Vulnerability:** `vuln` low byte 0x80 = downed and 0x02 = falling body; the air window is `airStart`–`airEnd`. A falling body past its air window counts as downed.

## Move properties used by the scene

- **Shakes:** 0x8001–0x8004 and 0x8006–0x8009.
- **Camera request:** 0x84CC (not a zoom). Low 16 bits become the fighter's sticky camera request, which selects the camera's follow rate (see `Docs/BATTLE_CAMERA_EFFECTS.txt`).
- **Slow-down:** 0x828C. Its value is the level in the high word and the ticks in the low word (inferred). 0x828D on the opponent's move grants immunity.
- **Rage Art cameras:** 0x8439/0x843A point at bank-4 look-at tracks, exported as "T7CF" `.bytes`.
- **Turn hold:** 0x81A3 holds the body turn between moves.

## JRT1 clips

- **Root:** −x forward, +y left, z up.
- **Body heading:** the hip (bone 2) yaw.
- **Presentation remap:** under `BODY_SCALE__group` the root travel lands 90° off the body (the body faces model +z), so presentation remaps `(dx, dy) → (−dy, −dx)`.
- **Backturned clips:** clips authored backturned start with |yaw0| > 90.

## Lying states

- Lying aliases come as face-up/face-down twins (4/8, 6/9, 7/10); reaction slot 13 is face up and 14 face down, picked by `LyingHeadForward`.
- Falls name the face-up twin even when they end face-down (`sDm_ASI10LD` → `dwDWN_00U`), and `sDm_ROL00RD/LD` end sideways. `OrientLying` (`LyingTwins`) picks the twin from the body line with a 60 cm threshold; `KeepLyingLine` turns the heading only for swings over 30°.

## Known approximations (not bugs to "fix" without asking; disclose them)

- Strike reach is a fixed 1.15 m (`CommandReach`, lateral tolerance 0.65).
- Throws, parries, power crush, walls and juggle physics are not simulated, so the movelist sample combos don't connect.
- Rage Art is a single 10-damage hit; its on-hit cutscene is not decoded.
- Slow-down strength is a guess; the 0x828C value split is inferred. Shake presets and the 0x84CC follow rate are original.
- Body collision uses feet-to-head lines (`SeparateTorsos`, `TorsoClearance` 0.5); air+air and air+down bodies still pass through each other by design.
- Standing up after 45 idle ticks on the floor is a convenience, not original.

## Victory takes

- **Real takes:** the w00/w01/w02 takes are the targets of cancels in the winner's moveset, chosen by condition 588 or by a held button.
- **Partner takes:** `_y` takes are the partner side of a paired take, and no cancel targets them.
- **By fighter:** Jin `jz_w00_n` (2), `jz_w01` (2), `jz_w02` (4); Kazuya `kz_w00` (2), `kz_w01_n` (3); Xiaoyu `fu_w00`, `fu_w01_n`, `fu_w02` (all 2); Katarina `ln_w00` (travels 1.6 m forward), `ln_w01` (1.3 m backward). Victory travel is clamped to the arena: across all 17 takes × 8 edge and diagonal starts the worst overshoot fell from 3.7 m to 0 (7 of 136 cases needed the clamp); that check was not saved as a harness.

## Pushbacks (knockback)

- **Layout** (TKMovesets `Structs_t7.h`): a reaction starts with 7 pushback pointers (front,
  backturned, left side, right side, front counter hit, downed, block), then six u16 directions,
  two u32, `vertical_pushback` (0x4C) and the 16 move ids from 0x4E. A pushback is
  `{u16 duration, i16 displacement, u32 num_of_loops, extradata}` with `num_of_loops` i16
  "horizontal offset" values (the `pushbacks` and `pushback_extra` blocks).
- **Reading (inferred):** the offsets are millimetres per frame for their first frames. Jin's jab
  pushes 135 mm on hit and 530 mm on block; plain hits and blocks have duration 0 and use only these.
  The displacement is signed millimetres every frame for `duration` frames; some records have only
  this (a pull-in when negative). Airborne and downed bodies use the downed slot (5).
- **Clips:** reaction clips carry no root travel (sDw_KAO00_, Dw_AIR_ponF: 0 mm), so without
  pushbacks every fall lands in place.
- **Knock-out blow (our choice):** it uses the downed slot (5) like air hits, with a 1 m minimum
  (50 mm × 20 frames) when the reaction has no long slide, so the death fall lands clear of the winner.
