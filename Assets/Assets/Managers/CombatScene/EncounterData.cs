using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Defines a single fight. Either a fixed authored roster, or a random roll from an
/// <see cref="EnemyPool"/> for mob encounters. A map node selects an EncounterData and the
/// combat scene's EnemySpawner builds the fight from <see cref="ResolveEnemies"/>.
/// </summary>
[CreateAssetMenu(fileName = "NewEncounter", menuName = "Enemies/Encounter")]
public class EncounterData : ScriptableObject
{
    public enum EncounterMode { Fixed, RandomFromPool }

    public string encounterId;

    [Tooltip("Fixed = use the authored 'enemies' list. RandomFromPool = roll from 'pool'.")]
    public EncounterMode mode = EncounterMode.Fixed;

    [Header("Fixed roster")]
    [Tooltip("Enemies to spawn, in slot order (index 0 -> slot 0). Capped by the spawner's slot count.")]
    public List<EnemyData> enemies = new List<EnemyData>();

    [Header("Random roster (mob fights)")]
    [Tooltip("Weighted pool to roll from when mode is RandomFromPool.")]
    public EnemyPool pool;
    [Tooltip("Inclusive range for how many enemies to roll (still capped by spawner slots).")]
    [Min(0)] public int minCount = 1;
    [Min(0)] public int maxCount = 3;

    [Header("Misc")]
    [Tooltip("Optional music override for this encounter (reserved; unused).")]
    public AudioClip musicOverride;

    /// <summary>
    /// The actual enemies for this fight: the fixed list, or a fresh weighted roll from the
    /// pool. Rolled per call, so each visit to a random node can differ.
    /// </summary>
    public List<EnemyData> ResolveEnemies()
    {
        if (mode == EncounterMode.RandomFromPool && pool != null)
        {
            int lo = Mathf.Max(0, Mathf.Min(minCount, maxCount));
            int hi = Mathf.Max(minCount, maxCount);
            int count = Random.Range(lo, hi + 1);

            var rolled = new List<EnemyData>(count);
            for (int i = 0; i < count; i++)
            {
                var e = pool.Roll();
                if (e != null) rolled.Add(e);
            }
            return rolled;
        }

        // Fixed (default).
        return enemies ?? new List<EnemyData>();
    }
}
