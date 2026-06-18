using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Persists across scene loads to carry the whole run's state between the map and combat.
/// A map node sets <see cref="nextEncounter"/> and loads the combat scene; the spawner reads it
/// back; on victory the combat scene writes results (HP, cleared node) back here and returns to
/// the map.
///
/// Put one in your entry/bootstrap scene (or the map). It survives scene loads via
/// DontDestroyOnLoad, and the singleton guard means a stray copy in another scene quietly
/// destroys itself. If no RunManager exists (e.g. Playing the combat scene standalone), the
/// spawner falls back to its own fallbackEncounter and combat keeps its old win/lose flow — so
/// this stays optional for isolated testing.
///
/// This is the run-state home: HP carryover, the run's learned recipes + Insights (the two power
/// axes), gold, and map progress. See MAP_DESIGN.md / MAP_INTEGRATION.md.
/// </summary>
[DisallowMultipleComponent]
public class RunManager : MonoBehaviour
{
    public static RunManager Instance { get; private set; }

    [Header("Handoff")]
    [Tooltip("Encounter the combat scene should load next. A map node sets this before loading Combat.")]
    public EncounterData nextEncounter;

    [Header("Run state (persists map <-> combat)")]
    [Tooltip("True while a run is in progress. Combat reads/writes run state only when this is set.")]
    public bool runActive;

    [Tooltip("Junior's max HP for this run. Combat applies it to the Player on spawn.")]
    public int playerMaxHP = 20;

    [Tooltip("Junior's current HP, carried between fights. -1 means 'full' (uninitialized).")]
    public int playerCurrentHP = -1;

    [Tooltip("Run currency for the Shop.")]
    public int gold;

    [Header("Build (the run's two power axes)")]
    [Tooltip("Fusion recipes learned this run (by FusionRecipe.recipeName). Applied to FusionBook on combat load.")]
    public List<string> unlockedRecipes = new();

    [Tooltip("Insights held this run (by id/name). Applied to the player on combat load. See MAP_DESIGN.md.")]
    public List<string> insights = new();

    [Header("Map progress")]
    [Tooltip("Scene to return to after a fight — set automatically to the map you came from.")]
    public string mapReturnScene;

    [Tooltip("Scene loaded on defeat (game over / menu). Leave empty to fall back to the old defeat flow until it exists.")]
    public string gameOverScene = "";

    [Tooltip("The node currently being resolved (set when you click it; marked cleared on victory).")]
    public string currentNodeId;

    [Tooltip("Ids of map nodes already cleared this run.")]
    public List<string> clearedNodeIds = new();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>Reset everything for a brand-new run. Call from the main menu / map entry.</summary>
    public void StartNewRun(int maxHP)
    {
        runActive = true;
        playerMaxHP = Mathf.Max(1, maxHP);
        playerCurrentHP = playerMaxHP;
        gold = 0;
        unlockedRecipes.Clear();
        insights.Clear();
        clearedNodeIds.Clear();
        currentNodeId = null;
        nextEncounter = null;
    }

    public bool IsNodeCleared(string id)
        => !string.IsNullOrEmpty(id) && clearedNodeIds.Contains(id);

    public void MarkNodeCleared(string id)
    {
        if (!string.IsNullOrEmpty(id) && !clearedNodeIds.Contains(id))
            clearedNodeIds.Add(id);
    }

    public void LearnRecipe(string recipeName)
    {
        if (!string.IsNullOrEmpty(recipeName) && !unlockedRecipes.Contains(recipeName))
            unlockedRecipes.Add(recipeName);
    }

    /// <summary>Pick the encounter and jump to the combat scene. Call this from a map node.</summary>
    public void GoToEncounter(EncounterData encounter, string combatSceneName, string nodeId = null)
    {
        nextEncounter = encounter;
        if (!string.IsNullOrEmpty(nodeId)) currentNodeId = nodeId;

        // Remember the map we're leaving so victory can bring us back to it (works for all 3 acts).
        var active = SceneManager.GetActiveScene().name;
        if (!string.IsNullOrEmpty(active)) mapReturnScene = active;

        if (!string.IsNullOrEmpty(combatSceneName))
            SceneManager.LoadScene(combatSceneName);
    }
}
