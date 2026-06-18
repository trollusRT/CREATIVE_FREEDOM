# Map Design — The Three Acts

> **What this is:** the *design intent* for the run/map layer — the goal of each map, the
> node types, and how **Insights** work. For the *technical* handoff (how a node actually loads
> a fight), see [MAP_INTEGRATION.md](MAP_INTEGRATION.md).
>
> **Status:** design doc. The combat/receiving side exists; the map, nodes, Insights, Shop, and
> Rest are **not built yet** — this is the target we're building toward.

Junior is an artist, and her world is being unmade by **The Things that Aren't** — creatures of
absence and colorlessness. A run is her journey from her cabin to the heart of that erasure, told
across **three maps (acts)**. The throughline is **color & creation vs. erasure**: the world
visibly desaturates as you climb, and Junior's power is her ability to *combine* her art (fusion
recipes) and hold onto inspiration (Insights) in the face of nothing.

---

## The run loop

Slay-the-Spire-style **branching** maps, hand-authored first. The player climbs from the bottom,
picks one node per row along connected paths, and ends each map at a boss — then on to the next
map. (Branch/connection model and scene wiring live in [MAP_INTEGRATION.md](MAP_INTEGRATION.md).)

Persistent run state — **HP, learned fusion recipes, Insights, gold** — lives on `RunManager` and
carries across every fight. **Defeat ends the run.**

---

## Node types

| Node | What it is | Reward |
|------|------------|--------|
| **Combat Stage** | Typical fight: 1–2 basic enemies, rarely a unique (MEI-I / Ms. Remember). | A *choice* of a fusion recipe **or** an Insight. |
| **Dire Stage** | A harder fight: two combat waves, or one wave of 3 enemies. | Stronger recipes + Insights. |
| **Shop** | Spend gold. | Buy buffs — max HP, fusion recipes, Insights. |
| **Rest** | Recover and breathe. | Heal to full, **or** talk to **Snack** (the worm friend). A painting minigame or short worldbuilding dialogue (minigame deferred). |
| **Boss Stage** | The act's finale: a powerful unique boss. | Epic-tier rewards; gates the next map. |

> Dire's "two waves" needs `EncounterData` to support **multi-wave** — today one encounter = one
> fight. Cheapest path: an `EncounterData.nextWave` chain the spawner walks before declaring victory.

---

## The three maps (acts)

### Act I — The Forest · *departure / discovery*
Junior busts down her cabin door and sets out to eradicate The Things that Aren't. This is the
on-ramp: the world is still fully colored, fights are gentle, Rests are generous.
- **Goal:** teach the loop, establish tone and stakes, hand the player their first recipes/Insights.
- **Signature — discovery.** Overgrown nodes whose type is hidden until an adjacent node is cleared;
  a guaranteed "first recipe" node; occasional **memory nodes** (cabin flashbacks — *why* she's doing this).

### Act II — The Riverside · *exposure / aftermath*
Out in the open, Junior is vulnerable to stronger enemies and sees first-hand what The Things did to
the world. Color is visibly draining into the water.
- **Goal:** raise difficulty and stakes; deliver the emotional gut-punch of the ruined world.
- **Signature — exposure & risk.** Rests can't fully heal (no safe shelter in the open);
  **fog / ambush nodes** hide the encounter until you arrive; the river forces branching (pick your
  crossings, fewer safe paths); **aftermath nodes** are pure worldbuilding.

### Act III — The Colorless City · *desperation / erasure*
Junior reaches the heart of the erasure to face The Things that Aren't, now desperate to destroy her.
The hardest enemies, near-monochrome; color exists only where Junior stands.
- **Goal:** the climax — a hostile map that fights back, ending at the final boss.
- **Signature — the map erases itself.** Taking one path collapses an adjacent one (no backtracking,
  every fork final); fewest Rests; enemies that attack your *build*, briefly **erasing a recipe or
  Insight** until you win the fight.

---

## How Insights work

**Insights** (the in-world term is **Inspirations**) are **permanent, passive, per-run buffs** —
relics, essentially. They are one of Junior's two power axes; the other is her **learned fusion
recipes**. Thematically they're fragments of creativity that The Things that Aren't can't take from her.

**Content source of truth:** the **Inspirations tab in `Cards.xlsx`** (≈186 designed so far). Each
row is one Insight: name, **rarity** (Common / Uncommon / Rare / Epic), **trigger** ("When/How it
Triggers"), **effect**, **stacking rule**, **synergy tags**, **drop source**, and **balancing knobs**.

**How they're acquired:**
- **Combat Stage** → choose **a fusion recipe OR an Insight** — the core build tension (engine vs. passive power).
- **Dire / Boss / Elite** → stronger Insights (Rare / Epic).
- **Shop** → buy specific Insights with gold.
- Special drops (Eldritch boss / shop) gate the wildest Epics.

**How they'll work in combat (proposed — mirrors `EnemyAbility`):**
The cleanest fit is a pattern already in the codebase. `EnemyAbility` is an abstract MonoBehaviour
with lifecycle hooks (`OnBattleStart`, `TryTakeTurn`, `OnActed`, `OnTookDamage`, `OnDied`) that
`Enemy` collects and broadcasts. Insights want the mirror image for the player:

- An **`Insight` ScriptableObject** = the data (name, rarity, trigger, tuning numbers), authored from the sheet.
- A runtime **insight host** the player owns, collecting active Insights and broadcasting hooks at the
  moments the sheet's triggers describe:
  `OnCombatStart`, `OnTurnStart` / `OnTurnEnd`, `OnPlayCard(card)`, `OnFuse(a, b, result)`,
  `OnTookDamage`, `OnDealtDamage`, `OnKill`, `OnHeal`, `OnApplyStatus`.
- **`RunManager`** owns the *run's* list of Insights (alongside learned recipes); each combat
  **re-applies** them, so they persist across the map↔combat boundary.

Most sheet effects reuse systems that **already exist** — AP, HP/maxHP, Shield, Regen, Poison,
Corrode, AttackBreak, DoubleDamage, Counter/Reflect, fusion, and card **color** (the `cardType`
string). A minority assume systems **not yet built** — a draw pile (for *Scry* / "top of deck"),
"Paint" (temp HP), and the shop economy; those are tracked as their own work, not blockers for the map.

**Stacking:** most Insights stack with per-row rules from the sheet ("+1 AP each copy", "+2 dmg",
etc.); some are unique ("Does not stack"). The runtime host honors each Insight's own rule.

---

## Theme cheat-sheet

- **Color / creation** = Junior, her cards (paint colors), fusion, Insights, the lush Forest. The
  project is literally *CREATIVE_FREEDOM*.
- **Erasure / nothing** = The Things that Aren't, Ms. Remember's "Forgotten" cards and Decaying Mind,
  the desaturating world, the Colorless City.
- The world loses color Act I → III; Insights and recipes are how Junior pushes back.

---

*Related: [MAP_INTEGRATION.md](MAP_INTEGRATION.md) (map→combat handoff), [SYSTEM_MAP.md](SYSTEM_MAP.md)
(script responsibilities), [PROJECTSUMMARY.md](PROJECTSUMMARY.md) (combat loop).*
