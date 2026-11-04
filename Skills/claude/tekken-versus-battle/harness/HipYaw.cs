using System; using System.Collections.Generic; using System.Linq; using Versus.Remaster;
// CPU matches: the drawn hip heading (re-facing dir + carried turn + the clip's hip yaw) of a body
// on the floor or rising. Reports per-tick jumps over 45 deg, split by hit / no hit and by move change.
static class HipYaw {
  static float Wrap(float d) => ((d + 180) % 360 + 360) % 360 - 180;
  public static float Yaw(CommandGraph[] g, FighterState f) { var mv = g[f.moveOwner].moves[f.move]; if (mv.clip < 0 || mv.clip >= g[f.moveOwner].clips.Length) return 0; var ys = g[f.moveOwner].clips[mv.clip].yaw; return ys == null || ys.Length == 0 ? 0 : ys[Math.Min(Math.Max(f.moveFrame - 1, 0), ys.Length - 1)]; }
  public static float Hip(CommandGraph[] g, FighterState f) {
    var mv = g[f.moveOwner].moves[f.move]; float y = 0;
    if (mv.clip >= 0 && mv.clip < g[f.moveOwner].clips.Length) { var ys = g[f.moveOwner].clips[mv.clip].yaw; if (ys != null && ys.Length > 0) y = ys[Math.Min(Math.Max(f.moveFrame - 1, 0), ys.Length - 1)]; }
    return Wrap((float)(Math.Atan2(f.dirZ, f.dirX) * 180 / Math.PI) - f.turn - y);
  }
  static void Main(string[] a) {
  UnityEngine.Resources.Root = System.IO.Path.Combine(a[0], "Assets/_Project/Resources");
  string[] codes = { "jin", "kaz", "xia", "asa", "ltn", "nsd", "bob", "leo" };
  int ticks = 0, jumps = 0, hitJumps = 0, downHits = 0; var causes = new Dictionary<string, int>();
  foreach (var p1 in codes) foreach (var p2 in codes) for (int seed = 0; seed < 2; seed++) {
    var g = new[] { CommandGraph.Load(p1), CommandGraph.Load(p2) };
    var cpu = new[] { new CpuPlayer(g[0], 3 + seed), new CpuPlayer(g[1], 5 + 7 * seed) };
    var m = new BattleModel(new MatchRules { health = 170 }, g);
    var hitNow = new bool[2]; m.Hit += h => { if (m.fighters[h.defender].downed || true) hitNow[h.defender] = true; };
    var ph = new float[2]; var pdir = new float[2]; var py = new float[2]; var pm = new string[2]; var wasDown = new bool[2];
    for (int t = 0; t < 60 * 200 && m.phase != MatchPhase.MatchOver; t++) {
      var c0 = m.phase == MatchPhase.Fight ? cpu[0].Next(m.fighters[0], m.fighters[1]) : PlayerCommand.Neutral;
      var c1 = m.phase == MatchPhase.Fight ? cpu[1].Next(m.fighters[1], m.fighters[0]) : PlayerCommand.Neutral;
      hitNow[0] = hitNow[1] = false; var before = m.phase;
      m.Tick(c0, c1);
      for (int i = 0; i < 2; i++) {
        var f = m.fighters[i]; float h = Hip(g, f); string mv = g[f.moveOwner].moves[f.move].name;
        if ((wasDown[i] || Environment.GetEnvironmentVariable("ALL") != null) && (before == m.phase || Environment.GetEnvironmentVariable("ROUNDEND") != null) && !(before == MatchPhase.RoundOver && m.phase == MatchPhase.Intro)) {
          ticks++; if (hitNow[i]) downHits++;
          float d = Math.Abs(Wrap(h - ph[i]));
          if (d > 45) { jumps++; if (hitNow[i]) hitJumps++; string k = $"{(hitNow[i] ? "hit" : "no hit")} {pm[i]}>{mv} ~{Math.Round(d / 30) * 30:F0}" + (hitNow[i] ? $" dir {Wrap((float)(Math.Atan2(f.dirZ, f.dirX) * 180 / Math.PI) - pdir[i]):F0} turn {f.turn:F0} yaw {py[i]:F0}>{Yaw(g, f):F0} f{f.moveFrame} wasdown {wasDown[i]} down {f.downed}" : ""); causes[k] = causes.TryGetValue(k, out int c) ? c + 1 : 1; }
        }
        ph[i] = h; pm[i] = mv; pdir[i] = (float)(Math.Atan2(f.dirZ, f.dirX) * 180 / Math.PI); py[i] = Yaw(g, f); wasDown[i] = f.downed;
      }
    }
  }
  Console.WriteLine($"ticks after a downed tick {ticks}: drawn hip jumps >45 deg {jumps} ({hitJumps} on a hit; {downHits} hits on downed bodies)");
  foreach (var kv in causes.Where(p => Environment.GetEnvironmentVariable("HITS") == null || p.Key.StartsWith("hit")).OrderByDescending(p => p.Value).Take(20)) Console.WriteLine($"  {kv.Value}x {kv.Key}");
} }
