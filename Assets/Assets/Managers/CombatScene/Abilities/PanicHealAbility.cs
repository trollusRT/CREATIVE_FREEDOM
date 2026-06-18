using UnityEngine;

/// <summary>
/// MEI-I: if she SURVIVES a single hit of >= threshold, she freaks out and heals to full.
/// </summary>
[DisallowMultipleComponent]
public class PanicHealAbility : EnemyAbility
{
    [Tooltip("Heal triggers when a single surviving hit deals at least this much.")]
    public int threshold = 8;
    [Tooltip("If true, can only trigger once per battle.")]
    public bool oncePerBattle = false;
    [Tooltip("Optional sting played when she freaks out and heals.")]
    public AudioClip healSfx;

    bool used;

    public override void OnBattleStart() { used = false; }

    public override void OnTookDamage(int amount, bool survived)
    {
        if (!survived) return;
        if (amount < threshold) return;
        if (oncePerBattle && used) return;

        used = true;
        int healed = enemy.maxHP - enemy.currentHP;
        if (healed > 0) enemy.Heal(healed);   // heals to full + shows the heal number
        if (healSfx && enemy.audioManager) enemy.audioManager.PlaySound(healSfx);
        Debug.Log($"{enemy.enemyName} freaks out and heals to full after a {amount}-damage hit!");
    }
}
