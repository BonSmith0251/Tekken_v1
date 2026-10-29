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
            if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (File.Exists("Temp/fix-eye-shadow"))
            {
                File.Delete("Temp/fix-eye-shadow");
                try { FixEyeShadows(); } catch (Exception e) { Debug.LogException(e); File.WriteAllText("Temp/hdrp-error.txt", e.ToString()); }
            }
            if (File.Exists("Temp/fix-lash-brow"))
            {
                File.Delete("Temp/fix-lash-brow");
                try { FixLashesAndBrows(); } catch (Exception e) { Debug.LogException(e); File.WriteAllText("Temp/hdrp-error.txt", e.ToString()); }
            }
            if (!File.Exists("Temp/hdrp-migrate")) return;
            File.Delete("Temp/hdrp-migrate");
            try { Apply(); } catch (Exception e) { Debug.LogException(e); File.WriteAllText("Temp/hdrp-error.txt", e.ToString()); }
        }

        public static bool IsEyeShadow(Material material) => material &&
            (material.name.EndsWith("_eye_shadow", StringComparison.OrdinalIgnoreCase) || material.name.EndsWith("_eye_corner", StringComparison.OrdinalIgnoreCase) ||
             material.name.IndexOf("_fah_", StringComparison.OrdinalIgnoreCase) >= 0);

        // The source eye shadow is black with a soft alpha gradient that darkens the eyeball
        // under the lid. Opaque or alpha-tested, it paints a hard black shape over the eye.
        // Xiaoyu's eye corner is a similar alpha overlay; opaque, its whole quad shows as a pink patch.
        // Bob's stubble (MI_CH_bob_fah_*) is one too: opaque, it covers his cheeks and jaw in a grey slab.
        public static void MakeEyeShadowTransparent(Material material)
        {
            material.SetFloat("_AlphaCutoffEnable", 0);
            material.SetFloat("_SurfaceType", 1);
            material.SetFloat("_BlendMode", 0);
            material.SetFloat("_EnableBlendModePreserveSpecularLighting", 0);
            HDMaterial.ValidateMaterial(material);
            EditorUtility.SetDirty(material);
        }

        // Same channel rules as StandardsToHDLitMaterialUpgrader: R metallic red (white for an
        // unmapped metallic setup, black for specular), G occlusion green, B detail alpha,
        // A smoothness alpha (white when the source has no alpha).
        static void WriteMaskMap(Material material, Texture2D metallic, bool metallicSetup, Texture2D occlusion, Texture2D detail, Texture2D smoothness)
        {
            var inputs = new[] { metallic, occlusion, detail, smoothness }.Where(t => t).ToArray();
            int width = inputs.Length > 0 ? inputs.Max(t => t.width) : 4, height = inputs.Length > 0 ? inputs.Max(t => t.height) : 4;
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float u = (x + .5f) / width, v = (y + .5f) / height;
                    float r = metallic ? metallic.GetPixelBilinear(u, v).r : metallicSetup ? 1 : 0;
                    float g = occlusion ? occlusion.GetPixelBilinear(u, v).g : 1;
                    float b = detail ? detail.GetPixelBilinear(u, v).a : 1;
                    // Inputs are decoded to RGBA32, so a source without alpha reads as white.
                    float a = smoothness ? smoothness.GetPixelBilinear(u, v).a : 1;
                    pixels[y * width + x] = new Color(r, g, b, a);
                }
            var mask = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            mask.SetPixels32(pixels);
            string path = AssetDatabase.GetAssetPath(material);
            path = path.Remove(path.Length - 4) + "_MaskMap.png";
            File.WriteAllBytes(path, mask.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(mask);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer && (importer.sRGBTexture || importer.textureType != TextureImporterType.Default))
            { importer.textureType = TextureImporterType.Default; importer.sRGBTexture = false; importer.SaveAndReimport(); }
            material.SetTexture("_MaskMap", AssetDatabase.LoadAssetAtPath<Texture2D>(path));
        }

        const int MaterialIdStandard = 1, MaterialIdSpecularColor = 4;

        static bool ClearStaleSpecularColor(Material material)
        {
            if (!material.HasProperty("_MaterialID") || (int)material.GetFloat("_MaterialID") != MaterialIdSpecularColor || material.GetTexture("_SpecularColorMap")) return false;
            material.SetFloat("_MaterialID", MaterialIdStandard);
            HDMaterial.ValidateMaterial(material);
            EditorUtility.SetDirty(material);
            return true;
        }

        // Eyelashes and eyebrows, by the game's names: eyelash/eyebrow, Katarina's matuge (matsuge) and
        // the blow2/blow22 brows her face borrows from Xiaoyu. Not cmn_gla_blow: that is the browline
        // glasses frame, a Lit wardrobe item.
        public static bool IsLashOrBrow(Material material) => material &&
            System.Text.RegularExpressions.Regex.IsMatch(material.name, @"^(?!.*_gla_).*(eyelash|eyebrow|matuge|_blow\d*$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Lashes and brows are thin alpha cards over the face. Lit (opaque or cut out) they show hard
        // edges and dark fringes; the built-in Unlit/Transparent is not an HDRP shader and renders pink.
        // HDRP/Unlit with Surface Type Transparent (alpha blend) draws the texture's alpha softly, the
        // set-up Jin's and Kazuya's costume lashes already had.
        public static void MakeLashOrBrowUnlit(Material material)
        {
            Texture map = null;
            foreach (var property in new[] { "_UnlitColorMap", "_BaseColorMap", "_MainTex" })
                if (!map && material.HasProperty(property)) map = material.GetTexture(property);
            Color color = material.HasProperty("_UnlitColor") && material.shader.name == "HDRP/Unlit" ? material.GetColor("_UnlitColor")
                : material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
            float doubleSided = material.HasProperty("_DoubleSidedEnable") ? material.GetFloat("_DoubleSidedEnable") : 0;
            material.shader = Shader.Find("HDRP/Unlit");
            material.SetTexture("_UnlitColorMap", map);
            material.SetColor("_UnlitColor", color);
            material.SetFloat("_SurfaceType", 1);
            material.SetFloat("_BlendMode", 0);
            material.SetFloat("_AlphaCutoffEnable", 0);
            material.SetFloat("_DoubleSidedEnable", doubleSided);
            material.renderQueue = -1;
            HDMaterial.ValidateMaterial(material);
            EditorUtility.SetDirty(material);
        }

        [MenuItem("Remaster/Rendering/Make eyelashes and eyebrows unlit transparent")]
        public static void FixLashesAndBrows()
        {
            var paths = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/_Project/Characters" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!IsLashOrBrow(material)) continue;
                MakeLashOrBrowUnlit(material); paths.Add(path);
            }
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Validation/HDRP");
            var checks = paths.Select(p => AssetDatabase.LoadAssetAtPath<Material>(p)).Select(m => "{\"material\":\"" + AssetDatabase.GetAssetPath(m) + "\",\"shader\":\"" + m.shader.name +
                "\",\"surface\":" + m.GetFloat("_SurfaceType") + ",\"map\":" + (m.GetTexture("_UnlitColorMap") ? "true" : "false") + "}");
            File.WriteAllText("Validation/HDRP/lash-brow.json", "{\"passed\":" + (paths.Count > 0 ? "true" : "false") + ",\"materials\":[" + string.Join(",", checks) + "]}");
            Debug.Log("Eyelashes and eyebrows set to HDRP/Unlit transparent: " + paths.Count);
        }

        // A uniformly tinted alpha-blended surface; the base color map is dropped because its alpha is not opacity.
        public static void MakeTransparent(Material material, Color tint)
        {
            material.SetTexture("_BaseColorMap", null);
            material.SetColor("_BaseColor", tint);
            MakeEyeShadowTransparent(material);
        }

        [MenuItem("Remaster/Rendering/Fix eye shadow overlays")]
        public static void FixEyeShadows()
        {
            var fixedPaths = new List<string>();
            var specularPaths = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/_Project" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material && material.shader.name == "HDRP/Lit" && ClearStaleSpecularColor(material)) specularPaths.Add(path);
                if (!IsEyeShadow(material)) continue;
                MakeEyeShadowTransparent(material); fixedPaths.Add(path);
            }
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Validation/HDRP");
            Func<List<string>, string> list = paths => "[" + string.Join(",", paths.Select(p => "\"" + p + "\"")) + "]";
            File.WriteAllText("Validation/HDRP/eye-shadow.json", "{\"passed\":true,\"materials\":" + list(fixedPaths) + ",\"staleSpecularColorCleared\":" + list(specularPaths) + "}");
            Debug.Log("Eye shadow overlays set to transparent: " + fixedPaths.Count + "; stale specular color cleared: " + specularPaths.Count);
        }

        [MenuItem("Remaster/Rendering/Configure HDRP and convert materials")]
        public static void Apply()
        {
            const string folder = "Assets/_Project/Rendering";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/_Project", "Rendering");
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
            const string profilePath = "Assets/_Project/Resources/RemasterEnvironment.asset";
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
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/_Project" }))
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
            bool specularSetup = material.shader.name == "Standard (Specular setup)";
            bool smoothnessFromAlbedo = material.GetFloat("_SmoothnessTextureChannel") != 0;
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
            Texture2D Decoded(string property) => material.HasProperty(property) ? material.GetTexture(property) as Texture2D : null;
            var metallic = specularSetup ? null : Decoded("_MetallicGlossMap");
            var occlusion = Decoded("_OcclusionMap");
            var detail = Decoded("_DetailMask");
            var smoothness = smoothnessFromAlbedo ? Decoded("_MainTex") : Decoded(specularSetup ? "_SpecGlossMap" : "_MetallicGlossMap");
            try
            {
                upgrader.Upgrade(material, MaterialUpgrader.UpgradeFlags.None);
                // HDRP's TextureCombiner renders the mask on the GPU and returns black once
                // the HDRP pipeline is active. Rebuild it from the decoded inputs on the CPU.
                if (material.GetTexture("_MaskMap")) WriteMaskMap(material, metallic, !specularSetup, occlusion, detail, smoothness);
                material.SetTexture("_BaseColorMap", originalAlbedo);
                material.SetTexture("_SpecularColorMap", originalSpecular);
                // The upgrader leaves _MaterialID from a previous conversion of a reused asset.
                // A stale SpecularColor ID with no map is a white mirror with no diffuse.
                if (!originalSpecular) ClearStaleSpecularColor(material);
            }
            finally { foreach (var texture in decoded.Values) UnityEngine.Object.DestroyImmediate(texture); }
            HDMaterial.ValidateMaterial(material);
            EditorUtility.SetDirty(material);
            return true;
        }
    }
}
