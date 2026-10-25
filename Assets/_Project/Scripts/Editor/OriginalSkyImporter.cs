using System;
using System.IO;
using System.IO.Compression;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Versus.Remaster.Editor
{
    [ScriptedImporter(1, "t7sky")]
    public sealed class OriginalSkyImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext context)
        {
            using var file = File.OpenRead(context.assetPath);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var reader = new BinaryReader(gzip);
            if (new string(reader.ReadChars(4)) != "T7SK") throw new InvalidDataException("Invalid original sky header");
            int size = reader.ReadInt32();
            if (size != 1024) throw new InvalidDataException("Unexpected sky resolution");
            var cube = new Cubemap(size, TextureFormat.RGBAHalf, true) { name = "Original stg29 HDR sky" };
            for (int face = 0; face < 6; face++)
            {
                byte[] pixels = reader.ReadBytes(size * size * 8);
                if (pixels.Length != size * size * 8) throw new EndOfStreamException();
                cube.SetPixelData(pixels, 0, (CubemapFace)face);
            }
            if (reader.BaseStream.ReadByte() != -1) throw new InvalidDataException("Extra sky payload");
            cube.Apply(true, true);
            context.AddObjectToAsset("sky", cube);
            context.SetMainObject(cube);
        }
    }
}
