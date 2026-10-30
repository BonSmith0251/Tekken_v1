using System;
using System.Collections.Generic;
using System.Linq;

namespace Versus.Remaster.Arcade
{
    [Serializable] public sealed class ArcadeKeyBinding
    {
        public ArcadeControls control;
        public string key, alternate;
        public ArcadeKeyBinding Copy() => (ArcadeKeyBinding)MemberwiseClone();
    }
    [Serializable] public sealed class ArcadeBindings
    {
        public int schema = 1;
        public ArcadeKeyBinding[] keys;
        public static ArcadeBindings Defaults()
        {
            string[] primary = { "W", "S", "A", "D", "U", "I", "J", "K", "UpArrow", "DownArrow", "LeftArrow", "RightArrow",
                "Keypad4", "Keypad5", "Keypad1", "Keypad2", "Alpha1", "Alpha2", "Alpha5", "Alpha6", "Alpha9", "F2" };
            var result = new ArcadeBindings { keys = new ArcadeKeyBinding[22] };
            for (int i = 0; i < 22; i++) result.keys[i] = new ArcadeKeyBinding { control = (ArcadeControls)(1u << i), key = primary[i] };
            string[] alternatives = { "O", "P", "L", "Semicolon" };
            for (int i = 0; i < 4; i++) result.keys[12 + i].alternate = alternatives[i];
            return result;
        }
        public ArcadeBindings Copy() => new ArcadeBindings { schema = schema, keys = keys.Select(k => k.Copy()).ToArray() };
        public void Validate()
        {
            if (schema != 1 || keys == null || keys.Length != 22) throw new ArgumentException("Expected all 22 keyboard controls.");
            var controls = new HashSet<ArcadeControls>(); var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in keys)
            {
                uint bit = row == null ? 0 : (uint)row.control;
                if (bit == 0 || bit >= 1u << 22 || (bit & (bit - 1)) != 0 || !controls.Add(row.control)) throw new ArgumentException("Duplicate or unknown logical control.");
                foreach (string key in new[] { row.key, row.alternate })
                {
                    if (key == null && key == row.alternate) continue;
                    if (string.IsNullOrWhiteSpace(key) || key.Length > 40 || key.Any(c => !char.IsLetterOrDigit(c)) || key == "None" || key == "Escape")
                        throw new ArgumentException("Invalid/reserved key: " + key);
                    if (key == "F2" && row.control != ArcadeControls.Test) throw new ArgumentException("F2 is reserved for operator recovery.");
                    if (!used.Add(key)) throw new ArgumentException("Key already assigned: " + key);
                }
                if (string.IsNullOrEmpty(row.key)) throw new ArgumentException("Each control needs a primary key.");
            }
        }
        public ArcadeBindings Rebind(ArcadeControls control, string key)
        {
            var copy = Copy(); var row = copy.keys.Single(k => k.control == control);
            row.key = key; row.alternate = null; copy.Validate(); return copy;
        }
        public ArcadeControls Read(Func<string, bool> held)
        {
            ArcadeControls value = 0;
            foreach (var row in keys)
                if (held(row.key) || row.alternate != null && held(row.alternate)) value |= row.control;
            // Fixed recovery key is never available to coin/start/gameplay mappings.
            if (held("F2")) value |= ArcadeControls.Test;
            return value;
        }
        public string Label(ArcadeControls control)
        {
            var row = keys.Single(k => k.control == control);
            return KeyLabel(row.key) + (row.alternate == null ? "" : " / " + KeyLabel(row.alternate));
        }
        static string KeyLabel(string key) => key.StartsWith("Alpha", StringComparison.Ordinal) ? key.Substring(5) :
            key.StartsWith("Keypad", StringComparison.Ordinal) ? "Num " + key.Substring(6) :
            key.EndsWith("Arrow", StringComparison.Ordinal) ? key.Substring(0, key.Length - 5) : key == "Semicolon" ? ";" : key;
    }
}
