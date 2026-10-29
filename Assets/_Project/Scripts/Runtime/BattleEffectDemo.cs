using System;
using System.Collections.Generic;

namespace Versus.Remaster
{
    /// <summary>
    /// Directs both fighters through a short exchange that produces one battle effect, for the effect tuning scene
    /// (VersusGame.effectTuningPanel): a clean hit, a counter hit, a guarded hit, a trade (both struck on one tick), a
    /// KO and a Rage Drive KO. Fighters stand idle between demonstrations, get up when knocked down, and are placed
    /// at a measured distance before each one. Which button gives which move, how fast it lands and from how far is
    /// measured on a scratch BattleModel with the same movesets, so no per-character table is needed. Pure C#; a
    /// demonstration aid, not original behaviour.
    /// </summary>
    public sealed class BattleEffectDemo
    {
        public enum Scenario { Hit, Counter, Guard, Trade, Ko, RageDriveKo }
        const long RageDriveProperty = 0x801B;
        const int IdleTimeout = 360, RunTimeout = 240, ProbeTicks = 90;
        static readonly int[] Buttons = { 1, 2, 4, 8 };
        static readonly float[] Distances = { 1.6f, 1.4f, 1.2f, 1.05f, .9f, .75f, .6f };

        readonly BattleModel battle;
        readonly CommandGraph[] graphs;
        readonly CpuPlayer[] cpu;
        BattleModel scratch;
        Scenario scenario;
        int stage = -1, ticks, attacker = 0;
        float distance = 1;
        // Per player: (relative stick, buttons, frames) triples with a start tick, or a move to force at start.
        readonly int[][] script = new int[2][];
        readonly int[] scriptStart = new int[2], scriptStep = new int[2], scriptFrame = new int[2], forced = { -1, -1 }, hold = new int[2];
        /// <summary>A demonstration is being prepared or played.</summary>
        public bool Active => stage >= 0;
        public Scenario Current => scenario;
        /// <summary>Report of the last measurements and events (for the panel and tests).</summary>
        public string Report { get; private set; } = "";
        public int hits, counters, guards, trades, kos, rageDriveKos;

        public BattleEffectDemo(BattleModel battle, CommandGraph[] graphs, CpuPlayer[] cpu)
        {
            this.battle = battle; this.graphs = graphs; this.cpu = cpu;
            battle.Hit += OnHit; battle.MoveProperty += OnMoveProperty;
        }

        readonly int[] struck = { -1, -1 };
        readonly bool[] rageDrive = new bool[2];
        void OnMoveProperty(int player, long id, long value) { if (id == RageDriveProperty) rageDrive[player] = value != 0; }
        void OnHit(HitEvent hit)
        {
            if (!Active) return;
            if (hit.blocked) guards++;
            else { hits++; if (hit.counterHit) counters++; struck[hit.defender] = hit.damage; }
        }

        /// <summary>Starts a demonstration; a running one is replaced.</summary>
        public void Start(Scenario what, int attackingPlayer = 0)
        {
            scenario = what; attacker = attackingPlayer; stage = 0; ticks = 0;
            script[0] = script[1] = null; forced[0] = forced[1] = -1; hold[0] = hold[1] = 0;
            struck[0] = struck[1] = -1;
        }

        public void Stop() { stage = -1; script[0] = script[1] = null; }

        /// <summary>The command for a player this tick: the demonstration's, a getup when lying, else neutral.</summary>
        public PlayerCommand Command(int player, FighterState self, FighterState foe)
        {
            if (self.downedTicks > 0) return cpu[player].Next(self, foe);
            var command = PlayerCommand.Neutral;
            if (stage != 2) return command;
            if (hold[player] != 0) command.stick = BattleModel.Relative(hold[player], self.facing);
            var steps = script[player];
            if (steps == null || ticks < scriptStart[player] || scriptStep[player] * 3 >= steps.Length) return command;
            int k = scriptStep[player] * 3;
            command.stick = BattleModel.Relative(steps[k], self.facing); command.buttons = steps[k + 1];
            if (++scriptFrame[player] >= steps[k + 2]) { scriptStep[player]++; scriptFrame[player] = 0; }
            return command;
        }

        /// <summary>Call after each battle tick.</summary>
        public void Tick()
        {
            if (!Active) return;
            ticks++;
            var a = battle.fighters[attacker]; var d = battle.fighters[1 - attacker];
            if (stage == 0)
            {
                // Wait for the fight and for both fighters to stand idle, then place them and plan the exchange.
                bool idle = battle.phase == MatchPhase.Fight && Idle(a) && Idle(d);
                if (!idle && ticks < IdleTimeout) return;
                if (battle.phase != MatchPhase.Fight) { Report = "Not in a fight"; Stop(); return; }
                Plan(); Place();
                stage = 2; ticks = 0; scriptStep[0] = scriptStep[1] = scriptFrame[0] = scriptFrame[1] = 0;
                for (int i = 0; i < 2; i++) if (forced[i] >= 0) { battle.interpreters[i].Force(forced[i]); battle.fighters[i].move = forced[i]; battle.fighters[i].moveFrame = 0; }
                return;
            }
            if (stage == 2)
            {
                if (struck[0] > 0 && struck[1] > 0) trades++;
                if (struck[0] > 0 || struck[1] > 0)
                {
                    if (battle.phase != MatchPhase.Fight) { kos++; if (battle.winner >= 0 && rageDrive[battle.winner]) rageDriveKos++; }
                }
                struck[0] = struck[1] = -1;
                bool scriptsDone = true;
                for (int i = 0; i < 2; i++) if (script[i] != null && scriptStep[i] * 3 < script[i].Length) scriptsDone = false;
                if ((scriptsDone && ticks > 90) || ticks > RunTimeout || battle.phase != MatchPhase.Fight)
                {
                    if (scenario == Scenario.RageDriveKo || scenario == Scenario.Ko) battle.rageGranted[attacker] = false;
                    Stop();
                }
            }
        }

        static bool Idle(FighterState f) => !f.attacking && !f.reacting && !f.downed && !f.airborne && f.stun == 0 && f.downedTicks == 0;

        // Places the fighters this far apart on the fight line, around their midpoint, both on the floor line z.
        void Place()
        {
            var a = battle.fighters[attacker]; var d = battle.fighters[1 - attacker];
            float mx = (a.x + d.x) * .5f, mz = (a.z + d.z) * .5f;
            float rx = battle.screenRightX, rz = battle.screenRightZ;
            float side = (d.x - a.x) * rx + (d.z - a.z) * rz >= 0 ? 1 : -1;
            float half = distance * .5f, limit = battle.rules.arenaHalfWidth - 1;
            mx = Math.Max(-limit + half, Math.Min(limit - half, mx));
            mz = Math.Max(-battle.rules.arenaHalfDepth + .5f, Math.Min(battle.rules.arenaHalfDepth - .5f, mz));
            a.x = mx - rx * half * side; a.z = mz - rz * half * side;
            d.x = mx + rx * half * side; d.z = mz + rz * half * side;
            a.dirX = rx * side; a.dirZ = rz * side; d.dirX = -rx * side; d.dirZ = -rz * side;
        }

        void Plan()
        {
            int A = attacker, D = 1 - attacker;
            // The fastest press that lands (a jab); among equals, one a standing guard blocks (a standard high/mid).
            var any = Best(A, m => m.hit, m => -m.startup * 2 + (m.blockedStanding ? 1 : 0));
            switch (scenario)
            {
                case Scenario.Hit:
                    Press(A, any); break;
                case Scenario.Guard:
                    // The defender holds back from the start; the attacker's press must be one a standing guard blocks.
                    var blocked = Best(A, m => m.hit && m.blockedStanding, m => -m.startup) ?? any;
                    hold[D] = 4; Press(D, null); Press(A, blocked);
                    if (blocked != null && blocked.blockedStanding) distance = blocked.guardDistance;
                    break;
                case Scenario.Counter:
                    // The defender starts its slowest landing press; the attacker's fastest lands inside its startup.
                    var slow = Best(D, m => m.hit, m => m.startup);
                    Press(D, slow, 0);
                    Press(A, any, slow != null && any != null ? Math.Max(0, slow.startup - any.startup - 4) : 0);
                    break;
                case Scenario.Trade:
                    // Both press their heaviest landing move, the slower one first, so both land on the same tick.
                    var ta = Best(A, m => m.hit && m.damage >= 17, m => m.damage) ?? any;
                    var td = Best(D, m => m.hit && m.damage >= 17, m => m.damage) ?? Best(D, m => m.hit, m => m.damage);
                    if (ta != null && td != null)
                    {
                        distance = Math.Min(ta.distance, td.distance);
                        Press(A, ta, Math.Max(0, td.startup - ta.startup)); Press(D, td, Math.Max(0, ta.startup - td.startup));
                    }
                    break;
                case Scenario.Ko:
                    battle.fighters[D].health = 1; Press(A, any); break;
                case Scenario.RageDriveKo:
                    // The attacker's Rage Drive move is forced (the movelist demos carry no Rage Drive input); Rage is
                    // granted so its 0x801B and hits resolve as in a real one. The defender takes it at 1 health.
                    int drive = RageDriveMove(A);
                    var probe = drive >= 0 ? ProbeMove(A, drive) : null;
                    battle.fighters[D].health = 1; battle.rageGranted[A] = true;
                    if (probe != null && probe.hit) { forced[A] = drive; distance = probe.distance; used = "P" + (A + 1) + " " + probe.name + " forced (" + probe.startup + " ticks)"; }
                    else Press(A, any);
                    break;
            }
            Report = scenario + ": " + (used.Length > 0 ? used + " at " + distance.ToString("0.00") + " m" : "no landing press found");
            used = "";
        }

        string used = "";
        void Press(int player, Probe probe, int start = 0)
        {
            if (probe == null) { script[player] = null; return; }
            script[player] = new[] { 5, probe.button, 2, 5, 0, 1 }; scriptStart[player] = start;
            if (scenario != Scenario.Trade) distance = probe.distance;
            used += (used.Length > 0 ? ", " : "") + "P" + (player + 1) + " " + probe.name + " (" + probe.startup + " ticks" +
                (start > 0 ? ", from tick " + start : "") + (probe.blockedStanding ? "" : ", not blocked standing") + ")";
        }

        sealed class Probe { public int button, move, startup, damage; public float distance, guardDistance; public bool hit, blockedStanding; public string name; }

        Probe Best(int player, Func<Probe, bool> ok, Func<Probe, int> score)
        {
            Probe best = null;
            foreach (int button in Buttons)
            {
                var p = ProbePress(player, button);
                if (p != null && ok(p) && (best == null || score(p) > score(best))) best = p;
            }
            return best;
        }

        int RageDriveMove(int player)
        {
            var moves = graphs[player].moves;
            for (int i = 0; i < moves.Length; i++)
            {
                var p = moves[i].properties; if (p == null || moves[i].damage <= 0) continue;
                for (int k = 0; k + 2 < p.Length; k += 3) if (p[k + 1] == RageDriveProperty && p[k + 2] != 0 && (p[k] & 0x3FFF) <= 1) return i;
            }
            return -1;
        }

        // Measures a press (or a forced move) on a scratch model: the furthest distance it lands from, how many ticks
        // it takes, its damage, and whether a standing guard blocks it.
        Probe ProbePress(int player, int button) => Measure(player, button, -1);
        Probe ProbeMove(int player, int move) => Measure(player, 0, move);
        Probe Measure(int player, int button, int move)
        {
            if (scratch == null) scratch = new BattleModel(battle.rules, graphs);
            var result = new Probe { button = button, move = move, name = move >= 0 ? graphs[player].moves[move].name : "button " + button };
            foreach (float far in Distances)
            {
                var landed = Run(player, button, move, far, false);
                if (landed == null) continue;
                result.hit = true; result.distance = far; result.startup = landed.Value.tick; result.damage = landed.Value.hit.damage;
                var it = scratch.interpreters[player]; result.name = it.ActedGraph.moves[it.ActedMove].name;
                // A guarding defender walks back while holding, so the guarded hit lands from nearer: find that distance.
                foreach (float near in Distances)
                {
                    if (near > far) continue;
                    var guarded = Run(player, button, move, near, true);
                    if (guarded != null && guarded.Value.hit.blocked) { result.blockedStanding = true; result.guardDistance = near; break; }
                }
                return result;
            }
            return result;
        }

        (int tick, HitEvent hit)? Run(int player, int button, int move, float far, bool guard)
        {
            scratch.Restart(); scratch.BeginFight();
            scratch.rageGranted[player] = battle.rageGranted[player];
            var self = scratch.fighters[player]; var foe = scratch.fighters[1 - player];
            self.x = player == 0 ? -far * .5f : far * .5f; foe.x = -self.x; self.z = foe.z = 0;
            (int, HitEvent)? landed = null;
            void Catch(HitEvent h) { if (landed == null && h.attacker == player) landed = (0, h); }
            scratch.Hit += Catch;
            try
            {
                if (move >= 0) { scratch.interpreters[player].Force(move); self.move = move; self.moveFrame = 0; }
                for (int t = 0; t < ProbeTicks && landed == null; t++)
                {
                    var mine = PlayerCommand.Neutral; var theirs = PlayerCommand.Neutral;
                    if (move < 0 && t < 2) mine.buttons = button;
                    if (guard) theirs.stick = BattleModel.Relative(4, foe.facing);
                    scratch.Tick(player == 0 ? mine : theirs, player == 0 ? theirs : mine);
                    if (landed != null) landed = (t + 1, landed.Value.Item2);
                }
            }
            finally { scratch.Hit -= Catch; }
            return landed;
        }
    }
}
