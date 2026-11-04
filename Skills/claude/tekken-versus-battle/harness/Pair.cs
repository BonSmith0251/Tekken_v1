using System; using System.Collections.Generic; using System.Linq; using Versus.Remaster;
// CPU versus CPU matches for a new pairing: rounds, results, hits per side, distinct moves used and
// the longest single stay in one move (a stuck clip shows up there). Edit the pairs below for a new fighter (last run: Bob, Leo).
static class Pair { static void Main(string[] a) {
  UnityEngine.Resources.Root = System.IO.Path.Combine(a[0], "Assets/_Project/Resources");
  foreach (var pair in new[] { ("bob","leo"), ("leo","nsd"), ("bob","bob") }) {
    var g = new[] { CommandGraph.Load(pair.Item1), CommandGraph.Load(pair.Item2) };
    for (int seed = 0; seed < 3; seed++) {
      var cpu = new[] { new CpuPlayer(g[0], 3 + seed * 11), new CpuPlayer(g[1], 5 + seed * 7) };
      var m = new BattleModel(new MatchRules { health = 170 }, g);
      var hits = new int[2]; var blocked = new int[2]; m.Hit += h => { if (h.blocked) blocked[h.attacker]++; else hits[h.attacker]++; };
      var used = new[] { new HashSet<string>(), new HashSet<string>() };
      var stay = new int[2]; var longest = new (int ticks, string move)[2]; var last = new int[] { -1, -1 };
      var rounds = new List<string>(); MatchPhase prev = m.phase; int t = 0;
      for (; t < 60 * 60 * 6 && m.phase != MatchPhase.MatchOver; t++) {
        var c0 = m.phase == MatchPhase.Fight ? cpu[0].Next(m.fighters[0], m.fighters[1]) : PlayerCommand.Neutral;
        var c1 = m.phase == MatchPhase.Fight ? cpu[1].Next(m.fighters[1], m.fighters[0]) : PlayerCommand.Neutral;
        m.Tick(c0, c1);
        for (int i = 0; i < 2 && m.phase == MatchPhase.Fight; i++) {
          var f = m.fighters[i]; string name = g[f.moveOwner].moves[f.move].name;
          used[i].Add(name);
          if (f.move == last[i]) stay[i]++; else { stay[i] = 0; last[i] = f.move; }
          if (stay[i] > longest[i].ticks) longest[i] = (stay[i], name);
        }
        if (prev == MatchPhase.Fight && m.phase != MatchPhase.Fight) rounds.Add(m.result + (m.winner >= 0 ? " P" + (m.winner + 1) : ""));
        prev = m.phase;
      }
      Console.WriteLine($"{pair.Item1} vs {pair.Item2} seed {seed}: {(m.phase == MatchPhase.MatchOver ? "match over" : "UNFINISHED")} after {t / 60}s, rounds [{string.Join(", ", rounds)}], " +
        $"hits {hits[0]}/{hits[1]}, blocked {blocked[0]}/{blocked[1]}, moves used {used[0].Count}/{used[1].Count}, " +
        $"longest stay P1 {longest[0].ticks}t {longest[0].move}, P2 {longest[1].ticks}t {longest[1].move}");
    }
  } } }
