# BONSAIMANCER — Development Plan

Last updated: 2026-04-02  (items 1–14 complete)

---

## Current Priority Queue

Work through these in order. Do not start the next item until the current one is
shippable (playable without obvious breakage).

Items 1–14 are complete. The queue below is the next phase.

### 15. Pinching Tool  ← NEXT
**Goal:** Lighter spring action distinct from shears: remove the soft shoot tip between
the first leaf pair after it unfolds.
- Keeps internodes short and twigs fine without the trauma of hard pruning
- Triggers `backBudStimulated` on nearby nodes (same as trim, lower health cost)
- Increments `refinementLevel` on the pinched node — fastest path to fine internode
  shortening since pinching can be done every shoot every spring
- Does NOT consume energy from the leaf cluster — leaves stay, just tip removed
- New tool type `ToolType.Pinch` in `ToolManager`; click a terminal growing tip to pinch
- Depends on: Bud/Leaf Integration (done ✓), Refinement Level (done ✓)

**Scope:** `ToolManager.cs` (new ToolType), `TreeInteraction.cs` (pinch hover + click),
`TreeSkeleton.cs` (PinchNode method), `buttonClicker.cs` + `ButtonUI.uxml` (Pinch button)

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

### 14. Save / Load System

**Goal:** Persist the full tree state to disk so the player can close and reopen
the game without losing years of growth.

**What needs serializing:**

| Data | Location |
|---|---|
| All `TreeNode` fields (id, depth, position, direction, radius, length, health, refinementLevel, wires, wounds, buds, etc.) | `TreeSkeleton.allNodes` |
| `GameManager` time state (year, day, current season, timescale) | `GameManager` |
| `TreeSkeleton` tuning values (can be omitted — re-read from Inspector on load) | optional |
| Leaf state (which node IDs have leaves) | `LeafManager.nodeLeaves` |
| Rock position/rotation (Ishitsuki) | Rock transform |
| Pot / tray position | already in scene; likely skip |

**Approach: JSON via `JsonUtility` or `Newtonsoft.Json`**

- `SaveData` plain C# class mirroring `TreeNode` fields as serializable primitives
  (no `Vector3` — serialize as float triples, or use `JsonUtility` which handles it)
- `SaveManager` MonoBehaviour (or static class) with `Save()` / `Load()` methods
- Save path: `Application.persistentDataPath/save.json`
- Auto-save at the end of each growing season (BranchGrow → BudSet transition)
- Manual save: Settings menu button ("Save Game")
- On load: reconstruct `allNodes` list, re-link parent/child references by id,
  re-spawn wound/wire/leaf GameObjects, set GameManager state

**Node re-linking after deserialize:**
```
1. Deserialize flat list of SaveNode (id, parentId, all data fields)
2. Instantiate TreeNode objects from SaveNode, assign all primitive fields
3. Second pass: for each node, node.parent = nodeById[parentId]; parent.children.Add(node)
4. Identify root node (parentId == -1)
```

**Leaf restore:** call `LeafManager.ForceSpawnLeaves(nodes)` on all terminal,
non-trimmed, non-root nodes after load. Leaves always respawn fresh — no need
to serialize individual leaf positions.

**Wire/wound GameObjects:** call existing `CreateWoundObject` and `WireRenderer`
spawn paths after populating node data — same as first-time creation.

**Scope:** New `SaveManager.cs`, `TreeSkeleton.cs` (expose save/load hooks),
`GameManager.cs` (serialize time state), `buttonClicker.cs` (Save button in Settings),
`ButtonUI.uxml` (Save button in Time tab or new Save tab)

**Implementation order:**
1. `SaveNode` data class + `SaveData` wrapper
2. `SaveManager.Save()` — serialize nodes + GameManager state to JSON
3. `SaveManager.Load()` — deserialize, rebuild tree, re-spawn visuals
4. Auto-save hook at BranchGrow→BudSet transition
5. Manual save button in Settings menu (Time tab)
6. Test: save mid-summer, restart editor, load — verify year/nodes/leaves match

---

## Backlog (not yet scheduled)

### Pot-Bound Soil Block
- When pot-bound roots go unchecked for **2+ years**, they fuse with the soil into
  a dense impacted mass that must be dealt with before Ishitsuki is available
- **Hard blocker** — Ishitsuki (`GameState.RockPlace`) is gated until cleared
  (impacted root ball would be both visually wrong and technically crash-prone in Ishitsuki)
- **Two removal methods:**
  - *Wash out* — hold the pot under a running faucet for a timed duration; slower,
    low trauma (watering can interaction or dedicated faucet prop TBD)
  - *Cut out* — use existing root-prune scissors in RootPrune mode; faster,
    meaningful health cost to nearby roots; same tool, different target
- Visual change: open; ideas include packed/cracked soil surface, roots visibly
  protruding from drainage holes, a UI indicator ("ROOT BOUND"), tray texture swap

**Implementation notes:**
- Track `potBoundYears` counter on `TreeSkeleton`; increment each spring when
  `boundaryPressure` across all roots exceeds a threshold
- `soilBlockActive` bool gates `GameState.RockPlace` in `buttonClicker`
- Wash-out: a hold-input timed action (e.g. 3–5 real seconds), clears `soilBlockActive`
- Cut-out: clicking root nodes marked as `isSoilBlock` in RootPrune mode shears
  them, clearing the block at a health cost

---

### Branch Weight & Strength

**Goal:** Heavy branches sag over time under their own load. Thin young wood bends;
thick old wood resists. Wires, careful pruning, and prop supports counteract droop.
Creates a new axis of craft — managing structure, not just shape.

> **Open design question:** How universal should this be? A large-branched juniper or
> cascading pine needs this badly. A fine-twigged Japanese maple with tiny branches may
> not. Likely this is a species flag (`branchWeightEnabled` on the species ScriptableObject)
> rather than a global system. Decision deferred until species system (backlog) is clearer.

---

#### Core Data

Two new floats on `TreeNode`:

```
branchLoad     float   accumulated downward force this node carries (own mass + children's load)
branchStrength float   structural resistance to bending; derived from radius and age
```

`branchLoad` is computed bottom-up each spring (terminals first, propagate to root):
```
node.branchLoad = node.radius² × node.length × woodDensity
                + sum(child.branchLoad for all children)
```

`branchStrength` is derived, not stored — recomputed from current state:
```
strength = radius³ × woodHardnessFactor × Mathf.Clamp01(node.age / matureAgeSeasons)
```
Young soft wood (low age) has low strength even if thick. Old wood is hard and rigid.

---

#### Sag Mechanic

Each spring, if `branchLoad / branchStrength > sagThreshold`:
```
sagAngle = Mathf.Clamp((load/strength - sagThreshold) * sagSensitivity, 0, maxSagAngleDeg)
node.growDirection = Slerp(node.growDirection, -Vector3.up, sagAngle / maxSagAngleDeg * sagBlend)
```

- Sag accumulates slowly — a branch doesn't crash overnight, it drifts over seasons
- Mesh updates automatically (growDirection change → mesh rebuild)
- Wire resists sag: a wired node's growDirection is already locked toward `wireTargetDirection`,
  so the wire's set resistance naturally counteracts the sag drift

**Cascade style:** intentionally heavy branches allowed to sag completely — the player
wires them down and then removes the wire once set, achieving the cascade silhouette
without fighting it.

---

#### Junction Stress

When a child branch is very heavy relative to its attachment node:
- `ApplyDamage(parent, DamageType.JunctionStress, stressDamage)` each spring
- Damage scales with `excess = childLoad - parent.branchStrength` — small excess = minor
  cosmetic stress, large excess over many seasons = structural failure (node dies)
- Remedy: remove the heavy sub-branch, or wire-support it to redistribute load

New `DamageType.JunctionStress` added to `NodeHealth.cs`.

---

#### Prop Supports (optional complexity)

A lightweight companion to wires for heavy branches:
- Player places a small prop object under a drooping branch
- Mechanically: marks the supported node as `hasPropSupport = true`; sag calculation
  skips those nodes
- Visual: a stick or forked branch prop prefab (no physics needed)
- Low scope addition once the core sag system is stable

---

#### Species Gate

Not all trees need this. Suggested species flags:
| Species type | Needs weight system? |
|---|---|
| Juniper, pine (thick secondary branches) | Yes |
| Cascade style (any species) | Yes — load is the whole point |
| Literati / windswept (minimal foliage mass) | Mild |
| Japanese maple (fine, light twigs) | No — twigs are too light to matter |
| Weeping cherry / willow | Yes — natural sag is the aesthetic |

**Scope:** `TreeNode.cs` (add `branchLoad`), `TreeSkeleton.cs` (bottom-up load calc,
sag pass in `StartNewGrowingSeason`, junction stress), `NodeHealth.cs` (new damage type),
species ScriptableObject (enable/disable flag + `woodDensity`, `woodHardnessFactor`,
`matureAgeSeasons`, `sagThreshold`, `sagSensitivity`)

**Dependencies:** Species system (backlog) — or can be prototyped with global
SerializeFields first and moved to ScriptableObject later.

---

### Watering System
- Watering can tool, soil moisture level, drain rate
- Feeds into `NodeHealth` via `Drought` damage type
- Nutrient concentration affects growth rate and recovery speed

### Pinching Tool
- Lighter spring action distinct from shears: remove the soft shoot tip between
  the first leaf pair after it unfolds
- Keeps internodes short and twigs fine without the trauma of hard pruning
- Triggers `backBudStimulated` on nearby nodes (same as trim, lower health cost)
- **Now also increments `refinementLevel`** on pinched node (item 10) — fastest path
  to fine internode shortening, since pinching can be done every shoot every spring
- **Does NOT consume the energy from that leaf cluster** — leaves stay, just tip removed
- Depends on: Bud/Leaf Integration (item 7a), Refinement Level (item 10)

### Defoliation
- Remove one leaf from each pair (~50% leaf surface) — the safe standard practice
- Primary effect: shorter internodes and increased back-budding next season
- Secondary effect: **increments `defoliationFactor`** which drives `leafScale` down
  next season (item 11 — Dynamic Leaf Scale)
- **Costs treeEnergy** (item 9) — you're sacrificing this season's photosynthesis
  for long-term refinement; only viable on a healthy, energetic tree
- Full defoliation (remove all leaves): higher energy cost, stronger leaf miniaturization
  effect, meaningful health cost — for advanced trees only
- Timing matters: early summer only; defoliating a weak tree crashes its energy budget
- Depends on: Bud/Leaf Integration (item 7a), Leaf Energy System (item 9),
  Dynamic Leaf Scale (item 11)

### Air Layer Root Continued Growth

**Goal:** Air layer roots (above-ground roots that developed on the trunk) should
keep growing after confirmation — currently they stop at the rock surface.

**Behaviour wanted:**
- Exposed air layer root tips continue extending each spring like any other root terminal
- Growth direction: gravity-biased downward, same as normal surface roots
- If a rock or convex collider is nearby, roots deflect toward and along its surface —
  the same rock-following logic used during Ishitsuki confirmation, but running live
  each season rather than only at confirm time
- Once a root tip reaches the soil plane it transitions to normal subsurface root behaviour
  (joins the pot root system, becomes trimmable in RootPrune mode)

**Scope:** `TreeSkeleton.cs` (`StartNewGrowingSeason` root growth pass — remove or
relax the `isAirLayerRoot` exclusion that currently halts their growth),
`ContinuationDirection` (add rock-proximity deflection for air layer nodes, mirroring
Ishitsuki drape logic)

**Implementation note:** The Ishitsuki `PreGrowRootsToSoil` already has rock-surface
projection logic. Air layer roots need the same per-step closest-point check, but
running in the standard seasonal growth loop rather than the one-shot pre-grow pass.

---

### Root Visibility Bug (Post-Ishitsuki)

**Bug:** After root-on-rock (Ishitsuki) is confirmed, non-Ishitsuki roots (pot roots,
surface roots) are invisible in RootPrune mode. They can be hovered (red outline
appears) and trimmed, but the mesh is never rendered — before or after the trim.

**Likely cause:** The Ishitsuki confirm step modifies `renderRoots`, a material flag,
or a layer/render flag on `TreeMeshBuilder` that the non-Ishitsuki root mesh depends
on. Or: the confirm step sets a condition that causes `TreeMeshBuilder` to skip
non-training-wire root nodes in the mesh rebuild.

**Scope:** `TreeMeshBuilder.cs` (check root-node mesh inclusion condition),
`TreeSkeleton.cs` (check if Ishitsuki confirm changes any flag that gates root rendering),
`buttonClicker.cs` (check RootPrune enter/exit toggles `renderRoots` correctly)

**Fix approach:**
1. Add GL debug lines in `OnRenderObject()` on `TreeMeshBuilder` — draw a colored line
   or ring for every root node regardless of render flags, so invisible roots become
   immediately visible as geometry even when the mesh isn't built for them
2. Color-code by state: green = included in mesh, red = excluded, yellow = isAirLayerRoot,
   cyan = isTrainingWire — makes the flag divergence obvious at a glance
3. Compare GL overlay before and after Ishitsuki confirm to identify which nodes drop out
4. Once the flag is identified, fix and remove the GL overlay

---

### Soil System

**Goal:** The substrate the tree grows in has meaningful mechanical consequences.
Mixing ratios, repotting, and substrate choice are part of bonsai practice — not
just decoration.

---

#### Substrate Components

Each pot holds a **soil mix** defined as a blend of up to four substrate types.
The mix is expressed as proportions (must sum to 1.0).

| Substrate | Water Retention | Drainage | Nutrients | Notes |
|---|---|---|---|---|
| **Akadama** | Medium | Medium | Low | Classic bonsai clay — retains shape, breaks down over years |
| **Pumice** | Low | High | None | Pure drainage and aeration; root anchor |
| **Lava rock** | Very low | Very high | None | Maximum aeration; no breakdown |
| **Organic compost** | High | Low | High | Feeds the tree; compacts over time, reduces drainage |
| **Sand** | Very low | High | None | Cheap drainage filler; no structure |
| **Kanuma** | High | Medium | Low | Acidic; ideal for azalea and acid-loving species |

**SerializeField mix** on `TreeSkeleton` (or a new `PotSoil` component):
```csharp
[SerializeField] float akadama   = 0.5f;
[SerializeField] float pumice    = 0.3f;
[SerializeField] float lavaRock  = 0.2f;
[SerializeField] float organic   = 0.0f;
```

---

#### Derived Properties

Computed from the mix at repot time (or when mix changes):

```
waterRetention  = weighted average of component retention values
drainageRate    = weighted average of component drainage values
nutrientLevel   = weighted average × organic fraction (bonus for compost)
aerationScore   = inverse of water retention (pumice/lava push this up)
```

These drive four gameplay values:

| Property | Effect |
|---|---|
| `waterRetention` | How long soil stays moist after watering; slower drain = less frequent watering needed but higher rot risk |
| `drainageRate` | How fast excess water exits; poor drainage → waterlogged → root rot damage |
| `nutrientLevel` | Boosts `treeEnergy` multiplier each season; decays over time as roots consume it |
| `aerationScore` | Oxygen availability at roots; low aeration → slowed root growth, higher `Drought` damage floor |

---

#### Soil Degradation

Substrates break down at different rates over years. Akadama and organic compost
compact the most; pumice and lava rock are nearly inert.

- Each growing season: `degradation += component.degradeRate`
- At high degradation: `drainageRate` drops, `waterRetention` rises
- Visual/UI cue: soil surface texture or an inspector-visible degradation score
- **Repotting resets degradation** — the primary mechanical reason to repot

---

#### Repotting

A periodic action (typically every 2–5 years on a healthy bonsai):

1. Player enters `RootPrune` mode
2. New **Repot** button becomes available (replaces or joins Air Layer)
3. Opens a substrate mixing UI (four sliders, sum locked to 100%)
4. On confirm: new mix applied, degradation reset, root trim recommended
   (repotting without any root reduction gives a mild stress penalty)
5. Root stress: `ApplyDamage(DamageType.RepotStress, repotStressDamage)` on all
   root terminals — recovers over one season

**Repot timing penalty:** Repotting in the wrong season (outside early spring)
applies a larger stress hit. A frost-health debuff if repotted in winter.

---

#### Nutrient Depletion & Fertilizing

`nutrientLevel` depletes each season as the tree grows:
```
nutrientLevel -= treeEnergy × nutrientConsumptionRate
```

When nutrient level is low:
- `treeEnergy` cap reduced (tree can't push full growth without food)
- Recovery rate from wounds/trauma reduced
- Leaf colour subtly fades (feeds into `LeafManager` colour lerp)

**Fertilizing** (new tool/action):
- Adds `nutrientBoost` to `nutrientLevel`, capped at `maxNutrientLevel`
- Over-fertilizing (repeated applications) pushes `nutrientLevel` above the cap →
  salt burn → `ApplyDamage(DamageType.FertilizerBurn, burnDamage)` on root nodes
- Types (optional complexity): balanced NPK vs. high-N (pushes growth, thin wood)
  vs. high-P (root strength, less top growth)

---

#### Waterlogging & Root Rot

If `drainageRate` is too low and watering is too frequent:
- Soil stays saturated: `saturationLevel` accumulates
- Above threshold: `ApplyDamage(DamageType.RootRot, rotRate)` on root nodes per season
- Root rot spreads upward — affected roots transmit reduced health to parent nodes
- **No cure** except repotting into better-draining substrate + removing rotted roots

---

#### Data Model

New component or fields on `TreeSkeleton`:

```csharp
// Mix (set at repot; serialized for save/load)
float akadama, pumice, lavaRock, organic, sand, kanuma;

// Derived (recomputed from mix)
float waterRetention, drainageRate, nutrientLevel, aerationScore;

// Live state
float soilDegradation;      // 0 = fresh, 1 = fully compacted
float saturationLevel;      // 0 = dry, 1 = waterlogged
float nutrientReserve;      // current nutrition; starts at mix's baseNutrientLevel
int   seasonsSinceRepot;    // increments each spring
```

---

#### Dependencies & Interactions

- **Watering System (item 13):** `drainageRate` and `waterRetention` directly control
  how fast moisture drains after a water event — soil system extends watering, not replaces it
- **Leaf Energy System (item 9):** `nutrientReserve` feeds into `treeEnergy` computation
  as an additive bonus: `treeEnergy *= Mathf.Lerp(nutrientMultiplierLow, 1f, nutrientReserve)`
- **Root System:** root growth speed scales with `aerationScore`; root rot damage
  uses existing `DamageType` and health system
- **Save/Load (item 14):** soil state fields (`degradation`, `saturationLevel`,
  `nutrientReserve`, current mix) must be serialized

**Scope:** New `PotSoil.cs` component (or fields on `TreeSkeleton`), `GameManager.cs`
(seasonal nutrient drain + degradation tick), `TreeInteraction.cs` (repot action),
`buttonClicker.cs` + `ButtonUI.uxml` (Repot button, substrate mixing UI),
`LeafManager.cs` (nutrient-driven colour influence)

---

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
- Ishitsuki (root-over-rock): `RockPlace` + `TreeOrient` game states, rock grab/rotate/confirm, tree orientation confirm, auto-generated training wires, root drape over rock surface via `PreGrowRootsToSoil`, `IshitsukiWire.cs`, Ishitsuki tab in Settings (`TreeSkeleton`, `TreeInteraction`, `RockPlacer`, `IshitsukiWire`, `buttonClicker`, `ButtonUI.uxml`)
- Trim trauma: `DamageType.TrimTrauma` applied on cut, seasonal recovery scaled by `treeEnergy` (`TreeSkeleton`, `NodeHealth.cs`)
- Trim undo: 5-second real-time window after each trim; `TrimUndoState` captures full subtree + ancestor flags; `Ctrl+Z` restores tree and re-spawns leaves; countdown label in UI (`TreeSkeleton`, `buttonClicker`, `ButtonUI.uxml`)
- Health ring GL debug: `OnRenderObject()` draws green→yellow→red rings at each node scaled by `node.radius`, toggled by `debugHealthRings` on `TreeMeshBuilder` (`TreeMeshBuilder`)
- Bud/leaf integration: `birthYear` on `TreeNode`; leaves only from buds on old wood; new spring growth (same year) bypasses bud gate; `subdivisionsLeft` guard removed so all growing tips get leaves (`LeafManager`, `TreeNode`)
- Ishitsuki root aging fix: training wire nodes (`isTrainingWire`) excluded from root-only skip in age loop so they brown correctly over ~2 seasons (`TreeSkeleton`)
- Leaf energy system: `ComputeTreeEnergy` in `LeafManager` computes `actual/potential` leaf area × health at bud-set; stored as `treeEnergy` on `TreeSkeleton`; drives growth speed, lateral chance, and trauma recovery (`LeafManager`, `TreeSkeleton`)
- Refinement level: `float refinementLevel` on `TreeNode`; +`refinementOnTrim/vigor` on cut, +`refinementOnBackBud` on back-bud activation; inherited by new growth; drives `chordLength × Pow(0.82, level)` shortening at all three segment-spawn sites (`TreeNode`, `TreeSkeleton`)
- Dynamic leaf scale: `seasonLeafScale` computed each spring from root pressure × refinement × defoliation factor; `baseLeafScale` is the species default; `defoliationFactor` stubbed at 0, decays 0.2/season; `RootPressureFactor()` and `RefinementCap` exposed on `TreeSkeleton` (`LeafManager`, `TreeSkeleton`)
- Per-branch vigor: `float branchVigor` on `TreeNode`; apical nudge each spring (`apicalVigorBonus / depth`), decay toward 1.0, clamp [0.2, 2.0]; multiplies `chordLength` and lateral chance; trim reduces vigor × 0.7 and scales refinement gain inversely (`TreeNode`, `TreeSkeleton`)
- Watering system: `soilMoisture` drained per in-game day; drought accumulator applies `DamageType.Drought` each season; `public void Water()` refills to 1.0; moisture bar in HUD (blue→amber→red) below watering can button; watering button works during BranchGrow/TimeGo/Idle post-plant (`TreeSkeleton`, `buttonClicker`, `ButtonUI.uxml`)
- Save / Load system: `SaveManager` static class with `Save()`/`Load()` writing JSON to `Application.persistentDataPath/bonsai_save.json`; `SaveData` + `SaveNode` serializable classes capture all `TreeNode` fields + GameManager time + skeleton live state; `LoadFromSaveData()` on `TreeSkeleton` clears and rebuilds tree, re-spawns wounds, rebuilds mesh, restores leaves via `LeafManager.ForceSpawnLeaves`; auto-save fires each September (BranchGrow→TimeGo); manual Save button in Settings → Time tab with timestamp feedback (`SaveManager.cs`, `TreeSkeleton`, `LeafManager`, `buttonClicker`, `ButtonUI.uxml`)
