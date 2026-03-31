using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Draws 2–3 wire binding loops that encircle both the rock and the tree trunk,
/// representing the Ishitsuki training wire. Follows the same silver → gold → red
/// colour lifecycle as branch wires.
///
/// Spawned at runtime by RockPlacer when the player confirms orientation.
/// </summary>
public class IshitsukiWire : MonoBehaviour
{
    [Tooltip("Number of horizontal binding loops along the rock's height.")]
    [SerializeField] int   loopCount    = 3;

    [Tooltip("Number of sample points per loop. Higher = smoother silhouette.")]
    [SerializeField] int   loopSamples  = 32;

    [Tooltip("World units the wire sits outside the rock/trunk surface.")]
    [SerializeField] float wireOffset   = 0.04f;

    [Tooltip("Line width of the wire.")]
    [SerializeField] float lineWidth    = 0.025f;

    [Tooltip("Rate-adjusted in-game days for the wire to fully set (~2 growing seasons).")]
    [SerializeField] float wireDaysToSet = 196f;

    // ── Runtime state ─────────────────────────────────────────────────────────

    Collider  rockCollider;
    Transform trunkTransform;
    float     trunkRadius;

    public float WireSetProgress    { get; private set; }
    public float WireDamageProgress { get; private set; }

    readonly List<LineRenderer> loops = new List<LineRenderer>();
    bool initialized;

    // ── Unity ─────────────────────────────────────────────────────────────────

    void OnEnable()  => GameManager.OnGameStateChanged += OnGameStateChanged;
    void OnDisable() => GameManager.OnGameStateChanged -= OnGameStateChanged;

    void OnGameStateChanged(GameState state) { }

    void Update()
    {
        if (!initialized) return;

        // Tick progress — SeasonalGrowthRate is 0 outside growing months,
        // so this is naturally a no-op during dormancy.
        float rate      = GameManager.SeasonalGrowthRate;
        float daysDelta = Time.deltaTime * GameManager.TIMESCALE / 24f;

        if (WireSetProgress < 1f)
            WireSetProgress = Mathf.Min(1f, WireSetProgress + daysDelta * rate / wireDaysToSet);
        else
            WireDamageProgress = Mathf.Min(1f, WireDamageProgress + daysDelta * rate / wireDaysToSet);

        // Update all loop colours
        Color col = WireColor();
        foreach (var lr in loops)
            if (lr != null) lr.material.color = col;
    }

    // ── Initialisation ────────────────────────────────────────────────────────

    /// <summary>
    /// Call once after spawning to provide the rock collider, trunk transform,
    /// and trunk base radius. Immediately builds loop geometry.
    /// </summary>
    public void Init(Collider rock, Transform trunk, float trunkRad)
    {
        rockCollider   = rock;
        trunkTransform = trunk;
        trunkRadius    = trunkRad;
        initialized    = true;
        BuildLoops();
    }

    // ── Loop geometry ─────────────────────────────────────────────────────────

    void BuildLoops()
    {
        if (rockCollider == null) return;

        Bounds  b      = rockCollider.bounds;
        Vector3 center = b.center;

        // Centre the loops on the midpoint between rock centre and trunk base (XZ only)
        if (trunkTransform != null)
            center = new Vector3(
                (center.x + trunkTransform.position.x) * 0.5f,
                center.y,
                (center.z + trunkTransform.position.z) * 0.5f);

        float searchRadius = b.extents.magnitude * 3f;

        for (int i = 0; i < loopCount; i++)
        {
            float   t = (i + 1f) / (loopCount + 1f);
            float   y = Mathf.Lerp(b.min.y + 0.05f, b.max.y - 0.05f, t);
            Vector3 loopCenter = new Vector3(center.x, y, center.z);

            var go = new GameObject($"_IshitsukiLoop_{i}");
            go.transform.SetParent(transform, false);

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace     = true;
            lr.loop              = true;
            lr.positionCount     = loopSamples;
            lr.startWidth        = lineWidth;
            lr.endWidth          = lineWidth;
            lr.material          = new Material(Shader.Find("Unlit/Color"));
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows    = false;

            var positions = new Vector3[loopSamples];
            for (int s = 0; s < loopSamples; s++)
            {
                float   angle = s * Mathf.PI * 2f / loopSamples;
                Vector3 dir   = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

                // Rock: ray from outside inward to find the surface in this direction.
                // Physics.Raycast works on any MeshCollider (convex or not), unlike
                // Physics.ClosestPoint which requires convex.
                Vector3 farPt   = loopCenter + dir * searchRadius;
                Vector3 inward  = (loopCenter - farPt).normalized;
                float   rockDist = b.extents.magnitude; // fallback = rough bounding-sphere radius
                if (Physics.Raycast(farPt, inward, out RaycastHit wireHit, searchRadius * 2f))
                    rockDist = new Vector2(wireHit.point.x - loopCenter.x,
                                          wireHit.point.z - loopCenter.z).magnitude;

                // Trunk: project trunk XZ onto this direction, add radius
                float trunkDist = 0f;
                if (trunkTransform != null)
                {
                    float toTrunkX = trunkTransform.position.x - loopCenter.x;
                    float toTrunkZ = trunkTransform.position.z - loopCenter.z;
                    float proj     = toTrunkX * dir.x + toTrunkZ * dir.z;
                    trunkDist = proj + trunkRadius;
                }

                float dist = Mathf.Max(rockDist, Mathf.Max(trunkDist, 0f)) + wireOffset;
                positions[s] = loopCenter + dir * dist;
            }

            lr.SetPositions(positions);
            loops.Add(lr);
        }
    }

    // ── Removal detection ────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the ray passes within <paramref name="maxDist"/> world units
    /// of any point on any binding loop. Used by TreeInteraction for removal picking.
    /// </summary>
    public bool IsNearRay(Ray ray, float maxDist)
    {
        var buf = new Vector3[loopSamples];
        foreach (var lr in loops)
        {
            if (lr == null) continue;
            lr.GetPositions(buf);
            for (int i = 0; i < buf.Length; i++)
            {
                Vector3 toPoint = buf[i] - ray.origin;
                float   along   = Vector3.Dot(toPoint, ray.direction);
                if (along < 0f) continue;
                Vector3 closest = ray.origin + ray.direction * along;
                if (Vector3.Distance(closest, buf[i]) < maxDist)
                    return true;
            }
        }
        return false;
    }

    // ── Wire colour ───────────────────────────────────────────────────────────

    Color WireColor()
    {
        if (WireSetProgress < 1f)
        {
            return Color.Lerp(
                new Color(0.55f, 0.55f, 0.60f),
                new Color(0.80f, 0.80f, 0.85f),
                WireSetProgress);
        }

        if (WireDamageProgress < 0.01f)
        {
            float pulse = (Mathf.Sin(Time.time * 4f) + 1f) * 0.5f;
            return Color.Lerp(
                new Color(0.85f, 0.68f, 0.05f),
                new Color(1.0f,  0.92f, 0.20f),
                pulse);
        }

        if (WireDamageProgress < 0.5f)
            return Color.Lerp(
                new Color(0.95f, 0.72f, 0.05f),
                new Color(0.92f, 0.38f, 0.04f),
                WireDamageProgress * 2f);

        return Color.Lerp(
            new Color(0.92f, 0.38f, 0.04f),
            new Color(0.50f, 0.02f, 0.02f),
            (WireDamageProgress - 0.5f) * 2f);
    }
}
