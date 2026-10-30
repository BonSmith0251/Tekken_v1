// Minimal stand-ins for the two UnityEngine calls CommandGraph uses, so the pure C# battle code runs on .NET.
using System.IO;
using System.Text.Json;
namespace UnityEngine
{
    public class Object { public static implicit operator bool(Object o) => o != null; }
    public class TextAsset : Object { public string text; }
    public static class Resources
    {
        public static string Root;
        public static T Load<T>(string path) where T : class
        {
            var file = Path.Combine(Root, path + ".json");
            return File.Exists(file) ? new TextAsset { text = File.ReadAllText(file) } as T : null;
        }
    }
    public static class JsonUtility
    {
        static readonly JsonSerializerOptions Options = new JsonSerializerOptions { IncludeFields = true };
        public static T FromJson<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
    }
}
