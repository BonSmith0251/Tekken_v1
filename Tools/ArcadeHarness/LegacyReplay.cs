using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Versus.Remaster;

static class LegacyReplay
{
    public static readonly string[] Codes = { "jin", "kaz", "xia", "asa", "ltn", "nsd", "bob", "leo" };
    public static string[] Capture(string project)
    {
        UnityEngine.Resources.Root = Path.Combine(project, "Assets/_Project/Resources");
        var graphs = new CommandGraph[Codes.Length];
        for (int i = 0; i < graphs.Length; i++) graphs[i] = CommandGraph.Load(Codes[i]);
        var traces = new List<string>();
        for (int i = 0; i < graphs.Length; i++)
            foreach (int seed in new[] { 31, 137 })
            {
                var pair = new[] { graphs[i], graphs[(i + 1) % graphs.Length] };
                var battle = new BattleModel(new MatchRules { health = 170 }, pair);
                var cpu = new[] { new CpuPlayer(pair[0], seed), new CpuPlayer(pair[1], seed + 17) };
                ulong hash = 14695981039346656037UL;
                void Add(int value) { unchecked { hash ^= (uint)value; hash *= 1099511628211UL; } }
                for (int tick = 0; tick < 6000; tick++)
                {
                    var one = battle.phase == MatchPhase.Fight ? cpu[0].Next(battle.fighters[0], battle.fighters[1]) : PlayerCommand.Neutral;
                    var two = battle.phase == MatchPhase.Fight ? cpu[1].Next(battle.fighters[1], battle.fighters[0]) : PlayerCommand.Neutral;
                    Add(one.stick); Add(one.buttons); Add(two.stick); Add(two.buttons);
                    battle.Tick(one, two); Add((int)battle.phase); Add(battle.round); Add(battle.winner); Add(battle.ticksLeft);
                    foreach (var fighter in battle.fighters)
                    { Add(fighter.health); Add(fighter.wins); Add(fighter.move); Add(fighter.moveFrame); Add(BitConverter.SingleToInt32Bits(fighter.x)); Add(BitConverter.SingleToInt32Bits(fighter.z)); }
                    if (battle.phase == MatchPhase.MatchOver) { battle.Restart(); cpu[0].Clear(); cpu[1].Clear(); }
                }
                traces.Add(Codes[i] + "/" + Codes[(i + 1) % graphs.Length] + "/" + seed + ":" + hash.ToString("x16"));
            }
        return traces.ToArray();
    }
#if LEGACY_CAPTURE
    static int Main(string[] args)
    {
        var traces = Capture(args[0]);
        File.WriteAllText(args[1], JsonSerializer.Serialize(traces, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Captured " + traces.Length + " legacy traces from pre-M2 CPU source (96,000 ticks).");
        return 0;
    }
#endif
}
