using System;
using System.Collections.Generic;
using UnityEngine;

namespace Versus.Remaster
{
    /// <summary>Character-owned native effect IDs. Only recovered dispatch paths are accepted;
    /// folder candidates and common impact roles are not automatic combat bindings.</summary>
    public sealed class CharacterBattleEffects : MonoBehaviour
    {
        [Serializable] sealed class Binding { public int effectId; public string role; }
        [Serializable] sealed class Registry { public string characterCode; public Binding[] bindings; }
        [Serializable] sealed class Bone { public int id; public string name; }
        [Serializable] sealed class Dispatch
        {
            public string characterCode;
            public int propertyId, effectId;
            public bool boneIndexOverridesSourceLocations;
            public string rotation;
            public float[] worldSpawnOffset;
            public Bone[] bones;
        }
        readonly Dictionary<int, string> roles = new Dictionary<int, string>();
        readonly Dictionary<int, Transform> nativeBones = new Dictionary<int, Transform>();
        readonly List<CascadeInstance> instances = new List<CascadeInstance>();
        readonly HashSet<int> warned = new HashSet<int>();
        CascadeEffects library;
        Dispatch dispatch;
        Transform actor;
        Transform[] rigBones;
        bool manual;
        public CascadeInstance LastInstance { get; private set; }
        public int ActiveCount { get { Prune(); return instances.Count; } }
        public int SpawnCount { get; private set; }
        public int LastBoneId { get; private set; } = -1;
        public Transform LastBone { get; private set; }

        public static CharacterBattleEffects Create(Transform parent, string characterCode, Transform actorRoot)
        {
            string folder = "CharacterEffects/" + characterCode;
            var registryAsset = Resources.Load<TextAsset>(folder + "/registry");
            var dispatchAsset = Resources.Load<TextAsset>(folder + "/native-dispatch");
            if (!registryAsset || !dispatchAsset) return null;
            var registry = JsonUtility.FromJson<Registry>(registryAsset.text);
            var recovered = JsonUtility.FromJson<Dispatch>(dispatchAsset.text);
            if (registry.characterCode != characterCode || recovered.characterCode != characterCode
                || recovered.propertyId != 0x802E || !recovered.boneIndexOverridesSourceLocations
                || recovered.rotation != "actor" || recovered.worldSpawnOffset == null || recovered.worldSpawnOffset.Length != 3)
                throw new InvalidOperationException("Unsupported character effect dispatch manifest: " + characterCode);
            var go = new GameObject(characterCode + " native battle effects");
            go.transform.SetParent(parent, false);
            var player = go.AddComponent<CharacterBattleEffects>();
            player.actor = actorRoot; player.dispatch = recovered;
            player.rigBones = actorRoot.GetComponentsInChildren<Transform>();
            foreach (var binding in registry.bindings) player.roles.Add(binding.effectId, binding.role);
            foreach (var bone in recovered.bones)
            {
                var resolved = Array.Find(player.rigBones, t => string.Equals(t.name, bone.name, StringComparison.OrdinalIgnoreCase));
                if (resolved) player.nativeBones.Add(bone.id, resolved);
            }
            player.library = CascadeEffects.Create(go.transform, folder, characterCode + " effect ID library");
            return player;
        }

        public bool TryPlayProperty(long propertyId, long value)
        {
            if (dispatch == null || propertyId != dispatch.propertyId || !roles.TryGetValue(dispatch.effectId, out var role)) return false;
            int boneId = (ushort)value;
            if (!nativeBones.TryGetValue(boneId, out var bone) || !bone)
            {
                if (warned.Add(boneId)) Debug.LogWarning("No verified rig bone for original character-effect bone " + boneId, this);
                return false;
            }
            // The dispatch manifest records the native helper's rotation convention separately
            // from the selected actor bone; no guessed hit-contact position is used here.
            Quaternion rotation = actor.rotation;
            var offset = new Vector3(dispatch.worldSpawnOffset[0], dispatch.worldSpawnOffset[1], dispatch.worldSpawnOffset[2]);
            var instance = library.Play(role, bone.position + offset, rotation, 1, rigBones, bone);
            if (!instance) return false;
            LastInstance = instance; LastBoneId = boneId; LastBone = bone; SpawnCount++;
            instances.Add(instance);
            if (manual) PrepareManual(instance);
            return true;
        }

        public void SetManualSimulation(bool value)
        {
            manual = value;
            foreach (var instance in instances)
            {
                if (!instance) continue;
                instance.enabled = !value;
                foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>())
                    if (value) ps.Pause(false); else ps.Play(false);
            }
        }
        static void PrepareManual(CascadeInstance instance)
        {
            instance.enabled = false;
            foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>())
            { ps.Simulate(0, false, true, false); ps.Pause(false); }
        }
        public void SimulateStep(float dt)
        {
            if (!manual) return;
            foreach (var instance in instances) if (instance && !instance.Finished) instance.SimulateStep(dt);
            Prune();
        }
        void LateUpdate() { Prune(); }
        void Prune()
        {
            for (int i = instances.Count - 1; i >= 0; i--)
                if (!instances[i] || instances[i].Finished)
                {
                    if (instances[i]) Retire(instances[i].gameObject);
                    instances.RemoveAt(i);
                }
        }
        public void Clear()
        {
            foreach (var instance in instances) if (instance) Retire(instance.gameObject);
            instances.Clear(); LastInstance = null; LastBone = null; LastBoneId = -1; SpawnCount = 0;
        }
        static void Retire(GameObject go)
        { go.SetActive(false); if (Application.isPlaying) Destroy(go); else DestroyImmediate(go); }
    }
}
