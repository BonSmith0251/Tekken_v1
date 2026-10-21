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

        public void Apply(Camera camera, Transform actor, int frame)
        {
            // Frame sampling preserves original cuts; smoothing would sweep through them.
            int f = Mathf.Clamp(frame, 0, Frames - 1);
            camera.transform.SetPositionAndRotation(actor.TransformPoint(positions[f]),
                Quaternion.LookRotation(actor.TransformDirection(forwards[f]), actor.TransformDirection(ups[f])));
            camera.fieldOfView = Camera.HorizontalToVerticalFieldOfView(horizontalFov[f], camera.aspect);
        }
    }
}
