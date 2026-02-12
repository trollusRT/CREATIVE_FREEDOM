\# Bug Log



\## High Priority

\- Hover ratchet bump:

&nbsp; - Card lifts on hover, pointer exits mid-tween, card returns down, then re-enters and repeats.

&nbsp; - Likely caused by collider / raycast area moving relative to pointer.



\- Turn pacing:

&nbsp; - Enemy turn begins while player animations still playing → overlaps/queues visuals.



\## Previously Resolved

\- Card fading to invisible on hover (alpha interference)

\- Card click not firing because Raycast Target on CardImage “ate” the hit

\- Missing animator trigger parameter for Y-Spray

\- Status tick removal bug (out-of-range from list removal during iteration)



\## Git / Repo Notes

\- GitHub push failed due to >100MB file in Linux build folder (`sharedassets0.assets.resS`).

\- Removed build artifacts and/or moved large assets to Git LFS.



