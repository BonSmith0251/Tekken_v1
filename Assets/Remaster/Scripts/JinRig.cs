using System;
using System.Collections.Generic;
using UnityEngine;

namespace Versus.Remaster
{
    public interface IFighterPresentation
    { void Render(FighterState fighter, MatchRules rules, float clock, bool moving); }

    /// <summary>New procedural motion on Jin's extracted bind skeleton and skin weights.</summary>
    public class JinRig : MonoBehaviour, IFighterPresentation
    {
        public JinRetargetMotion retarget;
        public FighterActionMotion actionMotion;
        public MoveSpec[] fighterMoves;
        Transform[] bones;
        Vector3[] positions;
        Quaternion[] rotations;
        Quaternion[] fingerPoses;
        bool[] fingerJoints;
        // Rigs differ in case (Xiaoyu: Base, HEAD), so bone lookups ignore it.
        readonly Dictionary<string, Transform> byName = new Dictionary<string, Transform>(System.StringComparer.OrdinalIgnoreCase);
        Transform pelvis;
        // Heading shown on screen: eases toward the fighter's facing so re-facing the opponent after
        // a long move, a cross-up or a hit turns the body instead of snapping it.
        Vector3 shownDir, shownAt;
        bool hasShownDir;
        Material[] materials;
        Color[] baseColors;
        void Awake() => Initialize();
        public void Initialize()
        {
            if (bones != null) return;
            var skeleton = transform.Find("Skeleton");
            if (!skeleton) throw new InvalidOperationException("Jin prefab has no skeleton");
            bones = skeleton.GetComponentsInChildren<Transform>();
            positions = new Vector3[bones.Length]; rotations = new Quaternion[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                byName[bones[i].name] = bones[i]; positions[i] = bones[i].localPosition; rotations[i] = bones[i].localRotation;
            }
            CacheFingerPoses();
            pelvis = byName["BASE"];
            RefreshMaterials();
        }
        // Call after swapping renderers (wardrobe items) so hit and guard tints reach the new pieces.
        public void RefreshMaterials()
        {
            var allMaterials = new List<Material>();
            foreach (var skin in GetComponentsInChildren<SkinnedMeshRenderer>()) allMaterials.AddRange(skin.materials);
            materials = allMaterials.ToArray(); baseColors = new Color[materials.Length];
            for (int i = 0; i < materials.Length; i++) baseColors[i] = materials[i].color;
        }
        Vector3 ShownDirection(FighterState f)
        {
            var target = new Vector3(f.dirX, 0, f.dirZ);
            var at = new Vector3(f.x, 0, f.z);
            // A new round or a staged cut places the fighter afresh: show its heading at once.
            if (!hasShownDir || (at - shownAt).sqrMagnitude > .25f || target.sqrMagnitude < 1e-6f) shownDir = target;
            else
            {
                // Ease the yaw about up only (a half turn must not tip the body through the vertical).
                float ease = 1 - Mathf.Exp(-Mathf.Max(0, Time.deltaTime) * 18);
                float yaw = Mathf.LerpAngle(Mathf.Atan2(shownDir.x, shownDir.z) * Mathf.Rad2Deg, Mathf.Atan2(target.x, target.z) * Mathf.Rad2Deg, ease);
                shownDir = new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad), 0, Mathf.Cos(yaw * Mathf.Deg2Rad));
            }
            hasShownDir = true; shownAt = at;
            return shownDir.sqrMagnitude > 1e-6f ? shownDir : Vector3.forward;
        }

        public void Render(FighterState f, MatchRules rules, float clock, bool moving)
        {
            Initialize();
            for (int i = 0; i < bones.Length; i++) { bones[i].localPosition = positions[i]; bones[i].localRotation = rotations[i]; }
            transform.position = new Vector3(f.x, 0, f.z);
            // Command battles face the opponent in 3D; the prototype faces along x.
            // The carried turn (backturned stance) rotates the body about up, positive to its right.
            transform.rotation = f.move >= 0 ? Quaternion.LookRotation(ShownDirection(f)) * Quaternion.Euler(0, f.turn, 0)
                                             : Quaternion.Euler(0, f.facing > 0 ? 90 : -90, 0);
            float action = 0;
            if (f.attack >= 0)
            {
                var move = fighterMoves != null ? fighterMoves[f.attack] : rules.moves[f.attack];
                float wind = Mathf.Clamp01((float)f.attackFrame / move.startup);
                float recover = Mathf.Clamp01((float)(f.attackFrame - move.startup - move.active) / move.recovery);
                action = Mathf.SmoothStep(0, 1, wind) * (1 - Mathf.SmoothStep(0, 1, recover));
            }
            float crouch = f.crouch ? .33f : .1f;
            pelvis.position += transform.up * (-crouch + Mathf.Sin(clock * 5) * .012f);
            pelvis.position += transform.forward * (f.attack == 1 ? action * .22f : f.attack == 0 ? action * .15f : 0);
            var spine = byName["Spine1"];
            spine.rotation = Quaternion.AngleAxis(f.stun > 0 ? -14 : f.crouch ? 13 : 4, transform.right) * spine.rotation;
            float gait = moving && f.attack < 0 && f.stun == 0 ? Mathf.Sin(clock * 10) : 0;
            Vector3 leftHand = new Vector3(-.25f, 1.5f - crouch, .35f);
            Vector3 rightHand = new Vector3(.25f, 1.55f - crouch, .23f);
            if (f.guard) { leftHand = new Vector3(-.19f, 1.68f - crouch, .22f); rightHand = new Vector3(.19f, 1.68f - crouch, .25f); }
            if (f.attack == 0) leftHand = Vector3.Lerp(leftHand, new Vector3(-.1f, 1.5f, 1.05f), action);
            if (f.attack == 1) rightHand = Vector3.Lerp(rightHand, new Vector3(.05f, 1.38f, 1.1f), action);
            Solve("L_Arm", "L_ForeArm", "L_Hand", leftHand, new Vector3(-.7f, 1.15f - crouch, .05f));
            Solve("R_Arm", "R_ForeArm", "R_Hand", rightHand, new Vector3(.7f, 1.15f - crouch, -.1f));
            Vector3 leftFoot = new Vector3(-.23f, .14f + Mathf.Max(0, gait) * .09f, .18f + gait * .2f);
            Vector3 rightFoot = new Vector3(.24f, .14f + Mathf.Max(0, -gait) * .09f, -.24f - gait * .2f);
            if (f.attack == 2) leftFoot = Vector3.Lerp(leftFoot, new Vector3(-.25f, .35f, 1.05f), action);
            if (f.attack == 3) rightFoot = Vector3.Lerp(rightFoot, new Vector3(.12f, 1.1f, 1.15f), action);
            Solve("L_UpLeg", "L_Leg", "L_Foot", leftFoot, new Vector3(-.3f, .6f, 1));
            Solve("R_UpLeg", "R_Leg", "R_Foot", rightFoot, new Vector3(.3f, .6f, 1));
            // Absolute bind-relative poses: no sideways curl or accumulated rotation.
            for (int i = 0; i < bones.Length; i++)
                if (fingerJoints[i]) bones[i].localRotation = fingerPoses[i];
            // Command battles play the original knockdown reaction instead of this tilt.
            if (f.health == 0 && f.move < 0)
            { transform.position += Vector3.up * .18f; transform.rotation *= Quaternion.Euler(-83, 0, 0); }
            for (int i = 0; i < materials.Length; i++) materials[i].color = f.flash > 0 ? Color.Lerp(baseColors[i], Color.white, .75f) : baseColors[i];
            if(actionMotion)actionMotion.Apply(f, f.attack >= 0 ? fighterMoves[f.attack] : null, clock, moving);
            else if(retarget)retarget.Apply(f,rules,clock,moving);
        }
        void CacheFingerPoses()
        {
            fingerPoses = new Quaternion[bones.Length];
            fingerJoints = new bool[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                string name = bones[i].name;
                bool left = name.StartsWith("L_", StringComparison.Ordinal);
                if (!left && !name.StartsWith("R_", StringComparison.Ordinal)) continue;
                string digit = name.Substring(2);
                if (digit.Length < 2) continue;
                int joint = digit[digit.Length - 1] - '0';
                if (joint < 1 || joint > 3) continue;
                digit = digit.Substring(0, digit.Length - 1);
                bool thumb = digit == "Thumb";
                if (!thumb && digit != "Index" && digit != "Middle" && digit != "Ring" && digit != "Pinky") continue;
                if (!byName.TryGetValue(left ? "L_Hand" : "R_Hand", out var hand)) continue;
                // Fingers extend along local -X and lie in the hand's XY plane.
                // Curl toward palm +Z on the left, -Z on the right, not sideways
                // around Z. Convert the hand hinge into each joint's bind basis;
                // this also respects the thumb's different bind orientation.
                Vector3 hinge = bones[i].InverseTransformDirection(
                    hand.TransformDirection(left ? Vector3.up : Vector3.down));
                float angle = thumb ? (joint == 1 ? 20 : joint == 2 ? 25 : 15)
                    : (joint == 1 ? 55 : joint == 2 ? 75 : 45);
                fingerJoints[i] = true;
                fingerPoses[i] = rotations[i] * Quaternion.AngleAxis(angle, hinge.normalized);
            }
        }

        void Solve(string upperName, string middleName, string endName, Vector3 localTarget, Vector3 localPole)
        {
            Transform upper = byName[upperName], middle = byName[middleName], end = byName[endName];
            Quaternion restEnd = end.rotation;
            Vector3 a = upper.position, b = middle.position, c = end.position;
            float ab = Vector3.Distance(a,b), bc = Vector3.Distance(b,c);
            Vector3 target = transform.TransformPoint(localTarget);
            Vector3 direction = (target - a).normalized;
            float distance = Mathf.Clamp(Vector3.Distance(a,target), Mathf.Abs(ab-bc)+.001f, ab+bc-.001f);
            target = a + direction * distance;
            Vector3 pole = transform.TransformPoint(localPole)-a;
            Vector3 bend = (pole - Vector3.Dot(pole,direction)*direction).normalized;
            float along = (ab*ab + distance*distance - bc*bc)/(2*distance);
            Vector3 elbow = a + direction*along + bend*Mathf.Sqrt(Mathf.Max(0,ab*ab-along*along));
            upper.rotation = Quaternion.FromToRotation(b-a,elbow-a)*upper.rotation;
            middle.rotation = Quaternion.FromToRotation(end.position-middle.position,target-middle.position)*middle.rotation;
            if(endName.EndsWith("Foot")) end.rotation=restEnd;
        }
    }
}
