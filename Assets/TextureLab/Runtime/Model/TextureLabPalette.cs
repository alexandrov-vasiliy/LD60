using System;
using System.Collections.Generic;
using UnityEngine;

namespace TextureLab.Runtime
{
    [CreateAssetMenu(fileName = "Texture Lab Palette", menuName = "Texture Lab/Palette")]
    public sealed class TextureLabPalette : ScriptableObject
    {
        [SerializeField] private string id = string.Empty;
        [SerializeField] private string displayName = string.Empty;
        [SerializeField] private List<Color> colors = new() { Color.black, Color.white };

        public string Id
        {
            get
            {
                if (string.IsNullOrEmpty(id))
                    id = Guid.NewGuid().ToString("N");
                return id;
            }
        }

        public string DisplayName
        {
            get => string.IsNullOrEmpty(displayName) ? name : displayName;
            set => displayName = value ?? string.Empty;
        }

        public List<Color> Colors => colors ??= new List<Color>();

        public TextureLabPaletteDefinition ToRuntimeDefinition()
        {
            var definition = new TextureLabPaletteDefinition
            {
                id = Id,
                displayName = DisplayName,
                colors = new List<Color32>(Colors.Count)
            };

            foreach (Color color in Colors)
            {
                definition.colors.Add(new Color32(
                    (byte)Mathf.RoundToInt(Mathf.Clamp01(color.r) * 255f),
                    (byte)Mathf.RoundToInt(Mathf.Clamp01(color.g) * 255f),
                    (byte)Mathf.RoundToInt(Mathf.Clamp01(color.b) * 255f),
                    (byte)Mathf.RoundToInt(Mathf.Clamp01(color.a) * 255f)));
            }

            return definition;
        }

        public void ApplyRuntimeDefinition(TextureLabPaletteDefinition definition)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));

            id = definition.id ?? string.Empty;
            displayName = definition.displayName ?? string.Empty;
            colors = new List<Color>(definition.colors != null ? definition.colors.Count : 0);
            if (definition.colors != null)
            {
                foreach (Color32 color in definition.colors)
                    colors.Add(new Color(color.r / 255f, color.g / 255f, color.b / 255f, color.a / 255f));
            }

            OnValidate();
        }

        private void OnValidate()
        {
            if (string.IsNullOrEmpty(id))
                id = Guid.NewGuid().ToString("N");

            if (string.IsNullOrEmpty(displayName))
                displayName = name;

            colors ??= new List<Color>();
            if (colors.Count > 64)
                colors.RemoveRange(64, colors.Count - 64);
            if (colors.Count == 0)
                colors.Add(Color.black);
        }
    }
}
