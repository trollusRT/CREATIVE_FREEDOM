using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The player's mirror of <see cref="Enemy"/>'s ability system: it collects the run's active
/// Insights and broadcasts lifecycle hooks at the moments combat reaches them. Where Enemy holds
/// concrete <see cref="EnemyAbility"/> MonoBehaviours, the host is data-driven — each Insight is an
/// authored <see cref="Insight"/> asset and the behaviour is resolved from its <c>effectId</c>
/// (the same shape as CardEffectRegistry's "Package E"). That keeps ~186 designed Insights as data
/// + one switch case each, instead of a class per Insight.
///
/// Broadcasters call the On* hooks: BattleManager (combat/turn/kill/play-card), Player (took damage),
/// FusionController (fuse). Put one InsightHost in the Combat scene and assign an InsightDatabase.
/// For standalone testing with no active run, drop Insight assets into <see cref="debugInsights"/>.
/// </summary>
public class InsightHost : MonoBehaviour
{
    public static InsightHost Instance { get; private set; }

    [Header("Refs")]
    [Tooltip("Junior. Auto-found from BattleManager / scene if left empty.")]
    [SerializeField] private Player player;

    [Tooltip("All authored Insights, so the run's id list can be resolved into assets.")]
    [SerializeField] private InsightDatabase database;

    [Header("Testing (no active run)")]
    [Tooltip("Insights granted when there's no active run (standalone combat). Mirrors EnemySpawner.fallbackEncounter / FusionBook.unlockAllForTesting.")]
    [SerializeField] private List<Insight> debugInsights = new();

    // One entry per distinct Insight held this combat; copies tracks duplicates for stacking.
    private class InsightRuntime
    {
        public Insight def;
        public int copies = 1;
    }

    private readonly List<InsightRuntime> active = new();

    void Awake()
    {
        if (Instance == null) Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
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
    /// Call this once at combat start, before OnCombatStart.
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
    public void OnTurnStart() => Fire(InsightTrigger.TurnStart);
    public void OnTurnEnd() => Fire(InsightTrigger.TurnEnd);
    public void OnPlayCard(CardData card) => Fire(InsightTrigger.PlayCard);
    public void OnFuse(CardData a, CardData b, CardData result) => Fire(InsightTrigger.Fuse);
    public void OnTookDamage(int amount) => Fire(InsightTrigger.TookDamage);
    public void OnKill(Enemy victim) => Fire(InsightTrigger.Kill);
    public void OnHeal(int amount) => Fire(InsightTrigger.Heal);

    // ----------------------------------------------------------------- resolution

    private void Fire(InsightTrigger trigger)
    {
        // Effects never mutate `active`, so a plain index loop is safe.
        for (int i = 0; i < active.Count; i++)
        {
            var r = active[i];
            if (r?.def == null || r.def.trigger != trigger) continue;
            Resolve(r);
        }
    }

    // Effect magnitude with stacking folded in.
    private static int Scaled(InsightRuntime r)
    {
        int n = r.def.amount;
        if (r.def.stacks && r.copies > 1) n += r.def.perCopyAmount * (r.copies - 1);
        return Mathf.Max(0, n);
    }

    private void Resolve(InsightRuntime r)
    {
        var def = r.def;
        int n = Scaled(r);
        var p = PlayerRef;

        switch (def.effectId)
        {
            case "GAIN_AP_THIS_TURN":
                if (BattleManager.Instance != null) BattleManager.Instance.GrantAP(n);
                Debug.Log($"[Insight] {def.displayName}: +{n} AP this turn.");
                break;

            case "GAIN_SHIELD":
                if (p != null) p.AddShield(n);
                Debug.Log($"[Insight] {def.displayName}: +{n} Shield.");
                break;

            case "HEAL":
                if (p != null) p.Heal(n);
                Debug.Log($"[Insight] {def.displayName}: heal {n}.");
                break;

            case "DRAW_CARDS":
                if (BattleManager.Instance != null && BattleManager.Instance.handManager != null)
                    BattleManager.Instance.handManager.DrawExtra(n);
                Debug.Log($"[Insight] {def.displayName}: draw {n}.");
                break;

            default:
                Debug.LogWarning($"[Insight] {def.displayName}: effectId '{def.effectId}' not implemented yet.");
                break;
        }
    }
}
