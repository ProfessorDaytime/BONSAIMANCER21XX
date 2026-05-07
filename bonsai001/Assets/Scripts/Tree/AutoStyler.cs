using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Structural, plan-based bonsai styler.
///
/// Each BranchTier defines N branch SLOTS at evenly-spaced azimuths starting at
/// azimuthOffsetDeg. Each spring AutoStyler matches living scaffold branches (depth=1)
/// to the nearest open slot; unmatched branches are scheduled for removal; empty slots
/// stimulate back-budding on the nearest trunk node in February.
///
/// Per-slot state machine:
///   Empty → Growing → Training → Established → Maintaining
///
/// Seasonal schedule:
///   February  — stimulate empty slots
///   Spring    — refresh slot matching; trunk wiring; remove set wires
///   October   — schedule scaffold branch wires for Growing/Training slots
///   Apr–May   — schedule overextended-tip pinches (silhouette containment)
///   June      — schedule ramification pinches (interior pad density)
///
/// GL overlay (showTargetShape):
///   Cyan rings        — canopy silhouette
///   Orange rings      — tier boundary bands
///   Yellow cross+arrow— trunk waypoints with lean direction
///   Colored diamond   — branch slots, color = state
///   Orange X          — scheduled trim     (GL, appears actionPreviewDays before firing)
///   Cyan circle       — scheduled wire     (GL)
///   Green spike       — scheduled pinch    (GL)
/// </summary>
[RequireComponent(typeof(TreeSkeleton))]
public class AutoStyler : MonoBehaviour
{
    public static AutoStyler Instance { get; private set; }

    [Tooltip("Style to grow toward.")]
    public StyleDefinition style;
    [Tooltip("When false, AutoStyler does nothing.")]
    public bool autoStyleEnabled = true;
    [Tooltip("Log each action to the console.")]
    public bool verboseLog = false;
    [Tooltip("In-game days an indicator is visible before its action fires.")]
    public float actionPreviewDays = 20f;
    [Tooltip("Draw target shape and slot plan as GL lines in Game/Scene view.")]
    public bool showTargetShape = true;

    TreeSkeleton skeleton;
    Material      glMat;

    List<BranchSlot> slots = new List<BranchSlot>();

    readonly HashSet<int> shapedNodeIds      = new HashSet<int>();
    readonly HashSet<int> autoWiredTrunkIds  = new HashSet<int>();
    readonly HashSet<int> autoWiredBranchIds = new HashSet<int>();

    // Scheduled actions: nodeId → fire day
    readonly Dictionary<int, float>   pendingTrims   = new Dictionary<int, float>();
    readonly Dictionary<int, float>   pendingWires   = new Dictionary<int, float>();
    readonly Dictionary<int, Vector3> pendingWireDir = new Dictionary<int, Vector3>();
    readonly Dictionary<int, float>   pendingPinches = new Dictionary<int, float>();

    // nodeId → in-game day when wireSetProgress first reached 1.0 (gold)
    readonly Dictionary<int, float>   wireGoldDay    = new Dictionary<int, float>();

    [Tooltip("In-game days after a wire turns gold before AutoStyler removes it.")]
    public float unwireDelayDays = 20f;

    // ── Public accessors for UI ───────────────────────────────────────────────

    public IReadOnlyList<BranchSlot> Slots => slots;
    public int PendingTrimCount  => pendingTrims.Count;
    public int PendingWireCount  => pendingWires.Count;
    public int PendingPinchCount => pendingPinches.Count;
    public int ShapedCount       => shapedNodeIds.Count;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Awake()
    {
        Instance = this;
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
        pendingTrims.Clear(); pendingWires.Clear(); pendingWireDir.Clear(); pendingPinches.Clear();
        wireGoldDay.Clear();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (glMat != null) Destroy(glMat);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    void Update() { UpdatePendingActions(); RemoveSetWires(); }

    void UpdatePendingActions()
    {
        float now = InGameDay();

        if (pendingTrims.Count > 0)
        {
            foreach (int id in new List<int>(pendingTrims.Keys))
            {
                var n = FindNodeById(id);
                if (n == null || n.isTrimmed || n.isDead) { pendingTrims.Remove(id); continue; }
                if (now >= pendingTrims[id]) { Log($"[AutoStyle] Trim fired node={id}"); skeleton.TrimNode(n); pendingTrims.Remove(id); }
            }
        }

        if (pendingWires.Count > 0)
        {
            foreach (int id in new List<int>(pendingWires.Keys))
            {
                var n = FindNodeById(id);
                if (n == null || n.isDead || n.isTrimmed || n.hasWire)
                { pendingWires.Remove(id); pendingWireDir.Remove(id); continue; }
                if (now >= pendingWires[id])
                {
                    Log($"[AutoStyle] Wire fired node={id}");
                    ApplyWire(n, pendingWireDir[id], autoWiredBranchIds);
                    pendingWires.Remove(id); pendingWireDir.Remove(id);
                }
            }
        }

        if (pendingPinches.Count > 0)
        {
            foreach (int id in new List<int>(pendingPinches.Keys))
            {
                var n = FindNodeById(id);
                if (n == null || n.isDead || n.isTrimmed || !n.isTerminal) { pendingPinches.Remove(id); continue; }
                if (now >= pendingPinches[id]) { Log($"[AutoStyle] Pinch fired node={id}"); skeleton.PinchNode(n); pendingPinches.Remove(id); }
            }
        }
    }

    // ── Season Hooks ──────────────────────────────────────────────────────────

    void HandleNewGrowingSeason()
    {
        if (!IsReady()) return;
        Log($"[AutoStyle] Spring — RefreshSlots + ShapeTrunk | year={GameManager.year}");
        RefreshSlots();
        ShapeTrunk();
    }

    void HandleMonthChanged(int month)
    {
        if (!IsReady()) return;
        switch (month)
        {
            case 2:  StimulateEmptySlots(); break;
            case 4:
            case 5:  PlanPinches(overextendedOnly: true); break;
            case 6:  if (style.enableRamification) PlanPinches(overextendedOnly: false); break;
            case 10: PlanScaffoldWires(); break;
        }
    }

    // ── Wire Cleanup ──────────────────────────────────────────────────────────

    void RemoveSetWires()
    {
        // Gold-day tracking: note the day each auto-wire first turns gold
        TrackWireGoldDays(autoWiredTrunkIds);
        TrackWireGoldDays(autoWiredBranchIds);

        // Unwire any node whose gold timer has expired
        float now = InGameDay();
        UnwireGoldExpired(autoWiredTrunkIds,  now);
        UnwireGoldExpired(autoWiredBranchIds, now);
    }

    void TrackWireGoldDays(HashSet<int> set)
    {
        foreach (int id in set)
        {
            if (wireGoldDay.ContainsKey(id)) continue;
            var n = FindNodeById(id);
            if (n != null && n.hasWire && n.wireSetProgress >= 1f)
                wireGoldDay[id] = InGameDay();
        }
    }

    void UnwireGoldExpired(HashSet<int> set, float now)
    {
        var toRemove = new List<int>();
        foreach (int id in set)
        {
            var n = FindNodeById(id);
            if (n == null) { toRemove.Add(id); wireGoldDay.Remove(id); continue; }
            if (!n.hasWire) { toRemove.Add(id); wireGoldDay.Remove(id); continue; }

            // Wire is gold and delay has elapsed → remove it
            if (wireGoldDay.TryGetValue(id, out float goldDay) && now >= goldDay + unwireDelayDays)
            {
                skeleton.UnwireNode(n);
                shapedNodeIds.Add(id);
                wireGoldDay.Remove(id);
                toRemove.Add(id);
                Log($"[AutoStyle] Unwire (gold+{unwireDelayDays}d) node={id}");
            }
        }
        foreach (int id in toRemove) set.Remove(id);
    }

    // ── Slot Management ───────────────────────────────────────────────────────

    void RefreshSlots()
    {
        if (style.branchTiers == null || style.branchTiers.Length == 0) return;

        float soilY = SoilWorldY();
        float treeH = Mathf.Max(0.01f, skeleton.CachedTreeHeight);

        slots.Clear();
        for (int ti = 0; ti < style.branchTiers.Length; ti++)
        {
            var tier = style.branchTiers[ti]; int count = Mathf.Max(1, tier.maxBranches);
            float step = 360f / count;
            for (int i = 0; i < count; i++)
                slots.Add(new BranchSlot(ti, (tier.azimuthOffsetDeg + i * step) % 360f));
        }

        var available = new List<TreeNode>();
        foreach (var n in skeleton.allNodes)
        {
            if (n.depth != 1 || n.isRoot || n.isDead || n.isTrimmed || n.length <= 0.001f) continue;
            available.Add(n);
        }

        // Prefer already-shaped nodes when greedy-matching (stability)
        available.Sort((a, b) => (shapedNodeIds.Contains(a.id) ? 0 : 1).CompareTo(shapedNodeIds.Contains(b.id) ? 0 : 1));

        // Wide tolerance: match any branch in the right height band to its closest slot.
        // The branch will be wired toward the slot's target azimuth — don't trim it just
        // because it grew in the wrong direction. 150° covers everything except the opposite side.
        foreach (var slot in slots)
        {
            var tier = style.branchTiers[slot.tierIndex]; float bestDiff = 150f; TreeNode best = null;
            foreach (var n in available)
            {
                float nodeY = (skeleton.transform.TransformPoint(n.worldPosition).y - soilY) / treeH;
                if (nodeY < tier.minHeightNorm - 0.05f || nodeY > tier.maxHeightNorm + 0.05f) continue;
                float diff = Mathf.Abs(Mathf.DeltaAngle(GetBranchAzimuth(n), slot.azimuthDeg));
                if (diff < bestDiff) { bestDiff = diff; best = n; }
            }
            if (best != null)
            { slot.assignedNodeId = best.id; slot.state = ComputeSlotState(slot, best); available.Remove(best); }
        }

        // Only trim a branch if its tier already has enough matched branches (the tier is truly full).
        // Count depth=1 scaffold branches only — not trunk segments.
        int depth1Count = 0;
        foreach (var x in skeleton.allNodes)
            if (x.depth == 1 && !x.isRoot && !x.isDead && !x.isTrimmed && x.length > 0.001f) depth1Count++;

        if (depth1Count > slots.Count)
        {
            // Per-tier slot capacity: count how many slots each tier has and how many are filled.
            var tierCapacity  = new int[style.branchTiers.Length];
            var tierOccupied  = new int[style.branchTiers.Length];
            for (int ti = 0; ti < style.branchTiers.Length; ti++)
                tierCapacity[ti] = style.branchTiers[ti].maxBranches;
            foreach (var slot in slots)
                if (slot.assignedNodeId >= 0) tierOccupied[slot.tierIndex]++;

            foreach (var n in available)
            {
                float nodeY = (skeleton.transform.TransformPoint(n.worldPosition).y - soilY) / treeH;
                int tierIdx = -1;
                for (int ti = 0; ti < style.branchTiers.Length; ti++)
                {
                    var t = style.branchTiers[ti];
                    if (nodeY >= t.minHeightNorm - 0.05f && nodeY <= t.maxHeightNorm + 0.05f)
                    { tierIdx = ti; break; }
                }
                // Only trim if its tier is full (no open slots left for it)
                if (tierIdx >= 0 && tierOccupied[tierIdx] < tierCapacity[tierIdx]) continue;
                if (pendingTrims.ContainsKey(n.id)) continue;
                pendingTrims[n.id] = InGameDay() + actionPreviewDays;
                Log($"[AutoStyle] Excess branch queued node={n.id} tier={tierIdx}");
            }
        }

        int occ = slots.Count(s => s.assignedNodeId >= 0);
        Log($"[AutoStyle] Slots {occ}/{slots.Count} — " +
            string.Join(" ", slots.GroupBy(s => s.state).Select(g => $"{g.Key}:{g.Count()}")) +
            $" | year={GameManager.year}");
    }

    SlotState ComputeSlotState(BranchSlot slot, TreeNode node)
    {
        if (node.hasWire && node.wireSetProgress < 1f) return SlotState.Training;
        if (shapedNodeIds.Contains(node.id))
            return node.children.Count > 0 ? SlotState.Maintaining : SlotState.Established;
        var tier = style.branchTiers[slot.tierIndex];
        return Vector3.Angle(node.growDirection, SlotTargetDirection(slot, tier)) >= style.wireThresholdDeg
            ? SlotState.Growing : SlotState.Established;
    }

    // ── Trunk Shaping ─────────────────────────────────────────────────────────

    void ShapeTrunk()
    {
        if (style.trunkWaypoints == null || style.trunkWaypoints.Length == 0) return;
        float soilY = SoilWorldY(); float treeH = Mathf.Max(0.01f, skeleton.CachedTreeHeight); int wiredCount = 0;
        foreach (var n in skeleton.allNodes)
        {
            if (n.isRoot || n.isDead || n.isTrimmed || n.depth != 0 || n.length <= 0.001f) continue;
            if (shapedNodeIds.Contains(n.id) || n.hasWire) continue;
            float heightNorm = (skeleton.transform.TransformPoint(n.worldPosition).y - soilY) / treeH;
            if (heightNorm < 0f) continue;
            Vector3 targetDir = WaypointDirection(NearestWaypoint(heightNorm));
            if (Vector3.Angle(n.growDirection, targetDir) < style.wireThresholdDeg) continue;
            ApplyWire(n, targetDir, autoWiredTrunkIds); wiredCount++;
            Log($"[AutoStyle] Trunk wire node={n.id} h={heightNorm:F2}");
        }
        if (wiredCount > 0) Log($"[AutoStyle] ShapeTrunk — wired={wiredCount} | year={GameManager.year}");
    }

    // ── Scaffold Branch Wires (October) ───────────────────────────────────────

    void PlanScaffoldWires()
    {
        int scheduled = 0;
        foreach (var slot in slots)
        {
            if (slot.state != SlotState.Growing && slot.state != SlotState.Training) continue;
            if (slot.assignedNodeId < 0) continue;
            var n = FindNodeById(slot.assignedNodeId);
            if (n == null || n.hasWire || n.isDead || n.isTrimmed || shapedNodeIds.Contains(n.id)) continue;
            if (n.length <= 0.001f || pendingWires.ContainsKey(n.id)) continue;
            var tier = style.branchTiers[slot.tierIndex]; Vector3 targetDir = SlotTargetDirection(slot, tier);
            if (Vector3.Angle(n.growDirection, targetDir) < style.wireThresholdDeg) continue;
            pendingWires[n.id] = InGameDay() + actionPreviewDays; pendingWireDir[n.id] = targetDir;
            scheduled++; Log($"[AutoStyle] Wire scheduled node={n.id} az={slot.azimuthDeg:F0}°");
        }
        if (scheduled > 0) Log($"[AutoStyle] October — {scheduled} wires | year={GameManager.year}");
    }

    // ── Empty Slot Stimulation (February) ────────────────────────────────────

    void StimulateEmptySlots()
    {
        float soilY = SoilWorldY(); float treeH = Mathf.Max(0.01f, skeleton.CachedTreeHeight); int count = 0;
        foreach (var slot in slots)
        {
            if (slot.assignedNodeId >= 0) continue;
            var tier = style.branchTiers[slot.tierIndex];
            float targetY = soilY + (tier.minHeightNorm + tier.maxHeightNorm) * 0.5f * treeH;
            TreeNode best = null; float bestDist = float.MaxValue;
            foreach (var n in skeleton.allNodes)
            {
                if (n.depth != 0 || n.isRoot || n.isDead || n.isTrimmed) continue;
                float d = Mathf.Abs(skeleton.transform.TransformPoint(n.worldPosition).y - targetY);
                if (d < bestDist) { bestDist = d; best = n; }
            }
            if (best != null) { best.backBudStimulated = true; count++; }
        }
        if (count > 0) Log($"[AutoStyle] February — stimulated {count} empty slots | year={GameManager.year}");
    }

    // ── Pinch Planning ────────────────────────────────────────────────────────

    void PlanPinches(bool overextendedOnly)
    {
        float soilY = SoilWorldY(); float treeH = Mathf.Max(0.01f, skeleton.CachedTreeHeight); int queued = 0;
        Vector3 trunkXZ = new Vector3(skeleton.transform.position.x, 0f, skeleton.transform.position.z);
        foreach (var n in skeleton.allNodes)
        {
            if (!n.isTerminal || n.isRoot || n.isDead || n.isTrimmed || !n.isGrowing) continue;
            if (pendingPinches.ContainsKey(n.id)) continue;
            Vector3 tipWorld = skeleton.transform.TransformPoint(n.tipPosition);
            float h = Mathf.Clamp01((tipWorld.y - soilY) / treeH);
            float silR = style.canopySilhouette.Evaluate(h) * style.maxCanopyRadius * treeH;
            if (silR <= 0f) continue;
            float horizDist = Vector3.Distance(new Vector3(tipWorld.x, 0f, tipWorld.z), trunkXZ);
            bool outside = horizDist > silR * style.pinchOvershootFactor;
            if (overextendedOnly)
            {
                if (!outside) continue;
            }
            else
            {
                if (outside) continue;
                if (h < 0.05f || h > style.ramificationMaxHeight) continue;
                float targetRef = style.ramificationTargetLevel * (1f - h * 0.4f);
                if (n.refinementLevel >= targetRef) continue;
                if (n.targetLength > 0f && n.length < n.targetLength * 0.85f) continue;
            }
            pendingPinches[n.id] = InGameDay() + actionPreviewDays * 0.5f;
            queued++;
        }
        if (queued > 0) Log($"[AutoStyle] Pinch ({(overextendedOnly ? "silhouette" : "ramification")}) — {queued} | month={GameManager.month}");
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

        // Canopy silhouette (cyan)
        GL.Begin(GL.LINES); GL.Color(new Color(0f, 0.9f, 0.9f, 0.55f));
        for (int i = 1; i <= 20; i++)
        {
            float h = i / 20f; float r = style.canopySilhouette.Evaluate(h) * style.maxCanopyRadius * treeH;
            if (r > 0.01f) DrawCircleGL(new Vector3(treeBase.x, soilY + h * treeH, treeBase.z), r, 24);
        }
        GL.End();

        // Tier rings (orange)
        if (style.branchTiers != null)
        {
            GL.Begin(GL.LINES); GL.Color(new Color(1f, 0.5f, 0f, 0.45f));
            foreach (var tier in style.branchTiers)
            {
                float rMin = Mathf.Max(0.05f, style.canopySilhouette.Evaluate(tier.minHeightNorm) * style.maxCanopyRadius * treeH * 1.1f);
                float rMax = Mathf.Max(0.05f, style.canopySilhouette.Evaluate(tier.maxHeightNorm) * style.maxCanopyRadius * treeH * 1.1f);
                DrawCircleGL(new Vector3(treeBase.x, soilY + tier.minHeightNorm * treeH, treeBase.z), rMin, 16);
                DrawCircleGL(new Vector3(treeBase.x, soilY + tier.maxHeightNorm * treeH, treeBase.z), rMax, 16);
            }
            GL.End();
        }

        // Trunk waypoints (yellow)
        if (style.trunkWaypoints != null)
        {
            GL.Begin(GL.LINES); GL.Color(new Color(1f, 0.95f, 0f, 0.85f));
            foreach (var wp in style.trunkWaypoints)
            {
                Vector3 pos = new Vector3(treeBase.x, soilY + wp.heightAboveSoil * treeH, treeBase.z); float cx = 0.05f;
                GL.Vertex(pos + Vector3.right * cx);   GL.Vertex(pos - Vector3.right * cx);
                GL.Vertex(pos + Vector3.forward * cx); GL.Vertex(pos - Vector3.forward * cx);
                if (wp.targetLeanAngleDeg > 0.5f)
                { Vector3 lean = skeleton.transform.TransformDirection(WaypointDirection(wp)); GL.Vertex(pos); GL.Vertex(pos + lean * Mathf.Max(0.08f, treeH * 0.12f)); }
            }
            GL.End();
        }

        // Slot diamonds + spokes (color = state)
        if (slots.Count > 0)
        {
            GL.Begin(GL.LINES);
            foreach (var slot in slots)
            {
                var tier = style.branchTiers[slot.tierIndex];
                float h = (tier.minHeightNorm + tier.maxHeightNorm) * 0.5f;
                float r = Mathf.Max(style.canopySilhouette.Evaluate(h) * style.maxCanopyRadius * treeH * 0.85f, 0.06f);
                float az = slot.azimuthDeg * Mathf.Deg2Rad;
                Vector3 pos = new Vector3(treeBase.x + Mathf.Sin(az) * r, soilY + h * treeH, treeBase.z + Mathf.Cos(az) * r);
                Vector3 hub = new Vector3(treeBase.x, pos.y, treeBase.z); float m = 0.035f;
                GL.Color(SlotColor(slot.state));
                GL.Vertex(pos + new Vector3(m, 0, 0));  GL.Vertex(pos + new Vector3(0, 0, m));
                GL.Vertex(pos + new Vector3(0, 0, m));  GL.Vertex(pos + new Vector3(-m, 0, 0));
                GL.Vertex(pos + new Vector3(-m, 0, 0)); GL.Vertex(pos + new Vector3(0, 0, -m));
                GL.Vertex(pos + new Vector3(0, 0, -m)); GL.Vertex(pos + new Vector3(m, 0, 0));
                GL.Vertex(pos - new Vector3(0, m, 0));  GL.Vertex(pos + new Vector3(0, m, 0));
                GL.Vertex(hub); GL.Vertex(pos);
            }
            GL.End();
        }

        // ── Action indicators (intent-based, always visible) ──────────────────

        // Build set of slot-assigned node IDs so we know which branches are "kept"
        var assignedIds = new HashSet<int>();
        foreach (var slot in slots) if (slot.assignedNodeId >= 0) assignedIds.Add(slot.assignedNodeId);

        // Trim candidates — orange X on every unmatched depth=1 branch (will be removed next spring)
        GL.Begin(GL.LINES); GL.Color(new Color(1f, 0.35f, 0f, 0.95f));
        foreach (var n in skeleton.allNodes)
        {
            if (n.depth != 1 || n.isRoot || n.isDead || n.isTrimmed || n.length <= 0.001f) continue;
            if (assignedIds.Contains(n.id)) continue;
            Vector3 mid = skeleton.transform.TransformPoint(n.worldPosition + n.growDirection * (n.length * 0.5f));
            float s = Mathf.Clamp(n.radius * 14f, 0.06f, 0.22f);
            // 3-plane X cross
            GL.Vertex(mid + new Vector3(-s, -s,  0)); GL.Vertex(mid + new Vector3( s,  s,  0));
            GL.Vertex(mid + new Vector3(-s,  s,  0)); GL.Vertex(mid + new Vector3( s, -s,  0));
            GL.Vertex(mid + new Vector3( 0, -s, -s)); GL.Vertex(mid + new Vector3( 0,  s,  s));
            GL.Vertex(mid + new Vector3( 0,  s, -s)); GL.Vertex(mid + new Vector3( 0, -s,  s));
            GL.Vertex(mid + new Vector3(-s,  0, -s)); GL.Vertex(mid + new Vector3( s,  0,  s));
            GL.Vertex(mid + new Vector3(-s,  0,  s)); GL.Vertex(mid + new Vector3( s,  0, -s));
        }
        GL.End();

        // Wire candidates — cyan circle on Growing/Training assigned branches
        GL.Begin(GL.LINES); GL.Color(new Color(0f, 0.7f, 1f, 0.95f));
        foreach (var slot in slots)
        {
            if (slot.state != SlotState.Growing && slot.state != SlotState.Training) continue;
            if (slot.assignedNodeId < 0) continue;
            var n = FindNodeById(slot.assignedNodeId);
            if (n == null || n.isDead || n.isTrimmed || n.hasWire || n.length <= 0.001f) continue;
            Vector3 mid = skeleton.transform.TransformPoint(n.worldPosition + n.growDirection * (n.length * 0.5f));
            float r = Mathf.Clamp(n.radius * 10f, 0.06f, 0.20f);
            DrawCircleGL(mid, r, 16);
            // cross-hair to make it obvious
            GL.Vertex(mid + Vector3.right * r); GL.Vertex(mid - Vector3.right * r);
            GL.Vertex(mid + Vector3.up    * r); GL.Vertex(mid - Vector3.up    * r);
        }
        GL.End();

        // Pending pinches — green spike at tip (seasonal; shown when queued)
        if (pendingPinches.Count > 0)
        {
            GL.Begin(GL.LINES); GL.Color(new Color(0.15f, 0.95f, 0.15f, 0.95f));
            foreach (var kv in pendingPinches)
            {
                var n = FindNodeById(kv.Key); if (n == null) continue;
                Vector3 tip = skeleton.transform.TransformPoint(n.tipPosition);
                float s = Mathf.Clamp(n.radius * 11f, 0.05f, 0.14f);
                GL.Vertex(tip + new Vector3( s, 0,  0)); GL.Vertex(tip + new Vector3( 0, 0,  s));
                GL.Vertex(tip + new Vector3( 0, 0,  s)); GL.Vertex(tip + new Vector3(-s, 0,  0));
                GL.Vertex(tip + new Vector3(-s, 0,  0)); GL.Vertex(tip + new Vector3( 0, 0, -s));
                GL.Vertex(tip + new Vector3( 0, 0, -s)); GL.Vertex(tip + new Vector3( s, 0,  0));
                GL.Vertex(tip - new Vector3(0, s * 2.5f, 0)); GL.Vertex(tip + new Vector3(0, s * 2.5f, 0));
            }
            GL.End();
        }

        GL.PopMatrix();
    }

    static Color SlotColor(SlotState s)
    {
        switch (s)
        {
            case SlotState.Empty:       return new Color(1f,   0.2f, 0.2f, 0.9f);
            case SlotState.Growing:     return new Color(1f,   0.85f, 0f,  0.9f);
            case SlotState.Training:    return new Color(0f,   0.6f,  1f,  0.9f);
            case SlotState.Established: return new Color(0.2f, 1f,   0.2f, 0.9f);
            case SlotState.Maintaining: return new Color(0.5f, 1f,   0.5f, 0.8f);
            default: return Color.white;
        }
    }

    void DrawCircleGL(Vector3 center, float radius, int segs)
    {
        float step = 2f * Mathf.PI / segs;
        for (int i = 0; i < segs; i++)
        {
            float a0 = i * step, a1 = (i + 1) * step;
            GL.Vertex(center + new Vector3(Mathf.Cos(a0) * radius, 0f, Mathf.Sin(a0) * radius));
            GL.Vertex(center + new Vector3(Mathf.Cos(a1) * radius, 0f, Mathf.Sin(a1) * radius));
        }
    }

    // ── Wire Apply ────────────────────────────────────────────────────────────

    void ApplyWire(TreeNode node, Vector3 targetLocalDir, HashSet<int> trackingSet)
    {
        skeleton.WireNode(node, targetLocalDir);
        Quaternion rot = Quaternion.FromToRotation(node.growDirection, targetLocalDir);
        node.growDirection = targetLocalDir;
        skeleton.RotateAndPropagateDescendants(node, rot, null);
        skeleton.meshBuilder?.SetDirty();
        trackingSet.Add(node.id);
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

    float GetBranchAzimuth(TreeNode node)
    {
        Vector3 worldDir = skeleton.transform.TransformDirection(node.growDirection);
        Vector3 horiz    = new Vector3(worldDir.x, 0f, worldDir.z);
        if (horiz.sqrMagnitude < 0.01f)
        {
            Vector3 w = skeleton.transform.TransformPoint(node.worldPosition);
            horiz = new Vector3(w.x - skeleton.transform.position.x, 0f, w.z - skeleton.transform.position.z);
        }
        if (horiz.sqrMagnitude < 0.001f) return 0f;
        horiz.Normalize();
        return (Mathf.Atan2(horiz.x, horiz.z) * Mathf.Rad2Deg + 360f) % 360f;
    }

    Vector3 SlotTargetDirection(BranchSlot slot, BranchTier tier)
    {
        float rad = tier.targetAngleDeg * Mathf.Deg2Rad; float az = slot.azimuthDeg * Mathf.Deg2Rad;
        Vector3 outward = new Vector3(Mathf.Sin(az), 0f, Mathf.Cos(az));
        Vector3 world   = Mathf.Cos(rad) * Vector3.up + Mathf.Sin(rad) * outward;
        return skeleton.transform.InverseTransformDirection(world).normalized;
    }

    TrunkWaypoint NearestWaypoint(float h)
    {
        var wps = style.trunkWaypoints; var best = wps[0]; float bestDst = float.MaxValue;
        foreach (var wp in wps) { float d = Mathf.Abs(wp.heightAboveSoil - h); if (d < bestDst) { bestDst = d; best = wp; } }
        return best;
    }

    BranchTier TierForHeight(float h)
    {
        if (style.branchTiers == null) return null;
        foreach (var t in style.branchTiers) if (h >= t.minHeightNorm && h <= t.maxHeightNorm) return t;
        return null;
    }

    Vector3 WaypointDirection(TrunkWaypoint wp)
    {
        if (wp.targetLeanAngleDeg < 0.5f) return Vector3.up;
        Vector3 leanDir = Quaternion.Euler(0f, wp.leanAxisDeg, 0f) * Vector3.forward;
        return Quaternion.AngleAxis(wp.targetLeanAngleDeg, Vector3.Cross(Vector3.up, leanDir).normalized) * Vector3.up;
    }

    TreeNode FindNodeById(int id) { foreach (var n in skeleton.allNodes) if (n.id == id) return n; return null; }

    static float InGameDay() => GameManager.dayOfYear + GameManager.year * 366f;
    void Log(string msg) { if (verboseLog) Debug.Log(msg); }
}

public enum SlotState { Empty, Growing, Training, Established, Maintaining }

public class BranchSlot
{
    public int       tierIndex;
    public float     azimuthDeg;
    public int       assignedNodeId = -1;
    public SlotState state          = SlotState.Empty;

    public BranchSlot(int tierIndex, float azimuthDeg)
    {
        this.tierIndex  = tierIndex;
        this.azimuthDeg = azimuthDeg;
    }
}
