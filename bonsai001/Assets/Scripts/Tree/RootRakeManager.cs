using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages the Repot Root-Raking mini-game.
///
/// Flow:
///   1. buttonClicker detects a pot-bound repot request → calls EnterRakeMode().
///   2. A tessellated cylinder soil-ball mesh covers the lifted root area.
///      Existing roots (trimmed to rakeMaxDepth) are rendered inside it naturally.
///   3. TreeInteraction calls RakeAt(worldPos) on vertical mouse strokes over the soil.
///   4. Soil cells near the stroke disappear (+ spawn a falling chunk), revealing
///      and gradually straightening the roots beneath.
///   5. Confirm Repot → apply PotSoil.Repot, discard old roots, regenerate clean set.
///
/// Surface-popping roots (cosmetic pot-bound visual) are also driven here.
/// Add this component to the same GameObject as TreeSkeleton.
/// </summary>
[RequireComponent(typeof(TreeSkeleton))]
public class RootRakeManager : MonoBehaviour
{
    // ── Inspector ──────────────────────────────────────────────────────────────

    [Header("Soil Ball")]
    [Tooltip("Vertical rings in the tessellated cylinder mesh. More = finer reveal.")]
    [SerializeField] int soilRings = 8;
    [Tooltip("Radial segments per ring.")]
    [SerializeField] int soilSegments = 16;
    [Tooltip("Opaque soil-coloured material for the cylinder.")]
    [SerializeField] Material soilMaterial;
    [Tooltip("Small cube/sphere prefab (Rigidbody required) spawned when a cell is raked off. Leave null to skip.")]
    [SerializeField] GameObject soilChunkPrefab;
    [Tooltip("World-space radius of the rake brush (cells within this distance of the hit point are removed).")]
    [SerializeField] float rakeBrushRadius = 0.35f;

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

    // Soil ball
    GameObject  soilBallGO;
    MeshFilter  soilMF;
    MeshCollider soilMC;
    bool[]      cellRemoved;
    Vector3[]   cellWorldCenters;
    int         numCells;
    float       soilRadius, soilHeight;
    Vector3     soilWorldCenter;

    // Pending repot data (set when entering rake mode)
    PotSoil.SoilPreset pendingPreset;
    PotSoil.PotSize    pendingSize;
    bool               pendingSizeChanged;

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

    // ── Unity ─────────────────────────────────────────────────────────────────

    void Awake()
    {
        skeleton        = GetComponent<TreeSkeleton>();
        potSoil         = GetComponent<PotSoil>();
        treeMeshBuilder = GetComponent<TreeMeshBuilder>();
    }

    void Update()
    {
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
    /// Begin the rake mini-game. Called by buttonClicker when repotting a pot-bound tree.
    /// </summary>
    public void EnterRakeMode(PotSoil.SoilPreset preset, PotSoil.PotSize size, bool sizeChanged)
    {
        if (InRakeMode) return;
        pendingPreset      = preset;
        pendingSize        = size;
        pendingSizeChanged = sizeChanged;

        DestroySurfaceRoots();
        TrimDeepRoots();
        BuildSoilBall();
        BuildRakeRootList();

        InRakeMode = true;
        GameManager.Instance.UpdateGameState(GameState.RootRake);
        Debug.Log($"[RootRake] Entered rake mode | roots={rakeRoots.Count} cells={numCells} | year={GameManager.year}");
    }

    /// <summary>
    /// Player confirmed the repot. Applies soil, discards old roots, regenerates clean set.
    /// </summary>
    public void ConfirmRepot()
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

        potSoil.Repot(skeleton, pendingPreset, pendingSize, pendingSizeChanged);
        skeleton.GetComponent<LeafManager>()?.RefreshFungalTint(skeleton);

        DiscardAndRegenerateRoots();
        ExitRakeMode(restoreRootPrune: true);
        Debug.Log($"[RootRake] Confirmed repot | hasLongRoot={skeleton.hasLongRoot} | year={GameManager.year}");
    }

    /// <summary>Cancel rake without repotting — return to repot panel.</summary>
    public void CancelRakeMode()
    {
        ExitRakeMode(restoreRootPrune: false);
    }

    void ExitRakeMode(bool restoreRootPrune)
    {
        InRakeMode = false;
        DestroySoilBall();
        rakeRoots.Clear();

        if (restoreRootPrune)
            GameManager.Instance.ToggleRootPrune();   // leave root work entirely
        else
            GameManager.Instance.UpdateGameState(GameState.RootPrune); // back to repot panel
    }

    // ── Raking ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by TreeInteraction when a vertical mouse drag hits the soil ball surface.
    /// worldHitPoint: the world-space surface point from the raycast.
    /// </summary>
    public void RakeAt(Vector3 worldHitPoint)
    {
        if (!InRakeMode || soilBallGO == null) return;

        bool changed = false;
        for (int i = 0; i < numCells; i++)
        {
            if (cellRemoved[i]) continue;
            if (Vector3.Distance(cellWorldCenters[i], worldHitPoint) <= rakeBrushRadius)
            {
                cellRemoved[i] = true;
                changed = true;
                SpawnChunk(cellWorldCenters[i]);
            }
        }

        if (!changed) return;
        RebuildSoilMesh();
        UpdateRootReveal();
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
        if (soilChunkPrefab == null) return;
        var chunk = Instantiate(soilChunkPrefab, worldPos, Random.rotation);
        var rb = chunk.GetComponent<Rigidbody>() ?? chunk.AddComponent<Rigidbody>();
        rb.linearVelocity = new Vector3(
            Random.Range(-0.5f, 0.5f),
            Random.Range(0.4f, 2.0f),
            Random.Range(-0.5f, 0.5f));
        Destroy(chunk, 3f);
    }

    // ── Soil Ball Mesh ─────────────────────────────────────────────────────────

    void BuildSoilBall()
    {
        var rootArea = skeleton.GetRootAreaTransform();
        if (rootArea != null)
        {
            // Average XZ dimension → cylinder radius; Y → height
            soilRadius      = (rootArea.lossyScale.x + rootArea.lossyScale.z) * 0.25f;
            soilHeight      = rootArea.lossyScale.y;
            soilWorldCenter = rootArea.position;
        }
        else
        {
            soilRadius      = 1.3f;
            soilHeight      = 1.0f;
            soilWorldCenter = skeleton.plantingSurfacePoint + Vector3.down * 0.5f;
        }

        numCells         = soilRings * soilSegments;
        cellRemoved      = new bool[numCells];
        cellWorldCenters = new Vector3[numCells];

        // Pre-compute world-space centre of each cell for brush proximity tests
        for (int ring = 0; ring < soilRings; ring++)
        {
            float y = soilWorldCenter.y + Mathf.Lerp(
                -soilHeight * 0.5f, soilHeight * 0.5f,
                (ring + 0.5f) / soilRings);

            for (int seg = 0; seg < soilSegments; seg++)
            {
                float a = (seg + 0.5f) / soilSegments * Mathf.PI * 2f;
                cellWorldCenters[ring * soilSegments + seg] = new Vector3(
                    soilWorldCenter.x + Mathf.Cos(a) * soilRadius,
                    y,
                    soilWorldCenter.z + Mathf.Sin(a) * soilRadius);
            }
        }

        soilBallGO = new GameObject("SoilBall");
        soilBallGO.transform.position = soilWorldCenter;

        soilMF = soilBallGO.AddComponent<MeshFilter>();
        var mr = soilBallGO.AddComponent<MeshRenderer>();
        mr.material = soilMaterial;
        soilMC = soilBallGO.AddComponent<MeshCollider>();

        RebuildSoilMesh();
    }

    void RebuildSoilMesh()
    {
        var verts = new List<Vector3>();
        var tris  = new List<int>();
        var norms = new List<Vector3>();

        // Side quads
        for (int ring = 0; ring < soilRings; ring++)
        {
            float y0 = Mathf.Lerp(-soilHeight * 0.5f, soilHeight * 0.5f, (float)ring       / soilRings);
            float y1 = Mathf.Lerp(-soilHeight * 0.5f, soilHeight * 0.5f, (float)(ring + 1) / soilRings);

            for (int seg = 0; seg < soilSegments; seg++)
            {
                if (cellRemoved[ring * soilSegments + seg]) continue;

                float a0 = (float)seg       / soilSegments * Mathf.PI * 2f;
                float a1 = (float)(seg + 1) / soilSegments * Mathf.PI * 2f;

                // Positions in soil-ball local space (origin = soilWorldCenter)
                Vector3 p00 = new(Mathf.Cos(a0) * soilRadius, y0, Mathf.Sin(a0) * soilRadius);
                Vector3 p10 = new(Mathf.Cos(a1) * soilRadius, y0, Mathf.Sin(a1) * soilRadius);
                Vector3 p01 = new(Mathf.Cos(a0) * soilRadius, y1, Mathf.Sin(a0) * soilRadius);
                Vector3 p11 = new(Mathf.Cos(a1) * soilRadius, y1, Mathf.Sin(a1) * soilRadius);

                Vector3 n0 = new(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                Vector3 n1 = new(Mathf.Cos(a1), 0f, Mathf.Sin(a1));

                int b = verts.Count;
                verts.Add(p00); norms.Add(n0);
                verts.Add(p10); norms.Add(n1);
                verts.Add(p11); norms.Add(n1);
                verts.Add(p01); norms.Add(n0);
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
            }
        }

        // Top and bottom disk caps
        AddDiskCap(verts, tris, norms,  soilHeight * 0.5f, +1f);
        AddDiskCap(verts, tris, norms, -soilHeight * 0.5f, -1f);

        var mesh = new Mesh { name = "SoilBall" };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.SetNormals(norms);
        soilMF.sharedMesh  = mesh;
        soilMC.sharedMesh  = mesh;
    }

    void AddDiskCap(List<Vector3> verts, List<int> tris, List<Vector3> norms, float y, float normY)
    {
        Vector3 capNorm = new(0f, normY, 0f);
        int ci = verts.Count;
        verts.Add(new Vector3(0f, y, 0f));
        norms.Add(capNorm);

        for (int seg = 0; seg < soilSegments; seg++)
        {
            float a0 = (float)seg       / soilSegments * Mathf.PI * 2f;
            float a1 = (float)(seg + 1) / soilSegments * Mathf.PI * 2f;
            int v0 = verts.Count;
            verts.Add(new(Mathf.Cos(a0) * soilRadius, y, Mathf.Sin(a0) * soilRadius)); norms.Add(capNorm);
            verts.Add(new(Mathf.Cos(a1) * soilRadius, y, Mathf.Sin(a1) * soilRadius)); norms.Add(capNorm);
            if (normY > 0f) { tris.Add(ci); tris.Add(v0);     tris.Add(v0 + 1); }
            else             { tris.Add(ci); tris.Add(v0 + 1); tris.Add(v0);     }
        }
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
                if (Vector3.Distance(cellWorldCenters[ci], nodeWorld) < influence)
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
