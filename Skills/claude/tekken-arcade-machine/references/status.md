# Arcade status

## 2026-10-30: VS screen no longer freezes on its dim first frame

User report: a dark, frozen VS frame (faint panels, no VS logo) before each match. Activating
LocalVersus2 blocks rendering for seconds (~4.4 s "Integration" in Editor.log), and `BeginLoad` started
the load on the same frame it opened `VersusLoadingScreen`, so the stall held the intro's first frame.
`BeginLoad` now opens the VS screen and `StartPendingLoad` starts the scene load 0.6 s later
(`VersusIntroSeconds`; the panels fade in over 0.5 s and VS pops by 0.5 s), so the stall holds the
finished VS frame. All four Play checks pass. The stall itself remains (scene activation cost).

## 2026-10-30: presentation pass with the original art

New `Scripts/Runtime/ArcadePresentation.cs` (partial ArcadeDirector) and exporter
`Tools/BattleHud/export_arcade_ui.py` → `Resources/BattleHud/Arcade/` (BattleHud import rule applies):
the title movie's ReplaceImages `TIT_OPERATIONMSG_*` (us: INSERT COIN(S), INSERT MORE COIN(S),
PRESS START / P1 / P2 / P1 OR P2 / P1 AND/OR P2 START BUTTON — the arcade build's own operation
messages), `TIT_COPYRIGHT`, staff-roll `Logo_TK7`, information bar `InfoBack3`.
- Title: logo, credit-driven blinking operation message (shared/separate pools, partial coins, free play), copyright.
- Every full page: the select screen's light + red/blue flare backdrop; credits on the info-bar strip
  (text only during play); a waiting challenger sees the other side's PRESS Px START BUTTON in the fight.
- Select: original art/name lettering/slot frames/tags as the desktop select; the CPU side shows the
  random panel + CPU tag (the ladder picks the opponent later); HUD digits for the timer.
- VS: `BeginLoad` already opened `VersusLoadingScreen`, but the opaque "GET READY" page covered it;
  the director now stays clear while it shows, and `VersusLoadingScreen.Hide()` closes it if the load
  is abandoned. Continue: big HUD digits + PRESS Px START BUTTON or the coin message.
- Editor-only keyboard hint line at the top; the cabinet player shows no key hints except select help.
No original art exists in the console data for CONTINUE?, GAME OVER, the arcade challenger call or a
Kazuya boss VS (only SW_UI_Makuai_HEI), so those stay shadowed text. No new sounds.
Checks: compile OK; solo, challenger, operator and ranking Play checks pass (solo now also captures
`arcade-title.png` and `arcade-versus.png` and captures continue while the message blink is lit).

## 2026-10-30: continue screen no longer shows the round call through it

Continue starts while the 2 s K.O./PERFECT call is still running, and VersusGame kept drawing (and
announcing) it under the 85% dim. `ArcadeDirector` now sets `VersusGame.ArcadeCallsHidden` whenever
the match is suspended behind an arcade page (continue, operator menu, game over...); `DrawGameplayGUI`
then draws only the gauges. Compile check passed, Unity recompiled, the automated Play check passed,
and the new `Validation/Arcade/arcade-continue.png` (captured on the first Continue frame) is clean.

## 2026-10-30: stale keyboard-test error archived on boot

The keyboard test's `arcade-error.txt` (02:20, old-ledger load failure) outlived its fix because
`ArcadeDirector` only ever wrote that file. A successful boot now moves it to
`arcade-error.previous.txt` (`ArchiveResolvedError`), so `arcade-error.txt` means a current fault.
Copies of the user's KeyboardTest settings, ledger and ranking load with the current code (ledger
recovers two interrupted paid sessions as refunds); the original files were not modified. Both
assemblies compile, Unity recompiled, and the saved-ledger Play check passed again (07:34 UTC).

## 2026-10-30: independent progress review (Claude)

Re-ran every harness from source: 31 core + 22 match + 36 flow + 22 operator + 21 ranking = 132/132
pass. `compile_check.sh` compiles both assemblies against the current shared sources (VersusGame
etc. as edited by other sessions). The editor's Assembly-CSharp.dll (02:57) is newer than every arcade
source. ArcadeBoot is not in Build Settings yet (expected before M7). Presentation notes from the
captures: the continue screen shows the round call (PERFECT/K.O.) faintly through the dim, and
`arcade-select.png` predates the hint/footer overlap fix, so that fix is uncaptured. The keyboard-test
ledger holds 16 credits against maxCredits 9 by design (overflow retained, display capped). No user
verification on their own controls is recorded yet.

## 2026-10-30: M6 rankings and initials playable

Added persistent top-ten CPU-ladder tables, timed three-letter initials, attract-mode tables and
operator viewing/confirmed clearing. Rules changes select separate tables. Continues retain personal
CPU stats; challenger takeover never inherits the other player's score. A qualifying versus loser
enters initials before the winner resumes, retaining the winner's paid entitlement. F2 pauses name
entry, coins remain live, and Start cannot spend during entry. Ranking storage failures disable
scores without blocking paid gameplay. See [rankings.md](rankings.md) for the authored scoring policy.

Validation: **31 core + 22 match + 36 flow + 22 operator + 21 ranking = 132 passing cases**.
Both Unity assemblies compile. The actual Unity ranking Play check passed twice: ladder completion,
game-over entry, edited/timeout initials, operator pause, coin/Start isolation, disk persistence and
confirmed table clear preserving credit. The final capture has aligned table columns. Automated
checks use isolated saves, logical inputs and forced KOs; physical controls remain unverified.
The operator Play regression also passed after the new menu entry, and the saved-ledger Boot/F2
regression retained the pre-M5 fixture's 13 credits and original counters without rewriting it.

Evidence: `Validation/Arcade/ranking-{validation,play-validation}.json`, `m6-unity-compile.txt`, and
`arcade-{name-entry,rankings,operator-rankings}.png`. Backups and changes are in
`Artifacts/ArcadeRankingsM6/`. Original arcade movies/presentation, sound/display service pages and
the standalone cabinet build remain. M6 does not change the money/settings serialization schema.

## 2026-10-30: fixed loading saves made before the operator update

The user's older keyboard-test ledger failed at boot: DataContractJsonSerializer required the
new bookkeepingClears, creditClears and operatorSettings fields on a Serializable class. Marked
only those additions OptionalField, retaining required original money fields and schema checks.
A regression using the actual pre-M5 snapshot failed before the fix and passes after it. Its
13 stored credits, counters, generation and event ID survive load and upgrade/reopen. The live
user ledger was not modified (before/after SHA-256 matches).

Validation: 22/22 operator cases (two new compatibility cases), 31/31 core cases, both Unity
assemblies compile. Actual Unity Boot plus F2 passed against an isolated byte-for-byte copy of
the old ledger, without rewriting it. Report: `Validation/Arcade/legacy-ledger-play-validation.json`.
Run via **Remaster > Arcade > Run saved ledger Play check** or `Temp/check-arcade-legacy`.
Source/data backups and validation evidence are in `Artifacts/ArcadeLedgerCompatibilityFix/`.
Fresh-file-only tests had missed this upgrade path; retain the old fixture for future schema additions.

## 2026-10-30: M5 core operator controls playable

F2 now opens the operator menu: Coin settings, Game settings, Input test/remapping, Bookkeeping and
System diagnostics. It pauses the paid game and arcade countdowns, continues accounting for coins/
service, and discards Start. Resume preserves the same match/entitlement. Settings apply from idle;
input captures save immediately and require release before another menu action. See
[operator-menu.md](operator-menu.md) for keys and storage precedence.

Operator Apply atomically persists settings together with credit-pool migration inside the ledger.
Legacy settings remain initial defaults; after Apply, embedded operatorSettings is authoritative.
Partial payments block ratio changes; switching shared to separate assigns existing whole credits
to P1, as explained by the confirmation. Separate confirmations distinguish clearing counters from
clearing balances/fractions. Clearing bookkeeping preserves active sessions and all paid value.

Validation: **20/20 operator + 36/36 flow + 22/22 match + 31/31 core = 109 cases pass**; both Unity
assemblies compile. The **actual Unity operator Play check passed**: timed selection stayed paused,
coin/Test/Start arbitration worked, the menu captured and persisted Q as P1 B1, Q confirmed select,
the same paid fight paused/resumed, free play applied at idle while retaining credits, and a confirmed
bookkeeping reset retained credits. The first Play run exposed a held-new-confirm capture loop;
the release gate fixed it, and the final check passed. Menu/input/bookkeeping/confirmation captures
were inspected; a later wording-only change replaces action enum names with readable confirmation labels.

Evidence: `Validation/Arcade/operator-{validation,play-validation}.json`, the existing core/match/flow
reports, `m5-unity-compile.txt`, and `arcade-operator-{home,input,bookkeeping,confirm}.png`.
Backups and change/evidence artifacts: `Artifacts/ArcadeOperatorM5/` in the workspace.
Physical keyboard/cabinet delivery, sound/display service pages, rankings, player deployment and
long-running bookkeeping statistics are not verified/implemented by this step.

## 2026-10-30: M4 challenger and direct two-player flow playable

Implemented independent two-player selection, direct simultaneous Starts, joining an existing solo
selection, and paid challenger entry during an active round. Challenger entry suspends the old match
at the completed tick, shows a two-second text call, then loads both humans. Both sessions remain
separate; versus completion closes only the loser's. A paid loser Continue rematches; timeout lets
the winner inherit the suspended ladder stage, or creates a new ladder after direct versus.
Drawn versus matches replay without another debit. See [challenger-flow.md](challenger-flow.md).

Validation: **36/36 flow + 22/22 match + 31/31 core = 89 cases passed**, including 96,000 ticks of
unchanged desktop CPU regression. Both Unity assemblies compile and the open editor was refreshed.
**Actual Unity challenger Play check passed**: incumbent suspension, both human movement inputs,
paid loser rematch, challenger winner taking over the same CPU opponent/stage, then a direct
simultaneous-start match whose winner gets a new ladder. KOs and inputs were injected for the check;
physical keyboard delivery/rollover and cabinet hardware remain untested.

Launch: **Remaster > Arcade > Play keyboard test**. During P1's active round press **6**, then **2**
to challenge. P1 selects with A/D + U; P2 with arrows + Numpad 4 or O. Direct versus: fund both
players and press both Starts on the title, or join while the first player is still selecting.
The loser alone may pay Continue. Existing keyboard-test data/settings are preserved.

Evidence: `Validation/Arcade/challenger-play-validation.json`, `flow-validation.json`,
`match-validation.json`, `core-validation.json`, `m4-unity-compile.txt`, and challenger/2P screenshots.
Source and prior skill backups: workspace `Artifacts/ArcadeChallengerM4/`.
Original challenger animation/audio, operator menu/remapping, rankings and player deployment remain.

## 2026-10-30: M3 keyboard loop playable and exercised in Unity

Implemented Boot -> title/CPU demo -> coin/Start -> timed single-player select -> VS/loading ->
human-vs-CPU ladder -> victory/next stage or Continue -> GameOver/Ending -> title. Either side can
start a solo ladder. A second human joining an active game is not enabled yet and does not debit.
`ArcadeLadder` uses seeded opponent/stage choices, retains them through continue, and handles small
pools without an endless uniqueness search. Stage overrides do not rewrite saved environment choices.

Launch through **Remaster > Arcade > Play keyboard test**. Read
[keyboard-playtest.md](keyboard-playtest.md) for keys, data location and checks.
The launch menu uses `playModeStartScene`, preserves/restores the prior editor start scene and refuses
to discard unsaved scenes. Normal local-versus startup and Build Settings are unchanged.

Validation: **16/16 flow cases**, **22/22 match cases**, **31/31 core cases** (69 total) passed;
both Unity assemblies compile. **Actual Unity Play check passed**: Boot, simulated coin/Start,
timed select, scene readiness, human movement, test-forced KO, a paid continue with the same
opponent/stage, second loss, countdown expiry, GameOver, and return to title. Test data is isolated.
This exercised the actual Unity match/render path and produced selection/fight/continue screenshots.
It injected logical controls; physical keyboard delivery/cabinet hardware still need the user's test.

Evidence: `Validation/Arcade/{flow-validation,match-validation,core-validation,play-validation}.json`,
`m3-unity-compile.txt`, and `arcade-{select,fight,continue}.png`. A small selection hint/footer overlap
seen in the captured frame was subsequently fixed by shortening the hint's layout rectangle.

## 2026-10-30: M2 match hooks implemented and checked

Added `Arcade/ArcadeMatchController.cs` and `VersusGameArcade.cs`; updated the match driver's
initialization/input/UI guards, optional CPU difficulty, and non-consuming held-button lookup.
Read [match-integration.md](match-integration.md) for the scene request/readiness/completion contract.

Implemented: one-use scene-bound setup/cancellation/failure state, explicit fighter/rules selection
before model creation, per-side human/CPU input, authored difficulty 1..10, terminal result snapshots,
once-only completion, suspension, retained victory playback, and arcade-only debug/rematch guards.
Arcade configuration uses an owned copy of the scene settings. Baseline battle/interpreter code,
move data, scene files and saved settings were not edited.

Validation: **22/22 match cases** and **31/31 core cases** passed. The 22 include real command-graph
matches for all eight fighters and identical default-CPU command/state fingerprints against the
pre-change source across **16 seeded traces / 96,000 ticks**. Both Unity assemblies compile.
Reports: `Validation/Arcade/match-validation.json`, `core-validation.json`, `m2-unity-compile.txt`.

This establishes headless logic and compile compatibility. The Unity Awake/scene/rendering path,
real controls, and cabinet hardware remain untested in Play. The hooks are opt-in; ordinary Play
still opens local versus. M3 must add the director and polling adapter to activate the arcade flow.

## 2026-10-30: M1 core implemented and checked

Added four pure C# files under `Assets/_Project/Scripts/Runtime/Arcade/`: settings, atomic JSON
storage, ledger, and logical input. Added `Tools/ArcadeHarness/CoreChecks.cs` and `Run.ps1`.
Read [core-implementation.md](core-implementation.md) before wiring them into the game.

Implemented: shared/separate balances, partial coins, overflow retention, service accounting,
start/continue/challenge prices, event deduplication, paid-session records, once-only interruption
refunds, file ownership/checksums, fail-closed storage, per-chute debounce/jam reporting, monotonic
event ordering, gameplay tap buffering, neutral opposite directions, and transition release handling.

Validation: **31/31 cases passed**, including faults before/after commit and refund, concurrent
writer rejection, corrupted files, and temporary/persistent Windows file-sharing locks. Both
`Assembly-CSharp` and `Assembly-CSharp-Editor` compile checks passed using Unity's Roslyn/Bee
references. Reports are in `Validation/Arcade/core-validation.json` and `unity-compile.txt`.

No director, hardware polling, live scene integration, Boot scene, player build, or cabinet test
was added/run in this step. The tests use isolated data under `Validation/Arcade/CoreChecksData`.
The pricing/default recovery policy remains provisional. Existing fight code was not edited.

## 2026-10-30: skill reviewed; implementation remains planned

Reviewed the installed Claude skill against the current Unity sources and settings. No arcade
runtime, Boot scene, arcade build hook, or arcade harness was found. This was a documentation
review; no game code, machine settings, credits, or hardware were changed.

Source findings:
- `VersusGame.automaticFight` still controls both CPUs together; `CpuPlayer` has no difficulty knob.
- Character select requires both sides and has no countdown. Human match end waits for input.
- Match setup happens in `Awake`; select picks have private setters, and `StartMatch`/`ReloadScene`
  are private. The proposed director cannot call these as though a public arcade API exists.
- VS loading already has a 20-second visual give-up timeout, but no arcade fault/recovery contract.
- Legacy input uses hard-coded keys and some joystick controls; no arcade input abstraction or
  cursor-hiding code was found in project scripts.
- Build Settings include LocalVersus2 first and sound_settings enabled. `BuildLocalVersus2`
  explicitly packages only LocalVersus2. Player settings remain windowed; run-in-background is on.
- Both decrypted CPU-table files are 35 bytes. No original ladder was recovered in this review.
- Existing compile/refresh helpers exist; proposed arcade harnesses/build flags do not.

Review corrections:
- Narrowed discovery wording and removed mandatory unavailable-tool dependencies.
- Separated observed code, proposed defaults, and cabinet verification.
- Specified unscaled cabinet event processing outside fight ticks, pulse-capture limitations,
  partial coin accounting, recoverable paid sessions, and persistence-failure handling.
- Resolved contradictory continue input, challenger post-match sequencing, and deadline ordering
  as explicit proposed policies; hardware/business choices remain unconfirmed.
- Scoped Windows kiosk changes to actual cabinet deployment and added bounded watchdog recovery.

## Milestones

| # | Milestone | State |
|---|---|---|
| M0 | Confirm hardware, pricing, ladder, monitor and recovery policy | Unconfirmed; does not block pure logic using labelled proposals |
| M1 | Settings, ledger, logical input core and persistence/recovery harness | Implemented; 31/31 core cases and both Unity assembly compile checks pass |
| M2 | Match-driver API: per-side CPU, level, injected rules, completion event, arcade controls | Implemented; 22/22 match cases, 31/31 core regression, legacy replay parity and Unity compile checks pass; actual scene paths exercised in M3/M4 |
| M3 | Flow, director, Boot scene: attract, select, ladder, continue, game over | Playable keyboard test; 16 flow cases and actual Unity continue/game-over loop pass |
| M4 | Challenger and direct 2P flow, shared/per-side credit tests | Implemented; 36 flow cases and actual Unity challenger/rematch/winner/direct-versus check pass |
| M5 | Operator menu, bookkeeping, input remap | Core coin/game/input/bookkeeping/diagnostic pages implemented; 22 operator cases including old-save compatibility pass; sound/display service pages remain |
| M6 | Overlays, name entry/ranking, original UI art where recovered | Initials, persistent rankings, attract tables and confirmed operator clear implemented; 21 ranking cases and actual Unity Play check pass; original presentation remains |
| M7 | Arcade player, cabinet setup instructions, watchdog and soak test | Not started |

Next: test initials/rankings with the user's controls, then prepare the explicit arcade player build
and deployment validation. Sound/display service pages and original presentation remain separate work.
M7 adds the player build and cabinet deployment; host lockdown needs deployment authorization.
Confirm the coin device's pulse/buffer behavior
before promising capture during loads or power loss. Use simulation/free play for provisional testing.

## Verification

- Source audit: completed for the integration points above.
- Arcade core harness: 31/31 passed. Unity assembly compile checks: passed.
- Arcade match harness: 22/22 passed; desktop CPU regression matches 96,000 pre-M2 ticks.
- Flow harness: 36/36 passed. Unity solo and challenger/direct-versus Play checks: passed (M3/M4); physical keyboard
  delivery, standalone player, physical power-cut and cabinet hardware: not tested.
- Operator harness: 22/22 passed, including old-save load/upgrade. Unity operator menu/pause/remap/apply/reset check passed with injected controls.
- Ranking harness: 21/21 passed. Actual Unity initials/ranking/persistence/operator-clear check passed.
- User verification: none recorded. The design's prices, timeouts, ladder and kiosk choices are proposals.
