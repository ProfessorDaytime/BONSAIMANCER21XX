using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pure data class representing one segment of the bonsai tree.
/// No MonoBehaviour — no GameObject, no Transform. All logic lives in TreeSkeleton.
///
/// Positions are in LOCAL space relative to the TreeSkeleton's GameObject transform.
/// A segment runs from worldPosition (base) to tipPosition (tip).
/// </summary>
public class TreeNode
{
    // ── Identity ──────────────────────────────────────────────────────────────

    public int id;
    public int depth;           // 0 = trunk base, increases toward tips

    // ── Geometry ──────────────────────────────────────────────────────────────

    public Vector3 worldPosition;   // base of this segment (local space)
    public Vector3 growDirection;   // unit vector pointing toward the tip
    public float   radius;          // radius at the base of this segment
    public float   minRadius;       // highest radius ever reached — pipe model can't shrink below this
    public float   length;          // current length (grows over time toward targetLength)

    // ── Growth ────────────────────────────────────────────────────────────────

    public float targetLength;      // the length this segment grows toward before branching
    public bool  isGrowing;         // true while length < targetLength
    public float age;               // accumulated grow-time (seconds, scaled by seasonal rate)

    // ── State ─────────────────────────────────────────────────────────────────

    public bool isTrimmed;          // trimmed nodes stop growing; their mesh remains
    public bool hasLeaves;          // true once leaves have been spawned at this tip
    public bool isRoot;             // part of the surface/sub-surface root system
    public int  subdivisionsLeft;   // subdivision segments remaining before branching is allowed (0 = ready to branch)
    public int   birthYear;          // GameManager.year when this node was created; used by LeafManager to identify new spring growth
    public float refinementLevel;   // 0 = raw growth. Increments on trim (+0.5) and back-bud activation (+0.25), caps at 6.
    public float branchVigor = 1f;  // per-node vigor multiplier (0.2–2.0). Apex/shallow nodes drift up; trimming and depth reduce it.
                                    // New growth inherits parent's level. Drives segment-length shortening: ×0.82 per level.

    // ── Post-trim regrowth cap ────────────────────────────────────────────────
    // When trimming creates a fresh stump, the surviving tip is marked as a
    // cut point. Regrowth from that point is limited to depthsPerYear levels per
    // season (same pacing as the first year of the tree's life), preventing a
    // hard prune from immediately regrowing to full depth in one season.

    // ── Bud system ────────────────────────────────────────────────────────────
    // Terminal buds are set at season end (August→September) and break in March.
    // Back-budding: trimming a tip stimulates dormant axillary buds on ancestors.

    public bool hasBud;              // terminal bud set; will activate next spring
    public bool backBudStimulated;   // tip ancestry was trimmed; boosted lateral chance next spring

    public bool isTrimCutPoint;      // this node is the exposed tip of a pruning cut
    public int  trimCutDepth;        // node.depth at the moment the cut was made
    public int  regrowthSeasonCount; // growing seasons elapsed since the cut

    // ── Graph ─────────────────────────────────────────────────────────────────

    public TreeNode        parent;
    public List<TreeNode>  children = new List<TreeNode>();

    public bool isTerminal => children.Count == 0;

    /// <summary>Tip position in local space.</summary>
    public Vector3 tipPosition => worldPosition + growDirection * length;

    /// <summary>
    /// Radius at the tip. Terminal nodes taper; branching nodes match the dominant child.
    /// </summary>
    public float tipRadius
    {
        get
        {
            // Cut-site cap: while the wound is open, keep the tip ring at the full
            // cut radius so the branch end looks like a flat sawn-off stump rather
            // than tapering toward the new small-radius regrowth shoots.
            if (isTrimCutPoint && hasWound)
                return radius;

            float terminalTip = radius * 0.55f;
            if (children.Count == 0) return terminalTip;

            // Find the dominant child (largest radius) and its growth progress.
            // Blend tipRadius from the tapered-terminal size toward the junction
            // size as the child grows, so new sub-segments emerge smoothly rather
            // than snapping the tip ring to a thinner radius at spawn time.
            float maxRadius   = 0f;
            float maxProgress = 0f;
            foreach (var child in children)
            {
                if (child.radius > maxRadius)
                {
                    maxRadius   = child.radius;
                    maxProgress = child.targetLength > 0f
                        ? Mathf.Clamp01(child.length / child.targetLength)
                        : 1f;
                }
            }

            // Clamp the lerp start so the tip ring is never narrower than the child's base.
            // Without this, thin branches (radius ≈ terminalRadius) taper to 0.55×radius
            // which is visually thinner than the child's base ring — making the child
            // look fatter than its parent even though the pipe model data is correct.
            float junctionStart = Mathf.Max(terminalTip, maxRadius);
            return Mathf.Lerp(junctionStart, maxRadius, maxProgress);
        }
    }

    // ── Health ────────────────────────────────────────────────────────────────
    // 0 = dead, 1 = fully healthy. Damage sources defined in DamageType enum.
    // Thresholds: <0.75 slowed growth, <0.25 dormant, <=0 dead.

    public float health = 1f;

    // ── Wiring ────────────────────────────────────────────────────────────────

    public bool    hasWire;
    public Vector3 wireOriginalDirection;  // growDirection at the moment of wiring
    public Vector3 wireTargetDirection;    // player-aimed target direction
    public float   wireSetProgress;        // 0→1: wood lignifying in new position
    public float   wireDamageProgress;     // 0→1: accumulates after fully set
    public float   wireAgeDays;            // total rate-adjusted in-game days on wire

    // ── Pot-bound pressure ────────────────────────────────────────────────────
    // Increments each season a root terminal spends near a tray/pot wall.
    // Above the threshold: growth slows, radius ticks up, and low-depth ancestors
    // get boosted lateral chances (the tree pushes new roots back toward the trunk).

    public int boundaryPressure;

    // ── Air Layering ──────────────────────────────────────────────────────────
    /// <summary>True for roots that developed from an air layer on the trunk.
    /// These render regardless of RootPrune mode since they sit above ground.</summary>
    public bool isAirLayerRoot;

    // ── Ishitsuki training wire ───────────────────────────────────────────────
    /// <summary>True for wires auto-generated by the Ishitsuki confirm step.
    /// These cannot be removed until wireSetProgress >= 1.0 (~2 growing seasons).</summary>
    public bool isTrainingWire;

    // ── Branch Weight & Sag ───────────────────────────────────────────────────
    public float branchLoad;       // accumulated downward force (own mass + children); computed each spring
    public float sagAngleDeg;      // current accumulated sag in degrees; bleeds into growDirection each spring

    // ── Dieback ───────────────────────────────────────────────────────────────
    public bool isDead;            // health hit 0 — no growth, no leaves; may become deadwood or fall
    public bool isDeadwood;        // large dead branch kept as structural deadwood (jin candidate)
    public int  shadedSeasons;     // consecutive seasons with no leaf-bearing descendants
    public int  deadSeasons;       // seasons elapsed since death (drives drop-off for small branches)

    // ── Fungal ────────────────────────────────────────────────────────────────

    public float fungalLoad;          // 0–1 infection severity; drains health above ~0.4
    public bool  isMycorrhizal;       // beneficial fungi network on this root node
    public int   healthySeasonsCount; // seasons at health>0.75 and fungalLoad<0.1 (mycorrhizal unlock)

    // ── Grafting ──────────────────────────────────────────────────────────────
    /// <summary>True while this terminal is the source of a pending approach graft.</summary>
    public bool isGraftSource;
    /// <summary>True for the bridge node created when an approach graft succeeds.</summary>
    public bool isGraftBridge;

    // ── Wound ─────────────────────────────────────────────────────────────────
    public bool    hasWound;
    public float   woundRadius;      // radius of the removed branch at the cut point
    public Vector3 woundFaceNormal;  // growDirection of the removed branch (cut face normal)
    public float   woundAge;         // growing seasons elapsed since the wound was made
    public bool    pasteApplied;     // player has sealed this wound with cut paste

    // ── Constructor ───────────────────────────────────────────────────────────

    public TreeNode(int id, int depth, Vector3 position, Vector3 direction, float radius, float targetLength, TreeNode parent)
    {
        this.id            = id;
        this.depth         = depth;
        this.worldPosition = position;
        this.growDirection = direction.normalized;
        this.radius        = radius;
        this.length        = 0f;
        this.targetLength  = targetLength;
        this.isGrowing     = true;
        this.age           = 0f;
        this.isTrimmed     = false;
        this.hasLeaves     = false;
        this.hasWire       = false;
        this.parent        = parent;
    }
}
