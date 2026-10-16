using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using TextureLab.Runtime;

namespace TextureLab.Editor
{
    internal enum TextureExportFormat
    {
        Png,
        Jpg
    }

    internal enum TextureExportImportSettings
    {
        Recommended,
        InheritSource
    }

    internal static class TextureExporter
    {
        internal static Texture2D Export(
            Texture2D source,
            IReadOnlyList<TextureEffectData> effects,
            string destinationPath,
            TextureExportFormat format,
            int jpgQuality,
            TextureExportImportSettings importSettings,
            FilterMode filterMode)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (string.IsNullOrEmpty(destinationPath))
                throw new ArgumentException("Export path is required.", nameof(destinationPath));

            Texture2D readback = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                using var processor = new TextureProcessor();
                processor.ProcessFullResolution(source, effects);
                RenderTexture result = processor.Result;
                readback = new Texture2D(result.width, result.height, TextureFormat.RGBA32, false, false);
                RenderTexture.active = result;
                readback.ReadPixels(new Rect(0f, 0f, result.width, result.height), 0, 0);
                readback.Apply(false, false);

                byte[] encoded = format == TextureExportFormat.Png
                    ? readback.EncodeToPNG()
                    : readback.EncodeToJPG(Mathf.Clamp(jpgQuality, 1, 100));
                WriteAtomically(destinationPath, encoded);

                string assetPath = ToAssetPath(destinationPath);
                if (assetPath == null)
                    return null;

                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                ApplyImportSettings(assetPath, source, format, importSettings, filterMode);
                var exportedAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                Selection.activeObject = exportedAsset;
                return exportedAsset;
            }
            finally
            {
                RenderTexture.active = previous;
                if (readback != null)
                    UnityEngine.Object.DestroyImmediate(readback);
            }
        }

        private static void WriteAtomically(string destinationPath, byte[] contents)
        {
            string fullPath = Path.GetFullPath(destinationPath);
            string directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                throw new DirectoryNotFoundException($"Export directory does not exist: {directory}");

            string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllBytes(temporaryPath, contents);
                if (File.Exists(fullPath))
                    File.Replace(temporaryPath, fullPath, null);
                else
                    File.Move(temporaryPath, fullPath);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        private static string ToAssetPath(string path)
        {
            string fullPath = Path.GetFullPath(path);
            string assetsPath = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string prefix = assetsPath + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return null;

            return "Assets/" + fullPath[prefix.Length..].Replace(Path.DirectorySeparatorChar, '/');
        }

        private static void ApplyImportSettings(
            string assetPath,
            Texture2D source,
            TextureExportFormat format,
            TextureExportImportSettings settings,
            FilterMode filterMode)
        {
            if (AssetImporter.GetAtPath(assetPath) is not TextureImporter importer)
                return;

            var sourceImporter = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(source)) as TextureImporter;
            bool png = format == TextureExportFormat.Png;
            if (settings == TextureExportImportSettings.InheritSource && sourceImporter != null)
            {
                importer.textureType = sourceImporter.textureType is TextureImporterType.Default or TextureImporterType.Sprite
                    ? sourceImporter.textureType
                    : TextureImporterType.Default;
                importer.sRGBTexture = sourceImporter.sRGBTexture;
                importer.wrapMode = sourceImporter.wrapMode;
                importer.filterMode = sourceImporter.filterMode;
                importer.mipmapEnabled = sourceImporter.mipmapEnabled;
                importer.alphaSource = png ? sourceImporter.alphaSource : TextureImporterAlphaSource.None;
                importer.alphaIsTransparency = png && sourceImporter.alphaIsTransparency;
            }
            else
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = sourceImporter == null || sourceImporter.sRGBTexture;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = filterMode;
                importer.mipmapEnabled = false;
                importer.alphaSource = png ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
                importer.alphaIsTransparency = png;
            }

            importer.SaveAndReimport();
        }
    }
}
