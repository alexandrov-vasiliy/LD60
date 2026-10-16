using System;
using System.Collections.Generic;
using UnityEngine;

namespace TextureLab.Runtime
{
    [Serializable]
    [CreateAssetMenu(fileName = "Texture Lab Preset", menuName = "Texture Lab/Preset")]
    public sealed class TextureLabPreset : ScriptableObject
    {
        [SerializeField] private int dataVersion = 1;
        [SerializeField] private string id = string.Empty;
        [SerializeField] private string displayName = string.Empty;
        [SerializeReference] private List<TextureEffectData> effects = new();
        [SerializeField] private List<TextureLabPalette> paletteDependencies = new();

        public int DataVersion => dataVersion;
        public string Id
        {
            get
            {
                if (string.IsNullOrEmpty(id))
                    id = TextureLabStableId.FromText("preset:" + name);
                return id;
            }
        }

        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
        internal IReadOnlyList<TextureEffectData> Effects => effects ??= new List<TextureEffectData>();
        internal IReadOnlyList<TextureLabPalette> PaletteDependencies => paletteDependencies ??= new List<TextureLabPalette>();

        internal void SetEffects(IEnumerable<TextureEffectData> source)
        {
            effects = CopyEffects(source);
        }

        internal void SetPaletteDependencies(IEnumerable<TextureLabPalette> source)
        {
            paletteDependencies = source == null
                ? new List<TextureLabPalette>()
                : new List<TextureLabPalette>(source);
        }

        internal List<TextureEffectData> CreateEffectCopies() => CopyEffects(Effects);

        public bool TryToRuntimeDefinition(
            out TextureLabPresetDefinition definition,
            out TextureLabValidationResult validation)
        {
            definition = null;
            validation = new TextureLabValidationResult();
            if (!TextureLabPortableSerializer.TrySerializeEffects(Effects, out List<TextureLabEffectRecord> records, out TextureLabValidationResult effectValidation))
            {
                validation.AddRange(effectValidation);
                return false;
            }

            var dependencies = new List<TextureLabPaletteDefinition>(PaletteDependencies.Count);
            foreach (TextureLabPalette palette in PaletteDependencies)
            {
                if (palette != null)
                    dependencies.Add(palette.ToRuntimeDefinition());
            }

            definition = new TextureLabPresetDefinition
            {
                schemaVersion = TextureLabSchema.CurrentVersion,
                id = Id,
                displayName = DisplayName,
                effects = records,
                paletteDependencies = dependencies
            };
            validation.AddRange(TextureLabPortableSerializer.ValidatePreset(definition));
            if (!validation.IsValid)
                definition = null;
            return validation.IsValid;
        }

        public bool ApplyRuntimeDefinition(
            TextureLabPresetDefinition definition,
            out TextureLabValidationResult validation)
        {
            validation = new TextureLabValidationResult();
            if (definition == null)
            {
                validation.Add("preset", "The preset is missing.");
                return false;
            }

            if (!TextureLabPortableSerializer.TryDeserializeEffects(definition.effects, out List<TextureEffectData> restoredEffects, out TextureLabValidationResult effectValidation))
            {
                validation.AddRange(effectValidation);
                return false;
            }

            id = definition.id ?? string.Empty;
            displayName = definition.displayName ?? string.Empty;
            effects = restoredEffects;
            paletteDependencies = new List<TextureLabPalette>();
            validation.AddRange(TextureLabPortableSerializer.ValidatePreset(definition));
            return validation.IsValid;
        }

        private void OnValidate()
        {
            if (string.IsNullOrEmpty(id))
                id = TextureLabStableId.FromText("preset:" + name);
            if (string.IsNullOrEmpty(displayName))
                displayName = name;
            effects ??= new List<TextureEffectData>();
            paletteDependencies ??= new List<TextureLabPalette>();
        }

        private static List<TextureEffectData> CopyEffects(IEnumerable<TextureEffectData> source)
        {
            var copies = new List<TextureEffectData>();
            foreach (TextureEffectData effect in source)
                copies.Add(effect.Duplicate());
            return copies;
        }
    }
}
