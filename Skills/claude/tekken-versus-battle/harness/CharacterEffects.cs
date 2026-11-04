using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Versus.Remaster;

// Character effect dispatch (property 0x802E, Resources/CharacterEffects/<code>/native-dispatch.json).
// 1) Forces every manifest move on an idle model and checks each original row is raised exactly once,
//    on its frame, with its bone value, in both player slots.
// 2) Runs CPU matches against Xiaoyu and counts dispatched rows per round (whiffs included).
// Usage: run.sh CharacterEffects [project]; code from CHARACTER_EFFECTS_CODE (default ltn).
static class CharacterEffects
{
    static BattleModel Model(CommandGraph own, int slot)
    {
        var other = CommandGraph.Load("xia");
        var model = new BattleModel(new MatchRules { health = 170 }, slot == 0 ? new[] { own, other } : new[] { other, own });
        for (int i = 0; i < 300 && model.phase != MatchPhase.Fight; i++) model.Tick(PlayerCommand.Neutral, PlayerCommand.Neutral);
        model.fighters[0].x = -4; model.fighters[1].x = 4;
        return model;
    }
    static int Main(string[] args)
    {
        UnityEngine.Resources.Root = Path.Combine(args[0], "Assets/_Project/Resources");
        string code = Environment.GetEnvironmentVariable("CHARACTER_EFFECTS_CODE") ?? "ltn";
        var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(UnityEngine.Resources.Root, "CharacterEffects", code, "native-dispatch.json")));
        var expected = doc.RootElement.GetProperty("events").EnumerateArray()
            .Select(e => (move: e.GetProperty("moveId").GetInt32(), frame: e.GetProperty("rawFrame").GetInt32(), value: e.GetProperty("propertyValue").GetInt64()))
            .ToList();
        var graph = CommandGraph.Load(code);
        int checkedMoves = 0, matched = 0; var faults = new List<string>();
        for (int slot = 0; slot < 2; slot++)
            foreach (var group in expected.GroupBy(e => e.move))
            {
                var model = Model(graph, slot);
                var seen = new List<(int move, int frame, long value)>();
                model.ActedMoveProperty += e => { if (e.id == 0x802E && e.player == slot && e.graph == graph && e.move == group.Key) seen.Add((e.move, e.frame, e.value)); };
                model.interpreters[slot].Force(group.Key);
                int last = group.Max(e => e.frame) + 3;
                for (int t = 0; t < last; t++) model.Tick(PlayerCommand.Neutral, PlayerCommand.Neutral);
                string after = model.interpreters[slot].ActedGraph.moves[model.interpreters[slot].ActedMove].name;
                var want = group.OrderBy(e => e.frame).ToList();
                var got = seen.OrderBy(e => e.frame).ToList();
                if (want.SequenceEqual(got)) matched += want.Count;
                else faults.Add($"slot {slot} {graph.moves[group.Key].name}: want [{string.Join(" ", want.Select(w => w.frame + ":" + w.value))}] got [{string.Join(" ", got.Select(w => w.frame + ":" + w.value))}] (move frames {graph.moves[group.Key].frames}; in {after} afterwards)");
                checkedMoves++;
            }
        Console.WriteLine($"{code}: {expected.Count} manifest rows in {expected.Select(e => e.move).Distinct().Count()} moves; forced in both slots: {matched}/{expected.Count * 2} rows raised once on their frame");
        foreach (var f in faults.Take(20)) Console.WriteLine("  " + f);

        // CPU matches: how often the effect plays in a real fight, and on which bones.
        int rounds = 0, events = 0; var bones = new Dictionary<long, int>(); var moves = new Dictionary<string, int>();
        for (int seed = 0; seed < 3; seed++)
        {
            int slot = seed % 2;
            var model = Model(graph, slot);
            var cpu = new[] { new CpuPlayer(model.graphs[0], 11 + seed), new CpuPlayer(model.graphs[1], 57 + seed) };
            model.ActedMoveProperty += e =>
            {
                if (e.id != 0x802E || e.player != slot || e.rawFrame != e.frame) return;
                events++; bones[e.value & 0xFFFF] = bones.GetValueOrDefault(e.value & 0xFFFF) + 1;
                string n = e.graph.moves[e.move].name; moves[n] = moves.GetValueOrDefault(n) + 1;
            };
            var phase = model.phase;
            for (int t = 0; t < 60 * 240 && model.phase != MatchPhase.MatchOver; t++)
            {
                model.Tick(cpu[0].Next(model.fighters[0], model.fighters[1]), cpu[1].Next(model.fighters[1], model.fighters[0]));
                if (model.phase == MatchPhase.RoundOver && phase != MatchPhase.RoundOver) rounds++;
                phase = model.phase;
            }
        }
        Console.WriteLine($"CPU vs Xiaoyu, 3 matches: {events} effect rows over {rounds} rounds ({(rounds > 0 ? events / (float)rounds : 0):0.0} per round)");
        Console.WriteLine("  bones: " + string.Join(", ", bones.OrderByDescending(b => b.Value).Select(b => b.Key + "x" + b.Value)));
        Console.WriteLine("  moves: " + string.Join(", ", moves.OrderByDescending(m => m.Value).Take(10).Select(m => m.Key + "x" + m.Value)));
        return faults.Count == 0 ? 0 : 1;
    }
}
