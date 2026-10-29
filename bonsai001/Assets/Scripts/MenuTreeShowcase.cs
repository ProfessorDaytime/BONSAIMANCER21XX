using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The player-grown tree standing in the main menu (PLAN "Release Prep" R3).
///
/// <para>Lives on the root of the baked <c>Resources/MenuTree/MenuTree</c> prefab, which
/// <c>Assets/Editor/MenuTreeBaker.cs</c> writes from a live tree during Play mode. The prefab is
/// nothing but static meshes plus this component — no TreeSkeleton, no GameManager, no colliders —
/// so the menu can't be broken by a sim or save-format change and nothing in it is clickable.</para>
///
/// <para>This component carries what was captured at bake time (camera pose, lights, ambient,
/// background) and presents it: it puts the menu camera where the player's camera was, re-frames
/// the tree to the right of the menu column, and drifts the camera slowly around it.</para>
///
/// <para>The tree itself is still. Its bark material sways only when a WindManager publishes the
/// wind shader globals, and the menu scene has none, so the bark stays at rest — which is also the
/// pose the leaves were baked in.</para>
/// </summary>
public class MenuTreeShowcase : MonoBehaviour
{
    public const string ResourcePath = "MenuTree/MenuTree";

    // ── Bake request (editor) ─────────────────────────────────────────────────
    // The baker is editor code, which runtime code can't reference. It subscribes here instead,
    // so the in-game "Bake as Menu Tree" button can reach it. Null in a player build.
    public static event System.Action BakeRequested;
    public static bool CanBake => BakeRequested != null;
    public static void RequestBake() => BakeRequested?.Invoke();

    [System.Serializable]
    public struct BakedLight
    {
        public Quaternion   localRotation;   // relative to the tree root
        public Color        color;
        public float        intensity;
        public LightShadows shadows;
        public float        shadowStrength;
    }

    [Header("Captured at bake (relative to the tree root)")]
    public Vector3          cameraLocalPosition;
    public Quaternion       cameraLocalRotation = Quaternion.identity;
    public float            fieldOfView = 60f;
    public float            nearClip = 0.05f;
    public float            farClip  = 500f;
    public CameraClearFlags clearFlags = CameraClearFlags.SolidColor;
    public Color            backgroundColor = Color.black;
    public Material         skybox;
    public AmbientMode      ambientMode = AmbientMode.Flat;
    public Color            ambientSky, ambientEquator, ambientGround;
    public float            ambientIntensity = 1f;
    public BakedLight[]     lights;
    [Tooltip("Centre of the tree's bark and foliage; the camera drifts around it.")]
    public Vector3          pivotLocal;

    [Header("Presentation")]
    [Tooltip("Where the tree sits across the screen (0 = left edge, 1 = right edge). The menu " +
             "column is on the left.")]
    [Range(0f, 1f)] public float screenX = 0.64f;
    [Tooltip("How far the camera swings either side of the baked angle, in degrees.")]
    public float driftDegrees = 10f;
    [Tooltip("Seconds for one full swing there and back.")]
    public float driftPeriod  = 70f;

    Camera     cam;
    Vector3    pivot;
    Vector3    startOffset;   // camera position relative to the pivot, at the baked pose
    Quaternion startRotation;

    /// <summary>Instantiates the baked tree, if one has been baked. Called by MainMenu.</summary>
    public static MenuTreeShowcase SpawnIfBaked()
    {
        var prefab = Resources.Load<GameObject>(ResourcePath);
        if (prefab == null) return null;
        var go = Instantiate(prefab);
        go.name = prefab.name;
        return go.GetComponent<MenuTreeShowcase>();
    }

    /// <summary>
    /// What the main menu shows (2026-10-29): the tree the player set as their menu tree in the
    /// greenhouse, else their most recently saved one, else the tree baked into the build.
    /// </summary>
    public static MenuTreeShowcase SpawnForMenu()
    {
        var snap = MenuTreeLibrary.LoadForMenu();
        if (snap != null)
        {
            var built = SpawnFromSnapshot(snap);
            if (built != null) return built;
        }
        return SpawnIfBaked();
    }

    /// <summary>Builds a player-saved menu tree (see <see cref="MenuTreeCapture"/>) and frames it
    /// as it was captured.</summary>
    public static MenuTreeShowcase SpawnFromSnapshot(MenuTreeSnapshot snap)
    {
        var mats = MenuTreeBuilder.BuildMaterials(snap);
        var go   = MenuTreeBuilder.Build(snap, mats);
        if (go == null) return null;

        var s = go.AddComponent<MenuTreeShowcase>();
        s.cameraLocalPosition = snap.cameraLocalPosition;
        s.cameraLocalRotation = snap.cameraLocalRotation;
        s.fieldOfView      = snap.fieldOfView;
        s.nearClip         = snap.nearClip;
        s.farClip          = snap.farClip;
        s.clearFlags       = snap.clearFlags;
        s.backgroundColor  = snap.backgroundColor;
        s.skybox           = snap.skyboxMaterial >= 0 && snap.skyboxMaterial < mats.Length ? mats[snap.skyboxMaterial] : null;
        if (s.clearFlags == CameraClearFlags.Skybox && s.skybox == null) s.clearFlags = CameraClearFlags.SolidColor;
        s.ambientMode      = snap.ambientMode;
        s.ambientSky       = snap.ambientSky;
        s.ambientEquator   = snap.ambientEquator;
        s.ambientGround    = snap.ambientGround;
        s.ambientIntensity = snap.ambientIntensity;
        s.lights           = snap.lights.ToArray();
        s.pivotLocal       = snap.pivotLocal;
        return s;
    }

    void Start()
    {
        ApplyEnvironment();

        cam = Camera.main;
        if (cam == null) return;

        cam.fieldOfView   = fieldOfView;
        cam.nearClipPlane = nearClip;
        cam.farClipPlane  = farClip;
        cam.clearFlags    = clearFlags;
        cam.backgroundColor = backgroundColor;

        pivot         = transform.TransformPoint(pivotLocal);
        startOffset   = transform.TransformPoint(cameraLocalPosition) - pivot;
        startRotation = transform.rotation * cameraLocalRotation;
        PlaceCamera();
    }

    void LateUpdate()
    {
        if (cam != null) PlaceCamera();
    }

    /// <summary>
    /// The baked pose, swung about the pivot's vertical axis by the drift, then slid sideways so
    /// the pivot lands at <see cref="screenX"/>. In-game the tree was framed in the middle of the
    /// screen; in the menu the left side belongs to the buttons.
    /// </summary>
    void PlaceCamera()
    {
        float yaw = GameSettings.ReducedMotion || driftPeriod <= 0f
            ? 0f
            : Mathf.Sin(Time.unscaledTime * 2f * Mathf.PI / driftPeriod) * driftDegrees;
        var swing = Quaternion.AngleAxis(yaw, Vector3.up);

        Vector3    pos = pivot + swing * startOffset;
        Quaternion rot = swing * startRotation;

        Vector3 local    = Quaternion.Inverse(rot) * (pivot - pos);
        float   hFov     = Camera.VerticalToHorizontalFieldOfView(cam.fieldOfView, cam.aspect);
        float   halfW    = Mathf.Tan(hFov * 0.5f * Mathf.Deg2Rad) * Mathf.Max(local.z, 0.01f);
        float   wantX    = (screenX - 0.5f) * 2f * halfW;
        pos += rot * Vector3.right * (local.x - wantX);

        cam.transform.SetPositionAndRotation(pos, rot);
    }

    /// <summary>Lights and ambient as they were when the tree was baked, so it looks the way the
    /// player saw it. The scene's own directional lights are switched off in favour of these.</summary>
    void ApplyEnvironment()
    {
        if (lights != null && lights.Length > 0)
        {
            foreach (var l in FindObjectsByType<Light>())
                if (l.type == LightType.Directional && !l.transform.IsChildOf(transform))
                    l.enabled = false;

            for (int i = 0; i < lights.Length; i++)
            {
                var b  = lights[i];
                var go = new GameObject($"MenuLight{i}");
                go.transform.SetParent(transform, false);
                go.transform.rotation = transform.rotation * b.localRotation;
                var l = go.AddComponent<Light>();
                l.type           = LightType.Directional;
                l.color          = b.color;
                l.intensity      = b.intensity;
                l.shadows        = b.shadows;
                l.shadowStrength = b.shadowStrength;
            }
        }

        if (skybox != null) RenderSettings.skybox = skybox;
        RenderSettings.ambientMode         = ambientMode;
        RenderSettings.ambientSkyColor     = ambientSky;
        RenderSettings.ambientEquatorColor = ambientEquator;
        RenderSettings.ambientGroundColor  = ambientGround;
        RenderSettings.ambientIntensity    = ambientIntensity;
    }
}
