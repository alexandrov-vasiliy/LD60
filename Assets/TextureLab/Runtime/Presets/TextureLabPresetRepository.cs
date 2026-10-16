using System;
using System.Collections.Generic;

namespace TextureLab.Runtime
{
    /// <summary>
    /// Runtime preset library with the same read-only built-in and explicit
    /// conflict policy as the palette repository.
    /// </summary>
    public sealed class TextureLabPresetRepository
    {
        private readonly Dictionary<string, TextureLabPresetDefinition> definitions =
            new(StringComparer.Ordinal);
        private readonly HashSet<string> builtInIds = new(StringComparer.Ordinal);

        public TextureLabPresetRepository()
        {
        }

        public TextureLabPresetRepository(TextureLabPresetCatalog catalog)
        {
            RegisterBuiltIns(catalog);
        }

        public event Action<string> PresetChanged;

        public int Revision { get; private set; }
        public int Count => definitions.Count;

        public static TextureLabPresetRepository CreateWithBuiltIns(TextureLabPresetCatalog catalog = null)
        {
            return new TextureLabPresetRepository(catalog ?? TextureLabPresetCatalog.LoadBuiltIn());
        }

        public IReadOnlyList<TextureLabPresetDefinition> GetAll(bool includeBuiltIns = true)
        {
            var result = new List<TextureLabPresetDefinition>(definitions.Count);
            foreach (KeyValuePair<string, TextureLabPresetDefinition> pair in definitions)
            {
                if (includeBuiltIns || !builtInIds.Contains(pair.Key))
                    result.Add(Clone(pair.Value));
            }

            result.Sort((left, right) => string.Compare(left.displayName, right.displayName, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        public bool Contains(string presetId)
        {
            return !string.IsNullOrEmpty(presetId) && definitions.ContainsKey(presetId);
        }

        public bool IsBuiltIn(string presetId)
        {
            return !string.IsNullOrEmpty(presetId) && builtInIds.Contains(presetId);
        }

        public bool TryGet(string presetId, out TextureLabPresetDefinition definition)
        {
            if (!string.IsNullOrEmpty(presetId) && definitions.TryGetValue(presetId, out TextureLabPresetDefinition stored))
            {
                definition = Clone(stored);
                return true;
            }

            definition = null;
            return false;
        }

        public void RegisterBuiltIns(TextureLabPresetCatalog catalog)
        {
            if (catalog == null)
                return;
            foreach (TextureLabPresetDefinition definition in catalog.CreateRuntimeDefinitions())
                RegisterBuiltIn(definition);
        }

        public bool RegisterBuiltIn(TextureLabPresetDefinition definition)
        {
            if (!TextureLabPortableSerializer.ValidatePreset(definition).IsValid)
                return false;

            if (definitions.TryGetValue(definition.id, out TextureLabPresetDefinition existing))
            {
                if (!AreEqual(existing, definition))
                    return false;
                builtInIds.Add(definition.id);
                existing.isBuiltIn = true;
                return true;
            }

            TextureLabPresetDefinition copy = Clone(definition);
            copy.isBuiltIn = true;
            definitions.Add(copy.id, copy);
            builtInIds.Add(copy.id);
            Revision++;
            PresetChanged?.Invoke(copy.id);
            return true;
        }

        public bool TryAddUser(TextureLabPresetDefinition definition, out TextureLabValidationResult validation)
        {
            validation = TextureLabPortableSerializer.ValidatePreset(definition);
            if (!validation.IsValid)
                return false;
            if (definitions.ContainsKey(definition.id))
            {
                validation.Add("id", "A preset with this ID already exists.");
                return false;
            }

            TextureLabPresetDefinition copy = Clone(definition);
            copy.isBuiltIn = false;
            definitions.Add(copy.id, copy);
            Revision++;
            PresetChanged?.Invoke(copy.id);
            return true;
        }

        public bool TryUpdateUser(TextureLabPresetDefinition definition, out TextureLabValidationResult validation)
        {
            validation = TextureLabPortableSerializer.ValidatePreset(definition);
            if (!validation.IsValid)
                return false;
            if (!definitions.ContainsKey(definition.id))
            {
                validation.Add("id", "The preset does not exist.");
                return false;
            }
            if (IsBuiltIn(definition.id))
            {
                validation.Add("id", "Built-in presets are read-only. Fork the preset before editing it.");
                return false;
            }

            TextureLabPresetDefinition copy = Clone(definition);
            copy.isBuiltIn = false;
            definitions[copy.id] = copy;
            Revision++;
            PresetChanged?.Invoke(copy.id);
            return true;
        }

        public bool TryRemoveUser(string presetId, out TextureLabValidationResult validation)
        {
            validation = new TextureLabValidationResult();
            if (!definitions.ContainsKey(presetId))
            {
                validation.Add("id", "The preset does not exist.");
                return false;
            }
            if (IsBuiltIn(presetId))
            {
                validation.Add("id", "Built-in presets are read-only.");
                return false;
            }

            definitions.Remove(presetId);
            Revision++;
            PresetChanged?.Invoke(presetId);
            return true;
        }

        public bool TryFork(
            string sourceId,
            string displayName,
            out TextureLabPresetDefinition fork,
            out TextureLabValidationResult validation)
        {
            fork = null;
            validation = new TextureLabValidationResult();
            if (!TryGet(sourceId, out TextureLabPresetDefinition source))
            {
                validation.Add("sourceId", "The source preset does not exist.");
                return false;
            }

            fork = Clone(source);
            fork.id = CreateUniqueId(source.id);
            fork.displayName = string.IsNullOrEmpty(displayName) ? source.displayName + " Copy" : displayName;
            fork.isBuiltIn = false;
            return TryAddUser(fork, out validation);
        }

        public bool TryImport(
            TextureLabPresetDefinition incoming,
            TextureLabPaletteRepository paletteRepository,
            TextureLabImportConflictPolicy conflictPolicy,
            out string resolvedId,
            out TextureLabValidationResult validation)
        {
            resolvedId = string.Empty;
            validation = new TextureLabValidationResult();
            if (!TextureLabPaletteDependencyResolver.TryImportPreset(
                    incoming,
                    paletteRepository,
                    conflictPolicy,
                    out TextureLabPresetDefinition resolved,
                    out TextureLabValidationResult dependencyValidation))
            {
                validation.AddRange(dependencyValidation);
                return false;
            }

            if (!definitions.TryGetValue(resolved.id, out TextureLabPresetDefinition local))
            {
                resolved.isBuiltIn = false;
                definitions.Add(resolved.id, resolved);
                resolvedId = resolved.id;
                Revision++;
                PresetChanged?.Invoke(resolved.id);
                return true;
            }

            if (AreEqual(local, resolved) || conflictPolicy == TextureLabImportConflictPolicy.UseLocal)
            {
                resolvedId = local.id;
                return true;
            }

            resolved.id = CreateUniqueId(resolved.id);
            resolved.isBuiltIn = false;
            definitions.Add(resolved.id, resolved);
            resolvedId = resolved.id;
            Revision++;
            PresetChanged?.Invoke(resolved.id);
            return true;
        }

        private string CreateUniqueId(string sourceId)
        {
            string candidate;
            do
            {
                candidate = TextureLabStableId.FromText(sourceId + ":fork:" + Guid.NewGuid().ToString("N"));
            }
            while (definitions.ContainsKey(candidate));
            return candidate;
        }

        private static bool AreEqual(TextureLabPresetDefinition left, TextureLabPresetDefinition right)
        {
            if (left == null || right == null)
                return false;
            try
            {
                return string.Equals(
                    TextureLabPortableSerializer.SerializePreset(left),
                    TextureLabPortableSerializer.SerializePreset(right),
                    StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        private static TextureLabPresetDefinition Clone(TextureLabPresetDefinition source)
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
                    clone.effects.Add(effect == null ? null : new TextureLabEffectRecord
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

            if (source.paletteDependencies != null)
            {
                foreach (TextureLabPaletteDefinition palette in source.paletteDependencies)
                {
                    clone.paletteDependencies.Add(palette == null ? null : new TextureLabPaletteDefinition
                    {
                        schemaVersion = palette.schemaVersion,
                        id = palette.id,
                        displayName = palette.displayName,
                        colors = palette.colors == null ? new List<UnityEngine.Color32>() : new List<UnityEngine.Color32>(palette.colors),
                        metadata = palette.metadata == null ? new List<TextureLabMetadataEntry>() : new List<TextureLabMetadataEntry>(palette.metadata),
                        isBuiltIn = palette.isBuiltIn
                    });
                }
            }

            return clone;
        }
    }
}
