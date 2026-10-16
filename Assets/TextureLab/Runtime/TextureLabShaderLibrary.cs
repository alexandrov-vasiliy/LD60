using System;
using UnityEngine;

namespace TextureLab.Runtime
{
    [CreateAssetMenu(fileName = "TextureLabShaderLibrary", menuName = "Texture Lab/Runtime Shader Library")]
    public sealed class TextureLabShaderLibrary : ScriptableObject
    {
        [SerializeField] private Shader pixelate;
        [SerializeField] private Shader color;
        [SerializeField] private Shader paletteQuantize;
        [SerializeField] private Shader dither;
        [SerializeField] private Shader noise;
        [SerializeField] private Shader blur;
        [SerializeField] private Shader seamless;
        [SerializeField] private Shader channels;
        [SerializeField] private Shader brushExposure;

        internal Shader Pixelate => Require(pixelate, nameof(pixelate));
        internal Shader Color => Require(color, nameof(color));
        internal Shader PaletteQuantize => Require(paletteQuantize, nameof(paletteQuantize));
        internal Shader Dither => Require(dither, nameof(dither));
        internal Shader Noise => Require(noise, nameof(noise));
        internal Shader Blur => Require(blur, nameof(blur));
        internal Shader Seamless => Require(seamless, nameof(seamless));
        internal Shader Channels => Require(channels, nameof(channels));
        internal Shader BrushExposure => Require(brushExposure, nameof(brushExposure));

        internal static TextureLabShaderLibrary LoadDefault()
        {
            TextureLabShaderLibrary library = Resources.Load<TextureLabShaderLibrary>("TextureLabShaderLibrary");
            return library != null
                ? library
                : throw new InvalidOperationException("Texture Lab shader library is missing from Resources.");
        }

        private static Shader Require(Shader shader, string fieldName) => shader != null
            ? shader
            : throw new InvalidOperationException($"Texture Lab shader library is missing '{fieldName}'.");
    }
}
