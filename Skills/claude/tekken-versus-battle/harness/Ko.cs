using System; using System.Collections.Generic; using Versus.Remaster;
static class Ko { static void Main(string[] a) {
  UnityEngine.Resources.Root = System.IO.Path.Combine(a[0], "Assets/_Project/Resources");
  foreach (var pair in new[] { ("jin","xia"), ("xia","jin"), ("kaz","jin") }) {
    var g = new[] { CommandGraph.Load(pair.Item1), CommandGraph.Load(pair.Item2) };
    var cpu = new[] { new CpuPlayer(g[0], 3), new CpuPlayer(g[1], 5) };
    var m = new BattleModel(new MatchRules { health = 170 }, g);
    MatchPhase last = m.phase; int koCount = 0; var trail = new List<string>(); int loser = -1; string prev = null; int since = 0;
    for (int t = 0; t < 60*900 && koCount < 3; t++) {
      var c0 = m.phase==MatchPhase.Fight ? cpu[0].Next(m.fighters[0], m.fighters[1]) : PlayerCommand.Neutral;
      var c1 = m.phase==MatchPhase.Fight ? cpu[1].Next(m.fighters[1], m.fighters[0]) : PlayerCommand.Neutral;
      m.Tick(c0, c1);
      if (last == MatchPhase.Fight && m.phase != MatchPhase.Fight) { loser = m.winner >= 0 ? 1 - m.winner : 0; trail.Clear(); prev = null; since = 0; }
      if (loser >= 0) {
        var f = m.fighters[loser]; string n = g[f.moveOwner].moves[f.move].name + (f.borrowed ? "*" : "");
        if (n != prev) { trail.Add(n + "@" + since); prev = n; }
        since++;
        if (m.phase == MatchPhase.Intro || m.phase == MatchPhase.MatchOver && since > 400) { Console.WriteLine($"{pair.Item1} vs {pair.Item2} KO {++koCount} ({m.result}, loser P{loser+1}, hp {m.fighters[loser].health}): " + string.Join(" > ", trail)); loser = -1; if (m.phase == MatchPhase.MatchOver) break; }
      }
      last = m.phase;
    }
  } } }
