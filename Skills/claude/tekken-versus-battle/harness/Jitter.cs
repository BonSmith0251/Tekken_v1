using System; using System.Collections.Generic; using Versus.Remaster;
// Measures how much a fighter's floor position and facing reverse direction tick to tick
// (jitter) after a KO, and while a downed fighter is walked over.
static class Jitter { static void Main(string[] a) {
  UnityEngine.Resources.Root = System.IO.Path.Combine(a[0], "Assets/_Project/Resources");
  double totalFlips = 0, totalTurn = 0; int rounds = 0;
  foreach (var pair in new[] { ("jin","xia"), ("xia","jin"), ("kaz","jin") })
  for (int seed = 0; seed < 4; seed++) {
    var g = new[] { CommandGraph.Load(pair.Item1), CommandGraph.Load(pair.Item2) };
    var cpu = new[] { new CpuPlayer(g[0], 3 + seed), new CpuPlayer(g[1], 5 + seed * 7) };
    var m = new BattleModel(new MatchRules { health = 170 }, g);
    MatchPhase last = m.phase; int loser = -1; float px = 0, pz = 0, pdx = 0, pdz = 0; float pdir = 0; int flips = 0; float turned = 0; int ticks = 0;
    for (int t = 0; t < 60 * 300 && m.phase != MatchPhase.MatchOver; t++) {
      var c0 = m.phase == MatchPhase.Fight ? cpu[0].Next(m.fighters[0], m.fighters[1]) : PlayerCommand.Neutral;
      var c1 = m.phase == MatchPhase.Fight ? cpu[1].Next(m.fighters[1], m.fighters[0]) : PlayerCommand.Neutral;
      m.Tick(c0, c1);
      if (last == MatchPhase.Fight && m.phase != MatchPhase.Fight) { loser = m.winner >= 0 ? 1 - m.winner : 0; var f0 = m.fighters[loser]; px = f0.x; pz = f0.z; pdx = pdz = 0; pdir = (float)Math.Atan2(f0.dirZ, f0.dirX); flips = 0; turned = 0; ticks = 0; }
      if (loser >= 0 && m.phase != MatchPhase.Fight && m.phase != MatchPhase.Intro) {
        var f = m.fighters[loser]; float dx = f.x - px, dz = f.z - pz;
        if (dx * pdx + dz * pdz < -1e-6f) flips++;          // movement reversed direction this tick
        float dir = (float)Math.Atan2(f.dirZ, f.dirX); float dd = Math.Abs(dir - pdir); if (dd > Math.PI) dd = (float)(2 * Math.PI - dd); turned += dd;
        px = f.x; pz = f.z; pdx = dx; pdz = dz; pdir = dir; ticks++;
      }
      if (last != MatchPhase.Fight && m.phase == MatchPhase.Intro && loser >= 0) { totalFlips += flips; totalTurn += turned * 180 / Math.PI; rounds++; loser = -1; }
      last = m.phase;
    }
  }
  Console.WriteLine($"round ends measured {rounds}: loser movement reversals {totalFlips / Math.Max(1, rounds):F1} per round end, loser turning {totalTurn / Math.Max(1, rounds):F0} deg per round end");
} }
