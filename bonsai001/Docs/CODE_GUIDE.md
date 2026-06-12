# BONSAIMANCER Code Guide

Last updated: 2026-05-07

---

## Quick orientation

All the "tree brain" components live on **one GameObject** in the scene. That GameObject has:

| Component | Purpose |
|-----------|---------|
| `TreeSkeleton` | Owns the tree graph, drives growth each frame |
| `AutoStyler` | Reads the graph, schedules trimming/wiring/pinching |
| `TreeMeshBuilder` | Reads the graph, builds the single unified mesh |
| `TreeInteraction` | Handles raycasts, highlights, and player tool input |
| `LeafManager` | Spawns/removes leaf prefab instances |
| `WireRenderer` | Renders the metal wire coils on wired nodes |
| `BarkFlakerManager` | Manages the bark-flaking visual on wounds |

`GameManager` and `buttonClicker` live on **separate GameObjects** and communicate via static events and singleton references.

---

## The data model: TreeNode

`TreeNode` is a plain C# class (no MonoBehaviour). The entire tree is a graph of these objects.

```
TreeSkeleton.root         ← the base trunk node
  └── TreeNode (depth 0)  ← trunk segment 0
        children[0]       ← next trunk segment
        children[1]       ← first lateral branch (depth 1)
              children[0] ← sub-branch (depth 2)
              children[1] ← sub-branch (depth 2)
```

Key geometry fields on each node:
- `worldPosition` — base of the segment in **local space** (relative to the tree GameObject)
- `growDirection` — unit vector pointing toward the tip
- `length` — current length (grows toward `targetLength` over the season)
- `tipPosition` — computed: `worldPosition + growDirection * length`
- `radius` — base radius; pipe model keeps this consistent with children

Key state flags:
- `isGrowing` — the update loop only elongates nodes where this is true
- `isTrimmed` — node was cut; mesh stays but no growth; excluded from all logic
- `isDead` — health hit zero; excluded from most logic
- `isRoot` — part of the root system (separate depth-counting, separate rendering rules)
- `isTerminal` — computed property: `children.Count == 0`
- `hasBud` — set each autumn, required to get leaves next spring (unless born this year)
- `hasLeaves` — true once LeafManager has spawned a cluster on this node

`TreeSkeleton.allNodes` is a `List<TreeNode>` — this is the authoritative list of **all** nodes, branches AND roots. Many systems iterate it. When searching for something, iterate `allNodes` directly.

---

## The state machine (GameManager)

`GameManager` owns the state enum and time. Nothing ticks unless the state is one of:

| State | What ticks |
|-------|-----------|
| `BranchGrow` | Tree grows; AutoStyler active; watering drains |
| `LeafGrow` | Leaf spawn (mostly handled inside BranchGrow) |
| `LeafFall` | Leaf fall rolls each frame |
| `TimeGo` | Calendar advances but tree doesn't grow |

State transitions are triggered by `GameManager.SetMonthText()`, which fires on the first day of each new month:

```
March  → BranchGrow   (also fires OnMonthChanged → AutoStyler.HandleNewGrowingSeason)
Sep    → TimeGo
Oct    → LeafFall
Nov    → TimeGo
```

The player can enter sub-states (`RootPrune`, `Wiring`, `GamePause`, etc.) at any time; those save the previous state and restore it on exit.

**`GameManager.SeasonalGrowthRate`** returns 0–1 depending on the month. March/April/May peak; July is nearly stopped; Aug–Feb are zero. TreeSkeleton multiplies all grow speeds by this.

---

## Time and how fast things happen

`GameManager.TIMESCALE` is "in-game hours per real second." At the default of 10, one real second = 10 in-game hours, so one in-game day takes ~2.4 real seconds. At TIMESCALE_FAST (200), one day takes ~0.12 real seconds.

To convert real time to in-game days:
```csharp
float inGameDays = Time.deltaTime * GameManager.TIMESCALE / 24f;
```

AutoStyler uses `InGameDay()`:
```csharp
float InGameDay() => (GameManager.year * 365f) + GameManager.dayOfYear;
```
This gives an absolute day counter that never resets. Scheduled action fire times are stored as absolute InGameDay values.

---

## Growth: how the tree actually grows (TreeSkeleton)

### Setup
When a new game starts, `TreeSkeleton.InitNewTree()` creates the first trunk nodes (several depth-0 subdivisions) and `RegenerateInitialRoots()` plants the first root strands.

### The grow loop
Every frame where `GameManager.SeasonalGrowthRate > 0`, `TreeSkeleton.Update()`:

1. Iterates `allNodes`
2. For each node where `isGrowing == true` and `health > 0.25`:
   - Advances `node.length` toward `node.targetLength` (speed = `baseGrowSpeed × depthSpeedDecay^depth × SeasonalGrowthRate × branchVigor`)
3. When `node.length >= node.targetLength`:
   - Calls `SpawnChildren(node)` — creates the continuation + optional lateral
   - Calls `RecalculateRadii()` — pipe model bottom-up walk, thickens trunk

### Season start (March 1 = new growing season)
`SetMonthText()` → `UpdateGameState(BranchGrow)` → fires `OnGameStateChanged(BranchGrow)` → `OnNewGrowingSeason?.Invoke()`.

`TreeSkeleton.StartNewGrowingSeason()` runs:
1. Sets buds on terminal nodes
2. Starts the new season's elongation on all nodes
3. Fires back-budding where flagged
4. Runs health/drought/fungal calculations
5. Fires `OnNewGrowingSeason` (AutoStyler listens here)

### Depth cap
Only nodes within `depthsPerYear × currentYear` depth can have children. This naturally slows the tree as it matures.

---

## AutoStyler: how it works

AutoStyler doesn't modify the tree directly every frame. Instead it **schedules** actions that fire at a specific `InGameDay`.

### Slots
AutoStyler builds a list of `BranchSlot` objects from `StyleDefinition.branchTiers`. Each tier says "I want N branches at azimuths 0°, 120°, 240° starting from azimuthOffsetDeg." That creates N slots total.

Each `BranchSlot` has a state:
```
Empty → Growing → Training → Established → Maintaining
```
A slot is matched to a real branch when `RefreshSlots()` runs. The match % displayed in the UI is `(Established + Maintaining) / totalSlots`.

### Seasonal schedule
AutoStyler subscribes to two events:

**`GameManager.OnMonthChanged`** → `HandleMonthChanged(month)`:
- **February**: `StimulateEmptySlots()` — stimulates back-budding on trunk nodes near empty slots
- **April–May**: `TrySchedulePinches()` — silhouette overshoot check, schedules pinches
- **June**: `TryScheduleRamificationPinches()` — interior pad density pinches
- **October**: `ScheduleBranchWires()` — wires Growing/Training branches toward slot azimuth

**`TreeSkeleton.OnNewGrowingSeason`** → `HandleNewGrowingSeason()`:
- `RefreshSlots()` — re-matches all branches to slots
- `WireTrunk()` — checks trunk node angles vs waypoints; wires if off

### Every frame (Update)
- `UpdatePendingActions()` — checks each pending trim/wire/pinch; fires any whose `fireDaytime <= InGameDay()`
- `RemoveSetWires()` — tracks when wires turn gold (`wireSetProgress >= 1.0`) and removes them `unwireDelayDays` later

### How a branch gets wired toward a slot (tracing)
1. October: `ScheduleBranchWires()` runs
2. For each `Growing`/`Training` slot with an assigned node, it computes `targetDir` = the slot's desired azimuth direction + tier's target elevation angle
3. Adds an entry: `pendingWires[nodeId] = InGameDay() + someDelay`; `pendingWireDir[nodeId] = targetDir`
4. Every frame: `UpdatePendingActions()` fires when the day arrives → `ApplyWire(node, dir, autoWiredBranchIds)` → `skeleton.WireNode(node, dir)`

### How to debug slow convergence
The style match is low when:
- `RefreshSlots()` can't match branches to slots (check slot azimuths vs actual branch positions)
- Branches keep getting trimmed before being assigned (check tier occupancy logic)
- No back-budding is happening to fill empty slots (check `backBudBaseChance`, `backBudActivationBoost` in TreeSkeleton Inspector)

Add `verboseLog = true` in the AutoStyler Inspector to see every action logged.

---

## Player tools: tracing a trim

1. Player selects Shears/Clippers/Saw tool → `ToolManager.SetTool(type)` → sets `GameManager.canTrim = true`
2. Each frame, `TreeInteraction.Update()` raycasts from the camera through the mouse position against the tree's `MeshCollider`
3. On hover: `TreeInteraction` builds a red highlight mesh over the subtree rooted at the hit node
4. On click: `TreeInteraction.HandleTrimClick()` → `skeleton.TrimNode(hitNode)`
5. `TrimNode` / `TrimSubtree`:
   - Marks all descendants as `isTrimmed = true`
   - Adds a wound to the parent node
   - Fires `OnSubtreeTrimmed(removedNodes)` event
6. `LeafManager` responds to `OnSubtreeTrimmed` → starts fall animation on affected leaf clusters
7. `meshBuilder.SetDirty()` → mesh rebuilds next frame

---

## Player tools: tracing a wire

1. Player selects Wire tool → click on a node → `GameManager.UpdateGameState(Wiring)` — **time pauses**
2. `TreeInteraction` enters `WirePhase.Aiming` — shows a direction arrow following the mouse
3. Second click → `TreeInteraction` calls `skeleton.WireNode(node, aimDirection)`:
   - Sets `node.hasWire = true`, `node.wireOriginalDirection`, `node.wireTargetDirection`
   - `node.growDirection` will be lerped toward `wireTargetDirection` each spring
4. `GameManager.UpdateGameState(WireAnimate)` → brief animation plays
5. State restores to pre-wire state; time resumes

Wire set progress (`wireSetProgress`) advances each spring as the wood lignifies in the new direction. When it hits 1.0, the wire turns gold. AutoStyler removes it after `unwireDelayDays`.

---

## Rendering: TreeMeshBuilder

`TreeMeshBuilder` builds one single `Mesh` for the whole tree. It rebuilds only when `SetDirty()` is called.

Key idea: when processing a node, it generates a **ring of vertices** at the base and a ring at the tip. Children share the parent's tip ring as their own base ring. When `RecalculateNormals()` runs, normals at junctions are averaged across all adjacent faces — giving smooth shading instead of visible seams.

`OnRenderObject()` (not `Update`) is used for GL debug overlays — root rainbow lines, health rings, AutoStyler's trim/wire indicators. This runs after the main render pass.

`SetDirty()` is the thing to call from any code that changes node geometry. Forgetting it = mesh doesn't update.

---

## LeafManager

Leaves are separate `GameObject` instances (the `leafPrefab`), parented to the tree transform. All leaves share one `Material` instance so seasonal color changes are O(1).

**Spring (BranchGrow)**: `SpawnSpringLeaves()` runs every frame during the growing season. It iterates `allNodes` and spawns a cluster on any terminal that has `hasBud = true` (or was born this season) and doesn't already have leaves.

**LeafFall**: `RollLeafFall()` runs each frame. Each leaf has a fall-color progress (0 = green, 1 = brown). The chance of falling on any given day scales with color progress — green leaves rarely fall, brown leaves fall quickly.

**Cleanup**: `CleanupOrphanedLeaves()` runs at spring start and removes clusters from nodes that were trimmed.

---

## SaveManager

`SaveManager` serializes the full tree graph (all `TreeNode` fields) to JSON in `Application.persistentDataPath`. On load, it reconstructs `allNodes`, re-links parent/child references, then calls `meshBuilder.SetDirty()` and `LeafManager.ClearAllLeaves()` followed by `SpawnSpringLeaves()` to restore the visual state.

When adding new `TreeNode` fields, you must add them to `SaveManager.SaveNodeData` and `LoadNodeData` or they won't persist.

---

## Coordinate spaces: a frequent source of confusion

All `TreeNode.worldPosition` and `TreeNode.growDirection` values are in **local space** — relative to the tree's GameObject transform. They are NOT Unity world space despite the field name "worldPosition."

To go from node position to Unity world space: `skeleton.transform.TransformPoint(node.worldPosition)`  
To go from a world-space direction to local: `skeleton.transform.InverseTransformDirection(worldDir)`

`AutoStyler.SlotTargetDirection()` returns a LOCAL space direction — this is correct because it's stored directly into `node.growDirection`.  
`AutoStyler.GetBranchAzimuth()` converts to world space first, reads the horizontal angle, then discards world space — also correct.

**`TrunkWaypoint.heightAboveSoil`** is named as if it's world units, but the default values (0.10, 0.30, 0.60, 1.00) and the code that uses them treat it as a 0–1 normalized fraction of tree height. Don't set these to actual meter values expecting them to work correctly — they're proportional fractions.

---

## Common bug patterns

### "The thing I changed isn't showing up in the mesh"
`meshBuilder.SetDirty()` was not called after the geometry change. Add it.

### "My action fires immediately / never fires"
Check the `InGameDay()` value at schedule time vs fire time. If `fireDaytime` is computed relative to now correctly but fires immediately, the `now >= fireDaytime` check in `UpdatePendingActions` is true on the same frame — add a minimum delay offset.

### "The GL indicators don't appear"
GL code must be in `OnRenderObject()`, not `Update()`. The GL material must use `ZTest Always` to draw through geometry. Check that `showTargetShape = true` in AutoStyler Inspector.

### "AutoStyler keeps trimming the wrong branches"
`RefreshSlots()` runs in spring. Add a log to see which node each slot is matching to. The 150° azimuth tolerance means a branch can be up to 150° off from the slot's target azimuth and still get assigned (it'll be wired toward the target rather than trimmed). If a branch is still being trimmed, it either: (a) wasn't matched to any slot, or (b) its specific tier is over-capacity.

### "Tree isn't growing"
Check: `isGrowing` on the node, `GameManager.SeasonalGrowthRate > 0` (must be March–July), `node.health > 0.25`, `node.subdivisionsLeft == 0` (must be zero to branch). Add a `Debug.Log` in TreeSkeleton's grow loop at the node in question.

### "Back-budding isn't happening"
`backBudStimulated` must be set on the ancestor (done by TrimNode). `backBudBaseChance` and `backBudActivationBoost` in the TreeSkeleton Inspector control probability. The back-bud roll happens in `StartNewGrowingSeason()`.

---

## Files you're most likely to edit

| File | When |
|------|------|
| [Tree/AutoStyler.cs](../Assets/Scripts/Tree/AutoStyler.cs) | Changing how the style system decides to trim/wire/pinch |
| [Tree/TreeSkeleton.cs](../Assets/Scripts/Tree/TreeSkeleton.cs) | Changing growth, branching, health, watering logic |
| [Tree/TreeNode.cs](../Assets/Scripts/Tree/TreeNode.cs) | Adding new per-node state (always update SaveManager too) |
| [Tree/StyleDefinition.cs](../Assets/Scripts/Tree/StyleDefinition.cs) | Changing what data a style asset holds |
| [buttonClicker.cs](../Assets/Scripts/buttonClicker.cs) | UI panels, stats display, tool button handlers |
| [GameManager.cs](../Assets/Scripts/GameManager.cs) | Game states, calendar, speed |
| [Editor/MoyogiGenerator.cs](../Assets/Editor/MoyogiGenerator.cs) | Debug tree injection |

Files you probably won't need to touch:
- `TreeMeshBuilder.cs` — mesh gen; only touch if you see rendering artifacts
- `TreeInteraction.cs` — input/highlight; only touch if player tool feedback is wrong
- `CameraOrbit.cs` — camera; only touch for camera feel
- `LeafManager.cs` — leaves; only touch if leaf spawning/falling behavior is wrong
