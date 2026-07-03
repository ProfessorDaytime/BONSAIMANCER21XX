# BONSAIMANCER — Development Plan

Last updated: 2026-07-02 · Added **Fable 5 Work Orders** section (time-boxed audit — see below). · Flower/fruit system (all 9 conifers cone), needle foliage for conifers, plus the H item menus and Quick-Start/Multi-Tree from earlier this phase. See "Completed This Phase" below for the full list and the Completed Items Log at the bottom for prior phases.

---

## Active Priority Queue

Pending work in recommended priority order. Detailed specs are under "Pending — Detailed Specs" below. (This ordering is a recommendation — reshuffle freely.)

| # | Item | Status / notes |
|---|------|----------------|
| ✅ | **UI — Themeable In-Game UI Rebuild** | ✅ **done 2026-06-15** (verified in-game — settings/pause, tooltips, calendar, stat panels, toolbars, species select all follow the theme). `UiThemePainter` (palette painter; captures originals from `resolvedStyle`, classifies → palette, contrast guard, functional denylist) + buttonClicker themes its UI from `UiTheme` on show + on `OnThemeChanged`: **stage 1** settings/pause + tooltips (explicit) + calendar; **stage 2** stat panels (Care Log / Tree Health / Auto Style / Root Health, via `ThemePanel`); **stage 3** whole-root walk (`ThemeRoot` → toolbar, date, corner controls) skipping self-themed overlays. `SetTabStyle`/species-card selection theme-driven. **`GameUiThemer` retired** (now an empty deprecated component — remove from the scene). Remaining = polish/edge cases as found. Original rebuild context (toolbar, Care Log, Tree Health, Auto Style, Root Health, Settings/pause, tooltips, calendar, date) **from scratch in C#** as code-built UI Toolkit panels that read `UiTheme.Current` and repaint on `OnThemeChanged` — the same pattern as the already-themeable menus (species select, shop, Journal, Customize, AP HUD). Replaces the hardcoded-colour `ButtonUI.uxml` markup so **all** UI follows the active theme. Retires the fragile `GameUiThemer` overlay once the rebuilt panels self-theme. Preserve all existing behaviour/wiring. See "Pending — Detailed Specs" → *Themeable In-Game UI Rebuild*. |
| ✅ | **H — Item Selection Menus** | ✅ done (pots / rocks / tables). GroundCover (moss & grass) folds into Decoration. See Completed This Phase. |
| ✅ | **20 — Multi-Tree / Quick-Start** | ✅ done — Quick-Start generator + tree shelf with save-before-switch. See Completed This Phase. |
| — | **Flower / Blossom & Fruit System** | ✅ core built 2026-06-14 — `FlowerManager` (flowers + fruit lifecycle), 14/17 species configured (all conifers cone). Showier blossom geometry ✅ (rounded cupped petals, 2026-06-15). Follow-ups: Azalea/Magnolia species, birch/willow catkins, ficus stylized bloom. See Completed This Phase. |
| — | **Needle Foliage (conifers)** | ✅ built 2026-06-14 — procedural needle-tuft meshes (`NeedleMesh`), one tuft per branch tip, 4 foliage types, evergreen persistence + deciduous-conifer drop. 10 species wired. See Completed This Phase. |
| ✅ | **19 — Gamification & Tutorial Progression** | ✅ core complete 2026-06-14 (`Docs/PROGRESSION_DESIGN.md`) — all 4 slices built: economy + zen core, cosmetic shop, Career/Sandbox gating + mode toggle, and the Journal/Encyclopedia (Achievements + Techniques/Phenomena/Species) with real milestone hooks (trim/wire/repot/bloom/fruit/survival). Remaining = ongoing balance tuning + optional richer tutorials. |
| ✅ | **21 — Decoration & Cosmetic Unlockables** | ✅ **done 2026-06-15** — all 4 cosmetic categories via the AP shop: Backgrounds, UI Themes (+ full game-UI theming), Music, and **Decoration placeables** (`DecorationManager` places a figurine/accent beside the tree; prefab or built-in procedural "Stone Lantern"; equipped-state persisted). `CustomizeManager` + ⚙ menu drive all four. See `Docs/PROGRESSION_DESIGN.md` §7. |
| 3 | **Backlog — Auto-Style Training Data Recorder** | ✅ **phase 1 built 2026-06-15** — `TrainingRecorder` writes one JSONL line per styling/care action (context + compact per-node tree snapshot) to `persistentDataPath/training/<session>.jsonl`. Hooks: Trim/Pinch/Wire/Unwire/Paste/Defoliate(All)/Repot; labelled `source=player|auto` via `ProgressionManager.AutomationActive`. Toggle in Settings → Debug (off by default). **Setup:** add a `TrainingRecorder` component. Phase 2 (consent, gzip, upload) remains. |
| 🔧 | **Pot Drainage Holes — root escape + drainage** | ✅ **mechanics built 2026-06-22** — `DrainageHole` cylinder marker; `PotSoil` collects holes (cylinder-derived **or** a procedural center+ring fallback) into box-local discs, hides the cylinders, scales `EffectiveDrainageRate` by total hole area (`HoleDrainageFactor`), and exposes `IsOverHole`. Pot-bound roots (`RootPressureFactor > 0.4`) over a hole grow out through the floor instead of being clamped (`TreeNode.escapedRoot`); floor deflection + the outer-boundary limit yield over holes. Debug GL overlay (`debugDrawHoles`) draws the discs. **Remaining:** author hole cylinders on real pots (waits on pot pipeline), tune drainage/escape balance, phase-2 clog feedback loop (escaped roots reduce drainage over time). See "Pending — Detailed Specs". |
| 3 | **Pots & Rocks content + scale standardization** | 🆕 raised 2026-06-15, **hard evidence 2026-07-02**: one session logged `rootArea` lossyScale at **3.12×2.77** (sane), **342×750**, and **494×1000** — a ~150× swing, meaning the rootArea transform sometimes sits under a ~150×-scaled parent depending on which pot/table/rock object is active. This is the direct cause of the intermittent root starbursts/spider-legs (F1b) and the kraken range. First fix: log + assert the rootArea's parent chain on new game; then settle the unit convention (1 unit = 1 cm), fix `PotDimensions`/import scales. Author real-size pots (starting 2-in, TinyPot01) + rocks after. |
| 4 | **K — Additional AutoStyler Styles** | **Lowest priority.** Blocked on user (descriptions + reference images). Chokkan / Shakan / cascade / windswept / broom / literati. |

---

## Fable 5 Work Orders (time-boxed — window closes ~2026-07-08)

Prioritized for a temporary Fable 5 window. Ordering favors **large, cross-cutting, whole-file reasoning** jobs that a smaller model handles poorly, with self-contained realism wins as reaches. Each item is spec'd so it can be handed straight to a fresh Fable 5 session. Audit basis: 2026-07-02.

| # | Item | Type | Why Fable 5 |
|---|------|------|-------------|
| F1 | ✅ **2026-07-02, VERIFIED** AutoStyler slot-fill — forced back-budding | Cross-system | Root cause: tree AT maxBranchNodes → stimulated buds silently discarded every year. Fix: styler-directed buds force-break (≤3/spring, energy-gated), bypassing cap+roll. **Measured: slot fill reached 10/14 (~71%) by tree year ~8 — up from the permanent 5/14 (36%) ceiling** |
| F1b | ✅ **triaged 2026-07-02** Pot-rim spider legs | Bug triage | Verdict: `PotDimensions` box ≫ TinyPot01 visual mesh — the scale-standardization debt (queue #3), NOT root logic. Confirm box-vs-pot bounds in-editor |
| F2 | ✅ **2026-07-02** Needle upgrades | Self-contained | Shipped: 4-variant tuft pool, per-tuft+per-leaf colour tint (property block, batching intact), year-round evergreen shed (capped trickle, 3× autumn). Remaining: pine candles, LOD/billboard |
| F3 | ✅ **jin 2026-07-02** / shari pending | Feature | Jin tool shipped end-to-end (see spec below). Shari = per-ring wedge vertex-colour follow-up |
| F4 | ✅ **2026-07-02** Bark flaking by species | Feature | 5 bark types with own geometry/palette/shed-rate (pine plates, blocks, birch two-tone curls, shreds, cypress sponge); flakes sit on the real surface radius |
| F5 | **TreeSkeleton partial-class split** | Refactor | 6,156-line file — recommend a dedicated session (zero-behaviour-change, needs a clean compile+run verify) |
| F6 | ✅ **2026-07-02** Wind / ambient foliage motion | Realism | `WindManager` (gust Perlin, wandering direction, real-time) + canopy sway/wobble via Leaf tracking; falling leaves drift downwind. Trunk mesh deliberately static (rebuild cost). **Setup: add a WindManager component** |
| F7 | ✅ **2026-07-02** Nebari — surface root flare | Realism | Mesh-level buttress ridges in `TreeMeshBuilder.AddRing`: ring verts in the flare zone push radially outward, world-azimuth lobe pattern (no swimming between rebuilds), strength ∝ trunk radius so it broadens with age; near-surface root rings share the flare so exposed roots merge in. Deliberately NOT node-based (avoids the F1b containment-bug class). Tunables under "Nebari (Surface Root Flare)" |
| F8 | ✅ **2026-07-02** AutoStyler periodic repot (3 iterations, see memory `project_auto_repot_root_prune`) | Bug/sim gap | Real `PotSoil.Repot` on a 2 yr young / 4 yr mature cadence + size-ups. Root-prune (`DiscardAndRegenerateRoots`, made public) **gated on `RootPressureFactor > 0.35`** — no prune = kraken roots; prune-every-refresh killed young Quick-Starts (trunk shading death). **Companion guard: `DiebackPass` can no longer drop node 0** (was deleting the whole tree into a zombie whose regen roots grew wild — the black-spider corpses / "state persisting between games"). **Retest Quick-Start 10 yr after recompile; discard zombie saves** |
| F10 | ✅ **2026-07-02** New-game manager reset (id contamination) | Bug | Node ids restart at 0 per tree, but `AutoStyler` (shapedNodeIds, pendings, slots) and `LeafManager` (nodeLeaves) kept the previous game's id-keyed state → second-in-session trees grew **stunted** (trunk never wired, canopy starved → low treeEnergy). `InitTree` now calls `ResetForNewTree()` on both. First-run-of-session trees were always fine — that's why the 20-yr QS looked good while 40/5-yr (run after it) didn't |
| F9 | **Fixed-timestep simulation — speed/frame consistency** | Architecture | The tree should be identical at any play speed. Today sim state advances per-frame with `inGameDays = dt×TIMESCALE/24`, so **thresholds crossed mid-frame resolve at frame boundaries with full-frame magnitudes** (an editor hitch = one 12-day step). Calendar/season events already accumulate correctly (verified gapless 2054–2098) and FOUR quantization bugs were patched case-by-case (auto-water cooldown, drought counter decay, watered-frame rule, and post-drain emergency water — the check-before-drain ordering let one TS=6000 frame carry moisture from "fine" to "bone dry" unseen, killing every 40-yr Quick-Start) — F9 replaces the whack-a-mole with the real fix: accumulate sim days and **step the simulation in fixed quanta** (e.g. 0.25 day) with a max-steps-per-frame cap (sim briefly lags wall-clock on hitches instead of jumping). Scope: `TreeSkeleton.Update` grow/care path, `PotSoil`, `AutoStyler` pendings; cosmetic real-time systems (wind, falling leaves/flakes) stay frame-based by design. True run-to-run determinism additionally needs a seeded per-tree RNG (optional phase 2). **Recommend doing F5 (partial split) first — the sim-step extraction falls out of it naturally** |

---

**F1. AutoStyler convergence — fill empty slots via forced back-budding** *(retest done 2026-07-02)*

- **Measured (2026-07-02, Ficus, Moyogi, Hands-Off):** **36% match at ~10 yr, still 36% at ~20 yr — fully plateaued.** The earlier "~17%" note is obsolete; ignore it.
- **Root cause (confirmed in the log, NOT slow convergence — a hard ceiling):** every spring the styler reports `RefreshSlots — 6 scaffold candidates` and `Slots 5/14 — Maintaining:5 Empty:9`. The tree only ever grows **6 primary scaffold branches**; 5 fill style slots and the **other 9 slots stay empty forever**. 5 ÷ 14 = 36%, mathematically pinned. Over 20 years the trunk produced **zero new scaffold branches**.
- **Why stimulation fails:** `StimulateEmptySlots` fires every February (`[AutoStyle] February — stimulated 5 empty slots (directional)`) and nudges the *azimuth* of the nearest trunk node — but if that node already spent its single lateral it never buds again, so candidate count stays at 6. The stimulation changes direction, never creates a branch.
- **The fix (re-scoped from "converge faster" → "make empty slots grow branches"):** implement genuine **epicormic / forced back-budding on old bare wood** — the real bonsai mechanic (cut back to bare trunk → dormant bud breaks). When an empty slot is stimulated, force a NEW lateral bud to break at that slot's target height + azimuth on the trunk, even on a node that already has a child. Gate on vigor/energy so it's earned, not free. Verify candidate count climbs past 6 and Empty count falls year over year.
- **Ceiling math:** filling even 4 of the 9 dead slots → ~64% with no other change; the wire-pacing / `MatchPercent` work is all downstream and can't help until the branches exist.
- **De-prioritized sub-symptom:** the pinch pass fires on large node batches per tick (`[AutoStyle] Pinch fired` on nodes 2955→2825) — check it's not thrashing, but it's secondary to the slot ceiling.
- **Scope:** `AutoStyler.StimulateEmptySlots`/`RefreshSlots`, `TreeSkeleton.SpawnChildren` (forced-bud path on lignified trunk nodes), `TreeNode` (bud-break-on-old-wood flag). **Files:** `Tree/AutoStyler.cs`, `Tree/TreeSkeleton.cs`, `Tree/TreeNode.cs`.

---

**F1b. Black spider-leg spikes around the pot rim** *(triaged 2026-07-02 — scale mismatch, not root logic)*

- **Symptom:** thin black root strands radiate horizontally from the soil/pot rim all the way around and droop over the pot/table edge (20-yr + 30-yr screenshots, Ficus in the new TinyPot).
- **Triage verdict (code-read, needs one in-editor confirm):** the roots are behaving correctly — the **logical root-containment box is far bigger than the new pot's visual mesh**. `PotSoil.PotDimensions` (old ~2.6-unit values) scales `rootAreaTransform`; the visible pot is the new **TinyPot01.fbx** (2-in, from the in-progress pot-authoring work). Roots legitimately grow to the box walls + the 1.3×/1.9× `RootDistRatio` boundary (`TreeSkeleton` grow pass), which sits well outside the small pot, at/above `rootVisibilityDepth = 0` so they render (`TreeMeshBuilder:554`). Top-face escape is also intentionally un-clamped ("looks realistic"), so surface-level strands persist rather than being trimmed at the visual wall.
- **Confirm:** at runtime compare `rootAreaTransform.lossyScale` vs the TinyPot mesh bounds (or enable the RootVis / drainage-hole debug overlays). Expect box ≫ pot.
- **Fix:** this is the **scale standardization item (queue #3)** — calibrate `PotDimensions` (and drainage-hole authoring) to the real-size pot meshes. Don't patch root logic for it.
- **Files:** `Tree/PotSoil.cs` (`PotDimensions`), pot import scale.

---

**F2. Needle Foliage — realism + variety upgrade** *(builds on the 2026-06-14 needle system)*

Current state: one shared tuft mesh + material per tree, one GameObject per tip (`NeedleMesh.cs`, `LeafManager.SpawnNeedleTuft`). Sound architecture, first-pass content. Upgrades, roughly in order:

- **Tuft variety** — every tip currently uses the ONE identical tuft mesh → visible repetition across hundreds of tips. Build a small pool (3–4) of tuft-mesh variants per `FoliageType` and index by `node.id`. Still one material, still batches.
- **Per-tip colour variety via instancing** — `LeafManager.EnsureNeedleAssets` already sets `needleMat.enableInstancing = true` but then writes one flat `_BaseColor` for the whole tree. Use a `MaterialPropertyBlock` per tuft for subtle hue/value jitter (and health/season tint) **without breaking batching** — this was already the noted follow-up.
- **Year-round needle drop** *(new — user request)* — a small percentage of needles should fall continuously all year, not just the deciduous-conifer autumn drop. Evergreen conifers shed old interior needles constantly (2–4 yr needle lifespan). Implement as a low-rate ambient shed: occasionally spawn a single falling-needle mesh from a random tuft (reuse the `Leaf` fall path / a lightweight falling quad), independent of season. Rate ∝ canopy size; higher in autumn. Keep it cheap (pooled, capped concurrent count).
- **Pine "candle" extension** — spring growth candles (elongating light-green new shoots that then open into needles). Noted follow-up, unbuilt.
- **LOD / distance form** — thousands of double-sided quads × every tip; with multi-tree this compounds. Collapse tufts to a cheaper billboard/impostor at distance or when zoomed out.
- **Broadleaf-evergreen flag** for ficus (keeps leaves year-round without being a needle species).
- **Scope:** `NeedleMesh.cs` (variant pool), `LeafManager.cs` (instancing block, ambient shed, LOD), `TreeSpecies.cs` (broadleaf-evergreen flag). **Files:** `Tree/NeedleMesh.cs`, `Tree/LeafManager.cs`, `Leaf.cs`, `Tree/TreeSpecies.cs`.

---

**F3. Jin / Shari — deadwood** *(JIN SHIPPED 2026-07-02 — shari remains)*

**Jin ✅ built:** `ToolType.Jin` (in the A/D tool cycle; silver tool colour) → `TreeInteraction.HandleJinHover` → `TreeSkeleton.JinNode`: walks the clicked branch's same-depth chain, strips laterals off with the bark (direct `RemoveSubtree`, deliberately NO wounds — stripped wood never calluses), marks the chain `isDead+isDeadwood+isJin`, defoliates. Jin terminals taper to a spike (`TreeNode.tipRadius` → 6%), render fresh-tan → weathering to lime-sulphur silver via `jinBleach` (+0.12/spring in `DiebackPass`). Persisted (`isJin`/`jinBleach` in saves); `first_jin` milestone + CareLog + TrainingRecorder hooks. **No UI button yet** — reachable via A/D tool cycle; add a toolbar button + Career gating later.
**Shari (follow-up):** bark-stripped channel down the trunk. Approach: store `shariAzimuthDeg`/`shariWidthDeg` per trunk node; `TreeMeshBuilder.AddRing` lerps ring-vertex colours to the bleached palette inside the world-azimuth wedge (vertex interpolation gives soft edges free). Per-ring colouring avoids the wound-system's smearing failure because the wedge is evaluated per vertex, not per segment.

*Original spec below for reference:*

Deadwood is one of the most iconic bonsai features and is currently only a stubbed hook (the wound system left "a hook for jin/deadwood"; `TreeSkeleton.cs:3080` has a placeholder teal cylinder for the air-layer site).

- **Jin** — a stripped, bleached deadwood branch tip. Player tool (or AutoStyler advanced step): convert a trimmed/dead branch end into deadwood instead of healing it — strip bark, taper to a point, recolour to weathered silver-grey, add lengthwise fissures.
- **Shari** — deadwood stripped down the trunk/branch surface (a bark-stripped channel exposing bleached heartwood) that the live vein grows around over seasons.
- **Interaction with existing systems:** deadwood nodes don't heal (skip `WoundHealProgress`), don't grow foliage, and read as dead in health/narrative. Lime-sulphur "bleaching" as an optional care action.
- **Geometry:** procedural, code-gen (consistent with the whole project — no art). Reuse the wound-disc / bark-vertex approach.
- **Scope:** new deadwood state on `TreeNode`, `TreeMeshBuilder` (stripped/bleached geometry + shader path in `BarkVertexColor.shader`), `TreeSkeleton` (jin/shari conversion, heal opt-out), a tool entry in `buttonClicker`. **Files:** `Tree/TreeNode.cs`, `Tree/TreeMeshBuilder.cs`, `Tree/TreeSkeleton.cs`, `buttonClicker.cs`, `BarkVertexColor.shader`, `SaveManager.cs`.

---

**F4. Procedural bark flaking by species** *(realism — replaces placeholder)*

`BarkFlakerManager.cs` is explicitly a placeholder (flat curved quad "until artist-modelled flakes are ready"). You have 18 botanical bark types mapped across 17 species (see memory `reference_bark_types`) — drive flaking from that instead of a single quad.

- **Per-bark-type flaking:** plate bark (pine — large jigsaw plates), shaggy/strip (juniper, cedar — long vertical peels), lenticel/paper (birch — horizontal papery curls), smooth (beech — minimal), fissured (elm, oak). Flake shape/size/shed behaviour keyed to bark type.
- **Shed over time** — flakes loosen and drop as the trunk thickens (ties into existing trunk-growth), revealing fresh bark colour beneath. Cheap falling flake reuse from the soil-clod / leaf-fall pattern.
- **Scope:** `BarkFlakerManager.cs` (per-type geometry + shed), read bark type from `TreeSpecies`. **Files:** `Tree/BarkFlakerManager.cs`, `Tree/TreeSpecies.cs`.

---

**F5. TreeSkeleton partial-class split** *(refactor — pure whole-file reasoning)*

`TreeSkeleton.cs` is **6,156 lines** and owns growth, roots, wounds, mesh coordination, water, wire, pinch, sag, and health — past the size that's safely hand-editable, and it's the file every other work order touches. Split into `partial class TreeSkeleton` files by subsystem (e.g. `TreeSkeleton.Growth.cs`, `.Roots.cs`, `.Wounds.cs`, `.Health.cs`, `.Care.cs`) with **zero behaviour change** — mechanical, but requires understanding the whole file at once, which is exactly Fable 5's strength. Do this **before** F1/F3 if possible so later edits land in smaller files. (Logging cleanup is intentionally NOT part of this — the Debug.Log spam goes away before player testing anyway.)

- **Scope:** `TreeSkeleton.cs` → multiple `partial` files; no signature or logic changes; verify compile + a clean 30-yr run matches pre-split behaviour.

---

**F6. Wind / ambient foliage motion** *(realism)*

No ambient motion exists today; every screenshot reads static. You already have leaf weight + elastic spring-back (`elasticSagDeg`, `ApplyDailySag`), so the sway can ride that system rather than a new one.

- Subtle per-branch wind sway (low-frequency noise, amplitude ∝ 1/branch-strength so fine tips move most), plus a gentle leaf/needle flutter. Gust variation. Must stay cheap at high timescale and during cinematic/beauty-shot holds.
- **Scope:** `TreeSkeleton` sag/elastic hook or a light dedicated `WindManager`; foliage flutter in `Leaf` / needle tuft. **Files:** `Tree/TreeSkeleton.cs` (or new `WindManager.cs`), `Leaf.cs`, `Tree/LeafManager.cs`.

---

**F7. Nebari — surface root flare** *(✅ SHIPPED 2026-07-02)*

**Built as mesh-level buttress ridges, not node-based surface roots** (node-based would interact with containment/pot-bound systems — the same bug class as the F1b spider legs). In `TreeMeshBuilder.AddRing`: every ring vertex inside the flare zone (height ≤ `nebariZoneRadii` × trunk radius above the base, plus a little below for soil-line roots) is pushed radially outward from the trunk axis — horizontal only, buttresses spread rather than climb — modulated by `pow(|sin(azimuth·lobes/2 + φ)|, sharpness)`. Keyed to **world azimuth** so ridges never swim between rebuilds; displacement ∝ **trunk base radius**, so seedlings stay smooth (`nebariMinTrunkRadius` gate) and the flare broadens as the trunk thickens. Near-surface root rings inside the zone receive the same push, so exposed roots visually merge into the buttress. Applied before the Ishitsuki rock-grip snap so rock gripping still wins. The underground cap welds to the flared base ring (no seam; flare tapers below soil). Tunables: `nebariStrength` (0.55), `nebariLobes` (7), `nebariSharpness` (3), `nebariZoneRadii` (2.8), `nebariMinTrunkRadius` (0.05). **Files:** `Tree/TreeMeshBuilder.cs` only.

---

**F8. AutoStyler periodic repot / soil refresh** *(✅ SHIPPED 2026-07-02, incl. same-day root-prune fix)*

**Status:** built and verified live (repots logged 2041–2059 incl. the M→L size-up; no errors). **First-run lesson:** soil-only repots kept the tree healthy while never pruning roots — the pot-bound inner-fill pumped the root system to its hard cap (900 = 1.5×`maxTotalRootNodes`) with per-season radius ticks thickening every strand ("kraken pot" screenshots, Oct 2051–Aug 2059). Fixed same day: `AdvancePotPhase` now calls `RootRakeManager.DiscardAndRegenerateRoots()` (made public) right after `PotSoil.Repot`, mirroring the player flow exactly. (The original reason for skipping root regen — allNodes bloat — was wrong: `RemoveSubtree` genuinely deletes nodes.) Old kraken saves self-heal at their next auto-repot. Tunables: `autoSoilRefresh`, `repotIntervalYoungYears` (2), `repotIntervalMatureYears` (4).

*Original problem statement below for reference:*

- **Problem:** under Hands-Off care the auto-styler never actually repots. `AutoStyler.AdvancePotPhase()` (the only pot action) is a one-way **pot-size upsize** on an age schedule (XS→S at yr 6, →M at yr 13, →L at yr 26) and early-outs unless the target size is larger than current — so it fires **at most 3 times in the tree's life and never again after year 26**. It calls `PotSoil.ApplyPotSize()` (geometry only), **not** `PotSoil.Repot()`, so it never resets `soilDegradation`, `saturationLevel`, or `seasonsSinceRepot`, and never applies fresh soil or root-prune. (Confirmed by the UI's "Since Repot" counter climbing to 16+ seasons and never resetting.)
- **Consequence:** `PotSoil.SeasonTick` keeps degrading soil toward `soilDegradation → 1.0`, drifting drainage/retention off the species' preferred values and stacking soil-mismatch nutrient penalties. A mature tree is slowly starved in dead soil it can never escape — the probable cause of the ~40% avg-health plateau seen alongside F1 (moisture fine, but "the tree is struggling"). Real bonsai are root-pruned + given fresh soil every **2–5 years for their whole life**.
- **Fix:** add a genuine periodic repot to the auto-care cycle — when `seasonsSinceRepot` exceeds a species/style interval (≈ every 2–4 yr, lengthening as the tree matures), call `PotSoil.Repot()` in early spring (month 2–4 for the good-timing bonus) with a species-appropriate `SoilPreset`, taking the normal repot stress and triggering a root-prune. Keep the existing size-advancement (pass `sizeChanged` only when a size step is also due). Log it to the CareLog. Verify `seasonsSinceRepot` cycles back to 0 and mature-tree avg health recovers over a long run.
- **Scope:** `AutoStyler.AdvancePotPhase` (or a new `PeriodicRepot` alongside it), `PotSoil.Repot`. **Files:** `Tree/AutoStyler.cs`, `Tree/PotSoil.cs`.

---

## Completed This Phase (Jun 2026)

- **A. AutoStyler Pacing & Convergence** ✅ — directional back-bud (`preferredLateralAzimuth`), `autoStyleWireSpeedMult` 1.5×, partial-credit `MatchPercent`, `fastConverge`; + scaffold-base slot-matching fix.
- **B. AutoStyler Extended Care** ✅ — auto-paste, late-June defoliation (`defoliateThreshold`, biennial), spring pot advancement (`potPhaseStartYears`).
- **C. 45° Branch Cut Angle** ✅ — `useBevelCut`/`cutAngleDeg`, angled callus, `bevelCutDrainMult` heal bonus (was already implemented; verified).
- **D. AutoStyler Care Log + Narrative** ✅ — `CareLog.cs` (persisted), templated action reasons, season-end health summary, collapsible CareLogPanel.
- **E. Leaf Growth & Bud-Break** ✅ — leaves emerge at 15% and mature over `leafGrowDays`, staged cluster unfurl, bud swells then opens.
- **F. Leaf Weight & Elastic Spring-Back** ✅ — leaf load in `ComputeLeafLoad`; `elasticSagDeg` eases branches down in summer and springs them back at leaf-fall.
- **G. Repot Soil Compaction & Rake Rework** ✅ — rake-first flow, solid voxel soil ball with code-gen falling clods, unsupported-island break-off, compaction debuff + clean-sweep scoring.
- **J. Root-on-Rock Realism** ✅ — organic curvature clamp (`rootMaxBendPerSegmentDeg`) + rock-cable corner-rounding (`SmoothRockCableStrand`) with concavity-aware surface offset; up-jut + clip-through fixed.
- **Ishitsuki care** ✅ — AutoStyler auto-removes the rock-binding wire when set; Trim tool cuts exposed rock/air-layer roots like branches; trimmed cables stay cut.
- **Weeds-on-rock fix** ✅ — weeds excluded from the rock footprint via the real `rockCollider` (not just the "Rock" tag).
- **UI polish** ✅ — Care Log (top-left), Tree Health (top-right), Auto Style + Root Health all collapsible; cinematic live zoom/pan that resets per entry.
- **Autonomous Run Loop** ✅ — `AutoRunManager.cs` hands-off record loop (Hands-Off mode, cinematic, species rotation, beauty-shot hold, loop count).
- **I. Species Parameter Realism Pass** ✅ done 2026-06-13 — added `leafGrowDays` + `leafBudBreakColor` to all 17 species (species-specific flush speed/color), fixed Ficus's subtropical growth window (was stopping in June), added `canDefoliate` field gating the AutoStyler's auto-defoliation so conifers are never defoliated, and corrected signature colors (white-pine blue needles, Scots-pine orange bark). Justification table in `Docs/SPECIES_REALISM.md`.
- **L. Bark Wound / Scar System** ✅ done 2026-06-13 — pruning cuts render as **dedicated callus-disc geometry** (`AddWoundDiscAt`), NOT a vertex channel — the first TEXCOORD1 attempt smeared across the trunk's welded segments and overlapped adjacent wounds (console proved 4 wounds → 1 visible). Classified by whether the branch continues: a **side cut** leaves a short bark **stub protruding** along the cut direction with the cut face on its end (`AddWoundStub`, bigger branch = bigger stub); an **end cut** caps the branch end. **Healing over seasons** (`TreeSkeleton.WoundHealProgress`, decoupled from the health drain so balance is untouched): the callus rolls in (heartwood→callus recolor + the face domes into a knob) and side-stubs are engulfed by the thickening trunk — small cuts vanish, big ones leave a lasting knob. Heal speed ∝ wound size, node vigor, cut paste, species. **Winding gotcha:** the disc winding had to be outward (`cross(v1-v0,v2-v0)` along +normal) or every disc lit from behind (dark/edge-on). Tunables: TreeSkeleton "Wound Occlusion" + TreeMeshBuilder "Pruning Scars". Hook left for jin/deadwood. `woundAge` persisted. Files: `TreeMeshBuilder`, `BarkVertexColor.shader` (wound block), `TreeSkeleton`, `TreeNode`, `SaveManager`. See memory `project_wound_disc_geometry`.
- **20. Multi-Tree / Quick-Start** ✅ done 2026-06-14 — **Quick-Start:** `QuickStartManager` fast-grows a developed tree (chosen species + style + target age 5/10/20/40 yr) by reusing the AutoRun path (reset → InitTree via the Water state → Hands-Off auto-care at Fast speed + cinematic) while the AutoStyler shapes it, then restores Med speed, drops cinematic, and saves a fresh slot; forces a Seedling origin so it inits cleanly after a prior rock/loaded tree. Player UI is `QuickStartPanel` (style cards + 5/10/20/40-yr age presets) reached via a ⚡ Quick-Start button injected next to the species-select Confirm. **Multi-Tree:** the existing `LoadMenu` shelf already lists every saved tree (thumbnail, origin badge, species, health bar) with LOAD/DELETE + New Game, reachable from the pause menu — added **save-current-before-switch** in `OnLoadMenuCardLoad` so in-session switching never loses progress (an unsaved current tree is preserved as its own slot). "New tree → Quick-Start" works via New Game → species select → ⚡ Quick-Start. **Setup:** add a `QuickStartManager` component to the scene and populate its Styles list. Files: `QuickStartManager.cs`, `QuickStartPanel.cs`, `buttonClicker.cs`.
- **H. Item Selection Menus** ✅ done 2026-06-14 — one reusable framework: `ItemDefinition` (ScriptableObject: name/desc/category/prefab/thumbnail/swatch, maps to `PotSize`/`RockSize`, unlock hook) + `ItemCatalog` registry + `ItemCatalogPanel` (shared code-built modal card grid). Entry points all reuse the panel: **Pots** — "Choose pot…" in the repot panel sets `pendingPotSize` (mesh = hook); **Rocks** — "Choose rock…" in the rock-size panel sets `RockPlacer.rockSize` and swaps the rock mesh via `RockPlacer.SetRockModel` (no-op until art exists); **Tables** — "Choose table…" on the species/new-game screen swaps the platform mesh via `ApplyTable` (assign **Table Platform** on buttonClicker, or it finds "PlanterTable"/"Platform"). Runs with zero content (empty category → existing default stands in). `Bonsai → Create Default Item Catalog` generates 6 pots + 4 rocks; assign the catalog to buttonClicker's **Item Catalog** field. **GroundCover (moss & grass)** category exists but its entry point folds into Decoration (#4). Files: `Items/ItemDefinition.cs`, `Items/ItemCatalog.cs`, `Items/ItemCatalogPanel.cs`, `Editor/ItemCatalogCreator.cs`, `RockPlacer.cs`, `buttonClicker.cs`.
- **Flower / Blossom & Fruit System (core)** ✅ done 2026-06-14 — `FlowerManager` (parallel to `LeafManager`, attach to bonsai) drives the full reproductive cycle from repro buds set in autumn (`TreeNode.hasFlowerBud`, gated on `floweringStartAge`/`flowerBudChance` for any species with a `bloomType` or `fruitType`). **Flowers** open at `bloomMonth` → hold `bloomDurationDays` → petal drop: `Blossom` (5-petal star), `Raceme` (hanging tapered octa-stack), `Catkin` (dangling spike). **Fruit** sets at `fruitSetMonth` → recolours to ripe at `fruitRipeMonth` → drops: `Berry`/`Fig`/`Samara`/`Cone`/`Pome` (octa-based). Fruit-only species skip the bloom. All geometry + materials are code-generated (no art); driven by in-game days so it keeps pace at any timescale. Configured 6 species: Cherry (pink blossom before leaves + red berries), Wisteria (purple racemes), Japanese Maple (red catkins + tan samaras), Ficus (green→purple figs), Juniper (green→blue berries), Japanese Black Pine (green→brown cones). `hasFlowerBud` persisted in saves. Also tuned Cherry's growth (was a dense twiggy ball → open graceful form). **Follow-ups:** showier blossom geometry, Azalea/Magnolia species, ficus stylized bloom, birch/willow catkins. Files: `FlowerManager.cs`, `TreeNode`, `TreeSpecies` (BloomType/FruitType + params), `TreeSkeleton.SetBuds`, `SaveManager`, 6 species `.asset`.
- **Cones on all conifers** ✅ done 2026-06-14 — extended the `FruitType.Cone` block from Japanese Black Pine to the other 8 conifers (3 pines, 2 spruces, 2 cedars, swamp cypress, dawn redwood) with slight per-group colour/ripen-month variety. Flower/fruit coverage now 14/17 species; only Elm, Silver Birch, Weeping Willow remain bare (birch/willow want catkins — a follow-up). Pure `.asset` data edits.
- **Cosmetic Unlockables — UI Themes** ✅ done 2026-06-15 — `UiTheme` central palette class (11 named colours) with 5 code-defined themes (Forest default + Charcoal/Night/Sakura/Parchment, incl. a light theme); `UiTheme.Current` + `Apply(id)` + `OnThemeChanged`. The code-built overlays (`ItemCatalogPanel`, `JournalPanel`, `CustomizeManager`, `ProgressionHUD`) had their hardcoded `Color` constants converted to properties reading `UiTheme.Current.*`, so a theme swap reskins them on next open; the always-on AP HUD subscribes to `OnThemeChanged` and rebuilds live. Theme items carry a `themeId` (`ItemDefinition`); `CustomizeManager` applies + equips + restores on load; `ItemCatalogCreator` ships the 5 themes (Forest free). Files: `Progression/UiTheme.cs`, `ItemCatalogPanel.cs`, `JournalPanel.cs`, `CustomizeManager.cs`, `ProgressionHUD.cs`, `Items/ItemDefinition.cs`, `Editor/ItemCatalogCreator.cs`.
- **Cosmetic Unlockables — Music** ✅ done 2026-06-15 — last media category. `ItemDefinition.audioClip` (Music category) + `MusicManager` (own AudioSource, loops, crossfades on equip via unscaled-time fade; null clip = silence). `CustomizeManager` applies/equips/restores Music like the others. `ItemCatalogCreator` ships a free **Silence** + 4 named slots (Zen Garden/Koto/Rainfall/Forest Birds) — **tracks are author-supplied**: drop an `AudioClip` onto each generated `Music_*` asset's Audio Clip field. **Setup:** add a `MusicManager` component to the scene. Files: `Progression/MusicManager.cs`, `Items/ItemDefinition.cs`, `Progression/CustomizeManager.cs`, `Editor/ItemCatalogCreator.cs`.
- **Cosmetic Unlockables — Game-UI theming pass** ✅ done 2026-06-15 — extends themes to the **main game UI** (buttonClicker's UXML, which uses inline styles so only runtime code can override them). `GameUiThemer` captures every element's original inline colours **once** at Start, then **classifies** each into a palette slot (bg: near-black→panelBg, dark→cardBg, bright+saturated→accent; border→edge; text: gold/blue→accent, bright→textMain, else→textSub) and re-applies on `OnThemeChanged` — so the whole green-on-dark UI follows the palette, not just a fixed list. Functional status-bar fills (moisture/compaction/saturation/nutrient) are **denylisted** to keep their semantic colours; the code-built overlays (created after Start) are excluded and self-theme. A **contrast resolver** swaps any themed text that doesn't contrast with its themed background to whichever of `textMain`/`ink` reads best (fixes light text on light-accent buttons like Sakura pink / Forest gold, and keeps the light Parchment theme correct in reverse). **Setup:** add a `GameUiThemer` component to the scene. Caveats: colourful action buttons (Save green / Load blue / Resume gold) all collapse to `accent` (text stays readable, labels distinguish them); USS-class-styled elements (rare here) aren't reached. File: `Progression/GameUiThemer.cs`.
- **Game-UI theming — dynamic panels + pause double-click** ✅ 2026-06-15 — `GameUiThemer` now **re-scans on every game-state change + every 0.5s** (capturing newly-built elements) so the lazily-created in-game panels (Care Log, Tree Health, Auto Style, Root Health, Settings/pause) get themed, not just the menu that existed at Start. Progression overlays are named with a **`pm-` prefix** and skipped (they self-theme); destroyed elements are pruned to avoid leaks. Also fixed the **pause menu needing two clicks**: `TogglePauseMenu` read `style.display` (inline, unset when hidden via USS) → now reads `resolvedStyle.display` (actual visibility) so the first click opens it. Files: `Progression/GameUiThemer.cs`, `ProgressionHUD.cs`, `Items/ItemCatalogPanel.cs`, `Progression/JournalPanel.cs`, `Progression/CustomizeManager.cs`, `buttonClicker.cs`.
- **UI RenderTexture offset fix** ✅ 2026-06-15 — fixed the recurring "UI treats 1/3 down as top of screen": the `UIToolkitRT` is shown by a uGUI **RawImage** whose RectTransform was fixed-size (1920×1080) centre-anchored, so it only aligned at exactly 1920×1080. Fix = set the RawImage anchors to **stretch/stretch** (fills any screen). Inspector change, not code. See memory `project_ui_rendertexture_offset`.
- **Cosmetic Unlockables — Backgrounds + shared scaffolding** ✅ done 2026-06-15 — extends the AP shop to non-tree cosmetics. `ItemCategory` += `Decoration/Background/Music/UiTheme`; `ProgressionProfile` += `equippedBackground/Music/Theme` (restored on load). `CustomizeManager` (assign the shared `ItemCatalog`) opens a **Customize menu** from the ⚙ on the AP chip and reuses `ItemCatalogPanel` per category (price/owned/buy), then applies + equips. `BackgroundManager.Apply` swaps the camera backdrop (skybox material or solid `swatchColor`) + `ambientColor` + optional scenery prefab, capturing scene defaults so the free default restores them. `ItemDefinition` += `skyboxMaterial`/`ambientColor`; `ItemCatalogCreator` ships 5 colour backdrops (Studio Teal free, Night/Sunset/Sakura/Mist priced). Music/UI-Theme/Decoration browse+buy+equip already work; their apply handlers are no-op stubs until built. **Setup:** add `CustomizeManager` (+ assign catalog) and `BackgroundManager` components; re-run `Bonsai → Create Default Item Catalog` for the backdrops. Files: `Progression/CustomizeManager.cs`, `Progression/BackgroundManager.cs`, `Items/ItemDefinition.cs`, `Items/ItemDefinition` (enum), `ProgressionProfile.cs`, `ProgressionHUD.cs`, `Editor/ItemCatalogCreator.cs`. See `Docs/PROGRESSION_DESIGN.md` §7.
- **Progression Slice 4 — Journal / Encyclopedia + Milestone Hooks** ✅ done 2026-06-14 — the zen reward layer. `Journal` (static content registry) + `JournalPanel` (UI Toolkit overlay, opened by clicking the AP chip) show **Achievements** (earned milestones with their flavour text) and an **Encyclopedia** in three sections — **Techniques** (pruning/wiring/pinching/repotting/advanced), **Phenomena** (flowering/fruiting), **Species** (all 17) — each entry unlocking via a profile predicate (milestone reached / tool unlocked / species grown) with a `(unlocked/total)` count and greyed locked stubs. Real milestone hooks now fire the first-time events: one-liners in `TreeSkeleton.TrimNode` (first_trim), `buttonClicker.OnWireSetGold` (first_wire), `PotSoil.Repot` (first_repot), `FlowerManager.Bloom`/`SetFruit` (first_bloom/first_fruit), plus age-based survival (`survive_5/10/25`) evaluated in `ProgressionManager` each state change (both modes). Files: `Progression/Journal.cs`, `Progression/JournalPanel.cs`, `ProgressionHUD.cs`, `ProgressionManager.cs`, `TreeSkeleton.cs`, `PotSoil.cs`, `FlowerManager.cs`, `buttonClicker.cs`.
- **Progression Slice 3 — Career Gating + Mode Select** ✅ done 2026-06-14 — a **Mode: Career/Sandbox** toggle injected beside the species-select Confirm (`SetupModeToggle`, mirrors the ⚡ Quick-Start injection) calls `ProgressionManager.SetMode`. **Sandbox** (default) unlocks every tool — current gameplay is unchanged unless you switch to Career. **Career** gates the primary tool buttons (trim/paste, wire/remove, pinch, defoliate, fertilize, root-prune, air-layer/rock/graft/promote) via `ApplyToolGates` — locked tools are **disabled + dimmed** (SetEnabled+opacity, *not* hidden, so it never fights the existing contextual show/hide), re-asserted on every state change. `ProgressionManager.EvaluateUnlocks` unlocks tiers on robust, persisted, self-readable triggers (branch-structure max-depth, tree age from `root.birthYear`, pot-bound `RootPressureFactor`) — no per-system event hooks needed — firing `OnToolUnlocked`; `ProgressionHUD` toasts "✦ Unlocked: …". Files: `ProgressionManager.cs`, `ProgressionHUD.cs`, `buttonClicker.cs`.
- **Progression Slice 2 — Cosmetic Shop** ✅ done 2026-06-14 — the existing Item Catalog *is* the storefront (no separate shop screen). `ItemDefinition` gained `cost` (AP price) + a stable `Id`; `ItemCatalogPanel` now shows a **balance header** and per-card state: **owned/free** (✓, selectable), **purchasable** (✦ price Buy button, enabled only if affordable → `TrySpend` + `GrantItem`, then the list refreshes so every card re-checks affordability), or milestone-**LOCKED**. Degrades gracefully with no `ProgressionManager` in the scene (ignores the economy, behaves as before). `ItemCatalogCreator` default items got a price curve (one free starter pot + rock, the rest 80–300 AP). Files: `Items/ItemDefinition.cs`, `Items/ItemCatalogPanel.cs`, `Editor/ItemCatalogCreator.cs`. Re-run `Bonsai → Create Default Item Catalog` to get priced defaults (or set `cost` on existing ItemDefinition assets).
- **Progression Slice 1 — Economy + Zen Core** ✅ done 2026-06-14 — first slice of item #19. **Aesthetic Points** soft currency, earned and spent on cosmetics only (never power). Global `ProgressionProfile` (`persistentDataPath/profile.json`, separate from tree save slots) holds points + unlocked tools + owned items + milestones + journal entries. `ProgressionManager` (scene singleton) awards points each autumn (`LeafFall`) scaled by average tree health (seasonal stewardship), plus milestone lump sums (`first_trim`/`first_bloom`/`survive_5/10/25`/`species_*` etc. — registry + dynamic), with `Award`/`TrySpend`/`CanAfford` + `IsToolUnlocked`/`Owns` stubs for later slices. `ProgressionHUD` (UI Toolkit, self-attaches to the `UIDocument` root) shows an AP balance chip + award/milestone toasts. Default mode **Sandbox** so nothing is gated yet. **Setup:** add `ProgressionManager` + `ProgressionHUD` components to the scene. Files: `Progression/ProgressionProfile.cs`, `Progression/ProgressionManager.cs`, `Progression/ProgressionHUD.cs`. See `Docs/PROGRESSION_DESIGN.md` + memory `project_progression_design`.
- **Needle Foliage (conifers)** ✅ done 2026-06-14 — conifers no longer use the broadleaf prefab. `NeedleMesh` builds one procedural **tuft** mesh per tree (a whole bundle of needles); `LeafManager` drops one tuft GameObject per branch tip (one per tip, not per needle — all tufts share the tree's one mesh + material) and reuses the `Leaf` component for scale-in/tracking/colour/fall. Four `FoliageType`s — PineFascicle (pines), SpruceRadial (spruces + cedars), FeatheryFrond (dawn redwood, swamp cypress), Scale (juniper). `species.evergreen` keeps needles year-round (autumn fall skipped, spring cleanup retires now-interior tufts so foliage tracks the tips); deciduous conifers drop like leaves. New fields `foliageType`/`evergreen`/`needlesPerTuft`/`needleLength` on `TreeSpecies`; 10 species wired. Needle size/density are a first pass — fine-tune later. Files: `NeedleMesh.cs`, `LeafManager.cs`, `TreeSpecies.cs`, 10 species `.asset`. See SYSTEMS.md §45 + memory `project_needle_foliage`.

---

## Completed This Phase — Implementation Detail (A–G)

<details>
<summary>Full implementation notes for the completed A–G items (expand for reference).</summary>

**A. AutoStyler Pacing & Convergence** ✅ done 2026-06-11

All four bullets implemented: directional back-bud (`preferredLateralAzimuth` on `TreeNode`, steered ±30° in `LateralDirection`, one slot claim per trunk node per February pass), `autoStyleWireSpeedMult` 1.5× via per-node `wireSetSpeedMult` (player wires unaffected; persisted in saves), partial-credit `AutoStyler.MatchPercent` (Growing 50% / Training 75% / Est+M 100%), and `fastConverge` toggle (wire 4×, preview 3 d, `depthsPerYearMult` 2× on `TreeSkeleton`). **Bonus root-cause fix:** slot matching, excess-trim counting, and GL trim X markers now only consider scaffold BASE segments (`depth==1 && parent.depth==0`) — previously every subdivision segment of a branch's first chord competed for slots, so one physical branch could occupy several slots and trim markers appeared mid-branch.

At default settings the tree reaches only ~17% style match after 30 in-game years. Needs to converge to a recognisable form within 10–15 years so testers and players can actually see the result.

> **Max safe speed reference:** At TIMESCALE=200 (Fast), 1 in-game day = 0.12 real seconds; 1 year ≈ 44 real seconds; 30 years ≈ 22 real minutes. The Calendar Speed Config tab goes up to 500 (≈18 real seconds/year; 30 years in ~9 min). Values above ~600 risk skipping `OnMonthChanged` events if multiple months tick in a single frame — the calendar accumulates fractional hours so month rollover is safe, but verify at your target rate. For testing: TIMESCALE=400–500 is a safe ceiling. For a true stress test at 1000+ use the AutoRunManager with `resetTreeOnLoop=false`.

- **Root cause audit:** Most empty slots come from azimuth mismatch — branches grow randomly and `backBudStimulated` is non-directional. A tree may never grow a branch at the right compass bearing even after decades.
- **Directional back-bud stimulation** — when stimulating empty slots in February, also nudge the nearest trunk node's lateral spawn direction toward the slot's azimuth. Requires a `preferredLateralAzimuth` field on `TreeNode` read by `SpawnChildren` to bias the first lateral's direction ±30° toward target.
- **Faster wire convergence** — add `autoStyleWireSpeedMult` (default 1.5×) on `AutoStyler` that scales the effective `wireDaysToSet` for auto-placed wires only, so auto-wires set in ~1.3 seasons rather than 2.
- **Match % counting** — count all assigned slots (not just Established/Maintaining) as partial credit in the % display. Occupied slots at any state = progress toward the style.
- **Debug / test mode** — add a `fastConverge` toggle that sets `wireSpeedMult=4`, `actionPreviewDays=3`, `depthsPerYear×2` so a tester can see the end result in ~5 years at fast speed.

---

**B. AutoStyler Extended Care (Paste / Defoliate / Repot)** ✅ done 2026-06-11

All three implemented: auto-paste on every auto-trim cut site (`autoPaste` toggle), late-June full defoliation via `LeafManager.DefoliateAll()` when leafy tips ≥ `defoliateThreshold` (default 80, scheduled June +20 d), and spring pot advancement XS→S→M→L at `StyleDefinition.potPhaseStartYears` (default {6, 13, 26}; only ever advances, never shrinks a player-chosen pot; calls `PotSoil.ApplyPotSize` directly).

AutoStyler should manage the full care cycle, not just wiring and trimming.

- **Wound paste** — after every auto-trim that creates a wound, immediately call `ApplyPaste(node)` on the parent (same API as the player's paste tool). No UI interaction needed — the system just always pastes its own cuts.
- **Defoliation** — in late June (after ramification), if the tree's canopy is dense enough (terminal nodes > defoliateThreshold, e.g. 80 tips), trigger a full defoliation pass. Defoliating mid-summer forces finer back-budding and reduces shading on interior branches. Call existing `skeleton.DefoliateNode` per terminal.
- **Auto-repot / pot sizing** — the tree starts in the smallest pot (XS) and AutoStyler advances the pot size on a schedule based on style phase:
  - **Phase 1 (years 1–5):** XS pot — tight restriction encourages trunk thickening and compact root mass.
  - **Phase 2 (years 6–12):** S pot — give roots room once trunk base is established.
  - **Phase 3 (years 13–25):** M pot — mid-development, branches thickening.
  - **Phase 4 (25+):** L pot — maintaining mature form.
  - Pot advancement fires at spring when `GameManager.year - startYear >= phaseThreshold`. Calls `PotSoil.ApplyPotSize(rootAreaTransform)` directly — no repot mini-game (auto-care bypasses that). Log the pot change.
  - `StyleDefinition` can optionally override the phase thresholds.

---

**C. 45-Degree Branch Cut Angle** ✅ done (verified 2026-06-11 — was already fully implemented)

`useBevelCut` (default on) + `cutAngleDeg` (45°) tilt `woundFaceNormal` in `TrimNode`; `AddWoundCap` renders the angled callus face; the health benefit ships as `bevelCutDrainMult = 0.7` applied in the seasonal wound-drain pass when the wound face is angled >10° off the branch axis.

In real bonsai, cuts are made at ~45° relative to the branch axis so water runs off the wound face rather than pooling. A flush flat cut retains moisture and invites fungal infection.

- **Wound face normal** — `TreeNode.woundFaceNormal` is already stored (currently set to `removedBranch.growDirection`). Change `TrimNode` to set `woundFaceNormal = Quaternion.AngleAxis(45f, perpendicularAxis) * removedBranch.growDirection` where `perpendicularAxis` is the cross product of `growDirection` and the parent's `growDirection`. This tilts the cut face 45° toward the direction growth was coming from.
- **Visual** — `AddWoundCap` in `TreeMeshBuilder` uses `woundFaceNormal` to orient the cap ring. With the angled normal the callus cap will render at 45° on the branch, which is visually correct and subtly different from a flat cross-cut.
- **Health benefit** — optionally reduce `woundDrainRate × 0.7` when `woundFaceAngle > 30°` (angled cuts heal faster).
- **Scope:** `TreeSkeleton.TrimNode`, `TreeMeshBuilder.AddWoundCap`, `TreeNode` (no new fields needed — `woundFaceNormal` already exists).

---

**D. AutoStyler Narrative / Care Log** ✅ done 2026-06-11

`CareLog.cs` — static rolling log (200-entry cap), persisted via `SaveData.careLog`, restored on load, cleared on restart, "Planted a new {species} seed" on InitTree. AutoStyler logs every action with a templated reason: trim (azimuth + why), paste, pinch (silhouette vs ramification), wire (bearing/lean, trunk vs slot wording, logged when the bend completes), unwire (gold + delay), defoliate (tip count), pot phase advance, February back-bud stimulation, and a spring review line (slots filled + match %). `TreeSkeleton.LogSeasonNarrative()` writes the 2–3 sentence health summary (mood from avg health, one positive, worst negatives: danger / open wounds / fungal / dry soil / pot-bound) right before the season-end autosave. `CareLogPanel` in the Stats overlay: latest entry pinned, scrollable newest-first history, dirty-flag refresh on `CareLog.OnChanged`.

A running log of why each auto-care action was taken, and a plain-English summary of the tree's current health state.

- **Action log** — each auto-trim/wire/pinch/paste/defoliate/repot records a `CareLogEntry { date, action, nodeId, reason }`. Reason is a short templated string: `"Branch at 240° exceeds tier-2 slot limit (3 branches → trimming to 2)"`, `"Wire set after 1.4 seasons — removing before bite"`, `"Defoliated 94 tips to improve interior light penetration"`.
- **Health narrative** — at season end, generate a 2–3 sentence summary of tree health: highest-impact positive and negative factors (moisture, fungal, nutrients, recent damage, pot-bound pressure). E.g. `"The tree is thriving — moisture is stable at 72% and 3 new scaffold branches established this season. Two open wounds are draining health slowly; consider applying paste."`
- **UI** — small scrollable log panel shown in the Stats overlay, or accessible via a new "Log" tab in the existing stats/style area. Latest entry shown inline; tap to expand full history.
- **Scope:** new `CareLog.cs` (singleton with `List<CareLogEntry>`, last 200 entries, serialized in `SaveData`), `AutoStyler.cs` (`LogAction()` helper), `TreeSkeleton.cs` (season-end narrative builder), `ButtonUI.uxml` + `buttonClicker.cs` (log panel).

---

**E. Leaf Growth & Bud-Break Rework** ✅ done 2026-06-11

All parts shipped. Leaves emerge at 15% scale tinted by `leafBudBreakColor` and mature over `leafGrowDays` — both now `TreeSpecies` fields (defaults 20 d / pale yellow-green, so the 17 existing `.asset` files work unedited). Clusters unfurl in stages: first batch at bud break, then one leaf — a PAIR for Opposite-bud species — every `leafUnfurlIntervalDays` (1.5 d ± jitter, LeafManager Inspector field). The autumn bud GameObject is handed from `TreeSkeleton` to `LeafManager.BeginBudOpen()` at bud break instead of being destroyed: it swells ~1.6× over two days and is removed when its cluster finishes unfurling (`budLingerMaxDays` = 8 d fallback covers terminals that never qualify for leaves).

- **Leaf maturation** ✓ — leaves emerge at **15%** scale in a paler yellow-green and grow to full size/color over `leafGrowDays` in-game days (per-species; set on each Leaf at spawn).
- **Bud-opening sequence** — instead of an instant full cluster: the autumn bud GameObject swells slightly in early March, opens (small scale pop + first leaf pair) at bud break, then the remaining leaves of the cluster unfurl one-by-one over the following days. The bud mesh is destroyed only after the last leaf emerges.
- **Multiple leaves per bud** — cluster size already exists; reuse it as "leaves remaining to unfurl." Opposite-bud species unfurl in pairs, alternate species singly.
- **Scope:** `LeafManager.cs` (spawn pipeline → staged unfurl state machine, per-cluster age), `Leaf.cs` (scale/color over age), `TreeSkeleton.cs` (bud-break hook passes the bud object instead of destroying it immediately), `TreeSpecies` (`leafGrowDays`, optional paler `leafBudBreakColor`).

---

**F. Leaf Weight & Elastic Spring-Back** ✅ done 2026-06-11 *(extends item 30 — Branch Weight & Strength)*

Implemented: `ComputeLeafLoad` walks live cluster counts (`LeafManager.NodeLeafCount × leafMassEach`) bottom-up once per in-game day inside `ApplyDailySag`. Each branch tracks `elasticSagDeg` toward a target of `elasticSagMaxDeg` (6°) scaled by leaf-load/strength ratio, rate-capped at `elasticSagPerDayDeg` (1.5°/day) in BOTH directions — so the canopy weight eases branches down through summer and they spring back up over a few days as the leaves fall. `elasticSagDeg` persists in saves so loading doesn't double-droop. Tunables on TreeSkeleton under "Leaf Weight (Elastic Sag)".

Wood load and permanent sag exist (`ComputeLoad` → `ApplySagAndStress`, load = wood mass only). Branches should also carry seasonal LEAF load — and visibly lift a little when the leaves drop in autumn.

- **Leaf mass in load** — `ComputeLoad` adds `leafClusterMass × (seasonLeafScale factor)` for each node with a live cluster (LeafManager exposes per-node cluster lookup). Summer load > winter load.
- **Elastic vs permanent sag** — split sag into the existing permanent component (set wood — never recovers, current behavior) and a small **elastic** component proportional to current excess load, capped at ~5–6°. Elastic sag is applied as a transient rotation and **removed pro-rata as leaf load disappears** (LeafFall / defoliation) — the branch "springs back up a small amount" exactly like the real thing.
- **Implementation sketch** — track `elasticSagDeg` per node; on each BranchWeightPass compute target elastic from load ratio, RotateTowards by the delta (positive or negative), reusing the bounded-degrees approach from the 2026-06 sag fix. No new save fields needed if recomputed each pass.
- **Note:** permanent sag now bleeds in gradually (`pendingSagDeg` / `sagDegPerDay` over `sagSpreadDays`, default 100 in-game days, applied once per in-game day in `ApplyDailySag`) instead of snapping on the spring frame. Elastic sag should ride the same daily hook.
- **Scope:** `TreeSkeleton.cs` (`ComputeLoad`, `ApplySagAndStress`), `LeafManager.cs` (cluster mass query), `TreeSpecies` (optional `leafClusterMass`).

---

**G. Repot Soil Compaction & Rake Rework** ✅ done 2026-06-12 *(flow reworked same day after playtest: rake-first)*

**Playtest fix:** the original order (pick soil → rake) never showed the player a rake prompt — and two latent bugs hid the ball anyway: `RootRake` state wasn't in TreeSkeleton's `inRootMode` so the tree DROPPED back down on rake entry, and the soil ball was built in world space at the pot instead of around the lifted root mass. New flow: **Repot button → tree lifts with the soil-caked root ball (ball parented to the tree, cell centers ball-local) → rake → Confirm stores raked % → repot panel → preset click = `ApplyRepot()`** (compaction/scoring/CareLog + PotSoil.Repot + root regen + settle back down). Leaving root mode any other way mid-rake cleans up and discards the visit.

Shipped: every repot now enters the rake step (`IsPotBound` gate removed — that gate was why it "didn't work"); pot-bound soil is compacted, needing `potBoundHitsPerCell` (2) rake passes per cell. Raked cells break off as **code-generated clods** (jittered squashed boxes, soil material, no prefabs/Rigidbodies) that tumble under manual gravity and are destroyed `chunkCullDepth` (4 u) below the pot. Over-raking bare cells can snap fine roots (cooldown + chance, visible strand loss + small parent health sting, shown in the rake HUD). On confirm: the un-raked fraction becomes `PotSoil.compaction` — drainage ×(1−0.5·c) and retention +0.15·c for the first season, decaying 0.6/season — a ≥95% clean sweep with zero snapped roots gives the whole tree +0.03 health, and the whole thing lands in the CareLog. `soilCompaction` persisted in saves.

- **Always rake** — every repot enters the rake step; pot-bound just means MORE compacted soil and tangled roots (higher difficulty / more strokes).
- **Tessellated soil ball** — replace the current visual with ~100 low-poly soil chunks (simple irregular wedges in a hemisphere around the root mass; a jittered grid is fine — no fancy Voronoi needed). Chunk meshes are **generated in code** (randomized squashed boxes / 6–10-vert blobs with vertex jitter, soil-colored vertex tint) — no prefabs or art assets. Rake strokes knock loose the chunks they cross: detach with a small impulse away from the stroke, **ballistic fall with light tumble (manual gravity, no Rigidbody needed), destroyed once world Y drops below 0** (under the table). Chunk count remaining is the progress meter.
- **Compaction state** — fresh repots start with a "compacted ring" debuff (reduced aeration/water retention for the first season — fields already exist on PotSoil) that raking fully clears; lazy raking leaves some compaction.
- **Arcade scoring** — % soil removed vs fine-root damage: over-raking through the same cell repeatedly snaps fine roots (small health hit, visible root count drop). Clean sweep = small health bonus on the repot.
- **Scope:** `RootRakeManager.cs` (chunk field, stroke detection, scoring), `buttonClicker.cs` (remove the IsPotBound gate), `PotSoil.cs` (compaction debuff hook), small chunk prefab or procedural mesh.

</details>

---

## Pending — Detailed Specs

*(One spec per pending item; priority order is in the Active Priority Queue table above.)*

**Pot Drainage Holes — root escape + drainage** *(raised 2026-06-15)*

**Goal:** simulate the drainage holes in the bottom of pots. Roots grow out through them when the tree is root-bound (a visible "needs repotting" tell), and the holes drive water drainage.

- **Hole data (authoring):** per-pot holes marked with **visible cylinder GameObjects** placed at each drainage hole in the pot (under a `DrainageHoles` parent, tagged via a small `DrainageHole` marker component or a name convention). The cylinder is WYSIWYG: its **XZ position = hole centre**, **XZ scale/diameter = hole size**, **Y axis = drainage direction** through the bottom. At runtime `PotSoil` collects them into `(localCentre, radius)` discs and **disables their MeshRenderers** (invisible in-game, GameObjects kept so they can be re-shown for editing). Rides the pot's scale automatically (avoids the unit mess). A pot's drainage capacity = Σ πr² over its holes.
- **Root escape:** the root system already clamps roots at the pot floor (`TreeSkeleton.SpawnChildren` bottom-face containment). Add: when a root tip hits the floor, if it's within a hole's radius **don't clamp** — let it keep growing down/out below the pot (flag `escapedRoot`, render poking out the bottom). Gate on pot-bound pressure so roots only push out once crowded; reinforces the existing repot prompt.
- **Drainage/water:** `PotSoil` drain rate scales with total hole area — more/bigger holes → drains faster (less waterlogging); fewer/clogged → higher saturation → root-rot risk. **Phase-2 feedback loop:** escaped roots clog the holes over time → drainage drops → saturation climbs → another repot signal.
- **Show holes:** modelled into the pot mesh (visual) + a debug GL/gizmo overlay drawing hole positions + radii for authoring/tuning.
- **Depends on:** the pot-making pipeline + scale standardization (holes are authored per pot).
- **Scope:** `ItemDefinition`/`PotSoil` (hole data + drainage), `TreeSkeleton` (root escape in `SpawnChildren`/containment, `escapedRoot` flag + render), debug overlay.

---

**Themeable In-Game UI Rebuild** *(raised 2026-06-15 — TOP PRIORITY)*

**Goal:** every piece of UI follows the active `UiTheme`. Today the menus theme correctly because they're **code-built** and read `UiTheme.Current`; the in-game UI doesn't, because it's **`ButtonUI.uxml`** with colours hardcoded in the markup. The `GameUiThemer` overlay that retrofits theming onto the UXML is fragile and doesn't reliably win against the baked/code-set colours. Fix: rebuild the in-game UI the same way the menus are built.

**Approach (chosen: full C# rebuild — Option 1):** recreate the in-game panels as code-built UI Toolkit panels (same element types — `VisualElement`/`Button`/`Label` — just with colours from the palette instead of frozen markup), each reading `UiTheme.Current` and subscribing to `UiTheme.OnThemeChanged` to repaint.

**Scope (panels to rebuild / re-colour):** left tool column (Trim/Water/Pinch/Defoliate/Wire/Unwire + moisture bar), right tool column (Paste/Repot/Air Layer/Graft/Promote/Fertilize/Herbicide/Fungicide + Place Rock), Repot panel (soil/pot/rock), Care Log, Tree Health, Auto Style, Root Health, Settings/pause (tabs + sliders + save/load/resume), first-use tooltips, the speed/calendar/pause corner controls, the date label. Keep functional indicators (moisture/compaction/saturation/nutrient bars, health gradients) semantically coloured, not theme-flattened.

**Constraints / care:**
- **Preserve all behaviour and wiring** — collapse toggles, sliders, tabs, calendar, tool activation, gating (`ApplyToolGates`), Quick-Start/mode injections, etc. This is a *re-skin to code-built + themed*, not a redesign.
- Reuse the menu idiom already proven (`ItemCatalogPanel`/`JournalPanel` style helpers: `Pad`/`Round`/`Border`, palette properties).
- Map each element to a palette slot explicitly (panels→panelBg, surfaces/buttons→cardBg, primary actions→accent+ink, headers/values→accent/textMain/textSub, borders→edge). No fragile colour-classifier guessing.
- Retire `GameUiThemer` (and its `pm-` skip hooks) once the rebuilt panels self-theme; keep `UiTheme` + the menu panels as-is.
- Likely staged: settings/pause + tooltips first (worst offenders), then the stat panels, then the toolbars. Each stage shippable.

**Files:** primarily `buttonClicker.cs` (+ possibly extracting panel builders into helpers), `ButtonUI.uxml` (strip colour markup / or drop rebuilt panels from it), `UiTheme.cs` (any missing slots), remove `Progression/GameUiThemer.cs` at the end.

---

**Flower / Blossom & Fruit System** *(raised 2026-06-14)*

Species-specific flowering and fruiting — an extension of the existing bud + leaf systems, roughly leaf-system sized.

- **`FlowerManager`** (parallel to `LeafManager`): rides the same bud cycle. **Flower buds** set in autumn alongside leaf buds (`hasFlowerBud` on `TreeNode`), only once the tree is mature enough. At the species `bloomMonth` they open into procedural blossoms at the bud sites. Lifecycle mirrors leaves: bloom → hold → **petal drop** (reuse the leaf-fall animation) → (fruiting species) fruit set → ripen (colour shift) over summer→fall → drop.
- **Procedural meshes** (code-gen, no art, like leaves/weeds): blossom = petal quads + center, raceme = hanging cluster, catkin = dangling spike, berry/fig = small sphere, cone = elongated shape, samara = winged seed.
- **`TreeSpecies` params:** `bloomType` (None/Blossom/Raceme/Catkin/Cone/Berry), `bloomColor`, `bloomMonth`, `bloomBeforeLeaves` (cherry/plum bloom on bare branches first), `fruits`, `fruitColor`, `fruitSetMonth`, `fruitRipeMonth`.
- **Per-species:** Cherry (pink blossom, before leaves), Wisteria (purple racemes), Ficus (figs), Juniper (berry-cones), pine/spruce/cedar/cypress (cones), Japanese Maple (samaras + tiny flowers), Birch/Willow (catkins).
- **Hooks:** Care Log narrative ("in full bloom"), gamification milestone (first bloom), and a seasonal beauty beat for the AutoRun/cinematic.
- **Suggested approach:** vertical slice first — Cherry blossoms end-to-end (flower bud → spring bloom → petal drop) to prove `FlowerManager` — then fan out to the other species + fruit.
- **Scope:** new `FlowerManager.cs` (+ optional `Flower.cs` like `Leaf.cs`), `TreeNode` (`hasFlowerBud`), `TreeSpecies` (params above), `TreeSkeleton` (set flower buds in autumn `SetBuds`, open at bloom), `SaveManager` (persist bud/flower/fruit state), the 17 species `.asset` files.

---

**Needle Foliage — Conifers** *(built 2026-06-14)*

Needles can't use the per-leaf prefab path (a conifer has thousands of needles). Efficiency principle: never one object/mesh per needle — one **tuft** mesh holds a whole bundle, and a single tuft GameObject sits at each branch tip (one per tip, not per needle). All tufts in a tree share one `NeedleMesh` + one material, so the renderer batches them.

- **`NeedleMesh`** (static builder): one unit-scale tuft mesh per tree, by `FoliageType` — `PineFascicle` (long needles in bundles), `SpruceRadial` (short needles all round), `FeatheryFrond` (flat fern-like spray), `Scale` (juniper). Each needle is a double-sided tapered quad (winding verified — thin foliage must not go dark edge-on).
- **`LeafManager` branch:** needle species spawn one `SpawnNeedleTuft` per tip instead of a leaf cluster, reusing the `Leaf` component for scale-in, node-tracking, seasonal colour and (deciduous conifers) the autumn fall. `seasonLeafScale` uses `species.needleLength`; season miniaturization (refinement/pot-bound) still shrinks needles.
- **Evergreen vs deciduous:** `species.evergreen` true → needles persist year-round (autumn fall skipped; spring cleanup retires tufts on now-interior nodes so foliage tracks the canopy tips). False (dawn redwood, swamp cypress) → drops like leaves.
- **`TreeSpecies` params:** `foliageType`, `evergreen`, `needlesPerTuft`, `needleLength`. Wired on 10 species: 3 pines (fascicle), 2 spruces + 2 cedars (radial), dawn redwood + swamp cypress (frond), juniper (scale).
- **Follow-ups:** per-instance colour variety without breaking batching; pine "candle" extension animation; broadleaf-evergreen flag for ficus.

---

**H. Item Selection Menus (Pots / Rocks / Moss & Grass / Tables)**

One reusable catalog structure for all placeable items, surfaced at different moments.

- **`ItemDefinition` ScriptableObject** — name, category (`Pot | Rock | GroundCover | Table`), prefab/mesh reference, thumbnail, size variants, optional unlock condition (placeholder for gamification).
- **`ItemCatalogPanel`** — one shared card-grid overlay (reuse the SpeciesSelect pattern: scrollable cards, name + thumbnail + chips), filtered by category at open time.
- **Entry points (different times, same panel):** Pots → "Choose pot…" in the repot panel; Rocks → the repot Rock toggle / RockPlace entry picks WHICH rock first; Tables → settings or new-game setup; Moss/Grass → a Ground tool usable during normal play (spawns cover patches; ties into the existing moss-suppression/herbicide systems later).
- **Apply handlers per category** — pot swaps the pot mesh + maps to `PotSize`; rock feeds `RockPlacer`; table swaps the Platform mesh; ground cover spawns patch instances.
- **Scope:** `ItemDefinition.cs`, `ItemCatalog.cs` (registry), panel UXML + `buttonClicker` wiring, per-category apply hooks. Content (actual models) authored separately and dropped into definitions.

---

**K. Additional AutoStyler Styles** *(LOWEST priority; discussion pending — user will provide descriptions + reference images)*

Beyond Moyogi and S-Curve. Likely set: **Chokkan** (formal upright), **Shakan** (slant), **Han-Kengai / Kengai** (semi/full cascade — branch growth below rim already allowed outside the pot box), **Fukinagashi** (windswept), **Hokidachi** (broom), **Bunjin** (literati). Most are pure `StyleDefinition` assets (waypoints/tiers/silhouette), but two need small engine extensions: cascade wants height bands below the soil line (negative `heightNorm` support in tiers + silhouette), and windswept wants ASYMMETRIC slot azimuths (today slots are evenly spaced per tier — add an optional explicit per-slot azimuth list to `BranchTier`).

---

---

**19. Gamification & Tutorial Progression** — ✅ design pass done (`Docs/PROGRESSION_DESIGN.md`). NOT XP/levels — direction is **Career/Sandbox modes + a zen meta-layer** (gentle milestones, Journal/Encyclopedia) + a **soft cosmetic currency** ("Aesthetic Points") earned through stewardship that buys aesthetic upgrades only (pots/rocks/tables/decor via the H Item Catalog), never gameplay power. Build in 4 slices (economy+zen core → shop → Career gating/mode select → content). See the design doc.

**20. Multi-Tree / Quick-Start** — multiple trees per session with per-tree save/load; auto-generate a tree at a given age with a randomised style for quick starts.

**21. Decoration System** — figurines / accent plantings / ground accents placed in the scene; overlaps H's GroundCover/decor catalog, so fold in after H.

---

> **Archive:** the original numbered queue items 1–18 (all done) and the Autonomous Run Loop (`AutoRunManager.cs`, done) are recorded in the sections below and in the **Completed Items Log** at the bottom of this doc.

---

## Completed — Auto-Style Engine & Earlier Phases (detail)

- **AutoStyler — slot-based plan engine** ✓ — `StyleDefinition` ScriptableObject (trunk waypoints, branch tiers with `azimuthOffsetDeg`, canopy silhouette curve, ramification settings). `AutoStyler.cs` greedy slot-matching (depth=1 branches matched to nearest azimuth slot); `BranchSlot` + `SlotState` (Empty/Growing/Training/Established/Maintaining); seasonal schedule (spring: slot refresh + trunk wire; Feb: back-bud stimulation; Apr/May: silhouette pinch; Jun: ramification; Oct: scaffold wires). `AutoStyler.Instance` static accessor + public slot/pending accessors.
- **AutoStyler — GL intent indicators** ✓ — All indicators are intent-based (always visible year-round, not queue-based). Orange X (6 lines, 3 planes) on every unmatched depth=1 branch = trim candidate. Cyan circle + crosshair on every Growing/Training assigned branch = wire candidate. Green spike at tip for queued pinches. Colored slot diamonds with trunk spoke. Canopy silhouette rings (cyan), tier boundary rings (orange), trunk waypoint crosses + lean arrows (yellow).
- **AutoStyler — auto-unwire gold timer** ✓ — `wireGoldDay` dictionary tracks the in-game day when each auto-wired node first reaches `wireSetProgress >= 1f`. `RemoveSetWires()` now runs every frame in `Update()`; unwires nodes whose gold day + `unwireDelayDays` (default 20) has elapsed. No longer waits until spring or until damage starts.
- **Style Panel UI** ✓ — `StylePanel` VisualElement in `ButtonUI.uxml`, positioned above Root Health Panel. Shows: style name, match % (color-coded green/yellow/red), occupied/total slots, state breakdown (E:n G:n T:n Est:n M:n), pending trim/wire/pinch counts, shaped trunk node count. Wired in `buttonClicker.cs` via `AutoStyler.Instance`; shown/hidden with Stats toggle.
- **Rainbow root debug overlay** ✓ — `debugRainbowRoots = true` field on `TreeMeshBuilder`; depth-coded color lines drawn in existing `OnRenderObject()` for all root nodes. Enabled by default for debugging. Toggle in Inspector.
- **StyleDefinitionCreator** ✓ — Editor script `Bonsai → Create Default Styles` regenerates Moyogi.asset and SCurve.asset with all new fields (azimuthOffsetDeg, ramification). Run after any `StyleDefinition` field additions.

- **Phototropism coordinate space fix** ✓ — `SunDirection()` in `TreeSkeleton` was returning world `Vector3.up`; `growDirection` is tree-local, so Slerping the two produced wrong results when the tree was tilted on a rock. Fixed to `transform.InverseTransformDirection(Vector3.up)`.
- **PinchNode bud-system fix (critical)** ✓ — `PinchNode()` was setting `node.isTrimmed = true`, which excluded the node from autumn `SetBuds()`. With `budSystemActive = true` in year 2+, only `hasBud` nodes can spawn — so all pinched nodes were permanently frozen with no path back. Fix: removed `isTrimmed = true`; use `node.length = node.targetLength` instead to halt extension while keeping the node alive for the bud cycle. Growth resumes next spring via normal bud break.
- **Defoliate hover fix** ✓ — `HandleDefoliateHover` required `n.isTerminal`, but by June most leaf-bearing nodes have already branched and are non-terminal. Removed the `isTerminal` filter; now targets any non-root, non-trimmed node that has a leaf cluster.
- **Root health NaN fix** ✓ — `RecalculateRootHealthScore()` was dividing `com /= totalRadius` when all root nodes had zero radius. Result: NaN → `Mathf.RoundToInt(NaN)` = `int.MinValue` = -2147483648 in the UI. Added `|| totalRadius <= 0f` to the early-return guard. Added NaN/Infinity display guard in `buttonClicker.UpdateRootHealthDisplay`.
- **3-speed time mode** ✓ — `SpeedMode` enum (Slow=0.5, Med=10, Fast=200 hrs/s) replaces the bool `IsSlowSpeed`. `ToggleSpeed()` cycles Slow→Med→Fast→Slow. Speed button shows ▶/▶▶/▶▶▶ with amber/grey/green tints. Auto-slow trigger moved from June to **April** (June is too late — growth is already done; April is the pinching window). `IsSlowSpeed` kept as back-compat property.
- **`OnMonthChanged` event** ✓ — `static event Action<int>` on `GameManager`, fired at end of `SetMonthText`. Drives month-triggered tutorials (April ramification) without polling.
- **`OnWireSetGold` event** ✓ — `public event Action` on `TreeSkeleton`, fired the first frame `wireSetProgress` crosses from <1 to ≥1 for any node. Unwire tooltip is now event-driven (fires on first gold wire) rather than button-click-driven.
- **April ramification tutorial** ✓ — `OnMonthChanged(4)` in `buttonClicker` triggers `MaybeShowTooltip("april_ramification", ...)` explaining the pinch window, auxin suppression, and back-budding.
- **Fertilizer tutorial** ✓ — `MaybeShowTooltip("fertilize", ...)` added to `OnFertilizeButtonClick`, covering seasonal timing and why the button dims Nov–Feb.
- **Herbicide tutorial** ✓ — `MaybeShowTooltip("herbicide", ...)` added to `OnHerbicideButtonClick`, covering nutrient competition and moss suppression.
- **Pinch visual indicators** ✓ — `DrawPinchMarkers` in `TreeInteraction` (registered to `endCameraRendering`) draws a GL octahedron at every pinchable tip when the Pinch tool is active: dim lime (r=0.055) for all eligible tips, bright lime (r=0.12) for the hovered tip. `hoveredPinchNode` cleared at top of `Update` each frame. `DrawGLDiamond` static helper draws a camera-facing 3-axis diamond.
- **Scale debug cubes** ✓ — `ScaleDebugger.cs` component draws GL wireframe 1×1×1 m cubes in `OnRenderObject`, centred on a `gridRadius×2+1` grid at `plantingSurfacePoint.y`. Toggled from Debug tab (`ToggleScaleCubes`).
- **UI button layout** ✓ — All tool buttons except Trim/Water reduced to 50px height. Fertilize/Herbicide/Fungicide group moved from `top:279` to `top:204` (immediately below Graft, no gap). Confirm/Cancel orient buttons changed from right-column stacked to centered row (`left:0; right:0; flex-direction:row; justify-content:center`) at `top:340px`.

- **22.** Fertilizer System — `nutrientReserve` (0→2) drains 0.4/season; `Fertilize()` blocked in winter; `nutrientMult` Lerp(0.6,1.4) multiplied into per-frame growth speed; FertilizerBurn on roots if reserve >1.5 at spring start; Fertilize button + nutrient bar right-side panel; Auto-fertilize toggle in Debug tab; serialized in SaveData
- **23.** Weed System — RMB click-hold-drag-up to pull; rip chance leaves stub (harder next pull, stump visual, 60% drain); Herbicide button clears all weeds + sets aeration penalty next season; WeedManager spawns procedural cube weeds (grass/clover/dandelion/thistle) as tree children; seasonal nutrient+moisture drain; weeds serialized in SaveData
- **24.** Fungus System — `fungalLoad` (0–1) per node; spreads from open wounds / overwatered roots / low-health nodes; seasonal spread to neighbours; FungalInfection DamageType; Fungicide button reduces load 0.6 across all nodes; mycorrhizal network on healthy root nodes (3+ healthy seasons) reduces nutrient drain 20%; herbicide kills mycorrhizae; infected leaves tint toward sickly yellow-green via MaterialPropertyBlock; all fields serialized
- **25.** Species Skeleton — `TreeSpecies` ScriptableObject; `ApplySpecies()` copies into existing `TreeSkeleton` fields on Awake; `BudType` moved to own file; species name displayed in Settings menu header; Japanese Maple (Opposite buds, fast/thirsty/fragile) and Juniper (slow/drought-tolerant/resilient) as starter species
- **26.** Species Selection Menu — fullscreen overlay on game start; 16 species with Growth / Water / Care / Soil chip tags; sortable by any tag; confirms into TipPause with species applied; `SpeciesSelect` GameState; ToolTip fixed to only show in TipPause without touching Main Camera
- **27.** Soil / Substrate System — `PotSoil` component; 7 substrates (akadama, pumice, lava rock, organic, sand, kanuma, perlite); weighted mix → derived properties; seasonal degradation + saturation + root rot; species soil mismatch penalty; `Repot()` with timing and too-soon stress multipliers; 4 presets; soil bars in Repot panel; 16 species .asset files with soil preferences; Roots→Repot rename; weeds auto-cleared on entering Repot mode; weeds excluded from trunk radius and rock surfaces
- **Pause Menu** ✓ — `GameState.GamePause`, `TogglePause()`, pause overlay in `buttonClicker.cs`
- **Autosave** ✓ — `SaveManager.AutoSave()` creates slot on first save, fires end-of-season
- **Calendar Play Modes tab** ✓ — `PlayModeManager.cs` singleton; `SpeedRuleTrigger` enum (Month, Season, MoistureBelow, HealthBelow, NutrientBelow, FungalLoadAbove, WeedCountAbove, WireSetGold, TreeInDanger); `SpeedRule` / `PlayMode` data model; lowest-speed-wins evaluation loop; idle re-arm via `unscaledTime` / in-game days; auto-water + auto-fertilize flags synced to `TreeSkeleton` each frame; 4 built-in presets; JSON persistence via `PlayerPrefs` + `JsonUtility`; 3-tab calendar strip (Schedule/Modes/Speed)
- **Calendar Speed Config tab** ✓ — `TIMESCALE_SLOW/MED/FAST` changed from `const` to `static float`; PlayerPrefs `ts_slow/med/fast` loaded in `Awake`, saved on slider change; ordering enforced (Slow < Med < Fast); three sliders with live `"1 in-game day = X"` hint labels and Reset button
- **Idle camera orbit** ✓ — `CameraOrbit.cs` saves `(yaw, pitch, radius, panY)` on orbit start; slow yaw at 4°/s + elevation ±5° sine (20 s period) using `unscaledDeltaTime`; any mouse/keyboard input stops orbit and restores saved state in one frame; driven by `PlayModeManager.IdleOrbitActive`
- **Calendar exit → medium speed** ✓ — `CloseCalendar()` calls `gm.SetSpeedMode(Med)` so PlayModeManager re-evaluates rules next frame
- **Calendar Parts 1–4** ✓ — Real month lengths (`DaysInMonth[]`, `IsLeapYear`, `DaysInCurrentMonth`); `ScheduledEvent` data model with `RepeatMode`/`Season`/`TimeOfDay`; `CheckScheduledEvents()` fires per day-tick; full calendar UI: day grid, day-detail view, add-event form (type chips, repeat toggle, N-day/N-week cadence, season scope), enable/disable toggle, delete, seasonal templates
- **Autosave System** ✓ — `SaveManager.AutoSave()` auto-creates a named slot (`"{Species} {Year} (autosave)"`) when no `ActiveSlotId` set; fires end-of-season in `TreeSkeleton`, after repot, after air layer sever, after Ishitsuki confirm. Toast feedback pending.
- **Root Containment fix** ✓ — terminal clamp in `SpawnChildren`: if root tip escapes side or bottom of `rootAreaTransform` box, sets `isTrimmed = true`; top-face emergence left alone; `distRatio >= 1.3f` hard-stops any root beyond 130 % of pot radius
- **Repot Root Raking** ✓ — `RootRakeManager.cs`; `GameState.RootRake`; rake brush spreads root nodes visually; prune-by-click removes excess roots; `hasLongRoot` flag on skeleton → `RegenerateInitialRoots` spawns bonus long strand; Confirm/Cancel buttons; root-count indicator in HUD
- **Pot Size Selection** ✓ — `PotSoil.PotSize` enum (XS/S/M/L/XL/Slab); `ApplyPotSize()` resizes `rootAreaTransform`; size buttons in repot panel; serialized in `SaveData`.
- **Rock Size Selection** ✓ — `RockPlacer.RockSize` enum (S/M/L/XL); `ApplyRockSize()` sets `transform.localScale`; S/M/L/XL chip buttons in HUD shown during RockPlace state; `SaveData.rockSize` serialized; restored in `LoadFromSaveData`.
- **Sibling Branch Fusion** ✓ — Automatic spring detection: siblings with tip proximity ≤ (rA+rB)×2.5 register a `FusionBond`; 4-season fuse creates bridge node (`isGraftBridge`) between tips; aborted if either node dies/trims; `SaveFusionBond` list serialized in `SaveData`.
- **Bark Texture System** ✓ — Optional pixel-art texture tiers on `TreeSpecies` (`youngBarkTexture`, `matureBarkTexture`, `barkVTiling`, `barkTexelRes`, `barkNoiseMode`). Shader (`BarkVertexColor.shader`) samples `_BarkTexA`/`_BarkTexB` when `_UseTextures=1`; per-texel pixel-perfect noise hard-threshold (scatter or Voronoi cellular) driven by vertex alpha `blend`. UV V-tiling driven by `barkVTilingScale` in `TreeMeshBuilder.AddRing`. Falls through to fully procedural bark when both textures are null — no art required to run.
- **Compiler fixes** ✓ — Duplicate `[System.Serializable]` on `SaveFusionBond` removed; `CursorStyle`/`IStyle.padding` errors fixed (cursor lines removed, padding split into four properties); `StyleLength(0f)` disambiguation; `statsToggleButton` dead field removed; all `FindObjectOfType<T>()` → `FindFirstObjectByType<T>()` across `PlayModeManager`, `TreeSkeleton`, `SaveManager`, `buttonClicker`.
- **debugSoilY sentinel** ✓ — `-9999` sentinel auto-populates from `plantingSurfacePoint.y` on first use
- **Camera root-mode regression** ✓ — `lastTargetPosition` delta compensation, `isDragging` safety-clear, pitch clamp per state
- **Cinematic mode fixes** ✓ — (a) C key toggles smooth constant orbit independent of timescale; (b) auto-zoom eases radius to `treeHeight × mult`, clamped to `zoomMin/Max`; (c) half-speed orbit when `GameManager.canTrim` (trim tool active); (d) CM no longer killed by normal state transitions (Water, BranchGrow, LeafFall, etc.) — only cancelled for editing states (RootPrune, WireAnimate, etc.)
- **Cinematic zoom lag fix** ✓ — `cachedTreeHeight` updated once per in-game day during growing season (inside the existing `lastRecalcDay` block) so the camera tracks real-time spring growth instead of waiting until the following spring
- **Dead-tree restart fix** ✓ — `TreeSkeleton.ClearForRestart()` destroys all visuals and nulls root; called from `OnDeadRestartClick` along with `GameManager.waterings = -1` so the next Water event re-triggers `InitTree()` on a blank slate
- **Seed material** ✓ — Seed sphere now uses the tree's own `sharedMaterial` (bark shader) instead of a `new Material(Standard)`
- **Root containment** ✓ — hard clamp in `SpawnChildren`: skips spawn if tip is outside side/bottom of `rootAreaTransform` box; top-face emergence allowed
- **Rock Placement UI lock + Cancel** ✓ — HUD dims (opacity 0.25 + PickingMode.Ignore) during RockPlace/TreeOrient; Confirm/Cancel always visible; Cancel restores pre-placement snapshot; camera-relative tree translation
- **New Input System Migration** ✓ — all `Input.*` calls replaced with `Mouse.current` / `Keyboard.current`; EventSystem updated to `InputSystemUIInputModule`
- **Growth Season Taper (item 34)** ✓ — `GrowthSeasonMult()` in `TreeSkeleton`; `growthSlowDay`/`growthStopDay` on `TreeSpecies`; `dayOfYear` on `GameManager`; 16 species assets updated; `species == null` guard
- **Roots → bark color over time** ✓ — removed `isRoot && !isTrainingWire` exclusion from age accumulation loop; exposed roots bark 3× faster via `fadeDays/3` in `GrowthColor`
- **Branch Saw** ✓ — `sawRadiusThreshold` (0.08) on `TreeSkeleton`; Saw tool triggers multi-stroke mechanic for thick branches; direction-reversal half-stroke counting (10 half-strokes = done); dark annulus groove deepens toward center as progress advances; ESC/RMB cancels; completes via normal `TrimNode` path

---

## Numbered Items

All items sorted ascending. Click a title to expand the spec.

---

<details>
<summary><strong>1. Tight-Angle Geometry</strong> ✓ done</summary>

**Goal:** Prevent vertex pinching where branches bend sharply.
**Scope:** `TreeMeshBuilder.cs`

- Bend rings inserted at child base
- Parallel-transport fix prevents tip ring twist

</details>

---

<details>
<summary><strong>2. Post-Trim Depth Cap</strong> ✓ done</summary>

**Goal:** After trimming back hard, regrowth is limited to early-year depths —
a branch cut to depth 1 can't grow 6 levels in one season.
**Scope:** `TreeSkeleton.cs`, `TreeNode.cs`

- Add `trimCutDepth` and `regrowthSeasonCount` fields to `TreeNode`
- When a node is identified as a trim cut point, record its depth
- Each new growing season, the cut point's subtree gets a depth cap of:
  `trimCutDepth + (regrowthSeasonCount * REGROWTH_DEPTH_PER_SEASON)`
  where `REGROWTH_DEPTH_PER_SEASON` mirrors the year-1 allowance
- `regrowthSeasonCount` increments each spring on affected nodes
- After enough seasons the cap naturally reaches the global `depthCap` and the
  special treatment ends

</details>

---

<details>
<summary><strong>3. Wire Rework + Health System Foundation</strong> ✓ done</summary>

**Goal:** Realistic wiring with meaningful consequences; health system that
future mechanics (watering, nutrients, trimming trauma) can feed into.

#### 3a. Wire Rework

**New wiring flow:**
1. Player aims and confirms wire direction
2. `GameState.WireAnimate` fires — time frozen, camera still moves
3. Branch snaps to `wireTargetDirection` immediately in skeleton data
4. A ~0.6 s spring animation plays on the mesh with slight overshoot + settle
5. On animation end → auto-resume prior game state

**New data on `TreeNode`:**
```
wireOriginalDirection   Vector3   direction at time of wiring
wireTargetDirection     Vector3   player-aimed direction
wireSetProgress         float     0→1, fully set = ready to remove
wireDamageProgress      float     0→1, accumulates after fully set
wireAgeDays             float     total in-game days wire has been on
```

**Set/damage accumulation:**
- Both progress values accumulate only during `BranchGrow` state
- Rate uses the same `SeasonalGrowthRate` multiplier
- `setProgress` fills over ~2 growing seasons at speed 1
- `damageProgress` begins filling as soon as `setProgress >= 1.0`

**Early removal (setProgress < 1.0):**
```
newDirection = Slerp(wireOriginalDirection, wireTargetDirection, setProgress)
```

**Re-wiring a previously-set branch:**
- Allowed freely; no cooldown
- Re-bending set wood applies a health hit: `damage = Lerp(0.05, 0.25, setProgress)`

**Wire colour progression:**
| Condition | Colour |
|---|---|
| `setProgress` 0→1 | Silver |
| `setProgress >= 1.0` | Gold emissive pulse |
| Damage zone begins | Gold → Orange |
| `damageProgress >= 0.5` | Orange → Red |
| `damageProgress >= 1.0` | Deep red, embedded |

#### 3b. Health System (Foundation)

**`float health = 1f` on `TreeNode`, range 0→1.**

Health thresholds:
| health | Effect |
|---|---|
| `< 0.75` | Growth rate multiplied by `health` |
| `< 0.5` | Leaves drop early; branch tints grey |
| `< 0.25` | Branch goes dormant |
| `<= 0` | Branch dead |

`DamageType` enum lives in `NodeHealth.cs`.

</details>

---

<details>
<summary><strong>4. Root System</strong> ✓ done</summary>

**Goal:** Visible surface/subsurface roots (nebari), trimmable in RootPrune mode
and naturally scorable for flared-root development.
**Scope:** `TreeSkeleton`, `TreeNode`, `TreeMeshBuilder`, `TreeInteraction`, `CameraOrbit`

- `isRoot` flag on `TreeNode`; roots are children of `skeleton.root`
- Gravity-biased `ContinuationDirection` and `LateralDirection` for root nodes
- Separate depth cap: `maxRootDepth` for root nodes
- `renderRoots` flag on `TreeMeshBuilder`
- `PlantRoot(Vector3 localDir)` — player-triggered from soil plane click
- `GameState.RootPrune`: tree lifts, roots revealed
- `HandleRootWorkHover()` in `TreeInteraction`
- `CameraOrbit` pitch relaxed to `pitchMinRootPrune` in RootPrune mode

</details>

---

<details>
<summary><strong>5. Bud System</strong> ✓ done</summary>

**Goal:** Spring growth emerges from pre-formed buds set the previous late summer.

**Phases:**

**Bud Set (August):**
- `node.hasBud = true`
- Bud GameObject spawned at `node.tipPosition`

**Dormant lateral buds:**
- `dormantBudCount` tracks latent axillary buds
- Increased activation chance if `backBudStimulated = true`

**Bud Break (March):**
- Bud GameObjects destroyed
- Growth begins from those nodes

**Back-budding from pruning:**
- Nearest 2–3 ancestor nodes get `backBudStimulated = true`
- Next spring: elevated lateral activation chance

**New data on `TreeNode`:**
```
hasBud               bool
backBudStimulated    bool
```

</details>

---

<details>
<summary><strong>6. Wound System</strong> ✓ done</summary>

**Goal:** Trimming branches leaves wounds that are a real health risk without care.

**Wound lifecycle:**
1. Branch trimmed → wound created at parent node
2. Wound GameObject spawned, scaled by `woundRadius`
3. Each growing season: wound drains health
4. Player applies cut paste → `pasteApplied = true`, drain drops to ~5%
5. `woundAge` increments each season; heals when `woundAge >= woundRadius × seasonsToHealPerUnit`

**Health drain:**
```
drain = woundRadius × woundDrainRate × (pasteApplied ? 0.05f : 1.0f)
```

**New data on `TreeNode`:**
```
hasWound       bool
woundRadius    float
woundAge       float
pasteApplied   bool
```

</details>

---

<details>
<summary><strong>7–16. Early Systems</strong> ✓ done</summary>

These items were completed in an earlier development phase. Full details in the Completed log at the bottom of this document.

- **7.** Bud/Leaf Integration — `birthYear` on `TreeNode`; leaves from buds on old wood
- **8.** Growth Stability — `maxBranchNodes` hard cap, `vigorFactor` lateral scaling
- **9.** Leaf Energy System — `treeEnergy` multiplier from canopy photosynthesis
- **10.** Refinement Level — `refinementLevel` on nodes; segment-length taper per level
- **11.** Dynamic Leaf Scale — root pressure + refinement drive `seasonLeafScale`
- **12.** Ishitsuki (Root-over-Rock) — `RockPlace` + `TreeOrient` states, training wires, drape logic
- **13.** Watering System — `soilMoisture`, `drainRatePerDay`, drought threshold + damage
- **14.** Save / Load System — full JSON save/load; `SaveData`, `SaveNode`, auto-save on season end
- **15.** Root Area Box Containment — `rootAreaTransform` replaces radial spread; six-face deflection
- **16.** Pot-Bound Root System — `boundaryPressure`, `boundaryGrowthScale`, fill-in laterals

</details>

---

<details>
<summary><strong>17. Root Visibility Bug (Post-Ishitsuki)</strong> ✓ done</summary>

**What was built:**
- `debugRootVisibility` toggle on `TreeMeshBuilder` draws GL lines on every root node:
  Cyan = isTrainingWire, Yellow = isAirLayerRoot, Green = included, Red = excluded
- `[RootVis] BuildMesh` log fires only on state change, not every dirty rebuild
- **Ghost root fix:** `SpawnTrainingWires` was calling `child.children.Clear()` on old
  root chains — removing list references but leaving old nodes in `allNodes` with
  `isGrowing=true`. Fixed by calling `RemoveSubtree` on each old child before clearing.

**Files changed:** `TreeSkeleton.cs`, `TreeMeshBuilder.cs`

</details>

---

<details>
<summary><strong>18. Auto-Water</strong> ✓ done</summary>

Waters automatically just before drought threshold is reached. On by default; Debug tab toggle. When auto-water fires, the Water button pulses between light and dark grey (0.15 s). In-game-day cooldown prevents rapid-fire at high timescale.

**Files changed:** `TreeSkeleton.cs`, `buttonClicker.cs`, `ButtonUI.uxml`

</details>

---

<details>
<summary><strong>19. Ishitsuki White First Segment</strong> ✓ done</summary>

**Bug:** The first segment of each Ishitsuki root chain (`startNode`) stayed permanently
white — never transitioned to bark colour because the age accumulation loop skipped
non-training-wire roots.

**Fix:** `startNode` marked `isTrainingWire=true` in `PreGrowRootsToSoil` so age
accumulates and it transitions to bark colour like the rest of the chain.

**Scope:** `TreeSkeleton.cs`

</details>

---

<details>
<summary><strong>20. Ishitsuki Roots Continue Underground</strong> ✓ done</summary>

**Bug:** After a training wire chain reached the soil plane, the terminal node was frozen.
Nothing transitioned it into the normal pot root system.

**Fix:** In `PreGrowRootsToSoil`, after the step loop breaks at soil, create one transition
node at the soil contact point (`isTrainingWire=false`) that enters the normal root system.
Underground roots blocked from growing if tip is above soil.

**Scope:** `TreeSkeleton.cs`

</details>

---

<details>
<summary><strong>21. Ishitsuki Cable Growth Animation</strong> ✓ done</summary>

**Feature:** New training wire chains visibly grow down the rock face each spring rather
than appearing fully pre-grown instantly.

**Implementation:** `PreGrowRootsToSoil(animated:true)` each spring places one segment
per strand. Confirm uses `animated:false` (instant full drape). Air-grown chain cleanup
preserves training-wire progress.

**Scope:** `TreeSkeleton.cs`

</details>

---

<details>
<summary><strong>22. Fertilizer System</strong> ✓ done</summary>

`nutrientReserve` (0→2) on `TreeSkeleton`. Drains 0.4/season; `Fertilize()` adds 0.5,
capped at 2, blocked in winter. Growth speed multiplier: `Lerp(0.6, 1.4, reserve/2)`.
FertilizerBurn applied to root nodes each spring when `reserve > 1.5`. Fertilize button
+ nutrient bar on right-side panel. Auto-fertilize toggle in Debug tab. Serialized.

</details>

---

<details>
<summary><strong>23. Weed System</strong> ✓ done</summary>

RMB click-hold-drag-up to pull weeds; positive Y delta accumulates pull progress.
Rip chance (per type) leaves a stub — shorter, brown, harder to pull next time.
`Physics.RaycastAll` required to click through Bonsai/PlanterTable colliders.
Weeds parented to tree GO so camera-orbit drag doesn't conflict. WeedManager singleton
auto-adds WeedPuller. Herbicide button clears all + sets aeration penalty. Four types:
Grass (40%), Clover (35%), Dandelion (15%), Thistle (10%). Serialized.

</details>

---

<details>
<summary><strong>24. Fungus System</strong> ✓ done</summary>

`fungalLoad` (0–1) + `isMycorrhizal` + `healthySeasonsCount` on `TreeNode`. Each spring:
nodes with open wounds, overwatered roots (`soilMoisture > 0.9`), or low health (<0.5)
accumulate fungalLoad; infected nodes spread to parent/children with 25% chance; nodes
above 0.4 load take `FungalInfection` damage scaled by excess. Recovery: 0.1/season when
no longer at-risk.

Mycorrhizal: root nodes healthy 3+ seasons become `isMycorrhizal`; reduces nutrient drain
by up to 20% based on coverage fraction. Fungicide and herbicide both kill mycorrhizae.

Visual: leaf tint toward sickly yellow-green via `MaterialPropertyBlock` + `fungalSeverity`
field on `Leaf.cs`.

Fungicide button on right-side panel; dims when no infection present; calls `ApplyFungicide()`
(reduces all loads by 0.6) + immediate leaf refresh. All fields serialized.

</details>

---

<details>
<summary><strong>25. Species — Skeleton</strong> ✓ done</summary>

`TreeSpecies` ScriptableObject created with all core species-differentiating parameters.
`ApplySpecies()` on `TreeSkeleton` copies ScriptableObject values into existing fields on
Awake — zero changes to downstream code, existing SerializeField values become fallbacks.
`BudType` enum moved to its own file so `TreeSpecies` can reference it.
Species name displayed in Settings menu header.

**Starter species shipped:**
- **Japanese Maple** — Opposite buds, fast growth (0.26), thirsty (drain 0.14/day), fragile wounds (0.08 drain/season), wire sets fast (140 days), high lateral density
- **Juniper** — Alternate buds, slow growth (0.14), drought-tolerant (drain 0.06/day), resilient wounds (0.03), wire sets slow (280 days), strong apical dominance

**Species Visuals** (bark shaders, leaf shapes, seasonal colour sets) deferred to after Health Consequences phase.

</details>

---

<details>
<summary><strong>27. Soil / Substrate System</strong> ✓ done</summary>

Full spec in Backlog → Soil / Substrate System.

</details>

---

<details>
<summary><strong>28. Tree Death ✓</strong></summary>

Full spec in Backlog → Tree Death. **Toggleable** — `treeDeathEnabled` bool on TreeSkeleton; all death checks skip when false. Off by default; turned on for testing then back off.

**Done:** `treeDeathEnabled` toggle, drought/health death conditions, `consecutiveCriticalSeasons` counter, `treeInDanger` flag, `LastDeathCause`, `GameState.TreeDead`, death overlay with load/restart buttons, `TreeDangerBanner` warning.

</details>

---

<details>
<summary><strong>29. Branch Death & Dieback ✓</strong></summary>

Full spec in Backlog → Branch Death & Dieback.

**Done:** `isDead`, `isDeadwood`, `shadedSeasons`, `deadSeasons` on `TreeNode`. `DiebackPass()` each spring: marks zero-health nodes dead, shading check on interior nodes (no living terminal children), small dead branches drop after `deadSeasonsToDrop` seasons, large ones become permanent deadwood. All fields serialized.

</details>

---

<details>
<summary><strong>30. Branch Weight & Strength ✓</strong></summary>

Full spec in Backlog → Branch Weight & Strength.

**Done:** `branchWeightEnabled` toggle + inspector fields. `BranchWeightPass()` calls
`ComputeLoad()` (bottom-up mass accumulation) then `ApplySagAndStress()` (maturity/strength
ratio, sag angle accumulation, `growDirection` Slerp toward down, `PropagatePositions()`
for descendant world positions, junction stress damage). `branchLoad` and `sagAngleDeg`
saved and restored in SaveManager.

</details>

---

<details>
<summary><strong>26. Multi-Tree / Quick-Start</strong> (moves here after Health phase)</summary>

Multiple trees in one session; save/load per-tree. Auto-generate a tree at a given age
with randomised style.

</details>

---

<details>
<summary><strong>31. Air Layer Root Continued Growth ✓</strong></summary>

Full spec in Backlog → Air Layer Root Continued Growth.

**Done:** `ContinuationDirection` now handles rock-surface deflection and soil-plane snap for
`isAirLayerRoot` nodes instead of early-returning. Continuation nodes transition to
`isAirLayerRoot = false` once their parent tip is at or below `plantingSurfacePoint.y`,
allowing underground growth to proceed as normal root segments.

</details>

---

<details>
<summary><strong>33. Named Save / Load Menu ✓</strong></summary>

**Goal:** Replace the single-slot save with a named multi-slot system. All saves persist
across sessions and are browsable from a load screen. Each save shows key metadata so the
player knows what they're loading without guessing from a filename.

---

**Save metadata (stored alongside each save):**

| Field | Notes |
|---|---|
| `saveName` | Player-entered string (max 32 chars) |
| `treeOrigin` | Enum: `Seedling`, `Cutting`, `AirLayer` |
| `speciesName` | Human-readable species name |
| `year` / `month` | In-game date at save time |
| `screenshotPath` | Optional thumbnail (128×128 PNG, same folder) |
| `saveTimestamp` | Real UTC time for sorting (ISO 8601 string) |
| `seasonsSinceRepot` | Quick health indicator |

`treeOrigin` is set once at tree birth and never changes:
- **Seedling** — default; player selected a species in the picker and started fresh
- **Cutting** — started from a `propagation` save (future feature hook; default to Seedling for now)
- **AirLayer** — tree was created via `SeverAirLayer`

---

**Data layout on disk:**

```
Application.persistentDataPath/
  saves/
    <slot-id>/           ← GUID or timestamp string, e.g. "20260407_143022"
      save.json          ← full SaveData (existing format)
      meta.json          ← SaveMeta (new lightweight struct)
      thumb.png          ← optional 128×128 screenshot
```

The existing `bonsai_save.json` and `bonsai_original.json` paths remain as a compatibility
fallback for the first session; a migration step on first load moves them into the new layout.

---

**SaveMeta struct (new, serializable):**

```csharp
[Serializable]
public class SaveMeta
{
    public string saveName;
    public string slotId;
    public int    treeOrigin;   // TreeOrigin enum cast to int
    public string speciesName;
    public int    year;
    public int    month;
    public string saveTimestamp;
    public int    seasonsSinceRepot;
    public int    nodeCount;    // rough tree size indicator
}

public enum TreeOrigin { Seedling, Cutting, AirLayer }
```

---

**SaveManager changes:**

- `static string SavesRoot` — `persistentDataPath/saves/`
- `static void SaveSlot(string slotId, TreeSkeleton, LeafManager, SaveMeta meta)` — writes
  `save.json` + `meta.json` to `saves/<slotId>/`
- `static bool LoadSlot(string slotId, TreeSkeleton, LeafManager)` — reads from slot folder
- `static List<SaveMeta> ListAllSaves()` — scans `saves/*/meta.json`, returns sorted by
  `saveTimestamp` descending (most recent first)
- `static string NewSlotId()` — returns `DateTime.UtcNow.ToString("yyyyMMdd_HHmmss")`
- Quick-save (current Save button) writes to the **active slot** (last loaded or just saved);
  if no active slot exists, prompts for a name first
- `static string ActiveSlotId { get; private set; }` — persists to `PlayerPrefs`

---

**New `SaveData` fields:**

```csharp
public string saveName;
public int    treeOrigin;   // TreeOrigin cast to int
public string speciesName;
public string saveTimestamp;
```

`treeOrigin` is set when `SpeciesSelect` confirms (→ Seedling) or when `SeverAirLayer`
completes (→ AirLayer). Stored on `TreeSkeleton` as a durable field, serialized into
every save made from that tree.

---

**Load Menu UI (`LoadMenuOverlay`):**

- Fullscreen overlay, appears in place of `SpeciesSelectOverlay` when a save exists at launch,
  or from the Settings → Load button at any time
- Scrollable list of save cards; each card shows:
  - **Save name** (large)
  - **Origin badge** — small chip: "🌱 Seedling", "✂ Cutting", "🌿 Air Layer" (coloured)
  - **Species name**
  - **In-game date** (e.g. "April 2131")
  - **Real save time** (e.g. "3 days ago")
  - **Node count** as a rough size proxy ("small / medium / large")
  - Optional thumbnail if screenshot exists
- Buttons per card: **Load** | **Delete** (confirm prompt before delete)
- Footer button: **New Game** — skips load menu, goes to SpeciesSelect
- On launch: if any save exists → show Load Menu; otherwise → go straight to SpeciesSelect

---

**Save Name Prompt:**

When saving to a new slot (no active slot), show a small modal with a text field
(`UnityEngine.UIElements.TextField`) pre-filled with `"<Species> <Year>"`.
Confirm saves; Escape cancels (falls back to quick-overwrite of the last slot if one exists).

---

**Scope:**

- `SaveManager.cs` — slot system, meta read/write, migration
- `SaveData` / `SaveMeta` — new fields
- `TreeSkeleton.cs` — `treeOrigin` field, set at species confirm and sever
- `buttonClicker.cs` — Load Menu, Save Name Prompt, card list build, slot tracking
- `ButtonUI.uxml` — `LoadMenuOverlay`, `SaveNamePromptOverlay`, card template
- `GameManager.cs` — launch flow: check for saves → LoadMenu or SpeciesSelect
- `GameState.LoadMenu` — new state

</details>

---

<details>
<summary><strong>32. Air Layering to New Tree ✓</strong></summary>

Full spec in Backlog → Air Layering & Branch-to-New-Tree.

**Done:** `AirLayerData` gets `isUnwrapped`, `rootGrowSeasons`, `isSeverable`. `UpdateAirLayers`
tracks post-unwrap season count; sets `isSeverable` after `airLayerRootSeasonsToSever` seasons.
`SeverAirLayer`: wounds cut site, removes subtree, saves original to `bonsai_original.json`,
builds `SaveData` from severed subtree (air-layer node becomes root, depths recalculated, air
roots become normal roots), loads new tree via `LoadFromSaveData`, transitions to Idle.
UI: `AirLayerSeverBanner` (shown in Update when `HasSeverableLayer`), `AirLayerSeverOverlay`
confirm/cancel, `LoadOriginalButton` in pause menu (visible when backup exists).
`GameState.AirLayerSever` freezes time while prompt is open.

</details>

---

<details>
<summary><strong>34. Growth Season Taper</strong></summary>

**Goal:** Primary extension growth stops in early-to-mid summer, matching real tree biology. Most temperate trees set buds and shift resources from extension to radial growth and energy storage once day length shortens past their threshold. The cutoff is species-specific and should feel gradual rather than snapping off.

**Science basis:**
- Spring flush uses pre-formed bud tissue and stored energy — fast and explosive
- Growth stop triggered primarily by shortening photoperiod, not temperature
- After the taper: radial growth (thickening) continues, but `isGrowing` extension stops
- Lammas (second flush) exists in some species but is not modelled here

**Approximate stop windows by species:**

| Species | Slow starts | Fully stopped |
|---|---|---|
| Juniper | ~May 20 (day 140) | ~Jun 15 (day 166) |
| Japanese Maple | ~Jun 1 (day 152) | ~Jul 1 (day 182) |
| Trident Maple | ~Jun 10 (day 161) | ~Jul 5 (day 186) |
| Ficus | ~Aug 1 (day 213) | ~Sep 1 (day 244) |
| Bald Cypress | ~Jun 15 (day 166) | ~Jul 15 (day 196) |

**New fields on `TreeSpecies` ScriptableObject:**
- `int growthSlowDay` — day of year growth begins tapering (default 150)
- `int growthStopDay` — day of year growth multiplier reaches zero (default 180)

**Implementation in `TreeSkeleton.cs`:**
```csharp
float GrowthSeasonMult()
{
    int day = GameManager.dayOfYear; // needs exposing or computing from month/day
    if (day < species.growthSlowDay) return 1f;
    if (day >= species.growthStopDay) return 0f;
    return 1f - Mathf.InverseLerp(species.growthSlowDay, species.growthStopDay, day);
}
```
Multiply `GrowthSeasonMult()` into the per-frame growth delta. When the multiplier reaches zero, `isGrowing` stays false and no new children are spawned for the rest of the season. Growth resumes next spring as normal.

**Note:** The "keep tips extending all season via `subdivisionsLeft = 1`" change made earlier is superseded by this. The taper naturally handles late-started branches — a branch that sprouted in May gets less time than one that sprouted in March, so they land at naturally different lengths without special-casing.

**Scope:** `TreeSpecies.cs` (two new fields), `TreeSkeleton.cs` (`GrowthSeasonMult()`, multiply into growth delta), `GameManager.cs` (expose `dayOfYear` as int), all 16 species `.asset` files (set slow/stop days)

</details>

---

<details>
<summary><strong>Species — Visuals</strong></summary>

Procedural geometry + hand-authored bark textures that transition between age stages
using a **pixel-perfect noise reveal** — no opacity blending, just a hard per-texel cutoff
that gradually exposes more pixels from the new tier. Geometry variation is fully procedural.

---

#### 0. Bark Texture System

**Design:** Each species has N bark texture tiers (suggested 3–4: seedling, young, mature, old).
Adjacent pairs don't need to be seamlessly blendable — each pixel is always 100% one tier or
the other. The transition looks like bark texture naturally spreading across the surface.

**UV generation** — `TreeMeshBuilder.AddRing` already writes UVs. The convention needs
to be locked down so textures tile correctly:
- **U** = angle around the ring, 0→1 wrapping the full circumference
- **V** = `cumulativeHeight * vTilingScale` — a per-species tiling scale so thick old trunks
  and thin twigs both show an appropriate amount of texture repeat
- UV continuity across segments is already handled by sharing the tip ring with the next
  segment's base ring — no seams between segments along the length

```csharp
// On TreeSpecies:
public Texture2D[] barkTiers;          // [0]=seedling, [1]=young, [2]=mature, [3]=old
public float[]     barkTierAgeBreaks;  // age values where each tier begins, e.g. {0, 1, 4, 10}
public float       barkVTiling = 2f;   // how many times the texture repeats per world unit of length
```

**Pixel-perfect noise transition** — instead of `lerp(texA, texB, blend)`, the shader
uses a noise value per texel compared against a threshold. Each texel is binary: fully tier A
or fully tier B. As `_BlendFactor` advances 0→1, more and more texels flip to tier B.

Two noise options (inspector-selectable per species):

| Mode | Pattern | Best for |
|---|---|---|
| **Scatter** | Per-texel value noise (hash of UV * resolution) | Random salt-and-pepper pixel spread |
| **Cellular** | Voronoi / cellular noise | Patches spreading across bark like aging spots or peeling |

```hlsl
// Inputs:
//   _BarkTexA, _BarkTexB  — the two adjacent tier textures
//   _BlendFactor          — 0=fully A, 1=fully B
//   _NoiseScale           — world-space pixels per noise cell (match your pixel art texel size)
//   _NoiseMode            — 0=scatter, 1=cellular
//   vertex.color          — tint overlay (growth color, fungal, deadwood, seasonal)

// Snap UVs to pixel grid so noise aligns exactly with texels
float2 texelUV = floor(uv * _TexelRes) / _TexelRes;

// --- Scatter mode: each texel independently random ---
float scatter = frac(sin(dot(texelUV, float2(127.1, 311.7))) * 43758.5453);

// --- Cellular mode: Voronoi distance to nearest cell center ---
float2 cell  = floor(texelUV * _NoiseScale);
float  minD  = 1.0;
for (int dy = -1; dy <= 1; dy++)
for (int dx = -1; dx <= 1; dx++) {
    float2 neighbor = cell + float2(dx, dy);
    float2 point    = neighbor + frac(sin(neighbor * float2(127.1, 311.7)) * 43758.5);
    minD = min(minD, length(texelUV * _NoiseScale - point));
}
float cellular = minD;  // 0 = at cell center, ~1 = at cell edge

float noise = lerp(scatter, cellular, _NoiseMode);

// Hard threshold — no blending, pixel is either A or B
half4 col = noise < _BlendFactor ? SAMPLE_TEXTURE2D(_BarkTexB, ...) : SAMPLE_TEXTURE2D(_BarkTexA, ...);
col *= IN.color;
```

**Key property:** Because this is a hard cutoff, pixel art pixels are never semi-transparent
or color-blended. A pixel at 60% through the transition is the same color as at 0% — it just
hasn't flipped yet. Adjacent tiers don't need to share a design language; the transition reads
as bark naturally changing rather than two textures dissolving together.

**`_TexelRes`** should match the pixel art resolution of the texture (e.g. 64 for a 64×64 sheet).
Set it on the material or as a global shader property.

**Blend factor calculation** — `TreeMeshBuilder` determines which two tiers a node sits
between based on its `age` and the species `barkTierAgeBreaks` array, then sets
`_BlendFactor` on a `MaterialPropertyBlock` per node (same mechanism as the existing
fungal tint system):
```csharp
int   tierA     = TierForAge(node.age, species.barkTierAgeBreaks);
int   tierB     = Mathf.Min(tierA + 1, species.barkTiers.Length - 1);
float blend     = NormalizedBlendInTier(node.age, tierA, species.barkTierAgeBreaks);
mpb.SetTexture("_BarkTexA", species.barkTiers[tierA]);
mpb.SetTexture("_BarkTexB", species.barkTiers[tierB]);
mpb.SetFloat("_BlendFactor", blend);
renderer.SetPropertyBlock(mpb);
```

**Vertex color role shifts** — with textures providing the base bark color, vertex color
becomes a multiplicative tint layer only:
- Young growth: slight green-yellow tint (blends out as age increases)
- Spring flush: species-specific tint overlay for the first days of the season
- Deadwood: desaturate/tint toward the species deadwood color
- Fungal infection: existing sickly yellow-green tint (already per-node via MPB)
- Seasonal bark tint: very subtle warm/cool shift

**Art workflow (for you):**
1. Paint each bark tier as a seamless square pixel art texture (suggested 64×64 or 128×128)
2. Adjacent tiers do **not** need to share colors or pattern — the noise transition handles it
3. Import to Unity with Point (no filter) sampling to preserve hard pixels
4. Assign to `barkTiers[]` on the species `.asset`
5. Tune `barkTierAgeBreaks`, `barkVTiling`, `_NoiseScale`, and `_NoiseMode` in-engine

**Noise mode feel:**
- Scatter at small `_TexelRes` = static/grain, good for fine bark texture like maple
- Cellular at larger scale = patches spreading organically, good for chunky juniper or pine bark
- You can also bake a custom noise mask texture for species that need a very specific look

---

---

#### 1. Geometry Variation

**Ring segment count** — controls how round vs. angular the wood looks. Set per species on `TreeSpecies`:
```csharp
public int ringSegments = 7;   // default; override per species
```
- Japanese Maple: **7–8** (rounder, smoother young wood)
- Juniper: **5–6** (angular, faceted, gnarly character)
- `TreeMeshBuilder` reads `skeleton.RingSegments` instead of the current hardcoded value

**Bark vertex jitter** — older/thicker nodes get slight per-vertex radial noise to break up the perfect cylinder. Controlled by two species fields:
```csharp
public float barkJitterRadius   = 0f;    // max world-unit displacement per vertex
public float barkJitterAgeMin   = 2f;    // minimum node age before jitter kicks in
```
Applied inside `AddRing` after the base position is computed. Young growth stays smooth; old wood gets organically rough.

**Junction swell** — at fork points, inflate the base ring of each child slightly so the branch origin looks like it's bulging out of the parent rather than snapping to a cylinder edge:
```csharp
public float junctionSwellFactor = 1.15f;   // multiplier on base ring radius at forks
```
Applied in `ProcessNode` when `parentBaseRingIndex != -1` and the node has siblings.

**Bark ridge fins** — a second geometry pass on mature segments (radius above a threshold) adds thin longitudinal fins running along the segment length. For species like elm or zelkova with deeply ridged bark:
```csharp
public int   barkRidgeCount     = 0;     // 0 = disabled
public float barkRidgeHeight    = 0.02f; // world units above surface
public float barkRidgeAgeMin    = 3f;
```

---

#### 2. Color & Shader Variation

All color is vertex color — no textures, no material swaps. New fields on `TreeSpecies`:

**Spring flush tint** — new growth color in the first weeks of the growing season before it hardens to bark:
```csharp
public Color springFlushColor = new Color(0.55f, 0.72f, 0.35f);  // default yellow-green
// Maple: bright red-pink flush
// Juniper: blue-green, barely distinguishable from mature
```
`GrowthColor()` in `TreeMeshBuilder` already lerps by age — this replaces the hardcoded young-growth color with the species value.

**Mature bark color** — replaces the current single hardcoded bark brown:
```csharp
public Color matureBarkColor  = new Color(0.32f, 0.22f, 0.14f);
public Color oldBarkColor     = new Color(0.22f, 0.15f, 0.10f);  // darker for very old wood
public float oldBarkAgeThreshold = 8f;
```

**Seasonal tints** — blended in by `GameManager.LeafHue` equivalent for bark (separate from leaf color):
```csharp
public Color autumnBarkTint   = Color.white;   // most species: no change
// Maple autumn: slight warm orange cast on young bark
```

**Deadwood color** — currently a single grey-brown everywhere:
```csharp
public Color deadwoodColor    = new Color(0.65f, 0.60f, 0.55f);
// Juniper jin: bleached silver-white
// Maple: darker charcoal grey
```

---

#### 3. Bud System Visuals

Buds are currently just a prefab placed at the tip position with no internal structure. This replaces them with procedurally animated geometry.

**Bud geometry** — a small teardrop/egg shape built from `AddRing`-style stacked rings, tapered at the tip. Size driven by `terminalRadius * budSizeMultiplier`. Scales up slightly over autumn/winter as the bud fattens:
```csharp
public float budSizeMultiplier  = 2.5f;
public Color budColor           = new Color(0.28f, 0.20f, 0.12f);   // dark brown
public Color budScaleColor      = new Color(0.35f, 0.28f, 0.16f);   // scale lines
```

**Bud break animation** — each spring, over the first 2–3 in-game days, the bud mesh transitions:
1. Bud scales separate and peel back (radial displacement of outer ring vertices)
2. Inner leaf/flower geometry emerges from the center (small rolled cone expanding)
3. Once fully open, the bud mesh is destroyed and normal leaf geometry takes over

Species control:
```csharp
public BudBreakType budBreakType = BudBreakType.Leaf;
// Leaf: standard green emerging foliage
// Flower: small clustered geometry before leaf flush (cherry, apple)
// NeedleCluster: radiating thin fins (pine, juniper)
```

**Opposite vs. alternate** — maple's opposite buds sit in facing pairs at each junction; juniper's alternate buds spiral. The `BudType` enum already exists; bud placement just needs to respect spacing and orientation correctly in `SetBuds()`.

**Flower buds (future hook)** — `BudBreakType.Flower` generates a small tight cluster geometry per node on designated flowering branches. Full flower system (petal geometry, pollination, fruit) deferred; this just reserves the hook.

---

#### Scope

- `TreeSpecies.cs` — all new fields above (ring segments, jitter, swell, ridges, colors, bark tier textures/age breaks/tiling)
- `TreeMeshBuilder.cs` — `ringSegments` from species; jitter in `AddRing`; junction swell; ridge fin pass; bark tier blend via `MaterialPropertyBlock`; vertex color becomes tint-only; UV tiling scale
- `Custom/BarkBlend` shader — new shader replacing `Custom/BarkVertexColor`; dual texture sample with pixel-perfect noise threshold + vertex color tint multiply; scatter and cellular noise modes
- `TreeSkeleton.cs` — expose `RingSegments` property; pass species refs to mesh builder
- `BudManager` or extended `TreeSkeleton.cs` — bud break animation coroutine; procedural bud geometry builder
- `GameManager.cs` — seasonal bark tint pass (analogous to `LeafHue`)
- All 16 species `.asset` files updated with new fields
- Art assets (your work): seamless bark texture sets per species, 3–4 tiers each

</details>

---

## Backlog

Future features not yet scheduled. Expand to read the spec.

---

<details>
<summary><strong>Auto-Style Training Data Recorder</strong> — capture player care actions as ML training data</summary>

### Goal
Record every player styling/care action together with a snapshot of the tree state, building a dataset to later train (or fine-tune) the auto-style system on real player technique instead of hand-written heuristics.

### What gets recorded
One JSONL line per action, written to `Application.persistentDataPath/training/<sessionId>.jsonl`:

- **Context:** date (year/month/day), species, style asset name, tree age, treeHeight, node count, match %, moisture/nutrient/health aggregates
- **Tree snapshot (compact):** per-node feature rows for the affected region (or full tree below a node-count cap): `id, depth, parentId, heightNorm, azimuthDeg, radius, length, vigor, health, isTerminal, hasWire, refinementLevel`
- **Action:** type (`Trim | Pinch | Wire | Unwire | Paste | Defoliate | Fertilize | Water | Repot | Graft | RockPlace`), target nodeId, parameters (wire target direction as lean+azimuth, repot preset/size…)
- **Outcome hooks (later):** optional season-end deltas (match %, health) appended for reward labeling

### Implementation sketch
- `TrainingRecorder.cs` singleton — subscribes to the same player-action entry points the tools already call (`TrimNode`, `WireNode`, `PinchNode`, `ApplyPaste`, `DefoliateNode/All`, `Repot`…), but ONLY when the action originates from player input (flag passed from `TreeInteraction` / `buttonClicker`, so AutoStyler's own actions are excluded or labeled `source=auto`)
- Debug-tab toggle `Record Training Data` (off by default), session file rotated per play session
- Keep it dumb and append-only — analysis/training happens outside the game

### Fleet telemetry (phase 2)
Eventually this should record **all players'** sessions and send them back for training — with eyes open about size:

- **Consent first** — opt-in toggle at first launch ("share anonymous care data to improve the auto-stylist"), anonymous session GUID only, no PII.
- **Size budget** — raw per-action JSONL gets big fast. Mitigations, in order: gzip the JSONL (~10× on this kind of data), cap per session (e.g. 2 MB compressed, then sample 1-in-N actions), record full node snapshots only on the FIRST action per season and deltas after, drop Water/Fertilize spam (keep counts per season instead of rows).
- **Transport** — simplest viable: batched HTTPS POST of the gzipped session file to a dumb collector (S3 presigned URL or a tiny Cloudflare Worker/R2 bucket) on session end + retry on next launch if offline. Interim/zero-infra option: "Export training data" button that zips `training/` so playtesters can send it manually.
- **Server side is out of scope for the game** — collector just stores blobs; dataset assembly/cleaning happens offline.

### Why backlog
Needs the AutoStyler behaviors to stabilize first so recorded context features match what a future model would consume.

</details>

---

<details>
<summary><strong>Calendar System — Real Month Lengths + Scheduling</strong> ✓ ALL PARTS DONE</summary>

### Goal
Replace the fixed 28-day month with real calendar month lengths and add a monthly calendar panel where the player can schedule recurring care tasks (watering, fertilizing) with optional repeat modes and seasonal scoping.

**Key design decisions (2026-04-18):**
- Opening the calendar **pauses** in-game time while the overlay is open.
- Scheduled events **auto-apply** — no player action needed when they fire (same as auto-water).
- Repeat cadence options: **every N days** or **every N weeks** (not calendar-month-based).
- Repeating events are **season-scoped** (spring/summer/fall/winter) — a "water every Tuesday in Spring" rule is silent during other seasons.
- Accessing a specific day drills into a day-detail view; from there the player can add a new event for that day.
- Event detail screen: type → amount/type selector → optional Repeating checkbox → if repeating, choose every N days or every N weeks.

---

### Part 1 — Real Month Lengths

**Current:** every month = 28 days; `dayOfYear = (month-1)×28 + currentDay`.

**Change:** use biological month lengths. The in-game year starts in January of a fictional year (e.g. 2123), so leap-year logic is optional but can be included for accuracy.

```csharp
// On GameManager
static readonly int[] DaysInMonth = { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };

static bool IsLeapYear(int year) =>
    (year % 4 == 0) && (year % 100 != 0 || year % 400 == 0);

static int DaysInCurrentMonth(int month, int year) =>
    (month == 2 && IsLeapYear(year)) ? 29 : DaysInMonth[month - 1];
```

`dayOfYear` becomes a computed property that sums `DaysInMonth[0..month-2] + dayOfMonth`.

**What changes:**
- `CalculateTime()` — day rollover uses `DaysInCurrentMonth(month, year)` instead of `28`.
- `SetMonthText()` — month boundary fires when `dayOfMonth > DaysInCurrentMonth(...)`.
- `dayOfYear` property — recomputed from real cumulative day counts.
- `GrowthSeasonMult()` in `TreeSkeleton` — uses same `dayOfYear`; no change needed there since the property value is correct.
- Winter skip (`month == 11 → month = 2, year++`) — unchanged; just skip November + December as before.
- `OnMonthChanged` — unchanged.

**Migration:** existing saves store `month` (1–12) and the accumulated `hoursElapsed`. On load the `dayOfMonth` is inferred from `hoursElapsed % (DaysInCurrentMonth(month, year) * 24)`. No save format change required.

---

### Part 2 — Scheduled Event Data Model

```csharp
public enum ScheduledEventType { Water, Fertilize }

public enum RepeatMode { Once, EveryNDays, EveryNWeeks }

[Serializable]
public class ScheduledEvent
{
    public string             id;               // GUID, generated at creation
    public ScheduledEventType type;
    public int                month;            // 1–12; anchor month for Once events
    public int                day;              // 1–31; anchor day for Once / interval origin
    public RepeatMode         repeat;
    public int                repeatInterval;   // N for EveryNDays / EveryNWeeks
    public Season             season;           // Spring/Summer/Autumn/Winter; repeat events only fire in this season
    public TimeOfDay          timeOfDay;        // Morning=7, Midday=12, Night=21
    public bool               enabled;
}

public enum TimeOfDay { Morning, Midday, Night }

// Season enum (maps to GameManager month ranges):
//   Spring = Mar–May (3–5), Summer = Jun–Aug (6–8),
//   Autumn = Sep–Oct (9–10), Winter = Nov–Feb (11–2)
public enum Season { Spring, Summer, Autumn, Winter, AllYear }
```

`GameManager` holds `List<ScheduledEvent> schedule` (serialized into `SaveData`). At the end of each real day tick (when `dayOfMonth` increments), `CheckScheduledEvents()` fires:

```csharp
void CheckScheduledEvents()
{
    foreach (var ev in schedule)
    {
        if (!ev.enabled) continue;
        if (!EventFiresToday(ev, month, dayOfMonth, year)) continue;
        switch (ev.type)
        {
            case ScheduledEventType.Water:      skeleton?.Water(); break;
            case ScheduledEventType.Fertilize:  skeleton?.Fertilize(); break;
        }
    }
}

bool EventFiresToday(ScheduledEvent ev, int m, int d, int y)
{
    // Season gate — repeating events only fire in their target season
    if (ev.repeat != RepeatMode.Once && ev.season != Season.AllYear && !IsInSeason(ev.season, m))
        return false;

    switch (ev.repeat)
    {
        case RepeatMode.Once:        return ev.month == m && ev.day == d;
        case RepeatMode.EveryNDays:  return dayOfYear % ev.repeatInterval == ev.day % ev.repeatInterval;
        case RepeatMode.EveryNWeeks: return dayOfYear % (ev.repeatInterval * 7) == ev.day % (ev.repeatInterval * 7);
        default: return false;
    }
}

bool IsInSeason(Season s, int month) => s switch
{
    Season.Spring => month >= 3 && month <= 5,
    Season.Summer => month >= 6 && month <= 8,
    Season.Autumn => month >= 9 && month <= 10,
    Season.Winter => month >= 11 || month <= 2,
    _             => true,
};
```

Scheduled actions use the same `Water()` / `Fertilize()` methods as manual button presses — no special-casing, health guards, and winter blocks apply normally. The Water button flash / Fertilize button flash will still fire, giving visible feedback that the schedule fired.

---

### Part 3 — Calendar Panel UI

**Access:** clicking the date label (currently shows e.g. "April 1, 2123 09:02") opens the calendar. Alternatively, a small calendar icon next to the date.

**Layout:**

```
┌────────────────────────────────────┐
│  ←  April 2123  →                  │
│  Mo Tu We Th Fr Sa Su              │
│                  1  2  3           │
│   4  5  6  7  8  9 10             │
│  11 12 [13]14 15 16 17            │  ← today highlighted
│  18 19  20 21 22 23 24            │
│  25 26  27 28 29 30               │
│                                    │
│  ● Scheduled:  [+ Add]             │
│  💧 Water every 2 days             │
│  🌿 Fertilize monthly (1st)        │
│  ── Seasonal Templates ──          │
│  [Spring] [Summer] [Autumn]        │
└────────────────────────────────────┘
```

**Day cell:** 28–32 px square. Current day gets a gold highlight. Days with scheduled events show small coloured dots (blue = water, green = fertilize). Past days are dimmed.

**Month navigation:** `←` / `→` buttons step through months. Does not need to expose future months further than 12 months ahead.

**Day-detail drill-down:**
Clicking a day cell opens a **day view** listing events already scheduled for that day and a `[+ Schedule Watering]` / `[+ Schedule Fertilizer]` button.

**Add event flow (inside day view):**
1. Click `[+ Schedule Fertilizer]` (or Watering) → opens the **event detail screen**.
2. Select amount (slider or chips: Light / Medium / Heavy) and type (Balanced / High-N / High-P / etc.).
3. **Time of day** chips: Morning / Midday / Night. Controls which in-game hour the event fires (e.g. Morning ≈ hour 7, Midday ≈ hour 12, Night ≈ hour 21). Some fertilizers are best applied at night when stomata are more open and heat stress is low.
4. **Repeating** checkbox at the bottom. If unchecked → Once event on this day.
5. If repeating:
   - Season selector: Spring / Summer / Autumn / Winter / All Year.
   - Cadence: **Every N days** or **Every N weeks** (toggle); number spinner for N (default 2 days / 1 week).
6. Confirm → adds `ScheduledEvent` to `GameManager.schedule`; dots appear on the calendar.

**Delete:** each event row has a ✕ button on the right.

**Toggle:** each event row has an enable/disable checkbox so the player can pause a schedule without deleting it.

---

### Part 4 — Seasonal Templates

Pre-built schedules that can be applied in one click. Applying a template does **not** remove manually-added events — it merges, deduplicating by type+day+repeat.

| Template | Water | Fertilize |
|---|---|---|
| **Spring** (Mar–May) | Every 2 days | Monthly, 1st of month |
| **Summer** (Jun–Aug) | Every 1 day (heat/growth peak) | Every 4 weeks |
| **Autumn** (Sep–Oct) | Every 3 days | None (harden before winter) |
| **Winter** (Nov–Feb) | Every 5 days | None (dormant — Fertilize blocked anyway) |

Templates target the species' active season — a Ficus "Summer" template runs longer than a Juniper one. Implementation: templates are static `List<ScheduledEvent>` definitions; `ApplyTemplate(TemplateType)` merges them into `schedule`.

---

### Scope

| File | Change |
|---|---|
| `GameManager.cs` | `DaysInMonth[]`, `DaysInCurrentMonth()`, `IsLeapYear()`, `dayOfMonth` field, real-day rollover in `CalculateTime()`, `CheckScheduledEvents()`, `schedule` list, `AddScheduledEvent()` / `RemoveScheduledEvent()`, seasonal template builders |
| `SaveData.cs` | Add `scheduledEvents` list |
| `buttonClicker.cs` | Calendar panel open/close, month navigation, day-cell grid build, event list render, add/delete/toggle handlers, template buttons |
| `ButtonUI.uxml` | `CalendarOverlay` — date header, 7-col day grid, event list, add-event popup, template buttons; date label made clickable |

**Not in scope:** notification/reminder outside the game, scheduling trim/wire tasks (these require player decision-making, not automation), multi-year advance scheduling.

---

### Part 5 — Play Modes Tab

A second tab in the calendar overlay. Lets the player define named automation profiles that control time speed, auto-care, idle behaviour, and camera orbit.

---

#### Play Mode Data Model

```csharp
public enum SpeedRuleTrigger
{
    Month, Season,
    MoistureBelow, HealthBelow, NutrientBelow, FungalLoadAbove, WeedCountAbove,
    WireSetGold, TreeInDanger,
}

[Serializable]
public class SpeedRule
{
    public bool              enabled;
    public SpeedRuleTrigger  trigger;
    public float             triggerParam;   // threshold value for numeric triggers; month int for Month
    public SpeedMode         targetSpeed;
    // Idle resume — either threshold alone re-arms the rule
    public bool              idleResumeEnabled;
    public float             idleResumeRealSeconds;   // 0 = disabled
    public float             idleResumeInGameDays;    // 0 = disabled
}

[Serializable]
public class PlayMode
{
    public string         name;
    public bool           isBuiltIn;          // built-ins cannot be deleted
    public SpeedMode      defaultSpeed;
    public bool           autoWater;
    public bool           autoFertilize;
    public bool           idleOrbit;
    public float          idleOrbitDelaySecs; // real seconds before orbit starts
    public List<SpeedRule> rules;
}
```

**Built-in presets (created at first launch if missing from PlayerPrefs):**

| Mode | Default | Auto-Water | Auto-Fert | Orbit |
|---|---|---|---|---|
| Screensaver | Fast | ✓ | ✓ | ✓ (30 s) |
| Active Play | Medium | ✗ | ✗ | ✗ |
| Hands-Off | Fast | ✓ | ✓ | ✗ |
| Focused | Slow | ✗ | ✗ | ✗ |

Built-in default rules:
- **Screensaver:** Month=Jan → Slowest; TreeInDanger → Slowest (idle resume 20 s real); MoistureBelow 0.3 → Slowest (idle resume 20 s real)
- **Active Play:** Month=Jan → Slow; WireSetGold → Slow (idle resume 5 in-game days); Season=Spring → Slow
- **Hands-Off:** TreeInDanger → Slowest (idle resume 60 s real)
- **Focused:** no default rules

Player can edit built-in rules and add their own. Built-ins cannot be deleted but can be reset.

---

#### Rule Evaluation

Runs every frame in `PlayModeManager.Update()`:
1. Collect all enabled rules in active mode whose trigger is currently true.
2. Take the **minimum (slowest)** `targetSpeed` among them.
3. If no rules active → use `mode.defaultSpeed`.
4. Set `GameManager.TIMESCALE` to the resolved speed value.

**Idle tracking:**
- `float lastInputRealTime` and `float lastInputInGameDay` reset on any mouse/keyboard event.
- Each frame, for every rule with `idleResumeEnabled`: if `Time.unscaledTime - lastInputRealTime > idleResumeRealSeconds` OR `inGameDay - lastInputInGameDay > idleResumeInGameDays`, mark that rule as **suppressed** until its trigger condition re-enters.
- "Suppressed" rules don't count toward the minimum speed calculation until the trigger exits and re-enters.

**Auto-care:** `PlayModeManager` sets `TreeSkeleton.autoWater` and calls `skeleton.AutoFertilize()` each season based on `activeMode.autoFertilize`.

---

#### Idle Camera Orbit

When `activeMode.idleOrbit == true` and `Time.unscaledTime - lastInputRealTime > idleOrbitDelaySecs`:
- Save current camera state (`savedOrbitYaw`, `savedPitch`, `savedDistance`, `savedTarget`).
- Begin slow yaw increment each frame (`orbitYawSpeed`, default 4°/s real time).
- Elevation drifts ±5° on a slow sine wave.
- Any input: stop orbit immediately, restore saved camera state in one frame.

Implemented in `CameraOrbit.cs` — new `IdleOrbitActive` bool property, driven by `PlayModeManager`.

---

#### UI — Modes Tab

```
[ Schedule | Modes | Speed ]   ← tab strip at top of calendar

  Active Mode: [Screensaver ▾]        [Reset to defaults]

  Default speed:  ● Slow  ● Med  ● Fast
  Auto-water:     [✓]    Auto-fertilize: [✓]
  Idle orbit:     [✓]  after  [30] real seconds

  ── Rules ──────────────────────────────────
  [✓] January          → Slowest   [idle: 20s]  [✕]
  [✓] Tree in Danger   → Slowest   [idle: 20s]  [✕]
  [✓] Moisture < 30%   → Slowest   [idle: 20s]  [✕]
  [+ Add Rule]

  ── Modes ───────────────────────────────────
  [Screensaver] [Active Play] [Hands-Off] [Focused] [+ New]
```

Add Rule flow: trigger picker → (if numeric: threshold slider) → speed picker → idle resume toggle → if enabled: real-seconds + in-game-days fields.

---

### Part 6 — Speed Config Tab

A third tab in the calendar overlay. Lets the player set the actual timescale ratios for Slow / Medium / Fast.

**Current defaults (hardcoded → become mutable):**
- Slow = 0.5 game-hrs/real-sec ≈ 1 in-game day per 48 real seconds
- Medium = 10 game-hrs/real-sec ≈ 1 in-game day per 2.4 real seconds
- Fast = 200 game-hrs/real-sec ≈ 1 in-game day per 7.2 real seconds

Each speed gets a slider + a live human-readable label: `"1 in-game day = X"` where X is formatted as seconds or minutes depending on the value.

```csharp
// GameManager — replace const with mutable statics, persisted to PlayerPrefs
public static float TIMESCALE_SLOW = 0.5f;    // range 0.02 – 5
public static float TIMESCALE_MED  = 10f;     // range 1 – 50
public static float TIMESCALE_FAST = 200f;    // range 20 – 500
```

Sliders enforce min/max so Slow < Med < Fast — each slider's max is capped at the next tier's current value minus a small gap.

Human-readable conversion:
```csharp
static string FormatDayDuration(float timescale)
{
    float realSecsPerGameDay = 24f / timescale;   // 24 game-hours / (game-hrs per real-sec)
    if (realSecsPerGameDay < 60f) return $"{realSecsPerGameDay:F0} real seconds";
    return $"{realSecsPerGameDay / 60f:F1} real minutes";
}
```

Values saved to `PlayerPrefs` on change (`"timescale_slow"`, `"timescale_med"`, `"timescale_fast"`), loaded on game start. `SetSpeedMode()` reads the mutable statics instead of the old consts.

**UI — Speed Tab:**
```
[ Schedule | Modes | Speed ]

  ── Time Speed Ratios ──────────────────────

  Slow    [━━━●━━━━━━━━━━━━━━━]   0.5×   1 in-game day = 48 real seconds
  Medium  [━━━━━━━━●━━━━━━━━━━]  10.0×   1 in-game day = 2.4 real seconds
  Fast    [━━━━━━━━━━━━━━━━━●━] 200.0×   1 in-game day = 7.2 real seconds

  [Reset to defaults]
```

---

### Updated Scope

| File | Change |
|---|---|
| `GameManager.cs` | `DaysInMonth[]`, `DaysInCurrentMonth()`, `IsLeapYear()`, `dayOfMonth`, real-day rollover, `CheckScheduledEvents()`, `schedule` list, seasonal templates; `TIMESCALE_SLOW/MED/FAST` → mutable statics with PlayerPrefs load/save; `SetSpeedMode()` reads mutable values |
| `PlayModeManager.cs` | New singleton — active mode, rule evaluation loop, idle tracking, auto-care dispatch, `IdleOrbitActive` property |
| `CameraOrbit.cs` | `IdleOrbitActive` consumption — yaw drift, elevation sine, saved-state restore on input |
| `SaveData.cs` | Add `scheduledEvents` list; `playModes` list; `activeModeName` |
| `buttonClicker.cs` | Calendar open/close + tab switching; Schedule tab (month nav, day grid, event list, add/delete/toggle, templates); Modes tab (mode chips, rule list, add rule flow, orbit settings); Speed tab (three sliders, live labels, reset) |
| `ButtonUI.uxml` | `CalendarOverlay` with tab strip; `ScheduleTab`, `ModesTab`, `SpeedTab` sub-panels; mode/rule row templates; speed sliders |

**Not in scope:** notification/reminder outside the game, scheduling trim/wire tasks, multi-year advance scheduling.

</details>

---

<details>
<summary><strong>Root Containment Fix</strong> ✓ done</summary>

**Issue:** Ground roots frequently escape the pot boundary — growing out the sides, deep through the bottom, and occasionally far above soil. Out-the-top is acceptable (surface roots are realistic); lateral and downward escapes are not.

**Existing system:** `rootAreaTransform` six-face box deflection + `DeflectFromRootAreaWalls` in `ContinuationDirection`. The deflection is applied but roots still escape.

**Proposed fixes:**
1. **Hard clamp at spawn:** In `SpawnChildren` for root nodes, if `tipPosition` is already outside the boundary box (minus a small margin for top face), skip spawning — don't add the node at all
2. **Stronger wall deflection:** Increase the deflection blend weight for side/bottom faces vs the top face; top face gets a weaker deflection (allow slight emergence)
3. **Terminal clamp:** If an existing root terminal is outside the boundary, mark it `isTrimmed` so it stops growing and doesn't seed new children

**Top-face exception:** Roots within a small vertical band above the soil plane (`0 to +topEmergenceMargin`) are left alone — surface radial roots are realistic and look good.

**Scope:** `TreeSkeleton.cs` (`SpawnChildren`, `DeflectFromRootAreaWalls`, possibly `StartNewGrowingSeason`)

</details>

---

<details>
<summary><strong>Autosave System</strong> ✓ done (toast pending)</summary>

**Current bug:** `TreeSkeleton.cs` calls `SaveManager.Save()` at the end of every growing season (TimeGo → bud set). `Save()` silently returns false when no `ActiveSlotId` is set — so unsaved new games never get written, and no feedback is given to the player.

**Goals:**
- Autosave fires at meaningful moments without interrupting the player
- Unsaved games (no slot yet) get an autosave slot created automatically with a sensible default name
- Player always knows the save happened (brief non-blocking toast)

**Autosave triggers:**
- End of every growing season (existing hook in `TreeSkeleton.cs`)
- After a repot
- After an air layer is severed (already calls `SaveOriginal`, but the new tree should also get an autosave slot immediately)
- After confirming Ishitsuki rock orientation

**No-slot behaviour:**
- If `ActiveSlotId == null`, auto-create a slot with `slotId = NewSlotId()` and `saveName = "{SpeciesName} {Year} (autosave)"`
- Set it as the active slot — the player can rename it later from the load menu (future: editable names)
- This means the first season end always produces a save, even on a brand-new game

**Toast notification:**
- A `SaveToastLabel` in the HUD (small, bottom-centre, above the selection bar area)
- Fades in, holds 2 s, fades out — driven by a coroutine in `buttonClicker.cs`
- Text: `"Autosaved"` for automatic saves, `"Saved"` for manual saves (reuse same toast for both)
- Replaces the current `SaveStatusLabel` in the pause menu (which the player rarely sees)

**Screenshot on autosave:**
- Same `TakeScreenshotForSlot` coroutine as manual saves — fires after autosave writes

**Scope:** `TreeSkeleton.cs` (autosave call → pass to buttonClicker event or call SaveManager directly with slot-creation fallback), `SaveManager.cs` (`AutoSave()` helper that creates a slot if needed), `buttonClicker.cs` (toast coroutine, wire up autosave feedback), `ButtonUI.uxml` (SaveToastLabel)

</details>

---

<details>
<summary><strong>Rock Placement — Lock UI + Cancel + Camera-Relative Controls</strong> ✓ done</summary>

**Done:** All tool buttons dim (`opacity 0.25` + `PickingMode.Ignore`) during `RockPlace`/`TreeOrient`. Cancel button calls `skeleton.RestorePrePlacementSnapshot()`. `RockPlacer.cs` uses `Vector3.ProjectOnPlane(cam.transform.right/forward, Vector3.up)` for camera-relative tree translation.

</details>

---

<details>
<summary><strong>Repot Root Raking Mini-Game</strong> ✓ done</summary>

**Trigger:** When the player repots a pot-bound tree (roots have hit the boundary wall for 2+ seasons), a rake mini-game fires before the new soil is applied.

**Design rationale:**
- Root-bound roots are tangled tightly in old soil — real repotting requires teasing them apart with a root rake before you can cut and arrange them
- The player gets to choose which roots to keep and which to prune, making repotting a deliberate act instead of a single button press
- Long roots are **valuable**: a long root creates surface roots and gives the tree room to develop nebari. The game should reward keeping at least one long root. The new pot generates one extra-long root on the side the player kept their longest root.

**Flow:**
1. Repot button pressed while pot-bound → enter `GameState.RootRake` (new state)
2. Camera tilts to a top-down view of the root ball lifted out of the pot
3. Player moves the mouse over roots to highlight them. **Left-click-drag** rakes horizontally across the root ball — each stroke un-mats nearby root nodes (visual only: they spread outward slightly)
4. After raking, unhealthy / excess roots are highlighted in red. Player left-clicks individual root tips to **prune** them
5. A root-count indicator shows current vs. target range. Target = approximately the count `PreGrowRootsToSoil` generates fresh (e.g., 6–10 root strands)
6. Once within target range, **Confirm Repot** becomes enabled; pressing it:
   - Calls `PotSoil.Repot()` as normal
   - Discards all current root nodes
   - Re-generates fresh roots via `PreGrowRootsToSoil` — same count as confirmed, preserving the cheat
   - If the player kept any root longer than 1.5× the average, flags `hasLongRoot = true` on the skeleton → next call to `PreGrowRootsToSoil` spawns one extra-long root cable on that side

**Root count cheat rationale:**
The player doesn't notice because the new roots look similar in count and arrangement to what they kept. The important feedback loop is the *decision* to prune carefully, not the exact topology.

**Scope:** `GameManager.cs` (new `RootRake` state), `buttonClicker.cs` (trigger + UI), `TreeSkeleton.cs` (`hasLongRoot` flag, hook into `PreGrowRootsToSoil`), `TreeInteraction.cs` (rake brush + pruning in RootRake mode), `ButtonUI.uxml` (root count indicator + Confirm Repot button)

</details>

---

<details>
<summary><strong>Pot and Rock Size Selection</strong> (Pot ✓ done; Rock size pending)</summary>

**Goal:** Give the player meaningful choices when repotting or placing a rock, with different sizes creating different constraints on root growth.

**Pots:**
- Pot sizes: XS / S / M / L / XL, plus a slab option (very shallow, very wide)
- Size affects `rootAreaTransform` scale — smaller pot → roots hit boundary sooner → pot-bound faster → repotting needed more frequently
- Shape variants: round, oval, rectangle, cascade (tall) — affects root area aspect ratio
- Shallow pots (slab) encourage wide lateral roots; deep pots allow longer downward roots
- UI: a pot selection panel in the Repot flow; each pot shown as a silhouette with size label
- Pot dimensions stored on `PotSoil` as `potWidth`, `potDepth`, `potHeight`; `rootAreaTransform` resized on repot to match

**Rocks (Ishitsuki):**
- Rock sizes: S / M / L / XL; taller rocks require more root cable length to reach soil → more seasons before the tree stabilises
- Rock shape presets affect how `PreGrowRootsToSoil` drapes cables (steep cliff vs. gentle slope vs. plateau)
- UI: rock selection panel before `GameState.RockPlace`; shown as silhouettes

**Root issues by size:**
| Container | Root effect |
|-----------|-------------|
| XS pot | Pot-bound in 1–2 years; strong nebari pressure; risk of root crush damage |
| Slab | Wide spreading roots; minimal downward growth; surface root aesthetics strong |
| Large rock | Very long cables; slower anchor; exposed root risk if not enough canopy to support them |
| Small rock | Short cables; quick anchor; less dramatic nebari; easier for beginners |

**Scope:** `PotSoil.cs` (`potWidth`/`potDepth`/`potHeight`, `rootAreaTransform` resize), `TreeSkeleton.cs` (Ishitsuki cable length scale by rock size), `buttonClicker.cs` + `ButtonUI.uxml` (pot/rock selection panels), `SaveData`/`SaveManager.cs` (serialize selected pot/rock)

</details>

---

<details>
<summary><strong>Species Visuals</strong> → scheduled as item 25</summary>

*(See item 25 above for full spec.)*

</details>

---

<details>
<summary><strong>Sibling Branch Fusion (Graft Bridging)</strong></summary>

**Goal:** When two sibling branches (same parent) grow close enough to touch, they gradually callus together and eventually fuse into one wireable unit — mimicking natural inosculation and enabling Clump Style bonsai.

**Detection (cheap — runs once per spring):**
- For each parent node, check all pairs of its direct children
- If `distance(tipA, tipB) < radiusA + radiusB`, start a `FusionBond` (stored on `TreeSkeleton`)
- `FusionBond`: `{ nodeIdA, nodeIdB, float progress 0→1, bool isComplete, bool wireRestarted }`
- Progress increments each spring by a rate scaled by `SeasonalGrowthRate`; reaches 1.0 after ~3–5 growing seasons of contact

**Visual bridge (ITNTCE):**
- In `TreeMeshBuilder`, for each bond with `progress > 0`, extrude a short flattened connecting cylinder between the two nearest surface points
- Cross-section scales from 0→full radius as `progress` goes 0→1
- Uses the same bark vertex color as surrounding wood

**Fusion completion (`progress >= 1.0`):**
- `isComplete = true`; the two nodes are treated as a single wireable unit
- `TreeInteraction` click on either node selects both; wire direction applied to both simultaneously
- Wire removal on one removes both; placing wire restarts `progress = 0` (bridge "breaks" then re-fuses with `wireRestarted = true` for faster re-fusion, ~1 season)

**Wiring implications:**
- Wire data (`wireTargetDirection`, `wireSetProgress`, etc.) duplicated across both nodes in the pair
- `SeverAirLayer` and `TrimNode` on one node of a complete bond also removes the bond

**Serialization:** `FusionBond` list added to `SaveData` and `TreeSkeleton`

**Future:** Non-sibling fusion (cross-tree, clump style) deferred to a separate backlog item after this is stable.

**Scope:** `TreeNode.cs` (bond ref), `TreeSkeleton.cs` (bond detection + progress), `TreeMeshBuilder.cs` (bridge geometry), `TreeInteraction.cs` (unified selection), `SaveData`/`SaveManager.cs`

</details>

---

<details>
<summary><strong>Branch Saw System</strong></summary>

**Goal:** Thick branches require a sawing action to remove rather than a single click, making large cuts feel weighty and deliberate.

**Threshold:** When the player clicks a branch in Trim mode and `node.radius >= sawRadiusThreshold` (inspector-tunable, default ~0.08), instead of immediately trimming, enter a **Saw sub-state**.

**Input mechanic:**
- The branch highlights with a saw-cut line across it
- Player must do repeated left↔right mouse/stick input across the cut line
- Each full back-and-forth stroke increments `sawProgress` (0→1); ~4–6 strokes to complete
- Visual: a deepening cut groove appears on the mesh at the cut face as `sawProgress` advances
- If the player clicks elsewhere or presses Escape mid-saw, `sawProgress` resets and the mode cancels

**Completion:**
- When `sawProgress >= 1.0`, the branch severs — same `TrimNode` call as a normal cut, same wound/undo system
- Play a crack/snap SFX at completion
- A few seasons later the wound calluses as normal

**Implementation notes:**
- New `GameState.Sawing` or a sub-mode flag on `ToolManager` — lean toward a flag to avoid a full state machine addition
- The saw target node stored on `TreeInteraction` for the duration
- Groove visual: a flat ring mesh (like the wound torus, but a slit) scaled by `sawProgress`; destroyed on completion or cancel

**Scope:** `TreeInteraction.cs`, `TreeSkeleton.cs` (threshold field), `TreeMeshBuilder.cs` (groove mesh), `buttonClicker.cs`/`ButtonUI.uxml` (progress indicator), `AudioManager` (SFX)

</details>

---

<details>
<summary><strong>Quick-Start / Auto-Generate</strong></summary>

- Auto-simulate ~1 year of growth in background
- Present 10 variation options; player picks one (or multiples) to start with
- Foundation: needs stable multi-year simulation first

</details>

---

<details>
<summary><strong>Gamification & Tutorial Progression</strong></summary>

**Goal:** Stagger tools and mechanics so the player learns one system at a time.

**Progression tiers (draft):**

| Tier | Unlocks | Trigger |
|---|---|---|
| 1 — Seedling | Watering, time controls, camera | Game start |
| 2 — First Cut | Trim tool, wound basics | Tree reaches depth 4 |
| 3 — Shaping | Wire tool, wire removal | First successful trim |
| 4 — Roots | Root prune mode, root planting | First wire set + removed cleanly |
| 5 — Refinement | Pinching, defoliation, leaf management | Tree survives 3 years |
| 6 — Soil Science | Repotting, soil mix, fertilizer | First repot prompt |
| 7 — Advanced | Air layering, Ishitsuki, multi-tree | Species mastery milestone |
| 8 — Master | All tools, competition mode | Complete a styled tree |

**Tutorial delivery:**
- Contextual prompts when a new situation arises
- Optional practice challenges
- No forced tutorials — tools unlock regardless
- In-game Journal/Encyclopedia

**Scoring / achievements:**
- Tree health score (rolling average)
- Style points (taper, ramification, proportion — algorithmic)
- Survival milestones (5, 10, 25 years)
- Technique badges

**Scope:** `ProgressionManager.cs`, `TutorialSystem.cs`, UI overlays, `GameManager.cs`,
save/load integration

</details>

---

<details>
<summary><strong>Decoration System</strong></summary>

**Goal:** Cosmetic elements placed on soil surface, pot rim, rock, or around tree base.

**Decoration types:**

| Type | Placement | Behaviour |
|---|---|---|
| Moss | Soil surface, rock surface | Grows/spreads slowly; needs moisture |
| Grass tufts | Soil surface, rock crevices | Seasonal; green spring/summer, brown autumn |
| Accent rocks | Soil surface | Static |
| Figurines | Anywhere on soil/rock | Static (traditional accent pieces) |
| Fallen leaves | Soil surface | Seasonal scatter in autumn; auto-cleared in spring |
| Deadwood pieces | Soil surface | Decorative driftwood |

**Placement system:**
- Player enters decoration mode (new game state)
- Click to place on valid surfaces; drag to reposition; right-click to remove
- Snapping to surface normals

**Moss (living decoration):**
- Spreads slowly each season if moisture adequate; dies if soil dries out frequently
- Player can trim/remove
- Moss coverage contributes to moisture retention

**Scope:** `DecorationManager.cs`, `Decoration.cs`, `MossDecoration.cs`, `GameState.Decorate`,
prefabs, UI palette, save/load integration

</details>

---

<details>
<summary><strong>Air Layering & Branch-to-New-Tree</strong></summary>

**Goal:** Player can air layer a branch, then sever it and start a brand new tree.

**Air layering process:**
1. **Select branch point:** Click a healthy node with adequate radius
2. **Apply wrap:** Visual wrap appears; air layer timer begins
3. **Root development (1–2 seasons):** Small roots appear; grow stronger each season
4. **Separation:** When roots sufficient, UI prompt: "Air layer roots strong enough. Cut now?"
5. **Sever:** Current state auto-saved; branch becomes new independent tree in fresh pot;
   original tree continues with wound at cut site

**New tree from air layer:**
- Inherits branch structure, wire state, health, refinement of source branch
- Starts with air layer roots as its initial root system

**Scope:** `TreeSkeleton.cs` (air layer state, root development, separation logic),
`TreeNode.cs` (air layer fields), `TreeInteraction.cs`, `SaveManager.cs` (clone subtree),
`GameManager.cs` (new tree session), `AirLayerTool` game state, separation prompt UI

</details>

---

<details>
<summary><strong>Steam / System Achievements</strong></summary>

**Goal:** Award achievements through Steamworks (or OS notifications on non-Steam builds) for meaningful milestones.

**Integration approach:**
- Wrap Steamworks.NET (or Facepunch.Steamworks) behind a thin `AchievementManager.cs` singleton
- On non-Steam builds, fall back to a local `PlayerPrefs` unlock log + optional in-game toast
- Call `AchievementManager.Unlock("ID")` from the relevant system; the manager deduplicates

**Draft achievement list:**

| ID | Name | Trigger |
|---|---|---|
| `FIRST_TRIM` | First Cut | Player trims for the first time |
| `FIRST_WIRE` | In Training | Wire fully set and removed cleanly |
| `SURVIVE_5` | Five Candles | Tree survives 5 in-game years |
| `SURVIVE_10` | Decade | Tree survives 10 years |
| `SURVIVE_25` | Ancient | Tree survives 25 years |
| `FIRST_REPOT` | Root Bound | Complete a repot |
| `ISHITSUKI` | Stone & Root | Place tree on a rock (Ishitsuki confirmed) |
| `AIR_LAYER` | New Life | Successfully sever an air layer |
| `MYCORRHIZAL` | Fungal Network | Achieve mycorrhizal coverage on 50 % of roots |
| `HEALTHY_DECADE` | Thriving | Maintain avg health > 80 % for 5 consecutive seasons |
| `ALL_SPECIES` | Collector | Grow every available species at least once |
| `FIRST_DEATH` | Lessons Learned | Let a tree die (unlocks on death screen) |

**Scope:** `AchievementManager.cs` (new), hook calls in `TreeSkeleton.cs`, `buttonClicker.cs`, `SaveManager.cs`; Steamworks package import

</details>

---

<details>
<summary><strong>Decoration System</strong></summary>

**Goal:** Cosmetic elements placed on soil surface, pot rim, rock, or around tree base.

**Decoration types:**

| Type | Placement | Behaviour |
|---|---|---|
| Moss | Soil surface, rock surface | Grows/spreads slowly; needs moisture |
| Grass tufts | Soil surface, rock crevices | Seasonal; green spring/summer, brown autumn |
| Accent rocks | Soil surface | Static |
| Figurines | Anywhere on soil/rock | Static (traditional accent pieces) |
| Fallen leaves | Soil surface | Seasonal scatter in autumn; auto-cleared in spring |
| Deadwood pieces | Soil surface | Decorative driftwood |

**Placement system:**
- Player enters decoration mode (new game state)
- Click to place on valid surfaces; drag to reposition; right-click to remove
- Snapping to surface normals

**Moss (living decoration):**
- Spreads slowly each season if moisture adequate; dies if soil dries out frequently
- Player can trim/remove
- Moss coverage contributes to moisture retention

**Scope:** `DecorationManager.cs`, `Decoration.cs`, `MossDecoration.cs`, `GameState.Decorate`,
prefabs, UI palette, save/load integration

</details>

---

<details>
<summary><strong>Steam / System Achievements</strong></summary>

**Goal:** Award achievements through Steamworks (or OS notifications on non-Steam builds) for meaningful milestones.

**Integration approach:**
- Wrap Steamworks.NET (or Facepunch.Steamworks) behind a thin `AchievementManager.cs` singleton
- On non-Steam builds, fall back to a local `PlayerPrefs` unlock log + optional in-game toast
- Call `AchievementManager.Unlock("ID")` from the relevant system; the manager deduplicates

**Draft achievement list:**

| ID | Name | Trigger |
|---|---|---|
| `FIRST_TRIM` | First Cut | Player trims for the first time |
| `FIRST_WIRE` | In Training | Wire fully set and removed cleanly |
| `SURVIVE_5` | Five Candles | Tree survives 5 in-game years |
| `SURVIVE_10` | Decade | Tree survives 10 years |
| `SURVIVE_25` | Ancient | Tree survives 25 years |
| `FIRST_REPOT` | Root Bound | Complete a repot |
| `ISHITSUKI` | Stone & Root | Place tree on a rock (Ishitsuki confirmed) |
| `AIR_LAYER` | New Life | Successfully sever an air layer |
| `MYCORRHIZAL` | Fungal Network | Achieve mycorrhizal coverage on 50 % of roots |
| `HEALTHY_DECADE` | Thriving | Maintain avg health > 80 % for 5 consecutive seasons |
| `ALL_SPECIES` | Collector | Grow every available species at least once |
| `FIRST_DEATH` | Lessons Learned | Let a tree die (unlocks on death screen) |

**Scope:** `AchievementManager.cs` (new), hook calls in `TreeSkeleton.cs`, `buttonClicker.cs`, `SaveManager.cs`; Steamworks package import

</details>

---

<details>
<summary><strong>Multi-Tree Planting</strong></summary>

- Plant two trees in the same pot
- Root/branch collision and fusion mechanics
- Shared health/soil system

</details>

---

<details>
<summary><strong>Camera & Input</strong></summary>

- Full Unity Input System migration (currently legacy `Input.*`)
- Wire animation skip key revisited as part of this

</details>

---

<details>
<summary><strong>Branch Promotion Advisor (Learning Tool)</strong></summary>

### Goal
A guided coaching mode that reads the tree's current state and gives the player actionable suggestions for achieving a specific growth goal — starting with "promote this branch." Long-term foundation for an in-game tutor that can teach any technique the game supports.

---

### UX Flow

1. Player activates the **Promote Branch** tool (new button, same toolbar row as Pinch).
2. Branch tips highlight with the same dot indicators used in Pinch mode.
3. Player clicks a terminal tip they want to grow more vigorously.
4. The system runs a **promotion analysis pass** on the skeleton data:
   - Identifies competing branches drawing more apical vigor (higher vigorFactor, larger radius, closer to apex).
   - Identifies branches shading the target (roughly above and overlapping in XZ).
   - Scores each branch by how much its removal or reduction would benefit the target.
5. **Visual overlays appear on the tree:**
   - Gold highlight: the chosen target tip.
   - Red/orange tint + marker: branches suggested for trimming (ranked by impact, top 3–5).
   - Yellow marker: branches suggested for pinching instead of removing.
   - Dim grey: branches that are neutral / should be left alone.
6. **Text panel** (side panel, same style as stats panel) shows:
   - "To promote [branch X]:" header.
   - Bulleted list of actions: "Trim branch Y in Spring — redirects apical energy" / "Pinch branch Z now — suppresses extension without removing".
   - Season timing for each suggestion.
   - Brief one-line explanation of why (apical dominance, shading, vigor ratio).
7. Suggestions update live if the player trims/pinches while the advisor is active (**persistent coaching mode**). One-shot mode available as a simpler alternative — same analysis, but overlays clear on next click.

---

### Analysis Logic

All data is already present on `TreeNode`:
- `vigorFactor` / `health` — vigor state of each node.
- `growDirection` — used to determine if a branch is above/shading another.
- `depth` — apical dominance: shallower depth = more dominant.
- `radius` — branch thickness as a proxy for resource draw.

**Promotion score** for branch B relative to target T:
```csharp
float PromotionScore(TreeNode b, TreeNode target)
{
    // How much does removing B help T?
    float depthAdvantage  = Mathf.Max(0f, target.depth - b.depth) / maxNodeDepth;  // B is closer to apex
    float vigorDominance  = b.vigorFactor - target.vigorFactor;                    // B is drawing more vigor
    float shadingPenalty  = IsShadingTarget(b, target) ? 0.4f : 0f;               // B is above T in XZ
    return depthAdvantage + vigorDominance + shadingPenalty;
}
```
Branches with score above a threshold are flagged for trimming; mid-score branches are flagged for pinching.

**Season suggestion:**
- Trim suggestions: "in Spring" for vigorous growth redirection; "any time" for deadwood.
- Pinch suggestions: "now" if currently growing season; "next Spring" otherwise.

---

### Modes

| Mode | Behaviour |
|---|---|
| **One-shot** | Click target → overlays appear → next tool click clears everything |
| **Persistent coaching** | Overlays stay active; re-analysis fires after each trim or pinch; panel updates in real-time |

Start with one-shot; add persistent as a toggle in the tool panel (`[Keep Active]` checkbox).

---

### Future Expansion

The same analysis architecture supports other coaching goals:
- "Reduce this branch" — find what's feeding it; suggest root pruning or defoliation.
- "Improve ramification here" — identify under-developed pads; suggest pinch timing.
- "Balance the canopy" — flag overlong vs. underdeveloped pads across the whole tree.
- "Prepare for show" — score against classic bonsai proportion rules; suggest corrections.

Each goal is a different scoring function over the same node graph — no new simulation needed.

---

### Scope

| File | Change |
|---|---|
| `TreeInteraction.cs` | New `PromoteBranch` tool mode; tip-click handler; `PromotionAnalysisPass()`; GL overlay draw in `OnRenderObject` |
| `TreeSkeleton.cs` | Expose `PromotionScore()` helper; `IsShadingTarget()` |
| `buttonClicker.cs` | Tool button + panel open/close; text suggestion list build; one-shot vs. persistent toggle |
| `ButtonUI.uxml` | Promotion advisor panel (side panel, collapsible) |

</details>

---

## Reference

<details>
<summary><strong>Ishitsuki Root Selection — How It Works</strong></summary>

> This section explains what happens to existing roots when Ishitsuki is confirmed,
> for reference when designing future Ishitsuki features.

**What the code does at confirm time (`SpawnTrainingWires`):**

1. Every direct child of `root` that is `isRoot=true` is a **trunk-root startNode** —
   the first segment of each root cable. These are the only root nodes that survive confirm.

2. Every node below those startNodes is **deleted** via `RemoveSubtree`.

3. `PreGrowRootsToSoil` then drapes a fresh `isTrainingWire=true` chain from each
   startNode's tip, down the rock face, to the soil.

**Implication for the player:**

- The **number of Ishitsuki strands = the number of trunk-root startNodes at confirm time**.
  If the tree auto-planted 6 trunk roots before confirm, there will be 6 strands.
- The **direction** each cable drapes is determined by the startNode's `growDirection`.
- All the deeper, branching organic root growth before confirm is discarded.

**Future design note:**
If we want pre-confirm root work to matter more:
- Let startNode direction be influenced by where the organic root was growing
- Preserve the top N segments of each organic root chain as the cable's initial part

</details>

---

## Completed Items Log

All systems built across all phases:

- Procedural branch skeleton + mesh builder (`TreeSkeleton`, `TreeMeshBuilder`)
- Wire placement + bend (`TreeInteraction`, `WireRenderer`)
- Trim subtree + highlight mesh
- Leaf lifecycle: spring spawn, autumn colour gradient, stochastic fall, fall animation (`LeafManager`, `Leaf`)
- Seasonal time system + game state machine (`GameManager`)
- Camera orbit, zoom, Y-pan (`CameraOrbit`)
- Tight-angle geometry: bend rings + parallel-transport frame fix (`TreeMeshBuilder`)
- Post-trim depth cap: cut point tracking + per-season regrowth limit (`TreeSkeleton`, `TreeNode`)
- Wire rework: instant snap + spring animation, `WireAnimate` state (`TreeInteraction`, `GameManager`)
- Health system foundation: `health` on `TreeNode`, `DamageType` enum, `ApplyDamage`, health-gated growth (`TreeSkeleton`, `NodeHealth.cs`)
- Wire colour progression: silver → gold pulse → orange → red (`WireRenderer`)
- Root system: `isRoot` flag, gravity-biased growth, `PlantRoot`, `RootPrune` state, lift animation, soil-plane interaction, pitch relaxation
- Bud system: terminal buds set in August, bud GOs spawned/destroyed, back-budding on trim, `backBudActivationBoost`, old-wood bud chance
- Wound system: half-torus wound visualization, health drain per season, cut paste tool, `pasteApplied` tint, subdivision-cut detection, cleanup on subtree removal
- Growth stability: `maxBranchNodes` hard cap, `vigorFactor` scaling lateral/back-bud chances; fixed `InvalidOperationException` in back-budding loop
- Root Area box containment: `rootAreaTransform` reference; `RootDistRatio` checks XZ walls + Y floor/ceiling; `DeflectFromRootAreaWalls` handles all six faces
- Pot-bound root system: `boundaryPressure` counter, thickening over seasons, `boundaryGrowthScale`, `wallSegmentScale`, `potBoundInnerBoost`, fill-in lateral budget
- Ishitsuki (root-over-rock): `RockPlace` + `TreeOrient` states, rock grab/rotate/confirm, training wires, drape over rock via `PreGrowRootsToSoil`, `IshitsukiWire.cs`
- Trim trauma: `DamageType.TrimTrauma`, seasonal recovery scaled by `treeEnergy`
- Trim undo: 5-second real-time window; `TrimUndoState` captures full subtree; `Ctrl+Z` restores tree; countdown label in UI
- Health ring GL debug: `OnRenderObject()` draws green→yellow→red rings at each node
- Bud/leaf integration: `birthYear` on `TreeNode`; leaves only from buds on old wood
- Save / Load System: full JSON via `JsonUtility`; `SaveData`, `SaveNode`; auto-save on season end; manual save in Settings
- Root Area box containment + pot-bound pressure system
- Auto-water: fires before drought threshold; Debug tab toggle; Water button flash; in-game-day cooldown
- Ishitsuki white first segment fix: `startNode` marked `isTrainingWire=true`
- Ishitsuki roots continue underground: soil-entry node spawned at soil contact point
- Ishitsuki cable growth animation: `PreGrowRootsToSoil(animated:true)` places one segment per spring
- Ghost root fix: `RemoveSubtree` called on old chains before re-draping at confirm
- Camera jump fix: `OnGameStateChanged` clears `isDragging`/`isPanning`; `lastTargetPosition` delta compensation
- Fertilizer System: `nutrientReserve` drain/boost, winter block, growth multiplier, FertilizerBurn, auto-fertilize
- Weed System: RMB pull mechanic, rip chance, 4 weed types, WeedManager + WeedPuller, Herbicide button
- Fungus System: `fungalLoad`, seasonal spread + damage, mycorrhizal network, Fungicide button, leaf tint
