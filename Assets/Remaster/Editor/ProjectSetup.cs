using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Versus.Remaster.Editor
{
    public static class ProjectSetup
    {
        const string ScenePath = "Assets/Remaster/Scenes/LocalVersus.unity";
        [MenuItem("Remaster/Open local versus scene")]
        public static void OpenScene() { if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath); }
        [MenuItem("Remaster/Open LocalVersus2 entrance scene")]
        public static void OpenLocalVersus2()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene("Assets/Remaster/Scenes/LocalVersus2.unity");
        }
        [MenuItem("Remaster/Set up project")]
        public static void Setup()
        {
            Directory.CreateDirectory("Assets/Remaster/Scenes");
            Directory.CreateDirectory("Assets/Remaster/Settings");
            Directory.CreateDirectory("Assets/Remaster/Resources");
            AssetDatabase.Refresh();
            const string surfacePath = "Assets/Remaster/Resources/PrototypeSurface.mat";
            if (!AssetDatabase.LoadAssetAtPath<Material>(surfacePath))
            {
                var surface = new Material(Shader.Find("Standard"));
                surface.EnableKeyword("_EMISSION"); surface.SetColor("_EmissionColor", Color.black);
                AssetDatabase.CreateAsset(surface, surfacePath);
            }
            var config = AssetDatabase.LoadAssetAtPath<RemasterConfig>("Assets/Remaster/Settings/LocalVersus.asset");
            if (!config)
            {
                config = ScriptableObject.CreateInstance<RemasterConfig>();
                AssetDatabase.CreateAsset(config, "Assets/Remaster/Settings/LocalVersus.asset");
            }
            ConfigureInput();
            PlayerSettings.companyName = "Local Remaster Workspace";
            PlayerSettings.productName = "Versus Remaster Prototype";
            PlayerSettings.defaultScreenWidth = 1280; PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.runInBackground = false;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            if (!File.Exists(ScenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var game = new GameObject("Local Versus - press Play").AddComponent<VersusGame>(); game.config = config;
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            else EditorSceneManager.OpenScene(ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
        }
        static void ConfigureInput()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/InputManager.asset");
            if (assets.Length == 0) throw new Exception("Input Manager settings were not generated");
            var settings = new SerializedObject(assets[0]);
            var axes = settings.FindProperty("m_Axes");
            for (int player = 1; player <= 2; player++) for (int axis = 0; axis < 2; axis++)
            {
                string name = "P" + player + "Stick" + (axis == 0 ? "X" : "Y");
                SerializedProperty entry = null;
                for (int j = 0; j < axes.arraySize; j++)
                    if (axes.GetArrayElementAtIndex(j).FindPropertyRelative("m_Name").stringValue == name) entry = axes.GetArrayElementAtIndex(j);
                if (entry == null) { axes.arraySize++; entry = axes.GetArrayElementAtIndex(axes.arraySize - 1); }
                entry.FindPropertyRelative("m_Name").stringValue = name;
                foreach (string field in new[] { "negativeButton", "positiveButton", "altNegativeButton", "altPositiveButton", "descriptiveName", "descriptiveNegativeName" })
                    entry.FindPropertyRelative(field).stringValue = "";
                entry.FindPropertyRelative("gravity").floatValue = 0;
                entry.FindPropertyRelative("dead").floatValue = .2f;
                entry.FindPropertyRelative("sensitivity").floatValue = 1;
                entry.FindPropertyRelative("snap").boolValue = false;
                entry.FindPropertyRelative("invert").boolValue = false;
                entry.FindPropertyRelative("type").intValue = 2;
                entry.FindPropertyRelative("axis").intValue = axis;
                entry.FindPropertyRelative("joyNum").intValue = player;
            }
            settings.ApplyModifiedProperties();
        }
        [MenuItem("Remaster/Validate combat rules")]
        public static void ValidateCombat()
        {
            int checks = CombatChecks.Run();
            Debug.Log("VERSUS_CHECKS_PASSED: " + checks);
            Directory.CreateDirectory("Validation");
            File.WriteAllText("Validation/combat-checks.json", "{\"passed\":true,\"checks\":" + checks + ",\"unity\":\"" + Application.unityVersion + "\"}");
        }
        public static void SetupAndCheck() { Setup(); ValidateCombat(); }
        [MenuItem("Remaster/Create Jin reference scene")]
        public static void CreateReferenceScene()
        {
            const string referenceScene = "Assets/Remaster/Scenes/JinReferencePose.unity";
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (File.Exists(referenceScene)) { EditorSceneManager.OpenScene(referenceScene); return; }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Jin - static reference pose only");
            string[] names = { "SK_CH_jin_fac", "SK_CH_jin_har_1p", "SK_CH_jin_bdu_1P", "SK_CH_jin_bdl_1P" };
            var material = new Material(Shader.Find("Standard")); material.color = new Color(.55f, .6f, .68f);
            string materialPath = "Assets/Remaster/Settings/ReferenceClay.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (existing) { UnityEngine.Object.DestroyImmediate(material); material = existing; }
            else AssetDatabase.CreateAsset(material, materialPath);
            int vertices = 0;
            foreach (string name in names)
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Remaster/ImportedReferences/Jin/" + name + ".obj");
                if (!asset) throw new Exception("Reference mesh missing: " + name);
                var part = (GameObject)PrefabUtility.InstantiatePrefab(asset, root.transform);
                foreach (var renderer in part.GetComponentsInChildren<Renderer>()) renderer.sharedMaterial = material;
                foreach (var mesh in part.GetComponentsInChildren<MeshFilter>()) vertices += mesh.sharedMesh.vertexCount;
            }
            if (vertices < 10000) throw new Exception("Imported reference geometry is unexpectedly empty");
            var camera = new GameObject("Reference camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(2.5f, 1.5f, 3.5f); camera.transform.LookAt(new Vector3(0, .95f, 0));
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.08f, .1f, .14f);
            var light = new GameObject("Reference light").AddComponent<Light>(); light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(35, -25, 0); light.intensity = 1.2f;
            RenderSettings.ambientLight = Color.gray;
            PrefabUtility.SaveAsPrefabAsset(root, "Assets/Remaster/ImportedReferences/Jin/JinReferencePose.prefab");
            EditorSceneManager.SaveScene(scene, referenceScene);
            File.WriteAllText("Validation/reference-import.json", "{\"importedParts\":4,\"vertices\":" + vertices + ",\"rigged\":false}");
        }
        [MenuItem("Remaster/Build Windows player")]
        public static void BuildWindows()
        {
            SetupAndCheck();
            CreateReferenceScene();
            EditorSceneManager.OpenScene(ScenePath);
            Directory.CreateDirectory("Builds/Windows");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { ScenePath }, locationPathName = "Builds/Windows/VersusRemaster.exe",
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.None });
            if (report.summary.result != BuildResult.Succeeded) throw new Exception("Windows build failed: " + report.summary.result);
            File.WriteAllText("Validation/build-result.json", "{\"succeeded\":true,\"errors\":" + report.summary.totalErrors + ",\"bytes\":" + report.summary.totalSize + "}");
        }
        [MenuItem("Remaster/Build LocalVersus2 Windows player")]
        public static void BuildLocalVersus2()
        {
            const string entranceScene = "Assets/Remaster/Scenes/LocalVersus2.unity";
            if (!File.Exists(entranceScene)) throw new FileNotFoundException("LocalVersus2 scene missing", entranceScene);
            Directory.CreateDirectory("Builds/LocalVersus2");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { entranceScene }, locationPathName = "Builds/LocalVersus2/LocalVersus2.exe",
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.None });
            if (report.summary.result != BuildResult.Succeeded)
                throw new Exception("LocalVersus2 build failed: " + report.summary.result);
        }
    }

    static class CombatChecks
    {
        static int checks;
        static void Check(bool condition, string name) { checks++; if (!condition) throw new Exception("Combat check failed: " + name); }
        static PlayerCommand N => PlayerCommand.Neutral;
        static BattleModel Ready(MatchRules rules = null)
        {
            var model = new BattleModel(rules ?? new MatchRules());
            for (int i = 0; i < 150; i++) model.Tick(N, N);
            model.fighters[0].x = -.55f; model.fighters[1].x = .55f;
            return model;
        }
        static void Attack(BattleModel model, int move, PlayerCommand defend)
        { var command = N; command.attack = move; model.Tick(command, defend); for (int i = 0; i < 55; i++) model.Tick(N, defend); }
        public static int Run()
        {
            checks = 0;
            var m = Ready(); Check(m.phase == MatchPhase.Fight, "intro ends");
            Attack(m, 0, N); Check(m.fighters[1].health == 93, "jab damages once, no multi-hit");
            m = Ready(); var guard = N; guard.guard = true; Attack(m, 0, guard); Check(m.fighters[1].health == 100, "standing guard stops high");
            m = Ready(); Attack(m, 2, guard); Check(m.fighters[1].health == 89, "low beats standing guard");
            guard.crouch = true; m = Ready(); Attack(m, 2, guard); Check(m.fighters[1].health == 100, "crouching guard stops low");
            m = Ready(); Attack(m, 1, guard); Check(m.fighters[1].health == 86, "mid beats crouching guard");
            m = Ready(); var crouch = N; crouch.crouch = true; Attack(m, 0, crouch); Check(m.fighters[1].health == 100, "crouch ducks high");
            m = Ready(); m.fighters[1].z = 1; Attack(m, 0, N); Check(m.fighters[1].health == 100, "sidestep evades");
            m = Ready(); m.fighters[1].x = 5; Attack(m, 3, N); Check(m.fighters[1].health == 100, "out of range misses");
            m = Ready(); var jab = N; jab.attack = 0; m.Tick(jab, jab); for (int i = 0; i < 12; i++) m.Tick(N, N);
            Check(m.fighters[0].health == 93 && m.fighters[1].health == 93, "same-frame trade symmetry");
            m = Ready(); m.fighters[0].health = 7; m.fighters[1].health = 7; m.Tick(jab, jab); for (int i = 0; i < 12; i++) m.Tick(N, N);
            Check(m.winner == -1 && m.fighters[0].wins == 0 && m.fighters[1].wins == 0, "double KO does not award win");
            m = Ready(new MatchRules { seconds = 1 }); m.fighters[1].health = 50;
            for (int i = 0; i < 60; i++) m.Tick(N, N);
            Check(m.phase == MatchPhase.RoundOver && m.winner == 0 && m.result == "TIME UP", "timeout awards higher health");
            m = Ready(new MatchRules { seconds = 1 }); for (int i = 0; i < 60; i++) m.Tick(N, N);
            Check(m.winner == -1, "equal-health timeout draw");
            m = Ready(new MatchRules { roundsToWin = 1 }); m.fighters[1].health = 1; Attack(m, 0, N);
            Check(m.phase == MatchPhase.MatchOver && m.winner == 0, "match winner");
            int ticks = m.ticksLeft; m.Tick(jab, jab); Check(m.ticksLeft == ticks, "finished match frozen");
            m.Restart(); Check(m.round == 1 && m.fighters[0].wins == 0 && m.fighters[1].health == 100, "rematch resets");
            m = Ready(); var walk = N; walk.x = -1; walk.z = 1; for (int i = 0; i < 900; i++) m.Tick(walk, N);
            Check(m.fighters[0].x >= -6 && m.fighters[0].z <= 2.8f, "arena bounds");
            m = Ready(); var invalid = N; invalid.attack = 99; m.Tick(invalid, N); Check(m.fighters[0].attack == -1, "invalid command ignored");
            return checks;
        }
    }
}
