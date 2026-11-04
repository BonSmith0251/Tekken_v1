using System; using Versus.Remaster;
static class Trace { static void Main(string[] a) {
  UnityEngine.Resources.Root = System.IO.Path.Combine(a[0], "Assets/_Project/Resources");
  var g = new[] { CommandGraph.Load("kaz"), CommandGraph.Load("jin") };
  // P1 sidesteps up/down repeatedly then jabs; P2 stands. Report alignment after each phase.
  foreach (int stepDir in new[] { 8, 2 })
  {
    var m = new BattleModel(new MatchRules { health = 170 }, g); m.BeginFight();
    int hits = 0; m.Hit += e => { if (e.attacker == 0 && !e.blocked) hits++; };
    for (int t = 0; t < 420; t++) {
      var c0 = PlayerCommand.Neutral; var c1 = PlayerCommand.Neutral;
      int phase = t % 60;
      if (t < 300) c0.stick = phase < 3 ? stepDir : 5;            // tap up/down = sidestep
      else { c0.stick = 5; c0.buttons = (t % 30 == 0) ? 1 : 0; }  // then jab
      m.Tick(c0, c1);
      if (t % 60 == 59) {
        var p = m.fighters[0]; var q = m.fighters[1];
        float dx = q.x - p.x, dz = q.z - p.z, len = (float)Math.Sqrt(dx*dx+dz*dz);
        float aimP = (p.dirX*dx + p.dirZ*dz)/len, aimQ = -(q.dirX*dx + q.dirZ*dz)/len;
        float axis = Math.Abs(m.screenRightX*dx + m.screenRightZ*dz)/len;
        Console.WriteLine($"stick {stepDir} t{t,3} {g[0].moves[p.move].name,-14} P1 ({p.x:F2},{p.z:F2}) P2 ({q.x:F2},{q.z:F2}) dist {len:F2} P1 aims {aimP:F3} P2 aims {aimQ:F3} screen axis on line {axis:F3} facing {p.facing}/{q.facing}");
      }
    }
    Console.WriteLine($"   jab hits after sidesteps: {hits}");
  } } }
