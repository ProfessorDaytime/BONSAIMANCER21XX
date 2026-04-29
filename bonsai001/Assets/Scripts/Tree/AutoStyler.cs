using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Automatically trims and wires the tree each season toward a target StyleDefinition.
///
/// Seasonal passes:
///   February  — branch tier management: schedules trims with a preview period.
///   Spring    — trunk + scaffold branch wiring; removes fully-set wires.
///   April–May — canopy pinching: contains terminals extending past the silhouette.
///   June      — ramification: proactively pinches interior terminals to build pad density.
///
/// GL overlay (when showTargetShape = true): cyan silhouette rings, orange tier bands,
/// yellow trunk waypoint arrows.
///
/// Attach to the same GameObject as TreeSkeleton.
/// </summary>
[RequireComponent(typeof(TreeSkeleton))]
public class AutoStyler : MonoBehaviour
{
    [Tooltip("Style to grow toward. Drag a StyleDefinition asset here.")]
    public StyleDefinition style;

    [Tooltip("When false, AutoStyler does nothing — handy for comparing styled vs. free growth.")]
    public bool autoStyleEnabled = true;

    [Tooltip("Log each action to the console for debugging.")]
    public bool verboseLog = false;

    [Tooltip("In-game days the orange preview sphere shows before a scheduled trim fires.")]
    public float trimPreviewDays = 10f;

    [Tooltip("Draw the target canopy silhouette and tier bands as GL lines in the Game/Scene view.")]
    public bool showTargetShape = true;

    TreeSkeleton skeleton;
    Material      glMat;

    // Nodes whose AutoStyler trunk wire is active (removed when set).
    readonly HashSet<int> autoWiredNodeIds  = new HashSet<int>();
    // Nodes whose AutoStyler scaffold wire is active (removed when set).
    readonly HashSet<int> shapedBranchIds   = new HashSet<int>();
    // Nodes whose AutoStyler wire fully set — never re-wire these.
    readonly HashSet<int> shapedNodeIds     = new HashSet<int>();

    // Scheduled trims: nodeId → in-game day when the trim fires.
    readonly Dictionary<int, float>      pendingTrims   = new Dictionary<int, float>();
    readonly Dictionary<int, GameObject> trimIndicators = new Dictionary<int, GameObject>();

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Awake()
    {
        skeleton = GetComponent<TreeSkeleton>();
        glMat = new Material(Shader.Find("Hidden/Internal-Colored"));
        glMat.hideFlags = HideFlags.HideAndDontSave;
    }

    void OnEnable()
    {
        skeleton.OnNewGrowingSeason += HandleNewGrowingSeason;
        GameManager.OnMonthChanged  += HandleMonthChanged;
    }

    void OnDisable()
    {
        skeleton.OnNewGrowingSeason -= HandleNewGrowingSeason;
        GameManager.OnMonthChanged  -= HandleMonthChanged;
        ClearAllIndicators();
    }

    void OnDestroy()
    {
        if (glMat != null) Destroy(glMat);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    void Update()
    {
        UpdatePendingTrims();
    }

    void UpdatePendingTrims()
    {
        if (pendingTrims.Count == 0) return;

        float now = InGameDay();
        var ids = new List<int>(pendingTrims.Keys);

        foreach (int id in ids)
        {
            TreeNode node = FindNodeById(id);

            if (node == null || node.isTrimmed || node.isDead)
            {
                DestroyIndicator(id);
                pendingTrims.Remove(id);
                continue;
            }

            if (now >= pendingTrims[id])
            {
                Log($"[AutoStyle] Pending trim fired node={id}");
                skeleton.TrimNode(node);
                DestroyIndicator(id);
                pendingTrims.Remove(id);
            }
        }
    }

    // ── Season Hooks ──────────────────────────────────────────────────────────

    void HandleNewGrowingSeason()
    {
        if (!IsReady()) return;
        Log($"[AutoStyle] Spring — ShapeTrunk + ShapeBranches | year={GameManager.year}");
        RemoveSetWires();
        ShapeTrunk();
        ShapeBranches();
    }

    void HandleMonthChanged(int month)
    {
        if (!IsReady()) return;
        if (month == 2)
        {
            Log($"[AutoStyle] February — ManageBranchTiers | year={GameManager.year}");
            ManageBranchTiers();
        }
        if (month == 4 || month == 5)
        {
            Log($"[AutoStyle] Month {month} — PinchOverextended | year={GameManager.year}");
            PinchOverextended();
        }
        if (month == 6 && style.enableRamification)
        {
            Log($"[AutoStyle] June — DevelopRamification | year={GameManager.year}");
            DevelopRamification();
        }
    }

    // ── Wire Cleanup ──────────────────────────────────────────────────────────

    void RemoveSetWires()
    {
        RemoveSetWiresFromSet(autoWiredNodeIds);
        RemoveSetWiresFromSet(shapedBranchIds);
    }

    void RemoveSetWiresFromSet(HashSet<int> set)
    {
        var toRemove = new List<int>();
        foreach (int id in set)
        {
            TreeNode node = FindNodeById(id);
            if (node == null) { toRemove.Add(id); continue; }

            if (!node.hasWire || node.wireSetProgress >= 1f)
            {
                if (node.hasWire && node.wireSetProgress >= 1f)
                {
                    skeleton.UnwireNode(node);
                    shapedNodeIds.Add(id);
                    Log($"[AutoStyle] Wire removed (set) node={id} — marked shaped");
                }
                toRemove.Add(id);
            }
        }
        foreach (int id in toRemove) set.Remove(id);
    }

    // ── Trunk Shaping ─────────────────────────────────────────────────────────

    void ShapeTrunk()
    {
        if (style.trunkWaypoints == null || style.trunkWaypoints.Length == 0) return;

        float soilY  = SoilWorldY();
        float treeH  = Mathf.Max(0.01f, skeleton.CachedTreeHeight);
        int wiredCount = 0, skippedWire = 0, skippedThreshold = 0;

        foreach (var node in skeleton.allNodes)
        {
            if (node.isRoot || node.isDead || node.isTrimmed) continue;
            if (node.depth != 0) continue;
            if (node.length <= 0.001f) continue;
            if (shapedNodeIds.Contains(node.id)) continue;
            if (node.hasWire) { skippedWire++; continue; }

            float nodeWorldY = skeleton.transform.TransformPoint(node.worldPosition).y;
            float heightNorm = (nodeWorldY - soilY) / treeH;
            if (heightNorm < 0f) continue;

            TrunkWaypoint wp             = NearestWaypoint(heightNorm);
            Vector3       targetLocalDir = WaypointDirection(wp);

            float deviation = Vector3.Angle(node.growDirection, targetLocalDir);
            if (deviation < style.wireThresholdDeg) { skippedThreshold++; continue; }

            skeleton.WireNode(node, targetLocalDir);
            Quaternion rot = Quaternion.FromToRotation(node.growDirection, targetLocalDir);
            node.growDirection = targetLocalDir;
            skeleton.RotateAndPropagateDescendants(node, rot, null);
            skeleton.meshBuilder?.SetDirty();

            autoWiredNodeIds.Add(node.id);
            wiredCount++;
            Log($"[AutoStyle] Wired trunk node={node.id} h={heightNorm:F2} dev={deviation:F1}°");
        }

        Log($"[AutoStyle] ShapeTrunk — wired={wiredCount} skipped(wire={skippedWire} threshold={skippedThreshold}) | year={GameManager.year}");
    }

    // ── Scaffold Branch Shaping ───────────────────────────────────────────────

    void ShapeBranches()
    {
        if (style.branchTiers == null || style.branchTiers.Length == 0) return;

        float soilY  = SoilWorldY();
        float treeH  = Mathf.Max(0.01f, skeleton.CachedTreeHeight);
        int wiredCount = 0;

        foreach (var node in skeleton.allNodes)
        {
            if (node.isRoot || node.isDead || node.isTrimmed) continue;
            if (node.depth != 1) continue; // primary scaffold branches only
            if (node.length <= 0.001f) continue;
            if (node.hasWire) continue;
            if (shapedNodeIds.Contains(node.id)) continue;

            float nodeWorldY = skeleton.transform.TransformPoint(node.worldPosition).y;
            float heightNorm = (nodeWorldY - soilY) / treeH;
            if (heightNorm < 0f) continue;

            BranchTier tier = TierForHeight(heightNorm);
            if (tier == null) continue;

            // Determine outward horizontal direction: azimuth of the branch from trunk center
            Vector3 worldGrowDir = skeleton.transform.TransformDirection(node.growDirection);
            Vector3 outward = new Vector3(worldGrowDir.x, 0f, worldGrowDir.z);
            if (outward.sqrMagnitude < 0.01f)
            {
                // Nearly vertical branch — use radial direction from trunk base instead
                Vector3 nodeWorld = skeleton.transform.TransformPoint(node.worldPosition);
                outward = new Vector3(nodeWorld.x - skeleton.transform.position.x, 0f,
                                      nodeWorld.z - skeleton.transform.position.z);
            }
            if (outward.sqrMagnitude < 0.001f) continue;
            outward.Normalize();

            float targetRad     = tier.targetAngleDeg * Mathf.Deg2Rad;
            Vector3 targetWorldDir = Mathf.Cos(targetRad) * Vector3.up + Mathf.Sin(targetRad) * outward;
            Vector3 targetLocalDir = skeleton.transform.InverseTransformDirection(targetWorldDir).normalized;

            float deviation = Vector3.Angle(node.growDirection, targetLocalDir);
            if (deviation < style.wireThresholdDeg) continue;

            skeleton.WireNode(node, targetLocalDir);
            Quaternion rot = Quaternion.FromToRotation(node.growDirection, targetLocalDir);
            node.growDirection = targetLocalDir;
            skeleton.RotateAndPropagateDescendants(node, rot, null);
            skeleton.meshBuilder?.SetDirty();

            shapedBranchIds.Add(node.id);
            wiredCount++;
            Log($"[AutoStyle] Wired branch node={node.id} h={heightNorm:F2} tier={tier.targetAngleDeg:F0}° dev={deviation:F1}°");
        }

        Log($"[AutoStyle] ShapeBranches — wired={wiredCount} | year={GameManager.year}");
    }

    // ── Branch Tier Management ─────────────────────────────────────────────────

    void ManageBranchTiers()
    {
        if (style.branchTiers == null || style.branchTiers.Length == 0) return;

        int totalBranchNodes = 0;
        foreach (var n in skeleton.allNodes) if (!n.isRoot) totalBranchNodes++;
        if (totalBranchNodes < 10) return;

        float soilY    = SoilWorldY();
        float treeH    = Mathf.Max(0.01f, skeleton.CachedTreeHeight);
        int schedCount = 0;

        foreach (var tier in style.branchTiers)
        {
            float minY = soilY + tier.minHeightNorm * treeH;
            float maxY = soilY + tier.maxHeightNorm * treeH;

            var candidates = new List<TreeNode>();
            foreach (var node in skeleton.allNodes)
            {
                if (node.isRoot || node.isDead || node.isTrimmed || node.parent == null) continue;
                if (node.parent.depth != 0) continue;
                if (node.parent.children.Count(c => !c.isRoot) <= 1) continue;

                float baseWorldY = skeleton.transform.TransformPoint(node.worldPosition).y;
                if (baseWorldY < minY || baseWorldY > maxY) continue;
                candidates.Add(node);
            }

            if (candidates.Count <= tier.maxBranches) continue;

            candidates.Sort((a, b) => BranchScore(a, tier).CompareTo(BranchScore(b, tier)));

            int toTrim = candidates.Count - tier.maxBranches;
            for (int i = 0; i < toTrim; i++)
            {
                int id = candidates[i].id;
                if (pendingTrims.ContainsKey(id)) continue;

                float fireDay = InGameDay() + trimPreviewDays;
                pendingTrims[id] = fireDay;
                SpawnTrimIndicator(candidates[i]);
                Log($"[AutoStyle] Tier trim scheduled node={id} score={BranchScore(candidates[i], tier):F2} fires in {trimPreviewDays} days");
                schedCount++;
            }
        }

        if (schedCount > 0)
            Log($"[AutoStyle] February tier trim — scheduled {schedCount} branches | year={GameManager.year}");
    }

    float BranchScore(TreeNode node, BranchTier tier)
    {
        float verticalAngle = Vector3.Angle(node.growDirection, Vector3.up);
        float angleDev      = Mathf.Abs(verticalAngle - tier.targetAngleDeg) / 90f;
        float anglePenalty  = angleDev > (tier.maxAngleTolerance / 90f) ? 2f : 0f;
        return node.radius * 10f + node.branchVigor - anglePenalty;
    }

    // ── Canopy Pinching ───────────────────────────────────────────────────────

    void PinchOverextended()
    {
        float soilY    = SoilWorldY();
        float treeH    = Mathf.Max(0.01f, skeleton.CachedTreeHeight);
        int pinchCount = 0;

        Vector3 trunkXZ = new Vector3(skeleton.transform.position.x, 0f, skeleton.transform.position.z);

        foreach (var node in skeleton.allNodes)
        {
            if (!node.isTerminal || node.isRoot || node.isDead || node.isTrimmed) continue;
            if (!node.isGrowing) continue;

            Vector3 tipWorld   = skeleton.transform.TransformPoint(node.tipPosition);
            float   heightNorm = Mathf.Clamp01((tipWorld.y - soilY) / treeH);

            float silhouetteR = style.canopySilhouette.Evaluate(heightNorm) * style.maxCanopyRadius * treeH;
            if (silhouetteR <= 0f) continue;

            float horizDist = Vector3.Distance(new Vector3(tipWorld.x, 0f, tipWorld.z), trunkXZ);
            if (horizDist > silhouetteR * style.pinchOvershootFactor)
            {
                skeleton.PinchNode(node);
                pinchCount++;
                Log($"[AutoStyle] Pinched node={node.id} h={heightNorm:F2} dist={horizDist:F2} limit={silhouetteR:F2}");
            }
        }

        if (pinchCount > 0)
            Log($"[AutoStyle] Canopy pinch — {pinchCount} tips | month={GameManager.month} year={GameManager.year}");
    }

    // ── Ramification ──────────────────────────────────────────────────────────

    void DevelopRamification()
    {
        float soilY    = SoilWorldY();
        float treeH    = Mathf.Max(0.01f, skeleton.CachedTreeHeight);
        int pinchCount = 0;

        Vector3 trunkXZ = new Vector3(skeleton.transform.position.x, 0f, skeleton.transform.position.z);

        foreach (var node in skeleton.allNodes)
        {
            if (!node.isTerminal || node.isRoot || node.isDead || node.isTrimmed) continue;
            if (!node.isGrowing) continue;

            Vector3 tipWorld   = skeleton.transform.TransformPoint(node.tipPosition);
            float   heightNorm = Mathf.Clamp01((tipWorld.y - soilY) / treeH);

            // Only ramify within the body of the tree — skip base and apex
            if (heightNorm < 0.05f || heightNorm > style.ramificationMaxHeight) continue;

            // Skip terminals outside the silhouette — PinchOverextended handles those
            float silhouetteR = style.canopySilhouette.Evaluate(heightNorm) * style.maxCanopyRadius * treeH;
            if (silhouetteR <= 0f) continue;
            float horizDist = Vector3.Distance(new Vector3(tipWorld.x, 0f, tipWorld.z), trunkXZ);
            if (horizDist >= silhouetteR * style.pinchOvershootFactor) continue;

            // Target refinement decreases toward the apex (lower pads are denser)
            float targetRef = style.ramificationTargetLevel * (1f - heightNorm * 0.4f);
            if (node.refinementLevel >= targetRef) continue;

            // Only pinch terminals that have grown to at least 85% of their target length
            if (node.targetLength > 0f && node.length < node.targetLength * 0.85f) continue;

            skeleton.PinchNode(node);
            pinchCount++;
            Log($"[AutoStyle] Ramification node={node.id} h={heightNorm:F2} ref={node.refinementLevel:F1}/{targetRef:F1}");
        }

        if (pinchCount > 0)
            Log($"[AutoStyle] Ramification — {pinchCount} tips | year={GameManager.year}");
    }

    // ── GL Visualization ──────────────────────────────────────────────────────

    void OnRenderObject()
    {
        if (!showTargetShape || style == null || skeleton == null || glMat == null) return;
        if (skeleton.root == null) return;

        float soilY = SoilWorldY();
        float treeH = Mathf.Max(0.01f, skeleton.CachedTreeHeight);
        Vector3 treeBase = new Vector3(skeleton.transform.position.x, soilY, skeleton.transform.position.z);

        glMat.SetPass(0);
        GL.PushMatrix();
        GL.MultMatrix(Matrix4x4.identity);

        // -- Canopy silhouette rings (cyan) at 20 height steps
        GL.Begin(GL.LINES);
        GL.Color(new Color(0f, 0.9f, 0.9f, 0.6f));
        for (int i = 1; i <= 20; i++)
        {
            float h = (float)i / 20f;
            float r = style.canopySilhouette.Evaluate(h) * style.maxCanopyRadius * treeH;
            if (r < 0.01f) continue;
            DrawCircleGL(new Vector3(treeBase.x, soilY + h * treeH, treeBase.z), r, 24);
        }
        GL.End();

        // -- Tier boundary rings (orange)
        if (style.branchTiers != null && style.branchTiers.Length > 0)
        {
            GL.Begin(GL.LINES);
            GL.Color(new Color(1f, 0.5f, 0f, 0.5f));
            foreach (var tier in style.branchTiers)
            {
                float rMin = Mathf.Max(0.05f, style.canopySilhouette.Evaluate(tier.minHeightNorm) * style.maxCanopyRadius * treeH * 1.1f);
                float rMax = Mathf.Max(0.05f, style.canopySilhouette.Evaluate(tier.maxHeightNorm) * style.maxCanopyRadius * treeH * 1.1f);
                DrawCircleGL(new Vector3(treeBase.x, soilY + tier.minHeightNorm * treeH, treeBase.z), rMin, 16);
                DrawCircleGL(new Vector3(treeBase.x, soilY + tier.maxHeightNorm * treeH, treeBase.z), rMax, 16);
            }
            GL.End();
        }

        // -- Trunk waypoint markers + lean arrows (yellow)
        if (style.trunkWaypoints != null && style.trunkWaypoints.Length > 0)
        {
            GL.Begin(GL.LINES);
            GL.Color(new Color(1f, 0.95f, 0f, 0.85f));
            foreach (var wp in style.trunkWaypoints)
            {
                float   worldY    = soilY + wp.heightAboveSoil * treeH;
                Vector3 markerPos = new Vector3(treeBase.x, worldY, treeBase.z);
                float   crossR    = 0.05f;

                // Small crosshair at waypoint height
                GL.Vertex(markerPos + Vector3.right   * crossR);   GL.Vertex(markerPos - Vector3.right   * crossR);
                GL.Vertex(markerPos + Vector3.forward * crossR);   GL.Vertex(markerPos - Vector3.forward * crossR);

                // Lean direction arrow
                if (wp.targetLeanAngleDeg > 0.5f)
                {
                    Vector3 localLean  = WaypointDirection(wp);
                    Vector3 worldLean  = skeleton.transform.TransformDirection(localLean);
                    float   arrowLen   = Mathf.Max(0.08f, treeH * 0.12f);
                    GL.Vertex(markerPos);
                    GL.Vertex(markerPos + worldLean * arrowLen);
                }
            }
            GL.End();
        }

        GL.PopMatrix();
    }

    // Must be called inside GL.Begin(GL.LINES) / GL.End()
    void DrawCircleGL(Vector3 center, float radius, int segments)
    {
        float step = 2f * Mathf.PI / segments;
        for (int i = 0; i < segments; i++)
        {
            float a0 = i * step, a1 = (i + 1) * step;
            GL.Vertex(center + new Vector3(Mathf.Cos(a0) * radius, 0f, Mathf.Sin(a0) * radius));
            GL.Vertex(center + new Vector3(Mathf.Cos(a1) * radius, 0f, Mathf.Sin(a1) * radius));
        }
    }

    // ── Indicator Helpers ─────────────────────────────────────────────────────

    void SpawnTrimIndicator(TreeNode node)
    {
        if (trimIndicators.ContainsKey(node.id)) return;

        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = $"TrimPreview_{node.id}";
        go.transform.SetParent(skeleton.transform, worldPositionStays: true);

        Vector3 mid = node.worldPosition + node.growDirection * (node.length * 0.5f);
        go.transform.position = skeleton.transform.TransformPoint(mid);
        go.transform.localScale = Vector3.one * Mathf.Clamp(node.radius * 6f, 0.04f, 0.15f);

        Object.Destroy(go.GetComponent<Collider>());

        var mr  = go.GetComponent<MeshRenderer>();
        var mat = mr.material;
        mat.color = new Color(1f, 0.35f, 0f);
        mr.material = mat;

        trimIndicators[node.id] = go;
    }

    void DestroyIndicator(int nodeId)
    {
        if (trimIndicators.TryGetValue(nodeId, out var go))
        {
            if (go != null) Object.Destroy(go);
            trimIndicators.Remove(nodeId);
        }
    }

    void ClearAllIndicators()
    {
        foreach (var kv in trimIndicators)
            if (kv.Value != null) Object.Destroy(kv.Value);
        trimIndicators.Clear();
        pendingTrims.Clear();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    bool IsReady() =>
        autoStyleEnabled && style != null && skeleton != null && skeleton.root != null &&
        GameManager.Instance != null && GameManager.Instance.state != GameState.TreeDead;

    float SoilWorldY()
    {
        float y = skeleton.plantingSurfacePoint.y;
        return y != 0f ? y : skeleton.transform.position.y;
    }

    TrunkWaypoint NearestWaypoint(float heightNorm)
    {
        var wps  = style.trunkWaypoints;
        var best = wps[0];
        float bestDist = float.MaxValue;
        foreach (var wp in wps)
        {
            float d = Mathf.Abs(wp.heightAboveSoil - heightNorm);
            if (d < bestDist) { bestDist = d; best = wp; }
        }
        return best;
    }

    BranchTier TierForHeight(float heightNorm)
    {
        if (style.branchTiers == null) return null;
        foreach (var tier in style.branchTiers)
            if (heightNorm >= tier.minHeightNorm && heightNorm <= tier.maxHeightNorm)
                return tier;
        return null;
    }

    Vector3 WaypointDirection(TrunkWaypoint wp)
    {
        if (wp.targetLeanAngleDeg < 0.5f) return Vector3.up;
        Vector3 leanDir  = Quaternion.Euler(0f, wp.leanAxisDeg, 0f) * Vector3.forward;
        Vector3 tiltAxis = Vector3.Cross(Vector3.up, leanDir).normalized;
        return Quaternion.AngleAxis(wp.targetLeanAngleDeg, tiltAxis) * Vector3.up;
    }

    TreeNode FindNodeById(int id)
    {
        foreach (var node in skeleton.allNodes)
            if (node.id == id) return node;
        return null;
    }

    static float InGameDay() => GameManager.dayOfYear + GameManager.year * 366f;

    void Log(string msg) { if (verboseLog) Debug.Log(msg); }
}
