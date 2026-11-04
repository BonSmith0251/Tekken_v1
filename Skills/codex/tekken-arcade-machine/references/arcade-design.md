# Arcade design (reviewed proposal, 2026-10-30)

Everything marked **default** is a proposal the user hasn't confirmed. Change it here when they do.
M1's settings/ledger/input, M2's match hooks, M3's keyboard loop and M4's challenger/direct versus flow now exist; see
[core-implementation.md](core-implementation.md) and [match-integration.md](match-integration.md)
for their contracts and [keyboard-playtest.md](keyboard-playtest.md) to launch it. M4 details are in [challenger-flow.md](challenger-flow.md).
M5 implements core operator controls and remapping; see [operator-menu.md](operator-menu.md).
M6 implements initials and persistent rankings; see [rankings.md](rankings.md) for the current policy.
Sound/display service pages, physical cabinet adapters and deployment remain proposed.
These are remaster policies, not recovered original Tekken arcade behavior. Read only the sections needed
for the task: input (1), ledger/recovery (2), flow (3), ladder (4), operator menu (5), build (6), tests (7).

## 1. Cabinet controls (ArcadeInput)

Logical controls: per player `Up Down Left Right`, `B1 B2 B3 B4` (LP RP LK RK), `Start`;
cabinet `Coin1`, `Coin2`, `Service`, `Test`, `VolUp`, `VolDown`, optional `Tilt`.

Hardware path **default**: a keyboard encoder (I-PAC style) or USB "zero delay" joystick boards, which
show up as keys/joystick buttons, so the legacy Input Manager keeps working. JVS or a serial coin board
would need a small native or serial reader feeding the same `ArcadeInput` events.

Proposed keyboard-encoder profile (verify actual wiring and encoder configuration):

| Control | P1 | P2 |
|---|---|---|
| Stick | arrows | R F D G |
| B1-B4 | LeftCtrl, LeftAlt, Space, LeftShift | A, S, Q, W |
| Start | 1 | 2 |
| Coin | 5 | 6 |
| Service / Test | 9 / F2 | |

Plan adapters for joystick buttons/axes as well. Preserve the current desktop keyboard profile
(WASD/UIJK, arrows/numpad); select an explicit binding profile rather than changing controls just
because execution is in the editor. Do not assume joystick button indices, polarity, D-pad axes,
or keyboard rollover work on the actual board. Test simultaneous two-player inputs.

Rules:
- Poll in a persistent adapter, queue timestamped events with a sequence number, and process
  cabinet events on an unscaled update independent of the match. Only gameplay commands feed the
  60 Hz fight tick. Keep processing while the match is absent, paused, or held by a VS overlay.
  Never debit a credit in `OnGUI`, which can run more than once per frame.
- `Update` polling alone cannot preserve pulses during synchronous loads or a blocked main thread.
  Use device-side buffering/counters or a suitable background device reader if required by the
  hardware, with a thread-safe queue and no Unity calls off the main thread. Measure minimum pulse
  widths and worst load stalls; report limitations until cabinet tests pass.
- Coin debounce: count a coin on the switch's rising edge only, ignore further edges for
  `coinLockoutMs` (default 80 ms), and treat a switch held over 2 s as a jam (log it, count nothing more
  until released). Apply this independently to each chute using unscaled time; 80 ms is provisional
  and may discard valid pulses from some devices. Establish whether one physical coin emits several
  pulses and normalize that before pricing. Do not coalesce multiple valid queued coins into one flag.
- Opposite directions held together resolve to neutral (SOCD); the fight's interpreter expects that.
- The test menu will have an input test page listing every logical control live, and a "press the
  button for X" remap that writes `arcade-input.json`. Keep Test/Service distinct from gameplay
  bindings; the proposed F2 Test mapping conflicts with today's automatic-fight debug toggle.

## 2. Money (ArcadeLedger)

Settings (in ArcadeSettings, operator-editable):

| Setting | Default | Notes |
|---|---|---|
| `freePlay` | off | shows FREE PLAY, Start always works |
| `coinsPerCredit` (chute 1 / chute 2) | 1 / 1 | e.g. 2 coins = 1 credit |
| `bonusCredits` | none | e.g. 3 coins → 4 credits |
| `creditsToStart` | 1 | |
| `creditsToContinue` | 1 | |
| `creditsToChallenge` | 1 | 2P join-in |
| `sharedCredits` | on | one pool; off = per-side pools on a 2-chute cabinet |
| `maxCredits` | 9 | inhibit/return payment at the cap where hardware supports it; do not silently discard paid value |
| `serviceCredit` | +1 per Service press | logged separately |

Ledger file `arcade-ledger.json` holds: schema version, monotonic generation, credits (per pool),
partial coin progress per chute, outstanding paid-session transactions, and bookkeeping counters (coins per chute,
service credits, games started, continues, challenger matches, play time, attract time, power-ons,
per-fighter picks, CPU wins/losses per ladder stage, last 50 errors). Define bonus thresholds and
rounding explicitly before enabling bonus pricing. Do not lose partial payments at restart or when
pricing changes. With shared credits, decide explicitly whether chute remainders combine.

Persistence/recovery contract:
- Serialize mutations through one ledger owner. A coin update includes balances/remainders and
  counters together. A paid action atomically checks funds, debits once, and creates a transaction
  ID with its session intent. Free play bypasses debit while preserving stored credit balances.
- Write a versioned, validated snapshot to a temporary file on the same volume, flush using the
  platform's durable-write facilities, and replace the committed file while keeping a last-good
  backup. Handle the first save when no destination exists. Atomic replacement avoids partial-file
  visibility; it does not alone guarantee durability on power failure.
- Define a commit boundary. Recover committed generations using validated data, not file mtime;
  do not treat an uncommitted temp file as a completed payment just because it is newer. If a
  journal is used, replay committed IDs once. Never use a recovered backup to blindly repeat a debit.
  M1 uses the primary file as the commit boundary and deliberately faults on a corrupt/missing
  primary with backup/temp evidence. It preserves the backup for operator reconciliation; automatic
  backup promotion is not implemented because it could roll back an acknowledged debit or coin.
- On storage failure, block new paid starts and signal an operator fault. Preserve evidence and
  pending events; use coin inhibit if supported. Missing storage on a fresh installation may start
  empty; corrupt existing money records must not silently reset to zero.
- A debit alone is not a completed paid game. On reboot after debit but before match readiness,
  recover the pending session or issue one recorded refund, using its transaction ID. **Proposed
  default:** refund an interrupted paid session once, then return to attract. Document the
  exploitation/tradeoff and confirm the deployed policy; test failure during refund as well.
  Record when the entitlement is fulfilled or closed (normal game over/declined continue/ending),
  so a completed run cannot be refunded on a later boot. A challenger suspends the incumbent's
  entitlement rather than completing it; keep each payer's transaction distinct.
- At the credit cap, account for accepted overflow as recoverable paid value if the device cannot
  inhibit/return it. Confirm the cap/overflow policy before taking payments on hardware.
- Checkpoint elapsed play/attract time periodically and at transitions; do not rewrite/flush the
  ledger every render frame. Persist money and paid-action transitions immediately.

Exactly-once processing applies to events the adapter has actually observed and durably recorded.
A non-buffered physical coin pulse can be lost before that point. State this limitation and test
device acknowledgement, storage recovery, and real power loss separately.

## 3. Game flow (ArcadeFlow)

```
Boot ─► Attract ◄──────────────────────────────────────────────┐
         │ (credit + Start, or Start on free play)              │
         ▼                                                      │
      Select (countdown) ─► VsScreen ─► Fight ─┬─ win ─► next ladder stage ─► … ─► Ending ─► NameEntry ─┤
         ▲                                     │                                                       │
         │                                     └─ lose ─► Continue (10…0) ─┬─ pay/Start ─► Select or Fight
         │                                                                 └─ timeout ─► GameOver ─► NameEntry ─┘
   Challenger: eligible other-side Start ─► debit once ─► call ─► Select (both) ─► versus match
               ─► loser Continue offer ─┬─ accepts/pays ─► rematch
                                       └─ timeout ─► winner's ladder resumes
Test button ─► suspend play ─► Test menu ─► Attract with pending paid-session recovery applied
```

Flow ordering is part of the contract. For equal timestamps, process coin/service credit before
Start, a valid Start at a deadline before timeout, and terminal match completion before challenger
eligibility. Use deterministic P1-before-P2 ordering for simultaneous Starts competing for one
shared credit. Consume each event once and do not carry a Start into a later eligible state.
Use fresh press edges for paid actions; clear stale gameplay edges at transitions. Use the same
ordering in the harness and the Unity adapter. A single flow owner controls both sides and scenes.

State details:

- **Boot**: load settings and ledger, apply screen settings, hide the cursor, self-check that the
  required Resources load (command graphs, stages, sound). On failure show an operator error screen,
  not a stack trace. Keep settings/ledger faults distinguishable from missing content. Do not
  accept paid starts until boot validation and storage are ready.
- **Attract**: loop through Title (original `SW_UI_Title` art if decoded, otherwise the logo) → a CPU
  vs CPU demo fight on a random saved stage with random fighters (the existing `automaticFight` path,
  one round, no entrance after the first, `attractSound` off/low/on **default low**) → how-to-play card
  (command list) → ranking table → repeat. Overlay always shows CREDIT n / FREE PLAY and blinks INSERT
  COIN or PRESS START. Any Start with enough credit starts a game for that side. The current select
  gate runs even when `automaticFight` is true; attract needs an explicit programmatic match setup
  that bypasses human selection. Do not use smoke-test flags as the production attract API.
- **Select**: the existing select screen, plus a countdown (**default 20 s**, announcer counts the last
  5 with CNT05..CNT00, auto-picks the cursor at 0). In 1P mode the other side shows the CPU tag; the
  CPU's fighter comes from the ladder. A second Start from the other side during select (with credit)
  turns it into a 2P versus game.
  M3 implements a separate timed single-human selector reusing the original portraits; the desktop
  select is unchanged. Second-side joining and countdown voice calls are later work.
- **VsScreen**: existing `VersusLoadingScreen`. Boss stages may use the decoded `SW_UI_Makuai_*` boss
  variants later. Track explicit match-ready/failure signals and an unscaled load deadline. The
  current overlay can dismiss itself after 20 seconds without readiness; that is not permission to
  begin a paid fight. On failure, cancel the pending scene transition, recover its paid transaction,
  and enter a fault state without a retry/debit loop.
- **Fight**: LocalVersus2 with the human side(s) on `ArcadeInput` and the CPU side on `CpuPlayer` at the
  ladder's level. Rules from settings (`roundSeconds` **60**, `roundsToWin` **2**, final-stage
  `bossRoundsToWin` **2**). No pause, no auto-rematch, no match-end panel. On match end VersusGame
  reports `{winner, roundsWon, perfect, timeUp}` to the director, which waits for the victory take to
  finish (or `victoryMaxSeconds` **8**) and moves on.
- **Continue**: dim the battle, show CONTINUE? with a big number from `continueSeconds` (**10**)
  down to 0, announcer `RES_BANK_SYVO_CNTnn` per second. **Proposed default:** only a fresh Start
  with sufficient credit accepts and pays. B1-B4 may speed the countdown (**default on**), never
  also pay for continuation; held fight inputs must be released before acceleration activates.
  An option allowing buttons to pay must disable that conflict and use fresh edges. Continuing goes to Select with the
  old pick highlighted (`continueReselect` **on**) and replays the same ladder stage against the same CPU.
  Inserting a coin during the countdown does not stop it; only an accepted Start does.
- **Challenger**: allowed only while a 1P ladder fight is in progress (`joinIn` **on**). The fight
  stops at a defined simulation boundary after eligibility is checked; the call shows (original `SW_UI_RankMatch_Challenger` art when decoded,
  else text) with `Play_*` announcer sting, both sides go to Select. After the versus match the winner
  retains the suspended ladder stage. **Proposed policy:** finish the loser's timed Continue offer
  before resuming the winner's ladder. If accepted, the loser pays `creditsToContinue` once and both
  return to versus selection; otherwise resume the winner's ladder with the new human owner. Never
  run a ladder and a loser Continue screen concurrently through competing scene owners.
  Accept new challenges only in `MatchPhase.Fight`, not Intro, RoundOver or MatchOver; reject an
  ineligible Start without charging or queuing it for a later fight. KO completion wins a same-tick
  race. Joining a previously entered Select uses `creditsToChallenge`; when both Starts arrive
  together in attract, charge `creditsToStart` per human before transitioning. A direct versus
  game's winner gets a new ladder after the
  loser's Continue offer. A drawn versus match replays without closing either entitlement or charging again. These pricing/ownership policies require confirmation before deployment.
- **GameOver**: 3 s GAME OVER, then NameEntry if the run qualifies, else Attract (or Title if
  credits remain, so the next player can just press Start).
- **NameEntry**: 3 letters with stick + button, 20 s limit, then the ranking (top 10 by stages cleared,
  then total time, then wins without losing a round). Saved in `arcade-ranking.json`.
  M6 implements timed initials and per-rules rankings for qualifying runs with at least one
  personally cleared CPU stage. See [rankings.md](rankings.md).
- **Ending**: after the last ladder stage: the winner's victory take, CONGRATULATIONS, the fighter's
  staging entrance as a short ending (until real endings exist), then NameEntry.

## 4. Ladder (ArcadeLadder)

**Authored default**: `ladderStages` 5: stages 1-4 random fighters from the usable imported roster
(never the player's own fighter, no repeats), stage 5 a fixed boss (**default Kazuya**; the user may want
another). Stages come from the saved `EnvironmentSelection` (no repeats in a run); the final stage uses a
`bossStage` (**default random**). CPU level rises per stage from `difficulty` (operator 1-8, **default 4**):
`level = clamp(difficulty - 1 + stageIndex, 1, 10)` with `stageIndex` defined as 1-based.
Validate that the boss, fighters, command graphs and stages can load before taking a paid start.
When the usable pool is too small, allow controlled repeats or shorten the ladder with an explicit
operator setting; never loop indefinitely searching for a unique pick. Store the seed and selected
order in the session so continue/recovery reuses the same opponent/stage.

M2's CpuPlayer level 1..10 changes guard reaction/chance, getup delay, and pauses between original
movelist demos. These are authored values awaiting play tuning. Level 0 and the existing two-argument
constructor preserve the desktop CPU policy. Punishment selection and autonomous Rage Art use are
not implemented as difficulty features. Keep the original moves/demo inputs; only choices change.

## 5. Operator test menu (ArcadeTestMenu)

M5 implements the core coin/game/input/bookkeeping/diagnostic pages; [operator-menu.md](operator-menu.md)
describes the concrete controls, configuration precedence and remaining service pages.
Opened by the mapped Test edge (F2 in the desktop profile, also retained as an operator recovery key). Suspend play and preserve the
paid session; apply the documented recovery/refund policy when abandoning it. Test must not also
toggle today's F2 CPU-debug action. Pages:
1. Coin settings (everything in §2), free play.
2. Game settings: difficulty, round time, rounds to win, ladder length, boss, continue time, join-in,
   attract sound, attract demo on/off, select timer, blood/effects intensity if wanted.
3. Sound: master and per-bus volumes (reuses `SoundSettings`), speaker test, announcer test.
4. Input test and remap; coin counter test. Clearly distinguish simulated pulses from real paid
   coins: tests must not fabricate revenue. Observe real coins through the same ledger owner.
5. Screen: resolution, refresh, vsync, monitor test pattern (crosshatch, colour bars), orientation.
6. Bookkeeping: all ledger counters; clear bookkeeping with operator confirmation; clear credits
   as a separate deliberate action. Changing bookkeeping must not erase paid balances/sessions.
7. Ranking: view / clear.
8. System: version, build date, uptime, error log, factory reset, exit to Windows (operator only).

M5 settings apply through the explicit Apply-and-return command at idle, after a confirmation;
ordinary Resume discards the draft. Bindings save immediately after capture. The menu is driven by stick + B1 (select) / B2 (back) so it works
without a keyboard.

## 6. Cabinet build and machine setup

- Unity: Boot scene first in Build Settings, then LocalVersus2 (and any arcade-only scene).
  `FullScreenMode.FullScreenWindow` as the proposed default, resolution from settings, cursor hidden and
  locked, `runInBackground` on, single instance, no resolution dialog, `ARCADE_BUILD` scripting define
  that strips debug keys, F-key panels, the tuning panel and editor-only menus. Scope the build's
  scene list, defines, and settings; restore editor/local-versus settings after success or failure.
  Existing `BuildLocalVersus2` explicitly supplies one scene and will not package Boot simply
  because it was added to Build Settings. Verify all required scenes and Resources in the player.
- Build entry `Scripts/Editor/ArcadeBuild.cs`, menu "Remaster/Build arcade player", flag
  `Temp/build-arcade` (content = output folder), report `Validation/Arcade/build.txt`.
- Windows deployment is a separate task on the identified cabinet. Prepare reversible setup and
  rollback instructions in `Docs/ARCADE_SETUP.txt`; do not change the development workstation's
  shell, login, update policy, power plan, or shortcuts merely because an arcade build is requested.
  Candidate settings include a dedicated account/startup task, no sleep during service, scheduled
  maintenance, and disabled distracting overlays. Confirm the target hardware and operating mode.
- Proposed watchdog: a main-thread heartbeat plus a loading grace period longer than measured
  worst-case loads, bounded restart attempts with backoff, and a visible fault after repeated
  failure. An operator exit/maintenance sentinel suppresses relaunch. Recover the ledger/session
  after restart; never let repeated failures consume more credit. A worker-only heartbeat can keep
  running while Unity hangs, so it is not sufficient by itself. Five-second restart is a target to
  validate, not a guarantee. Launch background helpers without stealing focus.
- Logging: `arcade.log` in persistentDataPath, rotated, with coin, start, continue, error events.
- Soak test before shipping: 12 h of attract plus scripted coins/starts (an `--arcade-soak` command
  line that feeds simulated input), watching memory, GPU memory, log size and frame time. The soak
  option does not exist yet. Isolate simulated credits/bookkeeping in a test data directory so a soak
  cannot alter real revenue. Record results separately from any real hardware acceptance test.

## 7. Edge cases the harness must cover

- Coins during Boot, scene loads, the VS screen, KO freeze, continue, name entry, test menu.
- Same-chute bounce, two independent chutes within the lockout, a jammed switch, multi-pulse coins,
  and valid events buffered during a long load. Verify partial coin progress and credit-cap overflow.
- Both Starts pressed on the same tick with one credit (first one in input order wins; the other side
  keeps waiting).
- Start pressed with 0 credits (nothing happens, INSERT COIN flashes).
- Challenger on the KO tick, during RoundOver, or during the victory take: reject without debit
  under the proposed eligibility rule; require a fresh Start in a later fight.
- Continue at 0 exactly; Start and timeout on the same tick (the player wins).
- Failure before/after temp flush, replacement, debit, scene load, match readiness, and refund;
  recover the same transaction without double debit/refund or a lost paid session. Test first save,
  disk full/read-only storage, malformed snapshots, committed backup recovery, and stale temp files.
- Free play toggled while credits are stored (credits stay; hidden until free play is off).
- Clock: all arcade timers use unscaled real time, because the KO near-freeze sets timeScale.
- Settings file missing or corrupt → validated defaults, logged; corrupt money records → operator
  fault/recovery, not an automatic zero balance.
- Duplicate director after scene reload, repeated completion callbacks, held Start crossing screens,
  both players starting with one/two/shared/separate credits, and continue after ownership changes.
- Match-ready never arrives, missing boss/stage, too-small roster/stage pool, operator Test during
  paid play, deliberate operator exit, watchdog restart limit, and free play with stored credits.

Implement only the harnesses relevant to the changed behavior, using the real core sources and a
controlled clock/storage adapter. A logic test cannot prove USB pulse capture, fullscreen behavior,
or power-loss durability; those require player and cabinet tests with recorded evidence.
