using System; using System.Collections.Generic; using System.Linq; using Versus.Remaster;
// The HUD holds a fighter's red damage while that fighter is reacting to a hit (BattleHud.Step, standing in
// for the native StopGaugeAnim/StartGaugeAnim), then drains it over 24 frames. This reports how long the
// red would hold after the last hit of each damage sequence, for comparison with the reference capture
// (a single hit held ~28 frames in TEKKEN 7 2026-10-23 08-04-08.mp4 at 75.30 s).
static class HudTrail { static void Main(string[] a) {
  UnityEngine.Resources.Root = System.IO.Path.Combine(a[0], "Assets/_Project/Resources");
  var holds = new List<int>(); var combos = new List<int>(); var stuck = 0;
  foreach (var pair in new[] { ("jin","xia"), ("kaz","jin"), ("asa","ltn"), ("xia","asa") }) {
    var g = new[] { CommandGraph.Load(pair.Item1), CommandGraph.Load(pair.Item2) };
    var cpu = new[] { new CpuPlayer(g[0], 3), new CpuPlayer(g[1], 5) };
    var m = new BattleModel(new MatchRules { health = 170 }, g);
    var lastHit = new[] { -1, -1 }; var hits = new int[2];
    m.Hit += h => { if (!h.blocked && h.damage > 0) { lastHit[h.defender] = tick; hits[h.defender]++; } };
    int matches = 0;
    for (tick = 0; tick < 60 * 900 && matches < 2; tick++) {
      var c0 = m.phase == MatchPhase.Fight ? cpu[0].Next(m.fighters[0], m.fighters[1]) : PlayerCommand.Neutral;
      var c1 = m.phase == MatchPhase.Fight ? cpu[1].Next(m.fighters[1], m.fighters[0]) : PlayerCommand.Neutral;
      m.Tick(c0, c1);
      for (int i = 0; i < 2; i++) {
        var f = m.fighters[i];
        if (lastHit[i] >= 0 && !(f.reacting && f.health > 0)) { holds.Add(tick - lastHit[i]); combos.Add(hits[i]); lastHit[i] = -1; hits[i] = 0; }
        else if (lastHit[i] >= 0 && tick - lastHit[i] > 600) { stuck++; lastHit[i] = -1; hits[i] = 0; }
      }
      if (m.phase == MatchPhase.MatchOver) { matches++; for (int k = 0; k < 300; k++) m.Tick(PlayerCommand.Neutral, PlayerCommand.Neutral); m.Restart(); }
    }
  }
  holds.Sort(); int P(int p) => holds[Math.Min(holds.Count - 1, holds.Count * p / 100)];
  Console.WriteLine($"damage sequences {holds.Count}: hold after last hit p10 {P(10)} p50 {P(50)} p90 {P(90)} max {holds.Last()} frames; held > 10 s: {stuck}");
  var single = holds.Where((h, i) => combos[i] == 1).ToList(); single.Sort();
  if (single.Count > 0) Console.WriteLine($"single hits {single.Count}: median hold {single[single.Count / 2]} frames (reference ~28)");
  Console.WriteLine($"multi-hit sequences {combos.Count(c => c > 1)} (red holds across the combo, then drains)");
} static int tick; }
