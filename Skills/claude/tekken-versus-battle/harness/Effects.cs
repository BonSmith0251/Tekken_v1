using System; using System.Collections.Generic; using Versus.Remaster;
// Counts the battle-effect triggers VersusGame derives (same rules as OnHit and UpdateBattleEffects):
// Hit / CriticalHit (counter) / Guard / Pierce (clean hit >= 20 damage) / Blast (lands from the air) /
// Smoke (any fall to the floor) / Rage start. Also reports the shortest gap between two floor effects
// for one fighter, to catch a downed flag that flickers and would spam smoke.
static class Effects { static void Main(string[] a) {
  UnityEngine.Resources.Root = System.IO.Path.Combine(a[0], "Assets/_Project/Resources");
  foreach (var pair in new[] { ("jin","xia"), ("xia","jin"), ("kaz","jin"), ("kaz","xia") }) {
    var g = new[] { CommandGraph.Load(pair.Item1), CommandGraph.Load(pair.Item2) };
    var cpu = new[] { new CpuPlayer(g[0], 3), new CpuPlayer(g[1], 5) };
    var m = new BattleModel(new MatchRules { health = 170 }, g);
    var n = new Dictionary<string, int> { ["Hit"]=0, ["Counter"]=0, ["Guard"]=0, ["Pierce"]=0, ["Blast"]=0, ["Smoke"]=0, ["Rage"]=0, ["Punish"]=0, ["KO"]=0 };
    m.Hit += h => { n[h.blocked ? "Guard" : h.counterHit ? "Counter" : "Hit"]++; if (!h.blocked && h.damage >= 20) n["Pierce"]++; if (h.punish) n["Punish"]++; };
    bool[] wasDown = new bool[2], wasAir = new bool[2], wasRage = new bool[2]; int[] lastFloor = { -9999, -9999 }; int minGap = int.MaxValue;
    int ticks = 0, matches = 0;
    for (int t = 0; t < 60*1200 && matches < 3; t++, ticks++) {
      var c0 = m.phase==MatchPhase.Fight ? cpu[0].Next(m.fighters[0], m.fighters[1]) : PlayerCommand.Neutral;
      var c1 = m.phase==MatchPhase.Fight ? cpu[1].Next(m.fighters[1], m.fighters[0]) : PlayerCommand.Neutral;
      m.Tick(c0, c1);
      bool live = m.phase == MatchPhase.Fight || m.phase == MatchPhase.RoundOver || m.phase == MatchPhase.MatchOver;
      for (int i = 0; i < 2; i++) {
        var f = m.fighters[i];
        if (live && f.downed && !wasDown[i]) { if (wasAir[i]) n["Blast"]++; n["Smoke"]++; minGap = Math.Min(minGap, t - lastFloor[i]); lastFloor[i] = t; }
        wasDown[i] = f.downed; wasAir[i] = f.airborne;
        bool rage = m.phase == MatchPhase.Fight && f.health > 0 && f.health * 4 <= m.rules.health;
        if (rage && !wasRage[i]) n["Rage"]++;
        wasRage[i] = rage;
      }
      if (m.phase == MatchPhase.MatchOver) { matches++; n["KO"]++; for (int k = 0; k < 300; k++) m.Tick(PlayerCommand.Neutral, PlayerCommand.Neutral); m.Restart(); wasDown = new bool[2]; wasAir = new bool[2]; wasRage = new bool[2]; }
    }
    Console.WriteLine($"{pair.Item1} vs {pair.Item2}: {matches} matches, {ticks/3600.0:0.0} min | " + string.Join("  ", n.Keys.Select(k => k + " " + n[k])) + $" | min floor gap {minGap} ticks");
  } } }
static class Ext { public static IEnumerable<string> Select(this IEnumerable<string> s, Func<string,string> f) { foreach (var x in s) yield return f(x); } }
