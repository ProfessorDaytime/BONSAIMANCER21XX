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

    [Header("Wound System")]
    [Tooltip("Health drained from a wounded node per growing season. Paste stops this drain.")]
    [SerializeField] float woundDrainRate = 0.05f;

    [Tooltip("Growing seasons to fully callus over one unit of wound radius. " +
             "Larger wounds (thicker cut branches) take proportionally longer to heal. " +
             "E.g. radius=0.1 × 20 = 2 seasons; radius=0.5 × 20 = 10 seasons.")]
    [SerializeField] float seasonsToHealPerUnit = 20f;

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

    [Tooltip("World-unit radius around the rock surface within which roots deflect to follow it.")]
    [SerializeField] float rockInfluenceRadius = 0.4f;

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
    float cachedTreeHeight = 1f;  // updated each spring; used for root spread radius
    int   lastRecalcDay   = -1;   // tracks last in-game day RecalculateRadii was run mid-season

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

        // Hide the seed once the trunk sprout has grown enough to be visible
        if (seedObject != null && seedObject.activeSelf &&
            root != null && root.length >= seedHideLength)
        {
            seedObject.SetActive(false);
            Debug.Log("[Tree] Seed hidden -- sprout emerged");
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
                             * healthMult;

            // Pot-bound roots near the tray wall grow slower
            if (node.isRoot && node.boundaryPressure >= boundaryPressureThreshold)
                speed *= boundaryGrowthScale;

            node.length += speed * inGameDays;
            node.age    += inGameDays * rate;
            anyGrew      = true;

            if (node.length >= node.targetLength)
            {
                bool belowCap = node.isRoot
                    ? node.depth < maxRootDepth
                    : node.depth < SeasonDepthCap && node.depth < CutPointDepthCap(node);

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
        foreach (var node in allNodes)
        {
            if (node.isTrimmed || node.isRoot || node.isGrowing) continue;
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
                Vector3 dir     = (outward + Vector3.down * rootInitialPitch).normalized;
                float   len     = Mathf.Max(rootSegmentLength * rootSegmentLengthDecay, 0.3f);
                var r           = CreateNode(root.worldPosition, dir, rootTerminalRadius, len, root);
                r.isRoot        = true;
            }
            Debug.Log($"[GRoot] Auto-planted {rootsToAdd} trunk roots | trunkRoots={trunkRootCount + rootsToAdd}/{targetTrunkRoots} year={GameManager.year}");
        }

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

            bool belowCap = node.isRoot
                ? node.depth < maxRootDepth
                : node.depth < SeasonDepthCap && node.depth < CutPointDepthCap(node);

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

            // Divide the chord into sub-segments so each is independently wireable.
            // Clamp per-segment (not the chord) so tip segments stay wireable regardless of N.
            float childLength = (!terminal.isRoot && branchSubdivisions > 1) ? chordLength / branchSubdivisions : chordLength;
            childLength = Mathf.Max(childLength, terminal.isRoot ? 0.3f : minSegmentLength);

            float nodeRadius = terminal.isRoot ? rootTerminalRadius : terminalRadius;

            if (terminal.isRoot)
            {
                if (currentRootCount >= maxTotalRootNodes) continue;  // hard cap reached

                float distRatio = RootDistRatio(terminal);
                if (distRatio >= 1.3f) continue;  // beyond hard outer boundary — stop

                if (distRatio >= 0.8f) childLength *= wallSegmentScale;

                var cont = CreateNode(terminal.tipPosition, ContinuationDirection(terminal), nodeRadius, childLength, terminal);
                cont.isRoot = true;
                currentRootCount++;

                float lateralScale  = Mathf.Clamp01(1f - distRatio);
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

                    if (currentBranchCount < maxBranchNodes && Random.value < springLateralChance * vigorFactor)
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
                float segLen   = branchSubdivisions > 1 ? chordLen / branchSubdivisions : chordLen;
                segLen = Mathf.Max(segLen, minSegmentLength) * Mathf.Max(0.1f, 1f - apicalDominance);
                var lat = CreateNode(node.tipPosition, LateralDirection(node), terminalRadius, segLen, node);
                lat.isRoot = false;
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
        nextId    = 0;
        startYear = GameManager.year;

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
        var node  = new TreeNode(nextId++, depth, position, direction, radius, targetLength, parent);

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
                var trunkSeg   = new TreeNode(nextId++, 0, node.tipPosition,
                                              ContinuationDirection(node),
                                              terminalRadius, segLen, node);
                trunkSeg.isGrowing = true;
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
                                   ContinuationDirection(node), terminalRadius, node.targetLength, node);
            sub.subdivisionsLeft = node.subdivisionsLeft - 1;
            sub.isGrowing = true;
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

            float distRatio = RootDistRatio(node);
            if (distRatio >= 1.3f) return;  // hard outer boundary — no further growth

            var rootCont = CreateNode(node.tipPosition, ContinuationDirection(node), nodeRadius, segLength, node);
            rootCont.isRoot = true;
            rootCount++;

            float lateralScale  = Mathf.Clamp01(1f - distRatio);
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

            Vector3 dir = (node.growDirection * inertiaWeight
                          + radial            * rootRadialWeight
                          + Vector3.down      * rootGravityWeight
                          + rand).normalized;

            Vector3 worldTip = transform.TransformPoint(node.tipPosition);
            bool nearRock = false;

            // ── Rock surface deflection (Ishitsuki) ───────────────────────────
            if (rockCollider != null)
            {
                Vector3 closestPt = Physics.ClosestPoint(worldTip, rockCollider,
                    rockCollider.transform.position, rockCollider.transform.rotation);
                float distToRock = Vector3.Distance(worldTip, closestPt);

                if (distToRock < rockInfluenceRadius)
                {
                    nearRock = true;

                    // Get surface normal via raycast from outside inward (world space).
                    Vector3 rockCenter = rockCollider.bounds.center;
                    Vector3 outward    = closestPt - rockCenter;
                    if (outward.sqrMagnitude < 0.001f) outward = Vector3.up;
                    outward.Normalize();

                    Vector3 surfaceNormal = outward;
                    Ray normalRay = new Ray(closestPt + outward * 0.5f, -outward);
                    if (rockCollider.Raycast(normalRay, out RaycastHit normalHit, 1f))
                        surfaceNormal = normalHit.normal;

                    // dir is in local space — convert to world for the projection.
                    Vector3 worldDir = transform.TransformDirection(dir);

                    Vector3 surfaceDir = Vector3.ProjectOnPlane(worldDir, surfaceNormal);
                    if (surfaceDir.sqrMagnitude < 0.001f)
                        surfaceDir = Vector3.ProjectOnPlane(Vector3.down, surfaceNormal);
                    surfaceDir = (surfaceDir.normalized + Vector3.down * rootGravityWeight * 3f).normalized;

                    float blend = 1f - Mathf.Clamp01(distToRock / rockInfluenceRadius);
                    Vector3 worldBlended = Vector3.Slerp(worldDir, surfaceDir, blend).normalized;

                    // Convert back to local space.
                    dir = transform.InverseTransformDirection(worldBlended);

                    Debug.Log($"[Rock] Deflect node={node.id} dist={distToRock:F3} blend={blend:F2} surfaceNormal={surfaceNormal}");
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
        Debug.Log($"[Bud] Buds set | terminal={termCount} lateral={latCount} year={GameManager.year}");
    }

    // Trimming

    /// <summary>
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
        }

        OnSubtreeTrimmed?.Invoke(removed);
        RecalculateRadii(root);
        meshBuilder.SetDirty();
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
    void SpawnTrainingWires()
    {
        // Lock in current world Y as the new rest position so the lift system
        // considers the tree already grounded here — no lowering animation.
        // currentLift = 0 means "at initY", so set initY to current Y and reset lift.
        initY       = transform.position.y;
        currentLift = 0f;
        // liftTarget will be set to 0 by OnGameStateChanged when the restore state fires.

        int count = 0;
        foreach (var node in allNodes)
        {
            if (!node.isRoot || node.isTrimmed || node.hasWire) continue;
            WireNode(node, node.growDirection);
            node.isTrainingWire = true;
            count++;
        }
        meshBuilder.SetDirty();
        Debug.Log($"[Ishitsuki] SpawnTrainingWires — wired {count} root nodes, new initY={initY:F3}");
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
