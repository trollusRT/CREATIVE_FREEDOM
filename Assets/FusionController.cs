using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;

public class FusionController : MonoBehaviour
{
    public static FusionController Instance;

    [Header("Data")]
    public FusionBook fusionBook;             // assign your FusionBook asset

    [Header("UI (fusion tray)")]
    public CanvasGroup hud;                   // the fusion panel (move it to bottom-center for the tray look)
    public Image slotAImage;
    public Image slotBImage;
    public Button fuseButton;
    public Button clearButton;

    [Header("Refs")]
    public HandManager handManager;

    [Header("Input")]
    public KeyCode toggleKey = KeyCode.Space;
    public KeyCode cancelKey = KeyCode.Escape;

    [Header("HUD placeholders")]
    [SerializeField] Sprite emptySlotSpriteA;   // optional; can use one for both
    [SerializeField] Sprite emptySlotSpriteB;

    [Header("Result preview (optional)")]
    [Tooltip("Image that shows the fused result's card art once a valid recipe is selected.")]
    [SerializeField] Image resultPreviewImage;
    [Tooltip("Optional container (e.g. the preview + an arrow) toggled with the preview.")]
    [SerializeField] GameObject resultPreviewRoot;

    [Header("Tray animation")]
    [SerializeField] float trayTweenDuration = 0.2f;
    [SerializeField] Ease trayShowEase = Ease.OutBack;

    [Header("Fuse ritual")]
    [Tooltip("Canvas the full-screen flash is parented to. Auto-resolved from the HUD if left empty.")]
    [SerializeField] Canvas ritualCanvas;
    [SerializeField, Range(0f, 1f)] float flashPeakAlpha = 0.45f;
    [SerializeField] float flashDuration = 0.4f;
    [SerializeField] AudioClip fuseSfx;
    [SerializeField, Range(0f, 1f)] float fuseSfxVolume = 1f;

    // Current selection
    private readonly List<Card> selected = new();

    // Empty slots
    private Sprite defaultSlotASprite, defaultSlotBSprite;

    // Edge-trackers for one-shot flair (preview entrance) vs continuous state (fuse pulse).
    private bool wasFusable;
    private bool previewShowing;
    private Tween fusePulseTween;

    void Awake()
    {
        Instance = this;

        defaultSlotASprite = slotAImage ? slotAImage.sprite : null;
        defaultSlotBSprite = slotBImage ? slotBImage.sprite : null;

        SetHUD(false, instant: true);
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
        PopSlot(selected.Count);   // bounce the slot that just filled
        return true;
    }

    void SetHUD(bool on, bool instant = false)
    {
        if (!hud) return;

        hud.blocksRaycasts = on;
        hud.interactable = on;

        var rt = hud.transform as RectTransform;
        hud.DOKill();
        if (rt) rt.DOKill();

        if (instant)
        {
            hud.alpha = on ? 1f : 0f;
            if (rt) rt.localScale = Vector3.one;
        }
        else if (on)
        {
            hud.alpha = 0f;
            if (rt) rt.localScale = Vector3.one * 0.9f;
            hud.DOFade(1f, trayTweenDuration).SetUpdate(true);
            if (rt) rt.DOScale(1f, trayTweenDuration).SetEase(trayShowEase).SetUpdate(true);
        }
        else
        {
            hud.DOFade(0f, trayTweenDuration * 0.7f).SetUpdate(true);
        }

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

    // Public so BattleManager can call it.
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
            slotAImage.preserveAspect = true;
        }

        if (slotBImage)
        {
            slotBImage.sprite = (selected.Count > 1)
                ? selected[1].cardData.cardSprite
                : emptyB;
            slotBImage.preserveAspect = true;
        }

        // Resolve the recipe (also used to drive the result preview).
        FusionRecipe recipe = null;
        bool canFuse = false;
        if (selected.Count == 2 && fusionBook != null &&
            fusionBook.TryGetRecipe(selected[0].cardData, selected[1].cardData, out recipe) &&
            fusionBook.IsLearned(recipe))
        {
            var bm = BattleManager.Instance;
            canFuse = bm != null &&
                      bm.state == BattleManager.BattleState.PLAYER_TURN &&
                      bm.playerAP >= 1;
        }

        if (fuseButton) fuseButton.interactable = canFuse;

        ShowResultPreview(canFuse && recipe != null ? recipe.result : null);

        // Start/stop the FUSE button's breathing pulse on the fusable edge.
        if (canFuse && !wasFusable) StartFusePulse();
        else if (!canFuse && wasFusable) StopFusePulse();
        wasFusable = canFuse;
    }

    void ShowResultPreview(CardData result)
    {
        if (resultPreviewRoot) resultPreviewRoot.SetActive(result != null);
        if (!resultPreviewImage) return;

        if (result != null)
        {
            resultPreviewImage.sprite = result.cardSprite;
            resultPreviewImage.preserveAspect = true;
            resultPreviewImage.enabled = true;

            if (!previewShowing)   // scale-in the instant it first appears
            {
                previewShowing = true;
                var rt = resultPreviewImage.transform as RectTransform;
                if (rt)
                {
                    rt.DOKill();
                    rt.localScale = Vector3.one * 0.4f;
                    rt.DOScale(1f, 0.28f).SetEase(Ease.OutBack).SetUpdate(true);
                }
            }
        }
        else
        {
            resultPreviewImage.enabled = false;
            previewShowing = false;
        }
    }

    void PopSlot(int slotNumber)
    {
        var img = slotNumber == 1 ? slotAImage : (slotNumber == 2 ? slotBImage : null);
        if (!img) return;
        var rt = img.transform as RectTransform;
        if (!rt) return;
        rt.DOKill(true);
        rt.DOPunchScale(Vector3.one * 0.3f, 0.3f, 8, 0.9f).SetUpdate(true);
    }

    void StartFusePulse()
    {
        var rt = fuseButton ? fuseButton.transform as RectTransform : null;
        if (!rt) return;
        fusePulseTween?.Kill();
        rt.localScale = Vector3.one;
        fusePulseTween = rt.DOScale(1.08f, 0.6f)
            .SetEase(Ease.InOutSine)
            .SetLoops(-1, LoopType.Yoyo)
            .SetUpdate(true);
    }

    void StopFusePulse()
    {
        fusePulseTween?.Kill();
        fusePulseTween = null;
        if (fuseButton)
        {
            var rt = fuseButton.transform as RectTransform;
            if (rt) rt.localScale = Vector3.one;
        }
    }

    void OnDisable()
    {
        fusePulseTween?.Kill();
        fusePulseTween = null;
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

        // --- Gameplay resolution (unchanged) ---
        var a = selected[0];
        var b = selected[1];
        handManager.RemoveCard(a);
        handManager.RemoveCard(b);

        var resultGO = handManager.SpawnCard(recipe.result);

        bm.UseAP(1);   // may end the turn if AP hits 0

        ClearSelection();
        ExitFusionMode();

        // --- Visual payoff (additive; runs after the fuse is already resolved) ---
        PlayFuseRitual(recipe.result, resultGO);
    }

    void PlayFuseRitual(CardData result, GameObject resultGO)
    {
        if (fuseSfx && AudioManager.Instance) AudioManager.Instance.PlaySound(fuseSfx, fuseSfxVolume);

        var canvas = ritualCanvas ? ritualCanvas
                   : (hud ? hud.GetComponentInParent<Canvas>() : null);
        if (canvas) SpawnFlash(canvas, ResolveResultColor(result));

        if (resultGO) StartCoroutine(Co_PopResult(resultGO));

        if (CombatVFXManager.Instance)
        {
            CombatVFXManager.Instance.PlayOnPlayer(VfxType.BuffGlow);
            CombatVFXManager.Instance.PlayOnPlayer(VfxType.PaintSplash);
        }
        if (CameraShakeManager.Instance) CameraShakeManager.Instance.Shake();
    }

    Color ResolveResultColor(CardData result)
    {
        if (result != null && EffectDirector.Instance != null)
            return EffectDirector.Instance.ResolveTypeColor(result.cardType, Color.white);
        return Color.white;
    }

    // Brief full-screen colour flash, parented to the canvas, tinted to the result's type colour.
    void SpawnFlash(Canvas canvas, Color color)
    {
        var go = new GameObject("FuseFlash",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
        go.transform.SetParent(canvas.transform, false);

        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        rt.SetAsLastSibling();

        var img = go.GetComponent<Image>();
        img.color = new Color(color.r, color.g, color.b, 1f);
        img.raycastTarget = false;

        var cg = go.GetComponent<CanvasGroup>();
        cg.alpha = 0f; cg.blocksRaycasts = false; cg.interactable = false;

        DOTween.Sequence().SetUpdate(true)
            .Append(cg.DOFade(flashPeakAlpha, flashDuration * 0.25f))
            .Append(cg.DOFade(0f, flashDuration * 0.75f))
            .OnComplete(() => { if (go) Destroy(go); });
    }

    // Pops the freshly-spawned result card. Waits a couple frames so Card.Start()
    // captures its baseScale at 1 before we punch (avoids corrupting hover/return scaling).
    IEnumerator Co_PopResult(GameObject go)
    {
        yield return null;
        yield return null;
        if (!go) yield break;
        var rt = go.transform as RectTransform;
        if (rt) rt.DOPunchScale(Vector3.one * 0.35f, 0.4f, 7, 0.8f).SetUpdate(true);
    }

    void ShakeHUD()
    {
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
