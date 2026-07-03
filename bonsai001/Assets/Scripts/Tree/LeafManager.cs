using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages the full leaf lifecycle:
///   Spring  (BranchGrow)  — spawns leaf clusters on eligible terminal nodes
///   Summer               — maintains cluster, updates colour
///   Autumn  (LeafFall)   — stochastically drops leaves day by day
///   Cleanup              — removes leaves from nodes that gained children or were trimmed
///
/// Leaves are instances of leafPrefab parented to the tree transform in local space.
/// All leaves share one Material instance so the seasonal colour update is O(1).
/// </summary>
public class LeafManager : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Tooltip("The leaf prefab to instantiate. Assign the Japanese Maple Leaf prefab.")]
    [SerializeField] GameObject leafPrefab;

    [Tooltip("Only terminal nodes at this depth or deeper get leaves (no leaves on trunk/scaffold).")]
    [SerializeField] int minLeafDepth = 1;

    [Tooltip("How many leaves per terminal node.")]
    [SerializeField] [Range(2, 10)] int leavesPerNode = 5;

    [Tooltip("Random ± range around leavesPerNode. e.g. 2 → Random(5-2, 5+2+1).")]
    [SerializeField] [Range(0, 5)] int leavesPerNodeRange = 0;

    [Tooltip("Radius of the random scatter cluster around the node tip.")]
    [SerializeField] float clusterRadius = 0.18f;

    [Tooltip("Species-default world-space scale for leaves. Season scale is computed from this.")]
    [SerializeField] float baseLeafScale = 0.25f;
    [Tooltip("Enable per-leaf and per-tick debug logs. Off by default.")]
    [SerializeField] bool verboseLog = false;

    [Tooltip("How much full pot-bound pressure shrinks leaves. 0 = no effect, 1 = shrinks to 40% of base.")]
    [SerializeField] [Range(0f, 1f)] float rootPressureLeafShrink = 1f;

    [Tooltip("How much full refinement level shrinks leaves. 0 = no effect, 1 = shrinks to 55% of base.")]
    [SerializeField] [Range(0f, 1f)] float refinementLeafShrink = 1f;

    [Tooltip("Probability per leaf per in-game day of falling during LeafFall state.")]
    [SerializeField] float baseFallChancePerDay = 0.15f;

    // ── References ────────────────────────────────────────────────────────────

    TreeSkeleton skeleton;

    // ── State ─────────────────────────────────────────────────────────────────

    // node.id → list of live leaf GameObjects
    readonly Dictionary<int, List<GameObject>> nodeLeaves = new Dictionary<int, List<GameObject>>();

    // Flat working list rebuilt when the dict changes — avoids dict enumeration in Update
    readonly List<(int nodeId, GameObject go)> allLeaves  = new List<(int, GameObject)>();
    bool listDirty = false;

    // Computed once each spring from root pressure + refinement + defoliation history.
    // All leaves spawned this season use this value so mid-season size never changes.
    float seasonLeafScale = 0.25f;

    // 0 → 1: set externally by the defoliation tool (not yet built); decays each season.
    public float defoliationFactor = 0f;

    bool isLeafFall      = false;
    bool isGrowingSeason = false;

    int lastSpringYear = -1;

    float fallDebugTimer = 0f;

    // Shared material — one colour update drives all leaves
    Material leafMat;

    // ── Needle foliage (conifers) ─────────────────────────────────────────────
    // A small pool of tuft meshes + one material is built per tree and shared by every
    // branch tip, so a fully-needled conifer is one object per tip (not per needle).
    // Multiple variants (indexed by node id) kill the repetition of every tip wearing
    // the identical tuft; they all share the one material so the renderer still batches.
    // Built lazily the first time a needle species spawns foliage.
    const int NeedleVariantCount = 4;
    Mesh[]   needleTuftVariants;
    Material needleMat;

    // Year-round evergreen shed: real conifers hold a needle 2–4 years and shed old
    // interior needles continuously, not just in autumn. A low-rate ambient trickle of
    // single falling needles (pooled cap) sells this without touching the tuft meshes.
    [Header("Evergreen Needle Shed")]
    [Tooltip("Average needles shed per 100 tufts per in-game day (evergreen conifers only; ~3× in autumn). 0 disables.")]
    [SerializeField] float needleShedPer100TuftsPerDay = 1.5f;
    [Tooltip("Max shed needles airborne at once — keeps the trickle cheap at any timescale.")]
    [SerializeField] int maxAirborneShedNeedles = 12;

    Mesh  shedNeedleMesh;
    float shedAccumulator;
    readonly List<GameObject> airborneShedNeedles = new List<GameObject>();

    bool IsNeedleSpecies => skeleton != null && skeleton.species != null
                            && skeleton.species.foliageType != FoliageType.BroadLeaf;

    bool IsEvergreen => skeleton != null && skeleton.species != null && skeleton.species.evergreen;

    void EnsureNeedleAssets()
    {
        if (skeleton == null || skeleton.species == null) return;

        if (needleTuftVariants == null)
        {
            needleTuftVariants = new Mesh[NeedleVariantCount];
            for (int i = 0; i < NeedleVariantCount; i++)
                needleTuftVariants[i] = NeedleMesh.Build(skeleton.species.foliageType,
                                                         Mathf.Clamp(skeleton.species.needlesPerTuft, 4, 40), i);
        }

        if (needleMat == null)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Standard")
                     ?? Shader.Find("Sprites/Default");
            needleMat = new Material(sh) { enableInstancing = true };
            Color c = skeleton.species.leafSpringColor;
            if (needleMat.HasProperty("_BaseColor")) needleMat.SetColor("_BaseColor", c);
            if (needleMat.HasProperty("_Color"))     needleMat.SetColor("_Color",     c);
            if (needleMat.HasProperty("_Cull"))      needleMat.SetFloat("_Cull", 0f);   // two-sided
            // Fully matte: hundreds of thin quads at ever-changing grazing angles turn
            // any specular into per-frame white glints — the "sparkle" that survives AA
            // (2026-07-03). Needles scatter light diffusely in reality anyway.
            if (needleMat.HasProperty("_Smoothness")) needleMat.SetFloat("_Smoothness", 0f);
            if (needleMat.HasProperty("_SpecularHighlights")) needleMat.SetFloat("_SpecularHighlights", 0f);
            needleMat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            if (needleMat.HasProperty("_EnvironmentReflections")) needleMat.SetFloat("_EnvironmentReflections", 0f);
            needleMat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        }
    }

    // ── Unity ─────────────────────────────────────────────────────────────────

    void Awake()
    {
        skeleton = GetComponent<TreeSkeleton>();

        if (leafPrefab == null)
        {
            Debug.LogError("LeafManager: leafPrefab is not assigned.", this);
            return;
        }

        // Create a shared material from the prefab's renderer so we own it
        var srcRenderer = leafPrefab.GetComponentInChildren<Renderer>();
        if (srcRenderer != null)
            leafMat = new Material(srcRenderer.sharedMaterial);
        else
        {
            leafMat = new Material(Shader.Find("Unlit/Color"));
            Debug.LogWarning("LeafManager: leaf prefab has no Renderer — using fallback unlit material.");
        }
    }

    void OnEnable()
    {
        GameManager.OnGameStateChanged += OnGameStateChanged;
        if (skeleton != null) skeleton.OnSubtreeTrimmed += OnSubtreeTrimmed;
    }

    void OnDisable()
    {
        GameManager.OnGameStateChanged -= OnGameStateChanged;
        if (skeleton != null) skeleton.OnSubtreeTrimmed -= OnSubtreeTrimmed;
    }

    void OnSubtreeTrimmed(List<TreeNode> removedNodes)
    {
        foreach (var node in removedNodes)
        {
            if (!nodeLeaves.TryGetValue(node.id, out var leaves)) continue;

            foreach (var go in leaves)
                if (go != null) go.GetComponent<Leaf>()?.StartFalling();

            nodeLeaves.Remove(node.id);
            listDirty = true;
        }
    }

    void OnGameStateChanged(GameState state)
    {
        isLeafFall      = (state == GameState.LeafFall);
        isGrowingSeason = (state == GameState.BranchGrow);

        if (state == GameState.BranchGrow && GameManager.year > lastSpringYear)
        {
            lastSpringYear = GameManager.year;
            defoliationFactor = Mathf.Max(0f, defoliationFactor - 0.2f);  // decays ~1 full defoliation per 5 seasons
            ComputeSeasonLeafScale();
            CleanupOrphanedLeaves();
            SpawnSpringLeaves();
        }

        if (state == GameState.LeafFall)
        {
            // Evergreen needles persist year-round — no autumn colour shift, no drop.
            if (IsEvergreen)
            {
                if (listDirty) RebuildFlatList();
                return;
            }

            if (listDirty) RebuildFlatList();
            Debug.Log($"[Leaves] LeafFall started | nodeLeaves={nodeLeaves.Count} allLeaves={allLeaves.Count}");

            // Give every live leaf a randomised fall-colour speed.
            // Guard IsInFallSeason so restoring LeafFall (e.g. after wiring) doesn't
            // reset the colour progress on leaves that are already turning.
            foreach (var kvp in nodeLeaves)
                foreach (var go in kvp.Value)
                {
                    if (go == null) continue;
                    var leaf = go.GetComponent<Leaf>();
                    if (leaf != null && !leaf.IsInFallSeason)
                        leaf.StartLeafFallSeason(Random.Range(0.4f, 2.2f));
                }

            fallDebugTimer = 0f;
        }
    }

    void Update()
    {
        if (leafMat == null) return;

        // Rebuild first so RollLeafFall always sees a current list
        if (listDirty)
            RebuildFlatList();

        // Check for new terminal nodes created mid-season (chain propagation).
        // SpawnSpringLeaves skips nodes already in the dict, so this is safe every frame.
        if (isGrowingSeason)
        {
            SpawnSpringLeaves();
            ProcessUnfurls();
        }
        UpdateOpeningBuds();

        // Evergreens shed a trickle of old needles all year — not just in autumn.
        if (IsNeedleSpecies && IsEvergreen)
            UpdateNeedleShed();

        if (isLeafFall && !IsEvergreen)
        {
            fallDebugTimer += Time.deltaTime;
            if (fallDebugTimer >= 1f)
            {
                fallDebugTimer = 0f;
                float prog = 0f;
                if (allLeaves.Count > 0)
                {
                    var (_, sampleGo) = allLeaves[0];
                    if (sampleGo != null) prog = sampleGo.GetComponent<Leaf>()?.FallColorProgress ?? 0f;
                }
                if (verboseLog) Debug.Log($"[Leaves] LeafFall tick | allLeaves={allLeaves.Count} nodeLeaves={nodeLeaves.Count} sampleProgress={prog:F2}");
            }
            RollLeafFall();
        }
    }

    // ── Season leaf scale ─────────────────────────────────────────────────────

    /// <summary>
    /// Computes this season's leaf size from three independent miniaturization factors.
    /// Called once at the start of BranchGrow; all leaves spawned this season use the result.
    ///
    ///   rootPressureFactor   — averaged boundaryPressure on root terminals, from TreeSkeleton
    ///   treeRefinementFactor — averaged refinementLevel on branch terminals, normalized to [0,1]
    ///   defoliationFactor    — set externally by the defoliation tool; decays each spring
    ///
    /// Each factor is independent and multiplicative. A fully pot-bound, well-refined,
    /// recently defoliated tree gets all three discounts simultaneously.
    /// </summary>
    void ComputeSeasonLeafScale()
    {
        float rootPressure   = skeleton.RootPressureFactor();
        float refinement     = TreeRefinementFactor();

        // Needle species size from species.needleLength; broadleaf from baseLeafScale.
        float foliageBase = (IsNeedleSpecies && skeleton.species != null)
            ? skeleton.species.needleLength : baseLeafScale;

        float scale = foliageBase
            * Mathf.Lerp(1f, 0.40f, rootPressure   * rootPressureLeafShrink)
            * Mathf.Lerp(1f, 0.55f, refinement      * refinementLeafShrink)
            * Mathf.Lerp(1f, 0.60f, defoliationFactor);

        seasonLeafScale = scale;
        Debug.Log($"[Leaves] seasonLeafScale={scale:F3} (base={foliageBase:F3} rootP={rootPressure:F2} refine={refinement:F2} defo={defoliationFactor:F2})");
    }

    /// <summary>Average refinementLevel across live branch terminals, normalized to [0,1].</summary>
    float TreeRefinementFactor()
    {
        float sum = 0f; int count = 0;
        foreach (var node in skeleton.allNodes)
        {
            if (node.isRoot || node.isTrimmed || !node.isTerminal) continue;
            sum += node.refinementLevel;
            count++;
        }
        if (count == 0) return 0f;
        float cap = skeleton.RefinementCap;
        return Mathf.Clamp01(sum / count / Mathf.Max(1f, cap));
    }

    // ── Bud-opening / staged unfurl (PLAN item E) ─────────────────────────────
    // A cluster no longer pops in whole: the first leaf (pair for opposite-bud
    // species) emerges at spawn, the rest unfurl one batch every
    // leafUnfurlIntervalDays. The autumn bud GameObject is handed over by
    // TreeSkeleton at bud break and lingers — swelling — until its cluster
    // finishes unfurling (or the fallback timer cleans it up).

    [Tooltip("In-game days between successive leaves unfurling from one opening bud.")]
    [SerializeField] float leafUnfurlIntervalDays = 1.5f;

    [Tooltip("Fallback: in-game days an opening bud may linger before it is removed even " +
             "if its cluster never finished unfurling (trimmed node, ineligible terminal…).")]
    [SerializeField] float budLingerMaxDays = 8f;

    class PendingUnfurl { public TreeNode node; public int remaining; public float nextDay; }
    class OpeningBud   { public GameObject go; public float bornDay; public Vector3 baseScale; }

    readonly List<PendingUnfurl>         pendingUnfurls = new List<PendingUnfurl>();
    readonly Dictionary<int, OpeningBud> openingBuds    = new Dictionary<int, OpeningBud>();

    static float DayNow() => GameManager.dayOfYear + GameManager.year * 366f;

    int UnfurlBatchSize =>
        skeleton.species != null && skeleton.species.budType == BudType.Opposite ? 2 : 1;

    /// <summary>TreeSkeleton hands the dormant bud GameObject here at bud break instead of
    /// destroying it, so it can visibly swell while its leaves unfurl.</summary>
    public void BeginBudOpen(int nodeId, GameObject budGo)
    {
        if (budGo == null) return;
        if (openingBuds.TryGetValue(nodeId, out var old) && old.go != null) Destroy(old.go);
        openingBuds[nodeId] = new OpeningBud
        { go = budGo, bornDay = DayNow(), baseScale = budGo.transform.localScale };
    }

    void FinishBudOpen(int nodeId)
    {
        if (nodeId < 0) return;
        if (openingBuds.TryGetValue(nodeId, out var b))
        {
            if (b.go != null) Destroy(b.go);
            openingBuds.Remove(nodeId);
        }
    }

    void ProcessUnfurls()
    {
        if (pendingUnfurls.Count == 0) return;
        float now = DayNow();
        for (int i = pendingUnfurls.Count - 1; i >= 0; i--)
        {
            var p = pendingUnfurls[i];
            var n = p.node;
            if (n == null || n.isTrimmed || n.isDead || !nodeLeaves.TryGetValue(n.id, out var list))
            {
                pendingUnfurls.RemoveAt(i);
                FinishBudOpen(n != null ? n.id : -1);
                continue;
            }
            if (now < p.nextDay) continue;

            int batch = Mathf.Min(UnfurlBatchSize, p.remaining);
            for (int b = 0; b < batch; b++) list.Add(SpawnSingleLeaf(n));
            listDirty   = true;
            p.remaining -= batch;
            p.nextDay    = now + leafUnfurlIntervalDays * Random.Range(0.7f, 1.3f);
            if (p.remaining <= 0)
            {
                pendingUnfurls.RemoveAt(i);
                FinishBudOpen(n.id);
            }
        }
    }

    void UpdateOpeningBuds()
    {
        if (openingBuds.Count == 0) return;
        float now = DayNow();
        List<int> done = null;
        foreach (var kv in openingBuds)
        {
            var b = kv.Value;
            if (b.go == null)
            {
                if (done == null) done = new List<int>();
                done.Add(kv.Key); continue;
            }
            float age = now - b.bornDay;
            if (age > budLingerMaxDays)
            {
                Destroy(b.go);
                if (done == null) done = new List<int>();
                done.Add(kv.Key); continue;
            }
            // Swell to ~1.6× over the first two in-game days, then hold while leaves unfurl
            b.go.transform.localScale = b.baseScale * (1f + 0.6f * Mathf.Clamp01(age / 2f));
        }
        if (done != null) foreach (int id in done) openingBuds.Remove(id);
    }

    // ── Spawning ──────────────────────────────────────────────────────────────

    void SpawnSpringLeaves()
    {
        if (leafPrefab == null || skeleton.root == null) return;

        int spawned = 0;
        foreach (var node in skeleton.allNodes)
        {
            if (node.isTrimmed)            continue;
            if (!node.isTerminal)          continue;
            if (node.isRoot)               continue;  // roots never get leaves
            if (node.depth < minLeafDepth) continue;
            if (nodeLeaves.ContainsKey(node.id)) continue;  // already has leaves

            // Bud gate: old-wood nodes only get leaves if they set a bud last autumn.
            // Nodes born this spring (birthYear == current year) get leaves immediately
            // since they are actively extending shoots — no bud needed.
            bool isNewThisSpring = node.birthYear == GameManager.year;
            if (!isNewThisSpring && !node.hasBud) continue;

            SpawnCluster(node);
            spawned++;
        }

        if (spawned > 0)
            Debug.Log($"[Leaves] SpawnSpringLeaves spawned={spawned} nodeLeaves={nodeLeaves.Count} year={GameManager.year}");
    }

    void SpawnCluster(TreeNode node)
    {
        // Needle species: one shared-mesh tuft per tip (no scatter, no staged unfurl).
        if (IsNeedleSpecies)
        {
            nodeLeaves[node.id] = new List<GameObject>(1) { SpawnNeedleTuft(node) };
            listDirty = true;
            FinishBudOpen(node.id);
            return;
        }

        int count = leavesPerNodeRange > 0
            ? Random.Range(leavesPerNode - leavesPerNodeRange, leavesPerNode + leavesPerNodeRange + 1)
            : leavesPerNode;
        count = Mathf.Max(1, count);

        // Register the cluster immediately (the bud gate checks nodeLeaves), spawn the
        // first batch now, and queue the rest to unfurl over the following days.
        var list = new List<GameObject>(count);
        nodeLeaves[node.id] = list;
        listDirty = true;

        int firstBatch = Mathf.Min(UnfurlBatchSize, count);
        for (int i = 0; i < firstBatch; i++)
            list.Add(SpawnSingleLeaf(node));

        int remaining = count - firstBatch;
        if (remaining > 0)
            pendingUnfurls.Add(new PendingUnfurl
            {
                node      = node,
                remaining = remaining,
                nextDay   = DayNow() + leafUnfurlIntervalDays * Random.Range(0.7f, 1.3f),
            });
        else
            FinishBudOpen(node.id);   // single-leaf cluster — bud has nothing left to do
    }

    GameObject SpawnSingleLeaf(TreeNode node)
    {
        // Build a world-space perpendicular basis from the branch direction so we can
        // spread leaves outward from the branch axis with the stem origin on the branch.
        Vector3 branchWorldDir = skeleton.transform.TransformDirection(node.growDirection).normalized;
        Vector3 perp = Vector3.Cross(branchWorldDir, Vector3.up);
        if (perp.sqrMagnitude < 0.01f) perp = Vector3.Cross(branchWorldDir, Vector3.right);
        perp.Normalize();

        // Place the stem origin ON the branch: scatter back along the segment
        // (0–80% of its length from tip) with only a tiny perpendicular wobble.
        float backFraction   = Random.Range(0f, 0.8f);
        Vector3 backOffset   = -node.growDirection * (backFraction * Mathf.Min(node.length, node.targetLength));
        Vector3 sideJitter   = Random.insideUnitSphere * clusterRadius * 0.1f;
        sideJitter          -= Vector3.Project(sideJitter, node.growDirection);  // keep perpendicular only
        Vector3 offset       = backOffset + sideJitter;

        // Orient leaf outward from branch: random azimuth around branch axis,
        // high elevation (mostly perpendicular) so leaves spread sideways, then
        // blend toward Vector3.down to simulate gravity droop.
        float   azimuth   = Random.Range(0f, 360f);
        float   elevation = Random.Range(55f, 88f);
        Vector3 outPerp   = Quaternion.AngleAxis(azimuth, branchWorldDir) * perp;
        Vector3 outDir    = Quaternion.AngleAxis(-elevation, Vector3.Cross(branchWorldDir, outPerp).normalized) * branchWorldDir;
        float   droop     = Random.Range(0.15f, 0.55f);
        outDir            = Vector3.Lerp(outDir, Vector3.down, droop).normalized;
        Quaternion rot    = Quaternion.LookRotation(outDir, Vector3.up)
                            * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

        var go = Instantiate(leafPrefab, skeleton.transform);
        go.transform.localPosition = node.tipPosition + offset;
        go.transform.rotation      = rot;
        go.transform.localScale    = Vector3.zero;  // Leaf.Update() scales it in

        // Apply shared material to all renderers in the prefab hierarchy
        foreach (var r in go.GetComponentsInChildren<Renderer>())
            r.sharedMaterial = leafMat;

        // Use existing Leaf component if the prefab already has one (avoids duplicate
        // components fighting each other — the second one would reset localPosition every frame)
        var leaf          = go.GetComponent<Leaf>() ?? go.AddComponent<Leaf>();
        leaf.ownerNode    = node;
        leaf.tipOffset    = offset;
        leaf.targetScale  = Vector3.one * seasonLeafScale;
        if (skeleton.species != null)
        {
            leaf.springColor = skeleton.species.leafSpringColor;
            leaf.growDays    = skeleton.species.leafGrowDays;
            leaf.youngTint   = skeleton.species.leafBudBreakColor;
        }

        // Per-leaf colour variety (milder than needle tufts) — property block only,
        // shared material and batching untouched.
        float lv = Random.Range(0.90f, 1.07f);
        leaf.tint = new Color(lv * Random.Range(0.96f, 1.04f), lv, lv * Random.Range(0.95f, 1.03f));

        leaf.ApplyDeformation(
            twistDeg:     Random.Range(-28f, 28f),
            curlFraction: Random.Range(0.05f, 0.30f)
        );

        return go;
    }

    /// <summary>
    /// Spawns a single procedural needle tuft at a branch tip (conifers). The tuft mesh
    /// holds the whole bundle of needles, so this is one object per tip — not per needle —
    /// and every tuft uses one of the tree's few pooled variant meshes + needleMat so the renderer
    /// batches them. Reuses the Leaf component for scale-in, node-tracking, seasonal colour
    /// and (deciduous conifers only) the autumn fall.
    /// </summary>
    GameObject SpawnNeedleTuft(TreeNode node)
    {
        EnsureNeedleAssets();

        Vector3 branchWorldDir = skeleton.transform.TransformDirection(node.growDirection).normalized;
        if (branchWorldDir.sqrMagnitude < 0.001f) branchWorldDir = Vector3.up;

        var go = new GameObject("NeedleTuft");
        go.transform.SetParent(skeleton.transform, worldPositionStays: false);
        go.transform.localPosition = node.tipPosition;
        // Outward orientation + free random roll — same mesh, varied look.
        go.transform.rotation   = Quaternion.LookRotation(branchWorldDir, Vector3.up)
                                * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
        go.transform.localScale = Vector3.zero;   // Leaf.Start scales it in

        // Variant by node id — stable across rebuilds, varied across the canopy.
        go.AddComponent<MeshFilter>().sharedMesh =
            needleTuftVariants[Mathf.Abs(node.id) % needleTuftVariants.Length];
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial    = needleMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var leaf         = go.AddComponent<Leaf>();
        leaf.ownerNode   = node;
        leaf.tipOffset   = Vector3.zero;
        leaf.targetScale = Vector3.one * seasonLeafScale;
        if (skeleton.species != null)
        {
            leaf.springColor = skeleton.species.leafSpringColor;
            leaf.growDays    = skeleton.species.leafGrowDays;
            leaf.youngTint   = skeleton.species.leafBudBreakColor;
        }
        // Per-tuft colour variety via the Leaf's MaterialPropertyBlock (per-renderer, so
        // the one shared material — and batching — is untouched): subtle value jitter with
        // a whisper of hue drift. Old interior growth reads darker, fresh tips brighter.
        float v = Random.Range(0.82f, 1.10f);
        leaf.tint = new Color(v * Random.Range(0.94f, 1.05f), v, v * Random.Range(0.92f, 1.06f));
        // No ApplyDeformation — that twist/curl is for flat leaf blades, not needle tufts.
        return go;
    }

    // ── Year-round evergreen needle shed ──────────────────────────────────────

    /// <summary>Low-rate ambient shed: real evergreens hold each needle 2–4 years and
    /// drop spent interior needles continuously. Expected rate scales with canopy size
    /// (tuft count), triples in autumn, and is capped by maxAirborneShedNeedles so it
    /// stays cheap at any timescale.</summary>
    void UpdateNeedleShed()
    {
        if (needleShedPer100TuftsPerDay <= 0f || nodeLeaves.Count == 0) return;

        airborneShedNeedles.RemoveAll(go => go == null);
        if (airborneShedNeedles.Count >= maxAirborneShedNeedles) return;

        float days       = Time.deltaTime * GameManager.TIMESCALE / 24f;
        float seasonMult = (GameManager.month >= 9 && GameManager.month <= 11) ? 3f : 1f;
        shedAccumulator += nodeLeaves.Count * 0.01f * needleShedPer100TuftsPerDay * seasonMult * days;

        while (shedAccumulator >= 1f && airborneShedNeedles.Count < maxAirborneShedNeedles)
        {
            shedAccumulator -= 1f;
            SpawnShedNeedle();
        }
        // Anything the cap refused this frame is dropped, not banked — a fast-forwarded
        // winter shouldn't dump a season of needles the moment the cap frees up.
        if (airborneShedNeedles.Count >= maxAirborneShedNeedles) shedAccumulator = 0f;
    }

    void SpawnShedNeedle()
    {
        if (listDirty) RebuildFlatList();
        if (allLeaves.Count == 0) return;
        var (_, tuftGo) = allLeaves[Random.Range(0, allLeaves.Count)];
        if (tuftGo == null) return;

        EnsureNeedleAssets();
        if (shedNeedleMesh == null) shedNeedleMesh = NeedleMesh.BuildSingleNeedle();

        var go = new GameObject("ShedNeedle");
        go.transform.position   = tuftGo.transform.position;
        go.transform.rotation   = Random.rotationUniform;
        go.transform.localScale = Vector3.one * seasonLeafScale;
        go.AddComponent<MeshFilter>().sharedMesh = shedNeedleMesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial    = needleMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // Reuse Leaf's fall path (drift + tumble + self-destroy). StartFalling before
        // Start() is fine — Start skips the grow-in shrink for already-falling leaves.
        var leaf = go.AddComponent<Leaf>();
        leaf.ownerNode   = null;
        leaf.targetScale = Vector3.one * seasonLeafScale;
        leaf.springColor = new Color(0.48f, 0.40f, 0.16f);   // spent-needle brown
        leaf.tint        = Color.white;
        leaf.StartFalling();
        airborneShedNeedles.Add(go);
    }

    /// <summary>
    /// Computes the tree's photosynthetic energy from the current canopy.
    /// Called at bud-set (end of summer) so it reflects the peak-season canopy.
    /// Returns a value clamped to [0, maxMultiplier]:
    ///   0   = no leaves at all
    ///   1.0 = full healthy canopy (all potential terminals have leaves at full health)
    ///   >1  = extra-lush bonus, capped at maxMultiplier
    /// </summary>
    public float ComputeTreeEnergy(List<TreeNode> allNodes, float maxMultiplier)
    {
        float potential = 0f;
        float actual    = 0f;

        foreach (var node in allNodes)
        {
            if (node.isRoot || node.isTrimmed || !node.isTerminal) continue;
            if (node.depth < minLeafDepth)    continue;
            if (node.subdivisionsLeft > 0)    continue;
            if (!node.hasLeaves)              continue;  // new-season terminals haven't leafed out yet

            if (!nodeLeaves.TryGetValue(node.id, out var leaves)) continue;
            potential += leaves.Count;
            actual    += leaves.Count * Mathf.Clamp01(node.health);
        }

        if (potential <= 0f) return 1f;  // seedling with no leaves yet — grow at default rate
        return Mathf.Clamp(actual / potential, 0f, maxMultiplier);
    }

    // ── Defoliation ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the given node currently has a live leaf cluster.
    /// Used by TreeInteraction to filter hover candidates.
    /// </summary>
    public bool NodeHasLeaves(int nodeId) => nodeLeaves.ContainsKey(nodeId);

    /// <summary>Live leaf count on a node — used by TreeSkeleton's elastic leaf-load sag.</summary>
    public int NodeLeafCount(int nodeId) =>
        nodeLeaves.TryGetValue(nodeId, out var list) ? list.Count : 0;

    /// <summary>
    /// Strips the leaf cluster from a single node (fall animation).
    /// Increases defoliationFactor proportionally and marks ancestors for back-budding.
    /// </summary>
    public void DefoliateNode(TreeNode node)
    {
        if (!nodeLeaves.ContainsKey(node.id)) return;
        TrainingRecorder.Instance?.RecordAction("Defoliate", node.id);

        // Strip leaves with fall animation
        StartFallingCluster(node.id);
        node.hasLeaves = false;

        // Stimulate back-buds on up to 2 ancestors (same as pinch)
        TreeNode ancestor = node.parent;
        for (int i = 0; i < 2 && ancestor != null; i++)
        {
            ancestor.backBudStimulated = true;
            ancestor = ancestor.parent;
        }

        // Bump defoliationFactor — proportional to fraction of leaf nodes stripped
        int total = 0;
        foreach (var n in skeleton.allNodes)
            if (!n.isRoot && !n.isTrimmed && n.isTerminal) total++;
        defoliationFactor = Mathf.Clamp01(defoliationFactor + (total > 0 ? 1f / total : 0.1f));

        Debug.Log($"[Defoliate] Stripped node={node.id} | defoliationFactor={defoliationFactor:F2}");
    }

    /// <summary>
    /// Strips all leaf clusters from the tree at once.
    /// Sets defoliationFactor to 1.0 (maximum miniaturization effect next spring).
    /// </summary>
    public void DefoliateAll()
    {
        TrainingRecorder.Instance?.RecordAction("DefoliateAll", -1);
        var ids = new List<int>(nodeLeaves.Keys);
        foreach (int id in ids)
            StartFallingCluster(id);

        // Clear hasLeaves on all branch nodes
        foreach (var node in skeleton.allNodes)
        {
            if (!node.isRoot) node.hasLeaves = false;
            // Back-bud stimulation on all non-root nodes
            if (!node.isRoot && !node.isTrimmed)
                node.backBudStimulated = true;
        }

        defoliationFactor = 1f;
        Debug.Log($"[Defoliate] Full defoliation | defoliationFactor=1.0 | year={GameManager.year} month={GameManager.month}");
    }

    /// <summary>
    /// Destroys all live leaf GameObjects and clears the tracking dictionaries.
    /// Called by SaveManager.Load() before re-spawning leaves from saved data.
    /// </summary>
    /// <summary>Instantly removes all leaves — called on tree death.</summary>
    public void DropAllLeaves() => ClearAllLeaves();

    public void ClearAllLeaves()
    {
        foreach (var kvp in nodeLeaves)
            foreach (var go in kvp.Value)
                if (go != null) Destroy(go);
        nodeLeaves.Clear();
        allLeaves.Clear();
        listDirty = false;

        pendingUnfurls.Clear();
        foreach (var b in openingBuds.Values)
            if (b.go != null) Destroy(b.go);
        openingBuds.Clear();
    }

    /// <summary>
    /// Updates each live leaf's fungal severity based on its owner node's fungalLoad,
    /// then forces a colour refresh. Call from buttonClicker or TreeSkeleton each season.
    /// Leaves in fall season are not affected (fall gradient takes priority).
    /// </summary>
    public void RefreshFungalTint(TreeSkeleton skel)
    {
        if (skel == null) return;
        // Build a quick id→node lookup
        var nodeById = new Dictionary<int, TreeNode>(skel.allNodes.Count);
        foreach (var n in skel.allNodes) nodeById[n.id] = n;

        foreach (var (nodeId, go) in allLeaves)
        {
            if (go == null) continue;
            var leaf = go.GetComponent<Leaf>();
            if (leaf == null) continue;
            if (nodeById.TryGetValue(nodeId, out var node))
            {
                leaf.fungalSeverity = node.fungalLoad;
                leaf.ForceRefreshColor();
            }
        }
    }

    /// <summary>
    /// Spawns leaf clusters on the given nodes unconditionally — no bud check.
    /// Used by the trim-undo system to restore leaves after a undo without waiting
    /// for the next growing season.
    /// </summary>
    public void ForceSpawnLeaves(List<TreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.isTrimmed)                    continue;
            if (node.isRoot)                       continue;
            if (node.depth < minLeafDepth)         continue;
            if (nodeLeaves.ContainsKey(node.id))   continue;
            SpawnCluster(node);
        }
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Removes leaf clusters from nodes that:
    ///   - Were trimmed (node.isTrimmed)
    ///   - Gained children (no longer terminal)
    /// Called at the start of each growing season.
    /// </summary>
    /// <summary>Destroy all foliage and clear per-tree state. Called by
    /// TreeSkeleton.InitTree: nodeLeaves is keyed by node id and ids restart from 0 on
    /// a new tree, so stale entries survive the id-based orphan cleanup and make
    /// SpawnSpringLeaves skip colliding tips — sparse canopy → low treeEnergy →
    /// stunted second-in-session trees (found 2026-07-02). Also drops the cached
    /// needle assets so a species change rebuilds them.</summary>
    public void ResetForNewTree()
    {
        foreach (var kvp in nodeLeaves)
            foreach (var go in kvp.Value)
                if (go != null) Destroy(go);
        nodeLeaves.Clear();
        allLeaves.Clear();
        listDirty = false;

        pendingUnfurls.Clear();
        foreach (var ob in openingBuds.Values)
            if (ob.go != null) Destroy(ob.go);
        openingBuds.Clear();

        foreach (var go in airborneShedNeedles)
            if (go != null) Destroy(go);
        airborneShedNeedles.Clear();
        shedAccumulator   = 0f;
        defoliationFactor = 0f;

        if (needleTuftVariants != null)
            foreach (var m in needleTuftVariants)
                if (m != null) Destroy(m);
        needleTuftVariants = null;
        if (needleMat      != null) { Destroy(needleMat);      needleMat      = null; }
        if (shedNeedleMesh != null) { Destroy(shedNeedleMesh); shedNeedleMesh = null; }
    }

    void CleanupOrphanedLeaves()
    {
        // Only remove leaves from trimmed or fully removed nodes.
        // Nodes that simply gained children this spring keep their leaves and
        // drop them naturally in LeafFall — removing them here would race with
        // TreeSkeleton.StartNewGrowingSeason (which runs first) and cause the
        // "ungrows last segment" visual glitch.
        var liveUntrimmed = new HashSet<int>();
        foreach (var node in skeleton.allNodes)
            if (!node.isTrimmed) liveUntrimmed.Add(node.id);

        // Evergreen needles never drop in autumn, so the only chance to keep foliage at the
        // live canopy tips is here: retire tufts on nodes that grew children (now interior).
        HashSet<int> liveTerminals = null;
        if (IsEvergreen)
        {
            liveTerminals = new HashSet<int>();
            foreach (var node in skeleton.allNodes)
                if (node.isTerminal && !node.isTrimmed && !node.isRoot)
                    liveTerminals.Add(node.id);
        }

        var toRemove = new List<int>();
        foreach (var kvp in nodeLeaves)
        {
            if (!liveUntrimmed.Contains(kvp.Key)) { toRemove.Add(kvp.Key); continue; }
            if (liveTerminals != null && !liveTerminals.Contains(kvp.Key)) toRemove.Add(kvp.Key);
        }

        foreach (int id in toRemove)
            StartFallingCluster(id);
    }

    // Starts the fall animation on all leaves in a cluster, then removes the cluster
    // from tracking so it is no longer managed. Each Leaf self-destructs after falling.
    void StartFallingCluster(int nodeId)
    {
        if (!nodeLeaves.TryGetValue(nodeId, out var list)) return;

        foreach (var go in list)
            if (go != null) go.GetComponent<Leaf>()?.StartFalling();

        nodeLeaves.Remove(nodeId);
        listDirty = true;
    }


    // ── Leaf fall ─────────────────────────────────────────────────────────────

    void RollLeafFall()
    {
        if (allLeaves.Count == 0) return;

        float inGameDays = Time.deltaTime * GameManager.TIMESCALE / 24f;

        // Iterate backward so we can remove mid-loop
        for (int i = allLeaves.Count - 1; i >= 0; i--)
        {
            var (nodeId, go) = allLeaves[i];
            if (go == null) { allLeaves.RemoveAt(i); continue; }

            var leaf = go.GetComponent<Leaf>();
            if (leaf == null) { allLeaves.RemoveAt(i); continue; }

            // Lazy fallback: if StartLeafFallSeason was missed, start it now
            if (!leaf.IsInFallSeason)
                leaf.StartLeafFallSeason(Random.Range(0.4f, 2.2f));

            // Fall chance scales with color progress:
            //   green  (0) → 0.2x base   (rare but possible)
            //   brown  (1) → 3.0x base   (very likely)
            float chance = baseFallChancePerDay * inGameDays * Mathf.Lerp(0.2f, 3f, leaf.FallColorProgress);

            if (Random.value < chance)
            {
                if (verboseLog) Debug.Log($"[Leaves] Leaf falling! progress={leaf.FallColorProgress:F2} chance={chance:F4}");
                leaf.StartFalling();

                if (nodeLeaves.TryGetValue(nodeId, out var list))
                {
                    list.Remove(go);
                    if (list.Count == 0)
                        nodeLeaves.Remove(nodeId);
                }

                allLeaves.RemoveAt(i);
            }
        }
    }

    // ── Flat list helper ──────────────────────────────────────────────────────

    void RebuildFlatList()
    {
        allLeaves.Clear();
        foreach (var kvp in nodeLeaves)
            foreach (var go in kvp.Value)
                if (go != null) allLeaves.Add((kvp.Key, go));

        listDirty = false;
    }
}
