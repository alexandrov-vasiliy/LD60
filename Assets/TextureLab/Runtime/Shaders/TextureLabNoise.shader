Shader "Hidden/TextureLab/Noise"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "Noise"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Fragment
            #include "UnityCG.cginc"

            Texture2D _MainTex;
            float4 _SourceSize;
            int _NoiseType;
            float _Amount;
            int _Scale;
            int _Seed;
            int _Rgb;

            struct Attributes
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                return output;
            }

            float Hash(float2 coordinate, int seed)
            {
                float value = dot(coordinate, float2(127.1, 311.7)) + seed * 74.7;
                return frac(sin(value) * 43758.5453123);
            }

            float ValueNoise(float2 coordinate, int seed)
            {
                float2 cell = floor(coordinate);
                float2 fraction = frac(coordinate);
                fraction = fraction * fraction * (3.0 - 2.0 * fraction);
                float lower = lerp(Hash(cell, seed), Hash(cell + float2(1.0, 0.0), seed), fraction.x);
                float upper = lerp(Hash(cell + float2(0.0, 1.0), seed), Hash(cell + 1.0, seed), fraction.x);
                return lerp(lower, upper, fraction.y);
            }

            float BlueNoise(float2 coordinate, int seed)
            {
                float value = dot(coordinate + seed, float2(0.06711056, 0.00583715));
                return frac(52.9829189 * frac(value));
            }

            float SampleNoise(float2 pixel, int seed)
            {
                float2 coordinate = pixel / max(_Scale, 1);
                if (_NoiseType == 0)
                    return Hash(floor(coordinate), seed);
                if (_NoiseType == 1)
                    return ValueNoise(coordinate, seed);
                return BlueNoise(coordinate, seed);
            }

            float4 Fragment(Varyings input) : SV_Target
            {
                int2 size = max(int2(_SourceSize.xy), 1);
                int2 coordinate = clamp(int2(input.uv * size), 0, size - 1);
                float4 source = _MainTex.Load(int3(coordinate, 0));
                float mono = SampleNoise(coordinate, _Seed) - 0.5;
                float3 noise = _Rgb != 0
                    ? float3(mono, SampleNoise(coordinate, _Seed + 131) - 0.5, SampleNoise(coordinate, _Seed + 263) - 0.5)
                    : mono.xxx;
                source.rgb = saturate(source.rgb + noise * _Amount);
                return source;
            }
            ENDHLSL
        }
    }
}
