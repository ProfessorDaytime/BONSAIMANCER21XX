using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.InputSystem;
using Debug = UnityEngine.Debug;
using Random = UnityEngine.Random;

/// <summary>
/// TreeSkeleton — Health partial. Branch weight & sag (permanent + elastic), dieback, shading, tree death.
/// Split from the 6,373-line TreeSkeleton.cs (F5, 2026-07-03) with zero
/// behaviour change; all serialized fields remain in TreeSkeleton.cs.
/// </summary>
public partial class TreeSkeleton : MonoBehaviour
{
    // ── Branch Weight & Sag ───────────────────────────────────────────────────

    void BranchWeightPass()
    {
        if (root == null) return;

        // Pass 1: compute branchLoad bottom-up for every branch node
        ComputeLoad(root);

        // Pass 2: apply sag + junction stress top-down
        ApplySagAndStress(root);

        meshBuilder.SetDirty();
    }

    float ComputeLoad(TreeNode node)
    {
        if (node.isRoot) { node.branchLoad = 0f; return 0f; }

        float ownMass = node.radius * node.radius * node.length * woodDensity;
        float childLoad = 0f;
        foreach (var child in node.children)
            childLoad += ComputeLoad(child);

        node.branchLoad = ownMass + childLoad;
        return node.branchLoad;
    }

    void ApplySagAndStress(TreeNode node)
    {
        if (!node.isRoot && !node.isTrimmed && !node.isDead && !node.isDeadwood)
        {
            // Strength: radius³ × hardness × maturity (0→1 over matureAgeSeasons)
            float seasonsAlive = Mathf.Max(1f, GameManager.year - node.birthYear);
            float maturity = Mathf.Clamp01(seasonsAlive / matureAgeSeasons);

            // Apply species wood hardness if available
            float hardness = woodHardness;
            if (species != null) hardness *= species.budType == BudType.Opposite ? 0.85f : 1.0f; // deciduous slightly softer

            float strength = node.radius * node.radius * node.radius * hardness * maturity;
            strength = Mathf.Max(strength, 0.0001f);   // prevent divide-by-zero on seedlings

            float ratio = node.branchLoad / strength;

            // Accumulate sag angle
            float prevSag = node.sagAngleDeg;
            if (ratio > sagThreshold)
            {
                float deltaSag = (ratio - sagThreshold) * sagSensitivity;
                node.sagAngleDeg = Mathf.Min(node.sagAngleDeg + deltaSag, maxSagAngleDeg);
            }
            else
            {
                // Gradual recovery when branch is no longer overloaded
                node.sagAngleDeg = Mathf.Max(0f, node.sagAngleDeg - 0.5f);
            }

            // Queue only THIS season's sag gain, in absolute DEGREES toward down — never as a
            // Slerp fraction of the 180° up→down arc (the old math rotated a freshly
            // overloaded whip 108° in one season, past horizontal and into the soil).
            // Lifetime rotation stays bounded by maxSagAngleDeg×sagBlend (~21° at defaults).
            // The rotation itself is NOT applied here: it bleeds in over sagSpreadDays via
            // ApplyDailySag so the droop creeps through the season instead of snapping on
            // the spring frame. sagAngleDeg recovery reduces bookkeeping only; set wood
            // stays where it bent.
            float sagGain = node.sagAngleDeg - prevSag;
            if (sagGain > 0.01f)
            {
                node.pendingSagDeg += sagGain * sagBlend;
                node.sagDegPerDay   = node.pendingSagDeg / Mathf.Max(1f, sagSpreadDays);
            }

            // Junction stress: damage parent when this child is very heavy relative to it
            if (node.parent != null && !node.parent.isRoot && ratio > junctionStressThreshold)
            {
                ApplyDamage(node.parent, DamageType.JunctionStress, junctionStressDamage);
                if (verboseLog) Debug.Log($"[Weight] Junction stress on node {node.parent.id} from child {node.id} ratio={ratio:F2} | year={GameManager.year}");
            }
        }

        foreach (var child in node.children)
            ApplySagAndStress(child);
    }

    /// <summary>
    /// Bleeds queued seasonal sag (pendingSagDeg) into growDirection a little each in-game
    /// day, so droop creeps in over sagSpreadDays instead of snapping on the spring frame.
    /// Cost: one allNodes scan plus, only on days where something rotated, a single
    /// position propagation from the root and one mesh rebuild.
    /// </summary>
    void ApplyDailySag()
    {
        if (!branchWeightEnabled || root == null) return;

        bool any = false;
        Vector3 downLocal = transform.InverseTransformDirection(Vector3.down).normalized;
        foreach (var node in allNodes)
        {
            if (node.pendingSagDeg <= 0f) continue;
            if (node.isRoot || node.isTrimmed || node.isDead || node.isDeadwood)
            { node.pendingSagDeg = 0f; continue; }

            float step = Mathf.Min(node.sagDegPerDay, node.pendingSagDeg);
            if (step <= 0f) { node.pendingSagDeg = 0f; continue; }

            node.growDirection  = Vector3.RotateTowards(
                node.growDirection, downLocal, step * Mathf.Deg2Rad, 0f).normalized;
            node.pendingSagDeg -= step;
            any = true;
        }

        // Elastic leaf-load droop: the target angle tracks the CURRENT canopy weight on
        // each branch, so summer leaves ease branches down and autumn leaf-fall lets them
        // spring back up by the same amount. Rate-capped per day in both directions.
        if (leafMgr != null && elasticSagMaxDeg > 0f)
        {
            ComputeLeafLoad(root);
            Vector3 upLocal = transform.InverseTransformDirection(Vector3.up).normalized;
            foreach (var node in allNodes)
            {
                if (node.isRoot || node.isTrimmed || node.isDead || node.isDeadwood) continue;
                if (node.leafLoad <= 0f && node.elasticSagDeg <= 0f) continue;   // winter no-op

                float seasonsAlive = Mathf.Max(1f, GameManager.year - node.birthYear);
                float maturity     = Mathf.Clamp01(seasonsAlive / matureAgeSeasons);
                float strength     = Mathf.Max(node.radius * node.radius * node.radius * woodHardness * maturity, 0.0001f);

                float target = elasticSagMaxDeg *
                               Mathf.Clamp01(node.leafLoad / strength / Mathf.Max(0.01f, elasticFullLoadRatio));
                float delta  = Mathf.Clamp(target - node.elasticSagDeg, -elasticSagPerDayDeg, elasticSagPerDayDeg);
                if (Mathf.Abs(delta) < 0.01f) continue;

                node.growDirection = Vector3.RotateTowards(
                    node.growDirection, delta > 0f ? downLocal : upLocal,
                    Mathf.Abs(delta) * Mathf.Deg2Rad, 0f).normalized;
                node.elasticSagDeg += delta;
                any = true;
            }
        }

        if (any)
        {
            PropagatePositions(root);
            meshBuilder?.SetDirty();
        }
    }

    /// <summary>Bottom-up leaf mass per subtree (leafLoad), from live cluster counts.</summary>
    float ComputeLeafLoad(TreeNode node)
    {
        float sum = !node.isRoot && leafMgr != null
            ? leafMgr.NodeLeafCount(node.id) * leafMassEach
            : 0f;
        foreach (var child in node.children)
            sum += ComputeLeafLoad(child);
        node.leafLoad = sum;
        return sum;
    }

    /// <summary>Roots anchored to the ground or a rock (everything except air-layer roots,
    /// which physically hang from a branch). Their geometry must never be dragged along when
    /// an ancestor trunk node is rotated, sagged, or moved — they're planted in the world,
    /// not suspended from the canopy. Without this guard, wiring or sagging node 0 teleports
    /// the entire root system to the first trunk segment's tip (the "root spider" bug).</summary>
    static bool IsGroundAnchoredRoot(TreeNode n) => n.isRoot && !n.isAirLayerRoot;

    /// <summary>
    /// After a sag adjustment, walk all descendants and update worldPosition
    /// to stay connected to their parent's new tipPosition.
    /// </summary>
    void PropagatePositions(TreeNode node)
    {
        foreach (var child in node.children)
        {
            if (IsGroundAnchoredRoot(child)) continue;
            child.worldPosition = node.tipPosition;
            PropagatePositions(child);
        }
    }

    // ── Branch Dieback ────────────────────────────────────────────────────────

    /// <summary>
    /// Called each spring. Three passes:
    ///  1. Mark newly dead nodes (health == 0 and not already isDead).
    ///  2. Apply shading damage to interior branches with no leaf-bearing descendants.
    ///  3. Remove small dead branches that have lingered past deadSeasonsToDrop.
    /// Large dead branches become isDeadwood and remain permanently.
    /// </summary>
    void DiebackPass()
    {
        var toRemove = new List<TreeNode>();

        foreach (var node in allNodes)
        {
            if (node.isTrimmed || node.isRoot) continue;

            // Jin weathering: stripped deadwood bleaches from fresh tan to silver-grey
            // over ~8 years of sun (colour picked up on the next mesh rebuild).
            if (node.isJin && node.jinBleach < 1f)
                node.jinBleach = Mathf.Min(1f, node.jinBleach + 0.12f);

            // Pass 1: mark newly dead
            if (!node.isDead && node.health <= 0f)
            {
                node.isDead = true;
                node.isGrowing = false;
                node.hasBud = false;

                if (node.radius >= diebackThinRadius)
                {
                    // Large branch — stays as deadwood
                    node.isDeadwood = true;
                    if (verboseLog) Debug.Log($"[Dieback] Node {node.id} depth={node.depth} r={node.radius:F3} → DEADWOOD | year={GameManager.year}");
                }
                else
                {
                    if (verboseLog) Debug.Log($"[Dieback] Node {node.id} depth={node.depth} r={node.radius:F3} → dying (drops in {deadSeasonsToDrop} seasons) | year={GameManager.year}");
                }

                // Drop leaves from dead node immediately
                GetComponent<LeafManager>()?.DefoliateNode(node);
            }

            // Pass 2: tick shading counter and apply damage
            if (!node.isDead && !node.isTerminal)
            {
                bool hasLeafDescendant = NodeHasLeafDescendant(node);
                if (!hasLeafDescendant)
                {
                    node.shadedSeasons++;
                    if (node.shadedSeasons > shadingToleranceSeasons)
                    {
                        float shadeDmg = shadingDamagePerSeason * (node.shadedSeasons - shadingToleranceSeasons);
                        ApplyDamage(node, DamageType.Shading, shadeDmg);
                        if (verboseLog) Debug.Log($"[Dieback] Shading dmg={shadeDmg:F3} node={node.id} shadedSeasons={node.shadedSeasons} | year={GameManager.year}");
                    }
                }
                else
                {
                    node.shadedSeasons = 0;
                }
            }

            // Pass 3: tick dead season counter; schedule small dead branches for removal.
            // NEVER drop the trunk base — RemoveSubtree(root) deletes the whole tree and
            // leaves a zombie skeleton that keeps growing regenerated roots with no canopy
            // (found 2026-07-02: "[Dieback] Dropped dead branch node=0 (801 nodes removed)",
            // the black-spider quick-start corpses). Whole-tree death is EvaluateTreeDeath's
            // job; a dead trunk base just stays as standing deadwood until then.
            if (node.isDead && !node.isDeadwood && node != root)
            {
                node.deadSeasons++;
                if (node.deadSeasons >= deadSeasonsToDrop)
                    toRemove.Add(node);
            }
        }

        // Remove dropped branches (trim from tips inward to keep parent references valid)
        foreach (var node in toRemove)
        {
            if (!allNodes.Contains(node)) continue;   // already removed as part of a subtree
            var parent = node.parent;
            if (parent != null) parent.children.Remove(node);
            var removed = new List<TreeNode>();
            RemoveSubtree(node, removed);
            if (parent != null && parent.isTerminal)
                parent.isTrimCutPoint = false;   // no cut, just fell off
            Debug.Log($"[Dieback] Dropped dead branch node={node.id} ({removed.Count} nodes removed) | year={GameManager.year}");
        }

        if (toRemove.Count > 0)
        {
            RecalculateRadii(root);
            meshBuilder.SetDirty();
        }
    }

    /// <summary>Returns true if any descendant of node has leaves.</summary>
    bool NodeHasLeafDescendant(TreeNode node)
    {
        foreach (var child in node.children)
        {
            if (child.hasLeaves) return true;
            if (NodeHasLeafDescendant(child)) return true;
        }
        return false;
    }

    // ── Tree Death ────────────────────────────────────────────────────────────

    void EvaluateTreeDeath()
    {
        // Condition 1: average trunk health critically low for N consecutive seasons
        float trunkHealthSum = 0f;
        int   trunkCount     = 0;
        foreach (var node in allNodes)
        {
            if (node.isRoot || node.isTrimmed) continue;
            trunkHealthSum += node.health;
            trunkCount++;
        }
        float avgHealth = trunkCount > 0 ? trunkHealthSum / trunkCount : 1f;

        if (avgHealth < criticalHealthThreshold)
        {
            consecutiveCriticalSeasons++;
            Debug.Log($"[Death] Critical season {consecutiveCriticalSeasons}/{criticalSeasonsTodie} | avgHealth={avgHealth:F3} | year={GameManager.year}");
        }
        else
        {
            if (consecutiveCriticalSeasons > 0)
                Debug.Log($"[Death] Tree recovered — resetting critical counter | avgHealth={avgHealth:F3}");
            consecutiveCriticalSeasons = 0;
        }

        // Condition 2: living root count too low
        int livingRoots = 0;
        foreach (var node in allNodes)
            if (node.isRoot && !node.isTrimmed && node.health > 0f) livingRoots++;

        bool rootCollapse = trunkCount > 0 && livingRoots < minimumLivingRootNodes;

        // Warning state: one critical season away from death
        bool wasInDanger = treeInDanger;
        treeInDanger = consecutiveCriticalSeasons >= criticalSeasonsTodie - 1 || rootCollapse;
        if (treeInDanger && !wasInDanger)
            Debug.Log($"[Death] WARNING — tree is in danger! | year={GameManager.year}");

        // Trigger death
        if (consecutiveCriticalSeasons >= criticalSeasonsTodie)
        {
            KillTree("critical health");
            return;
        }
        if (rootCollapse)
        {
            KillTree("root loss");
        }

        OnNewGrowingSeason?.Invoke();
    }

    /// <summary>Human-readable cause of death — read by ButtonClicker to populate the death screen.</summary>
    public static string LastDeathCause { get; private set; } = "";

    public void KillTree(string cause)
    {
        var _ks = GameManager.Instance.state;
        if (_ks == GameState.TreeDead || _ks == GameState.SpeciesSelect ||
            _ks == GameState.LoadMenu  || _ks == GameState.Menu) return;

        LastDeathCause = cause;
        Debug.Log($"[Death] Tree is dead. Cause: {cause} | year={GameManager.year}");

        // Grey out all branch nodes visually via mesh tint
        var mb = GetComponent<TreeMeshBuilder>();
        if (mb != null) mb.SetDeadTint(true);

        // Drop all leaves and bark flakes immediately
        GetComponent<LeafManager>()?.DropAllLeaves();
        GetComponent<BarkFlakerManager>()?.ClearAllFlakes();

        GameManager.Instance.UpdateGameState(GameState.TreeDead);
    }

    // Year Simulation (debug keys 1-9)

    /// <summary>
    /// Instantly simulates one full year of growth with no animation.
    /// </summary>
    public void SimulateYear()
    {
        if (root == null) return;

        GameManager.year++;
        lastGrownYear = GameManager.year;
        GameManager.Instance.TextCallFunction();

        // Mirror StartNewGrowingSeason: growing-season guard + reserve depletion flush.
        bool simIsGrowingSeason = GameManager.month >= 3 && GameManager.month <= 8;
        if (simIsGrowingSeason)
        {
            foreach (var node in allNodes)
            {
                if (!node.isTrimCutPoint) continue;
                node.regrowthSeasonCount++;
                if (node.trimCutDepth + node.regrowthSeasonCount * EffectiveDepthsPerYear >= SeasonDepthCap)
                    node.isTrimCutPoint = false;
            }
            CheckCutCapAbsorption();
        }
        if (cutDepthAccumulatedThisSeason >= heavyPruneThreshold)
            heavyPruneRecoveryActive = true;
        cutDepthAccumulatedThisSeason = 0;

        foreach (var node in allNodes)
        {
            if (node.isGrowing && !node.isTrimmed)
            {
                node.length    = node.targetLength;
                node.isGrowing = false;
            }
        }

        var terminals = new List<TreeNode>();
        foreach (var node in allNodes)
        {
            bool belowCap = node.isRoot
                ? node.depth < maxRootDepth
                : node.depth < SeasonDepthCap && node.depth < CutPointDepthCap(node);

            if (!node.isTrimmed && node.isTerminal && !node.isGrowing && belowCap)
                terminals.Add(node);
        }
        foreach (var terminal in terminals)
            SpawnChildren(terminal);

        bool anyGrowing = true;
        while (anyGrowing)
        {
            anyGrowing = false;
            var growing = new List<TreeNode>();
            foreach (var node in allNodes)
                if (node.isGrowing && !node.isTrimmed) growing.Add(node);

            foreach (var node in growing)
            {
                bool belowCap = node.isRoot
                    ? node.depth < maxRootDepth
                    : node.depth < SeasonDepthCap && node.depth < CutPointDepthCap(node);

                anyGrowing     = true;
                node.length    = node.targetLength;
                node.isGrowing = false;
                // SimulateYear is instant — don't add depth-cap extensions here or
                // the while loop would spin forever. Only allow belowCap or mid-chain.
                bool canSpawn  = belowCap || (!node.isRoot && node.subdivisionsLeft > 0);
                if (canSpawn) SpawnChildren(node);
            }
        }

        RecalculateRadii(root);
        meshBuilder.SetDirty();
    }

}
