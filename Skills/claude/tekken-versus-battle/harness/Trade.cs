using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Versus.Remaster;

// Original trade slow-down (catalog.json "tradeCamera", hit resolution 0x140713DD0) in CPU matches: ticks on which both
// fighters take a clean hit, how many reach the damage threshold (17) and so play the 10% / 24-frame slow-down, shake 5
// and held view, and the move pairs that traded. Usage: run.sh Trade [project].
static class Trade
{
    static int Main(string[] args)
    {
        UnityEngine.Resources.Root = Path.Combine(args[0], "Assets/_Project/Resources");
        const int Threshold = 17;
        int allTrades = 0, allEvents = 0, allRounds = 0;
        // Five matches per pairing (seeds base + 31 k).
        foreach (var (a, b, seed) in new[] { ("jin", "xia", 7), ("kaz", "ltn", 11), ("asa", "jin", 3), ("kaz", "xia", 5), ("ltn", "asa", 9), ("jin", "kaz", 13) }
            .SelectMany(p => Enumerable.Range(0, 5).Select(k => (p.Item1, p.Item2, p.Item3 + 31 * k))))
        {
            var graphs = new[] { CommandGraph.Load(a), CommandGraph.Load(b) };
            var model = new BattleModel(new MatchRules { health = 170 }, graphs);
            var cpu = new[] { new CpuPlayer(graphs[0], seed), new CpuPlayer(graphs[1], seed + 12) };
            var struck = new int[] { -1, -1 }; var attackerMove = new string[2];
            int trades = 0, events = 0, rounds = 0, koTrades = 0; var pairs = new Dictionary<string, int>();
            model.Hit += h =>
            {
                if (h.blocked) return;
                struck[h.defender] = Math.Max(struck[h.defender], h.damage);
                var it = model.interpreters[h.attacker]; attackerMove[h.attacker] = it.ActedGraph.moves[it.ActedMove].name + "(" + h.damage + ")";
            };
            var phase = model.phase;
            for (int t = 0; t < 60 * 400 && model.phase != MatchPhase.MatchOver; t++)
            {
                var c0 = model.phase == MatchPhase.Fight ? cpu[0].Next(model.fighters[0], model.fighters[1]) : PlayerCommand.Neutral;
                var c1 = model.phase == MatchPhase.Fight ? cpu[1].Next(model.fighters[1], model.fighters[0]) : PlayerCommand.Neutral;
                model.Tick(c0, c1);
                if (struck[0] > 0 && struck[1] > 0)
                {
                    trades++;
                    if (model.phase != MatchPhase.Fight) koTrades++;
                    else if (Math.Max(struck[0], struck[1]) >= Threshold) { events++; string k = attackerMove[0] + " / " + attackerMove[1]; pairs[k] = pairs.GetValueOrDefault(k) + 1; }
                }
                struck[0] = struck[1] = -1;
                if (phase == MatchPhase.Fight && model.phase != MatchPhase.Fight) rounds++;
                phase = model.phase;
            }
            allTrades += trades; allEvents += events; allRounds += rounds;
            Console.WriteLine($"{a} vs {b} (seed {seed}): {rounds} rounds, {trades} trades ({koTrades} ending the round), {events} slow-downs (>= {Threshold} damage)");
            if (pairs.Count > 0) Console.WriteLine("   " + string.Join(", ", pairs.OrderByDescending(x => x.Value).Take(5).Select(x => x.Key + " x" + x.Value)));
        }
        Console.WriteLine($"total: {allRounds} rounds, {allTrades} trades, {allEvents} trade slow-downs");
        return 0;
    }
}
