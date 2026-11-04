using System; using System.Collections.Generic; using System.Linq; using Versus.Remaster;
// CPU matches: distance between the fighters' bodies (feet-to-head lines on the floor) per
// tick, split by state. Reports how often they are closer than 0.35 m (bodies inside each other)
// and the moves involved.
static class Overlap {
  static float SegDist(FighterState p, FighterState q) {
    float best = float.MaxValue;
    for (int i = 0; i <= 20; i++) for (int j = 0; j <= 20; j++) {
      float s = i / 20f, t = j / 20f;
      float ax = p.feetX + (p.headX - p.feetX) * s, az = p.feetZ + (p.headZ - p.feetZ) * s, bx = q.feetX + (q.headX - q.feetX) * t, bz = q.feetZ + (q.headZ - q.feetZ) * t;
      best = Math.Min(best, (float)Math.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz)));
    }
    return best;
  }
  static void Main(string[] a) {
  UnityEngine.Resources.Root = System.IO.Path.Combine(a[0], "Assets/_Project/Resources");
  string[] codes = { "jin", "kaz", "xia", "asa", "ltn", "nsd", "bob", "leo" };
  var count = new Dictionary<string, (int ticks, int close)>(); var moves = new Dictionary<string, int>();
  foreach (var p1 in codes) foreach (var p2 in codes) {
    var g = new[] { CommandGraph.Load(p1), CommandGraph.Load(p2) };
    var cpu = new[] { new CpuPlayer(g[0], 3), new CpuPlayer(g[1], 5) };
    var m = new BattleModel(new MatchRules { health = 170 }, g);
    for (int t = 0; t < 60 * 200 && m.phase != MatchPhase.MatchOver; t++) {
      var c0 = m.phase == MatchPhase.Fight ? cpu[0].Next(m.fighters[0], m.fighters[1]) : PlayerCommand.Neutral;
      var c1 = m.phase == MatchPhase.Fight ? cpu[1].Next(m.fighters[1], m.fighters[0]) : PlayerCommand.Neutral;
      m.Tick(c0, c1);
      if (m.phase != MatchPhase.Fight) continue;
      var f = m.fighters; float d = SegDist(f[0], f[1]);
      string S(FighterState s) => s.downed ? "down" : s.airborne ? "air" : "up";
      var states = new[] { S(f[0]), S(f[1]) }; Array.Sort(states); string key = states[0] + "+" + states[1];
      (int ticks, int close) c = count.TryGetValue(key, out var v) ? v : (0, 0); c.ticks++;
      if (d < .35f) { c.close++; foreach (var s in f) { string n = $"{key} {g[s.moveOwner].moves[s.move].name}"; moves[n] = moves.TryGetValue(n, out int k) ? k + 1 : 1; } }
      count[key] = c;
    }
  }
  foreach (var kv in count.OrderBy(k => k.Key)) Console.WriteLine($"{kv.Key}: {kv.Value.ticks} ticks, torsos closer than 0.35 m {kv.Value.close} ({100.0 * kv.Value.close / kv.Value.ticks:F1}%)");
  foreach (var kv in moves.OrderByDescending(k => k.Value).Take(12)) Console.Write($"{kv.Key}:{kv.Value}  "); Console.WriteLine();
} }
