using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Draws two overlapping scale grids at soil level:
///   - Yellow: 1×1×1 metre cubes, stacked 2 high (≈6.5 ft tall column)
///   - Pink:   1×1×1 foot cubes (0.3048 m), 1 high
/// Toggled from the Debug tab in the Settings menu.
/// Attach to the same GameObject as TreeSkeleton.
/// </summary>
public class ScaleDebugger : MonoBehaviour
{
    [Tooltip("Show the scale grid (can also be toggled from the Debug tab at runtime).")]
    [SerializeField] bool visible = false;

    [Tooltip("How many 1 m cells to draw in each direction from centre.")]
    [SerializeField] int metreRadius = 1;

    [Tooltip("How many 1 m cubes to stack vertically.")]
    [SerializeField] int metreStack = 2;

    [Tooltip("How many 1 ft cubes to stack vertically.")]
    [SerializeField] int feetStack = 7;

    [Tooltip("Y position (world) of the grid base. Auto-read from TreeSkeleton if zero.")]
    [SerializeField] float worldY = 0f;

    [Tooltip("Colour of the 1-metre wireframe grid.")]
    [SerializeField] Color metreColor = new Color(1f, 0.85f, 0.1f, 0.85f);

    [Tooltip("Colour of the 1-foot wireframe grid.")]
    [SerializeField] Color feetColor  = new Color(1f, 0.45f, 0.75f, 0.70f);

    public bool Visible { get => visible; set => visible = value; }

    const float FOOT = 0.3048f;

    Material _mat;

    void Awake()
    {
        _mat = new Material(Shader.Find("Hidden/Internal-Colored"));
        _mat.hideFlags = HideFlags.HideAndDontSave;
        _mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        _mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        _mat.SetInt("_Cull",     (int)UnityEngine.Rendering.CullMode.Off);
        _mat.SetInt("_ZWrite",   0);
        _mat.SetInt("_ZTest",    (int)UnityEngine.Rendering.CompareFunction.Always);
    }

    void OnEnable()  => RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
    void OnDisable() => RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
    void OnDestroy() { if (_mat != null) Destroy(_mat); }

    void OnEndCameraRendering(ScriptableRenderContext ctx, Camera camera)
    {
        if (!Visible || _mat == null) return;
        if (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView) return;

        // Resolve base Y: prefer inspector override, then TreeSkeleton soil surface
        float baseY = worldY;
        if (Mathf.Approximately(baseY, 0f))
        {
            var skel = GetComponent<TreeSkeleton>();
            if (skel != null) baseY = skel.plantingSurfacePoint.y;
        }

        _mat.SetPass(0);
        GL.PushMatrix();
        GL.LoadProjectionMatrix(camera.projectionMatrix);
        GL.modelview = camera.worldToCameraMatrix;
        GL.Begin(GL.LINES);

        float cx = transform.position.x;
        float cz = transform.position.z;

        // ── Metre grid (yellow) ─────────────────────────────────────────────
        GL.Color(metreColor);
        for (int xi = -metreRadius; xi <= metreRadius; xi++)
        for (int zi = -metreRadius; zi <= metreRadius; zi++)
        for (int yi = 0; yi < metreStack; yi++)
            DrawWireBox(new Vector3(cx + xi, baseY + yi, cz + zi), Vector3.one);

        // ── Foot grid (pink) — same physical footprint as metre grid ────────
        int feetRadius = Mathf.CeilToInt((float)metreRadius / FOOT);
        GL.Color(feetColor);
        for (int xi = -feetRadius; xi <= feetRadius; xi++)
        for (int zi = -feetRadius; zi <= feetRadius; zi++)
        for (int yi = 0; yi < feetStack; yi++)
            DrawWireBox(new Vector3(cx + xi * FOOT, baseY + yi * FOOT, cz + zi * FOOT),
                        new Vector3(FOOT, FOOT, FOOT));

        GL.End();
        GL.PopMatrix();
    }

    static void DrawWireBox(Vector3 o, Vector3 s)
    {
        float x0 = o.x, x1 = o.x + s.x;
        float y0 = o.y, y1 = o.y + s.y;
        float z0 = o.z, z1 = o.z + s.z;

        // Bottom face
        Line(x0,y0,z0, x1,y0,z0); Line(x1,y0,z0, x1,y0,z1);
        Line(x1,y0,z1, x0,y0,z1); Line(x0,y0,z1, x0,y0,z0);
        // Top face
        Line(x0,y1,z0, x1,y1,z0); Line(x1,y1,z0, x1,y1,z1);
        Line(x1,y1,z1, x0,y1,z1); Line(x0,y1,z1, x0,y1,z0);
        // Verticals
        Line(x0,y0,z0, x0,y1,z0); Line(x1,y0,z0, x1,y1,z0);
        Line(x1,y0,z1, x1,y1,z1); Line(x0,y0,z1, x0,y1,z1);
    }

    static void Line(float x0,float y0,float z0, float x1,float y1,float z1)
    {
        GL.Vertex3(x0,y0,z0);
        GL.Vertex3(x1,y1,z1);
    }
}
