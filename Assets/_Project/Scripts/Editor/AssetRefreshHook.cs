using System.IO;
using UnityEditor;

namespace Versus.Remaster.Editor
{
    // Unity only refreshes assets (and recompiles changed scripts) when its window gains focus.
    // Creating Temp/refresh-assets asks the open editor to refresh now; it waits while playing,
    // since a recompile during Play wipes the scenes' non-serialized state.
    [InitializeOnLoad]
    public static class AssetRefreshHook
    {
        static AssetRefreshHook() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (!File.Exists("Temp/refresh-assets") || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete("Temp/refresh-assets");
            AssetDatabase.Refresh();
        }
    }
}
