# M5 operator controls

## User workflow

Launch **Remaster > Arcade > Play keyboard test**, focus Game view, then press **F2**. The same
Test mapping opens the menu during title, select, loading, battle, challenger call and Continue.
Navigation uses either player's Up/Down, Left/Right, B1 (select) and B2 (back). Default P1 keys:
W/S, A/D, U, I; P2 arrows, Numpad 4/O and Numpad 5/P. Enter/Escape also select/back.
F2 always resumes; **Resume game** and F2 discard unapplied coin/game drafts.

Pages: Coin settings, Game settings, Input test/remap, Bookkeeping, System diagnostics, Rankings.
Use Up/Down to scroll beyond the first ten rows. The menu shows pagination.
Coin/game edits stay in a draft. Back to Home, choose **Apply settings and return**, then Confirm.
Apply is allowed only while idle (title/demo), never during a player's ladder/Continue/run.
To change rules mid-run, resume and finish the game, then return to the operator menu at the title.
The operator controls do not abandon/refund a game simply to make Apply available.

Input page: choose a control with B1, release all keys, then press the replacement. Conflicting
bindings are rejected; Escape cancels. Capturing a replacement removes that control's old alternate.
The new binding saves immediately. The menu requires release after saving so a held new confirm key
cannot activate another row or reopen capture. Coin/Start held at remap time are rebased, not counted
as new pulses. F2 remains an immutable recovery key even if Test is also mapped elsewhere.
Keyboard and Unity joystick-button KeyCodes are supported; joystick axes/JVS buffering are not.

Bookkeeping displays the implemented counters: coins per chute, service credits, starts, continues,
challenges, refunded credits, total credit balances, chute fractions, active sessions and clear counts.
**Clear bookkeeping** and **Clear credits and partial coins** are distinct confirmed actions, with
Cancel selected initially. The former preserves every balance, fraction, paid session and event ID.
The latter removes balances/fractions, preserves counters, and is refused during an active session.
These controls are available inside operator Test; the keyboard test has no operator PIN/lock.
Elapsed-time/per-fighter statistics, sound calibration, monitor/display service, factory
reset and platform exit/lockdown are not implemented here. Preserve existing sound/display settings.

## State and storage contracts

Runtime additions: `ArcadeOperatorMenu.cs` is a partial ArcadeDirector, not a separate scene owner;
pure `Arcade/ArcadeBindings.cs` maps key names to logical controls. Changes to ArcadeFlow, ArcadeInput,
ArcadeMatchController and ArcadeLedger provide pause and persistence without changing combat rules.

- The operator menu preserves Flow.State and the per-player entitlements. Flow pauses countdowns;
  resume shifts deadlines by the elapsed unscaled operator time. Match readiness/results wait until
  resume. ArcadeInput.OperatorPaused makes even a newly loaded match controller start suspended.
- Coins/service still go through the one input queue and ledger while Test is open. Starts during
  Test and on the closing frame are discarded. Test sorts before same-sample coin/Start. Resume
  clears gameplay taps and requires held gameplay release; menu presses must not become attacks.
- `arcade-input.json` is a checksummed atomic binding file beside the ledger. Missing bindings create
  defaults; corrupt/unknown/duplicate mappings fault rather than silently rewriting the evidence.
- Fields added to the Serializable ledger must use `[OptionalField]` to remain readable from older
  snapshots under DataContractJsonSerializer. The M5 additions operatorSettings, bookkeepingClears
  and creditClears are optional; original monetary fields remain required. Test an actual older file,
  not only freshly created ledgers.
- Legacy `arcade-settings.json` supplies initial defaults. After an explicit operator Apply,
  `ArcadeLedgerState.operatorSettings` is authoritative. Settings AND migrated balances are one
  checksummed ledger generation, avoiding a split settings/ledger commit. Do not edit the ledger
  manually or expect changing the legacy settings file to override an applied operator configuration.
- Settings Apply rejects open paid sessions. A chute's ratio cannot change with a partial payment.
  Separate -> shared adds both whole balances; shared -> separate assigns the old whole balance to
  P1. The confirmation states this policy; chute fractions retain their chute identity. Lowering the
  visible credit cap retains overflow. Free play retains stored balances.
- Settings snapshots are copied. After successful Apply the director rebuilds the idle flow with
  the new configuration and updates debounce timing on the existing input adapter; event sequence
  and held coin state are preserved. All flow operations in that update use the same clock value.
- Clear bookkeeping and clear credits each commit atomically; their own clear counters are retained.
  Storage failures fault the ledger and follow the existing recovery policy, never retry a mutation
  as if it were a new payment. A bindings-save failure also stops the cabinet with diagnostic evidence.

## Verification

`Tools/ArcadeHarness/RunMatches.ps1 -OperatorChecks`: 22 cases cover paused deadlines, live coin/
ignored Start, exact Continue boundary after pause, readiness while paused, controller construction
under Test, Test priority, held-key rebasing, settings persistence, both pool migration directions,
partial-payment/active-session rejection, separate clears, four injected configuration commit faults,
key conflicts/recovery and saved bindings. The two upgrade cases load the pre-M5 fixture, preserve
its values through Apply/reopen, and reject a missing original required field. Existing 31 core + 22 match + 36 flow cases also pass.

Unity menu **Remaster > Arcade > Run operator Play check**, flag `Temp/check-arcade-operator`, uses
isolated data and the actual Boot/menu/match scenes. It drives navigation, captures Q as P1 B1,
reads the saved mapping, confirms select through the key-name adapter, pauses/resumes a paid fight,
applies free play from idle, and confirms a bookkeeping clear while checking stored credits survive.
Read `Validation/Arcade/operator-play-validation.json`; boolean observations distinguish partial
failures from a complete pass. Screenshots: `arcade-operator-{home,input,bookkeeping,confirm}.png`.
The first run exposed held-new-confirm reopening capture; the release gate fixes that path.
Injected key names do not validate physical keyboard rollover or cabinet pulses.

`Remaster > Arcade > Run saved ledger Play check` / `Temp/check-arcade-legacy` starts actual Boot
with `Tools/ArcadeHarness/Fixtures/ledger-before-operator.json` in an isolated directory. It verifies
13 credits and the old counters/event/generation survive without rewriting the file, opens F2,
and exits Play. Report: `Validation/Arcade/legacy-ledger-play-validation.json`.

M6 adds the name-entry timer to Game settings. Rankings views/clears the current rules table with
confirmation; pending name entry blocks clearing. Score-file faults disable scoring independently
of money. See [rankings.md](rankings.md).
