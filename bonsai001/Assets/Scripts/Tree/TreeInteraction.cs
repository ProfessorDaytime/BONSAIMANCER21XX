using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Handles all player interaction with the tree mesh.
///
/// Trim mode  (SmallClippers / BigClippers / Saw):
///   Hover → red subtree highlight. Click → trim subtree.
///
/// Wire mode  (Wire tool):
///   Hover → gold single-node highlight.
///   Click → pause game (Wiring state), mouse aims direction preview.
///   Click again → confirm wire. Right-click / Escape → cancel.
///
/// Remove Wire mode (RemoveWire tool):
///   Hover wired nodes → green single-node highlight. Click → unwire.
/// </summary>
[RequireComponent(typeof(TreeSkeleton), typeof(TreeMeshBuilder))]
public class TreeInteraction : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Tooltip("Must match TreeMeshBuilder.ringSegments so faceting aligns.")]
    [SerializeField] int ringSegments = 8;

    [Tooltip("How much larger the highlight mesh radius is to prevent z-fighting.")]
    [SerializeField] float highlightRadiusBias = 1.04f;

    [Tooltip("Length of the wire aim direction preview arrow.")]
    [SerializeField] float aimPreviewLength = 1.5f;

    // ── References ────────────────────────────────────────────────────────────

    TreeSkeleton    skeleton;
    TreeMeshBuilder meshBuilder;
    Camera          cam;

    // ── Highlight overlay ─────────────────────────────────────────────────────

    MeshFilter   highlightFilter;
    MeshRenderer highlightRenderer;
    Mesh         highlightMesh;
    Material     highlightMat;

    // Reused every rebuild — avoids per-frame GC allocations
    readonly List<Vector3> hVerts = new List<Vector3>();
    readonly List<int>     hTris  = new List<int>();
    readonly List<Vector2> hUVs   = new List<Vector2>();

    // ── Highlight state ───────────────────────────────────────────────────────

    enum HighlightMode { None, TrimSubtree, SingleGold, SingleGreen }

    TreeNode      highlightedNode;
    HighlightMode highlightMode = HighlightMode.None;

    static readonly Color ColTrim       = new Color(0.9f, 0.1f, 0.1f);   // red
    static readonly Color ColWire       = new Color(0.9f, 0.65f, 0.1f);  // gold
    static readonly Color ColRemoveWire = new Color(0.1f, 0.8f, 0.3f);   // green

    // ── Wire aim state ────────────────────────────────────────────────────────

    enum WirePhase { None, Aiming }

    WirePhase wirePhase    = WirePhase.None;
    TreeNode  wireTarget;
    Vector3   aimDirection;   // world-space direction being aimed
    GameState preWireState;   // state to restore when aim ends

    // Direction preview arrow
    LineRenderer aimPreview;

    // ── Unity ─────────────────────────────────────────────────────────────────

    void Awake()
    {
        skeleton    = GetComponent<TreeSkeleton>();
        meshBuilder = GetComponent<TreeMeshBuilder>();
        cam         = Camera.main;

        // ── Highlight child GO ────────────────────────────────────────────────
        var hlGo = new GameObject("_TreeHighlight");
        hlGo.transform.SetParent(transform, false);

        highlightFilter   = hlGo.AddComponent<MeshFilter>();
        highlightRenderer = hlGo.AddComponent<MeshRenderer>();
        highlightMat      = new Material(Shader.Find("Unlit/Color")) { color = ColTrim };
        highlightRenderer.material = highlightMat;
        highlightRenderer.enabled  = false;

        highlightMesh        = new Mesh { name = "HighlightMesh" };
        highlightFilter.mesh = highlightMesh;

        // ── Aim preview LineRenderer ──────────────────────────────────────────
        var aimGo = new GameObject("_WireAimPreview");
        aimGo.transform.SetParent(transform, false);

        aimPreview                   = aimGo.AddComponent<LineRenderer>();
        aimPreview.useWorldSpace     = true;
        aimPreview.positionCount     = 2;
        aimPreview.startWidth        = 0.04f;
        aimPreview.endWidth          = 0.02f;
        aimPreview.material          = new Material(Shader.Find("Unlit/Color")) { color = new Color(1f, 0.5f, 0f) };
        aimPreview.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        aimPreview.enabled           = false;
    }

    void Update()
    {
        if (skeleton.root == null)
        {
            SetHighlight(null, HighlightMode.None);
            return;
        }

        // Wire aim mode takes over completely
        if (wirePhase == WirePhase.Aiming)
        {
            UpdateWireAim();
            return;
        }

        if (GameManager.canWire)
            HandleWireHover();
        else if (GameManager.canRemoveWire)
            HandleRemoveWireHover();
        else if (GameManager.canTrim)
            HandleTrimHover();
        else
            SetHighlight(null, HighlightMode.None);
    }

    // ── Trim ──────────────────────────────────────────────────────────────────

    void HandleTrimHover()
    {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit) && hit.collider.gameObject == gameObject)
        {
            TreeNode node = meshBuilder.NodeFromTriangleIndex(hit.triangleIndex);
            if (node != null && node != skeleton.root)
            {
                SetHighlight(node, HighlightMode.TrimSubtree);

                if (Input.GetMouseButtonDown(0))
                {
                    SetHighlight(null, HighlightMode.None);
                    skeleton.TrimNode(node);
                }
                return;
            }
        }
        SetHighlight(null, HighlightMode.None);
    }

    // ── Wire placement ────────────────────────────────────────────────────────

    void HandleWireHover()
    {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit) && hit.collider.gameObject == gameObject)
        {
            TreeNode node = meshBuilder.NodeFromTriangleIndex(hit.triangleIndex);
            if (node != null && node != skeleton.root && !node.hasWire)
            {
                SetHighlight(node, HighlightMode.SingleGold);

                if (Input.GetMouseButtonDown(0))
                    StartWireAim(node);

                return;
            }
        }
        SetHighlight(null, HighlightMode.None);
    }

    void StartWireAim(TreeNode node)
    {
        wireTarget   = node;
        wirePhase    = WirePhase.Aiming;
        preWireState = GameManager.Instance.state;

        // Start aim arrow pointing along the branch's current direction
        aimDirection = transform.TransformDirection(node.growDirection);

        GameManager.Instance.UpdateGameState(GameState.Wiring);
        aimPreview.enabled = true;
        UpdateAimPreview();
    }

    void UpdateWireAim()
    {
        // Cancel automatically if wire tool was deselected
        if (!GameManager.canWire)
        {
            CancelWire();
            return;
        }

        // Update aim direction: project mouse onto a plane facing the camera at the branch base
        Vector3 branchWorldPos = transform.TransformPoint(wireTarget.worldPosition);
        Plane   aimPlane       = new Plane(-cam.transform.forward, branchWorldPos);
        Ray     ray            = cam.ScreenPointToRay(Input.mousePosition);

        if (aimPlane.Raycast(ray, out float dist))
        {
            Vector3 dir = ray.GetPoint(dist) - branchWorldPos;
            if (dir.sqrMagnitude > 0.01f)
                aimDirection = dir.normalized;
        }

        UpdateAimPreview();
        SetHighlight(wireTarget, HighlightMode.SingleGold);

        if (Input.GetMouseButtonDown(0))                              ConfirmWire();
        if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape)) CancelWire();
    }

    void UpdateAimPreview()
    {
        Vector3 worldBase = transform.TransformPoint(wireTarget.worldPosition);
        aimPreview.SetPosition(0, worldBase);
        aimPreview.SetPosition(1, worldBase + aimDirection * aimPreviewLength);
    }

    void ConfirmWire()
    {
        Vector3 localDir = transform.InverseTransformDirection(aimDirection);
        skeleton.WireNode(wireTarget, localDir);
        EndWireAim();
    }

    void CancelWire() => EndWireAim();

    void EndWireAim()
    {
        wirePhase          = WirePhase.None;
        wireTarget         = null;
        aimPreview.enabled = false;
        SetHighlight(null, HighlightMode.None);
        GameManager.Instance.UpdateGameState(preWireState);
    }

    // ── Remove wire ───────────────────────────────────────────────────────────

    void HandleRemoveWireHover()
    {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit) && hit.collider.gameObject == gameObject)
        {
            TreeNode node = meshBuilder.NodeFromTriangleIndex(hit.triangleIndex);
            if (node != null && node.hasWire)
            {
                SetHighlight(node, HighlightMode.SingleGreen);

                if (Input.GetMouseButtonDown(0))
                {
                    skeleton.UnwireNode(node);
                    SetHighlight(null, HighlightMode.None);
                }
                return;
            }
        }
        SetHighlight(null, HighlightMode.None);
    }

    // ── Highlight ─────────────────────────────────────────────────────────────

    void SetHighlight(TreeNode node, HighlightMode mode)
    {
        if (node == highlightedNode && mode == highlightMode) return;
        highlightedNode = node;
        highlightMode   = mode;

        if (node == null)
        {
            highlightRenderer.enabled = false;
            return;
        }

        switch (mode)
        {
            case HighlightMode.TrimSubtree: highlightMat.color = ColTrim;       break;
            case HighlightMode.SingleGold:  highlightMat.color = ColWire;       break;
            case HighlightMode.SingleGreen: highlightMat.color = ColRemoveWire; break;
        }

        RebuildHighlightMesh(node, singleNode: mode != HighlightMode.TrimSubtree);
        highlightRenderer.enabled = true;
    }

    void RebuildHighlightMesh(TreeNode node, bool singleNode)
    {
        hVerts.Clear();
        hTris.Clear();
        hUVs.Clear();

        BuildSubtreeNode(node, -1, 0f, Vector3.zero, recurse: !singleNode);

        highlightMesh.Clear();
        highlightMesh.SetVertices(hVerts);
        highlightMesh.SetTriangles(hTris, 0);
        highlightMesh.SetUVs(0, hUVs);
        highlightMesh.RecalculateNormals();
        highlightMesh.RecalculateBounds();
    }

    // ── Highlight mesh generation ──────────────────────────────────────────────

    int BuildSubtreeNode(TreeNode node, int baseRingStart, float cumHeight, Vector3 frameRight, bool recurse)
    {
        Vector3 axisUp = node.growDirection.normalized;

        // Parallel transport the frame through the bend
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
            int b0 = baseRingStart + i,    b1 = baseRingStart + i + 1;
            int t0 = tipRingStart  + i,    t1 = tipRingStart  + i + 1;

            hTris.Add(b0); hTris.Add(t0); hTris.Add(b1);
            hTris.Add(b1); hTris.Add(t0); hTris.Add(t1);
        }

        if (recurse)
            foreach (var child in node.children)
                BuildSubtreeNode(child, tipRingStart, tipHeight, frameRight, true);

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
