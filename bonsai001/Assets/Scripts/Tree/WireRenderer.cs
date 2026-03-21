using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Draws a copper-coloured helix LineRenderer on every wired branch.
/// Attach to the same GameObject as TreeSkeleton.
/// </summary>
[RequireComponent(typeof(TreeSkeleton))]
public class WireRenderer : MonoBehaviour
{
    [Header("Helix Shape")]
    [Tooltip("Number of full rotations the wire makes around the branch.")]
    [SerializeField] int   helixTurns    = 4;

    [Tooltip("Line sample points per full rotation. Higher = smoother helix.")]
    [SerializeField] int   pointsPerTurn = 12;

    [Tooltip("How far the helix centre sits from the branch surface.")]
    [SerializeField] float wireRadius    = 0.012f;

    [Header("Appearance")]
    [SerializeField] float lineWidth  = 0.010f;
    [SerializeField] Color wireColor  = new Color(0.72f, 0.45f, 0.20f); // copper

    // ── References ────────────────────────────────────────────────────────────

    TreeSkeleton skeleton;

    // nodeId → LineRenderer for that wired branch
    readonly Dictionary<int, LineRenderer> wireLines = new Dictionary<int, LineRenderer>();

    // ── Unity ─────────────────────────────────────────────────────────────────

    void Awake() => skeleton = GetComponent<TreeSkeleton>();

    void OnEnable()
    {
        if (skeleton != null) skeleton.OnSubtreeTrimmed += OnSubtreeTrimmed;
    }

    void OnDisable()
    {
        if (skeleton != null) skeleton.OnSubtreeTrimmed -= OnSubtreeTrimmed;
    }

    void OnSubtreeTrimmed(List<TreeNode> removed)
    {
        foreach (var node in removed)
            RemoveLine(node.id);
    }

    void Update()
    {
        if (skeleton.root == null) return;

        // Build set of currently-wired node ids
        var wiredIds = new HashSet<int>();
        foreach (var node in skeleton.allNodes)
        {
            if (!node.hasWire || node.isTrimmed) continue;
            wiredIds.Add(node.id);
            EnsureLine(node);
            UpdateHelix(node);
        }

        // Remove lines for nodes that are no longer wired
        var toRemove = new List<int>();
        foreach (var id in wireLines.Keys)
            if (!wiredIds.Contains(id)) toRemove.Add(id);

        foreach (var id in toRemove) RemoveLine(id);
    }

    // ── Line management ───────────────────────────────────────────────────────

    void EnsureLine(TreeNode node)
    {
        if (wireLines.ContainsKey(node.id)) return;

        var go = new GameObject($"_Wire_{node.id}");
        go.transform.SetParent(transform, false);

        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace         = false;
        lr.positionCount         = helixTurns * pointsPerTurn + 1;
        lr.startWidth            = lineWidth;
        lr.endWidth              = lineWidth;
        lr.material              = new Material(Shader.Find("Unlit/Color")) { color = wireColor };
        lr.shadowCastingMode     = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows        = false;

        wireLines[node.id] = lr;
    }

    void RemoveLine(int nodeId)
    {
        if (!wireLines.TryGetValue(nodeId, out var lr)) return;
        if (lr != null) Destroy(lr.gameObject);
        wireLines.Remove(nodeId);
    }

    // ── Helix geometry ────────────────────────────────────────────────────────

    void UpdateHelix(TreeNode node)
    {
        if (!wireLines.TryGetValue(node.id, out var lr) || lr == null) return;

        Vector3 axisUp = node.growDirection.normalized;

        // Stable perpendicular frame (parallel transport)
        Vector3 frameRight = Vector3.Cross(axisUp, Vector3.up);
        if (frameRight.sqrMagnitude < 0.001f)
            frameRight = Vector3.Cross(axisUp, Vector3.forward);
        frameRight = frameRight.normalized;
        Vector3 frameFwd = Vector3.Cross(frameRight, axisUp).normalized;

        int   totalPoints = helixTurns * pointsPerTurn + 1;
        float avgRadius   = Mathf.Lerp(node.radius, node.tipRadius, 0.5f);
        float helixR      = avgRadius + wireRadius;

        var positions = new Vector3[totalPoints];
        for (int i = 0; i < totalPoints; i++)
        {
            float   t      = (float)i / (totalPoints - 1);
            float   along  = t * node.length;
            float   angle  = t * helixTurns * Mathf.PI * 2f;
            Vector3 offset = (Mathf.Cos(angle) * frameRight + Mathf.Sin(angle) * frameFwd) * helixR;
            positions[i]   = node.worldPosition + axisUp * along + offset;
        }

        lr.SetPositions(positions);
    }
}
