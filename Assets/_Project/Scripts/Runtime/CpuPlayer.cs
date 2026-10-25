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
        int[] steps;
        int step, stepCount, stepFrame, pause;
        int guardedMove = -1;
        bool guarding;
        int getupDelay = -1, getupStick = 8, getupButtons;
        // Original getups from the lying loops: stand in place (up), roll forward/back, or a getup kick.
        static readonly int[,] Getups = { { 8, 0 }, { 8, 0 }, { 6, 0 }, { 4, 0 }, { 5, 8 }, { 5, 4 } };

        public CpuPlayer(CommandGraph graph, int seed) { this.graph = graph; random = new Random(seed); }
        public void Clear() { steps = null; pause = 0; guardedMove = -1; guarding = false; }

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
                    getupDelay = 6 + random.Next(30);
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
                if (step >= stepCount) { steps = null; pause = 20 + random.Next(40); }
                return command;
            }
            if (pause > 0) pause--;
            float gap = Math.Abs(foe.x - self.x);
            // Decide once per incoming attack whether to hold back (or down-back against lows) through it.
            if (!foe.attacking) guardedMove = -1;
            else if (foe.move != guardedMove) { guardedMove = foe.move; guarding = gap < 2f && random.Next(5) < 2; }
            if (foe.attacking && guarding)
                command.stick = ToScreen(foe.lowAttack ? 1 : back, self.facing);
            else if (gap > 1.6f)
                command.stick = ToScreen(forward, self.facing);
            else if (pause == 0 && graph.demos != null && graph.demos.Length > 0 && self.stun == 0)
            {
                var demo = graph.demos[random.Next(graph.demos.Length)];
                if (demo.steps != null && demo.steps.Length >= 3)
                {
                    // Skip the demo's trailing neutral recording time; the fight continues meanwhile.
                    stepCount = demo.steps.Length / 3;
                    while (stepCount > 1 && demo.steps[(stepCount - 1) * 3] == 5 && demo.steps[(stepCount - 1) * 3 + 1] == 0) stepCount--;
                    steps = demo.steps; step = stepFrame = 0;
                }
            }
            return command;
        }

        static int ToScreen(int relative, int facing) => BattleModel.Relative(relative, facing);
    }
}
