# Map → Combat Integration Guide

A starting point for building the **map stage** and its **nodes** so they hand off into the
existing data-driven combat system. The combat/receiving side is already built (Phases 1–3 of
the enemy-loading work); this doc is about the map/sending side that doesn't exist yet.

> TL;DR — the entire handoff is one call:
> `RunManager.Instance.GoToEncounter(encounterAsset, "Dire Stage")`.
> A map node picks an `EncounterData`, that loads the combat scene, and `EnemySpawner` builds
> the fight. You mostly need to author nodes and decide how combat returns to the map.

---

## 1. What already exists (combat side — don't rebuild)

| Piece | File | Role |
|-------|------|------|
| `RunManager` | [Assets/Assets/Managers/RunManager.cs](Assets/Assets/Managers/RunManager.cs) | DontDestroyOnLoad singleton; carries `nextEncounter` across scene loads. `GoToEncounter()` helper. |
| `EncounterData` | [Assets/Assets/Managers/CombatScene/EncounterData.cs](Assets/Assets/Managers/CombatScene/EncounterData.cs) | One fight. `Fixed` roster or `RandomFromPool`. `ResolveEnemies()` returns the final list. |
| `EnemyPool` | [Assets/Assets/Managers/CombatScene/EnemyPool.cs](Assets/Assets/Managers/CombatScene/EnemyPool.cs) | Weighted set of `EnemyData` for random mob fights. |
| `EnemyData` | [Assets/Assets/Managers/CombatScene/EnemyData.cs](Assets/Assets/Managers/CombatScene/EnemyData.cs) | Per-enemy stats/art/audio/prefab + faceoff portrait + special-mechanic flags. |
| `EnemySpawner` | [Assets/Assets/Managers/CombatScene/EnemySpawner.cs](Assets/Assets/Managers/CombatScene/EnemySpawner.cs) | Reads the encounter, instantiates prefabs into slots, wires HUD, returns the list. |
| `BattleManager` | [Assets/Assets/Managers/BattleManager.cs](Assets/Assets/Managers/BattleManager.cs) | Runs the fight. Spawns in `BeginIntroAndBattle`; victory/defeat in `HandleVictory`/`HandleDefeat`. |

`EnemySpawner.ResolveEncounter()` priority: **`PendingEncounter` → `RunManager.nextEncounter` → `fallbackEncounter`**.
The `fallbackEncounter` is only for Playing the combat scene standalone (no map).

---

## 2. The end-to-end flow

```
[Map scene]
  player clicks a node
    → RunManager.Instance.GoToEncounter(node.encounter, "Dire Stage")
        sets RunManager.nextEncounter
        SceneManager.LoadScene("Dire Stage")   (RunManager persists)

[Combat scene]
  BattleManager.BeginIntroAndBattle
    → EnemySpawner.SpawnForBattle()
         ResolveEncounter() → RunManager.nextEncounter
         ResolveEnemies()   → fixed list OR weighted roll
         instantiate prefabs, Enemy.Init(data), wire HUD
    → intro stinger builds faceoff from the spawned enemies' EnemyData
    → reveal → BeginBattle → fight

  on victory  → return to map (TODO — see §5)
  on defeat   → game over / back to menu (TODO — see §5)
```

---

## 3. Building the map — recommended starting approach

Start **authored, not procedural**: a scene with hand-placed node objects, each pointing at an
`EncounterData` asset. You can layer procedural generation on later without touching combat.

### 3a. Scene + persistence

- Make a `Map` scene. Add both `Map` and `Dire Stage` to **File → Build Settings**.
- Put a `RunManager` in the map scene (or a tiny bootstrap scene loaded first). It survives the
  jump into combat. The singleton guard means a stray copy elsewhere harmlessly self-destructs.
- Today `TestFlowController` shows a start screen and reloads the combat scene on win/lose. When
  the map exists, that role shifts to the map: the start screen becomes a main menu that loads
  `Map`, and combat returns to `Map` instead of reloading itself (see §5).

### 3b. A node component (sketch)

```csharp
using UnityEngine;
using UnityEngine.UI;

// Drop on each map node (a Button). Assign the EncounterData it starts.
[RequireComponent(typeof(Button))]
public class MapNode : MonoBehaviour
{
    public enum NodeKind { Combat, Elite, Boss, Shop, Rest, Event }

    public NodeKind kind = NodeKind.Combat;
    public EncounterData encounter;          // which fight this node starts
    [Tooltip("Combat scene to load. Must be in Build Settings.")]
    public string combatScene = "Dire Stage";

    [Header("State")]
    public bool cleared;                     // set true after you win this node
    public MapNode[] unlocks;                // nodes that become available after this one

    void Awake() => GetComponent<Button>().onClick.AddListener(OnClicked);

    void OnClicked()
    {
        if (kind == NodeKind.Combat || kind == NodeKind.Elite || kind == NodeKind.Boss)
        {
            if (encounter == null) { Debug.LogWarning($"{name}: no encounter assigned."); return; }
            RunManager.Instance.GoToEncounter(encounter, combatScene);
        }
        else
        {
            // Shop / Rest / Event: open the relevant panel instead of loading combat.
        }
    }
}
```

Non-combat node kinds are included so the map model is future-proof, even if you only implement
Combat first.

### 3c. Map data model

- **Authored graph (recommended first):** place `MapNode` objects in the scene, link `unlocks`
  to form the path. Simple, debuggable, enough for a vertical slice.
- **Procedural later:** generate a node graph at runtime and assign `EncounterData`/`EnemyPool`
  per node. The combat handoff is identical — only node creation changes.

Authoring fights: a fixed boss = an `EncounterData` in `Fixed` mode with its `EnemyData` list;
a random mob room = `EncounterData` in `RandomFromPool` mode with an `EnemyPool` and a
`minCount`/`maxCount` (capped by the spawner's 3 slots).

---

## 4. RunManager as the run-state home

Right now `RunManager` only holds `nextEncounter`. It's the natural place to track everything
that must persist across the map↔combat boundary. Add fields here as needed, e.g.:

```csharp
// On RunManager, as the meta layer grows:
public int playerCurrentHP = -1;     // -1 = full; carry HP between fights
public int playerMaxHP = 100;
public List<CardData> deck;          // the run's deck
public string mapReturnScene = "Map";
public readonly HashSet<string> clearedNodeIds = new();
```

Combat would read `playerCurrentHP` on spawn and write it back on victory; the map would read
`clearedNodeIds` to show progress.

---

## 5. Returning from combat (the main TODO)

`BattleManager.HandleVictory()` currently ends with `// TODO: next scene / rewards`, and
`TestFlowController.Co_EndFight()` reloads the **current** (combat) scene. For a real map you
want victory to go **back to the map**, and defeat to go to a game-over / menu.

Decision to make: keep `TestFlowController` for standalone testing and branch when a map run is
active. A minimal approach:

```csharp
// In the victory path, prefer returning to the map when a run is in progress:
if (RunManager.Instance != null && !string.IsNullOrEmpty(RunManager.Instance.mapReturnScene))
{
    // optionally: RunManager.Instance.playerCurrentHP = player.GetHP();
    //             mark the node cleared, grant rewards, etc.
    SceneManager.LoadScene(RunManager.Instance.mapReturnScene);
}
else
{
    // existing TestFlowController behaviour (reload combat scene)
}
```

Open questions to settle when you get here:
- **Map shape:** linear, or branching (Slay-the-Spire style)?
- **Persistence:** does HP/deck carry between fights? (If yes, fill in §4.)
- **Rewards:** card rewards / shop after a win — where in the return flow?
- **Defeat:** back to map node, or end the run to a menu?

---

## 6. First milestone checklist

1. Create a `Map` scene; add `Map` + `Dire Stage` to Build Settings.
2. Add a `RunManager` to the map scene.
3. Make 2 `EncounterData` assets (one fixed boss, one `RandomFromPool` mob room).
4. Add 2 `MapNode` buttons, assign their encounters.
5. Click a node → it loads `Dire Stage` and the right fight spawns (faceoff + HUD already work).
6. Wire the victory return (§5) so winning comes back to `Map`.

After that you have a working loop and can iterate on map shape, rewards, and run-state.

---

*Related: [SYSTEM_MAP.md](SYSTEM_MAP.md) (script responsibilities), [PROJECTSUMMARY.md](PROJECTSUMMARY.md) (combat loop).*
