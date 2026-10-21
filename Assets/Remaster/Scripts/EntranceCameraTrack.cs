using System;
using UnityEngine;

namespace Versus.Remaster
{
    // Native MOTA camera samples. This basis matches combat_basis.py's heading @ align.
    public sealed class EntranceCameraTrack
    {
        readonly Vector3[] positions, forwards, ups;
        readonly float[] horizontalFov;
        public int Frames => positions.Length;

        public EntranceCameraTrack(string resource, int moveId, int bodyFrames)
        {
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
                Quaternion rotation = Quaternion.AngleAxis(euler.z, Vector3.forward) *
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

        static Vector3 RigBasis(Vector3 native) => new Vector3(native.z, native.y, native.x);

        public void Apply(Camera camera, Transform actor, int frame, EntranceCameraLens lens)
        {
            // Frame sampling preserves original cuts; smoothing would sweep through them.
            int f = Mathf.Clamp(frame, 0, Frames - 1);
            camera.transform.SetPositionAndRotation(actor.TransformPoint(positions[f]),
                Quaternion.LookRotation(actor.TransformDirection(forwards[f]), actor.TransformDirection(ups[f])));
            lens.SetHorizontalFov(horizontalFov[f]);
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
            target.y = .95f;
            // Keep the outer body/guard silhouette within 85% of the viewport width.
            float halfWidth = Mathf.Abs(one.x - two.x) * .5f + .65f;
            float horizontalDistance = halfWidth / (Mathf.Tan(HorizontalFov * .5f * Mathf.Deg2Rad) * .85f);
            float distance = Mathf.Max(3.25f, horizontalDistance) + Mathf.Abs(one.z - two.z) * .5f;
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
