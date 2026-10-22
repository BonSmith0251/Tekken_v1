using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Versus.Remaster
{
    public sealed class OriginalStageLightingData : ScriptableObject
    {
        [Serializable]
        public struct StageLight
        {
            public string name;
            public float nativeIntensity;
            public Color color;
            public Vector3 unrealPitchYawRoll;
            public bool characterOnly;
        }

        public VolumeProfile profile;
        public StageLight[] lights;
        [Tooltip("Renderer calibration, not a claim that original native units equal lux.")]
        public float luxPerNativeUnit = 1;
        public const uint EnvironmentLayer = 1, CharacterLayer = 2;

        // Stage-world alignment: original fight camera looks toward -UE Y;
        // Unity's fight camera looks toward +Z. This is shared with the sky importer.
        public static Vector3 UnrealDirectionToUnity(Vector3 direction) =>
            new Vector3(direction.x, direction.z, -direction.y);

        public static Vector3 LightDirection(Vector3 pitchYawRoll)
        {
            float pitch = pitchYawRoll.x * Mathf.Deg2Rad, yaw = pitchYawRoll.y * Mathf.Deg2Rad;
            // Directional emission follows UE +X. Roll does not change that direction.
            return UnrealDirectionToUnity(new Vector3(Mathf.Cos(pitch) * Mathf.Cos(yaw),
                Mathf.Cos(pitch) * Mathf.Sin(yaw), Mathf.Sin(pitch)));
        }

        public void Apply(VersusGame game)
        {
            foreach (var light in game.GetComponentsInChildren<Light>())
                if (light.name == "Key light" || light.name == "Cool rim light") light.enabled = false;
            foreach (var rig in FindObjectsByType<JinRig>(FindObjectsSortMode.None))
                foreach (var renderer in rig.GetComponentsInChildren<Renderer>(true))
                    renderer.renderingLayerMask |= EnvironmentLayer | CharacterLayer;

            var root = new GameObject("Original stg29 lighting");
            root.transform.SetParent(game.transform, false);
            foreach (var source in lights)
            {
                var light = new GameObject(source.name).AddComponent<Light>();
                light.transform.SetParent(root.transform, false);
                light.type = LightType.Directional;
                light.transform.rotation = Quaternion.LookRotation(LightDirection(source.unrealPitchYawRoll), Vector3.up);
                light.color = source.color;
                light.useColorTemperature = false;
                light.shadows = source.characterOnly ? LightShadows.None : LightShadows.Soft;
                var hd = light.gameObject.AddComponent<HDAdditionalLightData>();
                hd.SetIntensity(source.nativeIntensity * luxPerNativeUnit, LightUnit.Lux);
                uint mask = source.characterOnly ? CharacterLayer : EnvironmentLayer;
                hd.SetLightLayer((RenderingLayerMask)mask, (RenderingLayerMask)(EnvironmentLayer | CharacterLayer));
                hd.EnableShadows(!source.characterOnly);
            }
        }
    }
}
