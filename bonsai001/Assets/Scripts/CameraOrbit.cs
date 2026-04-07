using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Orbits the camera around a target point when the player clicks and drags
/// on empty space (not over UI and not over any physics collider).
///
/// Attach to the Main Camera.  Drag the bonsai tree's transform (or any
/// suitable pivot) into the 'target' field in the Inspector.
/// </summary>
public class CameraOrbit : MonoBehaviour
{
    [Tooltip("Point to orbit around. Drag the tree's root transform here.")]
    [SerializeField] Transform target;

    [Tooltip("Degrees of rotation per pixel of mouse movement.")]
    [SerializeField] float sensitivity = 0.3f;

    [Tooltip("Minimum pitch angle (degrees, measured from horizontal).")]
    [SerializeField] float pitchMin = 5f;

    [Tooltip("Maximum pitch angle (degrees, measured from horizontal).")]
    [SerializeField] float pitchMax = 85f;

    [Tooltip("Scroll wheel zoom speed.")]
    [SerializeField] float zoomSpeed = 2f;

    [Tooltip("Closest the camera can get to the target.")]
    [SerializeField] float zoomMin = 2f;

    [Tooltip("Furthest the camera can get from the target.")]
    [SerializeField] float zoomMax = 50f;

    [Tooltip("Speed of vertical pan with middle-mouse drag.")]
    [SerializeField] float panSpeed = 0.05f;

    [Tooltip("Pitch floor while in Root Prune mode — allows looking up at the roots from below.")]
    [SerializeField] float pitchMinRootPrune = -30f;

    [Header("Start Position Overrides")]
    [Tooltip("Starting zoom distance. 0 = derive from camera scene position.")]
    [SerializeField] float startRadius = 0f;
    [Tooltip("Starting vertical pan offset. 0 = no pan.")]
    [SerializeField] float startPanY = 0f;

    // Current spherical coordinates relative to target
    float yaw;
    float pitch;
    float radius;

    // Vertical offset applied on top of target.position so panning doesn't
    // move the actual tree transform.
    float panY;

    bool isDragging;
    bool isPanning;

    float activePitchMin;

    Vector3 lastTargetPosition;

    // Reused buffer for UI raycasts — avoids per-frame allocation
    static readonly List<RaycastResult> uiHits = new List<RaycastResult>();

    void Start()
    {
        if (target == null)
        {
            Debug.LogWarning("[CameraOrbit] No target assigned — orbit disabled.");
            return;
        }

        // Initialise yaw/pitch/radius from the camera's current position
        // so the scene doesn't jump on first drag.
        Vector3 offset = transform.position - target.position;
        radius = offset.magnitude;
        pitch  = Mathf.Asin(offset.normalized.y) * Mathf.Rad2Deg;
        yaw    = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;

        if (startRadius > 0f) radius = startRadius;
        if (startPanY   != 0f) panY   = startPanY;
        ApplyOrbit();

        lastTargetPosition = target.position;
        activePitchMin = pitchMin;
        Debug.Log($"[CameraOrbit] Initialised — target={target.name} radius={radius:F2} yaw={yaw:F1} pitch={pitch:F1} panY={panY:F2}");
    }

    void OnEnable()  => GameManager.OnGameStateChanged += OnGameStateChanged;
    void OnDisable() => GameManager.OnGameStateChanged -= OnGameStateChanged;

    void OnGameStateChanged(GameState state)
    {
        // Cancel any in-progress drag so clicking a UI button to change state
        // doesn't leave isDragging=true and cause a jump on the next mouse move.
        isDragging = false;
        isPanning  = false;

        activePitchMin = (state == GameState.RootPrune) ? pitchMinRootPrune : pitchMin;
        pitch = Mathf.Clamp(pitch, activePitchMin, pitchMax);

        ApplyOrbit();
    }

    void Update()
    {
        if (target == null) return;

        // If the tree transform moved (lift/lower during RootPrune or TreeOrient),
        // compensate panY so the camera stays visually stationary rather than jumping.
        Vector3 targetDelta = target.position - lastTargetPosition;
        if (targetDelta.sqrMagnitude > 0.000001f)
        {
            panY -= targetDelta.y;
            lastTargetPosition = target.position;
            ApplyOrbit();
        }

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        // Block zoom when rock is grabbed (scroll = Y lift) or when right-click is
        // held in a rock mode (scroll = roll). Free zoom all other times.
        bool inRockMode = GameManager.Instance != null &&
                          (GameManager.Instance.state == GameState.RockPlace ||
                           GameManager.Instance.state == GameState.TreeOrient);
        bool blockScroll = RockPlacer.RockGrabbed ||
                           RockPlacer.TreeGrabbed ||
                           (inRockMode && Input.GetMouseButton(1));
        if (scroll != 0f && !blockScroll)
        {
            radius = Mathf.Clamp(radius - scroll * zoomSpeed * radius, zoomMin, zoomMax);
            ApplyOrbit();
            Debug.Log($"[CameraOrbit] zoom → startRadius={radius:F2} startPanY={panY:F2}");
        }

        if (Input.GetMouseButtonDown(0))
        {
            bool blocked = false;

            // Don't start a drag during wire, rock-placement, or when tree is grabbed.
            if (GameManager.Instance != null &&
                (GameManager.Instance.state == GameState.Wiring     ||
                 GameManager.Instance.state == GameState.WireAnimate ||
                 GameManager.Instance.state == GameState.RockPlace  ||
                 RockPlacer.TreeGrabbed))
            {
                blocked = true;
            }
            // Don't start a drag if the cursor is over an interactable UI element
            // (buttons, toggles, etc.).  We use RaycastAll rather than
            // IsPointerOverGameObject so that invisible background panels whose
            // "Raycast Target" flag is on don't block the camera.
            else if (IsPointerOverInteractableUI())
            {
                Debug.Log("[CameraOrbit] Click blocked — over interactive UI");
                blocked = true;
            }
            // Don't start a drag if the cursor is over an interactable physics collider
            // (the bonsai tree or any child of it).  Decorative objects like the planter
            // table have colliders but should NOT block camera rotation.
            else
            {
                Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray, out RaycastHit hit))
                {
                    bool isTree = target != null &&
                                  (hit.transform == target || hit.transform.IsChildOf(target));
                    if (isTree)
                    {
                        Debug.Log($"[Camera] Click blocked — hit tree '{hit.collider.name}'");
                        blocked = true;
                    }
                    // else: decorative collider, allow drag
                }
            }

            if (!blocked)
            {
                isDragging = true;
                Debug.Log($"[Camera] isDragging=true | gameState={GameManager.Instance?.state}");
            }
        }

        if (Input.GetMouseButtonDown(2)) isPanning  = true;
        if (Input.GetMouseButtonUp(2))   isPanning  = false;
        if (Input.GetMouseButtonUp(0) && isDragging) { isDragging = false; Debug.Log($"[Camera] isDragging=false | gameState={GameManager.Instance?.state}"); }

        // Safety: LMB not held but isDragging somehow stuck — force-clear it.
        if (isDragging && !Input.GetMouseButton(0))
        {
            isDragging = false;
            Debug.LogWarning($"[Camera] isDragging safety-cleared (LMB not held) | gameState={GameManager.Instance?.state}");
        }

        if (isPanning)
        {
            float panDelta = Input.GetAxis("Mouse Y");
            panY -= panDelta * panSpeed * radius;
            ApplyOrbit();
            // Debug.Log($"[CameraOrbit] pan → startRadius={radius:F2} startPanY={panY:F2}");
        }

        if (!isDragging) return;

        float dx = Input.GetAxis("Mouse X");
        float dy = Input.GetAxis("Mouse Y");

        yaw   += dx * sensitivity * 100f * Time.deltaTime;
        pitch -= dy * sensitivity * 100f * Time.deltaTime;   // subtract so dragging up tilts up
        pitch  = Mathf.Clamp(pitch, activePitchMin, pitchMax);

        ApplyOrbit();
    }

    /// <summary>
    /// Returns true only if the pointer is over a UI Selectable (Button, Toggle,
    /// Slider, etc.).  Ignores non-interactive Image/Panel backgrounds even if
    /// their Raycast Target flag is enabled.
    /// </summary>
    bool IsPointerOverInteractableUI()
    {
        if (EventSystem.current == null) return false;

        // UI Toolkit's panel covers the full screen, so IsPointerOverGameObject() always
        // returns true and cannot be used here. Use RaycastAll + Selectable check instead,
        // which only hits actual interactive UGUI elements (buttons, toggles, sliders).
        var pointer = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
        uiHits.Clear();
        EventSystem.current.RaycastAll(pointer, uiHits);

        foreach (var hit in uiHits)
            if (hit.gameObject.GetComponentInParent<Selectable>() != null)
                return true;

        return false;
    }

    /// <summary>
    /// Re-derives yaw, pitch, radius, and panY from the camera's current world
    /// position so the view doesn't jump after the target transform moves.
    /// Call this immediately after any code that repositions the camera target.
    /// </summary>
    public void ReSyncFromCurrentPosition()
    {
        if (target == null) return;
        // Compute the offset from target to camera in world space.
        Vector3 camPos  = transform.position;
        Vector3 tgtPos  = target.position;
        Vector3 offset  = camPos - tgtPos;
        radius = offset.magnitude;
        if (radius < 0.001f) return;
        Vector3 dir = offset / radius;
        pitch  = Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;
        yaw    = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        // panY: the pivot is target.position + panY*up. Since we want the camera
        // to stay exactly where it is, set panY=0 and absorb the vertical offset
        // into pitch/radius (which we've already computed from the real offset).
        panY = 0f;
        ApplyOrbit();
    }

    void ApplyOrbit()
    {
        // Convert spherical (yaw, pitch, radius) to a world-space camera position
        float pitchRad = pitch * Mathf.Deg2Rad;
        float yawRad   = yaw   * Mathf.Deg2Rad;

        Vector3 offset = new Vector3(
            Mathf.Sin(yawRad) * Mathf.Cos(pitchRad),
            Mathf.Sin(pitchRad),
            Mathf.Cos(yawRad) * Mathf.Cos(pitchRad)
        ) * radius;

        Vector3 pivot = target.position + Vector3.up * panY;
        transform.position = pivot + offset;
        transform.LookAt(pivot);
    }
}
