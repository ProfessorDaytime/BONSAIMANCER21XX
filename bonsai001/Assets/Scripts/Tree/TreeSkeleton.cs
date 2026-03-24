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

    [Header("Wiring")]
    [Tooltip("Rate-adjusted in-game days for a wire to fully set (~2 growing seasons at speed 1).")]
    [SerializeField] float wireDaysToSet = 196f;

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

    [Tooltip("Root spread target = tree height * this multiplier. " +
             "Roots slow and stop laterals as they approach this radius.")]
    [SerializeField] float rootSpreadMultiplier = 2f;

    [Tooltip("Chance per non-terminal root node per season to sprout a new fill-in lateral " +
             "inside the spread radius. Higher = denser root mat over time.")]
    [SerializeField] [Range(0f, 1f)] float rootFillLateralChance = 0.03f;

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
                    SpawnChildren(node);
                    structureChanged = true;
                }
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

        if (structureChanged)
            RecalculateRadii(root);

        if (anyGrew)
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
            meshBuilder.SetDirty();
        }

        bool inRootMode = (state == GameState.RootPrune);
        liftTarget = inRootMode ? rootLiftHeight : 0f;
        if (meshBuilder.renderRoots != inRootMode)
        {
            meshBuilder.renderRoots = inRootMode;
            meshBuilder.SetDirty();
        }

        if (inRootMode)
        {
            int rootCount = 0;
            foreach (var n in allNodes) if (n.isRoot) rootCount++;
            Debug.Log($"[Root] Entering RootPrune | rootNodes={rootCount} | liftTarget={liftTarget} | plantingNormal={plantingNormal} plantingPoint={plantingSurfacePoint}");
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
        float spreadRadius = cachedTreeHeight * rootSpreadMultiplier;
        Debug.Log($"[GRoot] StartNewGrowingSeason year={GameManager.year} | treeHeight={cachedTreeHeight:F2} spreadRadius={spreadRadius:F2}");

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

        var terminals = new List<TreeNode>();
        int resuming  = 0;
        foreach (var node in allNodes)
        {
            if (node.isGrowing && !node.isTrimmed) resuming++;

            bool belowCap = node.isRoot
                ? node.depth < maxRootDepth
                : node.depth < SeasonDepthCap && node.depth < CutPointDepthCap(node);

            if (!node.isTrimmed && node.isTerminal && !node.isGrowing && belowCap)
                terminals.Add(node);
        }

        int rootTerminals = 0;
        int branchTerminals = 0;
        foreach (var t in terminals) { if (t.isRoot) rootTerminals++; else branchTerminals++; }
        Debug.Log($"[Tree] StartNewGrowingSeason year={GameManager.year} | depthCap={SeasonDepthCap} | terminals={terminals.Count} (roots={rootTerminals} branches={branchTerminals}) resuming={resuming} total={allNodes.Count}");

        // Count current root nodes once so the cap check is O(1) per terminal
        int currentRootCount = 0;
        foreach (var n in allNodes) if (n.isRoot) currentRootCount++;

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
                var cont = CreateNode(terminal.tipPosition, ContinuationDirection(terminal), nodeRadius, childLength, terminal);
                cont.isRoot = false;
                if (branchSubdivisions > 1)
                    cont.subdivisionsLeft = branchSubdivisions - 1;

                if (Random.value < springLateralChance)
                {
                    var lat = CreateNode(terminal.tipPosition, LateralDirection(terminal), nodeRadius, childLength * 0.85f, terminal);
                    lat.isRoot = false;
                    if (branchSubdivisions > 1)
                        lat.subdivisionsLeft = branchSubdivisions - 1;
                    GameManager.branches++;
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

        if (terminals.Count > 0 || fillCount > 0)
        {
            RecalculateRadii(root);
            meshBuilder.SetDirty();
        }
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

        var cont = CreateNode(node.tipPosition, ContinuationDirection(node), nodeRadius, segLength, node);
        cont.isRoot = false;
        if (branchSubdivisions > 1)
            cont.subdivisionsLeft = branchSubdivisions - 1;

        float lateralChanceBranch = baseBranchChance * Mathf.Pow(branchChanceDepthDecay, node.depth);
        if (Random.value < lateralChanceBranch)
        {
            float branchLength = segLength * 0.85f;
            var lat = CreateNode(node.tipPosition, LateralDirection(node), nodeRadius, branchLength, node);
            lat.isRoot = false;
            if (branchSubdivisions > 1)
                lat.subdivisionsLeft = branchSubdivisions - 1;
            GameManager.branches++;
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
    /// Returns the horizontal distance ratio of a root node's tip to the spread radius.
    /// 0 = at trunk, 1 = at target spread radius, >1 = beyond it.
    /// </summary>
    float RootDistRatio(TreeNode node)
    {
        float spreadRadius = cachedTreeHeight * rootSpreadMultiplier;
        if (spreadRadius <= 0f) return 0f;
        Vector3 tip = node.tipPosition;
        float horizDist = Mathf.Sqrt(tip.x * tip.x + tip.z * tip.z);
        return horizDist / spreadRadius;
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

            // When near the planting surface, blend toward a surface-tangent direction
            // so roots flow along the rock face instead of going through it.
            Plane surface = new Plane(plantingNormal, plantingSurfacePoint);
            Vector3 worldTip = transform.TransformPoint(node.tipPosition);
            float distToSurface = surface.GetDistanceToPoint(worldTip);

            if (distToSurface >= 0f && distToSurface < rootSurfaceSnapDist)
            {
                Vector3 surfaceDir = Vector3.ProjectOnPlane(dir, plantingNormal);
                if (surfaceDir.sqrMagnitude > 0.001f)
                {
                    float blend = 1f - Mathf.Clamp01(distToSurface / rootSurfaceSnapDist);
                    dir = Vector3.Slerp(dir, surfaceDir.normalized, blend).normalized;
                    Debug.Log($"[Root] Surface snap node={node.id} depth={node.depth} | dist={distToSurface:F2} blend={blend:F2} | dir={dir}");
                }
            }

            // Clamp: roots must never grow upward — project to horizontal if Y > 0
            if (dir.y > 0f)
            {
                dir = Vector3.ProjectOnPlane(dir, Vector3.up);
                if (dir.sqrMagnitude < 0.001f)
                    dir = radial;  // fall back to radial outward if fully vertical
                dir.Normalize();
            }

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
            return lateral;
        }
        // Lateral branches get half the phototropism blend of continuation segments
        return Vector3.Slerp(dir, SunDirection(), phototropismWeight * 0.5f);
    }

    // Keep old name as alias so any external callers don't break.
    Vector3 LateralBranchDirection(TreeNode node) => LateralDirection(node);

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

        if (isRootCall)
        {
            radiiTimer.Stop();
            if (radiiTimer.ElapsedMilliseconds > 0)
                Debug.Log($"[Perf] RecalculateRadii nodes={allNodes.Count} took {radiiTimer.ElapsedMilliseconds}ms");
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

        OnSubtreeTrimmed?.Invoke(removed);
        RecalculateRadii(root);
        meshBuilder.SetDirty();
    }

    void RemoveSubtree(TreeNode node, List<TreeNode> removed)
    {
        foreach (var child in node.children)
            RemoveSubtree(child, removed);

        node.isTrimmed = true;
        allNodes.Remove(node);
        removed.Add(node);
    }

    // Wiring

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
