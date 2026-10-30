using System;

namespace Versus.Remaster
{
    /// <summary>
    /// Automatic-fight controller. It approaches, then performs a random original movelist command by
    /// replaying that entry's in-game demo input, so every attack goes through the same command graph
    /// a player uses. It sometimes holds back to guard when the opponent attacks.
    /// </summary>
    public sealed class CpuPlayer
    {
        readonly CommandGraph graph;
        readonly Random random;
        // 0 keeps the desktop/demo policy and RNG sequence unchanged. 1..10 are authored arcade
        // policies affecting decisions and delays only; the original movelist inputs stay intact.
        public int Difficulty { get; }
        int guardReaction;
        int[] steps;
        int step, stepCount, stepFrame, pause;
        int guardedMove = -1;
        bool guarding;
        int getupDelay = -1, getupStick = 8, getupButtons;
        // Original getups from the lying loops: stand in place (up), roll forward/back, or a getup kick.
        static readonly int[,] Getups = { { 8, 0 }, { 8, 0 }, { 6, 0 }, { 4, 0 }, { 5, 8 }, { 5, 4 } };

        // The movelist's Rage Art demo (CommandGraph.rageArt): its original input, replayed on request.
        readonly CommandGraph.Demo rageArt;
        bool rageArtRequested;
        /// <summary>True while a requested Rage Art waits for range, and while its input is replayed.</summary>
        public bool RageArtPending => rageArtRequested;
        public bool RageArtReplaying { get; private set; }
        public bool CanRageArt => rageArt != null;

        public CpuPlayer(CommandGraph graph, int seed) : this(graph, seed, 0) { }
        public CpuPlayer(CommandGraph graph, int seed, int difficulty)
        {
            if (difficulty < 0 || difficulty > 10) throw new ArgumentOutOfRangeException(nameof(difficulty));
            Difficulty = difficulty;
            this.graph = graph; random = new Random(seed);
            if (graph.rageArt != null && graph.rageArt.steps != null && graph.rageArt.steps.Length >= 3) rageArt = graph.rageArt;
        }
        public void Clear() { steps = null; pause = 0; guardedMove = -1; guarding = false; rageArtRequested = false; RageArtReplaying = false;
            if (Difficulty > 0) { guardReaction = 0; getupDelay = -1; } }
        /// <summary>Walk into range, then replay the original Rage Art input (needs Rage in the model).</summary>
        public void RequestRageArt() { if (rageArt != null) rageArtRequested = true; }

        public PlayerCommand Next(FighterState self, FighterState foe)
        {
            var command = PlayerCommand.Neutral;
            const int forward = 6, back = 4;
            if (self.downedTicks > 0)
            {
                // Lying down: after a short, varied pause choose one of the original getups.
                steps = null;
                if (getupDelay < 0)
                {
                    getupDelay = Difficulty == 0 ? 6 + random.Next(30) : Math.Max(2, 26 - Difficulty * 2) + random.Next(Math.Max(4, 28 - Difficulty * 2));
                    int pick = random.Next(Getups.GetLength(0));
                    getupStick = Getups[pick, 0]; getupButtons = Getups[pick, 1];
                }
                if (self.downedTicks >= getupDelay)
                { command.stick = ToScreen(getupStick, self.facing); command.buttons = self.downedTicks == getupDelay ? getupButtons : 0; }
                return command;
            }
            getupDelay = -1;
            if (steps != null)
            {
                int relative = steps[step * 3], buttons = steps[step * 3 + 1];
                command.stick = ToScreen(relative, self.facing); command.buttons = buttons;
                if (++stepFrame >= steps[step * 3 + 2]) { step++; stepFrame = 0; }
                if (step >= stepCount) { steps = null; pause = Difficulty == 0 ? 20 + random.Next(40) : (11 - Difficulty) * 5 + random.Next(10 + (11 - Difficulty) * 2); RageArtReplaying = false; }
                return command;
            }
            if (pause > 0) pause--;
            float gap = Math.Abs(foe.x - self.x);
            if (rageArtRequested)
            {
                // Close in (no guarding meanwhile), then start the Rage Art input once recovered.
                if (gap > 1.3f) { command.stick = ToScreen(forward, self.facing); return command; }
                if (self.stun == 0 && !self.attacking && !self.reacting && !self.downed)
                {
                    Start(rageArt);
                    rageArtRequested = false; RageArtReplaying = true;
                }
                return command;
            }
            // Decide once per incoming attack whether to hold back (or down-back against lows) through it.
            if (!foe.attacking) guardedMove = -1;
            else if (foe.move != guardedMove)
            {
                guardedMove = foe.move;
                guarding = gap < 2f && (Difficulty == 0 ? random.Next(5) < 2 : random.Next(100) < 15 + Difficulty * 7);
                guardReaction = Difficulty == 0 ? 0 : 14 - Difficulty;
            }
            if (foe.attacking && guarding && (Difficulty == 0 || guardReaction-- <= 0))
                command.stick = ToScreen(foe.lowAttack ? 1 : back, self.facing);
            else if (gap > 1.6f)
                command.stick = ToScreen(forward, self.facing);
            else if (pause == 0 && graph.demos != null && graph.demos.Length > 0 && self.stun == 0)
            {
                var demo = graph.demos[random.Next(graph.demos.Length)];
                if (demo.steps != null && demo.steps.Length >= 3) Start(demo);
            }
            return command;
        }

        void Start(CommandGraph.Demo demo)
        {
            // Skip the demo's trailing neutral recording time; the fight continues meanwhile.
            stepCount = demo.steps.Length / 3;
            while (stepCount > 1 && demo.steps[(stepCount - 1) * 3] == 5 && demo.steps[(stepCount - 1) * 3 + 1] == 0) stepCount--;
            steps = demo.steps; step = stepFrame = 0;
        }

        static int ToScreen(int relative, int facing) => BattleModel.Relative(relative, facing);
    }

    /// <summary>
    /// One press of the Rage Art button for one auto player (VersusGame). Grants Rage in the model while
    /// the CPU closes in and replays its original Rage Art input, retries the input if no Rage Art move
    /// came out (hit or still recovering when it played), and releases the grant once a Rage Art move
    /// has started and ended, the round leaves Fight, or 10 s pass. A demonstration aid, not original.
    /// </summary>
    public sealed class RageArtTrigger
    {
        const int RetryTicks = 30, TimeoutTicks = 600;
        int ticks = -1, idle;
        bool seen;
        public bool Active => ticks >= 0;
        /// <summary>Set when a Rage Art move started for this press (for reports and tests).</summary>
        public bool SawRageArt => seen;

        public void Press(BattleModel battle, int player, CpuPlayer cpu)
        {
            if (Active || !cpu.CanRageArt || battle.phase != MatchPhase.Fight) return;
            ticks = 0; idle = 0; seen = false;
            battle.rageGranted[player] = true;
            cpu.RequestRageArt();
        }

        /// <summary>Call after each battle tick.</summary>
        public void Tick(BattleModel battle, int player, CpuPlayer cpu)
        {
            if (!Active) return;
            ticks++;
            var it = battle.interpreters[player];
            bool inRageArt = it != null && it.ActedGraph != null && it.ActedGraph.moves[it.ActedMove].name.Contains("RageArts");
            if (inRageArt) seen = true;
            bool waiting = cpu.RageArtPending || cpu.RageArtReplaying;
            if (battle.phase != MatchPhase.Fight || ticks > TimeoutTicks || (seen && !inRageArt && !waiting)) { Release(battle, player); return; }
            if (!seen && !waiting && !inRageArt && ++idle > RetryTicks) { idle = 0; cpu.RequestRageArt(); }
        }

        public void Release(BattleModel battle, int player)
        {
            ticks = -1; idle = 0;
            if (battle != null) battle.rageGranted[player] = false;
        }
    }
}
