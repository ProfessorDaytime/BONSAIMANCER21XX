using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Flowers and fruit as one swaying mesh, drawn from what each branch stores plus the date
/// (2026-10-29: blossoms &amp; fruit step 1 the maples, step 2 cherry). Takes species over from
/// <see cref="FlowerManager"/> one group at a time.
///
/// <para>Shared lifecycle: a tip's flower bud (<c>TreeNode.hasFlowerBud</c>, set in autumn by
/// SetBuds) is spent in spring. <c>bloomYear</c> records the year and <c>fruitMask</c> which of its
/// flowers will set fruit. Both are saved, and every stage is rebuilt from them and the calendar,
/// so a load shows the right stage. Fruit grows to full size by the end of fruitSetMonth, ripens
/// around fruitRipeMonth, then lets go one piece at a time and falls (<see cref="FallingPiece"/>).</para>
///
/// <list type="bullet">
/// <item><b>Maples (<see cref="BloomType.Corymb"/>):</b> the cluster comes out of the opening bud
/// with the leaves (Mar 2–5) and blooms while they unfold; the flowers wither, and fertilised ones
/// grow into pairs of winged samaras that spin down in autumn. A few hang on through winter and drop
/// in the weeks before bud break.</item>
/// <item><b>Cherry (<see cref="BloomType.Blossom"/>):</b> plump flower buds on spurs along the twig
/// ends from September, swelling green-tipped in bloomMonth; then pink balloons push out on long
/// stalks and open into five-petalled blossoms, on bare branches when bloomBeforeLeaves. The petals
/// let go one by one and drift down; fertilised flowers swell into cherries that turn gold, then
/// red, and drop.</item>
/// </list>
///
/// Drawn with <c>Custom/TreeBud</c> two-sided (<c>_Cull</c> off: petals, sepals and wings are single
/// sheets), so it sways with the bark's wind math; hanging parts sway more the further they hang.
/// Rebuilt daily while anything is changing and whenever the bark mesh is rebuilt.
/// </summary>
public class FlowerVisuals : MonoBehaviour
{
    // ── Maple calendar (Corymb), in-game days ─────────────────────────────────
    // Maples flower while their leaves unfold (2026-10-29: opening in April put the flowers under a
    // full-grown canopy, and nobody saw them).
    const float EmergeDelay     = 1f;     // after bud break (Mar 1), as the bud scales part
    const float EmergeStagger   = 3f;
    const float EmergeDays      = 8f;
    const float OpenStagger     = 6f;     // clusters open over the first week of bloomMonth…
    const float FlowerStagger   = 3f;     // …the flowers in a cluster over a few days…
    const float OpenDays        = 3f;     // …each taking this long to open
    const float WitherDays      = 6f;

    // ── Cherry calendar (Blossom) ─────────────────────────────────────────────
    const float BudFormDays     = 45f;    // flower buds grow in from Sep 1
    const float BudFormStart    = 0.06f;  // …from this fraction of their winter size
    const float SwellDays       = 10f;    // they swell from the start of bloomMonth…
    const float BalloonStagger  = 4f;     // …then the flowers push out as pink balloons…
    const float BalloonDays     = 5f;     // …on lengthening stalks
    const float PetalDrift      = 2f;     // a flower's petals let go one by one over this many days

    // ── Fruit ─────────────────────────────────────────────────────────────────
    const float RipenLead       = 15f;    // ripening starts this long before fruitRipeMonth
    const float PersistDropDays = 20f;    // fruit that hangs on through winter drops over the last
                                          // weeks before bud break, gone before the tip flowers again

    // ── Sizes (tree units) ────────────────────────────────────────────────────
    const float PeduncleLength = 0.06f,  PeduncleRadius = 0.0014f;
    const float PedicelLength  = 0.026f, PedicelRadius  = 0.0008f;
    const float SepalLength    = 0.02f,  SepalWidth     = 0.012f;    // a maple flower ~2× a bud's length across
    const float SeedLength     = 0.012f, SeedWidth      = 0.0075f;
    const float WingLength     = 0.05f,  WingWidth      = 0.017f;
    const float SpurSpacing    = 0.05f;  // cherry flower spurs, back along the twig from its tip
    const float CherryBudLen   = 0.02f,  CherryBudWidth = 0.014f;    // plump, unlike a pointed leaf bud
    const float CherryStalk    = 0.034f, CherryStalkRadius = 0.0009f;
    const float PetalLength    = 0.026f, PetalWidth     = 0.024f;    // a blossom ~0.05 across
    const float CherryRadius   = 0.014f;
    const float HangFlex       = 0.6f;    // the hanging end sways this much more freely than its twig
    const int   MaxFlowers     = 16;      // fruitMask has a bit per flower
    const int   MaxFalling     = 150;     // samaras, petals and fruit in the air at once
    const int   SpawnPerFrame  = 6;       // let go a few a frame, not a day's worth at once
    const int   MaxPending     = 400;

    static readonly Color FlowerHeart = new Color(1f, 0.93f, 0.78f);    // pale petals and stamens
    static readonly Color Withered    = new Color(0.36f, 0.22f, 0.16f);
    static readonly Color SeedTint    = new Color(0.40f, 0.42f, 0.22f);
    static readonly Color PetalBlush  = new Color(0.93f, 0.45f, 0.62f);   // deeper pink at a petal's base
    static readonly Color Stamens     = new Color(0.98f, 0.86f, 0.52f);
    static readonly Color StalkGreen  = new Color(0.42f, 0.52f, 0.24f);
    static readonly Color Turning     = new Color(0.98f, 0.80f, 0.36f);   // a red fruit turns through gold, not mud

    // Wing outline as (along, across): a straight, veined outer edge and a rounded trailing edge.
    static readonly Vector2[] WingOutline =
    {
        new Vector2(0f,    0.30f), new Vector2(0.5f,  0.52f), new Vector2(0.9f, 0.40f),
        new Vector2(1f,    0.05f), new Vector2(0.86f, -0.38f), new Vector2(0.45f, -0.45f),
        new Vector2(0f,   -0.12f),
    };

    // Cherry petal outline as (along, across) from its base: rounded, widest past halfway, notched.
    static readonly Vector2[] PetalOutline =
    {
        new Vector2(0.30f, -0.30f), new Vector2(0.72f, -0.48f), new Vector2(0.98f, -0.26f),
        new Vector2(0.88f,  0f),
        new Vector2(0.98f,  0.26f), new Vector2(0.72f,  0.48f), new Vector2(0.30f,  0.30f),
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

        /// <summary>A triangle wound to face away from <paramref name="inside"/> (Unity's front face).</summary>
        public void TriOut(int a, int b, int c, Vector3 inside)
        {
            Vector3 n = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
            if (Vector3.Dot(n, verts[a] - inside) < 0f) { int t = b; b = c; c = t; }
            tris.Add(a); tris.Add(b); tris.Add(c);
        }

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

    /// <summary>One flowering twig's calendar, in absolute days (see <see cref="Day"/>).</summary>
    struct Times
    {
        public float swell, emerge, open, fruitStart, fruitFull, ripenStart, ripenEnd, end;
        public int   spurs, flowers;
    }

    /// <summary>How a species' ripe fruit lets go.</summary>
    struct Drop
    {
        public float lead, spread, persist;
        public FallingPiece.Kind kind;
    }

    /// <summary>A piece waiting to fall: released a few a frame (<see cref="SpawnPerFrame"/>).</summary>
    struct Pending
    {
        public Vector3 pos;
        public Quaternion rot;
        public Mesh mesh;
        public FallingPiece.Kind kind;
    }

    TreeSkeleton    skeleton;
    TreeMeshBuilder bark;
    Mesh            mesh;
    Material        material;
    TreeNode        trackedRoot;   // a new root means a load, a new tree or a sever

    bool  dirty = true, budsGrowing;
    float nextBuild, stateNow = -1f, lastBuildNow = -1f, lastBudBuild = -1f;
    int   liveClusters;

    readonly Geo       geo     = new Geo();
    readonly Geo       scratch = new Geo();
    readonly Vector3[] path    = new Vector3[4];
    readonly Vector3[] seg     = new Vector3[2];
    readonly Queue<Pending> pending = new Queue<Pending>();
    readonly Dictionary<(FallingPiece.Kind, Color), Mesh> pieceMeshes = new Dictionary<(FallingPiece.Kind, Color), Mesh>();

    /// <summary>Species whose flowers and fruit this draws; <see cref="FlowerManager"/> skips them.</summary>
    public static bool Handles(TreeSpecies sp) =>
        sp != null && (sp.bloomType == BloomType.Corymb || sp.bloomType == BloomType.Blossom);

    static bool IsBlossom(TreeSpecies sp) => sp.bloomType == BloomType.Blossom;

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

        // The bud material, drawn two-sided: petals, sepals and samara wings are single sheets.
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
        foreach (var m in pieceMeshes.Values) if (m != null) Destroy(m);
        if (material != null) Destroy(material);
    }

    void LateUpdate()
    {
        if (skeleton == null || skeleton.root == null || !Handles(skeleton.species))
        {
            if (mesh.vertexCount > 0) mesh.Clear();
            trackedRoot = null; stateNow = lastBuildNow = lastBudBuild = -1f; liveClusters = 0;
            pending.Clear();
            return;
        }
        if (skeleton.root != trackedRoot)
        {
            trackedRoot = skeleton.root;
            stateNow = lastBuildNow = -1f;   // nothing narrated or let go for days a load skipped
            pending.Clear();
            dirty = true;
        }
        ReleasePending();

        // Once an in-game day: spend flower buds and narrate, even while Quick Start hides the mesh,
        // so the tree it hands over is in the right state.
        float now = DayNow();
        if (now != stateNow)
        {
            if (SpendFlowerBuds()) dirty = true;
            if (stateNow >= 0f && !QuickStartManager.Protected) Narrate(stateNow, now);
            stateNow = now;
            if (liveClusters > 0) dirty = true;
            // Cherry flower buds appear with SetBuds on Sep 1 and grow in slowly: every other day.
            bool budSeason = IsBlossom(skeleton.species) && (GameManager.month == 9 || GameManager.month == 10);
            if ((budsGrowing || budSeason) && now - lastBudBuild >= 2f) { lastBudBuild = now; dirty = true; }
        }

        if (QuickStartManager.Protected)
        {
            if (mesh.vertexCount > 0) mesh.Clear();
            lastBuildNow = -1f;
            pending.Clear();
            return;
        }
        if (!dirty || Time.unscaledTime < nextBuild) return;
        nextBuild = Time.unscaledTime + 0.2f;
        dirty = false;
        Rebuild(now);
    }

    // ── State ─────────────────────────────────────────────────────────────────

    /// <summary>The month flower buds are spent: cherries' swell from bloomMonth, the maples' open
    /// at bud break.</summary>
    static int SpendMonth(TreeSpecies sp) => IsBlossom(sp) ? Mathf.Clamp(sp.bloomMonth, 1, 8) : 3;

    /// <summary>Spring to summer: flower buds open. Returns whether any did.</summary>
    bool SpendFlowerBuds()
    {
        var sp    = skeleton.species;
        int month = GameManager.month;
        if (month < SpendMonth(sp) || month > 8) return false;

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

    /// <summary>Which of a twig's new flowers will set fruit: fewer on a tired tree or twig.</summary>
    int RollFruit(TreeNode n, TreeSpecies sp)
    {
        if (sp.fruitType == FruitType.None) return 0;
        float chance = sp.fruitSetChance * Mathf.Lerp(0.4f, 1f, Mathf.Clamp01(skeleton.treeEnergy))
                       * Mathf.Clamp01(n.health);
        int mask = 0, count = FlowerCount(n, sp, SpurCount(sp));
        for (int i = 0; i < count; i++)
            if (Random.value < chance) mask |= 1 << i;
        return mask;
    }

    static int SpurCount(TreeSpecies sp) => IsBlossom(sp) ? Mathf.Clamp(sp.flowerSpurs, 1, 4) : 1;

    static int FlowerCount(TreeNode n, TreeSpecies sp, int spurs)
    {
        if (!IsBlossom(sp))
            return Mathf.Clamp(Mathf.RoundToInt(sp.flowersPerCluster * (0.7f + 0.6f * Hash01(n.id, n.bloomYear, 3))), 2, MaxFlowers);
        int total = 0;
        for (int k = 0; k < spurs; k++) total += PerSpur(n, sp, k, spurs);
        return total;
    }

    static int PerSpur(TreeNode n, TreeSpecies sp, int k, int spurs) =>
        Mathf.Clamp(Mathf.RoundToInt(sp.flowersPerCluster * (0.7f + 0.6f * Hash01(n.id, n.bloomYear, 40 + k))),
                    1, MaxFlowers / spurs);

    static Drop DropFor(TreeSpecies sp) => sp.fruitType == FruitType.Samara
        ? new Drop { lead = 15f, spread = 50f, persist = 0.2f, kind = FallingPiece.Kind.Spin }
        : new Drop { lead = 10f, spread = 25f, persist = 0f,   kind = FallingPiece.Kind.Drop };

    /// <summary>Care-log beats for this year's flowering, when their day passes in play.</summary>
    void Narrate(float prev, float now)
    {
        var sp = skeleton.species;
        int y  = GameManager.year;
        bool Crossed(float day) => prev < day && day <= now;

        // A typical twig's flowers are open on `bloom` and setting fruit by `set`.
        var   typical = Calendar(sp, y, 0.5f, 0.5f);
        float bloom   = typical.open + OpenDays;
        float set     = bloom + FlowerStagger + sp.bloomDurationDays + (IsBlossom(sp) ? PetalDrift : 0f);
        float ripe    = MonthStart(sp.fruitRipeMonth, y);
        if (!Crossed(bloom) && !Crossed(set) && !Crossed(ripe)) return;

        int twigs = 0, fruit = 0;
        foreach (var n in skeleton.allNodes)
        {
            if (n.bloomYear != y || n.isTrimmed || n.isDead) continue;
            twigs++;
            fruit += CountBits(n.fruitMask);
        }
        if (twigs == 0) return;

        if (Crossed(bloom))
        {
            CareLog.Add("Bloom", IsBlossom(sp)
                ? $"{sp.speciesName} is in full bloom{(sp.bloomBeforeLeaves ? " on bare branches" : "")}."
                : $"{sp.speciesName} is in flower: {twigs} clusters among the new leaves.");
            ProgressionManager.Instance?.ReachMilestone("first_bloom");
            Debug.Log($"[Flowers] {sp.speciesName} in flower: {twigs} flowering twigs | {GameManager.month}/{GameManager.day}/{y}");
        }
        if (Crossed(set) && fruit > 0)
        {
            CareLog.Add("Fruit", $"{sp.speciesName} set {FruitPhrase(sp, fruit)}.");
            ProgressionManager.Instance?.ReachMilestone("first_fruit");
        }
        if (Crossed(ripe) && fruit > 0)
            CareLog.Add("Fruit", sp.fruitType == FruitType.Samara
                ? $"{sp.speciesName}'s samaras are ripening; they'll spin down over the coming weeks."
                : $"{sp.speciesName}'s {FruitNoun(sp)} are ripening.");
    }

    static string FruitNoun(TreeSpecies sp) =>
        !string.IsNullOrEmpty(sp.fruitName) ? sp.fruitName : sp.fruitType == FruitType.Samara ? "samaras" : "fruit";

    static string FruitPhrase(TreeSpecies sp, int count) =>
        sp.fruitType == FruitType.Samara ? $"{count} pairs of samaras" : $"{count} {FruitNoun(sp)}";

    /// <summary>A species' flowering calendar for one year; h0/h1 stagger one twig from the next.</summary>
    static Times Calendar(TreeSpecies sp, int y, float h0, float h1)
    {
        var t = new Times();
        if (IsBlossom(sp))
        {
            t.swell      = MonthStart(sp.bloomMonth, y);
            t.emerge     = t.swell + SwellDays + h0 * BalloonStagger;
            t.open       = t.emerge + BalloonDays * 0.8f + h1 * 2f;
            t.fruitStart = t.open + OpenDays + sp.bloomDurationDays + PetalDrift * 0.5f;   // as the petals fall
        }
        else
        {
            t.swell      = t.emerge = MonthStart(3, y) + EmergeDelay + h0 * EmergeStagger;
            t.open       = Mathf.Max(t.emerge + EmergeDays * 0.6f, MonthStart(sp.bloomMonth, y) + h1 * OpenStagger);
            t.fruitStart = t.open + OpenDays + sp.bloomDurationDays * 0.7f;
        }
        t.fruitFull  = Mathf.Max(t.fruitStart + 20f, MonthEnd(sp.fruitSetMonth, y));
        t.ripenStart = Mathf.Max(t.fruitFull, MonthStart(sp.fruitRipeMonth, y) - RipenLead);
        t.ripenEnd   = Mathf.Max(t.ripenStart + 10f, MonthEnd(sp.fruitRipeMonth, y));
        return t;
    }

    Times TimesFor(TreeNode n, TreeSpecies sp)
    {
        int y = n.bloomYear;
        var t = Calendar(sp, y, Hash01(n.id, y, 0), Hash01(n.id, y, 1));
        t.spurs   = SpurCount(sp);
        t.flowers = FlowerCount(n, sp, t.spurs);
        if (n.fruitMask != 0)
        {
            var drop = DropFor(sp);
            t.end = drop.persist > 0f ? MonthStart(3, y + 1) : t.ripenEnd - drop.lead + drop.spread;
        }
        else
        {
            t.end = t.open + FlowerStagger + OpenDays + sp.bloomDurationDays + WitherDays
                    + (IsBlossom(sp) ? PetalDrift : 0f);
        }
        return t;
    }

    /// <summary>When fruit <paramref name="side"/> of flower <paramref name="i"/> lets go.</summary>
    static float DropDay(TreeNode n, int i, int side, in Times t, in Drop drop)
    {
        float h = Hash01(n.id, n.bloomYear, 64 + i * 2 + side);
        if (h < drop.persist)
            return MonthStart(3, n.bloomYear + 1) - PersistDropDays + h / drop.persist * (PersistDropDays - 1f);
        return t.ripenEnd - drop.lead + (h - drop.persist) / (1f - drop.persist) * drop.spread;
    }

    static Color FruitColour(TreeSpecies sp, float ripe)
    {
        Color a = sp.fruitColor, b = sp.fruitRipeColor;
        if (sp.fruitType != FruitType.Berry || b.r <= b.b) return Color.Lerp(a, b, ripe);
        // A red fruit turns through gold, not the mud a straight green → red blend passes through.
        return ripe < 0.4f ? Color.Lerp(a, Turning, ripe / 0.4f) : Color.Lerp(Turning, b, (ripe - 0.4f) / 0.6f);
    }

    // ── Mesh ──────────────────────────────────────────────────────────────────

    void Rebuild(float now)
    {
        geo.Clear();
        var     sp      = skeleton.species;
        int     year    = GameManager.year;
        Vector3 down    = transform.InverseTransformDirection(Vector3.down).normalized;
        bool    spawn   = lastBuildNow >= 0f && now > lastBuildNow;   // let go only what dropped since the last build
        bool    blossom = IsBlossom(sp);

        // Cherry flower buds grow in from Sep 1 and hold their size through winter.
        float budSize = GameManager.month >= 9
            ? Mathf.Lerp(BudFormStart, 1f, Smooth01((GameManager.dayOfYear - Doy(9, 1, year)) / BudFormDays))
            : 1f;
        budsGrowing = false;

        liveClusters = 0;
        foreach (var n in skeleton.allNodes)
        {
            if (n.isRoot || n.isTrimmed || n.isDead) continue;
            if (blossom && n.hasFlowerBud && !n.isJin)
            {
                AddWinterSpurs(n, sp, budSize);
                budsGrowing |= budSize < 1f;
            }
            if (n.bloomYear < year - 1) continue;
            var t = TimesFor(n, sp);
            if (now > t.end) continue;
            liveClusters++;
            if (now < t.swell) continue;
            if (blossom) AddBlossomTwig(n, sp, t, now, down, spawn);
            else         AddCluster(n, sp, t, now, down, spawn);
        }
        lastBuildNow = now;
        geo.ApplyTo(mesh);
    }

    /// <summary>Sway data for a twig, as the bark carries it at that tip.</summary>
    Vector2 FlexOf(TreeNode n)
    {
        Color c = bark != null ? bark.SwayColorAt(n) : new Color(0f, 0f, 0f, 0f);
        return new Vector2(c.r, c.a);
    }

    /// <summary>Sway data a fraction <paramref name="f"/> along a segment, as the bark blends it.</summary>
    Vector2 FlexAlong(TreeNode s, float f)
    {
        Vector2 tip = FlexOf(s);
        if (s.parent == null || f >= 1f) return tip;
        return new Vector2(tip.x, Mathf.Lerp(FlexOf(s.parent).y, tip.y, f));
    }

    /// <summary>Sway data part-way down a hanging stalk: stiffness eases off toward the end.</summary>
    static Vector2 Hang(Vector2 fx, float t) => new Vector2(fx.x, fx.y * (1f - HangFlex * t));

    // ── Maples ────────────────────────────────────────────────────────────────

    void AddCluster(TreeNode n, TreeSpecies sp, in Times t, float now, Vector3 down, bool spawn)
    {
        float e = Smooth01((now - t.emerge) / EmergeDays);
        if (e <= 0f) return;
        bool  fruits = n.fruitMask != 0;
        float g      = fruits ? Smooth01((now - t.fruitStart) / Mathf.Max(1f, t.fruitFull - t.fruitStart)) : 0f;
        float ripe   = Smooth01((now - t.ripenStart) / Mathf.Max(1f, t.ripenEnd - t.ripenStart));
        var   drop   = DropFor(sp);

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
            float drop0  = fruit ? DropDay(n, i, 0, t, drop) : 0f;
            float drop1  = fruit ? DropDay(n, i, 1, t, drop) : 0f;
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
            if (k == 0 ? spawn0 : spawn1)
                QueuePiece(joint, dir, outer, FallingPiece.Kind.Spin, PieceMesh(FallingPiece.Kind.Spin, sp.fruitRipeColor));
        }
    }

    // ── Cherry ────────────────────────────────────────────────────────────────

    /// <summary>Flower spur <paramref name="k"/> on a cherry twig: the tip, then spurs back along the
    /// twig (into its parents if it's short), each turned round it like alternate buds.</summary>
    static void SpurAt(TreeNode n, int k, out Vector3 pos, out Vector3 dir, out TreeNode on, out float f)
    {
        on = n;
        if (k == 0)
        {
            pos = n.tipPosition;
            dir = n.growDirection.normalized;
            f   = 1f;
            return;
        }
        float back = SpurSpacing * (k + 0.4f * (Hash01(n.id, 0, 50 + k) - 0.5f));
        float len  = Mathf.Max(0.001f, on.length);
        while (back > len && on.parent != null && !on.parent.isRoot)
        {
            back -= len;
            on    = on.parent;
            len   = Mathf.Max(0.001f, on.length);
        }
        f = 1f - Mathf.Clamp01(back / len);
        Vector3 axis = on.growDirection.normalized;
        Vector3 out0 = BudVisuals.Side(axis, Mathf.Repeat((n.id + k) * 137.508f, 360f));
        float   r    = Mathf.Lerp(on.radius, on.tipRadius, f);
        pos = on.worldPosition + axis * (f * on.length) + out0 * (r * 0.9f);
        dir = (out0 * 0.8f + axis * 0.45f).normalized;
    }

    /// <summary>A cherry twig's flower buds through autumn and winter, before they're spent.</summary>
    void AddWinterSpurs(TreeNode n, TreeSpecies sp, float size)
    {
        int spurs = SpurCount(sp);
        for (int k = 0; k < spurs; k++)
        {
            SpurAt(n, k, out var pos, out var dir, out var on, out float f);
            AddSpurBuds(geo, n, k, pos, dir, size, sp.budColor, FlexAlong(on, f));
        }
    }

    /// <summary>A spur's rosette of two or three plump flower buds.</summary>
    static void AddSpurBuds(Geo g, TreeNode n, int k, Vector3 pos, Vector3 dir, float size, Color col, Vector2 fx)
    {
        if (size < 0.02f) return;
        int   count = Hash01(n.id, 1, 60 + k) < 0.5f ? 2 : 3;
        float az0   = Hash01(n.id, 2, 60 + k) * 360f;
        for (int b = 0; b < count; b++)
        {
            Vector3 side = BudVisuals.Side(dir, az0 + b * 360f / count);
            Vector3 bdir = (dir + side * 0.45f).normalized;
            AddSpindle(g, pos + side * (CherryBudWidth * 0.25f * size), bdir, CherryBudLen * size, CherryBudWidth * size, col, fx);
        }
    }

    void AddBlossomTwig(TreeNode n, TreeSpecies sp, in Times t, float now, Vector3 down, bool spawn)
    {
        float swellK = Smooth01((now - t.swell) / Mathf.Max(1f, t.emerge - t.swell));
        float e      = Smooth01((now - t.emerge) / BalloonDays);
        float g      = n.fruitMask != 0 ? Smooth01((now - t.fruitStart) / Mathf.Max(1f, t.fruitFull - t.fruitStart)) : 0f;
        float ripe   = Smooth01((now - t.ripenStart) / Mathf.Max(1f, t.ripenEnd - t.ripenStart));
        var   drop   = DropFor(sp);
        Color budCol = Color.Lerp(sp.budColor, sp.budSwellColor, swellK);
        Color edge   = sp.bloomColor;
        Color blush  = Color.Lerp(sp.bloomColor, PetalBlush, 0.55f);
        Color fruitC = FruitColour(sp, ripe);

        int i = 0;
        for (int k = 0; k < t.spurs; k++)
        {
            SpurAt(n, k, out var pos, out var dir, out var on, out float f);
            Vector2 fx = FlexAlong(on, f);
            // The winter buds swell, then shrink away as the flowers push out of them.
            AddSpurBuds(geo, n, k, pos, dir, (1f + 0.35f * swellK) * (1f - e), budCol, fx);

            int count = PerSpur(n, sp, k, t.spurs);
            for (int j = 0; j < count; j++, i++)
            {
                if (i >= t.flowers) break;
                AddCherryFlower(n, sp, t, now, i, j, count, pos, dir, down, e, g, edge, blush, fruitC, drop, fx, spawn);
            }
        }
    }

    void AddCherryFlower(TreeNode n, TreeSpecies sp, in Times t, float now, int i, int j, int count,
                         Vector3 spur, Vector3 dir, Vector3 down, float e, float g,
                         Color edge, Color blush, Color fruitC, in Drop drop, Vector2 fx, bool spawn)
    {
        if (e <= 0f) return;
        float hi     = Hash01(n.id, n.bloomYear, 10 + i);
        bool  fruit  = ((n.fruitMask >> i) & 1) != 0;
        float open0  = t.open + hi * FlowerStagger;
        float o      = Smooth01((now - open0) / OpenDays);
        float fall   = open0 + OpenDays + sp.bloomDurationDays;   // the petals start to let go
        float gone   = fall + PetalDrift;
        float wither = Mathf.Clamp01((now - gone) / WitherDays);
        float fDrop  = fruit ? DropDay(n, i, 0, t, drop) : 0f;
        bool  fruitOn    = fruit && g > 0f && now < fDrop;
        bool  fruitSpawn = spawn && fruit && lastBuildNow < fDrop && fDrop <= now;
        bool  heartOn    = now < gone + (fruit ? 2f : WitherDays);

        // Stalk: out from the spur, bowing under its length; fruit hangs it straight down.
        float   az     = j * 360f / count + hi * 50f;
        Vector3 radial = BudVisuals.Side(dir, az);
        Vector3 ped    = Vector3.Slerp((dir * 0.6f + radial * 0.8f).normalized, down, 0.25f + (fruit ? 0.55f * g : 0f)).normalized;
        float   plen   = CherryStalk * (0.75f + 0.5f * Hash01(n.id, n.bloomYear, 30 + i)) * Mathf.Lerp(0.15f, 1f, e);
        Vector3 fpos   = spur + ped * plen;
        Vector2 fxEnd  = Hang(fx, 1f);
        if (heartOn || fruitOn)
        {
            seg[0] = spur; seg[1] = fpos;
            AddTube(geo, seg, 2, CherryStalkRadius, CherryStalkRadius * 0.8f, StalkGreen, Hang(fx, 0.5f), fxEnd);
        }

        // Flower: a pink balloon that opens into five notched petals, which let go one by one.
        float theta = Mathf.Lerp(10f, 80f, o);
        float cup   = 0.25f * o;
        float az0   = hi * 360f;
        for (int p = 0; p < 5; p++)
        {
            float pd = fall + Hash01(n.id, n.bloomYear, 100 + i * 5 + p) * PetalDrift;
            if (now < pd)
                AddPetal(geo, fpos, ped, az0 + 72f * p, theta, cup, PetalLength * e, PetalWidth * e, blush, edge, fxEnd);
            else if (spawn && lastBuildNow < pd)
                QueuePetal(fpos, ped, az0 + 72f * p, theta, sp);
        }
        // Heart: stamens while in flower, withering once the petals have gone.
        if (heartOn && o > 0f)
            AddStar(geo, fpos + ped * 0.002f, ped, 0.007f * e * (1f - wither) * Mathf.Clamp01(o * 2f),
                    Color.Lerp(Stamens, Withered, wither), fxEnd, az0);
        if (fruitOn)    AddCherry(geo, fpos, down, CherryRadius * Mathf.Lerp(0.15f, 1f, g), fruitC, fxEnd);
        if (fruitSpawn) QueuePiece(fpos, down, BudVisuals.Side(down, az), FallingPiece.Kind.Drop, PieceMesh(FallingPiece.Kind.Drop, sp.fruitRipeColor));
    }

    // ── Falling pieces ────────────────────────────────────────────────────────

    /// <summary>Queues a piece to fall from tree-space <paramref name="at"/>, its mesh's +X along
    /// <paramref name="along"/> and +Z along <paramref name="across"/>.</summary>
    void QueuePiece(Vector3 at, Vector3 along, Vector3 across, FallingPiece.Kind kind, Mesh pieceMesh)
    {
        if (material == null || pending.Count >= MaxPending) return;
        Vector3 wa = transform.TransformDirection(along), wc = transform.TransformDirection(across);
        wc = (wc - wa * Vector3.Dot(wc, wa)).normalized;
        pending.Enqueue(new Pending
        {
            pos  = transform.TransformPoint(at),
            rot  = Quaternion.LookRotation(wc, Vector3.Cross(wc, wa)),
            mesh = pieceMesh,
            kind = kind,
        });
    }

    void QueuePetal(Vector3 at, Vector3 face, float az, float theta, TreeSpecies sp)
    {
        Vector3 radial = BudVisuals.Side(face, az);
        float   c = Mathf.Cos(theta * Mathf.Deg2Rad), s = Mathf.Sin(theta * Mathf.Deg2Rad);
        Vector3 along  = face * c + radial * s;
        Vector3 across = Vector3.Cross(face, radial).normalized;
        QueuePiece(at + along * (PetalLength * 0.5f), along, across, FallingPiece.Kind.Flutter,
              PieceMesh(FallingPiece.Kind.Flutter, sp.bloomColor));
    }

    void ReleasePending()
    {
        for (int k = 0; k < SpawnPerFrame && pending.Count > 0 && FallingPiece.Live < MaxFalling; k++)
        {
            var p  = pending.Dequeue();
            var go = new GameObject("_Falling");   // world space, so the menu-tree capture never takes it
            go.transform.SetPositionAndRotation(p.pos, p.rot);
            go.AddComponent<MeshFilter>().sharedMesh = p.mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial    = material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            go.AddComponent<FallingPiece>().Launch(p.kind, p.rot);
        }
    }

    /// <summary>The mesh for a falling piece, built once per kind and colour, centred near its
    /// middle with +X along it: a ripe samara, a petal or a cherry. Its sway data is isRoot = 1,
    /// so the shader's wind leaves it alone.</summary>
    Mesh PieceMesh(FallingPiece.Kind kind, Color col)
    {
        if (pieceMeshes.TryGetValue((kind, col), out var m) && m != null) return m;
        var still = new Vector2(1f, 1f);
        scratch.Clear();
        switch (kind)
        {
            case FallingPiece.Kind.Spin:
                AddSpindle(scratch, Vector3.zero, Vector3.right, SeedLength, SeedWidth, Color.Lerp(col, SeedTint, 0.35f), still);
                AddWing(scratch, Vector3.right * (SeedLength * 0.7f), Vector3.right, Vector3.forward, WingLength, WingWidth, col, still);
                break;
            case FallingPiece.Kind.Flutter:
                AddPetalBasis(scratch, Vector3.left * (PetalLength * 0.5f), Vector3.right, Vector3.forward, Vector3.up,
                              0.15f, PetalLength, PetalWidth, Color.Lerp(col, PetalBlush, 0.55f), col, still);
                break;
            default:   // hangs along +X, its stalk back toward -X
                AddCherry(scratch, Vector3.zero, Vector3.right, CherryRadius, col, still);
                seg[0] = Vector3.zero; seg[1] = Vector3.left * (CherryStalk * 0.6f);
                AddTube(scratch, seg, 2, CherryStalkRadius, CherryStalkRadius * 0.8f, StalkGreen, still, still);
                break;
        }
        m = new Mesh { name = "FallingPiece" };
        scratch.ApplyTo(m);
        pieceMeshes[(kind, col)] = m;
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

    /// <summary>A small maple flower facing along <paramref name="dir"/>: five sepals, closed into a
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

    /// <summary>One cherry petal on a flower facing along <paramref name="face"/>: turned
    /// <paramref name="az"/> round it and opened <paramref name="theta"/> away from it.</summary>
    static void AddPetal(Geo g, Vector3 at, Vector3 face, float az, float theta, float cup, float len, float wid,
                         Color baseCol, Color edgeCol, Vector2 fx)
    {
        if (len < 0.0006f) return;
        Vector3 radial = BudVisuals.Side(face, az);
        float   c = Mathf.Cos(theta * Mathf.Deg2Rad), s = Mathf.Sin(theta * Mathf.Deg2Rad);
        AddPetalBasis(g, at, face * c + radial * s, Vector3.Cross(face, radial).normalized, face,
                      cup, len, wid, baseCol, edgeCol, fx);
    }

    /// <summary>A notched petal from <paramref name="at"/> along <paramref name="along"/>, curling
    /// toward <paramref name="up"/> by <paramref name="cup"/>: a fan from its base.</summary>
    static void AddPetalBasis(Geo g, Vector3 at, Vector3 along, Vector3 across, Vector3 up, float cup,
                              float len, float wid, Color baseCol, Color edgeCol, Vector2 fx)
    {
        Vector3 P(Vector2 q) => at + along * (q.x * len) + across * (q.y * wid) + up * (cup * q.x * q.x * len);
        Vector3 nrm   = Vector3.Cross(P(PetalOutline[0]) - at, P(PetalOutline[1]) - at).normalized;
        Color   notch = Color.Lerp(edgeCol, Color.white, 0.3f);
        int     b     = g.verts.Count;
        g.Vert(at, nrm, baseCol, fx);
        for (int k = 0; k < PetalOutline.Length; k++)
            g.Vert(P(PetalOutline[k]), nrm, k == 3 ? notch : edgeCol, fx);
        for (int k = 0; k < PetalOutline.Length - 1; k++) { g.tris.Add(b); g.tris.Add(b + 1 + k); g.tris.Add(b + 2 + k); }
    }

    /// <summary>A flat five-pointed tuft facing <paramref name="face"/> (a blossom's stamens).</summary>
    static void AddStar(Geo g, Vector3 at, Vector3 face, float radius, Color col, Vector2 fx, float az)
    {
        if (radius < 0.0005f) return;
        Vector3 centre = at + face * (radius * 0.4f);
        Vector3 p0     = at + BudVisuals.Side(face, az + 36f) * radius;
        Vector3 p1     = at + BudVisuals.Side(face, az + 108f) * radius;
        Vector3 nrm    = Vector3.Cross(p0 - centre, p1 - centre).normalized;
        int     b      = g.verts.Count;
        g.Vert(centre, nrm, Color.Lerp(col, Color.white, 0.2f), fx);
        for (int k = 0; k < 5; k++) g.Vert(at + BudVisuals.Side(face, az + 72f * k + 36f) * radius, nrm, col, fx);
        for (int k = 0; k < 5; k++) { g.tris.Add(b); g.tris.Add(b + 1 + k); g.tris.Add(b + 1 + (k + 1) % 5); }
    }

    /// <summary>A cherry hanging from <paramref name="top"/>: a little sphere dimpled where the stalk
    /// goes in.</summary>
    static void AddCherry(Geo g, Vector3 top, Vector3 down, float r, Color col, Vector2 fx)
    {
        if (r < 0.0008f) return;
        const int Around = 6;
        Vector3 c = top + down * (r * 0.85f);
        Vector3 u = BudVisuals.Side(down, 0f), v = Vector3.Cross(down, u);
        int start = g.verts.Count;
        g.Vert(c - down * (r * 0.7f), -down, col * 0.85f, fx);   // the dimple
        for (int ring = 0; ring < 3; ring++)
        {
            float a = (50f + 45f * ring) * Mathf.Deg2Rad;   // from the top down
            for (int i = 0; i < Around; i++)
            {
                float   b = (i + (ring % 2) * 0.5f) * Mathf.PI * 2f / Around;
                Vector3 n = -down * Mathf.Cos(a) + (u * Mathf.Cos(b) + v * Mathf.Sin(b)) * Mathf.Sin(a);
                g.Vert(c + n * r, n, ring == 0 ? Color.Lerp(col, Color.white, 0.12f) : col, fx);
            }
        }
        g.Vert(c + down * r, down, col * 0.9f, fx);
        int top0 = start, ring0 = start + 1, bottom = start + 1 + 3 * Around;
        for (int i = 0; i < Around; i++)
        {
            int i1 = (i + 1) % Around;
            g.TriOut(top0, ring0 + i, ring0 + i1, c);
            for (int ring = 0; ring < 2; ring++)
            {
                int a0 = ring0 + ring * Around, b0 = a0 + Around;
                g.TriOut(a0 + i, b0 + i, a0 + i1, c);
                g.TriOut(a0 + i1, b0 + i, b0 + i1, c);
            }
            g.TriOut(bottom, ring0 + 2 * Around + i1, ring0 + 2 * Around + i, c);
        }
    }

    /// <summary>A small closed spindle along <paramref name="dir"/> (a samara's nutlet, a flower bud).</summary>
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

/// <summary>A piece let go by <see cref="FlowerVisuals"/>, then gone once it's well below where it
/// started. Real time, like falling leaves; Reduced motion drops it straight and gently.
/// <list type="bullet">
/// <item><b>Spin:</b> a ripe samara tips into a helicopter spin, which slows its fall, and drifts
/// on the wind.</item>
/// <item><b>Flutter:</b> a petal tumbles and sways down, drifting on the wind.</item>
/// <item><b>Drop:</b> a ripe fruit simply falls.</item>
/// </list></summary>
public class FallingPiece : MonoBehaviour
{
    public enum Kind { Spin, Flutter, Drop }

    public static int Live { get; private set; }

    Kind       kind;
    Quaternion hang;
    Vector3    drift, tumble;
    float      age, yaw, spin, despawnY, phase, fallSpeed;

    void OnEnable()  => Live++;
    void OnDisable() => Live--;

    public void Launch(Kind k, Quaternion start)
    {
        kind     = k;
        hang     = start;
        yaw      = Random.Range(0f, 360f);
        spin     = Random.Range(600f, 950f) * (Random.value < 0.5f ? -1f : 1f);
        tumble   = Random.onUnitSphere;
        phase    = Random.Range(0f, 10f);
        despawnY = transform.position.y - 3f;
        drift    = new Vector3(Random.Range(-0.04f, 0.04f), 0f, Random.Range(-0.04f, 0.04f));
        var wind = WindManager.Instance;
        if (k != Kind.Drop && wind != null && wind.strength > 0f)
            drift += Vector3.ProjectOnPlane(wind.SampleSway(transform.position, Random.value) * (k == Kind.Flutter ? 12f : 8f), Vector3.up);
        if (GameSettings.ReducedMotion) { spin = 0f; drift = Vector3.zero; tumble = Vector3.zero; }   // still falls, gently
    }

    void Update()
    {
        float dt = Time.deltaTime;
        age += dt;
        switch (kind)
        {
            case Kind.Spin:
            {
                float k = Mathf.SmoothStep(0f, 1f, age / 0.6f);   // tips over into its spin…
                transform.position += (drift * k + Vector3.down * Mathf.Lerp(0.45f, 0.15f, k)) * dt;   // …which slows the fall
                yaw += spin * k * dt;
                // Spinning: wing near level, its tip coned up a little, pitched into the spin.
                Quaternion spinning = Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.Euler(15f, 0f, 12f);
                transform.rotation = Quaternion.Slerp(hang, spinning, k);
                break;
            }
            case Kind.Flutter:
            {
                float   k    = Mathf.SmoothStep(0f, 1f, age / 0.4f);
                Vector3 sway = new Vector3(Mathf.Sin(age * 3.1f + phase), 0f, Mathf.Cos(age * 2.3f + phase)) * 0.06f;
                transform.position += ((drift + sway) * k + Vector3.down * Mathf.Lerp(0.2f, 0.09f, k)) * dt;
                if (tumble != Vector3.zero) transform.rotation = Quaternion.AngleAxis(220f * dt, tumble) * transform.rotation;
                break;
            }
            default:
                fallSpeed += 2.2f * dt;
                transform.position += (drift + Vector3.down * fallSpeed) * dt;
                break;
        }
        if (age > 14f || transform.position.y < despawnY) Destroy(gameObject);
    }
}
