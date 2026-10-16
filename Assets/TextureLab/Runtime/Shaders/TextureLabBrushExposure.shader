Shader "Hidden/TextureLab/BrushExposure"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _ExposureMask ("Exposure Mask", 2D) = "black" {}
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "BrushMask"
            Blend One One
            BlendOp Add
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex MaskVert
            #pragma fragment MaskFragment

            struct Attributes
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 settings : TEXCOORD1;
            };

            Varyings MaskVert(Attributes input)
            {
                Varyings output;
                output.vertex = input.vertex;
                output.uv = input.uv;
                output.settings = input.color.rg;
                return output;
            }

            float4 MaskFragment(Varyings input) : SV_Target
            {
                float distanceToCenter = length(input.uv);
                clip(1.0 - distanceToCenter);
                float hardEdge = min(input.settings.y, 0.9999);
                float falloff = 1.0 - smoothstep(hardEdge, 1.0, distanceToCenter);
                return float4(input.settings.x * falloff, 0.0, 0.0, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ApplyExposure"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ApplyVert
            #pragma fragment ApplyFragment
            #include "UnityCG.cginc"

            Texture2D _MainTex;
            Texture2D _ExposureMask;
            float4 _SourceSize;

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

            Varyings ApplyVert(Attributes input)
            {
                Varyings output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                return output;
            }

            float4 ApplyFragment(Varyings input) : SV_Target
            {
                int2 size = max(int2(_SourceSize.xy), int2(1, 1));
                int2 coordinate = clamp(int2(input.uv * size), 0, size - 1);
                float4 source = _MainTex.Load(int3(coordinate, 0));
                float exposure = _ExposureMask.Load(int3(coordinate, 0)).r;
                source.rgb *= exp2(exposure);
                return source;
            }
            ENDHLSL
        }
    }
}
