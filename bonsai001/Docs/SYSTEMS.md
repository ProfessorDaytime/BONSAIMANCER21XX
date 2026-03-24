# BONSAIMANCER — Systems Reference

Last updated: 2026-03-22

A concise technical reference for every implemented game system. Read this
before touching a system's code — each entry explains the mental model, the
key data flow, and the things most likely to go wrong.

---

## Table of Contents

1. [Seasonal Time & Game State](#1-seasonal-time--game-state)
2. [Tree Skeleton](#2-tree-skeleton)
3. [Tree Mesh Builder](#3-tree-mesh-builder)
4. [Tree Interaction](#4-tree-interaction)
5. [Wire System](#5-wire-system)
6. [Health System](#6-health-system)
7. [Leaf System](#7-leaf-system)
8. [Post-Trim Regrowth Cap](#8-post-trim-regrowth-cap)
9. [Camera Orbit](#9-camera-orbit)
10. [Root System](#10-root-system)

---

## 1. Seasonal Time & Game State

**Files:** `GameManager.cs`

### What it does
GameManager owns the in-game calendar and the global state machine. Every
other system subscribes to `OnGameStateChanged` and reacts accordingly —
nothing polls `GameManager.state` directly except UI.

### Calendar
- Time advances in `CalculateTime()`, called only when the state is one of
  `BranchGrow | LeafGrow | LeafFall | TimeGo`.
- Rate is `TIMESCALE` in-game hours per real second (default 200).
- `TIMESCALE` is tunable at runtime with A/D keys (1–400).
- One month = 28 days. Year wraps through `SetMonthText()`.
- **Winter skip:** when November arrives the calendar jumps straight to
  February of the next year. This keeps seasons snappy without sitting
  through a dead winter.

### Seasonal growth rate
`SeasonalGrowthRate` returns a 0–1 multiplier that gates all organic
processes (branch growth, wire progress, leaf colour). It is 0 outside
March–August. Systems multiply their per-day deltas by this value so they
automatically go dormant in winter without special-casing.

### State machine

| State | Purpose |
|---|---|
| `TipPause` | Startup hold before any game begins |
| `Water` | Player waters the tree; triggers `InitTree` on first water |
| `BranchGrow` | Spring/summer growing season; tree grows, wire sets |
| `LeafGrow` | Leaf spawn phase (currently folded into BranchGrow) |
| `TimeGo` | Autumn before leaf fall; time ticks, nothing grows |
| `LeafFall` | Leaves turn and drop stochastically |
| `Pruning` | Player trims branches |
| `Shaping` | Player wires branches |
| `Wiring` | Wire aim sub-state (direction preview) |
| `WireAnimate` | Time frozen; spring-bend animation playing |
| `GamePause / Idle` | Reserved |

States are set by UI buttons. `OnGameStateChanged` fires a C# `Action<GameState>`
event that all interested systems subscribe to on `OnEnable`.

### Gotchas
- `canTrim`, `canWire`, `canRemoveWire` are static flags set by the UI/tool
  layer. Systems read them — GameManager does not set them.
- Time does **not** advance during `Wiring` or `WireAnimate`; these are
  intentionally absent from the `CalculateTime` guard.

---

## 2. Tree Skeleton

**Files:** `TreeSkeleton.cs`, `TreeNode.cs`

### What it does
TreeSkeleton owns the tree graph (a `TreeNode` DAG), drives growth every
frame during `BranchGrow`, and exposes mutation APIs (trim, wire, plant root)
to other systems. It never touches meshes directly — it calls
`meshBuilder.SetDirty()` whenever the geometry changes.

### TreeNode data model
Each node represents **one cylindrical branch segment**. Key fields:

| Field | Meaning |
|---|---|
| `worldPosition` | Base of the segment in tree-local space |
| `growDirection` | Normalised unit vector toward the tip |
| `length` | Current length (grows toward `targetLength`) |
| `radius` / `tipRadius` | Radii at base and tip (pipe model) |
| `depth` | 0 = trunk base, increases toward tips |
| `isGrowing` | True while length < targetLength |
| `isTrimmed` | Removed nodes stay in memory briefly for event delivery |
| `health` | 0–1; thresholds gate growth speed and dormancy |

`tipPosition` and `tipRadius` are computed properties — they always reflect
current state without extra bookkeeping.

### Growth loop
Each frame during `BranchGrow`:
1. Snapshot all `isGrowing && !isTrimmed` nodes.
2. For each: advance `length` by `baseGrowSpeed × rate × depthDecay × healthMult`.
3. When `length >= targetLength` and below both depth caps → `SpawnChildren`.
4. Run wire progress accumulation (see §5).
5. If structure changed → `RecalculateRadii`. If anything moved → `SetDirty`.

### Depth caps
Two independent caps gate spawning:

- **SeasonDepthCap** — global: `(year - startYear + 1) × depthsPerYear`.
  Grows by `depthsPerYear` each calendar year, representing gradual tree
  maturity.
- **CutPointDepthCap** — per-subtree: `trimCutDepth + regrowthSeasonCount × depthsPerYear`.
  Applied after heavy pruning (see §8). Walks up the parent chain to find
  the nearest active cut point.

A node spawns children only when `node.depth` is below **both** caps.

### Pipe model (`RecalculateRadii`)
Bottom-up traversal applying da Vinci's rule: `parent.radius² = Σ child.radius²`.
Terminal radii (`terminalRadius`) are the source of truth. As the tree grows
more branches the trunk automatically thickens. `minRadius` prevents any node
from ever shrinking below its historical maximum (past wire trauma or pruning
can't un-thicken a trunk).

### Branching directions
- **Continuation:** inertia + phototropism (upward) + random nudge.
- **Lateral:** random azimuth around parent axis + upward bias + splay angle.
- Both honour the inspector weights `inertiaWeight`, `phototropismWeight`,
  `randomWeight`, `branchAngleMin/Max`.

### Key APIs
| Method | Who calls it |
|---|---|
| `TrimNode(node)` | TreeInteraction |
| `WireNode(node, dir)` | TreeInteraction |
| `UnwireNode(node)` | TreeInteraction |
| `PlantRoot(dir)` | TreeInteraction (root mode) |
| `ApplyDamage(node, type, amount)` | Any system |
| `PropagatePosition(node)` | TreeInteraction (wire animation) |
| `RotateAndPropagateDescendants(node, rot, origDirs)` | TreeInteraction (wire anim — rotates whole subtree) |
| `SetDirty()` | Internal + any system that moves nodes |

### Gotchas
- `allNodes` is a flat list. Never remove from it mid-iteration — snapshot
  first, then remove.
- Root nodes (if planted) are children of `skeleton.root` in the same graph.
  They use `isRoot = true` to opt out of the branch depth cap.
- `RecalculateRadii` is not free — only call it after structural changes
  (spawning, trimming), not every frame.

---

## 3. Tree Mesh Builder

**Files:** `TreeMeshBuilder.cs`

### What it does
Reads the TreeSkeleton graph and builds **one unified mesh** for the entire
tree (trunk + all branches). A single mesh means junction vertices are shared
between parent and child segments; `RecalculateNormals()` then averages all
contributing face normals at each shared vertex, giving smooth shading across
every junction for free.

### Build cycle
`SetDirty()` raises a flag. On the next `LateUpdate`, `BuildMesh()` runs:
`vertices / triangles / uvs` lists are cleared, `ProcessNode` is called
recursively from the root, then the Unity `Mesh` object and `MeshCollider`
are refreshed atomically.

Building only on dirty avoids the cost of rebuilding every frame during
camera orbit or idle time.

### Ring-based geometry
Each node is a tapered cylinder. `AddRing()` appends `ringSegments + 1`
vertices in a circle perpendicular to the node's axis. The extra vertex
duplicates the first with U=1.0 so the UV seam is clean.

Two rings per node (base + tip) are connected by a quad strip. The base ring
is **inherited** from the parent's tip ring (shared vertex indices), which is
what makes normals smooth at junctions.

### Parallel transport
A `frameRight` vector is threaded through the recursion. At each level it is
projected onto the new axis to keep the angular ordering consistent around the
circumference. Without this the quad strip would develop a helical twist at
bends.

### Bend rings
When the angle between a parent and child `growDirection` exceeds
`BEND_THRESHOLD_DEG` (20°), intermediate rings are inserted at the base of
the child in the first 15% of its length. Each ring Slerps between the parent
and child directions; `bendFrame` is parallel-transported through each
intermediate step. **After the bend ring loop, `frameRight` is updated to
`bendFrame` projected onto `axisUp`** — this is critical; without it the final
quad strip to the tip ring twists visibly.

### Triangle range mapping
`triRanges` maps `(triStart, triEnd, TreeNode)` for every triangle strip.
`NodeFromTriangleIndex()` binary-searches this list to find which node a
raycast hit. Used by TreeInteraction for click detection.

### Gotchas
- Mesh is rebuilt entirely on every dirty — there is no incremental update.
  Keep `SetDirty()` calls to structural changes only.
- `meshCollider.sharedMesh = null` then re-assign is the correct Unity pattern
  for forcing collider refresh.
- `renderRoots` flag (when root system is active) gates whether root-flagged
  nodes are included in the build pass.

---

## 4. Tree Interaction

**Files:** `TreeInteraction.cs`

### What it does
Translates mouse input into tree operations (trim, wire, root placement) based
on the current game state. Builds and maintains a highlight overlay mesh that
previews the operation before the player commits.

### Interaction modes

| GameManager flag | Mode | Behaviour |
|---|---|---|
| `canTrim` | Trim | Hover → red subtree highlight. Click → `TrimNode`. |
| `canWire` | Wire | Hover → gold single-node highlight. Click → aim phase. |
| `canRemoveWire` | Unwire | Hover wired nodes → green. Click → `UnwireNode`. |
| `canRootWork` | Root | Hover roots → red trim highlight. Click soil plane → plant root. |

### Wire aim state machine
```
Idle
  └─ [click wirable node] → WirePhase.Aiming
       Mouse aims direction preview arrow.
       Left-click confirms  → WirePhase.Animating (WireAnimate game state)
       Right-click/Escape   → cancel, restore prior state.
            └─ Spring animation plays 0.6 s.
               Enter skips it.
               End → WirePhase.None, restore prior game state.
```

### Highlight mesh
A child `GameObject` ("_TreeHighlight") holds a separate mesh rebuilt each
hover frame. `BuildSubtreeNode` mirrors the branch mesh builder (without bend
rings) to produce a slightly enlarged overlay that avoids z-fighting via
`highlightRadiusBias`. Colour is set per mode on the shared material.

### Gotchas
- `WirePhase.Animating` blocks all other interaction until complete.
  Enter-to-skip is the only escape.
- Aim preview uses a `LineRenderer` on a separate child object; it's enabled
  only during `WirePhase.Aiming`.
- Root trim reuses the same `TrimSubtree` highlight colour as branch trim.
  Root nodes are distinguished by `node.isRoot`.

---

## 5. Wire System

**Files:** `TreeSkeleton.cs` (logic), `TreeInteraction.cs` (interaction),
`WireRenderer.cs` (visuals)

### What it does
Allows the player to bend branches by attaching a wire that holds a new
direction. The bend is immediate (skeleton snaps) but animated visually.
Progress is tracked over growing seasons; leaving a wire on too long damages
the branch.

### Data on TreeNode

| Field | Meaning |
|---|---|
| `wireOriginalDirection` | `growDirection` at the moment of wiring |
| `wireTargetDirection` | Player-aimed direction |
| `wireSetProgress` | 0→1; wood lignifying in the new position |
| `wireDamageProgress` | 0→1; accumulates after fully set |
| `wireAgeDays` | Total rate-adjusted in-game days wire has been on |

### Wire lifecycle
1. **Confirm aim** (`TreeInteraction.ConfirmWire`): captures old direction,
   calls `skeleton.WireNode()`, starts `WirePhase.Animating`.
2. **WireNode**: records `wireOriginalDirection`, sets `wireTargetDirection`,
   zeroes progress fields. **Does not change `growDirection`** — the animation
   drives it.
3. **Spring animation** (0.6 s): drives `node.growDirection` from original to
   target through a damped cosine spring (`WireSpringCurve`), calling
   `PropagatePosition + SetDirty` each frame. Enter skips to final position.
4. **Growing season** (`BranchGrow`): each frame advances `wireSetProgress`
   at rate `inGameDays × SeasonalGrowthRate / wireDaysToSet` (~196 days = 2
   seasons to fully set). Once set, `wireDamageProgress` begins climbing at
   the same pace while `ApplyDamage(WireDamage)` fires continuously.
5. **Unwire** (`UnwireNode`): if `setProgress < 1`, branch springs back to
   `Slerp(original, target, setProgress)`. If fully set, direction is
   permanent.
6. **Re-wiring** set wood: immediate `WireBend` health hit =
   `Lerp(0.05, 0.25, setProgress)`.

### Wire colour (WireRenderer)
| Condition | Colour |
|---|---|
| `setProgress` 0→1 | Silver, brightening |
| `setProgress == 1, damage < 0.01` | Gold pulse (Mathf.Sin oscillation) |
| `damage` 0→0.5 | Gold → orange |
| `damage` 0.5→1 | Orange → deep red |

`WireRenderer.Update` calls `UpdateHelix` every frame for each wired node.
Each wire has its own instantiated `Unlit/Color` material; colour is set via
`lr.material.color` (safe after first access).

### Gotchas
- `wireDaysToSet = 196f` is the Inspector field. This is rate-adjusted days,
  not real-time days — a dormant winter doesn't advance it at all.
- `WireSpringCurve(t) = 1 - e^(-5t) × cos(2πt × 1.5)` overshoots ~15 % at
  t ≈ 0.5 then settles.
- Wire geometry (helix) and wire logic (progress) are completely separate;
  the renderer reads node data but never writes it.

---

## 6. Health System

**Files:** `NodeHealth.cs` (enum), `TreeSkeleton.cs` (application)

### What it does
Each `TreeNode` has a `health` float (0–1, default 1). Damage sources reduce
it; it does not currently recover on its own (recovery mechanics are reserved
for the watering/nutrient systems).

### Damage types (`DamageType` enum)

| Type | Source | Amount |
|---|---|---|
| `WireBend` | Re-wiring already-set wood | `Lerp(0.05, 0.25, setProgress)` |
| `WireDamage` | Wire left on too long | `dmgDelta × 0.5` per frame |
| `TrimTrauma` | Reserved — small hit on prune | not yet wired |
| `Drought` | Reserved — unwatered soil | not yet wired |
| `NutrientLack` | Reserved — reduces recovery | not yet wired |

### Health thresholds

| Threshold | Effect |
|---|---|
| `>= 0.75` | Full growth speed |
| `< 0.75` | Growth speed multiplied by `health` (linear slowdown) |
| `< 0.25` | Branch dormant — skipped entirely in growth loop |
| `<= 0` | Dead — no growth; mesh stays until player trims it |

### API
`skeleton.ApplyDamage(node, DamageType, amount)` — reduces `node.health` by
`amount`, clamped to 0. All damage goes through this single chokepoint so
future balance passes only touch one function.

### Gotchas
- Health persists through seasons (no automatic recovery yet).
- Dead branches (`health <= 0`) remain in `allNodes` and in the mesh.
  The player must trim them to remove them.
- Leaf system does not yet react to health — early leaf drop at `< 0.5` is
  planned but not implemented.

---

## 7. Leaf System

**Files:** `LeafManager.cs`, `Leaf.cs`

### What it does
Manages the full lifecycle of leaf instances: spring spawn, summer position
tracking (so leaves follow wire-bent branches), autumn colour progression,
and stochastic fall animation.

### LeafManager responsibilities

**Spring (`BranchGrow` state):**
- Each frame, scans `skeleton.allNodes` for terminal nodes at `depth >= minLeafDepth`
  with no existing cluster.
- `SpawnCluster()` instantiates `leavesPerNode` prefab copies, parented to
  the tree transform, scattered within `clusterRadius` of `node.tipPosition`.
- Uses `GetComponent<Leaf>() ?? AddComponent<Leaf>()` — never unconditional
  `AddComponent`, which would create a second `Leaf` on prefabs that already
  have one (the two components fight each other via competing `localPosition`
  writes each frame).
- Assigns `leaf.ownerNode`, `leaf.tipOffset`, `leaf.targetScale`.
- Assigns the **shared material** instance to every renderer in the prefab
  so a single `_Color` update drives all leaves at O(1).

**Autumn (`LeafFall` state):**
- Assigns a randomised `fallColorSpeed` (0.4–2.2×) per leaf via
  `leaf.StartLeafFallSeason()`. Fast leaves turn brown early; slow ones
  linger at red.
- Each frame, `RollLeafFall()` iterates every live leaf and rolls a fall
  probability = `baseFallChancePerDay × inGameDays × Lerp(0.2, 3.0, colorProgress)`.
  Fully browned leaves are 15× more likely to fall than freshly-turned ones.

**Cleanup:**
- `CleanupOrphanedLeaves()` runs at spring start; removes clusters from nodes
  that were trimmed or gained children (no longer terminal).
- `OnSubtreeTrimmed` receives the removed-node list from TreeSkeleton and
  calls `StartFalling()` on those leaves immediately (they don't just vanish).

### Leaf responsibilities

Each `Leaf` MonoBehaviour on a live leaf instance:
- **Spring scale-in:** `SmoothStep(0, 1, timer / SCALE_DURATION)` over 1.5 s.
- **Position tracking:** `transform.localPosition = ownerNode.tipPosition + tipOffset`
  every frame while not falling — keeps leaves glued to wire-bent branches.
- **Colour:** `UpdateLeafColor()` samples a 5-stop gradient
  (green → yellow → orange → red → brown) via `FallColorProgress`.
- **Fall animation:** on `StartFalling()`, detaches from parent
  (`SetParent(null, worldPositionStays: true)`), sets random drift velocity
  and spin, and destroys itself after 5 s.

### Gotchas
- The flat `allLeaves` list exists only to avoid Dictionary enumeration
  inside `RollLeafFall`. It is rebuilt lazily (`listDirty` flag) rather than
  every frame.
- `Rigidbody` on leaf prefabs is forced kinematic in `Leaf.Start()` so the
  physics engine doesn't fight the manual position updates.
- `FallColorProgress` is driven by in-game days × `fallColorSpeed` / `FALL_COLOR_DAYS`
  (25 days). At `SeasonalGrowthRate = 1.0` and `TIMESCALE = 200`, one in-game
  day is ~7 real seconds, so a speed-1 leaf fully browns in ~3 real minutes.

---

## 8. Post-Trim Regrowth Cap

**Files:** `TreeSkeleton.cs`, `TreeNode.cs`

### What it does
Prevents a heavily pruned branch from immediately regrowing to full depth in
a single season. A fresh trim stump is allowed to push only `depthsPerYear`
new depth levels per growing season — the same pacing as year-one growth.

### Data on TreeNode

| Field | Meaning |
|---|---|
| `isTrimCutPoint` | True if this node is an exposed trim stump |
| `trimCutDepth` | `node.depth` at the moment of the cut |
| `regrowthSeasonCount` | Growing seasons elapsed since the cut |

### Lifecycle
1. **`TrimNode(B)`**: if B's parent `A` becomes terminal, mark `A` with
   `isTrimCutPoint = true`, `trimCutDepth = A.depth`, `regrowthSeasonCount = 0`.
   Re-cutting resets the counter.
2. **`StartNewGrowingSeason`**: increments `regrowthSeasonCount` on all active
   cut points. If `trimCutDepth + regrowthSeasonCount × depthsPerYear >= SeasonDepthCap`
   the restriction has "caught up" to the global cap and `isTrimCutPoint` is
   cleared.
3. **Depth check** (`CutPointDepthCap`): walks ancestry to find the nearest
   cut point, returns `trimCutDepth + regrowthSeasonCount × depthsPerYear`.
   Used in both the real-time `Update` growth loop and `SimulateYear`.

### Example
Tree with `depthsPerYear = 3`, global `SeasonDepthCap = 12`:
- Cut at depth 4 → cap starts at `4 + 0×3 = 4` (no growth this season).
- Season 1: `4 + 1×3 = 7` (three new levels allowed).
- Season 2: `4 + 2×3 = 10`.
- Season 3: `4 + 3×3 = 13 >= 12` → cut point cleared; full global cap applies.

---

## 9. Camera Orbit

**Files:** `CameraOrbit.cs`

### What it does
Orbits the camera around a target transform (the tree pivot) using spherical
coordinates. Responds to LMB drag, scroll wheel, and MMB drag. Designed to
not interfere with UI or tree interaction clicks.

### Controls

| Input | Effect |
|---|---|
| LMB drag on empty space | Orbit (yaw + pitch) |
| Scroll wheel | Zoom (proportional to current radius) |
| MMB drag up/down | Pan camera Y axis |

### Click filtering
Rather than `EventSystem.IsPointerOverGameObject()` (which returns true for
any panel with Raycast Target enabled, including invisible backgrounds),
`IsPointerOverInteractableUI()` uses `EventSystem.RaycastAll()` and checks
whether any hit has a `Selectable` component in its parent chain. Only actual
interactive elements (buttons, toggles, sliders) block orbit dragging.

Physics raycasts additionally block orbit if the cursor is over any collider
(tree mesh, future interactables) so orbit doesn't accidentally fire when
the player is clicking a branch.

### Zoom
`radius -= scroll × zoomSpeed × radius` — proportional zoom keeps the feel
consistent at any distance (small nudges near the tree, bigger jumps far away).

### Y-pan
`panY` is an offset added on top of `target.position` when computing the
orbit pivot. Moving the tree transform itself is not needed; the offset
preserves the tree's actual position data.

### Spherical coordinates
State is `(yaw, pitch, radius)` initialised from the camera's existing
position in `Start()` to prevent a startup jump. `ApplyOrbit()` converts to
Cartesian and calls `LookAt(pivot)`.

### Gotchas
- `pitchMin` (default 5°) prevents the camera from going below horizontal.
  In `RootPrune` mode this is relaxed to allow viewing roots from below.
- Drag start is blocked if cursor is over interactive UI **or** over any
  collider. This prevents accidental orbit when trimming or wiring.
- MMB pan and LMB orbit are mutually independent state flags — both can
  technically be true simultaneously without conflict.
- Drag start is additionally blocked during `Wiring` and `WireAnimate` states
  so the wire confirm click doesn't accidentally start a camera orbit.

---

## 10. Root System

**Files:** `TreeSkeleton.cs`, `TreeNode.cs`, `TreeMeshBuilder.cs`,
`TreeInteraction.cs`, `CameraOrbit.cs`

### What it does
Roots are surface and subsurface root strands (nebari) that grow outward and
downward from the trunk base. They are part of the same `TreeNode` DAG as
branches — no separate data structure — but are gated by `isRoot = true`,
which opts them out of the branch depth cap and gives them gravity-biased
growth directions. The pipe model naturally thickens the trunk base from root
radii, so a well-developed root system produces a visually flared trunk for
free.

Roots are normally invisible. They are revealed (and the tree is lifted off
the ground) when the player enters `RootPrune` mode, which is the only time
the player can plant or trim them.

### Data on TreeNode

| Field | Meaning |
|---|---|
| `isRoot` | True for all nodes belonging to a root strand |

Root nodes inherit all other `TreeNode` fields (`health`, `isTrimmed`,
`hasWire`, etc.) and participate in the same wire and trim mechanics as
branches. The only structural differences from branch nodes are the depth cap
and direction biases described below.

### Root growth
Root nodes are children of `skeleton.root` (the trunk base node) in the normal
`TreeNode` graph. Growth, branching, and radii are driven by the same
`Update()` loop as branches. The differences are:

**Direction biases:**
- **Continuation:** `inertia + Vector3.down × rootGravityWeight + randomNudge`
  — gravity replaces phototropism, so roots push outward and downward.
- **Lateral sub-roots:** same splay logic as branch laterals, but with a
  downward bias instead of an upward one.

**Lateral chance decay:** `rootLateralChance × 0.7^depth` — deep roots
branch less frequently than shallow ones.

**Depth cap:** `node.depth < maxRootDepth` (Inspector field, default 5),
independent of `SeasonDepthCap`. Root strands ignore the global branch cap
entirely.

### PlantRoot
`PlantRoot(Vector3 localDir)` is called by `TreeInteraction` when the player
clicks the soil plane in `RootPrune` mode:
1. Adds `rootInitialPitch` downward component to `localDir` before normalising
   so the root immediately heads into the soil.
2. Calls `CreateNode` with `isRoot = true` as a child of `skeleton.root`.
3. Calls `RecalculateRadii` + `SetDirty`.

### RootPrune mode
Entering `GameState.RootPrune` triggers two changes in `TreeSkeleton.OnGameStateChanged`:
- `liftTarget = rootLiftHeight` (default 3.5 units) — the tree's transform
  Y-position animates upward so the root system is visible above the soil.
- `meshBuilder.renderRoots = true` — root nodes are included in the next mesh
  build (they are skipped otherwise).

The lift is animated via `Mathf.MoveTowards` in `Update()` at `rootLiftSpeed`
(default 4 units/s), running independently of `isGrowing` so it works in any
game state. Exiting `RootPrune` reverses both: `liftTarget = 0`, `renderRoots = false`.

### Mesh rendering
`TreeMeshBuilder.renderRoots` (default `false`, `[HideInInspector]`) is
checked in the `ProcessNode` child loop:

```
foreach (var child in node.children)
{
    if (child.isRoot && !renderRoots) continue;
    ProcessNode(child, ...);
}
```

When `false`, root nodes and all their descendants are simply skipped during
the build pass. The mesh collider is therefore also root-free, meaning raycasts
won't hit roots outside of `RootPrune` mode.

### Interaction (RootPrune mode)
`HandleRootWorkHover()` runs when `GameManager.canRootWork = true`:

- **Trim existing root:** raycast tree mesh → if hit node has `isRoot = true`
  → red subtree highlight → click calls `skeleton.TrimNode(node)` (same path
  as branch trimming).
- **Plant new root:** raycast a horizontal `Plane` at world `y = 0` (the soil
  surface) → click computes an outward direction from the trunk base to the
  click point, converts to local space, and calls `skeleton.PlantRoot(localDir)`.

### Camera pitch in RootPrune
`CameraOrbit` subscribes to `OnGameStateChanged`. When `RootPrune` is entered,
`activePitchMin` is set to `pitchMinRootPrune` (default −30°), allowing the
player to tilt the camera below horizontal to look up at the underside of the
lifted root system. On exit, `activePitchMin` reverts to `pitchMin` (default 5°).

### Pipe model interaction
Root nodes participate in `RecalculateRadii` exactly like branch nodes. Since
they are children of `skeleton.root` (the trunk base), their radii propagate
up into the trunk via da Vinci's rule. A multi-strand, deep root system will
noticeably thicken the base of the trunk compared to a bare-rooted tree.

### Inspector fields (TreeSkeleton)

| Field | Default | Meaning |
|---|---|---|
| `maxRootDepth` | 5 | Maximum node depth for root strands |
| `rootLateralChance` | 0.35 | Base probability of a lateral sub-root per segment |
| `rootGravityWeight` | 0.30 | Downward pull on continuation direction |
| `rootInitialPitch` | 0.25 | Downward Y bias added when planting a fresh root |
| `rootLiftHeight` | 3.5 | World-units the tree lifts in RootPrune mode |
| `rootLiftSpeed` | 4.0 | Lift animation speed (units/s) |

### Gotchas
- `canRootWork` is set by the UI/tool layer, not by `GameManager` internally.
  Without it being set to `true`, root interaction never activates even if the
  state is `RootPrune`.
- `PlantRoot` uses world `y = 0` for the soil plane. If the scene's ground is
  not at world origin, `SOIL_WORLD_Y` in `TreeInteraction` needs adjusting.
- Root nodes ARE trimmable through the standard `TrimNode` path. Trimming a
  root near the trunk base removes the whole strand and immediately reduces
  the trunk's base radius via `RecalculateRadii`.
- Roots are never wirable in the current implementation (no UX surfaces it),
  but the data fields exist on `TreeNode` and the skeleton APIs would accept
  them if needed later.
