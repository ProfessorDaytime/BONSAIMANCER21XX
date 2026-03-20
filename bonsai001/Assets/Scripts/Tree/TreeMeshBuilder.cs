using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reads the TreeSkeleton graph and builds ONE unified Mesh for the whole tree.
///
/// Why one mesh?
///   The old system gave each cone its own GameObject + Mesh. Unity lights each
///   mesh independently from its local origin, so normals restart at every junction
///   and you get visible seams. A single mesh means vertex normals at junctions are
///   shared across both the parent's top ring and the child's bottom ring — Unity's
///   RecalculateNormals() then averages all contributing face normals at those shared
///   vertices, producing smooth, continuous shading across the entire tree.
///
/// How the junction sharing works:
///   When we process a node, we generate its tip ring and store its start index.
///   Every child of that node is told "your base ring starts at index X" — the same
///   index the parent's tip ring used. The tip ring vertices are therefore referenced
///   by the parent's quad strip AND each child's quad strip. RecalculateNormals()
///   sees all contributing face normals and averages them, giving a smooth blend.
///
/// Mesh is only rebuilt when SetDirty() is called — not every frame — unless a node
/// is actively growing or bending (Phase 2/5 will call SetDirty each frame for those).
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class TreeMeshBuilder : MonoBehaviour
{
    // ── Settings ──────────────────────────────────────────────────────────────

    [Tooltip("Number of sides on each cylinder segment. 8 is fine for a bonsai.")]
    [SerializeField] int ringSegments = 8;

    // ── References ────────────────────────────────────────────────────────────

    TreeSkeleton  skeleton;
    Mesh          mesh;
    MeshCollider  meshCollider;
    bool          isDirty;

    // ── Build Buffers (reused each rebuild to avoid GC pressure) ──────────────

    readonly List<Vector3> vertices  = new List<Vector3>();
    readonly List<int>     triangles = new List<int>();
    readonly List<Vector2> uvs       = new List<Vector2>();

    // ── Triangle Range → Node mapping (for Phase 3 tool hit detection) ────────

    // Key:   first triangle INDEX (not count) in the triangles list for this node
    // Value: the TreeNode
    // After a ray cast hit, use RaycastHit.triangleIndex to look up which node was struck.
    public readonly List<(int triStart, int triEnd, TreeNode node)> triRanges
        = new List<(int, int, TreeNode)>();

    // ── Unity ─────────────────────────────────────────────────────────────────

    void Awake()
    {
        skeleton     = GetComponent<TreeSkeleton>();
        mesh         = new Mesh();
        mesh.name    = "BonsaiTree";
        GetComponent<MeshFilter>().mesh = mesh;
        meshCollider = GetComponent<MeshCollider>();

        if (skeleton == null)
            Debug.LogError("TreeMeshBuilder: no TreeSkeleton found on this GameObject.", this);
    }

    public void SetDirty() => isDirty = true;

    void LateUpdate()
    {
        if (!isDirty) return;
        BuildMesh();
        isDirty = false;
    }

    // ── Mesh Construction ─────────────────────────────────────────────────────

    void BuildMesh()
    {
        if (skeleton.root == null) return;

        vertices.Clear();
        triangles.Clear();
        uvs.Clear();
        triRanges.Clear();

        // Traverse the node graph depth-first.
        // -1 signals "generate a base ring for me" (only the root node needs this).
        ProcessNode(skeleton.root, -1, 0f, Vector3.zero);

        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetUVs(0, uvs);

        // This is the key call: because junction ring vertices are shared between
        // the parent's top quads and each child's bottom quads, Unity averages all
        // contributing face normals at each shared vertex — smooth shading for free.
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        // Refresh collider
        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = mesh;
    }

    /// <summary>
    /// Recursively processes one node:
    ///   1. Uses the provided base ring (or generates one for the root).
    ///   2. Generates the tip ring.
    ///   3. Connects them with quads.
    ///   4. Passes the tip ring index to each child as their base ring.
    ///
    /// Returns the vertex index at which this node's tip ring begins, so the
    /// caller (parent) can hand it off to sibling children.
    /// </summary>
    /// <param name="node">Node to process.</param>
    /// <param name="baseRingStart">
    ///   Index in the vertex list where this node's base ring starts.
    ///   Pass -1 for the root node; the method generates the ring itself.
    /// </param>
    /// <param name="cumulativeHeight">
    ///   Running world-height from the root, used for V coordinate so bark
    ///   texture tiles continuously up the trunk regardless of segment boundaries.
    /// </param>
    /// <param name="frameRight">
    ///   The axisRight used when the base ring was generated. Passed down so each
    ///   child can inherit the parent's ring orientation via parallel transport,
    ///   preventing the quad strips from twisting at junctions.
    ///   Pass Vector3.zero for the root — it computes a fresh frame.
    /// </param>
    int ProcessNode(TreeNode node, int baseRingStart, float cumulativeHeight, Vector3 frameRight)
    {
        // ── Resolve frame ────────────────────────────────────────────────────
        // Parallel-transport the inherited frameRight into this node's plane.
        // This keeps adjacent rings rotationally consistent, eliminating twist.

        Vector3 axisUp = node.growDirection.normalized;

        if (frameRight.sqrMagnitude > 0.001f)
        {
            // Re-orthogonalize: project out the component along this direction.
            frameRight = Vector3.ProjectOnPlane(frameRight, axisUp).normalized;
        }

        // Fallback (root, or degenerate after projection):
        if (frameRight.sqrMagnitude < 0.001f)
        {
            frameRight = Vector3.Cross(axisUp, Vector3.up);
            if (frameRight.sqrMagnitude < 0.001f)
                frameRight = Vector3.Cross(axisUp, Vector3.forward);
            frameRight = frameRight.normalized;
        }

        // ── Base ring ────────────────────────────────────────────────────────
        // Root generates its own; all other nodes inherit their parent's tip ring.

        if (baseRingStart < 0)
        {
            baseRingStart = vertices.Count;
            AddRing(node.worldPosition, axisUp, frameRight, node.radius, cumulativeHeight);
        }

        // ── Tip ring ─────────────────────────────────────────────────────────
        // Same frame as base (direction doesn't change within a node).

        float tipHeight    = cumulativeHeight + node.length;
        int   tipRingStart = vertices.Count;
        AddRing(node.tipPosition, axisUp, frameRight, node.tipRadius, tipHeight);

        // ── Quads ─────────────────────────────────────────────────────────────
        // Connect base ring to tip ring.  Two triangles per quad column.

        int triStart = triangles.Count;

        for (int i = 0; i < ringSegments; i++)
        {
            int b0 = baseRingStart + i;
            int b1 = baseRingStart + i + 1;
            int t0 = tipRingStart  + i;
            int t1 = tipRingStart  + i + 1;

            // Winding order: counter-clockwise when viewed from outside
            triangles.Add(b0); triangles.Add(t0); triangles.Add(b1);
            triangles.Add(b1); triangles.Add(t0); triangles.Add(t1);
        }

        triRanges.Add((triStart, triangles.Count, node));

        // ── Recurse into children ─────────────────────────────────────────────
        // Every child shares our tip ring as its base ring, and inherits frameRight
        // so their tip rings stay aligned with ours (parallel transport continues).

        foreach (var child in node.children)
            ProcessNode(child, tipRingStart, tipHeight, frameRight);

        return tipRingStart;
    }

    // ── Ring Generation ───────────────────────────────────────────────────────

    /// <summary>
    /// Appends (ringSegments + 1) vertices forming a circle perpendicular to
    /// <paramref name="direction"/> at <paramref name="center"/> with the given radius.
    ///
    /// The +1 duplicates the first vertex at the end with U=1.0 so UV mapping
    /// has a clean seam rather than a hard wrap from 1 back to 0.
    /// </summary>
    void AddRing(Vector3 center, Vector3 axisUp, Vector3 axisRight, float radius, float heightV)
    {
        // axisUp and axisRight are already normalised and orthogonal (done in ProcessNode).
        Vector3 axisFwd = Vector3.Cross(axisRight, axisUp).normalized;

        for (int i = 0; i <= ringSegments; i++)
        {
            float t     = (float)i / ringSegments;
            float angle = t * Mathf.PI * 2f;
            float cos   = Mathf.Cos(angle);
            float sin   = Mathf.Sin(angle);

            Vector3 offset = (axisRight * cos + axisFwd * sin) * radius;
            vertices.Add(center + offset);

            // U = angle around circumference (0..1)
            // V = cumulative height from root * bark tile scale
            uvs.Add(new Vector2(t, heightV * 0.4f));
        }
    }

    // ── Public API (Phase 3 tool interaction) ─────────────────────────────────

    /// <summary>
    /// Given a triangle index from a RaycastHit, returns the TreeNode that
    /// owns that triangle. Returns null if not found.
    /// </summary>
    public TreeNode NodeFromTriangleIndex(int triangleIndex)
    {
        // triRanges stores [triStart, triEnd) in terms of INDEX into triangles list.
        // RaycastHit.triangleIndex is the triangle number (0-based), so multiply by 3
        // to get the index into the triangles array.
        int idx = triangleIndex * 3;
        foreach (var (triStart, triEnd, node) in triRanges)
        {
            if (idx >= triStart && idx < triEnd)
                return node;
        }
        return null;
    }
}
