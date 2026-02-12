using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

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

    private bool battleResolved = false;

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
    public float duckRelease = 0.0f;   // we won’t restore; victory track will reset


    void Awake()
    {
        if (Instance == null) Instance = this;
    }

    void Start()
    {
        battleResolved = false;
        if (passButton) passButton.onClick.AddListener(PassTurn);
        musicManager.PlayBattleMusic();
        UpdateUI();
        StartCoroutine(StartBattle());
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

        // Tick statuses at the start of the player's turn (use a snapshot to avoid collection changes mid-iteration)
        player.ProcessStatusEffects();

        var enemySnapshot = new List<Enemy>(enemies);
        for (int i = 0; i < enemySnapshot.Count; i++)
        {
            var e = enemySnapshot[i];
            if (e != null && !e.IsDead) e.ProcessStatusEffects();
        }

        // Clean dead (poison tick etc.)
        PruneDeadEnemies();
        if (!battleResolved && AliveEnemyCount() == 0)
        {
            StartCoroutine(HandleVictoryCinematic());
            return;                 // or yield break; if you're inside an IEnumerator
        }
        if (!battleResolved && enemies.Count == 0)
        {
            // Poison might have wiped the board
            StartCoroutine(HandleVictoryCinematic());
            return;
        }

        state = BattleState.PLAYER_TURN;
        playerAP = AP_PER_TURN;

        if (passButton) passButton.interactable = true;

        UpdateCharacterPortrait(player.GetSprite());
        handManager.DrawHand();
        UpdateUI();
    }

    public void UseAP(int amount)
    {
        if (state != BattleState.PLAYER_TURN || battleResolved) return;

        player.SpendAP(amount);
        playerAP -= amount;

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

        if (passButton) passButton.interactable = false;

        // clear fusion selection/slots at end of turn
        if (FusionController.Instance) FusionController.Instance.OnTurnEnded();

        // Discard at end of player turn
        handManager.DiscardHand();

        // We are between turns now.
        state = BattleState.TURN_END;
        UpdateUI();

        // Start delayed transition into the enemy turn
        StartCoroutine(BeginEnemyTurnAfterDelay());
    }

    IEnumerator BeginEnemyTurnAfterDelay()
    {
        // Wait a bit so player's last animations/VFX can breathe
        if (endOfPlayerDelay > 0f)
        {
            if (useUnscaledDelayForTurnGap)
                yield return new WaitForSecondsRealtime(endOfPlayerDelay);
            else
                yield return new WaitForSeconds(endOfPlayerDelay);
        }

        // If victory/defeat happened during the pause, bail.
        if (battleResolved || state == BattleState.VICTORY || state == BattleState.DEFEAT) yield break;

        // If delayed effects killed the last enemy during the pause, victory will kick in elsewhere
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

        // this one was already correct
        if (!battleResolved && enemies.Count == 0)
        {
            StartCoroutine(HandleVictoryCinematic());
            yield break;
        }

        // Iterate a snapshot so deaths during the loop don't explode the foreach
        var snapshot = new List<Enemy>(enemies);
        foreach (var enemy in snapshot)
        {
            if (battleResolved) yield break;
            if (enemy == null || enemy.IsDead || enemy.GetHP() <= 0) continue;

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
            // We won’t restore here (victory track resets pitch/vol), so restoreAtEnd = false.
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
        yield return new WaitForSeconds(2f);
        StartCoroutine(FadeToBlack());
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
        audioManager.PlaySound(click);
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
