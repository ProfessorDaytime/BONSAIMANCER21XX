# Bud System

---

## Overview

Buds make spring growth feel biological: the tree spends late summer setting
terminal buds, which are visible through winter and burst open in March.
Trimming releases dormant axillary buds lower on the branch (back-budding),
driving the ramification players need for fine bonsai structure.

---

## Data on TreeNode

| Field | Meaning |
|---|---|
| `hasBud` | Terminal bud set this late summer; will activate next spring |
| `backBudStimulated` | Tip ancestry was trimmed; boosted lateral chance next spring |

---

## Bud Set (Late Summer → September)

**Trigger:** `SetMonthText()` in `GameManager` hits month 9 →
`UpdateGameState(GameState.TimeGo)` → `TreeSkeleton.OnGameStateChanged` →
`SetBuds()`.

**What `SetBuds()` does:**

For every node in `allNodes`:
- Skip if `isTrimmed`, `isRoot`, or `subdivisionsLeft > 0`.
- **Terminal nodes** (no children, depth ≥ minLeafDepth): set `hasBud = true`,
  instantiate `budPrefab` at `node.tipPosition` facing `node.growDirection`,
  store in `budObjects[node.id]`.
- **Junction nodes** (has children, not a sub-segment): instantiate
  `lateralBudPrefab` if assigned, stored in `lateralBudObjects`.

Bud GameObjects remain visible through the dormant winter period.

---

## Bud Break (March)

**Trigger:** `SetMonthText()` hits month 3 → `UpdateGameState(GameState.BranchGrow)`
→ `StartNewGrowingSeason()`.

**What happens:**
1. All bud GameObjects from `budObjects` are destroyed.
2. `lateralBudObjects` are destroyed.
3. Terminal nodes with `hasBud = true` are treated as active growing tips —
   their `isGrowing` status is restored (they already have `isGrowing = true`
   if they never finished growing; if they finished, `SpawnChildren` kicks in).

Nodes that finished growing before winter resume naturally in the `Update`
growth loop since they still have `isGrowing = false` but check for children
spawning in `StartNewGrowingSeason`.

---

## Back-budding

**What sets `backBudStimulated`:**
`TrimNode()` — when a non-root branch is cut, the nearest **3 ancestors**
(walking up the parent chain, skipping root nodes) each get
`backBudStimulated = true`.

**What reads it:**
`StartNewGrowingSeason()` lateral-bud loop. For each node with
`backBudStimulated = true`:
```
if (Random.value < backBudBaseChance * backBudActivationBoost * vigorFactor)
    → spawn a lateral child on this node
```
The flag is consumed (reset to `false`) after the check, so it only fires
once per trim event.

**Intuition:** Removing a tip releases apical dominance — hormones redistribute
to dormant buds on older wood below the cut. The closer to the cut, the more
likely a bud breaks.

---

## Old-Wood Bud Chance

Each spring, **every** interior junction node (non-terminal, non-root,
depth < SeasonDepthCap) has a small spontaneous chance to sprout a lateral —
independent of trimming. This simulates random back-budding on mature wood.

```
if (Random.value < oldWoodBudChance * vigorFactor)
    → spawn lateral child
```

`oldWoodBudChance` is very low (default 0.01) so this produces occasional
surprise shoots rather than a burst of growth.

---

## vigorFactor

Both back-budding and old-wood budding are multiplied by `vigorFactor`
(computed each spring from `allNodes.Count / maxBranchNodes`). As the tree
fills up, vigor decreases, naturally limiting the total node count without
hard cutoffs feeling arbitrary.

---

## Inspector Fields (TreeSkeleton)

| Field | Default | Meaning |
|---|---|---|
| `budPrefab` | — | GameObject spawned at terminal tips each August |
| `lateralBudPrefab` | — | GameObject spawned at junctions (optional) |
| `budType` | `Alternate` | Bud arrangement (`Alternate` or `Opposite`) — affects lateral spawning pattern |
| `apicalDominance` | 0.3 | How strongly the lead tip suppresses laterals (0 = no suppression) |
| `oldWoodBudChance` | 0.01 | Per-node per-spring chance of spontaneous lateral on interior wood |
| `backBudBaseChance` | 0.15 | Base probability per stimulated node of activating a back-bud in spring |
| `backBudActivationBoost` | 1.0 | Multiplier on `backBudBaseChance` for trimming-stimulated nodes |
| `showTerminalBuds` | true | Toggle visibility of terminal bud prefabs |
| `showLateralBuds` | true | Toggle visibility of lateral bud prefabs |

---

## Gotchas

- `budObjects` is a `Dictionary<int, GameObject>` keyed by `node.id`. If a node
  is trimmed before September, `RemoveSubtree()` cleans up its bud object; no
  orphaned GameObjects.
- `lateralBudPrefab` is optional. If null, junction bud spawning is silently
  skipped — no error.
- Back-budding fires only for **branch** nodes (non-root). Root systems never
  receive `backBudStimulated`.
- `subdivisionsLeft > 0` prevents bud-set on nodes that are mid-subdivision
  (interior sub-segments of a long branch chord). Only the true terminal of a
  subdivision chain sets a bud.
- Setting `backBudActivationBoost` very high (e.g. 8–10) combined with frequent
  trimming can overwhelm the vigor cap and cause a burst of regrowth. Intended
  for species with aggressive back-budding; use with `maxBranchNodes` guard.
