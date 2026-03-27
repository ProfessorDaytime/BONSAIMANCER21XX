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

    // Tight-angle geometry: when the angle between a parent and child growDirection
    // exceeds this threshold, intermediate rings are inserted at the base of the
    // child segment to smooth the bend instead of pinching the vertices.
    const float BEND_THRESHOLD_DEG  = 20f;   // minimum angle that triggers extra rings
    const float BEND_RING_FRACTION   = 0.15f; // extra rings span first 15% of the child's length

    // References

    TreeSkeleton  skeleton;
    Mesh          mesh;
    MeshCollider  meshCollider;
    bool          isDirty;

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
        meshCollider = GetComponent<MeshCollider>();

        if (skeleton == null)
            Debug.LogError("TreeMeshBuilder: no TreeSkeleton found on this GameObject.", this);
    }

    public void SetDirty() => isDirty = true;

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

    void BuildMesh()
    {
        if (skeleton.root == null) return;

        if (renderRoots)
        {
            int rootNodes = 0, rootTerminals = 0, rootDepthMax = 0;
            foreach (var n in skeleton.allNodes)
            {
                if (!n.isRoot) continue;
                rootNodes++;
                if (n.isTerminal) rootTerminals++;
                if (n.depth > rootDepthMax) rootDepthMax = n.depth;
            }
            Debug.Log($"[GRoot] BuildMesh | rootNodes={rootNodes} rootTerminals={rootTerminals} maxRootDepth={rootDepthMax} totalNodes={skeleton.allNodes.Count}");
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

        // Underground stub: reuses the trunk base ring (indices 0..ringSegments) as the
        // top of a short downward cylinder, sealed with a flat bottom cap.
        // Skipped for Ishitsuki (tree is on a rock, not planted underground).
        if (rockCollider == null)
            AddUndergroundCap(skeleton.root);

        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetUVs(0, uvs);
        mesh.SetColors(colors);

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        // Refresh collider -- measure separately, it's usually the most expensive part
        colliderTimer.Restart();
        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = mesh;
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
        // Thin branches get a green new-growth tint; thick branches are bark-colored.
        Color baseColor = (node.isGrowing && showGrowingDebugColor) ? growingDebugColor : GrowthColor(node.radius,    node.age, node.isRoot);
        Color tipColor  = (node.isGrowing && showGrowingDebugColor) ? growingDebugColor : GrowthColor(node.tipRadius, node.age, node.isRoot);

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
            // Air layer roots always render.
            // Normal roots: show in RootPrune mode, OR when their base is above the visibility threshold.
            if (child.isRoot && !child.isAirLayerRoot && !renderRoots && child.worldPosition.y < rootVisibilityDepth) continue;
            // Skip zero-length children: they produce degenerate (zero-area) triangles
            // that corrupt RecalculateNormals at the shared tip ring, causing a one-frame
            // visual glitch where the parent tip looks wrong at the start of each season.
            // The parent keeps its end cap until the child grows past zero.
            if (child.length <= 0f) continue;
            hasRenderedChild = true;
            // Root children attach at the trunk BASE (worldPosition), not the tip.
            // Pass -1 so they generate a fresh base ring at their own position rather
            // than inheriting the trunk tip ring, which would stretch triangles wildly.
            int childBase = (!node.isRoot && child.isRoot) ? -1 : tipRingStart;
            ProcessNode(child, childBase, tipHeight, frameRight, axisUp);
        }

        // Cap the open tip ring on terminal (leaf) nodes so no hollow end is visible.
        // Fan from a center point outward: winding capCenter->r1->r0 produces a normal
        // pointing in the +axisUp direction (outward from the cut face).
        if (!hasRenderedChild)
        {
            int capCenter   = vertices.Count;
            int capTriStart = triangles.Count;

            vertices.Add(node.tipPosition);
            uvs.Add(new Vector2(0.5f, tipHeight * 0.4f));
            colors.Add(tipColor);

            for (int i = 0; i < ringSegments; i++)
            {
                int r0 = tipRingStart + i;
                int r1 = tipRingStart + i + 1;
                triangles.Add(capCenter); triangles.Add(r1); triangles.Add(r0);
            }
            triRanges.Add((capTriStart, triangles.Count, node));
        }

        return tipRingStart;
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
            if (gripCurrentNode && rockCollider != null)
            {
                Vector3 worldPos  = transform.TransformPoint(localPos);
                Vector3 closestPt = Physics.ClosestPoint(worldPos, rockCollider,
                    rockCollider.transform.position, rockCollider.transform.rotation);
                if (Vector3.Distance(worldPos, closestPt) < 0.12f)
                {
                    Vector3 outward = closestPt - rockCollider.bounds.center;
                    if (outward.sqrMagnitude < 0.001f) outward = Vector3.up;
                    localPos = transform.InverseTransformPoint(closestPt + outward.normalized * 0.008f);
                }
            }

            vertices.Add(localPos);
            uvs.Add(new Vector2(t, heightV * 0.4f));
            colors.Add(vertexColor);
        }
    }

    /// <summary>
    /// Returns the vertex color for a ring of the given radius.
    /// alpha = bark blend weight: 0 = fully new growth, 1 = fully bark.
    /// vertex.r encodes node type for the shader:
    ///   0 = branch (new growth = green _NGColor)
    ///   1 = root   (new growth = white _NGRootColor)
    /// </summary>
    Color GrowthColor(float radius, float age, bool isRoot = false)
    {
        float radiusT = Mathf.InverseLerp(thinRadius, thickRadius, radius);
        float ageT    = newGrowthFadeDays > 0f ? Mathf.Clamp01(age / newGrowthFadeDays) : 1f;
        float t       = Mathf.Max(radiusT, ageT);
        float rootBit = isRoot ? 1f : 0f;
        return new Color(rootBit, 1f, 1f, t);
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

        Color   color     = GrowthColor(root.radius, root.age, false);
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
}
