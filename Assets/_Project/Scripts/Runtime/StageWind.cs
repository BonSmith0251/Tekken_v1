using UnityEngine;

namespace Versus.Remaster
{
    // Gentle wind on a stage's foliage (ivy, vines, palms, leaves). The original moves them in the
    // material's vertex shader from the stage's WindDirectionalSource; here the meshes are bent on the
    // CPU each frame (they are small: stg01's ivy curtains are ~500 vertices each). Hanging ivy and
    // vines move most at their free lower ends; trees and plants at their tips. Each vertex also
    // flutters a little, phase-shifted by its position.
    public sealed class StageWind : MonoBehaviour
    {
        public MeshFilter[] foliage = new MeshFilter[0];
        public bool[] hanging = new bool[0];
        [Tooltip("Stage-local wind direction (the WindDirectionalSource's +X).")]
        public Vector3 direction = Vector3.right;
        [Tooltip("UE WindDirectionalSource values; the defaults are a light breeze.")]
        public float strength = .1f, speed = .1f, minGust = .1f, maxGust = .2f;
        // Not recovered: the original materials' wind response. Set by eye for a gentle sway.
        const float MetresPerStrength = .35f, SwayRadians = 12, MaxStrength = .5f;

        sealed class Bent
        {
            public Mesh mesh; public Vector3[] rest, moved; public float[] weight, phase; public Vector3 wind, flutter; public float scale;
        }
        Bent[] bent;

        void Start() { if (Application.isPlaying) Prepare(); }

        public void Prepare()
        {
            bent = new Bent[foliage.Length];
            for (int i = 0; i < foliage.Length; i++)
            {
                var f = foliage[i]; if (!f || !f.sharedMesh) continue;
                var mesh = Instantiate(f.sharedMesh); mesh.name = f.sharedMesh.name + " (wind)";
                var rest = mesh.vertices; var b = mesh.bounds;
                bool hang = i < hanging.Length && hanging[i];
                var weight = new float[rest.Length]; var phase = new float[rest.Length];
                float height = Mathf.Max(.01f, b.size.y);
                for (int v = 0; v < rest.Length; v++)
                {
                    float up = (rest[v].y - b.min.y) / height;
                    weight[v] = hang ? .35f + .65f * (1 - up) : up * up;
                    phase[v] = (rest[v].x * 1.7f + rest[v].z * 2.3f + rest[v].y * .9f) * 3;
                }
                // Wind and a flutter axis in this mesh's own space, in its own units.
                var t = f.transform; var worldWind = transform.TransformDirection(direction).normalized;
                float scale = Mathf.Max(.0001f, (Mathf.Abs(t.lossyScale.x) + Mathf.Abs(t.lossyScale.y) + Mathf.Abs(t.lossyScale.z)) / 3);
                var localWind = t.InverseTransformDirection(worldWind).normalized;
                var localFlutter = t.InverseTransformDirection(Vector3.Cross(worldWind, Vector3.up)).normalized;
                b.Expand(MetresPerStrength * 4 / scale);
                mesh.bounds = b;
                f.sharedMesh = mesh;
                bent[i] = new Bent { mesh = mesh, rest = rest, moved = (Vector3[])rest.Clone(), weight = weight, phase = phase, wind = localWind, flutter = localFlutter, scale = scale };
            }
        }

        void Update() { if (bent != null) Step(Time.time); }

        // Poses the foliage at a time (seconds); returns the largest vertex offset in metres.
        public float Step(float time)
        {
            if (bent == null) Prepare();
            float frequency = 1.2f + speed * SwayRadians;
            float gust = Mathf.Lerp(minGust, maxGust, .5f + .5f * Mathf.Sin(time * .37f) * Mathf.Sin(time * .11f + 1.3f));
            // Strong stage winds (strength 1) are capped at half so the foliage sways gently.
            float amplitude = Mathf.Min(strength, MaxStrength) * MetresPerStrength, largest = 0;
            foreach (var b in bent)
            {
                if (b == null) continue;
                float a = amplitude / b.scale;
                for (int v = 0; v < b.rest.Length; v++)
                {
                    float w = b.weight[v], p = b.phase[v];
                    float sway = (Mathf.Sin(time * frequency + p * .15f) * .6f + gust) * w;
                    float flutter = Mathf.Sin(time * frequency * 2.7f + p) * .35f * w;
                    b.moved[v] = b.rest[v] + (b.wind * sway + b.flutter * flutter) * a;
                    largest = Mathf.Max(largest, Mathf.Abs(sway) + Mathf.Abs(flutter));
                }
                b.mesh.vertices = b.moved;
            }
            return largest * amplitude;
        }
    }
}
