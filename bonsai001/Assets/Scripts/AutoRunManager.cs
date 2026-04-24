using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Autonomous run loop for unattended screen recording.
///
/// Lifecycle per run:
///   BeginRun → resets tree → triggers InitTree via Water state → enables cinematic
///   camera → switches to Hands-Off play mode → grows for runDurationYears in-game
///   years → holds a beauty shot → repeats.
///
/// Setup: add this component anywhere in the scene, assign the optional references
/// (auto-found if blank), tick Auto Run Enabled to start on Play.
/// Use the [ContextMenu] "Begin Run" button to kick off manually in-editor.
/// </summary>
public class AutoRunManager : MonoBehaviour
{
    public static AutoRunManager Instance;

    [Header("Run Config")]
    [Tooltip("Start the loop automatically when the scene plays.")]
    [SerializeField] bool autoRunEnabled = false;

    [Tooltip("How many in-game years each run lasts before the beauty shot triggers.")]
    [SerializeField] int runDurationYears = 10;

    [Tooltip("Real seconds to hold on the final frame before restarting.")]
    [SerializeField] float beautyShotSeconds = 5f;

    [Tooltip("Total number of loops to complete. 0 = run forever.")]
    [SerializeField] int loopCount = 0;

    [Tooltip("Species to cycle through, one per run. Leave empty to keep whatever is currently loaded.")]
    [SerializeField] List<TreeSpecies> speciesRotation = new List<TreeSpecies>();

    [Header("References (auto-found if blank)")]
    [SerializeField] TreeSkeleton skeleton;
    [SerializeField] CameraOrbit   cameraOrbit;

    // ── State ─────────────────────────────────────────────────────────────────

    int  startYear;
    int  completedLoops;
    int  speciesIndex;
    bool running;
    bool ending;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Awake() => Instance = this;

    void Start()
    {
        if (skeleton    == null) skeleton    = FindFirstObjectByType<TreeSkeleton>();
        if (cameraOrbit == null) cameraOrbit = FindFirstObjectByType<CameraOrbit>();

        if (autoRunEnabled)
            BeginRun();
    }

    void Update()
    {
        if (!running || ending) return;
        if (GameManager.year >= startYear + runDurationYears)
            StartCoroutine(EndRun());
    }

    // ── Public API ────────────────────────────────────────────────────────────

    [ContextMenu("Begin Run")]
    public void BeginRun() => StartCoroutine(BeginRunCoroutine());

    [ContextMenu("End Run Now")]
    public void ForceEndRun()
    {
        if (!ending) StartCoroutine(EndRun());
    }

    // ── Run coroutines ────────────────────────────────────────────────────────

    IEnumerator BeginRunCoroutine()
    {
        ending  = false;
        running = false;

        var gm = GameManager.Instance;
        if (gm == null) { Debug.LogError("[AutoRun] No GameManager found."); yield break; }
        if (skeleton == null) { Debug.LogError("[AutoRun] No TreeSkeleton found."); yield break; }

        // Switch species if a rotation list is configured
        if (speciesRotation != null && speciesRotation.Count > 0)
        {
            var sp = speciesRotation[speciesIndex % speciesRotation.Count];
            if (sp != null)
            {
                skeleton.species = sp;
                skeleton.ApplySpecies();
            }
        }

        // Reset tree to bare seed
        skeleton.ClearForRestart();
        GameManager.waterings = -1;

        // Trigger InitTree via the Water state (same path as the player pressing Water)
        if (gm.state == GameState.Water)
            gm.UpdateGameState(GameState.Idle);   // ensure guard doesn't skip re-fire
        gm.UpdateGameState(GameState.Water);

        yield return null;  // let InitTree complete before advancing

        // Unfreeze time — game may have been paused before AutoRun started.
        Time.timeScale = 1f;

        // Restore normal time-advancing state for the current month
        gm.UpdateGameState(gm.StateForMonth(GameManager.month));

        // Activate cinematic camera
        if (cameraOrbit != null)
            cameraOrbit.SetCinematicMode(true);

        // Switch to Hands-Off play mode (auto-water, auto-fertilize, fast speed)
        var pm = PlayModeManager.Instance;
        if (pm != null)
        {
            int idx = pm.modes.FindIndex(m => m.name == "Hands-Off");
            if (idx >= 0)
                pm.SetActiveMode(idx);
            else
                Debug.LogWarning("[AutoRun] 'Hands-Off' play mode not found — speed/care not configured.");
        }

        startYear = GameManager.year;
        running   = true;

        Debug.Log($"[AutoRun] Run {completedLoops + 1} started — " +
                  $"species={skeleton.SpeciesName} duration={runDurationYears}yr " +
                  $"startYear={startYear}");
    }

    IEnumerator EndRun()
    {
        ending  = true;
        running = false;

        Debug.Log($"[AutoRun] Run {completedLoops + 1} complete — " +
                  $"holding beauty shot for {beautyShotSeconds:F1}s");

        yield return new WaitForSecondsRealtime(beautyShotSeconds);

        completedLoops++;

        if (loopCount > 0 && completedLoops >= loopCount)
        {
            Debug.Log($"[AutoRun] All {loopCount} loop(s) complete. Stopping.");
            yield break;
        }

        speciesIndex++;
        BeginRun();
    }
}
