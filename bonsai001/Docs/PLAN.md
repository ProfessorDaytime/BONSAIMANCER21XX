# BONSAIMANCER — Development Plan

Last updated: 2026-03-21

---

## Current Priority Queue

Work through these in order. Do not start the next item until the current one is
shippable (playable without obvious breakage).

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

## Backlog (not yet scheduled)

These are captured but intentionally deferred until the priority queue above
is complete and multiple growing seasons are stable in testing.

### Camera & Input
- Full Unity Input System migration (currently legacy `Input.*`)
- Wire animation skip key will be revisited as part of this

### Watering System
- Watering can tool, soil moisture level, drain rate
- Feeds into `NodeHealth` via `Drought` damage type
- Nutrient concentration affects growth rate and recovery speed

### Tree Species
- Different growth parameters, leaf shapes, seasonal colours
- Species chosen at game start

### Nebari Development
- Visible surface root flare (nebari) scoring and shaping tools
- Encourage roots to hug the soil surface and radiate outward evenly
- Player feedback: nebari score / quality rating visible in UI
- Tied to spread radius system already in place

### Ishitsuki (Root-over-Rock)
- Depends on: rock/prop placement system (not yet built)
- Player places a rock near the trunk; roots detect it and route around/over it
- Roots grip crevices in the rock surface over multiple seasons
- Visual: roots partially embed into rock geometry over time

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
