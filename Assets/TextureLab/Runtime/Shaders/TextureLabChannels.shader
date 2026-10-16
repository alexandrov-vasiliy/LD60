Shader "Hidden/TextureLab/Channels"
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
            Name "ChannelMixer"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Fragment
            #include "UnityCG.cginc"

            Texture2D _MainTex;
            float4 _SourceSize;
            float4 _RedOutput;
            float4 _GreenOutput;
            float4 _BlueOutput;
            float4 _Constants;
            float4 _MixerSettings;
            float4 _MonochromeMix;
            float4 _AlphaOutput;

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

            float4 Fragment(Varyings input) : SV_Target
            {
                int2 size = max(int2(_SourceSize.xy), 1);
                int2 coordinate = clamp(int2(input.uv * size), 0, size - 1);
                float4 source = _MainTex.Load(int3(coordinate, 0));
                float3 mixed = float3(
                    dot(source.rgb, _RedOutput.rgb) + _Constants.r,
                    dot(source.rgb, _GreenOutput.rgb) + _Constants.g,
                    dot(source.rgb, _BlueOutput.rgb) + _Constants.b);

                if (_MixerSettings.y > 0.5)
                {
                    float gray = dot(source.rgb, _MonochromeMix.rgb) + _Constants.a;
                    mixed = gray.xxx;
                }

                float alpha = source.a;
                if (_MixerSettings.z > 0.5)
                    alpha = saturate(dot(source, _AlphaOutput) + _MixerSettings.w);

                return float4(saturate(lerp(source.rgb, mixed, _MixerSettings.x)), alpha);
            }
            ENDHLSL
        }

        Pass
        {
            Name "PreviewChannel"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment PreviewFragment
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            int _PreviewChannel;

            float4 PreviewFragment(v2f_img input) : SV_Target
            {
                float4 source = tex2D(_MainTex, input.uv);
                if (_PreviewChannel == 0)
                    return source;

                float value = source.r;
                if (_PreviewChannel == 2)
                    value = source.g;
                else if (_PreviewChannel == 3)
                    value = source.b;
                else if (_PreviewChannel == 4)
                    value = source.a;
                else if (_PreviewChannel == 5)
                    value = dot(source.rgb, float3(0.2126, 0.7152, 0.0722));

                return float4(value.xxx, 1.0);
            }
            ENDHLSL
        }
    }
}
