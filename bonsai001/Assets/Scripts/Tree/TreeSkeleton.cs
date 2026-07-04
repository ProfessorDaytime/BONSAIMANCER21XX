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
public partial class TreeSkeleton : MonoBehaviour
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

    [Tooltip("In-game days over which each spring's new sag rotation creeps in. " +
             "Spreads the droop through the season instead of snapping it on one frame.")]
    [SerializeField] float sagSpreadDays = 100f;

    [Header("Leaf Weight (Elastic Sag)")]
    [Tooltip("Mass of one leaf in the load model. The summer canopy adds load; autumn removes it.")]
    [SerializeField] float leafMassEach = 0.01f;

    [Tooltip("Maximum elastic droop (degrees) leaf weight can add to a branch. Unlike permanent " +
             "sag this REVERSES — branches spring back up as the leaves fall.")]
    [SerializeField] float elasticSagMaxDeg = 6f;

    [Tooltip("Leaf-load/strength ratio at which elastic droop reaches its maximum.")]
    [SerializeField] float elasticFullLoadRatio = 2f;

    [Tooltip("Fastest elastic droop change per in-game day (degrees) — eases both directions.")]
    [SerializeField] float elasticSagPerDayDeg = 1.5f;

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

    /// <summary>Hard branch-node budget (read by AutoStyler's crowding score).</summary>
    public int MaxBranchNodesPublic => maxBranchNodes;

    [Tooltip("Pipe-model exponent: parent = (Σ child^e)^(1/e). Leonardo's classic e=2\n" +
             "over-thickens at bonsai twig counts (the 'fist of stubs' look); field data\n" +
             "puts real trees nearer 2.5. Higher = slimmer trunks/branches everywhere.\n" +
             "Existing wood never shrinks (minRadius ratchet) — new growth grows slimmer.")]
    [SerializeField] [Range(1.5f, 3.5f)] float pipeModelExponent = 2.5f;

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

    [Tooltip("Max styler-directed slot-fill buds (epicormic buds forced on old trunk wood by the\n" +
             "AutoStyler's February stimulation) that break per spring. These bypass the branch cap\n" +
             "and the vigor roll — otherwise a mature tree at maxBranchNodes can never grow the\n" +
             "scaffold branches its style still needs. 0 disables forcing (probability path only).")]
    [SerializeField] [Range(0, 8)] int maxForcedBudsPerSpring = 3;

    [Tooltip("Minimum treeEnergy for a forced slot-fill bud to break — the tree must have the\n" +
             "reserves to push a new structural shoot from old wood.")]
    [SerializeField] [Range(0f, 1f)] float forcedBudMinEnergy = 0.5f;

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

    // True only for the SIM STEP in which auto-water fired (see SimStep). The drought
    // accumulator checks it: a watered step is not a dry step. (Historic: before F9's
    // fixed timestep, one hitch frame spanned ~12 in-game days and trees banked dry
    // days while being watered every frame — the 2026-07-02 drought-death family.)
    bool wateredThisFrame = false;

    // ── F9: fixed-timestep simulation ─────────────────────────────────────────
    [Tooltip("Fixed simulation step, in in-game days. ALL sim state (growth, moisture,\n" +
             "drought, wires, auto-care) advances in these quanta — never raw frame\n" +
             "time — so the tree is identical at any play speed and frame rate.\n" +
             "Smaller = finer threshold resolution, more CPU per sim day.")]
    [SerializeField] float simStepDays = 0.25f;

    [Tooltip("Max sim steps processed per rendered frame. On a hitch the sim briefly\n" +
             "lags the calendar and catches up over the next frames instead of taking\n" +
             "one giant step. 48 covers TIMESCALE 6000 at 30 fps.")]
    [SerializeField] int maxSimStepsPerFrame = 48;

    float simDayAccumulator;

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

    [Tooltip("Cut branches at an angle so water runs off the wound face rather than pooling. " +
             "Traditional bonsai practice — angled cuts also heal faster.")]
    [SerializeField] bool useBevelCut = true;

    [Tooltip("Angle of the cut face relative to the branch axis (degrees). 45 = traditional.")]
    [SerializeField] [Range(0f, 75f)] float cutAngleDeg = 45f;

    [Tooltip("Wound drain rate multiplier applied when useBevelCut is true. " +
             "0.7 = angled cuts drain 30% less health per season than flat cuts.")]
    [SerializeField] float bevelCutDrainMult = 0.7f;

    [Tooltip("Health drained from a wounded node per growing season. Paste stops this drain.")]
    [SerializeField] float woundDrainRate = 0.05f;

    [Tooltip("Growing seasons to fully callus over one unit of wound radius. " +
             "Larger wounds (thicker cut branches) take proportionally longer to heal. " +
             "E.g. radius=0.1 × 20 = 2 seasons; radius=0.5 × 20 = 10 seasons.")]
    [SerializeField] float seasonsToHealPerUnit = 20f;

    [Header("Wound Occlusion (visual healing)")]
    [Tooltip("Seasons for callus to roll over and occlude a cut, per unit of wound radius. Drives " +
             "the VISUAL heal (cut face closing + stub engulfed by the thickening trunk) — separate " +
             "from the health drain above, so balance is unchanged. Big cuts take far longer.")]
    [SerializeField] float woundOcclusionPerUnit = 120f;
    [Tooltip("Floor on occlusion seasons so even a tiny twig cut takes a season or two to seal.")]
    [SerializeField] float woundOcclusionBaseSeasons = 1f;
    [Tooltip("Cut paste divides occlusion time by this — faster, cleaner healing.")]
    [SerializeField] float woundPasteHealMult = 1.6f;

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

    // Drainage-hole root escape: cached once per growth pass so the per-node floor checks don't
    // re-GetComponent / re-walk the tree. _holeEscapePressure is RootPressureFactor() at pass start;
    // roots only escape through a hole once the tree is genuinely pot-bound (> 0.4).
    PotSoil _holePotSoil;
    float   _holeEscapePressure;

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

    [Tooltip("Lift multiplier while raking (RootRake state) so the soil-caked root ball — " +
             "which hangs below the trunk base — fully clears the pot rim.")]
    [SerializeField] float rakeLiftMult = 1.5f;

    [Tooltip("Lift/lower animation speed (units per second).")]
    [SerializeField] float rootLiftSpeed = 4f;

    [Tooltip("Distance from the planting surface at which roots begin to hug the surface.")]
    [SerializeField] float rootSurfaceSnapDist = 0.8f;

    [Tooltip("Max degrees a growing root segment may bend away from its parent segment per step. " +
             "Lower = smoother, more flowing roots (a 90° elbow becomes a multi-segment arc); " +
             "higher = sharper corners. Pure direction limit — never moves node positions. " +
             "180 disables smoothing.")]
    [SerializeField] float rootMaxBendPerSegmentDeg = 26f;

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

    [Tooltip("Corner-rounding passes applied to each pre-grown rock-root cable. Rounds the " +
             "out-then-down elbows where roots crest the rock and dive down the side, while " +
             "re-snapping to the rock face so they stay hugging it. 0 = off (sharp corners).")]
    [SerializeField] int rockCableSmoothIterations = 3;

    [Tooltip("Clear gap (world units) between a rock-root's outer surface and the rock. Each " +
             "cable node floats off the rock by its own radius PLUS this padding, so the root " +
             "tube sits on the outside instead of clipping through. Keep small for a snug look.")]
    [SerializeField] float rockRootSurfacePadding = 0.015f;

    [Tooltip("How many in-game days between [Tree5] snapshot log entries. Lower = more detail; higher = less spam.")]
    [SerializeField] int   snapshotLogIntervalDays = 30;

    [Tooltip("Enable high-frequency debug logs (per-spawn, per-node). Off by default to keep the console usable.")]
    [SerializeField] bool  verboseLog = false;

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

    /// <summary>
    /// Visual occlusion progress of a wound, 0 (fresh cut face) → 1 (callus fully closed over,
    /// stub engulfed). Drives the heal geometry in TreeMeshBuilder. Independent of the health
    /// drain. Bigger cuts (woundRadius) heal slower; vigorous nodes and cut paste heal faster —
    /// so a small cut on a strong young tree is absorbed quickly while a big cut lingers as a scar.
    /// </summary>
    public float WoundHealProgress(TreeNode node)
    {
        if (node == null || node.woundRadius <= 0.0001f) return 1f;
        float vigorF  = Mathf.Clamp(node.branchVigor, 0.5f, 2f);
        float pasteF  = node.pasteApplied ? Mathf.Max(1f, woundPasteHealMult) : 1f;
        float seasons = Mathf.Max(1f,
            (woundOcclusionBaseSeasons + node.woundRadius * woundOcclusionPerUnit) / (vigorF * pasteF));
        return Mathf.Clamp01(node.woundAge / seasons);
    }

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
            // Soil-surface Y, not the tree's Y — in Ishitsuki the tree sits up on the rock,
            // so weeds must use the tray soil level (plantingSurfacePoint) or they spawn above the rock.
            weedMgr.SetPotBounds(new Vector3(transform.position.x, plantingSurfacePoint.y, transform.position.z), weedSpawnRadius);
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
            potSoil.compaction         = data.soilCompaction;
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
        CareLog.Restore(data.careLog);
        startMonth             = data.startMonth;
        lastGrownYear          = data.lastGrownYear;
        isIshitsukiMode        = data.isIshitsukiMode;
        treeOrigin             = (TreeOrigin)data.treeOrigin;
        plantingNormal         = new Vector3(data.planNX, data.planNY, data.planNZ);
        plantingSurfacePoint   = new Vector3(data.planPX, data.planPY, data.planPZ);
        // Migrate saves that stored plantingSurfacePoint at world origin (0,0,0) while
        // the tree is elevated.  Any save where the stored Y is more than 10 units from
        // the tree's actual world Y was never intentionally set — reset to the tree pos.
        if (Mathf.Abs(plantingSurfacePoint.y - transform.position.y) > 10f)
        {
            plantingSurfacePoint = transform.position;
            Debug.Log($"[Migration] plantingSurfacePoint corrected to tree world pos y={transform.position.y:F1}");
        }
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
            node.hasFlowerBud       = sn.hasFlowerBud;
            node.backBudStimulated  = sn.backBudStimulated;
            node.preferredLateralAzimuth = sn.preferredLateralAzimuth;
            node.isTrimCutPoint     = sn.isTrimCutPoint;
            node.trimCutDepth       = sn.trimCutDepth;
            node.regrowthSeasonCount= sn.regrowthSeasonCount;
            node.health             = sn.health;
            node.branchLoad  = sn.branchLoad;
            node.sagAngleDeg = sn.sagAngleDeg;
            node.pendingSagDeg = sn.pendingSagDeg;
            node.sagDegPerDay  = sn.sagDegPerDay;
            node.elasticSagDeg = sn.elasticSagDeg;
            node.isDead        = sn.isDead;
            node.isDeadwood    = sn.isDeadwood;
            node.shadedSeasons = sn.shadedSeasons;
            node.deadSeasons   = sn.deadSeasons;
            node.isJin         = sn.isJin;
            node.jinBleach     = sn.jinBleach;
            node.fungalLoad          = sn.fungalLoad;
            node.isMycorrhizal       = sn.isMycorrhizal;
            node.healthySeasonsCount = sn.healthySeasonsCount;
            node.hasWire            = sn.hasWire;
            node.wireOriginalDirection = new Vector3(sn.woX, sn.woY, sn.woZ);
            node.wireTargetDirection   = new Vector3(sn.wtX, sn.wtY, sn.wtZ);
            node.wireSetProgress    = sn.wireSetProgress;
            node.wireDamageProgress = sn.wireDamageProgress;
            node.wireAgeDays        = sn.wireAgeDays;
            node.wireSetSpeedMult   = sn.wireSetSpeedMult <= 0f ? 1f : sn.wireSetSpeedMult;
            node.isTrainingWire     = sn.isTrainingWire;
            node.boundaryPressure   = sn.boundaryPressure;
            node.isAirLayerRoot     = sn.isAirLayerRoot;
            node.hasWound           = sn.hasWound;
            node.woundRadius        = sn.woundRadius;
            node.woundFaceNormal    = new Vector3(sn.wnX, sn.wnY, sn.wnZ);
            node.woundAge           = sn.woundAge;
            node.pasteApplied       = sn.pasteApplied;
            node.hadBudScar         = sn.hadBudScar;
            node.budScarAge         = sn.budScarAge;
            node.hadWoundScar       = sn.hadWoundScar;

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

    /// <summary>Runtime scale on depthsPerYear. Driven by AutoStyler's fastConverge debug
    /// toggle (2× while on); never serialized so it can't bake into the scene.</summary>
    [System.NonSerialized] public float depthsPerYearMult = 1f;

    LeafManager leafMgr;   // cached in Awake; used by the daily elastic leaf-load pass

    int EffectiveDepthsPerYear => Mathf.Max(1, Mathf.RoundToInt(depthsPerYear * depthsPerYearMult));

    /// <summary>Maximum depth allowed to sprout children this season.</summary>
    int SeasonDepthCap => startYear < 0
        ? EffectiveDepthsPerYear
        : Mathf.Min(maxDepth, (GameManager.year - startYear + 1) * EffectiveDepthsPerYear);

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
                int dpy = EffectiveDepthsPerYear;
                if (n.regrowthSeasonCount <= 1 && SeasonDepthCap > 0)
                {
                    float severity = (float)n.trimCutDepth / SeasonDepthCap;
                    if (severity > severeCutSeverityThreshold)
                        dpy = Mathf.Max(1, Mathf.RoundToInt(EffectiveDepthsPerYear * severeCutRecoveryScale));
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
        // Default soil surface to the tree's actual world position.
        // plantingSurfacePoint is world-space; if left at Vector3.zero and the tree
        // is elevated, ContinuationDirection sees every root as 82+ units above soil
        // and blends 85% toward Vector3.down — making all roots shoot straight down.
        // LoadTree() will override this for saved games; InitNewTree() inherits it.
        plantingSurfacePoint = transform.position;
        plantingNormal       = Vector3.up;

        // Initialise WeedManager pot bounds — updated each spring with the real position.
        var wm = GetComponent<WeedManager>();
        if (wm != null)
            wm.SetPotBounds(new Vector3(transform.position.x, plantingSurfacePoint.y, transform.position.z), weedSpawnRadius);

        leafMgr = GetComponent<LeafManager>();
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

}
