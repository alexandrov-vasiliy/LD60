using System;
using System.Collections.Generic;
using UnityEngine;

namespace TextureLab.Runtime
{
    /// <summary>
    /// A snapshot of platform information that is relevant to the current
    /// session. Detection is deliberately supplied by the host so the
    /// session model does not depend on a platform adapter.
    /// </summary>
    [Serializable]
    public sealed class TextureLabCapabilitySnapshot
    {
        [SerializeField] private string platform = "Unknown";
        [SerializeField] private string graphicsApi = string.Empty;
        [SerializeField] private int maxTextureSize;
        [SerializeField] private bool supportsAsyncReadback;
        [SerializeField] private bool supportsRHalf;
        [SerializeField] private bool supportsBrowserStorage;
        [SerializeField] private bool supportsBrowserDownload;

        public TextureLabCapabilitySnapshot()
        {
        }

        public TextureLabCapabilitySnapshot(
            string platform,
            string graphicsApi,
            int maxTextureSize,
            bool supportsAsyncReadback,
            bool supportsRHalf,
            bool supportsBrowserStorage,
            bool supportsBrowserDownload)
        {
            this.platform = string.IsNullOrEmpty(platform) ? "Unknown" : platform;
            this.graphicsApi = graphicsApi ?? string.Empty;
            this.maxTextureSize = Mathf.Max(0, maxTextureSize);
            this.supportsAsyncReadback = supportsAsyncReadback;
            this.supportsRHalf = supportsRHalf;
            this.supportsBrowserStorage = supportsBrowserStorage;
            this.supportsBrowserDownload = supportsBrowserDownload;
        }

        public string Platform => platform;
        public string GraphicsApi => graphicsApi;
        public int MaxTextureSize => maxTextureSize;
        public bool SupportsAsyncReadback => supportsAsyncReadback;
        public bool SupportsRHalf => supportsRHalf;
        public bool SupportsBrowserStorage => supportsBrowserStorage;
        public bool SupportsBrowserDownload => supportsBrowserDownload;

        internal TextureLabCapabilitySnapshot Clone()
        {
            return new TextureLabCapabilitySnapshot(
                platform,
                graphicsApi,
                maxTextureSize,
                supportsAsyncReadback,
                supportsRHalf,
                supportsBrowserStorage,
                supportsBrowserDownload);
        }
    }

    /// <summary>
    /// Runtime-owned session state. It contains no RenderTextures, Materials,
    /// undo entries, or platform-specific storage handles.
    /// </summary>
    [Serializable]
    public sealed class TextureLabSessionState
    {
        [SerializeField] private TextureLabDocument activeDocument = new();
        [SerializeField] private Texture2D sourceTexture;
        [SerializeField] private byte[] originalEncodedBytes;
        [SerializeField] private string activePresetId = string.Empty;
        [SerializeField] private string activePaletteId = string.Empty;
        [SerializeField] private TextureLabCapabilitySnapshot capabilities = new();
        [SerializeField] private bool isDirty;
        [SerializeField] private bool hasActiveDocument;

        public TextureLabDocument ActiveDocument => CloneDocument(activeDocument);
        public Texture2D SourceTexture => sourceTexture;
        public bool HasActiveDocument => hasActiveDocument;

        /// <summary>
        /// Returns a copy so callers cannot mutate the session's source buffer
        /// without going through a future controller operation.
        /// </summary>
        public byte[] OriginalEncodedBytes =>
            originalEncodedBytes == null ? null : (byte[])originalEncodedBytes.Clone();

        public string ActivePresetId => activePresetId;
        public string ActivePaletteId => activePaletteId;
        public TextureLabCapabilitySnapshot Capabilities => capabilities?.Clone() ?? new TextureLabCapabilitySnapshot();
        public bool IsDirty => isDirty;

        internal TextureLabSessionSnapshot CaptureSnapshot()
        {
            return new TextureLabSessionSnapshot(
                activeDocument,
                sourceTexture,
                originalEncodedBytes,
                activePresetId,
                activePaletteId,
                capabilities,
                isDirty,
                hasActiveDocument);
        }

        internal void RestoreSnapshot(TextureLabSessionSnapshot snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            activeDocument = CloneDocument(snapshot.Document);
            sourceTexture = snapshot.SourceTexture;
            originalEncodedBytes = snapshot.OriginalEncodedBytes == null
                ? null
                : (byte[])snapshot.OriginalEncodedBytes.Clone();
            activePresetId = snapshot.ActivePresetId ?? string.Empty;
            activePaletteId = snapshot.ActivePaletteId ?? string.Empty;
            capabilities = snapshot.Capabilities?.Clone() ?? new TextureLabCapabilitySnapshot();
            isDirty = snapshot.IsDirty;
            hasActiveDocument = snapshot.HasActiveDocument;
        }

        internal void ApplyDocumentEdits(TextureLabDocument document)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            if (activeDocument == null)
                activeDocument = new TextureLabDocument();

            activeDocument.effects = CloneEffects(document.effects);
            activeDocument.paletteDependencies = ClonePalettes(document.paletteDependencies);
            isDirty = true;
        }

        internal void ReplaceDocument(
            TextureLabDocument document,
            Texture2D texture,
            byte[] encodedBytes)
        {
            activeDocument = CloneDocument(document);
            sourceTexture = texture;
            originalEncodedBytes = encodedBytes == null ? null : (byte[])encodedBytes.Clone();
            activePresetId = string.Empty;
            activePaletteId = string.Empty;
            isDirty = false;
            hasActiveDocument = true;
        }

        internal void RestorePersistedSession(
            TextureLabDocument document,
            byte[] encodedBytes,
            string paletteId,
            string presetId,
            bool dirty)
        {
            activeDocument = CloneDocument(document);
            sourceTexture = null;
            originalEncodedBytes = encodedBytes == null ? null : (byte[])encodedBytes.Clone();
            activePaletteId = paletteId ?? string.Empty;
            activePresetId = presetId ?? string.Empty;
            isDirty = dirty;
            hasActiveDocument = true;
        }

        internal void SetActivePreset(string presetId)
        {
            activePresetId = presetId ?? string.Empty;
        }

        internal void SetActivePalette(string paletteId)
        {
            activePaletteId = paletteId ?? string.Empty;
        }

        internal void SetCapabilities(TextureLabCapabilitySnapshot snapshot)
        {
            capabilities = snapshot?.Clone() ?? new TextureLabCapabilitySnapshot();
        }

        internal void SetDirty(bool value)
        {
            isDirty = value;
        }

        internal void SetPreviewPreferences(TextureLabPreviewPreferences preferences)
        {
            if (activeDocument == null)
                activeDocument = new TextureLabDocument();
            activeDocument.previewPreferences = ClonePreviewPreferences(preferences);
        }

        internal static TextureLabDocument CloneDocument(TextureLabDocument source)
        {
            if (source == null)
                return new TextureLabDocument();

            var copy = new TextureLabDocument
            {
                schemaVersion = source.schemaVersion,
                applicationVersion = source.applicationVersion,
                documentId = source.documentId,
                displayName = source.displayName,
                sourceImage = CloneSource(source.sourceImage),
                effects = CloneEffects(source.effects),
                paletteDependencies = ClonePalettes(source.paletteDependencies),
                previewPreferences = ClonePreviewPreferences(source.previewPreferences),
                metadata = CloneMetadata(source.metadata)
            };
            return copy;
        }

        private static TextureLabSourceImage CloneSource(TextureLabSourceImage source)
        {
            if (source == null)
                return null;

            return new TextureLabSourceImage
            {
                originalFileName = source.originalFileName,
                mimeType = source.mimeType,
                width = source.width,
                height = source.height,
                embeddedEntryPath = source.embeddedEntryPath
            };
        }

        private static List<TextureLabEffectRecord> CloneEffects(IReadOnlyList<TextureLabEffectRecord> source)
        {
            var copies = new List<TextureLabEffectRecord>(source == null ? 0 : source.Count);
            if (source == null)
                return copies;

            foreach (TextureLabEffectRecord effect in source)
                copies.Add(CloneEffectRecord(effect));

            return copies;
        }

        private static List<TextureLabPaletteDefinition> ClonePalettes(IReadOnlyList<TextureLabPaletteDefinition> source)
        {
            var copies = new List<TextureLabPaletteDefinition>(source == null ? 0 : source.Count);
            if (source == null)
                return copies;

            foreach (TextureLabPaletteDefinition palette in source)
                copies.Add(ClonePaletteDefinition(palette));

            return copies;
        }

        internal static TextureLabEffectRecord CloneEffectRecord(TextureLabEffectRecord effect)
        {
            if (effect == null)
                return null;

            return new TextureLabEffectRecord
            {
                id = effect.id,
                effectType = effect.effectType,
                dataVersion = effect.dataVersion,
                enabled = effect.enabled,
                allowRandomize = effect.allowRandomize,
                parametersJson = effect.parametersJson
            };
        }

        internal static TextureLabPaletteDefinition ClonePaletteDefinition(TextureLabPaletteDefinition palette)
        {
            if (palette == null)
                return null;

            return new TextureLabPaletteDefinition
            {
                schemaVersion = palette.schemaVersion,
                id = palette.id,
                displayName = palette.displayName,
                colors = palette.colors == null
                    ? new List<Color32>()
                    : new List<Color32>(palette.colors),
                metadata = CloneMetadata(palette.metadata),
                isBuiltIn = palette.isBuiltIn
            };
        }

        internal static TextureLabPresetDefinition ClonePresetDefinition(TextureLabPresetDefinition preset)
        {
            if (preset == null)
                return null;

            var copy = new TextureLabPresetDefinition
            {
                schemaVersion = preset.schemaVersion,
                id = preset.id,
                displayName = preset.displayName,
                effects = new List<TextureLabEffectRecord>(),
                paletteDependencies = new List<TextureLabPaletteDefinition>(),
                isBuiltIn = preset.isBuiltIn
            };

            if (preset.effects != null)
            {
                foreach (TextureLabEffectRecord effect in preset.effects)
                    copy.effects.Add(CloneEffectRecord(effect));
            }

            if (preset.paletteDependencies != null)
            {
                foreach (TextureLabPaletteDefinition palette in preset.paletteDependencies)
                    copy.paletteDependencies.Add(ClonePaletteDefinition(palette));
            }

            return copy;
        }

        internal static long EstimateDocumentMemory(TextureLabDocument document)
        {
            if (document == null)
                return 256;

            long bytes = 1024;
            bytes += EstimateString(document.applicationVersion);
            bytes += EstimateString(document.documentId);
            bytes += EstimateString(document.displayName);
            if (document.sourceImage != null)
            {
                bytes += EstimateString(document.sourceImage.originalFileName);
                bytes += EstimateString(document.sourceImage.mimeType);
                bytes += EstimateString(document.sourceImage.embeddedEntryPath);
            }

            if (document.effects != null)
            {
                foreach (TextureLabEffectRecord effect in document.effects)
                    bytes += EstimateEffectMemory(effect);
            }

            if (document.paletteDependencies != null)
            {
                foreach (TextureLabPaletteDefinition palette in document.paletteDependencies)
                    bytes += EstimatePaletteMemory(palette);
            }

            bytes += EstimateMetadataMemory(document.metadata);
            return bytes;
        }

        internal static long EstimatePaletteMemory(TextureLabPaletteDefinition palette)
        {
            if (palette == null)
                return 128;

            long bytes = 256 + EstimateString(palette.id) + EstimateString(palette.displayName);
            bytes += palette.colors == null ? 0 : palette.colors.Count * 4L;
            bytes += EstimateMetadataMemory(palette.metadata);
            return bytes;
        }

        internal static long EstimatePresetMemory(TextureLabPresetDefinition preset)
        {
            if (preset == null)
                return 256;

            long bytes = 256 + EstimateString(preset.id) + EstimateString(preset.displayName);
            if (preset.effects != null)
            {
                foreach (TextureLabEffectRecord effect in preset.effects)
                    bytes += EstimateEffectMemory(effect);
            }
            if (preset.paletteDependencies != null)
            {
                foreach (TextureLabPaletteDefinition palette in preset.paletteDependencies)
                    bytes += EstimatePaletteMemory(palette);
            }
            return bytes;
        }

        private static long EstimateEffectMemory(TextureLabEffectRecord effect)
        {
            if (effect == null)
                return 128;
            return 192 + EstimateString(effect.id) + EstimateString(effect.effectType)
                + EstimateString(effect.parametersJson);
        }

        private static long EstimateMetadataMemory(IReadOnlyList<TextureLabMetadataEntry> metadata)
        {
            long bytes = 0;
            if (metadata == null)
                return bytes;
            foreach (TextureLabMetadataEntry entry in metadata)
            {
                if (entry != null)
                    bytes += 128 + EstimateString(entry.key) + EstimateString(entry.value);
            }
            return bytes;
        }

        private static long EstimateString(string value)
        {
            return string.IsNullOrEmpty(value) ? 0 : 24 + value.Length * 2L;
        }

        private static TextureLabPreviewPreferences ClonePreviewPreferences(TextureLabPreviewPreferences source)
        {
            if (source == null)
                return null;

            return new TextureLabPreviewPreferences
            {
                tiles = source.tiles,
                zoom = source.zoom,
                channel = source.channel,
                maxDimension = source.maxDimension
            };
        }

        private static List<TextureLabMetadataEntry> CloneMetadata(IReadOnlyList<TextureLabMetadataEntry> source)
        {
            var copies = new List<TextureLabMetadataEntry>(source == null ? 0 : source.Count);
            if (source == null)
                return copies;

            foreach (TextureLabMetadataEntry entry in source)
            {
                copies.Add(entry == null
                    ? null
                    : new TextureLabMetadataEntry { key = entry.key, value = entry.value });
            }

            return copies;
        }
    }

    /// <summary>
    /// Detached state used by runtime commands for atomic undo and redo.
    /// It is intentionally not serialized or exposed as portable document data.
    /// </summary>
    internal sealed class TextureLabSessionSnapshot
    {
        internal TextureLabSessionSnapshot(
            TextureLabDocument document,
            Texture2D sourceTexture,
            byte[] originalEncodedBytes,
            string activePresetId,
            string activePaletteId,
            TextureLabCapabilitySnapshot capabilities,
            bool isDirty,
            bool hasActiveDocument)
        {
            Document = TextureLabSessionState.CloneDocument(document);
            SourceTexture = sourceTexture;
            OriginalEncodedBytes = originalEncodedBytes == null
                ? null
                : (byte[])originalEncodedBytes.Clone();
            ActivePresetId = activePresetId ?? string.Empty;
            ActivePaletteId = activePaletteId ?? string.Empty;
            Capabilities = capabilities?.Clone() ?? new TextureLabCapabilitySnapshot();
            IsDirty = isDirty;
            HasActiveDocument = hasActiveDocument;
        }

        internal TextureLabDocument Document { get; }
        internal Texture2D SourceTexture { get; }
        internal byte[] OriginalEncodedBytes { get; }
        internal string ActivePresetId { get; }
        internal string ActivePaletteId { get; }
        internal TextureLabCapabilitySnapshot Capabilities { get; }
        internal bool IsDirty { get; }
        internal bool HasActiveDocument { get; }
        internal long EstimatedMemoryBytes =>
            TextureLabSessionState.EstimateDocumentMemory(Document)
            + (OriginalEncodedBytes == null ? 0 : OriginalEncodedBytes.Length)
            + 1024;
    }
}
