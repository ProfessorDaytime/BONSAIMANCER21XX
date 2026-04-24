using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Reads the TreeSkeleton graph and builds ONE unified Mesh for the whole tree.
///
/// Why one mesh?
///   The old system gave each cone its own GameObject + Mesh. Unity lights each
///   mesh independently from its local origin, so normals restart at every junction
///   and you get visible seams. A single mesh means vertex normals at junctions are
///   shared across both the parent's top ring and the child's bottom ring -- Unity's
///   RecalculateNormals() then averages all contributing face normals at those shared
///   vertices, producing smooth, continuous shading across the entire tree.
///
/// How the junction sharing works:
///   When we process a node, we generate its tip ring and store its start index.
///   Every child of that node is told "your base ring starts at index X" -- the same
///   index the parent's tip ring used. The tip ring vertices are therefore referenced
///   by the parent's quad strip AND each child's quad strip. RecalculateNormals()
///   sees all contributing face normals and averages them, giving a smooth blend.
///
/// Mesh is only rebuilt when SetDirty() is called -- not every frame -- unless a node
/// is actively growing or bending (Phase 2/5 will call SetDirty each frame for those).
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class TreeMeshBuilder : MonoBehaviour
{
    // Settings

    [Tooltip("Number of sides on each cylinder segment. 8 is fine for a bonsai.")]
    [SerializeField] int ringSegments = 8;

    // V-axis UV tiling scale. Matches the original hardcoded 0.4f by default.
    // Set from species.barkVTiling in ApplySpeciesColors (also defaults to 0.4f).
    float barkVTilingScale = 0.4f;

    // Tight-angle geometry: when the angle between a parent and child growDirection
    // exceeds this threshold, intermediate rings are inserted at the base of the
    // child segment to smooth the bend instead of pinching the vertices.
    const float BEND_THRESHOLD_DEG  = 20f;   // minimum angle that triggers extra rings
    const float BEND_RING_FRACTION   = 0.15f; // extra rings span first 15% of the child's length

    // References

    TreeSkeleton  skeleton;
    Mesh          mesh;
    MeshRenderer  meshRenderer;
    Material      _dbgRainbowMat;
    Material      _dbgHealthMat;
    MeshCollider  meshCollider;
    bool          isDirty;

    [Tooltip("Draw green→yellow→red rings on each branch node to visualise node health. Toggle at runtime.")]
    public bool debugHealthRings = false;

    [Tooltip("Draw colored lines on every root node to diagnose root visibility bugs.\n" +
             "Green  = included in mesh  (passes visibility check)\n" +
             "Red    = excluded by depth threshold (y < rootVisibilityDepth)\n" +
             "Cyan   = isTrainingWire (always rendered)\n" +
             "Yellow = isAirLayerRoot (always rendered)\n" +
             "Lines draw through geometry (ZTest Always) so they're visible in Game View.")]
    public bool debugRootVisibility = false;

    /// <summary>
    /// When false (default), only roots above rootVisibilityDepth are rendered.
    /// Set to true by TreeSkeleton when entering RootPrune mode (shows all roots).
    /// </summary>
    [HideInInspector] public bool renderRoots = false;

    /// <summary>Rock collider for Ishitsuki mesh gripping. Set by RockPlacer on confirm.</summary>
    [HideInInspector] public Collider rockCollider;

    // Set each ProcessNode call — tells AddRing whether to grip the rock surface.
    bool gripCurrentNode;

    [Header("Root Visibility")]
    [Tooltip("Roots whose local-Y base position is at or above this value render in normal mode. " +
             "Roots below this depth are hidden unless RootPrune mode is active. " +
             "0 = soil surface; negative values reveal shallow underground roots.")]
    [SerializeField] [Range(-0.5f, 0.5f)] public float rootVisibilityDepth = 0f;

    // New-growth tint settings

    [Tooltip("Branches at or below this radius get the full new-growth tint. " +
             "Match this to TreeSkeleton's Terminal Radius (default 0.04).")]
    [SerializeField] float thinRadius = 0.04f;

    [Tooltip("Branches at or above this radius show no tint (pure bark colour). " +
             "Anything between thinRadius and thickRadius is linearly blended.")]
    [SerializeField] float thickRadius = 0.14f;

    [Tooltip("Rate-adjusted in-game days before a branch fully fades from new growth to bark. " +
             "The fade uses whichever comes first: enough time passing OR the branch getting thick.")]
    [SerializeField] float newGrowthFadeDays = 150f;

    [Tooltip("Growing seasons until a wound fully fades from the vertex color channel.\n" +
             "Controls how long wound / callus visual remains visible after healing.")]
    [SerializeField] float woundFadeSeasons = 8f;

    [Tooltip("When enabled, still-growing segments are highlighted with the debug colour below.")]
    [SerializeField] bool showGrowingDebugColor = false;

    [Tooltip("Colour used to highlight segments that are still actively growing. " +
             "Alpha controls texture visibility: 0 = solid colour only, 1 = full bark texture. " +
             "Keep alpha at 0 for a clearly visible debug highlight.")]
    [SerializeField] Color growingDebugColor = new Color(0f, 0f, 1f, 0f);

    // Build Buffers (reused each rebuild to avoid GC pressure)

    readonly List<Vector3> vertices  = new List<Vector3>();
    readonly List<int>     triangles = new List<int>();
    readonly List<Vector2> uvs       = new List<Vector2>();
    readonly List<Color>   colors    = new List<Color>();

    // Triangle Range -> Node mapping (for Phase 3 tool hit detection)

    // Key:   first triangle INDEX (not count) in the triangles list for this node
    // Value: the TreeNode
    // After a ray cast hit, use RaycastHit.triangleIndex to look up which node was struck.
    public readonly List<(int triStart, int triEnd, TreeNode node)> triRanges
        = new List<(int, int, TreeNode)>();

    // Unity

    void Awake()
    {
        skeleton     = GetComponent<TreeSkeleton>();
        mesh             = new Mesh();
        mesh.name        = "BonsaiTree";
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;  // supports 4 billion vertices; default 16-bit caps at 65535
        GetComponent<MeshFilter>().mesh = mesh;
        meshCollider  = GetComponent<MeshCollider>();
        // Skip mesh cleaning — it fails on transiently degenerate geometry during early growth.
        meshCollider.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation
                                    | MeshColliderCookingOptions.WeldColocatedVertices;
        meshRenderer  = GetComponent<MeshRenderer>();

        if (skeleton == null)
            Debug.LogError("TreeMeshBuilder: no TreeSkeleton found on this GameObject.", this);
    }

    public void SetDirty() => isDirty = true;

    /// <summary>
    /// Pushes the current species' bark colors to the MeshRenderer's material.
    /// Safe to call any time species changes (species select, load, etc.).
    /// No-op when species is null or renderer has no material.
    /// </summary>
    public void ApplySpeciesColors()
    {
        if (meshRenderer == null || skeleton?.species == null) return;

        // material creates a per-instance copy on first access — fine for a single tree.
        var mat = meshRenderer.material;
        mat.SetColor("_NGColor",     skeleton.species.youngBarkColor);
        mat.SetColor("_BarkColor",   skeleton.species.matureBarkColor);
        mat.SetColor("_NGRootColor", skeleton.species.rootNewGrowthColor);
        mat.SetInt  ("_BarkType",    skeleton.species.barkType);

        // Bark texture tiers (optional pixel-art textures)
        bool hasTex = skeleton.species.youngBarkTexture != null || skeleton.species.matureBarkTexture != null;
        mat.SetFloat("_UseTextures",   hasTex ? 1f : 0f);
        mat.SetFloat("_TexelRes",      skeleton.species.barkTexelRes);
        mat.SetFloat("_BarkNoiseMode", skeleton.species.barkNoiseMode);
        if (skeleton.species.youngBarkTexture  != null) mat.SetTexture("_BarkTexA", skeleton.species.youngBarkTexture);
        if (skeleton.species.matureBarkTexture != null) mat.SetTexture("_BarkTexB", skeleton.species.matureBarkTexture);

        barkVTilingScale = skeleton.species.barkVTiling;
    }

    bool isDead = false;

    /// <summary>
    /// When dead=true, overrides all vertex colours to a grey-brown dead-wood tint.
    /// Call with false to restore normal colouring (e.g. after a load).
    /// </summary>
    public void SetDeadTint(bool dead)
    {
        isDead = dead;
        SetDirty();
    }

    void LateUpdate()
    {
        if (!isDirty) return;
        BuildMesh();
        isDirty = false;
    }

    // Mesh Construction

    // Perf tracking
    readonly Stopwatch buildTimer    = new Stopwatch();
    readonly Stopwatch colliderTimer = new Stopwatch();
    float meshLogTimer = 0f;

    // Accumulated stats between log intervals
    long   totalBuildMs    = 0;
    long   totalColliderMs = 0;
    int    buildCount      = 0;

    // Change-detection for [RootVis] log — only log when state differs
    bool _prevRootVis_renderRoots = false;
    int  _prevRootVis_included    = -1;
    int  _prevRootVis_skipped     = -1;

    void BuildMesh()
    {
        if (skeleton.root == null) return;

        if (renderRoots || debugRootVisibility)
        {
            int rootNodes = 0, rootTerminals = 0, rootDepthMax = 0;
            int wouldSkip = 0, wouldInclude = 0;
            foreach (var n in skeleton.allNodes)
            {
                if (!n.isRoot) continue;
                rootNodes++;
                if (n.isTerminal) rootTerminals++;
                if (n.depth > rootDepthMax) rootDepthMax = n.depth;

                // Replicate the exact skip condition from ProcessNode
                bool skipped = n.isRoot && !n.isAirLayerRoot && !n.isTrainingWire
                               && !renderRoots && n.worldPosition.y < rootVisibilityDepth;
                if (skipped) wouldSkip++; else wouldInclude++;
            }
            // Only log when renderRoots mode or included/skipped counts change — not every dirty rebuild
            bool stateChanged = renderRoots   != _prevRootVis_renderRoots
                             || wouldInclude  != _prevRootVis_included
                             || wouldSkip     != _prevRootVis_skipped;
            if (stateChanged)
            {
                _prevRootVis_renderRoots = renderRoots;
                _prevRootVis_included    = wouldInclude;
                _prevRootVis_skipped     = wouldSkip;
                Debug.Log($"[RootVis] year={GameManager.year} BuildMesh | renderRoots={renderRoots} threshold={rootVisibilityDepth:F3} " +
                          $"| rootNodes={rootNodes} terminals={rootTerminals} maxDepth={rootDepthMax} " +
                          $"| included={wouldInclude} skipped={wouldSkip}");
            }
        }

        buildTimer.Restart();

        vertices.Clear();
        triangles.Clear();
        uvs.Clear();
        colors.Clear();
        triRanges.Clear();

        // Traverse the node graph depth-first.
        // -1 signals "generate a base ring for me" (only the root node needs this).
        ProcessNode(skeleton.root, -1, 0f, Vector3.zero, Vector3.zero);

        // Seal the trunk base with an underground cylinder stub in all modes.
        // In Ishitsuki the rock sits in a pot of soil — the stub provides visual grounding
        // and hides the open bottom regardless of rock orientation.
        AddUndergroundCap(skeleton.root);

        // Override all vertex colours to dead-wood grey when tree has died
        if (isDead)
        {
            var deadCol = new Color(0.35f, 0.30f, 0.25f);   // ashen grey-brown
            for (int i = 0; i < colors.Count; i++) colors[i] = deadCol;
        }

        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetUVs(0, uvs);
        mesh.SetColors(colors);

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        // Apply species bark colors to the material (no-op if no species assigned).
        ApplySpeciesColors();

        // Refresh collider -- measure separately, it's usually the most expensive part
        colliderTimer.Restart();
        if (IsMeshColliderSafe())
        {
            // Re-apply options each time — Unity can reset them when sharedMesh is cleared.
            meshCollider.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation
                                        | MeshColliderCookingOptions.WeldColocatedVertices;
            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = mesh;
        }
        colliderTimer.Stop();

        buildTimer.Stop();

        totalBuildMs    += buildTimer.ElapsedMilliseconds;
        totalColliderMs += colliderTimer.ElapsedMilliseconds;
        buildCount++;

        // Log accumulated stats once per second
        meshLogTimer += Time.deltaTime;
        if (meshLogTimer >= 1f)
        {
            meshLogTimer = 0f;
            int nodes = skeleton.allNodes?.Count ?? 0;
            Debug.Log($"[Mesh] nodes={nodes} verts={vertices.Count} tris={triangles.Count / 3} " +
                      $"| builds/s={buildCount} " +
                      $"avgBuild={( buildCount > 0 ? totalBuildMs / buildCount : 0)}ms " +
                      $"avgCollider={( buildCount > 0 ? totalColliderMs / buildCount : 0)}ms");
            totalBuildMs    = 0;
            totalColliderMs = 0;
            buildCount      = 0;
        }
    }

    bool IsMeshColliderSafe()
    {
        if (vertices.Count < 3 || triangles.Count < 3) return false;

        // Reject non-finite vertices.
        foreach (var v in vertices)
            if (!float.IsFinite(v.x) || !float.IsFinite(v.y) || !float.IsFinite(v.z))
                return false;

        // Reject any zero-area triangle — PhysX mesh cleaning fails on these.
        const float kMinAreaSq = 1e-10f;
        for (int i = 0; i < triangles.Count; i += 3)
        {
            Vector3 a = vertices[triangles[i]];
            Vector3 b = vertices[triangles[i + 1]];
            Vector3 c = vertices[triangles[i + 2]];
            float areaSq = Vector3.Cross(b - a, c - a).sqrMagnitude;
            if (areaSq < kMinAreaSq) return false;
        }
        return true;
    }

    /// <summary>
    /// Recursively processes one node:
    ///   1. Uses the provided base ring (or generates one for the root).
    ///   2. Generates the tip ring.
    ///   3. Connects them with quads.
    ///   4. Passes the tip ring index to each child as their base ring.
    ///
    /// Returns the vertex index at which this node's tip ring begins, so the
    /// caller (parent) can hand it off to sibling children.
    /// </summary>
    /// <param name="node">Node to process.</param>
    /// <param name="baseRingStart">
    ///   Index in the vertex list where this node's base ring starts.
    ///   Pass -1 for the root node; the method generates the ring itself.
    /// </param>
    /// <param name="cumulativeHeight">
    ///   Running world-height from the root, used for V coordinate so bark
    ///   texture tiles continuously up the trunk regardless of segment boundaries.
    /// </param>
    /// <param name="frameRight">
    ///   The axisRight used when the base ring was generated. Passed down so each
    ///   child can inherit the parent's ring orientation via parallel transport,
    ///   preventing the quad strips from twisting at junctions.
    ///   Pass Vector3.zero for the root -- it computes a fresh frame.
    /// </param>
    /// <param name="parentDir">
    ///   The growDirection of the parent node (normalised).
    ///   Pass Vector3.zero for the root -- it skips the bend-ring logic.
    /// </param>
    int ProcessNode(TreeNode node, int baseRingStart, float cumulativeHeight,
                    Vector3 frameRight, Vector3 parentDir)
    {
        // Resolve frame
        // Save the incoming frame (oriented to the parent's direction) before
        // re-orthogonalising it to THIS node's direction. The bend rings below
        // need to transport through intermediate directions from incomingFrame.

        Vector3 axisUp       = node.growDirection.normalized;
        Vector3 incomingFrame = frameRight;   // frame still aligned to parent's axis

        if (frameRight.sqrMagnitude > 0.001f)
            frameRight = Vector3.ProjectOnPlane(frameRight, axisUp).normalized;

        if (frameRight.sqrMagnitude < 0.001f)
        {
            frameRight = Vector3.Cross(axisUp, Vector3.up);
            if (frameRight.sqrMagnitude < 0.001f)
                frameRight = Vector3.Cross(axisUp, Vector3.forward);
            frameRight = frameRight.normalized;
        }

        // Flag AddRing to push verts onto rock surface for Ishitsuki gripping.
        // Applies to all nodes — the 0.12 m distance guard in AddRing keeps it safe.
        gripCurrentNode = rockCollider != null;

        // Compute vertex colors for this segment.
        // Still-growing segments are tinted deep blue for debugging.
        // Exposed roots (above soil line) bark faster than buried ones.
        bool isExposed = node.isRoot && node.worldPosition.y > rootVisibilityDepth;

        // Dead / deadwood override: ashen grey-brown tint
        Color baseColor, tipColor;
        if (node.isDeadwood)
        {
            baseColor = tipColor = new Color(0.38f, 0.32f, 0.26f);   // silver-grey deadwood
        }
        else if (node.isDead)
        {
            // Fading dead branch: lerp toward grey as deadSeasons ticks up
            float fade = Mathf.Clamp01(node.deadSeasons / 2f);
            Color live = GrowthColor(node, node.radius, isExposed);
            Color dead = new Color(0.35f, 0.30f, 0.25f);
            baseColor = tipColor = Color.Lerp(live, dead, fade);
        }
        else
        {
            baseColor = (node.isGrowing && showGrowingDebugColor) ? growingDebugColor : GrowthColor(node, node.radius,    isExposed);
            tipColor  = (node.isGrowing && showGrowingDebugColor) ? growingDebugColor : GrowthColor(node, node.tipRadius, isExposed);
        }


        // Base ring

        if (baseRingStart < 0)
        {
            baseRingStart = vertices.Count;
            AddRing(node.worldPosition, axisUp, frameRight, node.radius, cumulativeHeight, baseColor);
        }

        // Bend rings
        // When the angle from parent to this node exceeds BEND_THRESHOLD_DEG,
        // insert intermediate rings that Slerp from parentDir to axisUp.
        // They are placed in the first BEND_RING_FRACTION of the node's length
        // so the bend is concentrated near the junction, matching how real wood bends.

        int     currentBase  = baseRingStart;
        Vector3 bendFrame    = incomingFrame; // transported through each intermediate dir

        if (parentDir.sqrMagnitude > 0.001f)
        {
            float bendAngle = Vector3.Angle(parentDir, axisUp);

            if (bendAngle > BEND_THRESHOLD_DEG)
            {
                // One ring per BEND_THRESHOLD_DEG of bend, capped at 4
                int count = Mathf.Clamp(Mathf.FloorToInt(bendAngle / BEND_THRESHOLD_DEG), 1, 4);

                for (int b = 1; b <= count; b++)
                {
                    float   t       = (float)b / (count + 1);
                    Vector3 bendDir = Vector3.Slerp(parentDir, axisUp, t).normalized;

                    // Parallel-transport bendFrame to bendDir
                    bendFrame = Vector3.ProjectOnPlane(bendFrame, bendDir).normalized;
                    if (bendFrame.sqrMagnitude < 0.001f)
                    {
                        bendFrame = Vector3.Cross(bendDir, Vector3.up);
                        if (bendFrame.sqrMagnitude < 0.001f)
                            bendFrame = Vector3.Cross(bendDir, Vector3.forward);
                        bendFrame = bendFrame.normalized;
                    }

                    // Position: fraction of the way along this segment
                    float   ringT  = t * BEND_RING_FRACTION;
                    Vector3 ringPos = node.worldPosition + axisUp * (node.length * ringT);
                    float   ringH   = cumulativeHeight + node.length * ringT;
                    float   ringR   = Mathf.Lerp(node.radius, node.tipRadius, ringT);
                    Color   ringC   = Color.Lerp(baseColor, tipColor, ringT);

                    int newRing = vertices.Count;
                    AddRing(ringPos, bendDir, bendFrame, ringR, ringH, ringC);

                    // Connect the previous ring to this bend ring
                    int bendTriStart = triangles.Count;
                    for (int i = 0; i < ringSegments; i++)
                    {
                        int b0 = currentBase + i,  b1 = currentBase + i + 1;
                        int r0 = newRing     + i,  r1 = newRing     + i + 1;
                        triangles.Add(b0); triangles.Add(r0); triangles.Add(b1);
                        triangles.Add(b1); triangles.Add(r0); triangles.Add(r1);
                    }
                    triRanges.Add((bendTriStart, triangles.Count, node));

                    currentBase = newRing;
                }

                // After bend rings, re-project the transported bendFrame onto axisUp
                // so the tip ring uses a frame consistent with the last bend ring.
                // Without this, the tip ring falls back to an independent computation
                // that can differ by ~90 degrees, making the final quad strip twist visibly.
                Vector3 transported = Vector3.ProjectOnPlane(bendFrame, axisUp).normalized;
                if (transported.sqrMagnitude > 0.001f)
                    frameRight = transported;
            }
        }

        // Tip ring
        // Uses frameRight (now consistent with the last bend ring if any were inserted).

        float tipHeight    = cumulativeHeight + node.length;
        int   tipRingStart = vertices.Count;
        AddRing(node.tipPosition, axisUp, frameRight, node.tipRadius, tipHeight, tipColor);

        // Quads (currentBase -> tip)
        // currentBase is either the original base ring (no bend) or the last
        // bend ring inserted above.

        int triStart = triangles.Count;

        for (int i = 0; i < ringSegments; i++)
        {
            int b0 = currentBase  + i;
            int b1 = currentBase  + i + 1;
            int t0 = tipRingStart + i;
            int t1 = tipRingStart + i + 1;

            triangles.Add(b0); triangles.Add(t0); triangles.Add(b1);
            triangles.Add(b1); triangles.Add(t0); triangles.Add(t1);
        }

        triRanges.Add((triStart, triangles.Count, node));

        // Recurse into children.
        // Pass axisUp as parentDir so each child can detect its own bend angle.
        // Root nodes are only rendered when renderRoots is true (RootPrune mode).
        // Track whether any child was rendered; if not, add a cap to close the tip.

        bool hasRenderedChild = false;
        foreach (var child in node.children)
        {
            // Air layer roots and Ishitsuki training cables always render regardless of depth.
            // Normal roots: show in RootPrune mode, OR when their base is above the visibility threshold.
            if (child.isRoot && !child.isAirLayerRoot && !child.isTrainingWire && !renderRoots && child.worldPosition.y < rootVisibilityDepth) continue;
            // Skip zero-length children: they produce degenerate (zero-area) triangles
            // that corrupt RecalculateNormals at the shared tip ring, causing a one-frame
            // visual glitch where the parent tip looks wrong at the start of each season.
            // The parent keeps its end cap until the child grows past zero.
            if (child.length <= 0f) continue;
            hasRenderedChild = true;
            // Root children attach at the trunk BASE (worldPosition), not the tip.
            // Pass -1 so they generate a fresh base ring at their own position rather
            // than inheriting the trunk tip ring, which would stretch triangles wildly.
            // Cut-site children also get a fresh base ring — the cap stays as a flat disc
            // and the shoot mesh starts small, completely detached, until the cap is absorbed.
            int childBase = (!node.isRoot && child.isRoot) ? -1
                          : (node.isTrimCutPoint && node.hasWound) ? -1
                          : tipRingStart;
            ProcessNode(child, childBase, tipHeight, frameRight, axisUp);
        }

        // Cap the open tip ring:
        //   • Always for terminal nodes (close the hollow end).
        //   • Also for cut-site nodes whose wound is still open: children grow laterally
        //     off the stump face so the flat disc must stay visible behind them.
        bool needsCap = !hasRenderedChild || (node.isTrimCutPoint && node.hasWound);
        if (needsCap)
        {
            int capTriStart = triangles.Count;

            if (node.hasWound && !node.isRoot)
                AddWoundCap(node, tipRingStart, tipHeight, axisUp, frameRight, tipColor);
            else
                AddFlatCap(node.tipPosition, tipRingStart, tipHeight, tipColor);

            triRanges.Add((capTriStart, triangles.Count, node));
        }

        return tipRingStart;
    }

    // Cap helpers

    /// <summary>Simple flat fan cap — used for healthy terminal nodes.</summary>
    void AddFlatCap(Vector3 center, int ringStart, float heightV, Color col)
    {
        int capCenter = vertices.Count;
        vertices.Add(center);
        uvs.Add(new Vector2(0.5f, heightV * 0.4f));
        colors.Add(col);

        for (int i = 0; i < ringSegments; i++)
        {
            int r0 = ringStart + i, r1 = ringStart + i + 1;
            triangles.Add(capCenter); triangles.Add(r1); triangles.Add(r0);
        }
    }

    /// <summary>
    /// Wound cap — replaces the flat disc with organic callus geometry:
    ///
    ///   1. Callus swell — outer ring vertices pushed outward so the branch lip
    ///      looks slightly swollen at the cut site.
    ///   2. Concave face — center depressed inward along -axisUp; depth fades to
    ///      zero as woundAge / seasonsToHeal approaches 1.
    ///   3. Callus crescent — once woundAge > 20 % healed, a second inner ring
    ///      at a smaller radius represents the callus roll closing inward.
    ///
    /// Vertex.g (wound intensity) is set high at the exposed face center and
    /// falls off toward the outer ring so the shader blends heartwood → callus.
    /// </summary>
    void AddWoundCap(TreeNode node, int outerRingStart, float heightV,
                     Vector3 axisUp, Vector3 axisRight, Color baseCol)
    {
        float outerR = node.tipRadius;

        // Heal progress 0 = fresh, 1 = fully healed
        float seasonsToHeal = Mathf.Max(1f, node.woundRadius * skeleton.SeasonsToHealPerUnit);
        float healProg      = Mathf.Clamp01(node.woundAge / seasonsToHeal);

        // Wound intensity for face vertices — full when fresh, zero when healed
        float woundIntensity = Mathf.Clamp01(1f - node.woundAge / Mathf.Max(1f, woundFadeSeasons));
        float pasteB         = node.pasteApplied ? 1f : 0f;
        float rootBit        = 0f;   // always a branch node here

        Color faceCol = new Color(rootBit, woundIntensity, pasteB, baseCol.a);
        Color rimCol  = new Color(rootBit, woundIntensity * 0.4f, pasteB, baseCol.a);

        // ── Callus swell: push outer ring vertices outward ────────────────────
        // We DON'T move the existing ring — instead we add a duplicate swollen ring
        // just above the tip so the cap triangles see the extra width.
        float swellAmount  = outerR * 0.12f * (1f - healProg);   // shrinks to zero as healed
        Vector3 axisFwd    = Vector3.Cross(axisRight, axisUp).normalized;
        int swellRingStart = vertices.Count;

        for (int i = 0; i <= ringSegments; i++)
        {
            float t     = (float)i / ringSegments;
            float angle = t * Mathf.PI * 2f;
            Vector3 dir = (axisRight * Mathf.Cos(angle) + axisFwd * Mathf.Sin(angle)).normalized;
            Vector3 pos = node.tipPosition + dir * (outerR + swellAmount);
            vertices.Add(pos);
            uvs.Add(new Vector2(t, heightV * 0.4f));
            colors.Add(rimCol);
        }

        // Connect the outer tip ring to the swell ring (a thin band)
        for (int i = 0; i < ringSegments; i++)
        {
            int b0 = outerRingStart + i,  b1 = outerRingStart + i + 1;
            int s0 = swellRingStart + i,  s1 = swellRingStart + i + 1;
            triangles.Add(b0); triangles.Add(s0); triangles.Add(b1);
            triangles.Add(b1); triangles.Add(s0); triangles.Add(s1);
        }

        // ── Concave face center ───────────────────────────────────────────────
        float depression  = outerR * 0.35f * (1f - healProg);    // fills in as healed
        Vector3 faceCenter = node.tipPosition - axisUp * depression;

        // ── Inner callus ring (appears once healing starts) ───────────────────
        // Radius shrinks inward as callus closes; absent when healProg = 0
        bool hasInnerRing  = healProg > 0.15f;
        float innerR       = outerR * (1f - healProg * 0.8f);     // closes to 20 % of outer
        int innerRingStart = -1;

        if (hasInnerRing)
        {
            innerRingStart = vertices.Count;
            Color innerCol = new Color(rootBit, woundIntensity * 0.2f, pasteB, Mathf.Lerp(baseCol.a, 1f, 0.5f));

            for (int i = 0; i <= ringSegments; i++)
            {
                float t     = (float)i / ringSegments;
                float angle = t * Mathf.PI * 2f;
                Vector3 dir = (axisRight * Mathf.Cos(angle) + axisFwd * Mathf.Sin(angle)).normalized;
                Vector3 pos = node.tipPosition + dir * innerR - axisUp * (depression * 0.4f);
                vertices.Add(pos);
                uvs.Add(new Vector2(t, heightV * 0.4f));
                colors.Add(innerCol);
            }

            // Band from swell ring → inner ring
            for (int i = 0; i < ringSegments; i++)
            {
                int s0 = swellRingStart  + i, s1 = swellRingStart  + i + 1;
                int n0 = innerRingStart  + i, n1 = innerRingStart  + i + 1;
                triangles.Add(s0); triangles.Add(n0); triangles.Add(s1);
                triangles.Add(s1); triangles.Add(n0); triangles.Add(n1);
            }
        }

        // ── Face fan to center ────────────────────────────────────────────────
        int fanBase = hasInnerRing ? innerRingStart : swellRingStart;
        int centerIdx = vertices.Count;
        vertices.Add(faceCenter);
        uvs.Add(new Vector2(0.5f, heightV * 0.4f));
        colors.Add(faceCol);

        for (int i = 0; i < ringSegments; i++)
        {
            int r0 = fanBase + i, r1 = fanBase + i + 1;
            triangles.Add(centerIdx); triangles.Add(r1); triangles.Add(r0);
        }
    }

    // Ring Generation

    /// <summary>
    /// Appends (ringSegments + 1) vertices forming a circle perpendicular to
    /// <paramref name="direction"/> at <paramref name="center"/> with the given radius.
    ///
    /// The +1 duplicates the first vertex at the end with U=1.0 so UV mapping
    /// has a clean seam rather than a hard wrap from 1 back to 0.
    /// </summary>
    void AddRing(Vector3 center, Vector3 axisUp, Vector3 axisRight, float radius, float heightV,
                 Color vertexColor)
    {
        // axisUp and axisRight are already normalised and orthogonal (done in ProcessNode).
        Vector3 axisFwd = Vector3.Cross(axisRight, axisUp).normalized;

        for (int i = 0; i <= ringSegments; i++)
        {
            float t     = (float)i / ringSegments;
            float angle = t * Mathf.PI * 2f;
            float cos   = Mathf.Cos(angle);
            float sin   = Mathf.Sin(angle);

            Vector3 localPos = center + (axisRight * cos + axisFwd * sin) * radius;

            // Ishitsuki mesh gripping: push root ring verts to the rock surface
            // so the mesh hugs the rock rather than clipping through it.
            // Physics.ClosestPoint only works on convex MeshColliders — guard to avoid
            // a warning flood that crashes VS Code on non-convex rock meshes.
            var  rockMC_  = rockCollider as MeshCollider;
            bool canGrip_ = rockCollider != null && (rockMC_ == null || rockMC_.convex);
            if (gripCurrentNode && canGrip_)
            {
                Vector3 worldPos  = transform.TransformPoint(localPos);
                Vector3 closestPt = Physics.ClosestPoint(worldPos, rockCollider,
                    rockCollider.transform.position, rockCollider.transform.rotation);
                // Only push vertices that are genuinely inside or flush with the rock.
                // A large threshold here snaps exterior vertices of adjacent rings to
                // different rock faces, producing distorted quads. 0.01 m catches only
                // truly interior / flush verts without deforming surrounding geometry.
                if (Vector3.Distance(worldPos, closestPt) < 0.01f)
                {
                    Vector3 outward = closestPt - rockCollider.bounds.center;
                    if (outward.sqrMagnitude < 0.001f) outward = Vector3.up;
                    localPos = transform.InverseTransformPoint(closestPt + outward.normalized * 0.008f);
                }
            }

            vertices.Add(localPos);
            uvs.Add(new Vector2(t, heightV * barkVTilingScale));
            colors.Add(vertexColor);
        }
    }

    /// How many single-child hops from this node to the chain terminal.
    static int RootDepthFromTip(TreeNode node)
    {
        int d = 0;
        var n = node;
        while (n.children.Count == 1 && n.children[0].isRoot) { d++; n = n.children[0]; }
        return d;
    }

    /// Rainbow: 0=Red 1=Orange 2=Yellow 3=Green 4=Cyan 5=NavyBlue 6=DeepPurple 7+=Magenta
    static Color RootRainbowColor(int d)
    {
        switch (d)
        {
            case 0:  return Color.red;
            case 1:  return new Color(1f, 0.5f, 0f);          // orange
            case 2:  return Color.yellow;
            case 3:  return Color.green;
            case 4:  return Color.cyan;
            case 5:  return new Color(0f, 0f, 0.6f);           // navy
            case 6:  return new Color(0.3f, 0f, 0.6f);         // deep purple
            case 7:  return Color.magenta;
            default: return Color.white;
        }
    }

    /// <summary>
    /// Vertex color encoding (full, used for branch/root geometry):
    ///   R = isRoot flag     (0 = branch,  1 = root)
    ///   G = wound intensity (0 = healthy, 1 = fresh wound, fades with woundAge)
    ///   B = paste applied   (0 = none,    1 = paste sealed)
    ///   A = bark blend      (0 = new growth, 1 = mature bark)
    /// </summary>
    Color GrowthColor(TreeNode node, float radius, bool isExposed)
    {
        float fadeDays = (node.isRoot && isExposed && newGrowthFadeDays > 0f)
            ? newGrowthFadeDays / 3f
            : newGrowthFadeDays;
        float radiusT = Mathf.InverseLerp(thinRadius, thickRadius, radius);
        float ageT    = fadeDays > 0f ? Mathf.Clamp01(node.age / fadeDays) : 1f;
        float t       = Mathf.Max(radiusT, ageT);
        float rootBit = node.isRoot ? 1f : 0f;

        float woundG = 0f;
        float pasteB = 0f;
        if (!node.isRoot && node.hasWound)
        {
            woundG = woundFadeSeasons > 0f
                ? Mathf.Clamp01(1f - node.woundAge / woundFadeSeasons)
                : 1f;
            pasteB = node.pasteApplied ? 1f : 0f;
        }

        return new Color(rootBit, woundG, pasteB, t);
    }

    /// <summary>
    /// Simplified overload for underground / cap geometry that carries no wound data.
    /// </summary>
    Color GrowthColor(float radius, float age, bool isRoot = false, bool isExposed = false)
    {
        float fadeDays = (isRoot && isExposed && newGrowthFadeDays > 0f)
            ? newGrowthFadeDays / 3f
            : newGrowthFadeDays;
        float radiusT = Mathf.InverseLerp(thinRadius, thickRadius, radius);
        float ageT    = fadeDays > 0f ? Mathf.Clamp01(age / fadeDays) : 1f;
        float t       = Mathf.Max(radiusT, ageT);
        float rootBit = isRoot ? 1f : 0f;
        return new Color(rootBit, 0f, 0f, t);
    }

    /// <summary>
    /// Seals the trunk base ring with a flat downward-facing cap (Ishitsuki mode).
    /// No underground cylinder — the rock surface is directly below the trunk.
    /// </summary>
    void AddTrunkBaseCap(TreeNode root)
    {
        if (root == null) return;

        Color color = GrowthColor(root.radius, root.age, true);

        // Fan the existing trunk base ring (vertices 0..ringSegments) to a center point.
        // Winding: capCenter → r0 → r1  →  normal points down (away from tree).
        int capCenter = vertices.Count;
        vertices.Add(root.worldPosition);
        uvs.Add(new Vector2(0.5f, 0f));
        colors.Add(color);

        for (int i = 0; i < ringSegments; i++)
        {
            int r0 = i, r1 = i + 1;
            triangles.Add(capCenter); triangles.Add(r0); triangles.Add(r1);
        }
    }

    /// <summary>
    /// Appends a short underground cylinder below the trunk base ring plus a flat
    /// bottom cap, so the mesh is sealed at the soil line.
    /// The trunk base ring (always at vertex indices 0..ringSegments after ProcessNode)
    /// is reused as the cylinder top — no seam.
    /// </summary>
    void AddUndergroundCap(TreeNode root)
    {
        if (root == null) return;

        const float undergroundDepth = 0.25f;

        Color   color     = GrowthColor(root.radius, root.age, true);
        Vector3 botCenter = root.worldPosition + Vector3.down * undergroundDepth;

        // Match the orientation the trunk base ring was built with:
        // axisUp=up → axisRight=right, axisFwd=forward (Cross(right,up)=forward).
        Vector3 axisRight = Vector3.right;
        Vector3 axisFwd   = Vector3.forward;

        // Bottom ring
        int topRingStart = 0;               // trunk base ring, built first in ProcessNode
        int botRingStart = vertices.Count;

        for (int i = 0; i <= ringSegments; i++)
        {
            float   t     = (float)i / ringSegments;
            float   angle = t * Mathf.PI * 2f;
            Vector3 pos   = botCenter
                          + (axisRight * Mathf.Cos(angle) + axisFwd * Mathf.Sin(angle)) * root.radius;
            vertices.Add(pos);
            uvs.Add(new Vector2(t, undergroundDepth * 0.4f));
            colors.Add(color);
        }

        // Cylinder walls: same winding as ProcessNode (lower ring = b, upper ring = t → outward normals)
        for (int i = 0; i < ringSegments; i++)
        {
            int b0 = botRingStart + i,   b1 = botRingStart + i + 1;
            int t0 = topRingStart + i,   t1 = topRingStart + i + 1;
            triangles.Add(b0); triangles.Add(t0); triangles.Add(b1);
            triangles.Add(b1); triangles.Add(t0); triangles.Add(t1);
        }

        // Bottom cap — fan, normal pointing down (opposite of tip cap winding)
        int capCenter = vertices.Count;
        vertices.Add(botCenter);
        uvs.Add(new Vector2(0.5f, undergroundDepth * 0.4f));
        colors.Add(color);
        for (int i = 0; i < ringSegments; i++)
        {
            int r0 = botRingStart + i, r1 = botRingStart + i + 1;
            triangles.Add(capCenter); triangles.Add(r0); triangles.Add(r1);
        }
    }

    // Public API (Phase 3 tool interaction)

    /// <summary>
    /// Given a triangle index from a RaycastHit, returns the TreeNode that
    /// owns that triangle. Returns null if not found.
    /// </summary>
    public TreeNode NodeFromTriangleIndex(int triangleIndex)
    {
        // triRanges stores [triStart, triEnd) in terms of INDEX into triangles list.
        // RaycastHit.triangleIndex is the triangle number (0-based), so multiply by 3
        // to get the index into the triangles array.
        int idx = triangleIndex * 3;
        foreach (var (triStart, triEnd, node) in triRanges)
        {
            if (idx >= triStart && idx < triEnd)
                return node;
        }
        return null;
    }

    // ── Root rainbow GL overlay ───────────────────────────────────────────────
    /*
    void OnRenderObject()
    {
        if (skeleton == null || skeleton.root == null) return;

        if (_dbgRainbowMat == null)
        {
            Shader sh = Shader.Find("Hidden/Internal-Colored");
            if (sh == null) return;
            _dbgRainbowMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            _dbgRainbowMat.SetInt("_ZWrite", 0);
            _dbgRainbowMat.SetInt("_Cull",   0);
            _dbgRainbowMat.SetInt("_ZTest",  (int)UnityEngine.Rendering.CompareFunction.Always);
        }

        _dbgRainbowMat.SetPass(0);
        GL.PushMatrix();
        GL.MultMatrix(transform.localToWorldMatrix);
        GL.Begin(GL.LINES);

        DrawRootRainbow(skeleton.root);

        GL.End();
        GL.PopMatrix();
    }

    void DrawRootRainbow(TreeNode node)
    {
        if (node.isRoot)
        {
            int   d    = RootDepthFromTip(node);
            Color col  = RootRainbowColor(d);
            GL.Color(col);
            GL.Vertex(node.worldPosition);
            GL.Vertex(node.tipPosition);
        }
        foreach (var child in node.children)
            DrawRootRainbow(child);
    }
    */

    // ── Health ring GL overlay ────────────────────────────────────────────────
    // Toggle debugHealthRings in the Inspector to show a green→yellow→red ring
    // at the base of every non-root branch segment, coloured by node.health.
    // Rings sit just outside the bark so the underlying geometry stays visible.

    void OnRenderObject()
    {
        if (skeleton == null || skeleton.root == null) return;

        if (_dbgHealthMat == null)
        {
            Shader sh = Shader.Find("Hidden/Internal-Colored");
            if (sh == null) return;
            _dbgHealthMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            _dbgHealthMat.SetInt("_ZWrite", 0);
            _dbgHealthMat.SetInt("_Cull",   0);
            _dbgHealthMat.SetInt("_ZTest",  (int)UnityEngine.Rendering.CompareFunction.Always);
        }

        _dbgHealthMat.SetPass(0);
        GL.PushMatrix();
        GL.MultMatrix(transform.localToWorldMatrix);
        GL.Begin(GL.LINES);

        if (debugHealthRings)
        {
            foreach (var node in skeleton.allNodes)
            {
                if (node.isRoot || node.isTrimmed) continue;

                float h   = node.health;
                Color col = h <= 0.5f
                    ? Color.Lerp(Color.red,    Color.yellow, h * 2f)
                    : Color.Lerp(Color.yellow, Color.green,  (h - 0.5f) * 2f);
                col.a = 1f;
                GL.Color(col);

                Vector3 fwd  = node.growDirection.sqrMagnitude > 0.001f
                               ? node.growDirection.normalized
                               : Vector3.up;
                Vector3 side = Vector3.Cross(fwd, Vector3.up);
                if (side.sqrMagnitude < 0.001f) side = Vector3.Cross(fwd, Vector3.right);
                side.Normalize();
                Vector3 up2  = Vector3.Cross(fwd, side).normalized;

                float   r    = node.radius * 1.25f;
                const int N  = 16;
                for (int i = 0; i < N; i++)
                {
                    float a0 = i       * Mathf.PI * 2f / N;
                    float a1 = (i + 1) * Mathf.PI * 2f / N;
                    GL.Vertex(node.worldPosition + side * (Mathf.Cos(a0) * r) + up2 * (Mathf.Sin(a0) * r));
                    GL.Vertex(node.worldPosition + side * (Mathf.Cos(a1) * r) + up2 * (Mathf.Sin(a1) * r));
                }
            }
        }

        if (debugRootVisibility)
        {
            // Draw a line from each root node's base to its tip, color-coded:
            //   Cyan   = isTrainingWire      (always rendered — Ishitsuki cable)
            //   Yellow = isAirLayerRoot      (always rendered — above-ground root)
            //   Green  = included in mesh    (renderRoots=true OR y >= threshold)
            //   Red    = excluded by depth   (y < threshold AND renderRoots=false)
            //
            // Lines always draw through geometry (ZTest Always) so they're visible
            // in Game View even when the mesh covers them.

            foreach (var node in skeleton.allNodes)
            {
                if (!node.isRoot) continue;

                Color col;
                if (node.isTrainingWire)
                    col = Color.cyan;
                else if (node.isAirLayerRoot)
                    col = Color.yellow;
                else if (renderRoots || node.worldPosition.y >= rootVisibilityDepth)
                    col = Color.green;
                else
                    col = Color.red;

                col.a = 1f;
                GL.Color(col);
                GL.Vertex(node.worldPosition);
                GL.Vertex(node.tipPosition);
            }
        }

        GL.End();
        GL.PopMatrix();
    }
}
