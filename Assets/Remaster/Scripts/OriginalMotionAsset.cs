using System;
using UnityEngine;

namespace Versus.Remaster
{
    /// <summary>Decoded original samples, in Tekken channel coordinates.
    /// Retargeting is required before applying these to Unity transforms.</summary>
    public sealed class OriginalMotionAsset : ScriptableObject
    {
        public int frameCount, channelCount;
        public float frameRate=60;
        [HideInInspector] public byte[] decodedC8;
        public Vector3 GetSample(int frame,int channel)
        {
            if(frame<0 || frame>=frameCount || channel<0 || channel>=channelCount)
                throw new ArgumentOutOfRangeException();
            int offset=8+4*channelCount+12*(frame*channelCount+channel);
            return new Vector3(BitConverter.ToSingle(decodedC8,offset),BitConverter.ToSingle(decodedC8,offset+4),BitConverter.ToSingle(decodedC8,offset+8));
        }
    }
}
