using System;
using System.Collections.Generic;

namespace TextureLab.Runtime
{
    /// <summary>
    /// Persistence facade for user palettes and presets. Built-ins stay in
    /// their authored catalog and are never written to local user storage.
    /// </summary>
    public sealed class TextureLabUserDataRepository
    {
        private const string PalettePrefix = "TextureLab/Palettes";
        private const string PresetPrefix = "TextureLab/Presets";

        private readonly TextureLabPaletteRepository palettes;
        private readonly TextureLabPresetRepository presets;
        private readonly ITextureLabLocalDataStore store;
        private bool loading;

        public TextureLabUserDataRepository(
            TextureLabPaletteRepository palettes,
            TextureLabPresetRepository presets,
            ITextureLabLocalDataStore store)
        {
            this.palettes = palettes ?? throw new ArgumentNullException(nameof(palettes));
            this.presets = presets ?? throw new ArgumentNullException(nameof(presets));
            this.store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public TextureLabPaletteRepository Palettes => palettes;
        public TextureLabPresetRepository Presets => presets;
        public ITextureLabLocalDataStore Store => store;

        public bool TryLoad(out TextureLabValidationResult validation)
        {
            validation = new TextureLabValidationResult();
            loading = true;
            try
            {
                LoadPalettes(validation);
                LoadPresets(validation);
            }
            finally
            {
                loading = false;
            }

            return validation.IsValid;
        }

        public bool TrySavePalette(TextureLabPaletteDefinition definition, out TextureLabValidationResult validation)
        {
            validation = TextureLabPortableSerializer.ValidatePalette(definition);
            if (!validation.IsValid)
                return false;
            if (definition.isBuiltIn || palettes.IsBuiltIn(definition.id))
            {
                validation.Add("id", "Built-in palettes are not stored as user data.");
                return false;
            }

            string json = TextureLabPortableSerializer.SerializePalette(definition);
            return TryWrite(GetPaletteKey(definition.id), json, "palette", validation);
        }

        public bool TryDeletePalette(string paletteId, out TextureLabValidationResult validation)
        {
            validation = new TextureLabValidationResult();
            if (string.IsNullOrEmpty(paletteId) || palettes.IsBuiltIn(paletteId))
                return true;

            return TryDelete(GetPaletteKey(paletteId), "palette", validation);
        }

        public bool TrySavePreset(TextureLabPresetDefinition definition, out TextureLabValidationResult validation)
        {
            validation = TextureLabPortableSerializer.ValidatePreset(definition);
            if (!validation.IsValid)
                return false;
            if (definition.isBuiltIn || presets.IsBuiltIn(definition.id))
            {
                validation.Add("id", "Built-in presets are not stored as user data.");
                return false;
            }

            string json = TextureLabPortableSerializer.SerializePreset(definition);
            return TryWrite(GetPresetKey(definition.id), json, "preset", validation);
        }

        public bool TryDeletePreset(string presetId, out TextureLabValidationResult validation)
        {
            validation = new TextureLabValidationResult();
            if (string.IsNullOrEmpty(presetId) || presets.IsBuiltIn(presetId))
                return true;

            return TryDelete(GetPresetKey(presetId), "preset", validation);
        }

        internal bool IsLoading => loading;

        private void LoadPalettes(TextureLabValidationResult validation)
        {
            if (!store.TryList(PalettePrefix, out IReadOnlyList<string> keys, out TextureLabStorageResult listResult))
            {
                AddStorageError(validation, "palettes", listResult);
                return;
            }

            foreach (string key in keys)
            {
                if (!key.EndsWith(".tlpalette", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!store.TryRead(key, out byte[] bytes, out TextureLabStorageResult readResult))
                {
                    AddStorageError(validation, key, readResult);
                    continue;
                }

                string json = System.Text.Encoding.UTF8.GetString(bytes);
                if (!TextureLabPortableSerializer.TryDeserializePalette(
                        json,
                        out TextureLabPaletteDefinition definition,
                        out TextureLabValidationResult parseValidation))
                {
                    AddValidationError(validation, key, parseValidation);
                    continue;
                }

                if (!palettes.TryImport(
                        definition,
                        TextureLabImportConflictPolicy.ImportAsCopy,
                        out _,
                        out TextureLabValidationResult importValidation))
                {
                    AddValidationError(validation, key, importValidation);
                }
            }
        }

        private void LoadPresets(TextureLabValidationResult validation)
        {
            if (!store.TryList(PresetPrefix, out IReadOnlyList<string> keys, out TextureLabStorageResult listResult))
            {
                AddStorageError(validation, "presets", listResult);
                return;
            }

            foreach (string key in keys)
            {
                if (!key.EndsWith(".tlpreset", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!store.TryRead(key, out byte[] bytes, out TextureLabStorageResult readResult))
                {
                    AddStorageError(validation, key, readResult);
                    continue;
                }

                string json = System.Text.Encoding.UTF8.GetString(bytes);
                if (!TextureLabPortableSerializer.TryDeserializePreset(
                        json,
                        out TextureLabPresetDefinition definition,
                        out TextureLabValidationResult parseValidation))
                {
                    AddValidationError(validation, key, parseValidation);
                    continue;
                }

                if (!presets.TryImport(
                        definition,
                        palettes,
                        TextureLabImportConflictPolicy.ImportAsCopy,
                        out _,
                        out TextureLabValidationResult importValidation))
                {
                    AddValidationError(validation, key, importValidation);
                }
            }
        }

        private bool TryWrite(
            string key,
            string text,
            string label,
            TextureLabValidationResult validation)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text ?? string.Empty);
            if (store.TryWriteAtomic(key, bytes, out TextureLabStorageResult result))
                return true;

            validation.Add(label, $"Could not save local data ({result.Code}): {result.Message}");
            return false;
        }

        private bool TryDelete(string key, string label, TextureLabValidationResult validation)
        {
            if (store.TryDelete(key, out TextureLabStorageResult result))
                return true;

            validation.Add(label, $"Could not delete local data ({result.Code}): {result.Message}");
            return false;
        }

        private static string GetPaletteKey(string id)
        {
            return PalettePrefix + "/" + TextureLabStableId.FromText(id) + ".tlpalette";
        }

        private static string GetPresetKey(string id)
        {
            return PresetPrefix + "/" + TextureLabStableId.FromText(id) + ".tlpreset";
        }

        private static void AddStorageError(
            TextureLabValidationResult validation,
            string path,
            TextureLabStorageResult result)
        {
            if (result.NotFound)
                return;
            validation.Add(path, $"Local storage failed ({result.Code}): {result.Message}");
        }

        private static void AddValidationError(
            TextureLabValidationResult target,
            string path,
            TextureLabValidationResult source)
        {
            if (source == null)
                return;
            foreach (string error in source.Errors)
                target.Add(path, error);
        }
    }
}
