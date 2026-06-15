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
    [Tooltip("Target wall-clock SECONDS for the whole fast-grow. The timescale is derived from this " +
             "and the chosen age — the catch-up calendar keeps days/sec ≈ TIMESCALE/24 regardless of " +
             "framerate, so a 10-yr and a 40-yr grow both aim for this. Clamped by Max Timescale.")]
    [SerializeField] float quickStartSeconds = 30f;
    [Tooltip("Upper bound on the derived timescale, so very old targets don't spike to an extreme " +
             "speed; they just take a little longer than the target instead.")]
    [SerializeField] float maxQuickStartTimescale = 6000f;

    public IReadOnlyList<StyleDefinition> Styles => styles;
    public bool IsGenerating => generating;
    /// <summary>0→1 progress of the current generation (by years grown).</summary>
    public float Progress => generating && totalYears > 0
        ? Mathf.Clamp01((GameManager.year - startYear) / (float)totalYears) : 0f;

    bool  generating;
    int   startYear, totalYears, targetEndYear;
    float savedFastTimescale = -1f;   // >0 while we've overridden TIMESCALE_FAST for the grow
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

        generating    = true;
        startYear     = GameManager.year;
        totalYears    = ageYears;
        targetEndYear = startYear + ageYears;   // set BEFORE any yield so Update() can't finish in the setup frame

        // Species + style. Quick-Start always grows from seed, so force a Seedling origin (a stale
        // Ishitsuki/loaded origin from a prior tree would make InitTree set up the wrong way).
        if (species != null) { skeleton.species = species; skeleton.ApplySpecies(); }
        skeleton.treeOrigin = TreeOrigin.Seedling;
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
        // Derive a timescale to hit ~quickStartSeconds of wall-clock for this age, then lift the Fast
        // speed to it for the duration (restored in Finish). With the catch-up calendar,
        // days/sec ≈ TIMESCALE/24, so wall-clock ≈ ageYears*365*24 / TIMESCALE.
        float ts = ageYears * 365f * 24f / Mathf.Max(5f, quickStartSeconds);
        ts = Mathf.Clamp(ts, 200f, Mathf.Max(200f, maxQuickStartTimescale));
        savedFastTimescale = GameManager.TIMESCALE_FAST;
        GameManager.TIMESCALE_FAST = Mathf.Max(GameManager.TIMESCALE_FAST, ts);
        gm.SetSpeedMode(GameManager.SpeedMode.Fast);

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
        if (savedFastTimescale > 0f) { GameManager.TIMESCALE_FAST = savedFastTimescale; savedFastTimescale = -1f; }
        gm?.SetSpeedMode(GameManager.SpeedMode.Med);

        if (!died && skeleton != null && skeleton.root != null)
        {
            RestoreHealthyBaseline();   // hand over a vigorous, fresh-potted specimen, not a half-dead one
            if (saveOnComplete)
            {
                SaveManager.ActiveSlotId = null;   // force a fresh slot for the generated tree
                SaveManager.AutoSave(skeleton, skeleton.GetComponent<LeafManager>());
            }
        }

        Debug.Log(died
            ? "[QuickStart] Tree died during fast-grow — aborted."
            : $"[QuickStart] Done — developed {skeleton?.SpeciesName} ready at year {GameManager.year}.");

        var cb = onComplete; onComplete = null;
        cb?.Invoke();
    }

    /// <summary>
    /// A Quick-Start tree is a *generated* developed specimen, so it should arrive healthy — not
    /// beaten up by ten simulated years of soil degradation and pot-bound pressure (which is what
    /// crashed health to ~0.07 at the end). Repot into an age-appropriate pot with fresh soil
    /// (preserves the developed nebari — Repot doesn't regenerate roots), then restore full vigor.
    /// </summary>
    void RestoreHealthyBaseline()
    {
        var potSoil = skeleton.GetComponent<PotSoil>();
        if (potSoil != null)
        {
            // Mirrors the AutoStyler's XS→S→M→L phase thresholds (6 / 13 / 26 yr) in case its
            // spring pot-advance didn't fire during the fast-grow, and clears the soil degradation
            // that built up over the decade.
            var size = totalYears >= 26 ? PotSoil.PotSize.L
                     : totalYears >= 13 ? PotSoil.PotSize.M
                     : totalYears >= 6  ? PotSoil.PotSize.S
                     :                    PotSoil.PotSize.XS;
            ProgressionManager.AutomationActive = true;   // generated tree's repot isn't the player's "first repot"
            try { potSoil.Repot(skeleton, potSoil.preset, size, size != potSoil.potSize); }
            finally { ProgressionManager.AutomationActive = false; }
        }

        // Full health (also overwrites the repot stress above), no danger, topped-up water + nutrients.
        foreach (var n in skeleton.allNodes) n.health = 1f;
        skeleton.nutrientReserve = Mathf.Max(skeleton.nutrientReserve, 1f);
        skeleton.treeInDanger = false;
        skeleton.consecutiveCriticalSeasons = 0;
        skeleton.Water();
    }
}
