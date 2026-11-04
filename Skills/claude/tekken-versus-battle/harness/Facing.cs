using System; using System.Collections.Generic; using System.Linq; using Versus.Remaster;
// CPU matches: how far each fighter's heading (dir turned by the carried turn) is from the
// opponent at hits, during moves, and how often the heading snaps by a large angle in one tick.
static class Facing {
  static BattleModel M; static float Yaw(FighterState f) => M.interpreters[M.fighters[0] == f ? 0 : 1].BodyYaw;
  static float Off(FighterState f, FighterState o) {
    double h = Math.Atan2(f.dirZ, f.dirX) - Yaw(f) * Math.PI / 180, to = Math.Atan2(o.z - f.z, o.x - f.x);
    double d = Math.Abs(((h - to) * 180 / Math.PI % 360 + 540) % 360 - 180); return (float)d; }
  static void Main(string[] a) {
  UnityEngine.Resources.Root = System.IO.Path.Combine(a[0], "Assets/_Project/Resources");
  int detail = 0; int hits = 0, hitsOff = 0, ticks = 0, offTicks = 0, snaps = 0, landOff = 0, lands = 0;
  var byMove = new Dictionary<string, int>();
  foreach (var pair in new[] { ("xia","jin"), ("jin","xia"), ("xia","kaz"), ("xia","xia") })
  for (int seed = 0; seed < 4; seed++) {
    var g = new[] { CommandGraph.Load(pair.Item1), CommandGraph.Load(pair.Item2) };
    var cpu = new[] { new CpuPlayer(g[0], 3 + seed), new CpuPlayer(g[1], 5 + seed * 7) };
    var m = new BattleModel(new MatchRules { health = 170 }, g); M = m;
    var pending = new List<int>();
    m.Hit += h => { if (!h.blocked) pending.Add(h.defender); };
    var pdir = new float[2]; var wasDown = new bool[2];
    for (int t = 0; t < 60 * 240 && m.phase != MatchPhase.MatchOver; t++) {
      var c0 = m.phase == MatchPhase.Fight ? cpu[0].Next(m.fighters[0], m.fighters[1]) : PlayerCommand.Neutral;
      var c1 = m.phase == MatchPhase.Fight ? cpu[1].Next(m.fighters[1], m.fighters[0]) : PlayerCommand.Neutral;
      pending.Clear(); m.Tick(c0, c1);
      if (m.phase != MatchPhase.Fight) { for (int i = 0; i < 2; i++) pdir[i] = (float)Math.Atan2(m.fighters[i].dirZ, m.fighters[i].dirX); continue; }
      foreach (int d in pending) { hits++; if (Off(m.fighters[d], m.fighters[1 - d]) > 60) hitsOff++; }
      for (int i = 0; i < 2; i++) {
        var f = m.fighters[i]; var o = m.fighters[1 - i];
        float dir = (float)Math.Atan2(f.dirZ, f.dirX); float dd = Math.Abs(dir - pdir[i]); if (dd > Math.PI) dd = (float)(2 * Math.PI - dd);
        if (dd * 180 / Math.PI > 60) snaps++;
        pdir[i] = dir;
        if (f.downed && !wasDown[i]) { lands++; if (Off(f, o) > 60 && !f.backturned) landOff++; }
        wasDown[i] = f.downed;
        if (f.downed || f.backturned || f.airborne) continue;
        ticks++;
        if (Off(f, o) > 90) { if (detail < 0 && g[f.moveOwner].moves[f.move].name.StartsWith("Fu_")) { detail++; var mv = g[f.moveOwner].moves[f.move]; Console.WriteLine($"{mv.name} f{f.moveFrame}/{mv.frames} act {mv.firstActive}-{mv.lastActive} off {Off(f,o):F0} turn {f.turn:F0} air {f.airborne} react {f.reacting} dist {Math.Sqrt((o.x-f.x)*(o.x-f.x)+(o.z-f.z)*(o.z-f.z)):F2} oppMove {g[o.moveOwner].moves[o.move].name}"); } offTicks++; string n = g[f.moveOwner].moves[f.move].name; byMove[n] = byMove.TryGetValue(n, out int c) ? c + 1 : 1; }
      }
    }
  }
  Console.WriteLine($"hits {hits}: defender heading >60 deg off attacker {hitsOff}");
  Console.WriteLine($"landings {lands}: >60 deg off opponent {landOff}");
  Console.WriteLine($"front-facing ticks {ticks}: heading >90 deg off opponent {offTicks}; snaps >60 deg/tick {snaps}");
  foreach (var kv in byMove.OrderByDescending(p => p.Value).Take(12)) Console.Write($"{kv.Key}:{kv.Value} "); Console.WriteLine();
} }
