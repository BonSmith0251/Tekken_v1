using System;
using System.Collections.Generic;
using UnityEngine;

namespace Versus.Remaster
{
    // Every imported action remains addressable by its catalog index. Balance values
    // are prototype estimates; the imported records do not supply verified hit data.
    public sealed class FighterActionCatalog
    {
        public readonly MuseumCharacter character;
        public readonly MoveSpec[] moves;
        public readonly int[] autoActions;
        public readonly int idle;
        readonly Dictionary<int, int> byMoveId = new Dictionary<int, int>();
        readonly Dictionary<string, int> byClip = new Dictionary<string, int>();

        /// <param name="stanceMoveId">Original stance move (the command graph's); -1 for the Jin/Kazuya defaults.</param>
        public FighterActionCatalog(MuseumCharacter character, string prefix, int stanceMoveId = -1)
        {
            if (!character || character.actions == null || character.actions.Length == 0)
                throw new InvalidOperationException("Missing fighter action catalog");
            this.character = character;
            moves = new MoveSpec[character.actions.Length];
            var playable = new List<int>();
            int stance = stanceMoveId >= 0 ? stanceMoveId : prefix == "Jz_" ? 1442 : 1435;
            idle = Array.FindIndex(character.actions, a => a.moveId == stance);
            if (idle < 0) idle = 0;
            for (int i = 0; i < moves.Length; i++)
            {
                var action = character.actions[i];
                if (action.motion && !byMoveId.ContainsKey(action.moveId)) byMoveId.Add(action.moveId, i);
                if (action.motion && !byClip.ContainsKey(action.motion.name)) byClip.Add(action.motion.name, i);
                string name = action.name ?? "";
                bool kick = name.IndexOf("RK", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("LK", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("KICK", StringComparison.OrdinalIgnoreCase) >= 0;
                bool low = name.IndexOf("LOW", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           name.IndexOf("sweep", StringComparison.OrdinalIgnoreCase) >= 0;
                int tier = (i % 4);
                moves[i] = new MoveSpec(name, low ? HitHeight.Low : kick ? HitHeight.Mid : HitHeight.High,
                    7 + tier * 3, 10 + tier * 3, 3, 15 + tier * 3, 16 + tier * 3, 1.55f + tier * .12f);
                if (action.motion && name.IndexOf(prefix, StringComparison.OrdinalIgnoreCase) >= 0 &&
                    name.IndexOf("WALK", StringComparison.OrdinalIgnoreCase) < 0 &&
                    name.IndexOf("DASH", StringComparison.OrdinalIgnoreCase) < 0 &&
                    name.IndexOf("STEP", StringComparison.OrdinalIgnoreCase) < 0 &&
                    name.IndexOf("FUN", StringComparison.OrdinalIgnoreCase) < 0)
                    playable.Add(i);
            }
            if (playable.Count == 0) throw new InvalidOperationException("No automatic actions for " + character.displayName);
            autoActions = playable.ToArray();
        }

        /// <summary>Catalog index of an original move id, or -1 when it has no converted clip.</summary>
        public int ActionForMove(int moveId) => byMoveId.TryGetValue(moveId, out int index) ? index : -1;

        /// <summary>Catalog index of a converted clip by source key ("common/0137"), or -1.</summary>
        public int ActionForClip(string key) => key != null && byClip.TryGetValue(key.Replace('/', '_'), out int index) ? index : -1;
    }
}
