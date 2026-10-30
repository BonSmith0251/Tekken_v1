using System;
using System.Collections.Generic;
using System.Linq;

namespace Versus.Remaster.Arcade
{
    public enum ArcadeFlowState { Attract, LoadingDemo, Demo, Challenger, Select, Loading, Fight, Victory, Continue, GameOver, Ending, NameEntry, Ranking, Fault }

    public sealed class ArcadeLadder
    {
        readonly string[] opponents, stages;
        public int Count => opponents.Length;
        public string Opponent(int index) => opponents[index];
        public string Stage(int index) => stages[index];
        public ArcadeLadder(string[] roster, string player, string boss, int count, string[] stagePool, int seed)
        {
            if (count < 1 || count > 100 || roster == null || !roster.Contains(boss)) throw new ArgumentException("Invalid ladder roster/boss/length.");
            var available = roster.Where(n => n != player && n != boss).Distinct().ToArray();
            if (available.Length == 0) available = roster.Where(n => n != player).Distinct().ToArray();
            if (available.Length == 0) available = new[] { boss };
            var pool = (stagePool ?? Array.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToArray();
            if (pool.Length == 0) pool = new[] { "arena" };
            var random = new Random(seed); var fighters = new List<string>(); var arenas = new List<string>();
            opponents = new string[count]; stages = new string[count];
            for (int i = 0; i < count; i++)
            {
                if (fighters.Count == 0) fighters.AddRange(available);
                int pick = random.Next(fighters.Count); opponents[i] = i == count - 1 ? boss : fighters[pick]; fighters.RemoveAt(pick);
                if (arenas.Count == 0) arenas.AddRange(pool);
                pick = random.Next(arenas.Count); stages[i] = arenas[pick]; arenas.RemoveAt(pick);
            }
        }
    }

    // Scene-independent cabinet flow. Each human keeps a separate ledger entitlement.
    public sealed class ArcadeFlow
    {
        readonly ArcadeSettings settings;
        readonly ArcadeLedger ledger;
        readonly ArcadeRanking ranking;
        readonly ArcadeScore[] runs = new ArcadeScore[2];
        long observedFightTicks;
        bool resumeWinnerAfterRanking;
        ArcadeScore pendingScore;
        public bool RankingResumesWinner => resumeWinnerAfterRanking;
        public int NameSide { get; private set; } = -1;
        public ArcadeNameEntry NameEntry { get; private set; }
        public string HighlightRunId { get; private set; }
        public string RankingRules => ArcadeRanking.Rules(settings);
        public void ObserveFightTicks(string id, long ticks)
        { if (!OperatorPaused && State == ArcadeFlowState.Fight && Match?.MatchId == id && ticks >= 0) observedFightTicks = Math.Max(observedFightTicks, ticks); }

        readonly string[] roster, stages;
        readonly Random random;
        readonly long[] sessions = new long[2];
        readonly int[] cursors = new int[2];
        readonly bool[] confirmed = new bool[2];
        ArcadeLadder ladder;
        double lastTime, enteredAt, firstStartTime;
        long lastEvent;
        bool won, challengeEligible;
        double pausedAt;
        public bool OperatorPaused { get; private set; }
        public void SetOperatorPaused(bool paused, double now)
        {
            Clock(now); if (OperatorPaused == paused) return;
            if (paused) pausedAt = now;
            else { double duration = now - pausedAt; Deadline += duration; enteredAt += duration; }
            OperatorPaused = paused; challengeEligible = false;
        }
        int versusWinner = -1;
        public ArcadeFlowState State { get; private set; }
        public ArcadeMatchSetup Match { get; private set; }
        public int HumanSide { get; private set; } = -1;
        public bool TwoPlayers { get; private set; }
        public int ContinueSide => TwoPlayers ? 1 - HumanSide : HumanSide;
        public int Cursor => HumanSide < 0 ? 0 : cursors[HumanSide];
        public int SelectionCursor(int side) => cursors[side];
        public string Fighter(int side) => roster[cursors[side]];
        public bool SelectionConfirmed(int side) => confirmed[side];
        public bool CanSelect(int side) => !OperatorPaused && State == ArcadeFlowState.Select && sessions[side] != 0 && !confirmed[side];
        public int StageIndex { get; private set; }
        public int StageCount => ladder?.Count ?? settings.ladderStages;
        public long SessionId => HumanSide < 0 ? 0 : sessions[HumanSide];
        public long SessionFor(int side) => sessions[side];
        public string SelectedFighter => roster[Cursor];
        public string[] Roster => (string[])roster.Clone();
        public double Deadline { get; private set; }
        public string Error { get; private set; }

        public ArcadeFlow(ArcadeSettings settings, ArcadeLedger ledger, string[] roster, string[] stages, int seed, double now, ArcadeRanking ranking = null)
        {
            settings.Validate(); this.ranking = ranking; this.settings = settings.Copy(); this.ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            this.roster = roster?.Distinct().ToArray() ?? throw new ArgumentNullException(nameof(roster));
            if (this.roster.Length == 0 || !this.roster.Contains(settings.boss)) throw new ArgumentException("The configured boss must be an available fighter.");
            this.stages = stages == null ? new[] { "arena" } : (string[])stages.Clone(); random = new Random(seed);
            lastEvent = ledger.Snapshot.lastEventId;
            Clock(now); Enter(ArcadeFlowState.Attract, now, 6);
        }

        // The adapter observes the completed simulation tick before processing Start. Never infer
        // eligibility merely from Flow.Fight: entrances, KO and between-round screens live there too.
        public void ObserveChallengeEligibility(string matchId, bool fighting)
        { challengeEligible = !OperatorPaused && State == ArcadeFlowState.Fight && Match?.MatchId == matchId && fighting; }

        public void Process(ArcadeInputEvent input, double now)
        {
            Clock(now);
            if (State == ArcadeFlowState.Fault || input.id <= lastEvent) return;
            lastEvent = input.id; // Also consume ineligible edges: they cannot become a later challenge.
            if (input.kind == ArcadeEventKind.Coin) { ledger.Coin(input.id, input.side); return; }
            if (input.kind == ArcadeEventKind.Service) { ledger.Service(input.id, input.side); return; }
            if (OperatorPaused) return;
            if (input.kind != ArcadeEventKind.Start || input.time < enteredAt) return;
            bool first = State == ArcadeFlowState.Attract || State == ArcadeFlowState.Demo || State == ArcadeFlowState.LoadingDemo || State == ArcadeFlowState.Ranking && !resumeWinnerAfterRanking;
            bool continuing = State == ArcadeFlowState.Continue && input.side == ContinueSide && input.time <= Deadline;
            bool simultaneous = State == ArcadeFlowState.Select && !TwoPlayers && input.side != HumanSide && input.time == firstStartTime;
            bool joiningSelect = State == ArcadeFlowState.Select && !TwoPlayers && input.side != HumanSide && settings.joinIn && input.time <= Deadline;
            bool challenging = State == ArcadeFlowState.Fight && !TwoPlayers && input.side != HumanSide && settings.joinIn && challengeEligible;
            if (!first && !continuing && !simultaneous && !joiningSelect && !challenging) return;
            var action = continuing ? ArcadePaidAction.Continue : first || simultaneous ? ArcadePaidAction.Start : ArcadePaidAction.Challenge;
            if (ledger.Begin(input.id, input.side, action, out long session) != ArcadeLedgerResult.Accepted) return;
            sessions[input.side] = session;
            if (first)
            {
                HumanSide = input.side; TwoPlayers = false; ladder = null; StageIndex = 0;
                runs[0] = runs[1] = null; BeginRun(input.side); HighlightRunId = null;
                cursors[input.side] = 0; firstStartTime = input.time; BeginSelection(now); return;
            }
            if (simultaneous || joiningSelect || challenging)
            {
                if (challenging) RecordCpuTime();
                BeginRun(input.side);
                TwoPlayers = true; cursors[input.side] = 0; confirmed[0] = confirmed[1] = false; Match = null;
                if (challenging) Enter(ArcadeFlowState.Challenger, now, 2);
                else BeginSelection(now);
                return;
            }
            if (continuing && !settings.continueReselect) BeginPaidMatch(now);
            else BeginSelection(now);
        }

        void BeginSelection(double now)
        { Match = null; confirmed[0] = confirmed[1] = false; Enter(ArcadeFlowState.Select, now, settings.selectSeconds); }

        public void MoveSelection(int direction) { if (HumanSide >= 0) MoveSelection(HumanSide, direction); }
        public void MoveSelection(int side, int direction)
        { if (CanSelect(side)) cursors[side] = (cursors[side] + Math.Sign(direction) + roster.Length) % roster.Length; }

        public void ConfirmSelection(double now) { if (HumanSide >= 0) ConfirmSelection(HumanSide, now); }
        public void ConfirmSelection(int side, double now)
        {
            Clock(now); if (!CanSelect(side)) return;
            confirmed[side] = true;
            if (TwoPlayers && (!confirmed[0] || !confirmed[1])) return;
            BeginPaidMatch(now);
        }

        void BeginRun(int side)
        { runs[side] = new ArcadeScore { runId = Guid.NewGuid().ToString("N"), rules = RankingRules, initials = "AAA", fighter = roster[0] }; }
        void RecordCpuTime()
        {
            if (!TwoPlayers && HumanSide >= 0 && runs[HumanSide] != null)
                runs[HumanSide].fightTicks = checked(runs[HumanSide].fightTicks + observedFightTicks);
            observedFightTicks = 0;
        }
        void BeginPaidMatch(double now)
        {
            observedFightTicks = 0;
            for (int side = 0; side < 2; side++) if (runs[side] != null && sessions[side] != 0) runs[side].fighter = Fighter(side);
            if (!TwoPlayers && ladder == null)
                ladder = new ArcadeLadder(roster, SelectedFighter, settings.boss, settings.ladderStages, stages, random.Next());
            int level = Math.Max(1, Math.Min(10, settings.difficulty + StageIndex));
            string opponent = TwoPlayers ? null : ladder.Opponent(StageIndex);
            string stage = ladder != null ? ladder.Stage(StageIndex) : stages.Length == 0 ? "arena" : stages[random.Next(stages.Length)];
            Match = new ArcadeMatchSetup(Guid.NewGuid().ToString("N"), TwoPlayers || HumanSide == 0 ? Fighter(0) : opponent,
                TwoPlayers || HumanSide == 1 ? Fighter(1) : opponent, !TwoPlayers && HumanSide != 0, !TwoPlayers && HumanSide != 1, level, level,
                settings.roundSeconds, !TwoPlayers && StageIndex == ladder.Count - 1 ? settings.bossRoundsToWin : settings.roundsToWin,
                true, stage);
            Enter(ArcadeFlowState.Loading, now, settings.loadTimeoutSeconds);
        }

        void BeginDemo(double now)
        {
            string first = roster[random.Next(roster.Length)], second = roster[random.Next(roster.Length)];
            string stage = stages.Length == 0 ? "arena" : stages[random.Next(stages.Length)];
            Match = new ArcadeMatchSetup(Guid.NewGuid().ToString("N"), first, second, true, true, 4, 4, 30, 1, false, stage);
            Enter(ArcadeFlowState.LoadingDemo, now, settings.loadTimeoutSeconds);
        }

        public void MatchReady(string id, double now)
        {
            Clock(now); if (OperatorPaused || Match == null || Match.MatchId != id) return;
            if (State == ArcadeFlowState.LoadingDemo) Enter(ArcadeFlowState.Demo, now, 30);
            else if (State == ArcadeFlowState.Loading)
            {
                foreach (long session in sessions) if (session != 0) ledger.MarkMatchReady(session);
                Enter(ArcadeFlowState.Fight, now, double.PositiveInfinity);
            }
        }

        public void MatchFinished(string id, int winner, double now, long fightTicks = 0, int opponentRounds = 1)
        {
            Clock(now); if (OperatorPaused || Match == null || Match.MatchId != id) return;
            if (State == ArcadeFlowState.Demo) { ShowIdleRanking(now); return; }
            if (State != ArcadeFlowState.Fight) return;
            if (winner < -1 || winner > 1) throw new ArgumentOutOfRangeException(nameof(winner));
            if (fightTicks < 0 || opponentRounds < 0) throw new ArgumentOutOfRangeException("Match score counters must be nonnegative.");
            observedFightTicks = Math.Max(observedFightTicks, fightTicks);
            if (!TwoPlayers)
            {
                RecordCpuTime();
                if (winner == HumanSide && runs[HumanSide] != null)
                { runs[HumanSide].stagesCleared++; if (opponentRounds == 0) runs[HumanSide].cleanWins++; }
            }
            won = winner == HumanSide; versusWinner = winner;
            Enter(ArcadeFlowState.Victory, now, settings.victoryMaxSeconds);
        }

        public void FinishVictory(double now)
        {
            Clock(now); if (OperatorPaused || State != ArcadeFlowState.Victory) return;
            if (TwoPlayers)
            {
                // A drawn versus match keeps both entitlements and replays without a payment.
                if (versusWinner < 0) { BeginPaidMatch(now); return; }
                HumanSide = versusWinner; int loser = 1 - HumanSide;
                ledger.Complete(sessions[loser]); sessions[loser] = 0; Match = null;
                Enter(ArcadeFlowState.Continue, now, settings.continueSeconds); return;
            }
            if (won && StageIndex + 1 < ladder.Count) { StageIndex++; BeginPaidMatch(now); return; }
            ledger.Complete(SessionId); sessions[HumanSide] = 0; Match = null;
            Enter(won ? ArcadeFlowState.Ending : ArcadeFlowState.Continue, now, won ? 5 : settings.continueSeconds);
        }

        public void AccelerateContinue(double seconds)
        {
            if (!OperatorPaused && State == ArcadeFlowState.Continue && settings.continueAcceleration && seconds > 0 && !double.IsInfinity(seconds))
                Deadline -= Math.Min(seconds, 1); // Adapter requires release after the fight before this can activate.
        }

        public void Advance(double now)
        {
            Clock(now); if (OperatorPaused || now < Deadline) return;
            switch (State)
            {
                case ArcadeFlowState.Attract: BeginDemo(now); break;
                case ArcadeFlowState.Demo: ShowIdleRanking(now); break;
                case ArcadeFlowState.Challenger: BeginSelection(now); break;
                case ArcadeFlowState.Select:
                    confirmed[0] = confirmed[1] = true; BeginPaidMatch(now); break;
                case ArcadeFlowState.Loading: case ArcadeFlowState.LoadingDemo: Fail("The match did not become ready before the loading deadline.", now); break;
                case ArcadeFlowState.Victory: FinishVictory(now); break;
                case ArcadeFlowState.Continue:
                    if (TwoPlayers) { TwoPlayers = false; FinishRun(1 - HumanSide, true, now); }
                    else Enter(ArcadeFlowState.GameOver, now, 3);
                    break;
                case ArcadeFlowState.GameOver: case ArcadeFlowState.Ending: FinishRun(HumanSide, false, now); break;
                case ArcadeFlowState.NameEntry: SaveName(now); break;
                case ArcadeFlowState.Ranking: FinishRanking(now); break;
            }
        }

        void FinishRun(int side, bool resumeWinner, double now)
        {
            resumeWinnerAfterRanking = resumeWinner;
            pendingScore = runs[side]; runs[side] = null;
            if (ranking != null && pendingScore != null && pendingScore.stagesCleared > 0 && pendingScore.fightTicks > 0 && ranking.Qualifies(pendingScore))
            {
                NameSide = side; NameEntry = new ArcadeNameEntry(); Match = null;
                Enter(ArcadeFlowState.NameEntry, now, settings.nameEntrySeconds); return;
            }
            pendingScore = null; FinishRanking(now);
        }
        public void EditName(int side, int characterStep, int positionStep)
        {
            if (OperatorPaused || State != ArcadeFlowState.NameEntry || side != NameSide) return;
            NameEntry.Change(characterStep); NameEntry.Move(positionStep);
        }
        public void ConfirmName(int side, double now)
        {
            Clock(now); if (OperatorPaused || State != ArcadeFlowState.NameEntry || side != NameSide) return;
            if (NameEntry.Confirm()) SaveName(now);
        }
        void SaveName(double now)
        {
            pendingScore.initials = NameEntry.Initials;
            if (ranking.Submit(pendingScore)) HighlightRunId = pendingScore.runId;
            pendingScore = null; NameEntry = null; NameSide = -1;
            Enter(ArcadeFlowState.Ranking, now, 8);
        }
        void FinishRanking(double now)
        {
            NameEntry = null; NameSide = -1; pendingScore = null;
            if (resumeWinnerAfterRanking) { resumeWinnerAfterRanking = false; BeginPaidMatch(now); }
            else ReturnToAttract(now);
        }
        void ShowIdleRanking(double now)
        {
            if (ranking == null || ranking.Table(RankingRules).Length == 0) { ReturnToAttract(now); return; }
            Match = null; resumeWinnerAfterRanking = false; HighlightRunId = null;
            Enter(ArcadeFlowState.Ranking, now, 8);
        }

        public void Fail(string reason, double now)
        {
            Clock(now); if (State == ArcadeFlowState.Fault) return;
            Error = reason;
            try { ledger.RecoverInterruptedSessions(); sessions[0] = sessions[1] = 0; }
            catch (Exception e) { Error += " Recovery requires service: " + e.Message; }
            Match = null; Enter(ArcadeFlowState.Fault, now, double.PositiveInfinity);
        }

        void ReturnToAttract(double now)
        {
            Match = null; HumanSide = -1; pendingScore = null; NameEntry = null; NameSide = -1; runs[0] = runs[1] = null; resumeWinnerAfterRanking = false; sessions[0] = sessions[1] = 0; TwoPlayers = false; ladder = null; StageIndex = 0;
            Enter(ArcadeFlowState.Attract, now, 6);
        }
        void Enter(ArcadeFlowState state, double now, double duration)
        { State = state; enteredAt = now; Deadline = now + duration; challengeEligible = false; }
        void Clock(double now)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < lastTime) throw new ArgumentOutOfRangeException(nameof(now));
            lastTime = now;
        }
    }
}
