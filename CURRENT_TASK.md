# Current Task

## 📌 Next session — START HERE (handoff from 2026-06-29)

**Done this session (committed):**
- **The Script — full first slice built & wired in Unity:** Focus economy (+ End Turn banking), **Remember**
  (right-click hand persistence), **Prime** (colour-biased draw via HUD buttons), **Foresee**
  (`ForeseeController` — right-click Junior → camera push-in, looping Thinking pose, pitch-bent music,
  hidden HUD, optional art border; preview & reroll next turn's hand). Full detail in [FOCUS_DESIGN.md](FOCUS_DESIGN.md).
- **Heavy-hitter fusion recipes wired:** 8 `FusionRecipe` assets in `Assets/Assets/Cards/Fusion/` + registered
  in `FusionBook` (see the fusion section below). Placeholder art added; **final art still TODO**.
- **Post-fight reward screen:** new `RewardController` (code-built) — victory shows a recipe-or-Insight choice,
  applied to the run; `FusionBook.ApplyRunRecipes()` completes recipe persistence. Needs a `RewardController`
  GameObject placed in the Combat scene.

**Pick up next session (roughly in order):**
1. ⭐ **Junior sprite effect during Foresee** *(new request)* — give Junior a visual effect while she's
   zoomed-in / Thinking in the Foresee focus (e.g. glow / outline / paint aura / shimmer). Hook it on in
   `ForeseeController.EnterForesee` (next to `SetThinking(true)` / `ZoomIn()`) and off in `ExitForesee`.
   Candidate tools already in the project: `CombatVFXManager.PlayOnPlayer(VfxType.…)`, the `HitFlash`
   component on the Player, or a toggled child VFX object / material swap. **Decide the look with the user first.**
2. **Wire the Insights runtime in Unity** so the reward screen can offer Insights (not just recipes): create
   the `InsightDatabase` asset + the first Insight assets + an `InsightHost` GameObject — exact table/steps in
   the "Insights runtime" goal section just below.
3. **Place the `RewardController` GameObject** in the Combat scene + verify the reward flow end-to-end.
4. **Finish heavy-hitter art** (placeholder → final) — then those 8 cards are fully done.
5. **Continue the Focus arc:** Focus-cost cards (`CardData.focusCost`, the spend sink) → enemy anti-Focus
   debuffs → cape-pose polish. **Open call:** roguelike vs roguelite (does The Script persist across runs).

---

## 🎯 Goal: Insights runtime — first slice + pass 2 (CODE-COMPLETE, awaiting Unity wiring)

The runtime that makes Inspirations fire in combat. Built **before** the reward system that grants
them (an Insight reward is meaningless until Insights can *do* something).

**What Insights are:** permanent, passive, per-run buffs (relics). One of Junior's two power axes
alongside learned fusion recipes. See **"How Insights work"** in [MAP_DESIGN.md](MAP_DESIGN.md).
Content (~186 designed) lives in the **"Inspirations"** tab of `Cards.xlsx`.

### Architecture (mirrors `EnemyAbility`, but data-driven)
- **`Insight` SO** = the data (id, rarity, `trigger`, `effectId`, `amount`, stacking). Authored from
  the sheet. (`Assets/Assets/Managers/CombatScene/Insights/Insight.cs`)
- **`InsightDatabase` SO** = id→asset registry (mirrors `CardDatabase`).
- **`InsightHost`** (singleton, in the Combat scene) collects the run's Insights via
  `InsightDatabase.Find(id)` and broadcasts hooks; each `effectId` is one case in `Resolve` (same
  shape as `CardEffectRegistry`). Scales to all ~186 as assets + one case each.
- **`RunManager.insights`** (`List<string>`, duplicates allowed = stacking) holds the run's set;
  `ApplyFromRun()` re-applies them each combat. Falls back to the host's `debugInsights` when no run.

### Done (code, this slice)
- New files: `Insight.cs`, `InsightDatabase.cs`, `InsightHost.cs`.
- **Flat Shield pool** on `Player` (`shieldPoints` + `AddShield`/`GetShield`) — a Slay-the-Spire block
  that absorbs direct hits before HP, **separate** from the halving `Shield` *status* (so "Gain N
  Shield" honors N). Direct hits only (not poison ticks); persists for the combat (per-turn reset = a knob).
- Broadcasts wired: `OnCombatStart`/`OnTurnStart`/`OnTurnEnd`/`OnKill`/`OnPlayCard` (BattleManager,
  incl. new `GrantAP`), `OnTookDamage` (Player), `OnFuse` (FusionController). `HandManager.DrawExtra`,
  `RunManager.AddInsight`. All broadcasts null-guarded → combat unchanged if no host present.
- 4 first-slice Insights (effectIds `GAIN_AP_THIS_TURN`/`GAIN_SHIELD`/`HEAL`/`DRAW_CARDS`):
  Quick Drying, Sturdy Easel, Audience Clap, Loose Grip (+ bonus Second Palette).

### TODO — Unity wiring (do on recompile)
1. Recompile; confirm no errors.
2. Create an `InsightDatabase` asset + the 4 Insight assets (`Assets > Create > Insights > …`):

   | displayName | id | trigger | effectId | amount | stacks/perCopy |
   |---|---|---|---|---|---|
   | Quick Drying | `quick_drying` | TurnStart | `GAIN_AP_THIS_TURN` | 1 | ✓ / 1 |
   | Sturdy Easel | `sturdy_easel` | CombatStart | `GAIN_SHIELD` | 3 | ✓ / 3 |
   | Audience Clap | `audience_clap` | Kill | `HEAL` | 2 | — |
   | Loose Grip | `loose_grip` | TookDamage | `GAIN_SHIELD` | 1 | ✓ / 1 |

3. Add Insights to the database; create an `InsightHost` GameObject (assign `Player` + database; drop
   the assets into `debugInsights` for standalone testing). `BattleManager.insightHost` auto-finds it.
4. Verify: Quick Drying → AP `7/6` at turn start; Sturdy Easel → first 3 dmg absorbed; Loose Grip →
   shield builds per hit; Audience Clap → heal on kill. Duplicate in `debugInsights` to confirm stacking.

### Pass 2 — widen coverage (DONE, code)
- **All hooks now broadcast**: `OnHeal` (Player.Heal, recursion-guarded), `OnDealtDamage` + `OnApplyStatus`
  (Enemy). `OnCombatStart/TurnStart/TurnEnd/Kill/PlayCard/TookDamage/Fuse` already wired in the first slice.
- **Pre-hit damage modifier**: a static `InsightHost.DealingCardDamage` flag is set around the card impact
  callback (`Card.STWithImpact`/`AOEWithImpact`), so `Enemy.TakeDamage` calls `ModifyDamageToEnemy(this, dmg,
  isAoe)` for player card hits only — "+X damage" Insights change the number without touching the 13 card cases.
- **Insight SO gating**: `oncePerTurn` / `oncePerCombat` (reset at TurnStart / ApplyFromRun).
- **effectId catalog** (author Insights against these — no code needed):
  - Start/turn/utility: `GAIN_AP_THIS_TURN`, `GAIN_SHIELD`, `HEAL`, `DRAW_CARDS`, `REDUCE_POISON`.
  - Damage modifiers (trigger DealtDamage): `DAMAGE_BONUS`, `DAMAGE_BONUS_ABOVE_HP` / `BELOW_HP` (threshold %
    in `amount2`), `DAMAGE_BONUS_PERCENT`, `DAMAGE_BONUS_ST` / `AOE`.
  - Colour "on play": `ON_PLAY_DAMAGE_RANDOM` / `ON_PLAY_SHIELD` / `ON_PLAY_DRAW` (`param` = colour, blank = any).
  - Fusion: `ON_FUSE_DRAW` / `ON_FUSE_SHIELD` (`param` = colour involved). Kill: `ON_KILL_DAMAGE_RANDOM`.
  - Status: `ON_STATUS_DEAL_DAMAGE` (`param` = status-name filter, e.g. `Poison` or `AttackBreak,Corrode`).

### Pass 3 — remaining (next)
- Status **power/duration** modifiers (Hex Palette, Yellow Frenzy, Ink Blot) — need a pre-apply hook
  mirroring the damage one. Per-turn-first-hit gating (Palette Knife). AP-next-turn (pending-AP) effects.
- Funnel the 3 direct-`activeEffects.Add` status cards (Attack Break, Corrode, Crushing Paint) through the
  `Enemy.Apply*` methods so `OnApplyStatus` fires uniformly (Poison/Sleep already covered).
- Author the ~186 assets from the Inspirations tab.
- **Caveat — card-cost gating doesn't exist:** every card costs 1 AP flat, so all "costs 1 less AP" /
  discount / free-fuse Insights are no-ops until that's built. Other unbuilt systems: draw pile (*Scry*),
  "Paint" temp HP, shop economy.

---

## 🔭 Review later: fusion combos / economy

The 8 heavy-hitter cards are coded, have `CardData` assets, and now have **fusion recipes wired** (8
`FusionRecipe` assets in `Assets/Assets/Cards/Fusion/`, registered in `FusionBook`). They fuse correctly,
but **still need art** — the fusion preview + spawned card key off `cardSprite`, so they render blank until
each card's sprite is assigned. Balance of the whole fusion table is still a later pass. Context:

- **Convention:** 18 of 19 recipes are **cost-1 + cost-1** (e.g. Imaginary Paint = Red Stroke + Attack
  Break). The lone exception is **CREATIVE FREEDOM = Reckless Stroke + Restore** — a deliberate 2-step
  reserved for the ultimate. Don't gate heavy hitters behind fuse-of-fusions; keep them one step from a
  drawn hand and gate *power* via rare learned recipes (reward layer; currently bypassed by
  `unlockAllForTesting`).
- **Headroom:** 13 cost-1 cards → 91 possible pairs, only 18 used. The newer commons **Ink Needle,
  Smudge, Rage Mark, Critic's Note, Streaking Medium** are in **zero** recipes — good free ingredients.
- **Wired cost-1 pairings** (✅ created as FusionRecipe assets, order-agnostic): Astral Nuke = Rage Mark +
  Ink Needle · Bleed Out = Ink Needle + Smudge · Vermillion Spear = Red Stroke + Critic's Note · Void
  Siphon = Red Stroke + Streaking Medium · Full Palette = Y-Spray + Ink Needle · Bloodbath = Enrage +
  Y-Spray · The Nothing = Smudge + Y-Spray · Blank Canvas = Cleanse + Streaking Medium. (Not used: the
  optional "ultimate" 2-step Astral Nuke = Imaginary Paint + Enrage.)
- Also worth reviewing: why mid-tier fusions feel rare (draw-RNG over 13 commons + limited fuses/turn,
  not fusion depth).

---

## Project State (map/run build)

**Done:**
- Run loop: `RunManager` run-state hub, HP carryover, victory→map / defeat→fresh-run wiring.
- Authored **branching map** (`MapNode` + `MapController`, world-space or UI nodes).
- `TestFlowController` is run-aware (start screen skipped when arriving from the map).
- **Multi-wave Dire** stages (`EncounterData.nextWave`).

**Post-fight reward screen — ✅ code-complete (this session):** `RewardController` (code-built overlay like
FusionController) — on victory, `BattleManager.HandleVictory` shows a choice of a fusion recipe **or** an
Insight (mix of up to 3 options + optional Skip); the pick is applied to the run (`RunManager.LearnRecipe`
/ `AddInsight`) so it carries forward. **Wiring:** add a `RewardController` GameObject to the Combat scene
(auto-finds FusionBook via FusionController + InsightDatabase via InsightHost). Insight *options* appear
once the InsightDatabase is populated; recipe rewards *gate* once `unlockAllForTesting` is off.

**Still deferred:**
- Shop & Rest/Snack panels.
- A real `GameOver` scene (defeat currently bounces to the map as a fresh run).
- Recipe unlocking: `FusionBook.ApplyRunRecipes()` now applies the run's `unlockedRecipes` each combat
  (called in `BeginBattle`); still bypassed by `unlockAllForTesting` (flip it off to make recipe rewards gate).

---

## ✅ DONE: Focus / The Script — first slice (all 4 verbs) built & wired in Unity
Turn-level **agency** system to fix luck-based combat (random fresh hand each turn). Junior earns **The
Script** (a cape) after Act I → spend **Focus** to Remember / Prime / Foresee her draw. Full spec in
**[FOCUS_DESIGN.md](FOCUS_DESIGN.md)**. Economy locked (+1/turn, cap 3, persists, 2 AP→1 Focus).

**Built & wired in Unity (run-specific):**
- **Step 1 — resource + economy:** `BattleManager` Focus (`focus`/`focusCap=3`/`focusPerTurn=1`) +
  `GrantFocus`/`SpendFocus`/`CanSpendFocus`; +1 at turn start; `floor(spareAP/2)` banked at `EndPlayerTurn`;
  null-guarded `focusText` HUD counter. **Pass button repurposed → End Turn** so unspent AP can be banked.
- **Step 2 — Remember (hand persistence):** `HandManager.DrawHand` keeps Remembered cards and tops up the
  rest (fewer fresh draws = built-in cost); `DiscardHand` erases only non-Remembered + returns the dumped
  count (Blank Canvas uses it); `Card.ToggleRemember` (**right-click** a hand card) spends/refunds 1 Focus,
  gold-tint + optional `rememberedIndicator`; drag-to-play now turn-gated (cards persist into enemy turn).
- **Step 3 — Prime (colour-biased draw):** `BattleManager.PrimeColor(colour)` (toggle/swap, costs 1 Focus,
  for HUD colour buttons) + `HandManager.DrawHand` guarantees the first fresh slot is the primed colour and
  weights the rest by `primeBias` (default 0.5), consumed after the draw; optional `primeText` HUD label.
  Primeable cost-1 colours: **Red, Blue, Yellow, Crimson, Purple, Golden**.
- **Step 4 — Foresee (preview/reroll next hand):** new `ForeseeController` (code-built stage like
  `FusionController`) — right-click Junior → camera push-in + "Thinking" pose, next turn's cards float
  face-down + FORESEE/BACK buttons. FORESEE (1 Focus) reveals them from the dark; click a card to reroll
  (1 Focus each); BACK zooms out. `HandManager.ComputeForeseenFresh`/`RerollForeseen`/`foreseenFresh`,
  consumed by `DrawHand` (Prime baked in; gracefully tops up/truncates if Remembers change afterward).
  Presentation: FORESEE/BACK flank Junior, HUD hidden via `hideDuringForesee`, music pitch-bends down
  (slows + deepens) and restores on BACK, optional full-screen `foreseeBorderSprite` over the vignette.
- Run-specific (resets per combat); no cross-run persistence yet.

**Wired in Unity (done):** `focusText`/`primeText` HUD + per-colour Prime buttons; Pass → End Turn;
optional `rememberedIndicator`; `Thinking` bool param + Idle⇄Thinking transitions in `Player.controller`
(clip loops); a `ForeseeController` GameObject with `hideDuringForesee` populated, music distortion, and
(optional) art border. All four verbs verified in play.

**Next (the open frontier):** Focus-cost cards (`CardData.focusCost`, a spend sink for banked Focus) →
enemy anti-Focus debuffs → cape-pose polish. **Open call:** roguelike vs roguelite (does The Script
persist across runs) — doesn't block; run-specific first, layer persistence later.

## Notes / Constraints
- Cards use `EffectDirector` + `EffectAnimatorHost` for visual timing.
- Combat code can't be compiled in the assistant's environment — changes are reviewed by hand;
  verify on Unity recompile.
- Reference docs: [MAP_DESIGN.md](MAP_DESIGN.md) (design intent), [MAP_INTEGRATION.md](MAP_INTEGRATION.md)
  (map→combat handoff), [SYSTEM_MAP.md](SYSTEM_MAP.md), [PROJECTSUMMARY.md](PROJECTSUMMARY.md).
