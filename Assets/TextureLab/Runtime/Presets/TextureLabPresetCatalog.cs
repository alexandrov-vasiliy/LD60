using System.Collections.Generic;
using UnityEngine;

namespace TextureLab.Runtime
{
    /// <summary>
    /// Explicit references for built-in presets. Runtime code never searches
    /// the AssetDatabase to discover these entries.
    /// </summary>
    public sealed class TextureLabPresetCatalog : ScriptableObject
    {
        public const string BuiltInResourceName = "TextureLabBuiltInPresetCatalog";

        [SerializeField] private List<TextureLabPreset> presets = new();

        public IReadOnlyList<TextureLabPreset> Presets => presets ??= new List<TextureLabPreset>();

        public static TextureLabPresetCatalog LoadBuiltIn()
        {
            return Resources.Load<TextureLabPresetCatalog>(BuiltInResourceName);
        }

        public List<TextureLabPresetDefinition> CreateRuntimeDefinitions()
        {
            var definitions = new List<TextureLabPresetDefinition>(Presets.Count);
            foreach (TextureLabPreset preset in Presets)
            {
                if (preset == null || !preset.TryToRuntimeDefinition(out TextureLabPresetDefinition definition, out _))
                    continue;

                definition.isBuiltIn = true;
                definitions.Add(definition);
            }

            return definitions;
        }
    }
}
