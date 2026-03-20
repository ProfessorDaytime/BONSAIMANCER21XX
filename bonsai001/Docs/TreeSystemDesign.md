# Bonsai Tree System — Design & Implementation Guide

## Overview

The tree system is rebuilt around two layers: a **skeleton** (pure data graph) and a **mesh builder** (visual output). The skeleton drives all logic — growth, wiring, seasonal state. The mesh builder reads the skeleton and produces one unified mesh, which fixes the per-cone lighting seam issue.

---

## Architecture at a Glance

```
GameManager  (seasons, states, time)
     │
TreeSkeleton (the graph of TreeNodes — all logic lives here)
     │
TreeMeshBuilder (reads skeleton, outputs one unified Mesh)
     │
MeshFilter / MeshCollider (Unity rendering & interaction)
```

Individual segment colliders for tool interaction are rebuilt from the skeleton alongside the mesh.

---

## Phase 1 — Skeleton and Unified Mesh

### The Problem with the Current System

Each `ProceduralCone` is a separate `GameObject` with its own mesh. Unity calculates lighting per-mesh from each mesh's local origin, so normals and shading restart at every junction. This cannot be fixed with a shader — the meshes must be joined.

### TreeNode (Data Class)

`TreeNode` is a plain C# class, not a MonoBehaviour. No GameObject, no Transform. It is the atom of the tree.

```
TreeNode
─────────────────────────────────
int       id
Vector3   worldPosition       // base of this segment
Vector3   growDirection       // unit vector pointing toward the tip
float     radius              // current radius at this segment's base
float     length              // current length of this segment
float     age                 // time this node has existed (seconds of grow-time)
bool      isTrimmed           // trimmed nodes stop growing and propagate no more children
bool      isTerminal          // true if this node currently has no children
TreeNode  parent              // null for root
List<TreeNode> children

// Wiring
bool      hasWire
Vector3   wireTargetDirection  // the direction this node is being bent toward
float     wireBendProgress     // 0..1 how far along the bend it is
```

### TreeSkeleton (MonoBehaviour on the root GameObject)

Owns the full list of `TreeNode` objects. Responsible for:
- Initializing the root node on first water
- Ticking growth each frame during growing seasons
- Propagating secondary thickening (pipe model) after any structural change
- Notifying `TreeMeshBuilder` when the graph changes

```csharp
public class TreeSkeleton : MonoBehaviour
{
    public List<TreeNode> allNodes;
    public TreeNode root;
    public TreeMeshBuilder meshBuilder;

    // Called by GameManager state changes
    public void OnSeasonChanged(Season season) { ... }
    public void Tick(float deltaTime) { ... }  // called each Update during grow season
    void RecalculateRadii() { ... }            // pipe model pass (bottom-up)
    void NotifyMeshDirty() { ... }             // triggers mesh rebuild
}
```

### TreeMeshBuilder (MonoBehaviour on the same GameObject)

Reads `TreeSkeleton.allNodes` and builds one unified `Mesh`. Called only when the skeleton is marked dirty (not every frame unless a node is actively growing or bending).

**How smooth normals work across junctions:**

At the junction between a parent segment tip and a child segment base, the builder averages the normals of the shared ring of vertices. The top ring of the parent and the bottom ring of the child occupy the same world positions — the builder writes them as shared vertices (or calculates the average normal and writes it to both). This is what makes the light flow smoothly across the whole tree.

```
Parent tip ring:   normals point outward+upward (blend of parent cylinder axis)
Child base ring:   normals point outward+upward (blend of child cylinder axis)
Shared normal:     average of both → smooth transition
```

**Mesh structure:**

For each `TreeNode`:
- Generate a ring of vertices at the base (`worldPosition`, `radius`)
- Generate a ring of vertices at the tip (`worldPosition + growDirection * length`, `childRadius or topRadius`)
- Connect with quad strips (same math as the existing `ProceduralCone`, just world-space now)

If a node has multiple children, the tip ring is shared among all children — one ring, multiple quad strips fanning out.

**Colliders:**

After mesh rebuild, create or update one `CapsuleCollider` per node (simplified, not mesh-accurate). Store a mapping from collider instance ID → `TreeNode` so tool scripts can look up which node was hit.

---

## Phase 2 — Growth Model

### Seasonal Growth Rates (Tokyo, temperate deciduous)

The `GameManager` already tracks month. Map each month to a growth rate multiplier:

| Month | Season State | Growth Multiplier | Notes |
|-------|-------------|-------------------|-------|
| January | Dormant | 0.0 | No growth |
| February | Dormant | 0.0 | |
| March | EarlySpring | 0.3 | Buds break, very slow start |
| April | Spring | 1.0 | Full growth |
| May | Spring | 1.0 | |
| June | Summer | 0.6 | Growth continues, slowing |
| July | Summer | 0.5 | |
| August | Summer | 0.4 | |
| September | Autumn | 0.0 | Growth stops; leaf hue shift begins |
| October | Autumn | 0.0 | Leaf fall |
| November | Autumn | 0.0 | Leaves mostly gone |
| December | Dormant | 0.0 | |

Add a `SeasonalGrowthRate` float property to `GameManager` that returns the current multiplier. `TreeSkeleton.Tick()` multiplies all growth values by this.

Watering acts as a **burst modifier** — it temporarily bumps the effective growth rate (e.g. ×1.5 for a few in-game days) during the growing season. Watering during dormancy does nothing for growth (the tree is just maintaining).

### Secondary Thickening — The Pipe Model

This is based on **da Vinci's rule**: the cross-sectional area of a branch equals the sum of the cross-sectional areas of all its children.

```
parent.radius² = sum of (child.radius²) for all children
```

In practice: after any growth tick, walk the tree **bottom-up** (leaves to root) and recalculate each node's radius. Terminal nodes have a base radius defined by their generation depth. As the tree adds more branches, the trunk automatically thickens — no manual tuning needed.

```csharp
void RecalculateRadii(TreeNode node)
{
    if (node.isTerminal)
    {
        // base radius stays at its initial growth value
        return;
    }
    float sumOfSquares = 0f;
    foreach (var child in node.children)
    {
        RecalculateRadii(child);           // recurse first (bottom-up)
        sumOfSquares += child.radius * child.radius;
    }
    node.radius = Mathf.Sqrt(sumOfSquares);
}
```

Call this once per growth tick after adding any new nodes.

### Direction — Phototropism and Randomness

Each growing tip calculates its next growth direction as a weighted blend:

```
newDirection = normalize(
    currentDirection  * 0.65   // inertia — branches don't suddenly reverse
  + sunDirection      * 0.20   // phototropism — grows toward light
  + randomOffset      * 0.15   // natural variation
)
```

**sunDirection** is mostly `Vector3.up` with a small horizontal offset based on the sun's position in the scene (the `skyLight` already in `GameManager`). You can read `skyLight.transform.forward` and invert it to get the direction light is coming from.

**randomOffset** is `Random.insideUnitSphere * 0.4f`. Use a per-node seed so the same tree grows consistently within a session, but differently each new game.

### Branching Rules (Stochastic L-System)

Each growing terminal node rolls for branching each growth tick. The probability depends on node age and depth:

```
branchChance = baseBranchChance * ageMultiplier * depthMultiplier
```

- `baseBranchChance` = 0.002 per tick (low — branching should feel like an event)
- `ageMultiplier` = increases with node age (older tips are more likely to branch)
- `depthMultiplier` = decreases with depth (deep branches branch less often)

When a branch does occur:
- The current node stops being terminal
- Two child nodes spawn at its tip
- One continues roughly in the parent direction (dominant / leader)
- One deviates by 20–45 degrees (subordinate branch)
- Both start with radius = `parent.radius * 0.7` (pipe model will correct this over time)

The root node (trunk) always spawns exactly two children on first growth to give the tree a base structure.

---

## Phase 3 — Tools

### Tool Tiers

Tools are selected from the UI and set `GameManager`'s active tool state. When the player clicks on a node in `Pruning` state, the hit node's radius is checked against the active tool's valid range.

| Tool | Valid Radius Range | GameObject Destroyed | Notes |
|------|-------------------|---------------------|-------|
| Shears | leaves only | leaf objects | Cannot cut branches |
| Small Clippers | 0 – 0.4 | node + all descendants | Thin branches, young growth |
| Big Clippers | 0.4 – 1.5 | node + all descendants | Thick branches, trunk sprouts |
| Saw | 1.5+ | node + all descendants | Trunk-level cuts only |

If the player clicks a node with the wrong tool, play a "wrong tool" sound and show a brief UI indicator — don't silently do nothing.

### Trimming a Node

When a valid cut is confirmed:

1. Mark `node.isTrimmed = true` on the cut node and every descendant
2. Remove all descendants from `TreeSkeleton.allNodes`
3. Mark the cut node's parent as `isTerminal = true`
4. Run `RecalculateRadii()` — the trunk will thin slightly (pipe model working in reverse)
5. Mark mesh dirty → rebuild

Trimmed nodes do not regrow. If the player wants growth from that point again, they must wait for the tree to naturally branch from a nearby node, or use wiring to redirect growth.

### Implementation in Unity

- Create a `ToolManager` MonoBehaviour (separate from GameManager)
- It exposes `ActiveTool` enum: `None, Shears, SmallClippers, BigClippers, Saw`
- `scr_Cyl_001` can be replaced — the click detection logic moves to a single `TreeInteraction` MonoBehaviour that reads `ToolManager.ActiveTool` and looks up the hit collider in the skeleton's collider map

---

## Phase 4 — Leaves

### Leaf Placement

Leaves grow on **terminal nodes** that are past a minimum depth (no leaves on the trunk, only on branch tips). Each terminal node can hold 3–7 leaves, clustered at the tip.

Each leaf is an instance of the existing leaf prefab, parented to the terminal node's position in world space. `Leaf.cs` stores:

```
TreeNode    ownerNode
float       hue          // 0 = green, 1 = red
Vector3     localOffset  // position relative to node tip
Quaternion  localRot
```

Leaves are spawned when a node becomes terminal and reaches a minimum age. They scale in from zero over a short duration (spring bud-burst feel).

### Seasonal Hue Shift

`GameManager` exposes a `float LeafHue` property (0–1) calculated from the current month:

| Month | LeafHue |
|-------|---------|
| April – August | 0.0 (green) |
| September | lerp 0.0 → 0.3 over the month |
| October | lerp 0.3 → 1.0 over the month |
| November | 1.0 (full red, leaves falling) |

Each `Leaf` reads `GameManager.LeafHue` each frame and sets it on the material:

```csharp
// In Leaf.Update():
float hue = GameManager.LeafHue;
Color leafColor = Color.HSVToRGB(Mathf.Lerp(0.33f, 0.0f, hue), 0.8f, 0.7f);
GetComponent<Renderer>().material.color = leafColor;
```

(HSV: 0.33 = green, 0.0 = red)

### Leaf Fall

In October–November, leaves fall off stochastically. Each `Leaf` rolls each day tick:

```
fallChance = baseFallChance * LeafHue   // more likely the redder the leaf
```

When a leaf falls:
- Play a small downward physics animation (add a `Rigidbody` temporarily, let it drift down over ~2 seconds)
- Destroy the GameObject after it lands or times out
- When all leaves on a node are gone, mark `node.hasLeaves = false`

Leaves re-grow in March when the growing season begins.

### Shears

Clicking a leaf cluster with Shears selected destroys all leaves on that terminal node immediately. This is how the player prunes leaf growth for styling.

---

## Phase 5 — Wiring

### How Bonsai Wiring Works (Real Life)

Wire is wrapped around a branch in a spiral. The gardener then bends the branch to a new angle. Over weeks or months the branch lignifies (hardens) in the new position. The wire is then removed.

In-game equivalent:
- Thin branches bend **immediately** when wired (young growth is flexible)
- Thick branches bend **slowly over game-time** (proportional to `1 / radius`)
- Wire is **visible** on the branch as a coiled mesh until explicitly removed

### Wiring Workflow (UI)

1. Player selects Wire tool → `GameState.Shaping`
2. Player clicks a node — this is the **anchor** (the fixed point)
3. Player clicks a second node on the same branch chain — this is the **target** (the tip to be moved)
4. A bezier curve gizmo appears between anchor and target using `BezierSpline.cs`
5. Player drags the bezier control point to define the desired curve
6. Player confirms → the wire is committed

On confirmation:
- Each node between anchor and target gets a `wireTargetDirection` set from the bezier curve tangent at that node's position along the curve
- Wire mesh is generated and parented to the branch
- Each frame, nodes lerp their `growDirection` toward `wireTargetDirection` at a rate of `bendRate / radius`

### Wire Mesh

The wire mesh is a thin tube (radius ~0.02 world units) that follows a helix around the branch segment. It uses a separate material (copper/aluminum look). The wire GameObject is parented to the affected node and rebuilds when the node's shape changes.

```
WireData (stored per wired node)
─────────────────────────────────
TreeNode   anchorNode
TreeNode   targetNode
GameObject wireMeshObject
bool       isFullyBent        // true when all nodes have reached their targets
```

### Removing Wire

Player selects Wire Removal tool → click any node that has a wire → `WireData` is removed, wire mesh destroyed, `wireTargetDirection` cleared. The branch **stays** in its new position (the `growDirection` values are already updated). This matches real bonsai — the shape holds after the wire comes off.

### Angle Limits

A branch cannot be bent past a maximum angle from its natural direction. The limit scales with radius:

```
maxBendAngle = Mathf.Lerp(90f, 15f, Mathf.Clamp01(node.radius / 1.5f))
```

Thin branches (radius ≈ 0): up to 90° bend allowed
Thick branches (radius ≈ 1.5+): only ~15° bend allowed

If the player's bezier control point would require a bend beyond this limit, clamp the angle and show a visual indicator.

---

## Existing Code — What to Keep

| File | Decision | Notes |
|------|----------|-------|
| `GameManager.cs` | Keep, extend | Add `SeasonalGrowthRate`, `LeafHue`, tool state integration |
| `ProceduralCone.cs` | Keep math, retire MonoBehaviour | The sin/cos vertex ring generation is reused inside `TreeMeshBuilder` |
| `scr_Cyl_001.cs` | Replace | Click detection moves to `TreeInteraction.cs` |
| `TreeRoot.cs` | Replace | `TreeSkeleton.cs` takes over |
| `BezierSpline.cs` | Keep, use directly | Powers the wiring curve gizmo |
| `Leaf.cs` | Implement | Add hue shift, fall logic, shears response |
| `AudioManager.cs` | Keep | Add "wrong tool" SFX, saw SFX |
| `TreeGen*.cs` | Archive | Already commented out, can delete eventually |

---

## File Structure (New Scripts)

```
Assets/Scripts/
├── Tree/
│   ├── TreeNode.cs            // plain C# data class
│   ├── TreeSkeleton.cs        // MonoBehaviour — owns the graph, drives growth
│   ├── TreeMeshBuilder.cs     // MonoBehaviour — reads skeleton, builds mesh
│   ├── TreeInteraction.cs     // MonoBehaviour — handles all tool clicks on tree
│   └── WireData.cs            // plain C# — wire state per affected node pair
├── Tools/
│   └── ToolManager.cs         // MonoBehaviour — active tool, UI hookup
├── Leaf.cs                    // (existing, implement)
└── GameManager.cs             // (existing, extend)
```

---

## Implementation Order Within Each Phase

### Phase 1
1. Write `TreeNode.cs`
2. Write `TreeSkeleton.cs` — just root node init and flat list management
3. Write `TreeMeshBuilder.cs` — generate a single static trunk cylinder, confirm smooth shading
4. Replace `TreeRoot.cs` hookup in the scene

### Phase 2
5. Add growth tick to `TreeSkeleton` — extend terminal nodes each frame
6. Add branching logic
7. Add pipe model `RecalculateRadii()`
8. Wire up `GameManager.SeasonalGrowthRate` and test full year cycle

### Phase 3
9. Write `ToolManager.cs`
10. Write `TreeInteraction.cs` (replaces `scr_Cyl_001`)
11. Implement trim logic in `TreeSkeleton`
12. Test all four tool tiers

### Phase 4
13. Implement `Leaf.cs` — spawn on terminal nodes
14. Add `GameManager.LeafHue` seasonal curve
15. Implement leaf fall stochastic logic

### Phase 5
16. Wire UI flow (anchor select → target select → bezier gizmo)
17. Commit wire → `wireTargetDirection` on nodes
18. Wire mesh generation
19. Bend-over-time logic in `TreeSkeleton.Tick()`
20. Wire removal tool
