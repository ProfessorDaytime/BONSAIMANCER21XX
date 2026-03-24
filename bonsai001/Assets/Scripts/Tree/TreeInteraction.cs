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

    enum HighlightMode { None, TrimSubtree, SingleGold, SingleGreen, WireRun, Paste }

    TreeNode        highlightedNode;
    HighlightMode   highlightMode = HighlightMode.None;
    List<TreeNode>  highlightedRun = new List<TreeNode>();

    static readonly Color ColTrim       = new Color(0.9f, 0.1f, 0.1f);   // red
    static readonly Color ColWire       = new Color(0.9f, 0.65f, 0.1f);  // gold
    static readonly Color ColRemoveWire = new Color(0.1f, 0.8f, 0.3f);   // green (single node)
    static readonly Color ColWireRun    = new Color(0.0f, 1.0f, 0.55f);  // bright green (full run)
    static readonly Color ColPaste      = new Color(0.2f, 0.8f, 0.9f);   // cyan (wound with paste)

    // ── Wire aim / animation state ────────────────────────────────────────────

    enum WirePhase { None, Aiming, Animating }

    WirePhase wirePhase    = WirePhase.None;
    TreeNode  wireTarget;
    Vector3   aimDirection;   // world-space direction being aimed
    GameState preWireState;   // state to restore when aim/animation ends

    // Spring animation
    float   wireAnimTimer;
    Vector3 wireAnimStartDir;
    Vector3 wireAnimEndDir;
    const float WIRE_ANIM_DURATION = 0.6f;

    // Original growDirections of wireTarget's descendants, captured at confirm time.
    // The animation applies Quaternion.FromToRotation(wireAnimStartDir, currentDir)
    // to these originals every frame rather than accumulating deltas, so there is
    // no floating-point drift even with the spring overshoot.
    readonly Dictionary<TreeNode, Vector3> descOriginalDirs = new Dictionary<TreeNode, Vector3>();

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

        // Wire animation blocks all other interaction until complete
        if (wirePhase == WirePhase.Animating)
        {
            UpdateWireAnimation();
            return;
        }

        // Wire aim mode takes over completely
        if (wirePhase == WirePhase.Aiming)
        {
            UpdateWireAim();
            return;
        }

        if (GameManager.canRootWork)
            HandleRootWorkHover();
        else if (GameManager.canWire)
            HandleWireHover();
        else if (GameManager.canRemoveWire)
            HandleRemoveWireHover();
        else if (GameManager.canTrim)
            HandleTrimHover();
        else if (GameManager.canPaste)
            HandlePasteHover();
        else
            SetHighlight(null, HighlightMode.None);
    }

    // ── Root work (RootPrune mode) ────────────────────────────────────────────

    // Y coordinate of the soil plane in world space.
    // Assumes the tree was planted at world y = 0.
    const float SOIL_WORLD_Y = 0f;

    void HandleRootWorkHover()
    {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);

        // Priority 1: trim an existing root by clicking it on the tree mesh.
        if (Physics.Raycast(ray, out RaycastHit hit) && hit.collider.gameObject == gameObject)
        {
            TreeNode node = meshBuilder.NodeFromTriangleIndex(hit.triangleIndex);
            if (node != null && node.isRoot && node != skeleton.root)
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

        // Priority 2: plant a new root by clicking the planting surface.
        Plane soilPlane = new Plane(skeleton.plantingNormal, skeleton.plantingSurfacePoint);
        if (soilPlane.Raycast(ray, out float dist))
        {
            SetHighlight(null, HighlightMode.None);

            if (Input.GetMouseButtonDown(0))
            {
                Vector3 worldPoint     = ray.GetPoint(dist);
                Vector3 trunkBaseWorld = transform.TransformPoint(skeleton.root.worldPosition);
                Vector3 outward        = worldPoint - trunkBaseWorld;
                outward.y = 0f;  // PlantRoot adds the downward pitch component

                if (outward.sqrMagnitude > 0.01f)
                {
                    Vector3 localDir = transform.InverseTransformDirection(outward.normalized);
                    skeleton.PlantRoot(localDir);
                }
            }
            return;
        }

        // Right-click any non-tree surface to place the tree on it.
        // The tree lowers to that surface and subsequent root growth hugs it.
        if (Input.GetMouseButtonDown(1))
        {
            if (Physics.Raycast(ray, out RaycastHit surfaceHit) &&
                surfaceHit.collider.gameObject != gameObject)
            {
                skeleton.SetPlantingSurface(surfaceHit.point, surfaceHit.normal);
            }
        }

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

        Debug.Log($"[Wire] StartWireAim node={node.id} depth={node.depth} | preWireState={preWireState} | canWire={GameManager.canWire}");

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
            Debug.Log($"[Wire] Auto-cancel — canWire went false while aiming | gameState={GameManager.Instance.state} | preWireState={preWireState}");
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

        if (Input.GetMouseButtonDown(0)) { ConfirmWire(); return; }
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

        // Capture start direction before WireNode records it as wireOriginalDirection
        wireAnimStartDir = wireTarget.growDirection;
        skeleton.WireNode(wireTarget, localDir);
        wireAnimEndDir = wireTarget.wireTargetDirection;

        // Snapshot every descendant's current growDirection.
        // The animation will apply FromToRotation(wireAnimStartDir, currentDir)
        // to these originals each frame so the whole subtree rotates as a rigid body.
        descOriginalDirs.Clear();
        CaptureDescendantDirs(wireTarget);

        wireAnimTimer      = 0f;
        wirePhase          = WirePhase.Animating;
        aimPreview.enabled = false;
        SetHighlight(null, HighlightMode.None);

        Debug.Log($"[Wire] ConfirmWire node={wireTarget.id} | preWireState={preWireState} | canWire={GameManager.canWire} | descSnapCount={descOriginalDirs.Count}");

        GameManager.Instance.UpdateGameState(GameState.WireAnimate);
    }

    void CaptureDescendantDirs(TreeNode node)
    {
        foreach (var child in node.children)
        {
            descOriginalDirs[child] = child.growDirection;
            CaptureDescendantDirs(child);
        }
    }

    void CancelWire()
    {
        Debug.Log($"[Wire] CancelWire | preWireState={preWireState} | canWire={GameManager.canWire}");
        EndWireAim();
    }

    void EndWireAim()
    {
        Debug.Log($"[Wire] EndWireAim → restoring state={preWireState} | wirePhase was={wirePhase}");
        wirePhase          = WirePhase.None;
        wireTarget         = null;
        aimPreview.enabled = false;
        SetHighlight(null, HighlightMode.None);
        GameManager.Instance.UpdateGameState(preWireState);
    }

    // ── Wire spring animation ─────────────────────────────────────────────────

    void UpdateWireAnimation()
    {
        wireAnimTimer += Time.deltaTime;

        bool done = wireAnimTimer >= WIRE_ANIM_DURATION
                    || Input.GetKeyDown(KeyCode.Return);

        float t      = done ? 1f : Mathf.Clamp01(wireAnimTimer / WIRE_ANIM_DURATION);
        float spring = WireSpringCurve(t);

        wireTarget.growDirection = Vector3.Slerp(wireAnimStartDir, wireAnimEndDir, spring).normalized;

        // Rotate all descendants by the same rotation applied to wireTarget
        // so the subtree moves as a rigid body. We apply to the ORIGINAL dirs
        // (captured at ConfirmWire) to avoid float drift from frame accumulation.
        Quaternion rot = Quaternion.FromToRotation(wireAnimStartDir, wireTarget.growDirection);
        skeleton.RotateAndPropagateDescendants(wireTarget, rot, descOriginalDirs);
        skeleton.meshBuilder.SetDirty();

        if (done)
        {
            // Lock wireTarget to exact target, then do one final rigid-body propagation.
            wireTarget.growDirection = wireAnimEndDir;
            rot = Quaternion.FromToRotation(wireAnimStartDir, wireAnimEndDir);
            skeleton.RotateAndPropagateDescendants(wireTarget, rot, descOriginalDirs);
            skeleton.meshBuilder.SetDirty();

            Debug.Log($"[Wire] AnimationDone node={wireTarget.id} | preWireState={preWireState} | canWire={GameManager.canWire}");
            descOriginalDirs.Clear();
            wirePhase  = WirePhase.None;
            wireTarget = null;
            GameManager.Instance.UpdateGameState(preWireState);
        }
    }

    /// <summary>
    /// Damped-spring easing: overshoots past 1 by ~15 % then settles.
    /// Input t is 0..1 over the animation duration.
    /// </summary>
    static float WireSpringCurve(float t)
    {
        return 1f - Mathf.Exp(-5f * t) * Mathf.Cos(Mathf.PI * 2f * t * 1.5f);
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
                var run    = skeleton.CollectWireRun(node);
                bool allSet = true;
                foreach (var n in run)
                    if (n.wireSetProgress < 1f) { allSet = false; break; }

                if (allSet && run.Count > 1)
                    SetHighlightRun(run);
                else
                    SetHighlight(node, HighlightMode.SingleGreen);

                if (Input.GetMouseButtonDown(0))
                {
                    skeleton.UnwireRun(node);
                    SetHighlight(null, HighlightMode.None);
                }
                return;
            }
        }
        SetHighlight(null, HighlightMode.None);
    }

    // ── Paste ─────────────────────────────────────────────────────────────────

    void HandlePasteHover()
    {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit) && hit.collider.gameObject == gameObject)
        {
            TreeNode node = meshBuilder.NodeFromTriangleIndex(hit.triangleIndex);
            if (node != null && node.hasWound && !node.pasteApplied)
            {
                SetHighlight(node, HighlightMode.Paste);

                if (Input.GetMouseButtonDown(0))
                {
                    skeleton.ApplyPaste(node);
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
        highlightedRun.Clear();  // switching to single-node mode; clear any run state
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
            case HighlightMode.Paste:       highlightMat.color = ColPaste;      break;
        }

        RebuildHighlightMesh(node, singleNode: mode != HighlightMode.TrimSubtree);
        highlightRenderer.enabled = true;
    }

    void SetHighlightRun(List<TreeNode> run)
    {
        // Skip rebuild if the same run is already highlighted
        if (highlightMode == HighlightMode.WireRun &&
            highlightedRun.Count == run.Count &&
            highlightedRun.Count > 0 && highlightedRun[0] == run[0])
            return;

        highlightedRun  = run;
        highlightedNode = run.Count > 0 ? run[0] : null;
        highlightMode   = HighlightMode.WireRun;

        highlightMat.color = ColWireRun;
        RebuildHighlightMeshRun(run);
        highlightRenderer.enabled = true;
    }

    void RebuildHighlightMeshRun(List<TreeNode> nodes)
    {
        hVerts.Clear();
        hTris.Clear();
        hUVs.Clear();

        // Chain each node's tip ring into the next node's base ring for seamless joints.
        int prevTip = -1;
        foreach (var node in nodes)
            prevTip = BuildSubtreeNode(node, prevTip, 0f, Vector3.zero, recurse: false);

        highlightMesh.Clear();
        highlightMesh.SetVertices(hVerts);
        highlightMesh.SetTriangles(hTris, 0);
        highlightMesh.SetUVs(0, hUVs);
        highlightMesh.RecalculateNormals();
        highlightMesh.RecalculateBounds();
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
