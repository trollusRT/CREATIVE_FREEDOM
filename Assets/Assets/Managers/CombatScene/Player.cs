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

    public AudioClip attackSound;
    public AudioClip damageSound;
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

    public void TakeDamage(int damage)
    {
        audioManager.PlaySound(hurtSound);
        currentHP -= damage;
        if (currentHP <= 0)
        {

            CombatVFXManager.Instance.ShakeCamera();
            animator.SetTrigger("Death");
            audioManager.PlaySound(deathSound);
            Debug.Log("Player Defeated!");
            // Implement Game Over Logic
        }
        else
        {
            Debug.Log("Yeouch");
            animator.SetTrigger("Hurt");
            audioManager.PlaySound(damageSound);
            CombatVFXManager.Instance.PlayOnPlayer(VfxType.PaintSplash);
        }

        CheckHP();
    }

    public void Heal(int amount)
    {
        animator.SetTrigger("Heal"); // add this state
        CombatVFXManager.Instance.PlayOnPlayer(VfxType.HealBurst);
        currentHP += amount;
        if (currentHP > maxHP) currentHP = maxHP;
        Debug.Log($"Player healed {amount}, HP now {currentHP}");

        CheckHP();
    }

    public void AttackAnimation()
    {
        animator.SetTrigger("Attack");
    }

    public void SpendAP(int amount)
    {
        if (AP >= amount)
        {
            audioManager.PlaySound(attackSound);
        }

        CheckHP();
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
                    TakeDamage(p);
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
        bool isCorroded = false;  // typically an Enemy effect

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
}