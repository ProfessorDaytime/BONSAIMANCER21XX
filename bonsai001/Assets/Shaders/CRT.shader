Shader "Hidden/CRT"
{
    Properties
    {
        // _MainTex is replaced by _BlitTexture (provided by URP's Blitter)
        _PrevFrame      ("Previous Frame",      2D)             = "black" {}

        // --- Scanlines ---
        _ScanlineStr    ("Scanline Strength",   Range(0, 1))    = 0.25
        _ScanlineFreq   ("Scanline Frequency",  Range(100, 600))= 240

        // --- Screen Shape ---
        _Curvature      ("Curvature",           Range(0, 0.3))  = 0.08
        _Vignette       ("Vignette Strength",   Range(0, 2))    = 0.8

        // --- Colour ---
        _RGBSplit       ("RGB Split",           Range(0, 0.005))= 0.001
        _Brightness     ("Brightness",          Range(0.5, 2))  = 1.05

        // --- Phosphor Mask ---
        _MaskStrength   ("Mask Strength",       Range(0, 1))    = 0.3
        _MaskScale      ("Mask Scale",          Range(1, 8))    = 3.0
        _MaskType       ("Mask Type 0=Shadow 1=Aperture", Range(0, 1)) = 0

        // --- Bloom / Phosphor Glow ---
        _BloomStr       ("Bloom Strength",      Range(0, 1))    = 0.15
        _BloomRadius    ("Bloom Radius",        Range(0, 6))    = 2.0

        // --- Phosphor Persistence ---
        _Persistence    ("Persistence",         Range(0, 0.9))  = 0.25

        // --- Horizontal Bandwidth Blur ---
        _HBlur          ("Horiz Bandwidth Blur",Range(0, 4))    = 1.0

        // --- Beam Intensity ---
        _BeamWidth      ("Beam Width",          Range(0, 1))    = 0.4

        // --- Interlace ---
        _InterlaceStr   ("Interlace Strength",  Range(0, 1))    = 0.0

        // --- Noise & Jitter ---
        _NoiseStr       ("Noise Strength",      Range(0, 0.15)) = 0.02
        _JitterStr      ("Horiz Jitter",        Range(0, 0.003))= 0.0005
        _RollSpeed      ("Roll Line Speed",     Range(0, 8))    = 1.0

        // --- Corner Convergence ---
        _CornerRGB      ("Corner RGB Spread",   Range(0, 0.01)) = 0.002

        // --- CRT Gamma ---
        _InputGamma     ("Input Gamma",         Range(1, 3))    = 2.2
        _OutputGamma    ("Output Gamma",        Range(1, 3))    = 2.5
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // _BlitTexture and _BlitTexture_TexelSize come from Blit.hlsl
            sampler2D _PrevFrame;

            float _ScanlineStr;
            float _ScanlineFreq;
            float _Curvature;
            float _Vignette;
            float _RGBSplit;
            float _Brightness;
            float _MaskStrength;
            float _MaskScale;
            float _MaskType;
            float _BloomStr;
            float _BloomRadius;
            float _Persistence;
            float _HBlur;
            float _BeamWidth;
            float _InterlaceStr;
            float _NoiseStr;
            float _JitterStr;
            float _RollSpeed;
            float _CornerRGB;
            float _InputGamma;
            float _OutputGamma;

            // --------------------------------------------------------
            //  Utility: pseudo-random hash
            // --------------------------------------------------------
            float hash(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            // --------------------------------------------------------
            //  Barrel distortion
            // --------------------------------------------------------
            float2 Curve(float2 uv)
            {
                uv = uv * 2.0 - 1.0;
                float2 offset = abs(uv.yx) * _Curvature;
                uv += uv * offset * offset;
                uv = uv * 0.5 + 0.5;
                return uv;
            }

            // --------------------------------------------------------
            //  Corner-dependent chromatic aberration
            // --------------------------------------------------------
            float3 SampleWithConvergence(float2 uv)
            {
                float2 center = uv - 0.5;
                float dist2 = dot(center, center);

                float2 splitR = float2( _RGBSplit + _CornerRGB * dist2,  _CornerRGB * center.y * 0.5);
                float2 splitB = float2(-_RGBSplit - _CornerRGB * dist2, -_CornerRGB * center.y * 0.5);

                float r = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + splitR, 0).r;
                float g = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv,          0).g;
                float b = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + splitB, 0).b;
                return float3(r, g, b);
            }

            // --------------------------------------------------------
            //  Horizontal bandwidth blur (box filter along X)
            // --------------------------------------------------------
            float3 HorizontalBlur(float2 uv)
            {
                if (_HBlur < 0.01) return SampleWithConvergence(uv);

                float3 sum = 0;
                float  tw  = 0;
                int samples = (int)ceil(_HBlur);

                [loop]
                for (int x = -samples; x <= samples; x++)
                {
                    float w = 1.0 - abs((float)x) / (_HBlur + 0.001);
                    w = max(w, 0);
                    float2 off = float2(x * _BlitTexture_TexelSize.x, 0);
                    sum += SampleWithConvergence(uv + off) * w;
                    tw  += w;
                }
                return sum / tw;
            }

            // --------------------------------------------------------
            //  Simple phosphor bloom (small-kernel glow)
            // --------------------------------------------------------
            float3 Bloom(float2 uv)
            {
                if (_BloomStr < 0.01) return float3(0, 0, 0);

                float3 sum = 0;
                float  tw  = 0;
                int r = (int)ceil(_BloomRadius);

                [loop]
                for (int y = -r; y <= r; y++)
                {
                    [loop]
                    for (int x = -r; x <= r; x++)
                    {
                        float d = length(float2(x, y));
                        if (d > _BloomRadius) continue;
                        float w = 1.0 - d / (_BloomRadius + 0.001);
                        w *= w;
                        float2 off = float2(x, y) * _BlitTexture_TexelSize.xy * 1.5;
                        sum += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + off, 0).rgb * w;
                        tw  += w;
                    }
                }
                return (sum / tw) * _BloomStr;
            }

            // --------------------------------------------------------
            //  Phosphor mask patterns
            // --------------------------------------------------------
            float3 PhosphorMask(float2 screenPos)
            {
                if (_MaskStrength < 0.01) return float3(1, 1, 1);

                float3 mask;

                if (_MaskType < 0.5)
                {
                    // Shadow mask: dot triad pattern
                    int px = (int)floor(screenPos.x / _MaskScale);
                    int py = (int)floor(screenPos.y / _MaskScale);
                    int phase = (px + (py % 2)) % 3;

                    float lo = 1.0 - _MaskStrength * 0.8;
                    float hi = 1.0;
                    mask = float3(lo, lo, lo);
                    if (phase == 0) mask.r = hi;
                    if (phase == 1) mask.g = hi;
                    if (phase == 2) mask.b = hi;
                }
                else
                {
                    // Aperture grille (Trinitron): vertical RGB stripes
                    int px = (int)floor(screenPos.x / _MaskScale);
                    int phase = px % 3;

                    float lo = 1.0 - _MaskStrength * 0.8;
                    float hi = 1.0;
                    mask = float3(lo, lo, lo);
                    if (phase == 0) mask.r = hi;
                    if (phase == 1) mask.g = hi;
                    if (phase == 2) mask.b = hi;
                }

                return mask;
            }

            // --------------------------------------------------------
            //  Main fragment — input.texcoord is the screen UV from Blit.hlsl Vert
            // --------------------------------------------------------
            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = Curve(input.texcoord);

                if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1)
                    return half4(0, 0, 0, 1);

                // ---------- Noise & horizontal jitter ----------
                float time = _Time.y;
                float noiseSeed = hash(float2(uv.y * 400.0, time * 100.0));

                float jitter = (noiseSeed - 0.5) * _JitterStr;
                uv.x += jitter;

                float rollPos  = frac(time * _RollSpeed * 0.1);
                float rollDist = abs(uv.y - rollPos);
                rollDist = min(rollDist, 1.0 - rollDist);
                float rollBar  = smoothstep(0.0, 0.05, rollDist);

                // ---------- Sample colour ----------
                float3 col = HorizontalBlur(uv);

                // ---------- Input gamma ----------
                col = pow(max(col, 0.001), _InputGamma / 2.2);

                // ---------- Add bloom ----------
                col += Bloom(uv);

                // ---------- Phosphor persistence ----------
                float3 prev = tex2D(_PrevFrame, uv).rgb;
                col = lerp(col, max(col, prev), _Persistence);

                // ---------- Scanlines with beam intensity ----------
                float lum      = dot(col, float3(0.299, 0.587, 0.114));
                float beamFill = lerp(1.0, lum, _BeamWidth);
                float scanPhase = sin(uv.y * _ScanlineFreq * PI) * 0.5 + 0.5;
                float scanline  = smoothstep(0.5 - beamFill * 0.5, 0.5 + beamFill * 0.5, scanPhase);
                scanline = lerp(1.0, scanline, _ScanlineStr);
                col *= scanline;

                // ---------- Interlacing ----------
                if (_InterlaceStr > 0.01)
                {
                    float scanLine = floor(uv.y * _ScanlineFreq);
                    float field    = fmod(floor(time * 30.0), 2.0);
                    float interlace = 1.0 - _InterlaceStr * step(0.5, fmod(scanLine + field, 2.0)) * 0.5;
                    col *= interlace;
                }

                // ---------- Phosphor mask ----------
                float2 screenPos = uv * _BlitTexture_TexelSize.zw;
                col *= PhosphorMask(screenPos);

                // ---------- Rolling bar ----------
                col *= lerp(1.0 - _NoiseStr * 3.0, 1.0, rollBar);

                // ---------- Static noise ----------
                float noise = hash(float2(uv.x * 800.0 + time * 37.0, uv.y * 600.0 + time * 53.0));
                col += (noise - 0.5) * _NoiseStr;

                // ---------- Vignette ----------
                float2 vig = uv * (1.0 - uv);
                float v = vig.x * vig.y * 16.0;
                v = saturate(pow(v, _Vignette * 0.25));
                col *= v;

                // ---------- Brightness & output gamma ----------
                col *= _Brightness;
                col  = pow(max(col, 0.0), 2.2 / _OutputGamma);
                col  = max(col, 0.01);

                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
