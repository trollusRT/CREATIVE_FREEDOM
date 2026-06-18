using UnityEngine;

/// <summary>
/// Ms. Remember: chance, each turn she acts, to inflict a Decaying Mind stack on the player
/// (replaces a hand card with a useless Forgotten card). The stack logic itself lives in
/// BattleManager/Player; this ability just rolls the chance after she acts.
/// </summary>
[DisallowMultipleComponent]
public class DecayingMindAbility : EnemyAbility
{
    [Range(0f, 1f)] public float chance = 0.5f;

    public override void OnActed()
    {
        if (Random.value < chance)
            BattleManager.Instance?.ApplyDecayingMindToPlayer();
    }
}
