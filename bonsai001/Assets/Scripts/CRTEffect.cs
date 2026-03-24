using UnityEngine;

/// <summary>
/// Full-screen CRT post-process effect.
/// Attach to the main camera. Assign the CRT shader in the Inspector.
/// </summary>
[ExecuteInEditMode]
[RequireComponent(typeof(Camera))]
public class CRTEffect : MonoBehaviour
{
    [SerializeField] Shader crtShader;

    [Header("Scanlines")]
    [Range(0f, 1f)]   public float scanlineStrength  = 0.25f;
    [Range(100, 600)] public float scanlineFrequency = 240f;

    [Header("Screen Shape")]
    [Range(0f, 0.3f)] public float curvature = 0.08f;
    [Range(0f, 2f)]   public float vignette  = 0.8f;

    [Header("Colour")]
    [Range(0f, 0.005f)] public float rgbSplit   = 0.001f;
    [Range(0.5f, 2f)]   public float brightness = 1.05f;

    Material mat;

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
        if (mat != null)
        {
            DestroyImmediate(mat);
            mat = null;
        }
    }

    void OnRenderImage(RenderTexture src, RenderTexture dst)
    {
        if (mat == null) { Graphics.Blit(src, dst); return; }

        mat.SetFloat("_ScanlineStr",  scanlineStrength);
        mat.SetFloat("_ScanlineFreq", scanlineFrequency);
        mat.SetFloat("_Curvature",    curvature);
        mat.SetFloat("_Vignette",     vignette);
        mat.SetFloat("_RGBSplit",     rgbSplit);
        mat.SetFloat("_Brightness",   brightness);

        Graphics.Blit(src, dst, mat);
    }
}
