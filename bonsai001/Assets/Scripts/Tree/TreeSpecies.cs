using UnityEngine;

/// <summary>
/// ScriptableObject that defines all species-specific parameters for a bonsai tree.
///
/// Assign one to TreeSkeleton.species. On Awake(), TreeSkeleton copies these values
/// into its local fields so all existing code continues to work unchanged.
///
/// Create via: right-click in Project → Create → Bonsai → Tree Species
/// </summary>
[CreateAssetMenu(fileName = "NewSpecies", menuName = "Bonsai/Tree Species")]
public class TreeSpecies : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Display name shown in the UI.")]
    public string speciesName = "Unknown Species";

    [Tooltip("Scientific name for flavour.")]
    public string scientificName = "";

    // ── Growth ────────────────────────────────────────────────────────────────

    [Header("Growth Speed")]
    [Tooltip("How many local units per in-game day a depth-0 segment grows.")]
    public float baseGrowSpeed = 0.2f;

    [Tooltip("Speed multiplier per depth level — deeper branches grow slower.")]
    public float depthSpeedDecay = 0.85f;

    [Tooltip("How many depth levels the tree can grow per year.")]
    public int depthsPerYear = 3;

    [Tooltip("Day of year (1-336) when extension growth begins slowing. Default ~day 150 = late May.")]
    public int growthSlowDay = 150;

    [Tooltip("Day of year (1-336) when extension growth fully stops. Default ~day 180 = late June.")]
    public int growthStopDay = 180;

    [Tooltip("Fraction of targetLength added to each existing segment per year at depth 0.")]
    public float baseElongation = 0.05f;

    [Tooltip("Multiplier applied to baseElongation per depth level (0–1).")]
    [Range(0f, 1f)] public float elongationDepthDecay = 0.7f;

    // ── Segment Lengths ───────────────────────────────────────────────────────

    [Header("Segment Lengths")]
    [Tooltip("Target length of each branch chord.")]
    public float branchSegmentLength = 1.0f;

    [Tooltip("Each depth level, branch segments get this much shorter (0–1).")]
    public float segmentLengthDecay = 0.80f;

    [Tooltip("Target length of each root segment.")]
    public float rootSegmentLength = 0.7f;

    // ── Branching ─────────────────────────────────────────────────────────────

    [Header("Branching")]
    [Tooltip("Probability per segment of spawning a lateral branch at depth 0.")]
    public float baseBranchChance = 0.75f;

    [Tooltip("Branch chance decreases with depth.")]
    public float branchChanceDepthDecay = 0.90f;

    [Tooltip("Probability of a lateral branch spawning each spring.")]
    public float springLateralChance = 0.80f;

    [Tooltip("Chance per interior junction node per spring to sprout a new lateral spontaneously.")]
    [Range(0f, 0.5f)] public float oldWoodBudChance = 0.01f;

    [Tooltip("Base chance per stimulated ancestor node to sprout a back-bud each spring.")]
    [Range(0f, 1f)] public float backBudBaseChance = 0.15f;

    // ── Bud Type ──────────────────────────────────────────────────────────────

    [Header("Bud Type")]
    [Tooltip("Alternate: one continuation + optional lateral (most trees).\n" +
             "Opposite: two symmetric equal forks (Japanese maple, ash, dogwood).")]
    public BudType budType = BudType.Alternate;

    [Tooltip("How strongly the terminal bud suppresses lateral shoots (0 = equal vigor, 1 = laterals don't grow).")]
    [Range(0f, 1f)] public float apicalDominance = 0.3f;

    // ── Per-Branch Vigor ──────────────────────────────────────────────────────

    [Header("Per-Branch Vigor")]
    [Tooltip("Vigor added each spring per node, scaled by 1/depth.")]
    public float apicalVigorBonus = 0.15f;

    [Tooltip("Each spring, vigor blends this fraction toward 1.0 (regression to mean).")]
    [Range(0f, 0.5f)] public float vigorDecayRate = 0.15f;

    // ── Wiring ────────────────────────────────────────────────────────────────

    [Header("Wiring")]
    [Tooltip("Rate-adjusted in-game days for a wire to fully set.\n" +
             "Higher = wood is harder to train but holds shape longer once set.\n" +
             "Juniper ≈ 280  |  maple ≈ 140  |  pine ≈ 350")]
    public float wireDaysToSet = 196f;

    // ── Wound Tolerance ───────────────────────────────────────────────────────

    [Header("Wound Tolerance")]
    [Tooltip("Health drained from a wounded node per growing season (no paste).\n" +
             "Higher = more fragile after cuts. Maple ≈ 0.07  |  juniper ≈ 0.03")]
    public float woundDrainRate = 0.05f;

    [Tooltip("Growing seasons to fully callus over one unit of wound radius.")]
    public float seasonsToHealPerUnit = 20f;

    [Tooltip("Health hit applied to the cut-site node each time it is trimmed.")]
    public float trimTraumaDamage = 0.05f;

    [Tooltip("Health recovered per growing season on non-root damaged nodes.")]
    public float trimTraumaRecoveryPerSeason = 0.04f;

    // ── Water Needs ───────────────────────────────────────────────────────────

    [Header("Water Needs")]
    [Tooltip("Moisture lost per in-game day while actively growing.\n" +
             "Higher = more frequent watering needed. Maple ≈ 0.14  |  juniper ≈ 0.07")]
    public float drainRatePerDay = 0.1f;

    [Tooltip("Moisture level below which drought stress begins.")]
    [Range(0f, 1f)] public float droughtThreshold = 0.3f;

    [Tooltip("Health lost per in-game day spent below drought threshold.")]
    public float droughtDamagePerDay = 0.008f;

    // ── Nutrients ─────────────────────────────────────────────────────────────

    [Header("Nutrients")]
    [Tooltip("Nutrient units drained each spring. Fast-growing species need more food.")]
    public float nutrientDrainPerSeason = 0.4f;

    // ── Fungal Resistance ─────────────────────────────────────────────────────

    [Header("Fungal Resistance")]
    [Tooltip("Chance per season that an infected node spreads fungalLoad to each adjacent node.\n" +
             "Higher = disease spreads faster in this species.")]
    [Range(0f, 1f)] public float fungalSpreadChance = 0.25f;

    [Tooltip("fungalLoad reduced per season when a node is no longer at risk.\n" +
             "Higher = this species shakes off infection faster.")]
    public float fungalRecoveryRate = 0.10f;

    // ── Leaf Energy ───────────────────────────────────────────────────────────

    [Header("Leaf Energy")]
    [Tooltip("Maximum energy multiplier from a full, healthy canopy.\n" +
             "Dense-leafed species (maple) can push 1.5+; sparse species (literati pine) stay closer to 1.0.")]
    public float maxEnergyMultiplier = 1.5f;

    // ── Soil Preferences ──────────────────────────────────────────────────────

    [Header("Soil Preferences")]
    [Tooltip("Ideal soil water retention (0=fast-draining, 1=moisture-retaining).\n" +
             "Maple ≈ 0.55  |  juniper ≈ 0.30  |  azalea ≈ 0.65")]
    [Range(0f, 1f)] public float preferredWaterRetention = 0.50f;

    [Tooltip("Ideal soil drainage rate (0=waterlogged, 1=free-draining).\n" +
             "Maple ≈ 0.55  |  juniper ≈ 0.75  |  azalea ≈ 0.50")]
    [Range(0f, 1f)] public float preferredDrainageRate = 0.55f;

    [Tooltip("Ideal soil aeration score (0=compacted, 1=very open).\n" +
             "Most trees ≈ 0.50  |  junipers prefer higher ≈ 0.65")]
    [Range(0f, 1f)] public float preferredAerationScore = 0.50f;

    [Tooltip("How far from any preferred value before penalties start.\n" +
             "0.2 = penalties kick in when the soil is 0.2 units away from ideal.")]
    [Range(0.05f, 0.5f)] public float soilToleranceRange = 0.20f;

    [Tooltip("Health penalty applied to all nodes each spring when soil is outside tolerance on any axis.\n" +
             "0.03 = mild yellowing; 0.08 = noticeable stunting.")]
    public float soilMismatchPenalty = 0.03f;

    [Tooltip("How much repotting this species tolerates. Higher = less RepotStress damage.\n" +
             "Juniper ≈ 0.8 (repot-hardy)  |  maple ≈ 0.5 (moderate)  |  pine ≈ 0.3 (sensitive)")]
    [Range(0.1f, 1f)] public float repotTolerance = 0.5f;

    // ── Visuals ───────────────────────────────────────────────────────────────

    [Header("Visuals")]
    [Tooltip("Bark type 1-18 from botanical classification.\n" +
             "1=Smooth  2=Fine fissures  4=Interlacing ridges  7=Vertical strips\n" +
             "8=Irregular blocks  9=Large plates  10=Peeling strips  12=Fibrous shreds\n" +
             "14=Spongy fibrous  16=Horizontal lenticels\n" +
             "See memory/reference_bark_types.md for full table.")]
    [Range(1, 18)] public int barkType = 2;

    [Tooltip("Bark color on thin, new-growth twigs (maps to shader _NGColor).\n" +
             "Deciduous: fresh green.  Pine/juniper: yellow-green.  Deadwood: grey.")]
    public Color youngBarkColor = new Color(0.42f, 0.62f, 0.25f);

    [Tooltip("Bark color on thick, mature branches (maps to shader _BarkColor).\n" +
             "Most trees: warm grey-brown.  Cherry: distinctive grey-pink.")]
    public Color matureBarkColor = new Color(0.45f, 0.38f, 0.30f);

    [Tooltip("New-growth color on exposed roots (maps to shader _NGRootColor).\n" +
             "Typically pale tan / cream.")]
    public Color rootNewGrowthColor = new Color(0.82f, 0.75f, 0.58f);

    [Tooltip("Leaf color during spring and summer (non-fall season).\n" +
             "Maple: bright green.  Pine: dark blue-green.  Wisteria: medium green.")]
    public Color leafSpringColor = new Color(0.15f, 0.55f, 0.10f);
}
