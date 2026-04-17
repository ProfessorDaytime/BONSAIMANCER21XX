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
    static readonly (float w, float d, float h)[] PotDimensions =
    {
        (1.2f, 1.0f, 0.6f),   // XS
        (1.8f, 1.5f, 0.8f),   // S
        (2.6f, 2.0f, 1.0f),   // M  ← default
        (3.4f, 2.6f, 1.1f),   // L
        (4.2f, 3.2f, 1.2f),   // XL
        (5.0f, 3.8f, 0.5f),   // Slab — wide, very shallow
    };

    /// <summary>
    /// Resize the rootAreaTransform to match the chosen pot size.
    /// Call this after setting potSize, typically during Repot().
    /// </summary>
    public void ApplyPotSize(Transform rootAreaTransform)
    {
        if (rootAreaTransform == null) return;
        var (w, d, h) = PotDimensions[(int)potSize];
        rootAreaTransform.localScale = new Vector3(w, h, d);
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

    /// <summary>
    /// Effective drainage rate accounting for compaction.
    /// </summary>
    public float EffectiveDrainageRate => drainageRate * (1f - soilDegradation * 0.70f);

    /// <summary>
    /// Effective water retention accounting for compaction (degraded soil holds more water).
    /// </summary>
    public float EffectiveWaterRetention => Mathf.Min(1f, waterRetention + soilDegradation * 0.25f);

    /// <summary>
    /// Drain rate multiplier applied to TreeSkeleton.drainRatePerDay.
    /// High retention = slower drain (multiplier &lt; 1).
    /// Low retention = faster drain (multiplier &gt; 1).
    /// Range: ~0.35 (very retentive) → ~1.55 (very free-draining).
    /// </summary>
    public float DrainRateMultiplier => Mathf.Lerp(1.55f, 0.35f, EffectiveWaterRetention);

    /// <summary>
    /// Called once per growing season from TreeSkeleton.StartNewGrowingSeason.
    /// Updates degradation, waterlogging, root rot, and species mismatch penalties.
    /// </summary>
    public void SeasonTick(TreeSkeleton skeleton)
    {
        seasonsSinceRepot++;

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
            Debug.Log($"[Soil] Root rot dmg={rotDmg:F3} on {rotCount} root nodes | saturation={saturationLevel:F2} | year={GameManager.year}");
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
                Debug.Log($"[Soil] Soil mismatch penalty={penalty:F4} (delta={maxDelta:F2}, tol={sp.soilToleranceRange:F2}) | year={GameManager.year}");
            }
        }

        Debug.Log($"[Soil] SeasonTick | degradation={soilDegradation:F2} saturation={saturationLevel:F2} " +
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

        ApplyPreset(newPreset);
        soilDegradation   = 0f;
        saturationLevel   = 0f;
        seasonsSinceRepot = 0;

        if (sizeChanged)
        {
            potSize = newSize;
            var rootArea = skeleton.GetRootAreaTransform();
            ApplyPotSize(rootArea);
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
