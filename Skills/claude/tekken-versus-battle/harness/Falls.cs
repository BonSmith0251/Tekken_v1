using System; using System.Collections.Generic; using Versus.Remaster;
static class Falls { static void Main(string[] a) {
  UnityEngine.Resources.Root = System.IO.Path.Combine(a[0], "Assets/_Project/Resources");
  var g = new[] { CommandGraph.Load("jin"), CommandGraph.Load("xia") };
  var cpu = new[] { new CpuPlayer(g[0], 3), new CpuPlayer(g[1], 5) };
  var m = new BattleModel(new MatchRules { health = 170 }, g);
  var trail = new List<string>[2] { new List<string>(), new List<string>() }; var prev = new string[2]; int shown = 0; var startT = new int[2];
  for (int t = 0; t < 60*600 && m.phase != MatchPhase.MatchOver && shown < 12; t++) {
    var c0 = m.phase==MatchPhase.Fight ? cpu[0].Next(m.fighters[0], m.fighters[1]) : PlayerCommand.Neutral;
    var c1 = m.phase==MatchPhase.Fight ? cpu[1].Next(m.fighters[1], m.fighters[0]) : PlayerCommand.Neutral;
    m.Tick(c0, c1);
    for (int i = 0; i < 2; i++) { var f = m.fighters[i]; string name = g[f.moveOwner].moves[f.move].name + (f.borrowed ? "*" : "");
      if (name == prev[i]) continue; prev[i] = name;
      bool down = name.StartsWith("sDw") || name.StartsWith("dw") || name.StartsWith("Dw");
      if (down && trail[i].Count == 0) startT[i] = t;
      if (trail[i].Count > 0 || down) trail[i].Add(name + "@" + (t - startT[i]));
      bool standing = name.StartsWith("Jz_sKAM") || name.StartsWith("Fu_sKAM");
      if (trail[i].Count > 0 && (standing || trail[i].Count > 14)) { Console.WriteLine($"P{i+1}: " + string.Join(" > ", trail[i])); trail[i].Clear(); shown++; }
    }
  }
} }
