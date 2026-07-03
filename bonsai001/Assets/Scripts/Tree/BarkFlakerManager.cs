using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Spawns 3D bark-flake meshes on trunk and scaffold nodes for species with
/// peeling bark (Types 10, 12, 14: Birch, Juniper, Redwood, Cedar, Cypress).
///
/// Analogous to LeafManager: flakes are GameObjects parented to the tree transform
/// in local space, oriented normal to the branch surface.
///
/// Flake count per node scales with tree age × (1 - health), giving the impression
/// of a peeling, lived-in surface without overwhelming the scene.
///
/// Flake geometry + palette are procedural PER BARK TYPE (2026-07-02): pine jigsaw
/// plates (9), blocky chunks (8), birch paper curls with the white/tan two-tone (10),
/// long fibrous shreds (12), spongy strips (14) — each with its own shed rate and
/// scale, sitting on the actual bark surface radius. An artist prefab assigned to
/// barkFlakePrefab still overrides everything (pivot at top edge, hangs downward).
/// </summary>
public class BarkFlakerManager : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Tooltip("Bark-flake prefab to instantiate.\n" +
             "Placeholder: leave null to use the built-in curved-quad fallback.\n" +
             "Swap for artist-modelled bark when ready. Pivot at top edge.")]
    [SerializeField] GameObject barkFlakePrefab;

    [Tooltip("Maximum flakes per node at peak age + stress.")]
    [SerializeField] [Range(1, 6)] int maxFlakesPerNode = 2;

    [Tooltip("Tree age (years) before any flakes appear.")]
    [SerializeField] float minAgeForFlakes = 4f;

    [Tooltip("Tree age (years) at which max flakes are reached.")]
    [SerializeField] float ageRange = 25f;

    [Tooltip("Only nodes at this depth or shallower (trunk / scaffold) get flakes.")]
    [SerializeField] int maxFlakeDepth = 3;

    [Tooltip("Minimum node radius before flakes appear on it.")]
    [SerializeField] float minFlakeRadius = 0.06f;

    [Tooltip("How much a stressed / unhealthy tree peels vs a healthy one.\n" +
             "0 = health has no effect.  1 = unhealthy tree peels at full rate.")]
    [SerializeField] [Range(0f, 1f)] float healthFlakeBoost = 0.6f;

    [Tooltip("World-space max scatter radius around the node tip for each flake.")]
    [SerializeField] float scatterRadius = 0.08f;

    [Tooltip("Max random tilt angle (degrees) from the branch surface normal.")]
    [SerializeField] float tiltJitter = 22f;

    [Tooltip("Base world-space scale of each flake. Randomised ±30 %% per instance.")]
    [SerializeField] float baseFlakeScale = 0.18f;

    // ── References ────────────────────────────────────────────────────────────

    TreeSkeleton skeleton;

    // ── State ─────────────────────────────────────────────────────────────────

    // nodeId → list of live flake GameObjects
    readonly Dictionary<int, List<GameObject>> nodeFlakes = new Dictionary<int, List<GameObject>>();

    int lastSpringYear = -1;

    [Tooltip("Chance per flake each spring to peel off and fall, so bark turns over and never piles up.")]
    [SerializeField] [Range(0f, 1f)] float shedChancePerSeason = 0.35f;

    // Flakes that have peeled off and are drifting down before being destroyed.
    class FallingFlake { public GameObject go; public Vector3 vel; public Vector3 spin; public float age; }
    readonly List<FallingFlake> fallingFlakes = new List<FallingFlake>();

    // ── Unity ─────────────────────────────────────────────────────────────────

    void Awake()
    {
        skeleton = GetComponent<TreeSkeleton>();
        cam      = Camera.main;
    }

    // ── Peel-and-discard interaction ──────────────────────────────────────────
    // RMB grabs a loose flake off the trunk (same button + RaycastAll idiom as
    // WeedPuller so tree/table colliders don't block the pick); dragging carries it
    // with the cursor; releasing discards it — peeled bark just vanishes. Grooming
    // only, no sim effect.
    Camera     cam;
    GameObject heldFlake;
    float      heldDistance;

    void OnEnable()  => GameManager.OnGameStateChanged += OnGameStateChanged;
    void OnDisable() => GameManager.OnGameStateChanged -= OnGameStateChanged;

    void OnGameStateChanged(GameState state)
    {
        if (state != GameState.BranchGrow) return;
        if (skeleton?.species == null)     return;

        // Active for every bark type that visibly flakes/plates/peels:
        // 8 blocks, 9 large plates (pines), 10 peeling strips (birch),
        // 12 fibrous shreds (juniper/cedar/redwood), 14 spongy fibrous (cypress).
        if (!IsFlakingBarkType(skeleton.species.barkType)) return;

        if (GameManager.year <= lastSpringYear) return;
        lastSpringYear = GameManager.year;

        RefreshFlakes();
    }

    static bool IsFlakingBarkType(int bt) => bt == 8 || bt == 9 || bt == 10 || bt == 12 || bt == 14;

    // Per-type turnover: plates lock in for years; birch paper rolls off every season.
    float TypeShedChance(int bt) => bt switch
    {
        9  => 0.10f,
        8  => 0.15f,
        10 => 0.45f,
        12 => 0.35f,
        14 => 0.25f,
        _  => shedChancePerSeason,
    };

    static float TypeScale(int bt) => bt switch
    {
        9  => 1.6f,    // big jigsaw plates
        8  => 1.1f,
        10 => 1.0f,
        12 => 1.25f,   // long hanging shreds
        14 => 0.9f,
        _  => 1f,
    };

    // ── Falling (peeled) flakes + peel interaction ────────────────────────────
    void Update()
    {
        UpdateFlakePicking();

        if (fallingFlakes.Count == 0) return;
        float dt = Time.deltaTime;   // real-time so peeled bark always falls at a visible rate
        for (int i = fallingFlakes.Count - 1; i >= 0; i--)
        {
            var f = fallingFlakes[i];
            if (f.go == null) { fallingFlakes.RemoveAt(i); continue; }
            f.vel.y -= 0.9f * dt;                                   // gravity
            f.go.transform.position += f.vel * dt;
            f.go.transform.Rotate(f.spin * dt, Space.Self);        // tumble
            f.age += dt;
            // Quick-Start: peeled bark vanishes after a few frames (same rule as leaves —
            // falling debris nobody watches shouldn't accumulate during the fast-grow).
            float flakeLifetime = (QuickStartManager.Instance != null && QuickStartManager.Instance.IsGenerating)
                ? 0.05f : 4f;
            if (f.age > flakeLifetime) { Destroy(f.go); fallingFlakes.RemoveAt(i); }
        }
    }

    void UpdateFlakePicking()
    {
        if (Mouse.current == null) return;
        if (cam == null) { cam = Camera.main; if (cam == null) return; }

        // Grab: RMB press over a flake (skip if a weed pull is already in progress).
        if (Mouse.current.rightButton.wasPressedThisFrame && heldFlake == null)
        {
            if (WeedPuller.Instance != null && WeedPuller.Instance.IsPulling) return;
            var ray  = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
            var hits = Physics.RaycastAll(ray);
            float best = float.MaxValue;
            foreach (var h in hits)
                if (h.collider.GetComponent<BarkFlake>() != null && h.distance < best)
                { best = h.distance; heldFlake = h.collider.gameObject; }
            if (heldFlake != null)
            {
                heldDistance = best;
                ForgetFlake(heldFlake);                       // manager no longer owns it
                heldFlake.transform.SetParent(null, true);
            }
            return;
        }

        if (heldFlake == null) return;

        if (Mouse.current.rightButton.isPressed)
        {
            // Carry with the cursor at the grab depth, with a lazy tumble.
            var ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
            heldFlake.transform.position = ray.GetPoint(heldDistance);
            heldFlake.transform.Rotate(Vector3.up, 60f * Time.unscaledDeltaTime, Space.World);
        }
        else
        {
            // Dropped → discarded. Peeled bark doesn't litter the scene.
            Destroy(heldFlake);
            heldFlake = null;
        }
    }

    /// <summary>Remove a flake GameObject from every manager list so RefreshFlakes /
    /// the fall animation stop touching it (a grabbed flake belongs to the player's hand).</summary>
    void ForgetFlake(GameObject go)
    {
        fallingFlakes.RemoveAll(f => f.go == go);   // grabbable mid-fall too
        foreach (var kvp in nodeFlakes)
            if (kvp.Value.Remove(go)) return;
    }

    /// <summary>Detach a flake and let it drift down + tumble, then self-destruct (peeled bark).</summary>
    void ShedFlake(GameObject go)
    {
        if (go == null) return;
        go.transform.SetParent(null, worldPositionStays: true);
        fallingFlakes.Add(new FallingFlake
        {
            go   = go,
            vel  = new Vector3(Random.Range(-0.12f, 0.12f), Random.Range(-0.05f, 0.05f), Random.Range(-0.12f, 0.12f)),
            spin = Random.insideUnitSphere * Random.Range(40f, 160f),
            age  = 0f,
        });
    }

    void ShedCluster(int nodeId)
    {
        if (!nodeFlakes.TryGetValue(nodeId, out var list)) return;
        foreach (var go in list) ShedFlake(go);
        nodeFlakes.Remove(nodeId);
    }

    // ── Flake Management ──────────────────────────────────────────────────────

    void RefreshFlakes()
    {
        // Bark peels from nodes that are now trimmed or no longer valid (it falls, not vanishes).
        var toRemove = new List<int>();
        var liveIds  = new HashSet<int>();
        foreach (var node in skeleton.allNodes)
            if (!node.isTrimmed) liveIds.Add(node.id);
        foreach (var id in nodeFlakes.Keys)
            if (!liveIds.Contains(id)) toRemove.Add(id);
        foreach (var id in toRemove)
            ShedCluster(id);

        // Spawn / top-up flakes on eligible nodes
        float treeAge = GameManager.year - skeleton.plantingYear;

        foreach (var node in skeleton.allNodes)
        {
            if (node.isTrimmed)                  continue;
            if (node.isRoot)                     continue;
            if (node.depth > maxFlakeDepth)      continue;
            if (node.radius < minFlakeRadius)    continue;
            if (!node.isTerminal && node.children.Count > 0 && node.depth == 0 && node.radius < 0.12f) continue;

            int target = TargetFlakeCount(node, treeAge);
            if (target == 0) { ShedCluster(node.id); continue; }

            if (!nodeFlakes.TryGetValue(node.id, out var list))
            {
                list = new List<GameObject>(maxFlakesPerNode);
                nodeFlakes[node.id] = list;
            }

            // Turnover: some flakes peel off and fall each season, then fresh bark replaces them —
            // so old flakes never accumulate as litter. Rate depends on the bark type.
            float shedChance = TypeShedChance(skeleton.species != null ? skeleton.species.barkType : 0);
            for (int i = list.Count - 1; i >= 0; i--)
                if (Random.value < shedChance)
                {
                    ShedFlake(list[i]);
                    list.RemoveAt(i);
                }

            // Remove excess (also peels off)
            while (list.Count > target)
            {
                int last = list.Count - 1;
                ShedFlake(list[last]);
                list.RemoveAt(last);
            }

            // Add missing (fresh bark)
            while (list.Count < target)
                list.Add(SpawnFlake(node));
        }
    }

    int TargetFlakeCount(TreeNode node, float treeAge)
    {
        // Age factor: 0 until minAgeForFlakes, ramps to 1 over ageRange
        float ageFactor = Mathf.Clamp01((treeAge - minAgeForFlakes) / Mathf.Max(1f, ageRange));
        if (ageFactor <= 0f) return 0;

        // Health factor: healthy tree (1.0) gets minimal peeling,
        // stressed tree (0.0) gets healthFlakeBoost extra
        float stress = healthFlakeBoost * (1f - Mathf.Clamp01(node.health));
        float combined = Mathf.Clamp01(ageFactor + stress * ageFactor);

        return Mathf.RoundToInt(combined * maxFlakesPerNode);
    }

    GameObject SpawnFlake(TreeNode node)
    {
        int bt = skeleton.species != null ? skeleton.species.barkType : 0;

        // Sit ON the bark: pick a point along the segment axis and push out to the
        // surface radius there (the old scatter-around-the-tip left flakes floating).
        float   t          = Random.Range(0.1f, 0.9f);
        Vector3 branchAxis = node.growDirection.normalized;
        Vector3 axisPos    = Vector3.Lerp(node.worldPosition, node.tipPosition, t);
        Vector3 outward    = Vector3.ProjectOnPlane(Random.onUnitSphere, branchAxis).normalized;
        if (outward.sqrMagnitude < 0.001f) outward = Vector3.right;
        float   surfaceR   = Mathf.Lerp(node.radius, node.tipRadius, t);
        Vector3 localPos   = axisPos + outward * (surfaceR * 0.92f);

        // Peel-away tilt around the circumferential tangent. Plates/blocks hug the
        // surface; strips and paper lift their free edge off the trunk.
        bool    hugs    = bt == 8 || bt == 9;
        Vector3 tangent = Vector3.Cross(branchAxis, outward).normalized;
        float   tiltDeg = hugs ? Random.Range(0f, tiltJitter * 0.35f)
                               : Random.Range(3f, tiltJitter);
        Vector3 peeled  = Quaternion.AngleAxis(tiltDeg, tangent) * outward;

        // Face outward (+Z = away from the trunk), body hanging down the axis (strips)
        // or lying flat with a random roll (plates/blocks).
        Quaternion rot = Quaternion.LookRotation(peeled, branchAxis)
                       * Quaternion.Euler(0f, 0f, hugs ? Random.Range(0f, 360f)
                                                       : Random.Range(-12f, 12f));

        GameObject go;
        if (barkFlakePrefab != null)
        {
            go = Instantiate(barkFlakePrefab, skeleton.transform);
        }
        else
        {
            // ── Procedural flake, geometry + palette per bark type ───────────
            go = new GameObject("_BarkFlake");
            go.transform.SetParent(skeleton.transform, false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = FlakeMeshForType(bt);

            // Inherit the tree's bark material (vertex colours carry the flake palette)
            var treeMr = skeleton.GetComponent<MeshRenderer>();
            if (treeMr != null) mr.sharedMaterial = treeMr.sharedMaterial;
            else
            {
                var mat = new Material(Shader.Find("Unlit/Color"));
                mat.color = skeleton.species != null
                    ? skeleton.species.matureBarkColor
                    : new Color(0.42f, 0.35f, 0.27f);
                mr.material = mat;
            }
        }

        float scale = baseFlakeScale * TypeScale(bt) * Random.Range(0.70f, 1.30f);
        go.transform.localPosition = localPos;
        go.transform.rotation      = rot;
        go.transform.localScale    = Vector3.one * scale;

        // Pickable: marker + a raycast collider (BoxCollider auto-fits the mesh bounds).
        go.AddComponent<BarkFlake>();
        if (go.GetComponent<Collider>() == null)
            go.AddComponent<BoxCollider>();

        return go;
    }

    void DestroyFlakeCluster(int nodeId)
    {
        if (!nodeFlakes.TryGetValue(nodeId, out var list)) return;
        foreach (var go in list)
            if (go != null) Destroy(go);
        nodeFlakes.Remove(nodeId);
    }

    /// <summary>Removes all flakes — called on tree death or full repot.</summary>
    public void ClearAllFlakes()
    {
        foreach (var id in new List<int>(nodeFlakes.Keys))
            DestroyFlakeCluster(id);
    }

    // ── Per-bark-type flake meshes ────────────────────────────────────────────
    // One cached mesh per type; instance variety comes from scale/roll/tilt jitter.
    // All meshes: pivot at the attachment edge, face +Z (verified against
    // BuildCurvedQuad's clockwise-from-+Z winding), double-sided via FinishFlakeMesh,
    // palette in vertex colours (the bark shader multiplies them).

    static readonly Dictionary<int, Mesh> typeMeshCache = new Dictionary<int, Mesh>();

    static Mesh FlakeMeshForType(int bt)
    {
        if (typeMeshCache.TryGetValue(bt, out var cached)) return cached;
        Mesh mesh;
        switch (bt)
        {
            case 9:  mesh = BuildPlate(7, 0.06f, new Color(0.47f, 0.37f, 0.28f),
                                       new Color(0.62f, 0.38f, 0.22f)); break;   // pine plate, orange under
            case 8:  mesh = BuildPlate(5, 0.03f, new Color(0.38f, 0.31f, 0.25f),
                                       new Color(0.44f, 0.35f, 0.27f)); break;   // irregular block
            case 10: mesh = BuildPaperCurl(); break;                              // birch paper
            case 12: mesh = BuildShred();     break;                              // fibrous shred
            case 14: mesh = BuildSpongyStrip(); break;                            // cypress sponge
            default: mesh = BuildCurvedQuad(); break;
        }
        typeMeshCache[bt] = mesh;
        return mesh;
    }

    /// <summary>Duplicates the geometry reversed for a lit backside, writes the two
    /// palettes into vertex colours, recalculates normals/bounds.</summary>
    static Mesh FinishFlakeMesh(string name, List<Vector3> verts, List<int> tris,
                                Color front, Color back)
    {
        int n = verts.Count;
        var allVerts = new List<Vector3>(verts);
        allVerts.AddRange(verts);
        var allTris = new List<int>(tris);
        for (int i = 0; i < tris.Count; i += 3)
        {
            allTris.Add(n + tris[i]);
            allTris.Add(n + tris[i + 2]);
            allTris.Add(n + tris[i + 1]);
        }
        var colors = new Color[allVerts.Count];
        for (int i = 0; i < n; i++)               colors[i] = front;
        for (int i = n; i < allVerts.Count; i++)  colors[i] = back;

        var mesh = new Mesh { name = name };
        mesh.SetVertices(allVerts);
        mesh.SetTriangles(allTris, 0);
        mesh.SetColors(colors);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>Irregular polygon plate with a slight outward dome — pine jigsaw plates
    /// (n=7, deep relief) and blocky bark chunks (n=5, shallow).</summary>
    static Mesh BuildPlate(int sides, float dome, Color front, Color back)
    {
        var rng   = new System.Random(4241 + sides);
        var verts = new List<Vector3> { new Vector3(0f, 0f, dome) };
        for (int i = 0; i < sides; i++)
        {
            float a = i / (float)sides * Mathf.PI * 2f;
            float r = 0.5f * (0.72f + (float)rng.NextDouble() * 0.38f);
            verts.Add(new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f));
        }
        var tris = new List<int>();
        for (int i = 0; i < sides; i++)
        {
            // Clockwise from +Z so the dome faces away from the trunk.
            tris.Add(0);
            tris.Add(1 + (i + 1) % sides);
            tris.Add(1 + i);
        }
        return FinishFlakeMesh(sides >= 7 ? "BarkPlate" : "BarkBlock", verts, tris, front, back);
    }

    /// <summary>Grid strip helper: cols×rows in XY (pivot top edge, hangs -Y), with a
    /// per-row/column Z displacement supplied by `zAt(u, v)` and width taper by `widthAt(v)`.</summary>
    static void BuildStripGrid(List<Vector3> verts, List<int> tris, int cols, int rows,
                               float width, float length,
                               System.Func<float, float, float> zAt,
                               System.Func<float, float> widthAt)
    {
        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
        {
            float u = (float)c / (cols - 1);
            float v = (float)r / (rows - 1);
            float w = width * widthAt(v);
            verts.Add(new Vector3(Mathf.Lerp(-w, w, u) * 0.5f, 0.5f - v * length, zAt(u, v)));
        }
        for (int r = 0; r < rows - 1; r++)
        for (int c = 0; c < cols - 1; c++)
        {
            int i = r * cols + c;
            tris.Add(i);     tris.Add(i + 1);        tris.Add(i + cols);
            tris.Add(i + 1); tris.Add(i + cols + 1); tris.Add(i + cols);
        }
    }

    /// <summary>Birch: a papery horizontal strip whose free end rolls away from the
    /// trunk — white face, tan inner side (the signature betula two-tone).</summary>
    static Mesh BuildPaperCurl()
    {
        var verts = new List<Vector3>(); var tris = new List<int>();
        BuildStripGrid(verts, tris, cols: 6, rows: 4, width: 1.1f, length: 0.45f,
            zAt:      (u, v) => (1f - Mathf.Cos(v * Mathf.PI * 0.85f)) * 0.24f,   // roll outward as it descends
            widthAt:  v => 1f);
        return FinishFlakeMesh("BarkPaperCurl", verts, tris,
                               new Color(0.88f, 0.85f, 0.80f),    // papery white outer
                               new Color(0.72f, 0.55f, 0.38f));   // tan inner face
    }

    /// <summary>Juniper / cedar / redwood: a long narrow fibrous shred, wavering
    /// slightly and tapering to a ragged point.</summary>
    static Mesh BuildShred()
    {
        var verts = new List<Vector3>(); var tris = new List<int>();
        BuildStripGrid(verts, tris, cols: 3, rows: 6, width: 0.20f, length: 1.05f,
            zAt:      (u, v) => Mathf.Sin(v * 9f) * 0.03f,
            widthAt:  v => Mathf.Lerp(1f, 0.25f, v));
        return FinishFlakeMesh("BarkShred", verts, tris,
                               new Color(0.45f, 0.38f, 0.30f),
                               new Color(0.55f, 0.42f, 0.30f));
    }

    /// <summary>Swamp cypress: short, wide, soft spongy strip that stays close to the trunk.</summary>
    static Mesh BuildSpongyStrip()
    {
        var verts = new List<Vector3>(); var tris = new List<int>();
        BuildStripGrid(verts, tris, cols: 4, rows: 4, width: 0.36f, length: 0.6f,
            zAt:      (u, v) => Mathf.Sin(u * Mathf.PI) * 0.05f,                  // gentle outward bulge
            widthAt:  v => Mathf.Lerp(1f, 0.7f, v));
        return FinishFlakeMesh("BarkSponge", verts, tris,
                               new Color(0.52f, 0.34f, 0.24f),
                               new Color(0.58f, 0.42f, 0.30f));
    }

    // ── Placeholder Mesh ──────────────────────────────────────────────────────

    /// <summary>
    /// Builds a simple 3×4 curved quad (slight bend ~20°) to stand in until
    /// artist-modelled bark flakes are available. The quad faces +Z, pivot at
    /// the top edge (y = 0.5 in local space), hangs downward along -Y.
    /// </summary>
    static Mesh BuildCurvedQuad()
    {
        // 3 columns × 4 rows of vertices
        const int cols = 3, rows = 4;
        var verts = new Vector3[cols * rows];
        var uvs2  = new Vector2[cols * rows];
        var tris  = new List<int>();

        float curveDeg = 20f;   // total bend across the quad width

        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
        {
            float u = (float)c / (cols - 1);   // 0..1 across width
            float v = (float)r / (rows - 1);   // 0..1 top→bottom

            // Slight S-curve on the width axis
            float angle = Mathf.Lerp(-curveDeg * 0.5f, curveDeg * 0.5f, u) * Mathf.Deg2Rad;
            float x = Mathf.Sin(angle) * 0.5f;
            float z = (1f - Mathf.Cos(angle)) * 0.15f;   // slight Z offset at edges

            verts[r * cols + c] = new Vector3(x, 0.5f - v, z);   // pivot at top (y=0.5)
            uvs2 [r * cols + c] = new Vector2(u, 1f - v);
        }

        for (int r = 0; r < rows - 1; r++)
        for (int c = 0; c < cols - 1; c++)
        {
            int i  = r * cols + c;
            tris.Add(i);        tris.Add(i + 1);        tris.Add(i + cols);
            tris.Add(i + 1);    tris.Add(i + cols + 1); tris.Add(i + cols);
        }

        var mesh = new Mesh();
        mesh.name     = "BarkFlakePlaceholder";
        mesh.vertices  = verts;
        mesh.uv        = uvs2;
        mesh.triangles = tris.ToArray();
        mesh.RecalculateNormals();
        return mesh;
    }
}

/// <summary>Marker for pickable bark flakes — BarkFlakerManager raycasts for this
/// (mirrors how WeedPuller finds Weed components). Added via AddComponent only.</summary>
public class BarkFlake : MonoBehaviour {}
