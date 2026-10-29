using UnityEngine;

namespace Versus.Remaster
{
    // Root of a stage scene (Scenes/Stages/<stg>.unity, built by StageSceneSetup): a linked instance of
    // the stage prefab with its lighting, probes and camera, for editing and lightmap baking. In Play it
    // starts the stage's particle systems, as StageEnvironment.Spawn does elsewhere.
    public sealed class StageScene : MonoBehaviour
    {
        public string stage;
        public GameObject stageInstance;

        void Start()
        {
            if (stageInstance) StageEnvironment.StartEffects(stageInstance);
        }
    }
}
