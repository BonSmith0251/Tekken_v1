using System; using System.Collections.Generic; using Versus.Remaster;
static class Props { static void Main(string[] a) {
  UnityEngine.Resources.Root = System.IO.Path.Combine(a[0], "Assets/_Project/Resources");
  var g = new[] { CommandGraph.Load("kaz"), CommandGraph.Load("jin") };
  var m = new BattleModel(new MatchRules { health = 170 }, g); m.BeginFight();
  var seen = new Dictionary<string,int>();
  m.MoveProperty += (p, id, v) => { if (id==0x8439||id==0x843A||id==0x828C||id==0x84CC||(id>=0x8001&&id<=0x8009)) { string k=$"P{p+1} {id:X} {g[m.fighters[p].moveOwner].moves[m.fighters[p].move].name} v={v}"; seen[k]=seen.TryGetValue(k,out int c)?c+1:1; } };
  // P1 performs Rage Art (b+1+2 with low health = rage) at close range; then CPU battle.
  m.fighters[0].health = 30;
  for (int t = 0; t < 600; t++) {
    var c0 = PlayerCommand.Neutral; var c1 = PlayerCommand.Neutral;
    if (t == 10) { c0.stick = BattleModel.Relative(3, m.fighters[0].facing); c0.buttons = 3; }
    m.Tick(c0, c1); m.fighters[0].health = Math.Max(m.fighters[0].health, 30);
    if (t % 40 == 0) Console.WriteLine($"t{t,3} P1 {g[m.fighters[0].moveOwner].moves[m.fighters[0].move].name,-16} f{m.fighters[0].moveFrame,3}  P2 {g[m.fighters[1].moveOwner].moves[m.fighters[1].move].name,-16} f{m.fighters[1].moveFrame,3} hp {m.fighters[1].health}");
  }
  var cpu = new[] { new CpuPlayer(g[0], 3), new CpuPlayer(g[1], 5) }; var b = new BattleModel(new MatchRules { health = 170 }, g);
  b.MoveProperty += (p, id, v) => { if (id==0x84CC||(id>=0x8001&&id<=0x8009)) { string k=$"cpu {id:X}"; seen[k]=seen.TryGetValue(k,out int c)?c+1:1; } };
  for (int t = 0; t < 7200 && b.phase != MatchPhase.MatchOver; t++) b.Tick(b.phase==MatchPhase.Fight?cpu[0].Next(b.fighters[0],b.fighters[1]):PlayerCommand.Neutral, b.phase==MatchPhase.Fight?cpu[1].Next(b.fighters[1],b.fighters[0]):PlayerCommand.Neutral);
  foreach (var kv in seen) Console.WriteLine($"  {kv.Value,3} {kv.Key}");
} }
