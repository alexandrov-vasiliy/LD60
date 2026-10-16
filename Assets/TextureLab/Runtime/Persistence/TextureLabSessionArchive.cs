using System;
using System.Collections.Generic;
using System.Text;

namespace TextureLab.Runtime
{
    /// <summary>
    /// The last-session container is intentionally separate from the portable
    /// document format. It stores transient selections and dirty state while
    /// keeping undo history and platform handles out of persisted data.
    /// </summary>
    public static class TextureLabSessionArchive
    {
        public const int CurrentVersion = 1;
        public const string ManifestEntry = "manifest.json";
        public const string LastSessionKey = "TextureLab/Session/last-session.texturelab";

        public static byte[] Serialize(
            TextureLabDocument document,
            byte[] sourceBytes,
            string activePaletteId,
            string activePresetId,
            bool isDirty)
        {
            TextureLabValidationResult validation = TextureLabPortableSerializer.ValidateDocument(document, sourceBytes);
            if (!validation.IsValid)
                throw new TextureLabSerializationException($"Invalid last session: {validation}");

            TextureLabDocument copy = TextureLabSessionState.CloneDocument(document);
            if (string.IsNullOrEmpty(copy.sourceImage.embeddedEntryPath))
            {
                copy.sourceImage.embeddedEntryPath = copy.sourceImage.mimeType == "image/png"
                    ? "source/image.png"
                    : "source/image.jpg";
            }

            var manifest = new TextureLabSessionArchiveManifest
            {
                schemaVersion = CurrentVersion,
                document = copy,
                activePaletteId = activePaletteId ?? string.Empty,
                activePresetId = activePresetId ?? string.Empty,
                isDirty = isDirty
            };

            string manifestJson = UnityEngine.JsonUtility.ToJson(manifest, false);
            if (Encoding.UTF8.GetByteCount(manifestJson) > TextureLabSchema.MaxJsonBytes)
                throw new TextureLabSerializationException("The last-session manifest exceeds the size limit.");

            var entries = new List<TextureLabZipEntry>(2)
            {
                new TextureLabZipEntry(ManifestEntry, Encoding.UTF8.GetBytes(manifestJson)),
                new TextureLabZipEntry(copy.sourceImage.embeddedEntryPath, sourceBytes)
            };
            return TextureLabZipArchive.Create(entries);
        }

        public static bool TryDeserialize(
            byte[] archive,
            out TextureLabDocument document,
            out byte[] sourceBytes,
            out string activePaletteId,
            out string activePresetId,
            out bool isDirty,
            out TextureLabValidationResult validation)
        {
            document = null;
            sourceBytes = null;
            activePaletteId = string.Empty;
            activePresetId = string.Empty;
            isDirty = false;
            validation = new TextureLabValidationResult();

            if (!TextureLabZipArchive.TryRead(archive, out List<TextureLabZipEntry> entries, out TextureLabValidationResult zipValidation))
            {
                validation.AddRange(zipValidation);
                return false;
            }

            TextureLabZipEntry manifestEntry = FindEntry(entries, ManifestEntry);
            if (manifestEntry == null)
            {
                validation.Add("manifest", "The last-session manifest is missing.");
                return false;
            }

            TextureLabSessionArchiveManifest manifest;
            try
            {
                manifest = UnityEngine.JsonUtility.FromJson<TextureLabSessionArchiveManifest>(
                    Encoding.UTF8.GetString(manifestEntry.Data));
            }
            catch (Exception exception)
            {
                validation.Add("manifest", $"The last-session manifest is invalid: {exception.Message}");
                return false;
            }

            if (manifest == null)
            {
                validation.Add("manifest", "The last-session manifest is empty.");
                return false;
            }

            if (manifest.schemaVersion != CurrentVersion)
                validation.Add("manifest.schemaVersion", "The last-session version is unsupported.");
            if (manifest.document == null)
                validation.Add("manifest.document", "The last-session document is missing.");
            else
                validation.AddRange(TextureLabPortableSerializer.ValidateDocument(manifest.document, null));

            if (!validation.IsValid)
                return false;

            TextureLabZipEntry sourceEntry = FindEntry(entries, manifest.document.sourceImage.embeddedEntryPath);
            if (sourceEntry == null)
            {
                validation.Add("source", "The last-session source entry is missing.");
                return false;
            }

            TextureLabValidationResult sourceValidation = TextureLabPortableSerializer.ValidateDocument(
                manifest.document,
                sourceEntry.Data);
            validation.AddRange(sourceValidation);
            if (!validation.IsValid)
                return false;

            document = TextureLabSessionState.CloneDocument(manifest.document);
            sourceBytes = sourceEntry.Data == null ? null : (byte[])sourceEntry.Data.Clone();
            activePaletteId = manifest.activePaletteId ?? string.Empty;
            activePresetId = manifest.activePresetId ?? string.Empty;
            isDirty = manifest.isDirty;
            return true;
        }

        private static TextureLabZipEntry FindEntry(IReadOnlyList<TextureLabZipEntry> entries, string name)
        {
            if (entries == null || string.IsNullOrEmpty(name))
                return null;

            foreach (TextureLabZipEntry entry in entries)
            {
                if (string.Equals(entry.Name, name, StringComparison.Ordinal))
                    return entry;
            }

            return null;
        }

        [Serializable]
        private sealed class TextureLabSessionArchiveManifest
        {
            public int schemaVersion;
            public TextureLabDocument document;
            public string activePaletteId;
            public string activePresetId;
            public bool isDirty;
        }
    }
}
