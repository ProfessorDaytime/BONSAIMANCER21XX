using UnityEngine;

/// <summary>
/// Full-screen CRT post-process effect — enhanced edition.
/// Attach to the main camera. Assign the CRT shader in the Inspector.
///
/// New features beyond the original scanlines / curvature / vignette / RGB split:
///   • Phosphor mask (shadow mask or aperture grille)
///   • Phosphor glow / bloom
///   • Phosphor persistence (ghosting via previous-frame blending)
///   • Horizontal bandwidth blur (asymmetric signal blur)
///   • Beam intensity modulation (bright pixels widen into scanline gaps)
///   • Interlacing flicker
///   • Analog noise + rolling interference bar + horizontal jitter
///   • Corner convergence error (radial chromatic aberration)
///   • CRT gamma curve (input/output gamma separation)
/// </summary>
[ExecuteInEditMode]
[RequireComponent(typeof(Camera))]
public class CRTEffect : MonoBehaviour
{
    [SerializeField] Shader crtShader;

    // ── Scanlines ──────────────────────────────────────────────
    [Header("Scanlines")]
    [Range(0f, 1f)]    public float scanlineStrength  = 0.25f;
    [Range(100, 600)]  public float scanlineFrequency = 240f;

    // ── Screen Shape ───────────────────────────────────────────
    [Header("Screen Shape")]
    [Range(0f, 0.3f)]  public float curvature = 0.08f;
    [Range(0f, 2f)]    public float vignette  = 0.8f;

    // ── Colour ─────────────────────────────────────────────────
    [Header("Colour")]
    [Range(0f, 0.005f)] public float rgbSplit   = 0.001f;
    [Range(0.5f, 2f)]   public float brightness = 1.05f;

    // ── Phosphor Mask ──────────────────────────────────────────
    [Header("Phosphor Mask")]
    [Tooltip("Strength of the RGB phosphor dot/stripe pattern")]
    [Range(0f, 1f)]    public float maskStrength = 0.3f;
    [Tooltip("Pixel scale of the mask pattern — higher = coarser")]
    [Range(1f, 8f)]    public float maskScale    = 3f;
    [Tooltip("0 = Shadow Mask (dot triad)   1 = Aperture Grille (Trinitron stripes)")]
    [Range(0f, 1f)]    public float maskType     = 0f;

    // ── Phosphor Glow / Bloom ──────────────────────────────────
    [Header("Phosphor Glow")]
    [Range(0f, 1f)]    public float bloomStrength = 0.15f;
    [Range(0f, 6f)]    public float bloomRadius   = 2f;

    // ── Phosphor Persistence ───────────────────────────────────
    [Header("Phosphor Persistence")]
    [Tooltip("How much the previous frame bleeds through (ghosting)")]
    [Range(0f, 0.9f)]  public float persistence = 0.25f;

    // ── Horizontal Bandwidth ───────────────────────────────────
    [Header("Horizontal Bandwidth Blur")]
    [Tooltip("Simulates limited horizontal signal bandwidth — blurs left/right only")]
    [Range(0f, 4f)]    public float horizontalBlur = 1f;

    // ── Beam Intensity ─────────────────────────────────────────
    [Header("Beam Intensity")]
    [Tooltip("Bright pixels widen into the scanline gap, darkening lines only show in dim areas")]
    [Range(0f, 1f)]    public float beamWidth = 0.4f;

    // ── Interlacing ────────────────────────────────────────────
    [Header("Interlacing")]
    [Range(0f, 1f)]    public float interlaceStrength = 0f;

    // ── Noise & Jitter ─────────────────────────────────────────
    [Header("Noise & Jitter")]
    [Range(0f, 0.15f)] public float noiseStrength   = 0.02f;
    [Range(0f, 0.003f)]public float horizontalJitter = 0.0005f;
    [Tooltip("Speed of the rolling interference bar")]
    [Range(0f, 8f)]    public float rollSpeed        = 1f;

    // ── Corner Convergence ─────────────────────────────────────
    [Header("Corner Convergence Error")]
    [Tooltip("Additional RGB misalignment that increases toward the screen corners")]
    [Range(0f, 0.01f)] public float cornerConvergence = 0.002f;

    // ── CRT Gamma ──────────────────────────────────────────────
    [Header("CRT Gamma")]
    [Range(1f, 3f)]    public float inputGamma  = 2.2f;
    [Range(1f, 3f)]    public float outputGamma = 2.5f;

    // ── Internal state ─────────────────────────────────────────
    Material mat;
    RenderTexture prevFrame;

    void OnEnable()
    {
        if (crtShader == null)
            crtShader = Shader.Find("Hidden/CRT");

        if (crtShader != null)
            mat = new Material(crtShader) { hideFlags = HideFlags.HideAndDontSave };
        else
            Debug.LogError("CRTEffect: could not find Hidden/CRT shader.", this);
    }

    void OnDisable()
    {
        if (mat != null)      { DestroyImmediate(mat); mat = null; }
        if (prevFrame != null) { prevFrame.Release(); DestroyImmediate(prevFrame); prevFrame = null; }
    }

    /// <summary>
    /// Ensure the persistence buffer matches screen size.
    /// </summary>
    void EnsurePrevFrame(int w, int h)
    {
        if (prevFrame != null && (prevFrame.width != w || prevFrame.height != h))
        {
            prevFrame.Release();
            DestroyImmediate(prevFrame);
            prevFrame = null;
        }

        if (prevFrame == null)
        {
            prevFrame = new RenderTexture(w, h, 0, RenderTextureFormat.DefaultHDR)
            {
                hideFlags  = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode   = TextureWrapMode.Clamp
            };
            prevFrame.Create();
        }
    }

    void OnRenderImage(RenderTexture src, RenderTexture dst)
    {
        if (mat == null) { Graphics.Blit(src, dst); return; }

        // --- Persistence buffer ---
        EnsurePrevFrame(src.width, src.height);

        // Original parameters
        mat.SetFloat("_ScanlineStr",  scanlineStrength);
        mat.SetFloat("_ScanlineFreq", scanlineFrequency);
        mat.SetFloat("_Curvature",    curvature);
        mat.SetFloat("_Vignette",     vignette);
        mat.SetFloat("_RGBSplit",     rgbSplit);
        mat.SetFloat("_Brightness",   brightness);

        // New parameters
        mat.SetFloat("_MaskStrength", maskStrength);
        mat.SetFloat("_MaskScale",    maskScale);
        mat.SetFloat("_MaskType",     maskType);
        mat.SetFloat("_BloomStr",     bloomStrength);
        mat.SetFloat("_BloomRadius",  bloomRadius);
        mat.SetFloat("_Persistence",  persistence);
        mat.SetFloat("_HBlur",        horizontalBlur);
        mat.SetFloat("_BeamWidth",    beamWidth);
        mat.SetFloat("_InterlaceStr", interlaceStrength);
        mat.SetFloat("_NoiseStr",     noiseStrength);
        mat.SetFloat("_JitterStr",    horizontalJitter);
        mat.SetFloat("_RollSpeed",    rollSpeed);
        mat.SetFloat("_CornerRGB",    cornerConvergence);
        mat.SetFloat("_InputGamma",   inputGamma);
        mat.SetFloat("_OutputGamma",  outputGamma);

        mat.SetTexture("_PrevFrame",  prevFrame);

        // Render the CRT effect
        Graphics.Blit(src, dst, mat);

        // Store this frame for next frame's persistence
        Graphics.Blit(dst, prevFrame);
    }
}
