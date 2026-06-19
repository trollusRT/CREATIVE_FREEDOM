# Current Task

## 🎯 Goal: Insights runtime — first slice (CODE-COMPLETE, awaiting Unity wiring)

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

### Pass 2 — widen coverage (next)
- Broadcast `OnHeal` / `OnDealtDamage` / `ApplyStatus` (host methods/enums for the first two exist;
  not broadcast yet). `OnPlayCard`/`OnFuse` already fire — just no effects authored on them.
- Add a target-aware `ModifyOutgoingDamage(enemy, dmg)` hook for damage-modifier Insights
  (Critique, Palette Knife, Deep Pigment).
- Then author the rest of the ~186 from the Inspirations tab.
- Systems still unbuilt for a minority: draw pile (*Scry* / "top of deck"), "Paint" temp HP, shop economy.

---

## Project State (map/run build)

**Done:**
- Run loop: `RunManager` run-state hub, HP carryover, victory→map / defeat→fresh-run wiring.
- Authored **branching map** (`MapNode` + `MapController`, world-space or UI nodes).
- `TestFlowController` is run-aware (start screen skipped when arriving from the map).
- **Multi-wave Dire** stages (`EncounterData.nextWave`).

**Deferred (after Insights):**
- **Post-fight reward screen** (the recipe-vs-Insight choice) — the natural next step once Insights fire.
- Shop & Rest/Snack panels.
- A real `GameOver` scene (defeat currently bounces to the map as a fresh run).
- Recipe unlocking wired through `FusionBook.learned` (machinery exists; currently bypassed by `unlockAllForTesting`).

---

## Notes / Constraints
- Cards use `EffectDirector` + `EffectAnimatorHost` for visual timing.
- Combat code can't be compiled in the assistant's environment — changes are reviewed by hand;
  verify on Unity recompile.
- Reference docs: [MAP_DESIGN.md](MAP_DESIGN.md) (design intent), [MAP_INTEGRATION.md](MAP_INTEGRATION.md)
  (map→combat handoff), [SYSTEM_MAP.md](SYSTEM_MAP.md), [PROJECTSUMMARY.md](PROJECTSUMMARY.md).
