using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Visible buds on opposite-budded broadleaf trees — the maples (2026-10-29, leaf-out step 3,
/// from the user's reference photos).
///
/// <list type="bullet">
/// <item><b>Forming (summer):</b> once a shoot stops extending, its buds form under the leaves —
/// tiny and green, blushed red at the tip — then grow through the summer and autumn and ripen to
/// the dormant colour. Calendar-driven (<see cref="Development"/>), so a loaded save shows the
/// right size straight away.</item>
/// <item><b>Dormant (Sep–Feb):</b> every branch tip that has set a bud carries a V-pair of small,
/// pointed, deep-red buds, turned 90° from the tip's leaf pair; tips long enough for a second leaf
/// pair also carry a smaller opposite pair further back, in that pair's leaf axils.</item>
/// <item><b>Swelling (February):</b> the buds lengthen, fatten and turn pinker.</item>
/// <item><b>Breaking (early March):</b> each tip bud splits into four scales that spread open around
/// the new shoot over <see cref="OpeningDays"/> in-game days, then shrink away. A dormant side bud
/// that wakes into a new shoot (back-bud, old-wood bud) first swells out of the bark at the shoot's
/// base, then opens the same way.</item>
/// </list>
///
/// All buds are one combined mesh on a child object in the tree's own space, drawn with
/// <c>Custom/TreeBud</c>, which sways with the bark's wind math. The mesh is rebuilt when the bark
/// mesh is rebuilt, once an in-game day while buds swell or open, and every few days while they form.
/// </summary>
public class BudVisuals : MonoBehaviour
{
    const float OpeningDays       = 12f;
    const float SideOpeningDays   = 14f;     // a side bud first swells out of the bark…
    const float SideSwellShare    = 0.15f;   // …over this share of its days, then opens
    const float SecondPairLength  = 0.15f;   // matches LeafManager.oppositeSecondPairLength
    const int   Sides             = 6;
    const float FormStart         = 0.06f;   // size of a bud on the day it forms
    const int   FullSizeDay       = 304;     // day of year (Oct 31) forming buds reach dormant size
    const int   FormRebuildDays   = 3;       // forming buds grow slowly; rebuild every few days
    const float SideBudTwigRadius = 0.006f;  // side buds are sized like a typical twig's tip bud

    static readonly Color ScalePale = new Color(1f, 0.85f, 0.75f);   // opening scales pale toward this

    TreeSkeleton    skeleton;
    TreeMeshBuilder bark;
    Mesh            mesh;
    MeshRenderer    meshRenderer;

    bool  dirty = true;
    int   lastDay = -1, lastMonth = -1, lastFormDay = -1;
    float nextBuild;

    // Tips drawn as dormant buds on the last build — promoted to opening buds when spring comes.
    readonly List<TreeNode> dormantTips = new List<TreeNode>();
    readonly List<TreeNode> openingTips = new List<TreeNode>();
    bool  lastWasDormant;
    int   openingYear = -1;
    float openingStartDay;

    // New shoots that broke from dormant side buds, with the day each broke.
    readonly List<(TreeNode shoot, float start)> sideOpenings = new List<(TreeNode, float)>();
    TreeNode trackedRoot;   // a new root means a load, a new tree or a sever: forget the old buds

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

    void OnEnable()
    {
        if (bark != null)     bark.Rebuilt            += MarkDirty;
        if (skeleton != null) skeleton.OnSideBudBroke += SideBudBroke;
    }
    void OnDisable()
    {
        if (bark != null)     bark.Rebuilt            -= MarkDirty;
        if (skeleton != null) skeleton.OnSideBudBroke -= SideBudBroke;
    }
    void MarkDirty() => dirty = true;

    void SideBudBroke(TreeNode shoot)
    {
        if (!Active) return;
        if (skeleton.root != trackedRoot) { trackedRoot = skeleton.root; ForgetBuds(); }   // before LateUpdate would
        sideOpenings.Add((shoot, DayNow()));
        dirty = true;
    }

    bool Active =>
        skeleton != null && skeleton.root != null && skeleton.species != null
        && skeleton.species.budType == BudType.Opposite
        && skeleton.species.foliageType == FoliageType.BroadLeaf
        && !QuickStartManager.Protected;   // nobody watches buds during the fast-grow

    void ForgetBuds()
    {
        dormantTips.Clear(); openingTips.Clear(); sideOpenings.Clear();
        lastWasDormant = false;
    }

    void LateUpdate()
    {
        if (!Active)
        {
            if (mesh.vertexCount > 0) mesh.Clear();
            ForgetBuds();
            return;
        }
        if (skeleton.root != trackedRoot) { trackedRoot = skeleton.root; ForgetBuds(); dirty = true; }

        // Animate once an in-game day while buds swell or open, every few days while they form;
        // otherwise only on bark rebuilds.
        int day = GameManager.dayOfYear + GameManager.year * 366;
        if (day != lastDay)
        {
            bool animating = GameManager.month == 2 || openingTips.Count > 0 || sideOpenings.Count > 0
                             || GameManager.month != lastMonth;
            int  doy       = GameManager.dayOfYear;
            if (doy >= skeleton.species.growthSlowDay && doy <= FullSizeDay + 1 && day - lastFormDay >= FormRebuildDays)
            {
                lastFormDay = day;
                animating   = true;
            }
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
        // Buds form in the leaf axils as the shoots slow (growthSlowDay — June on the maples).
        bool  forming = !dormant && GameManager.dayOfYear >= sp.growthSlowDay;
        float swell   = month == 2 ? Mathf.SmoothStep(0f, 1f, (GameManager.day - 1) / 27f) : 0f;
        float dev     = Development(sp);
        float form    = Mathf.Lerp(FormStart, 1f, dev);
        float ripen   = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 0.85f, dev));   // green in July, red by October

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
        if (dormant || forming)
        {
            // Green, blushed red at the tip, while forming; the dormant red once ripe; pinker as it swells.
            Color col = Color.Lerp(Color.Lerp(sp.budFormColor, sp.budColor, ripen), sp.budSwellColor, swell);
            Color tip = Color.Lerp(Lighter(col), sp.budColor, 0.55f * (1f - ripen));
            foreach (var n in skeleton.allNodes)
            {
                if (!n.isTerminal || n.isRoot || n.isTrimmed || n.isDead || n.isJin) continue;
                if (n.depth < 2) continue;
                if (dormant)
                {
                    if (!n.hasBud) continue;
                    dormantTips.Add(n);
                }
                // Forming: only on shoots that have stopped extending — the tips SetBuds will bud
                // on Sep 1 (it skips mid-chord sub-segments).
                else if (n.subdivisionsLeft > 0 || (n.isGrowing && GameManager.dayOfYear < sp.growthStopDay)) continue;
                AddTipPair(n, swell, col, tip, form);
                if (Mathf.Min(n.length, n.targetLength) >= SecondPairLength) AddLateralPair(n, swell, col, tip, form);
            }
        }

        if (openingTips.Count > 0)
        {
            float p = (DayNow() - openingStartDay) / OpeningDays;
            if (p >= 1f) openingTips.Clear();
            else
                foreach (var n in openingTips)
                    if (n != null && !n.isTrimmed && !n.isDead) AddTipOpening(n, Mathf.Clamp01(p), sp.budSwellColor);
        }

        float now = DayNow();
        for (int i = sideOpenings.Count - 1; i >= 0; i--)
        {
            var (shoot, start) = sideOpenings[i];
            float p = (now - start) / SideOpeningDays;
            if (p >= 1f || shoot.isTrimmed || shoot.isDead || shoot.parent == null || shoot.parent.isTrimmed)
            {
                sideOpenings.RemoveAt(i);
                continue;
            }
            AddSideOpening(shoot, Mathf.Max(0f, p), sp.budSwellColor);
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

    /// <summary>How far this year's buds have developed: 0 on the day they start forming (the
    /// species' growthSlowDay), 1 at full dormant size (end of October) and all winter.</summary>
    static float Development(TreeSpecies sp)
    {
        if (GameManager.month <= 2) return 1f;
        int start = Mathf.Min(sp.growthSlowDay, FullSizeDay - 30);
        return Mathf.Clamp01((GameManager.dayOfYear - start) / (float)(FullSizeDay - start));
    }

    // ── Bud placement ─────────────────────────────────────────────────────────

    // Same per-node base angle as LeafManager's leaf pairs: pair 0 at base, pair 1 at base + 90°.
    internal static float BaseAzimuth(TreeNode n) => Mathf.Repeat(n.id * 137.508f, 360f);

    static Vector3 Side(TreeNode n, float azimuthDeg) => Side(n.growDirection.normalized, azimuthDeg);

    /// <summary>A unit vector perpendicular to <paramref name="axis"/>, turned
    /// <paramref name="azimuthDeg"/> around it (also used by FlowerVisuals).</summary>
    internal static Vector3 Side(Vector3 axis, float azimuthDeg)
    {
        Vector3 perp = Vector3.Cross(axis, Vector3.up);
        if (perp.sqrMagnitude < 0.01f) perp = Vector3.Cross(axis, Vector3.right);
        return Quaternion.AngleAxis(azimuthDeg, axis) * perp.normalized;
    }

    static Color Lighter(Color c) => Color.Lerp(c, Color.white, 0.12f);

    // Smaller, and far less dependent on twig thickness (2026-10-29: "they start a little too big";
    // thick cut stumps carried the largest buds). A typical tip (r ≈ 0.006) gets ~0.022 × 0.009.
    static float BudLength(float r, float swell) => Mathf.Clamp(0.014f + r * 1.4f, 0.014f, 0.03f) * (1f + 0.7f * swell);
    static float BudWidth (float r, float swell) => Mathf.Clamp(0.005f + r * 0.6f, 0.005f, 0.012f) * (1f + 0.5f * swell);

    /// <summary>The V-pair at the tip, between the tip's leaf pair (turned 90° from it).</summary>
    void AddTipPair(TreeNode n, float swell, Color col, Color tipCol, float form)
    {
        Vector3 axis = n.growDirection.normalized;
        Vector3 side = Side(n, BaseAzimuth(n) + 90f);
        Vector2 fx   = FlexOf(n);
        float   len  = BudLength(n.radius, swell) * form, wid = BudWidth(n.radius, swell) * form;
        foreach (float s in new[] { 1f, -1f })
        {
            Vector3 dir  = (axis * Mathf.Cos(25f * Mathf.Deg2Rad) + side * s * Mathf.Sin(25f * Mathf.Deg2Rad)).normalized;
            Vector3 root = n.tipPosition + side * s * n.radius * 0.35f;
            AddBud(root, dir, len, wid, col, tipCol, fx);
        }
    }

    /// <summary>A smaller pair in the axils of the tip's second leaf pair, halfway back.</summary>
    void AddLateralPair(TreeNode n, float swell, Color col, Color tipCol, float form)
    {
        Vector3 axis = n.growDirection.normalized;
        Vector3 side = Side(n, BaseAzimuth(n) + 90f);
        Vector3 at   = n.tipPosition - axis * (0.55f * Mathf.Min(n.length, n.targetLength));
        Vector2 fx   = FlexOf(n);
        float   len  = BudLength(n.radius, swell) * 0.7f * form, wid = BudWidth(n.radius, swell) * 0.85f * form;
        foreach (float s in new[] { 1f, -1f })
        {
            Vector3 dir  = (axis * Mathf.Cos(40f * Mathf.Deg2Rad) + side * s * Mathf.Sin(40f * Mathf.Deg2Rad)).normalized;
            Vector3 root = at + side * s * n.radius * 0.85f;
            AddBud(root, dir, len, wid, col * 0.92f, tipCol * 0.92f, fx);
        }
    }

    /// <summary>A broken tip bud: four scales spreading open around the new shoot, then shrinking.</summary>
    void AddTipOpening(TreeNode n, float p, Color col) =>
        AddScales(n.tipPosition, n.growDirection.normalized, BaseAzimuth(n), n.radius, n.radius, 1f, p, col, FlexOf(n));

    /// <summary>A dormant side bud waking into a new shoot: it swells out of the bark where the
    /// shoot leaves its parent, then opens around the shoot like a tip bud.</summary>
    void AddSideOpening(TreeNode shoot, float p, Color col)
    {
        TreeNode parent  = shoot.parent;
        Vector3  axis    = shoot.growDirection.normalized;
        Vector3  stem    = parent.growDirection.normalized;
        Vector3  outward = axis - stem * Vector3.Dot(axis, stem);
        Vector3  at      = shoot.worldPosition;   // the junction's centre, inside the parent's wood…
        if (outward.sqrMagnitude > 1e-4f) at += outward.normalized * parent.tipRadius * 0.9f;   // …so out to its bark
        float swellIn = Mathf.SmoothStep(0f, 1f, p / SideSwellShare);
        float open    = Mathf.Clamp01((p - SideSwellShare) / (1f - SideSwellShare));
        AddScales(at, axis, BaseAzimuth(shoot), SideBudTwigRadius, shoot.radius, 0.85f * swellIn, open, col, FlexOf(parent));
    }

    /// <summary>Four bud scales around <paramref name="axis"/>: closed at p = 0, spread open by
    /// p = 0.4, shrinking away after p = 0.6, paling as they open.</summary>
    void AddScales(Vector3 at, Vector3 axis, float baseAz, float budRadius, float shootRadius,
                   float size, float p, Color col, Vector2 fx)
    {
        float spread = Mathf.Lerp(20f, 70f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(p / 0.4f)));
        float shrink = p < 0.6f ? 1f : 1f - (p - 0.6f) / 0.4f;
        float len    = BudLength(budRadius, 1f) * 1.3f * shrink * size;
        float wid    = BudWidth(budRadius, 1f) * 0.8f * shrink * size;
        if (len < 0.002f) return;
        Color c = Color.Lerp(col, Color.Lerp(col, ScalePale, 0.4f), p);
        for (int k = 0; k < 4; k++)
        {
            Vector3 side = Side(axis, baseAz + 90f + 90f * k);
            float   ang  = spread * (k % 2 == 0 ? 1f : 0.8f);
            Vector3 dir  = (axis * Mathf.Cos(ang * Mathf.Deg2Rad) + side * Mathf.Sin(ang * Mathf.Deg2Rad)).normalized;
            AddBud(at + side * shootRadius * 0.3f, dir, len * (k % 2 == 0 ? 1f : 0.85f), wid, c, Lighter(c), fx);
        }
    }

#if UNITY_EDITOR
    /// <summary>Editor export (Bonsai ▸ Export Flower and Fruit Shapes): a tip's dormant bud pairs,
    /// or its bud scales opening at <paramref name="p"/>, built on a stand-in twig. Works on a
    /// component that never woke, so no mesh object is needed.</summary>
    internal Mesh BuildReference(TreeNode n, TreeSpecies sp, bool opening, float p)
    {
        verts.Clear(); normals.Clear(); colors.Clear(); flex.Clear(); tris.Clear();
        if (opening) AddTipOpening(n, p, sp.budSwellColor);
        else
        {
            AddTipPair(n, 0f, sp.budColor, Lighter(sp.budColor), 1f);
            AddLateralPair(n, 0f, sp.budColor, Lighter(sp.budColor), 1f);
        }
        return ReferenceMesh(opening ? "BudOpening" : "BudsDormant");
    }

    /// <summary>Editor export: one dormant tip bud, or one bud scale at full spread, base at the
    /// origin along +Z, sized for a typical tip (r = 0.006).</summary>
    internal Mesh BuildReferencePart(TreeSpecies sp, bool scale)
    {
        verts.Clear(); normals.Clear(); colors.Clear(); flex.Clear(); tris.Clear();
        const float r = 0.006f;
        if (scale)
            AddBud(Vector3.zero, Vector3.forward, BudLength(r, 1f) * 1.3f, BudWidth(r, 1f) * 0.8f,
                   sp.budSwellColor, Lighter(sp.budSwellColor), Vector2.one);
        else
            AddBud(Vector3.zero, Vector3.forward, BudLength(r, 0f), BudWidth(r, 0f),
                   sp.budColor, Lighter(sp.budColor), Vector2.one);
        return ReferenceMesh(scale ? "BudScale" : "LeafBud");
    }

    Mesh ReferenceMesh(string name)
    {
        var m = new Mesh { name = name };
        m.SetVertices(verts);
        m.SetNormals(normals);
        m.SetColors(colors);
        m.SetTriangles(tris, 0);
        m.RecalculateBounds();
        return m;
    }
#endif

    /// <summary>Sway data for the twig a bud sits on, as the bark mesh carries it at that tip.</summary>
    Vector2 FlexOf(TreeNode n)
    {
        Color c = bark != null ? bark.SwayColorAt(n) : new Color(0f, 0f, 0f, 0f);
        return new Vector2(c.r, c.a);
    }

    /// <summary>A pointed bud: a six-sided spindle from <paramref name="root"/> along
    /// <paramref name="dir"/> — narrow at the base, widest a third of the way up, to a point.</summary>
    void AddBud(Vector3 root, Vector3 dir, float len, float wid, Color col, Color tipCol, Vector2 fx)
    {
        Vector3 u = Vector3.Cross(dir, Vector3.up);
        if (u.sqrMagnitude < 0.01f) u = Vector3.Cross(dir, Vector3.right);
        u.Normalize();
        Vector3 v = Vector3.Cross(dir, u);

        int start = verts.Count;
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
