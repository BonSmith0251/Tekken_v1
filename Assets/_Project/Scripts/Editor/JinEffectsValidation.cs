using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Versus.Remaster.Editor
{
    // Exercises live particle data, rather than treating exported JSON as proof of rendering.
    [InitializeOnLoad]
    public static class JinEffectsValidation
    {
        [Serializable] sealed class Library { public CascadeRoleData[] roles; }
        [Serializable] sealed class Result
        {
            public bool passed;
            public float neckSpawnErrorMetres, sourceFrame, expectedSourceFrame, allowedSimulationFrameLag;
            public int atlasParticles, commonRenderers, stageRenderers;
            public string nativeBone;
            public float nativeBoneSpawnErrorMetres, worldParticleDriftMetres;
            public bool nativeClearPassed;
            public string error;
        }
        static JinEffectsValidation() { EditorApplication.update += Poll; }
        static void Poll()
        {
            const string flag = "Temp/validate-jin-effects";
            if (!File.Exists(flag) || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete(flag);
            Run();
        }

        [MenuItem("Remaster/Validation/Jin effect rendering")]
        public static void Run()
        {
            var result = new Result();
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var host = new GameObject("Isolated Jin effect validation");
                SceneManager.MoveGameObjectToScene(host, scene);
                var showcase = host.AddComponent<CharacterEffectsShowcase>();
                showcase.character = AssetDatabase.LoadAssetAtPath<MuseumCharacter>("Assets/_Project/Museum/Jin.asset");
                showcase.showInterface = false;
                showcase.Initialize(false);
                Require(showcase.Roles.Length == 18, "Expected the verified installation's 18 Jin candidate roles.");
                var neck = showcase.Model.GetComponentsInChildren<Transform>().Single(t => t.name.Equals("Neck", StringComparison.OrdinalIgnoreCase));
                var atlas = showcase.Instance.GetComponentsInChildren<ParticleSystem>().Single(p => p.name == "animtex");
                var material = atlas.GetComponent<ParticleSystemRenderer>().sharedMaterial;
                Require(material.GetVector("_SubUVGrid") == new Vector4(8, 8, 1, 1), "NOMAL needs an 8x8 interpolated atlas.");
                Require(material.mainTexture && material.mainTexture.name == "T_EF_COL_jin_thunder00_tss", "NOMAL is using the wrong source texture.");
                Require(!ShaderUtil.ShaderHasError(material.shader), "Particle shader has compilation errors.");

                // Emit without advancing the particle: the original zero-offset Neck source must win
                // over the showcase's separate manual world anchor and over other provided bones.
                showcase.Instance.Step(.00001f);
                var particles = new ParticleSystem.Particle[16];
                Require(atlas.GetParticles(particles) == 1, "NOMAL should emit its one atlas particle at time zero.");
                result.neckSpawnErrorMetres = Vector3.Distance(particles[0].position, neck.position);
                Require(result.neckSpawnErrorMetres < .001f, "Atlas particle did not spawn at the original named Neck bone.");

                showcase.Restart();
                showcase.Advance(.15f);
                atlas = showcase.Instance.GetComponentsInChildren<ParticleSystem>().Single(p => p.name == "animtex");
                result.atlasParticles = atlas.GetParticles(particles);
                var data = new List<Vector4>();
                atlas.GetCustomParticleData(data, ParticleSystemCustomData.Custom2);
                Require(result.atlasParticles == 1 && data.Count == 1, "Animated atlas frame stream is missing.");
                // Original baked SubImageIndex is 0..40 over particle life; test Unity's evaluated
                // Custom2 stream against the actual simulated particle age, including first tick.
                result.sourceFrame = data[0].x;
                result.expectedSourceFrame = 40 * (1 - particles[0].remainingLifetime / particles[0].startLifetime);
                // Unity evaluates CustomData at the beginning of its particle simulation step,
                // while GetParticles returns the lifetime after that step. Allow exactly one step.
                result.allowedSimulationFrameLag = 40f / 120 / particles[0].startLifetime;
                Require(result.sourceFrame <= result.expectedSourceFrame + .02f
                    && result.expectedSourceFrame - result.sourceFrame <= result.allowedSimulationFrameLag + .02f,
                    "Atlas frame did not advance with the particle's life.");

                var native = CharacterBattleEffects.Create(host.transform, "jin", showcase.Model.transform);
                Require(native, "Verified native dispatch manifest is missing.");
                native.SetManualSimulation(true);
                Require(!native.TryPlayProperty(0x801F, 0), "An unimplemented dispatch path was guessed.");
                Require(native.TryPlayProperty(0x802E, 7), "Verified Jin property failed to dispatch.");
                result.nativeBone = native.LastBone.name;
                Require(result.nativeBone == "R_Arm", "Native bone 7 must resolve through the original VBN to R_Arm.");
                native.LastInstance.Step(.00001f);
                atlas = native.LastInstance.GetComponentsInChildren<ParticleSystem>().Single(p => p.name == "animtex");
                Require(atlas.GetParticles(particles) == 1, "Native bone dispatch did not emit the atlas particle.");
                result.nativeBoneSpawnErrorMetres = Vector3.Distance(particles[0].position, native.LastBone.position);
                Require(result.nativeBoneSpawnErrorMetres < .001f, "BoneIndex did not override the cooked Neck source.");
                var emittedPosition = particles[0].position;
                showcase.Model.transform.position += Vector3.right;
                native.SimulateStep(.03f);
                Require(atlas.GetParticles(particles) == 1, "Native atlas particle died too soon.");
                result.worldParticleDriftMetres = Vector3.Distance(emittedPosition, particles[0].position);
                Require(result.worldParticleDriftMetres < .001f, "NOMAL incorrectly dragged an existing world-space particle with the actor.");
                native.Clear();
                result.nativeClearPassed = native.ActiveCount == 0 && !native.LastInstance && native.SpawnCount == 0;
                Require(result.nativeClearPassed, "Native effect cleanup failed.");

                result.commonRenderers = CheckRegularLibrary(host.transform, "HitEffects", CascadeEffects.Hit);
                var stage = Resources.Load<TextAsset>("StageEffects/effects");
                Require(stage, "Existing stage effect library is missing.");
                string stageRole = JsonUtility.FromJson<Library>(stage.text).roles[0].role;
                result.stageRenderers = CheckRegularLibrary(host.transform, "StageEffects", stageRole);
                result.passed = true;
            }
            catch (Exception error) { result.error = error.ToString(); Debug.LogException(error); }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
            Directory.CreateDirectory("Validation/JinEffects");
            File.WriteAllText("Validation/JinEffects/runtime-validation.json", JsonUtility.ToJson(result, true));
            Debug.Log("Jin effect runtime validation passed=" + result.passed);
        }

        static int CheckRegularLibrary(Transform host, string folder, string role)
        {
            var library = CascadeEffects.Create(host, folder, "Existing " + folder);
            var instance = library.Play(role, Vector3.zero, Quaternion.identity);
            Require(instance, "Existing effect role failed: " + folder + "/" + role);
            instance.SimulateStep(.05f);
            var renderers = instance.GetComponentsInChildren<ParticleSystemRenderer>();
            Require(renderers.Length > 0, "Existing effect has no particle renderers: " + folder);
            foreach (var renderer in renderers)
            {
                Require(renderer.sharedMaterial, "Existing effect material is missing.");
                Require(renderer.sharedMaterial.GetVector("_SubUVGrid").w == 0, "A regular effect accidentally enabled atlas sampling.");
                Require(!ShaderUtil.ShaderHasError(renderer.sharedMaterial.shader), "Shared particle shader failed compilation.");
            }
            return renderers.Length;
        }

        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
