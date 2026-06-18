using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

public class BattleManager : MonoBehaviour
{
    public static BattleManager Instance;

    [Header("Turn pacing")]
    [Tooltip("Pause after the player ends their turn before enemies begin.")]
    public float endOfPlayerDelay = 1.0f;      // tweak in Inspector
    public bool useUnscaledDelayForTurnGap = false; // keep false unless you want it to ignore timeScale


    public enum BattleState { START, PLAYER_TURN, ENEMY_TURN, TURN_END, VICTORY, DEFEAT }
    public BattleState state;

    public const int AP_PER_TURN = 6;
    public int playerAP = AP_PER_TURN;

    public List<Enemy> enemies;
    public Player player;

    // UI
    public TextMeshProUGUI playerHPText;
    public TextMeshProUGUI playerAPText;
    public TextMeshProUGUI stageNameText;
    public Button passButton;
    public Image fadeToBlackImage;
    public Image characterPortrait;
    public Animator portraitAnimator;

    public AudioClip click;

    public HandManager handManager;
    public AudioManager audioManager;
    public MusicManager musicManager;

    [Header("Enemy spawning (optional)")]
    [Tooltip("Phase 1: if set, builds the enemy list from encounter data at battle start. Leave null to use scene-placed enemies.")]
    public EnemySpawner enemySpawner;

    [Header("Intro")]
    [Tooltip("Optional map→battle faceoff stinger. Auto-found in the scene if left empty.")]
    public BattleIntroStinger introStinger;

    private bool battleResolved = false;



    [Header("Action gating (turn pacing)")]
    [Tooltip("If > 0, BattleManager will wait for all registered effect animations to complete before starting the enemy turn.")]
    public bool gateEnemyTurnOnPendingEffects = true;

    [Tooltip("Failsafe: maximum time (seconds) to wait for pending effects before starting enemy turn anyway.")]
    public float maxEffectGateSeconds = 4.0f;

    private int pendingEffectCount = 0;
    private Coroutine enemyTurnGateCo;

    [Header("Card replay bookkeeping")]
    public CardData lastResolvedCard;
    public GameObject lastResolvedTarget; // Enemy GameObject or null
    private bool usedAgainThisTurn = false;
    private bool usedReiterateThisTurn = false;
    private int cardsResolvedThisTurn = 0;

    public void NotifyCardResolved(CardData resolved, GameObject target)
    {
        if (resolved == null) return;
        if (resolved.cardName == "Again!" || resolved.cardName == "Reiterate")
        {
            cardsResolvedThisTurn++;
            return;
        }
        lastResolvedCard = resolved;
        lastResolvedTarget = target;
        cardsResolvedThisTurn++;
    }

    public bool CanUseAgainThisTurn() => !usedAgainThisTurn && cardsResolvedThisTurn > 0 && lastResolvedCard != null;
    public bool CanUseReiterateThisTurn() => !usedReiterateThisTurn && cardsResolvedThisTurn == 0;
    public void MarkUsedAgain() => usedAgainThisTurn = true;
    public void MarkUsedReiterate() => usedReiterateThisTurn = true;

    public void ReiterateHandAndAP()
    {
        if (handManager != null)
        {
            handManager.DiscardHand();
            handManager.DrawHand();
        }
        playerAP = AP_PER_TURN;
        UpdateUI();
    }

    public void PlayLastResolvedCard()
    {
        if (lastResolvedCard == null) return;

        // Try to resolve against last target if it's still a living enemy.
        Enemy targetEnemy = null;
        if (lastResolvedTarget != null)
        {
            targetEnemy = lastResolvedTarget.GetComponent<Enemy>();
            if (targetEnemy != null && targetEnemy.IsDead) targetEnemy = null;
        }

        // Replay subset of effects supported by CurrentCards. (Does not spend AP; Again! already did.)
        switch (lastResolvedCard.cardName)
        {
            case "Restore":
                player.Heal(Random.Range(lastResolvedCard.minValue, lastResolvedCard.maxValue + 1));
                break;
            case "Rejuvenate":
                player.Heal(lastResolvedCard.minValue);
                break;
            case "Shield":
                player.activeEffects.Add(new StatusEffect { type = StatusType.Shield, duration = 2 });
                break;
            case "Defensive Stance":
                player.activeEffects.Add(new StatusEffect { type = StatusType.DefensiveStance, duration = 2 });
                break;
            case "Red Stroke":
                if (targetEnemy != null) targetEnemy.TakeDamage(player.ModifyOutgoingDamage(lastResolvedCard.minValue));
                break;
            case "Siphon":
                if (targetEnemy != null)
                {
                    targetEnemy.TakeDamage(player.ModifyOutgoingDamage(lastResolvedCard.minValue));
                    player.Heal(Mathf.Max(1, lastResolvedCard.minValue / 2));
                }
                break;
            case "Attack Break":
                if (targetEnemy != null) targetEnemy.ApplyAttackBreak(lastResolvedCard.minValue, 2);
                break;
            case "Poison":
                if (targetEnemy != null) targetEnemy.ApplyPoison(lastResolvedCard.minValue, 3);
                break;
            case "Sleep":
                if (targetEnemy != null) targetEnemy.ApplySleep(2);
                break;
            case "Corrode":
                if (targetEnemy != null) targetEnemy.ApplyCorrode(lastResolvedCard.minValue, 2);
                break;
            case "Ink Needle":
                if (targetEnemy != null) { targetEnemy.TakeDamage(lastResolvedCard.minValue); targetEnemy.ApplyPoison(2, 2); }
                break;
            case "Smudge":
                if (targetEnemy != null) { targetEnemy.TakeDamage(lastResolvedCard.minValue); targetEnemy.ApplyCorrode(1, 2); }
                break;
            case "Rage Mark":
                if (targetEnemy != null)
                {
                    int rmDmg = lastResolvedCard.minValue;
                    if (player != null && player.currentHP * 2 < player.maxHP) rmDmg += 3;
                    targetEnemy.TakeDamage(rmDmg);
                }
                break;
            case "Critic's Note":
                if (targetEnemy != null) targetEnemy.ApplyAttackBreak(3, 2);
                break;
            case "Streaking Medium":
                player.ApplyRegen(1, 3);
                player.ReducePoison(1);
                break;
            default:
                // If a card isn't supported here yet, it simply won't replay.
                break;
        }
    }

    // BattleManager fields (tweak in Inspector)
    [Header("Victory Cinematic")]
    public float victoryFreeze = 0.06f;
    public float victorySlowScale = 0.2f;
    public float victorySlowDuration = 0.35f;
    public float victoryRestoreDuration = 0.15f;
    public float victoryPostDelay = 0.25f;

    // Music duck targets
    [Header("Victory Music Ducking")]
    [Range(0.3f, 1f)] public float duckPitch = 0.7f;
    [Range(0f, 1f)] public float duckVolume = 0.6f;
    public float duckAttack = 0.08f;   // how fast to drop
    public float duckRelease = 0.0f;   // we won't restore; victory track will reset


    void Awake()
    {
        if (Instance == null) Instance = this;
    }

    void Start()
    {
        battleResolved = false;
        if (passButton) passButton.onClick.AddListener(PassTurn);
        UpdateUI();

        // Hold on the start menu (if present) until the player presses Start.
        if (TestFlowController.Instance != null && TestFlowController.Instance.gateBattleStart)
            return;

        BeginIntroAndBattle();
    }

    public void BeginIntroAndBattle()
    {
        // Spawn first so the live enemies exist (hidden behind the stinger's backdrop) and the
        // faceoff stinger can build its cast from their EnemyData. The battle logic itself still
        // doesn't start until the reveal beat (BeginBattle).
        SpawnEncounterIfNeeded();

        if (introStinger == null)
            introStinger = FindFirstObjectByType<BattleIntroStinger>(FindObjectsInactive.Include);

        if (introStinger != null)
        {
            // Stinger covers the screen, then parts to reveal the live fight.
            // Kick off music + the battle exactly on the reveal beat.
            introStinger.BuildCastFromEnemies(enemies);
            introStinger.Play(onReveal: BeginBattle);
        }
        else
        {
            BeginBattle();
        }
    }

    void BeginBattle()
    {
        if (musicManager) musicManager.PlayBattleMusic();
        StartCoroutine(StartBattle());
    }

    // Phase 1: if a spawner is wired, build the enemy list from encounter data.
    // Backward compatible: if the scene already placed enemies (list non-empty), keep them.
    void SpawnEncounterIfNeeded()
    {
        if (enemySpawner == null) return;
        if (enemies != null && enemies.Count > 0) return; // scene already placed enemies

        var spawned = enemySpawner.SpawnForBattle();
        if (spawned != null && spawned.Count > 0)
            enemies = spawned;
    }

    IEnumerator StartBattle()
    {
        state = BattleState.START;
        yield return new WaitForSeconds(0.25f);
        StartPlayerTurn();
    }

    void StartPlayerTurn()
    {

        if (battleResolved) return;

        usedAgainThisTurn = false;
        usedReiterateThisTurn = false;
        cardsResolvedThisTurn = 0;
        lastResolvedCard = null;
        lastResolvedTarget = null;

        // Tick the player's own statuses at the start of their turn (poison/regen).
        // Enemy statuses now tick at the start of each enemy's own turn (see EnemyTurn),
        // so enemy poison "ticks before actions" instead of a turn late.
        player.ProcessStatusEffects();

        // Damage-over-time (poison) can be lethal; resolve defeat before the turn proceeds.
        if (!battleResolved && player.GetHP() <= 0)
        {
            StartCoroutine(HandleDefeat());
            return;
        }

        state = BattleState.PLAYER_TURN;
        playerAP = AP_PER_TURN;
        if (player != null) player.AP = AP_PER_TURN; // keep the secondary counter (SpendAP sound gate) in sync

        if (passButton) passButton.interactable = true;

        UpdateCharacterPortrait(player.GetSprite());
        handManager.DrawHand();
        UpdateUI();
    }

    public void UseAP(int amount)
    {
        if (state != BattleState.PLAYER_TURN || battleResolved) return;

        player.SpendAP(amount);
        playerAP--;
        // Some cards may have killed enemies mid-turn
        if (!battleResolved && AliveEnemyCount() == 0)
        {
            StartCoroutine(HandleVictoryCinematic());
            return;                 // or yield break; if you're inside an IEnumerator
        }

        UpdateUI();

        if (playerAP <= 0)
        {
            EndPlayerTurn();
        }
    }

    void EndPlayerTurn()
    {
        if (battleResolved) return;

        // Decaying Mind grace counts down once per player turn; clears after 2 stack-free turns.
        if (player != null) player.TickDecayingMindEndOfTurn();

        if (passButton) passButton.interactable = false;

        // clear fusion selection/slots at end of turn
        if (FusionController.Instance) FusionController.Instance.OnTurnEnded();

        // Discard at end of player turn
        handManager.DiscardHand();

        // We are between turns now.
        state = BattleState.TURN_END;
        UpdateUI();

        // Start transition into the enemy turn (optionally gated on pending effects)
        if (enemyTurnGateCo != null) StopCoroutine(enemyTurnGateCo);
        enemyTurnGateCo = StartCoroutine(BeginEnemyTurnWhenReady());
    }


    IEnumerator BeginEnemyTurnWhenReady()
    {
        // Wait until all registered effect animations have finished (optional)
        if (gateEnemyTurnOnPendingEffects)
        {
            // Allow a frame so any last-second cards can register before we check.
            yield return null;

            float waited = 0f;
            while (!battleResolved && state != BattleState.VICTORY && state != BattleState.DEFEAT && pendingEffectCount > 0)
            {
                waited += Time.unscaledDeltaTime;
                if (maxEffectGateSeconds > 0f && waited >= maxEffectGateSeconds)
                {
                    Debug.LogWarning($"[BattleManager] Pending effects gate timed out (pending={pendingEffectCount}). Continuing to enemy turn.");
                    pendingEffectCount = 0; // failsafe to prevent soft-lock
                    break;
                }
                yield return null;
            }
        }

        // Wait a bit so the player's last animations/VFX can breathe
        if (endOfPlayerDelay > 0f)
        {
            if (useUnscaledDelayForTurnGap)
                yield return new WaitForSecondsRealtime(endOfPlayerDelay);
            else
                yield return new WaitForSeconds(endOfPlayerDelay);
        }

        // If victory/defeat happened during the pause, bail.
        if (battleResolved || state == BattleState.VICTORY || state == BattleState.DEFEAT) yield break;

        // If delayed effects killed the last enemy during the pause, bail (victory will trigger elsewhere).
        if (AliveEnemyCount() == 0) yield break;

        state = BattleState.ENEMY_TURN;
        StartCoroutine(EnemyTurn());
    }



    IEnumerator EnemyTurn()
    {
        if (battleResolved) yield break;

        Debug.Log("Enemy Turn Start");

        // Clean any dead before iterating
        PruneDeadEnemies();

        // was: return;
        if (!battleResolved && AliveEnemyCount() == 0)
        {
            StartCoroutine(HandleVictoryCinematic());
            yield break;                 // or yield break; if you're inside an IEnumerator
        }


        // Iterate a snapshot so deaths during the loop don't explode the foreach
        var snapshot = new List<Enemy>(enemies);
        foreach (var enemy in snapshot)
        {
            if (battleResolved) yield break;
            if (enemy == null || enemy.IsDead || enemy.GetHP() <= 0) continue;

            // Statuses tick at the START of this enemy's turn, before it acts:
            // poison "ticks before actions" and can kill it before it attacks.
            enemy.TickTurnStartStatuses();
            if (enemy.IsDead || enemy.GetHP() <= 0)
            {
                PruneDeadEnemies();
                if (!battleResolved && AliveEnemyCount() == 0)
                {
                    StartCoroutine(HandleVictoryCinematic());
                    yield break;
                }
                continue; // died to poison; it doesn't get to act
            }

            UpdateCharacterPortrait(enemy.getSprite());
            enemy.TakeTurn();

            if (player.GetHP() <= 0)
            {
                StartCoroutine(HandleDefeat());
                yield break;
            }

            yield return new WaitForSeconds(1f);
        }

        // Clean any that died during enemy actions (thorns, reflect, etc.)
        PruneDeadEnemies();

        if (!battleResolved) EndEnemyTurn();
    }

    void EndEnemyTurn()
    {
        if (battleResolved) return;
        PruneDeadEnemies();
        if (!battleResolved && AliveEnemyCount() == 0) { StartCoroutine(HandleVictory()); return; }

        if (player.GetHP() <= 0) { StartCoroutine(HandleDefeat()); return; }
        if (AliveEnemyCount() == 0)
        {
            StartCoroutine(HandleVictoryCinematic());
            return;
        }

        state = BattleState.TURN_END;
        StartCoroutine(StartBattle());
        UpdateUI();
    }

    IEnumerator HandleVictory()
    {
        if (state == BattleState.VICTORY || state == BattleState.DEFEAT) yield break;

        battleResolved = true;                 // set it here
        state = BattleState.VICTORY;

        if (passButton) passButton.interactable = false;
        handManager.DiscardHand();

        Debug.Log("Player Wins!");
        musicManager.PlayVictoryMusic();

        // Persistent run: save HP, mark the node cleared, and return to the map we came from.
        var rm = RunManager.Instance;
        if (rm != null && rm.runActive && !string.IsNullOrEmpty(rm.mapReturnScene))
        {
            if (player != null) player.SaveHPToRun();
            rm.MarkNodeCleared(rm.currentNodeId);
            // TODO: rewards (recipe / Insight choice) before returning to the map.
            yield return new WaitForSeconds(2f);
            yield return StartCoroutine(FadeToBlack());
            SceneManager.LoadScene(rm.mapReturnScene);
            yield break;
        }

        // No active run (standalone combat): keep the existing test-flow / wait behaviour.
        if (TestFlowController.Instance != null)
            yield return StartCoroutine(TestFlowController.Instance.Co_EndFight(true));
        else
            yield return new WaitForSeconds(2f);
        // TODO: next scene / rewards
    }

    private IEnumerator HandleVictoryCinematic()
    {
        // Block duplicate starts
        if (state == BattleState.VICTORY || state == BattleState.DEFEAT) yield break;

        // Optional: block pass input right away
        if (passButton) passButton.interactable = false;

        // Duck music pitch/volume across the whole cinematic window
        if (musicManager)
        {
            float totalHold = victoryFreeze + victorySlowDuration + victoryRestoreDuration + victoryPostDelay;
            // We won't restore here (victory track resets pitch/vol), so restoreAtEnd = false.
            musicManager.DuckPitchAndVolume(duckPitch, duckVolume, duckAttack, totalHold, duckRelease, restoreAtEnd: false);
        }

        // Run freeze/slow-mo (unscaled time)
        if (SlowMoController.Instance)
            yield return SlowMoController.Instance.PulseCo(victoryFreeze, victorySlowScale, victorySlowDuration, victoryRestoreDuration);
        else
            yield return new WaitForSecondsRealtime(victoryFreeze + victorySlowDuration + victoryRestoreDuration);

        // Extra pause before victory logic/music
        if (victoryPostDelay > 0f)
            yield return new WaitForSecondsRealtime(victoryPostDelay);

        // Proceed to normal victory flow (plays victory clip, resets pitch/volume inside MusicManager)
        yield return StartCoroutine(HandleVictory());
    }




    IEnumerator HandleDefeat()
    {
        if (battleResolved || state == BattleState.DEFEAT) yield break;
        battleResolved = true;

        state = BattleState.DEFEAT;
        if (passButton) passButton.interactable = false;
        handManager.DiscardHand();

        Debug.Log("Player Defeated!");
        musicManager.PlayDefeatMusic();

        // Persistent run: defeat ends the run. Go to the game-over scene if one is set; until that
        // exists, bounce back to the map (which starts a fresh run) so the loop stays testable.
        var rm = RunManager.Instance;
        if (rm != null && rm.runActive)
        {
            rm.runActive = false;   // end the run; HP/rewards are not carried over
            string dest = !string.IsNullOrEmpty(rm.gameOverScene) ? rm.gameOverScene : rm.mapReturnScene;
            if (!string.IsNullOrEmpty(dest))
            {
                yield return new WaitForSeconds(2f);
                yield return StartCoroutine(FadeToBlack());
                SceneManager.LoadScene(dest);
                yield break;
            }
            // No destination available (standalone): fall through to the presentation below.
        }

        if (TestFlowController.Instance != null)
        {
            yield return StartCoroutine(TestFlowController.Instance.Co_EndFight(false));
        }
        else
        {
            yield return new WaitForSeconds(2f);
            StartCoroutine(FadeToBlack());
        }
    }

    IEnumerator FadeToBlack()
    {
        float duration = 2f;
        float t = 0f;
        var color = fadeToBlackImage.color;
        while (t < duration)
        {
            t += Time.deltaTime;
            color.a = Mathf.Clamp01(t / duration);
            fadeToBlackImage.color = color;
            yield return null;
        }
    }

    void UpdateCharacterPortrait(Sprite newPortrait)
    {
        if (characterPortrait.sprite != newPortrait)
            StartCoroutine(SwitchCharacterPortrait(newPortrait));
    }

    IEnumerator SwitchCharacterPortrait(Sprite newPortrait)
    {
        portraitAnimator.SetTrigger("Switch");
        yield return new WaitForSeconds(0.15f);
        characterPortrait.sprite = newPortrait;
        portraitAnimator.SetTrigger("Return");
    }

    public void UpdateUI()
    {
        playerHPText.text = $"{player.GetHP()}/{player.maxHP}";
        playerAPText.text = $"{playerAP}/{AP_PER_TURN}";
        stageNameText.text = "DIRE STAGE"; // TODO
    }

    void PassTurn()
    {
        if (state != BattleState.PLAYER_TURN || battleResolved) return;
        UseAP(1);
        if (audioManager && click) audioManager.PlaySound(click);
    }

    // ---------- Effect / action gating ----------
    public void NotifyEffectStarted()
    {
        pendingEffectCount = Mathf.Max(0, pendingEffectCount + 1);
        // Debug.Log($"[BattleManager] Effect started. pending={pendingEffectCount}");
    }

    public void NotifyEffectCompleted()
    {
        pendingEffectCount = Mathf.Max(0, pendingEffectCount - 1);
        // Debug.Log($"[BattleManager] Effect completed. pending={pendingEffectCount}");
    }

    public bool HasPendingEffects()
    {
        return pendingEffectCount > 0;
    }

    // ---------- Helpers ----------
    void PruneDeadEnemies()
    {
        for (int i = enemies.Count - 1; i >= 0; i--)
        {
            var e = enemies[i];
            if (e == null || e.IsDead || e.GetHP() <= 0)
            {
                enemies.RemoveAt(i);
            }
        }
    }

    // Called by Enemy.Die()
    public void OnEnemyDied(Enemy e)
    {
        if (e != null) enemies.Remove(e);
        if (state == BattleState.VICTORY || state == BattleState.DEFEAT) return;

        if (AliveEnemyCount() == 0)
        {
            StartCoroutine(HandleVictoryCinematic());
        }

    }

    // Ms. Remember boss mechanic: add one Decaying Mind stack to the player (capped at hand size).
    // Manifests as Forgotten cards the next time the hand is drawn.
    public void ApplyDecayingMindToPlayer()
    {
        if (player == null) return;
        int cap = handManager != null ? handManager.HandSize : 6;
        player.AddDecayingMind(cap);
        Debug.Log("Ms. Remember inflicts Decaying Mind on Junior.");
    }

    public void CheckVictoryImmediate()
    {
        if (state == BattleState.VICTORY || state == BattleState.DEFEAT) return;
        PruneDeadEnemies();
        if (AliveEnemyCount() == 0)
        {
            StartCoroutine(HandleVictoryCinematic());
        }
    }

    public int AliveEnemyCount()
    {
        int alive = 0;
        for (int i = 0; i < enemies.Count; i++)
        {
            var e = enemies[i];
            if (e != null && !e.IsDead && e.GetHP() > 0) alive++;
        }
        return alive;
    }

}