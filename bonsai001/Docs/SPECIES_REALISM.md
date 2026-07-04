# Species Parameter Realism — Justification

Reference for the 17 `TreeSpecies` `.asset` files (`Assets/Scripts/Tree/Species/`). Most growth/water/wound values were already carefully tuned; this pass (2026-06-13) verified them against real horticulture, added the two new leaf fields to every species, fixed Ficus's growth window, added per-species **defoliation gating**, and corrected a few signature colors.

## What this pass changed

1. **`leafGrowDays` + `leafBudBreakColor` added to all 17** (previously defaulting to a generic 20 d / pale green). These drive the leaf-emergence look from item E: leaves spawn at 15% size tinted by `leafBudBreakColor`, maturing to `leafSpringColor` over `leafGrowDays`.
2. **`canDefoliate` (new field)** — `false` for all conifers. The AutoStyler's auto-defoliation pass now skips them (`TryAutoDefoliate`); defoliating a pine/spruce/juniper/cedar can kill it. The player's manual Defoliate tool is unaffected.
3. **Ficus growth window** — was `slow 152 / stop 183` (≈ June, a temperate window). Ficus is subtropical and flushes all summer → `slow 205 / stop 232`, so it uses the whole Mar–Aug growth season instead of stopping in June.
4. **Signature colors** — Japanese White Pine leaf turned blue-green (the "white" pine glaucous needle); Scots Pine mature bark warmed toward its famous orange-copper upper trunk.

## Why each species behaves as it does

| Species | Signature realism (key params) | Leaf flush |
|---|---|---|
| **Japanese Maple** | Fast (`baseGrowSpeed 0.26`, `depthsPerYear 4`), thirsty (`drainRatePerDay 0.14`), wires set fast (`wireDaysToSet 140`), seals wounds poorly (`woundDrainRate 0.08`), opposite buds (`budType 1`), free back-budding, weak apical dominance (0.15). | Reddish-bronze flush (`leafBudBreakColor` warm coral), 14 d. |
| **Juniper** | Slow (0.14), drought-hard (`droughtThreshold 0.18`), wires hold slowly (280), wound-resilient (0.03), strong apical dominance (0.55), repot-hardy (0.75), wants gritty free-draining soil (drainage 0.78, aeration 0.70). **No defoliation.** | Soft muted-green scale foliage, 30 d. |
| **Ficus** | Fast & tropical (0.30), heals fast (`seasonsToHealPerUnit 14`, `trimTraumaRecovery 0.10`), very repot-hardy (0.80), long internodes (`branchSegmentLength 2.0`), **grows all summer** (205/232). Smooth bark (type 1). | Glossy pale green, 18 d. |
| **Japanese Black Pine** | Slow (0.18), strong apical dominance (0.60), very slow wires (350), almost no back-budding (`oldWoodBudChance 0.005`), repot-sensitive (0.28), plated bark (type 9). **No defoliation.** | Whitish candle → bright green, 35 d. |
| **Japanese White Pine** | Slowest pine (0.12), highest apical dominance (0.65), slowest wires (380), repot-sensitive (0.25). Blue-green needles (corrected). **No defoliation.** | Pale blue-green candle, 40 d. |
| **Scots Pine** | Slow (0.16), apical (0.58), slow wires (320), drought-hard, famous orange upper bark (corrected `matureBarkColor`). **No defoliation.** | Pale lime candle, 35 d. |
| **Cherry (Prunus)** | Disease-prone (`fungalSpreadChance 0.35`, slow `fungalRecoveryRate 0.07`), seals cuts poorly (0.07), horizontal-lenticel bark (type 16). | Bronze-tinged young leaves, 15 d. |
| **Silver Birch** | Fast (0.28, `depthsPerYear 4`), thirsty (0.15), bleeds/seals poorly (`woundDrainRate 0.09`), peeling white bark (type 10, white colors). | Bright fresh green, 12 d (fast). |
| **Elm** | Fast, freely back-budding (`oldWoodBudChance 0.07`, `backBudBaseChance 0.18`), repot-hardy (0.65), ridged bark (type 4). | Bright green, small, 13 d. |
| **Wisteria** | Vigorous vine (0.35, `depthsPerYear 5`), very weak apical dominance (0.12), thirsty/hungry (`nutrientDrain 0.58`), late grower (161/196). | Bright green, 14 d. |
| **Weeping Willow** | Fastest (0.40), weakest apical dominance (0.10), extremely thirsty (`drainRatePerDay 0.22`, `droughtThreshold 0.55`), loves wet soil (retention 0.72), disease-prone (0.40), late grower. | Yellow-green, 11 d (fastest). |
| **Dawn Redwood** | Fast deciduous conifer (0.32), feathery foliage, vigorous late grower, likes moist soil (retention 0.62). **No defoliation.** | Lime-green feathery flush, 22 d. |
| **Swamp Cypress** | Wet-tolerant deciduous conifer (retention 0.72, low aeration 0.28), `droughtThreshold 0.50`, fibrous bark (type 14). **No defoliation.** | Lime-green feathery flush, 22 d. |
| **Atlas Cedar** | Slow (0.15), apical (0.50), slow wires (280), drought-hard, vertical-strip bark (type 7). **No defoliation.** | Pale blue-green, 30 d. |
| **Japanese Cedar (Cryptomeria)** | Medium (0.22), fibrous shredding bark (type 12), moderate everything. **No defoliation.** | Bright yellow-green, 28 d. |
| **Ezo Spruce** | Slow (0.12), apical (0.55), slow wires (300), repot-sensitive (0.35), blocky bark (type 8). **No defoliation.** | Apple-green new tips, 30 d. |
| **Alberta Spruce** | Slowest of all (0.08, `depthsPerYear 1`), dwarf conical habit, highest apical dominance, repot-cautious. **No defoliation.** | Bright apple-green tips, 32 d. |

## General principles encoded

- **Conifers** (pines, spruces, cedars, juniper, cryptomeria, deciduous redwood/cypress): slow growth, strong apical dominance, slow-setting wires (250–380 d), minimal back-budding, gritty free-draining soil, **never auto-defoliated**.
- **Deciduous broadleaf** (maple, elm, birch, cherry, willow, wisteria): fast growth, weak apical dominance, fast wires (100–170 d), free back-budding, defoliation-tolerant.
- **Wound response**: thin-barked bleeders (maple, birch, cherry, willow) drain health faster after cuts; pines/junipers shrug cuts off.
- **Water**: willow ≫ birch/maple ≫ cherry/elm/ficus ≫ cedar/spruce/pine/juniper. Drought threshold inverts (junipers stay healthy down to 0.18 moisture; willow stresses below 0.55).
- **Leaf flush speed**: fast for thin deciduous leaves (11–15 d), slow for conifer candles/needles (28–40 d).

---

## 2026-07-03 — Engine-Aware Retune (post Fable-5 engine pass)

The 2026-07-02/03 engine work (branch cap enforced on ALL spawn paths, leader exemption,
forced back-buds, fixed-timestep sim) changed what species parameters actually DO. This
pass re-tunes data against the new engine + adds `leafAutumnColor` (new field: fall now
runs spring→species-autumn→dried instead of one hardcoded green→brown ramp).

**Principles applied:**
- `springLateralChance > 0.8` never expressed "vigorous ramifier" — it just exhausted the
  2000-node budget in a few years and floored vigor for everything (flat Elm pancakes,
  8.6k-node Dawn Redwood before the cap fix). All heavy ramifiers pulled into **0.50–0.65**;
  species character now comes from depths/segment lengths/colours.
- `baseGrowSpeed < 0.14` = trunks that never complete segments in playable time (Juniper
  froze at 0.5 height for 20 years). Floor raised to **0.14–0.18** for slow conifers —
  still the slowest class, but alive.
- `branchChanceDepthDecay ≈ 0.9` lets depth-10+ twigs keep dividing → the leafless
  "winter broccoli" crown (Dawn Redwood report). Heavy species dropped to **0.82–0.85**.
- Juniper `youngBarkColor` warmed olive→reddish-tan (real juniper young bark), so fresh
  leader growth doesn't read as a green blob while it matures.

**Autumn colours set:** maple crimson, birch gold, elm/willow/wisteria yellow-golds,
cherry orange-red, dawn redwood/swamp cypress copper-rust, ficus stays green-yellow
(barely turns). Evergreens ignore the field.

**Changed:** Alberta Spruce, Atlas Cedar, Cherry, Dawn Redwood, Elm, Ezo Spruce, Ficus,
J. Cedar, J. Maple, J. White Pine, Juniper, Silver Birch, Swamp Cypress, Weeping Willow,
Wisteria. Unchanged: J. Black Pine, Scots Pine (already coherent).

### 2026-07-03 (later) — Ficus-envelope convergence
After seven engine-side anti-broccoli fixes, remaining density was traced to the data:
Ficus (branchSegmentLength 2.0, baseBranchChance 0.25, springLateralChance 0.08) has
produced beautiful trees in this engine from day one; every broccoli species branched
2–6× more often along runs half as long. ALL species are now calibrated into that
proven envelope (seg 1.3–1.8, branch 0.26–0.32, spring 0.10–0.18). Species character
is carried by colours, foliage type, bloom/fruit, bark, autumn colour, and growth
pace — NOT by branch frequency.
