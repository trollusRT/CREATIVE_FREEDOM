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

\- SpawnEncounterIfNeeded(): builds `enemies` from EnemySpawner at battle start (kept null-safe + backward compatible)



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

\- Init(EnemyData, player, audio) / ApplyData(): data-driven setup; falls back to Inspector values when no data assigned



\## Player.cs

\- HP, Heal(), SpendAP(), TakeDamage()

\- activeEffects list and ProcessStatusEffects()

\- Plays hurt/buff/attack anim triggers



\## EnemyData.cs (ScriptableObject)

\- Authored "stat card" per unique enemy (mirrors CardData)

\- Holds: prefab ref, stats, HP-band portraits, audio, special-mechanic flags

\- Enemy.Init() copies these into runtime fields at spawn



\## EncounterData.cs (ScriptableObject)

\- Defines one fight: ordered list of EnemyData (+ optional music override)

\- A map node selects an encounter; the spawner builds the fight from it



\## EnemySpawner.cs

\- Instantiates an encounter's enemy prefabs into scene slot anchors (up to 3)

\- EnemySlot groups {anchor, hpText, hpFollower}: wires the shared HUD HP text (FollowWorldTargetUI) to each spawned enemy; hides unused slots

\- Calls Enemy.Init() (stats + player/audio refs), returns the live list to BattleManager

\- Falls back to scene-placed enemies if no encounter/slots; `PendingEncounter` is the Phase 3 map hook

\- BattleManager spawns in BeginIntroAndBattle (before the stinger); BattleIntroStinger.BuildCastFromEnemies() pulls faceoff portraits/names from EnemyData (slidePortrait)



\## EnemyAbility.cs + Abilities/ (Phase 2)

\- Pluggable special mechanics as components on the enemy prefab; Enemy collects them (EnsureAbilities) and broadcasts hooks: OnBattleStart / TryTakeTurn / OnActed / OnTookDamage / OnDied

\- PanicHealAbility (MEI-I, OnTookDamage), DecayingMindAbility (Ms. Remember, OnActed), CriticalEyeAbility (TryTakeTurn telegraph → big hit next turn)

\- EnemyIntent / IntentKind: Enemy.CurrentIntent describes the planned action (for a future telegraph UI)

\- Legacy EnemyData flags bridge into PanicHeal/DecayingMind components at battle start; Critical Eye is attached to the prefab manually



\## RunManager.cs (Phase 3)

\- DontDestroyOnLoad singleton; carries `nextEncounter` from the map into the combat scene

\- `GoToEncounter(encounter, sceneName)`: a map node sets the encounter and loads combat

\- EnemySpawner.ResolveEncounter priority: PendingEncounter → RunManager.nextEncounter → fallbackEncounter

\- Optional for standalone combat testing (spawner falls back); future home for run state (HP/deck/map)



\## EnemyPool.cs + EncounterData modes (Phase 3)

\- EnemyPool: weighted list of EnemyData with Roll() for random mob fights

\- EncounterData.mode = Fixed (authored list) | RandomFromPool (pool + min/maxCount)

\- EncounterData.ResolveEnemies() returns the final roster; spawner builds from it



