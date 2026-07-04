using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.InputSystem;
using Debug = UnityEngine.Debug;
using Random = UnityEngine.Random;

/// <summary>
/// TreeSkeleton — Init partial. Restart / InitTree / ClearForRestart / initial + regenerated roots.
/// Split from the 6,373-line TreeSkeleton.cs (F5, 2026-07-03) with zero
/// behaviour change; all serialized fields remain in TreeSkeleton.cs.
/// </summary>
public partial class TreeSkeleton : MonoBehaviour
{
    // ── Restart ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Destroys all tree visuals and clears node state so that the next Water
    /// event (when root == null) triggers a fresh InitTree(). Call from the
    /// dead-tree restart button before transitioning to SpeciesSelect.
    /// </summary>
    public void ClearForRestart()
    {
        CareLog.Clear();
        GetComponent<LeafManager>()?.ClearAllLeaves();
        foreach (var go in budObjects.Values)    if (go != null) Destroy(go);
        budObjects.Clear();
        foreach (var go in lateralBudObjects)    if (go != null) Destroy(go);
        lateralBudObjects.Clear();
        foreach (var go in woundObjects.Values)  if (go != null) Destroy(go);
        woundObjects.Clear();
        if (seedObject != null) { Destroy(seedObject); seedObject = null; }
        allNodes.Clear();
        root = null;
        meshBuilder?.SetDirty();
    }

    // Initialisation

    void InitTree()
    {
        foreach (var go in budObjects.Values)
            if (go != null) Destroy(go);
        budObjects.Clear();

        foreach (var go in lateralBudObjects)
            if (go != null) Destroy(go);
        lateralBudObjects.Clear();

        foreach (var go in woundObjects.Values)
            if (go != null) Destroy(go);
        woundObjects.Clear();

        // New tree = new node-id space (nextId restarts at 0). Managers that key state
        // by node id MUST be cleared or the previous game's ids poison the new tree —
        // found 2026-07-02: second-in-session Quick-Starts grew stunted because stale
        // shapedNodeIds skipped trunk wiring and stale nodeLeaves entries starved the
        // canopy (low treeEnergy).
        GetComponent<AutoStyler>()?.ResetForNewTree();
        GetComponent<LeafManager>()?.ResetForNewTree();

        allNodes.Clear();
        nextId               = 0;
        startYear            = GameManager.year;
        startMonth           = GameManager.month;
        lastGrownYear        = GameManager.year - 1;  // ensure OnGameStateChanged(BranchGrow) fires StartNewGrowingSeason
        lastSnapshotAbsDay  = -1;

        if (species != null) ApplySpecies();

        // Fresh seed = fresh pot. Reset the pot/soil state the previous game left on the
        // PotSoil component — the auto-styler may have advanced the pot to L and degraded
        // the soil, and a stale L-sized root box around a seedling lets new roots range
        // far outside the visual pot (2026-07-02 "starburst on new game" report; pot size
        // never reset). Loading a save is unaffected: the load path restores + applies
        // its own pot state after this.
        var potSoilReset = GetComponent<PotSoil>();
        if (potSoilReset != null)
        {
            potSoilReset.potSize = PotSoil.PotSize.XS;   // design start: smallest pot
            potSoilReset.ApplyPotSize(GetRootAreaTransform());
            potSoilReset.soilDegradation   = 0f;
            potSoilReset.saturationLevel   = 0f;
            potSoilReset.compaction        = 0f;
            potSoilReset.seasonsSinceRepot = 0;
            potSoilReset.ComputeDerivedProperties();
            potSoilReset.RefreshDrainageHoles();
            Debug.Log($"[Tree] InitTree — pot/soil reset to XS fresh | year={GameManager.year}");
        }

        // Create the seed visual -- an elongated sphere at the soil surface.
        // It disappears once the sprout grows past seedHideLength.
        if (seedObject != null) Destroy(seedObject);
        seedObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        seedObject.name = "_Seed";
        seedObject.transform.SetParent(transform, false);
        seedObject.transform.localPosition = Vector3.zero;
        seedObject.transform.localScale    = new Vector3(0.06f, 0.10f, 0.06f);
        // Remove the collider so it doesn't interfere with tree raycasts
        Destroy(seedObject.GetComponent<Collider>());
        // Use the same material as the trunk so it matches the bark shader.
        var seedRenderer = seedObject.GetComponent<Renderer>();
        if (seedRenderer != null)
        {
            var treeRend = meshBuilder?.GetComponent<MeshRenderer>();
            if (treeRend != null)
                seedRenderer.sharedMaterial = treeRend.sharedMaterial;
        }

        // The first trunk node starts at zero length and grows upward.
        // SpawnChildren will add (trunkSubdivisions - 1) more depth-0 segments
        // before allowing real branching, giving several individually wireable sections.
        float trunkSegLen = branchSegmentLength / Mathf.Max(1, trunkSubdivisions);
        root           = new TreeNode(nextId++, 0, Vector3.zero, Vector3.up,
                                      terminalRadius, trunkSegLen, null);
        root.isGrowing = true;
        allNodes.Add(root);

        // Sprout initial root strands evenly around the seed.
        // Roots also start at zero length and grow during BranchGrow seasons.
        int roots = Mathf.Max(0, initialRootCount);
        for (int i = 0; i < roots; i++)
        {
            float   angle   = (float)i / roots * Mathf.PI * 2f;
            Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 dir     = (outward + Vector3.down * rootInitialPitch).normalized;
            float   len     = Mathf.Max(rootSegmentLength * rootSegmentLengthDecay, 0.3f);
            var r           = CreateNode(root.worldPosition, dir, rootTerminalRadius, len, root);
            r.isRoot        = true;
        }

        CareLog.Add("Plant", $"Planted a new {SpeciesName} seed");
        Debug.Log($"[Tree] InitTree (seed) year={GameManager.year} | trunk growing | initialRoots={roots}");

        RecalculateRadii(root);
        meshBuilder.SetDirty();
    }

    // Root Planting

    /// <summary>Returns the rootAreaTransform for external systems (e.g. PotSoil sizing).</summary>
    public Transform GetRootAreaTransform() => rootAreaTransform;

    /// <summary>
    /// True when at least one non-trimmed root terminal has accumulated enough
    /// boundary pressure to be considered pot-bound.
    /// </summary>
    public bool IsPotBound()
    {
        foreach (var n in allNodes)
            if (n.isRoot && n.isTerminal && !n.isTrimmed && n.boundaryPressure >= boundaryPressureThreshold)
                return true;
        return false;
    }

    /// <summary>
    /// Plant a fresh evenly-spaced set of root strands from the trunk base.
    /// Called by RootRakeManager.ConfirmRepot() after discarding the old root graph.
    /// If withLongRoot is true, an extra pre-grown long root cable is added in a random direction.
    /// </summary>
    public void RegenerateInitialRoots(bool withLongRoot)
    {
        int count = Mathf.Max(1, initialRootCount);
        for (int i = 0; i < count; i++)
        {
            float   angle   = (float)i / count * Mathf.PI * 2f;
            Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            PlantRoot(outward);
        }

        if (withLongRoot)
        {
            float   angle = Random.Range(0f, Mathf.PI * 2f);
            Vector3 dir   = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            PlantLongRoot(dir);
            hasLongRoot = false;   // consumed
        }

        RecalculateRadii(root);
        meshBuilder.SetDirty();
        Debug.Log($"[Root] RegenerateInitialRoots count={count} withLongRoot={withLongRoot} | year={GameManager.year}");
    }

    /// <summary>
    /// Plants a multi-segment pre-grown root cable that starts longer than normal.
    /// Simulates the long surface root kept by the player during the rake mini-game.
    /// </summary>
    public void PlantLongRoot(Vector3 directionLocal)
    {
        if (root == null) return;
        Vector3 dir = (directionLocal + Vector3.down * rootInitialPitch).normalized;
        float   seg = Mathf.Max(rootSegmentLength * rootSegmentLengthDecay, 0.3f);

        TreeNode prev = root;
        for (int i = 0; i < 4; i++)
        {
            float segLen = seg * Mathf.Max(0.3f, 1f - i * 0.18f);
            var node = CreateNode(prev.tipPosition, dir, rootTerminalRadius, segLen, prev);
            node.isRoot = true;
            node.length = node.targetLength;   // pre-grown
            prev = node;
        }

        RecalculateRadii(root);
        meshBuilder.SetDirty();
        Debug.Log($"[Root] PlantLongRoot dir={directionLocal} | year={GameManager.year}");
    }

    /// <summary>
    /// Plants a new root strand from the base of the trunk.
    /// Called by TreeInteraction when the player clicks the planting surface in RootPrune mode.
    /// </summary>
    public void PlantRoot(Vector3 directionLocal)
    {
        if (root == null) return;

        Vector3 dir = (directionLocal + Vector3.down * rootInitialPitch).normalized;

        float len = Mathf.Max(rootSegmentLength * rootSegmentLengthDecay, 0.3f);

        var newRoot = CreateNode(root.worldPosition, dir, rootTerminalRadius, len, root);
        newRoot.isRoot = true;

        int totalRoots = 0;
        foreach (var n in allNodes) if (n.isRoot) totalRoots++;
        Debug.Log($"[Root] PlantRoot id={newRoot.id} | dir={dir} | len={len:F2} | totalRootNodes={totalRoots}");

        RecalculateRadii(root);
        meshBuilder.SetDirty();
    }

}
