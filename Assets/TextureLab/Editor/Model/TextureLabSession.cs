using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using TextureLab.Runtime;

namespace TextureLab.Editor
{
    [FilePath("Library/TextureLabSession.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class TextureLabSession : ScriptableSingleton<TextureLabSession>
    {
        [SerializeField] private Texture2D sourceTexture;
        [SerializeReference] private List<TextureEffectData> effects = new();
        [SerializeField] private List<TextureLabPaletteDefinition> paletteOverrides = new();
        [SerializeField] private int previewTiles = 1;
        [SerializeField] private int previewZoom;
        [SerializeField] private PreviewChannel previewChannel;
        [SerializeField] private int previewMaxDimension = 1024;
        [NonSerialized] private Dictionary<string, TextureLabPalette> overrideObjects = new();

        internal Texture2D SourceTexture { get => sourceTexture; set => sourceTexture = value; }
        internal List<TextureEffectData> Effects => effects ??= new List<TextureEffectData>();
        private List<TextureLabPaletteDefinition> PaletteOverrides => paletteOverrides ??= new List<TextureLabPaletteDefinition>();
        internal int PreviewTiles { get => previewTiles is >= 2 and <= 8 ? previewTiles : 1; set => previewTiles = value; }
        internal int PreviewZoom { get => previewZoom is 25 or 50 or 100 or 200 ? previewZoom : 0; set => previewZoom = value; }
        internal PreviewChannel PreviewChannel { get => previewChannel; set => previewChannel = value; }
        internal int PreviewMaxDimension { get => previewMaxDimension is 512 or 2048 ? previewMaxDimension : 1024; set => previewMaxDimension = value; }

        internal void ReplaceEffects(IEnumerable<TextureEffectData> source)
        {
            effects = new List<TextureEffectData>();
            foreach (TextureEffectData effect in source)
                effects.Add(effect.Duplicate());
        }

        internal void ResolvePaletteReferences(IEnumerable<TextureLabPalette> palettes)
        {
            var byId = new Dictionary<string, TextureLabPalette>();
            var activeOverrideIds = new HashSet<string>();
            foreach (TextureLabPaletteDefinition definition in PaletteOverrides)
            {
                if (definition == null || string.IsNullOrEmpty(definition.id))
                    continue;

                TextureLabPalette overridePalette = GetOverrideObject(definition);
                activeOverrideIds.Add(definition.id);
                byId[definition.id] = overridePalette;
            }

            if (palettes != null)
            {
                foreach (TextureLabPalette palette in palettes)
                {
                    if (palette != null && !byId.ContainsKey(palette.Id))
                        byId.Add(palette.Id, palette);
                }
            }

            foreach (TextureEffectData effect in Effects)
            {
                if (effect is PaletteQuantizeEffectData quantize
                    && byId.TryGetValue(quantize.PaletteId, out TextureLabPalette palette))
                    quantize.ResolvePalette(palette);
            }

            RemoveStaleOverrideObjects(activeOverrideIds);
        }

        internal TextureLabPalette EnsureEditablePalette(TextureLabPalette palette)
        {
            if (palette == null)
                return null;

            TextureLabPaletteDefinition definition = FindPaletteOverride(palette.Id);
            if (definition == null)
            {
                definition = palette.ToRuntimeDefinition();
                definition.isBuiltIn = false;
                PaletteOverrides.Add(definition);
            }

            return GetOverrideObject(definition);
        }

        internal void UpdatePaletteOverride(TextureLabPalette palette)
        {
            if (palette == null)
                return;

            TextureLabPaletteDefinition updated = palette.ToRuntimeDefinition();
            updated.isBuiltIn = false;
            for (int index = 0; index < PaletteOverrides.Count; index++)
            {
                TextureLabPaletteDefinition existing = PaletteOverrides[index];
                if (existing != null && string.Equals(existing.id, updated.id, StringComparison.Ordinal))
                {
                    PaletteOverrides[index] = updated;
                    return;
                }
            }

            PaletteOverrides.Add(updated);
        }

        internal void Persist() => Save(true);

        private TextureLabPaletteDefinition FindPaletteOverride(string paletteId)
        {
            foreach (TextureLabPaletteDefinition definition in PaletteOverrides)
            {
                if (definition != null && string.Equals(definition.id, paletteId, StringComparison.Ordinal))
                    return definition;
            }

            return null;
        }

        private TextureLabPalette GetOverrideObject(TextureLabPaletteDefinition definition)
        {
            overrideObjects ??= new Dictionary<string, TextureLabPalette>();
            if (!overrideObjects.TryGetValue(definition.id, out TextureLabPalette palette) || palette == null)
            {
                palette = CreateInstance<TextureLabPalette>();
                palette.hideFlags = HideFlags.HideAndDontSave;
                overrideObjects[definition.id] = palette;
            }

            palette.name = string.IsNullOrEmpty(definition.displayName)
                ? "Texture Lab Palette Override"
                : definition.displayName;
            palette.ApplyRuntimeDefinition(definition);
            return palette;
        }

        private void RemoveStaleOverrideObjects(HashSet<string> activeOverrideIds)
        {
            if (overrideObjects == null)
                return;

            var staleIds = new List<string>();
            foreach (KeyValuePair<string, TextureLabPalette> pair in overrideObjects)
            {
                if (!activeOverrideIds.Contains(pair.Key))
                {
                    if (pair.Value != null)
                        DestroyImmediate(pair.Value);
                    staleIds.Add(pair.Key);
                }
            }

            foreach (string staleId in staleIds)
                overrideObjects.Remove(staleId);
        }

        private void OnDisable()
        {
            if (overrideObjects == null)
                return;

            foreach (TextureLabPalette palette in overrideObjects.Values)
            {
                if (palette != null)
                    DestroyImmediate(palette);
            }

            overrideObjects.Clear();
        }
    }
}
