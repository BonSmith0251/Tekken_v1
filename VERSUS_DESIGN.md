# Local Versus game design

## Goal

Build a two player, offline 3D fighting game around the readable, repeatable loop that makes Tekken 7 Versus Battle compelling: choose fighters, choose a stage, fight timed rounds, see a clear winner, and immediately play again. The first playable slice uses the Jin and Kazuya assets already imported into this Unity project. It should feel responsive and legible before adding a larger roster or elaborate presentation.

## Review of the original

Tekken 7's Versus Battle supports local competition with character and costume selection. The official version 5.10 notes describe favorite character and costume shortcuts, remembering the last costume in a session, and tournament settings that disable those conveniences in Versus Battle. Bandai Namco's Tekken 7 tournament rules document 60 second rounds and a first to three rounds format as the default competitive setup. These sources establish the selection and match framework; they do not provide a full specification for combat timing or every Versus screen.

The local extracted bundle includes cooked battle events such as `BP_BattleEvent` and `OnRoundSettled`, character blueprints, and game configuration. It is a behavioral reference, not executable Unity source. Native Tekken combat logic and complete original screen behavior are not recovered here. Avoid treating the prototype's move damage, startup, hit windows, or arena dimensions as original Tekken 7 values.

Sources: [Bandai Namco v5.10 notes](https://www.bandainamcoent.com/news/tekken-7-patch-v5.1), [Bandai Namco Tekken 7 tournament rules](https://www.bandainamcoent.com/news/tekken-tour-na), `decrypted/versus_scripts/UNITY_PORTING_NOTES.txt`, `decrypted/versus_scripts/function-index.csv`.

## Player flow

1. **Title / mode selection:** Start Local Versus. Show both controller assignments and a quick controls view.
2. **Character selection:** P1 and P2 independently select Jin or Kazuya, including mirror matches. Show a large model preview, name, costume, and clear ready state. Let either player back out before both confirm.
3. **Stage and rules:** Select a stage or Random. Set rounds to win and time limit. Default to first to three rounds, 60 seconds. Keep a first to two option for shorter sessions.
4. **Versus introduction:** Show both chosen characters and stage, then transition into the arena with a brief round announcement.
5. **Battle:** Both players fight at a fixed 60 Hz simulation rate. HUD shows health, timer, round wins, fighter names, and a readable pause state. The camera keeps both fighters in frame while preserving the side view and depth movement.
6. **Round result:** Stop the clock on KO or time expiry, show the result, award a round, reset positions and health, then start the next round. Define draws explicitly: replay the round with no point awarded.
7. **Match result:** Show the winner and offer Rematch, Character Select, Stage/Rules, and Main Menu. Rematch keeps the choices and swaps no controllers.

## Combat design for the first playable slice

- Four face buttons correspond to left punch, right punch, left kick, and right kick. Directions combine with attacks to choose moves from a fighter specific catalog. Show move names in an optional training overlay.
- Movement supports forward, back, crouch, and sidestep. Back guards standing attacks; down back guards lows. High attacks miss crouching opponents. Mids threaten crouch guard. Input is interpreted relative to the opponent's current side.
- Each move has startup, active, and recovery frames, reach, height, damage, hit stun, block stun, and animation binding. Contact is resolved once per move. Same frame hits can trade.
- Make hit sparks, sounds, impact pause, and camera motion communicate contact without hiding spacing. Use imported character motion where it is verified; mark missing states for animation work.
- Later fidelity work can add throws, launchers, aerial juggles, wall interactions, Rage, and stage breaks. Each requires reference and dedicated implementation, not a guessed toggle on the present four move model.

## Current project assessment

| Area | Present in Unity project | Design gap |
| --- | --- | --- |
| Fighters | Imported Jin and Kazuya models, costumes, motion catalogs | Both are hard wired to player slots; no selection or mirror match |
| Match | `BattleModel` has health, timer, rounds, KO, timeout, draw replay, and 60 Hz ticks | Default is first to two; no rules screen or persistent match choices |
| Controls | Two keyboards/gamepads, four attack inputs, guard, crouch, sidestep | No controller assignment, remapping, directional move selection, or input display |
| Presentation | Arena, camera, HUD, hit feedback, pause, rematch | One generated stage; no character select, stage select, or full result choices |
| Automation | Automatic fight and smoke capture support | Automatic fight is a development demonstration, not the default Versus entry flow |

The current `VersusGame` starts with fixed Jin versus Kazuya and a single Neon Deck arena. Its `BattleModel` is a useful deterministic foundation. `MatchRules` explicitly calls its balance prototype data, so it must stay labeled as original design until move data is independently verified.

## Step by step implementation design

### Step 1 — Define the playable product and data

Ship a Windows local two player game at 1280 × 720 as the first target. Support two gamepads, shared keyboard, and mixed gamepad/keyboard play. Keep the imported Jin and Kazuya content as the initial roster. Provide Neon Deck as the first stage and a second, visually distinct stage before calling the mode complete. Online play, AI opponents, story, progression, and character customization editing are outside this first release. Automatic fight remains a development option.

Create immutable definitions separate from a match in progress:

| Definition | Required fields | Used by |
| --- | --- | --- |
| `CharacterDefinition` | ID, display name, prefab, available costume prefabs, portrait, action catalog, default stance | Character select and fighter spawning |
| `MoveDefinition` | Input command, motion reference, startup/active/recovery frames, hit height, damage, reach, hit/block stun, cancel rules | Input parser and 60 Hz simulation |
| `StageDefinition` | ID, display name, preview, arena prefab, bounds, spawn points, camera limits, music | Stage select and arena loading |
| `MatchSettings` | Rounds to win, seconds per round, stage ID or Random, controller assignments | Session and battle creation |
| `MatchSession` | Selected characters/costumes, settings, round score, current phase | Screen transitions and rematch |

Use ScriptableObjects for authored definitions and plain runtime objects for session state. Validate missing references when entering Play, with an error naming the character or stage. The first test is to load both definitions, instantiate either character in either slot, and instantiate each stage without a scene edit.

### Step 2 — Create a single screen and session controller

Add an explicit state machine: `Title → ControllerJoin → CharacterSelect → StageSelect → VersusIntro → RoundIntro → Fighting → RoundResult → MatchResult`. `Pause` overlays Fighting and returns to the exact prior state. A back action returns to the previous selection screen until the versus introduction has started. Centralize transitions in one controller; screens request actions and do not directly spawn fighters or edit scores.

Keep `LocalVersus.unity` as the composition scene. Split the current `VersusGame` responsibilities into screen flow, input ownership, battle simulation, arena presentation, and HUD. Preserve `BattleModel` as the deterministic core. Configure the initial scene to open on Title with `automaticFight = false`; keep a command line or editor option for automated demonstration.

Acceptance: launch the Windows build, navigate Title to MatchResult and back to Title, and repeat without reloading the application. No stale input or doubled fighter instances may survive a transition.

### Step 3 — Assign input devices and define commands

On ControllerJoin, each player presses Confirm on a separate device. A keyboard may be assigned to both players using the existing distinct key clusters. Record the device owner of each player; reject one gamepad controlling both slots. Show the assigned devices throughout selection and pause.

Menu commands are Navigate, Confirm, Back, and Start/Pause. In combat, sample movement and attack buttons each render frame, timestamp edges, then consume them on the next 60 Hz tick. Clear pending commands when entering pause, changing ownership, or starting a round. Keep the existing eight tick move buffer as a tunable prototype value, not a Tekken 7 claim.

Map attack buttons consistently as 1 = left punch, 2 = right punch, 3 = left kick, 4 = right kick. For keyboard, preserve P1 F/G/H/J and P2 numpad 1/2/4/5 plus current alternatives. Treat forward and back relative to the opponent; reverse them cleanly when sides switch. Include stick dead zones and prevent held Confirm from also triggering an attack on the next screen.

Acceptance: P1 and P2 navigate independently; simultaneous button presses register; side swaps reverse forward/back; pausing and resuming never causes a stored attack.

### Step 4 — Build character and costume selection

Give each player a focus cursor, a selected character, a costume choice, and a Ready flag. Either player may choose Jin or Kazuya, including Jin versus Jin or Kazuya versus Kazuya. Confirm first locks the character and moves focus to costume; Confirm again sets Ready. Back removes Ready, then returns to character choice. When both are Ready, proceed after a short visible countdown; any Back cancels it.

The layout needs two equally sized player panels, readable fighter names, a large model or portrait preview, the currently selected costume, and a distinct Ready indicator. Show unsupported costumes as unavailable instead of silently swapping assets. For mirror matches, distinguish players through HUD color and optional costume choice; never change move data merely for color.

Acceptance: both players can select the same fighter with separate prefab instances and action states, change or cancel choices, and reach stage selection without a missing model or incorrect name.

### Step 5 — Select stage and rules

Offer Neon Deck, the second authored stage, and Random. Let either player navigate the stage list; Confirm locks the current stage and opens a concise rules panel. Rules are first to two or first to three rounds, and 60 or 99 seconds; default to first to three and 60 seconds. Random resolves once per match and remains fixed through all rounds and rematches unless the players return to stage selection.

The second stage must change more than color: silhouette, floor pattern, lighting, background, and music should distinguish it. Keep equivalent collision bounds and spawn spacing in the first release so stage choice is cosmetic for competitive balance. Display a preview and stage name before loading the arena.

Acceptance: each choice loads the correct arena, settings persist across all rounds and Rematch, and returning to StageSelect can alter them.

### Step 6 — Present the matchup and start a round

On entering VersusIntro, preload both fighters and the selected arena. Show names, costume previews, stage name, and player labels for roughly two seconds; allow Confirm to skip after assets are ready. Place fighters at authored spawn points and face each other. Start each round with full health, zero stun, no active attack, cleared input buffers, and an unchanged score.

RoundIntro announces the round number and then Fight. The timer starts only when the Fighting state begins. Inputs may be sampled during introduction but must not execute until the first fight tick. Camera framing should include both fighters, health bars, and the usable arena without abrupt cuts.

Acceptance: round one and subsequent rounds begin from identical gameplay state apart from score; early input cannot land a hit during the announcement.

### Step 7 — Implement the 60 Hz fight simulation

Advance movement, attack state, collision, hit detection, damage, and the round timer in a fixed tick. Presentation interpolates or samples the simulation but never decides whether a hit happened. Maintain per fighter state for position, facing, health, guard level, crouch, active move, move frame, stun, input buffer, and round wins.

For the first slice, author at least four verified playable moves per fighter: one fast high, one mid, one low, and one longer reach move. The current `FighterActionCatalog` derives hit height and frame values from names and array position; replace that inference for the playable set with explicit `MoveDefinition` entries. Extra imported animations may remain browseable but should not enter the selectable move set until their data and alignment are reviewed.

Evaluate hits from the attacker and defender states at the same tick before applying results, preserving trades. Guard outcome follows attack height: standing guard blocks high/mid, crouch guard blocks low, high misses a crouching defender, and mid hits crouch guard. Resolve body overlap and arena bounds consistently. Add tests for these cases, time expiry, simultaneous KO, and match win threshold. Run the same input sequence twice and compare resulting state to detect nondeterminism.

Acceptance: visual impact coincides with the authored active frames; no move hits twice from one activation; the same tick inputs produce the same result regardless of rendering frame rate.

### Step 8 — Align animation and feedback

Bind idle, movement, crouch, attack, block, hit, KO, and victory states per character. Imported motion is used where a compatible clip exists. Mark placeholders in the asset definition and visual review list. Keep root movement controlled by battle state until root motion can be reconciled with collision.

Give attacks a clear startup silhouette, active contact moment, and recovery. Use distinct block and hit sounds, short sparks, damage reaction, and restrained camera shake. Add optional input and move name overlays for review builds. Sound and camera effects should not change simulation time or obscure the fighters.

Acceptance: record both fighters performing every playable move from each side; reviewers can identify hit, block, whiff, and recovery without reading debug text.

### Step 9 — Resolve rounds and matches

At zero health or zero time, stop the timer and accept no new combat input. If both health values are equal at time expiry, or both reach zero on the same tick, call a draw and replay the round without awarding a point. Otherwise award exactly one round to the fighter with health remaining or greater health at timeout. Show KO, Time Up, or Double KO text as appropriate, then show the round winner and score.

At the configured win threshold, enter MatchResult. Provide Rematch, Character Select, Stage/Rules, and Main Menu. Rematch resets wins and stage state, keeps selections and device ownership, and has an explicit Confirm so accidental held input cannot restart. Pause offers Resume, Restart Match, and Main Menu, with a confirmation on restart because it discards the current score.

Acceptance: all exits work from keyboard and gamepad; a draw never awards a round; rematch starts at round one with zero wins; changing character returns through stage selection.

### Step 10 — Package and verify the release

Build a standalone Windows player with only required scenes and assets. Verify from a clean launch without Unity open. Test two gamepads, two keyboard players, and mixed devices. Check 16:9 at 1280 × 720 and one higher resolution, both mirror matches, both stages, first to two and first to three, pause during each phase, a timeout, a KO, a draw, and ten consecutive rematches.

Maintain a short reference log for any claim of original game fidelity: source, observation, implementation decision, and remaining difference. The local cooked package metadata can identify components and events, but native combat behavior must be observed separately. Update this design when decisions change, and keep the playable build's version and control sheet beside it.

**First release definition of done:** two people can launch the build, independently select characters and costumes, choose a stage and rules, complete a full match, and rematch or change choices without using the editor or command line.
