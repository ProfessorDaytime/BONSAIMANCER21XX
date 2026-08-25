using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Debug harness that drives a tree toward TIPPING so the stability system can be observed and
/// tuned. Attach alongside TreeSkeleton.
///
/// Why a harness is needed at all: a healthy, well-balanced tree should essentially NEVER tip, and
/// that is correct behaviour. The overturning moment is driven by how far the canopy's centre of
/// mass overhangs the pot's base edge — a symmetrical Moyogi has almost no overhang, so its lever
/// arm is ~0 no matter how large it grows. On top of that, the pot-volume growth limit now caps
/// the canopy a small pot can carry, which is a second stabiliser. Tipping is therefore a
/// STYLING-and-neglect risk, not something that happens on its own.
///
/// So the harness forces the four conditions that actually cause it:
///   1. A one-sided canopy — trims everything on one half, the way a windswept/cascade is made.
///      This is the dominant term; without it nothing else matters.
///   2. Dry soil — held just above the drought threshold, so ballast is low but the tree lives.
///   3. No repot rescue — the auto-styler's soil refresh and pot up-size would relieve the problem.
///   4. Optionally lifts the pot-volume growth cap, so the canopy CAN outgrow its pot.
/// </summary>
[RequireComponent(typeof(TreeSkeleton))]
public class TippingTestMode : MonoBehaviour
{
    [Header("Tipping Test Harness")]
    [Tooltip("Master switch. OFF by default — tipping was verified working 2026-08-24 (a tree went " +
             "over at ratio 0.79 after the one-sided trim pushed reach past the pot's footprint). " +
             "Tick it on to exercise tipping again; it deliberately mistreats the tree, so it must " +
             "never be left on for normal play.")]
    public bool active = false;

    [Tooltip("Soil moisture to hold. Must stay ABOVE the species drought threshold (~0.30) or the " +
             "tree dies of thirst before it ever gets top-heavy enough to tip.")]
    [Range(0f, 1f)] public float holdMoisture = 0.38f;

    [Tooltip("Compass direction (degrees) the canopy is kept on. Everything outside the arc below " +
             "gets trimmed, throwing the centre of mass to one side.")]
    [Range(0f, 360f)] public float keepAzimuthDeg = 90f;

    [Tooltip("Half-width of the kept arc, degrees. 90 keeps a hemisphere; smaller = more extreme.")]
    [Range(20f, 170f)] public float keepHalfArcDeg = 75f;

    [Tooltip("Maximum branches trimmed per season. Trimming wounds the tree, so this is capped to " +
             "shape it over time rather than mutilating it in one pass. Raised from 8 — on an " +
             "800-branch canopy that was far too slow to build real one-sidedness.")]
    public int maxTrimsPerSeason = 25;

    [Tooltip("Only trim branches at least this far out along the canopy — trimming near the trunk " +
             "removes the scaffolding the canopy hangs from and just shrinks the tree.")]
    public float minTrimDistance = 0.25f;

    [Tooltip("Suppress the auto-styler's soil refresh / pot up-size, which would otherwise rescue " +
             "the tree by giving it a bigger, heavier, more stable pot.")]
    public bool suppressAutoRepot = true;

    [Tooltip("Lift the pot-volume growth cap so the canopy can outgrow what its pot should carry.")]
    public bool bypassPotGrowthLimit = true;

    [Tooltip("Hold WindManager at storm strength. This is the term that actually tips trees — " +
             "measured 2026-08-24, a tree's own weight CANNOT overturn its pot (2 kg of soil vs a " +
             "168 g tree), so without wind nothing here will ever go over. 1.0 ≈ 16 m/s at peak gust.")]
    public bool forceStormWind = true;

    [Range(0f, 1f)] public float stormWindStrength = 1f;

    TreeSkeleton skeleton;
    AutoStyler   styler;
    int          lastSeasonStamp = -1;
    bool         appliedGrowthOverride;

    void Awake()
    {
        skeleton = GetComponent<TreeSkeleton>();
        styler   = GetComponent<AutoStyler>();
    }

    void Update()
    {
        if (!active || skeleton == null) return;

        // Hold the soil dry. Done every frame because the auto-water pass can fire at any time and
        // would otherwise refill the pot — wet soil is ~1.5× the ballast of dry.
        if (skeleton.soilMoisture > holdMoisture) skeleton.soilMoisture = holdMoisture;

        if (suppressAutoRepot && styler != null)
        {
            // TWO separate flags: autoSoilRefresh is the periodic soil/repot cycle, autoRepot gates
            // AdvancePotPhase — the age-scheduled pot SIZE up-grade. Suppressing only the first
            // left the styler still promoting XS→S→M, and every up-size hands the tree a heavier,
            // wider, far more stable pot, so it outran the test every time (2026-08-24).
            styler.autoSoilRefresh = false;
            styler.autoRepot       = false;
        }

        if (forceStormWind && WindManager.Instance != null)
            WindManager.Instance.strength = stormWindStrength;

        if (bypassPotGrowthLimit && !appliedGrowthOverride)
        {
            skeleton.PotVolumeLimitsGrowth = false;
            appliedGrowthOverride = true;
        }

        // One-sided trim, once per season.
        int stamp = GameManager.year * 4 + Mathf.Clamp(GameManager.month / 3, 0, 3);
        if (stamp != lastSeasonStamp)
        {
            lastSeasonStamp = stamp;
            TrimOffsideBranches();
        }
    }

    /// <summary>Trims branches whose tips sit outside the kept arc, building a lopsided canopy the
    /// same way a windswept style is made. Works outward-in and is budgeted per season so the tree
    /// is shaped rather than gutted.</summary>
    void TrimOffsideBranches()
    {
        if (skeleton.root == null) return;

        Vector3 keepDir = new Vector3(
            Mathf.Cos(keepAzimuthDeg * Mathf.Deg2Rad), 0f,
            Mathf.Sin(keepAzimuthDeg * Mathf.Deg2Rad));

        var doomed = new List<TreeNode>();
        foreach (var n in skeleton.allNodes)
        {
            if (n.isRoot || n.isTrimmed || n.isDead || n.depth < 1) continue;

            Vector3 offset = n.tipPosition - skeleton.root.worldPosition;
            Vector3 flat   = new Vector3(offset.x, 0f, offset.z);
            if (flat.magnitude < minTrimDistance) continue;

            float angle = Vector3.Angle(flat.normalized, keepDir);
            if (angle > keepHalfArcDeg) doomed.Add(n);
        }

        // SHALLOWEST first — removing a wrong-side scaffold takes that entire side with it, which
        // is how a windswept is really made. Sorting farthest-first instead only nibbled outer
        // twigs off an 800-branch canopy, and the centre of mass barely moved (measured
        // 2026-08-24: reach stuck at 0.11–0.18 against a 0.279 footprint, so lever stayed 0).
        // Ties break on distance so the most off-balance wood goes first.
        doomed.Sort((a, b) =>
        {
            int d = a.depth.CompareTo(b.depth);
            if (d != 0) return d;
            return (b.tipPosition - skeleton.root.worldPosition).sqrMagnitude
                .CompareTo((a.tipPosition - skeleton.root.worldPosition).sqrMagnitude);
        });

        int trimmed = 0;
        foreach (var n in doomed)
        {
            if (trimmed >= maxTrimsPerSeason) break;
            if (n.isTrimmed) continue;          // may have been removed as part of an earlier subtree
            skeleton.TrimNode(n, stimulateRegrowth: false);
            trimmed++;
        }

        if (trimmed > 0)
            Debug.Log($"[TipTest] Trimmed {trimmed} off-side branches (keep {keepAzimuthDeg:F0}°±{keepHalfArcDeg:F0}°) " +
                      $"| moisture held at {holdMoisture:F2} | year={GameManager.year}");
    }
}
