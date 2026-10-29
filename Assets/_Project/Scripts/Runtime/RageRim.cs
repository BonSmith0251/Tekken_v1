using System.Collections.Generic;
using UnityEngine;

namespace Versus.Remaster
{
    /// <summary>The red Rage rim glow on one fighter (HitEffects/RageRim shader). Each skinned mesh gets
    /// a hidden copy sharing its bones; the copies draw only while <see cref="On"/>.</summary>
    public sealed class RageRim : MonoBehaviour
    {
        // Fitted to the reference rim (thin bright red edge, body otherwise unchanged); set on every
        // Create so a cached material never keeps old values.
        const float Power = 3.5f, Intensity = 2.2f, Push = .004f;
        static Material material;
        readonly List<SkinnedMeshRenderer> overlays = new List<SkinnedMeshRenderer>(), sources = new List<SkinnedMeshRenderer>();
        bool on;

        public static RageRim Create(GameObject fighter)
        {
            if (!material)
            {
                var shader = Resources.Load<Shader>("HitEffects/RageRim");
                if (!shader) { Debug.LogWarning("HitEffects/RageRim shader is missing."); return null; }
                material = new Material(shader) { name = "Rage rim" };
            }
            material.SetFloat("_Power", Power); material.SetFloat("_Intensity", Intensity); material.SetFloat("_Push", Push);
            return fighter.AddComponent<RageRim>();
        }

        public bool On
        {
            get => on;
            set
            {
                if (value == on) return;
                on = value;
                if (on) Build();
                Sync();
            }
        }

        // Built on first use and again if the wardrobe has added renderers since.
        void Build()
        {
            for (int i = overlays.Count - 1; i >= 0; i--)
                if (!overlays[i] || !sources[i]) { if (overlays[i]) Destroy(overlays[i].gameObject); overlays.RemoveAt(i); sources.RemoveAt(i); }
            foreach (var source in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!source.sharedMesh || overlays.Contains(source) || sources.Contains(source)) continue;
                var go = new GameObject(source.name + " rage rim");
                go.layer = source.gameObject.layer;
                go.transform.SetParent(source.transform, false);
                var overlay = go.AddComponent<SkinnedMeshRenderer>();
                overlay.sharedMesh = source.sharedMesh;
                overlay.bones = source.bones;
                overlay.rootBone = source.rootBone;
                overlay.localBounds = source.localBounds;
                overlay.updateWhenOffscreen = source.updateWhenOffscreen;
                overlay.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                overlay.receiveShadows = false;
                var materials = new Material[source.sharedMesh.subMeshCount];
                for (int i = 0; i < materials.Length; i++) materials[i] = material;
                overlay.sharedMaterials = materials;
                overlay.enabled = false;
                overlays.Add(overlay); sources.Add(source);
            }
        }

        void LateUpdate() { if (on) Sync(); }

        // Hidden source pieces (swapped wardrobe items) hide their rim too.
        void Sync()
        {
            for (int i = 0; i < overlays.Count; i++)
                if (overlays[i]) overlays[i].enabled = on && sources[i] && sources[i].enabled && sources[i].gameObject.activeInHierarchy;
        }
    }
}
