---
name: tekken-arcade-machine
description: "Build, repair, and validate the Tekken Unity remaster's arcade cabinet layer: coin and credit handling, attract mode, start/continue flow, CPU ladder, challenger join-in, rankings, operator menu, cabinet input, and Windows arcade builds. Use for this project's arcade machine, cabinet controls, coin acceptor, free play, stuck arcade screens, or kiosk setup."
---

# Tekken arcade machine

This skill owns the cabinet input, credits, operator settings, and flow around a match in
`UnityRemaster/TekkenVersusRemaster`, under `D:/games/Tekken.7.Ultimate.Edition.v5.10`.
Confirm the paths and project version locally; the reviewed project uses Unity 2023.2.22f1.
The settings/ledger/input, match hooks, Boot scene, and keyboard-playable arcade loop with challenger and direct two-player matches
are implemented. A Unity Play check has exercised start, movement, continue and game-over return.
M5 adds an F2 operator menu with coin/game settings, input test/remapping, bookkeeping and diagnostics.
M6 adds timed initials entry, persistent CPU-ladder rankings, attract tables and operator table clearing.
Sound/display service pages, original title/challenger movie presentation, physical cabinet adapters
and deployment remain planned. See
[references/status.md](references/status.md) and recheck the source before relying on that snapshot.

Read status first. For implementation, read the relevant sections of
[references/arcade-design.md](references/arcade-design.md), which records proposed defaults,
transaction recovery, state transitions, and validation cases. Hardware and pricing questions
need not block pure logic work using explicitly labelled defaults. A skill review does not
authorize building the cabinet software or changing the host's Windows configuration.

## Integration boundaries

- Keep combat rules, move data, and the interpreter intact. Add small orchestration hooks to the
  existing match driver for per-side CPU control, injected settings, input, and match completion.
- Preserve local versus/editor behavior when arcade mode is inactive. Arcade mode owns the player
  screens and disables their pause, rematch, quit, and debug actions. Operating-system shortcut
  restrictions require separate cabinet configuration; fullscreen alone does not supply them.
- Battle, UI, sound, stage, and costume work may have sibling skills named `tekken-versus-battle`,
  `tekken-versus-ui`, `tekken-sound`, `tekken-stage-environments`, and `tekken-costume-wardrobe`.
  Locate and read one when the task enters its subsystem. These currently live under
  `C:/Users/Administrator/.claude/skills`; do not assume they are installed in every agent's catalog.
  If unavailable, inspect that subsystem's source and document the missing guidance.
- Inspect current files and any available session coordination before shared-file edits. Do not
  require a particular session-list tool that may not exist. Preserve unrelated changes; use
  backups when the project has no Git checkout. Commit only when requested.

## Cabinet invariants

1. Give every observed coin and paid action one identity and one ledger transaction. Persist
   credit balances, partial coin counts, and paid-session recovery together. Never claim an atomic
   rename guarantees capture of a physical pulse during a process crash or power loss. Hardware
   buffering, durable acknowledgement, and power-cut tests determine that guarantee.
2. Poll cabinet/system events independently of fight `FixedUpdate`, loading overlays, and
   `Time.timeScale`. Use unscaled time for attract, selection, continue, and transition deadlines.
   Gameplay button buffering can feed simulation ticks; coins, Start, Service, and Test cannot
   wait for a fight tick. Frame polling cannot capture pulses missed during a blocked main thread.
3. Player-facing waits have defined timeouts and recovery. Operator Test and operator fault states
   may wait for service. A failed load must not spend another credit or reveal a broken fight.
4. Centralize arcade bindings in an input adapter with a testable logical-event core. Read each
   edge once, debounce each chute separately, and prevent held buttons or stale scene events from
   spending credits. Preserve existing desktop controls through a separate binding profile.
5. Keep flow, ledger, ladder, and input normalization testable without Unity. Scene loading,
   rendering, audio, and device polling are adapters; they need not all fit in one MonoBehaviour.
6. Separate evidence: a source audit, passing harness, Unity player test, and cabinet hardware test
   establish different things. Report what was actually exercised and what remains unverified.

## Verified source map

Paths below are relative to the project; runtime files are under `Assets/_Project/Scripts/Runtime/`.
Search symbols rather than relying on line numbers. Snapshot: 2026-10-30.

| Piece | Current behavior and needed integration |
|---|---|
| `VersusGame.cs`, `VersusGameArcade.cs` | `PrepareArcadeMatch` queues a one-use scene-bound request before Awake; arcade setup overrides desktop picks, injects rules, routes each side independently, and emits `ArcadeMatchCompleted` once. `ArcadeResult` retains the result, `ArcadeVictoryComplete` reports presentation completion, and `SuspendArcadeMatch` suspends simulation. Arcade guards suppress desktop pause/debug/rematch/menu actions. Local mode retains its existing paths. See the match integration reference before use. |
| `CpuPlayer.cs` | Existing two-argument construction preserves legacy behavior. Optional third `difficulty` argument accepts 0 (legacy) or 1..10 (authored arcade guard reaction/chance, getup delay and attack pacing). It does not recover original arcade CPU difficulty. |
| `CharacterSelect.cs`, `ArcadeDirector.cs` | Desktop select still requires both humans. ArcadeDirector provides its own timed one/two-human selector using the existing character art; it passes picks through `PrepareArcadeMatch`, never through desktop static setters. |
| `VersusLoadingScreen.cs` | Public `Show`, `Holding`, and `MatchReady`; persistent overlay with a 20-second give-up path. Its visual timeout does not prove successful loading. The director must validate readiness and handle failure. |
| `BattleModel.cs`, `RemasterConfig` | Reuse `MatchPhase`, `MatchRules`, winner/results, and `Restart()`. The driver currently forces health to 170. Do not change combat implementation for cabinet flow. |
| `BattleSound`, `BattleMusic`, `BattleHud` | Reuse existing playback and 1280x720 letterboxed presentation. `Resources/BattleSound/sounds.json` includes countdown clips; asset presence alone does not establish original arcade trigger semantics. |
| `EnvironmentSelection`, `SoundSettings`, `FightWardrobe` | Preserve saved settings. ArcadeLadder picks usable saved stages (arena fallback, controlled repeats on small pools); per-match stage goes through `ArcadeMatchSetup.Stage` and does not rewrite EnvironmentSelection. |
| `Assets/_Project/Scripts/Editor/ProjectSetup.cs` | `BuildLocalVersus2` explicitly builds only `LocalVersus2.unity`, regardless of other enabled Build Settings scenes. An arcade build needs an explicit scene list and packaged resources. |
| `WardrobeBuildPackager.cs`, `AssetRefreshHook.cs` | Existing flags are `Temp/build-localversus2` and `Temp/refresh-assets`. `Temp/build-arcade` does not exist yet. |
| `ProjectSettings` | LocalVersus2 is the first enabled scene; sound_settings is also enabled. Player settings are windowed, legacy input, run-in-background enabled. An arcade build must scope and restore any temporary settings/define changes. |

The workspace's `decrypted/tkdata/list/cpu_chara_tbl.bin` and `cpu_stage_tbl.bin` are each 35 bytes.
They do not supply a recovered playable ladder in this review. Treat the proposed opponent/stage
order as authored; do not infer original behavior from filenames or file size alone.

## Planned layout and validation

`Assets/_Project/Scripts/Runtime/Arcade/` contains `ArcadeSettings`, `ArcadeStorage`, `ArcadeLedger`,
and `ArcadeInput`. Read [references/core-implementation.md](references/core-implementation.md)
before integrating or changing them; it explains event ordering, storage failure handling, and
the runnable `Tools/ArcadeHarness/Run.ps1` checks. M2 adds `ArcadeMatchController` and the runtime
partial `VersusGameArcade.cs`; read [references/match-integration.md](references/match-integration.md)
before wiring a scene director, and run `Tools/ArcadeHarness/RunMatches.ps1` for its checks.
M3 adds pure `ArcadeFlow`/`ArcadeLadder` and the runtime `ArcadeDirector.cs` (keyboard polling and
overlay), plus `Scenes/ArcadeBoot.unity` and editor `ArcadePlaytest.cs`.
Read [references/keyboard-playtest.md](references/keyboard-playtest.md) to launch/test the actual
Unity loop. Read [references/challenger-flow.md](references/challenger-flow.md) before changing M4
challenge eligibility, per-player entitlements, or winner/loser continuation. M5 adds the partial
`ArcadeOperatorMenu.cs`, pure `ArcadeBindings`, and operator harness/Play check. Read
[references/operator-menu.md](references/operator-menu.md) before changing pause, remapping,
operator settings or counter/credit resets. Read [references/rankings.md](references/rankings.md)
before changing score ownership, CPU timing, initials entry, ranking persistence or rule grouping.
Sound/display service pages, original arcade movie presentation, the arcade player build and
`--arcade-soak` remain planned.

For an implementation task, select the next dependency needed for the user's requested outcome;
do not stop at an arbitrary single milestone. Exercise the relevant design cases against the real
logic, including failures/restarts for paid transitions and baseline local-versus behavior.

An existing optional compile helper is
`C:/Users/Administrator/.claude/skills/tekken-versus-battle/harness/compile_check.sh`.
Inspect it and its Unity/Bee dependencies before use; it needs Git Bash/MSYS (`cygpath`, `find`, etc.),
not plain PowerShell. It compiles to a temporary directory and **does not refresh the open editor**.
Use the project's refresh hook or normal Unity refresh, check compiler errors and current assembly
timestamps, and then trigger a supported editor action. Do not open a second Unity editor on the
same project or treat a deleted flag as proof of success. Check the action's report and output.

For a skill-only revision, validate skill frontmatter and reference links; do not start
a game, build, or hardware test unnecessarily. Update status with the evidence, defaults still
unconfirmed, and next meaningful task. Give hardware test steps only for implemented behavior.
