using UnityEngine;

/// <summary>
/// Click-hold-drag mechanic for pulling weeds out of the bonsai pot.
///
/// Flow:
///   1. LMB down over a weed → start pull.
///   2. Hold and drag the mouse upward → accumulates normalised pull force.
///   3. Cross <see cref="Weed.forceRequired"/> → resolve pull:
///        - Roll against <see cref="Weed.ripChance"/>. On rip: top tears off, roots remain
///          (weed stays, becomes harder to pull, acts like trim — still drains nutrients).
///        - On clean pull: weed fully removed.
///   4. Release LMB before threshold → weed snaps back to rest position.
///
/// Weeds are parented to the tree GameObject so CameraOrbit's existing
/// "block drag on tree child" check prevents camera rotation while pulling.
///
/// Attach to the same GameObject as <see cref="TreeInteraction"/>.
/// TreeInteraction skips click handling while <see cref="IsPulling"/> is true.
/// </summary>
public class WeedPuller : MonoBehaviour
{
    public static WeedPuller Instance { get; private set; }

    [Tooltip("Maximum world-unit height the weed visually rises during a pull. " +
             "Purely cosmetic — does not affect the force calculation.")]
    [SerializeField] float animRiseMax = 0.35f;

    // ── State ─────────────────────────────────────────────────────────────────

    Weed  heldWeed;
    float pullProgress;    // normalised upward drag accumulated this attempt
    float lastMouseY;      // screen Y on the previous frame (for delta calculation)

    Camera cam;


    // ── Properties ────────────────────────────────────────────────────────────

    /// <summary>True while a weed is being held. TreeInteraction queries this to skip click handling.</summary>
    public bool IsPulling => heldWeed != null;

    // ── Unity ─────────────────────────────────────────────────────────────────

    void Awake()
    {
        Instance = this;
        cam      = Camera.main;
    }

    void Start()
    {
        // Camera.main can be null in Awake if the camera hasn't registered yet.
        if (cam == null) cam = Camera.main;
    }

    void Update()
    {
        // ── Start a new pull ───────────────────────────────────────────────────
        if (Input.GetMouseButtonDown(1) && heldWeed == null)
        {
            if (WeedManager.Instance == null || cam == null) return;

            // RaycastAll so the Bonsai/PlanterTable mesh colliders in front don't
            // block us from reaching the weed's BoxCollider behind them.
            var ray  = cam.ScreenPointToRay(Input.mousePosition);
            var hits = Physics.RaycastAll(ray);

            Weed  foundWeed  = null;
            float closestDist = float.MaxValue;
            foreach (var h in hits)
            {
                var w = h.collider.GetComponent<Weed>();
                if (w != null && h.distance < closestDist)
                {
                    foundWeed   = w;
                    closestDist = h.distance;
                }
            }

            if (foundWeed != null)
            {
                heldWeed     = foundWeed;
                pullProgress = 0f;
                lastMouseY   = Input.mousePosition.y;
                Debug.Log($"[WeedPuller] Grabbed {foundWeed.name} | " +
                          $"force={foundWeed.forceRequired:F3} rip={foundWeed.ripChance:F2} ripped={foundWeed.isRipped}");
            }
            return;
        }

        // ── Active pull ────────────────────────────────────────────────────────
        if (heldWeed == null) return;

        // Only upward movement counts — downward drag doesn't reduce progress,
        // but it also doesn't help; the player must re-grip by releasing and re-clicking.
        float dy = Input.mousePosition.y - lastMouseY;
        if (dy > 0f)
            pullProgress += dy / Screen.height;
        lastMouseY = Input.mousePosition.y;

        // Visually lift the weed proportional to pull progress
        float frac = Mathf.Clamp01(pullProgress / heldWeed.forceRequired);
        heldWeed.transform.position = heldWeed.restPosition + Vector3.up * frac * animRiseMax;

        // ── Threshold reached → resolve ────────────────────────────────────────
        if (pullProgress >= heldWeed.forceRequired)
        {
            bool rips = Random.value < heldWeed.ripChance;

            if (rips)
            {
                WeedManager.Instance.RipWeed(heldWeed);
            }
            else
            {
                WeedManager.Instance.RemoveWeed(heldWeed);
            }

            heldWeed = null;
            return;
        }

        // ── Released before threshold → snap back ──────────────────────────────
        if (Input.GetMouseButtonUp(1))
        {
            heldWeed.transform.position = heldWeed.restPosition;
            Debug.Log($"[WeedPuller] Released early — weed snapped back (progress={pullProgress:F3}/{heldWeed.forceRequired:F3}).");
            heldWeed = null;
        }
    }
}
