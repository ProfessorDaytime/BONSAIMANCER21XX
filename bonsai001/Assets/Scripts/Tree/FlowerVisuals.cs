using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Flowers and fruit as one swaying mesh, drawn from what each branch stores plus the date
/// (2026-10-29, blossoms &amp; fruit step 1). Takes species over from <see cref="FlowerManager"/>
/// one group at a time; today the maples (<see cref="BloomType.Corymb"/>).
///
/// <list type="bullet">
/// <item><b>Spring bud break:</b> a tip's flower bud (<c>TreeNode.hasFlowerBud</c>, set in autumn by
/// SetBuds) is spent. <c>bloomYear</c> records the year and <c>fruitMask</c> which of the cluster's
/// flowers will set fruit. Both are saved, and everything below is rebuilt from them and the
/// calendar, so a load shows the right stage.</item>
/// <item><b>March:</b> the cluster comes out of the opening bud with the leaves: a stalk that
/// lengthens and droops, carrying tight flower buds.</item>
/// <item><b>bloomMonth:</b> the flowers open one by one over a few days, hold for
/// bloomDurationDays, then wither; those that set no fruit drop away.</item>
/// <item><b>To the end of fruitSetMonth:</b> each fertilised flower grows into a pair of winged
/// samaras.</item>
/// <item><b>Around fruitRipeMonth:</b> they ripen from fruitColor to fruitRipeColor, then let go one
/// at a time over the following weeks and spin down (<see cref="SamaraSpinner"/>); a few hang on
/// through the winter and drop in the weeks before bud break.</item>
/// </list>
///
/// Drawn with <c>Custom/TreeBud</c> two-sided (<c>_Cull</c> off: sepals and wings are single
/// sheets), so it sways with the bark's wind math; hanging parts sway more the further they hang.
/// Rebuilt daily while a cluster is alive and whenever the bark mesh is rebuilt.
/// </summary>
public class FlowerVisuals : MonoBehaviour
{
    // ── Calendar (in-game days) ───────────────────────────────────────────────
    // Maples flower while their leaves unfold (2026-10-29: opening in April put the flowers under a
    // full-grown canopy, and nobody saw them).
    const float EmergeDelay     = 1f;     // after bud break (Mar 1), as the bud scales part
    const float EmergeStagger   = 3f;
    const float EmergeDays      = 8f;
    const float OpenStagger     = 6f;     // clusters open over the first week of bloomMonth…
    const float FlowerStagger   = 3f;     // …the flowers in a cluster over a few days…
    const float OpenDays        = 3f;     // …each taking this long to open
    const float WitherDays      = 6f;
    const float RipenLead       = 15f;    // ripening starts this long before fruitRipeMonth
    const float DropLead        = 15f;    // the first samaras let go this long before it ends…
    const float DropSpread      = 50f;    // …and the rest over this many days
    const float PersistShare    = 0.2f;   // the share that hang on through winter…
    const float PersistDropDays = 20f;    // …and drop over the last weeks before bud break, so they're
                                          // gone before the tip's next flower bud is spent on Mar 1

    // ── Sizes (tree units) ────────────────────────────────────────────────────
    const float PeduncleLength = 0.06f,  PeduncleRadius = 0.0014f;
    const float PedicelLength  = 0.026f, PedicelRadius  = 0.0008f;
    const float SepalLength    = 0.02f,  SepalWidth     = 0.012f;    // a flower ~2× a bud's length across
    const float SeedLength     = 0.012f, SeedWidth      = 0.0075f;
    const float WingLength     = 0.05f,  WingWidth      = 0.017f;
    const float HangFlex       = 0.6f;    // the hanging end sways this much more freely than its twig
    const int   MaxFlowers     = 16;      // fruitMask has a bit per flower
    const int   MaxSpinners    = 40;

    static readonly Color FlowerHeart = new Color(1f, 0.93f, 0.78f);   // pale petals and stamens
    static readonly Color Withered    = new Color(0.36f, 0.22f, 0.16f);
    static readonly Color SeedTint    = new Color(0.40f, 0.42f, 0.22f);

    // Wing outline as (along, across): a straight, veined outer edge and a rounded trailing edge.
    static readonly Vector2[] WingOutline =
    {
        new Vector2(0f,    0.30f), new Vector2(0.5f,  0.52f), new Vector2(0.9f, 0.40f),
        new Vector2(1f,    0.05f), new Vector2(0.86f, -0.38f), new Vector2(0.45f, -0.45f),
        new Vector2(0f,   -0.12f),
    };

    /// <summary>Vertex lists for one mesh.</summary>
    sealed class Geo
    {
        public readonly List<Vector3> verts   = new List<Vector3>();
        public readonly List<Vector3> normals = new List<Vector3>();
        public readonly List<Color>   colors  = new List<Color>();
        public readonly List<Vector2> flex    = new List<Vector2>();
        public readonly List<int>     tris    = new List<int>();

        public void Clear() { verts.Clear(); normals.Clear(); colors.Clear(); flex.Clear(); tris.Clear(); }

        public void Vert(Vector3 p, Vector3 n, Color c, Vector2 f) { verts.Add(p); normals.Add(n); colors.Add(c); flex.Add(f); }

        public void ApplyTo(Mesh m)
        {
            m.Clear();
            if (verts.Count == 0) return;
            m.SetVertices(verts);
            m.SetNormals(normals);
            m.SetColors(colors);
            m.SetUVs(1, flex);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
        }
    }

    /// <summary>One cluster's calendar, in absolute days (see <see cref="Day"/>).</summary>
    struct Times
    {
        public float emerge, open, fruitStart, fruitFull, ripenStart, ripenEnd, end;
        public int   flowers;
    }

    TreeSkeleton    skeleton;
    TreeMeshBuilder bark;
    Mesh            mesh;
    Material        material;
    TreeNode        trackedRoot;   // a new root means a load, a new tree or a sever

    bool  dirty = true;
    float nextBuild, stateNow = -1f, lastBuildNow = -1f;
    int   liveClusters;

    readonly Geo       geo     = new Geo();
    readonly Geo       scratch = new Geo();
    readonly Vector3[] path    = new Vector3[4];
    readonly Vector3[] seg     = new Vector3[2];
    readonly Dictionary<Color, Mesh> spinnerMeshes = new Dictionary<Color, Mesh>();

    /// <summary>Species whose flowers and fruit this draws; <see cref="FlowerManager"/> skips them.</summary>
    public static bool Handles(TreeSpecies sp) => sp != null && sp.bloomType == BloomType.Corymb;

    static float Day(int year, int doy) => year * 366f + doy;   // same scale as BudVisuals.DayNow
    static float DayNow() => Day(GameManager.year, GameManager.dayOfYear);

    static int Doy(int month, int day, int year)
    {
        month = Mathf.Clamp(month, 1, 12);
        int d = Mathf.Clamp(day, 1, GameManager.DaysInMonth(month, year));
        for (int m = 1; m < month; m++) d += GameManager.DaysInMonth(m, year);
        return d;
    }

    static float MonthStart(int month, int year) => Day(year, Doy(month, 1, year));
    static float MonthEnd(int month, int year)   => Day(year, Doy(month, 31, year));

    static float Smooth01(float x) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(x));

    static float Hash01(int a, int b, int c)
    {
        unchecked
        {
            uint h = (uint)a * 0x9E3779B1u + (uint)b * 0x85EBCA77u + (uint)c * 0xC2B2AE3Du;
            h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
            return (h >> 8) / 16777216f;
        }
    }

    static int CountBits(int x) { int n = 0; while (x != 0) { x &= x - 1; n++; } return n; }

    void Awake()
    {
        skeleton = GetComponent<TreeSkeleton>();
        bark     = GetComponent<TreeMeshBuilder>();

        var go = new GameObject("_Flowers");
        go.transform.SetParent(transform, false);   // tree space, so the sway math matches the bark
        mesh = new Mesh { name = "TreeFlowers", indexFormat = IndexFormat.UInt32 };
        mesh.MarkDynamic();
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = ShadowCastingMode.Off;

        // The bud material, drawn two-sided: sepals and samara wings are single sheets.
        var budMat = Resources.Load<Material>("TreeBudMaterial");
        if (budMat != null)
        {
            material = new Material(budMat) { name = "TreeFlowers" };
            material.SetFloat("_Cull", (float)CullMode.Off);
            mr.sharedMaterial = material;
        }
        else Debug.LogWarning("[Flowers] Resources/TreeBudMaterial is missing; flowers and fruit won't draw.");
    }

    void OnEnable()  { if (bark != null) bark.Rebuilt += MarkDirty; }
    void OnDisable() { if (bark != null) bark.Rebuilt -= MarkDirty; }
    void MarkDirty() => dirty = true;

    void OnDestroy()
    {
        foreach (var m in spinnerMeshes.Values) if (m != null) Destroy(m);
        if (material != null) Destroy(material);
    }

    void LateUpdate()
    {
        if (skeleton == null || skeleton.root == null || !Handles(skeleton.species))
        {
            if (mesh.vertexCount > 0) mesh.Clear();
            trackedRoot = null; stateNow = lastBuildNow = -1f; liveClusters = 0;
            return;
        }
        if (skeleton.root != trackedRoot)
        {
            trackedRoot = skeleton.root;
            stateNow = lastBuildNow = -1f;   // nothing narrated or let go for days a load skipped
            dirty = true;
        }

        // Once an in-game day: spend flower buds and narrate, even while Quick Start hides the mesh,
        // so the tree it hands over is in the right state.
        float now = DayNow();
        if (now != stateNow)
        {
            if (SpendFlowerBuds()) dirty = true;
            if (stateNow >= 0f && !QuickStartManager.Protected) Narrate(stateNow, now);
            stateNow = now;
            if (liveClusters > 0) dirty = true;
        }

        if (QuickStartManager.Protected)
        {
            if (mesh.vertexCount > 0) mesh.Clear();
            lastBuildNow = -1f;
            return;
        }
        if (!dirty || Time.unscaledTime < nextBuild) return;
        nextBuild = Time.unscaledTime + 0.2f;
        dirty = false;
        Rebuild(now);
    }

    // ── State ─────────────────────────────────────────────────────────────────

    /// <summary>Spring to summer: flower buds open with the leaves. Returns whether any did.</summary>
    bool SpendFlowerBuds()
    {
        int month = GameManager.month;
        if (month < 3 || month > 8) return false;

        var sp = skeleton.species;
        int spent = 0, fruiting = 0;
        foreach (var n in skeleton.allNodes)
        {
            if (!n.hasFlowerBud) continue;
            n.hasFlowerBud = false;
            if (n.isRoot || n.isTrimmed || n.isDead || n.isJin) continue;
            n.bloomYear = GameManager.year;
            n.fruitMask = RollFruit(n, sp);
            fruiting   += CountBits(n.fruitMask);
            spent++;
        }
        if (spent > 0)
            Debug.Log($"[Flowers] {sp.speciesName}: {spent} flower buds broke, {fruiting} flowers will set fruit | year={GameManager.year}");
        return spent > 0;
    }

    /// <summary>Which of a new cluster's flowers will set fruit: fewer on a tired tree or twig.</summary>
    int RollFruit(TreeNode n, TreeSpecies sp)
    {
        if (sp.fruitType == FruitType.None) return 0;
        float chance = sp.fruitSetChance * Mathf.Lerp(0.4f, 1f, Mathf.Clamp01(skeleton.treeEnergy))
                       * Mathf.Clamp01(n.health);
        int mask = 0, count = FlowerCount(n, sp);
        for (int i = 0; i < count; i++)
            if (Random.value < chance) mask |= 1 << i;
        return mask;
    }

    static int FlowerCount(TreeNode n, TreeSpecies sp) =>
        Mathf.Clamp(Mathf.RoundToInt(sp.flowersPerCluster * (0.7f + 0.6f * Hash01(n.id, n.bloomYear, 3))), 2, MaxFlowers);

    /// <summary>Care-log beats for this year's flowering, when their day passes in play.</summary>
    void Narrate(float prev, float now)
    {
        var sp = skeleton.species;
        int y  = GameManager.year;
        bool Crossed(float day) => prev < day && day <= now;

        // A typical cluster's flowers are open on `bloom` (see TimesFor) and setting fruit by `set`.
        float bloom = Mathf.Max(MonthStart(3, y) + EmergeDelay + EmergeStagger * 0.5f + EmergeDays * 0.6f,
                                MonthStart(sp.bloomMonth, y) + OpenStagger * 0.5f) + OpenDays;
        float set   = bloom + FlowerStagger + sp.bloomDurationDays;
        float ripe  = MonthStart(sp.fruitRipeMonth, y);
        if (!Crossed(bloom) && !Crossed(set) && !Crossed(ripe)) return;

        int clusters = 0, fruit = 0;
        foreach (var n in skeleton.allNodes)
        {
            if (n.bloomYear != y || n.isTrimmed || n.isDead) continue;
            clusters++;
            fruit += CountBits(n.fruitMask);
        }
        if (clusters == 0) return;

        bool samaras = sp.fruitType == FruitType.Samara;
        if (Crossed(bloom))
        {
            CareLog.Add("Bloom", $"{sp.speciesName} is in flower: {clusters} clusters among the new leaves.");
            ProgressionManager.Instance?.ReachMilestone("first_bloom");
            Debug.Log($"[Flowers] {sp.speciesName} in flower: {clusters} clusters | {GameManager.month}/{GameManager.day}/{y}");
        }
        if (Crossed(set) && fruit > 0)
        {
            CareLog.Add("Fruit", samaras ? $"{sp.speciesName} set {fruit} pairs of samaras."
                                         : $"{sp.speciesName} set {fruit} fruit.");
            ProgressionManager.Instance?.ReachMilestone("first_fruit");
        }
        if (Crossed(ripe) && fruit > 0)
            CareLog.Add("Fruit", samaras ? $"{sp.speciesName}'s samaras are ripening; they'll spin down over the coming weeks."
                                         : $"{sp.speciesName}'s fruit is ripening.");
    }

    Times TimesFor(TreeNode n, TreeSpecies sp)
    {
        int y = n.bloomYear;
        var t = new Times { flowers = FlowerCount(n, sp) };
        t.emerge     = MonthStart(3, y) + EmergeDelay + Hash01(n.id, y, 0) * EmergeStagger;
        t.open       = Mathf.Max(t.emerge + EmergeDays * 0.6f, MonthStart(sp.bloomMonth, y) + Hash01(n.id, y, 1) * OpenStagger);
        t.fruitStart = t.open + OpenDays + sp.bloomDurationDays * 0.7f;
        t.fruitFull  = Mathf.Max(t.fruitStart + 20f, MonthEnd(sp.fruitSetMonth, y));
        t.ripenStart = Mathf.Max(t.fruitFull, MonthStart(sp.fruitRipeMonth, y) - RipenLead);
        t.ripenEnd   = Mathf.Max(t.ripenStart + 10f, MonthEnd(sp.fruitRipeMonth, y));
        t.end        = n.fruitMask != 0
            ? MonthStart(3, y + 1)
            : t.open + FlowerStagger + OpenDays + sp.bloomDurationDays + WitherDays;
        return t;
    }

    /// <summary>When samara <paramref name="side"/> of flower <paramref name="i"/> lets go.</summary>
    static float DropDay(TreeNode n, int i, int side, in Times t)
    {
        float h = Hash01(n.id, n.bloomYear, 64 + i * 2 + side);
        if (h < PersistShare)
            return MonthStart(3, n.bloomYear + 1) - PersistDropDays + h / PersistShare * (PersistDropDays - 1f);
        return t.ripenEnd - DropLead + (h - PersistShare) / (1f - PersistShare) * DropSpread;
    }

    // ── Mesh ──────────────────────────────────────────────────────────────────

    void Rebuild(float now)
    {
        geo.Clear();
        var     sp    = skeleton.species;
        int     year  = GameManager.year;
        Vector3 down  = transform.InverseTransformDirection(Vector3.down).normalized;
        bool    spawn = lastBuildNow >= 0f && now > lastBuildNow;   // let go only what dropped since the last build

        liveClusters = 0;
        foreach (var n in skeleton.allNodes)
        {
            if (n.bloomYear < year - 1 || n.isRoot || n.isTrimmed || n.isDead) continue;
            var t = TimesFor(n, sp);
            if (now > t.end) continue;
            liveClusters++;
            if (now >= t.emerge) AddCluster(n, sp, t, now, down, spawn);
        }
        lastBuildNow = now;
        geo.ApplyTo(mesh);
    }

    /// <summary>Sway data for the twig a cluster hangs from, as the bark carries it at that tip.</summary>
    Vector2 FlexOf(TreeNode n)
    {
        Color c = bark != null ? bark.SwayColorAt(n) : new Color(0f, 0f, 0f, 0f);
        return new Vector2(c.r, c.a);
    }

    /// <summary>Sway data part-way down a hanging cluster: stiffness eases off toward the end.</summary>
    static Vector2 Hang(Vector2 fx, float t) => new Vector2(fx.x, fx.y * (1f - HangFlex * t));

    void AddCluster(TreeNode n, TreeSpecies sp, in Times t, float now, Vector3 down, bool spawn)
    {
        float e = Smooth01((now - t.emerge) / EmergeDays);
        if (e <= 0f) return;
        bool  fruits = n.fruitMask != 0;
        float g      = fruits ? Smooth01((now - t.fruitStart) / Mathf.Max(1f, t.fruitFull - t.fruitStart)) : 0f;
        float ripe   = Smooth01((now - t.ripenStart) / Mathf.Max(1f, t.ripenEnd - t.ripenStart));

        // Peduncle: out of the opening bud along the shoot, drooping as it lengthens and as the
        // fruit gains weight.
        Vector3 axis  = n.growDirection.normalized;
        Vector3 side  = BudVisuals.Side(axis, BudVisuals.BaseAzimuth(n) + 45f);
        Vector3 a     = n.tipPosition + side * (n.radius * 0.5f);
        Vector3 out0  = (axis * 0.55f + side * 0.45f).normalized;
        float   len   = PeduncleLength * Mathf.Lerp(0.15f, 1f, e);
        float   droop = Mathf.Lerp(0.1f, 0.5f, e) + 0.5f * g;   // held out among the leaves; fruit pulls it down
        Vector3 p1    = a + out0 * (len * 0.5f);
        Vector3 p2    = a + out0 * (len * 0.55f) + down * (len * droop);
        Vector2 fx    = FlexOf(n);
        Color   stalk = Color.Lerp(Color.Lerp(sp.bloomColor, sp.youngBarkColor, 0.5f),
                                   Color.Lerp(sp.fruitRipeColor, sp.matureBarkColor, 0.5f), ripe);

        bool any = false;
        for (int i = 0; i < t.flowers; i++)
        {
            float hi     = Hash01(n.id, n.bloomYear, 10 + i);
            bool  fruit  = ((n.fruitMask >> i) & 1) != 0;
            float open0  = t.open + hi * FlowerStagger;
            float o      = Smooth01((now - open0) / OpenDays);
            float w      = Mathf.Clamp01((now - (open0 + OpenDays + sp.bloomDurationDays)) / WitherDays);
            float drop0  = fruit ? DropDay(n, i, 0, t) : 0f;
            float drop1  = fruit ? DropDay(n, i, 1, t) : 0f;
            bool  flower = w < 1f;
            bool  show0  = fruit && g > 0f && now < drop0, show1 = fruit && g > 0f && now < drop1;
            bool  spawn0 = spawn && fruit && lastBuildNow < drop0 && drop0 <= now;
            bool  spawn1 = spawn && fruit && lastBuildNow < drop1 && drop1 <= now;
            if (!flower && !show0 && !show1 && !spawn0 && !spawn1) continue;

            // Pedicel: a short stalk off the peduncle's end; the cluster spreads as it emerges and
            // fruit pulls it straight down.
            float   az     = i * 137.508f + hi * 40f;
            Vector3 radial = BudVisuals.Side(down, az);
            float   spread = Mathf.Lerp(0.3f, 0.8f, e) * Mathf.Lerp(0.8f, 1.1f, hi);
            Vector3 ped    = (down * (1f - 0.5f * spread) + radial * spread).normalized;
            if (fruit) ped = Vector3.Slerp(ped, down, 0.45f * g).normalized;
            float   plen   = PedicelLength * (0.7f + 0.6f * Hash01(n.id, n.bloomYear, 30 + i)) * Mathf.Lerp(0.2f, 1f, e);
            Vector3 f      = p2 + ped * plen;
            Vector2 fxEnd  = Hang(fx, 1f);

            if (flower || show0 || show1)
            {
                any = true;
                seg[0] = p2; seg[1] = f;
                AddTube(geo, seg, 2, PedicelRadius * Mathf.Lerp(0.5f, 1f, e), PedicelRadius * 0.8f, stalk, Hang(fx, 0.7f), fxEnd);
            }
            if (flower)
            {
                float size  = e * (fruit ? Mathf.Lerp(1f, 0.45f, w) : 1f - w);
                float theta = Mathf.Lerp(12f, 75f, o) + 25f * w;   // opens, then folds back as it withers
                Color sepal = Color.Lerp(Color.Lerp(sp.bloomColor * 0.9f, sp.bloomColor, o), Withered, w);
                Color heart = Color.Lerp(sepal, FlowerHeart, 0.8f * o * (1f - w));
                AddFlower(geo, f, ped, theta, SepalLength * size, SepalWidth * size, heart, sepal, fxEnd, az);
            }
            if (show0 || show1 || spawn0 || spawn1)
                AddSamaraPair(sp, f, down, az, g, ripe, show0, show1, spawn0, spawn1, fxEnd);
        }

        if (!any) return;
        for (int k = 0; k < 4; k++)   // the peduncle as four points on the curve a → p1 → p2
        {
            float u = k / 3f;
            path[k] = (1f - u) * (1f - u) * a + 2f * (1f - u) * u * p1 + u * u * p2;
        }
        AddTube(geo, path, 4, PeduncleRadius * Mathf.Lerp(0.5f, 1f, e), PedicelRadius, stalk, fx, Hang(fx, 0.55f));
    }

    /// <summary>A pair of samaras joined at <paramref name="joint"/>, wings spread
    /// <c>samaraSpread</c> apart in one plane, across the pedicel's swing.</summary>
    void AddSamaraPair(TreeSpecies sp, Vector3 joint, Vector3 down, float az, float g, float ripe,
                       bool show0, bool show1, bool spawn0, bool spawn1, Vector2 fx)
    {
        Vector3 h     = BudVisuals.Side(down, az + 90f);
        Vector3 plane = Vector3.Cross(h, down).normalized;
        float   half  = sp.samaraSpread * 0.5f * Mathf.Deg2Rad;
        Color   wing  = Color.Lerp(sp.fruitColor, sp.fruitRipeColor, ripe);
        Color   seed  = Color.Lerp(wing, SeedTint, 0.35f);
        float   sLen  = SeedLength * Mathf.Lerp(0.35f, 1f, g), sWid = SeedWidth * Mathf.Lerp(0.4f, 1f, g);
        float   wLen  = WingLength * Smooth01(g / 0.85f),     wWid = WingWidth * Smooth01((g - 0.1f) / 0.9f);
        for (int k = 0; k < 2; k++)
        {
            float   s     = k == 0 ? -1f : 1f;
            Vector3 dir   = (down * Mathf.Cos(half) + h * (s * Mathf.Sin(half))).normalized;
            Vector3 outer = Vector3.Cross(plane, dir).normalized;
            if (Vector3.Dot(outer, h * s) < 0f) outer = -outer;
            if (k == 0 ? show0 : show1)
            {
                AddSpindle(geo, joint, dir, sLen, sWid, seed, fx);
                if (wLen > 0.0005f && wWid > 0.0002f)
                    AddWing(geo, joint + dir * (sLen * 0.7f), dir, outer, wLen, wWid, wing, fx);
            }
            if (k == 0 ? spawn0 : spawn1) SpawnSpinner(joint, dir, outer, sp);
        }
    }

    // ── Spinning samaras ──────────────────────────────────────────────────────

    void SpawnSpinner(Vector3 joint, Vector3 dir, Vector3 outer, TreeSpecies sp)
    {
        if (material == null || SamaraSpinner.Live >= MaxSpinners) return;
        Vector3    wd   = transform.TransformDirection(dir);
        Vector3    wo   = transform.TransformDirection(outer);
        Quaternion hang = Quaternion.LookRotation(wo, Vector3.Cross(wo, wd));   // local +X along the samara, +Z outer

        var go = new GameObject("_Samara");   // world space, so the menu-tree capture never takes it
        go.transform.SetPositionAndRotation(transform.TransformPoint(joint), hang);
        go.AddComponent<MeshFilter>().sharedMesh = SpinnerMesh(sp);
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial    = material;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        go.AddComponent<SamaraSpinner>().Launch(hang);
    }

    /// <summary>A full-grown ripe samara along +X, wing flat in the XZ plane; shared per colour.</summary>
    Mesh SpinnerMesh(TreeSpecies sp)
    {
        Color wing = sp.fruitRipeColor;
        if (spinnerMeshes.TryGetValue(wing, out var m) && m != null) return m;
        var still = new Vector2(1f, 1f);   // isRoot = 1: the shader's sway leaves it alone
        scratch.Clear();
        AddSpindle(scratch, Vector3.zero, Vector3.right, SeedLength, SeedWidth, Color.Lerp(wing, SeedTint, 0.35f), still);
        AddWing(scratch, Vector3.right * (SeedLength * 0.7f), Vector3.right, Vector3.forward, WingLength, WingWidth, wing, still);
        m = new Mesh { name = "Samara" };
        scratch.ApplyTo(m);
        spinnerMeshes[wing] = m;
        return m;
    }

    // ── Geometry ──────────────────────────────────────────────────────────────

    /// <summary>A thin three-sided stalk through the first <paramref name="count"/> points, tapering
    /// from r0 to r1, its sway easing from fx0 to fx1. Ring frames are carried along the curve so
    /// the stalk never twists.</summary>
    static void AddTube(Geo g, Vector3[] pts, int count, float r0, float r1, Color col, Vector2 fx0, Vector2 fx1)
    {
        const int Sides = 3;
        int     start = g.verts.Count;
        Vector3 u     = Vector3.zero;
        for (int k = 0; k < count; k++)
        {
            Vector3 d = (k < count - 1 ? pts[k + 1] - pts[k] : pts[k] - pts[k - 1]).normalized;
            if (k == 0)
            {
                u = Vector3.Cross(d, Vector3.up);
                if (u.sqrMagnitude < 0.01f) u = Vector3.Cross(d, Vector3.right);
            }
            else u -= d * Vector3.Dot(u, d);
            u.Normalize();
            Vector3 v = Vector3.Cross(d, u);
            float   t = k / (float)(count - 1);
            float   r = Mathf.Lerp(r0, r1, t);
            Vector2 f = Vector2.Lerp(fx0, fx1, t);
            for (int i = 0; i < Sides; i++)
            {
                float   ang    = i * Mathf.PI * 2f / Sides;
                Vector3 radial = u * Mathf.Cos(ang) + v * Mathf.Sin(ang);
                g.Vert(pts[k] + radial * r, radial, col, f);
            }
        }
        for (int k = 0; k < count - 1; k++)
            for (int i = 0; i < Sides; i++)
            {
                int a0 = start + k * Sides + i, a1 = start + k * Sides + (i + 1) % Sides;
                int b0 = a0 + Sides, b1 = a1 + Sides;
                // Clockwise seen from outside (Unity's front face): cross(v1-v0, v2-v0) points outward.
                g.tris.Add(a0); g.tris.Add(a1); g.tris.Add(b0);
                g.tris.Add(a1); g.tris.Add(b1); g.tris.Add(b0);
            }
    }

    /// <summary>A small flower facing along <paramref name="dir"/>: five sepals, closed into a
    /// pointed bud at theta ≈ 12° and spread into a star at 75°, pale at the heart.</summary>
    static void AddFlower(Geo g, Vector3 at, Vector3 dir, float theta, float len, float wid,
                          Color heart, Color sepal, Vector2 fx, float az)
    {
        if (len < 0.0004f) return;
        float c = Mathf.Cos(theta * Mathf.Deg2Rad), s = Mathf.Sin(theta * Mathf.Deg2Rad);
        for (int k = 0; k < 5; k++)
        {
            Vector3 radial = BudVisuals.Side(dir, az + 72f * k);
            Vector3 sd     = dir * c + radial * s;
            Vector3 tan    = Vector3.Cross(dir, radial).normalized;
            Vector3 v1     = at + sd * (0.45f * len) - tan * (0.5f * wid);
            Vector3 v2     = at + sd * len;
            Vector3 v3     = at + sd * (0.45f * len) + tan * (0.5f * wid);
            Vector3 nrm    = Vector3.Cross(v1 - at, v2 - at).normalized;   // matches the winding below
            int     b      = g.verts.Count;
            g.Vert(at, nrm, heart, fx);
            g.Vert(v1, nrm, sepal, fx);
            g.Vert(v2, nrm, sepal, fx);
            g.Vert(v3, nrm, sepal, fx);
            g.tris.Add(b); g.tris.Add(b + 1); g.tris.Add(b + 2);
            g.tris.Add(b); g.tris.Add(b + 2); g.tris.Add(b + 3);
        }
    }

    /// <summary>A small closed spindle along <paramref name="dir"/> (a samara's nutlet).</summary>
    static void AddSpindle(Geo g, Vector3 root, Vector3 dir, float len, float wid, Color col, Vector2 fx)
    {
        const int Sides = 5;
        Vector3 u = Vector3.Cross(dir, Vector3.up);
        if (u.sqrMagnitude < 0.01f) u = Vector3.Cross(dir, Vector3.right);
        u.Normalize();
        Vector3 v = Vector3.Cross(dir, u);

        int start = g.verts.Count;
        g.Vert(root, -dir, col, fx);
        for (int i = 0; i < Sides; i++)
        {
            float   ang    = i * Mathf.PI * 2f / Sides;
            Vector3 radial = u * Mathf.Cos(ang) + v * Mathf.Sin(ang);
            g.Vert(root + dir * (len * 0.45f) + radial * (wid * 0.5f), radial, col, fx);
        }
        g.Vert(root + dir * len, dir, Color.Lerp(col, Color.white, 0.1f), fx);

        int tip = start + Sides + 1;
        for (int i = 0; i < Sides; i++)
        {
            int r0 = start + 1 + i, r1 = start + 1 + (i + 1) % Sides;
            g.tris.Add(start); g.tris.Add(r1); g.tris.Add(r0);   // outward, as AddTube
            g.tris.Add(r0);    g.tris.Add(r1); g.tris.Add(tip);
        }
    }

    /// <summary>A flat samara wing from <paramref name="b"/> along <paramref name="along"/>, its
    /// veined edge toward <paramref name="across"/>: a fan around a centre point.</summary>
    static void AddWing(Geo g, Vector3 b, Vector3 along, Vector3 across, float len, float wid, Color col, Vector2 fx)
    {
        Vector3 P(Vector2 q) => b + along * (q.x * len) + across * (q.y * wid);
        Vector3 centre = b + along * (0.45f * len);
        Vector3 nrm    = Vector3.Cross(P(WingOutline[0]) - centre, P(WingOutline[1]) - centre).normalized;
        Color   vein   = col * 0.8f, thin = Color.Lerp(col, Color.white, 0.15f);

        int c = g.verts.Count;
        g.Vert(centre, nrm, col, fx);
        foreach (var q in WingOutline) g.Vert(P(q), nrm, q.y > 0.2f ? vein : thin, fx);
        int m = WingOutline.Length;
        for (int j = 0; j < m; j++) { g.tris.Add(c); g.tris.Add(c + 1 + j); g.tris.Add(c + 1 + (j + 1) % m); }
    }
}

/// <summary>A ripe samara let go by <see cref="FlowerVisuals"/>: it tips into a helicopter spin,
/// which slows its fall, and drifts down on the wind, then is gone. Real time, like falling leaves.</summary>
public class SamaraSpinner : MonoBehaviour
{
    public static int Live { get; private set; }

    Quaternion hang;
    Vector3    drift;
    float      age, yaw, spin, despawnY;

    void OnEnable()  => Live++;
    void OnDisable() => Live--;

    public void Launch(Quaternion hanging)
    {
        hang     = hanging;
        yaw      = Random.Range(0f, 360f);
        spin     = Random.Range(600f, 950f) * (Random.value < 0.5f ? -1f : 1f);
        despawnY = transform.position.y - 3f;
        drift    = new Vector3(Random.Range(-0.04f, 0.04f), 0f, Random.Range(-0.04f, 0.04f));
        var wind = WindManager.Instance;
        if (wind != null && wind.strength > 0f)
            drift += Vector3.ProjectOnPlane(wind.SampleSway(transform.position, Random.value) * 8f, Vector3.up);
        if (GameSettings.ReducedMotion) { spin = 0f; drift = Vector3.zero; }   // still falls, gently
    }

    void Update()
    {
        float dt = Time.deltaTime;
        age += dt;
        float k = Mathf.SmoothStep(0f, 1f, age / 0.6f);   // tips over into its spin…
        transform.position += (drift * k + Vector3.down * Mathf.Lerp(0.45f, 0.15f, k)) * dt;   // …which slows the fall
        yaw += spin * k * dt;
        // Spinning: wing near level, its tip coned up a little, pitched into the spin.
        Quaternion spinning = Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.Euler(15f, 0f, 12f);
        transform.rotation = Quaternion.Slerp(hang, spinning, k);
        if (age > 12f || transform.position.y < despawnY) Destroy(gameObject);
    }
}
