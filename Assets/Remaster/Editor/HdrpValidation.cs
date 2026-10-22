using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Versus.Remaster.Editor
{
    [InitializeOnLoad]
    public static class HdrpValidation
    {
        static int sample, pendingFrame;
        static RenderTexture target;
        static Camera camera;
        static readonly float[] Times = { 1.5f, 5.5f, 13f };
        static readonly string[] Names = { "kazuya", "jin", "combat" };
        static HdrpValidation() { EditorApplication.update += Poll; }

        [MenuItem("Remaster/Rendering/Validate HDRP in current versus scene")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!UnityEngine.Object.FindFirstObjectByType<VersusGame>()) throw new InvalidOperationException("Open LocalVersus2 before validating.");
            sample = 0;
            Directory.CreateDirectory("Validation/HDRP");
            SessionState.SetBool("HdrpValidation", true);
            EditorApplication.isPlaying = true;
        }
        static void Poll()
        {
            if (File.Exists("Temp/hdrp-validate") && !EditorApplication.isCompiling)
            { File.Delete("Temp/hdrp-validate"); Run(); }
            if (!SessionState.GetBool("HdrpValidation", false) || !EditorApplication.isPlaying) return;
            Application.runInBackground = true;
            EditorApplication.QueuePlayerLoopUpdate();
            if (Time.time < .5f) return;
            try
            {
                if (target)
                {
                    if (Time.frameCount < pendingFrame + 3) return;
                    var previous = RenderTexture.active;
                    RenderTexture.active = target;
                    var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                    image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
                    File.WriteAllBytes("Validation/HDRP/" + Names[sample] + ".png", image.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(image);
                    RenderTexture.active = previous; camera.targetTexture = null;
                    target.Release(); UnityEngine.Object.DestroyImmediate(target); target = null;
                    sample++;
                    if (sample == Times.Length)
                    {
                        int wrong = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
                            .SelectMany(r => r.sharedMaterials).Count(m => m && !m.shader.name.StartsWith("HDRP/"));
                        bool hdrp = RenderPipelineManager.currentPipeline is HDRenderPipeline;
                        File.WriteAllText("Validation/HDRP/playback.json", "{\"hdrpActive\":" + hdrp.ToString().ToLowerInvariant() + ",\"nonHdrpMaterials\":" + wrong + ",\"captures\":3}");
                        var original = Resources.Load<OriginalStageLightingData>("OriginalStageLighting");
                        if (original)
                        {
                            var root = GameObject.Find("Original stg29 lighting");
                            var lights = root.GetComponentsInChildren<HDAdditionalLightData>();
                            int characterLights = lights.Count(l => (uint)l.lightlayersMask == OriginalStageLightingData.CharacterLayer);
                            var rigs = UnityEngine.Object.FindObjectsByType<JinRig>(FindObjectsSortMode.None);
                            int wrongLayers = rigs.SelectMany(r => r.GetComponentsInChildren<Renderer>(true))
                                .Count(r => (r.renderingLayerMask & 3) != 3);
                            original.profile.TryGet<Fog>(out var fog);
                            original.profile.TryGet<Exposure>(out var exposure);
                            original.profile.TryGet<HDRISky>(out var sky);
                            if (lights.Length != 4 || characterLights != 3 || wrongLayers != 0 || fog.enabled.value ||
                                exposure.mode.value != ExposureMode.Fixed || !sky.hdriSky.value || sky.hdriSky.value.width != 1024)
                                throw new InvalidOperationException("Original stage lighting runtime check failed.");
                            Directory.CreateDirectory("Validation/OriginalLighting/Applied");
                            foreach (var name in Names) File.Copy("Validation/HDRP/" + name + ".png", "Validation/OriginalLighting/Applied/" + name + ".png", true);
                            File.WriteAllText("Validation/OriginalLighting/Applied/playback.json",
                                "{\"hdrpActive\":" + hdrp.ToString().ToLowerInvariant() + ",\"nonHdrpMaterials\":" + wrong +
                                ",\"captures\":3,\"lights\":4,\"characterOnlyLights\":3,\"incorrectCharacterLayers\":0,\"fogEnabled\":false,\"fixedExposure\":true,\"skyResolution\":1024}");
                        }
                        SessionState.SetBool("HdrpValidation", false); EditorApplication.isPlaying = false;
                    }
                    return;
                }
                if (sample >= Times.Length) return;
                var game = UnityEngine.Object.FindFirstObjectByType<VersusGame>();
                var entrance = (LocalVersusEntrance)typeof(VersusGame).GetField("entrance", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(game);
                if (entrance == null) throw new InvalidOperationException("Enable entrances for this validation.");
                if (sample == 0 && (entrance.Current != LocalVersusEntrance.Shot.PlayerOne || entrance.Frame < 60)) return;
                if (sample == 1 && (entrance.Current != LocalVersusEntrance.Shot.PlayerTwo || entrance.Frame < 60)) return;
                if (sample == 2 && entrance.Active) return;
                camera = Camera.main;
                target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
                camera.targetTexture = target; pendingFrame = Time.frameCount;
            }
            catch (Exception e)
            {
                SessionState.SetBool("HdrpValidation", false);
                File.WriteAllText("Validation/HDRP/error.txt", e.ToString());
                EditorApplication.isPlaying = false; Debug.LogException(e);
            }
        }
    }
}
