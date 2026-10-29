using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Versus.Remaster.Editor
{
    // Player builds ship only the wardrobe items saved in the Wardrobe scene. Before a build, each
    // wardrobe is copied into Resources/Wardrobe with the prefabs, hat-hair variants and tint
    // textures of unsaved pieces removed; their names stay so dressing can still hide them.
    // The copies are deleted after the build (or on the next editor load if a build failed).
    [InitializeOnLoad]
    public sealed class WardrobeBuildPackager : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        const string Source = "Assets/_Project/Wardrobe";
        const string Report = "Validation/Wardrobe/build-package.json";
        public int callbackOrder => 0;

        static WardrobeBuildPackager()
        {
            EditorApplication.delayCall += () => { if (!BuildPipeline.isBuildingPlayer) Remove(); };
            EditorApplication.update += Poll;
            EditorApplication.update += PollBuild;
        }

        static void Poll()
        {
            if (!File.Exists("Temp/package-wardrobe") || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete("Temp/package-wardrobe");
            try { DryRun(); } catch (Exception e) { Debug.LogException(e); File.WriteAllText("Temp/package-wardrobe-error.txt", e.ToString()); }
        }

        // Temp/build-localversus2 builds the versus player from the open editor; the flag file may
        // name an output folder (default Builds/LocalVersus2).
        static void PollBuild()
        {
            if (!File.Exists("Temp/build-localversus2") || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
            string folder = File.ReadAllText("Temp/build-localversus2").Trim();
            File.Delete("Temp/build-localversus2");
            try { ProjectSetup.BuildLocalVersus2(folder.Length > 0 ? folder : null); File.WriteAllText("Validation/Wardrobe/build-localversus2.txt", "succeeded " + DateTime.Now.ToString("s")); }
            catch (Exception e) { Debug.LogException(e); File.WriteAllText("Temp/build-localversus2-error.txt", e.ToString()); }
            // A failed build skips OnPostprocessBuild.
            finally { Remove(); }
        }

        public void OnPreprocessBuild(BuildReport report) => Package();
        public void OnPostprocessBuild(BuildReport report) => Remove();

        // Writes the report without leaving the copies behind.
        [MenuItem("Remaster/Wardrobe/Report saved items packaged for builds")]
        public static void DryRun()
        {
            Package();
            Remove();
            Debug.Log("Wardrobe build package report: " + Report);
        }

        public static void Package()
        {
            Remove();
            Directory.CreateDirectory(FightWardrobe.PackagedFolder);
            AssetDatabase.ImportAsset(FightWardrobe.PackagedFolder);
            var lines = new System.Collections.Generic.List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:CharacterWardrobe", new[] { Source }))
            {
                var source = AssetDatabase.LoadAssetAtPath<CharacterWardrobe>(AssetDatabase.GUIDToAssetPath(guid));
                string code = FightWardrobe.Code(source);
                var saved = FightWardrobe.Load(code);
                var copy = UnityEngine.Object.Instantiate(source);
                copy.name = source.name;
                int kept = 0, stripped = 0;
                var keptNames = new System.Collections.Generic.List<string>();
                foreach (var slot in copy.slots)
                {
                    saved.TryGetValue(slot.name, out var ticked);
                    foreach (var piece in slot.pieces)
                    {
                        if (piece.dependency) piece.dependencyRenderer = piece.dependency.GetComponentInChildren<SkinnedMeshRenderer>(true).name;
                        if (ticked != null && ticked.Contains(piece.piece)) { kept++; keptNames.Add(slot.name + ": " + piece.name); continue; }
                        piece.prefab = null; piece.dependency = null;
                        piece.tints = new CharacterWardrobe.Tint[0];
                        stripped++;
                    }
                }
                string copyPath = FightWardrobe.PackagedFolder + "/" + source.name + ".asset";
                AssetDatabase.CreateAsset(copy, copyPath);
                lines.Add("{\"character\":\"" + code + "\",\"kept\":" + kept + ",\"stripped\":" + stripped +
                    ",\"fullMB\":" + Megabytes(AssetDatabase.GetAssetPath(source), source.character) + ",\"packagedMB\":" + Megabytes(copyPath, source.character) + ",\"items\":[" +
                    string.Join(",", keptNames.Select(n => "\"" + n.Replace("\"", "\\\"") + "\"")) + "]}");
                if (kept == 0) Debug.LogWarning(source.name + " has no saved items; builds dress " + code + " in its model's own outfit.");
            }
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory(Path.GetDirectoryName(Report));
            File.WriteAllText(Report, "{\"passed\":true,\"wardrobes\":[\n" + string.Join(",\n", lines) + "\n]}");
        }

        // Source size of what a wardrobe adds to a build beyond its character asset (model, preset
        // outfits and motions, which the scenes ship anyway). Scripts are excluded.
        static string Megabytes(string path, MuseumCharacter character)
        {
            var shared = new System.Collections.Generic.HashSet<string>(character ? AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(character), true) : new string[0]);
            long bytes = AssetDatabase.GetDependencies(path, true).Where(p => !p.EndsWith(".cs") && !shared.Contains(p)).Sum(p => File.Exists(p) ? new FileInfo(p).Length : 0);
            return (bytes / 1048576.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        }

        static void Remove()
        {
            if (AssetDatabase.IsValidFolder(FightWardrobe.PackagedFolder)) AssetDatabase.DeleteAsset(FightWardrobe.PackagedFolder);
        }
    }
}
