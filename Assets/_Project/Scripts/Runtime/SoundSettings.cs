using System;
using System.IO;
using UnityEngine;

namespace Versus.Remaster
{
    // The sound mix chosen in sound_settings (Resources/SoundSettings.asset). BattleSound and BattleMusic read it.
    // In the editor Save writes the asset; a player build keeps its copy in persistentDataPath.
    [CreateAssetMenu(menuName = "Remaster/Sound settings")]
    public sealed class SoundSettings : ScriptableObject
    {
        public enum MusicChoice { StageTrack, OneTrack, Off }

        [Range(0, 1)] public float master = 1;
        [Range(0, 1)] public float music = .5f;
        [Tooltip("Hits, whooshes, guards, footsteps, Rage and menu tones.")]
        [Range(0, 1)] public float effects = 1;
        [Tooltip("The fighters' shouts, cries and entrance / victory lines.")]
        [Range(0, 1)] public float voices = .9f;
        [Tooltip("Round calls, K.O., the name calls.")]
        [Range(0, 1)] public float announcer = 1;
        [Tooltip("StageTrack: each stage's own music (the original); OneTrack: the track below in every match.")]
        public MusicChoice musicChoice = MusicChoice.StageTrack;
        [Tooltip("BGM_Selector name, e.g. stg29_uyuni_vr (used with OneTrack).")]
        public string track = "stg11_arena";
        [Tooltip("Switch to the track's Final arrangement in the final round (the original FinalRound state).")]
        public bool finalRoundMusic = true;
        [Tooltip("Show the game's English subtitles for entrance and victory lines.")]
        public bool subtitles = true;

        static SoundSettings current;
        static string PlayerFile => Path.Combine(Application.persistentDataPath, "sound-settings.json");

        /// <summary>The saved settings (loaded once).</summary>
        public static SoundSettings Current
        {
            get
            {
                if (current) return current;
                var asset = Resources.Load<SoundSettings>("SoundSettings");
                current = asset ? asset : CreateInstance<SoundSettings>();
#if !UNITY_EDITOR
                try { if (File.Exists(PlayerFile)) { current = Instantiate(current); JsonUtility.FromJsonOverwrite(File.ReadAllText(PlayerFile), current); } }
                catch (Exception e) { Debug.LogWarning("Sound settings not read: " + e.Message); }
#endif
                return current;
            }
        }

        public float Music => master * music;
        public float Effects => master * effects;
        public float Voices => master * voices;
        public float Announcer => master * announcer;

        public void Save()
        {
#if UNITY_EDITOR
            if (!UnityEditor.AssetDatabase.Contains(this))
                UnityEditor.AssetDatabase.CreateAsset(this, "Assets/_Project/Resources/SoundSettings.asset");
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.AssetDatabase.SaveAssetIfDirty(this);
#else
            File.WriteAllText(PlayerFile, JsonUtility.ToJson(this));
#endif
        }

        /// <summary>The settings scene's unsaved working copy, heard live while it is open; null otherwise.</summary>
        public static SoundSettings Preview;

        /// <summary>What the players use now: the settings scene's working copy, else the saved settings.</summary>
        public static SoundSettings Active => Preview ? Preview : Current;

        public void CopyFrom(SoundSettings other) => JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(other), this);
    }
}
