using UnityEditor;

namespace Versus.Remaster.Editor
{
    // Keeps Player Settings > Resolution and Presentation > Run In Background on, so the
    // in-memory value Unity saves back to ProjectSettings.asset can't turn it off again.
    [InitializeOnLoad]
    public static class RunInBackgroundSetting
    {
        static RunInBackgroundSetting()
        {
            if (!PlayerSettings.runInBackground) PlayerSettings.runInBackground = true;
        }
    }
}
