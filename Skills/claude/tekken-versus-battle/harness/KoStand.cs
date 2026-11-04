using System; using System.Collections.Generic; using System.Linq; using Versus.Remaster;
// After each KO, follows the loser's moves until the next round (or 400 ticks into MatchOver) and
// flags "stands then falls": the loser is upright (not downed, not airborne) for a stretch after
// having been down or after the KO, then goes down again.
static class KoStand { static void Main(string[] a) {
  UnityEngine.Resources.Root = System.IO.Path.Combine(a[0], "Assets/_Project/Resources");
  string[] codes = { "jin", "kaz", "xia", "asa", "ltn", "nsd", "bob", "leo" };
  int kos = 0, bad = 0; var examples = new List<string>();
  foreach (var p1 in codes) foreach (var p2 in codes)
  for (int seed = 0; seed < 2; seed++) {
    var g = new[] { CommandGraph.Load(p1), CommandGraph.Load(p2) };
    var cpu = new[] { new CpuPlayer(g[0], 3 + seed), new CpuPlayer(g[1], 5 + 7 * seed) };
    var m = new BattleModel(new MatchRules { health = 170 }, g);
    MatchPhase last = m.phase; int loser = -1, since = 0; var trail = new List<(string n, bool up, int at)>();
    for (int t = 0; t < 60 * 400; t++) {
      var c0 = m.phase == MatchPhase.Fight ? cpu[0].Next(m.fighters[0], m.fighters[1]) : PlayerCommand.Neutral;
      var c1 = m.phase == MatchPhase.Fight ? cpu[1].Next(m.fighters[1], m.fighters[0]) : PlayerCommand.Neutral;
      m.Tick(c0, c1);
      if (last == MatchPhase.Fight && m.phase != MatchPhase.Fight && m.result != "TIME UP") { loser = m.winner >= 0 ? 1 - m.winner : 0; trail.Clear(); since = 0; }
      if (loser >= 0) {
        var f = m.fighters[loser]; string n = g[f.moveOwner].moves[f.move].name + (f.borrowed ? "*" : "");
        bool up = !f.downed && !f.airborne;
        if (trail.Count == 0 || trail[^1].n != n || trail[^1].up != up) trail.Add((n, up, since));
        since++;
        bool end = m.phase == MatchPhase.Intro || (m.phase == MatchPhase.MatchOver && since > 400);
        if (end) {
          kos++;
          // upright stretch of >= 8 ticks that comes after a non-upright entry and is followed by a non-upright one
          bool flagged = false;
          for (int i = 1; i + 1 < trail.Count; i++) {
            int len = trail[i + 1].at - trail[i].at;
            // A one-tick airborne flag on the KO tick itself (the immediate collapse starting) is not a fall.
            if (trail[i].up && len >= 8 && Enumerable.Range(0, i).Any(j => !trail[j].up && trail[j + 1].at - trail[j].at >= 2) && trail.Skip(i + 1).Any(x => !x.up)) flagged = true;
          }
          if (flagged) { bad++; if (examples.Count < 12) examples.Add($"{p1} vs {p2} loser P{loser + 1}: " + string.Join(" > ", trail.Select(x => $"{x.n}{(x.up ? "" : "_")}@{x.at}"))); }
          loser = -1;
          if (m.phase == MatchPhase.MatchOver) break;
        }
      }
      last = m.phase;
    }
  }
  Console.WriteLine($"KOs {kos}: loser stands then falls again in {bad}");
  foreach (var e in examples) Console.WriteLine(e);
} }
