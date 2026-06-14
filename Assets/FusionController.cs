using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class FusionController : MonoBehaviour
{
    public static FusionController Instance;

    [Header("Data")]
    public FusionBook fusionBook;             // assign your FusionBook asset

    [Header("UI (over player)")]
    public CanvasGroup hud;                   // panel above player
    public Image slotAImage;
    public Image slotBImage;
    public Button fuseButton;
    public Button clearButton;

    [Header("Refs")]
    public HandManager handManager;

    [Header("Input")]
    public KeyCode toggleKey = KeyCode.Space;
    public KeyCode cancelKey = KeyCode.Escape;

    // --- in FusionController fields ---
    [Header("HUD placeholders")]
    [SerializeField] Sprite emptySlotSpriteA;   // optional; can use one for both
    [SerializeField] Sprite emptySlotSpriteB;


    // Current selection
    private readonly List<Card> selected = new();

    // Empty slots
    private Sprite defaultSlotASprite, defaultSlotBSprite;

    void Awake()
    {
        Instance = this;

        defaultSlotASprite = slotAImage ? slotAImage.sprite : null;
        defaultSlotBSprite = slotBImage ? slotBImage.sprite : null;

        SetHUD(false);
        if (fuseButton) fuseButton.onClick.AddListener(TryFuse);
        if (clearButton) clearButton.onClick.AddListener(ClearSelection);
    }

    void Update()
    {
        // Only allow while it's the player's turn
        if (BattleManager.Instance == null ||
            BattleManager.Instance.state != BattleManager.BattleState.PLAYER_TURN)
            return;

        if (Input.GetKeyDown(toggleKey))
            ToggleFusionMode();

        if (Input.GetKeyDown(cancelKey) && IsActive)
            ExitFusionMode();
    }

    public bool IsActive => hud && hud.alpha > 0.5f;

    public void EnterFusionMode()
    {
        ClearSelection();
        SetHUD(true);
    }

    public void ExitFusionMode()
    {
        ClearSelection();
        SetHUD(false);
    }

    // Call from keyboard or a UI button if you want one later
    public void ToggleFusionMode()
    {
        if (IsActive) ExitFusionMode();
        else EnterFusionMode();
    }


    public bool TrySelectCard(Card c)
    {
        if (!c) return false;

        // Auto-enter fusion mode if HUD is hidden
        if (!IsActive) EnterFusionMode();

        if (!c.IsFusionSelectable()) return false;
        if (selected.Contains(c)) { DeselectCard(c); return true; }
        if (selected.Count >= 2) { ShakeHUD(); return false; }

        selected.Add(c);
        c.SetFusionSelected(true);
        UpdateHUD();
        return true;
    }

    void SetHUD(bool on)
    {
        if (!hud) return;
        hud.alpha = on ? 1f : 0f;
        hud.blocksRaycasts = on;
        hud.interactable = on;
        UpdateHUD();
    }


    public void DeselectCard(Card c)
    {
        if (!c) return;
        if (selected.Remove(c))
        {
            c.SetFusionSelected(false);
            UpdateHUD();
        }
    }


    // Change this from private to public so BattleManager can call it.
    public void ClearSelection()
    {
        foreach (var c in selected)
        {
            if (!c) continue;
            var cg = c.GetComponent<CanvasGroup>();
            if (cg) cg.alpha = 1f;
            c.SetFusionSelected(false);   // optional: re-enable drag
        }
        selected.Clear();
        UpdateHUD(); // this clears the slot images
    }

    // FusionController.cs
    public void OnTurnEnded(bool hideHud = true)
    {
        // Deselect any cards (safe even if they were destroyed by discard)
        ClearSelection();

        // Optionally hide the HUD between turns
        if (hideHud) SetHUD(false);
    }

    void UpdateHUD()
    {
        var emptyA = emptySlotSpriteA ? emptySlotSpriteA : defaultSlotASprite;
        var emptyB = emptySlotSpriteB ? emptySlotSpriteB : defaultSlotBSprite;

        if (slotAImage)
        {
            slotAImage.sprite = (selected.Count > 0)
                ? selected[0].cardData.cardSprite
                : emptyA;
            slotAImage.preserveAspect = true; // optional: keep aspect
        }

        if (slotBImage)
        {
            slotBImage.sprite = (selected.Count > 1)
                ? selected[1].cardData.cardSprite
                : emptyB;
            slotBImage.preserveAspect = true;
        }

        // Enable fuse if exactly two selected and recipe is valid + learned
        bool canFuse = false;
        if (selected.Count == 2 && fusionBook != null)
        {
            if (fusionBook.TryGetRecipe(selected[0].cardData, selected[1].cardData, out var r) &&
                fusionBook.IsLearned(r))
            {
                var bm = BattleManager.Instance;
                canFuse = bm != null &&
                          bm.state == BattleManager.BattleState.PLAYER_TURN &&
                          bm.playerAP >= 1;
            }
        }
        if (fuseButton) fuseButton.interactable = canFuse;
    }

    void TryFuse()
    {
        // Must have exactly 2 cards selected and a book
        if (selected.Count != 2 || fusionBook == null) { ShakeHUD(); return; }

        // Must be player's turn and have at least 1 AP
        var bm = BattleManager.Instance;
        if (bm == null || bm.state != BattleManager.BattleState.PLAYER_TURN) { ShakeHUD(); return; }
        if (bm.playerAP < 1) { ShakeHUD(); return; }

        // Must be a valid + learned recipe
        if (!fusionBook.TryGetRecipe(selected[0].cardData, selected[1].cardData, out var recipe)
            || !fusionBook.IsLearned(recipe))
        {
            ShakeHUD();
            ClearSelection();
            return;
        }

        // Remove both ingredients from hand
        var a = selected[0];
        var b = selected[1];
        handManager.RemoveCard(a);
        handManager.RemoveCard(b);

        // Spawn the fused card into the hand
        handManager.SpawnCard(recipe.result);

        // Cost 1 AP (this will also end the turn automatically if AP hits 0)
        bm.UseAP(1);

        // Clear fusion UI state
        ClearSelection();

        // Optional: hide the HUD after a successful fuse
        ExitFusionMode();
    }


    void ShakeHUD()
    {
        // simple nudge
        if (!hud) return;
        var rt = hud.transform as RectTransform;
        if (!rt) return;
        StartCoroutine(Shake(rt, 12f, 0.15f));
    }

    System.Collections.IEnumerator Shake(RectTransform rt, float dist, float dur)
    {
        Vector2 start = rt.anchoredPosition;
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = t / dur;
            float offs = Mathf.Sin(k * Mathf.PI * 4f) * dist * (1f - k);
            rt.anchoredPosition = start + new Vector2(offs, 0f);
            yield return null;
        }
        rt.anchoredPosition = start;
    }
}
