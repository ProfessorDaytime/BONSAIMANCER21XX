# Nebari System

*Nebari* (根張り) is the visible surface root flare at the base of a bonsai tree.
A well-developed nebari — roots radiating evenly outward in all directions close
to the soil surface — is one of the primary aesthetic qualities judged in bonsai.

---

## Overview

The nebari system evaluates the quality of a tree's surface root flare and
exposes that quality as a **0–100 score**. The score is visible in the UI during
Root Prune mode and updates automatically each spring and each time the player
opens the Roots panel.

---

## What Gets Scored

Only **surface roots** are considered — root nodes that meet all of the following:

| Criterion | Default value | Inspector field |
|-----------|--------------|-----------------|
| `depth` between 1 and `nebariMaxDepth` | 3 | `Nebari Max Depth` |
| `tipPosition.y > -nebariSurfaceDepth` (close to soil surface) | 0.3 units | `Nebari Surface Depth` |
| `isRoot = true` and `isTrimmed = false` | — | — |

Depth 0 is the trunk root node itself (not scored). Depth 1–3 captures the first
few branching generations off the trunk — the visible flare region. Fine roots
deeper in the tray are ignored.

`Y = 0` is the soil surface in local space. Roots that dive immediately below
`-nebariSurfaceDepth` are considered buried and do not contribute.

---

## Score Components

The final score is a weighted sum of three components, multiplied by 100:

```
NebariScore = (angularScore × 0.50 + girthScore × 0.30 + balanceScore × 0.20) × 100
```

### 1. Angular Coverage — 50%

The horizontal plane around the trunk is divided into **8 sectors of 45°** each
(N, NE, E, SE, S, SW, W, NW). For each qualifying surface root, its tip position
is projected onto the XZ plane and assigned to a sector by angle.

```
angularScore = sectors_with_any_roots / 8
```

A tree with roots in all 8 directions scores 1.0. A tree with roots only on one
side scores 0.125.

### 2. Girth — 30%

The average radius of all qualifying surface root nodes is compared against a
target "ideal" radius.

```
girthScore = clamp(averageRadius / nebariTargetRadius, 0, 1)
```

Roots thicken naturally over time via the pipe model (children make parents
thicker). Young trees will score low here; a mature, well-developed base scores
high.

| Inspector field | Default | Meaning |
|-----------------|---------|---------|
| `Nebari Target Radius` | 0.04 units | Radius considered "fully developed" |

### 3. Radial Balance — 20%

The centre of mass of all qualifying surface roots (weighted by radius) is
computed in the XZ plane. A balanced tree has its centre of mass near the trunk
origin `(0, 0)`.

```
balanceScore = clamp(1 - centreOfMass.magnitude / nebariBalanceRadius, 0, 1)
```

If all roots grow to one side, the centre of mass shifts outward and this
component approaches zero.

| Inspector field | Default | Meaning |
|-----------------|---------|---------|
| `Nebari Balance Radius` | 1.5 units | Offset at which balance score reaches zero |

---

## UI Display

The **Nebari panel** appears in the bottom-left corner of the screen whenever
Root Prune mode is active. It contains:

- **Score number** — the current 0–100 score, rounded to the nearest integer
- **8 sector squares** — one per directional sector, coloured from dark grey
  (no roots) to green (well-covered). The brightest square is the best-covered
  direction; all others are scaled relative to it.

The panel is hidden outside Root Prune mode to keep the screen uncluttered
during normal trimming and wiring.

---

## When the Score Updates

| Event | Trigger |
|-------|---------|
| Player opens Roots panel | `OnGameStateChanged → RootPrune` |
| Growing season ends | End of `StartNewGrowingSeason` |

The score does **not** update in real-time as roots grow during the spring
animation — it reflects the state at the start of Root Prune mode or end of
the season.

---

## How to Improve Nebari

| Goal | Action |
|------|--------|
| Better angular coverage | Use Root Prune mode to plant roots in gap directions; trim roots that cluster in the same sector |
| Better girth | Let the tree age; do not prune trunk-base roots aggressively |
| Better balance | Trim oversized roots on the dominant side; plant roots on the weak side |
| Surface exposure | Trim roots that dive immediately downward; encourage shallow-angled growth |

---

## Inspector Reference (TreeSkeleton)

All fields are under the **Nebari** header in the Inspector.

| Field | Default | Description |
|-------|---------|-------------|
| `Nebari Max Depth` | 3 | Maximum root depth included in scoring |
| `Nebari Surface Depth` | 0.3 | Y-depth threshold below soil surface (units) |
| `Nebari Target Radius` | 0.04 | Root radius considered fully developed |
| `Nebari Balance Radius` | 1.5 | Horizontal offset at which balance score = 0 |

---

## Code Location

| Item | Location |
|------|----------|
| Score fields and properties | `TreeSkeleton.cs` — `NebariScore`, `NebariSectorCoverage` |
| Scoring algorithm | `TreeSkeleton.RecalculateNebariScore()` |
| UI panel | `Assets/UI/ButtonUI.uxml` — `NebariPanel` |
| UI wiring + sector squares | `buttonClicker.cs` — `UpdateNebariDisplay()` |
