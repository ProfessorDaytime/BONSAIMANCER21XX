// Rain streaks for WeatherVisuals (2026-10-30): stretched-billboard particles, soft at the sides
// and fading toward the tail, tinted by the particle colour. Lives in Resources so the build keeps
// it — WeatherVisuals makes its material in code.
Shader "Custom/RainStreak"
{
    Properties
    {
        _Color ("Tint", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex   RainVert
            #pragma fragment RainFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
            CBUFFER_END

            struct RainAttr { float4 posOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct RainVary { float4 posHCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };

            RainVary RainVert(RainAttr IN)
            {
                RainVary OUT;
                OUT.posHCS = TransformObjectToHClip(IN.posOS.xyz);
                OUT.color  = IN.color * _Color;
                OUT.uv     = IN.uv;
                return OUT;
            }

            half4 RainFrag(RainVary IN) : SV_Target
            {
                // A stretched billboard runs u along the streak and v across it.
                half across = 1.0 - abs(IN.uv.y * 2.0 - 1.0);
                half along  = smoothstep(0.0, 0.6, IN.uv.x);
                return half4(IN.color.rgb, IN.color.a * across * along);
            }
            ENDHLSL
        }
    }
}
