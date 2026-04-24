Shader "Custom/BarkVertexColor"
{
    // ── Vertex color encoding (set by TreeMeshBuilder) ───────────────────────
    //   R = isRoot      (0 = branch, 1 = root)
    //   G = woundMask   (0 = healthy, 1 = fresh wound — fades with woundAge)
    //   B = pasteMask   (0 = none, 1 = cut paste applied)
    //   A = barkBlend   (0 = new growth / thin twig, 1 = mature bark)
    //
    // ── Bark patterns ────────────────────────────────────────────────────────
    //   _BarkType (int 1-18) selects the procedural pattern for mature bark.
    //   Thin branches always use Type-2 fine fissures regardless of species.
    //   The barkBlend weight fades between them — matching real bark development.
    //
    // ── Lighting ─────────────────────────────────────────────────────────────
    //   3-band cel shading (shadow / mid / lit) with a separate outline pass.

    Properties
    {
        [Header(Species Bark Colors)]
        [MainColor]
        _BarkColor        ("Mature Bark Color",   Color)      = (0.45,0.38,0.30,1)
        _NGColor          ("Young Bark (branch)", Color)      = (0.42,0.62,0.25,1)
        _NGRootColor      ("Young Bark (root)",   Color)      = (0.82,0.75,0.58,1)

        [Header(Bark Pattern)]
        [IntRange] _BarkType      ("Bark Type (1-18)", Range(1,18)) = 2
        _BarkPatternScale         ("Pattern Scale",    Float)      = 1.0
        _BarkGrooveDepth          ("Groove Darkness",  Range(0,1)) = 0.45

        [Header(Wound)]
        _WoundHeartColor   ("Heartwood Color",  Color) = (0.82,0.68,0.48,1)
        _WoundCambiumColor ("Cambium Ring",     Color) = (0.55,0.72,0.30,1)
        _WoundCallusColor  ("Callus Color",     Color) = (0.52,0.38,0.24,1)
        _PasteColor        ("Cut Paste Color",  Color) = (0.22,0.20,0.18,1)

        [Header(Bark Textures)]
        [NoScaleOffset] _BarkTexA     ("Young Bark Texture (optional)", 2D) = "white" {}
        [NoScaleOffset] _BarkTexB     ("Mature Bark Texture (optional)", 2D) = "white" {}
        _UseTextures      ("Use Textures (0=proc, 1=tex)", Float) = 0
        _TexelRes         ("Texel Resolution (e.g. 64)", Float) = 64
        _BarkNoiseMode    ("Noise Mode (0=scatter, 1=cellular)", Range(0,1)) = 0

        [Header(Debug)]
        [Toggle] _ForceMatureBark ("Force Mature Bark (test on primitives)", Float) = 0

        [Header(Cel Shading)]
        _ShadowThreshold  ("Shadow Threshold",  Range(0,1)) = 0.20
        _MidThreshold     ("Mid Threshold",     Range(0,1)) = 0.55
        _ShadowTint       ("Shadow Tint",       Color)      = (0.25,0.22,0.28,1)
        _MidTint          ("Mid Tint",          Color)      = (0.78,0.75,0.72,1)

        [Header(Outline)]
        _OutlineWidth     ("Outline Width",     Float)      = 0.018
        _OutlineColor     ("Outline Color",     Color)      = (0.10,0.08,0.06,1)
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

        // ────────────────────────────────────────────────────────────────────
        //  PASS 1 — Silhouette outline (backface inflate)
        // ────────────────────────────────────────────────────────────────────
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex   OutlineVert
            #pragma fragment OutlineFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BarkColor;
                float4 _NGColor;
                float4 _NGRootColor;
                int    _BarkType;
                float  _BarkPatternScale;
                float  _BarkGrooveDepth;
                float4 _WoundHeartColor;
                float4 _WoundCambiumColor;
                float4 _WoundCallusColor;
                float4 _PasteColor;
                float  _UseTextures;
                float  _TexelRes;
                float  _BarkNoiseMode;
                float  _ShadowThreshold;
                float  _MidThreshold;
                float4 _ShadowTint;
                float4 _MidTint;
                float  _OutlineWidth;
                float4 _OutlineColor;
                float  _ForceMatureBark;
            CBUFFER_END

            struct OAttr
            {
                float4 posOS  : POSITION;
                float3 nrmOS  : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct OVary
            {
                float4 posHCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            OVary OutlineVert(OAttr IN)
            {
                OVary OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                // Push outward in world space along the normal
                float3 posWS  = TransformObjectToWorld(IN.posOS.xyz);
                float3 nrmWS  = normalize(TransformObjectToWorldNormal(IN.nrmOS));
                posWS        += nrmWS * _OutlineWidth;
                OUT.posHCS    = TransformWorldToHClip(posWS);
                return OUT;
            }
            half4 OutlineFrag(OVary IN) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }

        // ────────────────────────────────────────────────────────────────────
        //  PASS 2 — Forward Lit (3-band cel + procedural bark)
        // ────────────────────────────────────────────────────────────────────
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
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BarkColor;
                float4 _NGColor;
                float4 _NGRootColor;
                int    _BarkType;
                float  _BarkPatternScale;
                float  _BarkGrooveDepth;
                float4 _WoundHeartColor;
                float4 _WoundCambiumColor;
                float4 _WoundCallusColor;
                float4 _PasteColor;
                float  _UseTextures;
                float  _TexelRes;
                float  _BarkNoiseMode;
                float  _ShadowThreshold;
                float  _MidThreshold;
                float4 _ShadowTint;
                float4 _MidTint;
                float  _OutlineWidth;
                float4 _OutlineColor;
                float  _ForceMatureBark;
            CBUFFER_END

            TEXTURE2D(_BarkTexA); SAMPLER(sampler_BarkTexA);
            TEXTURE2D(_BarkTexB); SAMPLER(sampler_BarkTexB);

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
                float2 uv          : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                float3 normalWS    : TEXCOORD2;
                half4  color       : TEXCOORD3;
                float  fogCoord    : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // ── Noise utilities (Unity ShaderGraph built-ins, inlined) ──────

            // Hash for 2D → float
            float Hash21(float2 p)
            {
                p = frac(p * float2(234.34, 435.345));
                p += dot(p, p + 34.23);
                return frac(p.x * p.y);
            }
            // Hash for 2D → 2D
            float2 Hash22(float2 p)
            {
                float2 q = float2(dot(p, float2(127.1, 311.7)),
                                  dot(p, float2(269.5, 183.3)));
                return frac(sin(q) * 43758.54);
            }

            // Simple noise (value noise)
            float SimpleNoise(float2 uv, float scale)
            {
                uv *= scale;
                float2 i = floor(uv), f = frac(uv);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash21(i),              Hash21(i + float2(1,0)), u.x),
                            lerp(Hash21(i + float2(0,1)), Hash21(i + float2(1,1)), u.x), u.y);
            }

            // Gradient noise
            float GradNoise(float2 uv, float scale)
            {
                uv *= scale;
                float2 i = floor(uv), f = frac(uv);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = dot(Hash22(i),              f);
                float b = dot(Hash22(i + float2(1,0)), f - float2(1,0));
                float c = dot(Hash22(i + float2(0,1)), f - float2(0,1));
                float d = dot(Hash22(i + float2(1,1)), f - float2(1,1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y) * 0.5 + 0.5;
            }

            // Voronoi — returns distance to nearest cell center
            float Voronoi(float2 uv, float2 scale, float angleOffset, out float cells)
            {
                uv *= scale;
                float2 g = floor(uv), f = frac(uv);
                float minDist = 8.0;
                float minCell = 0.0;
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    float2 nb  = float2(x, y);
                    float2 rnd = Hash22(g + nb);
                    rnd = 0.5 + 0.5 * sin(angleOffset + 6.2831 * rnd);
                    float2 r  = nb + rnd - f;
                    float  d  = dot(r, r);
                    if (d < minDist) { minDist = d; minCell = Hash21(g + nb); }
                }
                cells = minCell;
                return sqrt(minDist);
            }

            // ── Per-type pattern functions ───────────────────────────────────
            // All return a float [0,1]: 1 = bark surface, 0 = deep groove.

            float BarkType1(float2 uv)  // Smooth (Ficus, Beech)
            {
                return 0.85 + SimpleNoise(uv, 4.0) * 0.15;
            }

            float BarkType2(float2 uv)  // Fine fissures (Maple, Wisteria) — also the universal young-bark pattern
            {
                float n = GradNoise(uv * float2(10.0, 1.5), 1.0);
                return step(0.52, n);
            }

            float BarkType4(float2 uv)  // Interlacing ridges (Willow, Elm)
            {
                float nA = GradNoise(uv * float2(7.0, 1.0), 1.0);
                float nB = GradNoise(uv * float2(7.0, 1.0) + float2(0.35, 0.0), 1.0);
                return max(step(0.48, nA), step(0.48, nB));
            }

            float BarkType7(float2 uv)  // Vertical strip scales (Atlas Cedar)
            {
                float cells;
                float v = Voronoi(uv, float2(0.09, 4.5), 2.0, cells);
                return smoothstep(0.04, 0.18, v);
            }

            float BarkType8(float2 uv)  // Irregular close blocks (Spruce)
            {
                float cA, cB;
                float vA = Voronoi(uv, float2(1.3, 2.2), 2.0, cA);
                float vB = Voronoi(uv, float2(2.6, 3.0), 2.0, cB);
                return lerp(step(0.14, vA), step(0.10, vB), 0.40);
            }

            float BarkType9(float2 uv)  // Large plates (Pine)
            {
                float cells;
                float v = Voronoi(uv, float2(0.7, 1.1), 2.0, cells);
                // Thick dark border = plate edges; pale interior = plate face
                return 1.0 - saturate(v * 7.0);
            }

            float BarkType10(float2 uv)  // Peeling horizontal strips (Birch)
            {
                float warp = GradNoise(uv * float2(2.0, 0.35), 1.0) * 3.2;
                float bands = sin(uv.y * 20.0 + warp);
                return smoothstep(0.2, 0.7, bands * 0.5 + 0.5);
            }

            float BarkType12(float2 uv)  // Fibrous shreds (Juniper, Cedar, Redwood)
            {
                float n = SimpleNoise(uv * float2(38.0, 0.9), 1.0);
                return step(0.52, n);
            }

            float BarkType14(float2 uv)  // Spongy fibrous (Swamp Cypress)
            {
                float base = GradNoise(uv * float2(4.5, 0.45), 1.0);
                float edge = SimpleNoise(uv * float2(18.0, 2.5), 1.0);
                return step(0.42, base + edge * 0.22);
            }

            float BarkType16(float2 uv)  // Horizontal lenticels (Cherry)
            {
                float n = SimpleNoise(uv * float2(0.7, 28.0), 1.0);
                return step(0.70, n);  // sparse bright dashes on dark bark
            }

            float BarkPattern(float2 uv, int barkType)
            {
                // Scale the UV by the artist-controlled pattern scale
                uv *= _BarkPatternScale;

                if      (barkType == 1)  return BarkType1(uv);
                else if (barkType == 4)  return BarkType4(uv);
                else if (barkType == 7)  return BarkType7(uv);
                else if (barkType == 8)  return BarkType8(uv);
                else if (barkType == 9)  return BarkType9(uv);
                else if (barkType == 10) return BarkType10(uv);
                else if (barkType == 12) return BarkType12(uv);
                else if (barkType == 14) return BarkType14(uv);
                else if (barkType == 16) return BarkType16(uv);
                else                     return BarkType2(uv);  // default / type 2
            }

            // ── Vertex / Fragment ────────────────────────────────────────────

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
                OUT.uv          = IN.uv;
                OUT.color       = IN.color;
                OUT.fogCoord    = ComputeFogFactor(posInputs.positionCS.z);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                // ── Decode vertex channels ───────────────────────────────────
                // _ForceMatureBark overrides vertex data so primitives show clean bark.
                half isRoot    = _ForceMatureBark > 0.5h ? 0.0h : IN.color.r;
                half woundMask = _ForceMatureBark > 0.5h ? 0.0h : saturate(IN.color.g);
                half pasteMask = _ForceMatureBark > 0.5h ? 0.0h : saturate(IN.color.b);
                half blend     = _ForceMatureBark > 0.5h ? 1.0h : saturate(IN.color.a);

                // ── Bark pattern — layered ───────────────────────────────────
                // Young: always Type 2 fine fissures; mature: species type.
                float2 uv          = IN.uv;
                float youngPattern = BarkType2(uv * _BarkPatternScale);
                float maturePattern= BarkPattern(uv, _BarkType);
                float pattern      = lerp(youngPattern, maturePattern, blend);

                // ── Base species albedo ──────────────────────────────────────
                half4 ngTint  = lerp(_NGColor, _NGRootColor, isRoot);
                half3 albedo  = lerp(ngTint.rgb, _BarkColor.rgb, blend);

                // Grooves darken the color — pattern=1 is ridges, pattern=0 is groove
                albedo *= lerp(1.0 - _BarkGrooveDepth, 1.0, pattern);

                // ── Optional pixel-art texture tier blend ────────────────────
                // When _UseTextures is enabled, each texel flips from young→mature
                // texture via a hard noise threshold driven by vertex alpha (blend).
                // No blending — every texel is 100% one tier or the other.
                if (_UseTextures > 0.5)
                {
                    // Snap UV to nearest texel so noise aligns to pixel boundaries.
                    float2 tUV = floor(IN.uv * _TexelRes) / _TexelRes;

                    // ── Scatter mode (0): per-texel independent random ────────
                    float scatter = frac(sin(dot(tUV, float2(127.1h, 311.7h))) * 43758.5453h);

                    // ── Cellular mode (1): Voronoi cell distance ──────────────
                    float2 cellUV = tUV * 4.0;
                    float2 cellFloor = floor(cellUV);
                    float  minD = 1.0;
                    for (int dy2 = -1; dy2 <= 1; dy2++)
                    for (int dx2 = -1; dx2 <= 1; dx2++)
                    {
                        float2 nb2 = cellFloor + float2(dx2, dy2);
                        float2 pt  = nb2 + frac(sin(nb2 * float2(127.1h, 311.7h)) * 43758.5h);
                        minD = min(minD, length(cellUV - pt));
                    }

                    float noiseVal = lerp(scatter, saturate(minD), _BarkNoiseMode);

                    // Hard threshold: flip texel to mature (B) when noise < blend.
                    half4 texSample = noiseVal < blend
                        ? SAMPLE_TEXTURE2D(_BarkTexB, sampler_BarkTexB, IN.uv)
                        : SAMPLE_TEXTURE2D(_BarkTexA, sampler_BarkTexA, IN.uv);

                    // Replace procedural albedo with texture sample.
                    // Multiply by groove pattern so bark geometry still reads.
                    albedo = texSample.rgb * lerp(1.0 - _BarkGrooveDepth, 1.0, pattern);
                }

                // ── Wound layer ──────────────────────────────────────────────
                // Three zones driven by woundMask value:
                //   woundMask ≈ 0.8-1.0 → inner heartwood (fresh exposed wood)
                //   woundMask ≈ 0.3-0.7 → cambium ring (green edge)
                //   woundMask ≈ 0.0-0.3 → callus / healing bark
                if (woundMask > 0.01h)
                {
                    half3 woundZone;
                    if (woundMask > 0.65h)
                        woundZone = lerp(_WoundCambiumColor.rgb, _WoundHeartColor.rgb,
                                         smoothstep(0.65, 1.0, woundMask));
                    else
                        woundZone = lerp(_WoundCallusColor.rgb, _WoundCambiumColor.rgb,
                                         smoothstep(0.0, 0.65, woundMask));
                    albedo = lerp(albedo, woundZone, woundMask);
                }

                // ── Paste override ───────────────────────────────────────────
                // Hard threshold: prevents smooth-interpolated edge fragments from
                // tinting adjacent bark with the paste color.
                if (pasteMask > 0.5h)
                    albedo = _PasteColor.rgb;

                // ── Dead-wood check (passed in when isDead, vertex alpha pushed to 1
                //    and vertex.g overridden — handled via albedo already being grey) ──

                // ── 3-band cel lighting ──────────────────────────────────────
                float3 normalWS = normalize(IN.normalWS);

                #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE) || defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                #else
                    float4 shadowCoord = float4(0, 0, 0, 0);
                #endif

                Light mainLight = GetMainLight(shadowCoord);
                float NdotL     = dot(normalWS, normalize(mainLight.direction));
                float litVal    = saturate(NdotL) * mainLight.shadowAttenuation;

                // Step into 3 bands
                half3 lightTint;
                if      (litVal < _ShadowThreshold) lightTint = _ShadowTint.rgb;
                else if (litVal < _MidThreshold)    lightTint = _MidTint.rgb;
                else                                lightTint = half3(1.0, 1.0, 1.0);

                half3 finalColor = albedo * mainLight.color.rgb * lightTint;

                // Add additional lights (no cel banding on them — just additive fill)
                #ifdef _ADDITIONAL_LIGHTS
                    uint lightCount = GetAdditionalLightsCount();
                    for (uint i = 0u; i < lightCount; ++i)
                    {
                        Light addLight = GetAdditionalLight(i, IN.positionWS);
                        float addNdotL = saturate(dot(normalWS, addLight.direction));
                        finalColor    += albedo * addLight.color * addNdotL * addLight.distanceAttenuation * 0.3h;
                    }
                #endif

                finalColor = MixFog(finalColor, IN.fogCoord);
                return half4(finalColor, 1.0h);
            }
            ENDHLSL
        }

        // ────────────────────────────────────────────────────────────────────
        //  PASS 3 — Shadow Caster
        // ────────────────────────────────────────────────────────────────────
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

            CBUFFER_START(UnityPerMaterial)
                float4 _BarkColor;
                float4 _NGColor;
                float4 _NGRootColor;
                int    _BarkType;
                float  _BarkPatternScale;
                float  _BarkGrooveDepth;
                float4 _WoundHeartColor;
                float4 _WoundCambiumColor;
                float4 _WoundCallusColor;
                float4 _PasteColor;
                float  _UseTextures;
                float  _TexelRes;
                float  _BarkNoiseMode;
                float  _ShadowThreshold;
                float  _MidThreshold;
                float4 _ShadowTint;
                float4 _MidTint;
                float  _OutlineWidth;
                float4 _OutlineColor;
                float  _ForceMatureBark;
            CBUFFER_END

            float3 _LightDirection;

            struct ShadowAttr { float4 posOS : POSITION; float3 nrmOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct ShadowVary { float4 posHCS : SV_POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };

            ShadowVary ShadowVert(ShadowAttr IN)
            {
                ShadowVary OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                float3 posWS = TransformObjectToWorld(IN.posOS.xyz);
                float3 nrmWS = TransformObjectToWorldNormal(IN.nrmOS);
                OUT.posHCS   = TransformWorldToHClip(ApplyShadowBias(posWS, nrmWS, _LightDirection));
                return OUT;
            }
            half4 ShadowFrag(ShadowVary IN) : SV_Target { return 0; }
            ENDHLSL
        }

        // ────────────────────────────────────────────────────────────────────
        //  PASS 4 — Depth Only
        // ────────────────────────────────────────────────────────────────────
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

            CBUFFER_START(UnityPerMaterial)
                float4 _BarkColor;
                float4 _NGColor;
                float4 _NGRootColor;
                int    _BarkType;
                float  _BarkPatternScale;
                float  _BarkGrooveDepth;
                float4 _WoundHeartColor;
                float4 _WoundCambiumColor;
                float4 _WoundCallusColor;
                float4 _PasteColor;
                float  _UseTextures;
                float  _TexelRes;
                float  _BarkNoiseMode;
                float  _ShadowThreshold;
                float  _MidThreshold;
                float4 _ShadowTint;
                float4 _MidTint;
                float  _OutlineWidth;
                float4 _OutlineColor;
                float  _ForceMatureBark;
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
