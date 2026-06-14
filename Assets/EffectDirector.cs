using UnityEngine;
using System;
using System.Collections.Generic;

public enum EffectKey
{
    None,
    RedStroke,
    Siphon,
    RecklessStroke,
    AttackBreak,
    PoisonST,
    Sleep,
    Corrode,
    ImaginaryPaint,
    CrushingPaint,
    FinishingTouch,
    PoolOfPaint,
    ToxicPaint,

    // Player
    Restore,
    Rejuvenate,
    CreativeFreedom,

    // AoE
    RedSplatter,
    YSpray,
}

[Serializable]
public struct TypeColor
{
    public string typeName;   // e.g. "Red", "Crimson", "Blue", "Yellow", ...
    public Color color;       // Use HDR + Bloom for a real glow
}

public class EffectDirector : MonoBehaviour
{
    public static EffectDirector Instance;

    [Header("Hosts")]
    public EffectAnimatorHost playerSingleTargetHost; // for player ST hits / fallback
    public EffectAnimatorHost aoeHost;                // center-screen AoE host

    [Header("Type Colors")]
    public List<TypeColor> typeColors = new();        // fill in Inspector

    void Awake()
    {
        if (Instance == null) Instance = this;
    }

    /// <summary>
    /// Play a single-target effect on an enemy. Optional tint.
    /// </summary>
    public void PlayEnemyHit(Enemy enemy, EffectKey key, AudioClip sfx = null, float volume = 1f, Color? tint = null)
    {
        if (!enemy) return;
        var host = enemy.GetComponentInChildren<EffectAnimatorHost>(true) ?? playerSingleTargetHost;
        if (!host) return;

        var trig = TriggerFor(key);
        if (string.IsNullOrEmpty(trig))
        {
            Debug.LogWarning($"[EffectDirector] No trigger mapped for {key}. Host={host.name}");
            return;
        }
        host.Play(trig, sfx, volume, tint);
    }

    /// <summary>
    /// Play a single-target effect on the player. Optional tint.
    /// </summary>
    public void PlayPlayerHit(EffectKey key, AudioClip sfx = null, float volume = 1f, Color? tint = null)
    {
        if (!playerSingleTargetHost) return;
        var trig = TriggerFor(key);
        if (string.IsNullOrEmpty(trig))
        {
            Debug.LogWarning($"[EffectDirector] No trigger mapped for {key}. Host={playerSingleTargetHost.name}");
            return;
        }
        playerSingleTargetHost.Play(trig, sfx, volume, tint);

    }

    /// <summary>
    /// Play an AoE effect in the center screen. Optional tint.
    /// </summary>
    public void PlayAoe(EffectKey key, AudioClip sfx = null, float volume = 1f, Color? tint = null)
    {
        if (!aoeHost) return;
        var trig = TriggerFor(key);
        if (string.IsNullOrEmpty(trig))
        {
            Debug.LogWarning($"[EffectDirector] No trigger mapped for {key}. Host={aoeHost.name}");
            return;
        }
        aoeHost.Play(trig, sfx, volume, tint);
    }

    /// <summary>
    /// Resolve a tint color from CardData.cardType (string). Falls back if not found.
    /// </summary>
    public Color ResolveTypeColor(string cardType, Color fallback)
    {
        if (string.IsNullOrEmpty(cardType)) return fallback;
        for (int i = 0; i < typeColors.Count; i++)
        {
            if (string.Equals(typeColors[i].typeName, cardType, StringComparison.OrdinalIgnoreCase))
                return typeColors[i].color;
        }
        return fallback;
    }

    // 
    public EffectAnimatorHost GetEnemyHost(Enemy enemy)
    {
        if (!enemy) return playerSingleTargetHost; // fallback
        var host = enemy.GetComponentInChildren<EffectAnimatorHost>(includeInactive: true);
        return host ? host : playerSingleTargetHost;
    }

    // Map EffectKey -> Animator Trigger string (match your Animator parameter names)
    private static readonly Dictionary<EffectKey, string> triggerMap = new()
    {
        // single-target
        { EffectKey.RedStroke,       "Red Stroke" },
        { EffectKey.Siphon,          "Siphon" },
        { EffectKey.RecklessStroke,  "Reckless Stroke" },
        { EffectKey.AttackBreak,     "Attack Break" },
        { EffectKey.PoisonST,        "Poison" },
        { EffectKey.Sleep,           "Sleep" },
        { EffectKey.Corrode,         "Corrode" },
        { EffectKey.ImaginaryPaint,  "Imaginary Paint" },
        { EffectKey.CrushingPaint,   "Crushing Paint" },
        { EffectKey.FinishingTouch,  "Finishing Touch" },
        { EffectKey.PoolOfPaint, "Pool of Paint" },
        { EffectKey.ToxicPaint, "Toxic Paint" },

        // AoE
        { EffectKey.RedSplatter,     "Red Splatter" },
        { EffectKey.YSpray,          "Y-Spray" },

        // Player
        { EffectKey.Restore,       "Restore" },
        { EffectKey.Rejuvenate,      "Rejuvenate" },
        { EffectKey.CreativeFreedom,      "CreativeFreedom" },
    };

    private string TriggerFor(EffectKey key)
    {
        return triggerMap.TryGetValue(key, out var trig) ? trig : null;
    }
}
