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

    enum HighlightMode { None, TrimSubtree, SingleGold, SingleGreen, WireRun, Paste, AirLayer }

    TreeNode        highlightedNode;
    HighlightMode   highlightMode = HighlightMode.None;
    List<TreeNode>  highlightedRun = new List<TreeNode>();

    static readonly Color ColTrim       = new Color(0.9f, 0.1f, 0.1f);   // red
    static readonly Color ColWire       = new Color(0.9f, 0.65f, 0.1f);  // gold
    static readonly Color ColRemoveWire = new Color(0.1f, 0.8f, 0.3f);   // green (single node)
    static readonly Color ColWireRun    = new Color(0.0f, 1.0f, 0.55f);  // bright green (full run)
    static readonly Color ColPaste      = new Color(0.2f, 0.8f, 0.9f);   // cyan (wound with paste)
    static readonly Color ColAirLayer   = new Color(0.0f, 0.85f, 0.85f); // teal (air layer placement)
    static readonly Color ColRootWork   = new Color(0.9f, 0.45f, 0.1f);  // orange (root work)

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

    // ── Selection sphere ──────────────────────────────────────────────────────
    GameObject selectionSphere;
    Material   sphereMat;

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

        // ── Selection sphere ──────────────────────────────────────────────────
        selectionSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        selectionSphere.name = "_SelectionSphere";
        var sCol = selectionSphere.GetComponent<Collider>();
        if (sCol != null) Destroy(sCol);
        sphereMat = new Material(Shader.Find("Standard"));
        sphereMat.SetFloat("_Mode", 3f);
        sphereMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        sphereMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        sphereMat.SetInt("_ZWrite", 0);
        sphereMat.EnableKeyword("_ALPHABLEND_ON");
        sphereMat.renderQueue = 3000;
        selectionSphere.GetComponent<Renderer>().material = sphereMat;
        selectionSphere.SetActive(false);
    }

    // ── Selection sphere & node picking ───────────────────────────────────────

    void UpdateSelectionSphere()
    {
        float radius = GameManager.selectionRadius;
        Ray   ray    = cam.ScreenPointToRay(Input.mousePosition);

        if (radius <= 0f)
        {
            selectionSphere.SetActive(false);
            return;
        }

        // Position at raycast hit, or project to a fixed depth if ray hits nothing.
        Vector3 spherePos = Physics.Raycast(ray, out RaycastHit hit)
            ? hit.point
            : ray.GetPoint(Vector3.Distance(cam.transform.position, transform.position));

        selectionSphere.SetActive(true);
        selectionSphere.transform.position   = spherePos;
        selectionSphere.transform.localScale = Vector3.one * radius * 2f;

        Color col  = ToolColor();
        col.a      = 0.22f;
        sphereMat.color = col;
    }

    Color ToolColor()
    {
        if (GameManager.canRootWork) return ColRootWork;
        switch (ToolManager.Instance.ActiveTool)
        {
            case ToolType.SmallClippers:
            case ToolType.BigClippers:
            case ToolType.Saw:        return ColTrim;
            case ToolType.Wire:       return ColWire;
            case ToolType.RemoveWire: return ColRemoveWire;
            case ToolType.Paste:      return ColPaste;
            case ToolType.AirLayer:   return ColAirLayer;
            default:                  return Color.white;
        }
    }

    /// <summary>
    /// Picks the most relevant node at the cursor.
    /// When selectionRadius > 0 finds the nearest node within that world-space
    /// distance from the camera ray — works even when the ray misses all colliders.
    /// When radius is 0 falls back to exact triangle hit on the tree mesh.
    /// </summary>
    TreeNode PickNode(Ray ray, out RaycastHit hit)
    {
        Physics.Raycast(ray, out hit);   // attempt hit but don't require it
        float r = GameManager.selectionRadius;
        if (r > 0f) return NodeNearRay(ray, r);
        if (!hit.collider || hit.collider.gameObject != gameObject) return null;
        return meshBuilder.NodeFromTriangleIndex(hit.triangleIndex);
    }

    /// <summary>Returns the nearest node whose base or tip is within <paramref name="radius"/>
    /// world units of the camera ray.</summary>
    TreeNode NodeNearRay(Ray ray, float radius)
    {
        TreeNode best     = null;
        float    bestDist = radius;
        foreach (var node in skeleton.allNodes)
        {
            float d = Mathf.Min(
                DistToRay(transform.TransformPoint(node.worldPosition), ray),
                DistToRay(transform.TransformPoint(node.tipPosition),   ray));
            if (d < bestDist) { bestDist = d; best = node; }
        }
        return best;
    }

    // Perpendicular distance from a world point to an infinite ray.
    static float DistToRay(Vector3 point, Ray ray) =>
        Vector3.Cross(ray.direction, point - ray.origin).magnitude;

    void Update()
    {
        if (skeleton.root == null)
        {
            SetHighlight(null, HighlightMode.None);
            return;
        }

        UpdateSelectionSphere();

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
        else if (GameManager.canAirLayer)
            HandleAirLayerHover();
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

        // Priority 1: trim an existing root.
        TreeNode rootNode = PickNode(ray, out RaycastHit hit);
        if (rootNode != null && rootNode.isRoot && rootNode != skeleton.root)
        {
            SetHighlight(rootNode, HighlightMode.TrimSubtree);
            if (Input.GetMouseButtonDown(0))
            {
                SetHighlight(null, HighlightMode.None);
                skeleton.TrimNode(rootNode);
            }
            return;
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
        Ray      ray  = cam.ScreenPointToRay(Input.mousePosition);
        TreeNode node = PickNode(ray, out _);
        if (node != null && node != skeleton.root && !node.isRoot)
        {
            SetHighlight(node, HighlightMode.TrimSubtree);
            if (Input.GetMouseButtonDown(0))
            {
                SetHighlight(null, HighlightMode.None);
                skeleton.TrimNode(node);
            }
            return;
        }
        SetHighlight(null, HighlightMode.None);
    }

    // ── Wire placement ────────────────────────────────────────────────────────

    void HandleWireHover()
    {
        Ray      ray  = cam.ScreenPointToRay(Input.mousePosition);
        TreeNode node = PickNode(ray, out _);
        if (node != null && node != skeleton.root && (!node.isRoot || node.isAirLayerRoot) && !node.hasWire)
        {
            SetHighlight(node, HighlightMode.SingleGold);
            if (Input.GetMouseButtonDown(0))
                StartWireAim(node);
            return;
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
        Ray      ray  = cam.ScreenPointToRay(Input.mousePosition);
        TreeNode node = PickNode(ray, out _);
        if (node != null && node.hasWire)
        {
            var  run    = skeleton.CollectWireRun(node);
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
        SetHighlight(null, HighlightMode.None);
    }

    // ── Paste ─────────────────────────────────────────────────────────────────

    void HandlePasteHover()
    {
        Ray      ray  = cam.ScreenPointToRay(Input.mousePosition);
        TreeNode node = PickNode(ray, out _);
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
        SetHighlight(null, HighlightMode.None);
    }

    // ── Air Layer ─────────────────────────────────────────────────────────────

    void HandleAirLayerHover()
    {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit))
        {
            SetHighlight(null, HighlightMode.None);
            return;
        }

        // Priority 1: hit a wrap cylinder directly — click to unwrap if ready.
        foreach (var layer in skeleton.airLayers)
        {
            if (layer.wrapObject != null && hit.collider.gameObject == layer.wrapObject)
            {
                SetHighlight(null, HighlightMode.None);
                if (layer.rootsSpawned && Input.GetMouseButtonDown(0))
                    skeleton.UnwrapAirLayer(layer);
                return;
            }
        }

        // Priority 2: hit the tree mesh — place a new air layer.
        if (hit.collider.gameObject != gameObject)
        {
            SetHighlight(null, HighlightMode.None);
            return;
        }

        TreeNode node = meshBuilder.NodeFromTriangleIndex(hit.triangleIndex);
        if (node == null || node == skeleton.root || node.isRoot)
        {
            SetHighlight(null, HighlightMode.None);
            return;
        }

        // Don't allow placing on a node that already has a layer.
        foreach (var l in skeleton.airLayers)
        {
            if (l.node == node)
            {
                SetHighlight(null, HighlightMode.None);
                return;
            }
        }

        SetHighlight(node, HighlightMode.AirLayer);
        if (Input.GetMouseButtonDown(0))
        {
            skeleton.PlaceAirLayer(node);
            SetHighlight(null, HighlightMode.None);
        }
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
            case HighlightMode.AirLayer:    highlightMat.color = ColAirLayer;   break;
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
