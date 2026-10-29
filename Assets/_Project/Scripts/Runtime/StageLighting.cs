using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Versus.Remaster
{
    // An original stage's own lighting (built by StageLightingBuilder): its lights are children of the
    // prefab, its sky, fog and post process are `profile`. While the stage is shown it replaces the
    // scene's Render Settings rig (key/fill lights and volume); Deactivate puts the rig back.
    public sealed class StageLighting : MonoBehaviour
    {
        public VolumeProfile profile;
        [Tooltip("Stage-local height of the original height fog.")]
        public float fogBase;
        readonly List<Behaviour> switchedOff = new List<Behaviour>();
        // HDRP renders cascade shadows for one directional light only and logs "Cascade Shadow atlasing
        // has failed" every frame when two cast them. Other directional lights that cast shadows while the
        // stage is shown (a scene key light, fallback lights another script creates later) lose theirs.
        readonly List<(Light light, LightShadows shadows)> unshadowed = new List<(Light, LightShadows)>();
        float nextShadowCheck;
        Volume volume;

        public void Activate()
        {
            Deactivate();
            // Only this scene's rig: editor captures build a preview scene next to the user's open scene.
            foreach (var root in gameObject.scene.GetRootGameObjects())
                foreach (var rig in root.GetComponentsInChildren<StagingRenderSettings>())
                {
                    foreach (var light in rig.GetComponentsInChildren<Light>()) if (light.enabled) { light.enabled = false; switchedOff.Add(light); }
                    foreach (var v in rig.GetComponentsInChildren<Volume>()) if (v.enabled) { v.enabled = false; switchedOff.Add(v); }
                }
            // Fighters receive both the stage lights and the character-only (LC_CHARA_BOTH) lights.
            foreach (var root in gameObject.scene.GetRootGameObjects())
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    if (!r.transform.IsChildOf(transform))
                        r.renderingLayerMask |= OriginalStageLightingData.EnvironmentLayer | OriginalStageLightingData.CharacterLayer;
            KeepOneShadowSun();
            if (!profile) return;
            volume = gameObject.AddComponent<Volume>();
            volume.isGlobal = true; volume.priority = 50;
            volume.sharedProfile = PlacedProfile();
        }

        // A copy of the profile for where the stage now stands: fog heights follow the stage's elevation
        // and the HDRI sky turns with it. Stage scenes save this copy as an asset.
        public VolumeProfile PlacedProfile()
        {
            if (!profile) return null;
            // Instantiate copies the list but shares the components; copy them too, or the fog and sky
            // overrides below would write into the stage's profile asset.
            var instance = Instantiate(profile);
            for (int i = 0; i < instance.components.Count; i++)
            {
                instance.components[i] = Instantiate(profile.components[i]);
                instance.components[i].name = profile.components[i].name;
            }
            if (instance.TryGet<Fog>(out var fog))
            {
                float baseHeight = transform.TransformPoint(new Vector3(0, fogBase, 0)).y;
                fog.baseHeight.Override(baseHeight);
                fog.maximumHeight.Override(baseHeight + fog.maximumHeight.value);
            }
            if (instance.TryGet<HDRISky>(out var sky)) sky.rotation.Override(Mathf.Repeat(transform.eulerAngles.y, 360));
            return instance;
        }

        public void Deactivate()
        {
            foreach (var b in switchedOff) if (b) b.enabled = true;
            switchedOff.Clear();
            foreach (var (light, shadows) in unshadowed) if (light) light.shadows = shadows;
            unshadowed.Clear();
            if (volume) { if (Application.isPlaying) Destroy(volume); else DestroyImmediate(volume); }
            volume = null;
        }

        void LateUpdate()
        {
            if (!volume || Time.unscaledTime < nextShadowCheck) return;
            nextShadowCheck = Time.unscaledTime + .5f;
            KeepOneShadowSun();
        }

        // The stage's own brightest shadow-casting sun keeps its shadows; every other enabled directional
        // light that casts shadows has them switched off (restored by Deactivate).
        void KeepOneShadowSun()
        {
            var suns = new List<Light>();
            foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (light.isActiveAndEnabled && light.type == LightType.Directional && light.shadows != LightShadows.None
                    // In Play every loaded scene renders together; editor captures use their own preview scene.
                    && (Application.isPlaying || light.gameObject.scene == gameObject.scene)) suns.Add(light);
            if (suns.Count <= 1) return;
            Light keep = null;
            foreach (var light in suns)
                if (light.transform.IsChildOf(transform) && (!keep || light.intensity > keep.intensity)) keep = light;
            if (!keep) keep = suns[0];
            foreach (var light in suns)
                if (light != keep) { unshadowed.Add((light, light.shadows)); light.shadows = LightShadows.None; }
        }

        void OnDestroy() => Deactivate();
    }
}
