using UnityEngine;
using System.Collections.Generic;
using TMPro;

/// <summary>
/// Builds the combat scene's enemy list from an <see cref="EncounterData"/>.
/// For each slot it instantiates the enemy prefab at an anchor, pushes its data via
/// Enemy.Init(), and wires the shared HUD HP text (which floats above the enemy via
/// <see cref="FollowWorldTargetUI"/>). Returns the live list for BattleManager.
///
/// Backward compatible: if no encounter resolves (or no slots are set) it returns null,
/// and BattleManager keeps any hand-placed scene enemies. Each slot's HP text is optional —
/// leave it empty if an enemy prefab carries its own HP text.
///
/// Phase 3 hook: a map / RunManager can set <see cref="PendingEncounter"/> before the
/// combat scene loads; until then <see cref="fallbackEncounter"/> lets you Play standalone.
/// </summary>
public class EnemySpawner : MonoBehaviour
{
    [System.Serializable]
    public class EnemySlot
    {
        [Tooltip("Where this enemy stands in the scene.")]
        public Transform anchor;
        [Tooltip("Shared HUD HP text for this slot. Leave empty if the prefab carries its own.")]
        public TextMeshProUGUI hpText;
        [Tooltip("Optional. Retargeted to the spawned enemy so the HP text floats above it.")]
        public FollowWorldTargetUI hpFollower;
    }

    [Header("Scene slots (up to 3)")]
    [Tooltip("One per enemy position, in order. The slot count caps enemies per fight.")]
    public EnemySlot[] slots;

    [Header("Refs injected into each spawned enemy")]
    [Tooltip("Optional. Falls back to BattleManager.Instance.player if left empty.")]
    public Player player;
    [Tooltip("Optional. Falls back to BattleManager.Instance.audioManager if left empty.")]
    public AudioManager audioManager;

    [Header("Encounter")]
    [Tooltip("Spawned when nothing is provided at runtime. Lets you Play the combat scene standalone.")]
    public EncounterData fallbackEncounter;

    /// <summary>Set by a map / RunManager before the combat scene loads. Wins over the fallback.</summary>
    public EncounterData PendingEncounter { get; set; }

    public EncounterData ResolveEncounter()
    {
        // Priority: an explicit override, then the run's chosen encounter (set by the map via
        // RunManager), then the local fallback (for Playing the combat scene standalone).
        if (PendingEncounter != null) return PendingEncounter;
        if (RunManager.Instance != null && RunManager.Instance.nextEncounter != null)
            return RunManager.Instance.nextEncounter;
        return fallbackEncounter;
    }

    /// <summary>
    /// Instantiates the resolved encounter and returns the spawned enemies, or null if
    /// there is nothing to spawn (so the caller can fall back to scene-placed enemies).
    /// </summary>
    public List<Enemy> SpawnForBattle()
    {
        var encounter = ResolveEncounter();
        var roster = encounter != null ? encounter.ResolveEnemies() : null;
        if (roster == null || roster.Count == 0)
        {
            Debug.Log("[EnemySpawner] No encounter to spawn; leaving scene-placed enemies as-is.");
            return null;
        }
        if (slots == null || slots.Length == 0)
        {
            Debug.LogWarning("[EnemySpawner] No spawn slots assigned; cannot spawn encounter.");
            return null;
        }

        // Resolve refs once, falling back to the BattleManager so a missed Inspector wire
        // doesn't leave enemies with null player/audio references.
        var p = player != null ? player
                : (BattleManager.Instance != null ? BattleManager.Instance.player : null);
        var am = audioManager != null ? audioManager
                : (BattleManager.Instance != null ? BattleManager.Instance.audioManager : null);

        var spawned = new List<Enemy>();
        for (int i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            EnemyData data = (i < roster.Count) ? roster[i] : null;

            // No enemy for this slot (or it's misconfigured): hide its HUD and skip.
            if (data == null || data.prefab == null || slot == null || slot.anchor == null)
            {
                if (data != null && data.prefab != null && (slot == null || slot.anchor == null))
                    Debug.LogWarning($"[EnemySpawner] Slot {i} has no anchor; cannot place '{data.enemyName}'.");
                HideSlotHud(slot);
                continue;
            }

            var go = Instantiate(data.prefab, slot.anchor);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;

            var enemy = go.GetComponent<Enemy>();
            if (enemy == null)
            {
                Debug.LogError($"[EnemySpawner] Prefab for '{data.enemyName}' has no Enemy component.");
                Destroy(go);
                HideSlotHud(slot);
                continue;
            }

            enemy.Init(data, p, am);   // runs before Start(), so Start()'s UpdateHPText sees the wired text
            WireSlotHud(slot, enemy);
            spawned.Add(enemy);
        }

        return spawned;
    }

    // Point this slot's shared HUD at the freshly spawned enemy.
    void WireSlotHud(EnemySlot slot, Enemy enemy)
    {
        if (slot == null) return;
        if (slot.hpText != null)
        {
            slot.hpText.gameObject.SetActive(true);
            enemy.hpText = slot.hpText;
        }
        if (slot.hpFollower != null)
            slot.hpFollower.target = enemy.transform;
    }

    // Hide an unused slot's HUD so it doesn't show a stale bar.
    void HideSlotHud(EnemySlot slot)
    {
        if (slot != null && slot.hpText != null)
            slot.hpText.gameObject.SetActive(false);
    }
}
