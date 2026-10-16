Shader "Hidden/TextureLab/Seamless"
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
            Name "Offset"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment OffsetFragment
            #include "UnityCG.cginc"

            Texture2D _MainTex;
            float4 _SourceSize;
            float4 _Offset;
            float4 _Settings;

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

            float4 LoadSource(float2 uv)
            {
                int2 size = max(int2(_SourceSize.xy), int2(1, 1));
                int2 coordinate = clamp(int2(floor(uv * size)), 0, size - 1);
                return _MainTex.Load(int3(coordinate, 0));
            }

            float4 OffsetFragment(Varyings input) : SV_Target
            {
                float2 uv = input.uv - _Offset.xy;
                uv = _Settings.z > 0.5 ? frac(uv) : saturate(uv);
                return LoadSource(uv);
            }
            ENDHLSL
        }

        Pass
        {
            Name "SeamBlend"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment SeamBlendFragment
            #include "UnityCG.cginc"

            Texture2D _MainTex;
            float4 _SourceSize;
            float4 _Settings;
            float _BlendAlpha;

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

            float4 LoadSource(float2 uv)
            {
                int2 size = max(int2(_SourceSize.xy), int2(1, 1));
                int2 coordinate = clamp(int2(floor(uv * size)), 0, size - 1);
                return _MainTex.Load(int3(coordinate, 0));
            }

            float EdgeBlend(float coordinate)
            {
                if (_Settings.x <= 0.0)
                    return 0.0;

                float edgeDistance = min(coordinate, 1.0 - coordinate);
                return (1.0 - smoothstep(0.0, _Settings.x, edgeDistance)) * _Settings.y;
            }

            float4 SeamBlendFragment(Varyings input) : SV_Target
            {
                float horizontalWeight = _Settings.z > 0.5 ? EdgeBlend(input.uv.x) : 0.0;
                float verticalWeight = _Settings.w > 0.5 ? EdgeBlend(input.uv.y) : 0.0;
                float4 source = LoadSource(input.uv);
                float4 horizontal = LoadSource(float2(1.0 - input.uv.x, input.uv.y));
                float4 vertical = LoadSource(float2(input.uv.x, 1.0 - input.uv.y));
                float4 diagonal = LoadSource(1.0 - input.uv);

                float3 color = lerp(
                    lerp(source.rgb, horizontal.rgb, horizontalWeight),
                    lerp(vertical.rgb, diagonal.rgb, horizontalWeight),
                    verticalWeight);
                float alpha = source.a;
                if (_BlendAlpha > 0.5)
                {
                    alpha = lerp(
                        lerp(source.a, horizontal.a, horizontalWeight),
                        lerp(vertical.a, diagonal.a, horizontalWeight),
                        verticalWeight);
                }

                return float4(color, alpha);
            }
            ENDHLSL
        }
    }
}
