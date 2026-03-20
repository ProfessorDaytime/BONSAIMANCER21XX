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
    public float   length;          // current length (grows over time toward targetLength)

    // ── Growth ────────────────────────────────────────────────────────────────

    public float targetLength;      // the length this segment grows toward before branching
    public bool  isGrowing;         // true while length < targetLength
    public float age;               // accumulated grow-time (seconds, scaled by seasonal rate)

    // ── State ─────────────────────────────────────────────────────────────────

    public bool isTrimmed;          // trimmed nodes stop growing; their mesh remains
    public bool hasLeaves;          // true once leaves have been spawned at this tip

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
            if (children.Count == 0)
                return radius * 0.55f;

            float max = 0f;
            foreach (var child in children)
                max = Mathf.Max(max, child.radius);
            return max;
        }
    }

    // ── Wiring ────────────────────────────────────────────────────────────────

    public bool    hasWire;
    public Vector3 wireTargetDirection;
    public float   wireBendProgress;

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
