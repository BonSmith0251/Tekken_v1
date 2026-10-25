using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Versus.Remaster
{
    // Original entrance hand and facial motion, stored relative to the extracted bind pose.
    public sealed class SecondaryMotionTrack
    {
        readonly Transform[] bones;
        readonly Vector3[] bindPositions, bindScales;
        readonly Quaternion[] bindRotations;
        readonly Vector3[,] positions, scales;
        readonly Quaternion[,] rotations;
        readonly int frames;

        public SecondaryMotionTrack(Transform actor, string resource, int expectedFrames)
        {
            var asset = Resources.Load<TextAsset>("SecondaryMotion/" + resource);
            if (!asset) throw new InvalidOperationException("Missing secondary motion: " + resource);
            var map = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
            foreach (var bone in actor.GetComponentsInChildren<Transform>()) map[bone.name] = bone;
            using (var reader = new BinaryReader(new MemoryStream(asset.bytes), Encoding.UTF8))
            {
                if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "SMT1")
                    throw new InvalidOperationException("Invalid secondary motion: " + resource);
                frames = reader.ReadInt32(); int count = reader.ReadInt32(); int rate = reader.ReadInt32();
                if (frames != expectedFrames || rate != 60 || count < 1 || count > 256)
                    throw new InvalidOperationException("Secondary motion/body mismatch: " + resource);
                bones = new Transform[count]; bindPositions = new Vector3[count]; bindScales = new Vector3[count];
                bindRotations = new Quaternion[count];
                for (int i = 0; i < count; i++)
                {
                    int length = reader.ReadInt32();
                    if (length < 1 || length > 128) throw new InvalidOperationException("Invalid secondary bone name");
                    string name = Encoding.UTF8.GetString(reader.ReadBytes(length));
                    if (!map.TryGetValue(name, out bones[i]))
                        throw new InvalidOperationException("Missing secondary bone " + name + " on " + actor.name);
                    bindPositions[i] = bones[i].localPosition; bindRotations[i] = bones[i].localRotation;
                    bindScales[i] = bones[i].localScale;
                }
                if (reader.BaseStream.Length - reader.BaseStream.Position != (long)frames * count * 40)
                    throw new InvalidOperationException("Truncated secondary motion: " + resource);
                positions = new Vector3[frames, count]; rotations = new Quaternion[frames, count]; scales = new Vector3[frames, count];
                for (int f = 0; f < frames; f++) for (int i = 0; i < count; i++)
                {
                    positions[f, i] = ReadVector(reader);
                    rotations[f, i] = new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    scales[f, i] = ReadVector(reader);
                }
            }
        }

        static Vector3 ReadVector(BinaryReader reader) => new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

        public void Apply(float frame, Func<int, int> frameMap = null)
        {
            frame = Mathf.Clamp(frame, 0, frames - 1);
            int a = (int)frame, b = a + 1; float t = frame - a;
            int stitch = -1; float blend = 1;
            if (frameMap != null)
            {
                // Match the body's six-frame pose blend at Kazuya's removed shot overlaps.
                for (int cut = Mathf.Max(1, a - 5); cut <= a + 1; cut++)
                    if (frameMap(cut) > frameMap(cut - 1) + 1)
                    {
                        stitch = Mathf.Clamp(frameMap(cut - 1), 0, frames - 1);
                        blend = Mathf.SmoothStep(0, 1, (frame - (cut - 1)) / 6f);
                    }
                a = frameMap(a); b = frameMap(b);
            }
            a = Mathf.Clamp(a, 0, frames - 1); b = Mathf.Clamp(b, 0, frames - 1);
            for (int i = 0; i < bones.Length; i++)
            {
                Vector3 position = Vector3.Lerp(positions[a, i], positions[b, i], t);
                Quaternion rotation = Quaternion.Slerp(rotations[a, i], rotations[b, i], t);
                Vector3 scale = Vector3.Lerp(scales[a, i], scales[b, i], t);
                if (stitch >= 0)
                {
                    position = Vector3.Lerp(positions[stitch, i], position, blend);
                    rotation = Quaternion.Slerp(rotations[stitch, i], rotation, blend);
                    scale = Vector3.Lerp(scales[stitch, i], scale, blend);
                }
                bones[i].localPosition = bindPositions[i] + position;
                bones[i].localRotation = bindRotations[i] * rotation;
                bones[i].localScale = Vector3.Scale(bindScales[i], scale);
            }
        }

        public void Restore()
        {
            for (int i = 0; i < bones.Length; i++)
            {
                bones[i].localPosition = bindPositions[i]; bones[i].localRotation = bindRotations[i];
                bones[i].localScale = bindScales[i];
            }
        }
    }
}
