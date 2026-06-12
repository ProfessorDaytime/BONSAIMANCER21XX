using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Procedurally builds a mid-development Informal Upright (Moyogi) bonsai tree
/// directly into the scene's TreeSkeleton, bypassing the season-by-season growth system.
///
/// Menu: Bonsai → Generate Moyogi Debug Tree   (play-mode only)
///
/// Structure:
///   Trunk  — 7 segments following the Moyogi waypoints; apex ≈ 198 cm above base.
///   Scaffold (depth=1) — 6 primary branches at botanically correct heights/azimuths.
///   Sub-branches (depth=2) — 2–3 per scaffold, fanning perpendicular to parent.
///   Terminals — hasLeaves=true so LeafManager spawns foliage on the next cycle.
///
/// All nodes are frozen (isGrowing=false, subdivisionsLeft=0, hasBud=false).
/// The AutoStyler can begin maintenance immediately after generation.
/// </summary>
public static class MoyogiGenerator
{
    [MenuItem("Bonsai/Generate Moyogi Debug Tree")]
    static void Generate()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[MoyogiGen] Enter Play mode first.");
            return;
        }

        var skeleton = Object.FindFirstObjectByType<TreeSkeleton>();
        if (skeleton == null) { Debug.LogError("[MoyogiGen] No TreeSkeleton found."); return; }

        // ── Clear existing tree ───────────────────────────────────────────────────
        skeleton.ClearForRestart();

        int id        = 0;
        int birthYear = Mathf.Max(1, GameManager.year - 6);

        // ── Trunk ─────────────────────────────────────────────────────────────────
        // 7 segments. Each entry: (lean°, axis°, length, base-radius)
        // Axis convention: 0°=+Z front, 90°=+X right, 180°=-Z back, 270°=-X left.
        //
        // Shape: gentle "(" — leans left (270°) in lower half, corrects rightward
        // in upper half so apex ends roughly above base. Max drift ≈ 19cm left.
        // Lean angles 20–28° give clearly visible movement at game scale.
        var trunkSpec = new (float lean, float axis, float len, float rad)[]
        {
            ( 8f, 270f, 0.28f, 0.260f),   // base — slight left lean to start movement
            (28f, 270f, 0.36f, 0.200f),   // lean hard left — forms the belly of the curve
            (25f, 270f, 0.36f, 0.160f),   // still leaning left — peak of curve
            (18f, 270f, 0.34f, 0.124f),   // easing, still left
            (20f,  90f, 0.34f, 0.088f),   // lean right — correction toward apex-above-base
            (10f,  90f, 0.24f, 0.060f),   // gentler right lean, approaching vertical
            ( 2f,   0f, 0.20f, 0.040f),   // apex — nearly straight up
        };

        var trunk     = new List<TreeNode>(trunkSpec.Length);
        Vector3 pos   = Vector3.zero;
        TreeNode prev = null;

        foreach (var (lean, axis, len, rad) in trunkSpec)
        {
            var dir  = StyleDir(lean, axis);
            var node = Freeze(new TreeNode(id++, 0, pos, dir, rad, len, prev), birthYear);
            node.minRadius = rad;
            skeleton.allNodes.Add(node);
            if (prev == null) skeleton.root = node;
            else              prev.children.Add(node);
            trunk.Add(node);
            prev = node;
            pos  = node.tipPosition;
        }

        // Apex tip gets leaves (the top of the last trunk node)
        trunk[trunk.Count - 1].hasLeaves = true;

        // ── Scaffold Branches (depth=1) ───────────────────────────────────────────
        // (trunkNodeIndex, azimuth°, angleFromVertical°, length, baseRadius, subCount)
        //   azimuth: 0=+Z front, 90=+X right, 180=-Z back, 270=-X left
        //   angle:   0=straight up, 90=horizontal
        var scaffoldSpec = new (int ti, float az, float ang, float len, float rad, int subs)[]
        {
            (1, 270f, 80f, 0.52f, 0.110f, 3),   // First Branch  — left, lowest, longest
            (1, 190f, 75f, 0.34f, 0.076f, 2),   // Back Branch   — back-left, same tier
            (2,  60f, 70f, 0.44f, 0.092f, 3),   // Second Branch — right-front
            (3, 220f, 60f, 0.34f, 0.072f, 2),   // Third Branch  — back-left, mid
            (4,  40f, 50f, 0.26f, 0.056f, 2),   // Fourth Branch — right-front, upper
            (5, 300f, 40f, 0.20f, 0.040f, 2),   // Fifth Branch  — left, near apex
        };

        foreach (var (ti, az, ang, slen, srad, subCount) in scaffoldSpec)
        {
            var trunkNode = trunk[ti];
            var sdir      = StyleDir(ang, az);

            // Attach at the base of the trunk node (junction with trunk)
            var scaffold  = Freeze(new TreeNode(id++, 1, trunkNode.worldPosition, sdir, srad, slen, trunkNode), birthYear);
            scaffold.minRadius = srad;
            trunkNode.children.Add(scaffold);
            skeleton.allNodes.Add(scaffold);

            // Sub-branches fan perpendicular to scaffold direction
            float subRad = srad * 0.52f;
            for (int s = 0; s < subCount; s++)
            {
                // Position along scaffold: evenly spaced in the outer 55–90% of the branch
                float t      = 0.55f + (s / (float)subCount) * 0.35f;
                Vector3 sp   = scaffold.worldPosition + sdir * (slen * t);

                // Alternate azimuth offset to create a natural fan
                float subAz  = az + (s % 2 == 0 ? 55f : -65f);
                float subAng = Mathf.Clamp(ang - 25f, 20f, 70f);
                var   subDir = StyleDir(subAng, subAz);
                float subLen = slen * Mathf.Max(0.28f - s * 0.05f, 0.12f);

                var sub = Freeze(new TreeNode(id++, 2, sp, subDir, subRad, subLen, scaffold), birthYear);
                sub.minRadius  = subRad;
                sub.hasLeaves  = true;   // terminals get foliage
                scaffold.children.Add(sub);
                skeleton.allNodes.Add(sub);
            }
        }

        // ── Rebuild mesh + leaves ─────────────────────────────────────────────────
        skeleton.meshBuilder.SetDirty();

        // Snap camera out to a useful distance — the normal auto-zoom only triggers
        // during BranchGrow state, so statically generated trees appear tiny.
        Object.FindFirstObjectByType<CameraOrbit>()?.ResetViewForTree(pos.y);

        // Clear stale leaves; LeafManager will re-spawn from hasLeaves nodes next update
        skeleton.GetComponent<LeafManager>()?.ClearAllLeaves();

        int leafNodes = 0;
        foreach (var n in skeleton.allNodes) if (n.hasLeaves) leafNodes++;

        Debug.Log($"[MoyogiGen] Done — {skeleton.allNodes.Count} nodes " +
                  $"({trunk.Count} trunk | {scaffoldSpec.Length} scaffold | {leafNodes} leaf terminals) " +
                  $"| apex ≈ {pos:F2}");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Direction vector for a given lean angle from vertical and compass azimuth.
    /// Matches the StyleDefinition convention: 0°=+Z, 90°=+X.
    /// </summary>
    static Vector3 StyleDir(float leanDeg, float azimuthDeg)
    {
        float lean = leanDeg  * Mathf.Deg2Rad;
        float az   = azimuthDeg * Mathf.Deg2Rad;
        return new Vector3(
            Mathf.Sin(lean) * Mathf.Sin(az),
            Mathf.Cos(lean),
            Mathf.Sin(lean) * Mathf.Cos(az)
        ).normalized;
    }

    /// <summary>Freezes a node so the growth system leaves it alone this season.</summary>
    static TreeNode Freeze(TreeNode n, int birthYear)
    {
        n.length           = n.targetLength;
        n.isGrowing        = false;
        n.hasBud           = false;
        n.subdivisionsLeft = 0;
        n.birthYear        = birthYear;
        n.refinementLevel  = n.depth > 0 ? 1f : 0f;
        n.branchVigor      = 0.85f;
        n.health           = 1f;
        return n;
    }
}
