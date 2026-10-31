using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Flowers and fruit for every species as one swaying mesh, drawn from what each branch stores plus
/// the date (2026-10-29: blossoms &amp; fruit step 1 the maples, step 2 cherry, step 3 everyone else).
/// Replaces <see cref="FlowerManager"/>.
///
/// <para>Shared lifecycle: a tip's flower bud (<c>TreeNode.hasFlowerBud</c>, set in autumn by
/// SetBuds) is spent in spring. <c>bloomYear</c> records the year and <c>fruitMask</c> which of its
/// flowers will set fruit. Both are saved, and every stage is rebuilt from them and the calendar, so
/// a load shows the right stage. Fruit grows to full size by the end of fruitSetMonth, ripens around
/// fruitRipeMonth, then lets go one piece at a time and falls (<see cref="FallingPiece"/>);
/// <c>fruitPersist</c> of it hangs on through winter and drops before the buds break.</para>
///
/// <para>Each kind has its own look and calendar (FlowerVisuals.Kinds.cs): maple clusters and
/// samaras, cherry blossoms and cherries, elm tufts and round samaras, wisteria racemes and pods,
/// birch and willow catkins, and the fruit of species without showy flowers (figs, juniper berries,
/// cones). All but the maples sit on flower spurs along the twig ends, which show their winter flower
/// buds (or birch's stiff catkins) from September.</para>
///
/// Drawn with <c>Custom/TreeBud</c> two-sided (<c>_Cull</c> off: petals, sepals and wings are single
/// sheets), so it sways with the bark's wind math; hanging parts sway more the further they hang.
/// Rebuilt daily while anything is changing and whenever the bark mesh is rebuilt.
/// </summary>
public partial class FlowerVisuals : MonoBehaviour
{
    // ── Calendars, in-game days ───────────────────────────────────────────────
    // Maples (Corymb) flower while their leaves unfold (2026-10-29: opening in April put the flowers
    // under a full-grown canopy, and nobody saw them).
    const float EmergeDelay     = 1f;     // after bud break (Mar 1), as the bud scales part
    const float EmergeStagger   = 3f;
    const float EmergeDays      = 8f;
    const float OpenStagger     = 6f;     // clusters open over the first week of bloomMonth…
    const float FlowerStagger   = 3f;     // …the flowers in a cluster over a few days…
    const float OpenDays        = 3f;     // …each taking this long to open
    const float WitherDays      = 6f;

    // Spur flowers (cherry, elm): winter buds swell from the start of bloomMonth, then the flowers
    // push out of them on lengthening stalks.
    const float BudFormDays     = 45f;    // winter flower buds grow in from Sep 1…
    const float BudFormStart    = 0.06f;  // …from this fraction of their size
    const float SwellDays       = 10f;
    const float BalloonStagger  = 4f;
    const float BalloonDays     = 5f;
    const float PetalDrift      = 2f;     // a flower's petals let go one by one over this many days

    const float RacemeSwellDays = 5f;     // wisteria: the bud swells, the raceme lengthens…
    const float RacemeGrowDays  = 10f;
    const float RacemeOpenDays  = 8f;     // …and its florets open from the top down
    const float CatkinGrowDays  = 8f;     // catkins lengthen and loosen from bud break
    const float FruitAppearLead = 25f;    // fruit-only species: fruit shows this long before fruitSetMonth

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
    const float SpurSpacing    = 0.05f;  // flower spurs, back along the twig from its tip
    const float SpurBudLength  = 0.02f,  SpurBudWidth   = 0.014f;    // plump, unlike a pointed leaf bud
    const float CherryStalk    = 0.034f, StalkRadius    = 0.0009f;
    const float PetalLength    = 0.026f, PetalWidth     = 0.024f;    // a cherry blossom ~0.05 across
    const float CherryRadius   = 0.014f;                             // berries: × fruitSize
    const float ElmStalk       = 0.012f, ElmSepal       = 0.008f, ElmSamara = 0.016f;
    const float RacemeLength   = 0.16f,  FloretSize     = 0.014f;
    const float PodLength      = 0.11f,  PodWidth       = 0.022f;
    const float CatkinLength   = 0.08f,  CatkinWinter   = 0.032f, CatkinWidth = 0.009f;
    const float ConeLength     = 0.06f,  ConeWidth      = 0.034f;
    const float FigLength      = 0.026f, FigWidth       = 0.024f;
    const float FluffRadius    = 0.007f;
    const float HangFlex       = 0.6f;    // the hanging end sways this much more freely than its twig
    const int   MaxFlowers     = 16;      // fruitMask has a bit per flower
    const int   MaxFalling     = 150;     // pieces in the air at once
    const int   SpawnPerFrame  = 6;       // let go a few a frame, not a day's worth at once
    const int   MaxPending     = 400;

    static readonly Color FlowerHeart = new Color(1f, 0.93f, 0.78f);    // pale petals and stamens
    static readonly Color Withered    = new Color(0.36f, 0.22f, 0.16f);
    static readonly Color SeedTint    = new Color(0.40f, 0.42f, 0.22f);
    static readonly Color PetalBlush  = new Color(0.93f, 0.45f, 0.62f);   // deeper pink at a petal's base
    static readonly Color Stamens     = new Color(0.98f, 0.86f, 0.52f);
    static readonly Color StalkGreen  = new Color(0.42f, 0.52f, 0.24f);
    static readonly Color Turning     = new Color(0.98f, 0.80f, 0.36f);   // a red fruit turns through gold, not mud
    static readonly Color FluffWhite  = new Color(0.97f, 0.96f, 0.92f);

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

        /// <summary>A modelled part (<see cref="FlowerParts.Append"/>); false if it has no models.</summary>
        public bool Model(string id, int key, Vector3 at, Quaternion rot, float size,
                          Color baseCol, Color tipCol, Color accent, Vector2 f) =>
            FlowerParts.Append(id, key, at, rot, size, baseCol, tipCol, accent, f, verts, normals, colors, flex, tris);

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
    }

    /// <summary>One flower (or fruit) on a spur, with its twig's stages, for the per-kind drawers.</summary>
    struct Ctx
    {
        public TreeNode    n;
        public TreeSpecies sp;
        public Times       t;
        public Drop        drop;
        public float       now, e, g, ripe, hi;
        public int         i, j, count;
        public bool        fruit, spawn;
        public Vector3     spur, dir, down;
        public Vector2     fx;
    }

    /// <summary>Shapes a falling piece can take.</summary>
    enum Piece { Samara, Petal, Disc, Fluff, Cherry, Berry, Fig, Cone, Pod, Catkin }

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
    readonly Dictionary<(Piece, Color, TreeSpecies), Mesh> pieceMeshes = new Dictionary<(Piece, Color, TreeSpecies), Mesh>();

    /// <summary>Species whose flowers and fruit this draws: all that flower or fruit. FlowerManager
    /// skips them.</summary>
    public static bool Handles(TreeSpecies sp) =>
        sp != null && (sp.bloomType != BloomType.None || sp.fruitType != FruitType.None);

    /// <summary>Kinds whose flower spurs show winter flower buds (or birch's stiff catkins).</summary>
    static bool HasWinterBuds(TreeSpecies sp) =>
        sp.bloomType == BloomType.Blossom || sp.bloomType == BloomType.Tuft
        || sp.bloomType == BloomType.Raceme || sp.bloomType == BloomType.Catkin;

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

    /// <summary>A part hanging along <paramref name="down"/> (its -Y), turned toward
    /// <paramref name="hint"/>.</summary>
    static Quaternion HangRot(Vector3 down, Vector3 hint)
    {
        Vector3 fwd = Vector3.ProjectOnPlane(hint, down);
        if (fwd.sqrMagnitude < 1e-6f) fwd = BudVisuals.Side(down, 0f);
        return Quaternion.LookRotation(fwd.normalized, -down);
    }

    static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float u) =>
        (1f - u) * (1f - u) * a + 2f * (1f - u) * u * b + u * u * c;

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
            // Petals are thin: they glow when lit from behind and stay bright in shade (2026-10-31).
            material.SetFloat("_Translucency", 0.75f);
            material.SetFloat("_ShadowLift", 0.4f);
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
            // Winter flower buds appear with SetBuds on Sep 1 and grow in slowly: every other day.
            bool budSeason = HasWinterBuds(skeleton.species) && (GameManager.month == 9 || GameManager.month == 10);
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

    /// <summary>The month flower buds are spent: at bud break for the maples and fruit-only species,
    /// otherwise as they start to swell in bloomMonth.</summary>
    static int SpendMonth(TreeSpecies sp) =>
        sp.bloomType == BloomType.Corymb || sp.bloomType == BloomType.None ? 3 : Mathf.Clamp(sp.bloomMonth, 1, 8);

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
            Debug.Log($"[Flowers] {sp.speciesName}: {spent} flower buds broke, {fruiting} will set fruit | year={GameManager.year}");
        return spent > 0;
    }

    /// <summary>Which of a twig's new flowers will set fruit: fewer on a tired tree or twig.</summary>
    int RollFruit(TreeNode n, TreeSpecies sp)
    {
        if (sp.fruitType == FruitType.None) return 0;
        float chance = sp.fruitSetChance * Mathf.Lerp(0.4f, 1f, Mathf.Clamp01(skeleton.treeEnergy))
                       * Mathf.Clamp01(n.health);
        int mask = 0, count = FlowerCount(n, sp, SpurCount(sp), n.bloomYear);
        for (int i = 0; i < count; i++)
            if (Random.value < chance) mask |= 1 << i;
        return mask;
    }

    static int SpurCount(TreeSpecies sp) => sp.bloomType == BloomType.Corymb ? 1 : Mathf.Clamp(sp.flowerSpurs, 1, 4);

    /// <summary>Flowers on a twig in a given bloom year: the bits of its fruitMask.</summary>
    static int FlowerCount(TreeNode n, TreeSpecies sp, int spurs, int year)
    {
        if (sp.bloomType == BloomType.Corymb)
            return Mathf.Clamp(Mathf.RoundToInt(sp.flowersPerCluster * (0.7f + 0.6f * Hash01(n.id, year, 3))), 2, MaxFlowers);
        int total = 0;
        for (int k = 0; k < spurs; k++) total += PerSpur(n, sp, k, spurs, year);
        return total;
    }

    /// <summary>Flowers on spur <paramref name="k"/>: one raceme, or flowersPerCluster give or take 30%.</summary>
    static int PerSpur(TreeNode n, TreeSpecies sp, int k, int spurs, int year)
    {
        if (sp.bloomType == BloomType.Raceme) return 1;
        return Mathf.Clamp(Mathf.RoundToInt(sp.flowersPerCluster * (0.7f + 0.6f * Hash01(n.id, year, 40 + k))),
                           1, MaxFlowers / spurs);
    }

    static Drop DropFor(TreeSpecies sp)
    {
        var d = sp.fruitType switch
        {
            FruitType.Samara => new Drop { lead = 15f, spread = 50f },
            FruitType.Fluff  => new Drop { lead = 5f,  spread = 20f },
            FruitType.Cone   => new Drop { lead = 0f,  spread = 75f },
            FruitType.Pod    => new Drop { lead = 0f,  spread = 75f },
            _                => new Drop { lead = 10f, spread = 25f },   // berries, figs, round samaras
        };
        d.persist = Mathf.Clamp01(sp.fruitPersist);
        return d;
    }

    static FallingPiece.Kind KindOf(Piece p) => p switch
    {
        Piece.Samara => FallingPiece.Kind.Spin,
        Piece.Petal  => FallingPiece.Kind.Flutter,
        Piece.Disc   => FallingPiece.Kind.Flutter,
        Piece.Fluff  => FallingPiece.Kind.Flutter,
        _            => FallingPiece.Kind.Drop,
    };

    /// <summary>Care-log beats for this year's flowering, when their day passes in play.</summary>
    void Narrate(float prev, float now)
    {
        var sp = skeleton.species;
        int y  = GameManager.year;
        bool Crossed(float day) => prev < day && day <= now;

        // A typical twig's flowers are open on `bloom`; its fruit is set by `set`.
        var   typical = Calendar(sp, y, 0.5f, 0.5f);
        bool  flowers = sp.bloomType != BloomType.None;
        float bloom   = typical.open + OpenDays;
        float set     = flowers ? Mathf.Max(typical.fruitStart, bloom + FlowerStagger) + 2f : typical.fruitFull;
        float ripe    = MonthStart(sp.fruitRipeMonth, y);
        if (!(flowers && Crossed(bloom)) && !Crossed(set) && !Crossed(ripe)) return;

        int twigs = 0, fruit = 0;
        foreach (var n in skeleton.allNodes)
        {
            if (n.bloomYear != y || n.isTrimmed || n.isDead) continue;
            twigs++;
            fruit += CountBits(n.fruitMask);
        }
        if (twigs == 0) return;

        if (flowers && Crossed(bloom))
        {
            string where = sp.bloomBeforeLeaves ? " on bare branches" : "";
            CareLog.Add("Bloom", sp.bloomType switch
            {
                BloomType.Corymb  => $"{sp.speciesName} is in flower: {twigs} clusters among the new leaves.",
                BloomType.Blossom => $"{sp.speciesName} is in full bloom{where}.",
                BloomType.Tuft    => $"{sp.speciesName} is in flower: tiny tufts along the twigs{where}.",
                BloomType.Raceme  => $"{sp.speciesName} is in bloom: {twigs * SpurCount(sp)} racemes hanging.",
                _                 => $"{sp.speciesName}'s catkins are out, shedding pollen.",
            });
            ProgressionManager.Instance?.ReachMilestone("first_bloom");
            Debug.Log($"[Flowers] {sp.speciesName} in flower: {twigs} flowering twigs | {GameManager.month}/{GameManager.day}/{y}");
        }
        if (Crossed(set) && fruit > 0)
        {
            CareLog.Add("Fruit", flowers ? $"{sp.speciesName} set {FruitPhrase(sp, fruit)}."
                                         : $"{sp.speciesName} is bearing {FruitPhrase(sp, fruit)}.");
            ProgressionManager.Instance?.ReachMilestone("first_fruit");
        }
        if (Crossed(ripe) && fruit > 0)
            CareLog.Add("Fruit", sp.fruitType switch
            {
                FruitType.Samara      => $"{sp.speciesName}'s samaras are ripening; they'll spin down over the coming weeks.",
                FruitType.RoundSamara => $"{sp.speciesName}'s samaras are ripening; they'll drift down soon.",
                FruitType.Fluff       => $"{sp.speciesName}'s catkins are bursting into fluff.",
                _                     => $"{sp.speciesName}'s {FruitNoun(sp)} are ripening.",
            });
    }

    static string FruitNoun(TreeSpecies sp)
    {
        if (!string.IsNullOrEmpty(sp.fruitName)) return sp.fruitName;
        return sp.fruitType switch
        {
            FruitType.Samara or FruitType.RoundSamara => "samaras",
            FruitType.Cone  => "cones",
            FruitType.Fig   => "figs",
            FruitType.Berry => "berries",
            FruitType.Pod   => "pods",
            FruitType.Fluff => "seed catkins",
            _               => "fruit",
        };
    }

    static string FruitPhrase(TreeSpecies sp, int count) =>
        sp.fruitType == FruitType.Samara ? $"{count} pairs of samaras" : $"{count} {FruitNoun(sp)}";

    /// <summary>A species' flowering calendar for one year; h0/h1 stagger one twig from the next.</summary>
    static Times Calendar(TreeSpecies sp, int y, float h0, float h1)
    {
        var t = new Times();
        switch (sp.bloomType)
        {
            case BloomType.Corymb:
                t.swell      = t.emerge = MonthStart(3, y) + EmergeDelay + h0 * EmergeStagger;
                t.open       = Mathf.Max(t.emerge + EmergeDays * 0.6f, MonthStart(sp.bloomMonth, y) + h1 * OpenStagger);
                t.fruitStart = t.open + OpenDays + sp.bloomDurationDays * 0.7f;
                break;
            case BloomType.Blossom:
            case BloomType.Tuft:
                t.swell      = MonthStart(sp.bloomMonth, y);
                t.emerge     = t.swell + SwellDays + h0 * BalloonStagger;
                t.open       = t.emerge + BalloonDays * 0.8f + h1 * 2f;
                t.fruitStart = t.open + OpenDays + sp.bloomDurationDays + PetalDrift * 0.5f;   // as the petals fall
                break;
            case BloomType.Raceme:
                t.swell      = MonthStart(sp.bloomMonth, y);
                t.emerge     = t.swell + RacemeSwellDays + h0 * 4f;
                t.open       = t.emerge + RacemeGrowDays * 0.6f + h1 * 3f;
                t.fruitStart = t.open + RacemeOpenDays + OpenDays + sp.bloomDurationDays;
                break;
            case BloomType.Catkin:
                t.swell      = MonthStart(sp.bloomMonth, y);
                t.emerge     = t.swell + h0 * 4f;
                t.open       = t.emerge + CatkinGrowDays * 0.7f + h1 * 2f;   // shedding pollen
                t.fruitStart = t.open + OpenDays;                            // seed catkins fill at once
                break;
            default:   // fruit only: the fruit simply appears and grows
                t.swell      = t.emerge = t.open = t.fruitStart
                             = MonthStart(sp.fruitSetMonth, y) - FruitAppearLead + h0 * 10f;
                break;
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
        t.flowers = FlowerCount(n, sp, t.spurs, y);
        if (n.fruitMask != 0)
        {
            var drop = DropFor(sp);
            t.end = drop.persist > 0f ? MonthStart(3, y + 1) : t.ripenEnd - drop.lead + drop.spread;
        }
        else
        {
            float flowersGone = sp.bloomType switch
            {
                BloomType.Raceme => t.open + RacemeOpenDays + OpenDays + sp.bloomDurationDays + 3f,
                BloomType.Catkin => t.open + FlowerStagger + OpenDays + sp.bloomDurationDays,
                BloomType.None   => t.open,
                _                => t.open + FlowerStagger + OpenDays + sp.bloomDurationDays + PetalDrift,
            };
            t.end = flowersGone + WitherDays;
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

    /// <summary>Whether fruit <paramref name="side"/> of c's flower is still on the tree, and whether
    /// it let go since the last build.</summary>
    bool FruitOn(in Ctx c, int side, out bool letGo)
    {
        float d = DropDay(c.n, c.i, side, c.t, c.drop);
        letGo = c.spawn && c.fruit && lastBuildNow < d && d <= c.now;
        return c.fruit && c.g > 0f && c.now < d;
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
        var     sp     = skeleton.species;
        int     year   = GameManager.year;
        Vector3 down   = transform.InverseTransformDirection(Vector3.down).normalized;
        bool    spawn  = lastBuildNow >= 0f && now > lastBuildNow;   // let go only what dropped since the last build
        bool    winter = HasWinterBuds(sp);

        // Winter flower buds grow in from Sep 1 and hold their size; they open in the next bloom year.
        float budSize = GameManager.month >= 9
            ? Mathf.Lerp(BudFormStart, 1f, Smooth01((GameManager.dayOfYear - Doy(9, 1, year)) / BudFormDays))
            : 1f;
        int nextBloom = GameManager.month >= 9 ? year + 1 : year;
        budsGrowing = false;

        liveClusters = 0;
        foreach (var n in skeleton.allNodes)
        {
            if (n.isRoot || n.isTrimmed || n.isDead) continue;
            if (winter && n.hasFlowerBud && !n.isJin)
            {
                AddWinterSpurs(n, sp, budSize, nextBloom, down);
                budsGrowing |= budSize < 1f;
            }
            if (n.bloomYear < year - 1) continue;
            var t = TimesFor(n, sp);
            if (now > t.end) continue;
            liveClusters++;
            if (now < t.swell) continue;
            if (sp.bloomType == BloomType.Corymb) AddCluster(n, sp, t, now, down, spawn);
            else                                  AddSpurTwig(n, sp, t, now, down, spawn);
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

    /// <summary>Flower spur <paramref name="k"/> on a flowering twig: the tip, then spurs back along
    /// the twig (into its parents if it's short), each turned round it like alternate buds.</summary>
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

    // ── Falling pieces ────────────────────────────────────────────────────────

    /// <summary>Queues a piece to fall from tree-space <paramref name="at"/>, its mesh's +X along
    /// <paramref name="along"/> and +Z along <paramref name="across"/>.</summary>
    void QueuePiece(Vector3 at, Vector3 along, Vector3 across, Piece piece, Color col, TreeSpecies sp)
    {
        if (material == null || pending.Count >= MaxPending) return;
        Vector3 wa = transform.TransformDirection(along).normalized, wc = transform.TransformDirection(across);
        wc -= wa * Vector3.Dot(wc, wa);
        if (wc.sqrMagnitude < 1e-6f) wc = BudVisuals.Side(wa, 0f);
        wc.Normalize();
        pending.Enqueue(new Pending
        {
            pos  = transform.TransformPoint(at),
            rot  = Quaternion.LookRotation(wc, Vector3.Cross(wc, wa)),
            mesh = PieceMesh(piece, col, sp),
            kind = KindOf(piece),
        });
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

    /// <summary>The mesh for a falling piece, built once per shape, colour and species, along +X
    /// (+Z across). Its sway data is isRoot = 1, so the shader's wind leaves it alone.</summary>
    Mesh PieceMesh(Piece piece, Color col, TreeSpecies sp)
    {
        if (pieceMeshes.TryGetValue((piece, col, sp), out var m) && m != null) return m;
        var   still = new Vector2(1f, 1f);
        float size  = Mathf.Max(0.1f, sp.fruitSize);
        scratch.Clear();
        switch (piece)
        {
            // Modelled parts first: each guard adds the model, or fails through to the code shape.
            case Piece.Samara when scratch.Model("part_maple_samara", 0, Vector3.zero, Quaternion.LookRotation(Vector3.right, Vector3.down),
                                                 SeedLength * 0.7f + WingLength, col * 0.8f, Color.Lerp(col, Color.white, 0.15f),
                                                 Color.Lerp(col, SeedTint, 0.35f), still):
                break;
            case Piece.Petal when sp.bloomType == BloomType.Blossom
                                  && scratch.Model("part_cherry_petal", 0, Vector3.left * (PetalLength * 0.5f),
                                                   Quaternion.LookRotation(Vector3.right, Vector3.up), PetalLength,
                                                   Color.Lerp(col, PetalBlush, 0.55f), col, col, still):
                break;
            case Piece.Cherry when scratch.Model("part_cherry_fruit", 0, Vector3.zero, Quaternion.LookRotation(Vector3.forward, Vector3.left),
                                                 CherryRadius * 1.85f, Color.Lerp(col, Color.white, 0.15f), col * 0.9f, StalkGreen, still):
                seg[0] = Vector3.zero; seg[1] = Vector3.left * (CherryStalk * 0.6f);
                AddTube(scratch, seg, 2, StalkRadius, StalkRadius * 0.8f, StalkGreen, still, still);
                break;
            case Piece.Fig when scratch.Model("part_fig", 0, Vector3.zero, Quaternion.LookRotation(Vector3.right, Vector3.up),
                                              FigLength * size, col, col, col, still):
                break;
            case Piece.Samara:
                AddSpindle(scratch, Vector3.zero, Vector3.right, SeedLength, SeedWidth, Color.Lerp(col, SeedTint, 0.35f), still);
                AddWing(scratch, Vector3.right * (SeedLength * 0.7f), Vector3.right, Vector3.forward, WingLength, WingWidth, col, still);
                break;
            case Piece.Petal:
                AddPetalBasis(scratch, Vector3.left * (PetalLength * 0.5f), Vector3.right, Vector3.forward, Vector3.up,
                              0.15f, PetalLength, PetalWidth, Color.Lerp(col, PetalBlush, 0.55f), col, still);
                break;
            case Piece.Disc:
                AddDisc(scratch, Vector3.zero, Vector3.up, Vector3.right, ElmSamara * size, col, Color.Lerp(col, SeedTint, 0.5f), still);
                break;
            case Piece.Fluff:
                AddBall(scratch, Vector3.zero, FluffRadius, FluffWhite, still);
                break;
            case Piece.Cherry:   // hangs along +X, its stalk back toward -X
                AddBerry(scratch, Vector3.zero, Vector3.right, CherryRadius, col, still);
                seg[0] = Vector3.zero; seg[1] = Vector3.left * (CherryStalk * 0.6f);
                AddTube(scratch, seg, 2, StalkRadius, StalkRadius * 0.8f, StalkGreen, still, still);
                break;
            case Piece.Berry:
                AddBerry(scratch, Vector3.zero, Vector3.right, CherryRadius * size, col, still);
                break;
            case Piece.Fig:
                AddLathe(scratch, Vector3.zero, Vector3.right, FigLength * size, FigWidth * size, PearT, PearR, 6, col, col, still, still);
                break;
            case Piece.Cone:
                AddCone(scratch, Vector3.zero, Vector3.right, ConeLength * size, ConeWidth * size, sp.coneShape, 1f, col, still);
                break;
            case Piece.Pod:
                AddLathe(scratch, Vector3.zero, Vector3.right, PodLength * size, PodWidth * size, PodT, PodR, 6, col, col * 0.92f, still, still, 0.35f);
                break;
            default:   // Catkin
                AddLathe(scratch, Vector3.zero, Vector3.right, CatkinLength, CatkinWidth, CatkinT, CatkinR, 6, col, col * 0.85f, still, still);
                break;
        }
        m = new Mesh { name = "FallingPiece" };
        scratch.ApplyTo(m);
        pieceMeshes[(piece, col, sp)] = m;
        return m;
    }
}

/// <summary>A piece let go by <see cref="FlowerVisuals"/>, then gone once it's well below where it
/// started. Real time, like falling leaves; Reduced motion drops it straight and gently.
/// <list type="bullet">
/// <item><b>Spin:</b> a ripe samara tips into a helicopter spin, which slows its fall, and drifts
/// on the wind.</item>
/// <item><b>Flutter:</b> a petal, a round samara or a tuft of seed fluff tumbles and sways down,
/// drifting on the wind.</item>
/// <item><b>Drop:</b> fruit, a cone or a spent catkin simply falls.</item>
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
