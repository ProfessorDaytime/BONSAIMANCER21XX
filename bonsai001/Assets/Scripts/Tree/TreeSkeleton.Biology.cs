using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.InputSystem;
using Debug = UnityEngine.Debug;
using Random = UnityEngine.Random;

/// <summary>
/// TreeSkeleton — Biology partial. Graft system, sibling branch fusion, fungal system.
/// Split from the 6,373-line TreeSkeleton.cs (F5, 2026-07-03) with zero
/// behaviour change; all serialized fields remain in TreeSkeleton.cs.
/// </summary>
public partial class TreeSkeleton : MonoBehaviour
{
    // ── Graft System ──────────────────────────────────────────────────────────

    [Header("Grafting")]
    [Tooltip("Maximum world-unit distance between source tip and target node for a graft attempt to be valid.")]
    [SerializeField] float graftMaxDistance = 3f;
    [Tooltip("Number of growing seasons until a graft fuses. Default 2.")]
    [SerializeField] int graftSeasonsToFuse = 2;

    // ── Sibling Branch Fusion ─────────────────────────────────────────────────

    [Header("Sibling Fusion")]
    [Tooltip("Seasons for sibling tips touching to fully fuse into one unit. Default 4.")]
    [SerializeField] int fusionSeasonsToFuse = 4;
    [Tooltip("Detection threshold: fuse when tip-to-tip distance < (rA+rB) × this multiplier. Default 2.5.")]
    [SerializeField] float fusionTipProximityMult = 2.5f;

    /// <summary>All active and completed sibling fusion bonds on this tree.</summary>
    public readonly List<FusionBond> fusionBonds = new List<FusionBond>();

    public class FusionBond
    {
        public int  nodeIdA;
        public int  nodeIdB;
        public int  seasonsElapsed;
        public bool isComplete;
        public int  bridgeId;    // id of the bridge node created on success (-1 until then)

        public FusionBond(int a, int b) { nodeIdA = a; nodeIdB = b; bridgeId = -1; }
    }

    /// <summary>All active graft attempts on this tree.</summary>
    public readonly List<GraftAttempt> graftAttempts = new List<GraftAttempt>();

    /// <summary>Exposed so TreeInteraction can read it for the progress line colour.</summary>
    public int GraftSeasonsToFuse => graftSeasonsToFuse;

    /// <summary>Source node awaiting a second click to form a graft pair. Null when no graft is pending.</summary>
    public TreeNode pendingGraftSource;

    public class GraftAttempt
    {
        public int  sourceId;
        public int  targetId;
        public int  seasonsElapsed;
        public bool succeeded;
        public int  bridgeId;    // id of the bridge node created on success (-1 until then)

        public GraftAttempt(int src, int tgt) { sourceId = src; targetId = tgt; bridgeId = -1; }
    }

    /// <summary>
    /// Begin a graft attempt. Source must be a living non-root terminal.
    /// Target must be on a different ancestry chain, within graftMaxDistance.
    /// Returns null with a reason string on failure.
    /// </summary>
    public (bool ok, string reason) TryStartGraft(TreeNode source, TreeNode target)
    {
        if (source == null || target == null)
            return (false, "null node");
        if (source == target)
            return (false, "same node");
        if (source.isRoot || target.isRoot)
            return (false, "roots cannot be grafted");
        if (!source.isTerminal)
            return (false, "source must be a terminal tip");
        if (source.isTrimmed || target.isTrimmed || source.isDead || target.isDead)
            return (false, "node is trimmed or dead");
        if (source.isGraftSource)
            return (false, "source already has a pending graft");

        // Source and target must not share ancestry (no grafting within the same branch run)
        TreeNode n = target;
        while (n != null) { if (n == source) return (false, "target is ancestor of source"); n = n.parent; }
        n = source;
        while (n != null) { if (n == target) return (false, "source is ancestor of target"); n = n.parent; }

        // Distance check: tip of source to base of target
        float dist = Vector3.Distance(
            transform.TransformPoint(source.tipPosition),
            transform.TransformPoint(target.worldPosition));
        if (dist > graftMaxDistance)
            return (false, $"too far ({dist:F2}m > {graftMaxDistance:F2}m max)");

        source.isGraftSource = true;
        graftAttempts.Add(new GraftAttempt(source.id, target.id));
        pendingGraftSource   = null;
        Debug.Log($"[Graft] Started: source={source.id} → target={target.id} dist={dist:F2}");
        return (true, "");
    }

    /// <summary>Cancel the pending source selection (ESC / tool switch).</summary>
    public void CancelPendingGraft()
    {
        pendingGraftSource = null;
    }

    /// <summary>
    /// Advance all active grafts by one growing season.
    /// Called from StartNewGrowingSeason.
    /// </summary>
    void AdvanceGrafts()
    {
        for (int i = graftAttempts.Count - 1; i >= 0; i--)
        {
            var g = graftAttempts[i];
            if (g.succeeded) continue;

            // Look up live nodes
            TreeNode src = allNodes.Find(n => n.id == g.sourceId);
            TreeNode tgt = allNodes.Find(n => n.id == g.targetId);

            // Abort if either node is gone / dead / no longer a terminal
            if (src == null || tgt == null || src.isTrimmed || tgt.isTrimmed || src.isDead || tgt.isDead)
            {
                if (src != null) src.isGraftSource = false;
                Debug.Log($"[Graft] Attempt {g.sourceId}→{g.targetId} aborted (node gone/dead)");
                graftAttempts.RemoveAt(i);
                continue;
            }

            g.seasonsElapsed++;

            // Bend source tip growDirection toward target each season (halfway per season)
            Vector3 srcTipWorld = transform.TransformPoint(src.tipPosition);
            Vector3 tgtWorld    = transform.TransformPoint(tgt.worldPosition);
            Vector3 toTarget    = (tgtWorld - srcTipWorld).normalized;
            float   bendFactor  = 0.35f * g.seasonsElapsed;   // 35% per season
            src.growDirection   = Vector3.Slerp(src.growDirection, toTarget, Mathf.Clamp01(bendFactor)).normalized;
            meshBuilder.SetDirty();

            if (g.seasonsElapsed < graftSeasonsToFuse) continue;

            // ── Fuse ────────────────────────────────────────────────────────
            float bridgeLen = Vector3.Distance(
                transform.TransformPoint(src.tipPosition),
                transform.TransformPoint(tgt.worldPosition));

            var bridge = new TreeNode(
                nextId++,
                src.depth + 1,
                src.tipPosition,
                toTarget.magnitude > 0.001f ? transform.InverseTransformDirection(toTarget) : src.growDirection,
                src.radius * 0.6f,
                bridgeLen,
                src);
            bridge.isGraftBridge = true;
            bridge.length        = bridgeLen;   // already fully grown
            bridge.isGrowing     = false;
            bridge.age           = bridge.targetLength;

            src.children.Add(bridge);
            allNodes.Add(bridge);

            g.succeeded  = true;
            g.bridgeId   = bridge.id;
            src.isGraftSource = false;

            RecalculateRadii(root);
            meshBuilder.SetDirty();
            Debug.Log($"[Graft] Fused: source={src.id} → target={tgt.id} | bridge={bridge.id} len={bridgeLen:F2}");
        }
    }

    // ── Sibling Branch Fusion ─────────────────────────────────────────────────

    /// <summary>
    /// Scan all non-root terminal siblings each spring. When two tips from the same
    /// parent are close enough they are registered as a new FusionBond.
    /// Called from StartNewGrowingSeason after all new growth is spawned.
    /// </summary>
    void DetectNewFusions()
    {
        // Build a per-parent list of living terminal branches
        var byParent = new Dictionary<int, List<TreeNode>>();
        foreach (var n in allNodes)
        {
            if (n.isRoot || n.isTrimmed || n.isDead || !n.isTerminal) continue;
            if (n.parent == null) continue;

            if (!byParent.TryGetValue(n.parent.id, out var list))
            {
                list = new List<TreeNode>();
                byParent[n.parent.id] = list;
            }
            list.Add(n);
        }

        foreach (var kv in byParent)
        {
            var siblings = kv.Value;
            for (int i = 0; i < siblings.Count - 1; i++)
            {
                for (int j = i + 1; j < siblings.Count; j++)
                {
                    var a = siblings[i];
                    var b = siblings[j];

                    // Skip if either node is already in any bond
                    bool alreadyBonded = fusionBonds.Exists(fb =>
                        fb.nodeIdA == a.id || fb.nodeIdB == a.id ||
                        fb.nodeIdA == b.id || fb.nodeIdB == b.id);
                    if (alreadyBonded) continue;

                    float threshold = (a.radius + b.radius) * fusionTipProximityMult;
                    float dist = Vector3.Distance(
                        transform.TransformPoint(a.tipPosition),
                        transform.TransformPoint(b.tipPosition));
                    if (dist <= threshold)
                    {
                        fusionBonds.Add(new FusionBond(a.id, b.id));
                        Debug.Log($"[Fusion] New bond: {a.id}↔{b.id} dist={dist:F3} threshold={threshold:F3}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Advance all pending fusion bonds by one season. When seasonsElapsed reaches
    /// fusionSeasonsToFuse, a bridge node is created between the two tips.
    /// Called from StartNewGrowingSeason after DetectNewFusions.
    /// </summary>
    void AdvanceFusions()
    {
        for (int i = fusionBonds.Count - 1; i >= 0; i--)
        {
            var fb = fusionBonds[i];
            if (fb.isComplete) continue;

            TreeNode a = allNodes.Find(n => n.id == fb.nodeIdA);
            TreeNode b = allNodes.Find(n => n.id == fb.nodeIdB);

            if (a == null || b == null || a.isTrimmed || b.isTrimmed || a.isDead || b.isDead)
            {
                Debug.Log($"[Fusion] Bond {fb.nodeIdA}↔{fb.nodeIdB} aborted (node gone/dead/trimmed)");
                fusionBonds.RemoveAt(i);
                continue;
            }

            fb.seasonsElapsed++;

            if (fb.seasonsElapsed < fusionSeasonsToFuse) continue;

            // ── Create bridge node ───────────────────────────────────────────
            Vector3 tipAWorld = transform.TransformPoint(a.tipPosition);
            Vector3 tipBWorld = transform.TransformPoint(b.tipPosition);
            float   bridgeLen = Vector3.Distance(tipAWorld, tipBWorld);

            if (bridgeLen < 0.001f)
            {
                fusionBonds.RemoveAt(i);
                continue;
            }

            Vector3 bridgeDirLocal = transform.InverseTransformDirection(
                (tipBWorld - tipAWorld).normalized);

            var bridge = new TreeNode(
                nextId++,
                a.depth + 1,
                a.tipPosition,
                bridgeDirLocal,
                Mathf.Min(a.radius, b.radius) * 0.65f,
                bridgeLen,
                a);
            bridge.isGraftBridge = true;
            bridge.length        = bridgeLen;
            bridge.isGrowing     = false;
            bridge.age           = bridge.targetLength;

            a.children.Add(bridge);
            allNodes.Add(bridge);

            fb.isComplete = true;
            fb.bridgeId   = bridge.id;

            RecalculateRadii(root);
            meshBuilder.SetDirty();
            Debug.Log($"[Fusion] Complete: {a.id}↔{b.id} bridge={bridge.id} len={bridgeLen:F3}");
        }
    }

    // ── Fungal System ─────────────────────────────────────────────────────────

    /// <summary>
    /// Per-season fungal infection update. Called from StartNewGrowingSeason.
    ///
    /// At-risk conditions for a node:
    ///   - Open wound (hasWound &amp;&amp; !pasteApplied)
    ///   - Over-watered roots (isRoot &amp;&amp; soilMoisture > 0.9)
    ///   - Low health (&lt;0.5)
    ///
    /// If at-risk: fungalLoad increases by fungalLoadIncrease.
    /// Spread: each infected node has fungalSpreadChance to nudge fungalLoad on each
    ///   adjacent (parent / child) node by half the increase.
    /// Damage: applied if fungalLoad > 0.4, scaled by excess.
    /// Recovery: fungalLoad drops by fungalRecoveryRate per season when no longer at-risk.
    /// </summary>
    void UpdateFungalInfection()
    {
        // Collect spread deltas separately so we don't double-count in one pass
        var spreadDelta = new Dictionary<int, float>(allNodes.Count);
        foreach (var n in allNodes) spreadDelta[n.id] = 0f;

        int infectedCount = 0;
        foreach (var node in allNodes)
        {
            if (node.isTrimmed) continue;

            bool atRisk = (node.hasWound && !node.pasteApplied)
                       || (node.isRoot && soilMoisture > 0.9f)
                       || (node.health < 0.5f);

            if (atRisk)
            {
                node.fungalLoad = Mathf.Min(1f, node.fungalLoad + fungalLoadIncrease);
            }
            else if (node.fungalLoad > 0f)
            {
                node.fungalLoad = Mathf.Max(0f, node.fungalLoad - fungalRecoveryRate);
            }

            // Spread to neighbours
            if (node.fungalLoad > 0.05f)
            {
                float nudge = node.fungalLoad * 0.5f * fungalLoadIncrease;
                if (node.parent != null && Random.value < fungalSpreadChance)
                    spreadDelta[node.parent.id] += nudge;
                foreach (var child in node.children)
                    if (Random.value < fungalSpreadChance)
                        spreadDelta[child.id] += nudge;

                // Damage: excess above 0.4
                float excess = node.fungalLoad - 0.4f;
                if (excess > 0f)
                {
                    ApplyDamage(node, DamageType.FungalInfection, fungalDamagePerLoad * excess);
                    infectedCount++;
                }
            }
        }

        // Apply spread
        foreach (var node in allNodes)
        {
            float d = spreadDelta[node.id];
            if (d > 0f) node.fungalLoad = Mathf.Min(1f, node.fungalLoad + d);
        }

        if (verboseLog && infectedCount > 0)
            Debug.Log($"[Fungal] {infectedCount} node(s) took fungal damage | year={GameManager.year}");
    }

    /// <summary>
    /// Per-season mycorrhizal network update. Called from StartNewGrowingSeason after
    /// UpdateFungalInfection so fungalLoad is already updated.
    ///
    /// Root nodes that stay healthy (health>0.75, fungalLoad&lt;0.1) for
    /// mycorrhizalHealthySeasonsRequired seasons gain the isMycorrhizal flag.
    /// Nodes that go over threshold lose it.
    /// </summary>
    void UpdateMycorrhizal()
    {
        int gained = 0, lost = 0;
        foreach (var node in allNodes)
        {
            if (!node.isRoot || node.isTrimmed) continue;

            bool healthy = node.health > 0.75f && node.fungalLoad < 0.1f;
            if (healthy)
            {
                node.healthySeasonsCount++;
                if (!node.isMycorrhizal && node.healthySeasonsCount >= mycorrhizalHealthySeasonsRequired)
                {
                    node.isMycorrhizal = true;
                    gained++;
                }
            }
            else
            {
                node.healthySeasonsCount = 0;
                if (node.isMycorrhizal) { node.isMycorrhizal = false; lost++; }
            }
        }

        if (verboseLog && (gained > 0 || lost > 0))
            Debug.Log($"[Fungal] Mycorrhizal: +{gained} gained, -{lost} lost | year={GameManager.year}");
    }

    // Wound System

    void CreateWoundObject(TreeNode node)
    {
        // The wound is now rendered as part of the unified tree mesh (callus cap geometry
        // in TreeMeshBuilder) driven by node.hasWound / node.woundAge / node.pasteApplied
        // via vertex color channels G and B.  We keep a lightweight empty anchor here so
        // all existing woundObjects book-keeping (heal loop, undo, etc.) stays unchanged.

        if (woundObjects.TryGetValue(node.id, out var existing))
        {
            if (existing != null) Destroy(existing);
            woundObjects.Remove(node.id);
        }

        var go = new GameObject($"_WoundAnchor_{node.id}");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = node.tipPosition;

        woundObjects[node.id] = go;
        // Mesh rebuild is already triggered by the trim that calls this.
        int liveWounds = 0, scarred = 0;
        foreach (var n in allNodes) { if (n.hasWound) liveWounds++; if (n.hadWoundScar) scarred++; }
        Debug.Log($"[Wound] node={node.id} r={node.woundRadius:F3} faceN={node.woundFaceNormal} grow={node.growDirection} term={node.isTerminal} | live={liveWounds} scars={scarred}");
    }

    /// <summary>
    /// Low-poly half-torus (outer half only, theta ∈ [0, π]).
    /// The ring lies in the XZ plane; the tube protrudes along +Y.
    /// </summary>
    Mesh BuildHalfTorusMesh(float R, float r, int phiSteps, int thetaSteps)
    {
        var verts = new List<Vector3>();
        var tris  = new List<int>();
        var uvs   = new List<Vector2>();

        int cols = phiSteps + 1;

        for (int j = 0; j <= thetaSteps; j++)
        {
            float theta = Mathf.PI * j / thetaSteps;
            float ct = Mathf.Cos(theta), st = Mathf.Sin(theta);
            for (int i = 0; i <= phiSteps; i++)
            {
                float phi = Mathf.PI * 2f * i / phiSteps;
                float cp = Mathf.Cos(phi), sp = Mathf.Sin(phi);
                verts.Add(new Vector3((R + r * ct) * cp, r * st, (R + r * ct) * sp));
                uvs.Add(new Vector2((float)i / phiSteps, (float)j / thetaSteps));
            }
        }

        for (int j = 0; j < thetaSteps; j++)
        {
            for (int i = 0; i < phiSteps; i++)
            {
                int a = j * cols + i, b = j * cols + i + 1;
                int c = (j + 1) * cols + i, d = (j + 1) * cols + i + 1;
                tris.Add(a); tris.Add(c); tris.Add(b);
                tris.Add(b); tris.Add(c); tris.Add(d);
            }
        }

        var mesh = new Mesh { name = "HalfTorus" };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.SetUVs(0, uvs);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// Applies wound sealing paste to a node, stopping the seasonal health drain.
    /// </summary>
    public void ApplyPaste(TreeNode node)
    {
        if (!node.hasWound || node.pasteApplied) return;
        TrainingRecorder.Instance?.RecordAction("Paste", node.id);
        node.pasteApplied = true;

        // Paste is now visualised via vertex.b in the unified tree mesh.
        // Trigger a rebuild so the new vertex data takes effect immediately.
        meshBuilder.SetDirty();
        Debug.Log($"[Wound] Paste applied node={node.id}");
    }

    /// <summary>
    /// Recursively updates children's worldPosition when a parent node is moved.
    /// </summary>
    public void PropagatePosition(TreeNode node)
    {
        foreach (var child in node.children)
        {
            if (IsGroundAnchoredRoot(child)) continue;
            child.worldPosition = node.tipPosition;
            PropagatePosition(child);
        }
    }
}
