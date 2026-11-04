using System; using Versus.Remaster;
// A fighter lies down (forced into the face-up lying loop) while the other walks, dashes and
// sidesteps into and across them. Reports how far the fallen fighter is displaced.
static class Pushed { static void Main(string[] a) {
  UnityEngine.Resources.Root = System.IO.Path.Combine(a[0], "Assets/_Project/Resources");
  foreach (var pair in new[] { ("jin","xia"), ("xia","jin"), ("kaz","jin") }) {
    var g = new[] { CommandGraph.Load(pair.Item1), CommandGraph.Load(pair.Item2) };
    var m = new BattleModel(new MatchRules { health = 170 }, g); m.BeginFight();
    m.interpreters[1].Force(g[1].aliases[7]);              // lying face up
    var f = m.fighters[1]; m.Tick(PlayerCommand.Neutral, PlayerCommand.Neutral);
    float x0 = f.x, z0 = f.z, maxMove = 0; int downTicks = 0; string walker = "";
    for (int t = 0; t < 600; t++) {
      var c0 = PlayerCommand.Neutral;
      int phase = t / 120;   // walk forward, dash (f,f), walk, sidestep up taps, walk back
      int rel = phase == 0 || phase == 2 ? 6 : phase == 1 ? (t % 12 < 2 || (t % 12 >= 4 && t % 12 < 6) ? 6 : 5) : phase == 3 ? (t % 40 < 2 ? 8 : 6) : 4;
      c0.stick = BattleModel.Relative(rel, m.fighters[0].facing);
      m.Tick(c0, PlayerCommand.Neutral);
      if (f.downed) downTicks++;
      maxMove = Math.Max(maxMove, (float)Math.Sqrt((f.x - x0) * (f.x - x0) + (f.z - z0) * (f.z - z0)));
      if (t == 300) walker = g[0].moves[m.fighters[0].move].name;
    }
    Console.WriteLine($"{pair.Item1} walks over {pair.Item2}: fallen fighter moved at most {maxMove:F3} m (downed {downTicks}/600 ticks, still {g[f.moveOwner].moves[f.move].name}), walker x {m.fighters[0].x:F2} vs fallen {f.x:F2}");
  } } }
