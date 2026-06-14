using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Quick-Start — generate a developed bonsai at a chosen age by fast-forwarding growth while the
/// AutoStyler shapes it, then hand control back to the player (optionally saving it as a new slot).
/// Reuses the AutoRunManager path (reset → InitTree via the Water state → Hands-Off auto-care at
/// Fast speed) but is player-triggered, stops at a target age, and returns to normal play instead
/// of looping. Multi-tree then just treats each generated tree as another save slot.
/// </summary>
public class QuickStartManager : MonoBehaviour
{
    public static QuickStartManager Instance;

    [Header("References (auto-found if blank)")]
    [SerializeField] TreeSkeleton skeleton;
    [SerializeField] CameraOrbit  cameraOrbit;

    [Header("Selectable styles (offered by the picker UI)")]
    [SerializeField] List<StyleDefinition> styles = new List<StyleDefinition>();

    [Header("Test defaults (ContextMenu / Inspector)")]
    [SerializeField] TreeSpecies     testSpecies;
    [SerializeField] StyleDefinition testStyle;
    [Tooltip("Target age in in-game years to grow to before handing control back.")]
    [SerializeField] int  testAgeYears   = 10;
    [Tooltip("Save the finished tree as a new slot when generation completes.")]
    [SerializeField] bool saveOnComplete = true;

    public IReadOnlyList<StyleDefinition> Styles => styles;
    public bool IsGenerating => generating;
    /// <summary>0→1 progress of the current generation (by years grown).</summary>
    public float Progress => generating && totalYears > 0
        ? Mathf.Clamp01((GameManager.year - startYear) / (float)totalYears) : 0f;

    bool generating;
    int  startYear, totalYears, targetEndYear;
    System.Action onComplete;

    void Awake() => Instance = this;

    void Start()
    {
        if (skeleton    == null) skeleton    = FindAnyObjectByType<TreeSkeleton>();
        if (cameraOrbit == null) cameraOrbit = FindAnyObjectByType<CameraOrbit>();
    }

    [ContextMenu("Quick-Start (test defaults)")]
    public void GenerateTest() => Generate(testSpecies, testStyle, testAgeYears, null);

    /// <summary>Grow a fresh tree of the given species + style to <paramref name="ageYears"/>, then
    /// hand control back (and optionally save a slot). Null species/style keeps the current one.</summary>
    public void Generate(TreeSpecies species, StyleDefinition style, int ageYears, System.Action onDone = null)
    {
        if (generating) { Debug.LogWarning("[QuickStart] Already generating."); return; }
        onComplete = onDone;
        StartCoroutine(GenerateCoroutine(species, style, Mathf.Max(1, ageYears)));
    }

    IEnumerator GenerateCoroutine(TreeSpecies species, StyleDefinition style, int ageYears)
    {
        var gm = GameManager.Instance;
        if (gm == null)       { Debug.LogError("[QuickStart] No GameManager.");  generating = false; yield break; }
        if (skeleton == null) { Debug.LogError("[QuickStart] No TreeSkeleton."); generating = false; yield break; }

        generating = true;

        // Species + style.
        if (species != null) { skeleton.species = species; skeleton.ApplySpecies(); }
        if (style != null && AutoStyler.Instance != null)
        {
            AutoStyler.Instance.style            = style;
            AutoStyler.Instance.autoStyleEnabled = true;
        }

        // Fresh tree — clear any existing, then re-init via the Water state (the path AutoRun uses).
        if (skeleton.root != null) skeleton.ClearForRestart();
        GameManager.waterings = -1;
        if (gm.state == GameState.Water) gm.UpdateGameState(GameState.Idle);
        gm.UpdateGameState(GameState.Water);
        yield return null;   // let InitTree complete

        // Fast hands-off growth + cinematic camera.
        Time.timeScale = 1f;
        gm.UpdateGameState(gm.StateForMonth(GameManager.month));
        cameraOrbit?.SetCinematicMode(true);

        var pm = PlayModeManager.Instance;
        if (pm != null)
        {
            int idx = pm.modes.FindIndex(m => m.name == "Hands-Off");
            if (idx >= 0) pm.SetActiveMode(idx);
        }
        gm.SetSpeedMode(GameManager.SpeedMode.Fast);

        startYear     = GameManager.year;
        totalYears    = ageYears;
        targetEndYear = startYear + ageYears;
        Debug.Log($"[QuickStart] Growing {skeleton.SpeciesName} to age {ageYears}yr " +
                  $"(year {startYear} → {targetEndYear}), style={(style != null ? style.name : "current")}.");
    }

    void Update()
    {
        if (!generating || GameManager.Instance == null) return;
        bool aged = GameManager.year >= targetEndYear;
        bool died = GameManager.Instance.state == GameState.TreeDead;
        if (aged || died) Finish(died);
    }

    void Finish(bool died)
    {
        generating = false;

        var gm = GameManager.Instance;
        cameraOrbit?.SetCinematicMode(false);
        gm?.SetSpeedMode(GameManager.SpeedMode.Med);

        if (!died && saveOnComplete && skeleton != null && skeleton.root != null)
        {
            SaveManager.ActiveSlotId = null;   // force a fresh slot for the generated tree
            SaveManager.AutoSave(skeleton, skeleton.GetComponent<LeafManager>());
        }

        Debug.Log(died
            ? "[QuickStart] Tree died during fast-grow — aborted."
            : $"[QuickStart] Done — developed {skeleton?.SpeciesName} ready at year {GameManager.year}.");

        var cb = onComplete; onComplete = null;
        cb?.Invoke();
    }
}
