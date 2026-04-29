using UnityEngine;
using UnityEditor;

/// <summary>
/// Creates the two built-in style definition assets under Assets/Scripts/Tree/Species/.
/// Menu: Bonsai → Create Default Styles
/// </summary>
public static class StyleDefinitionCreator
{
    const string OutputFolder = "Assets/Scripts/Tree/Species";

    [MenuItem("Bonsai/Create Default Styles")]
    static void CreateDefaultStyles()
    {
        CreateMoyogi();
        CreateSCurve();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[AutoStyle] Created Moyogi.asset and SCurve.asset in " + OutputFolder);
    }

    static void CreateMoyogi()
    {
        var s = ScriptableObject.CreateInstance<StyleDefinition>();
        s.styleName  = "Moyogi";
        s.styleType  = StyleDefinition.StyleType.Moyogi;
        s.wireThresholdDeg = 12f;
        s.maxCanopyRadius  = 0.6f;   // fraction of tree height
        s.pinchOvershootFactor  = 1.2f;
        s.enableRamification    = true;
        s.ramificationTargetLevel = 3f;
        s.ramificationMaxHeight = 0.75f;

        // Trunk: gentle informal upright — base leans slightly, apex straightens
        s.trunkWaypoints = new TrunkWaypoint[]
        {
            new TrunkWaypoint { heightAboveSoil = 0.05f, targetLeanAngleDeg =  0f, leanAxisDeg =   0f },
            new TrunkWaypoint { heightAboveSoil = 0.20f, targetLeanAngleDeg = 18f, leanAxisDeg =   0f },
            new TrunkWaypoint { heightAboveSoil = 0.45f, targetLeanAngleDeg = 12f, leanAxisDeg =  45f },
            new TrunkWaypoint { heightAboveSoil = 0.70f, targetLeanAngleDeg =  8f, leanAxisDeg = 315f },
            new TrunkWaypoint { heightAboveSoil = 0.95f, targetLeanAngleDeg =  4f, leanAxisDeg =  10f },
        };

        // Tiers: bottom branches wide, upper branches progressively more upward
        s.branchTiers = new BranchTier[]
        {
            new BranchTier { minHeightNorm = 0.05f, maxHeightNorm = 0.25f, maxBranches = 2, targetAngleDeg = 80f, maxAngleTolerance = 35f },
            new BranchTier { minHeightNorm = 0.25f, maxHeightNorm = 0.50f, maxBranches = 3, targetAngleDeg = 65f, maxAngleTolerance = 35f },
            new BranchTier { minHeightNorm = 0.50f, maxHeightNorm = 0.75f, maxBranches = 4, targetAngleDeg = 50f, maxAngleTolerance = 30f },
            new BranchTier { minHeightNorm = 0.75f, maxHeightNorm = 1.00f, maxBranches = 5, targetAngleDeg = 35f, maxAngleTolerance = 30f },
        };

        // Oval canopy, widest at 40% height
        s.canopySilhouette = new AnimationCurve(
            new Keyframe(0.00f, 0.00f),
            new Keyframe(0.15f, 0.55f),
            new Keyframe(0.40f, 1.00f),
            new Keyframe(0.75f, 0.70f),
            new Keyframe(1.00f, 0.15f)
        );

        AssetDatabase.CreateAsset(s, OutputFolder + "/Moyogi.asset");
    }

    static void CreateSCurve()
    {
        var s = ScriptableObject.CreateInstance<StyleDefinition>();
        s.styleName  = "S-Curve";
        s.styleType  = StyleDefinition.StyleType.SCurve;
        s.wireThresholdDeg = 10f;
        s.maxCanopyRadius  = 0.5f;   // fraction of tree height
        s.pinchOvershootFactor  = 1.15f;
        s.enableRamification    = true;
        s.ramificationTargetLevel = 2.5f;
        s.ramificationMaxHeight = 0.70f;

        // Trunk: alternating lean — the commercial S shape
        s.trunkWaypoints = new TrunkWaypoint[]
        {
            new TrunkWaypoint { heightAboveSoil = 0.05f, targetLeanAngleDeg =  0f, leanAxisDeg =   0f },
            new TrunkWaypoint { heightAboveSoil = 0.20f, targetLeanAngleDeg = 22f, leanAxisDeg =   0f },
            new TrunkWaypoint { heightAboveSoil = 0.40f, targetLeanAngleDeg = 22f, leanAxisDeg = 180f },
            new TrunkWaypoint { heightAboveSoil = 0.60f, targetLeanAngleDeg = 18f, leanAxisDeg =   0f },
            new TrunkWaypoint { heightAboveSoil = 0.80f, targetLeanAngleDeg = 14f, leanAxisDeg = 180f },
            new TrunkWaypoint { heightAboveSoil = 0.95f, targetLeanAngleDeg =  6f, leanAxisDeg =   0f },
        };

        // Tiers: tighter control, fewer branches per tier for a cleaner commercial look
        s.branchTiers = new BranchTier[]
        {
            new BranchTier { minHeightNorm = 0.05f, maxHeightNorm = 0.30f, maxBranches = 2, targetAngleDeg = 75f, maxAngleTolerance = 30f },
            new BranchTier { minHeightNorm = 0.30f, maxHeightNorm = 0.55f, maxBranches = 3, targetAngleDeg = 60f, maxAngleTolerance = 28f },
            new BranchTier { minHeightNorm = 0.55f, maxHeightNorm = 0.78f, maxBranches = 3, targetAngleDeg = 45f, maxAngleTolerance = 25f },
            new BranchTier { minHeightNorm = 0.78f, maxHeightNorm = 1.00f, maxBranches = 4, targetAngleDeg = 30f, maxAngleTolerance = 25f },
        };

        // Rounded conical canopy, widest at 35% height
        s.canopySilhouette = new AnimationCurve(
            new Keyframe(0.00f, 0.00f),
            new Keyframe(0.10f, 0.45f),
            new Keyframe(0.35f, 1.00f),
            new Keyframe(0.65f, 0.75f),
            new Keyframe(1.00f, 0.10f)
        );

        AssetDatabase.CreateAsset(s, OutputFolder + "/SCurve.asset");
    }
}
