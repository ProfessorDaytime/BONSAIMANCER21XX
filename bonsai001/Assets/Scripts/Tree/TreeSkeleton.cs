using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.InputSystem;
using Debug = UnityEngine.Debug;
using Random = UnityEngine.Random;

/// <summary>
/// Owns and manages the tree graph. Drives growth, branching, and secondary thickening.
///
/// Growth model:
///   - Each terminal node (isGrowing = true) extends its length toward targetLength each frame.
///   - When it reaches targetLength it spawns children: always one continuation (apical
///     meristem) and probabilistically one lateral branch.
///   - After any structural change, RecalculateRadii() walks bottom-up and applies da Vinci's
///     pipe model: parent.radius^2 = sum(child.radius^2). This thickens the trunk automatically
///     as more branches accumulate.
///   - Growth direction blends inertia + phototropism (sun bias) + random perturbation.
///   - Growth only ticks when isGrowing = true (set by BranchGrow game state).
/// </summary>
public class TreeSkeleton : MonoBehaviour
{
    // ── Species ───────────────────────────────────────────────────────────────

    [Header("Branch Weight & Sag")]
    [Tooltip("When false, no sag or junction stress is computed. Off by default.")]
    [SerializeField] public bool branchWeightEnabled = false;

    [Tooltip("Density of wood in the load formula: load = radius² × length × density. " +
             "Tune until heavy branches visibly droop over 2–3 seasons.")]
    [SerializeField] float woodDensity = 12f;

    [Tooltip("Hardness factor for strength: strength = radius³ × woodHardness × maturity. " +
             "Higher = harder to sag. Junipers ≈ 1.4, maples ≈ 0.9.")]
    [SerializeField] float woodHardness = 1.0f;

    [Tooltip("load/strength ratio above which sag starts accumulating.")]
    [SerializeField] float sagThreshold = 1.0f;

    [Tooltip("Scales how fast sag angle accumulates above sagThreshold.")]
    [SerializeField] float sagSensitivity = 8f;

    [Tooltip("Maximum sag angle in degrees a branch can accumulate over its lifetime.")]
    [SerializeField] float maxSagAngleDeg = 35f;

    [Tooltip("How strongly accumulated sag blends the growDirection downward per spring. " +
             "1 = fully sagged at maxSagAngleDeg, 0 = no visual effect.")]
    [SerializeField] float sagBlend = 0.6f;

    [Tooltip("Seasons for a new branch to reach full wood maturity (maximum strength).")]
    [SerializeField] int matureAgeSeasons = 8;

    [Tooltip("load/strength ratio above which junction stress damage is applied to the parent node.")]
    [SerializeField] float junctionStressThreshold = 2.5f;

    [Tooltip("Health damage to parent node per season when a child exceeds junctionStressThreshold.")]
    [SerializeField] float junctionStressDamage = 0.02f;

    [Header("Winter Pruning")]
    [Tooltip("Winter cut forced-dormancy skip: cuts made in months 11–2 at or below this depth get regrowthSeasonCount set to 2, skipping winter + giving a slow spring start. 0 = disabled.")]
    [SerializeField] int winterDormantDepthThreshold = 3;

    [Tooltip("Total trimmed depth in one season that counts as 'heavy pruning' and triggers reserve depletion next spring.")]
    [SerializeField] int heavyPruneThreshold = 6;

    [Tooltip("Growth multiplier applied for one spring after heavy winter pruning (reserve depletion). Default 0.5 = half speed.")]
    [SerializeField] float heavyPruneRecoveryScale = 0.5f;

    [Tooltip("Severity ratio (cut depth / SeasonDepthCap) above which regrowth rate is scaled down for the first recovery season.")]
    [SerializeField] float severeCutSeverityThreshold = 0.8f;

    [Tooltip("Regrowth rate multiplier applied during the first recovery season for a severe cut (severity > threshold).")]
    [SerializeField] float severeCutRecoveryScale = 0.7f;

    // Runtime winter pruning state
    int  cutDepthAccumulatedThisSeason;   // resets each spring; tracks heavy pruning
    bool heavyPruneRecoveryActive;        // true for one spring after a heavy prune season

    [Header("Branch Dieback")]
    [Tooltip("When false, branches never die or drop — all dieback checks skipped. Off by default.")]
    [SerializeField] public bool branchDiebackEnabled = false;

    [Tooltip("Radius at or below this value = small branch; drops off after deadSeasonsToDrop seasons.")]
    [SerializeField] float diebackThinRadius = 0.06f;

    [Tooltip("Seasons a small dead branch lingers before being removed.")]
    [SerializeField] int deadSeasonsToDrop = 2;

    [Tooltip("Seasons an interior branch can be fully shaded (no leaf descendants) before it starts dying.")]
    [SerializeField] int shadingToleranceSeasons = 2;

    [Tooltip("Health damage applied per season to a shaded branch above the shading tolerance.")]
    [SerializeField] float shadingDamagePerSeason = 0.15f;

    [Header("Tree Death")]
    [Tooltip("When false, the tree cannot die — all death checks are skipped. Off by default; enable for testing.")]
    [SerializeField] public bool treeDeathEnabled = false;

    [Tooltip("Seasons where average trunk health < criticalHealthThreshold before death is triggered.")]
    [SerializeField] int criticalSeasonsTodie = 2;

    [Tooltip("Average trunk health below this value counts as a critical season.")]
    [SerializeField] float criticalHealthThreshold = 0.10f;

    [Tooltip("Consecutive in-game days at moisture == 0 that trigger immediate death.")]
    [SerializeField] float droughtDeathDays = 60f;

    [Tooltip("Minimum living root mass (node count) before the tree collapses from root loss.")]
    [SerializeField] int minimumLivingRootNodes = 2;

    // Runtime death tracking
    [HideInInspector] public int  consecutiveCriticalSeasons = 0;
    [HideInInspector] public bool treeInDanger  = false;   // warning state — one season left
    [HideInInspector] public bool hasLongRoot   = false;   // set by rake mini-game; drives one extra-long root on RegenerateInitialRoots
    bool wasPotBound = false;                              // tracks first pot-bound trigger for cosmetic surface roots

    [Header("Species")]
    [Tooltip("Drag a TreeSpecies ScriptableObject here. On Awake the species values are " +
             "copied into the fields below, overriding any Inspector defaults.")]
    [SerializeField] public TreeSpecies species;

    /// <summary>Display name of the active species (or 'Unknown' if none assigned).</summary>
    public string SpeciesName => species != null ? species.speciesName : "Unknown";

    /// <summary>How this tree came into existence. Preserved across saves.</summary>
    public TreeOrigin treeOrigin = TreeOrigin.Seedling;

    // Inspector

    [Header("Growth Speed")]
    [Tooltip("How many local units per in-game day a depth-0 segment grows. " +
             "The season length (not maxDepth) controls how much the tree grows each year.")]
    [SerializeField] float baseGrowSpeed = 0.2f;

    [Tooltip("Speed multiplier per depth level -- deeper branches grow slower.")]
    [SerializeField] float depthSpeedDecay = 0.85f;

    [Header("Segment Lengths")]
    [Tooltip("Global scale multiplier applied to all branch and root segment lengths. " +
             "Dial this down to shrink the whole tree without touching species assets. " +
             "0.15 ≈ realistic bonsai scale; 1.0 = raw species values.")]
    [SerializeField] [Range(0.05f, 1f)] float globalSegmentScale = 1f;

    [Tooltip("Target length of each branch chord. Larger = taller, longer-armed tree.")]
    [SerializeField] float branchSegmentLength = 1.0f;

    [Tooltip("Target length of each root segment before it branches. Smaller = denser, earlier splits.")]
    [SerializeField] float rootSegmentLength = 0.7f;

    [Tooltip("Multiplier on root segment length when the root is near or at the tray boundary (distRatio ≥ 0.8). " +
             "Shorter segments = more deflection steps = smoother curves along the wall. 0.25–0.4 recommended.")]
    [SerializeField] [Range(0.1f, 1f)] float wallSegmentScale = 0.35f;

    [Tooltip("Each depth level, branch segments get this much shorter (0..1).")]
    [SerializeField] float segmentLengthDecay = 0.80f;

    [Tooltip("Each depth level, root segments get this much shorter (0..1). " +
             "Keep this higher than segmentLengthDecay so roots stay long enough to reach the spread radius.")]
    [SerializeField] float rootSegmentLengthDecay = 0.95f;

    [Header("Radii")]
    [Tooltip("Fixed radius of every new terminal segment. The pipe model derives all " +
             "parent radii from this -- more branches = thicker trunk automatically.")]
    [SerializeField] float terminalRadius = 0.04f;

    [Tooltip("Starting radius for root segments (much thinner than branches).")]
    [SerializeField] float rootTerminalRadius = 0.004f;

    [Header("Branching")]
    [Tooltip("Probability per segment of spawning a lateral branch (at depth 0).")]
    [SerializeField] float baseBranchChance = 0.75f;

    [Tooltip("Branch chance decreases with depth -- keeps tips from over-branching.")]
    [SerializeField] float branchChanceDepthDecay = 0.90f;

    [Tooltip("Hard safety cap on segment depth.")]
    [SerializeField] int maxDepth = 50;

    [Tooltip("How many depth levels the tree can grow per year. " +
             "Year 1 cap = depthsPerYear. Year 2 = 2xdepthsPerYear. Etc. " +
             "2-3 is realistic for a young tree; 4-5 for faster-growing species.")]
    [SerializeField] int depthsPerYear = 3;

    [Tooltip("Fraction of targetLength added to each existing segment per year. " +
             "Trunk (depth 0) elongates the most; deeper branches decay by elongationDepthDecay.")]
    [SerializeField] float baseElongation = 0.05f;

    [Tooltip("Multiplier applied to baseElongation per depth level (0–1). " +
             "0.7 means depth-1 elongates 70% as much as depth-0, depth-2 elongates 49%, etc.")]
    [SerializeField] [Range(0f, 1f)] float elongationDepthDecay = 0.7f;

    [Tooltip("Probability of a lateral branch spawning each spring (flat rate, not depth-decayed).")]
    [SerializeField] float springLateralChance = 0.80f;

    [Tooltip("Hard cap on total branch nodes. No new branch segments spawn once reached.\n" +
             "Lateral chances also scale toward zero as the tree approaches this cap,\n" +
             "so vigor naturally diminishes with density. Trim regularly to stay vigorous.")]
    [SerializeField] int maxBranchNodes = 2000;

    [Header("Direction")]
    [Tooltip("How strongly each segment continues its parent's direction (0..1).")]
    [SerializeField] float inertiaWeight = 0.65f;

    [Tooltip("Blend fraction toward the sun per new segment (0 = no lean, 1 = point straight up). " +
             "0.01 = nearly imperceptible, 0.1 = gentle lean, 0.5 = strong pull.")]
    [SerializeField] [Range(0f, 1f)] float phototropismWeight = 0.15f;

    [Tooltip("Magnitude of random perturbation per new segment.")]
    [SerializeField] float randomWeight = 0.15f;

    [Tooltip("Max random deviation angle (degrees) for a new branch direction.")]
    [SerializeField] float branchAngleMin = 25f;
    [SerializeField] float branchAngleMax = 55f;

    [Header("Trunk Subdivisions")]
    [Tooltip("Number of individually wire-able segments the initial trunk is split into. " +
             "All share depth 0 -- they don't count toward the branching depth cap.")]
    [SerializeField] int trunkSubdivisions = 3;

    [Tooltip("Number of individually wire-able sub-segments per branch chord. " +
             "Higher = more control points for shaping (S-curves etc). " +
             "Does NOT change total branch length per season — just divides the same chord into more pieces. " +
             "Sub-segments share the parent branch's depth so they don't slow the depth cap.")]
    [SerializeField] [Range(1, 12)] int branchSubdivisions = 6;

    [Tooltip("Minimum length for each sub-segment after subdivision. " +
             "Prevents tip segments from becoming too small to wire. " +
             "Actual segment = max(chordLength/N, minSegmentLength).")]
    [SerializeField] float minSegmentLength = 0.08f;

    [Tooltip("Hard cap on individual branch segment length. " +
             "Long chords are split into however many segments are needed to stay under this length. " +
             "Does not affect total branch length — only adds more wiring/shaping points. " +
             "Set to 0 to disable (use branchSubdivisions count only).")]
    [SerializeField] float maxSegmentLength = 0.35f;

    [Header("Bud System")]
    [Tooltip("Prefab spawned at terminal tips each autumn as a visible dormant bud. Assign in Inspector.")]
    [SerializeField] GameObject budPrefab;

    [Tooltip("Optional prefab for dormant lateral buds on non-terminal nodes. Leave empty to skip.")]
    [SerializeField] GameObject lateralBudPrefab;

    [Tooltip("Alternate: one continuation + one optional lateral per node (most trees).\n" +
             "Opposite: two symmetric equal forks per node (Japanese maple, ash, dogwood).")]
    [SerializeField] BudType budType = BudType.Alternate;

    [Tooltip("How strongly the terminal bud suppresses lateral shoots (0 = equal vigor, 1 = laterals don't grow).\n" +
             "Japanese maple ≈ 0.1  |  most broadleaf trees ≈ 0.3–0.5  |  conifers ≈ 0.7–0.9")]
    [SerializeField] [Range(0f, 1f)] float apicalDominance = 0.3f;

    [Tooltip("Chance per interior junction node per spring to spontaneously sprout a new lateral,\n" +
             "simulating dormant axillary buds breaking on old wood without a trimming event.\n" +
             "Japanese maple ≈ 0.05–0.15  |  most trees ≈ 0–0.02")]
    [SerializeField] [Range(0f, 0.5f)] float oldWoodBudChance = 0.01f;

    [Tooltip("Base chance per stimulated ancestor node to sprout a back-bud each spring.\n" +
             "Independent of springLateralChance so you can tune them separately.\n" +
             "0.15 = ~1 bud from 3 stimulated ancestors on a young tree.")]
    [SerializeField] [Range(0f, 1f)] float backBudBaseChance = 0.15f;

    [Tooltip("Multiplier applied to backBudBaseChance on nodes whose tip ancestry was trimmed (back-budding).")]
    [SerializeField] [Range(1f, 10f)] float backBudActivationBoost = 1f;

    [Tooltip("Spawn the terminal bud prefab at tip nodes each autumn. Disable to hide bud objects without affecting bud growth logic.")]
    [SerializeField] bool showTerminalBuds = true;

    [Tooltip("Spawn the lateral bud prefab at junction nodes each autumn. Disable to hide lateral bud objects without affecting bud growth logic.")]
    [SerializeField] bool showLateralBuds = true;

    [Header("Wiring")]
    [Tooltip("Rate-adjusted in-game days for a wire to fully set (~2 growing seasons at speed 1).")]
    [SerializeField] float wireDaysToSet = 196f;

    [Header("Refinement")]
    [Tooltip("Segment-length multiplier per refinement level. 0.82 = 18% shorter per level; " +
             "at level 6 segments are ~30% of their original length — fine refined twigs.")]
    [SerializeField] [Range(0.5f, 0.99f)] float refinementTaper = 0.82f;

    [Tooltip("Maximum refinement level. Segments stop shortening above this value.")]
    [SerializeField] [Range(2f, 10f)] float refinementCap = 6f;

    [Tooltip("Refinement gained each time a node is the direct cut point of a trim.")]
    [SerializeField] float refinementOnTrim = 0.5f;

    [Tooltip("Refinement gained on a node when one of its back-buds activates in spring.")]
    [SerializeField] float refinementOnBackBud = 0.25f;

    [Tooltip("Average boundaryPressure across root terminals at which leaf miniaturization is maximized. " +
             "Below this → partial shrink; at or above → full shrink factor applied.")]
    [SerializeField] float rootPressureFullRestriction = 8f;

    [Header("Per-Branch Vigor")]
    [Tooltip("Vigor added each spring per node, scaled by 1/depth. Trunk (depth 0) and depth-1 get full bonus; " +
             "deeper nodes get progressively less. Simulates apical dominance.")]
    [SerializeField] float apicalVigorBonus = 0.15f;

    [Tooltip("Each spring, vigor blends this fraction toward 1.0 (regression to mean). " +
             "0.15 = moderate decay; a node at 2.0 drops ~0.15 toward neutral each season.")]
    [SerializeField] [Range(0f, 0.5f)] float vigorDecayRate = 0.15f;

    [Tooltip("Multiplier applied to a node's branchVigor when it is the direct trim cut point. " +
             "0.7 = 30% reduction — cutting the tip weakens that branch.")]
    [SerializeField] [Range(0.1f, 1f)] float vigorTrimMultiplier = 0.7f;

    [Tooltip("Minimum branchVigor a node can reach regardless of damage or trimming.")]
    [SerializeField] float vigorMin = 0.2f;

    [Tooltip("Maximum branchVigor a node can reach. Keeps runaway apical growth in check.")]
    [SerializeField] float vigorMax = 2.0f;

    [Header("Watering")]
    [Tooltip("Moisture lost per in-game day while the tree is actively growing. " +
             "At 0.1 the soil dries out in ~10 in-game days without watering.")]
    [SerializeField] float drainRatePerDay = 0.1f;

    [Tooltip("Moisture level below which drought stress begins accumulating (0→1).")]
    [SerializeField] [Range(0f, 1f)] float droughtThreshold = 0.3f;

    [Tooltip("Health lost per in-game day spent below the drought threshold. " +
             "Applies to all branch nodes at the start of the next growing season.")]
    [SerializeField] float droughtDamagePerDay = 0.008f;

    /// <summary>Current soil moisture 0 (bone dry) → 1 (just watered). Drained each grow tick.</summary>
    public float soilMoisture = 1f;
    public float droughtDaysAccumulated = 0f;

    /// <summary>When true, waters automatically just before drought threshold is reached.</summary>
    public bool autoWaterEnabled = false;

    /// <summary>Set to true for one frame when auto-water fires. buttonClicker reads and clears this to flash the water button.</summary>
    [HideInInspector] public bool autoWaterJustFired = false;

    /// <summary>In-game days since last auto-water. Prevents firing more than once per in-game day at high timescale.</summary>
    float autoWaterCooldownDays = 0f;

    [Header("Weeds")]
    [Tooltip("Radius (world units) on the soil surface within which weeds can spawn. " +
             "Should roughly match the pot rim radius.")]
    [SerializeField] float weedSpawnRadius = 0.8f;

    [Header("Fertilizer")]
    [Tooltip("Nutrient units drained each spring. At 0.4 the tree runs low after ~2 unfertilized seasons.")]
    [SerializeField] float nutrientDrainPerSeason = 0.4f;

    [Tooltip("Nutrient units added by one Fertilize() call. Two applications reach the over-fertilize threshold.")]
    [SerializeField] float fertilizeAmount = 0.5f;

    [Tooltip("nutrientReserve above this value causes FertilizerBurn to root nodes each spring.")]
    [SerializeField] float fertilizerBurnThreshold = 1.5f;

    [Tooltip("Health damage applied to each root node when nutrientReserve > fertilizerBurnThreshold.")]
    [SerializeField] float fertilizerBurnDamage = 0.08f;

    /// <summary>Available soil nutrients (0 = depleted, 1 = neutral, 2 = max / over-fertilized).</summary>
    public float nutrientReserve = 1f;

    /// <summary>When true, auto-fertilizes before the reserve drops to zero.</summary>
    public bool autoFertilizeEnabled = false;

    /// <summary>Set true for one frame when auto-fertilize fires; buttonClicker reads and clears it.</summary>
    [HideInInspector] public bool autoFertilizeJustFired = false;

    /// <summary>When true, applies herbicide automatically after weeds go unattended for autoHerbicideDelayDays.</summary>
    public bool autoHerbicideEnabled = false;

    [Tooltip("In-game days weeds must be present before auto-herbicide fires. Gives the player time to pull them manually.")]
    public float autoHerbicideDelayDays = 5f;

    [HideInInspector] public bool  autoHerbicideJustFired = false;
    float autoHerbicidePendingDays = 0f;

    /// <summary>When true, applies fungicide automatically when infection exceeds autoFungicideThreshold.</summary>
    public bool autoFungicideEnabled = false;

    [Tooltip("fungalLoad level on any node that triggers the auto-fungicide countdown.")]
    public float autoFungicideThreshold = 0.25f;

    [Tooltip("In-game days after fungal threshold is hit before auto-fungicide fires.")]
    public float autoFungicideDelayDays = 3f;

    [HideInInspector] public bool  autoFungicideJustFired = false;
    float autoFungicidePendingDays = 0f;

    [Header("Fungal System")]
    [Tooltip("Chance per season that an infected node spreads fungalLoad to each adjacent node.")]
    [SerializeField] [Range(0f, 1f)] float fungalSpreadChance = 0.25f;

    [Tooltip("fungalLoad added per season to a node that is currently at risk (open wound, overwatered roots, low health).")]
    [SerializeField] float fungalLoadIncrease = 0.20f;

    [Tooltip("Health damage per unit of fungalLoad above 0.4, applied each season.")]
    [SerializeField] float fungalDamagePerLoad = 0.05f;

    [Tooltip("fungalLoad reduced per season when a node is no longer at risk.")]
    [SerializeField] float fungalRecoveryRate = 0.10f;

    [Tooltip("How much fungicide reduces each node's fungalLoad per application.")]
    [SerializeField] float fungicideReduceAmount = 0.60f;

    [Tooltip("Fraction of nutrientDrainPerSeason saved when the entire root network is mycorrhizal. " +
             "Actual saving scales linearly with the fraction of root nodes that are mycorrhizal.")]
    [SerializeField] [Range(0f, 0.5f)] float mycorrhizalNutrientBonus = 0.20f;

    [Tooltip("Consecutive healthy seasons (health>0.75 and fungalLoad<0.1) a root node needs before " +
             "it becomes mycorrhizal.")]
    [SerializeField] int mycorrhizalHealthySeasonsRequired = 3;

    [Header("Leaf Energy")]
    [Tooltip("Maximum energy multiplier. A very lush, healthy canopy can exceed 1.0 up to this cap " +
             "for a modest extra-vigour bonus. 1.5 = 50% bonus growth at peak canopy.")]
    [SerializeField] float maxEnergyMultiplier = 1.5f;

    [Header("Wound System")]
    [Tooltip("Branches at or above this radius require the Saw tool's multi-stroke mechanic " +
             "to remove. Thinner branches can be cut with a single click.")]
    [SerializeField] public float sawRadiusThreshold = 0.08f;

    [Tooltip("Health drained from a wounded node per growing season. Paste stops this drain.")]
    [SerializeField] float woundDrainRate = 0.05f;

    [Tooltip("Growing seasons to fully callus over one unit of wound radius. " +
             "Larger wounds (thicker cut branches) take proportionally longer to heal. " +
             "E.g. radius=0.1 × 20 = 2 seasons; radius=0.5 × 20 = 10 seasons.")]
    [SerializeField] float seasonsToHealPerUnit = 20f;

    [Tooltip("Health hit applied to the cut-site node each time it is trimmed. " +
             "Accumulates: cutting the same node three times in one session is noticeably weakening. " +
             "Recovers at trimTraumaRecoveryPerSeason each spring.")]
    [SerializeField] float trimTraumaDamage = 0.05f;

    [Tooltip("Health recovered per growing season on any damaged non-root node. " +
             "Covers trim trauma and slow wire recovery. " +
             "Keep slightly below woundDrainRate so unprotected wounds still net-worsen over time.")]
    [SerializeField] float trimTraumaRecoveryPerSeason = 0.04f;

    [Tooltip("Optional material for wound visualization. " +
             "If left empty a plain dark-brown Unlit material is used automatically. " +
             "The bark shader uses vertex colours so it can't be darkened at runtime.")]
    [SerializeField] Material woundMaterialOverride;

    [Header("Root System")]
    [Tooltip("Maximum node depth a root strand can grow to.")]
    [SerializeField] int maxRootDepth = 12;

    [Tooltip("Probability of a lateral sub-root branching off per root segment.")]
    [SerializeField] float rootLateralChance = 0.65f;

    [Tooltip("How much lateral chance decays per depth level. " +
             "1.0 = no decay (every depth equally likely to lateral). " +
             "0.7 = deep roots rarely branch. Raise toward 1.0 for quicker, denser branching.")]
    [SerializeField] [Range(0.5f, 1f)] float rootLateralDepthDecay = 0.85f;

    [Tooltip("New trunk root strands are planted automatically each spring until this many " +
             "direct-trunk roots exist. Spread evenly around the trunk.")]
    [SerializeField] int targetTrunkRoots = 21;

    [Tooltip("How many new trunk roots are planted per spring. " +
             "Lower = slower, more organic buildup toward targetTrunkRoots.")]
    [SerializeField] [Range(1, 5)] int trunkRootsPerYear = 2;

    [Tooltip("Assign the 'Root Area' scene object. When set, roots fill that box instead of " +
             "spreading by radius. Leave empty to use the legacy radial spread system.")]
    [SerializeField] Transform rootAreaTransform;

    [Header("Root Health")]
    [Tooltip("Maximum root depth counted toward the root health score. 1 = only first segments off the trunk; 3 captures the full surface flare.")]
    [SerializeField] int rootHealthMaxDepth = 3;

    [Tooltip("Root tips within this many Y-units of the soil surface (Y=0) count as surface roots for root health scoring.")]
    [SerializeField] float rootHealthSurfaceDepth = 0.3f;

    [Tooltip("Root radius considered ideal for the girth component. Roots at or above this thickness score full girth points.")]
    [SerializeField] float rootHealthTargetRadius = 0.04f;

    [Tooltip("If the centre of mass of surface roots is this far from the trunk horizontally, balance reaches zero.")]
    [SerializeField] float rootHealthBalanceRadius = 1.5f;

    [Tooltip("Fallback used when no Root Area is assigned: spread target = tree height × this.\n" +
             "Ignored when rootAreaTransform is set.")]
    [SerializeField] float rootSpreadMultiplier = 2f;

    [Tooltip("Chance per non-terminal root node per season to sprout a new fill-in lateral " +
             "inside the spread radius. Higher = denser root mat over time.")]
    [SerializeField] [Range(0f, 1f)] float rootFillLateralChance = 0.03f;

    [Tooltip("Seasons a root terminal must spend near a wall (distRatio ≥ 0.85) before " +
             "pot-bound effects activate: slower growth, thickening, and inner fill boost.")]
    [SerializeField] int boundaryPressureThreshold = 3;

    [Tooltip("Growth speed multiplier applied to pot-bound root terminals (fraction of normal rate). " +
             "0.35 = roots near walls grow at 35% speed.")]
    [SerializeField] [Range(0f, 1f)] float boundaryGrowthScale = 0.35f;

    [Tooltip("Radius added to a pot-bound root node per season it stays at the wall. " +
             "Thickening propagates up the pipe model, making wall-hugging roots visibly gnarled.")]
    [SerializeField] float boundaryThickenRate = 0.003f;

    [Tooltip("Multiplier on rootFillLateralChance for low-depth (≤ 2) root ancestors of pot-bound terminals. " +
             "Simulates the tree pushing new root mass back toward the trunk when it can't spread further.")]
    [SerializeField] float potBoundInnerBoost = 3f;

    [Tooltip("Maximum new inner-fill laterals spawned per season from the pot-bound system. " +
             "This budget is independent of maxTotalRootNodes so it fires even when the outer cap is full.")]
    [SerializeField] int potBoundMaxFillPerYear = 30;

    [Tooltip("Hard cap on total root nodes. No new root segments (continuation or lateral) " +
             "spawn once this is reached. Prevents unbounded root growth over many seasons.")]
    [SerializeField] int maxTotalRootNodes = 1500;

    [Tooltip("Outward-from-trunk radial bias on root continuation. Keeps roots spreading wide.")]
    [SerializeField] float rootRadialWeight = 0.25f;

    [Tooltip("Downward gravity bias on root continuation. Keep this small — roots should stay near the surface.")]
    [SerializeField] float rootGravityWeight = 0.05f;

    [Tooltip("Initial downward Y component added to a freshly planted root direction (before normalising).")]
    [SerializeField] float rootInitialPitch = 0.08f;

    [Tooltip("Number of root strands automatically generated when the tree is first created.")]
    [SerializeField] int initialRootCount = 5;

    [Tooltip("How far the tree object lifts in world units when entering Root Prune mode.")]
    [SerializeField] float rootLiftHeight = 3.5f;

    [Tooltip("Lift/lower animation speed (units per second).")]
    [SerializeField] float rootLiftSpeed = 4f;

    [Tooltip("Distance from the planting surface at which roots begin to hug the surface.")]
    [SerializeField] float rootSurfaceSnapDist = 0.8f;

    [Header("Ishitsuki Rock")]
    [Tooltip("Convex MeshCollider of the placed rock. Set at runtime by RockPlacer on orientation confirm.")]
    public Collider rockCollider;

    /// <summary>
    /// True once a rock has been confirmed. Unlike rockCollider, this never goes null
    /// even if the Collider reference somehow gets cleared — used to gate Ishitsuki-mode
    /// logic everywhere so we don't depend on rockCollider != null as the mode flag.
    /// </summary>
    [HideInInspector] public bool isIshitsukiMode = false;

    [Tooltip("World-unit radius around the rock surface within which roots deflect to follow it.")]
    [SerializeField] float rockInfluenceRadius = 0.4f;

    [Tooltip("How many in-game days between [Tree5] snapshot log entries. Lower = more detail; higher = less spam.")]
    [SerializeField] int   snapshotLogIntervalDays = 5;

    [Tooltip("Max in-game days to randomly delay a new lateral branch before it starts elongating. Spreads branch activation across the growing season. 0 = old instant behaviour.")]
    [SerializeField] float branchSpawnMaxDelay = 50f;

    [Tooltip("Enable to override the auto-computed soil Y with the value below. Useful for testing root draping.")]
    [SerializeField] bool  debugSoilYOverride = false;

    [Tooltip("Manual soil Y world position. Only used when Debug Soil Y Override is enabled. Defaults to -9999 which auto-populates from the computed soilY on first use — then you can nudge it.")]
    [SerializeField] float debugSoilY = -9999f;

    // ── Soil debug GL overlay (set by SpawnTrainingWires, rendered by OnRenderObject) ──
    bool    _soilDbgActive;
    float   _soilDbgEndTime;
    float   _soilDbgSoilY, _soilDbgRockTop, _soilDbgRockBot;
    Vector3 _soilDbgCenter;
    float   _soilDbgR;
    Material _soilDbgMat;

    [Header("Air Layering")]
    [Tooltip("Prefab spawned at the air layer site to represent the coconut coir wrap. Optional — system works without it.")]
    [SerializeField] GameObject airLayerWrapPrefab;

    [Tooltip("Number of growing seasons before roots develop under the wrap.")]
    [SerializeField] int airLayerSeasonsToRoot = 2;

    [Tooltip("Seasons of air-layer root growth (after unwrapping) before the Sever option appears.")]
    [SerializeField] int airLayerRootSeasonsToSever = 2;

    [Tooltip("Number of new roots spawned when the wrap is removed.")]
    [SerializeField] [Range(2, 21)] int airLayerRootCount = 17;

    [Tooltip("Number of segments per root strand spawned at unwrap. More = longer, snakier roots from the start.")]
    [SerializeField] [Range(1, 8)] int airLayerRootSegments = 3;

    [Tooltip("Target length of each segment on a newly-spawned air-layer root strand.")]
    [SerializeField] float airLayerRootTargetLength = 1.0f;

    [Tooltip("Radius of air-layer roots as a fraction of the trunk node's radius at the layer site.")]
    [SerializeField] [Range(0.1f, 1f)] float airLayerRootRadiusMultiplier = 0.35f;

    [Tooltip("Ishitsuki cable radius at the trunk base as a fraction of trunk radius. Tapers to rootTerminalRadius toward the tips.")]
    [SerializeField] [Range(0.05f, 1f)] float ishitsukiCableRadiusMultiplier = 0.3f;

    [Tooltip("Minimum angle (degrees) allowed between consecutive cable segments. If a new segment would bend back toward the trunk sharper than this, it falls straight down instead. 60–90 prevents the sharp U-turns visible on the upper rock face.")]
    [SerializeField] [Range(10f, 90f)] float minCableAngleDeg = 65f;

    [Header("Seed")]
    [Tooltip("Trunk length at which the seed object is hidden (the sprout has emerged).")]
    [SerializeField] float seedHideLength = 0.25f;

    // References

    [HideInInspector] public TreeMeshBuilder meshBuilder;

    /// <summary>Fired after a trim, with the list of every node that was removed.</summary>
    public event Action<List<TreeNode>> OnSubtreeTrimmed;
    /// <summary>Fired the first time any wire on this tree turns gold (set complete).</summary>
    public event Action OnWireSetGold;
    /// <summary>Fired at the end of StartNewGrowingSeason each spring, after all branching and bud-break logic completes.</summary>
    public event Action OnNewGrowingSeason;

    // Tree Data

    [HideInInspector] public TreeNode       root;
    [HideInInspector] public List<TreeNode> allNodes = new List<TreeNode>();

    int   nextId          = 0;
    bool  isGrowing       = false;
    int   lastGrownYear   = -1;
    int   startYear       = -1;
    int   startMonth      = -1;
    float cachedTreeHeight = 1f;  // updated each spring and on trim; used for root spread radius
    /// <summary>Height of the tallest non-root tip above the tree's local origin. Updated each spring and on every trim.</summary>
    public float CachedTreeHeight => cachedTreeHeight;
    int   lastRecalcDay   = -1;   // tracks last in-game day RecalculateRadii was run mid-season

    // ── Leaf energy ───────────────────────────────────────────────────────────
    /// <summary>
    /// Photosynthetic energy from last season's canopy. Computed by LeafManager
    /// at bud-set time (September). Multiplies growth speed, lateral chance, and
    /// health recovery the following spring. Initialised to 1 so year-1 grows normally.
    /// </summary>
    public float treeEnergy = 1f;

    /// <summary>
    /// Averaged boundaryPressure across root terminals, normalized to [0, 1].
    /// 0 = no pot-bound pressure; 1 = fully restricted (at or above rootPressureFullRestriction).
    /// Used by LeafManager to drive leaf miniaturization.
    /// </summary>
    public float RootPressureFactor()
    {
        float sum = 0f; int count = 0;
        foreach (var n in allNodes)
            if (n.isRoot && n.isTerminal) { sum += n.boundaryPressure; count++; }
        if (count == 0) return 0f;
        return Mathf.Clamp01(sum / count / Mathf.Max(1f, rootPressureFullRestriction));
    }

    /// <summary>Exposes refinementCap so LeafManager can normalize refinement levels.</summary>
    public float RefinementCap => refinementCap;
    /// <summary>Exposes wound-heal rate so TreeMeshBuilder can compute callus geometry.</summary>
    public float SeasonsToHealPerUnit => seasonsToHealPerUnit;

    // ── Save / Load accessors (expose private fields for SaveManager) ─────────
    public int   SaveStartYear     { get => startYear;      set => startYear      = value; }
    /// <summary>Year the tree was planted. -1 before first BranchGrow season.</summary>
    public int   plantingYear     => startYear;
    public int   SaveStartMonth    { get => startMonth;     set => startMonth     = value; }
    public int   SaveLastGrownYear { get => lastGrownYear;  set => lastGrownYear  = value; }

    /// <summary>Fill soil moisture to 1.0. Called by the watering can button.</summary>
    public void Water()
    {
        soilMoisture = 1f;
        Debug.Log($"[Water] Watered | moisture restored to 1.0 | year={GameManager.year} month={GameManager.month}");
    }

    /// <summary>
    /// Add nutrients. Clamped to 2× max to prevent excessive accumulation.
    /// Blocked in winter (months 11–2) — fertilizing dormant trees causes burn without benefit.
    /// </summary>
    public bool Fertilize()
    {
        int m = GameManager.month;
        bool isWinter = m == 11 || m == 12 || m == 1 || m == 2;
        if (isWinter)
        {
            Debug.Log($"[Fert] Fertilize blocked — winter month={m}");
            return false;
        }
        float nutrientCap = GetComponent<PotSoil>()?.nutrientCapacity ?? 2f;
        nutrientReserve = Mathf.Min(nutrientReserve + fertilizeAmount, nutrientCap);
        Debug.Log($"[Fert] Fertilized → nutrientReserve={nutrientReserve:F2} | year={GameManager.year} month={m}");
        return true;
    }

    /// <summary>
    /// Reduce fungalLoad on all nodes and destroy existing mycorrhizal networks.
    /// Called by buttonClicker when the player uses the Fungicide tool.
    /// </summary>
    public void ApplyFungicide()
    {
        int infected = 0, myco = 0;
        foreach (var node in allNodes)
        {
            if (node.fungalLoad > 0f)
            {
                node.fungalLoad = Mathf.Max(0f, node.fungalLoad - fungicideReduceAmount);
                infected++;
            }
            if (node.isMycorrhizal) { node.isMycorrhizal = false; node.healthySeasonsCount = 0; myco++; }
        }
        Debug.Log($"[Fungal] Fungicide applied | {infected} node(s) treated, {myco} mycorrhizal network(s) destroyed.");
    }

    /// <summary>
    /// Kill all mycorrhizal networks (called when herbicide is used — broad-spectrum soil damage).
    /// </summary>
    public void DamageMycorrhizae()
    {
        int count = 0;
        foreach (var node in allNodes)
            if (node.isMycorrhizal) { node.isMycorrhizal = false; node.healthySeasonsCount = 0; count++; }
        if (count > 0) Debug.Log($"[Fungal] Herbicide destroyed {count} mycorrhizal network(s).");
    }

    // ── Save / Load ───────────────────────────────────────────────────────────

    /// <summary>
    /// Rebuilds the entire tree from deserialized save data.
    /// Called by SaveManager.Load() after GameManager time state is already restored.
    /// </summary>
    public void LoadFromSaveData(SaveData data, LeafManager leafManager)
    {
        // Clear all existing GameObjects and state
        foreach (var go in budObjects.Values)    if (go != null) Destroy(go);
        budObjects.Clear();
        foreach (var go in lateralBudObjects)    if (go != null) Destroy(go);
        lateralBudObjects.Clear();
        foreach (var go in woundObjects.Values)  if (go != null) Destroy(go);
        woundObjects.Clear();
        if (seedObject != null) { Destroy(seedObject); seedObject = null; }
        allNodes.Clear();
        root = null;

        // Restore skeleton live state
        treeEnergy             = data.treeEnergy;
        soilMoisture           = data.soilMoisture;
        droughtDaysAccumulated = data.droughtDaysAccumulated;
        nutrientReserve        = data.nutrientReserve > 0f ? data.nutrientReserve : 1f;

        // Restore weed state
        var weedMgr = GetComponent<WeedManager>();
        if (weedMgr != null)
        {
            weedMgr.SetPotBounds(transform.position, weedSpawnRadius);
            weedMgr.LoadSaveState(data.weeds);
        }

        // Restore soil state
        var potSoil = GetComponent<PotSoil>();
        if (potSoil != null)
        {
            potSoil.preset             = (PotSoil.SoilPreset)data.soilPreset;
            potSoil.akadama            = data.soilAkadama;
            potSoil.pumice             = data.soilPumice;
            potSoil.lavaRock           = data.soilLavaRock;
            potSoil.organic            = data.soilOrganic;
            potSoil.sand               = data.soilSand;
            potSoil.kanuma             = data.soilKanuma;
            potSoil.perlite            = data.soilPerlite;
            potSoil.soilDegradation    = data.soilDegradation;
            potSoil.saturationLevel    = data.soilSaturation;
            potSoil.seasonsSinceRepot  = data.soilSeasonsSinceRepot;
            potSoil.potSize            = (PotSoil.PotSize)data.potSize;
            potSoil.ComputeDerivedProperties();
            potSoil.ApplyPotSize(rootAreaTransform);
        }
        var rockPlacer = UnityEngine.Object.FindAnyObjectByType<RockPlacer>();
        if (rockPlacer != null)
        {
            rockPlacer.rockSize = (RockPlacer.RockSize)data.rockSize;
            rockPlacer.ApplyRockSize();
        }
        startYear              = data.startYear;
        startMonth             = data.startMonth;
        lastGrownYear          = data.lastGrownYear;
        isIshitsukiMode        = data.isIshitsukiMode;
        treeOrigin             = (TreeOrigin)data.treeOrigin;
        plantingNormal         = new Vector3(data.planNX, data.planNY, data.planNZ);
        plantingSurfacePoint   = new Vector3(data.planPX, data.planPY, data.planPZ);
        nextId                 = 0;

        // Pass 1: create TreeNode objects from save data
        var nodeById = new Dictionary<int, TreeNode>(data.nodes.Count);
        foreach (var sn in data.nodes)
        {
            var pos = new Vector3(sn.px, sn.py, sn.pz);
            var dir = new Vector3(sn.dx, sn.dy, sn.dz);
            var node = new TreeNode(sn.id, sn.depth, pos, dir, sn.radius, sn.targetLength, null);

            node.minRadius          = sn.minRadius;
            node.length             = sn.length;
            node.isGrowing          = sn.isGrowing;
            node.age                = sn.age;
            node.isTrimmed          = sn.isTrimmed;
            node.hasLeaves          = sn.hasLeaves;
            node.isRoot             = sn.isRoot;
            node.subdivisionsLeft   = sn.subdivisionsLeft;
            node.birthYear          = sn.birthYear;
            node.refinementLevel    = sn.refinementLevel;
            node.branchVigor        = sn.branchVigor;
            node.hasBud             = sn.hasBud;
            node.backBudStimulated  = sn.backBudStimulated;
            node.isTrimCutPoint     = sn.isTrimCutPoint;
            node.trimCutDepth       = sn.trimCutDepth;
            node.regrowthSeasonCount= sn.regrowthSeasonCount;
            node.health             = sn.health;
            node.branchLoad  = sn.branchLoad;
            node.sagAngleDeg = sn.sagAngleDeg;
            node.isDead        = sn.isDead;
            node.isDeadwood    = sn.isDeadwood;
            node.shadedSeasons = sn.shadedSeasons;
            node.deadSeasons   = sn.deadSeasons;
            node.fungalLoad          = sn.fungalLoad;
            node.isMycorrhizal       = sn.isMycorrhizal;
            node.healthySeasonsCount = sn.healthySeasonsCount;
            node.hasWire            = sn.hasWire;
            node.wireOriginalDirection = new Vector3(sn.woX, sn.woY, sn.woZ);
            node.wireTargetDirection   = new Vector3(sn.wtX, sn.wtY, sn.wtZ);
            node.wireSetProgress    = sn.wireSetProgress;
            node.wireDamageProgress = sn.wireDamageProgress;
            node.wireAgeDays        = sn.wireAgeDays;
            node.isTrainingWire     = sn.isTrainingWire;
            node.boundaryPressure   = sn.boundaryPressure;
            node.isAirLayerRoot     = sn.isAirLayerRoot;
            node.hasWound           = sn.hasWound;
            node.woundRadius        = sn.woundRadius;
            node.woundFaceNormal    = new Vector3(sn.wnX, sn.wnY, sn.wnZ);
            node.woundAge           = sn.woundAge;
            node.pasteApplied       = sn.pasteApplied;

            nodeById[sn.id] = node;
            allNodes.Add(node);
            if (sn.id >= nextId) nextId = sn.id + 1;
        }

        // Pass 2: re-link parent/child references
        foreach (var sn in data.nodes)
        {
            var node = nodeById[sn.id];
            if (sn.parentId >= 0 && nodeById.TryGetValue(sn.parentId, out var parent))
            {
                node.parent = parent;
                parent.children.Add(node);
            }
            else
            {
                root = node;
            }
        }

        // Pass 3: re-spawn wound objects
        foreach (var node in allNodes)
            if (node.hasWound) CreateWoundObject(node);

        // Pass 3b: restore fusion bonds
        fusionBonds.Clear();
        if (data.fusionBonds != null)
        {
            foreach (var sf in data.fusionBonds)
                fusionBonds.Add(new FusionBond(sf.nodeIdA, sf.nodeIdB)
                {
                    seasonsElapsed = sf.seasonsElapsed,
                    isComplete     = sf.isComplete,
                    bridgeId       = sf.bridgeId,
                });
        }

        // Rebuild the mesh
        RecalculateRadii(root);
        meshBuilder.SetDirty();

        // Pass 4: restore leaves on terminal non-root nodes
        if (leafManager != null)
        {
            leafManager.ClearAllLeaves();
            var leafNodes = new System.Collections.Generic.List<TreeNode>();
            foreach (var node in allNodes)
                if (node.isTerminal && !node.isTrimmed && !node.isRoot && node.hasLeaves)
                    leafNodes.Add(node);
            leafManager.ForceSpawnLeaves(leafNodes);
        }

        Debug.Log($"[Save] Tree restored: {allNodes.Count} nodes | root={root?.id} | year={GameManager.year}");
    }

    // ── Trim undo ─────────────────────────────────────────────────────────────
    TrimUndoState pendingUndo = null;

    [Tooltip("Seconds after a trim during which Ctrl+Z can undo it. Season tick clears the window.")]
    [SerializeField] float undoWindowSeconds = 5f;

    public bool  CanUndo          => pendingUndo != null &&
                                     Time.realtimeSinceStartup - pendingUndo.timestamp < undoWindowSeconds;
    public float UndoTimeRemaining => pendingUndo == null ? 0f :
                                      Mathf.Max(0f, undoWindowSeconds - (Time.realtimeSinceStartup - pendingUndo.timestamp));

    // node.id → live terminal bud GameObject (spawned at season end, destroyed on bud break)
    readonly Dictionary<int, GameObject> budObjects = new Dictionary<int, GameObject>();

    // Lateral (axillary) bud GameObjects — all destroyed at spring start
    readonly List<GameObject> lateralBudObjects = new List<GameObject>();

    // node.id → wound visualization GameObject (half-torus at the cut face)
    readonly Dictionary<int, GameObject> woundObjects = new Dictionary<int, GameObject>();

    // ── Root Health ───────────────────────────────────────────────────────────
    /// <summary>0–100 root health score. Updated each spring and on RootPrune entry.</summary>
    public float RootHealthScore { get; private set; }
    /// <summary>Per-sector coverage, normalised 0–1. Length = 8 (N, NE, E, SE, S, SW, W, NW).</summary>
    public float[] RootHealthSectorCoverage { get; private set; } = new float[8];

    // ── Menu-exposed tuning properties ───────────────────────────────────────
    public float BaseGrowSpeed
    {
        get => baseGrowSpeed;
        set => baseGrowSpeed = Mathf.Clamp(value, 0.01f, 1f);
    }
    public float SpringLateralChance
    {
        get => springLateralChance;
        set => springLateralChance = Mathf.Clamp01(value);
    }
    public float DepthSpeedDecay
    {
        get => depthSpeedDecay;
        set => depthSpeedDecay = Mathf.Clamp(value, 0.5f, 1f);
    }
    public float IshitsukiCableRadiusMultiplier
    {
        get => ishitsukiCableRadiusMultiplier;
        set => ishitsukiCableRadiusMultiplier = Mathf.Clamp(value, 0.05f, 1f);
    }
    public float MinCableAngleDeg
    {
        get => minCableAngleDeg;
        set => minCableAngleDeg = Mathf.Clamp(value, 10f, 90f);
    }

    // ── Air Layering ──────────────────────────────────────────────────────────
    /// <summary>
    /// Tracks all active air layers. Exposed so TreeInteraction can read layer state.
    /// </summary>
    public readonly List<AirLayerData> airLayers = new List<AirLayerData>();

    /// <summary>Data for one active air layer on the trunk.</summary>
    public class AirLayerData
    {
        public TreeNode   node;              // trunk node the layer is applied to
        public int        seasonsElapsed;    // growing seasons since placement
        public bool       rootsSpawned;      // true once roots are ready to emerge
        public GameObject wrapObject;        // the coir wrap visual (may be null)
        public bool       isUnwrapped;       // true after player clicks to unwrap
        public int        rootGrowSeasons;   // seasons of root growth since unwrap
        public bool       isSeverable;       // true when rootGrowSeasons >= threshold
    }

    int   lastSnapshotAbsDay = -1;

    readonly Stopwatch growthTimer = new Stopwatch();
    long totalGrowthMs = 0;
    int  growthFrames  = 0;

    // Seed visual -- hidden once the sprout has grown past seedHideLength
    GameObject seedObject;

    // Root lift animation
    float initY       = 0f;  // world Y the tree rests at (updated by SetPlantingSurface)
    float liftTarget  = 0f;  // 0 = grounded, rootLiftHeight = lifted
    float currentLift = 0f;

    // Planting surface -- the surface the tree rests on.
    // Initially flat ground (normal = up, point = origin).
    // Updated by SetPlantingSurface() when player places the tree on a rock.
    [HideInInspector] public Vector3 plantingNormal       = Vector3.up;
    [HideInInspector] public Vector3 plantingSurfacePoint = Vector3.zero;

    // Pre-placement snapshot — captured when entering RockPlace, restored on cancel.
    Vector3    prePlacementPosition;
    Quaternion prePlacementRotation;
    Vector3    prePlacementSurfacePoint;
    Vector3    prePlacementNormal;

    /// <summary>Maximum depth allowed to sprout children this season.</summary>
    int SeasonDepthCap => startYear < 0
        ? depthsPerYear
        : Mathf.Min(maxDepth, (GameManager.year - startYear + 1) * depthsPerYear);

    /// <summary>
    /// Returns the effective depth cap for a node, capped by any trim cut point
    /// in its ancestry.  A fresh stump only allows depthsPerYear new levels per
    /// season, mirroring year-1 pacing.  Returns SeasonDepthCap when no
    /// cut-point restriction applies.
    /// </summary>
    int CutPointDepthCap(TreeNode node)
    {
        TreeNode n = node;
        while (n != null)
        {
            if (n.isTrimCutPoint)
            {
                // Suggestion 4: severe cuts (depth ratio > threshold) grow back more slowly
                // for the first recovery season, scaling the per-season recovery rate down.
                int dpy = depthsPerYear;
                if (n.regrowthSeasonCount <= 1 && SeasonDepthCap > 0)
                {
                    float severity = (float)n.trimCutDepth / SeasonDepthCap;
                    if (severity > severeCutSeverityThreshold)
                        dpy = Mathf.Max(1, Mathf.RoundToInt(depthsPerYear * severeCutRecoveryScale));
                }
                return n.trimCutDepth + n.regrowthSeasonCount * dpy;
            }
            n = n.parent;
        }
        return SeasonDepthCap;
    }

    // Unity

    void Awake()
    {
        meshBuilder = GetComponent<TreeMeshBuilder>();
        if (meshBuilder == null)
            Debug.LogError("TreeSkeleton: TreeMeshBuilder not found on this GameObject -- both components must be on the same GameObject.", this);

        ApplySpecies();

        initY = transform.position.y;

        // Initialise WeedManager pot bounds — updated each spring with the real position.
        var wm = GetComponent<WeedManager>();
        if (wm != null)
            wm.SetPotBounds(transform.position, weedSpawnRadius);
    }

    /// <summary>
    /// Copies all parameters from the assigned TreeSpecies ScriptableObject into the
    /// local SerializeField fields. Safe to call at runtime if you swap species mid-session.
    /// Does nothing if no species is assigned.
    /// </summary>
    public void ApplySpecies()
    {
        if (species == null) return;

        baseGrowSpeed                = species.baseGrowSpeed;
        depthSpeedDecay              = species.depthSpeedDecay;
        depthsPerYear                = species.depthsPerYear;
        baseElongation               = species.baseElongation;
        elongationDepthDecay         = species.elongationDepthDecay;

        branchSegmentLength          = species.branchSegmentLength;
        segmentLengthDecay           = species.segmentLengthDecay;
        rootSegmentLength            = species.rootSegmentLength;

        baseBranchChance             = species.baseBranchChance;
        branchChanceDepthDecay       = species.branchChanceDepthDecay;
        springLateralChance          = species.springLateralChance;
        oldWoodBudChance             = species.oldWoodBudChance;
        backBudBaseChance            = species.backBudBaseChance;

        budType                      = species.budType;
        apicalDominance              = species.apicalDominance;

        apicalVigorBonus             = species.apicalVigorBonus;
        vigorDecayRate               = species.vigorDecayRate;

        wireDaysToSet                = species.wireDaysToSet;

        woundDrainRate               = species.woundDrainRate;
        seasonsToHealPerUnit         = species.seasonsToHealPerUnit;
        trimTraumaDamage             = species.trimTraumaDamage;
        trimTraumaRecoveryPerSeason  = species.trimTraumaRecoveryPerSeason;

        drainRatePerDay              = species.drainRatePerDay;
        droughtThreshold             = species.droughtThreshold;
        droughtDamagePerDay          = species.droughtDamagePerDay;

        nutrientDrainPerSeason       = species.nutrientDrainPerSeason;

        fungalSpreadChance           = species.fungalSpreadChance;
        fungalRecoveryRate           = species.fungalRecoveryRate;

        maxEnergyMultiplier          = species.maxEnergyMultiplier;

        meshBuilder?.ApplySpeciesColors();

        Debug.Log($"[Species] Applied '{species.speciesName}' ({species.scientificName})");
    }

    void OnEnable()  => GameManager.OnGameStateChanged += OnGameStateChanged;
    void OnDisable() => GameManager.OnGameStateChanged -= OnGameStateChanged;

    // ── Soil debug GL overlay — renders into both Game View and Scene View ────
    void OnRenderObject()
    {
        if (!_soilDbgActive) return;
        if (Time.realtimeSinceStartup > _soilDbgEndTime) { _soilDbgActive = false; return; }

        if (_soilDbgMat == null)
        {
            Shader sh = Shader.Find("Hidden/Internal-Colored");
            if (sh == null) return;
            _soilDbgMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            _soilDbgMat.SetInt("_ZWrite", 0);
            _soilDbgMat.SetInt("_Cull",   0);
            _soilDbgMat.SetInt("_ZTest",  (int)UnityEngine.Rendering.CompareFunction.Always);
        }

        _soilDbgMat.SetPass(0);
        GL.PushMatrix();
        GL.Begin(GL.LINES);

        Vector3 c = _soilDbgCenter;
        float   r = _soilDbgR;

        // GREEN cross + 4 tall pillars = soilY (where roots stop)
        GL.Color(Color.green);
        GL.Vertex3(c.x - r, _soilDbgSoilY, c.z);     GL.Vertex3(c.x + r, _soilDbgSoilY, c.z);
        GL.Vertex3(c.x, _soilDbgSoilY, c.z - r);     GL.Vertex3(c.x, _soilDbgSoilY, c.z + r);
        // Vertical pillars so they're visible edge-on
        GL.Vertex3(c.x - r, _soilDbgSoilY, c.z - r); GL.Vertex3(c.x - r, _soilDbgSoilY + 3f, c.z - r);
        GL.Vertex3(c.x + r, _soilDbgSoilY, c.z - r); GL.Vertex3(c.x + r, _soilDbgSoilY + 3f, c.z - r);
        GL.Vertex3(c.x - r, _soilDbgSoilY, c.z + r); GL.Vertex3(c.x - r, _soilDbgSoilY + 3f, c.z + r);
        GL.Vertex3(c.x + r, _soilDbgSoilY, c.z + r); GL.Vertex3(c.x + r, _soilDbgSoilY + 3f, c.z + r);

        // RED cross = rock bottom bound (old wrong soilY target)
        GL.Color(Color.red);
        GL.Vertex3(c.x - r, _soilDbgRockBot, c.z);   GL.Vertex3(c.x + r, _soilDbgRockBot, c.z);
        GL.Vertex3(c.x, _soilDbgRockBot, c.z - r);   GL.Vertex3(c.x, _soilDbgRockBot, c.z + r);

        // CYAN cross = rock top bound
        GL.Color(Color.cyan);
        GL.Vertex3(c.x - r, _soilDbgRockTop, c.z);   GL.Vertex3(c.x + r, _soilDbgRockTop, c.z);
        GL.Vertex3(c.x, _soilDbgRockTop, c.z - r);   GL.Vertex3(c.x, _soilDbgRockTop, c.z + r);

        GL.End();
        GL.PopMatrix();
    }

    void Update()
    {
        // Debug: press 1-9 to instantly simulate that many years of growth
        if (Keyboard.current != null)
        {
            Key[] digitKeys = {
                Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5,
                Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9
            };
            for (int k = 0; k < digitKeys.Length; k++)
            {
                if (Keyboard.current[digitKeys[k]].wasPressedThisFrame)
                {
                    for (int y = 0; y <= k; y++) SimulateYear();
                    break;
                }
            }
        }

        // Root lift animation -- runs regardless of grow state
        if (!Mathf.Approximately(currentLift, liftTarget))
        {
            currentLift = Mathf.MoveTowards(currentLift, liftTarget, rootLiftSpeed * Time.deltaTime);
            var p = transform.position;
            transform.position = new Vector3(p.x, initY + currentLift, p.z);
        }

        // Hide the seed after ~1 in-game month OR once the sprout is tall enough.
        if (seedObject != null && seedObject.activeSelf && root != null)
        {
            bool sproutVisible = root.length >= seedHideLength;
            bool monthPassed   = startMonth >= 0 &&
                                 (GameManager.year * 12 + GameManager.month) >
                                 (startYear  * 12 + startMonth);
            if (sproutVisible || monthPassed)
            {
                seedObject.SetActive(false);
                Debug.Log("[Tree] Seed hidden");
            }
        }

        // Keep air layer wraps sized to the trunk every frame so they can't be swallowed.
        if (airLayers.Count > 0)
            foreach (var layer in airLayers)
                SetAirLayerWrapTransform(layer);

        // Keep air layer root bases anchored to parent tip as the trunk grows.
        UpdateAirLayerRootPositions();

        // In-game time for this frame — used by auto-care and growth.
        float inGameDays = Time.deltaTime * GameManager.TIMESCALE / 24f;

        // Auto-care: runs regardless of grow state (dormancy, leaf fall, TimeGo, etc.)
        // Only requires root to exist so we don't fire before the tree is initialised.
        if (root != null)
        {
            autoWaterCooldownDays += inGameDays;
            if (autoWaterEnabled && soilMoisture < 0.5f && autoWaterCooldownDays >= 1.0f)
            {
                Water();
                autoWaterJustFired    = true;
                autoWaterCooldownDays = 0f;
            }
            if (autoFertilizeEnabled && nutrientReserve < 0.6f)
            {
                if (Fertilize())
                    autoFertilizeJustFired = true;
            }

            // Auto-herbicide: count up days while weeds are present; fire after delay
            if (autoHerbicideEnabled)
            {
                var wm = GetComponent<WeedManager>();
                if (wm != null && wm.ActiveWeedCount > 0)
                {
                    autoHerbicidePendingDays += inGameDays;
                    if (autoHerbicidePendingDays >= autoHerbicideDelayDays)
                    {
                        wm.HerbicideAll();
                        autoHerbicideJustFired   = true;
                        autoHerbicidePendingDays = 0f;
                    }
                }
                else
                {
                    autoHerbicidePendingDays = 0f;  // player pulled them — reset countdown
                }
            }

            // Auto-fungicide: fire after delay once any node exceeds the threshold
            if (autoFungicideEnabled)
            {
                bool infected = false;
                foreach (var n in allNodes)
                    if (n.fungalLoad > autoFungicideThreshold) { infected = true; break; }

                if (infected)
                {
                    autoFungicidePendingDays += inGameDays;
                    if (autoFungicidePendingDays >= autoFungicideDelayDays)
                    {
                        ApplyFungicide();
                        autoFungicideJustFired   = true;
                        autoFungicidePendingDays = 0f;
                    }
                }
                else
                {
                    autoFungicidePendingDays = 0f;
                }
            }
        }

        if (!isGrowing || root == null) return;

        float rate = GameManager.SeasonalGrowthRate;
        if (rate <= 0f) return;

        // Tick down branch activation delays regardless of rate (calendar time, not growth rate)
        if (branchSpawnMaxDelay > 0f)
            foreach (var node in allNodes)
                if (node.growthStartDelay > 0f)
                    node.growthStartDelay = Mathf.Max(0f, node.growthStartDelay - inGameDays);

        bool structureChanged = false;
        bool anyGrew          = false;

        // Soil moisture drain — modified by PotSoil water retention if present
        var potSoil = GetComponent<PotSoil>();
        float soilDrainMult = potSoil != null ? potSoil.DrainRateMultiplier : 1f;
        soilMoisture = Mathf.Max(0f, soilMoisture - drainRatePerDay * soilDrainMult * inGameDays * rate);
        if (soilMoisture < droughtThreshold)
            droughtDaysAccumulated += inGameDays;

        // Drought death: extended time at zero moisture kills immediately
        if (treeDeathEnabled && soilMoisture <= 0f)
        {
            droughtDaysAccumulated += inGameDays;   // already counted above, but total at zero is what matters
            if (droughtDaysAccumulated >= droughtDeathDays)
            {
                Debug.Log($"[Death] Tree died from drought — {droughtDaysAccumulated:F0} dry days | year={GameManager.year}");
                KillTree("drought");
                return;
            }
        }

        // Snapshot growing nodes -- we may add new ones during this loop
        var snapshot = new List<TreeNode>(allNodes.Count);
        foreach (var node in allNodes)
        {
            if (node.isGrowing && !node.isTrimmed && node.growthStartDelay <= 0f)
                snapshot.Add(node);
        }

        // Log every 5 in-game days — filter console by [Tree5]
        int absDay = GameManager.year * 365 + GameManager.dayOfYear;
        bool doLog = absDay - lastSnapshotAbsDay >= snapshotLogIntervalDays;
        if (doLog)
        {
            lastSnapshotAbsDay = absDay;
            int maxNodeDepth = 0;
            int rootNodes = 0, branchNodes = 0;
            foreach (var n in allNodes)
            {
                if (n.depth > maxNodeDepth) maxNodeDepth = n.depth;
                if (n.isRoot) rootNodes++; else branchNodes++;
            }
            long avgGrowth = growthFrames > 0 ? totalGrowthMs / growthFrames : 0;
            Debug.Log($"[Tree5] {GameManager.month}/{GameManager.day}/{GameManager.year} | " +
                      $"rate={rate:F2} growing={snapshot.Count} " +
                      $"total={allNodes.Count} (branches={branchNodes} roots={rootNodes}) maxDepth={maxNodeDepth} depthCap={SeasonDepthCap} | " +
                      $"growthLoop avg={avgGrowth}ms over {growthFrames} frames");

            totalGrowthMs = 0;
            growthFrames  = 0;
        }

        growthTimer.Restart();
        foreach (var node in snapshot)
        {
            // Dead or deadwood nodes never grow
            if (node.isDead || node.isDeadwood) continue;

            // Dormant from poor health -- skip this node entirely
            if (node.health < 0.25f) continue;

            // Health below 0.75 proportionally slows growth
            float healthMult = node.health >= 0.75f ? 1f : node.health;

            // nutrientReserve 0→2 maps to 0.6→1.4× growth; 1.0 = neutral (no effect)
            float nutrientMult = Mathf.Lerp(0.6f, 1.4f, Mathf.Clamp01(nutrientReserve / 2f));

            float speed = baseGrowSpeed
                             * rate
                             * Mathf.Pow(depthSpeedDecay, node.depth)
                             * healthMult
                             * treeEnergy
                             * nutrientMult
                             * GrowthSeasonMult();

            // Pot-bound roots near the tray wall grow slower
            if (node.isRoot && node.boundaryPressure >= boundaryPressureThreshold)
                speed *= boundaryGrowthScale;

            node.length += speed * inGameDays;
            node.age    += inGameDays * rate;
            anyGrew      = true;

            if (node.length >= node.targetLength)
            {
                bool belowCap;
                if (node.isRoot && isIshitsukiMode)
                {
                    Vector3 tipW = transform.TransformPoint(node.tipPosition);
                    if (node.isTrainingWire)
                    {
                        // Training-wire cables stop at soil; PreGrowRootsToSoil manages them.
                        belowCap = tipW.y > plantingSurfacePoint.y;
                    }
                    else
                    {
                        // Underground roots (continuation from training wire):
                        // only grow once the tip is actually below soil.
                        // This prevents accidentally-planted or stray roots from growing in air.
                        belowCap = tipW.y <= plantingSurfacePoint.y && node.depth < maxRootDepth;
                    }
                }
                else
                {
                    belowCap = node.isRoot
                        ? node.depth < maxRootDepth
                        : node.depth < SeasonDepthCap && node.depth < CutPointDepthCap(node);
                }

                // Always stop at targetLength — never grow past it.
                // Previously belowCap=false nodes grew indefinitely, creating
                // visually long "zombie" segments at the season depth boundary.
                node.length    = node.targetLength;
                node.isGrowing = false;
                // Finalize radius and lock minRadius so RecalculateRadii can't collapse
                // this node to 0 when its new children (starting at radius=0) are summed.
                float finalR   = (node.isRoot ? rootTerminalRadius : terminalRadius) * globalSegmentScale;
                node.radius    = finalR;
                node.minRadius = finalR;

                // Sub-chain continuation (subdivisionsLeft > 0) uses the same depth as
                // the parent, so it never consumes the season depth cap. Always allow it.
                // Real depth+1 branching (subdivisionsLeft == 0) requires belowCap.
                // Depth-frontier tips stop here and become eligible terminals next season
                // when SeasonDepthCap increases (depthsPerYear new depths per year).
                bool canSpawn = belowCap || (!node.isRoot && node.subdivisionsLeft > 0);
                if (canSpawn && !(isIshitsukiMode && node.isTrainingWire))
                    SpawnChildren(node);
                structureChanged = true;
            }
            else
            {
                // Ramp radius proportional to growth progress so parent thickening
                // spreads across the season rather than spiking at spawn time.
                // Floor at 10% of target so new segments never pop to zero-radius
                // on their first frame (which causes the visible "shrink" animation).
                float targetR = (node.isRoot ? rootTerminalRadius : terminalRadius) * globalSegmentScale;
                float rampedR = targetR * Mathf.Clamp01(node.length / node.targetLength);
                node.radius = Mathf.Max(rampedR, targetR * 0.1f);
            }
        }

        // Age accumulation — all non-trimmed nodes age each growing tick,
        // not just the ones currently growing. This drives the new-growth-to-bark
        // material fade in TreeMeshBuilder even after a segment stops elongating.
        // Roots are included so they transition from white → bark over seasons.
        // Above-ground roots bark faster (handled in GrowthColor via isExposed).
        foreach (var node in allNodes)
        {
            if (node.isTrimmed || node.isGrowing) continue;
            node.age += inGameDays * rate;
        }

        // Wire progress accumulation
        foreach (var node in allNodes)
        {
            if (!node.hasWire || node.isTrimmed) continue;

            node.wireAgeDays += inGameDays * rate;

            if (node.wireSetProgress < 1f)
            {
                float prev = node.wireSetProgress;
                node.wireSetProgress = Mathf.Min(1f,
                    node.wireSetProgress + inGameDays * rate / wireDaysToSet);
                // Fire once when a wire first turns gold
                if (prev < 1f && node.wireSetProgress >= 1f)
                    OnWireSetGold?.Invoke();
            }
            else if (node.wireDamageProgress < 1f)
            {
                float dmgDelta = inGameDays * rate / wireDaysToSet;
                node.wireDamageProgress = Mathf.Min(1f, node.wireDamageProgress + dmgDelta);
                ApplyDamage(node, DamageType.WireDamage, dmgDelta * 0.5f);
            }
        }

        growthTimer.Stop();
        totalGrowthMs += growthTimer.ElapsedMilliseconds;
        growthFrames++;

        // RecalculateRadii once per in-game day. Structural events (trim, spring start)
        // still trigger it immediately via their own direct calls.
        // Mid-season, the daily cadence picks up the ramping radii of growing nodes
        // and propagates them up the tree gradually rather than all at once.
        bool newDay = GameManager.day != lastRecalcDay;
        if (structureChanged || newDay)
        {
            RecalculateRadii(root);
            if (newDay)
            {
                lastRecalcDay  = GameManager.day;
                cachedTreeHeight = CalculateTreeHeight();  // keep cinematic zoom current during spring
            }
        }

        if (anyGrew || structureChanged)
            meshBuilder.SetDirty();
    }

    // Game State

    void OnGameStateChanged(GameState state)
    {
        if (state == GameState.Water && root == null)
            InitTree();

        bool wasGrowing = isGrowing;
        isGrowing = (state == GameState.BranchGrow);
        Debug.Log($"[TreeState] State -> {state} | isGrowing={isGrowing} | year={GameManager.year} lastGrownYear={lastGrownYear}");

        // Season just ended — freeze all still-growing segments at their current length.
        // Only on TimeGo (the true season end), not on temporary tool states like Wiring.
        if (state == GameState.TimeGo && wasGrowing && root != null)
        {
            foreach (var node in allNodes)
                if (node.isGrowing) node.isGrowing = false;
            SetBuds();
            meshBuilder.SetDirty();

            // Auto-save at the end of each growing season (after bud set).
            SaveManager.AutoSave(this, GetComponent<LeafManager>());
        }

        // TreeOrient lowers the tree so you orient at working height, not suspended in the air.
        // RootPrune and RockPlace still lift. Everything else grounds the tree.
        bool inRootMode = state == GameState.RootPrune || state == GameState.RockPlace;
        liftTarget = inRootMode ? rootLiftHeight : 0f;
        if (meshBuilder.renderRoots != GameManager.IsRootLiftActive(state))
        {
            meshBuilder.renderRoots = inRootMode;
            meshBuilder.SetDirty();
        }

        if (state == GameState.RockPlace)
        {
            prePlacementPosition     = transform.position;
            prePlacementRotation     = transform.rotation;
            prePlacementSurfacePoint = plantingSurfacePoint;
            prePlacementNormal       = plantingNormal;
        }

        if (state == GameState.RootPrune)
        {
            int rootCount = 0;
            foreach (var n in allNodes) if (n.isRoot) rootCount++;
            Debug.Log($"[Root] Entering RootPrune | rootNodes={rootCount} | liftTarget={liftTarget} | plantingNormal={plantingNormal} plantingPoint={plantingSurfacePoint}");
            RecalculateRootHealthScore();
        }

        if (state == GameState.BranchGrow && root != null && GameManager.year > lastGrownYear)
        {
            lastGrownYear = GameManager.year;
            StartNewGrowingSeason();
        }
    }

    /// <summary>Taper multiplier (0–1) based on day of year vs species slow/stop window.</summary>
    float GrowthSeasonMult()
    {
        float baseMult = 1f;
        if (species != null)
        {
            int d = GameManager.dayOfYear;
            if (d >= species.growthStopDay) return 0f;
            if (d >= species.growthSlowDay)
                baseMult = 1f - Mathf.InverseLerp(species.growthSlowDay, species.growthStopDay, d);
        }

        // Suggestion 3: reserve depletion — one spring of reduced growth after heavy pruning.
        if (heavyPruneRecoveryActive)
        {
            heavyPruneRecoveryActive = false; // consume; lasts exactly one growing season
            return baseMult * heavyPruneRecoveryScale;
        }

        return baseMult;
    }

    public void RestorePrePlacementSnapshot()
    {
        transform.position   = prePlacementPosition;
        transform.rotation   = prePlacementRotation;
        plantingSurfacePoint = prePlacementSurfacePoint;
        plantingNormal       = prePlacementNormal;
        meshBuilder.SetDirty();
    }

    void StartNewGrowingSeason()
    {
        // ── Scale debug (once per year) — filter console by [SCALEDEBUG] ───
        {
            // Use transform.position.y as fallback when plantingSurfacePoint hasn't been set
            float soilY    = (plantingSurfacePoint.y != 0f) ? plantingSurfacePoint.y : transform.position.y;
            float tallestY = soilY;
            float longestSeg = 0f;
            foreach (var n in allNodes)
            {
                if (n.isRoot) continue;
                Vector3 wp = transform.TransformPoint(n.tipPosition);
                if (wp.y > tallestY)    tallestY    = wp.y;
                if (n.targetLength > longestSeg) longestSeg = n.targetLength;
            }
            Debug.Log($"[SCALEDEBUG] year={GameManager.year} branchSegLen={branchSegmentLength:F4} " +
                      $"depthCap={SeasonDepthCap} heightAboveSoil={tallestY - soilY:F3} " +
                      $"longestSeg={longestSeg:F4} nodes={allNodes.Count}");
        }

        // Season tick: undo window expires — the tree has started responding to cuts
        pendingUndo = null;

        // Drought damage: apply accumulated stress from the previous season
        if (droughtDaysAccumulated > 0f)
        {
            float totalDamage = droughtDamagePerDay * droughtDaysAccumulated;
            foreach (var node in allNodes)
                if (!node.isRoot && !node.isTrimmed)
                    ApplyDamage(node, DamageType.Drought, totalDamage);
            Debug.Log($"[Water] Drought damage={totalDamage:F3} over {droughtDaysAccumulated:F1} dry days | year={GameManager.year}");
            droughtDaysAccumulated = 0f;
        }

        // Weeds: drain nutrients/moisture from existing weeds, maybe spawn a new one.
        // Use transform.position as soil center — always valid. plantingSurfacePoint stays
        // at Vector3.zero for non-Ishitsuki trees so it can't be used reliably here.
        var weedMgr = GetComponent<WeedManager>();
        if (weedMgr != null)
        {
            weedMgr.SetPotBounds(transform.position, weedSpawnRadius);
            weedMgr.SetTrunkRadius(root != null ? root.radius : 0f);
            weedMgr.ApplySeasonAndSpawn(this);
        }

        // Soil: degrade, check waterlogging, apply species mismatch penalties
        var soil = GetComponent<PotSoil>();
        soil?.SeasonTick(this);
        if (soil != null)
            nutrientReserve = Mathf.Min(nutrientReserve, soil.nutrientCapacity);

        // Fertilizer: burn if over-applied, then drain for this season
        if (nutrientReserve > fertilizerBurnThreshold)
        {
            float excess = nutrientReserve - fertilizerBurnThreshold;
            float burnDmg = fertilizerBurnDamage * excess;
            foreach (var node in allNodes)
                if (node.isRoot && !node.isTrimmed)
                    ApplyDamage(node, DamageType.FertilizerBurn, burnDmg);
            Debug.Log($"[Fert] Over-fertilize burn={burnDmg:F3} (reserve={nutrientReserve:F2} > threshold={fertilizerBurnThreshold:F2}) | year={GameManager.year}");
        }
        // Fungal: update infection and mycorrhizal networks before nutrient drain
        // (mycorrhizal nodes reduce effective drain)
        UpdateFungalInfection();
        UpdateMycorrhizal();
        GetComponent<LeafManager>()?.RefreshFungalTint(this);

        // Nutrient drain — mycorrhizal root coverage reduces drain proportionally
        int rootNodeCount = 0, mycoNodeCount = 0;
        foreach (var n in allNodes)
        {
            if (!n.isRoot || n.isTrimmed) continue;
            rootNodeCount++;
            if (n.isMycorrhizal) mycoNodeCount++;
        }
        float mycoFraction    = rootNodeCount > 0 ? (float)mycoNodeCount / rootNodeCount : 0f;
        float effectiveDrain  = nutrientDrainPerSeason * (1f - mycoFraction * mycorrhizalNutrientBonus);
        nutrientReserve = Mathf.Max(0f, nutrientReserve - effectiveDrain);
        Debug.Log($"[Fert] Season drain={effectiveDrain:F3} (myco={mycoFraction:P0} bonus) → nutrientReserve={nutrientReserve:F2} | year={GameManager.year}");

        // Suggestion 2: growth window lock — only advance recovery during growing months (Mar–Aug).
        // Dormant-season ticks don't count toward cut-point recovery.
        bool isGrowingSeason = GameManager.month >= 3 && GameManager.month <= 8;
        if (isGrowingSeason)
        {
            foreach (var node in allNodes)
            {
                if (!node.isTrimCutPoint) continue;
                node.regrowthSeasonCount++;
                if (node.trimCutDepth + node.regrowthSeasonCount * depthsPerYear >= SeasonDepthCap)
                    node.isTrimCutPoint = false;
            }
            CheckCutCapAbsorption();
        }

        // Suggestion 3: reserve depletion — heavy pruning this season penalises next spring's growth.
        if (cutDepthAccumulatedThisSeason >= heavyPruneThreshold)
        {
            heavyPruneRecoveryActive = true;
            Debug.Log($"[Prune] Heavy prune season: cutDepth={cutDepthAccumulatedThisSeason} >= threshold={heavyPruneThreshold}. Growth penalty next spring.");
        }
        cutDepthAccumulatedThisSeason = 0;

        // Refresh cached tree height and compute spread radius for this season
        cachedTreeHeight = CalculateTreeHeight();
        if (rootAreaTransform != null)
            Debug.Log($"[GRoot] StartNewGrowingSeason year={GameManager.year} | treeHeight={cachedTreeHeight:F2} rootArea={rootAreaTransform.lossyScale.x:F2}×{rootAreaTransform.lossyScale.z:F2}");
        else
        {
            float spreadRadius = cachedTreeHeight * rootSpreadMultiplier;
            Debug.Log($"[GRoot] StartNewGrowingSeason year={GameManager.year} | treeHeight={cachedTreeHeight:F2} spreadRadius={spreadRadius:F2}");
        }

        // Auto-plant trunk roots each spring until targetTrunkRoots is reached.
        // Counts only direct children of root that are roots (depth-1 trunk strands).
        int trunkRootCount = 0;
        int totalRootCount = 0;
        foreach (var n in allNodes)
        {
            if (!n.isRoot) continue;
            totalRootCount++;
            if (n.parent == root) trunkRootCount++;
        }
        int rootsToAdd = Mathf.Min(targetTrunkRoots - trunkRootCount, Mathf.Min(trunkRootsPerYear, maxTotalRootNodes - totalRootCount));
        if (rootsToAdd > 0)
        {
            for (int i = 0; i < rootsToAdd; i++)
            {
                // Spread new roots evenly in the gaps between existing ones
                float angle   = (trunkRootCount + i) * (Mathf.PI * 2f / targetTrunkRoots);
                Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                // Ishitsuki roots drape down a rock face — start steep so the first visible
                // segment flows downward rather than shooting radially outward.
                Vector3 dir = isIshitsukiMode
                    ? (outward * 0.35f + Vector3.down).normalized
                    : (outward + Vector3.down * rootInitialPitch).normalized;
                float   len     = Mathf.Max(rootSegmentLength * rootSegmentLengthDecay, 0.3f);

                // In Ishitsuki mode, place the startNode on the bark surface rather than
                // the trunk center so all cables have distinct, spread-out origins and
                // don't bunch into a dense white cluster at the root base.
                Vector3 localOutward = isIshitsukiMode
                    ? transform.InverseTransformDirection(outward).normalized
                    : Vector3.zero;
                float   barkOffset   = isIshitsukiMode ? Mathf.Max(root.radius, 0.04f) : 0f;
                Vector3 startPos     = root.worldPosition + localOutward * barkOffset;

                var r           = CreateNode(startPos, dir, rootTerminalRadius, len, root);
                r.isRoot        = true;
            }
            Debug.Log($"[GRoot] Auto-planted {rootsToAdd} trunk roots | trunkRoots={trunkRootCount + rootsToAdd}/{targetTrunkRoots} year={GameManager.year}");
        }

        // In Ishitsuki mode, pre-grow ALL trunk root cables toward soil before the
        // terminals list is built below.  This does two things:
        //   (a) Newly auto-planted roots get their chains draped over the rock immediately.
        //   (b) Existing mid-rock terminals from prior years get extended further.
        // Both cases prevent ContinuationDirection() from spawning children in random
        // air directions off the mid-rock positions.
        Debug.Log($"[PreGrow] year={GameManager.year} StartNewGrowingSeason: isIshitsukiMode={isIshitsukiMode} rockCollider={(rockCollider != null ? rockCollider.name : "NULL")}");
        if (isIshitsukiMode)
            PreGrowRootsToSoil(animated: true);

        // Elongate existing segments — lower-depth segments grow longer each year
        if (baseElongation > 0f)
        {
            foreach (var node in allNodes)
            {
                if (node.isTrimmed || node.isRoot || !node.isTerminal) continue;
                // Skip mid-chain sub-segments — their length was fixed when the chain started.
                // Elongating them would cause all remaining sub-segments to inherit the stretched
                // targetLength, producing one long segment per branch instead of N short ones.
                if (node.subdivisionsLeft > 0) continue;
                float delta = node.targetLength * baseElongation * Mathf.Pow(elongationDepthDecay, node.depth);
                node.targetLength += delta;
                node.length       += delta;  // advance length directly — avoids re-triggering SpawnChildren
            }
        }

        // Lateral bud visuals are only shown over winter; destroy them all at spring start.
        foreach (var go in lateralBudObjects)
            if (go != null) Destroy(go);
        lateralBudObjects.Clear();

        // Flush any growth-start delays carried over from the previous season.
        // Nodes that were waiting to activate in autumn get their chance at spring start.
        foreach (var node in allNodes)
            node.growthStartDelay = 0f;

        // Resume any non-root segments that were stopped mid-chord in autumn
        // (stopped while isGrowing but before reaching targetLength, no children yet).
        // This covers both mid-subdivision chains and any segment that didn't finish in time.
        // They resume rather than being re-spawned from a bud break.
        int resumed = 0;
        foreach (var node in allNodes)
        {
            if (node.isTrimmed || node.isRoot || !node.isTerminal) continue;
            if (node.isGrowing) continue;                   // already active
            if (node.hasBud) continue;                      // will be handled by bud break below
            if (node.length >= node.targetLength) continue; // fully grown, not mid-chord

            node.isGrowing = true;
            resumed++;
        }
        if (resumed > 0)
            Debug.Log($"[Bud] Resumed mid-chord segments={resumed} year={GameManager.year}");

        // Bud system: if any branch nodes have hasBud set (year 2+), use those as
        // the terminal list. Year 1 (fresh tree, no prior season) falls back to
        // the original isTerminal scan.
        bool budSystemActive = allNodes.Exists(n => !n.isRoot && n.hasBud);

        var terminals = new List<TreeNode>();
        int resuming  = 0;
        foreach (var node in allNodes)
        {
            if (node.isGrowing && !node.isTrimmed) resuming++;

            bool belowCap;
            if (node.isRoot && isIshitsukiMode)
            {
                if (node.isTrainingWire)
                {
                    // Training-wire cables: eligible while tip is at or above soil so they
                    // can spawn an underground continuation node the season they hit soil.
                    Vector3 tipW = transform.TransformPoint(node.tipPosition);
                    belowCap = tipW.y >= plantingSurfacePoint.y - 0.15f;
                }
                else
                {
                    // Underground roots branching off the training wire: use normal depth cap.
                    belowCap = node.depth < maxRootDepth;
                }
            }
            else
            {
                belowCap = node.isRoot
                    ? node.depth < maxRootDepth
                    : node.depth < SeasonDepthCap && node.depth < CutPointDepthCap(node);
            }

            if (node.isRoot)
            {
                if (!node.isTrimmed && node.isTerminal && !node.isGrowing && belowCap)
                    terminals.Add(node);
            }
            else
            {
                bool eligible = budSystemActive
                    ? (!node.isTrimmed && node.hasBud && belowCap)
                    : (!node.isTrimmed && node.isTerminal && !node.isGrowing && belowCap);
                if (eligible) terminals.Add(node);
            }
        }

        int rootTerminals = 0;
        int branchTerminals = 0;
        foreach (var t in terminals) { if (t.isRoot) rootTerminals++; else branchTerminals++; }
        Debug.Log($"[Tree] StartNewGrowingSeason year={GameManager.year} | depthCap={SeasonDepthCap} | terminals={terminals.Count} (roots={rootTerminals} branches={branchTerminals}) resuming={resuming} total={allNodes.Count}");

        // Count current root nodes once so the cap check is O(1) per terminal
        int currentRootCount = 0;
        int currentBranchCount = 0;
        foreach (var n in allNodes)
        {
            if (n.isRoot) currentRootCount++;
            else currentBranchCount++;
        }

        // Vigor: lateral chances scale down as the tree approaches the branch node cap.
        // Floors at 0.05 so old-wood / back-bud growth never stops completely on a living tree.
        float vigorFactor = Mathf.Max(0.05f, 1f - (float)currentBranchCount / maxBranchNodes);
        Debug.Log($"[Tree] branchNodes={currentBranchCount}/{maxBranchNodes} vigorFactor={vigorFactor:F2}");

        foreach (var terminal in terminals)
        {
            float baseSegLen  = (terminal.isRoot ? rootSegmentLength : branchSegmentLength) * globalSegmentScale;
            float decay       = terminal.isRoot ? rootSegmentLengthDecay : segmentLengthDecay;
            float chordLength = baseSegLen * Mathf.Pow(decay, terminal.depth + 1);

            // Refinement shortens internodes: each level applies ×refinementTaper (default 0.82).
            if (!terminal.isRoot && terminal.refinementLevel > 0f)
                chordLength *= Mathf.Pow(refinementTaper, terminal.refinementLevel);

            // Per-branch vigor scales segment length: high-vigor shoots grow longer each season.
            if (!terminal.isRoot)
                chordLength *= terminal.branchVigor;

            // Divide the chord into sub-segments so each is independently wireable.
            // Clamp per-segment (not the chord) so tip segments stay wireable regardless of N.
            // Also enforce maxSegmentLength: use whichever subdivision count gives shorter segments.
            int   subdivs     = !terminal.isRoot ? SubdivsForChord(chordLength) : 1;
            float childLength = (subdivs > 1) ? chordLength / subdivs : chordLength;
            childLength = Mathf.Max(childLength, terminal.isRoot ? 0.025f : minSegmentLength);

            float nodeRadius = terminal.isRoot ? rootTerminalRadius : terminalRadius;

            if (terminal.isRoot)
            {
                if (currentRootCount >= maxTotalRootNodes) continue;  // hard cap reached

                bool isIshitsuki = isIshitsukiMode;
                float distRatio  = RootDistRatio(terminal);
                if (!isIshitsuki && distRatio >= 1.3f) continue;  // beyond hard outer boundary — stop

                // Terminal clamp: if this root tip has already escaped the side or bottom
                // of the pot box, stop it permanently rather than letting it grow further out.
                // Top-face escape (surface roots) is left alone — it looks realistic.
                if (!isIshitsuki && rootAreaTransform != null)
                {
                    Vector3 tipW  = transform.TransformPoint(terminal.tipPosition);
                    Vector3 local = rootAreaTransform.InverseTransformPoint(tipW);
                    bool outsideSide   = Mathf.Abs(local.x) > 0.5f || Mathf.Abs(local.z) > 0.5f;
                    bool outsideBottom = local.y < -0.5f;
                    if (outsideSide || outsideBottom)
                    {
                        terminal.isTrimmed = true;
                        continue;
                    }
                }
                // In Ishitsuki mode, training-wire cable growth is handled exclusively by
                // PreGrowRootsToSoil.  Air layer roots keep growing downward each season.
                // Training-wire tips that have reached soil spawn one underground continuation.
                if (isIshitsuki && !terminal.isAirLayerRoot)
                {
                    if (!terminal.isTrainingWire) continue;
                    // Allow soil-level training wires through; skip mid-rock ones.
                    Vector3 twTipW = transform.TransformPoint(terminal.tipPosition);
                    if (twTipW.y > plantingSurfacePoint.y + 0.1f) continue;
                }

                if (!isIshitsuki && distRatio >= 0.8f) childLength *= wallSegmentScale;

                // Training-wire tips at soil spawn underground continuation (not training wire).
                bool isTrainingWireTransition = isIshitsuki && terminal.isTrainingWire;

                var cont = CreateNode(terminal.tipPosition, ContinuationDirection(terminal), nodeRadius, childLength, terminal);
                cont.isRoot         = true;
                // Air-layer roots transition to normal underground roots once they reach soil.
                Vector3 terminalTipW = transform.TransformPoint(terminal.tipPosition);
                bool stillAboveSoil  = terminal.isAirLayerRoot &&
                                       terminalTipW.y > plantingSurfacePoint.y + 0.05f;
                cont.isAirLayerRoot  = stillAboveSoil;
                // Underground continuation is a normal root, not a training-wire cable.
                // (isTrainingWire already defaults to false on new nodes.)
                currentRootCount++;

                // Training-wire→underground transitions get laterals; mid-rock Ishitsuki cables don't.
                float lateralScale  = (isIshitsuki && !isTrainingWireTransition) ? 0f : Mathf.Clamp01(1f - distRatio);
                float lateralChance = rootLateralChance * lateralScale;
                if (currentRootCount < maxTotalRootNodes && Random.value < lateralChance)
                {
                    var lat = CreateNode(terminal.tipPosition, LateralDirection(terminal), nodeRadius, childLength * 0.85f, terminal);
                    lat.isRoot         = true;
                    lat.isAirLayerRoot = stillAboveSoil;
                    currentRootCount++;
                }
            }
            else
            {
                if (currentBranchCount >= maxBranchNodes) continue;  // hard cap reached

                // Bud break — destroy the dormant bud GameObject
                if (terminal.hasBud)
                {
                    terminal.hasBud = false;
                    if (budObjects.TryGetValue(terminal.id, out var budGo))
                    {
                        Destroy(budGo);
                        budObjects.Remove(terminal.id);
                    }
                }

                if (budType == BudType.Opposite)
                {
                    var forkNeeded = currentBranchCount + 2 <= maxBranchNodes;
                    var (dirA, dirB) = OppositeForkDirections(terminal);
                    var forkA = CreateNode(terminal.tipPosition, dirA, nodeRadius, childLength, terminal);
                    forkA.isRoot = false;
                    if (subdivs > 1) forkA.subdivisionsLeft = subdivs - 1;
                    currentBranchCount++;
                    if (forkNeeded)
                    {
                        var forkB = CreateNode(terminal.tipPosition, dirB, nodeRadius, childLength, terminal);
                        forkB.isRoot = false;
                        if (subdivs > 1) forkB.subdivisionsLeft = subdivs - 1;
                        currentBranchCount++;
                    }
                    GameManager.branches++;
                }
                else
                {
                    var cont = CreateNode(terminal.tipPosition, ContinuationDirection(terminal), nodeRadius, childLength, terminal);
                    cont.isRoot = false;
                    if (subdivs > 1)
                        cont.subdivisionsLeft = subdivs - 1;
                    cont.growthStartDelay = Random.Range(0f, branchSpawnMaxDelay * 0.3f);
                    currentBranchCount++;

                    if (currentBranchCount < maxBranchNodes && Random.value < springLateralChance * vigorFactor * treeEnergy * terminal.branchVigor)
                    {
                        float latLength = childLength * 0.85f * Mathf.Max(0.1f, 1f - apicalDominance);
                        var lat = CreateNode(terminal.tipPosition, LateralDirection(terminal), nodeRadius, latLength, terminal);
                        lat.isRoot = false;
                        if (subdivs > 1)
                            lat.subdivisionsLeft = subdivs - 1;
                        lat.growthStartDelay = Random.Range(0f, branchSpawnMaxDelay);
                        currentBranchCount++;
                        GameManager.branches++;
                    }
                }
            }
        }

        // Fill-in laterals: non-terminal root nodes inside the spread radius
        // continue sprouting new side roots each season, densifying the root mat.
        // Snapshot allNodes first — CreateNode appends to allNodes during iteration.
        int fillCount = 0;
        var fillCandidates = new List<TreeNode>(allNodes);
        foreach (var node in fillCandidates)
        {
            if (currentRootCount >= maxTotalRootNodes) break;  // hard cap reached

            if (!node.isRoot || node.isTrimmed || node.isTerminal) continue;
            if (node.depth >= maxRootDepth - 1) continue;

            float distRatio = RootDistRatio(node);
            if (distRatio >= 1f) continue;  // only fill inside the target radius

            // Chance: high near trunk, fades toward the spread edge, decays with depth
            float chance = rootFillLateralChance * (1f - distRatio) * Mathf.Pow(0.6f, node.depth);
            if (Random.value < chance)
            {
                float segLen = rootSegmentLength * Mathf.Pow(rootSegmentLengthDecay, node.depth + 1);
                segLen = Mathf.Max(segLen, 0.3f);
                var lat = CreateNode(node.tipPosition, LateralDirection(node), rootTerminalRadius, segLen, node);
                lat.isRoot = true;
                fillCount++;
                currentRootCount++;
            }
        }

        if (fillCount > 0)
            Debug.Log($"[GRoot] Fill-in laterals sprouted={fillCount} year={GameManager.year}");

        // Boundary pressure: roots near walls thicken, slow down, and stimulate inner fills.
        // Track which low-depth ancestors need a lateral boost this season.
        var potBoundInnerCandidates = new HashSet<TreeNode>();
        int potBoundCount = 0;
        foreach (var node in allNodes)
        {
            if (!node.isRoot || node.isTrimmed) continue;
            // Ishitsuki roots are outside the pot by design — do not treat as pot-bound.
            if (isIshitsukiMode) continue;

            float distRatio = RootDistRatio(node);
            if (distRatio >= 0.85f)
            {
                node.boundaryPressure++;
                if (node.boundaryPressure >= boundaryPressureThreshold)
                {
                    // Thicken — propagates up the pipe model to parent roots
                    node.radius    += boundaryThickenRate;
                    node.minRadius  = Mathf.Max(node.minRadius, node.radius);
                    potBoundCount++;

                    // Walk up toward trunk to collect low-depth ancestors for inner fill
                    TreeNode anc = node.parent;
                    while (anc != null && anc.isRoot)
                    {
                        if (anc.depth <= 2 && !anc.isTerminal)
                            potBoundInnerCandidates.Add(anc);
                        anc = anc.parent;
                    }
                }
            }
            else
            {
                // Pressure decays when the root is no longer crowded against a wall
                node.boundaryPressure = Mathf.Max(0, node.boundaryPressure - 1);
            }
        }

        // Spawn extra fill-in laterals from low-depth ancestors of pot-bound terminals.
        // Simulates the tree pushing new root mass back toward the trunk when walled in.
        int potBoundFillCount = 0;
        // Use a slightly higher ceiling so inner fill still works when outer cap is full,
        // but cap total roots at 1.5× maxTotalRootNodes to prevent unbounded growth.
        int potBoundRootCap = Mathf.RoundToInt(maxTotalRootNodes * 1.5f);
        foreach (var node in potBoundInnerCandidates)
        {
            if (potBoundFillCount >= potBoundMaxFillPerYear) break;
            if (currentRootCount >= potBoundRootCap) break;
            if (node.depth >= maxRootDepth - 1) continue;

            float distRatio = RootDistRatio(node);
            float chance = rootFillLateralChance * potBoundInnerBoost * Mathf.Pow(0.6f, node.depth);
            if (Random.value < chance)
            {
                float segLen = rootSegmentLength * Mathf.Pow(rootSegmentLengthDecay, node.depth + 1);
                segLen = Mathf.Max(segLen, 0.3f);
                var lat = CreateNode(node.tipPosition, LateralDirection(node), rootTerminalRadius, segLen, node);
                lat.isRoot = true;
                currentRootCount++;
                potBoundFillCount++;
            }
        }
        if (potBoundCount > 0)
            Debug.Log($"[GRoot] Pot-bound terminals={potBoundCount} innerFill={potBoundFillCount} year={GameManager.year}");

        // Pot-bound first trigger: notify RootRakeManager to nudge tree + spawn surface roots
        bool nowPotBound = IsPotBound();
        if (nowPotBound && !wasPotBound)
            GetComponent<RootRakeManager>()?.OnBecamePotBound();
        wasPotBound = nowPotBound;

        // Back-budding: nodes whose tip ancestry was trimmed get a boosted chance
        // to sprout a new lateral from dormant axillary buds.
        // Snapshot allNodes first — CreateNode appends to allNodes during iteration.
        int backBudCount = 0;
        var backBudCandidates = new List<TreeNode>(allNodes);
        foreach (var node in backBudCandidates)
        {
            if (!node.backBudStimulated || node.isTrimmed || node.isRoot) continue;
            node.backBudStimulated = false;  // consume — only fires once per trim event

            if (currentBranchCount >= maxBranchNodes) continue;  // hard cap

            bool belowCap = node.depth < SeasonDepthCap && node.depth < CutPointDepthCap(node);
            if (!belowCap) continue;

            float chance = backBudBaseChance * backBudActivationBoost * vigorFactor
                           * Mathf.Pow(branchChanceDepthDecay, node.depth);
            if (Random.value < chance)
            {
                float chordLen    = branchSegmentLength * globalSegmentScale * Mathf.Pow(segmentLengthDecay, node.depth + 1);
                if (node.refinementLevel > 0f)
                    chordLen *= Mathf.Pow(refinementTaper, node.refinementLevel);
                int   bbSubdivs   = SubdivsForChord(chordLen);
                float segLen      = bbSubdivs > 1 ? chordLen / bbSubdivs : chordLen;
                segLen = Mathf.Max(segLen, minSegmentLength) * Mathf.Max(0.1f, 1f - apicalDominance);
                var lat = CreateNode(node.tipPosition, LateralDirection(node), terminalRadius, segLen, node);
                lat.isRoot = false;
                lat.refinementLevel = Mathf.Min(node.refinementLevel + refinementOnBackBud, refinementCap);
                if (bbSubdivs > 1) lat.subdivisionsLeft = bbSubdivs - 1;
                currentBranchCount++;
                backBudCount++;
            }
        }
        if (backBudCount > 0)
            Debug.Log($"[Bud] Back-buds activated={backBudCount} year={GameManager.year}");

        // Old-wood budding: dormant axillary buds on interior junction nodes can break
        // spontaneously each spring without requiring a trim event. Rate is low on most trees;
        // higher on Japanese maple and other freely back-budding species.
        if (oldWoodBudChance > 0f)
        {
            int oldWoodCount = 0;
            var junctionCandidates = new List<TreeNode>(allNodes);
            foreach (var node in junctionCandidates)
            {
                if (node.isTrimmed || node.isRoot || node.isTerminal) continue;
                // Skip sub-segment junctions — not real branching forks
                bool isSubJunction = node.children.Count == 1 && node.children[0].depth == node.depth;
                if (isSubJunction) continue;

                if (currentBranchCount >= maxBranchNodes) break;  // hard cap

                bool belowCap = node.depth < SeasonDepthCap && node.depth < CutPointDepthCap(node);
                if (!belowCap) continue;

                if (Random.value >= oldWoodBudChance * vigorFactor) continue;

                float chordLen    = branchSegmentLength * globalSegmentScale * Mathf.Pow(segmentLengthDecay, node.depth + 1);
                if (node.refinementLevel > 0f)
                    chordLen *= Mathf.Pow(refinementTaper, node.refinementLevel);
                int   owSubdivs   = SubdivsForChord(chordLen);
                float segLen      = owSubdivs > 1 ? chordLen / owSubdivs : chordLen;
                segLen = Mathf.Max(segLen, minSegmentLength) * Mathf.Max(0.1f, 1f - apicalDominance);
                var lat = CreateNode(node.tipPosition, LateralDirection(node), terminalRadius, segLen, node);
                lat.isRoot = false;
                if (owSubdivs > 1) lat.subdivisionsLeft = owSubdivs - 1;
                currentBranchCount++;
                oldWoodCount++;
            }
            if (oldWoodCount > 0)
                Debug.Log($"[Bud] Old-wood buds broke={oldWoodCount} year={GameManager.year}");
        }

        // Wound aging: drain health, scale wound visuals, heal over time
        foreach (var node in allNodes)
        {
            if (!node.hasWound) continue;

            node.woundAge++;

            if (!node.pasteApplied)
                ApplyDamage(node, DamageType.WoundDrain, woundDrainRate);

            float seasonsToHeal = Mathf.Max(1f, node.woundRadius * seasonsToHealPerUnit);
            float remaining     = 1f - node.woundAge / seasonsToHeal;

            if (remaining <= 0f)
            {
                node.hasWound = false;
                if (woundObjects.TryGetValue(node.id, out var wGo))
                {
                    Destroy(wGo);
                    woundObjects.Remove(node.id);
                }
            }
            // Wound visuals are now driven by vertex.g (woundAge) in TreeMeshBuilder —
            // no per-frame scale needed here.
        }

        // Trim trauma recovery: all damaged non-root nodes heal a small amount each spring.
        // Wound drain (woundDrainRate = 0.05) slightly exceeds this (0.04) so unprotected
        // wounds still net-worsen, while paste-protected and trauma-only nodes recover cleanly.
        float recoveryThisSeason = trimTraumaRecoveryPerSeason * treeEnergy;
        if (recoveryThisSeason > 0f)
        {
            foreach (var node in allNodes)
            {
                if (node.isRoot || node.isTrimmed || node.health >= 1f) continue;
                node.health = Mathf.Min(1f, node.health + recoveryThisSeason);
            }
        }

        // Per-branch vigor update:
        //   1. Apical nudge — shallow/apex nodes accumulate vigor naturally each season
        //      (simulates the hormonal advantage of the apex and outer tips).
        //   2. Decay toward 1.0 — without trimming, most nodes regress to neutral over time.
        //   3. Clamp to [vigorMin, vigorMax].
        foreach (var node in allNodes)
        {
            if (node.isRoot || node.isTrimmed) continue;
            float depthFactor = node.depth == 0 ? 1f : 1f / node.depth;
            node.branchVigor += apicalVigorBonus * depthFactor;
            node.branchVigor  = Mathf.Lerp(node.branchVigor, 1f, vigorDecayRate);
            node.branchVigor  = Mathf.Clamp(node.branchVigor, vigorMin, vigorMax);
        }

        if (terminals.Count > 0 || fillCount > 0 || backBudCount > 0)
        {
            RecalculateRadii(root);
            meshBuilder.SetDirty();
        }

        UpdateAirLayers();
        AdvanceGrafts();
        DetectNewFusions();
        AdvanceFusions();
        RecalculateRootHealthScore();

        // Branch weight: compute loads bottom-up, sag directions, apply junction stress
        if (branchWeightEnabled)
            BranchWeightPass();

        // Dieback: mark dead branches, remove dropped ones, apply shading damage
        if (branchDiebackEnabled)
            DiebackPass();

        // Death evaluation — runs after all seasonal damage is applied
        if (treeDeathEnabled)
            EvaluateTreeDeath();
    }

    // ── Branch Weight & Sag ───────────────────────────────────────────────────

    void BranchWeightPass()
    {
        if (root == null) return;

        // Pass 1: compute branchLoad bottom-up for every branch node
        ComputeLoad(root);

        // Pass 2: apply sag + junction stress top-down
        ApplySagAndStress(root);

        meshBuilder.SetDirty();
    }

    float ComputeLoad(TreeNode node)
    {
        if (node.isRoot) { node.branchLoad = 0f; return 0f; }

        float ownMass = node.radius * node.radius * node.length * woodDensity;
        float childLoad = 0f;
        foreach (var child in node.children)
            childLoad += ComputeLoad(child);

        node.branchLoad = ownMass + childLoad;
        return node.branchLoad;
    }

    void ApplySagAndStress(TreeNode node)
    {
        if (!node.isRoot && !node.isTrimmed && !node.isDead && !node.isDeadwood)
        {
            // Strength: radius³ × hardness × maturity (0→1 over matureAgeSeasons)
            float seasonsAlive = Mathf.Max(1f, GameManager.year - node.birthYear);
            float maturity = Mathf.Clamp01(seasonsAlive / matureAgeSeasons);

            // Apply species wood hardness if available
            float hardness = woodHardness;
            if (species != null) hardness *= species.budType == BudType.Opposite ? 0.85f : 1.0f; // deciduous slightly softer

            float strength = node.radius * node.radius * node.radius * hardness * maturity;
            strength = Mathf.Max(strength, 0.0001f);   // prevent divide-by-zero on seedlings

            float ratio = node.branchLoad / strength;

            // Accumulate sag angle
            if (ratio > sagThreshold)
            {
                float deltaSag = (ratio - sagThreshold) * sagSensitivity;
                node.sagAngleDeg = Mathf.Min(node.sagAngleDeg + deltaSag, maxSagAngleDeg);
            }
            else
            {
                // Gradual recovery when branch is no longer overloaded
                node.sagAngleDeg = Mathf.Max(0f, node.sagAngleDeg - 0.5f);
            }

            // Apply sag to growDirection: blend toward -up by sagAngleDeg
            if (node.sagAngleDeg > 0.01f)
            {
                float t = (node.sagAngleDeg / maxSagAngleDeg) * sagBlend;
                Vector3 sagged = Vector3.Slerp(node.growDirection, Vector3.down, t).normalized;
                node.growDirection = sagged;
                // Propagate position change down to all descendants
                PropagatePositions(node);
            }

            // Junction stress: damage parent when this child is very heavy relative to it
            if (node.parent != null && !node.parent.isRoot && ratio > junctionStressThreshold)
            {
                ApplyDamage(node.parent, DamageType.JunctionStress, junctionStressDamage);
                Debug.Log($"[Weight] Junction stress on node {node.parent.id} from child {node.id} ratio={ratio:F2} | year={GameManager.year}");
            }
        }

        foreach (var child in node.children)
            ApplySagAndStress(child);
    }

    /// <summary>
    /// After a sag adjustment, walk all descendants and update worldPosition
    /// to stay connected to their parent's new tipPosition.
    /// </summary>
    void PropagatePositions(TreeNode node)
    {
        foreach (var child in node.children)
        {
            child.worldPosition = node.tipPosition;
            PropagatePositions(child);
        }
    }

    // ── Branch Dieback ────────────────────────────────────────────────────────

    /// <summary>
    /// Called each spring. Three passes:
    ///  1. Mark newly dead nodes (health == 0 and not already isDead).
    ///  2. Apply shading damage to interior branches with no leaf-bearing descendants.
    ///  3. Remove small dead branches that have lingered past deadSeasonsToDrop.
    /// Large dead branches become isDeadwood and remain permanently.
    /// </summary>
    void DiebackPass()
    {
        var toRemove = new List<TreeNode>();

        foreach (var node in allNodes)
        {
            if (node.isTrimmed || node.isRoot) continue;

            // Pass 1: mark newly dead
            if (!node.isDead && node.health <= 0f)
            {
                node.isDead = true;
                node.isGrowing = false;
                node.hasBud = false;

                if (node.radius >= diebackThinRadius)
                {
                    // Large branch — stays as deadwood
                    node.isDeadwood = true;
                    Debug.Log($"[Dieback] Node {node.id} depth={node.depth} r={node.radius:F3} → DEADWOOD | year={GameManager.year}");
                }
                else
                {
                    Debug.Log($"[Dieback] Node {node.id} depth={node.depth} r={node.radius:F3} → dying (drops in {deadSeasonsToDrop} seasons) | year={GameManager.year}");
                }

                // Drop leaves from dead node immediately
                GetComponent<LeafManager>()?.DefoliateNode(node);
            }

            // Pass 2: tick shading counter and apply damage
            if (!node.isDead && !node.isTerminal)
            {
                bool hasLeafDescendant = NodeHasLeafDescendant(node);
                if (!hasLeafDescendant)
                {
                    node.shadedSeasons++;
                    if (node.shadedSeasons > shadingToleranceSeasons)
                    {
                        float shadeDmg = shadingDamagePerSeason * (node.shadedSeasons - shadingToleranceSeasons);
                        ApplyDamage(node, DamageType.Shading, shadeDmg);
                        Debug.Log($"[Dieback] Shading dmg={shadeDmg:F3} node={node.id} shadedSeasons={node.shadedSeasons} | year={GameManager.year}");
                    }
                }
                else
                {
                    node.shadedSeasons = 0;
                }
            }

            // Pass 3: tick dead season counter; schedule small dead branches for removal
            if (node.isDead && !node.isDeadwood)
            {
                node.deadSeasons++;
                if (node.deadSeasons >= deadSeasonsToDrop)
                    toRemove.Add(node);
            }
        }

        // Remove dropped branches (trim from tips inward to keep parent references valid)
        foreach (var node in toRemove)
        {
            if (!allNodes.Contains(node)) continue;   // already removed as part of a subtree
            var parent = node.parent;
            if (parent != null) parent.children.Remove(node);
            var removed = new List<TreeNode>();
            RemoveSubtree(node, removed);
            if (parent != null && parent.isTerminal)
                parent.isTrimCutPoint = false;   // no cut, just fell off
            Debug.Log($"[Dieback] Dropped dead branch node={node.id} ({removed.Count} nodes removed) | year={GameManager.year}");
        }

        if (toRemove.Count > 0)
        {
            RecalculateRadii(root);
            meshBuilder.SetDirty();
        }
    }

    /// <summary>Returns true if any descendant of node has leaves.</summary>
    bool NodeHasLeafDescendant(TreeNode node)
    {
        foreach (var child in node.children)
        {
            if (child.hasLeaves) return true;
            if (NodeHasLeafDescendant(child)) return true;
        }
        return false;
    }

    // ── Tree Death ────────────────────────────────────────────────────────────

    void EvaluateTreeDeath()
    {
        // Condition 1: average trunk health critically low for N consecutive seasons
        float trunkHealthSum = 0f;
        int   trunkCount     = 0;
        foreach (var node in allNodes)
        {
            if (node.isRoot || node.isTrimmed) continue;
            trunkHealthSum += node.health;
            trunkCount++;
        }
        float avgHealth = trunkCount > 0 ? trunkHealthSum / trunkCount : 1f;

        if (avgHealth < criticalHealthThreshold)
        {
            consecutiveCriticalSeasons++;
            Debug.Log($"[Death] Critical season {consecutiveCriticalSeasons}/{criticalSeasonsTodie} | avgHealth={avgHealth:F3} | year={GameManager.year}");
        }
        else
        {
            if (consecutiveCriticalSeasons > 0)
                Debug.Log($"[Death] Tree recovered — resetting critical counter | avgHealth={avgHealth:F3}");
            consecutiveCriticalSeasons = 0;
        }

        // Condition 2: living root count too low
        int livingRoots = 0;
        foreach (var node in allNodes)
            if (node.isRoot && !node.isTrimmed && node.health > 0f) livingRoots++;

        bool rootCollapse = trunkCount > 0 && livingRoots < minimumLivingRootNodes;

        // Warning state: one critical season away from death
        bool wasInDanger = treeInDanger;
        treeInDanger = consecutiveCriticalSeasons >= criticalSeasonsTodie - 1 || rootCollapse;
        if (treeInDanger && !wasInDanger)
            Debug.Log($"[Death] WARNING — tree is in danger! | year={GameManager.year}");

        // Trigger death
        if (consecutiveCriticalSeasons >= criticalSeasonsTodie)
        {
            KillTree("critical health");
            return;
        }
        if (rootCollapse)
        {
            KillTree("root loss");
        }

        OnNewGrowingSeason?.Invoke();
    }

    /// <summary>Human-readable cause of death — read by ButtonClicker to populate the death screen.</summary>
    public static string LastDeathCause { get; private set; } = "";

    public void KillTree(string cause)
    {
        var _ks = GameManager.Instance.state;
        if (_ks == GameState.TreeDead || _ks == GameState.SpeciesSelect ||
            _ks == GameState.LoadMenu  || _ks == GameState.Menu) return;

        LastDeathCause = cause;
        Debug.Log($"[Death] Tree is dead. Cause: {cause} | year={GameManager.year}");

        // Grey out all branch nodes visually via mesh tint
        var mb = GetComponent<TreeMeshBuilder>();
        if (mb != null) mb.SetDeadTint(true);

        // Drop all leaves and bark flakes immediately
        GetComponent<LeafManager>()?.DropAllLeaves();
        GetComponent<BarkFlakerManager>()?.ClearAllFlakes();

        GameManager.Instance.UpdateGameState(GameState.TreeDead);
    }

    // Year Simulation (debug keys 1-9)

    /// <summary>
    /// Instantly simulates one full year of growth with no animation.
    /// </summary>
    public void SimulateYear()
    {
        if (root == null) return;

        GameManager.year++;
        lastGrownYear = GameManager.year;
        GameManager.Instance.TextCallFunction();

        // Mirror StartNewGrowingSeason: growing-season guard + reserve depletion flush.
        bool simIsGrowingSeason = GameManager.month >= 3 && GameManager.month <= 8;
        if (simIsGrowingSeason)
        {
            foreach (var node in allNodes)
            {
                if (!node.isTrimCutPoint) continue;
                node.regrowthSeasonCount++;
                if (node.trimCutDepth + node.regrowthSeasonCount * depthsPerYear >= SeasonDepthCap)
                    node.isTrimCutPoint = false;
            }
            CheckCutCapAbsorption();
        }
        if (cutDepthAccumulatedThisSeason >= heavyPruneThreshold)
            heavyPruneRecoveryActive = true;
        cutDepthAccumulatedThisSeason = 0;

        foreach (var node in allNodes)
        {
            if (node.isGrowing && !node.isTrimmed)
            {
                node.length    = node.targetLength;
                node.isGrowing = false;
            }
        }

        var terminals = new List<TreeNode>();
        foreach (var node in allNodes)
        {
            bool belowCap = node.isRoot
                ? node.depth < maxRootDepth
                : node.depth < SeasonDepthCap && node.depth < CutPointDepthCap(node);

            if (!node.isTrimmed && node.isTerminal && !node.isGrowing && belowCap)
                terminals.Add(node);
        }
        foreach (var terminal in terminals)
            SpawnChildren(terminal);

        bool anyGrowing = true;
        while (anyGrowing)
        {
            anyGrowing = false;
            var growing = new List<TreeNode>();
            foreach (var node in allNodes)
                if (node.isGrowing && !node.isTrimmed) growing.Add(node);

            foreach (var node in growing)
            {
                bool belowCap = node.isRoot
                    ? node.depth < maxRootDepth
                    : node.depth < SeasonDepthCap && node.depth < CutPointDepthCap(node);

                anyGrowing     = true;
                node.length    = node.targetLength;
                node.isGrowing = false;
                // SimulateYear is instant — don't add depth-cap extensions here or
                // the while loop would spin forever. Only allow belowCap or mid-chain.
                bool canSpawn  = belowCap || (!node.isRoot && node.subdivisionsLeft > 0);
                if (canSpawn) SpawnChildren(node);
            }
        }

        RecalculateRadii(root);
        meshBuilder.SetDirty();
    }

    // ── Restart ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Destroys all tree visuals and clears node state so that the next Water
    /// event (when root == null) triggers a fresh InitTree(). Call from the
    /// dead-tree restart button before transitioning to SpeciesSelect.
    /// </summary>
    public void ClearForRestart()
    {
        GetComponent<LeafManager>()?.ClearAllLeaves();
        foreach (var go in budObjects.Values)    if (go != null) Destroy(go);
        budObjects.Clear();
        foreach (var go in lateralBudObjects)    if (go != null) Destroy(go);
        lateralBudObjects.Clear();
        foreach (var go in woundObjects.Values)  if (go != null) Destroy(go);
        woundObjects.Clear();
        if (seedObject != null) { Destroy(seedObject); seedObject = null; }
        allNodes.Clear();
        root = null;
        meshBuilder?.SetDirty();
    }

    // Initialisation

    void InitTree()
    {
        foreach (var go in budObjects.Values)
            if (go != null) Destroy(go);
        budObjects.Clear();

        foreach (var go in lateralBudObjects)
            if (go != null) Destroy(go);
        lateralBudObjects.Clear();

        foreach (var go in woundObjects.Values)
            if (go != null) Destroy(go);
        woundObjects.Clear();

        allNodes.Clear();
        nextId               = 0;
        startYear            = GameManager.year;
        startMonth           = GameManager.month;
        lastGrownYear        = GameManager.year - 1;  // ensure OnGameStateChanged(BranchGrow) fires StartNewGrowingSeason
        lastSnapshotAbsDay  = -1;

        if (species != null) ApplySpecies();

        // Create the seed visual -- an elongated sphere at the soil surface.
        // It disappears once the sprout grows past seedHideLength.
        if (seedObject != null) Destroy(seedObject);
        seedObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        seedObject.name = "_Seed";
        seedObject.transform.SetParent(transform, false);
        seedObject.transform.localPosition = Vector3.zero;
        seedObject.transform.localScale    = new Vector3(0.06f, 0.10f, 0.06f);
        // Remove the collider so it doesn't interfere with tree raycasts
        Destroy(seedObject.GetComponent<Collider>());
        // Use the same material as the trunk so it matches the bark shader.
        var seedRenderer = seedObject.GetComponent<Renderer>();
        if (seedRenderer != null)
        {
            var treeRend = meshBuilder?.GetComponent<MeshRenderer>();
            if (treeRend != null)
                seedRenderer.sharedMaterial = treeRend.sharedMaterial;
        }

        // The first trunk node starts at zero length and grows upward.
        // SpawnChildren will add (trunkSubdivisions - 1) more depth-0 segments
        // before allowing real branching, giving several individually wireable sections.
        float trunkSegLen = branchSegmentLength / Mathf.Max(1, trunkSubdivisions);
        root           = new TreeNode(nextId++, 0, Vector3.zero, Vector3.up,
                                      terminalRadius, trunkSegLen, null);
        root.isGrowing = true;
        allNodes.Add(root);

        // Sprout initial root strands evenly around the seed.
        // Roots also start at zero length and grow during BranchGrow seasons.
        int roots = Mathf.Max(0, initialRootCount);
        for (int i = 0; i < roots; i++)
        {
            float   angle   = (float)i / roots * Mathf.PI * 2f;
            Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 dir     = (outward + Vector3.down * rootInitialPitch).normalized;
            float   len     = Mathf.Max(rootSegmentLength * rootSegmentLengthDecay, 0.3f);
            var r           = CreateNode(root.worldPosition, dir, rootTerminalRadius, len, root);
            r.isRoot        = true;
        }

        Debug.Log($"[Tree] InitTree (seed) year={GameManager.year} | trunk growing | initialRoots={roots}");

        RecalculateRadii(root);
        meshBuilder.SetDirty();
    }

    // Root Planting

    /// <summary>Returns the rootAreaTransform for external systems (e.g. PotSoil sizing).</summary>
    public Transform GetRootAreaTransform() => rootAreaTransform;

    /// <summary>
    /// True when at least one non-trimmed root terminal has accumulated enough
    /// boundary pressure to be considered pot-bound.
    /// </summary>
    public bool IsPotBound()
    {
        foreach (var n in allNodes)
            if (n.isRoot && n.isTerminal && !n.isTrimmed && n.boundaryPressure >= boundaryPressureThreshold)
                return true;
        return false;
    }

    /// <summary>
    /// Plant a fresh evenly-spaced set of root strands from the trunk base.
    /// Called by RootRakeManager.ConfirmRepot() after discarding the old root graph.
    /// If withLongRoot is true, an extra pre-grown long root cable is added in a random direction.
    /// </summary>
    public void RegenerateInitialRoots(bool withLongRoot)
    {
        int count = Mathf.Max(1, initialRootCount);
        for (int i = 0; i < count; i++)
        {
            float   angle   = (float)i / count * Mathf.PI * 2f;
            Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            PlantRoot(outward);
        }

        if (withLongRoot)
        {
            float   angle = Random.Range(0f, Mathf.PI * 2f);
            Vector3 dir   = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            PlantLongRoot(dir);
            hasLongRoot = false;   // consumed
        }

        RecalculateRadii(root);
        meshBuilder.SetDirty();
        Debug.Log($"[Root] RegenerateInitialRoots count={count} withLongRoot={withLongRoot} | year={GameManager.year}");
    }

    /// <summary>
    /// Plants a multi-segment pre-grown root cable that starts longer than normal.
    /// Simulates the long surface root kept by the player during the rake mini-game.
    /// </summary>
    public void PlantLongRoot(Vector3 directionLocal)
    {
        if (root == null) return;
        Vector3 dir = (directionLocal + Vector3.down * rootInitialPitch).normalized;
        float   seg = Mathf.Max(rootSegmentLength * rootSegmentLengthDecay, 0.3f);

        TreeNode prev = root;
        for (int i = 0; i < 4; i++)
        {
            float segLen = seg * Mathf.Max(0.3f, 1f - i * 0.18f);
            var node = CreateNode(prev.tipPosition, dir, rootTerminalRadius, segLen, prev);
            node.isRoot = true;
            node.length = node.targetLength;   // pre-grown
            prev = node;
        }

        RecalculateRadii(root);
        meshBuilder.SetDirty();
        Debug.Log($"[Root] PlantLongRoot dir={directionLocal} | year={GameManager.year}");
    }

    /// <summary>
    /// Plants a new root strand from the base of the trunk.
    /// Called by TreeInteraction when the player clicks the planting surface in RootPrune mode.
    /// </summary>
    public void PlantRoot(Vector3 directionLocal)
    {
        if (root == null) return;

        Vector3 dir = (directionLocal + Vector3.down * rootInitialPitch).normalized;

        float len = Mathf.Max(rootSegmentLength * rootSegmentLengthDecay, 0.3f);

        var newRoot = CreateNode(root.worldPosition, dir, rootTerminalRadius, len, root);
        newRoot.isRoot = true;

        int totalRoots = 0;
        foreach (var n in allNodes) if (n.isRoot) totalRoots++;
        Debug.Log($"[Root] PlantRoot id={newRoot.id} | dir={dir} | len={len:F2} | totalRootNodes={totalRoots}");

        RecalculateRadii(root);
        meshBuilder.SetDirty();
    }

    // ── Air Layering ──────────────────────────────────────────────────────────

    /// <summary>
    /// Places an air layer on a trunk node. The wrap is positioned at the node's
    /// tip and tracked in <see cref="airLayers"/>. After <see cref="airLayerSeasonsToRoot"/>
    /// growing seasons <see cref="AirLayerData.rootsSpawned"/> becomes true;
    /// the player then clicks again to call <see cref="UnwrapAirLayer"/>.
    /// </summary>
    public void PlaceAirLayer(TreeNode node)
    {
        if (node == null || node.isRoot) return;

        // Disallow duplicate layers on the same node.
        foreach (var l in airLayers)
            if (l.node == node) return;

        var layer = new AirLayerData { node = node };

        if (airLayerWrapPrefab != null)
        {
            layer.wrapObject = Instantiate(airLayerWrapPrefab, transform);
        }
        else
        {
            // Placeholder: teal cylinder wrapping the branch at the layer site.
            layer.wrapObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            layer.wrapObject.transform.SetParent(transform, false);
            var mat = new Material(Shader.Find("Standard"));
            mat.color = new Color(0f, 0.75f, 0.75f);
            layer.wrapObject.GetComponent<Renderer>().material = mat;
        }

        SetAirLayerWrapTransform(layer);
        airLayers.Add(layer);
        Debug.Log($"[AirLayer] PlaceAirLayer node={node.id} depth={node.depth}");
    }

    /// <summary>
    /// Spawns air-layer roots radially from the layer node and removes the wrap.
    /// Only call when <see cref="AirLayerData.rootsSpawned"/> is true.
    /// </summary>
    public void UnwrapAirLayer(AirLayerData layer)
    {
        if (layer == null)          { Debug.LogWarning("[AirLayer] UnwrapAirLayer called with null layer"); return; }
        if (!layer.rootsSpawned)    { Debug.LogWarning("[AirLayer] UnwrapAirLayer called but rootsSpawned=false"); return; }

        float spawnRadius = Mathf.Max(layer.node.radius * airLayerRootRadiusMultiplier, terminalRadius);
        float spawnLength = Mathf.Max(airLayerRootTargetLength, 0.1f);

        Debug.Log($"[AirLayer] UnwrapAirLayer firing — node={layer.node.id} spawnRadius={spawnRadius:F4} spawnLength={spawnLength:F3} segments={airLayerRootSegments} nodeRadius={layer.node.radius:F4}");

        float angleStep = 360f / airLayerRootCount;
        for (int i = 0; i < airLayerRootCount; i++)
        {
            float   angle  = i * angleStep * Mathf.Deg2Rad;
            Vector3 radial = new Vector3(Mathf.Cos(angle), -0.15f, Mathf.Sin(angle)).normalized;

            // Spawn a chain of segments per strand, each a child of the previous.
            // Start on the trunk's cylindrical surface (not its center axis) so the
            // first segment doesn't have to travel through the bark to become visible.
            Vector3  radialXZ = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            TreeNode prev     = layer.node;
            Vector3  prevTip  = layer.node.tipPosition + radialXZ * layer.node.radius;
            float    segRadius = spawnRadius;
            for (int s = 0; s < airLayerRootSegments; s++)
            {
                // Slightly vary direction each segment so strands curve naturally.
                Vector3 segDir = (radial + Random.insideUnitSphere * 0.15f).normalized;
                var seg = CreateNode(prevTip, segDir, segRadius, spawnLength, prev);
                seg.isRoot         = true;
                seg.isAirLayerRoot = true;
                seg.radius         = segRadius;
                seg.minRadius      = segRadius;
                seg.length         = spawnLength * 0.4f;   // start short so they visibly grow
                prev    = seg;
                prevTip = seg.tipPosition;
                segRadius *= 0.8f;                  // taper along the strand
            }
        }

        if (layer.wrapObject != null)
        {
            Destroy(layer.wrapObject);
            layer.wrapObject = null;
        }

        // Keep layer in airLayers so UpdateAirLayers() can track post-unwrap
        // root growth seasons and set isSeverable. SeverAirLayer removes it.
        layer.isUnwrapped = true;
        RecalculateRadii(root);
        meshBuilder.SetDirty();
        Debug.Log($"[AirLayer] UnwrapAirLayer done — spawned {airLayerRootCount} air-layer roots");
    }

    /// <summary>
    /// Positions and scales the wrap cylinder to match the current node radius and direction.
    /// Unity's Cylinder primitive is 2 units tall, 1 unit diameter at scale (1,1,1).
    /// We orient the cylinder's Y-axis along growDirection and scale it to sit snugly
    /// outside the branch surface.
    /// </summary>
    void SetAirLayerWrapTransform(AirLayerData layer)
    {
        if (layer.wrapObject == null) return;
        var node = layer.node;
        float wrapRadius = Mathf.Max(node.radius * 4f, 0.04f);
        float wrapHeight = Mathf.Max(node.radius * 4f, 0.04f);
        // Center the band along the segment.
        layer.wrapObject.transform.localPosition = node.worldPosition + node.growDirection * (node.length * 0.5f);
        layer.wrapObject.transform.localRotation = Quaternion.FromToRotation(Vector3.up, node.growDirection);
        // Cylinder: diameter = scale.x, height = scale.y * 2
        layer.wrapObject.transform.localScale    = new Vector3(wrapRadius * 2f, wrapHeight * 0.5f, wrapRadius * 2f);
    }

    /// <summary>
    /// Advances all active air layers by one growing season.
    /// Called from <see cref="StartNewGrowingSeason"/>.
    /// </summary>
    void UpdateAirLayers()
    {
        foreach (var layer in airLayers)
        {
            // Post-unwrap: track root growth seasons toward sever readiness.
            if (layer.isUnwrapped)
            {
                if (!layer.isSeverable)
                {
                    layer.rootGrowSeasons++;
                    if (layer.rootGrowSeasons >= airLayerRootSeasonsToSever)
                    {
                        layer.isSeverable = true;
                        Debug.Log($"[AirLayer] Layer node={layer.node.id} is now severable after {layer.rootGrowSeasons} seasons of root growth.");
                    }
                }
                continue;
            }

            if (layer.rootsSpawned) continue;

            layer.seasonsElapsed++;

            // Keep wrap sized and positioned as the trunk thickens.
            SetAirLayerWrapTransform(layer);

            if (layer.seasonsElapsed >= airLayerSeasonsToRoot)
            {
                layer.rootsSpawned = true;
                // Turn the wrap gold to signal roots are ready to emerge.
                if (layer.wrapObject != null)
                {
                    var rend = layer.wrapObject.GetComponent<Renderer>();
                    if (rend != null) rend.material.color = new Color(0.85f, 0.65f, 0.0f);
                }
                Debug.Log($"[AirLayer] Roots ready — node={layer.node.id} after {layer.seasonsElapsed} seasons. Click the gold wrap to unwrap.");
            }
        }
    }

    /// <summary>True if any unwrapped air layer has grown enough roots to sever.</summary>
    public bool HasSeverableLayer => airLayers.Exists(l => l.isSeverable);

    /// <summary>Returns the first severable layer, or null.</summary>
    public AirLayerData GetFirstSeverableLayer() => airLayers.Find(l => l.isSeverable);

    /// <summary>
    /// Severs the air-layered branch from the trunk, saving the original tree as a backup,
    /// then loads the severed branch + its air-layer roots as the new current tree.
    /// </summary>
    public void SeverAirLayer(AirLayerData layer)
    {
        if (layer == null || !layer.isSeverable)
        {
            Debug.LogWarning("[AirLayer] SeverAirLayer called with null or non-severable layer.");
            return;
        }

        var leafMgr = GetComponent<LeafManager>();

        // ── 1. Wound the cut site on the original tree ────────────────────
        var cutSite = layer.node.parent;
        if (cutSite != null)
        {
            cutSite.hasWound        = true;
            cutSite.woundRadius     = layer.node.radius;
            cutSite.woundFaceNormal = layer.node.growDirection;
            cutSite.woundAge        = 0f;
            cutSite.pasteApplied    = false;
        }

        // ── 2. Detach the severed subtree from the original ───────────────
        layer.node.parent?.children.Remove(layer.node);
        var removed = new List<TreeNode>();
        RemoveSubtree(layer.node, removed);
        RecalculateRadii(root);
        meshBuilder.SetDirty();
        Debug.Log($"[AirLayer] Severed — removed {removed.Count} nodes from original tree, wound applied at node={cutSite?.id}");

        // ── 3. Save modified original (with wound, without severed branch) ─
        SaveManager.SaveOriginal(this, leafMgr);

        // ── 4. Build and load the new tree from the severed subtree ──────
        var newData = BuildSeveredTreeSaveData(layer, removed);
        airLayers.Remove(layer);
        LoadFromSaveData(newData, leafMgr);
        treeOrigin = TreeOrigin.AirLayer;
        SaveManager.ActiveSlotId = null;   // new tree — no slot yet, prompt on first save

        GameManager.Instance.UpdateGameState(GameState.Idle);
        Debug.Log("[AirLayer] New tree loaded from severed air layer.");
    }

    SaveData BuildSeveredTreeSaveData(AirLayerData layer, List<TreeNode> subtreeNodes)
    {
        // Compute new depths with the air layer node as root (depth 0).
        var depthMap = new Dictionary<int, int>();
        AssignSubtreeDepths(layer.node, 0, depthMap);

        var potSoil = GetComponent<PotSoil>();

        var newData = new SaveData
        {
            year      = GameManager.year,
            month     = GameManager.month,
            day       = GameManager.day,
            hour      = GameManager.hour,
            waterings = 0,

            treeEnergy             = treeEnergy,
            soilMoisture           = 0.5f,
            droughtDaysAccumulated = 0f,
            nutrientReserve        = 1f,

            // Keep the same planting surface — air layer roots have already grown to it.
            planNX = plantingNormal.x,  planNY = plantingNormal.y,  planNZ = plantingNormal.z,
            planPX = plantingSurfacePoint.x, planPY = plantingSurfacePoint.y, planPZ = plantingSurfacePoint.z,

            startYear       = GameManager.year,
            startMonth      = GameManager.month,
            lastGrownYear   = lastGrownYear,
            isIshitsukiMode = false,

            // Fresh classic soil for the new pot.
            soilAkadama   = potSoil?.akadama   ?? 0.5f,
            soilPumice    = potSoil?.pumice    ?? 0.3f,
            soilLavaRock  = potSoil?.lavaRock  ?? 0.2f,
            soilPreset    = (int)PotSoil.SoilPreset.ClassicBonsai,
            soilDegradation = 0f,
            soilSaturation  = 0.5f,
            soilSeasonsSinceRepot = 0,
        };

        foreach (var node in subtreeNodes)
        {
            var sn = SaveManager.SerializeNode(node);
            // New root has no parent.
            if (node == layer.node) sn.parentId = -1;
            // Recalculate depths from the new root.
            if (depthMap.TryGetValue(node.id, out int d)) sn.depth = d;
            // Air-layer roots become normal roots now that they're in their own pot.
            if (node.isAirLayerRoot) { sn.isAirLayerRoot = false; }
            // Clear training-wire flag — not applicable in new tree.
            sn.isTrainingWire = false;
            newData.nodes.Add(sn);
        }

        return newData;
    }

    void AssignSubtreeDepths(TreeNode node, int depth, Dictionary<int, int> map)
    {
        map[node.id] = depth;
        foreach (var child in node.children)
            AssignSubtreeDepths(child, depth + 1, map);
    }

    /// <summary>
    /// Sets the surface the tree is resting on and lowers the tree onto it.
    /// Called by TreeInteraction when the player right-clicks a surface in RootPrune mode.
    /// The tree's resting Y is updated to the surface contact point, and subsequent root
    /// growth near the surface will hug it.
    /// </summary>
    public void SetPlantingSurface(Vector3 worldSurfacePoint, Vector3 worldSurfaceNormal)
    {
        plantingSurfacePoint = worldSurfacePoint;
        plantingNormal       = worldSurfaceNormal.normalized;

        // Update the resting Y so the tree lowers to this surface height.
        initY = worldSurfacePoint.y;

        // Lower the tree onto the surface.
        liftTarget = 0f;

        Debug.Log($"[Root] SetPlantingSurface point={worldSurfacePoint} normal={worldSurfaceNormal}");
    }

    // Node Factory

    public TreeNode CreateNode(Vector3 position, Vector3 direction, float radius, float targetLength, TreeNode parent)
    {
        int depth = parent == null ? 0 : parent.depth + 1;
        var node = new TreeNode(nextId++, depth, position, direction, radius, targetLength, parent)
        {
            birthYear        = GameManager.year,
            refinementLevel  = parent?.refinementLevel ?? 0f,
            branchVigor      = parent?.branchVigor ?? 1f,
        };

        // New nodes start thin and ramp toward their target radius as they grow,
        // distributing trunk thickening across the season instead of spiking at spawn.
        // Parent == null is the tree root node itself; it starts visible immediately.
        if (parent != null)
            node.radius = 0f;

        if (parent != null)
            parent.children.Add(node);

        allNodes.Add(node);
        return node;
    }

    // Branching

    void SpawnChildren(TreeNode node)
    {
        // Trunk elongation: depth-0 non-root nodes keep adding depth-0 segments
        // until we reach trunkSubdivisions. Only then does real branching begin.
        // This gives the player several independently-wireable trunk segments.
        if (!node.isRoot && node.depth == 0)
        {
            int trunkCount = 0;
            foreach (var n in allNodes)
                if (!n.isRoot && n.depth == 0) trunkCount++;

            if (trunkCount < trunkSubdivisions)
            {
                float segLen   = branchSegmentLength / trunkSubdivisions;
                var trunkSeg = new TreeNode(nextId++, 0, node.tipPosition,
                                             ContinuationDirection(node),
                                             terminalRadius, segLen, node)
                {
                    birthYear  = GameManager.year,
                    isGrowing  = true,
                };
                node.children.Add(trunkSeg);
                allNodes.Add(trunkSeg);
                Debug.Log($"[Tree] Trunk segment {trunkCount + 1}/{trunkSubdivisions} id={trunkSeg.id}");
                return;
            }
            // All trunk segments grown -- fall through to first real branch
        }

        // Branch subdivision: non-root nodes grow N same-depth sub-segments before branching.
        // Same depth as parent means sub-segments don't consume the season depth cap.
        if (!node.isRoot && node.subdivisionsLeft > 0)
        {
            // Clamp inherited targetLength to maxSegmentLength so any elongation that
            // snuck through doesn't cascade down the rest of the chain.
            float chainSegLen = maxSegmentLength > 0f
                ? Mathf.Min(node.targetLength, maxSegmentLength)
                : node.targetLength;
            var sub = new TreeNode(nextId++, node.depth, node.tipPosition,
                                   ContinuationDirection(node), terminalRadius, chainSegLen, node)
            {
                birthYear        = GameManager.year,
                subdivisionsLeft = node.subdivisionsLeft - 1,
                isGrowing        = true,
            };
            node.children.Add(sub);
            allNodes.Add(sub);
            return;
        }

        float baseSegLen  = node.isRoot ? rootSegmentLength : branchSegmentLength;
        float decay       = node.isRoot ? rootSegmentLengthDecay : segmentLengthDecay;
        float childLength = baseSegLen * Mathf.Pow(decay, node.depth + 1);

        // Each new branch chord is divided into however many segments are needed so
        // neither the fixed branchSubdivisions count nor maxSegmentLength is exceeded.
        int   nodeSubdivs = !node.isRoot ? SubdivsForChord(childLength) : 1;
        float segLength   = (nodeSubdivs > 1) ? childLength / nodeSubdivs : childLength;
        segLength        = Mathf.Max(segLength, node.isRoot ? 0.025f : minSegmentLength);

        float nodeRadius = (node.isRoot ? rootTerminalRadius : terminalRadius) * globalSegmentScale;

        // Root soft spread cap: laterals scale to zero at the target radius;
        // continuation itself stops beyond 1.3× the target radius.
        // Hard node cap enforced here as well as in StartNewGrowingSeason.
        if (node.isRoot)
        {
            int rootCount = 0;
            foreach (var n in allNodes) if (n.isRoot) rootCount++;
            if (rootCount >= maxTotalRootNodes) return;

            float distRatio   = RootDistRatio(node);
            bool  isIshitsuki = isIshitsukiMode;
            if (!isIshitsuki && distRatio >= 1.3f) return;  // hard outer boundary — no further growth

            // Hard spawn clamp: if the tip is already outside the box (side or bottom),
            // don't plant new children at all.  Top-face emergence is allowed.
            if (!isIshitsuki && rootAreaTransform != null)
            {
                Vector3 tipW  = transform.TransformPoint(node.tipPosition);
                Vector3 local = rootAreaTransform.InverseTransformPoint(tipW);
                bool outsideSide   = Mathf.Abs(local.x) > 0.5f || Mathf.Abs(local.z) > 0.5f;
                bool outsideBottom = local.y < -0.5f;
                if (outsideSide || outsideBottom) return;
            }
            if (isIshitsuki)
            {
                Vector3 tipW = transform.TransformPoint(node.tipPosition);
                if (tipW.y <= plantingSurfacePoint.y) return;
            }

            var rootCont = CreateNode(node.tipPosition, ContinuationDirection(node), nodeRadius, segLength, node);
            rootCont.isRoot         = true;
            rootCont.isAirLayerRoot = node.isAirLayerRoot;
            rootCount++;

            float lateralScale  = isIshitsuki ? 0f : Mathf.Clamp01(1f - distRatio);
            float lateralChance = rootLateralChance * Mathf.Pow(rootLateralDepthDecay, node.depth) * lateralScale;
            if (rootCount < maxTotalRootNodes && Random.value < lateralChance)
            {
                var lat = CreateNode(node.tipPosition, LateralDirection(node), nodeRadius, segLength * 0.85f, node);
                lat.isRoot         = true;
                lat.isAirLayerRoot = node.isAirLayerRoot;
                Debug.Log($"[GRoot] SpawnChildren lateral | node={node.id} depth={node.depth} distRatio={distRatio:F2} -> lat id={lat.id}");
            }
            return;
        }

        // Cut-site regrowth: shoots emerge from the side of the wound, never straight
        // through the cap face. Always at least one shoot; often two (epicormic response).
        if (node.isTrimCutPoint)
        {
            var shoot = CreateNode(node.tipPosition, CutSiteDirection(node), nodeRadius, segLength, node);
            shoot.isRoot = false;
            if (nodeSubdivs > 1) shoot.subdivisionsLeft = nodeSubdivs - 1;

            // Second shoot from the opposite side of the cap — common after hard cuts.
            if (Random.value < 0.65f)
            {
                float lat2Len = segLength * 0.85f;
                var shoot2 = CreateNode(node.tipPosition, CutSiteDirection(node), nodeRadius, lat2Len, node);
                shoot2.isRoot = false;
                if (nodeSubdivs > 1) shoot2.subdivisionsLeft = nodeSubdivs - 1;
                GameManager.branches++;
            }
            return;
        }

        if (budType == BudType.Opposite)
        {
            var (dirA, dirB) = OppositeForkDirections(node);
            var forkA = CreateNode(node.tipPosition, dirA, nodeRadius, segLength, node);
            forkA.isRoot = false;
            if (nodeSubdivs > 1) forkA.subdivisionsLeft = nodeSubdivs - 1;
            forkA.growthStartDelay = Random.Range(0f, branchSpawnMaxDelay * 0.3f);
            var forkB = CreateNode(node.tipPosition, dirB, nodeRadius, segLength, node);
            forkB.isRoot = false;
            if (nodeSubdivs > 1) forkB.subdivisionsLeft = nodeSubdivs - 1;
            forkB.growthStartDelay = Random.Range(0f, branchSpawnMaxDelay);
            GameManager.branches++;
        }
        else
        {
            var cont = CreateNode(node.tipPosition, ContinuationDirection(node), nodeRadius, segLength, node);
            cont.isRoot = false;
            if (nodeSubdivs > 1)
                cont.subdivisionsLeft = nodeSubdivs - 1;
            cont.growthStartDelay = Random.Range(0f, branchSpawnMaxDelay * 0.3f);

            float lateralChanceBranch = baseBranchChance * Mathf.Pow(branchChanceDepthDecay, node.depth);
            if (Random.value < lateralChanceBranch)
            {
                float latLength = segLength * 0.85f * Mathf.Max(0.1f, 1f - apicalDominance);
                var lat = CreateNode(node.tipPosition, LateralDirection(node), nodeRadius, latLength, node);
                lat.isRoot = false;
                if (nodeSubdivs > 1)
                    lat.subdivisionsLeft = nodeSubdivs - 1;
                lat.growthStartDelay = Random.Range(0f, branchSpawnMaxDelay);
                GameManager.branches++;
            }
        }
    }

    // Root Spread Helpers

    /// <summary>
    /// Returns the highest tipPosition.y among all non-root branch nodes.
    /// Used to compute the target root spread radius.
    /// </summary>
    float CalculateTreeHeight()
    {
        float h = 0.5f;  // minimum so spread radius is never zero
        foreach (var node in allNodes)
            if (!node.isRoot && node.tipPosition.y > h)
                h = node.tipPosition.y;
        return h;
    }

    /// <summary>
    /// Scores the current root health (surface root flare) 0–100 and updates
    /// RootHealthScore and RootHealthSectorCoverage (8 directional sectors).
    /// Considers shallow trunk roots: depth 1–rootHealthMaxDepth, Y > -rootHealthSurfaceDepth.
    /// Components: angular coverage (50%), girth thickness (30%), radial balance (20%).
    /// </summary>
    public void RecalculateRootHealthScore()
    {
        const int sectors = 8;
        float[] sectorRadius = new float[sectors];
        float   totalRadius  = 0f;
        int     count        = 0;
        Vector2 com          = Vector2.zero;

        foreach (var node in allNodes)
        {
            if (!node.isRoot || node.isTrimmed) continue;
            if (node.depth < 1 || node.depth > rootHealthMaxDepth) continue;
            if (node.tipPosition.y < -rootHealthSurfaceDepth) continue;

            Vector3 tip   = node.tipPosition;
            float   angle = Mathf.Atan2(tip.z, tip.x);            // –π … +π
            float   t     = (angle + Mathf.PI) / (Mathf.PI * 2f); // 0 … 1
            int     s     = Mathf.Clamp(Mathf.FloorToInt(t * sectors), 0, sectors - 1);

            sectorRadius[s] += node.radius;
            totalRadius      += node.radius;
            com              += new Vector2(tip.x, tip.z) * node.radius;
            count++;
        }

        // Normalise sector coverage for the UI (0 = empty, 1 = best-covered sector)
        float maxSector = 0f;
        for (int i = 0; i < sectors; i++) if (sectorRadius[i] > maxSector) maxSector = sectorRadius[i];
        RootHealthSectorCoverage = new float[sectors];
        if (maxSector > 0f)
            for (int i = 0; i < sectors; i++) RootHealthSectorCoverage[i] = sectorRadius[i] / maxSector;

        if (count == 0 || totalRadius <= 0f) { RootHealthScore = 0f; return; }

        // Angular coverage: fraction of the 8 sectors that have any roots
        int coveredSectors = 0;
        for (int i = 0; i < sectors; i++) if (sectorRadius[i] > 0f) coveredSectors++;
        float angularScore = (float)coveredSectors / sectors;

        // Girth: average root radius vs the ideal target radius
        float girthScore = Mathf.Clamp01(totalRadius / count / rootHealthTargetRadius);

        // Balance: centre of mass close to the trunk origin (safe — totalRadius > 0 guaranteed above)
        com /= totalRadius;
        float balanceScore = Mathf.Clamp01(1f - com.magnitude / rootHealthBalanceRadius);

        RootHealthScore = (angularScore * 0.5f + girthScore * 0.3f + balanceScore * 0.2f) * 100f;
        Debug.Log($"[RootHealth] score={RootHealthScore:F1} angular={angularScore:F2} girth={girthScore:F2} balance={balanceScore:F2} sectors={coveredSectors}/8 nodes={count}");
    }

    /// <summary>
    /// Returns the number of sub-segments to use for a branch chord of the given length.
    /// Takes the maximum of branchSubdivisions and however many segments are needed to
    /// keep each one under maxSegmentLength.  Always returns at least 1.
    /// </summary>
    int SubdivsForChord(float chordLength)
    {
        // When maxSegmentLength is set, use it exclusively — branchSubdivisions as a floor
        // creates dozens of micro-segments on short scaled chords (the "sausage" problem).
        if (maxSegmentLength > 0f)
            return Mathf.Max(1, Mathf.CeilToInt(chordLength / maxSegmentLength));
        return Mathf.Max(1, branchSubdivisions);
    }

    /// <summary>
    /// Returns the horizontal distance ratio of a root node's tip to the spread radius.
    /// 0 = at trunk, 1 = at target spread radius, >1 = beyond it.
    /// </summary>
    float RootDistRatio(TreeNode node)
    {
        if (rootAreaTransform != null)
        {
            // Box mode: 0=center, 1=at wall, >1=outside.
            // InverseTransformPoint gives coords in Root Area local space where
            // the box extents are [-0.5, 0.5] on each axis.
            Vector3 worldTip  = transform.TransformPoint(node.tipPosition);
            Vector3 areaLocal = rootAreaTransform.InverseTransformPoint(worldTip);
            float xRatio = Mathf.Abs(areaLocal.x) * 2f;
            float zRatio = Mathf.Abs(areaLocal.z) * 2f;
            // Y: check both floor and ceiling — roots must stay inside the tray height
            float yRatio = Mathf.Abs(areaLocal.y) * 2f;
            return Mathf.Max(xRatio, zRatio, yRatio);
        }
        // Legacy radial fallback
        float spreadRadius = cachedTreeHeight * rootSpreadMultiplier;
        if (spreadRadius <= 0f) return 0f;
        Vector3 tip = node.tipPosition;
        float horizDist = Mathf.Sqrt(tip.x * tip.x + tip.z * tip.z);
        return horizDist / spreadRadius;
    }

    /// <summary>
    /// When a Root Area box is assigned, deflects a root direction away from walls
    /// the root is approaching, so roots run along the inside of the box rather
    /// than stopping dead or punching through.
    /// treeLocalDir and treeLocalPos are in TreeSkeleton local space.
    /// </summary>
    Vector3 DeflectFromRootAreaWalls(Vector3 treeLocalDir, Vector3 treeLocalPos)
    {
        if (rootAreaTransform == null) return treeLocalDir;

        Vector3 worldPos  = transform.TransformPoint(treeLocalPos);
        Vector3 worldDir  = transform.TransformDirection(treeLocalDir);
        Vector3 areaLocal = rootAreaTransform.InverseTransformPoint(worldPos);

        // Margin in normalised box coords (0.5 = half-extent).
        // Within this distance of a wall, start blending toward the wall surface.
        const float margin = 0.20f;

        // Accumulate a weighted inward normal for each nearby face.
        // Side and bottom faces get a stronger push (1.0) than the top face (0.3)
        // so surface roots can emerge naturally while lateral/downward escape is blocked hard.
        Vector3 wallNormal   = Vector3.zero;
        float   totalWeight  = 0f;

        void AddFace(Vector3 faceNormal, float t, float faceWeight)
        {
            float w = t * faceWeight;
            wallNormal  += faceNormal * w;
            totalWeight += w;
        }

        AddFace( rootAreaTransform.right,   Mathf.InverseLerp(0.5f - margin,  0.5f,  areaLocal.x), 1.0f);
        AddFace(-rootAreaTransform.right,   Mathf.InverseLerp(0.5f - margin,  0.5f, -areaLocal.x), 1.0f);
        AddFace( rootAreaTransform.forward, Mathf.InverseLerp(0.5f - margin,  0.5f,  areaLocal.z), 1.0f);
        AddFace(-rootAreaTransform.forward, Mathf.InverseLerp(0.5f - margin,  0.5f, -areaLocal.z), 1.0f);
        // Bottom (floor): strong deflection — roots must not escape downward
        AddFace(-rootAreaTransform.up,      Mathf.InverseLerp(-0.5f + margin, -0.5f, areaLocal.y), 1.0f);
        // Top (soil surface): weak deflection — let roots emerge slightly above soil
        AddFace( rootAreaTransform.up,      Mathf.InverseLerp( 0.5f - margin,  0.5f,  areaLocal.y), 0.3f);

        if (totalWeight > 0.001f)
        {
            wallNormal.Normalize();
            // Blend between along-wall tangent (hard redirect) and the inward normal (push back in).
            // At the wall face the blend is 70% tangent + 30% inward push so roots curve
            // parallel to the wall rather than bouncing straight back.
            Vector3 along = Vector3.ProjectOnPlane(worldDir, wallNormal);
            Vector3 inward = -wallNormal;  // points back toward box interior
            if (along.sqrMagnitude > 0.001f)
            {
                float blend = Mathf.Clamp01(totalWeight);
                worldDir = Vector3.Slerp(worldDir, Vector3.Lerp(along.normalized, inward, 0.3f), blend).normalized;
            }
            else
            {
                worldDir = inward;
            }
        }

        Vector3 result = transform.InverseTransformDirection(worldDir);
        return result.sqrMagnitude > 0.001f ? result.normalized : treeLocalDir;
    }

    // Direction Helpers

    /// <summary>
    /// Continuation direction: inertia + phototropism upward for branches,
    /// inertia + gravity downward for roots. Roots near the planting surface
    /// have their direction deflected to hug the surface.
    /// </summary>
    Vector3 ContinuationDirection(TreeNode node)
    {
        Vector3 rand = Random.insideUnitSphere * randomWeight;

        // Air-layer roots grow downward (gravitropism / anti-phototropism).
        // They deflect along rock surfaces and snap onto the soil plane once they arrive.
        if (node.isAirLayerRoot)
        {
            Vector3 airInertia = (node.growDirection * inertiaWeight + rand).normalized;
            Vector3 dir = Vector3.Slerp(airInertia, Vector3.down, 0.7f).normalized;

            Vector3 worldTip = transform.TransformPoint(node.tipPosition);

            // ── Rock surface deflection ───────────────────────────────────────
            if (rockCollider != null)
            {
                float effectiveRadius = rockInfluenceRadius * 2f;
                Vector3 rockCenter = rockCollider.bounds.center;
                Vector3 toCenter   = (rockCenter - worldTip).normalized;
                Vector3 closestPt  = rockCenter;
                if (rockCollider.Raycast(new Ray(worldTip, toCenter), out RaycastHit cpHit, effectiveRadius * 3f))
                    closestPt = cpHit.point;
                else
                {
                    Vector3 outside = worldTip - toCenter * effectiveRadius * 2f;
                    if (rockCollider.Raycast(new Ray(outside, toCenter), out RaycastHit cpHit2, effectiveRadius * 4f))
                        closestPt = cpHit2.point;
                }
                float distToRock = Vector3.Distance(worldTip, closestPt);
                if (distToRock < effectiveRadius)
                {
                    Vector3 outward = (closestPt - rockCenter).normalized;
                    if (outward.sqrMagnitude < 0.001f) outward = Vector3.up;

                    Vector3 surfaceNormal = outward;
                    Ray normalRay = new Ray(closestPt + outward * 0.5f, -outward);
                    if (rockCollider.Raycast(normalRay, out RaycastHit normalHit, 1f))
                        surfaceNormal = normalHit.normal;

                    Vector3 worldDir   = transform.TransformDirection(dir);
                    Vector3 surfaceDir = Vector3.ProjectOnPlane(worldDir, surfaceNormal);
                    if (surfaceDir.sqrMagnitude < 0.001f)
                        surfaceDir = Vector3.ProjectOnPlane(Vector3.down, surfaceNormal);
                    surfaceDir = (surfaceDir.normalized + Vector3.down * rootGravityWeight * 20f).normalized;

                    float blend = Mathf.Lerp(0.6f, 1.0f, 1f - Mathf.Clamp01(distToRock / effectiveRadius));
                    dir = transform.InverseTransformDirection(
                        Vector3.Slerp(worldDir, surfaceDir, blend).normalized);
                }
            }

            // ── Soil-plane snap ───────────────────────────────────────────────
            Plane soilPlane = new Plane(plantingNormal, plantingSurfacePoint);
            float distToSoil = soilPlane.GetDistanceToPoint(worldTip);
            if (distToSoil >= 0f && distToSoil < rootSurfaceSnapDist)
            {
                Vector3 surfaceDir = Vector3.ProjectOnPlane(dir, plantingNormal);
                if (surfaceDir.sqrMagnitude > 0.001f)
                {
                    float blend = 1f - Mathf.Clamp01(distToSoil / rootSurfaceSnapDist);
                    dir = Vector3.Slerp(dir, surfaceDir.normalized, blend).normalized;
                }
            }

            return dir;
        }

        if (node.isRoot)
        {
            // Outward radial direction from trunk base, projected flat.
            // This is the primary bias — keeps roots spreading wide near the surface.
            Vector3 trunkBase = root != null ? root.worldPosition : Vector3.zero;
            Vector3 radial    = node.worldPosition - trunkBase;
            radial.y = 0f;
            if (radial.sqrMagnitude < 0.001f)
                radial = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f));
            radial = radial.normalized;

            // Ishitsuki: suppress radial spread on the rock face — roots flow DOWN, not outward.
            bool isIshitsuki = isIshitsukiMode;
            Vector3 dir = (node.growDirection * inertiaWeight
                          + (isIshitsuki ? Vector3.zero : radial * rootRadialWeight)
                          + Vector3.down      * rootGravityWeight
                          + rand).normalized;

            Vector3 worldTip = transform.TransformPoint(node.tipPosition);
            bool nearRock = false;

            // ── Rock surface deflection (Ishitsuki) ───────────────────────────
            // Note: all pre-grown Ishitsuki cables are handled by PreGrowRootsToSoil.
            // This block only fires for any organic root growth that slips through
            // (e.g. auto-planted trunk roots before their first pre-grow pass).
            // Use a raycast-based closest-point approximation instead of
            // Physics.ClosestPoint, which requires a convex MeshCollider.
            if (rockCollider != null)
            {
                float effectiveRadius = rockInfluenceRadius * 2f;

                // Approximate closest surface point: cast a ray from worldTip toward
                // the rock centre; the hit point is on the surface facing us.
                Vector3 rockCenter = rockCollider.bounds.center;
                Vector3 toCenter   = (rockCenter - worldTip).normalized;
                Vector3 closestPt  = rockCenter; // fallback
                if (rockCollider.Raycast(new Ray(worldTip, toCenter), out RaycastHit cpHit, effectiveRadius * 3f))
                    closestPt = cpHit.point;
                else
                {
                    // Also try from outside in (root may be inside/near the rock)
                    Vector3 outside = worldTip - toCenter * effectiveRadius * 2f;
                    if (rockCollider.Raycast(new Ray(outside, toCenter), out RaycastHit cpHit2, effectiveRadius * 4f))
                        closestPt = cpHit2.point;
                }
                float distToRock = Vector3.Distance(worldTip, closestPt);

                if (distToRock < effectiveRadius)
                {
                    nearRock = true;

                    // Get surface normal via raycast from outside inward (world space).
                    Vector3 outward    = closestPt - rockCenter;
                    if (outward.sqrMagnitude < 0.001f) outward = Vector3.up;
                    outward.Normalize();

                    Vector3 surfaceNormal = outward;
                    Ray normalRay = new Ray(closestPt + outward * 0.5f, -outward);
                    if (rockCollider.Raycast(normalRay, out RaycastHit normalHit, 1f))
                        surfaceNormal = normalHit.normal;

                    // dir is in local space — convert to world for the projection.
                    Vector3 worldDir = transform.TransformDirection(dir);

                    // Project onto rock surface, fall back to pure-down if tangent is zero.
                    Vector3 surfaceDir = Vector3.ProjectOnPlane(worldDir, surfaceNormal);
                    if (surfaceDir.sqrMagnitude < 0.001f)
                        surfaceDir = Vector3.ProjectOnPlane(Vector3.down, surfaceNormal);

                    // On near-horizontal surfaces (top of rock) gravity is perpendicular
                    // to the plane, so adding it then re-projecting would kill it entirely.
                    // Instead: push radially outward toward the rock edge so the root flows
                    // off the top and down the side, THEN let gravity do its work on the face.
                    float upDot = Vector3.Dot(surfaceNormal, Vector3.up);
                    if (upDot > 0.5f)
                    {
                        Vector3 radialOut = worldTip - rockCollider.bounds.center;
                        radialOut.y = 0f;
                        if (radialOut.sqrMagnitude > 0.001f)
                        {
                            Vector3 edgePush = Vector3.ProjectOnPlane(radialOut.normalized, surfaceNormal);
                            if (edgePush.sqrMagnitude > 0.001f)
                                surfaceDir = (surfaceDir.normalized + edgePush.normalized * upDot).normalized;
                        }
                    }

                    // Gravity bias — NOT re-projected, so it works on all surface angles.
                    // On vertical faces this pulls strongly downward; on horizontal faces
                    // the slight inward lean is harmless (segments are short).
                    surfaceDir = (surfaceDir.normalized + Vector3.down * rootGravityWeight * 20f).normalized;

                    // Full adhesion regardless of height — real Ishitsuki roots grip the
                    // rock all the way from crown to soil, never float off mid-face.
                    // Blend: 1.0 on surface → 0.6 at far edge of influence.
                    float blend = Mathf.Lerp(0.6f, 1.0f,
                        1f - Mathf.Clamp01(distToRock / effectiveRadius));
                    dir = transform.InverseTransformDirection(
                        Vector3.Slerp(worldDir, surfaceDir, blend).normalized);
                }
            }

            // Roots in free air past the rock edge: fall nearly straight down to reach soil.
            if (!nearRock)
            {
                float heightAboveSoil = transform.TransformPoint(node.tipPosition).y - plantingSurfacePoint.y;
                if (heightAboveSoil > 0.05f)
                {
                    // 0.95 max blend → almost straight down; faster than before so
                    // roots don't hang horizontally past the rock edge.
                    float fallBlend = Mathf.Clamp01(heightAboveSoil / 0.3f) * (isIshitsuki ? 0.95f : 0.85f);
                    Vector3 worldDirFall = transform.TransformDirection(dir);
                    worldDirFall = Vector3.Slerp(worldDirFall, Vector3.down, fallBlend).normalized;
                    dir = transform.InverseTransformDirection(worldDirFall);
                }
            }

            // When near the planting surface, blend toward a surface-tangent direction
            // so roots flow along the soil face instead of going through it.
            Plane surface = new Plane(plantingNormal, plantingSurfacePoint);
            float distToSurface = surface.GetDistanceToPoint(worldTip);

            if (distToSurface >= 0f && distToSurface < rootSurfaceSnapDist)
            {
                Vector3 surfaceDir = Vector3.ProjectOnPlane(dir, plantingNormal);
                if (surfaceDir.sqrMagnitude > 0.001f)
                {
                    float blend = 1f - Mathf.Clamp01(distToSurface / rootSurfaceSnapDist);
                    dir = Vector3.Slerp(dir, surfaceDir.normalized, blend).normalized;
                }
            }

            // Clamp: roots must never grow upward — EXCEPT when near the rock,
            // where they may need to crest an edge to get over the side.
            if (!nearRock && dir.y > 0f)
            {
                dir = Vector3.ProjectOnPlane(dir, Vector3.up);
                if (dir.sqrMagnitude < 0.001f)
                    dir = radial;
                dir.Normalize();
            }

            dir = DeflectFromRootAreaWalls(dir, node.tipPosition);
            return dir;
        }
        // Slerp toward sun so phototropismWeight is a direct blend fraction (0=none, 1=point straight up)
        Vector3 inertiaDir = (node.growDirection * inertiaWeight + rand).normalized;
        return Vector3.Slerp(inertiaDir, SunDirection(), phototropismWeight);
    }

    /// <summary>
    /// Lateral direction for both branches (splay + upward bias) and roots (splay + downward bias).
    /// </summary>
    Vector3 LateralDirection(TreeNode node)
    {
        Vector3 perp = Vector3.Cross(node.growDirection, Random.insideUnitSphere).normalized;
        if (perp.sqrMagnitude < 0.001f)
            perp = Vector3.Cross(node.growDirection, Vector3.right).normalized;

        float   angle = Random.Range(branchAngleMin, branchAngleMax);
        Vector3 dir   = Vector3.Slerp(node.growDirection, perp, angle / 90f);

        if (node.isRoot)
        {
            Vector3 trunkBase  = root != null ? root.worldPosition : Vector3.zero;
            Vector3 rootRadial = node.worldPosition - trunkBase;
            rootRadial.y = 0f;
            if (rootRadial.sqrMagnitude > 0.001f) rootRadial.Normalize();
            Vector3 bias    = rootRadial * rootRadialWeight * 0.5f + Vector3.down * rootGravityWeight * 0.5f;
            Vector3 lateral = (dir + bias).normalized;
            // Clamp: lateral roots must not grow upward
            if (lateral.y > 0f)
            {
                lateral = Vector3.ProjectOnPlane(lateral, Vector3.up);
                if (lateral.sqrMagnitude < 0.001f) lateral = rootRadial;
                lateral.Normalize();
            }
            lateral = DeflectFromRootAreaWalls(lateral, node.tipPosition);
            return lateral;
        }
        // Lateral branches get half the phototropism blend of continuation segments
        return Vector3.Slerp(dir, SunDirection(), phototropismWeight * 0.5f);
    }

    // Keep old name as alias so any external callers don't break.
    Vector3 LateralBranchDirection(TreeNode node) => LateralDirection(node);

    /// <summary>
    /// Once a wound has healed and the surrounding wood has thickened enough to
    /// visually absorb the cut cap, clear isTrimCutPoint so the tip reverts to
    /// normal taper behaviour (no more flat disc or forced lateral regrowth).
    /// </summary>
    void CheckCutCapAbsorption()
    {
        foreach (var node in allNodes)
        {
            if (!node.isTrimCutPoint || node.hasWound) continue;
            if (node.parent == null) continue;
            // Parent's callousing wood has grown large enough to swallow the cap.
            if (node.parent.radius >= node.radius * 0.92f)
            {
                node.isTrimCutPoint = false;
                Debug.Log($"[Cap] Node {node.id} cut cap absorbed (parent.r={node.parent.radius:F3} ≥ {node.radius * 0.92f:F3})");
                meshBuilder.SetDirty();
            }
        }
    }

    /// <summary>
    /// Direction for shoots sprouting from a cut stump.
    /// Always a strong lateral (40–70°) so growth emerges from the side of the
    /// cap rather than punching straight through the cut face.
    /// </summary>
    Vector3 CutSiteDirection(TreeNode node)
    {
        Vector3 perp = Vector3.Cross(node.growDirection, Random.insideUnitSphere).normalized;
        if (perp.sqrMagnitude < 0.001f)
            perp = Vector3.Cross(node.growDirection, Vector3.right).normalized;
        float   angle = Random.Range(40f, 70f);
        Vector3 dir   = Vector3.Slerp(node.growDirection, perp, angle / 90f);
        return Vector3.Slerp(dir, SunDirection(), phototropismWeight * 0.5f);
    }

    /// <summary>
    /// Returns two symmetric fork directions for Opposite budding.
    /// Both branches diverge equally from the parent's grow direction,
    /// mirrored across a random perpendicular axis.
    /// </summary>
    (Vector3 a, Vector3 b) OppositeForkDirections(TreeNode node)
    {
        Vector3 perp = Vector3.Cross(node.growDirection, Random.insideUnitSphere).normalized;
        if (perp.sqrMagnitude < 0.001f)
            perp = Vector3.Cross(node.growDirection, Vector3.right).normalized;

        float halfAngle = Random.Range(branchAngleMin, branchAngleMax) * 0.5f;
        Vector3 dir1 = Quaternion.AngleAxis( halfAngle, perp) * node.growDirection;
        Vector3 dir2 = Quaternion.AngleAxis(-halfAngle, perp) * node.growDirection;

        // Same phototropism blend as laterals
        dir1 = Vector3.Slerp(dir1, SunDirection(), phototropismWeight * 0.5f).normalized;
        dir2 = Vector3.Slerp(dir2, SunDirection(), phototropismWeight * 0.5f).normalized;
        return (dir1, dir2);
    }

    /// <summary>
    /// Direction toward the sun in tree-local space.
    /// Converts world up so phototropism works correctly when the tree is tilted on a rock.
    /// </summary>
    Vector3 SunDirection()
    {
        return transform.InverseTransformDirection(Vector3.up);
    }

    // Pipe Model

    /// <summary>
    /// Recalculates all node radii bottom-up using da Vinci's pipe model:
    ///     parent.radius^2 = sum(child.radius^2)
    /// </summary>
    readonly Stopwatch radiiTimer = new Stopwatch();

    public void RecalculateRadii(TreeNode node)
    {
        // Time only the root call so we get one measurement per full traversal
        bool isRootCall = (node == root);
        if (isRootCall) radiiTimer.Restart();

        RecalculateRadiiInternal(node);

        // After the pipe model runs, override air-layer root radii so they track
        // the trunk's growth and never get swallowed. Must run after the main pass
        // so the pipe model doesn't immediately overwrite them.
        if (isRootCall) ScaleAirLayerRootRadii();
        if (isRootCall) ScaleIshitsukiCableRadii();

        if (isRootCall)
        {
            radiiTimer.Stop();
            if (radiiTimer.ElapsedMilliseconds > 0)
                Debug.Log($"[Perf] RecalculateRadii nodes={allNodes.Count} took {radiiTimer.ElapsedMilliseconds}ms");
        }
    }

    /// <summary>
    /// Keeps air-layer root radii proportional to their trunk parent as the tree thickens.
    /// Walks each isAirLayerRoot node up the chain to find the first non-air-layer ancestor
    /// (the trunk node), then sets radius = trunkRadius * multiplier * taper^depth.
    /// </summary>
    /// <summary>
    /// Each frame: re-anchor every air-layer root base to its parent's current tip
    /// so strands don't get swallowed as the trunk node grows longer.
    /// Multiple passes handle chains: pass N propagates corrections N links deep.
    /// </summary>
    void UpdateAirLayerRootPositions()
    {
        if (allNodes == null) return;
        for (int pass = 0; pass < airLayerRootSegments; pass++)
        {
            foreach (var node in allNodes)
            {
                if (!node.isAirLayerRoot || node.parent == null) continue;

                if (node.parent.isAirLayerRoot)
                {
                    // Chain segment: just track parent tip directly.
                    node.worldPosition = node.parent.tipPosition;
                }
                else
                {
                    // First segment: anchor to the cylindrical surface of the trunk node,
                    // not its center, so the root visually emerges from the bark.
                    // Derive the radial direction from the current XZ offset; fall back
                    // to the node's grow direction on the first frame (when offset == 0).
                    Vector3 offset    = node.worldPosition - node.parent.tipPosition;
                    Vector3 radialDir = new Vector3(offset.x, 0f, offset.z);
                    if (radialDir.sqrMagnitude < 0.0001f)
                        radialDir = new Vector3(node.growDirection.x, 0f, node.growDirection.z);
                    if (radialDir.sqrMagnitude < 0.0001f)
                        radialDir = Vector3.right;
                    node.worldPosition = node.parent.tipPosition + radialDir.normalized * node.parent.radius;
                }
            }
        }
    }

    void ScaleAirLayerRootRadii()
    {
        foreach (var node in allNodes)
        {
            if (!node.isAirLayerRoot) continue;

            // Walk up to find the trunk node and how deep in the strand this segment is.
            int      chainDepth = 0;
            TreeNode trunkNode  = node.parent;
            while (trunkNode != null && trunkNode.isAirLayerRoot)
            {
                chainDepth++;
                trunkNode = trunkNode.parent;
            }
            if (trunkNode == null) continue;

            float r = Mathf.Max(
                trunkNode.radius * airLayerRootRadiusMultiplier * Mathf.Pow(0.8f, chainDepth),
                terminalRadius);
            node.radius    = r;
            node.minRadius = r;
        }
    }

    /// <summary>
    /// Scales Ishitsuki pre-grown cable radii proportional to trunk thickness each season.
    /// startNodes (direct root children of root) are set to trunkRadius * multiplier.
    /// Each cable segment tapers toward rootTerminalRadius with distance from the startNode.
    /// Must run after RecalculateRadiiInternal so it overrides the pipe-model floor.
    /// </summary>
    void ScaleIshitsukiCableRadii()
    {
        if (!isIshitsukiMode) return;

        // trunk radius is whatever the pipe model computed for the tree base
        float trunkRadius = root.radius;
        if (trunkRadius <= 0f) trunkRadius = rootTerminalRadius;

        // Scale startNodes (direct root children that own cable chains)
        foreach (var child in root.children)
        {
            if (!child.isRoot || child.isTrainingWire) continue;
            float r = Mathf.Max(trunkRadius * ishitsukiCableRadiusMultiplier, rootTerminalRadius);
            child.radius    = r;
            child.minRadius = r;
        }

        // Scale each pre-grown cable node by its depth in the chain from its startNode
        foreach (var node in allNodes)
        {
            if (!node.isTrainingWire) continue;

            // Walk up to find the startNode (first non-training-wire root ancestor)
            int      chainDepth = 0;
            TreeNode ancestor   = node.parent;
            while (ancestor != null && ancestor.isTrainingWire)
            {
                chainDepth++;
                ancestor = ancestor.parent;
            }

            float r = Mathf.Max(
                trunkRadius * ishitsukiCableRadiusMultiplier * Mathf.Pow(0.82f, chainDepth + 1),
                rootTerminalRadius);
            node.radius    = r;
            node.minRadius = r;
        }
    }

    void RecalculateRadiiInternal(TreeNode node)
    {
        if (node.isTerminal) return;

        float sumOfSquares = 0f;
        foreach (var child in node.children)
        {
            RecalculateRadiiInternal(child);
            // Root children don't contribute to branch pipe-model radii — they would
            // otherwise inflate the trunk as the root system grows exponentially.
            if (!node.isRoot && child.isRoot) continue;
            sumOfSquares += child.radius * child.radius;
        }
        float pipeRadius = Mathf.Sqrt(sumOfSquares);
        node.radius    = Mathf.Max(pipeRadius, node.minRadius);
        node.minRadius = node.radius;
    }

    // Bud System

    /// <summary>
    /// Called at season end (TimeGo). Sets hasBud on all eligible terminal branch nodes
    /// and spawns a bud prefab at each tip position.
    /// </summary>
    void SetBuds()
    {
        int termCount = 0;
        int latCount  = 0;
        foreach (var node in allNodes)
        {
            if (node.isTrimmed || node.isRoot) continue;

            if (node.isTerminal)
            {
                // subdivisionsLeft > 0 means this is a mid-chord sub-segment that was
                // stopped before completing. Resume it in spring instead of treating it
                // as a real tip — SetBuds skips it; StartNewGrowingSeason resumes it.
                if (node.subdivisionsLeft > 0) continue;
                if (node.hasBud) continue;

                node.hasBud = true;
                if (showTerminalBuds && budPrefab != null)
                {
                    var bud = Instantiate(budPrefab, transform);
                    bud.transform.localPosition = node.tipPosition;
                    bud.transform.localRotation = Quaternion.LookRotation(node.growDirection);
                    budObjects[node.id] = bud;
                }
                termCount++;
            }
            else
            {
                // Junction node — spawn a lateral (axillary) bud visual.
                // Skip sub-segment junctions (they're part of a single wireable chord, not real forks).
                bool isSubJunction = node.children.Count == 1 && node.children[0].depth == node.depth;
                if (isSubJunction) continue;

                if (showLateralBuds && lateralBudPrefab != null)
                {
                    var latBud = Instantiate(lateralBudPrefab, transform);
                    latBud.transform.localPosition = node.tipPosition;
                    latBud.transform.localRotation = Quaternion.LookRotation(node.growDirection);
                    lateralBudObjects.Add(latBud);
                    latCount++;
                }
            }
        }
        // Canopy energy: computed from current leaf state (peak summer canopy).
        // Stored on treeEnergy and used as a multiplier next spring.
        var lm = GetComponent<LeafManager>();
        if (lm != null)
        {
            treeEnergy = lm.ComputeTreeEnergy(allNodes, Mathf.Clamp(maxEnergyMultiplier, 1f, 3f));
            treeEnergy = Mathf.Max(0.4f, treeEnergy);  // floor: leafless trees still grow at base rate
        }
        Debug.Log($"[Bud] Buds set | terminal={termCount} lateral={latCount} treeEnergy={treeEnergy:F2} year={GameManager.year}");
    }

    // Trimming

    /// <summary>
    /// Pinches the soft growing tip of a terminal node.
    /// Unlike TrimNode, no wound is created and leaves remain on the node.
    /// The tip stops growing; back-buds are stimulated on nearby ancestors.
    /// </summary>
    public void PinchNode(TreeNode node)
    {
        if (node == null || !node.isTerminal || node.isRoot || node.isTrimmed)
        {
            Debug.Log("[Pinch] Ignored: node must be a non-root, non-trimmed terminal.");
            return;
        }

        // Stop growth at current length — soft tissue only, no woody wound.
        // Do NOT set isTrimmed: that would exclude this node from autumn SetBuds,
        // preventing any regrowth next spring. Instead freeze it in place so
        // StartNewGrowingSeason's mid-chord resume skips it (length == targetLength)
        // and SetBuds sets hasBud in autumn → bud break next spring → normal branching.
        node.isGrowing = false;
        node.length    = node.targetLength;

        // Refinement gain (same rate as a trim cut but scaled by vigor — less effective on strong vigorous growth)
        float refGain = refinementOnTrim / Mathf.Max(0.5f, node.branchVigor);
        node.refinementLevel = Mathf.Min(node.refinementLevel + refGain, refinementCap);

        // Vigor reduction — pinching weakens the tip slightly
        node.branchVigor = Mathf.Max(vigorMin, node.branchVigor * vigorTrimMultiplier);

        // Light health cost — much less than a hard cut (soft tissue only)
        ApplyDamage(node, DamageType.TrimTrauma, trimTraumaDamage * 0.25f);

        // Back-bud stimulation on up to 2 ancestors
        TreeNode ancestor = node.parent;
        for (int i = 0; i < 2 && ancestor != null && ancestor != root; i++)
        {
            ancestor.backBudStimulated = true;
            ancestor = ancestor.parent;
        }

        meshBuilder.SetDirty();
        Debug.Log($"[Pinch] Pinched node={node.id} depth={node.depth} refLevel={node.refinementLevel:F2} vigor={node.branchVigor:F2}");
    }

    /// Removes a node and all its descendants.
    /// </summary>
    public void TrimNode(TreeNode node)
    {
        if (node == root)
        {
            Debug.LogWarning("TreeSkeleton: cannot trim the root node.");
            return;
        }

        TreeNode parent = node.parent;

        // Capture undo state before any modifications
        pendingUndo = CaptureTrimUndoState(node, parent);

        parent?.children.Remove(node);

        var removed = new List<TreeNode>();
        RemoveSubtree(node, removed);

        if (node.isRoot)
            Debug.Log($"[Root] TrimRoot node={node.id} depth={node.depth} | removed={removed.Count} nodes");

        if (parent != null && parent.isTerminal)
        {
            parent.isTrimCutPoint = true;
            parent.trimCutDepth   = parent.depth;

            // Winter pruning: deep cuts in dormancy (months 11–2) that exceed the
            // threshold start with a head-start season count to reflect winter callusing,
            // but actual recovery is slowed by severity and reserve-depletion effects.
            int m = GameManager.month;
            bool isWinterCut = m == 11 || m == 12 || m == 1 || m == 2;
            if (isWinterCut && winterDormantDepthThreshold > 0 && parent.depth >= winterDormantDepthThreshold)
                parent.regrowthSeasonCount = 2;
            else
                parent.regrowthSeasonCount = 0;

            // Vigorous shoots refine slower (more wood per cut = less structural change per trim).
            float refGain = refinementOnTrim / Mathf.Max(0.5f, parent.branchVigor);
            parent.refinementLevel = Mathf.Min(parent.refinementLevel + refGain, refinementCap);

            // Cutting the tip reduces local vigor — energy that was driving extension is lost.
            parent.branchVigor = Mathf.Max(vigorMin, parent.branchVigor * vigorTrimMultiplier);
        }

        // Back-budding: stimulate the nearest 3 non-root ancestors so they have a
        // boosted chance to sprout a lateral next spring.
        if (!node.isRoot)
        {
            int stimulated = 0;
            TreeNode ancestor = parent;
            while (ancestor != null && stimulated < 3)
            {
                if (!ancestor.isRoot)
                {
                    ancestor.backBudStimulated = true;
                    stimulated++;
                }
                ancestor = ancestor.parent;
            }
        }

        // Wound: mark the exposed cut face and spawn a visualization.
        // Subdivision cut (same depth) = tip nip — small ring.
        // Real branch cut (deeper node) = full callus wound.
        if (parent != null && !parent.isRoot && parent != root)
        {
            bool isSubdivisionCut  = (node.depth == parent.depth);
            parent.hasWound        = true;
            parent.woundRadius     = isSubdivisionCut ? node.radius * 0.35f : node.radius;
            parent.woundFaceNormal = isSubdivisionCut ? parent.growDirection : node.growDirection;
            parent.woundAge        = 0f;
            parent.pasteApplied    = false;
            CreateWoundObject(parent);

            // Trim trauma: small health hit on the cut site. Accumulates with repeated cuts;
            // recovers at trimTraumaRecoveryPerSeason each spring.
            ApplyDamage(parent, DamageType.TrimTrauma, trimTraumaDamage);
        }

        // Winter pruning: accumulate cut depth for heavy-prune reserve-depletion tracking.
        if (!node.isRoot)
        {
            int removedBranchNodes = 0;
            foreach (var r in removed) if (!r.isRoot) removedBranchNodes++;
            cutDepthAccumulatedThisSeason += removedBranchNodes;
        }

        OnSubtreeTrimmed?.Invoke(removed);
        RecalculateRadii(root);
        meshBuilder.SetDirty();
        cachedTreeHeight = CalculateTreeHeight();  // keep cinematic camera zoom current
    }

    // ── Trim undo data ────────────────────────────────────────────────────────

    class TrimUndoState
    {
        public TreeNode       subtreeRoot;
        public List<TreeNode> subtreeNodes;   // every node in the subtree
        public TreeNode       parent;

        // Parent state before the trim
        public bool    isTrimCutPoint;
        public int     trimCutDepth;
        public int     regrowthSeasonCount;
        public float   health;

        // Parent wound state before the trim (may have had a pre-existing wound)
        public bool    hasWound;
        public float   woundRadius;
        public Vector3 woundFaceNormal;
        public float   woundAge;
        public bool    pasteApplied;

        // Ancestor backBudStimulated states before the trim (up to 3)
        public List<(TreeNode node, bool wasStimulated)> ancestorStates;

        public float timestamp;   // Time.realtimeSinceStartup when the trim fired
    }

    TrimUndoState CaptureTrimUndoState(TreeNode subtreeRoot, TreeNode parent)
    {
        var s = new TrimUndoState
        {
            subtreeRoot  = subtreeRoot,
            subtreeNodes = new List<TreeNode>(),
            parent       = parent,
            timestamp    = Time.realtimeSinceStartup,
        };

        CollectSubtreeNodes(subtreeRoot, s.subtreeNodes);

        if (parent != null)
        {
            s.isTrimCutPoint      = parent.isTrimCutPoint;
            s.trimCutDepth        = parent.trimCutDepth;
            s.regrowthSeasonCount = parent.regrowthSeasonCount;
            s.health              = parent.health;
            s.hasWound            = parent.hasWound;
            s.woundRadius         = parent.woundRadius;
            s.woundFaceNormal     = parent.woundFaceNormal;
            s.woundAge            = parent.woundAge;
            s.pasteApplied        = parent.pasteApplied;
        }

        s.ancestorStates = new List<(TreeNode, bool)>();
        int count = 0;
        var anc = parent;
        while (anc != null && count < 3)
        {
            if (!anc.isRoot)
            {
                s.ancestorStates.Add((anc, anc.backBudStimulated));
                count++;
            }
            anc = anc.parent;
        }

        return s;
    }

    void CollectSubtreeNodes(TreeNode node, List<TreeNode> result)
    {
        result.Add(node);
        foreach (var child in node.children)
            CollectSubtreeNodes(child, result);
    }

    // ── Branch Promotion Advisor ──────────────────────────────────────────────

    bool IsAncestorOf(TreeNode ancestor, TreeNode node)
    {
        var cur = node.parent;
        while (cur != null)
        {
            if (cur == ancestor) return true;
            cur = cur.parent;
        }
        return false;
    }

    /// <summary>
    /// Scores how much removing/reducing <paramref name="candidate"/> would benefit
    /// <paramref name="target"/>. Returns 0–1 (higher = remove first), or -1 if ineligible.
    /// </summary>
    public float PromotionScore(TreeNode candidate, TreeNode target)
    {
        if (candidate == target) return -1f;
        if (candidate.isRoot || candidate.isTrimmed || candidate.isDead) return -1f;
        if (IsAncestorOf(candidate, target)) return -1f;

        // Apical dominance: shallower nodes suppress deeper ones more.
        float depthFactor  = 1f - Mathf.Clamp01((candidate.depth - 1) / 5f);
        // Radius: thicker = more resource draw.
        float radiusFactor = Mathf.Clamp01(candidate.radius / 0.25f);
        // Vigor: high-vigor = dominant drain.
        float vigorFactor  = Mathf.Clamp01((candidate.branchVigor - 0.2f) / 1.8f);
        // Directional competition: growing toward the target's light cone.
        float dirFactor = 0f;
        Vector3 toTarget = target.tipPosition - candidate.worldPosition;
        if (toTarget.sqrMagnitude > 0.0001f)
            dirFactor = Mathf.Clamp01((Vector3.Dot(candidate.growDirection.normalized, toTarget.normalized) + 1f) * 0.5f);

        return Mathf.Clamp01(depthFactor * 0.35f + radiusFactor * 0.25f + vigorFactor * 0.20f + dirFactor * 0.20f);
    }

    /// <summary>Returns "Remove", "Trim back", or "Pinch" based on score and node type.</summary>
    public static string PromotionAction(TreeNode candidate, float score)
    {
        if (score > 0.65f) return "Remove";
        if (score > 0.35f) return candidate.isTerminal ? "Pinch" : "Trim back";
        return "Trim back";
    }

    /// <summary>Returns the ideal season string for the recommended action.</summary>
    public static string BestPromotionSeason(TreeNode candidate, string action) => action switch
    {
        "Remove"    => "Late Winter (Jan–Feb)",
        "Pinch"     => "Spring (Apr–May)",
        _           => "Summer (Jun–Jul)",
    };

    /// <summary>
    /// Restores the last trim if called within the undo window (default 5 seconds).
    /// Leaves are re-spawned fresh on the restored terminals — the fall animation plays
    /// on trim and new leaves pop back on undo.
    /// </summary>
    public void UndoLastTrim()
    {
        if (!CanUndo) return;
        var u = pendingUndo;
        pendingUndo = null;

        // Re-attach the subtree
        u.parent.children.Add(u.subtreeRoot);
        foreach (var n in u.subtreeNodes)
        {
            n.isTrimmed = false;
            allNodes.Add(n);
        }

        // Restore parent fields
        u.parent.isTrimCutPoint      = u.isTrimCutPoint;
        u.parent.trimCutDepth        = u.trimCutDepth;
        u.parent.regrowthSeasonCount = u.regrowthSeasonCount;
        u.parent.health              = u.health;

        // Destroy the wound object the trim created, then restore the pre-trim state
        if (woundObjects.TryGetValue(u.parent.id, out var wGo))
        {
            Destroy(wGo);
            woundObjects.Remove(u.parent.id);
        }
        u.parent.hasWound        = u.hasWound;
        u.parent.woundRadius     = u.woundRadius;
        u.parent.woundFaceNormal = u.woundFaceNormal;
        u.parent.woundAge        = u.woundAge;
        u.parent.pasteApplied    = u.pasteApplied;
        if (u.hasWound)
            CreateWoundObject(u.parent);

        // Restore ancestor back-bud flags
        foreach (var (node, wasStimulated) in u.ancestorStates)
            node.backBudStimulated = wasStimulated;

        // Re-spawn leaves on restored terminal branch nodes only during leaf season.
        // If undo fires in winter (time skipped past August while window was open),
        // skip the spawn — the nodes will get leaves naturally next spring.
        if (GameManager.SeasonalGrowthRate > 0f)
        {
            var leafManager = GetComponent<LeafManager>();
            if (leafManager != null)
            {
                var terminals = new List<TreeNode>();
                foreach (var n in u.subtreeNodes)
                    if (!n.isRoot && n.isTerminal) terminals.Add(n);
                leafManager.ForceSpawnLeaves(terminals);
            }
        }

        RecalculateRadii(root);
        meshBuilder.SetDirty();
        Debug.Log($"[Undo] Restored subtree root={u.subtreeRoot.id} nodes={u.subtreeNodes.Count}");
    }

    void RemoveSubtree(TreeNode node, List<TreeNode> removed)
    {
        foreach (var child in node.children)
            RemoveSubtree(child, removed);

        // Clean up any dormant bud object sitting at this node's tip.
        if (budObjects.TryGetValue(node.id, out var budGo))
        {
            Destroy(budGo);
            budObjects.Remove(node.id);
        }

        // Clean up any wound object on this node.
        if (woundObjects.TryGetValue(node.id, out var woundGo))
        {
            Destroy(woundGo);
            woundObjects.Remove(node.id);
        }

        // Clean up any air layer wrap on this node.
        for (int i = airLayers.Count - 1; i >= 0; i--)
        {
            if (airLayers[i].node == node)
            {
                if (airLayers[i].wrapObject != null) Destroy(airLayers[i].wrapObject);
                airLayers.RemoveAt(i);
            }
        }

        node.isTrimmed = true;
        allNodes.Remove(node);
        removed.Add(node);
    }

    // Wiring

    /// <summary>
    /// Auto-wires all unrimmed root nodes when the player confirms Ishitsuki orientation.
    /// Wires hold the current root direction (no bending); locked from removal until set.
    /// </summary>
    public void SpawnTrainingWires()
    {
        string rootAreaInfo = rootAreaTransform != null ? rootAreaTransform.position.ToString() : "NULL";
        Debug.Log("[SpawnWires] frame=" + Time.frameCount
                  + " | rockCollider=" + (rockCollider != null)
                  + " meshBuilder=" + (meshBuilder != null)
                  + " rootAreaTransform=" + (rootAreaTransform != null)
                  + "\n  rootAreaTransform.position=" + rootAreaInfo
                  + "\n  plantingSurfacePoint.y BEFORE=" + plantingSurfacePoint.y.ToString("F3")
                  + "\n  transform.position=" + transform.position);

        // Mark Ishitsuki mode permanently — this flag never goes null.
        isIshitsukiMode = true;

        // Lock in current world Y as the new rest position so the lift system
        // considers the tree already grounded here — no lowering animation.
        initY       = transform.position.y;
        currentLift = 0f;

        // Set soil surface Y from the root area transform so Ishitsuki root chains
        // stop at the actual visible tray/soil surface.
        // rootAreaTransform.position.y IS the visible soil — the rock may be partially
        // buried below it, so we must NOT use min(areaY, rockBase).
        {
            float areaY    = rootAreaTransform != null ? rootAreaTransform.position.y : plantingSurfacePoint.y;
            float rockBase = rockCollider      != null ? rockCollider.bounds.min.y     : areaY;
            Debug.Log($"[SpawnWires] year={GameManager.year} soilY: areaY(rootArea)={areaY:F3} rockBase={rockBase:F3} → using areaY={areaY:F3}");
            plantingSurfacePoint = new Vector3(plantingSurfacePoint.x, areaY, plantingSurfacePoint.z);
        }

        // Share rock collider with the mesh builder for gripping visuals.
        if (meshBuilder != null) meshBuilder.rockCollider = rockCollider;

        // ── Diagnostic snapshot ───────────────────────────────────────────────
        Vector3 treeWorldPos  = transform.position;
        Vector3 rockWorldPos  = rockCollider != null ? rockCollider.transform.position : Vector3.zero;
        Bounds  rockBounds    = rockCollider != null ? rockCollider.bounds : new Bounds();
        Vector3 rootAreaPos   = rootAreaTransform != null ? rootAreaTransform.position : Vector3.zero;
        Debug.Log($"[Ishitsuki] year={GameManager.year} WORLD POSITIONS:" +
                  $"\n  tree.position      = {treeWorldPos}" +
                  $"\n  rock.position      = {rockWorldPos}" +
                  $"\n  rock.bounds.min    = {rockBounds.min}" +
                  $"\n  rock.bounds.max    = {rockBounds.max}" +
                  $"\n  rock.bounds.center = {rockBounds.center}" +
                  $"\n  rootArea.position  = {rootAreaPos}" +
                  $"\n  soilY (after fix)  = {plantingSurfacePoint.y:F3}" +
                  $"\n  rockTopY           = {rockBounds.max.y:F3}" +
                  $"\n  rockBottomY        = {rockBounds.min.y:F3}" +
                  $"\n  rockHeightAboveSoil= {rockBounds.max.y - plantingSurfacePoint.y:F3}" +
                  $"\n  rockScale          = {(rockCollider != null ? rockCollider.transform.lossyScale.ToString() : "N/A")}");

        // Log trunk root starting positions before draping.
        if (root != null)
        {
            int ri = 0;
            foreach (var child in root.children)
            {
                if (!child.isRoot) continue;
                Vector3 wPos = transform.TransformPoint(child.worldPosition);
                Vector3 wTip = transform.TransformPoint(child.tipPosition);
                Debug.Log($"[Ishitsuki] year={GameManager.year} TrunkRoot[{ri}] depth={child.depth}" +
                          $" worldPos={wPos} tipWorld={wTip}" +
                          $" tipY={wTip.y:F3} soilY={plantingSurfacePoint.y:F3}" +
                          $" distAboveSoil={wTip.y - plantingSurfacePoint.y:F3}");
                ri++;
            }
        }
        // ── Game-view debug markers (GL lines — visible in Game View without Gizmos) ──
        {
            _soilDbgSoilY   = plantingSurfacePoint.y;
            _soilDbgRockTop = rockCollider != null ? rockCollider.bounds.max.y : _soilDbgSoilY + 2f;
            _soilDbgRockBot = rockCollider != null ? rockCollider.bounds.min.y : _soilDbgSoilY;
            _soilDbgCenter  = rockCollider != null ? rockCollider.bounds.center : transform.position;
            _soilDbgR       = rockCollider != null ? rockCollider.bounds.extents.magnitude * 1.1f : 1.5f;
            // _soilDbgActive  = true;
            _soilDbgEndTime = Time.realtimeSinceStartup + 60f;
        }
        // ─────────────────────────────────────────────────────────────────────

        // Clear existing trunk-root chains so PreGrowRootsToSoil builds fresh cables
        // from the correct trunk-tip positions instead of from the draped chain tips
        // (which can end up above the trunk after DrapeRootsOverRock adds an upward bias).
        // Use RemoveSubtree so orphaned nodes are also purged from allNodes — without
        // this, the old chain nodes stay in allNodes with isGrowing=true and show up
        // as ghost roots growing in mid-air above the rock.
        if (root != null)
        {
            foreach (var child in root.children)
            {
                if (!child.isRoot) continue;
                var toRemove = new List<TreeNode>(child.children);
                foreach (var oldChild in toRemove)
                    RemoveSubtree(oldChild, new List<TreeNode>());
                child.children.Clear();
            }
        }

        // Pre-grow root cables from the trunk base all the way to the soil.
        // In real Ishitsuki the roots are already established before rock placement —
        // what takes years is new thin roots filling in, not the original cables reaching soil.
        PreGrowRootsToSoil();

        meshBuilder.SetDirty();
        Debug.Log($"[Ishitsuki] year={GameManager.year} SpawnTrainingWires — initY={initY:F3} soilY={plantingSurfacePoint.y:F3}");
    }

    /// <summary>
    /// Walks all root nodes parent-first and snaps each one onto the rock surface,
    /// bending its growDirection to follow the surface tangent downward.
    /// Called once when the player confirms orientation.
    /// </summary>
    void DrapeRootsOverRock()
    {
        if (rockCollider == null || root == null) return;

        float snapRadius = rockCollider.bounds.extents.magnitude * 1.5f;
        int   snapped    = 0;

        var queue = new Queue<TreeNode>();
        foreach (var child in root.children)
            if (child.isRoot) queue.Enqueue(child);

        while (queue.Count > 0)
        {
            TreeNode node = queue.Dequeue();

            Vector3 worldPos = transform.TransformPoint(node.worldPosition);

            // Nodes inside the rock bounds can't use Physics.ClosestPoint reliably.
            // Shoot a ray from the trunk base in this root's own radial direction so
            // each root projects to a different surface point and they fan out naturally.
            Vector3 closestPt;
            bool    insideBounds = rockCollider.bounds.Contains(worldPos);
            if (insideBounds)
            {
                // Radial direction: horizontal XZ offset of this node from the trunk base.
                Vector3 localRadial = node.worldPosition - root.worldPosition;
                localRadial.y = 0f;
                if (localRadial.sqrMagnitude < 0.001f)
                    localRadial = new Vector3(node.growDirection.x, 0f, node.growDirection.z);
                Vector3 worldRadial   = transform.TransformDirection(localRadial).normalized;
                if (worldRadial.sqrMagnitude < 0.001f) worldRadial = Vector3.forward;
                Vector3 trunkWorldPos = transform.TransformPoint(root.worldPosition);
                if (rockCollider.Raycast(new Ray(trunkWorldPos, worldRadial),
                        out RaycastHit bHit, 10f))
                    closestPt = bHit.point;
                else
                    closestPt = Physics.ClosestPoint(worldPos, rockCollider,
                        rockCollider.transform.position, rockCollider.transform.rotation);
            }
            else
            {
                closestPt = Physics.ClosestPoint(worldPos, rockCollider,
                    rockCollider.transform.position, rockCollider.transform.rotation);
            }

            float dist = Vector3.Distance(worldPos, closestPt);

            if (insideBounds || dist < snapRadius)
            {
                // Surface normal via raycast from slightly outside.
                Vector3 outward = closestPt - rockCollider.bounds.center;
                if (outward.sqrMagnitude < 0.001f) outward = Vector3.up;
                outward.Normalize();
                Vector3 surfaceNormal = outward;
                if (rockCollider.Raycast(new Ray(closestPt + outward * 0.5f, -outward),
                        out RaycastHit hit, 1f))
                    surfaceNormal = hit.normal;

                // Move node base to surface with small clearance.
                node.worldPosition = transform.InverseTransformPoint(
                    closestPt + surfaceNormal * 0.025f);

                // Bend growDirection to surface tangent, biased downward.
                Vector3 worldDir = transform.TransformDirection(node.growDirection);
                Vector3 tangent  = Vector3.ProjectOnPlane(worldDir, surfaceNormal);
                if (tangent.sqrMagnitude < 0.001f)
                {
                    Vector3 radialOut = new Vector3(
                        worldPos.x - rockCollider.bounds.center.x, 0f,
                        worldPos.z - rockCollider.bounds.center.z).normalized;
                    tangent = Vector3.ProjectOnPlane(radialOut, surfaceNormal);
                }
                // Add gravity bias then re-project onto the surface plane so the
                // direction stays truly tangent — without this re-projection the
                // direction has an inward component and tipPosition dips into the rock,
                // forcing the next segment to angle sharply upward (the zigzag).
                // A small outward-normal offset keeps the tip just above the surface.
                tangent = Vector3.ProjectOnPlane(
                    (tangent.normalized + Vector3.down * 0.4f).normalized,
                    surfaceNormal);
                if (tangent.sqrMagnitude < 0.001f)
                    tangent = Vector3.ProjectOnPlane(Vector3.down, surfaceNormal);
                tangent = (tangent.normalized + surfaceNormal * 0.08f).normalized;
                node.growDirection = transform.InverseTransformDirection(tangent).normalized;
                snapped++;
            }

            foreach (var child in node.children)
                if (child.isRoot && !child.isTrimmed) queue.Enqueue(child);
        }

        Debug.Log($"[Ishitsuki] DrapeRootsOverRock — snapped {snapped} root nodes");
    }

    /// <summary>
    /// For each trunk root snapped to the rock surface, pre-spawns a fully-grown chain
    /// of nodes tracing the rock face down to the soil level.
    ///
    /// Real Ishitsuki roots are established BEFORE placement — the tree was trained to
    /// the rock over years before the player places it. Only the new fill-in roots that
    /// grow afterward should take a long time. The initial cables reach soil immediately.
    /// </summary>
    void PreGrowRootsToSoil(bool animated = false)
    {
        if (root == null) return;
        if (rockCollider == null)
        {
            Debug.LogWarning($"[PreGrow] year={GameManager.year} rockCollider is NULL (isIshitsukiMode={isIshitsukiMode}) — cannot drape roots.");
            return;
        }

        float soilY = debugSoilYOverride ? debugSoilY : plantingSurfacePoint.y;
        if (debugSoilYOverride && debugSoilY <= -9998f)
        {
            debugSoilY = plantingSurfacePoint.y;
            soilY      = debugSoilY;
        }

        float segLen     = rootSegmentLength * 0.5f;
        int   grown      = 0;
        float rockSearchR = rockCollider.bounds.extents.magnitude * 2.5f;

        var trunkRoots = new List<TreeNode>();
        foreach (var child in root.children)
            if (child.isRoot) trunkRoots.Add(child);

        Debug.Log($"[PreGrow] year={GameManager.year} soilY={soilY:F3} segLen={segLen:F3} trunkRoots={trunkRoots.Count}");

        // Prevent multiple strands from converging on the same rock-surface point.
        // Each strand claims its equatorial edge XZ; later strands rotate their scan
        // direction until they land at least minEdgeSep away from all claimed points.
        var  claimedEdges = new List<Vector3>();
        float minEdgeSep  = Mathf.Max(segLen * 1.5f, 0.04f);

        int strandIndex = 0;
        foreach (var startNode in trunkRoots)
        {
            TreeNode current     = startNode;
            int      strandGrown = 0;

            // ── Phase 1: fast-forward to the chain tip ────────────────────────────
            { int walkGuard = 0;
              while (current.children.Count > 0 && ++walkGuard < 5000)
                  current = current.children[0];
              if (walkGuard >= 5000) { Debug.LogError($"[PreGrow] Phase1 walk cycle detected on strand={strandIndex} — skipping"); strandIndex++; continue; }
            }

            Vector3 existingTip = transform.TransformPoint(current.tipPosition);
            if (existingTip.y <= soilY + 0.05f)
            {
                Debug.Log($"[PreGrow] year={GameManager.year} strand={strandIndex} tip already at soil tipY={existingTip.y:F3}");
                strandIndex++;
                continue;
            }

            // Remove only non-training-wire (accidentally air-grown) children.
            // Preserve existing training-wire cable segments so animated growth
            // continues from the real chain tip rather than restarting each spring.
            var nonWireChildren = startNode.children.FindAll(c => !c.isTrainingWire);
            foreach (var c in nonWireChildren)
                RemoveSubtree(c, new List<TreeNode>());
            startNode.children.RemoveAll(c => !c.isTrainingWire);

            // Re-walk to the actual chain tip after cleanup.
            current = startNode;
            { int walkGuard = 0;
              while (current.children.Count > 0 && ++walkGuard < 5000)
                  current = current.children[0];
              if (walkGuard >= 5000) { Debug.LogError($"[PreGrow] Re-walk cycle detected on strand={strandIndex} — skipping"); strandIndex++; continue; }
            }

            // ── Find entry point on rock face this strand is aimed at ─────────────
            // Each trunk root points in a different XZ direction from the trunk.
            // Cast from outside the rock bounds inward along that direction so every
            // strand starts on its own face — not all piling onto the nearest one.
            Vector3 startTip  = transform.TransformPoint(startNode.tipPosition);
            Vector3 strandDir = transform.TransformDirection(startNode.growDirection).normalized;
            Vector3 strandXZ  = new Vector3(strandDir.x, 0f, strandDir.z);
            if (strandXZ.sqrMagnitude < 0.001f)
                strandXZ = new Vector3(strandDir.x, strandDir.z, 0f);
            if (strandXZ.sqrMagnitude < 0.001f)
                strandXZ = Vector3.right;
            strandXZ = strandXZ.normalized;

            Vector3 rockCenter  = rockCollider.bounds.center;
            float   rockTopY    = rockCollider.bounds.max.y;
            float   surfOffset  = rootTerminalRadius * 2f; // float nodes one full diameter above surface

            // ── Entry point: two-step — find XZ edge, then scan down from above it ─────
            // Step 1: horizontal ray at rock center Y (the widest cross-section) finds the
            //         outermost XZ position of the rock in this strand's direction.
            //         This almost always hits, unlike a ray at startTip.y which can miss
            //         near the rock top where the mesh tapers.
            float   entryY    = Mathf.Max(startTip.y, rockTopY) + 0.5f;
            float   entryDist = rockCollider.bounds.size.y + 1.5f;

            // Find the equatorial edge XZ for this strand, retrying with small angular
            // offsets if the hit point is too close to an already-claimed one.
            Vector3 edgeXZ  = Vector3.zero;
            bool    edgeOk  = false;
            for (int retry = 0; retry <= 8 && !edgeOk; retry++)
            {
                float   retryA   = retry * (Mathf.PI / 9f); // ~20° steps
                float   cos      = Mathf.Cos(retryA);
                float   sin      = Mathf.Sin(retryA);
                // Rotate strandXZ left/right alternately: 0, +20°, -20°, +40°, -40°…
                float   sign     = (retry % 2 == 0) ? 1f : -1f;
                float   a        = retryA * sign;
                float   scanX    = strandXZ.x * Mathf.Cos(a) - strandXZ.z * Mathf.Sin(a);
                float   scanZ    = strandXZ.x * Mathf.Sin(a) + strandXZ.z * Mathf.Cos(a);
                Vector3 scanDir  = new Vector3(scanX, 0f, scanZ).normalized;

                Vector3 horizOrig = rockCenter + scanDir * rockSearchR;
                horizOrig.y       = rockCenter.y;
                Vector3 candidate;
                if (rockCollider.Raycast(new Ray(horizOrig, -scanDir), out RaycastHit edgeHit, rockSearchR * 2f))
                {
                    candidate = edgeHit.point;
                }
                else
                {
                    float projExtent = Mathf.Abs(scanDir.x) * rockCollider.bounds.extents.x
                                     + Mathf.Abs(scanDir.z) * rockCollider.bounds.extents.z;
                    candidate = new Vector3(rockCenter.x + scanDir.x * projExtent,
                                           rockCenter.y,
                                           rockCenter.z + scanDir.z * projExtent);
                }

                // Check XZ distance against all previously claimed edges.
                bool tooClose = false;
                foreach (var claimed in claimedEdges)
                {
                    float dx = candidate.x - claimed.x;
                    float dz = candidate.z - claimed.z;
                    if (dx * dx + dz * dz < minEdgeSep * minEdgeSep) { tooClose = true; break; }
                }

                if (!tooClose || retry == 8)
                {
                    edgeXZ  = candidate;
                    // If we rotated, also update strandXZ so step-loop raycasts stay consistent.
                    if (retry > 0) strandXZ = scanDir;
                    edgeOk  = true;
                }
            }
            claimedEdges.Add(edgeXZ);

            // Step 2: edgeXZ is the rock's outermost XZ in this strand's direction at
            //         its widest Y cross-section. Used as snap target when under-rock.
            //         baseWorld starts at the trunk-root tip — no entry scan landing, so
            //         there is no mesh gap between the trunk root and the first pre-grown node.
            // For animated continued growth, start from the actual chain tip so each
            // spring extends the cable one step further rather than restarting from startNode.
            Vector3 baseWorld      = transform.TransformPoint(current.tipPosition);
            bool    hasHitExterior = false;
            // Seed prevTangent from the last placed segment so the angle guard
            // produces smooth continuity across season boundaries.
            Vector3 prevTangent = (current != startNode)
                ? transform.TransformDirection(current.growDirection).normalized
                : strandDir;

            // ── Phase 2: step down the rock face to soil ──────────────────────────
            // Each step re-queries the rock exterior by shooting a horizontal ray from
            // outside the rock along the strand's fixed XZ direction at the new Y level.
            //
            // Before the first exterior hit: if the horizontal ray misses, check whether
            // we are still inside the rock with a downward ray. If so, snap XZ to edgeXZ
            // (the outer edge at the rock's widest section) so the chain exits the rock
            // instead of tunnelling through it.
            //
            // After the first exterior hit: a horizontal miss means we have descended past
            // the rock's lower surface — free-fall straight down to soil.
            for (int step = 0; step < 120; step++)
            {
                if (baseWorld.y <= soilY + 0.05f)
                {
                    Debug.Log($"[PreGrow] year={GameManager.year} strand={strandIndex} step={step} REACHED SOIL baseY={baseWorld.y:F3}");
                    break;
                }

                // Advance Y by one segment downward.
                float targetY = baseWorld.y - segLen;
                if (targetY < soilY) targetY = soilY;

                // Horizontal ray: from outside the rock inward along strandXZ at targetY.
                Vector3 scanOrig = rockCenter + strandXZ * rockSearchR;
                scanOrig.y       = targetY;
                bool hitRock     = rockCollider.Raycast(new Ray(scanOrig, -strandXZ), out RaycastHit hit, rockSearchR * 2f);

                Vector3 nodePos;
                Vector3 tangent;

                string stepMode;
                if (hitRock)
                {
                    // Exterior surface found — float one full root diameter above it.
                    nodePos         = hit.point + hit.normal * surfOffset;
                    tangent         = nodePos - baseWorld;
                    tangent         = tangent.sqrMagnitude > 0.001f ? tangent.normalized : Vector3.down;
                    stepMode        = "exterior";
                    hasHitExterior  = true;
                }
                else if (!hasHitExterior)
                {
                    // Haven't reached the exterior yet — check whether the trunk-root tip
                    // (or a prior node) is inside the rock by shooting down from above.
                    Vector3 checkOrig = new Vector3(baseWorld.x, rockTopY + 0.5f, baseWorld.z);
                    float   checkDist = rockTopY - soilY + 1f;
                    bool underRock    = rockCollider.Raycast(new Ray(checkOrig, Vector3.down), out RaycastHit checkHit, checkDist)
                                       && checkHit.point.y > targetY;
                    if (underRock)
                    {
                        // Snap XZ directly to the outer edge at this Y — jumps chain
                        // onto the rock exterior without drifting through the interior.
                        nodePos  = new Vector3(edgeXZ.x, targetY, edgeXZ.z);
                        tangent  = (nodePos - baseWorld).sqrMagnitude > 0.001f
                                   ? (nodePos - baseWorld).normalized
                                   : Vector3.down;
                        stepMode = "toEdge";
                    }
                    else
                    {
                        // We're above the rock and the ray just missed — free-fall.
                        nodePos  = new Vector3(baseWorld.x, targetY, baseWorld.z);
                        tangent  = Vector3.down;
                        stepMode = "freeFall";
                    }
                }
                else
                {
                    // Horizontal ray missed after tracking the exterior — the rock's side
                    // face has tapered away.  Before free-falling, try a downward ray from
                    // the outer edge (edgeXZ) to find the rock's lower curved surface.
                    // This lets the chain follow the lower hemisphere of a round rock
                    // instead of hanging in space at the equatorial XZ.
                    Vector3 downOrig = new Vector3(edgeXZ.x, baseWorld.y + 0.1f, edgeXZ.z);
                    float   downDist = baseWorld.y - soilY + 0.5f;
                    if (rockCollider.Raycast(new Ray(downOrig, Vector3.down), out RaycastHit lowerHit, downDist)
                        && lowerHit.point.y < baseWorld.y)
                    {
                        // Lower rock face found — float above it.
                        nodePos   = lowerHit.point + lowerHit.normal * surfOffset;
                        nodePos.y = Mathf.Clamp(nodePos.y, soilY, baseWorld.y - 0.001f);
                        tangent   = (nodePos - baseWorld).sqrMagnitude > 0.001f
                                    ? (nodePos - baseWorld).normalized
                                    : Vector3.down;
                        stepMode  = "lowerFace";
                    }
                    else
                    {
                        // Nothing found — rock is fully below us, drop straight to soil.
                        nodePos  = new Vector3(baseWorld.x, targetY, baseWorld.z);
                        tangent  = Vector3.down;
                        stepMode = "freeFall";
                    }
                }

                // ── Sharp-angle guard ────────────────────────────────────────────────
                // If the new segment would bend back toward the trunk at an angle sharper
                // than minCableAngleDeg, override to freeFall (drop straight down).
                // This removes the visible U-turn kinks on the upper rock face.
                if (stepMode != "freeFall")
                {
                    float bendAngle = Vector3.Angle(prevTangent, tangent);
                    if (bendAngle > (180f - minCableAngleDeg))
                    {
                        nodePos  = new Vector3(baseWorld.x, targetY, baseWorld.z);
                        tangent  = Vector3.down;
                        stepMode = "freeFall(angleGuard)";
                    }
                }
                prevTangent = tangent;

                if (step == 0 || step % 5 == 0)
                    Debug.Log($"[PreGrow] year={GameManager.year} s={strandIndex} step={step}" +
                              $" targetY={targetY:F3} nodeY={nodePos.y:F3} mode={stepMode}");

                Vector3 localPos = transform.InverseTransformPoint(baseWorld);
                Vector3 localDir = transform.InverseTransformDirection(tangent).normalized;

                var newNode              = CreateNode(localPos, localDir, rootTerminalRadius, segLen, current);
                newNode.isRoot           = true;
                newNode.isTrainingWire   = true;   // exempt from rootVisibilityDepth cull in TreeMeshBuilder
                newNode.length           = animated ? 0f    : segLen;
                newNode.isGrowing        = animated;        // true = grows visually; false = frozen at full length
                newNode.radius           = animated ? 0f    : rootTerminalRadius;

                current = newNode;
                grown++;
                strandGrown++;

                // Advance to the new exterior surface point — NOT tangent * segLen.
                baseWorld = nodePos;

                // Animated mode: one segment per season. The next spring call picks up from here.
                if (animated) break;
            }

            // Prevent startNode from reaching targetLength in the Update growth loop and
            // firing SpawnChildren — which would append a second air-growing continuation
            // root alongside the pre-grown chain. Mark it fully grown but frozen.
            // Also mark isTrainingWire so the age loop doesn't skip it (age=0 = stays white forever).
            startNode.isTrainingWire = true;
            if (startNode.isGrowing)
            {
                startNode.length    = startNode.targetLength;
                startNode.isGrowing = false;
                startNode.radius    = rootTerminalRadius;
                startNode.minRadius = rootTerminalRadius;
            }

            Vector3 finalTip = transform.TransformPoint(current.tipPosition);
            Debug.Log($"[PreGrow] year={GameManager.year} strand={strandIndex} DONE grew={strandGrown} finalTipY={finalTip.y:F3} soilY={soilY:F3} aboveSoil={finalTip.y - soilY:F3}");
            strandIndex++;
        }

        // After pre-growing, rebuild radii so the new nodes feed into the pipe model.
        if (grown > 0)
            RecalculateRadii(root);

        Debug.Log($"[Ishitsuki] year={GameManager.year} PreGrowRootsToSoil — spawned {grown} pre-grown nodes total");
    }

    /// <summary>
    /// Attaches a wire to a node.
    /// </summary>
    public void WireNode(TreeNode node, Vector3 targetDirectionLocal)
    {
        if (node.hasWire && node.wireSetProgress > 0f)
        {
            float damage = Mathf.Lerp(0.05f, 0.25f, node.wireSetProgress);
            ApplyDamage(node, DamageType.WireBend, damage);
        }

        node.wireOriginalDirection = node.growDirection;
        node.hasWire               = true;
        node.wireTargetDirection   = targetDirectionLocal.normalized;
        node.wireSetProgress       = 0f;
        node.wireDamageProgress    = 0f;
        node.wireAgeDays           = 0f;
    }

    // ── Mesh surface helpers ──────────────────────────────────────────────────

    struct RockSurfaceHit { public Vector3 point, normal; public float dist; }

    /// <summary>
    /// Finds the closest point on any triangle of the mesh to <paramref name="worldPos"/>.
    /// Works for any shape — convex, concave, overhang — from any position.
    /// Uses interpolated per-vertex normals (barycentric) to avoid zero-normal on shared vertices.
    /// </summary>
    static RockSurfaceHit ClosestPointOnMesh(
        Vector3   worldPos,
        Vector3[] verts,
        int[]     tris,
        Vector3[] normals,   // mesh.normals — per-vertex, pre-computed by Unity
        Transform xform)
    {
        Vector3 local   = xform.InverseTransformPoint(worldPos);
        float   minSq   = float.MaxValue;
        Vector3 bestPt  = local;
        int     bestI   = 0;

        for (int i = 0; i < tris.Length; i += 3)
        {
            Vector3 a = verts[tris[i]];
            Vector3 b = verts[tris[i + 1]];
            Vector3 c = verts[tris[i + 2]];

            Vector3 cp = ClosestPointOnTriangle(local, a, b, c);
            float   sq = (cp - local).sqrMagnitude;
            if (sq >= minSq) continue;

            minSq  = sq;
            bestPt = cp;
            bestI  = i;
        }

        // Interpolate surface normal at bestPt using barycentric coords + per-vertex normals.
        // This never produces zero even when bestPt is exactly on a shared vertex,
        // because per-vertex normals are averaged over all adjacent faces by Unity.
        Vector3 bestN = Vector3.up;
        if (normals != null && normals.Length > 0)
        {
            int     iA = tris[bestI], iB = tris[bestI + 1], iC = tris[bestI + 2];
            Vector3 a  = verts[iA],   b  = verts[iB],       c  = verts[iC];

            // Compute barycentric coords of bestPt on triangle (a, b, c).
            // bestPt = a*(1-u-v) + b*v + c*u  →  wA=1-u-v, wB=v, wC=u
            Vector3 v0 = c - a, v1 = b - a, v2 = bestPt - a;
            float d00 = Vector3.Dot(v0, v0), d01 = Vector3.Dot(v0, v1), d02 = Vector3.Dot(v0, v2);
            float d11 = Vector3.Dot(v1, v1), d12 = Vector3.Dot(v1, v2);
            float denom = d00 * d11 - d01 * d01;

            Vector3 interpolated;
            if (Mathf.Abs(denom) < 1e-10f)
            {
                // Degenerate (zero-area) triangle — fall back to cross product
                interpolated = Vector3.Cross(b - a, c - a);
            }
            else
            {
                float invD = 1f / denom;
                float wC   = (d11 * d02 - d01 * d12) * invD;   // weight for vertex C
                float wB   = (d00 * d12 - d01 * d02) * invD;   // weight for vertex B
                float wA   = 1f - wB - wC;                     // weight for vertex A
                // Clamp to [0,1] to handle floating-point noise at triangle edges
                wA = Mathf.Clamp01(wA); wB = Mathf.Clamp01(wB); wC = Mathf.Clamp01(wC);
                float sum = wA + wB + wC;
                if (sum > 1e-6f) { wA /= sum; wB /= sum; wC /= sum; }
                interpolated = normals[iA] * wA + normals[iB] * wB + normals[iC] * wC;
            }

            if (interpolated.sqrMagnitude > 1e-6f)
                bestN = interpolated.normalized;
            // else bestN stays Vector3.up (safe fallback)
        }

        return new RockSurfaceHit
        {
            point  = xform.TransformPoint(bestPt),
            normal = xform.TransformDirection(bestN),
            dist   = Mathf.Sqrt(minSq)
        };
    }

    /// <summary>
    /// Returns the closest point on triangle (a,b,c) to point p.
    /// All in the same local space. Standard Ericson/Christer method.
    /// </summary>
    static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a, ac = c - a, ap = p - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f) return a;

        Vector3 bp = p - b;
        float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3) return b;

        Vector3 cp = p - c;
        float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6) return c;

        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            return a + ab * (d1 / (d1 - d3));

        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            return a + ac * (d2 / (d2 - d6));

        float va = d3 * d6 - d5 * d4;
        if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
            return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));

        float denom = 1f / (va + vb + vc);
        return a + ab * (vb * denom) + ac * (vc * denom);
    }

    /// <summary>
    /// Collects the contiguous wire run that includes <paramref name="start"/>.
    /// Walks up to the highest wired ancestor, then follows the most direction-aligned
    /// wired child at each fork (leaving other branches' wires in place).
    /// </summary>
    public List<TreeNode> CollectWireRun(TreeNode start)
    {
        // Walk up to the top of the run
        TreeNode top = start;
        while (top.parent != null && top.parent.hasWire)
            top = top.parent;

        // Walk down following the best-aligned wired child at each fork
        var run = new List<TreeNode>();
        TreeNode cur = top;
        while (cur != null && cur.hasWire)
        {
            run.Add(cur);
            cur = WireRunChild(cur);
        }
        return run;
    }

    TreeNode WireRunChild(TreeNode node)
    {
        TreeNode best    = null;
        float    bestDot = -2f;
        foreach (var child in node.children)
        {
            if (!child.hasWire) continue;
            float dot = Vector3.Dot(node.growDirection, child.growDirection);
            if (dot > bestDot) { bestDot = dot; best = child; }
        }
        return best;
    }

    /// <summary>
    /// Smart unwire: if every node in the wire run is fully set, removes them all.
    /// Otherwise falls back to unwiring only the clicked node.
    /// </summary>
    public void UnwireRun(TreeNode node)
    {
        var run = CollectWireRun(node);

        bool allSet = true;
        foreach (var n in run)
            if (n.wireSetProgress < 1f) { allSet = false; break; }

        if (allSet && run.Count > 1)
        {
            foreach (var n in run)
                UnwireNode(n);
            Debug.Log($"[Wire] UnwireRun removed count={run.Count}");
        }
        else
        {
            UnwireNode(node);
        }
    }

    /// <summary>
    /// Removes the wire. If not fully set, the branch springs back partially.
    /// </summary>
    public void UnwireNode(TreeNode node)
    {
        if (node.wireSetProgress < 1f)
        {
            Vector3 prevDir    = node.growDirection;
            node.growDirection = Vector3.Slerp(
                node.wireOriginalDirection,
                node.wireTargetDirection,
                node.wireSetProgress).normalized;

            Quaternion springBackRot = Quaternion.FromToRotation(prevDir, node.growDirection);
            RotateAndPropagateDescendants(node, springBackRot, null);
        }

        node.hasWire            = false;
        node.wireSetProgress    = 0f;
        node.wireDamageProgress = 0f;
        node.wireAgeDays        = 0f;
        meshBuilder.SetDirty();
    }

    /// <summary>
    /// Rotates every descendant's growDirection by rot and propagates their worldPositions.
    /// </summary>
    public void RotateAndPropagateDescendants(
        TreeNode node, Quaternion rot,
        System.Collections.Generic.Dictionary<TreeNode, Vector3> originalDirs)
    {
        foreach (var child in node.children)
        {
            if (originalDirs != null && originalDirs.TryGetValue(child, out var origDir))
                child.growDirection = (rot * origDir).normalized;
            else
                child.growDirection = (rot * child.growDirection).normalized;

            child.worldPosition = node.tipPosition;
            RotateAndPropagateDescendants(child, rot, originalDirs);
        }
    }

    /// <summary>
    /// Reduces a node's health by amount, clamped to 0.
    /// </summary>
    public void ApplyDamage(TreeNode node, DamageType type, float amount)
    {
        node.health = Mathf.Max(0f, node.health - amount);
    }

    // ── Graft System ──────────────────────────────────────────────────────────

    [Header("Grafting")]
    [Tooltip("Maximum world-unit distance between source tip and target node for a graft attempt to be valid.")]
    [SerializeField] float graftMaxDistance = 3f;
    [Tooltip("Number of growing seasons until a graft fuses. Default 2.")]
    [SerializeField] int graftSeasonsToFuse = 2;

    // ── Sibling Branch Fusion ─────────────────────────────────────────────────

    [Header("Sibling Fusion")]
    [Tooltip("Seasons for sibling tips touching to fully fuse into one unit. Default 4.")]
    [SerializeField] int fusionSeasonsToFuse = 4;
    [Tooltip("Detection threshold: fuse when tip-to-tip distance < (rA+rB) × this multiplier. Default 2.5.")]
    [SerializeField] float fusionTipProximityMult = 2.5f;

    /// <summary>All active and completed sibling fusion bonds on this tree.</summary>
    public readonly List<FusionBond> fusionBonds = new List<FusionBond>();

    public class FusionBond
    {
        public int  nodeIdA;
        public int  nodeIdB;
        public int  seasonsElapsed;
        public bool isComplete;
        public int  bridgeId;    // id of the bridge node created on success (-1 until then)

        public FusionBond(int a, int b) { nodeIdA = a; nodeIdB = b; bridgeId = -1; }
    }

    /// <summary>All active graft attempts on this tree.</summary>
    public readonly List<GraftAttempt> graftAttempts = new List<GraftAttempt>();

    /// <summary>Exposed so TreeInteraction can read it for the progress line colour.</summary>
    public int GraftSeasonsToFuse => graftSeasonsToFuse;

    /// <summary>Source node awaiting a second click to form a graft pair. Null when no graft is pending.</summary>
    public TreeNode pendingGraftSource;

    public class GraftAttempt
    {
        public int  sourceId;
        public int  targetId;
        public int  seasonsElapsed;
        public bool succeeded;
        public int  bridgeId;    // id of the bridge node created on success (-1 until then)

        public GraftAttempt(int src, int tgt) { sourceId = src; targetId = tgt; bridgeId = -1; }
    }

    /// <summary>
    /// Begin a graft attempt. Source must be a living non-root terminal.
    /// Target must be on a different ancestry chain, within graftMaxDistance.
    /// Returns null with a reason string on failure.
    /// </summary>
    public (bool ok, string reason) TryStartGraft(TreeNode source, TreeNode target)
    {
        if (source == null || target == null)
            return (false, "null node");
        if (source == target)
            return (false, "same node");
        if (source.isRoot || target.isRoot)
            return (false, "roots cannot be grafted");
        if (!source.isTerminal)
            return (false, "source must be a terminal tip");
        if (source.isTrimmed || target.isTrimmed || source.isDead || target.isDead)
            return (false, "node is trimmed or dead");
        if (source.isGraftSource)
            return (false, "source already has a pending graft");

        // Source and target must not share ancestry (no grafting within the same branch run)
        TreeNode n = target;
        while (n != null) { if (n == source) return (false, "target is ancestor of source"); n = n.parent; }
        n = source;
        while (n != null) { if (n == target) return (false, "source is ancestor of target"); n = n.parent; }

        // Distance check: tip of source to base of target
        float dist = Vector3.Distance(
            transform.TransformPoint(source.tipPosition),
            transform.TransformPoint(target.worldPosition));
        if (dist > graftMaxDistance)
            return (false, $"too far ({dist:F2}m > {graftMaxDistance:F2}m max)");

        source.isGraftSource = true;
        graftAttempts.Add(new GraftAttempt(source.id, target.id));
        pendingGraftSource   = null;
        Debug.Log($"[Graft] Started: source={source.id} → target={target.id} dist={dist:F2}");
        return (true, "");
    }

    /// <summary>Cancel the pending source selection (ESC / tool switch).</summary>
    public void CancelPendingGraft()
    {
        pendingGraftSource = null;
    }

    /// <summary>
    /// Advance all active grafts by one growing season.
    /// Called from StartNewGrowingSeason.
    /// </summary>
    void AdvanceGrafts()
    {
        for (int i = graftAttempts.Count - 1; i >= 0; i--)
        {
            var g = graftAttempts[i];
            if (g.succeeded) continue;

            // Look up live nodes
            TreeNode src = allNodes.Find(n => n.id == g.sourceId);
            TreeNode tgt = allNodes.Find(n => n.id == g.targetId);

            // Abort if either node is gone / dead / no longer a terminal
            if (src == null || tgt == null || src.isTrimmed || tgt.isTrimmed || src.isDead || tgt.isDead)
            {
                if (src != null) src.isGraftSource = false;
                Debug.Log($"[Graft] Attempt {g.sourceId}→{g.targetId} aborted (node gone/dead)");
                graftAttempts.RemoveAt(i);
                continue;
            }

            g.seasonsElapsed++;

            // Bend source tip growDirection toward target each season (halfway per season)
            Vector3 srcTipWorld = transform.TransformPoint(src.tipPosition);
            Vector3 tgtWorld    = transform.TransformPoint(tgt.worldPosition);
            Vector3 toTarget    = (tgtWorld - srcTipWorld).normalized;
            float   bendFactor  = 0.35f * g.seasonsElapsed;   // 35% per season
            src.growDirection   = Vector3.Slerp(src.growDirection, toTarget, Mathf.Clamp01(bendFactor)).normalized;
            meshBuilder.SetDirty();

            if (g.seasonsElapsed < graftSeasonsToFuse) continue;

            // ── Fuse ────────────────────────────────────────────────────────
            float bridgeLen = Vector3.Distance(
                transform.TransformPoint(src.tipPosition),
                transform.TransformPoint(tgt.worldPosition));

            var bridge = new TreeNode(
                nextId++,
                src.depth + 1,
                src.tipPosition,
                toTarget.magnitude > 0.001f ? transform.InverseTransformDirection(toTarget) : src.growDirection,
                src.radius * 0.6f,
                bridgeLen,
                src);
            bridge.isGraftBridge = true;
            bridge.length        = bridgeLen;   // already fully grown
            bridge.isGrowing     = false;
            bridge.age           = bridge.targetLength;

            src.children.Add(bridge);
            allNodes.Add(bridge);

            g.succeeded  = true;
            g.bridgeId   = bridge.id;
            src.isGraftSource = false;

            RecalculateRadii(root);
            meshBuilder.SetDirty();
            Debug.Log($"[Graft] Fused: source={src.id} → target={tgt.id} | bridge={bridge.id} len={bridgeLen:F2}");
        }
    }

    // ── Sibling Branch Fusion ─────────────────────────────────────────────────

    /// <summary>
    /// Scan all non-root terminal siblings each spring. When two tips from the same
    /// parent are close enough they are registered as a new FusionBond.
    /// Called from StartNewGrowingSeason after all new growth is spawned.
    /// </summary>
    void DetectNewFusions()
    {
        // Build a per-parent list of living terminal branches
        var byParent = new Dictionary<int, List<TreeNode>>();
        foreach (var n in allNodes)
        {
            if (n.isRoot || n.isTrimmed || n.isDead || !n.isTerminal) continue;
            if (n.parent == null) continue;

            if (!byParent.TryGetValue(n.parent.id, out var list))
            {
                list = new List<TreeNode>();
                byParent[n.parent.id] = list;
            }
            list.Add(n);
        }

        foreach (var kv in byParent)
        {
            var siblings = kv.Value;
            for (int i = 0; i < siblings.Count - 1; i++)
            {
                for (int j = i + 1; j < siblings.Count; j++)
                {
                    var a = siblings[i];
                    var b = siblings[j];

                    // Skip if either node is already in any bond
                    bool alreadyBonded = fusionBonds.Exists(fb =>
                        fb.nodeIdA == a.id || fb.nodeIdB == a.id ||
                        fb.nodeIdA == b.id || fb.nodeIdB == b.id);
                    if (alreadyBonded) continue;

                    float threshold = (a.radius + b.radius) * fusionTipProximityMult;
                    float dist = Vector3.Distance(
                        transform.TransformPoint(a.tipPosition),
                        transform.TransformPoint(b.tipPosition));
                    if (dist <= threshold)
                    {
                        fusionBonds.Add(new FusionBond(a.id, b.id));
                        Debug.Log($"[Fusion] New bond: {a.id}↔{b.id} dist={dist:F3} threshold={threshold:F3}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Advance all pending fusion bonds by one season. When seasonsElapsed reaches
    /// fusionSeasonsToFuse, a bridge node is created between the two tips.
    /// Called from StartNewGrowingSeason after DetectNewFusions.
    /// </summary>
    void AdvanceFusions()
    {
        for (int i = fusionBonds.Count - 1; i >= 0; i--)
        {
            var fb = fusionBonds[i];
            if (fb.isComplete) continue;

            TreeNode a = allNodes.Find(n => n.id == fb.nodeIdA);
            TreeNode b = allNodes.Find(n => n.id == fb.nodeIdB);

            if (a == null || b == null || a.isTrimmed || b.isTrimmed || a.isDead || b.isDead)
            {
                Debug.Log($"[Fusion] Bond {fb.nodeIdA}↔{fb.nodeIdB} aborted (node gone/dead/trimmed)");
                fusionBonds.RemoveAt(i);
                continue;
            }

            fb.seasonsElapsed++;

            if (fb.seasonsElapsed < fusionSeasonsToFuse) continue;

            // ── Create bridge node ───────────────────────────────────────────
            Vector3 tipAWorld = transform.TransformPoint(a.tipPosition);
            Vector3 tipBWorld = transform.TransformPoint(b.tipPosition);
            float   bridgeLen = Vector3.Distance(tipAWorld, tipBWorld);

            if (bridgeLen < 0.001f)
            {
                fusionBonds.RemoveAt(i);
                continue;
            }

            Vector3 bridgeDirLocal = transform.InverseTransformDirection(
                (tipBWorld - tipAWorld).normalized);

            var bridge = new TreeNode(
                nextId++,
                a.depth + 1,
                a.tipPosition,
                bridgeDirLocal,
                Mathf.Min(a.radius, b.radius) * 0.65f,
                bridgeLen,
                a);
            bridge.isGraftBridge = true;
            bridge.length        = bridgeLen;
            bridge.isGrowing     = false;
            bridge.age           = bridge.targetLength;

            a.children.Add(bridge);
            allNodes.Add(bridge);

            fb.isComplete = true;
            fb.bridgeId   = bridge.id;

            RecalculateRadii(root);
            meshBuilder.SetDirty();
            Debug.Log($"[Fusion] Complete: {a.id}↔{b.id} bridge={bridge.id} len={bridgeLen:F3}");
        }
    }

    // ── Fungal System ─────────────────────────────────────────────────────────

    /// <summary>
    /// Per-season fungal infection update. Called from StartNewGrowingSeason.
    ///
    /// At-risk conditions for a node:
    ///   - Open wound (hasWound &amp;&amp; !pasteApplied)
    ///   - Over-watered roots (isRoot &amp;&amp; soilMoisture > 0.9)
    ///   - Low health (&lt;0.5)
    ///
    /// If at-risk: fungalLoad increases by fungalLoadIncrease.
    /// Spread: each infected node has fungalSpreadChance to nudge fungalLoad on each
    ///   adjacent (parent / child) node by half the increase.
    /// Damage: applied if fungalLoad > 0.4, scaled by excess.
    /// Recovery: fungalLoad drops by fungalRecoveryRate per season when no longer at-risk.
    /// </summary>
    void UpdateFungalInfection()
    {
        // Collect spread deltas separately so we don't double-count in one pass
        var spreadDelta = new Dictionary<int, float>(allNodes.Count);
        foreach (var n in allNodes) spreadDelta[n.id] = 0f;

        int infectedCount = 0;
        foreach (var node in allNodes)
        {
            if (node.isTrimmed) continue;

            bool atRisk = (node.hasWound && !node.pasteApplied)
                       || (node.isRoot && soilMoisture > 0.9f)
                       || (node.health < 0.5f);

            if (atRisk)
            {
                node.fungalLoad = Mathf.Min(1f, node.fungalLoad + fungalLoadIncrease);
            }
            else if (node.fungalLoad > 0f)
            {
                node.fungalLoad = Mathf.Max(0f, node.fungalLoad - fungalRecoveryRate);
            }

            // Spread to neighbours
            if (node.fungalLoad > 0.05f)
            {
                float nudge = node.fungalLoad * 0.5f * fungalLoadIncrease;
                if (node.parent != null && Random.value < fungalSpreadChance)
                    spreadDelta[node.parent.id] += nudge;
                foreach (var child in node.children)
                    if (Random.value < fungalSpreadChance)
                        spreadDelta[child.id] += nudge;

                // Damage: excess above 0.4
                float excess = node.fungalLoad - 0.4f;
                if (excess > 0f)
                {
                    ApplyDamage(node, DamageType.FungalInfection, fungalDamagePerLoad * excess);
                    infectedCount++;
                }
            }
        }

        // Apply spread
        foreach (var node in allNodes)
        {
            float d = spreadDelta[node.id];
            if (d > 0f) node.fungalLoad = Mathf.Min(1f, node.fungalLoad + d);
        }

        if (infectedCount > 0)
            Debug.Log($"[Fungal] {infectedCount} node(s) took fungal damage | year={GameManager.year}");
    }

    /// <summary>
    /// Per-season mycorrhizal network update. Called from StartNewGrowingSeason after
    /// UpdateFungalInfection so fungalLoad is already updated.
    ///
    /// Root nodes that stay healthy (health>0.75, fungalLoad&lt;0.1) for
    /// mycorrhizalHealthySeasonsRequired seasons gain the isMycorrhizal flag.
    /// Nodes that go over threshold lose it.
    /// </summary>
    void UpdateMycorrhizal()
    {
        int gained = 0, lost = 0;
        foreach (var node in allNodes)
        {
            if (!node.isRoot || node.isTrimmed) continue;

            bool healthy = node.health > 0.75f && node.fungalLoad < 0.1f;
            if (healthy)
            {
                node.healthySeasonsCount++;
                if (!node.isMycorrhizal && node.healthySeasonsCount >= mycorrhizalHealthySeasonsRequired)
                {
                    node.isMycorrhizal = true;
                    gained++;
                }
            }
            else
            {
                node.healthySeasonsCount = 0;
                if (node.isMycorrhizal) { node.isMycorrhizal = false; lost++; }
            }
        }

        if (gained > 0 || lost > 0)
            Debug.Log($"[Fungal] Mycorrhizal: +{gained} gained, -{lost} lost | year={GameManager.year}");
    }

    // Wound System

    void CreateWoundObject(TreeNode node)
    {
        // The wound is now rendered as part of the unified tree mesh (callus cap geometry
        // in TreeMeshBuilder) driven by node.hasWound / node.woundAge / node.pasteApplied
        // via vertex color channels G and B.  We keep a lightweight empty anchor here so
        // all existing woundObjects book-keeping (heal loop, undo, etc.) stays unchanged.

        if (woundObjects.TryGetValue(node.id, out var existing))
        {
            if (existing != null) Destroy(existing);
            woundObjects.Remove(node.id);
        }

        var go = new GameObject($"_WoundAnchor_{node.id}");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = node.tipPosition;

        woundObjects[node.id] = go;
        // Mesh rebuild is already triggered by the trim that calls this.
        Debug.Log($"[Wound] Wound registered node={node.id} radius={node.woundRadius:F3}");
    }

    /// <summary>
    /// Low-poly half-torus (outer half only, theta ∈ [0, π]).
    /// The ring lies in the XZ plane; the tube protrudes along +Y.
    /// </summary>
    Mesh BuildHalfTorusMesh(float R, float r, int phiSteps, int thetaSteps)
    {
        var verts = new List<Vector3>();
        var tris  = new List<int>();
        var uvs   = new List<Vector2>();

        int cols = phiSteps + 1;

        for (int j = 0; j <= thetaSteps; j++)
        {
            float theta = Mathf.PI * j / thetaSteps;
            float ct = Mathf.Cos(theta), st = Mathf.Sin(theta);
            for (int i = 0; i <= phiSteps; i++)
            {
                float phi = Mathf.PI * 2f * i / phiSteps;
                float cp = Mathf.Cos(phi), sp = Mathf.Sin(phi);
                verts.Add(new Vector3((R + r * ct) * cp, r * st, (R + r * ct) * sp));
                uvs.Add(new Vector2((float)i / phiSteps, (float)j / thetaSteps));
            }
        }

        for (int j = 0; j < thetaSteps; j++)
        {
            for (int i = 0; i < phiSteps; i++)
            {
                int a = j * cols + i, b = j * cols + i + 1;
                int c = (j + 1) * cols + i, d = (j + 1) * cols + i + 1;
                tris.Add(a); tris.Add(c); tris.Add(b);
                tris.Add(b); tris.Add(c); tris.Add(d);
            }
        }

        var mesh = new Mesh { name = "HalfTorus" };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.SetUVs(0, uvs);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// Applies wound sealing paste to a node, stopping the seasonal health drain.
    /// </summary>
    public void ApplyPaste(TreeNode node)
    {
        if (!node.hasWound || node.pasteApplied) return;
        node.pasteApplied = true;

        // Paste is now visualised via vertex.b in the unified tree mesh.
        // Trigger a rebuild so the new vertex data takes effect immediately.
        meshBuilder.SetDirty();
        Debug.Log($"[Wound] Paste applied node={node.id}");
    }

    /// <summary>
    /// Recursively updates children's worldPosition when a parent node is moved.
    /// </summary>
    public void PropagatePosition(TreeNode node)
    {
        foreach (var child in node.children)
        {
            child.worldPosition = node.tipPosition;
            PropagatePosition(child);
        }
    }
}
