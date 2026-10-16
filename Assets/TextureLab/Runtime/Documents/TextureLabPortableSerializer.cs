using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace TextureLab.Runtime
{
    public static class TextureLabPortableSerializer
    {
        private const int MaxDisplayNameLength = 256;
        private const int MaxFileNameLength = 256;
        private const int MaxMimeTypeLength = 64;

        public static string SerializePalette(TextureLabPaletteDefinition palette)
        {
            TextureLabValidationResult validation = ValidatePalette(palette);
            ThrowIfInvalid(validation, "palette");
            return SerializeJson(palette, "palette");
        }

        public static bool TryDeserializePalette(
            string json,
            out TextureLabPaletteDefinition palette,
            out TextureLabValidationResult validation)
        {
            palette = null;
            validation = new TextureLabValidationResult();
            if (!TryParseJson(json, out palette, validation, "palette"))
                return false;

            if (!TextureLabSchemaMigration.TryMigratePalette(palette, out TextureLabValidationResult migrationValidation))
                validation.AddRange(migrationValidation);
            validation.AddRange(ValidatePalette(palette));
            if (!validation.IsValid)
                palette = null;
            return validation.IsValid;
        }

        public static string SerializePreset(TextureLabPresetDefinition preset)
        {
            TextureLabValidationResult validation = ValidatePreset(preset);
            ThrowIfInvalid(validation, "preset");
            return SerializeJson(preset, "preset");
        }

        public static bool TryDeserializePreset(
            string json,
            out TextureLabPresetDefinition preset,
            out TextureLabValidationResult validation)
        {
            preset = null;
            validation = new TextureLabValidationResult();
            if (!TryParseJson(json, out preset, validation, "preset"))
                return false;

            if (!TextureLabSchemaMigration.TryMigratePreset(preset, out TextureLabValidationResult migrationValidation))
                validation.AddRange(migrationValidation);
            validation.AddRange(ValidatePreset(preset));
            if (!validation.IsValid)
                preset = null;
            return validation.IsValid;
        }

        public static byte[] SerializeDocument(TextureLabDocument document, byte[] sourceBytes)
        {
            TextureLabValidationResult validation = ValidateDocument(document, sourceBytes);
            ThrowIfInvalid(validation, "document");

            if (string.IsNullOrEmpty(document.sourceImage.embeddedEntryPath))
                document.sourceImage.embeddedEntryPath = GetDefaultSourceEntry(document.sourceImage.mimeType);

            string manifest = SerializeJson(document, "document");
            var entries = new List<TextureLabZipEntry>(2)
            {
                new TextureLabZipEntry("manifest.json", Encoding.UTF8.GetBytes(manifest)),
                new TextureLabZipEntry(document.sourceImage.embeddedEntryPath, sourceBytes)
            };
            return TextureLabZipArchive.Create(entries);
        }

        public static bool TryDeserializeDocument(
            byte[] containerBytes,
            out TextureLabDocument document,
            out byte[] sourceBytes,
            out TextureLabValidationResult validation)
        {
            document = null;
            sourceBytes = null;
            validation = new TextureLabValidationResult();
            if (containerBytes == null)
            {
                validation.Add("container", "The container is missing.");
                return false;
            }

            if (!TextureLabZipArchive.TryRead(containerBytes, out List<TextureLabZipEntry> entries, out TextureLabValidationResult zipValidation))
            {
                validation.AddRange(zipValidation);
                return false;
            }

            TextureLabZipEntry manifestEntry = FindEntry(entries, "manifest.json");
            if (manifestEntry == null)
            {
                validation.Add("container", "The manifest.json entry is missing.");
                return false;
            }

            if (!TryParseJson(Encoding.UTF8.GetString(manifestEntry.Data), out document, validation, "manifest"))
                return false;

            if (!TextureLabSchemaMigration.TryMigrateDocument(document, out TextureLabValidationResult migrationValidation))
                validation.AddRange(migrationValidation);
            validation.AddRange(ValidateDocument(document, null));
            if (!validation.IsValid)
            {
                document = null;
                return false;
            }

            TextureLabZipEntry sourceEntry = FindEntry(entries, document.sourceImage.embeddedEntryPath);
            if (sourceEntry == null)
            {
                validation.Add("sourceImage.embeddedEntryPath", "The embedded source entry is missing.");
                document = null;
                return false;
            }

            sourceBytes = sourceEntry.Data;
            TextureLabValidationResult sourceValidation = ValidateDocument(document, sourceBytes);
            validation.AddRange(sourceValidation);
            if (!validation.IsValid)
            {
                document = null;
                sourceBytes = null;
                return false;
            }

            return true;
        }

        public static TextureLabValidationResult ValidateDocument(TextureLabDocument document, byte[] sourceBytes)
        {
            var validation = new TextureLabValidationResult();
            if (document == null)
            {
                validation.Add("document", "The document is missing.");
                return validation;
            }

            if (document.schemaVersion != TextureLabSchema.CurrentVersion)
                validation.Add("schemaVersion", $"Version {document.schemaVersion} is not supported.");
            ValidateId(document.documentId, "documentId", validation);
            ValidateString(document.applicationVersion, "applicationVersion", 128, validation, false);
            ValidateString(document.displayName, "displayName", MaxDisplayNameLength, validation, false);
            ValidateSource(document.sourceImage, sourceBytes, validation);
            ValidateEffects(document.effects, "effects", validation);
            ValidatePalettes(document.paletteDependencies, "paletteDependencies", validation);

            if (document.previewPreferences == null)
                validation.Add("previewPreferences", "Preview preferences are missing.");
            else
            {
                if (document.previewPreferences.tiles < 1 || document.previewPreferences.tiles > 8)
                    validation.Add("previewPreferences.tiles", "Tiles must be between 1 and 8.");
                if (document.previewPreferences.zoom != 0 && document.previewPreferences.zoom != 25
                    && document.previewPreferences.zoom != 50 && document.previewPreferences.zoom != 100
                    && document.previewPreferences.zoom != 200)
                    validation.Add("previewPreferences.zoom", "Zoom must be 0, 25, 50, 100 or 200.");
                if (document.previewPreferences.maxDimension != 512
                    && document.previewPreferences.maxDimension != 1024
                    && document.previewPreferences.maxDimension != 2048)
                    validation.Add("previewPreferences.maxDimension", "Preview max dimension is unsupported.");
            }

            if (document.metadata == null)
                validation.Add("metadata", "Metadata is missing.");
            else if (document.metadata.Count > TextureLabSchema.MaxMetadataEntries)
                validation.Add("metadata", "Too many metadata entries.");
            else
            {
                var keys = new HashSet<string>(StringComparer.Ordinal);
                for (int index = 0; index < document.metadata.Count; index++)
                {
                    TextureLabMetadataEntry entry = document.metadata[index];
                    if (entry == null)
                    {
                        validation.Add($"metadata[{index}]", "Metadata entry is missing.");
                        continue;
                    }

                    ValidateString(entry.key, $"metadata[{index}].key", TextureLabSchema.MaxMetadataStringLength, validation, true);
                    ValidateString(entry.value, $"metadata[{index}].value", TextureLabSchema.MaxMetadataStringLength, validation, false);
                    if (!string.IsNullOrEmpty(entry.key) && !keys.Add(entry.key))
                        validation.Add($"metadata[{index}].key", "Metadata keys must be unique.");
                }
            }

            return validation;
        }

        public static TextureLabValidationResult ValidatePreset(TextureLabPresetDefinition preset)
        {
            var validation = new TextureLabValidationResult();
            if (preset == null)
            {
                validation.Add("preset", "The preset is missing.");
                return validation;
            }

            if (preset.schemaVersion != TextureLabSchema.CurrentVersion)
                validation.Add("schemaVersion", $"Version {preset.schemaVersion} is not supported.");
            ValidateId(preset.id, "id", validation);
            ValidateString(preset.displayName, "displayName", MaxDisplayNameLength, validation, false);
            ValidateEffects(preset.effects, "effects", validation);
            ValidatePalettes(preset.paletteDependencies, "paletteDependencies", validation);
            return validation;
        }

        public static TextureLabValidationResult ValidatePalette(TextureLabPaletteDefinition palette)
        {
            var validation = new TextureLabValidationResult();
            if (palette == null)
            {
                validation.Add("palette", "The palette is missing.");
                return validation;
            }

            if (palette.schemaVersion != TextureLabSchema.CurrentVersion)
                validation.Add("schemaVersion", $"Version {palette.schemaVersion} is not supported.");
            ValidateId(palette.id, "id", validation);
            ValidateString(palette.displayName, "displayName", MaxDisplayNameLength, validation, false);
            ValidateColors(palette.colors, "colors", validation);
            return validation;
        }

        internal static bool TrySerializeEffects(
            IReadOnlyList<TextureEffectData> effects,
            out List<TextureLabEffectRecord> records,
            out TextureLabValidationResult validation)
        {
            records = new List<TextureLabEffectRecord>();
            validation = new TextureLabValidationResult();
            if (effects == null)
            {
                validation.Add("effects", "The effect list is missing.");
                return false;
            }

            if (effects.Count > TextureLabSchema.MaxEffects)
                validation.Add("effects", "Too many effects.");

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < effects.Count; index++)
            {
                TextureEffectData effect = effects[index];
                if (effect == null)
                {
                    validation.Add($"effects[{index}]", "Effect is missing.");
                    continue;
                }

                if (!ids.Add(effect.Id))
                    validation.Add($"effects[{index}].id", "Effect IDs must be unique.");
                if (TextureLabEffectRegistry.TrySerialize(effect, out TextureLabEffectRecord record, out TextureLabValidationResult effectValidation))
                    records.Add(record);
                else
                    AddPrefixed(validation, $"effects[{index}]", effectValidation);
            }

            if (!validation.IsValid)
                records.Clear();
            return validation.IsValid;
        }

        internal static bool TryDeserializeEffects(
            IReadOnlyList<TextureLabEffectRecord> records,
            out List<TextureEffectData> effects,
            out TextureLabValidationResult validation)
        {
            effects = new List<TextureEffectData>();
            validation = new TextureLabValidationResult();
            ValidateEffects(records, "effects", validation);
            if (!validation.IsValid)
                return false;

            for (int index = 0; index < records.Count; index++)
            {
                if (TextureLabEffectRegistry.TryDeserialize(records[index], out TextureEffectData effect, out TextureLabValidationResult effectValidation))
                    effects.Add(effect);
                else
                    AddPrefixed(validation, $"effects[{index}]", effectValidation);
            }

            if (!validation.IsValid)
                effects.Clear();
            return validation.IsValid;
        }

        internal static TextureLabPaletteDefinition ToPortablePalette(TextureLabPalette palette, string displayName)
        {
            if (palette == null)
                return null;

            var definition = new TextureLabPaletteDefinition
            {
                id = palette.Id,
                displayName = displayName ?? string.Empty,
                colors = new List<Color32>(palette.Colors.Count)
            };
            foreach (Color color in palette.Colors)
                definition.colors.Add(ToColor32(color));
            return definition;
        }

        private static string SerializeJson<T>(T value, string label)
        {
            string json = JsonUtility.ToJson(value, false);
            if (Encoding.UTF8.GetByteCount(json) > TextureLabSchema.MaxJsonBytes)
                throw new TextureLabSerializationException($"The {label} JSON exceeds the size limit.");
            return json;
        }

        private static bool TryParseJson<T>(
            string json,
            out T value,
            TextureLabValidationResult validation,
            string label) where T : class
        {
            value = null;
            if (string.IsNullOrEmpty(json))
            {
                validation.Add(label, "JSON is empty.");
                return false;
            }

            if (Encoding.UTF8.GetByteCount(json) > TextureLabSchema.MaxJsonBytes)
            {
                validation.Add(label, "JSON exceeds the size limit.");
                return false;
            }

            try
            {
                value = JsonUtility.FromJson<T>(json);
                if (value == null)
                    validation.Add(label, "JSON did not contain an object.");
            }
            catch (Exception exception)
            {
                validation.Add(label, $"Invalid JSON: {exception.Message}");
            }

            return validation.IsValid;
        }

        private static void ValidateSource(TextureLabSourceImage source, byte[] sourceBytes, TextureLabValidationResult validation)
        {
            if (source == null)
            {
                validation.Add("sourceImage", "Source image metadata is missing.");
                return;
            }

            ValidateString(source.originalFileName, "sourceImage.originalFileName", MaxFileNameLength, validation, false);
            ValidateString(source.mimeType, "sourceImage.mimeType", MaxMimeTypeLength, validation, true);
            if (source.mimeType != "image/png" && source.mimeType != "image/jpeg")
                validation.Add("sourceImage.mimeType", "Only image/png and image/jpeg are supported.");
            if (source.width < 1 || source.width > TextureLabSchema.MaxSourceWidth)
                validation.Add("sourceImage.width", "Width is outside the supported range.");
            if (source.height < 1 || source.height > TextureLabSchema.MaxSourceHeight)
                validation.Add("sourceImage.height", "Height is outside the supported range.");

            if (!string.IsNullOrEmpty(source.embeddedEntryPath) && !IsSafeSourceEntry(source.embeddedEntryPath))
                validation.Add("sourceImage.embeddedEntryPath", "Embedded source path is not safe.");

            if (sourceBytes != null)
            {
                if (sourceBytes.Length == 0)
                    validation.Add("source", "Source bytes are empty.");
                else if (sourceBytes.Length > TextureLabZipLimits.Default.MaxEntryBytes)
                    validation.Add("source", "Source bytes exceed the size limit.");
            }
        }

        private static void ValidateEffects(IReadOnlyList<TextureLabEffectRecord> effects, string path, TextureLabValidationResult validation)
        {
            if (effects == null)
            {
                validation.Add(path, "Effect records are missing.");
                return;
            }

            if (effects.Count > TextureLabSchema.MaxEffects)
                validation.Add(path, "Too many effects.");

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < effects.Count; index++)
            {
                TextureLabEffectRecord record = effects[index];
                if (record == null)
                {
                    validation.Add($"{path}[{index}]", "Effect record is missing.");
                    continue;
                }

                TextureLabEffectRegistry.ValidateRecord(record, validation);
                if (!ids.Add(record.id))
                    validation.Add($"{path}[{index}].id", "Effect IDs must be unique.");
            }
        }

        private static void ValidatePalettes(IReadOnlyList<TextureLabPaletteDefinition> palettes, string path, TextureLabValidationResult validation)
        {
            if (palettes == null)
            {
                validation.Add(path, "Palette dependencies are missing.");
                return;
            }

            if (palettes.Count > TextureLabSchema.MaxPaletteDependencies)
                validation.Add(path, "Too many palette dependencies.");

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < palettes.Count; index++)
            {
                TextureLabPaletteDefinition palette = palettes[index];
                TextureLabValidationResult paletteValidation = ValidatePalette(palette);
                AddPrefixed(validation, $"{path}[{index}]", paletteValidation);
                if (palette != null && !ids.Add(palette.id))
                    validation.Add($"{path}[{index}].id", "Palette IDs must be unique.");
            }
        }

        private static void ValidateColors(IReadOnlyList<Color32> colors, string path, TextureLabValidationResult validation)
        {
            if (colors == null)
            {
                validation.Add(path, "Colors are missing.");
                return;
            }

            if (colors.Count < 1 || colors.Count > TextureLabSchema.MaxPaletteColors)
                validation.Add(path, $"Color count must be between 1 and {TextureLabSchema.MaxPaletteColors}.");
        }

        private static void ValidateString(string value, string path, int maxLength, TextureLabValidationResult validation, bool required)
        {
            if (string.IsNullOrEmpty(value))
            {
                if (required)
                    validation.Add(path, "A value is required.");
                return;
            }

            if (value.Length > maxLength)
                validation.Add(path, "Value is too long.");
            for (int index = 0; index < value.Length; index++)
            {
                if (char.IsControl(value[index]))
                {
                    validation.Add(path, "Value contains a control character.");
                    break;
                }
            }
        }

        private static void ValidateId(string value, string path, TextureLabValidationResult validation)
        {
            if (string.IsNullOrEmpty(value))
            {
                validation.Add(path, "An ID is required.");
                return;
            }

            if (value.Length > 128)
                validation.Add(path, "ID is too long.");
            for (int index = 0; index < value.Length; index++)
            {
                if (char.IsControl(value[index]) || char.IsWhiteSpace(value[index]))
                {
                    validation.Add(path, "ID contains whitespace or a control character.");
                    break;
                }
            }
        }

        private static void AddPrefixed(TextureLabValidationResult target, string prefix, TextureLabValidationResult source)
        {
            if (source == null)
                return;
            foreach (string error in source.Errors)
                target.Add(prefix, error);
        }

        private static void ThrowIfInvalid(TextureLabValidationResult validation, string label)
        {
            if (!validation.IsValid)
                throw new TextureLabSerializationException($"Invalid {label}: {validation}");
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

        private static bool IsSafeSourceEntry(string path)
        {
            return path.StartsWith("source/", StringComparison.Ordinal)
                && path.Length <= 240
                && path.IndexOf("..", StringComparison.Ordinal) < 0
                && path.IndexOf('\\') < 0;
        }

        private static string GetDefaultSourceEntry(string mimeType)
        {
            return mimeType == "image/jpeg" ? "source/original.jpg" : "source/original.png";
        }

        private static Color32 ToColor32(Color color)
        {
            return new Color32(
                (byte)Mathf.RoundToInt(Mathf.Clamp01(color.r) * 255f),
                (byte)Mathf.RoundToInt(Mathf.Clamp01(color.g) * 255f),
                (byte)Mathf.RoundToInt(Mathf.Clamp01(color.b) * 255f),
                (byte)Mathf.RoundToInt(Mathf.Clamp01(color.a) * 255f));
        }
    }
}
