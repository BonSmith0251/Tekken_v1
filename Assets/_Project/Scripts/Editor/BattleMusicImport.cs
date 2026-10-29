using UnityEditor;
using UnityEngine;

namespace Versus.Remaster.Editor
{
    // The stage music (Resources/BattleMusic, 1-3 minute 5.1 tracks) streams from disk instead of being decompressed
    // into memory; battle sounds keep Unity's defaults.
    sealed class BattleMusicImport : AssetPostprocessor
    {
        void OnPreprocessAudio()
        {
            if (!assetPath.Replace('\\', '/').Contains("/Resources/BattleMusic/")) return;
            var importer = (AudioImporter)assetImporter;
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.Streaming;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = .7f;
            importer.defaultSampleSettings = settings;
            importer.loadInBackground = true;
        }
    }
}
