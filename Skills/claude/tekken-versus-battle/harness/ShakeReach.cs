using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Versus.Remaster;

// Are shake-carrying moves (properties 0x8001-0x800A) reached in CPU matches, and do they last until
// their property frame? Counts entries of such moves, how many raised the shake, and the moves that
// were left before the frame (with the frame they reached). Usage: run.sh ShakeReach [project].
static class ShakeReach
{
    static bool IsShake(long id) => id >= 0x8001 && id <= 0x800A;
    static int Main(string[] args)
    {
        UnityEngine.Resources.Root = Path.Combine(args[0], "Assets/_Project/Resources");
        foreach (var (a, b) in new[] { ("jin", "xia"), ("kaz", "ltn"), ("asa", "jin") })
        {
            var graphs = new[] { CommandGraph.Load(a), CommandGraph.Load(b) };
            var model = new BattleModel(new MatchRules { health = 170 }, graphs);
            var cpu = new[] { new CpuPlayer(graphs[0], 7), new CpuPlayer(graphs[1], 19) };
            int entered = 0, raised = 0, rounds = 0, landings = 0;
            var missed = new Dictionary<string, int>(); var fallMoves = new Dictionary<string, int>(); var raisedBy = new Dictionary<string, int>();
            var current = new (CommandGraph g, int m, int need, bool hit, int last)[2];
            model.MoveProperty += (p, id, v) =>
            {
                if (!IsShake(id)) return;
                raised++; current[p].hit = true;
                var it = model.interpreters[p]; string n = it.Active.moves[it.Move].name; raisedBy[n] = raisedBy.GetValueOrDefault(n) + 1;
            };
            var phase = model.phase; var wasDown = new bool[2];
            for (int t = 0; t < 60 * 300 && model.phase != MatchPhase.MatchOver; t++)
            {
                var c0 = model.phase == MatchPhase.Fight ? cpu[0].Next(model.fighters[0], model.fighters[1]) : PlayerCommand.Neutral;
                var c1 = model.phase == MatchPhase.Fight ? cpu[1].Next(model.fighters[1], model.fighters[0]) : PlayerCommand.Neutral;
                model.Tick(c0, c1);
                for (int p = 0; p < 2; p++)
                {
                    var it = model.interpreters[p]; var g = it.Active; int m = it.Move;
                    if (model.fighters[p].downed && !wasDown[p])
                    {
                        landings++;
                        string n = g.moves[m].name; fallMoves[n] = fallMoves.GetValueOrDefault(n) + 1;
                    }
                    wasDown[p] = model.fighters[p].downed;
                    if (current[p].g != g || current[p].m != m)
                    {
                        if (current[p].g != null && current[p].need > 0 && !current[p].hit)
                        {
                            string n = current[p].g.moves[current[p].m].name + "@" + current[p].need + "(left " + current[p].last + ")";
                            missed[n] = missed.GetValueOrDefault(n) + 1;
                        }
                        var props = g.moves[m].properties; int need = 0;
                        if (props != null) for (int k = 0; k + 2 < props.Length; k += 3) if (IsShake(props[k + 1])) { need = (int)(props[k] & 0x3FFF); break; }
                        if (need > 0) entered++;
                        current[p] = (g, m, need, false, 0);
                    }
                    current[p].last = it.Frame;
                }
                if (model.phase != phase && (model.phase == MatchPhase.RoundOver || model.phase == MatchPhase.MatchOver)) rounds++;
                phase = model.phase;
            }
            Console.WriteLine($"{a} vs {b}: {rounds} rounds, {landings} falls; shake moves entered {entered}, shakes raised {raised}");
            Console.WriteLine("   raised by: " + string.Join(", ", raisedBy.OrderByDescending(x => x.Value).Take(6).Select(x => x.Key + "x" + x.Value)));
            Console.WriteLine("   fall moves: " + string.Join(", ", fallMoves.OrderByDescending(x => x.Value).Take(10).Select(x => x.Key + "x" + x.Value)));
            Console.WriteLine("   left early: " + string.Join(", ", missed.OrderByDescending(x => x.Value).Take(8).Select(x => x.Key + "x" + x.Value)));
        }
        return 0;
    }
}
