\# Bug Log


\## Previously Resolved

\- Card fading to invisible on hover (alpha interference)

\- Card click not firing because Raycast Target on CardImage “ate” the hit

\- Missing animator trigger parameter for Y-Spray

\- Status tick removal bug (out-of-range from list removal during iteration)

\- Enemy turn begins while player animations still playing → overlaps/queues visuals.

\- Card lifts on hover, pointer exits mid-tween, card returns down, then re-enters and repeats.


\## Git / Repo Notes

\- GitHub push failed due to >100MB file in Linux build folder (`sharedassets0.assets.resS`).

\- Removed build artifacts and/or moved large assets to Git LFS.



