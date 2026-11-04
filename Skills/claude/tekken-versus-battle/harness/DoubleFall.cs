using System; using System.Collections.Generic; using System.Linq; using Versus.Remaster;
// CPU matches: after each hit, follows the defender until the next hit or 150 ticks and flags a
// "double fall": the body leaves the floor or falls, is upright on its feet for >= 4 ticks, then
// goes airborne or down again without being hit. Lists the move chains.
static class DoubleFall { static void Main(string[] a) {
  UnityEngine.Resources.Root = System.IO.Path.Combine(a[0], "Assets/_Project/Resources");
  string[] codes = { "jin", "kaz", "xia", "asa", "ltn", "nsd", "bob", "leo" };
  int follows = 0, bad = 0; var kinds = new Dictionary<string, int>(); var ex = new List<string>();
  foreach (var p1 in codes) foreach (var p2 in codes) for (int seed = 0; seed < 2; seed++) {
    var g = new[] { CommandGraph.Load(p1), CommandGraph.Load(p2) };
    var cpu = new[] { new CpuPlayer(g[0], 3 + seed), new CpuPlayer(g[1], 5 + 7 * seed) };
    var m = new BattleModel(new MatchRules { health = 170 }, g);
    var trail = new List<(string n, char s, int at)>[] { null, null }; var age = new int[2]; var hitInfo = new string[2];
    void Close(int i) {
      var t = trail[i]; if (t == null) return; trail[i] = null; follows++;
      for (int k = 1; k + 1 < t.Count; k++) {
        int len = t[k + 1].at - t[k].at;
        if (t[k].s == 'U' && len >= 4 && t.Take(k).Any(x => x.s != 'U') && t.Skip(k + 1).Any(x => x.s != 'U')) {
          bad++; string key = string.Join(">", t.Select(x => x.n + "_" + x.s)); kinds[key] = kinds.TryGetValue(key, out int c) ? c + 1 : 1;
          if (ex.Count < 40 && (Environment.GetEnvironmentVariable("RISING") == null || System.Text.RegularExpressions.Regex.IsMatch(hitInfo[i], "while (Co_|cr2st|cKAM|dw|Fu_cKAM|Fu_cr2st)"))) ex.Add($"{p1} vs {p2}: {hitInfo[i]} | " + string.Join(" > ", t.Select(x => $"{x.n}[{x.s}]@{x.at}"))); break;
        }
      }
    }
    m.Hit += h => { if (h.blocked) return; Close(h.defender); var d = m.fighters[h.defender];
      hitInfo[h.defender] = $"hit while {g[d.moveOwner].moves[d.move].name}{(d.downed ? " down" : d.airborne ? " air" : "")}"; trail[h.defender] = new List<(string, char, int)>(); age[h.defender] = 0; };
    for (int t = 0; t < 60 * 200 && m.phase != MatchPhase.MatchOver; t++) {
      var c0 = m.phase == MatchPhase.Fight ? cpu[0].Next(m.fighters[0], m.fighters[1]) : PlayerCommand.Neutral;
      var c1 = m.phase == MatchPhase.Fight ? cpu[1].Next(m.fighters[1], m.fighters[0]) : PlayerCommand.Neutral;
      m.Tick(c0, c1);
      for (int i = 0; i < 2; i++) {
        if (trail[i] == null) continue;
        var f = m.fighters[i]; string n = g[f.moveOwner].moves[f.move].name; char s = f.downed ? 'D' : f.airborne ? 'A' : 'U';
        var tr = trail[i]; if (tr.Count == 0 || tr[^1].n != n || tr[^1].s != s) tr.Add((n, s, age[i]));
        if (++age[i] > 150 || m.phase != MatchPhase.Fight) Close(i);
      }
    }
  }
  Console.WriteLine($"hits followed {follows}: upright in between two falls without a hit {bad}");
  foreach (var kv in kinds.OrderByDescending(p => p.Value).Take(12)) Console.WriteLine($"  {kv.Value}x {kv.Key}");
  foreach (var e in ex) Console.WriteLine(e);
} }
