using System.Collections.Generic;
using UnityEngine;

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
/// Placeholder mesh: simple curved quad until artist-modelled bark flakes are ready.
/// Swap in by assigning the modelled prefab to barkFlakePrefab in the Inspector.
/// Pivot of the prefab should be at the top edge so the flake "hangs" downward.
///
/// Peeling species: assign on TreeSpecies.barkType
///   10 = horizontal rolling strips (Birch)
///   12 = fibrous shreds            (Juniper, Cedar, Redwood)
///   14 = spongy fibrous attached   (Cypress)
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

    // ── Unity ─────────────────────────────────────────────────────────────────

    void Awake()
    {
        skeleton = GetComponent<TreeSkeleton>();
    }

    void OnEnable()  => GameManager.OnGameStateChanged += OnGameStateChanged;
    void OnDisable() => GameManager.OnGameStateChanged -= OnGameStateChanged;

    void OnGameStateChanged(GameState state)
    {
        if (state != GameState.BranchGrow) return;
        if (skeleton?.species == null)     return;

        // Only active for peeling bark species (Types 10, 12, 14)
        int bt = skeleton.species.barkType;
        if (bt != 10 && bt != 12 && bt != 14) return;

        if (GameManager.year <= lastSpringYear) return;
        lastSpringYear = GameManager.year;

        RefreshFlakes();
    }

    // ── Flake Management ──────────────────────────────────────────────────────

    void RefreshFlakes()
    {
        // Remove flakes from nodes that are now trimmed or no longer valid
        var toRemove = new List<int>();
        var liveIds  = new HashSet<int>();
        foreach (var node in skeleton.allNodes)
            if (!node.isTrimmed) liveIds.Add(node.id);
        foreach (var id in nodeFlakes.Keys)
            if (!liveIds.Contains(id)) toRemove.Add(id);
        foreach (var id in toRemove)
            DestroyFlakeCluster(id);

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
            if (target == 0) { DestroyFlakeCluster(node.id); continue; }

            if (!nodeFlakes.TryGetValue(node.id, out var list))
            {
                list = new List<GameObject>(maxFlakesPerNode);
                nodeFlakes[node.id] = list;
            }

            // Remove excess
            while (list.Count > target)
            {
                int last = list.Count - 1;
                if (list[last] != null) Destroy(list[last]);
                list.RemoveAt(last);
            }

            // Add missing
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
        // Position: near node tip, scattered on the branch surface
        Vector3 scatter = Random.insideUnitSphere * scatterRadius;
        Vector3 localPos = node.tipPosition + scatter;

        // Orientation: roughly align with branch normal (outward from axis)
        Vector3 branchAxis = node.growDirection.normalized;
        Vector3 outward    = Vector3.ProjectOnPlane(Random.onUnitSphere, branchAxis).normalized;
        if (outward.sqrMagnitude < 0.001f) outward = Vector3.right;

        // Tilt the flake away from the surface by a small random angle
        Vector3 tiltAxis = Vector3.Cross(outward, branchAxis).normalized;
        float   tiltDeg  = Random.Range(-tiltJitter, tiltJitter);
        Vector3 finalUp  = Quaternion.AngleAxis(tiltDeg, tiltAxis) * outward;

        Quaternion rot   = Quaternion.LookRotation(branchAxis, finalUp);

        GameObject go;
        if (barkFlakePrefab != null)
        {
            go = Instantiate(barkFlakePrefab, skeleton.transform);
        }
        else
        {
            // ── Placeholder: a flat curved quad built at runtime ────────────
            go = new GameObject("_BarkFlake");
            go.transform.SetParent(skeleton.transform, false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = BuildCurvedQuad();

            // Inherit the tree's bark material (same renderer as the trunk)
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

        float scale = baseFlakeScale * Random.Range(0.70f, 1.30f);
        go.transform.localPosition = localPos;
        go.transform.rotation      = rot;
        go.transform.localScale    = Vector3.one * scale;

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
