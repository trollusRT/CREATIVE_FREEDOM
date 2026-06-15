using UnityEngine;
using System.Collections.Generic;
using UnityEngine.UI;
using TMPro;

public class Player : MonoBehaviour
{
    public int maxHP = 20;
    public int currentHP;
    public Animator animator;
    public int AP;

    [Header("Reactive effects (non-StatusType)")]
    [SerializeField] private int doubleDamageCharges = 0;
    [SerializeField] private int counterTurns = 0;
    [SerializeField] private int counterPower = 0;
    [SerializeField] private int reflectTurns = 0;
    [SerializeField] private bool reflectArmed = false;
    [SerializeField] private int leechTrapTurns = 0;
    [SerializeField] private int leechTrapPower = 0;
    [SerializeField] private bool secondWindArmed = false;
    [SerializeField] private int secondWindRegenPerTurn = 0;
    [SerializeField] private int secondWindRegenTurns = 0;
    [SerializeField] private bool creativeFreedomActive = false;
    [SerializeField] private int creativeFreedomRemainingDamage = 0;

    public AudioClip attackSound;
    public AudioClip damageSound;
    [Tooltip("Optional impact sound when Junior is hit (e.g., punch.wav). If set, this is used instead of damageSound.")]
    public AudioClip punchImpactSound;
    public AudioClip deathSound;
    public AudioClip hurtSound;

    public TextMeshProUGUI playerHPText;

    public AudioManager audioManager;
    public List<StatusEffect> activeEffects = new List<StatusEffect>();
    // A buff to increase damage for next attack, or other single triggers
    public bool nextAttackIsDoubled;

    public Sprite stableSprite; // Reference to the portrait sprite
    public Sprite hurtSprite; // Reference to the portrait sprite
    public Sprite criticalSprite; // Reference to the portrait sprite
    public Sprite currentSprite;

    void Start()
    {
        currentHP = maxHP;
        animator = GetComponent<Animator>();
        currentSprite = stableSprite;
        AP = 6;

    }

    public void UpdateHPText()
    {
        playerHPText.text = currentHP + "/" + maxHP;
    }

    public void TakeDamage(int damage) => TakeDamage(damage, null);

    public void TakeDamage(int damage, Enemy source)
    {
        // Apply Shield / DefensiveStance from activeEffects (if present).
        int finalDamage = Mathf.Max(0, damage);

        // Defensive Stance: reduce any incoming damage to 1 while active
        if (HasStatus(StatusType.DefensiveStance))
            finalDamage = Mathf.Min(finalDamage, 1);

        // Shield: flat reduction of 2 while active (min 0)
        if (HasStatus(StatusType.Shield))
            finalDamage = Mathf.Max(0, finalDamage - 2);

        // Second Wind cheat death
        if (secondWindArmed && currentHP - finalDamage <= 0)
        {
            currentHP = 1;
            secondWindArmed = false;
            // grant regen after cheating death
            ApplyRegen(secondWindRegenPerTurn, secondWindRegenTurns);
            UpdateHPText();
            if (animator) animator.SetTrigger("Buff");
            return;
        }

        // Play audio for normal hits only (status ticks should call TakeStatusDamage instead)
        if (audioManager && hurtSound) audioManager.PlaySound(hurtSound);
        var impact = punchImpactSound ? punchImpactSound : damageSound;
        if (audioManager && impact) audioManager.PlaySound(impact);

        currentHP -= finalDamage;
        DamageNumbers.ShowDamage(transform.position, finalDamage);

        if (creativeFreedomActive)
        {
            creativeFreedomRemainingDamage -= finalDamage;
            if (creativeFreedomRemainingDamage <= 0)
                creativeFreedomActive = false;
        }

        if (currentHP <= 0)
        {
            CombatVFXManager.Instance.ShakeCamera();
            animator.SetTrigger("Death");
            audioManager.PlaySound(deathSound);
            Debug.Log("Player Defeated!");
        }
        else
        {
            animator.SetTrigger("Hurt");
            CombatVFXManager.Instance.PlayOnPlayer(VfxType.PaintSplash);
        }

        UpdateHPText();

        // Reactive effects
        if (source != null)
        {
            if (reflectTurns > 0 && reflectArmed)
            {
                reflectArmed = false;
                source.TakeDamage(finalDamage);
            }

            if (counterTurns > 0)
            {
                source.TakeDamage(counterPower);
            }

            if (leechTrapTurns > 0)
            {
                source.TakeDamage(leechTrapPower);
                Heal(leechTrapPower);
            }
        }
    }


    /// <summary>
    /// Damage applied by status effects (poison, etc.). Intentionally avoids full hurt SFX spam.
    /// </summary>
    private void ApplyStatusDamage(int damage)
    {
        damage = Mathf.Max(0, damage);
        if (damage <= 0) return;

        currentHP -= damage;
        DamageNumbers.ShowDamage(transform.position, damage);

        if (currentHP <= 0)
        {
            currentHP = 0;
            CombatVFXManager.Instance.ShakeCamera();
            if (animator) animator.SetTrigger("Death");
            if (audioManager && deathSound) audioManager.PlaySound(deathSound);
            Debug.Log("Player Defeated!");
        }
        else
        {
            if (animator) animator.SetTrigger("Hurt");
        }

        CheckHP();
    }

    public void ApplyPoison(int dmgPerTurn, int turns)
    {
        StatusEffectStacking.AddOrStack(activeEffects, StatusType.Poison, dmgPerTurn, turns);
    }

    public void ApplyRegen(int healPerTurn, int turns)
    {
        StatusEffectStacking.AddOrStack(activeEffects, StatusType.Regen, healPerTurn, turns);
    }

    public void Heal(int amount)
    {
        animator.SetTrigger("Heal"); // add this state
        CombatVFXManager.Instance.PlayOnPlayer(VfxType.HealBurst);
        currentHP += amount;
        if (currentHP > maxHP) currentHP = maxHP;
        DamageNumbers.ShowHeal(transform.position, amount);
        Debug.Log($"Player healed {amount}, HP now {currentHP}");

        CheckHP();
    }

    public void AttackAnimation()
    {
        animator.SetTrigger("Attack");
    }

    public void SpendAP(int amount)
    {
        if (AP < amount) return;
        AP -= amount;
        if (audioManager && attackSound) audioManager.PlaySound(attackSound);
    }

    // e.g. for enrage: we reduce HP by X, set a status that buffs damage
    public void ModifyAttackPower(int additionalDamage, int turns)
    {
        // Could store as a status effect, or just a boolean for the next X attacks
        // We'll do a status effect for consistency
        var effect = new StatusEffect
        {
            type = StatusType.DoubleDamage, // or a new type "EnrageBuff"
            power = additionalDamage,
            duration = turns
        };
        activeEffects.Add(effect);

        CheckHP();
    }

    public void CheckHP()
    {
        float ratio = (float)currentHP / (float)maxHP;

        if (ratio > 0.66f)
        {
            Debug.Log($"Junior is at 66-100% HP range.");
            currentSprite = stableSprite;
        }
        else if (ratio > 0.33f)
        {
            Debug.Log($"Junior is at 33-66% HP range.");
            currentSprite = hurtSprite;
        }
        else if (ratio > 0)
        {
            Debug.Log($"Junior is at 1-33% HP range.");
            currentSprite = criticalSprite;
        }
        else
        {
            Debug.Log($"Junior is at 0 or below HP!");
            // Possibly handle death or negative HP scenario.
        }
    }

    public int GetHP()
    {
        return currentHP;
    }

    public Sprite GetSprite()
    {
        return currentSprite;
    }

    public void ProcessStatusEffects()
    {

        // decay reactive-effect timers
        if (counterTurns > 0) counterTurns--;
        if (reflectTurns > 0) { reflectTurns--; if (reflectTurns == 0) reflectArmed = false; }
        if (leechTrapTurns > 0) leechTrapTurns--;
        if (activeEffects == null || activeEffects.Count == 0)
        {
            UpdateHPText();
            return;
        }

        // 1) Tick effects without removing from the list mid-iteration.
        for (int i = 0; i < activeEffects.Count; i++)
        {
            var eff = activeEffects[i];
            if (eff == null) continue;

            switch (eff.type)
            {
                case StatusType.Poison:
                    // apply damage this tick
                    int p = Mathf.Max(1, eff.power);
                    ApplyStatusDamage(p);
                    eff.duration--;
                    break;

                case StatusType.Regen:
                    // apply heal this tick
                    int h = Mathf.Max(1, eff.power);
                    Heal(h);
                    eff.duration--;
                    break;

                // add other ticking effects here if needed
                // case StatusType.AttackBreak:
                // case StatusType.DefensiveStance:
                //   eff.duration--;

                default:
                    // non-ticking or generic decay
                    if (eff.duration > 0) eff.duration--;
                    break;
            }

            // IMPORTANT:
            // If StatusEffect is a *struct*, assign back into the list:
            // activeEffects[i] = eff;
            //
            // If StatusEffect is a class (reference type), the field changes already apply,
            // so you don't need to reassign. Leaving the line below commented is fine for classes.
            // activeEffects[i] = eff;
        }

        // 2) Now prune expired effects *after* the loop.
        for (int i = activeEffects.Count - 1; i >= 0; i--)
        {
            var eff = activeEffects[i];
            if (eff == null || eff.duration <= 0)
                activeEffects.RemoveAt(i);
        }

        UpdateHPText();
    }


    private int CalculateIncomingDamage(int rawDamage)
    {
        // Check if we have Shield (halves damage), DefensiveStance (reduce to 1), etc.
        // We'll do a quick pass:
        bool hasShield = false;
        bool hasDefensiveStance = false;

        foreach (var eff in activeEffects)
        {
            if (eff.type == StatusType.Shield)
            {
                hasShield = true;
            }
            if (eff.type == StatusType.DefensiveStance)
            {
                // reduce damage to 1
                hasDefensiveStance = true;
            }
        }

        if (hasDefensiveStance && rawDamage > 0)
            rawDamage = 1;
        else if (hasShield)
            rawDamage = Mathf.CeilToInt(rawDamage / 2f);

        return rawDamage;
    }


    // ------------------- Package E support helpers -------------------
    private bool HasStatus(StatusType type)
    {
        if (activeEffects == null) return false;
        for (int i = 0; i < activeEffects.Count; i++)
        {
            var e = activeEffects[i];
            if (e != null && e.type == type && e.duration > 0) return true;
        }
        return false;
    }

    // Used by cards that trade HP for power (prevents self-KO by payment).
    public bool PayHealth(int amount)
    {
        amount = Mathf.Max(0, amount);
        if (amount <= 0) return true;
        if (currentHP - amount <= 0) return false;
        currentHP -= amount;
        UpdateHPText();
        return true;
    }

    public void AddDoubleDamageCharges(int charges)
    {
        doubleDamageCharges = Mathf.Max(0, doubleDamageCharges + Mathf.Max(0, charges));
    }

    public void EnableSecondWind(int regenPerTurn, int turns)
    {
        secondWindArmed = true;
        secondWindRegenPerTurn = Mathf.Max(1, regenPerTurn);
        secondWindRegenTurns = Mathf.Max(1, turns);
    }

    public void EnableCounter(int power, int turns)
    {
        counterPower = Mathf.Max(0, power);
        counterTurns = Mathf.Max(counterTurns, Mathf.Max(0, turns));
    }

    public void EnableReflect(int turns)
    {
        reflectTurns = Mathf.Max(reflectTurns, Mathf.Max(0, turns));
        reflectArmed = true;
    }

    public void EnableLeechTrap(int power, int turns)
    {
        leechTrapPower = Mathf.Max(0, power);
        leechTrapTurns = Mathf.Max(leechTrapTurns, Mathf.Max(0, turns));
    }

    public void EnableCreativeFreedom(int damageBudget)
    {
        creativeFreedomActive = true;
        creativeFreedomRemainingDamage = Mathf.Max(0, damageBudget);
        Debug.Log("Lets get creative!");
    }

    public int ModifyOutgoingDamage(int baseDamage)
    {
        int dmg = Mathf.Max(0, baseDamage);

        if (nextAttackIsDoubled)
        {
            nextAttackIsDoubled = false;
            return dmg * 2;
        }

        if (doubleDamageCharges > 0)
        {
            doubleDamageCharges--;
            return dmg * 2;
        }

        if (HasStatus(StatusType.DoubleDamage) || creativeFreedomActive)
        {
            return dmg * 2;
        }

        return dmg;
    }

}
