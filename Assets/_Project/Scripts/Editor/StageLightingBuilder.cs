using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Versus.Remaster.Editor
{
    // Builds a stage prefab's original lighting from Tools/import_museum_stages.py data: real HDRP lights
    // on the light nodes, one volume profile per stage (fixed exposure, sky ambient, height fog, bloom,
    // saturation, vignette, AO, indirect intensity) and the foliage wind. StageLighting switches it on
    // in place of the scene's Render Settings rig while the stage is shown.
    public static class StageLightingBuilder
    {
        [Serializable] public class LightData { public string type, channel, mobility; public float intensity, radius, inner, outer, falloffExponent, indirect; public float[] color; public bool inverseSquared, shadows, enabled; }
        [Serializable] public class SkyData { public float intensity; public float[] color, lower; public bool lowerIsBlack, specified; public string cubemap; }
        [Serializable] public class FogData { public float density, heightFalloff, maxOpacity, startDistance; public float[] color; public bool enabled; }
        [Serializable] public class WindData { public float strength, speed, minGust, maxGust; }
        [Serializable] public class PostData
        {
            public float BloomIntensity = -1, BloomThreshold = -1, FilmSaturation = -1, VignetteIntensity = -1,
                AmbientOcclusionIntensity = -1, AmbientOcclusionRadius = -1, IndirectLightingIntensity = -1;
            public float[] SceneColorTint, IndirectLightingColor;
        }

        const string Folder = "Assets/_Project/Stages/Lighting";
        // Calibration shared with the stg29 work: UE native units -> lux 1:1 at fixed EV 0.
        const float LuxPerNativeUnit = 1;
        // Static lights only reach the original through baked lightmaps, which are not converted;
        // the nearest ones to the match start stand in as shadowless realtime lights.
        const int BakedLightLimit = 96;
        const float SkyReflection = .35f;
        const int WindVertexBudget = 120000;
        const float BakedLumenScale = .25f, BakedCandelaScale = .5f, BakedLumenCap = 2000, MaxSkyIntensity = 1.5f, MaxSunLux = 20, OutlierSunLux = 8, BakedLuxBudget = 6;
        static readonly Regex Foliage = new Regex("(ivy|vine|leaf|leaves|foliage|tree|grass|bush|palm|frond|tuta|happa|kusa|cherry|bamboo|weed|fern|moss_card)", RegexOptions.IgnoreCase);
        static readonly Regex Hanging = new Regex("(ivy|vine|tuta|tsuta)", RegexOptions.IgnoreCase);

        public static void AddLight(Transform node, LightData d)
        {
            if (d == null || string.IsNullOrEmpty(d.type) || !d.enabled || d.intensity <= 0) return;
            var go = new GameObject(d.mobility == "static" ? "Light (baked in the original)" : "Light");
            go.transform.SetParent(node, false);
            // A UE light shines along its local +X; a Unity light along +Z.
            go.transform.localRotation = Quaternion.Euler(0, 90, 0);
            var light = go.AddComponent<Light>();
            light.type = d.type == "directional" ? LightType.Directional : d.type == "spot" ? LightType.Spot : LightType.Point;
            light.color = d.color != null && d.color.Length >= 3 ? new Color(d.color[0], d.color[1], d.color[2]) : Color.white;
            light.useColorTemperature = false;
            bool character = d.channel == "LC_CHARA_BOTH", stage = d.channel == "LC_STAGE";
            bool shadows = d.type == "directional" && d.shadows && !character;
            light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            if (light.type != LightType.Directional) light.range = Mathf.Max(.1f, d.radius);
            if (light.type == LightType.Spot)
            {
                // UE cone angles are half-angles.
                light.spotAngle = Mathf.Clamp(d.outer * 2, 1, 179);
                light.innerSpotAngle = Mathf.Clamp(d.inner * 2, 0, light.spotAngle);
            }
            var hd = go.AddComponent<HDAdditionalLightData>();
            // Every stage's sun is 0.2..20 except stg18's 200 (its lightmaps carried the scene; at 200 lux
            // realtime it burns white). Suns are capped at MaxSunLux (set by eye from that outlier).
            // An outlier above that range is replaced by a typical sun (the median of the stages' suns, 8)
            // rather than the top of the range, which still burned stg18's white pipes.
            if (light.type == LightType.Directional) hd.SetIntensity(d.intensity * LuxPerNativeUnit > MaxSunLux ? OutlierSunLux : d.intensity * LuxPerNativeUnit, LightUnit.Lux);
            // Static lights shaped only the baked lightmaps; their values were tuned for the bake (stg19 has
            // 240000-lumen spots, stg30 10 million) and burn the scene white as realtime lights. Stand-ins
            // are capped and dimmed (set by eye). Movable/stationary lights keep their own values:
            // inverse-squared ones in lumens, the older linear-falloff ones as candela (not recovered exactly).
            else if (d.mobility == "static")
            {
                float value = Mathf.Min(d.inverseSquared ? d.intensity * BakedLumenScale : d.intensity * BakedCandelaScale, BakedLumenCap);
                var unit = d.inverseSquared ? LightUnit.Lumen : LightUnit.Candela;
                hd.SetIntensity(value, unit);
                baked[hd] = (value, unit);
            }
            else if (d.inverseSquared) hd.SetIntensity(d.intensity, LightUnit.Lumen);
            else hd.SetIntensity(d.intensity, LightUnit.Candela);
            uint mask = character ? OriginalStageLightingData.CharacterLayer : stage ? OriginalStageLightingData.EnvironmentLayer
                : OriginalStageLightingData.EnvironmentLayer | OriginalStageLightingData.CharacterLayer;
            hd.SetLightLayer((RenderingLayerMask)mask, (RenderingLayerMask)mask);
            hd.EnableShadows(shadows);
        }

        // Keeps the nearest baked-only lights to the match start (see BakedLightLimit).
        // Baked-light stand-ins of the stage being built (AddLight records them; TrimBakedLights uses and clears it).
        static readonly Dictionary<HDAdditionalLightData, (float value, LightUnit unit)> baked = new Dictionary<HDAdditionalLightData, (float, LightUnit)>();

        // Keeps the nearest baked-only lights to the match start (see BakedLightLimit), then scales them so
        // together they add at most BakedLuxBudget at chest height on the match start. Per-light caps are
        // not enough where a stage packs hundreds of bake lights into a small room (stg18: 666 lights,
        // ~52 lux at the fighters; stg13 ~180; stg90 ~70), which burned the scene white.
        public static int TrimBakedLights(GameObject root, Vector3 matchStart)
        {
            var lights = root.GetComponentsInChildren<Light>(true).Where(l => l.name.StartsWith("Light (baked")).ToList();
            var start = root.transform.TransformPoint(matchStart);
            var chest = start + Vector3.up;
            float Lux(IEnumerable<KeyValuePair<HDAdditionalLightData, (float value, LightUnit unit)>> set)
            {
                float sum = 0;
                foreach (var kv in set)
                {
                    var light = kv.Key.GetComponent<Light>(); float d2 = (kv.Key.transform.position - chest).sqrMagnitude;
                    if (d2 >= light.range * light.range) continue;
                    sum += (kv.Value.unit == LightUnit.Lumen ? kv.Value.value / (4 * Mathf.PI) : kv.Value.value) / Mathf.Max(d2, 1);
                }
                return sum;
            }
            // Every baked light stays in the prefab with its bake intensity (StageBakedLight) for the stage
            // scenes' lightmap bake: all of them together are held to the same budget at the match start.
            var all = baked.Where(kv => kv.Key).ToList();
            float allLux = Lux(all);
            foreach (var kv in all)
            {
                var marker = kv.Key.gameObject.AddComponent<StageBakedLight>();
                marker.unit = kv.Value.unit;
                marker.bakeIntensity = kv.Value.value * (allLux > BakedLuxBudget ? BakedLuxBudget / allLux : 1);
            }
            // At runtime only the nearest stand in (realtime); the rest are switched off, not removed.
            int removed = 0;
            foreach (var l in lights.OrderBy(l => (l.transform.position - start).sqrMagnitude).Skip(BakedLightLimit))
            { l.gameObject.SetActive(false); removed++; }
            var kept = all.Where(kv => kv.Key.gameObject.activeSelf).ToList();
            foreach (var kv in kept) kv.Key.GetComponent<StageBakedLight>().standIn = true;
            float lux = Lux(kept);
            LastBakedLux = lux;
            if (lux > BakedLuxBudget)
                foreach (var (hd, (value, unit)) in kept.Select(kv => (kv.Key, kv.Value)))
                    hd.SetIntensity(value * BakedLuxBudget / lux, unit);
            baked.Clear();
            return removed;
        }
        public static float LastBakedLux { get; private set; }

        static T Override<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (!profile.TryGet<T>(out var c)) { c = profile.Add<T>(true); AssetDatabase.AddObjectToAsset(c, profile); }
            c.active = true;
            return c;
        }

        // One profile per stage. Fog height and sky rotation follow the spawned stage at runtime (StageLighting).
        public static VolumeProfile BuildProfile(string stage, SkyData sky, FogData fog, PostData post)
        {
            Directory.CreateDirectory(Folder);
            string path = Folder + "/" + stage + "_Lighting.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (!profile) { profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, path); }
            // The original's auto exposure is pinned (min = max brightness 1): fixed, as the stg29 calibration.
            var exposure = Override<Exposure>(profile);
            exposure.mode.Override(ExposureMode.Fixed); exposure.fixedExposure.Override(0);
            // UE 4.13's filmic tonemapper is ACES-based.
            Override<Tonemapping>(profile).mode.Override(TonemappingMode.ACES);

            var visual = Override<VisualEnvironment>(profile);
            Cubemap cube = sky != null && !string.IsNullOrEmpty(sky.cubemap) ? AssetDatabase.LoadAssetAtPath<Cubemap>(Folder + "/" + sky.cubemap + ".t7sky") : null;
            // Sky-light intensity scales the ambient; some stages push it to compensate their bake (stg30: 5).
            float skyIntensity = sky != null && sky.color != null ? Mathf.Min(sky.intensity, MaxSkyIntensity) : 1;
            if (cube)
            {
                visual.skyType.Override((int)SkyType.HDRI);
                var hdri = Override<HDRISky>(profile);
                hdri.hdriSky.Override(cube);
                hdri.skyIntensityMode.Override(SkyIntensityMode.Multiplier); hdri.multiplier.Override(skyIntensity);
                hdri.updateMode.Override(EnvironmentUpdateMode.OnChanged);
            }
            else
            {
                visual.skyType.Override((int)SkyType.Gradient);
                var gradient = Override<GradientSky>(profile);
                var c = sky != null && sky.color != null && sky.color.Length >= 3 ? new Color(sky.color[0], sky.color[1], sky.color[2]) * skyIntensity : new Color(.38f, .43f, .54f);
                var low = sky != null && sky.lower != null && sky.lower.Length >= 3 && !sky.lowerIsBlack ? new Color(sky.lower[0], sky.lower[1], sky.lower[2]) * skyIntensity : c * .25f;
                gradient.top.Override(c); gradient.middle.Override(c * .8f); gradient.bottom.Override(low);
            }
            var indirect = Override<IndirectLightingController>(profile);
            indirect.indirectDiffuseLightingMultiplier.Override(post != null && post.IndirectLightingIntensity >= 0 ? post.IndirectLightingIntensity : 1);
            // The original's box/sphere reflection captures keep interiors and water from mirroring the open
            // sky; they are not converted, so the sky reflection is toned down (set by eye).
            indirect.reflectionLightingMultiplier.Override(SkyReflection);

            var hdFog = Override<Fog>(profile);
            bool fogOn = fog != null && fog.color != null && fog.enabled && fog.density > 0;
            hdFog.enabled.Override(fogOn);
            if (fogOn)
            {
                // UE height fog: density / 1000 per cm, falling off by exp(-falloff / 1000 * height in cm).
                hdFog.meanFreePath.Override(Mathf.Clamp(1 / (fog.density * .1f), 1, 100000));
                hdFog.maximumHeight.Override(10 / Mathf.Max(.01f, fog.heightFalloff));
                hdFog.colorMode.Override(FogColorMode.ConstantColor);
                hdFog.color.Override(new Color(fog.color[0], fog.color[1], fog.color[2]));
                hdFog.maxFogDistance.Override(5000);
            }
            var bloom = Override<Bloom>(profile);
            bloom.intensity.Override(post != null && post.BloomIntensity >= 0 ? post.BloomIntensity : 0);
            if (post != null && post.BloomThreshold > 0) bloom.threshold.Override(post.BloomThreshold);
            var color = Override<ColorAdjustments>(profile);
            color.saturation.Override(post != null && post.FilmSaturation >= 0 ? (post.FilmSaturation - 1) * 100 : 0);
            if (post != null && post.SceneColorTint != null && post.SceneColorTint.Length >= 3)
                color.colorFilter.Override(new Color(post.SceneColorTint[0], post.SceneColorTint[1], post.SceneColorTint[2]));
            var vignette = Override<Vignette>(profile);
            vignette.intensity.Override(post != null && post.VignetteIntensity >= 0 ? post.VignetteIntensity : 0);
            var ao = Override<ScreenSpaceAmbientOcclusion>(profile);
            ao.intensity.Override(post != null && post.AmbientOcclusionIntensity >= 0 ? post.AmbientOcclusionIntensity : .5f);
            ao.radius.Override(Mathf.Clamp(post != null && post.AmbientOcclusionRadius > 0 ? post.AmbientOcclusionRadius * .01f : .5f, .25f, 5));
            foreach (var c in profile.components) EditorUtility.SetDirty(c);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        // StageLighting on the prefab root: profile, fog base, and the wind for foliage renderers.
        public static void Finish(GameObject root, string stage, VolumeProfile profile, Transform fogNode, Transform windNode, WindData wind)
        {
            var lighting = root.AddComponent<StageLighting>();
            lighting.profile = profile;
            lighting.fogBase = fogNode ? root.transform.InverseTransformPoint(fogNode.position).y : 0;
            var foliage = new List<MeshFilter>(); var hanging = new List<bool>();
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var names = r.sharedMaterials.Where(m => m).Select(m => m.name).ToArray();
                if (!names.Any(n => Foliage.IsMatch(n))) continue;
                var f = r.GetComponent<MeshFilter>(); if (!f || !f.sharedMesh || f.sharedMesh.vertexCount > 60000) continue;
                foliage.Add(f); hanging.Add(names.Any(n => Hanging.IsMatch(n)));
            }
            if (foliage.Count == 0) return;
            // The sway runs on the CPU every frame: keep the foliage nearest the fighters within a budget
            // (stg31 has ~650k foliage vertices; stg01's ivy ~2k).
            var start = root.GetComponent<StageInfo>() is var info && info ? root.transform.TransformPoint(info.matchStart) : root.transform.position;
            var order = Enumerable.Range(0, foliage.Count).OrderBy(i => (foliage[i].transform.position - start).sqrMagnitude).ToList();
            var keep = new List<int>(); int budget = WindVertexBudget;
            foreach (var i in order) { int n = foliage[i].sharedMesh.vertexCount; if (n > budget) continue; budget -= n; keep.Add(i); }
            foliage = keep.Select(i => foliage[i]).ToList(); hanging = keep.Select(i => hanging[i]).ToList();
            var w = root.AddComponent<StageWind>();
            w.foliage = foliage.ToArray(); w.hanging = hanging.ToArray();
            // WindDirectionalSource blows along its +X (UE); without one, the engine default.
            w.direction = windNode ? root.transform.InverseTransformDirection(windNode.right) : Vector3.right;
            if (wind != null && wind.strength > 0) { w.strength = wind.strength; w.speed = wind.speed; w.minGust = wind.minGust; w.maxGust = wind.maxGust; }
        }
    }
}
