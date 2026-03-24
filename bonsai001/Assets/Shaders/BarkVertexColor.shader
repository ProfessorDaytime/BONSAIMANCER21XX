Shader "Custom/BarkVertexColor"
{
    // Two full PBR material sets — New Growth and Bark — blended by vertex alpha.
    //   vertex alpha = 0  →  fully new growth (young thin branches)
    //   vertex alpha = 1  →  fully bark        (mature thick branches)
    // The blend is driven by TreeMeshBuilder based on node radius and age.

    Properties
    {
        [Header(Bark)]
        _BarkColor        ("Color",             Color)      = (1,1,1,1)
        _BarkTex          ("Albedo",            2D)         = "white" {}
        [Normal]
        _BarkBump         ("Normal Map",        2D)         = "bump"  {}
        _BarkGlossiness   ("Smoothness",        Range(0,1)) = 0.01
        _BarkMetallic     ("Metallic",          Range(0,1)) = 0.0
        [HDR]
        _BarkEmissionColor("Emission Color",    Color)      = (0,0,0,1)
        _BarkEmissionMap  ("Emission Map",      2D)         = "white" {}
        _BarkOcclusionMap ("Occlusion Map",     2D)         = "white" {}
        _BarkOcclusionStr ("Occlusion Strength",Range(0,1)) = 1.0

        [Header(New Growth)]
        _NGColor          ("Color",             Color)      = (0.5,0.95,0.4,1)
        _NGTex            ("Albedo",            2D)         = "white" {}
        [Normal]
        _NGBump           ("Normal Map",        2D)         = "bump"  {}
        _NGGlossiness     ("Smoothness",        Range(0,1)) = 0.1
        _NGMetallic       ("Metallic",          Range(0,1)) = 0.0
        [HDR]
        _NGEmissionColor  ("Emission Color",    Color)      = (0,0,0,1)
        _NGEmissionMap    ("Emission Map",      2D)         = "white" {}
        _NGOcclusionMap   ("Occlusion Map",     2D)         = "white" {}
        _NGOcclusionStr   ("Occlusion Strength",Range(0,1)) = 1.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        // Bark
        sampler2D _BarkTex;
        sampler2D _BarkBump;
        sampler2D _BarkEmissionMap;
        sampler2D _BarkOcclusionMap;
        fixed4 _BarkColor;
        fixed4 _BarkEmissionColor;
        half   _BarkGlossiness;
        half   _BarkMetallic;
        half   _BarkOcclusionStr;

        // New Growth
        sampler2D _NGTex;
        sampler2D _NGBump;
        sampler2D _NGEmissionMap;
        sampler2D _NGOcclusionMap;
        fixed4 _NGColor;
        fixed4 _NGEmissionColor;
        half   _NGGlossiness;
        half   _NGMetallic;
        half   _NGOcclusionStr;

        struct Input
        {
            float2 uv_BarkTex;
            float2 uv_NGTex;
            float4 color : COLOR;   // alpha = bark blend weight (0=new growth, 1=bark)
        };

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float blend = saturate(IN.color.a);

            // Sample both material sets
            fixed4 barkAlbedo = tex2D(_BarkTex, IN.uv_BarkTex) * _BarkColor;
            fixed4 ngAlbedo   = tex2D(_NGTex,   IN.uv_NGTex)   * _NGColor;

            fixed3 barkNormal = UnpackNormal(tex2D(_BarkBump,   IN.uv_BarkTex));
            fixed3 ngNormal   = UnpackNormal(tex2D(_NGBump,     IN.uv_NGTex));

            fixed3 barkEmit = tex2D(_BarkEmissionMap, IN.uv_BarkTex).rgb * _BarkEmissionColor.rgb;
            fixed3 ngEmit   = tex2D(_NGEmissionMap,   IN.uv_NGTex).rgb   * _NGEmissionColor.rgb;

            float barkOcc = lerp(1.0, tex2D(_BarkOcclusionMap, IN.uv_BarkTex).g, _BarkOcclusionStr);
            float ngOcc   = lerp(1.0, tex2D(_NGOcclusionMap,   IN.uv_NGTex).g,   _NGOcclusionStr);

            // Blend everything by vertex alpha
            o.Albedo     = lerp(ngAlbedo.rgb,  barkAlbedo.rgb,  blend);
            o.Normal     = normalize(lerp(ngNormal, barkNormal, blend));
            o.Metallic   = lerp(_NGMetallic,   _BarkMetallic,   blend);
            o.Smoothness = lerp(_NGGlossiness, _BarkGlossiness, blend);
            o.Emission   = lerp(ngEmit,        barkEmit,        blend);
            o.Occlusion  = lerp(ngOcc,         barkOcc,         blend);
            o.Alpha      = 1.0;
        }
        ENDCG
    }

    FallBack "Diffuse"
}
