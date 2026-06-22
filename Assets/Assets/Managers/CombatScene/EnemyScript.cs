using UnityEngine;
using System.Collections.Generic;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

public class Enemy : MonoBehaviour
{
    [Header("Data (optional)")]
    [Tooltip("If set (by EnemySpawner, or assigned in the Inspector), these values override the fields below at Start.")]
    public EnemyData data;

    [Header("Stats")]
    public string enemyName;
    public int maxHP = 20;
    public int currentHP;
    public int baseAttackDamage = 4;

    [Header("Special Mechanics")]
    [Tooltip("MEI-I: if she SURVIVES a single hit of >= panicHealThreshold, she freaks out and heals to full.")]
    public bool panicHealOnBigHit = false;
    public int panicHealThreshold = 8;
    [Tooltip("If true, the panic heal can only trigger once per battle.")]
    public bool panicHealOncePerBattle = false;
    [Tooltip("Optional sting played when she freaks out and heals.")]
    public AudioClip panicSfx;

    [Tooltip("Ms. Remember: chance, each of her turns, to inflict a Decaying Mind stack (replaces a hand card with Forgotten).")]
    public bool appliesDecayingMind = false;
    [Range(0f, 1f)] public float decayingMindChance = 0.5f;

    [Header("Portrait Sprites (HP bands)")]
    public Sprite stableSprite;   // 66�100%
    public Sprite hurtSprite;     // 33�66%
    public Sprite criticalSprite; // 1�33%
    private Sprite currentSprite;

    [Header("Refs")]
    public Player player;
    public TextMeshProUGUI hpText;
    public AudioManager audioManager;

    [Header("Audio")]
    public AudioClip attackSound;
    public AudioClip damageSound;
    public AudioClip deathSound;

    [Header("Runtime")]
    public List<StatusEffect> activeEffects = new List<StatusEffect>();
    public bool IsDead { get; private set; }

    // Pluggable special mechanics (Phase 2): collected from EnemyAbility components on this
    // enemy at battle start, plus any bridged from legacy EnemyData flags. See EnemyAbility.
    private readonly List<EnemyAbility> abilities = new List<EnemyAbility>();
    private bool abilitiesReady = false;

    // What this enemy intends to do this turn — exposed for a future telegraph UI.
    public EnemyIntent CurrentIntent { get; private set; }
    public void SetIntent(EnemyIntent intent) => CurrentIntent = intent;

    [Header("Hit Reaction")]
    public float knockback = 0.15f;     // world units, punched away from the player
    public float squash = 0.12f;        // punch-scale amount
    public float hitStop = 0.06f;       // realtime seconds of freeze on impact
    public float shakeMin = 0.15f;
    public float shakeMax = 0.5f;
    public int shakeMaxDamage = 20;     // damage that maps to shakeMax

    private Animator animator;
    private Collider2D col2d;
    private Transform hitVisual;
    private HitFlash hitFlash;

    void Awake()
    {
        animator = GetComponent<Animator>();
        col2d = GetComponent<Collider2D>();
        var sr = GetComponentInChildren<SpriteRenderer>();
        hitVisual = sr ? sr.transform : transform;
        hitFlash = HitFlash.EnsureOn(gameObject);
    }

    void Start()
    {
        if (data != null) ApplyData();   // data-driven enemies override Inspector defaults
        IsDead = false;
        if (currentHP <= 0) currentHP = maxHP;
        currentHP = Mathf.Clamp(currentHP, 0, maxHP);
        currentSprite = stableSprite;
        UpdateHPText();
        UpdatePortraitBand();
        EnsureAbilities();
    }

    // ------------------- Data init -------------------

    // Populate this enemy from an EnemyData asset and wire scene refs. Called by
    // EnemySpawner right after Instantiate (after Awake, before Start), so Start()'s
    // setup runs against the data-driven values.
    public void Init(EnemyData source, Player player, AudioManager audioManager)
    {
        data = source;
        if (player != null) this.player = player;
        if (audioManager != null) this.audioManager = audioManager;
        ApplyData();
    }

    // Copies EnemyData values into the runtime fields. Separate from Init so Start() can
    // also apply data when an enemy is placed in the scene with a data asset assigned.
    private void ApplyData()
    {
        if (data == null) return;

        enemyName = data.enemyName;
        maxHP = data.maxHP;
        currentHP = data.maxHP;
        baseAttackDamage = data.baseAttackDamage;

        stableSprite = data.stableSprite;
        hurtSprite = data.hurtSprite;
        criticalSprite = data.criticalSprite;

        attackSound = data.attackSound;
        damageSound = data.damageSound;
        deathSound = data.deathSound;

        // Special mechanics (Phase 1: still flag-based; migrates to abilities in Phase 2).
        panicHealOnBigHit = data.panicHealOnBigHit;
        panicHealThreshold = data.panicHealThreshold;
        panicHealOncePerBattle = data.panicHealOncePerBattle;
        panicSfx = data.panicSfx;

        appliesDecayingMind = data.appliesDecayingMind;
        decayingMindChance = data.decayingMindChance;
    }

    // ------------------- Abilities (Phase 2) -------------------

    // Collect ability components (bridging legacy EnemyData flags into abilities first),
    // then initialise them. Idempotent and lazy, so it's safe to call from Start or from
    // the first hook that fires.
    private void EnsureAbilities()
    {
        if (abilitiesReady) return;
        abilitiesReady = true;

        // Bridge: legacy flag config becomes an ability component when one isn't already
        // attached to the prefab. Lets Phase 1 EnemyData assets keep working unchanged.
        if (panicHealOnBigHit && GetComponent<PanicHealAbility>() == null)
        {
            var ab = gameObject.AddComponent<PanicHealAbility>();
            ab.threshold = panicHealThreshold;
            ab.oncePerBattle = panicHealOncePerBattle;
            ab.healSfx = panicSfx;
        }
        if (appliesDecayingMind && GetComponent<DecayingMindAbility>() == null)
        {
            var ab = gameObject.AddComponent<DecayingMindAbility>();
            ab.chance = decayingMindChance;
        }

        abilities.Clear();
        GetComponents(abilities);          // every EnemyAbility on this enemy
        for (int i = 0; i < abilities.Count; i++) abilities[i].Initialize(this);
        for (int i = 0; i < abilities.Count; i++) abilities[i].OnBattleStart();
    }

    // Fan a "survived a hit" event out to reactive abilities (e.g. MEI-I's panic heal).
    private void NotifyDamaged(int amount)
    {
        EnsureAbilities();
        for (int i = 0; i < abilities.Count; i++)
            abilities[i].OnTookDamage(amount, survived: true);
    }

    // ------------------- Turn / Actions -------------------

    public void TakeTurn()
    {
        if (IsDead) return;

        if (ConsumeSleepOrStunIfPresent())
        {
            // Optional: play an idle/sleep anim
            // animator?.SetTrigger("SleepIdle");
            Debug.Log($"{enemyName} is asleep/stunned and skips the turn.");
            return;
        }

        EnsureAbilities();

        // An ability may fully own the turn (e.g. Critical Eye criticising or unleashing).
        bool handled = false;
        for (int i = 0; i < abilities.Count; i++)
        {
            if (abilities[i].TryTakeTurn()) { handled = true; break; }
        }

        if (!handled) DefaultAttack();

        // Post-action hooks (e.g. Ms. Remember rolling a Decaying Mind stack).
        for (int i = 0; i < abilities.Count; i++)
            abilities[i].OnActed();
    }

    // The basic "swing at the player" turn, used when no ability takes over.
    private void DefaultAttack()
    {
        int damage = CalculateOutgoingDamage();
        SetIntent(new EnemyIntent { kind = IntentKind.Attack, amount = damage });
        Debug.Log(enemyName + " attacks!");

        // Optional: your attack VFX
        // CombatVFXManager.Instance.PlayOnEnemy(VfxType.Slash, player.transform.position);

        if (player) player.TakeDamage(damage, this);

        if (audioManager && attackSound) audioManager.PlaySound(attackSound);
        if (animator) animator.SetTrigger("Attack");
    }

    public void ApplyPoison(int dmgPerTurn, int turns)
    {
        // Stack poison predictably: damage stacks, duration refreshes to max.
        StatusEffectStacking.AddOrStack(activeEffects, StatusType.Poison, dmgPerTurn, turns);
        Debug.Log($"{enemyName} is poisoned for {turns} turns (+{dmgPerTurn}/turn).");
        InsightHost.Instance?.OnApplyStatus(this, StatusType.Poison);
    }

    public void ApplySleep(int turns)
    {
        // Sleep doesn't stack power; refresh duration to max.
        StatusEffectStacking.AddOrStack(activeEffects, StatusType.Sleep, 0, turns, stackPower: false, refreshDurationToMax: true);
        Debug.Log($"{enemyName} sleeps for {turns} turn(s).");
        InsightHost.Instance?.OnApplyStatus(this, StatusType.Sleep);
    }

    // Package E helpers (data-driven card effects)
    public void ApplyAttackBreak(int power, int turns)
    {
        power = Mathf.Max(0, power);
        turns = Mathf.Max(0, turns);
        StatusEffectStacking.AddOrStack(activeEffects, StatusType.AttackBreak, power, turns);
        Debug.Log($"{enemyName} attack broken for {turns} turn(s) (-{power}).");
        InsightHost.Instance?.OnApplyStatus(this, StatusType.AttackBreak);
    }

    public void ApplyCorrode(int power, int turns)
    {
        power = Mathf.Max(0, power);
        turns = Mathf.Max(0, turns);
        StatusEffectStacking.AddOrStack(activeEffects, StatusType.Corrode, power, turns);
        Debug.Log($"{enemyName} corroded for {turns} turn(s) (+{power}).");
        InsightHost.Instance?.OnApplyStatus(this, StatusType.Corrode);
    }

    public void Heal(int amount)
    {
        if (IsDead) return;
        currentHP = Mathf.Min(currentHP + amount, maxHP);
        DamageNumbers.ShowHeal(transform.position, amount);
        Debug.Log($"{enemyName} healed by {amount}, HP = {currentHP}");
        UpdateHPText();
        UpdatePortraitBand();
    }

    public void TakeDamage(int amount)
    {
        if (IsDead) return;

        amount = CalculateIncomingDamage(amount);

        // Player card hit: let Insights modify the outgoing damage before it lands (Critique, Deep
        // Pigment, Fine Detail, ...). Poison ticks / reflect / counter don't set this flag, so they're
        // unaffected.
        if (InsightHost.DealingCardDamage && InsightHost.Instance != null)
            amount = InsightHost.Instance.ModifyDamageToEnemy(this, amount, InsightHost.DealingCardDamageIsAoe);

        currentHP -= amount;
        DamageNumbers.ShowDamage(transform.position, amount);
        Debug.Log($"{enemyName} took {amount} damage, HP = {currentHP}");

        UpdateHPText();

        // Post-hit Insight reactions, player card hits only (hook wired; reaction effects land later).
        if (InsightHost.DealingCardDamage)
            InsightHost.Instance?.OnDealtDamage(this, amount);

        if (currentHP <= 0)
        {
            Die();
            return;
        }

        if (animator) animator.SetTrigger("Hurt");
        if (audioManager && damageSound) audioManager.PlaySound(damageSound);

        PlayHitReaction(amount);

        // Reactive abilities (e.g. MEI-I's panic heal) respond to surviving the hit.
        NotifyDamaged(amount);

        UpdatePortraitBand();
    }

    // Called at the START of this enemy's turn, before it acts. Damage-over-time
    // ticks here ("ticks before actions") and can kill the enemy before it attacks.
    // Sleep/Stun are intentionally NOT decayed here — they're consumed when the enemy
    // attempts to act (ConsumeSleepOrStunIfPresent), so they skip a turn instead of
    // expiring before the skip check ever runs.
    public void TickTurnStartStatuses()
    {
        if (IsDead) return;

        for (int i = activeEffects.Count - 1; i >= 0; i--)
        {
            var eff = activeEffects[i];
            if (eff == null) { activeEffects.RemoveAt(i); continue; }

            // Crowd-control decays on the action attempt, not on the tick.
            if (eff.type == StatusType.Sleep || eff.type == StatusType.Stun)
                continue;

            if (eff.type == StatusType.Poison)
            {
                int dmg = Mathf.Max(1, eff.power);
                currentHP -= dmg;
                DamageNumbers.ShowPoison(transform.position, dmg);
                Debug.Log($"{enemyName} took {dmg} poison damage, HP = {currentHP}");
                UpdateHPText();

                if (currentHP <= 0)
                {
                    Die();
                    return; // stop further processing; this enemy is gone
                }

                // Make the tick noticeable. Lighter than a full hit reaction
                // (no knockback/camera shake) since this fires every turn.
                if (hitFlash) hitFlash.Flash(0.06f, new Color(0.6f, 1f, 0.5f)); // poison-green
                if (animator) animator.SetTrigger("Hurt");
                if (audioManager && damageSound) audioManager.PlaySound(damageSound);
            }

            // Poison and the passive timed debuffs (Corrode, AttackBreak, ...) decay once per round.
            eff.duration--;
            if (eff.duration <= 0)
            {
                Debug.Log($"{enemyName}'s {eff.type} ended.");
                activeEffects.RemoveAt(i);
            }
        }

        UpdatePortraitBand();
    }

    // Returns true if the enemy is asleep/stunned (and consumes one turn of it),
    // meaning it should skip its action this turn.
    private bool ConsumeSleepOrStunIfPresent()
    {
        bool skip = false;
        for (int i = activeEffects.Count - 1; i >= 0; i--)
        {
            var eff = activeEffects[i];
            if (eff == null) continue;
            if (eff.type != StatusType.Sleep && eff.type != StatusType.Stun) continue;

            skip = true;
            eff.duration--;
            if (eff.duration <= 0)
            {
                Debug.Log($"{enemyName}'s {eff.type} ended.");
                activeEffects.RemoveAt(i);
            }
        }
        return skip;
    }

    // ------------------- Helpers / State -------------------

    // Impact feedback when this enemy is struck by a card.
    private void PlayHitReaction(int amount)
    {
        if (!hitVisual) return;

        if (hitFlash) hitFlash.Flash(0.07f, Color.white);

        hitVisual.DOKill(true);
        hitVisual.DOPunchScale(Vector3.one * squash, 0.25f, 8, 0.9f);

        Vector3 dir = player ? (transform.position - player.transform.position) : Vector3.right;
        dir.z = 0f;
        if (dir.sqrMagnitude < 0.0001f) dir = Vector3.right;
        hitVisual.DOPunchPosition(dir.normalized * knockback, 0.25f, 8, 0.9f);

        if (CameraShakeManager.Instance)
        {
            float k = shakeMaxDamage > 0 ? Mathf.Clamp01((float)amount / shakeMaxDamage) : 1f;
            CameraShakeManager.Instance.Shake(Mathf.Lerp(shakeMin, shakeMax, k));
        }

        if (CombatVFXManager.Instance)
            CombatVFXManager.Instance.HitStop(hitStop);
    }

    private void Die()
    {
        if (IsDead) return;
        IsDead = true;

        // Untargetable & no more raycasts
        if (col2d) col2d.enabled = false;
        gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");
        var th = GetComponent<TargetHighlighter>();
        if (th) th.SetHighlighted(false);

        // SFX/Anim
        if (audioManager && deathSound) audioManager.PlaySound(deathSound);
        if (animator) animator.SetTrigger("Death");

        // Let abilities react to death (cleanup / on-death effects).
        for (int i = 0; i < abilities.Count; i++)
            abilities[i].OnDied();

        // Inform manager immediately (triggers instant victory if last)
        if (BattleManager.Instance) BattleManager.Instance.OnEnemyDied(this);
    }

    private int CalculateOutgoingDamage()
    {
        if (IsAsleepOrStunned()) return 0;
        return ApplyOutgoingModifiers(baseAttackDamage);
    }

    // Applies the enemy's outgoing-damage debuffs (AttackBreak, DefensiveStance). Public so
    // abilities (e.g. Critical Eye's big hit) route their damage through the same modifiers.
    public int ApplyOutgoingModifiers(int damage)
    {
        foreach (var eff in activeEffects)
        {
            if (eff.type == StatusType.AttackBreak)
                damage = Mathf.Max(0, damage - eff.power);
            if (eff.type == StatusType.DefensiveStance)
                damage = Mathf.Min(damage, 1); // "all damage = 1" style
        }
        return Mathf.Max(0, damage);
    }

    private int CalculateIncomingDamage(int rawDamage)
    {
        bool corroded = false;

        foreach (var eff in activeEffects)
        {
            if (eff.type == StatusType.Corrode) corroded = true;
            if (eff.type == StatusType.DefensiveStance)
                rawDamage = Mathf.Min(rawDamage, 1); // If you want enemy to also be reduced
        }

        if (corroded)
            rawDamage = Mathf.CeilToInt(rawDamage * 1.25f);

        return Mathf.Max(0, rawDamage);
    }

    private bool IsAsleepOrStunned()
    {
        for (int i = 0; i < activeEffects.Count; i++)
        {
            var t = activeEffects[i].type;
            if (t == StatusType.Sleep || t == StatusType.Stun) return true;
        }
        return false;
    }

    private void UpdateHPText()
    {
        if (hpText) hpText.text = $"{Mathf.Max(0, currentHP)}/{maxHP}";
    }

    private void UpdatePortraitBand()
    {
        if (maxHP <= 0) return;
        float ratio = (float)Mathf.Max(0, currentHP) / maxHP;

        if (ratio > 0.66f) currentSprite = stableSprite;
        else if (ratio > 0.33f) currentSprite = hurtSprite;
        else if (ratio > 0f) currentSprite = criticalSprite;
        // else: dead � portrait often handled by death anim / keep last

        // If you want an immediate portrait refresh while this enemy is �active� in UI,
        // you could ping BattleManager to update the portrait here conditionally.
    }

    // ------------------- Public getters / ability helpers -------------------

    public Sprite getSprite() => currentSprite;
    public int GetHP() => currentHP;

    // Lets abilities drive this enemy's Animator (which is private).
    public void PlayAnimTrigger(string trigger)
    {
        if (animator && !string.IsNullOrEmpty(trigger)) animator.SetTrigger(trigger);
    }
}
