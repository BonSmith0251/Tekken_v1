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

        public EntranceCameraTrack(string resource, int moveId, int bodyFrames)
        {
            cuts = resource == "Kazuya_s03" ? new[] { 47, 88 } :
                resource == "Jin_s02" ? new[] { 123 } : new int[0];
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
        }

        static Vector3 ReadVector(byte[] data, int p) => new Vector3(
            BitConverter.ToSingle(data, p), BitConverter.ToSingle(data, p + 4), BitConverter.ToSingle(data, p + 8));

        static Vector3 RigBasis(Vector3 native) => new Vector3(-native.z, native.y, -native.x);

        public void Apply(Camera camera, Transform actor, float frame, EntranceCameraLens lens, Bounds focus,
            Vector3 faceForward, Func<int, int> frameMap = null)
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
            if (next > f + 1 || Array.IndexOf(cuts, next) >= 0) blend = 0;
            Vector3 nativePosition = actor.TransformPoint(Vector3.Lerp(positions[f], positions[next], blend));
            Quaternion rotation = Quaternion.Slerp(Quaternion.LookRotation(forwards[f], ups[f]),
                Quaternion.LookRotation(forwards[next], ups[next]), blend);
            lens.SetHorizontalFov(Mathf.Lerp(horizontalFov[f], horizontalFov[next], blend));
            // Retarget the shot to the anatomical front of the animated head. The
            // imported rig's bone axes are not Unity's forward axis; the caller
            // supplies the bind-pose-corrected direction. Keep a slight three-quarter view.
            Vector3 front = Vector3.ProjectOnPlane(faceForward, Vector3.up);
            if (front.sqrMagnitude < .001f) front = actor.forward;
            front = Quaternion.AngleAxis(12, Vector3.up) * front.normalized;
            Vector3 nativeBack = -(actor.rotation * rotation * Vector3.forward);
            float elevation = Mathf.Clamp(nativeBack.y, -.25f, .35f);
            Vector3 offset = (front + Vector3.up * elevation).normalized;
            float distance = Mathf.Max(.5f, Vector3.Distance(nativePosition, focus.center));
            camera.transform.SetPositionAndRotation(focus.center + offset * distance,
                Quaternion.LookRotation(-offset, Vector3.up));
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
