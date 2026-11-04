# M4 challenger and direct-versus flow

This is an authored remaster policy, not a recovered original Tekken arcade specification.
The implementation extends `Runtime/Arcade/ArcadeFlow.cs`, `Runtime/ArcadeDirector.cs`, and
`Editor/ArcadePlaytest.cs`; `Editor/ArcadeChallengerCheck.cs` drives the new Play check.
Combat and desktop-versus code are unchanged.

## Eligibility and pricing

The director observes readiness/result and the last completed battle tick before draining Start
inputs. It calls `ObserveChallengeEligibility` with the current match ID, MatchPhase.Fight,
positive health/time, and an inactive VS overlay. Flow.Fight alone is insufficient because it
also encloses entrance/round transitions. KO/result transitions win a same-frame Start race.
A denied edge is consumed in memory and cannot later replay as a challenge. Input/ledger event
IDs remain ordered; never feed a saved Start as a fresh event. Physical pulse capture is unchanged.

- First Start in attract/demo pays creditsToStart. Another Start with the same sampled timestamp
  also pays creditsToStart; with one shared credit only the first side enters. Both same-sample
  Starts permit direct versus even when joinIn is off.
- Another side joining an already-entered solo Select pays creditsToChallenge when joinIn is on.
  Select's deadline must not have expired. Both players then get a fresh selection timer.
- Another side joining an eligible solo fight pays creditsToChallenge when joinIn is on. The
  director suspends the incumbent game before the next simulation tick, shows a two-second text
  challenger call, then selects both fighters. The original challenger movie/sting is not installed.
- No join is charged during loading, intro, round end, victory or an existing two-player fight.
- Selection has independent cursors and confirmation flags; confirmed picks cannot move. The
  timeout confirms both outstanding picks, including a player who never touched the controls.

## Entitlements and continuation

`SessionFor(side)` tracks each payer. `HumanSide` is the solo owner / most recent versus winner;
`ContinueSide` is the loser during a two-player Continue. Do not use HumanSide to route that
countdown's buttons or payment. `TwoPlayers` remains true throughout the loser's Continue screen.
Both sessions are marked ready when versus initializes. Loading/fault/restart recovery uses the
existing ledger policy and refunds all still-open entitlements once into their original pools.

At versus completion only the loser's entitlement closes. During the timed Continue offer the
winner's remains open and no CPU ladder runs. Only the loser can buy creditsToContinue; exact
boundary Start wins, a late Start does not debit. Accepted continue returns both players to select
(or directly reloads versus when continueReselect is false), retaining the winner's transaction.
A drawn versus match reloads without a new payment or closing either transaction.

On declined continue, the winner takes over the incumbent's unchanged ladder stage/opponent/arena.
The winner's selected fighter stays selected, even if that produces a mirror fight against the
retained CPU opponent. A direct-versus game had no suspended ladder, so its winner receives a new
seeded ladder starting at stage zero. Resumed CPU wins advance normally. A paid loser may win a
later rematch and become owner; never transfer the old loser's closed transaction to the winner.

## Verification

Run `Tools/ArcadeHarness/RunMatches.ps1 -FlowChecks`: 36 cases, including the previous 16 solo
cases, shared/separate credits, simultaneous and later starts, joinIn, independent picks, deadlines,
KO precedence, stale/rejected edges, either winner, rematches, draws, free play and restart recovery.
Run core/match regression: 31 + 22 cases, including the existing 96,000-tick desktop CPU baseline.
Both Unity assemblies must compile and the open editor must refresh before running new menus.

`Remaster > Arcade > Run challenger Play check` / `Temp/check-arcade-challenger` runs real Boot and
match scenes, logical coin/Start/movement, test-forced KOs, paid loser continue, challenger winner
ladder resumption and direct-versus winner's new ladder. Evidence is in
`Validation/Arcade/challenger-play-validation.json` and `arcade-{challenger,two-player-select,
two-player-fight,loser-continue}.png`. Verify the report's pass flag and individual observations;
physical keyboard/cabinet behavior and original presentation fidelity are separate checks.
