# Wound System

---

## Overview

Trimming large branches leaves exposed cut faces (wounds) that drain the
parent node's health each growing season until they callus over. Players
seal wounds with cut paste to dramatically reduce the drain. Thin tip cuts
produce negligible wounds; removing a major branch without paste is a
meaningful health risk.

Wound geometry is embedded directly in the **unified tree mesh** via
`AddWoundCap()` in `TreeMeshBuilder`. No separate wound GameObject, material,
or MeshRenderer is created — the callus grows as part of the same mesh that
renders the whole tree.

---

## Data on TreeNode

| Field | Meaning |
|---|---|
| `hasWound` | This node has an exposed cut face |
| `woundRadius` | Radius of the removed branch at cut time (drives wound size and drain) |
| `woundFaceNormal` | `growDirection` of the removed branch (used to orient wound geometry) |
| `woundAge` | Growing seasons elapsed since the cut |
| `pasteApplied` | Player has sealed this wound; drain drops to zero; vertex.b = 1 |

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

`CreateWoundObject()` is called immediately after setting wound fields — it
creates an **empty** anchor `GameObject` (`_WoundAnchor_N`) to preserve all
existing book-keeping paths (heal loop, undo, `woundObjects` dict) without
needing to restructure them. No mesh or renderer is attached to this object.

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
- Anchor GameObject destroyed and removed from `woundObjects` dict.
- `meshBuilder.SetDirty()` rebuilds geometry without the wound cap.
- Health drain stops.

`seasonsToHealPerUnit` default is `20`. A wound of radius `0.05` (thin twig)
heals in 1 season. A wound of radius `0.5` takes 10 seasons.

---

## Wound Geometry — AddWoundCap

`TreeMeshBuilder.AddWoundCap(node, outerRingStart, heightV, axisUp, axisRight, baseCol)`
is called instead of `AddFlatCap` whenever `node.hasWound`.

```
healProgress = Clamp01(woundAge / seasonsToHeal)
outerR       = node's tip ring radius

1. Callus swell ring
   pushed outward by outerR × 0.12 × (1 − healProgress)
   Band: outer tip ring → swell ring

2. Optional inner closing ring (if healProgress > 0.15)
   radius = outerR × (1 − healProgress × 0.8)
   Band: swell ring → inner ring

3. Concave center vertex
   at tipPosition − axisUp × outerR × 0.35 × (1 − healProgress)
   Fan: inner ring (or swell ring) → center
```

As `healProgress` goes from 0→1:
- The swell ring shrinks back to flush.
- The inner ring opens from nothing to near-full coverage.
- The concave depression fills in.
- At `healProgress == 1` the result is effectively flat (wound removed on next
  dirty rebuild because `hasWound` is now false).

---

## Vertex Color Encoding (Wound Channels)

The bark shader reads four vertex color channels:

| Channel | Meaning |
|---|---|
| `vertex.r` | `isRoot` flag (unchanged from prior system) |
| `vertex.a` | `barkBlend` twig→mature (unchanged) |
| `vertex.g` | Wound intensity: `1 − (woundAge / woundFadeSeasons)`, clamped 0–1 |
| `vertex.b` | Paste mask: `1.0` if `node.pasteApplied`, else `0.0` |

`woundFadeSeasons` (default 8) is a `[SerializeField]` on `TreeMeshBuilder` —
separate from the heal timer so visual fade can differ from mechanical healing.

The shader blends three wound zones based on `vertex.g`:
- **Heartwood** — dark inner core color (`_WoundHeartColor`)
- **Cambium** — bright ring at the cut edge (`_WoundCambiumColor`)
- **Callus** — fleshy outer roll (`_WoundCallusColor`)

When `vertex.b > 0.5` (paste applied), all three zones are overridden by
`_PasteColor` (default: dark grey-brown paste).

---

## Cut Paste Application

**How the player applies it:**
`TreeInteraction` in Paste mode — player clicks a wounded node.
Calls `skeleton.ApplyPaste(node)`.

**What `ApplyPaste` does:**
1. Guard: `if (!node.hasWound || node.pasteApplied) return;`
2. Sets `node.pasteApplied = true`.
3. Calls `meshBuilder.SetDirty()` — next build encodes `vertex.b = 1` on the
   wound cap geometry, triggering the paste color override in the shader.

**GameState requirement:** `canPaste` flag must be true. This is set by the UI
when the Paste button is active.

---

## Wound Anchor Lifecycle

`woundObjects` in `TreeSkeleton` is a `Dictionary<int, GameObject>` keyed by
`node.id`. It stores the empty anchor GameObjects.

**Creation:** `CreateWoundObject(node)` — called immediately after wound fields are set in `TrimNode`.

**Destruction:**
- Node trimmed: `RemoveSubtree()` destroys wound anchor.
- Wound heals: `StartNewGrowingSeason()` destroys and removes from dict.
- Subtree removed for any reason: covered by `RemoveSubtree()`.

---

## Inspector Fields (TreeSkeleton)

| Field | Default | Meaning |
|---|---|---|
| `woundDrainRate` | 0.05 | Health lost per growing season per wounded node (if no paste) |
| `seasonsToHealPerUnit` | 20 | Growing seasons to heal per unit of `woundRadius` |

## Inspector Fields (TreeMeshBuilder)

| Field | Default | Meaning |
|---|---|---|
| `woundFadeSeasons` | 8 | Seasons over which wound vertex.g fades for visual purposes |

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
- The empty `_WoundAnchor_N` anchor GameObject has no renderer. Never assign a
  MeshFilter or material to it — the wound face lives in the unified tree mesh.
