using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Versus.Remaster.Editor
{
    /// <summary>
    /// Replays the original movelist demos through the runtime CommandInterpreter and compares every
    /// entered move with Tools/CommandGraph/command_reference.py's saved traces. Also runs a headless
    /// CPU-versus-CPU command battle to check the battle model end to end. Writes
    /// Validation/CommandGraph/command-graph-unity.json. Queue with Temp/validate-command-graph.
    /// </summary>
    [InitializeOnLoad]
    public static class CommandGraphValidation
    {
        [Serializable] class Step { public string input; public int frames; }
        [Serializable] class Demo { public int index; public string name, command; public bool setup, rage, passed; public int distance, expected; public Step[] inputs; public int[] trace; }
        [Serializable] class Reference { public int opponent; public Demo[] demos; }

        static CommandGraphValidation() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (!File.Exists("Temp/validate-command-graph") || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete("Temp/validate-command-graph");
            try { Run(); }
            catch (Exception e) { Debug.LogException(e); File.WriteAllText("Validation/CommandGraph/command-graph-unity-error.txt", e.ToString()); }
        }

        static readonly Dictionary<string, int> Directions = new Dictionary<string, int>
        { { "d/b", 1 }, { "d", 2 }, { "d/f", 3 }, { "b", 4 }, { "n", 5 }, { "f", 6 }, { "u/b", 7 }, { "u", 8 }, { "u/f", 9 } };

        static void Parse(string text, out int direction, out int buttons)
        {
            direction = 5; buttons = 0;
            foreach (var part in text.Trim().ToLowerInvariant().Split('+'))
            {
                if (Directions.TryGetValue(part, out int d)) direction = d;
                else if (part.Length == 1 && part[0] >= '1' && part[0] <= '4') buttons |= 1 << (part[0] - '1');
            }
        }

        [MenuItem("Remaster/Validate original command graphs")]
        public static void Run()
        {
            Directory.CreateDirectory("Validation/CommandGraph");
            var lines = new List<string>();
            bool passed = true;
            foreach (var code in new[] { "jin", "kaz", "xia", "asa", "ltn" })
            {
                var graph = CommandGraph.Load(code);
                var reference = JsonUtility.FromJson<Reference>(File.ReadAllText("Validation/CommandGraph/" + code + "-reference.json"));
                int same = 0, plain = 0, plainPassed = 0;
                var mismatches = new List<string>();
                foreach (var demo in reference.demos)
                {
                    var context = new CommandContext { characterId = graph.characterId, opponentId = reference.opponent, rage = demo.rage, distance = demo.distance };
                    var interpreter = new CommandInterpreter(graph, context);
                    for (int i = 0; i < 30; i++) interpreter.Step(5, 0);
                    var trace = new List<int>();
                    int byInput = 0;
                    void Advance(int d, int b)
                    {
                        if (!interpreter.Step(d, b)) return;
                        trace.Add(interpreter.Move);
                        if (interpreter.EnteredByInput) byInput++;
                    }
                    foreach (var s in demo.inputs) { Parse(s.input, out int d, out int b); for (int f = 0; f < s.frames; f++) Advance(d, b); }
                    for (int i = 0; i < 90; i++) Advance(5, 0);
                    bool match = trace.SequenceEqual(demo.trace);
                    if (match) same++;
                    else mismatches.Add(demo.index + " " + demo.name + ": unity " + string.Join(">", trace.Select(m => graph.moves[m].name)) +
                                        " | reference " + string.Join(">", demo.trace.Select(m => graph.moves[m].name)));
                    if (!demo.setup) { plain++; if (byInput == demo.expected) plainPassed++; }
                }
                // Xiaoyu's three "Back towards enemy" entries need the backturned state, not yet tracked.
                // Alisa's Quick Slider and 10 Hit Combo demos press buttons the reference does not consume yet.
                // Katarina's 10 Hit Combo has the same gap.
                int knownGaps = code == "asa" ? 2 : code == "ltn" ? 1 : 0;
                passed &= same == reference.demos.Length && plainPassed >= plain - knownGaps;
                lines.Add("{\"character\":\"" + code + "\",\"demos\":" + reference.demos.Length + ",\"tracesMatchingReference\":" + same +
                          ",\"plainCommandsPassed\":" + plainPassed + ",\"plainCommands\":" + plain +
                          ",\"mismatches\":[" + string.Join(",", mismatches.Select(m => "\"" + m.Replace("\"", "'") + "\"")) + "]}");
            }
            // Headless CPU versus CPU on the command battle model: no exceptions, moves change, hits land.
            var graphs = new[] { CommandGraph.Load("kaz"), CommandGraph.Load("jin") };
            var battle = new BattleModel(new MatchRules { health = VersusGame.TekkenHealth, seconds = 60, roundsToWin = 2 }, graphs);
            var cpu = new[] { new CpuPlayer(graphs[0], 3), new CpuPlayer(graphs[1], 5) };
            int hits = 0, blocks = 0, ticks = 0;
            var movesSeen = new[] { new HashSet<int>(), new HashSet<int>() };
            battle.Hit += h => { if (h.blocked) blocks++; else hits++; };
            for (; ticks < 60 * 120 && battle.phase != MatchPhase.MatchOver; ticks++)
            {
                var c0 = battle.phase == MatchPhase.Fight ? cpu[0].Next(battle.fighters[0], battle.fighters[1]) : PlayerCommand.Neutral;
                var c1 = battle.phase == MatchPhase.Fight ? cpu[1].Next(battle.fighters[1], battle.fighters[0]) : PlayerCommand.Neutral;
                battle.Tick(c0, c1);
                for (int i = 0; i < 2; i++) movesSeen[i].Add(battle.fighters[i].move);
            }
            bool battleOk = hits > 0 && movesSeen[0].Count > 10 && movesSeen[1].Count > 10;
            // Guard rules: P1 repeats a command every 50 frames at close range while P2 holds a direction.
            var guards = new List<string>();
            void Guard(string label, int p1Relative, int p1Buttons, int p2Relative, bool expectBlocks, bool expectHits)
            {
                var m = new BattleModel(new MatchRules { health = VersusGame.TekkenHealth }, graphs);
                m.BeginFight();
                int h = 0, bl = 0;
                m.Hit += e => { if (e.attacker == 0) { if (e.blocked) bl++; else h++; } };
                for (int t = 0; t < 600; t++)
                {
                    var c0 = PlayerCommand.Neutral; var c1 = PlayerCommand.Neutral;
                    c0.stick = BattleModel.Relative(t % 50 < 2 ? p1Relative : 5, m.fighters[0].facing);
                    c0.buttons = t % 50 == 1 ? p1Buttons : 0;
                    c1.stick = BattleModel.Relative(p2Relative, m.fighters[1].facing);
                    m.Tick(c0, c1);
                    m.fighters[0].x = -.55f; m.fighters[1].x = .55f; m.fighters[0].z = m.fighters[1].z = 0;
                    m.fighters[0].health = m.fighters[1].health = VersusGame.TekkenHealth;
                }
                bool ok = (bl > 0) == expectBlocks && (h > 0) == expectHits;
                passed &= ok;
                guards.Add("{\"case\":\"" + label + "\",\"hits\":" + h + ",\"blocks\":" + bl + ",\"passed\":" + ok.ToString().ToLowerInvariant() + "}");
            }
            Guard("1 vs neutral", 5, 1, 5, false, true);
            Guard("1 vs back", 5, 1, 4, true, false);
            Guard("1 vs down (high whiffs)", 5, 1, 2, false, false);
            Guard("d+4 low vs back", 2, 8, 4, false, true);
            Guard("d+4 low vs down-back", 2, 8, 1, true, false);
            Guard("d/f+1 mid vs down-back", 3, 1, 1, false, true);
            Guard("d/f+1 mid vs back", 3, 1, 4, true, false);
            lines.Add("{\"guard\":[" + string.Join(",", guards) + "]}");
            passed &= battleOk;
            lines.Add("{\"cpuBattle\":{\"ticks\":" + ticks + ",\"hits\":" + hits + ",\"blocks\":" + blocks + ",\"rounds\":" + battle.round +
                      ",\"winner\":" + battle.winner + ",\"kazuyaMoves\":" + movesSeen[0].Count + ",\"jinMoves\":" + movesSeen[1].Count +
                      ",\"kazuyaHealth\":" + battle.fighters[0].health + ",\"jinHealth\":" + battle.fighters[1].health + "}}");
            File.WriteAllText("Validation/CommandGraph/command-graph-unity.json", "{\"passed\":" + passed.ToString().ToLowerInvariant() + ",\"results\":[\n" + string.Join(",\n", lines) + "\n]}");
            Debug.Log("Command graph validation " + (passed ? "passed" : "FAILED") + ": Validation/CommandGraph/command-graph-unity.json");
        }
    }
}
