Shader "Hidden/TextureLab/Pixelate"
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
        float4 _VirtualResolution;

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

        int2 SourceCoordinate(float2 uv)
        {
            int2 size = max(int2(_SourceSize.xy), 1);
            return clamp(int2(uv * size), 0, size - 1);
        }
        ENDHLSL

        Pass
        {
            Name "Nearest"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragmentNearest

            float4 FragmentNearest(Varyings input) : SV_Target
            {
                float2 cells = max(_VirtualResolution.xy, 1.0);
                float2 uv = (floor(input.uv * cells) + 0.5) / cells;
                return _MainTex.Load(int3(SourceCoordinate(uv), 0));
            }
            ENDHLSL
        }

        Pass
        {
            Name "Average"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragmentAverage

            float4 FragmentAverage(Varyings input) : SV_Target
            {
                int2 sourceSize = max(int2(_SourceSize.xy), 1);
                int2 cells = max(int2(_VirtualResolution.xy), 1);
                int2 cell = min(int2(input.uv * cells), cells - 1);
                int2 start = int2(floor((float2)cell * sourceSize / cells));
                int2 end = max(start + 1, int2(floor((float2)(cell + 1) * sourceSize / cells)));
                float4 sum = 0.0;
                int count = 0;

                [loop]
                for (int y = 0; y < 64; y++)
                {
                    if (start.y + y >= end.y)
                        break;

                    [loop]
                    for (int x = 0; x < 64; x++)
                    {
                        if (start.x + x >= end.x)
                            break;

                        sum += _MainTex.Load(int3(start + int2(x, y), 0));
                        count++;
                    }
                }

                return sum / max(count, 1);
            }
            ENDHLSL
        }
    }
}
