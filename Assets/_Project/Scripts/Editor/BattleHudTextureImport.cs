using UnityEditor;

namespace Versus.Remaster.Editor
{
    // The battle HUD and character select draw their original UI textures 1:1-ish on screen: no mipmaps,
    // no wrapping, no compression banding on the gauge gradients.
    public sealed class BattleHudTextureImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            string path = assetPath.Replace('\\', '/');
            if (!path.Contains("/Resources/BattleHud/") && !path.Contains("/Resources/CharacterSelect/")) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            importer.filterMode = UnityEngine.FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            // The select screen's 1800x1320 character art would take ~95 MB uncompressed.
            string file = System.IO.Path.GetFileName(path);
            if (file.StartsWith("Art_") || file.StartsWith("VsArt_"))
            {
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.maxTextureSize = 2048;
            }
        }
    }
}
