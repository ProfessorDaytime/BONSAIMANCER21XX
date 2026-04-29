using System;
using UnityEngine;

/// <summary>
/// Encodes a target bonsai style for the AutoStyler to work toward each season.
/// Create via: right-click in Project → Create → Bonsai → Style Definition
/// </summary>
[CreateAssetMenu(fileName = "NewStyle", menuName = "Bonsai/Style Definition")]
public class StyleDefinition : ScriptableObject
{
    public enum StyleType { Moyogi, SCurve }

    [Header("Identity")]
    public string styleName = "New Style";
    public StyleType styleType = StyleType.Moyogi;

    // ── Trunk Curve ───────────────────────────────────────────────────────────
    // Waypoints define the target trunk silhouette from base to apex.
    // Heights are in local tree units above the soil surface.
    // The AutoStyler wires trunk nodes toward their nearest waypoint's target direction.

    [Header("Trunk Waypoints")]
    [Tooltip("Degrees from vertical a trunk node must deviate before the AutoStyler wires it.")]
    [Range(5f, 45f)] public float wireThresholdDeg = 12f;

    public TrunkWaypoint[] trunkWaypoints = new TrunkWaypoint[]
    {
        new TrunkWaypoint { heightAboveSoil = 0.10f, targetLeanAngleDeg =  0f, leanAxisDeg = 0f },
        new TrunkWaypoint { heightAboveSoil = 0.30f, targetLeanAngleDeg = 15f, leanAxisDeg = 0f },
        new TrunkWaypoint { heightAboveSoil = 0.60f, targetLeanAngleDeg =  8f, leanAxisDeg = 0f },
        new TrunkWaypoint { heightAboveSoil = 1.00f, targetLeanAngleDeg =  4f, leanAxisDeg = 0f },
    };

    // ── Branch Tiers ──────────────────────────────────────────────────────────
    // Each tier defines a vertical band and how many branches are allowed there.
    // Heights are normalized (0 = soil, 1 = apex).

    [Header("Branch Tiers")]
    [Tooltip("Normalized height (0–1) bands. AutoStyler trims excess branches per tier each February.")]
    public BranchTier[] branchTiers = new BranchTier[]
    {
        new BranchTier { minHeightNorm = 0.00f, maxHeightNorm = 0.25f, maxBranches = 3, targetAngleDeg = 80f, maxAngleTolerance = 40f },
        new BranchTier { minHeightNorm = 0.25f, maxHeightNorm = 0.55f, maxBranches = 4, targetAngleDeg = 65f, maxAngleTolerance = 35f },
        new BranchTier { minHeightNorm = 0.55f, maxHeightNorm = 0.80f, maxBranches = 5, targetAngleDeg = 50f, maxAngleTolerance = 35f },
        new BranchTier { minHeightNorm = 0.80f, maxHeightNorm = 1.00f, maxBranches = 6, targetAngleDeg = 35f, maxAngleTolerance = 30f },
    };

    // ── Canopy Silhouette ─────────────────────────────────────────────────────
    // X axis = normalized height (0 = soil, 1 = apex).
    // Y axis = normalized radius (0 = no canopy, 1 = max canopy radius).

    [Header("Canopy Silhouette")]
    [Tooltip("Maximum canopy spread as a fraction of tree height (0.6 = 60% of height). Scales with the tree.")]
    public float maxCanopyRadius = 0.6f;

    [Tooltip("Curve defining the canopy silhouette. X = normalized height, Y = fraction of maxCanopyRadius. " +
             "A triangle peaking at 0.5 gives a classic oval; peaking at 0.3 gives a more upright form.")]
    public AnimationCurve canopySilhouette = AnimationCurve.EaseInOut(0f, 0f, 1f, 0f);

    // ── Pinch Settings ────────────────────────────────────────────────────────

    [Header("Pinching")]
    [Tooltip("Terminals extending this far BEYOND the silhouette radius are pinched in April–May.")]
    [Range(1.0f, 2.0f)] public float pinchOvershootFactor = 1.15f;

    // ── Ramification ─────────────────────────────────────────────────────────

    [Header("Ramification")]
    [Tooltip("When true, AutoStyler proactively pinches interior terminals each June to build branch density.")]
    public bool enableRamification = true;

    [Tooltip("Target pinch cycles for lower-canopy branches (higher = denser pads). Tapers toward apex.")]
    [Range(1f, 6f)] public float ramificationTargetLevel = 3f;

    [Tooltip("Normalized height above which ramification is skipped — apex stays lighter and airier.")]
    [Range(0.3f, 1f)] public float ramificationMaxHeight = 0.75f;
}

// ── Data Types ────────────────────────────────────────────────────────────────

[Serializable]
public class TrunkWaypoint
{
    [Tooltip("Height above soil (local units) where this waypoint applies.")]
    public float heightAboveSoil;

    [Tooltip("Target lean angle from vertical (0° = straight up, 30° = gentle lean).")]
    [Range(0f, 60f)] public float targetLeanAngleDeg;

    [Tooltip("Compass bearing of the lean (degrees, 0 = +Z, 90 = +X). " +
             "For S-curve, consecutive waypoints alternate by 180°.")]
    [Range(0f, 360f)] public float leanAxisDeg;
}

[Serializable]
public class BranchTier
{
    [Tooltip("Normalized height of the bottom of this tier (0 = soil, 1 = apex).")]
    [Range(0f, 1f)] public float minHeightNorm;

    [Tooltip("Normalized height of the top of this tier.")]
    [Range(0f, 1f)] public float maxHeightNorm;

    [Tooltip("Maximum number of primary branches allowed in this tier. Excess are trimmed in February.")]
    public int maxBranches;

    [Tooltip("Target angle from vertical for branches in this tier (90° = horizontal).")]
    [Range(0f, 90f)] public float targetAngleDeg;

    [Tooltip("Branches deviating more than this from targetAngleDeg are flagged for removal.")]
    [Range(10f, 90f)] public float maxAngleTolerance;
}
