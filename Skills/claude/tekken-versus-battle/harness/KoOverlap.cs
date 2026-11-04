using System; using System.Collections.Generic; using System.Linq; using Versus.Remaster;
// After each KO (all 25 pairings x 2 seeds), measures how far the loser travels from the KO blow
// until the round ends, and how often the two bodies (feet-to-head lines) come closer than 0.35 m.
static class KoOverlap {
  static float Seg(FighterState a, FighterState b) {
    float best = float.MaxValue;
    for (int i = 0; i <= 10; i++) for (int j = 0; j <= 10; j++) {
      float s = i / 10f, t = j / 10f;
      float ax = a.feetX + (a.headX - a.feetX) * s, az = a.feetZ + (a.headZ - a.feetZ) * s;
      float bx = b.feetX + (b.headX - b.feetX) * t, bz = b.feetZ + (b.headZ - b.feetZ) * t;
      best = Math.Min(best, (float)Math.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz)));
    }
    return best;
  }
  static void Main(string[] a) {
  UnityEngine.Resources.Root = System.IO.Path.Combine(a[0], "Assets/_Project/Resources");
  string[] codes = { "jin", "kaz", "xia", "asa", "ltn", "nsd", "bob", "leo" };
  var slides = new List<float>(); var mins = new List<float>(); int kos = 0, overlapKos = 0, ticks = 0, close = 0; var ex = new List<string>();
  foreach (var p1 in codes) foreach (var p2 in codes)
  for (int seed = 0; seed < 2; seed++) {
    var g = new[] { CommandGraph.Load(p1), CommandGraph.Load(p2) };
    var cpu = new[] { new CpuPlayer(g[0], 3 + seed), new CpuPlayer(g[1], 5 + 7 * seed) };
    var m = new BattleModel(new MatchRules { health = 170 }, g);
    MatchPhase last = m.phase; int loser = -1, since = 0; float x0 = 0, z0 = 0, minD = 9; int closeHere = 0; string blow = "";
    for (int t = 0; t < 60 * 400; t++) {
      var c0 = m.phase == MatchPhase.Fight ? cpu[0].Next(m.fighters[0], m.fighters[1]) : PlayerCommand.Neutral;
      var c1 = m.phase == MatchPhase.Fight ? cpu[1].Next(m.fighters[1], m.fighters[0]) : PlayerCommand.Neutral;
      if (m.phase == MatchPhase.Fight) { /* position before the blow */ }
      float px0 = m.fighters[0].x, pz0 = m.fighters[0].z, px1 = m.fighters[1].x, pz1 = m.fighters[1].z;
      m.Tick(c0, c1);
      if (last == MatchPhase.Fight && m.phase != MatchPhase.Fight && m.result != "TIME UP" && m.winner >= 0) {
        loser = 1 - m.winner; since = 0; minD = 9; closeHere = 0;
        x0 = loser == 0 ? px0 : px1; z0 = loser == 0 ? pz0 : pz1;
        var w = m.fighters[m.winner]; blow = g[w.moveOwner].moves[w.move].name;
      }
      if (loser >= 0) {
        var f = m.fighters[loser]; var w = m.fighters[1 - loser];
        since++;
        if (since > 4 && since <= 230) { float d = Seg(f, w); minD = Math.Min(minD, d); ticks++; if (d < .35f) { close++; closeHere++; } }
        if (since == 230) {
          kos++; float s = (float)Math.Sqrt((f.x - x0) * (f.x - x0) + (f.z - z0) * (f.z - z0)); slides.Add(s); mins.Add(minD);
          if (s < .5f && Environment.GetEnvironmentVariable("SHORT") != null) Console.WriteLine($"short: {p1} vs {p2} blow {blow} slide {s:F2} from ({x0:F2},{z0:F2}) to ({f.x:F2},{f.z:F2}) winner ({w.x:F2},{w.z:F2}) loser {g[f.moveOwner].moves[f.move].name}");
          if (closeHere > 0) { overlapKos++; if (ex.Count < 10) ex.Add($"{p1} vs {p2}: blow {blow}, slide {s:F2} m, closest {minD:F2} m, {closeHere} close ticks, loser {g[f.moveOwner].moves[f.move].name}"); }
          loser = -1; if (m.phase == MatchPhase.MatchOver) break;
        }
      }
      last = m.phase;
    }
  }
  slides.Sort(); mins.Sort();
  Console.WriteLine($"KOs {kos}: KO slide median {slides[slides.Count / 2]:F2} m, p10 {slides[slides.Count / 10]:F2}, p90 {slides[slides.Count * 9 / 10]:F2}");
  Console.WriteLine($"bodies closer than 0.35 m after the KO: {overlapKos} KOs, {close} of {ticks} ticks ({100.0 * close / ticks:F1}%), closest median {mins[mins.Count / 2]:F2}");
  foreach (var e in ex) Console.WriteLine(e);
} }
