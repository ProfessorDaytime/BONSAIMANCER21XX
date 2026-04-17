# BONSAIMANCER — Development Plan

Last updated: 2026-04-16 · Items 1–34 complete + Branch Saw + Bark Shader System; priority queue items 1–15 done

---

## Priority Queue

Ordered by current priority. Work top-to-bottom.

1. ✓ **Rock Placement — Lock UI + Cancel + Camera-Relative Controls** *(backlog)*
2. ✓ **New Input System Migration**
3. ✓ **Growth Season Taper** *(item 34)*
4. ✓ **Roots → bark color over time** *(backlog)*
5. ✓ **Branch Saw**
6. ✓ **Pot & Rock Size Selection** *(backlog)*
7. ✓ **Repot Root Raking Mini-Game** *(backlog)*
8. ✓ **UI Cycle Toggle + Tree Health Stats + Active Tool in Calendar** — single `◉` button cycles Tools → Stats → Neither; stats panel shows all health values live; tool name appended to calendar TMP
9. ✓ **Confirm/Cancel Visibility** — hidden by default; shown only in RockPlace/TreeOrient; styled to match pause button
10. ✓ **Time Speed Toggle Button** — a small button above the `◉` cycle button, same size as Pause. Cycles between two speeds: *Fast* (TIMESCALE=200, current default) and *Slow* (1 game-hour = 2 real seconds, i.e. TIMESCALE≈1/120). Button shows current state (`▶▶` fast / `▶` slow); amber tint in slow mode. **Auto-trigger:** when the calendar enters January (`month == 1`), automatically switch to Slow so the player has comfortable trimming time. Does not auto-switch back — player controls that manually.
    - **Scope:** `GameManager.cs` (constants, auto-trigger in `SetMonthText`), `buttonClicker.cs` (button field, click handler, Q wiring), `ButtonUI.uxml` (new button above `◉`)
11. ✓ **Realistic Winter Pruning** — decouple dormancy from growth cap so heavy winter cuts don't create a "no growth for a year" deadlock. Four changes:
    - **Growth window lock:** `regrowthSeasonCount` only increments during growing seasons (Mar–Aug). Winter cuts sit dormant until spring, then begin recovering at normal rate. Zero code impact outside `StartNewGrowingSeason`. *(Suggestion 2 — lowest risk, implement first)*
    - **Forced dormant skip:** If a cut is made in winter (month 11–2) AND `trimCutDepth` is deep (> configurable threshold, e.g. depth 4), set `regrowthSeasonCount = 2` at the moment of cut so the tree skips winter + has a slow spring start. *(Suggestion 1)*
    - **Reserve depletion:** Track total `cutDepthThisSeason` on `TreeSkeleton`; if it exceeds `heavyPruneReserveThreshold`, multiply next spring's `growthSeasonMult` by `heavyPruneRecoveryScale` (default 0.5) for one season. Reset after spring. *(Suggestion 3)*
    - **Severity-scaled regrowth rate:** In `CutPointDepthCap`, when `severity > 0.8` (heavily pruned, ratio of cut depth to tree depth), scale `depthsPerYear` by 0.7 for that node's first recovery season. *(Suggestion 4)*
    - **Scope:** `TreeSkeleton.cs` (`StartNewGrowingSeason`, `TrimNode`, `CutPointDepthCap`, `GrowthSeasonMult`), `TreeNode.cs` (no new fields needed), `TreeSpecies.cs` (optional threshold fields)
12. ✓ **First-Use Tooltip System** — reuse the existing `TipPause` / tooltip overlay mechanism to pop up contextual tooltips the first time the player clicks each tool button. Each tooltip is a short text explaining what the tool does and a basic tip. Player must click X or press ESC to dismiss (same as current TipPause). After first dismissal, that tool's tooltip never shows again (track shown-set in PlayerPrefs or a `HashSet<string>` serialized to SaveData). Tooltips can also be triggered programmatically (e.g. first repot, first wiring session, first winter).
    - **Scope:** `GameManager.cs` (new `ShowTooltip(string title, string body)` method, enters TipPause), `buttonClicker.cs` (intercept first-click per tool button, call ShowTooltip), `ButtonUI.uxml` (add title label to tooltip overlay if not already present), `SaveData` (add `shownTooltips` string list)
13. ✓ **Graft / Sibling Branch Fusion** — Approach graft (inarizashi): two-click tool selects source terminal + target node; over `graftSeasonsToFuse` (default 2) growing seasons the source tip's direction bends toward the target; on fusion a bridge node is created spanning the gap. Amber GL line shows in-progress grafts; pale green circle marks pending source. ESC/RMB cancels selection. Failed if either node dies before fusion.
14. ✓ **Species Visuals** — per-species bark colors (`youngBarkColor`, `matureBarkColor`, `rootNewGrowthColor`) pushed to shader `_NGColor`/`_BarkColor`/`_NGRootColor` via `TreeMeshBuilder.ApplySpeciesColors()`; per-species `leafSpringColor` replaces hardcoded green in `Leaf.cs`; `LeafManager` passes color from `species` on spawn; all fields have sensible defaults so existing `.asset` files work without edits
15. ✓ **Bark Shader System** — 100% procedural HLSL replacing all texture lookups. 10 botanical bark patterns (smooth, fine fissures, interlacing, vertical strips, irregular blocks, large plates, peeling strips, fibrous shreds, spongy, lenticels) driven by inlined SimpleNoise/GradNoise/Voronoi. Layered blend: vertex.a fades Type-2 fine fissures (twigs) → species bark type (mature wood). 3-band cel-shaded lighting (shadow/mid/lit thresholds) + backface-inflate silhouette outline pass. Wound face embedded in unified mesh as organic callus geometry (`AddWoundCap`): swell ring + closing ring + concave center, progress-driven by `healProgress`. vertex.g = wound intensity, vertex.b = paste mask. BarkFlakerManager spawns 3D peeling meshes on trunk/scaffold for barkType 10/12/14, count = f(age, health). All 17 species `.asset` files updated with `barkType` + color values.
16. **Gamification & Tutorial Progression** *(backlog)*
16. **Multi-Tree / Quick-Start** *(item 26)*
17. **Decoration System** *(backlog)*

---

## Completed This Phase

- **Phototropism coordinate space fix** ✓ — `SunDirection()` in `TreeSkeleton` was returning world `Vector3.up`; `growDirection` is tree-local, so Slerping the two produced wrong results when the tree was tilted on a rock. Fixed to `transform.InverseTransformDirection(Vector3.up)`.
- **PinchNode bud-system fix (critical)** ✓ — `PinchNode()` was setting `node.isTrimmed = true`, which excluded the node from autumn `SetBuds()`. With `budSystemActive = true` in year 2+, only `hasBud` nodes can spawn — so all pinched nodes were permanently frozen with no path back. Fix: removed `isTrimmed = true`; use `node.length = node.targetLength` instead to halt extension while keeping the node alive for the bud cycle. Growth resumes next spring via normal bud break.
- **Defoliate hover fix** ✓ — `HandleDefoliateHover` required `n.isTerminal`, but by June most leaf-bearing nodes have already branched and are non-terminal. Removed the `isTerminal` filter; now targets any non-root, non-trimmed node that has a leaf cluster.
- **Root health NaN fix** ✓ — `RecalculateRootHealthScore()` was dividing `com /= totalRadius` when all root nodes had zero radius. Result: NaN → `Mathf.RoundToInt(NaN)` = `int.MinValue` = -2147483648 in the UI. Added `|| totalRadius <= 0f` to the early-return guard. Added NaN/Infinity display guard in `buttonClicker.UpdateRootHealthDisplay`.
- **3-speed time mode** ✓ — `SpeedMode` enum (Slow=0.5, Med=10, Fast=200 hrs/s) replaces the bool `IsSlowSpeed`. `ToggleSpeed()` cycles Slow→Med→Fast→Slow. Speed button shows ▶/▶▶/▶▶▶ with amber/grey/green tints. Auto-slow trigger moved from June to **April** (June is too late — growth is already done; April is the pinching window). `IsSlowSpeed` kept as back-compat property.
- **`OnMonthChanged` event** ✓ — `static event Action<int>` on `GameManager`, fired at end of `SetMonthText`. Drives month-triggered tutorials (April ramification) without polling.
- **`OnWireSetGold` event** ✓ — `public event Action` on `TreeSkeleton`, fired the first frame `wireSetProgress` crosses from <1 to ≥1 for any node. Unwire tooltip is now event-driven (fires on first gold wire) rather than button-click-driven.
- **April ramification tutorial** ✓ — `OnMonthChanged(4)` in `buttonClicker` triggers `MaybeShowTooltip("april_ramification", ...)` explaining the pinch window, auxin suppression, and back-budding.
- **Fertilizer tutorial** ✓ — `MaybeShowTooltip("fertilize", ...)` added to `OnFertilizeButtonClick`, covering seasonal timing and why the button dims Nov–Feb.
- **Herbicide tutorial** ✓ — `MaybeShowTooltip("herbicide", ...)` added to `OnHerbicideButtonClick`, covering nutrient competition and moss suppression.
- **Pinch visual indicators** ✓ — `DrawPinchMarkers` in `TreeInteraction` (registered to `endCameraRendering`) draws a GL octahedron at every pinchable tip when the Pinch tool is active: dim lime (r=0.055) for all eligible tips, bright lime (r=0.12) for the hovered tip. `hoveredPinchNode` cleared at top of `Update` each frame. `DrawGLDiamond` static helper draws a camera-facing 3-axis diamond.
- **Scale debug cubes** ✓ — `ScaleDebugger.cs` component draws GL wireframe 1×1×1 m cubes in `OnRenderObject`, centred on a `gridRadius×2+1` grid at `plantingSurfacePoint.y`. Toggled from Debug tab (`ToggleScaleCubes`).
- **UI button layout** ✓ — All tool buttons except Trim/Water reduced to 50px height. Fertilize/Herbicide/Fungicide group moved from `top:279` to `top:204` (immediately below Graft, no gap). Confirm/Cancel orient buttons changed from right-column stacked to centered row (`left:0; right:0; flex-direction:row; justify-content:center`) at `top:340px`.

- **22.** Fertilizer System — `nutrientReserve` (0→2) drains 0.4/season; `Fertilize()` blocked in winter; `nutrientMult` Lerp(0.6,1.4) multiplied into per-frame growth speed; FertilizerBurn on roots if reserve >1.5 at spring start; Fertilize button + nutrient bar right-side panel; Auto-fertilize toggle in Debug tab; serialized in SaveData
- **23.** Weed System — RMB click-hold-drag-up to pull; rip chance leaves stub (harder next pull, stump visual, 60% drain); Herbicide button clears all weeds + sets aeration penalty next season; WeedManager spawns procedural cube weeds (grass/clover/dandelion/thistle) as tree children; seasonal nutrient+moisture drain; weeds serialized in SaveData
- **24.** Fungus System — `fungalLoad` (0–1) per node; spreads from open wounds / overwatered roots / low-health nodes; seasonal spread to neighbours; FungalInfection DamageType; Fungicide button reduces load 0.6 across all nodes; mycorrhizal network on healthy root nodes (3+ healthy seasons) reduces nutrient drain 20%; herbicide kills mycorrhizae; infected leaves tint toward sickly yellow-green via MaterialPropertyBlock; all fields serialized
- **25.** Species Skeleton — `TreeSpecies` ScriptableObject; `ApplySpecies()` copies into existing `TreeSkeleton` fields on Awake; `BudType` moved to own file; species name displayed in Settings menu header; Japanese Maple (Opposite buds, fast/thirsty/fragile) and Juniper (slow/drought-tolerant/resilient) as starter species
- **26.** Species Selection Menu — fullscreen overlay on game start; 16 species with Growth / Water / Care / Soil chip tags; sortable by any tag; confirms into TipPause with species applied; `SpeciesSelect` GameState; ToolTip fixed to only show in TipPause without touching Main Camera
- **27.** Soil / Substrate System — `PotSoil` component; 7 substrates (akadama, pumice, lava rock, organic, sand, kanuma, perlite); weighted mix → derived properties; seasonal degradation + saturation + root rot; species soil mismatch penalty; `Repot()` with timing and too-soon stress multipliers; 4 presets; soil bars in Repot panel; 16 species .asset files with soil preferences; Roots→Repot rename; weeds auto-cleared on entering Repot mode; weeds excluded from trunk radius and rock surfaces
- **Pause Menu** ✓ — `GameState.GamePause`, `TogglePause()`, pause overlay in `buttonClicker.cs`
- **Autosave** ✓ — `SaveManager.AutoSave()` creates slot on first save, fires end-of-season
- **debugSoilY sentinel** ✓ — `-9999` sentinel auto-populates from `plantingSurfacePoint.y` on first use
- **Camera root-mode regression** ✓ — `lastTargetPosition` delta compensation, `isDragging` safety-clear, pitch clamp per state
- **Root containment** ✓ — hard clamp in `SpawnChildren`: skips spawn if tip is outside side/bottom of `rootAreaTransform` box; top-face emergence allowed
- **Rock Placement UI lock + Cancel** ✓ — HUD dims (opacity 0.25 + PickingMode.Ignore) during RockPlace/TreeOrient; Confirm/Cancel always visible; Cancel restores pre-placement snapshot; camera-relative tree translation
- **New Input System Migration** ✓ — all `Input.*` calls replaced with `Mouse.current` / `Keyboard.current`; EventSystem updated to `InputSystemUIInputModule`
- **Growth Season Taper (item 34)** ✓ — `GrowthSeasonMult()` in `TreeSkeleton`; `growthSlowDay`/`growthStopDay` on `TreeSpecies`; `dayOfYear` on `GameManager`; 16 species assets updated; `species == null` guard
- **Roots → bark color over time** ✓ — removed `isRoot && !isTrainingWire` exclusion from age accumulation loop; exposed roots bark 3× faster via `fadeDays/3` in `GrowthColor`
- **Branch Saw** ✓ — `sawRadiusThreshold` (0.08) on `TreeSkeleton`; Saw tool triggers multi-stroke mechanic for thick branches; direction-reversal half-stroke counting (10 half-strokes = done); dark annulus groove deepens toward center as progress advances; ESC/RMB cancels; completes via normal `TrimNode` path

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
<summary><strong>34. Growth Season Taper</strong></summary>

**Goal:** Primary extension growth stops in early-to-mid summer, matching real tree biology. Most temperate trees set buds and shift resources from extension to radial growth and energy storage once day length shortens past their threshold. The cutoff is species-specific and should feel gradual rather than snapping off.

**Science basis:**
- Spring flush uses pre-formed bud tissue and stored energy — fast and explosive
- Growth stop triggered primarily by shortening photoperiod, not temperature
- After the taper: radial growth (thickening) continues, but `isGrowing` extension stops
- Lammas (second flush) exists in some species but is not modelled here

**Approximate stop windows by species:**

| Species | Slow starts | Fully stopped |
|---|---|---|
| Juniper | ~May 20 (day 140) | ~Jun 15 (day 166) |
| Japanese Maple | ~Jun 1 (day 152) | ~Jul 1 (day 182) |
| Trident Maple | ~Jun 10 (day 161) | ~Jul 5 (day 186) |
| Ficus | ~Aug 1 (day 213) | ~Sep 1 (day 244) |
| Bald Cypress | ~Jun 15 (day 166) | ~Jul 15 (day 196) |

**New fields on `TreeSpecies` ScriptableObject:**
- `int growthSlowDay` — day of year growth begins tapering (default 150)
- `int growthStopDay` — day of year growth multiplier reaches zero (default 180)

**Implementation in `TreeSkeleton.cs`:**
```csharp
float GrowthSeasonMult()
{
    int day = GameManager.dayOfYear; // needs exposing or computing from month/day
    if (day < species.growthSlowDay) return 1f;
    if (day >= species.growthStopDay) return 0f;
    return 1f - Mathf.InverseLerp(species.growthSlowDay, species.growthStopDay, day);
}
```
Multiply `GrowthSeasonMult()` into the per-frame growth delta. When the multiplier reaches zero, `isGrowing` stays false and no new children are spawned for the rest of the season. Growth resumes next spring as normal.

**Note:** The "keep tips extending all season via `subdivisionsLeft = 1`" change made earlier is superseded by this. The taper naturally handles late-started branches — a branch that sprouted in May gets less time than one that sprouted in March, so they land at naturally different lengths without special-casing.

**Scope:** `TreeSpecies.cs` (two new fields), `TreeSkeleton.cs` (`GrowthSeasonMult()`, multiply into growth delta), `GameManager.cs` (expose `dayOfYear` as int), all 16 species `.asset` files (set slow/stop days)

</details>

---

<details>
<summary><strong>Species — Visuals</strong></summary>

Procedural geometry + hand-authored bark textures that transition between age stages
using a **pixel-perfect noise reveal** — no opacity blending, just a hard per-texel cutoff
that gradually exposes more pixels from the new tier. Geometry variation is fully procedural.

---

#### 0. Bark Texture System

**Design:** Each species has N bark texture tiers (suggested 3–4: seedling, young, mature, old).
Adjacent pairs don't need to be seamlessly blendable — each pixel is always 100% one tier or
the other. The transition looks like bark texture naturally spreading across the surface.

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

**Pixel-perfect noise transition** — instead of `lerp(texA, texB, blend)`, the shader
uses a noise value per texel compared against a threshold. Each texel is binary: fully tier A
or fully tier B. As `_BlendFactor` advances 0→1, more and more texels flip to tier B.

Two noise options (inspector-selectable per species):

| Mode | Pattern | Best for |
|---|---|---|
| **Scatter** | Per-texel value noise (hash of UV * resolution) | Random salt-and-pepper pixel spread |
| **Cellular** | Voronoi / cellular noise | Patches spreading across bark like aging spots or peeling |

```hlsl
// Inputs:
//   _BarkTexA, _BarkTexB  — the two adjacent tier textures
//   _BlendFactor          — 0=fully A, 1=fully B
//   _NoiseScale           — world-space pixels per noise cell (match your pixel art texel size)
//   _NoiseMode            — 0=scatter, 1=cellular
//   vertex.color          — tint overlay (growth color, fungal, deadwood, seasonal)

// Snap UVs to pixel grid so noise aligns exactly with texels
float2 texelUV = floor(uv * _TexelRes) / _TexelRes;

// --- Scatter mode: each texel independently random ---
float scatter = frac(sin(dot(texelUV, float2(127.1, 311.7))) * 43758.5453);

// --- Cellular mode: Voronoi distance to nearest cell center ---
float2 cell  = floor(texelUV * _NoiseScale);
float  minD  = 1.0;
for (int dy = -1; dy <= 1; dy++)
for (int dx = -1; dx <= 1; dx++) {
    float2 neighbor = cell + float2(dx, dy);
    float2 point    = neighbor + frac(sin(neighbor * float2(127.1, 311.7)) * 43758.5);
    minD = min(minD, length(texelUV * _NoiseScale - point));
}
float cellular = minD;  // 0 = at cell center, ~1 = at cell edge

float noise = lerp(scatter, cellular, _NoiseMode);

// Hard threshold — no blending, pixel is either A or B
half4 col = noise < _BlendFactor ? SAMPLE_TEXTURE2D(_BarkTexB, ...) : SAMPLE_TEXTURE2D(_BarkTexA, ...);
col *= IN.color;
```

**Key property:** Because this is a hard cutoff, pixel art pixels are never semi-transparent
or color-blended. A pixel at 60% through the transition is the same color as at 0% — it just
hasn't flipped yet. Adjacent tiers don't need to share a design language; the transition reads
as bark naturally changing rather than two textures dissolving together.

**`_TexelRes`** should match the pixel art resolution of the texture (e.g. 64 for a 64×64 sheet).
Set it on the material or as a global shader property.

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
1. Paint each bark tier as a seamless square pixel art texture (suggested 64×64 or 128×128)
2. Adjacent tiers do **not** need to share colors or pattern — the noise transition handles it
3. Import to Unity with Point (no filter) sampling to preserve hard pixels
4. Assign to `barkTiers[]` on the species `.asset`
5. Tune `barkTierAgeBreaks`, `barkVTiling`, `_NoiseScale`, and `_NoiseMode` in-engine

**Noise mode feel:**
- Scatter at small `_TexelRes` = static/grain, good for fine bark texture like maple
- Cellular at larger scale = patches spreading organically, good for chunky juniper or pine bark
- You can also bake a custom noise mask texture for species that need a very specific look

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
- `Custom/BarkBlend` shader — new shader replacing `Custom/BarkVertexColor`; dual texture sample with pixel-perfect noise threshold + vertex color tint multiply; scatter and cellular noise modes
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
<summary><strong>Calendar System — Real Month Lengths + Scheduling</strong></summary>

### Goal
Replace the fixed 28-day month with real calendar month lengths and add a monthly calendar panel where the player can schedule recurring care tasks (watering, fertilizing) with optional repeat modes and seasonal templates.

---

### Part 1 — Real Month Lengths

**Current:** every month = 28 days; `dayOfYear = (month-1)×28 + currentDay`.

**Change:** use biological month lengths. The in-game year starts in January of a fictional year (e.g. 2123), so leap-year logic is optional but can be included for accuracy.

```csharp
// On GameManager
static readonly int[] DaysInMonth = { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };

static bool IsLeapYear(int year) =>
    (year % 4 == 0) && (year % 100 != 0 || year % 400 == 0);

static int DaysInCurrentMonth(int month, int year) =>
    (month == 2 && IsLeapYear(year)) ? 29 : DaysInMonth[month - 1];
```

`dayOfYear` becomes a computed property that sums `DaysInMonth[0..month-2] + dayOfMonth`.

**What changes:**
- `CalculateTime()` — day rollover uses `DaysInCurrentMonth(month, year)` instead of `28`.
- `SetMonthText()` — month boundary fires when `dayOfMonth > DaysInCurrentMonth(...)`.
- `dayOfYear` property — recomputed from real cumulative day counts.
- `GrowthSeasonMult()` in `TreeSkeleton` — uses same `dayOfYear`; no change needed there since the property value is correct.
- Winter skip (`month == 11 → month = 2, year++`) — unchanged; just skip November + December as before.
- `OnMonthChanged` — unchanged.

**Migration:** existing saves store `month` (1–12) and the accumulated `hoursElapsed`. On load the `dayOfMonth` is inferred from `hoursElapsed % (DaysInCurrentMonth(month, year) * 24)`. No save format change required.

---

### Part 2 — Scheduled Event Data Model

```csharp
public enum ScheduledEventType { Water, Fertilize }

public enum RepeatMode { Once, EveryNDays, Weekly, Monthly, Yearly }

[Serializable]
public class ScheduledEvent
{
    public string           id;               // GUID, generated at creation
    public ScheduledEventType type;
    public int              month;            // 1–12; 0 = "every month"
    public int              day;              // 1–31
    public RepeatMode       repeat;
    public int              repeatIntervalDays; // used when repeat == EveryNDays
    // For Yearly: fires on ev.month + ev.day once per calendar year.
    // Useful for once-a-year fertilizer applications, repot reminders, etc.
    public bool             enabled;
}
```

`GameManager` holds `List<ScheduledEvent> schedule` (serialized into `SaveData`). At the end of each real day tick (when `dayOfMonth` increments), `CheckScheduledEvents()` fires:

```csharp
void CheckScheduledEvents()
{
    foreach (var ev in schedule)
    {
        if (!ev.enabled) continue;
        if (!EventFiresToday(ev, month, dayOfMonth, year)) continue;
        switch (ev.type)
        {
            case ScheduledEventType.Water:      skeleton?.Water(); break;
            case ScheduledEventType.Fertilize:  skeleton?.Fertilize(); break;
        }
    }
}

bool EventFiresToday(ScheduledEvent ev, int m, int d, int y)
{
    switch (ev.repeat)
    {
        case RepeatMode.Once:        return ev.month == m && ev.day == d;
        case RepeatMode.Monthly:     return ev.day == d;  // same day every month
        case RepeatMode.Weekly:      return dayOfYear % 7 == ev.day % 7;
        case RepeatMode.EveryNDays:  return dayOfYear % ev.repeatIntervalDays == ev.day % ev.repeatIntervalDays;
        case RepeatMode.Yearly:      return ev.month == m && ev.day == d;  // same month+day every year
        default: return false;
    }
}
```

Scheduled actions use the same `Water()` / `Fertilize()` methods as manual button presses — no special-casing, health guards, and winter blocks apply normally. The Water button flash / Fertilize button flash will still fire, giving visible feedback that the schedule fired.

---

### Part 3 — Calendar Panel UI

**Access:** clicking the date label (currently shows e.g. "April 1, 2123 09:02") opens the calendar. Alternatively, a small calendar icon next to the date.

**Layout:**

```
┌────────────────────────────────────┐
│  ←  April 2123  →                  │
│  Mo Tu We Th Fr Sa Su              │
│                  1  2  3           │
│   4  5  6  7  8  9 10             │
│  11 12 [13]14 15 16 17            │  ← today highlighted
│  18 19  20 21 22 23 24            │
│  25 26  27 28 29 30               │
│                                    │
│  ● Scheduled:  [+ Add]             │
│  💧 Water every 2 days             │
│  🌿 Fertilize monthly (1st)        │
│  ── Seasonal Templates ──          │
│  [Spring] [Summer] [Autumn]        │
└────────────────────────────────────┘
```

**Day cell:** 28–32 px square. Current day gets a gold highlight. Days with scheduled events show small coloured dots (blue = water, green = fertilize). Past days are dimmed.

**Month navigation:** `←` / `→` buttons step through months. Does not need to expose future months further than 12 months ahead.

**Add event flow:**
1. Click `[+ Add]` or click a day cell → opens a small event editor popup.
2. Select type: Water | Fertilize.
3. Set repeat: Once / Every N days / Weekly / Monthly / Yearly.
4. If "Every N days": number input (default 2).
5. Confirm → adds `ScheduledEvent` to `GameManager.schedule`.

**Delete:** each event row has a ✕ button on the right.

**Toggle:** each event row has an enable/disable checkbox so the player can pause a schedule without deleting it.

---

### Part 4 — Seasonal Templates

Pre-built schedules that can be applied in one click. Applying a template does **not** remove manually-added events — it merges, deduplicating by type+day+repeat.

| Template | Water | Fertilize |
|---|---|---|
| **Spring** (Mar–May) | Every 2 days | Monthly, 1st of month |
| **Summer** (Jun–Aug) | Every 1 day (heat/growth peak) | Every 4 weeks |
| **Autumn** (Sep–Oct) | Every 3 days | None (harden before winter) |
| **Winter** (Nov–Feb) | Every 5 days | None (dormant — Fertilize blocked anyway) |

Templates target the species' active season — a Ficus "Summer" template runs longer than a Juniper one. Implementation: templates are static `List<ScheduledEvent>` definitions; `ApplyTemplate(TemplateType)` merges them into `schedule`.

---

### Scope

| File | Change |
|---|---|
| `GameManager.cs` | `DaysInMonth[]`, `DaysInCurrentMonth()`, `IsLeapYear()`, `dayOfMonth` field, real-day rollover in `CalculateTime()`, `CheckScheduledEvents()`, `schedule` list, `AddScheduledEvent()` / `RemoveScheduledEvent()`, seasonal template builders |
| `SaveData.cs` | Add `scheduledEvents` list |
| `buttonClicker.cs` | Calendar panel open/close, month navigation, day-cell grid build, event list render, add/delete/toggle handlers, template buttons |
| `ButtonUI.uxml` | `CalendarOverlay` — date header, 7-col day grid, event list, add-event popup, template buttons; date label made clickable |

**Not in scope:** notification/reminder outside the game, scheduling trim/wire tasks (these require player decision-making, not automation), multi-year advance scheduling.

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
<summary><strong>Rock Placement — Lock UI + Cancel + Camera-Relative Controls</strong></summary>

**What's done:** `confirmOrientButton` toggles in during RockPlace/TreeOrient. `ToolManager.ClearTool()` fires on entering those states.

**What still needs doing:**

1. **Hide all other HUD buttons** during `RockPlace` and `TreeOrient` — currently Trim, Wire, Water, Repot, etc. remain visible and clickable, which corrupts the placement flow if pressed. In `OnGameStateChanged`, when `inRockPlace || inTreeOrient`, set every HUD button except Confirm and Cancel to `display: none`; restore them on exit.

2. **Cancel button** — alongside Confirm. Pressing it reverts everything to the moment before Place Rock was pressed:
   - Snapshot tree transform (position, rotation), `plantingSurfacePoint`, `plantingNormal`, and root-prune state when entering `RockPlace`
   - Store as `rockPlaceCancelSnapshot` on `TreeSkeleton` or `GameManager`
   - `CancelRockPlace()`: restore snapshot, call `ToggleRootPrune()` to lower the tree, return to pre-lift state
   - Cancel works from both `RockPlace` and `TreeOrient` — both revert all the way back

3. **Camera-relative controls** — dragging/rotating the tree on the rock currently uses world-space axes, so controls feel reversed when the camera is behind or to the side. Fix: project mouse delta onto the camera's right/forward vectors when computing drag translation and rotation in `TreeInteraction.cs` (or wherever RockPlace/TreeOrient mouse handling lives).

**Scope:** `GameManager.cs` (`CancelRockPlace()`), `TreeSkeleton.cs` (snapshot struct), `buttonClicker.cs` (hide/show HUD, wire Cancel), `ButtonUI.uxml` (Cancel button), `TreeInteraction.cs` (camera-relative drag/rotate)

</details>

---

<details>
<summary><strong>Repot Root Raking Mini-Game</strong></summary>

**Trigger:** When the player repots a pot-bound tree (roots have hit the boundary wall for 2+ seasons), a rake mini-game fires before the new soil is applied.

**Design rationale:**
- Root-bound roots are tangled tightly in old soil — real repotting requires teasing them apart with a root rake before you can cut and arrange them
- The player gets to choose which roots to keep and which to prune, making repotting a deliberate act instead of a single button press
- Long roots are **valuable**: a long root creates surface roots and gives the tree room to develop nebari. The game should reward keeping at least one long root. The new pot generates one extra-long root on the side the player kept their longest root.

**Flow:**
1. Repot button pressed while pot-bound → enter `GameState.RootRake` (new state)
2. Camera tilts to a top-down view of the root ball lifted out of the pot
3. Player moves the mouse over roots to highlight them. **Left-click-drag** rakes horizontally across the root ball — each stroke un-mats nearby root nodes (visual only: they spread outward slightly)
4. After raking, unhealthy / excess roots are highlighted in red. Player left-clicks individual root tips to **prune** them
5. A root-count indicator shows current vs. target range. Target = approximately the count `PreGrowRootsToSoil` generates fresh (e.g., 6–10 root strands)
6. Once within target range, **Confirm Repot** becomes enabled; pressing it:
   - Calls `PotSoil.Repot()` as normal
   - Discards all current root nodes
   - Re-generates fresh roots via `PreGrowRootsToSoil` — same count as confirmed, preserving the cheat
   - If the player kept any root longer than 1.5× the average, flags `hasLongRoot = true` on the skeleton → next call to `PreGrowRootsToSoil` spawns one extra-long root cable on that side

**Root count cheat rationale:**
The player doesn't notice because the new roots look similar in count and arrangement to what they kept. The important feedback loop is the *decision* to prune carefully, not the exact topology.

**Scope:** `GameManager.cs` (new `RootRake` state), `buttonClicker.cs` (trigger + UI), `TreeSkeleton.cs` (`hasLongRoot` flag, hook into `PreGrowRootsToSoil`), `TreeInteraction.cs` (rake brush + pruning in RootRake mode), `ButtonUI.uxml` (root count indicator + Confirm Repot button)

</details>

---

<details>
<summary><strong>Pot and Rock Size Selection</strong></summary>

**Goal:** Give the player meaningful choices when repotting or placing a rock, with different sizes creating different constraints on root growth.

**Pots:**
- Pot sizes: XS / S / M / L / XL, plus a slab option (very shallow, very wide)
- Size affects `rootAreaTransform` scale — smaller pot → roots hit boundary sooner → pot-bound faster → repotting needed more frequently
- Shape variants: round, oval, rectangle, cascade (tall) — affects root area aspect ratio
- Shallow pots (slab) encourage wide lateral roots; deep pots allow longer downward roots
- UI: a pot selection panel in the Repot flow; each pot shown as a silhouette with size label
- Pot dimensions stored on `PotSoil` as `potWidth`, `potDepth`, `potHeight`; `rootAreaTransform` resized on repot to match

**Rocks (Ishitsuki):**
- Rock sizes: S / M / L / XL; taller rocks require more root cable length to reach soil → more seasons before the tree stabilises
- Rock shape presets affect how `PreGrowRootsToSoil` drapes cables (steep cliff vs. gentle slope vs. plateau)
- UI: rock selection panel before `GameState.RockPlace`; shown as silhouettes

**Root issues by size:**
| Container | Root effect |
|-----------|-------------|
| XS pot | Pot-bound in 1–2 years; strong nebari pressure; risk of root crush damage |
| Slab | Wide spreading roots; minimal downward growth; surface root aesthetics strong |
| Large rock | Very long cables; slower anchor; exposed root risk if not enough canopy to support them |
| Small rock | Short cables; quick anchor; less dramatic nebari; easier for beginners |

**Scope:** `PotSoil.cs` (`potWidth`/`potDepth`/`potHeight`, `rootAreaTransform` resize), `TreeSkeleton.cs` (Ishitsuki cable length scale by rock size), `buttonClicker.cs` + `ButtonUI.uxml` (pot/rock selection panels), `SaveData`/`SaveManager.cs` (serialize selected pot/rock)

</details>

---

<details>
<summary><strong>Species Visuals</strong> → scheduled as item 25</summary>

*(See item 25 above for full spec.)*

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
<summary><strong>Quick-Start / Auto-Generate</strong></summary>

- Auto-simulate ~1 year of growth in background
- Present 10 variation options; player picks one (or multiples) to start with
- Foundation: needs stable multi-year simulation first

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
<summary><strong>Multi-Tree Planting</strong></summary>

- Plant two trees in the same pot
- Root/branch collision and fusion mechanics
- Shared health/soil system

</details>

---

<details>
<summary><strong>Camera & Input</strong></summary>

- Full Unity Input System migration (currently legacy `Input.*`)
- Wire animation skip key revisited as part of this

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
