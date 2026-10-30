using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Versus.Remaster.Arcade
{
    // Core serialization is identical in Unity and the .NET harness; no Unity shim is involved.
    public static class ArcadeJson
    {
        public static string Encode<T>(T value)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        public static T Decode<T>(string json)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }
    }

    public enum ArcadeWriteStage { BeforeWrite, AfterFlush, BeforeCommit, AfterCommit }

    // One owner holds a separate lock file for the lifetime of this store. Dispose before reopening.
    // Only the primary file is committed. .tmp is never promoted on startup, and a suspect primary
    // never silently rolls back money to .bak. Backup inspection/reconciliation belongs to the operator.
    public sealed class ArcadeAtomicFile<T> : IDisposable where T : class
    {
        [Serializable]
        public sealed class Envelope
        {
            public int schema = 1;
            public string payload, sha256;
        }

        readonly string path;
        readonly Action<T> validate;
        readonly Action<ArcadeWriteStage> faultInjection;
        readonly FileStream owner;
        bool disposed, failed;
        const long MaxBytes = 4 * 1024 * 1024;

        public ArcadeAtomicFile(string path, Action<T> validate, Action<ArcadeWriteStage> faultInjection = null)
        {
            this.path = Path.GetFullPath(path);
            this.validate = validate ?? throw new ArgumentNullException(nameof(validate));
            this.faultInjection = faultInjection;
            Directory.CreateDirectory(Path.GetDirectoryName(this.path));
            owner = new FileStream(this.path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }

        public T Load()
        {
            EnsureUsable();
            try
            {
                if (!File.Exists(path))
                {
                    if (File.Exists(path + ".bak") || File.Exists(path + ".tmp"))
                        throw new InvalidDataException("Primary arcade file is missing but recovery evidence exists: " + path);
                    return null; // Only a genuinely new store starts empty.
                }
                if (new FileInfo(path).Length > MaxBytes) throw new InvalidDataException("Arcade file exceeds size limit.");
                var envelope = ArcadeJson.Decode<Envelope>(File.ReadAllText(path, Encoding.UTF8));
                if (envelope == null || envelope.schema != 1 || envelope.payload == null ||
                    !string.Equals(envelope.sha256, Hash(envelope.payload), StringComparison.Ordinal))
                    throw new InvalidDataException("Arcade file checksum/schema failed: " + path);
                var value = ArcadeJson.Decode<T>(envelope.payload);
                if (value == null) throw new InvalidDataException("Empty arcade payload.");
                validate(value);
                return value;
            }
            catch { failed = true; throw; }
        }

        public void Save(T value)
        {
            EnsureUsable();
            validate(value);
            string payload = ArcadeJson.Encode(value);
            byte[] bytes = Encoding.UTF8.GetBytes(ArcadeJson.Encode(new Envelope { payload = payload, sha256 = Hash(payload) }));
            if (bytes.LongLength > MaxBytes) throw new InvalidDataException("Arcade file exceeds size limit.");
            try
            {
                faultInjection?.Invoke(ArcadeWriteStage.BeforeWrite);
                using (var stream = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                faultInjection?.Invoke(ArcadeWriteStage.AfterFlush);
                faultInjection?.Invoke(ArcadeWriteStage.BeforeCommit);
                CommitFile();
                faultInjection?.Invoke(ArcadeWriteStage.AfterCommit);
            }
            catch { failed = true; throw; } // Outcome may be uncertain: reopen; never retry on this instance.
        }

        static string Hash(string value)
        {
            using (var sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));
        }

        void CommitFile()
        {
            // File watchers/virus scanners can briefly open a newly written file without delete
            // sharing. Retry only that OS error, for at most 150 ms, without reapplying the transaction.
            // Other failures (including an uncertain commit) fault the store immediately.
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    if (File.Exists(path)) File.Replace(path + ".tmp", path, path + ".bak");
                    else File.Move(path + ".tmp", path);
                    return;
                }
                catch (IOException e) when (attempt < 4 && ((e.HResult & 0xFFFF) == 32 || (e.HResult & 0xFFFF) == 33)
                    && File.Exists(path + ".tmp"))
                { Thread.Sleep(10 << attempt); }
            }
        }

        void EnsureUsable()
        {
            if (disposed) throw new ObjectDisposedException(nameof(ArcadeAtomicFile<T>));
            if (failed) throw new InvalidOperationException("Arcade storage is faulted; close and recover before further writes.");
        }

        public void Dispose() { if (disposed) return; disposed = true; owner.Dispose(); }
    }

    public static class ArcadeSettingsFile
    {
        // Missing/corrupt settings use labelled defaults, with a diagnostic, without overwriting evidence.
        public static ArcadeSettings Load(string path, out string diagnostic)
        {
            diagnostic = null;
            try
            {
                using (var file = new ArcadeAtomicFile<ArcadeSettings>(path, s => s.Validate()))
                {
                    var settings = file.Load();
                    if (settings != null) return settings;
                    diagnostic = "Arcade settings missing; using provisional defaults.";
                }
            }
            catch (Exception e) { diagnostic = "Arcade settings unavailable; using provisional defaults: " + e.Message; }
            return new ArcadeSettings();
        }

        public static void Save(string path, ArcadeSettings settings)
        {
            settings.Validate();
            using (var file = new ArcadeAtomicFile<ArcadeSettings>(path, s => s.Validate())) file.Save(settings.Copy());
        }
    }
}
