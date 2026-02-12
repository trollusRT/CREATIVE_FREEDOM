using UnityEngine;

[CreateAssetMenu(fileName = "FusionRecipe", menuName = "Cards/Fusion Recipe")]
public class FusionRecipe : ScriptableObject
{
    [Header("Display")]
    public string recipeName;              // e.g. "Red Splatter", "CREATIVE FREEDOM"

    [Header("Result")]
    public CardData result;                // fused card (2AP/3AP)

    [Header("Ingredients (2 cards)")]
    public CardData ingredientA;
    public CardData ingredientB;

    [Header("Behavior")]
    public bool orderAgnostic = true;      // true = (A+B == B+A)
    public bool isSpecial3Cost = false;    // for your 3AP special fusions
}
