using UnityEngine;
using System.Collections.Generic;
using UnityEngine.UI;
using TMPro;

public class Enemy : MonoBehaviour
{
    [Header("Stats")]
    public string enemyName;
    public int maxHP = 20;
    public int currentHP;
    public int baseAttackDamage = 4;

    [Header("Portrait Sprites (HP bands)")]
    public Sprite stableSprite;   // 66–100%
    public Sprite hurtSprite;     // 33–66%
    public Sprite criticalSprite; // 1–33%
    private Sprite currentSprite;

    [Header("Refs")]
    public Player player;
    public TextMeshProUGUI hpText;
    public AudioManager audioManager;

    [Header("Audio")]
    public AudioClip attackSound;
    public AudioClip damageSound;
    public AudioClip deathSound;

    [Header("Runtime")]
    public List<StatusEffect> activeEffects = new List<StatusEffect>();
    public bool IsDead { get; private set; }

    private Animator animator;
    private Collider2D col2d;

    void Awake()
    {
        animator = GetComponent<Animator>();
        col2d = GetComponent<Collider2D>();
    }

    void Start()
    {
        IsDead = false;
        currentHP = Mathf.Clamp(currentHP <= 0 ? maxHP : currentHP, 0, maxHP);
        currentSprite = stableSprite;
        UpdateHPText();
        UpdatePortraitBand();
    }

    // ------------------- Turn / Actions -------------------

    public void TakeTurn()
    {
        if (IsDead) return;

        if (IsAsleepOrStunned())
        {
            // Optional: play an idle/sleep anim
            // animator?.SetTrigger("SleepIdle");
            Debug.Log($"{enemyName} is asleep/stunned and skips the turn.");
            return;
        }

        int damage = CalculateOutgoingDamage();
        Debug.Log(enemyName + " attacks!");

        // Optional: your attack VFX
        // CombatVFXManager.Instance.PlayOnEnemy(VfxType.Slash, player.transform.position);

        player.TakeDamage(damage);

        if (audioManager && attackSound) audioManager.PlaySound(attackSound);
        if (animator) animator.SetTrigger("Attack");
    }

    public void ApplyPoison(int dmgPerTurn, int turns)
    {
        activeEffects.Add(new StatusEffect
        {
            type = StatusType.Poison,
            power = dmgPerTurn,
            duration = turns
        });
        Debug.Log($"{enemyName} is poisoned for {turns} turns ({dmgPerTurn}/turn).");
    }

    public void ApplySleep(int turns)
    {
        activeEffects.Add(new StatusEffect
        {
            type = StatusType.Sleep,
            duration = turns
        });
        Debug.Log($"{enemyName} sleeps for {turns} turn(s).");
    }

    public void Heal(int amount)
    {
        if (IsDead) return;
        currentHP = Mathf.Min(currentHP + amount, maxHP);
        Debug.Log($"{enemyName} healed by {amount}, HP = {currentHP}");
        UpdateHPText();
        UpdatePortraitBand();
    }

    public void TakeDamage(int amount)
    {
        if (IsDead) return;

        amount = CalculateIncomingDamage(amount);
        currentHP -= amount;
        Debug.Log($"{enemyName} took {amount} damage, HP = {currentHP}");

        UpdateHPText();

        if (currentHP <= 0)
        {
            Die();
            return;
        }

        if (animator) animator.SetTrigger("Hurt");
        if (audioManager && damageSound) audioManager.PlaySound(damageSound);

        UpdatePortraitBand();
    }

    public void ProcessStatusEffects()
    {
        if (IsDead) return;

        for (int i = activeEffects.Count - 1; i >= 0; i--)
        {
            var eff = activeEffects[i];

            switch (eff.type)
            {
                case StatusType.Poison:
                    currentHP -= eff.power;
                    Debug.Log($"{enemyName} took {eff.power} poison damage, HP = {currentHP}");
                    UpdateHPText();

                    if (currentHP <= 0)
                    {
                        Die();
                        return; // stop further processing; this enemy is gone
                    }
                    break;

                case StatusType.Sleep:
                    // Passive countdown handled below
                    break;

                case StatusType.AttackBreak:
                case StatusType.Corrode:
                case StatusType.Stun:
                case StatusType.DefensiveStance:
                    // Effects applied elsewhere; just tick duration
                    break;
            }

            eff.duration--;
            if (eff.duration <= 0)
            {
                Debug.Log($"{enemyName}'s {eff.type} ended.");
                activeEffects.RemoveAt(i);
            }
        }

        UpdatePortraitBand();
    }

    // ------------------- Helpers / State -------------------

    private void Die()
    {
        if (IsDead) return;
        IsDead = true;

        // Untargetable & no more raycasts
        if (col2d) col2d.enabled = false;
        gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");
        var th = GetComponent<TargetHighlighter>();
        if (th) th.SetHighlighted(false);

        // SFX/Anim
        if (audioManager && deathSound) audioManager.PlaySound(deathSound);
        if (animator) animator.SetTrigger("Death");

        // Inform manager immediately (triggers instant victory if last)
        if (BattleManager.Instance) BattleManager.Instance.OnEnemyDied(this);
    }

    private int CalculateOutgoingDamage()
    {
        int damage = baseAttackDamage;

        // Debuffs, etc.
        foreach (var eff in activeEffects)
        {
            if (eff.type == StatusType.AttackBreak)
                damage = Mathf.Max(0, damage - eff.power);
            if (eff.type == StatusType.DefensiveStance)
                damage = Mathf.Min(damage, 1); // “all damage = 1” style
            // add more if needed
        }

        if (IsAsleepOrStunned()) return 0;

        return damage;
    }

    private int CalculateIncomingDamage(int rawDamage)
    {
        bool corroded = false;

        foreach (var eff in activeEffects)
        {
            if (eff.type == StatusType.Corrode) corroded = true;
            if (eff.type == StatusType.DefensiveStance)
                rawDamage = Mathf.Min(rawDamage, 1); // If you want enemy to also be reduced
        }

        if (corroded)
            rawDamage = Mathf.CeilToInt(rawDamage * 1.25f);

        return Mathf.Max(0, rawDamage);
    }

    private bool IsAsleepOrStunned()
    {
        for (int i = 0; i < activeEffects.Count; i++)
        {
            var t = activeEffects[i].type;
            if (t == StatusType.Sleep || t == StatusType.Stun) return true;
        }
        return false;
    }

    private void UpdateHPText()
    {
        if (hpText) hpText.text = $"{Mathf.Max(0, currentHP)}/{maxHP}";
    }

    private void UpdatePortraitBand()
    {
        if (maxHP <= 0) return;
        float ratio = (float)Mathf.Max(0, currentHP) / maxHP;

        if (ratio > 0.66f) currentSprite = stableSprite;
        else if (ratio > 0.33f) currentSprite = hurtSprite;
        else if (ratio > 0f) currentSprite = criticalSprite;
        // else: dead – portrait often handled by death anim / keep last

        // If you want an immediate portrait refresh while this enemy is “active” in UI,
        // you could ping BattleManager to update the portrait here conditionally.
    }

    // ------------------- Public getters -------------------

    public Sprite getSprite() => currentSprite;
    public int GetHP() => currentHP;
}
