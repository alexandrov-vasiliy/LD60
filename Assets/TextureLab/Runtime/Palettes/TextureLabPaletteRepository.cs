using System;
using System.Collections.Generic;

namespace TextureLab.Runtime
{
    public enum TextureLabImportConflictPolicy
    {
        UseLocal,
        ImportAsCopy
    }

    /// <summary>
    /// Runtime-owned palette library. ScriptableObjects are only an authoring
    /// adapter; repository entries are detached DTOs and are safe to use in a
    /// Player or Web build.
    /// </summary>
    public sealed class TextureLabPaletteRepository
    {
        private readonly Dictionary<string, TextureLabPaletteDefinition> definitions =
            new(StringComparer.Ordinal);
        private readonly HashSet<string> builtInIds = new(StringComparer.Ordinal);

        public TextureLabPaletteRepository()
        {
        }

        public TextureLabPaletteRepository(TextureLabPaletteCatalog catalog)
        {
            RegisterBuiltIns(catalog);
        }

        public event Action<string> PaletteChanged;

        public int Revision { get; private set; }
        public int Count => definitions.Count;

        public static TextureLabPaletteRepository CreateWithBuiltIns(TextureLabPaletteCatalog catalog = null)
        {
            return new TextureLabPaletteRepository(catalog ?? TextureLabPaletteCatalog.LoadBuiltIn());
        }

        public IReadOnlyList<TextureLabPaletteDefinition> GetAll(bool includeBuiltIns = true)
        {
            var result = new List<TextureLabPaletteDefinition>(definitions.Count);
            foreach (KeyValuePair<string, TextureLabPaletteDefinition> pair in definitions)
            {
                if (includeBuiltIns || !builtInIds.Contains(pair.Key))
                    result.Add(Clone(pair.Value));
            }

            result.Sort((left, right) => string.Compare(left.displayName, right.displayName, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        public bool Contains(string paletteId)
        {
            return !string.IsNullOrEmpty(paletteId) && definitions.ContainsKey(paletteId);
        }

        public bool IsBuiltIn(string paletteId)
        {
            return !string.IsNullOrEmpty(paletteId) && builtInIds.Contains(paletteId);
        }

        public bool TryGet(string paletteId, out TextureLabPaletteDefinition definition)
        {
            if (!string.IsNullOrEmpty(paletteId) && definitions.TryGetValue(paletteId, out TextureLabPaletteDefinition stored))
            {
                definition = Clone(stored);
                return true;
            }

            definition = null;
            return false;
        }

        public void RegisterBuiltIns(TextureLabPaletteCatalog catalog)
        {
            if (catalog == null)
                return;

            foreach (TextureLabPaletteDefinition definition in catalog.CreateRuntimeDefinitions())
                RegisterBuiltIn(definition);
        }

        public bool RegisterBuiltIn(TextureLabPaletteDefinition definition)
        {
            if (!TryValidate(definition, out _))
                return false;

            if (definitions.TryGetValue(definition.id, out TextureLabPaletteDefinition existing))
            {
                if (!AreEqual(existing, definition))
                    return false;

                builtInIds.Add(definition.id);
                existing.isBuiltIn = true;
                return true;
            }

            TextureLabPaletteDefinition copy = Clone(definition);
            copy.isBuiltIn = true;
            definitions.Add(copy.id, copy);
            builtInIds.Add(copy.id);
            Revision++;
            PaletteChanged?.Invoke(copy.id);
            return true;
        }

        public bool TryAddUser(TextureLabPaletteDefinition definition, out TextureLabValidationResult validation)
        {
            validation = Validate(definition);
            if (!validation.IsValid)
                return false;
            if (definitions.ContainsKey(definition.id))
            {
                validation.Add("id", "A palette with this ID already exists.");
                return false;
            }

            TextureLabPaletteDefinition copy = Clone(definition);
            copy.isBuiltIn = false;
            definitions.Add(copy.id, copy);
            Revision++;
            PaletteChanged?.Invoke(copy.id);
            return true;
        }

        public bool TryUpdateUser(TextureLabPaletteDefinition definition, out TextureLabValidationResult validation)
        {
            if (definition != null && IsBuiltIn(definition.id))
            {
                validation = new TextureLabValidationResult();
                validation.Add("id", "Built-in palettes must be updated through TryUpdate.");
                return false;
            }

            return TryUpdate(definition, out validation);
        }

        public bool TryUpdate(TextureLabPaletteDefinition definition, out TextureLabValidationResult validation)
        {
            validation = Validate(definition);
            if (!validation.IsValid)
                return false;
            if (!definitions.TryGetValue(definition.id, out TextureLabPaletteDefinition existing))
            {
                validation.Add("id", "The palette does not exist.");
                return false;
            }

            TextureLabPaletteDefinition copy = Clone(definition);
            copy.isBuiltIn = builtInIds.Contains(definition.id) || existing.isBuiltIn;
            definitions[copy.id] = copy;
            Revision++;
            PaletteChanged?.Invoke(copy.id);
            return true;
        }

        public bool TryRemoveUser(string paletteId, out TextureLabValidationResult validation)
        {
            validation = new TextureLabValidationResult();
            if (!definitions.ContainsKey(paletteId))
            {
                validation.Add("id", "The palette does not exist.");
                return false;
            }
            if (IsBuiltIn(paletteId))
            {
                validation.Add("id", "Built-in palettes are read-only.");
                return false;
            }

            definitions.Remove(paletteId);
            Revision++;
            PaletteChanged?.Invoke(paletteId);
            return true;
        }

        public bool TryFork(
            string sourceId,
            string displayName,
            out TextureLabPaletteDefinition fork,
            out TextureLabValidationResult validation)
        {
            fork = null;
            validation = new TextureLabValidationResult();
            if (!TryGet(sourceId, out TextureLabPaletteDefinition source))
            {
                validation.Add("sourceId", "The source palette does not exist.");
                return false;
            }

            fork = Clone(source);
            fork.id = CreateUniqueId(source.id);
            fork.displayName = string.IsNullOrEmpty(displayName) ? source.displayName + " Copy" : displayName;
            fork.isBuiltIn = false;
            return TryAddUser(fork, out validation);
        }

        public bool TryImport(
            TextureLabPaletteDefinition incoming,
            TextureLabImportConflictPolicy conflictPolicy,
            out string resolvedId,
            out TextureLabValidationResult validation)
        {
            resolvedId = string.Empty;
            validation = Validate(incoming);
            if (!validation.IsValid)
                return false;

            if (!definitions.TryGetValue(incoming.id, out TextureLabPaletteDefinition local))
            {
                TextureLabPaletteDefinition copy = Clone(incoming);
                copy.isBuiltIn = false;
                definitions.Add(copy.id, copy);
                resolvedId = copy.id;
                Revision++;
                PaletteChanged?.Invoke(copy.id);
                return true;
            }

            if (AreEqual(local, incoming) || conflictPolicy == TextureLabImportConflictPolicy.UseLocal)
            {
                resolvedId = local.id;
                return true;
            }

            TextureLabPaletteDefinition importedCopy = Clone(incoming);
            importedCopy.id = CreateUniqueId(incoming.id);
            importedCopy.isBuiltIn = false;
            definitions.Add(importedCopy.id, importedCopy);
            resolvedId = importedCopy.id;
            Revision++;
            PaletteChanged?.Invoke(importedCopy.id);
            return true;
        }

        private static TextureLabValidationResult Validate(TextureLabPaletteDefinition definition)
        {
            return TextureLabPortableSerializer.ValidatePalette(definition);
        }

        private static bool TryValidate(TextureLabPaletteDefinition definition, out TextureLabValidationResult validation)
        {
            validation = Validate(definition);
            return validation.IsValid;
        }

        private static TextureLabPaletteDefinition Clone(TextureLabPaletteDefinition source)
        {
            if (source == null)
                return null;

            var copy = new TextureLabPaletteDefinition
            {
                schemaVersion = source.schemaVersion,
                id = source.id,
                displayName = source.displayName,
                colors = source.colors == null ? new List<UnityEngine.Color32>() : new List<UnityEngine.Color32>(source.colors),
                metadata = source.metadata == null ? new List<TextureLabMetadataEntry>() : new List<TextureLabMetadataEntry>(source.metadata),
                isBuiltIn = source.isBuiltIn
            };
            return copy;
        }

        private static bool AreEqual(TextureLabPaletteDefinition left, TextureLabPaletteDefinition right)
        {
            if (left == null || right == null || left.id != right.id || left.displayName != right.displayName)
                return false;
            if (left.colors == null || right.colors == null || left.colors.Count != right.colors.Count)
                return false;
            for (int index = 0; index < left.colors.Count; index++)
            {
                if (!left.colors[index].Equals(right.colors[index]))
                    return false;
            }

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
    }
}
