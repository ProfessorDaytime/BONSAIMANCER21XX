Shader "Custom/BarkVertexColor"
{
    // Two full PBR material sets — New Growth and Bark — blended by vertex alpha.
    //   vertex alpha = 0  →  fully new growth (young thin branches)
    //   vertex alpha = 1  →  fully bark        (mature thick branches)
    //   vertex red   = 0  →  branch (green new growth color)
    //   vertex red   = 1  →  root   (white/beige new growth color)
    // The blend is driven by TreeMeshBuilder based on node radius and age.
    //
    // URP HLSL rewrite — replaces the old surface shader.
    // Properties use Roughness (not Smoothness) per artist preference.

    Properties
    {
        [Header(Bark)]
        _BarkColor        ("Color",             Color)      = (1,1,1,1)
        _BarkTex          ("Albedo",            2D)         = "white" {}
        [Normal]
        _BarkBump         ("Normal Map",        2D)         = "bump"  {}
        _BarkRoughness    ("Roughness",         Range(0,1)) = 0.99
        _BarkMetallic     ("Metallic",          Range(0,1)) = 0.0
        [HDR]
        _BarkEmissionColor("Emission Color",    Color)      = (0,0,0,1)
        _BarkEmissionMap  ("Emission Map",      2D)         = "white" {}
        _BarkOcclusionMap ("Occlusion Map",     2D)         = "white" {}
        _BarkOcclusionStr ("Occlusion Strength",Range(0,1)) = 1.0

        [Header(New Growth)]
        _NGColor          ("Color (branches)",  Color)      = (0.5,0.95,0.4,1)
        _NGRootColor      ("Color (roots)",     Color)      = (0.92,0.90,0.85,1)
        _NGTex            ("Albedo",            2D)         = "white" {}
        [Normal]
        _NGBump           ("Normal Map",        2D)         = "bump"  {}
        _NGRoughness      ("Roughness",         Range(0,1)) = 0.9
        _NGMetallic       ("Metallic",          Range(0,1)) = 0.0
        [HDR]
        _NGEmissionColor  ("Emission Color",    Color)      = (0,0,0,1)
        _NGEmissionMap    ("Emission Map",      2D)         = "white" {}
        _NGOcclusionMap   ("Occlusion Map",     2D)         = "white" {}
        _NGOcclusionStr   ("Occlusion Strength",Range(0,1)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Geometry"
        }
        LOD 300

        // ────────────────────────────────────────────────────────────────────────────
        //  FORWARD LIT PASS
        // ────────────────────────────────────────────────────────────────────────────
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ LIGHTMAP_ON DIRLIGHTMAP_COMBINED
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // ── Per-material constants (SRP Batcher compatible) ──────────────────
            CBUFFER_START(UnityPerMaterial)
                float4 _BarkTex_ST;
                float4 _NGTex_ST;
                float4 _BarkColor;
                float4 _BarkEmissionColor;
                float  _BarkRoughness;
                float  _BarkMetallic;
                float  _BarkOcclusionStr;
                float4 _NGColor;
                float4 _NGRootColor;
                float4 _NGEmissionColor;
                float  _NGRoughness;
                float  _NGMetallic;
                float  _NGOcclusionStr;
            CBUFFER_END

            TEXTURE2D(_BarkTex);          SAMPLER(sampler_BarkTex);
            TEXTURE2D(_BarkBump);         SAMPLER(sampler_BarkBump);
            TEXTURE2D(_BarkEmissionMap);  SAMPLER(sampler_BarkEmissionMap);
            TEXTURE2D(_BarkOcclusionMap); SAMPLER(sampler_BarkOcclusionMap);
            TEXTURE2D(_NGTex);            SAMPLER(sampler_NGTex);
            TEXTURE2D(_NGBump);           SAMPLER(sampler_NGBump);
            TEXTURE2D(_NGEmissionMap);    SAMPLER(sampler_NGEmissionMap);
            TEXTURE2D(_NGOcclusionMap);   SAMPLER(sampler_NGOcclusionMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uvBark      : TEXCOORD0;
                float2 uvNG        : TEXCOORD1;
                float3 positionWS  : TEXCOORD2;
                float3 normalWS    : TEXCOORD3;
                float4 tangentWS   : TEXCOORD4;  // xyz=tangent w=sign
                half4  color       : TEXCOORD5;
                float  fogCoord    : TEXCOORD6;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   nrmInputs = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);

                OUT.positionHCS = posInputs.positionCS;
                OUT.positionWS  = posInputs.positionWS;
                OUT.normalWS    = nrmInputs.normalWS;
                OUT.tangentWS   = float4(nrmInputs.tangentWS, IN.tangentOS.w * GetOddNegativeScale());
                OUT.uvBark      = TRANSFORM_TEX(IN.uv, _BarkTex);
                OUT.uvNG        = TRANSFORM_TEX(IN.uv, _NGTex);
                OUT.color       = IN.color;
                OUT.fogCoord    = ComputeFogFactor(posInputs.positionCS.z);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                half blend  = saturate(IN.color.a);  // 0=new growth, 1=bark
                half isRoot = IN.color.r;            // 0=branch, 1=root

                // ── Albedo ──────────────────────────────────────────────────────────
                half4 ngTint  = lerp(_NGColor, _NGRootColor, isRoot);
                half4 barkAlb = SAMPLE_TEXTURE2D(_BarkTex, sampler_BarkTex, IN.uvBark) * _BarkColor;
                half4 ngAlb   = SAMPLE_TEXTURE2D(_NGTex,   sampler_NGTex,   IN.uvNG)   * ngTint;
                half3 albedo  = lerp(ngAlb.rgb, barkAlb.rgb, blend);

                // ── Normal (tangent space) ──────────────────────────────────────────
                half3 barkN   = UnpackNormal(SAMPLE_TEXTURE2D(_BarkBump, sampler_BarkBump, IN.uvBark));
                half3 ngN     = UnpackNormal(SAMPLE_TEXTURE2D(_NGBump,   sampler_NGBump,   IN.uvNG));
                half3 normalTS = normalize(lerp(ngN, barkN, blend));

                // ── Occlusion ───────────────────────────────────────────────────────
                half barkOcc   = lerp(1.0h, SAMPLE_TEXTURE2D(_BarkOcclusionMap, sampler_BarkOcclusionMap, IN.uvBark).g, _BarkOcclusionStr);
                half ngOcc     = lerp(1.0h, SAMPLE_TEXTURE2D(_NGOcclusionMap,   sampler_NGOcclusionMap,   IN.uvNG).g,   _NGOcclusionStr);
                half occlusion = lerp(ngOcc, barkOcc, blend);

                // ── Emission ────────────────────────────────────────────────────────
                half3 barkEmit = SAMPLE_TEXTURE2D(_BarkEmissionMap, sampler_BarkEmissionMap, IN.uvBark).rgb * _BarkEmissionColor.rgb;
                half3 ngEmit   = SAMPLE_TEXTURE2D(_NGEmissionMap,   sampler_NGEmissionMap,   IN.uvNG).rgb   * _NGEmissionColor.rgb;
                half3 emission = lerp(ngEmit, barkEmit, blend);

                // ── PBR scalars (Roughness exposed, converted to Smoothness for URP) ─
                half metallic  = lerp(_NGMetallic,  _BarkMetallic,  blend);
                half roughness = lerp(_NGRoughness, _BarkRoughness, blend);

                // ── World-space normal from TBN ─────────────────────────────────────
                float3 bitangentWS = cross(IN.normalWS, IN.tangentWS.xyz) * IN.tangentWS.w;
                float3x3 TBN       = float3x3(IN.tangentWS.xyz, bitangentWS, IN.normalWS);
                float3 normalWS    = normalize(TransformTangentToWorld(normalTS, TBN));

                // ── Fill URP surface data ───────────────────────────────────────────
                SurfaceData surfaceData;
                ZERO_INITIALIZE(SurfaceData, surfaceData);
                surfaceData.albedo     = albedo;
                surfaceData.metallic   = metallic;
                surfaceData.smoothness = 1.0h - roughness;
                surfaceData.normalTS   = normalTS;
                surfaceData.emission   = emission;
                surfaceData.occlusion  = occlusion;
                surfaceData.alpha      = 1.0h;

                // ── Fill URP input data ─────────────────────────────────────────────
                InputData inputData;
                ZERO_INITIALIZE(InputData, inputData);
                inputData.positionWS              = IN.positionWS;
                inputData.normalWS                = normalWS;
                inputData.viewDirectionWS         = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                inputData.fogCoord                = IN.fogCoord;
                inputData.vertexLighting          = half3(0, 0, 0);
                inputData.bakedGI                 = half3(0, 0, 0);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionHCS);
                inputData.shadowMask              = half4(1, 1, 1, 1);

                #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE) || defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    inputData.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                #else
                    inputData.shadowCoord = float4(0, 0, 0, 0);
                #endif

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb   = MixFog(color.rgb, IN.fogCoord);
                return color;
            }
            ENDHLSL
        }

        // ────────────────────────────────────────────────────────────────────────────
        //  SHADOW CASTER PASS
        // ────────────────────────────────────────────────────────────────────────────
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back

            HLSLPROGRAM
            #pragma vertex   ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            // Must match ForwardLit CBUFFER exactly for SRP Batcher
            CBUFFER_START(UnityPerMaterial)
                float4 _BarkTex_ST;
                float4 _NGTex_ST;
                float4 _BarkColor;
                float4 _BarkEmissionColor;
                float  _BarkRoughness;
                float  _BarkMetallic;
                float  _BarkOcclusionStr;
                float4 _NGColor;
                float4 _NGRootColor;
                float4 _NGEmissionColor;
                float  _NGRoughness;
                float  _NGMetallic;
                float  _NGOcclusionStr;
            CBUFFER_END

            float3 _LightDirection;

            struct ShadowAttr
            {
                float4 posOS : POSITION;
                float3 nrmOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct ShadowVary
            {
                float4 posHCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            ShadowVary ShadowVert(ShadowAttr IN)
            {
                ShadowVary OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                float3 posWS  = TransformObjectToWorld(IN.posOS.xyz);
                float3 nrmWS  = TransformObjectToWorldNormal(IN.nrmOS);
                OUT.posHCS    = TransformWorldToHClip(ApplyShadowBias(posWS, nrmWS, _LightDirection));
                return OUT;
            }
            half4 ShadowFrag(ShadowVary IN) : SV_Target { return 0; }
            ENDHLSL
        }

        // ────────────────────────────────────────────────────────────────────────────
        //  DEPTH ONLY PASS
        // ────────────────────────────────────────────────────────────────────────────
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R Cull Back

            HLSLPROGRAM
            #pragma vertex   DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Must match ForwardLit CBUFFER exactly for SRP Batcher
            CBUFFER_START(UnityPerMaterial)
                float4 _BarkTex_ST;
                float4 _NGTex_ST;
                float4 _BarkColor;
                float4 _BarkEmissionColor;
                float  _BarkRoughness;
                float  _BarkMetallic;
                float  _BarkOcclusionStr;
                float4 _NGColor;
                float4 _NGRootColor;
                float4 _NGEmissionColor;
                float  _NGRoughness;
                float  _NGMetallic;
                float  _NGOcclusionStr;
            CBUFFER_END

            struct DepthAttr { float4 posOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct DepthVary { float4 posHCS : SV_POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };

            DepthVary DepthVert(DepthAttr IN)
            {
                DepthVary OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                OUT.posHCS = TransformObjectToHClip(IN.posOS.xyz);
                return OUT;
            }
            half DepthFrag(DepthVary IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
