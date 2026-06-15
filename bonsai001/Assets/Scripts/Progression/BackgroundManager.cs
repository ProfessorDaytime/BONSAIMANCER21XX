using UnityEngine;

/// <summary>
/// Applies a Background cosmetic — the environment around the tree. A background is an
/// <see cref="ItemDefinition"/> (category Background) that swaps the camera's backdrop:
/// a skybox material if one is set, otherwise a solid colour from its swatch, plus an
/// ambient tint and optional scenery prefab. Driven by <see cref="CustomizeManager"/>
/// (buy/equip + restore on load). Captures the scene's original look on first use so the
/// "default" background restores it exactly.
///
/// Slice: cosmetic unlockables — Backgrounds. See Docs/PROGRESSION_DESIGN.md.
/// </summary>
public class BackgroundManager : MonoBehaviour
{
    public static BackgroundManager Instance { get; private set; }

    Camera           cam;
    Material         defaultSkybox;
    Color            defaultBg;
    Color            defaultAmbient;
    CameraClearFlags defaultClear;
    bool             captured;
    GameObject       currentScenery;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void EnsureCaptured()
    {
        if (captured) return;
        cam = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
        if (cam == null) return;
        defaultBg      = cam.backgroundColor;
        defaultClear   = cam.clearFlags;
        defaultSkybox  = RenderSettings.skybox;
        defaultAmbient = RenderSettings.ambientLight;
        captured = true;
    }

    /// <summary>Applies a Background definition. Null restores the captured default.</summary>
    public void Apply(ItemDefinition def)
    {
        EnsureCaptured();
        if (cam == null) return;

        // Optional scenery prefab — destroy the previous one first.
        if (currentScenery != null) { Destroy(currentScenery); currentScenery = null; }
        if (def != null && def.prefab != null) currentScenery = Instantiate(def.prefab);

        if (def == null)
        {
            cam.clearFlags      = defaultClear;
            cam.backgroundColor = defaultBg;
            RenderSettings.skybox      = defaultSkybox;
            RenderSettings.ambientLight = defaultAmbient;
            return;
        }

        if (def.skyboxMaterial != null)
        {
            cam.clearFlags        = CameraClearFlags.Skybox;
            RenderSettings.skybox = def.skyboxMaterial;
        }
        else
        {
            cam.clearFlags        = CameraClearFlags.SolidColor;
            cam.backgroundColor   = def.swatchColor;
            RenderSettings.skybox = defaultSkybox;
        }
        RenderSettings.ambientLight = def.ambientColor;
    }
}
