# BONSAIMANCER — Development Plan

Last updated: 2026-04-07 · Items 1–32 complete (28–30 dieback/death/weight, 31–32 air layer)

---

## Completed This Phase

- **22.** Fertilizer System — `nutrientReserve` (0→2) drains 0.4/season; `Fertilize()` blocked in winter; `nutrientMult` Lerp(0.6,1.4) multiplied into per-frame growth speed; FertilizerBurn on roots if reserve >1.5 at spring start; Fertilize button + nutrient bar right-side panel; Auto-fertilize toggle in Debug tab; serialized in SaveData
- **23.** Weed System — RMB click-hold-drag-up to pull; rip chance leaves stub (harder next pull, stump visual, 60% drain); Herbicide button clears all weeds + sets aeration penalty next season; WeedManager spawns procedural cube weeds (grass/clover/dandelion/thistle) as tree children; seasonal nutrient+moisture drain; weeds serialized in SaveData
- **24.** Fungus System — `fungalLoad` (0–1) per node; spreads from open wounds / overwatered roots / low-health nodes; seasonal spread to neighbours; FungalInfection DamageType; Fungicide button reduces load 0.6 across all nodes; mycorrhizal network on healthy root nodes (3+ healthy seasons) reduces nutrient drain 20%; herbicide kills mycorrhizae; infected leaves tint toward sickly yellow-green via MaterialPropertyBlock; all fields serialized
- **25.** Species Skeleton — `TreeSpecies` ScriptableObject; `ApplySpecies()` copies into existing `TreeSkeleton` fields on Awake; `BudType` moved to own file; species name displayed in Settings menu header; Japanese Maple (Opposite buds, fast/thirsty/fragile) and Juniper (slow/drought-tolerant/resilient) as starter species
- **26.** Species Selection Menu — fullscreen overlay on game start; 16 species with Growth / Water / Care / Soil chip tags; sortable by any tag; confirms into TipPause with species applied; `SpeciesSelect` GameState; ToolTip fixed to only show in TipPause without touching Main Camera
- **27.** Soil / Substrate System — `PotSoil` component; 7 substrates (akadama, pumice, lava rock, organic, sand, kanuma, perlite); weighted mix → derived properties; seasonal degradation + saturation + root rot; species soil mismatch penalty; `Repot()` with timing and too-soon stress multipliers; 4 presets; soil bars in Repot panel; 16 species .asset files with soil preferences; Roots→Repot rename; weeds auto-cleared on entering Repot mode; weeds excluded from trunk radius and rock surfaces

---

## Numbered Items

All items sorted ascending. Click a title to expand the spec.

---

<details>
<summary><strong>1. Tight-Angle Geometry</strong> ✓ done</summary>

**Goal:** Prevent vertex pinching where branches bend sharply.
**Scope:** `TreeMeshBuilder.cs`

- Bend rings inserted at child base
- Parallel-transport fix prevents tip ring twist

</details>

---

<details>
<summary><strong>2. Post-Trim Depth Cap</strong> ✓ done</summary>

**Goal:** After trimming back hard, regrowth is limited to early-year depths —
a branch cut to depth 1 can't grow 6 levels in one season.
**Scope:** `TreeSkeleton.cs`, `TreeNode.cs`

- Add `trimCutDepth` and `regrowthSeasonCount` fields to `TreeNode`
- When a node is identified as a trim cut point, record its depth
- Each new growing season, the cut point's subtree gets a depth cap of:
  `trimCutDepth + (regrowthSeasonCount * REGROWTH_DEPTH_PER_SEASON)`
  where `REGROWTH_DEPTH_PER_SEASON` mirrors the year-1 allowance
- `regrowthSeasonCount` increments each spring on affected nodes
- After enough seasons the cap naturally reaches the global `depthCap` and the
  special treatment ends

</details>

---

<details>
<summary><strong>3. Wire Rework + Health System Foundation</strong> ✓ done</summary>

**Goal:** Realistic wiring with meaningful consequences; health system that
future mechanics (watering, nutrients, trimming trauma) can feed into.

#### 3a. Wire Rework

**New wiring flow:**
1. Player aims and confirms wire direction
2. `GameState.WireAnimate` fires — time frozen, camera still moves
3. Branch snaps to `wireTargetDirection` immediately in skeleton data
4. A ~0.6 s spring animation plays on the mesh with slight overshoot + settle
5. On animation end → auto-resume prior game state

**New data on `TreeNode`:**
```
wireOriginalDirection   Vector3   direction at time of wiring
wireTargetDirection     Vector3   player-aimed direction
wireSetProgress         float     0→1, fully set = ready to remove
wireDamageProgress      float     0→1, accumulates after fully set
wireAgeDays             float     total in-game days wire has been on
```

**Set/damage accumulation:**
- Both progress values accumulate only during `BranchGrow` state
- Rate uses the same `SeasonalGrowthRate` multiplier
- `setProgress` fills over ~2 growing seasons at speed 1
- `damageProgress` begins filling as soon as `setProgress >= 1.0`

**Early removal (setProgress < 1.0):**
```
newDirection = Slerp(wireOriginalDirection, wireTargetDirection, setProgress)
```

**Re-wiring a previously-set branch:**
- Allowed freely; no cooldown
- Re-bending set wood applies a health hit: `damage = Lerp(0.05, 0.25, setProgress)`

**Wire colour progression:**
| Condition | Colour |
|---|---|
| `setProgress` 0→1 | Silver |
| `setProgress >= 1.0` | Gold emissive pulse |
| Damage zone begins | Gold → Orange |
| `damageProgress >= 0.5` | Orange → Red |
| `damageProgress >= 1.0` | Deep red, embedded |

#### 3b. Health System (Foundation)

**`float health = 1f` on `TreeNode`, range 0→1.**

Health thresholds:
| health | Effect |
|---|---|
| `< 0.75` | Growth rate multiplied by `health` |
| `< 0.5` | Leaves drop early; branch tints grey |
| `< 0.25` | Branch goes dormant |
| `<= 0` | Branch dead |

`DamageType` enum lives in `NodeHealth.cs`.

</details>

---

<details>
<summary><strong>4. Root System</strong> ✓ done</summary>

**Goal:** Visible surface/subsurface roots (nebari), trimmable in RootPrune mode
and naturally scorable for flared-root development.
**Scope:** `TreeSkeleton`, `TreeNode`, `TreeMeshBuilder`, `TreeInteraction`, `CameraOrbit`

- `isRoot` flag on `TreeNode`; roots are children of `skeleton.root`
- Gravity-biased `ContinuationDirection` and `LateralDirection` for root nodes
- Separate depth cap: `maxRootDepth` for root nodes
- `renderRoots` flag on `TreeMeshBuilder`
- `PlantRoot(Vector3 localDir)` — player-triggered from soil plane click
- `GameState.RootPrune`: tree lifts, roots revealed
- `HandleRootWorkHover()` in `TreeInteraction`
- `CameraOrbit` pitch relaxed to `pitchMinRootPrune` in RootPrune mode

</details>

---

<details>
<summary><strong>5. Bud System</strong> ✓ done</summary>

**Goal:** Spring growth emerges from pre-formed buds set the previous late summer.

**Phases:**

**Bud Set (August):**
- `node.hasBud = true`
- Bud GameObject spawned at `node.tipPosition`

**Dormant lateral buds:**
- `dormantBudCount` tracks latent axillary buds
- Increased activation chance if `backBudStimulated = true`

**Bud Break (March):**
- Bud GameObjects destroyed
- Growth begins from those nodes

**Back-budding from pruning:**
- Nearest 2–3 ancestor nodes get `backBudStimulated = true`
- Next spring: elevated lateral activation chance

**New data on `TreeNode`:**
```
hasBud               bool
backBudStimulated    bool
```

</details>

---

<details>
<summary><strong>6. Wound System</strong> ✓ done</summary>

**Goal:** Trimming branches leaves wounds that are a real health risk without care.

**Wound lifecycle:**
1. Branch trimmed → wound created at parent node
2. Wound GameObject spawned, scaled by `woundRadius`
3. Each growing season: wound drains health
4. Player applies cut paste → `pasteApplied = true`, drain drops to ~5%
5. `woundAge` increments each season; heals when `woundAge >= woundRadius × seasonsToHealPerUnit`

**Health drain:**
```
drain = woundRadius × woundDrainRate × (pasteApplied ? 0.05f : 1.0f)
```

**New data on `TreeNode`:**
```
hasWound       bool
woundRadius    float
woundAge       float
pasteApplied   bool
```

</details>

---

<details>
<summary><strong>7–16. Early Systems</strong> ✓ done</summary>

These items were completed in an earlier development phase. Full details in the Completed log at the bottom of this document.

- **7.** Bud/Leaf Integration — `birthYear` on `TreeNode`; leaves from buds on old wood
- **8.** Growth Stability — `maxBranchNodes` hard cap, `vigorFactor` lateral scaling
- **9.** Leaf Energy System — `treeEnergy` multiplier from canopy photosynthesis
- **10.** Refinement Level — `refinementLevel` on nodes; segment-length taper per level
- **11.** Dynamic Leaf Scale — root pressure + refinement drive `seasonLeafScale`
- **12.** Ishitsuki (Root-over-Rock) — `RockPlace` + `TreeOrient` states, training wires, drape logic
- **13.** Watering System — `soilMoisture`, `drainRatePerDay`, drought threshold + damage
- **14.** Save / Load System — full JSON save/load; `SaveData`, `SaveNode`, auto-save on season end
- **15.** Root Area Box Containment — `rootAreaTransform` replaces radial spread; six-face deflection
- **16.** Pot-Bound Root System — `boundaryPressure`, `boundaryGrowthScale`, fill-in laterals

</details>

---

<details>
<summary><strong>17. Root Visibility Bug (Post-Ishitsuki)</strong> ✓ done</summary>

**What was built:**
- `debugRootVisibility` toggle on `TreeMeshBuilder` draws GL lines on every root node:
  Cyan = isTrainingWire, Yellow = isAirLayerRoot, Green = included, Red = excluded
- `[RootVis] BuildMesh` log fires only on state change, not every dirty rebuild
- **Ghost root fix:** `SpawnTrainingWires` was calling `child.children.Clear()` on old
  root chains — removing list references but leaving old nodes in `allNodes` with
  `isGrowing=true`. Fixed by calling `RemoveSubtree` on each old child before clearing.

**Files changed:** `TreeSkeleton.cs`, `TreeMeshBuilder.cs`

</details>

---

<details>
<summary><strong>18. Auto-Water</strong> ✓ done</summary>

Waters automatically just before drought threshold is reached. On by default; Debug tab toggle. When auto-water fires, the Water button pulses between light and dark grey (0.15 s). In-game-day cooldown prevents rapid-fire at high timescale.

**Files changed:** `TreeSkeleton.cs`, `buttonClicker.cs`, `ButtonUI.uxml`

</details>

---

<details>
<summary><strong>19. Ishitsuki White First Segment</strong> ✓ done</summary>

**Bug:** The first segment of each Ishitsuki root chain (`startNode`) stayed permanently
white — never transitioned to bark colour because the age accumulation loop skipped
non-training-wire roots.

**Fix:** `startNode` marked `isTrainingWire=true` in `PreGrowRootsToSoil` so age
accumulates and it transitions to bark colour like the rest of the chain.

**Scope:** `TreeSkeleton.cs`

</details>

---

<details>
<summary><strong>20. Ishitsuki Roots Continue Underground</strong> ✓ done</summary>

**Bug:** After a training wire chain reached the soil plane, the terminal node was frozen.
Nothing transitioned it into the normal pot root system.

**Fix:** In `PreGrowRootsToSoil`, after the step loop breaks at soil, create one transition
node at the soil contact point (`isTrainingWire=false`) that enters the normal root system.
Underground roots blocked from growing if tip is above soil.

**Scope:** `TreeSkeleton.cs`

</details>

---

<details>
<summary><strong>21. Ishitsuki Cable Growth Animation</strong> ✓ done</summary>

**Feature:** New training wire chains visibly grow down the rock face each spring rather
than appearing fully pre-grown instantly.

**Implementation:** `PreGrowRootsToSoil(animated:true)` each spring places one segment
per strand. Confirm uses `animated:false` (instant full drape). Air-grown chain cleanup
preserves training-wire progress.

**Scope:** `TreeSkeleton.cs`

</details>

---

<details>
<summary><strong>22. Fertilizer System</strong> ✓ done</summary>

`nutrientReserve` (0→2) on `TreeSkeleton`. Drains 0.4/season; `Fertilize()` adds 0.5,
capped at 2, blocked in winter. Growth speed multiplier: `Lerp(0.6, 1.4, reserve/2)`.
FertilizerBurn applied to root nodes each spring when `reserve > 1.5`. Fertilize button
+ nutrient bar on right-side panel. Auto-fertilize toggle in Debug tab. Serialized.

</details>

---

<details>
<summary><strong>23. Weed System</strong> ✓ done</summary>

RMB click-hold-drag-up to pull weeds; positive Y delta accumulates pull progress.
Rip chance (per type) leaves a stub — shorter, brown, harder to pull next time.
`Physics.RaycastAll` required to click through Bonsai/PlanterTable colliders.
Weeds parented to tree GO so camera-orbit drag doesn't conflict. WeedManager singleton
auto-adds WeedPuller. Herbicide button clears all + sets aeration penalty. Four types:
Grass (40%), Clover (35%), Dandelion (15%), Thistle (10%). Serialized.

</details>

---

<details>
<summary><strong>24. Fungus System</strong> ✓ done</summary>

`fungalLoad` (0–1) + `isMycorrhizal` + `healthySeasonsCount` on `TreeNode`. Each spring:
nodes with open wounds, overwatered roots (`soilMoisture > 0.9`), or low health (<0.5)
accumulate fungalLoad; infected nodes spread to parent/children with 25% chance; nodes
above 0.4 load take `FungalInfection` damage scaled by excess. Recovery: 0.1/season when
no longer at-risk.

Mycorrhizal: root nodes healthy 3+ seasons become `isMycorrhizal`; reduces nutrient drain
by up to 20% based on coverage fraction. Fungicide and herbicide both kill mycorrhizae.

Visual: leaf tint toward sickly yellow-green via `MaterialPropertyBlock` + `fungalSeverity`
field on `Leaf.cs`.

Fungicide button on right-side panel; dims when no infection present; calls `ApplyFungicide()`
(reduces all loads by 0.6) + immediate leaf refresh. All fields serialized.

</details>

---

<details>
<summary><strong>25. Species — Skeleton</strong> ✓ done</summary>

`TreeSpecies` ScriptableObject created with all core species-differentiating parameters.
`ApplySpecies()` on `TreeSkeleton` copies ScriptableObject values into existing fields on
Awake — zero changes to downstream code, existing SerializeField values become fallbacks.
`BudType` enum moved to its own file so `TreeSpecies` can reference it.
Species name displayed in Settings menu header.

**Starter species shipped:**
- **Japanese Maple** — Opposite buds, fast growth (0.26), thirsty (drain 0.14/day), fragile wounds (0.08 drain/season), wire sets fast (140 days), high lateral density
- **Juniper** — Alternate buds, slow growth (0.14), drought-tolerant (drain 0.06/day), resilient wounds (0.03), wire sets slow (280 days), strong apical dominance

**Species Visuals** (bark shaders, leaf shapes, seasonal colour sets) deferred to after Health Consequences phase.

</details>

---

<details>
<summary><strong>27. Soil / Substrate System</strong> ✓ done</summary>

Full spec in Backlog → Soil / Substrate System.

</details>

---

<details>
<summary><strong>28. Tree Death ✓</strong></summary>

Full spec in Backlog → Tree Death. **Toggleable** — `treeDeathEnabled` bool on TreeSkeleton; all death checks skip when false. Off by default; turned on for testing then back off.

**Done:** `treeDeathEnabled` toggle, drought/health death conditions, `consecutiveCriticalSeasons` counter, `treeInDanger` flag, `LastDeathCause`, `GameState.TreeDead`, death overlay with load/restart buttons, `TreeDangerBanner` warning.

</details>

---

<details>
<summary><strong>29. Branch Death & Dieback ✓</strong></summary>

Full spec in Backlog → Branch Death & Dieback.

**Done:** `isDead`, `isDeadwood`, `shadedSeasons`, `deadSeasons` on `TreeNode`. `DiebackPass()` each spring: marks zero-health nodes dead, shading check on interior nodes (no living terminal children), small dead branches drop after `deadSeasonsToDrop` seasons, large ones become permanent deadwood. All fields serialized.

</details>

---

<details>
<summary><strong>30. Branch Weight & Strength ✓</strong></summary>

Full spec in Backlog → Branch Weight & Strength.

**Done:** `branchWeightEnabled` toggle + inspector fields. `BranchWeightPass()` calls
`ComputeLoad()` (bottom-up mass accumulation) then `ApplySagAndStress()` (maturity/strength
ratio, sag angle accumulation, `growDirection` Slerp toward down, `PropagatePositions()`
for descendant world positions, junction stress damage). `branchLoad` and `sagAngleDeg`
saved and restored in SaveManager.

</details>

---

<details>
<summary><strong>26. Multi-Tree / Quick-Start</strong> (moves here after Health phase)</summary>

Multiple trees in one session; save/load per-tree. Auto-generate a tree at a given age
with randomised style.

</details>

---

<details>
<summary><strong>31. Air Layer Root Continued Growth ✓</strong></summary>

Full spec in Backlog → Air Layer Root Continued Growth.

**Done:** `ContinuationDirection` now handles rock-surface deflection and soil-plane snap for
`isAirLayerRoot` nodes instead of early-returning. Continuation nodes transition to
`isAirLayerRoot = false` once their parent tip is at or below `plantingSurfacePoint.y`,
allowing underground growth to proceed as normal root segments.

</details>

---

<details>
<summary><strong>33. Named Save / Load Menu ✓</strong></summary>

**Goal:** Replace the single-slot save with a named multi-slot system. All saves persist
across sessions and are browsable from a load screen. Each save shows key metadata so the
player knows what they're loading without guessing from a filename.

---

**Save metadata (stored alongside each save):**

| Field | Notes |
|---|---|
| `saveName` | Player-entered string (max 32 chars) |
| `treeOrigin` | Enum: `Seedling`, `Cutting`, `AirLayer` |
| `speciesName` | Human-readable species name |
| `year` / `month` | In-game date at save time |
| `screenshotPath` | Optional thumbnail (128×128 PNG, same folder) |
| `saveTimestamp` | Real UTC time for sorting (ISO 8601 string) |
| `seasonsSinceRepot` | Quick health indicator |

`treeOrigin` is set once at tree birth and never changes:
- **Seedling** — default; player selected a species in the picker and started fresh
- **Cutting** — started from a `propagation` save (future feature hook; default to Seedling for now)
- **AirLayer** — tree was created via `SeverAirLayer`

---

**Data layout on disk:**

```
Application.persistentDataPath/
  saves/
    <slot-id>/           ← GUID or timestamp string, e.g. "20260407_143022"
      save.json          ← full SaveData (existing format)
      meta.json          ← SaveMeta (new lightweight struct)
      thumb.png          ← optional 128×128 screenshot
```

The existing `bonsai_save.json` and `bonsai_original.json` paths remain as a compatibility
fallback for the first session; a migration step on first load moves them into the new layout.

---

**SaveMeta struct (new, serializable):**

```csharp
[Serializable]
public class SaveMeta
{
    public string saveName;
    public string slotId;
    public int    treeOrigin;   // TreeOrigin enum cast to int
    public string speciesName;
    public int    year;
    public int    month;
    public string saveTimestamp;
    public int    seasonsSinceRepot;
    public int    nodeCount;    // rough tree size indicator
}

public enum TreeOrigin { Seedling, Cutting, AirLayer }
```

---

**SaveManager changes:**

- `static string SavesRoot` — `persistentDataPath/saves/`
- `static void SaveSlot(string slotId, TreeSkeleton, LeafManager, SaveMeta meta)` — writes
  `save.json` + `meta.json` to `saves/<slotId>/`
- `static bool LoadSlot(string slotId, TreeSkeleton, LeafManager)` — reads from slot folder
- `static List<SaveMeta> ListAllSaves()` — scans `saves/*/meta.json`, returns sorted by
  `saveTimestamp` descending (most recent first)
- `static string NewSlotId()` — returns `DateTime.UtcNow.ToString("yyyyMMdd_HHmmss")`
- Quick-save (current Save button) writes to the **active slot** (last loaded or just saved);
  if no active slot exists, prompts for a name first
- `static string ActiveSlotId { get; private set; }` — persists to `PlayerPrefs`

---

**New `SaveData` fields:**

```csharp
public string saveName;
public int    treeOrigin;   // TreeOrigin cast to int
public string speciesName;
public string saveTimestamp;
```

`treeOrigin` is set when `SpeciesSelect` confirms (→ Seedling) or when `SeverAirLayer`
completes (→ AirLayer). Stored on `TreeSkeleton` as a durable field, serialized into
every save made from that tree.

---

**Load Menu UI (`LoadMenuOverlay`):**

- Fullscreen overlay, appears in place of `SpeciesSelectOverlay` when a save exists at launch,
  or from the Settings → Load button at any time
- Scrollable list of save cards; each card shows:
  - **Save name** (large)
  - **Origin badge** — small chip: "🌱 Seedling", "✂ Cutting", "🌿 Air Layer" (coloured)
  - **Species name**
  - **In-game date** (e.g. "April 2131")
  - **Real save time** (e.g. "3 days ago")
  - **Node count** as a rough size proxy ("small / medium / large")
  - Optional thumbnail if screenshot exists
- Buttons per card: **Load** | **Delete** (confirm prompt before delete)
- Footer button: **New Game** — skips load menu, goes to SpeciesSelect
- On launch: if any save exists → show Load Menu; otherwise → go straight to SpeciesSelect

---

**Save Name Prompt:**

When saving to a new slot (no active slot), show a small modal with a text field
(`UnityEngine.UIElements.TextField`) pre-filled with `"<Species> <Year>"`.
Confirm saves; Escape cancels (falls back to quick-overwrite of the last slot if one exists).

---

**Scope:**

- `SaveManager.cs` — slot system, meta read/write, migration
- `SaveData` / `SaveMeta` — new fields
- `TreeSkeleton.cs` — `treeOrigin` field, set at species confirm and sever
- `buttonClicker.cs` — Load Menu, Save Name Prompt, card list build, slot tracking
- `ButtonUI.uxml` — `LoadMenuOverlay`, `SaveNamePromptOverlay`, card template
- `GameManager.cs` — launch flow: check for saves → LoadMenu or SpeciesSelect
- `GameState.LoadMenu` — new state

</details>

---

<details>
<summary><strong>32. Air Layering to New Tree ✓</strong></summary>

Full spec in Backlog → Air Layering & Branch-to-New-Tree.

**Done:** `AirLayerData` gets `isUnwrapped`, `rootGrowSeasons`, `isSeverable`. `UpdateAirLayers`
tracks post-unwrap season count; sets `isSeverable` after `airLayerRootSeasonsToSever` seasons.
`SeverAirLayer`: wounds cut site, removes subtree, saves original to `bonsai_original.json`,
builds `SaveData` from severed subtree (air-layer node becomes root, depths recalculated, air
roots become normal roots), loads new tree via `LoadFromSaveData`, transitions to Idle.
UI: `AirLayerSeverBanner` (shown in Update when `HasSeverableLayer`), `AirLayerSeverOverlay`
confirm/cancel, `LoadOriginalButton` in pause menu (visible when backup exists).
`GameState.AirLayerSever` freezes time while prompt is open.

</details>

---

<details>
<summary><strong>Species — Visuals</strong></summary>

Procedural geometry + hand-authored bark textures that cross-fade between age stages.
The player makes a set of seamless bark textures per species per age tier; the shader
blends between adjacent tiers based on node age. Geometry variation is fully procedural.

---

#### 0. Bark Texture System

**Design:** Each species has N bark texture tiers (suggested 3–4: seedling, young, mature, old).
Adjacent tiers are designed to be seamless with each other so the blend is smooth.
The shader samples two adjacent tiers and lerps between them using a `_BlendFactor` (0→1).

**UV generation** — `TreeMeshBuilder.AddRing` already writes UVs. The convention needs
to be locked down so textures tile correctly:
- **U** = angle around the ring, 0→1 wrapping the full circumference
- **V** = `cumulativeHeight * vTilingScale` — a per-species tiling scale so thick old trunks
  and thin twigs both show an appropriate amount of texture repeat
- UV continuity across segments is already handled by sharing the tip ring with the next
  segment's base ring — no seams between segments along the length

```csharp
// On TreeSpecies:
public Texture2D[] barkTiers;          // [0]=seedling, [1]=young, [2]=mature, [3]=old
public float[]     barkTierAgeBreaks;  // age values where each tier begins, e.g. {0, 1, 4, 10}
public float       barkVTiling = 2f;   // how many times the texture repeats per world unit of length
```

**Shader** (`Custom/BarkBlend`) — replaces the current `Custom/BarkVertexColor`:
```hlsl
// Inputs:
//   _BarkTexA, _BarkTexB  — the two adjacent tier textures
//   _BlendFactor          — 0=fully A, 1=fully B (per-mesh, set by TreeMeshBuilder)
//   vertex.color          — still used for growth tint, seasonal tint, deadwood overlay
//
// Sample both, lerp, then multiply by vertex color for tint overlay.
half4 col = lerp(tex2D(_BarkTexA, uv), tex2D(_BarkTexB, uv), _BlendFactor);
col *= IN.color;
```

**Blend factor calculation** — `TreeMeshBuilder` determines which two tiers a node sits
between based on its `age` and the species `barkTierAgeBreaks` array, then sets
`_BlendFactor` on a `MaterialPropertyBlock` per node (same mechanism as the existing
fungal tint system):
```csharp
int   tierA     = TierForAge(node.age, species.barkTierAgeBreaks);
int   tierB     = Mathf.Min(tierA + 1, species.barkTiers.Length - 1);
float blend     = NormalizedBlendInTier(node.age, tierA, species.barkTierAgeBreaks);
mpb.SetTexture("_BarkTexA", species.barkTiers[tierA]);
mpb.SetTexture("_BarkTexB", species.barkTiers[tierB]);
mpb.SetFloat("_BlendFactor", blend);
renderer.SetPropertyBlock(mpb);
```

**Vertex color role shifts** — with textures providing the base bark color, vertex color
becomes a multiplicative tint layer only:
- Young growth: slight green-yellow tint (blends out as age increases)
- Spring flush: species-specific tint overlay for the first days of the season
- Deadwood: desaturate/tint toward the species deadwood color
- Fungal infection: existing sickly yellow-green tint (already per-node via MPB)
- Seasonal bark tint: very subtle warm/cool shift

**Art workflow (for you):**
1. Model the segment at a known scale in Blender (e.g. 1m radius, 1m tall cylinder)
2. Paint/photograph each bark tier as a seamless square texture
3. Each adjacent pair must be blendable — design them as variations of the same base pattern
4. Import to Unity, assign to `barkTiers[]` on the species `.asset`
5. Tune `barkTierAgeBreaks` and `barkVTiling` until it looks right in-engine

**PS2/low-poly consideration:** At low `ringSegments` (5–6 for juniper), UVs stretch
noticeably across each face — that's a feature, not a bug for this aesthetic. Slightly
blurry or painterly textures will look better than sharp photographic ones at this poly
count.

---

---

#### 1. Geometry Variation

**Ring segment count** — controls how round vs. angular the wood looks. Set per species on `TreeSpecies`:
```csharp
public int ringSegments = 7;   // default; override per species
```
- Japanese Maple: **7–8** (rounder, smoother young wood)
- Juniper: **5–6** (angular, faceted, gnarly character)
- `TreeMeshBuilder` reads `skeleton.RingSegments` instead of the current hardcoded value

**Bark vertex jitter** — older/thicker nodes get slight per-vertex radial noise to break up the perfect cylinder. Controlled by two species fields:
```csharp
public float barkJitterRadius   = 0f;    // max world-unit displacement per vertex
public float barkJitterAgeMin   = 2f;    // minimum node age before jitter kicks in
```
Applied inside `AddRing` after the base position is computed. Young growth stays smooth; old wood gets organically rough.

**Junction swell** — at fork points, inflate the base ring of each child slightly so the branch origin looks like it's bulging out of the parent rather than snapping to a cylinder edge:
```csharp
public float junctionSwellFactor = 1.15f;   // multiplier on base ring radius at forks
```
Applied in `ProcessNode` when `parentBaseRingIndex != -1` and the node has siblings.

**Bark ridge fins** — a second geometry pass on mature segments (radius above a threshold) adds thin longitudinal fins running along the segment length. For species like elm or zelkova with deeply ridged bark:
```csharp
public int   barkRidgeCount     = 0;     // 0 = disabled
public float barkRidgeHeight    = 0.02f; // world units above surface
public float barkRidgeAgeMin    = 3f;
```

---

#### 2. Color & Shader Variation

All color is vertex color — no textures, no material swaps. New fields on `TreeSpecies`:

**Spring flush tint** — new growth color in the first weeks of the growing season before it hardens to bark:
```csharp
public Color springFlushColor = new Color(0.55f, 0.72f, 0.35f);  // default yellow-green
// Maple: bright red-pink flush
// Juniper: blue-green, barely distinguishable from mature
```
`GrowthColor()` in `TreeMeshBuilder` already lerps by age — this replaces the hardcoded young-growth color with the species value.

**Mature bark color** — replaces the current single hardcoded bark brown:
```csharp
public Color matureBarkColor  = new Color(0.32f, 0.22f, 0.14f);
public Color oldBarkColor     = new Color(0.22f, 0.15f, 0.10f);  // darker for very old wood
public float oldBarkAgeThreshold = 8f;
```

**Seasonal tints** — blended in by `GameManager.LeafHue` equivalent for bark (separate from leaf color):
```csharp
public Color autumnBarkTint   = Color.white;   // most species: no change
// Maple autumn: slight warm orange cast on young bark
```

**Deadwood color** — currently a single grey-brown everywhere:
```csharp
public Color deadwoodColor    = new Color(0.65f, 0.60f, 0.55f);
// Juniper jin: bleached silver-white
// Maple: darker charcoal grey
```

---

#### 3. Bud System Visuals

Buds are currently just a prefab placed at the tip position with no internal structure. This replaces them with procedurally animated geometry.

**Bud geometry** — a small teardrop/egg shape built from `AddRing`-style stacked rings, tapered at the tip. Size driven by `terminalRadius * budSizeMultiplier`. Scales up slightly over autumn/winter as the bud fattens:
```csharp
public float budSizeMultiplier  = 2.5f;
public Color budColor           = new Color(0.28f, 0.20f, 0.12f);   // dark brown
public Color budScaleColor      = new Color(0.35f, 0.28f, 0.16f);   // scale lines
```

**Bud break animation** — each spring, over the first 2–3 in-game days, the bud mesh transitions:
1. Bud scales separate and peel back (radial displacement of outer ring vertices)
2. Inner leaf/flower geometry emerges from the center (small rolled cone expanding)
3. Once fully open, the bud mesh is destroyed and normal leaf geometry takes over

Species control:
```csharp
public BudBreakType budBreakType = BudBreakType.Leaf;
// Leaf: standard green emerging foliage
// Flower: small clustered geometry before leaf flush (cherry, apple)
// NeedleCluster: radiating thin fins (pine, juniper)
```

**Opposite vs. alternate** — maple's opposite buds sit in facing pairs at each junction; juniper's alternate buds spiral. The `BudType` enum already exists; bud placement just needs to respect spacing and orientation correctly in `SetBuds()`.

**Flower buds (future hook)** — `BudBreakType.Flower` generates a small tight cluster geometry per node on designated flowering branches. Full flower system (petal geometry, pollination, fruit) deferred; this just reserves the hook.

---

#### Scope

- `TreeSpecies.cs` — all new fields above (ring segments, jitter, swell, ridges, colors, bark tier textures/age breaks/tiling)
- `TreeMeshBuilder.cs` — `ringSegments` from species; jitter in `AddRing`; junction swell; ridge fin pass; bark tier blend via `MaterialPropertyBlock`; vertex color becomes tint-only; UV tiling scale
- `Custom/BarkBlend` shader — new shader replacing `Custom/BarkVertexColor`; dual texture sample + lerp + vertex color multiply
- `TreeSkeleton.cs` — expose `RingSegments` property; pass species refs to mesh builder
- `BudManager` or extended `TreeSkeleton.cs` — bud break animation coroutine; procedural bud geometry builder
- `GameManager.cs` — seasonal bark tint pass (analogous to `LeafHue`)
- All 16 species `.asset` files updated with new fields
- Art assets (your work): seamless bark texture sets per species, 3–4 tiers each

</details>

---

## Backlog

Future features not yet scheduled. Expand to read the spec.

---

<details>
<summary><strong>Pot-Bound Soil Block</strong></summary>

When pot-bound roots go unchecked for **2+ years**, they fuse with the soil into
a dense impacted mass that must be dealt with before Ishitsuki is available.

**Hard blocker** — `GameState.RockPlace` is gated until cleared.

**Two removal methods:**
- *Wash out* — hold-input timed action; slower, low trauma
- *Cut out* — use root-prune scissors on `isSoilBlock` nodes; faster, health cost

**Implementation notes:**
- Track `potBoundYears` counter on `TreeSkeleton`; increment each spring when
  `boundaryPressure` across all roots exceeds a threshold
- `soilBlockActive` bool gates `GameState.RockPlace` in `buttonClicker`
- Cut-out: clicking root nodes marked `isSoilBlock` in RootPrune mode shears them

</details>

---

<details>
<summary><strong>Branch Weight & Strength</strong></summary>

Heavy branches sag over time under their own load. Thin young wood bends; thick old
wood resists. Wires, careful pruning, and prop supports counteract droop.

> **Open design question:** Universal vs. species-gated? Large-branched juniper / cascading
> pine needs this badly. Fine-twigged maple probably doesn't. Likely a species flag
> (`branchWeightEnabled`) rather than a global system.

#### Core Data

Two new floats on `TreeNode`:
```
branchLoad     float   accumulated downward force (own mass + children's load)
branchStrength float   derived from radius and age; not stored
```

`branchLoad` computed bottom-up each spring:
```
node.branchLoad = node.radius² × node.length × woodDensity
                + sum(child.branchLoad for all children)
```

`branchStrength`:
```
strength = radius³ × woodHardnessFactor × Clamp01(node.age / matureAgeSeasons)
```

#### Sag Mechanic

Each spring, if `branchLoad / branchStrength > sagThreshold`:
```
sagAngle = Clamp((load/strength - sagThreshold) * sagSensitivity, 0, maxSagAngleDeg)
node.growDirection = Slerp(node.growDirection, -Vector3.up, sagAngle / maxSagAngleDeg * sagBlend)
```

#### Junction Stress

When a child branch is very heavy relative to its attachment node:
- `ApplyDamage(parent, DamageType.JunctionStress, stressDamage)` each spring
- New `DamageType.JunctionStress` in `NodeHealth.cs`

#### Prop Supports (optional)

Player places a stick prop under a drooping branch → `hasPropSupport = true`;
sag calculation skips those nodes.

**Scope:** `TreeNode.cs`, `TreeSkeleton.cs`, `NodeHealth.cs`, species ScriptableObject
**Dependencies:** Species system (item 25)

</details>

---

<details>
<summary><strong>Air Layer Root Continued Growth</strong></summary>

**Goal:** Air layer roots (above-ground roots on the trunk) should keep growing after
confirmation — currently they stop at the rock surface.

**Behaviour wanted:**
- Air layer root tips continue extending each spring like any other root terminal
- Growth direction: gravity-biased downward
- If a rock or convex collider is nearby, roots deflect toward and along its surface
  (same rock-following logic as Ishitsuki, but running live each season)
- Once a root tip reaches the soil plane, it transitions to normal subsurface root behaviour

**Scope:** `TreeSkeleton.cs` (`StartNewGrowingSeason` root growth pass — relax the
`isAirLayerRoot` exclusion that currently halts their growth), `ContinuationDirection`
(add rock-proximity deflection for air layer nodes)

</details>

---

<details>
<summary><strong>Soil / Substrate System</strong> → scheduled as item 27</summary>

**Goal:** Different soil mixes with distinct properties that interact differently with
each tree species.

#### Substrate Components

| Component | Moisture Retention | Drainage | Nutrients | Notes |
|---|---|---|---|---|
| Akadama | High | Medium | Medium | Breaks down over time |
| Pumice | Low | Very High | Low | Structural; doesn't compact |
| Lava rock | Low | High | Low | Maximum aeration |
| Organic compost | Very High | Low | High | Risk of waterlog + root rot |
| Sand | Very Low | High | None | Fast drainage |
| Kanuma | High | Medium | Acidic | For azaleas |

#### Derived Properties

```
waterRetention  = weighted average of component retention values
drainageRate    = weighted average of component drainage values
nutrientLevel   = weighted average × organic fraction
aerationScore   = inverse of water retention
```

#### Soil Degradation

- Each growing season: `degradation += component.degradeRate`
- At high degradation: `drainageRate` drops, `waterRetention` rises
- **Repotting resets degradation**

#### Repotting

1. Player enters `RootPrune` mode
2. New **Repot** button opens a substrate mixing UI (sliders, sum locked to 100%)
3. On confirm: new mix applied, degradation reset
4. `ApplyDamage(DamageType.RepotStress)` on all root terminals — recovers over one season
5. Repotting outside early spring → larger stress hit

#### Waterlogging & Root Rot

If `drainageRate` is too low and watering too frequent:
- `saturationLevel` accumulates
- Above threshold: `ApplyDamage(DamageType.RootRot, rotRate)` on root nodes per season
- No cure except repotting + removing rotted roots

**Scope:** New `PotSoil.cs`, `GameManager.cs` (seasonal drain + degradation), `TreeInteraction.cs`
(repot action), `buttonClicker.cs` + `ButtonUI.uxml` (Repot button, substrate mixing UI),
`LeafManager.cs` (nutrient-driven colour influence)

</details>

---

<details>
<summary><strong>Tree Death</strong></summary>

**Goal:** Trees can die permanently if neglected or abused.

**Death conditions:**
- Average health across all trunk nodes < 0.05 for 2+ consecutive seasons
- Total living root mass drops below critical minimum
- Prolonged drought at moisture 0
- Multiple simultaneous stressors

**Death sequence:**
1. Warning signs over 1–2 seasons: leaves brown, growth stops, bark greys
2. Point of no return: UI warning ("This tree is dying — act now or lose it")
3. If uncorrected: all leaves drop, mesh goes grey/brown
4. Dead tree remains as memorial
5. Player can start new tree or load save

**Recovery window:** Between warning and death, aggressive intervention can save
the tree — success depends on how far gone it is.

**Scope:** `TreeSkeleton.cs`, `GameManager.cs`, UI (warning overlays, death screen),
`NodeHealth.cs`

</details>

---

<details>
<summary><strong>Branch Death & Dieback</strong></summary>

**Goal:** Individual branches can die and either fall off or remain as deadwood.
Deadwood management (jin, shari) is a real bonsai technique.

**Branch death causes:**
- Health reaches 0 on a node
- Shading: interior branches receiving no light for 2+ seasons die back
- Overextension: branch too long without ramification weakens at the base

**What happens when a branch dies:**
- **Light branches (small radius):** Fall off after 1–2 seasons. Parent gets a small wound.
- **Heavy branches (large radius):** Remain as deadwood. Can be trimmed away or preserved as jin.
  If left too long, may harbour disease (fungus system interaction).
- **Species variation:** Junipers/pines retain deadwood; deciduous species drop faster.

**Jin / Shari hook:**
- Deliberately kill a branch tip and strip bark → jin (deadwood point)
- Shari: strip bark along trunk → deadwood stripe
- Aesthetic/competition scoring only

**Scope:** `TreeNode.cs`, `TreeSkeleton.cs`, `TreeMeshBuilder.cs`, species ScriptableObject

</details>

---

<details>
<summary><strong>Gamification & Tutorial Progression</strong></summary>

**Goal:** Stagger tools and mechanics so the player learns one system at a time.

**Progression tiers (draft):**

| Tier | Unlocks | Trigger |
|---|---|---|
| 1 — Seedling | Watering, time controls, camera | Game start |
| 2 — First Cut | Trim tool, wound basics | Tree reaches depth 4 |
| 3 — Shaping | Wire tool, wire removal | First successful trim |
| 4 — Roots | Root prune mode, root planting | First wire set + removed cleanly |
| 5 — Refinement | Pinching, defoliation, leaf management | Tree survives 3 years |
| 6 — Soil Science | Repotting, soil mix, fertilizer | First repot prompt |
| 7 — Advanced | Air layering, Ishitsuki, multi-tree | Species mastery milestone |
| 8 — Master | All tools, competition mode | Complete a styled tree |

**Tutorial delivery:**
- Contextual prompts when a new situation arises
- Optional practice challenges
- No forced tutorials — tools unlock regardless
- In-game Journal/Encyclopedia

**Scoring / achievements:**
- Tree health score (rolling average)
- Style points (taper, ramification, proportion — algorithmic)
- Survival milestones (5, 10, 25 years)
- Technique badges

**Scope:** `ProgressionManager.cs`, `TutorialSystem.cs`, UI overlays, `GameManager.cs`,
save/load integration

</details>

---

<details>
<summary><strong>Decoration System</strong></summary>

**Goal:** Cosmetic elements placed on soil surface, pot rim, rock, or around tree base.

**Decoration types:**

| Type | Placement | Behaviour |
|---|---|---|
| Moss | Soil surface, rock surface | Grows/spreads slowly; needs moisture |
| Grass tufts | Soil surface, rock crevices | Seasonal; green spring/summer, brown autumn |
| Accent rocks | Soil surface | Static |
| Figurines | Anywhere on soil/rock | Static (traditional accent pieces) |
| Fallen leaves | Soil surface | Seasonal scatter in autumn; auto-cleared in spring |
| Deadwood pieces | Soil surface | Decorative driftwood |

**Placement system:**
- Player enters decoration mode (new game state)
- Click to place on valid surfaces; drag to reposition; right-click to remove
- Snapping to surface normals

**Moss (living decoration):**
- Spreads slowly each season if moisture adequate; dies if soil dries out frequently
- Player can trim/remove
- Moss coverage contributes to moisture retention

**Scope:** `DecorationManager.cs`, `Decoration.cs`, `MossDecoration.cs`, `GameState.Decorate`,
prefabs, UI palette, save/load integration

</details>

---

<details>
<summary><strong>Air Layering & Branch-to-New-Tree</strong></summary>

**Goal:** Player can air layer a branch, then sever it and start a brand new tree.

**Air layering process:**
1. **Select branch point:** Click a healthy node with adequate radius
2. **Apply wrap:** Visual wrap appears; air layer timer begins
3. **Root development (1–2 seasons):** Small roots appear; grow stronger each season
4. **Separation:** When roots sufficient, UI prompt: "Air layer roots strong enough. Cut now?"
5. **Sever:** Current state auto-saved; branch becomes new independent tree in fresh pot;
   original tree continues with wound at cut site

**New tree from air layer:**
- Inherits branch structure, wire state, health, refinement of source branch
- Starts with air layer roots as its initial root system

**Scope:** `TreeSkeleton.cs` (air layer state, root development, separation logic),
`TreeNode.cs` (air layer fields), `TreeInteraction.cs`, `SaveManager.cs` (clone subtree),
`GameManager.cs` (new tree session), `AirLayerTool` game state, separation prompt UI

</details>

---

<details>
<summary><strong>Steam / System Achievements</strong></summary>

**Goal:** Award achievements through Steamworks (or OS notifications on non-Steam builds) for meaningful milestones.

**Integration approach:**
- Wrap Steamworks.NET (or Facepunch.Steamworks) behind a thin `AchievementManager.cs` singleton
- On non-Steam builds, fall back to a local `PlayerPrefs` unlock log + optional in-game toast
- Call `AchievementManager.Unlock("ID")` from the relevant system; the manager deduplicates

**Draft achievement list:**

| ID | Name | Trigger |
|---|---|---|
| `FIRST_TRIM` | First Cut | Player trims for the first time |
| `FIRST_WIRE` | In Training | Wire fully set and removed cleanly |
| `SURVIVE_5` | Five Candles | Tree survives 5 in-game years |
| `SURVIVE_10` | Decade | Tree survives 10 years |
| `SURVIVE_25` | Ancient | Tree survives 25 years |
| `FIRST_REPOT` | Root Bound | Complete a repot |
| `ISHITSUKI` | Stone & Root | Place tree on a rock (Ishitsuki confirmed) |
| `AIR_LAYER` | New Life | Successfully sever an air layer |
| `MYCORRHIZAL` | Fungal Network | Achieve mycorrhizal coverage on 50 % of roots |
| `HEALTHY_DECADE` | Thriving | Maintain avg health > 80 % for 5 consecutive seasons |
| `ALL_SPECIES` | Collector | Grow every available species at least once |
| `FIRST_DEATH` | Lessons Learned | Let a tree die (unlocks on death screen) |

**Scope:** `AchievementManager.cs` (new), hook calls in `TreeSkeleton.cs`, `buttonClicker.cs`, `SaveManager.cs`; Steamworks package import

</details>

---

<details>
<summary><strong>Camera & Input</strong></summary>

- Full Unity Input System migration (currently legacy `Input.*`)
- Wire animation skip key revisited as part of this

</details>

---

<details>
<summary><strong>Multi-Tree Planting</strong></summary>

- Plant two trees in the same pot
- Root/branch collision and fusion mechanics
- Shared health/soil system

</details>

---

<details>
<summary><strong>Quick-Start / Auto-Generate</strong></summary>

- Auto-simulate ~1 year of growth in background
- Present 10 variation options; player picks one (or multiples) to start with
- Foundation: needs stable multi-year simulation first

</details>

---

<details>
<summary><strong>Sibling Branch Fusion (Graft Bridging)</strong></summary>

**Goal:** When two sibling branches (same parent) grow close enough to touch, they gradually callus together and eventually fuse into one wireable unit — mimicking natural inosculation and enabling Clump Style bonsai.

**Detection (cheap — runs once per spring):**
- For each parent node, check all pairs of its direct children
- If `distance(tipA, tipB) < radiusA + radiusB`, start a `FusionBond` (stored on `TreeSkeleton`)
- `FusionBond`: `{ nodeIdA, nodeIdB, float progress 0→1, bool isComplete, bool wireRestarted }`
- Progress increments each spring by a rate scaled by `SeasonalGrowthRate`; reaches 1.0 after ~3–5 growing seasons of contact

**Visual bridge (ITNTCE):**
- In `TreeMeshBuilder`, for each bond with `progress > 0`, extrude a short flattened connecting cylinder between the two nearest surface points
- Cross-section scales from 0→full radius as `progress` goes 0→1
- Uses the same bark vertex color as surrounding wood

**Fusion completion (`progress >= 1.0`):**
- `isComplete = true`; the two nodes are treated as a single wireable unit
- `TreeInteraction` click on either node selects both; wire direction applied to both simultaneously
- Wire removal on one removes both; placing wire restarts `progress = 0` (bridge "breaks" then re-fuses with `wireRestarted = true` for faster re-fusion, ~1 season)

**Wiring implications:**
- Wire data (`wireTargetDirection`, `wireSetProgress`, etc.) duplicated across both nodes in the pair
- `SeverAirLayer` and `TrimNode` on one node of a complete bond also removes the bond

**Serialization:** `FusionBond` list added to `SaveData` and `TreeSkeleton`

**Future:** Non-sibling fusion (cross-tree, clump style) deferred to a separate backlog item after this is stable.

**Scope:** `TreeNode.cs` (bond ref), `TreeSkeleton.cs` (bond detection + progress), `TreeMeshBuilder.cs` (bridge geometry), `TreeInteraction.cs` (unified selection), `SaveData`/`SaveManager.cs`

</details>

---

<details>
<summary><strong>Autosave System</strong></summary>

**Current bug:** `TreeSkeleton.cs` calls `SaveManager.Save()` at the end of every growing season (TimeGo → bud set). `Save()` silently returns false when no `ActiveSlotId` is set — so unsaved new games never get written, and no feedback is given to the player.

**Goals:**
- Autosave fires at meaningful moments without interrupting the player
- Unsaved games (no slot yet) get an autosave slot created automatically with a sensible default name
- Player always knows the save happened (brief non-blocking toast)

**Autosave triggers:**
- End of every growing season (existing hook in `TreeSkeleton.cs`)
- After a repot
- After an air layer is severed (already calls `SaveOriginal`, but the new tree should also get an autosave slot immediately)
- After confirming Ishitsuki rock orientation

**No-slot behaviour:**
- If `ActiveSlotId == null`, auto-create a slot with `slotId = NewSlotId()` and `saveName = "{SpeciesName} {Year} (autosave)"`
- Set it as the active slot — the player can rename it later from the load menu (future: editable names)
- This means the first season end always produces a save, even on a brand-new game

**Toast notification:**
- A `SaveToastLabel` in the HUD (small, bottom-centre, above the selection bar area)
- Fades in, holds 2 s, fades out — driven by a coroutine in `buttonClicker.cs`
- Text: `"Autosaved"` for automatic saves, `"Saved"` for manual saves (reuse same toast for both)
- Replaces the current `SaveStatusLabel` in the pause menu (which the player rarely sees)

**Screenshot on autosave:**
- Same `TakeScreenshotForSlot` coroutine as manual saves — fires after autosave writes

**Scope:** `TreeSkeleton.cs` (autosave call → pass to buttonClicker event or call SaveManager directly with slot-creation fallback), `SaveManager.cs` (`AutoSave()` helper that creates a slot if needed), `buttonClicker.cs` (toast coroutine, wire up autosave feedback), `ButtonUI.uxml` (SaveToastLabel)

</details>

---

<details>
<summary><strong>Root Containment Fix</strong></summary>

**Issue:** Ground roots frequently escape the pot boundary — growing out the sides, deep through the bottom, and occasionally far above soil. Out-the-top is acceptable (surface roots are realistic); lateral and downward escapes are not.

**Existing system:** `rootAreaTransform` six-face box deflection + `DeflectFromRootAreaWalls` in `ContinuationDirection`. The deflection is applied but roots still escape.

**Proposed fixes:**
1. **Hard clamp at spawn:** In `SpawnChildren` for root nodes, if `tipPosition` is already outside the boundary box (minus a small margin for top face), skip spawning — don't add the node at all
2. **Stronger wall deflection:** Increase the deflection blend weight for side/bottom faces vs the top face; top face gets a weaker deflection (allow slight emergence)
3. **Terminal clamp:** If an existing root terminal is outside the boundary, mark it `isTrimmed` so it stops growing and doesn't seed new children

**Top-face exception:** Roots within a small vertical band above the soil plane (`0 to +topEmergenceMargin`) are left alone — surface radial roots are realistic and look good.

**Scope:** `TreeSkeleton.cs` (`SpawnChildren`, `DeflectFromRootAreaWalls`, possibly `StartNewGrowingSeason`)

</details>

---

<details>
<summary><strong>Branch Saw System</strong></summary>

**Goal:** Thick branches require a sawing action to remove rather than a single click, making large cuts feel weighty and deliberate.

**Threshold:** When the player clicks a branch in Trim mode and `node.radius >= sawRadiusThreshold` (inspector-tunable, default ~0.08), instead of immediately trimming, enter a **Saw sub-state**.

**Input mechanic:**
- The branch highlights with a saw-cut line across it
- Player must do repeated left↔right mouse/stick input across the cut line
- Each full back-and-forth stroke increments `sawProgress` (0→1); ~4–6 strokes to complete
- Visual: a deepening cut groove appears on the mesh at the cut face as `sawProgress` advances
- If the player clicks elsewhere or presses Escape mid-saw, `sawProgress` resets and the mode cancels

**Completion:**
- When `sawProgress >= 1.0`, the branch severs — same `TrimNode` call as a normal cut, same wound/undo system
- Play a crack/snap SFX at completion
- A few seasons later the wound calluses as normal

**Implementation notes:**
- New `GameState.Sawing` or a sub-mode flag on `ToolManager` — lean toward a flag to avoid a full state machine addition
- The saw target node stored on `TreeInteraction` for the duration
- Groove visual: a flat ring mesh (like the wound torus, but a slit) scaled by `sawProgress`; destroyed on completion or cancel

**Scope:** `TreeInteraction.cs`, `TreeSkeleton.cs` (threshold field), `TreeMeshBuilder.cs` (groove mesh), `buttonClicker.cs`/`ButtonUI.uxml` (progress indicator), `AudioManager` (SFX)

</details>

---

<details>
<summary><strong>Rock Placement — Confirm/Cancel Only</strong></summary>

**Issue:** During `GameState.RockPlace` and `GameState.TreeOrient`, all normal tool buttons (Trim, Wire, Water, etc.) remain visible and clickable. Only two actions should be available.

**Desired behaviour:**
- On entering `RockPlace`: hide all buttons except **Confirm** (already toggled in) and a new **Cancel** button
- Cancel undoes all changes made since the player pressed Place Rock — restores the tree's pre-lift position/orientation and exits back to the previous state
- On entering `TreeOrient`: same two buttons remain; Confirm advances to the next step (already works); Cancel still undoes everything back to pre-RockPlace

**Implementation:**
- Capture tree transform (position, rotation) and `plantingSurfacePoint`/`plantingNormal` when entering `RockPlace`
- Store as `rockPlaceCancelState` on `GameManager` or `TreeSkeleton`
- Cancel button calls a new `GameManager.CancelRockPlace()` which restores the snapshot and calls `ToggleRootPrune()` to lower the tree
- In `OnGameStateChanged`, when `inRockPlace || inTreeOrient`: hide all HUD buttons except Confirm; show Cancel button
- Cancel button added to UXML alongside Confirm

**Scope:** `GameManager.cs`, `TreeSkeleton.cs` (snapshot), `buttonClicker.cs`, `ButtonUI.uxml`

</details>

---

## Reference

<details>
<summary><strong>Ishitsuki Root Selection — How It Works</strong></summary>

> This section explains what happens to existing roots when Ishitsuki is confirmed,
> for reference when designing future Ishitsuki features.

**What the code does at confirm time (`SpawnTrainingWires`):**

1. Every direct child of `root` that is `isRoot=true` is a **trunk-root startNode** —
   the first segment of each root cable. These are the only root nodes that survive confirm.

2. Every node below those startNodes is **deleted** via `RemoveSubtree`.

3. `PreGrowRootsToSoil` then drapes a fresh `isTrainingWire=true` chain from each
   startNode's tip, down the rock face, to the soil.

**Implication for the player:**

- The **number of Ishitsuki strands = the number of trunk-root startNodes at confirm time**.
  If the tree auto-planted 6 trunk roots before confirm, there will be 6 strands.
- The **direction** each cable drapes is determined by the startNode's `growDirection`.
- All the deeper, branching organic root growth before confirm is discarded.

**Future design note:**
If we want pre-confirm root work to matter more:
- Let startNode direction be influenced by where the organic root was growing
- Preserve the top N segments of each organic root chain as the cable's initial part

</details>

---

## Completed Items Log

All systems built across all phases:

- Procedural branch skeleton + mesh builder (`TreeSkeleton`, `TreeMeshBuilder`)
- Wire placement + bend (`TreeInteraction`, `WireRenderer`)
- Trim subtree + highlight mesh
- Leaf lifecycle: spring spawn, autumn colour gradient, stochastic fall, fall animation (`LeafManager`, `Leaf`)
- Seasonal time system + game state machine (`GameManager`)
- Camera orbit, zoom, Y-pan (`CameraOrbit`)
- Tight-angle geometry: bend rings + parallel-transport frame fix (`TreeMeshBuilder`)
- Post-trim depth cap: cut point tracking + per-season regrowth limit (`TreeSkeleton`, `TreeNode`)
- Wire rework: instant snap + spring animation, `WireAnimate` state (`TreeInteraction`, `GameManager`)
- Health system foundation: `health` on `TreeNode`, `DamageType` enum, `ApplyDamage`, health-gated growth (`TreeSkeleton`, `NodeHealth.cs`)
- Wire colour progression: silver → gold pulse → orange → red (`WireRenderer`)
- Root system: `isRoot` flag, gravity-biased growth, `PlantRoot`, `RootPrune` state, lift animation, soil-plane interaction, pitch relaxation
- Bud system: terminal buds set in August, bud GOs spawned/destroyed, back-budding on trim, `backBudActivationBoost`, old-wood bud chance
- Wound system: half-torus wound visualization, health drain per season, cut paste tool, `pasteApplied` tint, subdivision-cut detection, cleanup on subtree removal
- Growth stability: `maxBranchNodes` hard cap, `vigorFactor` scaling lateral/back-bud chances; fixed `InvalidOperationException` in back-budding loop
- Root Area box containment: `rootAreaTransform` reference; `RootDistRatio` checks XZ walls + Y floor/ceiling; `DeflectFromRootAreaWalls` handles all six faces
- Pot-bound root system: `boundaryPressure` counter, thickening over seasons, `boundaryGrowthScale`, `wallSegmentScale`, `potBoundInnerBoost`, fill-in lateral budget
- Ishitsuki (root-over-rock): `RockPlace` + `TreeOrient` states, rock grab/rotate/confirm, training wires, drape over rock via `PreGrowRootsToSoil`, `IshitsukiWire.cs`
- Trim trauma: `DamageType.TrimTrauma`, seasonal recovery scaled by `treeEnergy`
- Trim undo: 5-second real-time window; `TrimUndoState` captures full subtree; `Ctrl+Z` restores tree; countdown label in UI
- Health ring GL debug: `OnRenderObject()` draws green→yellow→red rings at each node
- Bud/leaf integration: `birthYear` on `TreeNode`; leaves only from buds on old wood
- Save / Load System: full JSON via `JsonUtility`; `SaveData`, `SaveNode`; auto-save on season end; manual save in Settings
- Root Area box containment + pot-bound pressure system
- Auto-water: fires before drought threshold; Debug tab toggle; Water button flash; in-game-day cooldown
- Ishitsuki white first segment fix: `startNode` marked `isTrainingWire=true`
- Ishitsuki roots continue underground: soil-entry node spawned at soil contact point
- Ishitsuki cable growth animation: `PreGrowRootsToSoil(animated:true)` places one segment per spring
- Ghost root fix: `RemoveSubtree` called on old chains before re-draping at confirm
- Camera jump fix: `OnGameStateChanged` clears `isDragging`/`isPanning`; `lastTargetPosition` delta compensation
- Fertilizer System: `nutrientReserve` drain/boost, winter block, growth multiplier, FertilizerBurn, auto-fertilize
- Weed System: RMB pull mechanic, rip chance, 4 weed types, WeedManager + WeedPuller, Herbicide button
- Fungus System: `fungalLoad`, seasonal spread + damage, mycorrhizal network, Fungicide button, leaf tint
