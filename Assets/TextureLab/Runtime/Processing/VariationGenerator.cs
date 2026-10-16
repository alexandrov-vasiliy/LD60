using System;
using System.Collections.Generic;
using UnityEngine;

namespace TextureLab.Runtime
{
    internal static class VariationGenerator
    {
        private static readonly int[] PixelateSizes = { 1, 2, 4, 8, 16, 32, 64 };
        private static readonly int[] TargetResolutions = { 32, 64, 128, 256, 512, 1024, 2048 };

        internal static List<List<TextureEffectData>> Generate(IReadOnlyList<TextureEffectData> source, int seed, int count = 9)
        {
            var random = new System.Random(seed);
            var variations = new List<List<TextureEffectData>>(count);
            for (int i = 0; i < count; i++)
            {
                var variation = new List<TextureEffectData>(source.Count);
                foreach (TextureEffectData effect in source)
                {
                    TextureEffectData copy = effect.Duplicate();
                    if (copy.AllowRandomize)
                        Randomize(copy, random);
                    variation.Add(copy);
                }

                variations.Add(variation);
            }

            return variations;
        }

        private static void Randomize(TextureEffectData effect, System.Random random)
        {
            switch (effect)
            {
                case PixelateEffectData pixelate:
                    if (pixelate.Mode == PixelateMode.BlockSize)
                        pixelate.BlockSize = Step(PixelateSizes, pixelate.BlockSize, random);
                    else
                        pixelate.TargetResolution = Step(TargetResolutions, pixelate.TargetResolution, random);
                    break;
                case PosterizeEffectData posterize:
                    posterize.RedBits = Mathf.Clamp(posterize.RedBits + Delta(random), 1, 8);
                    posterize.GreenBits = Mathf.Clamp(posterize.GreenBits + Delta(random), 1, 8);
                    posterize.BlueBits = Mathf.Clamp(posterize.BlueBits + Delta(random), 1, 8);
                    break;
                case LevelsEffectData levels:
                    levels.BlackPoint = Mathf.Clamp01(levels.BlackPoint + Range(random, -0.08f, 0.08f));
                    levels.WhitePoint = Mathf.Clamp(levels.WhitePoint + Range(random, -0.08f, 0.08f), levels.BlackPoint + 0.001f, 1f);
                    levels.Gamma = Mathf.Clamp(levels.Gamma + Range(random, -0.25f, 0.25f), 0.1f, 4f);
                    break;
                case ColorAdjustmentsEffectData adjustments:
                    adjustments.Brightness = Mathf.Clamp(adjustments.Brightness + Range(random, -0.12f, 0.12f), -1f, 1f);
                    adjustments.Contrast = Mathf.Clamp(adjustments.Contrast + Range(random, -0.12f, 0.12f), -1f, 1f);
                    adjustments.Gamma = Mathf.Clamp(adjustments.Gamma + Range(random, -0.2f, 0.2f), 0.1f, 4f);
                    break;
                case PaletteQuantizeEffectData quantize when quantize.Palette != null:
                    int maximum = Mathf.Min(64, quantize.Palette.Colors.Count);
                    int current = quantize.ColorLimit > 0 ? quantize.ColorLimit : maximum;
                    quantize.ColorLimit = Mathf.Clamp(current + Delta(random), 1, maximum);
                    break;
                case DitherEffectData dither:
                    dither.Strength = Mathf.Clamp01(dither.Strength + Range(random, -0.08f, 0.08f));
                    dither.Scale = Mathf.Clamp(dither.Scale + Delta(random), 1, 8);
                    dither.Seed = random.Next();
                    break;
                case NoiseEffectData noise:
                    noise.Amount = Mathf.Clamp01(noise.Amount + Range(random, -0.1f, 0.1f));
                    noise.Scale = Mathf.Clamp(noise.Scale + Delta(random) * 2, 1, 64);
                    noise.Seed = random.Next();
                    break;
                case GaussianBlurEffectData blur:
                    blur.Radius = Mathf.Clamp(blur.Radius + Range(random, -2f, 2f), 0f, 32f);
                    blur.Iterations = Mathf.Clamp(blur.Iterations + Delta(random), 1, 4);
                    break;
                case ChannelRemapEffectData mixer:
                    mixer.Strength = Mathf.Clamp01(mixer.Strength + Range(random, -0.12f, 0.12f));
                    mixer.RedOutput += RandomVector(random, 0.05f);
                    mixer.GreenOutput += RandomVector(random, 0.05f);
                    mixer.BlueOutput += RandomVector(random, 0.05f);
                    break;
                case OffsetEffectData offset:
                    offset.OffsetX = Mathf.Repeat(offset.OffsetX + Range(random, -0.1f, 0.1f), 1f);
                    offset.OffsetY = Mathf.Repeat(offset.OffsetY + Range(random, -0.1f, 0.1f), 1f);
                    break;
                case SeamBlendEffectData seamBlend:
                    seamBlend.BlendWidth = Mathf.Clamp(seamBlend.BlendWidth + Range(random, -0.05f, 0.05f), 0f, 0.5f);
                    seamBlend.BlendStrength = Mathf.Clamp01(seamBlend.BlendStrength + Range(random, -0.12f, 0.12f));
                    break;
            }
        }

        private static int Step(IReadOnlyList<int> values, int value, System.Random random)
        {
            int index = 0;
            for (int i = 1; i < values.Count; i++)
            {
                if (Mathf.Abs(values[i] - value) < Mathf.Abs(values[index] - value))
                    index = i;
            }

            return values[Mathf.Clamp(index + Delta(random), 0, values.Count - 1)];
        }

        private static int Delta(System.Random random) => random.Next(-1, 2);
        private static float Range(System.Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);
        private static Vector3 RandomVector(System.Random random, float amount) => new(
            Range(random, -amount, amount),
            Range(random, -amount, amount),
            Range(random, -amount, amount));
    }
}
