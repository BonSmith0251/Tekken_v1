using System;
using System.Linq;
using UnityEngine;

namespace Versus.Remaster
{
    // The imported original stages (Resources/MuseumStages, built by MuseumStageImporter), shared by
    // env_scene and the versus scenes.
    public static class StageEnvironment
    {
        public static string[] Names
        {
            get
            {
                var list = Resources.Load<TextAsset>("MuseumStages/StageNames");
                return list ? list.text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).OrderBy(x => x).ToArray() : new string[0];
            }
        }

        // How the last Spawn placed its stage, for validation captures.
        public static string LastAlignment { get; private set; } = "";

        public static bool Exists(string name) => !string.IsNullOrEmpty(name) && Resources.Load<GameObject>("MuseumStages/" + name);

        // Instantiates a stage under parent with its fight-origin floor at the parent's height.
        public static GameObject Spawn(string name, Transform parent)
        {
            var prefab = Resources.Load<GameObject>("MuseumStages/" + name);
            if (!prefab) throw new InvalidOperationException("Stage " + name + " is not imported.");
            var stage = UnityEngine.Object.Instantiate(prefab, parent);
            stage.name = name;
            Place(stage, parent);
            if (Application.isPlaying) StartEffects(stage);
            // The original stage lighting replaces the scene's Render Settings rig while shown.
            var lighting = stage.GetComponent<StageLighting>();
            if (lighting) lighting.Activate();
            return stage;
        }

        // Puts a stage instance (spawned, or a prefab instance in a stage scene) on its match start:
        // turned to the original fight axis, the start marker over the parent's origin and the floor under
        // it at the parent's height.
        public static void Place(GameObject stage, Transform parent)
        {
            // The match starts at the original's GAME_START marker; without one, at the map origin.
            var info = stage.GetComponent<StageInfo>();
            var start = info && info.hasMatchStart ? info.matchStart : Vector3.zero;
            // Turn the stage so the marker's direction (the original's opening camera view) looks along
            // +Z like the versus camera; the fighters then stand on the original fight axis.
            var turn = Quaternion.AngleAxis(info && info.hasMatchStart ? -(info.matchYaw + 90) : 0, Vector3.up);
            stage.transform.localRotation = turn;
            var turned = turn * start;
            stage.transform.localPosition -= new Vector3(turned.x, 0, turned.z);
            var origin = parent ? parent.position : Vector3.zero;
            float top = stage.transform.position.y + (info && info.hasMatchStart ? start.y + 2 : 20), range = info && info.hasMatchStart ? 8 : 40;
            float closest = float.MaxValue, floorHeight = top - (info && info.hasMatchStart ? 2 : 20);
            // Maps use different world elevations (and multi-floor stages stack floors), so put the
            // walkable surface just below the start marker at the characters' feet.
            foreach (var filter in stage.GetComponentsInChildren<MeshFilter>())
            {
                var bounds = filter.GetComponent<Renderer>().bounds;
                if (!filter.sharedMesh || bounds.min.x > origin.x || bounds.max.x < origin.x || bounds.min.z > origin.z || bounds.max.z < origin.z) continue;
                // Glass floors and shallow water are walkable; only the invisible helper surfaces are not.
                if (filter.GetComponent<Renderer>().sharedMaterials.All(m => !m || m.name == "Hidden")) continue;
                var collider = filter.gameObject.AddComponent<MeshCollider>(); collider.sharedMesh = filter.sharedMesh;
                Physics.SyncTransforms();
                if (collider.Raycast(new Ray(new Vector3(origin.x, top, origin.z), Vector3.down), out var hit, range) && hit.normal.y > .4f && top - hit.point.y < closest)
                { closest = top - hit.point.y; floorHeight = hit.point.y; }
                UnityEngine.Object.DestroyImmediate(collider);
            }
            stage.transform.position -= Vector3.up * (floorHeight - origin.y);
            LastAlignment = stage.name + ": floor " + floorHeight.ToString("0.00") + " below top " + top.ToString("0.00") + (closest == float.MaxValue ? " (no hit)" : "") +
                ", yaw " + (info ? info.matchYaw : 0);
        }

        // Before removing a spawned stage: gives the scene back its own lighting.
        public static void Despawn(GameObject stage)
        {
            if (!stage) return;
            var lighting = stage.GetComponent<StageLighting>();
            if (lighting) lighting.Deactivate();
        }

        // Plays the stage's original particle systems (looping ones run until the stage is destroyed).
        // Editor captures call it too and then step the returned instances with SimulateStep.
        public static System.Collections.Generic.List<CascadeInstance> StartEffects(GameObject stage)
        {
            var started = new System.Collections.Generic.List<CascadeInstance>();
            var markers = stage.GetComponentsInChildren<StageEffectMarker>();
            if (markers.Length == 0) return started;
            var effects = CascadeEffects.Create(stage.transform, "StageEffects", "Stage effects");
            foreach (var marker in markers)
            {
                // One-shot systems (smokshot leaf and wood bursts) belong to break events the remaster
                // doesn't drive; only ambient, looping systems start with the stage.
                if (!effects.Has(marker.system) || !effects.Loops(marker.system)) continue;
                var t = marker.transform; var s = t.lossyScale;
                // CascadeEffects takes the source effect's X axis as local +z; the marker's UE X is +X.
                var instance = effects.Play(marker.system, t.position, t.rotation * Quaternion.Euler(0, 90, 0),
                    (Mathf.Abs(s.x) + Mathf.Abs(s.y) + Mathf.Abs(s.z)) / 3);
                if (instance) started.Add(instance);
            }
            return started;
        }
    }
}
