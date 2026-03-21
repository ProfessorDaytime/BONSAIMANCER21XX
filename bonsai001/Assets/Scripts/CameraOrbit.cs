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

    // Current spherical coordinates relative to target
    float yaw;
    float pitch;
    float radius;

    // Vertical offset applied on top of target.position so panning doesn't
    // move the actual tree transform.
    float panY;

    bool isDragging;
    bool isPanning;

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

        Debug.Log($"[CameraOrbit] Initialised — target={target.name} radius={radius:F2} yaw={yaw:F1} pitch={pitch:F1}");
    }

    void Update()
    {
        if (target == null) return;

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll != 0f)
        {
            radius = Mathf.Clamp(radius - scroll * zoomSpeed * radius, zoomMin, zoomMax);
            ApplyOrbit();
        }

        if (Input.GetMouseButtonDown(0))
        {
            // Don't start a drag if the cursor is over an interactable UI element
            // (buttons, toggles, etc.).  We use RaycastAll rather than
            // IsPointerOverGameObject so that invisible background panels whose
            // "Raycast Target" flag is on don't block the camera.
            if (IsPointerOverInteractableUI())
            {
                Debug.Log("[CameraOrbit] Click blocked — over interactive UI");
                return;
            }

            // Don't start a drag if the cursor is over any physics collider
            // (tree mesh, or any future interactable with a collider)
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                Debug.Log($"[CameraOrbit] Click blocked — hit collider '{hit.collider.name}' on '{hit.collider.gameObject.name}'");
                return;
            }

            Debug.Log("[CameraOrbit] Drag started");
            isDragging = true;
        }

        if (Input.GetMouseButtonDown(2)) isPanning  = true;
        if (Input.GetMouseButtonUp(2))   isPanning  = false;
        if (Input.GetMouseButtonUp(0))   isDragging = false;

        if (isPanning)
        {
            float panDelta = Input.GetAxis("Mouse Y");
            panY -= panDelta * panSpeed * radius;
            ApplyOrbit();
        }

        if (!isDragging) return;

        float dx = Input.GetAxis("Mouse X");
        float dy = Input.GetAxis("Mouse Y");

        yaw   += dx * sensitivity * 100f * Time.deltaTime;
        pitch -= dy * sensitivity * 100f * Time.deltaTime;   // subtract so dragging up tilts up
        pitch  = Mathf.Clamp(pitch, pitchMin, pitchMax);

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

        var pointer = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
        uiHits.Clear();
        EventSystem.current.RaycastAll(pointer, uiHits);

        foreach (var hit in uiHits)
            if (hit.gameObject.GetComponentInParent<Selectable>() != null)
                return true;

        return false;
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
