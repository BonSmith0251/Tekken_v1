# M6 rankings and initials

This is an authored remaster policy, not recovered original arcade scoring or presentation.
Runtime: `Arcade/ArcadeRanking.cs`, `ArcadeFlow.cs`, `ArcadeRankingOverlay.cs`, and the director/operator
adapters. `ArcadeMatchController.FightTicks` and `VersusGame.ArcadeFightTicks` expose the existing
60 Hz fight counter. Combat rules, moves and desktop controls are unchanged.

## Scores and ownership

Each human Start or Challenge creates a run ID. Continue retains that ID and accumulated CPU stats.
Qualify with at least one personally cleared CPU stage and positive fight ticks. Sort stages
descending, ticks ascending, clean wins descending; keep ten. A clean win means winning a CPU match
without losing a round, not necessarily full health. Exact ties retain earlier submissions.

Count all CPU fight ticks, including failed attempts and unfinished play before a challenger joins.
Exclude versus ticks, loading, menus, selection, presentation and operator pauses. The director
observes the completed tick before processing Start. Match IDs/state guards prevent duplicate scores.
The record displays the final selected fighter; changing fighters on continue retains the run.

A challenger inherits ladder position but never another player's cleared-stage count. When a versus
loser declines Continue, finalize that loser's run. If qualifying, their initials and eight-second
table precede the winner's ladder resumption; the winner keeps their open paid entitlement. Otherwise
resume immediately. There is one screen/match owner. Start cannot take over that waiting winner's run.

Tables separate difficulty, ladder length, round time, normal/boss rounds, boss identifier and
continue-reselect policy. Prices/free play do not split tables. Stage selection/random seeds are
normal arcade variation. Keep at most 128 rule profiles, ten rows each; new profiles cannot qualify
at that limit. Changing settings selects another table without erasing old ones.

## Screens and persistence

Ending/GameOver delay -> qualifying NameEntry -> Ranking -> title. Only the scoring side can enter
A-Z initials. Up/Down changes a letter; Left/Right moves position; B1 confirms/advances, B2 backs up.
Confirming the third position submits. The default 20-second timeout submits the current letters,
initially AAA. Game settings can change the timer. Prompts reflect bindings. Fresh edges prevent
held fight/menu buttons from entering letters. F2 pauses deadlines; coins/service still register.
Start during initials is discarded without charging. Fresh Start may interrupt idle/post-run tables.

Demo completion/timeout shows a nonempty current table before returning to title. Operator > Rankings
shows the rules and scores; confirmed Clear removes only that table and never credits. Clearing is
blocked during pending initials. The new score is highlighted on the public table.

`arcade-ranking.json` sits beside the ledger and uses the existing checksummed atomic store. Missing
creates empty data. Corrupt/unwritable scores disable ranking and preserve evidence; they must not
fault, refund or block paid gameplay. Operator diagnostics shows the error. No automatic backup
promotion or corrupt-file reset is implemented. A failed store cannot clear evidence through the UI.

Validate score IDs, initials, bounds, unique runs and table caps; return copies. Re-submitting a
recorded run ID cannot rename or duplicate it. After uncertain commit, reopen the primary; it may
contain the submitted score once. Run progress/pending initials are in memory, so interruption before
submission can lose an unfinished score. Existing paid-session recovery is unchanged. M6 adds no
money/settings serialization fields; keep the pre-M5 fixture and OptionalField regression tests.

## Validation

`Tools/ArcadeHarness/RunMatches.ps1 -RankingChecks`: 21 cases cover order/cap/ties, rules, persistence,
duplicates, invalid data, clearing, corrupt/nonfatal storage, four write-boundary failures, endings,
initials/timeouts/ownership, coins/Start, F2 deadlines, continues, duplicate results, challenger CPU
stats and attract rotation. Regressions: 31 core + 22 match (96,000 desktop CPU ticks) + 36 flow +
22 operator, including old-ledger compatibility.

`Remaster > Arcade > Run ranking Play check` / `Temp/check-arcade-ranking` uses actual scenes with
isolated data, logical controls and forced test KOs. It wins a two-stage run and enters BAA, wins one
stage then loses another run and times out to AAA, checks both disk records, and confirms a table
clear while preserving credit. It also checks F2 deadlines and wrong-side/Start isolation.
Report: `Validation/Arcade/ranking-play-validation.json`. Screenshots: `arcade-name-entry.png`,
`arcade-rankings.png`, `arcade-operator-rankings.png`. Operator/saved-ledger Play checks remain useful
regressions. Physical controls, original arcade movies, sound/display service pages and deployment
remain separate work.
