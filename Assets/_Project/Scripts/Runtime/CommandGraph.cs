using System;
using System.Collections.Generic;
using UnityEngine;

namespace Versus.Remaster
{
    /// <summary>
    /// A fighter's original command graph, exported by Tools/CommandGraph/export_command_graph.py
    /// from the installed &lt;chr&gt;mot.bin. Tables keep their source indexing, so move ids, cancel
    /// offsets, group-cancel offsets and reaction indices are used without remapping.
    /// </summary>
    [Serializable]
    public sealed class CommandGraph
    {
        public string source, sha256;
        public int characterId, stance, requirementEnd;
        /// <summary>Standard states (stance, crouch, downed...) by alias slot; a slot names each character's equivalent.</summary>
        public int[] aliases;
        public Move[] moves;
        public Cancel[] cancels, groupCancels;
        public HitCondition[] hitConditions;
        public Reaction[] reactions;
        public Clip[] clips;
        public long[] requirements;
        public Sequence[] sequences;
        public long[] inputs;
        public Demo[] demos;

        /// <summary>A movelist entry's original demo input: (numpad direction, buttons, frames) triples.</summary>
        [Serializable] public sealed class Demo { public string name, command; public int[] steps; }
        [Serializable] public sealed class Move
        {
            public int id, frames, transition, firstActive, lastActive, damage, hitlevel;
            /// <summary>Vulnerability state (low byte: 0x80 downed, 0x20 crouching) and airborne frame window.</summary>
            public int vuln, airStart, airEnd;
            public int cancelStart, hitConditionStart, clip;
            public long[] properties;
            public string name;
        }
        [Serializable] public sealed class Cancel
        {
            public long command;
            public int reqStart, reqCount, extra, start, end, startingFrame, target;
        }
        [Serializable] public sealed class HitCondition { public int reqStart, reqCount, damage, reaction; }
        [Serializable] public sealed class Reaction { public int[] moves; }
        [Serializable] public sealed class Sequence { public int window, inputStart, inputCount; }
        /// <summary>Cumulative root travel in millimetres (forward, to the fighter's left) and the hip's yaw
        /// relative to the stance in degrees (positive to the fighter's right).</summary>
        [Serializable] public sealed class Clip { public string key; public int[] forward, lateral, yaw; }

        Dictionary<string, int> byName;

        public int FindMove(string name)
        {
            if (byName == null)
            {
                byName = new Dictionary<string, int>(moves.Length);
                foreach (var m in moves) if (!string.IsNullOrEmpty(m.name) && !byName.ContainsKey(m.name)) byName.Add(m.name, m.id);
            }
            return byName.TryGetValue(name ?? "", out int id) ? id : -1;
        }

        public static CommandGraph Load(string code)
        {
            var asset = Resources.Load<TextAsset>("CommandGraph/" + code);
            if (!asset) throw new InvalidOperationException("Missing command graph: Resources/CommandGraph/" + code);
            var graph = JsonUtility.FromJson<CommandGraph>(asset.text);
            if (graph?.moves == null || graph.moves.Length == 0 || graph.stance < 0 || graph.stance >= graph.moves.Length)
                throw new InvalidOperationException("Invalid command graph: " + code);
            return graph;
        }
    }
}
