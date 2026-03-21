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
    [SerializeField] int minLeafDepth = 2;

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

        if (state == GameState.BranchGrow)
        {
            CleanupOrphanedLeaves();
            SpawnSpringLeaves();
        }
    }

    void Update()
    {
        if (leafMat == null) return;

        UpdateLeafColour();

        // Check for new terminal nodes created mid-season (chain propagation).
        // SpawnSpringLeaves skips nodes already in the dict, so this is safe every frame.
        if (isGrowingSeason)
            SpawnSpringLeaves();

        if (isLeafFall)
            RollLeafFall();

        if (listDirty)
            RebuildFlatList();
    }

    // ── Spawning ──────────────────────────────────────────────────────────────

    void SpawnSpringLeaves()
    {
        if (leafPrefab == null || skeleton.root == null) return;

        foreach (var node in skeleton.allNodes)
        {
            if (node.isTrimmed)           continue;
            if (!node.isTerminal)         continue;
            if (node.depth < minLeafDepth) continue;
            if (nodeLeaves.ContainsKey(node.id)) continue;  // already has leaves

            SpawnCluster(node);
        }
    }

    void SpawnCluster(TreeNode node)
    {
        var list = new List<GameObject>(leavesPerNode);

        for (int i = 0; i < leavesPerNode; i++)
        {
            // Random offset in a sphere around the tip
            Vector3 localPos = node.tipPosition + Random.insideUnitSphere * clusterRadius;
            Quaternion rot   = Random.rotation;

            var go = Instantiate(leafPrefab, skeleton.transform);
            go.transform.localPosition = localPos;
            go.transform.rotation      = rot;
            go.transform.localScale    = Vector3.zero;  // Leaf.Update() scales it in

            // Apply shared material to all renderers in the prefab hierarchy
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                r.sharedMaterial = leafMat;

            var leaf          = go.AddComponent<Leaf>();
            leaf.ownerNode    = node;
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
        // Build a quick lookup of current valid node ids
        var validTerminals = new HashSet<int>();
        foreach (var node in skeleton.allNodes)
        {
            if (!node.isTrimmed && node.isTerminal)
                validTerminals.Add(node.id);
        }

        var toRemove = new List<int>();
        foreach (var kvp in nodeLeaves)
        {
            if (!validTerminals.Contains(kvp.Key))
                toRemove.Add(kvp.Key);
        }

        foreach (int id in toRemove)
            DestroyCluster(id);
    }

    void DestroyCluster(int nodeId)
    {
        if (!nodeLeaves.TryGetValue(nodeId, out var list)) return;

        foreach (var go in list)
            if (go != null) Destroy(go);

        nodeLeaves.Remove(nodeId);
        listDirty = true;
    }

    // ── Colour ────────────────────────────────────────────────────────────────

    void UpdateLeafColour()
    {
        float hue   = GameManager.LeafHue;
        // HSV: 0.33 = green, 0.0 = red
        Color color = Color.HSVToRGB(Mathf.Lerp(0.33f, 0f, hue), 0.85f, 0.75f);
        leafMat.color = color;
    }

    // ── Leaf fall ─────────────────────────────────────────────────────────────

    void RollLeafFall()
    {
        if (allLeaves.Count == 0) return;

        float inGameDays = Time.deltaTime * GameManager.TIMESCALE / 24f;
        float chance     = baseFallChancePerDay * inGameDays;

        // Iterate backward so we can remove mid-loop
        for (int i = allLeaves.Count - 1; i >= 0; i--)
        {
            var (nodeId, go) = allLeaves[i];
            if (go == null) { allLeaves.RemoveAt(i); continue; }

            if (Random.value < chance)
            {
                go.GetComponent<Leaf>()?.StartFalling();

                // Remove from dict
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
