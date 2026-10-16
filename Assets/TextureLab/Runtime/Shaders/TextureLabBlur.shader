Shader "Hidden/TextureLab/Blur"
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
            Name "GaussianBlur"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Fragment
            #include "UnityCG.cginc"

            Texture2D _MainTex;
            SamplerState sampler_MainTex;
            float4 _SourceSize;
            float2 _Direction;
            float _Radius;

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

            float4 SampleSource(float2 uv)
            {
                return _MainTex.SampleLevel(sampler_MainTex, saturate(uv), 0);
            }

            float4 Fragment(Varyings input) : SV_Target
            {
                // ponytail: a fixed nine-tap kernel keeps each pass constant-cost;
                // add radius-dependent kernels only if high-radius quality needs it.
                float2 offset = _Direction * _Radius / max(_SourceSize.xy, 1.0);
                float4 center = SampleSource(input.uv);
                float3 color = center.rgb * 0.227027;
                color += (SampleSource(input.uv + offset * 1.384615).rgb + SampleSource(input.uv - offset * 1.384615).rgb) * 0.316216;
                color += (SampleSource(input.uv + offset * 3.230769).rgb + SampleSource(input.uv - offset * 3.230769).rgb) * 0.070270;
                return float4(color, center.a);
            }
            ENDHLSL
        }
    }
}
