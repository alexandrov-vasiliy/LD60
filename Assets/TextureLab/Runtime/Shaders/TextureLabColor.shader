Shader "Hidden/TextureLab/Color"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        HLSLINCLUDE
        #pragma target 3.5
        #include "UnityCG.cginc"

        Texture2D _MainTex;
        float4 _SourceSize;
        float4 _Bits;
        float4 _InputLevels;
        float4 _OutputLevels;
        float4 _Adjustments;
        float4 _ReplaceSource;
        float4 _Replacement;
        float4 _ReplaceSettings;

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

        float4 ReadSource(float2 uv)
        {
            int2 size = max(int2(_SourceSize.xy), 1);
            int2 coordinate = clamp(int2(uv * size), 0, size - 1);
            return _MainTex.Load(int3(coordinate, 0));
        }
        ENDHLSL

        Pass
        {
            Name "Posterize"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragmentPosterize

            float4 FragmentPosterize(Varyings input) : SV_Target
            {
                float4 source = ReadSource(input.uv);
                float3 levels = exp2(clamp(_Bits.rgb, 1.0, 8.0)) - 1.0;
                source.rgb = round(saturate(source.rgb) * levels) / levels;
                return source;
            }
            ENDHLSL
        }

        Pass
        {
            Name "Levels"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragmentLevels

            float4 FragmentLevels(Varyings input) : SV_Target
            {
                float4 source = ReadSource(input.uv);
                float blackPoint = _InputLevels.x;
                float whitePoint = max(_InputLevels.y, blackPoint + 0.0001);
                float gamma = max(_InputLevels.z, 0.0001);
                float3 color = saturate((source.rgb - blackPoint) / (whitePoint - blackPoint));
                color = pow(color, 1.0 / gamma);
                source.rgb = lerp(_OutputLevels.x, _OutputLevels.y, color);
                return source;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ColorAdjustments"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragmentAdjustments

            float4 FragmentAdjustments(Varyings input) : SV_Target
            {
                float4 source = ReadSource(input.uv);
                float brightness = _Adjustments.x;
                float contrast = _Adjustments.y;
                float gamma = max(_Adjustments.z, 0.0001);
                float3 color = (source.rgb - 0.5) * (1.0 + contrast) + 0.5 + brightness;
                source.rgb = pow(saturate(color), 1.0 / gamma);
                return source;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ColorReplace"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragmentReplace

            float4 FragmentReplace(Varyings input) : SV_Target
            {
                float4 source = ReadSource(input.uv);
                float distanceToSource = length(source.rgb - _ReplaceSource.rgb);
                float softness = max(_ReplaceSettings.y, 0.00001);
                float mask = 1.0 - smoothstep(_ReplaceSettings.x, _ReplaceSettings.x + softness, distanceToSource);
                source.rgb = _ReplaceSettings.z > 0.5 ? mask.xxx : lerp(source.rgb, _Replacement.rgb, mask);
                return source;
            }
            ENDHLSL
        }
    }
}
