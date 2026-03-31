Shader "Hidden/CRT"
{
    Properties
    {
        _MainTex        ("Screen",              2D)             = "white" {}
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

        // --- NEW: Phosphor Mask ---
        _MaskStrength   ("Mask Strength",       Range(0, 1))    = 0.3
        _MaskScale      ("Mask Scale",          Range(1, 8))    = 3.0
        _MaskType       ("Mask Type 0=Shadow 1=Aperture", Range(0, 1)) = 0

        // --- NEW: Bloom / Phosphor Glow ---
        _BloomStr       ("Bloom Strength",      Range(0, 1))    = 0.15
        _BloomRadius    ("Bloom Radius",        Range(0, 6))    = 2.0

        // --- NEW: Phosphor Persistence ---
        _Persistence    ("Persistence",         Range(0, 0.9))  = 0.25

        // --- NEW: Horizontal Bandwidth Blur ---
        _HBlur          ("Horiz Bandwidth Blur",Range(0, 4))    = 1.0

        // --- NEW: Beam Intensity (bright pixels widen) ---
        _BeamWidth      ("Beam Width",          Range(0, 1))    = 0.4

        // --- NEW: Interlace ---
        _InterlaceStr   ("Interlace Strength",  Range(0, 1))    = 0.0

        // --- NEW: Noise & Jitter ---
        _NoiseStr       ("Noise Strength",      Range(0, 0.15)) = 0.02
        _JitterStr      ("Horiz Jitter",        Range(0, 0.003))= 0.0005
        _RollSpeed      ("Roll Line Speed",     Range(0, 8))    = 1.0

        // --- NEW: Corner Convergence ---
        _CornerRGB      ("Corner RGB Spread",   Range(0, 0.01)) = 0.002

        // --- NEW: CRT Gamma ---
        _InputGamma     ("Input Gamma",         Range(1, 3))    = 2.2
        _OutputGamma    ("Output Gamma",        Range(1, 3))    = 2.5
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4    _MainTex_TexelSize;   // (1/w, 1/h, w, h)

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


            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float4 pos    : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

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
            //  Increases toward edges, slight asymmetry
            // --------------------------------------------------------
            float3 SampleWithConvergence(float2 uv)
            {
                float2 center = uv - 0.5;
                float dist2 = dot(center, center);

                // Base split (uniform) + corner spread (radial)
                float2 splitR = float2( _RGBSplit + _CornerRGB * dist2,  _CornerRGB * center.y * 0.5);
                float2 splitB = float2(-_RGBSplit - _CornerRGB * dist2, -_CornerRGB * center.y * 0.5);

                // tex2Dlod with mip 0 — required when this function is called from
                // inside a dynamic loop (implicit gradient instructions are not valid there).
                float r = tex2Dlod(_MainTex, float4(uv + splitR, 0, 0)).r;
                float g = tex2Dlod(_MainTex, float4(uv,          0, 0)).g;
                float b = tex2Dlod(_MainTex, float4(uv + splitB, 0, 0)).b;
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
                    float2 off = float2(x * _MainTex_TexelSize.x, 0);
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
                        w *= w; // quadratic falloff for soft glow
                        float2 off = float2(x, y) * _MainTex_TexelSize.xy * 1.5;
                        sum += tex2Dlod(_MainTex, float4(uv + off, 0, 0)).rgb * w;
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

                    // Offset every other row for triangle pattern
                    int phase = (px + (py % 2)) % 3;

                    // Each phosphor dot lights one channel strongly, others dimly
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
            //  Main fragment
            // --------------------------------------------------------
            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = Curve(i.uv);

                // Out-of-bounds → black
                if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1)
                    return fixed4(0, 0, 0, 1);

                // ---------- Noise & horizontal jitter ----------
                float time = _Time.y;
                float noiseSeed = hash(float2(uv.y * 400.0, time * 100.0));

                // Per-line horizontal jitter
                float jitter = (noiseSeed - 0.5) * _JitterStr;
                uv.x += jitter;

                // Rolling interference bar
                float rollPos = frac(time * _RollSpeed * 0.1);
                float rollDist = abs(uv.y - rollPos);
                rollDist = min(rollDist, 1.0 - rollDist); // wrap
                float rollBar = smoothstep(0.0, 0.05, rollDist);

                // ---------- Sample colour with bandwidth blur + convergence ----------
                float3 col = HorizontalBlur(uv);

                // ---------- Input gamma (linearise from assumed sRGB-ish) ----------
                col = pow(max(col, 0.001), _InputGamma / 2.2);

                // ---------- Add bloom ----------
                col += Bloom(uv);

                // ---------- Phosphor persistence (blend with previous frame) ----------
                float3 prev = tex2D(_PrevFrame, uv).rgb;
                col = lerp(col, max(col, prev), _Persistence);

                // ---------- Scanlines with beam intensity modulation ----------
                float lum = dot(col, float3(0.299, 0.587, 0.114));
                // Bright areas → scanline gap shrinks (beam widens)
                float beamFill = lerp(1.0, lum, _BeamWidth);
                float scanPhase = sin(uv.y * _ScanlineFreq * UNITY_PI) * 0.5 + 0.5;
                // Sharpen the scanline, then modulate by beam width
                float scanline = smoothstep(0.5 - beamFill * 0.5, 0.5 + beamFill * 0.5, scanPhase);
                scanline = lerp(1.0, scanline, _ScanlineStr);
                col *= scanline;

                // ---------- Interlacing ----------
                if (_InterlaceStr > 0.01)
                {
                    float scanLine = floor(uv.y * _ScanlineFreq);
                    float field = fmod(floor(time * 30.0), 2.0); // alternating fields at ~30hz
                    float interlace = 1.0 - _InterlaceStr * step(0.5, fmod(scanLine + field, 2.0)) * 0.5;
                    col *= interlace;
                }

                // ---------- Phosphor mask ----------
                float2 screenPos = uv * _MainTex_TexelSize.zw; // pixel coordinates
                col *= PhosphorMask(screenPos);

                // ---------- Rolling bar interference ----------
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

                // CRT gamma: slightly higher than modern displays
                col = pow(max(col, 0.0), 2.2 / _OutputGamma);

                // CRT black floor: phosphors never truly go black
                col = max(col, 0.01);

                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
