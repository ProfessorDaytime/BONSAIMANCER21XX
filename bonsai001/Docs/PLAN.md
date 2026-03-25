# BONSAIMANCER — Development Plan

Last updated: 2026-03-24

---

## Current Priority Queue

Work through these in order. Do not start the next item until the current one is
shippable (playable without obvious breakage).

Items 1–6 are complete. The queue below is the next phase.

### 7. Nebari Development
**Goal:** Surface root flare scoring and shaping. Encourage roots to hug the soil
surface and radiate outward evenly — the defining aesthetic of a quality bonsai base.

**Scope:** `TreeSkeleton.cs`, `TreeInteraction.cs`, UI
- Nebari score: measures how evenly roots radiate from the trunk at soil level
  (angular coverage + surface exposure vs. buried depth)
- Visual feedback: score or quality indicator in UI
- Root pruning already in place (RootPrune state); this layer adds scoring on top
- Possible: root training tool to nudge surface roots into better positions
- Depends on: Root System (done ✓), Root Area box (done ✓)

---

### 8. Ishitsuki (Root-over-Rock)
**Goal:** Player places a rock near the trunk; roots detect it and route over/around
it over multiple seasons, gripping the surface.

**Scope:** New rock placement system, `TreeSkeleton.cs`, `TreeInteraction.cs`
- Rock prop: player places/positions a rock GameObject near the trunk base
- Roots detect nearby rock geometry and deflect to hug its surface
- Over seasons, roots partially embed into rock crevices (visual only — offset mesh)
- Depends on: Root System (done ✓), Root Area box (done ✓)

---

### 9. Watering System
**Goal:** Water the tree to keep it healthy. Neglect causes drought stress that
slows growth and eventually damages nodes. Creates a real maintenance loop.

**Scope:** `GameManager.cs`, `TreeSkeleton.cs`, UI
- Soil moisture level (float 0→1), drains over time at a species-configurable rate
- Watering can tool/button fills moisture back to 1
- Drought damage feeds into existing `NodeHealth` via `DamageType.Drought`
- Moisture visible in UI (soil indicator or pot texture change — TBD)
- Low moisture → `Drought` damage accumulates on all nodes each season
- Very low moisture → dormancy (mirrors health < 0.25 behaviour already in place)
- Depends on: Health System (done ✓)

---

### 10. Pinching Tool
**Goal:** Light spring maintenance — remove the soft shoot tip between the first
leaf pair before it hardens. Keeps internodes short without trauma.

**Scope:** `ToolManager.cs`, `TreeInteraction.cs`, `TreeSkeleton.cs`
- New `ToolType.Pinch`; highlights soft new-growth tips (current season only)
- On click: removes tip node, triggers `backBudStimulated` on parent (same as trim)
- No wound created (too small); tiny health cost vs. shears
- Depends on: Bud System (done ✓)

---

### 11. Defoliation
**Goal:** Remove leaves strategically to encourage finer ramification and
back-budding. Risk/reward — safe in early summer on healthy trees only.

**Scope:** `LeafManager.cs`, `TreeInteraction.cs`, new defoliation mode
- Partial defoliation: remove one leaf per pair (~50% surface) — always safe
- Full defoliation: remove all leaves — meaningful health cost, large back-bud boost
- Timing enforced: only available in early summer (June); outside window = disabled tool
- Smaller replacement leaves next season (scale parameter on `LeafManager`)
- Depends on: Bud System (done ✓), Leaf system

---

### 1. Tight-Angle Geometry  ✓ *done*
**Goal:** Prevent vertex pinching where branches bend sharply.
**Scope:** Self-contained change to `TreeMeshBuilder.cs`.
- Bend rings inserted at child base; parallel-transport fix prevents tip ring twist.

---

### 2. Post-Trim Depth Cap  ✓ *done*
**Goal:** After trimming back hard, regrowth is limited to early-year depths —
a branch cut to depth 1 can't grow 6 levels in one season.
**Scope:** `TreeSkeleton.cs`, `TreeNode.cs`.
**Approach:**
- Add `trimCutDepth` and `regrowthSeasonCount` fields to `TreeNode`.
- When a node is identified as a trim cut point, record its depth.
- Each new growing season, the cut point's subtree gets a depth cap of:
  `trimCutDepth + (regrowthSeasonCount * REGROWTH_DEPTH_PER_SEASON)`
  where `REGROWTH_DEPTH_PER_SEASON` mirrors the year-1 allowance.
- `regrowthSeasonCount` increments each spring on affected nodes.
- After enough seasons the cap naturally reaches the global `depthCap` and the
  special treatment ends.

---

### 3. Wire Rework + Health System Foundation  ✓ *done*
**Goal:** Realistic wiring with meaningful consequences; health system that
future mechanics (watering, nutrients, trimming trauma) can feed into.

#### 3a. Wire Rework

**New wiring flow:**
1. Player aims and confirms wire direction.
2. `GameState.WireAnimate` fires — time frozen, camera still moves, all other
   input blocked.
3. Branch snaps to `wireTargetDirection` immediately in skeleton data.
4. A ~0.6 s spring animation plays on the mesh with slight overshoot + settle.
5. Player can skip animation with **Enter** (full input system refactor later).
6. On animation end → auto-resume prior game state.

**New data on `TreeNode`:**
```
wireOriginalDirection   Vector3   direction at time of wiring
wireTargetDirection     Vector3   player-aimed direction
wireSetProgress         float     0→1, fully set = ready to remove
wireDamageProgress      float     0→1, accumulates after fully set
wireAgeDays             float     total in-game days wire has been on
```

**Set/damage accumulation:**
- Both progress values accumulate only during `BranchGrow` state.
- Rate uses the same `SeasonalGrowthRate` multiplier (dormant seasons do nothing).
- `setProgress` fills over ~2 growing seasons at speed 1.
- `damageProgress` begins filling as soon as `setProgress >= 1.0`.

**Early removal (setProgress < 1.0):**
```
newDirection = Slerp(wireOriginalDirection, wireTargetDirection, setProgress)
```
Branch snaps immediately to the intermediate direction — no animation, player
already had their satisfying spring moment when wiring.

**Re-wiring a previously-set branch:**
- Allowed freely; no cooldown.
- Re-bending set wood applies a health hit to the node:
  `damage = Lerp(0.05, 0.25, setProgress)` — the more fully set, the more
  stress from re-bending.
- Uses `WireBend` damage type in the health system (see 3b).

**Wire colour progression (on wire mesh material):**
| Condition | Colour |
|---|---|
| `setProgress` 0 → 1 | Silver |
| `setProgress >= 1.0` | Obvious gold emissive glow (pulsing) |
| Damage zone begins | Gold → Orange lerp |
| `damageProgress >= 0.5` | Orange → Red |
| `damageProgress >= 1.0` | Deep red, wire embedded |

#### 3b. Health System (Foundation)

**`float health = 1f` on `TreeNode`, range 0 → 1.**

Damage sources (now or reserved for later):
```
WireBend      — re-bending set wood; immediate hit
WireDamage    — progressive, from wireDamageProgress
TrimTrauma    — small hit on trim, recovers over a season      [later]
Drought       — slow drain if unwatered                        [later]
NutrientLack  — reduces recovery rate                         [later]
WoundDrain    — progressive, from open unprotected wounds      [item 6]
```

Health thresholds and effects:
| health | Effect |
|---|---|
| `< 0.75` | Growth rate multiplied by `health` |
| `< 0.5` | Leaves drop early; branch mesh tints slightly grey |
| `< 0.25` | Branch goes dormant (no growth regardless of season) |
| `<= 0` | Branch dead: mesh turns grey, leaves all fall, no regrowth |

Dead branches:
- Remain in the scene visually until the player trims them away.
- Can still be trimmed/removed normally.
- Propagate a reduced health penalty to the parent node (stress from carrying
  dead wood).

**`DamageType` enum** lives in a new `NodeHealth.cs` alongside a small helper
so the system stays clean as more damage types are added later.

---

### 4. Root System  ✓ *done*
**Goal:** Visible surface/subsurface roots (nebari), trimmable in RootPrune mode
and naturally scorable for flared-root development.
**Scope:** Extended existing `TreeSkeleton`/`TreeNode`/`TreeMeshBuilder`/`TreeInteraction`
and `CameraOrbit` (Option A — no new classes needed).

**What was built:**
- `isRoot` flag on `TreeNode`; roots are children of `skeleton.root`.
- Gravity-biased `ContinuationDirection` and `LateralDirection` for root nodes.
- Separate depth cap: `maxRootDepth` (not `SeasonDepthCap`) for root nodes.
- Pipe model thickens trunk base from root radii automatically.
- `renderRoots` flag on `TreeMeshBuilder`; root nodes skipped in mesh unless flag set.
- `PlantRoot(Vector3 localDir)` on `TreeSkeleton` — player-triggered from soil plane click.
- `GameState.RootPrune`: tree lifts (`rootLiftHeight`, animated), roots revealed.
- `HandleRootWorkHover()` in `TreeInteraction`: click root mesh to trim, click soil to plant.
- `CameraOrbit` pitch relaxed to `pitchMinRootPrune` (−30°) in RootPrune mode.

---

### 5. Bud System  ✓ *done*
**Goal:** Spring growth emerges from pre-formed buds set the previous late summer.
Gives the simulation biological accuracy and enables back-budding, apical dominance
tuning, and the pinching mechanic. Applies to all tree species.

**Phases:**

**Bud Set (August):** When the season winds down, terminal nodes set a bud.
- `node.hasBud = true`
- Bud GameObject (player-provided prefab) spawned at `node.tipPosition`
- Visible through the dormant winter period

**Dormant lateral buds:** Set during growth alongside each new node.
- `node.dormantBudCount` tracks latent axillary buds on each node
- Normally suppressed (apical dominance); activation chance is low
- Increased activation chance in spring if `node.backBudStimulated = true`

**Bud Break (March):** `StartNewGrowingSeason` reads `hasBud` nodes.
- Bud GameObjects destroyed (or animate away)
- Growth begins from those nodes as today

**Back-budding from pruning:** When a tip is trimmed:
- The nearest 2–3 ancestor nodes get `backBudStimulated = true`
- Next spring, those nodes roll against an elevated lateral activation chance
- Simulates the hormonal response to tip removal (apical dominance released)

**New data on `TreeNode`:**
```
hasBud               bool     terminal bud set this late summer
dormantBudCount      int      latent axillary buds available on this node
backBudStimulated    bool     tip ancestry was trimmed; boosted lateral chance next spring
```

**New on `TreeSkeleton`:**
```
[SerializeField] GameObject budPrefab             drag in bud prefab
[SerializeField] GameObject lateralBudPrefab      optional — visible dormant laterals
[SerializeField] float      backBudActivationBoost  multiplier on springLateralChance for stimulated nodes
```

**Scope:** `TreeSkeleton.cs`, `TreeNode.cs`, bud prefab(s)

---

### 6. Wound System  ✓ *done*
**Goal:** Trimming branches leaves wounds that are a real health risk without care.
Players manage wounds with cut paste. Wounds heal slowly over seasons, visually
and mechanically. Thin tip cuts barely matter; removing a large branch unprotected
should have meaningful consequences. Applies to all tree species; vulnerability
is a per-species parameter.

**Wound lifecycle:**
1. Branch trimmed → wound created at parent node (cut site)
2. Wound GameObject spawned, scaled by `woundRadius` (radius of cut branch)
3. Each growing season: wound drains health from the node
4. Player can apply cut paste → `pasteApplied = true`, drain drops to ~5%
5. `woundAge` increments each growing season; wound heals when
   `woundAge >= woundRadius × seasonsToHealPerUnit`
6. On heal: health drain stops, wound GameObject destroyed

**Health drain (per growing season):**
```
drain = woundRadius × woundDrainRate × (pasteApplied ? 0.05f : 1.0f)
```

**Wound healing (visual):**
- Wound GameObject scale lerps from `woundRadius` to 0 as `woundAge` approaches the heal threshold
- Represents callus tissue slowly rolling over the cut

**New data on `TreeNode`:**
```
hasWound             bool
woundRadius          float   radius of cut branch at time of trim
woundAge             float   growing seasons elapsed since cut
pasteApplied         bool    player protected this wound
```

**New on `TreeSkeleton` (species-configurable):**
```
[SerializeField] GameObject woundPrefab
[SerializeField] float      woundDrainRate          health lost per season per unit of radius
[SerializeField] float      seasonsToHealPerUnit    growing seasons to close per unit of radius
```

**Cut paste action:**
- New tool/interaction: player clicks a wound in normal mode to apply paste
- Cheap, unlimited — the cost is attention, not resources (for now)
- Visual change on wound GameObject when paste applied

**Scope:** `TreeSkeleton.cs`, `TreeNode.cs`, `TreeInteraction.cs` (paste action),
wound prefab

---

## Backlog (not yet scheduled)

### Watering System
- Watering can tool, soil moisture level, drain rate
- Feeds into `NodeHealth` via `Drought` damage type
- Nutrient concentration affects growth rate and recovery speed

### Pinching Tool
- Lighter spring action distinct from shears: remove the soft shoot tip between
  the first leaf pair after it unfolds
- Keeps internodes short and twigs fine without the trauma of hard pruning
- Triggers `backBudStimulated` on nearby nodes (same as trim, lower health cost)
- Depends on: Bud System (item 5)

### Defoliation
- Remove one leaf from each pair (~50% leaf surface) — the safe standard practice
- Primary effect: shorter internodes and increased back-budding next season
- Secondary effect: slightly smaller replacement leaves
- Full defoliation (remove all leaves) is higher-risk: meaningful health cost,
  only appropriate for healthy, developed trees
- Timing matters: early summer only; defoliating a weak or young tree damages it
- Depends on: Bud System (item 5), Leaf system refinement

### Tree Species
- Growth parameters, wound vulnerability, seasonal colours, leaf shapes
- All species-configurable values already in place as `[SerializeField]` on
  `TreeSkeleton` — species = a ScriptableObject that drives those values
- Japanese maple specifics (opposite bud pairs, bark evolution stages,
  red spring flush) added here once generic systems are solid

### Camera & Input
- Full Unity Input System migration (currently legacy `Input.*`)
- Wire animation skip key revisited as part of this

### Multi-Tree Planting
- Plant two trees in the same pot
- Root/branch collision and fusion mechanics
- Shared health/soil system

### Quick-Start / Auto-Generate
- Auto-simulate ~1 year of growth in background
- Present 10 variation options; player picks one (or multiples) to start with
- Foundation for the above: needs stable multi-year simulation first

---

## Completed

- Procedural branch skeleton + mesh builder (`TreeSkeleton`, `TreeMeshBuilder`)
- Wire placement + bend (`TreeInteraction`, `WireRenderer`)
- Trim subtree + highlight mesh
- Leaf lifecycle: spring spawn, autumn colour gradient, stochastic fall,
  fall animation (`LeafManager`, `Leaf`)
- Seasonal time system + game state machine (`GameManager`)
- Camera orbit, zoom, Y-pan (`CameraOrbit`)
- Tight-angle geometry: bend rings + parallel-transport frame fix (`TreeMeshBuilder`)
- Post-trim depth cap: cut point tracking + per-season regrowth limit (`TreeSkeleton`, `TreeNode`)
- Wire rework: instant snap + spring animation, Enter to skip, WireAnimate state (`TreeInteraction`, `GameManager`)
- Health system foundation: `health` on `TreeNode`, `DamageType` enum, `ApplyDamage`, health-gated growth (`TreeSkeleton`, `NodeHealth.cs`)
- Wire colour progression: silver → gold pulse → orange → red (`WireRenderer`)
- Root system: `isRoot` flag, gravity-biased growth, `PlantRoot`, `RootPrune` state, lift animation, soil-plane interaction, pitch relaxation (`TreeSkeleton`, `TreeMeshBuilder`, `TreeInteraction`, `CameraOrbit`)
- Bud system: terminal buds set in August, bud GameObjects spawned/destroyed, back-budding on trim (up to 3 ancestors stimulated), `backBudActivationBoost`, old-wood bud chance, show/hide toggles for terminal and lateral bud prefabs (`TreeSkeleton`, `TreeNode`)
- Wound system: half-torus wound visualization on trim, `woundRadius`/`woundFaceNormal`/`woundAge`, health drain per season, cut paste tool + Paste button UI, `pasteApplied` tints wound, subdivision-cut detection (smaller ring), wound cleanup on subtree removal (`TreeSkeleton`, `TreeNode`, `TreeInteraction`, `ToolManager`, `buttonClicker`, `ButtonUI.uxml`)
- Growth stability: `maxBranchNodes` hard cap, `vigorFactor` scaling lateral/back-bud chances as tree fills up; fixed `InvalidOperationException` in back-budding loop (snapshot `allNodes` before iteration) that was silently aborting spring music (`TreeSkeleton`)
- Root Area box containment: `rootAreaTransform` reference replaces radial spread; `RootDistRatio` checks XZ walls + Y floor + Y ceiling in Root Area local space; `DeflectFromRootAreaWalls` deflects all six faces (`TreeSkeleton`)
- Pot-bound root system: `boundaryPressure` counter per root node, thickening over seasons, `boundaryGrowthScale` slows terminal growth near walls, `wallSegmentScale` shortens segments near walls for smoother curves, `potBoundInnerBoost` stimulates low-depth fill-in laterals, `potBoundMaxFillPerYear` budget independent of outer cap (capped at 1.5× `maxTotalRootNodes`) (`TreeSkeleton`, `TreeNode`)
