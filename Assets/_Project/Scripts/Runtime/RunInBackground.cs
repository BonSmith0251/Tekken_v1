using UnityEngine;

namespace Versus.Remaster
{
    // Every scene keeps playing while its window is unfocused (editor Play mode and players alike).
    public static class RunInBackground
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Enable() => Application.runInBackground = true;
    }
}
