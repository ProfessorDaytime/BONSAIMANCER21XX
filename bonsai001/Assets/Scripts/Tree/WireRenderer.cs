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
    [SerializeField] float lineWidth = 0.010f;
    // Wire colour is driven by set/damage progress — no static colour field needed.

    // ── References ────────────────────────────────────────────────────────────

    TreeSkeleton    skeleton;
    TreeMeshBuilder meshBuilder;   // for NodeSwayFlex — the wire must bend with the bark

    // nodeId → LineRenderer for that wired branch
    readonly Dictionary<int, LineRenderer> wireLines = new Dictionary<int, LineRenderer>();

    // ── Unity ─────────────────────────────────────────────────────────────────

    void Awake()
    {
        skeleton    = GetComponent<TreeSkeleton>();
        meshBuilder = GetComponent<TreeMeshBuilder>();
    }

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
        lr.material              = new Material(Shader.Find("Unlit/Color"));
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

    // ── Wire colour ───────────────────────────────────────────────────────────

    /// <summary>
    /// Silver while setting → obvious gold pulse when fully set → orange → deep red.
    /// </summary>
    static Color WireColor(TreeNode node)
    {
        if (node.wireSetProgress < 1f)
        {
            // Silver, brightening slightly as it sets
            return Color.Lerp(
                new Color(0.55f, 0.55f, 0.60f),
                new Color(0.80f, 0.80f, 0.85f),
                node.wireSetProgress);
        }

        if (node.wireDamageProgress < 0.01f)
        {
            // Obvious gold pulse: ready to remove
            float pulse = (Mathf.Sin(Time.time * 4f) + 1f) * 0.5f;
            return Color.Lerp(new Color(0.85f, 0.68f, 0.05f), new Color(1.0f, 0.92f, 0.20f), pulse);
        }

        if (node.wireDamageProgress < 0.5f)
        {
            // Gold → orange
            return Color.Lerp(
                new Color(0.95f, 0.72f, 0.05f),
                new Color(0.92f, 0.38f, 0.04f),
                node.wireDamageProgress * 2f);
        }

        // Orange → deep red
        return Color.Lerp(
            new Color(0.92f, 0.38f, 0.04f),
            new Color(0.50f, 0.02f, 0.02f),
            (node.wireDamageProgress - 0.5f) * 2f);
    }

    // ── Helix geometry ────────────────────────────────────────────────────────

    void UpdateHelix(TreeNode node)
    {
        if (!wireLines.TryGetValue(node.id, out var lr) || lr == null) return;

        lr.material.color = WireColor(node);

        Vector3 axisUp = node.growDirection.normalized;

        // Stable perpendicular frame (parallel transport)
        Vector3 frameRight = Vector3.Cross(axisUp, Vector3.up);
        if (frameRight.sqrMagnitude < 0.001f)
            frameRight = Vector3.Cross(axisUp, Vector3.forward);
        frameRight = frameRight.normalized;
        Vector3 frameFwd = Vector3.Cross(frameRight, axisUp).normalized;

        // Wrap count scales with segment length (one wrap ≈ 0.22 units) — a fixed 4
        // turns over the post-stretch 2.4-unit chords degenerated into a near-straight
        // line beside the branch. And the helix radius TAPERS with the wood: the old
        // constant average radius buried the coil at the thick end and floated it off
        // the thin end — the "double outline" strokes (2026-07-05).
        int turns = Mathf.Clamp(Mathf.RoundToInt(node.length / 0.22f), 3, 24);
        int totalPoints = turns * pointsPerTurn + 1;
        if (lr.positionCount != totalPoints) lr.positionCount = totalPoints;

        // Ride the bark's vertex-shader sway: the wire wraps a branch that now bends, so the
        // whole helix takes the node's sway displacement (one sample — the segment is short) or
        // the copper visibly floats beside the bark in wind. AutoStyler wires YOUNG shoots, and
        // young wood is exactly where flex is highest. WindSway is the CPU mirror of the shader.
        Vector3 swayLocal = Vector3.zero;
        if (meshBuilder != null && WindManager.CanonStrength > 0f)
        {
            float flex = meshBuilder.NodeSwayFlex(node);
            if (flex > 0f)
            {
                Vector3 restW = transform.TransformPoint(node.tipPosition);
                swayLocal = transform.InverseTransformVector(WindSway.Sway(restW, flex));
            }
        }

        var positions = new Vector3[totalPoints];
        for (int i = 0; i < totalPoints; i++)
        {
            float   t      = (float)i / (totalPoints - 1);
            float   along  = t * node.length;
            float   angle  = t * turns * Mathf.PI * 2f;
            float   helixR = Mathf.Lerp(node.radius, node.tipRadius, t) + wireRadius;
            Vector3 offset = (Mathf.Cos(angle) * frameRight + Mathf.Sin(angle) * frameFwd) * helixR;
            // Sway eases in from the base ring to the tip so the wire's root stays welded to
            // the stiffer parent wood, matching how the bark's own flex grows along the segment.
            positions[i]   = node.worldPosition + axisUp * along + offset + swayLocal * t;
        }

        lr.SetPositions(positions);
    }
}
