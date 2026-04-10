using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

/// <summary>
/// URP 17 Renderer Feature for the CRT post-process effect.
/// Uses RenderGraphUtils.BlitMaterialParameters / renderGraph.AddBlitPass —
/// the canonical URP 17 pattern that works with Intermediate Texture = Auto.
/// </summary>
public sealed class CRTRendererFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public sealed class CRTSettings
    {
        public Shader shader;

        [Header("Scanlines")]
        [Range(0f, 1f)]    public float scanlineStrength  = 0.25f;
        [Range(100, 600)]  public float scanlineFrequency = 240f;

        [Header("Screen Shape")]
        [Range(0f, 0.3f)]  public float curvature = 0.08f;
        [Range(0f, 2f)]    public float vignette  = 0.8f;

        [Header("Colour")]
        [Range(0f, 0.005f)] public float rgbSplit   = 0.001f;
        [Range(0.5f, 2f)]   public float brightness = 1.05f;

        [Header("Phosphor Mask")]
        [Range(0f, 1f)]    public float maskStrength = 0.3f;
        [Range(1f, 8f)]    public float maskScale    = 3f;
        [Range(0f, 1f)]    public float maskType     = 0f;

        [Header("Phosphor Glow")]
        [Range(0f, 1f)]    public float bloomStrength = 0.15f;
        [Range(0f, 6f)]    public float bloomRadius   = 2f;

        [Header("Phosphor Persistence")]
        [Range(0f, 0.9f)]  public float persistence = 0.25f;

        [Header("Horizontal Bandwidth")]
        [Range(0f, 4f)]    public float horizontalBlur = 1f;

        [Header("Beam Intensity")]
        [Range(0f, 1f)]    public float beamWidth = 0.4f;

        [Header("Interlacing")]
        [Range(0f, 1f)]    public float interlaceStrength = 0f;

        [Header("Noise and Jitter")]
        [Range(0f, 0.15f)]  public float noiseStrength    = 0.02f;
        [Range(0f, 0.003f)] public float horizontalJitter = 0.0005f;
        [Range(0f, 8f)]     public float rollSpeed        = 1f;

        [Header("Corner Convergence")]
        [Range(0f, 0.01f)]  public float cornerConvergence = 0.002f;

        [Header("CRT Gamma")]
        [Range(1f, 3f)]    public float inputGamma  = 2.2f;
        [Range(1f, 3f)]    public float outputGamma = 2.5f;
    }

    public CRTSettings settings = new CRTSettings();

    CRTPass m_Pass;
    Material m_Material;

    public override void Create()
    {
        Debug.Log("[CRT] *** Create() called — feature is initializing ***");
        m_Pass = new CRTPass();
        m_Pass.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
    }

    static int s_AddPassFrame = -1;
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        bool doLog = Time.frameCount != s_AddPassFrame && Time.frameCount % 60 == 0;
        if (doLog) s_AddPassFrame = Time.frameCount;

        var camType = renderingData.cameraData.cameraType;
        if (doLog) Debug.Log($"[CRT] AddRenderPasses | camType={camType} shader={(settings.shader != null ? settings.shader.name : "NULL")} mat={(m_Material != null ? "OK" : "NULL")}");

        if (camType == CameraType.Preview || camType == CameraType.SceneView) return;

        if (settings.shader == null)
        {
            settings.shader = Shader.Find("Hidden/CRT");
            if (settings.shader == null)
            {
                Debug.LogWarning("[CRTRendererFeature] Hidden/CRT shader not found.");
                return;
            }
        }

        if (m_Material == null)
            m_Material = CoreUtils.CreateEngineMaterial(settings.shader);

        if (doLog) Debug.Log($"[CRT] Enqueueing pass | mat={m_Material.name} shader={settings.shader.name} matValid={m_Material.shader != null}");

        SetMaterialProps(m_Material);
        m_Pass.Setup(m_Material, m_Pass.GetPrevRT());
        renderer.EnqueuePass(m_Pass);
    }

    void SetMaterialProps(Material mat)
    {
        mat.SetFloat("_ScanlineStr",  settings.scanlineStrength);
        mat.SetFloat("_ScanlineFreq", settings.scanlineFrequency);
        mat.SetFloat("_Curvature",    settings.curvature);
        mat.SetFloat("_Vignette",     settings.vignette);
        mat.SetFloat("_RGBSplit",     settings.rgbSplit);
        mat.SetFloat("_Brightness",   settings.brightness);
        mat.SetFloat("_MaskStrength", settings.maskStrength);
        mat.SetFloat("_MaskScale",    settings.maskScale);
        mat.SetFloat("_MaskType",     settings.maskType);
        mat.SetFloat("_BloomStr",     settings.bloomStrength);
        mat.SetFloat("_BloomRadius",  settings.bloomRadius);
        mat.SetFloat("_Persistence",  settings.persistence);
        mat.SetFloat("_HBlur",        settings.horizontalBlur);
        mat.SetFloat("_BeamWidth",    settings.beamWidth);
        mat.SetFloat("_InterlaceStr", settings.interlaceStrength);
        mat.SetFloat("_NoiseStr",     settings.noiseStrength);
        mat.SetFloat("_JitterStr",    settings.horizontalJitter);
        mat.SetFloat("_RollSpeed",    settings.rollSpeed);
        mat.SetFloat("_CornerRGB",    settings.cornerConvergence);
        mat.SetFloat("_InputGamma",   settings.inputGamma);
        mat.SetFloat("_OutputGamma",  settings.outputGamma);
    }

    protected override void Dispose(bool disposing)
    {
        m_Pass?.Cleanup();
        CoreUtils.Destroy(m_Material);
        m_Material = null;
    }
}

// ─────────────────────────────────────────────────────────────────────────────

sealed class CRTPass : ScriptableRenderPass
{
    Material  m_Mat;
    RTHandle  m_PrevRT;
    static int s_DiagFrame = -1;

    public RTHandle GetPrevRT() => m_PrevRT;

    public CRTPass()
    {
        // Must be set before AddRenderPasses returns so URP allocates
        // the intermediate texture before RecordRenderGraph runs.
        requiresIntermediateTexture = true;
    }

    public void Setup(Material mat, RTHandle prevRT)
    {
        m_Mat    = mat;
        m_PrevRT = prevRT;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        bool doLog = Time.frameCount != s_DiagFrame && Time.frameCount % 60 == 0;
        if (doLog) s_DiagFrame = Time.frameCount;

        if (m_Mat == null) { if (doLog) Debug.Log("[CRTPass] RecordRenderGraph — mat is NULL, skipping"); return; }

        var resourceData = frameData.Get<UniversalResourceData>();

        if (doLog) Debug.Log($"[CRTPass] RecordRenderGraph | isBackBuffer={resourceData.isActiveTargetBackBuffer} source.valid={resourceData.activeColorTexture.IsValid()}");

        if (resourceData.isActiveTargetBackBuffer)
        {
            Debug.LogError("[CRTPass] Active target is BackBuffer — intermediate texture required.");
            return;
        }

        var source = resourceData.activeColorTexture;

        // Allocate/resize persistent prev-frame RT for phosphor persistence.
        var srcDesc   = renderGraph.GetTextureDesc(source);
        var prevTDesc = new TextureDesc(srcDesc.width, srcDesc.height)
        {
            format     = srcDesc.format,
            filterMode = FilterMode.Bilinear,
            wrapMode   = TextureWrapMode.Clamp,
            name       = "_CRT_Prev",
        };
        RenderingUtils.ReAllocateHandleIfNeeded(ref m_PrevRT, prevTDesc, "_CRT_Prev");

        if (m_PrevRT?.rt != null)
            m_Mat.SetTexture("_PrevFrame", m_PrevRT.rt);

        // Create destination, blit with CRT material, swap as new cameraColor
        var destDesc  = renderGraph.GetTextureDesc(source);
        destDesc.name = "_CRT_Out";
        destDesc.clearBuffer = false;
        TextureHandle dest = renderGraph.CreateTexture(destDesc);

        var blitParams = new RenderGraphUtils.BlitMaterialParameters(source, dest, m_Mat, 0);
        renderGraph.AddBlitPass(blitParams, "CRT Effect");

        // Swap so subsequent passes (and the final output) see the CRT result
        resourceData.cameraColor = dest;

        // Save this frame's result into prevRT for next-frame persistence.
        if (m_PrevRT?.rt != null)
        {
            TextureHandle prevHandle = renderGraph.ImportTexture(m_PrevRT);
            renderGraph.AddBlitPass(dest, prevHandle, new Vector2(1, 1), new Vector2(0, 0),
                passName: "CRT Save Prev");
        }
    }

    public void Cleanup()
    {
        m_PrevRT?.Release();
        m_PrevRT = null;
    }
}
