using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// All authored Insights, indexed by id. Lets the InsightHost turn the run's
/// <see cref="RunManager.insights"/> (a list of id strings) back into <see cref="Insight"/> assets.
/// Mirrors <see cref="CardDatabase"/>.
/// </summary>
[CreateAssetMenu(fileName = "InsightDatabase", menuName = "Insights/Insight Database")]
public class InsightDatabase : ScriptableObject
{
    public List<Insight> allInsights = new();

    // Built lazily from allInsights; rebuilt on demand if an entry is missing.
    private Dictionary<string, Insight> index;

    private void OnEnable() => index = null; // force a rebuild when entering play/edit domain

    private void BuildIndex()
    {
        index = new Dictionary<string, Insight>();
        foreach (var ins in allInsights)
        {
            if (ins == null || string.IsNullOrEmpty(ins.id)) continue;
            index[ins.id] = ins;
        }
    }

    /// <summary>Resolve an Insight by its id, or null if it isn't in the database.</summary>
    public Insight Find(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (index == null) BuildIndex();
        if (index.TryGetValue(id, out var found)) return found;

        // Tolerate assets added at runtime / out of order: one rebuild before giving up.
        BuildIndex();
        return index.TryGetValue(id, out found) ? found : null;
    }
}
