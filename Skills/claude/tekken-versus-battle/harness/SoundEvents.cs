using System; using System.Collections.Generic; using System.Linq; using Versus.Remaster;
// CPU matches (all pairings): which original sound properties fire (0x84C4 own / 0x84C6 opponent, by
// category; 0x8193 footsteps), and how many voice indices fall outside the fighter's exported list.
static class SoundEvents { static void Main(string[] a) {
  UnityEngine.Resources.Root = System.IO.Path.Combine(a[0], "Assets/_Project/Resources");
  var table = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(a[0], "Assets/_Project/Resources/BattleSound/sounds.json")));
  var lists = new Dictionary<string, int[]>();
  foreach (var v in table.RootElement.GetProperty("voices").EnumerateArray())
    lists[v.GetProperty("code").GetString()] = new[] { v.GetProperty("attack").GetArrayLength(), v.GetProperty("damage").GetArrayLength(), v.GetProperty("death").GetArrayLength() };
  string[] codes = { "jin", "kaz", "xia", "asa", "ltn" };
  var counts = new SortedDictionary<string, int>(); int wrapped = 0, voices = 0, hits = 0, matchTicks = 0;
  foreach (var p1 in codes) foreach (var p2 in codes) {
    var g = new[] { CommandGraph.Load(p1), CommandGraph.Load(p2) }; var code = new[] { p1, p2 };
    var cpu = new[] { new CpuPlayer(g[0], 3), new CpuPlayer(g[1], 5) };
    var m = new BattleModel(new MatchRules { health = 170 }, g);
    m.Hit += h => hits++;
    m.MoveProperty += (pl, id, value) => {
      if (id == 0x8193) { counts["footstep"] = counts.GetValueOrDefault("footstep") + 1; return; }
      if (id != 0x84C4 && id != 0x84C6) return;
      int cat = (int)(value >> 24) & 0xFF, idx = (int)(value & 0xFFFFFF); int who = id == 0x84C6 ? 1 - pl : pl;
      string k = (id == 0x84C6 ? "opp " : "own ") + cat; if (cat == 12 || cat == 8 || cat == 1 || cat == 7) { var f = m.fighters[pl]; string mk = $"   {cat}:{idx} {g[f.moveOwner].moves[f.move].name}"; counts[mk] = counts.GetValueOrDefault(mk) + 1; } counts[k] = counts.GetValueOrDefault(k) + 1;
      if (cat >= 2 && cat <= 4) { voices++; if (idx >= lists[code[who]][cat - 2]) wrapped++; }
    };
    for (int t = 0; t < 60 * 200 && m.phase != MatchPhase.MatchOver; t++) {
      var c0 = m.phase == MatchPhase.Fight ? cpu[0].Next(m.fighters[0], m.fighters[1]) : PlayerCommand.Neutral;
      var c1 = m.phase == MatchPhase.Fight ? cpu[1].Next(m.fighters[1], m.fighters[0]) : PlayerCommand.Neutral;
      m.Tick(c0, c1); matchTicks++;
    }
  }
  Console.WriteLine($"{matchTicks / 3600f:F1} min of CPU matches, {hits} hits; voices {voices} ({wrapped} indices past the fighter's list, wrapped)");
  foreach (var kv in counts) Console.WriteLine($"  {kv.Key}: {kv.Value}");
} }
