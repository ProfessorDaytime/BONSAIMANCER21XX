using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages the Repot Root-Raking mini-game.
///
/// Flow (rake-first, matching real practice — comb the old soil out, THEN pot up):
///   1. The Repot button lifts the tree and immediately calls EnterRakeMode(): the
///      root ball appears caked in a tessellated soil cylinder, parented to the tree
///      so it rides the lift. Pot-bound soil is compacted — cells take several passes.
///   2. TreeInteraction calls RakeAt(worldPos) on vertical mouse strokes over the soil.
///   3. Cells near the stroke break off as code-generated clods that tumble and fall
///      (manual gravity, destroyed below the pot), revealing and gradually
///      straightening the roots beneath. Over-raking bare cells can snap fine roots.
///   4. Confirm → ConfirmRepot() stores the raked fraction and opens the repot panel
///      (RootPrune state) to choose soil preset + pot size.
///   5. Preset click → ApplyRepot(): un-raked fraction becomes PotSoil.compaction
///      (first-season drainage/aeration debuff), clean sweep = small health bonus,
///      CareLog entry, PotSoil.Repot, old roots discarded and regenerated.
///
/// Surface-popping roots (cosmetic pot-bound visual) are also driven here.
/// Add this component to the same GameObject as TreeSkeleton.
/// </summary>
[RequireComponent(typeof(TreeSkeleton))]
public class RootRakeManager : MonoBehaviour
{
    // ── Inspector ──────────────────────────────────────────────────────────────

    [Header("Soil Ball")]
    [Tooltip("Approximate number of voxel cells the soil ball is tessellated into. The ball is a " +
             "SOLID ellipsoid of cells — raking removes cells anywhere (top included) and the " +
             "cells behind become the new surface, so the ball always reads as a solid shape.")]
    [SerializeField] int targetChunkCount = 110;
    [Tooltip("Opaque soil-coloured material for the ball (also used for the falling chunks).")]
    [SerializeField] Material soilMaterial;
    [Tooltip("Optional authored dirt-clod mesh used for every soil cell AND falling chunk, replacing " +
             "the built-in cubes. Auto-centered and auto-scaled to the cell size; every cell gets a " +
             "stable random rotation on all axes. Keep it low-poly — the ball collider rebakes from " +
             "the combined mesh on every rake stroke. Leave empty for the default cubes.")]
    [SerializeField] Mesh dirtCellMesh;
    [Tooltip("World-space radius of the rake brush (cells within this distance of the hit point are removed).")]
    [SerializeField] float rakeBrushRadius = 0.35f;

    [Header("Soil Chunks")]
    [Tooltip("Manual gravity (units/s²) on knocked-loose soil chunks. No Rigidbodies used.")]
    [SerializeField] float chunkGravity = 5f;
    [Tooltip("How far below the pot a falling chunk is destroyed (world units).")]
    [SerializeField] float chunkCullDepth = 4f;

    [Header("Difficulty & Scoring")]
    [Tooltip("Rake passes needed to free a cell when the tree is pot-bound (compacted soil).")]
    [SerializeField] int potBoundHitsPerCell = 2;
    [Tooltip("Real-seconds cooldown between fine-root snaps while over-raking bare cells.")]
    [SerializeField] float overRakeSnapCooldown = 0.75f;
    [Tooltip("Chance per over-rake stroke (past cooldown) of snapping a fine root.")]
    [Range(0f, 1f)] [SerializeField] float overRakeSnapChance = 0.25f;

    [Header("Root Straightening")]
    [Tooltip("Speed at which revealed root nodes lerp toward their radial target positions.")]
    [SerializeField] float straightenSpeed = 1.5f;
    [Tooltip("Root nodes deeper than this depth are trimmed when rake mode starts.")]
    [SerializeField] int rakeMaxDepth = 3;

    [Header("Surface Roots (Pot-Bound Visual)")]
    [Tooltip("Number of surface-popping root nubs to spawn when pot-bound.")]
    [SerializeField] int surfaceRootCount = 4;
    [Tooltip("How far above soil the nubs poke (world units).")]
    [SerializeField] float surfaceRootPeak = 0.10f;
    [Tooltip("Growth speed of root-nub animation (units per frame).")]
    [SerializeField] float surfaceRootSpeed = 0.003f;
    [Tooltip("Material for the surface nubs. Leave null to fall back to soilMaterial.")]
    [SerializeField] Material surfaceRootMaterial;

    // ── Private ───────────────────────────────────────────────────────────────

    TreeSkeleton    skeleton;
    PotSoil         potSoil;
    TreeMeshBuilder treeMeshBuilder;

    // Soil ball — parented to the tree so it rides the lift animation; cell centers are
    // stored in ball-local space and transformed on demand.
    GameObject  soilBallGO;
    MeshFilter  soilMF;
    MeshCollider soilMC;
    bool[]      cellRemoved;
    int[]       cellHits;          // rake passes received per cell (pot-bound cells need several)
    Vector3[]   cellLocalCenters;
    Vector3Int[] cellCoords;       // grid coordinates for 6-neighbour connectivity
    readonly Dictionary<Vector3Int, int> cellByCoord = new();
    float       nextIslandCheck;   // throttle — also catches root prune-clicks unanchoring islands
    int         numCells;

    // Authored dirt mesh (dirtCellMesh) baked per cell — cached source data + per-cell
    // rotations rolled once at build so the ball doesn't shimmer on every rebuild.
    Quaternion[] cellRotations;
    Vector3[]    dirtVerts, dirtNorms;
    int[]        dirtTris;
    Vector3      dirtCenter;
    float        dirtScaleToCell = 1f;
    int         hitsToRemove = 1;  // 1 normally; potBoundHitsPerCell when pot-bound
    float       soilRadius, soilHeight;
    float       cellSize;          // voxel edge length, derived from ball volume / targetChunkCount
    Vector3     soilWorldCenter;   // ball center at build time (chunk impulse reference)

    // Shared box topology (corner bits: x=1, y=2, z=4). Every triangle individually
    // shoelace-verified clockwise-from-outside against Unity's documented front-face
    // winding (Mesh docs quad example).
    static readonly int[] BoxTris =
    {
        0,2,3, 0,3,1,   // -z
        4,5,7, 4,7,6,   // +z
        0,1,5, 0,5,4,   // -y
        2,6,7, 2,7,3,   // +y
        0,4,6, 0,6,2,   // -x
        1,3,7, 1,7,5,   // +x
    };

    // Same topology as quads (one per face, CW from outside) for flat-shaded cells
    static readonly int[][] FaceQuads =
    {
        new[] { 0, 2, 3, 1 },   // -z
        new[] { 4, 5, 7, 6 },   // +z
        new[] { 0, 1, 5, 4 },   // -y
        new[] { 2, 6, 7, 3 },   // +y
        new[] { 0, 4, 6, 2 },   // -x
        new[] { 1, 3, 7, 5 },   // +x
    };
    static readonly Vector3[] FaceNormals =
    { Vector3.back, Vector3.forward, Vector3.down, Vector3.up, Vector3.left, Vector3.right };

    Vector3 CellWorld(int i) =>
        soilBallGO != null ? soilBallGO.transform.TransformPoint(cellLocalCenters[i])
                           : cellLocalCenters[i];

    // Falling chunks (code-generated meshes, manual ballistic animation — no Rigidbodies)
    class FallingChunk { public GameObject go; public Mesh mesh; public Vector3 vel; public Vector3 spin; }
    readonly List<FallingChunk> fallingChunks = new();
    float chunkCullY;

    // Over-rake scoring
    int   fineRootsSnapped;
    float nextOverRakeSnapTime;

    // Result of the most recent completed rake this visit (-1 = none); consumed by ApplyRepot
    float lastRakedFraction = -1f;

    // Root reveal / straighten list
    struct RakeRoot
    {
        public TreeNode node;
        public Vector3  targetLocal;  // radial straight-out target in tree local space
        public float    revealT;      // 0 = covered, 1 = fully straightened
    }
    readonly List<RakeRoot> rakeRoots = new();

    // Surface root coroutines / GameObjects
    readonly List<Coroutine>  surfaceCoroutines = new();
    readonly List<GameObject> surfaceRootGOs    = new();
    bool surfaceRootsSpawned;

    // ── Public state ──────────────────────────────────────────────────────────

    public bool       InRakeMode  { get; private set; }
    public GameObject SoilBallGO  => soilBallGO;
    public int        FineRootsSnapped => fineRootsSnapped;

    // ── Unity ─────────────────────────────────────────────────────────────────

    void Awake()
    {
        skeleton        = GetComponent<TreeSkeleton>();
        potSoil         = GetComponent<PotSoil>();
        treeMeshBuilder = GetComponent<TreeMeshBuilder>();
    }

    void Update()
    {
        UpdateFallingChunks();   // chunks keep falling even after rake mode exits

        // Player left root mode some other way (ESC, Repot button toggle, state change)
        // while raking — clean up the ball and discard this visit's results.
        if (InRakeMode && GameManager.Instance != null &&
            GameManager.Instance.state != GameState.RootRake)
        {
            InRakeMode = false;
            DestroySoilBall();
            rakeRoots.Clear();
            lastRakedFraction = -1f;
        }

        // Periodic island sweep — catches root prune-clicks (handled in TreeInteraction)
        // that may have cut the root holding an island in place.
        if (InRakeMode && Time.time >= nextIslandCheck)
        {
            nextIslandCheck = Time.time + 0.5f;
            BreakOffIslands();
        }

        if (!InRakeMode || rakeRoots.Count == 0) return;

        bool dirty = false;
        float dt = Time.deltaTime;

        for (int i = 0; i < rakeRoots.Count; i++)
        {
            var rr = rakeRoots[i];
            if (rr.revealT <= 0f) continue;

            Vector3 prev = rr.node.worldPosition;
            rr.node.worldPosition = Vector3.Lerp(prev, rr.targetLocal,
                                                  dt * straightenSpeed * rr.revealT);
            if ((rr.node.worldPosition - prev).sqrMagnitude > 1e-8f) dirty = true;
            rakeRoots[i] = rr;
        }

        if (dirty) treeMeshBuilder?.SetDirty();
    }

    // ── Pot-Bound Cosmetic Roots ──────────────────────────────────────────────

    /// <summary>
    /// Called by TreeSkeleton the first time the tree becomes pot-bound.
    /// Nudges the tree up slightly and spawns surface-popping root nubs.
    /// </summary>
    public void OnBecamePotBound()
    {
        if (surfaceRootsSpawned) return;
        surfaceRootsSpawned = true;
        StartCoroutine(NudgeTreeUp(0.12f));
        for (int i = 0; i < surfaceRootCount; i++)
            surfaceCoroutines.Add(StartCoroutine(SpawnSurfaceRoot(i)));
    }

    IEnumerator NudgeTreeUp(float amount)
    {
        float startY  = transform.position.y;
        float targetY = startY + amount;
        for (float t = 0f; t < 1f; t = Mathf.MoveTowards(t, 1f, Time.deltaTime * 0.12f))
        {
            var p = transform.position;
            p.y = Mathf.Lerp(startY, targetY, t);
            transform.position = p;
            yield return null;
        }
    }

    IEnumerator SpawnSurfaceRoot(int index)
    {
        yield return new WaitForSeconds(index * 0.9f + Random.Range(0f, 0.4f));

        float angle = Random.Range(0f, Mathf.PI * 2f);
        float dist  = Random.Range(0.15f, 0.55f);
        Vector3 soilBase = skeleton.plantingSurfacePoint
                         + new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);

        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = "SurfaceRoot";
        go.transform.localScale = new Vector3(0.025f, 0.025f, 0.025f);
        var rend = go.GetComponent<Renderer>();
        if (rend != null)
            rend.material = surfaceRootMaterial != null ? surfaceRootMaterial : soilMaterial;
        Destroy(go.GetComponent<Collider>());
        surfaceRootGOs.Add(go);

        float baseY = soilBase.y - 0.03f;
        float peak  = soilBase.y + surfaceRootPeak;
        float cur   = baseY;
        go.transform.position = new Vector3(soilBase.x, cur, soilBase.z);

        while (cur < peak)
        {
            if (go == null) yield break;
            cur += surfaceRootSpeed;
            go.transform.position = new Vector3(soilBase.x, cur, soilBase.z);
            yield return null;
        }

        yield return new WaitForSeconds(Random.Range(0.5f, 2.5f));

        while (cur > baseY - 0.025f)
        {
            if (go == null) yield break;
            cur -= surfaceRootSpeed * 0.6f;
            go.transform.position = new Vector3(soilBase.x, cur, soilBase.z);
            yield return null;
        }
        // Stays just below surface — destroyed when rake starts
    }

    void DestroySurfaceRoots()
    {
        foreach (var c in surfaceCoroutines) if (c != null) StopCoroutine(c);
        surfaceCoroutines.Clear();
        foreach (var go in surfaceRootGOs) if (go != null) Destroy(go);
        surfaceRootGOs.Clear();
    }

    // ── Enter / Exit Rake Mode ────────────────────────────────────────────────

    /// <summary>
    /// Begin the rake step. Called by buttonClicker the moment the Repot button lifts
    /// the tree: the root ball appears caked in old soil, ready to be combed clean.
    /// </summary>
    public void EnterRakeMode()
    {
        if (InRakeMode) return;

        // Pot-bound = the old soil is compacted hard around the root ball: every cell
        // needs several rake passes. A routine repot frees cells in one pass.
        hitsToRemove         = skeleton.IsPotBound() ? Mathf.Max(1, potBoundHitsPerCell) : 1;
        fineRootsSnapped     = 0;
        nextOverRakeSnapTime = 0f;
        lastRakedFraction    = -1f;

        DestroySurfaceRoots();
        TrimDeepRoots();
        BuildSoilBall();
        BuildRakeRootList();

        InRakeMode = true;
        GameManager.Instance.UpdateGameState(GameState.RootRake);
        Debug.Log($"[RootRake] Entered rake mode | roots={rakeRoots.Count} cells={numCells} hitsPerCell={hitsToRemove} | year={GameManager.year}");
    }

    /// <summary>
    /// Player finished raking. Stores the result and opens the repot panel, where
    /// choosing a soil preset calls ApplyRepot() to complete the job.
    /// </summary>
    public void ConfirmRepot()
    {
        lastRakedFraction = SoilRemovedFraction();
        ExitRakeMode(toRepotPanel: true);
        Debug.Log($"[RootRake] Raking finished | raked={lastRakedFraction * 100f:F0}% snapped={fineRootsSnapped} | year={GameManager.year}");
    }

    /// <summary>
    /// Final step, called by buttonClicker when a soil preset is chosen in the repot
    /// panel: applies rake scoring (compaction, clean-sweep bonus), the new soil and
    /// pot, then discards the old roots and regenerates a clean set.
    /// </summary>
    public void ApplyRepot(PotSoil.SoilPreset preset, PotSoil.PotSize size, bool sizeChanged)
    {
        // Check for a notably long root (> 1.5× average terminal length)
        float total = 0f; int cnt = 0;
        foreach (var n in skeleton.allNodes)
            if (n.isRoot && n.isTerminal && !n.isTrimmed) { total += n.length; cnt++; }
        float avg = cnt > 0 ? total / cnt : 1f;
        skeleton.hasLongRoot = false;
        foreach (var n in skeleton.allNodes)
        {
            if (n.isRoot && n.isTerminal && !n.isTrimmed && n.length > avg * 1.5f)
            {
                skeleton.hasLongRoot = true;
                break;
            }
        }

        // Scoring: un-raked soil stays compacted around the root ball in the new pot
        // (reduced drainage/aeration for the first season); a clean sweep with no
        // snapped roots gives the whole tree a small health bonus.
        float removed = Mathf.Clamp01(Mathf.Max(0f, lastRakedFraction));
        potSoil.compaction = Mathf.Clamp01(1f - removed);

        bool cleanSweep = removed >= 0.95f && fineRootsSnapped == 0;
        if (cleanSweep)
            foreach (var n in skeleton.allNodes)
                if (!n.isRoot && !n.isTrimmed && !n.isDead)
                    n.health = Mathf.Min(1f, n.health + 0.03f);

        CareLog.Add("Repot",
            $"Repotted ({size} pot) — raked {removed * 100f:F0}% of the old soil" +
            (fineRootsSnapped > 0
                ? $", snapped {fineRootsSnapped} fine root{(fineRootsSnapped == 1 ? "" : "s")} over-raking"
                : cleanSweep ? " — clean sweep" : "") +
            (potSoil.compaction > 0.3f ? "; the leftover soil sits compacted this season" : ""));

        potSoil.Repot(skeleton, preset, size, sizeChanged);
        skeleton.GetComponent<LeafManager>()?.RefreshFungalTint(skeleton);

        DiscardAndRegenerateRoots();
        lastRakedFraction = -1f;
        fineRootsSnapped  = 0;

        // Leave root work entirely — tree settles back into the (new) pot.
        if (GameManager.IsRootLiftActive(GameManager.Instance.state))
            GameManager.Instance.ToggleRootPrune();
        Debug.Log($"[RootRake] Repot applied | compaction={potSoil.compaction:F2} | year={GameManager.year}");
    }

    /// <summary>Abort the repot from the rake step — tree settles back, nothing applied.</summary>
    public void CancelRakeMode()
    {
        ExitRakeMode(toRepotPanel: false);
    }

    void ExitRakeMode(bool toRepotPanel)
    {
        InRakeMode = false;
        DestroySoilBall();
        rakeRoots.Clear();

        if (toRepotPanel)
            GameManager.Instance.UpdateGameState(GameState.RootPrune); // pick soil + pot size
        else
            GameManager.Instance.ToggleRootPrune();                    // leave root work entirely
    }

    // ── Raking ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by TreeInteraction when a vertical mouse drag hits the soil ball surface.
    /// worldHitPoint: the world-space surface point from the raycast.
    /// </summary>
    public void RakeAt(Vector3 worldHitPoint)
    {
        if (!InRakeMode || soilBallGO == null) return;

        bool changed = false; int bareHits = 0;
        for (int i = 0; i < numCells; i++)
        {
            if (Vector3.Distance(CellWorld(i), worldHitPoint) > rakeBrushRadius) continue;
            if (cellRemoved[i]) { bareHits++; continue; }   // raking soil that's already gone

            if (++cellHits[i] >= hitsToRemove)
            {
                cellRemoved[i] = true;
                changed = true;
                SpawnChunk(CellWorld(i));
            }
        }

        // Over-raking bare cells works the rake through exposed roots — occasionally one
        // snaps: visible strand loss plus a small health sting on its parent.
        if (bareHits > 0 && Time.time >= nextOverRakeSnapTime && Random.value < overRakeSnapChance)
        {
            nextOverRakeSnapTime = Time.time + overRakeSnapCooldown;
            TrySnapFineRoot(worldHitPoint);
        }

        if (!changed) return;
        RebuildSoilMesh();
        UpdateRootReveal();
        BreakOffIslands();
    }

    /// <summary>
    /// Any clump of cells no longer connected to the core (the clump around the trunk
    /// column) breaks off and falls — unless a living root still runs through it, which
    /// holds it in place as an island until that root is cut.
    /// </summary>
    void BreakOffIslands()
    {
        if (soilBallGO == null) return;

        // A cell only stays if it's connected (through the cell grid) to something that
        // actually holds it up: a living root passing through it, or the trunk column.
        // Seed the "keep" set with every directly-anchored cell and flood from all of them.
        // (The old code kept whatever clump was nearest the ball centre regardless of
        // support — so once the trunk cells were raked, a floating island could become that
        // "core" and was protected forever. This fixes that.)
        var keep  = new bool[numCells];
        var queue = new Queue<int>();
        var ballT = soilBallGO.transform;

        Vector3 trunkBaseWorld = skeleton.root != null
            ? transform.TransformPoint(skeleton.root.worldPosition)
            : transform.position;
        float trunkColR2 = (cellSize * 1.5f) * (cellSize * 1.5f);
        float rootReach  = cellSize * 0.75f;

        for (int i = 0; i < numCells; i++)
        {
            if (cellRemoved[i]) continue;
            if (CellAnchored(i, trunkBaseWorld, trunkColR2, rootReach, ballT))
            { keep[i] = true; queue.Enqueue(i); }
        }

        // Fallback: if nothing is anchored (e.g. every root was trimmed mid-rake), keep the
        // clump nearest the trunk so the tree isn't left floating in mid-air.
        if (queue.Count == 0)
        {
            int core = -1; float best = float.MaxValue;
            for (int i = 0; i < numCells; i++)
            {
                if (cellRemoved[i]) continue;
                float d = cellLocalCenters[i].sqrMagnitude;
                if (d < best) { best = d; core = i; }
            }
            if (core < 0) return;
            keep[core] = true; queue.Enqueue(core);
        }

        // Flood the keep set over 6-neighbour grid adjacency.
        while (queue.Count > 0)
        {
            var c = cellCoords[queue.Dequeue()];
            for (int n = 0; n < 6; n++)
            {
                if (!cellByCoord.TryGetValue(c + Neigh[n], out int j)) continue;
                if (cellRemoved[j] || keep[j]) continue;
                keep[j] = true; queue.Enqueue(j);
            }
        }

        // Anything not reached by an anchor falls.
        bool broke = false;
        for (int i = 0; i < numCells; i++)
        {
            if (cellRemoved[i] || keep[i]) continue;
            cellRemoved[i] = true;
            SpawnChunk(CellWorld(i));
            broke = true;
        }

        if (broke)
        {
            RebuildSoilMesh();
            UpdateRootReveal();
        }
    }

    static readonly Vector3Int[] Neigh =
    {
        new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1),
    };

    /// <summary>True if a cell is directly held up — in the trunk column, or with a living
    /// root passing through it. Such cells (and anything grid-connected to them) survive.</summary>
    bool CellAnchored(int i, Vector3 trunkBaseWorld, float trunkColR2, float rootReach, Transform ballT)
    {
        // Trunk column: the trunk physically grips the soil right around its base.
        Vector3 w = CellWorld(i);
        float dx = w.x - trunkBaseWorld.x, dz = w.z - trunkBaseWorld.z;
        if (dx * dx + dz * dz < trunkColR2) return true;

        // Root proximity: a living root segment runs through this cell.
        Vector3 cellLocal = cellLocalCenters[i];
        foreach (var n in skeleton.allNodes)
        {
            if (!n.isRoot || n.isTrimmed || n == skeleton.root) continue;
            Vector3 a = ballT.InverseTransformPoint(transform.TransformPoint(n.worldPosition));
            Vector3 b = ballT.InverseTransformPoint(transform.TransformPoint(n.tipPosition));
            if (DistPointSegment(cellLocal, a, b) < rootReach) return true;
        }
        return false;
    }

    static float DistPointSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float t = Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f);
        return Vector3.Distance(p, a + ab * Mathf.Clamp01(t));
    }

    void TrySnapFineRoot(Vector3 worldHitPoint)
    {
        TreeNode best = null; float bestDist = rakeBrushRadius * 2f;
        foreach (var n in skeleton.allNodes)
        {
            if (!n.isRoot || !n.isTerminal || n.isTrimmed || n == skeleton.root) continue;
            float d = Vector3.Distance(transform.TransformPoint(n.worldPosition), worldHitPoint);
            if (d < bestDist) { bestDist = d; best = n; }
        }
        if (best == null) return;

        var parent = best.parent;
        skeleton.TrimNode(best);
        if (parent != null) skeleton.ApplyDamage(parent, DamageType.TrimTrauma, 0.01f);
        fineRootsSnapped++;
        treeMeshBuilder?.SetDirty();
        BreakOffIslands();   // the snapped root may have been holding an island up
        Debug.Log($"[RootRake] Fine root snapped (over-raking) — total={fineRootsSnapped}");
    }

    /// <summary>Fraction of soil cells removed [0–1], used by the UI progress label.</summary>
    public float SoilRemovedFraction()
    {
        if (numCells == 0) return 0f;
        int n = 0;
        for (int i = 0; i < numCells; i++) if (cellRemoved[i]) n++;
        return (float)n / numCells;
    }

    /// <summary>Count of living root terminal nodes (for UI display).</summary>
    public int RootStrandCount()
    {
        int n = 0;
        foreach (var node in skeleton.allNodes)
            if (node.isRoot && node.isTerminal && !node.isTrimmed) n++;
        return n;
    }

    void SpawnChunk(Vector3 worldPos)
    {
        // Falling chunk: the authored dirt mesh when assigned (shared asset — never
        // destroyed), otherwise a code-generated jittered box. No physics components;
        // UpdateFallingChunks animates it ballistically until it drops chunkCullDepth
        // below the pot, then destroys the GameObject (and the mesh only if generated).
        var go = new GameObject("SoilChunk");
        go.transform.position = worldPos;
        go.transform.rotation = Random.rotation;

        Mesh mesh = null;   // stays null for the shared authored mesh → not destroyed on cull
        if (dirtVerts != null)
        {
            go.AddComponent<MeshFilter>().sharedMesh = dirtCellMesh;
            go.transform.localScale = Vector3.one * (dirtScaleToCell * Random.Range(0.85f, 1.05f));
        }
        else
        {
            mesh = BuildChunkMesh(cellSize * Random.Range(0.85f, 1.05f));
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
        }
        go.AddComponent<MeshRenderer>().sharedMaterial = soilMaterial;

        // Knocked away from the soil ball (≈ away from the stroke), with a little pop upward
        Vector3 ballCenter = soilBallGO != null ? soilBallGO.transform.position : soilWorldCenter;
        Vector3 outward = (worldPos - ballCenter); outward.y = 0f;
        outward = outward.sqrMagnitude > 0.001f ? outward.normalized : Random.insideUnitSphere;
        fallingChunks.Add(new FallingChunk
        {
            go   = go,
            mesh = mesh,
            vel  = outward * Random.Range(0.4f, 1.0f) + Vector3.up * Random.Range(0.3f, 0.9f),
            spin = Random.insideUnitSphere * Random.Range(90f, 320f),
        });
    }

    /// <summary>Low-poly soil clod: a box with every corner jittered ±35% and slightly squashed.</summary>
    static Mesh BuildChunkMesh(float size)
    {
        var corners = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            Vector3 c = new(
                (i & 1) == 0 ? -1f : 1f,
                (i & 2) == 0 ? -0.7f : 0.7f,   // squashed vertically — clods aren't cubes
                (i & 4) == 0 ? -1f : 1f);
            corners[i] = Vector3.Scale(c, Vector3.one * (size * 0.5f))
                       + Random.insideUnitSphere * size * 0.175f;
        }

        var mesh = new Mesh { name = "SoilChunk" };
        mesh.vertices  = corners;
        mesh.triangles = BoxTris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    void UpdateFallingChunks()
    {
        if (fallingChunks.Count == 0) return;
        float dt = Time.deltaTime;
        for (int i = fallingChunks.Count - 1; i >= 0; i--)
        {
            var c = fallingChunks[i];
            if (c.go == null)
            {
                if (c.mesh != null) Destroy(c.mesh);
                fallingChunks.RemoveAt(i); continue;
            }

            c.vel.y -= chunkGravity * dt;
            c.go.transform.position += c.vel * dt;
            c.go.transform.Rotate(c.spin * dt, Space.Self);

            if (c.go.transform.position.y < chunkCullY)
            {
                Destroy(c.go);
                if (c.mesh != null) Destroy(c.mesh);
                fallingChunks.RemoveAt(i);
            }
        }
    }

    // ── Soil Ball Mesh ─────────────────────────────────────────────────────────

    void BuildSoilBall()
    {
        var rootArea = skeleton.GetRootAreaTransform();
        if (rootArea != null)
        {
            // Average XZ dimension → ball radius; Y → vertical semi-axis
            soilRadius = (rootArea.lossyScale.x + rootArea.lossyScale.z) * 0.25f;
            soilHeight = rootArea.lossyScale.y;
        }
        else
        {
            soilRadius = 1.3f;
            soilHeight = 1.0f;
        }
        // Verticality floor: the rootArea box can be quite flat, which would collapse the
        // ball to a single cell layer — keep it a proper rounded ball relative to its width.
        float radiusY = Mathf.Max(soilHeight * 0.55f, soilRadius * 0.5f);

        // Wrap the ball around the LIFTED root mass: top bulges just over the root
        // crown, the bulk hangs below — a root ball pulled out of the pot.
        Vector3 trunkBaseWorld = skeleton.root != null
            ? transform.TransformPoint(skeleton.root.worldPosition)
            : skeleton.plantingSurfacePoint;
        soilWorldCenter = trunkBaseWorld + Vector3.down * (radiusY * 0.6f);

        // Chunks vanish once they fall past the pot rim area
        chunkCullY = (rootArea != null ? rootArea.position.y : soilWorldCenter.y) - chunkCullDepth;

        // Voxelize a SOLID ellipsoid: cube cells packed in a grid, kept when their
        // center lies inside the ellipsoid. Cell size derived so the count lands
        // near targetChunkCount. Centers stored ball-local (transformed on demand).
        float volume = 4f / 3f * Mathf.PI * soilRadius * soilRadius * radiusY;
        cellSize = Mathf.Pow(volume / Mathf.Max(20, targetChunkCount), 1f / 3f);

        var centers = new List<Vector3>();
        var coords  = new List<Vector3Int>();
        int nx = Mathf.CeilToInt(soilRadius / cellSize);
        int ny = Mathf.CeilToInt(radiusY    / cellSize);
        for (int ix = -nx; ix <= nx; ix++)
        for (int iy = -ny; iy <= ny; iy++)
        for (int iz = -nx; iz <= nx; iz++)
        {
            Vector3 c = new(ix * cellSize, iy * cellSize, iz * cellSize);
            float ex = c.x / soilRadius, ey = c.y / radiusY, ez = c.z / soilRadius;
            if (ex * ex + ey * ey + ez * ez <= 1f)
            {
                centers.Add(c);
                coords.Add(new Vector3Int(ix, iy, iz));
            }
        }

        numCells         = centers.Count;
        cellLocalCenters = centers.ToArray();
        cellCoords       = coords.ToArray();
        cellRemoved      = new bool[numCells];
        cellHits         = new int[numCells];
        cellByCoord.Clear();
        for (int i = 0; i < numCells; i++) cellByCoord[cellCoords[i]] = i;
        nextIslandCheck = 0f;

        // Stable per-cell rotation for the authored dirt mesh (rolled once, not per rebuild)
        cellRotations = new Quaternion[numCells];
        for (int i = 0; i < numCells; i++) cellRotations[i] = Random.rotationUniform;

        // Cache authored dirt mesh data, centered on its bounds and scaled to the cell
        dirtVerts = null; dirtNorms = null; dirtTris = null;
        if (dirtCellMesh != null)
        {
            dirtVerts = dirtCellMesh.vertices;
            dirtNorms = dirtCellMesh.normals;
            dirtTris  = dirtCellMesh.triangles;
            if (dirtNorms == null || dirtNorms.Length != dirtVerts.Length)
            {
                Debug.LogWarning("[RootRake] dirtCellMesh has no normals — falling back to built-in cubes.");
                dirtVerts = null; dirtNorms = null; dirtTris = null;
            }
            else
            {
                dirtCenter = dirtCellMesh.bounds.center;
                Vector3 size = dirtCellMesh.bounds.size;
                float maxDim = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
                dirtScaleToCell = maxDim > 0.0001f ? cellSize * 1.15f / maxDim : 1f;
            }
        }

        soilBallGO = new GameObject("SoilBall");
        soilBallGO.transform.position = soilWorldCenter;
        // Ride the tree so the ball tracks the lift animation
        soilBallGO.transform.SetParent(skeleton.transform, worldPositionStays: true);

        soilMF = soilBallGO.AddComponent<MeshFilter>();
        var mr = soilBallGO.AddComponent<MeshRenderer>();
        mr.material = soilMaterial;
        soilMC = soilBallGO.AddComponent<MeshCollider>();

        RebuildSoilMesh();
    }

    void RebuildSoilMesh()
    {
        // Solid-of-cells rendering: every remaining cell contributes a slightly
        // oversized cube (overlap hides seams). Faces between neighbours are buried
        // inside the solid, so when a cell is raked away its neighbours' faces are
        // simply revealed — the surface "closes up" behind every removed chunk.
        var verts = new List<Vector3>(numCells * 8);
        var tris  = new List<int>(numCells * 36);
        var norms = new List<Vector3>(numCells * 8);

        if (dirtVerts != null)
        {
            // Authored dirt clod baked per cell: centered, scaled to the cell, with the
            // cell's stable random rotation applied to positions and normals.
            for (int i = 0; i < numCells; i++)
            {
                if (cellRemoved[i]) continue;
                var rot = cellRotations[i];
                int b = verts.Count;
                for (int k = 0; k < dirtVerts.Length; k++)
                {
                    verts.Add(cellLocalCenters[i] + rot * ((dirtVerts[k] - dirtCenter) * dirtScaleToCell));
                    norms.Add(rot * dirtNorms[k]);
                }
                foreach (int t in dirtTris) tris.Add(b + t);
            }
        }
        else
        {
            float half = cellSize * 0.53f;   // ~6% overlap, no cracks
            for (int i = 0; i < numCells; i++)
            {
                if (cellRemoved[i]) continue;

                // Flat-shaded faces (4 verts each, face normal): corner-blended normals made
                // every cube edge shade dark, reading as fake grooves between cells.
                for (int f = 0; f < 6; f++)
                {
                    int b = verts.Count;
                    var quad = FaceQuads[f];
                    for (int q = 0; q < 4; q++)
                    {
                        int k = quad[q];
                        verts.Add(cellLocalCenters[i] + new Vector3(
                            ((k & 1) == 0 ? -half : half),
                            ((k & 2) == 0 ? -half : half),
                            ((k & 4) == 0 ? -half : half)));
                        norms.Add(FaceNormals[f]);
                    }
                    tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                    tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
                }
            }
        }

        var mesh = new Mesh { name = "SoilBall" };
        // Authored clods × ~110 cells can exceed the 16-bit index limit
        if (verts.Count > 65000)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.SetNormals(norms);
        soilMF.sharedMesh  = mesh;
        soilMC.sharedMesh  = verts.Count > 0 ? mesh : null;
    }

    void DestroySoilBall()
    {
        if (soilBallGO != null) { Destroy(soilBallGO); soilBallGO = null; }
    }

    // ── Root Reveal & Straightening ───────────────────────────────────────────

    void BuildRakeRootList()
    {
        rakeRoots.Clear();
        Vector3 trunkLocal = skeleton.root.worldPosition;  // local space

        foreach (var node in skeleton.allNodes)
        {
            if (!node.isRoot || node == skeleton.root || node.isTrimmed) continue;

            // Compute the radial outward target in tree local space
            Vector3 horiz = node.worldPosition - trunkLocal;
            horiz.y = 0f;
            float dist = Mathf.Max(0.08f, horiz.magnitude);
            Vector3 dir = horiz / dist;

            // Gently slope downward with depth so roots fan out below the trunk
            float targetY = trunkLocal.y - 0.04f * node.depth;
            Vector3 targetLocal = new(
                trunkLocal.x + dir.x * dist,
                targetY,
                trunkLocal.z + dir.z * dist);

            rakeRoots.Add(new RakeRoot
            {
                node        = node,
                targetLocal = targetLocal,
                revealT     = 0f
            });
        }
    }

    void UpdateRootReveal()
    {
        float influence = rakeBrushRadius * 2.5f;

        for (int ri = 0; ri < rakeRoots.Count; ri++)
        {
            var rr = rakeRoots[ri];
            if (rr.revealT >= 1f) continue;

            Vector3 nodeWorld = transform.TransformPoint(rr.node.worldPosition);
            int nearTotal = 0, nearRemoved = 0;

            for (int ci = 0; ci < numCells; ci++)
            {
                if (Vector3.Distance(CellWorld(ci), nodeWorld) < influence)
                {
                    nearTotal++;
                    if (cellRemoved[ci]) nearRemoved++;
                }
            }

            if (nearTotal > 0 && nearRemoved > 0)
                rr.revealT = Mathf.Min(1f, rr.revealT + (float)nearRemoved / nearTotal * 0.6f);

            rakeRoots[ri] = rr;
        }
    }

    // ── Root Graph Manipulation ───────────────────────────────────────────────

    void TrimDeepRoots()
    {
        var toTrim = new List<TreeNode>();
        foreach (var n in skeleton.allNodes)
            if (n.isRoot && n.depth > rakeMaxDepth && !n.isTrimmed)
                toTrim.Add(n);

        // Trim deepest first so child-before-parent ordering is preserved
        toTrim.Sort((a, b) => b.depth.CompareTo(a.depth));
        foreach (var n in toTrim)
            if (!n.isTrimmed) skeleton.TrimNode(n);

        treeMeshBuilder?.SetDirty();
        Debug.Log($"[RootRake] TrimDeepRoots — trimmed {toTrim.Count} nodes beyond depth {rakeMaxDepth}");
    }

    void DiscardAndRegenerateRoots()
    {
        // Detach all root children from the trunk root node
        if (skeleton.root != null)
        {
            var rootChildren = new List<TreeNode>(
                skeleton.root.children.FindAll(c => c.isRoot));
            foreach (var child in rootChildren)
                skeleton.TrimNode(child);
        }

        // Plant fresh root strands (+ optional long root)
        skeleton.RegenerateInitialRoots(skeleton.hasLongRoot);
        treeMeshBuilder?.SetDirty();
    }
}
