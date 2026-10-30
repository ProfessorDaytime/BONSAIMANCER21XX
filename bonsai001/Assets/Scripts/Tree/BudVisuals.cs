using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Visible buds on opposite-budded broadleaf trees — Japanese maple (2026-10-29, leaf-out step 3,
/// from the user's reference photos).
///
/// <list type="bullet">
/// <item><b>Dormant (Sep–Feb):</b> every branch tip that has set a bud carries a V-pair of small,
/// pointed, deep-red buds, turned 90° from the tip's leaf pair; tips long enough for a second leaf
/// pair also carry a smaller opposite pair further back, in that pair's leaf axils.</item>
/// <item><b>Swelling (February):</b> the buds lengthen, fatten and turn pinker.</item>
/// <item><b>Breaking (early March):</b> each tip bud splits into four scales that spread open around
/// the new shoot over <see cref="OpeningDays"/> in-game days, then shrink away.</item>
/// </list>
///
/// All buds are one combined mesh on a child object in the tree's own space, drawn with
/// <c>Custom/TreeBud</c>, which sways with the bark's wind math. The mesh is rebuilt only when the
/// bark mesh is rebuilt, or once an in-game day while buds are swelling or opening.
/// </summary>
public class BudVisuals : MonoBehaviour
{
    const float OpeningDays          = 12f;
    const float SecondPairLength     = 0.15f;   // matches LeafManager.oppositeSecondPairLength
    const int   Sides                = 6;

    TreeSkeleton    skeleton;
    TreeMeshBuilder bark;
    Mesh            mesh;
    MeshRenderer    meshRenderer;

    bool  dirty = true;
    int   lastDay = -1, lastMonth = -1;
    float nextBuild;

    // Tips drawn as dormant buds on the last build — promoted to opening buds when spring comes.
    readonly List<TreeNode> dormantTips = new List<TreeNode>();
    readonly List<TreeNode> openingTips = new List<TreeNode>();
    bool  lastWasDormant;
    int   openingYear = -1;
    float openingStartDay;

    readonly List<Vector3> verts   = new List<Vector3>();
    readonly List<Vector3> normals = new List<Vector3>();
    readonly List<Color>   colors  = new List<Color>();
    readonly List<Vector2> flex    = new List<Vector2>();
    readonly List<int>     tris    = new List<int>();

    static float DayNow() => GameManager.dayOfYear + GameManager.year * 366f;

    void Awake()
    {
        skeleton = GetComponent<TreeSkeleton>();
        bark     = GetComponent<TreeMeshBuilder>();

        var go = new GameObject("_Buds");
        go.transform.SetParent(transform, false);   // tree space, so the sway math matches the bark
        mesh = new Mesh { name = "TreeBuds", indexFormat = IndexFormat.UInt32 };
        mesh.MarkDynamic();
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        meshRenderer = go.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial    = Resources.Load<Material>("TreeBudMaterial");
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        if (meshRenderer.sharedMaterial == null)
            Debug.LogWarning("[Buds] Resources/TreeBudMaterial is missing; buds won't draw.");
    }

    void OnEnable()  { if (bark != null) bark.Rebuilt += MarkDirty; }
    void OnDisable() { if (bark != null) bark.Rebuilt -= MarkDirty; }
    void MarkDirty() => dirty = true;

    bool Active =>
        skeleton != null && skeleton.root != null && skeleton.species != null
        && skeleton.species.budType == BudType.Opposite
        && skeleton.species.foliageType == FoliageType.BroadLeaf
        && !QuickStartManager.Protected;   // nobody watches buds during the fast-grow

    void LateUpdate()
    {
        if (!Active)
        {
            if (mesh.vertexCount > 0) mesh.Clear();
            dormantTips.Clear(); openingTips.Clear(); lastWasDormant = false;
            return;
        }

        // Animate once an in-game day while something is changing; otherwise only on bark rebuilds.
        int day = GameManager.dayOfYear + GameManager.year * 366;
        if (day != lastDay)
        {
            bool animating = GameManager.month == 2 || openingTips.Count > 0 || GameManager.month != lastMonth;
            lastDay = day; lastMonth = GameManager.month;
            if (animating) dirty = true;
        }
        if (!dirty || Time.unscaledTime < nextBuild) return;
        nextBuild = Time.unscaledTime + 0.2f;
        dirty = false;
        Rebuild();
    }

    void Rebuild()
    {
        verts.Clear(); normals.Clear(); colors.Clear(); flex.Clear(); tris.Clear();

        var   sp      = skeleton.species;
        int   month   = GameManager.month;
        bool  dormant = month >= 9 || month <= 2;
        float swell   = month == 2 ? Mathf.SmoothStep(0f, 1f, (GameManager.day - 1) / 27f) : 0f;

        // Spring: the buds drawn at the end of winter break open.
        if (!dormant && lastWasDormant && openingYear != GameManager.year)
        {
            openingTips.Clear();
            openingTips.AddRange(dormantTips);
            openingYear     = GameManager.year;
            openingStartDay = DayNow();
        }
        lastWasDormant = dormant;

        dormantTips.Clear();
        if (dormant)
        {
            Color col = Color.Lerp(sp.budColor, sp.budSwellColor, swell);
            foreach (var n in skeleton.allNodes)
            {
                if (!n.isTerminal || n.isRoot || n.isTrimmed || n.isDead || n.isJin || !n.hasBud) continue;
                if (n.depth < 2) continue;
                dormantTips.Add(n);
                AddTipPair(n, swell, col);
                if (Mathf.Min(n.length, n.targetLength) >= SecondPairLength) AddLateralPair(n, swell, col);
            }
        }

        if (openingTips.Count > 0)
        {
            float p = (DayNow() - openingStartDay) / OpeningDays;
            if (p >= 1f) openingTips.Clear();
            else
                foreach (var n in openingTips)
                    if (n != null && !n.isTrimmed && !n.isDead) AddOpeningScales(n, Mathf.Clamp01(p), sp.budSwellColor);
        }

        mesh.Clear();
        if (verts.Count == 0) return;
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetColors(colors);
        mesh.SetUVs(1, flex);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
    }

    // ── Bud placement ─────────────────────────────────────────────────────────

    // Same per-node base angle as LeafManager's leaf pairs: pair 0 at base, pair 1 at base + 90°.
    static float BaseAzimuth(TreeNode n) => Mathf.Repeat(n.id * 137.508f, 360f);

    static Vector3 Side(TreeNode n, float azimuthDeg)
    {
        Vector3 axis = n.growDirection.normalized;
        Vector3 perp = Vector3.Cross(axis, Vector3.up);
        if (perp.sqrMagnitude < 0.01f) perp = Vector3.Cross(axis, Vector3.right);
        return Quaternion.AngleAxis(azimuthDeg, axis) * perp.normalized;
    }

    static float BudLength(TreeNode n, float swell) => Mathf.Clamp(n.radius * 5.5f, 0.018f, 0.055f) * (1f + 0.7f * swell);
    static float BudWidth (TreeNode n, float swell) => Mathf.Clamp(n.radius * 1.8f, 0.007f, 0.022f) * (1f + 0.5f * swell);

    /// <summary>The V-pair at the tip, between the tip's leaf pair (turned 90° from it).</summary>
    void AddTipPair(TreeNode n, float swell, Color col)
    {
        Vector3 axis = n.growDirection.normalized;
        Vector3 side = Side(n, BaseAzimuth(n) + 90f);
        Vector2 fx   = FlexOf(n);
        float   len  = BudLength(n, swell), wid = BudWidth(n, swell);
        foreach (float s in new[] { 1f, -1f })
        {
            Vector3 dir  = (axis * Mathf.Cos(25f * Mathf.Deg2Rad) + side * s * Mathf.Sin(25f * Mathf.Deg2Rad)).normalized;
            Vector3 root = n.tipPosition + side * s * n.radius * 0.35f;
            AddBud(root, dir, len, wid, col, fx);
        }
    }

    /// <summary>A smaller pair in the axils of the tip's second leaf pair, halfway back.</summary>
    void AddLateralPair(TreeNode n, float swell, Color col)
    {
        Vector3 axis = n.growDirection.normalized;
        Vector3 side = Side(n, BaseAzimuth(n) + 90f);
        Vector3 at   = n.tipPosition - axis * (0.55f * Mathf.Min(n.length, n.targetLength));
        Vector2 fx   = FlexOf(n);
        float   len  = BudLength(n, swell) * 0.7f, wid = BudWidth(n, swell) * 0.85f;
        foreach (float s in new[] { 1f, -1f })
        {
            Vector3 dir  = (axis * Mathf.Cos(40f * Mathf.Deg2Rad) + side * s * Mathf.Sin(40f * Mathf.Deg2Rad)).normalized;
            Vector3 root = at + side * s * n.radius * 0.85f;
            AddBud(root, dir, len, wid, col * 0.92f, fx);
        }
    }

    /// <summary>A broken bud: four scales spreading open around the new shoot, then shrinking.</summary>
    void AddOpeningScales(TreeNode n, float p, Color col)
    {
        Vector3 axis   = n.growDirection.normalized;
        float   baseAz = BaseAzimuth(n);
        Vector2 fx     = FlexOf(n);
        float   spread = Mathf.Lerp(20f, 70f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(p / 0.4f)));
        float   shrink = p < 0.6f ? 1f : 1f - (p - 0.6f) / 0.4f;
        float   len    = BudLength(n, 1f) * 1.3f * shrink, wid = BudWidth(n, 1f) * 0.8f * shrink;
        if (len < 0.002f) return;
        Color   c      = Color.Lerp(col, new Color(0.86f, 0.55f, 0.50f), p);   // scales pale as they open
        for (int k = 0; k < 4; k++)
        {
            Vector3 side = Side(n, baseAz + 90f + 90f * k);
            float   ang  = spread * (k % 2 == 0 ? 1f : 0.8f);
            Vector3 dir  = (axis * Mathf.Cos(ang * Mathf.Deg2Rad) + side * Mathf.Sin(ang * Mathf.Deg2Rad)).normalized;
            AddBud(n.tipPosition + side * n.radius * 0.3f, dir, len * (k % 2 == 0 ? 1f : 0.85f), wid, c, fx);
        }
    }

    /// <summary>Sway data for the twig a bud sits on, as the bark mesh carries it at that tip.</summary>
    Vector2 FlexOf(TreeNode n)
    {
        Color c = bark != null ? bark.SwayColorAt(n) : new Color(0f, 0f, 0f, 0f);
        return new Vector2(c.r, c.a);
    }

    /// <summary>A pointed bud: a six-sided spindle from <paramref name="root"/> along
    /// <paramref name="dir"/> — narrow at the base, widest a third of the way up, to a point.</summary>
    void AddBud(Vector3 root, Vector3 dir, float len, float wid, Color col, Vector2 fx)
    {
        Vector3 u = Vector3.Cross(dir, Vector3.up);
        if (u.sqrMagnitude < 0.01f) u = Vector3.Cross(dir, Vector3.right);
        u.Normalize();
        Vector3 v = Vector3.Cross(dir, u);

        int start = verts.Count;
        Color tipCol = Color.Lerp(col, Color.white, 0.12f);
        for (int ring = 0; ring < 2; ring++)
        {
            float   along  = ring == 0 ? 0f : len * 0.38f;
            float   radius = wid * (ring == 0 ? 0.45f : 0.5f);
            for (int i = 0; i < Sides; i++)
            {
                float   a      = i * Mathf.PI * 2f / Sides;
                Vector3 radial = u * Mathf.Cos(a) + v * Mathf.Sin(a);
                verts.Add(root + dir * along + radial * radius);
                normals.Add(ring == 0 ? (radial - dir * 0.3f).normalized : radial);
                colors.Add(col);
                flex.Add(fx);
            }
        }
        verts.Add(root + dir * len);
        normals.Add(dir);
        colors.Add(tipCol);
        flex.Add(fx);

        int tip = start + Sides * 2;
        for (int i = 0; i < Sides; i++)
        {
            int a0 = start + i, a1 = start + (i + 1) % Sides;
            int b0 = a0 + Sides, b1 = a1 + Sides;
            // Clockwise seen from outside (Unity's front face): cross(v1-v0, v2-v0) points outward.
            tris.Add(a0); tris.Add(a1); tris.Add(b0);
            tris.Add(a1); tris.Add(b1); tris.Add(b0);
            tris.Add(b0); tris.Add(b1); tris.Add(tip);
        }
    }
}
