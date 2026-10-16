using System;
using System.Collections.Generic;
using UnityEngine;

namespace TextureLab.Runtime
{
    /// <summary>
    /// Embeds palette definitions in portable documents and resolves imports
    /// without silently changing a palette that already exists locally.
    /// </summary>
    public static class TextureLabPaletteDependencyResolver
    {
        public static bool TryEmbedPresetDependencies(
            TextureLabPresetDefinition source,
            TextureLabPaletteRepository repository,
            out TextureLabPresetDefinition embedded,
            out TextureLabValidationResult validation)
        {
            embedded = ClonePreset(source);
            validation = TextureLabPortableSerializer.ValidatePreset(embedded);
            if (!validation.IsValid)
            {
                embedded = null;
                return false;
            }

            var dependenciesById = new Dictionary<string, TextureLabPaletteDefinition>(StringComparer.Ordinal);
            foreach (TextureLabPaletteDefinition dependency in embedded.paletteDependencies)
                dependenciesById[dependency.id] = dependency;

            foreach (TextureLabEffectRecord effect in embedded.effects)
            {
                if (!TryGetPaletteId(effect, out string paletteId, out string error))
                {
                    if (!string.IsNullOrEmpty(error))
                        validation.Add("effects", error);
                    continue;
                }

                if (dependenciesById.ContainsKey(paletteId))
                    continue;
                if (repository == null || !repository.TryGet(paletteId, out TextureLabPaletteDefinition local))
                {
                    validation.Add("paletteDependencies", $"Palette '{paletteId}' is not available for embedding.");
                    continue;
                }

                embedded.paletteDependencies.Add(local);
                dependenciesById.Add(paletteId, local);
            }

            validation.AddRange(TextureLabPortableSerializer.ValidatePreset(embedded));
            if (!validation.IsValid)
                embedded = null;
            return validation.IsValid;
        }

        public static bool TryImportPreset(
            TextureLabPresetDefinition source,
            TextureLabPaletteRepository repository,
            TextureLabImportConflictPolicy conflictPolicy,
            out TextureLabPresetDefinition resolved,
            out TextureLabValidationResult validation)
        {
            resolved = ClonePreset(source);
            validation = TextureLabPortableSerializer.ValidatePreset(resolved);
            if (!validation.IsValid)
            {
                resolved = null;
                return false;
            }
            if (repository == null)
            {
                validation.Add("repository", "A palette repository is required to import dependencies.");
                resolved = null;
                return false;
            }

            var remap = new Dictionary<string, string>(StringComparer.Ordinal);
            var importedDependencies = new List<TextureLabPaletteDefinition>(resolved.paletteDependencies.Count);
            foreach (TextureLabPaletteDefinition dependency in resolved.paletteDependencies)
            {
                if (!repository.TryImport(dependency, conflictPolicy, out string resolvedId, out TextureLabValidationResult dependencyValidation))
                {
                    AddPrefixed(validation, $"paletteDependencies[{dependency.id}]", dependencyValidation);
                    continue;
                }

                remap[dependency.id] = resolvedId;
                repository.TryGet(resolvedId, out TextureLabPaletteDefinition imported);
                importedDependencies.Add(imported);
            }

            for (int index = 0; index < resolved.effects.Count; index++)
            {
                TextureLabEffectRecord effect = resolved.effects[index];
                if (!TryGetPaletteId(effect, out string paletteId, out string error))
                {
                    if (!string.IsNullOrEmpty(error))
                        validation.Add($"effects[{index}]", error);
                    continue;
                }

                if (!remap.TryGetValue(paletteId, out string resolvedId))
                {
                    if (!repository.Contains(paletteId))
                    {
                        validation.Add($"effects[{index}].parameters.paletteId", $"Palette '{paletteId}' is not available locally or in dependencies.");
                        continue;
                    }

                    resolvedId = paletteId;
                    repository.TryGet(resolvedId, out TextureLabPaletteDefinition local);
                    importedDependencies.Add(local);
                    remap[paletteId] = resolvedId;
                }

                if (!string.Equals(paletteId, resolvedId, StringComparison.Ordinal))
                    effect.parametersJson = ReplacePaletteId(effect.parametersJson, resolvedId);
            }

            resolved.paletteDependencies = Deduplicate(importedDependencies);
            validation.AddRange(TextureLabPortableSerializer.ValidatePreset(resolved));
            if (!validation.IsValid)
                resolved = null;
            return validation.IsValid;
        }

        private static bool TryGetPaletteId(TextureLabEffectRecord effect, out string paletteId, out string error)
        {
            paletteId = string.Empty;
            error = string.Empty;
            if (effect == null || effect.effectType != TextureLabEffectRegistry.PaletteQuantize)
                return false;

            if (string.IsNullOrEmpty(effect.parametersJson))
            {
                error = "Palette quantization parameters are missing.";
                return false;
            }

            try
            {
                PaletteQuantizeParameters parameters = JsonUtility.FromJson<PaletteQuantizeParameters>(effect.parametersJson);
                paletteId = parameters != null ? parameters.paletteId : string.Empty;
            }
            catch (Exception exception)
            {
                error = $"Palette quantization parameters are invalid: {exception.Message}";
                return false;
            }

            if (string.IsNullOrEmpty(paletteId))
                error = "Palette quantization requires a paletteId.";
            return string.IsNullOrEmpty(error);
        }

        private static string ReplacePaletteId(string json, string paletteId)
        {
            PaletteQuantizeParameters parameters = JsonUtility.FromJson<PaletteQuantizeParameters>(json);
            parameters.paletteId = paletteId;
            return JsonUtility.ToJson(parameters, false);
        }

        private static TextureLabPresetDefinition ClonePreset(TextureLabPresetDefinition source)
        {
            if (source == null)
                return null;

            var clone = new TextureLabPresetDefinition
            {
                schemaVersion = source.schemaVersion,
                id = source.id,
                displayName = source.displayName,
                effects = new List<TextureLabEffectRecord>(),
                paletteDependencies = new List<TextureLabPaletteDefinition>(),
                isBuiltIn = source.isBuiltIn
            };

            if (source.effects != null)
            {
                foreach (TextureLabEffectRecord effect in source.effects)
                {
                    if (effect == null)
                        clone.effects.Add(null);
                    else
                    {
                        clone.effects.Add(new TextureLabEffectRecord
                        {
                            id = effect.id,
                            effectType = effect.effectType,
                            dataVersion = effect.dataVersion,
                            enabled = effect.enabled,
                            allowRandomize = effect.allowRandomize,
                            parametersJson = effect.parametersJson
                        });
                    }
                }
            }

            if (source.paletteDependencies != null)
            {
                foreach (TextureLabPaletteDefinition palette in source.paletteDependencies)
                {
                    if (palette == null)
                    {
                        clone.paletteDependencies.Add(null);
                        continue;
                    }

                    clone.paletteDependencies.Add(new TextureLabPaletteDefinition
                    {
                        schemaVersion = palette.schemaVersion,
                        id = palette.id,
                        displayName = palette.displayName,
                        colors = palette.colors == null ? new List<Color32>() : new List<Color32>(palette.colors),
                        metadata = palette.metadata == null ? new List<TextureLabMetadataEntry>() : new List<TextureLabMetadataEntry>(palette.metadata),
                        isBuiltIn = palette.isBuiltIn
                    });
                }
            }

            return clone;
        }

        private static List<TextureLabPaletteDefinition> Deduplicate(IEnumerable<TextureLabPaletteDefinition> source)
        {
            var result = new List<TextureLabPaletteDefinition>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (TextureLabPaletteDefinition palette in source)
            {
                if (palette != null && ids.Add(palette.id))
                    result.Add(palette);
            }

            return result;
        }

        private static void AddPrefixed(TextureLabValidationResult target, string prefix, TextureLabValidationResult source)
        {
            if (source == null)
                return;
            foreach (string error in source.Errors)
                target.Add(prefix, error);
        }

        [Serializable]
        private sealed class PaletteQuantizeParameters
        {
            public int version;
            public string paletteId = string.Empty;
            public int colorLimit;
            public int extractionColorCount;
        }
    }
}
