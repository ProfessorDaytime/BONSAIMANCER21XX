using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedural flowering AND fruiting — parallel to LeafManager, riding the same bud cycle. Repro
/// buds set in autumn (TreeSkeleton.SetBuds for any species with a bloomType or fruitType). Each
/// year: at bloomMonth showy flowers open (Blossom / Raceme / Catkin) and drop their petals after
/// bloomDurationDays; at fruitSetMonth fruit sets (green) at the same sites, recolours to ripe at
/// fruitRipeMonth, then drops. Fruit-only species (figs, cones, berries) skip the bloom. All
/// geometry + materials are code-generated — no art. Attach to the bonsai GameObject.
/// </summary>
public class FlowerManager : MonoBehaviour
{
    [SerializeField] TreeSkeleton skeleton;
    [Tooltip("Optional material to clone for blossoms/fruit; otherwise a per-colour Lit material is made.")]
    [SerializeField] Material flowerMaterialOverride;
    [SerializeField] float blossomScale   = 0.06f;
    [SerializeField] int   blossomsPerBud  = 3;
    [SerializeField] float fruitScale      = 0.05f;

    readonly List<GameObject>      blossoms = new List<GameObject>();
    readonly List<GameObject>      fruits   = new List<GameObject>();
    readonly Dictionary<int, Mesh>     meshCache = new Dictionary<int, Mesh>();
    readonly Dictionary<int, Material> matCache  = new Dictionary<int, Material>();

    int   lastBloomYear = -1, lastFruitYear = -1, lastRipeYear = -1;
    float bloomDay, dayAccum;
    bool  bloomed;

    void Awake()
    {
        if (skeleton == null) skeleton = GetComponent<TreeSkeleton>();
        if (skeleton == null) skeleton = FindAnyObjectByType<TreeSkeleton>();
    }

    void OnEnable()  { GameManager.OnMonthChanged += OnMonth; }
    void OnDisable() { GameManager.OnMonthChanged -= OnMonth; }

    void OnMonth(int month)
    {
        var sp = skeleton != null ? skeleton.species : null;
        if (sp == null) return;

        if (sp.bloomType != BloomType.None && month == sp.bloomMonth && GameManager.year != lastBloomYear)
            Bloom(sp);

        if (sp.fruitType != FruitType.None)
        {
            if (month == sp.fruitSetMonth  && GameManager.year != lastFruitYear) SetFruit(sp);
            if (month == sp.fruitRipeMonth && GameManager.year != lastRipeYear)  RipenFruit(sp);
        }
    }

    void Update()
    {
        dayAccum += Time.deltaTime * GameManager.TIMESCALE / 24f;

        if (bloomed && skeleton != null && skeleton.species != null &&
            dayAccum - bloomDay >= skeleton.species.bloomDurationDays)
        {
            foreach (var b in blossoms) if (b != null) b.GetComponent<Flower>()?.FallNow();
            bloomed = false;
        }

        blossoms.RemoveAll(b => b == null);
        fruits.RemoveAll(f => f == null);
    }

    // ── Flowers ──────────────────────────────────────────────────────────────
    void Bloom(TreeSpecies sp)
    {
        lastBloomYear = GameManager.year;
        ClearList(blossoms);

        Mesh mesh = BloomMesh(sp.bloomType);
        Material mat = MatFor(sp.bloomColor);
        bool hang   = sp.bloomType == BloomType.Raceme || sp.bloomType == BloomType.Catkin;
        bool faceOut = sp.bloomType == BloomType.Blossom;

        int clusters = 0;
        foreach (var node in skeleton.allNodes)
        {
            if (node == null || node.isRoot || node.isTrimmed || !node.hasFlowerBud) continue;
            int n = sp.bloomType == BloomType.Blossom ? Mathf.Max(1, blossomsPerBud) : 1;
            for (int i = 0; i < n; i++)
                blossoms.Add(SpawnProp(node, mesh, mat, blossomScale * Random.Range(0.8f, 1.2f), hang, faceOut));
            if (sp.fruitType == FruitType.None) node.hasFlowerBud = false;   // nothing fruits here, consume now
            clusters++;
        }

        if (clusters > 0)
        {
            bloomDay = dayAccum;
            bloomed  = true;
            CareLog.Add("Bloom", $"{sp.speciesName} burst into bloom — {clusters} flowering clusters.");
            ProgressionManager.Instance?.ReachMilestone("first_bloom");
            Debug.Log($"[Flowers] {sp.speciesName} bloom — {clusters} clusters, {blossoms.Count} flowers | year={GameManager.year}");
        }
    }

    // ── Fruit ────────────────────────────────────────────────────────────────
    void SetFruit(TreeSpecies sp)
    {
        lastFruitYear = GameManager.year;
        ClearList(fruits);

        Mesh mesh = FruitMesh(sp.fruitType);
        Material mat = MatFor(sp.fruitColor);   // unripe (green)

        int n = 0;
        foreach (var node in skeleton.allNodes)
        {
            if (node == null || node.isRoot || node.isTrimmed || !node.hasFlowerBud) continue;
            fruits.Add(SpawnProp(node, mesh, mat, fruitScale * Random.Range(0.85f, 1.15f), true, false));
            node.hasFlowerBud = false;   // repro cycle for this node done
            n++;
        }
        if (n > 0)
        {
            CareLog.Add("Fruit", $"{sp.speciesName} set {n} fruit.");
            ProgressionManager.Instance?.ReachMilestone("first_fruit");
            Debug.Log($"[Flowers] {sp.speciesName} set {n} fruit | year={GameManager.year}");
        }
    }

    void RipenFruit(TreeSpecies sp)
    {
        lastRipeYear = GameManager.year;
        Material ripe = MatFor(sp.fruitRipeColor);
        foreach (var f in fruits)
        {
            if (f == null) continue;
            var mr = f.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = ripe;
            f.GetComponent<Flower>()?.FallAfter(Random.Range(6f, 20f));   // ripe fruit drops over the next weeks
        }
        if (fruits.Count > 0) CareLog.Add("Fruit", $"{sp.speciesName} fruit is ripe.");
    }

    // ── Spawning ─────────────────────────────────────────────────────────────
    GameObject SpawnProp(TreeNode node, Mesh mesh, Material mat, float scale, bool hang, bool faceOut)
    {
        Vector3 dir  = skeleton.transform.TransformDirection(node.growDirection).normalized;
        Vector3 perp = Vector3.Cross(dir, Vector3.up);
        if (perp.sqrMagnitude < 0.01f) perp = Vector3.Cross(dir, Vector3.right);
        perp.Normalize();

        float   back   = Random.Range(0f, 0.7f);
        Vector3 pos    = node.tipPosition - node.growDirection * (back * Mathf.Min(node.length, node.targetLength));
        Vector3 outDir = Quaternion.AngleAxis(Random.Range(0f, 360f), dir) * perp;
        pos += outDir * (node.radius + Random.Range(0f, scale));
        if (hang) pos += Vector3.down * (scale * 0.6f);

        var go = new GameObject(faceOut ? "Blossom" : "FlowerProp");
        go.transform.SetParent(skeleton.transform, false);
        go.transform.localPosition = pos;
        if (faceOut)
            go.transform.localRotation = Quaternion.LookRotation(outDir, Vector3.up)
                                       * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
        else
            go.transform.rotation = Quaternion.identity;   // world: the mesh's -Y hangs down
        go.transform.localScale = Vector3.zero;

        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial    = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        go.AddComponent<Flower>().Init(scale);
        return go;
    }

    void ClearList(List<GameObject> list)
    {
        foreach (var g in list) if (g != null) Destroy(g);
        list.Clear();
    }

    // ── Mesh + material caches ───────────────────────────────────────────────
    Mesh BloomMesh(BloomType t) => GetMesh(100 + (int)t, () =>
        t switch
        {
            BloomType.Raceme => BuildRaceme(),
            BloomType.Catkin => BuildOcta(new Vector3(0.30f, 1.7f, 0.30f), Vector3.down * 0.5f),
            _                => BuildBlossom(5),
        });

    Mesh FruitMesh(FruitType t) => GetMesh(200 + (int)t, () =>
        t switch
        {
            FruitType.Cone   => BuildOcta(new Vector3(0.55f, 1.35f, 0.55f), Vector3.zero),
            FruitType.Samara => BuildOcta(new Vector3(1.5f,  0.5f,  0.08f), Vector3.zero),
            _                => BuildOcta(Vector3.one, Vector3.zero),   // Berry / Fig / Pome — round
        });

    Mesh GetMesh(int key, System.Func<Mesh> build)
    {
        if (!meshCache.TryGetValue(key, out var m)) { m = build(); meshCache[key] = m; }
        return m;
    }

    Material MatFor(Color c)
    {
        int key = c.GetHashCode();
        if (!matCache.TryGetValue(key, out var m)) { m = MakeMat(c); matCache[key] = m; }
        return m;
    }

    Material MakeMat(Color c)
    {
        Material m;
        if (flowerMaterialOverride != null) m = new Material(flowerMaterialOverride);
        else
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Standard")
                     ?? Shader.Find("Sprites/Default");
            m = new Material(sh);
        }
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color"))     m.SetColor("_Color",     c);
        if (m.HasProperty("_Cull"))      m.SetFloat("_Cull", 0f);
        return m;
    }

    // ── Procedural geometry ──────────────────────────────────────────────────
    // Showy blossom facing +Z: `petals` rounded petals that CUP upward (a shallow bowl) around a
    // lower central throat, so they catch light and read as a real flower from many angles.
    // Each petal is a fan from the centre across a 7-point rounded rim (CCW = upward normals).
    static Mesh BuildBlossom(int petals)
    {
        var verts = new List<Vector3> { new Vector3(0f, 0f, 0f) };   // 0 = central throat (lowest)
        var tris  = new List<int>();

        const float baseR = 0.12f, midR = 0.34f, tipR = 0.52f;   // petal radii: attach → widest → tip
        const float zBase = 0.02f, zMid = 0.05f, zTip = 0.09f;   // cup: petals lift toward the tip

        for (int p = 0; p < petals; p++)
        {
            float a  = (p + 0.5f) / petals * Mathf.PI * 2f;
            float hw = Mathf.PI / petals * 0.95f;                // half-width — petals nearly meet

            Vector3 P(float r, float off, float z) =>
                new Vector3(Mathf.Cos(a + off) * r, Mathf.Sin(a + off) * r, z);

            int b = verts.Count;
            // Rounded petal rim, CCW from the −side to the +side.
            verts.Add(P(baseR,        -hw * 0.5f, zBase));   // b+0  base −
            verts.Add(P(midR,         -hw,        zMid));    // b+1  widest −
            verts.Add(P(tipR,         -hw * 0.3f, zTip));    // b+2  shoulder −
            verts.Add(P(tipR * 1.04f,  0f,        zTip));    // b+3  tip
            verts.Add(P(tipR,         +hw * 0.3f, zTip));    // b+4  shoulder +
            verts.Add(P(midR,         +hw,        zMid));    // b+5  widest +
            verts.Add(P(baseR,        +hw * 0.5f, zBase));   // b+6  base +

            for (int i = 0; i < 6; i++)
            {
                tris.Add(0); tris.Add(b + i); tris.Add(b + i + 1);   // fan from the throat
            }
        }
        return Finish(verts, tris, "Blossom");
    }

    // Octahedron (outward winding verified) scaled + offset — round berry, elongated cone/catkin, flat samara.
    static readonly Vector3[] OctaV =
        { new(1,0,0), new(-1,0,0), new(0,1,0), new(0,-1,0), new(0,0,1), new(0,0,-1) };
    static readonly int[] OctaT =
        { 2,4,0, 2,1,4, 2,5,1, 2,0,5, 3,0,4, 3,4,1, 3,1,5, 3,5,0 };

    static Mesh BuildOcta(Vector3 scale, Vector3 offset)
    {
        var verts = new Vector3[6];
        for (int i = 0; i < 6; i++) verts[i] = Vector3.Scale(OctaV[i] * 0.5f, scale) + offset;
        var m = new Mesh { name = "Octa" };
        m.vertices = verts; m.triangles = OctaT;
        m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    // Hanging raceme (wisteria) — a tapered vertical stack of small octahedra, drooping along -Y.
    static Mesh BuildRaceme()
    {
        var verts = new List<Vector3>();
        var tris  = new List<int>();
        const int beads = 4;
        var rng = new System.Random(12345);
        for (int i = 0; i < beads; i++)
        {
            float y = -0.32f * i;
            float s = 0.5f * (1f - 0.16f * i);
            float jx = (float)(rng.NextDouble() - 0.5) * 0.1f;
            float jz = (float)(rng.NextDouble() - 0.5) * 0.1f;
            int b = verts.Count;
            for (int v = 0; v < 6; v++)
                verts.Add(Vector3.Scale(OctaV[v] * 0.5f, new Vector3(s, s, s)) + new Vector3(jx, y, jz));
            foreach (int t in OctaT) tris.Add(b + t);
        }
        return Finish(verts, tris, "Raceme");
    }

    static Mesh Finish(List<Vector3> verts, List<int> tris, string name)
    {
        var m = new Mesh { name = name };
        m.SetVertices(verts); m.SetTriangles(tris, 0);
        m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }
}

/// <summary>One procedural blossom or fruit: blooms/grows in (scale 0→1), holds, then on FallNow /
/// FallAfter drifts down, spins, shrinks, and self-destroys. Driven by in-game days, so it keeps
/// pace at any timescale.</summary>
public class Flower : MonoBehaviour
{
    float   maxScale = 0.06f, age, growDays = 3f, fallAge, spin, fallAt = -1f, schedAge;
    bool    falling;
    Vector3 drift;

    public void Init(float scale) { maxScale = scale; spin = Random.Range(-120f, 120f); }
    public void FallNow()          { BeginFall(); }
    public void FallAfter(float days) { if (!falling) { fallAt = days; schedAge = 0f; } }

    void BeginFall()
    {
        if (falling) return;
        falling = true;
        drift   = Vector3.down * Random.Range(0.4f, 0.9f) + Random.insideUnitSphere * 0.15f;
    }

    void Update()
    {
        float dtDays = Time.deltaTime * GameManager.TIMESCALE / 24f;

        if (!falling)
        {
            age += dtDays;
            float t = Mathf.Clamp01(age / Mathf.Max(0.1f, growDays));
            transform.localScale = Vector3.one * (maxScale * Mathf.SmoothStep(0f, 1f, t));
            if (fallAt >= 0f) { schedAge += dtDays; if (schedAge >= fallAt) BeginFall(); }
        }
        else
        {
            fallAge += dtDays;
            transform.position += drift * (dtDays * 0.25f);
            transform.Rotate(Vector3.forward, spin * dtDays, Space.Self);
            float k = Mathf.Clamp01(1f - fallAge / 3f);
            transform.localScale = Vector3.one * (maxScale * k);
            if (k <= 0.02f) Destroy(gameObject);
        }
    }
}
