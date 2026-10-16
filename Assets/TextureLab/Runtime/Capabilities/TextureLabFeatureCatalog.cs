using System;
using System.Collections.Generic;
using UnityEngine;

namespace TextureLab.Runtime
{
    public enum TextureLabEffectGroup
    {
        Structure,
        Color,
        Texture,
        SeamAndPaint
    }

    [Serializable]
    public sealed class TextureLabFeatureEntry
    {
        [SerializeField] private string effectType = string.Empty;
        [SerializeField] private TextureLabEffectGroup group;
        [SerializeField] private int sortOrder;
        [SerializeField] private bool webEnabled = true;
        [SerializeField] private bool windowsEnabled = true;
        [SerializeField] private string unavailableMessageKey = "feature.desktopOnly";
        [SerializeField] private string requiredCapability = string.Empty;

        public TextureLabFeatureEntry()
        {
        }

        public TextureLabFeatureEntry(
            string effectType,
            TextureLabEffectGroup group,
            int sortOrder,
            bool webEnabled,
            bool windowsEnabled,
            string messageKey,
            string requiredCapability)
        {
            this.effectType = effectType ?? string.Empty;
            this.group = group;
            this.sortOrder = sortOrder;
            this.webEnabled = webEnabled;
            this.windowsEnabled = windowsEnabled;
            unavailableMessageKey = string.IsNullOrEmpty(messageKey) ? "feature.desktopOnly" : messageKey;
            this.requiredCapability = requiredCapability ?? string.Empty;
        }

        public string EffectType => effectType;
        public TextureLabEffectGroup Group => group;
        public int SortOrder => sortOrder;
        public bool WebEnabled => webEnabled;
        public bool WindowsEnabled => windowsEnabled;
        public string UnavailableMessageKey => unavailableMessageKey;
        public string RequiredCapability => requiredCapability;
    }

    /// <summary>
    /// Developer-facing feature policy. It is deliberately data-driven so a
    /// Web limitation can be changed without teaching the UI about a platform.
    /// </summary>
    [CreateAssetMenu(fileName = "TextureLabFeatureCatalog", menuName = "Texture Lab/Feature Catalog")]
    public sealed class TextureLabFeatureCatalog : ScriptableObject
    {
        public const string DefaultResourceName = "TextureLabFeatureCatalog";

        [SerializeField] private List<TextureLabFeatureEntry> entries = new();
        private Dictionary<string, TextureLabFeatureEntry> byEffectType;

        public IReadOnlyList<TextureLabFeatureEntry> Entries => entries ??= new List<TextureLabFeatureEntry>();

        private void OnEnable()
        {
            EnsureDefaults();
        }

        public static TextureLabFeatureCatalog LoadDefault()
        {
            return Resources.Load<TextureLabFeatureCatalog>(DefaultResourceName);
        }

        public void EnsureDefaults()
        {
            entries ??= new List<TextureLabFeatureEntry>();
            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (TextureLabFeatureEntry entry in entries)
            {
                if (entry != null && !string.IsNullOrEmpty(entry.EffectType))
                    known.Add(entry.EffectType);
            }

            foreach (TextureLabEffectDescriptor descriptor in TextureLabEffectRegistry.Descriptors)
            {
                if (known.Contains(descriptor.EffectType))
                    continue;

                string required = descriptor.EffectType == TextureLabEffectRegistry.ExposureBrush
                    ? "rhalf"
                    : string.Empty;
                entries.Add(new TextureLabFeatureEntry(
                    descriptor.EffectType,
                    DefaultGroup(descriptor.EffectType),
                    DefaultSortOrder(descriptor.EffectType),
                    true,
                    true,
                    "feature.desktopOnly",
                    required));
                known.Add(descriptor.EffectType);
            }

            byEffectType = null;
        }

        public bool IsAvailable(
            string effectType,
            TextureLabCapabilitySnapshot capabilities,
            out string messageKey)
        {
            messageKey = string.Empty;
            TextureLabFeatureEntry entry = Find(effectType);
            if (entry == null)
            {
                messageKey = "feature.unknown";
                return false;
            }

            string platform = capabilities?.Platform ?? string.Empty;
            bool isWeb = platform.IndexOf("Web", StringComparison.OrdinalIgnoreCase) >= 0
                || platform.IndexOf("GLPlayer", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isWindows = platform.IndexOf("Windows", StringComparison.OrdinalIgnoreCase) >= 0;
            if (isWeb && !entry.WebEnabled)
            {
                messageKey = entry.UnavailableMessageKey;
                return false;
            }
            if (isWindows && !entry.WindowsEnabled)
            {
                messageKey = entry.UnavailableMessageKey;
                return false;
            }

            if (string.Equals(entry.RequiredCapability, "rhalf", StringComparison.OrdinalIgnoreCase)
                && capabilities != null
                && !capabilities.SupportsRHalf)
            {
                messageKey = "feature.rhalfRequired";
                return false;
            }

            return true;
        }

        public bool TryGetEntry(string effectType, out TextureLabFeatureEntry entry)
        {
            EnsureDefaults();
            if (string.IsNullOrEmpty(effectType))
            {
                entry = null;
                return false;
            }
            if (byEffectType == null)
            {
                byEffectType = new Dictionary<string, TextureLabFeatureEntry>(StringComparer.Ordinal);
                foreach (TextureLabFeatureEntry catalogEntry in entries)
                {
                    if (catalogEntry != null && !string.IsNullOrEmpty(catalogEntry.EffectType))
                        byEffectType[catalogEntry.EffectType] = catalogEntry;
                }
            }

            return byEffectType.TryGetValue(effectType, out entry);
        }

        private TextureLabFeatureEntry Find(string effectType)
        {
            TryGetEntry(effectType, out TextureLabFeatureEntry entry);
            return entry;
        }

        private static TextureLabEffectGroup DefaultGroup(string effectType)
        {
            return effectType switch
            {
                TextureLabEffectRegistry.Pixelate => TextureLabEffectGroup.Structure,
                TextureLabEffectRegistry.Posterize => TextureLabEffectGroup.Structure,
                TextureLabEffectRegistry.Levels => TextureLabEffectGroup.Structure,
                TextureLabEffectRegistry.GaussianBlur => TextureLabEffectGroup.Structure,
                TextureLabEffectRegistry.ColorAdjustments => TextureLabEffectGroup.Color,
                TextureLabEffectRegistry.ColorReplace => TextureLabEffectGroup.Color,
                TextureLabEffectRegistry.PaletteQuantize => TextureLabEffectGroup.Color,
                TextureLabEffectRegistry.ChannelMixer => TextureLabEffectGroup.Color,
                TextureLabEffectRegistry.Dither => TextureLabEffectGroup.Texture,
                TextureLabEffectRegistry.Noise => TextureLabEffectGroup.Texture,
                _ => TextureLabEffectGroup.SeamAndPaint
            };
        }

        private static int DefaultSortOrder(string effectType)
        {
            return effectType switch
            {
                TextureLabEffectRegistry.Pixelate => 0,
                TextureLabEffectRegistry.Posterize => 10,
                TextureLabEffectRegistry.Levels => 20,
                TextureLabEffectRegistry.GaussianBlur => 30,
                TextureLabEffectRegistry.ColorAdjustments => 0,
                TextureLabEffectRegistry.ColorReplace => 10,
                TextureLabEffectRegistry.PaletteQuantize => 20,
                TextureLabEffectRegistry.ChannelMixer => 30,
                TextureLabEffectRegistry.Dither => 0,
                TextureLabEffectRegistry.Noise => 10,
                TextureLabEffectRegistry.ExposureBrush => 0,
                TextureLabEffectRegistry.Offset => 10,
                TextureLabEffectRegistry.SeamBlend => 20,
                _ => int.MaxValue
            };
        }
    }
}
