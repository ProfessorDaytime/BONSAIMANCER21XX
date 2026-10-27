// Luma-keyed, transparent, unlit video surface for URP.
//
// Made for footage shot over black (rain, snow, sparks, dust): brightness becomes transparency,
// so black disappears and the bright streaks stay. Two controls on top of that:
//   _KeyLow / _KeyHigh - brightness ramp. Below _KeyLow is fully transparent, above _KeyHigh is
//                        fully opaque, smooth in between (a soft key avoids harsh edges on thin
//                        streaks).
//   _Opacity           - overall strength of the whole layer.
// _Fade is driven at runtime by RainVisual for fading in and out; leave it at 1 in the material.
//
// Unlit on purpose: rain streaks shouldn't pick up scene lighting or cast shadows.

Shader "Bonsai/Luma Key Video"
{
    Properties
    {
        [MainTexture] _BaseMap ("Video", 2D) = "black" {}
        [MainColor]   _Tint    ("Tint", Color) = (0.85, 0.9, 1, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.45
        _KeyLow  ("Key: transparent below", Range(0, 1)) = 0.06
        _KeyHigh ("Key: opaque above", Range(0, 1)) = 0.45
        _Fade    ("Fade (set by script)", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "LumaKeyUnlit"

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _Tint;
                half   _Opacity;
                half   _KeyLow;
                half   _KeyHigh;
                half   _Fade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half3 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).rgb;

                // Rec. 709 luma. Brightness becomes alpha: black -> 0, bright streaks -> 1.
                half luma = dot(c, half3(0.2126h, 0.7152h, 0.0722h));
                half key  = smoothstep(_KeyLow, max(_KeyHigh, _KeyLow + 0.001h), luma);

                // _Fade can exceed 1 during a storm (RainVisual.stormStrength); clamp the result.
                half alpha = saturate(key * _Opacity * _Fade * _Tint.a);
                return half4(c * _Tint.rgb, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
