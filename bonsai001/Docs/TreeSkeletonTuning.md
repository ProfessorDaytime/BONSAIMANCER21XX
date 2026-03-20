# TreeSkeleton — How It Works & Inspector Tuning Guide

---

## How the Tree Grows (Overview)

```
Water button clicked
    → InitTree() creates the root node (depth 0, isGrowing=true)
    → BranchGrow state begins

Each frame (Update):
    → All nodes with isGrowing=true grow by speed * inGameDays
    → When a node reaches targetLength:
        → isGrowing = false
        → SpawnChildren() fires → creates 1 apical + 0-1 lateral child(ren)
        → Children start with isGrowing=true and immediately join the chain

September arrives:
    → State → TimeGo, isGrowing=false on skeleton
    → All in-progress nodes pause (node.isGrowing stays true, just not ticked)

Next April:
    → State → BranchGrow, isGrowing=true
    → StartNewGrowingSeason() fires once per year
        → Looks for finished terminal nodes with no children yet → sprouts them
        → In-progress nodes from last year resume automatically via Update()
```

**Year-over-year growth** comes from two sources:
1. **Resumed chain** — nodes that were still growing when winter hit pick up where they left off
2. **New spring buds** — any finished terminals that didn't get children (can happen if tree was trimmed, or nodes finished right at end of season) get fresh buds from `StartNewGrowingSeason`

---

## Why the Trunk Thickens

The **da Vinci pipe model** runs bottom-up after every structural change:

```
parent.radius = sqrt( sum of all child.radius² )
```

- Every new node starts at `terminalRadius` (e.g., 0.04)
- Non-terminal nodes get their radius SET by the pipe model — they don't keep their own value
- More branches = more squares summed = thicker parent = thicker trunk
- **Thickening is driven entirely by how many branches accumulate over the years**

If the trunk isn't thickening, the tree doesn't have enough branches yet. More years + high `springLateralChance` = more branches = thicker trunk.

---

## Inspector Parameters

### Growth Speed

| Parameter | Default | What It Does |
|-----------|---------|--------------|
| `baseGrowSpeed` | 0.2 | Units of length grown per **in-game day** at depth 0. Increase to make the tree grow faster through the season. |
| `depthSpeedDecay` | 0.85 | Each depth level, growth speed is multiplied by this. At 0.85, depth-10 grows at 20% of depth-0 speed; depth-20 grows at 4%. **This is the main lever for how far the chain gets each season.** Lower = deeper chain each year; too low = deep branches take decades. |

**Key math:** Time to complete a segment at depth D =
`targetLength(D) / (baseGrowSpeed × depthSpeedDecay^D × seasonalRate)`

At default values:
- Depth 0: ~10 days — Depth 10: ~38 days — Depth 20: ~145 days — Depth 25: ~290 days

Depth 25+ segments take multiple seasons to complete. This is intentional (deep fine branches grow slowly) but if it feels too slow, raise `depthSpeedDecay` toward 0.95–0.98.

**Recommended range:** 0.88–0.98. At 0.98 all depths grow at nearly the same speed. At 0.80 deep branches become nearly frozen.

---

### Segment Lengths

| Parameter | Default | What It Does |
|-----------|---------|--------------|
| `rootSegmentLength` | 2.0 | Length of the trunk (depth 0) segment. |
| `segmentLengthDecay` | 0.80 | Each depth level, segments get this much shorter. At 0.80, depth-5 segments are 33% of trunk length. Clamped to minimum 0.3 below depth ~10. |

Shorter segments = more detail, denser-looking tree. If segments feel too long or too stubby, adjust these first.

---

### Radii

| Parameter | Default | What It Does |
|-----------|---------|--------------|
| `terminalRadius` | 0.04 | The starting radius of **every** new node. The pipe model overrides non-terminal radii upward from this. Think of it as "twig thickness." Increase for chunkier trees; decrease for more delicate branching. |

---

### Branching

| Parameter | Default | What It Does |
|-----------|---------|--------------|
| `baseBranchChance` | 0.75 | Probability of a lateral branch spawning in `SpawnChildren` (mid-season chain growth). This is **depth-decayed** — see below. |
| `branchChanceDepthDecay` | 0.65 | Multiplied per depth level against `baseBranchChance`. At 0.65: depth 0 = 75%, depth 3 = 21%, depth 6 = 6%. Deep branches rarely get laterals in-season. |
| `springLateralChance` | 0.80 | Flat probability used by `StartNewGrowingSeason` — no depth decay. Controls how many terminals get a lateral bud each spring. **This is the main driver of trunk thickening year over year.** |
| `maxDepth` | 50 | Hard safety cap. Not a season budget. The season length (140 in-game days) controls how deep the chain gets each year. Leave at 50 unless you want a hard limit. |

**Branching and thickening:** More laterals = more total branches = bigger `sqrt(sum of squares)` = thicker trunk. If the trunk isn't getting thick enough over years, raise `springLateralChance` toward 0.95.

---

### Direction

| Parameter | Default | What It Does |
|-----------|---------|--------------|
| `inertiaWeight` | 0.65 | How strongly each new segment continues its parent's direction. Higher = straighter, more columnar tree. |
| `phototropismWeight` | 0.20 | How strongly new segments bend upward. Higher = more vertical growth. |
| `randomWeight` | 0.15 | Magnitude of random perturbation. Higher = more chaotic, natural-looking silhouette. |
| `branchAngleMin` | 25° | Minimum angle laterals deviate from parent direction. |
| `branchAngleMax` | 55° | Maximum angle. Wider range = more dramatic branching shape. |

The three weights don't need to sum to 1 — the result is always normalized. You can push them around freely.

---

## Common Tuning Scenarios

**"Tree finishes growing too early in the season"**
→ Reduce `baseGrowSpeed` (e.g., 0.1) or reduce `depthSpeedDecay` slightly (e.g., 0.90)

**"Tree barely grows year 2 and beyond"**
→ Raise `depthSpeedDecay` toward 0.92–0.95 — deep nodes are currently taking too long to resume and complete
→ Also raise `springLateralChance` so more fresh buds are added each spring

**"Trunk isn't thickening"**
→ Raise `springLateralChance` (more laterals per spring = more branches = thicker trunk via pipe model)
→ More years of growth needed — trunk thickens accumatively

**"Branches are too straight / too wiggly"**
→ Adjust `inertiaWeight` (higher = straighter) and `randomWeight` (higher = wigglier)

**"Tree is too bushy / too sparse"**
→ Adjust `baseBranchChance` and `springLateralChance`
→ Also adjust `branchChanceDepthDecay` — lower = fewer deep laterals

---

## Seasonal Growth Rates (SeasonalGrowthRate)

| Month | Rate | Effect |
|-------|------|--------|
| March | 0.3 | Buds break, slow start |
| April | 1.0 | Peak growth |
| May | 1.0 | Peak growth |
| June | 0.6 | Slowing |
| July | 0.5 | |
| August | 0.4 | Winding down |
| Sept–Feb | 0.0 | Dormant |

Growth speed is multiplied by this rate each frame. The tree naturally grows faster in April/May and slows through summer, which extends the "visible growing season" because slower months let the calendar advance without the chain finishing.
