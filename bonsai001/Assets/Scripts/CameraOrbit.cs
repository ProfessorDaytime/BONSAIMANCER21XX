using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.InputSystem;

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

    // Idle orbit state
    float savedOrbitYaw;
    float savedOrbitPitch;
    float savedOrbitRadius;
    float savedOrbitPanY;
    bool  orbitStateSaved;
    float orbitElevPhase;   // sine phase for gentle elevation drift
    const float OrbitYawSpeed   = 4f;    // degrees per real second
    const float OrbitElevAmpl   = 5f;    // ± degrees of pitch drift
    const float OrbitElevPeriod = 20f;   // seconds for one full elevation cycle

    // Cinematic mode — toggled with C, ignores all input, smooth constant orbit
    [Header("Cinematic Mode")]
    [Tooltip("Yaw speed in degrees per real second during cinematic orbit. Default 0.8°/s — comfortable up to ~8× speed-up.")]
    [SerializeField] float cinematicYawSpeed = 0.8f;
    [Tooltip("Peak elevation drift in degrees (±) during cinematic orbit.")]
    [SerializeField] float cinematicElevAmpl = 3f;
    [Tooltip("Seconds for one full elevation cycle during cinematic orbit.")]
    [SerializeField] float cinematicElevPeriod = 30f;

    [Tooltip("Tree skeleton used to read tree height for cinematic auto-zoom. Optional — drag the tree GameObject here.")]
    [SerializeField] TreeSkeleton skeleton;
    [Tooltip("Camera radius = tree height × this multiplier. At pitch≈20° and 60° VFOV, 1.2 puts the canopy top at ~90% and the base at ~20% from screen bottom.")]
    [SerializeField] float cinematicZoomHeightMult = 1.2f;
    [Tooltip("Minimum radius the auto-zoom will target (never zooms closer than this).")]
    [SerializeField] float cinematicZoomMin = 3f;
    [Tooltip("How fast the radius eases toward the target height-based zoom. Lower = slower ease.")]
    [SerializeField] float cinematicZoomLerpSpeed = 1.5f;
    [Tooltip("Fraction of tree height used as the look-at pivot in cinematic mode. " +
             "0.5 = look halfway up — puts the base at ~20% from screen bottom at pitch≈20°.")]
    [SerializeField] [Range(0f, 1f)] float cinematicFramingFraction = 0.5f;
    [Tooltip("How fast the look-at pivot eases to the framing height. Lower = slower.")]
    [SerializeField] float cinematicPanLerpSpeed = 1.2f;
    [Tooltip("Minimum pitch enforced while cinematic mode is active. Prevents the camera from " +
             "going nearly horizontal (5°) where the top of a growing tree quickly escapes the frame. " +
             "20° gives good vertical headroom without looking top-down.")]
    [SerializeField] float cinematicMinPitch = 20f;
    [Tooltip("Log cinematic zoom state (height, radius, panY) to the console every ~2 real seconds.")]
    [SerializeField] bool debugCinematicZoom = false;

    bool  cinematicActive;
    float cinematicElevPhase;
    float cinematicBasePitch;  // pitch captured when cinematic mode was entered
    float cinematicSavedPanY;  // panY to restore when cinematic mode exits
    float cinematicDebugTimer;

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

        // Auto-find skeleton for cinematic auto-zoom if not assigned in Inspector.
        if (skeleton == null)
        {
            skeleton = FindAnyObjectByType<TreeSkeleton>();
            if (skeleton != null)
                Debug.Log("[CameraOrbit] Auto-found TreeSkeleton for cinematic zoom.");
            else
                Debug.LogWarning("[CameraOrbit] No TreeSkeleton found — cinematic auto-zoom disabled. Drag it into the Inspector.");
        }

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
        // Only kill cinematic mode for states where it would actively interfere with
        // interactive editing. Normal gameplay transitions (Water, BranchGrow, LeafFall,
        // etc.) should not interrupt a cinematic recording session.
        if (state == GameState.RootPrune  ||
            state == GameState.RootRake   ||
            state == GameState.RockPlace  ||
            state == GameState.TreeOrient ||
            state == GameState.WireAnimate ||
            state == GameState.SpeciesSelect ||
            state == GameState.LoadMenu)
            cinematicActive = false;

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

        float scroll = Mouse.current != null ? Mouse.current.scroll.ReadValue().y / 120f : 0f;
        // Block zoom when rock is grabbed (scroll = Y lift) or when right-click is
        // held in a rock mode (scroll = roll). Free zoom all other times.
        bool inRockMode = GameManager.Instance != null &&
                          (GameManager.Instance.state == GameState.RockPlace ||
                           GameManager.Instance.state == GameState.TreeOrient);
        bool blockScroll = cinematicActive ||
                           RockPlacer.RockGrabbed ||
                           RockPlacer.TreeGrabbed ||
                           (inRockMode && Mouse.current != null && Mouse.current.rightButton.isPressed);
        if (scroll != 0f && !blockScroll)
        {
            radius = Mathf.Clamp(radius - scroll * zoomSpeed * radius, zoomMin, zoomMax);
            ApplyOrbit();
            Debug.Log($"[CameraOrbit] zoom → startRadius={radius:F2} startPanY={panY:F2}");
        }

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
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
                Ray ray = Camera.main.ScreenPointToRay((Vector3)Mouse.current.position.ReadValue());
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

        if (Mouse.current != null && Mouse.current.middleButton.wasPressedThisFrame)  isPanning = true;
        if (Mouse.current != null && Mouse.current.middleButton.wasReleasedThisFrame) isPanning = false;
        if (Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame && isDragging) { isDragging = false; Debug.Log($"[Camera] isDragging=false | gameState={GameManager.Instance?.state}"); }

        // Safety: LMB not held but isDragging somehow stuck — force-clear it.
        if (isDragging && (Mouse.current == null || !Mouse.current.leftButton.isPressed))
        {
            isDragging = false;
            Debug.LogWarning($"[Camera] isDragging safety-cleared (LMB not held) | gameState={GameManager.Instance?.state}");
        }

        if (isPanning)
        {
            float panDelta = Mouse.current != null ? Mouse.current.delta.ReadValue().y * 0.01f : 0f;
            panY -= panDelta * panSpeed * radius;
            ApplyOrbit();
            // Debug.Log($"[CameraOrbit] pan → startRadius={radius:F2} startPanY={panY:F2}");
        }

        // Cinematic mode — C key toggles; runs independently of all other input
        if (Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame)
            SetCinematicMode(!cinematicActive);

        if (cinematicActive)
        {
            float effectiveYawSpeed = GameManager.canTrim ? cinematicYawSpeed * 0.5f : cinematicYawSpeed;
            yaw += effectiveYawSpeed * Time.unscaledDeltaTime;
            cinematicElevPhase += Time.unscaledDeltaTime / cinematicElevPeriod * Mathf.PI * 2f;
            pitch = cinematicBasePitch + Mathf.Sin(cinematicElevPhase) * cinematicElevAmpl;
            // Enforce cinematicMinPitch — never let elevation drift below the floor
            pitch = Mathf.Clamp(pitch, Mathf.Max(activePitchMin, cinematicMinPitch), pitchMax);

            // Auto-zoom + framing: ease radius and look-at pivot to fit the full canopy.
            if (skeleton != null)
            {
                float treeH = skeleton.CachedTreeHeight;

                // Always track the pivot so the base stays at ~20% from the bottom.
                float targetPanY = treeH * cinematicFramingFraction;
                panY = Mathf.Lerp(panY, targetPanY, cinematicPanLerpSpeed * Time.unscaledDeltaTime);

                // Only pull the camera back when the canopy enters the top 10% of the frame.
                float targetRadius = Mathf.Max(cinematicZoomMin, treeH * cinematicZoomHeightMult);
                targetRadius = Mathf.Clamp(targetRadius, zoomMin, zoomMax);
                Vector3 treeTop = skeleton.transform.position + Vector3.up * treeH;
                Vector3 vp = Camera.main.WorldToViewportPoint(treeTop);
                if (vp.z > 0f && vp.y > 0.9f)
                    radius = Mathf.Lerp(radius, targetRadius, cinematicZoomLerpSpeed * Time.unscaledDeltaTime);

                if (debugCinematicZoom)
                {
                    cinematicDebugTimer += Time.unscaledDeltaTime;
                    if (cinematicDebugTimer >= 2f)
                    {
                        cinematicDebugTimer = 0f;
                        Debug.Log($"[CinematicZoom] treeH={treeH:F2} targetR={targetRadius:F2} radius={radius:F2} targetPanY={targetPanY:F2} panY={panY:F2} pitch={pitch:F1}");
                    }
                }
            }

            ApplyOrbit();
            return;
        }

        // Idle orbit — driven by PlayModeManager
        bool orbitActive = PlayModeManager.Instance != null && PlayModeManager.Instance.IdleOrbitActive;
        if (orbitActive)
        {
            if (!orbitStateSaved)
            {
                savedOrbitYaw    = yaw;
                savedOrbitPitch  = pitch;
                savedOrbitRadius = radius;
                savedOrbitPanY   = panY;
                orbitStateSaved  = true;
                orbitElevPhase   = 0f;
            }
            yaw += OrbitYawSpeed * Time.unscaledDeltaTime;
            orbitElevPhase += Time.unscaledDeltaTime / OrbitElevPeriod * Mathf.PI * 2f;
            pitch = savedOrbitPitch + Mathf.Sin(orbitElevPhase) * OrbitElevAmpl;
            pitch = Mathf.Clamp(pitch, activePitchMin, pitchMax);
            ApplyOrbit();
            return;
        }
        else if (orbitStateSaved)
        {
            // Snap back to saved position when orbit stops
            yaw    = savedOrbitYaw;
            pitch  = savedOrbitPitch;
            radius = savedOrbitRadius;
            panY   = savedOrbitPanY;
            orbitStateSaved = false;
            ApplyOrbit();
        }

        if (!isDragging) return;

        Vector2 mouseDelta = Mouse.current != null ? Mouse.current.delta.ReadValue() * 0.01f : Vector2.zero;
        float dx = mouseDelta.x;
        float dy = mouseDelta.y;

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
        var pointer = new PointerEventData(EventSystem.current) { position = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero };
        uiHits.Clear();
        EventSystem.current.RaycastAll(pointer, uiHits);

        foreach (var hit in uiHits)
            if (hit.gameObject.GetComponentInParent<Selectable>() != null)
                return true;

        return false;
    }

    /// <summary>
    /// Enable or disable cinematic mode programmatically (same effect as pressing C).
    /// Called by AutoRunManager to start recording without keyboard input.
    /// </summary>
    public void SetCinematicMode(bool active)
    {
        if (cinematicActive == active) return;
        cinematicActive = active;

        if (active)
        {
            cinematicBasePitch = Mathf.Max(cinematicMinPitch, pitch);
            cinematicElevPhase = 0f;
            cinematicSavedPanY = panY;
            cinematicDebugTimer = 0f;
            if (skeleton != null)
            {
                float treeH = skeleton.CachedTreeHeight;
                float snapR = Mathf.Clamp(
                    Mathf.Max(cinematicZoomMin, treeH * cinematicZoomHeightMult),
                    zoomMin, zoomMax);
                radius = Mathf.Max(radius, snapR);
                panY   = treeH * cinematicFramingFraction;
            }
            ApplyOrbit();
            Debug.Log($"[CameraOrbit] Cinematic ON — yaw={yaw:F1} pitch={pitch:F1} → basePitch={cinematicBasePitch:F1} radius={radius:F2} panY={panY:F2}");
        }
        else
        {
            panY = cinematicSavedPanY;
            ApplyOrbit();
            Debug.Log($"[CameraOrbit] Cinematic OFF — yaw={yaw:F1} pitch={pitch:F1}");
        }
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
