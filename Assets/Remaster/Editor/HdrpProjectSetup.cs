using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Versus.Remaster.Editor
{
    [InitializeOnLoad]
    public static class HdrpProjectSetup
    {
        static HdrpProjectSetup() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (!File.Exists("Temp/hdrp-migrate") || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete("Temp/hdrp-migrate");
            try { Apply(); } catch (Exception e) { Debug.LogException(e); File.WriteAllText("Temp/hdrp-error.txt", e.ToString()); }
        }

        [MenuItem("Remaster/Rendering/Configure HDRP and convert materials")]
        public static void Apply()
        {
            const string folder = "Assets/Remaster/Rendering";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Remaster", "Rendering");
            string path = folder + "/RemasterHDRP.asset";
            var pipeline = AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(path);
            if (!pipeline) { pipeline = ScriptableObject.CreateInstance<HDRenderPipelineAsset>(); AssetDatabase.CreateAsset(pipeline, path); }
            GraphicsSettings.defaultRenderPipeline = pipeline;
            GraphicsSettings.lightsUseLinearIntensity = true;
            GraphicsSettings.lightsUseColorTemperature = true;
            int quality = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
                QualitySettings.antiAliasing = 0;
                QualitySettings.skinWeights = SkinWeights.Unlimited;
            }
            QualitySettings.SetQualityLevel(quality, false);
            PlayerSettings.colorSpace = ColorSpace.Linear;
            // HDRP 16 exposes initialization internally; use the same factory as its Wizard.
            var global = (RenderPipelineGlobalSettings)typeof(HDRenderPipeline).Assembly.GetType("UnityEngine.Rendering.HighDefinition.HDRenderPipelineGlobalSettings").GetMethod("Ensure", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { true });
            global.Initialize();
            EditorUtility.SetDirty(global);
            const string profilePath = "Assets/Remaster/Resources/RemasterEnvironment.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if (!profile)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, profilePath);
                var exposure = profile.Add<Exposure>(true);
                exposure.mode.value = ExposureMode.Fixed; exposure.fixedExposure.value = 0;
                var visual = profile.Add<VisualEnvironment>(true); visual.skyType.value = (int)SkyType.Gradient;
                var sky = profile.Add<GradientSky>(true);
                sky.top.value = new Color(.38f,.43f,.54f);
                sky.middle.value = new Color(.25f,.29f,.36f); sky.bottom.value = new Color(.09f,.11f,.15f);
                foreach (var component in profile.components) AssetDatabase.AddObjectToAsset(component, profile);
                EditorUtility.SetDirty(profile);
            }
            if (!profile.TryGet<Fog>(out var fog))
            {
                fog = profile.Add<Fog>(true);
                AssetDatabase.AddObjectToAsset(fog, profile);
            }
            fog.enabled.value = true; fog.meanFreePath.value = 45;
            fog.albedo.value = new Color(.025f,.04f,.07f);
            fog.baseHeight.value = 0; fog.maximumHeight.value = 15;
            EditorUtility.SetDirty(fog); EditorUtility.SetDirty(profile);
            int converted = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/Remaster" }))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (ConvertMaterial(material)) converted++;
            }
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Validation/HDRP");
            File.WriteAllText("Validation/HDRP/migration.json", "{\"hdrp\":\"16.0.6\",\"convertedThisRun\":" + converted + ",\"qualityLevels\":" + QualitySettings.names.Length + "}");
            Debug.Log("HDRP migration complete: " + converted + " materials.");
        }

        public static bool ConvertMaterial(Material material)
        {
            if (!material || (material.shader.name != "Standard" && material.shader.name != "Standard (Specular setup)")) return false;
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("UnityEditor.Rendering.HighDefinition.StandardsToHDLitMaterialUpgrader")).First(t => t != null);
            var upgrader = (MaterialUpgrader)Activator.CreateInstance(type, new object[] { material.shader.name, "HDRP/Lit", null });
            // Decode mask inputs directly. Unity's converter otherwise copies them through
            // Temp/convassets, which can fail on this project's duplicate texture filenames.
            Texture originalAlbedo = material.GetTexture("_MainTex");
            Texture originalSpecular = material.HasProperty("_SpecGlossMap") ? material.GetTexture("_SpecGlossMap") : null;
            var decoded = new Dictionary<Texture, Texture2D>();
            foreach (var property in new[] { "_MainTex", "_MetallicGlossMap", "_SpecGlossMap", "_OcclusionMap", "_DetailMask" })
            {
                if (!material.HasProperty(property)) continue;
                var source = material.GetTexture(property);
                string sourcePath = AssetDatabase.GetAssetPath(source);
                if (!source || !File.Exists(sourcePath)) continue;
                if (!decoded.TryGetValue(source, out var texture))
                {
                    var importer = AssetImporter.GetAtPath(sourcePath) as TextureImporter;
                    texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, importer && !importer.sRGBTexture);
                    if (!texture.LoadImage(File.ReadAllBytes(sourcePath))) throw new InvalidDataException(sourcePath);
                    decoded.Add(source, texture);
                }
                material.SetTexture(property, texture);
            }
            try
            {
                upgrader.Upgrade(material, MaterialUpgrader.UpgradeFlags.None);
                material.SetTexture("_BaseColorMap", originalAlbedo);
                material.SetTexture("_SpecularColorMap", originalSpecular);
            }
            finally { foreach (var texture in decoded.Values) UnityEngine.Object.DestroyImmediate(texture); }
            HDMaterial.ValidateMaterial(material);
            EditorUtility.SetDirty(material);
            return true;
        }
    }
}
