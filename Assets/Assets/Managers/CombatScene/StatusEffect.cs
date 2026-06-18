using System.Collections.Generic;
using UnityEngine;

public enum StatusType
{
    Poison,
    Sleep,
    Stun,
    Reflect,
    Shield,         // Halves damage
    Corrode,        // Take increased damage
    DefensiveStance, // reduce all damage to 1
    Regen,          //Heal HP for some amount each turn
    Counter,
    LeechTrap,
    DoubleDamage,   // Ongoing effect that doubles outgoing damage
    AttackBreak,    // Attack -X% or -some amount
    DecayingMind,   // Ms. Remember debuff: power = # of hand cards replaced by Forgotten; duration = turns of grace left
    // etc.
}

[System.Serializable]
public class StatusEffect
{
    public StatusType type;
    public int power;       // e.g. poison dmg, reflect amount
    public int duration;    // how many turns remain
    public bool usedOnce;   // e.g. for Reflect that triggers once
}

public static class StatusEffectStacking
{
    /// <summary>
    /// Adds a status effect, or stacks into an existing one (same type).
    /// Default rule: power stacks additively, duration refreshes to max(existing, new).
    /// </summary>
    public static void AddOrStack(
        List<StatusEffect> list,
        StatusType type,
        int power,
        int duration,
        bool stackPower = true,
        bool refreshDurationToMax = true)
    {
        if (list == null) return;

        power = Mathf.Max(0, power);
        duration = Mathf.Max(0, duration);

        for (int i = 0; i < list.Count; i++)
        {
            var e = list[i];
            if (e == null) continue;
            if (e.type != type) continue;

            if (stackPower) e.power += power;
            if (refreshDurationToMax) e.duration = Mathf.Max(e.duration, duration);
            else e.duration += duration;

            e.usedOnce = false;
            return;
        }

        list.Add(new StatusEffect
        {
            type = type,
            power = power,
            duration = duration,
            usedOnce = false
        });
    }
}