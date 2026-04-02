# Wound System

---

## Overview

Trimming large branches leaves exposed cut faces (wounds) that drain the
parent node's health each growing season until they callus over. Players
seal wounds with cut paste to dramatically reduce the drain. Thin tip cuts
produce negligible wounds; removing a major branch without paste is a
meaningful health risk.

---

## Data on TreeNode

| Field | Meaning |
|---|---|
| `hasWound` | This node has an exposed cut face |
| `woundRadius` | Radius of the removed branch at cut time (drives wound size and drain) |
| `woundFaceNormal` | `growDirection` of the removed branch (used to orient the wound mesh) |
| `woundAge` | Growing seasons elapsed since the cut |
| `pasteApplied` | Player has sealed this wound; drain drops to zero |

---

## When Wounds Are Created

`TrimNode()` creates a wound on the **parent** of the cut node when:
- Parent exists, is not a root, and is not the virtual trunk root.

**Two wound types based on depth:**

| Type | Condition | `woundRadius` | Description |
|---|---|---|---|
| Subdivision cut | `node.depth == parent.depth` | `node.radius × 0.35` | Small nip — same-depth subdivision snip |
| Full branch cut | depth mismatch | `node.radius` | Full callus needed — major limb removed |

Initial state: `woundAge = 0`, `pasteApplied = false`.

If the parent node already has a wound, it is replaced by the new one (re-cutting
a stump resets the wound).

---

## Health Drain Per Season

Applied in `StartNewGrowingSeason()` for every node with `hasWound && !pasteApplied`:

```
ApplyDamage(node, DamageType.WoundDrain, woundDrainRate)
```

`woundDrainRate` default is `0.05` health per season. A large wound (`woundRadius`
≈ 1) on an unprotected branch will drain the node to dead health in ~20 seasons
if left untreated.

Once `pasteApplied = true` the drain stops entirely (the check is skipped).

---

## Wound Healing

Each growing season `woundAge++`. Healing threshold:

```
seasonsToHeal = Max(1, woundRadius × seasonsToHealPerUnit)
```

When `woundAge >= seasonsToHeal`:
- `node.hasWound = false`
- Wound GameObject destroyed and removed from `woundObjects` dict.
- Health drain stops.

`seasonsToHealPerUnit` default is `20`. A wound of radius `0.05` (thin twig)
heals in 1 season. A wound of radius `0.5` takes 10 seasons.

---

## Cut Paste Application

**How the player applies it:**
`TreeInteraction` in Paste mode — player clicks a wounded node.
Calls `skeleton.ApplyPaste(node)`.

**What `ApplyPaste` does:**
1. Guard: `if (!node.hasWound || node.pasteApplied) return;`
2. Sets `node.pasteApplied = true`.
3. Tints the wound GameObject slightly lighter (RGB channels ×1.1–1.15) to
   give visual feedback that paste has been applied.

**GameState requirement:** `canPaste` flag must be true. This is set by the UI
when the Paste button is active.

---

## Wound Mesh Lifecycle

**Creation:** `CreateWoundObject(node)` — called immediately after the wound fields
are set in `TrimNode`.

Builds a half-torus mesh:
```
visR    = Max(node.woundRadius, node.tipRadius) × 1.1   (outer radius)
minorR  = visR × 0.2                                     (tube radius)
Verts:   BuildHalfTorusMesh(visR, minorR, 12 segments, 6 rings)
```

Positioned at: `transform.TransformPoint(node.tipPosition + node.woundFaceNormal × (minorR × 0.5))`
Oriented with: `Quaternion.FromToRotation(Vector3.up, worldFaceNormal)`
Material: `woundMaterialOverride` if assigned; otherwise fallback Unlit brown `(0.28, 0.18, 0.10)`.
Stored in: `Dictionary<int, GameObject> woundObjects` keyed by `node.id`.

**Destruction:**
- Node trimmed: `RemoveSubtree()` destroys wound object.
- Wound heals: `StartNewGrowingSeason()` destroys and removes from dict.
- Subtree removed for any reason: covered by `RemoveSubtree()`.

---

## Inspector Fields (TreeSkeleton)

| Field | Default | Meaning |
|---|---|---|
| `woundDrainRate` | 0.05 | Health lost per growing season per wounded node (if no paste) |
| `seasonsToHealPerUnit` | 20 | Growing seasons to heal per unit of `woundRadius` |
| `woundMaterialOverride` | null | Optional material for wound mesh; defaults to Unlit brown |

---

## Gotchas

- Wounds are placed on the **parent** of the trimmed node, not the node itself.
  If you trim the very root of the tree (depth 0), no wound is created (guard
  prevents it).
- `woundAge` is a counter of **growing seasons**, not real-time or in-game days.
  A wound set in autumn before a long winter still only ages by 1 when the next
  spring arrives.
- The subdivision-cut wound (`woundRadius × 0.35`) is intentionally tiny. It
  represents the player pinching off a sub-segment rather than a full branch, and
  will heal in a single season at default settings.
- Paste is purely mechanical — it stops drain immediately but does NOT accelerate
  healing. `woundAge` still increments at the same rate.
- Multiple wounds on one node: the current implementation replaces rather than
  accumulates. If a branch is cut, allowed to callus, then cut again, the new
  wound starts fresh.
