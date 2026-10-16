using System;
using System.Collections.Generic;
using UnityEngine;

namespace TextureLab.Runtime
{
    public static class TextureLabSchema
    {
        public const int CurrentVersion = 1;
        public const int EffectDataVersion = 1;
        public const int MaxEffects = 256;
        public const int MaxPaletteDependencies = 256;
        public const int MaxMetadataEntries = 64;
        public const int MaxMetadataStringLength = 512;
        public const int MaxJsonBytes = 4 * 1024 * 1024;
        public const int MaxSourceWidth = 8192;
        public const int MaxSourceHeight = 8192;
        public const int MaxWebSourceDimension = 4096;
        public const int MaxPaletteColors = 64;
        public const int MaxBrushStrokes = 1024;
        public const int MaxBrushPointsPerStroke = 8192;
        public const int MaxBrushPointsTotal = 262144;
    }

    [Serializable]
    public sealed class TextureLabDocument
    {
        public int schemaVersion = TextureLabSchema.CurrentVersion;
        public string applicationVersion = string.Empty;
        public string documentId = Guid.NewGuid().ToString("N");
        public string displayName = "Untitled Texture";
        public TextureLabSourceImage sourceImage = new();
        public List<TextureLabEffectRecord> effects = new();
        public List<TextureLabPaletteDefinition> paletteDependencies = new();
        public TextureLabPreviewPreferences previewPreferences = new();
        public List<TextureLabMetadataEntry> metadata = new();
    }

    [Serializable]
    public sealed class TextureLabSourceImage
    {
        public string originalFileName = string.Empty;
        public string mimeType = string.Empty;
        public int width;
        public int height;
        public string embeddedEntryPath = string.Empty;
    }

    [Serializable]
    public sealed class TextureLabPreviewPreferences
    {
        public int tiles = 1;
        public int zoom;
        public PreviewChannel channel;
        public int maxDimension = 1024;
    }

    [Serializable]
    public sealed class TextureLabMetadataEntry
    {
        public string key = string.Empty;
        public string value = string.Empty;
    }

    [Serializable]
    public sealed class TextureLabEffectRecord
    {
        public string id = string.Empty;
        public string effectType = string.Empty;
        public int dataVersion = TextureLabSchema.EffectDataVersion;
        public bool enabled = true;
        public bool allowRandomize = true;
        public string parametersJson = string.Empty;
    }

    [Serializable]
    public sealed class TextureLabPaletteDefinition
    {
        public int schemaVersion = TextureLabSchema.CurrentVersion;
        public string id = string.Empty;
        public string displayName = string.Empty;
        public List<Color32> colors = new();
        public List<TextureLabMetadataEntry> metadata = new();

        [NonSerialized] public bool isBuiltIn;
    }

    [Serializable]
    public sealed class TextureLabPresetDefinition
    {
        public int schemaVersion = TextureLabSchema.CurrentVersion;
        public string id = string.Empty;
        public string displayName = string.Empty;
        public List<TextureLabEffectRecord> effects = new();
        public List<TextureLabPaletteDefinition> paletteDependencies = new();

        [NonSerialized] public bool isBuiltIn;
    }

    public sealed class TextureLabValidationResult
    {
        private readonly List<string> errors = new();

        public bool IsValid => errors.Count == 0;
        public IReadOnlyList<string> Errors => errors;

        public void Add(string path, string message)
        {
            if (errors.Count < 128)
                errors.Add(string.IsNullOrEmpty(path) ? message : $"{path}: {message}");
        }

        public void Add(string message) => Add(string.Empty, message);

        internal void AddRange(TextureLabValidationResult other)
        {
            if (other == null)
                return;

            foreach (string error in other.errors)
                Add(error);
        }

        public override string ToString() => string.Join("; ", errors);
    }

    public sealed class TextureLabSerializationException : Exception
    {
        public TextureLabSerializationException(string message) : base(message) { }
    }
}
