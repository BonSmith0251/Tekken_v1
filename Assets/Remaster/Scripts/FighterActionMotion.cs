using System;
using System.Collections.Generic;
using UnityEngine;

namespace Versus.Remaster
{
    public sealed class FighterActionMotion : MonoBehaviour
    {
        FighterActionCatalog catalog;
        Transform[] bones;
        Vector3[] positions;
        Quaternion[,] rotations;
        int frames, current = -1, lastAttackFrame = -1;
        int presentationAction = -1;
        float presentationFrame;
        bool presentationLoops;
        bool presentationRootMotion;
        float stateStart;
        public int PlayedActions { get; private set; }
        public int CurrentAction => current;
        public bool Ready => catalog != null && bones != null;

        public void SetPresentation(int action, float frame, bool loop, bool rootMotion = false)
        {
            presentationAction = action;
            presentationFrame = frame;
            presentationLoops = loop;
            presentationRootMotion = rootMotion;
        }

        public void Initialize(FighterActionCatalog source)
        {
            catalog = source;
            var map = new Dictionary<string, Transform>();
            foreach (var bone in GetComponentsInChildren<Transform>()) map[bone.name] = bone;
            bones = new Transform[source.character.boneNames.Length];
            for (int i = 0; i < bones.Length; i++)
                if (!map.TryGetValue(source.character.boneNames[i], out bones[i]))
                    throw new InvalidOperationException("Missing " + source.character.boneNames[i] + " on " + source.character.displayName);
            Load(source.idle);
        }

        void Load(int index)
        {
            var asset = catalog.character.actions[index].motion;
            if (!asset) throw new InvalidOperationException("Missing action motion " + index);
            byte[] data = asset.bytes;
            if (data.Length < 16 || System.Text.Encoding.ASCII.GetString(data, 0, 4) != "JRT1")
                throw new InvalidOperationException("Invalid action motion " + index);
            int count = BitConverter.ToInt32(data, 8);
            frames = BitConverter.ToInt32(data, 4);
            if (frames < 1 || count != bones.Length || BitConverter.ToInt32(data, 12) != 60 ||
                data.Length != 16L + frames * (3L + count * 4L) * 4)
                throw new InvalidOperationException("Action motion does not match rig " + index);
            positions = new Vector3[frames]; rotations = new Quaternion[frames, count];
            for (int f = 0; f < frames; f++)
            {
                int offset = 16 + f * (3 + count * 4) * 4;
                positions[f] = new Vector3(BitConverter.ToSingle(data, offset), BitConverter.ToSingle(data, offset + 4), BitConverter.ToSingle(data, offset + 8));
                for (int c = 0; c < count; c++)
                {
                    int p = offset + 12 + c * 16;
                    rotations[f, c] = new Quaternion(BitConverter.ToSingle(data, p), BitConverter.ToSingle(data, p + 4),
                        BitConverter.ToSingle(data, p + 8), BitConverter.ToSingle(data, p + 12));
                }
            }
            current = index;
        }

        public void Apply(FighterState fighter, MoveSpec move, float clock, bool moving)
        {
            if (!Ready) return;
            int target = presentationAction >= 0 ? presentationAction :
                fighter.attack >= 0 ? fighter.attack : catalog.idle;
            if (target != current || (fighter.attack >= 0 && fighter.attackFrame < lastAttackFrame))
            {
                Load(target); stateStart = clock;
                if (fighter.attack >= 0) PlayedActions++;
            }
            lastAttackFrame = fighter.attackFrame;
            float frame = presentationAction >= 0 ?
                (presentationLoops ? Mathf.Repeat(presentationFrame, frames) : Mathf.Clamp(presentationFrame, 0, frames - 1)) :
                fighter.attack >= 0 && move != null
                ? Mathf.Clamp01((float)fighter.attackFrame / (move.startup + move.active + move.recovery)) * (frames - 1)
                : Mathf.Repeat((clock - stateStart) * 60, frames);
            int a = Mathf.Clamp((int)frame, 0, frames - 1), b = Mathf.Min(a + 1, frames - 1);
            float t = frame - a;
            // Combat stays in place. Original entrance tracks retain root motion to match their cameras.
            Vector3 root = Vector3.Lerp(positions[a], positions[b], t);
            if (presentationAction < 0 || !presentationRootMotion) { root.x = 0; root.y = 0; }
            bones[0].localPosition = root;
            for (int i = 0; i < bones.Length; i++)
                bones[i].localRotation = Quaternion.Slerp(rotations[a, i], rotations[b, i], t);
        }
    }
}
