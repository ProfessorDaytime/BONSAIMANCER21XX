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
    /// What the main menu shows (2026-10-29): the player's most recently saved menu tree, or, if
    /// they have none yet, the tree baked into the build.
    /// </summary>
    public static MenuTreeShowcase SpawnForMenu()
    {
        var snap = MenuTreeLibrary.LoadMostRecent();
        if (snap != null)
        {
            var built = SpawnFromSnapshot(snap);
            if (built != null) return built;
        }
        return SpawnIfBaked();
    }

    static readonly int IdBaseColor = Shader.PropertyToID("_BaseColor");
    static readonly int IdColor     = Shader.PropertyToID("_Color");

    /// <summary>Builds a player-saved menu tree (see <see cref="MenuTreeCapture"/>).</summary>
    public static MenuTreeShowcase SpawnFromSnapshot(MenuTreeSnapshot snap)
    {
        if (snap == null || snap.parts.Count == 0) return null;

        var mats = new Material[snap.materials.Count];
        for (int i = 0; i < mats.Length; i++) mats[i] = BuildMaterial(snap.materials[i]);

        var go = new GameObject("MenuTree");
        go.transform.SetPositionAndRotation(Vector3.zero, snap.rootRotation);
        go.transform.localScale = snap.rootScale;

        var mpb = new MaterialPropertyBlock();
        int built = 0;
        foreach (var p in snap.parts)
        {
            Mesh mesh = p.mesh != null ? BuildMesh(p.mesh) : MenuTreeRegistry.Resolve<Mesh>(p.meshKey);
            if (mesh == null) { Debug.LogWarning($"[MenuTree] Missing model {p.meshKey}; part skipped."); continue; }

            var part = new GameObject("Part");
            part.transform.SetParent(go.transform, false);
            var m = p.localMatrix;
            part.transform.localPosition = m.GetPosition();
            part.transform.localRotation = m.rotation;
            part.transform.localScale    = m.lossyScale;

            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = part.AddComponent<MeshRenderer>();
            var list = new Material[Mathf.Max(1, p.materials.Length)];
            for (int i = 0; i < p.materials.Length; i++)
            {
                int mi = p.materials[i];
                list[i] = mi >= 0 && mi < mats.Length ? mats[mi] : null;
            }
            mr.sharedMaterials   = list;
            mr.shadowCastingMode = p.cast;
            mr.receiveShadows    = p.receive;
            if (p.hasTint)
            {
                mpb.Clear();
                mpb.SetColor(IdBaseColor, p.tint);
                mpb.SetColor(IdColor, p.tint);
                mr.SetPropertyBlock(mpb);
            }
            built++;
        }
        if (built == 0) { Destroy(go); return null; }

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

    static Mesh BuildMesh(MenuTreeSnapshot.MeshData d)
    {
        var mesh = new Mesh { indexFormat = IndexFormat.UInt32, name = "MenuTreePart" };
        mesh.vertices = d.vertices;
        if (d.normals  != null) mesh.normals  = d.normals;
        if (d.tangents != null) mesh.tangents = d.tangents;
        if (d.colors   != null) mesh.colors   = d.colors;
        for (int c = 0; c < 4; c++)
            if (d.uvs[c] != null) mesh.SetUVs(c, d.uvs[c]);
        mesh.triangles = d.triangles;
        mesh.RecalculateBounds();
        return mesh;
    }

    static Material BuildMaterial(MenuTreeSnapshot.MaterialRecipe r)
    {
        var sh = Shader.Find(r.shader);
        if (sh == null)
        {
            Debug.LogWarning($"[MenuTree] Shader '{r.shader}' isn't in this build; using URP Lit.");
            sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) return null;
        }
        var m = new Material(sh);
        foreach (var (n, v) in r.floats)
        {
            if (!m.HasProperty(n)) continue;
            int pi = sh.FindPropertyIndex(n);
            if (pi >= 0 && sh.GetPropertyType(pi) == ShaderPropertyType.Int) m.SetInteger(n, (int)v);
            else m.SetFloat(n, v);
        }
        foreach (var (n, v) in r.colors)  if (m.HasProperty(n)) m.SetColor(n, v);
        foreach (var (n, v) in r.vectors) if (m.HasProperty(n)) m.SetVector(n, v);
        foreach (var (n, key, sc, of) in r.textures)
        {
            if (!m.HasProperty(n)) continue;
            var tex = MenuTreeRegistry.Resolve<Texture>(key);
            if (tex == null) continue;
            m.SetTexture(n, tex);
            m.SetTextureScale(n, sc);
            m.SetTextureOffset(n, of);
        }
        m.shaderKeywords = r.keywords;
        m.renderQueue    = r.renderQueue;
        return m;
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
