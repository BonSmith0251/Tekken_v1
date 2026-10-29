using UnityEngine;

namespace Versus.Remaster
{
    // Where an original stage places a particle system (brazier fire, falling sand, dust). The
    // system is decoded into Resources/StageEffects by Tools/StageEffects/export_stage_effects.py;
    // StageEnvironment.StartEffects plays it. The transform is the UE component's (UE X = local +X).
    public sealed class StageEffectMarker : MonoBehaviour
    {
        public string system;
    }
}
