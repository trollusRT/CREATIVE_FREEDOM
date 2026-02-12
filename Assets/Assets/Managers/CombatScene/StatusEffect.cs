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