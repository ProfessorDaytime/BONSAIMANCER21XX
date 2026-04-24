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

    // Gradient: green → yellow → orange → red → brown
    static readonly Color[] FallGradient =
    {
        new Color(0.15f, 0.55f, 0.10f),  // green
        new Color(0.85f, 0.82f, 0.08f),  // yellow
        new Color(0.92f, 0.38f, 0.04f),  // orange
        new Color(0.72f, 0.08f, 0.04f),  // red
        new Color(0.32f, 0.16f, 0.04f),  // brown
    };

    // Number of in-game days for a speed-1 leaf to go fully green→brown.
    const float FALL_COLOR_DAYS = 25f;

    // ── Scale-in ──────────────────────────────────────────────────────────────

    const float SCALE_DURATION = 1.5f;
    float scaleTimer = 0f;

    // ── Fall animation ────────────────────────────────────────────────────────

    bool    isFalling = false;
    float   fallTimer;
    Vector3 driftVelocity;   // world-space, set in StartFalling after unparenting
    float   rotSpeed;

    // ── Rendering ─────────────────────────────────────────────────────────────

    Renderer              leafRenderer;
    MaterialPropertyBlock propBlock;

    // ── Unity ─────────────────────────────────────────────────────────────────

    void Start()
    {
        leafRenderer = GetComponentInChildren<Renderer>();
        propBlock    = new MaterialPropertyBlock();

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
        // Track owner node so wire-bending keeps leaves attached to their branch
        if (!isFalling && ownerNode != null)
            transform.localPosition = ownerNode.tipPosition + tipOffset;

        // Spring scale-in (skip once falling so the fall animation keeps the correct scale)
        if (!isFalling && scaleTimer < SCALE_DURATION)
        {
            scaleTimer += Time.deltaTime;
            transform.localScale = targetScale * Mathf.SmoothStep(0f, 1f, scaleTimer / SCALE_DURATION);
        }

        // Autumn colour progression
        if (isInFallSeason)
        {
            float inGameDays  = Time.deltaTime * GameManager.TIMESCALE / 24f;
            fallColorProgress = Mathf.Min(1f, fallColorProgress + inGameDays * fallColorSpeed / FALL_COLOR_DAYS);
            UpdateLeafColor();

            // Fully browned — force fall regardless of LeafManager's roll
            if (fallColorProgress >= 1f && !isFalling)
                StartFalling();
        }

        // Fall animation
        if (!isFalling) return;

        fallTimer += Time.deltaTime;

        transform.position  += driftVelocity * Time.deltaTime;
        transform.Rotate(Vector3.forward, rotSpeed * Time.deltaTime, Space.Self);
        driftVelocity.y     -= 1.2f * Time.deltaTime;

        if (fallTimer > 5f)
            Destroy(gameObject);
    }

    // ── Colour ────────────────────────────────────────────────────────────────

    static readonly Color FungalSicklyColor = new Color(0.68f, 0.72f, 0.18f); // sickly yellow-green

    void UpdateLeafColor()
    {
        if (leafRenderer == null) return;

        Color color;
        if (isInFallSeason)
        {
            float t = fallColorProgress * (FallGradient.Length - 1);
            int   i = Mathf.Clamp((int)t, 0, FallGradient.Length - 2);
            color = Color.Lerp(FallGradient[i], FallGradient[i + 1], t - i);
        }
        else
        {
            color = Color.Lerp(springColor, FungalSicklyColor, Mathf.Clamp01(fungalSeverity));
        }

        leafRenderer.GetPropertyBlock(propBlock);
        propBlock.SetColor("_Color", color);      // Standard / Unlit shaders
        propBlock.SetColor("_BaseColor", color);  // URP shaders
        leafRenderer.SetPropertyBlock(propBlock);
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

        // Snap to full size if the leaf hasn't finished scaling in yet
        if (scaleTimer < SCALE_DURATION)
        {
            scaleTimer           = SCALE_DURATION;
            transform.localScale = targetScale;
        }

        transform.SetParent(null, worldPositionStays: true);
        driftVelocity = new Vector3(
            Random.Range(-0.3f, 0.3f),
            Random.Range(-0.2f, 0f),
            Random.Range(-0.3f, 0.3f)
        );
        rotSpeed = Random.Range(-60f, 60f);
    }
}
