using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.InputSystem;

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

    enum HighlightMode { None, TrimSubtree, SingleGold, SingleGreen, WireRun, Paste, AirLayer, Pinch, Defoliate, GraftSource, GraftTarget, PromoteHover }

    TreeNode        highlightedNode;
    HighlightMode   highlightMode = HighlightMode.None;
    List<TreeNode>  highlightedRun = new List<TreeNode>();

    // ── Tool highlight palette ─────────────────────────────────────────
    //
    // NOT readonly: Settings ▸ Accessibility repalettes these through
    // ApplyHighlightPalette (PLAN R2). They are private to this file, so making them
    // assignable is contained.
    //
    // The defaults below are the original hand-picked set. Note the problem they have:
    // trim / wire / unwire are red / gold / green, which is the least distinguishable
    // possible triad under deuteranopia and protanopia — on the three most
    // consequential actions in the game. Hence the alternative palettes.

    static Color ColTrim       = new Color(0.9f, 0.1f, 0.1f);   // red
    static Color ColWire       = new Color(0.9f, 0.65f, 0.1f);  // gold
    static Color ColRemoveWire = new Color(0.1f, 0.8f, 0.3f);   // green (single node)
    static Color ColWireRun    = new Color(0.0f, 1.0f, 0.55f);  // bright green (full run)
    static Color ColPaste      = new Color(0.2f, 0.8f, 0.9f);   // cyan (wound with paste)
    static Color ColAirLayer   = new Color(0.0f, 0.85f, 0.85f); // teal (air layer placement)
    static Color ColRootWork   = new Color(0.9f, 0.45f, 0.1f);  // orange (root work)
    static Color ColPinch      = new Color(0.55f, 1.0f, 0.15f); // lime-green (pinch tip)
    static Color ColDefoliate  = new Color(1.0f,  0.75f, 0.0f); // amber (defoliate)
    static Color ColGraft      = new Color(0.55f, 1.0f,  0.45f); // pale green (graft source)
    static Color ColGraftTgt   = new Color(1.0f,  0.9f,  0.3f);  // gold (graft target)

    /// <summary>
    /// Repalettes the hover highlights for a colour-vision mode. Called by
    /// <see cref="GameSettings.Colorblind"/>, including once at startup.
    ///
    /// <para>The alternative palettes are built from the <b>Okabe–Ito</b> set, which is designed
    /// so every pair stays distinguishable under the common dichromacies. Within each palette the
    /// three critical actions — trim, wire, unwire — are separated along the axis that mode can
    /// still see: blue–yellow for the red–green deficiencies, red–green for tritanopia.</para>
    ///
    /// <para>Colour is not the only cue being relied on here — trim highlights a whole subtree
    /// while wire highlights a single node or a run — but it is the fastest one, and these are
    /// destructive operations.</para>
    /// </summary>
    public static void ApplyHighlightPalette(GameSettings.ColorblindMode mode)
    {
        // Okabe–Ito reference values.
        var orange       = new Color(0.902f, 0.624f, 0.000f);
        var skyBlue      = new Color(0.337f, 0.706f, 0.914f);
        var bluishGreen  = new Color(0.000f, 0.620f, 0.451f);
        var yellow       = new Color(0.941f, 0.894f, 0.259f);
        var blue         = new Color(0.000f, 0.447f, 0.698f);
        var vermillion   = new Color(0.835f, 0.369f, 0.000f);
        var purple       = new Color(0.800f, 0.475f, 0.655f);

        switch (mode)
        {
            case GameSettings.ColorblindMode.Deuteranopia:
            case GameSettings.ColorblindMode.Protanopia:
                // Red and green collapse together; separate along blue–yellow instead.
                ColTrim       = vermillion;
                ColWire       = blue;
                ColRemoveWire = skyBlue;
                ColWireRun    = yellow;
                ColPaste      = purple;
                ColAirLayer   = skyBlue;
                ColRootWork   = orange;
                ColPinch      = yellow;
                ColDefoliate  = orange;
                ColGraft      = purple;
                ColGraftTgt   = yellow;
                break;

            case GameSettings.ColorblindMode.Tritanopia:
                // Blue and yellow collapse; red–green separation is available again.
                ColTrim       = vermillion;
                ColRemoveWire = bluishGreen;
                ColWire       = purple;
                ColWireRun    = bluishGreen;
                ColPaste      = skyBlue;
                ColAirLayer   = bluishGreen;
                ColRootWork   = vermillion;
                ColPinch      = bluishGreen;
                ColDefoliate  = purple;
                ColGraft      = bluishGreen;
                ColGraftTgt   = purple;
                break;

            default:
                ColTrim       = new Color(0.9f, 0.1f, 0.1f);
                ColWire       = new Color(0.9f, 0.65f, 0.1f);
                ColRemoveWire = new Color(0.1f, 0.8f, 0.3f);
                ColWireRun    = new Color(0.0f, 1.0f, 0.55f);
                ColPaste      = new Color(0.2f, 0.8f, 0.9f);
                ColAirLayer   = new Color(0.0f, 0.85f, 0.85f);
                ColRootWork   = new Color(0.9f, 0.45f, 0.1f);
                ColPinch      = new Color(0.55f, 1.0f, 0.15f);
                ColDefoliate  = new Color(1.0f,  0.75f, 0.0f);
                ColGraft      = new Color(0.55f, 1.0f,  0.45f);
                ColGraftTgt   = new Color(1.0f,  0.9f,  0.3f);
                break;
        }
    }

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

    // ── Saw sub-state ─────────────────────────────────────────────────────────

    [Tooltip("Pixels of mouse travel ACROSS the branch in one direction before a reversal counts as a half-stroke.")]
    [SerializeField] float sawMinStrokePx = 20f;

    [Tooltip("Back-and-forths needed to saw through the thinnest twig.")]
    [SerializeField] int sawMinBackForths = 2;
    [Tooltip("Back-and-forths needed to saw through wood as thick as the trunk base. Scales linearly with radius in between.")]
    [SerializeField] int sawMaxBackForths = 10;

    bool     isSawing;
    TreeNode sawTarget;
    float    sawProgress;       // 0→1
    Vector2  sawLastMouse;
    float    sawCurrentDir;     // +1 or -1
    float    sawDirTravel;      // pixels traveled in current direction
    int      sawHalfStrokes;
    int      sawNeededHalfStrokes;  // set per branch in StartSawing
    bool     sawStrokeSounded;  // this stroke's rasp has played

    // ── Selection cursor (screen-space GL circle) ─────────────────────────────
    Material glCircleMat;
    float    selCirclePixelRadius;
    Color    selCircleColor;

    // ── Pinch tip markers ─────────────────────────────────────────────────────
    // Hovered pinchable node, updated each frame in HandlePinchHover.
    TreeNode hoveredPinchNode;

    // ── Promotion Advisor state ───────────────────────────────────────────────
    TreeNode hoveredPromoteNode;
    TreeNode lockedPromoteTarget;
    List<(TreeNode node, float score)> promotionScores = new List<(TreeNode, float)>();

    static readonly Color ColPromoteTarget  = new Color(0.2f,  0.9f,  1.0f, 1.0f);
    static readonly Color ColPromoteTrim    = new Color(0.9f,  0.15f, 0.15f, 1.0f);
    static readonly Color ColPromotePinch   = new Color(0.55f, 1.0f,  0.15f, 1.0f);
    static readonly Color ColPromoteReduce  = new Color(0.9f,  0.65f, 0.1f,  1.0f);
    static readonly Color ColPromoteNeutral = new Color(0.35f, 0.35f, 0.35f, 0.45f);

    public TreeNode LockedPromoteTarget => lockedPromoteTarget;
    public System.Collections.Generic.IReadOnlyList<(TreeNode node, float score)> PromotionScores => promotionScores;

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
        // Drawn as a screen-space GL circle via RenderPipelineManager.endCameraRendering
        // (OnRenderObject is not called in URP).
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

    void OnEnable()
    {
        FocusGuard.FocusLost += OnFocusLost;
        RenderPipelineManager.endCameraRendering += DrawSelectionCircle;
        RenderPipelineManager.endCameraRendering += DrawGraftLines;
        RenderPipelineManager.endCameraRendering += DrawPinchMarkers;
        RenderPipelineManager.endCameraRendering += DrawPromotionOverlays;
    }
    void OnDisable()
    {
        FocusGuard.FocusLost -= OnFocusLost;
        RenderPipelineManager.endCameraRendering -= DrawSelectionCircle;
        RenderPipelineManager.endCameraRendering -= DrawGraftLines;
        RenderPipelineManager.endCameraRendering -= DrawPinchMarkers;
        RenderPipelineManager.endCameraRendering -= DrawPromotionOverlays;
    }

    // Draws a screen-space circle at the cursor after URP finishes rendering the camera.
    // OnRenderObject is not invoked by URP; endCameraRendering fires instead.
    void DrawSelectionCircle(ScriptableRenderContext ctx, Camera camera)
    {
        if (camera != cam || glCircleMat == null || selCirclePixelRadius <= 0f) return;

        Vector2 mouse = Mouse.current.position.ReadValue();
        glCircleMat.SetPass(0);
        GL.PushMatrix();
        GL.LoadPixelMatrix(0, camera.pixelWidth, 0, camera.pixelHeight);
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

    // Draws world-space graft lines: amber for in-progress grafts, green for pending source.
    void DrawGraftLines(ScriptableRenderContext ctx, Camera camera)
    {
        if (camera != cam || glCircleMat == null) return;
        if (skeleton == null) return;

        bool hasPending  = skeleton.pendingGraftSource != null;
        bool hasAttempts = skeleton.graftAttempts.Count > 0;
        if (!hasPending && !hasAttempts) return;

        glCircleMat.SetPass(0);
        GL.PushMatrix();
        GL.LoadProjectionMatrix(camera.projectionMatrix);
        GL.modelview = camera.worldToCameraMatrix;
        GL.Begin(GL.LINES);

        // Pending source → mouse cursor in world (dashed look via short lines)
        if (hasPending)
        {
            GL.Color(new Color(0.55f, 1.0f, 0.45f, 0.85f));
            var src = skeleton.pendingGraftSource;
            Vector3 srcWorld = skeleton.transform.TransformPoint(src.tipPosition);
            // Draw a pulsing dot at the pending source tip
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2f;
                float r = 0.04f;
                GL.Vertex(srcWorld + new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0));
                GL.Vertex(srcWorld + new Vector3(Mathf.Cos(a + Mathf.PI / 4f) * r, Mathf.Sin(a + Mathf.PI / 4f) * r, 0));
            }
        }

        // In-progress graft attempts — amber line source tip → target
        if (hasAttempts)
        {
            var nodes = skeleton.allNodes;
            foreach (var g in skeleton.graftAttempts)
            {
                if (g.succeeded) continue;
                TreeNode src = nodes.Find(n => n.id == g.sourceId);
                TreeNode tgt = nodes.Find(n => n.id == g.targetId);
                if (src == null || tgt == null) continue;

                float t  = (float)g.seasonsElapsed / Mathf.Max(1, skeleton.GraftSeasonsToFuse);
                Color c  = Color.Lerp(new Color(0.9f, 0.6f, 0.1f, 0.7f),
                                      new Color(0.4f, 0.9f, 0.4f, 0.9f), t);
                GL.Color(c);
                GL.Vertex(skeleton.transform.TransformPoint(src.tipPosition));
                GL.Vertex(skeleton.transform.TransformPoint(tgt.worldPosition));
            }
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
            case ToolType.Graft:      return skeleton?.pendingGraftSource != null ? ColGraftTgt : ColGraft;
            case ToolType.Jin:        return new Color(0.80f, 0.77f, 0.72f);   // weathered silver
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

    /// <summary>Window lost focus mid-gesture (2026-10-29): end anything that relies on a held
    /// button, so nothing is left running or half-done when the player comes back.</summary>
    void OnFocusLost()
    {
        EndPour();
        if (isSawing) CancelSaw();
        if (wirePhase == WirePhase.Aiming) CancelWire();
    }

    void Update()
    {
        // The click that brings the window back must not trim, water or wire (FocusGuard).
        if (FocusGuard.SwallowingClick) return;

        // Nothing on the tree while a menu, dialog or tooltip is open, and no press that lands on
        // the interface (2026-10-29). UI buttons fire on release but the tools act on press, so
        // pressing a tooltip's OK or the pause menu's Quit Game also started a wire on the tree
        // underneath — and the wire then "restored" the tooltip state after every wire.
        var gm = GameManager.Instance;
        if (gm != null && GameManager.IsOverlayState(gm.state)) return;
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && CursorManager.PointerOverUI())
            return;

        if (skeleton.root == null)
        {
            SetHighlight(null, HighlightMode.None);
            if (GameManager.canWater) HandlePlantingWater();
            return;
        }

        // Clear pending graft selection if the tool was switched away
        if (!GameManager.canGraft && skeleton.pendingGraftSource != null)
            skeleton.CancelPendingGraft();

        // Clear promotion target if tool was switched away
        if (!GameManager.canPromote && lockedPromoteTarget != null)
        {
            lockedPromoteTarget = null;
            promotionScores.Clear();
        }

        UpdateSelectionCursor();
        hoveredPinchNode   = null;
        hoveredPromoteNode = null;

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

        // Saw sub-state — block all other interaction while sawing
        if (isSawing)
        {
            UpdateSawing();
            return;
        }

        var gstate = GameManager.Instance.state;
        if (GameManager.canRootRake)
        {
            HandleRootRakeHover();
            return;
        }
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
        else if (GameManager.canGraft)
            HandleGraftHover();
        else if (GameManager.canPromote)
            HandlePromoteHover();
        else if (GameManager.canWater)
            HandleWaterHover();
        else if (GameManager.canJin)
            HandleJinHover();
        else
        {
            SetHighlight(null, HighlightMode.None);
            HandleInspect();
        }
    }

    // ── Root work (RootPrune mode) ────────────────────────────────────────────

    // Y coordinate of the soil plane in world space.
    // Assumes the tree was planted at world y = 0.
    const float SOIL_WORLD_Y = 0f;

    void HandleRootWorkHover()
    {
        Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());

        // Diagnostic: F key dumps recorded failed clicks.
        if (Keyboard.current.fKey.wasPressedThisFrame && diagArmed)
            DumpSelectionDiagnostics();

        // Priority 1: trim an existing root.
        TreeNode rootNode = PickNode(ray, out RaycastHit hit, n => n.isRoot && n != skeleton.root);
        if (rootNode != null)
        {
            SetHighlight(rootNode, HighlightMode.TrimSubtree);
            if (Mouse.current.leftButton.wasPressedThisFrame)
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
        if (Mouse.current.leftButton.wasPressedThisFrame)
            RecordFailedClick(ray);


        // Priority 2: plant a new root by clicking the planting surface.
        Plane soilPlane = new Plane(skeleton.plantingNormal, skeleton.plantingSurfacePoint);
        if (soilPlane.Raycast(ray, out float dist))
        {
            SetHighlight(null, HighlightMode.None);

            if (Mouse.current.leftButton.wasPressedThisFrame)
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
        if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            if (Physics.Raycast(ray, out RaycastHit surfaceHit) &&
                surfaceHit.collider.gameObject != gameObject)
            {
                skeleton.SetPlantingSurface(surfaceHit.point, surfaceHit.normal);
            }
        }

        SetHighlight(null, HighlightMode.None);
    }

    // ── Root Rake mode ────────────────────────────────────────────────────────

    // Minimum absolute vertical mouse movement (screen pixels) per frame to count as a rake stroke.
    const float RakeMinDeltaY = 5f;

    void HandleRootRakeHover()
    {
        var rakeManager = skeleton.GetComponent<RootRakeManager>();
        if (rakeManager == null) return;

        // ESC cancels rake mode
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            rakeManager.CancelRakeMode();
            return;
        }

        Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());

        // Priority 1: rake — wherever the ray is on the soil ball, strokes rake it.
        // RaycastAll because the pot/table/tree colliders sit in front of the ball's
        // lower half (same lesson as weed pulling). Root pruning only takes over where
        // the ball has been cleared (or roots poke out beyond it).
        if (rakeManager.SoilBallGO != null)
        {
            var hits = Physics.RaycastAll(ray, 200f);
            foreach (var h in hits)
            {
                if (h.collider == null || h.collider.gameObject != rakeManager.SoilBallGO) continue;
                SetHighlight(null, HighlightMode.None);
                // Only rake on significant vertical movement (up or down — either counts)
                float deltaY = Mouse.current.delta.ReadValue().y;
                if (Mathf.Abs(deltaY) >= RakeMinDeltaY)
                    rakeManager.RakeAt(h.point);
                return;
            }
        }

        // Priority 2: click a root node tip to prune it (same as RootWork)
        TreeNode rootNode = PickNode(ray, out _, n => n.isRoot && n != skeleton.root);
        if (rootNode != null)
        {
            SetHighlight(rootNode, HighlightMode.TrimSubtree);
            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                SetHighlight(null, HighlightMode.None);
                skeleton.TrimNode(rootNode);
            }
            return;
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
            mouseScreen   = Mouse.current.position.ReadValue(),
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
        Ray      ray  = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        // Branches as always, plus EXPOSED roots — Ishitsuki rock cables (isTrainingWire)
        // and air-layer roots — so the player can trim a root on the rock like a branch.
        // (Buried pot roots stay out of the clippers; those are managed in RootPrune mode.)
        TreeNode node = PickNode(ray, out _,
            n => n != skeleton.root && (!n.isRoot || n.isTrainingWire || n.isAirLayerRoot));
        if (node != null)
        {
            SetHighlight(node, HighlightMode.TrimSubtree);
            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                var  tool     = ToolManager.Instance.ActiveTool;
                bool tooThick = node.radius >= skeleton.SawRadiusThreshold;
                if (tool == ToolType.Saw)
                {
                    // The saw never cuts in one click (2026-10-29) — even a twig takes a couple of strokes.
                    StartSawing(node);
                    Debug.Log($"[Saw] Start node={node.id} r={node.radius:F3} trunk={skeleton.root.radius:F3} " +
                              $"halfStrokes={sawNeededHalfStrokes}");
                }
                else if (tooThick)
                {
                    Debug.Log($"[Saw] Clippers refused node={node.id} r={node.radius:F3} " +
                              $"limit={skeleton.SawRadiusThreshold:F3} trunk={skeleton.root.radius:F3}");
                    // Clippers can't take wood this thick (2026-10-28) — the saw is the only way.
                    if (inspectUi == null) inspectUi = FindAnyObjectByType<ButtonClicker>();
                    string name = tool == ToolType.BigClippers ? "big clippers" : "small clippers";
                    inspectUi?.ShowToolHint(
                        $"That branch is too thick for the {name}. Press {KeyBindings.Display(BindAction.NextTool)} " +
                        "until the tool reads Saw, then move the mouse back and forth across the branch to cut through.");
                }
                else
                {
                    SetHighlight(null, HighlightMode.None);
                    CursorManager.PlayUse();
                    skeleton.TrimNode(node);
                }
            }
            return;
        }
        SetHighlight(null, HighlightMode.None);
    }

    // ── Watering can ────────────────────────────────────────────────────────────

    // Pouring (2026-10-28): a click is a sip, holding keeps pouring, so the player decides how
    // wet the pot gets. Real seconds, not game time, so it feels the same at any speed.
    const float PourSip  = 0.08f;   // moisture from a single click
    const float PourRate = 0.45f;   // moisture per real second while held (empty → full ≈ 2 s)
    bool  pouring;
    float pourAnimTimer, pourStartMoisture;

    void EndPour()
    {
        if (!pouring) return;
        pouring = false;
        Debug.Log($"[Water] Poured {pourStartMoisture:F2} → {skeleton.soilMoisture:F2} | " +
                  $"year={GameManager.year} month={GameManager.month}");
    }

    /// <summary>
    /// The watering can (R4, 2026-10-27): click the soil to water. Water poured over the canopy
    /// still reaches the soil, but every branch whose leaves sit between the can and the soil gets
    /// soaked, which raises its fungal risk (<see cref="TreeSkeleton.WetFoliage"/>). A click that
    /// misses both the soil and the tree does nothing. Holding the button keeps pouring.
    /// </summary>
    void HandleWaterHover()
    {
        SetHighlight(null, HighlightMode.None);

        var s = GameManager.Instance.state;
        bool canPour = s == GameState.Idle     || s == GameState.BranchGrow || s == GameState.TimeGo ||
                       s == GameState.LeafGrow || s == GameState.LeafFall;

        if (pouring)
        {
            if (!canPour || !Mouse.current.leftButton.isPressed) { EndPour(); return; }
            skeleton.AddWater(PourRate * Time.unscaledDeltaTime);
            pourAnimTimer += Time.unscaledDeltaTime;
            if (pourAnimTimer >= 0.45f) { pourAnimTimer = 0f; CursorManager.PlayUse(); }
            return;
        }

        if (!canPour || !Mouse.current.leftButton.wasPressedThisFrame || CursorManager.PointerOverUI()) return;

        Ray   ray      = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        var   pot      = skeleton.GetComponent<PotSoil>();
        Vector3 soilPoint = default;
        bool  hitSoil  = pot != null && pot.RaycastSoil(ray, out soilPoint);
        float soilDist = hitSoil ? Vector3.Distance(ray.origin, soilPoint) : float.MaxValue;

        // Leaves in front of the soil (or anywhere on the ray, if the soil was missed) catch it.
        var soaked = new HashSet<TreeNode>();
        foreach (var leaf in GetComponentsInChildren<Leaf>())
        {
            if (leaf.ownerNode == null) continue;
            var r = leaf.GetComponentInChildren<Renderer>();
            if (r != null && r.bounds.IntersectRay(ray, out float d) && d < soilDist)
                soaked.Add(leaf.ownerNode);
        }

        if (!hitSoil && soaked.Count == 0) return;

        AudioManager.Instance?.PlaySFX("Water");
        CursorManager.PlayUse();
        pouring           = true;
        pourAnimTimer     = 0f;
        pourStartMoisture = skeleton.soilMoisture;
        skeleton.AddWater(PourSip);   // over the leaves or not, the water ends up in the pot

        if (soaked.Count > 0)
        {
            skeleton.WetFoliage(soaked);
            FindAnyObjectByType<ButtonClicker>()?.ShowFoliageWateringTip();
        }
    }

    // ── Inspect (no tool) ───────────────────────────────────────────────────────

    // A click (press and release without dragging) with no tool on a leaf or branch shows what is
    // wrong with that branch (2026-10-28). Press/release rather than press, so orbiting the camera
    // doesn't pop a card.
    Vector2 inspectPressPos;
    bool    inspectPressed;
    LeafManager inspectLeaves;
    ButtonClicker inspectUi;

    void HandleInspect()
    {
        var mouse = Mouse.current;
        if (mouse.leftButton.wasPressedThisFrame && !CursorManager.PointerOverUI())
        {
            inspectPressed  = true;
            inspectPressPos = mouse.position.ReadValue();
            return;
        }
        if (!inspectPressed || !mouse.leftButton.wasReleasedThisFrame) return;
        inspectPressed = false;
        Vector2 pos = mouse.position.ReadValue();
        if ((pos - inspectPressPos).sqrMagnitude > 36f) return;   // a drag, not a click

        if (inspectLeaves == null) inspectLeaves = skeleton.GetComponent<LeafManager>();
        if (inspectUi == null) inspectUi = FindAnyObjectByType<ButtonClicker>();
        if (inspectLeaves == null || inspectUi == null) return;

        Ray ray = cam.ScreenPointToRay(pos);
        TreeNode node = null;
        float best = float.MaxValue;
        foreach (var leaf in GetComponentsInChildren<Leaf>())
        {
            if (leaf.ownerNode == null) continue;
            var r = leaf.GetComponentInChildren<Renderer>();
            if (r != null && r.bounds.IntersectRay(ray, out float d) && d < best) { best = d; node = leaf.ownerNode; }
        }
        if (node == null) node = PickNode(ray, out _, n => !n.isRoot && !n.isTrimmed);

        inspectUi.ShowInspectCard(node != null ? inspectLeaves.Diagnose(node) : null, pos);
    }

    /// <summary>Before there is a tree: the first can-on-soil click waters the seed in.</summary>
    void HandlePlantingWater()
    {
        if (!GameManager.AwaitingPlanting) return;
        if (!Mouse.current.leftButton.wasPressedThisFrame || CursorManager.PointerOverUI()) return;

        var pot = skeleton.GetComponent<PotSoil>();
        Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (pot == null || !pot.RaycastSoil(ray, out _)) return;

        AudioManager.Instance?.PlaySFX("Water");
        CursorManager.PlayUse();
        GameManager.Instance.PlantSeed();
    }

    // ── Jin (strip a branch to deadwood) ──────────────────────────────────────

    void HandleJinHover()
    {
        Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        // Live branches only — no roots, no trunk base, nothing already jinned.
        TreeNode node = PickNode(ray, out _,
            n => n != skeleton.root && !n.isRoot && !n.isJin && !n.isTrimmed);
        if (node != null)
        {
            SetHighlight(node, HighlightMode.TrimSubtree);
            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                SetHighlight(null, HighlightMode.None);
                AudioManager.Instance?.PlaySFX("Trim");
                skeleton.JinNode(node);
            }
            return;
        }
        SetHighlight(null, HighlightMode.None);
    }

    // ── Saw mechanic ──────────────────────────────────────────────────────────

    void StartSawing(TreeNode node)
    {
        sawTarget      = node;
        sawProgress    = 0f;
        sawHalfStrokes = 0;
        float trunkR   = skeleton.root != null ? skeleton.root.radius : 0f;
        float t        = trunkR > 0f ? Mathf.Clamp01(node.radius / trunkR) : 1f;
        sawNeededHalfStrokes = 2 * Mathf.RoundToInt(Mathf.Lerp(sawMinBackForths, sawMaxBackForths, t));
        sawCurrentDir  = 0f;
        sawDirTravel   = 0f;
        sawLastMouse   = Mouse.current.position.ReadValue();
        sawStrokeSounded = false;
        isSawing       = true;
        SetHighlight(node, HighlightMode.TrimSubtree);
    }

    void UpdateSawing()
    {
        // Guard: node might have been trimmed or tree rebuilt externally
        if (sawTarget == null || sawTarget.isTrimmed || !GameManager.canTrim)
        {
            CancelSaw();
            return;
        }

        // ESC, RMB, or any click on a different target → cancel
        if (Keyboard.current.escapeKey.wasPressedThisFrame
            || Mouse.current.rightButton.wasPressedThisFrame)
        {
            CancelSaw();
            return;
        }

        // Strokes run ACROSS the branch as it appears on screen (2026-10-28) — like a real saw,
        // not a fixed left-right wiggle. Movement along the branch doesn't count.
        Vector2 mouse  = Mouse.current.position.ReadValue();
        Vector2 across = SawAcrossScreenDir();
        float   dx     = Vector2.Dot(mouse - sawLastMouse, across);
        sawLastMouse = mouse;

        if (Mathf.Abs(dx) > 0.5f)
        {
            float dir = Mathf.Sign(dx);
            if (dir != sawCurrentDir && sawCurrentDir != 0f)
            {
                // Reversed direction — count a half-stroke if enough travel, flick splinters
                if (sawDirTravel >= sawMinStrokePx)
                {
                    sawHalfStrokes++;
                    SawEffects.EmitSplinters(SawKerfWorld(), SawStrokeWorldDir(across, sawCurrentDir), SawFxScale());
                }
                sawDirTravel     = 0f;
                sawStrokeSounded = false;
            }
            sawCurrentDir = dir;
            sawDirTravel += Mathf.Abs(dx);

            // One rasp per stroke, once the blade is properly moving.
            if (!sawStrokeSounded && sawDirTravel >= sawMinStrokePx * 0.5f)
            {
                sawStrokeSounded = true;
                SawEffects.PlayStroke(dir > 0f);
            }
            SawEffects.EmitDust(SawKerfWorld(), SawStrokeWorldDir(across, dir), SawFxScale(), Mathf.Abs(dx));
        }

        sawProgress = Mathf.Clamp01((float)sawHalfStrokes / sawNeededHalfStrokes);
        SetHighlight(sawTarget, HighlightMode.TrimSubtree);

        if (sawHalfStrokes >= sawNeededHalfStrokes)
            CompleteSaw();
    }

    void CompleteSaw()
    {
        var node = sawTarget;
        SetHighlight(null, HighlightMode.None);
        isSawing    = false;
        sawTarget   = null;
        sawProgress = 0f;
        AudioManager.Instance?.PlaySFX("Trim");
        skeleton.TrimNode(node);
    }

    void CancelSaw()
    {
        SetHighlight(null, HighlightMode.None);
        isSawing    = false;
        sawTarget   = null;
        sawProgress = 0f;
        sawHalfStrokes = 0;
    }

    /// <summary>Screen-space unit vector perpendicular to the branch being sawn. Falls back to
    /// horizontal when the branch points at the camera and has no on-screen direction.</summary>
    Vector2 SawAcrossScreenDir()
    {
        var t = skeleton.transform;
        Vector2 a = cam.WorldToScreenPoint(t.TransformPoint(sawTarget.worldPosition));
        Vector2 b = cam.WorldToScreenPoint(t.TransformPoint(sawTarget.tipPosition));
        Vector2 d = b - a;
        if (d.sqrMagnitude < 36f) return Vector2.right;
        d.Normalize();
        return new Vector2(-d.y, d.x);
    }

    /// <summary>The cut point on the camera-facing side of the branch, in world space.</summary>
    Vector3 SawKerfWorld()
    {
        Vector3 c = skeleton.transform.TransformPoint(sawTarget.worldPosition);
        Vector3 toCam = (cam.transform.position - c).normalized;
        return c + toCam * sawTarget.radius * skeleton.transform.lossyScale.x;
    }

    Vector3 SawStrokeWorldDir(Vector2 across, float sign) =>
        (cam.transform.right * across.x + cam.transform.up * across.y).normalized * sign;

    /// <summary>Effect size follows the branch thickness, so a trunk chop throws more than a limb.</summary>
    float SawFxScale() => Mathf.Clamp(sawTarget.radius * skeleton.transform.lossyScale.x * 12f, 0.25f, 1.5f);

    // ── Wire placement ────────────────────────────────────────────────────────

    void HandleWireHover()
    {
        Ray      ray  = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        // The trunk's first segment (skeleton.root) IS wireable. It used to be excluded because a
        // bend rotated every descendant, and the underground roots hang off the root node — wiring
        // it would have swung the root system out of the soil. RotateAndPropagateDescendants now
        // skips ground-anchored roots (since 2026-06-11), so the exclusion was a leftover guard and
        // blocked shaping the base of the trunk (removed 2026-10-27). Buried roots stay unwireable.
        TreeNode node = PickNode(ray, out _, n => (!n.isRoot || n.isAirLayerRoot) && !n.hasWire);
        if (node != null)
        {
            SetHighlight(node, HighlightMode.SingleGold);
            if (Mouse.current.leftButton.wasPressedThisFrame)
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
        // Never come back to a menu or dialog state when the wire is done.
        if (GameManager.IsOverlayState(preWireState))
            preWireState = GameManager.Instance.StateForMonth(GameManager.month);

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
        Ray     ray            = cam.ScreenPointToRay(Mouse.current.position.ReadValue());

        if (aimPlane.Raycast(ray, out float dist))
        {
            Vector3 dir = ray.GetPoint(dist) - branchWorldPos;
            if (dir.sqrMagnitude > 0.01f)
                aimDirection = dir.normalized;
        }

        UpdateAimPreview();
        SetHighlight(wireTarget, HighlightMode.SingleGold);

        if (Mouse.current.leftButton.wasPressedThisFrame) { ConfirmWire(); return; }
        if (Mouse.current.rightButton.wasPressedThisFrame || Keyboard.current.escapeKey.wasPressedThisFrame) CancelWire();
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
        ProgressionManager.Instance?.ReachMilestone("first_wire");   // player-applied wires only
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
                    || KeyBindings.Pressed(BindAction.SkipAnimation)
                    || GameSettings.ReducedMotion;   // reduced motion: the wire just snaps into place

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
        Ray      ray  = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
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

            if (Mouse.current.leftButton.wasPressedThisFrame)
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
                if (Mouse.current.leftButton.wasPressedThisFrame)
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
        Ray      ray  = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        TreeNode node = PickNode(ray, out _, n => n.hasWound && !n.pasteApplied);
        if (node != null)
        {
            SetHighlight(node, HighlightMode.Paste);
            if (Mouse.current.leftButton.wasPressedThisFrame)
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
        Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
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
                if (layer.rootsSpawned && Mouse.current.leftButton.wasPressedThisFrame)
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
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            skeleton.PlaceAirLayer(node);
            SetHighlight(null, HighlightMode.None);
        }
    }

    // ── Pinch ─────────────────────────────────────────────────────────────────

    void HandlePinchHover()
    {
        Ray      ray  = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        // Only target terminal, growing, non-root branch nodes
        TreeNode node = PickNode(ray, out _, n => n.isTerminal && n.isGrowing && !n.isRoot && !n.isTrimmed);
        hoveredPinchNode = node;
        if (node != null)
        {
            SetHighlight(node, HighlightMode.Pinch);
            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                SetHighlight(null, HighlightMode.None);
                skeleton.PinchNode(node);
            }
            return;
        }
        SetHighlight(null, HighlightMode.None);
    }

    // Draws world-space diamond markers at every pinchable tip when the pinch tool is active.
    // All tips get a small lime diamond; the hovered tip gets a larger bright one.
    void DrawPinchMarkers(ScriptableRenderContext ctx, Camera camera)
    {
        if (camera != cam || glCircleMat == null) return;
        if (!GameManager.canPinch || skeleton == null || skeleton.allNodes == null) return;

        glCircleMat.SetPass(0);
        GL.PushMatrix();
        GL.LoadProjectionMatrix(camera.projectionMatrix);
        GL.modelview = camera.worldToCameraMatrix;
        GL.Begin(GL.LINES);

        foreach (var node in skeleton.allNodes)
        {
            if (!node.isTerminal || !node.isGrowing || node.isRoot || node.isTrimmed) continue;

            Vector3 wp     = transform.TransformPoint(node.tipPosition);
            bool    hov    = node == hoveredPinchNode;
            float   r      = hov ? 0.12f : 0.055f;
            Color   col    = hov
                ? new Color(0.55f, 1.0f, 0.15f, 1.0f)   // bright lime
                : new Color(0.55f, 1.0f, 0.15f, 0.55f);  // dimmer for all tips

            GL.Color(col);
            DrawGLDiamond(wp, r, camera);
        }

        GL.End();
        GL.PopMatrix();
    }

    // Draws a 3-axis diamond (6 points) around a world position, sized in world units.
    // Assumes GL.Begin(GL.LINES) is already active.
    static void DrawGLDiamond(Vector3 center, float r, Camera cam)
    {
        // Use camera right/up so diamond always faces camera
        Vector3 rt = cam.transform.right   * r;
        Vector3 up = cam.transform.up      * r;
        Vector3 fw = cam.transform.forward * r;

        Vector3 px = center + rt;
        Vector3 nx = center - rt;
        Vector3 py = center + up;
        Vector3 ny = center - up;
        Vector3 pz = center + fw;
        Vector3 nz = center - fw;

        // Horizontal diamond (camera-facing)
        GL.Vertex(px); GL.Vertex(py);
        GL.Vertex(py); GL.Vertex(nx);
        GL.Vertex(nx); GL.Vertex(ny);
        GL.Vertex(ny); GL.Vertex(px);

        // Depth spikes
        GL.Vertex(py); GL.Vertex(pz);
        GL.Vertex(py); GL.Vertex(nz);
        GL.Vertex(ny); GL.Vertex(pz);
        GL.Vertex(ny); GL.Vertex(nz);
        GL.Vertex(px); GL.Vertex(pz);
        GL.Vertex(px); GL.Vertex(nz);
        GL.Vertex(nx); GL.Vertex(pz);
        GL.Vertex(nx); GL.Vertex(nz);
    }

    // ── Promotion Advisor ─────────────────────────────────────────────────────

    void HandlePromoteHover()
    {
        // ESC or RMB clears the locked target
        if ((Keyboard.current?.escapeKey.wasPressedThisFrame ?? false) ||
            Mouse.current.rightButton.wasPressedThisFrame)
        {
            lockedPromoteTarget = null;
            promotionScores.Clear();
            SetHighlight(null, HighlightMode.None);
            return;
        }

        Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        TreeNode node = PickNode(ray, out _, n => n.isTerminal && !n.isRoot && !n.isTrimmed && !n.isDead);
        hoveredPromoteNode = node;

        if (lockedPromoteTarget == null)
        {
            // Phase 1: hover + click to lock
            if (node != null)
            {
                SetHighlight(node, HighlightMode.PromoteHover);
                if (Mouse.current.leftButton.wasPressedThisFrame)
                {
                    lockedPromoteTarget = node;
                    RebuildPromotionScores();
                    SetHighlight(null, HighlightMode.None);
                }
            }
            else
                SetHighlight(null, HighlightMode.None);
        }
        else
        {
            // Phase 2: target locked — click a different tip to switch target
            if (node != null && node != lockedPromoteTarget && Mouse.current.leftButton.wasPressedThisFrame)
            {
                lockedPromoteTarget = node;
                RebuildPromotionScores();
            }
            SetHighlight(null, HighlightMode.None);
        }
    }

    void RebuildPromotionScores()
    {
        promotionScores.Clear();
        if (lockedPromoteTarget == null || skeleton == null) return;
        foreach (var n in skeleton.allNodes)
        {
            float s = skeleton.PromotionScore(n, lockedPromoteTarget);
            if (s >= 0f) promotionScores.Add((n, s));
        }
        promotionScores.Sort((a, b) => b.score.CompareTo(a.score));
    }

    void DrawPromotionOverlays(ScriptableRenderContext ctx, Camera camera)
    {
        if (camera != cam || glCircleMat == null) return;
        if (!GameManager.canPromote || skeleton == null || skeleton.allNodes == null) return;

        glCircleMat.SetPass(0);
        GL.PushMatrix();
        GL.LoadProjectionMatrix(camera.projectionMatrix);
        GL.modelview = camera.worldToCameraMatrix;
        GL.Begin(GL.LINES);

        // Locked target: large cyan circle
        if (lockedPromoteTarget != null)
        {
            GL.Color(ColPromoteTarget);
            DrawGLCircleWorld(transform.TransformPoint(lockedPromoteTarget.tipPosition), 0.18f, camera);
        }

        // Hover candidate (phase 1): dimmer cyan diamond
        if (lockedPromoteTarget == null && hoveredPromoteNode != null)
        {
            GL.Color(new Color(0.2f, 0.9f, 1.0f, 0.75f));
            DrawGLDiamond(transform.TransformPoint(hoveredPromoteNode.tipPosition), 0.14f, camera);
        }

        // All candidates when target is locked
        if (lockedPromoteTarget != null)
        {
            int rank = 0;
            foreach (var (node, score) in promotionScores)
            {
                string action = TreeSkeleton.PromotionAction(node, score);
                Color col = action == "Remove" ? ColPromoteTrim
                          : action == "Pinch"  ? ColPromotePinch
                          : action == "Reduce" ? ColPromoteReduce
                          :                      ColPromoteNeutral;
                float r   = rank < 5 ? Mathf.Lerp(0.065f, 0.11f, score) : 0.035f;
                col.a     = rank < 5 ? Mathf.Lerp(0.5f,   1.0f,  score) : 0.25f;
                GL.Color(col);
                Vector3 wp = transform.TransformPoint(node.isTerminal ? node.tipPosition : node.worldPosition);
                DrawGLDiamond(wp, r, camera);
                rank++;
            }
        }

        GL.End();
        GL.PopMatrix();
    }

    // Draws a camera-facing circle in world space. Assumes GL.Begin(GL.LINES) is active.
    static void DrawGLCircleWorld(Vector3 center, float r, Camera cam)
    {
        const int segs = 24;
        Vector3 rt = cam.transform.right * r;
        Vector3 up = cam.transform.up    * r;
        for (int i = 0; i < segs; i++)
        {
            float a1 = (float)i       / segs * Mathf.PI * 2f;
            float a2 = (float)(i + 1) / segs * Mathf.PI * 2f;
            GL.Vertex(center + Mathf.Cos(a1) * rt + Mathf.Sin(a1) * up);
            GL.Vertex(center + Mathf.Cos(a2) * rt + Mathf.Sin(a2) * up);
        }
    }

    // ── Defoliate ─────────────────────────────────────────────────────────────

    void HandleDefoliateHover()
    {
        Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        var leafManager = skeleton.GetComponent<LeafManager>();

        // Target any non-root node that has a leaf cluster — NOT restricted to terminal,
        // because by June most leaf-bearing nodes have already branched and are no longer terminal.
        TreeNode node = PickNode(ray, out _,
            n => !n.isRoot && !n.isTrimmed && leafManager != null && leafManager.NodeHasLeaves(n.id));

        if (node != null)
        {
            SetHighlight(node, HighlightMode.Defoliate);
            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                SetHighlight(null, HighlightMode.None);
                leafManager.DefoliateNode(node);
            }
            return;
        }
        SetHighlight(null, HighlightMode.None);
    }

    // ── Graft ─────────────────────────────────────────────────────────────────

    void HandleGraftHover()
    {
        // ESC / RMB cancels a pending source selection
        if (Mouse.current.rightButton.wasPressedThisFrame ||
            (UnityEngine.InputSystem.Keyboard.current?.escapeKey.wasPressedThisFrame ?? false))
        {
            skeleton.CancelPendingGraft();
            SetHighlight(null, HighlightMode.None);
            return;
        }

        Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());

        if (skeleton.pendingGraftSource == null)
        {
            // Phase 1: pick source — must be a living non-root terminal
            TreeNode node = PickNode(ray, out _,
                n => n.isTerminal && !n.isRoot && !n.isTrimmed && !n.isDead && !n.isGraftSource);

            SetHighlight(node, HighlightMode.GraftSource);

            if (node != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                skeleton.pendingGraftSource = node;
                SetHighlight(null, HighlightMode.None);
                Debug.Log($"[Graft] Source selected: node={node.id} depth={node.depth}");
            }
        }
        else
        {
            // Phase 2: pick target — any living non-root node, different ancestry, in range
            TreeNode src = skeleton.pendingGraftSource;
            TreeNode node = PickNode(ray, out _,
                n => n != src && !n.isRoot && !n.isTrimmed && !n.isDead);

            SetHighlight(node, HighlightMode.GraftTarget);

            if (node != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                var (ok, reason) = skeleton.TryStartGraft(src, node);
                if (ok)
                {
                    SetHighlight(null, HighlightMode.None);
                    Debug.Log($"[Graft] Attempt started: {src.id} → {node.id}");
                }
                else
                {
                    Debug.Log($"[Graft] Invalid target: {reason}");
                    // Keep source selected; player can try another target
                }
            }
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
            case HighlightMode.Pinch:       highlightMat.color = ColPinch;      break;
            case HighlightMode.Defoliate:   highlightMat.color = ColDefoliate;  break;
            case HighlightMode.GraftSource:  highlightMat.color = ColGraft;        break;
            case HighlightMode.GraftTarget:  highlightMat.color = ColGraftTgt;     break;
            case HighlightMode.PromoteHover: highlightMat.color = ColPromoteTarget; break;
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
