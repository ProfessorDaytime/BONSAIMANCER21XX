# Ishitsuki System — Root-over-Rock

*Ishitsuki* (石付き) is the bonsai style where a tree's roots grip the surface
of a rock, with the combined root-and-rock sitting in a shallow tray.

---

## Overview

The Ishitsuki system covers the full pipeline from the player placing a rock and
orienting the tree, through the automatic generation of training wire and pre-grown
root cables, to the ongoing visual gripping of roots against the rock surface.

---

## Gameplay Flow

```
Player presses Roots button
    → ToggleRootPrune() → GameState.RootPrune
    → Tree lifts, roots revealed

Player presses Place Rock button
    → GameState.RockPlace
    → Rock becomes grabbable and positionable in 3D

Player confirms rock position
    → GameState.TreeOrient
    → Tree transform becomes rotatable/translatable onto rock

Player presses Confirm Orientation
    → GameManager.ConfirmRockOrient()
        → OnRockOrientConfirmed event fires
        → RockPlacer.OnOrientConfirmed()
            → skeleton.rockCollider assigned
            → skeleton.SpawnTrainingWires()
            → IshitsukiWire loops generated
        → State restores to preRootPruneState (usually BranchGrow or LeafFall)
```

---

## Key Flag: `isIshitsukiMode`

`bool isIshitsukiMode` on TreeSkeleton. Set to `true` inside `SpawnTrainingWires()`;
never reset after that. Gates:

- `PreGrowRootsToSoil()` is called each spring (normally suppressed)
- Initial trunk root direction uses steep downward angle instead of near-horizontal
- `ScaleIshitsukiCableRadii()` runs after the pipe model each `RecalculateRadii`
- Rock-deflection code in `ContinuationDirection` is active for root nodes

---

## RockPlacer.cs — Input Details

`RockPlacer` is a MonoBehaviour that handles all mouse input during `RockPlace`
and `TreeOrient` states. It subscribes to `OnGameStateChanged` and
`OnRockOrientConfirmed`.

### Rock Placement (`HandleRockPlace`)

| Input | Effect |
|---|---|
| Left-click on rock | Grab / release |
| Mouse move (grabbed, no RMB) | Rock tracks cursor projected onto horizontal plane |
| Scroll wheel (no RMB) | Raise / lower rock vertically |
| Right-drag | Yaw around world up + pitch around camera right |
| Right-drag + scroll | Roll around camera forward axis |

### Tree Orientation (`HandleTreeOrient`)

| Input | Effect |
|---|---|
| Left-click on tree mesh | Grab / release |
| Mouse move (grabbed, no RMB) | Translate tree on world XY |
| Scroll wheel (no RMB) | Translate tree on Z |
| Right-drag | Yaw + pitch |
| Right-drag + scroll | Roll |

### Inspector Fields

| Field | Default | Meaning |
|---|---|---|
| `treeTransform` | — | Ref to tree's root Transform |
| `rotateSensitivity` | 0.4 | Degrees per mouse pixel |
| `rollSensitivity` | 15 | Degrees per scroll tick |
| `liftSensitivity` | 0.15 | World units per scroll tick (rock vertical) |

---

## SpawnTrainingWires

Called once when `OnRockOrientConfirmed` fires. Lives in `TreeSkeleton.cs`.

**Step by step:**
1. Set `isIshitsukiMode = true`.
2. Lock current Y: `initY = transform.position.y`, zero out the lift offset.
3. Compute `soilY` from `rootAreaTransform.position.y` (or `plantingSurfacePoint.y` fallback).
4. Update `plantingSurfacePoint.y = soilY` so all downstream code uses the same value.
5. Store `rockCollider` reference on the mesh builder (enables grip-snap in `AddRing`).
6. Clear existing trunk-root children: `child.children.Clear()` for each direct root child.
   This forces `PreGrowRootsToSoil` to build fresh cables from the trunk tip outward
   rather than trying to extend existing mid-air chains.
7. Call `PreGrowRootsToSoil()`.

---

## PreGrowRootsToSoil — Draping Algorithm

Builds a pre-grown root cable for every trunk-root strand that hasn't yet reached `soilY`.
Called each spring (`StartNewGrowingSeason`) when `isIshitsukiMode` is true.

### Claimed-edge deduplication

Before the strand loop, a `List<Vector3> claimedEdges` is initialised. Each strand
claims its equatorial edge point; later strands rotate their scan direction in ~20°
steps (up to 8 retries, alternating left/right) until the new edge XZ is at least
`minEdgeSep = max(segLen × 1.5, 0.04)` from all claimed points. This prevents
multiple cables piling onto the same rock face.

### Per-strand loop (up to 120 steps)

Each step computes `targetY = baseWorld.y − segLen` and shoots a horizontal ray
from outside the rock inward along `strandXZ` at that Y. Four modes:

| Mode | Condition | Node placement |
|---|---|---|
| `exterior` | Horizontal ray hits rock | `hit.point + hit.normal × surfOffset` — floats one root diameter above surface |
| `toEdge` | Horizontal miss, downward ray confirms still under rock | Snap XZ to `edgeXZ` (equatorial edge), keep targetY — exits interior without tunnelling |
| `lowerFace` | After first exterior hit, horizontal miss, downward ray from edgeXZ hits lower hemisphere | `lowerHit.point + lowerHit.normal × surfOffset` — follows curved underside |
| `freeFall` | No rock contact at all | Drop straight down (`baseWorld.x/z`, targetY) |

The loop breaks when `baseWorld.y <= soilY + 0.05`.

### Sharp-angle guard

After `nodePos` and `tangent` are computed for a step, the angle between the
new tangent and the previous step's tangent is checked:

```
if (Vector3.Angle(prevTangent, tangent) > (180 - minCableAngleDeg))
    → override to freeFall (drop straight down)
    → stepMode = "freeFall(angleGuard)"
```

This prevents the visible U-turn kinks where a cable snaps back toward the
trunk to reach a higher rock vertex before continuing downward. `minCableAngleDeg`
(default 65°, Inspector-tunable) controls the threshold. Setting it lower allows
tighter bends; higher forces straighter cables. The log tag `freeFall(angleGuard)`
makes it easy to count how often it fires.

### Node creation

Each new node gets:
```
newNode.isRoot         = true
newNode.isTrainingWire = true   // exempt from rootVisibilityDepth cull
newNode.isGrowing      = false  // pre-grown: don't let Update tick it
newNode.length         = segLen
newNode.radius         = rootTerminalRadius
```

`startNode` (the direct-child trunk root that owns the chain) is also frozen:
`isGrowing = false`, `length = targetLength` — prevents `SpawnChildren` from
appending a second air-growing continuation alongside the cable.

---

## isTrainingWire Flag

`public bool isTrainingWire` on `TreeNode`. Set by `PreGrowRootsToSoil` on every
node it creates. Two effects:

1. **Mesh visibility** — `TreeMeshBuilder.ProcessNode` always renders these nodes
   regardless of `rootVisibilityDepth` or `renderRoots` (see §Root Visibility Cull below).
2. **Wire removal lock** — `TreeInteraction` prevents the player from removing these
   wires until `wireSetProgress >= 1.0` (~2 growing seasons).

---

## ScaleIshitsukiCableRadii

Called from `RecalculateRadii()` after the pipe model pass, every `RecalculateRadii`
invocation. Only runs when `isIshitsukiMode`.

**Purpose:** The pipe model propagates `rootTerminalRadius` uniformly up a straight
chain. This method overrides that to make cables thickest at the trunk base and
taper toward `rootTerminalRadius` at the tips, and to track trunk growth over seasons.

**Formula:**

```
trunkRadius = root.radius  (set by pipe model from branch children)

startNode (isRoot, parent == root, !isTrainingWire):
    radius = Max(trunkRadius × ishitsukiCableRadiusMultiplier, rootTerminalRadius)
    minRadius = same

each isTrainingWire node:
    chainDepth = hops up to the first non-training-wire ancestor (startNode)
    radius = Max(trunkRadius × ishitsukiCableRadiusMultiplier × 0.82^(chainDepth+1), rootTerminalRadius)
    minRadius = same
```

Taper factor `0.82` per step is hardcoded. `ishitsukiCableRadiusMultiplier` is
tunable in the Inspector.

---

## Root Visibility Cull (TreeMeshBuilder)

`ProcessNode` in `TreeMeshBuilder.cs` (line ~385):

```csharp
if (child.isRoot
    && !child.isAirLayerRoot
    && !child.isTrainingWire
    && !renderRoots
    && child.worldPosition.y < rootVisibilityDepth)
    continue;
```

Pre-grown Ishitsuki cables (`isTrainingWire = true`) skip this check entirely —
they always render, even below soil level, so the full cable from trunk to tray
is always visible.

`rootVisibilityDepth` (Inspector, default `0`) is in local Y space. Negative
values reveal shallow underground roots in normal mode.

---

## Rock Grip Snap (TreeMeshBuilder)

When `rockCollider` is assigned on the mesh builder (set by `SpawnTrainingWires`),
`AddRing()` calls `Physics.ClosestPoint()` for each root-segment vertex. If the
vertex is within ~0.01 units of the rock surface it is snapped to that surface,
making roots visually press into / hug the rock. Works only on convex MeshColliders.

---

## IshitsukiWire.cs

Generates the binding-wire loop meshes that visually hold the roots against the rock.
Created by `RockPlacer.OnOrientConfirmed()` after `SpawnTrainingWires` completes.

The wire loops follow the same silver→gold→orange→red colour lifecycle as branch wires
via `wireSetProgress`. They cannot be removed until `wireSetProgress >= 1.0`.

---

## Trunk Root Spawn Position (Bunching Fix)

In Ishitsuki mode, trunk roots are spawned offset from the trunk **center** to
the **bark surface**, so each cable has a distinct, spread-out origin:

```
localOutward = transform.InverseTransformDirection(outward).normalized
barkOffset   = Max(root.radius, 0.04)
startPos     = root.worldPosition + localOutward × barkOffset
```

Without this, all cables spawn at `root.worldPosition` (the same point) and
produce a dense white cluster before diverging. With the offset each cable
starts visually separated at the bark surface, matching how roots emerge from
a real trunk base.

---

## Inspector Fields Reference (TreeSkeleton)

| Field | Default | Meaning |
|---|---|---|
| `rockInfluenceRadius` | 0.4 | How close a root node must be to the rock surface before its direction deflects toward the surface tangent during live growth |
| `ishitsukiCableRadiusMultiplier` | 0.3 | Cable radius at the trunk base as a fraction of trunk radius. Tapers toward `rootTerminalRadius` at cable tips |
| `debugSoilYOverride` | false | When checked, `debugSoilY` is used instead of `plantingSurfacePoint.y` for `PreGrowRootsToSoil` |
| `debugSoilY` | -9999 | Sentinel: auto-populates from `plantingSurfacePoint.y` on first use when override is checked. Edit to nudge soil level for testing |
| `minCableAngleDeg` | 65 | Minimum angle (degrees) between consecutive cable segments. Steps that would bend back sharper than this fall straight down instead. Also exposed in the Settings menu Ishitsuki tab |

---

## Gotchas

- `isIshitsukiMode` is never reset. Once set it stays true for the rest of the
  session (and would need to be serialised/loaded for persistence).
- `soilY` is computed from `rootAreaTransform.position.y`, NOT `rockCollider.bounds.min.y`.
  The rock is partially buried so its bottom is below the tray surface — using the
  rock base would drape cables too short.
- The claimed-edges dedup uses XZ distance only. Two strands on opposite sides of
  a very small rock may still visually overlap at the base where the rock narrows.
- Trunk roots are created with a steep initial direction in Ishitsuki mode
  (`outward × 0.35 + Vector3.down`, ~70° down) so the first visible cable segment
  flows downward rather than shooting radially outward.
- Trunk root base positions are offset to the bark surface (`root.radius` outward
  in local space) to prevent all cables bunching at the trunk center. If `root.radius`
  is very small early in the tree's life, a minimum of 0.04 units is enforced.
- The sharp-angle guard (`minCableAngleDeg`) only overrides to freeFall — it never
  skips steps. A cable will still reach soil in the same number of steps; some steps
  just drop vertically instead of tracking the rock face.
- Pre-grown nodes have `isGrowing = false` permanently. They never resume live
  growth. Seasonal thickening comes entirely from `ScaleIshitsukiCableRadii`.
