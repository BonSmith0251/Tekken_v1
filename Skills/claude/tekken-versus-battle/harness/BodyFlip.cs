using System; using System.Collections.Generic; using System.Linq; using Versus.Remaster;
// CPU matches: flags ticks where a body's floor line (feet -> head, from the clips' body tracks)
// turns more than 100 deg or its head moves more than 0.6 m in one tick without a hit landing:
// a pose snap between two moves (e.g. a tech roll whose clip starts lying the other way round).
static class BodyFlip { static void Main(string[] a) {
  UnityEngine.Resources.Root = System.IO.Path.Combine(a[0], "Assets/_Project/Resources");
  string[] codes = { "jin", "kaz", "xia", "asa", "ltn", "nsd", "bob", "leo" };
  int ticks = 0, flips = 0, downHits = 0, downFlips = 0; var downKinds = new Dictionary<string, int>(); var kinds = new Dictionary<string, int>();
  foreach (var p1 in codes) foreach (var p2 in codes) for (int seed = 0; seed < 2; seed++) {
    var g = new[] { CommandGraph.Load(p1), CommandGraph.Load(p2) };
    var cpu = new[] { new CpuPlayer(g[0], 3 + seed), new CpuPlayer(g[1], 5 + 7 * seed) };
    var m = new BattleModel(new MatchRules { health = 170 }, g);
    var hit = new bool[2]; var wasDown = new bool[2]; m.Hit += h => hit[h.defender] = true;
    var ph = new (float hx, float hz, float fx, float fz, string mv)[2]; bool first = true;
    for (int t = 0; t < 60 * 200 && m.phase != MatchPhase.MatchOver; t++) {
      var c0 = m.phase == MatchPhase.Fight ? cpu[0].Next(m.fighters[0], m.fighters[1]) : PlayerCommand.Neutral;
      var c1 = m.phase == MatchPhase.Fight ? cpu[1].Next(m.fighters[1], m.fighters[0]) : PlayerCommand.Neutral;
      hit[0] = hit[1] = false; var before = m.phase; m.Tick(c0, c1);
      for (int i = 0; i < 2; i++) {
        var f = m.fighters[i]; string mv = g[f.moveOwner].moves[f.move].name;
        if (!first && before == m.phase && !hit[i]) {
          var p = ph[i]; ticks++;
          float ax = p.hx - p.fx, az = p.hz - p.fz, bx = f.headX - f.feetX, bz = f.headZ - f.feetZ;
          float la = (float)Math.Sqrt(ax * ax + az * az), lb = (float)Math.Sqrt(bx * bx + bz * bz);
          double ang = la > .35f && lb > .35f ? Math.Acos(Math.Max(-1, Math.Min(1, (ax * bx + az * bz) / (la * lb)))) * 180 / Math.PI : 0;
          float headMove = (float)Math.Sqrt((f.headX - p.hx) * (f.headX - p.hx) + (f.headZ - p.hz) * (f.headZ - p.hz));
          if (ang > 100 || headMove > .6f) { flips++; string k = $"{p.mv}>{mv}"; kinds[k] = kinds.TryGetValue(k, out int c) ? c + 1 : 1; }
        }
        if (!first && before == m.phase && hit[i] && wasDown[i]) {
          var p = ph[i]; downHits++;
          float ax = p.hx - p.fx, az = p.hz - p.fz, bx = f.headX - f.feetX, bz = f.headZ - f.feetZ;
          float la = (float)Math.Sqrt(ax * ax + az * az), lb = (float)Math.Sqrt(bx * bx + bz * bz);
          double ang = la > .35f && lb > .35f ? Math.Acos(Math.Max(-1, Math.Min(1, (ax * bx + az * bz) / (la * lb)))) * 180 / Math.PI : 0;
          if (ang > 100) { downFlips++; string k = $"{p.mv}>{mv}"; downKinds[k] = downKinds.TryGetValue(k, out int c) ? c + 1 : 1; }
        }
        wasDown[i] = f.downed;
        ph[i] = (f.headX, f.headZ, f.feetX, f.feetZ, mv);
      }
      first = false;
    }
  }
  Console.WriteLine($"ticks {ticks}: body line flips >100 deg or head jumps >0.6 m without a hit: {flips}");
  Console.WriteLine($"hits on bodies lying on the floor {downHits}: body line flipped >100 deg by the hit {downFlips}");
  foreach (var kv in downKinds.OrderByDescending(p => p.Value).Take(8)) Console.WriteLine($"  hit {kv.Value}x {kv.Key}");
  foreach (var kv in kinds.OrderByDescending(p => p.Value).Take(Environment.GetEnvironmentVariable("ALLKINDS") != null ? 400 : 25)) Console.WriteLine($"  {kv.Value}x {kv.Key}");
} }
