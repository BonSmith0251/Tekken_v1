using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace Versus.Remaster
{
    // Scenes create cameras and lights in Awake/Start, so configure them before rendering.
    [DefaultExecutionOrder(10000)]
    public sealed class HdrpSceneLighting : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var host = new GameObject("HDRP scene lighting");
            DontDestroyOnLoad(host);
            host.AddComponent<HdrpSceneLighting>();
            var volume = host.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = Resources.Load<VolumeProfile>("RemasterEnvironment");
        }

        bool configured;
        void OnEnable() => SceneManager.sceneLoaded += SceneLoaded;
        void OnDisable() => SceneManager.sceneLoaded -= SceneLoaded;
        void SceneLoaded(Scene scene, LoadSceneMode mode) => configured = false;

        void LateUpdate()
        {
            if (configured) return;
            configured = true;
            var game = FindFirstObjectByType<VersusGame>();
            var original = game ? Resources.Load<OriginalStageLightingData>("OriginalStageLighting") : null;
            GetComponent<Volume>().sharedProfile = original ? original.profile : Resources.Load<VolumeProfile>("RemasterEnvironment");
            if (original) original.Apply(game);
            foreach (var camera in Camera.allCameras)
            {
                if (!camera.TryGetComponent<HDAdditionalCameraData>(out var data))
                    data = camera.gameObject.AddComponent<HDAdditionalCameraData>();
                data.clearColorMode = original ? HDAdditionalCameraData.ClearColorMode.Sky : HDAdditionalCameraData.ClearColorMode.Color;
                data.backgroundColorHDR = camera.backgroundColor;
                data.antialiasing = HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                // Camera transform, physical lens, viewport and entrance cuts remain authored.
            }
            foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.TryGetComponent<HDAdditionalLightData>(out _)) continue;
                float intensity = light.intensity;
                var data = light.gameObject.AddComponent<HDAdditionalLightData>();
                if (light.type == LightType.Directional)
                    data.SetIntensity(intensity * Mathf.PI, LightUnit.Lux);
            }
        }

        public static void Emission(Material material, Color color)
        {
            material.SetColor("_EmissiveColor", color);
        }
    }
}
