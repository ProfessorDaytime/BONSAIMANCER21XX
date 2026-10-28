using UnityEngine;

/// <summary>
/// Sits on each spawned leaf instance.
///
/// Spring  — scales in from zero over SCALE_DURATION seconds.
/// Autumn  — independently progresses through green→yellow→orange→red→brown
///           at a randomized speed set by LeafManager. Falls when fully brown
///           or when LeafManager's stochastic roll triggers it.
/// Fall    — drifts and rotates before destroying itself.
/// </summary>
public class Leaf : MonoBehaviour
{
    // ── Set by LeafManager on spawn ───────────────────────────────────────────

    [HideInInspector] public TreeNode ownerNode;
    [HideInInspector] public Vector3  targetScale = Vector3.one;
    // Offset from ownerNode.tipPosition in skeleton-local space.
    // Updated each frame so leaves track the node when wire-bending moves it.
    [HideInInspector] public Vector3  tipOffset;

    // ── Species colour (set by LeafManager on spawn) ─────────────────────────

    // Spring/summer leaf color. LeafManager sets this from species.leafSpringColor.
    // Defaults to the same green used in FallGradient[0] so existing trees are unchanged.
    [HideInInspector] public Color springColor = new Color(0.15f, 0.55f, 0.10f);

    // Per-instance colour variety (set by LeafManager on spawn; white = no change).
    // Applied through this leaf's MaterialPropertyBlock, so the shared material — and
    // therefore batching — is untouched. Composes with every seasonal colour state.
    [HideInInspector] public Color tint = Color.white;

    // Peak autumn colour (set from species.leafAutumnColor by LeafManager). The fall
    // sequence runs springColor → THIS → dried-dark. Replaces the old hardcoded
    // green→brown gradient that turned dawn-redwood tufts near-black instead of copper
    // and gave every species the same autumn (2026-07-03 species pass).
    [HideInInspector] public Color autumnColor = new Color(0.85f, 0.55f, 0.12f);

    // ── Fall colour (set by LeafManager when autumn begins) ───────────────────

    // How fast this leaf runs through the colour gradient relative to the base rate.
    // Randomised per-leaf — creates the natural variation where some fall at yellow,
    // others at orange/red, and the last stragglers at brown.
    [HideInInspector] public float fallColorSpeed = 1f;

    // ── Fungal tint (set by LeafManager.RefreshFungalTint) ───────────────────
    // 0 = healthy green. 1 = fully sickly. Ignored during fall season.
    [HideInInspector] public float fungalSeverity = 0f;

    // 0 = green, 1 = fully brown.  Public read so LeafManager can weight the fall roll.
    public float FallColorProgress => fallColorProgress;
    public bool  IsInFallSeason    => isInFallSeason;

    float fallColorProgress = 0f;
    bool  isInFallSeason    = false;

    // Number of in-game days for a speed-1 leaf to go fully green→brown.
    const float FALL_COLOR_DAYS = 25f;

    // ── Grow-in ───────────────────────────────────────────────────────────────
    // Leaves emerge at GROW_START_FRACTION of full size in a paler green and expand
    // over growDays in-game days — real leaves unfurl small and pale, they don't
    // pop in full-size. growDays/youngTint are set per-species by LeafManager on
    // spawn; the initializers are fallbacks for prefab-placed leaves.

    const float GROW_START_FRACTION = 0.15f;

    [HideInInspector] public float growDays  = 20f;
    [HideInInspector] public Color youngTint = new Color(0.72f, 0.88f, 0.38f);

    float growProgress = 0f;   // 0 = just emerged, 1 = full size + full color

    // ── Fall animation ────────────────────────────────────────────────────────

    bool    isFalling = false;
    float   fallTimer;
    Vector3 driftVelocity;   // world-space, set in StartFalling after unparenting
    float   rotSpeed;

    // ── Rendering ─────────────────────────────────────────────────────────────

    Renderer              leafRenderer;
    MaterialPropertyBlock propBlock;

    // ── Wind ──────────────────────────────────────────────────────────────────
    // Per-leaf phase staggers the canopy shimmer; base rotation captured in Start so
    // the wobble never accumulates. Sway rides the existing per-frame node tracking.
    float      windPhase;
    Quaternion baseLocalRotation;

    [Tooltip("Largest angle a leaf may swing about its stem attachment, in degrees.")]
    public float maxSwingDeg = 32f;

    // Branch-level wind identity: leaves sharing a branch share these, so they swing as one
    // group. Cached at Start — walking the parent chain every frame for every leaf is wasteful.
    float   branchPhase;
    Vector3 branchAnchorWorld;

    // The bark now bends in the vertex shader (BarkVertexColor TreeSway); the leaf's anchor must
    // ride the SAME displacement or the stem visibly detaches from its twig. Computed through
    // WindSway.cs — the CPU mirror of the shader function (see its MIRROR CONTRACT header).
    TreeMeshBuilder swayBuilder;

    /// <summary>Finds the branch this leaf belongs to (the nearest scaffold-level ancestor) and
    /// caches a phase + sample point from it, so a whole branch's foliage moves together.</summary>
    void CacheBranchIdentity()
    {
        var b = ownerNode;
        while (b != null && b.parent != null && b.depth > 1) b = b.parent;
        if (b == null) b = ownerNode;
        branchPhase       = b != null ? (b.id * 0.6180339f) % 1f : windPhase;   // golden-ratio spread
        // Node positions are in SKELETON space, and the leaf is parented to the skeleton — so the
        // parent does the conversion, not the leaf's own (rotated, scaled) transform.
        branchAnchorWorld = (b != null && transform.parent != null)
            ? transform.parent.TransformPoint(b.tipPosition)
            : transform.position;
    }

    // ── Unity ─────────────────────────────────────────────────────────────────

    void Start()
    {
        leafRenderer = GetComponentInChildren<Renderer>();
        propBlock    = new MaterialPropertyBlock();

        windPhase         = Random.value;
        baseLocalRotation = transform.localRotation;
        CacheBranchIdentity();
        // For riding the bark's vertex-shader sway: leaves are parented under the skeleton, so
        // the builder is findable here. Cached now — falling leaves may be reparented later.
        swayBuilder = GetComponentInParent<TreeMeshBuilder>();

        // Start small immediately — no full-size flash on the spawn frame.
        // (Unless StartFalling already ran before Start — shed needles spawn falling
        // at full scale; don't shrink them back down.)
        if (!isFalling)
            transform.localScale = targetScale * GROW_START_FRACTION;

        // If the leaf prefab has a Rigidbody, make it kinematic so the physics
        // engine doesn't override our manual transform.position changes.
        var rb = GetComponentInChildren<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity  = false;
        }

        UpdateLeafColor();   // initialise to green
    }

    void Update()
    {
        // Track owner node so wire-bending keeps leaves attached to their branch.
        // Ambient wind rides the same per-frame update: a small world-space sway offset
        // plus a rotation wobble around the captured base orientation.
        if (!isFalling && ownerNode != null)
        {
            // The stem attachment ALWAYS sits exactly on its twig. The twig itself now BENDS in
            // the bark vertex shader, so "on its twig" means node position PLUS the same sway
            // displacement the bark vertices get there — computed via WindSway (the CPU mirror
            // of the shader's TreeSway/TreeFlex). One offset for the whole leaf: the stem stays
            // welded to the moving twig, never sliding relative to it (2026-08-24 rule).
            Vector3 basePos = ownerNode.tipPosition + tipOffset;
            Vector3 swayLocal = Vector3.zero;
            if (swayBuilder != null && transform.parent != null && WindManager.CanonStrength > 0f)
            {
                float flex = swayBuilder.NodeSwayFlex(ownerNode);
                if (flex > 0f)
                {
                    Vector3 restW = transform.parent.TransformPoint(basePos);
                    swayLocal = transform.parent.InverseTransformVector(WindSway.Sway(restW, flex));
                }
            }
            transform.localPosition = basePos + swayLocal;

            var wind = WindManager.Instance;
            if (wind != null && wind.strength > 0f)
            {
                // Sample at the BRANCH, with a branch-level phase, so every leaf on the same
                // branch swings together as if the branch itself were moving — instead of each
                // leaf fluttering independently, which reads as insects rather than foliage.
                Vector3 swayW = wind.SampleSway(branchAnchorWorld, branchPhase);

                Quaternion parentRot = transform.parent != null ? transform.parent.rotation
                                                                : Quaternion.identity;
                Quaternion restWorld = parentRot * baseLocalRotation;
                Vector3    bladeDirW = restWorld * Vector3.forward;   // blade runs along local +Z

                // Only the component across the blade swings it; along-blade wind just compresses.
                Vector3 lateral = Vector3.ProjectOnPlane(swayW, bladeDirW);
                Quaternion swung = restWorld;
                if (lateral.sqrMagnitude > 1e-10f)
                {
                    // Displacement → angle about the attachment, using the blade as the lever arm.
                    float arm   = Mathf.Max(0.01f, targetScale.z);
                    float angle = Mathf.Min(maxSwingDeg,
                                            Mathf.Rad2Deg * Mathf.Atan2(lateral.magnitude, arm));
                    Vector3 axis = Vector3.Cross(bladeDirW, lateral.normalized);
                    if (axis.sqrMagnitude > 1e-10f)
                        swung = Quaternion.AngleAxis(angle, axis.normalized) * restWorld;
                }

                // Per-leaf flutter on top: a twist about the leaf's own axes, still pivoting at
                // the stem, so individual leaves shimmer within the branch's shared motion.
                float w = Mathf.Sin(Time.time * (2.1f + windPhase * 1.7f) + windPhase * 11f)
                          * 6f * wind.GustLevel;
                transform.rotation = swung * Quaternion.Euler(w, 0f, w * 0.7f);
            }
            else
            {
                transform.localRotation = baseLocalRotation;
            }
        }

        // Colour repaints are throttled to visible steps (2026-10-27). They used to run every frame
        // for every leaf through spring grow-in and all of autumn — ~2,000 property-block uploads
        // a frame on a mature tree for a colour that moves a fraction of a percent per frame.

        // Grow-in over in-game days (skip once falling so the fall keeps the correct scale)
        if (!isFalling && growProgress < 1f)
        {
            float elapsedDays = Time.deltaTime * GameManager.TIMESCALE / 24f;
            growProgress      = Mathf.Min(1f, growProgress + elapsedDays / Mathf.Max(1f, growDays));
            float s = Mathf.Lerp(GROW_START_FRACTION, 1f, Mathf.SmoothStep(0f, 1f, growProgress));
            transform.localScale = targetScale * s;
            if (growProgress >= 1f || growProgress - paintedGrow >= ColorStep)
            {
                paintedGrow = growProgress;
                UpdateLeafColor();   // young pale green darkens to the species color as it grows
            }
        }

        // Autumn colour progression. A falling leaf keeps the colour it dropped with.
        if (isInFallSeason && !isFalling)
        {
            float inGameDays  = Time.deltaTime * GameManager.TIMESCALE / 24f;
            fallColorProgress = Mathf.Min(1f, fallColorProgress + inGameDays * fallColorSpeed / FALL_COLOR_DAYS);
            if (fallColorProgress >= 1f || fallColorProgress - paintedFall >= ColorStep)
            {
                paintedFall = fallColorProgress;
                UpdateLeafColor();
            }

            // Fully browned — force fall regardless of LeafManager's roll
            if (fallColorProgress >= 1f)
                StartFalling();
        }

        // Fall animation
        if (!isFalling) return;

        fallTimer += Time.deltaTime;

        transform.position  += driftVelocity * Time.deltaTime;
        transform.Rotate(Vector3.forward, rotSpeed * Time.deltaTime, Space.Self);
        driftVelocity.y     -= 1.2f * Time.deltaTime;

        // Quick-Start: falling foliage is just debris nobody watches — vanish after a
        // few frames instead of drifting for 5 real seconds (hundreds of leaves × 40
        // autumns otherwise pile up as live GameObjects during the fast-grow).
        float fallLifetime = (QuickStartManager.Instance != null && QuickStartManager.Instance.IsGenerating)
            ? 0.05f : 5f;
        // Also gone once it has dropped well below where it let go — by then it's under the table
        // and out of shot, and there's no point animating it for the rest of the 5 seconds.
        if (fallTimer > fallLifetime || transform.position.y < despawnY)
            Destroy(gameObject);
    }

    const float ColorStep = 0.01f;   // repaint when progress has moved this much (100 steps per ramp)
    float paintedGrow = -1f, paintedFall = -1f;
    float despawnY    = float.NegativeInfinity;

    static readonly int IdColor     = Shader.PropertyToID("_Color");
    static readonly int IdBaseColor = Shader.PropertyToID("_BaseColor");

    // ── Colour ────────────────────────────────────────────────────────────────

    // Stress symptom, driven by LeafManager.UpdateStressSymptoms: the colour of the dominant
    // cause (drought, waterlogging, hunger, a failing branch) and how far this leaf has gone toward
    // it. Moves gradually in in-game days, both ways, so a fixed problem fades rather than snapping.
    Color stressColor;
    float stressAmount;
    public float StressAmount => stressAmount;

    public void TickStress(float target, Color causeColor, float onsetStep, float recoverStep)
    {
        float before = stressAmount;
        if (target > stressAmount)
        {
            stressColor  = stressAmount < 0.02f ? causeColor : Color.Lerp(stressColor, causeColor, Mathf.Clamp01(onsetStep * 4f));
            stressAmount = Mathf.Min(target, stressAmount + onsetStep);
        }
        else
        {
            stressAmount = Mathf.Max(target, stressAmount - recoverStep);
        }
        if (Mathf.Abs(stressAmount - before) > 0.002f) UpdateLeafColor();
    }

    static readonly Color FungalSicklyColor = new Color(0.68f, 0.72f, 0.18f); // sickly yellow-green

    void UpdateLeafColor()
    {
        if (leafRenderer == null) return;

        Color color;
        if (isInFallSeason)
        {
            // Two stages: turn to the species' autumn colour over the first 60% of the
            // fall, then dry/darken toward drop. Per-species — maples go crimson, birch
            // gold, dawn redwood copper — instead of one shared green→brown ramp.
            float t = fallColorProgress;
            color = t < 0.6f
                ? Color.Lerp(springColor, autumnColor, t / 0.6f)
                : Color.Lerp(autumnColor, autumnColor * 0.45f, (t - 0.6f) / 0.4f);
        }
        else
        {
            // Young leaves are paler; the species spring color develops as the leaf grows
            Color spring = Color.Lerp(Color.Lerp(springColor, youngTint, 0.65f), springColor,
                                      Mathf.SmoothStep(0f, 1f, growProgress));
            color = Color.Lerp(spring, FungalSicklyColor, Mathf.Clamp01(fungalSeverity));
            if (stressAmount > 0f) color = Color.Lerp(color, stressColor, stressAmount);
        }

        color *= tint;   // per-instance variety (white = no-op)

        leafRenderer.GetPropertyBlock(propBlock);
        propBlock.SetColor(IdColor, color);       // Standard / Unlit shaders
        propBlock.SetColor(IdBaseColor, color);   // URP shaders
        leafRenderer.SetPropertyBlock(propBlock);
    }

    /// <summary>
    /// Puts the leaf at its windless rest pose. Used by the menu-tree bake, which captures the bark
    /// at rest (its sway is in the shader, not the mesh), so the leaves must be at rest too. The
    /// next Update re-applies the wind.
    /// </summary>
    public void SnapToRest()
    {
        if (isFalling || ownerNode == null || propBlock == null) return;   // propBlock: Start has run
        transform.localPosition = ownerNode.tipPosition + tipOffset;
        transform.localRotation = baseLocalRotation;
    }

    /// <summary>Force a colour refresh — called by LeafManager after updating fungalSeverity.</summary>
    public void ForceRefreshColor() => UpdateLeafColor();

    /// <summary>
    /// Deforms this leaf's mesh in place with a random twist (rotation around the stem/Z axis,
    /// growing from base to tip) and a cup curl (blade edges droop relative to the center).
    /// Call once immediately after instantiation, before the scale-in begins.
    /// twistDeg: signed degrees of rotation at the tip (e.g. ±25).
    /// curlFraction: 0=flat, 1=edges droop by 100% of leaf length at the tip.
    /// </summary>
    public void ApplyDeformation(float twistDeg, float curlFraction)
    {
        foreach (var mf in GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            if (!mf.sharedMesh.isReadable) continue;   // Read/Write not enabled in import settings
            var mesh  = Instantiate(mf.sharedMesh);
            var verts = mesh.vertices;

            float zMin = float.MaxValue, zMax = float.MinValue, xMax = 0f;
            foreach (var v in verts)
            {
                if (v.z < zMin) zMin = v.z;
                if (v.z > zMax) zMax = v.z;
                if (Mathf.Abs(v.x) > xMax) xMax = Mathf.Abs(v.x);
            }
            float zRange = zMax - zMin;
            if (zRange < 0.0001f) { mf.mesh = mesh; continue; }

            for (int i = 0; i < verts.Length; i++)
            {
                float t = (verts[i].z - zMin) / zRange;   // 0 = stem end, 1 = leaf tip

                // Twist: rotate XY plane around Z axis, increasing toward tip
                float rad  = twistDeg * t * Mathf.Deg2Rad;
                float cosA = Mathf.Cos(rad), sinA = Mathf.Sin(rad);
                float x    = verts[i].x * cosA - verts[i].y * sinA;
                float y    = verts[i].x * sinA + verts[i].y * cosA;
                verts[i]   = new Vector3(x, y, verts[i].z);

                // Cup curl: blade edges droop downward proportional to (x/xMax)^2 * t
                if (xMax > 0.0001f)
                {
                    float xNorm  = verts[i].x / xMax;
                    verts[i].y  -= curlFraction * zRange * xNorm * xNorm * t;
                }
            }

            mesh.vertices = verts;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mf.mesh = mesh;
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by LeafManager when the LeafFall season begins.
    /// Each leaf gets a different speed so they colour and fall at staggered rates.
    /// </summary>
    public void StartLeafFallSeason(float speed)
    {
        isInFallSeason    = true;
        fallColorSpeed    = speed;
        fallColorProgress = 0f;
        paintedFall       = 0f;
        UpdateLeafColor();
    }

    /// <summary>
    /// Detaches from the tree and begins the fall animation.
    /// Safe to call multiple times — subsequent calls are ignored.
    /// </summary>
    public void StartFalling()
    {
        if (isFalling) return;
        isFalling = true;

        // Snap to full size if the leaf hasn't finished growing in yet
        if (growProgress < 1f)
        {
            growProgress         = 1f;
            transform.localScale = targetScale;
        }

        transform.SetParent(null, worldPositionStays: true);
        despawnY = transform.position.y - 3f;
        driftVelocity = new Vector3(
            Random.Range(-0.3f, 0.3f),
            Random.Range(-0.2f, 0f),
            Random.Range(-0.3f, 0.3f)
        );
        // Falling leaves blow downwind — sample the ambient wind once at release.
        var wind = WindManager.Instance;
        if (wind != null && wind.strength > 0f)
            driftVelocity += wind.SampleSway(transform.position, windPhase) * 12f;
        rotSpeed = Random.Range(-60f, 60f);

        // Reduced motion: leaves still fall, so autumn still reads, but drop straight down
        // without the drift and spin.
        if (GameSettings.ReducedMotion)
        {
            driftVelocity = new Vector3(0f, -0.1f, 0f);
            rotSpeed      = 0f;
        }
    }
}
