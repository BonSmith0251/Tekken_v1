using System;
using System.Collections.Generic;
using UnityEngine;

namespace Versus.Remaster
{
    public sealed class FighterActionMotion : MonoBehaviour
    {
        FighterActionCatalog catalog, currentSource;
        /// <summary>The opponent's catalog: hit reactions borrowed from its moveset may use its own clips.</summary>
        public FighterActionCatalog opponent;
        Transform[] bones;
        Vector3[] positions;
        Quaternion[,] rotations;
        int frames, current = -1, lastAttackFrame = -1, lastMoveFrame = -1;
        int presentationAction = -1;
        float presentationFrame;
        bool presentationLoops;
        bool presentationRootMotion;
        Func<int, int> presentationFrameMap;
        float stateStart;
        SecondaryMotionTrack combatHands;
        int combatHandFrames;
        public int PlayedActions { get; private set; }
        // Move changes in command battles blend from the last shown pose over a few frames, as the
        // original animation system does, so one-frame transitional states never pop.
        const float CrossfadeFrames = 5;
        Quaternion[] shownPose, fadeFrom;
        Vector3 shownRoot, fadeRoot;
        float fadeClock = -1;
        public int CurrentAction => current;
        public bool Ready => catalog != null && bones != null;

        public void SetPresentation(int action, float frame, bool loop, bool rootMotion = false, Func<int, int> frameMap = null)
        {
            presentationAction = action;
            presentationFrame = frame;
            presentationLoops = loop;
            presentationRootMotion = rootMotion;
            presentationFrameMap = frameMap;
        }

        public void Initialize(FighterActionCatalog source)
        {
            catalog = source;
            var map = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
            foreach (var bone in GetComponentsInChildren<Transform>()) map[bone.name] = bone;
            bones = new Transform[source.character.boneNames.Length];
            for (int i = 0; i < bones.Length; i++)
                if (!map.TryGetValue(source.character.boneNames[i], out bones[i]))
                    throw new InvalidOperationException("Missing " + source.character.boneNames[i] + " on " + source.character.displayName);
            // Native stance hands by character code; fighters without a baked track keep the body clip's hands.
            // Jin's and Kazuya's museum assets predate characterCode.
            string code = !string.IsNullOrEmpty(source.character.characterCode) ? source.character.characterCode :
                source.character.displayName.StartsWith("Jin", StringComparison.Ordinal) ? "jin" : "kaz";
            string handResource = code + "_combat_hands";
            var handAsset = Resources.Load<TextAsset>("SecondaryMotion/" + handResource);
            if (handAsset)
            {
                combatHandFrames = BitConverter.ToInt32(handAsset.bytes, 4);
                combatHands = new SecondaryMotionTrack(transform, handResource, combatHandFrames);
            }
            Load(source, source.idle);
        }

        void Load(FighterActionCatalog source, int index)
        {
            var asset = source.character.actions[index].motion;
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
            current = index; currentSource = source;
        }

        public void Apply(FighterState fighter, MoveSpec move, float clock, bool moving)
        {
            if (!Ready) return;
            // Command battles name the original move's clip and frame; otherwise the prototype attack.
            // A reaction borrowed from the opponent's moveset uses this rig's conversion of shared
            // common clips, and the opponent's conversion of its own character clips (same 19 bones).
            var source = catalog;
            int commandAction = -1;
            if (presentationAction < 0 && fighter.move >= 0)
            {
                bool shared = fighter.clipKey != null && fighter.clipKey.StartsWith("common/", StringComparison.Ordinal);
                if (fighter.borrowed && !shared && opponent != null) source = opponent;
                commandAction = source.ActionForClip(fighter.clipKey);
                if (commandAction < 0 && !fighter.borrowed) commandAction = catalog.ActionForMove(fighter.move);
                if (commandAction < 0) source = catalog;
            }
            int target = presentationAction >= 0 ? presentationAction :
                fighter.move >= 0 ? (commandAction >= 0 ? commandAction : current >= 0 ? current : catalog.idle) :
                fighter.attack >= 0 ? fighter.attack : catalog.idle;
            if (fighter.move >= 0 && commandAction < 0 && presentationAction < 0) source = currentSource ?? catalog;
            if (target != current || source != currentSource || (fighter.attack >= 0 && fighter.attackFrame < lastAttackFrame) ||
                (commandAction >= 0 && fighter.moveFrame < lastMoveFrame))
            {
                // Blend fight moves and the switch into a victory take; entrance shots start on a cut.
                if (shownPose != null && (fighter.move >= 0 || presentationAction >= 0) && presentationFrameMap == null)
                {
                    if (fadeFrom == null) fadeFrom = new Quaternion[shownPose.Length];
                    Array.Copy(shownPose, fadeFrom, shownPose.Length); fadeRoot = shownRoot; fadeClock = clock;
                }
                else fadeClock = -1;
                Load(source, target); stateStart = clock;
                if (fighter.attack >= 0 || (commandAction >= 0 && target != catalog.idle)) PlayedActions++;
            }
            lastAttackFrame = fighter.attackFrame;
            lastMoveFrame = fighter.moveFrame;
            float frame = presentationAction >= 0 ?
                (presentationLoops ? Mathf.Repeat(presentationFrame, frames) : Mathf.Clamp(presentationFrame, 0, frames - 1)) :
                commandAction >= 0 ? Mathf.Clamp(fighter.moveFrame - 1, 0, frames - 1) :
                fighter.move >= 0 ? Mathf.Repeat((clock - stateStart) * 60, frames) :
                fighter.attack >= 0 && move != null
                ? Mathf.Clamp01((float)fighter.attackFrame / (move.startup + move.active + move.recovery)) * (frames - 1)
                : Mathf.Repeat((clock - stateStart) * 60, frames);
            int a = Mathf.Clamp((int)frame, 0, frames - 1), b = Mathf.Min(a + 1, frames - 1);
            float t = frame - a;
            int stitchFrom = -1;
            float stitchBlend = 1;
            if (presentationAction >= 0 && presentationFrameMap != null)
            {
                // Ease joint rotations across edited shot joins; root translation keeps
                // descending along the stitched trajectory without replaying the fall.
                for (int cut = Mathf.Max(1, a - 5); cut <= a + 1; cut++)
                    if (presentationFrameMap(cut) > presentationFrameMap(cut - 1) + 1)
                    {
                        stitchFrom = Mathf.Clamp(presentationFrameMap(cut - 1), 0, frames - 1);
                        stitchBlend = Mathf.SmoothStep(0, 1, (frame - (cut - 1)) / 6f);
                    }
                // Map endpoints separately so interpolation never visits removed overlap frames.
                a = Mathf.Clamp(presentationFrameMap(a), 0, frames - 1);
                b = Mathf.Clamp(presentationFrameMap(b), 0, frames - 1);
            }
            // Restore authored stance hands after JinRig's generic fallback. Entrance
            // secondary tracks override this later; attacks use the stance's neutral
            // hand pose until their own per-move secondary events are implemented.
            combatHands?.Apply(target == catalog.idle && currentSource == catalog ? Mathf.Repeat(frame, combatHandFrames) : 0);
            // Combat stays in place. Original entrance tracks retain root motion to match their cameras.
            Vector3 root = Vector3.Lerp(positions[a], positions[b], t);
            if (presentationAction < 0 || !presentationRootMotion) { root.x = 0; root.y = 0; }
            else
            {
                // A clip's root travel is forward along local -x and left along +y, but under the rig's
                // BODY_SCALE group those axes land 90 degrees off the animated body (which faces model +z).
                // Map the travel into the body frame: -x forward -> +y, +y left -> -x (measured on the
                // converted walk and victory clips: planted-foot slide drops from metres to centimetres).
                float dx = root.x - positions[0].x, dy = root.y - positions[0].y;
                root.x = positions[0].x - dy; root.y = positions[0].y - dx;
            }
            float fade = fadeClock < 0 ? 1 : Mathf.SmoothStep(0, 1, (clock - fadeClock) * 60 / CrossfadeFrames);
            if (fade >= 1) fadeClock = -1;
            if (fade < 1) root = Vector3.Lerp(fadeRoot, root, fade);
            bones[0].localPosition = root;
            if (shownPose == null) shownPose = new Quaternion[bones.Length];
            shownRoot = root;
            for (int i = 0; i < bones.Length; i++)
            {
                Quaternion pose = Quaternion.Slerp(rotations[a, i], rotations[b, i], t);
                if (stitchFrom >= 0) pose = Quaternion.Slerp(rotations[stitchFrom, i], pose, stitchBlend);
                if (fade < 1) pose = Quaternion.Slerp(fadeFrom[i], pose, fade);
                bones[i].localRotation = shownPose[i] = pose;
            }
        }
    }
}
