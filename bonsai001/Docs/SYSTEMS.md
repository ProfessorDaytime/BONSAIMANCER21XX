# BONSAIMANCER — Systems Reference

Last updated: 2026-04-23 (added §42 Sibling Branch Fusion, §41 rock size updated, §43 Bark Texture System added)

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
11. [Bud System](#11-bud-system)
12. [Wound System](#12-wound-system)
13. [Nebari Scoring](#13-nebari-scoring)
14. [Air Layer System](#14-air-layer-system)
15. [Pot-Bound Root Pressure](#15-pot-bound-root-pressure)
16. [Ishitsuki (Root-over-Rock)](#16-ishitsuki-root-over-rock)
17. [Pause & Settings Menu](#17-pause--settings-menu)
18. [Bark Shader System](#18-bark-shader-system)
19. [Bark Flaker Manager](#19-bark-flaker-manager)
36. [Play Mode Manager](#36-play-mode-manager)
37. [Calendar System (3-Tab Overlay)](#37-calendar-system-3-tab-overlay)
38. [Calendar Scheduling (Parts 1–4)](#38-calendar-scheduling-parts-14)
39. [Autosave System](#39-autosave-system)
40. [Repot Root Raking](#40-repot-root-raking)
41. [Pot Size Selection](#41-pot-size-selection)
42. [Sibling Branch Fusion](#42-sibling-branch-fusion)
43. [Bark Texture System](#43-bark-texture-system)

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
- **`OnMonthChanged`** — `static event Action<int>` fired at end of `SetMonthText` each time the month changes. Drives month-triggered tutorials (e.g. April ramification) without polling.

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
| `RootPrune` | Tree lifted, root mesh visible, root trim/placement active |
| `RockPlace` | Rock grabbed and being positioned in 3D space |
| `TreeOrient` | Tree transform being rotated onto the placed rock |
| `GamePause / Idle` | Reserved |

`canRootWork` is set to `true` for `RootPrune`, `RockPlace`, and `TreeOrient`.
It gates root interaction in `TreeInteraction` and rock/tree input in `RockPlacer`.

`preRootPruneState` saves whatever state was active before entering root mode
so `ToggleRootPrune()` and `ConfirmRockOrient()` can restore the calendar.
If the saved state was not a time-ticking state (e.g. the player entered roots
from `Idle`), the restore falls back to `LeafFall` to keep the calendar moving.

States are set by UI buttons. `OnGameStateChanged` fires a C# `Action<GameState>`
event that all interested systems subscribe to on `OnEnable`.

### Speed mode
`SpeedMode` enum (`Slow`, `Med`, `Fast`). `ToggleSpeed()` cycles Slow→Med→Fast→Slow. Speed button label: ▶ / ▶▶ / ▶▶▶ with amber/grey/green tints. `IsSlowSpeed` kept as a back-compat property (`=> CurrentSpeed == SpeedMode.Slow`). Auto-slow fires in **April** (`month == 4`) — the pinching window.

**Mutable timescale statics:** `TIMESCALE_SLOW`, `TIMESCALE_MED`, `TIMESCALE_FAST` are `public static float` (not `const`). Defaults: 0.5 / 10 / 200 game-hrs/real-sec. Values are loaded from `PlayerPrefs` (`ts_slow/med/fast`) in `Awake()` via `LoadTimescalePrefs()`, which also enforces ordering (`Slow < Med < Fast`). `SaveTimescalePrefs()` writes them back. The Calendar Speed Config tab is the primary UI for changing them. `SetSpeedMode()` reads the mutable values so all speed changes automatically use the player's configured ratios.

`PlayModeManager` (see §36) calls `gm.SetSpeedMode()` each frame; the speed button still works as a manual override — `PlayModeManager` will override it back next frame if a rule applies.

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

### Phototropism coordinate space
`SunDirection()` returns `transform.InverseTransformDirection(Vector3.up)` — world up converted to tree-local space. `growDirection` is tree-local, so the phototropism Slerp target must also be local. When the tree is upright this is identical to `Vector3.up`; when tilted on a rock the difference is significant. **Do not return world `Vector3.up` directly** — the Slerp between local inertia and world up produces incorrect growth angles.

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
- **`RecalculateRootHealthScore` NaN guard:** if all root nodes have `radius == 0` the centre-of-mass calculation divides by `totalRadius == 0` → NaN → `Mathf.RoundToInt(NaN)` = `int.MinValue` = −2147483648. The method early-returns when `count == 0 || totalRadius <= 0f`. The UI additionally guards against NaN/Infinity before displaying the value.

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
| `canPinch` | Pinch | Hover → lime single-node highlight + GL diamond marker. Click → `PinchNode`. |
| `canDefoliate` | Defoliate | Hover any non-root node with a leaf cluster → amber highlight. Click → `DefoliateNode`. Note: filter does **not** require `isTerminal` — by June most leaf nodes have branched. |

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

### Pinch tip markers
`DrawPinchMarkers` is registered to `RenderPipelineManager.endCameraRendering`. When `GameManager.canPinch` is true, it iterates `skeleton.allNodes` and draws a GL octahedron (`DrawGLDiamond`) at each eligible tip's `tipPosition` in world space:
- **All pinchable tips:** dim lime-green, radius 0.055 m — shows the player where to aim.
- **Hovered tip** (`hoveredPinchNode`): bright lime-green, radius 0.12 m — confirms what will be pinched.

`hoveredPinchNode` is cleared to `null` at the top of `Update()` each frame and re-set inside `HandlePinchHover` if a node is under the cursor. `DrawGLDiamond` draws a camera-facing 3-axis octahedron (6 vertices, 12 edge pairs) so markers are readable from all angles.

### Gotchas
- `WirePhase.Animating` blocks all other interaction until complete.
  Enter-to-skip is the only escape.
- Aim preview uses a `LineRenderer` on a separate child object; it's enabled
  only during `WirePhase.Aiming`.
- Root trim reuses the same `TrimSubtree` highlight colour as branch trim.
  Root nodes are distinguished by `node.isRoot`.
- `DrawPinchMarkers` / `DrawGraftLines` / `DrawSelectionCircle` are all registered to `endCameraRendering`, not `OnRenderObject` — URP does not invoke `OnRenderObject` on components.

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

### OnWireSetGold event
`public event Action OnWireSetGold` on `TreeSkeleton`. Fired the first frame any node's `wireSetProgress` crosses from < 1 to ≥ 1 during the wire accumulation loop. `buttonClicker` subscribes and calls `MaybeShowTooltip("removewire", ...)` — the unwire tutorial fires exactly once, on the first wire that goes gold, regardless of which button the player pressed.

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

### Idle orbit
When `PlayModeManager.Instance.IdleOrbitActive` is true, the camera enters a slow automated orbit:
- On the first orbit frame, current `(yaw, pitch, radius, panY)` are saved to `savedOrbit*` fields.
- Each frame: `yaw += OrbitYawSpeed (4°/s) × unscaledDeltaTime`.
- Pitch drifts ±`OrbitElevAmpl` (5°) on a sine wave with period `OrbitElevPeriod` (20 s real).
- `Update()` returns early after `ApplyOrbit()` — normal drag input is not processed.
- When `IdleOrbitActive` becomes false (any input in `PlayModeManager`), the saved state is restored in one frame and `orbitStateSaved` is cleared.

All orbit motion uses `Time.unscaledDeltaTime` so it runs regardless of `Time.timeScale`.

### Gotchas
- `pitchMin` (default 5°) prevents the camera from going below horizontal.
  In `RootPrune` mode this is relaxed to allow viewing roots from below.
- Drag start is blocked if cursor is over interactive UI **or** over any
  collider. This prevents accidental orbit when trimming or wiring.
- MMB pan and LMB orbit are mutually independent state flags — both can
  technically be true simultaneously without conflict.
- Drag start is additionally blocked during `Wiring` and `WireAnimate` states
  so the wire confirm click doesn't accidentally start a camera orbit.
- Idle orbit snap-back is instantaneous (one frame). There is intentionally no
  smooth transition — the user just interacted, so the camera should be exactly
  where they left it.

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
| `isAirLayerRoot` | Root spawned from an unwrapped air layer; always renders, scales with trunk |
| `isTrainingWire` | Pre-grown Ishitsuki cable; always renders, exempt from depth cull |
| `boundaryPressure` | Seasons spent near a tray wall; drives thickening and growth slowdown |

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
- **Root containment hard clamp:** in `SpawnChildren`, before processing a root terminal, if `distRatio >= 1.3f` the terminal is skipped. Additionally, if the tip is already outside the side or bottom faces of `rootAreaTransform`, `isTrimmed = true` is set permanently. Top-face escape (surface roots rising above soil) is intentionally left alone. Ishitsuki training-wire roots are exempt from all pot-boundary checks.
- Root nodes ARE trimmable through the standard `TrimNode` path. Trimming a
  root near the trunk base removes the whole strand and immediately reduces
  the trunk's base radius via `RecalculateRadii`.
- Roots are never wirable in the current implementation (no UX surfaces it),
  but the data fields exist on `TreeNode` and the skeleton APIs would accept
  them if needed later.

---

## 11. Bud System

**Files:** `TreeSkeleton.cs`, `TreeNode.cs`

**Full doc:** `Docs/BudSystem.md`

### What it does
Terminal buds are set each August and are visible through winter as bud
prefab GameObjects. In March they break — growth resumes from those nodes.
Trimming any branch stimulates back-budding on up to 3 ancestors, giving
the player a way to push growth lower. Old-wood budding provides a small
spontaneous lateral chance on all interior nodes each spring.

### Key data
| Field | Meaning |
|---|---|
| `hasBud` | Terminal bud set this August; activates next March |
| `backBudStimulated` | Nearest 3 ancestors of a trim cut; boosted lateral chance next spring |

### Flow
```
August → SetBuds() → hasBud = true, budPrefab spawned
March  → StartNewGrowingSeason() → bud objects destroyed, growth resumes
TrimNode() → 3 ancestors get backBudStimulated = true
StartNewGrowingSeason() → stimulated nodes roll backBudBaseChance × boost
```

### Pinching and the bud system
`PinchNode()` stops tip extension by setting `node.length = node.targetLength` and `node.isGrowing = false`. It does **not** set `node.isTrimmed = true`. This is critical:

- `budSystemActive` is `true` from year 2 onward. When it is set, `StartNewGrowingSeason` only calls `SetBuds()` on nodes that have `hasBud = true`.
- `SetBuds()` in August only sets buds on non-trimmed nodes.
- If `isTrimmed = true` were set: the node is excluded from `SetBuds()` → no bud → no spring growth → permanent blockage.
- With `isTrimmed = false` and `length == targetLength`: the node is included in autumn `SetBuds()`, gets `hasBud = true` in winter, and breaks normally next March. This produces the correct real-world behaviour: pinching stops the current shoot and forces lateral back-buds.

### Gotchas
- `backBudStimulated` is consumed (reset) after each spring check — one roll per trim.
- `subdivisionsLeft > 0` blocks bud-set on sub-segment nodes; only true chain tips get buds.
- `vigorFactor` scales all lateral chances down as the tree fills `maxBranchNodes`.
- **Never set `isTrimmed = true` on a pinched node.** Use `length = targetLength` only.

---

## 12. Wound System

**Files:** `TreeSkeleton.cs`, `TreeNode.cs`, `TreeInteraction.cs`, `TreeMeshBuilder.cs`

**Full doc:** `Docs/WoundSystem.md`

### What it does
Trimming creates a wound on the parent node. Each growing season the wound
drains health (`woundDrainRate` per season) until the player applies cut paste
or the wound calluses over naturally. Large wounds on heavy branches are a
real health risk if ignored.

Wound geometry is **embedded directly in the unified tree mesh** via
`AddWoundCap()` — no separate GameObject or material. The callus ring, inner
closing ring, and concave center are built procedurally from `woundAge` and
`healProgress`.

### Key data
| Field | Meaning |
|---|---|
| `hasWound` | Exposed cut face exists on this node |
| `woundRadius` | Size of cut (drives visual scale and drain) |
| `woundAge` | Seasons elapsed; heals when `>= woundRadius × seasonsToHealPerUnit` |
| `pasteApplied` | Stops health drain; encodes as `vertex.b = 1` in mesh for shader |

### Vertex color encoding (wound-related channels)
| Channel | Meaning |
|---|---|
| `vertex.r` | `isRoot` flag (unchanged) |
| `vertex.a` | `barkBlend` twig→mature (unchanged) |
| `vertex.g` | Wound intensity: `1 − (woundAge / woundFadeSeasons)`, clamped 0–1 |
| `vertex.b` | Paste mask: `1` if `node.pasteApplied`, else `0` |

### Flow
```
TrimNode(child)
    → parent.hasWound = true, woundRadius set, woundAge = 0
    → CreateWoundObject() creates empty anchor GameObject (_WoundAnchor_N)
    → meshBuilder.SetDirty() → AddWoundCap() builds callus geometry in unified mesh
StartNewGrowingSeason()
    → woundAge++ for all wounds
    → ApplyDamage(WoundDrain) if !pasteApplied
    → hasWound = false + anchor destroyed if healed → SetDirty()
Player clicks wound in Paste mode
    → ApplyPaste() → pasteApplied = true → meshBuilder.SetDirty()
    → vertex.b flips to 1; shader shows paste tint over wound zone
```

### Gotchas
- Subdivision cuts use `woundRadius × 0.35` (tiny nip, heals in 1 season by default).
- Full branch cuts use full `node.radius` — a thick branch unprotected for 20 seasons can kill the node.
- Paste stops drain but does not speed up healing.
- The empty `_WoundAnchor_N` GameObject is kept to preserve book-keeping paths (heal loop, undo, `woundObjects` dict) without breaking existing code.

---

## 13. Nebari Scoring

**Files:** `TreeSkeleton.cs`

**Full doc:** `Docs/RootHealthSystem.md`

### What it does
Scores the visible root flare (nebari) 0–100 based on angular coverage (50%),
girth (30%), and radial balance (20%). Displayed in the Nebari panel whenever
Root Prune mode is active.

Only surface roots at depth 1–`nebariMaxDepth` and within `nebariSurfaceDepth`
of Y=0 are counted. See `RootHealthSystem.md` for the full scoring formulas.

---

## 14. Air Layer System

**Files:** `TreeSkeleton.cs`, `TreeNode.cs`

**Full doc:** `Docs/AirLayerSystem.md`

### What it does
Places a propagation wrap on a trunk/branch node. After `airLayerSeasonsToRoot`
growing seasons, the player unwraps it and `airLayerRootCount` root strands
are spawned radially on the cylindrical surface of that node. These roots
(`isAirLayerRoot = true`) always render, scale with the trunk via
`ScaleAirLayerRootRadii`, and re-anchor each frame in `UpdateAirLayerRootPositions`
so they stay flush to the bark as the trunk thickens.

### Gotchas
- Air-layer roots are exempt from `rootVisibilityDepth` cull — always visible.
- `ScaleAirLayerRootRadii` overrides the pipe model after `RecalculateRadiiInternal`
  every call; the override is intentional.
- Root positions are updated every frame regardless of game state.

---

## 15. Pot-Bound Root Pressure

**Files:** `TreeSkeleton.cs`, `TreeNode.cs`

### What it does
Simulates what happens when roots hit the walls of a tray and can't spread further.
Three mechanical effects activate when a root terminal's `boundaryPressure` exceeds
the threshold:

| Effect | Trigger | Value |
|---|---|---|
| Growth slowdown | `boundaryPressure >= threshold` | Speed × `boundaryGrowthScale` (default 0.35) |
| Thickening | Same | `node.radius += boundaryThickenRate` per season |
| Inner fill boost | Same | Low-depth ancestors get `rootFillLateralChance × potBoundInnerBoost` |

### Flow
```
Each spring (StartNewGrowingSeason):
    For each root terminal:
        distRatio = distance(tip.xz, trunk.xz) / spreadRadius
        if distRatio >= 0.85 → boundaryPressure++, add radius thickening
        if distRatio < 0.85  → boundaryPressure-- (clamp 0)
    For pot-bound terminals (pressure >= threshold):
        collect low-depth (≤2) ancestors as inner fill candidates
        each candidate rolls rootFillLateralChance × potBoundInnerBoost
        spawn lateral up to potBoundMaxFillPerYear budget
```

### Inspector fields
| Field | Default | Meaning |
|---|---|---|
| `boundaryPressureThreshold` | 3 | Seasons near wall before effects activate |
| `boundaryGrowthScale` | 0.35 | Speed multiplier for pot-bound terminals |
| `boundaryThickenRate` | 0.003 | Radius added per season at wall |
| `potBoundInnerBoost` | 3.0 | Fill-lateral chance multiplier for inner ancestors |
| `potBoundMaxFillPerYear` | 30 | Budget cap on inner-fill laterals per spring |

### Gotchas
- `potBoundMaxFillPerYear` is independent of `maxTotalRootNodes` — inner fill
  can fire even when the outer root cap is full.
- `spreadRadius` is `rootAreaTransform` box size in Ishitsuki mode, or
  `cachedTreeHeight × rootSpreadMultiplier` otherwise.

---

## 16. Ishitsuki (Root-over-Rock)

**Files:** `TreeSkeleton.cs`, `TreeMeshBuilder.cs`, `RockPlacer.cs`,
`IshitsukiWire.cs`, `GameManager.cs`

**Full doc:** `Docs/IshitsukiSystem.md`

### What it does
The Ishitsuki system drapes root cables over a rock surface from the trunk
base to the soil. Once the player confirms the tree's orientation on the rock,
`SpawnTrainingWires()` sets `isIshitsukiMode = true`, assigns the rock
collider, and calls `PreGrowRootsToSoil()` to build pre-grown cable chains.
Each spring the cables are extended further until they reach `soilY`.

### Key concepts
- **`isTrainingWire`** on cable nodes — always renders, exempt from
  `rootVisibilityDepth` cull, can't be removed until `wireSetProgress >= 1.0`.
- **`PreGrowRootsToSoil`** — four step modes: `exterior` (on rock face),
  `toEdge` (under rock → snap to edge), `lowerFace` (lower hemisphere), `freeFall`.
- **`ScaleIshitsukiCableRadii`** — cables thicken with the trunk each season
  via `trunkRadius × ishitsukiCableRadiusMultiplier × 0.82^chainDepth`.
- **Claimed edges** — deduplication prevents multiple cables converging on the
  same rock surface point.
- **Grip snap** — `AddRing` in the mesh builder snaps root vertices to the rock
  surface when within ~0.01 units (convex collider required).

See `IshitsukiSystem.md` for the complete flow and all edge cases.

---

## 17. Pause & Settings Menu

**Files:** `buttonClicker.cs`, `ButtonUI.uxml`, `GameManager.cs`

### What it does
A full-screen dark overlay menu (ESC or the II button) that pauses all game
systems and exposes key tuning parameters as live sliders. Changes take effect
immediately — no apply/cancel step.

### Pause mechanism

`GameManager.TogglePause()`:
- **Opening:** saves `state` into `prePauseState`, sets `Time.timeScale = 0`,
  transitions to `GameState.GamePause`. Setting `timeScale` to zero freezes
  everything driven by `Time.deltaTime` — calendar, tree growth, wire
  progress, leaf fall, camera animation.
- **Closing:** restores `Time.timeScale = 1`, calls `UpdateGameState(prePauseState)`.

`ButtonClicker.Update()` checks `Input.GetKeyDown(KeyCode.Escape)` every
frame and calls `TogglePauseMenu()`. The II button in the HUD does the same.

### UI structure (`ButtonUI.uxml`)

```
PauseMenuOverlay  (full-screen, display:none when closed)
└── PauseMenuPanel  (520px centred dark card)
    ├── Title label  "SETTINGS"
    ├── TabBar
    │   ├── TabBtnTime
    │   ├── TabBtnGrowth
    │   └── TabBtnIshitsuki
    ├── TabContentTime      (visible by default)
    ├── TabContentGrowth    (hidden)
    ├── TabContentIshitsuki (hidden)
    └── ResumeButton
```

### Tabs and sliders

| Tab | Slider / Control | Drives | Range |
|---|---|---|---|
| **Time** | Time Scale | `GameManager.TIMESCALE` | 1–400 |
| **Time** | Quick Winter | `GameManager.quickWinter` | toggle |
| **Growth** | Grow Speed | `TreeSkeleton.BaseGrowSpeed` | 0.02–0.8 |
| **Growth** | Spring Laterals | `TreeSkeleton.SpringLateralChance` | 0–1 |
| **Growth** | Depth Decay | `TreeSkeleton.DepthSpeedDecay` | 0.5–1 |
| **Ishitsuki** | Cable Radius | `TreeSkeleton.IshitsukiCableRadiusMultiplier` | 0.05–1 |
| **Ishitsuki** | Min Cable Angle | `TreeSkeleton.MinCableAngleDeg` | 10–90° |

Each slider has a live value label to its right that updates as you drag.

### Tab switching

`SelectTab(int index)` in `ButtonClicker`:
- Shows the matching `TabContent*` panel, hides the others.
- Highlights the active tab button gold (`#E5B316` bg, near-black text, bold).
- Inactive tabs are dark grey with grey text.

### Slider sync

`SyncSlidersFromGame()` is called each time the menu opens. It uses
`SetValueWithoutNotify()` to push current live values into the sliders
without triggering their change callbacks, preventing a spurious write-back
on open. The `skeleton` `[SerializeField]` reference on `ButtonClicker` must
be assigned in the Inspector for the Growth and Ishitsuki sliders to work.

### Quick Winter sync

Quick Winter is exposed in two places: the HUD button and the menu toggle.
Both stay in sync:
- HUD button click → sets `GameManager.quickWinter`, calls
  `toggleQuickWinter.SetValueWithoutNotify(...)`.
- Menu toggle change → sets `GameManager.quickWinter`, updates HUD button
  colours directly.

### Gotchas
- `Time.timeScale = 0` also freezes Unity's `Animator` components and any
  code using `Time.deltaTime`. Code using `Time.unscaledDeltaTime` (none
  currently) would still run through a pause.
- ESC is checked in `ButtonClicker.Update()` — if that MonoBehaviour is
  disabled for any reason, ESC won't work. The II button always works.
- The menu opens on the **Time** tab every time regardless of which tab was
  last active. This is intentional — keeps the most-used control front and centre.
- If `skeleton` is null on `ButtonClicker`, Growth and Ishitsuki sliders still
  render but their callbacks silently no-op. No error is thrown.

---

## 18. Bark Shader System

**Files:** `Assets/Shaders/BarkVertexColor.shader`, `TreeMeshBuilder.cs`, `TreeSpecies.cs`

### What it does
A 100% procedural HLSL shader that generates 10 distinct botanical bark
surface patterns from math alone — no textures. Pattern complexity scales
with the `_BarkBlend` vertex channel, giving twigs a simple fine-fissure look
and mature wood the full species bark type. Cel-shaded 3-band lighting with a
silhouette outline pass.

### Bark types (barkType on TreeSpecies)
| ID | Pattern | Key species |
|---|---|---|
| 1 | Smooth | Ficus |
| 2 | Fine fissures | Japanese Maple, Wisteria |
| 4 | Interlacing ridges | Willow, Elm |
| 7 | Vertical strips | Atlas Cedar |
| 8 | Irregular blocks | Spruce (2 spp.) |
| 9 | Large plates | Pine (3 spp.) |
| 10 | Peeling horizontal strips | Birch |
| 12 | Fibrous shreds | Juniper, Redwood, J. Cedar |
| 14 | Spongy fibrous attached | Cypress |
| 16 | Lenticels | Cherry |

### Vertex color encoding
| Channel | Shader reads as |
|---|---|
| `vertex.r` | `isRoot` — shifts pattern toward root texture |
| `vertex.a` | `barkBlend` — 0=twig (Type 2) → 1=full species type |
| `vertex.g` | `woundIntensity` — drives heartwood/cambium/callus zone blend |
| `vertex.b` | `pasteMask` — overrides wound zone with paste color when 1 |

### Passes
1. **Outline (SRPDefaultUnlit, Cull Front)** — position inflated by `_OutlineWidth`
   along world normal.
2. **ForwardLit** — cel lighting: `NdotL × shadowAttenuation` stepped at
   `_ShadowThreshold` and `_MidThreshold` into 3 tint bands; additional lights
   added as additive fill. Wound face: 3-zone blend (heartwood / cambium / callus)
   modulated by `vertex.g`; paste overrides with `_PasteColor` when `vertex.b > 0.5`.
3. **ShadowCaster / DepthOnly** — standard passes with matching CBUFFER for SRP
   Batcher compatibility.

### Material properties set at runtime
`TreeMeshBuilder.ApplySpeciesColors()` calls:
- `mat.SetColor("_BarkColor", species.matureBarkColor)`
- `mat.SetColor("_NGColor", species.youngBarkColor)`
- `mat.SetColor("_NGRootColor", species.rootNewGrowthColor)`
- `mat.SetInt("_BarkType", species.barkType)`

### Gotchas
- All 4 passes must declare an identical CBUFFER for the SRP Batcher to accept
  the shader. Adding a new property requires updating all 4 CBUFFER blocks.
- `BuildCurvedQuad` in `BarkFlakerManager` is a placeholder mesh; swap by
  assigning an artist-modelled prefab to `barkFlakePrefab` in the Inspector.

---

## 19. Bark Flaker Manager

**Files:** `BarkFlakerManager.cs`

### What it does
Spawns 3D bark-flake GameObjects on trunk and scaffold nodes for peeling-bark
species (barkType 10 Birch, 12 Juniper/Cedar/Redwood, 14 Cypress). Operates
identically to `LeafManager` — flakes are parented to the tree transform in
local space. Flake count per node scales with tree age and stress so older,
unhealthier trees visibly peel more.

### When flakes spawn
- Only during `BranchGrow` state (once per year via `lastSpringYear` guard).
- Only for barkType 10, 12, or 14.
- Only nodes at `depth <= maxFlakeDepth` (default 3) and `radius >= minFlakeRadius`
  (default 0.06).

### Count formula
```
ageFactor = Clamp01((treeAge - minAgeForFlakes) / ageRange)
stress    = healthFlakeBoost × (1 − node.health)
combined  = Clamp01(ageFactor + stress × ageFactor)
count     = Round(combined × maxFlakesPerNode)
```
Default: flakes start appearing after year 4, peak over 25 years, healthy
trees peel minimally, stressed trees peel at `healthFlakeBoost` (0.6) rate.

### Lifecycle
- `RefreshFlakes()` removes flakes from trimmed nodes, adds/removes to match
  target count each spring.
- `ClearAllFlakes()` is called on tree death or full repot.
- Fallback mesh (`BuildCurvedQuad`): 3×4 vertex curved quad, 20° bend, pivot
  at top edge so the flake hangs downward.

### Inspector fields
| Field | Default | Meaning |
|---|---|---|
| `barkFlakePrefab` | null | Artist prefab; null uses built-in curved quad |
| `maxFlakesPerNode` | 2 | Max flakes per node at peak age+stress |
| `minAgeForFlakes` | 4 | Years before any flakes appear |
| `ageRange` | 25 | Years from first flake to max count |
| `maxFlakeDepth` | 3 | Only trunk/scaffold nodes |
| `minFlakeRadius` | 0.06 | Skip thin twigs |
| `healthFlakeBoost` | 0.6 | How much stress amplifies peeling |

---

## 18. Watering System

**Files:** `TreeSkeleton.cs`, `buttonClicker.cs`

Tracks `soilMoisture` (0–1). Drains at `drainRatePerDay × SeasonalGrowthRate` each frame. Below `droughtThreshold`, `Drought` damage is applied to all nodes. Auto-water fires just before threshold on an in-game-day cooldown and pulses the Water button grey for ~0.15 s. `drainRatePerDay` is set per-species (Maple 0.14, Juniper 0.06). Auto-water can be toggled off in the Debug tab.

---

## 19. Save / Load System

**Files:** `SaveManager.cs`, `SaveData.cs`

Full JSON serialization into named slots: `saves/<slotId>/save.json` + `meta.json` + optional `thumb.png`. `SaveMeta` is lightweight (name, species, date, nodeCount) — readable by the Load Menu without deserializing the full tree. `ActiveSlotId` persists to `PlayerPrefs`. Quick-save writes to active slot; prompts for a name if no active slot. Legacy `bonsai_save.json` is migrated to slot layout on first load. Auto-save fires at end of season.

---

## 20. Fertilizer System

**Files:** `TreeSkeleton.cs`, `buttonClicker.cs`

`nutrientReserve` (0–2). Drains 0.4/season. `Fertilize()` adds 0.5, capped at 2, blocked in winter. Growth mult = `Lerp(0.6, 1.4, reserve/2)`. FertilizerBurn on root nodes each spring when `reserve > 1.5`. Nutrient bar on right panel. Auto-fertilize toggle in Debug tab.

---

## 21. Weed System

**Files:** `WeedManager.cs`, `WeedPuller.cs`, `Weed.cs`

RMB click-hold-drag-up. Upward mouse delta accumulates `pullProgress`. Rip chance leaves a stub (`isRipped`, 1.8× force, 60% drain). `Physics.RaycastAll` required to reach weed colliders behind tree mesh. Four types: Grass 40%, Clover 35%, Dandelion 15%, Thistle 10%. Herbicide clears all + sets `aerationPenalty`. Weeds parented to tree GO, auto-cleared on Repot.

---

## 22. Fungus System

**Files:** `TreeSkeleton.cs`, `TreeNode.cs`, `Leaf.cs`

`fungalLoad` (0–1) per node. Spread sources each spring: open wounds, `soilMoisture > 0.9`, `health < 0.5`. Infected nodes spread to neighbours at 25% chance. Load > 0.4 → `FungalInfection` damage. Recovery: −0.1/season when no risk. Mycorrhizal: root nodes healthy 3+ seasons → reduces nutrient drain up to 20% by coverage fraction. Fungicide and herbicide kill mycorrhizae. Leaf tint via `MaterialPropertyBlock` driven by `fungalSeverity`.

---

## 23. Species System

**Files:** `TreeSpecies.cs`, `TreeSkeleton.cs`

`TreeSpecies` ScriptableObject. `ApplySpecies()` copies values into existing `TreeSkeleton` fields on Awake. 16 species assets in `Assets/Resources/Species/`. Species name shown in Settings header. Full-screen `SpeciesSelect` overlay at game start; sortable by tag chips; confirms into `TipPause`. `BudType` enum in its own file.

---

## 24. Soil / Substrate System

**Files:** `PotSoil.cs`, `TreeSkeleton.cs`

7 substrates (akadama, pumice, lava rock, organic, sand, kanuma, perlite). Weighted mix → derived `moistureRetention`, `aeration`, `drainage`, `pH`, `compaction`. Organic compacts over time. Species soil mismatch → growth penalty. High moisture + low aeration + overwatering → root rot. `Repot()` resets compaction; too-soon repot applies stress multiplier. 4 presets (Classic, Free-Drain, Moist, Acidic). Soil bars in Repot panel. Entering Repot auto-clears weeds.

---

## 25. Tree Death

**Files:** `TreeSkeleton.cs`, `buttonClicker.cs`

Toggleable (`treeDeathEnabled`, off by default). Death conditions: sustained `soilMoisture <= 0` or `consecutiveCriticalSeasons >= threshold`. `TreeDangerBanner` shown during warning phase. On death → `GameState.TreeDead` + death overlay (Load / Restart). `LastDeathCause` set before transition.

---

## 26. Branch Death & Dieback

**Files:** `TreeSkeleton.cs`, `TreeNode.cs`, `TreeMeshBuilder.cs`

`isDead`, `isDeadwood`, `shadedSeasons`, `deadSeasons` on `TreeNode`. `DiebackPass()` each spring: zero-health nodes → `isDead`; interior nodes with no living terminal children for `shadedSeasons >= threshold` → `isDead`. Small dead branches drop after `deadSeasonsToDrop` seasons; large ones → `isDeadwood` (permanent). Dead mesh colour: `Lerp(live, ashenGrey, deadSeasons/2)`.

---

## 27. Branch Weight & Strength

**Files:** `TreeSkeleton.cs`, `TreeNode.cs`

Toggleable (`branchWeightEnabled`, off by default). `BranchWeightPass()` each spring: bottom-up `ComputeLoad()` then `ApplySagAndStress()`. Sag angle accumulates when load/strength exceeds `sagThreshold`; `growDirection` Slerps toward down; `PropagatePositions()` updates descendants. Junction stress damage when ratio exceeds `junctionStressThreshold`. `branchLoad` and `sagAngleDeg` serialized.

---

## 28. Air Layering → New Tree

**Files:** `TreeSkeleton.cs`, `SaveManager.cs`, `buttonClicker.cs`

After `airLayerRootSeasonsToSever` seasons post-unwrap, `isSeverable = true` → `AirLayerSeverBanner` shown. Confirm: wound on cut site, subtree removed, original backed up to `bonsai_original.json`, new `SaveData` built from severed subtree (air layer node = new root, depths recalculated, air roots → normal roots), `LoadFromSaveData` called. `treeOrigin = AirLayer`. `LoadOriginalButton` visible when backup exists.

---

## 29. Named Save / Load Menu

**Files:** `SaveManager.cs`, `buttonClicker.cs`, `ButtonUI.uxml`

Multi-slot named saves. `SaveMeta` readable without full deserialization. `LoadMenu` shown at launch when saves exist. Cards show name, origin badge, species, date, real save time, node count proxy. Per-card: Load | Delete (confirm). Footer: New Game. Quick-save to active slot; Save Name Prompt on first save. `ActiveSlotId` in `PlayerPrefs`. `NewSlotId()` = `yyyyMMdd_HHmmss`.

---

## 30. Growth Season Taper

**Files:** `TreeSpecies.cs`, `TreeSkeleton.cs`, `GameManager.cs`

`growthSlowDay` / `growthStopDay` per species. `dayOfYear = (month-1)×28 + day` (March = month 1). `GrowthSeasonMult()` returns 1 before slow day, 0 at/after stop day, linear taper between. Multiplied into per-node growth delta each frame. `species == null` guard returns 1f. All 16 species assets updated with biology-appropriate windows (Juniper 140/166, Japanese Maple 152/182, Ficus 213/244, etc.).

---

## 31. Root Bark Color Transition

**Files:** `TreeSkeleton.cs`, `TreeMeshBuilder.cs`

Root nodes are now included in the age-accumulation loop (the `isRoot && !isTrainingWire` exclusion was removed). `GrowthColor()` in `TreeMeshBuilder` uses `node.age` to compute vertex alpha (0 = new-growth white/beige via `_NGRootColor`, 1 = fully barked). Exposed roots (`worldPosition.y > rootVisibilityDepth`) use `fadeDays/3` — bark in ~50 days vs ~150 days buried.

---

## 32. Rock Placement & Tree Orient

**Files:** `RockPlacer.cs`, `buttonClicker.cs`

Two-phase Ishitsuki setup. RockPlace: LMB grab/drop rock, mouse follows cursor on horizontal plane, scroll raises/lowers, RMB drag = yaw+pitch rotation, RMB+scroll = roll. TreeOrient: same rotation scheme on `treeTransform`; LMB drag moves tree on camera-relative axes (`ProjectOnPlane`). HUD dims (`opacity 0.25` + `PickingMode.Ignore`) during both states; Confirm/Cancel always visible. Cancel calls `RestorePrePlacementSnapshot()`. Confirm fires `OnRockOrientConfirmed` → drapes training wires.

---

## 33. Input System

**Files:** All input-using scripts

All input uses `com.unity.inputsystem`. Key mappings: `Mouse.current.leftButton.wasPressedThisFrame`, `Mouse.current.delta.ReadValue() * 0.01f` (matches old `GetAxis` sensitivity), `Mouse.current.scroll.ReadValue().y / 120f`. `Mouse.current` / `Keyboard.current` null-checked throughout. EventSystem must use `Input System UI Input Module` — not `Standalone Input Module`.

---

## 35. First-Use Tutorial / Tooltip System

**Files:** `buttonClicker.cs`, `ButtonUI.uxml`, `GameManager.cs`, `TreeSkeleton.cs`

### What it does
Shows a contextual full-screen tooltip overlay the first time each tool or situation is encountered. Each tooltip fires at most once per install (tracked in `PlayerPrefs`). The overlay pauses the game and must be dismissed with "GOT IT".

### `MaybeShowTooltip(id, title, body)`
Central method on `buttonClicker`. Guards:
1. `shownTooltips.Contains(id)` — skips if already shown (in-memory HashSet, populated from `PlayerPrefs` on `OnEnable`).
2. Adds `id` to the set, writes `PlayerPrefs.SetInt("Tooltip_" + id, 1)`, shows the `TooltipOverlay`, sets title/body labels.

Dismissal via the "GOT IT" button hides the overlay. The game does not need to be in a specific state — the overlay works on top of any state.

### Triggers

| ID | Trigger | File |
|---|---|---|
| `trim` | Trim button clicked | `buttonClicker` |
| `water` | Water button clicked | `buttonClicker` |
| `wire` | Wire button clicked | `buttonClicker` |
| `repot` | Root Prune button clicked | `buttonClicker` |
| `paste` | Paste button clicked | `buttonClicker` |
| `pinch` | Pinch button clicked | `buttonClicker` |
| `defoliate` | Defoliate button clicked | `buttonClicker` |
| `airlayer` | Air Layer button clicked | `buttonClicker` |
| `fertilize` | Fertilize button clicked (first use) | `buttonClicker` |
| `herbicide` | Herbicide button clicked (first use) | `buttonClicker` |
| `removewire` | First wire turns gold (`OnWireSetGold` event) | `buttonClicker` + `TreeSkeleton` |
| `april_ramification` | Month changes to April (`OnMonthChanged` event) | `buttonClicker` + `GameManager` |

### Gotchas
- `PlayerPrefs` tooltips persist across sessions. To reset all tooltips for testing: `PlayerPrefs.DeleteAll()` in the console, or delete each `"Tooltip_<id>"` key individually.
- The overlay has no concept of game state — it can appear mid-animation or mid-season. This is intentional; contextual timing matters more than pausing cleanly.
- `OnWireSetGold` fires per-skeleton-instance. If you ever support multiple trees, the event subscription in `buttonClicker.OnEnable` must be updated to subscribe to the correct skeleton.

---

## 34. Branch Saw

**Files:** `TreeInteraction.cs`, `TreeSkeleton.cs`

### What it does
Thick branches require a back-and-forth sawing gesture to remove rather than a single click, making large cuts feel weighty and deliberate.

### Threshold
`skeleton.sawRadiusThreshold` (default 0.08, Inspector field under Wound System header). When the player clicks a branch in Trim mode and `node.radius >= sawRadiusThreshold` **and** the active tool is `ToolType.Saw`, `StartSawing()` is called instead of `TrimNode()`. Thin branches and all non-Saw tools cut instantly as before.

### Stroke mechanic
Mouse X movement is tracked frame-to-frame. A **half-stroke** is counted each time the mouse reverses direction after traveling at least `sawMinStrokePx` (default 20 px) in the current direction. `sawTotalHalfStrokes` (default 10) half-strokes = ~5 full back-and-forth strokes to complete the cut.

```
sawProgress = Clamp01(sawHalfStrokes / sawTotalHalfStrokes)
```

### Groove mesh
A dark brown annulus is built at the node base, perpendicular to `growDirection`. The outer radius is `node.radius × 1.12` (slightly proud of the bark). The inner radius lerps from `node.radius × 0.9` → 0 as progress goes 0 → 1, so the groove visually deepens inward as you saw.

The groove is a child `GameObject ("_SawGroove")` rebuilt each frame via `UpdateSawGroove()` and destroyed on complete or cancel.

### Completion & cancel
- **Complete** (`sawHalfStrokes >= sawTotalHalfStrokes`): groove destroyed, `PlaySFX("Trim")`, `skeleton.TrimNode(node)` — identical path to a normal trim from here.
- **Cancel** (ESC or RMB): groove destroyed, no tree change, returns to normal trim hover.
- **Auto-cancel**: if `sawTarget.isTrimmed`, `!GameManager.canTrim`, or the tool is switched mid-saw.

### Interaction blocking
`isSawing` is checked at the top of `TreeInteraction.Update()`, same as `WirePhase.Animating` — all other hover/click handling is skipped until the saw completes or cancels.

### Inspector fields (`TreeInteraction`)

| Field | Default | Meaning |
|---|---|---|
| `sawMinStrokePx` | 20 | Pixels of travel before a reversal counts as a half-stroke |
| `sawTotalHalfStrokes` | 10 | Half-strokes to complete the cut (~5 full strokes) |

### Inspector field (`TreeSkeleton`)

| Field | Default | Meaning |
|---|---|---|
| `sawRadiusThreshold` | 0.08 | Branches at or above this radius require the Saw tool gesture |

### Gotchas
- SmallClippers and BigClippers always cut instantly regardless of radius — only `ToolType.Saw` triggers the mechanic.
- The groove mesh is in tree-local space; it correctly follows the branch if the tree lifts (e.g. during root mode, though sawing is blocked there anyway).
- `sawProgress` is purely cosmetic — the cut is binary (not partial). There is no "halfway sawed" state that persists if you cancel.

---

## 36. Play Mode Manager

**Files:** `PlayModeManager.cs`

### What it does
`PlayModeManager` is a singleton that runs an automation layer on top of `GameManager`'s speed controls. Each frame it evaluates the active `PlayMode`'s rules, picks the lowest (slowest) matching speed, and calls `gm.SetSpeedMode()`. It also syncs `TreeSkeleton.autoWaterEnabled` / `autoFertilizeEnabled` and exposes `IdleOrbitActive` for `CameraOrbit`.

### Data model

```
PlayMode
  ├── name, isBuiltIn
  ├── defaultSpeed         — used when no rules fire
  ├── autoWater / autoFertilize   — synced to TreeSkeleton each frame
  ├── idleOrbit / idleOrbitDelaySecs
  └── List<SpeedRule>
        ├── trigger         (SpeedRuleTrigger enum)
        ├── triggerParam    (threshold / month index)
        ├── targetSpeed
        ├── idleResumeEnabled / idleResumeRealSeconds / idleResumeInGameDays
        └── [NonSerialized] suppressed
```

`SpeedRule.suppressed` is a runtime flag — it is deliberately `[NonSerialized]` so it resets to false on every load/restart.

### Trigger types (SpeedRuleTrigger)

| Value | Active when |
|---|---|
| `Month` | `GameManager.month == (int)triggerParam` |
| `Season` | `GameManager.IsInSeason((Season)triggerParam, month)` |
| `MoistureBelow` | `skeleton.soilMoisture < triggerParam` |
| `HealthBelow` | average non-root node health < triggerParam |
| `NutrientBelow` | `skeleton.nutrientReserve < triggerParam` |
| `FungalLoadAbove` | max `node.fungalLoad` across all nodes > triggerParam |
| `WeedCountAbove` | `WeedManager.ActiveWeedCount > (int)triggerParam` |
| `WireSetGold` | `OnWireSetGold` event fired this frame (one-shot, clears each frame) |
| `TreeInDanger` | `skeleton.treeInDanger` |

### Rule evaluation (every frame)
1. Skip if `GameState.CalendarOpen` or `GameState.GamePause`.
2. Un-suppress rules whose idle timer has elapsed.
3. Walk all enabled, non-suppressed rules; collect those whose trigger is active.
4. Take the minimum (slowest) `targetSpeed`. If none fire → `mode.defaultSpeed`.
5. If resolved speed differs from `GameManager.CurrentSpeed` → call `gm.SetSpeedMode(resolved)`.

### Idle tracking
`lastInputRealTime` (`Time.unscaledTime`) and `lastInputInGameDay` reset on any mouse click, scroll, or keypress each frame. `IdleOrbitActive` is set `true` when `mode.idleOrbit && (unscaledTime - lastInputRealTime) >= idleOrbitDelaySecs`.

When idle timers for a rule's re-arm threshold elapse, the rule's `suppressed` flag is cleared. This lets a "Month=Jan → Slow" rule fire once when January arrives, then re-arm after the player hasn't touched the game for N real seconds.

### Built-in presets

| Mode | Default | Auto | Orbit | Key rules |
|---|---|---|---|---|
| Screensaver | Fast | ✓/✓ | 30 s | Jan→Slow; Danger→Slow (re-arm 20 s); Moisture<30%→Slow (re-arm 20 s) |
| Active Play | Med | ✗/✗ | — | Jan→Slow; WireGold→Slow (re-arm 5 days); Spring→Slow |
| Hands-Off | Fast | ✓/✓ | — | Danger→Slow (re-arm 60 s) |
| Focused | Slow | ✗/✗ | — | (no rules) |

### Persistence
Modes are serialized as `JsonUtility.ToJson(new PlayModeList { modes })` into `PlayerPrefs["playModes"]`. `activeModeIndex` stored separately. On `Awake`, tries to load from PlayerPrefs; falls back to `CreateDefaultModes()`. `SaveModes()` is called after any mode/rule change. `[NonSerialized]` fields are never included.

### Gotchas
- `PlayModeManager` calls `gm.SetSpeedMode()` every frame when the resolved speed differs. The manual speed toggle button still works, but it will be overridden next frame by PlayModeManager. This is intentional — "the mode wins."
- `WireSetGold` is a one-shot: `wireGoldFired` is set on `OnWireSetGold` and cleared at the bottom of `Update()`. If the rule is suppressed, the one-shot is consumed and won't re-fire until the trigger exits + re-enters (which for WireSetGold means the next wire goes gold).
- Auto-care is written to `TreeSkeleton` fields (`autoWaterEnabled`, `autoFertilizeEnabled`) each frame, not once — so toggling the active mode instantly changes auto-care behavior without a reload.

---

## 37. Calendar System (3-Tab Overlay)

**Files:** `buttonClicker.cs`, `ButtonUI.uxml`, `PlayModeManager.cs`

### What it does
A full-screen overlay opened by clicking the date label. Three tabs: **Schedule** (care event scheduling), **Modes** (Play Mode editor), **Speed** (timescale config). Opening the calendar transitions to `GameState.CalendarOpen` (pauses time). Closing calls `gm.SetSpeedMode(Med)` so PlayModeManager re-evaluates rules next frame from a known baseline.

### Tab strip
`CalTabSchedule` / `CalTabModes` / `CalTabSpeed` buttons call `SwitchCalTab(0/1/2)`, which shows the matching sub-panel (`CalScheduleTab` / `CalModesTab` / `CalSpeedTab`) and hides the others. Active tab gets a gold underline; inactive tabs are default text color. Calendar always opens on the Schedule tab.

### Schedule tab
Month/year header with `◂`/`▸` navigation buttons. A 7-column day grid (`CalMonthView`) populated dynamically with day-cell buttons. Clicking a day opens `CalDayView` listing that day's events. Events can be added (type, amount, time-of-day, repeat cadence, season scope), toggled, or deleted. Implemented but Part 1–4 of the Calendar System spec (real month lengths, event data model, persistent schedule) are still pending.

### Modes tab
Mode chip strip (`CalModeChips`) — one chip per `PlayMode`. Selecting a chip shows that mode's settings panel (`CalModeSettings`):
- **Speed chips** — Slow / Med / Fast, updates `mode.defaultSpeed`
- **Auto-water / Auto-fertilize** toggles
- **Idle orbit** toggle + integer delay field
- **Rules list** (`CalRulesList`) — one row per `SpeedRule` with enable checkbox + trigger summary label + delete button
- **Add Rule** button → opens `CalAddRulePanel`: trigger dropdown → param slider (if numeric) → speed chips → idle resume toggle + real-seconds + in-game-days fields → ADD / Cancel
- **Reset to defaults** button — calls `PlayModeManager.ResetBuiltInModes()`

### Speed tab
Three rows, one per speed tier:

| Row | Slider range | Live label | Hint |
|---|---|---|---|
| Slow | 0.05 – (Med-0.1) | e.g. `0.5×` | `"1 in-game day = 48 real seconds"` |
| Medium | (Slow+0.1) – (Fast-1) | e.g. `10×` | `"1 in-game day = 2.4 real seconds"` |
| Fast | (Med+1) – 500 | e.g. `200×` | `"1 in-game day = 7.2 real seconds"` |

All sliders share `OnSpeedSliderChanged`, which enforces ordering, calls `GameManager.SaveTimescalePrefs()`, and updates all three hint labels. `Reset to defaults` restores 0.5 / 10 / 200 and saves.

`FormatDayDuration(timescale)`: `float realSecs = 24f / timescale`; returns `"X real seconds"` when < 60, `"X.X real minutes"` otherwise.

### Gotchas
- Calendar uses `DisplayStyle.Flex` / `DisplayStyle.None` for all show/hide — not `visible`. Setting `visible = false` hides content but the element still occupies layout space.
- `SwitchCalTab` must be called from `OpenCalendar` to reset to the Schedule tab on every open; otherwise the last-active tab persists.
- `RefreshModesTab` must be called after `PlayModeManager` mode list changes (add/delete/reset), not just on tab switch, or the chip strip will show stale data.
- The Add Rule panel (`CalAddRulePanel`) is hidden by default and shown only when Add Rule is clicked. Confirm/Cancel both hide it again. The confirm path creates a `SpeedRule`, appends it to the active mode's list, calls `PlayModeManager.SaveModes()`, and refreshes the rules list.

---

## 38. Calendar Scheduling (Parts 1–4)

**Files:** `GameManager.cs`, `buttonClicker.cs`, `ButtonUI.uxml`, `SaveData.cs`

### What it does
Replaces the fixed 28-day month with real calendar month lengths and adds a scheduling system for auto-firing care tasks on configured days.

### Real month lengths
`DaysInMonthTable[]` holds real month lengths. `IsLeapYear(y)` and `DaysInMonth(m, y)` are static helpers. `dayOfYear` is a computed property summing months 1–(month-1) then adding `dayOfMonth`. Day rollover in `CalculateTime()` uses `DaysInMonth(month, year)`. Winter skip (Nov → Feb) is unchanged.

### Scheduled event data model

```csharp
public enum ScheduledEventType { Water, Fertilize }
public enum RepeatMode { Once, EveryNDays, EveryNWeeks }
public enum TimeOfDay { Morning, Midday, Night }

[Serializable]
public class ScheduledEvent
{
    public string id;
    public ScheduledEventType type;
    public int month, day;
    public RepeatMode repeat;
    public int repeatInterval;
    public Season season;
    public TimeOfDay timeOfDay;
    public bool enabled;
}
```

`GameManager.schedule` is a `static List<ScheduledEvent>`. `CheckScheduledEvents()` fires each time `dayOfMonth` increments, calling `skeleton.Water()` or `skeleton.Fertilize()` for matching events. Season gate: repeat events only fire during their target season. `EventFiresOnDate(ev, m, d, y)` is a static helper used by the calendar UI to show dots on day cells.

### Calendar UI — Schedule tab
**Month view:** 7-column day grid (`CalMonthView`). `MakeDayCell()` builds each cell: today highlighted gold, water/fertilize event dots shown as coloured indicators, past days dimmed.

**Day view (`CalDayView`):** lists events for the selected day. Each row has enable toggle + type label + delete button.

**Event add view (`CalEventView`):** type chips (Water/Fertilize) → season chip → repeat toggle → if repeating: N-day/N-week cadence with decrement/increment buttons → Confirm/Cancel. Confirm creates a `ScheduledEvent`, adds it to `GameManager.schedule`, and saves to `SaveData`.

### Seasonal templates
Four template buttons (Spring/Summer/Autumn/Winter) merge pre-built `ScheduledEvent` sets into the schedule, deduplicating by type+repeat. Templates apply species-appropriate care cadences.

### Gotchas
- `CheckScheduledEvents` runs inside the same guard as `CalculateTime` — it only fires when the state is a time-advancing state, never during pause or calendar-open.
- The `dayOfMonth` field is separate from the old `day` field; both are serialized but only `dayOfMonth` drives the new real-month rollover.
- `EventFiresOnDate` uses DOY-based modulo math for repeating events; the origin anchor (`ev.day`) must be computed correctly when the event was created or dots won't align with fire days.

---

## 39. Autosave System

**Files:** `SaveManager.cs`, `TreeSkeleton.cs`

### What it does
`SaveManager.AutoSave(skeleton, leafManager)` creates a save slot automatically if no `ActiveSlotId` exists, then saves. This ensures new games always get written without the player having to manually save first.

### Auto-slot creation
When `ActiveSlotId` is null, `AutoSave` generates a new slot with `NewSlotId()` and names it `"{SpeciesName} {Year} (autosave)"`. Sets it as the active slot. Subsequent autosaves reuse the same slot.

### Autosave triggers (in TreeSkeleton)
- End of every growing season (`StartNewGrowingSeason` after bud set)
- After a successful repot
- After an air layer is severed
- After Ishitsuki rock orientation is confirmed

### Gotchas
- A save toast ("Autosaved" HUD label) is not yet implemented — only `SaveStatusLabel` in the pause menu reflects save state. The `SaveManager.AutoSave` call itself is silent to the player.
- `AutoSave` calls `TakeScreenshotForSlot` via the same coroutine as manual saves for thumbnail capture.

---

## 40. Repot Root Raking

**Files:** `RootRakeManager.cs`, `GameManager.cs`, `buttonClicker.cs`, `ButtonUI.uxml`

### What it does
When repotting a pot-bound tree, instead of immediately applying new soil the game enters `GameState.RootRake`. The player rakes roots apart and prunes excess ones before confirming. Keeping a long root gives a bonus root strand in the new pot.

### Flow
1. Repot button pressed while pot-bound → enter `GameState.RootRake`.
2. `RootRakeManager` (`GetComponent` on the skeleton's GameObject) drives the interaction.
3. Player rakes (LMB drag) to visually spread roots, click individual root tips to prune.
4. Root-count indicator in HUD (`RootRakePanel`) shows current vs. target range.
5. **Confirm:** `RootRakeManager.ConfirmRepot()` — calls `PotSoil.Repot()`, checks if any root is longer than 1.5× average (sets `skeleton.hasLongRoot`), then calls `skeleton.RegenerateInitialRoots(hasLongRoot)` which spawns one extra-long strand on the long-root side if flagged.
6. **Cancel:** `CancelRakeMode()` returns to normal repot flow.

### Key data
| Field | Location | Meaning |
|---|---|---|
| `hasLongRoot` | `TreeSkeleton` | Set by RootRakeManager on confirm; consumed by `RegenerateInitialRoots` |
| `canRootRake` | `GameManager` | Static flag, true only during `RootRake` state |
| `RootRakePanel` | `ButtonUI.uxml` | HUD panel shown during rake mode with count label + Confirm/Cancel |

### Gotchas
- `RootRakeManager` is attached as a component on the same GameObject as `TreeSkeleton` — `buttonClicker` accesses it via `skeleton.GetComponent<RootRakeManager>()`.
- `hasLongRoot` is reset to `false` in `ConfirmRepot` before checking, so it always reflects the current rake session.
- `GameState.RootRake` is included in `canRootWork` (same flag set as `RootPrune`/`RockPlace`/`TreeOrient`) so root interaction remains active.

---

## 41. Pot Size Selection

**Files:** `PotSoil.cs`, `buttonClicker.cs`, `SaveData.cs`

### What it does
Lets the player choose a pot size when repotting. Size affects `rootAreaTransform` scale, which controls how quickly roots hit the boundary and trigger pot-bound mechanics.

### PotSize enum
`XS / S / M / L / XL / Slab` on `PotSoil`. `ApplyPotSize(rootAreaTransform)` resizes the transform to match the chosen size. Slab is very wide and shallow, encouraging lateral surface roots.

### UI
Six buttons in the repot panel (`PotSizeXSButton` … `PotSizeSlabButton`). `SelectPotSize(size)` updates `potSoil.potSize`, calls `ApplyPotSize`, and updates the `PotSizeLabel`. Current size is highlighted.

### Serialization
`SaveData.potSize` stores `(int)potSoil.potSize`. Loaded and applied on `LoadFromSaveData`.

### Rock size
`RockPlacer.RockSize` enum (S/M/L/XL). `ApplyRockSize()` sets `transform.localScale` from a lookup table. Four chip buttons (`RockSizeSButton`…`RockSizeXLButton`) shown in the HUD `RockSizePanel` only during the RockPlace state. `SaveData.rockSize` stores the cast-to-int value; restored in `LoadFromSaveData`.

### Gotchas
- `ApplyPotSize` must be called after every repot and also on load, or the root area won't match the serialized pot size.
- Slab roots grow wide and shallow — combined with `rootGravityWeight` they still tend downward; the slab primarily caps downward depth via the bottom-face clamp.

---

## 42. Sibling Branch Fusion

**Files:** `TreeSkeleton.cs`, `SaveManager.cs`

### What it does
Automatically detects when sibling branch tips grow close enough to touch and registers a multi-season fusion process. After enough seasons, a physical bridge node appears between them — the same `isGraftBridge` path used by approach grafts. This simulates the natural self-grafting that happens in dense bonsai canopies.

### Detection — `DetectNewFusions()`
Called from `StartNewGrowingSeason` after `AdvanceGrafts`. Groups all living, non-root, non-dead, terminal nodes by parent. For each sibling pair:
- Neither node may already be in any `FusionBond`.
- Tip-to-tip world distance must be ≤ `(radiusA + radiusB) × fusionTipProximityMult` (default 2.5).

On match, a `FusionBond(nodeIdA, nodeIdB)` is added to `skeleton.fusionBonds`.

### Progression — `AdvanceFusions()`
Called immediately after `DetectNewFusions`. For each incomplete bond:
1. Look up both nodes in `allNodes`. If either is gone/dead/trimmed → remove bond.
2. Increment `seasonsElapsed`.
3. Once `seasonsElapsed >= fusionSeasonsToFuse` (default 4): create a bridge `TreeNode` from `tipA` pointing toward `tipB`, radius = `min(rA, rB) × 0.65`, length = world tip-to-tip distance. Mark `isGraftBridge = true`, `isGrowing = false`. Append to `a.children` and `allNodes`. Set `isComplete = true`, `bridgeId = bridge.id`. Call `RecalculateRadii` + `meshBuilder.SetDirty`.

### Data model

```csharp
public class FusionBond
{
    public int  nodeIdA, nodeIdB;
    public int  seasonsElapsed;
    public bool isComplete;
    public int  bridgeId;   // -1 until fused
}
```

### Serialization
`SaveFusionBond` mirrors `FusionBond` fields. `SaveData.fusionBonds` list populated in `BuildSaveData`; restored in `LoadFromSaveData` (Pass 3b, before mesh rebuild).

### Inspector fields (`TreeSkeleton`, Header "Sibling Fusion")

| Field | Default | Meaning |
|---|---|---|
| `fusionSeasonsToFuse` | 4 | Growing seasons before two touching tips fuse |
| `fusionTipProximityMult` | 2.5 | Tip distance threshold = (rA+rB) × this value |

### Gotchas
- Detection only considers **terminal** nodes. Mid-chain sub-segment tips are excluded — they're too close by construction and would produce noise.
- The bridge reuses `isGraftBridge`, so `TreeMeshBuilder` already renders it as a thin connecting segment without any extra mesh code.
- Bonds that lose a node (trimmed, dead) are silently removed; no partial geometry is left behind.
- On load, `fusionBonds` is cleared and rebuilt from `SaveData.fusionBonds`. Completed bonds are restored but the bridge node itself is already in `allNodes` (it was serialized as a regular `SaveNode`), so no bridge re-creation is needed.

---

## 43. Bark Texture System

**Files:** `TreeSpecies.cs`, `BarkVertexColor.shader`, `TreeMeshBuilder.cs`

### What it does
Optional pixel-art texture tiers layered on top of the existing procedural bark. When no textures are assigned the system is completely inert — the shader falls through to the original procedural patterns. When textures are assigned, each texel makes a hard binary flip from the young tier to the mature tier as the bark matures, using pixel-snapped noise so the transition reads like bark organically aging rather than two textures cross-fading.

### Data flow
1. `TreeSpecies` holds `youngBarkTexture`, `matureBarkTexture` (optional `Texture2D`).
2. `TreeMeshBuilder.ApplySpeciesColors()` pushes them to the material: `_BarkTexA` / `_BarkTexB`, `_UseTextures` (0 when both null, 1 otherwise), `_TexelRes`, `_BarkNoiseMode`. Also sets `barkVTilingScale` from `species.barkVTiling` (default `0.4f` — matches original UV scale).
3. `AddRing` writes `uv.y = heightV * barkVTilingScale` (was hardcoded `0.4f`).
4. In the shader `ForwardLit` fragment: when `_UseTextures > 0.5`, snap UV to the texel grid (`floor(uv * _TexelRes) / _TexelRes`), compute noise (scatter or Voronoi cellular), hard-threshold against vertex alpha `blend`. Each texel is 100% young or 100% mature — no color blending.

### Noise modes
| `_BarkNoiseMode` | Pattern | Best for |
|---|---|---|
| 0 (scatter) | Per-texel random hash — salt-and-pepper spread | Fine maple / cherry bark |
| 1 (cellular) | Voronoi cell distance — spreading patches | Chunky pine / juniper plate bark |

### Art workflow
1. Paint seamless tile textures (suggested 64×64 or 128×128), import with **Point (no filter)** sampling.
2. Assign to `youngBarkTexture` / `matureBarkTexture` on the species `.asset`.
3. Set `barkVTiling` to the desired repeat density (start around 2–4 for 64×64 art).
4. Set `barkTexelRes` to match the texture dimension (e.g. 64).
5. Tune `barkNoiseMode` in Inspector.

### Gotchas
- `_UseTextures = 0` when both textures are null — the shader takes the existing procedural path with zero overhead.
- Adjacent tiers don't need to share a color language — the noise transition handles it.
- Groove darkening is still applied on top of the texture sample, so bark geometry reads through even with textures.
- `barkVTiling` defaults to `0.4f` which preserves the UV scale that the procedural bark patterns were tuned for. Raise it only when switching to textures.
