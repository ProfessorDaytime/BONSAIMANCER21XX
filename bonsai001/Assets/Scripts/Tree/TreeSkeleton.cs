using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns and manages the tree graph. Drives growth, branching, and secondary thickening.
///
/// Growth model:
///   - Each terminal node (isGrowing = true) extends its length toward targetLength each frame.
///   - When it reaches targetLength it spawns children: always one continuation (apical
///     meristem) and probabilistically one lateral branch.
///   - After any structural change, RecalculateRadii() walks bottom-up and applies da Vinci's
///     pipe model: parent.radius² = sum(child.radius²). This thickens the trunk automatically
///     as more branches accumulate.
///   - Growth direction blends inertia + phototropism (sun bias) + random perturbation.
///   - Growth only ticks when isGrowing = true (set by BranchGrow game state).
/// </summary>
public class TreeSkeleton : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Growth Speed")]
    [Tooltip("How many local units per in-game day a depth-0 segment grows. " +
             "The season length (not maxDepth) controls how much the tree grows each year.")]
    [SerializeField] float baseGrowSpeed = 0.2f;

    [Tooltip("Speed multiplier per depth level — deeper branches grow slower.")]
    [SerializeField] float depthSpeedDecay = 0.85f;

    [Header("Segment Lengths")]
    [Tooltip("Target length of the root trunk segment.")]
    [SerializeField] float rootSegmentLength = 2.0f;

    [Tooltip("Each depth level, segments get this much shorter (0..1).")]
    [SerializeField] float segmentLengthDecay = 0.80f;

    [Header("Radii")]
    [Tooltip("Fixed radius of every new terminal segment. The pipe model derives all " +
             "parent radii from this — more branches = thicker trunk automatically.")]
    [SerializeField] float terminalRadius = 0.04f;

    [Header("Branching")]
    [Tooltip("Probability per segment of spawning a lateral branch (at depth 0).")]
    [SerializeField] float baseBranchChance = 0.75f;

    [Tooltip("Branch chance decreases with depth — keeps tips from over-branching.")]
    [SerializeField] float branchChanceDepthDecay = 0.90f;

    [Tooltip("Hard safety cap on segment depth.")]
    [SerializeField] int maxDepth = 50;

    [Tooltip("How many depth levels the tree can grow per year. " +
             "Year 1 cap = depthsPerYear. Year 2 = 2×depthsPerYear. Etc. " +
             "2–3 is realistic for a young tree; 4–5 for faster-growing species.")]
    [SerializeField] int depthsPerYear = 3;

    [Tooltip("Probability of a lateral branch spawning each spring (flat rate, not depth-decayed).")]
    [SerializeField] float springLateralChance = 0.80f;

    [Header("Direction")]
    [Tooltip("How strongly each segment continues its parent's direction (0..1).")]
    [SerializeField] float inertiaWeight = 0.65f;

    [Tooltip("How strongly each segment bends toward the sun (upward).")]
    [SerializeField] float phototropismWeight = 0.20f;

    [Tooltip("Magnitude of random perturbation per new segment.")]
    [SerializeField] float randomWeight = 0.15f;

    [Tooltip("Max random deviation angle (degrees) for a new branch direction.")]
    [SerializeField] float branchAngleMin = 25f;
    [SerializeField] float branchAngleMax = 55f;

    // ── References ────────────────────────────────────────────────────────────

    [HideInInspector] public TreeMeshBuilder meshBuilder;

    // ── Tree Data ─────────────────────────────────────────────────────────────

    [HideInInspector] public TreeNode       root;
    [HideInInspector] public List<TreeNode> allNodes = new List<TreeNode>();

    int  nextId          = 0;
    bool isGrowing       = false;
    int  lastGrownYear   = -1;  // tracks which year StartNewGrowingSeason last ran
    int  startYear       = -1;  // calendar year the tree was first planted

    float debugLogTimer  = 0f;

    /// <summary>Maximum depth allowed to sprout children this season.</summary>
    int SeasonDepthCap => startYear < 0
        ? depthsPerYear
        : Mathf.Min(maxDepth, (GameManager.year - startYear + 1) * depthsPerYear);

    // ── Unity ─────────────────────────────────────────────────────────────────

    void Awake()
    {
        meshBuilder = GetComponent<TreeMeshBuilder>();
        if (meshBuilder == null)
            Debug.LogError("TreeSkeleton: TreeMeshBuilder not found on this GameObject — both components must be on the same GameObject.", this);
    }

    void OnEnable()  => GameManager.OnGameStateChanged += OnGameStateChanged;
    void OnDisable() => GameManager.OnGameStateChanged -= OnGameStateChanged;

    void Update()
    {
        // Debug: press 1–9 to instantly simulate that many years of growth
        for (int k = 1; k <= 9; k++)
        {
            if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + k - 1)))
            {
                for (int y = 0; y < k; y++) SimulateYear();
                break;
            }
        }

        if (!isGrowing || root == null) return;

        float rate = GameManager.SeasonalGrowthRate;
        if (rate <= 0f) return;

        bool structureChanged = false;
        bool anyGrew          = false;

        // Snapshot growing nodes — we may add new ones during this loop
        var snapshot = new List<TreeNode>(allNodes.Count);
        foreach (var node in allNodes)
        {
            if (node.isGrowing && !node.isTrimmed)
                snapshot.Add(node);
        }

        // Log once per real second so we can see when/why growth stops
        debugLogTimer += Time.deltaTime;
        if (debugLogTimer >= 1f)
        {
            debugLogTimer = 0f;
            int maxNodeDepth = 0;
            foreach (var n in allNodes) if (n.depth > maxNodeDepth) maxNodeDepth = n.depth;
            Debug.Log($"[Tree] {GameManager.month}/{GameManager.day}/{GameManager.year} | " +
                      $"state=BranchGrow rate={rate:F2} | " +
                      $"growing={snapshot.Count} total={allNodes.Count} maxDepth={maxNodeDepth}");
        }

        foreach (var node in snapshot)
        {
            // TIMESCALE/24f converts real seconds → in-game days, so baseGrowSpeed is
            // in units/in-game-day and growth fills the same calendar span at any timescale.
            float inGameDays = Time.deltaTime * GameManager.TIMESCALE / 24f;
            float speed      = baseGrowSpeed
                             * rate
                             * Mathf.Pow(depthSpeedDecay, node.depth);

            node.length += speed * inGameDays;
            node.age    += inGameDays * rate;
            anyGrew      = true;

            if (node.length >= node.targetLength)
            {
                if (node.depth < SeasonDepthCap)
                {
                    // Reached target and below depth cap — spawn children, stop growing.
                    node.length    = node.targetLength;
                    node.isGrowing = false;
                    SpawnChildren(node);
                    structureChanged = true;
                }
                // else: at depth cap — keep growing past targetLength until dormancy.
                // Next spring when the cap increases, Update() will spawn children on
                // the first tick (length is already >= targetLength).
            }
        }

        if (structureChanged)
            RecalculateRadii(root);

        if (anyGrew)
            meshBuilder.SetDirty();
    }

    // ── Game State ────────────────────────────────────────────────────────────

    void OnGameStateChanged(GameState state)
    {
        if (state == GameState.Water && root == null)
            InitTree();

        isGrowing = (state == GameState.BranchGrow);
        Debug.Log($"[Tree] State → {state} | isGrowing={isGrowing} | year={GameManager.year} lastGrownYear={lastGrownYear}");

        // Start a new growing season once per calendar year.
        if (state == GameState.BranchGrow && root != null && GameManager.year > lastGrownYear)
        {
            lastGrownYear = GameManager.year;
            StartNewGrowingSeason();
        }
    }

    void StartNewGrowingSeason()
    {
        // Collect finished terminal nodes (last year's tips).
        // Nodes still mid-growth (node.isGrowing = true) will resume naturally via Update().
        var terminals = new List<TreeNode>();
        int resuming  = 0;
        foreach (var node in allNodes)
        {
            if (node.isGrowing && !node.isTrimmed) resuming++;
            if (!node.isTrimmed && node.isTerminal && !node.isGrowing && node.depth < SeasonDepthCap)
                terminals.Add(node);
        }

        Debug.Log($"[Tree] StartNewGrowingSeason year={GameManager.year} | depthCap={SeasonDepthCap} | terminals={terminals.Count} resuming={resuming} total={allNodes.Count}");

        foreach (var terminal in terminals)
        {
            float childLength = rootSegmentLength * Mathf.Pow(segmentLengthDecay, terminal.depth + 1);
            childLength = Mathf.Max(childLength, 0.3f);

            CreateNode(terminal.tipPosition, ContinuationDirection(terminal), terminalRadius, childLength, terminal);

            if (Random.value < springLateralChance)
            {
                float branchLength = childLength * 0.85f;
                CreateNode(terminal.tipPosition, LateralBranchDirection(terminal), terminalRadius, branchLength, terminal);
                GameManager.branches++;
            }
        }

        if (terminals.Count > 0)
        {
            RecalculateRadii(root);
            meshBuilder.SetDirty();
        }
    }

    // ── Year Simulation (debug keys 1–9) ──────────────────────────────────────

    /// <summary>
    /// Instantly simulates one full year of growth with no animation.
    /// Completes any in-progress segments, spawns new-season buds, then
    /// immediately completes those too. Press 1–9 to simulate that many years.
    /// </summary>
    public void SimulateYear()
    {
        if (root == null) return;

        // Advance the calendar by one year so the UI reflects the skip.
        GameManager.year++;
        lastGrownYear = GameManager.year;
        GameManager.Instance.TextCallFunction();

        // First: finish any segments that were mid-growth (interrupted by winter).
        foreach (var node in allNodes)
        {
            if (node.isGrowing && !node.isTrimmed)
            {
                node.length    = node.targetLength;
                node.isGrowing = false;
            }
        }

        // Spawn this season's new buds from all finished terminals.
        var terminals = new List<TreeNode>();
        foreach (var node in allNodes)
        {
            if (!node.isTrimmed && node.isTerminal && !node.isGrowing && node.depth < SeasonDepthCap)
                terminals.Add(node);
        }
        foreach (var terminal in terminals)
            SpawnChildren(terminal);

        // Run the full growth chain to completion — keep completing growing nodes
        // and spawning their children until nothing is left growing.
        // This simulates the entire spring-summer season in one shot.
        bool anyGrowing = true;
        while (anyGrowing)
        {
            anyGrowing = false;
            var growing = new List<TreeNode>();
            foreach (var node in allNodes)
                if (node.isGrowing && !node.isTrimmed) growing.Add(node);

            foreach (var node in growing)
            {
                anyGrowing     = true;
                node.length    = node.targetLength;
                node.isGrowing = false;
                if (node.depth < SeasonDepthCap)
                    SpawnChildren(node);
            }
        }

        RecalculateRadii(root);
        meshBuilder.SetDirty();
    }

    // ── Initialisation ────────────────────────────────────────────────────────

    void InitTree()
    {
        allNodes.Clear();
        nextId   = 0;
        startYear = GameManager.year;

        // Single root segment — all nodes start at terminalRadius; pipe model builds trunk thickness
        root = CreateNode(Vector3.zero, Vector3.up, terminalRadius, rootSegmentLength, null);

        meshBuilder.SetDirty();
    }

    // ── Node Factory ──────────────────────────────────────────────────────────

    public TreeNode CreateNode(Vector3 position, Vector3 direction, float radius, float targetLength, TreeNode parent)
    {
        int depth = parent == null ? 0 : parent.depth + 1;
        var node  = new TreeNode(nextId++, depth, position, direction, radius, targetLength, parent);

        if (parent != null)
            parent.children.Add(node);

        allNodes.Add(node);
        return node;
    }

    // ── Branching ─────────────────────────────────────────────────────────────

    void SpawnChildren(TreeNode node)
    {
        float childLength = rootSegmentLength * Mathf.Pow(segmentLengthDecay, node.depth + 1);
        childLength       = Mathf.Max(childLength, 0.3f); // never shorter than 0.3

        // All new nodes start at terminalRadius. RecalculateRadii() will set correct
        // parent radii bottom-up via the pipe model — no manual shrinking factors needed.

        // ── Apical continuation (always spawns) ───────────────────────────────
        CreateNode(node.tipPosition, ContinuationDirection(node), terminalRadius, childLength, node);

        // ── Lateral branch (depth-decayed) ────────────────────────────────────
        // Decay prevents exponential node explosion: ~75% at depth 0, ~26% at depth 10, ~9% at depth 20.
        float lateralChance = baseBranchChance * Mathf.Pow(branchChanceDepthDecay, node.depth);
        if (Random.value < lateralChance)
        {
            float branchLength = childLength * 0.85f;
            CreateNode(node.tipPosition, LateralBranchDirection(node), terminalRadius, branchLength, node);
            GameManager.branches++;
        }
    }

    // ── Direction Helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// The continuation direction blends parent inertia, phototropism, and a small
    /// random nudge. This gives each tree a unique silhouette while still growing
    /// generally upward.
    /// </summary>
    Vector3 ContinuationDirection(TreeNode node)
    {
        Vector3 sunDir = SunDirection();
        Vector3 rand   = Random.insideUnitSphere * randomWeight;
        Vector3 dir    = node.growDirection * inertiaWeight
                       + sunDir             * phototropismWeight
                       + rand;
        return dir.normalized;
    }

    /// <summary>
    /// Lateral branches splay outward from the parent direction with a random azimuth
    /// and an upward bias. Angle from parent axis is constrained to branchAngleMin..Max.
    /// </summary>
    Vector3 LateralBranchDirection(TreeNode node)
    {
        // Find a random perpendicular vector (random azimuth around parent axis)
        Vector3 perp = Vector3.Cross(node.growDirection, Random.insideUnitSphere).normalized;
        if (perp.sqrMagnitude < 0.001f)
            perp = Vector3.Cross(node.growDirection, Vector3.right).normalized;

        // Slerp from parent direction toward the perpendicular by the branch angle
        float   angle     = Random.Range(branchAngleMin, branchAngleMax);
        Vector3 branchDir = Vector3.Slerp(node.growDirection, perp, angle / 90f);

        // Add the same phototropism + random nudge as continuation
        branchDir = (branchDir + SunDirection() * phototropismWeight * 0.5f).normalized;
        return branchDir;
    }

    /// <summary>
    /// Direction toward the sun. Mostly up, with a slight push from the scene's
    /// directional light if available. Falls back to Vector3.up.
    /// </summary>
    Vector3 SunDirection()
    {
        // The GameManager's skyLight is a directional light; its -forward is "toward the sun"
        // We read it via the Light component cached on GameManager if accessible,
        // otherwise fall back to straight up.
        return Vector3.up;
        // Phase 2+ TODO: read skyLight.transform.forward and invert for a slight angle
    }

    // ── Pipe Model ────────────────────────────────────────────────────────────

    /// <summary>
    /// Recalculates all node radii bottom-up using da Vinci's pipe model:
    ///     parent.radius² = sum(child.radius²)
    ///
    /// Terminal radii are the source of truth — everything else derives from them.
    /// As the tree accumulates more branches, the trunk automatically thickens.
    /// </summary>
    public void RecalculateRadii(TreeNode node)
    {
        if (node.isTerminal) return;

        float sumOfSquares = 0f;
        foreach (var child in node.children)
        {
            RecalculateRadii(child);
            sumOfSquares += child.radius * child.radius;
        }
        node.radius = Mathf.Sqrt(sumOfSquares);
    }

    // ── Trimming ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Removes a node and all its descendants. Called by TreeInteraction (Phase 3).
    /// </summary>
    public void TrimNode(TreeNode node)
    {
        if (node == root)
        {
            Debug.LogWarning("TreeSkeleton: cannot trim the root node.");
            return;
        }

        node.parent?.children.Remove(node);
        RemoveSubtree(node);
        RecalculateRadii(root);
        meshBuilder.SetDirty();
    }

    void RemoveSubtree(TreeNode node)
    {
        foreach (var child in node.children)
            RemoveSubtree(child);

        node.isTrimmed = true;
        allNodes.Remove(node);
    }
}
