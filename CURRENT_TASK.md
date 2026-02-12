\# Current Task



\## Immediate Goals

1\) Fix hover “ratchet bump” (hover lift triggers pointer exit while moving)

2\) Add “hit impact” punch.wav to Junior getting hit (play alongside hurt sound)

3\) Implement proper Poison tick logic (consistent turn start tick, predictable duration)

4\) Implement “wait before enemy turn” so player animations finish before enemies act

5\) Confirm fusion costs 1 AP and fusion slots discard/clear at end of player turn



\## Project State

\- Repo: CREATIVE\_FREEDOM (private)

\- Branch being used: dev

\- Git LFS configured



\## Notes / Constraints

\- Cards use EffectDirector + EffectAnimatorHost for visual timing.

\- Enemy turn currently starts immediately after AP reaches 0 / hand empty → needs pacing delay or action queue.



