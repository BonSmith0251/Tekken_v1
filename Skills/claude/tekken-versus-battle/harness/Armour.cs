using System; using System.Collections.Generic; using System.Linq; using Versus.Remaster;
// Power crush armour (property 0x81BD): for every fighter, each armoured move (power crush, its *arm variant,
// Rage Art startup and attack, Kazuya's transform) is forced while the attacker's fastest high, mid, low and
// throw is timed to land inside the armoured frames. Highs and mids must be absorbed (damage dealt, defender
// keeps its move); lows and throws must not. Usage: run.sh Armour [project].
static class Armour {
  static long[] Props(CommandGraph.Move m) => m.properties ?? new long[0];
  // First armoured frame and the frame armour ends (exclusive; the move's length if it never ends).
  static (int from, int to)? Window(CommandGraph.Move m) {
    var p = Props(m); int from = -1, to = -1;
    for (int k = 0; k + 2 < p.Length; k += 3)
      if (p[k + 1] == BattleModel.Armour) { int f = (int)(p[k] & 0x3FFF); if (p[k + 2] != 0 && from < 0) from = f; else if (p[k + 2] == 0 && from >= 0 && to < 0) to = f; }
    return from < 0 ? null : (from, to < 0 ? m.frames : to);
  }
  static void Main(string[] a) {
    UnityEngine.Resources.Root = System.IO.Path.Combine(a[0], "Assets/_Project/Resources");
    int faults = 0, cases = 0;
    foreach (string code in new[] { "jin", "kaz", "xia", "asa", "ltn", "nsd", "bob", "leo" }) {
      var own = CommandGraph.Load(code); var foeCode = code == "xia" ? "jin" : "xia"; var foe = CommandGraph.Load(foeCode);
      string prefix = foe.moves[foe.stance].name.Substring(0, 3);
      int Pick(Func<CommandGraph.Move, bool> kind) {
        int best = -1;
        for (int i = 0; i < foe.moves.Length; i++) {
          var m = foe.moves[i];
          if (m.firstActive <= 0 || m.damage <= 0 || !kind(m) || !(m.name.StartsWith(prefix) || m.name.StartsWith("Thrmv"))) continue;
          if (best < 0 || m.firstActive < foe.moves[best].firstActive) best = i;
        }
        return best;
      }
      var attacks = new (string kind, int move, bool absorb)[] {
        ("high", Pick(m => BattleModel.Height(m.hitlevel) == HitHeight.High && (m.hitlevel & 0x400000) == 0), true),
        ("mid", Pick(m => BattleModel.Height(m.hitlevel) == HitHeight.Mid && (m.hitlevel & 0x400000) == 0), true),
        ("low", Pick(m => BattleModel.Height(m.hitlevel) == HitHeight.Low && (m.hitlevel & 0x400000) == 0), false),
        ("throw", Pick(m => (m.hitlevel & 0x400000) != 0), false) };
      for (int i = 0; i < own.moves.Length; i++) {
        var m = own.moves[i]; var w = Window(m);
        if (w == null || m.name.StartsWith("Co_") || m.name.StartsWith("He_") || m.name.StartsWith("SKz") || m.name.Contains("7CS") || m.name.Contains("PHASE")) continue;
        var parts = new List<string>();
        foreach (var at in attacks) {
          if (at.move < 0) { parts.Add(at.kind + " n/a"); continue; }
          var model = new BattleModel(new MatchRules { health = 170 }, new[] { foe, own });
          model.Restart(); model.BeginFight();
          var atk = model.fighters[0]; var def = model.fighters[1];
          atk.x = -.45f; def.x = .45f; atk.z = def.z = 0;
          int land = Math.Min(w.Value.to - 1, w.Value.from + 2), hitFirst = foe.moves[at.move].firstActive;
          int defenderStart = Math.Max(0, hitFirst - land), attackerStart = Math.Max(0, land - hitFirst);
          HitEvent? landed = null; string after = "";
          model.Hit += h => { if (landed == null && h.attacker == 0) landed = h; };
          for (int t = 0; t < 120 && landed == null; t++) {
            if (t == defenderStart) { model.interpreters[1].Force(i); def.move = i; def.moveFrame = 0; }
            if (t == attackerStart) { model.interpreters[0].Force(at.move); atk.move = at.move; atk.moveFrame = 0; }
            model.Tick(PlayerCommand.Neutral, PlayerCommand.Neutral);
          }
          if (landed != null) after = model.interpreters[1].Active == own && model.interpreters[1].Move == i ? "kept" : "lost";
          bool ok = landed != null && landed.Value.absorbed == at.absorb && (at.absorb ? after == "kept" && landed.Value.damage > 0 : after == "lost");
          cases++; if (!ok && landed != null) faults++;
          parts.Add($"{at.kind} {(landed == null ? "missed" : landed.Value.absorbed ? "absorbed" : landed.Value.blocked ? "guarded" : "hit")}{(landed != null && !ok ? "!" : "")}");
        }
        Console.WriteLine($"{code} {m.name} armour {w.Value.from}-{w.Value.to}: " + string.Join(", ", parts));
      }
    }
    Console.WriteLine(faults == 0 ? $"PASS ({cases} cases; 'missed' = the attack did not reach, not counted)" : $"{faults} wrong");
  }
}
