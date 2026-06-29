using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "FusionBook", menuName = "Cards/Fusion Book")]
public class FusionBook : ScriptableObject
{
    public List<FusionRecipe> allRecipes = new();

    // Learned set (by recipeName). (Not serialized�runtime only)
    public HashSet<string> learned = new();

    // Dev / testing
    [Header("Dev / Testing")]
    public bool unlockAllForTesting = true;

    // Runtime index for quick lookup
    private Dictionary<(CardData, CardData), FusionRecipe> dict;

    private void OnEnable()
    {
        // Build the lookup each time we enter play/edit domain
        BuildIndex();

        // For testing, mark everything as learned every time
        if (unlockAllForTesting)
            UnlockAll();
    }

    public void BuildIndex()
    {
        dict = new Dictionary<(CardData, CardData), FusionRecipe>();
        foreach (var r in allRecipes)
        {
            if (!r || !r.ingredientA || !r.ingredientB || !r.result) continue;

            var keyAB = (r.ingredientA, r.ingredientB);
            dict[keyAB] = r;

            if (r.orderAgnostic)
            {
                var keyBA = (r.ingredientB, r.ingredientA);
                dict[keyBA] = r;
            }
        }
    }

    public bool TryGetRecipe(CardData a, CardData b, out FusionRecipe recipe)
    {
        if (dict == null) BuildIndex();
        if (a == null || b == null) { recipe = null; return false; }

        if (dict.TryGetValue((a, b), out recipe))
            return true;

        recipe = null;
        return false;
    }

    // Always true when unlockAllForTesting is on
    public bool IsLearned(FusionRecipe r)
        => unlockAllForTesting || (r && learned.Contains(r.recipeName));

    public void Unlock(FusionRecipe r)
    {
        if (r != null) learned.Add(r.recipeName);
    }

    public void UnlockAll()
    {
        learned.Clear();
        foreach (var r in allRecipes)
        {
            if (!r) continue;
            learned.Add(r.recipeName);
        }
    }

    /// <summary>
    /// Apply the active run's learned recipes (RunManager.unlockedRecipes) to this book at combat start,
    /// mirroring InsightHost.ApplyFromRun. No-op while unlockAllForTesting is on (everything's already
    /// learned); when it's off, `learned` becomes exactly the run's recipes so reward-granted recipes gate.
    /// </summary>
    public void ApplyRunRecipes()
    {
        if (unlockAllForTesting) return;

        learned.Clear();
        var rm = RunManager.Instance;
        if (rm == null || rm.unlockedRecipes == null) return;
        foreach (var name in rm.unlockedRecipes)
            if (!string.IsNullOrEmpty(name)) learned.Add(name);
    }
}
