using System;

namespace Versus.Remaster.Arcade
{
    // Immutable per-match setup. MatchId is distinct from a ledger entitlement/session ID: a ladder
    // entitlement can own several matches. Fighter names are validated against the roster by the bridge.
    public sealed class ArcadeMatchSetup
    {
        public string MatchId { get; }
        public string PlayerOne { get; }
        public string PlayerTwo { get; }
        public bool CpuOne { get; }
        public bool CpuTwo { get; }
        public int LevelOne { get; }
        public int LevelTwo { get; }
        public int RoundSeconds { get; }
        public int RoundsToWin { get; }
        public bool PlayEntrance { get; }
        public string Stage { get; }

        public ArcadeMatchSetup(string matchId, string playerOne, string playerTwo, bool cpuOne, bool cpuTwo,
            int levelOne = 4, int levelTwo = 4, int roundSeconds = 60, int roundsToWin = 2, bool playEntrance = true, string stage = null)
        {
            if (string.IsNullOrWhiteSpace(matchId) || matchId.Length > 128) throw new ArgumentException("A bounded, unique match ID is required.");
            if (string.IsNullOrWhiteSpace(playerOne) || string.IsNullOrWhiteSpace(playerTwo)) throw new ArgumentException("Both fighters are required.");
            if (levelOne < 1 || levelOne > 10 || levelTwo < 1 || levelTwo > 10) throw new ArgumentOutOfRangeException("CPU levels must be 1..10.");
            if (roundSeconds < 10 || roundSeconds > 300 || roundsToWin < 1 || roundsToWin > 10) throw new ArgumentOutOfRangeException("Invalid arcade round rules.");
            MatchId = matchId; PlayerOne = playerOne; PlayerTwo = playerTwo; CpuOne = cpuOne; CpuTwo = cpuTwo;
            LevelOne = levelOne; LevelTwo = levelTwo; RoundSeconds = roundSeconds; RoundsToWin = roundsToWin; PlayEntrance = playEntrance;
            Stage = stage;
        }

        public bool IsCpu(int side) { Side(side); return side == 0 ? CpuOne : CpuTwo; }
        public int CpuLevel(int side) { Side(side); return side == 0 ? LevelOne : LevelTwo; }
        internal static void Side(int side) { if (side < 0 || side > 1) throw new ArgumentOutOfRangeException(nameof(side)); }

        public MatchRules BuildRules(MatchRules baseline, int health)
        {
            if (baseline == null) throw new ArgumentNullException(nameof(baseline));
            var rules = new MatchRules { health = health, seconds = RoundSeconds, roundsToWin = RoundsToWin,
                walkSpeed = baseline.walkSpeed, arenaHalfWidth = baseline.arenaHalfWidth, arenaHalfDepth = baseline.arenaHalfDepth };
            rules.Validate(); return rules;
        }
    }

    public enum ArcadeRequestState { Pending, Claimed, Ready, Failed, Cancelled, Released }

    public sealed class ArcadeMatchRequest
    {
        public ArcadeMatchSetup Setup { get; }
        public ArcadeInput Input { get; }
        public string ScenePath { get; }
        public ArcadeRequestState State { get; private set; }
        public string Failure { get; private set; }

        public ArcadeMatchRequest(ArcadeMatchSetup setup, ArcadeInput input, string scenePath)
        {
            Setup = setup ?? throw new ArgumentNullException(nameof(setup)); Input = input ?? throw new ArgumentNullException(nameof(input));
            if (string.IsNullOrWhiteSpace(scenePath) || !scenePath.StartsWith("Assets/", StringComparison.Ordinal) ||
                !scenePath.EndsWith(".unity", StringComparison.Ordinal)) throw new ArgumentException("Use an explicit project scene path.");
            ScenePath = scenePath;
        }
        public bool TryClaim(string scenePath)
        {
            if (State != ArcadeRequestState.Pending || !string.Equals(ScenePath, scenePath, StringComparison.Ordinal)) return false;
            State = ArcadeRequestState.Claimed; return true;
        }
        public bool Cancel()
        {
            if (State != ArcadeRequestState.Pending) return false;
            State = ArcadeRequestState.Cancelled; return true;
        }
        public void MarkReady()
        {
            if (State != ArcadeRequestState.Claimed) throw new InvalidOperationException("Only a claimed match can become ready.");
            State = ArcadeRequestState.Ready;
        }
        public void Fail(Exception error)
        {
            if (State != ArcadeRequestState.Claimed) throw new InvalidOperationException("Only an initializing match can fail.");
            Failure = error?.ToString() ?? "Unknown match initialization failure."; State = ArcadeRequestState.Failed;
        }
        public void Release()
        {
            if (State == ArcadeRequestState.Ready || State == ArcadeRequestState.Claimed) State = ArcadeRequestState.Released;
        }
    }

    public sealed class ArcadeMatchResult
    {
        public string MatchId { get; }
        public int Winner { get; }
        public int PlayerOneRounds { get; }
        public int PlayerTwoRounds { get; }
        public int Round { get; }
        public bool FinalRoundPerfect { get; }
        public bool TimeUp { get; }
        public long FightTicks { get; }
        public int FinalTicksLeft { get; }
        internal ArcadeMatchResult(ArcadeMatchSetup setup, BattleModel battle, long fightTicks)
        {
            MatchId = setup.MatchId; Winner = battle.winner; PlayerOneRounds = battle.fighters[0].wins;
            PlayerTwoRounds = battle.fighters[1].wins; Round = battle.round;
            FinalRoundPerfect = Winner >= 0 && battle.fighters[Winner].health == battle.rules.health;
            TimeUp = battle.result == "TIME UP"; FightTicks = fightTicks; FinalTicksLeft = battle.ticksLeft;
        }
    }

    // Drives the real model in arcade mode only. Presentation and system/coin events stay with their
    // own adapters; this class never drains cabinet events and never restarts a completed match.
    public sealed class ArcadeMatchController
    {
        readonly ArcadeMatchSetup setup;
        readonly ArcadeInput input;
        readonly CpuPlayer[] cpu;
        readonly int idleGetupTicks;
        long fightTicks;
        public long FightTicks => fightTicks;
        bool completionTaken;
        bool suspended;
        public bool Suspended => suspended || input.OperatorPaused;
        public ArcadeMatchResult Result { get; private set; }

        public ArcadeMatchController(ArcadeMatchSetup setup, ArcadeInput input, CpuPlayer[] cpu, int idleGetupTicks)
        {
            this.setup = setup ?? throw new ArgumentNullException(nameof(setup)); this.input = input ?? throw new ArgumentNullException(nameof(input));
            if (cpu == null || cpu.Length != 2 || cpu[0] == null || cpu[1] == null) throw new ArgumentException("Two CPU controllers are required.");
            if (idleGetupTicks < 1) throw new ArgumentOutOfRangeException(nameof(idleGetupTicks));
            this.cpu = (CpuPlayer[])cpu.Clone(); this.idleGetupTicks = idleGetupTicks;
            input.ResetGameplayForTransition();
        }

        public void SetSuspended(bool value)
        {
            if (suspended == value) return;
            suspended = value; input.ResetGameplayForTransition();
            for (int i = 0; i < 2; i++) cpu[i].Clear();
        }

        public void Tick(BattleModel battle, out PlayerCommand one, out PlayerCommand two)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));
            one = two = PlayerCommand.Neutral;
            if (Suspended) return;
            var previous = battle.phase;
            if (previous == MatchPhase.Fight && Result == null)
            {
                one = Command(battle, 0); two = Command(battle, 1); fightTicks++;
            }
            battle.Tick(one, two); // Neutral end-of-round ticks preserve the original death/victory flow.
            if (previous != MatchPhase.Fight && battle.phase == MatchPhase.Fight)
            {
                input.ResetGameplayForTransition();
                for (int i = 0; i < 2; i++) cpu[i].Clear();
            }
            if (battle.phase == MatchPhase.MatchOver && Result == null) Result = new ArcadeMatchResult(setup, battle, fightTicks);
        }

        PlayerCommand Command(BattleModel battle, int side)
        {
            if (setup.IsCpu(side)) return cpu[side].Next(battle.fighters[side], battle.fighters[1 - side]);
            var controls = input.ConsumeGameplay(side);
            var command = PlayerCommand.Neutral; command.stick = controls.stick; command.buttons = controls.buttons;
            if (battle.fighters[side].downedTicks >= idleGetupTicks && command.stick == 5 && command.buttons == 0) command.stick = 8;
            return command;
        }

        public int VictoryButtons(int side) => setup.IsCpu(side) ? 0 : input.HeldButtons(side);
        public bool TryTakeCompletion(out ArcadeMatchResult result)
        {
            result = null;
            if (completionTaken || Result == null) return false;
            completionTaken = true; result = Result; return true;
        }
    }
}
