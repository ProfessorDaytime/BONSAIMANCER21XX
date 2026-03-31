// BACKUP of PreGrowRootsToSoil — 2026-03-31
// Root placement confirmed WORKING by user at this state.
// DO NOT modify this file. It is a reference snapshot.
//
// Key behaviours locked in:
//   • baseWorld = startTip  (no entry-scan landing, no mesh gap)
//   • hasHitExterior gate   (underRock snap only before first exterior hit)
//   • edgeXZ fallback       (bounding-box edge when horizontal ray misses at equator)
//   • startNode.isGrowing = false after PreGrow (blocks SpawnChildren air roots)
//   • surfOffset = rootTerminalRadius * 2f
//
// Remaining issue at time of backup:
//   Post-exterior freeFall nodes drop straight down from equatorial XZ,
//   so lower-rock-face nodes float in space beside the narrowing rock bottom.
//   Fix: add downward-ray scan from edgeXZ in post-exterior branch.

/*
    void PreGrowRootsToSoil()
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

        float segLen      = rootSegmentLength * 0.5f;
        int   grown       = 0;
        float rockSearchR = rockCollider.bounds.extents.magnitude * 2.5f;

        var trunkRoots = new List<TreeNode>();
        foreach (var child in root.children)
            if (child.isRoot) trunkRoots.Add(child);

        int strandIndex = 0;
        foreach (var startNode in trunkRoots)
        {
            TreeNode current     = startNode;
            int      strandGrown = 0;

            // Phase 1: fast-forward to chain tip
            while (current.children.Count > 0)
                current = current.children[0];

            Vector3 existingTip = transform.TransformPoint(current.tipPosition);
            if (existingTip.y <= soilY + 0.05f) { strandIndex++; continue; }

            Vector3 startTip  = transform.TransformPoint(startNode.tipPosition);
            Vector3 strandDir = transform.TransformDirection(startNode.growDirection).normalized;
            Vector3 strandXZ  = new Vector3(strandDir.x, 0f, strandDir.z);
            if (strandXZ.sqrMagnitude < 0.001f) strandXZ = new Vector3(strandDir.x, strandDir.z, 0f);
            if (strandXZ.sqrMagnitude < 0.001f) strandXZ = Vector3.right;
            strandXZ = strandXZ.normalized;

            Vector3 rockCenter = rockCollider.bounds.center;
            float   rockTopY   = rockCollider.bounds.max.y;
            float   surfOffset = rootTerminalRadius * 2f;

            // edgeXZ: outer surface at equatorial Y (or bbox fallback)
            Vector3 horizOrig = rockCenter + strandXZ * rockSearchR;
            horizOrig.y = rockCenter.y;
            Vector3 edgeXZ;
            if (rockCollider.Raycast(new Ray(horizOrig, -strandXZ), out RaycastHit edgeHit, rockSearchR * 2f))
            {
                edgeXZ = edgeHit.point;
            }
            else
            {
                float projExtent = Mathf.Abs(strandXZ.x) * rockCollider.bounds.extents.x
                                 + Mathf.Abs(strandXZ.z) * rockCollider.bounds.extents.z;
                edgeXZ = new Vector3(rockCenter.x + strandXZ.x * projExtent,
                                     rockCenter.y,
                                     rockCenter.z + strandXZ.z * projExtent);
            }

            Vector3 baseWorld      = startTip;   // ← no entry scan; chain connects to trunk
            bool hasHitExterior    = false;

            for (int step = 0; step < 120; step++)
            {
                if (baseWorld.y <= soilY + 0.05f) break;

                float targetY = baseWorld.y - segLen;
                if (targetY < soilY) targetY = soilY;

                Vector3 scanOrig = rockCenter + strandXZ * rockSearchR;
                scanOrig.y = targetY;
                bool hitRock = rockCollider.Raycast(new Ray(scanOrig, -strandXZ), out RaycastHit hit, rockSearchR * 2f);

                Vector3 nodePos, tangent;
                string stepMode;

                if (hitRock)
                {
                    nodePos        = hit.point + hit.normal * surfOffset;
                    tangent        = nodePos - baseWorld;
                    tangent        = tangent.sqrMagnitude > 0.001f ? tangent.normalized : Vector3.down;
                    stepMode       = "exterior";
                    hasHitExterior = true;
                }
                else if (!hasHitExterior)
                {
                    Vector3 checkOrig = new Vector3(baseWorld.x, rockTopY + 0.5f, baseWorld.z);
                    float   checkDist = rockTopY - soilY + 1f;
                    bool underRock    = rockCollider.Raycast(new Ray(checkOrig, Vector3.down), out RaycastHit checkHit, checkDist)
                                       && checkHit.point.y > targetY;
                    if (underRock)
                    {
                        nodePos  = new Vector3(edgeXZ.x, targetY, edgeXZ.z);
                        tangent  = (nodePos - baseWorld).sqrMagnitude > 0.001f ? (nodePos - baseWorld).normalized : Vector3.down;
                        stepMode = "toEdge";
                    }
                    else
                    {
                        nodePos  = new Vector3(baseWorld.x, targetY, baseWorld.z);
                        tangent  = Vector3.down;
                        stepMode = "freeFall";
                    }
                }
                else
                {
                    // POST-EXTERIOR FREEFELL (the remaining issue):
                    // Drops straight down from last exterior XZ — floats beside lower rock face.
                    nodePos  = new Vector3(baseWorld.x, targetY, baseWorld.z);
                    tangent  = Vector3.down;
                    stepMode = "freeFall";
                }

                Vector3 localPos = transform.InverseTransformPoint(baseWorld);
                Vector3 localDir = transform.InverseTransformDirection(tangent).normalized;
                var newNode       = CreateNode(localPos, localDir, rootTerminalRadius, segLen, current);
                newNode.isRoot    = true;
                newNode.length    = segLen;
                newNode.isGrowing = false;
                newNode.radius    = rootTerminalRadius;
                current   = newNode;
                baseWorld = nodePos;
            }

            // Freeze startNode so Update's growth loop never fires SpawnChildren on it.
            if (startNode.isGrowing)
            {
                startNode.length    = startNode.targetLength;
                startNode.isGrowing = false;
                startNode.radius    = rootTerminalRadius;
                startNode.minRadius = rootTerminalRadius;
            }
            strandIndex++;
        }
        if (grown > 0) RecalculateRadii(root);
    }
*/
