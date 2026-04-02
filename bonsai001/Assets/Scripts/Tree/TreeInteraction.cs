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

    enum HighlightMode { None, TrimSubtree, SingleGold, SingleGreen, WireRun, Paste, AirLayer, Pinch, Defoliate }

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
    static readonly Color ColPinch      = new Color(0.55f, 1.0f, 0.15f); // lime-green (pinch tip)
    static readonly Color ColDefoliate  = new Color(1.0f,  0.75f, 0.0f); // amber (defoliate)

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

    // ── Selection cursor (screen-space GL circle) ─────────────────────────────
    Material glCircleMat;
    float    selCirclePixelRadius;
    Color    selCircleColor;

    // ── Selection diagnostics ─────────────────────────────────────────────────
    struct FailedClick
    {
        public Vector3 camPos;
        public Vector3 camFwd;
        public Vector2 mouseScreen;
        public float   selRadius;
        public float   selPixelRadius;
        public Ray     ray;
        // Top candidates at click time (node id, DistToRay, isRoot)
        public (int id, float dist, bool isRoot, Vector3 worldPos)[] nearest;
    }
    readonly System.Collections.Generic.List<FailedClick> failedClicks = new();
    bool diagArmed; // true after 1+ failed clicks, waiting for F or a trim
    readonly System.Collections.Generic.List<GameObject> diagMarkers = new();

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

        // ── Selection cursor ──────────────────────────────────────────────────
        // Drawn as a screen-space GL circle in OnRenderObject so its pixel
        // size is always consistent regardless of camera distance or angle.
        glCircleMat = new Material(Shader.Find("Hidden/Internal-Colored"));
        glCircleMat.hideFlags = HideFlags.HideAndDontSave;
        glCircleMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        glCircleMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        glCircleMat.SetInt("_Cull",   0);
        glCircleMat.SetInt("_ZWrite", 0);
        glCircleMat.SetInt("_ZTest",  (int)UnityEngine.Rendering.CompareFunction.Always);
    }

    // ── Selection sphere & node picking ───────────────────────────────────────

    void UpdateSelectionCursor()
    {
        float r = GameManager.selectionRadius;
        if (r <= 0f) { selCirclePixelRadius = 0f; return; }

        // Use camera-to-tree distance as a fixed reference depth so the circle
        // never changes size while hovering over geometry at varying depths.
        // DistToRay is purely screen-space so any consistent depth reference works.
        float depth = Vector3.Distance(cam.transform.position, transform.position);

        // Convert world-unit radius to screen pixels at this depth.
        // Same formula as perspective projection: pixelR = worldR * focalLength / depth
        float halfFovRad  = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
        float focalLength = cam.pixelHeight * 0.5f / Mathf.Tan(halfFovRad);
        selCirclePixelRadius = r * focalLength / Mathf.Max(depth, 0.01f);

        Color col = ToolColor();
        col.a = 0.9f;
        selCircleColor = col;
    }

    // Draws a screen-space circle at the cursor using GL after all geometry renders.
    void OnRenderObject()
    {
        if (Camera.current != cam || selCirclePixelRadius <= 0f) return;

        Vector2 mouse = Input.mousePosition;
        glCircleMat.SetPass(0);
        GL.PushMatrix();
        GL.LoadPixelMatrix(0, cam.pixelWidth, 0, cam.pixelHeight);
        GL.Begin(GL.LINES);
        GL.Color(selCircleColor);
        const int segs = 48;
        for (int i = 0; i < segs; i++)
        {
            float a1 = (float)i       / segs * Mathf.PI * 2f;
            float a2 = (float)(i + 1) / segs * Mathf.PI * 2f;
            GL.Vertex3(mouse.x + Mathf.Cos(a1) * selCirclePixelRadius,
                       mouse.y + Mathf.Sin(a1) * selCirclePixelRadius, 0f);
            GL.Vertex3(mouse.x + Mathf.Cos(a2) * selCirclePixelRadius,
                       mouse.y + Mathf.Sin(a2) * selCirclePixelRadius, 0f);
        }
        GL.End();
        GL.PopMatrix();
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
            case ToolType.Pinch:      return ColPinch;
            case ToolType.Defoliate:  return ColDefoliate;
            default:                  return Color.white;
        }
    }

    /// <summary>
    /// Picks the most relevant node at the cursor.
    /// When selectionRadius > 0, uses perpendicular distance from each node to
    /// the camera ray (depth-independent — equivalent to screen-space XY radius).
    /// <paramref name="filter"/> limits which nodes are considered; pass null to accept all.
    /// When radius is 0, falls back to exact triangle hit on the tree mesh.
    /// </summary>
    TreeNode PickNode(Ray ray, out RaycastHit hit, System.Func<TreeNode, bool> filter = null)
    {
        float r = GameManager.selectionRadius;
        if (r > 0f)
        {
            hit = default;
            return NodeNearRay(ray, r, filter);
        }

        // In Ishitsuki mode the rock collider sits between the camera and trunk
        // segments that are inside or behind it — Physics.Raycast would stop at
        // the rock and never reach the tree mesh. Use RaycastAll and walk the hits
        // in distance order, picking the first one on the tree's own collider.
        if (skeleton.rockCollider != null)
        {
            hit = default;
            var hits = Physics.RaycastAll(ray);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (h.collider.gameObject != gameObject) continue;
                hit = h;
                var rn = meshBuilder.NodeFromTriangleIndex(h.triangleIndex);
                return (rn != null && (filter == null || filter(rn))) ? rn : null;
            }
            return null;
        }

        Physics.Raycast(ray, out hit);
        if (!hit.collider || hit.collider.gameObject != gameObject) return null;
        var node = meshBuilder.NodeFromTriangleIndex(hit.triangleIndex);
        return (node != null && (filter == null || filter(node))) ? node : null;
    }

    /// <summary>Returns the nearest node within <paramref name="radius"/> world units
    /// of the camera ray (perpendicular / screen-space distance), optionally filtered.</summary>
    TreeNode NodeNearRay(Ray ray, float radius, System.Func<TreeNode, bool> filter = null)
    {
        TreeNode best     = null;
        float    bestDist = radius;
        foreach (var node in skeleton.allNodes)
        {
            if (filter != null && !filter(node)) continue;
            float d = SegmentDistToRay(
                transform.TransformPoint(node.worldPosition),
                transform.TransformPoint(node.tipPosition),
                ray);
            if (d < bestDist) { bestDist = d; best = node; }
        }
        return best;
    }

    // Perpendicular (screen-space) distance from a world point to a ray.
    static float DistToRay(Vector3 point, Ray ray) =>
        Vector3.Cross(ray.direction, point - ray.origin).magnitude;

    // Minimum perpendicular distance from any point on segment A→B to a ray.
    // Uses the analytic closest-t on the segment rather than sampling endpoints only,
    // so long or angled segments are selected even when the cursor is over their middle.
    static float SegmentDistToRay(Vector3 a, Vector3 b, Ray ray)
    {
        Vector3 u  = Vector3.Cross(a - ray.origin, ray.direction);
        Vector3 v  = Vector3.Cross(b - a,          ray.direction);
        float   vv = Vector3.Dot(v, v);
        float   t  = (vv < 1e-8f) ? 0f : Mathf.Clamp01(-Vector3.Dot(u, v) / vv);
        return DistToRay(a + t * (b - a), ray);
    }

    void Update()
    {
        if (skeleton.root == null)
        {
            SetHighlight(null, HighlightMode.None);
            return;
        }

        UpdateSelectionCursor();

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

        var gstate = GameManager.Instance.state;
        if (GameManager.canRootWork &&
            gstate != GameState.RockPlace &&
            gstate != GameState.TreeOrient)
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
        else if (GameManager.canPinch)
            HandlePinchHover();
        else if (GameManager.canDefoliate)
            HandleDefoliateHover();
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

        // Diagnostic: F key dumps recorded failed clicks.
        if (Input.GetKeyDown(KeyCode.F) && diagArmed)
            DumpSelectionDiagnostics();

        // Priority 1: trim an existing root.
        TreeNode rootNode = PickNode(ray, out RaycastHit hit, n => n.isRoot && n != skeleton.root);
        if (rootNode != null)
        {
            SetHighlight(rootNode, HighlightMode.TrimSubtree);
            if (Input.GetMouseButtonDown(0))
            {
                SetHighlight(null, HighlightMode.None);
                // If there were queued failed clicks, log this as the intended target.
                if (diagArmed)
                {
                    Vector3 wp = transform.TransformPoint(rootNode.worldPosition);
                    Debug.LogWarning($"[SelDiag] INTENDED TARGET after {failedClicks.Count} missed click(s): " +
                                     $"node={rootNode.id} isRoot={rootNode.isRoot} " +
                                     $"worldPos={wp:F3} radius={rootNode.radius:F4}");
                    failedClicks.Clear();
                    diagArmed = false;
                }
                skeleton.TrimNode(rootNode);
            }
            return;
        }

        // Record a failed click (clicked but no root found).
        if (Input.GetMouseButtonDown(0))
            RecordFailedClick(ray);


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

    void RecordFailedClick(Ray ray)
    {
        // Collect the 8 nearest nodes (by DistToRay) regardless of filter,
        // so we can see what was close but not selected.
        var candidates = new System.Collections.Generic.List<(int id, float dist, bool isRoot, Vector3 worldPos)>();
        foreach (var node in skeleton.allNodes)
        {
            float d = SegmentDistToRay(
                transform.TransformPoint(node.worldPosition),
                transform.TransformPoint(node.tipPosition),
                ray);
            candidates.Add((node.id, d, node.isRoot, transform.TransformPoint(node.worldPosition)));
        }
        candidates.Sort((a, b) => a.dist.CompareTo(b.dist));

        var entry = new FailedClick
        {
            camPos        = cam.transform.position,
            camFwd        = cam.transform.forward,
            mouseScreen   = Input.mousePosition,
            selRadius     = GameManager.selectionRadius,
            selPixelRadius = selCirclePixelRadius,
            ray           = ray,
            nearest       = candidates.GetRange(0, Mathf.Min(8, candidates.Count)).ToArray()
        };
        failedClicks.Add(entry);
        diagArmed = true;
        Debug.Log($"[SelDiag] Failed click #{failedClicks.Count} recorded. " +
                  $"selRadius={entry.selRadius:F3} pixelR={entry.selPixelRadius:F1} — " +
                  $"nearest node: id={entry.nearest[0].id} dist={entry.nearest[0].dist:F4} isRoot={entry.nearest[0].isRoot}. " +
                  $"Press F to dump full diagnostics.");
    }

    void DumpSelectionDiagnostics()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[SelDiag] ══ DUMP: {failedClicks.Count} failed click(s) ══");
        for (int i = 0; i < failedClicks.Count; i++)
        {
            var fc = failedClicks[i];
            sb.AppendLine($"  Click {i + 1}: mouse={fc.mouseScreen} selRadius={fc.selRadius:F3} " +
                          $"pixelR={fc.selPixelRadius:F1}");
            sb.AppendLine($"    cam pos={fc.camPos:F3}  fwd={fc.camFwd:F3}");
            sb.AppendLine($"    ray origin={fc.ray.origin:F3}  dir={fc.ray.direction:F3}");
            sb.AppendLine($"    Nearest nodes by DistToRay:");
            foreach (var n in fc.nearest)
                sb.AppendLine($"      node={n.id,4}  distToRay={n.dist:F4}  isRoot={n.isRoot}  pos={n.worldPos:F3}" +
                              (n.dist <= fc.selRadius ? "  ← IN RADIUS" : "  ← OUTSIDE"));
        }
        sb.AppendLine("[SelDiag] ══ Markers placed at candidate nodes — trim the one you meant. ══");
        Debug.LogWarning(sb.ToString());

        SpawnDiagMarkers();
        failedClicks.Clear();
        diagArmed = false;
    }

    void SpawnDiagMarkers()
    {
        ClearDiagMarkers();

        // Collect unique root-node world positions from all failed clicks (top 3 each).
        var seen = new System.Collections.Generic.HashSet<int>();
        foreach (var fc in failedClicks)
        {
            int shown = 0;
            foreach (var n in fc.nearest)
            {
                if (!n.isRoot || seen.Contains(n.id)) continue;
                seen.Add(n.id);

                // Cyan sphere = in radius, orange = outside
                bool inRadius = n.dist <= fc.selRadius;
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = $"_DiagMarker_node{n.id}";
                Destroy(go.GetComponent<Collider>());
                go.transform.position   = n.worldPos;
                go.transform.localScale = Vector3.one * 0.06f;
                var mat = new Material(Shader.Find("Unlit/Color"))
                    { color = inRadius ? new Color(0f, 1f, 1f) : new Color(1f, 0.5f, 0f) };
                go.GetComponent<Renderer>().material = mat;
                diagMarkers.Add(go);
                if (++shown >= 3) break;
            }
        }
    }

    void ClearDiagMarkers()
    {
        foreach (var go in diagMarkers)
            if (go != null) Destroy(go);
        diagMarkers.Clear();
    }

    // ── Trim ──────────────────────────────────────────────────────────────────

    void HandleTrimHover()
    {
        Ray      ray  = cam.ScreenPointToRay(Input.mousePosition);
        TreeNode node = PickNode(ray, out _, n => n != skeleton.root && !n.isRoot);
        if (node != null)
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
        TreeNode node = PickNode(ray, out _, n => n != skeleton.root && (!n.isRoot || n.isAirLayerRoot) && !n.hasWire);
        if (node != null)
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
        // Training wires (Ishitsuki) are locked until fully set — exclude them while silver
        TreeNode node = PickNode(ray, out _, n => n.hasWire);
        if (node != null)
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

        // Check for Ishitsuki binding wire — locked until fully set (gold)
        var ishWire = skeleton.GetComponentInChildren<IshitsukiWire>();
        if (ishWire != null && ishWire.IsNearRay(ray, 0.15f))
        {
            if (ishWire.WireSetProgress >= 1f)
            {
                // Reuse SingleGreen highlight on the tree mesh as a visual cue
                SetHighlight(skeleton.root, HighlightMode.SingleGreen);
                if (Input.GetMouseButtonDown(0))
                {
                    Destroy(ishWire.gameObject);
                    SetHighlight(null, HighlightMode.None);
                }
            }
            return;
        }

        SetHighlight(null, HighlightMode.None);
    }

    // ── Paste ─────────────────────────────────────────────────────────────────

    void HandlePasteHover()
    {
        Ray      ray  = cam.ScreenPointToRay(Input.mousePosition);
        TreeNode node = PickNode(ray, out _, n => n.hasWound && !n.pasteApplied);
        if (node != null)
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

    // ── Pinch ─────────────────────────────────────────────────────────────────

    void HandlePinchHover()
    {
        Ray      ray  = cam.ScreenPointToRay(Input.mousePosition);
        // Only target terminal, growing, non-root branch nodes
        TreeNode node = PickNode(ray, out _, n => n.isTerminal && n.isGrowing && !n.isRoot && !n.isTrimmed);
        if (node != null)
        {
            SetHighlight(node, HighlightMode.Pinch);
            if (Input.GetMouseButtonDown(0))
            {
                SetHighlight(null, HighlightMode.None);
                skeleton.PinchNode(node);
            }
            return;
        }
        SetHighlight(null, HighlightMode.None);
    }

    // ── Defoliate ─────────────────────────────────────────────────────────────

    void HandleDefoliateHover()
    {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        var leafManager = skeleton.GetComponent<LeafManager>();

        // Target terminal non-root nodes that currently have a leaf cluster
        TreeNode node = PickNode(ray, out _,
            n => n.isTerminal && !n.isRoot && !n.isTrimmed && leafManager != null && leafManager.NodeHasLeaves(n.id));

        if (node != null)
        {
            SetHighlight(node, HighlightMode.Defoliate);
            if (Input.GetMouseButtonDown(0))
            {
                SetHighlight(null, HighlightMode.None);
                leafManager.DefoliateNode(node);
            }
            return;
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
            case HighlightMode.AirLayer:    highlightMat.color = ColAirLayer;   break;
            case HighlightMode.Pinch:       highlightMat.color = ColPinch;      break;
            case HighlightMode.Defoliate:   highlightMat.color = ColDefoliate;  break;
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
