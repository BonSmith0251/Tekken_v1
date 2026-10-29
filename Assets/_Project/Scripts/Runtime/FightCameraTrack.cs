using System;
using UnityEngine;

namespace Versus.Remaster
{
    /// <summary>
    /// An original look-at camera track (Rage Art cameras), exported by
    /// Tools/CommandGraph/export_fight_cameras.py from bank 4 of &lt;chr&gt;rotsm.arc.
    /// Channels: look-at point, camera position (millimetres; +z the fighter's forward, +x its
    /// left, +y up), lens (horizontal FOV radians) and the blend back to the gameplay camera.
    /// </summary>
    public sealed class FightCameraTrack
    {
        readonly Vector3[] targets, positions;
        readonly float[] horizontalFov, blend;
        public int Frames => targets.Length;

        public FightCameraTrack(string resource) : this(Load(resource), resource) { }

        static byte[] Load(string resource)
        {
            var asset = Resources.Load<TextAsset>("FightCameras/" + resource);
            if (!asset) throw new InvalidOperationException("Missing fight camera: " + resource);
            return asset.bytes;
        }

        public FightCameraTrack(byte[] data, string resource = "camera bytes")
        {
            if (data == null || data.Length < 32 || System.Text.Encoding.ASCII.GetString(data, 0, 4) != "T7CF")
                throw new InvalidOperationException("Invalid fight camera: " + resource);
            int frames = BitConverter.ToInt32(data, 4), channels = BitConverter.ToInt32(data, 8);
            if (frames < 1 || channels != 5 || BitConverter.ToInt32(data, 12) != 11 || BitConverter.ToInt32(data, 16) != 11 ||
                BitConverter.ToInt32(data, 20) != 15 || BitConverter.ToInt32(data, 24) != 3 ||
                BitConverter.ToInt32(data, 28) != 21 ||
                data.Length != 12 + channels * 4 + frames * channels * 12L)
                throw new InvalidOperationException("Unsupported fight camera layout: " + resource);
            for (int offset = 32; offset < data.Length; offset += 4)
            {
                float value = BitConverter.ToSingle(data, offset);
                if (float.IsNaN(value) || float.IsInfinity(value))
                    throw new InvalidOperationException("Nonfinite fight camera sample: " + resource);
            }
            targets = new Vector3[frames]; positions = new Vector3[frames];
            horizontalFov = new float[frames]; blend = new float[frames];
            int start = 12 + channels * 4;
            for (int f = 0; f < frames; f++)
            {
                int p = start + f * channels * 12;
                targets[f] = Read(data, p) * .001f;
                positions[f] = Read(data, p + 12) * .001f;
                horizontalFov[f] = Mathf.Clamp(BitConverter.ToSingle(data, p + 24) * Mathf.Rad2Deg, 5, 120);
                blend[f] = Mathf.Clamp01(BitConverter.ToSingle(data, p + 36));
            }
        }

        static Vector3 Read(byte[] data, int p) => new Vector3(
            BitConverter.ToSingle(data, p), BitConverter.ToSingle(data, p + 4), BitConverter.ToSingle(data, p + 8));

        /// <summary>Camera pose at a (fractional) frame, anchored at the fighter's floor position and facing.</summary>
        public void Sample(float frame, Vector3 anchor, Vector3 forward, out Vector3 position, out Vector3 target,
            out float fov, out float gameplayBlend)
        {
            float sample = Mathf.Clamp(frame, 0, Frames - 1);
            int a = (int)sample, b = Mathf.Min(a + 1, Frames - 1);
            float t = sample - a;
            forward.y = 0; forward = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
            var left = new Vector3(-forward.z, 0, forward.x);
            Vector3 World(Vector3 native) => anchor + forward * native.z + left * native.x + Vector3.up * native.y;
            target = World(Vector3.Lerp(targets[a], targets[b], t));
            position = World(Vector3.Lerp(positions[a], positions[b], t));
            fov = Mathf.Lerp(horizontalFov[a], horizontalFov[b], t);
            gameplayBlend = Mathf.Lerp(blend[a], blend[b], t);
        }
    }
}
