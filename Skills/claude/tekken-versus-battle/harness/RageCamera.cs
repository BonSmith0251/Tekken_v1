using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Versus.Remaster;

// Rage Art cinematic camera in versus: forces each fighter's Rage Art (the movelist demo's first move)
// at close range in Rage, in both player slots, and reports the 0x8439/0x843A events VersusGame would
// act on (bank flag 0x4000, or no flag = the moveset's own bank; side matching the actor's facing) and whether Resources/FightCameras has
// the track. Usage: run.sh RageCamera [project].
static class RageCamera
{
    static int Main(string[] args)
    {
        string root = args[0];
        UnityEngine.Resources.Root = Path.Combine(root, "Assets/_Project/Resources");
        int faults = 0;
        foreach (string code in new[] { "jin", "kaz", "xia", "asa", "ltn", "nsd", "bob", "leo" })
        {
            var reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "Validation/CommandGraph", code + "-reference.json")));
            var demo = reference.RootElement.GetProperty("demos").EnumerateArray().First(d => d.GetProperty("name").GetString() == "Rage Art");
            var graph = CommandGraph.Load(code);
            // The demo can start from a stance or walk (Xiaoyu's back walk); force its first Rage Art move.
            int first = demo.GetProperty("trace").EnumerateArray().Select(t => t.GetInt32()).First(id => graph.moves[id].name.Contains("RageArts"));
            for (int slot = 0; slot < 2; slot++)
            {
                var own = CommandGraph.Load(code); var other = CommandGraph.Load("xia");
                var model = new BattleModel(new MatchRules { health = 170 }, slot == 0 ? new[] { own, other } : new[] { other, own });
                for (int i = 0; i < 300 && model.phase != MatchPhase.Fight; i++) model.Tick(PlayerCommand.Neutral, PlayerCommand.Neutral);
                model.fighters[0].x = -.6f; model.fighters[1].x = .6f;
                model.fighters[slot].health = 30;
                var events = new List<string>(); var moves = new List<string>();
                bool played = false;
                model.MoveProperty += (player, id, value) =>
                {
                    if (player != slot || (id != 0x8439 && id != 0x843A)) return;
                    // 0x4000|i: track i in the archive; plain i (DLC: Alisa, Lidia): the moveset's own bank, file <code>_m<i>.
                    bool bank = (value & 0xC000) == 0x4000 || (value & 0xC000) == 0, side = (id == 0x8439) == (model.fighters[player].facing > 0);
                    int track = (int)(value & 0x3FFF);
                    string prefix = (value & 0xC000) == 0 ? "m" : "";
                    bool file = File.Exists(Path.Combine(UnityEngine.Resources.Root, "FightCameras", code + "_" + prefix + track + ".bytes"));
                    events.Add($"{(id == 0x8439 ? "L" : "R")}{track}{(bank && side ? (file ? "*" : "!missing") : "")}");
                    if (bank && side && file) played = true;
                };
                model.interpreters[slot].Force(first);
                int last = -1;
                for (int t = 0; t < 400; t++)
                {
                    model.Tick(PlayerCommand.Neutral, PlayerCommand.Neutral);
                    int m = model.fighters[slot].move;
                    if (m != last) { moves.Add(own.moves[m].name); last = m; }
                }
                if (!played) faults++;
                Console.WriteLine($"{code} slot {slot} facing {(slot == 0 ? "+" : "-")}: camera {(played ? "PLAYS" : "none")} | events {string.Join(" ", events)} | {string.Join(" > ", moves.Take(5))}");
            }
        }
        Console.WriteLine("* = VersusGame starts this track; !missing = selected but no FightCameras file");
        return faults == 0 ? 0 : 1;
    }
}
