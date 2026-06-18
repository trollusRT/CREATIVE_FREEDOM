using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// A weighted set of enemies to roll random encounters from (mob fights). Higher weight =
/// more likely. Used by EncounterData when its mode is RandomFromPool.
/// </summary>
[CreateAssetMenu(fileName = "NewEnemyPool", menuName = "Enemies/Enemy Pool")]
public class EnemyPool : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        public EnemyData enemy;
        [Min(0f)] public float weight = 1f;
    }

    public List<Entry> entries = new List<Entry>();

    /// <summary>Weighted-random pick (duplicates allowed across calls). Null if the pool is empty.</summary>
    public EnemyData Roll()
    {
        float total = 0f;
        foreach (var e in entries)
            if (e != null && e.enemy != null && e.weight > 0f) total += e.weight;
        if (total <= 0f) return null;

        float r = Random.value * total;
        foreach (var e in entries)
        {
            if (e == null || e.enemy == null || e.weight <= 0f) continue;
            r -= e.weight;
            if (r <= 0f) return e.enemy;
        }

        // Floating-point safety net: return the last valid entry.
        for (int i = entries.Count - 1; i >= 0; i--)
            if (entries[i] != null && entries[i].enemy != null) return entries[i].enemy;
        return null;
    }
}
