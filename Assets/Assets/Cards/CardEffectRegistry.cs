using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Package E: data-driven gameplay effects for cards.
///
/// Intent:
/// - Keep legacy name-based switch in Card.cs for complex/unique cards.
/// - Allow new cards to be added by setting CardData.effectId (+ optional effectKey) with no Card.cs edits.
///
/// Conventions (recommended):
/// - DAMAGE_ST_RANDOM:       minValue..maxValue damage to a single enemy
/// - DAMAGE_AOE_RANDOM:      minValue..maxValue damage to all living enemies
/// - HEAL_PLAYER_RANDOM:     minValue..maxValue heal to player
/// - POISON_ST:              power=minValue, turns=maxValue
/// - SLEEP_ST:               turns=minValue
/// - ATTACK_BREAK_ST:        power=minValue, turns=maxValue
/// - CORRODE_ST:             power=minValue, turns=maxValue
///
/// You can add more IDs over time without touching Card.cs.
/// </summary>
public static class CardEffectRegistry
{
    // Fast string checks without allocations.
    private static bool IdEquals(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public struct EnemyResolve
    {
        public bool isAoe;
        public EffectKey vfxKey;
        public Action onImpact;
    }

    public struct PlayerResolve
    {
        public EffectKey vfxKey;
        public Action onResolve;
    }

    /// <summary>
    /// Try build an enemy-targeted effect from CardData.effectId.
    /// Returns false if effectId is empty/unrecognized.
    /// </summary>
    public static bool TryResolveEnemy(CardData data, Enemy targetEnemy, out EnemyResolve resolved)
    {
        resolved = default;
        if (data == null) return false;
        if (string.IsNullOrWhiteSpace(data.effectId)) return false;

        // Capture for closures.
        int min = data.minValue;
        int max = data.maxValue;

        // DAMAGE_ST_RANDOM
        if (IdEquals(data.effectId, "DAMAGE_ST_RANDOM"))
        {
            resolved.isAoe = false;
            resolved.vfxKey = data.effectKey;
            resolved.onImpact = () =>
            {
                if (!targetEnemy || targetEnemy.IsDead) return;
                int dmg = UnityEngine.Random.Range(min, max + 1);
                targetEnemy.TakeDamage(dmg);
            };
            return true;
        }

        // POISON_ST (power=minValue, turns=maxValue)
        if (IdEquals(data.effectId, "POISON_ST"))
        {
            resolved.isAoe = false;
            resolved.vfxKey = data.effectKey;
            resolved.onImpact = () =>
            {
                if (!targetEnemy || targetEnemy.IsDead) return;
                int power = Mathf.Max(0, min);
                int turns = Mathf.Max(0, max);
                targetEnemy.ApplyPoison(power, turns);
            };
            return true;
        }

        // SLEEP_ST (turns=minValue)
        if (IdEquals(data.effectId, "SLEEP_ST"))
        {
            resolved.isAoe = false;
            resolved.vfxKey = data.effectKey;
            resolved.onImpact = () =>
            {
                if (!targetEnemy || targetEnemy.IsDead) return;
                int turns = Mathf.Max(0, min);
                targetEnemy.ApplySleep(turns);
            };
            return true;
        }

        // ATTACK_BREAK_ST (power=minValue, turns=maxValue)
        if (IdEquals(data.effectId, "ATTACK_BREAK_ST"))
        {
            resolved.isAoe = false;
            resolved.vfxKey = data.effectKey;
            resolved.onImpact = () =>
            {
                if (!targetEnemy || targetEnemy.IsDead) return;
                int power = Mathf.Max(0, min);
                int turns = Mathf.Max(0, max);
                targetEnemy.ApplyAttackBreak(power, turns);
            };
            return true;
        }

        // CORRODE_ST (power=minValue, turns=maxValue)
        if (IdEquals(data.effectId, "CORRODE_ST"))
        {
            resolved.isAoe = false;
            resolved.vfxKey = data.effectKey;
            resolved.onImpact = () =>
            {
                if (!targetEnemy || targetEnemy.IsDead) return;
                int power = Mathf.Max(0, min);
                int turns = Mathf.Max(0, max);
                targetEnemy.ApplyCorrode(power, turns);
            };
            return true;
        }

        // DAMAGE_AOE_RANDOM
        if (IdEquals(data.effectId, "DAMAGE_AOE_RANDOM"))
        {
            resolved.isAoe = true;
            resolved.vfxKey = data.effectKey;
            resolved.onImpact = () =>
            {
                if (BattleManager.Instance == null) return;
                var snapshot = new List<Enemy>(BattleManager.Instance.enemies);
                foreach (var e in snapshot)
                {
                    if (!e || e.IsDead) continue;
                    int dmg = UnityEngine.Random.Range(min, max + 1);
                    e.TakeDamage(dmg);
                }
            };
            return true;
        }

        return false;
    }

    /// <summary>
    /// Try build a player-targeted effect from CardData.effectId.
    /// Returns false if effectId is empty/unrecognized.
    /// </summary>
    public static bool TryResolvePlayer(CardData data, Player player, out PlayerResolve resolved)
    {
        resolved = default;
        if (data == null) return false;
        if (player == null) return false;
        if (string.IsNullOrWhiteSpace(data.effectId)) return false;

        int min = data.minValue;
        int max = data.maxValue;

        // HEAL_PLAYER_RANDOM
        if (IdEquals(data.effectId, "HEAL_PLAYER_RANDOM"))
        {
            resolved.vfxKey = data.effectKey;
            resolved.onResolve = () =>
            {
                int heal = UnityEngine.Random.Range(min, max + 1);
                player.Heal(heal);
            };
            return true;
        }

        // REGEN_PLAYER (power=minValue, turns=maxValue)
        if (IdEquals(data.effectId, "REGEN_PLAYER"))
        {
            resolved.vfxKey = data.effectKey;
            resolved.onResolve = () =>
            {
                int power = Mathf.Max(0, min);
                int turns = Mathf.Max(0, max);
                player.ApplyRegen(power, turns);
            };
            return true;
        }

        // SHIELD_PLAYER (turns=minValue)
        if (IdEquals(data.effectId, "SHIELD_PLAYER"))
        {
            resolved.vfxKey = data.effectKey;
            resolved.onResolve = () =>
            {
                int turns = Mathf.Max(0, min);
                StatusEffectStacking.AddOrStack(player.activeEffects, StatusType.Shield, power: 0, duration: turns,
                    stackPower: false, refreshDurationToMax: true);
            };
            return true;
        }

        return false;
    }
}
