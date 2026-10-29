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
        public int bodyStep, bodyFields;
        public Move[] moves;
        public Cancel[] cancels, groupCancels;
        public HitCondition[] hitConditions;
        public Reaction[] reactions;
        /// <summary>Knockback records the reactions point at (see Tools/CommandGraph/pushbacks.py).</summary>
        public Pushback[] pushbacks;
        public Clip[] clips;
        public long[] requirements;
        public Sequence[] sequences;
        public long[] inputs;
        public Demo[] demos;
        /// <summary>The movelist's Rage Art input (needs Rage; replayed by the Rage Art button). Tools/CommandGraph/add_rage_art_demo.py.</summary>
        public Demo rageArt;

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
        /// <summary>Move per reaction slot, and the pushback per hit side: 0 front, 1 backturned,
        /// 2 left side, 3 right side, 4 front counter hit, 5 downed, 6 block (-1 none).</summary>
        [Serializable] public sealed class Reaction { public int[] moves, pushbacks; }
        /// <summary>Knockback (the reaction clips carry no root travel): <c>curve</c> pushes one value per
        /// frame in millimetres, then <c>displacement</c> (signed) millimetres every frame for
        /// <c>duration</c> frames. See Tools/CommandGraph/pushbacks.py.</summary>
        [Serializable] public sealed class Pushback { public int duration, displacement; public int[] curve; }
        [Serializable] public sealed class Sequence { public int window, inputStart, inputCount; }
        /// <summary>Cumulative root travel in millimetres (forward, to the fighter's left) and the hip's yaw
        /// relative to the stance in degrees (positive to the fighter's right).</summary>
        /// <summary><c>body</c>: every <c>bodyStep</c> frames, <c>bodyFields</c> values: the hip, head and feet
        /// midpoint relative to the fighter's floor position in the body frame, centimetres
        /// [hipForward, hipLeft, headForward, headLeft, feetForward, feetLeft] (Tools/CommandGraph/body_points.py).</summary>
        [Serializable] public sealed class Clip { public string key; public int[] forward, lateral, yaw, body; }

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
