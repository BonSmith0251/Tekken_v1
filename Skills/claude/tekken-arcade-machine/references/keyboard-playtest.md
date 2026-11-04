# Playable Unity arcade loop (M3/M4)

Project: `D:/games/Tekken.7.Ultimate.Edition.v5.10/UnityRemaster/TekkenVersusRemaster`.

## User launch and controls

In Unity, save any unsaved scene work, then choose **Remaster > Arcade > Play keyboard test**.
Click the Game view so it receives keyboard input. The menu uses `ArcadeBoot.unity` as a temporary
Play start scene and restores the previous start-scene setting on exit. It does not change Build
Settings or replace the user's open scene. Stop Unity Play normally to exit the test.

| Action | P1 | P2 |
|---|---|---|
| Insert coin | 5 | 6 |
| Start / accept Continue | 1 | 2 |
| Select fighter | A / D | Left / Right |
| Confirm selection | U | Numpad 4 or O |
| Fight movement | W A S D | Arrows |
| LP / RP / LK / RK | U I J K | Numpad 4 5 1 2 or O P L ; |

Default keys are shown above; saved remaps change the displayed prompts.
9 adds a service credit to P1's pool (shared by default). The profile uses legacy keyboard polling;
F2 opens the operator menu, including input test/remapping. Keyboard and Unity joystick-button
keys are supported; board-specific axes, USB/JVS buffering and hardware validation remain. Arcade matches suppress the desktop pause/debug/rematch controls.

Test: insert a coin, start, choose a fighter, fight the CPU. Win to advance through the five-stage
authored ladder. Lose and insert another coin plus Start within ten seconds to continue against
the same opponent on the same stage. Let Continue expire to see GameOver and return to the title.
Selection auto-confirms at twenty seconds. The idle title starts a CPU demo; Start can interrupt it.
Holding a fight button after releasing the prior fight inputs accelerates the continue countdown.
P2 can also start a solo ladder. To challenge P1 during an active round, press 6 then 2. Both players
select independently, using P1 A/D + U and P2 Left/Right + Numpad 4 or O. Each selection locks on
confirmation; timeout confirms any remaining pick. To start direct versus, fund two credits and
press both Starts on the title, or have the other player join before solo selection finishes.
After versus, only the loser may pay Continue to rematch; on timeout the winner resumes the
suspended ladder stage, or starts a new ladder for a direct-versus game. See [challenger-flow.md](challenger-flow.md).

Editor test settings/credits are in `Validation/Arcade/KeyboardTest/`. Missing settings are written
with the labelled provisional defaults. An interrupted paid test is refunded once on the next boot.
Do not manually edit the checksummed ledger or silently promote its backup after corruption.
Standalone cabinet builds and OS lockdown are not part of this step.

## Implementation map and limits

- `Runtime/Arcade/ArcadeFlow.cs`: pure state machine and seeded `ArcadeLadder`. Handles paid session
  creation/closure, per-side solo ownership, deadlines, match IDs, readiness/results and fault refunds.
- `Runtime/ArcadeDirector.cs`: persistent Boot object, keyboard sampling, event batch ownership,
  scene loading, overlays and selection art. Menu controls use fresh edges; cabinet events are
  processed outside fight ticks. Queue items are removed only after successful processing.
- `Runtime/Arcade/ArcadeMatchController.cs`: M2 setup includes an optional stage; VersusGame applies
  it per match without changing saved stage selections. Small pools permit controlled repeats.
- `Editor/ArcadePlaytest.cs`: launch menus, Boot scene creation and automated Play check. Refuses
  to discard dirty scenes and restores `playModeStartScene`. Editor flags: `Temp/setup-arcade`,
  `Temp/play-arcade`, `Temp/check-arcade-play`; queue only after current editor assemblies compile.

The flow supports solo CPU ladders and two-human versus. F2 provides the operator controls described
in [operator-menu.md](operator-menu.md). M6 adds name entry/rankings as described below; original title/challenger movie presentation and an arcade player build remain.
The overlays reuse existing character art; this is the first functional arcade UI. Fight sound is
the existing game mix; attract-volume policy and countdown announcements are not implemented.

On a load or storage fault the screen stops and writes `arcade-error.txt` beside the test ledger,
including any unacknowledged input IDs. Pending paid entitlements are refunded when storage is
healthy; uncertain storage failures require recovery before more starts. An async load already
in progress may finish after the user starts selecting from the demo: the old demo is then
suspended, its stale readiness cannot hijack selection, and the queued paid match loads next.

Keyboard polling cannot catch coin pulses while Unity's main thread blocks, including heavy asset
integration. Real cabinet acceptance requires device buffering/inhibit/acknowledgement tests.

## Verification commands and evidence

From the project directory:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Tools/ArcadeHarness/Run.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Tools/ArcadeHarness/RunMatches.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Tools/ArcadeHarness/RunMatches.ps1 -FlowChecks
```

Counts: 31 core + 22 match + 36 flow + 22 operator + 21 ranking = 132 passing cases. Reports are in `Validation/Arcade/`.

**Remaster > Arcade > Run automated Play check** launches the actual Boot and match scenes with
isolated data under `Validation/Arcade/PlayChecks/<id>`. It injects logical controls and test-forces
two losses to cover paid continue and game-over return, then exits Play. It has shortened timers
and uses the generated arena only for that test; manual testing uses saved stages and normal timers.
It writes `play-validation.json`, `arcade-select.png`, `arcade-fight.png`, and `arcade-continue.png`.
The recorded run passed, including movement in the actual scene and the same opponent/stage after
continue. These injections verify the logical adapter, not delivery from a physical keyboard/board.

**Remaster > Arcade > Run challenger Play check** exercises challenger and direct-versus paths
through actual scenes with logical controls and forced test KOs. Flag: `Temp/check-arcade-challenger`.
Report: `Validation/Arcade/challenger-play-validation.json`; use the boolean fields to distinguish
completed behavior from a partial/failed run. Both Play checks use isolated data and restore the
prior editor start scene; neither verifies physical keyboard rollover or a cabinet coin board.

**Remaster > Arcade > Run operator Play check** / `Temp/check-arcade-operator` checks pause,
coin/Start arbitration, input remapping and persisted keys, real fight resume, settings Apply and
bookkeeping confirmation in isolated data. Report: `Validation/Arcade/operator-play-validation.json`.

## Scores and initials (M6)

Clear at least one CPU stage and finish the run (ending, or let Continue expire). If the run makes
the current rules' top ten, enter three letters: P1 **W/S** changes a letter, **A/D** moves position,
**U** confirms each letter, **I** moves back. P2 uses arrows and Numpad 4/O, Numpad 5/P. Prompts
reflect remapped controls. The default 20-second timeout submits the current letters (AAA initially).
F2 pauses the name timer; coin/service inputs still register, but Start does not pay on name entry.

Ranks sort personal CPU stages cleared, then CPU fight ticks (lower is better), then clean match
wins (no lost round). Continues retain earned stages and failed-fight time. Challenger wins inherit
ladder position but never another player's cleared-stage count. A qualifying versus loser enters a
name before the winner's CPU ladder resumes; the winner's paid entitlement stays open throughout.
Rules with different difficulty/ladder length/round rules/boss/reselect policy have separate tables.

The table appears after initials and between demo/title cycles. **F2 > Rankings** views or clears
the current table, with a confirmation. Credits are unaffected. See [rankings.md](rankings.md).
The Unity menu **Remaster > Arcade > Run ranking Play check** / `Temp/check-arcade-ranking` covers
ending/game-over initials, saving, timeout, F2, coin isolation and confirmed table clear with isolated
test data. Report: `Validation/Arcade/ranking-play-validation.json`.
