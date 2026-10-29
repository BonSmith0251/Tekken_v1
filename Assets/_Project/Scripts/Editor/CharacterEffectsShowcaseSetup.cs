using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Fighter = Versus.Remaster.CharacterEffectsShowcase.Fighter;

namespace Versus.Remaster.Editor
{
    [InitializeOnLoad]
    public static class CharacterEffectsShowcaseSetup
    {
        const string ScenePath = "Assets/_Project/Scenes/CharacterEffectsShowcase.unity";
        const string OldScenePath = "Assets/_Project/Scenes/JinEffectsShowcase.unity";
        const string Output = "Validation/JinEffects";
        const string CharacterPath = "Assets/_Project/Museum/Jin.asset";
        [Serializable] sealed class CaptureRow { public string role, file, limitations; public float seconds; public bool facingRight; public int particles, actionMove, sourceFrame, triggeredEvents; public string[] rendererTextures; }
        [Serializable] sealed class Report { public bool passed; public string presentation = "manual original-asset preview; battle triggers not inferred"; public int systems; public CaptureRow[] captures; }

        static CharacterEffectsShowcaseSetup() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
            Job("Temp/setup-character-effects-showcase", CreateScene);
            Job("Temp/capture-jin-effects", Capture);
            Job("Temp/capture-jin-action-effects", CaptureActions);
            Job("Temp/capture-katarina-action-effects", CaptureKatarinaActions);
            Job("Temp/capture-character-action-effects", CaptureAllCharacterActions);
            Job("Temp/check-character-effects-showcase", CheckSwitching);
            Job("Temp/open-character-effects-showcase", Open);
            Job("Temp/play-character-effects-showcase", OpenAndPlay);
        }

        static void Job(string flag, Action action)
        {
            if (!File.Exists(flag)) return;
            File.Delete(flag);
            try { action(); }
            catch (Exception error) { Debug.LogException(error); File.WriteAllText(flag + "-error.txt", error.ToString()); }
        }

        // All five fighters, in roster order. Jin's and Kazuya's museum assets store no character code.
        static Fighter[] Fighters() => new[]
        {
            Fighter("Jin", "jin", 1840, 1779),                 // Jz_shoryu24 (EWHF), Jz_dslpS
            Fighter("Kazuya", "kaz", 1712, 1614, 1565),         // Kz_shoryu_S (EWGF), Kz_kazup00, Kz_rprp01E
            Fighter("Xiaoyu", "xia", 1791),                     // Fu_torp00sp (Thunder Strike)
            Fighter("Alisa", "asa", 1989, 1931),                // Aa_steWP, Aa_atamasuper
            Fighter("Katarina", "ltn", 1678, 1980, 1883),       // Ln_1lrk, Jn_SUPER_0, Ln_Lnage_n
        };
        static Fighter Fighter(string name, string code, params int[] actions) => new Fighter {
            character = LoadCharacter("Assets/_Project/Museum/" + name + ".asset"), code = code, actions = actions };

        [MenuItem("Remaster/Rendering/Create character effects showcase")]
        public static void CreateScene()
        {
            // The scene was JinEffectsShowcase.unity until 2026-10-27; move it so its GUID and edits survive.
            if (!File.Exists(ScenePath) && File.Exists(OldScenePath))
            {
                string error = AssetDatabase.MoveAsset(OldScenePath, ScenePath);
                if (!string.IsNullOrEmpty(error)) throw new IOException(error);
            }
            var open = SceneManager.GetSceneByPath(ScenePath);
            if (open.IsValid() && open.isLoaded && open.isDirty)
                throw new InvalidOperationException(ScenePath + " is open with unsaved edits; save it first.");
            var previous = SceneManager.GetActiveScene();
            bool wasOpen = open.IsValid() && open.isLoaded;
            var scene = wasOpen ? open : File.Exists(ScenePath) ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive)
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                var showcase = scene.GetRootGameObjects().Select(g => g.GetComponent<CharacterEffectsShowcase>()).FirstOrDefault(c => c);
                if (!showcase)
                {
                    var created = new GameObject();
                    SceneManager.MoveGameObjectToScene(created, scene);
                    showcase = created.AddComponent<CharacterEffectsShowcase>();
                }
                showcase.gameObject.name = "Character battle effects showcase";
                showcase.fighters = Fighters();
                showcase.character = showcase.fighters[0].character;
                EditorUtility.SetDirty(showcase);
                if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Could not save " + ScenePath);
            }
            finally
            {
                if (!wasOpen)
                {
                    if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/scene-created.txt", ScenePath);
            Debug.Log("Character effects showcase ready: " + ScenePath + " (Jin, Kazuya, Xiaoyu, Alisa, Katarina).");
        }

        [MenuItem("Remaster/Rendering/Open character effects showcase")]
        public static void Open()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Open scenes have unsaved edits. Showcase is available at " + ScenePath + "; these edits were preserved.");
            CreateScene();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Selection.activeGameObject = UnityEngine.Object.FindFirstObjectByType<CharacterEffectsShowcase>().gameObject;
            EditorApplication.ExecuteMenuItem("Window/General/Game");
        }

        [MenuItem("Remaster/Rendering/Play character effects showcase")]
        public static void OpenAndPlay()
        {
            Open(); // Throws before changing scenes if there are unsaved user edits.
            EditorApplication.isPlaying = true;
        }

        // One showcase switching through all five fighters, as the scene does: each must build, dispatch
        // its first featured action's original rows, and render. Output: Validation/CharacterEffectsShowcase.
        [MenuItem("Remaster/Validation/Character effects showcase switching")]
        public static void CheckSwitching()
        {
            const string output = "Validation/CharacterEffectsShowcase";
            Directory.CreateDirectory(output);
            var rows = new List<CaptureRow>();
            var preview = new PreviewRenderUtility();
            float previousExposure = Shader.GetGlobalFloat("_CascadeExposure");
            try
            {
                var host = new GameObject("Character effects showcase check");
                preview.AddSingleGO(host);
                var showcase = host.AddComponent<CharacterEffectsShowcase>();
                showcase.fighters = Fighters(); showcase.showInterface = false;
                showcase.Initialize(false);
                Shader.SetGlobalFloat("_CascadeExposure", 1 / 1.2f);
                preview.lights[0].transform.rotation = Quaternion.Euler(35, 30, 0);
                preview.ambientColor = new Color(.3f, .3f, .32f);
                var cam = preview.camera;
                cam.transform.position = new Vector3(-.15f, 1.25f, -4.4f);
                cam.transform.LookAt(new Vector3(-.15f, .95f, 0));
                cam.fieldOfView = 38; cam.nearClipPlane = .05f; cam.farClipPlane = 60;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.045f, .055f, .075f);
                // Twice round, so switching back to a fighter is covered too.
                for (int pass = 0; pass < 2; pass++)
                    for (int i = 0; i < showcase.fighters.Length; i++)
                    {
                        showcase.SelectFighter(i);
                        var fighter = showcase.fighters[i];
                        if (showcase.ActionMove != fighter.actions[0])
                            throw new InvalidOperationException(fighter.code + ": switching did not select its first original action.");
                        if (host.GetComponentsInChildren<CharacterBattleEffects>(true).Length != 1)
                            throw new InvalidOperationException(fighter.code + ": the previous fighter's dispatcher survived the switch.");
                        showcase.Advance(.8f);
                        if (showcase.TriggeredEvents < 1)
                            throw new InvalidOperationException(fighter.code + ": no original effect dispatched in its first action.");
                        if (pass == 0) Save(preview, showcase, fighter.code + "-" + fighter.actions[0], rows, output);
                    }
                File.WriteAllText(output + "/switch-report.json", JsonUtility.ToJson(new Report {
                    passed = true, systems = showcase.fighters.Length, presentation = "one showcase switching through every fighter twice; first featured action each",
                    captures = rows.ToArray() }, true));
            }
            finally { preview.Cleanup(); Shader.SetGlobalFloat("_CascadeExposure", previousExposure); }
            Debug.Log("Character effects showcase switching checked: Validation/CharacterEffectsShowcase");
        }

        [MenuItem("Remaster/Rendering/Capture Jin effects")]
        public static void Capture()
        {
            Directory.CreateDirectory(Output);
            var rows = new List<CaptureRow>();
            var preview = new PreviewRenderUtility();
            float previousExposure = Shader.GetGlobalFloat("_CascadeExposure");
            try
            {
                var host = new GameObject("Jin effects isolated capture");
                preview.AddSingleGO(host);
                var showcase = host.AddComponent<CharacterEffectsShowcase>();
                showcase.character = LoadCharacter(); showcase.showInterface = false;
                showcase.Initialize(false);
                Shader.SetGlobalFloat("_CascadeExposure", 1 / 1.2f);
                preview.lights[0].intensity = 1.1f; preview.lights[0].transform.rotation = Quaternion.Euler(35, 30, 0);
                preview.lights[1].intensity = .5f;
                preview.ambientColor = new Color(.3f, .3f, .32f);
                var cam = preview.camera;
                cam.transform.position = new Vector3(-.15f, 1.35f, -4.7f);
                cam.transform.LookAt(new Vector3(-.15f, 1.05f, 0));
                cam.fieldOfView = 38; cam.nearClipPlane = .05f; cam.farClipPlane = 60;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.045f, .055f, .075f);
                foreach (var role in showcase.Roles)
                {
                    showcase.SetFacing(true); showcase.SelectRole(role);
                    foreach (float second in new[] { .03f, .15f, .30f })
                    {
                        showcase.Advance(second - showcase.Clock);
                        Save(preview, showcase, role + "-right-" + Mathf.RoundToInt(second * 1000).ToString("000"), rows);
                    }
                }
                foreach (string role in showcase.Roles.Where(r => r == CharacterEffectsShowcase.DefaultRole || r.IndexOf("unique", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    showcase.SelectRole(role); showcase.SetFacing(false); showcase.Seek(.25f);
                    Save(preview, showcase, role + "-left-250", rows);
                }
                // Exercise backward seek, owner turn and moving attachment using the same preview clock.
                showcase.SelectRole(CharacterEffectsShowcase.DefaultRole);
                showcase.Seek(.4f); showcase.Seek(.08f);
                if (Mathf.Abs(showcase.Clock - .08f) > .0001f) throw new Exception("Preview seek failed.");
                showcase.SetFacing(true); showcase.SetTravel(true); showcase.Advance(.2f);
                Save(preview, showcase, "Jin-NOMAL-moving-preview", rows);
                showcase.SetTravel(false); showcase.Restart(); showcase.Advance(.15f);
                cam.transform.position = new Vector3(.05f, 1.45f, -2.3f);
                cam.transform.LookAt(new Vector3(.05f, 1.35f, 0));
                Save(preview, showcase, "Jin-NOMAL-close-150", rows);
                var report = new Report { passed = true, systems = showcase.Roles.Length, captures = rows.ToArray() };
                File.WriteAllText(Output + "/capture-report.json", JsonUtility.ToJson(report, true));
            }
            finally { preview.Cleanup(); Shader.SetGlobalFloat("_CascadeExposure", previousExposure); }
            Debug.Log("Jin original-effect preview captures: " + Output);
        }

        static void Save(PreviewRenderUtility preview, CharacterEffectsShowcase showcase, string name, List<CaptureRow> rows, string output = Output)
        {
            // HDRP may convert the preview's legacy light values during Render. Reapply the
            // presentation lighting for every frame, as the common-effect capture harness does.
            preview.lights[0].intensity = 2f;
            preview.lights[1].intensity = .8f;
            string file = output + "/" + name + ".png";
            // Several captures happen in one editor update. HDRP can reuse the first skinned
            // vertex buffer for that editor frame, despite changed bones. Bake a temporary
            // current-pose render snapshot; the real rig still drives the effect attachments.
            var snapshots = new List<Tuple<SkinnedMeshRenderer, GameObject, Mesh>>();
            try
            {
                foreach (var skinned in showcase.Model.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    if (!skinned.enabled) continue;
                    var mesh = new Mesh();
                    skinned.BakeMesh(mesh);
                    var visual = new GameObject(skinned.name + " capture pose");
                    visual.layer = skinned.gameObject.layer;
                    visual.transform.SetParent(skinned.transform, false);
                    visual.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = visual.AddComponent<MeshRenderer>();
                    renderer.sharedMaterials = skinned.sharedMaterials;
                    renderer.shadowCastingMode = skinned.shadowCastingMode;
                    renderer.receiveShadows = skinned.receiveShadows;
                    snapshots.Add(Tuple.Create(skinned, visual, mesh));
                    skinned.enabled = false;
                }
                preview.BeginStaticPreview(new Rect(0, 0, 960, 720));
                preview.Render(true);
                var image = preview.EndStaticPreview();
                try { File.WriteAllBytes(file, image.EncodeToPNG()); }
                finally { UnityEngine.Object.DestroyImmediate(image); }
            }
            finally
            {
                foreach (var snapshot in snapshots)
                {
                    snapshot.Item1.enabled = true;
                    UnityEngine.Object.DestroyImmediate(snapshot.Item2);
                    UnityEngine.Object.DestroyImmediate(snapshot.Item3);
                }
            }
            rows.Add(new CaptureRow { role = showcase.CurrentRole, file = file, seconds = showcase.Clock,
                facingRight = showcase.FacingRight, particles = showcase.ParticleCount, limitations = showcase.PreviewLimitations,
                actionMove = showcase.ActionMove, sourceFrame = showcase.SourceFrame, triggeredEvents = showcase.TriggeredEvents,
                rendererTextures = showcase.Instance ? showcase.Instance.GetComponentsInChildren<ParticleSystemRenderer>().Select(renderer =>
                    renderer.name + ": " + (renderer.sharedMaterial && renderer.sharedMaterial.HasProperty("_MainTex") && renderer.sharedMaterial.GetTexture("_MainTex") ?
                        renderer.sharedMaterial.GetTexture("_MainTex").name : "NO_TEXTURE")).ToArray() : Array.Empty<string>() });
        }

        [MenuItem("Remaster/Rendering/Capture Jin original action effects")]
        public static void CaptureActions()
        {
            Directory.CreateDirectory(Output);
            var rows = new List<CaptureRow>();
            var preview = new PreviewRenderUtility();
            float previousExposure = Shader.GetGlobalFloat("_CascadeExposure");
            try
            {
                var host = new GameObject("Jin original action capture");
                preview.AddSingleGO(host);
                var showcase = host.AddComponent<CharacterEffectsShowcase>();
                showcase.character = LoadCharacter(); showcase.showInterface = false;
                showcase.Initialize(false);
                Shader.SetGlobalFloat("_CascadeExposure", 1 / 1.2f);
                preview.lights[0].transform.rotation = Quaternion.Euler(35, 30, 0);
                preview.ambientColor = new Color(.3f, .3f, .32f);
                var cam = preview.camera;
                cam.transform.position = new Vector3(-.15f, 1.35f, -4.7f);
                cam.transform.LookAt(new Vector3(-.15f, 1.05f, 0));
                cam.fieldOfView = 38; cam.nearClipPlane = .05f; cam.farClipPlane = 60;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.045f, .055f, .075f);
                foreach (int move in new[] { 1779, 1840 })
                    foreach (bool right in new[] { true, false })
                    {
                        showcase.SelectAction(move); showcase.SetFacing(right);
                        foreach (int frame in new[] { 5, 6, 9, 15, 22 })
                        {
                            showcase.Advance((frame - 1) / 60f - showcase.Clock);
                            if (showcase.TriggeredEvents != (frame < 6 ? 0 : 1))
                                throw new InvalidOperationException("Original action " + move + " must dispatch its one effect at source frame 6; observed " +
                                    showcase.TriggeredEvents + " at frame " + showcase.SourceFrame);
                            Save(preview, showcase, "Jin-action-" + move + (right ? "-right-" : "-left-") + frame.ToString("000"), rows);
                        }
                    }
                showcase.SelectAction(1840); showcase.SetFacing(false); showcase.Seek(14 / 60f);
                cam.transform.position = new Vector3(-.55f, 1.65f, -2.1f);
                cam.transform.LookAt(new Vector3(-.55f, 1.5f, 0));
                Save(preview, showcase, "Jin-action-1840-close-015", rows);
                showcase.Seek(4 / 60f);
                if (showcase.TriggeredEvents != 0) throw new InvalidOperationException("Seeking before the original trigger failed to clear its instance.");
                showcase.Advance(1 / 60f);
                if (showcase.TriggeredEvents != 1) throw new InvalidOperationException("Original trigger failed after backward seek.");
                File.WriteAllText(Output + "/action-capture-report.json", JsonUtility.ToJson(new Report {
                    passed = true, systems = 1, presentation = "original Jin action + timed property through shared character dispatcher; visual parity not asserted",
                    captures = rows.ToArray()
                }, true));
            }
            finally { preview.Cleanup(); Shader.SetGlobalFloat("_CascadeExposure", previousExposure); }
            Debug.Log("Jin original action effects captured in " + Output);
        }

        // Each fighter's reachable original 0x802E rows through the shared dispatcher, captured 2 and
        // 6 frames after every trigger, facing both ways. Output: Validation/<Name>Effects. The job
        // fails if a capture's dispatch count differs from the original rows up to that frame.
        static readonly (string name, string code, (int move, int[] triggers)[] moves)[] CharacterActions = {
            ("Katarina", "ltn", new[] { (1678, new[] { 10, 20, 30, 40 }), (1980, new[] { 1, 14, 27, 40 }), (1883, new[] { 90 }) }),   // Ln_1lrk, Jn_SUPER_0, Ln_Lnage_n
            ("Kazuya", "kaz", new[] { (1712, new[] { 6 }), (1614, new[] { 8, 15, 20 }), (1565, new[] { 19, 25 }) }),                  // Kz_shoryu_S, Kz_kazup00, Kz_rprp01E
            ("Xiaoyu", "xia", new[] { (1791, new[] { 28, 43, 58 }) }),                                                                // Fu_torp00sp
            ("Alisa", "asa", new[] { (1989, new[] { 1, 10, 20 }), (1931, new[] { 1, 15, 30, 45 }) }),                                // Aa_steWP, Aa_atamasuper
        };

        [MenuItem("Remaster/Rendering/Capture Katarina original action effects")]
        public static void CaptureKatarinaActions() => CaptureCharacterActions("Katarina");

        [MenuItem("Remaster/Rendering/Capture Kazuya, Xiaoyu, Alisa and Katarina action effects")]
        public static void CaptureAllCharacterActions() { foreach (var entry in CharacterActions) CaptureCharacterActions(entry.name); }

        static void CaptureCharacterActions(string name)
        {
            var entry = CharacterActions.First(e => e.name == name);
            var moves = entry.moves;
            string output = "Validation/" + name + "Effects";
            Directory.CreateDirectory(output);
            var rows = new List<CaptureRow>();
            var preview = new PreviewRenderUtility();
            float previousExposure = Shader.GetGlobalFloat("_CascadeExposure");
            try
            {
                var host = new GameObject(name + " original action capture");
                preview.AddSingleGO(host);
                var showcase = host.AddComponent<CharacterEffectsShowcase>();
                showcase.character = LoadCharacter("Assets/_Project/Museum/" + name + ".asset"); showcase.characterCode = entry.code; showcase.showInterface = false;
                showcase.Initialize(false);
                Shader.SetGlobalFloat("_CascadeExposure", 1 / 1.2f);
                preview.lights[0].transform.rotation = Quaternion.Euler(35, 30, 0);
                preview.ambientColor = new Color(.3f, .3f, .32f);
                var cam = preview.camera;
                cam.transform.position = new Vector3(-.15f, 1.25f, -4.4f);
                cam.transform.LookAt(new Vector3(-.15f, .95f, 0));
                cam.fieldOfView = 38; cam.nearClipPlane = .05f; cam.farClipPlane = 60;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.045f, .055f, .075f);
                foreach (var (move, triggers) in moves)
                    foreach (bool right in new[] { true, false })
                    {
                        showcase.SelectAction(move); showcase.SetFacing(right);
                        foreach (int frame in triggers.SelectMany(t => new[] { t + 2, t + 6 }).Distinct().OrderBy(f => f))
                        {
                            showcase.Advance((frame - 1) / 60f - showcase.Clock);
                            int expected = triggers.Count(t => t <= showcase.SourceFrame);
                            if (showcase.TriggeredEvents != expected)
                                throw new InvalidOperationException("Original action " + move + " should have dispatched " + expected +
                                    " effects by frame " + showcase.SourceFrame + "; observed " + showcase.TriggeredEvents);
                            Save(preview, showcase, name + "-action-" + move + (right ? "-right-" : "-left-") + frame.ToString("000"), rows, output);
                        }
                    }
                File.WriteAllText(output + "/action-capture-report.json", JsonUtility.ToJson(new Report {
                    passed = true, systems = 1, presentation = "original " + name + " actions + timed property 0x802E through the shared character dispatcher; visual parity not asserted",
                    captures = rows.ToArray()
                }, true));
            }
            finally { preview.Cleanup(); Shader.SetGlobalFloat("_CascadeExposure", previousExposure); }
            Debug.Log(name + " original action effects captured in " + output);
        }

        static MuseumCharacter LoadCharacter(string path = CharacterPath)
        {
            var character = AssetDatabase.LoadAssetAtPath<MuseumCharacter>(path);
            if (!character) throw new FileNotFoundException(path);
            return character;
        }
    }
}
