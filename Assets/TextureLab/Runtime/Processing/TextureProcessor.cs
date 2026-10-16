using System;
using System.Collections.Generic;
using UnityEngine;

namespace TextureLab.Runtime
{
    internal readonly struct ProcessingContext
    {
        internal ProcessingContext(int originalWidth, int originalHeight)
        {
            OriginalWidth = originalWidth;
            OriginalHeight = originalHeight;
        }

        internal int OriginalWidth { get; }
        internal int OriginalHeight { get; }
    }

    internal interface ITextureEffectProcessor
    {
        void Process(Texture source, RenderTexture destination, TextureEffectData effect, ProcessingContext context);
    }

    internal sealed class TextureProcessor : IDisposable
    {
        private static readonly int PreviewChannelId = Shader.PropertyToID("_PreviewChannel");
        private readonly PixelateProcessor pixelateProcessor;
        private readonly ColorEffectProcessor colorProcessor;
        private readonly PaletteQuantizeProcessor paletteProcessor;
        private readonly DitherProcessor ditherProcessor;
        private readonly NoiseProcessor noiseProcessor;
        private readonly GaussianBlurProcessor blurProcessor;
        private readonly SeamlessProcessor seamlessProcessor;
        private readonly ChannelMixerProcessor channelProcessor;
        private readonly ExposureBrushProcessor exposureBrushProcessor;
        private readonly TextureLabPaletteRepository paletteRepository;
        private readonly Material displayMaterial;
        private readonly Dictionary<string, ITextureEffectProcessor> processors;
        private RenderTexture targetA;
        private RenderTexture targetB;
        private RenderTexture displayTarget;

        internal TextureProcessor(
            TextureLabShaderLibrary shaderLibrary = null,
            TextureLabPaletteRepository paletteRepository = null)
        {
            shaderLibrary ??= TextureLabShaderLibrary.LoadDefault();
            this.paletteRepository = paletteRepository;
            pixelateProcessor = (PixelateProcessor)TextureLabEffectRegistry.CreateProcessor(TextureLabEffectRegistry.Pixelate, shaderLibrary);
            colorProcessor = (ColorEffectProcessor)TextureLabEffectRegistry.CreateProcessor(TextureLabEffectRegistry.Posterize, shaderLibrary);
            paletteProcessor = new PaletteQuantizeProcessor(shaderLibrary.PaletteQuantize, paletteRepository);
            ditherProcessor = (DitherProcessor)TextureLabEffectRegistry.CreateProcessor(TextureLabEffectRegistry.Dither, shaderLibrary);
            noiseProcessor = (NoiseProcessor)TextureLabEffectRegistry.CreateProcessor(TextureLabEffectRegistry.Noise, shaderLibrary);
            blurProcessor = (GaussianBlurProcessor)TextureLabEffectRegistry.CreateProcessor(TextureLabEffectRegistry.GaussianBlur, shaderLibrary);
            seamlessProcessor = (SeamlessProcessor)TextureLabEffectRegistry.CreateProcessor(TextureLabEffectRegistry.SeamBlend, shaderLibrary);
            channelProcessor = (ChannelMixerProcessor)TextureLabEffectRegistry.CreateProcessor(TextureLabEffectRegistry.ChannelMixer, shaderLibrary);
            exposureBrushProcessor = (ExposureBrushProcessor)TextureLabEffectRegistry.CreateProcessor(TextureLabEffectRegistry.ExposureBrush, shaderLibrary);
            Shader channelShader = shaderLibrary.Channels;
            displayMaterial = new Material(channelShader) { hideFlags = HideFlags.HideAndDontSave };
            processors = new Dictionary<string, ITextureEffectProcessor>
            {
                [TextureLabEffectRegistry.Pixelate] = pixelateProcessor,
                [TextureLabEffectRegistry.Posterize] = colorProcessor,
                [TextureLabEffectRegistry.Levels] = colorProcessor,
                [TextureLabEffectRegistry.ColorAdjustments] = colorProcessor,
                [TextureLabEffectRegistry.ColorReplace] = colorProcessor,
                [TextureLabEffectRegistry.PaletteQuantize] = paletteProcessor,
                [TextureLabEffectRegistry.Dither] = ditherProcessor,
                [TextureLabEffectRegistry.Noise] = noiseProcessor,
                [TextureLabEffectRegistry.GaussianBlur] = blurProcessor,
                [TextureLabEffectRegistry.Offset] = seamlessProcessor,
                [TextureLabEffectRegistry.SeamBlend] = seamlessProcessor,
                [TextureLabEffectRegistry.ChannelMixer] = channelProcessor,
                [TextureLabEffectRegistry.ExposureBrush] = exposureBrushProcessor
            };
        }

        internal RenderTexture Result { get; private set; }

        internal void Process(Texture2D source, IReadOnlyList<TextureEffectData> effects, int previewMaxDimension)
        {
            if (source == null)
            {
                Result = null;
                return;
            }

            GetPreviewSize(source.width, source.height, previewMaxDimension, out int width, out int height);
            EnsureTargets(width, height);
            Graphics.Blit(source, targetA);

            RenderTexture current = targetA;
            RenderTexture destination = targetB;
            var context = new ProcessingContext(source.width, source.height);

            foreach (TextureEffectData effect in effects)
            {
                if (!effect.Enabled
                    || !TextureLabEffectRegistry.TryGetEffectType(effect, out string effectType)
                    || !processors.TryGetValue(effectType, out ITextureEffectProcessor processor))
                    continue;

                processor.Process(current, destination, effect, context);
                (current, destination) = (destination, current);
            }

            Result = current;
        }

        internal void ProcessFullResolution(Texture2D source, IReadOnlyList<TextureEffectData> effects)
        {
            Process(source, effects, Mathf.Max(source.width, source.height));
        }

        internal RenderTexture RenderDisplay(Texture source, PreviewChannel channel, int width, int height)
        {
            if (source == null)
                return null;

            EnsureDisplayTarget(width, height);
            displayMaterial.SetInt(PreviewChannelId, (int)channel);
            Graphics.Blit(source, displayTarget, displayMaterial, 1);
            return displayTarget;
        }

        public void Dispose()
        {
            pixelateProcessor.Dispose();
            colorProcessor.Dispose();
            paletteProcessor.Dispose();
            ditherProcessor.Dispose();
            noiseProcessor.Dispose();
            blurProcessor.Dispose();
            seamlessProcessor.Dispose();
            channelProcessor.Dispose();
            exposureBrushProcessor.Dispose();
            RuntimeObjectLifetime.Destroy(displayMaterial);
            ReleaseTargets();
        }

        private static void GetPreviewSize(int sourceWidth, int sourceHeight, int maxDimension, out int width, out int height)
        {
            float scale = Mathf.Min(1f, Mathf.Max(1, maxDimension) / (float)Mathf.Max(sourceWidth, sourceHeight));
            width = Mathf.Max(1, Mathf.RoundToInt(sourceWidth * scale));
            height = Mathf.Max(1, Mathf.RoundToInt(sourceHeight * scale));
        }

        private void EnsureTargets(int width, int height)
        {
            if (targetA != null && targetA.width == width && targetA.height == height)
                return;

            ReleaseTargets();
            targetA = CreateTarget("Texture Lab Preview A", width, height);
            targetB = CreateTarget("Texture Lab Preview B", width, height);
        }

        private void EnsureDisplayTarget(int width, int height)
        {
            if (displayTarget != null && displayTarget.width == width && displayTarget.height == height)
                return;

            ReleaseTarget(ref displayTarget);
            displayTarget = CreateTarget("Texture Lab Preview Display", width, height);
        }

        private static RenderTexture CreateTarget(string name, int width, int height)
        {
            var target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            if (!target.Create())
            {
                RuntimeObjectLifetime.Destroy(target);
                throw new InvalidOperationException($"Texture Lab could not create render target '{name}'.");
            }
            return target;
        }

        private void ReleaseTargets()
        {
            ReleaseTarget(ref targetA);
            ReleaseTarget(ref targetB);
            ReleaseTarget(ref displayTarget);
            Result = null;
        }

        private static void ReleaseTarget(ref RenderTexture target)
        {
            if (target == null)
                return;

            target.Release();
            RuntimeObjectLifetime.Destroy(target);
            target = null;
        }
    }

    internal sealed class PixelateProcessor : ITextureEffectProcessor, IDisposable
    {
        private static readonly int SourceSizeId = Shader.PropertyToID("_SourceSize");
        private static readonly int VirtualResolutionId = Shader.PropertyToID("_VirtualResolution");
        private readonly Material material;

        internal PixelateProcessor(Shader shader)
        {
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        public void Process(Texture source, RenderTexture destination, TextureEffectData effect, ProcessingContext context)
        {
            var pixelate = (PixelateEffectData)effect;
            GetVirtualSize(pixelate, context, out int virtualWidth, out int virtualHeight);
            virtualWidth = Mathf.Clamp(virtualWidth, 1, source.width);
            virtualHeight = Mathf.Clamp(virtualHeight, 1, source.height);

            material.SetVector(SourceSizeId, new Vector4(source.width, source.height, 0f, 0f));
            material.SetVector(VirtualResolutionId, new Vector4(virtualWidth, virtualHeight, 0f, 0f));

            if (pixelate.Sampling == PixelSampling.Nearest)
            {
                Graphics.Blit(source, destination, material, 0);
                return;
            }

            RenderTexture reduced = RenderTexture.GetTemporary(
                virtualWidth,
                virtualHeight,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Default);
            reduced.filterMode = FilterMode.Point;
            reduced.wrapMode = TextureWrapMode.Clamp;

            try
            {
                Graphics.Blit(source, reduced, material, 1);
                material.SetVector(SourceSizeId, new Vector4(virtualWidth, virtualHeight, 0f, 0f));
                Graphics.Blit(reduced, destination, material, 0);
            }
            finally
            {
                RenderTexture.ReleaseTemporary(reduced);
            }
        }

        public void Dispose() => RuntimeObjectLifetime.Destroy(material);

        private static void GetVirtualSize(PixelateEffectData effect, ProcessingContext context, out int width, out int height)
        {
            if (effect.Mode == PixelateMode.BlockSize)
            {
                int size = Mathf.Clamp(effect.BlockSize, 1, 64);
                width = Mathf.CeilToInt(context.OriginalWidth / (float)size);
                height = Mathf.CeilToInt(context.OriginalHeight / (float)size);
                return;
            }

            int target = Mathf.Clamp(effect.TargetResolution, 16, 2048);
            float scale = Mathf.Min(1f, target / (float)Mathf.Max(context.OriginalWidth, context.OriginalHeight));
            width = Mathf.Max(1, Mathf.RoundToInt(context.OriginalWidth * scale));
            height = Mathf.Max(1, Mathf.RoundToInt(context.OriginalHeight * scale));
        }
    }

    internal sealed class ColorEffectProcessor : ITextureEffectProcessor, IDisposable
    {
        private static readonly int SourceSizeId = Shader.PropertyToID("_SourceSize");
        private static readonly int BitsId = Shader.PropertyToID("_Bits");
        private static readonly int InputLevelsId = Shader.PropertyToID("_InputLevels");
        private static readonly int OutputLevelsId = Shader.PropertyToID("_OutputLevels");
        private static readonly int AdjustmentsId = Shader.PropertyToID("_Adjustments");
        private static readonly int ReplaceSourceId = Shader.PropertyToID("_ReplaceSource");
        private static readonly int ReplacementId = Shader.PropertyToID("_Replacement");
        private static readonly int ReplaceSettingsId = Shader.PropertyToID("_ReplaceSettings");
        private readonly Material material;

        internal ColorEffectProcessor(Shader shader)
        {
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        public void Process(Texture source, RenderTexture destination, TextureEffectData effect, ProcessingContext context)
        {
            material.SetVector(SourceSizeId, new Vector4(source.width, source.height, 0f, 0f));

            switch (effect)
            {
                case PosterizeEffectData posterize:
                    material.SetVector(BitsId, new Vector4(posterize.RedBits, posterize.GreenBits, posterize.BlueBits, 0f));
                    Graphics.Blit(source, destination, material, 0);
                    break;
                case LevelsEffectData levels:
                    material.SetVector(InputLevelsId, new Vector4(levels.BlackPoint, levels.WhitePoint, levels.Gamma, 0f));
                    material.SetVector(OutputLevelsId, new Vector4(levels.OutputBlack, levels.OutputWhite, 0f, 0f));
                    Graphics.Blit(source, destination, material, 1);
                    break;
                case ColorAdjustmentsEffectData adjustments:
                    material.SetVector(AdjustmentsId, new Vector4(adjustments.Brightness, adjustments.Contrast, adjustments.Gamma, 0f));
                    Graphics.Blit(source, destination, material, 2);
                    break;
                case ColorReplaceEffectData replace:
                    material.SetColor(ReplaceSourceId, replace.SourceColor);
                    material.SetColor(ReplacementId, replace.ReplacementColor);
                    material.SetVector(ReplaceSettingsId, new Vector4(replace.Tolerance, replace.Softness, replace.PreviewMask ? 1f : 0f, 0f));
                    Graphics.Blit(source, destination, material, 3);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(effect), effect.GetType().Name, "Unsupported color effect.");
            }
        }

        public void Dispose() => RuntimeObjectLifetime.Destroy(material);
    }

    internal sealed class PaletteQuantizeProcessor : ITextureEffectProcessor, IDisposable
    {
        private const int MaximumColors = 64;
        private static readonly int SourceSizeId = Shader.PropertyToID("_SourceSize");
        private static readonly int PaletteLabId = Shader.PropertyToID("_PaletteLab");
        private static readonly int PaletteRgbId = Shader.PropertyToID("_PaletteRgb");
        private static readonly int PaletteCountId = Shader.PropertyToID("_PaletteCount");
        private readonly Vector4[] paletteLabBuffer = new Vector4[MaximumColors];
        private readonly Vector4[] paletteRgbBuffer = new Vector4[MaximumColors];
        private readonly TextureLabPaletteRepository paletteRepository;
        private readonly Material material;
        private string cachedPaletteId = string.Empty;
        private int cachedColorLimit = -1;
        private int cachedPaletteRevision = -1;
        private int cachedPaletteHash;
        private int cachedColorCount;

        internal PaletteQuantizeProcessor(Shader shader, TextureLabPaletteRepository paletteRepository)
        {
            this.paletteRepository = paletteRepository;
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        public void Process(Texture source, RenderTexture destination, TextureEffectData effect, ProcessingContext context)
        {
            var quantize = (PaletteQuantizeEffectData)effect;
            int colorCount = PreparePalette(quantize);
            if (colorCount == 0)
            {
                Graphics.Blit(source, destination);
                return;
            }

            material.SetVector(SourceSizeId, new Vector4(source.width, source.height, 0f, 0f));
            material.SetVectorArray(PaletteLabId, paletteLabBuffer);
            material.SetVectorArray(PaletteRgbId, paletteRgbBuffer);
            material.SetInt(PaletteCountId, colorCount);
            Graphics.Blit(source, destination, material, 0);
        }

        public void Dispose() => RuntimeObjectLifetime.Destroy(material);

        private int PreparePalette(PaletteQuantizeEffectData quantize)
        {
            TextureLabPaletteDefinition definition = null;
            IReadOnlyList<Color> colors = quantize.Palette != null ? quantize.Palette.Colors : null;
            int repositoryRevision = paletteRepository != null ? paletteRepository.Revision : -1;
            if (colors == null && paletteRepository != null)
            {
                paletteRepository.TryGet(quantize.PaletteId, out definition);
                if (definition != null)
                    colors = ToColors(definition.colors);
            }

            int sourceCount = colors == null ? 0 : Mathf.Min(MaximumColors, colors.Count);
            int colorCount = quantize.ColorLimit > 0 ? Mathf.Min(sourceCount, quantize.ColorLimit) : sourceCount;
            int paletteHash = CalculateHash(colors, colorCount);
            if (string.Equals(cachedPaletteId, quantize.PaletteId, StringComparison.Ordinal)
                && cachedColorLimit == quantize.ColorLimit
                && cachedPaletteRevision == repositoryRevision
                && cachedPaletteHash == paletteHash)
                return cachedColorCount;

            cachedPaletteId = quantize.PaletteId;
            cachedColorLimit = quantize.ColorLimit;
            cachedPaletteRevision = repositoryRevision;
            cachedPaletteHash = paletteHash;
            cachedColorCount = colorCount;
            for (int index = 0; index < colorCount; index++)
            {
                Color color = colors[index];
                paletteRgbBuffer[index] = new Vector4(color.r, color.g, color.b, color.a);
                Vector3 lab = LinearRgbToOklab(color);
                paletteLabBuffer[index] = new Vector4(lab.x, lab.y, lab.z, 0f);
            }

            return colorCount;
        }

        private static List<Color> ToColors(IReadOnlyList<Color32> colors)
        {
            var result = new List<Color>(colors == null ? 0 : colors.Count);
            if (colors == null)
                return result;
            foreach (Color32 color in colors)
                result.Add(new Color(color.r / 255f, color.g / 255f, color.b / 255f, color.a / 255f));
            return result;
        }

        private static int CalculateHash(IReadOnlyList<Color> colors, int count)
        {
            unchecked
            {
                int hash = 17;
                for (int index = 0; index < count; index++)
                {
                    Color color = colors[index];
                    hash = hash * 31 + Mathf.RoundToInt(color.r * 65535f);
                    hash = hash * 31 + Mathf.RoundToInt(color.g * 65535f);
                    hash = hash * 31 + Mathf.RoundToInt(color.b * 65535f);
                    hash = hash * 31 + Mathf.RoundToInt(color.a * 65535f);
                }

                return hash;
            }
        }

        private static Vector3 LinearRgbToOklab(Color color)
        {
            float l = 0.4122214708f * color.r + 0.5363325363f * color.g + 0.0514459929f * color.b;
            float m = 0.2119034982f * color.r + 0.6806995451f * color.g + 0.1073969566f * color.b;
            float s = 0.0883024619f * color.r + 0.2817188376f * color.g + 0.6299787005f * color.b;
            float lRoot = Mathf.Pow(Mathf.Max(l, 0f), 1f / 3f);
            float mRoot = Mathf.Pow(Mathf.Max(m, 0f), 1f / 3f);
            float sRoot = Mathf.Pow(Mathf.Max(s, 0f), 1f / 3f);
            return new Vector3(
                0.2104542553f * lRoot + 0.793617785f * mRoot - 0.0040720468f * sRoot,
                1.9779984951f * lRoot - 2.428592205f * mRoot + 0.4505937099f * sRoot,
                0.0259040371f * lRoot + 0.7827717662f * mRoot - 0.808675766f * sRoot);
        }
    }

    internal sealed class DitherProcessor : ITextureEffectProcessor, IDisposable
    {
        private static readonly int SourceSizeId = Shader.PropertyToID("_SourceSize");
        private static readonly int PatternId = Shader.PropertyToID("_Pattern");
        private static readonly int StrengthId = Shader.PropertyToID("_Strength");
        private static readonly int ScaleId = Shader.PropertyToID("_Scale");
        private static readonly int SeedId = Shader.PropertyToID("_Seed");
        private static readonly int RgbId = Shader.PropertyToID("_Rgb");
        private readonly Material material;

        internal DitherProcessor(Shader shader)
        {
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        public void Process(Texture source, RenderTexture destination, TextureEffectData effect, ProcessingContext context)
        {
            var dither = (DitherEffectData)effect;
            float previewScale = source.width / (float)Mathf.Max(1, context.OriginalWidth);
            int scaledPattern = Mathf.Max(1, Mathf.RoundToInt(dither.Scale * previewScale));
            material.SetVector(SourceSizeId, new Vector4(source.width, source.height, 0f, 0f));
            material.SetInt(PatternId, (int)dither.Pattern);
            material.SetFloat(StrengthId, dither.Strength);
            material.SetInt(ScaleId, scaledPattern);
            material.SetInt(SeedId, dither.Seed);
            material.SetInt(RgbId, dither.Channels == DitherChannels.RGB ? 1 : 0);
            Graphics.Blit(source, destination, material, 0);
        }

        public void Dispose() => RuntimeObjectLifetime.Destroy(material);
    }

    internal sealed class NoiseProcessor : ITextureEffectProcessor, IDisposable
    {
        private static readonly int SourceSizeId = Shader.PropertyToID("_SourceSize");
        private static readonly int NoiseTypeId = Shader.PropertyToID("_NoiseType");
        private static readonly int AmountId = Shader.PropertyToID("_Amount");
        private static readonly int ScaleId = Shader.PropertyToID("_Scale");
        private static readonly int SeedId = Shader.PropertyToID("_Seed");
        private static readonly int RgbId = Shader.PropertyToID("_Rgb");
        private readonly Material material;

        internal NoiseProcessor(Shader shader)
        {
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        public void Process(Texture source, RenderTexture destination, TextureEffectData effect, ProcessingContext context)
        {
            var noise = (NoiseEffectData)effect;
            float previewScale = source.width / (float)Mathf.Max(1, context.OriginalWidth);
            int scaledNoise = Mathf.Max(1, Mathf.RoundToInt(noise.Scale * previewScale));
            material.SetVector(SourceSizeId, new Vector4(source.width, source.height, 0f, 0f));
            material.SetInt(NoiseTypeId, (int)noise.Type);
            material.SetFloat(AmountId, noise.Amount);
            material.SetInt(ScaleId, scaledNoise);
            material.SetInt(SeedId, noise.Seed);
            material.SetInt(RgbId, noise.Channels == DitherChannels.RGB ? 1 : 0);
            Graphics.Blit(source, destination, material, 0);
        }

        public void Dispose() => RuntimeObjectLifetime.Destroy(material);
    }

    internal sealed class GaussianBlurProcessor : ITextureEffectProcessor, IDisposable
    {
        private static readonly int SourceSizeId = Shader.PropertyToID("_SourceSize");
        private static readonly int DirectionId = Shader.PropertyToID("_Direction");
        private static readonly int RadiusId = Shader.PropertyToID("_Radius");
        private readonly Material material;

        internal GaussianBlurProcessor(Shader shader)
        {
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        public void Process(Texture source, RenderTexture destination, TextureEffectData effect, ProcessingContext context)
        {
            var blur = (GaussianBlurEffectData)effect;
            RenderTexture temporary = RenderTexture.GetTemporary(
                source.width,
                source.height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Default);
            temporary.filterMode = FilterMode.Bilinear;
            temporary.wrapMode = TextureWrapMode.Clamp;

            try
            {
                material.SetVector(SourceSizeId, new Vector4(source.width, source.height, 0f, 0f));
                material.SetFloat(RadiusId, blur.Radius);
                Texture current = source;
                int iterations = Mathf.Clamp(blur.Iterations, 1, 4);
                for (int i = 0; i < iterations; i++)
                {
                    material.SetVector(DirectionId, Vector2.right);
                    Graphics.Blit(current, temporary, material, 0);
                    material.SetVector(DirectionId, Vector2.up);
                    Graphics.Blit(temporary, destination, material, 0);
                    current = destination;
                }
            }
            finally
            {
                RenderTexture.ReleaseTemporary(temporary);
            }
        }

        public void Dispose() => RuntimeObjectLifetime.Destroy(material);
    }

    internal sealed class SeamlessProcessor : ITextureEffectProcessor, IDisposable
    {
        private static readonly int SourceSizeId = Shader.PropertyToID("_SourceSize");
        private static readonly int OffsetId = Shader.PropertyToID("_Offset");
        private static readonly int SettingsId = Shader.PropertyToID("_Settings");
        private static readonly int BlendAlphaId = Shader.PropertyToID("_BlendAlpha");
        private readonly Material material;

        internal SeamlessProcessor(Shader shader)
        {
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        public void Process(Texture source, RenderTexture destination, TextureEffectData effect, ProcessingContext context)
        {
            material.SetVector(SourceSizeId, new Vector4(source.width, source.height, 0f, 0f));
            switch (effect)
            {
                case OffsetEffectData offset:
                    material.SetVector(OffsetId, new Vector4(offset.OffsetX, offset.OffsetY, 0f, 0f));
                    material.SetVector(SettingsId, new Vector4(0f, 0f, offset.Wrap == OffsetWrapMode.Repeat ? 1f : 0f, 0f));
                    Graphics.Blit(source, destination, material, 0);
                    break;
                case SeamBlendEffectData seamBlend:
                    material.SetVector(SettingsId, new Vector4(
                        seamBlend.BlendWidth,
                        seamBlend.BlendStrength,
                        seamBlend.Horizontal ? 1f : 0f,
                        seamBlend.Vertical ? 1f : 0f));
                    material.SetFloat(BlendAlphaId, seamBlend.BlendAlpha ? 1f : 0f);
                    Graphics.Blit(source, destination, material, 1);
                    break;
            }
        }

        public void Dispose() => RuntimeObjectLifetime.Destroy(material);
    }

    internal sealed class ExposureBrushProcessor : ITextureEffectProcessor, IDisposable
    {
        private static readonly int SourceSizeId = Shader.PropertyToID("_SourceSize");
        private static readonly int ExposureMaskId = Shader.PropertyToID("_ExposureMask");
        private readonly BrushStrokeRasterizer rasterizer;
        private readonly Material material;

        internal ExposureBrushProcessor(Shader shader)
        {
            rasterizer = new BrushStrokeRasterizer(shader);
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        public void Process(Texture source, RenderTexture destination, TextureEffectData effect, ProcessingContext context)
        {
            var brush = (ExposureBrushEffectData)effect;
            RenderTexture mask = RenderTexture.GetTemporary(
                source.width,
                source.height,
                0,
                RenderTextureFormat.RHalf,
                RenderTextureReadWrite.Linear);
            mask.filterMode = FilterMode.Point;
            mask.wrapMode = TextureWrapMode.Clamp;

            try
            {
                rasterizer.Rasterize(mask, brush, context);
                material.SetVector(SourceSizeId, new Vector4(source.width, source.height, 0f, 0f));
                material.SetTexture(ExposureMaskId, mask);
                Graphics.Blit(source, destination, material, 1);
            }
            finally
            {
                RenderTexture.ReleaseTemporary(mask);
            }
        }

        public void Dispose()
        {
            rasterizer.Dispose();
            RuntimeObjectLifetime.Destroy(material);
        }
    }

    internal sealed class ChannelMixerProcessor : ITextureEffectProcessor, IDisposable
    {
        private static readonly int SourceSizeId = Shader.PropertyToID("_SourceSize");
        private static readonly int RedOutputId = Shader.PropertyToID("_RedOutput");
        private static readonly int GreenOutputId = Shader.PropertyToID("_GreenOutput");
        private static readonly int BlueOutputId = Shader.PropertyToID("_BlueOutput");
        private static readonly int ConstantsId = Shader.PropertyToID("_Constants");
        private static readonly int MixerSettingsId = Shader.PropertyToID("_MixerSettings");
        private static readonly int MonochromeMixId = Shader.PropertyToID("_MonochromeMix");
        private static readonly int AlphaOutputId = Shader.PropertyToID("_AlphaOutput");
        private readonly Material material;

        internal ChannelMixerProcessor(Shader shader)
        {
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        public void Process(Texture source, RenderTexture destination, TextureEffectData effect, ProcessingContext context)
        {
            var mixer = (ChannelRemapEffectData)effect;
            material.SetVector(SourceSizeId, new Vector4(source.width, source.height, 0f, 0f));
            material.SetVector(RedOutputId, mixer.RedOutput);
            material.SetVector(GreenOutputId, mixer.GreenOutput);
            material.SetVector(BlueOutputId, mixer.BlueOutput);
            material.SetVector(ConstantsId, new Vector4(
                mixer.Constants.x,
                mixer.Constants.y,
                mixer.Constants.z,
                mixer.MonochromeConstant));
            material.SetVector(MixerSettingsId, new Vector4(
                mixer.Strength,
                mixer.Monochrome ? 1f : 0f,
                mixer.AlphaMode == ChannelMixerAlphaMode.Mix ? 1f : 0f,
                mixer.AlphaConstant));
            material.SetVector(MonochromeMixId, mixer.MonochromeMix);
            material.SetVector(AlphaOutputId, mixer.AlphaOutput);
            Graphics.Blit(source, destination, material, 0);
        }

        public void Dispose() => RuntimeObjectLifetime.Destroy(material);
    }
}
