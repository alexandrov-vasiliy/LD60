using System;

namespace TextureLab.Runtime
{
    /// <summary>
    /// Central migration seam for portable schemas. Version 1 is the first
    /// released contract; future versions must add explicit vN -> vN+1 steps.
    /// </summary>
    public static class TextureLabSchemaMigration
    {
        public static bool TryMigrateDocument(
            TextureLabDocument document,
            out TextureLabValidationResult validation)
        {
            validation = new TextureLabValidationResult();
            if (document == null)
            {
                validation.Add("document", "The document is missing.");
                return false;
            }

            if (!TryMigrateVersion(document.schemaVersion, out int targetVersion, validation, "document"))
                return false;

            document.schemaVersion = targetVersion;
            return true;
        }

        public static bool TryMigratePreset(
            TextureLabPresetDefinition preset,
            out TextureLabValidationResult validation)
        {
            validation = new TextureLabValidationResult();
            if (preset == null)
            {
                validation.Add("preset", "The preset is missing.");
                return false;
            }

            if (!TryMigrateVersion(preset.schemaVersion, out int targetVersion, validation, "preset"))
                return false;

            preset.schemaVersion = targetVersion;
            return true;
        }

        public static bool TryMigratePalette(
            TextureLabPaletteDefinition palette,
            out TextureLabValidationResult validation)
        {
            validation = new TextureLabValidationResult();
            if (palette == null)
            {
                validation.Add("palette", "The palette is missing.");
                return false;
            }

            if (!TryMigrateVersion(palette.schemaVersion, out int targetVersion, validation, "palette"))
                return false;

            palette.schemaVersion = targetVersion;
            return true;
        }

        private static bool TryMigrateVersion(
            int sourceVersion,
            out int targetVersion,
            TextureLabValidationResult validation,
            string path)
        {
            targetVersion = sourceVersion;
            if (sourceVersion == TextureLabSchema.CurrentVersion)
                return true;

            if (sourceVersion > TextureLabSchema.CurrentVersion)
                validation.Add(path, $"Schema version {sourceVersion} is newer than supported version {TextureLabSchema.CurrentVersion}.");
            else
                validation.Add(path, $"No migration path exists from schema version {sourceVersion}.");
            return false;
        }
    }
}
