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

    [Tooltip("World-space scale applied to every leaf instance.")]
    [SerializeField] float leafScale = 0.25f;

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

    // ── Spawning ──────────────────────────────────────────────────────────────

    void SpawnSpringLeaves()
    {
        if (leafPrefab == null || skeleton.root == null) return;

        foreach (var node in skeleton.allNodes)
        {
            if (node.isTrimmed)            continue;
            if (!node.isTerminal)          continue;
            if (node.isRoot)               continue;  // roots never get leaves
            if (node.depth < minLeafDepth) continue;
            if (node.subdivisionsLeft > 0) continue;  // mid-chain sub-segment — will spawn more before branching
            if (nodeLeaves.ContainsKey(node.id)) continue;  // already has leaves

            SpawnCluster(node);
        }
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
            leaf.targetScale  = Vector3.one * leafScale;

            list.Add(go);
        }

        nodeLeaves[node.id] = list;
        listDirty = true;
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
