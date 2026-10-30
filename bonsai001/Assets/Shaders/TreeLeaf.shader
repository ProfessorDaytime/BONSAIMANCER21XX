// Broadleaf foliage (2026-10-30), in place of URP Lit on the leaf materials. A leaf is a thin
// sheet, so both faces are drawn and each is lit from its own side; light wraps past the edge
// of the lit half instead of cutting off at a line; and sun striking the far face glows
// through it (translucency), brightest when looking toward the sun through the canopy. Lit by
// the main light and ambient only.
// The colour comes from _BaseColor, which Leaf sets per leaf through a MaterialPropertyBlock.
//
// Spring unfold: the blade folds along its midrib, each half turned down by _FoldAngle × _Fold
// (undersides together, as a maple leaf leaves the bud). Leaf sets _Fold per leaf; LeafManager
// finds the midrib in the model and stores it in object space in _FoldOrigin/_FoldAxis/_FoldUp,
// or sets _FoldAngle to 0 when the model can't be read.
Shader "Custom/TreeLeaf"
{
    Properties
    {
        _BaseColor        ("Colour",                         Color)       = (0.2, 0.55, 0.12, 1)
        _Wrap             ("Light Wrap",                     Range(0, 1)) = 0.35
        _ShadowFill       ("Sun Left in Shadow",             Range(0, 1)) = 0.45
        _Translucency     ("Translucency",                   Range(0, 2)) = 0.7
        _TranslucencyTint ("Translucency Tint",              Color)       = (1.0, 1.15, 0.55, 1)
        _ForwardScatter   ("Glow Looking Toward the Sun",    Range(0, 4)) = 1.5
        _Smoothness       ("Smoothness",                     Range(0, 1)) = 0.45
        _Specular         ("Specular",                       Range(0, 1)) = 0.12
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0

        _FoldAngle        ("Fold Angle (each half, folded)", Range(0, 90)) = 80
        [HideInInspector] _Fold       ("Fold",        Range(0, 1)) = 0
        [HideInInspector] _FoldOrigin ("Fold Origin", Vector)      = (0, 0, 0, 0)
        [HideInInspector] _FoldAxis   ("Fold Axis",   Vector)      = (0, 0, 1, 0)
        [HideInInspector] _FoldUp     ("Fold Up",     Vector)      = (1, 0, 0, 0)
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

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float  _Wrap;
            float  _ShadowFill;
            float  _Translucency;
            float4 _TranslucencyTint;
            float  _ForwardScatter;
            float  _Smoothness;
            float  _Specular;
            float  _FoldAngle;
            float  _Fold;
            float4 _FoldOrigin;
            float4 _FoldAxis;
            float4 _FoldUp;
        CBUFFER_END

        // Turns each half of the blade about the midrib line, the +w half by +θ and the −w half
        // by −θ, so both droop away from _FoldUp. Every pass folds, so shadows and depth match.
        void Fold(inout float3 posOS, inout float3 nrmOS)
        {
            if (_Fold <= 0.0) return;
            float3 a  = normalize(_FoldAxis.xyz);
            float3 w  = normalize(cross(a, _FoldUp.xyz));   // a × w = −up: +θ lowers the +w half
            float3 d  = posOS - _FoldOrigin.xyz;
            float  th = radians(_FoldAngle) * _Fold * (dot(d, w) >= 0.0 ? 1.0 : -1.0);
            float  c  = cos(th), s = sin(th);
            posOS = _FoldOrigin.xyz + d * c + cross(a, d) * s + a * dot(a, d) * (1.0 - c);
            nrmOS = nrmOS * c + cross(a, nrmOS) * s + a * dot(a, nrmOS) * (1.0 - c);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex   LeafVert
            #pragma fragment LeafFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct LeafAttr
            {
                float4 posOS : POSITION;
                float3 nrmOS : NORMAL;
            };
            struct LeafVary
            {
                float4 posHCS : SV_POSITION;
                float3 nrmWS  : TEXCOORD0;
                float3 posWS  : TEXCOORD1;
                float  fog    : TEXCOORD2;
            };

            LeafVary LeafVert(LeafAttr IN)
            {
                LeafVary OUT;
                float3 pos = IN.posOS.xyz, nrm = IN.nrmOS;
                Fold(pos, nrm);
                OUT.posWS  = TransformObjectToWorld(pos);
                OUT.posHCS = TransformWorldToHClip(OUT.posWS);
                OUT.nrmWS  = TransformObjectToWorldNormal(nrm);
                OUT.fog    = ComputeFogFactor(OUT.posHCS.z);
                return OUT;
            }

            // One light on one face: wrapped diffuse on the side facing it, light through the
            // leaf on the side facing away, and a soft sheen.
            half3 LeafLight(Light light, float3 n, float3 v, half3 albedo)
            {
                // A shaded leaf keeps _ShadowFill of the sun: light scattered through the canopy
                // above it, so shadow darkens a leaf without blacking it out.
                half  atten   = light.distanceAttenuation * lerp(_ShadowFill, 1.0, light.shadowAttenuation);
                float ndl     = dot(n, light.direction);
                half  diffuse = saturate((ndl + _Wrap) / (1.0 + _Wrap));
                half  toward  = pow(saturate(dot(v, -light.direction)), 4.0);
                half3 through = albedo * _TranslucencyTint.rgb
                              * (_Translucency * saturate(-ndl) * (0.35 + _ForwardScatter * toward));
                float3 h      = normalize(light.direction + v);
                half  sheen   = pow(saturate(dot(n, h)), exp2(1.0 + 10.0 * _Smoothness))
                              * _Specular * saturate(ndl);
                return light.color * atten * (albedo * diffuse + through + sheen);
            }

            half4 LeafFrag(LeafVary IN, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float3 n      = normalize(IN.nrmWS) * IS_FRONT_VFACE(face, 1.0, -1.0);
                float3 v      = GetWorldSpaceNormalizeViewDir(IN.posWS);
                half3  albedo = _BaseColor.rgb;

                half3 col = albedo * SampleSH(n);
                // The sun only, like the bark and buds: the scene's other directional and point
                // lights lit every face from some side and flattened the canopy (2026-10-30).
                col += LeafLight(GetMainLight(TransformWorldToShadowCoord(IN.posWS)), n, v, albedo);
                return half4(MixFog(col, IN.fog), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex   ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;   // set by URP for the light being rendered
            float3 _LightPosition;

            struct ShadowAttr { float4 posOS : POSITION; float3 nrmOS : NORMAL; };

            float4 ShadowVert(ShadowAttr IN) : SV_POSITION
            {
                float3 pos = IN.posOS.xyz, nrm = IN.nrmOS;
                Fold(pos, nrm);
                float3 posWS = TransformObjectToWorld(pos);
                float3 nrmWS = TransformObjectToWorldNormal(nrm);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDir = normalize(_LightPosition - posWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif
                float4 hcs = TransformWorldToHClip(ApplyShadowBias(posWS, nrmWS, lightDir));
                #if UNITY_REVERSED_Z
                    hcs.z = min(hcs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    hcs.z = max(hcs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return hcs;
            }
            half4 ShadowFrag() : SV_Target { return 0; }
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

            struct DepthAttr { float4 posOS : POSITION; float3 nrmOS : NORMAL; };

            float4 DepthVert(DepthAttr IN) : SV_POSITION
            {
                float3 pos = IN.posOS.xyz, nrm = IN.nrmOS;
                Fold(pos, nrm);   // must match ForwardLit
                return TransformObjectToHClip(pos);
            }
            half DepthFrag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
