using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Versus.Remaster.Editor
{
    // Loads every exported battle sound and music clip the way the scenes do (Resources) and reports what is missing.
    // Flag: Temp/validate-battle-sound → Validation/BattleSound/validation.json.
    [InitializeOnLoad]
    static class BattleSoundValidation
    {
        [Serializable] sealed class Event { public string name; public string[] files; }
        [Serializable] sealed class Voices { public string code, name; public string[] attack, damage, death; }
        [Serializable] sealed class Table { public Event[] events; public Voices[] voices; public string[] tones, systemTones; }
        [Serializable] sealed class Track { public string name, stage, normal, final; }
        [Serializable] sealed class Music { public Track[] tracks; }
        [Serializable] sealed class MoveSounds { public int id; public int[] clips; }
        [Serializable] sealed class MoveTable { public MoveSounds[] moves; }

        static BattleSoundValidation() { EditorApplication.update += Poll; }

        static void Poll()
        {
            if (!File.Exists("Temp/validate-battle-sound") || EditorApplication.isCompiling) return;
            File.Delete("Temp/validate-battle-sound");
            try { Run(); }
            catch (Exception e) { Debug.LogException(e); File.WriteAllText("Temp/validate-battle-sound-error.txt", e.ToString()); }
        }

        [MenuItem("Remaster/Validate battle sound and music")]
        static void Run()
        {
            var problems = new List<string>();
            var table = JsonUtility.FromJson<Table>(Resources.Load<TextAsset>("BattleSound/sounds").text);
            var events = new HashSet<string>(table.events.Select(e => e.name));
            int clips = 0;
            foreach (var e in table.events)
                foreach (var f in e.files)
                {
                    var clip = Resources.Load<AudioClip>("BattleSound/" + f);
                    if (!clip) problems.Add("missing clip " + f); else clips++;
                }
            foreach (var v in table.voices)
            {
                if (string.IsNullOrEmpty(v.name)) problems.Add(v.code + ": no name call");
                if (v.attack.Length == 0 || v.damage.Length == 0 || v.death.Length == 0) problems.Add(v.code + ": an empty voice list");
                var moves = Resources.Load<TextAsset>("BattleSound/moves_" + v.code);
                if (!moves) { problems.Add(v.code + ": no move sound lists"); continue; }
                foreach (var m in JsonUtility.FromJson<MoveTable>(moves.text).moves)
                    foreach (int c in m.clips)
                    {
                        int category = c >> 24, index = c & 0xFFFFFF;
                        if ((category == 1 || category == 6 || category == 7) && (index >= table.tones.Length || table.tones[index] == ""))
                            problems.Add($"{v.code} move {m.id}: tone {category}:{index} not exported");
                    }
            }
            int tones = table.tones.Count(t => t != "" && events.Contains(t));
            var music = JsonUtility.FromJson<Music>(Resources.Load<TextAsset>("BattleMusic/music").text);
            float seconds = 0; int tracks = 0;
            foreach (var t in music.tracks)
                foreach (var f in new[] { t.normal, t.final }.Distinct())
                {
                    var clip = Resources.Load<AudioClip>("BattleMusic/" + f);
                    if (!clip) { problems.Add("missing music " + f); continue; }
                    tracks++; seconds += clip.length;
                }
            Directory.CreateDirectory("Validation/BattleSound");
            File.WriteAllText("Validation/BattleSound/validation.json", JsonUtility.ToJson(new Report
            {
                passed = problems.Count == 0, soundClips = clips, events = table.events.Length, tones = tones,
                fighters = table.voices.Length, musicClips = tracks, musicMinutes = seconds / 60, problems = problems.Take(50).ToArray()
            }, true));
            Debug.Log($"Battle sound validation: {clips} sound clips, {tracks} music clips, {problems.Count} problems.");
        }

        [Serializable] sealed class Report
        {
            public bool passed; public int soundClips, events, tones, fighters, musicClips; public float musicMinutes; public string[] problems;
        }
    }
}
