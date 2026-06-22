# The Script & the Focus System — Design

> **What this is:** the design for Junior's turn-level *agency* layer — a small resource (**Focus**)
> she spends to bend the draw to her will, framed in-world as a cape called **The Script**. This is a
> design doc; its **first slice is now built and in-game**. It's the answer to "combat feels luck-based."
>
> **Status:** **The full first slice is built and wired in Unity** — Focus resource + economy on
> `BattleManager` (End Turn banking, `focusText`), **Remember** (right-click hand persistence), **Prime**
> (colour-biased refill via HUD buttons), and **Foresee** (`ForeseeController`: right-click Junior →
> camera push-in, looping Thinking pose, pitch-bent music, hidden HUD, optional art border; preview &
> reroll next turn's hand). Run-specific (no cross-run persistence yet). Remaining: Focus-cost cards,
> enemy anti-Focus, cape-pose polish, and the roguelike/roguelite call. Most pieces reuse systems that already exist (AP, StatusEffect, cardType colours, EnemyIntent,
> SlowMoController, the Forgotten/Decaying Mind mechanic). Combat can't be compiled in the assistant's
> environment — verify on Unity recompile.
>
> *Related: [MAP_DESIGN.md](MAP_DESIGN.md), [CURRENT_TASK.md](CURRENT_TASK.md), [SYSTEM_MAP.md](SYSTEM_MAP.md).*

---

## The problem it solves

Combat is currently **luck-based**. `HandManager.DrawHand()` clears the hand and draws **six fresh
random cost-1 cards every turn**; there's no deck, no draw pile, no carry-over (`DiscardHand` wipes
unplayed cards). Every turn is an independent dice roll from the same flat pool of ~13 commons —
nothing you did last turn shapes this one. **Fusion** is the only agency, and it's reactive (you can
only combine what RNG handed you, and can't hold a pair for next turn).

The goal: give the player **agency during their turn and the ability to strategize**, while keeping
*some* luck. Not eliminate randomness — *shape* it.

---

## The pitch

Junior earns **The Script** — a split-white cape — after Act I (The Forest). It grants "lesser reality
manipulation," which in gameplay is the **Focus** system: a small, scarce resource she spends to
**Remember** cards, **Prime** her colours, and **Foresee** her draw. Thematically, the world (The
Things that Aren't) *erases* her hand each turn; Focus is how she **defies the forgetting** and authors
what comes next. Color/creation vs. nothing — now with *authorship* as the verb.

Power comes in two flavours, which keeps the kit healthy:
- **Fusion** = tactical, draw-dependent power (combine what you drew).
- **Focus** = planned, restraint-dependent power (bank toward a deliberate moment).

---

## The Focus economy — **LOCKED**

- **+1 Focus per turn (flat).**
- **Cap 3.** **Persists** (never resets between turns).
- **Bank 2 spare AP → 1 Focus** at end of turn (AP economy is 6/turn, flat 1 AP per card — see
  [ap-one-per-card]).
- Spend ~**1 Focus** per verb (Remember / Prime / Foresee). Spendable during your turn (Focus-cost
  cards) and at end of turn (shaping verbs, which affect *next* turn).

**Cadence check** (why these numbers feel right): play flat-out (all 6 AP) and you trickle **+1/turn**;
ease off (spend 4, 2 spare) and you bank **+2**; a full turtle turn refills you toward the cap. So
baseline you get a Focus-action every couple of turns, and a deliberate breather lets you reload for a
burst — scarce, but always moving. **This is the "not always luck" dial.**

The heart of it: when you've got 2–3 Focus banked, every turn is a real decision — spend it to **fix
your draw** (Prime/Remember) or to **unleash** a Focus-cost card? Same currency, competing uses.

---

## The three verbs — **LOCKED (behaviour), numbers to tune**

- **Remember** — lock a selected card so it survives the end-of-turn erasure into next turn. Costs ~1
  Focus/card; you can remember as many as you have Focus (so the cap *is* the limiter). Remembered
  cards fill next turn's hand slots (you draw fewer fresh — built-in cost). Primary use: hold one
  fusion ingredient while you Prime/draw the other.
- **Prime** — bias next turn's refill toward a chosen colour. **Guarantees at least one card of that
  colour** and weights the rest toward it — but *which* cards of that colour you get stays luck.
  (Example: low on HP → Prime Yellow → guaranteed a Yellow next hand, hopefully a Restore or two. You
  reliably get your *shot* at healing, not a gift-wrapped perfect hand.)
- **Foresee** — peek at part of next turn's incoming hand; optionally swap/reroll one. (Depth = open;
  see below.)

No full draw pile to shuffle/manage — this is **odds manipulation, not a deck**. Deliberately *not*
Slay-the-Spire: the agency lives in-turn, not in a between-fights deckbuilding screen.

---

## Focus-cost cards — **LOCKED (concept)**

Future cards priced in **Focus** instead of (or on top of) AP. Because Focus is scarce and caps at 3,
a Focus-cost card is a *saved-up* moment — power gated behind foresight and restraint, not just an
action. This is the clean answer to the earlier "heavy hitters need a real cost" problem (see
[card-implementation-gaps]): the marquee/ultimate cards cost Focus.

- Needs a new `CardData` field (e.g. `focusCost`), checked alongside the AP check in `Card.cs`.
- **Build axis:** Insights/rewards that grant "+1 Focus cap" or "+1 Focus/turn" let a player lean into
  a Focus-heavy, premium-card style — another way the run shapes play (mirrors agency-scales-with-
  investment).
- Open: Focus-only vs AP+Focus pricing; which cards; whether a fusion *result* can also cost Focus
  (double-gated true ultimate).

---

## Enemy anti-Focus arsenal — **LOCKED (concept), content TBD**

The enemies of forgetting push back: debuffs that attack The Script. Organised by what they bite, each
with the **counterplay** that keeps it strategic rather than feel-bad. (Ms. Remember is the archetype;
many future enemies extend this.)

**Tax the income:** *Distraction* (skip next flat +1) · *Draining Gaze* (AP→Focus banking off a turn)
· *Damp* (lose 1 stored Focus/turn — spend before you bleed).
**Raid the pool:** *Siphon/Feed* (steal 1 stored Focus on hit; enemy heals/empowers) · *Narrow Mind*
(Focus cap reduced for N turns).
**Lock a verb:** *Smear* (no Prime next turn) · *Wet Erasure* (can't Remember — cash in your hand now)
· *Creative Block* (all Focus spending sealed one turn; telegraphed).
**Punish setups (elite/boss):** *Forget* (erase a Remembered card → it becomes a Forgotten card,
reusing the existing mechanic; *counter:* don't Remember while it's alive) · *Color Bleed* (scramble
your Primed colour; *counter:* Prime late).
**Corrupt (signature bosses):** *Backfire* (spending Focus also costs HP) · *False Memory* (Remember
stores a Forgotten instead — a whole fight built on distrusting your hand).

**Design principles (important):**
1. **Telegraph the heavy ones** via the existing `EnemyIntent`/`Enemy.CurrentIntent` (currently
   unused) so the player can pre-spend and play around them — converts feel-bad into a puzzle.
2. **Mostly cleansable + temporary** — live in the `StatusEffect` system so **Cleanse** /
   **Just Give Me a Second** answer them. A few boss ones can be sticky.
3. **Escalate by act** — Forest nibbles, Riverside drains, Colorless City attacks the *build* directly
   (MAP_DESIGN already calls for "erasing a recipe or Insight"). Focus-disruption intensifying Act I→III
   is the *mechanical expression of the world desaturating.*
4. **Give the player armour** — protective Insights ("Focus can't be stolen," "your Remembered card
   can't be erased," "ignore the first Focus debuff each fight"). Makes it a two-sided meta: you invest
   *against* erasure.

Framing tie-in: anti-Focus attacks are **things trying to tear or rewrite The Script**; protective
Insights *reinforce* it. The fight becomes "can Junior author reality faster than they erase it."

---

## Roguelike vs roguelite — ⚠️ **KEY OPEN DECISION**

Two separate axes — don't conflate:
- *When in a run* you get Focus → **after Act I** (locked).
- *Whether The Script persists across runs* → **the open call.**

**Recommendation: roguelite-light — "keep the cape, not the canvas."** Unlock The Script *permanently*
after the first Act I clear, so future runs have Focus from the start (never replay a whole act stripped
of your most interesting system). But keep all *power* run-specific — Focus cap, income upgrades,
Insights, recipes, HP all still reset. You're unlocking a *mechanic* (like unlocking a character/base
ability), not snowballing stats, so it barely betrays the roguelike — and for a story-forward game it's
narratively cleaner (Junior doesn't lose an earned artifact every death).

The story beat survives either way: first playthrough you *gain* The Script after the Forest (the
reveal); afterward she canonically has it. **Pure roguelike alternative:** she re-finds it each climb —
maximal purity, but a Focus-less Act I on *every* attempt (tedious; you lose your signature toy for a
whole act repeatedly).

→ **Decide before building the unlock/persistence layer.** Everything else can be built run-specific
first and have persistence layered on once decided.

---

## UI & feel — **LOCKED (direction)**

- **Focus = small white paint blips beside the AP** — consistent visual language (Focus is paint, like
  the cards). Render in The Script's white; consider "wet → dry": a banked blip fills solid so the row
  visibly *builds* toward the cap of 3. Glanceable.
- **Spending Focus = a moment:** the screen focuses on Junior in a thinking pose, The Script cape
  flaring (faint glowing script). Makes Focus-spends weighty vs. the quick card plays — fits "reality
  manipulation." **Caution:** you'll spend Focus often, so keep it *snappy* (~0.5s slow-zoom + flare);
  reserve the full dramatic version for big spends (a Focus-cost card, a clutch Prime), with a subtle
  shimmer for routine ones. **Reuse `SlowMoController`** (victory cinematic already uses it) + the
  portrait system; the cape unfurl is the new art.

---

## How it maps to the three acts

- **Act I — Forest:** base loop only (cards, fusion, AP), **no Focus**. The on-ramp; teaches
  fundamentals without overload.
- **Gain The Script** → entering **Act II — Riverside:** Focus comes online exactly as difficulty ramps
  and enemies begin draining/disrupting it. Agency arrives when it's needed and earned.
- **Act III — Colorless City:** erasure attacks the *build* directly (tear the Script, erase
  recipes/Insights). Peak two-sided meta.

The mechanic's *introduction is narratively motivated*, and agency *scales with progression* — both
onboarding concerns solved by the fiction.

---

## Implementation sketch (where it plugs into current code)

- **Root change — stop the full wipe.** `HandManager.DrawHand()` currently `ClearHand()` + 6 random
  cost-1. Move to: keep Remembered cards, draw *up to* hand size. `DiscardHand` (end of turn, in
  `BattleManager.EndPlayerTurn`) should erase only *non-remembered* cards.
- **Focus resource.** Likely on `Player` (mirrors `shieldPoints`/`pendingAttackBonus`) or
  `BattleManager` (near `playerAP`/`GrantAP`). Fields: `focus` (0–`focusCap`), `focusCap=3`. Grant +1
  at turn start; bank `spareAP/2` at `EndPlayerTurn`. UI next to `playerAPText`.
- **Verbs.** Remember = mark a `Card`/hand slot as kept (a small flag + a "memory slot" UI/gesture).
  Prime = store a chosen colour; bias `DrawHand`'s pick (guarantee ≥1, weight the rest) — `DrawHand`
  already filters `cardCost==1`, so add colour weighting there. Foresee = compute next hand early and
  show it.
- **Weighted pool (run-level).** Later: a per-run colour weighting so draws reflect the build (rewards
  grant "more Red"). `DrawHand`'s random pick reads the weights.
- **Focus-cost cards.** New `CardData.focusCost`; check + spend in `Card.cs` alongside the AP check.
- **Enemy debuffs.** Add Focus-disruption to the `StatusEffect`/`StatusType` system so existing tick/
  cleanse machinery applies; new effects read/modify the Focus fields. Telegraph via `EnemyIntent`.
  Extend the existing **Forgotten**/**Decaying Mind** family for *Forget*/*False Memory*.
- **Protective Insights.** Add effectIds to `InsightHost` (Focus-grant, Focus-cap, Focus-protection) —
  the data-driven Insight system ([insights-runtime]) already supports new effectIds + a build axis.
- **The pose.** Reuse `SlowMoController` + portrait swap; new cape art/anim.

---

## Open decisions (resolve as we build)

1. **Roguelike vs roguelite** (above) — the big one.
2. ~~**Prime** exact weighting~~ — implemented as: guarantee the first fresh slot is the colour, then each
   other fresh slot has `primeBias` (default **0.5**) chance of it. Tune `HandManager.primeBias` in play.
3. ~~**Foresee** depth~~ — implemented as: peek the *whole* next fresh hand; reroll any card by clicking
   (FORESEE costs 1 Focus, each reroll 1 Focus — both tunable on `ForeseeController`).
4. **Focus-cost cards** — Focus-only vs AP+Focus; which cards; can a fusion result also cost Focus?
5. **Remember** — confirm 1 Focus/card; do remembered cards count against next hand size (recommended: yes).
6. **Animation cadence** — which spends get the full cinematic vs a shimmer.
7. **Numbers to playtest** — income/cap/banking rate, verb costs, on-ramp generosity (extra early Focus?).

---

## Suggested first slice (when we start)

Mirror how the Insights runtime was built — a small vertical slice to *feel* it before going wide.
**✅ All four steps below are now built, wired, and playable in Unity.**

1. ~~**Focus resource + UI**: +1/turn, cap 3, persist, AP-banking. Visible next to AP.~~ **✅ code-complete**
   — `BattleManager.focus`/`focusCap`/`focusPerTurn`, `GrantFocus`/`SpendFocus`/`CanSpendFocus`, +1 at
   turn start, `floor(spareAP/2)` banked in `EndPlayerTurn`, null-guarded `focusText` counter. The
   **Pass button was repurposed → End Turn** so spare AP can exist to bank. Needs Unity wiring: add a TMP
   counter and assign `focusText`; relabel the button "End Turn".
2. ~~**Remember + hand persistence**: stop wiping the hand; let one selected card carry over for 1 Focus.~~
   **✅ code-complete** — `HandManager.DrawHand` keeps Remembered cards + tops up (fewer fresh draws =
   built-in cost), `DiscardHand` erases only non-Remembered (returns the dumped count for Blank Canvas),
   `Card.ToggleRemember` (**right-click** a hand card) spends/refunds 1 Focus with a gold-tint + optional
   `rememberedIndicator`. Drag-to-play is now turn-gated (cards persist into the enemy turn). This makes
   fusion *deliberate* — the core agency payoff.
3. ~~**Prime**: colour-bias the next draw (guarantee ≥1).~~ **✅ code-complete** — `BattleManager.PrimeColor(colour)`
   (toggle/swap, 1 Focus) for HUD colour buttons; `HandManager.DrawHand` guarantees the first fresh slot is
   the primed colour and weights the rest by `primeBias` (default 0.5), consumed after the draw. Primeable
   cost-1 colours: Red, Blue, Yellow, Crimson, Purple, Golden.
4. ~~Verify the loop end-to-end (the low-HP heal-pivot), then add Foresee~~ — **✅ Foresee built & wired**
   (`ForeseeController`: right-click Junior → camera push-in, looping Thinking pose (wired in
   `Player.controller`), pitch-bent music, HUD hidden via `hideDuringForesee`, optional art border;
   FORESEE reveals next turn's hand from the dark, click cards to reroll, BACK restores everything;
   `HandManager.foreseenFresh` → `DrawHand`). Still to add: Focus-cost cards, enemy anti-Focus, cape-pose polish.

(Run-specific first; layer the roguelite persistence once that decision is locked.)
