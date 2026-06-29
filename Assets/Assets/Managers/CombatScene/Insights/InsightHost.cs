using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The player's mirror of <see cref="Enemy"/>'s ability system: it collects the run's active
/// Insights and broadcasts lifecycle hooks at the moments combat reaches them. Where Enemy holds
/// concrete <see cref="EnemyAbility"/> MonoBehaviours, the host is data-driven — each Insight is an
/// authored <see cref="Insight"/> asset and the behaviour is resolved from its <c>effectId</c>
/// (the same shape as CardEffectRegistry's "Package E"). That keeps the ~186 designed Insights as
/// data + one switch case each, instead of a class per Insight.
///
/// Two resolution paths:
///  - <b>Reactions</b> (most effects): a hook fires <see cref="Fire"/> → <see cref="Resolve"/>,
///    honouring once-per-turn / once-per-combat gating.
///  - <b>Pre-hit damage modifiers</b> (Critique, Deep Pigment, ...): <see cref="ModifyDamageToEnemy"/>
///    is consulted by <see cref="Enemy.TakeDamage"/> while a player card is dealing damage, BEFORE
///    the hit lands, so "+X damage" actually changes the number.
///
/// Broadcasters: BattleManager (combat/turn/kill/play-card), Player (took damage / heal),
/// FusionController (fuse), Enemy (dealt damage / apply status). Put one InsightHost in the Combat
/// scene and assign an InsightDatabase; for standalone testing drop assets into <see cref="debugInsights"/>.
/// </summary>
public class InsightHost : MonoBehaviour
{
    public static InsightHost Instance { get; private set; }

    [Header("Refs")]
    [Tooltip("Junior. Auto-found from BattleManager / scene if left empty.")]
    [SerializeField] private Player player;

    [Tooltip("All authored Insights, so the run's id list can be resolved into assets.")]
    [SerializeField] private InsightDatabase database;

    /// <summary>The authored Insight database (read by the reward screen to offer Insight choices).</summary>
    public InsightDatabase Database => database;

    [Header("Testing (no active run)")]
    [Tooltip("Insights granted when there's no active run (standalone combat). Mirrors EnemySpawner.fallbackEncounter / FusionBook.unlockAllForTesting.")]
    [SerializeField] private List<Insight> debugInsights = new();

    // One entry per distinct Insight held this combat; copies tracks duplicates for stacking.
    private class InsightRuntime
    {
        public Insight def;
        public int copies = 1;
        public bool usedThisTurn;
        public bool usedThisCombat;
    }

    private readonly List<InsightRuntime> active = new();

    // ---- ambient context for the current broadcast (read by Resolve) ----
    private CardData ctxCard;                              // OnPlayCard
    private CardData ctxFuseA, ctxFuseB, ctxFuseResult;   // OnFuse
    private Enemy ctxStatusTarget;                         // OnApplyStatus
    private StatusType ctxStatusType;
    private bool inHeal;          // recursion guard: a heal triggered by a heal-Insight
    private bool inApplyStatus;   // recursion guard: a status applied by a status-Insight

    // ---- ambient flag: a player card is currently dealing damage to an enemy ----
    // Set around the card's impact callback (Card.STWithImpact/AOEWithImpact) so Enemy.TakeDamage
    // can tell a card hit (which Insights modify) from poison ticks / reflect / counter (which they don't).
    public static bool DealingCardDamage { get; private set; }
    public static bool DealingCardDamageIsAoe { get; private set; }
    public static void BeginCardDamage(bool isAoe) { DealingCardDamage = true; DealingCardDamageIsAoe = isAoe; }
    public static void EndCardDamage() { DealingCardDamage = false; DealingCardDamageIsAoe = false; }

    void Awake()
    {
        if (Instance == null) Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        EndCardDamage();
    }

    private Player PlayerRef
    {
        get
        {
            if (player != null) return player;
            if (BattleManager.Instance != null && BattleManager.Instance.player != null)
                player = BattleManager.Instance.player;
            else
                player = FindFirstObjectByType<Player>();
            return player;
        }
    }

    // ----------------------------------------------------------------- apply the run's build

    /// <summary>
    /// Rebuild the active set for this combat. Sources ids from the run (so Insights persist across
    /// the map↔combat boundary); falls back to <see cref="debugInsights"/> for isolated testing.
    /// Call once at combat start, before OnCombatStart. Resets per-combat gating.
    /// </summary>
    public void ApplyFromRun()
    {
        active.Clear();

        var rm = RunManager.Instance;
        bool fromRun = rm != null && rm.runActive && rm.insights != null && rm.insights.Count > 0;

        if (fromRun)
        {
            foreach (var id in rm.insights) AddById(id);
        }
        else
        {
            foreach (var ins in debugInsights) AddInsight(ins);
        }

        Debug.Log($"[InsightHost] Applied {active.Count} insight(s) for this combat (source: {(fromRun ? "run" : "debug")}).");
    }

    private void AddById(string id)
    {
        if (database == null)
        {
            Debug.LogWarning("[InsightHost] No InsightDatabase assigned; cannot resolve run insights.");
            return;
        }
        var ins = database.Find(id);
        if (ins == null)
        {
            Debug.LogWarning($"[InsightHost] Insight id '{id}' not found in database.");
            return;
        }
        AddInsight(ins);
    }

    private void AddInsight(Insight ins)
    {
        if (ins == null) return;
        for (int i = 0; i < active.Count; i++)
        {
            if (active[i].def == ins) { active[i].copies++; return; }
        }
        active.Add(new InsightRuntime { def = ins, copies = 1 });
    }

    // ----------------------------------------------------------------- hooks (broadcasters call these)

    public void OnCombatStart() => Fire(InsightTrigger.CombatStart);

    public void OnTurnStart()
    {
        for (int i = 0; i < active.Count; i++) active[i].usedThisTurn = false; // reset per-turn gating
        Fire(InsightTrigger.TurnStart);
    }

    public void OnTurnEnd() => Fire(InsightTrigger.TurnEnd);

    public void OnPlayCard(CardData card)
    {
        ctxCard = card;
        Fire(InsightTrigger.PlayCard);
        ctxCard = null;
    }

    public void OnFuse(CardData a, CardData b, CardData result)
    {
        ctxFuseA = a; ctxFuseB = b; ctxFuseResult = result;
        Fire(InsightTrigger.Fuse);
        ctxFuseA = ctxFuseB = ctxFuseResult = null;
    }

    public void OnTookDamage(int amount) => Fire(InsightTrigger.TookDamage);

    public void OnDealtDamage(Enemy target, int amount) => Fire(InsightTrigger.DealtDamage);

    public void OnKill(Enemy victim) => Fire(InsightTrigger.Kill);

    public void OnHeal(int amount)
    {
        if (inHeal) return;            // a heal-triggered Insight that heals must not re-trigger
        inHeal = true;
        Fire(InsightTrigger.Heal);
        inHeal = false;
    }

    public void OnApplyStatus(Enemy target, StatusType type)
    {
        if (inApplyStatus) return;     // a status-triggered Insight that applies a status must not re-trigger
        inApplyStatus = true;
        ctxStatusTarget = target;
        ctxStatusType = type;
        Fire(InsightTrigger.ApplyStatus);
        ctxStatusTarget = null;
        inApplyStatus = false;
    }

    // ----------------------------------------------------------------- resolution

    private void Fire(InsightTrigger trigger)
    {
        // Effects never add to `active`, so a plain index loop is safe.
        for (int i = 0; i < active.Count; i++)
        {
            var r = active[i];
            if (r?.def == null || r.def.trigger != trigger) continue;
            if (r.def.oncePerCombat && r.usedThisCombat) continue;
            if (r.def.oncePerTurn && r.usedThisTurn) continue;

            if (Resolve(r))
            {
                if (r.def.oncePerTurn) r.usedThisTurn = true;
                if (r.def.oncePerCombat) r.usedThisCombat = true;
            }
        }
    }

    // Effect magnitude with stacking folded in.
    private static int Scaled(InsightRuntime r)
    {
        int n = r.def.amount;
        if (r.def.stacks && r.copies > 1) n += r.def.perCopyAmount * (r.copies - 1);
        return Mathf.Max(0, n);
    }

    // Returns true if the effect actually applied (so gating only burns when it fired).
    private bool Resolve(InsightRuntime r)
    {
        var def = r.def;
        int n = Scaled(r);
        var p = PlayerRef;

        switch (def.effectId)
        {
            // ---- start/turn/utility ----
            case "GAIN_AP_THIS_TURN":
                if (BattleManager.Instance == null) return false;
                BattleManager.Instance.GrantAP(n);
                Debug.Log($"[Insight] {def.displayName}: +{n} AP this turn.");
                return true;

            case "GAIN_SHIELD":
                if (p == null) return false;
                p.AddShield(n);
                Debug.Log($"[Insight] {def.displayName}: +{n} Shield.");
                return true;

            case "HEAL":
                if (p == null) return false;
                p.Heal(n);
                Debug.Log($"[Insight] {def.displayName}: heal {n}.");
                return true;

            case "DRAW_CARDS":
                return DrawExtra(n, def);

            case "REDUCE_POISON": // e.g. Restorative Wash (OnHeal)
                if (p == null) return false;
                p.ReducePoison(Mathf.Max(1, n));
                Debug.Log($"[Insight] {def.displayName}: reduce Poison {Mathf.Max(1, n)}.");
                return true;

            // ---- colour "On Play a <colour> card" reactions (param = colour filter, blank = any) ----
            case "ON_PLAY_DAMAGE_RANDOM":
                if (!MatchesColor(ctxCard, def.param)) return false;
                DamageRandomEnemy(n);
                Debug.Log($"[Insight] {def.displayName}: {n} dmg to a random enemy.");
                return true;

            case "ON_PLAY_SHIELD":
                if (!MatchesColor(ctxCard, def.param) || p == null) return false;
                p.AddShield(n);
                Debug.Log($"[Insight] {def.displayName}: +{n} Shield.");
                return true;

            case "ON_PLAY_DRAW":
                if (!MatchesColor(ctxCard, def.param)) return false;
                return DrawExtra(n, def);

            // ---- fusion reactions (param = colour involved, blank = any) ----
            case "ON_FUSE_DRAW":
                if (!FuseMatchesColor(def.param)) return false;
                return DrawExtra(n, def);

            case "ON_FUSE_SHIELD":
                if (!FuseMatchesColor(def.param) || p == null) return false;
                p.AddShield(n);
                Debug.Log($"[Insight] {def.displayName}: +{n} Shield.");
                return true;

            // ---- kill reactions ----
            case "ON_KILL_DAMAGE_RANDOM": // e.g. Edge Highlight
                DamageRandomEnemy(n);
                Debug.Log($"[Insight] {def.displayName}: {n} dmg to a random enemy.");
                return true;

            // ---- status reactions (param = status name filter, blank = any) ----
            case "ON_STATUS_DEAL_DAMAGE": // e.g. Ink Spill (Poison), Tough Love (AttackBreak/Corrode)
                if (ctxStatusTarget == null || ctxStatusTarget.IsDead) return false;
                if (!MatchesStatus(ctxStatusType, def.param)) return false;
                ctxStatusTarget.TakeDamage(Mathf.Max(1, n));
                Debug.Log($"[Insight] {def.displayName}: {Mathf.Max(1, n)} dmg on applying {ctxStatusType}.");
                return true;

            // ---- pre-hit damage modifiers: handled in ModifyDamageToEnemy, not on broadcast ----
            case "DAMAGE_BONUS":
            case "DAMAGE_BONUS_ABOVE_HP":
            case "DAMAGE_BONUS_BELOW_HP":
            case "DAMAGE_BONUS_PERCENT":
            case "DAMAGE_BONUS_ST":
            case "DAMAGE_BONUS_AOE":
                return false;

            default:
                Debug.LogWarning($"[Insight] {def.displayName}: effectId '{def.effectId}' not implemented yet.");
                return false;
        }
    }

    /// <summary>
    /// Pre-hit damage modifier, called by <see cref="Enemy.TakeDamage"/> while a player card is
    /// dealing damage. Sums flat + percentage bonuses from DealtDamage Insights whose condition
    /// (target HP band, single-target vs AoE) matches. No-op when none are held.
    /// </summary>
    public int ModifyDamageToEnemy(Enemy target, int dmg, bool isAoe)
    {
        if (target == null || dmg <= 0) return dmg;

        int flat = 0;
        int pct = 0;
        for (int i = 0; i < active.Count; i++)
        {
            var r = active[i];
            if (r?.def == null || r.def.trigger != InsightTrigger.DealtDamage) continue;
            int n = Scaled(r);
            switch (r.def.effectId)
            {
                case "DAMAGE_BONUS": flat += n; break;
                case "DAMAGE_BONUS_ABOVE_HP": if (HpRatio(target) > r.def.amount2 / 100f) flat += n; break;
                case "DAMAGE_BONUS_BELOW_HP": if (HpRatio(target) < r.def.amount2 / 100f) flat += n; break;
                case "DAMAGE_BONUS_PERCENT": pct += n; break;
                case "DAMAGE_BONUS_ST": if (!isAoe) flat += n; break;
                case "DAMAGE_BONUS_AOE": if (isAoe) flat += n; break;
            }
        }

        if (flat == 0 && pct == 0) return dmg;
        int result = dmg + flat + Mathf.RoundToInt(dmg * pct / 100f);
        return Mathf.Max(0, result);
    }

    // ----------------------------------------------------------------- helpers

    private bool DrawExtra(int count, Insight def)
    {
        var hm = BattleManager.Instance != null ? BattleManager.Instance.handManager : null;
        if (hm == null || count <= 0) return false;
        hm.DrawExtra(count);
        Debug.Log($"[Insight] {def.displayName}: draw {count}.");
        return true;
    }

    private void DamageRandomEnemy(int dmg)
    {
        var bm = BattleManager.Instance;
        if (bm == null || bm.enemies == null || dmg <= 0) return;

        var alive = new List<Enemy>();
        foreach (var e in bm.enemies)
            if (e != null && !e.IsDead && e.GetHP() > 0) alive.Add(e);
        if (alive.Count == 0) return;

        alive[UnityEngine.Random.Range(0, alive.Count)].TakeDamage(dmg);
    }

    private static float HpRatio(Enemy e)
        => (e != null && e.maxHP > 0) ? (float)e.currentHP / e.maxHP : 0f;

    // True when the played card's colour (cardType) matches the filter, or the filter is blank.
    private static bool MatchesColor(CardData card, string param)
    {
        if (string.IsNullOrWhiteSpace(param)) return true;
        return card != null && string.Equals(card.cardType, param.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    // True when any fusion participant (ingredients or result) matches the colour filter, or it's blank.
    private bool FuseMatchesColor(string param)
    {
        if (string.IsNullOrWhiteSpace(param)) return true;
        return MatchesColor(ctxFuseA, param) || MatchesColor(ctxFuseB, param) || MatchesColor(ctxFuseResult, param);
    }

    // True when the applied status matches the (comma-separated) filter, or the filter is blank.
    private static bool MatchesStatus(StatusType type, string param)
    {
        if (string.IsNullOrWhiteSpace(param)) return true;
        string name = type.ToString();
        foreach (var part in param.Split(','))
            if (string.Equals(part.Trim(), name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
