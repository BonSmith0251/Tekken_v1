using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Versus.Remaster
{
    // Data written by Tools/HitEffects/export_hit_effects.py: Tekken 7's common battle effects
    // (BP_CommonEffectManager roles) with each Cascade emitter decoded into metres and Unity axes.
    // Curves are flat strided arrays: sizeLife/velocityLife (t,x,y,z), colorLife/colorScale
    // (t,r,g,b,a), drag (t,v), bursts (count, countLow, time).
    [Serializable] public sealed class CascadeEmitterData
    {
        public string name, kind, align, texture, mesh, blend, axisLock, shape;
        public float intensity = 1, duration = 1, delay, rate, cameraOffset, shapeRadius, shapeHeight, shapeVelocityScale, alphaClip;
        public int loops = 1;
        public bool distortion, mask, floor, bones, gpu, shapeVelocity, shapeSurface, hasLight;
        public float[] scroll, tiling, life, bursts, sizeMin, sizeMax, sizeLife, colorMin, colorMax, colorLife, colorScale,
            velocityMin, velocityMax, radial, coneAngle, coneSpeed, coneDir, velocityLife, accel, drag, shapeOffset,
            locationMin, locationMax, rotation, rotationRate, meshRotationMin, meshRotationMax, meshRotationRateMin,
            meshRotationRateMax, emitterRotation, lightColor, vectorField, alphaClipLife, tint;
        public float lightBrightness, lightRadiusScale;
    }
    [Serializable] public sealed class CascadeSystemData { public string name; public CascadeEmitterData[] emitters; }
    [Serializable] public sealed class CascadeRoleData { public string role, system; }
    [Serializable] sealed class CascadeLibraryData { public CascadeRoleData[] roles; public CascadeSystemData[] systems; public string[] limitations; }
    [Serializable] sealed class CascadeMeshData { public float[] positions, uv; public int[] indices; }

    /// <summary>
    /// Plays the original game's common effects by role (HitFX, GuardFX, CriticalHit3FX, ...).
    /// Each emitter spawns its particles with the source distributions; Unity modules carry the
    /// over-life colour, size, drag, force and speed curves. Vector-field turbulence, dynamic
    /// material parameters and refraction-only emitters are not reproduced.
    /// </summary>
    public sealed class CascadeEffects : MonoBehaviour
    {
        public const string Hit = "HitFX", Counter = "CriticalHit3FX", Guard = "GuardFX", Pierce = "PierceFX",
            Blast = "BlastFX", Smoke = "SmokeFX", Rage = "RageFX", RageAura = "RageAuraFX",
            PunishHigh = "PracticePunishNoticeHiFX", PunishMid = "PracticePunishNoticeMidFX", PunishLow = "PracticePunishNoticeLoFX",
            KO = "KOFX";
        // Not recovered: how strongly the original glare power fed its bloom. Tuned by eye.
        public float glareGain = 1;
        readonly Dictionary<string, CascadeSystemData> roles = new Dictionary<string, CascadeSystemData>();
        readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
        readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
        Shader shader;
        static readonly int ExposureId = Shader.PropertyToID("_CascadeExposure");

        public static CascadeEffects Create(Transform parent)
        {
            var go = new GameObject("Battle effects");
            if (parent) go.transform.SetParent(parent, false);
            var effects = go.AddComponent<CascadeEffects>();
            effects.Load();
            return effects;
        }

        void Load()
        {
            shader = Resources.Load<Shader>("HitEffects/CascadeParticle");
            var asset = Resources.Load<TextAsset>("HitEffects/effects");
            if (!shader || !asset) { Debug.LogWarning("Battle effects are missing; run Tools/HitEffects/export_hit_effects.py."); return; }
            var data = JsonUtility.FromJson<CascadeLibraryData>(asset.text);
            var systems = new Dictionary<string, CascadeSystemData>();
            foreach (var s in data.systems) systems[s.name] = s;
            foreach (var r in data.roles) if (systems.TryGetValue(r.system, out var s)) roles[r.role] = s;
        }

        public bool Has(string role) => roles.ContainsKey(role);

        /// <summary>Plays a role at a world position. Local +z of the rotation is the source's
        /// effect X axis (the hit direction). Bones, when given, seed character-bone emitters.</summary>
        public CascadeInstance Play(string role, Vector3 position, Quaternion rotation, float scale = 1, Transform[] bones = null)
        {
            if (!roles.TryGetValue(role, out var system)) return null;
            var go = new GameObject(role);
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(position, rotation);
            var instance = go.AddComponent<CascadeInstance>();
            instance.Begin(this, system, scale, bones);
            return instance;
        }

        void LateUpdate()
        {
            // HDRP pre-exposes lit colour; this unlit shader applies the same fixed-exposure factor.
            float exposure = 1 / 1.2f;
            var stack = VolumeManager.instance?.stack;
            var settings = stack?.GetComponent<Exposure>();
            if (settings != null && settings.mode.value == ExposureMode.Fixed)
                exposure = 1 / (1.2f * Mathf.Pow(2, settings.fixedExposure.value));
            Shader.SetGlobalFloat(ExposureId, exposure);
        }

        internal Material MaterialFor(CascadeEmitterData e, float peak)
        {
            // The original materials multiply by a glare power (1..100) and let bloom and the tonemap
            // whiten the core. Without bloom here, the shader whitens hot colour itself; the square
            // root keeps a glare of 20 at about 4.5x (the reference video's sparks read white-yellow).
            float intensity = peak * Mathf.Sqrt(Mathf.Max(1, e.intensity)) * glareGain;
            string key = e.texture + "|" + e.blend + "|" + e.mask + "|" + intensity.ToString("0.###") + "|" + e.cameraOffset + "|" +
                         Vec(e.scroll) + "|" + Vec(e.tiling) + "|" + e.alphaClip + "|" + Vec(e.tint);
            if (materials.TryGetValue(key, out var m)) return m;
            m = new Material(shader) { name = e.name, renderQueue = (int)RenderQueue.Transparent };
            if (!string.IsNullOrEmpty(e.texture)) m.mainTexture = TextureFor(e.texture);
            m.SetFloat("_Intensity", intensity);
            if (e.tint != null && e.tint.Length == 3) m.SetColor("_Tint", new Color(e.tint[0], e.tint[1], e.tint[2]));
            m.SetFloat("_MaskFromRed", e.mask ? 1 : 0);
            m.SetFloat("_CameraOffset", e.cameraOffset);
            m.SetFloat("_AlphaClip", e.alphaClip);
            if (e.scroll != null && e.scroll.Length >= 2) m.SetVector("_Scroll", new Vector4(e.scroll[0], e.scroll[1]));
            if (e.tiling != null && e.tiling.Length >= 2) m.SetVector("_Tiling", new Vector4(e.tiling[0], e.tiling[1]));
            m.SetFloat("_SrcBlend", (float)BlendMode.One);
            m.SetFloat("_DstBlend", (float)(e.blend == "add" ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            materials[key] = m;
            return m;
        }

        static string Vec(float[] v) => v == null ? "" : string.Join(",", v);

        Texture2D TextureFor(string name)
        {
            if (textures.TryGetValue(name, out var t)) return t;
            t = Resources.Load<Texture2D>("HitEffects/Textures/" + name);
            textures[name] = t;
            return t;
        }

        internal Mesh MeshFor(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (meshes.TryGetValue(name, out var mesh)) return mesh;
            var asset = Resources.Load<TextAsset>("HitEffects/Meshes/" + name);
            if (asset)
            {
                var d = JsonUtility.FromJson<CascadeMeshData>(asset.text);
                var vertices = new Vector3[d.positions.Length / 3];
                var uv = new Vector2[vertices.Length];
                for (int i = 0; i < vertices.Length; i++)
                {
                    vertices[i] = new Vector3(d.positions[i * 3], d.positions[i * 3 + 1], d.positions[i * 3 + 2]);
                    if (d.uv != null && d.uv.Length >= i * 2 + 2) uv[i] = new Vector2(d.uv[i * 2], 1 - d.uv[i * 2 + 1]);
                }
                mesh = new Mesh { name = name, indexFormat = vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.vertices = vertices; mesh.uv = uv; mesh.triangles = d.indices;
                mesh.RecalculateBounds();
            }
            meshes[name] = mesh;
            return mesh;
        }
    }

    /// <summary>One playing effect: emits each emitter's bursts and rate over its duration and loops,
    /// then removes itself once every particle has died. Looping systems run until Stop().</summary>
    public sealed class CascadeInstance : MonoBehaviour
    {
        sealed class Emitter
        {
            public CascadeEmitterData data;
            public ParticleSystem system;
            public float accumulator, peak, sizeScaleSpeed;
            public int loop = -1;
            public bool[] burstsDone;
            public Gradient gradient;
            public Color startScale = Color.white;
            public bool colorFromLife;
        }
        readonly List<Emitter> emitters = new List<Emitter>();
        float time, scale = 1;
        bool stopped;
        Transform[] bones;
        CascadeEffects owner;
        static readonly System.Random Random = new System.Random();

        public bool Finished { get; private set; }

        internal void Begin(CascadeEffects effects, CascadeSystemData system, float effectScale, Transform[] boneList)
        {
            owner = effects; scale = effectScale; bones = boneList;
            foreach (var e in system.emitters)
            {
                if (e.distortion) continue;
                var emitter = new Emitter { data = e };
                if (e.kind != "light" && !(string.IsNullOrEmpty(e.texture) && e.hasLight)) emitter.system = Build(e, emitter);
                if (emitter.system || e.hasLight) emitters.Add(emitter);
            }
        }

        /// <summary>Stops spawning; particles already alive finish their lives.</summary>
        public void Stop() => stopped = true;

        void Update() { if (Application.isPlaying) Step(Time.deltaTime); }

        /// <summary>Advances spawning by dt. Play mode calls it every frame; editor captures call it
        /// directly and then simulate the particle systems by the same step.</summary>
        public void Step(float dt)
        {
            time += dt;
            bool spawning = false, alive = false;
            foreach (var e in emitters)
            {
                var d = e.data;
                float local = time - d.delay;
                if (e.system && e.system.particleCount > 0) alive = true;
                if (stopped || local < 0) { spawning |= !stopped; continue; }
                float cycle = d.duration > 0 ? d.duration : 1;
                int loop = Mathf.FloorToInt(local / cycle);
                if (d.loops > 0 && loop >= d.loops) continue;
                spawning = true;
                if (loop != e.loop) { e.loop = loop; e.burstsDone = new bool[(d.bursts?.Length ?? 0) / 3]; }
                float inCycle = local - loop * cycle;
                for (int b = 0; b < e.burstsDone.Length; b++)
                {
                    if (e.burstsDone[b] || inCycle < d.bursts[b * 3 + 2]) continue;
                    e.burstsDone[b] = true;
                    int high = (int)d.bursts[b * 3], low = (int)d.bursts[b * 3 + 1];
                    Emit(e, low >= 0 ? Random.Next(Mathf.Min(low, high), Mathf.Max(low, high) + 1) : high);
                }
                if (d.rate > 0)
                {
                    e.accumulator += d.rate * dt;
                    int n = Mathf.FloorToInt(e.accumulator);
                    e.accumulator -= n;
                    if (n > 0) Emit(e, Mathf.Min(n, 200));
                }
            }
            if (!spawning && !alive && time > .05f) { Finished = true; if (Application.isPlaying) Destroy(gameObject); }
        }

        /// <summary>Editor capture: step spawning and simulate every emitter by dt.</summary>
        public void SimulateStep(float dt)
        {
            Step(dt);
            foreach (var e in emitters) if (e.system) e.system.Simulate(dt, false, false, false);
        }

        ParticleSystem Build(CascadeEmitterData d, Emitter emitter)
        {
            var go = new GameObject(d.name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false; main.loop = false; main.duration = 1;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSize3D = true; main.startRotation3D = d.kind == "mesh";
            main.maxParticles = 1000;
            main.gravityModifier = 0;
            var emission = ps.emission; emission.enabled = false;
            var shape = ps.shape; shape.enabled = false;

            emitter.gradient = BuildGradient(d, out emitter.peak, out emitter.startScale, out emitter.colorFromLife);
            var col = ps.colorOverLifetime; col.enabled = true; col.color = new ParticleSystem.MinMaxGradient(emitter.gradient);

            if (d.sizeLife != null && d.sizeLife.Length >= 8)
            {
                var size = ps.sizeOverLifetime; size.enabled = true; size.separateAxes = true;
                // Sprites: x width, y height. Meshes: UE (x, y, z) scale is Unity (z, x, y).
                bool mesh = d.kind == "mesh";
                size.x = new ParticleSystem.MinMaxCurve(1, Curve(d.sizeLife, 4, mesh ? 2 : 1));
                size.y = new ParticleSystem.MinMaxCurve(1, Curve(d.sizeLife, 4, mesh ? 3 : 2));
                size.z = new ParticleSystem.MinMaxCurve(1, Curve(d.sizeLife, 4, mesh ? 1 : 3));
            }
            // A GPU vector field pulls each particle's velocity toward the field by its tightness every
            // frame, which catches the spawn burst and swirls it in place. The field shape is not
            // decoded: the pull becomes drag (-ln(1 - tightness) per 60 Hz frame, capped) and the
            // field strength becomes noise of the same speed.
            float fieldDrag = 0;
            if (d.vectorField != null && d.vectorField.Length == 2 && d.vectorField[1] > 0)
            {
                fieldDrag = Mathf.Min(25, -Mathf.Log(Mathf.Max(1e-3f, 1 - d.vectorField[1])) * 60);
                var noise = ps.noise; noise.enabled = true;
                noise.strength = d.vectorField[0] * scale; noise.frequency = 3; noise.scrollSpeed = .5f;
                noise.damping = true; noise.quality = ParticleSystemNoiseQuality.Medium;
            }
            if (d.drag != null && d.drag.Length >= 4 || fieldDrag > 0)
            {
                var limit = ps.limitVelocityOverLifetime; limit.enabled = true; limit.limit = 1000;
                var curve = d.drag != null && d.drag.Length >= 4 ? Curve(d.drag, 2, 1) : AnimationCurve.Constant(0, 1, 0);
                if (fieldDrag > 0)
                {
                    var keys = curve.keys;
                    for (int k = 0; k < keys.Length; k++) keys[k].value = Mathf.Max(keys[k].value, fieldDrag);
                    curve.keys = keys;
                }
                limit.drag = new ParticleSystem.MinMaxCurve(1, curve);
                limit.multiplyDragByParticleSize = false; limit.multiplyDragByParticleVelocity = false;
            }
            if (d.velocityLife != null && d.velocityLife.Length >= 8)
            {
                var vel = ps.velocityOverLifetime; vel.enabled = true;
                vel.speedModifier = new ParticleSystem.MinMaxCurve(1, Curve(d.velocityLife, 4, 3));
            }
            if (d.accel != null && d.accel.Length == 3 && (d.accel[0] != 0 || d.accel[1] != 0 || d.accel[2] != 0))
            {
                var force = ps.forceOverLifetime; force.enabled = true; force.space = ParticleSystemSimulationSpace.World;
                var a = transform.rotation * new Vector3(d.accel[0], d.accel[1], d.accel[2]) * scale;
                force.x = a.x; force.y = a.y; force.z = a.z;
            }

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = owner.MaterialFor(d, emitter.peak);
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            if (d.kind == "mesh")
            {
                var mesh = owner.MeshFor(d.mesh);
                if (!mesh) { DestroyImmediate(go); return null; }
                renderer.renderMode = ParticleSystemRenderMode.Mesh; renderer.mesh = mesh;
                renderer.alignment = d.align == "velocity" ? ParticleSystemRenderSpace.Velocity : ParticleSystemRenderSpace.World;
            }
            else if (d.align == "velocity")
            {
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.lengthScale = 1; renderer.velocityScale = 0;
            }
            else renderer.renderMode = d.axisLock == "EPAL_Z" ? ParticleSystemRenderMode.HorizontalBillboard : ParticleSystemRenderMode.Billboard;
            if (d.alphaClipLife != null && d.alphaClipLife.Length >= 4)
            {
                // The source's AlphaClip dynamic parameter over each particle's life, read by the shader
                // from TEXCOORD0.z (Custom1.x packed after the UVs).
                var custom = ps.customData; custom.enabled = true;
                custom.SetMode(ParticleSystemCustomData.Custom1, ParticleSystemCustomDataMode.Vector);
                custom.SetVectorComponentCount(ParticleSystemCustomData.Custom1, 1);
                custom.SetVector(ParticleSystemCustomData.Custom1, 0, new ParticleSystem.MinMaxCurve(1, Curve(d.alphaClipLife, 2, 1)));
                renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream> {
                    ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal, ParticleSystemVertexStream.Color,
                    ParticleSystemVertexStream.UV, ParticleSystemVertexStream.Custom1X });
            }
            // In edit mode (captures) a never-simulated system drops emitted particles; prime it instead.
            if (Application.isPlaying) ps.Play(); else ps.Simulate(0, false, true, false);
            return ps;
        }

        static AnimationCurve Curve(float[] rows, int stride, int column)
        {
            var curve = new AnimationCurve();
            for (int i = 0; i + stride <= rows.Length; i += stride) curve.AddKey(new Keyframe(rows[i], rows[i + column]));
            for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0);
            return curve;
        }

        // Normalises the source HDR colour (ColorOverLife sets it, ColorScaleOverLife multiplies it) into a
        // gradient no brighter than 1; the peak becomes the material intensity.
        static Gradient BuildGradient(CascadeEmitterData d, out float peak, out Color startScale, out bool fromLife)
        {
            fromLife = d.colorLife != null && d.colorLife.Length >= 10;
            int n = fromLife ? d.colorLife.Length / 5 : d.colorScale != null ? d.colorScale.Length / 5 : 2;
            var rgb = new Color[Mathf.Max(2, n)]; var times = new float[rgb.Length];
            float startPeak = 1;
            if (!fromLife && d.colorMax != null && d.colorMax.Length >= 3) startPeak = Mathf.Max(d.colorMax[0], d.colorMax[1], d.colorMax[2], 1e-4f);
            peak = 0;
            for (int i = 0; i < rgb.Length; i++)
            {
                float t = rgb.Length > 1 ? i / (float)(rgb.Length - 1) : 0;
                Color c = fromLife ? Sample(d.colorLife, t) : new Color(startPeak, startPeak, startPeak, 1);
                if (!fromLife) c.a = 1;
                if (d.colorScale != null && d.colorScale.Length >= 5) c *= Sample(d.colorScale, t);
                rgb[i] = c; times[i] = t;
                peak = Mathf.Max(peak, c.r, c.g, c.b);
            }
            if (peak < 1e-4f) peak = 1;
            var colorKeys = new GradientColorKey[Mathf.Min(8, rgb.Length)];
            var alphaKeys = new GradientAlphaKey[colorKeys.Length];
            for (int k = 0; k < colorKeys.Length; k++)
            {
                int i = Mathf.RoundToInt(k * (rgb.Length - 1) / (float)Mathf.Max(1, colorKeys.Length - 1));
                var c = rgb[i];
                colorKeys[k] = new GradientColorKey(new Color(c.r / peak, c.g / peak, c.b / peak), times[i]);
                alphaKeys[k] = new GradientAlphaKey(Mathf.Clamp01(c.a), times[i]);
            }
            var g = new Gradient(); g.SetKeys(colorKeys, alphaKeys);
            startScale = new Color(1 / startPeak, 1 / startPeak, 1 / startPeak, 1);
            return g;
        }

        static Color Sample(float[] rows, float t)
        {
            int count = rows.Length / 5;
            if (count == 0) return Color.white;
            if (t <= rows[0] || count == 1) return new Color(rows[1], rows[2], rows[3], rows[4]);
            for (int i = 1; i < count; i++)
            {
                if (t > rows[i * 5]) continue;
                float t0 = rows[(i - 1) * 5], t1 = rows[i * 5], f = t1 > t0 ? (t - t0) / (t1 - t0) : 0;
                return Color.Lerp(new Color(rows[(i - 1) * 5 + 1], rows[(i - 1) * 5 + 2], rows[(i - 1) * 5 + 3], rows[(i - 1) * 5 + 4]),
                                  new Color(rows[i * 5 + 1], rows[i * 5 + 2], rows[i * 5 + 3], rows[i * 5 + 4]), f);
            }
            int last = (count - 1) * 5;
            return new Color(rows[last + 1], rows[last + 2], rows[last + 3], rows[last + 4]);
        }

        static float Range(float[] v, int i = 0) => v == null || v.Length < i + 2 ? 0 : Mathf.Lerp(v[i], v[i + 1], (float)Random.NextDouble());
        static float Unit() => (float)Random.NextDouble();
        static Vector3 V(float[] v) => v == null || v.Length < 3 ? Vector3.zero : new Vector3(v[0], v[1], v[2]);
        static Vector3 Lerp(float[] a, float[] b, float t) => Vector3.Lerp(V(a), V(b), t);
        static Vector3 Lerp(float[] a, float[] b) => new Vector3(Mathf.Lerp(V(a).x, V(b).x, Unit()), Mathf.Lerp(V(a).y, V(b).y, Unit()), Mathf.Lerp(V(a).z, V(b).z, Unit()));

        void Emit(Emitter e, int count)
        {
            var d = e.data;
            var root = transform;
            Vector3 origin = root.position;
            if (d.floor) origin.y = .01f;
            if (d.hasLight && Application.isPlaying) Flash(e, origin);
            if (!e.system) return;
            var p = new ParticleSystem.EmitParams();
            var emitterRotation = d.emitterRotation != null && d.emitterRotation.Length == 3
                ? Quaternion.Euler(d.emitterRotation[0], d.emitterRotation[1], d.emitterRotation[2]) : Quaternion.identity;
            for (int i = 0; i < count; i++)
            {
                // Spawn location in the effect's local space (metres, Unity axes).
                Vector3 local = V(d.shapeOffset), offset = Vector3.zero;
                if (d.shape == "sphere") offset = (d.shapeSurface ? UnityEngine.Random.onUnitSphere : UnityEngine.Random.insideUnitSphere) * d.shapeRadius;
                else if (d.shape == "cylinder")
                {
                    var disk = UnityEngine.Random.insideUnitCircle * d.shapeRadius;
                    offset = new Vector3(disk.x, (Unit() - .5f) * d.shapeHeight, disk.y);
                }
                local += offset;
                if (d.locationMin != null) local += Lerp(d.locationMin, d.locationMax);
                Vector3 velocity = d.velocityMin != null ? Lerp(d.velocityMin, d.velocityMax) : Vector3.zero;
                if (d.radial != null && local.sqrMagnitude > 1e-8f) velocity += local.normalized * Range(d.radial);
                if (d.shapeVelocity) velocity += offset * d.shapeVelocityScale;
                if (d.coneSpeed != null && d.coneDir != null)
                {
                    var axis = V(d.coneDir); if (axis.sqrMagnitude < 1e-6f) axis = Vector3.forward;
                    float angle = Range(d.coneAngle);
                    var tilt = Quaternion.AngleAxis(angle, Vector3.Cross(axis, Mathf.Abs(axis.y) < .9f ? Vector3.up : Vector3.right).normalized);
                    var spin = Quaternion.AngleAxis(Unit() * 360, axis.normalized);
                    velocity += spin * tilt * axis.normalized * Range(d.coneSpeed);
                }
                Vector3 world;
                if (d.bones && bones != null && bones.Length > 0)
                {
                    var bone = bones[Random.Next(bones.Length)];
                    world = (bone ? bone.position : origin) + root.rotation * (offset * scale);
                }
                else world = origin + root.rotation * (emitterRotation * local * scale);
                p.position = world;
                p.velocity = root.rotation * (emitterRotation * velocity * scale);
                p.startLifetime = Mathf.Max(.02f, Range(d.life));
                float s = Unit();
                Vector3 size = d.sizeMin != null ? Lerp(d.sizeMin, d.sizeMax, s) : Vector3.one * .1f;
                p.startSize3D = d.kind == "mesh" ? new Vector3(size.y, size.z, size.x) * scale : new Vector3(size.x, size.y, 1) * scale;
                Color start = Color.white;
                if (!e.colorFromLife && d.colorMin != null && d.colorMin.Length >= 4)
                {
                    float c = Unit();
                    start = new Color(Mathf.Lerp(d.colorMin[0], d.colorMax[0], c), Mathf.Lerp(d.colorMin[1], d.colorMax[1], c),
                                      Mathf.Lerp(d.colorMin[2], d.colorMax[2], c), Mathf.Lerp(d.colorMin[3], d.colorMax[3], c));
                    start = new Color(start.r * e.startScale.r, start.g * e.startScale.g, start.b * e.startScale.b, Mathf.Clamp01(start.a));
                }
                p.startColor = start;
                if (d.kind == "mesh")
                {
                    var turns = d.meshRotationMin != null ? Lerp(d.meshRotationMin, d.meshRotationMax) : Vector3.zero;
                    // Cascade mesh rotation (roll X, pitch Y, yaw Z in turns) about Unity z, x, y.
                    var q = root.rotation * emitterRotation * Quaternion.Euler(turns.y * 360, turns.z * 360, turns.x * 360);
                    p.rotation3D = q.eulerAngles;
                    if (d.meshRotationRateMin != null)
                    {
                        var rate = Lerp(d.meshRotationRateMin, d.meshRotationRateMax);
                        p.angularVelocity3D = new Vector3(rate.y, rate.z, rate.x) * 360;
                    }
                }
                else
                {
                    p.rotation = Range(d.rotation) * 360;
                    p.angularVelocity = Range(d.rotationRate) * 360;
                }
                e.system.Emit(p, 1);
            }
        }

        // ParticleModuleLight: a brief point light. Its radius is RadiusScale times the particle's half
        // size; the steep source exponent is approximated by a shorter HDRP range.
        void Flash(Emitter e, Vector3 position)
        {
            var d = e.data;
            var go = new GameObject(d.name + " light");
            go.transform.SetParent(transform, false);
            go.transform.position = position;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point; light.shadows = LightShadows.None;
            var color = d.lightColor != null && d.lightColor.Length >= 3 ? new Color(d.lightColor[0], d.lightColor[1], d.lightColor[2]) : Color.white;
            float max = Mathf.Max(color.r, color.g, color.b, 1e-4f);
            light.color = color / max;
            float halfSize = d.sizeMax != null && d.sizeMax.Length > 0 ? d.sizeMax[0] * .5f : .25f;
            light.range = Mathf.Max(.5f, d.lightRadiusScale * halfSize * .25f) * scale;
            var hd = go.AddComponent<HDAdditionalLightData>();
            float lumen = d.lightBrightness * max;
            hd.SetIntensity(lumen, LightUnit.Lumen);
            hd.SetRange(light.range);
            float life = Mathf.Max(.05f, Range(d.life));
            StartCoroutine(FadeLight(hd, lumen, life, go));
        }

        static System.Collections.IEnumerator FadeLight(HDAdditionalLightData light, float lumen, float life, GameObject go)
        {
            for (float t = 0; t < life && light; t += Time.deltaTime)
            {
                light.SetIntensity(lumen * (1 - t / life), LightUnit.Lumen);
                yield return null;
            }
            if (go) Destroy(go);
        }
    }
}
