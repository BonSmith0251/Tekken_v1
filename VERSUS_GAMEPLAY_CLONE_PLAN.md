# Jin versus Kazuya: Versus gameplay clone plan

## What I tried on 20 October 2026

I ran `Builds/Windows/VersusRemaster.exe --smoke-test -batchmode` with rendering enabled. It exited successfully after the scripted ten second run. `runtime-smoke.json` reported two fighters in Fight phase, both retargeted action systems ready, six actions played by Jin and six by Kazuya, and seven resolved hits. The camera capture showed Jin and Kazuya fighting on the Neon Deck stage. This verifies a working automated fight and visible imported characters, not human control, a complete match, or original Tekken 7 presentation. The capture is camera only, so it does not prove that the HUD renders correctly.

The current build opens in automatic combat (`VersusGame.automaticFight = true`). The player can press F2 to toggle manual play, but no automated controller test was performed. The code has no character entrance, Ready pose, scripted round start action, or victory action. Its round announcements are text over live character rendering. KO is a procedural body rotation; guard and stun also use procedural poses. The `MuseumCharacter` assets contain many attack and common loss motions, but searching the action names did not establish a suitable Jin or Kazuya intro or win clip. Exact original Versus camera shots, timings, voice cues, and animation choices still need direct capture and mapping.

## Target sequence and state ownership

Every character action must belong to one explicit phase. The match simulation is frozen outside Fight; animation and camera may still advance. Use this sequence for both players:

| Phase | Character action | Camera/HUD | Advance condition |
| --- | --- | --- | --- |
| `StageLoad` | Spawn both selected costumes at authored anchors; hidden or neutral pose | Fade from versus card; preload arena | Both rigs and required clips ready |
| `EntranceP1` | Jin or selected P1 entrance clip, or authored fallback | P1 close shot; names visible | Clip event or bounded timeout |
| `EntranceP2` | Kazuya or selected P2 entrance clip, or authored fallback | P2 close shot | Clip event or bounded timeout |
| `FaceOff` | Both turn to one another and enter neutral stance | Camera blends to gameplay framing | Both aligned to spawn and facing |
| `Ready` | Idle/guard stance with a short anticipation beat | Full HUD visible; show round number and READY | Timeline marker; both assets ready |
| `FightCall` | Transition to combat idle; no automatic attack | FIGHT cue and sound; timer still frozen until cue ends | Explicit fight frame marker |
| `Fight` | Movement, attacks, guard, crouch, hit, knockdown | Follow camera, timer, health, score | KO, double KO, or time expiry |
| `FinalBeat` | Freeze contact pose briefly; resolve winner | KO/TIME UP overlay; hit effect and sound | Fixed presentation duration |
| `WinLose` | Winner victory action; loser grounded/recovery/loss action | Winner shot, score, match outcome | Actions complete or safe timeout |
| `NextRound` | Reset positions and states, then Ready | Round number increments | Round remains in match |
| `MatchResult` | Winner idle victory loop; loser settled pose | Rematch/selection options | Player choice |

Proposed first pass timings: entrance clips at their natural length, FaceOff 30 frames, Ready 60 frames, FightCall 30 frames, FinalBeat 35 frames, and WinLose 120–180 frames. These are production targets for pacing review, not measured original Tekken 7 timings. Events embedded in authored clips should replace fixed durations once clips are chosen. Let players skip entrances and result camera sequences only after both assets have loaded; never skip the state reset or fight start marker.

## Step 1 — Capture the original reference precisely

Record a local Tekken 7 Versus match with Jin as P1 and Kazuya as P2, then reverse sides. Capture character selection through rematch, with the game version and settings noted. Make separate clips for normal KO, timeout, double KO, final round, and match victory. At 60 fps, log the first and last frame of stage entrance, each character intro, Ready, Fight, hit freeze, round finish, winner pose, and result menu. Record camera angle and transition, sound/voice cue, HUD state, whether the fighter can act, and whether the timer runs. Keep a reference table with observed facts and confidence. This is required before claiming exact visual or temporal fidelity.

The locally extracted `BP_BattleEvent` and `OnRoundSettled` references may help identify event names; they do not give a complete runnable timeline. Original native combat rules are not recovered. Use direct play capture as the timing authority, then document intentional differences.

## Step 2 — Audit the Jin and Kazuya action libraries

Make a per character action manifest from `Assets/Remaster/Museum/Jin.asset` and `Kazuya.asset`: asset index, move ID, original animation name, clip length, whether its `motion` exists, visual purpose, and rig quality. Review candidates in the Character Museum at normal speed. Assign only inspected clips to `entrance`, `faceOff`, `readyIdle`, `fightIdle`, `roundWin`, `matchWin`, `roundLose`, and `KO`. Mark a field Missing if no acceptable clip exists. The current common `co_lose_*` entries are candidates for review, not approved loss clips.

For missing actions, author short transitions using the existing imported rig and stance: entrance walk/turn, Ready weight shift, post KO settle, and a modest victory hold. Keep them labeled as replacement animation. Do not play an unrelated attack motion as a victory. Record any costume that breaks a clip or changes bone mapping.

Deliverable: two action manifests and a small in editor preview scene where a reviewer can press one key to play each phase action in order. Acceptance: no popping bind pose, displaced feet, reversed facing, or costume clipping in either side position.

## Step 3 — Separate match logic from cinematic direction

Keep `BattleModel` responsible for the deterministic 60 Hz Fight state: positions, attacks, contact, damage, timer, wins, and result. Add a `VersusPhaseDirector` responsible for the table above. It sends StartRound and BeginFight commands to `BattleModel`; it never invents hit outcomes. Add a `FighterPresentationController` per character that chooses clips based on phase and combat state. Add a `VersusCameraDirector` with authored shots and blends. Add an audio cue layer with named events rather than hard coded sounds in the phase logic.

The director should own a `PhaseContext` containing phase, round number, winner, result type, elapsed presentation frames, whether both characters are ready, and whether skip was requested. All phase changes pass through one method that clears stale input and emits enter/exit events. A watchdog timeout returns to a safe pose if a clip event fails. Keep phase durations in a data asset to allow reference based tuning.

## Step 4 — Stage and place the fighters

Create two spawn anchors and four camera anchors for each arena: P1 intro, P2 intro, gameplay, and result. Spawn both fighters at the selected anchors and set facing from the current opponent position. Verify their foot contact with the floor. On a second round, reset to the same gameplay anchors while preserving score and chosen costumes. During an entrance, hide the non featured fighter only if the reference capture does; otherwise keep them in the scene with a neutral pose.

For the current Neon Deck, retain the existing collision bounds and camera follow as the gameplay shot. Use separate scripted close shots only during entrances and results. Acceptance: neither close shot crops the face or feet at 1280 × 720; the gameplay camera frames both fighters at minimum and maximum legal separation.

## Step 5 — Implement Ready and Fight as a frame accurate handoff

At Ready, reset health, guard, stun, attack, buffer, position, facing, and the round timer. Hold simulation ticks while rendering stance animations and round HUD. On the FightCall marker, show FIGHT and play its cue; set Fight active on one exact 60 Hz tick. Start the timer on that tick. Buffering movement or attack before the marker is a design decision to match the reference; until verified, discard early input to prevent an invisible opening hit.

Record the start tick and first legal attack tick in a diagnostic overlay. Acceptance: both characters gain control on the same tick; a held attack through the introduction does not fire unexpectedly; round two cannot inherit round one stun or attack state.

## Step 6 — Clone character actions during Fight

Build an authored input to action map for each fighter. Map the four buttons to left punch, right punch, left kick, and right kick, then expand with directional inputs after the four core moves are visually validated. For each move store the source motion, frame duration, startup/active/recovery, hit height, range, damage, hit/block stun, and the animation frame corresponding to contact. The present catalog guesses values from animation names and array position; those guesses are unsuitable for faithful gameplay.

The presentation controller needs priority rules: KO > hit reaction > block reaction > active attack > crouch/guard > movement > idle. A new state starts at a defined frame and blends for a short authored duration. Preserve battle owned root position and facing; do not let a motion clip move the fighter outside the collision model. Add foot planting or controlled root offsets where a kick visibly slides. Review at least one hit, blocked hit, whiff, sidestep, side switch, and trade for each fighter.

For a closer Tekken 7 clone, later passes must implement throws, launches, airborne states, juggles, walls, Rage, and move specific cancel rules. Add them one system at a time after the base stance and hit synchronization are reliable; the existing four button prototype cannot reproduce those interactions by animation alone.

## Step 7 — Direct the final hit and results

On the tick that decides a round, snapshot both fighters' poses and combat states. Apply a brief hit freeze for KO, then play the correct knockout or timeout cue. Show final health and stop the timer. Enter a KO/loss action for the defeated fighter and a restrained winning reaction for the victor. For timeout, preserve both standing poses first, display Time Up, then transition to winner/loser actions. For a draw or double KO, do not arbitrarily choose a winner action; show both appropriate outcomes and replay the round without a point.

Distinguish round win from match win. A round winner gets a short reaction and both fighters reset for Ready. A match winner gets a full victory action and result camera, followed by selectable Rematch, Character Select, Stage/Rules, and Main Menu. Result input becomes active only after the minimum reveal time. Acceptance: exactly one round point is awarded for a normal winner, no point for a draw, and the action/camera pair always matches the resolved outcome.

## Step 8 — Test the complete sequence with both characters

Add a deterministic scripted scenario runner that can force: normal KO by Jin, normal KO by Kazuya, timeout, double KO, final round victory, and rematch. It should report ordered phase events with frame numbers, selected clips, chosen camera, timer start/stop, and score changes. Save screenshots at EntranceP1, EntranceP2, Ready, Fight, FinalBeat, and MatchResult with the HUD included. The existing `arena-preview.png` is camera only and is insufficient for this review.

Run a manual two player session after automated checks: both controllers, keyboard versus controller, side reversal, pause during Fight, skipped entrance, and ten rematches. Compare captures side by side with the reference table. Fix action timing, camera framing, and missing transitions before expanding the move roster.

## Implementation order and release gate

1. Capture the original and approve the Jin/Kazuya phase action manifests.
2. Build `VersusPhaseDirector`, clip bindings, phase event logging, and camera anchors.
3. Ship StageLoad → EntranceP1 → EntranceP2 → FaceOff → Ready → FightCall → Fight without visual gaps.
4. Add FinalBeat → WinLose → NextRound/MatchResult and all draw/timeout paths.
5. Replace guessed playable move values with reviewed per fighter data; synchronize contact and animation.
6. Add HUD included captures and manual two player verification; tune against the reference.

The clone's first milestone is a complete Jin versus Kazuya match where both characters perform believable and distinct entrance, ready, combat, loss, and victory actions, and every transition can be replayed and measured. Exact original fidelity is a later milestone dependent on direct reference capture and verified move data.
