Shader "Hidden/TextureLab/Dither"
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
            Name "Dither"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Fragment
            #include "UnityCG.cginc"

            Texture2D _MainTex;
            float4 _SourceSize;
            int _Pattern;
            float _Strength;
            int _Scale;
            int _Seed;
            int _Rgb;

            // ponytail: a 16x16 tile keeps the package self-contained; replace it
            // with a larger blue-noise texture if repetition becomes visible.
            static const int BlueNoise16[256] =
            {
                197,148,243,25,254,33,182,159,213,194,2,151,238,252,11,249,
                199,1,174,183,247,200,215,19,139,61,100,143,32,242,224,72,
                44,136,233,83,17,127,75,244,51,218,70,195,193,60,196,116,
                221,176,45,191,104,92,123,103,185,217,210,20,154,232,29,177,
                16,234,184,63,239,167,156,4,128,38,206,160,59,153,112,120,
                203,231,9,157,86,30,85,198,73,161,171,84,131,5,150,66,
                27,219,108,169,62,245,69,205,190,168,12,99,135,155,82,166,
                147,55,152,179,175,46,101,24,132,68,240,95,18,125,229,90,
                87,170,106,31,223,230,58,209,91,163,57,202,204,124,14,107,
                114,3,172,180,227,10,255,34,149,0,207,43,201,141,119,88,
                220,248,96,121,56,102,54,113,80,246,81,89,122,40,214,26,
                186,53,79,15,115,137,250,21,105,47,111,28,241,129,50,118,
                133,212,71,97,142,42,216,253,78,93,77,134,187,6,173,39,
                158,8,146,228,65,181,165,41,226,13,126,76,130,192,251,164,
                225,162,140,37,235,7,236,117,211,109,52,144,23,145,35,94,
                22,98,64,188,67,189,49,110,36,222,237,48,178,74,138,208
            };

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

            int Bayer2(int x, int y)
            {
                x %= 2;
                y %= 2;
                return y == 0 ? (x == 0 ? 0 : 2) : (x == 0 ? 3 : 1);
            }

            int Bayer4(int x, int y)
            {
                return 4 * Bayer2(x % 2, y % 2) + Bayer2((x / 2) % 2, (y / 2) % 2);
            }

            int Bayer8(int x, int y)
            {
                return 4 * Bayer4(x % 4, y % 4) + Bayer2((x / 4) % 2, (y / 4) % 2);
            }

            float Threshold(int2 pixel, int channel)
            {
                int scale = max(_Scale, 1);
                int seed = abs(_Seed) + channel * 131;
                int2 patternCoordinate = pixel / scale + int2(seed * 17, seed * 29);
                int rank;
                int count;

                if (_Pattern == 0)
                {
                    rank = Bayer2(patternCoordinate.x, patternCoordinate.y);
                    count = 4;
                }
                else if (_Pattern == 1)
                {
                    rank = Bayer4(patternCoordinate.x, patternCoordinate.y);
                    count = 16;
                }
                else if (_Pattern == 2)
                {
                    rank = Bayer8(patternCoordinate.x, patternCoordinate.y);
                    count = 64;
                }
                else
                {
                    int x = patternCoordinate.x % 16;
                    int y = patternCoordinate.y % 16;
                    rank = BlueNoise16[y * 16 + x];
                    count = 256;
                }

                return (rank + 0.5) / count - 0.5;
            }

            float4 Fragment(Varyings input) : SV_Target
            {
                int2 size = max(int2(_SourceSize.xy), 1);
                int2 coordinate = clamp(int2(input.uv * size), 0, size - 1);
                float4 source = _MainTex.Load(int3(coordinate, 0));
                float mono = Threshold(coordinate, 0);
                float3 noise = _Rgb != 0
                    ? float3(mono, Threshold(coordinate, 1), Threshold(coordinate, 2))
                    : mono.xxx;
                source.rgb = saturate(source.rgb + noise * _Strength);
                return source;
            }
            ENDHLSL
        }
    }
}
