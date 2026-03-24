Shader "Hidden/CRT"
{
    Properties
    {
        _MainTex      ("Screen",          2D)      = "white" {}
        _ScanlineStr  ("Scanline Strength",  Range(0, 1)) = 0.25
        _ScanlineFreq ("Scanline Frequency", Range(100, 600)) = 240
        _Curvature    ("Curvature",          Range(0, 0.3)) = 0.08
        _Vignette     ("Vignette Strength",  Range(0, 2))   = 0.8
        _RGBSplit     ("RGB Split",          Range(0, 0.005)) = 0.001
        _Brightness   ("Brightness",         Range(0.5, 2))  = 1.05
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
            float4    _MainTex_TexelSize;
            float _ScanlineStr;
            float _ScanlineFreq;
            float _Curvature;
            float _Vignette;
            float _RGBSplit;
            float _Brightness;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float4 pos    : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            // Barrel distortion — bends the image inward so it looks like a curved tube screen
            float2 Curve(float2 uv)
            {
                uv = uv * 2.0 - 1.0;
                float2 offset = abs(uv.yx) * _Curvature;
                uv += uv * offset * offset;
                uv = uv * 0.5 + 0.5;
                return uv;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = Curve(i.uv);

                // Clamp — pixels outside the curved screen go black
                if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1)
                    return fixed4(0, 0, 0, 1);

                // Slight RGB chromatic aberration
                float r = tex2D(_MainTex, uv + float2( _RGBSplit, 0)).r;
                float g = tex2D(_MainTex, uv).g;
                float b = tex2D(_MainTex, uv - float2( _RGBSplit, 0)).b;
                fixed4 col = fixed4(r, g, b, 1);

                // Scanlines — a sine wave darkens every other line
                float scanline = sin(uv.y * _ScanlineFreq * UNITY_PI) * 0.5 + 0.5;
                scanline = 1.0 - _ScanlineStr * (1.0 - scanline * scanline);
                col.rgb *= scanline;

                // Vignette — darken corners
                float2 vig = uv * (1.0 - uv);
                float  v   = vig.x * vig.y * 16.0;
                v = saturate(pow(v, _Vignette * 0.25));
                col.rgb *= v;

                col.rgb *= _Brightness;
                return col;
            }
            ENDCG
        }
    }
}
