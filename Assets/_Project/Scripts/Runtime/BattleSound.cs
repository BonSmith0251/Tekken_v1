using System;
using System.Collections.Generic;
using UnityEngine;

namespace Versus.Remaster
{
    /// <summary>
    /// Tekken 7's battle sounds, exported from its Wwise banks by Tools/BattleSound/export_battle_sound.py
    /// into Resources/BattleSound (one clip per media file, sounds.json naming the events and each fighter's
    /// voice lists). Events play by their original Wwise name; an event with several media files picks one
    /// at random, as its random container does. Each fighter speaks on its own channel: a new line cuts the
    /// previous one.
    /// </summary>
    public sealed class BattleSound : MonoBehaviour
    {
        [Serializable] sealed class Event { public string name; public string[] files; }
        [Serializable] sealed class Voices { public string code, name; public string[] attack, damage, death, lines; }
        [Serializable] sealed class Line { public string name, text; }
        [Serializable] sealed class Table { public Event[] events; public Voices[] voices; public string[] tones, systemTones; public Line[] lines; }
        [Serializable] sealed class MoveSounds { public int id; public int[] clips; }
        [Serializable] sealed class MoveTable { public MoveSounds[] moves; }

        // Original voice categories of property 0x84C4 / 0x84C6 (value = category << 24 | index), named by the
        // common helper moves SE_KIAI_n, SE_YARARE_n and SE_DEAD_n.
        public const int AttackVoice = 2, DamageVoice = 3, DeathVoice = 4;
        // Category 12 with index ItemLine + n: the fighter's "<...>_item<nn>" line (Alisa's chainsaws, thrusters).
        public const int ItemCategory = 12, ItemLine = 0x2000;
        // Categories 1, 6, 7 and 8 index the common battle table (the executable's /Game/Sound/Battle list: whooshes,
        // punches, bone cracks, sparks); 15 indexes the common manager's BattleSysTones (Rage Art, Devil Kazuya cues).
        public const int GuardTone = 1, SwingTone = 6, ImpactTone = 7, SystemTone = 15;
        // A move's own sound list (its voiceclip block): 2:999 asks for any attack shout.
        public const int AnyVoice = 999;

        const int EffectSources = 10;
        readonly Dictionary<string, string[]> events = new Dictionary<string, string[]>(StringComparer.Ordinal);
        readonly Dictionary<string, Voices> voices = new Dictionary<string, Voices>(StringComparer.Ordinal);
        readonly Dictionary<int, string> items = new Dictionary<int, string>();
        readonly Dictionary<string, string> lines = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly Dictionary<string, Dictionary<int, int[]>> moveSounds = new Dictionary<string, Dictionary<int, int[]>>(StringComparer.Ordinal);
        string[] tones = Array.Empty<string>(), systemTones = Array.Empty<string>();
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>(StringComparer.Ordinal);
        readonly List<AudioSource> pool = new List<AudioSource>();
        readonly AudioSource[] speakers = new AudioSource[2];
        int next;

        public float volume = 1;
        public bool Loaded => events.Count > 0;

        public static BattleSound Create(Transform parent)
        {
            var sound = new GameObject("Battle Sound").AddComponent<BattleSound>();
            sound.transform.SetParent(parent, false);
            sound.Load();
            return sound;
        }

        void Load()
        {
            var json = Resources.Load<TextAsset>("BattleSound/sounds");
            if (!json) { Debug.LogWarning("BattleSound: Resources/BattleSound/sounds.json is missing (run Tools/BattleSound)."); return; }
            var table = JsonUtility.FromJson<Table>(json.text);
            foreach (var e in table.events ?? Array.Empty<Event>())
            {
                if (e.files == null || e.files.Length == 0) continue;
                events[e.name] = e.files;
                int at = e.name.LastIndexOf("_item", StringComparison.Ordinal);
                if (at > 0 && int.TryParse(e.name.Substring(at + 5), out int n)) items[n] = e.name;
            }
            foreach (var v in table.voices ?? Array.Empty<Voices>()) voices[v.code] = v;
            foreach (var l in table.lines ?? Array.Empty<Line>()) lines[l.name] = l.text;
            tones = table.tones ?? tones; systemTones = table.systemTones ?? systemTones;
            for (int i = 0; i < EffectSources; i++) pool.Add(NewSource());
            for (int i = 0; i < 2; i++) speakers[i] = NewSource();
        }

        AudioSource NewSource()
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false; source.spatialBlend = 0;
            return source;
        }

        public bool Has(string name) => name != null && events.ContainsKey(name);

        /// <summary>One of an event's clips (at random), for players that outlive this component.</summary>
        public AudioClip Clip(string name) => Pick(name);

        /// <summary>Plays an original event (one of its variations at random) on the effects channel; false when it
        /// isn't exported.</summary>
        public bool Play(string name, float gain = 1, float pitch = 1) => PlayOn(name, gain * SoundSettings.Active.Effects, pitch);

        /// <summary>An announcer call (round calls, K.O., name calls) on the announcer channel.</summary>
        public bool Announce(string name, float gain = 1) => PlayOn(name, gain * SoundSettings.Active.Announcer, 1);

        bool PlayOn(string name, float gain, float pitch)
        {
            var clip = Pick(name);
            if (!clip) return false;
            var source = pool[next]; next = (next + 1) % pool.Count;
            source.Stop(); source.clip = clip; source.volume = Mathf.Clamp01(gain * volume); source.pitch = pitch;
            source.Play();
            return true;
        }

        /// <summary>A fighter's own line (voice, entrance or victory take) on the voices channel, cutting its
        /// previous one.</summary>
        public bool Say(int player, string name, float gain = 1)
        {
            var clip = Pick(name);
            if (!clip || player < 0 || player > 1) return false;
            var speaker = speakers[player];
            speaker.Stop(); speaker.clip = clip; speaker.volume = Mathf.Clamp01(gain * volume * SoundSettings.Active.Voices); speaker.pitch = 1;
            speaker.Play();
            return true;
        }

        // ---- Catalogue (the sound settings scene browses these) ----------------------------------------------
        public IEnumerable<string> EventNames => events.Keys;
        public IEnumerable<string> FighterCodes => voices.Keys;
        public IReadOnlyList<string> Tones => tones;
        public IReadOnlyList<string> SystemTones => systemTones;
        public IEnumerable<KeyValuePair<string, string>> Lines => lines;
        public int Variations(string name) => name != null && events.TryGetValue(name, out var files) ? files.Length : 0;

        /// <summary>A fighter's attack / damage / death voices or entrance / victory lines (role "attack", "damage", "death", "lines").</summary>
        public string[] VoiceList(string code, string role) =>
            code == null || !voices.TryGetValue(code, out var v) ? Array.Empty<string>() :
            role == "attack" ? v.attack ?? Array.Empty<string>() : role == "damage" ? v.damage ?? Array.Empty<string>() :
            role == "lines" ? v.lines ?? Array.Empty<string>() : v.death ?? Array.Empty<string>();

        /// <summary>The n-th voice of an original category (attack / damage / death) from the fighter's list.</summary>
        public bool Voice(int player, string code, int category, int index, float gain = 1)
        {
            if (code == null || !voices.TryGetValue(code, out var v)) return false;
            var list = category == AttackVoice ? v.attack : category == DamageVoice ? v.damage : category == DeathVoice ? v.death : null;
            if (list == null || list.Length == 0 || index < 0) return false;
            if (index == AnyVoice) index = UnityEngine.Random.Range(0, list.Length);
            // Lists differ in length between fighters; a common move's higher index wraps around.
            return Say(player, list[index % list.Length], gain);
        }

        /// <summary>A common battle tone (categories 1/6/7/8) or battle-system tone (15) by its original index.</summary>
        public bool Tone(int category, int index, float gain = 1)
        {
            var table = category == SystemTone ? systemTones : category == GuardTone || category == SwingTone || category == ImpactTone || category == 8 ? tones : null;
            return table != null && index >= 0 && index < table.Length && Play(table[index], gain);
        }

        public static bool IsTone(int category) => category == GuardTone || category == SwingTone || category == ImpactTone || category == 8 || category == SystemTone;

        /// <summary>The announcer's call of the fighter's name (syvo_name_*), or null.</summary>
        public string NameCall(string code) =>
            code != null && voices.TryGetValue(code, out var v) && !string.IsNullOrEmpty(v.name) ? v.name : null;

        /// <summary>The English text of an entrance or victory line (game subtitles), or null.</summary>
        public string LineText(string name) => name != null && lines.TryGetValue(name, out var text) ? text : null;

        /// <summary>Length in seconds of a fighter's line being spoken now, 0 when silent.</summary>
        public float Speaking(int player) =>
            player >= 0 && player < 2 && speakers[player] && speakers[player].isPlaying && speakers[player].clip
                ? speakers[player].clip.length - speakers[player].time : 0;

        /// <summary>A move's own sound list (category << 24 | index values), from Resources/BattleSound/moves_&lt;code&gt;.json.</summary>
        public int[] MoveClips(string code, int move)
        {
            if (code == null) return null;
            if (!moveSounds.TryGetValue(code, out var table))
            {
                table = new Dictionary<int, int[]>();
                var json = Resources.Load<TextAsset>("BattleSound/moves_" + code);
                if (json)
                    foreach (var m in JsonUtility.FromJson<MoveTable>(json.text).moves ?? Array.Empty<MoveSounds>())
                        table[m.id] = m.clips;
                moveSounds[code] = table;
            }
            return table.TryGetValue(move, out var clips) ? clips : null;
        }

        /// <summary>A fighter's machinery line (category 12, index ItemLine + n).</summary>
        public bool Item(int index, float gain = 1) =>
            index >= ItemLine && items.TryGetValue(index - ItemLine, out var name) && Play(name, gain);

        /// <summary>A pain cry graded by the blow (0 light .. 2 heavy) from the fighter's damage list, which
        /// runs from light to heavy cries; a death cry at random.</summary>
        public bool Cry(int player, string code, int tier, bool death, float gain = 1)
        {
            if (code == null || !voices.TryGetValue(code, out var v)) return false;
            var list = death ? v.death : v.damage;
            if (list == null || list.Length == 0) return false;
            int from = death ? 0 : list.Length * Mathf.Clamp(tier, 0, 2) / 3, to = death ? list.Length : Mathf.Max(from + 1, list.Length * (Mathf.Clamp(tier, 0, 2) + 1) / 3);
            return Say(player, list[UnityEngine.Random.Range(from, Mathf.Min(to, list.Length))], gain);
        }

        public void StopVoices() { foreach (var s in speakers) if (s) s.Stop(); }

        public void StopAll()
        {
            foreach (var s in pool) if (s) s.Stop();
            StopVoices();
        }

        AudioClip Pick(string name)
        {
            if (name == null || !events.TryGetValue(name, out var files)) return null;
            string file = files[files.Length == 1 ? 0 : UnityEngine.Random.Range(0, files.Length)];
            if (!clips.TryGetValue(file, out var clip))
            {
                clip = Resources.Load<AudioClip>("BattleSound/" + file);
                clips[file] = clip;
            }
            return clip;
        }
    }
}
