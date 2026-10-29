using System;
using System.Collections.Generic;
using System.IO;
using Versus.Remaster;

static class PropertyEvents
{
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    static BattleModel Model(out CommandGraph jin)
    {
        jin = CommandGraph.Load("jin");
        var model = new BattleModel(new MatchRules { health = 170 }, new[] { jin, CommandGraph.Load("xia") });
        for (int i = 0; i < 300 && model.phase != MatchPhase.Fight; i++) model.Tick(PlayerCommand.Neutral, PlayerCommand.Neutral);
        Require(model.phase == MatchPhase.Fight, "Could not enter fight phase.");
        model.fighters[0].x = -4; model.fighters[1].x = 4;
        return model;
    }
    static int Main(string[] args)
    {
        UnityEngine.Resources.Root = Path.Combine(args[0], "Assets/_Project/Resources");
        int realEvents = 0;
        foreach (int id in new[] { 1779, 1840, 1843, 1844 })
        {
            var model = Model(out var jin);
            var events = new List<ActedMovePropertyEvent>();
            model.ActedMoveProperty += e => { if (e.id == 0x802E && e.player == 0) events.Add(e); };
            model.interpreters[0].Force(id);
            for (int frame = 0; frame < 8; frame++) model.Tick(PlayerCommand.Neutral, PlayerCommand.Neutral);
            Require(events.Count == 1, "Expected exactly one original event in " + jin.moves[id].name);
            var row = events[0];
            Require(row.graph == jin && row.move == id && row.frame == 6 && row.rawFrame == 6 && row.value == 7,
                "Original Jin event lost its move/frame/bone provenance.");
            realEvents++;
        }
        for (int actor = 0; actor < 2; actor++)
        {
            var model = Model(out var jin);
            var move = jin.moves[1779];
            move.cancelStart = -1; move.frames = 1; move.transition = jin.stance;
            move.firstActive = move.lastActive = 0;
            move.properties = new long[] { 1, 0x802E, 7 };
            var events = new List<ActedMovePropertyEvent>();
            model.ActedMoveProperty += e => { if (e.id == 0x802E && e.player == actor) events.Add(e); };
            if (actor == 0) model.interpreters[0].Force(1779); else model.interpreters[1].Borrow(jin, 1779);
            model.Tick(PlayerCommand.Neutral, PlayerCommand.Neutral);
            Require(events.Count == 1 && events[0].graph == jin && events[0].move == 1779 && events[0].frame == 1,
                "Final-frame property was lost or assigned to the wrong graph/actor.");
            model.Tick(PlayerCommand.Neutral, PlayerCommand.Neutral);
            Require(events.Count == 1, "Final-frame property was duplicated after transition.");
        }
        // An observer must not alter the existing battle model. Compare identical seeded matches.
        var a = Model(out var ga); var b = Model(out var gb);
        var ca = new[] { new CpuPlayer(a.graphs[0], 31), new CpuPlayer(a.graphs[1], 48) };
        var cb = new[] { new CpuPlayer(b.graphs[0], 31), new CpuPlayer(b.graphs[1], 48) };
        int observed = 0;
        b.ActedMoveProperty += e => observed++;
        for (int tick = 0; tick < 3600; tick++)
        {
            a.Tick(ca[0].Next(a.fighters[0], a.fighters[1]), ca[1].Next(a.fighters[1], a.fighters[0]));
            b.Tick(cb[0].Next(b.fighters[0], b.fighters[1]), cb[1].Next(b.fighters[1], b.fighters[0]));
            for (int i = 0; i < 2; i++)
                Require(a.fighters[i].move == b.fighters[i].move && a.fighters[i].health == b.fighters[i].health
                    && a.fighters[i].x == b.fighters[i].x && a.fighters[i].z == b.fighters[i].z,
                    "Visual event observation changed battle state.");
        }
        Console.WriteLine("PASS: " + realEvents + " original Jin electric events; own/borrowed final-frame events; no duplicates; "
            + "3600-tick battle-state parity with " + observed + " observed properties.");
        return 0;
    }
}
