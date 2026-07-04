using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.InputSystem;
using Debug = UnityEngine.Debug;
using Random = UnityEngine.Random;

/// <summary>
/// TreeSkeleton — Advisor partial. Branch promotion advisor and mesh-surface helper queries.
/// Split from the 6,373-line TreeSkeleton.cs (F5, 2026-07-03) with zero
/// behaviour change; all serialized fields remain in TreeSkeleton.cs.
/// </summary>
public partial class TreeSkeleton : MonoBehaviour
{
    // ── Branch Promotion Advisor ──────────────────────────────────────────────

    bool IsAncestorOf(TreeNode ancestor, TreeNode node)
    {
        var cur = node.parent;
        while (cur != null)
        {
            if (cur == ancestor) return true;
            cur = cur.parent;
        }
        return false;
    }

    /// <summary>
    /// Scores how much removing/reducing <paramref name="candidate"/> would benefit
    /// <paramref name="target"/>. Returns 0–1 (higher = remove first), or -1 if ineligible.
    /// </summary>
    public float PromotionScore(TreeNode candidate, TreeNode target)
    {
        if (candidate == target) return -1f;
        if (candidate.isRoot || candidate.isTrimmed || candidate.isDead) return -1f;
        if (IsAncestorOf(candidate, target)) return -1f;

        // Apical dominance: shallower nodes suppress deeper ones more.
        float depthFactor  = 1f - Mathf.Clamp01((candidate.depth - 1) / 5f);
        // Radius: thicker = more resource draw.
        float radiusFactor = Mathf.Clamp01(candidate.radius / 0.25f);
        // Vigor: high-vigor = dominant drain.
        float vigorFactor  = Mathf.Clamp01((candidate.branchVigor - 0.2f) / 1.8f);
        // Directional competition: growing toward the target's light cone.
        float dirFactor = 0f;
        Vector3 toTarget = target.tipPosition - candidate.worldPosition;
        if (toTarget.sqrMagnitude > 0.0001f)
            dirFactor = Mathf.Clamp01((Vector3.Dot(candidate.growDirection.normalized, toTarget.normalized) + 1f) * 0.5f);

        return Mathf.Clamp01(depthFactor * 0.35f + radiusFactor * 0.25f + vigorFactor * 0.20f + dirFactor * 0.20f);
    }

    /// <summary>Returns "Remove", "Trim back", or "Pinch" based on score and node type.</summary>
    public static string PromotionAction(TreeNode candidate, float score)
    {
        if (score > 0.65f) return "Remove";
        if (score > 0.35f) return candidate.isTerminal ? "Pinch" : "Trim back";
        return "Trim back";
    }

    /// <summary>Returns the ideal season string for the recommended action.</summary>
    public static string BestPromotionSeason(TreeNode candidate, string action) => action switch
    {
        "Remove"    => "Late Winter (Jan–Feb)",
        "Pinch"     => "Spring (Apr–May)",
        _           => "Summer (Jun–Jul)",
    };

    /// <summary>
    /// Restores the last trim if called within the undo window (default 5 seconds).
    /// Leaves are re-spawned fresh on the restored terminals — the fall animation plays
    /// on trim and new leaves pop back on undo.
    /// </summary>
    public void UndoLastTrim()
    {
        if (!CanUndo) return;
        var u = pendingUndo;
        pendingUndo = null;

        // Re-attach the subtree
        u.parent.children.Add(u.subtreeRoot);
        foreach (var n in u.subtreeNodes)
        {
            n.isTrimmed = false;
            allNodes.Add(n);
        }

        // Restore parent fields
        u.parent.isTrimCutPoint      = u.isTrimCutPoint;
        u.parent.trimCutDepth        = u.trimCutDepth;
        u.parent.regrowthSeasonCount = u.regrowthSeasonCount;
        u.parent.health              = u.health;

        // Destroy the wound object the trim created, then restore the pre-trim state
        if (woundObjects.TryGetValue(u.parent.id, out var wGo))
        {
            Destroy(wGo);
            woundObjects.Remove(u.parent.id);
        }
        u.parent.hasWound        = u.hasWound;
        u.parent.woundRadius     = u.woundRadius;
        u.parent.woundFaceNormal = u.woundFaceNormal;
        u.parent.woundAge        = u.woundAge;
        u.parent.pasteApplied    = u.pasteApplied;
        if (u.hasWound)
            CreateWoundObject(u.parent);

        // Restore ancestor back-bud flags
        foreach (var (node, wasStimulated) in u.ancestorStates)
            node.backBudStimulated = wasStimulated;

        // Re-spawn leaves on restored terminal branch nodes only during leaf season.
        // If undo fires in winter (time skipped past August while window was open),
        // skip the spawn — the nodes will get leaves naturally next spring.
        if (GameManager.SeasonalGrowthRate > 0f)
        {
            var leafManager = GetComponent<LeafManager>();
            if (leafManager != null)
            {
                var terminals = new List<TreeNode>();
                foreach (var n in u.subtreeNodes)
                    if (!n.isRoot && n.isTerminal) terminals.Add(n);
                leafManager.ForceSpawnLeaves(terminals);
            }
        }

        RecalculateRadii(root);
        meshBuilder.SetDirty();
        Debug.Log($"[Undo] Restored subtree root={u.subtreeRoot.id} nodes={u.subtreeNodes.Count}");
    }

    void RemoveSubtree(TreeNode node, List<TreeNode> removed)
    {
        foreach (var child in node.children)
            RemoveSubtree(child, removed);

        // Clean up any dormant bud object sitting at this node's tip.
        if (budObjects.TryGetValue(node.id, out var budGo))
        {
            Destroy(budGo);
            budObjects.Remove(node.id);
        }

        // Clean up any wound object on this node.
        if (woundObjects.TryGetValue(node.id, out var woundGo))
        {
            Destroy(woundGo);
            woundObjects.Remove(node.id);
        }

        // Clean up any air layer wrap on this node.
        for (int i = airLayers.Count - 1; i >= 0; i--)
        {
            if (airLayers[i].node == node)
            {
                if (airLayers[i].wrapObject != null) Destroy(airLayers[i].wrapObject);
                airLayers.RemoveAt(i);
            }
        }

        node.isTrimmed = true;
        allNodes.Remove(node);
        removed.Add(node);
    }

    // Wiring

    /// <summary>
    /// Auto-wires all unrimmed root nodes when the player confirms Ishitsuki orientation.
    /// Wires hold the current root direction (no bending); locked from removal until set.
    /// </summary>
    public void SpawnTrainingWires()
    {
        string rootAreaInfo = rootAreaTransform != null ? rootAreaTransform.position.ToString() : "NULL";
        Debug.Log("[SpawnWires] frame=" + Time.frameCount
                  + " | rockCollider=" + (rockCollider != null)
                  + " meshBuilder=" + (meshBuilder != null)
                  + " rootAreaTransform=" + (rootAreaTransform != null)
                  + "\n  rootAreaTransform.position=" + rootAreaInfo
                  + "\n  plantingSurfacePoint.y BEFORE=" + plantingSurfacePoint.y.ToString("F3")
                  + "\n  transform.position=" + transform.position);

        // Mark Ishitsuki mode permanently — this flag never goes null.
        isIshitsukiMode = true;

        // Lock in current world Y as the new rest position so the lift system
        // considers the tree already grounded here — no lowering animation.
        initY       = transform.position.y;
        currentLift = 0f;

        // Set soil surface Y from the root area transform so Ishitsuki root chains
        // stop at the actual visible tray/soil surface.
        // rootAreaTransform.position.y IS the visible soil — the rock may be partially
        // buried below it, so we must NOT use min(areaY, rockBase).
        {
            float areaY    = rootAreaTransform != null ? rootAreaTransform.position.y : plantingSurfacePoint.y;
            float rockBase = rockCollider      != null ? rockCollider.bounds.min.y     : areaY;
            Debug.Log($"[SpawnWires] year={GameManager.year} soilY: areaY(rootArea)={areaY:F3} rockBase={rockBase:F3} → using areaY={areaY:F3}");
            plantingSurfacePoint = new Vector3(plantingSurfacePoint.x, areaY, plantingSurfacePoint.z);
        }

        // Share rock collider with the mesh builder for gripping visuals.
        if (meshBuilder != null) meshBuilder.rockCollider = rockCollider;

        // ── Diagnostic snapshot ───────────────────────────────────────────────
        Vector3 treeWorldPos  = transform.position;
        Vector3 rockWorldPos  = rockCollider != null ? rockCollider.transform.position : Vector3.zero;
        Bounds  rockBounds    = rockCollider != null ? rockCollider.bounds : new Bounds();
        Vector3 rootAreaPos   = rootAreaTransform != null ? rootAreaTransform.position : Vector3.zero;
        Debug.Log($"[Ishitsuki] year={GameManager.year} WORLD POSITIONS:" +
                  $"\n  tree.position      = {treeWorldPos}" +
                  $"\n  rock.position      = {rockWorldPos}" +
                  $"\n  rock.bounds.min    = {rockBounds.min}" +
                  $"\n  rock.bounds.max    = {rockBounds.max}" +
                  $"\n  rock.bounds.center = {rockBounds.center}" +
                  $"\n  rootArea.position  = {rootAreaPos}" +
                  $"\n  soilY (after fix)  = {plantingSurfacePoint.y:F3}" +
                  $"\n  rockTopY           = {rockBounds.max.y:F3}" +
                  $"\n  rockBottomY        = {rockBounds.min.y:F3}" +
                  $"\n  rockHeightAboveSoil= {rockBounds.max.y - plantingSurfacePoint.y:F3}" +
                  $"\n  rockScale          = {(rockCollider != null ? rockCollider.transform.lossyScale.ToString() : "N/A")}");

        // Log trunk root starting positions before draping.
        if (root != null)
        {
            int ri = 0;
            foreach (var child in root.children)
            {
                if (!child.isRoot) continue;
                Vector3 wPos = transform.TransformPoint(child.worldPosition);
                Vector3 wTip = transform.TransformPoint(child.tipPosition);
                Debug.Log($"[Ishitsuki] year={GameManager.year} TrunkRoot[{ri}] depth={child.depth}" +
                          $" worldPos={wPos} tipWorld={wTip}" +
                          $" tipY={wTip.y:F3} soilY={plantingSurfacePoint.y:F3}" +
                          $" distAboveSoil={wTip.y - plantingSurfacePoint.y:F3}");
                ri++;
            }
        }
        // ── Game-view debug markers (GL lines — visible in Game View without Gizmos) ──
        {
            _soilDbgSoilY   = plantingSurfacePoint.y;
            _soilDbgRockTop = rockCollider != null ? rockCollider.bounds.max.y : _soilDbgSoilY + 2f;
            _soilDbgRockBot = rockCollider != null ? rockCollider.bounds.min.y : _soilDbgSoilY;
            _soilDbgCenter  = rockCollider != null ? rockCollider.bounds.center : transform.position;
            _soilDbgR       = rockCollider != null ? rockCollider.bounds.extents.magnitude * 1.1f : 1.5f;
            // _soilDbgActive  = true;
            _soilDbgEndTime = Time.realtimeSinceStartup + 60f;
        }
        // ─────────────────────────────────────────────────────────────────────

        // Clear existing trunk-root chains so PreGrowRootsToSoil builds fresh cables
        // from the correct trunk-tip positions instead of from the draped chain tips
        // (which can end up above the trunk after DrapeRootsOverRock adds an upward bias).
        // Use RemoveSubtree so orphaned nodes are also purged from allNodes — without
        // this, the old chain nodes stay in allNodes with isGrowing=true and show up
        // as ghost roots growing in mid-air above the rock.
        if (root != null)
        {
            foreach (var child in root.children)
            {
                if (!child.isRoot) continue;
                var toRemove = new List<TreeNode>(child.children);
                foreach (var oldChild in toRemove)
                    RemoveSubtree(oldChild, new List<TreeNode>());
                child.children.Clear();
            }
        }

        // Pre-grow root cables from the trunk base all the way to the soil.
        // In real Ishitsuki the roots are already established before rock placement —
        // what takes years is new thin roots filling in, not the original cables reaching soil.
        PreGrowRootsToSoil();

        meshBuilder.SetDirty();
        Debug.Log($"[Ishitsuki] year={GameManager.year} SpawnTrainingWires — initY={initY:F3} soilY={plantingSurfacePoint.y:F3}");
    }

    /// <summary>
    /// Walks all root nodes parent-first and snaps each one onto the rock surface,
    /// bending its growDirection to follow the surface tangent downward.
    /// Called once when the player confirms orientation.
    /// </summary>
    void DrapeRootsOverRock()
    {
        if (rockCollider == null || root == null) return;

        float snapRadius = rockCollider.bounds.extents.magnitude * 1.5f;
        int   snapped    = 0;

        var queue = new Queue<TreeNode>();
        foreach (var child in root.children)
            if (child.isRoot) queue.Enqueue(child);

        while (queue.Count > 0)
        {
            TreeNode node = queue.Dequeue();

            Vector3 worldPos = transform.TransformPoint(node.worldPosition);

            // Nodes inside the rock bounds can't use Physics.ClosestPoint reliably.
            // Shoot a ray from the trunk base in this root's own radial direction so
            // each root projects to a different surface point and they fan out naturally.
            Vector3 closestPt;
            bool    insideBounds = rockCollider.bounds.Contains(worldPos);
            if (insideBounds)
            {
                // Radial direction: horizontal XZ offset of this node from the trunk base.
                Vector3 localRadial = node.worldPosition - root.worldPosition;
                localRadial.y = 0f;
                if (localRadial.sqrMagnitude < 0.001f)
                    localRadial = new Vector3(node.growDirection.x, 0f, node.growDirection.z);
                Vector3 worldRadial   = transform.TransformDirection(localRadial).normalized;
                if (worldRadial.sqrMagnitude < 0.001f) worldRadial = Vector3.forward;
                Vector3 trunkWorldPos = transform.TransformPoint(root.worldPosition);
                if (rockCollider.Raycast(new Ray(trunkWorldPos, worldRadial),
                        out RaycastHit bHit, 10f))
                    closestPt = bHit.point;
                else
                    closestPt = Physics.ClosestPoint(worldPos, rockCollider,
                        rockCollider.transform.position, rockCollider.transform.rotation);
            }
            else
            {
                closestPt = Physics.ClosestPoint(worldPos, rockCollider,
                    rockCollider.transform.position, rockCollider.transform.rotation);
            }

            float dist = Vector3.Distance(worldPos, closestPt);

            if (insideBounds || dist < snapRadius)
            {
                // Surface normal via raycast from slightly outside.
                Vector3 outward = closestPt - rockCollider.bounds.center;
                if (outward.sqrMagnitude < 0.001f) outward = Vector3.up;
                outward.Normalize();
                Vector3 surfaceNormal = outward;
                if (rockCollider.Raycast(new Ray(closestPt + outward * 0.5f, -outward),
                        out RaycastHit hit, 1f))
                    surfaceNormal = hit.normal;

                // Move node base to surface with small clearance.
                node.worldPosition = transform.InverseTransformPoint(
                    closestPt + surfaceNormal * 0.025f);

                // Bend growDirection to surface tangent, biased downward.
                Vector3 worldDir = transform.TransformDirection(node.growDirection);
                Vector3 tangent  = Vector3.ProjectOnPlane(worldDir, surfaceNormal);
                if (tangent.sqrMagnitude < 0.001f)
                {
                    Vector3 radialOut = new Vector3(
                        worldPos.x - rockCollider.bounds.center.x, 0f,
                        worldPos.z - rockCollider.bounds.center.z).normalized;
                    tangent = Vector3.ProjectOnPlane(radialOut, surfaceNormal);
                }
                // Add gravity bias then re-project onto the surface plane so the
                // direction stays truly tangent — without this re-projection the
                // direction has an inward component and tipPosition dips into the rock,
                // forcing the next segment to angle sharply upward (the zigzag).
                // A small outward-normal offset keeps the tip just above the surface.
                tangent = Vector3.ProjectOnPlane(
                    (tangent.normalized + Vector3.down * 0.4f).normalized,
                    surfaceNormal);
                if (tangent.sqrMagnitude < 0.001f)
                    tangent = Vector3.ProjectOnPlane(Vector3.down, surfaceNormal);
                tangent = (tangent.normalized + surfaceNormal * 0.08f).normalized;
                node.growDirection = transform.InverseTransformDirection(tangent).normalized;
                snapped++;
            }

            foreach (var child in node.children)
                if (child.isRoot && !child.isTrimmed) queue.Enqueue(child);
        }

        Debug.Log($"[Ishitsuki] DrapeRootsOverRock — snapped {snapped} root nodes");
    }

    /// <summary>
    /// For each trunk root snapped to the rock surface, pre-spawns a fully-grown chain
    /// of nodes tracing the rock face down to the soil level.
    ///
    /// Real Ishitsuki roots are established BEFORE placement — the tree was trained to
    /// the rock over years before the player places it. Only the new fill-in roots that
    /// grow afterward should take a long time. The initial cables reach soil immediately.
    /// </summary>
    void PreGrowRootsToSoil(bool animated = false)
    {
        if (root == null) return;
        if (rockCollider == null)
        {
            Debug.LogWarning($"[PreGrow] year={GameManager.year} rockCollider is NULL (isIshitsukiMode={isIshitsukiMode}) — cannot drape roots.");
            return;
        }

        float soilY = debugSoilYOverride ? debugSoilY : plantingSurfacePoint.y;
        if (debugSoilYOverride && debugSoilY <= -9998f)
        {
            debugSoilY = plantingSurfacePoint.y;
            soilY      = debugSoilY;
        }

        float segLen     = rootSegmentLength * 0.5f;
        int   grown      = 0;
        float rockSearchR = rockCollider.bounds.extents.magnitude * 2.5f;

        var trunkRoots = new List<TreeNode>();
        foreach (var child in root.children)
            if (child.isRoot) trunkRoots.Add(child);

        Debug.Log($"[PreGrow] year={GameManager.year} soilY={soilY:F3} segLen={segLen:F3} trunkRoots={trunkRoots.Count}");

        // Prevent multiple strands from converging on the same rock-surface point.
        // Each strand claims its equatorial edge XZ; later strands rotate their scan
        // direction until they land at least minEdgeSep away from all claimed points.
        var  claimedEdges = new List<Vector3>();
        float minEdgeSep  = Mathf.Max(segLen * 1.5f, 0.04f);

        int strandIndex = 0;
        foreach (var startNode in trunkRoots)
        {
            TreeNode current     = startNode;
            int      strandGrown = 0;

            // ── Phase 1: fast-forward to the chain tip ────────────────────────────
            { int walkGuard = 0;
              while (current.children.Count > 0 && ++walkGuard < 5000)
                  current = current.children[0];
              if (walkGuard >= 5000) { Debug.LogError($"[PreGrow] Phase1 walk cycle detected on strand={strandIndex} — skipping"); strandIndex++; continue; }
            }

            // Player cut this cable (trim tool) — respect it, don't regrow the strand.
            if (current.isTrimCutPoint)
            {
                if (verboseLog) Debug.Log($"[PreGrow] strand={strandIndex} tip is a trim cut point — not regrowing");
                strandIndex++;
                continue;
            }

            Vector3 existingTip = transform.TransformPoint(current.tipPosition);
            if (existingTip.y <= soilY + 0.05f)
            {
                Debug.Log($"[PreGrow] year={GameManager.year} strand={strandIndex} tip already at soil tipY={existingTip.y:F3}");
                strandIndex++;
                continue;
            }

            // Remove only non-training-wire (accidentally air-grown) children.
            // Preserve existing training-wire cable segments so animated growth
            // continues from the real chain tip rather than restarting each spring.
            var nonWireChildren = startNode.children.FindAll(c => !c.isTrainingWire);
            foreach (var c in nonWireChildren)
                RemoveSubtree(c, new List<TreeNode>());
            startNode.children.RemoveAll(c => !c.isTrainingWire);

            // Re-walk to the actual chain tip after cleanup.
            current = startNode;
            { int walkGuard = 0;
              while (current.children.Count > 0 && ++walkGuard < 5000)
                  current = current.children[0];
              if (walkGuard >= 5000) { Debug.LogError($"[PreGrow] Re-walk cycle detected on strand={strandIndex} — skipping"); strandIndex++; continue; }
            }

            // ── Find entry point on rock face this strand is aimed at ─────────────
            // Each trunk root points in a different XZ direction from the trunk.
            // Cast from outside the rock bounds inward along that direction so every
            // strand starts on its own face — not all piling onto the nearest one.
            Vector3 startTip  = transform.TransformPoint(startNode.tipPosition);
            Vector3 strandDir = transform.TransformDirection(startNode.growDirection).normalized;
            Vector3 strandXZ  = new Vector3(strandDir.x, 0f, strandDir.z);
            if (strandXZ.sqrMagnitude < 0.001f)
                strandXZ = new Vector3(strandDir.x, strandDir.z, 0f);
            if (strandXZ.sqrMagnitude < 0.001f)
                strandXZ = Vector3.right;
            strandXZ = strandXZ.normalized;

            Vector3 rockCenter  = rockCollider.bounds.center;
            float   rockTopY    = rockCollider.bounds.max.y;
            float   surfOffset  = rootTerminalRadius * 2f; // float nodes one full diameter above surface

            // ── Entry point: two-step — find XZ edge, then scan down from above it ─────
            // Step 1: horizontal ray at rock center Y (the widest cross-section) finds the
            //         outermost XZ position of the rock in this strand's direction.
            //         This almost always hits, unlike a ray at startTip.y which can miss
            //         near the rock top where the mesh tapers.
            float   entryY    = Mathf.Max(startTip.y, rockTopY) + 0.5f;
            float   entryDist = rockCollider.bounds.size.y + 1.5f;

            // Find the equatorial edge XZ for this strand, retrying with small angular
            // offsets if the hit point is too close to an already-claimed one.
            Vector3 edgeXZ  = Vector3.zero;
            bool    edgeOk  = false;
            for (int retry = 0; retry <= 8 && !edgeOk; retry++)
            {
                float   retryA   = retry * (Mathf.PI / 9f); // ~20° steps
                float   cos      = Mathf.Cos(retryA);
                float   sin      = Mathf.Sin(retryA);
                // Rotate strandXZ left/right alternately: 0, +20°, -20°, +40°, -40°…
                float   sign     = (retry % 2 == 0) ? 1f : -1f;
                float   a        = retryA * sign;
                float   scanX    = strandXZ.x * Mathf.Cos(a) - strandXZ.z * Mathf.Sin(a);
                float   scanZ    = strandXZ.x * Mathf.Sin(a) + strandXZ.z * Mathf.Cos(a);
                Vector3 scanDir  = new Vector3(scanX, 0f, scanZ).normalized;

                Vector3 horizOrig = rockCenter + scanDir * rockSearchR;
                horizOrig.y       = rockCenter.y;
                Vector3 candidate;
                if (rockCollider.Raycast(new Ray(horizOrig, -scanDir), out RaycastHit edgeHit, rockSearchR * 2f))
                {
                    candidate = edgeHit.point;
                }
                else
                {
                    float projExtent = Mathf.Abs(scanDir.x) * rockCollider.bounds.extents.x
                                     + Mathf.Abs(scanDir.z) * rockCollider.bounds.extents.z;
                    candidate = new Vector3(rockCenter.x + scanDir.x * projExtent,
                                           rockCenter.y,
                                           rockCenter.z + scanDir.z * projExtent);
                }

                // Check XZ distance against all previously claimed edges.
                bool tooClose = false;
                foreach (var claimed in claimedEdges)
                {
                    float dx = candidate.x - claimed.x;
                    float dz = candidate.z - claimed.z;
                    if (dx * dx + dz * dz < minEdgeSep * minEdgeSep) { tooClose = true; break; }
                }

                if (!tooClose || retry == 8)
                {
                    edgeXZ  = candidate;
                    // If we rotated, also update strandXZ so step-loop raycasts stay consistent.
                    if (retry > 0) strandXZ = scanDir;
                    edgeOk  = true;
                }
            }
            claimedEdges.Add(edgeXZ);

            // Step 2: edgeXZ is the rock's outermost XZ in this strand's direction at
            //         its widest Y cross-section. Used as snap target when under-rock.
            //         baseWorld starts at the trunk-root tip — no entry scan landing, so
            //         there is no mesh gap between the trunk root and the first pre-grown node.
            // For animated continued growth, start from the actual chain tip so each
            // spring extends the cable one step further rather than restarting from startNode.
            Vector3 baseWorld      = transform.TransformPoint(current.tipPosition);
            bool    hasHitExterior = false;
            // Seed prevTangent from the last placed segment so the angle guard
            // produces smooth continuity across season boundaries.
            Vector3 prevTangent = (current != startNode)
                ? transform.TransformDirection(current.growDirection).normalized
                : strandDir;

            // ── Phase 2: step down the rock face to soil ──────────────────────────
            // Each step re-queries the rock exterior by shooting a horizontal ray from
            // outside the rock along the strand's fixed XZ direction at the new Y level.
            //
            // Before the first exterior hit: if the horizontal ray misses, check whether
            // we are still inside the rock with a downward ray. If so, snap XZ to edgeXZ
            // (the outer edge at the rock's widest section) so the chain exits the rock
            // instead of tunnelling through it.
            //
            // After the first exterior hit: a horizontal miss means we have descended past
            // the rock's lower surface — free-fall straight down to soil.
            for (int step = 0; step < 120; step++)
            {
                if (baseWorld.y <= soilY + 0.05f)
                {
                    Debug.Log($"[PreGrow] year={GameManager.year} strand={strandIndex} step={step} REACHED SOIL baseY={baseWorld.y:F3}");
                    break;
                }

                // Advance Y by one segment downward.
                float targetY = baseWorld.y - segLen;
                if (targetY < soilY) targetY = soilY;

                // Horizontal ray: from outside the rock inward along strandXZ at targetY.
                Vector3 scanOrig = rockCenter + strandXZ * rockSearchR;
                scanOrig.y       = targetY;
                bool hitRock     = rockCollider.Raycast(new Ray(scanOrig, -strandXZ), out RaycastHit hit, rockSearchR * 2f);

                Vector3 nodePos;
                Vector3 tangent;

                string stepMode;
                if (hitRock)
                {
                    // Exterior surface found — float one full root diameter above it.
                    nodePos         = hit.point + hit.normal * surfOffset;
                    tangent         = nodePos - baseWorld;
                    tangent         = tangent.sqrMagnitude > 0.001f ? tangent.normalized : Vector3.down;
                    stepMode        = "exterior";
                    hasHitExterior  = true;
                }
                else if (!hasHitExterior)
                {
                    // Haven't reached the exterior yet — check whether the trunk-root tip
                    // (or a prior node) is inside the rock by shooting down from above.
                    Vector3 checkOrig = new Vector3(baseWorld.x, rockTopY + 0.5f, baseWorld.z);
                    float   checkDist = rockTopY - soilY + 1f;
                    bool underRock    = rockCollider.Raycast(new Ray(checkOrig, Vector3.down), out RaycastHit checkHit, checkDist)
                                       && checkHit.point.y > targetY;
                    if (underRock)
                    {
                        // Snap XZ directly to the outer edge at this Y — jumps chain
                        // onto the rock exterior without drifting through the interior.
                        nodePos  = new Vector3(edgeXZ.x, targetY, edgeXZ.z);
                        tangent  = (nodePos - baseWorld).sqrMagnitude > 0.001f
                                   ? (nodePos - baseWorld).normalized
                                   : Vector3.down;
                        stepMode = "toEdge";
                    }
                    else
                    {
                        // We're above the rock and the ray just missed — free-fall.
                        nodePos  = new Vector3(baseWorld.x, targetY, baseWorld.z);
                        tangent  = Vector3.down;
                        stepMode = "freeFall";
                    }
                }
                else
                {
                    // Horizontal ray missed after tracking the exterior — the rock's side
                    // face has tapered away.  Before free-falling, try a downward ray from
                    // the outer edge (edgeXZ) to find the rock's lower curved surface.
                    // This lets the chain follow the lower hemisphere of a round rock
                    // instead of hanging in space at the equatorial XZ.
                    Vector3 downOrig = new Vector3(edgeXZ.x, baseWorld.y + 0.1f, edgeXZ.z);
                    float   downDist = baseWorld.y - soilY + 0.5f;
                    if (rockCollider.Raycast(new Ray(downOrig, Vector3.down), out RaycastHit lowerHit, downDist)
                        && lowerHit.point.y < baseWorld.y)
                    {
                        // Lower rock face found — float above it.
                        nodePos   = lowerHit.point + lowerHit.normal * surfOffset;
                        nodePos.y = Mathf.Clamp(nodePos.y, soilY, baseWorld.y - 0.001f);
                        tangent   = (nodePos - baseWorld).sqrMagnitude > 0.001f
                                    ? (nodePos - baseWorld).normalized
                                    : Vector3.down;
                        stepMode  = "lowerFace";
                    }
                    else
                    {
                        // Nothing found — rock is fully below us, drop straight to soil.
                        nodePos  = new Vector3(baseWorld.x, targetY, baseWorld.z);
                        tangent  = Vector3.down;
                        stepMode = "freeFall";
                    }
                }

                // ── Sharp-angle guard ────────────────────────────────────────────────
                // If the new segment would bend back toward the trunk at an angle sharper
                // than minCableAngleDeg, override to freeFall (drop straight down).
                // This removes the visible U-turn kinks on the upper rock face.
                if (stepMode != "freeFall")
                {
                    float bendAngle = Vector3.Angle(prevTangent, tangent);
                    if (bendAngle > (180f - minCableAngleDeg))
                    {
                        nodePos  = new Vector3(baseWorld.x, targetY, baseWorld.z);
                        tangent  = Vector3.down;
                        stepMode = "freeFall(angleGuard)";
                    }
                }
                prevTangent = tangent;

                if (verboseLog && (step == 0 || step % 5 == 0))
                    Debug.Log($"[PreGrow] year={GameManager.year} s={strandIndex} step={step}" +
                              $" targetY={targetY:F3} nodeY={nodePos.y:F3} mode={stepMode}");

                Vector3 localPos = transform.InverseTransformPoint(baseWorld);
                Vector3 localDir = transform.InverseTransformDirection(tangent).normalized;

                var newNode              = CreateNode(localPos, localDir, rootTerminalRadius, segLen, current);
                newNode.isRoot           = true;
                newNode.isTrainingWire   = true;   // exempt from rootVisibilityDepth cull in TreeMeshBuilder
                newNode.length           = animated ? 0f    : segLen;
                newNode.isGrowing        = animated;        // true = grows visually; false = frozen at full length
                newNode.radius           = animated ? 0f    : rootTerminalRadius;

                current = newNode;
                grown++;
                strandGrown++;

                // Advance to the new exterior surface point — NOT tangent * segLen.
                baseWorld = nodePos;

                // Animated mode: one segment per season. The next spring call picks up from here.
                if (animated) break;
            }

            // Prevent startNode from reaching targetLength in the Update growth loop and
            // firing SpawnChildren — which would append a second air-growing continuation
            // root alongside the pre-grown chain. Mark it fully grown but frozen.
            // Also mark isTrainingWire so the age loop doesn't skip it (age=0 = stays white forever).
            startNode.isTrainingWire = true;
            if (startNode.isGrowing)
            {
                startNode.length    = startNode.targetLength;
                startNode.isGrowing = false;
                startNode.radius    = rootTerminalRadius;
                startNode.minRadius = rootTerminalRadius;
            }

            Vector3 finalTip = transform.TransformPoint(current.tipPosition);
            Debug.Log($"[PreGrow] year={GameManager.year} strand={strandIndex} DONE grew={strandGrown} finalTipY={finalTip.y:F3} soilY={soilY:F3} aboveSoil={finalTip.y - soilY:F3}");
            strandIndex++;
        }

        // Rebuild radii FIRST so the cable nodes have their final thickness — the smoothing
        // pass below floats each node off the rock by its own radius, so it needs real radii.
        if (grown > 0)
            RecalculateRadii(root);

        // Round the out-then-down elbows on every cable so roots flow over the rock instead
        // of cornering, and push each node out by (radius + padding) so the tube sits OUTSIDE
        // the rock. Runs each call; the surface re-snap keeps it stable (already-smooth,
        // on-surface nodes barely move), so it doesn't over-smooth.
        if (rockCableSmoothIterations > 0)
            foreach (var startNode in trunkRoots)
                SmoothRockCableStrand(startNode);

        Debug.Log($"[Ishitsuki] year={GameManager.year} PreGrowRootsToSoil — spawned {grown} pre-grown nodes total");
    }

    /// <summary>
    /// Rounds the corners of one pre-grown rock-root cable (the training-wire chain hanging
    /// off <paramref name="startNode"/>). Endpoint-preserving weighted-Laplacian smoothing of
    /// the polyline vertices, with each moved vertex re-snapped onto the rock face so the
    /// cable stays hugging the rock instead of sinking in or floating off.
    ///
    /// Safe by construction: all smoothed positions are computed first, then written back in
    /// one pass with each segment's dir+length recomputed for continuity (tip[i] == base[i+1]).
    /// It never chains a node off its parent's tip mid-pass — the failure mode that cascades
    /// bad positions. The trunk anchor (vertex 0) and the soil-contact tip (last vertex) are
    /// fixed, so the cable still starts at the trunk and ends at the soil.
    /// </summary>
    void SmoothRockCableStrand(TreeNode startNode)
    {
        if (rockCollider == null || startNode == null) return;

        // Gather the linear cable: startNode, then the first training-wire root child each step.
        var chain = new List<TreeNode>();
        TreeNode n = startNode; int guard = 0;
        while (n != null && ++guard < 5000)
        {
            chain.Add(n);
            TreeNode next = null;
            foreach (var c in n.children)
                if (c.isRoot && c.isTrainingWire) { next = c; break; }
            n = next;
        }
        int count = chain.Count;
        if (count < 3) return;   // need at least one interior segment to round

        // Polyline vertices in LOCAL space: each node's base, then the final tip.
        int vCount = count + 1;
        var V = new Vector3[vCount];
        for (int i = 0; i < count; i++) V[i] = chain[i].worldPosition;  // local base
        V[count] = chain[count - 1].tipPosition;                        // local end tip

        float   soilY      = debugSoilYOverride ? debugSoilY : plantingSurfacePoint.y;
        float   reach      = rockCollider.bounds.extents.magnitude * 2f;
        Vector3 rockCenter = rockCollider.bounds.center;

        // ── Pass 1: Laplacian smoothing, interior re-snapped to the BARE rock surface ──
        // (thickness/padding offset is applied in pass 2 so the smoothing math works on the
        // surface itself). Record each vertex's surface contact point + outward normal.
        var Sworld = new Vector3[vCount];
        var Nworld = new Vector3[vCount];
        var onRock = new bool[vCount];

        for (int iter = 0; iter < rockCableSmoothIterations; iter++)
        {
            var np = (Vector3[])V.Clone();
            for (int i = 1; i < vCount - 1; i++)   // endpoints fixed
            {
                Vector3 s = V[i] * 0.5f + V[i - 1] * 0.25f + V[i + 1] * 0.25f;
                Vector3 w = transform.TransformPoint(s);
                onRock[i] = false;
                if (w.y > soilY + 0.02f && SnapToRock(w, rockCenter, reach, out Vector3 sp, out Vector3 sn))
                {
                    Sworld[i] = sp; Nworld[i] = sn; onRock[i] = true;
                    w = sp;
                }
                if (w.y < soilY) w.y = soilY;
                np[i] = transform.InverseTransformPoint(w);
            }
            V = np;
        }

        // ── Pass 2: concavity-aware outward offset ────────────────────────────────────
        // Push each on-rock vertex out by (radius + padding). On CONCAVE spans the straight
        // tube between two nodes would cut the bulge, so add extra: sample the real surface
        // along each segment's chord and measure how far it sticks out toward the root past
        // the chord (positive = concave). Convex spans measure <=0 and get no extra, so they
        // stay snug. This is exactly "detect concave, give it more padding" — measured, not guessed.
        var extra = new float[vCount];
        for (int i = 0; i < vCount - 1; i++)
        {
            if (!onRock[i] || !onRock[i + 1]) continue;
            Vector3 navg = (Nworld[i] + Nworld[i + 1]).normalized;
            if (navg.sqrMagnitude < 1e-4f) continue;
            float segExtra = 0f;
            for (int k = 1; k <= 5; k++)   // 5 samples along the chord catch deeper concavities
            {
                Vector3 chordPt = Vector3.Lerp(Sworld[i], Sworld[i + 1], k / 6f);
                if (rockCollider.Raycast(new Ray(chordPt + navg * reach, -navg), out RaycastHit mh, reach * 2f))
                {
                    float bulge = Vector3.Dot(mh.point - chordPt, navg);  // >0 = surface toward the root
                    if (bulge > segExtra) segExtra = bulge;
                }
            }
            segExtra *= 1.2f;   // safety margin for the deepest point between samples
            if (segExtra > extra[i])     extra[i]     = segExtra;
            if (segExtra > extra[i + 1]) extra[i + 1] = segExtra;
        }

        for (int i = 1; i < vCount - 1; i++)
        {
            if (!onRock[i]) continue;
            float r = Mathf.Max(chain[Mathf.Min(i, count - 1)].radius, rootTerminalRadius);
            Vector3 w = Sworld[i] + Nworld[i] * (r + rockRootSurfacePadding + extra[i]);

            // Draping roots only descend — never let the outward offset (which can lift a node
            // along an up-facing facet's normal) push it above its parent. Kills the occasional
            // segment that juts upward off the rock crown. V[i-1] is already finalised here.
            float prevY = transform.TransformPoint(V[i - 1]).y;
            if (w.y > prevY + 0.01f) w.y = prevY + 0.01f;

            V[i] = transform.InverseTransformPoint(w);
        }

        // Write back: positions, then per-segment dir/length for continuity. The last node
        // keeps its own dir/length when the final segment is ~zero (an actively-growing tip).
        for (int i = 0; i < count; i++)
        {
            chain[i].worldPosition = V[i];
            Vector3 d = V[i + 1] - V[i];
            float len = d.magnitude;
            if (len > 1e-5f)
            {
                chain[i].growDirection = d / len;
                chain[i].length        = len;
            }
        }
    }

    /// <summary>Nearest rock surface point + outward normal, by casting from outside the rock
    /// inward along the radial from the bounds centre. Works on non-convex mesh colliders
    /// (unlike Physics.ClosestPoint). Returns false if the ray misses (past the rock edge).</summary>
    bool SnapToRock(Vector3 worldPos, Vector3 rockCenter, float reach, out Vector3 surfPt, out Vector3 surfNormal)
    {
        Vector3 outwardN = worldPos - rockCenter;
        if (outwardN.sqrMagnitude < 1e-4f) outwardN = Vector3.up;
        outwardN.Normalize();
        if (rockCollider.Raycast(new Ray(worldPos + outwardN * reach, -outwardN), out RaycastHit hit, reach * 2f))
        {
            surfPt = hit.point; surfNormal = hit.normal;
            return true;
        }
        surfPt = worldPos; surfNormal = outwardN;
        return false;
    }

    /// <summary>
    /// Attaches a wire to a node.
    /// </summary>
    public void WireNode(TreeNode node, Vector3 targetDirectionLocal)
    {
        TrainingRecorder.Instance?.RecordAction("Wire", node != null ? node.id : -1);
        if (node.hasWire && node.wireSetProgress > 0f)
        {
            float damage = Mathf.Lerp(0.05f, 0.25f, node.wireSetProgress);
            ApplyDamage(node, DamageType.WireBend, damage);
        }

        node.wireOriginalDirection = node.growDirection;
        node.hasWire               = true;
        node.wireTargetDirection   = targetDirectionLocal.normalized;
        node.wireSetProgress       = 0f;
        node.wireDamageProgress    = 0f;
        node.wireAgeDays           = 0f;
        node.wireSetSpeedMult      = 1f;   // player-speed default; AutoStyler overrides after calling this
    }

    // ── Mesh surface helpers ──────────────────────────────────────────────────

    struct RockSurfaceHit { public Vector3 point, normal; public float dist; }

    /// <summary>
    /// Finds the closest point on any triangle of the mesh to <paramref name="worldPos"/>.
    /// Works for any shape — convex, concave, overhang — from any position.
    /// Uses interpolated per-vertex normals (barycentric) to avoid zero-normal on shared vertices.
    /// </summary>
    static RockSurfaceHit ClosestPointOnMesh(
        Vector3   worldPos,
        Vector3[] verts,
        int[]     tris,
        Vector3[] normals,   // mesh.normals — per-vertex, pre-computed by Unity
        Transform xform)
    {
        Vector3 local   = xform.InverseTransformPoint(worldPos);
        float   minSq   = float.MaxValue;
        Vector3 bestPt  = local;
        int     bestI   = 0;

        for (int i = 0; i < tris.Length; i += 3)
        {
            Vector3 a = verts[tris[i]];
            Vector3 b = verts[tris[i + 1]];
            Vector3 c = verts[tris[i + 2]];

            Vector3 cp = ClosestPointOnTriangle(local, a, b, c);
            float   sq = (cp - local).sqrMagnitude;
            if (sq >= minSq) continue;

            minSq  = sq;
            bestPt = cp;
            bestI  = i;
        }

        // Interpolate surface normal at bestPt using barycentric coords + per-vertex normals.
        // This never produces zero even when bestPt is exactly on a shared vertex,
        // because per-vertex normals are averaged over all adjacent faces by Unity.
        Vector3 bestN = Vector3.up;
        if (normals != null && normals.Length > 0)
        {
            int     iA = tris[bestI], iB = tris[bestI + 1], iC = tris[bestI + 2];
            Vector3 a  = verts[iA],   b  = verts[iB],       c  = verts[iC];

            // Compute barycentric coords of bestPt on triangle (a, b, c).
            // bestPt = a*(1-u-v) + b*v + c*u  →  wA=1-u-v, wB=v, wC=u
            Vector3 v0 = c - a, v1 = b - a, v2 = bestPt - a;
            float d00 = Vector3.Dot(v0, v0), d01 = Vector3.Dot(v0, v1), d02 = Vector3.Dot(v0, v2);
            float d11 = Vector3.Dot(v1, v1), d12 = Vector3.Dot(v1, v2);
            float denom = d00 * d11 - d01 * d01;

            Vector3 interpolated;
            if (Mathf.Abs(denom) < 1e-10f)
            {
                // Degenerate (zero-area) triangle — fall back to cross product
                interpolated = Vector3.Cross(b - a, c - a);
            }
            else
            {
                float invD = 1f / denom;
                float wC   = (d11 * d02 - d01 * d12) * invD;   // weight for vertex C
                float wB   = (d00 * d12 - d01 * d02) * invD;   // weight for vertex B
                float wA   = 1f - wB - wC;                     // weight for vertex A
                // Clamp to [0,1] to handle floating-point noise at triangle edges
                wA = Mathf.Clamp01(wA); wB = Mathf.Clamp01(wB); wC = Mathf.Clamp01(wC);
                float sum = wA + wB + wC;
                if (sum > 1e-6f) { wA /= sum; wB /= sum; wC /= sum; }
                interpolated = normals[iA] * wA + normals[iB] * wB + normals[iC] * wC;
            }

            if (interpolated.sqrMagnitude > 1e-6f)
                bestN = interpolated.normalized;
            // else bestN stays Vector3.up (safe fallback)
        }

        return new RockSurfaceHit
        {
            point  = xform.TransformPoint(bestPt),
            normal = xform.TransformDirection(bestN),
            dist   = Mathf.Sqrt(minSq)
        };
    }

    /// <summary>
    /// Returns the closest point on triangle (a,b,c) to point p.
    /// All in the same local space. Standard Ericson/Christer method.
    /// </summary>
    static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a, ac = c - a, ap = p - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f) return a;

        Vector3 bp = p - b;
        float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3) return b;

        Vector3 cp = p - c;
        float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6) return c;

        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            return a + ab * (d1 / (d1 - d3));

        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            return a + ac * (d2 / (d2 - d6));

        float va = d3 * d6 - d5 * d4;
        if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
            return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));

        float denom = 1f / (va + vb + vc);
        return a + ab * (vb * denom) + ac * (vc * denom);
    }

    /// <summary>
    /// Collects the contiguous wire run that includes <paramref name="start"/>.
    /// Walks up to the highest wired ancestor, then follows the most direction-aligned
    /// wired child at each fork (leaving other branches' wires in place).
    /// </summary>
    public List<TreeNode> CollectWireRun(TreeNode start)
    {
        // Walk up to the top of the run
        TreeNode top = start;
        while (top.parent != null && top.parent.hasWire)
            top = top.parent;

        // Walk down following the best-aligned wired child at each fork
        var run = new List<TreeNode>();
        TreeNode cur = top;
        while (cur != null && cur.hasWire)
        {
            run.Add(cur);
            cur = WireRunChild(cur);
        }
        return run;
    }

    TreeNode WireRunChild(TreeNode node)
    {
        TreeNode best    = null;
        float    bestDot = -2f;
        foreach (var child in node.children)
        {
            if (!child.hasWire) continue;
            float dot = Vector3.Dot(node.growDirection, child.growDirection);
            if (dot > bestDot) { bestDot = dot; best = child; }
        }
        return best;
    }

    /// <summary>
    /// Smart unwire: if every node in the wire run is fully set, removes them all.
    /// Otherwise falls back to unwiring only the clicked node.
    /// </summary>
    public void UnwireRun(TreeNode node)
    {
        var run = CollectWireRun(node);

        bool allSet = true;
        foreach (var n in run)
            if (n.wireSetProgress < 1f) { allSet = false; break; }

        if (allSet && run.Count > 1)
        {
            foreach (var n in run)
                UnwireNode(n);
            Debug.Log($"[Wire] UnwireRun removed count={run.Count}");
        }
        else
        {
            UnwireNode(node);
        }
    }

    /// <summary>
    /// Removes the wire. If not fully set, the branch springs back partially.
    /// </summary>
    public void UnwireNode(TreeNode node)
    {
        TrainingRecorder.Instance?.RecordAction("Unwire", node != null ? node.id : -1);
        if (node.wireSetProgress < 1f)
        {
            Vector3 prevDir    = node.growDirection;
            node.growDirection = Vector3.Slerp(
                node.wireOriginalDirection,
                node.wireTargetDirection,
                node.wireSetProgress).normalized;

            Quaternion springBackRot = Quaternion.FromToRotation(prevDir, node.growDirection);
            RotateAndPropagateDescendants(node, springBackRot, null);
        }

        node.hasWire            = false;
        node.wireSetProgress    = 0f;
        node.wireDamageProgress = 0f;
        node.wireAgeDays        = 0f;
        meshBuilder.SetDirty();
    }

    /// <summary>
    /// Rotates every descendant's growDirection by rot and propagates their worldPositions.
    /// </summary>
    public void RotateAndPropagateDescendants(
        TreeNode node, Quaternion rot,
        System.Collections.Generic.Dictionary<TreeNode, Vector3> originalDirs)
    {
        foreach (var child in node.children)
        {
            if (IsGroundAnchoredRoot(child)) continue;

            if (originalDirs != null && originalDirs.TryGetValue(child, out var origDir))
                child.growDirection = (rot * origDir).normalized;
            else
                child.growDirection = (rot * child.growDirection).normalized;

            child.worldPosition = node.tipPosition;
            RotateAndPropagateDescendants(child, rot, originalDirs);
        }
    }

    /// <summary>
    /// Reduces a node's health by amount, clamped to 0.
    /// </summary>
    public void ApplyDamage(TreeNode node, DamageType type, float amount)
    {
        node.health = Mathf.Max(0f, node.health - amount);
    }

}
