# Progression, Economy & Tutorial — Design Pass

*Design pass for Plan item #19 (Gamification & Tutorial Progression). Status: design, awaiting build approval. Drafted 2026-06-14.*

## Design pillars (from direction call)

1. **Two modes, chosen at New Game.**
   - **Career** — tools unlock gradually for guided, one-system-at-a-time learning.
   - **Sandbox** — everything unlocked from the start; free play.
2. **Zen meta-layer, not a grind.** No XP bars, no levels, no leaderboards, no
   competition mode. The reward is the tree itself, plus a quietly-filling
   Journal/Encyclopedia and gentle milestones.
3. **Soft cosmetic economy.** A single earned currency buys **aesthetic upgrades
   only** — pots, rocks, tables, ground cover, decorations, display scenes.
   **Never** gameplay power (no faster growth, no health buffs). Beauty is the
   payoff, so spending stays zen.

The three pillars are independent layers: the **economy + Journal run in both
modes**; only **tool-gating** is Career-specific.

---

## 1. Currency — "Aesthetic Points"

A single soft currency, earned slowly through good stewardship, spent on beauty.
Earning is **passive-friendly** so it never becomes a chore:

| Source | Award | Rationale |
|---|---|---|
| Seasonal stewardship | small trickle each season × tree health (rolling avg) | rewards keeping trees alive & healthy, not clicking |
| Milestones (first-time) | lump sum per Journal milestone | one-off discovery rewards |
| Seasonal style bonus | scaled by AutoStyler `MatchPercent` / refinement at season end | rewards well-shaped trees |
| Species & style completion | larger lump on first complete of each | long-horizon goals |

Tuning goal: a casually-tended healthy tree earns enough for a nicer pot every
few in-game years — visible progress, no grind. Career and Sandbox both earn
(Sandbox players still want to dress their trees).

Currency lives in a **global profile** (not per tree) so it accumulates across
every tree you grow.

## 2. Aesthetic shop — reuse the Item Catalog

No separate shop screen. The **existing `ItemCatalogPanel`** (pots / rocks /
tables, and later Decoration's ground cover/figurines) becomes the storefront:

- `ItemDefinition` gains `cost` and an `unlockedByDefault` / milestone-unlock flag
  (the `unlockCondition` placeholder H already left).
- The panel shows a **balance header**, each card shows **price + owned/locked
  state**, and a locked card has a **Buy** button (disabled if you can't afford).
- Purchased item IDs persist in the global profile; owned items are free to swap
  thereafter.
- A few items can be **milestone-locked** instead of priced (e.g. a special pot
  for completing your first styled tree) — flavour, optional.

## 3. Career tool-gating

Career gates tools behind gentle, teaching-oriented triggers. Tools **unlock**
(never re-lock), each unlock fires a contextual tutorial. Sandbox skips all of
this. Refined from the draft tier table toward "unlock when the situation that
needs the tool first arises":

| Tier | Unlocks | Trigger |
|---|---|---|
| 1 Seedling | Water, time controls, camera | game start |
| 2 First Cut | Trim, cut paste | tree has real branch structure (≥ N depth-1 branches) |
| 3 Shaping | Wire, wire removal | after first trim |
| 4 Refinement | Pinch, defoliation, leaf mgmt | tree survives ~3 years |
| 5 Soil | Repot, soil mix, fertilizer, rake | first time the tree is pot-bound (root pressure high) |
| 6 Roots | Root prune, root-over-rock planting | after first clean repot |
| 7 Advanced | Air layering, Ishitsuki, Quick-Start, multi-tree | first styled tree (high MatchPercent) |

Unlocks are **global profile** state — once you've learned Trim, every later
Career tree starts with it. (You're not re-taught on tree #2.)

## 4. Zen rewards — milestones & Journal

The non-currency reward layer, active in **both** modes:

- **Milestones** (gentle toast + Journal entry, no score): first leaf, first
  bloom, first fruit, first trim/wire/repot, survive 1 / 5 / 10 / 25 years,
  first styled tree, each species grown, each style completed.
- **Journal / Encyclopedia** — fills in as you encounter things: a **Techniques**
  section (short how/why blurb per tool, unlocked when you first use or unlock it),
  a **Species** section (the botanical notes we already have in
  `SPECIES_REALISM.md` + bark/foliage/bloom facts, unlocked per species grown),
  and a **Phenomena** section (bud break, leaf fall, wound healing, flowering —
  unlocked when first observed). This is the quiet "collection" reward.
- **Light readouts only:** reuse the existing Tree Health panel and AutoStyler
  MatchPercent. No global player level.

## 5. Architecture & scope

**New global profile (separate from per-tree save slots):**
`ProgressionProfile` — `gameModeDefault`, `currency`, `unlockedTools` (set),
`ownedItemIds` (set), `milestoneFlags` (set), `journalEntries` (set). One file in
`persistentDataPath`, loaded at boot.

**New components / files:**
- `ProgressionManager.cs` (scene singleton) — owns the profile, evaluates triggers
  by subscribing to `GameManager.OnGameStateChanged` / `OnMonthChanged` + tree
  state, raises `OnToolUnlocked`, `OnMilestone`, `OnCurrencyChanged`. `IsUnlocked(tool)`
  returns true in Sandbox.
- `CurrencyManager` (or fold into ProgressionManager) — `Award(amount, reason)`,
  `TrySpend(amount)`, balance events. Awards narrate to the Care Log / a toast.
- `JournalPanel.cs` — code-built overlay (same card pattern as the others):
  Techniques / Species / Phenomena tabs + milestone log.
- `GameMode` enum `{ Career, Sandbox }` — chosen at New Game (species-select screen),
  stored per save slot.
- Balance HUD chip (small, top bar) + milestone toast.

**Touch points:**
- `ItemDefinition` (+`cost`, unlock flag), `ItemCatalogPanel` (prices/balance/buy),
  `ItemCatalog`.
- `buttonClicker` — tool buttons check `ProgressionManager.IsUnlocked`; New Game
  mode pick; wire up Journal + balance HUD.
- `SaveManager` — load/save the global `ProgressionProfile`; store `GameMode` on the
  slot.
- `GameManager` — expose any missing milestone signals (first bloom already flows
  through FlowerManager; survival years from the calendar).

## 6. Build slices (incremental, each shippable)

1. **Economy + zen core** — `ProgressionProfile` (global save), `CurrencyManager`
   (award/spend/balance + HUD chip), milestone tracking + toast, `JournalPanel`
   (milestone log first; encyclopedia content later). Earns in both modes. *No
   gating, no shop yet — currency just accrues and milestones pop.*
2. **Aesthetic shop** — `ItemDefinition.cost` + owned state, `ItemCatalogPanel`
   prices/balance/buy, persist ownership. Currency becomes spendable on pots/rocks/
   tables. (Decoration items slot in when #21 is built.)
3. **Career gating + mode select** — `GameMode` at New Game, tool-unlock evaluation,
   `buttonClicker` gates, contextual unlock tutorials (reuse the first-use tooltip
   system, SYSTEMS §35).
4. **Content & tuning** — encyclopedia entries (techniques/species/phenomena),
   tutorial copy, currency-rate balancing.

Recommended start: **Slice 1** — it's the zen reward spine and the economy
foundation, works in both modes, and is independent of the mode-select work.

## 7. Cosmetic categories — Decoration / Background / Music / UI Theme *(added 2026-06-15)*

The shop generalizes to any cosmetic. `ItemCategory` gains `Decoration, Background, Music,
UiTheme`; each is just an `ItemDefinition` priced in Aesthetic Points, browsed + bought through
the **same** `ItemCatalogPanel`. What's new per category is only (a) the **apply handler** and
(b) the **entry point**.

- **Entry point:** placeable items (pots/rocks/tables) surface at their existing moments;
  non-placeable media (background/music/theme) browse from a shared **Customize menu**
  (`CustomizeManager`, opened by the ⚙ on the AP chip).
- **Equipped state:** `ProgressionProfile` keeps `equippedBackground / equippedMusic /
  equippedTheme` (ItemDefinition.Id), re-applied on load by `CustomizeManager.RestoreEquipped`.
- **Apply handlers:**
  - **Background** ✅ built — `BackgroundManager.Apply` swaps the camera backdrop (skybox material,
    or a solid `swatchColor`) + `ambientColor`, optional scenery `prefab`; captures the scene's
    original look so the free default restores it. Default catalog ships 5 colour backdrops.
  - **UI Theme** ✅ built — `UiTheme` central palette (5 code-defined themes) drives every code-built
    overlay (shop / Journal / Customize / AP HUD); the panels read `UiTheme.Current` at build time and
    the always-on HUD rebuilds on `OnThemeChanged`. A theme item carries a `themeId`; apply = palette
    swap. **Main game UI (UXML) is also themed** by `GameUiThemer`: the UXML uses inline styles (so
    stylesheet-swapping won't work), so it walks the UIDocument tree on theme change and remaps the
    known chrome colours (dark button/panel bg, grey borders, grey/green labels, gold accents) to the
    palette, leaving functional bars/textures alone. (Heuristic colour-match, not a USS-class refactor.)
  - **Music** ✅ built — `MusicManager` (own AudioSource) crossfades to the equipped track;
    `ItemDefinition.audioClip` holds it (null = silence). Tracks are author-supplied (drop AudioClips
    onto the `Music_*` assets); the catalog ships a free "Silence" + 4 named slots.
  - **Decoration (#21)** ✅ built — `DecorationManager` places a figurine/accent beside the tree;
    a Decoration `ItemDefinition` supplies a `prefab` or a built-in `proceduralId` (ships a procedural
    "Stone Lantern" so it works with zero art); one shown at a time, equipped-state persisted. Set the
    manager's `anchor` to where decorations sit (or it offsets from the planter/tree).

Build order for this expansion: **Backgrounds + scaffolding ✅** → **UI Themes ✅** → **Music ✅** → **Decoration ✅**. (Item #21 complete.)
