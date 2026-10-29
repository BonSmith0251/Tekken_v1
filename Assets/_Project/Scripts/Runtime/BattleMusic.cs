using System;
using System.Collections.Generic;
using UnityEngine;

namespace Versus.Remaster
{
    /// <summary>
    /// Tekken 7's music, exported from its AkBankBGM by Tools/BattleSound/export_battle_music.py into
    /// Resources/BattleMusic (music.json + one clip per arrangement). In the game, BGM_Container switches on the
    /// BGM_Selector state (one value per track, a stage's being stgNN_<name>) and then on the FinalRound state,
    /// Normal or Final. This player keeps that model: a track per stage, its Final arrangement from the final
    /// round on, and the CharaSelect track on the select screen. It survives scene reloads (select → match), so a
    /// change of track crossfades instead of cutting.
    /// </summary>
    public sealed class BattleMusic : MonoBehaviour
    {
        [Serializable] sealed class Track { public string name, stage, normal, final; }
        [Serializable] sealed class Table { public Track[] tracks; }

        public const string SelectTrack = "CharaSelect";
        // Remaster choice: the generated arena and the stages without a track of their own (stg17, stg25, stg90)
        // play the Arena stage's music. stg08a uses stg08's.
        const string FallbackTrack = "stg11_arena";
        const float FadeSeconds = 1.5f;

        static BattleMusic instance;
        readonly Dictionary<string, Track> byName = new Dictionary<string, Track>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Track> byStage = new Dictionary<string, Track>(StringComparer.OrdinalIgnoreCase);
        readonly AudioSource[] decks = new AudioSource[2];
        int live;
        Track current;
        bool final, silent = true;
        float fade = 1;

        public string Playing => current?.name;
        public bool PlayingFinal => current != null && final;
        public IEnumerable<string> TrackNames => byName.Keys;
        public bool HasFinal(string name) => byName.TryGetValue(name, out var t) && !string.IsNullOrEmpty(t.final) && t.final != t.normal;
        // The music volume of the sound settings (live, so the settings scene's sliders are heard at once).
        static float Volume => SoundSettings.Active.Music;

        public static BattleMusic Instance
        {
            get
            {
                if (!instance)
                {
                    instance = new GameObject("Battle Music").AddComponent<BattleMusic>();
                    DontDestroyOnLoad(instance.gameObject);
                    instance.Load();
                }
                return instance;
            }
        }

        void Load()
        {
            for (int i = 0; i < decks.Length; i++)
            {
                var deck = decks[i] = gameObject.AddComponent<AudioSource>();
                deck.playOnAwake = false; deck.loop = true; deck.spatialBlend = 0; deck.volume = 0;
                // Music keeps playing through the KO near-freeze and pause, which slow Time.timeScale.
                deck.ignoreListenerPause = true;
            }
            var json = Resources.Load<TextAsset>("BattleMusic/music");
            if (!json) { Debug.LogWarning("BattleMusic: Resources/BattleMusic/music.json is missing (run Tools/BattleSound/export_battle_music.py)."); return; }
            foreach (var t in JsonUtility.FromJson<Table>(json.text).tracks ?? Array.Empty<Track>())
            {
                byName[t.name] = t;
                if (!string.IsNullOrEmpty(t.stage) && !byStage.ContainsKey(t.stage)) byStage[t.stage] = t;
            }
        }

        /// <summary>The music of a stage (stg08a → stg08; no track or "arena" → the Arena stage's), or the sound
        /// settings' one track, or silence.</summary>
        public void PlayStage(string stage, bool finalRound = false)
        {
            var settings = SoundSettings.Active;
            if (settings.musicChoice == SoundSettings.MusicChoice.Off) { Stop(); return; }
            if (settings.musicChoice == SoundSettings.MusicChoice.OneTrack && byName.ContainsKey(settings.track ?? ""))
            { PlayTrack(settings.track, finalRound && settings.finalRoundMusic); return; }
            Track track = null;
            if (!string.IsNullOrEmpty(stage) && !byStage.TryGetValue(stage, out track) && stage.Length > 5)
                byStage.TryGetValue(stage.Substring(0, 5), out track);
            Play(track ?? (byName.TryGetValue(FallbackTrack, out var fallback) ? fallback : null), finalRound && settings.finalRoundMusic);
        }

        /// <summary>A track by its BGM_Selector name (e.g. CharaSelect).</summary>
        public void PlayTrack(string name, bool finalRound = false) =>
            Play(name != null && byName.TryGetValue(name, out var track) ? track : null, finalRound);

        /// <summary>The FinalRound state: the playing stage track changes to its Final (or back to Normal) arrangement.</summary>
        public void SetFinalRound(bool finalRound)
        {
            finalRound &= SoundSettings.Active.finalRoundMusic;
            if (current != null && finalRound != final) Play(current, finalRound, force: true);
        }

        public void Stop() { current = null; Crossfade(null); }

        /// <summary>A one-shot that has to survive a scene reload (the VS screen's sting).</summary>
        public void Sting(AudioClip clip, float gain = 1)
        {
            if (!clip) return;
            if (!sting) { sting = gameObject.AddComponent<AudioSource>(); sting.playOnAwake = false; sting.spatialBlend = 0; sting.ignoreListenerPause = true; }
            sting.PlayOneShot(clip, gain);
        }
        AudioSource sting;

        void Play(Track track, bool finalRound, bool force = false)
        {
            if (track == null) return;
            if (!force && track == current && finalRound == final) return;
            string file = finalRound && !string.IsNullOrEmpty(track.final) ? track.final : track.normal;
            bool sameFile = current != null && (final && !string.IsNullOrEmpty(current.final) ? current.final : current.normal) == file;
            current = track; final = finalRound;
            if (sameFile && decks[live].isPlaying) return;      // stg21/stg22 use one arrangement for every round
            var clip = Resources.Load<AudioClip>("BattleMusic/" + file);
            if (!clip) { Debug.LogWarning("BattleMusic: missing clip " + file); return; }
            Crossfade(clip);
        }

        // A new clip starts on the idle deck and fades in while the other fades out; null fades everything out.
        void Crossfade(AudioClip clip)
        {
            silent = !clip;
            if (clip)
            {
                live = 1 - live;
                decks[live].clip = clip; decks[live].volume = 0; decks[live].Play();
            }
            fade = 0;
        }

        void Update()
        {
            float volume = Volume;
            if (fade >= 1) { if (!silent && decks[live].isPlaying) decks[live].volume = volume; return; }
            fade = Mathf.Min(1, fade + Time.unscaledDeltaTime / FadeSeconds);
            for (int i = 0; i < decks.Length; i++)
            {
                var deck = decks[i];
                float target = i == live && !silent ? volume : 0;
                deck.volume = Mathf.MoveTowards(deck.volume, target, Time.unscaledDeltaTime / FadeSeconds * Mathf.Max(volume, .01f));
                if (fade >= 1) { deck.volume = target; if (target == 0) deck.Stop(); }
            }
        }
    }
}
