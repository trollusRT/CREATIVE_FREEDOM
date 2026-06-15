using UnityEngine;
using System.Collections.Generic;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

public class Enemy : MonoBehaviour
{
    [Header("Stats")]
    public string enemyName;
    public int maxHP = 20;
    public int currentHP;
    public int baseAttackDamage = 4;

    [Header("Portrait Sprites (HP bands)")]
    public Sprite stableSprite;   // 66�100%
    public Sprite hurtSprite;     // 33�66%
    public Sprite criticalSprite; // 1�33%
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

    [Header("Hit Reaction")]
    public float knockback = 0.15f;     // world units, punched away from the player
    public float squash = 0.12f;        // punch-scale amount
    public float hitStop = 0.06f;       // realtime seconds of freeze on impact
    public float shakeMin = 0.15f;
    public float shakeMax = 0.5f;
    public int shakeMaxDamage = 20;     // damage that maps to shakeMax

    private Animator animator;
    private Collider2D col2d;
    private Transform hitVisual;

    void Awake()
    {
        animator = GetComponent<Animator>();
        col2d = GetComponent<Collider2D>();
        var sr = GetComponentInChildren<SpriteRenderer>();
        hitVisual = sr ? sr.transform : transform;
    }

    void Start()
    {
        IsDead = false;
        if (currentHP <= 0) currentHP = maxHP;
        currentHP = Mathf.Clamp(currentHP, 0, maxHP);
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

        player.TakeDamage(damage, this);

        if (audioManager && attackSound) audioManager.PlaySound(attackSound);
        if (animator) animator.SetTrigger("Attack");
    }

    public void ApplyPoison(int dmgPerTurn, int turns)
    {
        // Stack poison predictably: damage stacks, duration refreshes to max.
        StatusEffectStacking.AddOrStack(activeEffects, StatusType.Poison, dmgPerTurn, turns);
        Debug.Log($"{enemyName} is poisoned for {turns} turns (+{dmgPerTurn}/turn).");
    }

    public void ApplySleep(int turns)
    {
        // Sleep doesn't stack power; refresh duration to max.
        StatusEffectStacking.AddOrStack(activeEffects, StatusType.Sleep, 0, turns, stackPower: false, refreshDurationToMax: true);
        Debug.Log($"{enemyName} sleeps for {turns} turn(s).");
    }

    // Package E helpers (data-driven card effects)
    public void ApplyAttackBreak(int power, int turns)
    {
        power = Mathf.Max(0, power);
        turns = Mathf.Max(0, turns);
        StatusEffectStacking.AddOrStack(activeEffects, StatusType.AttackBreak, power, turns);
        Debug.Log($"{enemyName} attack broken for {turns} turn(s) (-{power}).");
    }

    public void ApplyCorrode(int power, int turns)
    {
        power = Mathf.Max(0, power);
        turns = Mathf.Max(0, turns);
        StatusEffectStacking.AddOrStack(activeEffects, StatusType.Corrode, power, turns);
        Debug.Log($"{enemyName} corroded for {turns} turn(s) (+{power}).");
    }

    public void Heal(int amount)
    {
        if (IsDead) return;
        currentHP = Mathf.Min(currentHP + amount, maxHP);
        DamageNumbers.ShowHeal(transform.position, amount);
        Debug.Log($"{enemyName} healed by {amount}, HP = {currentHP}");
        UpdateHPText();
        UpdatePortraitBand();
    }

    public void TakeDamage(int amount)
    {
        if (IsDead) return;

        amount = CalculateIncomingDamage(amount);
        currentHP -= amount;
        DamageNumbers.ShowDamage(transform.position, amount);
        Debug.Log($"{enemyName} took {amount} damage, HP = {currentHP}");

        UpdateHPText();

        if (currentHP <= 0)
        {
            Die();
            return;
        }

        if (animator) animator.SetTrigger("Hurt");
        if (audioManager && damageSound) audioManager.PlaySound(damageSound);

        PlayHitReaction(amount);

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
                    currentHP -= Mathf.Max(1, eff.power);
                    DamageNumbers.ShowDamage(transform.position, Mathf.Max(1, eff.power));
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

    // Impact feedback when this enemy is struck by a card.
    private void PlayHitReaction(int amount)
    {
        if (!hitVisual) return;

        hitVisual.DOKill(true);
        hitVisual.DOPunchScale(Vector3.one * squash, 0.25f, 8, 0.9f);

        Vector3 dir = player ? (transform.position - player.transform.position) : Vector3.right;
        dir.z = 0f;
        if (dir.sqrMagnitude < 0.0001f) dir = Vector3.right;
        hitVisual.DOPunchPosition(dir.normalized * knockback, 0.25f, 8, 0.9f);

        if (CameraShakeManager.Instance)
        {
            float k = shakeMaxDamage > 0 ? Mathf.Clamp01((float)amount / shakeMaxDamage) : 1f;
            CameraShakeManager.Instance.Shake(Mathf.Lerp(shakeMin, shakeMax, k));
        }

        if (CombatVFXManager.Instance)
            CombatVFXManager.Instance.HitStop(hitStop);
    }

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
                damage = Mathf.Min(damage, 1); // �all damage = 1� style
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
        // else: dead � portrait often handled by death anim / keep last

        // If you want an immediate portrait refresh while this enemy is �active� in UI,
        // you could ping BattleManager to update the portrait here conditionally.
    }

    // ------------------- Public getters -------------------

    public Sprite getSprite() => currentSprite;
    public int GetHP() => currentHP;
}
