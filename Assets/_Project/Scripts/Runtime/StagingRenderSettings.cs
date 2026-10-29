using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Versus.Remaster
{
    // The staging_test look as a scene object, shared by staging_test, the museum and the wardrobe:
    // its Key/Fill light children and Volume are edited directly; the colors below are re-applied
    // every frame by the scene's controller. Each scene keeps its own copy for separate tuning.
    [DisallowMultipleComponent]
    public sealed class StagingRenderSettings : MonoBehaviour
    {
        [Tooltip("Unity ambient color. HDRP lights mostly from the environment volume's sky, so this has little effect.")]
        public Color ambientLight = new Color(.46f, .48f, .54f);
        public Color background = new Color(.025f, .035f, .055f);
        public Color floor = new Color(.12f, .16f, .21f);

        [Header("Antialiasing")]
        [Tooltip("SMAA is the established default. TAA is smoother on hair and thin edges but can ghost on fast motion and camera cuts.")]
        public HDAdditionalCameraData.AntialiasingMode antialiasing = HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        public HDAdditionalCameraData.SMAAQualityLevel smaaQuality = HDAdditionalCameraData.SMAAQualityLevel.High;
        public HDAdditionalCameraData.TAAQualityLevel taaQuality = HDAdditionalCameraData.TAAQualityLevel.Medium;
        [Range(0, 2)] public float taaSharpen = .5f;
        [Tooltip("Higher reduces shimmer on specular highlights at the cost of more blur.")]
        [Range(0, 1)] public float taaAntiFlicker = .5f;
        [Range(0, 1)] public float taaHistorySharpening = .35f;

        // Versus cameras look along +Z, the staging and museum cameras along -Z; turning the rig
        // keeps the key and fill on the same sides of the frame.
        public const float VersusYaw = 180;

        // The scene's object, or the same defaults built at runtime when the scene lacks one.
        public static StagingRenderSettings FindOrCreate(Transform parent)
        {
            var settings = FindFirstObjectByType<StagingRenderSettings>();
            return settings ? settings : Create(parent, null);
        }

        // Builds the established look under parent. The editor setup and the runtime fallback share it.
        public static StagingRenderSettings Create(Transform parent, VolumeProfile profile)
        {
            var root = new GameObject("Render Settings");
            root.transform.SetParent(parent, false);
            var settings = root.AddComponent<StagingRenderSettings>();
            // Without shadows both lights reach the eyeballs and mouth interior through the
            // lids and lips. Contact shadows resolve the millimeter gaps the shadow map cannot.
            CreateLight(root.transform, "Key light", Color.white, 1.35f, -45, LightShadows.Soft);
            CreateLight(root.transform, "Fill light", new Color(.65f, .75f, 1f), .65f, 45, LightShadows.None);
            // Stays above the project-wide environment volume (priority 0); local to this scene.
            var volume = root.AddComponent<Volume>();
            volume.isGlobal = true; volume.priority = 10;
            volume.sharedProfile = profile ? profile : CreateDefaultProfile();
            return settings;
        }

        static void CreateLight(Transform parent, string name, Color color, float intensity, float yaw, LightShadows shadows)
        {
            var light = new GameObject(name).AddComponent<Light>();
            light.transform.SetParent(parent, false);
            light.transform.localRotation = Quaternion.Euler(42, yaw, 0);
            light.type = LightType.Directional;
            light.color = color;
            light.shadows = shadows;
            if (!light.TryGetComponent<HDAdditionalLightData>(out var data)) data = light.gameObject.AddComponent<HDAdditionalLightData>();
            // Legacy intensity converted as HdrpSceneLighting does: intensity * PI lux.
            data.SetIntensity(intensity * Mathf.PI, LightUnit.Lux);
            data.useContactShadow.useOverride = true;
            data.useContactShadow.@override = true;
        }

        public static VolumeProfile CreateDefaultProfile()
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var contact = profile.Add<ContactShadows>(true);
            contact.enable.value = true; contact.length.value = .04f; contact.thicknessScale.value = .02f;
            contact.maxDistance.value = 20; contact.fadeDistance.value = 2;
            // Close-up takes need a short shadow range for usable shadow-map resolution.
            profile.Add<HDShadowSettings>(true).maxShadowDistance.value = 12;
            return profile;
        }

        public void Apply(Camera camera, Renderer floorRenderer)
        {
            RenderSettings.ambientLight = ambientLight;
            camera.backgroundColor = background;
            if (!camera.TryGetComponent<HDAdditionalCameraData>(out var cameraData)) cameraData = camera.gameObject.AddComponent<HDAdditionalCameraData>();
            cameraData.backgroundColorHDR = background;
            cameraData.antialiasing = antialiasing;
            cameraData.SMAAQuality = smaaQuality;
            cameraData.TAAQuality = taaQuality;
            cameraData.taaSharpenStrength = taaSharpen;
            cameraData.taaAntiFlicker = taaAntiFlicker;
            cameraData.taaHistorySharpening = taaHistorySharpening;
            if (floorRenderer) floorRenderer.sharedMaterial.color = floor;
        }
    }
}
