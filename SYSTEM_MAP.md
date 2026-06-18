\# System Map (Scripts + Responsibilities)



\## BattleManager.cs

\- Owns combat state machine: START → PLAYER\_TURN → ENEMY\_TURN → TURN\_END → VICTORY/DEFEAT

\- Tracks AP (`playerAP`)

\- Calls:

&nbsp; - Player.ProcessStatusEffects() (at player-turn start)

&nbsp; - Enemy.TickTurnStartStatuses() (at each enemy's own turn start; Sleep/Stun consumed on action attempt)

&nbsp; - HandManager.DrawHand() / DiscardHand()

&nbsp; - FusionController.OnTurnEnded() (clears fusion slots)

\- Victory/defeat cinematics + music control



\## Card.cs

\- Handles UI interaction:

&nbsp; - hover lift tween

&nbsp; - drag/drop targeting via Physics2D raycast

&nbsp; - click-to-fusion selection

\- Validates AP before applying effects

\- Calls EffectDirector to play animation and arms impact callback for gameplay effects

\- Destroys card after play (effects still apply via armed impact callback)



\## EffectDirector.cs

\- Routes effect requests to proper animator host:

&nbsp; - Enemy ST host (child on enemy) OR fallback host

&nbsp; - aoeHost (mid-screen animator)

\- Maps EffectKey → animator trigger string

\- Provides type tint colors (CardData.cardType → Color)



\## EffectAnimatorHost.cs

\- Plays animator trigger

\- Can “ArmImpact” to execute callback on impact frame via animation event



\## Enemy.cs

\- HP, TakeDamage(), Die()

\- activeEffects list and ProcessStatusEffects()

\- TakeTurn() executes enemy AI actions

\- Status helpers: ApplyPoison(), ApplySleep(), etc.



\## Player.cs

\- HP, Heal(), SpendAP(), TakeDamage()

\- activeEffects list and ProcessStatusEffects()

\- Plays hurt/buff/attack anim triggers



