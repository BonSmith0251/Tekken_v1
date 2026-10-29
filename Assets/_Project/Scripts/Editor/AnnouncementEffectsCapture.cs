using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Versus.Remaster.Editor
{
    // Calls the real HUD renderer at fixed movie ticks without loading or saving a scene.
    [InitializeOnLoad]
    public sealed class AnnouncementEffectsCapture : EditorWindow
    {
        static AnnouncementEffectsCapture()
        {
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged += change =>
            {
                if (change != PlayModeStateChange.EnteredEditMode) return;
                string saved = SessionState.GetString("AnnouncementSceneSetup", "");
                if (saved.Length == 0) return;
                SessionState.EraseString("AnnouncementSceneSetup");
                EditorSceneManager.RestoreSceneManagerSetup(JsonUtility.FromJson<SavedScenes>(saved).scenes);
            };
        }
        [Serializable] class SavedScenes { public SceneSetup[] scenes; }
        static void Poll()
        {
            PollLive();
            const string flag = "Temp/capture-announcements";
            if (!File.Exists(flag) || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
            string run = File.ReadAllText(flag).Trim(); File.Delete(flag);
            var window = GetWindow<AnnouncementEffectsCapture>(true, "Announcement capture");
            window.output = "Validation/Announcements/" + (run == "before" ? "before" : "after");
            window.pending = true; window.Show(); window.Repaint();
        }
        static readonly System.Collections.Generic.HashSet<BattleHud.Call> liveCalls = new System.Collections.Generic.HashSet<BattleHud.Call>();
        static void PollLive()
        {
            const string flag = "Temp/play-announcements", folder = "Validation/Announcements/live";
            if (File.Exists(flag) && !EditorApplication.isCompiling && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                File.Delete(flag); Directory.CreateDirectory(folder);
                if (File.Exists(folder + "/error.txt")) File.Delete(folder + "/error.txt");
                if (!UnityEngine.Object.FindFirstObjectByType<VersusGame>())
                {
                    for (int index = 0; index < SceneManager.sceneCount; index++)
                        if (SceneManager.GetSceneAt(index).isDirty)
                        { File.WriteAllText(folder + "/error.txt", "Live capture skipped to preserve an unsaved scene. Fixed-frame validation is available."); return; }
                    SessionState.SetString("AnnouncementSceneSetup", JsonUtility.ToJson(new SavedScenes { scenes = EditorSceneManager.GetSceneManagerSetup() }));
                    EditorSceneManager.OpenScene("Assets/_Project/Scenes/LocalVersus2.unity");
                }
                liveCalls.Clear(); SessionState.SetFloat("AnnouncementLiveStart", (float)EditorApplication.timeSinceStartup);
                SessionState.SetBool("AnnouncementLiveCapture", true); EditorApplication.isPlaying = true;
            }
            if (!SessionState.GetBool("AnnouncementLiveCapture", false) || !EditorApplication.isPlaying) return;
            try
            {
                Application.runInBackground = true; EditorApplication.QueuePlayerLoopUpdate();
                var game = UnityEngine.Object.FindFirstObjectByType<VersusGame>();
                if (!game || game.battle == null) return;
                game.automaticFight = true;
                var entrance = (LocalVersusEntrance)Field(game, "entrance");
                BattleHud.Call call = BattleHud.Call.None; float age = 0;
                if (entrance != null && entrance.Active && entrance.ShowsBattleHud)
                { call = entrance.Cue; age = entrance.CueSeconds; }
                else if (game.battle.phase == MatchPhase.Intro)
                {
                    int tick = 150 - game.battle.phaseTicks;
                    call = tick < 68 ? BattleHud.Call.Round : tick < 108 ? BattleHud.Call.Ready : BattleHud.Call.Fight;
                    age = (tick - (tick < 68 ? 0 : tick < 108 ? 68 : 108)) / 60f;
                }
                else if (game.battle.phase == MatchPhase.RoundOver || game.battle.phase == MatchPhase.MatchOver)
                {
                    if (game.battle.result != "TIME UP" && game.battle.result != "DOUBLE KO")
                    { call = BattleHud.Call.KO; age = (float)Field(game, "roundEndClock"); }
                }
                if (call != BattleHud.Call.None && age >= .2f && age < .7f && liveCalls.Add(call))
                    ScreenCapture.CaptureScreenshot(folder + "/" + call + ".png");
                if (liveCalls.Count == 4 || EditorApplication.timeSinceStartup - SessionState.GetFloat("AnnouncementLiveStart", 0) > 120)
                {
                    File.WriteAllText(folder + "/result.txt", "Natural versus playback captured: " + string.Join(", ", liveCalls) + ".\n");
                    SessionState.SetBool("AnnouncementLiveCapture", false); EditorApplication.isPlaying = false;
                }
            }
            catch (Exception e)
            {
                Directory.CreateDirectory(folder); File.WriteAllText(folder + "/error.txt", e.ToString());
                SessionState.SetBool("AnnouncementLiveCapture", false); EditorApplication.isPlaying = false;
            }
        }
        string output;
        bool pending;
        void OnGUI()
        {
            if (!pending || Event.current.type != EventType.Repaint) return;
            pending = false;
            try
            {
                Directory.CreateDirectory(output);
                var hud = new BattleHud("JIN", "KAZ");
                foreach (var call in new[] { BattleHud.Call.Round, BattleHud.Call.Ready, BattleHud.Call.Fight, BattleHud.Call.KO })
                    foreach (int tick in new[] { 0, 4, 8, 12, 20, 30, 40, 50, 70, 100, 118, 130 })
                        Capture(hud, call, tick, output);
                // Shared-player regressions: other calls, digit masks, rewind and continuous READY -> FIGHT.
                foreach (var call in new[] { BattleHud.Call.DoubleKO, BattleHud.Call.TimeUp, BattleHud.Call.Draw })
                    Capture(hud, call, 30, output);
                foreach (int round in new[] { 2, 10 }) Capture(hud, BattleHud.Call.Round, 20, output, round);
                Capture(hud, BattleHud.Call.Round, 20, output, 1, true);
                var first = File.ReadAllBytes(output + "/Fight-012.png");
                Capture(hud, BattleHud.Call.Fight, 12, output);
                if (!System.Linq.Enumerable.SequenceEqual(first, File.ReadAllBytes(output + "/Fight-012.png")))
                    throw new Exception("Announcement rewind did not reproduce the same rendered frame.");
                CheckPlaybackScripts();
                var shader = Resources.Load<Shader>("BattleHud/GfxLayer");
                if (!shader || !shader.isSupported || ShaderUtil.ShaderHasError(shader)) throw new Exception("Announcement shader failed compilation.");
                File.WriteAllText(output + "/complete.txt", "PASS: 54 Unity HUD captures; rewind pixel equality; READY holds FIGHT, cue releases it, child stops at frame 40; shader supported with no compiler errors.\n");
            }
            catch (Exception e) { Directory.CreateDirectory(output); File.WriteAllText(output + "/error.txt", e.ToString()); Debug.LogException(e); }
            EditorApplication.delayCall += Close;
        }
        static object Field(object value, string name) => value.GetType().GetField(name,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(value);
        static void CheckPlaybackScripts()
        {
            var clip = new GfxClip("ReadyFight_mc", "Ready");
            object FightChild()
            {
                var state = Field(clip, "state");
                var children = (System.Collections.IEnumerable)Field(state, "children");
                foreach (var pair in children)
                {
                    var child = pair.GetType().GetProperty("Value").GetValue(pair);
                    if ((string)Field(child, "name") == "Moji_Fight_mc") return child;
                }
                throw new Exception("FIGHT child missing");
            }
            void Check(int tick, int expectedFrame, bool held)
            {
                clip.Draw((tick + .01f) / 60f, Matrix4x4.identity);
                var child = FightChild();
                if ((int)Field(child, "frame") != expectedFrame || (bool)Field(child, "held") != held)
                    throw new Exception("FIGHT playback differs from original frame scripts at READY tick " + tick);
            }
            Check(38, 0, true); Check(39, 0, false); Check(40, 1, false); Check(78, 39, true);
        }
        static void Capture(BattleHud hud, BattleHud.Call call, int tick, string folder, int round = 1, bool final = false)
        {
            var previous = RenderTexture.active; var matrix = GUI.matrix; bool srgb = GL.sRGBWrite;
            var rt = RenderTexture.GetTemporary(1280, 720, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            GL.PushMatrix();
            try
            {
                RenderTexture.active = rt; GL.sRGBWrite = true;
                GL.Clear(true, true, Color.black); GL.LoadPixelMatrix(0, 1280, 720, 0);
                GUI.matrix = Matrix4x4.identity;
                var trace = new System.Text.StringBuilder();
                GfxClip.TraceLayer = line => trace.AppendLine(line);
                hud.DrawCall(call, round, final, (tick + .001f) / 60f);
                GfxClip.TraceLayer = null;
                string name = call + (round == 1 ? "" : round.ToString()) + (final ? "Final" : "") + "-" + tick.ToString("D3");
                File.WriteAllText(folder + "/" + name + ".txt", trace.ToString());
                texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply();
                File.WriteAllBytes(folder + "/" + name + ".png", texture.EncodeToPNG());
            }
            finally
            {
                GfxClip.TraceLayer = null;
                GL.PopMatrix(); GUI.matrix = matrix; GL.sRGBWrite = srgb; RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt); DestroyImmediate(texture);
            }
        }
    }
}
