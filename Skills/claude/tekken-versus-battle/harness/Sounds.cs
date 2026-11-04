using System; using System.Collections.Generic; using System.Linq; using Versus.Remaster;
// CPU matches (all pairings of the eight fighters) against the exported sound tables: how many 0x84C4/0x84C6
// properties resolve to an exported clip (voices, common battle tones 1/6/7/8, system tones 15), and how many clean
// hits take their impact from the attacker's own sound list (moves_<code>.json) instead of the damage fallback.
static class Sounds { static void Main(string[] a) {
  string res = System.IO.Path.Combine(a[0], "Assets/_Project/Resources");
  UnityEngine.Resources.Root = res;
  var table = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(res, "BattleSound/sounds.json"))).RootElement;
  var events = new HashSet<string>(table.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("name").GetString()));
  string[] tones = table.GetProperty("tones").EnumerateArray().Select(e => e.GetString()).ToArray();
  string[] system = table.GetProperty("systemTones").EnumerateArray().Select(e => e.GetString()).ToArray();
  var voiceLen = new Dictionary<string, int[]>();
  foreach (var v in table.GetProperty("voices").EnumerateArray())
    voiceLen[v.GetProperty("code").GetString()] = new[] { v.GetProperty("attack").GetArrayLength(), v.GetProperty("damage").GetArrayLength(), v.GetProperty("death").GetArrayLength() };
  string[] codes = { "jin", "kaz", "xia", "asa", "ltn", "nsd", "bob", "leo" };
  var lists = new Dictionary<string, Dictionary<int, int[]>>();
  foreach (var c in codes) {
    var d = new Dictionary<int, int[]>();
    foreach (var m in System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(res, $"BattleSound/moves_{c}.json"))).RootElement.GetProperty("moves").EnumerateArray())
      d[m.GetProperty("id").GetInt32()] = m.GetProperty("clips").EnumerateArray().Select(x => x.GetInt32()).ToArray();
    lists[c] = d;
  }
  var played = new SortedDictionary<string, int>(); var silent = new SortedDictionary<string, int>();
  int clean = 0, listed = 0, blocked = 0, ticks = 0;
  void Count(SortedDictionary<string, int> d, string k) => d[k] = d.GetValueOrDefault(k) + 1;
  foreach (var p1 in codes) foreach (var p2 in codes) {
    var g = new[] { CommandGraph.Load(p1), CommandGraph.Load(p2) }; var code = new[] { p1, p2 };
    var cpu = new[] { new CpuPlayer(g[0], 3), new CpuPlayer(g[1], 5) };
    var m = new BattleModel(new MatchRules { health = 170 }, g);
    m.Hit += h => {
      if (h.blocked) { blocked++; return; }
      if (h.absorbed) return;
      clean++;
      var f = m.fighters[h.attacker];
      if (lists[code[f.moveOwner]].TryGetValue(f.move, out var clips) && clips.Any(x => x >> 24 == 7 && (x & 0xFFFFFF) < tones.Length && tones[x & 0xFFFFFF] != "")) listed++;
    };
    m.MoveProperty += (pl, id, value) => {
      if (id != 0x84C4 && id != 0x84C6) return;
      int cat = (int)(value >> 24) & 0xFF, idx = (int)(value & 0xFFFFFF); int who = id == 0x84C6 ? 1 - pl : pl;
      bool ok = cat >= 2 && cat <= 4 ? voiceLen[code[who]][cat - 2] > 0
              : cat == 1 || cat == 6 || cat == 7 || cat == 8 ? idx < tones.Length && tones[idx] != ""
              : cat == 15 ? idx < system.Length && system[idx] != ""
              : cat == 9 || cat == 12;
      Count(ok ? played : silent, "cat " + cat);
    };
    for (int t = 0; t < 60 * 200 && m.phase != MatchPhase.MatchOver; t++) {
      var c0 = m.phase == MatchPhase.Fight ? cpu[0].Next(m.fighters[0], m.fighters[1]) : PlayerCommand.Neutral;
      var c1 = m.phase == MatchPhase.Fight ? cpu[1].Next(m.fighters[1], m.fighters[0]) : PlayerCommand.Neutral;
      m.Tick(c0, c1); ticks++;
    }
  }
  Console.WriteLine($"{ticks / 3600f:F1} min of CPU matches; clean hits {clean}, impact from the attacker's list {listed} ({100f * listed / Math.Max(1, clean):F0}%), blocks {blocked}");
  Console.WriteLine("sound properties with a clip: " + string.Join(", ", played.Select(kv => $"{kv.Key} {kv.Value}")));
  Console.WriteLine("sound properties without a clip: " + string.Join(", ", silent.Select(kv => $"{kv.Key} {kv.Value}")));
} }
