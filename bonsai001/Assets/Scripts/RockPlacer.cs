using UnityEngine;

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
            if (!Input.GetMouseButton(1))
            {
                movePlane = new Plane(Vector3.up, transform.position);
                Ray ray = cam.ScreenPointToRay(Input.mousePosition);
                if (movePlane.Raycast(ray, out float dist))
                    transform.position = ray.GetPoint(dist);
            }

            // Scroll (no right-click) → raise / lower.
            if (!Input.GetMouseButton(1))
            {
                float scroll = Input.GetAxis("Mouse ScrollWheel");
                if (scroll != 0f)
                {
                    var p = transform.position;
                    transform.position = new Vector3(p.x, p.y + scroll * liftSensitivity * 10f, p.z);
                }
            }

            // Left-click to drop the rock in place (stays in RockPlace).
            // Confirm button transitions to TreeOrient.
            if (Input.GetMouseButtonDown(0))
                rockGrabbed = false;
        }
        else
        {
            // Left-click on the rock to grab it.
            if (Input.GetMouseButtonDown(0))
            {
                Ray ray = cam.ScreenPointToRay(Input.mousePosition);
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
            if (!Input.GetMouseButton(1))
            {
                float mx = Input.GetAxis("Mouse X");
                float my = Input.GetAxis("Mouse Y");
                float mz = Input.GetAxis("Mouse ScrollWheel");

                var p = treeTransform.position;
                treeTransform.position = new Vector3(
                    p.x - mx * rotateSensitivity * 2f,
                    p.y + my * rotateSensitivity * 2f,
                    p.z + mz * liftSensitivity * 10f);
            }

            if (Input.GetMouseButtonDown(0))
                treeGrabbed = false;
        }
        else
        {
            if (Input.GetMouseButtonDown(0))
            {
                Ray ray = cam.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray, out RaycastHit hit) &&
                    (hit.transform == treeTransform || hit.transform.IsChildOf(treeTransform)))
                    treeGrabbed = true;
            }
        }
    }

    // ── Shared right-drag rotation ────────────────────────────────────────────

    void HandleRightDragRotate(Transform target)
    {
        bool rightDown = Input.GetMouseButtonDown(1);
        bool rightHeld = Input.GetMouseButton(1);
        bool rightUp   = Input.GetMouseButtonUp(1);

        if (rightDown)
        {
            rightDragging = true;
            lastMousePos  = Input.mousePosition;
        }
        if (rightUp)
            rightDragging = false;

        if (rightDragging && rightHeld)
        {
            Vector2 mouse  = Input.mousePosition;
            Vector2 delta  = mouse - lastMousePos;
            lastMousePos   = mouse;

            float scroll = Input.GetAxis("Mouse ScrollWheel");

            if (scroll != 0f)
            {
                // Roll around camera forward axis.
                target.Rotate(cam.transform.forward, -scroll * rollSensitivity * 100f * Time.deltaTime, Space.World);
            }
            else
            {
                // Yaw around world up, pitch around camera right.
                target.Rotate(Vector3.up,             delta.x * rotateSensitivity, Space.World);
                target.Rotate(cam.transform.right,   -delta.y * rotateSensitivity, Space.World);
            }
        }
    }
}
