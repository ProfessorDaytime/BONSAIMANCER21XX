// Buds on opposite-budded broadleaf trees (Japanese maple), drawn as one combined mesh by
// BudVisuals (2026-10-29); also their flowers and fruit (FlowerVisuals), two-sided via _Cull.
// Colour comes from the vertex colour; UV1 carries the sway data of the twig each bud sits on
// (x = isRoot, y = GrowthColor alpha), so buds bend with the bark exactly.
// Lighting matches the bark's three cel bands.
Shader "Custom/TreeBud"
{
    Properties
    {
        _ShadowThreshold ("Shadow Threshold", Range(0,1)) = 0.20
        _MidThreshold    ("Mid Threshold",    Range(0,1)) = 0.55
        _ShadowTint      ("Shadow Tint",      Color)      = (0.25,0.22,0.28,1)
        _MidTint         ("Mid Tint",         Color)      = (0.78,0.75,0.72,1)
        // Off for single-sheet geometry (sepals, samara wings): back faces light with the flipped normal.
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        // Thin sheets lit from behind glow with this share of the light (petals; 0 for buds).
        _Translucency    ("Translucency",     Range(0,1)) = 0
        // Lifts the shadow and mid bands toward full colour (flowers read bright in shade).
        _ShadowLift      ("Shadow Lift",      Range(0,1)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Geometry"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // ── Branch sway ──────────────────────────────────────────────────────
        // MIRROR CONTRACT — a copy of TreeFlex / TreeSway in BarkVertexColor.shader (and of
        // WindSway.cs). All three must change together, or the buds slide off their twigs.
        float4 _TreeWindA;
        float4 _TreeWindB;
        float  _TreeWindReachSpan;

        float TreeFlex(float3 posOS, float4 col)
        {
            float youngness = 1.0 - col.a;
            float reach     = saturate((length(posOS.xz) - _TreeWindB.w)
                                       / max(_TreeWindReachSpan, 0.001));
            float anchor    = max(saturate(posOS.y / max(_TreeWindB.z, 0.001)), reach);
            return (1.0 - col.r) * anchor * max(youngness, reach);
        }

        float3 TreeSway(float3 restWS, float flex)
        {
            float t     = _TreeWindB.x;
            float2 w    = _TreeWindA.xy;
            float phase = restWS.x * 1.7 + restWS.z * 2.3;
            float pB    = restWS.x * 0.9 - restWS.z * 1.3;
            float sMain = sin(t * 1.9 + phase) * 0.6 + sin(t * 3.7 + phase * 1.31) * 0.4;
            float sPerp = sin(t * 2.6 + pB);
            float amp   = _TreeWindB.y * _TreeWindA.w * (0.25 + 0.75 * _TreeWindA.z);
            float f2    = flex * flex;
            float3 d    = float3(w.x, 0, w.y) * (sMain * amp * f2)
                        + float3(-w.y, 0, w.x) * (sPerp * amp * 0.35 * f2);
            d.y        -= abs(sMain) * amp * 0.18 * f2;   // bend is an arc: tips settle down
            return d;
        }

        // UV1 = (isRoot, GrowthColor alpha) of the twig the bud sits on.
        float3 SwayedWS(float3 posOS, float2 flexData)
        {
            float3 ws = TransformObjectToWorld(posOS);
            return ws + TreeSway(ws, TreeFlex(posOS, float4(flexData.x, 0, 0, flexData.y)));
        }

        CBUFFER_START(UnityPerMaterial)
            float  _ShadowThreshold;
            float  _MidThreshold;
            float4 _ShadowTint;
            float4 _MidTint;
            float  _Translucency;
            float  _ShadowLift;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex   BudVert
            #pragma fragment BudFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct BudAttr
            {
                float4 posOS  : POSITION;
                float3 nrmOS  : NORMAL;
                half4  color  : COLOR;
                float2 flex   : TEXCOORD1;
            };
            struct BudVary
            {
                float4 posHCS : SV_POSITION;
                float3 nrmWS  : TEXCOORD0;
                float3 posWS  : TEXCOORD1;
                half4  color  : COLOR;
            };

            BudVary BudVert(BudAttr IN)
            {
                BudVary OUT;
                float3 ws  = SwayedWS(IN.posOS.xyz, IN.flex);
                OUT.posWS  = ws;
                OUT.posHCS = TransformWorldToHClip(ws);
                OUT.nrmWS  = TransformObjectToWorldNormal(IN.nrmOS);
                OUT.color  = IN.color;
                return OUT;
            }

            half4 BudFrag(BudVary IN, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float3 nrm      = normalize(IN.nrmWS) * IS_FRONT_VFACE(face, 1.0, -1.0);
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(IN.posWS));
                float ndl       = dot(nrm, normalize(mainLight.direction));
                float litVal    = saturate(max(ndl, -ndl * _Translucency)) * mainLight.shadowAttenuation;
                half3 tint = litVal < _ShadowThreshold ? _ShadowTint.rgb
                           : litVal < _MidThreshold    ? _MidTint.rgb
                           :                             half3(1, 1, 1);
                tint = lerp(tint, half3(1, 1, 1), _ShadowLift);
                return half4(IN.color.rgb * mainLight.color.rgb * tint, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex   DepthVert
            #pragma fragment DepthFrag

            struct DepthAttr { float4 posOS : POSITION; float2 flex : TEXCOORD1; };
            struct DepthVary { float4 posHCS : SV_POSITION; };

            DepthVary DepthVert(DepthAttr IN)
            {
                DepthVary OUT;
                OUT.posHCS = TransformWorldToHClip(SwayedWS(IN.posOS.xyz, IN.flex));   // must match ForwardLit
                return OUT;
            }
            half DepthFrag(DepthVary IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
