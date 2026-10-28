using System;
using System.Collections.Generic;
using UnityEngine;

namespace Versus.Remaster
{
    // Replays timelines decoded from a Scaleform movie (Resources/BattleHud/GameMessage.json, written by
    // Tools/BattleHud/export_game_message.py) with IMGUI, as Flash plays them: nested sprites, per-frame
    // matrices, colour transforms, visibility, blend modes, blur and letter-shaped masks; named clips hold on
    // their last frame (their frame scripts call stop()), the rest loop, keeping instances that persist.
    // The fly-ins are Flash IDE motion tweens exported as ActionScript (fl.motion MotionBase +
    // AnimatorFactory3D): each named target gets Animator3D.setTime3D's transform per frame,
    // Initial * T(initialPosition) * T(x,y,z) * Rz * Ry * Rx * ScaleSkew, projected with the movie's
    // perspective (60 degree field of view, centre 960,540). Layers draw through GfxLayer.shader.
    public sealed class GfxClip
    {
        [Serializable] class Data { public float stageWidth, stageHeight, fps; public Entry[] entries; public Image[] images; public Mask[] masks; public Sprite[] sprites; public Motion[] motions; }
        [Serializable] class Entry { public string name; public int sprite; }
        [Serializable] class Image { public int id, w, h; public string name; }
        [Serializable] class Label { public string name; public int frame; }
        [Serializable] class Sprite { public int id, frames; public bool stops; public Label[] labels; public Frame[] frameEvents; }
        [Serializable] class Frame { public int frame; public Event[] events; }
        [Serializable] class Event
        {
            public int depth, character, clipDepth, blend, visible;
            public bool remove, move, hasMatrix, hasColor, hasFilter, hasVisible;
            public float[] matrix, mul, add, blur;
            public string name;
        }
        [Serializable] class Mask { public int id; public float xMin, xMax, yMin, yMax; }
        [Serializable] class Motion { public int sprite; public string target; public Segment[] segments; }
        [Serializable] class Segment
        {
            public int spanStart, duration;
            public bool is3D, hasInitialPosition, hasMatrix3D;
            public float[] initialPosition, matrix3D;
            public Prop[] props;
            [NonSerialized] public Dictionary<string, float[]> byName;
        }
        [Serializable] class Prop { public string name; public float[] values; }

        sealed class Instance
        {
            public Sprite sprite; public int character, frame, blend, clipDepth;
            public string name;
            public Vector2 blur;
            // Set by ActionScript: a scripted x/y wins over timeline tweens, hidden skips drawing, held stops the playhead.
            public float? scriptX, scriptY;
            public bool hidden, held;
            // PlaceObject3 HasVisible: most layers are placed invisible and revealed on their frame.
            public bool timelineHidden;
            public Matrix4x4 matrix = Matrix4x4.identity;
            // The placement when the instance appeared: Animator3D's _initialMatrixOfTarget.
            public Matrix4x4? initial;
            public Vector4 mul = Vector4.one, add = Vector4.zero;
            // Set by a motion tween each frame; they override the timeline's values.
            public Matrix4x4? motion;
            public bool? motionHidden;
            public int? motionBlend;
            public Vector4? motionMul, motionAdd;
            public Vector2? motionBlur;
            public readonly SortedList<int, Instance> children = new SortedList<int, Instance>();
        }

        const int AdditiveBlend = 8;
        static Data data;
        static readonly Dictionary<int, Sprite> sprites = new Dictionary<int, Sprite>();
        static readonly Dictionary<int, Image> images = new Dictionary<int, Image>();
        static readonly Dictionary<int, Texture2D> textures = new Dictionary<int, Texture2D>();
        static readonly Dictionary<Sprite, Dictionary<int, Event[]>> eventsByFrame = new Dictionary<Sprite, Dictionary<int, Event[]>>();
        static readonly Dictionary<int, List<Motion>> motionsBySprite = new Dictionary<int, List<Motion>>();
        // Main.__setPerspectiveProjection_: fieldOfView 60, projectionCenter (960, 540).
        static float focalLength;
        static Vector2 projectionCenter;

        readonly Sprite root;
        readonly int startFrame, stopFrame;
        Instance state;
        int ticks = -1;

        // A clip of the movie: the named top-level sprite played from a label (or frame) to stopFrame, where it holds.
        public GfxClip(string entry, string startLabel, string stopLabel = null, int stopOffset = 0)
        {
            Load();
            int id = Array.Find(data.entries, e => e.name == entry)?.sprite ?? -1;
            if (!sprites.TryGetValue(id, out root)) throw new ArgumentException("No GameMessage clip " + entry);
            startFrame = LabelFrame(startLabel, 0);
            stopFrame = stopLabel == null ? root.frames - 1 : LabelFrame(stopLabel, root.frames - 1) + stopOffset;
        }

        // Frames already played when the call first shows (measured against gameplay captures: TEKKEN 7's first
        // visible ROUND frame is the movie's frame 12 of the fly-in).
        public int LeadFrames { get; set; }

        public static float StageWidth { get { Load(); return data.stageWidth; } }
        public int Length => stopFrame - startFrame;

        int LabelFrame(string label, int fallback)
        {
            if (label == null) return fallback;
            var l = Array.Find(root.labels, x => x.name == label);
            return l != null ? l.frame : fallback;
        }

        static void Load()
        {
            if (data != null) return;
            var json = Resources.Load<TextAsset>("BattleHud/GameMessage");
            data = JsonUtility.FromJson<Data>(json.text);
            foreach (var s in data.sprites)
            {
                sprites[s.id] = s;
                var map = new Dictionary<int, Event[]>();
                foreach (var f in s.frameEvents) map[f.frame] = f.events;
                eventsByFrame[s] = map;
            }
            foreach (var i in data.images) images[i.id] = i;
            foreach (var mask in data.masks) maskBounds[mask.id] = Rect.MinMaxRect(mask.xMin, mask.yMin, mask.xMax, mask.yMax);
            foreach (var motion in data.motions ?? Array.Empty<Motion>())
            {
                foreach (var segment in motion.segments)
                {
                    segment.byName = new Dictionary<string, float[]>();
                    foreach (var p in segment.props ?? Array.Empty<Prop>()) segment.byName[p.name] = p.values;
                }
                if (!motionsBySprite.TryGetValue(motion.sprite, out var list)) motionsBySprite[motion.sprite] = list = new List<Motion>();
                list.Add(motion);
            }
            focalLength = data.stageWidth / 2 / Mathf.Tan(30 * Mathf.Deg2Rad);
            projectionCenter = new Vector2(data.stageWidth / 2, data.stageHeight / 2);
        }

        // Draws the clip `elapsed` seconds after it started, centred on the stage. `stageToGui` maps the
        // movie's stage (1920x1080) into the caller's GUI space.
        public void Draw(float elapsed, Matrix4x4 stageToGui)
        {
            if (UnityEngine.Event.current.type != EventType.Repaint || elapsed < 0) return;
            int target = Mathf.FloorToInt(elapsed * data.fps) + LeadFrames;
            if (state == null || target < ticks)
            {
                state = new Instance { sprite = root, matrix = Matrix4x4.Translate(new Vector3(data.stageWidth / 2, data.stageHeight / 2, 0)) };
                Rebuild(state, startFrame); ticks = 0;
                foreach (var command in script) command(state);
            }
            for (; ticks < target; ticks++) Step(state, true);
            var color = GUI.color; var previous = GUI.matrix;
            try { Render(state, previous * stageToGui, state.matrix, Matrix4x4.identity, false, Vector4.one, Vector4.zero, false, Vector2.zero, (ActiveMask)null); }
            finally { GUI.matrix = previous; GUI.color = color; }
        }

        // ActionScript run on the clip once it is at its start frame (as the game's native code does before
        // playing it), addressing instances by name paths such as "Anim_mc.Num1".
        readonly List<Action<Instance>> script = new List<Action<Instance>>();

        static Instance Find(Instance from, string path)
        {
            foreach (var name in path.Split('.'))
            {
                Instance next = null;
                foreach (var child in from.children.Values) if (child.name == name) { next = child; break; }
                if (next == null) return null;
                from = next;
            }
            return from;
        }

        public GfxClip SetPosition(string path, float? x, float? y) => Command(path, i =>
        {
            if (x.HasValue) { i.scriptX = x; i.matrix.m03 = x.Value; }
            if (y.HasValue) { i.scriptY = y; i.matrix.m13 = y.Value; }
        });
        public GfxClip SetVisible(string path, bool visible) => Command(path, i => i.hidden = !visible);
        // gotoAndStop(frame), with Flash's 1-based frame number.
        public GfxClip GotoAndStop(string path, int frame) => Command(path, i => { if (i.sprite != null) { Rebuild(i, Mathf.Clamp(frame - 1, 0, i.sprite.frames - 1)); i.held = true; } });

        GfxClip Command(string path, Action<Instance> action)
        {
            script.Add(rootInstance => { var target = Find(rootInstance, path); if (target != null) action(target); });
            state = null; // replay with the new script
            return this;
        }

        // Flash goto: a fresh display list built from the frame events up to `frame`, with new child clips.
        static void Rebuild(Instance i, int frame)
        {
            i.children.Clear();
            for (int f = 0; f <= frame; f++) Apply(i, f);
            i.frame = frame;
            Animate(i);
        }

        // Flash looping back to frame 1 keeps instances still at the same depth with the same symbol (their
        // playheads and scripted properties survive); the rest is rebuilt from frame 1's placements.
        static void Loop(Instance i)
        {
            var old = new Dictionary<int, Instance>();
            foreach (var pair in i.children) old[pair.Key] = pair.Value;
            i.children.Clear();
            Apply(i, 0);
            foreach (var depth in new List<int>(i.children.Keys))
            {
                var placed = i.children[depth];
                if (!old.TryGetValue(depth, out var kept) || kept.character != placed.character) continue;
                kept.matrix = placed.matrix; kept.mul = placed.mul; kept.add = placed.add; kept.timelineHidden = placed.timelineHidden;
                if (kept.scriptX.HasValue) kept.matrix.m03 = kept.scriptX.Value;
                if (kept.scriptY.HasValue) kept.matrix.m13 = kept.scriptY.Value;
                i.children[depth] = kept;
            }
            i.frame = 0;
            Animate(i);
        }

        void Step(Instance i, bool isRoot)
        {
            if (i.sprite == null) return;
            foreach (var child in new List<Instance>(i.children.Values)) Step(child, false);
            if (i.held) return;
            int next = i.frame + 1;
            if (isRoot ? next > stopFrame : next >= i.sprite.frames)
            {
                if (isRoot || i.sprite.stops) return; // holds on its last frame
                Loop(i); return;
            }
            i.frame = next;
            Apply(i, next);
            Animate(i);
        }

        static void Apply(Instance i, int frame)
        {
            if (!eventsByFrame[i.sprite].TryGetValue(frame, out var events)) return;
            foreach (var e in events)
            {
                if (e.remove) { i.children.Remove(e.depth); continue; }
                i.children.TryGetValue(e.depth, out var child);
                if (child == null || !e.move || (e.character >= 0 && e.character != child.character))
                {
                    var created = new Instance { character = e.character >= 0 ? e.character : child?.character ?? -1, name = !string.IsNullOrEmpty(e.name) ? e.name : child?.name };
                    if (child != null && e.move)
                    {
                        created.matrix = child.matrix; created.initial = child.initial; created.mul = child.mul; created.add = child.add;
                        created.blend = child.blend; created.clipDepth = child.clipDepth; created.blur = child.blur; created.timelineHidden = child.timelineHidden;
                    }
                    sprites.TryGetValue(created.character, out created.sprite);
                    if (created.sprite != null) Rebuild(created, 0);
                    i.children[e.depth] = child = created;
                }
                if (e.hasMatrix)
                {
                    var m = e.matrix; var mat = Matrix4x4.identity;
                    mat.m00 = m[0]; mat.m10 = m[1]; mat.m01 = m[2]; mat.m11 = m[3];
                    mat.m03 = child.scriptX ?? m[4]; mat.m13 = child.scriptY ?? m[5];
                    child.matrix = mat;
                    if (!child.initial.HasValue) child.initial = mat;
                }
                if (!string.IsNullOrEmpty(e.name)) child.name = e.name;
                if (e.hasColor) { child.mul = new Vector4(e.mul[0], e.mul[1], e.mul[2], e.mul[3]); child.add = new Vector4(e.add[0], e.add[1], e.add[2], e.add[3]) / 255f; }
                if (e.blend > 0) child.blend = e.blend;
                if (e.clipDepth > 0) child.clipDepth = e.clipDepth;
                if (e.hasFilter) child.blur = new Vector2(e.blur[0], e.blur[1]);
                if (e.hasVisible) child.timelineHidden = e.visible == 0;
            }
        }

        // The motion tweens of this sprite, applied to their named children for the sprite's current frame.
        static void Animate(Instance i)
        {
            if (i.sprite == null || !motionsBySprite.TryGetValue(i.sprite.id, out var motions)) return;
            foreach (var motion in motions)
            {
                Instance target = null;
                foreach (var child in i.children.Values) if (child.name == motion.target) { target = child; break; }
                if (target == null) continue;
                Segment segment = null;
                foreach (var s in motion.segments) if (i.frame >= s.spanStart && i.frame < s.spanStart + s.duration) { segment = s; break; }
                if (segment == null) continue;
                int t = i.frame - segment.spanStart;
                var values = segment.byName;
                float Value(string name, float fallback) => values.TryGetValue(name, out var a) && a.Length > 0 ? a[Mathf.Min(t, a.Length - 1)] : fallback;
                bool Has(string name) => values.ContainsKey(name);

                if (Has("visible")) target.motionHidden = Value("visible", 1) < .5f;
                if (Has("blendMode")) target.motionBlend = (int)Value("blendMode", 1);
                if (Has("redMultiplier") || Has("greenMultiplier") || Has("blueMultiplier") || Has("alphaMultiplier") ||
                    Has("redOffset") || Has("greenOffset") || Has("blueOffset") || Has("alphaOffset"))
                {
                    target.motionMul = new Vector4(Value("redMultiplier", 1), Value("greenMultiplier", 1), Value("blueMultiplier", 1), Value("alphaMultiplier", 1));
                    target.motionAdd = new Vector4(Value("redOffset", 0), Value("greenOffset", 0), Value("blueOffset", 0), Value("alphaOffset", 0)) / 255f;
                }
                if (Has("blurX") || Has("blurY")) target.motionBlur = new Vector2(Value("blurX", 0), Value("blurY", 0));

                var initial = target.initial ?? target.matrix;
                if (segment.hasMatrix3D)
                {
                    // A segment holding a Matrix3D keyframe: matrix3D.clone().append(initial). rawData is column-major.
                    var raw = segment.matrix3D; var m3 = Matrix4x4.identity;
                    for (int c = 0; c < 4; c++) for (int r = 0; r < 4; r++) m3[r, c] = raw[c * 4 + r];
                    target.motion = initial * m3;
                }
                else if (segment.is3D)
                {
                    var m = Rotation(Vector3.forward, Value("rotationZ", 0)) * Rotation(Vector3.up, Value("rotationY", 0)) * Rotation(Vector3.right, Value("rotationX", 0));
                    float x = Value("x", 0), y = Value("y", 0), z = Value("z", 0);
                    if (x != 0 || y != 0 || z != 0) m = Matrix4x4.Translate(new Vector3(x, y, z)) * m;
                    if (segment.hasInitialPosition)
                        m = Matrix4x4.Translate(new Vector3(segment.initialPosition[0], segment.initialPosition[1], segment.initialPosition[2])) * m;
                    float sx = Value("scaleX", 1), sy = Value("scaleY", 1), kx = Value("skewX", 0) * Mathf.Deg2Rad, ky = Value("skewY", 0) * Mathf.Deg2Rad;
                    var scaleSkew = Matrix4x4.identity;
                    scaleSkew.m00 = sx * Mathf.Cos(ky); scaleSkew.m10 = sx * Mathf.Sin(ky);
                    scaleSkew.m01 = -sy * Mathf.Sin(kx); scaleSkew.m11 = sy * Mathf.Cos(kx);
                    target.motion = initial * m * scaleSkew;
                }
                else target.motion = null;
            }
        }

        // Right-handed rotation (the movie's stored matrices map +x to (cos, 0, -sin) for rotationY).
        static Matrix4x4 Rotation(Vector3 axis, float degrees)
        {
            float a = degrees * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
            var m = Matrix4x4.identity;
            if (axis == Vector3.right) { m.m11 = c; m.m12 = -s; m.m21 = s; m.m22 = c; }
            else if (axis == Vector3.up) { m.m00 = c; m.m02 = s; m.m20 = -s; m.m22 = c; }
            else { m.m00 = c; m.m01 = -s; m.m10 = s; m.m11 = c; }
            return m;
        }

        // Flash's perspective in the 2D space `outer` maps to: q = c + (p - c) * f / (f + z), as a homogeneous
        // 4x4 (the GPU divides by w). GfxLayer.shader applies it (GUI.matrix rejects it).
        static Matrix4x4 Projection(Matrix4x4 outer)
        {
            var c = outer.inverse.MultiplyPoint3x4(new Vector3(projectionCenter.x, projectionCenter.y, 0));
            float f = focalLength / Mathf.Sqrt(Mathf.Abs(outer.m00 * outer.m11 - outer.m01 * outer.m10));
            var p = Matrix4x4.identity;
            p.m02 = c.x / f; p.m12 = c.y / f; p.m22 = 1e-4f; p.m32 = 1 / f;
            return p;
        }

        // A clip layer: for each of its shapes (one, or up to two when the clip layer is a sprite), the world
        // matrix, bounds and the letter outline rasterised by the exporter. The mask is their union.
        sealed class ActiveMask { public readonly List<(Matrix4x4 world, Rect bounds, Texture2D texture)> shapes = new List<(Matrix4x4, Rect, Texture2D)>(); }

        static ActiveMask ClipLayer(Instance c, Matrix4x4 world)
        {
            var mask = new ActiveMask();
            if (maskBounds.TryGetValue(c.character, out var bounds)) mask.shapes.Add((world, bounds, MaskTexture(c.character)));
            else if (c.sprite != null)
                foreach (var shape in c.children.Values)
                    if (mask.shapes.Count < 2 && maskBounds.TryGetValue(shape.character, out var b))
                        mask.shapes.Add((world * shape.matrix, b, MaskTexture(shape.character)));
            return mask.shapes.Count > 0 ? mask : null;
        }
        static readonly Dictionary<int, Texture2D> maskTextures = new Dictionary<int, Texture2D>();
        static Texture2D MaskTexture(int id)
        {
            if (!maskTextures.TryGetValue(id, out var texture))
                maskTextures[id] = texture = Resources.Load<Texture2D>("BattleHud/GameMessage/Mask_" + id);
            return texture;
        }
        static Material normalLayer, additiveLayer;
        static readonly Dictionary<int, Rect> maskBounds = new Dictionary<int, Rect>();

        static Material Layer(bool additive)
        {
            if (!normalLayer)
            {
                var shader = Resources.Load<Shader>("BattleHud/GfxLayer");
                if (!shader) return null;
                normalLayer = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                normalLayer.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                normalLayer.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                additiveLayer = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                additiveLayer.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                additiveLayer.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            }
            return additive ? additiveLayer : normalLayer;
        }

        // `outer` is the 2D stage-space transform down to the nearest 3D ancestor; `inner` the 3D chain below it
        // (in3D). Blend and blur pass down to a sprite's children, as Flash composites a filtered or additive
        // sprite as a whole.
        static void Render(Instance i, Matrix4x4 baseMatrix, Matrix4x4 outer, Matrix4x4 inner, bool in3D, Vector4 mul, Vector4 add, bool additive, Vector2 blur, ActiveMask mask)
        {
            ActiveMask clip = null; int clipUntil = -1;
            foreach (var pair in i.children)
            {
                var c = pair.Value;
                if (c.hidden || (c.motionHidden ?? c.timelineHidden)) continue;
                var o = outer; var inn = inner; bool is3D = in3D;
                if (c.motion.HasValue) { inn = in3D ? inner * c.motion.Value : c.motion.Value; is3D = true; }
                else if (in3D) inn = inner * c.matrix;
                else o = outer * c.matrix;
                var world = is3D ? o * Projection(o) * inn : o;
                if (c.clipDepth > 0)
                {
                    // A clip layer masks the depths above it up to clipDepth and never draws itself.
                    clip = ClipLayer(c, world);
                    clipUntil = c.clipDepth; continue;
                }
                var activeMask = pair.Key <= clipUntil && clip != null ? clip : mask;
                var cm = Vector4.Scale(c.motionMul ?? c.mul, mul);
                var ca = Vector4.Scale(c.motionAdd ?? c.add, mul) + add;
                bool add8 = additive || (c.motionBlend ?? c.blend) == AdditiveBlend;
                var b = Vector2.Max(blur, c.motionBlur ?? c.blur);
                if (c.sprite != null) { Render(c, baseMatrix, o, inn, is3D, cm, ca, add8, b, activeMask); continue; }
                if (!images.TryGetValue(c.character, out var image)) continue;
                if (cm.w + ca.w <= .004f) continue;
                if (!textures.TryGetValue(c.character, out var texture))
                    textures[c.character] = texture = Resources.Load<Texture2D>("BattleHud/GameMessage/" + image.name);
                if (!texture) continue;
                var material = Layer(add8);
                var full = baseMatrix * world;
                if (!material)
                {
                    if (!full.ValidTRS()) continue; // GUI.matrix rejects the 3D tween's perspective
                    GUI.matrix = full;
                    GUI.color = new Color(cm.x + ca.x, cm.y + ca.y, cm.z + ca.z, Mathf.Clamp01(cm.w + ca.w));
                    GUI.DrawTexture(new Rect(0, 0, image.w, image.h), texture);
                    continue;
                }
                material.SetVector("_Mul", cm);
                material.SetVector("_Add", ca);
                // Blur is in stage pixels; convert to this image's UV through its on-stage scale.
                float sx = new Vector2(world.m00, world.m10).magnitude * image.w, sy = new Vector2(world.m01, world.m11).magnitude * image.h;
                material.SetVector("_Blur", new Vector4(sx > 0 ? b.x / sx : 0, sy > 0 ? b.y / sy : 0, 0, 0));
                int shapes = 0;
                if (activeMask != null)
                    foreach (var (maskWorld, r, maskTexture) in activeMask.shapes)
                    {
                        if (!maskTexture) continue;
                        // Layer UV (x right, y down, in pixels via the image size) -> mask-local coordinates. Mask
                        // and layer share the same projection, so the product is affine.
                        var toMask = maskWorld.inverse * world * Matrix4x4.Scale(new Vector3(image.w, image.h, 1));
                        string suffix = shapes == 0 ? "" : "2";
                        material.SetVector(shapes == 0 ? "_MaskRow0" : "_MaskRow2", new Vector4(toMask.m00, toMask.m01, toMask.m03, 0));
                        material.SetVector(shapes == 0 ? "_MaskRow1" : "_MaskRow3", new Vector4(toMask.m10, toMask.m11, toMask.m13, 0));
                        material.SetVector("_MaskRect" + suffix, new Vector4(r.xMin, r.yMin, r.xMax, r.yMax));
                        material.SetTexture("_MaskTex" + suffix, maskTexture);
                        shapes++;
                    }
                material.SetFloat("_UseMask", shapes);
                // The shader applies the transform: GUI.matrix only takes TRS matrices (Unity logs "Ignoring
                // invalid matrix" for skews and the perspective), so it stays at identity.
                material.SetVector("_ProjX", full.GetRow(0));
                material.SetVector("_ProjY", full.GetRow(1));
                material.SetVector("_ProjW", full.GetRow(3));
                GUI.matrix = Matrix4x4.identity;
                Graphics.DrawTexture(new Rect(0, 0, image.w, image.h), texture, material);
            }
        }
    }
}
