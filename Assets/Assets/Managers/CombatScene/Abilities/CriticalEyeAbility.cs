using UnityEngine;

/// <summary>
/// Critical Eye: sometimes spends a turn criticising the player — a telegraph that deals no
/// damage and plays a different animation — then unleashes a big hit on its NEXT turn.
///
/// If the enemy is slept/stunned while charging, the turn is skipped upstream (in Enemy) and
/// the wind-up is held, so the big hit simply lands once it can act again.
/// </summary>
[DisallowMultipleComponent]
public class CriticalEyeAbility : EnemyAbility
{
    [Tooltip("Chance, when idle, to spend this turn criticising instead of attacking normally.")]
    [Range(0f, 1f)] public float criticizeChance = 0.5f;
    [Tooltip("Damage dealt on the turn after criticising (run through the enemy's outgoing modifiers).")]
    public int bigHitDamage = 8;

    [Header("Presentation")]
    [Tooltip("Animator trigger for the criticise (telegraph) pose.")]
    public string criticizeTrigger = "Criticize";
    [Tooltip("Animator trigger for the big hit. Falls back to the normal Attack trigger.")]
    public string bigHitTrigger = "Attack";
    public AudioClip criticizeSfx;
    public AudioClip bigHitSfx;

    bool charging;

    public override void OnBattleStart() { charging = false; }

    public override bool TryTakeTurn()
    {
        if (charging)
        {
            // Unleash the telegraphed big hit.
            charging = false;
            int dmg = enemy.ApplyOutgoingModifiers(bigHitDamage);
            enemy.SetIntent(new EnemyIntent { kind = IntentKind.BigHit, amount = dmg });

            if (enemy.player) enemy.player.TakeDamage(dmg, enemy);
            enemy.PlayAnimTrigger(bigHitTrigger);
            if (bigHitSfx && enemy.audioManager) enemy.audioManager.PlaySound(bigHitSfx);
            Debug.Log($"{enemy.enemyName} unleashes a critical strike for {dmg}!");
            return true;
        }

        // Idle: maybe wind up this turn (telegraph, no damage).
        if (Random.value < criticizeChance)
        {
            charging = true;
            enemy.SetIntent(new EnemyIntent
            {
                kind = IntentKind.Telegraph,
                amount = bigHitDamage,
                label = "Criticising…"
            });
            enemy.PlayAnimTrigger(criticizeTrigger);
            if (criticizeSfx && enemy.audioManager) enemy.audioManager.PlaySound(criticizeSfx);
            Debug.Log($"{enemy.enemyName} sizes up the player, winding up a critical strike…");
            return true;   // turn consumed by the telegraph
        }

        return false;  // act normally this turn
    }
}
