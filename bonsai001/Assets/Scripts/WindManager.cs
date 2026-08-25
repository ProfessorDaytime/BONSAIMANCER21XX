using UnityEngine;

/// <summary>
/// Global ambient wind for foliage motion. (F6, 2026-07-02)
///
/// Deliberately does NOT bend the trunk/branch mesh — that would force a full mesh
/// rebuild every frame (rebuilds are event-driven). Instead every Leaf / needle tuft —
/// which already tracks its node's position each frame — samples a sway offset here,
/// so the whole canopy flutters while the wood stays still. At bonsai scale that's
/// also the physically right read: the trunk is rigid, the foliage trembles.
///
/// Runs on REAL time (Time.time), independent of the game timescale, like the other
/// purely aesthetic motions (falling bark, falling leaves) — wind shouldn't become a
/// blur at Fast speed or freeze in Pause menus that keep rendering.
///
/// Setup: add a WindManager component anywhere in the scene. No references needed.
/// </summary>
public class WindManager : MonoBehaviour
{
    public static WindManager Instance { get; private set; }

    [Tooltip("Overall wind intensity. 0 = dead calm (system inert).")]
    [Range(0f, 1f)] public float strength = 0.35f;

    [Tooltip("How often gusts roll through (Perlin octave speed).")]
    [Range(0.02f, 1f)] public float gustFrequency = 0.15f;

    [Tooltip("Prevailing horizontal wind direction (normalized at sample time). " +
             "Wanders slowly on its own so the motion never loops visibly.")]
    public Vector3 baseDirection = new Vector3(1f, 0f, 0.35f);

    [Tooltip("World-space amplitude, in units, of a full-strength gust at a foliage tip.")]
    public float maxSwayAmplitude = 0.045f;

    [Header("Branch Sway (vertex shader)")]
    [Tooltip("Peak whole-branch deflection in world units at flex=1, strength=1, full gust. " +
             "The bark mesh bends this far at its thinnest extremities in a storm.")]
    public float branchSwayMax = 0.22f;

    [Tooltip("Height (world units) over which sway ramps in from the pinned trunk base.")]
    public float swayHeightRef = 1.2f;

    [Tooltip("Horizontal distance from the trunk axis where 'this is a branch, not trunk' begins. " +
             "Inside it, mature wood is rigid; beyond it, even old wood may sway.")]
    public float swayReachInner = 0.35f;

    [Tooltip("Distance beyond Reach Inner over which branch-ness saturates to 1.")]
    public float swayReachSpan = 1.2f;

    // ── Canonical published wind state ────────────────────────────────────────
    // ONE authoritative set of values, pushed to shader globals AND exposed as statics, so the
    // GPU bark sway (BarkVertexColor.shader) and every CPU rider (leaves, needle tufts, wires —
    // via WindSway.cs) sample the IDENTICAL field. If they read wind independently they drift,
    // and foliage visibly detaches from the twigs it hangs on.
    public static Vector3 CanonDir       { get; private set; } = Vector3.right;
    public static float   CanonGustEnv   { get; private set; }   // 0..1 envelope, pre-strength
    public static float   CanonStrength  { get; private set; }
    public static float   CanonTime      { get; private set; }
    public static float   CanonAmpMax    { get; private set; }
    public static float   CanonHeightRef { get; private set; } = 1.2f;
    public static float   CanonReachIn   { get; private set; } = 0.35f;
    public static float   CanonReachSpan { get; private set; } = 1.2f;

    static readonly int IdWindA     = Shader.PropertyToID("_TreeWindA");
    static readonly int IdWindB     = Shader.PropertyToID("_TreeWindB");
    static readonly int IdReachSpan = Shader.PropertyToID("_TreeWindReachSpan");

    void Awake()    => Instance = this;
    void OnDestroy() { if (Instance == this) Instance = null; }

    void OnDisable()
    {
        // Freeze cleanly at zero — shader globals outlive the component, and stale values
        // would leave the tree bent mid-gust forever.
        CanonStrength = 0f;
        Shader.SetGlobalVector(IdWindA, Vector4.zero);
    }

    void Update()
    {
        float t = Time.time;

        // Wandering prevailing direction — SAME constants as SampleSway's wander so the
        // canopy flutter and the branch sway agree about where the wind is coming from.
        float wanderDeg = (Mathf.PerlinNoise(t * 0.02f, 3.7f) - 0.5f) * 50f;
        Vector3 dir = baseDirection.sqrMagnitude > 0.001f ? baseDirection.normalized : Vector3.right;
        dir = Quaternion.AngleAxis(wanderDeg, Vector3.up) * dir;

        CanonDir       = dir;
        CanonGustEnv   = Mathf.PerlinNoise(t * gustFrequency, 0.5f);
        CanonStrength  = strength;
        CanonTime      = t;
        CanonAmpMax    = branchSwayMax;
        CanonHeightRef = swayHeightRef;
        CanonReachIn   = swayReachInner;
        CanonReachSpan = swayReachSpan;

        Shader.SetGlobalVector(IdWindA, new Vector4(dir.x, dir.z, CanonGustEnv, strength));
        Shader.SetGlobalVector(IdWindB, new Vector4(t, branchSwayMax, swayHeightRef, swayReachInner));
        Shader.SetGlobalFloat (IdReachSpan, swayReachSpan);
    }

    /// <summary>Current gust envelope 0..1 — shared so callers can scale secondary
    /// effects (falling-leaf drift, shed rates) with the same weather.</summary>
    public float GustLevel =>
        strength <= 0f ? 0f : Mathf.PerlinNoise(Time.time * gustFrequency, 0.5f) * strength;

    /// <summary>
    /// World-space sway offset for a foliage element. `phase01` (per-instance random,
    /// 0..1) staggers the motion so the canopy shimmers instead of moving as one block;
    /// nearby tips still correlate through the shared gust envelope.
    /// </summary>
    public Vector3 SampleSway(Vector3 worldPos, float phase01)
    {
        if (strength <= 0f) return Vector3.zero;

        float t = Time.time;

        // Gust envelope: slow Perlin swell shared across the tree, with a per-instance
        // spatial offset so gusts appear to travel through the canopy.
        float gust = Mathf.PerlinNoise(t * gustFrequency + worldPos.x * 0.05f,
                                       phase01 * 7.31f + worldPos.z * 0.05f);
        float amp  = strength * maxSwayAmplitude * (0.2f + 0.8f * gust);

        // Flutter: two incommensurate sines per axis, frequency detuned per instance.
        float sMain = Mathf.Sin(t * (1.7f + phase01 * 1.3f) + phase01 * 12.9f);
        float sSide = Mathf.Sin(t * (2.9f + phase01 * 0.8f) + phase01 * 5.7f);
        float sLift = Mathf.Sin(t * (2.2f + phase01)        + phase01 * 9.1f);

        // Slowly wandering prevailing direction (±25° over ~a minute).
        float wander = (Mathf.PerlinNoise(t * 0.02f, 3.7f) - 0.5f) * 50f * Mathf.Deg2Rad;
        Vector3 dir  = baseDirection.sqrMagnitude > 0.001f ? baseDirection.normalized : Vector3.right;
        dir = Quaternion.AngleAxis(wander * Mathf.Rad2Deg, Vector3.up) * dir;
        Vector3 side = Vector3.Cross(dir, Vector3.up);

        return (dir  * (0.55f + 0.45f * sMain)
              + side * sSide * 0.35f
              + Vector3.up * sLift * 0.20f) * amp;
    }
}
