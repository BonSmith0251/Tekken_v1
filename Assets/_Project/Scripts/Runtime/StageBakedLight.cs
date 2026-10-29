using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Versus.Remaster
{
    // On every light the original only baked into its lightmaps (UE "static" mobility). At runtime the
    // nearest ones stand in as dim realtime lights (standIn, enabled); the rest stay in the prefab
    // switched off. A stage scene (Scenes/Stages/<stg>.unity) turns all of them on as Unity baked lights
    // at bakeIntensity for its lightmap bake.
    public sealed class StageBakedLight : MonoBehaviour
    {
        public bool standIn;
        [Tooltip("Intensity for baking: the per-light stand-in value, scaled so all of the stage's baked lights together give the same budget at the match start.")]
        public float bakeIntensity;
        public LightUnit unit = LightUnit.Lumen;
    }
}
