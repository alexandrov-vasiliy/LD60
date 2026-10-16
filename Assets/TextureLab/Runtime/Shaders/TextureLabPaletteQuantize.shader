Shader "Hidden/TextureLab/PaletteQuantize"
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
            Name "PaletteQuantize"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Fragment
            #include "UnityCG.cginc"

            Texture2D _MainTex;
            float4 _SourceSize;
            float4 _PaletteLab[64];
            float4 _PaletteRgb[64];
            int _PaletteCount;

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

            float3 LinearRgbToOklab(float3 color)
            {
                float3 lms = mul(float3x3(
                    0.4122214708, 0.5363325363, 0.0514459929,
                    0.2119034982, 0.6806995451, 0.1073969566,
                    0.0883024619, 0.2817188376, 0.6299787005), color);
                lms = pow(max(lms, 0.0), 1.0 / 3.0);
                return mul(float3x3(
                    0.2104542553, 0.7936177850, -0.0040720468,
                    1.9779984951, -2.4285922050, 0.4505937099,
                    0.0259040371, 0.7827717662, -0.8086757660), lms);
            }

            float4 Fragment(Varyings input) : SV_Target
            {
                int2 size = max(int2(_SourceSize.xy), 1);
                int2 coordinate = clamp(int2(input.uv * size), 0, size - 1);
                float4 source = _MainTex.Load(int3(coordinate, 0));
                float3 sourceLab = LinearRgbToOklab(source.rgb);
                float smallestDistance = 1e20;
                float3 nearest = source.rgb;

                [loop]
                for (int i = 0; i < 64; i++)
                {
                    if (i >= _PaletteCount)
                        break;

                    float3 difference = sourceLab - _PaletteLab[i].xyz;
                    float distance = dot(difference, difference);
                    if (distance < smallestDistance)
                    {
                        smallestDistance = distance;
                        nearest = _PaletteRgb[i].rgb;
                    }
                }

                return float4(nearest, source.a);
            }
            ENDHLSL
        }
    }
}
