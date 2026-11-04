using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Versus.Remaster;

// Rage Art button (VersusGame.TriggerRageArt / ReleaseRageGrants) in CPU auto fights: two seconds into
// a round, grant Rage and request the Rage Art; report whether a RageArts move starts within 8 s, whether
// its camera event (0x8439/0x843A, bank 0x4000) fires, and whether the grant releases afterwards.
// Every fighter, both slots, against Xiaoyu. Usage: run.sh RageButton [project].
static class RageButton
{
    static int Main(string[] args)
    {
        UnityEngine.Resources.Root = Path.Combine(args[0], "Assets/_Project/Resources");
        int faults = 0;
        foreach (string code in new[] { "jin", "kaz", "xia", "asa", "ltn", "nsd", "bob", "leo" })
            for (int slot = 0; slot < 2; slot++)
            {
                var own = CommandGraph.Load(code); var other = CommandGraph.Load(code == "xia" ? "jin" : "xia");
                var model = new BattleModel(new MatchRules { health = 170 }, slot == 0 ? new[] { own, other } : new[] { other, own });
                var cpu = new[] { new CpuPlayer(model.graphs[0], 31), new CpuPlayer(model.graphs[1], 48) };
                int pressedAt = -1, startedAt = -1, releasedAt = -1, camera = -1; string bank = ""; int armedAt = -1;
                string rageMove = "";
                model.MoveProperty += (player, id, value) =>
                // Archive (kind 0x4000) or moveset-bank (kind 0, VersusGame's "m" tracks: Alisa, Lidia) cameras.
                { if (player == slot && (id == 0x8439 || id == 0x843A) && ((value & 0xC000) == 0x4000 || (value & 0xC000) == 0) && camera < 0) { camera = (int)(value & 0x3FFF); bank = (value & 0xC000) == 0 ? "m" : ""; } };
                // The camera starts during the Rage Art's startup; a Rage Art struck out of its startup by the
                // opponent never reaches that frame.
                string outcome = "uninterrupted";
                model.Hit += h => { if (h.defender == slot && startedAt >= 0 && camera < 0 && outcome == "uninterrupted") outcome = (h.absorbed ? "absorbed " : h.blocked ? "guarded " : "struck out of it by ") + model.graphs[h.attacker].moves[model.fighters[h.attacker].move].name + $" ({h.damage} damage, health left {model.fighters[slot].health})"; };
                int fightTicks = 0;
                var trigger = new RageArtTrigger();
                for (int t = 0; t < 60 * 30; t++)
                {
                    if (model.phase == MatchPhase.Fight) fightTicks++;
                    if (model.phase == MatchPhase.Fight && pressedAt < 0 && fightTicks > 120) { trigger.Press(model, slot, cpu[slot]); pressedAt = t; }
                    var c0 = model.phase == MatchPhase.Fight ? cpu[0].Next(model.fighters[0], model.fighters[1]) : PlayerCommand.Neutral;
                    var c1 = model.phase == MatchPhase.Fight ? cpu[1].Next(model.fighters[1], model.fighters[0]) : PlayerCommand.Neutral;
                    model.Tick(c0, c1);
                    trigger.Tick(model, slot, cpu[slot]);
                    var it = model.interpreters[slot];
                    string name = it.ActedGraph != null ? it.ActedGraph.moves[it.ActedMove].name : "";
                    if (pressedAt >= 0 && startedAt < 0 && name.Contains("RageArts")) { startedAt = t; rageMove = name; }
                    if (startedAt >= 0 && armedAt < 0 && model.IsArmoured(slot)) armedAt = t;
                    if (pressedAt >= 0 && releasedAt < 0 && !trigger.Active) releasedAt = t;
                    if (releasedAt >= 0 && t > releasedAt + 30) break;
                }
                bool ok = startedAt >= 0 && startedAt - pressedAt <= 600 && releasedAt > startedAt && !model.rageGranted[slot] && (camera >= 0 || outcome.StartsWith("struck"));
                if (!ok) faults++;
                Console.WriteLine($"{code} slot {slot}: {(ok ? "OK " : "BAD")} pressed {pressedAt} -> {rageMove} after {(startedAt < 0 ? -1 : startedAt - pressedAt)} ticks, {outcome}, armoured {(armedAt < 0 ? "never" : $"after {armedAt - startedAt} ticks")}, camera {(camera < 0 ? "none" : bank + camera)}, grant released after {(releasedAt < 0 ? -1 : releasedAt - pressedAt)} ticks");
            }
        Console.WriteLine(faults == 0 ? "PASS" : faults + " failed");
        return faults == 0 ? 0 : 1;
    }
}
