# M2: match setup and lifecycle

Project: `D:/games/Tekken.7.Ultimate.Edition.v5.10/UnityRemaster/TekkenVersusRemaster`.
The pure logic is `Assets/_Project/Scripts/Runtime/Arcade/ArcadeMatchController.cs`.
The Unity bridge is the partial `Assets/_Project/Scripts/Runtime/VersusGameArcade.cs`, with small
guards/calls in `VersusGame.cs`. M3's `ArcadeDirector` now loads scenes and polls its keyboard profile;
see [keyboard-playtest.md](keyboard-playtest.md).

## Director integration

Create a new immutable `ArcadeMatchSetup` per match, with a unique match ID, two fighter enum names
(for example `Jin`, `Kazuya`), per-side CPU flags and levels, seconds, rounds to win, and entrance choice.
Match IDs and ledger entitlement/session IDs are different: a paid ladder can contain several matches.
Keep that mapping in the director. The optional setup `stage` argument (M3) supplies a per-match
stage from the user's usable saved choices; it must not overwrite EnvironmentSelection.

Before loading the scene, queue:

```csharp
var setup = new ArcadeMatchSetup(matchId, "Jin", "Kazuya", false, true,
    levelOne: 4, levelTwo: 4, roundSeconds: 60, roundsToWin: 2, playEntrance: true);
var request = VersusGame.PrepareArcadeMatch(setup, persistentInput,
    "Assets/_Project/Scenes/LocalVersus2.unity");
// Now load that exact scene through the director's existing scene-loading mechanism.
```

The bridge consumes a request only in the target scene's Awake and only once. A second pending
request is rejected. A timeout or failed/cancelled load must call `CancelArcadeMatch(request)` while
the request is Pending; otherwise a later load could consume it. A different scene cannot claim it.
Do not queue before entering Play: subsystem initialization intentionally clears stale static requests.
Queue from the persistent director after Play starts, immediately before loading the match scene.

Observe `request.State`: Pending -> Claimed -> Ready, or Failed with a retained `Failure` string.
Only Ready acknowledges successful fighter/model/presentation setup. The loading overlay's timeout
is not a readiness signal. `VersusGame.ArcadeCurrent` points to the claimed driver; check that its
`ArcadeRequest` is the same request and its state is Ready before acting on it. Scene unload releases
the request; failed initialization preserves its failure. Cancellation after claim needs scene
teardown and the director's paid-session recovery; it is not a pending-request cancellation.

In arcade mode the driver bypasses desktop character select and ignores its stale static picks,
copies the scene config, injects seconds/rounds (health remains 170), and creates each CPU with its
assigned level. Default local-versus setup still takes the existing path when no request is claimed.
Smoke/preview command-line actions do not schedule capture/exit during a claimed arcade match.

## Input, completion and suspension

The persistent adapter samples `ArcadeInput` on unscaled time and processes Coin/Start/Service/Test
events outside the fight loop. The match controller consumes only gameplay input. Human sides use
their own stick/buttons; CPU sides replay original demo inputs. Input during intro is cleared before
Fight; held gameplay must be released at the transition. The current desktop idle-getup convenience
also applies to humans in arcade mode.

`ArcadeMatchCompleted` fires once, after the terminal tick and its presentation processing. Subscribe
after readiness, and also inspect `ArcadeResult` if attachment was delayed. Its immutable snapshot
has match ID, winner, each side's rounds won, round number, final-round perfect/time-up flags, total
simulated Fight ticks and final timer ticks. Fight ticks are simulation time, not wall time.

The controller keeps neutral model ticks after MatchOver so death/victory presentation continues;
it never auto-rematches. `ArcadeVictoryComplete` indicates that the winner's take finished. The
director must also enforce its unscaled victory timeout, including missing/stuck presentation.
Schedule scene transitions on the director's next update, not synchronously inside the callback.

`SuspendArcadeMatch(true)` stops battle ticks and match Update clocks without stopping cabinet
input processing. Resume clears gameplay/CPU inputs. It is an operator/director control, not a player
pause action. Do not close or refund the ledger entitlement merely because this API suspends a match.

Arcade mode guards keyboard debug/pause/rematch actions, entrance Enter-skip, effect demo buttons,
tuning/help panels and desktop result/menu choices. Original HUD, round calls and victory banner
remain. Victory selection reads held cabinet buttons without consuming queued gameplay taps.
Window/system shortcut lockdown and a hidden cursor belong to later cabinet deployment/Boot work.

## CPU difficulty and regression

`CpuPlayer(graph, seed)` and explicit level 0 retain the existing desktop CPU and random sequence.
Levels 1..10 are authored remaster settings: higher levels react to guard opportunities sooner,
choose guard more often, get up sooner on average, and pause less between original demo inputs.
This does not establish native Tekken arcade difficulty or guarantee relative strength for every
fighter. Autonomous Rage Art use and advanced punishment selection are not added.

Run from the project directory:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Tools/ArcadeHarness/RunMatches.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Tools/ArcadeHarness/Run.ps1
```

`match-validation.json` reports 22 M2 cases; `core-validation.json` reports the 31 M1 regressions.
`legacy-cpu-baseline.json` was generated from the backed-up pre-M2 CpuPlayer source using the real
unchanged battle/interpreter code: eight pairings, two seeds each, 6,000 ticks per trace. It checks
default CPU commands and battle states across 96,000 ticks. Do not regenerate it from the changed
implementation just to make a failure pass. The input/resource shim is local to the test tools.

These headless checks do not execute Unity's Awake/rendering/scene-load path. M3's separate Play
check now exercises readiness, timed select -> match load, P1 movement, paid continue and game-over
return. P2 device controls and cabinet acceptance still require playtests. Ordinary Play retains the
existing scene behavior; use the Arcade menu for the explicit Boot-scene launch.
