using System;
using System.Collections.Generic;
using UnityEngine;

namespace TextureLab.Runtime
{
    public sealed class TextureLabEffectDescriptor
    {
        internal TextureLabEffectDescriptor(string effectType, string displayName, int dataVersion)
        {
            EffectType = effectType;
            DisplayName = displayName;
            DataVersion = dataVersion;
        }

        public string EffectType { get; }
        public string DisplayName { get; }
        public int DataVersion { get; }
    }

    /// <summary>
    /// Explicit, AOT-safe map between portable effect IDs and their codecs.
    /// No CLR type discovery or assembly-qualified names are used here.
    /// </summary>
    public static class TextureLabEffectRegistry
    {
        public const string Pixelate = "texturelab.pixelate";
        public const string Posterize = "texturelab.posterize";
        public const string Levels = "texturelab.levels";
        public const string ColorAdjustments = "texturelab.color-adjustments";
        public const string ColorReplace = "texturelab.color-replace";
        public const string PaletteQuantize = "texturelab.palette-quantize";
        public const string Dither = "texturelab.dither";
        public const string Noise = "texturelab.noise";
        public const string GaussianBlur = "texturelab.gaussian-blur";
        public const string ExposureBrush = "texturelab.exposure-brush";
        public const string Offset = "texturelab.offset";
        public const string SeamBlend = "texturelab.seam-blend";
        public const string ChannelMixer = "texturelab.channel-mixer";

        private sealed class EffectDefinition
        {
            internal readonly TextureLabEffectDescriptor Descriptor;
            internal readonly Func<TextureEffectData, string> Serialize;
            internal readonly Func<string, TextureEffectData> Deserialize;
            internal readonly Action<TextureEffectData, TextureLabValidationResult> Validate;
            internal readonly Func<TextureLabShaderLibrary, ITextureEffectProcessor> CreateProcessor;

            internal EffectDefinition(
                string effectType,
                string displayName,
                Func<TextureEffectData, string> serialize,
                Func<string, TextureEffectData> deserialize,
                Action<TextureEffectData, TextureLabValidationResult> validate,
                Func<TextureLabShaderLibrary, ITextureEffectProcessor> createProcessor)
            {
                Descriptor = new TextureLabEffectDescriptor(effectType, displayName, TextureLabSchema.EffectDataVersion);
                Serialize = serialize;
                Deserialize = deserialize;
                Validate = validate;
                CreateProcessor = createProcessor;
            }
        }

        private static readonly Dictionary<string, EffectDefinition> definitions = BuildDefinitions();
        private static readonly List<TextureLabEffectDescriptor> descriptors = BuildDescriptors();

        public static IReadOnlyList<TextureLabEffectDescriptor> Descriptors => descriptors;

        public static bool TryGetDescriptor(string effectType, out TextureLabEffectDescriptor descriptor)
        {
            if (effectType != null && definitions.TryGetValue(effectType, out EffectDefinition definition))
            {
                descriptor = definition.Descriptor;
                return true;
            }

            descriptor = null;
            return false;
        }

        internal static ITextureEffectProcessor CreateProcessor(string effectType, TextureLabShaderLibrary shaderLibrary)
        {
            if (shaderLibrary == null)
                throw new ArgumentNullException(nameof(shaderLibrary));
            if (effectType == null || !definitions.TryGetValue(effectType, out EffectDefinition definition))
                throw new InvalidOperationException($"Effect type '{effectType}' is not registered.");
            return definition.CreateProcessor(shaderLibrary);
        }

        internal static bool TryGetEffectType(TextureEffectData effect, out string effectType)
        {
            if (TryGetDefinition(effect, out EffectDefinition definition))
            {
                effectType = definition.Descriptor.EffectType;
                return true;
            }

            effectType = null;
            return false;
        }

        internal static bool TrySerialize(
            TextureEffectData effect,
            out TextureLabEffectRecord record,
            out TextureLabValidationResult validation)
        {
            record = null;
            validation = new TextureLabValidationResult();
            if (!TryGetDefinition(effect, out EffectDefinition definition))
            {
                validation.Add("effect", "The effect type is not registered.");
                return false;
            }

            definition.Validate(effect, validation);
            if (!validation.IsValid)
                return false;

            try
            {
                record = new TextureLabEffectRecord
                {
                    id = effect.Id,
                    effectType = definition.Descriptor.EffectType,
                    dataVersion = definition.Descriptor.DataVersion,
                    enabled = effect.Enabled,
                    allowRandomize = effect.AllowRandomize,
                    parametersJson = definition.Serialize(effect)
                };
                return true;
            }
            catch (Exception exception)
            {
                validation.Add("effect", $"Could not serialize the effect: {exception.Message}");
                return false;
            }
        }

        internal static bool TryDeserialize(
            TextureLabEffectRecord record,
            out TextureEffectData effect,
            out TextureLabValidationResult validation)
        {
            effect = null;
            validation = new TextureLabValidationResult();
            ValidateRecord(record, validation);
            if (!validation.IsValid || !definitions.TryGetValue(record.effectType, out EffectDefinition definition))
                return false;

            try
            {
                effect = definition.Deserialize(record.parametersJson);
                effect.ApplyPortableState(record.id, record.enabled, record.allowRandomize);
                definition.Validate(effect, validation);
                if (!validation.IsValid)
                {
                    effect = null;
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                validation.Add("parameters", $"Could not deserialize the effect: {exception.Message}");
                effect = null;
                return false;
            }
        }

        internal static void ValidateRecord(TextureLabEffectRecord record, TextureLabValidationResult validation)
        {
            if (record == null)
            {
                validation.Add("effect", "The effect record is missing.");
                return;
            }

            ValidateId(record.id, "effect.id", validation);
            if (string.IsNullOrEmpty(record.effectType))
                validation.Add("effect.effectType", "An effect type is required.");
            else if (!definitions.TryGetValue(record.effectType, out EffectDefinition definition))
                validation.Add("effect.effectType", $"Unknown effect type '{record.effectType}'.");
            else if (record.dataVersion != definition.Descriptor.DataVersion)
                validation.Add("effect.dataVersion", $"Version {record.dataVersion} is not supported for '{record.effectType}'.");

            if (string.IsNullOrEmpty(record.parametersJson))
                validation.Add("effect.parametersJson", "Effect parameters are required.");
            else if (record.parametersJson.Length > TextureLabSchema.MaxJsonBytes)
                validation.Add("effect.parametersJson", "Effect parameters exceed the size limit.");
        }

        private static Dictionary<string, EffectDefinition> BuildDefinitions()
        {
            return new Dictionary<string, EffectDefinition>(StringComparer.Ordinal)
            {
                [Pixelate] = new EffectDefinition(Pixelate, "Pixelate", SerializePixelate, DeserializePixelate, ValidatePixelate, CreatePixelateProcessor),
                [Posterize] = new EffectDefinition(Posterize, "Posterize", SerializePosterize, DeserializePosterize, ValidatePosterize, CreateColorProcessor),
                [Levels] = new EffectDefinition(Levels, "Levels", SerializeLevels, DeserializeLevels, ValidateLevels, CreateColorProcessor),
                [ColorAdjustments] = new EffectDefinition(ColorAdjustments, "Color Adjustments", SerializeColorAdjustments, DeserializeColorAdjustments, ValidateColorAdjustments, CreateColorProcessor),
                [ColorReplace] = new EffectDefinition(ColorReplace, "Color Replace", SerializeColorReplace, DeserializeColorReplace, ValidateColorReplace, CreateColorProcessor),
                [PaletteQuantize] = new EffectDefinition(PaletteQuantize, "Palette Quantization", SerializePaletteQuantize, DeserializePaletteQuantize, ValidatePaletteQuantize, CreatePaletteQuantizeProcessor),
                [Dither] = new EffectDefinition(Dither, "Dither", SerializeDither, DeserializeDither, ValidateDither, CreateDitherProcessor),
                [Noise] = new EffectDefinition(Noise, "Noise", SerializeNoise, DeserializeNoise, ValidateNoise, CreateNoiseProcessor),
                [GaussianBlur] = new EffectDefinition(GaussianBlur, "Gaussian Blur", SerializeGaussianBlur, DeserializeGaussianBlur, ValidateGaussianBlur, CreateGaussianBlurProcessor),
                [ExposureBrush] = new EffectDefinition(ExposureBrush, "Dodge / Burn Brush", SerializeExposureBrush, DeserializeExposureBrush, ValidateExposureBrush, CreateExposureBrushProcessor),
                [Offset] = new EffectDefinition(Offset, "Offset", SerializeOffset, DeserializeOffset, ValidateOffset, CreateSeamlessProcessor),
                [SeamBlend] = new EffectDefinition(SeamBlend, "Seam Blend", SerializeSeamBlend, DeserializeSeamBlend, ValidateSeamBlend, CreateSeamlessProcessor),
                [ChannelMixer] = new EffectDefinition(ChannelMixer, "Channel Mixer", SerializeChannelMixer, DeserializeChannelMixer, ValidateChannelMixer, CreateChannelMixerProcessor)
            };
        }

        private static ITextureEffectProcessor CreatePixelateProcessor(TextureLabShaderLibrary library) => new PixelateProcessor(library.Pixelate);
        private static ITextureEffectProcessor CreateColorProcessor(TextureLabShaderLibrary library) => new ColorEffectProcessor(library.Color);
        private static ITextureEffectProcessor CreatePaletteQuantizeProcessor(TextureLabShaderLibrary library) => new PaletteQuantizeProcessor(library.PaletteQuantize, null);
        private static ITextureEffectProcessor CreateDitherProcessor(TextureLabShaderLibrary library) => new DitherProcessor(library.Dither);
        private static ITextureEffectProcessor CreateNoiseProcessor(TextureLabShaderLibrary library) => new NoiseProcessor(library.Noise);
        private static ITextureEffectProcessor CreateGaussianBlurProcessor(TextureLabShaderLibrary library) => new GaussianBlurProcessor(library.Blur);
        private static ITextureEffectProcessor CreateSeamlessProcessor(TextureLabShaderLibrary library) => new SeamlessProcessor(library.Seamless);
        private static ITextureEffectProcessor CreateExposureBrushProcessor(TextureLabShaderLibrary library) => new ExposureBrushProcessor(library.BrushExposure);
        private static ITextureEffectProcessor CreateChannelMixerProcessor(TextureLabShaderLibrary library) => new ChannelMixerProcessor(library.Channels);

        private static List<TextureLabEffectDescriptor> BuildDescriptors()
        {
            var result = new List<TextureLabEffectDescriptor>(definitions.Count);
            foreach (EffectDefinition definition in definitions.Values)
                result.Add(definition.Descriptor);
            result.Sort((left, right) => string.CompareOrdinal(left.EffectType, right.EffectType));
            return result;
        }

        private static bool TryGetDefinition(TextureEffectData effect, out EffectDefinition definition)
        {
            definition = null;
            string effectType = effect switch
            {
                PixelateEffectData _ => Pixelate,
                PosterizeEffectData _ => Posterize,
                LevelsEffectData _ => Levels,
                ColorAdjustmentsEffectData _ => ColorAdjustments,
                ColorReplaceEffectData _ => ColorReplace,
                PaletteQuantizeEffectData _ => PaletteQuantize,
                DitherEffectData _ => Dither,
                NoiseEffectData _ => Noise,
                GaussianBlurEffectData _ => GaussianBlur,
                ExposureBrushEffectData _ => ExposureBrush,
                OffsetEffectData _ => Offset,
                SeamBlendEffectData _ => SeamBlend,
                ChannelRemapEffectData _ => ChannelMixer,
                _ => null
            };

            return effectType != null && definitions.TryGetValue(effectType, out definition);
        }

        private static T Parse<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json))
                throw new ArgumentException("JSON is empty.");

            T value = JsonUtility.FromJson<T>(json);
            return value ?? throw new ArgumentException("JSON did not contain an object.");
        }

        private static string Write<T>(T value) => JsonUtility.ToJson(value, false);

        private static void ApplyCommon(TextureEffectData effect, int version)
        {
            if (version != TextureLabSchema.EffectDataVersion)
                throw new ArgumentException($"Unsupported effect parameter version {version}.");
        }

        private static string SerializePixelate(TextureEffectData data)
        {
            var effect = (PixelateEffectData)data;
            return Write(new PixelateParameters
            {
                version = TextureLabSchema.EffectDataVersion,
                mode = (int)effect.Mode,
                blockSize = effect.BlockSize,
                targetResolution = effect.TargetResolution,
                sampling = (int)effect.Sampling
            });
        }

        private static TextureEffectData DeserializePixelate(string json)
        {
            PixelateParameters value = Parse<PixelateParameters>(json);
            ApplyCommon(null, value.version);
            return new PixelateEffectData
            {
                Mode = (PixelateMode)value.mode,
                BlockSize = value.blockSize,
                TargetResolution = value.targetResolution,
                Sampling = (PixelSampling)value.sampling
            };
        }

        private static string SerializePosterize(TextureEffectData data)
        {
            var effect = (PosterizeEffectData)data;
            return Write(new PosterizeParameters
            {
                version = TextureLabSchema.EffectDataVersion,
                redBits = effect.RedBits,
                greenBits = effect.GreenBits,
                blueBits = effect.BlueBits
            });
        }

        private static TextureEffectData DeserializePosterize(string json)
        {
            PosterizeParameters value = Parse<PosterizeParameters>(json);
            ApplyCommon(null, value.version);
            return new PosterizeEffectData
            {
                RedBits = value.redBits,
                GreenBits = value.greenBits,
                BlueBits = value.blueBits
            };
        }

        private static string SerializeLevels(TextureEffectData data)
        {
            var effect = (LevelsEffectData)data;
            return Write(new LevelsParameters
            {
                version = TextureLabSchema.EffectDataVersion,
                blackPoint = effect.BlackPoint,
                whitePoint = effect.WhitePoint,
                gamma = effect.Gamma,
                outputBlack = effect.OutputBlack,
                outputWhite = effect.OutputWhite
            });
        }

        private static TextureEffectData DeserializeLevels(string json)
        {
            LevelsParameters value = Parse<LevelsParameters>(json);
            ApplyCommon(null, value.version);
            return new LevelsEffectData
            {
                BlackPoint = value.blackPoint,
                WhitePoint = value.whitePoint,
                Gamma = value.gamma,
                OutputBlack = value.outputBlack,
                OutputWhite = value.outputWhite
            };
        }

        private static string SerializeColorAdjustments(TextureEffectData data)
        {
            var effect = (ColorAdjustmentsEffectData)data;
            return Write(new ColorAdjustmentsParameters
            {
                version = TextureLabSchema.EffectDataVersion,
                brightness = effect.Brightness,
                contrast = effect.Contrast,
                gamma = effect.Gamma
            });
        }

        private static TextureEffectData DeserializeColorAdjustments(string json)
        {
            ColorAdjustmentsParameters value = Parse<ColorAdjustmentsParameters>(json);
            ApplyCommon(null, value.version);
            return new ColorAdjustmentsEffectData
            {
                Brightness = value.brightness,
                Contrast = value.contrast,
                Gamma = value.gamma
            };
        }

        private static string SerializeColorReplace(TextureEffectData data)
        {
            var effect = (ColorReplaceEffectData)data;
            return Write(new ColorReplaceParameters
            {
                version = TextureLabSchema.EffectDataVersion,
                sourceColor = effect.SourceColor,
                replacementColor = effect.ReplacementColor,
                tolerance = effect.Tolerance,
                softness = effect.Softness,
                previewMask = effect.PreviewMask
            });
        }

        private static TextureEffectData DeserializeColorReplace(string json)
        {
            ColorReplaceParameters value = Parse<ColorReplaceParameters>(json);
            ApplyCommon(null, value.version);
            return new ColorReplaceEffectData
            {
                SourceColor = value.sourceColor,
                ReplacementColor = value.replacementColor,
                Tolerance = value.tolerance,
                Softness = value.softness,
                PreviewMask = value.previewMask
            };
        }

        private static string SerializePaletteQuantize(TextureEffectData data)
        {
            var effect = (PaletteQuantizeEffectData)data;
            return Write(new PaletteQuantizeParameters
            {
                version = TextureLabSchema.EffectDataVersion,
                paletteId = effect.Palette != null ? effect.Palette.Id : effect.PaletteId,
                colorLimit = effect.ColorLimit,
                extractionColorCount = effect.ExtractionColorCount
            });
        }

        private static TextureEffectData DeserializePaletteQuantize(string json)
        {
            PaletteQuantizeParameters value = Parse<PaletteQuantizeParameters>(json);
            ApplyCommon(null, value.version);
            return new PaletteQuantizeEffectData
            {
                PaletteId = value.paletteId,
                ColorLimit = value.colorLimit,
                ExtractionColorCount = value.extractionColorCount
            };
        }

        private static string SerializeDither(TextureEffectData data)
        {
            var effect = (DitherEffectData)data;
            return Write(new DitherParameters
            {
                version = TextureLabSchema.EffectDataVersion,
                pattern = (int)effect.Pattern,
                strength = effect.Strength,
                scale = effect.Scale,
                seed = effect.Seed,
                channels = (int)effect.Channels
            });
        }

        private static TextureEffectData DeserializeDither(string json)
        {
            DitherParameters value = Parse<DitherParameters>(json);
            ApplyCommon(null, value.version);
            return new DitherEffectData
            {
                Pattern = (DitherPattern)value.pattern,
                Strength = value.strength,
                Scale = value.scale,
                Seed = value.seed,
                Channels = (DitherChannels)value.channels
            };
        }

        private static string SerializeNoise(TextureEffectData data)
        {
            var effect = (NoiseEffectData)data;
            return Write(new NoiseParameters
            {
                version = TextureLabSchema.EffectDataVersion,
                type = (int)effect.Type,
                amount = effect.Amount,
                scale = effect.Scale,
                seed = effect.Seed,
                channels = (int)effect.Channels
            });
        }

        private static TextureEffectData DeserializeNoise(string json)
        {
            NoiseParameters value = Parse<NoiseParameters>(json);
            ApplyCommon(null, value.version);
            return new NoiseEffectData
            {
                Type = (NoiseType)value.type,
                Amount = value.amount,
                Scale = value.scale,
                Seed = value.seed,
                Channels = (DitherChannels)value.channels
            };
        }

        private static string SerializeGaussianBlur(TextureEffectData data)
        {
            var effect = (GaussianBlurEffectData)data;
            return Write(new GaussianBlurParameters
            {
                version = TextureLabSchema.EffectDataVersion,
                radius = effect.Radius,
                iterations = effect.Iterations
            });
        }

        private static TextureEffectData DeserializeGaussianBlur(string json)
        {
            GaussianBlurParameters value = Parse<GaussianBlurParameters>(json);
            ApplyCommon(null, value.version);
            return new GaussianBlurEffectData
            {
                Radius = value.radius,
                Iterations = value.iterations
            };
        }

        private static string SerializeExposureBrush(TextureEffectData data)
        {
            var effect = (ExposureBrushEffectData)data;
            var parameters = new ExposureBrushParameters
            {
                version = TextureLabSchema.EffectDataVersion,
                brushSize = effect.BrushSize,
                brushHardness = effect.BrushHardness,
                brushExposure = effect.BrushExposure,
                brushMode = (int)effect.BrushMode,
                wrap = (int)effect.Wrap,
                strokes = new List<BrushStrokeParameters>()
            };

            foreach (BrushStroke stroke in effect.Strokes)
            {
                parameters.strokes.Add(new BrushStrokeParameters
                {
                    mode = (int)stroke.Mode,
                    size = stroke.Size,
                    hardness = stroke.Hardness,
                    exposure = stroke.Exposure,
                    points = new List<Vector2>(stroke.Points)
                });
            }

            return Write(parameters);
        }

        private static TextureEffectData DeserializeExposureBrush(string json)
        {
            ExposureBrushParameters value = Parse<ExposureBrushParameters>(json);
            ApplyCommon(null, value.version);
            var effect = new ExposureBrushEffectData
            {
                BrushSize = value.brushSize,
                BrushHardness = value.brushHardness,
                BrushExposure = value.brushExposure,
                BrushMode = (ExposureBrushMode)value.brushMode,
                Wrap = (OffsetWrapMode)value.wrap
            };

            if (value.strokes != null)
            {
                foreach (BrushStrokeParameters stroke in value.strokes)
                {
                    var restored = new BrushStroke(
                        (ExposureBrushMode)stroke.mode,
                        stroke.size,
                        stroke.hardness,
                        stroke.exposure);
                    if (stroke.points != null)
                        restored.Points.AddRange(stroke.points);
                    effect.Strokes.Add(restored);
                }
            }

            return effect;
        }

        private static string SerializeOffset(TextureEffectData data)
        {
            var effect = (OffsetEffectData)data;
            return Write(new OffsetParameters
            {
                version = TextureLabSchema.EffectDataVersion,
                offsetX = effect.OffsetX,
                offsetY = effect.OffsetY,
                wrap = (int)effect.Wrap
            });
        }

        private static TextureEffectData DeserializeOffset(string json)
        {
            OffsetParameters value = Parse<OffsetParameters>(json);
            ApplyCommon(null, value.version);
            return new OffsetEffectData
            {
                OffsetX = value.offsetX,
                OffsetY = value.offsetY,
                Wrap = (OffsetWrapMode)value.wrap
            };
        }

        private static string SerializeSeamBlend(TextureEffectData data)
        {
            var effect = (SeamBlendEffectData)data;
            return Write(new SeamBlendParameters
            {
                version = TextureLabSchema.EffectDataVersion,
                blendWidth = effect.BlendWidth,
                blendStrength = effect.BlendStrength,
                horizontal = effect.Horizontal,
                vertical = effect.Vertical,
                blendAlpha = effect.BlendAlpha
            });
        }

        private static TextureEffectData DeserializeSeamBlend(string json)
        {
            SeamBlendParameters value = Parse<SeamBlendParameters>(json);
            ApplyCommon(null, value.version);
            return new SeamBlendEffectData
            {
                BlendWidth = value.blendWidth,
                BlendStrength = value.blendStrength,
                Horizontal = value.horizontal,
                Vertical = value.vertical,
                BlendAlpha = value.blendAlpha
            };
        }

        private static string SerializeChannelMixer(TextureEffectData data)
        {
            var effect = (ChannelRemapEffectData)data;
            return Write(new ChannelMixerParameters
            {
                version = TextureLabSchema.EffectDataVersion,
                redOutput = effect.RedOutput,
                greenOutput = effect.GreenOutput,
                blueOutput = effect.BlueOutput,
                constants = effect.Constants,
                strength = effect.Strength,
                monochrome = effect.Monochrome,
                monochromeMix = effect.MonochromeMix,
                monochromeConstant = effect.MonochromeConstant,
                alphaMode = (int)effect.AlphaMode,
                alphaOutput = effect.AlphaOutput,
                alphaConstant = effect.AlphaConstant
            });
        }

        private static TextureEffectData DeserializeChannelMixer(string json)
        {
            ChannelMixerParameters value = Parse<ChannelMixerParameters>(json);
            ApplyCommon(null, value.version);
            return new ChannelRemapEffectData
            {
                RedOutput = value.redOutput,
                GreenOutput = value.greenOutput,
                BlueOutput = value.blueOutput,
                Constants = value.constants,
                Strength = value.strength,
                Monochrome = value.monochrome,
                MonochromeMix = value.monochromeMix,
                MonochromeConstant = value.monochromeConstant,
                AlphaMode = (ChannelMixerAlphaMode)value.alphaMode,
                AlphaOutput = value.alphaOutput,
                AlphaConstant = value.alphaConstant
            };
        }

        private static void ValidatePixelate(TextureEffectData data, TextureLabValidationResult result)
        {
            var effect = (PixelateEffectData)data;
            AddParameterVersion(effect, result);
            EnumRange((int)effect.Mode, 0, 1, "mode", result);
            Range(effect.BlockSize, 1, 64, "blockSize", result);
            Range(effect.TargetResolution, 16, 2048, "targetResolution", result);
            EnumRange((int)effect.Sampling, 0, 1, "sampling", result);
        }

        private static void ValidatePosterize(TextureEffectData data, TextureLabValidationResult result)
        {
            var effect = (PosterizeEffectData)data;
            AddCommon(effect, result);
            Range(effect.RedBits, 1, 8, "redBits", result);
            Range(effect.GreenBits, 1, 8, "greenBits", result);
            Range(effect.BlueBits, 1, 8, "blueBits", result);
        }

        private static void ValidateLevels(TextureEffectData data, TextureLabValidationResult result)
        {
            var effect = (LevelsEffectData)data;
            AddCommon(effect, result);
            Range(effect.BlackPoint, 0f, 1f, "blackPoint", result);
            Range(effect.WhitePoint, 0f, 1f, "whitePoint", result);
            Range(effect.Gamma, 0.1f, 4f, "gamma", result);
            Range(effect.OutputBlack, 0f, 1f, "outputBlack", result);
            Range(effect.OutputWhite, 0f, 1f, "outputWhite", result);
            if (effect.WhitePoint <= effect.BlackPoint)
                result.Add("parameters.whitePoint", "White point must be greater than black point.");
            if (effect.OutputWhite < effect.OutputBlack)
                result.Add("parameters.outputWhite", "Output white must not be below output black.");
        }

        private static void ValidateColorAdjustments(TextureEffectData data, TextureLabValidationResult result)
        {
            var effect = (ColorAdjustmentsEffectData)data;
            AddCommon(effect, result);
            Range(effect.Brightness, -1f, 1f, "brightness", result);
            Range(effect.Contrast, -1f, 1f, "contrast", result);
            Range(effect.Gamma, 0.1f, 4f, "gamma", result);
        }

        private static void ValidateColorReplace(TextureEffectData data, TextureLabValidationResult result)
        {
            var effect = (ColorReplaceEffectData)data;
            AddCommon(effect, result);
            ColorValue(effect.SourceColor, "sourceColor", result);
            ColorValue(effect.ReplacementColor, "replacementColor", result);
            Range(effect.Tolerance, 0f, 1.7320508f, "tolerance", result);
            Range(effect.Softness, 0f, 1.7320508f, "softness", result);
        }

        private static void ValidatePaletteQuantize(TextureEffectData data, TextureLabValidationResult result)
        {
            var effect = (PaletteQuantizeEffectData)data;
            AddCommon(effect, result);
            string paletteId = effect.Palette != null ? effect.Palette.Id : effect.PaletteId;
            ValidateId(paletteId, "parameters.paletteId", result);
            Range(effect.ColorLimit, 0, TextureLabSchema.MaxPaletteColors, "colorLimit", result);
            Range(effect.ExtractionColorCount, 1, TextureLabSchema.MaxPaletteColors, "extractionColorCount", result);
        }

        private static void ValidateDither(TextureEffectData data, TextureLabValidationResult result)
        {
            var effect = (DitherEffectData)data;
            AddCommon(effect, result);
            EnumRange((int)effect.Pattern, 0, 3, "pattern", result);
            Range(effect.Strength, 0f, 1f, "strength", result);
            Range(effect.Scale, 1, 8, "scale", result);
            EnumRange((int)effect.Channels, 0, 1, "channels", result);
        }

        private static void ValidateNoise(TextureEffectData data, TextureLabValidationResult result)
        {
            var effect = (NoiseEffectData)data;
            AddCommon(effect, result);
            EnumRange((int)effect.Type, 0, 2, "type", result);
            Range(effect.Amount, 0f, 1f, "amount", result);
            Range(effect.Scale, 1, 64, "scale", result);
            EnumRange((int)effect.Channels, 0, 1, "channels", result);
        }

        private static void ValidateGaussianBlur(TextureEffectData data, TextureLabValidationResult result)
        {
            var effect = (GaussianBlurEffectData)data;
            AddCommon(effect, result);
            Range(effect.Radius, 0f, 32f, "radius", result);
            Range(effect.Iterations, 1, 4, "iterations", result);
        }

        private static void ValidateExposureBrush(TextureEffectData data, TextureLabValidationResult result)
        {
            var effect = (ExposureBrushEffectData)data;
            AddCommon(effect, result);
            Range(effect.BrushSize, 1f, 512f, "brushSize", result);
            Range(effect.BrushHardness, 0f, 1f, "brushHardness", result);
            Range(effect.BrushExposure, 0.01f, 2f, "brushExposure", result);
            EnumRange((int)effect.BrushMode, 0, 1, "brushMode", result);
            EnumRange((int)effect.Wrap, 0, 1, "wrap", result);
            if (effect.Strokes.Count > TextureLabSchema.MaxBrushStrokes)
                result.Add("parameters.strokes", "Too many brush strokes.");

            int totalPoints = 0;
            for (int strokeIndex = 0; strokeIndex < effect.Strokes.Count; strokeIndex++)
            {
                BrushStroke stroke = effect.Strokes[strokeIndex];
                Range(stroke.Size, 1f, 512f, $"strokes[{strokeIndex}].size", result);
                Range(stroke.Hardness, 0f, 1f, $"strokes[{strokeIndex}].hardness", result);
                Range(stroke.Exposure, 0.01f, 2f, $"strokes[{strokeIndex}].exposure", result);
                EnumRange((int)stroke.Mode, 0, 1, $"strokes[{strokeIndex}].mode", result);
                if (stroke.Points.Count > TextureLabSchema.MaxBrushPointsPerStroke)
                    result.Add($"parameters.strokes[{strokeIndex}].points", "Too many points in brush stroke.");

                totalPoints += stroke.Points.Count;
                for (int pointIndex = 0; pointIndex < stroke.Points.Count; pointIndex++)
                {
                    Vector2 point = stroke.Points[pointIndex];
                    Range(point.x, 0f, 1f, $"strokes[{strokeIndex}].points[{pointIndex}].x", result);
                    Range(point.y, 0f, 1f, $"strokes[{strokeIndex}].points[{pointIndex}].y", result);
                }
            }

            if (totalPoints > TextureLabSchema.MaxBrushPointsTotal)
                result.Add("parameters.strokes", "The total number of brush points is too large.");
        }

        private static void ValidateOffset(TextureEffectData data, TextureLabValidationResult result)
        {
            var effect = (OffsetEffectData)data;
            AddCommon(effect, result);
            Range(effect.OffsetX, 0f, 1f, "offsetX", result);
            Range(effect.OffsetY, 0f, 1f, "offsetY", result);
            EnumRange((int)effect.Wrap, 0, 1, "wrap", result);
        }

        private static void ValidateSeamBlend(TextureEffectData data, TextureLabValidationResult result)
        {
            var effect = (SeamBlendEffectData)data;
            AddCommon(effect, result);
            Range(effect.BlendWidth, 0f, 0.5f, "blendWidth", result);
            Range(effect.BlendStrength, 0f, 1f, "blendStrength", result);
        }

        private static void ValidateChannelMixer(TextureEffectData data, TextureLabValidationResult result)
        {
            var effect = (ChannelRemapEffectData)data;
            AddCommon(effect, result);
            VectorValue(effect.RedOutput, -2f, 2f, "redOutput", result);
            VectorValue(effect.GreenOutput, -2f, 2f, "greenOutput", result);
            VectorValue(effect.BlueOutput, -2f, 2f, "blueOutput", result);
            VectorValue(effect.Constants, -1f, 1f, "constants", result);
            Range(effect.Strength, 0f, 1f, "strength", result);
            VectorValue(effect.MonochromeMix, -2f, 2f, "monochromeMix", result);
            Range(effect.MonochromeConstant, -1f, 1f, "monochromeConstant", result);
            EnumRange((int)effect.AlphaMode, 0, 1, "alphaMode", result);
            VectorValue(effect.AlphaOutput, -2f, 2f, "alphaOutput", result);
            Range(effect.AlphaConstant, -1f, 1f, "alphaConstant", result);
        }

        private static void AddCommon(TextureEffectData effect, TextureLabValidationResult result)
        {
            ValidateId(effect.Id, "effect.id", result);
        }

        private static void AddParameterVersion(TextureEffectData effect, TextureLabValidationResult result)
        {
            AddCommon(effect, result);
        }

        private static void ValidateId(string value, string path, TextureLabValidationResult result)
        {
            if (string.IsNullOrEmpty(value))
            {
                result.Add(path, "An ID is required.");
                return;
            }

            if (value.Length > 128)
                result.Add(path, "The ID is too long.");

            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (char.IsControl(character) || char.IsWhiteSpace(character))
                {
                    result.Add(path, "The ID contains whitespace or a control character.");
                    break;
                }
            }
        }

        private static void EnumRange(int value, int min, int max, string path, TextureLabValidationResult result)
        {
            if (value < min || value > max)
                result.Add($"parameters.{path}", $"Value must be between {min} and {max}.");
        }

        private static void Range(int value, int min, int max, string path, TextureLabValidationResult result)
        {
            if (value < min || value > max)
                result.Add($"parameters.{path}", $"Value must be between {min} and {max}.");
        }

        private static void Range(float value, float min, float max, string path, TextureLabValidationResult result)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < min || value > max)
                result.Add($"parameters.{path}", $"Value must be finite and between {min} and {max}.");
        }

        private static void ColorValue(Color value, string path, TextureLabValidationResult result)
        {
            if (!Finite(value.r) || !Finite(value.g) || !Finite(value.b) || !Finite(value.a))
                result.Add($"parameters.{path}", "Color components must be finite.");
        }

        private static void VectorValue(Vector3 value, float min, float max, string path, TextureLabValidationResult result)
        {
            Range(value.x, min, max, $"{path}.x", result);
            Range(value.y, min, max, $"{path}.y", result);
            Range(value.z, min, max, $"{path}.z", result);
        }

        private static void VectorValue(Vector4 value, float min, float max, string path, TextureLabValidationResult result)
        {
            Range(value.x, min, max, $"{path}.x", result);
            Range(value.y, min, max, $"{path}.y", result);
            Range(value.z, min, max, $"{path}.z", result);
            Range(value.w, min, max, $"{path}.w", result);
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        [Serializable] private sealed class PixelateParameters { public int version; public int mode; public int blockSize; public int targetResolution; public int sampling; }
        [Serializable] private sealed class PosterizeParameters { public int version; public int redBits; public int greenBits; public int blueBits; }
        [Serializable] private sealed class LevelsParameters { public int version; public float blackPoint; public float whitePoint; public float gamma; public float outputBlack; public float outputWhite; }
        [Serializable] private sealed class ColorAdjustmentsParameters { public int version; public float brightness; public float contrast; public float gamma; }
        [Serializable] private sealed class ColorReplaceParameters { public int version; public Color sourceColor; public Color replacementColor; public float tolerance; public float softness; public bool previewMask; }
        [Serializable] private sealed class PaletteQuantizeParameters { public int version; public string paletteId; public int colorLimit; public int extractionColorCount; }
        [Serializable] private sealed class DitherParameters { public int version; public int pattern; public float strength; public int scale; public int seed; public int channels; }
        [Serializable] private sealed class NoiseParameters { public int version; public int type; public float amount; public int scale; public int seed; public int channels; }
        [Serializable] private sealed class GaussianBlurParameters { public int version; public float radius; public int iterations; }
        [Serializable] private sealed class ExposureBrushParameters { public int version; public float brushSize; public float brushHardness; public float brushExposure; public int brushMode; public int wrap; public List<BrushStrokeParameters> strokes; }
        [Serializable] private sealed class BrushStrokeParameters { public int mode; public float size; public float hardness; public float exposure; public List<Vector2> points; }
        [Serializable] private sealed class OffsetParameters { public int version; public float offsetX; public float offsetY; public int wrap; }
        [Serializable] private sealed class SeamBlendParameters { public int version; public float blendWidth; public float blendStrength; public bool horizontal; public bool vertical; public bool blendAlpha; }
        [Serializable] private sealed class ChannelMixerParameters { public int version; public Vector3 redOutput; public Vector3 greenOutput; public Vector3 blueOutput; public Vector3 constants; public float strength; public bool monochrome; public Vector3 monochromeMix; public float monochromeConstant; public int alphaMode; public Vector4 alphaOutput; public float alphaConstant; }
    }
}
