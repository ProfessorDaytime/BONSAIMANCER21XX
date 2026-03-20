using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Handles all player interaction with the tree mesh:
///   - Hover: highlights the hovered node and all its descendants in red
///   - Click: trims the highlighted subtree via TreeSkeleton.TrimNode()
///
/// Requires TreeSkeleton and TreeMeshBuilder on the same GameObject,
/// and a MeshCollider (already added by TreeMeshBuilder's RequireComponent).
///
/// The highlight is a separate child GameObject with its own mesh, built
/// from the same ring math as TreeMeshBuilder but with a slightly inflated
/// radius so it renders on top without z-fighting.
/// </summary>
[RequireComponent(typeof(TreeSkeleton), typeof(TreeMeshBuilder))]
public class TreeInteraction : MonoBehaviour
{
    [Tooltip("Must match TreeMeshBuilder.ringSegments so faceting aligns.")]
    [SerializeField] int ringSegments = 8;

    [Tooltip("How much larger the highlight mesh is relative to the branch radius. " +
             "Prevents z-fighting with the main mesh.")]
    [SerializeField] float highlightRadiusBias = 1.04f;

    // ── References ────────────────────────────────────────────────────────────

    TreeSkeleton    skeleton;
    TreeMeshBuilder meshBuilder;
    Camera          cam;

    // ── Highlight overlay ─────────────────────────────────────────────────────

    MeshFilter   highlightFilter;
    MeshRenderer highlightRenderer;
    Mesh         highlightMesh;

    // Reused every rebuild — avoids GC allocations on hover
    readonly List<Vector3> hVerts = new List<Vector3>();
    readonly List<int>     hTris  = new List<int>();
    readonly List<Vector2> hUVs   = new List<Vector2>();

    // ── State ─────────────────────────────────────────────────────────────────

    TreeNode hoveredNode;

    // ── Unity ─────────────────────────────────────────────────────────────────

    void Awake()
    {
        skeleton    = GetComponent<TreeSkeleton>();
        meshBuilder = GetComponent<TreeMeshBuilder>();
        cam         = Camera.main;

        // Child GameObject for the red highlight overlay
        var go = new GameObject("_TreeHighlight");
        go.transform.SetParent(transform, false);

        highlightFilter   = go.AddComponent<MeshFilter>();
        highlightRenderer = go.AddComponent<MeshRenderer>();

        // Red unlit material — created at runtime so no material asset is needed
        var mat = new Material(Shader.Find("Unlit/Color"));
        mat.color = new Color(0.9f, 0.1f, 0.1f);
        highlightRenderer.material  = mat;
        highlightRenderer.enabled   = false;

        highlightMesh      = new Mesh { name = "HighlightMesh" };
        highlightFilter.mesh = highlightMesh;
    }

    void Update()
    {
        if (!GameManager.canTrim || skeleton.root == null)
        {
            SetHovered(null);
            return;
        }

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit) && hit.collider.gameObject == gameObject)
        {
            TreeNode node = meshBuilder.NodeFromTriangleIndex(hit.triangleIndex);
            if (node != null && node != skeleton.root)
            {
                SetHovered(node);

                if (Input.GetMouseButtonDown(0))
                    ExecuteTrim(node);

                return;
            }
        }

        SetHovered(null);
    }

    // ── Highlight ─────────────────────────────────────────────────────────────

    void SetHovered(TreeNode node)
    {
        if (node == hoveredNode) return;
        hoveredNode = node;

        if (node == null)
        {
            highlightRenderer.enabled = false;
        }
        else
        {
            RebuildHighlightMesh(node);
            highlightRenderer.enabled = true;
        }
    }

    void RebuildHighlightMesh(TreeNode node)
    {
        hVerts.Clear();
        hTris.Clear();
        hUVs.Clear();

        BuildSubtreeNode(node, -1, 0f, Vector3.zero);

        highlightMesh.Clear();
        highlightMesh.SetVertices(hVerts);
        highlightMesh.SetTriangles(hTris, 0);
        highlightMesh.SetUVs(0, hUVs);
        highlightMesh.RecalculateNormals();
        highlightMesh.RecalculateBounds();
    }

    // ── Trim ──────────────────────────────────────────────────────────────────

    void ExecuteTrim(TreeNode node)
    {
        SetHovered(null);
        skeleton.TrimNode(node);
        // canTrim stays true — player can keep trimming until they deselect the tool
    }

    // ── Highlight mesh generation ──────────────────────────────────────────────
    // Mirror of TreeMeshBuilder's ProcessNode/AddRing, using local hVerts/hTris/hUVs
    // buffers and a slightly inflated radius to prevent z-fighting.

    int BuildSubtreeNode(TreeNode node, int baseRingStart, float cumHeight, Vector3 frameRight)
    {
        Vector3 axisUp = node.growDirection.normalized;

        // Parallel-transport the inherited frame (same logic as TreeMeshBuilder)
        if (frameRight.sqrMagnitude > 0.001f)
            frameRight = Vector3.ProjectOnPlane(frameRight, axisUp).normalized;

        if (frameRight.sqrMagnitude < 0.001f)
        {
            frameRight = Vector3.Cross(axisUp, Vector3.up);
            if (frameRight.sqrMagnitude < 0.001f)
                frameRight = Vector3.Cross(axisUp, Vector3.forward);
            frameRight = frameRight.normalized;
        }

        if (baseRingStart < 0)
        {
            baseRingStart = hVerts.Count;
            AddHighlightRing(node.worldPosition, axisUp, frameRight, node.radius, cumHeight);
        }

        float tipHeight    = cumHeight + node.length;
        int   tipRingStart = hVerts.Count;
        AddHighlightRing(node.tipPosition, axisUp, frameRight, node.tipRadius, tipHeight);

        for (int i = 0; i < ringSegments; i++)
        {
            int b0 = baseRingStart + i;
            int b1 = baseRingStart + i + 1;
            int t0 = tipRingStart  + i;
            int t1 = tipRingStart  + i + 1;

            hTris.Add(b0); hTris.Add(t0); hTris.Add(b1);
            hTris.Add(b1); hTris.Add(t0); hTris.Add(t1);
        }

        foreach (var child in node.children)
            BuildSubtreeNode(child, tipRingStart, tipHeight, frameRight);

        return tipRingStart;
    }

    void AddHighlightRing(Vector3 center, Vector3 axisUp, Vector3 axisRight, float radius, float heightV)
    {
        Vector3 axisFwd = Vector3.Cross(axisRight, axisUp).normalized;
        float   r       = radius * highlightRadiusBias;

        for (int i = 0; i <= ringSegments; i++)
        {
            float   t      = (float)i / ringSegments;
            float   angle  = t * Mathf.PI * 2f;
            Vector3 offset = (axisRight * Mathf.Cos(angle) + axisFwd * Mathf.Sin(angle)) * r;

            hVerts.Add(center + offset);
            hUVs.Add(new Vector2(t, heightV * 0.4f));
        }
    }
}
