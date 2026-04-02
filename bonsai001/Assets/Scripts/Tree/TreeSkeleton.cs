using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Random = UnityEngine.Random;

/// <summary>
/// Owns and manages the tree graph. Drives growth, branching, and secondary thickening.
///
/// Growth model:
///   - Each terminal node (isGrowing = true) extends its length toward targetLength each frame.
///   - When it reaches targetLength it spawns children: always one continuation (apical
///     meristem) and probabilistically one lateral branch.
///   - After any structural change, RecalculateRadii() walks bottom-up and applies da Vinci's
///     pipe model: parent.radius^2 = sum(child.radius^2). This thickens the trunk automatically
///     as more branches accumulate.
///   - Growth direction blends inertia + phototropism (sun bias) + random perturbation.
///   - Growth only ticks when isGrowing = true (set by BranchGrow game state).
/// </summary>
public class TreeSkeleton : MonoBehaviour
{
    // Inspector

    [Header("Growth Speed")]
    [Tooltip("How many local units per in-game day a depth-0 segment grows. " +
             "The season length (not maxDepth) controls how much the tree grows each year.")]
    [SerializeField] float baseGrowSpeed = 0.2f;

    [Tooltip("Speed multiplier per depth level -- deeper branches grow slower.")]
    [SerializeField] float depthSpeedDecay = 0.85f;

    [Header("Segment Lengths")]
    [Tooltip("Target length of each branch chord. Larger = taller, longer-armed tree.")]
    [SerializeField] float branchSegmentLength = 1.0f;

    [Tooltip("Target length of each root segment before it branches. Smaller = denser, earlier splits.")]
    [SerializeField] float rootSegmentLength = 0.7f;

    [Tooltip("Multiplier on root segment length when the root is near or at the tray boundary (distRatio ≥ 0.8). " +
             "Shorter segments = more deflection steps = smoother curves along the wall. 0.25–0.4 recommended.")]
    [SerializeField] [Range(0.1f, 1f)] float wallSegmentScale = 0.35f;

    [Tooltip("Each depth level, branch segments get this much shorter (0..1).")]
    [SerializeField] float segmentLengthDecay = 0.80f;

    [Tooltip("Each depth level, root segments get this much shorter (0..1). " +
             "Keep this higher than segmentLengthDecay so roots stay long enough to reach the spread radius.")]
    [SerializeField] float rootSegmentLengthDecay = 0.95f;

    [Header("Radii")]
    [Tooltip("Fixed radius of every new terminal segment. The pipe model derives all " +
             "parent radii from this -- more branches = thicker trunk automatically.")]
    [SerializeField] float terminalRadius = 0.04f;

    [Tooltip("Starting radius for root segments (much thinner than branches).")]
    [SerializeField] float rootTerminalRadius = 0.004f;

    [Header("Branching")]
    [Tooltip("Probability per segment of spawning a lateral branch (at depth 0).")]
    [SerializeField] float baseBranchChance = 0.75f;

    [Tooltip("Branch chance decreases with depth -- keeps tips from over-branching.")]
    [SerializeField] float branchChanceDepthDecay = 0.90f;

    [Tooltip("Hard safety cap on segment depth.")]
    [SerializeField] int maxDepth = 50;

    [Tooltip("How many depth levels the tree can grow per year. " +
             "Year 1 cap = depthsPerYear. Year 2 = 2xdepthsPerYear. Etc. " +
             "2-3 is realistic for a young tree; 4-5 for faster-growing species.")]
    [SerializeField] int depthsPerYear = 3;

    [Tooltip("Fraction of targetLength added to each existing segment per year. " +
             "Trunk (depth 0) elongates the most; deeper branches decay by elongationDepthDecay.")]
    [SerializeField] float baseElongation = 0.05f;

    [Tooltip("Multiplier applied to baseElongation per depth level (0–1). " +
             "0.7 means depth-1 elongates 70% as much as depth-0, depth-2 elongates 49%, etc.")]
    [SerializeField] [Range(0f, 1f)] float elongationDepthDecay = 0.7f;

    [Tooltip("Probability of a lateral branch spawning each spring (flat rate, not depth-decayed).")]
    [SerializeField] float springLateralChance = 0.80f;

    [Tooltip("Hard cap on total branch nodes. No new branch segments spawn once reached.\n" +
             "Lateral chances also scale toward zero as the tree approaches this cap,\n" +
             "so vigor naturally diminishes with density. Trim regularly to stay vigorous.")]
    [SerializeField] int maxBranchNodes = 2000;

    [Header("Direction")]
    [Tooltip("How strongly each segment continues its parent's direction (0..1).")]
    [SerializeField] float inertiaWeight = 0.65f;

    [Tooltip("Blend fraction toward the sun per new segment (0 = no lean, 1 = point straight up). " +
             "0.01 = nearly imperceptible, 0.1 = gentle lean, 0.5 = strong pull.")]
    [SerializeField] [Range(0f, 1f)] float phototropismWeight = 0.15f;

    [Tooltip("Magnitude of random perturbation per new segment.")]
    [SerializeField] float randomWeight = 0.15f;

    [Tooltip("Max random deviation angle (degrees) for a new branch direction.")]
    [SerializeField] float branchAngleMin = 25f;
    [SerializeField] float branchAngleMax = 55f;

    [Header("Trunk Subdivisions")]
    [Tooltip("Number of individually wire-able segments the initial trunk is split into. " +
             "All share depth 0 -- they don't count toward the branching depth cap.")]
    [SerializeField] int trunkSubdivisions = 3;

    [Tooltip("Number of individually wire-able sub-segments per branch chord. " +
             "Higher = smoother curves when wired. 1 = no subdivision (original behavior). " +
             "Sub-segments share the parent branch's depth so they don't slow the depth cap.")]
    [SerializeField] [Range(1, 8)] int branchSubdivisions = 3;

    [Tooltip("Minimum length for each sub-segment after subdivision. " +
             "Prevents tip segments from becoming too small to wire. " +
             "Actual segment = max(chordLength/N, minSegmentLength).")]
    [SerializeField] float minSegmentLength = 0.15f;

    [Header("Bud System")]
    [Tooltip("Prefab spawned at terminal tips each autumn as a visible dormant bud. Assign in Inspector.")]
    [SerializeField] GameObject budPrefab;

    [Tooltip("Optional prefab for dormant lateral buds on non-terminal nodes. Leave empty to skip.")]
    [SerializeField] GameObject lateralBudPrefab;

    [Tooltip("Alternate: one continuation + one optional lateral per node (most trees).\n" +
             "Opposite: two symmetric equal forks per node (Japanese maple, ash, dogwood).")]
    [SerializeField] BudType budType = BudType.Alternate;

    [Tooltip("How strongly the terminal bud suppresses lateral shoots (0 = equal vigor, 1 = laterals don't grow).\n" +
             "Japanese maple ≈ 0.1  |  most broadleaf trees ≈ 0.3–0.5  |  conifers ≈ 0.7–0.9")]
    [SerializeField] [Range(0f, 1f)] float apicalDominance = 0.3f;

    [Tooltip("Chance per interior junction node per spring to spontaneously sprout a new lateral,\n" +
             "simulating dormant axillary buds breaking on old wood without a trimming event.\n" +
             "Japanese maple ≈ 0.05–0.15  |  most trees ≈ 0–0.02")]
    [SerializeField] [Range(0f, 0.5f)] float oldWoodBudChance = 0.01f;

    [Tooltip("Base chance per stimulated ancestor node to sprout a back-bud each spring.\n" +
             "Independent of springLateralChance so you can tune them separately.\n" +
             "0.15 = ~1 bud from 3 stimulated ancestors on a young tree.")]
    [SerializeField] [Range(0f, 1f)] float backBudBaseChance = 0.15f;

    [Tooltip("Multiplier applied to backBudBaseChance on nodes whose tip ancestry was trimmed (back-budding).")]
    [SerializeField] [Range(1f, 10f)] float backBudActivationBoost = 1f;

    [Tooltip("Spawn the terminal bud prefab at tip nodes each autumn. Disable to hide bud objects without affecting bud growth logic.")]
    [SerializeField] bool showTerminalBuds = true;

    [Tooltip("Spawn the lateral bud prefab at junction nodes each autumn. Disable to hide lateral bud objects without affecting bud growth logic.")]
    [SerializeField] bool showLateralBuds = true;

    [Header("Wiring")]
    [Tooltip("Rate-adjusted in-game days for a wire to fully set (~2 growing seasons at speed 1).")]
    [SerializeField] float wireDaysToSet = 196f;

    [Header("Refinement")]
    [Tooltip("Segment-length multiplier per refinement level. 0.82 = 18% shorter per level; " +
             "at level 6 segments are ~30% of their original length — fine refined twigs.")]
    [SerializeField] [Range(0.5f, 0.99f)] float refinementTaper = 0.82f;

    [Tooltip("Maximum refinement level. Segments stop shortening above this value.")]
    [SerializeField] [Range(2f, 10f)] float refinementCap = 6f;

    [Tooltip("Refinement gained each time a node is the direct cut point of a trim.")]
    [SerializeField] float refinementOnTrim = 0.5f;

    [Tooltip("Refinement gained on a node when one of its back-buds activates in spring.")]
    [SerializeField] float refinementOnBackBud = 0.25f;

    [Tooltip("Average boundaryPressure across root terminals at which leaf miniaturization is maximized. " +
             "Below this → partial shrink; at or above → full shrink factor applied.")]
    [SerializeField] float rootPressureFullRestriction = 8f;

    [Header("Per-Branch Vigor")]
    [Tooltip("Vigor added each spring per node, scaled by 1/depth. Trunk (depth 0) and depth-1 get full bonus; " +
             "deeper nodes get progressively less. Simulates apical dominance.")]
    [SerializeField] float apicalVigorBonus = 0.15f;

    [Tooltip("Each spring, vigor blends this fraction toward 1.0 (regression to mean). " +
             "0.15 = moderate decay; a node at 2.0 drops ~0.15 toward neutral each season.")]
    [SerializeField] [Range(0f, 0.5f)] float vigorDecayRate = 0.15f;

    [Tooltip("Multiplier applied to a node's branchVigor when it is the direct trim cut point. " +
             "0.7 = 30% reduction — cutting the tip weakens that branch.")]
    [SerializeField] [Range(0.1f, 1f)] float vigorTrimMultiplier = 0.7f;

    [Tooltip("Minimum branchVigor a node can reach regardless of damage or trimming.")]
    [SerializeField] float vigorMin = 0.2f;

    [Tooltip("Maximum branchVigor a node can reach. Keeps runaway apical growth in check.")]
    [SerializeField] float vigorMax = 2.0f;

    [Header("Watering")]
    [Tooltip("Moisture lost per in-game day while the tree is actively growing. " +
             "At 0.1 the soil dries out in ~10 in-game days without watering.")]
    [SerializeField] float drainRatePerDay = 0.1f;

    [Tooltip("Moisture level below which drought stress begins accumulating (0→1).")]
    [SerializeField] [Range(0f, 1f)] float droughtThreshold = 0.3f;

    [Tooltip("Health lost per in-game day spent below the drought threshold. " +
             "Applies to all branch nodes at the start of the next growing season.")]
    [SerializeField] float droughtDamagePerDay = 0.008f;

    /// <summary>Current soil moisture 0 (bone dry) → 1 (just watered). Drained each grow tick.</summary>
    public float soilMoisture = 1f;
    public float droughtDaysAccumulated = 0f;

    [Header("Leaf Energy")]
    [Tooltip("Maximum energy multiplier. A very lush, healthy canopy can exceed 1.0 up to this cap " +
             "for a modest extra-vigour bonus. 1.5 = 50% bonus growth at peak canopy.")]
    [SerializeField] float maxEnergyMultiplier = 1.5f;

    [Header("Wound System")]
    [Tooltip("Health drained from a wounded node per growing season. Paste stops this drain.")]
    [SerializeField] float woundDrainRate = 0.05f;

    [Tooltip("Growing seasons to fully callus over one unit of wound radius. " +
             "Larger wounds (thicker cut branches) take proportionally longer to heal. " +
             "E.g. radius=0.1 × 20 = 2 seasons; radius=0.5 × 20 = 10 seasons.")]
    [SerializeField] float seasonsToHealPerUnit = 20f;

    [Tooltip("Health hit applied to the cut-site node each time it is trimmed. " +
             "Accumulates: cutting the same node three times in one session is noticeably weakening. " +
             "Recovers at trimTraumaRecoveryPerSeason each spring.")]
    [SerializeField] float trimTraumaDamage = 0.05f;

    [Tooltip("Health recovered per growing season on any damaged non-root node. " +
             "Covers trim trauma and slow wire recovery. " +
             "Keep slightly below woundDrainRate so unprotected wounds still net-worsen over time.")]
    [SerializeField] float trimTraumaRecoveryPerSeason = 0.04f;

    [Tooltip("Optional material for wound visualization. " +
             "If left empty a plain dark-brown Unlit material is used automatically. " +
             "The bark shader uses vertex colours so it can't be darkened at runtime.")]
    [SerializeField] Material woundMaterialOverride;

    [Header("Root System")]
    [Tooltip("Maximum node depth a root strand can grow to.")]
    [SerializeField] int maxRootDepth = 12;

    [Tooltip("Probability of a lateral sub-root branching off per root segment.")]
    [SerializeField] float rootLateralChance = 0.65f;

    [Tooltip("How much lateral chance decays per depth level. " +
             "1.0 = no decay (every depth equally likely to lateral). " +
             "0.7 = deep roots rarely branch. Raise toward 1.0 for quicker, denser branching.")]
    [SerializeField] [Range(0.5f, 1f)] float rootLateralDepthDecay = 0.85f;

    [Tooltip("New trunk root strands are planted automatically each spring until this many " +
             "direct-trunk roots exist. Spread evenly around the trunk.")]
    [SerializeField] int targetTrunkRoots = 21;

    [Tooltip("How many new trunk roots are planted per spring. " +
             "Lower = slower, more organic buildup toward targetTrunkRoots.")]
    [SerializeField] [Range(1, 5)] int trunkRootsPerYear = 2;

    [Tooltip("Assign the 'Root Area' scene object. When set, roots fill that box instead of " +
             "spreading by radius. Leave empty to use the legacy radial spread system.")]
    [SerializeField] Transform rootAreaTransform;

    [Header("Root Health")]
    [Tooltip("Maximum root depth counted toward the root health score. 1 = only first segments off the trunk; 3 captures the full surface flare.")]
    [SerializeField] int rootHealthMaxDepth = 3;

    [Tooltip("Root tips within this many Y-units of the soil surface (Y=0) count as surface roots for root health scoring.")]
    [SerializeField] float rootHealthSurfaceDepth = 0.3f;

    [Tooltip("Root radius considered ideal for the girth component. Roots at or above this thickness score full girth points.")]
    [SerializeField] float rootHealthTargetRadius = 0.04f;

    [Tooltip("If the centre of mass of surface roots is this far from the trunk horizontally, balance reaches zero.")]
    [SerializeField] float rootHealthBalanceRadius = 1.5f;

    [Tooltip("Fallback used when no Root Area is assigned: spread target = tree height × this.\n" +
             "Ignored when rootAreaTransform is set.")]
    [SerializeField] float rootSpreadMultiplier = 2f;

    [Tooltip("Chance per non-terminal root node per season to sprout a new fill-in lateral " +
             "inside the spread radius. Higher = denser root mat over time.")]
    [SerializeField] [Range(0f, 1f)] float rootFillLateralChance = 0.03f;

    [Tooltip("Seasons a root terminal must spend near a wall (distRatio ≥ 0.85) before " +
             "pot-bound effects activate: slower growth, thickening, and inner fill boost.")]
    [SerializeField] int boundaryPressureThreshold = 3;

    [Tooltip("Growth speed multiplier applied to pot-bound root terminals (fraction of normal rate). " +
             "0.35 = roots near walls grow at 35% speed.")]
    [SerializeField] [Range(0f, 1f)] float boundaryGrowthScale = 0.35f;

    [Tooltip("Radius added to a pot-bound root node per season it stays at the wall. " +
             "Thickening propagates up the pipe model, making wall-hugging roots visibly gnarled.")]
    [SerializeField] float boundaryThickenRate = 0.003f;

    [Tooltip("Multiplier on rootFillLateralChance for low-depth (≤ 2) root ancestors of pot-bound terminals. " +
             "Simulates the tree pushing new root mass back toward the trunk when it can't spread further.")]
    [SerializeField] float potBoundInnerBoost = 3f;

    [Tooltip("Maximum new inner-fill laterals spawned per season from the pot-bound system. " +
             "This budget is independent of maxTotalRootNodes so it fires even when the outer cap is full.")]
    [SerializeField] int potBoundMaxFillPerYear = 30;

    [Tooltip("Hard cap on total root nodes. No new root segments (continuation or lateral) " +
             "spawn once this is reached. Prevents unbounded root growth over many seasons.")]
    [SerializeField] int maxTotalRootNodes = 1500;

    [Tooltip("Outward-from-trunk radial bias on root continuation. Keeps roots spreading wide.")]
    [SerializeField] float rootRadialWeight = 0.25f;

    [Tooltip("Downward gravity bias on root continuation. Keep this small — roots should stay near the surface.")]
    [SerializeField] float rootGravityWeight = 0.05f;

    [Tooltip("Initial downward Y component added to a freshly planted root direction (before normalising).")]
    [SerializeField] float rootInitialPitch = 0.08f;

    [Tooltip("Number of root strands automatically generated when the tree is first created.")]
    [SerializeField] int initialRootCount = 5;

    [Tooltip("How far the tree object lifts in world units when entering Root Prune mode.")]
    [SerializeField] float rootLiftHeight = 3.5f;

    [Tooltip("Lift/lower animation speed (units per second).")]
    [SerializeField] float rootLiftSpeed = 4f;

    [Tooltip("Distance from the planting surface at which roots begin to hug the surface.")]
    [SerializeField] float rootSurfaceSnapDist = 0.8f;

    [Header("Ishitsuki Rock")]
    [Tooltip("Convex MeshCollider of the placed rock. Set at runtime by RockPlacer on orientation confirm.")]
    public Collider rockCollider;

    /// <summary>
    /// True once a rock has been confirmed. Unlike rockCollider, this never goes null
    /// even if the Collider reference somehow gets cleared — used to gate Ishitsuki-mode
    /// logic everywhere so we don't depend on rockCollider != null as the mode flag.
    /// </summary>
    [HideInInspector] public bool isIshitsukiMode = false;

    [Tooltip("World-unit radius around the rock surface within which roots deflect to follow it.")]
    [SerializeField] float rockInfluenceRadius = 0.4f;

    [Tooltip("Enable to override the auto-computed soil Y with the value below. Useful for testing root draping.")]
    [SerializeField] bool  debugSoilYOverride = false;

    [Tooltip("Manual soil Y world position. Only used when Debug Soil Y Override is enabled. Defaults to -9999 which auto-populates from the computed soilY on first use — then you can nudge it.")]
    [SerializeField] float debugSoilY = -9999f;

    // ── Soil debug GL overlay (set by SpawnTrainingWires, rendered by OnRenderObject) ──
    bool    _soilDbgActive;
    float   _soilDbgEndTime;
    float   _soilDbgSoilY, _soilDbgRockTop, _soilDbgRockBot;
    Vector3 _soilDbgCenter;
    float   _soilDbgR;
    Material _soilDbgMat;

    [Header("Air Layering")]
    [Tooltip("Prefab spawned at the air layer site to represent the coconut coir wrap. Optional — system works without it.")]
    [SerializeField] GameObject airLayerWrapPrefab;

    [Tooltip("Number of growing seasons before roots develop under the wrap.")]
    [SerializeField] int airLayerSeasonsToRoot = 2;

    [Tooltip("Number of new roots spawned when the wrap is removed.")]
    [SerializeField] [Range(2, 21)] int airLayerRootCount = 17;

    [Tooltip("Number of segments per root strand spawned at unwrap. More = longer, snakier roots from the start.")]
    [SerializeField] [Range(1, 8)] int airLayerRootSegments = 3;

    [Tooltip("Target length of each segment on a newly-spawned air-layer root strand.")]
    [SerializeField] float airLayerRootTargetLength = 1.0f;

    [Tooltip("Radius of air-layer roots as a fraction of the trunk node's radius at the layer site.")]
    [SerializeField] [Range(0.1f, 1f)] float airLayerRootRadiusMultiplier = 0.35f;

    [Tooltip("Ishitsuki cable radius at the trunk base as a fraction of trunk radius. Tapers to rootTerminalRadius toward the tips.")]
    [SerializeField] [Range(0.05f, 1f)] float ishitsukiCableRadiusMultiplier = 0.3f;

    [Tooltip("Minimum angle (degrees) allowed between consecutive cable segments. If a new segment would bend back toward the trunk sharper than this, it falls straight down instead. 60–90 prevents the sharp U-turns visible on the upper rock face.")]
    [SerializeField] [Range(10f, 90f)] float minCableAngleDeg = 65f;

    [Header("Seed")]
    [Tooltip("Trunk length at which the seed object is hidden (the sprout has emerged).")]
    [SerializeField] float seedHideLength = 0.25f;

    // References

    [HideInInspector] public TreeMeshBuilder meshBuilder;

    /// <summary>Fired after a trim, with the list of every node that was removed.</summary>
    public event Action<List<TreeNode>> OnSubtreeTrimmed;

    // Tree Data

    [HideInInspector] public TreeNode       root;
    [HideInInspector] public List<TreeNode> allNodes = new List<TreeNode>();

    int   nextId          = 0;
    bool  isGrowing       = false;
    int   lastGrownYear   = -1;
    int   startYear       = -1;
    int   startMonth      = -1;
    float cachedTreeHeight = 1f;  // updated each spring; used for root spread radius
    int   lastRecalcDay   = -1;   // tracks last in-game day RecalculateRadii was run mid-season

    // ── Leaf energy ───────────────────────────────────────────────────────────
    /// <summary>
    /// Photosynthetic energy from last season's canopy. Computed by LeafManager
    /// at bud-set time (September). Multiplies growth speed, lateral chance, and
    /// health recovery the following spring. Initialised to 1 so year-1 grows normally.
    /// </summary>
    public float treeEnergy = 1f;

    /// <summary>
    /// Averaged boundaryPressure across root terminals, normalized to [0, 1].
    /// 0 = no pot-bound pressure; 1 = fully restricted (at or above rootPressureFullRestriction).
    /// Used by LeafManager to drive leaf miniaturization.
    /// </summary>
    public float RootPressureFactor()
    {
        float sum = 0f; int count = 0;
        foreach (var n in allNodes)
            if (n.isRoot && n.isTerminal) { sum += n.boundaryPressure; count++; }
        if (count == 0) return 0f;
        return Mathf.Clamp01(sum / count / Mathf.Max(1f, rootPressureFullRestriction));
    }

    /// <summary>Exposes refinementCap so LeafManager can normalize refinement levels.</summary>
    public float RefinementCap => refinementCap;

    // ── Save / Load accessors (expose private fields for SaveManager) ─────────
    public int   SaveStartYear     { get => startYear;      set => startYear      = value; }
    public int   SaveStartMonth    { get => startMonth;     set => startMonth     = value; }
    public int   SaveLastGrownYear { get => lastGrownYear;  set => lastGrownYear  = value; }

    /// <summary>Fill soil moisture to 1.0. Called by the watering can button.</summary>
    public void Water()
    {
        soilMoisture = 1f;
        Debug.Log($"[Water] Watered | moisture restored to 1.0 | year={GameManager.year} month={GameManager.month}");
    }

    // ── Save / Load ───────────────────────────────────────────────────────────

    /// <summary>
    /// Rebuilds the entire tree from deserialized save data.
    /// Called by SaveManager.Load() after GameManager time state is already restored.
    /// </summary>
    public void LoadFromSaveData(SaveData data, LeafManager leafManager)
    {
        // Clear all existing GameObjects and state
        foreach (var go in budObjects.Values)    if (go != null) Destroy(go);
        budObjects.Clear();
        foreach (var go in lateralBudObjects)    if (go != null) Destroy(go);
        lateralBudObjects.Clear();
        foreach (var go in woundObjects.Values)  if (go != null) Destroy(go);
        woundObjects.Clear();
        if (seedObject != null) { Destroy(seedObject); seedObject = null; }
        allNodes.Clear();
        root = null;

        // Restore skeleton live state
        treeEnergy             = data.treeEnergy;
        soilMoisture           = data.soilMoisture;
        droughtDaysAccumulated = data.droughtDaysAccumulated;
        startYear              = data.startYear;
        startMonth             = data.startMonth;
        lastGrownYear          = data.lastGrownYear;
        isIshitsukiMode        = data.isIshitsukiMode;
        plantingNormal         = new Vector3(data.planNX, data.planNY, data.planNZ);
        plantingSurfacePoint   = new Vector3(data.planPX, data.planPY, data.planPZ);
        nextId                 = 0;

        // Pass 1: create TreeNode objects from save data
        var nodeById = new Dictionary<int, TreeNode>(data.nodes.Count);
        foreach (var sn in data.nodes)
        {
            var pos = new Vector3(sn.px, sn.py, sn.pz);
            var dir = new Vector3(sn.dx, sn.dy, sn.dz);
            var node = new TreeNode(sn.id, sn.depth, pos, dir, sn.radius, sn.targetLength, null);

            node.minRadius          = sn.minRadius;
            node.length             = sn.length;
            node.isGrowing          = sn.isGrowing;
            node.age                = sn.age;
            node.isTrimmed          = sn.isTrimmed;
            node.hasLeaves          = sn.hasLeaves;
            node.isRoot             = sn.isRoot;
            node.subdivisionsLeft   = sn.subdivisionsLeft;
            node.birthYear          = sn.birthYear;
            node.refinementLevel    = sn.refinementLevel;
            node.branchVigor        = sn.branchVigor;
            node.hasBud             = sn.hasBud;
            node.backBudStimulated  = sn.backBudStimulated;
            node.isTrimCutPoint     = sn.isTrimCutPoint;
            node.trimCutDepth       = sn.trimCutDepth;
            node.regrowthSeasonCount= sn.regrowthSeasonCount;
            node.health             = sn.health;
            node.hasWire            = sn.hasWire;
            node.wireOriginalDirection = new Vector3(sn.woX, sn.woY, sn.woZ);
            node.wireTargetDirection   = new Vector3(sn.wtX, sn.wtY, sn.wtZ);
            node.wireSetProgress    = sn.wireSetProgress;
            node.wireDamageProgress = sn.wireDamageProgress;
            node.wireAgeDays        = sn.wireAgeDays;
            node.isTrainingWire     = sn.isTrainingWire;
            node.boundaryPressure   = sn.boundaryPressure;
            node.isAirLayerRoot     = sn.isAirLayerRoot;
            node.hasWound           = sn.hasWound;
            node.woundRadius        = sn.woundRadius;
            node.woundFaceNormal    = new Vector3(sn.wnX, sn.wnY, sn.wnZ);
            node.woundAge           = sn.woundAge;
            node.pasteApplied       = sn.pasteApplied;

            nodeById[sn.id] = node;
            allNodes.Add(node);
            if (sn.id >= nextId) nextId = sn.id + 1;
        }

        // Pass 2: re-link parent/child references
        foreach (var sn in data.nodes)
        {
            var node = nodeById[sn.id];
            if (sn.parentId >= 0 && nodeById.TryGetValue(sn.parentId, out var parent))
            {
                node.parent = parent;
                parent.children.Add(node);
            }
            else
            {
                root = node;
            }
        }

        // Pass 3: re-spawn wound objects
        foreach (var node in allNodes)
            if (node.hasWound) CreateWoundObject(node);

        // Rebuild the mesh
        RecalculateRadii(root);
        meshBuilder.SetDirty();

        // Pass 4: restore leaves on terminal non-root nodes
        if (leafManager != null)
        {
            leafManager.ClearAllLeaves();
            var leafNodes = new System.Collections.Generic.List<TreeNode>();
            foreach (var node in allNodes)
                if (node.isTerminal && !node.isTrimmed && !node.isRoot && node.hasLeaves)
                    leafNodes.Add(node);
            leafManager.ForceSpawnLeaves(leafNodes);
        }

        Debug.Log($"[Save] Tree restored: {allNodes.Count} nodes | root={root?.id} | year={GameManager.year}");
    }

    // ── Trim undo ─────────────────────────────────────────────────────────────
    TrimUndoState pendingUndo = null;

    [Tooltip("Seconds after a trim during which Ctrl+Z can undo it. Season tick clears the window.")]
    [SerializeField] float undoWindowSeconds = 5f;

    public bool  CanUndo          => pendingUndo != null &&
                                     Time.realtimeSinceStartup - pendingUndo.timestamp < undoWindowSeconds;
    public float UndoTimeRemaining => pendingUndo == null ? 0f :
                                      Mathf.Max(0f, undoWindowSeconds - (Time.realtimeSinceStartup - pendingUndo.timestamp));

    // node.id → live terminal bud GameObject (spawned at season end, destroyed on bud break)
    readonly Dictionary<int, GameObject> budObjects = new Dictionary<int, GameObject>();

    // Lateral (axillary) bud GameObjects — all destroyed at spring start
    readonly List<GameObject> lateralBudObjects = new List<GameObject>();

    // node.id → wound visualization GameObject (half-torus at the cut face)
    readonly Dictionary<int, GameObject> woundObjects = new Dictionary<int, GameObject>();

    // ── Root Health ───────────────────────────────────────────────────────────
    /// <summary>0–100 root health score. Updated each spring and on RootPrune entry.</summary>
    public float RootHealthScore { get; private set; }
    /// <summary>Per-sector coverage, normalised 0–1. Length = 8 (N, NE, E, SE, S, SW, W, NW).</summary>
    public float[] RootHealthSectorCoverage { get; private set; } = new float[8];

    // ── Menu-exposed tuning properties ───────────────────────────────────────
    public float BaseGrowSpeed
    {
        get => baseGrowSpeed;
        set => baseGrowSpeed = Mathf.Clamp(value, 0.01f, 1f);
    }
    public float SpringLateralChance
    {
        get => springLateralChance;
        set => springLateralChance = Mathf.Clamp01(value);
    }
    public float DepthSpeedDecay
    {
        get => depthSpeedDecay;
        set => depthSpeedDecay = Mathf.Clamp(value, 0.5f, 1f);
    }
    public float IshitsukiCableRadiusMultiplier
    {
        get => ishitsukiCableRadiusMultiplier;
        set => ishitsukiCableRadiusMultiplier = Mathf.Clamp(value, 0.05f, 1f);
    }
    public float MinCableAngleDeg
    {
        get => minCableAngleDeg;
        set => minCableAngleDeg = Mathf.Clamp(value, 10f, 90f);
    }

    // ── Air Layering ──────────────────────────────────────────────────────────
    /// <summary>
    /// Tracks all active air layers. Exposed so TreeInteraction can read layer state.
    /// </summary>
    public readonly List<AirLayerData> airLayers = new List<AirLayerData>();

    /// <summary>Data for one active air layer on the trunk.</summary>
    public class AirLayerData
    {
        public TreeNode   node;           // trunk node the layer is applied to
        public int        seasonsElapsed; // growing seasons since placement
        public bool       rootsSpawned;   // true once roots are ready to emerge
        public GameObject wrapObject;     // the coir wrap visual (may be null)
    }

    float debugLogTimer  = 0f;

    readonly Stopwatch growthTimer = new Stopwatch();
    long totalGrowthMs = 0;
    int  growthFrames  = 0;

    // Seed visual -- hidden once the sprout has grown past seedHideLength
    GameObject seedObject;

    // Root lift animation
    float initY       = 0f;  // world Y the tree rests at (updated by SetPlantingSurface)
    float liftTarget  = 0f;  // 0 = grounded, rootLiftHeight = lifted
    float currentLift = 0f;

    // Planting surface -- the surface the tree rests on.
    // Initially flat ground (normal = up, point = origin).
    // Updated by SetPlantingSurface() when player places the tree on a rock.
    [HideInInspector] public Vector3 plantingNormal       = Vector3.up;
    [HideInInspector] public Vector3 plantingSurfacePoint = Vector3.zero;

    /// <summary>Maximum depth allowed to sprout children this season.</summary>
    int SeasonDepthCap => startYear < 0
        ? depthsPerYear
        : Mathf.Min(maxDepth, (GameManager.year - startYear + 1) * depthsPerYear);

    /// <summary>
    /// Returns the effective depth cap for a node, capped by any trim cut point
    /// in its ancestry.  A fresh stump only allows depthsPerYear new levels per
    /// season, mirroring year-1 pacing.  Returns SeasonDepthCap when no
    /// cut-point restriction applies.
    /// </summary>
    int CutPointDepthCap(TreeNode node)
    {
        TreeNode n = node;
        while (n != null)
        {
            if (n.isTrimCutPoint)
                return n.trimCutDepth + n.regrowthSeasonCount * depthsPerYear;
            n = n.parent;
        }
        return SeasonDepthCap;
    }

    // Unity

    void Awake()
    {
        meshBuilder = GetComponent<TreeMeshBuilder>();
        if (meshBuilder == null)
            Debug.LogError("TreeSkeleton: TreeMeshBuilder not found on this GameObject -- both components must be on the same GameObject.", this);

        initY = transform.position.y;
    }

    void OnEnable()  => GameManager.OnGameStateChanged += OnGameStateChanged;
    void OnDisable() => GameManager.OnGameStateChanged -= OnGameStateChanged;

    // ── Soil debug GL overlay — renders into both Game View and Scene View ────
    void OnRenderObject()
    {
        if (!_soilDbgActive) return;
        if (Time.realtimeSinceStartup > _soilDbgEndTime) { _soilDbgActive = false; return; }

        if (_soilDbgMat == null)
        {
            Shader sh = Shader.Find("Hidden/Internal-Colored");
            if (sh == null) return;
            _soilDbgMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            _soilDbgMat.SetInt("_ZWrite", 0);
            _soilDbgMat.SetInt("_Cull",   0);
            _soilDbgMat.SetInt("_ZTest",  (int)UnityEngine.Rendering.CompareFunction.Always);
        }

        _soilDbgMat.SetPass(0);
        GL.PushMatrix();
        GL.Begin(GL.LINES);

        Vector3 c = _soilDbgCenter;
        float   r = _soilDbgR;

        // GREEN cross + 4 tall pillars = soilY (where roots stop)
        GL.Color(Color.green);
        GL.Vertex3(c.x - r, _soilDbgSoilY, c.z);     GL.Vertex3(c.x + r, _soilDbgSoilY, c.z);
        GL.Vertex3(c.x, _soilDbgSoilY, c.z - r);     GL.Vertex3(c.x, _soilDbgSoilY, c.z + r);
        // Vertical pillars so they're visible edge-on
        GL.Vertex3(c.x - r, _soilDbgSoilY, c.z - r); GL.Vertex3(c.x - r, _soilDbgSoilY + 3f, c.z - r);
        GL.Vertex3(c.x + r, _soilDbgSoilY, c.z - r); GL.Vertex3(c.x + r, _soilDbgSoilY + 3f, c.z - r);
        GL.Vertex3(c.x - r, _soilDbgSoilY, c.z + r); GL.Vertex3(c.x - r, _soilDbgSoilY + 3f, c.z + r);
        GL.Vertex3(c.x + r, _soilDbgSoilY, c.z + r); GL.Vertex3(c.x + r, _soilDbgSoilY + 3f, c.z + r);

        // RED cross = rock bottom bound (old wrong soilY target)
        GL.Color(Color.red);
        GL.Vertex3(c.x - r, _soilDbgRockBot, c.z);   GL.Vertex3(c.x + r, _soilDbgRockBot, c.z);
        GL.Vertex3(c.x, _soilDbgRockBot, c.z - r);   GL.Vertex3(c.x, _soilDbgRockBot, c.z + r);

        // CYAN cross = rock top bound
        GL.Color(Color.cyan);
        GL.Vertex3(c.x - r, _soilDbgRockTop, c.z);   GL.Vertex3(c.x + r, _soilDbgRockTop, c.z);
        GL.Vertex3(c.x, _soilDbgRockTop, c.z - r);   GL.Vertex3(c.x, _soilDbgRockTop, c.z + r);

        GL.End();
        GL.PopMatrix();
    }

    void Update()
    {
        // Debug: press 1-9 to instantly simulate that many years of growth
        for (int k = 1; k <= 9; k++)
        {
            if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + k - 1)))
            {
                for (int y = 0; y < k; y++) SimulateYear();
                break;
            }
        }

        // Root lift animation -- runs regardless of grow state
        if (!Mathf.Approximately(currentLift, liftTarget))
        {
            currentLift = Mathf.MoveTowards(currentLift, liftTarget, rootLiftSpeed * Time.deltaTime);
            var p = transform.position;
            transform.position = new Vector3(p.x, initY + currentLift, p.z);
        }

        // Hide the seed after ~1 in-game month OR once the sprout is tall enough.
        if (seedObject != null && seedObject.activeSelf && root != null)
        {
            bool sproutVisible = root.length >= seedHideLength;
            bool monthPassed   = startMonth >= 0 &&
                                 (GameManager.year * 12 + GameManager.month) >
                                 (startYear  * 12 + startMonth);
            if (sproutVisible || monthPassed)
            {
                seedObject.SetActive(false);
                Debug.Log("[Tree] Seed hidden");
            }
        }

        // Keep air layer wraps sized to the trunk every frame so they can't be swallowed.
        if (airLayers.Count > 0)
            foreach (var layer in airLayers)
                SetAirLayerWrapTransform(layer);

        // Keep air layer root bases anchored to parent tip as the trunk grows.
        UpdateAirLayerRootPositions();

        if (!isGrowing || root == null) return;

        float rate = GameManager.SeasonalGrowthRate;
        if (rate <= 0f) return;

        bool structureChanged = false;
        bool anyGrew          = false;

        // TIMESCALE/24f converts real seconds to in-game days
        float inGameDays = Time.deltaTime * GameManager.TIMESCALE / 24f;

        // Soil moisture drain — slower when the season slows down (rate already encodes that)
        soilMoisture = Mathf.Max(0f, soilMoisture - drainRatePerDay * inGameDays * rate);
        if (soilMoisture < droughtThreshold)
            droughtDaysAccumulated += inGameDays;

        // Snapshot growing nodes -- we may add new ones during this loop
        var snapshot = new List<TreeNode>(allNodes.Count);
        foreach (var node in allNodes)
        {
            if (node.isGrowing && !node.isTrimmed)
                snapshot.Add(node);
        }

        // Log once per real second
        debugLogTimer += Time.deltaTime;
        bool doLog = debugLogTimer >= 1f;
        if (doLog)
        {
            debugLogTimer = 0f;
            int maxNodeDepth = 0;
            int rootNodes = 0, branchNodes = 0;
            foreach (var n in allNodes)
            {
                if (n.depth > maxNodeDepth) maxNodeDepth = n.depth;
                if (n.isRoot) rootNodes++; else branchNodes++;
            }
            long avgGrowth = growthFrames > 0 ? totalGrowthMs / growthFrames : 0;
            Debug.Log($"[Tree] {GameManager.month}/{GameManager.day}/{GameManager.year} | " +
                      $"rate={rate:F2} growing={snapshot.Count} " +
                      $"total={allNodes.Count} (branches={branchNodes} roots={rootNodes}) maxDepth={maxNodeDepth} | " +
                      $"growthLoop avg={avgGrowth}ms over {growthFrames} frames");
            totalGrowthMs = 0;
            growthFrames  = 0;
        }

        growthTimer.Restart();
        foreach (var node in snapshot)
        {
            // Dormant from poor health -- skip this node entirely
            if (node.health < 0.25f) continue;

            // Health below 0.75 proportionally slows growth
            float healthMult = node.health >= 0.75f ? 1f : node.health;

            float speed = baseGrowSpeed
                             * rate
                             * Mathf.Pow(depthSpeedDecay, node.depth)
                             * healthMult
                             * treeEnergy;

            // Pot-bound roots near the tray wall grow slower
            if (node.isRoot && node.boundaryPressure >= boundaryPressureThreshold)
                speed *= boundaryGrowthScale;

            node.length += speed * inGameDays;
            node.age    += inGameDays * rate;
            anyGrew      = true;

            if (node.length >= node.targetLength)
            {
                bool belowCap;
                if (node.isRoot && isIshitsukiMode)
                {
                    // Ishitsuki: roots stop at soil surface, not at depth limit.
                    Vector3 tipW = transform.TransformPoint(node.tipPosition);
                    belowCap = tipW.y > plantingSurfacePoint.y;
                }
                else
                {
                    belowCap = node.isRoot
                        ? node.depth < maxRootDepth
                        : node.depth < SeasonDepthCap && node.depth < CutPointDepthCap(node);
                }

                if (belowCap)
                {
                    node.length    = node.targetLength;
                    node.isGrowing = false;
                    // Finalize radius and lock minRadius so RecalculateRadii can't collapse
                    // this node to 0 when its new children (starting at radius=0) are summed.
                    float finalR   = node.isRoot ? rootTerminalRadius : terminalRadius;
                    node.radius    = finalR;
                    node.minRadius = finalR;
                    SpawnChildren(node);
                    structureChanged = true;
                }
            }
            else
            {
                // Ramp radius proportional to growth progress so parent thickening
                // spreads across the season rather than spiking at spawn time.
                float targetR = node.isRoot ? rootTerminalRadius : terminalRadius;
                node.radius = targetR * Mathf.Clamp01(node.length / node.targetLength);
            }
        }

        // Age accumulation — all non-trimmed, non-root nodes age each growing tick,
        // not just the ones currently growing. This drives the new-growth-to-bark
        // material fade in TreeMeshBuilder even after a segment stops elongating.
        // Training wire cable nodes are roots with isGrowing=false permanently, so
        // they're included here too — otherwise they never brown.
        foreach (var node in allNodes)
        {
            if (node.isTrimmed || node.isGrowing) continue;
            if (node.isRoot && !node.isTrainingWire) continue;
            node.age += inGameDays * rate;
        }

        // Wire progress accumulation
        foreach (var node in allNodes)
        {
            if (!node.hasWire || node.isTrimmed) continue;

            node.wireAgeDays += inGameDays * rate;

            if (node.wireSetProgress < 1f)
            {
                node.wireSetProgress = Mathf.Min(1f,
                    node.wireSetProgress + inGameDays * rate / wireDaysToSet);
            }
            else if (node.wireDamageProgress < 1f)
            {
                float dmgDelta = inGameDays * rate / wireDaysToSet;
                node.wireDamageProgress = Mathf.Min(1f, node.wireDamageProgress + dmgDelta);
                ApplyDamage(node, DamageType.WireDamage, dmgDelta * 0.5f);
            }
        }

        growthTimer.Stop();
        totalGrowthMs += growthTimer.ElapsedMilliseconds;
        growthFrames++;

        // RecalculateRadii once per in-game day. Structural events (trim, spring start)
        // still trigger it immediately via their own direct calls.
        // Mid-season, the daily cadence picks up the ramping radii of growing nodes
        // and propagates them up the tree gradually rather than all at once.
        bool newDay = GameManager.day != lastRecalcDay;
        if (structureChanged || newDay)
        {
            RecalculateRadii(root);
            if (newDay) lastRecalcDay = GameManager.day;
        }

        if (anyGrew || structureChanged)
            meshBuilder.SetDirty();
    }

    // Game State

    void OnGameStateChanged(GameState state)
    {
        if (state == GameState.Water && root == null)
            InitTree();

        bool wasGrowing = isGrowing;
        isGrowing = (state == GameState.BranchGrow);
        Debug.Log($"[Tree] State -> {state} | isGrowing={isGrowing} | year={GameManager.year} lastGrownYear={lastGrownYear}");

        // Season just ended — freeze all still-growing segments at their current length.
        // Only on TimeGo (the true season end), not on temporary tool states like Wiring.
        if (state == GameState.TimeGo && wasGrowing && root != null)
        {
            foreach (var node in allNodes)
                if (node.isGrowing) node.isGrowing = false;
            SetBuds();
            meshBuilder.SetDirty();

            // Auto-save at the end of each growing season (after bud set).
            SaveManager.Save(this, GetComponent<LeafManager>());
        }

        // TreeOrient lowers the tree so you orient at working height, not suspended in the air.
        // RootPrune and RockPlace still lift. Everything else grounds the tree.
        bool inRootMode = state == GameState.RootPrune || state == GameState.RockPlace;
        liftTarget = inRootMode ? rootLiftHeight : 0f;
        if (meshBuilder.renderRoots != GameManager.IsRootLiftActive(state))
        {
            meshBuilder.renderRoots = inRootMode;
            meshBuilder.SetDirty();
        }

        if (state == GameState.RootPrune)
        {
            int rootCount = 0;
            foreach (var n in allNodes) if (n.isRoot) rootCount++;
            Debug.Log($"[Root] Entering RootPrune | rootNodes={rootCount} | liftTarget={liftTarget} | plantingNormal={plantingNormal} plantingPoint={plantingSurfacePoint}");
            RecalculateRootHealthScore();
        }

        if (state == GameState.BranchGrow && root != null && GameManager.year > lastGrownYear)
        {
            lastGrownYear = GameManager.year;
            StartNewGrowingSeason();
        }
    }

    void StartNewGrowingSeason()
    {
        // Season tick: undo window expires — the tree has started responding to cuts
        pendingUndo = null;

        // Drought damage: apply accumulated stress from the previous season
        if (droughtDaysAccumulated > 0f)
        {
            float totalDamage = droughtDamagePerDay * droughtDaysAccumulated;
            foreach (var node in allNodes)
                if (!node.isRoot && !node.isTrimmed)
                    ApplyDamage(node, DamageType.Drought, totalDamage);
            Debug.Log($"[Water] Drought damage={totalDamage:F3} over {droughtDaysAccumulated:F1} dry days | year={GameManager.year}");
            droughtDaysAccumulated = 0f;
        }

        foreach (var node in allNodes)
        {
            if (!node.isTrimCutPoint) continue;
            node.regrowthSeasonCount++;
            if (node.trimCutDepth + node.regrowthSeasonCount * depthsPerYear >= SeasonDepthCap)
                node.isTrimCutPoint = false;
        }

        // Refresh cached tree height and compute spread radius for this season
        cachedTreeHeight = CalculateTreeHeight();
        if (rootAreaTransform != null)
            Debug.Log($"[GRoot] StartNewGrowingSeason year={GameManager.year} | treeHeight={cachedTreeHeight:F2} rootArea={rootAreaTransform.lossyScale.x:F2}×{rootAreaTransform.lossyScale.z:F2}");
        else
        {
            float spreadRadius = cachedTreeHeight * rootSpreadMultiplier;
            Debug.Log($"[GRoot] StartNewGrowingSeason year={GameManager.year} | treeHeight={cachedTreeHeight:F2} spreadRadius={spreadRadius:F2}");
        }

        // Auto-plant trunk roots each spring until targetTrunkRoots is reached.
        // Counts only direct children of root that are roots (depth-1 trunk strands).
        int trunkRootCount = 0;
        int totalRootCount = 0;
        foreach (var n in allNodes)
        {
            if (!n.isRoot) continue;
            totalRootCount++;
            if (n.parent == root) trunkRootCount++;
        }
        int rootsToAdd = Mathf.Min(targetTrunkRoots - trunkRootCount, Mathf.Min(trunkRootsPerYear, maxTotalRootNodes - totalRootCount));
        if (rootsToAdd > 0)
        {
            for (int i = 0; i < rootsToAdd; i++)
            {
                // Spread new roots evenly in the gaps between existing ones
                float angle   = (trunkRootCount + i) * (Mathf.PI * 2f / targetTrunkRoots);
                Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                // Ishitsuki roots drape down a rock face — start steep so the first visible
                // segment flows downward rather than shooting radially outward.
                Vector3 dir = isIshitsukiMode
                    ? (outward * 0.35f + Vector3.down).normalized
                    : (outward + Vector3.down * rootInitialPitch).normalized;
                float   len     = Mathf.Max(rootSegmentLength * rootSegmentLengthDecay, 0.3f);

                // In Ishitsuki mode, place the startNode on the bark surface rather than
                // the trunk center so all cables have distinct, spread-out origins and
                // don't bunch into a dense white cluster at the root base.
                Vector3 localOutward = isIshitsukiMode
                    ? transform.InverseTransformDirection(outward).normalized
                    : Vector3.zero;
                float   barkOffset   = isIshitsukiMode ? Mathf.Max(root.radius, 0.04f) : 0f;
                Vector3 startPos     = root.worldPosition + localOutward * barkOffset;

                var r           = CreateNode(startPos, dir, rootTerminalRadius, len, root);
                r.isRoot        = true;
            }
            Debug.Log($"[GRoot] Auto-planted {rootsToAdd} trunk roots | trunkRoots={trunkRootCount + rootsToAdd}/{targetTrunkRoots} year={GameManager.year}");
        }

        // In Ishitsuki mode, pre-grow ALL trunk root cables toward soil before the
        // terminals list is built below.  This does two things:
        //   (a) Newly auto-planted roots get their chains draped over the rock immediately.
        //   (b) Existing mid-rock terminals from prior years get extended further.
        // Both cases prevent ContinuationDirection() from spawning children in random
        // air directions off the mid-rock positions.
        Debug.Log($"[PreGrow] year={GameManager.year} StartNewGrowingSeason: isIshitsukiMode={isIshitsukiMode} rockCollider={(rockCollider != null ? rockCollider.name : "NULL")}");
        if (isIshitsukiMode)
            PreGrowRootsToSoil();

        // Elongate existing segments — lower-depth segments grow longer each year
        if (baseElongation > 0f)
        {
            foreach (var node in allNodes)
            {
                if (node.isTrimmed || node.isRoot || !node.isTerminal) continue;
                float delta = node.targetLength * baseElongation * Mathf.Pow(elongationDepthDecay, node.depth);
                node.targetLength += delta;
                node.length       += delta;  // advance length directly — avoids re-triggering SpawnChildren
            }
        }

        // Lateral bud visuals are only shown over winter; destroy them all at spring start.
        foreach (var go in lateralBudObjects)
            if (go != null) Destroy(go);
        lateralBudObjects.Clear();

        // Resume any non-root segments that were stopped mid-chord in autumn
        // (stopped while isGrowing but before reaching targetLength, no children yet).
        // This covers both mid-subdivision chains and any segment that didn't finish in time.
        // They resume rather than being re-spawned from a bud break.
        int resumed = 0;
        foreach (var node in allNodes)
        {
            if (node.isTrimmed || node.isRoot || !node.isTerminal) continue;
            if (node.isGrowing) continue;                   // already active
            if (node.hasBud) continue;                      // will be handled by bud break below
            if (node.length >= node.targetLength) continue; // fully grown, not mid-chord

            node.isGrowing = true;
            resumed++;
        }
        if (resumed > 0)
            Debug.Log($"[Bud] Resumed mid-chord segments={resumed} year={GameManager.year}");

        // Bud system: if any branch nodes have hasBud set (year 2+), use those as
        // the terminal list. Year 1 (fresh tree, no prior season) falls back to
        // the original isTerminal scan.
        bool budSystemActive = allNodes.Exists(n => !n.isRoot && n.hasBud);

        var terminals = new List<TreeNode>();
        int resuming  = 0;
        foreach (var node in allNodes)
        {
            if (node.isGrowing && !node.isTrimmed) resuming++;

            bool belowCap;
            if (node.isRoot && isIshitsukiMode)
            {
                // Ishitsuki: roots stop at soil surface, not at depth limit.
                Vector3 tipW = transform.TransformPoint(node.tipPosition);
                belowCap = tipW.y > plantingSurfacePoint.y;
            }
            else
            {
                belowCap = node.isRoot
                    ? node.depth < maxRootDepth
                    : node.depth < SeasonDepthCap && node.depth < CutPointDepthCap(node);
            }

            if (node.isRoot)
            {
                if (!node.isTrimmed && node.isTerminal && !node.isGrowing && belowCap)
                    terminals.Add(node);
            }
            else
            {
                bool eligible = budSystemActive
                    ? (!node.isTrimmed && node.hasBud && belowCap)
                    : (!node.isTrimmed && node.isTerminal && !node.isGrowing && belowCap);
                if (eligible) terminals.Add(node);
            }
        }

        int rootTerminals = 0;
        int branchTerminals = 0;
        foreach (var t in terminals) { if (t.isRoot) rootTerminals++; else branchTerminals++; }
        Debug.Log($"[Tree] StartNewGrowingSeason year={GameManager.year} | depthCap={SeasonDepthCap} | terminals={terminals.Count} (roots={rootTerminals} branches={branchTerminals}) resuming={resuming} total={allNodes.Count}");

        // Count current root nodes once so the cap check is O(1) per terminal
        int currentRootCount = 0;
        int currentBranchCount = 0;
        foreach (var n in allNodes)
        {
            if (n.isRoot) currentRootCount++;
            else currentBranchCount++;
        }

        // Vigor: lateral chances scale to zero as the tree approaches the branch node cap.
        // At 0% capacity, vigorFactor=1 (full chance). At 100% cap, vigorFactor=0 (no laterals).
        float vigorFactor = Mathf.Clamp01(1f - (float)currentBranchCount / maxBranchNodes);
        Debug.Log($"[Tree] branchNodes={currentBranchCount}/{maxBranchNodes} vigorFactor={vigorFactor:F2}");

        foreach (var terminal in terminals)
        {
            float baseSegLen  = terminal.isRoot ? rootSegmentLength : branchSegmentLength;
            float decay       = terminal.isRoot ? rootSegmentLengthDecay : segmentLengthDecay;
            float chordLength = baseSegLen * Mathf.Pow(decay, terminal.depth + 1);

            // Refinement shortens internodes: each level applies ×refinementTaper (default 0.82).
            if (!terminal.isRoot && terminal.refinementLevel > 0f)
                chordLength *= Mathf.Pow(refinementTaper, terminal.refinementLevel);

            // Per-branch vigor scales segment length: high-vigor shoots grow longer each season.
            if (!terminal.isRoot)
                chordLength *= terminal.branchVigor;

            // Divide the chord into sub-segments so each is independently wireable.
            // Clamp per-segment (not the chord) so tip segments stay wireable regardless of N.
            float childLength = (!terminal.isRoot && branchSubdivisions > 1) ? chordLength / branchSubdivisions : chordLength;
            childLength = Mathf.Max(childLength, terminal.isRoot ? 0.3f : minSegmentLength);

            float nodeRadius = terminal.isRoot ? rootTerminalRadius : terminalRadius;

            if (terminal.isRoot)
            {
                if (currentRootCount >= maxTotalRootNodes) continue;  // hard cap reached

                bool isIshitsuki = isIshitsukiMode;
                float distRatio  = RootDistRatio(terminal);
                if (!isIshitsuki && distRatio >= 1.3f) continue;  // beyond hard outer boundary — stop
                // In Ishitsuki mode all root cable growth is handled exclusively by
                // PreGrowRootsToSoil (called each spring before this loop).  Skip here
                // to prevent ContinuationDirection from sending cables in wrong directions.
                if (isIshitsuki) continue;

                if (!isIshitsuki && distRatio >= 0.8f) childLength *= wallSegmentScale;

                var cont = CreateNode(terminal.tipPosition, ContinuationDirection(terminal), nodeRadius, childLength, terminal);
                cont.isRoot = true;
                currentRootCount++;

                float lateralScale  = isIshitsuki ? 0f : Mathf.Clamp01(1f - distRatio);
                float lateralChance = rootLateralChance * lateralScale;
                if (currentRootCount < maxTotalRootNodes && Random.value < lateralChance)
                {
                    var lat = CreateNode(terminal.tipPosition, LateralDirection(terminal), nodeRadius, childLength * 0.85f, terminal);
                    lat.isRoot = true;
                    currentRootCount++;
                }
            }
            else
            {
                if (currentBranchCount >= maxBranchNodes) continue;  // hard cap reached

                // Bud break — destroy the dormant bud GameObject
                if (terminal.hasBud)
                {
                    terminal.hasBud = false;
                    if (budObjects.TryGetValue(terminal.id, out var budGo))
                    {
                        Destroy(budGo);
                        budObjects.Remove(terminal.id);
                    }
                }

                if (budType == BudType.Opposite)
                {
                    var forkNeeded = currentBranchCount + 2 <= maxBranchNodes;
                    var (dirA, dirB) = OppositeForkDirections(terminal);
                    var forkA = CreateNode(terminal.tipPosition, dirA, nodeRadius, childLength, terminal);
                    forkA.isRoot = false;
                    if (branchSubdivisions > 1) forkA.subdivisionsLeft = branchSubdivisions - 1;
                    currentBranchCount++;
                    if (forkNeeded)
                    {
                        var forkB = CreateNode(terminal.tipPosition, dirB, nodeRadius, childLength, terminal);
                        forkB.isRoot = false;
                        if (branchSubdivisions > 1) forkB.subdivisionsLeft = branchSubdivisions - 1;
                        currentBranchCount++;
                    }
                    GameManager.branches++;
                }
                else
                {
                    var cont = CreateNode(terminal.tipPosition, ContinuationDirection(terminal), nodeRadius, childLength, terminal);
                    cont.isRoot = false;
                    if (branchSubdivisions > 1)
                        cont.subdivisionsLeft = branchSubdivisions - 1;
                    currentBranchCount++;

                    if (currentBranchCount < maxBranchNodes && Random.value < springLateralChance * vigorFactor * treeEnergy * terminal.branchVigor)
                    {
                        float latLength = childLength * 0.85f * Mathf.Max(0.1f, 1f - apicalDominance);
                        var lat = CreateNode(terminal.tipPosition, LateralDirection(terminal), nodeRadius, latLength, terminal);
                        lat.isRoot = false;
                        if (branchSubdivisions > 1)
                            lat.subdivisionsLeft = branchSubdivisions - 1;
                        currentBranchCount++;
                        GameManager.branches++;
                    }
                }
            }
        }

        // Fill-in laterals: non-terminal root nodes inside the spread radius
        // continue sprouting new side roots each season, densifying the root mat.
        // Snapshot allNodes first — CreateNode appends to allNodes during iteration.
        int fillCount = 0;
        var fillCandidates = new List<TreeNode>(allNodes);
        foreach (var node in fillCandidates)
        {
            if (currentRootCount >= maxTotalRootNodes) break;  // hard cap reached

            if (!node.isRoot || node.isTrimmed || node.isTerminal) continue;
            if (node.depth >= maxRootDepth - 1) continue;

            float distRatio = RootDistRatio(node);
            if (distRatio >= 1f) continue;  // only fill inside the target radius

            // Chance: high near trunk, fades toward the spread edge, decays with depth
            float chance = rootFillLateralChance * (1f - distRatio) * Mathf.Pow(0.6f, node.depth);
            if (Random.value < chance)
            {
                float segLen = rootSegmentLength * Mathf.Pow(rootSegmentLengthDecay, node.depth + 1);
                segLen = Mathf.Max(segLen, 0.3f);
                var lat = CreateNode(node.tipPosition, LateralDirection(node), rootTerminalRadius, segLen, node);
                lat.isRoot = true;
                fillCount++;
                currentRootCount++;
            }
        }

        if (fillCount > 0)
            Debug.Log($"[GRoot] Fill-in laterals sprouted={fillCount} year={GameManager.year}");

        // Boundary pressure: roots near walls thicken, slow down, and stimulate inner fills.
        // Track which low-depth ancestors need a lateral boost this season.
        var potBoundInnerCandidates = new HashSet<TreeNode>();
        int potBoundCount = 0;
        foreach (var node in allNodes)
        {
            if (!node.isRoot || node.isTrimmed) continue;
            // Ishitsuki roots are outside the pot by design — do not treat as pot-bound.
            if (isIshitsukiMode) continue;

            float distRatio = RootDistRatio(node);
            if (distRatio >= 0.85f)
            {
                node.boundaryPressure++;
                if (node.boundaryPressure >= boundaryPressureThreshold)
                {
                    // Thicken — propagates up the pipe model to parent roots
                    node.radius    += boundaryThickenRate;
                    node.minRadius  = Mathf.Max(node.minRadius, node.radius);
                    potBoundCount++;

                    // Walk up toward trunk to collect low-depth ancestors for inner fill
                    TreeNode anc = node.parent;
                    while (anc != null && anc.isRoot)
                    {
                        if (anc.depth <= 2 && !anc.isTerminal)
                            potBoundInnerCandidates.Add(anc);
                        anc = anc.parent;
                    }
                }
            }
            else
            {
                // Pressure decays when the root is no longer crowded against a wall
                node.boundaryPressure = Mathf.Max(0, node.boundaryPressure - 1);
            }
        }

        // Spawn extra fill-in laterals from low-depth ancestors of pot-bound terminals.
        // Simulates the tree pushing new root mass back toward the trunk when walled in.
        int potBoundFillCount = 0;
        // Use a slightly higher ceiling so inner fill still works when outer cap is full,
        // but cap total roots at 1.5× maxTotalRootNodes to prevent unbounded growth.
        int potBoundRootCap = Mathf.RoundToInt(maxTotalRootNodes * 1.5f);
        foreach (var node in potBoundInnerCandidates)
        {
            if (potBoundFillCount >= potBoundMaxFillPerYear) break;
            if (currentRootCount >= potBoundRootCap) break;
            if (node.depth >= maxRootDepth - 1) continue;

            float distRatio = RootDistRatio(node);
            float chance = rootFillLateralChance * potBoundInnerBoost * Mathf.Pow(0.6f, node.depth);
            if (Random.value < chance)
            {
                float segLen = rootSegmentLength * Mathf.Pow(rootSegmentLengthDecay, node.depth + 1);
                segLen = Mathf.Max(segLen, 0.3f);
                var lat = CreateNode(node.tipPosition, LateralDirection(node), rootTerminalRadius, segLen, node);
                lat.isRoot = true;
                currentRootCount++;
                potBoundFillCount++;
            }
        }
        if (potBoundCount > 0)
            Debug.Log($"[GRoot] Pot-bound terminals={potBoundCount} innerFill={potBoundFillCount} year={GameManager.year}");

        // Back-budding: nodes whose tip ancestry was trimmed get a boosted chance
        // to sprout a new lateral from dormant axillary buds.
        // Snapshot allNodes first — CreateNode appends to allNodes during iteration.
        int backBudCount = 0;
        var backBudCandidates = new List<TreeNode>(allNodes);
        foreach (var node in backBudCandidates)
        {
            if (!node.backBudStimulated || node.isTrimmed || node.isRoot) continue;
            node.backBudStimulated = false;  // consume — only fires once per trim event

            if (currentBranchCount >= maxBranchNodes) continue;  // hard cap

            bool belowCap = node.depth < SeasonDepthCap && node.depth < CutPointDepthCap(node);
            if (!belowCap) continue;

            float chance = backBudBaseChance * backBudActivationBoost * vigorFactor
                           * Mathf.Pow(branchChanceDepthDecay, node.depth);
            if (Random.value < chance)
            {
                float chordLen = branchSegmentLength * Mathf.Pow(segmentLengthDecay, node.depth + 1);
                if (node.refinementLevel > 0f)
                    chordLen *= Mathf.Pow(refinementTaper, node.refinementLevel);
                float segLen   = branchSubdivisions > 1 ? chordLen / branchSubdivisions : chordLen;
                segLen = Mathf.Max(segLen, minSegmentLength) * Mathf.Max(0.1f, 1f - apicalDominance);
                var lat = CreateNode(node.tipPosition, LateralDirection(node), terminalRadius, segLen, node);
                lat.isRoot = false;
                lat.refinementLevel = Mathf.Min(node.refinementLevel + refinementOnBackBud, refinementCap);
                if (branchSubdivisions > 1) lat.subdivisionsLeft = branchSubdivisions - 1;
                currentBranchCount++;
                backBudCount++;
            }
        }
        if (backBudCount > 0)
            Debug.Log($"[Bud] Back-buds activated={backBudCount} year={GameManager.year}");

        // Old-wood budding: dormant axillary buds on interior junction nodes can break
        // spontaneously each spring without requiring a trim event. Rate is low on most trees;
        // higher on Japanese maple and other freely back-budding species.
        if (oldWoodBudChance > 0f)
        {
            int oldWoodCount = 0;
            var junctionCandidates = new List<TreeNode>(allNodes);
            foreach (var node in junctionCandidates)
            {
                if (node.isTrimmed || node.isRoot || node.isTerminal) continue;
                // Skip sub-segment junctions — not real branching forks
                bool isSubJunction = node.children.Count == 1 && node.children[0].depth == node.depth;
                if (isSubJunction) continue;

                if (currentBranchCount >= maxBranchNodes) break;  // hard cap

                bool belowCap = node.depth < SeasonDepthCap && node.depth < CutPointDepthCap(node);
                if (!belowCap) continue;

                if (Random.value >= oldWoodBudChance * vigorFactor) continue;

                float chordLen = branchSegmentLength * Mathf.Pow(segmentLengthDecay, node.depth + 1);
                if (node.refinementLevel > 0f)
                    chordLen *= Mathf.Pow(refinementTaper, node.refinementLevel);
                float segLen   = branchSubdivisions > 1 ? chordLen / branchSubdivisions : chordLen;
                segLen = Mathf.Max(segLen, minSegmentLength) * Mathf.Max(0.1f, 1f - apicalDominance);
                var lat = CreateNode(node.tipPosition, LateralDirection(node), terminalRadius, segLen, node);
                lat.isRoot = false;
                if (branchSubdivisions > 1) lat.subdivisionsLeft = branchSubdivisions - 1;
                currentBranchCount++;
                oldWoodCount++;
            }
            if (oldWoodCount > 0)
                Debug.Log($"[Bud] Old-wood buds broke={oldWoodCount} year={GameManager.year}");
        }

        // Wound aging: drain health, scale wound visuals, heal over time
        foreach (var node in allNodes)
        {
            if (!node.hasWound) continue;

            node.woundAge++;

            if (!node.pasteApplied)
                ApplyDamage(node, DamageType.WoundDrain, woundDrainRate);

            float seasonsToHeal = Mathf.Max(1f, node.woundRadius * seasonsToHealPerUnit);
            float remaining     = 1f - node.woundAge / seasonsToHeal;

            if (remaining <= 0f)
            {
                node.hasWound = false;
                if (woundObjects.TryGetValue(node.id, out var wGo))
                {
                    Destroy(wGo);
                    woundObjects.Remove(node.id);
                }
            }
            else if (woundObjects.TryGetValue(node.id, out var wGo) && wGo != null)
            {
                wGo.transform.localScale = Vector3.one * remaining;
            }
        }

        // Trim trauma recovery: all damaged non-root nodes heal a small amount each spring.
        // Wound drain (woundDrainRate = 0.05) slightly exceeds this (0.04) so unprotected
        // wounds still net-worsen, while paste-protected and trauma-only nodes recover cleanly.
        float recoveryThisSeason = trimTraumaRecoveryPerSeason * treeEnergy;
        if (recoveryThisSeason > 0f)
        {
            foreach (var node in allNodes)
            {
                if (node.isRoot || node.isTrimmed || node.health >= 1f) continue;
                node.health = Mathf.Min(1f, node.health + recoveryThisSeason);
            }
        }

        // Per-branch vigor update:
        //   1. Apical nudge — shallow/apex nodes accumulate vigor naturally each season
        //      (simulates the hormonal advantage of the apex and outer tips).
        //   2. Decay toward 1.0 — without trimming, most nodes regress to neutral over time.
        //   3. Clamp to [vigorMin, vigorMax].
        foreach (var node in allNodes)
        {
            if (node.isRoot || node.isTrimmed) continue;
            float depthFactor = node.depth == 0 ? 1f : 1f / node.depth;
            node.branchVigor += apicalVigorBonus * depthFactor;
            node.branchVigor  = Mathf.Lerp(node.branchVigor, 1f, vigorDecayRate);
            node.branchVigor  = Mathf.Clamp(node.branchVigor, vigorMin, vigorMax);
        }

        if (terminals.Count > 0 || fillCount > 0 || backBudCount > 0)
        {
            RecalculateRadii(root);
            meshBuilder.SetDirty();
        }

        UpdateAirLayers();
        RecalculateRootHealthScore();
    }

    // Year Simulation (debug keys 1-9)

    /// <summary>
    /// Instantly simulates one full year of growth with no animation.
    /// </summary>
    public void SimulateYear()
    {
        if (root == null) return;

        GameManager.year++;
        lastGrownYear = GameManager.year;
        GameManager.Instance.TextCallFunction();

        foreach (var node in allNodes)
        {
            if (!node.isTrimCutPoint) continue;
            node.regrowthSeasonCount++;
            if (node.trimCutDepth + node.regrowthSeasonCount * depthsPerYear >= SeasonDepthCap)
                node.isTrimCutPoint = false;
        }

        foreach (var node in allNodes)
        {
            if (node.isGrowing && !node.isTrimmed)
            {
                node.length    = node.targetLength;
                node.isGrowing = false;
            }
        }

        var terminals = new List<TreeNode>();
        foreach (var node in allNodes)
        {
            bool belowCap = node.isRoot
                ? node.depth < maxRootDepth
                : node.depth < SeasonDepthCap && node.depth < CutPointDepthCap(node);

            if (!node.isTrimmed && node.isTerminal && !node.isGrowing && belowCap)
                terminals.Add(node);
        }
        foreach (var terminal in terminals)
            SpawnChildren(terminal);

        bool anyGrowing = true;
        while (anyGrowing)
        {
            anyGrowing = false;
            var growing = new List<TreeNode>();
            foreach (var node in allNodes)
                if (node.isGrowing && !node.isTrimmed) growing.Add(node);

            foreach (var node in growing)
            {
                bool belowCap = node.isRoot
                    ? node.depth < maxRootDepth
                    : node.depth < SeasonDepthCap && node.depth < CutPointDepthCap(node);

                anyGrowing     = true;
                node.length    = node.targetLength;
                node.isGrowing = false;
                if (belowCap) SpawnChildren(node);
            }
        }

        RecalculateRadii(root);
        meshBuilder.SetDirty();
    }

    // Initialisation

    void InitTree()
    {
        foreach (var go in budObjects.Values)
            if (go != null) Destroy(go);
        budObjects.Clear();

        foreach (var go in lateralBudObjects)
            if (go != null) Destroy(go);
        lateralBudObjects.Clear();

        foreach (var go in woundObjects.Values)
            if (go != null) Destroy(go);
        woundObjects.Clear();

        allNodes.Clear();
        nextId     = 0;
        startYear  = GameManager.year;
        startMonth = GameManager.month;

        // Create the seed visual -- an elongated sphere at the soil surface.
        // It disappears once the sprout grows past seedHideLength.
        if (seedObject != null) Destroy(seedObject);
        seedObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        seedObject.name = "_Seed";
        seedObject.transform.SetParent(transform, false);
        seedObject.transform.localPosition = Vector3.zero;
        seedObject.transform.localScale    = new Vector3(0.06f, 0.10f, 0.06f);
        // Remove the collider so it doesn't interfere with tree raycasts
        Destroy(seedObject.GetComponent<Collider>());
        // Give it a warm brown seed colour
        var seedRenderer = seedObject.GetComponent<Renderer>();
        if (seedRenderer != null)
        {
            seedRenderer.material       = new Material(Shader.Find("Standard"));
            seedRenderer.material.color = new Color(0.45f, 0.28f, 0.10f);
        }

        // The first trunk node starts at zero length and grows upward.
        // SpawnChildren will add (trunkSubdivisions - 1) more depth-0 segments
        // before allowing real branching, giving several individually wireable sections.
        float trunkSegLen = branchSegmentLength / Mathf.Max(1, trunkSubdivisions);
        root           = new TreeNode(nextId++, 0, Vector3.zero, Vector3.up,
                                      terminalRadius, trunkSegLen, null);
        root.isGrowing = true;
        allNodes.Add(root);

        // Sprout initial root strands evenly around the seed.
        // Roots also start at zero length and grow during BranchGrow seasons.
        int roots = Mathf.Max(0, initialRootCount);
        for (int i = 0; i < roots; i++)
        {
            float   angle   = (float)i / roots * Mathf.PI * 2f;
            Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 dir     = (outward + Vector3.down * rootInitialPitch).normalized;
            float   len     = Mathf.Max(rootSegmentLength * rootSegmentLengthDecay, 0.3f);
            var r           = CreateNode(root.worldPosition, dir, rootTerminalRadius, len, root);
            r.isRoot        = true;
        }

        Debug.Log($"[Tree] InitTree (seed) year={GameManager.year} | trunk growing | initialRoots={roots}");

        RecalculateRadii(root);
        meshBuilder.SetDirty();
    }

    // Root Planting

    /// <summary>
    /// Plants a new root strand from the base of the trunk.
    /// Called by TreeInteraction when the player clicks the planting surface in RootPrune mode.
    /// </summary>
    public void PlantRoot(Vector3 directionLocal)
    {
        if (root == null) return;

        Vector3 dir = (directionLocal + Vector3.down * rootInitialPitch).normalized;

        float len = Mathf.Max(rootSegmentLength * rootSegmentLengthDecay, 0.3f);

        var newRoot = CreateNode(root.worldPosition, dir, rootTerminalRadius, len, root);
        newRoot.isRoot = true;

        int totalRoots = 0;
        foreach (var n in allNodes) if (n.isRoot) totalRoots++;
        Debug.Log($"[Root] PlantRoot id={newRoot.id} | dir={dir} | len={len:F2} | totalRootNodes={totalRoots}");

        RecalculateRadii(root);
        meshBuilder.SetDirty();
    }

    // ── Air Layering ──────────────────────────────────────────────────────────

    /// <summary>
    /// Places an air layer on a trunk node. The wrap is positioned at the node's
    /// tip and tracked in <see cref="airLayers"/>. After <see cref="airLayerSeasonsToRoot"/>
    /// growing seasons <see cref="AirLayerData.rootsSpawned"/> becomes true;
    /// the player then clicks again to call <see cref="UnwrapAirLayer"/>.
    /// </summary>
    public void PlaceAirLayer(TreeNode node)
    {
        if (node == null || node.isRoot) return;

        // Disallow duplicate layers on the same node.
        foreach (var l in airLayers)
            if (l.node == node) return;

        var layer = new AirLayerData { node = node };

        if (airLayerWrapPrefab != null)
        {
            layer.wrapObject = Instantiate(airLayerWrapPrefab, transform);
        }
        else
        {
            // Placeholder: teal cylinder wrapping the branch at the layer site.
            layer.wrapObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            layer.wrapObject.transform.SetParent(transform, false);
            var mat = new Material(Shader.Find("Standard"));
            mat.color = new Color(0f, 0.75f, 0.75f);
            layer.wrapObject.GetComponent<Renderer>().material = mat;
        }

        SetAirLayerWrapTransform(layer);
        airLayers.Add(layer);
        Debug.Log($"[AirLayer] PlaceAirLayer node={node.id} depth={node.depth}");
    }

    /// <summary>
    /// Spawns air-layer roots radially from the layer node and removes the wrap.
    /// Only call when <see cref="AirLayerData.rootsSpawned"/> is true.
    /// </summary>
    public void UnwrapAirLayer(AirLayerData layer)
    {
        if (layer == null)          { Debug.LogWarning("[AirLayer] UnwrapAirLayer called with null layer"); return; }
        if (!layer.rootsSpawned)    { Debug.LogWarning("[AirLayer] UnwrapAirLayer called but rootsSpawned=false"); return; }

        float spawnRadius = Mathf.Max(layer.node.radius * airLayerRootRadiusMultiplier, terminalRadius);
        float spawnLength = Mathf.Max(airLayerRootTargetLength, 0.1f);

        Debug.Log($"[AirLayer] UnwrapAirLayer firing — node={layer.node.id} spawnRadius={spawnRadius:F4} spawnLength={spawnLength:F3} segments={airLayerRootSegments} nodeRadius={layer.node.radius:F4}");

        float angleStep = 360f / airLayerRootCount;
        for (int i = 0; i < airLayerRootCount; i++)
        {
            float   angle  = i * angleStep * Mathf.Deg2Rad;
            Vector3 radial = new Vector3(Mathf.Cos(angle), -0.15f, Mathf.Sin(angle)).normalized;

            // Spawn a chain of segments per strand, each a child of the previous.
            // Start on the trunk's cylindrical surface (not its center axis) so the
            // first segment doesn't have to travel through the bark to become visible.
            Vector3  radialXZ = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            TreeNode prev     = layer.node;
            Vector3  prevTip  = layer.node.tipPosition + radialXZ * layer.node.radius;
            float    segRadius = spawnRadius;
            for (int s = 0; s < airLayerRootSegments; s++)
            {
                // Slightly vary direction each segment so strands curve naturally.
                Vector3 segDir = (radial + Random.insideUnitSphere * 0.15f).normalized;
                var seg = CreateNode(prevTip, segDir, segRadius, spawnLength, prev);
                seg.isRoot         = true;
                seg.isAirLayerRoot = true;
                seg.radius         = segRadius;
                seg.minRadius      = segRadius;
                seg.length         = spawnLength * 0.4f;   // start short so they visibly grow
                prev    = seg;
                prevTip = seg.tipPosition;
                segRadius *= 0.8f;                  // taper along the strand
            }
        }

        if (layer.wrapObject != null)
            Destroy(layer.wrapObject);

        airLayers.Remove(layer);

        RecalculateRadii(root);
        meshBuilder.SetDirty();
        Debug.Log($"[AirLayer] UnwrapAirLayer done — spawned {airLayerRootCount} air-layer roots");
    }

    /// <summary>
    /// Positions and scales the wrap cylinder to match the current node radius and direction.
    /// Unity's Cylinder primitive is 2 units tall, 1 unit diameter at scale (1,1,1).
    /// We orient the cylinder's Y-axis along growDirection and scale it to sit snugly
    /// outside the branch surface.
    /// </summary>
    void SetAirLayerWrapTransform(AirLayerData layer)
    {
        if (layer.wrapObject == null) return;
        var node = layer.node;
        float wrapRadius = Mathf.Max(node.radius * 4f, 0.04f);
        float wrapHeight = Mathf.Max(node.radius * 4f, 0.04f);
        // Center the band along the segment.
        layer.wrapObject.transform.localPosition = node.worldPosition + node.growDirection * (node.length * 0.5f);
        layer.wrapObject.transform.localRotation = Quaternion.FromToRotation(Vector3.up, node.growDirection);
        // Cylinder: diameter = scale.x, height = scale.y * 2
        layer.wrapObject.transform.localScale    = new Vector3(wrapRadius * 2f, wrapHeight * 0.5f, wrapRadius * 2f);
    }

    /// <summary>
    /// Advances all active air layers by one growing season.
    /// Called from <see cref="StartNewGrowingSeason"/>.
    /// </summary>
    void UpdateAirLayers()
    {
        foreach (var layer in airLayers)
        {
            if (layer.rootsSpawned) continue;

            layer.seasonsElapsed++;

            // Keep wrap sized and positioned as the trunk thickens.
            SetAirLayerWrapTransform(layer);

            if (layer.seasonsElapsed >= airLayerSeasonsToRoot)
            {
                layer.rootsSpawned = true;
                // Turn the wrap gold to signal roots are ready to emerge.
                if (layer.wrapObject != null)
                {
                    var rend = layer.wrapObject.GetComponent<Renderer>();
                    if (rend != null) rend.material.color = new Color(0.85f, 0.65f, 0.0f);
                }
                Debug.Log($"[AirLayer] Roots ready — node={layer.node.id} after {layer.seasonsElapsed} seasons. Click the gold wrap to unwrap.");
            }
        }
    }

    /// <summary>
    /// Sets the surface the tree is resting on and lowers the tree onto it.
    /// Called by TreeInteraction when the player right-clicks a surface in RootPrune mode.
    /// The tree's resting Y is updated to the surface contact point, and subsequent root
    /// growth near the surface will hug it.
    /// </summary>
    public void SetPlantingSurface(Vector3 worldSurfacePoint, Vector3 worldSurfaceNormal)
    {
        plantingSurfacePoint = worldSurfacePoint;
        plantingNormal       = worldSurfaceNormal.normalized;

        // Update the resting Y so the tree lowers to this surface height.
        initY = worldSurfacePoint.y;

        // Lower the tree onto the surface.
        liftTarget = 0f;

        Debug.Log($"[Root] SetPlantingSurface point={worldSurfacePoint} normal={worldSurfaceNormal}");
    }

    // Node Factory

    public TreeNode CreateNode(Vector3 position, Vector3 direction, float radius, float targetLength, TreeNode parent)
    {
        int depth = parent == null ? 0 : parent.depth + 1;
        var node = new TreeNode(nextId++, depth, position, direction, radius, targetLength, parent)
        {
            birthYear        = GameManager.year,
            refinementLevel  = parent?.refinementLevel ?? 0f,
            branchVigor      = parent?.branchVigor ?? 1f,
        };

        // New nodes start thin and ramp toward their target radius as they grow,
        // distributing trunk thickening across the season instead of spiking at spawn.
        // Parent == null is the tree root node itself; it starts visible immediately.
        if (parent != null)
            node.radius = 0f;

        if (parent != null)
            parent.children.Add(node);

        allNodes.Add(node);
        return node;
    }

    // Branching

    void SpawnChildren(TreeNode node)
    {
        // Trunk elongation: depth-0 non-root nodes keep adding depth-0 segments
        // until we reach trunkSubdivisions. Only then does real branching begin.
        // This gives the player several independently-wireable trunk segments.
        if (!node.isRoot && node.depth == 0)
        {
            int trunkCount = 0;
            foreach (var n in allNodes)
                if (!n.isRoot && n.depth == 0) trunkCount++;

            if (trunkCount < trunkSubdivisions)
            {
                float segLen   = branchSegmentLength / trunkSubdivisions;
                var trunkSeg = new TreeNode(nextId++, 0, node.tipPosition,
                                             ContinuationDirection(node),
                                             terminalRadius, segLen, node)
                {
                    birthYear  = GameManager.year,
                    isGrowing  = true,
                };
                node.children.Add(trunkSeg);
                allNodes.Add(trunkSeg);
                Debug.Log($"[Tree] Trunk segment {trunkCount + 1}/{trunkSubdivisions} id={trunkSeg.id}");
                return;
            }
            // All trunk segments grown -- fall through to first real branch
        }

        // Branch subdivision: non-root nodes grow N same-depth sub-segments before branching.
        // Same depth as parent means sub-segments don't consume the season depth cap.
        if (!node.isRoot && node.subdivisionsLeft > 0)
        {
            var sub = new TreeNode(nextId++, node.depth, node.tipPosition,
                                   ContinuationDirection(node), terminalRadius, node.targetLength, node)
            {
                birthYear        = GameManager.year,
                subdivisionsLeft = node.subdivisionsLeft - 1,
                isGrowing        = true,
            };
            node.children.Add(sub);
            allNodes.Add(sub);
            return;
        }

        float baseSegLen  = node.isRoot ? rootSegmentLength : branchSegmentLength;
        float decay       = node.isRoot ? rootSegmentLengthDecay : segmentLengthDecay;
        float childLength = baseSegLen * Mathf.Pow(decay, node.depth + 1);

        // Each new branch chord is divided into branchSubdivisions segments of equal length.
        // Clamp per-segment so tip segments stay at a wireable minimum length regardless of N.
        float segLength  = (!node.isRoot && branchSubdivisions > 1) ? childLength / branchSubdivisions : childLength;
        segLength        = Mathf.Max(segLength, node.isRoot ? 0.3f : minSegmentLength);
        float nodeRadius = node.isRoot ? rootTerminalRadius : terminalRadius;

        // Root soft spread cap: laterals scale to zero at the target radius;
        // continuation itself stops beyond 1.3× the target radius.
        // Hard node cap enforced here as well as in StartNewGrowingSeason.
        if (node.isRoot)
        {
            int rootCount = 0;
            foreach (var n in allNodes) if (n.isRoot) rootCount++;
            if (rootCount >= maxTotalRootNodes) return;

            float distRatio   = RootDistRatio(node);
            bool  isIshitsuki = isIshitsukiMode;
            if (!isIshitsuki && distRatio >= 1.3f) return;  // hard outer boundary — no further growth
            if (isIshitsuki)
            {
                Vector3 tipW = transform.TransformPoint(node.tipPosition);
                if (tipW.y <= plantingSurfacePoint.y) return;
            }

            var rootCont = CreateNode(node.tipPosition, ContinuationDirection(node), nodeRadius, segLength, node);
            rootCont.isRoot = true;
            rootCount++;

            float lateralScale  = isIshitsuki ? 0f : Mathf.Clamp01(1f - distRatio);
            float lateralChance = rootLateralChance * Mathf.Pow(rootLateralDepthDecay, node.depth) * lateralScale;
            if (rootCount < maxTotalRootNodes && Random.value < lateralChance)
            {
                var lat = CreateNode(node.tipPosition, LateralDirection(node), nodeRadius, segLength * 0.85f, node);
                lat.isRoot = true;
                Debug.Log($"[GRoot] SpawnChildren lateral | node={node.id} depth={node.depth} distRatio={distRatio:F2} -> lat id={lat.id}");
            }
            return;
        }

        if (budType == BudType.Opposite)
        {
            var (dirA, dirB) = OppositeForkDirections(node);
            var forkA = CreateNode(node.tipPosition, dirA, nodeRadius, segLength, node);
            forkA.isRoot = false;
            if (branchSubdivisions > 1) forkA.subdivisionsLeft = branchSubdivisions - 1;
            var forkB = CreateNode(node.tipPosition, dirB, nodeRadius, segLength, node);
            forkB.isRoot = false;
            if (branchSubdivisions > 1) forkB.subdivisionsLeft = branchSubdivisions - 1;
            GameManager.branches++;
        }
        else
        {
            var cont = CreateNode(node.tipPosition, ContinuationDirection(node), nodeRadius, segLength, node);
            cont.isRoot = false;
            if (branchSubdivisions > 1)
                cont.subdivisionsLeft = branchSubdivisions - 1;

            float lateralChanceBranch = baseBranchChance * Mathf.Pow(branchChanceDepthDecay, node.depth);
            if (Random.value < lateralChanceBranch)
            {
                float latLength = segLength * 0.85f * Mathf.Max(0.1f, 1f - apicalDominance);
                var lat = CreateNode(node.tipPosition, LateralDirection(node), nodeRadius, latLength, node);
                lat.isRoot = false;
                if (branchSubdivisions > 1)
                    lat.subdivisionsLeft = branchSubdivisions - 1;
                GameManager.branches++;
            }
        }
    }

    // Root Spread Helpers

    /// <summary>
    /// Returns the highest tipPosition.y among all non-root branch nodes.
    /// Used to compute the target root spread radius.
    /// </summary>
    float CalculateTreeHeight()
    {
        float h = 0.5f;  // minimum so spread radius is never zero
        foreach (var node in allNodes)
            if (!node.isRoot && node.tipPosition.y > h)
                h = node.tipPosition.y;
        return h;
    }

    /// <summary>
    /// Scores the current root health (surface root flare) 0–100 and updates
    /// RootHealthScore and RootHealthSectorCoverage (8 directional sectors).
    /// Considers shallow trunk roots: depth 1–rootHealthMaxDepth, Y > -rootHealthSurfaceDepth.
    /// Components: angular coverage (50%), girth thickness (30%), radial balance (20%).
    /// </summary>
    public void RecalculateRootHealthScore()
    {
        const int sectors = 8;
        float[] sectorRadius = new float[sectors];
        float   totalRadius  = 0f;
        int     count        = 0;
        Vector2 com          = Vector2.zero;

        foreach (var node in allNodes)
        {
            if (!node.isRoot || node.isTrimmed) continue;
            if (node.depth < 1 || node.depth > rootHealthMaxDepth) continue;
            if (node.tipPosition.y < -rootHealthSurfaceDepth) continue;

            Vector3 tip   = node.tipPosition;
            float   angle = Mathf.Atan2(tip.z, tip.x);            // –π … +π
            float   t     = (angle + Mathf.PI) / (Mathf.PI * 2f); // 0 … 1
            int     s     = Mathf.Clamp(Mathf.FloorToInt(t * sectors), 0, sectors - 1);

            sectorRadius[s] += node.radius;
            totalRadius      += node.radius;
            com              += new Vector2(tip.x, tip.z) * node.radius;
            count++;
        }

        // Normalise sector coverage for the UI (0 = empty, 1 = best-covered sector)
        float maxSector = 0f;
        for (int i = 0; i < sectors; i++) if (sectorRadius[i] > maxSector) maxSector = sectorRadius[i];
        RootHealthSectorCoverage = new float[sectors];
        if (maxSector > 0f)
            for (int i = 0; i < sectors; i++) RootHealthSectorCoverage[i] = sectorRadius[i] / maxSector;

        if (count == 0) { RootHealthScore = 0f; return; }

        // Angular coverage: fraction of the 8 sectors that have any roots
        int coveredSectors = 0;
        for (int i = 0; i < sectors; i++) if (sectorRadius[i] > 0f) coveredSectors++;
        float angularScore = (float)coveredSectors / sectors;

        // Girth: average root radius vs the ideal target radius
        float girthScore = Mathf.Clamp01(totalRadius / count / rootHealthTargetRadius);

        // Balance: centre of mass close to the trunk origin
        com /= totalRadius;
        float balanceScore = Mathf.Clamp01(1f - com.magnitude / rootHealthBalanceRadius);

        RootHealthScore = (angularScore * 0.5f + girthScore * 0.3f + balanceScore * 0.2f) * 100f;
        Debug.Log($"[RootHealth] score={RootHealthScore:F1} angular={angularScore:F2} girth={girthScore:F2} balance={balanceScore:F2} sectors={coveredSectors}/8 nodes={count}");
    }

    /// <summary>
    /// Returns the horizontal distance ratio of a root node's tip to the spread radius.
    /// 0 = at trunk, 1 = at target spread radius, >1 = beyond it.
    /// </summary>
    float RootDistRatio(TreeNode node)
    {
        if (rootAreaTransform != null)
        {
            // Box mode: 0=center, 1=at wall, >1=outside.
            // InverseTransformPoint gives coords in Root Area local space where
            // the box extents are [-0.5, 0.5] on each axis.
            Vector3 worldTip  = transform.TransformPoint(node.tipPosition);
            Vector3 areaLocal = rootAreaTransform.InverseTransformPoint(worldTip);
            float xRatio = Mathf.Abs(areaLocal.x) * 2f;
            float zRatio = Mathf.Abs(areaLocal.z) * 2f;
            // Y: check both floor and ceiling — roots must stay inside the tray height
            float yRatio = Mathf.Abs(areaLocal.y) * 2f;
            return Mathf.Max(xRatio, zRatio, yRatio);
        }
        // Legacy radial fallback
        float spreadRadius = cachedTreeHeight * rootSpreadMultiplier;
        if (spreadRadius <= 0f) return 0f;
        Vector3 tip = node.tipPosition;
        float horizDist = Mathf.Sqrt(tip.x * tip.x + tip.z * tip.z);
        return horizDist / spreadRadius;
    }

    /// <summary>
    /// When a Root Area box is assigned, deflects a root direction away from walls
    /// the root is approaching, so roots run along the inside of the box rather
    /// than stopping dead or punching through.
    /// treeLocalDir and treeLocalPos are in TreeSkeleton local space.
    /// </summary>
    Vector3 DeflectFromRootAreaWalls(Vector3 treeLocalDir, Vector3 treeLocalPos)
    {
        if (rootAreaTransform == null) return treeLocalDir;

        Vector3 worldPos  = transform.TransformPoint(treeLocalPos);
        Vector3 worldDir  = transform.TransformDirection(treeLocalDir);
        Vector3 areaLocal = rootAreaTransform.InverseTransformPoint(worldPos);

        // Margin in normalised box coords (0.5 = half-extent).
        // Within this distance of a wall, start blending toward the wall surface.
        const float margin = 0.15f;

        Vector3 wallNormal = Vector3.zero;
        if (areaLocal.x >  0.5f - margin) wallNormal += rootAreaTransform.right    *  Mathf.InverseLerp(0.5f - margin,  0.5f,  areaLocal.x);
        if (areaLocal.x < -0.5f + margin) wallNormal -= rootAreaTransform.right    *  Mathf.InverseLerp(-0.5f + margin, -0.5f, areaLocal.x);
        if (areaLocal.z >  0.5f - margin) wallNormal += rootAreaTransform.forward  *  Mathf.InverseLerp(0.5f - margin,  0.5f,  areaLocal.z);
        if (areaLocal.z < -0.5f + margin) wallNormal -= rootAreaTransform.forward  *  Mathf.InverseLerp(-0.5f + margin, -0.5f, areaLocal.z);
        // Y floor and ceiling: roots approaching either face redirect horizontally
        if (areaLocal.y < -0.5f + margin) wallNormal -= rootAreaTransform.up * Mathf.InverseLerp(-0.5f + margin, -0.5f, areaLocal.y);
        if (areaLocal.y >  0.5f - margin) wallNormal += rootAreaTransform.up * Mathf.InverseLerp( 0.5f - margin,  0.5f, areaLocal.y);

        if (wallNormal.sqrMagnitude > 0.001f)
        {
            wallNormal.Normalize();
            // Project dir onto the wall surface so the root runs along it
            Vector3 along = Vector3.ProjectOnPlane(worldDir, wallNormal);
            if (along.sqrMagnitude > 0.001f)
                worldDir = along.normalized;
        }

        Vector3 result = transform.InverseTransformDirection(worldDir);
        return result.sqrMagnitude > 0.001f ? result.normalized : treeLocalDir;
    }

    // Direction Helpers

    /// <summary>
    /// Continuation direction: inertia + phototropism upward for branches,
    /// inertia + gravity downward for roots. Roots near the planting surface
    /// have their direction deflected to hug the surface.
    /// </summary>
    Vector3 ContinuationDirection(TreeNode node)
    {
        Vector3 rand = Random.insideUnitSphere * randomWeight;

        // Air-layer roots grow downward (gravitropism / anti-phototropism).
        // Bypass normal root surface-snap logic — they hang freely from the trunk.
        if (node.isAirLayerRoot)
        {
            Vector3 airInertia = (node.growDirection * inertiaWeight + rand).normalized;
            return Vector3.Slerp(airInertia, Vector3.down, 0.7f).normalized;
        }

        if (node.isRoot)
        {
            // Outward radial direction from trunk base, projected flat.
            // This is the primary bias — keeps roots spreading wide near the surface.
            Vector3 trunkBase = root != null ? root.worldPosition : Vector3.zero;
            Vector3 radial    = node.worldPosition - trunkBase;
            radial.y = 0f;
            if (radial.sqrMagnitude < 0.001f)
                radial = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f));
            radial = radial.normalized;

            // Ishitsuki: suppress radial spread on the rock face — roots flow DOWN, not outward.
            bool isIshitsuki = isIshitsukiMode;
            Vector3 dir = (node.growDirection * inertiaWeight
                          + (isIshitsuki ? Vector3.zero : radial * rootRadialWeight)
                          + Vector3.down      * rootGravityWeight
                          + rand).normalized;

            Vector3 worldTip = transform.TransformPoint(node.tipPosition);
            bool nearRock = false;

            // ── Rock surface deflection (Ishitsuki) ───────────────────────────
            // Note: all pre-grown Ishitsuki cables are handled by PreGrowRootsToSoil.
            // This block only fires for any organic root growth that slips through
            // (e.g. auto-planted trunk roots before their first pre-grow pass).
            // Use a raycast-based closest-point approximation instead of
            // Physics.ClosestPoint, which requires a convex MeshCollider.
            if (rockCollider != null)
            {
                float effectiveRadius = rockInfluenceRadius * 2f;

                // Approximate closest surface point: cast a ray from worldTip toward
                // the rock centre; the hit point is on the surface facing us.
                Vector3 rockCenter = rockCollider.bounds.center;
                Vector3 toCenter   = (rockCenter - worldTip).normalized;
                Vector3 closestPt  = rockCenter; // fallback
                if (rockCollider.Raycast(new Ray(worldTip, toCenter), out RaycastHit cpHit, effectiveRadius * 3f))
                    closestPt = cpHit.point;
                else
                {
                    // Also try from outside in (root may be inside/near the rock)
                    Vector3 outside = worldTip - toCenter * effectiveRadius * 2f;
                    if (rockCollider.Raycast(new Ray(outside, toCenter), out RaycastHit cpHit2, effectiveRadius * 4f))
                        closestPt = cpHit2.point;
                }
                float distToRock = Vector3.Distance(worldTip, closestPt);

                if (distToRock < effectiveRadius)
                {
                    nearRock = true;

                    // Get surface normal via raycast from outside inward (world space).
                    Vector3 outward    = closestPt - rockCenter;
                    if (outward.sqrMagnitude < 0.001f) outward = Vector3.up;
                    outward.Normalize();

                    Vector3 surfaceNormal = outward;
                    Ray normalRay = new Ray(closestPt + outward * 0.5f, -outward);
                    if (rockCollider.Raycast(normalRay, out RaycastHit normalHit, 1f))
                        surfaceNormal = normalHit.normal;

                    // dir is in local space — convert to world for the projection.
                    Vector3 worldDir = transform.TransformDirection(dir);

                    // Project onto rock surface, fall back to pure-down if tangent is zero.
                    Vector3 surfaceDir = Vector3.ProjectOnPlane(worldDir, surfaceNormal);
                    if (surfaceDir.sqrMagnitude < 0.001f)
                        surfaceDir = Vector3.ProjectOnPlane(Vector3.down, surfaceNormal);

                    // On near-horizontal surfaces (top of rock) gravity is perpendicular
                    // to the plane, so adding it then re-projecting would kill it entirely.
                    // Instead: push radially outward toward the rock edge so the root flows
                    // off the top and down the side, THEN let gravity do its work on the face.
                    float upDot = Vector3.Dot(surfaceNormal, Vector3.up);
                    if (upDot > 0.5f)
                    {
                        Vector3 radialOut = worldTip - rockCollider.bounds.center;
                        radialOut.y = 0f;
                        if (radialOut.sqrMagnitude > 0.001f)
                        {
                            Vector3 edgePush = Vector3.ProjectOnPlane(radialOut.normalized, surfaceNormal);
                            if (edgePush.sqrMagnitude > 0.001f)
                                surfaceDir = (surfaceDir.normalized + edgePush.normalized * upDot).normalized;
                        }
                    }

                    // Gravity bias — NOT re-projected, so it works on all surface angles.
                    // On vertical faces this pulls strongly downward; on horizontal faces
                    // the slight inward lean is harmless (segments are short).
                    surfaceDir = (surfaceDir.normalized + Vector3.down * rootGravityWeight * 20f).normalized;

                    // Full adhesion regardless of height — real Ishitsuki roots grip the
                    // rock all the way from crown to soil, never float off mid-face.
                    // Blend: 1.0 on surface → 0.6 at far edge of influence.
                    float blend = Mathf.Lerp(0.6f, 1.0f,
                        1f - Mathf.Clamp01(distToRock / effectiveRadius));
                    dir = transform.InverseTransformDirection(
                        Vector3.Slerp(worldDir, surfaceDir, blend).normalized);
                }
            }

            // Roots in free air past the rock edge: fall nearly straight down to reach soil.
            if (!nearRock)
            {
                float heightAboveSoil = transform.TransformPoint(node.tipPosition).y - plantingSurfacePoint.y;
                if (heightAboveSoil > 0.05f)
                {
                    // 0.95 max blend → almost straight down; faster than before so
                    // roots don't hang horizontally past the rock edge.
                    float fallBlend = Mathf.Clamp01(heightAboveSoil / 0.3f) * (isIshitsuki ? 0.95f : 0.85f);
                    Vector3 worldDirFall = transform.TransformDirection(dir);
                    worldDirFall = Vector3.Slerp(worldDirFall, Vector3.down, fallBlend).normalized;
                    dir = transform.InverseTransformDirection(worldDirFall);
                }
            }

            // When near the planting surface, blend toward a surface-tangent direction
            // so roots flow along the soil face instead of going through it.
            Plane surface = new Plane(plantingNormal, plantingSurfacePoint);
            float distToSurface = surface.GetDistanceToPoint(worldTip);

            if (distToSurface >= 0f && distToSurface < rootSurfaceSnapDist)
            {
                Vector3 surfaceDir = Vector3.ProjectOnPlane(dir, plantingNormal);
                if (surfaceDir.sqrMagnitude > 0.001f)
                {
                    float blend = 1f - Mathf.Clamp01(distToSurface / rootSurfaceSnapDist);
                    dir = Vector3.Slerp(dir, surfaceDir.normalized, blend).normalized;
                }
            }

            // Clamp: roots must never grow upward — EXCEPT when near the rock,
            // where they may need to crest an edge to get over the side.
            if (!nearRock && dir.y > 0f)
            {
                dir = Vector3.ProjectOnPlane(dir, Vector3.up);
                if (dir.sqrMagnitude < 0.001f)
                    dir = radial;
                dir.Normalize();
            }

            dir = DeflectFromRootAreaWalls(dir, node.tipPosition);
            return dir;
        }
        // Slerp toward sun so phototropismWeight is a direct blend fraction (0=none, 1=point straight up)
        Vector3 inertiaDir = (node.growDirection * inertiaWeight + rand).normalized;
        return Vector3.Slerp(inertiaDir, SunDirection(), phototropismWeight);
    }

    /// <summary>
    /// Lateral direction for both branches (splay + upward bias) and roots (splay + downward bias).
    /// </summary>
    Vector3 LateralDirection(TreeNode node)
    {
        Vector3 perp = Vector3.Cross(node.growDirection, Random.insideUnitSphere).normalized;
        if (perp.sqrMagnitude < 0.001f)
            perp = Vector3.Cross(node.growDirection, Vector3.right).normalized;

        float   angle = Random.Range(branchAngleMin, branchAngleMax);
        Vector3 dir   = Vector3.Slerp(node.growDirection, perp, angle / 90f);

        if (node.isRoot)
        {
            Vector3 trunkBase  = root != null ? root.worldPosition : Vector3.zero;
            Vector3 rootRadial = node.worldPosition - trunkBase;
            rootRadial.y = 0f;
            if (rootRadial.sqrMagnitude > 0.001f) rootRadial.Normalize();
            Vector3 bias    = rootRadial * rootRadialWeight * 0.5f + Vector3.down * rootGravityWeight * 0.5f;
            Vector3 lateral = (dir + bias).normalized;
            // Clamp: lateral roots must not grow upward
            if (lateral.y > 0f)
            {
                lateral = Vector3.ProjectOnPlane(lateral, Vector3.up);
                if (lateral.sqrMagnitude < 0.001f) lateral = rootRadial;
                lateral.Normalize();
            }
            lateral = DeflectFromRootAreaWalls(lateral, node.tipPosition);
            return lateral;
        }
        // Lateral branches get half the phototropism blend of continuation segments
        return Vector3.Slerp(dir, SunDirection(), phototropismWeight * 0.5f);
    }

    // Keep old name as alias so any external callers don't break.
    Vector3 LateralBranchDirection(TreeNode node) => LateralDirection(node);

    /// <summary>
    /// Returns two symmetric fork directions for Opposite budding.
    /// Both branches diverge equally from the parent's grow direction,
    /// mirrored across a random perpendicular axis.
    /// </summary>
    (Vector3 a, Vector3 b) OppositeForkDirections(TreeNode node)
    {
        Vector3 perp = Vector3.Cross(node.growDirection, Random.insideUnitSphere).normalized;
        if (perp.sqrMagnitude < 0.001f)
            perp = Vector3.Cross(node.growDirection, Vector3.right).normalized;

        float halfAngle = Random.Range(branchAngleMin, branchAngleMax) * 0.5f;
        Vector3 dir1 = Quaternion.AngleAxis( halfAngle, perp) * node.growDirection;
        Vector3 dir2 = Quaternion.AngleAxis(-halfAngle, perp) * node.growDirection;

        // Same phototropism blend as laterals
        dir1 = Vector3.Slerp(dir1, SunDirection(), phototropismWeight * 0.5f).normalized;
        dir2 = Vector3.Slerp(dir2, SunDirection(), phototropismWeight * 0.5f).normalized;
        return (dir1, dir2);
    }

    /// <summary>
    /// Direction toward the sun. Falls back to Vector3.up.
    /// </summary>
    Vector3 SunDirection()
    {
        return Vector3.up;
    }

    // Pipe Model

    /// <summary>
    /// Recalculates all node radii bottom-up using da Vinci's pipe model:
    ///     parent.radius^2 = sum(child.radius^2)
    /// </summary>
    readonly Stopwatch radiiTimer = new Stopwatch();

    public void RecalculateRadii(TreeNode node)
    {
        // Time only the root call so we get one measurement per full traversal
        bool isRootCall = (node == root);
        if (isRootCall) radiiTimer.Restart();

        RecalculateRadiiInternal(node);

        // After the pipe model runs, override air-layer root radii so they track
        // the trunk's growth and never get swallowed. Must run after the main pass
        // so the pipe model doesn't immediately overwrite them.
        if (isRootCall) ScaleAirLayerRootRadii();
        if (isRootCall) ScaleIshitsukiCableRadii();

        if (isRootCall)
        {
            radiiTimer.Stop();
            if (radiiTimer.ElapsedMilliseconds > 0)
                Debug.Log($"[Perf] RecalculateRadii nodes={allNodes.Count} took {radiiTimer.ElapsedMilliseconds}ms");
        }
    }

    /// <summary>
    /// Keeps air-layer root radii proportional to their trunk parent as the tree thickens.
    /// Walks each isAirLayerRoot node up the chain to find the first non-air-layer ancestor
    /// (the trunk node), then sets radius = trunkRadius * multiplier * taper^depth.
    /// </summary>
    /// <summary>
    /// Each frame: re-anchor every air-layer root base to its parent's current tip
    /// so strands don't get swallowed as the trunk node grows longer.
    /// Multiple passes handle chains: pass N propagates corrections N links deep.
    /// </summary>
    void UpdateAirLayerRootPositions()
    {
        if (allNodes == null) return;
        for (int pass = 0; pass < airLayerRootSegments; pass++)
        {
            foreach (var node in allNodes)
            {
                if (!node.isAirLayerRoot || node.parent == null) continue;

                if (node.parent.isAirLayerRoot)
                {
                    // Chain segment: just track parent tip directly.
                    node.worldPosition = node.parent.tipPosition;
                }
                else
                {
                    // First segment: anchor to the cylindrical surface of the trunk node,
                    // not its center, so the root visually emerges from the bark.
                    // Derive the radial direction from the current XZ offset; fall back
                    // to the node's grow direction on the first frame (when offset == 0).
                    Vector3 offset    = node.worldPosition - node.parent.tipPosition;
                    Vector3 radialDir = new Vector3(offset.x, 0f, offset.z);
                    if (radialDir.sqrMagnitude < 0.0001f)
                        radialDir = new Vector3(node.growDirection.x, 0f, node.growDirection.z);
                    if (radialDir.sqrMagnitude < 0.0001f)
                        radialDir = Vector3.right;
                    node.worldPosition = node.parent.tipPosition + radialDir.normalized * node.parent.radius;
                }
            }
        }
    }

    void ScaleAirLayerRootRadii()
    {
        foreach (var node in allNodes)
        {
            if (!node.isAirLayerRoot) continue;

            // Walk up to find the trunk node and how deep in the strand this segment is.
            int      chainDepth = 0;
            TreeNode trunkNode  = node.parent;
            while (trunkNode != null && trunkNode.isAirLayerRoot)
            {
                chainDepth++;
                trunkNode = trunkNode.parent;
            }
            if (trunkNode == null) continue;

            float r = Mathf.Max(
                trunkNode.radius * airLayerRootRadiusMultiplier * Mathf.Pow(0.8f, chainDepth),
                terminalRadius);
            node.radius    = r;
            node.minRadius = r;
        }
    }

    /// <summary>
    /// Scales Ishitsuki pre-grown cable radii proportional to trunk thickness each season.
    /// startNodes (direct root children of root) are set to trunkRadius * multiplier.
    /// Each cable segment tapers toward rootTerminalRadius with distance from the startNode.
    /// Must run after RecalculateRadiiInternal so it overrides the pipe-model floor.
    /// </summary>
    void ScaleIshitsukiCableRadii()
    {
        if (!isIshitsukiMode) return;

        // trunk radius is whatever the pipe model computed for the tree base
        float trunkRadius = root.radius;
        if (trunkRadius <= 0f) trunkRadius = rootTerminalRadius;

        // Scale startNodes (direct root children that own cable chains)
        foreach (var child in root.children)
        {
            if (!child.isRoot || child.isTrainingWire) continue;
            float r = Mathf.Max(trunkRadius * ishitsukiCableRadiusMultiplier, rootTerminalRadius);
            child.radius    = r;
            child.minRadius = r;
        }

        // Scale each pre-grown cable node by its depth in the chain from its startNode
        foreach (var node in allNodes)
        {
            if (!node.isTrainingWire) continue;

            // Walk up to find the startNode (first non-training-wire root ancestor)
            int      chainDepth = 0;
            TreeNode ancestor   = node.parent;
            while (ancestor != null && ancestor.isTrainingWire)
            {
                chainDepth++;
                ancestor = ancestor.parent;
            }

            float r = Mathf.Max(
                trunkRadius * ishitsukiCableRadiusMultiplier * Mathf.Pow(0.82f, chainDepth + 1),
                rootTerminalRadius);
            node.radius    = r;
            node.minRadius = r;
        }
    }

    void RecalculateRadiiInternal(TreeNode node)
    {
        if (node.isTerminal) return;

        float sumOfSquares = 0f;
        foreach (var child in node.children)
        {
            RecalculateRadiiInternal(child);
            // Root children don't contribute to branch pipe-model radii — they would
            // otherwise inflate the trunk as the root system grows exponentially.
            if (!node.isRoot && child.isRoot) continue;
            sumOfSquares += child.radius * child.radius;
        }
        float pipeRadius = Mathf.Sqrt(sumOfSquares);
        node.radius    = Mathf.Max(pipeRadius, node.minRadius);
        node.minRadius = node.radius;
    }

    // Bud System

    /// <summary>
    /// Called at season end (TimeGo). Sets hasBud on all eligible terminal branch nodes
    /// and spawns a bud prefab at each tip position.
    /// </summary>
    void SetBuds()
    {
        int termCount = 0;
        int latCount  = 0;
        foreach (var node in allNodes)
        {
            if (node.isTrimmed || node.isRoot) continue;

            if (node.isTerminal)
            {
                // subdivisionsLeft > 0 means this is a mid-chord sub-segment that was
                // stopped before completing. Resume it in spring instead of treating it
                // as a real tip — SetBuds skips it; StartNewGrowingSeason resumes it.
                if (node.subdivisionsLeft > 0) continue;
                if (node.hasBud) continue;

                node.hasBud = true;
                if (showTerminalBuds && budPrefab != null)
                {
                    var bud = Instantiate(budPrefab, transform);
                    bud.transform.localPosition = node.tipPosition;
                    bud.transform.localRotation = Quaternion.LookRotation(node.growDirection);
                    budObjects[node.id] = bud;
                }
                termCount++;
            }
            else
            {
                // Junction node — spawn a lateral (axillary) bud visual.
                // Skip sub-segment junctions (they're part of a single wireable chord, not real forks).
                bool isSubJunction = node.children.Count == 1 && node.children[0].depth == node.depth;
                if (isSubJunction) continue;

                if (showLateralBuds && lateralBudPrefab != null)
                {
                    var latBud = Instantiate(lateralBudPrefab, transform);
                    latBud.transform.localPosition = node.tipPosition;
                    latBud.transform.localRotation = Quaternion.LookRotation(node.growDirection);
                    lateralBudObjects.Add(latBud);
                    latCount++;
                }
            }
        }
        // Canopy energy: computed from current leaf state (peak summer canopy).
        // Stored on treeEnergy and used as a multiplier next spring.
        var lm = GetComponent<LeafManager>();
        if (lm != null)
            treeEnergy = lm.ComputeTreeEnergy(allNodes, Mathf.Clamp(maxEnergyMultiplier, 1f, 3f));
        Debug.Log($"[Bud] Buds set | terminal={termCount} lateral={latCount} treeEnergy={treeEnergy:F2} year={GameManager.year}");
    }

    // Trimming

    /// <summary>
    /// Pinches the soft growing tip of a terminal node.
    /// Unlike TrimNode, no wound is created and leaves remain on the node.
    /// The tip stops growing; back-buds are stimulated on nearby ancestors.
    /// </summary>
    public void PinchNode(TreeNode node)
    {
        if (node == null || !node.isTerminal || node.isRoot || node.isTrimmed)
        {
            Debug.Log("[Pinch] Ignored: node must be a non-root, non-trimmed terminal.");
            return;
        }

        // Stop growth at current length — soft tissue only, no woody wound
        node.isGrowing = false;
        node.isTrimmed = true;

        // Refinement gain (same rate as a trim cut but scaled by vigor — less effective on strong vigorous growth)
        float refGain = refinementOnTrim / Mathf.Max(0.5f, node.branchVigor);
        node.refinementLevel = Mathf.Min(node.refinementLevel + refGain, refinementCap);

        // Vigor reduction — pinching weakens the tip slightly
        node.branchVigor = Mathf.Max(vigorMin, node.branchVigor * vigorTrimMultiplier);

        // Light health cost — much less than a hard cut (soft tissue only)
        ApplyDamage(node, DamageType.TrimTrauma, trimTraumaDamage * 0.25f);

        // Back-bud stimulation on up to 2 ancestors
        TreeNode ancestor = node.parent;
        for (int i = 0; i < 2 && ancestor != null && ancestor != root; i++)
        {
            ancestor.backBudStimulated = true;
            ancestor = ancestor.parent;
        }

        meshBuilder.SetDirty();
        Debug.Log($"[Pinch] Pinched node={node.id} depth={node.depth} refLevel={node.refinementLevel:F2} vigor={node.branchVigor:F2}");
    }

    /// Removes a node and all its descendants.
    /// </summary>
    public void TrimNode(TreeNode node)
    {
        if (node == root)
        {
            Debug.LogWarning("TreeSkeleton: cannot trim the root node.");
            return;
        }

        TreeNode parent = node.parent;

        // Capture undo state before any modifications
        pendingUndo = CaptureTrimUndoState(node, parent);

        parent?.children.Remove(node);

        var removed = new List<TreeNode>();
        RemoveSubtree(node, removed);

        if (node.isRoot)
            Debug.Log($"[Root] TrimRoot node={node.id} depth={node.depth} | removed={removed.Count} nodes");

        if (parent != null && parent.isTerminal)
        {
            parent.isTrimCutPoint      = true;
            parent.trimCutDepth        = parent.depth;
            parent.regrowthSeasonCount = 0;

            // Vigorous shoots refine slower (more wood per cut = less structural change per trim).
            float refGain = refinementOnTrim / Mathf.Max(0.5f, parent.branchVigor);
            parent.refinementLevel = Mathf.Min(parent.refinementLevel + refGain, refinementCap);

            // Cutting the tip reduces local vigor — energy that was driving extension is lost.
            parent.branchVigor = Mathf.Max(vigorMin, parent.branchVigor * vigorTrimMultiplier);
        }

        // Back-budding: stimulate the nearest 3 non-root ancestors so they have a
        // boosted chance to sprout a lateral next spring.
        if (!node.isRoot)
        {
            int stimulated = 0;
            TreeNode ancestor = parent;
            while (ancestor != null && stimulated < 3)
            {
                if (!ancestor.isRoot)
                {
                    ancestor.backBudStimulated = true;
                    stimulated++;
                }
                ancestor = ancestor.parent;
            }
        }

        // Wound: mark the exposed cut face and spawn a visualization.
        // Subdivision cut (same depth) = tip nip — small ring.
        // Real branch cut (deeper node) = full callus wound.
        if (parent != null && !parent.isRoot && parent != root)
        {
            bool isSubdivisionCut  = (node.depth == parent.depth);
            parent.hasWound        = true;
            parent.woundRadius     = isSubdivisionCut ? node.radius * 0.35f : node.radius;
            parent.woundFaceNormal = isSubdivisionCut ? parent.growDirection : node.growDirection;
            parent.woundAge        = 0f;
            parent.pasteApplied    = false;
            CreateWoundObject(parent);

            // Trim trauma: small health hit on the cut site. Accumulates with repeated cuts;
            // recovers at trimTraumaRecoveryPerSeason each spring.
            ApplyDamage(parent, DamageType.TrimTrauma, trimTraumaDamage);
        }

        OnSubtreeTrimmed?.Invoke(removed);
        RecalculateRadii(root);
        meshBuilder.SetDirty();
    }

    // ── Trim undo data ────────────────────────────────────────────────────────

    class TrimUndoState
    {
        public TreeNode       subtreeRoot;
        public List<TreeNode> subtreeNodes;   // every node in the subtree
        public TreeNode       parent;

        // Parent state before the trim
        public bool    isTrimCutPoint;
        public int     trimCutDepth;
        public int     regrowthSeasonCount;
        public float   health;

        // Parent wound state before the trim (may have had a pre-existing wound)
        public bool    hasWound;
        public float   woundRadius;
        public Vector3 woundFaceNormal;
        public float   woundAge;
        public bool    pasteApplied;

        // Ancestor backBudStimulated states before the trim (up to 3)
        public List<(TreeNode node, bool wasStimulated)> ancestorStates;

        public float timestamp;   // Time.realtimeSinceStartup when the trim fired
    }

    TrimUndoState CaptureTrimUndoState(TreeNode subtreeRoot, TreeNode parent)
    {
        var s = new TrimUndoState
        {
            subtreeRoot  = subtreeRoot,
            subtreeNodes = new List<TreeNode>(),
            parent       = parent,
            timestamp    = Time.realtimeSinceStartup,
        };

        CollectSubtreeNodes(subtreeRoot, s.subtreeNodes);

        if (parent != null)
        {
            s.isTrimCutPoint      = parent.isTrimCutPoint;
            s.trimCutDepth        = parent.trimCutDepth;
            s.regrowthSeasonCount = parent.regrowthSeasonCount;
            s.health              = parent.health;
            s.hasWound            = parent.hasWound;
            s.woundRadius         = parent.woundRadius;
            s.woundFaceNormal     = parent.woundFaceNormal;
            s.woundAge            = parent.woundAge;
            s.pasteApplied        = parent.pasteApplied;
        }

        s.ancestorStates = new List<(TreeNode, bool)>();
        int count = 0;
        var anc = parent;
        while (anc != null && count < 3)
        {
            if (!anc.isRoot)
            {
                s.ancestorStates.Add((anc, anc.backBudStimulated));
                count++;
            }
            anc = anc.parent;
        }

        return s;
    }

    void CollectSubtreeNodes(TreeNode node, List<TreeNode> result)
    {
        result.Add(node);
        foreach (var child in node.children)
            CollectSubtreeNodes(child, result);
    }

    /// <summary>
    /// Restores the last trim if called within the undo window (default 5 seconds).
    /// Leaves are re-spawned fresh on the restored terminals — the fall animation plays
    /// on trim and new leaves pop back on undo.
    /// </summary>
    public void UndoLastTrim()
    {
        if (!CanUndo) return;
        var u = pendingUndo;
        pendingUndo = null;

        // Re-attach the subtree
        u.parent.children.Add(u.subtreeRoot);
        foreach (var n in u.subtreeNodes)
        {
            n.isTrimmed = false;
            allNodes.Add(n);
        }

        // Restore parent fields
        u.parent.isTrimCutPoint      = u.isTrimCutPoint;
        u.parent.trimCutDepth        = u.trimCutDepth;
        u.parent.regrowthSeasonCount = u.regrowthSeasonCount;
        u.parent.health              = u.health;

        // Destroy the wound object the trim created, then restore the pre-trim state
        if (woundObjects.TryGetValue(u.parent.id, out var wGo))
        {
            Destroy(wGo);
            woundObjects.Remove(u.parent.id);
        }
        u.parent.hasWound        = u.hasWound;
        u.parent.woundRadius     = u.woundRadius;
        u.parent.woundFaceNormal = u.woundFaceNormal;
        u.parent.woundAge        = u.woundAge;
        u.parent.pasteApplied    = u.pasteApplied;
        if (u.hasWound)
            CreateWoundObject(u.parent);

        // Restore ancestor back-bud flags
        foreach (var (node, wasStimulated) in u.ancestorStates)
            node.backBudStimulated = wasStimulated;

        // Re-spawn leaves on restored terminal branch nodes
        var leafManager = GetComponent<LeafManager>();
        if (leafManager != null)
        {
            var terminals = new List<TreeNode>();
            foreach (var n in u.subtreeNodes)
                if (!n.isRoot && n.isTerminal) terminals.Add(n);
            leafManager.ForceSpawnLeaves(terminals);
        }

        RecalculateRadii(root);
        meshBuilder.SetDirty();
        Debug.Log($"[Undo] Restored subtree root={u.subtreeRoot.id} nodes={u.subtreeNodes.Count}");
    }

    void RemoveSubtree(TreeNode node, List<TreeNode> removed)
    {
        foreach (var child in node.children)
            RemoveSubtree(child, removed);

        // Clean up any dormant bud object sitting at this node's tip.
        if (budObjects.TryGetValue(node.id, out var budGo))
        {
            Destroy(budGo);
            budObjects.Remove(node.id);
        }

        // Clean up any wound object on this node.
        if (woundObjects.TryGetValue(node.id, out var woundGo))
        {
            Destroy(woundGo);
            woundObjects.Remove(node.id);
        }

        // Clean up any air layer wrap on this node.
        for (int i = airLayers.Count - 1; i >= 0; i--)
        {
            if (airLayers[i].node == node)
            {
                if (airLayers[i].wrapObject != null) Destroy(airLayers[i].wrapObject);
                airLayers.RemoveAt(i);
            }
        }

        node.isTrimmed = true;
        allNodes.Remove(node);
        removed.Add(node);
    }

    // Wiring

    /// <summary>
    /// Auto-wires all unrimmed root nodes when the player confirms Ishitsuki orientation.
    /// Wires hold the current root direction (no bending); locked from removal until set.
    /// </summary>
    public void SpawnTrainingWires()
    {
        string rootAreaInfo = rootAreaTransform != null ? rootAreaTransform.position.ToString() : "NULL";
        Debug.Log("[SpawnWires] frame=" + Time.frameCount
                  + " | rockCollider=" + (rockCollider != null)
                  + " meshBuilder=" + (meshBuilder != null)
                  + " rootAreaTransform=" + (rootAreaTransform != null)
                  + "\n  rootAreaTransform.position=" + rootAreaInfo
                  + "\n  plantingSurfacePoint.y BEFORE=" + plantingSurfacePoint.y.ToString("F3")
                  + "\n  transform.position=" + transform.position);

        // Mark Ishitsuki mode permanently — this flag never goes null.
        isIshitsukiMode = true;

        // Lock in current world Y as the new rest position so the lift system
        // considers the tree already grounded here — no lowering animation.
        initY       = transform.position.y;
        currentLift = 0f;

        // Set soil surface Y from the root area transform so Ishitsuki root chains
        // stop at the actual visible tray/soil surface.
        // rootAreaTransform.position.y IS the visible soil — the rock may be partially
        // buried below it, so we must NOT use min(areaY, rockBase).
        {
            float areaY    = rootAreaTransform != null ? rootAreaTransform.position.y : plantingSurfacePoint.y;
            float rockBase = rockCollider      != null ? rockCollider.bounds.min.y     : areaY;
            Debug.Log($"[SpawnWires] year={GameManager.year} soilY: areaY(rootArea)={areaY:F3} rockBase={rockBase:F3} → using areaY={areaY:F3}");
            plantingSurfacePoint = new Vector3(plantingSurfacePoint.x, areaY, plantingSurfacePoint.z);
        }

        // Share rock collider with the mesh builder for gripping visuals.
        if (meshBuilder != null) meshBuilder.rockCollider = rockCollider;

        // ── Diagnostic snapshot ───────────────────────────────────────────────
        Vector3 treeWorldPos  = transform.position;
        Vector3 rockWorldPos  = rockCollider != null ? rockCollider.transform.position : Vector3.zero;
        Bounds  rockBounds    = rockCollider != null ? rockCollider.bounds : new Bounds();
        Vector3 rootAreaPos   = rootAreaTransform != null ? rootAreaTransform.position : Vector3.zero;
        Debug.Log($"[Ishitsuki] year={GameManager.year} WORLD POSITIONS:" +
                  $"\n  tree.position      = {treeWorldPos}" +
                  $"\n  rock.position      = {rockWorldPos}" +
                  $"\n  rock.bounds.min    = {rockBounds.min}" +
                  $"\n  rock.bounds.max    = {rockBounds.max}" +
                  $"\n  rock.bounds.center = {rockBounds.center}" +
                  $"\n  rootArea.position  = {rootAreaPos}" +
                  $"\n  soilY (after fix)  = {plantingSurfacePoint.y:F3}" +
                  $"\n  rockTopY           = {rockBounds.max.y:F3}" +
                  $"\n  rockBottomY        = {rockBounds.min.y:F3}" +
                  $"\n  rockHeightAboveSoil= {rockBounds.max.y - plantingSurfacePoint.y:F3}" +
                  $"\n  rockScale          = {(rockCollider != null ? rockCollider.transform.lossyScale.ToString() : "N/A")}");

        // Log trunk root starting positions before draping.
        if (root != null)
        {
            int ri = 0;
            foreach (var child in root.children)
            {
                if (!child.isRoot) continue;
                Vector3 wPos = transform.TransformPoint(child.worldPosition);
                Vector3 wTip = transform.TransformPoint(child.tipPosition);
                Debug.Log($"[Ishitsuki] year={GameManager.year} TrunkRoot[{ri}] depth={child.depth}" +
                          $" worldPos={wPos} tipWorld={wTip}" +
                          $" tipY={wTip.y:F3} soilY={plantingSurfacePoint.y:F3}" +
                          $" distAboveSoil={wTip.y - plantingSurfacePoint.y:F3}");
                ri++;
            }
        }
        // ── Game-view debug markers (GL lines — visible in Game View without Gizmos) ──
        {
            _soilDbgSoilY   = plantingSurfacePoint.y;
            _soilDbgRockTop = rockCollider != null ? rockCollider.bounds.max.y : _soilDbgSoilY + 2f;
            _soilDbgRockBot = rockCollider != null ? rockCollider.bounds.min.y : _soilDbgSoilY;
            _soilDbgCenter  = rockCollider != null ? rockCollider.bounds.center : transform.position;
            _soilDbgR       = rockCollider != null ? rockCollider.bounds.extents.magnitude * 1.1f : 1.5f;
            _soilDbgActive  = true;
            _soilDbgEndTime = Time.realtimeSinceStartup + 60f;
        }
        // ─────────────────────────────────────────────────────────────────────

        // Clear existing trunk-root chains so PreGrowRootsToSoil builds fresh cables
        // from the correct trunk-tip positions instead of from the draped chain tips
        // (which can end up above the trunk after DrapeRootsOverRock adds an upward bias).
        if (root != null)
        {
            foreach (var child in root.children)
                if (child.isRoot) child.children.Clear();
        }

        // Pre-grow root cables from the trunk base all the way to the soil.
        // In real Ishitsuki the roots are already established before rock placement —
        // what takes years is new thin roots filling in, not the original cables reaching soil.
        PreGrowRootsToSoil();

        meshBuilder.SetDirty();
        Debug.Log($"[Ishitsuki] year={GameManager.year} SpawnTrainingWires — initY={initY:F3} soilY={plantingSurfacePoint.y:F3}");
    }

    /// <summary>
    /// Walks all root nodes parent-first and snaps each one onto the rock surface,
    /// bending its growDirection to follow the surface tangent downward.
    /// Called once when the player confirms orientation.
    /// </summary>
    void DrapeRootsOverRock()
    {
        if (rockCollider == null || root == null) return;

        float snapRadius = rockCollider.bounds.extents.magnitude * 1.5f;
        int   snapped    = 0;

        var queue = new Queue<TreeNode>();
        foreach (var child in root.children)
            if (child.isRoot) queue.Enqueue(child);

        while (queue.Count > 0)
        {
            TreeNode node = queue.Dequeue();

            Vector3 worldPos = transform.TransformPoint(node.worldPosition);

            // Nodes inside the rock bounds can't use Physics.ClosestPoint reliably.
            // Shoot a ray from the trunk base in this root's own radial direction so
            // each root projects to a different surface point and they fan out naturally.
            Vector3 closestPt;
            bool    insideBounds = rockCollider.bounds.Contains(worldPos);
            if (insideBounds)
            {
                // Radial direction: horizontal XZ offset of this node from the trunk base.
                Vector3 localRadial = node.worldPosition - root.worldPosition;
                localRadial.y = 0f;
                if (localRadial.sqrMagnitude < 0.001f)
                    localRadial = new Vector3(node.growDirection.x, 0f, node.growDirection.z);
                Vector3 worldRadial   = transform.TransformDirection(localRadial).normalized;
                if (worldRadial.sqrMagnitude < 0.001f) worldRadial = Vector3.forward;
                Vector3 trunkWorldPos = transform.TransformPoint(root.worldPosition);
                if (rockCollider.Raycast(new Ray(trunkWorldPos, worldRadial),
                        out RaycastHit bHit, 10f))
                    closestPt = bHit.point;
                else
                    closestPt = Physics.ClosestPoint(worldPos, rockCollider,
                        rockCollider.transform.position, rockCollider.transform.rotation);
            }
            else
            {
                closestPt = Physics.ClosestPoint(worldPos, rockCollider,
                    rockCollider.transform.position, rockCollider.transform.rotation);
            }

            float dist = Vector3.Distance(worldPos, closestPt);

            if (insideBounds || dist < snapRadius)
            {
                // Surface normal via raycast from slightly outside.
                Vector3 outward = closestPt - rockCollider.bounds.center;
                if (outward.sqrMagnitude < 0.001f) outward = Vector3.up;
                outward.Normalize();
                Vector3 surfaceNormal = outward;
                if (rockCollider.Raycast(new Ray(closestPt + outward * 0.5f, -outward),
                        out RaycastHit hit, 1f))
                    surfaceNormal = hit.normal;

                // Move node base to surface with small clearance.
                node.worldPosition = transform.InverseTransformPoint(
                    closestPt + surfaceNormal * 0.025f);

                // Bend growDirection to surface tangent, biased downward.
                Vector3 worldDir = transform.TransformDirection(node.growDirection);
                Vector3 tangent  = Vector3.ProjectOnPlane(worldDir, surfaceNormal);
                if (tangent.sqrMagnitude < 0.001f)
                {
                    Vector3 radialOut = new Vector3(
                        worldPos.x - rockCollider.bounds.center.x, 0f,
                        worldPos.z - rockCollider.bounds.center.z).normalized;
                    tangent = Vector3.ProjectOnPlane(radialOut, surfaceNormal);
                }
                // Add gravity bias then re-project onto the surface plane so the
                // direction stays truly tangent — without this re-projection the
                // direction has an inward component and tipPosition dips into the rock,
                // forcing the next segment to angle sharply upward (the zigzag).
                // A small outward-normal offset keeps the tip just above the surface.
                tangent = Vector3.ProjectOnPlane(
                    (tangent.normalized + Vector3.down * 0.4f).normalized,
                    surfaceNormal);
                if (tangent.sqrMagnitude < 0.001f)
                    tangent = Vector3.ProjectOnPlane(Vector3.down, surfaceNormal);
                tangent = (tangent.normalized + surfaceNormal * 0.08f).normalized;
                node.growDirection = transform.InverseTransformDirection(tangent).normalized;
                snapped++;
            }

            foreach (var child in node.children)
                if (child.isRoot && !child.isTrimmed) queue.Enqueue(child);
        }

        Debug.Log($"[Ishitsuki] DrapeRootsOverRock — snapped {snapped} root nodes");
    }

    /// <summary>
    /// For each trunk root snapped to the rock surface, pre-spawns a fully-grown chain
    /// of nodes tracing the rock face down to the soil level.
    ///
    /// Real Ishitsuki roots are established BEFORE placement — the tree was trained to
    /// the rock over years before the player places it. Only the new fill-in roots that
    /// grow afterward should take a long time. The initial cables reach soil immediately.
    /// </summary>
    void PreGrowRootsToSoil()
    {
        if (root == null) return;
        if (rockCollider == null)
        {
            Debug.LogWarning($"[PreGrow] year={GameManager.year} rockCollider is NULL (isIshitsukiMode={isIshitsukiMode}) — cannot drape roots.");
            return;
        }

        float soilY = debugSoilYOverride ? debugSoilY : plantingSurfacePoint.y;
        if (debugSoilYOverride && debugSoilY <= -9998f)
        {
            debugSoilY = plantingSurfacePoint.y;
            soilY      = debugSoilY;
        }

        float segLen     = rootSegmentLength * 0.5f;
        int   grown      = 0;
        float rockSearchR = rockCollider.bounds.extents.magnitude * 2.5f;

        var trunkRoots = new List<TreeNode>();
        foreach (var child in root.children)
            if (child.isRoot) trunkRoots.Add(child);

        Debug.Log($"[PreGrow] year={GameManager.year} soilY={soilY:F3} segLen={segLen:F3} trunkRoots={trunkRoots.Count}");

        // Prevent multiple strands from converging on the same rock-surface point.
        // Each strand claims its equatorial edge XZ; later strands rotate their scan
        // direction until they land at least minEdgeSep away from all claimed points.
        var  claimedEdges = new List<Vector3>();
        float minEdgeSep  = Mathf.Max(segLen * 1.5f, 0.04f);

        int strandIndex = 0;
        foreach (var startNode in trunkRoots)
        {
            TreeNode current     = startNode;
            int      strandGrown = 0;

            // ── Phase 1: fast-forward to the chain tip ────────────────────────────
            while (current.children.Count > 0)
                current = current.children[0];

            Vector3 existingTip = transform.TransformPoint(current.tipPosition);
            if (existingTip.y <= soilY + 0.05f)
            {
                Debug.Log($"[PreGrow] year={GameManager.year} strand={strandIndex} tip already at soil tipY={existingTip.y:F3}");
                strandIndex++;
                continue;
            }

            // ── Find entry point on rock face this strand is aimed at ─────────────
            // Each trunk root points in a different XZ direction from the trunk.
            // Cast from outside the rock bounds inward along that direction so every
            // strand starts on its own face — not all piling onto the nearest one.
            Vector3 startTip  = transform.TransformPoint(startNode.tipPosition);
            Vector3 strandDir = transform.TransformDirection(startNode.growDirection).normalized;
            Vector3 strandXZ  = new Vector3(strandDir.x, 0f, strandDir.z);
            if (strandXZ.sqrMagnitude < 0.001f)
                strandXZ = new Vector3(strandDir.x, strandDir.z, 0f);
            if (strandXZ.sqrMagnitude < 0.001f)
                strandXZ = Vector3.right;
            strandXZ = strandXZ.normalized;

            Vector3 rockCenter  = rockCollider.bounds.center;
            float   rockTopY    = rockCollider.bounds.max.y;
            float   surfOffset  = rootTerminalRadius * 2f; // float nodes one full diameter above surface

            // ── Entry point: two-step — find XZ edge, then scan down from above it ─────
            // Step 1: horizontal ray at rock center Y (the widest cross-section) finds the
            //         outermost XZ position of the rock in this strand's direction.
            //         This almost always hits, unlike a ray at startTip.y which can miss
            //         near the rock top where the mesh tapers.
            float   entryY    = Mathf.Max(startTip.y, rockTopY) + 0.5f;
            float   entryDist = rockCollider.bounds.size.y + 1.5f;

            // Find the equatorial edge XZ for this strand, retrying with small angular
            // offsets if the hit point is too close to an already-claimed one.
            Vector3 edgeXZ  = Vector3.zero;
            bool    edgeOk  = false;
            for (int retry = 0; retry <= 8 && !edgeOk; retry++)
            {
                float   retryA   = retry * (Mathf.PI / 9f); // ~20° steps
                float   cos      = Mathf.Cos(retryA);
                float   sin      = Mathf.Sin(retryA);
                // Rotate strandXZ left/right alternately: 0, +20°, -20°, +40°, -40°…
                float   sign     = (retry % 2 == 0) ? 1f : -1f;
                float   a        = retryA * sign;
                float   scanX    = strandXZ.x * Mathf.Cos(a) - strandXZ.z * Mathf.Sin(a);
                float   scanZ    = strandXZ.x * Mathf.Sin(a) + strandXZ.z * Mathf.Cos(a);
                Vector3 scanDir  = new Vector3(scanX, 0f, scanZ).normalized;

                Vector3 horizOrig = rockCenter + scanDir * rockSearchR;
                horizOrig.y       = rockCenter.y;
                Vector3 candidate;
                if (rockCollider.Raycast(new Ray(horizOrig, -scanDir), out RaycastHit edgeHit, rockSearchR * 2f))
                {
                    candidate = edgeHit.point;
                }
                else
                {
                    float projExtent = Mathf.Abs(scanDir.x) * rockCollider.bounds.extents.x
                                     + Mathf.Abs(scanDir.z) * rockCollider.bounds.extents.z;
                    candidate = new Vector3(rockCenter.x + scanDir.x * projExtent,
                                           rockCenter.y,
                                           rockCenter.z + scanDir.z * projExtent);
                }

                // Check XZ distance against all previously claimed edges.
                bool tooClose = false;
                foreach (var claimed in claimedEdges)
                {
                    float dx = candidate.x - claimed.x;
                    float dz = candidate.z - claimed.z;
                    if (dx * dx + dz * dz < minEdgeSep * minEdgeSep) { tooClose = true; break; }
                }

                if (!tooClose || retry == 8)
                {
                    edgeXZ  = candidate;
                    // If we rotated, also update strandXZ so step-loop raycasts stay consistent.
                    if (retry > 0) strandXZ = scanDir;
                    edgeOk  = true;
                }
            }
            claimedEdges.Add(edgeXZ);

            // Step 2: edgeXZ is the rock's outermost XZ in this strand's direction at
            //         its widest Y cross-section. Used as snap target when under-rock.
            //         baseWorld starts at the trunk-root tip — no entry scan landing, so
            //         there is no mesh gap between the trunk root and the first pre-grown node.
            Vector3 baseWorld       = startTip;
            bool    hasHitExterior  = false;
            // Track the previous segment's direction so we can reject sharp backwards bends.
            Vector3 prevTangent     = strandDir; // world-space; updated each step

            // ── Phase 2: step down the rock face to soil ──────────────────────────
            // Each step re-queries the rock exterior by shooting a horizontal ray from
            // outside the rock along the strand's fixed XZ direction at the new Y level.
            //
            // Before the first exterior hit: if the horizontal ray misses, check whether
            // we are still inside the rock with a downward ray. If so, snap XZ to edgeXZ
            // (the outer edge at the rock's widest section) so the chain exits the rock
            // instead of tunnelling through it.
            //
            // After the first exterior hit: a horizontal miss means we have descended past
            // the rock's lower surface — free-fall straight down to soil.
            for (int step = 0; step < 120; step++)
            {
                if (baseWorld.y <= soilY + 0.05f)
                {
                    Debug.Log($"[PreGrow] year={GameManager.year} strand={strandIndex} step={step} REACHED SOIL baseY={baseWorld.y:F3}");
                    break;
                }

                // Advance Y by one segment downward.
                float targetY = baseWorld.y - segLen;
                if (targetY < soilY) targetY = soilY;

                // Horizontal ray: from outside the rock inward along strandXZ at targetY.
                Vector3 scanOrig = rockCenter + strandXZ * rockSearchR;
                scanOrig.y       = targetY;
                bool hitRock     = rockCollider.Raycast(new Ray(scanOrig, -strandXZ), out RaycastHit hit, rockSearchR * 2f);

                Vector3 nodePos;
                Vector3 tangent;

                string stepMode;
                if (hitRock)
                {
                    // Exterior surface found — float one full root diameter above it.
                    nodePos         = hit.point + hit.normal * surfOffset;
                    tangent         = nodePos - baseWorld;
                    tangent         = tangent.sqrMagnitude > 0.001f ? tangent.normalized : Vector3.down;
                    stepMode        = "exterior";
                    hasHitExterior  = true;
                }
                else if (!hasHitExterior)
                {
                    // Haven't reached the exterior yet — check whether the trunk-root tip
                    // (or a prior node) is inside the rock by shooting down from above.
                    Vector3 checkOrig = new Vector3(baseWorld.x, rockTopY + 0.5f, baseWorld.z);
                    float   checkDist = rockTopY - soilY + 1f;
                    bool underRock    = rockCollider.Raycast(new Ray(checkOrig, Vector3.down), out RaycastHit checkHit, checkDist)
                                       && checkHit.point.y > targetY;
                    if (underRock)
                    {
                        // Snap XZ directly to the outer edge at this Y — jumps chain
                        // onto the rock exterior without drifting through the interior.
                        nodePos  = new Vector3(edgeXZ.x, targetY, edgeXZ.z);
                        tangent  = (nodePos - baseWorld).sqrMagnitude > 0.001f
                                   ? (nodePos - baseWorld).normalized
                                   : Vector3.down;
                        stepMode = "toEdge";
                    }
                    else
                    {
                        // We're above the rock and the ray just missed — free-fall.
                        nodePos  = new Vector3(baseWorld.x, targetY, baseWorld.z);
                        tangent  = Vector3.down;
                        stepMode = "freeFall";
                    }
                }
                else
                {
                    // Horizontal ray missed after tracking the exterior — the rock's side
                    // face has tapered away.  Before free-falling, try a downward ray from
                    // the outer edge (edgeXZ) to find the rock's lower curved surface.
                    // This lets the chain follow the lower hemisphere of a round rock
                    // instead of hanging in space at the equatorial XZ.
                    Vector3 downOrig = new Vector3(edgeXZ.x, baseWorld.y + 0.1f, edgeXZ.z);
                    float   downDist = baseWorld.y - soilY + 0.5f;
                    if (rockCollider.Raycast(new Ray(downOrig, Vector3.down), out RaycastHit lowerHit, downDist)
                        && lowerHit.point.y < baseWorld.y)
                    {
                        // Lower rock face found — float above it.
                        nodePos   = lowerHit.point + lowerHit.normal * surfOffset;
                        nodePos.y = Mathf.Clamp(nodePos.y, soilY, baseWorld.y - 0.001f);
                        tangent   = (nodePos - baseWorld).sqrMagnitude > 0.001f
                                    ? (nodePos - baseWorld).normalized
                                    : Vector3.down;
                        stepMode  = "lowerFace";
                    }
                    else
                    {
                        // Nothing found — rock is fully below us, drop straight to soil.
                        nodePos  = new Vector3(baseWorld.x, targetY, baseWorld.z);
                        tangent  = Vector3.down;
                        stepMode = "freeFall";
                    }
                }

                // ── Sharp-angle guard ────────────────────────────────────────────────
                // If the new segment would bend back toward the trunk at an angle sharper
                // than minCableAngleDeg, override to freeFall (drop straight down).
                // This removes the visible U-turn kinks on the upper rock face.
                if (stepMode != "freeFall")
                {
                    float bendAngle = Vector3.Angle(prevTangent, tangent);
                    if (bendAngle > (180f - minCableAngleDeg))
                    {
                        nodePos  = new Vector3(baseWorld.x, targetY, baseWorld.z);
                        tangent  = Vector3.down;
                        stepMode = "freeFall(angleGuard)";
                    }
                }
                prevTangent = tangent;

                if (step == 0 || step % 5 == 0)
                    Debug.Log($"[PreGrow] year={GameManager.year} s={strandIndex} step={step}" +
                              $" targetY={targetY:F3} nodeY={nodePos.y:F3} mode={stepMode}");

                Vector3 localPos = transform.InverseTransformPoint(baseWorld);
                Vector3 localDir = transform.InverseTransformDirection(tangent).normalized;

                var newNode              = CreateNode(localPos, localDir, rootTerminalRadius, segLen, current);
                newNode.isRoot           = true;
                newNode.isTrainingWire   = true;   // exempt from rootVisibilityDepth cull in TreeMeshBuilder
                newNode.length           = segLen;
                newNode.isGrowing        = false;
                newNode.radius           = rootTerminalRadius;

                current = newNode;
                grown++;
                strandGrown++;

                // Advance to the new exterior surface point — NOT tangent * segLen.
                baseWorld = nodePos;
            }

            // Prevent startNode from reaching targetLength in the Update growth loop and
            // firing SpawnChildren — which would append a second air-growing continuation
            // root alongside the pre-grown chain. Mark it fully grown but frozen.
            if (startNode.isGrowing)
            {
                startNode.length    = startNode.targetLength;
                startNode.isGrowing = false;
                startNode.radius    = rootTerminalRadius;
                startNode.minRadius = rootTerminalRadius;
            }

            Vector3 finalTip = transform.TransformPoint(current.tipPosition);
            Debug.Log($"[PreGrow] year={GameManager.year} strand={strandIndex} DONE grew={strandGrown} finalTipY={finalTip.y:F3} soilY={soilY:F3} aboveSoil={finalTip.y - soilY:F3}");
            strandIndex++;
        }

        // After pre-growing, rebuild radii so the new nodes feed into the pipe model.
        if (grown > 0)
            RecalculateRadii(root);

        Debug.Log($"[Ishitsuki] year={GameManager.year} PreGrowRootsToSoil — spawned {grown} pre-grown nodes total");
    }

    /// <summary>
    /// Attaches a wire to a node.
    /// </summary>
    public void WireNode(TreeNode node, Vector3 targetDirectionLocal)
    {
        if (node.hasWire && node.wireSetProgress > 0f)
        {
            float damage = Mathf.Lerp(0.05f, 0.25f, node.wireSetProgress);
            ApplyDamage(node, DamageType.WireBend, damage);
        }

        node.wireOriginalDirection = node.growDirection;
        node.hasWire               = true;
        node.wireTargetDirection   = targetDirectionLocal.normalized;
        node.wireSetProgress       = 0f;
        node.wireDamageProgress    = 0f;
        node.wireAgeDays           = 0f;
    }

    // ── Mesh surface helpers ──────────────────────────────────────────────────

    struct RockSurfaceHit { public Vector3 point, normal; public float dist; }

    /// <summary>
    /// Finds the closest point on any triangle of the mesh to <paramref name="worldPos"/>.
    /// Works for any shape — convex, concave, overhang — from any position.
    /// Uses interpolated per-vertex normals (barycentric) to avoid zero-normal on shared vertices.
    /// </summary>
    static RockSurfaceHit ClosestPointOnMesh(
        Vector3   worldPos,
        Vector3[] verts,
        int[]     tris,
        Vector3[] normals,   // mesh.normals — per-vertex, pre-computed by Unity
        Transform xform)
    {
        Vector3 local   = xform.InverseTransformPoint(worldPos);
        float   minSq   = float.MaxValue;
        Vector3 bestPt  = local;
        int     bestI   = 0;

        for (int i = 0; i < tris.Length; i += 3)
        {
            Vector3 a = verts[tris[i]];
            Vector3 b = verts[tris[i + 1]];
            Vector3 c = verts[tris[i + 2]];

            Vector3 cp = ClosestPointOnTriangle(local, a, b, c);
            float   sq = (cp - local).sqrMagnitude;
            if (sq >= minSq) continue;

            minSq  = sq;
            bestPt = cp;
            bestI  = i;
        }

        // Interpolate surface normal at bestPt using barycentric coords + per-vertex normals.
        // This never produces zero even when bestPt is exactly on a shared vertex,
        // because per-vertex normals are averaged over all adjacent faces by Unity.
        Vector3 bestN = Vector3.up;
        if (normals != null && normals.Length > 0)
        {
            int     iA = tris[bestI], iB = tris[bestI + 1], iC = tris[bestI + 2];
            Vector3 a  = verts[iA],   b  = verts[iB],       c  = verts[iC];

            // Compute barycentric coords of bestPt on triangle (a, b, c).
            // bestPt = a*(1-u-v) + b*v + c*u  →  wA=1-u-v, wB=v, wC=u
            Vector3 v0 = c - a, v1 = b - a, v2 = bestPt - a;
            float d00 = Vector3.Dot(v0, v0), d01 = Vector3.Dot(v0, v1), d02 = Vector3.Dot(v0, v2);
            float d11 = Vector3.Dot(v1, v1), d12 = Vector3.Dot(v1, v2);
            float denom = d00 * d11 - d01 * d01;

            Vector3 interpolated;
            if (Mathf.Abs(denom) < 1e-10f)
            {
                // Degenerate (zero-area) triangle — fall back to cross product
                interpolated = Vector3.Cross(b - a, c - a);
            }
            else
            {
                float invD = 1f / denom;
                float wC   = (d11 * d02 - d01 * d12) * invD;   // weight for vertex C
                float wB   = (d00 * d12 - d01 * d02) * invD;   // weight for vertex B
                float wA   = 1f - wB - wC;                     // weight for vertex A
                // Clamp to [0,1] to handle floating-point noise at triangle edges
                wA = Mathf.Clamp01(wA); wB = Mathf.Clamp01(wB); wC = Mathf.Clamp01(wC);
                float sum = wA + wB + wC;
                if (sum > 1e-6f) { wA /= sum; wB /= sum; wC /= sum; }
                interpolated = normals[iA] * wA + normals[iB] * wB + normals[iC] * wC;
            }

            if (interpolated.sqrMagnitude > 1e-6f)
                bestN = interpolated.normalized;
            // else bestN stays Vector3.up (safe fallback)
        }

        return new RockSurfaceHit
        {
            point  = xform.TransformPoint(bestPt),
            normal = xform.TransformDirection(bestN),
            dist   = Mathf.Sqrt(minSq)
        };
    }

    /// <summary>
    /// Returns the closest point on triangle (a,b,c) to point p.
    /// All in the same local space. Standard Ericson/Christer method.
    /// </summary>
    static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a, ac = c - a, ap = p - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f) return a;

        Vector3 bp = p - b;
        float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3) return b;

        Vector3 cp = p - c;
        float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6) return c;

        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            return a + ab * (d1 / (d1 - d3));

        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            return a + ac * (d2 / (d2 - d6));

        float va = d3 * d6 - d5 * d4;
        if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
            return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));

        float denom = 1f / (va + vb + vc);
        return a + ab * (vb * denom) + ac * (vc * denom);
    }

    /// <summary>
    /// Collects the contiguous wire run that includes <paramref name="start"/>.
    /// Walks up to the highest wired ancestor, then follows the most direction-aligned
    /// wired child at each fork (leaving other branches' wires in place).
    /// </summary>
    public List<TreeNode> CollectWireRun(TreeNode start)
    {
        // Walk up to the top of the run
        TreeNode top = start;
        while (top.parent != null && top.parent.hasWire)
            top = top.parent;

        // Walk down following the best-aligned wired child at each fork
        var run = new List<TreeNode>();
        TreeNode cur = top;
        while (cur != null && cur.hasWire)
        {
            run.Add(cur);
            cur = WireRunChild(cur);
        }
        return run;
    }

    TreeNode WireRunChild(TreeNode node)
    {
        TreeNode best    = null;
        float    bestDot = -2f;
        foreach (var child in node.children)
        {
            if (!child.hasWire) continue;
            float dot = Vector3.Dot(node.growDirection, child.growDirection);
            if (dot > bestDot) { bestDot = dot; best = child; }
        }
        return best;
    }

    /// <summary>
    /// Smart unwire: if every node in the wire run is fully set, removes them all.
    /// Otherwise falls back to unwiring only the clicked node.
    /// </summary>
    public void UnwireRun(TreeNode node)
    {
        var run = CollectWireRun(node);

        bool allSet = true;
        foreach (var n in run)
            if (n.wireSetProgress < 1f) { allSet = false; break; }

        if (allSet && run.Count > 1)
        {
            foreach (var n in run)
                UnwireNode(n);
            Debug.Log($"[Wire] UnwireRun removed count={run.Count}");
        }
        else
        {
            UnwireNode(node);
        }
    }

    /// <summary>
    /// Removes the wire. If not fully set, the branch springs back partially.
    /// </summary>
    public void UnwireNode(TreeNode node)
    {
        if (node.wireSetProgress < 1f)
        {
            Vector3 prevDir    = node.growDirection;
            node.growDirection = Vector3.Slerp(
                node.wireOriginalDirection,
                node.wireTargetDirection,
                node.wireSetProgress).normalized;

            Quaternion springBackRot = Quaternion.FromToRotation(prevDir, node.growDirection);
            RotateAndPropagateDescendants(node, springBackRot, null);
        }

        node.hasWire            = false;
        node.wireSetProgress    = 0f;
        node.wireDamageProgress = 0f;
        node.wireAgeDays        = 0f;
        meshBuilder.SetDirty();
    }

    /// <summary>
    /// Rotates every descendant's growDirection by rot and propagates their worldPositions.
    /// </summary>
    public void RotateAndPropagateDescendants(
        TreeNode node, Quaternion rot,
        System.Collections.Generic.Dictionary<TreeNode, Vector3> originalDirs)
    {
        foreach (var child in node.children)
        {
            if (originalDirs != null && originalDirs.TryGetValue(child, out var origDir))
                child.growDirection = (rot * origDir).normalized;
            else
                child.growDirection = (rot * child.growDirection).normalized;

            child.worldPosition = node.tipPosition;
            RotateAndPropagateDescendants(child, rot, originalDirs);
        }
    }

    /// <summary>
    /// Reduces a node's health by amount, clamped to 0.
    /// </summary>
    public void ApplyDamage(TreeNode node, DamageType type, float amount)
    {
        node.health = Mathf.Max(0f, node.health - amount);
    }

    // Wound System

    void CreateWoundObject(TreeNode node)
    {
        // Destroy any existing wound object on this node (re-trim case)
        if (woundObjects.TryGetValue(node.id, out var existing))
        {
            Destroy(existing);
            woundObjects.Remove(node.id);
        }

        var go = new GameObject($"_Wound_{node.id}");
        go.transform.SetParent(transform, false);

        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();

        // The major radius must be at least as large as the parent's own tip radius,
        // otherwise the ring is buried inside the parent branch mesh.
        // Add a 10% bias to ensure it sits clearly outside the surface.
        float visR   = Mathf.Max(node.woundRadius, node.tipRadius) * 1.1f;
        float minorR = visR * 0.2f;
        mf.mesh = BuildHalfTorusMesh(visR, minorR, 12, 6);

        // Material: use override if assigned; otherwise a plain dark-brown Unlit fallback.
        // The bark shader (Custom/BarkVertexColor) uses vertex colours and has no _Color
        // property, so we can't tint it at runtime.
        if (woundMaterialOverride != null)
        {
            mr.sharedMaterial = woundMaterialOverride;
        }
        else
        {
            var mat = new Material(Shader.Find("Unlit/Color"));
            mat.color = new Color(0.28f, 0.18f, 0.10f);  // dark bark brown
            mr.material = mat;
        }

        // Place at the cut face, pushed slightly forward along the cut direction
        // so the ring sits proud of the parent's surface rather than buried in it.
        // Orient so the ring plane is perpendicular to the removed branch's direction.
        go.transform.localPosition = node.tipPosition + node.woundFaceNormal * (minorR * 0.5f);
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, node.woundFaceNormal);

        woundObjects[node.id] = go;
        Debug.Log($"[Wound] Created wound node={node.id} radius={node.woundRadius:F3}");
    }

    /// <summary>
    /// Low-poly half-torus (outer half only, theta ∈ [0, π]).
    /// The ring lies in the XZ plane; the tube protrudes along +Y.
    /// </summary>
    Mesh BuildHalfTorusMesh(float R, float r, int phiSteps, int thetaSteps)
    {
        var verts = new List<Vector3>();
        var tris  = new List<int>();
        var uvs   = new List<Vector2>();

        int cols = phiSteps + 1;

        for (int j = 0; j <= thetaSteps; j++)
        {
            float theta = Mathf.PI * j / thetaSteps;
            float ct = Mathf.Cos(theta), st = Mathf.Sin(theta);
            for (int i = 0; i <= phiSteps; i++)
            {
                float phi = Mathf.PI * 2f * i / phiSteps;
                float cp = Mathf.Cos(phi), sp = Mathf.Sin(phi);
                verts.Add(new Vector3((R + r * ct) * cp, r * st, (R + r * ct) * sp));
                uvs.Add(new Vector2((float)i / phiSteps, (float)j / thetaSteps));
            }
        }

        for (int j = 0; j < thetaSteps; j++)
        {
            for (int i = 0; i < phiSteps; i++)
            {
                int a = j * cols + i, b = j * cols + i + 1;
                int c = (j + 1) * cols + i, d = (j + 1) * cols + i + 1;
                tris.Add(a); tris.Add(c); tris.Add(b);
                tris.Add(b); tris.Add(c); tris.Add(d);
            }
        }

        var mesh = new Mesh { name = "HalfTorus" };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.SetUVs(0, uvs);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// Applies wound sealing paste to a node, stopping the seasonal health drain.
    /// </summary>
    public void ApplyPaste(TreeNode node)
    {
        if (!node.hasWound || node.pasteApplied) return;
        node.pasteApplied = true;

        // Tint the wound object grey to indicate it's been sealed
        if (woundObjects.TryGetValue(node.id, out var go) && go != null)
        {
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                Color c = mr.material.color;
                mr.material.color = new Color(c.r * 1.1f, c.g * 1.15f, c.b * 1.1f, c.a);
            }
        }
        Debug.Log($"[Wound] Paste applied node={node.id}");
    }

    /// <summary>
    /// Recursively updates children's worldPosition when a parent node is moved.
    /// </summary>
    public void PropagatePosition(TreeNode node)
    {
        foreach (var child in node.children)
        {
            child.worldPosition = node.tipPosition;
            PropagatePosition(child);
        }
    }
}

public enum BudType
{
    Alternate,  // one continuation + optional lateral (default for most trees)
    Opposite,   // two symmetric equal forks (Japanese maple, ash, dogwood)
}
