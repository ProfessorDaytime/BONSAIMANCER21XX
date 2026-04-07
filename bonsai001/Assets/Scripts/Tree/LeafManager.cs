using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages the full leaf lifecycle:
///   Spring  (BranchGrow)  — spawns leaf clusters on eligible terminal nodes
///   Summer               — maintains cluster, updates colour
///   Autumn  (LeafFall)   — stochastically drops leaves day by day
///   Cleanup              — removes leaves from nodes that gained children or were trimmed
///
/// Leaves are instances of leafPrefab parented to the tree transform in local space.
/// All leaves share one Material instance so the seasonal colour update is O(1).
/// </summary>
public class LeafManager : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Tooltip("The leaf prefab to instantiate. Assign the Japanese Maple Leaf prefab.")]
    [SerializeField] GameObject leafPrefab;

    [Tooltip("Only terminal nodes at this depth or deeper get leaves (no leaves on trunk/scaffold).")]
    [SerializeField] int minLeafDepth = 1;

    [Tooltip("How many leaves per terminal node.")]
    [SerializeField] [Range(2, 10)] int leavesPerNode = 5;

    [Tooltip("Radius of the random scatter cluster around the node tip.")]
    [SerializeField] float clusterRadius = 0.18f;

    [Tooltip("Species-default world-space scale for leaves. Season scale is computed from this.")]
    [SerializeField] float baseLeafScale = 0.25f;

    [Tooltip("How much full pot-bound pressure shrinks leaves. 0 = no effect, 1 = shrinks to 40% of base.")]
    [SerializeField] [Range(0f, 1f)] float rootPressureLeafShrink = 1f;

    [Tooltip("How much full refinement level shrinks leaves. 0 = no effect, 1 = shrinks to 55% of base.")]
    [SerializeField] [Range(0f, 1f)] float refinementLeafShrink = 1f;

    [Tooltip("Probability per leaf per in-game day of falling during LeafFall state.")]
    [SerializeField] float baseFallChancePerDay = 0.15f;

    // ── References ────────────────────────────────────────────────────────────

    TreeSkeleton skeleton;

    // ── State ─────────────────────────────────────────────────────────────────

    // node.id → list of live leaf GameObjects
    readonly Dictionary<int, List<GameObject>> nodeLeaves = new Dictionary<int, List<GameObject>>();

    // Flat working list rebuilt when the dict changes — avoids dict enumeration in Update
    readonly List<(int nodeId, GameObject go)> allLeaves  = new List<(int, GameObject)>();
    bool listDirty = false;

    // Computed once each spring from root pressure + refinement + defoliation history.
    // All leaves spawned this season use this value so mid-season size never changes.
    float seasonLeafScale = 0.25f;

    // 0 → 1: set externally by the defoliation tool (not yet built); decays each season.
    public float defoliationFactor = 0f;

    bool isLeafFall      = false;
    bool isGrowingSeason = false;

    int lastSpringYear = -1;

    float fallDebugTimer = 0f;

    // Shared material — one colour update drives all leaves
    Material leafMat;

    // ── Unity ─────────────────────────────────────────────────────────────────

    void Awake()
    {
        skeleton = GetComponent<TreeSkeleton>();

        if (leafPrefab == null)
        {
            Debug.LogError("LeafManager: leafPrefab is not assigned.", this);
            return;
        }

        // Create a shared material from the prefab's renderer so we own it
        var srcRenderer = leafPrefab.GetComponentInChildren<Renderer>();
        if (srcRenderer != null)
            leafMat = new Material(srcRenderer.sharedMaterial);
        else
        {
            leafMat = new Material(Shader.Find("Unlit/Color"));
            Debug.LogWarning("LeafManager: leaf prefab has no Renderer — using fallback unlit material.");
        }
    }

    void OnEnable()
    {
        GameManager.OnGameStateChanged += OnGameStateChanged;
        if (skeleton != null) skeleton.OnSubtreeTrimmed += OnSubtreeTrimmed;
    }

    void OnDisable()
    {
        GameManager.OnGameStateChanged -= OnGameStateChanged;
        if (skeleton != null) skeleton.OnSubtreeTrimmed -= OnSubtreeTrimmed;
    }

    void OnSubtreeTrimmed(List<TreeNode> removedNodes)
    {
        foreach (var node in removedNodes)
        {
            if (!nodeLeaves.TryGetValue(node.id, out var leaves)) continue;

            foreach (var go in leaves)
                if (go != null) go.GetComponent<Leaf>()?.StartFalling();

            nodeLeaves.Remove(node.id);
            listDirty = true;
        }
    }

    void OnGameStateChanged(GameState state)
    {
        isLeafFall      = (state == GameState.LeafFall);
        isGrowingSeason = (state == GameState.BranchGrow);

        if (state == GameState.BranchGrow && GameManager.year > lastSpringYear)
        {
            lastSpringYear = GameManager.year;
            defoliationFactor = Mathf.Max(0f, defoliationFactor - 0.2f);  // decays ~1 full defoliation per 5 seasons
            ComputeSeasonLeafScale();
            CleanupOrphanedLeaves();
            SpawnSpringLeaves();
        }

        if (state == GameState.LeafFall)
        {
            if (listDirty) RebuildFlatList();
            Debug.Log($"[Leaves] LeafFall started | nodeLeaves={nodeLeaves.Count} allLeaves={allLeaves.Count}");

            // Give every live leaf a randomised fall-colour speed.
            // Guard IsInFallSeason so restoring LeafFall (e.g. after wiring) doesn't
            // reset the colour progress on leaves that are already turning.
            foreach (var kvp in nodeLeaves)
                foreach (var go in kvp.Value)
                {
                    if (go == null) continue;
                    var leaf = go.GetComponent<Leaf>();
                    if (leaf != null && !leaf.IsInFallSeason)
                        leaf.StartLeafFallSeason(Random.Range(0.4f, 2.2f));
                }

            fallDebugTimer = 0f;
        }
    }

    void Update()
    {
        if (leafMat == null) return;

        // Rebuild first so RollLeafFall always sees a current list
        if (listDirty)
            RebuildFlatList();

        // Check for new terminal nodes created mid-season (chain propagation).
        // SpawnSpringLeaves skips nodes already in the dict, so this is safe every frame.
        if (isGrowingSeason)
            SpawnSpringLeaves();

        if (isLeafFall)
        {
            fallDebugTimer += Time.deltaTime;
            if (fallDebugTimer >= 1f)
            {
                fallDebugTimer = 0f;
                float prog = 0f;
                if (allLeaves.Count > 0)
                {
                    var (_, sampleGo) = allLeaves[0];
                    if (sampleGo != null) prog = sampleGo.GetComponent<Leaf>()?.FallColorProgress ?? 0f;
                }
                Debug.Log($"[Leaves] LeafFall tick | allLeaves={allLeaves.Count} nodeLeaves={nodeLeaves.Count} sampleProgress={prog:F2}");
            }
            RollLeafFall();
        }
    }

    // ── Season leaf scale ─────────────────────────────────────────────────────

    /// <summary>
    /// Computes this season's leaf size from three independent miniaturization factors.
    /// Called once at the start of BranchGrow; all leaves spawned this season use the result.
    ///
    ///   rootPressureFactor   — averaged boundaryPressure on root terminals, from TreeSkeleton
    ///   treeRefinementFactor — averaged refinementLevel on branch terminals, normalized to [0,1]
    ///   defoliationFactor    — set externally by the defoliation tool; decays each spring
    ///
    /// Each factor is independent and multiplicative. A fully pot-bound, well-refined,
    /// recently defoliated tree gets all three discounts simultaneously.
    /// </summary>
    void ComputeSeasonLeafScale()
    {
        float rootPressure   = skeleton.RootPressureFactor();
        float refinement     = TreeRefinementFactor();

        float scale = baseLeafScale
            * Mathf.Lerp(1f, 0.40f, rootPressure   * rootPressureLeafShrink)
            * Mathf.Lerp(1f, 0.55f, refinement      * refinementLeafShrink)
            * Mathf.Lerp(1f, 0.60f, defoliationFactor);

        seasonLeafScale = scale;
        Debug.Log($"[Leaves] seasonLeafScale={scale:F3} (base={baseLeafScale:F3} rootP={rootPressure:F2} refine={refinement:F2} defo={defoliationFactor:F2})");
    }

    /// <summary>Average refinementLevel across live branch terminals, normalized to [0,1].</summary>
    float TreeRefinementFactor()
    {
        float sum = 0f; int count = 0;
        foreach (var node in skeleton.allNodes)
        {
            if (node.isRoot || node.isTrimmed || !node.isTerminal) continue;
            sum += node.refinementLevel;
            count++;
        }
        if (count == 0) return 0f;
        float cap = skeleton.RefinementCap;
        return Mathf.Clamp01(sum / count / Mathf.Max(1f, cap));
    }

    // ── Spawning ──────────────────────────────────────────────────────────────

    void SpawnSpringLeaves()
    {
        if (leafPrefab == null || skeleton.root == null) return;

        int spawned = 0;
        foreach (var node in skeleton.allNodes)
        {
            if (node.isTrimmed)            continue;
            if (!node.isTerminal)          continue;
            if (node.isRoot)               continue;  // roots never get leaves
            if (node.depth < minLeafDepth) continue;
            // subdivisionsLeft is intentionally NOT checked: leaves appear on all growing
            // tips, not just the end of a chain. Interior clusters are cleaned up at next
            // spring by CleanupOrphanedLeaves when those nodes gain children.
            if (nodeLeaves.ContainsKey(node.id)) continue;  // already has leaves

            // Bud gate: old-wood nodes only get leaves if they set a bud last autumn.
            // Nodes born this spring (birthYear == current year) get leaves immediately
            // since they are actively extending shoots — no bud needed.
            bool isNewThisSpring = node.birthYear == GameManager.year;
            if (!isNewThisSpring && !node.hasBud) continue;

            SpawnCluster(node);
            spawned++;
        }

        if (spawned > 0)
            Debug.Log($"[Leaves] SpawnSpringLeaves spawned={spawned} nodeLeaves={nodeLeaves.Count} year={GameManager.year}");
    }

    void SpawnCluster(TreeNode node)
    {
        var list = new List<GameObject>(leavesPerNode);

        for (int i = 0; i < leavesPerNode; i++)
        {
            // Random scatter offset in skeleton-local space, stored so Leaf.Update()
            // can reapply it each frame and track the node when wire-bending moves it.
            Vector3    offset   = Random.insideUnitSphere * clusterRadius;
            Quaternion rot      = Random.rotation;

            var go = Instantiate(leafPrefab, skeleton.transform);
            go.transform.localPosition = node.tipPosition + offset;
            go.transform.rotation      = rot;
            go.transform.localScale    = Vector3.zero;  // Leaf.Update() scales it in

            // Apply shared material to all renderers in the prefab hierarchy
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                r.sharedMaterial = leafMat;

            // Use existing Leaf component if the prefab already has one (avoids duplicate
            // components fighting each other — the second one would reset localPosition every frame)
            var leaf          = go.GetComponent<Leaf>() ?? go.AddComponent<Leaf>();
            leaf.ownerNode    = node;
            leaf.tipOffset    = offset;
            leaf.targetScale  = Vector3.one * seasonLeafScale;

            list.Add(go);
        }

        nodeLeaves[node.id] = list;
        listDirty = true;
    }

    /// <summary>
    /// Computes the tree's photosynthetic energy from the current canopy.
    /// Called at bud-set (end of summer) so it reflects the peak-season canopy.
    /// Returns a value clamped to [0, maxMultiplier]:
    ///   0   = no leaves at all
    ///   1.0 = full healthy canopy (all potential terminals have leaves at full health)
    ///   >1  = extra-lush bonus, capped at maxMultiplier
    /// </summary>
    public float ComputeTreeEnergy(List<TreeNode> allNodes, float maxMultiplier)
    {
        float potential = 0f;
        float actual    = 0f;

        foreach (var node in allNodes)
        {
            if (node.isRoot || node.isTrimmed || !node.isTerminal) continue;
            if (node.depth < minLeafDepth)    continue;
            if (node.subdivisionsLeft > 0)    continue;

            potential += leavesPerNode * baseLeafScale * baseLeafScale;

            if (nodeLeaves.TryGetValue(node.id, out var leaves))
                actual += leaves.Count * seasonLeafScale * seasonLeafScale * Mathf.Clamp01(node.health);
        }

        if (potential <= 0f) return 1f;  // seedling with no leaves yet — grow at default rate
        return Mathf.Clamp(actual / potential, 0f, maxMultiplier);
    }

    // ── Defoliation ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the given node currently has a live leaf cluster.
    /// Used by TreeInteraction to filter hover candidates.
    /// </summary>
    public bool NodeHasLeaves(int nodeId) => nodeLeaves.ContainsKey(nodeId);

    /// <summary>
    /// Strips the leaf cluster from a single node (fall animation).
    /// Increases defoliationFactor proportionally and marks ancestors for back-budding.
    /// </summary>
    public void DefoliateNode(TreeNode node)
    {
        if (!nodeLeaves.ContainsKey(node.id)) return;

        // Strip leaves with fall animation
        StartFallingCluster(node.id);
        node.hasLeaves = false;

        // Stimulate back-buds on up to 2 ancestors (same as pinch)
        TreeNode ancestor = node.parent;
        for (int i = 0; i < 2 && ancestor != null; i++)
        {
            ancestor.backBudStimulated = true;
            ancestor = ancestor.parent;
        }

        // Bump defoliationFactor — proportional to fraction of leaf nodes stripped
        int total = 0;
        foreach (var n in skeleton.allNodes)
            if (!n.isRoot && !n.isTrimmed && n.isTerminal) total++;
        defoliationFactor = Mathf.Clamp01(defoliationFactor + (total > 0 ? 1f / total : 0.1f));

        Debug.Log($"[Defoliate] Stripped node={node.id} | defoliationFactor={defoliationFactor:F2}");
    }

    /// <summary>
    /// Strips all leaf clusters from the tree at once.
    /// Sets defoliationFactor to 1.0 (maximum miniaturization effect next spring).
    /// </summary>
    public void DefoliateAll()
    {
        var ids = new List<int>(nodeLeaves.Keys);
        foreach (int id in ids)
            StartFallingCluster(id);

        // Clear hasLeaves on all branch nodes
        foreach (var node in skeleton.allNodes)
        {
            if (!node.isRoot) node.hasLeaves = false;
            // Back-bud stimulation on all non-root nodes
            if (!node.isRoot && !node.isTrimmed)
                node.backBudStimulated = true;
        }

        defoliationFactor = 1f;
        Debug.Log($"[Defoliate] Full defoliation | defoliationFactor=1.0 | year={GameManager.year} month={GameManager.month}");
    }

    /// <summary>
    /// Destroys all live leaf GameObjects and clears the tracking dictionaries.
    /// Called by SaveManager.Load() before re-spawning leaves from saved data.
    /// </summary>
    /// <summary>Instantly removes all leaves — called on tree death.</summary>
    public void DropAllLeaves() => ClearAllLeaves();

    public void ClearAllLeaves()
    {
        foreach (var kvp in nodeLeaves)
            foreach (var go in kvp.Value)
                if (go != null) Destroy(go);
        nodeLeaves.Clear();
        allLeaves.Clear();
        listDirty = false;
    }

    /// <summary>
    /// Updates each live leaf's fungal severity based on its owner node's fungalLoad,
    /// then forces a colour refresh. Call from buttonClicker or TreeSkeleton each season.
    /// Leaves in fall season are not affected (fall gradient takes priority).
    /// </summary>
    public void RefreshFungalTint(TreeSkeleton skel)
    {
        if (skel == null) return;
        // Build a quick id→node lookup
        var nodeById = new Dictionary<int, TreeNode>(skel.allNodes.Count);
        foreach (var n in skel.allNodes) nodeById[n.id] = n;

        foreach (var (nodeId, go) in allLeaves)
        {
            if (go == null) continue;
            var leaf = go.GetComponent<Leaf>();
            if (leaf == null) continue;
            if (nodeById.TryGetValue(nodeId, out var node))
            {
                leaf.fungalSeverity = node.fungalLoad;
                leaf.ForceRefreshColor();
            }
        }
    }

    /// <summary>
    /// Spawns leaf clusters on the given nodes unconditionally — no bud check.
    /// Used by the trim-undo system to restore leaves after a undo without waiting
    /// for the next growing season.
    /// </summary>
    public void ForceSpawnLeaves(List<TreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.isTrimmed)                    continue;
            if (node.isRoot)                       continue;
            if (node.depth < minLeafDepth)         continue;
            if (nodeLeaves.ContainsKey(node.id))   continue;
            SpawnCluster(node);
        }
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Removes leaf clusters from nodes that:
    ///   - Were trimmed (node.isTrimmed)
    ///   - Gained children (no longer terminal)
    /// Called at the start of each growing season.
    /// </summary>
    void CleanupOrphanedLeaves()
    {
        // Only remove leaves from trimmed or fully removed nodes.
        // Nodes that simply gained children this spring keep their leaves and
        // drop them naturally in LeafFall — removing them here would race with
        // TreeSkeleton.StartNewGrowingSeason (which runs first) and cause the
        // "ungrows last segment" visual glitch.
        var liveUntrimmed = new HashSet<int>();
        foreach (var node in skeleton.allNodes)
            if (!node.isTrimmed) liveUntrimmed.Add(node.id);

        var toRemove = new List<int>();
        foreach (var kvp in nodeLeaves)
            if (!liveUntrimmed.Contains(kvp.Key))
                toRemove.Add(kvp.Key);

        foreach (int id in toRemove)
            StartFallingCluster(id);
    }

    // Starts the fall animation on all leaves in a cluster, then removes the cluster
    // from tracking so it is no longer managed. Each Leaf self-destructs after falling.
    void StartFallingCluster(int nodeId)
    {
        if (!nodeLeaves.TryGetValue(nodeId, out var list)) return;

        foreach (var go in list)
            if (go != null) go.GetComponent<Leaf>()?.StartFalling();

        nodeLeaves.Remove(nodeId);
        listDirty = true;
    }


    // ── Leaf fall ─────────────────────────────────────────────────────────────

    void RollLeafFall()
    {
        if (allLeaves.Count == 0) return;

        float inGameDays = Time.deltaTime * GameManager.TIMESCALE / 24f;

        // Iterate backward so we can remove mid-loop
        for (int i = allLeaves.Count - 1; i >= 0; i--)
        {
            var (nodeId, go) = allLeaves[i];
            if (go == null) { allLeaves.RemoveAt(i); continue; }

            var leaf = go.GetComponent<Leaf>();
            if (leaf == null) { allLeaves.RemoveAt(i); continue; }

            // Lazy fallback: if StartLeafFallSeason was missed, start it now
            if (!leaf.IsInFallSeason)
                leaf.StartLeafFallSeason(Random.Range(0.4f, 2.2f));

            // Fall chance scales with color progress:
            //   green  (0) → 0.2x base   (rare but possible)
            //   brown  (1) → 3.0x base   (very likely)
            float chance = baseFallChancePerDay * inGameDays * Mathf.Lerp(0.2f, 3f, leaf.FallColorProgress);

            if (Random.value < chance)
            {
                Debug.Log($"[Leaves] Leaf falling! progress={leaf.FallColorProgress:F2} chance={chance:F4}");
                leaf.StartFalling();

                if (nodeLeaves.TryGetValue(nodeId, out var list))
                {
                    list.Remove(go);
                    if (list.Count == 0)
                        nodeLeaves.Remove(nodeId);
                }

                allLeaves.RemoveAt(i);
            }
        }
    }

    // ── Flat list helper ──────────────────────────────────────────────────────

    void RebuildFlatList()
    {
        allLeaves.Clear();
        foreach (var kvp in nodeLeaves)
            foreach (var go in kvp.Value)
                if (go != null) allLeaves.Add((kvp.Key, go));

        listDirty = false;
    }
}
