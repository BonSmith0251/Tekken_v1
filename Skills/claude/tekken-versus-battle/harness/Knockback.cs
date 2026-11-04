using System; using System.Collections.Generic; using System.Linq; using Versus.Remaster;
// CPU matches: how far a clean hit carries the defender. For every clean hit that puts the
// defender on the floor within 90 ticks (and no further hit lands first), the defender's floor
// travel from the hit to the first downed tick, and how much of it points away from the attacker.
static class Knockback { static void Main(string[] a) {
  UnityEngine.Resources.Root = System.IO.Path.Combine(a[0], "Assets/_Project/Resources");
  string[] codes = { "jin", "kaz", "xia", "asa", "ltn", "nsd", "bob", "leo" };
  var falls = new List<(float travel, float away, string move)>(); var stands = new List<float>(); var airs = new List<float>(); var airBy = new Dictionary<string, List<float>>(); var airName = new string[2];
  foreach (var p1 in codes) foreach (var p2 in codes) {
    var g = new[] { CommandGraph.Load(p1), CommandGraph.Load(p2) };
    var cpu = new[] { new CpuPlayer(g[0], 3), new CpuPlayer(g[1], 5) };
    var m = new BattleModel(new MatchRules { health = 170 }, g);
    var track = new (int ticks, float x, float z, float ax, float az, string move, bool done)[2];
    for (int i = 0; i < 2; i++) track[i].done = true;
    var airTrack = new (int ticks, float x, float z, float ax, float az, bool on)[2];
    m.Hit += h => {
      var d = m.fighters[h.defender]; var at = m.fighters[h.attacker];
      if (!track[h.defender].done && track[h.defender].ticks > 0) track[h.defender].done = true; // re-hit: discard
      if (!h.blocked && d.airborne) { airTrack[h.defender] = (0, d.x, d.z, at.x, at.z, true); airName[h.defender] = null; }
      if (h.blocked || d.downed) return;
      track[h.defender] = (0, d.x, d.z, at.x, at.z, null, false);
    };
    for (int t = 0; t < 60 * 200 && m.phase != MatchPhase.MatchOver; t++) {
      var c0 = m.phase == MatchPhase.Fight ? cpu[0].Next(m.fighters[0], m.fighters[1]) : PlayerCommand.Neutral;
      var c1 = m.phase == MatchPhase.Fight ? cpu[1].Next(m.fighters[1], m.fighters[0]) : PlayerCommand.Neutral;
      m.Tick(c0, c1);
      for (int i = 0; i < 2; i++) {
        ref var q = ref airTrack[i];
        if (q.on) {
          var f = m.fighters[i]; q.ticks++; if (airName[i] == null) airName[i] = g[f.moveOwner].moves[f.move].name;
          if (f.downed || q.ticks >= 40) {
            float ux = q.x - q.ax, uz = q.z - q.az, ul = (float)Math.Sqrt(ux * ux + uz * uz);
            float dist = ul > 1e-4f ? ((f.x - q.x) * ux + (f.z - q.z) * uz) / ul : 0; airs.Add(dist); q.on = false; if (!airBy.TryGetValue(airName[i] ?? "?", out var lst)) airBy[airName[i] ?? "?"] = lst = new List<float>(); lst.Add(dist);
          }
        }
      }
      for (int i = 0; i < 2; i++) {
        ref var k = ref track[i]; if (k.done) continue;
        var f = m.fighters[i]; k.ticks++;
        if (k.ticks == 1) k.move = g[f.moveOwner].moves[f.move].name;
        float dx = f.x - k.x, dz = f.z - k.z, ux = k.x - k.ax, uz = k.z - k.az, ul = (float)Math.Sqrt(ux * ux + uz * uz);
        float travel = (float)Math.Sqrt(dx * dx + dz * dz), away = ul > 1e-4f ? (dx * ux + dz * uz) / ul : 0;
        if (f.downed) { falls.Add((travel, away, k.move)); k.done = true; }
        else if (k.ticks == 20 && !f.airborne) { stands.Add(away); }
        if (k.ticks >= 90) k.done = true;
      }
    }
  }
  falls.Sort((x, y) => x.away.CompareTo(y.away));
  float P(List<float> v, double q) => v.Count == 0 ? 0 : v.OrderBy(x => x).ElementAt((int)Math.Min(v.Count - 1, q * v.Count));
  var aw = falls.Select(f => f.away).ToList();
  Console.WriteLine($"falls {falls.Count}: carried away from attacker median {P(aw, .5):F2} m, p10 {P(aw, .1):F2}, p90 {P(aw, .9):F2}, max {aw.DefaultIfEmpty().Max():F2}");
  if (Environment.GetEnvironmentVariable("AIRBY") != null) Console.WriteLine("air hits by reaction: " + string.Join("  ", airBy.OrderByDescending(p => p.Value.Count).Take(8).Select(p => $"{p.Key}:{p.Value.Count}x {p.Value.OrderBy(v => v).ElementAt(p.Value.Count / 2):F2}m")));
  Console.WriteLine($"air hits {airs.Count}: carried away until landing (or 40 ticks) median {P(airs, .5):F2} m, p90 {P(airs, .9):F2}");
  Console.WriteLine($"standing hits {stands.Count}: pushed away after 20 ticks median {P(stands, .5):F2} m, p90 {P(stands, .9):F2}");
  foreach (var grp in falls.GroupBy(f => f.move).OrderByDescending(x => x.Count()).Take(8))
    Console.Write($"{grp.Key}:{grp.Count()}x {grp.Average(x => x.away):F2}m  ");
  Console.WriteLine();
} }
