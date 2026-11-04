using System; using System.Collections.Generic; using System.Linq; using Versus.Remaster;
// CPU matches: heading changes of a fighter on the floor (or that was on the floor in the last 30
// ticks), split by cause: re-facing (dir) or the carried turn. Reports jumps over 20 deg in a tick
// with the move before/after and whether a hit landed that tick.
static class DownYaw {
  static float Deg(float a) => (a * 180 / (float)Math.PI + 540) % 360 - 180;
  static void Main(string[] a) {
  UnityEngine.Resources.Root = System.IO.Path.Combine(a[0], "Assets/_Project/Resources");
  string[] codes = { "jin", "kaz", "xia", "asa", "ltn", "nsd", "bob", "leo" };
  int dirJumps = 0, turnJumps = 0, ticks = 0; var causes = new Dictionary<string, int>();
  foreach (var p1 in codes) foreach (var p2 in codes) {
    var g = new[] { CommandGraph.Load(p1), CommandGraph.Load(p2) };
    var cpu = new[] { new CpuPlayer(g[0], 3), new CpuPlayer(g[1], 5) };
    var m = new BattleModel(new MatchRules { health = 170 }, g);
    var hitNow = new bool[2]; m.Hit += h => hitNow[h.defender] = true;
    var pd = new float[2]; var pt = new float[2]; var since = new int[2]; var pm = new string[2];
    for (int t = 0; t < 60 * 200 && m.phase != MatchPhase.MatchOver; t++) {
      var c0 = m.phase == MatchPhase.Fight ? cpu[0].Next(m.fighters[0], m.fighters[1]) : PlayerCommand.Neutral;
      var c1 = m.phase == MatchPhase.Fight ? cpu[1].Next(m.fighters[1], m.fighters[0]) : PlayerCommand.Neutral;
      hitNow[0] = hitNow[1] = false; var ph = m.phase; m.Tick(c0, c1);
      for (int i = 0; i < 2; i++) {
        var f = m.fighters[i]; float d = (float)Math.Atan2(f.dirZ, f.dirX);
        string mv = g[f.moveOwner].moves[f.move].name;
        since[i] = f.downed ? 0 : since[i] + 1;
        if (since[i] <= 30 && t > 0 && ph == m.phase) {
          ticks++;
          float dd = Math.Abs(Deg(d - pd[i])), dt = Math.Abs(f.turn - pt[i]);
          if (dd > 20) { dirJumps++; string k = $"dir {(hitNow[i] ? "on hit" : "no hit")} {(f.downed ? "down" : f.airborne ? "air" : "up")} {pm[i]}>{mv}"; causes[k] = causes.TryGetValue(k, out int c) ? c + 1 : 1; }
          if (dt > 20) { turnJumps++; string k = $"turn {pm[i]}>{mv} ({pt[i]:F0}>{f.turn:F0})"; causes[k] = causes.TryGetValue(k, out int c) ? c + 1 : 1; }
        }
        pd[i] = d; pt[i] = f.turn; pm[i] = mv;
      }
    }
  }
  Console.WriteLine($"floor ticks {ticks}: heading (dir) jumps >20 deg {dirJumps}, carried-turn jumps >20 deg {turnJumps}");
  foreach (var kv in causes.OrderByDescending(p => p.Value).Take(15)) Console.WriteLine($"  {kv.Value}x {kv.Key}");
} }
