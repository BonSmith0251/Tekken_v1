using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Versus.Remaster.Editor
{
    [InitializeOnLoad]
    public static class OriginalStageLightingSetup
    {
        [Serializable] class Rotation { public float pitch, yaw, roll; }
        [Serializable] class SourceLight { public string actor, nativeLightChannel; public float nativeIntensity; public int[] colorRgba8; public Rotation relativeRotationDegrees; }
        [Serializable] class Source { public SourceLight[] lights; }
        static OriginalStageLightingSetup() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (!File.Exists("Temp/apply-original-lighting") || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete("Temp/apply-original-lighting");
            try { Apply(); } catch (Exception e) { File.WriteAllText("Temp/original-lighting-error.txt", e.ToString()); Debug.LogException(e); }
        }
        static T Component<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet<T>(out var component)) return component;
            component = profile.Add<T>(true); AssetDatabase.AddObjectToAsset(component, profile); return component;
        }
        [MenuItem("Remaster/Rendering/Apply original stg29 lighting")]
        public static void Apply()
        {
            var pipeline = (HDRenderPipelineAsset)GraphicsSettings.defaultRenderPipeline;
            var settings = pipeline.currentPlatformRenderPipelineSettings;
            settings.supportLightLayers = true; settings.supportSSR = true; settings.supportSSAO = true;
            pipeline.currentPlatformRenderPipelineSettings = settings;
            EditorUtility.SetDirty(pipeline);

            const string path = "Assets/_Project/Resources/OriginalStage29Environment.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (!profile) { profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, path); }
            var exposure = Component<Exposure>(profile);
            exposure.mode.value = ExposureMode.Fixed; exposure.fixedExposure.value = 0;
            var sky = Component<HDRISky>(profile);
            sky.hdriSky.value = AssetDatabase.LoadAssetAtPath<Cubemap>("Assets/_Project/Rendering/OriginalStage/Stg29Sky.t7sky");
            if (!sky.hdriSky.value) throw new InvalidOperationException("Import the original HDR sky first.");
            sky.skyIntensityMode.value = SkyIntensityMode.Multiplier; sky.multiplier.value = 1;
            sky.rotation.value = 0; sky.updateMode.value = EnvironmentUpdateMode.OnChanged;
            Component<VisualEnvironment>(profile).skyType.value = (int)SkyType.HDRI;
            Component<Fog>(profile).enabled.value = false;
            Component<Tonemapping>(profile).mode.value = TonemappingMode.Neutral;
            var bloom = Component<Bloom>(profile);
            bloom.intensity.value = .2f; bloom.scatter.value = .7f; bloom.threshold.value = 1;
            var ao = Component<ScreenSpaceAmbientOcclusion>(profile);
            ao.intensity.value = 1; ao.radius.value = .6f;
            var reflections = Component<ScreenSpaceReflection>(profile);
            reflections.enabled.value = true; reflections.enabledTransparent.value = false;
            reflections.tracing.value = RayCastingMode.RayMarching;
            reflections.minSmoothness = .55f; reflections.smoothnessFadeStart = .8f;
            Component<IndirectLightingController>(profile).reflectionLightingMultiplier.value = .7f;
            Component<MotionBlur>(profile).intensity.value = 0;
            Component<DepthOfField>(profile).focusMode.value = DepthOfFieldMode.Off;
            Component<ScreenSpaceLensFlare>(profile).intensity.value = 0;
            foreach (var component in profile.components) EditorUtility.SetDirty(component);
            EditorUtility.SetDirty(profile);

            const string dataPath = "Assets/_Project/Resources/OriginalStageLighting.asset";
            var data = AssetDatabase.LoadAssetAtPath<OriginalStageLightingData>(dataPath);
            if (!data) { data = ScriptableObject.CreateInstance<OriginalStageLightingData>(); AssetDatabase.CreateAsset(data, dataPath); }
            var source = JsonUtility.FromJson<Source>(File.ReadAllText("Validation/OriginalLighting/stg29-summary.json"));
            data.lights = Array.ConvertAll(source.lights, l => new OriginalStageLightingData.StageLight {
                name = l.actor, nativeIntensity = l.nativeIntensity,
                color = new Color32((byte)l.colorRgba8[0], (byte)l.colorRgba8[1], (byte)l.colorRgba8[2], 255),
                unrealPitchYawRoll = new Vector3(l.relativeRotationDegrees.pitch, l.relativeRotationDegrees.yaw, l.relativeRotationDegrees.roll),
                characterOnly = l.nativeLightChannel == "LC_CHARA_BOTH" });
            data.profile = profile; data.luxPerNativeUnit = 1;
            EditorUtility.SetDirty(data); AssetDatabase.SaveAssets();
            File.WriteAllText("Validation/OriginalLighting/applied.json", "{\"stage\":\"stg29\",\"lights\":4,\"characterOnlyLights\":3,\"fog\":false,\"skyFaces\":6,\"fixedExposureEV\":0,\"luxPerNativeUnit\":1,\"rendererMapping\":\"approximate HDRP calibration, native values retained\"}");
            Debug.Log("Original stg29 lighting profile applied.");
        }
    }
}
