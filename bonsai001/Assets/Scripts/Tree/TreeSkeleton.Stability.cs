using UnityEngine;

/// <summary>
/// Tipping stability — whether the tree's own weight overturns its pot.
///
/// Real bonsai physics: the canopy's weight acting out to one side creates an OVERTURNING moment
/// about the pot's base edge, resisted by a RIGHTING moment from the ballast (pot shell + soil +
/// held water) acting at the footprint radius. When the overturning moment wins, the whole
/// planting goes over — pot and all, which is how it happens in life.
///
/// Why the soil matters so much: a saturated bonsai mix carries ~40% of its volume in water, so a
/// freshly watered pot weighs ~1.5× its dry self. Watering genuinely stabilises the tree, and the
/// dangerous case is the DRY pot — exactly what growers guard against before wind. The mix choice
/// matters too: free-draining pumice/perlite is best for roots but lightest, so the horticulturally
/// "good" soil is the tippiest (see PotSoil.MixDryDensity).
///
/// Deliberately NOT rigid-body physics: the tree is a procedural mesh rebuilt constantly, so a
/// real collider/rigidbody would fight the growth system. This is an analytic moment balance
/// evaluated per season, with a visual lean driven off the same margin.
/// </summary>
public partial class TreeSkeleton
{
    [Header("Stability / Tipping")]
    [Tooltip("Enable tipping. Off = trees never fall over regardless of how top-heavy they get.")]
    [SerializeField] bool enableTipping = true;

    [Tooltip("Green wood density, g/cm³. Everything here is in GRAMS via PotSoil.WorldToCm3, so " +
             "canopy and ballast are directly comparable — an earlier arbitrary-units version made " +
             "the canopy outweigh its own soil ~50×, which tipped every tree instantly.")]
    // NOT the existing `woodDensity` (=12) in TreeSkeleton.cs — that one is an arbitrary-unit
    // constant for the elastic-sag mass and is unrelated to real density.
    [SerializeField] float woodDensityGramsCm3 = 0.75f;

    [Tooltip("Mass of a single leaf in grams. A wet maple leaf is roughly 0.3 g.")]
    [SerializeField] float leafGramsEach = 0.3f;

    [Tooltip("Safety factor on the righting moment. Above 1 the tree tolerates being somewhat " +
             "top-heavy before going over — pots grip the bench, roots anchor, and a tree that " +
             "tips the instant the maths crosses 1.0 feels unfair.")]
    [SerializeField] [Range(1f, 3f)] float tipSafetyFactor = 1.35f;

    [Tooltip("Stability ratio below which the tree visibly leans as a warning, before it tips.")]
    [SerializeField] [Range(1f, 3f)] float leanWarnRatio = 1.8f;

    [Tooltip("Maximum warning lean, degrees, as stability approaches the tipping point.")]
    [SerializeField] [Range(0f, 25f)] float maxWarnLeanDeg = 9f;

    [Tooltip("An anchor wire through the drainage holes ties the tree into the pot — the real " +
             "counter-move. Multiplies the righting moment while set.")]
    [SerializeField] [Range(1f, 4f)] float anchorWireBonus = 2.2f;

    /// <summary>True while the tree is tied into its pot with an anchor wire.</summary>
    public bool AnchorWireSet { get; set; }

    /// <summary>True once the tree has actually gone over. Latched until replanted/repotted.</summary>
    public bool HasTipped { get; private set; }

    // Cached readout for the UI, refreshed by EvaluateStability.
    public struct StabilityReport
    {
        public float canopyMass;        // total above-ground mass (wood + foliage)
        public float ballastMass;       // pot shell + soil + held water
        public float comHeight;         // canopy centre-of-mass height above the soil line
        public float comOffset;         // horizontal distance of the CoM from the trunk axis
        public float overturning;       // canopy moment about the base edge
        public float righting;          // ballast moment about the same edge
        public float ratio;             // righting / overturning — below 1 it goes over
        public float leanDeg;           // current warning lean
        public bool  wetBonusActive;    // soil currently holding meaningful water
    }

    public StabilityReport Stability { get; private set; }

    /// <summary>Canopy mass and centre of mass, in skeleton-local space, measured from the soil
    /// line. Wood mass is volume (πr²ℓ) × density; foliage rides the existing leaf-mass units.</summary>
    void ComputeCanopyMass(out float mass, out Vector3 com)
    {
        mass = 0f;
        Vector3 weighted = Vector3.zero;
        _canopyWidth = _canopyHeight = 0f;
        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        _foliageCount = 0;

        foreach (var node in allNodes)
        {
            if (node.isRoot || node.isTrimmed) continue;

            // Grams: segment volume (world³) → cm³ → × green-wood density.
            float wood = Mathf.PI * node.radius * node.radius * node.length
                         * PotSoil.WorldToCm3 * woodDensityGramsCm3;
            float leaf = leafMgr != null ? leafMgr.NodeLeafCount(node.id) * leafGramsEach : 0f;
            float m    = wood + leaf;
            if (m <= 0f) continue;

            // Segment mass acts at its midpoint; foliage at the tip. Close enough, and it keeps
            // a long horizontal branch from over-reading as if all its mass sat at the far end.
            Vector3 at = wood >= leaf
                ? (node.worldPosition + node.tipPosition) * 0.5f
                : node.tipPosition;

            mass     += m;
            weighted += at * m;

            // Silhouette extents — the canopy's frontal area is what the wind pushes on.
            Vector3 t = node.tipPosition;
            if (t.x < minX) minX = t.x; if (t.x > maxX) maxX = t.x;
            if (t.z < minZ) minZ = t.z; if (t.z > maxZ) maxZ = t.z;
            if (t.y < minY) minY = t.y; if (t.y > maxY) maxY = t.y;
            if (leafMgr != null) _foliageCount += leafMgr.NodeLeafCount(node.id);
        }

        if (maxX > minX) _canopyWidth  = Mathf.Max(maxX - minX, maxZ - minZ);
        if (maxY > minY) _canopyHeight = maxY - minY;

        com = mass > 0.0001f ? weighted / mass : Vector3.zero;
    }

    float _canopyWidth, _canopyHeight;
    int   _foliageCount;

    /// <summary>Lateral wind force on the canopy, in grams-force.
    ///
    /// This is the term that makes tipping possible at all. A tree's own weight cannot overturn its
    /// pot — statically it only tips once its centre of mass passes outside the pot's base, which a
    /// centred tree never does, and the masses bear that out (a 2 kg pot vs a 168 g tree). Real
    /// potted trees blow over; they do not fall over. Measured 2026-08-24.
    ///
    /// Drag: F = ½ρv²·Cd·A, converted N → gf (×101.97). A leafy canopy is a far better sail than a
    /// bare winter one, so foliage scales the effective area — which is why a deciduous tree
    /// survives a winter gale that would have taken it in summer.</summary>
    float WindForceGf(out float speed)
    {
        speed = 0f;
        var wind = WindManager.Instance;
        if (!windLoadEnabled || wind == null || wind.strength <= 0f) return 0f;

        speed = wind.strength * stormWindSpeed * Mathf.Lerp(0.35f, 1f, wind.GustLevel);
        var weather = WeatherManager.Instance;
        if (WeatherManager.Sheltered && weather != null) speed *= weather.shelterWindFactor;

        // Frontal area in m². World units → cm (×19.28) → m (÷100).
        float unitToM = Mathf.Pow(PotSoil.WorldToCm3, 1f / 3f) * 0.01f;
        float areaM2  = _canopyWidth * _canopyHeight * unitToM * unitToM;

        // Foliage fills the silhouette; bare branches let most of the wind through.
        float density = Mathf.Clamp01(0.18f + _foliageCount / Mathf.Max(1f, foliageFullSail));
        areaM2 *= Mathf.Min(1f, density) * canopySolidity;

        return 0.5f * 1.225f * speed * speed * windDragCoefficient * areaM2 * 101.97f;
    }

    [Header("Wind Load")]
    [Tooltip("Include wind in the tipping balance. Without it trees are effectively untippable, " +
             "because gravity alone cannot overturn a pot heavier than the tree.")]
    [SerializeField] bool windLoadEnabled = true;

    [Tooltip("Wind speed in m/s at full strength and a peak gust. 15–20 is a real storm; a small " +
             "potted tree goes over around there, which matches life.")]
    [SerializeField] float stormWindSpeed = 16f;

    [Tooltip("Drag coefficient of a canopy — a porous, streamlining shape, so well under a flat plate.")]
    [SerializeField] float windDragCoefficient = 0.8f;

    [Tooltip("Fraction of its bounding silhouette a full canopy actually blocks.")]
    [Range(0.1f, 1f)] [SerializeField] float canopySolidity = 0.55f;

    [Tooltip("Leaf count at which the canopy is a full sail. Below this it is partly transparent.")]
    [SerializeField] float foliageFullSail = 400f;

    /// <summary>Recomputes the moment balance and applies the visual consequence. Called each
    /// season change — cheap enough there, and stability only moves as the tree grows.</summary>
    public void EvaluateStability()
    {
        if (!enableTipping || root == null || allNodes.Count == 0) return;

        var potSoil = _holePotSoil != null ? _holePotSoil : GetComponent<PotSoil>();
        if (potSoil == null) return;

        ComputeCanopyMass(out float canopyMass, out Vector3 com);
        if (canopyMass <= 0.0001f) return;

        // Heights measured from the trunk base (the soil line, since node space starts there).
        float comHeight = Mathf.Max(0f, com.y - root.worldPosition.y);
        float comOffset = new Vector2(com.x - root.worldPosition.x,
                                      com.z - root.worldPosition.z).magnitude;

        float footprint   = Mathf.Max(0.01f, potSoil.FootprintRadius);
        float ballastMass = potSoil.PotShellMass + potSoil.SoilMass(soilMoisture);

        // Moments about the downhill base edge. The canopy's lever arm is how far its CoM
        // overhangs that edge; the ballast always acts at the footprint radius.
        float leverArm    = Mathf.Max(0f, comOffset - footprint);
        float windGf      = WindForceGf(out float windSpeed);
        float overturning = canopyMass * leverArm
                          + canopyMass * comHeight * 0.04f    // slenderness: tall = precarious
                          + windGf * comHeight;               // wind acts at the canopy's centre
        float righting    = ballastMass * footprint
                          * (AnchorWireSet ? anchorWireBonus : 1f);

        float ratio = overturning > 0.0001f
            ? righting / overturning * tipSafetyFactor
            : 999f;

        // Warning lean: below leanWarnRatio the tree visibly leans toward its overhang, giving
        // the player a read on danger before anything is lost.
        float lean = 0f;
        if (ratio < leanWarnRatio)
            lean = Mathf.Lerp(maxWarnLeanDeg, 0f, Mathf.InverseLerp(1f, leanWarnRatio, ratio));

        Stability = new StabilityReport
        {
            canopyMass     = canopyMass,
            ballastMass    = ballastMass,
            comHeight      = comHeight,
            comOffset      = comOffset,
            overturning    = overturning,
            righting       = righting,
            ratio          = ratio,
            leanDeg        = lean,
            wetBonusActive = soilMoisture > 0.35f,
        };

        // Season-cadence tuning readout. woodDensityGramsCm3 and tipSafetyFactor are estimates until
        // measured against real runs, and without this the system is silent unless a tree falls.
        Debug.Log($"[Stab] ratio={ratio:F2} lean={lean:F1}° | canopy={canopyMass:F1} " +
                  $"(com h={comHeight:F2} reach={comOffset:F2}) | ballast={ballastMass:F1} " +
                  $"(soil={potSoil.SoilMass(soilMoisture):F1} shell={potSoil.PotShellMass:F1} " +
                  $"moist={soilMoisture:F2}) | footprint={footprint:F3} lever={leverArm:F3} " +
                  $"| wind={windGf:F0}gf @{windSpeed:F1}m/s (sail {_canopyWidth:F2}×{_canopyHeight:F2}, " +
                  $"leaves={_foliageCount}) | over={overturning:F1} right={righting:F1}");

        if (ratio < 1f && !HasTipped) TipOver(com);
        else ApplyWarningLean(lean, com);
    }

    /// <summary>Visual lean toward the overhang — the tree is not falling, just showing strain.
    /// Applied to the tree transform's rotation only, so nothing about growth or seating moves.</summary>
    void ApplyWarningLean(float leanDeg, Vector3 com)
    {
        if (leanDeg <= 0.01f)
        {
            if (_leanApplied != 0f)
            {
                transform.rotation = _uprightRotation;
                _leanApplied = 0f;
            }
            return;
        }

        if (_leanApplied == 0f) _uprightRotation = transform.rotation;

        Vector3 overhang = new Vector3(com.x - root.worldPosition.x, 0f, com.z - root.worldPosition.z);
        if (overhang.sqrMagnitude < 1e-8f) return;

        Vector3 axis = Vector3.Cross(Vector3.up, transform.TransformDirection(overhang).normalized);
        if (axis.sqrMagnitude < 1e-8f) return;

        transform.rotation = Quaternion.AngleAxis(leanDeg, axis.normalized) * _uprightRotation;
        _leanApplied = leanDeg;
    }

    Quaternion _uprightRotation = Quaternion.identity;
    float      _leanApplied;

    /// <summary>The tree goes over. Pot and all — a tipped planting lies on its side, it does not
    /// launch the tree out of its pot. Latched: it stays down until the player repots/rights it.</summary>
    void TipOver(Vector3 com)
    {
        HasTipped = true;

        Vector3 overhang = new Vector3(com.x - root.worldPosition.x, 0f, com.z - root.worldPosition.z);
        if (overhang.sqrMagnitude < 1e-8f) overhang = Vector3.forward;

        Vector3 axis = Vector3.Cross(Vector3.up, transform.TransformDirection(overhang).normalized);
        if (axis.sqrMagnitude < 1e-8f) axis = Vector3.right;

        if (_leanApplied == 0f) _uprightRotation = transform.rotation;

        const float tipDeg = 78f;
        // Pivot on the downhill base edge of the pot — the point it actually rolls over.
        var soil = _holePotSoil != null ? _holePotSoil : GetComponent<PotSoil>();
        Vector3 baseWorld = transform.TransformPoint(root.worldPosition);
        if (soil != null)
        {
            baseWorld -= Vector3.up * soil.CurrentPotHeight;
            baseWorld += overhang.normalized * soil.FootprintRadius;
        }

        transform.rotation = Quaternion.AngleAxis(tipDeg, axis.normalized) * _uprightRotation;
        _leanApplied = tipDeg;

        // The pot and soil are separate objects — take them over too, or the tree appears to
        // vault out of a pot that stays neatly upright.
        soil?.TipVisuals(baseWorld, axis.normalized, tipDeg);

        CareLog.Add("Tipped", "The tree overbalanced and went over — too much canopy for the pot");
        Debug.LogWarning($"[Stability] TIPPED | ratio={Stability.ratio:F2} " +
                         $"canopy={Stability.canopyMass:F1} ballast={Stability.ballastMass:F1} " +
                         $"comOffset={Stability.comOffset:F2} footprint={_holePotSoil?.FootprintRadius:F2}");
    }

    /// <summary>Stand the tree back up — after a repot, a bigger pot, or an anchor wire.</summary>
    public void RightTree()
    {
        if (!HasTipped && _leanApplied == 0f) return;
        HasTipped = false;
        transform.rotation = _uprightRotation;
        _leanApplied = 0f;
        (_holePotSoil != null ? _holePotSoil : GetComponent<PotSoil>())?.RestoreVisuals();
        CareLog.Add("Righted", "The tree was stood back up and re-seated");
    }
}
