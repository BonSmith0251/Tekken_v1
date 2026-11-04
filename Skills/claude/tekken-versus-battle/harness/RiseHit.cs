using System; using System.Collections.Generic; using System.Linq; using Versus.Remaster;
// Katarina vs Jin (and all pairs with RISEALL): hits that land while the defender is getting up
// (rising from the floor). Prints the defender's move chain (owner graph, state) for 120 ticks.
static class RiseHit { static void Main(string[] a) {
  UnityEngine.Resources.Root = System.IO.Path.Combine(a[0], "Assets/_Project/Resources");
  string[] codes = { "jin", "kaz", "xia", "asa", "ltn", "nsd", "bob", "leo" };
  var pairs = Environment.GetEnvironmentVariable("RISEALL") != null ? (from x in codes from y in codes select (x, y)).ToList() : new List<(string, string)> { ("jin", "ltn"), ("ltn", "jin") };
  var kinds = new Dictionary<string, int>(); int shown = 0;
  foreach (var (p1, p2) in pairs) for (int seed = 0; seed < 6; seed++) {
    var g = new[] { CommandGraph.Load(p1), CommandGraph.Load(p2) };
    var cpu = new[] { new CpuPlayer(g[0], 3 + seed), new CpuPlayer(g[1], 5 + 7 * seed) };
    var m = new BattleModel(new MatchRules { health = 170 }, g);
    var prev = new string[2]; var prevDown = new bool[2]; var risingFor = new int[2];
    var trail = new List<string>[2]; var left = new int[2]; var head = new string[2];
    m.Hit += h => { if (h.blocked) return; int i = h.defender;
      if (risingFor[i] > 0 && risingFor[i] < 60) { trail[i] = new List<string>(); left[i] = 120; var at = m.fighters[h.attacker];
        head[i] = $"{p1} vs {p2} seed {seed}: P{i + 1} hit by {g[at.moveOwner].moves[at.move].name} while {prev[i]} (rising {risingFor[i]} ticks)"; } };
    for (int t = 0; t < 60 * 200 && m.phase != MatchPhase.MatchOver; t++) {
      for (int i = 0; i < 2; i++) { var f = m.fighters[i]; prev[i] = g[f.moveOwner].moves[f.move].name; }
      var c0 = m.phase == MatchPhase.Fight ? cpu[0].Next(m.fighters[0], m.fighters[1]) : PlayerCommand.Neutral;
      var c1 = m.phase == MatchPhase.Fight ? cpu[1].Next(m.fighters[1], m.fighters[0]) : PlayerCommand.Neutral;
      m.Tick(c0, c1);
      for (int i = 0; i < 2; i++) {
        var f = m.fighters[i];
        if (f.downed) risingFor[i] = 1; else if (risingFor[i] > 0) risingFor[i]++;
        if (risingFor[i] > 0 && !f.downed && (f.move == g[i].stance)) risingFor[i] = 0;
        if (trail[i] == null) continue;
        string s = g[f.moveOwner].moves[f.move].name + (f.borrowed ? "*" : "") + (f.downed ? "_D" : f.airborne ? "_A" : "_U");
        if (trail[i].Count == 0 || trail[i][^1].Split('@')[0] != s) trail[i].Add(s + "@" + (120 - left[i]));
        if (--left[i] <= 0) {
          string chain = string.Join(" > ", trail[i]);
          string key = string.Join(">", trail[i].Select(x => x.Split('@')[0]).Take(6)); kinds[key] = kinds.TryGetValue(key, out int c) ? c + 1 : 1;
          if (shown++ < 25) Console.WriteLine(head[i] + "\n   " + chain);
          trail[i] = null;
        }
      }
    }
  }
  Console.WriteLine("chains:"); foreach (var kv in kinds.OrderByDescending(p => p.Value).Take(15)) Console.WriteLine($"  {kv.Value}x {kv.Key}");
} }
