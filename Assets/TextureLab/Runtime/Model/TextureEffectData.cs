using System;
using System.Collections.Generic;
using UnityEngine;

namespace TextureLab.Runtime
{
    internal enum PixelateMode
    {
        BlockSize,
        TargetResolution
    }

    internal enum PixelSampling
    {
        Nearest,
        Average
    }

    internal enum DitherPattern
    {
        Bayer2x2,
        Bayer4x4,
        Bayer8x8,
        BlueNoise
    }

    internal enum DitherChannels
    {
        Monochrome,
        RGB
    }

    internal enum NoiseType
    {
        White,
        Value,
        Blue
    }

    internal enum ChannelMixerAlphaMode
    {
        Preserve,
        Mix
    }

    internal enum OffsetWrapMode
    {
        Repeat,
        Clamp
    }

    internal enum ExposureBrushMode
    {
        Lighten,
        Darken
    }

    [Serializable]
    internal abstract class TextureEffectData
    {
        [SerializeField] private string id = Guid.NewGuid().ToString("N");
        [SerializeField] private bool enabled = true;
        [SerializeField] private bool expanded = true;
        [SerializeField] private bool allowRandomize = true;

        internal string Id => id;
        internal bool Enabled { get => enabled; set => enabled = value; }
        internal bool Expanded { get => expanded; set => expanded = value; }
        internal bool AllowRandomize { get => allowRandomize; set => allowRandomize = value; }
        internal abstract string DisplayName { get; }
        internal abstract TextureEffectData Duplicate();

        internal void ApplyPortableState(string portableId, bool portableEnabled, bool portableAllowRandomize)
        {
            id = portableId;
            enabled = portableEnabled;
            allowRandomize = portableAllowRandomize;
            expanded = true;
        }

        protected T CopyCommonTo<T>(T copy) where T : TextureEffectData
        {
            copy.enabled = enabled;
            copy.expanded = expanded;
            copy.allowRandomize = allowRandomize;
            return copy;
        }
    }

    [Serializable]
    internal sealed class PixelateEffectData : TextureEffectData
    {
        [SerializeField] private PixelateMode mode = PixelateMode.BlockSize;
        [SerializeField] private int blockSize = 4;
        [SerializeField] private int targetResolution = 128;
        [SerializeField] private PixelSampling sampling = PixelSampling.Nearest;

        internal override string DisplayName => "Pixelate";
        internal PixelateMode Mode { get => mode; set => mode = value; }
        internal int BlockSize { get => blockSize; set => blockSize = value; }
        internal int TargetResolution { get => targetResolution; set => targetResolution = value; }
        internal PixelSampling Sampling { get => sampling; set => sampling = value; }

        internal override TextureEffectData Duplicate() => CopyCommonTo(new PixelateEffectData
        {
            mode = mode,
            blockSize = blockSize,
            targetResolution = targetResolution,
            sampling = sampling
        });
    }

    [Serializable]
    internal sealed class PosterizeEffectData : TextureEffectData
    {
        [SerializeField, Range(1, 8)] private int redBits = 5;
        [SerializeField, Range(1, 8)] private int greenBits = 5;
        [SerializeField, Range(1, 8)] private int blueBits = 5;

        internal override string DisplayName => "Posterize";
        internal int RedBits { get => redBits; set => redBits = value; }
        internal int GreenBits { get => greenBits; set => greenBits = value; }
        internal int BlueBits { get => blueBits; set => blueBits = value; }

        internal override TextureEffectData Duplicate() => CopyCommonTo(new PosterizeEffectData
        {
            redBits = redBits,
            greenBits = greenBits,
            blueBits = blueBits
        });
    }

    [Serializable]
    internal sealed class LevelsEffectData : TextureEffectData
    {
        [SerializeField, Range(0f, 1f)] private float blackPoint;
        [SerializeField, Range(0f, 1f)] private float whitePoint = 1f;
        [SerializeField, Range(0.1f, 4f)] private float gamma = 1f;
        [SerializeField, Range(0f, 1f)] private float outputBlack;
        [SerializeField, Range(0f, 1f)] private float outputWhite = 1f;

        internal override string DisplayName => "Levels";
        internal float BlackPoint { get => blackPoint; set => blackPoint = value; }
        internal float WhitePoint { get => whitePoint; set => whitePoint = value; }
        internal float Gamma { get => gamma; set => gamma = value; }
        internal float OutputBlack { get => outputBlack; set => outputBlack = value; }
        internal float OutputWhite { get => outputWhite; set => outputWhite = value; }

        internal override TextureEffectData Duplicate() => CopyCommonTo(new LevelsEffectData
        {
            blackPoint = blackPoint,
            whitePoint = whitePoint,
            gamma = gamma,
            outputBlack = outputBlack,
            outputWhite = outputWhite
        });
    }

    [Serializable]
    internal sealed class ColorAdjustmentsEffectData : TextureEffectData
    {
        [SerializeField, Range(-1f, 1f)] private float brightness;
        [SerializeField, Range(-1f, 1f)] private float contrast;
        [SerializeField, Range(0.1f, 4f)] private float gamma = 1f;

        internal override string DisplayName => "Color Adjustments";
        internal float Brightness { get => brightness; set => brightness = value; }
        internal float Contrast { get => contrast; set => contrast = value; }
        internal float Gamma { get => gamma; set => gamma = value; }

        internal override TextureEffectData Duplicate() => CopyCommonTo(new ColorAdjustmentsEffectData
        {
            brightness = brightness,
            contrast = contrast,
            gamma = gamma
        });
    }

    [Serializable]
    internal sealed class ColorReplaceEffectData : TextureEffectData
    {
        [SerializeField] private Color sourceColor = Color.black;
        [SerializeField] private Color replacementColor = Color.white;
        [SerializeField, Range(0f, 1.7320508f)] private float tolerance = 0.1f;
        [SerializeField, Range(0f, 1.7320508f)] private float softness = 0.05f;
        [SerializeField] private bool previewMask;

        internal override string DisplayName => "Color Replace";
        internal Color SourceColor { get => sourceColor; set => sourceColor = value; }
        internal Color ReplacementColor { get => replacementColor; set => replacementColor = value; }
        internal float Tolerance { get => tolerance; set => tolerance = value; }
        internal float Softness { get => softness; set => softness = value; }
        internal bool PreviewMask { get => previewMask; set => previewMask = value; }

        internal override TextureEffectData Duplicate() => CopyCommonTo(new ColorReplaceEffectData
        {
            sourceColor = sourceColor,
            replacementColor = replacementColor,
            tolerance = tolerance,
            softness = softness,
            previewMask = previewMask
        });
    }

    [Serializable]
    internal sealed class PaletteQuantizeEffectData : TextureEffectData
    {
        // The adapter is deliberately not serialized. Portable state owns only
        // paletteId; Editor/runtime hosts resolve that ID through a repository.
        [NonSerialized] private TextureLabPalette palette;
        [SerializeField] private string paletteId = string.Empty;
        [SerializeField, Range(0, 64)] private int colorLimit;
        [SerializeField] private int extractionColorCount = 16;

        internal override string DisplayName => "Palette Quantization";
        internal TextureLabPalette Palette
        {
            get => palette;
            set
            {
                palette = value;
                if (value != null)
                    paletteId = value.Id;
            }
        }

        internal void ResolvePalette(TextureLabPalette value)
        {
            palette = value;
            if (value != null)
                paletteId = value.Id;
        }
        internal string PaletteId { get => paletteId; set => paletteId = value ?? string.Empty; }
        internal int ColorLimit { get => Mathf.Clamp(colorLimit, 0, 64); set => colorLimit = Mathf.Clamp(value, 0, 64); }
        internal int ExtractionColorCount { get => extractionColorCount; set => extractionColorCount = value; }

        internal override TextureEffectData Duplicate() => CopyCommonTo(new PaletteQuantizeEffectData
        {
            palette = palette,
            paletteId = paletteId,
            colorLimit = colorLimit,
            extractionColorCount = extractionColorCount
        });
    }

    [Serializable]
    internal sealed class DitherEffectData : TextureEffectData
    {
        [SerializeField] private DitherPattern pattern = DitherPattern.Bayer4x4;
        [SerializeField, Range(0f, 1f)] private float strength = 0.1f;
        [SerializeField, Range(1, 8)] private int scale = 1;
        [SerializeField] private int seed;
        [SerializeField] private DitherChannels channels = DitherChannels.Monochrome;

        internal override string DisplayName => "Dither";
        internal DitherPattern Pattern { get => pattern; set => pattern = value; }
        internal float Strength { get => strength; set => strength = value; }
        internal int Scale { get => scale; set => scale = value; }
        internal int Seed { get => seed; set => seed = value; }
        internal DitherChannels Channels { get => channels; set => channels = value; }

        internal override TextureEffectData Duplicate() => CopyCommonTo(new DitherEffectData
        {
            pattern = pattern,
            strength = strength,
            scale = scale,
            seed = seed,
            channels = channels
        });
    }

    [Serializable]
    internal sealed class NoiseEffectData : TextureEffectData
    {
        [SerializeField] private NoiseType type = NoiseType.White;
        [SerializeField, Range(0f, 1f)] private float amount = 0.1f;
        [SerializeField, Range(1, 64)] private int scale = 1;
        [SerializeField] private int seed;
        [SerializeField] private DitherChannels channels = DitherChannels.Monochrome;

        internal override string DisplayName => "Noise";
        internal NoiseType Type { get => type; set => type = value; }
        internal float Amount { get => amount; set => amount = value; }
        internal int Scale { get => scale; set => scale = value; }
        internal int Seed { get => seed; set => seed = value; }
        internal DitherChannels Channels { get => channels; set => channels = value; }

        internal override TextureEffectData Duplicate() => CopyCommonTo(new NoiseEffectData
        {
            type = type,
            amount = amount,
            scale = scale,
            seed = seed,
            channels = channels
        });
    }

    [Serializable]
    internal sealed class GaussianBlurEffectData : TextureEffectData
    {
        [SerializeField, Range(0f, 32f)] private float radius = 1f;
        [SerializeField, Range(1, 4)] private int iterations = 1;

        internal override string DisplayName => "Gaussian Blur";
        internal float Radius { get => radius; set => radius = value; }
        internal int Iterations { get => iterations; set => iterations = value; }

        internal override TextureEffectData Duplicate() => CopyCommonTo(new GaussianBlurEffectData
        {
            radius = radius,
            iterations = iterations
        });
    }

    [Serializable]
    internal sealed class BrushStroke
    {
        [SerializeField] private List<Vector2> points = new();
        [SerializeField] private ExposureBrushMode mode;
        [SerializeField] private float size;
        [SerializeField] private float hardness;
        [SerializeField] private float exposure;

        internal BrushStroke(ExposureBrushMode mode, float size, float hardness, float exposure)
        {
            this.mode = mode;
            this.size = size;
            this.hardness = hardness;
            this.exposure = exposure;
        }

        internal List<Vector2> Points => points ??= new List<Vector2>();
        internal ExposureBrushMode Mode => mode;
        internal float Size => size;
        internal float Hardness => hardness;
        internal float Exposure => exposure;

        internal BrushStroke Duplicate()
        {
            var copy = new BrushStroke(mode, size, hardness, exposure);
            copy.Points.AddRange(Points);
            return copy;
        }
    }

    [Serializable]
    internal sealed class ExposureBrushEffectData : TextureEffectData
    {
        [SerializeField] private List<BrushStroke> strokes = new();
        [SerializeField, Range(1f, 512f)] private float brushSize = 96f;
        [SerializeField, Range(0f, 1f)] private float brushHardness = 0.5f;
        [SerializeField, Range(0.01f, 2f)] private float brushExposure = 0.25f;
        [SerializeField] private ExposureBrushMode brushMode;
        [SerializeField] private OffsetWrapMode wrap = OffsetWrapMode.Clamp;

        internal ExposureBrushEffectData() => AllowRandomize = false;

        internal override string DisplayName => "Dodge / Burn Brush";
        internal List<BrushStroke> Strokes => strokes ??= new List<BrushStroke>();
        internal float BrushSize { get => brushSize; set => brushSize = value; }
        internal float BrushHardness { get => brushHardness; set => brushHardness = value; }
        internal float BrushExposure { get => brushExposure; set => brushExposure = value; }
        internal ExposureBrushMode BrushMode { get => brushMode; set => brushMode = value; }
        internal OffsetWrapMode Wrap { get => wrap; set => wrap = value; }

        internal BrushStroke CreateStroke() => new(brushMode, brushSize, brushHardness, brushExposure);

        internal override TextureEffectData Duplicate()
        {
            var copy = CopyCommonTo(new ExposureBrushEffectData
            {
                brushSize = brushSize,
                brushHardness = brushHardness,
                brushExposure = brushExposure,
                brushMode = brushMode,
                wrap = wrap
            });
            foreach (BrushStroke stroke in Strokes)
                copy.Strokes.Add(stroke.Duplicate());
            return copy;
        }
    }

    [Serializable]
    internal sealed class OffsetEffectData : TextureEffectData
    {
        [SerializeField, Range(0f, 1f)] private float offsetX;
        [SerializeField, Range(0f, 1f)] private float offsetY;
        [SerializeField] private OffsetWrapMode wrap = OffsetWrapMode.Repeat;

        internal OffsetEffectData() => AllowRandomize = false;

        internal override string DisplayName => "Offset";
        internal float OffsetX { get => offsetX; set => offsetX = value; }
        internal float OffsetY { get => offsetY; set => offsetY = value; }
        internal OffsetWrapMode Wrap { get => wrap; set => wrap = value; }

        internal override TextureEffectData Duplicate() => CopyCommonTo(new OffsetEffectData
        {
            offsetX = offsetX,
            offsetY = offsetY,
            wrap = wrap
        });
    }

    [Serializable]
    internal sealed class SeamBlendEffectData : TextureEffectData
    {
        [SerializeField, Range(0f, 0.5f)] private float blendWidth = 0.1f;
        [SerializeField, Range(0f, 1f)] private float blendStrength = 0.5f;
        [SerializeField] private bool horizontal = true;
        [SerializeField] private bool vertical = true;
        [SerializeField] private bool blendAlpha;

        internal SeamBlendEffectData() => AllowRandomize = false;

        internal override string DisplayName => "Seam Blend";
        internal float BlendWidth { get => blendWidth; set => blendWidth = value; }
        internal float BlendStrength { get => blendStrength; set => blendStrength = value; }
        internal bool Horizontal { get => horizontal; set => horizontal = value; }
        internal bool Vertical { get => vertical; set => vertical = value; }
        internal bool BlendAlpha { get => blendAlpha; set => blendAlpha = value; }

        internal override TextureEffectData Duplicate() => CopyCommonTo(new SeamBlendEffectData
        {
            blendWidth = blendWidth,
            blendStrength = blendStrength,
            horizontal = horizontal,
            vertical = vertical,
            blendAlpha = blendAlpha
        });
    }

    [Serializable]
    internal sealed class ChannelRemapEffectData : TextureEffectData
    {
        [SerializeField] private Vector3 redOutput = Vector3.right;
        [SerializeField] private Vector3 greenOutput = Vector3.up;
        [SerializeField] private Vector3 blueOutput = Vector3.forward;
        [SerializeField] private Vector3 constants;
        [SerializeField, Range(0f, 1f)] private float strength = 1f;
        [SerializeField] private bool monochrome;
        [SerializeField] private Vector3 monochromeMix = new(0.2126f, 0.7152f, 0.0722f);
        [SerializeField, Range(-1f, 1f)] private float monochromeConstant;
        [SerializeField] private ChannelMixerAlphaMode alphaMode;
        [SerializeField] private Vector4 alphaOutput = new(0f, 0f, 0f, 1f);
        [SerializeField, Range(-1f, 1f)] private float alphaConstant;

        internal override string DisplayName => "Channel Mixer";
        internal Vector3 RedOutput { get => redOutput; set => redOutput = value; }
        internal Vector3 GreenOutput { get => greenOutput; set => greenOutput = value; }
        internal Vector3 BlueOutput { get => blueOutput; set => blueOutput = value; }
        internal Vector3 Constants { get => constants; set => constants = value; }
        internal float Strength { get => strength; set => strength = value; }
        internal bool Monochrome { get => monochrome; set => monochrome = value; }
        internal Vector3 MonochromeMix { get => monochromeMix; set => monochromeMix = value; }
        internal float MonochromeConstant { get => monochromeConstant; set => monochromeConstant = value; }
        internal ChannelMixerAlphaMode AlphaMode { get => alphaMode; set => alphaMode = value; }
        internal Vector4 AlphaOutput { get => alphaOutput; set => alphaOutput = value; }
        internal float AlphaConstant { get => alphaConstant; set => alphaConstant = value; }

        internal Vector3 GetOutput(int index) => index switch
        {
            0 => redOutput,
            1 => greenOutput,
            _ => blueOutput
        };

        internal void SetOutput(int index, Vector3 value)
        {
            if (index == 0)
                redOutput = value;
            else if (index == 1)
                greenOutput = value;
            else
                blueOutput = value;
        }

        internal void ResetAll()
        {
            redOutput = Vector3.right;
            greenOutput = Vector3.up;
            blueOutput = Vector3.forward;
            constants = Vector3.zero;
            strength = 1f;
            monochrome = false;
            monochromeMix = new Vector3(0.2126f, 0.7152f, 0.0722f);
            monochromeConstant = 0f;
            alphaMode = ChannelMixerAlphaMode.Preserve;
            alphaOutput = new Vector4(0f, 0f, 0f, 1f);
            alphaConstant = 0f;
        }

        internal override TextureEffectData Duplicate() => CopyCommonTo(new ChannelRemapEffectData
        {
            redOutput = redOutput,
            greenOutput = greenOutput,
            blueOutput = blueOutput,
            constants = constants,
            strength = strength,
            monochrome = monochrome,
            monochromeMix = monochromeMix,
            monochromeConstant = monochromeConstant,
            alphaMode = alphaMode,
            alphaOutput = alphaOutput,
            alphaConstant = alphaConstant
        });
    }
}
