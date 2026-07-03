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
///   February  — stimulate empty slots (directional: steers the new shoot toward the slot azimuth)
///   Spring    — refresh slot matching; trunk wiring; pot phase advancement; remove set wires
///   October   — schedule scaffold branch wires for Growing/Training slots
///   Apr–May   — schedule overextended-tip pinches (silhouette containment)
///   June      — schedule ramification pinches (interior pad density); late-June defoliation when dense
///
/// Extended care: every auto-trim wound is pasted immediately; auto wires set faster than
/// player wires (autoStyleWireSpeedMult); pot advances XS→S→M→L at the style's phase years.
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

    [Header("Convergence")]
    [Tooltip("Wire set-speed multiplier for AutoStyler-placed wires only (player wires unaffected). " +
             "1.5 ≈ auto wires set in ~1.3 seasons instead of 2.")]
    public float autoStyleWireSpeedMult = 1.5f;
    [Tooltip("Debug/test: converge in ~5 years — wire speed 4×, action preview 3 days, double depth growth per year.")]
    public bool fastConverge = false;

    [Header("Wire Animation")]
    [Tooltip("Animate auto-wires in REAL seconds (hover → wire on → slow bend → settle) so the work " +
             "is visible even at fast game speed. Off = instant snap like before.")]
    public bool animateAutoWires = true;
    [Tooltip("Real seconds the hover ring converges on the branch before the wire goes on.")]
    public float wireAnimHoverSecs = 0.6f;
    [Tooltip("Real seconds the branch takes to bend to its target — the slow, visible part.")]
    public float wireAnimRotateSecs = 2.0f;
    [Tooltip("Real seconds the ring lingers after the bend before fading out.")]
    public float wireAnimSettleSecs = 0.5f;

    [Header("Extended Care")]
    [Tooltip("Seal every auto-trim wound with cut paste immediately (same effect as the player's paste tool).")]
    public bool autoPaste = true;
    [Tooltip("Full defoliation in late June when the canopy has at least this many leafy tips. 0 = never defoliate.")]
    public int defoliateThreshold = 80;
    [Tooltip("Minimum years between auto-defoliations. Real practice: full defoliation every year " +
             "exhausts a tree — healthy trees are done every other year at most.")]
    public int defoliateMinIntervalYears = 2;
    [Tooltip("Advance pot size (XS→S→M→L) at the style's pot-phase years each spring. Never shrinks a player-chosen pot.")]
    public bool autoRepot = true;
    [Tooltip("Hands-off soil refresh: a real repot (fresh soil, degradation/saturation reset, repot stress)\n" +
             "whenever the soil has been in the pot too long — real bonsai get fresh soil every 2–5 years\n" +
             "for life. Without this the mix degrades toward 1.0 and mismatch penalties slowly starve a\n" +
             "mature tree. Requires autoRepot.")]
    public bool autoSoilRefresh = true;
    [Tooltip("Growing seasons between hands-off soil refreshes while the tree is young (before the style's M-pot phase year).")]
    [Range(1, 8)]  public int repotIntervalYoungYears = 2;
    [Tooltip("Growing seasons between hands-off soil refreshes once mature (after the style's M-pot phase year).")]
    [Range(2, 12)] public int repotIntervalMatureYears = 4;

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
    // nodeId → true when the pinch is silhouette containment, false when ramification
    readonly Dictionary<int, bool>    pendingPinchSilhouette = new Dictionary<int, bool>();

    // nodeId → in-game day when wireSetProgress first reached 1.0 (gold)
    readonly Dictionary<int, float>   wireGoldDay    = new Dictionary<int, float>();

    // In-game day the late-June defoliation check fires; negative = nothing scheduled
    float pendingDefoliateDay = -1f;
    // Year of the last auto-defoliation (enforces defoliateMinIntervalYears)
    int   lastDefoliateYear   = -999;

    // ── Wire animation state ──────────────────────────────────────────────────
    // Auto-wires play one at a time: Hover (ring converges) → Rotate (slow bend,
    // real seconds) → Settle (ring fades). Driven by Time.deltaTime so the pace is
    // identical at any game speed.

    enum WireAnimPhase { Hover, Rotate, Settle }

    class WireAnim
    {
        public int           nodeId;
        public Vector3       targetDir;
        public HashSet<int>  trackingSet;
        public Vector3       startDir;
        public WireAnimPhase phase;
        public float         phaseTime;
    }

    readonly Queue<WireAnim> wireAnimQueue = new Queue<WireAnim>();
    readonly HashSet<int>    queuedWireIds = new HashSet<int>();   // queued or animating
    WireAnim activeWireAnim;

    [Tooltip("In-game days after a wire turns gold before AutoStyler removes it.")]
    public float unwireDelayDays = 20f;

    // fastConverge overrides for testers — see PLAN.md "AutoStyler Pacing & Convergence"
    float EffectivePreviewDays   => fastConverge ? 3f : actionPreviewDays;
    float EffectiveWireSpeedMult => fastConverge ? 4f : autoStyleWireSpeedMult;

    // ── Public accessors for UI ───────────────────────────────────────────────

    public IReadOnlyList<BranchSlot> Slots => slots;
    public int PendingTrimCount  => pendingTrims.Count;
    public int PendingWireCount  => pendingWires.Count;
    public int PendingPinchCount => pendingPinches.Count;
    public int ShapedCount       => shapedNodeIds.Count;

    /// <summary>Style match with partial credit: every occupied slot counts toward the score —
    /// Growing 50%, Training 75%, Established/Maintaining 100%. An occupied slot at any state
    /// is progress toward the style even before the branch is fully trained.</summary>
    public int MatchPercent
    {
        get
        {
            if (slots.Count == 0) return 0;
            float score = 0f;
            foreach (var s in slots)
            {
                switch (s.state)
                {
                    case SlotState.Growing:     score += 0.50f; break;
                    case SlotState.Training:    score += 0.75f; break;
                    case SlotState.Established:
                    case SlotState.Maintaining: score += 1f;    break;
                }
            }
            return Mathf.RoundToInt(score * 100f / slots.Count);
        }
    }

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
        pendingPinchSilhouette.Clear();
        wireGoldDay.Clear();
        pendingDefoliateDay = -1f;
        wireAnimQueue.Clear(); queuedWireIds.Clear(); activeWireAnim = null;
        skeleton.depthsPerYearMult = 1f;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (glMat != null) Destroy(glMat);
    }

    /// <summary>Clear ALL per-tree state. Called by TreeSkeleton.InitTree: node ids
    /// restart from 0 on a new tree, so stale id sets from the previous game silently
    /// poison the new one — found 2026-07-02 when a second in-session Quick-Start
    /// started with its trunk ids already in shapedNodeIds (never trunk-wired) and
    /// stale pending actions aimed at ghost ids → stunted, unstyled trees.</summary>
    public void ResetForNewTree()
    {
        slots.Clear();
        shapedNodeIds.Clear();
        autoWiredTrunkIds.Clear();
        autoWiredBranchIds.Clear();
        pendingTrims.Clear(); pendingWires.Clear(); pendingWireDir.Clear();
        pendingPinches.Clear(); pendingPinchSilhouette.Clear();
        wireGoldDay.Clear();
        wireAnimQueue.Clear(); queuedWireIds.Clear(); activeWireAnim = null;
        pendingDefoliateDay = -1f;
        lastDefoliateYear   = -999;
    }

    // ── Update ────────────────────────────────────────────────────────────────

    void Update()
    {
        // fastConverge doubles depth growth; keep the skeleton in sync every frame so
        // toggling the Inspector checkbox mid-game takes effect immediately.
        skeleton.depthsPerYearMult = (autoStyleEnabled && fastConverge) ? 2f : 1f;
        UpdatePendingActions();
        UpdateWireAnimation();
        RemoveSetWires();
        RemoveIshitsukiWireIfSet();
    }

    // Day the Ishitsuki rock-binding wire first turned gold (-1 = not gold / no wire)
    float ishitsukiWireGoldDay = -1f;

    /// <summary>
    /// Removes the Ishitsuki rock-binding wire (the loops wrapping tree + rock) once it has
    /// set, mirroring how branch/trunk wires are auto-unwired: track the day it turns gold
    /// (WireSetProgress >= 1), then destroy it unwireDelayDays later — the roots have
    /// gripped the stone and the wire's job is done.
    /// </summary>
    void RemoveIshitsukiWireIfSet()
    {
        if (!autoStyleEnabled) return;
        var wire = skeleton.GetComponentInChildren<IshitsukiWire>();
        if (wire == null) { ishitsukiWireGoldDay = -1f; return; }

        if (wire.WireSetProgress < 1f) { ishitsukiWireGoldDay = -1f; return; }

        float now = InGameDay();
        if (ishitsukiWireGoldDay < 0f) { ishitsukiWireGoldDay = now; return; }  // first gold frame

        if (now >= ishitsukiWireGoldDay + unwireDelayDays)
        {
            CareLog.Add("Unwire", "Removed the rock-binding wire — the roots have gripped the stone");
            Log($"[AutoStyle] Ishitsuki binding wire removed (set+{unwireDelayDays}d)");
            Destroy(wire.gameObject);
            ishitsukiWireGoldDay = -1f;
        }
    }

    // ── Wire Animation ────────────────────────────────────────────────────────

    void UpdateWireAnimation()
    {
        // Quick-Start: apply queued wires INSTANTLY. The bend animation is deliberately
        // real-time and one-at-a-time (watchable at any speed) — but at QS timescales one
        // real second ≈ months of sim, so a short queue starved wires for sim-YEARS. The
        // coil went on at hover-end while gold-tracking registration only happened at
        // animation completion, leaving red, never-tracked wires the styler couldn't
        // remove and runs the player couldn't cleanly unwire (measured 2026-07-02:
        // 69 wired, only 26 removed). Nobody watches the fast-grow — skip the show.
        if (QuickStartManager.Instance != null && QuickStartManager.Instance.IsGenerating
            && (activeWireAnim != null || wireAnimQueue.Count > 0))
        {
            while (activeWireAnim != null || wireAnimQueue.Count > 0)
            {
                var anim = activeWireAnim ?? wireAnimQueue.Dequeue();
                bool inProgress = activeWireAnim != null &&
                                  (anim.phase == WireAnimPhase.Rotate || anim.phase == WireAnimPhase.Settle);
                activeWireAnim = null;
                queuedWireIds.Remove(anim.nodeId);

                var n = FindNodeById(anim.nodeId);
                if (n == null || n.isDead || n.isTrimmed) continue;
                if (anim.phase == WireAnimPhase.Settle)  continue;          // already bent + registered
                if (n.hasWire && !inProgress)            continue;          // conflicting fresh entry

                if (!n.hasWire)
                {
                    skeleton.WireNode(n, anim.targetDir);
                    n.wireSetSpeedMult = EffectiveWireSpeedMult;
                }
                // Snap the bend the animation would have eased through.
                Quaternion delta = Quaternion.FromToRotation(n.growDirection, anim.targetDir);
                n.growDirection = anim.targetDir.normalized;
                skeleton.RotateAndPropagateDescendants(n, delta, null);
                anim.trackingSet.Add(n.id);
                LogWireApplied(n, anim.targetDir, anim.trackingSet);
                Log($"[AutoStyle] Wire snapped on instantly (quick-start) node={n.id}");
            }
            skeleton.meshBuilder?.SetDirty();
            return;
        }

        if (activeWireAnim == null && wireAnimQueue.Count > 0)
        {
            activeWireAnim = wireAnimQueue.Dequeue();
            var start = FindNodeById(activeWireAnim.nodeId);
            if (start == null || start.isDead || start.isTrimmed || start.hasWire)
            { queuedWireIds.Remove(activeWireAnim.nodeId); activeWireAnim = null; return; }
            activeWireAnim.startDir  = start.growDirection;
            activeWireAnim.phase     = WireAnimPhase.Hover;
            activeWireAnim.phaseTime = 0f;
        }
        if (activeWireAnim == null) return;

        var node = FindNodeById(activeWireAnim.nodeId);
        if (node == null || node.isDead || node.isTrimmed)
        { queuedWireIds.Remove(activeWireAnim.nodeId); activeWireAnim = null; return; }

        // Real time on purpose — the animation pace is identical at any game speed.
        activeWireAnim.phaseTime += Time.deltaTime;

        switch (activeWireAnim.phase)
        {
            case WireAnimPhase.Hover:
                if (activeWireAnim.phaseTime >= wireAnimHoverSecs)
                {
                    // Coil goes on at the branch's current angle; the bend follows.
                    skeleton.WireNode(node, activeWireAnim.targetDir);
                    node.wireSetSpeedMult = EffectiveWireSpeedMult;
                    activeWireAnim.phase     = WireAnimPhase.Rotate;
                    activeWireAnim.phaseTime = 0f;
                }
                break;

            case WireAnimPhase.Rotate:
            {
                float t     = Mathf.Clamp01(activeWireAnim.phaseTime / Mathf.Max(0.01f, wireAnimRotateSecs));
                float eased = Mathf.SmoothStep(0f, 1f, t);
                Vector3 want = Vector3.Slerp(activeWireAnim.startDir, activeWireAnim.targetDir, eased).normalized;
                // Incremental delta each frame keeps descendants consistent even while the
                // tree is actively growing around the animation.
                Quaternion delta = Quaternion.FromToRotation(node.growDirection, want);
                node.growDirection = want;
                skeleton.RotateAndPropagateDescendants(node, delta, null);
                skeleton.meshBuilder?.SetDirty();
                if (t >= 1f)
                {
                    activeWireAnim.trackingSet.Add(node.id);
                    activeWireAnim.phase     = WireAnimPhase.Settle;
                    activeWireAnim.phaseTime = 0f;
                    LogWireApplied(node, activeWireAnim.targetDir, activeWireAnim.trackingSet);
                    Log($"[AutoStyle] Wire bent node={node.id}");
                }
                break;
            }

            case WireAnimPhase.Settle:
                if (activeWireAnim.phaseTime >= wireAnimSettleSecs)
                {
                    queuedWireIds.Remove(activeWireAnim.nodeId);
                    activeWireAnim = null;
                }
                break;
        }
    }

    void UpdatePendingActions()
    {
        float now = InGameDay();

        if (pendingTrims.Count > 0)
        {
            foreach (int id in new List<int>(pendingTrims.Keys))
            {
                var n = FindNodeById(id);
                if (n == null || n.isTrimmed || n.isDead) { pendingTrims.Remove(id); continue; }
                if (now >= pendingTrims[id])
                {
                    Log($"[AutoStyle] Trim fired node={id}");
                    float trimAz = GetBranchAzimuth(n);
                    var cutSite = n.parent;   // TrimNode wounds the parent — capture before the cut
                    ProgressionManager.AutomationActive = true;   // auto-styler cuts don't earn the player's badge
                    try { skeleton.TrimNode(n); }
                    finally { ProgressionManager.AutomationActive = false; }
                    CareLog.Add("Trim", $"Removed branch at {trimAz:F0}° — no open slot in its height band", id);
                    if (autoPaste && cutSite != null && cutSite.hasWound && !cutSite.pasteApplied)
                    {
                        skeleton.ApplyPaste(cutSite);
                        CareLog.Add("Paste", "Sealed the fresh cut with paste", cutSite.id);
                        Log($"[AutoStyle] Paste applied to cut site node={cutSite.id}");
                    }
                    pendingTrims.Remove(id);
                }
            }
        }

        if (pendingDefoliateDay >= 0f && now >= pendingDefoliateDay)
        {
            pendingDefoliateDay = -1f;
            TryAutoDefoliate();
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
                if (n == null || n.isDead || n.isTrimmed || !n.isTerminal)
                { pendingPinches.Remove(id); pendingPinchSilhouette.Remove(id); continue; }
                if (now >= pendingPinches[id])
                {
                    Log($"[AutoStyle] Pinch fired node={id}");
                    skeleton.PinchNode(n);
                    bool silhouette = !pendingPinchSilhouette.TryGetValue(id, out bool s) || s;
                    CareLog.Add("Pinch", silhouette
                        ? "Pinched a tip past the silhouette — containing the outline"
                        : "Pinched a matured shoot to force division (ramification)", id);
                    pendingPinches.Remove(id); pendingPinchSilhouette.Remove(id);
                }
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
        AdvancePotPhase();
    }

    void HandleMonthChanged(int month)
    {
        if (!IsReady()) return;
        switch (month)
        {
            case 2:  StimulateEmptySlots(); break;
            case 4:
            case 5:  PlanPinches(overextendedOnly: true); break;
            case 6:
                if (style.enableRamification) PlanPinches(overextendedOnly: false);
                if (defoliateThreshold > 0) pendingDefoliateDay = InGameDay() + 20f;  // fires late June
                break;
            case 10: PlanScaffoldWires(); break;
        }
    }

    // ── Extended Care ─────────────────────────────────────────────────────────

    /// <summary>Late June: if the canopy is dense enough, strip all leaves. Mid-summer
    /// defoliation forces finer back-budding and opens interior branches to light.</summary>
    void TryAutoDefoliate()
    {
        if (!IsReady() || defoliateThreshold <= 0) return;
        // Never auto-defoliate conifers — it can kill them. Broadleaf species only.
        if (skeleton.species != null && !skeleton.species.canDefoliate) return;
        if (GameManager.year - lastDefoliateYear < defoliateMinIntervalYears) return;
        int tips = 0;
        foreach (var n in skeleton.allNodes)
            if (n.isTerminal && !n.isRoot && !n.isDead && !n.isTrimmed) tips++;
        if (tips < defoliateThreshold) return;
        var leaves = GetComponent<LeafManager>();
        if (leaves == null) return;
        leaves.DefoliateAll();
        lastDefoliateYear = GameManager.year;
        CareLog.Add("Defoliate", $"Defoliated {tips} tips to force finer back-budding and light the interior");
        Debug.Log($"[AutoStyle] Defoliated — {tips} tips ≥ threshold {defoliateThreshold} | year={GameManager.year}");
    }

    /// <summary>Spring: real hands-off repotting. Two triggers, one Repot call:
    /// (a) pot-size advancement XS→S→M→L at the style's phase years (restriction early
    /// thickens the trunk; room later supports the maturing root mass; never shrinks a
    /// player-chosen pot), and (b) periodic soil refresh when the mix has been in the pot
    /// longer than the interval — previously this method only resized geometry via
    /// ApplyPotSize and NEVER called PotSoil.Repot, so soilDegradation/saturation/
    /// seasonsSinceRepot were never reset under hands-off care and mismatch penalties
    /// slowly starved mature trees (found 2026-07-02). Bypasses the repot mini-game;
    /// fires in early spring so Repot's good-timing window applies.</summary>
    void AdvancePotPhase()
    {
        if (!autoRepot) return;
        var potSoil = GetComponent<PotSoil>();
        if (potSoil == null || skeleton.plantingYear < 0) return;

        var phases = (style.potPhaseStartYears != null && style.potPhaseStartYears.Length >= 3)
            ? style.potPhaseStartYears
            : new[] { 6, 13, 26 };

        int treeYear = GameManager.year - skeleton.plantingYear + 1;
        var target = treeYear >= phases[2] ? PotSoil.PotSize.L
                   : treeYear >= phases[1] ? PotSoil.PotSize.M
                   : treeYear >= phases[0] ? PotSoil.PotSize.S
                   :                         PotSoil.PotSize.XS;

        bool sizeUp = (int)target > (int)potSoil.potSize;

        // Soil refresh due? seasonsSinceRepot ticks once per growing season (≈ once per
        // in-game year). Young trees get fresh soil more often than mature ones.
        int intervalYears = treeYear >= phases[1] ? repotIntervalMatureYears : repotIntervalYoungYears;
        bool soilDue = autoSoilRefresh && potSoil.seasonsSinceRepot >= intervalYears;

        if (!sizeUp && !soilDue) return;

        var prev    = potSoil.potSize;
        var newSize = sizeUp ? target : potSoil.potSize;
        // Keep the pot's current mix (player's choice) unless it's Custom, then the classic mix.
        var preset  = potSoil.preset != PotSoil.SoilPreset.Custom
                    ? potSoil.preset : PotSoil.SoilPreset.ClassicBonsai;

        potSoil.Repot(skeleton, preset, newSize, sizeChanged: sizeUp);

        // Root-prune — the other half of a real repot — but ONLY when the roots actually
        // need it. First F8 attempt pruned nothing (roots hit the 900-node cap: "kraken
        // pot"); second attempt pruned on EVERY refresh, and the 2-year full discard
        // killed young Quick-Start trees (uptake never recovered → canopy died → shading
        // cascaded into the trunk, 2026-07-02). Pot-bound pressure is the bonsai-correct
        // trigger: prune when crowded, leave establishing root systems alone.
        bool rootPruned = false;
        if (skeleton.RootPressureFactor() > 0.35f)
        {
            GetComponent<RootRakeManager>()?.DiscardAndRegenerateRoots();
            rootPruned = true;
        }

        CareLog.Add("Repot", sizeUp
            ? $"Repotted into a {newSize} pot{(rootPruned ? " — root-pruned," : " —")} fresh {preset} soil (tree year {treeYear})"
            : $"Repotted{(rootPruned ? " — root-pruned and" : " —")} refreshed the {preset} soil (tree year {treeYear})");
        Debug.Log($"[AutoStyle] Repot pot {prev}→{newSize} (sizeUp={sizeUp} soilDue={soilDue} rootPruned={rootPruned}, tree year {treeYear}) | year={GameManager.year}");
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
                CareLog.Add("Unwire", $"Wire fully set — removed {unwireDelayDays:F0} days after gold, before it could bite", id);
                Log($"[AutoStyle] Unwire (gold+{unwireDelayDays}d) node={id}");
            }
        }
        foreach (int id in toRemove) set.Remove(id);
    }

    // ── Slot Management ───────────────────────────────────────────────────────

    void RefreshSlots()
    {
        // Slots are rebuilt from scratch each spring — there's no persistent slot list between
        // seasons. That means assignedNodeId is always re-matched, so a branch that moved
        // (via wiring) will be re-evaluated against all slots on the next spring.
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
            if (!IsScaffoldBase(n)) continue;
            available.Add(n);
        }

        // Prefer already-shaped nodes when greedy-matching (stability)
        available.Sort((a, b) => (shapedNodeIds.Contains(a.id) ? 0 : 1).CompareTo(shapedNodeIds.Contains(b.id) ? 0 : 1));

        if (verboseLog)
        {
            Log($"[AutoStyle] RefreshSlots — {available.Count} scaffold candidates | treeH={treeH:F2} soilY={soilY:F2}");
            foreach (var n in available)
            {
                float candY = (skeleton.transform.TransformPoint(n.worldPosition).y - soilY) / treeH;
                Log($"[AutoStyle]   candidate node={n.id} h={candY:F2} az={GetBranchAzimuth(n):F0}° len={n.length:F2}");
            }
        }

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
        // Count physical scaffold branches (base segments) — not trunk or mid-chord segments.
        int scaffoldCount = 0;
        foreach (var x in skeleton.allNodes)
            if (IsScaffoldBase(x)) scaffoldCount++;

        if (scaffoldCount > slots.Count)
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
                pendingTrims[n.id] = InGameDay() + EffectivePreviewDays;
                Log($"[AutoStyle] Excess branch queued node={n.id} tier={tierIdx}");
            }
        }

        int occ = slots.Count(s => s.assignedNodeId >= 0);
        CareLog.Add("Season", $"Spring review — {occ}/{slots.Count} branch positions filled ({MatchPercent}% style match)");
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
            // Never wire the base node: its angle is the planting angle (fixed in reality),
            // and rotating it whips the entire tree — and everything hanging off it — around
            // the soil line in one frame.
            if (n == skeleton.root) continue;
            if (shapedNodeIds.Contains(n.id) || n.hasWire || queuedWireIds.Contains(n.id)) continue;
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
            if (n.length <= 0.001f || pendingWires.ContainsKey(n.id) || queuedWireIds.Contains(n.id)) continue;
            var tier = style.branchTiers[slot.tierIndex]; Vector3 targetDir = SlotTargetDirection(slot, tier);
            if (Vector3.Angle(n.growDirection, targetDir) < style.wireThresholdDeg) continue;
            pendingWires[n.id] = InGameDay() + EffectivePreviewDays; pendingWireDir[n.id] = targetDir;
            scheduled++; Log($"[AutoStyle] Wire scheduled node={n.id} az={slot.azimuthDeg:F0}°");
        }
        if (scheduled > 0) Log($"[AutoStyle] October — {scheduled} wires | year={GameManager.year}");
    }

    // ── Empty Slot Stimulation (February) ────────────────────────────────────

    void StimulateEmptySlots()
    {
        float soilY = SoilWorldY(); float treeH = Mathf.Max(0.01f, skeleton.CachedTreeHeight); int count = 0;
        // One slot per trunk node per pass — otherwise two empty slots in the same tier
        // would claim the same node and the second azimuth would overwrite the first.
        var claimed = new HashSet<int>();
        foreach (var slot in slots)
        {
            if (slot.assignedNodeId >= 0) continue;
            var tier = style.branchTiers[slot.tierIndex];
            float targetY = soilY + (tier.minHeightNorm + tier.maxHeightNorm) * 0.5f * treeH;
            TreeNode best = null; float bestDist = float.MaxValue;
            foreach (var n in skeleton.allNodes)
            {
                if (n.depth != 0 || n.isRoot || n.isDead || n.isTrimmed || claimed.Contains(n.id)) continue;
                // The lateral spawns at this node's TIP — a buried or sagged-below-soil trunk
                // node would sprout a shoot underground that tunnels out the side of the pot.
                if (skeleton.transform.TransformPoint(n.tipPosition).y < soilY + 0.01f) continue;
                float d = Mathf.Abs(skeleton.transform.TransformPoint(n.worldPosition).y - targetY);
                if (d < bestDist) { bestDist = d; best = n; }
            }
            if (best != null)
            {
                best.backBudStimulated       = true;
                best.preferredLateralAzimuth = slot.azimuthDeg;   // steer the new shoot toward the slot
                claimed.Add(best.id);
                count++;
            }
        }
        if (count > 0)
        {
            CareLog.Add("BackBud", $"Encouraged buds at {count} trunk point{(count == 1 ? "" : "s")} facing empty branch positions");
            Log($"[AutoStyle] February — stimulated {count} empty slots (directional) | year={GameManager.year}");
        }
    }

    // ── Pinch Planning ────────────────────────────────────────────────────────

    void PlanPinches(bool overextendedOnly)
    {
        float soilY = SoilWorldY(); float treeH = Mathf.Max(0.01f, skeleton.CachedTreeHeight); int queued = 0;
        Vector3 trunkXZ = new Vector3(skeleton.transform.position.x, 0f, skeleton.transform.position.z);
        foreach (var n in skeleton.allNodes)
        {
            if (!n.isTerminal || n.isRoot || n.isDead || n.isTrimmed) continue;
            // Silhouette containment targets tips that are actively extending. Ramification
            // pinches the season's FINISHED shoots — by June most have set, and cutting the
            // matured shoot to force division is the whole technique — so no isGrowing there.
            if (overextendedOnly && !n.isGrowing) continue;
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
            pendingPinches[n.id] = InGameDay() + EffectivePreviewDays * 0.5f;
            pendingPinchSilhouette[n.id] = overextendedOnly;
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

        // Trim candidates — orange X on every unmatched scaffold branch (will be removed next spring)
        GL.Begin(GL.LINES); GL.Color(new Color(1f, 0.35f, 0f, 0.95f));
        foreach (var n in skeleton.allNodes)
        {
            if (!IsScaffoldBase(n)) continue;
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

        // Active wire animation — bright ring that converges on the branch (hover), pulses
        // through the slow bend, and fades out (settle). Recomputed from live node geometry
        // each frame so it follows the branch as it bends.
        if (activeWireAnim != null)
        {
            var wn = FindNodeById(activeWireAnim.nodeId);
            if (wn != null)
            {
                Vector3 mid = skeleton.transform.TransformPoint(wn.worldPosition + wn.growDirection * (wn.length * 0.5f));
                float baseR = Mathf.Clamp(wn.radius * 12f, 0.08f, 0.26f);
                float r = baseR, alpha = 1f;
                switch (activeWireAnim.phase)
                {
                    case WireAnimPhase.Hover:
                        float ht = Mathf.Clamp01(activeWireAnim.phaseTime / Mathf.Max(0.01f, wireAnimHoverSecs));
                        r = Mathf.Lerp(baseR * 3f, baseR, Mathf.SmoothStep(0f, 1f, ht));
                        break;
                    case WireAnimPhase.Rotate:
                        r = baseR * (1f + 0.12f * Mathf.Sin(Time.time * 12f));
                        break;
                    case WireAnimPhase.Settle:
                        alpha = 1f - Mathf.Clamp01(activeWireAnim.phaseTime / Mathf.Max(0.01f, wireAnimSettleSecs));
                        break;
                }
                GL.Begin(GL.LINES); GL.Color(new Color(0.25f, 0.95f, 1f, alpha));
                DrawCircleGL(mid, r, 20);
                if (activeWireAnim.phase == WireAnimPhase.Rotate)
                {
                    // Tick showing where the branch is being pulled toward
                    Vector3 tgt = skeleton.transform.TransformDirection(activeWireAnim.targetDir).normalized;
                    GL.Vertex(mid); GL.Vertex(mid + tgt * baseR * 2.2f);
                }
                GL.End();
            }
        }

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
        if (animateAutoWires)
        {
            // Queue the visible hover→bend→settle sequence; one plays at a time.
            if (queuedWireIds.Contains(node.id)) return;
            queuedWireIds.Add(node.id);
            wireAnimQueue.Enqueue(new WireAnim
            { nodeId = node.id, targetDir = targetLocalDir, trackingSet = trackingSet });
            return;
        }
        ApplyWireInstant(node, targetLocalDir, trackingSet);
    }

    void ApplyWireInstant(TreeNode node, Vector3 targetLocalDir, HashSet<int> trackingSet)
    {
        skeleton.WireNode(node, targetLocalDir);
        node.wireSetSpeedMult = EffectiveWireSpeedMult;   // auto wires set faster than player wires
        Quaternion rot = Quaternion.FromToRotation(node.growDirection, targetLocalDir);
        node.growDirection = targetLocalDir;
        skeleton.RotateAndPropagateDescendants(node, rot, null);
        skeleton.meshBuilder?.SetDirty();
        trackingSet.Add(node.id);
        LogWireApplied(node, targetLocalDir, trackingSet);
    }

    void LogWireApplied(TreeNode node, Vector3 targetLocalDir, HashSet<int> trackingSet)
    {
        Vector3 w   = skeleton.transform.TransformDirection(targetLocalDir).normalized;
        float lean  = Vector3.Angle(w, Vector3.up);
        float az    = (Mathf.Atan2(w.x, w.z) * Mathf.Rad2Deg + 360f) % 360f;
        CareLog.Add("Wire", trackingSet == autoWiredTrunkIds
            ? $"Wired a trunk section toward its waypoint line ({lean:F0}° lean)"
            : $"Wired branch toward {az:F0}° at {lean:F0}° from vertical — training to its slot", node.id);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    bool IsReady() =>
        autoStyleEnabled && style != null && skeleton != null && skeleton.root != null &&
        GameManager.Instance != null && GameManager.Instance.state != GameState.TreeDead;

    /// <summary>The FIRST segment of a physical scaffold branch — the depth-1 node whose
    /// parent is a trunk (depth-0) node. A branch's first chord is a CHAIN of depth-1
    /// subdivision segments; matching every segment would let one branch occupy several
    /// slots and put trim markers mid-branch, so slot logic only ever looks at the base.</summary>
    static bool IsScaffoldBase(TreeNode n) =>
        n.depth == 1 && !n.isRoot && !n.isDead && !n.isTrimmed && n.length > 0.001f &&
        n.parent != null && n.parent.depth == 0;

    float SoilWorldY()
    {
        float y = skeleton.plantingSurfacePoint.y;
        return y != 0f ? y : skeleton.transform.position.y;
    }

    float GetBranchAzimuth(TreeNode node)
    {
        Vector3 worldDir = skeleton.transform.TransformDirection(node.growDirection);
        Vector3 horiz    = new Vector3(worldDir.x, 0f, worldDir.z);
        // Fallback: a nearly-vertical branch has no meaningful growDirection azimuth,
        // so use the branch's world position relative to trunk instead.
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
        // Builds the direction a branch SHOULD point for its slot:
        //   "targetAngleDeg from vertical, aimed at the slot's compass azimuth"
        // Result is returned in LOCAL space (skeleton.transform.InverseTransformDirection)
        // so it can be stored on node.growDirection directly.
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
