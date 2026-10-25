using System;
using UnityEngine;

namespace Versus.Remaster
{
    // Native MOTA camera samples converted into the imported actor's local space.
    // Native timing and lens samples, composed for the retargeted actors.
    public sealed class EntranceCameraTrack
    {
        readonly Vector3[] positions, forwards, ups;
        readonly float[] horizontalFov;
        readonly int[] cuts;
        public int Frames => positions.Length;
        float lastFrame = -1, smoothedFov, smoothedRoll;
        float smoothedDistance;
        Vector3 smoothedDirection;

        public EntranceCameraTrack(string resource, int moveId, int bodyFrames)
        {
            cuts = (resource == "Kazuya_s03" || resource == "kaz_staging_2326") ? new[] { 47, 88 } :
                (resource == "Jin_s02" || resource == "jin_staging_2220") ? new[] { 123 } : new int[0];
            var asset = Resources.Load<TextAsset>("EntranceCameras/" + resource);
            if (!asset) throw new InvalidOperationException("Missing entrance camera: " + resource);
            byte[] data = asset.bytes;
            if (data.Length < 20 || System.Text.Encoding.ASCII.GetString(data, 0, 4) != "T7C1")
                throw new InvalidOperationException("Invalid entrance camera: " + resource);
            int frames = BitConverter.ToInt32(data, 4);
            if (frames != bodyFrames || BitConverter.ToInt32(data, 8) != 60 ||
                BitConverter.ToInt32(data, 12) != moveId || data.Length != 20L + frames * 48L)
                throw new InvalidOperationException("Entrance camera does not match its body action: " + resource);
            positions = new Vector3[frames]; forwards = new Vector3[frames]; ups = new Vector3[frames];
            horizontalFov = new float[frames];
            for (int f = 0; f < frames; f++)
            {
                int p = 20 + f * 48;
                positions[f] = RigBasis(ReadVector(data, p)) * .001f;
                Vector3 euler = ReadVector(data, p + 12) * Mathf.Rad2Deg;
                // Native camera uses XYZ Euler rotations and looks along negative Z.
                Quaternion rotation = Quaternion.AngleAxis(-euler.z, Vector3.forward) *
                    Quaternion.AngleAxis(euler.y, Vector3.up) * Quaternion.AngleAxis(euler.x, Vector3.right);
                forwards[f] = RigBasis(rotation * Vector3.back);
                ups[f] = RigBasis(rotation * Vector3.up);
                horizontalFov[f] = BitConverter.ToSingle(data, p + 24) * Mathf.Rad2Deg;
                if (float.IsNaN(horizontalFov[f]) || horizontalFov[f] <= 0 || horizontalFov[f] >= 179)
                    throw new InvalidOperationException("Invalid entrance FOV: " + resource);
            }
            BlendSourceEdits();
        }

        void BlendSourceEdits()
        {
            // Replace within-action camera cuts with a short, zero-slope blend.
            // Blend before and after each edit so the camera does not chase a jump.
            var sourcePositions = (Vector3[])positions.Clone();
            var sourceForwards = (Vector3[])forwards.Clone();
            var sourceUps = (Vector3[])ups.Clone();
            var sourceFov = (float[])horizontalFov.Clone();
            int lastCut = -20;
            for (int cut = 1; cut < Frames; cut++)
            {
                bool discontinuity = Array.IndexOf(cuts, cut) >= 0 ||
                    Vector3.Distance(sourcePositions[cut-1], sourcePositions[cut]) > .75f ||
                    Mathf.Abs(sourceFov[cut]-sourceFov[cut-1]) > 4 ||
                    Vector3.Angle(sourceForwards[cut-1], sourceForwards[cut]) > 35;
                if (!discontinuity || cut-lastCut < 12) continue;
                lastCut = cut;
                int first = Mathf.Max(0, cut-6), last = Mathf.Min(Frames-1, cut+9);
                var from = Quaternion.LookRotation(sourceForwards[first], sourceUps[first]);
                var to = Quaternion.LookRotation(sourceForwards[last], sourceUps[last]);
                for (int f = first; f <= last; f++)
                {
                    float t = Mathf.SmoothStep(0, 1, (f-first)/(float)(last-first));
                    positions[f] = Vector3.Lerp(sourcePositions[first], sourcePositions[last], t);
                    var pose = Quaternion.Slerp(from, to, t);
                    forwards[f] = pose * Vector3.forward; ups[f] = pose * Vector3.up;
                    horizontalFov[f] = Mathf.Lerp(sourceFov[first], sourceFov[last], t);
                }
            }
        }

        static Vector3 ReadVector(byte[] data, int p) => new Vector3(
            BitConverter.ToSingle(data, p), BitConverter.ToSingle(data, p + 4), BitConverter.ToSingle(data, p + 8));

        static Vector3 RigBasis(Vector3 native) => new Vector3(-native.z, native.y, -native.x);

        public void Apply(Camera camera, Transform actor, float frame, EntranceCameraLens lens, Bounds focus,
            Vector3 faceForward, Func<int, int> frameMap = null, Vector3? headPosition = null)
        {
            float sample = Mathf.Clamp(frame, 0, Frames - 1);
            int f = (int)sample, next = Mathf.Min(f + 1, Frames - 1);
            float blend = sample - f;
            if (frameMap != null)
            {
                f = Mathf.Clamp(frameMap(f), 0, Frames - 1);
                next = Mathf.Clamp(frameMap(next), 0, Frames - 1);
            }
            // A removed overlap is also a camera cut, never a sweep through discarded samples.
            if (next > f + 1) blend = 0;
            Vector3 nativePosition = actor.TransformPoint(Vector3.Lerp(positions[f], positions[next], blend));
            Quaternion rotation = Quaternion.Slerp(Quaternion.LookRotation(forwards[f], ups[f]),
                Quaternion.LookRotation(forwards[next], ups[next]), blend);
            lens.SetHorizontalFov(Mathf.Lerp(horizontalFov[f], horizontalFov[next], blend));
            // Preserve the take's shot scale: a native close-up must not be
            // expanded to a full-body shot by the shared framing safety bounds.
            if (headPosition.HasValue)
            {
                float sourceDistance = Vector3.Distance(nativePosition, headPosition.Value);
                float height = Mathf.Clamp(2 * sourceDistance * Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad) * .64f, .5f, Mathf.Max(.5f, focus.size.y));
                float crop = Mathf.InverseLerp(focus.size.y, .5f, height);
                Vector3 cropCenter = Vector3.Lerp(focus.center, headPosition.Value, crop);
                focus = new Bounds(cropCenter, new Vector3(Mathf.Lerp(focus.size.x, .5f, crop), height, Mathf.Lerp(focus.size.z, .44f, crop)));
                focus.Encapsulate(new Bounds(headPosition.Value + Vector3.up * .08f, Vector3.one * .4f));
            }
            // Keep this action's native lateral travel and height. Reflect only
            // the depth component when it would put the camera behind the face;
            // a shared frontal azimuth erased the differences between source takes.
            Vector3 front = Vector3.ProjectOnPlane(faceForward, Vector3.up);
            if (front.sqrMagnitude < .001f) front = actor.forward;
            front.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, front).normalized;
            Vector3 nativeOffset = nativePosition - focus.center;
            float depth = Mathf.Abs(Vector3.Dot(nativeOffset, front));
            float lateral = Vector3.Dot(nativeOffset, right);
            // Keep a readable three-quarter/profile limit without flattening the
            // native camera's changing side, orbit, distance or elevation.
            depth = Mathf.Max(depth, Mathf.Abs(lateral) / Mathf.Tan(75 * Mathf.Deg2Rad), .15f);
            Vector3 adaptedOffset = right * lateral + front * depth + Vector3.up * nativeOffset.y;
            float distance = Mathf.Max(.5f, adaptedOffset.magnitude);
            Vector3 offset = adaptedOffset.normalized;
            Vector3 nativeUp = actor.rotation * rotation * Vector3.up;
            Vector3 shotUp = Vector3.ProjectOnPlane(nativeUp, -offset);
            float roll = shotUp.sqrMagnitude > .001f
                ? Mathf.Clamp(Vector3.SignedAngle(Vector3.ProjectOnPlane(Vector3.up, -offset), shotUp, -offset), -12, 12) : 0;
            float deltaFrames = frame - lastFrame;
            if (lastFrame < 0 || deltaFrames < 0 || deltaFrames > 30)
            {
                smoothedDistance = distance; smoothedDirection = offset;
                smoothedFov = camera.fieldOfView; smoothedRoll = roll;
            }
            else
            {
                float weight = 1 - Mathf.Exp(-Mathf.Max(0, deltaFrames) / 9f);
                smoothedDistance = Mathf.Lerp(smoothedDistance, distance, weight);
                smoothedRoll = Mathf.LerpAngle(smoothedRoll, roll, weight);
                smoothedDirection = Vector3.RotateTowards(smoothedDirection,
                    Vector3.Slerp(smoothedDirection, offset, weight), 2.2f * Mathf.Deg2Rad * deltaFrames, 0).normalized;
                smoothedFov = Mathf.MoveTowards(smoothedFov,
                    Mathf.Lerp(smoothedFov, camera.fieldOfView, weight), .8f * deltaFrames);
            }
            lastFrame = frame;
            lens.SetVerticalFov(smoothedFov);
            camera.transform.SetPositionAndRotation(focus.center + smoothedDirection * smoothedDistance,
                Quaternion.LookRotation(-smoothedDirection, Vector3.up) * Quaternion.AngleAxis(smoothedRoll, Vector3.forward));
            // Frame the retargeted subject, including head volume rather than only
            // its neck-level pivot. Native cuts and FOV remain intact.
            float tanY = Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad);
            float tanX = tanY * camera.aspect;
            Vector3 center = camera.transform.InverseTransformPoint(focus.center);
            camera.transform.position += camera.transform.right * center.x + camera.transform.up * center.y;
            float retreat = 0;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = focus.center + Vector3.Scale(focus.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 local = camera.transform.InverseTransformPoint(corner);
                float required = Mathf.Max(camera.nearClipPlane + .1f,
                    Mathf.Abs(local.x) / (tanX * .76f), Mathf.Abs(local.y) / (tanY * .64f));
                retreat = Mathf.Max(retreat, required - local.z);
            }
            if (retreat > 0) camera.transform.position -= camera.transform.forward * (retreat + .02f);
            if (camera.transform.position.y < .35f)
            {
                // A low, upward-looking source shot cannot dolly through the Unity stage.
                // Clamp continuously at the floor; snapping up to subject height causes a jerk.
                Vector3 position = camera.transform.position;
                position.y = .35f;
                camera.transform.position = position;
                Vector3 direction = focus.center - position;
                if (direction.sqrMagnitude > .0001f)
                    camera.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            }
        }
    }

    // Reference-video framing: a near-level camera, with room for both fighters.
    // Distances are calibrated to the imported rigs, not recovered engine constants.
    public static class GameplayCameraFraming
    {
        public const float HorizontalFov = 65f;
        /// <summary>Frames both fighters from the side of their line; screenRight is that line's screen-right direction.</summary>
        public static void Evaluate(Vector3 one, Vector3 two, Vector3 screenRight, out Vector3 position, out Vector3 target)
        {
            screenRight.y = 0;
            if (screenRight.sqrMagnitude < 1e-6f) screenRight = Vector3.right;
            screenRight.Normalize();
            var forward = Vector3.Cross(screenRight, Vector3.up);
            target = (one + two) * .5f;
            target.y = 1.08f;
            var offset = two - one; offset.y = 0;
            float along = Mathf.Abs(Vector3.Dot(offset, screenRight)), depth = Mathf.Abs(Vector3.Dot(offset, forward));
            float halfWidth = along * .5f + .65f;
            float horizontalDistance = halfWidth / (Mathf.Tan(HorizontalFov * .5f * Mathf.Deg2Rad) * .90f);
            float distance = Mathf.Max(3.45f, horizontalDistance) + depth * .5f;
            position = target - forward * distance;
        }

        public static void Evaluate(Vector3 one, Vector3 two, out Vector3 position, out Vector3 target)
        {
            target = (one + two) * .5f;
            target.y = 1.08f;
            // Keep the outer body/guard silhouette within 90% of the viewport width.
            float halfWidth = Mathf.Abs(one.x - two.x) * .5f + .65f;
            float horizontalDistance = halfWidth / (Mathf.Tan(HorizontalFov * .5f * Mathf.Deg2Rad) * .90f);
            float distance = Mathf.Max(3.45f, horizontalDistance) + Mathf.Abs(one.z - two.z) * .5f;
            position = target + Vector3.back * distance;
        }
    }

    // Source tracks store angular FOV, not filmback dimensions. 36 mm is an
    // equivalent-lens convention; the reference composition uses a 16:9 gate.
    public sealed class EntranceCameraLens
    {
        public const float ReferenceAspect = 16f / 9f;
        public static readonly Vector2 Sensor = new Vector2(36, 36 / ReferenceAspect);
        readonly Camera camera;
        readonly Rect originalRect;
        readonly bool originalPhysical;
        readonly Vector2 originalSensor, originalShift;
        readonly Camera.GateFitMode originalGateFit;
        readonly float originalFov, originalFocalLength;

        public EntranceCameraLens(Camera camera)
        {
            this.camera = camera;
            originalRect = camera.rect;
            originalPhysical = camera.usePhysicalProperties;
            originalSensor = camera.sensorSize; originalShift = camera.lensShift;
            originalGateFit = camera.gateFit;
            originalFov = camera.fieldOfView; originalFocalLength = camera.focalLength;
        }

        void Configure()
        {
            float width = camera.targetTexture ? camera.targetTexture.width : Screen.width;
            float height = camera.targetTexture ? camera.targetTexture.height : Screen.height;
            float aspect = width * originalRect.width / Mathf.Max(1, height * originalRect.height);
            Rect rect = originalRect;
            if (aspect > ReferenceAspect)
            {
                rect.width *= ReferenceAspect / aspect;
                rect.x += (originalRect.width - rect.width) * .5f;
            }
            else
            {
                rect.height *= aspect / ReferenceAspect;
                rect.y += (originalRect.height - rect.height) * .5f;
            }
            camera.rect = rect;
            camera.ResetAspect();
            camera.usePhysicalProperties = true;
            camera.sensorSize = Sensor;
            camera.lensShift = Vector2.zero;
            camera.gateFit = Camera.GateFitMode.None;
        }

        public void SetHorizontalFov(float degrees)
        {
            Configure();
            camera.focalLength = Camera.FieldOfViewToFocalLength(degrees, Sensor.x);
        }

        public void SetVerticalFov(float degrees)
        {
            Configure();
            camera.focalLength = Camera.FieldOfViewToFocalLength(degrees, Sensor.y);
        }

        public void Restore()
        {
            camera.rect = originalRect;
            camera.ResetAspect();
            camera.sensorSize = originalSensor; camera.lensShift = originalShift;
            camera.gateFit = originalGateFit;
            camera.usePhysicalProperties = originalPhysical;
            if (originalPhysical) camera.focalLength = originalFocalLength;
            else camera.fieldOfView = originalFov;
        }
    }
}
