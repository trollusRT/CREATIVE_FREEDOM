using UnityEngine;

/// <summary>
/// Authored "stat card" for an enemy, mirroring CardData. One asset per unique enemy.
/// EnemySpawner instantiates <see cref="prefab"/> and Enemy.Init() copies these values
/// into the runtime fields, so all stats/visuals/audio/mechanics live in data, not in
/// hand-placed scene instances.
///
/// Phase 1: special mechanics are still the existing flag set so behaviour is identical.
/// Phase 2 will replace the flags here with a list of pluggable EnemyAbility configs.
/// </summary>
[CreateAssetMenu(fileName = "NewEnemyData", menuName = "Enemies/Enemy Data")]
public class EnemyData : ScriptableObject
{
    [Header("Identity")]
    public string enemyName = "Enemy";

    [Tooltip("Visual rig spawned into the combat scene. The prefab must carry: an Enemy " +
             "component, a Collider2D, a child EffectAnimatorHost, a TargetHighlighter, an " +
             "Animator, and its own HP text.")]
    public GameObject prefab;

    [Header("Stats")]
    public int maxHP = 20;
    public int baseAttackDamage = 4;

    [Header("Portrait (HP bands)")]
    public Sprite stableSprite;    // 66-100%
    public Sprite hurtSprite;      // 33-66%
    public Sprite criticalSprite;  // 1-33%

    [Header("Faceoff (intro stinger)")]
    [Tooltip("Dramatic slide-in portrait shown on the BattleIntroStinger (e.g. MEI-I / Ms. Remember SlidePortrait).")]
    public Sprite slidePortrait;

    [Header("Audio")]
    public AudioClip attackSound;
    public AudioClip damageSound;
    public AudioClip deathSound;

    // Convenience flags: at battle start Enemy bridges these into a PanicHealAbility /
    // DecayingMindAbility when the prefab doesn't already have one. For richer config — or
    // for Critical Eye — attach EnemyAbility components to the prefab directly instead.
    [Header("Special Mechanics")]
    [Tooltip("MEI-I: if she SURVIVES a single hit of >= panicHealThreshold, she heals to full.")]
    public bool panicHealOnBigHit = false;
    public int panicHealThreshold = 8;
    [Tooltip("If true, the panic heal can only trigger once per battle.")]
    public bool panicHealOncePerBattle = false;
    [Tooltip("Optional sting played when she freaks out and heals.")]
    public AudioClip panicSfx;

    [Tooltip("Ms. Remember: chance, each of her turns, to inflict a Decaying Mind stack.")]
    public bool appliesDecayingMind = false;
    [Range(0f, 1f)] public float decayingMindChance = 0.5f;
}
