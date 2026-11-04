using System; using System.Collections.Generic; using Versus.Remaster;
static class Getup { static void Main(string[] a) {
  UnityEngine.Resources.Root = System.IO.Path.Combine(a[0], "Assets/_Project/Resources");
  var g = new[] { CommandGraph.Load("jin"), CommandGraph.Load("xia") };
  var cpu = new[] { new CpuPlayer(g[0], 3), new CpuPlayer(g[1], 5) };
  var m = new BattleModel(new MatchRules { health = 170 }, g);
  int downs = 0, getups = 0, longest = 0; var stuck = new int[2]; var after = new Dictionary<string,int>(); var prev = new string[2]; var seq = new Dictionary<string,int>();
  for (int t = 0; t < 60*600 && m.phase != MatchPhase.MatchOver; t++) {
    var c0 = m.phase==MatchPhase.Fight ? cpu[0].Next(m.fighters[0], m.fighters[1]) : PlayerCommand.Neutral;
    var c1 = m.phase==MatchPhase.Fight ? cpu[1].Next(m.fighters[1], m.fighters[0]) : PlayerCommand.Neutral;
    m.Tick(c0, c1);
    for (int i = 0; i < 2; i++) { var f = m.fighters[i]; string name = g[f.moveOwner].moves[f.move].name;
      if (f.downedTicks == 1) downs++;
      if (prev[i] != null && (prev[i] == "dwKAM_00U" || prev[i] == "dwKAM_00D") && f.downedTicks == 0) { getups++; after[name] = after.TryGetValue(name, out int k) ? k + 1 : 1; }
      if (name != prev[i] && (name.StartsWith("dw") || name.StartsWith("Dw") || name.StartsWith("sDw") || name.Contains("DWN") || name.Contains("Uoki") || name.Contains("Aoki") || name.Contains("UToki") || name.Contains("AO"))) { string k2 = name + (f.borrowed ? " (borrowed)" : ""); seq[k2] = seq.TryGetValue(k2, out int q) ? q + 1 : 1; }
      longest = Math.Max(longest, f.downedTicks); prev[i] = name; }
  }
  Console.WriteLine($"Jin vs Xiaoyu CPU: downs {downs}, getups {getups}, longest lie {longest} ticks, rounds {m.round}, phase {m.phase}");
  foreach (var kv in seq) Console.WriteLine($"   state {kv.Key} x{kv.Value}");
  foreach (var kv in after) Console.WriteLine($"   getup -> {kv.Key} x{kv.Value}");
} }
