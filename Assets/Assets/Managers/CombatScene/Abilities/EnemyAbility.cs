using UnityEngine;

/// <summary>
/// Base class for an enemy's pluggable special mechanic. Attach concrete abilities to an
/// enemy prefab; Enemy collects them at battle start and calls these hooks. Per-battle state
/// lives on the component (enemies are spawned fresh each fight, and OnBattleStart resets).
///
/// Reactive abilities override the On* hooks; proactive ones override TryTakeTurn to take
/// over the enemy's turn entirely (telegraphs, multi-turn combos).
///
/// Note: do NOT put [DisallowMultipleComponent] here — it would block an enemy from having
/// several different abilities. Put it on each concrete ability instead.
/// </summary>
public abstract class EnemyAbility : MonoBehaviour
{
    protected Enemy enemy;

    public virtual void Initialize(Enemy owner) { enemy = owner; }

    /// <summary>Once, when the battle begins (after Initialize). Reset per-battle state here.</summary>
    public virtual void OnBattleStart() { }

    /// <summary>
    /// Return true to fully handle the enemy's turn (the default attack is skipped).
    /// Used for telegraphed / multi-turn actions like Critical Eye.
    /// </summary>
    public virtual bool TryTakeTurn() => false;

    /// <summary>After the enemy has acted this turn (default attack or a handled turn).</summary>
    public virtual void OnActed() { }

    /// <summary>After the enemy takes and SURVIVES a hit (survived is always true here).</summary>
    public virtual void OnTookDamage(int amount, bool survived) { }

    /// <summary>When the enemy dies.</summary>
    public virtual void OnDied() { }
}
