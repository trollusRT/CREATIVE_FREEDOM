using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Persists across scene loads to carry run state from the map into the combat scene.
/// Right now that's just <see cref="nextEncounter"/>: a map node sets it, then loads the
/// combat scene, where EnemySpawner reads it back.
///
/// Put one in your entry/bootstrap scene (or the map). It survives scene loads via
/// DontDestroyOnLoad, and the singleton guard means a stray copy in another scene quietly
/// destroys itself. If no RunManager exists (e.g. Playing the combat scene standalone), the
/// spawner falls back to its own fallbackEncounter, so this is optional for testing.
///
/// Natural home for future run state too — player HP carryover, the current deck, map
/// position, rewards — add fields here as the meta layer grows.
/// </summary>
[DisallowMultipleComponent]
public class RunManager : MonoBehaviour
{
    public static RunManager Instance { get; private set; }

    [Tooltip("Encounter the combat scene should load next. A map node sets this before loading Combat.")]
    public EncounterData nextEncounter;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>Pick the encounter and jump to the combat scene. Call this from a map node.</summary>
    public void GoToEncounter(EncounterData encounter, string combatSceneName)
    {
        nextEncounter = encounter;
        if (!string.IsNullOrEmpty(combatSceneName))
            SceneManager.LoadScene(combatSceneName);
    }
}
