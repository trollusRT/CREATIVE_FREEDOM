# Current Task

## 🎯 Goal: Build the Insights runtime system

**Start the Insights system — the runtime that makes Inspirations actually fire in combat.**
Do this **before** the reward system that grants them (an Insight reward is meaningless until
Insights can *do* something).

**What Insights are:** permanent, passive, per-run buffs (relics). They're one of Junior's two
power axes alongside learned fusion recipes. See the **"How Insights work"** section of
[MAP_DESIGN.md](MAP_DESIGN.md) for the full design. Content (~230 designed) lives in the
**"Inspirations"** tab of `Cards.xlsx` (rarity, trigger, effect, stacking, etc.).

### Approach (proposed — mirrors `EnemyAbility`)
The cleanest fit is the pattern already in the codebase. `EnemyAbility` is an abstract
MonoBehaviour with lifecycle hooks that `Enemy` collects and broadcasts. Insights want the mirror
image for the player:

1. **`Insight` ScriptableObject** = the data (name, rarity, trigger, tuning numbers), authored from the sheet.
2. **Insight host** the player owns — collects active Insights and broadcasts hooks at the moments
   the sheet's triggers describe:
   `OnCombatStart`, `OnTurnStart` / `OnTurnEnd`, `OnPlayCard(card)`, `OnFuse(a, b, result)`,
   `OnTookDamage`, `OnDealtDamage`, `OnKill`, `OnHeal`, `OnApplyStatus`.
3. **`RunManager.insights`** (already a `List<string>`) holds the run's set; each combat **re-applies**
   them, so they persist across the map↔combat boundary.

### Where it plugs in
- `BattleManager` broadcasts the hooks at the right moments (turn start/end, card played, kill, etc.).
- `Player` already has reactive-effect fields (doubleDamage charges, counters, reflect…) — a good
  pattern to mirror for Insight effects.
- `FusionController` is the place for the `OnFuse` hook.
- Most sheet effects reuse systems that already exist (AP, HP/maxHP, Shield, Regen, Poison, Corrode,
  AttackBreak, DoubleDamage, Counter/Reflect, fusion, card color via `cardType`). A minority need
  systems not built yet (draw pile for *Scry*, "Paint" temp HP) — skip those for the first pass.

### Suggested first slice
Stand up the `Insight` SO + host + a couple of hooks (`OnCombatStart`, `OnTurnStart`, `OnPlayCard`),
implement **3–4 simple Insights end-to-end** (e.g. Quick Drying = +1 AP at turn start; Sturdy Easel =
3 Shield at combat start; Critique = +1 damage above 66% HP), and verify they fire. Then widen hook
coverage.

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
