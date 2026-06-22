using UnityEngine;

/// <summary>
/// Rarity tier for an Insight (Inspiration). Drives which reward pools it appears in.
/// </summary>
public enum InsightRarity
{
    Common,
    Uncommon,
    Rare,
    Epic
}

/// <summary>
/// When an Insight fires. The InsightHost broadcasts each of these at the matching moment in
/// combat; an Insight only reacts to its own <see cref="Insight.trigger"/>. Mirrors the lifecycle
/// hooks on <see cref="EnemyAbility"/>, but for the player.
/// </summary>
public enum InsightTrigger
{
    CombatStart,   // once, when the fight begins
    TurnStart,     // start of the player's turn (after AP reset + draw)
    TurnEnd,       // end of the player's turn
    PlayCard,      // a card resolved
    Fuse,          // a fusion completed
    TookDamage,    // the player took (or absorbed) a hit and survived
    DealtDamage,   // the player dealt damage to an enemy (pass 2)
    Kill,          // an enemy died
    Heal,          // the player healed (pass 2)
    ApplyStatus    // the player applied a status to an enemy (pass 2)
}

/// <summary>
/// Data for a single Insight (in-world: an "Inspiration") — a permanent, passive, per-run buff.
/// Authored as a ScriptableObject from the Inspirations tab of Cards.xlsx, mirroring how
/// <see cref="CardData"/> authors a card. The runtime behaviour lives in InsightHost, which reads
/// <see cref="effectId"/> + the tuning numbers below (the same data-driven shape as
/// CardEffectRegistry). See MAP_DESIGN.md "How Insights work".
/// </summary>
[CreateAssetMenu(fileName = "NewInsight", menuName = "Insights/Insight")]
public class Insight : ScriptableObject
{
    [Tooltip("Stable key used everywhere this Insight is referenced (RunManager.insights, the database). Keep it unique and lowercase, e.g. 'quick_drying'.")]
    public string id;

    public string displayName;
    public InsightRarity rarity;

    [Tooltip("When this Insight fires.")]
    public InsightTrigger trigger;

    [TextArea] public string description;

    [Header("Effect")]
    [Tooltip("Which effect the InsightHost runs when this fires, e.g. GAIN_AP_THIS_TURN, GAIN_SHIELD, HEAL, DRAW_CARDS.")]
    public string effectId;

    [Tooltip("Primary tuning number (AP/shield/heal/draw amount, etc.).")]
    public int amount;

    [Tooltip("Secondary tuning number (reserved for two-number effects).")]
    public int amount2;

    [Tooltip("Reserved free-form parameter (e.g. a card colour filter for colour-specific Insights).")]
    public string param;

    [Header("Stacking")]
    [Tooltip("If true, holding duplicate copies increases the effect by perCopyAmount each.")]
    public bool stacks;

    [Tooltip("Extra effect added per copy beyond the first (only used when 'stacks' is true).")]
    public int perCopyAmount;

    [Header("Gating")]
    [Tooltip("If true, this Insight fires at most once per player turn (e.g. 'Once per turn' on the sheet).")]
    public bool oncePerTurn;

    [Tooltip("If true, this Insight fires at most once per combat (e.g. 'Once per battle / Once per combat').")]
    public bool oncePerCombat;
}
