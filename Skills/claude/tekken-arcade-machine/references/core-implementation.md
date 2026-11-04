# M1 core: integration contract

Project: `D:/games/Tekken.7.Ultimate.Edition.v5.10/UnityRemaster/TekkenVersusRemaster`.
Files are under `Assets/_Project/Scripts/Runtime/Arcade/`, namespace `Versus.Remaster.Arcade`.
All four files are ordinary C# with no Unity startup hooks or MonoBehaviours. Adding them does
not activate arcade mode. Settings and ledger JSON are checksummed envelopes around serialized
payloads, using the same `DataContractJsonSerializer` code in Unity and the headless harness.

## Wiring sequence

1. Load `ArcadeSettingsFile.Load(path, out diagnostic)`. Missing/corrupt settings give provisional
   validated defaults and a diagnostic without overwriting the old file. Do not ignore that
   diagnostic when enabling paid play. Default filenames are a future director's responsibility:
   `arcade-settings.json` and `arcade-ledger.json` beneath its selected data directory.
2. Open one `ArcadeLedger` with the settings. It holds a separate file lock until disposed. New
   storage commits an empty snapshot before accepting money. Existing corrupt/missing-primary
   evidence faults; it does not silently restore an older backup. Preserve all files for operator
   reconciliation. Live settings/pool/pricing migration is not implemented. Reopening with different
   coin ratios or shared/separate pools is rejected without changing stored money.
3. At boot, call `RecoverInterruptedSessions()` before accepting new starts. The provisional policy
   refunds the recorded costs of every open entitlement once, including an interrupted ready match;
   free-play entitlements cost zero. Do not call recovery on each scene change. A real deployment
   must decide whether to adopt this policy or resume sessions instead.
4. Construct ONE `ArcadeInput(settings, ledger.Snapshot.lastEventId)` for the persistent director.
   Feed `Sample(logicalControls, monotonicUnscaledTime)` from an adapter. Use the same input instance
   across scene loads. It is a single-consumer core, not a thread-safe device driver. Background
   readers must marshal observations to it. No keyboard/USB/JVS mapping or remap UI exists yet.
5. `DrainEvents()` assigns increasing IDs after ordering events by time, coins/service before Start,
   then P1 before P2. Feed money events to the ledger in that order. Do not pass unrelated per-device
   sequence numbers or consume the batch out of order: the ledger uses a persisted high-water mark.
   Use a fresh sequencer after reopening/recovery; do not recycle stale batches across boots.
6. For an eligible Start only, call `Begin(id, side, action, out sessionId)`. Only **Accepted**
   authorizes one new session. Duplicate, insufficient funds, and already-active results must not
   launch a match. Denied requests persist their ID, preventing a replay from spending a later coin.
   Eligibility, challenger arbitration, and deadlines belong to M3's flow, not the ledger.
7. `MarkMatchReady(sessionId)` records readiness. `Complete(sessionId)` closes an entitlement only
   when its run ends normally; a ladder match or a challenger interruption alone must not close it.
   Session ownership transfer after challenger victory still needs a flow/API extension; do not
   fake it by spending again or editing a returned snapshot.
8. Route `ConsumeGameplay(side)` into the fight adapter. Stick numbers use neutral 5; buttons are
   LP/RP/LK/RK bits 1/2/4/8. Short taps are retained until consumed. `ResetGameplayForTransition()`
   clears gameplay taps and suppresses held gameplay until release; it preserves queued coin events.

## Storage and event failures

The ledger copies state, writes/flushes a same-volume temp, then replaces the primary with a backup
(or moves on first save). The primary is the commit boundary; stale `.tmp` files are ignored when
a valid primary exists. A missing primary with `.tmp`/`.bak` evidence faults. Checksums detect damage,
not malicious tampering. Snapshot getters return deep copies.

A thrown write latches the instance into a fault. The commit may already have happened; close and
reopen before deciding what persisted. Never retry the paid action on the faulted instance. The
adapter must retain the failed event and remaining batch until it knows the outcome; draining the
input queue alone is not durable acknowledgement. Inhibit coins where supported and expose an
operator fault. Physical capture before durable receipt remains hardware-dependent.

Only OS sharing/lock violations during the file commit are retried, up to 150 ms total delay; a
persistent lock faults. Storage is synchronous in M1. Measure main-thread stalls and use buffered
device capture before deploying it with real coins; frame polling cannot capture unobserved pulses.

Partial payments stay per chute even with shared credits. Accepted overflow stays in the total
balance above `maxCredits`; `VisibleCredits`, `OverflowCredits`, and `ShouldInhibitCoin` expose it
without destroying value. The input service event currently targets side 0; an operator can call
`Service(id, side)` for either side. Bonus pricing, bookkeeping time/pick statistics, operator
reconciliation, hardware inhibit/acknowledgement, and live pricing migration are not implemented.

## Run the checks

From the project directory, run this process-scoped command (it does not change Windows policy):

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Tools/ArcadeHarness/Run.ps1
```

The runner compiles the actual four runtime sources with Unity's bundled Roslyn and .NET 6 runtime.
It stops on compiler/test failure, writes `Validation/Arcade/core-validation.json`, and keeps isolated
test data in a new `Validation/Arcade/CoreChecksData/<id>` directory. It never reads live credits.
The 31 cases cover pricing, remainders, duplicate/denied starts, two-player contention, overflow,
free play, interrupted/completed sessions, checksum/ownership failures, commit/refund faults,
sharing locks, debounce/jam behavior, input ordering, buffering and transition handling.

For Unity integration compilation, the sibling battle `compile_check.sh` needs Git Bash. Set
`TMPDIR` to a writable validation directory first; its default short Windows user path may be
inaccessible in the sandbox. Check both assembly exit codes. This compiles temporary assemblies,
not the open editor's loaded code, and establishes no player/hardware verification.

## M5 operator extension

`ArcadeLedgerState.operatorSettings` is optional on legacy schema-1 records. After the operator
explicitly applies settings it becomes authoritative, committed in the same generation as credit
pool/coin-ratio migration. Constructor defaults still apply to older records without that field.
Read [operator-menu.md](operator-menu.md) before changing this precedence or migrating pricing.
`ClearBookkeeping` preserves credits, partial payments, active sessions and event identity;
`ClearCredits` is a separate confirmed operator action, rejects active sessions, and preserves counters.
`ArcadeInput.OperatorPaused` freezes existing AND newly created match controllers, while polling
still records coins. Test sorts before same-sample money/Start so Test+Start cannot begin a game.

Compatibility fix: `[Serializable]` fields are required by DataContractJsonSerializer unless marked
`[OptionalField]`. The three M5 ledger additions carry that attribute; schema-1 files made before M5
must load without rewriting or resetting credits. Keep original money fields required. The saved
pre-M5 fixture is covered by OperatorChecks and the actual Unity saved-ledger Play check.
