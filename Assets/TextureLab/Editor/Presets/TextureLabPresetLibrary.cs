using UnityEditor;
using UnityEngine;
using TextureLab.Runtime;

namespace TextureLab.Editor
{
    internal static class TextureLabPresetLibrary
    {
        private const string RuntimeFolder = "Assets/TextureLab/Runtime";
        private const string PresetsFolder = RuntimeFolder + "/Presets";
        private const string Folder = PresetsFolder + "/Starter";

        [MenuItem("Tools/Texture Lab/Create Starter Presets")]
        private static void CreateStarterPresets()
        {
            EnsureFolder();
            Create("PSX Soft",
                new PixelateEffectData { Mode = PixelateMode.TargetResolution, TargetResolution = 128, Sampling = PixelSampling.Average },
                new DitherEffectData { Pattern = DitherPattern.Bayer4x4, Strength = 0.07f },
                new PosterizeEffectData { RedBits = 5, GreenBits = 5, BlueBits = 5 });
            Create("PSX Harsh",
                new PixelateEffectData { BlockSize = 4, Sampling = PixelSampling.Nearest },
                new DitherEffectData { Pattern = DitherPattern.Bayer4x4, Strength = 0.1f },
                new PosterizeEffectData { RedBits = 4, GreenBits = 4, BlueBits = 4 });
            Create("Low Color",
                new PosterizeEffectData { RedBits = 3, GreenBits = 3, BlueBits = 3 },
                new DitherEffectData { Pattern = DitherPattern.Bayer8x8, Strength = 0.12f });
            Create("Retro PC",
                new PixelateEffectData { Mode = PixelateMode.TargetResolution, TargetResolution = 320, Sampling = PixelSampling.Nearest },
                new PosterizeEffectData { RedBits = 3, GreenBits = 3, BlueBits = 2 });
            Create("Dirty Texture",
                new NoiseEffectData { Type = NoiseType.Value, Amount = 0.12f, Scale = 4, Seed = 1 },
                new ColorAdjustmentsEffectData { Contrast = 0.1f });
            Create("Posterized",
                new PosterizeEffectData { RedBits = 4, GreenBits = 4, BlueBits = 4 });
            Create("Pixel Art",
                new PixelateEffectData { BlockSize = 8, Sampling = PixelSampling.Nearest },
                new PosterizeEffectData { RedBits = 5, GreenBits = 5, BlueBits = 5 });
            Create("Dreamcast-ish",
                new PixelateEffectData { Mode = PixelateMode.TargetResolution, TargetResolution = 256, Sampling = PixelSampling.Average },
                new ColorAdjustmentsEffectData { Contrast = 0.12f },
                new DitherEffectData { Pattern = DitherPattern.Bayer4x4, Strength = 0.04f });
            Create("Dark Horror",
                new ColorAdjustmentsEffectData { Brightness = -0.15f, Contrast = 0.3f },
                new NoiseEffectData { Type = NoiseType.Blue, Amount = 0.07f, Scale = 2, Seed = 13 },
                new LevelsEffectData { BlackPoint = 0.08f, WhitePoint = 0.9f, Gamma = 1.15f });
            AssetDatabase.SaveAssets();
        }

        private static void Create(string name, params TextureEffectData[] effects)
        {
            string path = $"{Folder}/{name}.asset";
            if (AssetDatabase.LoadAssetAtPath<TextureLabPreset>(path) != null)
                return;

            var preset = ScriptableObject.CreateInstance<TextureLabPreset>();
            preset.SetEffects(effects);
            AssetDatabase.CreateAsset(preset, path);
        }

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(PresetsFolder))
                AssetDatabase.CreateFolder(RuntimeFolder, "Presets");
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder(PresetsFolder, "Starter");
        }
    }
}
