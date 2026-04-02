# Air Layer System

---

## Overview

Air layering is a propagation technique where a band of bark is removed from
a branch, wrapped in moist medium, and eventually forms roots in open air.
In-game it's a way to develop above-ground roots on a trunk or thick branch —
useful for creating surface-root flare (nebari) higher up, or for Ishitsuki
preparation. After `airLayerSeasonsToRoot` growing seasons, the wrap can be
removed and the roots are permanently visible.

---

## Gameplay Flow

```
Player selects Air Layer tool (canAirLayer = true)
    → Clicks a branch node
    → PlaceAirLayer(node) called
        → Wrap object spawned around node
        → AirLayerData added to airLayers list

Time passes (airLayerSeasonsToRoot seasons, default 2)
    → rootsSpawned = true (set in StartNewGrowingSeason)

Player clicks wrap to remove it
    → UnwrapAirLayer(layer) called
        → Root strands spawned on cylindrical surface of trunk node
        → Wrap GameObject destroyed
```

---

## Data

### AirLayerData (internal struct / class on TreeSkeleton)
| Field | Meaning |
|---|---|
| `node` | The TreeNode the layer was placed on |
| `wrapObject` | The wrap mesh GameObject |
| `seasonsElapsed` | Growing seasons since placement |
| `rootsSpawned` | True once seasonsElapsed >= airLayerSeasonsToRoot |

### TreeNode fields
| Field | Meaning |
|---|---|
| `isAirLayerRoot` | True for every segment of a root spawned from an air layer |

---

## UnwrapAirLayer — Step by Step

Called when the player removes the wrap. The entry guard ensures roots have
developed: `if (!layer.rootsSpawned) return;`

1. Compute spawn parameters:
   ```
   spawnRadius = Max(layer.node.radius × airLayerRootRadiusMultiplier, terminalRadius)
   spawnLength = Max(airLayerRootTargetLength, 0.1)
   ```

2. Spawn `airLayerRootCount` root strands (default 17), evenly spaced radially:
   ```
   angle = i × (360° / airLayerRootCount)
   radialXZ = (cos(angle), 0, sin(angle))
   basePos = layer.node.tipPosition + radialXZ × layer.node.radius
   ```

3. Each strand has `airLayerRootSegments` nodes (default 3):
   - Direction: `radialXZ + Random.insideUnitSphere × 0.15` (slight random splay)
   - Radius tapers: `segRadius *= 0.8` per segment
   - All segments same length (`spawnLength`)
   - Each node: `isRoot = true`, `isAirLayerRoot = true`

4. Destroy wrap GameObject, remove from `airLayers` list.
5. `RecalculateRadii(root)` + `meshBuilder.SetDirty()`.

---

## isAirLayerRoot Flag — Effects

### Rendering
Air-layer roots are **always rendered** regardless of `rootVisibilityDepth` or
whether `renderRoots` is true. They sit above ground by design and should
always be visible.

The cull check in `TreeMeshBuilder.ProcessNode`:
```csharp
if (child.isRoot && !child.isAirLayerRoot && !child.isTrainingWire
    && !renderRoots && child.worldPosition.y < rootVisibilityDepth)
    continue;
```
`isAirLayerRoot` short-circuits the entire check.

### Radii — ScaleAirLayerRootRadii
Called from `RecalculateRadii()` after the pipe model pass (every `RecalculateRadii`
invocation). For each node with `isAirLayerRoot`:

1. Walk up the parent chain to find the first non-air-layer ancestor (the trunk node).
   Count `chainDepth` (how many air-layer hops to that ancestor).
2. Formula:
   ```
   r = Max(trunkNode.radius × airLayerRootRadiusMultiplier × 0.8^chainDepth, terminalRadius)
   ```
3. Set both `node.radius` and `node.minRadius` to `r`.

This makes the first segment (directly on the trunk surface) thickest, tapering
outward, and scales with trunk growth over seasons.

### Positions — UpdateAirLayerRootPositions
Called every frame in `Update()`. Multi-pass (one pass per `airLayerRootSegments`)
so chain corrections propagate through all segments in one frame:

- **Chain segments** (parent is also air-layer root): `node.worldPosition = parent.tipPosition`
- **First segment** (parent is trunk): re-anchor to cylindrical surface of trunk:
  ```
  radialDir = (node.worldPosition - parent.tipPosition) projected to XZ
  node.worldPosition = parent.tipPosition + radialDir.normalized × parent.radius
  ```
  This keeps roots flush to the trunk bark as the trunk thickens, preventing them
  from being swallowed into the mesh.

---

## Inspector Fields (TreeSkeleton)

| Field | Default | Meaning |
|---|---|---|
| `airLayerWrapPrefab` | — | GameObject instantiated as the wrap visual |
| `airLayerSeasonsToRoot` | 2 | Growing seasons before the player can unwrap |
| `airLayerRootCount` | 17 | Number of root strands spawned radially on unwrap |
| `airLayerRootSegments` | 3 | Segments per strand |
| `airLayerRootTargetLength` | 1.0 | Length of each segment (world units) |
| `airLayerRootRadiusMultiplier` | 0.35 | Root radius at the trunk surface as a fraction of trunk node radius |

---

## Gotchas

- Air-layer roots spawn at the **tip** of the layered node (`layer.node.tipPosition`).
  Placing a layer on a very long segment means roots sprout at the far end.
- The multi-pass position update runs every frame regardless of game state.
  This ensures roots track wire-bent parent nodes instantly without waiting for
  a growth tick.
- `isAirLayerRoot` is never cleared — once set, a node is always treated as an
  air-layer root for rendering and radius purposes.
- Air-layer roots participate in the pipe model normally (they are root children
  of a branch node, not of `skeleton.root`). The skip rule in
  `RecalculateRadiiInternal` (`if (!node.isRoot && child.isRoot) continue`) still
  applies at the branch→root junction, so they don't inflate branch radii.
  `ScaleAirLayerRootRadii` runs after and overrides the flat `terminalRadius`
  result with the trunk-proportional value.
- `canAirLayer` flag must be true for the UI to surface the tool. It is set by
  `GameManager` based on game state (similar to `canTrim`, `canWire`).
