using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Versus.Remaster;

// Original KO presentation (catalog.json "koCamera", Tools/CameraEffects/export_ko_camera.py) in CPU matches: the KO
// schedule VersusGame plays (near-freeze, KO camera weights, victory start), how many KOs end a round, and how many are
// Rage Drive KOs (0x801B active on the winner: 150-frame freeze; the original's orbiting camera is not wired).
// Usage: run.sh KoCamera [project].
static class KoCamera
{
    static int Main(string[] args)
    {
        UnityEngine.Resources.Root = Path.Combine(args[0], "Assets/_Project/Resources");
        var ko = JsonDocument.Parse(File.ReadAllText(Path.Combine(args[0], "Assets/_Project/Resources/CameraEffects/catalog.json")))
            .RootElement.GetProperty("koCamera");
        int speed = ko.GetProperty("slowSpeedPercent").GetInt32(), slow = ko.GetProperty("slowFrames").GetInt32();
        int driveSlow = ko.GetProperty("rageDriveSlowFrames").GetInt32(), blendIn = ko.GetProperty("blendInFrames").GetInt32();
        int hold = ko.GetProperty("holdFrames").GetInt32(), blendOut = ko.GetProperty("blendOutFrames").GetInt32();
        float fov = ko.GetProperty("horizontalFov").GetSingle();
        int drive = ko.GetProperty("rageDriveProperty").GetInt32();
        float Weight(float f) => f <= 0 || f >= blendIn + hold + blendOut ? 0 : f < blendIn ? f / blendIn : f < blendIn + hold ? 1 : 1 - (f - blendIn - hold) / blendOut;
        Console.WriteLine($"KO: speed {speed}% for {slow} frames ({slow / 60f:0.00} s; {driveSlow} after a Rage Drive), " +
            $"fighters advance {slow * speed / 100f:0.0} ticks in it; camera FOV 65 -> {fov} (x{Math.Tan(fov * Math.PI / 360) / Math.Tan(65 * Math.PI / 360):0.00} size)");
        Console.WriteLine("   camera weight by frame: " + string.Join(" ", new[] { 1, 3, 5, 50, 95, 100, 105, 110, 115 }.Select(f => $"{f}:{Weight(f):0.00}")));
        Console.WriteLine($"   victory take at {slow / 60f + 1.2f:0.00} s ({driveSlow / 60f + 1.2f:0.00} s after a Rage Drive); was 2.40 s after a 30% slow motion");
        int totalKos = 0, totalDrive = 0;
        foreach (var (a, b, seed) in new[] { ("jin", "xia", 7), ("kaz", "ltn", 11), ("asa", "jin", 3), ("kaz", "xia", 5), ("ltn", "asa", 9), ("jin", "kaz", 13) })
        {
            var graphs = new[] { CommandGraph.Load(a), CommandGraph.Load(b) };
            var model = new BattleModel(new MatchRules { health = 170 }, graphs);
            var cpu = new[] { new CpuPlayer(graphs[0], seed), new CpuPlayer(graphs[1], seed + 12) };
            var rageDrive = new bool[2]; int kos = 0, driveKos = 0, timeUps = 0, driveMoves = 0;
            var finishers = new Dictionary<string, int>();
            model.MoveProperty += (p, id, v) => { if (id == drive) { rageDrive[p] = v != 0; if (v != 0) driveMoves++; } };
            var phase = model.phase;
            for (int t = 0; t < 60 * 400 && model.phase != MatchPhase.MatchOver; t++)
            {
                var c0 = model.phase == MatchPhase.Fight ? cpu[0].Next(model.fighters[0], model.fighters[1]) : PlayerCommand.Neutral;
                var c1 = model.phase == MatchPhase.Fight ? cpu[1].Next(model.fighters[1], model.fighters[0]) : PlayerCommand.Neutral;
                model.Tick(c0, c1);
                if (phase == MatchPhase.Fight && model.phase != MatchPhase.Fight)
                {
                    if (model.result == "TIME UP") timeUps++;
                    else
                    {
                        kos++;
                        if (model.winner >= 0 && rageDrive[model.winner]) driveKos++;
                        if (model.winner >= 0)
                        {
                            var it = model.interpreters[model.winner]; string n = it.ActedGraph.moves[it.ActedMove].name;
                            finishers[n] = finishers.GetValueOrDefault(n) + 1;
                        }
                    }
                    rageDrive[0] = rageDrive[1] = false;
                }
                phase = model.phase;
            }
            totalKos += kos; totalDrive += driveKos;
            Console.WriteLine($"{a} vs {b}: {kos} KOs ({driveKos} by Rage Drive), {timeUps} time-ups; Rage Drive moves started {driveMoves}");
            Console.WriteLine("   finishing moves: " + string.Join(", ", finishers.OrderByDescending(x => x.Value).Take(6).Select(x => x.Key + "x" + x.Value)));
        }
        Console.WriteLine($"total: {totalKos} KOs, {totalDrive} Rage Drive KOs");
        return 0;
    }
}
