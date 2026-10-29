using UnityEngine;

namespace Versus.Remaster
{
    // On each imported stage prefab: where the original's match begins (its GAME_START player start).
    public sealed class StageInfo : MonoBehaviour
    {
        public bool hasMatchStart;
        [Tooltip("Stage-local position of the GAME_START marker, in metres.")]
        public Vector3 matchStart;
        [Tooltip("The marker's Unreal yaw. -90 looks along Unity +Z, the versus camera's view direction.")]
        public float matchYaw = -90;
    }
}
