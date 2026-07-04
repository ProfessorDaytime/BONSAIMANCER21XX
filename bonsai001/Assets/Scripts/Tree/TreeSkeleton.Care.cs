using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.InputSystem;
using Debug = UnityEngine.Debug;
using Random = UnityEngine.Random;

/// <summary>
/// TreeSkeleton — Care partial. Player + AutoStyler actions: air layering, rock work, watering,
/// trim/pinch/wire/unwire, jin, trim-undo data.
/// Split from the 6,373-line TreeSkeleton.cs (F5, 2026-07-03) with zero
/// behaviour change; all serialized fields remain in TreeSkeleton.cs.
/// </summary>
public partial class TreeSkeleton : MonoBehaviour
{
    // ── Air Layering ──────────────────────────────────────────────────────────

    /// <summary>
    /// Places an air layer on a trunk node. The wrap is positioned at the node's
    /// tip and tracked in <see cref="airLayers"/>. After <see cref="airLayerSeasonsToRoot"/>
    /// growing seasons <see cref="AirLayerData.rootsSpawned"/> becomes true;
    /// the player then clicks again to call <see cref="UnwrapAirLayer"/>.
    /// </summary>
    public void PlaceAirLayer(TreeNode node)
    {
        if (node == null || node.isRoot) return;

        // Disallow duplicate layers on the same node.
        foreach (var l in airLayers)
            if (l.node == node) return;

        var layer = new AirLayerData { node = node };

        if (airLayerWrapPrefab != null)
        {
            layer.wrapObject = Instantiate(airLayerWrapPrefab, transform);
        }
        else
        {
            // Placeholder: teal cylinder wrapping the branch at the layer site.
            layer.wrapObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            layer.wrapObject.transform.SetParent(transform, false);
            var mat = new Material(Shader.Find("Standard"));
            mat.color = new Color(0f, 0.75f, 0.75f);
            layer.wrapObject.GetComponent<Renderer>().material = mat;
        }

        SetAirLayerWrapTransform(layer);
        airLayers.Add(layer);
        Debug.Log($"[AirLayer] PlaceAirLayer node={node.id} depth={node.depth}");
    }

    /// <summary>
    /// Spawns air-layer roots radially from the layer node and removes the wrap.
    /// Only call when <see cref="AirLayerData.rootsSpawned"/> is true.
    /// </summary>
    public void UnwrapAirLayer(AirLayerData layer)
    {
        if (layer == null)          { Debug.LogWarning("[AirLayer] UnwrapAirLayer called with null layer"); return; }
        if (!layer.rootsSpawned)    { Debug.LogWarning("[AirLayer] UnwrapAirLayer called but rootsSpawned=false"); return; }

        float spawnRadius = Mathf.Max(layer.node.radius * airLayerRootRadiusMultiplier, terminalRadius);
        float spawnLength = Mathf.Max(airLayerRootTargetLength, 0.1f);

        Debug.Log($"[AirLayer] UnwrapAirLayer firing — node={layer.node.id} spawnRadius={spawnRadius:F4} spawnLength={spawnLength:F3} segments={airLayerRootSegments} nodeRadius={layer.node.radius:F4}");

        float angleStep = 360f / airLayerRootCount;
        for (int i = 0; i < airLayerRootCount; i++)
        {
            float   angle  = i * angleStep * Mathf.Deg2Rad;
            Vector3 radial = new Vector3(Mathf.Cos(angle), -0.15f, Mathf.Sin(angle)).normalized;

            // Spawn a chain of segments per strand, each a child of the previous.
            // Start on the trunk's cylindrical surface (not its center axis) so the
            // first segment doesn't have to travel through the bark to become visible.
            Vector3  radialXZ = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            TreeNode prev     = layer.node;
            Vector3  prevTip  = layer.node.tipPosition + radialXZ * layer.node.radius;
            float    segRadius = spawnRadius;
            for (int s = 0; s < airLayerRootSegments; s++)
            {
                // Slightly vary direction each segment so strands curve naturally.
                Vector3 segDir = (radial + Random.insideUnitSphere * 0.15f).normalized;
                var seg = CreateNode(prevTip, segDir, segRadius, spawnLength, prev);
                seg.isRoot         = true;
                seg.isAirLayerRoot = true;
                seg.radius         = segRadius;
                seg.minRadius      = segRadius;
                seg.length         = spawnLength * 0.4f;   // start short so they visibly grow
                prev    = seg;
                prevTip = seg.tipPosition;
                segRadius *= 0.8f;                  // taper along the strand
            }
        }

        if (layer.wrapObject != null)
        {
            Destroy(layer.wrapObject);
            layer.wrapObject = null;
        }

        // Keep layer in airLayers so UpdateAirLayers() can track post-unwrap
        // root growth seasons and set isSeverable. SeverAirLayer removes it.
        layer.isUnwrapped = true;
        RecalculateRadii(root);
        meshBuilder.SetDirty();
        Debug.Log($"[AirLayer] UnwrapAirLayer done — spawned {airLayerRootCount} air-layer roots");
    }

    /// <summary>
    /// Positions and scales the wrap cylinder to match the current node radius and direction.
    /// Unity's Cylinder primitive is 2 units tall, 1 unit diameter at scale (1,1,1).
    /// We orient the cylinder's Y-axis along growDirection and scale it to sit snugly
    /// outside the branch surface.
    /// </summary>
    void SetAirLayerWrapTransform(AirLayerData layer)
    {
        if (layer.wrapObject == null) return;
        var node = layer.node;
        float wrapRadius = Mathf.Max(node.radius * 4f, 0.04f);
        float wrapHeight = Mathf.Max(node.radius * 4f, 0.04f);
        // Center the band along the segment.
        layer.wrapObject.transform.localPosition = node.worldPosition + node.growDirection * (node.length * 0.5f);
        layer.wrapObject.transform.localRotation = Quaternion.FromToRotation(Vector3.up, node.growDirection);
        // Cylinder: diameter = scale.x, height = scale.y * 2
        layer.wrapObject.transform.localScale    = new Vector3(wrapRadius * 2f, wrapHeight * 0.5f, wrapRadius * 2f);
    }

    /// <summary>
    /// Advances all active air layers by one growing season.
    /// Called from <see cref="StartNewGrowingSeason"/>.
    /// </summary>
    void UpdateAirLayers()
    {
        foreach (var layer in airLayers)
        {
            // Post-unwrap: track root growth seasons toward sever readiness.
            if (layer.isUnwrapped)
            {
                if (!layer.isSeverable)
                {
                    layer.rootGrowSeasons++;
                    if (layer.rootGrowSeasons >= airLayerRootSeasonsToSever)
                    {
                        layer.isSeverable = true;
                        Debug.Log($"[AirLayer] Layer node={layer.node.id} is now severable after {layer.rootGrowSeasons} seasons of root growth.");
                    }
                }
                continue;
            }

            if (layer.rootsSpawned) continue;

            layer.seasonsElapsed++;

            // Keep wrap sized and positioned as the trunk thickens.
            SetAirLayerWrapTransform(layer);

            if (layer.seasonsElapsed >= airLayerSeasonsToRoot)
            {
                layer.rootsSpawned = true;
                // Turn the wrap gold to signal roots are ready to emerge.
                if (layer.wrapObject != null)
                {
                    var rend = layer.wrapObject.GetComponent<Renderer>();
                    if (rend != null) rend.material.color = new Color(0.85f, 0.65f, 0.0f);
                }
                Debug.Log($"[AirLayer] Roots ready — node={layer.node.id} after {layer.seasonsElapsed} seasons. Click the gold wrap to unwrap.");
            }
        }
    }

    /// <summary>True if any unwrapped air layer has grown enough roots to sever.</summary>
    public bool HasSeverableLayer => airLayers.Exists(l => l.isSeverable);

    /// <summary>Returns the first severable layer, or null.</summary>
    public AirLayerData GetFirstSeverableLayer() => airLayers.Find(l => l.isSeverable);

    /// <summary>
    /// Severs the air-layered branch from the trunk, saving the original tree as a backup,
    /// then loads the severed branch + its air-layer roots as the new current tree.
    /// </summary>
    public void SeverAirLayer(AirLayerData layer)
    {
        if (layer == null || !layer.isSeverable)
        {
            Debug.LogWarning("[AirLayer] SeverAirLayer called with null or non-severable layer.");
            return;
        }

        var leafMgr = GetComponent<LeafManager>();

        // ── 1. Wound the cut site on the original tree ────────────────────
        var cutSite = layer.node.parent;
        if (cutSite != null)
        {
            cutSite.hasWound        = true;
            cutSite.hadWoundScar    = true;
            cutSite.woundRadius     = layer.node.radius;
            cutSite.woundFaceNormal = layer.node.growDirection;
            cutSite.woundAge        = 0f;
            cutSite.pasteApplied    = false;
        }

        // ── 2. Detach the severed subtree from the original ───────────────
        layer.node.parent?.children.Remove(layer.node);
        var removed = new List<TreeNode>();
        RemoveSubtree(layer.node, removed);
        RecalculateRadii(root);
        meshBuilder.SetDirty();
        Debug.Log($"[AirLayer] Severed — removed {removed.Count} nodes from original tree, wound applied at node={cutSite?.id}");

        // ── 3. Save modified original (with wound, without severed branch) ─
        SaveManager.SaveOriginal(this, leafMgr);

        // ── 4. Build and load the new tree from the severed subtree ──────
        var newData = BuildSeveredTreeSaveData(layer, removed);
        airLayers.Remove(layer);
        LoadFromSaveData(newData, leafMgr);
        treeOrigin = TreeOrigin.AirLayer;
        SaveManager.ActiveSlotId = null;   // new tree — no slot yet, prompt on first save

        GameManager.Instance.UpdateGameState(GameState.Idle);
        Debug.Log("[AirLayer] New tree loaded from severed air layer.");
    }

    SaveData BuildSeveredTreeSaveData(AirLayerData layer, List<TreeNode> subtreeNodes)
    {
        // Compute new depths with the air layer node as root (depth 0).
        var depthMap = new Dictionary<int, int>();
        AssignSubtreeDepths(layer.node, 0, depthMap);

        var potSoil = GetComponent<PotSoil>();

        var newData = new SaveData
        {
            year      = GameManager.year,
            month     = GameManager.month,
            day       = GameManager.day,
            hour      = GameManager.hour,
            waterings = 0,

            treeEnergy             = treeEnergy,
            soilMoisture           = 0.5f,
            droughtDaysAccumulated = 0f,
            nutrientReserve        = 1f,

            // Keep the same planting surface — air layer roots have already grown to it.
            planNX = plantingNormal.x,  planNY = plantingNormal.y,  planNZ = plantingNormal.z,
            planPX = plantingSurfacePoint.x, planPY = plantingSurfacePoint.y, planPZ = plantingSurfacePoint.z,

            startYear       = GameManager.year,
            startMonth      = GameManager.month,
            lastGrownYear   = lastGrownYear,
            isIshitsukiMode = false,

            // Fresh classic soil for the new pot.
            soilAkadama   = potSoil?.akadama   ?? 0.5f,
            soilPumice    = potSoil?.pumice    ?? 0.3f,
            soilLavaRock  = potSoil?.lavaRock  ?? 0.2f,
            soilPreset    = (int)PotSoil.SoilPreset.ClassicBonsai,
            soilDegradation = 0f,
            soilSaturation  = 0.5f,
            soilSeasonsSinceRepot = 0,
        };

        foreach (var node in subtreeNodes)
        {
            var sn = SaveManager.SerializeNode(node);
            // New root has no parent.
            if (node == layer.node) sn.parentId = -1;
            // Recalculate depths from the new root.
            if (depthMap.TryGetValue(node.id, out int d)) sn.depth = d;
            // Air-layer roots become normal roots now that they're in their own pot.
            if (node.isAirLayerRoot) { sn.isAirLayerRoot = false; }
            // Clear training-wire flag — not applicable in new tree.
            sn.isTrainingWire = false;
            newData.nodes.Add(sn);
        }

        return newData;
    }

    void AssignSubtreeDepths(TreeNode node, int depth, Dictionary<int, int> map)
    {
        map[node.id] = depth;
        foreach (var child in node.children)
            AssignSubtreeDepths(child, depth + 1, map);
    }

    /// <summary>
    /// Sets the surface the tree is resting on and lowers the tree onto it.
    /// Called by TreeInteraction when the player right-clicks a surface in RootPrune mode.
    /// The tree's resting Y is updated to the surface contact point, and subsequent root
    /// growth near the surface will hug it.
    /// </summary>
    public void SetPlantingSurface(Vector3 worldSurfacePoint, Vector3 worldSurfaceNormal)
    {
        plantingSurfacePoint = worldSurfacePoint;
        plantingNormal       = worldSurfaceNormal.normalized;

        // Update the resting Y so the tree lowers to this surface height.
        initY = worldSurfacePoint.y;

        // Lower the tree onto the surface.
        liftTarget = 0f;

        Debug.Log($"[Root] SetPlantingSurface point={worldSurfacePoint} normal={worldSurfaceNormal}");
    }

    // Node Factory

    public TreeNode CreateNode(Vector3 position, Vector3 direction, float radius, float targetLength, TreeNode parent)
    {
        int depth = parent == null ? 0 : parent.depth + 1;
        var node = new TreeNode(nextId++, depth, position, direction, radius, targetLength, parent)
        {
            birthYear        = GameManager.year,
            refinementLevel  = parent?.refinementLevel ?? 0f,
            branchVigor      = parent?.branchVigor ?? 1f,
        };

        // New nodes start thin and ramp toward their target radius as they grow,
        // distributing trunk thickening across the season instead of spiking at spawn.
        // Parent == null is the tree root node itself; it starts visible immediately.
        if (parent != null)
            node.radius = 0f;

        if (parent != null)
            parent.children.Add(node);

        allNodes.Add(node);
        return node;
    }

    // Branching

    void SpawnChildren(TreeNode node)
    {
        // Branches never grow through the soil: a non-root tip inside the substrate volume
        // (the rootArea box) spawns nothing. Drooping below the pot rim OUTSIDE the box is
        // allowed (cascade styles); extending INTO the soil is not. The 0.45 top margin keeps
        // the trunk base, which sits exactly on the surface, out of the check.
        if (!node.isRoot && rootAreaTransform != null)
        {
            Vector3 tipWorld = transform.TransformPoint(node.tipPosition);
            Vector3 boxLocal = rootAreaTransform.InverseTransformPoint(tipWorld);
            bool insideSoil = Mathf.Abs(boxLocal.x) <= 0.5f && Mathf.Abs(boxLocal.z) <= 0.5f &&
                              boxLocal.y < 0.45f && boxLocal.y >= -0.5f;
            if (insideSoil) return;
        }

        // Trunk elongation: depth-0 non-root nodes keep adding depth-0 segments
        // until we reach trunkSubdivisions. Only then does real branching begin.
        // This gives the player several independently-wireable trunk segments.
        if (!node.isRoot && node.depth == 0)
        {
            int trunkCount = 0;
            foreach (var n in allNodes)
                if (!n.isRoot && n.depth == 0) trunkCount++;

            if (trunkCount < trunkSubdivisions)
            {
                float segLen   = branchSegmentLength / trunkSubdivisions;
                var trunkSeg = new TreeNode(nextId++, 0, node.tipPosition,
                                             ContinuationDirection(node),
                                             terminalRadius, segLen, node)
                {
                    birthYear  = GameManager.year,
                    isGrowing  = true,
                };
                node.children.Add(trunkSeg);
                allNodes.Add(trunkSeg);
                if (verboseLog) Debug.Log($"[Tree] Trunk segment {trunkCount + 1}/{trunkSubdivisions} id={trunkSeg.id}");
                return;
            }
            // All trunk segments grown -- fall through to first real branch
        }

        // Branch subdivision: non-root nodes grow N same-depth sub-segments before branching.
        // Same depth as parent means sub-segments don't consume the season depth cap.
        if (!node.isRoot && node.subdivisionsLeft > 0)
        {
            // Clamp inherited targetLength to maxSegmentLength so any elongation that
            // snuck through doesn't cascade down the rest of the chain.
            float chainSegLen = maxSegmentLength > 0f
                ? Mathf.Min(node.targetLength, maxSegmentLength)
                : node.targetLength;
            var sub = new TreeNode(nextId++, node.depth, node.tipPosition,
                                   ContinuationDirection(node), terminalRadius, chainSegLen, node)
            {
                birthYear        = GameManager.year,
                subdivisionsLeft = node.subdivisionsLeft - 1,
                isGrowing        = true,
            };
            node.children.Add(sub);
            allNodes.Add(sub);
            return;
        }

        float baseSegLen  = node.isRoot ? rootSegmentLength : branchSegmentLength;
        float decay       = node.isRoot ? rootSegmentLengthDecay : segmentLengthDecay;
        // globalSegmentScale applies to branch chords here exactly as it already does in the
        // back-bud and old-wood spawn paths — previously only those paths scaled, so normal
        // chords came out ~7× longer than back-bud shoots and the year-1 tree grew as a pole
        // several times taller than the pot.
        float childLength = baseSegLen * Mathf.Pow(decay, node.depth + 1);
        if (!node.isRoot) childLength *= globalSegmentScale;

        // Each new branch chord is divided into however many segments are needed so
        // neither the fixed branchSubdivisions count nor maxSegmentLength is exceeded.
        int   nodeSubdivs = !node.isRoot ? SubdivsForChord(childLength) : 1;
        float segLength   = (nodeSubdivs > 1) ? childLength / nodeSubdivs : childLength;
        segLength        = Mathf.Max(segLength, node.isRoot ? 0.025f : minSegmentLength);

        float nodeRadius = (node.isRoot ? rootTerminalRadius : terminalRadius) * globalSegmentScale;

        // Root soft spread cap: laterals scale to zero at the target radius;
        // continuation itself stops beyond 1.3× the target radius.
        // Hard node cap enforced here as well as in StartNewGrowingSeason.
        if (node.isRoot)
        {
            int rootCount = 0;
            foreach (var n in allNodes) if (n.isRoot) rootCount++;
            if (rootCount >= maxTotalRootNodes) return;

            float distRatio   = RootDistRatio(node);
            bool  isIshitsuki = isIshitsukiMode;
            if (!isIshitsuki && distRatio >= 1.3f) return;  // hard outer boundary — no further growth

            // Hard spawn clamp: if the tip is already outside the box (side or bottom),
            // don't plant new children at all.  Top-face emergence is allowed.
            if (!isIshitsuki && rootAreaTransform != null)
            {
                Vector3 tipW  = transform.TransformPoint(node.tipPosition);
                Vector3 local = rootAreaTransform.InverseTransformPoint(tipW);
                bool outsideSide   = Mathf.Abs(local.x) > 0.5f || Mathf.Abs(local.z) > 0.5f;
                bool outsideBottom = local.y < -0.5f;
                if (outsideSide || outsideBottom) return;
            }
            if (isIshitsuki)
            {
                Vector3 tipW = transform.TransformPoint(node.tipPosition);
                if (tipW.y <= plantingSurfacePoint.y) return;
            }

            var rootCont = CreateNode(node.tipPosition, ContinuationDirection(node), nodeRadius, segLength, node);
            rootCont.isRoot         = true;
            rootCont.isAirLayerRoot = node.isAirLayerRoot;
            rootCount++;

            float lateralScale  = isIshitsuki ? 0f : Mathf.Clamp01(1f - distRatio);
            float lateralChance = rootLateralChance * Mathf.Pow(rootLateralDepthDecay, node.depth) * lateralScale;
            if (rootCount < maxTotalRootNodes && Random.value < lateralChance)
            {
                var lat = CreateNode(node.tipPosition, LateralDirection(node), nodeRadius, segLength * 0.85f, node);
                lat.isRoot         = true;
                lat.isAirLayerRoot = node.isAirLayerRoot;
                if (verboseLog) Debug.Log($"[GRoot] SpawnChildren lateral | node={node.id} depth={node.depth} distRatio={distRatio:F2} -> lat id={lat.id}");
            }
            return;
        }

        // Hard branch cap — mid-season completions call SpawnChildren directly, and the
        // branch paths below never checked it (only the root path did): a fast,
        // short-segment species completed hundreds of chords per season and exploded to
        // 8,592 branch nodes, 4× the cap (Dawn Redwood, 2026-07-03 — the "green blob"
        // trunk was the pipe model summing them). Same leader exemption as the
        // season-start loop: depth ≤ 1 keeps extending so the trunk never freezes.
        if (node.depth > 1)
        {
            int branchCount = 0;
            foreach (var n in allNodes) if (!n.isRoot) branchCount++;
            if (branchCount >= maxBranchNodes) return;
        }

        // Cut-site regrowth: shoots emerge from the side of the wound, never straight
        // through the cap face. Always at least one shoot; often two (epicormic response).
        if (node.isTrimCutPoint)
        {
            var shoot = CreateNode(node.tipPosition, CutSiteDirection(node), nodeRadius, segLength, node);
            shoot.isRoot = false;
            if (nodeSubdivs > 1) shoot.subdivisionsLeft = nodeSubdivs - 1;

            // Second shoot from the opposite side of the cap — common after hard cuts.
            if (Random.value < 0.65f)
            {
                float lat2Len = segLength * 0.85f;
                var shoot2 = CreateNode(node.tipPosition, CutSiteDirection(node), nodeRadius, lat2Len, node);
                shoot2.isRoot = false;
                if (nodeSubdivs > 1) shoot2.subdivisionsLeft = nodeSubdivs - 1;
                GameManager.branches++;
            }
            return;
        }

        if (budType == BudType.Opposite)
        {
            var (dirA, dirB) = OppositeForkDirections(node);
            var forkA = CreateNode(node.tipPosition, dirA, nodeRadius, segLength, node);
            forkA.isRoot = false;
            if (nodeSubdivs > 1) forkA.subdivisionsLeft = nodeSubdivs - 1;
            forkA.growthStartDelay = Random.Range(0f, branchSpawnMaxDelay * 0.3f);

            // The second bud of the opposite pair extends only sometimes — unconditional
            // forking doubled nodes per depth level, exponential ×2 growth that NO species
            // parameter could rein in (maple = the worst broccoli however tuned,
            // 2026-07-03). Apical dominance suppresses the partner bud most of the time,
            // so the fork rolls the same chance the Alternate path's lateral does.
            float forkChance = baseBranchChance * Mathf.Pow(branchChanceDepthDecay, node.depth);
            if (Random.value < forkChance)
            {
                var forkB = CreateNode(node.tipPosition, dirB, nodeRadius, segLength, node);
                forkB.isRoot = false;
                if (nodeSubdivs > 1) forkB.subdivisionsLeft = nodeSubdivs - 1;
                forkB.growthStartDelay = Random.Range(0f, branchSpawnMaxDelay);
                GameManager.branches++;
            }
        }
        else
        {
            var cont = CreateNode(node.tipPosition, ContinuationDirection(node), nodeRadius, segLength, node);
            cont.isRoot = false;
            if (nodeSubdivs > 1)
                cont.subdivisionsLeft = nodeSubdivs - 1;
            cont.growthStartDelay = Random.Range(0f, branchSpawnMaxDelay * 0.3f);

            float lateralChanceBranch = baseBranchChance * Mathf.Pow(branchChanceDepthDecay, node.depth);
            if (Random.value < lateralChanceBranch)
            {
                float latLength = segLength * 0.85f * Mathf.Max(0.1f, 1f - apicalDominance);
                var lat = CreateNode(node.tipPosition, LateralDirection(node), nodeRadius, latLength, node);
                lat.isRoot = false;
                if (nodeSubdivs > 1)
                    lat.subdivisionsLeft = nodeSubdivs - 1;
                lat.growthStartDelay = Random.Range(0f, branchSpawnMaxDelay);
                GameManager.branches++;
            }
        }
    }

    // Root Spread Helpers

    /// <summary>
    /// Returns the highest tipPosition.y among all non-root branch nodes.
    /// Used to compute the target root spread radius.
    /// </summary>
    float CalculateTreeHeight()
    {
        float h = 0.5f;  // minimum so spread radius is never zero
        foreach (var node in allNodes)
            if (!node.isRoot && node.tipPosition.y > h)
                h = node.tipPosition.y;
        return h;
    }

    /// <summary>
    /// Scores the current root health (surface root flare) 0–100 and updates
    /// RootHealthScore and RootHealthSectorCoverage (8 directional sectors).
    /// Considers shallow trunk roots: depth 1–rootHealthMaxDepth, Y > -rootHealthSurfaceDepth.
    /// Components: angular coverage (50%), girth thickness (30%), radial balance (20%).
    /// </summary>
    public void RecalculateRootHealthScore()
    {
        const int sectors = 8;
        float[] sectorRadius = new float[sectors];
        float   totalRadius  = 0f;
        int     count        = 0;
        Vector2 com          = Vector2.zero;

        foreach (var node in allNodes)
        {
            if (!node.isRoot || node.isTrimmed) continue;
            if (node.depth < 1 || node.depth > rootHealthMaxDepth) continue;
            if (node.tipPosition.y < -rootHealthSurfaceDepth) continue;

            Vector3 tip   = node.tipPosition;
            float   angle = Mathf.Atan2(tip.z, tip.x);            // –π … +π
            float   t     = (angle + Mathf.PI) / (Mathf.PI * 2f); // 0 … 1
            int     s     = Mathf.Clamp(Mathf.FloorToInt(t * sectors), 0, sectors - 1);

            sectorRadius[s] += node.radius;
            totalRadius      += node.radius;
            com              += new Vector2(tip.x, tip.z) * node.radius;
            count++;
        }

        // Normalise sector coverage for the UI (0 = empty, 1 = best-covered sector)
        float maxSector = 0f;
        for (int i = 0; i < sectors; i++) if (sectorRadius[i] > maxSector) maxSector = sectorRadius[i];
        RootHealthSectorCoverage = new float[sectors];
        if (maxSector > 0f)
            for (int i = 0; i < sectors; i++) RootHealthSectorCoverage[i] = sectorRadius[i] / maxSector;

        if (count == 0 || totalRadius <= 0f) { RootHealthScore = 0f; return; }

        // Angular coverage: fraction of the 8 sectors that have any roots
        int coveredSectors = 0;
        for (int i = 0; i < sectors; i++) if (sectorRadius[i] > 0f) coveredSectors++;
        float angularScore = (float)coveredSectors / sectors;

        // Girth: average root radius vs the ideal target radius
        float girthScore = Mathf.Clamp01(totalRadius / count / rootHealthTargetRadius);

        // Balance: centre of mass close to the trunk origin (safe — totalRadius > 0 guaranteed above)
        com /= totalRadius;
        float balanceScore = Mathf.Clamp01(1f - com.magnitude / rootHealthBalanceRadius);

        RootHealthScore = (angularScore * 0.5f + girthScore * 0.3f + balanceScore * 0.2f) * 100f;
        if (verboseLog) Debug.Log($"[RootHealth] score={RootHealthScore:F1} angular={angularScore:F2} girth={girthScore:F2} balance={balanceScore:F2} sectors={coveredSectors}/8 nodes={count}");
    }

    /// <summary>
    /// Returns the number of sub-segments to use for a branch chord of the given length.
    /// Takes the maximum of branchSubdivisions and however many segments are needed to
    /// keep each one under maxSegmentLength.  Always returns at least 1.
    /// </summary>
    int SubdivsForChord(float chordLength)
    {
        // When maxSegmentLength is set, use it exclusively — branchSubdivisions as a floor
        // creates dozens of micro-segments on short scaled chords (the "sausage" problem).
        if (maxSegmentLength > 0f)
            return Mathf.Max(1, Mathf.CeilToInt(chordLength / maxSegmentLength));
        return Mathf.Max(1, branchSubdivisions);
    }

    /// <summary>
    /// Returns the horizontal distance ratio of a root node's tip to the spread radius.
    /// 0 = at trunk, 1 = at target spread radius, >1 = beyond it.
    /// </summary>
    float RootDistRatio(TreeNode node)
    {
        if (rootAreaTransform != null)
        {
            // Box mode: 0=center, 1=at wall, >1=outside.
            // InverseTransformPoint gives coords in Root Area local space where
            // the box extents are [-0.5, 0.5] on each axis.
            Vector3 worldTip  = transform.TransformPoint(node.tipPosition);
            Vector3 areaLocal = rootAreaTransform.InverseTransformPoint(worldTip);
            float xRatio = Mathf.Abs(areaLocal.x) * 2f;
            float zRatio = Mathf.Abs(areaLocal.z) * 2f;
            // Y: check both floor and ceiling — roots must stay inside the tray height
            float yRatio = Mathf.Abs(areaLocal.y) * 2f;
            return Mathf.Max(xRatio, zRatio, yRatio);
        }
        // Legacy radial fallback
        float spreadRadius = cachedTreeHeight * rootSpreadMultiplier;
        if (spreadRadius <= 0f) return 0f;
        Vector3 tip = node.tipPosition;
        float horizDist = Mathf.Sqrt(tip.x * tip.x + tip.z * tip.z);
        return horizDist / spreadRadius;
    }

    /// <summary>
    /// When a Root Area box is assigned, deflects a root direction away from walls
    /// the root is approaching, so roots run along the inside of the box rather
    /// than stopping dead or punching through.
    /// treeLocalDir and treeLocalPos are in TreeSkeleton local space.
    /// </summary>
    Vector3 DeflectFromRootAreaWalls(Vector3 treeLocalDir, Vector3 treeLocalPos)
    {
        if (rootAreaTransform == null) return treeLocalDir;

        Vector3 worldPos  = transform.TransformPoint(treeLocalPos);
        Vector3 worldDir  = transform.TransformDirection(treeLocalDir);
        Vector3 areaLocal = rootAreaTransform.InverseTransformPoint(worldPos);

        // Margin in normalised box coords (0.5 = half-extent).
        // Within this distance of a wall, start blending toward the wall surface.
        const float margin = 0.20f;

        // Accumulate a weighted inward normal for each nearby face.
        // Side and bottom faces get a stronger push (1.0) than the top face (0.3)
        // so surface roots can emerge naturally while lateral/downward escape is blocked hard.
        Vector3 wallNormal   = Vector3.zero;
        float   totalWeight  = 0f;

        void AddFace(Vector3 faceNormal, float t, float faceWeight)
        {
            float w = t * faceWeight;
            wallNormal  += faceNormal * w;
            totalWeight += w;
        }

        AddFace( rootAreaTransform.right,   Mathf.InverseLerp(0.5f - margin,  0.5f,  areaLocal.x), 1.0f);
        AddFace(-rootAreaTransform.right,   Mathf.InverseLerp(0.5f - margin,  0.5f, -areaLocal.x), 1.0f);
        AddFace( rootAreaTransform.forward, Mathf.InverseLerp(0.5f - margin,  0.5f,  areaLocal.z), 1.0f);
        AddFace(-rootAreaTransform.forward, Mathf.InverseLerp(0.5f - margin,  0.5f, -areaLocal.z), 1.0f);
        // Bottom (floor): strong deflection — roots must not escape downward... unless the tree is
        // pot-bound and this tip is over a drainage hole, in which case let it descend out the hole.
        bool floorEscape = _holePotSoil != null && _holeEscapePressure > 0.4f
                           && _holePotSoil.IsOverHole(areaLocal.x, areaLocal.z);
        if (!floorEscape)
            AddFace(-rootAreaTransform.up,  Mathf.InverseLerp(-0.5f + margin, -0.5f, areaLocal.y), 1.0f);
        // Top (soil surface): weak deflection — let roots emerge slightly above soil
        AddFace( rootAreaTransform.up,      Mathf.InverseLerp( 0.5f - margin,  0.5f,  areaLocal.y), 0.3f);

        if (totalWeight > 0.001f)
        {
            wallNormal.Normalize();
            // Blend between along-wall tangent (hard redirect) and the inward normal (push back in).
            // At the wall face the blend is 70% tangent + 30% inward push so roots curve
            // parallel to the wall rather than bouncing straight back.
            Vector3 along = Vector3.ProjectOnPlane(worldDir, wallNormal);
            Vector3 inward = -wallNormal;  // points back toward box interior
            if (along.sqrMagnitude > 0.001f)
            {
                float blend = Mathf.Clamp01(totalWeight);
                worldDir = Vector3.Slerp(worldDir, Vector3.Lerp(along.normalized, inward, 0.3f), blend).normalized;
            }
            else
            {
                worldDir = inward;
            }
        }

        Vector3 result = transform.InverseTransformDirection(worldDir);
        return result.sqrMagnitude > 0.001f ? result.normalized : treeLocalDir;
    }

    // Direction Helpers

    /// <summary>
    /// Continuation direction: inertia + phototropism upward for branches,
    /// inertia + gravity downward for roots. Roots near the planting surface
    /// have their direction deflected to hug the surface.
    /// </summary>
    Vector3 ContinuationDirection(TreeNode node)
    {
        Vector3 rand = Random.insideUnitSphere * randomWeight;

        // Air-layer roots grow downward (gravitropism / anti-phototropism).
        // They deflect along rock surfaces and snap onto the soil plane once they arrive.
        if (node.isAirLayerRoot)
        {
            Vector3 airInertia = (node.growDirection * inertiaWeight + rand).normalized;
            Vector3 dir = Vector3.Slerp(airInertia, Vector3.down, 0.7f).normalized;

            Vector3 worldTip = transform.TransformPoint(node.tipPosition);

            // ── Rock surface deflection ───────────────────────────────────────
            if (rockCollider != null)
            {
                float effectiveRadius = rockInfluenceRadius * 2f;
                Vector3 rockCenter = rockCollider.bounds.center;
                Vector3 toCenter   = (rockCenter - worldTip).normalized;
                Vector3 closestPt  = rockCenter;
                if (rockCollider.Raycast(new Ray(worldTip, toCenter), out RaycastHit cpHit, effectiveRadius * 3f))
                    closestPt = cpHit.point;
                else
                {
                    Vector3 outside = worldTip - toCenter * effectiveRadius * 2f;
                    if (rockCollider.Raycast(new Ray(outside, toCenter), out RaycastHit cpHit2, effectiveRadius * 4f))
                        closestPt = cpHit2.point;
                }
                float distToRock = Vector3.Distance(worldTip, closestPt);
                if (distToRock < effectiveRadius)
                {
                    Vector3 outward = (closestPt - rockCenter).normalized;
                    if (outward.sqrMagnitude < 0.001f) outward = Vector3.up;

                    Vector3 surfaceNormal = outward;
                    Ray normalRay = new Ray(closestPt + outward * 0.5f, -outward);
                    if (rockCollider.Raycast(normalRay, out RaycastHit normalHit, 1f))
                        surfaceNormal = normalHit.normal;

                    Vector3 worldDir   = transform.TransformDirection(dir);
                    Vector3 surfaceDir = Vector3.ProjectOnPlane(worldDir, surfaceNormal);
                    if (surfaceDir.sqrMagnitude < 0.001f)
                        surfaceDir = Vector3.ProjectOnPlane(Vector3.down, surfaceNormal);
                    surfaceDir = (surfaceDir.normalized + Vector3.down * rootGravityWeight * 20f).normalized;

                    float blend = Mathf.Lerp(0.6f, 1.0f, 1f - Mathf.Clamp01(distToRock / effectiveRadius));
                    dir = transform.InverseTransformDirection(
                        Vector3.Slerp(worldDir, surfaceDir, blend).normalized);
                }
            }

            // ── Soil-plane snap ───────────────────────────────────────────────
            Plane soilPlane = new Plane(plantingNormal, plantingSurfacePoint);
            float distToSoil = soilPlane.GetDistanceToPoint(worldTip);
            if (distToSoil >= 0f && distToSoil < rootSurfaceSnapDist)
            {
                Vector3 surfaceDir = Vector3.ProjectOnPlane(dir, plantingNormal);
                if (surfaceDir.sqrMagnitude > 0.001f)
                {
                    float blend = 1f - Mathf.Clamp01(distToSoil / rootSurfaceSnapDist);
                    dir = Vector3.Slerp(dir, surfaceDir.normalized, blend).normalized;
                }
            }

            return LimitRootBend(node, dir);
        }

        if (node.isRoot)
        {
            // Outward radial direction from trunk base, projected flat.
            // This is the primary bias — keeps roots spreading wide near the surface.
            Vector3 trunkBase = root != null ? root.worldPosition : Vector3.zero;
            Vector3 radial    = node.worldPosition - trunkBase;
            radial.y = 0f;
            if (radial.sqrMagnitude < 0.001f)
                radial = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f));
            radial = radial.normalized;

            // Ishitsuki: suppress radial spread on the rock face — roots flow DOWN, not outward.
            bool isIshitsuki = isIshitsukiMode;
            Vector3 dir = (node.growDirection * inertiaWeight
                          + (isIshitsuki ? Vector3.zero : radial * rootRadialWeight)
                          + Vector3.down      * rootGravityWeight
                          + rand).normalized;

            Vector3 worldTip = transform.TransformPoint(node.tipPosition);
            bool nearRock = false;

            // ── Rock surface deflection (Ishitsuki) ───────────────────────────
            // Note: all pre-grown Ishitsuki cables are handled by PreGrowRootsToSoil.
            // This block only fires for any organic root growth that slips through
            // (e.g. auto-planted trunk roots before their first pre-grow pass).
            // Use a raycast-based closest-point approximation instead of
            // Physics.ClosestPoint, which requires a convex MeshCollider.
            if (rockCollider != null)
            {
                float effectiveRadius = rockInfluenceRadius * 2f;

                // Approximate closest surface point: cast a ray from worldTip toward
                // the rock centre; the hit point is on the surface facing us.
                Vector3 rockCenter = rockCollider.bounds.center;
                Vector3 toCenter   = (rockCenter - worldTip).normalized;
                Vector3 closestPt  = rockCenter; // fallback
                if (rockCollider.Raycast(new Ray(worldTip, toCenter), out RaycastHit cpHit, effectiveRadius * 3f))
                    closestPt = cpHit.point;
                else
                {
                    // Also try from outside in (root may be inside/near the rock)
                    Vector3 outside = worldTip - toCenter * effectiveRadius * 2f;
                    if (rockCollider.Raycast(new Ray(outside, toCenter), out RaycastHit cpHit2, effectiveRadius * 4f))
                        closestPt = cpHit2.point;
                }
                float distToRock = Vector3.Distance(worldTip, closestPt);

                if (distToRock < effectiveRadius)
                {
                    nearRock = true;

                    // Get surface normal via raycast from outside inward (world space).
                    Vector3 outward    = closestPt - rockCenter;
                    if (outward.sqrMagnitude < 0.001f) outward = Vector3.up;
                    outward.Normalize();

                    Vector3 surfaceNormal = outward;
                    Ray normalRay = new Ray(closestPt + outward * 0.5f, -outward);
                    if (rockCollider.Raycast(normalRay, out RaycastHit normalHit, 1f))
                        surfaceNormal = normalHit.normal;

                    // dir is in local space — convert to world for the projection.
                    Vector3 worldDir = transform.TransformDirection(dir);

                    // Project onto rock surface, fall back to pure-down if tangent is zero.
                    Vector3 surfaceDir = Vector3.ProjectOnPlane(worldDir, surfaceNormal);
                    if (surfaceDir.sqrMagnitude < 0.001f)
                        surfaceDir = Vector3.ProjectOnPlane(Vector3.down, surfaceNormal);

                    // On near-horizontal surfaces (top of rock) gravity is perpendicular
                    // to the plane, so adding it then re-projecting would kill it entirely.
                    // Instead: push radially outward toward the rock edge so the root flows
                    // off the top and down the side, THEN let gravity do its work on the face.
                    float upDot = Vector3.Dot(surfaceNormal, Vector3.up);
                    if (upDot > 0.5f)
                    {
                        Vector3 radialOut = worldTip - rockCollider.bounds.center;
                        radialOut.y = 0f;
                        if (radialOut.sqrMagnitude > 0.001f)
                        {
                            Vector3 edgePush = Vector3.ProjectOnPlane(radialOut.normalized, surfaceNormal);
                            if (edgePush.sqrMagnitude > 0.001f)
                                surfaceDir = (surfaceDir.normalized + edgePush.normalized * upDot).normalized;
                        }
                    }

                    // Gravity bias — NOT re-projected, so it works on all surface angles.
                    // On vertical faces this pulls strongly downward; on horizontal faces
                    // the slight inward lean is harmless (segments are short).
                    surfaceDir = (surfaceDir.normalized + Vector3.down * rootGravityWeight * 20f).normalized;

                    // Full adhesion regardless of height — real Ishitsuki roots grip the
                    // rock all the way from crown to soil, never float off mid-face.
                    // Blend: 1.0 on surface → 0.6 at far edge of influence.
                    float blend = Mathf.Lerp(0.6f, 1.0f,
                        1f - Mathf.Clamp01(distToRock / effectiveRadius));
                    dir = transform.InverseTransformDirection(
                        Vector3.Slerp(worldDir, surfaceDir, blend).normalized);
                }
            }

            // Roots in free air past the rock edge: fall nearly straight down to reach soil.
            if (!nearRock)
            {
                float heightAboveSoil = transform.TransformPoint(node.tipPosition).y - plantingSurfacePoint.y;
                if (heightAboveSoil > 0.05f)
                {
                    // 0.95 max blend → almost straight down; faster than before so
                    // roots don't hang horizontally past the rock edge.
                    float fallBlend = Mathf.Clamp01(heightAboveSoil / 0.3f) * (isIshitsuki ? 0.95f : 0.85f);
                    Vector3 worldDirFall = transform.TransformDirection(dir);
                    worldDirFall = Vector3.Slerp(worldDirFall, Vector3.down, fallBlend).normalized;
                    dir = transform.InverseTransformDirection(worldDirFall);
                }
            }

            // When near the planting surface, blend toward a surface-tangent direction
            // so roots flow along the soil face instead of going through it.
            Plane surface = new Plane(plantingNormal, plantingSurfacePoint);
            float distToSurface = surface.GetDistanceToPoint(worldTip);

            if (distToSurface >= 0f && distToSurface < rootSurfaceSnapDist)
            {
                Vector3 surfaceDir = Vector3.ProjectOnPlane(dir, plantingNormal);
                if (surfaceDir.sqrMagnitude > 0.001f)
                {
                    float blend = 1f - Mathf.Clamp01(distToSurface / rootSurfaceSnapDist);
                    dir = Vector3.Slerp(dir, surfaceDir.normalized, blend).normalized;
                }
            }

            // Smooth the chain: limit how far this segment may rotate away from its
            // parent so a sharp out-then-down elbow becomes a flowing arc. Applied
            // before the safety clamps below so they remain authoritative.
            dir = LimitRootBend(node, dir);

            // Clamp: roots must never grow upward — EXCEPT when near the rock,
            // where they may need to crest an edge to get over the side.
            if (!nearRock && dir.y > 0f)
            {
                dir = Vector3.ProjectOnPlane(dir, Vector3.up);
                if (dir.sqrMagnitude < 0.001f)
                    dir = radial;
                dir.Normalize();
            }

            dir = DeflectFromRootAreaWalls(dir, node.tipPosition);
            return dir;
        }
        // Straight within the season's shoot: mid-chord sub-segments barely deviate.
        // Full random + sun jitter applied PER SEGMENT × 3-4 subdivisions per chord gave
        // every branch a curling random walk — the "brain coral" tortuosity that read as
        // broccoli even after density was fixed (2026-07-03). Real shoots extend straight
        // and change direction BETWEEN seasons, so direction character belongs at chord
        // boundaries, not inside one shoot.
        float straightness = node.subdivisionsLeft > 0 ? 0.15f : 1f;
        // Slerp toward sun so phototropismWeight is a direct blend fraction (0=none, 1=point straight up)
        Vector3 inertiaDir = (node.growDirection * inertiaWeight + rand * straightness).normalized;
        return Vector3.Slerp(inertiaDir, SunDirection(), phototropismWeight * straightness);
    }

    /// <summary>
    /// Caps how far a new root segment's direction may rotate from its parent segment's,
    /// turning sharp elbows into smooth multi-segment arcs. Pure direction limit: it never
    /// touches node positions, so it can't cascade bad positions the way re-chaining can,
    /// and because it only ever shrinks the bend it cannot reintroduce a zigzag.
    /// Both vectors are tree-local (parent.growDirection and the freshly computed dir).
    /// </summary>
    Vector3 LimitRootBend(TreeNode parent, Vector3 desiredLocalDir)
    {
        if (rootMaxBendPerSegmentDeg >= 179f) return desiredLocalDir;
        Vector3 parentDir = parent.growDirection;
        if (parentDir.sqrMagnitude < 1e-6f || desiredLocalDir.sqrMagnitude < 1e-6f)
            return desiredLocalDir;
        return Vector3.RotateTowards(
            parentDir.normalized, desiredLocalDir.normalized,
            rootMaxBendPerSegmentDeg * Mathf.Deg2Rad, 0f).normalized;
    }

    /// <summary>
    /// Lateral direction for both branches (splay + upward bias) and roots (splay + downward bias).
    /// </summary>
    Vector3 LateralDirection(TreeNode node)
    {
        // AutoStyler steering: an empty branch slot wants the next lateral at a specific
        // compass azimuth. Build the direction from that azimuth (±30° jitter) instead of
        // random splay, then consume the preference so only the first lateral is steered.
        if (!node.isRoot && node.preferredLateralAzimuth >= 0f)
        {
            float az   = (node.preferredLateralAzimuth + Random.Range(-30f, 30f)) * Mathf.Deg2Rad;
            float elev = Random.Range(branchAngleMin, branchAngleMax) * Mathf.Deg2Rad;
            Vector3 outward = new Vector3(Mathf.Sin(az), 0f, Mathf.Cos(az));
            Vector3 world   = Mathf.Cos(elev) * Vector3.up + Mathf.Sin(elev) * outward;
            node.preferredLateralAzimuth = -1f;
            return transform.InverseTransformDirection(world).normalized;
        }

        Vector3 perp = Vector3.Cross(node.growDirection, Random.insideUnitSphere).normalized;
        if (perp.sqrMagnitude < 0.001f)
            perp = Vector3.Cross(node.growDirection, Vector3.right).normalized;

        float   angle = Random.Range(branchAngleMin, branchAngleMax);
        Vector3 dir   = Vector3.Slerp(node.growDirection, perp, angle / 90f);

        if (node.isRoot)
        {
            Vector3 trunkBase  = root != null ? root.worldPosition : Vector3.zero;
            Vector3 rootRadial = node.worldPosition - trunkBase;
            rootRadial.y = 0f;
            if (rootRadial.sqrMagnitude > 0.001f) rootRadial.Normalize();
            Vector3 bias    = rootRadial * rootRadialWeight * 0.5f + Vector3.down * rootGravityWeight * 0.5f;
            Vector3 lateral = (dir + bias).normalized;
            // Clamp: lateral roots must not grow upward
            if (lateral.y > 0f)
            {
                lateral = Vector3.ProjectOnPlane(lateral, Vector3.up);
                if (lateral.sqrMagnitude < 0.001f) lateral = rootRadial;
                lateral.Normalize();
            }
            lateral = DeflectFromRootAreaWalls(lateral, node.tipPosition);
            return lateral;
        }
        // Lateral branches get half the phototropism blend of continuation segments,
        // then the planar-pad flatten (see FlattenTowardPad — the anti-broccoli rule).
        return FlattenTowardPad(Vector3.Slerp(dir, SunDirection(), phototropismWeight * 0.5f), node.depth + 1);
    }

    // Keep old name as alias so any external callers don't break.
    Vector3 LateralBranchDirection(TreeNode node) => LateralDirection(node);

    /// <summary>
    /// Once a wound has healed and the surrounding wood has thickened enough to
    /// visually absorb the cut cap, clear isTrimCutPoint so the tip reverts to
    /// normal taper behaviour (no more flat disc or forced lateral regrowth).
    /// </summary>
    void CheckCutCapAbsorption()
    {
        foreach (var node in allNodes)
        {
            if (!node.isTrimCutPoint || node.hasWound) continue;
            if (node.parent == null) continue;
            // Parent's callousing wood has grown large enough to swallow the cap.
            if (node.parent.radius >= node.radius * 0.92f)
            {
                node.isTrimCutPoint = false;
                if (verboseLog) Debug.Log($"[Cap] Node {node.id} cut cap absorbed (parent.r={node.parent.radius:F3} ≥ {node.radius * 0.92f:F3})");
                meshBuilder.SetDirty();
            }
        }
    }

    /// <summary>Planar-pad bias — THE anti-broccoli geometry rule (2026-07-03).
    /// Real twigs spread in near-horizontal fans (that's what a "pad" is); unbiased 3D
    /// splay turns every cluster into a sphere, and spherical knots read as broccoli
    /// from year 2 no matter what the styler does. Deeper twigs flatten harder (pad
    /// interiors); trunk/scaffold spawns (child depth < 2) keep full freedom so styling
    /// still owns the structure.</summary>
    Vector3 FlattenTowardPad(Vector3 dir, int childDepth)
    {
        if (childDepth < 2) return dir;
        Vector3 flatDir = Vector3.ProjectOnPlane(dir, Vector3.up);
        if (flatDir.sqrMagnitude < 1e-4f) return dir;                       // straight-up shoot: leave it
        flatDir = (flatDir.normalized + Vector3.up * 0.12f).normalized;     // pads rise slightly
        float flatten = Mathf.Lerp(0.35f, 0.85f, Mathf.Clamp01((childDepth - 2) / 6f));
        return Vector3.Slerp(dir, flatDir, flatten).normalized;
    }

    /// <summary>
    /// Direction for shoots sprouting from a cut stump.
    /// Always a strong lateral (40–70°) so growth emerges from the side of the
    /// cap rather than punching straight through the cut face.
    /// </summary>
    Vector3 CutSiteDirection(TreeNode node)
    {
        Vector3 perp = Vector3.Cross(node.growDirection, Random.insideUnitSphere).normalized;
        if (perp.sqrMagnitude < 0.001f)
            perp = Vector3.Cross(node.growDirection, Vector3.right).normalized;
        float   angle = Random.Range(40f, 70f);
        Vector3 dir   = Vector3.Slerp(node.growDirection, perp, angle / 90f);
        return FlattenTowardPad(Vector3.Slerp(dir, SunDirection(), phototropismWeight * 0.5f), node.depth + 1);
    }

    /// <summary>
    /// Returns two symmetric fork directions for Opposite budding.
    /// Both branches diverge equally from the parent's grow direction,
    /// mirrored across a random perpendicular axis.
    /// </summary>
    (Vector3 a, Vector3 b) OppositeForkDirections(TreeNode node)
    {
        Vector3 perp = Vector3.Cross(node.growDirection, Random.insideUnitSphere).normalized;
        if (perp.sqrMagnitude < 0.001f)
            perp = Vector3.Cross(node.growDirection, Vector3.right).normalized;

        float halfAngle = Random.Range(branchAngleMin, branchAngleMax) * 0.5f;
        Vector3 dir1 = Quaternion.AngleAxis( halfAngle, perp) * node.growDirection;
        Vector3 dir2 = Quaternion.AngleAxis(-halfAngle, perp) * node.growDirection;

        // Same phototropism blend as laterals, then the planar-pad flatten.
        dir1 = FlattenTowardPad(Vector3.Slerp(dir1, SunDirection(), phototropismWeight * 0.5f).normalized, node.depth + 1);
        dir2 = FlattenTowardPad(Vector3.Slerp(dir2, SunDirection(), phototropismWeight * 0.5f).normalized, node.depth + 1);
        return (dir1, dir2);
    }

    /// <summary>
    /// Direction toward the sun in tree-local space.
    /// Converts world up so phototropism works correctly when the tree is tilted on a rock.
    /// </summary>
    Vector3 SunDirection()
    {
        // Normalized: InverseTransformDirection scales by the inverse transform scale, and a
        // non-unit result silently stretches every growDirection it gets Slerped into.
        return transform.InverseTransformDirection(Vector3.up).normalized;
    }

    // Pipe Model

    /// <summary>
    /// Recalculates all node radii bottom-up using da Vinci's pipe model:
    ///     parent.radius^2 = sum(child.radius^2)
    /// </summary>
    readonly Stopwatch radiiTimer = new Stopwatch();

    public void RecalculateRadii(TreeNode node)
    {
        // Time only the root call so we get one measurement per full traversal
        bool isRootCall = (node == root);
        if (isRootCall) radiiTimer.Restart();

        RecalculateRadiiInternal(node);

        // After the pipe model runs, override air-layer root radii so they track
        // the trunk's growth and never get swallowed. Must run after the main pass
        // so the pipe model doesn't immediately overwrite them.
        if (isRootCall) ScaleAirLayerRootRadii();
        if (isRootCall) ScaleIshitsukiCableRadii();

        if (isRootCall)
        {
            radiiTimer.Stop();
            if (radiiTimer.ElapsedMilliseconds > 0)
                Debug.Log($"[Perf] RecalculateRadii nodes={allNodes.Count} took {radiiTimer.ElapsedMilliseconds}ms");
        }
    }

    /// <summary>
    /// Keeps air-layer root radii proportional to their trunk parent as the tree thickens.
    /// Walks each isAirLayerRoot node up the chain to find the first non-air-layer ancestor
    /// (the trunk node), then sets radius = trunkRadius * multiplier * taper^depth.
    /// </summary>
    /// <summary>
    /// Each frame: re-anchor every air-layer root base to its parent's current tip
    /// so strands don't get swallowed as the trunk node grows longer.
    /// Multiple passes handle chains: pass N propagates corrections N links deep.
    /// </summary>
    void UpdateAirLayerRootPositions()
    {
        if (allNodes == null) return;
        for (int pass = 0; pass < airLayerRootSegments; pass++)
        {
            foreach (var node in allNodes)
            {
                if (!node.isAirLayerRoot || node.parent == null) continue;

                if (node.parent.isAirLayerRoot)
                {
                    // Chain segment: just track parent tip directly.
                    node.worldPosition = node.parent.tipPosition;
                }
                else
                {
                    // First segment: anchor to the cylindrical surface of the trunk node,
                    // not its center, so the root visually emerges from the bark.
                    // Derive the radial direction from the current XZ offset; fall back
                    // to the node's grow direction on the first frame (when offset == 0).
                    Vector3 offset    = node.worldPosition - node.parent.tipPosition;
                    Vector3 radialDir = new Vector3(offset.x, 0f, offset.z);
                    if (radialDir.sqrMagnitude < 0.0001f)
                        radialDir = new Vector3(node.growDirection.x, 0f, node.growDirection.z);
                    if (radialDir.sqrMagnitude < 0.0001f)
                        radialDir = Vector3.right;
                    node.worldPosition = node.parent.tipPosition + radialDir.normalized * node.parent.radius;
                }
            }
        }
    }

    void ScaleAirLayerRootRadii()
    {
        foreach (var node in allNodes)
        {
            if (!node.isAirLayerRoot) continue;

            // Walk up to find the trunk node and how deep in the strand this segment is.
            int      chainDepth = 0;
            TreeNode trunkNode  = node.parent;
            while (trunkNode != null && trunkNode.isAirLayerRoot)
            {
                chainDepth++;
                trunkNode = trunkNode.parent;
            }
            if (trunkNode == null) continue;

            float r = Mathf.Max(
                trunkNode.radius * airLayerRootRadiusMultiplier * Mathf.Pow(0.8f, chainDepth),
                terminalRadius);
            node.radius    = r;
            node.minRadius = r;
        }
    }

    /// <summary>
    /// Scales Ishitsuki pre-grown cable radii proportional to trunk thickness each season.
    /// startNodes (direct root children of root) are set to trunkRadius * multiplier.
    /// Each cable segment tapers toward rootTerminalRadius with distance from the startNode.
    /// Must run after RecalculateRadiiInternal so it overrides the pipe-model floor.
    /// </summary>
    void ScaleIshitsukiCableRadii()
    {
        if (!isIshitsukiMode) return;

        // trunk radius is whatever the pipe model computed for the tree base
        float trunkRadius = root.radius;
        if (trunkRadius <= 0f) trunkRadius = rootTerminalRadius;

        // Scale startNodes (direct root children that own cable chains)
        foreach (var child in root.children)
        {
            if (!child.isRoot || child.isTrainingWire) continue;
            float r = Mathf.Max(trunkRadius * ishitsukiCableRadiusMultiplier, rootTerminalRadius);
            child.radius    = r;
            child.minRadius = r;
        }

        // Scale each pre-grown cable node by its depth in the chain from its startNode
        foreach (var node in allNodes)
        {
            if (!node.isTrainingWire) continue;

            // Walk up to find the startNode (first non-training-wire root ancestor)
            int      chainDepth = 0;
            TreeNode ancestor   = node.parent;
            while (ancestor != null && ancestor.isTrainingWire)
            {
                chainDepth++;
                ancestor = ancestor.parent;
            }

            float r = Mathf.Max(
                trunkRadius * ishitsukiCableRadiusMultiplier * Mathf.Pow(0.82f, chainDepth + 1),
                rootTerminalRadius);
            node.radius    = r;
            node.minRadius = r;
        }
    }

    void RecalculateRadiiInternal(TreeNode node)
    {
        if (node.isTerminal) return;

        // Pipe model with a tunable exponent. Leonardo's classic exponent 2
        // (parent² = Σ child²) over-thickens badly at bonsai twig counts: a branch
        // with 300 descendants gets 17× the terminal radius while staying short —
        // the year-6 "fist of stubs" look, branch length:thickness ≈ 3:1 where real
        // wood runs 20:1 (2026-07-03). Field measurements put real trees nearer 2.5:
        // parent = (Σ child^e)^(1/e); higher e = slimmer aggregation at every level,
        // compounding to a much slenderer skeleton.
        float e = Mathf.Max(1.5f, pipeModelExponent);
        float sum = 0f;
        foreach (var child in node.children)
        {
            RecalculateRadiiInternal(child);
            // Root children don't contribute to branch pipe-model radii — they would
            // otherwise inflate the trunk as the root system grows exponentially.
            if (!node.isRoot && child.isRoot) continue;
            sum += Mathf.Pow(child.radius, e);
        }
        float pipeRadius = Mathf.Pow(sum, 1f / e);
        node.radius    = Mathf.Max(pipeRadius, node.minRadius);
        node.minRadius = node.radius;
    }

    // Bud System

    /// <summary>
    /// Called at season end (TimeGo). Sets hasBud on all eligible terminal branch nodes
    /// and spawns a bud prefab at each tip position.
    /// </summary>
    void SetBuds()
    {
        int termCount = 0;
        int latCount  = 0;

        // Flowering species also set FLOWER buds in autumn — they open into blossoms at bloomMonth
        // next spring (FlowerManager). Gated on maturity so young seedlings don't bloom.
        bool canFlower = species != null
                         && (species.bloomType != BloomType.None || species.fruitType != FruitType.None)
                         && (GameManager.year - startYear) >= species.floweringStartAge;

        foreach (var node in allNodes)
        {
            if (node.isTrimmed || node.isRoot) continue;

            if (node.isTerminal)
            {
                // subdivisionsLeft > 0 means this is a mid-chord sub-segment that was
                // stopped before completing. Resume it in spring instead of treating it
                // as a real tip — SetBuds skips it; StartNewGrowingSeason resumes it.
                if (node.subdivisionsLeft > 0) continue;
                if (node.hasBud) continue;

                node.hasBud = true;
                if (canFlower && UnityEngine.Random.value < species.flowerBudChance)
                    node.hasFlowerBud = true;
                if (showTerminalBuds && budPrefab != null)
                {
                    var bud = Instantiate(budPrefab, transform);
                    bud.transform.localPosition = node.tipPosition;
                    bud.transform.localRotation = Quaternion.LookRotation(node.growDirection);
                    budObjects[node.id] = bud;
                }
                termCount++;
            }
            else
            {
                // Junction node — spawn a lateral (axillary) bud visual.
                // Skip sub-segment junctions (they're part of a single wireable chord, not real forks).
                bool isSubJunction = node.children.Count == 1 && node.children[0].depth == node.depth;
                if (isSubJunction) continue;

                if (showLateralBuds && lateralBudPrefab != null)
                {
                    var latBud = Instantiate(lateralBudPrefab, transform);
                    latBud.transform.localPosition = node.tipPosition;
                    latBud.transform.localRotation = Quaternion.LookRotation(node.growDirection);
                    lateralBudObjects.Add(latBud);
                    latCount++;
                }
            }
        }
        // Canopy energy: computed from current leaf state (peak summer canopy).
        // Stored on treeEnergy and used as a multiplier next spring.
        var lm = GetComponent<LeafManager>();
        if (lm != null)
        {
            treeEnergy = lm.ComputeTreeEnergy(allNodes, Mathf.Clamp(maxEnergyMultiplier, 1f, 3f));
            treeEnergy = Mathf.Max(0.4f, treeEnergy);  // floor: leafless trees still grow at base rate
        }
        Debug.Log($"[Bud] Buds set | terminal={termCount} lateral={latCount} treeEnergy={treeEnergy:F2} year={GameManager.year}");
    }

    // Trimming

    /// <summary>
    /// Pinches the soft growing tip of a terminal node.
    /// Unlike TrimNode, no wound is created and leaves remain on the node.
    /// The tip stops growing; back-buds are stimulated on nearby ancestors.
    /// </summary>
    public void PinchNode(TreeNode node)
    {
        if (node == null || !node.isTerminal || node.isRoot || node.isTrimmed)
        {
            Debug.Log("[Pinch] Ignored: node must be a non-root, non-trimmed terminal.");
            return;
        }

        TrainingRecorder.Instance?.RecordAction("Pinch", node.id);

        // Stop growth at current length — soft tissue only, no woody wound.
        // Do NOT set isTrimmed: that would exclude this node from autumn SetBuds,
        // preventing any regrowth next spring. Instead freeze it in place so
        // StartNewGrowingSeason's mid-chord resume skips it (length == targetLength)
        // and SetBuds sets hasBud in autumn → bud break next spring → normal branching.
        node.isGrowing = false;
        node.length    = node.targetLength;

        // Refinement gain (same rate as a trim cut but scaled by vigor — less effective on strong vigorous growth)
        float refGain = refinementOnTrim / Mathf.Max(0.5f, node.branchVigor);
        node.refinementLevel = Mathf.Min(node.refinementLevel + refGain, refinementCap);

        // Vigor reduction — pinching weakens the tip slightly
        node.branchVigor = Mathf.Max(vigorMin, node.branchVigor * vigorTrimMultiplier);

        // Light health cost — much less than a hard cut (soft tissue only)
        ApplyDamage(node, DamageType.TrimTrauma, trimTraumaDamage * 0.25f);

        // Back-bud stimulation on up to 2 ancestors
        TreeNode ancestor = node.parent;
        for (int i = 0; i < 2 && ancestor != null && ancestor != root; i++)
        {
            ancestor.backBudStimulated = true;
            ancestor = ancestor.parent;
        }

        meshBuilder.SetDirty();
        Debug.Log($"[Pinch] Pinched node={node.id} depth={node.depth} refLevel={node.refinementLevel:F2} vigor={node.branchVigor:F2}");
    }

    /// Removes a node and all its descendants.
    /// stimulateRegrowth=false makes this a pure REDUCTION cut: no cut-point epicormic
    /// regrowth, no ancestor back-bud stimulation. The AutoStyler's thinning/excess trims
    /// use it — with the default behaviour every styling cut manufactured a twig knot
    /// bigger than what it removed (1–2 cut-site shoots + 3 stimulated ancestors), which
    /// kept the crowns broccoli despite the thinning pass (2026-07-03). Player cuts keep
    /// the full response: trim-to-back-bud is a real technique the game should reward.
    /// </summary>
    public void TrimNode(TreeNode node, bool stimulateRegrowth = true)
    {
        if (node == root)
        {
            Debug.LogWarning("TreeSkeleton: cannot trim the root node.");
            return;
        }

        if (!ProgressionManager.AutomationActive)
            ProgressionManager.Instance?.ReachMilestone("first_trim");   // player cuts only, not the auto-styler
        TrainingRecorder.Instance?.RecordAction("Trim", node.id);

        TreeNode parent = node.parent;

        // Capture undo state before any modifications
        pendingUndo = CaptureTrimUndoState(node, parent);

        parent?.children.Remove(node);

        var removed = new List<TreeNode>();
        RemoveSubtree(node, removed);

        if (node.isRoot)
            Debug.Log($"[Root] TrimRoot node={node.id} depth={node.depth} | removed={removed.Count} nodes");

        if (parent != null && parent.isTerminal && stimulateRegrowth)
        {
            parent.isTrimCutPoint = true;
            parent.trimCutDepth   = parent.depth;

            // Winter pruning: deep cuts in dormancy (months 11–2) that exceed the
            // threshold start with a head-start season count to reflect winter callusing,
            // but actual recovery is slowed by severity and reserve-depletion effects.
            int m = GameManager.month;
            bool isWinterCut = m == 11 || m == 12 || m == 1 || m == 2;
            if (isWinterCut && winterDormantDepthThreshold > 0 && parent.depth >= winterDormantDepthThreshold)
                parent.regrowthSeasonCount = 2;
            else
                parent.regrowthSeasonCount = 0;

            // Vigorous shoots refine slower (more wood per cut = less structural change per trim).
            float refGain = refinementOnTrim / Mathf.Max(0.5f, parent.branchVigor);
            parent.refinementLevel = Mathf.Min(parent.refinementLevel + refGain, refinementCap);

            // Cutting the tip reduces local vigor — energy that was driving extension is lost.
            parent.branchVigor = Mathf.Max(vigorMin, parent.branchVigor * vigorTrimMultiplier);
        }

        // Back-budding: stimulate the nearest 3 non-root ancestors so they have a
        // boosted chance to sprout a lateral next spring. (Skipped for reduction cuts —
        // see the stimulateRegrowth doc above.)
        if (!node.isRoot && stimulateRegrowth)
        {
            int stimulated = 0;
            TreeNode ancestor = parent;
            while (ancestor != null && stimulated < 3)
            {
                if (!ancestor.isRoot)
                {
                    ancestor.backBudStimulated = true;
                    stimulated++;
                }
                ancestor = ancestor.parent;
            }
        }

        // Wound: mark the exposed cut face and spawn a visualization.
        // Subdivision cut (same depth) = tip nip — small ring.
        // Real branch cut (deeper node) = full callus wound.
        if (parent != null && !parent.isRoot && parent != root)
        {
            bool isSubdivisionCut  = (node.depth == parent.depth);
            parent.hasWound        = true;
            parent.hadWoundScar    = true;   // keeps a faint callus scar after it heals
            parent.woundRadius     = isSubdivisionCut ? node.radius * 0.35f : node.radius;
            if (isSubdivisionCut)
            {
                parent.woundFaceNormal = parent.growDirection;
            }
            else if (useBevelCut && cutAngleDeg > 0.5f)
            {
                // Tilt the cut face toward the parent so water runs off.
                // Perpendicular axis = hinge of the tilt (cross of removed branch and parent).
                Vector3 perp = Vector3.Cross(node.growDirection, parent.growDirection).normalized;
                if (perp.sqrMagnitude < 0.001f)
                    perp = Vector3.Cross(node.growDirection, Vector3.up).normalized;
                parent.woundFaceNormal = Quaternion.AngleAxis(cutAngleDeg, perp) * node.growDirection;
            }
            else
            {
                parent.woundFaceNormal = node.growDirection;
            }
            parent.woundAge        = 0f;
            parent.pasteApplied    = false;
            CreateWoundObject(parent);

            // Trim trauma: small health hit on the cut site. Accumulates with repeated cuts;
            // recovers at trimTraumaRecoveryPerSeason each spring.
            ApplyDamage(parent, DamageType.TrimTrauma, trimTraumaDamage);
        }

        // Winter pruning: accumulate cut depth for heavy-prune reserve-depletion tracking.
        if (!node.isRoot)
        {
            int removedBranchNodes = 0;
            foreach (var r in removed) if (!r.isRoot) removedBranchNodes++;
            cutDepthAccumulatedThisSeason += removedBranchNodes;
        }

        OnSubtreeTrimmed?.Invoke(removed);
        RecalculateRadii(root);
        meshBuilder.SetDirty();
        cachedTreeHeight = CalculateTreeHeight();  // keep cinematic camera zoom current
    }

    // ── Jin (deliberate deadwood) ─────────────────────────────────────────────

    /// <summary>
    /// Convert the branch starting at `node` into jin — deliberate stripped deadwood.
    /// The node's same-depth continuation chain (one physical branch) is kept as a
    /// bleached spike; laterals hanging off it come away with the bark (removed
    /// directly, no wounds — no callus ever forms on stripped wood). The chain is
    /// marked dead + deadwood + jin: it never heals, grows, or leafs, and weathers
    /// from fresh tan to silver-grey over the years in DiebackPass.
    /// </summary>
    public void JinNode(TreeNode node)
    {
        if (node == null || node == root || node.isRoot || node.isTrimmed || node.isJin) return;

        if (!ProgressionManager.AutomationActive)
            ProgressionManager.Instance?.ReachMilestone("first_jin");   // player jins only
        TrainingRecorder.Instance?.RecordAction("Jin", node.id);

        // Walk the same-depth chain outward (one physical branch = a chain of same-depth
        // segments — see the depth-per-chord convention); strip every lateral off it.
        var chain      = new List<TreeNode>();
        var strippedOff = new List<TreeNode>();
        var cur = node;
        while (cur != null)
        {
            chain.Add(cur);
            TreeNode next = null;
            foreach (var c in new List<TreeNode>(cur.children))
            {
                if (next == null && c.depth == cur.depth) { next = c; continue; }  // the continuation
                cur.children.Remove(c);
                RemoveSubtree(c, strippedOff);   // laterals come away with the bark — no wound
            }
            cur = next;
        }

        var lm = GetComponent<LeafManager>();
        foreach (var n in chain)
        {
            n.isDead       = true;
            n.isDeadwood   = true;
            n.isJin        = true;
            n.jinBleach    = 0f;      // fresh-stripped tan; weathers silver in DiebackPass
            n.health       = 0f;
            n.isGrowing    = false;
            n.hasBud       = false;
            n.hasFlowerBud = false;
            n.hasWire      = false;
            // Stripped wood carries no bark history: no wound, no callus, no cut-point cap
            // (the cap would force a flat stump ring and fight the jin spike taper).
            n.hasWound       = false;
            n.hadWoundScar   = false;
            n.isTrimCutPoint = false;
            lm?.DefoliateNode(n);
        }

        if (strippedOff.Count > 0) OnSubtreeTrimmed?.Invoke(strippedOff);
        CareLog.Add("Jin", "Stripped a branch to jin — bleached deadwood that tells the tree's history");
        Debug.Log($"[Jin] node={node.id} chain={chain.Count} strippedLaterals={strippedOff.Count} | year={GameManager.year}");
        RecalculateRadii(root);
        meshBuilder.SetDirty();
        cachedTreeHeight = CalculateTreeHeight();
    }

    // ── Trim undo data ────────────────────────────────────────────────────────

    class TrimUndoState
    {
        public TreeNode       subtreeRoot;
        public List<TreeNode> subtreeNodes;   // every node in the subtree
        public TreeNode       parent;

        // Parent state before the trim
        public bool    isTrimCutPoint;
        public int     trimCutDepth;
        public int     regrowthSeasonCount;
        public float   health;

        // Parent wound state before the trim (may have had a pre-existing wound)
        public bool    hasWound;
        public float   woundRadius;
        public Vector3 woundFaceNormal;
        public float   woundAge;
        public bool    pasteApplied;

        // Ancestor backBudStimulated states before the trim (up to 3)
        public List<(TreeNode node, bool wasStimulated)> ancestorStates;

        public float timestamp;   // Time.realtimeSinceStartup when the trim fired
    }

    TrimUndoState CaptureTrimUndoState(TreeNode subtreeRoot, TreeNode parent)
    {
        var s = new TrimUndoState
        {
            subtreeRoot  = subtreeRoot,
            subtreeNodes = new List<TreeNode>(),
            parent       = parent,
            timestamp    = Time.realtimeSinceStartup,
        };

        CollectSubtreeNodes(subtreeRoot, s.subtreeNodes);

        if (parent != null)
        {
            s.isTrimCutPoint      = parent.isTrimCutPoint;
            s.trimCutDepth        = parent.trimCutDepth;
            s.regrowthSeasonCount = parent.regrowthSeasonCount;
            s.health              = parent.health;
            s.hasWound            = parent.hasWound;
            s.woundRadius         = parent.woundRadius;
            s.woundFaceNormal     = parent.woundFaceNormal;
            s.woundAge            = parent.woundAge;
            s.pasteApplied        = parent.pasteApplied;
        }

        s.ancestorStates = new List<(TreeNode, bool)>();
        int count = 0;
        var anc = parent;
        while (anc != null && count < 3)
        {
            if (!anc.isRoot)
            {
                s.ancestorStates.Add((anc, anc.backBudStimulated));
                count++;
            }
            anc = anc.parent;
        }

        return s;
    }

    void CollectSubtreeNodes(TreeNode node, List<TreeNode> result)
    {
        result.Add(node);
        foreach (var child in node.children)
            CollectSubtreeNodes(child, result);
    }

}
