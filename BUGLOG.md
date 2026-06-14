\# Bug Log


\## Previously Resolved

\- Card fading to invisible on hover (alpha interference)

\- Card click not firing because Raycast Target on CardImage “ate” the hit

\- Missing animator trigger parameter for Y-Spray

\- Status tick removal bug (out-of-range from list removal during iteration)

\- Enemy turn begins while player animations still playing → overlaps/queues visuals.

\- Card lifts on hover, pointer exits mid-tween, card returns down, then re-enters and repeats.


\## Resolved — Card Pass (2026-06-13)

\- Card costs/values had drifted from the design sheet (Cards.xlsx → "Current Cards", now the source of truth); reconciled all costs, Imaginary Paint damage, and Restore healing.

\- Corrode existed as a card asset but was missing from CardDatabase, so it never appeared in play; registered it.

\- Defensive Stance was implemented as an enemy-targeted attack; corrected to a player self-buff (Player already had the "reduce damage to 1" status handling).

\- Attack cards whose VFX trigger didn't exist on the animator (Attack Break, Siphon, Sleep, Corrode, Finishing Touch, Red Splatter) silently applied no effect — damage/Hurt fire from an animation event inside the VFX clip, which never played. EffectAnimatorHost now fires the armed impact immediately when the trigger/clip is missing (also stops the "Parameter X does not exist" warnings).

\- Many cards never played their VFX (player buffs only fired a generic "Buff"; Poison/Toxic Paint played nothing). Wired them to their FXPlayer/FXSingleTarget clips and generated the missing clips from existing sprite sheets.


\## Git / Repo Notes

\- GitHub push failed due to >100MB file in Linux build folder (`sharedassets0.assets.resS`).

\- Removed build artifacts and/or moved large assets to Git LFS.



