using System.Collections.Generic;
using UnityEngine;

namespace TextureLab.Runtime
{
    /// <summary>
    /// Explicit palette references that are available in every player build.
    /// Keeping the references serialized makes the catalog AOT and WebGL safe.
    /// </summary>
    public sealed class TextureLabPaletteCatalog : ScriptableObject
    {
        public const string BuiltInResourceName = "TextureLabBuiltInPaletteCatalog";

        [SerializeField] private List<TextureLabPalette> palettes = new();

        public IReadOnlyList<TextureLabPalette> Palettes => palettes ??= new List<TextureLabPalette>();

        public static TextureLabPaletteCatalog LoadBuiltIn()
        {
            return Resources.Load<TextureLabPaletteCatalog>(BuiltInResourceName);
        }

        public bool Contains(TextureLabPalette palette)
        {
            if (palette == null)
                return false;
            foreach (TextureLabPalette candidate in Palettes)
            {
                if (candidate == palette)
                    return true;
            }

            return false;
        }

        public List<TextureLabPaletteDefinition> CreateRuntimeDefinitions()
        {
            var definitions = new List<TextureLabPaletteDefinition>(Palettes.Count);
            foreach (TextureLabPalette palette in Palettes)
            {
                if (palette == null)
                    continue;

                TextureLabPaletteDefinition definition = palette.ToRuntimeDefinition();
                definition.isBuiltIn = true;
                definitions.Add(definition);
            }

            return definitions;
        }
    }
}
