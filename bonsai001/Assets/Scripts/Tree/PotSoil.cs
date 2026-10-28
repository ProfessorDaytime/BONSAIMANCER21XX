using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages the soil substrate inside the pot.
///
/// The mix is expressed as seven component fractions that are normalised to sum to 1.0.
/// Derived properties (waterRetention, drainageRate, aerationScore, nutrientCapacity)
/// are computed from the mix and applied to the tree's growth and health systems.
///
/// Live state (degradation, saturation) updates each growing season via SeasonTick().
/// Repot() resets degradation and applies temporary root stress.
///
/// Requires TreeSkeleton on the same GameObject.
/// </summary>
[RequireComponent(typeof(TreeSkeleton))]
public class PotSoil : MonoBehaviour
{
    // ── Substrate component base properties ───────────────────────────────────
    // waterRetention, drainageRate, nutrientCapacity, degradeRate

    static readonly float[] AkadamaBase  = { 0.65f, 0.55f, 0.40f, 0.08f };
    static readonly float[] PumiceBase   = { 0.15f, 0.90f, 0.00f, 0.01f };
    static readonly float[] LavaRockBase = { 0.10f, 0.95f, 0.00f, 0.00f };
    static readonly float[] OrganicBase  = { 0.85f, 0.20f, 0.90f, 0.15f };
    static readonly float[] SandBase     = { 0.05f, 0.85f, 0.00f, 0.00f };
    static readonly float[] KanumaBase   = { 0.70f, 0.50f, 0.30f, 0.05f };
    static readonly float[] PerliteBase  = { 0.08f, 0.93f, 0.00f, 0.00f };

    // ── Mix (fractions; auto-normalised) ──────────────────────────────────────

    [Header("Substrate Mix")]
    [Tooltip("Classic bonsai clay. Retains shape well; breaks down over years.")]
    [Range(0f, 1f)] public float akadama  = 0.50f;

    [Tooltip("Pure drainage and aeration; structural. Doesn't compact.")]
    [Range(0f, 1f)] public float pumice   = 0.30f;

    [Tooltip("Maximum aeration; very free-draining. Never breaks down.")]
    [Range(0f, 1f)] public float lavaRock = 0.20f;

    [Tooltip("High moisture and nutrients; compacts over time. Root-rot risk if overused.")]
    [Range(0f, 1f)] public float organic  = 0.00f;

    [Tooltip("Cheap drainage filler; no structure or nutrients.")]
    [Range(0f, 1f)] public float sand     = 0.00f;

    [Tooltip("Acidic substrate ideal for azaleas and acid-loving species.")]
    [Range(0f, 1f)] public float kanuma   = 0.00f;

    [Tooltip("Lightweight volcanic glass; excellent drainage, near-zero water retention. Common pumice substitute.")]
    [Range(0f, 1f)] public float perlite  = 0.00f;

    // ── Derived properties (read-only; recomputed on repot + Awake) ───────────

    [Header("Derived Properties (read-only)")]
    [Tooltip("How slowly soil loses moisture after watering. 0=fast drain, 1=holds moisture.")]
    [Range(0f, 1f)] public float waterRetention;

    [Tooltip("How fast excess water exits the pot. Low drainage risks waterlogging.")]
    [Range(0f, 1f)] public float drainageRate;

    [Tooltip("Oxygen availability at root level. Higher = healthier roots.")]
    [Range(0f, 1f)] public float aerationScore;

    [Tooltip("Maximum nutrientReserve this soil can hold (0→2 scale). Higher organic = more capacity.")]
    public float nutrientCapacity;

    [Tooltip("Base rate at which this mix degrades per season.")]
    public float baseDegradeRate;

    // ── Live state ────────────────────────────────────────────────────────────

    [Header("Soil State")]
    [Tooltip("How compacted the soil has become. 0=fresh, 1=fully compacted. Resets on repot.")]
    [Range(0f, 1f)] public float soilDegradation;

    [Tooltip("Accumulated waterlog severity. Above 0.5 starts causing root rot each season.")]
    [Range(0f, 1f)] public float saturationLevel;

    [Tooltip("Growing seasons since the last repot.")]
    public int seasonsSinceRepot;

    // ── Pot Size ──────────────────────────────────────────────────────────────

    public enum PotSize { XS, S, M, L, XL, Slab }

    [Header("Pot Size")]
    [Tooltip("Current pot size. Change via Repot panel; affects rootAreaTransform scale.")]
    public PotSize potSize = PotSize.M;

    /// <summary>
    /// Dimensions (width, depth, height) in world units for each pot size.
    /// Slab is wide but very shallow.
    /// </summary>
    // 2026-07-04 scale calibration (queue #3): the old values (M = 2.6 wide) described a
    // logical box ~5× wider than the authored pot meshes (~0.5 rim). Roots legally filled
    // that phantom volume and read as a table-wide "root monster" dwarfing the fixed tree.
    // These are sized to the ACTUAL pot visuals; root segment lengths + growth floors were
    // rescaled with them (÷3.5) so root mechanics still function inside the honest box.
    // Re-bumped 2026-07-04 (same day): the first calibration predated the chord-length
    // stretch that made mature trees ~50% bigger — an L pot was dollhouse-scale under a
    // 4-unit canopy. Target proportion: mature canopy ≈ 2.5–3× the pot width.
    // 2026-08-24: all six sizes recalibrated to the real authored "<Size> Plastic Pot.fbx"
    // meshes (parsed vertex bounds, confirmed 1:1 FBX import scale — verified against
    // TinyPot01/XS first since its real dims were already known). Each row is that mesh's
    // actual diameter × height, so box and visual can never drift apart (F1b's root cause).
    // Deliberately NOT real-world inches (game keeps its game-feel unit convention).
    static readonly (float w, float d, float h)[] PotDimensions =
    {
        (0.528f, 0.528f, 0.604f), // XS — matches XS Plastic Pot.fbx / TinyPot01.fbx exactly
        (0.790f, 0.790f, 0.711f), // S  — matches S Plastic Pot.fbx exactly
        (1.116f, 1.116f, 0.784f), // M  — matches M Plastic Pot.fbx exactly ← default
        (1.371f, 1.371f, 0.621f), // L  — matches L Plastic Pot.fbx exactly
        (1.706f, 1.706f, 0.547f), // XL — matches XL Plastic Pot.fbx exactly
        (2.107f, 2.107f, 0.253f), // Slab — matches Slab 01 Plastic Pot.fbx exactly
    };

    // ── Drainage-hole pattern (shared contract between the Blender models and the sim) ──────
    // Expressed as fractions of the pot's FLOOR radius so ONE pattern scales to all six sizes:
    // model the holes at these fractions of each pot's base radius and the sim's hole discs line
    // up automatically — no per-pot marker objects, no per-pot table. `PotSoil.HoleWorldLayout`
    // prints the real Blender numbers per size.
    public const float HoleCentreRadiusFrac = 0.18f;   // central hole radius ÷ floor radius
    public const float HoleRingFrac         = 0.55f;   // ring hole distance from centre ÷ floor radius
    public const float HoleRadiusFrac       = 0.13f;   // ring hole radius ÷ floor radius

    /// <summary>Height of the current pot in world units — the root-lift animation scales off this
    /// so it stays proportional at every pot size.</summary>
    public float CurrentPotHeight => PotDimensions[(int)potSize].h;

    /// <summary>Interior soil volume of a pot size, in world units³. The pots are round tapered
    /// frustums, so this is the exact frustum volume (πh/3)(R² + Rr + r²) using the measured
    /// taper — not a box approximation, which would overstate a tapered pot by ~35%.</summary>
    public static float SoilVolumeOf(PotSize size)
    {
        var (w, d, h) = PotDimensions[(int)size];
        float R = (w + d) * 0.25f;                    // mean rim radius (w==d for these pots)
        float r = R * PotTaper[(int)size];            // floor radius
        return Mathf.PI * h / 3f * (R * R + R * r + r * r);
    }

    /// <summary>Soil volume of the pot currently in use.</summary>
    public float SoilVolume => SoilVolumeOf(potSize);

    /// <summary>Root capacity of the current pot relative to the reference size — the multiplier
    /// applied to the tree's root-node budget. This is the horticultural core of bonsai: a small
    /// pot holds less soil, so it supports fewer roots, so the tree takes up less water and
    /// nutrient and grows more slowly and smaller. XS lands near 0.17 against the M reference.</summary>
    public float PotVolumeFactor => SoilVolume / Mathf.Max(0.0001f, SoilVolumeOf(RootBudgetReferenceSize));

    /// <summary>Pot size the tree's authored `maxTotalRootNodes` budget is expressed FOR. M is the
    /// component default, so existing tuning keeps its meaning at M and scales from there.</summary>
    public const PotSize RootBudgetReferenceSize = PotSize.M;

    // ── Mass (drives tipping stability) ───────────────────────────────────────
    // Dry bulk densities of each substrate, in g/cm³, real horticultural values. These give the
    // soil mix a SECOND meaning beyond drainage: free-draining pumice/perlite mixes are the best
    // for roots but the LIGHTEST, so they hold the tree down least — a real grower trade-off.
    static readonly float[] ComponentBulkDensity =
    {   // akadama pumice lava organic sand kanuma perlite
        0.70f, 0.50f, 0.90f, 0.30f, 1.60f, 0.60f, 0.10f
    };

    /// <summary>Cubic centimetres in one cubic world unit. From the project's scale convention:
    /// 0.527 units = 4 in = 10.16 cm, so 1 unit ≈ 19.28 cm and 1 unit³ ≈ 7166 cm³. Everything mass-
    /// related resolves to GRAMS through this, so the stability numbers are physically meaningful
    /// and checkable against reality (an XS pot of soil ≈ 475 g, which is right for a 4-in pot).</summary>
    public const float WorldToCm3 = 7166f;

    [Tooltip("Pot shell material density, g/cm³. 0.95 ≈ polypropylene nursery pot; a glazed " +
             "ceramic pot is nearer 2.4 and resists tipping far better.")]
    public float potShellDensity = 0.95f;

    [Tooltip("Pot wall thickness in centimetres — drives the shell's mass.")]
    public float potWallThicknessCm = 0.25f;

    [Tooltip("Water held by saturated substrate as a fraction of total soil volume. Bonsai mixes " +
             "run high — this is why a watered pot is far more stable than a dry one.")]
    [Range(0f, 0.7f)] public float saturatedWaterFraction = 0.40f;

    /// <summary>Dry bulk density of the CURRENT mix, g/cm³, from the normalised fractions.</summary>
    public float MixDryDensity
    {
        get
        {
            float[] f = { akadama, pumice, lavaRock, organic, sand, kanuma, perlite };
            float sum = 0f, dens = 0f;
            for (int i = 0; i < f.Length; i++) { sum += f[i]; dens += f[i] * ComponentBulkDensity[i]; }
            return sum > 0.0001f ? dens / sum : 0.6f;
        }
    }

    /// <summary>Mass of the soil column in GRAMS, including held water. A saturated bonsai mix
    /// carries ~40% of its volume as water, so a watered pot runs ~1.5× its dry weight.</summary>
    public float SoilMass(float moisture01)
    {
        float cm3   = SoilVolume * WorldToCm3;
        float dry   = cm3 * MixDryDensity;
        float water = cm3 * saturatedWaterFraction * Mathf.Clamp01(moisture01);   // water = 1.0 g/cm³
        return dry + water;
    }

    /// <summary>Mass of the empty pot shell in GRAMS — a thin shell over the frustum's outer
    /// surface, so bigger pots are heavier without per-pot authoring.</summary>
    public float PotShellMass
    {
        get
        {
            var (w, d, h) = PotDimensions[(int)potSize];
            float R = (w + d) * 0.25f;
            float r = R * PotTaper[(int)potSize];
            float slant = Mathf.Sqrt((R - r) * (R - r) + h * h);
            float areaWorld = Mathf.PI * (R + r) * slant + Mathf.PI * r * r;   // side + base
            float areaCm2   = areaWorld * Mathf.Pow(WorldToCm3, 2f / 3f);      // world² → cm²
            return areaCm2 * potWallThicknessCm * potShellDensity;
        }
    }

    /// <summary>Half-width of the pot's FOOTPRINT — the lever arm resisting a tip. This is the
    /// base radius, not the rim: a tapered pot pivots on its narrow base, which is exactly why
    /// wide shallow bonsai pots are stable and tall nursery pots are not.</summary>
    public float FootprintRadius
    {
        get
        {
            var (w, d, _) = PotDimensions[(int)potSize];
            return (w + d) * 0.25f * PotTaper[(int)potSize];
        }
    }

    /// <summary>Height of the pot+soil centre of mass above the pot's base, world units. Soil sits
    /// low in the pot, which is what makes it an effective counterweight.</summary>
    public float BallastCentreHeight => PotDimensions[(int)potSize].h * 0.45f;

    /// <summary>Modelling aid: the drainage-hole layout for one pot size in WORLD units, ready to
    /// punch into the Blender model. Returns floor radius, centre-hole radius, ring distance and
    /// ring-hole radius.</summary>
    public static (float floorR, float centreR, float ringDist, float ringR) HoleWorldLayout(PotSize size)
    {
        var (w, _, _) = PotDimensions[(int)size];
        float floorR = w * 0.5f * PotTaper[(int)size];
        return (floorR, HoleCentreRadiusFrac * floorR, HoleRingFrac * floorR, HoleRadiusFrac * floorR);
    }

    /// <summary>Base-to-rim radius ratio of each authored pot mesh (measured from the FBX vertex
    /// rings, 2026-08-24). Every nursery pot came out at a uniform 0.706; the slab is shallower-
    /// walled at 0.852. Drives both the soil surface radius and the root-containment profile.</summary>
    static readonly float[] PotTaper = { 0.706f, 0.706f, 0.706f, 0.706f, 0.706f, 0.852f };

    /// <summary>Containment test in root-box LOCAL space (the ±0.5 unit cube), against the pot's
    /// real ROUND, TAPERED profile instead of the box itself. The box circumscribes the pot, so a
    /// pure box test legally allowed roots into the corners (√2 ≈ 41% past the wall) and into the
    /// taper void low down (up to ~42% past the base wall) — space that is outside the visible pot.
    /// That is the residue of the F1b "roots through the pot wall" class.</summary>
    public bool IsInsidePotProfile(float lx, float ly, float lz)
    {
        if (!useTaperedContainment) return Mathf.Abs(lx) <= 0.5f && Mathf.Abs(lz) <= 0.5f;
        float t     = Mathf.Clamp01(ly + 0.5f);                       // 0 = floor, 1 = rim
        float rNorm = Mathf.Lerp(0.5f * PotTaper[(int)potSize], 0.5f, t);
        return (lx * lx + lz * lz) <= rNorm * rNorm;
    }

    [Tooltip("Contain roots to the pot's real round, tapered profile rather than the circumscribing " +
             "box. More faithful (no roots in the phantom corners), but it removes ~36% of the box's " +
             "volume, so trees become pot-bound sooner and want repotting more often. Turn off to " +
             "restore the old box behaviour.")]
    public bool useTaperedContainment = true;

    /// <summary>
    /// Resize the rootAreaTransform to match the chosen pot size.
    /// Call this after setting potSize, typically during Repot().
    /// </summary>
    [Header("Pot Visual")]
    [Tooltip("Optional pot visual object. With usePlaceholderPotMesh on, a procedural pot\n" +
             "sized EXACTLY to PotDimensions is generated here on every size change, so the\n" +
             "visual pot and the root box can never drift apart. Replace with authored\n" +
             "models per size later and untick the flag.")]
    public Transform potVisual;
    [Tooltip("Generate placeholder pot meshes on potVisual at every size change. Turn OFF to " +
             "use the authored meshes in Size Pot Prefabs below.")]
    public bool usePlaceholderPotMesh = false;

    [Tooltip("Authored pot model per PotSize, in enum order (XS, S, M, L, XL, Slab). Used when " +
             "usePlaceholderPotMesh is off and no catalog pot has been chosen. Any empty slot " +
             "falls back to the procedural placeholder for that size.")]
    public GameObject[] sizePotPrefabs = new GameObject[6];

    // Catalog override: a pot chosen in the item catalog wins over sizePotPrefabs until cleared.
    GameObject catalogPotPrefab;

    bool loggedSeating;   // one-shot [Seat] diagnostic guard

    // Bench height: the fixed surface the pot rests on. Captured from the starting arrangement,
    // then held, so changing pot depth moves the TREE rather than floating the pot.
    float benchY;
    bool  benchCaptured;

    /// <summary>Skin the pot with a catalog-chosen model (null clears back to the per-size
    /// default). The mesh is re-seated on the root box immediately.</summary>
    public void SetPotModel(GameObject prefab, Transform rootAreaTransform)
    {
        catalogPotPrefab = prefab;
        UpdatePotVisual(rootAreaTransform);
    }

    Material potCeramicMat, potSoilMat;

    Transform soilVisual;   // runtime-built soil surface for authored (open-shell) pot meshes
    float     soilRadX, soilRadZ;   // its half-extents, for RaycastSoil

    /// <summary>Builds/updates the soil surface sitting inside an authored pot mesh. The pot is a
    /// round tapered frustum, so the disc radius is the pot's interior radius AT the soil line,
    /// derived from <see cref="PotTaper"/> — not the rim radius, which would clip through the wall.
    /// Sits <c>soilDropFrac</c> below the rim so you look slightly down into the pot.</summary>
    void UpdateSoilVisual(float w, float d, float h, Transform rootAreaTransform)
    {
        if (rootAreaTransform == null) return;
        const float soilDropFrac = 0.10f;   // soil surface below the rim, fraction of pot height
        const float wallFrac     = 0.94f;   // inside the wall thickness

        if (soilVisual == null)
        {
            var go = new GameObject("_PotSoilSurface");
            go.transform.SetParent(null, true);
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>();
            soilVisual = go.transform;
        }

        float taper  = PotTaper[(int)potSize];
        float tSoil  = 1f - soilDropFrac;                       // 0 = floor, 1 = rim
        float rTopX  = w * 0.5f, rTopZ = d * 0.5f;
        float radX   = Mathf.Lerp(rTopX * taper, rTopX, tSoil) * wallFrac;
        float radZ   = Mathf.Lerp(rTopZ * taper, rTopZ, tSoil) * wallFrac;

        soilRadX = radX;
        soilRadZ = radZ;
        var mf = soilVisual.GetComponent<MeshFilter>();
        mf.sharedMesh = ProceduralPot.BuildSoilSurface(radX, radZ, h * 0.10f);
        CeramicMaterial();                                       // ensures potSoilMat exists
        soilVisual.GetComponent<MeshRenderer>().sharedMaterial = potSoilMat;

        float boxTop = rootAreaTransform.position.y + h * 0.5f;
        soilVisual.position   = new Vector3(rootAreaTransform.position.x,
                                            boxTop - h * soilDropFrac,
                                            rootAreaTransform.position.z);
        soilVisual.rotation   = Quaternion.identity;
        soilVisual.localScale = Vector3.one;
    }

    /// <summary>
    /// Where a ray meets the soil surface inside the pot, if it does (the watering can's target).
    /// The surface is the elliptical disc UpdateSoilVisual builds; a little extra radius lets a
    /// click on the rim count, since nobody aims a watering can at the pot wall.
    /// </summary>
    public bool RaycastSoil(Ray ray, out Vector3 point)
    {
        point = default;
        if (soilVisual == null || soilRadX <= 0f || soilRadZ <= 0f) return false;
        var plane = new Plane(Vector3.up, soilVisual.position);
        if (!plane.Raycast(ray, out float t)) return false;
        point = ray.GetPoint(t);
        Vector3 d  = point - soilVisual.position;
        float   ex = d.x / (soilRadX * 1.1f), ez = d.z / (soilRadZ * 1.1f);
        return ex * ex + ez * ez <= 1f;
    }

    // ── Tipping visuals ───────────────────────────────────────────────────────
    // A tipped planting goes over as a unit — the tree does not leap out of a pot that stays
    // upright. The pot and soil are separate scene objects from the tree, so they need rotating
    // about the same pivot explicitly.
    bool       visualsTipped;
    Vector3    potHomePos,  soilHomePos;
    Quaternion potHomeRot,  soilHomeRot;

    /// <summary>Rotate pot + soil about a world pivot, so they fall with the tree.</summary>
    public void TipVisuals(Vector3 pivotWorld, Vector3 axisWorld, float degrees)
    {
        if (visualsTipped) return;
        visualsTipped = true;

        var rot = Quaternion.AngleAxis(degrees, axisWorld.normalized);
        if (potVisual != null)
        {
            potHomePos = potVisual.position; potHomeRot = potVisual.rotation;
            potVisual.position = pivotWorld + rot * (potVisual.position - pivotWorld);
            potVisual.rotation = rot * potVisual.rotation;
        }
        if (soilVisual != null)
        {
            soilHomePos = soilVisual.position; soilHomeRot = soilVisual.rotation;
            soilVisual.position = pivotWorld + rot * (soilVisual.position - pivotWorld);
            soilVisual.rotation = rot * soilVisual.rotation;
        }
    }

    /// <summary>Stand the pot and soil back up.</summary>
    public void RestoreVisuals()
    {
        if (!visualsTipped) return;
        visualsTipped = false;
        if (potVisual  != null) { potVisual.position  = potHomePos;  potVisual.rotation  = potHomeRot; }
        if (soilVisual != null) { soilVisual.position = soilHomePos; soilVisual.rotation = soilHomeRot; }
    }

    /// <summary>Lazily builds the shared ceramic/soil materials and returns the ceramic one.
    /// Also the fallback skin for an authored mesh that arrived without its own material.</summary>
    Material CeramicMaterial()
    {
        if (potCeramicMat == null)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            potCeramicMat = new Material(sh);
            Color ceramic = new Color(0.58f, 0.58f, 0.60f);
            if (potCeramicMat.HasProperty("_BaseColor")) potCeramicMat.SetColor("_BaseColor", ceramic);
            if (potCeramicMat.HasProperty("_Color"))     potCeramicMat.SetColor("_Color",     ceramic);
            potSoilMat = new Material(sh);
            Color soil = new Color(0.35f, 0.26f, 0.18f);
            if (potSoilMat.HasProperty("_BaseColor")) potSoilMat.SetColor("_BaseColor", soil);
            if (potSoilMat.HasProperty("_Color"))     potSoilMat.SetColor("_Color",     soil);
        }
        return potCeramicMat;
    }

    /// <summary>Puts the right pot mesh on potVisual for the current size and seats it on the
    /// root box (rim top = box top). Source priority: catalog choice → authored per-size prefab
    /// → procedural placeholder. Authored meshes are scaled from their real bounds to the
    /// logical PotDimensions, so a model that drifts from its spec still can't desync the box.
    /// World-size normalized against any scaled parents, same as the root box itself.</summary>
    void UpdatePotVisual(Transform rootAreaTransform)
    {
        if (potVisual == null) return;
        var (w, d, h) = PotDimensions[(int)potSize];

        var mf = potVisual.GetComponent<MeshFilter>();
        if (mf == null) mf = potVisual.gameObject.AddComponent<MeshFilter>();
        var mr = potVisual.GetComponent<MeshRenderer>();
        if (mr == null) mr = potVisual.gameObject.AddComponent<MeshRenderer>();

        GameObject authored = catalogPotPrefab;
        if (authored == null && !usePlaceholderPotMesh
            && sizePotPrefabs != null && (int)potSize < sizePotPrefabs.Length)
            authored = sizePotPrefabs[(int)potSize];

        var srcFilter = authored != null ? authored.GetComponentInChildren<MeshFilter>() : null;
        Mesh authoredMesh = srcFilter != null ? srcFilter.sharedMesh : null;

        // A non-uniformly scaled ancestor SHEARS any rotated child — Unity's transform cannot
        // represent rotation-under-nonuniform-scale, and dividing the parent scale out per-axis
        // in LOCAL space does not undo it once the mesh is rotated. This scene has exactly that
        // (rootArea's parent logged at 190×500×500), which rendered the pot as a squashed dome
        // (2026-08-24). Detach to the world root — position/rotation/scale are all set
        // explicitly below, so the pot gains nothing from being parented.
        if (potVisual.parent != null)
        {
            Vector3 pls = potVisual.parent.lossyScale;
            bool nonUniform = Mathf.Abs(pls.x - pls.y) > 0.001f || Mathf.Abs(pls.y - pls.z) > 0.001f;
            if (nonUniform)
            {
                Debug.Log($"[Seat] Detaching potVisual from '{potVisual.parent.name}' " +
                          $"(non-uniform lossyScale {pls}) — it was shearing the rotated pot mesh.");
                potVisual.SetParent(null, true);
            }
        }

        Vector3 ps = potVisual.parent != null ? potVisual.parent.lossyScale : Vector3.one;
        Vector3 parentNorm = new Vector3(
            1f / Mathf.Max(0.0001f, Mathf.Abs(ps.x)),
            1f / Mathf.Max(0.0001f, Mathf.Abs(ps.y)),
            1f / Mathf.Max(0.0001f, Mathf.Abs(ps.z)));

        if (authoredMesh != null)
        {
            mf.sharedMesh = authoredMesh;
            var srcRend = authored.GetComponentInChildren<MeshRenderer>();
            bool gotMats = false;
            if (srcRend != null && srcRend.sharedMaterials.Length > 0)
            {
                // An FBX with no authored material imports with a null/placeholder slot —
                // copying that renders the pot solid black. Only take materials that exist.
                bool allReal = true;
                foreach (var m in srcRend.sharedMaterials) if (m == null) { allReal = false; break; }
                if (allReal) { mr.sharedMaterials = srcRend.sharedMaterials; gotMats = true; }
            }
            if (!gotMats) mr.sharedMaterials = new[] { CeramicMaterial() };

            // The mesh's own orientation inside the prefab MUST be honoured: Blender FBXs
            // import Z-up with a -90° X rotation on the model transform rather than baked
            // into the vertices, so forcing identity laid every pot on its side (2026-08-24).
            // NOT relative-to-root: single-mesh FBXs collapse so the MeshFilter sits on the
            // prefab ROOT and the -90° lives on that root's own transform — a relative matrix
            // cancels it by construction (that bug shipped for one round: potVisualY logged
            // 81.698 = the identity-rotation seat, not 81.396). Asset-space rotation
            // accumulates from the root inclusive, so it is right in both layouts.
            Quaternion meshRot = srcFilter.transform.rotation;

            // Fit LOCAL mesh axes to the world dimension each one lands on after meshRot —
            // scale is applied pre-rotation (Unity's TRS), so mapping matters.
            Bounds b = authoredMesh.bounds;
            Vector3 target = new Vector3(w, h, d);
            Vector3 fit = Vector3.one;
            for (int i = 0; i < 3; i++)
            {
                Vector3 localAxis = Vector3.zero; localAxis[i] = 1f;
                Vector3 worldAxis = meshRot * localAxis;
                int j = 0; float best = Mathf.Abs(worldAxis.x);
                if (Mathf.Abs(worldAxis.y) > best) { best = Mathf.Abs(worldAxis.y); j = 1; }
                if (Mathf.Abs(worldAxis.z) > best) j = 2;
                fit[i] = target[j] / Mathf.Max(0.0001f, b.size[i]);
            }

            potVisual.rotation   = meshRot;
            potVisual.localScale = Vector3.Scale(fit, parentNorm);

            if (rootAreaTransform != null)
            {
                // Pivot is wherever the artist left it, so derive placement from the rotated,
                // scaled corner extents rather than assuming a centered or based pivot.
                Vector3 wMin = Vector3.positiveInfinity, wMax = Vector3.negativeInfinity;
                for (int k = 0; k < 8; k++)
                {
                    Vector3 corner = b.center + new Vector3(
                        (k & 1) == 0 ? -b.extents.x : b.extents.x,
                        (k & 2) == 0 ? -b.extents.y : b.extents.y,
                        (k & 4) == 0 ? -b.extents.z : b.extents.z);
                    Vector3 wp = meshRot * Vector3.Scale(fit, corner);
                    wMin = Vector3.Min(wMin, wp); wMax = Vector3.Max(wMax, wp);
                }
                Vector3 wCenter = (wMin + wMax) * 0.5f;
                float boxTop = rootAreaTransform.position.y + h * 0.5f;
                potVisual.position = new Vector3(
                    rootAreaTransform.position.x - wCenter.x,
                    boxTop - wMax.y,                       // rim top sits on the box top
                    rootAreaTransform.position.z - wCenter.z);
            }
            // Authored pots are open shells — the procedural pot carried its soil as submesh 1,
            // so switching to real meshes left the player looking into an empty pot (2026-08-24).
            UpdateSoilVisual(w, d, h, rootAreaTransform);
            return;
        }

        mf.sharedMesh      = ProceduralPot.Build(w, d, h);
        mr.sharedMaterials = new[] { CeramicMaterial(), potSoilMat };

        // Sit exactly on the root box: same position, world-scale 1 (normalize parents).
        if (rootAreaTransform != null) potVisual.position = rootAreaTransform.position;
        potVisual.localScale = parentNorm;
        potVisual.rotation = Quaternion.identity;
    }

    [Tooltip("Keep the root box's SOIL SURFACE (its top face) at the tree's base. The trunk's " +
             "root node is created at mesh-local origin — i.e. the tree's own transform — but the " +
             "box and pot are placed from rootAreaTransform, and nothing tied the two together: a " +
             "rootArea object not sitting exactly at the tree's base left the tree rendering in " +
             "mid-air above its pot at every age (2026-08-24). Also keeps roots growing inside the " +
             "pot they are drawn in. Turn off only if a scene deliberately offsets the two.")]
    public bool alignRootBoxToTreeBase = true;

    public void ApplyPotSize(Transform rootAreaTransform)
    {
        if (rootAreaTransform == null) return;
        var (w, d, h) = PotDimensions[(int)potSize];

        if (alignRootBoxToTreeBase)
        {
            // The BENCH is the fixed thing — the pot rests on it, and the soil line is one pot
            // height above it. Pinning the soil line to the tree instead meant every pot depth put
            // its base somewhere different, so a shallow slab hovered 0.35 above the table
            // (2026-08-24). Captured once from the starting arrangement.
            // Use the tree's RESTING height, never the live transform: RootRake/RootPrune animate
            // the tree several units into the air, and seating to that sent the pot up after it.
            var skelForBase = GetComponent<TreeSkeleton>();
            float baseY = skelForBase != null ? skelForBase.GroundedBaseY : transform.position.y;

            if (!benchCaptured)
            {
                benchY        = baseY - h;   // bench = current soil line minus this pot's depth
                benchCaptured = true;
            }

            // Soil line rides pot depth; the tree follows it so its base stays at the soil.
            float soilTopY = benchY + h;
            skelForBase?.SetGroundedBaseY(soilTopY);

            Vector3 want = new Vector3(transform.position.x,
                                       soilTopY - h * 0.5f,
                                       transform.position.z);
            if ((rootAreaTransform.position - want).sqrMagnitude > 1e-8f)
            {
                if (!loggedSeating)
                    Debug.Log($"[Seat] Re-seating root box {rootAreaTransform.position} → {want} " +
                              $"(tree base y={transform.position.y:F3}, pot {potSize} h={h:F3}) — " +
                              "the tree was floating clear of its soil.");
                rootAreaTransform.position = want;
            }
        }
        // PotDimensions are WORLD units (see doc above) but this sets localScale — so a
        // scaled parent silently broke the contract: one 2026-07-02 session logged the
        // rootArea at 3.12×2.77 world (sane) AND 494×1000 (under a ~150×-scaled pot/table
        // parent), causing the intermittent root starbursts. Divide out the parent's
        // lossyScale so the box's world size is always exactly PotDimensions.
        Vector3 ps = rootAreaTransform.parent != null
            ? rootAreaTransform.parent.lossyScale : Vector3.one;
        rootAreaTransform.localScale = new Vector3(
            w / Mathf.Max(0.0001f, Mathf.Abs(ps.x)),
            h / Mathf.Max(0.0001f, Mathf.Abs(ps.y)),
            d / Mathf.Max(0.0001f, Mathf.Abs(ps.z)));

        UpdatePotVisual(rootAreaTransform);

        // One-shot seating report (2026-08-24): the tree renders in the AIR above the pot —
        // even a 0.5-unit seedling floats clear of the soil, so it is a placement gap, not a
        // missing trunk mesh. Prints who is where so the offset can be attributed to the
        // pot box, the tree transform, or the table under them.
        if (!loggedSeating)
        {
            loggedSeating = true;
            var skel = GetComponent<TreeSkeleton>();
            float boxTop = rootAreaTransform.position.y + h * 0.5f;
            // node worldPosition is mesh-LOCAL despite the name — convert before comparing.
            string rootInfo = (skel != null && skel.root != null)
                ? $"treeBaseY={transform.TransformPoint(skel.root.worldPosition).y:F3} " +
                  $"(gap above soil={(transform.TransformPoint(skel.root.worldPosition).y - boxTop):F3})"
                : "treeRoot=<null>";
            Debug.Log($"[Seat] pot={potSize} box={w:F3}×{d:F3}×{h:F3} " +
                      $"| rootAreaY={rootAreaTransform.position.y:F3} soilTopY={boxTop:F3} " +
                      $"| treeTransformY={transform.position.y:F3} {rootInfo} " +
                      $"| potVisualY={(potVisual != null ? potVisual.position.y.ToString("F3") : "<none>")} " +
                      $"| parentScale={ps}");
        }

        if (verboseLog)
            Debug.Log($"[Soil] ApplyPotSize {potSize} | world={w}×{d}×{h} parentScale={ps} | year={GameManager.year}");
    }

    // ── Presets ───────────────────────────────────────────────────────────────

    public enum SoilPreset
    {
        Custom,
        ClassicBonsai,      // 50 akadama / 30 pumice / 20 lava rock
        FreeDraining,       // 20 akadama / 40 pumice / 40 lava rock
        MoistureRetaining,  // 50 akadama / 20 pumice / 10 lava / 20 organic
        Acidic,             // 40 kanuma / 30 pumice / 30 akadama
    }

    [Header("Preset")]
    [Tooltip("Pick a preset to populate the mix sliders. Set to Custom to tune manually.")]
    public SoilPreset preset = SoilPreset.ClassicBonsai;

    // ── Inspector validation ──────────────────────────────────────────────────

    [Header("Settings")]
    [Tooltip("Enable per-season debug logs for soil state. Off by default.")]
    [SerializeField] bool verboseLog = false;

    [Tooltip("Health damage applied to each root node per season when saturationLevel > 0.5.")]
    [SerializeField] float rootRotDamagePerSeason = 0.04f;

    [Tooltip("Health damage applied to each root terminal when repotting.")]
    [SerializeField] float repotBaseDamage = 0.10f;

    [Tooltip("Soil degrades at half-speed once it has been in the pot this many seasons.")]
    [SerializeField] int degradationSlowdownSeasons = 4;

    // ── Unity ─────────────────────────────────────────────────────────────────

    void Awake()
    {
        ApplyPreset(preset);
        ComputeDerivedProperties();
    }

    void Start()
    {
        // Build the pot immediately — not only on size changes — so assigning Pot Visual
        // on an existing tree shows a pot without waiting for the next repot event
        // (2026-07-04 setup confusion).
        if (potVisual != null)
        {
            var skel = GetComponent<TreeSkeleton>();
            UpdatePotVisual(skel != null ? skel.GetRootAreaTransform() : null);
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Apply a named preset, then recompute derived properties.
    /// </summary>
    public void ApplyPreset(SoilPreset p)
    {
        preset = p;
        switch (p)
        {
            case SoilPreset.ClassicBonsai:
                (akadama, pumice, lavaRock, organic, sand, kanuma) = (0.50f, 0.30f, 0.20f, 0f, 0f, 0f);
                break;
            case SoilPreset.FreeDraining:
                (akadama, pumice, lavaRock, organic, sand, kanuma) = (0.20f, 0.40f, 0.40f, 0f, 0f, 0f);
                break;
            case SoilPreset.MoistureRetaining:
                (akadama, pumice, lavaRock, organic, sand, kanuma) = (0.50f, 0.20f, 0.10f, 0.20f, 0f, 0f);
                break;
            case SoilPreset.Acidic:
                (akadama, pumice, lavaRock, organic, sand, kanuma) = (0.30f, 0.30f, 0f, 0f, 0f, 0.40f);
                break;
        }
        ComputeDerivedProperties();
    }

    /// <summary>
    /// Recompute derived properties from the current mix fractions.
    /// Normalises fractions so they always sum to 1.
    /// </summary>
    public void ComputeDerivedProperties()
    {
        float total = akadama + pumice + lavaRock + organic + sand + kanuma + perlite;
        if (total <= 0f) { akadama = 1f; total = 1f; }

        float a = akadama  / total;
        float p = pumice   / total;
        float l = lavaRock / total;
        float o = organic  / total;
        float s = sand     / total;
        float k = kanuma   / total;
        float r = perlite  / total;

        waterRetention  = a * AkadamaBase[0]  + p * PumiceBase[0]  + l * LavaRockBase[0]
                        + o * OrganicBase[0]  + s * SandBase[0]    + k * KanumaBase[0]  + r * PerliteBase[0];
        drainageRate    = a * AkadamaBase[1]  + p * PumiceBase[1]  + l * LavaRockBase[1]
                        + o * OrganicBase[1]  + s * SandBase[1]    + k * KanumaBase[1]  + r * PerliteBase[1];
        float rawNutrient
                        = a * AkadamaBase[2]  + p * PumiceBase[2]  + l * LavaRockBase[2]
                        + o * OrganicBase[2]  + s * SandBase[2]    + k * KanumaBase[2]  + r * PerliteBase[2];
        baseDegradeRate = a * AkadamaBase[3]  + p * PumiceBase[3]  + l * LavaRockBase[3]
                        + o * OrganicBase[3]  + s * SandBase[3]    + k * KanumaBase[3]  + r * PerliteBase[3];

        aerationScore    = 1f - waterRetention;
        // nutrientCapacity: scales the maximum nutrientReserve the soil can hold (0→2 range)
        nutrientCapacity = Mathf.Lerp(1.2f, 2.0f, rawNutrient);
    }

    [Tooltip("0–1: fresh-repot compaction around the root ball — the un-raked fraction of the " +
             "old soil, set by the rake mini-game at confirm. Penalizes drainage and aeration, " +
             "then loosens over the first season as roots and watering work it open.")]
    [Range(0f, 1f)] public float compaction = 0f;

    /// <summary>
    /// Effective drainage rate accounting for degradation, fresh-repot compaction, and the pot's
    /// drainage holes (more/bigger holes drain faster; few or clogged holes risk waterlogging).
    /// </summary>
    public float EffectiveDrainageRate => drainageRate * (1f - soilDegradation * 0.70f) * (1f - compaction * 0.5f) * HoleDrainageFactor;

    /// <summary>
    /// Effective water retention accounting for degradation and compaction (both hold more water).
    /// </summary>
    public float EffectiveWaterRetention => Mathf.Min(1f, waterRetention + soilDegradation * 0.25f + compaction * 0.15f);

    /// <summary>
    /// Drain rate multiplier applied to TreeSkeleton.drainRatePerDay.
    /// High retention = slower drain (multiplier &lt; 1).
    /// Low retention = faster drain (multiplier &gt; 1).
    /// Range: ~0.35 (very retentive) → ~1.55 (very free-draining).
    /// </summary>
    public float DrainRateMultiplier => Mathf.Lerp(1.55f, 0.35f, EffectiveWaterRetention);

    // ── Drainage holes ──────────────────────────────────────────────────────────
    // Holes live on the bottom face of the rootArea box, expressed in box-LOCAL space
    // (the same space TreeSkeleton uses for root containment: |x|,|z| ≤ 0.5, bottom at y = -0.5).
    // So a hole is a disc (centre.x, centre.y → box-local x,z) of the given local radius. Authored
    // by placing DrainageHole cylinders in the pot model; falls back to a procedural pattern when a
    // pot has none yet, so the drainage + root-escape mechanics work before pots are authored.

    public struct Hole { public Vector2 centre; public float radius; }   // box-local XZ centre + radius

    readonly List<Hole> drainageHoles = new List<Hole>();
    float totalHoleArea;
    bool  holesReady;

    // Total disc area of the procedural default pattern — the "normal pot" baseline (factor = 1).
    const float BaselineHoleArea = 0.063f;   // π·(0.075² + 4·0.06²)

    public IReadOnlyList<Hole> DrainageHoles { get { EnsureHoles(); return drainageHoles; } }

    /// <summary>
    /// Drainage multiplier from the pot's holes, relative to a normally-holed pot. More/bigger holes
    /// drain faster (&gt;1, less waterlogging); few or clogged holes drain slower (&lt;1, rot risk).
    /// </summary>
    public float HoleDrainageFactor
    {
        get { EnsureHoles(); return Mathf.Clamp(totalHoleArea / BaselineHoleArea, 0.4f, 1.6f); }
    }

    void EnsureHoles() { if (!holesReady) RefreshDrainageHoles(); }

    /// <summary>
    /// (Re)collect drainage holes from DrainageHole cylinder markers in the scene, converting each to
    /// a box-local disc and hiding its renderer. Falls back to a procedural pattern when none exist.
    /// Call after the pot's rootArea transform changes (e.g. a repot that resizes the pot).
    /// </summary>
    public void RefreshDrainageHoles()
    {
        drainageHoles.Clear();
        var rootArea = GetComponent<TreeSkeleton>()?.GetRootAreaTransform();

        var markers = FindObjectsByType<DrainageHole>(FindObjectsSortMode.None);
        if (rootArea != null && markers != null && markers.Length > 0)
        {
            foreach (var m in markers)
            {
                if (m == null) continue;
                Vector3 local = rootArea.InverseTransformPoint(m.transform.position);
                // World radius → box-local. The box scale differs on X vs Z, so average the two
                // axes (a circular hole becomes a slight ellipse in box-local; close enough for the disc test).
                float rLocal = (m.WorldRadius / Mathf.Max(0.001f, rootArea.lossyScale.x)
                              + m.WorldRadius / Mathf.Max(0.001f, rootArea.lossyScale.z)) * 0.5f;
                drainageHoles.Add(new Hole { centre = new Vector2(local.x, local.z), radius = Mathf.Max(0.01f, rLocal) });
                if (m.hideInGame)
                    foreach (var r in m.GetComponentsInChildren<Renderer>()) r.enabled = false;
            }
        }
        else
        {
            // Procedural default: one central hole plus a ring of four, sized and placed relative
            // to the pot's real FLOOR radius (rim × taper), not the box half-extent.
            // The old constants (ring 0.32, r 0.06) were box-relative and reached 0.38 box-local
            // while the nursery-pot floor only extends to 0.5×0.706 = 0.353 — the ring holes sat
            // OUTSIDE the base wall, so a root "escaping through a hole" emerged from thin air
            // beside the pot (2026-08-24). Anything authored in Blender must match this pattern;
            // see HoleRingFrac/HoleRadiusFrac and the table in PLAN.
            float baseR = 0.5f * PotTaper[(int)potSize];          // pot floor radius, box-local
            drainageHoles.Add(new Hole { centre = Vector2.zero, radius = HoleCentreRadiusFrac * baseR });
            for (int i = 0; i < 4; i++)
            {
                float a = (i + 0.5f) / 4f * Mathf.PI * 2f;
                drainageHoles.Add(new Hole
                {
                    centre = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (HoleRingFrac * baseR),
                    radius = HoleRadiusFrac * baseR
                });
            }
        }

        totalHoleArea = 0f;
        foreach (var h in drainageHoles) totalHoleArea += Mathf.PI * h.radius * h.radius;
        holesReady = true;
    }

    /// <summary>
    /// True if box-local floor point (x,z) lies within any drainage hole disc — used by the root
    /// system to let pot-bound roots grow out through a hole instead of being clamped at the floor.
    /// </summary>
    public bool IsOverHole(float boxLocalX, float boxLocalZ)
    {
        EnsureHoles();
        foreach (var h in drainageHoles)
        {
            float dx = boxLocalX - h.centre.x, dz = boxLocalZ - h.centre.y;
            if (dx * dx + dz * dz <= h.radius * h.radius) return true;
        }
        return false;
    }

    [Header("Debug")]
    [Tooltip("Draw the derived drainage-hole discs on the pot floor (GL overlay) to verify placement.")]
    public bool debugDrawHoles = false;
    static Material glMat;

    void OnRenderObject()
    {
        if (!debugDrawHoles || !BonsaiDebug.Enabled) return;
        EnsureHoles();
        var rootArea = GetComponent<TreeSkeleton>()?.GetRootAreaTransform();
        if (rootArea == null || drainageHoles.Count == 0) return;

        if (glMat == null)
        {
            glMat = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
            glMat.SetInt("_ZWrite", 0);
            glMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);   // draw over geometry
        }
        glMat.SetPass(0);
        GL.PushMatrix();
        GL.MultMatrix(rootArea.localToWorldMatrix);   // hole discs are in box-local space, floor at y = -0.5
        GL.Begin(GL.LINES);
        GL.Color(new Color(0.3f, 0.8f, 1f, 1f));
        const int seg = 24;
        foreach (var h in drainageHoles)
        {
            for (int i = 0; i < seg; i++)
            {
                float a0 = i        / (float)seg * Mathf.PI * 2f;
                float a1 = (i + 1)  / (float)seg * Mathf.PI * 2f;
                GL.Vertex3(h.centre.x + Mathf.Cos(a0) * h.radius, -0.5f, h.centre.y + Mathf.Sin(a0) * h.radius);
                GL.Vertex3(h.centre.x + Mathf.Cos(a1) * h.radius, -0.5f, h.centre.y + Mathf.Sin(a1) * h.radius);
            }
        }
        GL.End();
        GL.PopMatrix();
    }

    /// <summary>
    /// Called once per growing season from TreeSkeleton.StartNewGrowingSeason.
    /// Updates degradation, waterlogging, root rot, and species mismatch penalties.
    /// </summary>
    public void SeasonTick(TreeSkeleton skeleton)
    {
        seasonsSinceRepot++;

        // Fresh-repot compaction loosens fast — mostly gone after one season
        compaction = Mathf.Max(0f, compaction - 0.6f);

        // Degradation — slows after a few seasons as the mix stabilises
        float degradeThisSeason = baseDegradeRate;
        if (seasonsSinceRepot > degradationSlowdownSeasons)
            degradeThisSeason *= 0.5f;
        soilDegradation = Mathf.Min(1f, soilDegradation + degradeThisSeason * (1f - soilDegradation));

        // Waterlogging — builds when drainage is poor and soil stayed wet
        float effectiveDrain = EffectiveDrainageRate;
        if (skeleton.soilMoisture > 0.85f && effectiveDrain < 0.45f)
        {
            float satRate = (0.45f - effectiveDrain) * 0.5f;
            saturationLevel = Mathf.Min(1f, saturationLevel + satRate);
        }
        else
        {
            // Drain saturation when conditions improve
            saturationLevel = Mathf.Max(0f, saturationLevel - 0.10f);
        }

        // Root rot damage from sustained waterlogging
        if (saturationLevel > 0.5f)
        {
            float rotDmg = rootRotDamagePerSeason * (saturationLevel - 0.5f) * 2f;
            int rotCount = 0;
            foreach (var node in skeleton.allNodes)
            {
                if (!node.isRoot || node.isTrimmed) continue;
                skeleton.ApplyDamage(node, DamageType.RootRot, rotDmg);
                rotCount++;
            }
            if (verboseLog) Debug.Log($"[Soil] Root rot dmg={rotDmg:F3} on {rotCount} root nodes | saturation={saturationLevel:F2} | year={GameManager.year}");
        }

        // Species soil mismatch penalty
        var sp = skeleton.species;
        if (sp != null)
        {
            float retentionDelta  = Mathf.Abs(EffectiveWaterRetention - sp.preferredWaterRetention);
            float drainDelta      = Mathf.Abs(EffectiveDrainageRate   - sp.preferredDrainageRate);
            float aerationDelta   = Mathf.Abs(aerationScore            - sp.preferredAerationScore);
            float maxDelta        = Mathf.Max(retentionDelta, Mathf.Max(drainDelta, aerationDelta));

            if (maxDelta > sp.soilToleranceRange)
            {
                float severity = (maxDelta - sp.soilToleranceRange) / (1f - sp.soilToleranceRange);
                float penalty  = sp.soilMismatchPenalty * severity;
                foreach (var node in skeleton.allNodes)
                    if (!node.isTrimmed)
                        skeleton.ApplyDamage(node, DamageType.NutrientLack, penalty);
                if (verboseLog) Debug.Log($"[Soil] Soil mismatch penalty={penalty:F4} (delta={maxDelta:F2}, tol={sp.soilToleranceRange:F2}) | year={GameManager.year}");
            }
        }

        if (verboseLog) Debug.Log($"[Soil] SeasonTick | degradation={soilDegradation:F2} saturation={saturationLevel:F2} " +
                  $"effectiveDrain={EffectiveDrainageRate:F2} drainMult={DrainRateMultiplier:F2} | year={GameManager.year}");
    }

    /// <summary>
    /// Repot with a new soil preset and optional pot size. Resets degradation and
    /// saturation; applies root stress damage scaled by the species' repot tolerance.
    /// </summary>
    public void Repot(TreeSkeleton skeleton, SoilPreset newPreset,
                      PotSize newSize = PotSize.M, bool sizeChanged = false)
    {
        int prevSeasonsSinceRepot = seasonsSinceRepot;  // capture before reset

        if (!ProgressionManager.AutomationActive)
            ProgressionManager.Instance?.ReachMilestone("first_repot");   // player repots only, not Quick-Start
        TrainingRecorder.Instance?.RecordAction("Repot", -1, $"{{\"preset\":\"{newPreset}\",\"size\":\"{newSize}\"}}");

        ApplyPreset(newPreset);
        soilDegradation   = 0f;
        saturationLevel   = 0f;
        seasonsSinceRepot = 0;

        // Repotting stands a tipped tree back up — re-seating it in fresh soil is exactly how a
        // grower recovers one, and a bigger pot is the fix the player was being nudged toward.
        skeleton.RightTree();

        if (sizeChanged)
        {
            potSize = newSize;
            var rootArea = skeleton.GetRootAreaTransform();
            ApplyPotSize(rootArea);
            RefreshDrainageHoles();   // rootArea scale changed — re-derive box-local hole discs
            Debug.Log($"[Soil] Pot size → {newSize} | year={GameManager.year}");
        }

        // Repot stress — less damage for tolerant species, worse outside early spring
        float tolerance    = skeleton.species != null ? skeleton.species.repotTolerance : 0.5f;
        float stress       = repotBaseDamage * (1f - tolerance);
        bool  goodTiming   = GameManager.month >= 2 && GameManager.month <= 4;
        if (!goodTiming) stress *= 1.8f;   // repotting outside early spring is harsh

        // Extra stress if repotted too recently — roots haven't had time to re-establish
        if (prevSeasonsSinceRepot > 0 && prevSeasonsSinceRepot < 2)
        {
            stress *= 2.0f;
            Debug.Log($"[Soil] Repotted too soon ({prevSeasonsSinceRepot} seasons since last) — stress doubled.");
        }

        int affected = 0;
        foreach (var node in skeleton.allNodes)
        {
            if (!node.isRoot || !node.isTerminal || node.isTrimmed) continue;
            skeleton.ApplyDamage(node, DamageType.RepotStress, stress);
            affected++;
        }

        Debug.Log($"[Soil] Repotted → {newPreset} | stress={stress:F3} on {affected} root terminals " +
                  $"| goodTiming={goodTiming} | year={GameManager.year} month={GameManager.month}");
    }
}
