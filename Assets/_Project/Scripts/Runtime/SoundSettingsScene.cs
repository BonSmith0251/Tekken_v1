using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Versus.Remaster
{
    // sound_settings: set the volumes and the music choice versus uses (Resources/SoundSettings.asset), and listen to
    // every exported music track and sound. Changes are heard at once; Save keeps them, Revert drops them.
    public sealed class SoundSettingsScene : MonoBehaviour
    {
        const float Width = 1280, Height = 720;
        static readonly string[] Pages = { "Announcer", "Battle", "System", "Menu", "Fighters" };
        static readonly string[] Roles = { "attack", "damage", "death", "lines" };
        static readonly Dictionary<string, string> FighterNames = new Dictionary<string, string> {
            { "jin", "Jin" }, { "kaz", "Kazuya" }, { "xia", "Xiaoyu" }, { "asa", "Alisa" }, { "ltn", "Katarina" },
            { "nsd", "Lidia" }, { "bob", "Bob" }, { "leo", "Leo" } };

        SoundSettings working;
        BattleSound sound;
        BattleMusic music;
        string[] tracks = new string[0], fighters = new string[0];
        int page, fighter;
        Vector2 trackScroll, soundScroll;
        string status = "", lastPlayed = "";
        bool dirty;
        GUIStyle title, header, row, rowOn, small, wrap;

        void Start()
        {
            if (!Camera.main)
            {
                var cam = new GameObject("Sound settings camera").AddComponent<Camera>();
                cam.transform.SetParent(transform, false); cam.tag = "MainCamera";
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.05f, .06f, .09f);
            }
            if (!FindFirstObjectByType<AudioListener>()) gameObject.AddComponent<AudioListener>();
            working = ScriptableObject.CreateInstance<SoundSettings>();
            working.CopyFrom(SoundSettings.Current);
            SoundSettings.Preview = working;
            sound = BattleSound.Create(transform);
            music = BattleMusic.Instance;
            tracks = music.TrackNames.OrderBy(t => t == BattleMusic.SelectTrack ? "" : t, StringComparer.OrdinalIgnoreCase).ToArray();
            fighters = sound.FighterCodes.ToArray();
            if (!sound.Loaded) status = "Resources/BattleSound is missing: run Tools/BattleSound/export_battle_sound.py.";
            else if (tracks.Length == 0) status = "Resources/BattleMusic is missing: run Tools/BattleSound/export_battle_music.py.";
        }

        void OnDestroy() { if (SoundSettings.Preview == working) SoundSettings.Preview = null; }

        void Styles()
        {
            if (title != null) return;
            title = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold };
            header = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold };
            small = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            wrap = new GUIStyle(small) { wordWrap = true };
            row = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft, fontSize = 13 };
            rowOn = new GUIStyle(row); rowOn.normal.textColor = rowOn.hover.textColor = new Color(1, .8f, .3f);
        }

        void OnGUI()
        {
            if (working == null) return;
            Styles();
            float scale = Mathf.Min(Screen.width / Width, Screen.height / Height);
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - Width * scale) / 2, (Screen.height - Height * scale) / 2, 0),
                Quaternion.identity, new Vector3(scale, scale, 1));
            GUI.Label(new Rect(30, 18, 600, 36), "SOUND SETTINGS", title);
            GUI.Label(new Rect(30, 52, 1220, 22), "Music and sounds from Tekken 7's own banks. Changes play at once; Save keeps them for versus.", small);
            DrawMix(new Rect(30, 90, 360, 610));
            DrawMusic(new Rect(410, 90, 400, 610));
            DrawSounds(new Rect(830, 90, 420, 610));
        }

        // ---- Mix ------------------------------------------------------------------------------------------
        void DrawMix(Rect r)
        {
            GUI.Box(r, GUIContent.none);
            GUILayout.BeginArea(new Rect(r.x + 14, r.y + 10, r.width - 28, r.height - 20));
            GUILayout.Label("VOLUME", header);
            working.master = Slider("Master", working.master);
            working.music = Slider("Music", working.music);
            working.effects = Slider("Effects (hits, whooshes, menus)", working.effects);
            working.voices = Slider("Voices (shouts, lines)", working.voices);
            working.announcer = Slider("Announcer", working.announcer);
            GUILayout.Space(14);
            GUILayout.Label("MUSIC IN MATCHES", header);
            var choice = (SoundSettings.MusicChoice)GUILayout.Toolbar((int)working.musicChoice, new[] { "Stage's own", "One track", "Off" });
            if (choice != working.musicChoice) { working.musicChoice = choice; dirty = true; }
            GUILayout.Label(working.musicChoice == SoundSettings.MusicChoice.StageTrack ? "Each stage plays its own track (the original)." :
                working.musicChoice == SoundSettings.MusicChoice.OneTrack ? "Every match plays: " + working.track + "  (pick with USE in the list)" :
                "No music in matches or on the select screen.", wrap);
            bool final = GUILayout.Toggle(working.finalRoundMusic, " Final-round arrangement in the final round");
            if (final != working.finalRoundMusic) { working.finalRoundMusic = final; dirty = true; }
            bool subs = GUILayout.Toggle(working.subtitles, " Subtitles for entrance and victory lines");
            if (subs != working.subtitles) { working.subtitles = subs; dirty = true; }
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("SAVE", GUILayout.Height(36))) Save();
            if (GUILayout.Button("REVERT", GUILayout.Height(36))) { working.CopyFrom(SoundSettings.Current); dirty = false; status = "Back to the saved settings."; }
            if (GUILayout.Button("DEFAULTS", GUILayout.Height(36)))
            { var d = ScriptableObject.CreateInstance<SoundSettings>(); working.CopyFrom(d); Destroy(d); dirty = true; status = "Defaults (not saved yet)."; }
            GUILayout.EndHorizontal();
            GUILayout.Label((dirty ? "Unsaved changes. " : "") + status, wrap);
            GUILayout.EndArea();
        }

        float Slider(string label, float value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, small, GUILayout.Width(200));
            GUILayout.Label(Mathf.RoundToInt(value * 100) + "%", small, GUILayout.Width(44));
            GUILayout.EndHorizontal();
            float next = GUILayout.HorizontalSlider(value, 0, 1);
            GUILayout.Space(6);
            if (!Mathf.Approximately(next, value)) dirty = true;
            return next;
        }

        void Save()
        {
            var saved = SoundSettings.Current;
            saved.CopyFrom(working);
            try { saved.Save(); dirty = false; status = "Saved. Versus uses these settings from its next match."; }
            catch (Exception e) { status = "Not saved: " + e.Message; Debug.LogException(e); }
        }

        // ---- Music ----------------------------------------------------------------------------------------
        void DrawMusic(Rect r)
        {
            GUI.Box(r, GUIContent.none);
            GUI.Label(new Rect(r.x + 14, r.y + 10, 300, 24), "MUSIC  (" + tracks.Length + " tracks)", header);
            string playing = music.Playing;
            GUI.Label(new Rect(r.x + 14, r.y + 34, r.width - 120, 20),
                playing == null ? "Not playing" : "Playing: " + playing + (music.PlayingFinal ? " (final round)" : ""), small);
            if (GUI.Button(new Rect(r.xMax - 94, r.y + 32, 80, 24), "STOP")) music.Stop();
            var view = new Rect(r.x + 8, r.y + 62, r.width - 16, r.height - 70);
            float rowHeight = 28;
            var content = new Rect(0, 0, view.width - 20, tracks.Length * rowHeight);
            trackScroll = GUI.BeginScrollView(view, trackScroll, content);
            for (int i = 0; i < tracks.Length; i++)
            {
                string t = tracks[i];
                float y = i * rowHeight;
                bool on = t == playing;
                bool used = working.musicChoice == SoundSettings.MusicChoice.OneTrack && t == working.track;
                GUI.Label(new Rect(4, y + 4, 200, 22), (used ? "★ " : "") + t, on ? rowOn : small);
                if (GUI.Button(new Rect(206, y + 2, 64, 24), on && !music.PlayingFinal ? "▶ NORMAL" : "NORMAL", on && !music.PlayingFinal ? rowOn : row))
                    music.PlayTrack(t, false);
                if (music.HasFinal(t) && GUI.Button(new Rect(272, y + 2, 54, 24), on && music.PlayingFinal ? "▶ FINAL" : "FINAL", on && music.PlayingFinal ? rowOn : row))
                    music.PlayTrack(t, true);
                if (t != BattleMusic.SelectTrack && GUI.Button(new Rect(328, y + 2, 44, 24), "USE", row))
                { working.track = t; working.musicChoice = SoundSettings.MusicChoice.OneTrack; dirty = true; }
            }
            GUI.EndScrollView();
        }

        // ---- Sounds ---------------------------------------------------------------------------------------
        void DrawSounds(Rect r)
        {
            GUI.Box(r, GUIContent.none);
            GUI.Label(new Rect(r.x + 14, r.y + 10, 300, 24), "SOUNDS", header);
            int next = GUI.Toolbar(new Rect(r.x + 10, r.y + 38, r.width - 20, 26), page, Pages);
            if (next != page) { page = next; soundScroll = Vector2.zero; }
            float top = r.y + 70;
            if (Pages[page] == "Fighters" && fighters.Length > 0)
            {
                var labels = fighters.Select(c => FighterNames.TryGetValue(c, out var n) ? n : c).ToArray();
                int pick = GUI.SelectionGrid(new Rect(r.x + 10, top, r.width - 20, 52), fighter, labels, 4);
                if (pick != fighter) { fighter = pick; soundScroll = Vector2.zero; }
                top += 58;
            }
            var items = Items().ToList();
            GUI.Label(new Rect(r.x + 14, r.yMax - 26, r.width - 28, 22), lastPlayed == "" ? items.Count + " sounds; click one to play it." : "Played: " + lastPlayed, small);
            var view = new Rect(r.x + 8, top, r.width - 16, r.yMax - 30 - top);
            float y = 0;
            var heights = items.Select(i => i.text == null ? 26f : 44f).ToArray();
            var content = new Rect(0, 0, view.width - 20, heights.Sum() + 4);
            soundScroll = GUI.BeginScrollView(view, soundScroll, content);
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.header) { GUI.Label(new Rect(4, y + 4, content.width, 20), item.label, header); y += heights[i]; continue; }
                int variations = sound.Variations(item.name);
                string label = item.label + (variations > 1 ? "  ×" + variations : "");
                if (GUI.Button(new Rect(2, y + 1, content.width - 4, heights[i] - 2), label + (item.text != null ? "\n“" + item.text + "”" : ""), row))
                    Audition(item);
                y += heights[i];
            }
            GUI.EndScrollView();
        }

        struct Item { public string label, name, text; public int channel; public bool header; }
        const int Effects = 0, Voices = 1, AnnouncerChannel = 2;

        IEnumerable<Item> Items()
        {
            if (!sound || !sound.Loaded) yield break;
            switch (Pages[page])
            {
                case "Announcer":
                    foreach (var n in sound.EventNames.Where(n => n.StartsWith("Play_") || n.StartsWith("syvo_name_") || n.StartsWith("RES_BANK_SYVO") || n == "syvo_great").OrderBy(n => n))
                        yield return new Item { label = n, name = n, channel = AnnouncerChannel };
                    break;
                case "Battle":
                    yield return new Item { header = true, label = "Common battle table (move categories 1/6/7/8)" };
                    for (int i = 0; i < sound.Tones.Count; i++)
                        if (sound.Tones[i] != "") yield return new Item { label = i + "  " + sound.Tones[i], name = sound.Tones[i], channel = Effects };
                    yield return new Item { header = true, label = "Other battle sounds" };
                    var tones = new HashSet<string>(sound.Tones);
                    foreach (var n in sound.EventNames.Where(n => !tones.Contains(n) && (n.StartsWith("RES_BANK_") && !n.StartsWith("RES_BANK_SYVO") || n.StartsWith("FX_HIT") ||
                                 n == "Guard01" || n == "PlayerOne_FootSteps" || n.StartsWith("se_rage") || n.StartsWith("SE_battle") || n == "SE_RageArts_Start" || n == "SE_Combo_Finish")).OrderBy(n => n))
                        yield return new Item { label = n, name = n, channel = Effects };
                    break;
                case "System":
                    yield return new Item { header = true, label = "BattleSysTones used by the movesets (category 15)" };
                    for (int i = 0; i < sound.SystemTones.Count; i++)
                        if (sound.SystemTones[i] != "" && sound.Has(sound.SystemTones[i]))
                            yield return new Item { label = i + "  " + sound.SystemTones[i], name = sound.SystemTones[i], channel = Effects };
                    break;
                case "Menu":
                    foreach (var n in sound.EventNames.Where(n => n.StartsWith("SE_") && !n.StartsWith("SE_battle") && n != "SE_RageArts_Start" && n != "SE_Combo_Finish").OrderBy(n => n))
                        yield return new Item { label = n, name = n, channel = Effects };
                    break;
                case "Fighters":
                    if (fighters.Length == 0) yield break;
                    string code = fighters[Mathf.Clamp(fighter, 0, fighters.Length - 1)];
                    string call = sound.NameCall(code);
                    if (call != null) yield return new Item { label = "Name call  " + call, name = call, channel = AnnouncerChannel };
                    foreach (var role in Roles)
                    {
                        var list = sound.VoiceList(code, role);
                        if (list.Length == 0) continue;
                        yield return new Item { header = true, label = role == "lines" ? "Entrance (_s) and victory (_w) lines" : role == "attack" ? "Attack shouts" : role == "damage" ? "Pain cries" : "Death cries" };
                        foreach (var n in list) yield return new Item { label = n, name = n, channel = Voices, text = role == "lines" ? sound.LineText(n) : null };
                    }
                    break;
            }
        }

        void Audition(Item item)
        {
            bool played = item.channel == AnnouncerChannel ? sound.Announce(item.name) : item.channel == Voices ? sound.Say(0, item.name) : sound.Play(item.name);
            lastPlayed = played ? item.name : item.name + " (no clip)";
        }
    }
}
