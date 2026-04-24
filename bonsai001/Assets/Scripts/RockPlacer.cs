using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Handles Ishitsuki rock placement and tree orientation.
///
/// Attach to the rock GameObject.
///
/// Rock Placement (GameState.RockPlace):
///   Left-click rock  → grab it.
///   Mouse move       → rock tracks cursor on horizontal plane at rock's Y.
///   Scroll wheel     → raise / lower rock (burial depth).
///   Right drag       → rotate rock on yaw + pitch.
///   Right drag+scroll→ rotate rock on roll axis.
///   Left-click again → confirm placement → enters TreeOrient.
///
/// Tree Orientation (GameState.TreeOrient):
///   Right drag       → rotate the tree transform on 2 axes.
///   Right drag+scroll→ rotate on roll axis.
///   Left drag        → camera orbit (handled by CameraOrbit, unchanged).
///   Confirm button   → lock in orientation, fire OnRockOrientConfirmed.
/// </summary>
public class RockPlacer : MonoBehaviour
{
    [Tooltip("The tree's root transform — rotated during TreeOrient.")]
    [SerializeField] Transform treeTransform;

    [Tooltip("Degrees per pixel for right-drag rotation.")]
    [SerializeField] float rotateSensitivity = 0.4f;

    [Tooltip("Degrees per scroll tick for roll rotation.")]
    [SerializeField] float rollSensitivity = 15f;

    [Tooltip("World units per scroll tick for rock Y movement.")]
    [SerializeField] float liftSensitivity = 0.15f;

    // ── Rock size ─────────────────────────────────────────────────────────────

    public enum RockSize { S, M, L, XL }

    static readonly Vector3[] RockScales =
    {
        new Vector3(0.6f,  0.5f,  0.6f),   // S
        new Vector3(1.0f,  0.85f, 1.0f),   // M  ← default
        new Vector3(1.5f,  1.3f,  1.5f),   // L
        new Vector3(2.2f,  1.9f,  2.2f),   // XL
    };

    public RockSize rockSize = RockSize.M;

    public void ApplyRockSize()
    {
        transform.localScale = RockScales[(int)rockSize];
    }

    // ── State ─────────────────────────────────────────────────────────────────
    public static bool RockGrabbed { get; private set; }
    bool rockGrabbed { get => RockGrabbed; set => RockGrabbed = value; }

    public static bool TreeGrabbed { get; private set; }
    bool treeGrabbed { get => TreeGrabbed; set => TreeGrabbed = value; }

    Camera cam;
    Collider rockCollider;

    // Plane used to project mouse cursor to world position while rock is grabbed.
    // Updated each frame to match the rock's current Y.
    Plane movePlane;

    // Right-drag tracking
    bool  rightDragging;
    Vector2 lastMousePos;

    void Awake()
    {
        cam          = Camera.main;
        rockCollider = GetComponent<Collider>();
    }

    void OnEnable()
    {
        Debug.Log("[RockPlacer] OnEnable — subscribing events");
        GameManager.OnGameStateChanged  += OnGameStateChanged;
        GameManager.OnRockOrientConfirmed += OnOrientConfirmed;
    }

    void OnDisable()
    {
        GameManager.OnGameStateChanged  -= OnGameStateChanged;
        GameManager.OnRockOrientConfirmed -= OnOrientConfirmed;
    }

    void OnGameStateChanged(GameState state)
    {
        if (state != GameState.RockPlace && state != GameState.TreeOrient)
        {
            // Exiting rock modes — release anything held.
            rockGrabbed   = false;
            treeGrabbed   = false;
            rightDragging = false;
        }
    }

    void OnOrientConfirmed()
    {
        rightDragging = false;

        Debug.Log($"[RockPlacer] OnOrientConfirmed fired | rockCollider={rockCollider} treeTransform={treeTransform}");
        if (rockCollider == null || treeTransform == null) return;

        var skeleton   = treeTransform.GetComponent<TreeSkeleton>();
        float trunkRad = (skeleton != null && skeleton.root != null) ? skeleton.root.radius : 0.1f;

        if (skeleton != null)
        {
            skeleton.rockCollider = rockCollider;
            skeleton.SpawnTrainingWires();   // drape roots + set mesh gripping; must run AFTER rockCollider is set
        }

        var wireGO = new GameObject("IshitsukiBindingWires");
        wireGO.transform.SetParent(treeTransform, false);
        var wire = wireGO.AddComponent<IshitsukiWire>();
        wire.Init(rockCollider, treeTransform, trunkRad);

        Debug.Log("[Ishitsuki] Binding wire loops spawned");
    }

    void Update()
    {
        var state = GameManager.Instance.state;

        if (state == GameState.RockPlace)
            HandleRockPlace();
        else if (state == GameState.TreeOrient)
            HandleTreeOrient();
    }

    // ── Rock Placement ────────────────────────────────────────────────────────

    void HandleRockPlace()
    {
        // Right-drag to rotate the rock.
        HandleRightDragRotate(transform);

        if (rockGrabbed)
        {
            // Move rock on horizontal plane at its current Y — but not while rotating.
            if (!Mouse.current.rightButton.isPressed)
            {
                movePlane = new Plane(Vector3.up, transform.position);
                Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
                if (movePlane.Raycast(ray, out float dist))
                    transform.position = ray.GetPoint(dist);
            }

            // Scroll (no right-click) → raise / lower.
            if (!Mouse.current.rightButton.isPressed)
            {
                float scroll = Mouse.current.scroll.ReadValue().y / 120f;
                if (scroll != 0f)
                {
                    var p = transform.position;
                    transform.position = new Vector3(p.x, p.y + scroll * liftSensitivity * 10f, p.z);
                }
            }

            // Left-click to drop the rock in place (stays in RockPlace).
            // Confirm button transitions to TreeOrient.
            if (Mouse.current.leftButton.wasPressedThisFrame)
                rockGrabbed = false;
        }
        else
        {
            // Left-click on the rock to grab it.
            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
                if (rockCollider != null && rockCollider.Raycast(ray, out RaycastHit _, 200f))
                    rockGrabbed = true;
            }
        }
    }

    // ── Tree Orientation ──────────────────────────────────────────────────────

    void HandleTreeOrient()
    {
        if (treeTransform == null) return;

        HandleRightDragRotate(treeTransform);

        // Left-click on the tree → grab it.
        // While grabbed (no right-click held):
        //   Mouse X/Y → move tree on world X/Y axes
        //   Scroll    → move tree on world Z axis
        if (treeGrabbed)
        {
            if (!Mouse.current.rightButton.isPressed)
            {
                Vector2 md = Mouse.current.delta.ReadValue() * 0.01f;
                float mx = md.x;
                float my = md.y;
                float mz = Mouse.current.scroll.ReadValue().y / 120f;

                // Camera-relative axes projected onto horizontal plane so
                // dragging always moves the tree away from / toward the camera.
                Vector3 camRight = Vector3.ProjectOnPlane(cam.transform.right,   Vector3.up).normalized;
                Vector3 camFwd   = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;

                treeTransform.position = treeTransform.position
                    + camRight   * (mx * rotateSensitivity * 2f)
                    + Vector3.up * (my * rotateSensitivity * 2f)
                    + camFwd     * (mz * liftSensitivity   * 10f);
            }

            if (Mouse.current.leftButton.wasPressedThisFrame)
                treeGrabbed = false;
        }
        else
        {
            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
                if (Physics.Raycast(ray, out RaycastHit hit) &&
                    (hit.transform == treeTransform || hit.transform.IsChildOf(treeTransform)))
                    treeGrabbed = true;
            }
        }
    }

    // ── Shared right-drag rotation ────────────────────────────────────────────

    void HandleRightDragRotate(Transform target)
    {
        bool rightDown = Mouse.current.rightButton.wasPressedThisFrame;
        bool rightHeld = Mouse.current.rightButton.isPressed;
        bool rightUp   = Mouse.current.rightButton.wasReleasedThisFrame;

        if (rightDown)
        {
            rightDragging = true;
            lastMousePos  = Mouse.current.position.ReadValue();
        }
        if (rightUp)
            rightDragging = false;

        if (rightDragging && rightHeld)
        {
            Vector2 mouse  = Mouse.current.position.ReadValue();
            Vector2 delta  = mouse - lastMousePos;
            lastMousePos   = mouse;

            float scroll = Mouse.current.scroll.ReadValue().y / 120f;

            if (scroll != 0f)
            {
                // Roll around camera forward axis.
                target.Rotate(cam.transform.forward, -scroll * rollSensitivity * 100f * Time.deltaTime, Space.World);
            }
            else
            {
                // Yaw around world up, pitch around camera right.
                target.Rotate(Vector3.up, delta.x * rotateSensitivity, Space.World);
                target.Rotate(cam.transform.right,   -delta.y * rotateSensitivity, Space.World);
            }
        }
    }
}
