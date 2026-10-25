using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Versus.Remaster.Editor
{
    public class ReferenceBrowser : EditorWindow
    {
        string search = "BP_BattleEvent";
        string[] lines = Array.Empty<string>();
        Vector2 scroll;
        static string Root => Path.GetFullPath("References/ExtractedVersus");
        [MenuItem("Remaster/Extracted script references")]
        public static void Open() => GetWindow<ReferenceBrowser>("Versus References");
        void OnEnable()
        { string index = Path.Combine(Root, "function-index.csv"); if (File.Exists(index)) lines = File.ReadAllLines(index); }
        void OnGUI()
        {
            EditorGUILayout.LabelField("Local two-player versus references", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("These are cooked Unreal functions and metadata. They do not run in Unity. Native C++ implementations, complete movesets and original animation are not recovered.", MessageType.Info);
            search = EditorGUILayout.TextField("Search names / paths", search);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Open reference folder")) EditorUtility.RevealInFinder(Root);
            if (GUILayout.Button("Porting notes")) EditorUtility.OpenWithDefaultApp(Path.Combine(Root, "LOCAL_VERSUS_NOTES.txt"));
            EditorGUILayout.EndHorizontal();
            var found = lines.Skip(1).Where(line => line.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            EditorGUILayout.LabelField(found.Length + " functions / showing first 200");
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (string line in found.Take(200)) EditorGUILayout.SelectableLabel(line, EditorStyles.wordWrappedMiniLabel, GUILayout.Height(48));
            EditorGUILayout.EndScrollView();
        }
    }
}
