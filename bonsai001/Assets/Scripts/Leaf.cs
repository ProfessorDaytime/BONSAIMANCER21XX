using UnityEngine;

/// <summary>
/// Sits on each spawned leaf instance. Handles the fall animation when triggered
/// by LeafManager. Color is driven by LeafManager via a shared material — this
/// component doesn't touch the material directly.
///
/// Scale-in animation (spring bud-burst) is driven by LeafManager setting
/// targetScale before calling Init(); this component lerps toward it.
/// </summary>
public class Leaf : MonoBehaviour
{
    // ── Set by LeafManager on spawn ────────────────────────────────────────────

    [HideInInspector] public TreeNode ownerNode;
    [HideInInspector] public Vector3  targetScale = Vector3.one;

    // ── Scale-in ──────────────────────────────────────────────────────────────

    const float SCALE_DURATION = 1.5f;   // real seconds to reach full size
    float scaleTimer = 0f;

    // ── Fall ──────────────────────────────────────────────────────────────────

    bool    isFalling    = false;
    float   fallTimer    = 0f;
    Vector3 driftVelocity;
    float   rotSpeed;

    // ── Unity ─────────────────────────────────────────────────────────────────

    void Update()
    {
        // ── Spring scale-in ───────────────────────────────────────────────────
        if (scaleTimer < SCALE_DURATION)
        {
            scaleTimer += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, scaleTimer / SCALE_DURATION);
            transform.localScale = targetScale * t;
        }

        // ── Fall animation ────────────────────────────────────────────────────
        if (!isFalling) return;

        fallTimer += Time.deltaTime;

        transform.position  += driftVelocity * Time.deltaTime;
        transform.Rotate(Vector3.forward, rotSpeed * Time.deltaTime, Space.Self);

        // Gentle gravity
        driftVelocity.y -= 1.2f * Time.deltaTime;

        if (fallTimer > 5f || transform.position.y < -10f)
            Destroy(gameObject);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Detaches from the tree and starts the fall animation.
    /// Called by LeafManager when this leaf's fall roll succeeds.
    /// </summary>
    public void StartFalling()
    {
        isFalling = true;

        // Unparent so it drifts independently in world space
        transform.SetParent(null, worldPositionStays: true);

        driftVelocity = new Vector3(
            Random.Range(-0.3f, 0.3f),
            Random.Range(-0.2f, 0f),
            Random.Range(-0.3f, 0.3f)
        );
        rotSpeed = Random.Range(-120f, 120f);
    }
}
