# CREATIVE_FREEDOM — Project Summary

## One-line pitch
A turn-based roguelike deckbuilder where paint-themed cards (colors) can be fused into stronger spells, with tight AP management and status-driven combat.

---

## Unity / Project Notes
- Engine: Unity 6 (6000.0.42f1)
- Combat is UI-driven (drag cards) + 2D colliders for targets.
- Repo uses Git LFS for large assets.

---

## Combat Loop (Authoritative)
1. **Turn Start**
   - Status effects tick for Player and Enemies (poison, regen, etc.)
   - Dead enemies pruned.
   - If all enemies dead → victory flow.

2. **Player Phase**
   - Player starts with **AP_PER_TURN** (BattleManager constant).
   - Player plays cards by dragging to targets or clicking for fusion selection.
   - Cards spend AP (BattleManager.UseAP).
   - Turn ends when AP hits 0 (or Pass costs 1 AP).

3. **Enemy Phase**
   - Enemies act in sequence.
   - After each enemy action: check player death / victory.
   - After loop: prune dead → start next player turn.

4. **Turn End**
   - Hand discarded.
   - Fusion slots cleared.
   - Next turn begins.

---

## Core Systems

### AP System
- BattleManager tracks `playerAP`.
- Cards should never apply effects if AP is insufficient.
- Fusion costs **1 AP** (intended rule).
- Pass costs **1 AP**.

### Hand System
- HandManager draws and discards.
- Cards remove themselves from hand when dragged (and reinsert if returned).

### Card Interaction
- Cards support:
  - Hover lift (visual tween)
  - Drag to target (2D raycast)
  - Click to select for FusionController
- CanvasGroup used to control raycast blocking during drag.
- Known gotcha: UI Images can “eat” raycasts if Raycast Target is enabled.

### Targeting + Highlighting
- Raycast uses `Camera.main.ScreenToWorldPoint` → `Physics2D.Raycast`.
- Single-target highlight: `TargetHighlighter` on hovered target.
- AoE highlight: all enemies when hovering any enemy (for AoE cards).

### Effect / Animation System
- Card plays visuals through `EffectDirector`.
- `EffectDirector` routes to `EffectAnimatorHost`:
  - **Enemy ST host** (attached to enemy) for single target impacts
  - **aoeHost** (mid-screen animator) for AoE / shared animations
- Impact timing: `EffectAnimatorHost.ArmImpact(callback)` executes gameplay effects on the animation’s impact frame.

### Status Effects
- Both Player and Enemy maintain `activeEffects` list.
- Player statuses tick in `Player.ProcessStatusEffects()` at the start of the player's turn.
- Enemy statuses tick in `Enemy.TickTurnStartStatuses()` at the start of each enemy's own turn (so DoT "ticks before actions" and can kill before the enemy attacks). Sleep/Stun are NOT decayed there — they're consumed on the action attempt via `Enemy.ConsumeSleepOrStunIfPresent()`.
- Known prior bug: list removal during iteration caused out-of-range. (Use reverse for loop or snapshot.)

---

## Implemented / In-progress Cards (as of this handoff)
- Single Target: Red Stroke, Siphon, Attack Break, Corrode, Finishing Touch, Crushing Paint, Imaginary Paint
- AoE: Red Splatter, Y-Spray
- Status: Poison (ST), Sleep (ST)
- Golden: Pool of Paint (poison + regen over 3 turns), Toxic Paint (power poison variant)

---

## Fusion System (Rules)
- FusionController has two slots.
- When turn ends, **fusion slots must clear** and the cards in them are discarded like hand cards.
- Fusion selection visually “dims” a card (CanvasGroup alpha) and disables dragging.

---

## Known UX Problems (worth re-checking)
- Hover “hitbox drift” / repeated lift bump:
  - Hover lift tween causes pointer exit because collider/hit area changes as the card moves.
  - Can cause “ratcheting” upward over repeated enter/exit.
- Need a stable hover target / raycast area to prevent exit during tween.

---

## Current Work Style
When debugging:
- Prefer adding `Debug.Log` on:
  - what raycast hit was detected
  - state transitions (PLAYER_TURN → ENEMY_TURN)
  - impact callbacks firing
- Prefer snapshot lists during iteration when effects can remove enemies.
