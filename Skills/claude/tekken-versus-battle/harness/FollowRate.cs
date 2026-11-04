using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Versus.Remaster;

// Original fight-camera follow rate (0x84CC camera requests, Tools/CameraEffects/export_follow_rates.py) in CPU
// matches: how often the old remaster "launch zoom" fired (every 0x84CC), and how long the original rate is off
// its base, by request and by the move that set it. Usage: run.sh FollowRate [project].
static class FollowRate
{
    static int Main(string[] args)
    {
        UnityEngine.Resources.Root = Path.Combine(args[0], "Assets/_Project/Resources");
        // Values from catalog.json "followRate" (base 0.1, +0.003 / -0.002 per frame).
        var scales = new Dictionary<int, float> { [18] = 1, [19] = 1, [11] = 3, [12] = 2, [13] = 1.5f, [14] = .5f, [15] = .25f };
        const float Base = .1f, Rise = .003f, Fall = .002f;
        foreach (var (a, b) in new[] { ("jin", "xia"), ("kaz", "ltn"), ("asa", "jin"), ("kaz", "xia") })
        {
            var graphs = new[] { CommandGraph.Load(a), CommandGraph.Load(b) };
            var model = new BattleModel(new MatchRules { health = 170 }, graphs);
            var cpu = new[] { new CpuPlayer(graphs[0], 7), new CpuPlayer(graphs[1], 19) };
            var request = new int[2]; float rate = Base;
            int oldZooms = 0, rounds = 0, ticks = 0, offBase = 0; float minRate = Base, maxRate = Base;
            var byRequest = new Dictionary<int, int>(); var setters = new Dictionary<string, int>();
            model.MoveProperty += (p, id, v) =>
            {
                if (id != 0x84CC) return;
                oldZooms++; request[p] = (int)(v & 0xFFFF);
                if (scales.ContainsKey(request[p]) && scales[request[p]] != 1)
                {
                    var it = model.interpreters[p]; string n = it.ActedGraph.moves[it.ActedMove].name + "=" + request[p];
                    setters[n] = setters.GetValueOrDefault(n) + 1;
                }
            };
            var phase = model.phase;
            for (int t = 0; t < 60 * 300 && model.phase != MatchPhase.MatchOver; t++)
            {
                var c0 = model.phase == MatchPhase.Fight ? cpu[0].Next(model.fighters[0], model.fighters[1]) : PlayerCommand.Neutral;
                var c1 = model.phase == MatchPhase.Fight ? cpu[1].Next(model.fighters[1], model.fighters[0]) : PlayerCommand.Neutral;
                model.Tick(c0, c1);
                float best = 0;
                foreach (int r in request) if (scales.TryGetValue(r, out float s)) best = Math.Max(best, Base * s);
                float target = best < .001f ? Base : best;
                rate = rate < target ? rate + Rise : rate > target ? rate - Fall : rate;
                ticks++; if (Math.Abs(rate - Base) > .005f) { offBase++; int key = Math.Abs(target - Base) > 1e-4f ? (int)Math.Round(target / Base * 100) : 100; byRequest[key] = byRequest.GetValueOrDefault(key) + 1; }
                minRate = Math.Min(minRate, rate); maxRate = Math.Max(maxRate, rate);
                if (model.phase != phase && (model.phase == MatchPhase.RoundOver || model.phase == MatchPhase.MatchOver)) { rounds++; request[0] = request[1] = 0; rate = Base; }
                phase = model.phase;
            }
            Console.WriteLine($"{a} vs {b}: {rounds} rounds; old launch zoom fired {oldZooms}x ({oldZooms / (float)Math.Max(1, rounds):0.0}/round)");
            Console.WriteLine($"   original rate off base (>0.005) {offBase} of {ticks} ticks ({100f * offBase / ticks:0.0}%), range {minRate:0.000}-{maxRate:0.000}; by target %: " +
                string.Join(" ", byRequest.OrderBy(x => x.Key).Select(x => x.Key + ":" + x.Value)));
            Console.WriteLine("   set by: " + string.Join(", ", setters.OrderByDescending(x => x.Value).Take(8).Select(x => x.Key + "x" + x.Value)));
        }
        return 0;
    }
}
