/// <summary>
/// What an enemy plans to do on its turn. Exposed by Enemy (CurrentIntent) so a future
/// telegraph UI can show "winding up", "attacking for X", etc. Gameplay doesn't depend on
/// it yet — it's purely descriptive.
/// </summary>
public enum IntentKind
{
    None,
    Attack,
    BigHit,
    Telegraph,   // winding up / no damage this turn
    Heal,
    Debuff,
    Special,
}

public struct EnemyIntent
{
    public IntentKind kind;
    public int amount;     // damage/heal magnitude, 0 if not applicable
    public string label;   // optional UI caption, e.g. "Criticising…"
}
