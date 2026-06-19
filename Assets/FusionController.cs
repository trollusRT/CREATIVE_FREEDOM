using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;
using DG.Tweening;

/// <summary>
/// Fusion as a dynamic centre-stage ritual instead of a static tray.
///
/// Hidden until used. Click a card and the stage fades in over a translucent, ebbing dark
/// cloud: the first ingredient sits on the left, the second on the right, each lit with its
/// type colour, with the result card vaguely glowing between them. FUSE floats above the
/// result, CANCEL below. On fuse, the ingredients spin into the centre and join, the result
/// pops up glowing its colour, then drops down into the deck.
///
/// The whole presentation is built in code (like BattleIntroStinger / DamageNumbers), so the
/// only scene wiring it needs is the FusionBook + HandManager it already had. The old static
/// tray/buttons are auto-hidden on Awake.
/// </summary>
public class FusionController : MonoBehaviour
{
    public static FusionController Instance;

    [Header("Data")]
    public FusionBook fusionBook;

    [Header("Refs")]
    public HandManager handManager;

    [Header("Legacy UI to retire (optional)")]
    [Tooltip("Old static tray + buttons. Auto-hidden on start so they stop cluttering the field.")]
    public CanvasGroup hud;
    public Button fuseButton;          // legacy — hidden on start
    public Button clearButton;         // legacy — hidden on start
    public GameObject resultPreviewRoot;

    [Header("Input")]
    public KeyCode cancelKey = KeyCode.Escape;

    [Header("Stage layout (reference 800x600)")]
    public float cardHeight = 230f;
    [Tooltip("Horizontal distance of each ingredient from centre.")]
    public float ingredientSpread = 215f;
    [Range(0.4f, 1f)] public float resultScale = 0.8f;
    [Tooltip("How faint the result card sits in the middle before fusing.")]
    [Range(0.1f, 1f)] public float resultAlpha = 0.45f;
    public float buttonOffsetY = 172f;

    [Header("Dark cloud")]
    public Vector2 cloudSize = new Vector2(880f, 540f);
    [Range(0f, 1f)] public float cloudAlpha = 0.22f;
    public Color cloudColor = Color.black;

    [Header("Glow")]
    [Range(0f, 1f)] public float glowAlpha = 0.75f;

    [Header("Buttons")]
    [Tooltip("Button art. If set, replaces the coloured fallback + label. Pick the Fuse_0 / Cancel_0 sub-sprite.")]
    public Sprite fuseSprite;
    public Sprite cancelSprite;
    public float buttonHeight = 74f;
    [Tooltip("Fallback button colours used only when no sprite is assigned.")]
    public Color fuseColor = new Color(0.20f, 0.75f, 0.35f);
    public Color cancelColor = new Color(0.78f, 0.22f, 0.22f);

    [Header("Timings (unscaled)")]
    public float showDur = 0.22f;
    public float hideDur = 0.16f;
    public float spinDur = 0.34f;
    public float popDur = 0.24f;
    public float dropDur = 0.40f;

    [Header("Audio")]
    [Tooltip("Played when a card is added to the fusion stage.")]
    public AudioClip addCardSfx;
    [Range(0f, 1f)] public float addCardSfxVolume = 1f;
    public AudioClip fuseSfx;
    [Range(0f, 1f)] public float fuseSfxVolume = 1f;

    // ---- selection state ----
    private readonly List<Card> selected = new();

    // ---- runtime stage ----
    GameObject overlayGO;
    RectTransform overlayRT;
    RectTransform stageRoot;
    CanvasGroup stageGroup;
    RectTransform cloudRoot;

    CardVisual slotA, slotB, slotR;
    Button fuseBtn;

    Sprite radialSprite;
    Tween fusePulse;
    bool idleRunning;
    readonly List<Tween> idle = new();

    class CardVisual
    {
        public RectTransform rt;     // positioned container (show/hide + scale)
        public CanvasGroup cg;
        public RectTransform bob;    // idle sway
        public Image glow;
        public Image card;
        public Vector2 basePos;
        public float baseScale = 1f;
        public bool isResult;
        public bool shown;
    }

    void Awake()
    {
        Instance = this;

        // Retire the old static presentation.
        if (hud) hud.gameObject.SetActive(false);
        if (fuseButton) fuseButton.gameObject.SetActive(false);
        if (clearButton) clearButton.gameObject.SetActive(false);
        if (resultPreviewRoot) resultPreviewRoot.SetActive(false);
    }

    void Update()
    {
        if (BattleManager.Instance == null ||
            BattleManager.Instance.state != BattleManager.BattleState.PLAYER_TURN)
            return;

        if (Input.GetKeyDown(cancelKey) && IsActive)
            ExitFusionMode();
    }

    public bool IsActive => stageGroup != null && stageGroup.alpha > 0.5f;

    // ----------------------------------------------------------------- public API

    public bool TrySelectCard(Card c)
    {
        if (!c) return false;
        if (!c.IsFusionSelectable()) return false;

        if (selected.Contains(c)) { DeselectCard(c); return true; }
        if (selected.Count >= 2) { ShakeStage(); return false; }

        selected.Add(c);
        c.SetFusionSelected(true);

        if (addCardSfx && AudioManager.Instance)
            AudioManager.Instance.PlaySound(addCardSfx, addCardSfxVolume);

        EnsureStage();
        ShowStage();
        RefreshStage();
        return true;
    }

    public void DeselectCard(Card c)
    {
        if (!c) return;
        if (!selected.Remove(c)) return;

        c.SetFusionSelected(false);
        if (selected.Count == 0) ExitFusionMode();
        else RefreshStage();
    }

    public void ClearSelection()
    {
        foreach (var c in selected)
        {
            if (!c) continue;
            c.SetFusionSelected(false);
            var cg = c.GetComponent<CanvasGroup>();
            if (cg) cg.alpha = 1f;
        }
        selected.Clear();
    }

    public void EnterFusionMode() => EnsureStage();

    public void ExitFusionMode()
    {
        ClearSelection();
        HideStage();
    }

    public void ToggleFusionMode()
    {
        if (IsActive) ExitFusionMode();
    }

    public void OnTurnEnded(bool hide = true)
    {
        ClearSelection();
        if (hide) HideStage();
    }

    // ----------------------------------------------------------------- stage build

    void EnsureStage()
    {
        if (overlayGO) return;

        overlayGO = new GameObject("FusionStage (runtime)", typeof(RectTransform));
        overlayRT = (RectTransform)overlayGO.transform;
        overlayRT.SetParent(null, false);          // root overlay -> always full-screen
        Stretch(overlayRT);

        var canvas = overlayGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 900;                 // above hand, below DamageNumbers(9999)/intro(10000)
        var scaler = overlayGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(800, 600);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        overlayGO.AddComponent<GraphicRaycaster>();

        stageRoot = NewRect("StageRoot", overlayRT, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
        stageGroup = stageRoot.gameObject.AddComponent<CanvasGroup>();
        stageGroup.alpha = 0f;
        stageGroup.blocksRaycasts = false;

        // Dark ebbing cloud (three overlapping soft blobs).
        cloudRoot = NewRect("Cloud", stageRoot, Half(), Half(), Half());
        for (int i = 0; i < 3; i++)
        {
            var blob = NewImage($"Blob_{i}", cloudRoot, new Color(cloudColor.r, cloudColor.g, cloudColor.b, cloudAlpha),
                Half(), Half(), Half());
            blob.sprite = GetRadial();
            blob.rectTransform.sizeDelta = cloudSize * Random.Range(0.85f, 1.15f);
            blob.raycastTarget = false;
        }

        // Card slots.
        slotR = MakeCardVisual("ResultSlot", new Vector2(0f, 0f), resultScale, isResult: true);
        slotA = MakeCardVisual("IngredientA", new Vector2(-ingredientSpread, 0f), 1f, isResult: false);
        slotB = MakeCardVisual("IngredientB", new Vector2(ingredientSpread, 0f), 1f, isResult: false);

        // Buttons.
        fuseBtn = MakeButton("FuseButton", "FUSE", fuseSprite, fuseColor, new Vector2(0f, buttonOffsetY), TryFuse);
        MakeButton("CancelButton", "CANCEL", cancelSprite, cancelColor, new Vector2(0f, -buttonOffsetY), ExitFusionMode);

        stageRoot.gameObject.SetActive(false);
    }

    CardVisual MakeCardVisual(string name, Vector2 basePos, float baseScale, bool isResult)
    {
        var rt = NewRect(name, stageRoot, Half(), Half(), Half());
        rt.anchoredPosition = basePos;
        rt.localScale = Vector3.one * baseScale;
        var cg = rt.gameObject.AddComponent<CanvasGroup>();
        cg.alpha = 0f;

        var bob = NewRect("Bob", rt, Half(), Half(), Half());

        var glow = NewImage("Glow", bob, new Color(1f, 1f, 1f, 0f), Half(), Half(), Half());
        glow.sprite = GetRadial();
        glow.raycastTarget = false;

        var card = NewImage("Card", bob, Color.white, Half(), Half(), Half());
        card.preserveAspect = true;
        card.raycastTarget = false;

        return new CardVisual { rt = rt, cg = cg, bob = bob, glow = glow, card = card, basePos = basePos, baseScale = baseScale, isResult = isResult };
    }

    // ----------------------------------------------------------------- refresh

    void RefreshStage()
    {
        var a = selected.Count > 0 ? selected[0].cardData : null;
        var b = selected.Count > 1 ? selected[1].cardData : null;

        SetSlot(slotA, a, ResolveColor(a));
        SetSlot(slotB, b, ResolveColor(b));

        FusionRecipe recipe = null;
        bool valid = a != null && b != null && fusionBook != null &&
                     fusionBook.TryGetRecipe(a, b, out recipe) && fusionBook.IsLearned(recipe);

        var result = valid ? recipe.result : null;
        SetSlot(slotR, result, ResolveColor(result));

        var bm = BattleManager.Instance;
        bool canFuse = valid && bm != null &&
                       bm.state == BattleManager.BattleState.PLAYER_TURN && bm.playerAP >= 1;

        if (fuseBtn) fuseBtn.interactable = canFuse;
        if (canFuse) StartFusePulse(); else StopFusePulse();
    }

    void SetSlot(CardVisual cv, CardData data, Color color)
    {
        bool show = data != null && data.cardSprite != null;

        if (show)
        {
            SetCardSprite(cv, data.cardSprite);
            cv.glow.color = new Color(color.r, color.g, color.b, glowAlpha);
            var cc = cv.card.color; cc.a = cv.isResult ? resultAlpha : 1f; cv.card.color = cc;

            if (!cv.shown)
            {
                cv.shown = true;
                AnimateSlotIn(cv);
                StartBob(cv);
                StartGlowPulse(cv);
            }
        }
        else if (cv.shown)
        {
            cv.shown = false;
            cv.cg.DOKill();
            cv.cg.DOFade(0f, hideDur).SetUpdate(true);
        }
    }

    void AnimateSlotIn(CardVisual cv)
    {
        cv.rt.DOKill();
        cv.cg.DOKill();
        cv.cg.alpha = 0f;
        cv.rt.localScale = Vector3.one * (cv.baseScale * 0.7f);
        cv.cg.DOFade(1f, showDur).SetUpdate(true);
        cv.rt.DOScale(cv.baseScale, showDur * 1.3f).SetEase(Ease.OutBack).SetUpdate(true);
    }

    // ----------------------------------------------------------------- show / hide

    void ShowStage()
    {
        EnsureStage();
        stageRoot.gameObject.SetActive(true);

        stageGroup.DOKill();
        stageGroup.blocksRaycasts = true;
        if (stageGroup.alpha < 0.99f)
        {
            stageGroup.alpha = 0f;
            stageRoot.localScale = Vector3.one * 0.96f;
            stageGroup.DOFade(1f, showDur).SetUpdate(true);
            stageRoot.DOScale(1f, showDur).SetEase(Ease.OutBack).SetUpdate(true);
        }

        StartCloudEbb();
    }

    void HideStage()
    {
        if (overlayGO == null) return;

        KillIdle();
        StopFusePulse();

        if (stageGroup)
        {
            stageGroup.DOKill();
            stageGroup.blocksRaycasts = false;
            stageGroup.DOFade(0f, hideDur).SetUpdate(true)
                .OnComplete(() => { if (stageRoot) stageRoot.gameObject.SetActive(false); });
        }

        ResetSlot(slotA);
        ResetSlot(slotB);
        ResetSlot(slotR);
    }

    void ResetSlot(CardVisual cv)
    {
        if (cv == null) return;
        cv.shown = false;
        cv.rt.DOKill();
        cv.cg.DOKill();
        cv.bob.DOKill();
        cv.glow.DOKill();
        cv.cg.alpha = 0f;
        cv.bob.anchoredPosition = Vector2.zero;
        cv.bob.localRotation = Quaternion.identity;
    }

    // ----------------------------------------------------------------- idle motion

    void StartCloudEbb()
    {
        if (idleRunning || cloudRoot == null) return;
        idleRunning = true;

        for (int i = 0; i < cloudRoot.childCount; i++)
        {
            var blob = cloudRoot.GetChild(i) as RectTransform;
            if (!blob) continue;
            float dur = Random.Range(2.2f, 3.4f);
            float drift = Random.Range(18f, 40f);
            blob.localRotation = Quaternion.identity;
            blob.localScale = Vector3.one * Random.Range(0.85f, 0.95f);

            Idle(blob.DOScale(Random.Range(1.08f, 1.22f), dur)
                .SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo).SetUpdate(true));
            Idle(blob.DOAnchorPosX(Random.Range(-drift, drift), dur * 1.1f)
                .SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo).SetUpdate(true));
            Idle(blob.DOAnchorPosY(Random.Range(-drift, drift) * 0.6f, dur * 0.9f)
                .SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo).SetUpdate(true));
            Idle(blob.DORotate(new Vector3(0, 0, Random.Range(-8f, 8f)), dur * 1.3f)
                .SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo).SetUpdate(true));
        }
    }

    void StartBob(CardVisual cv)
    {
        cv.bob.DOKill();
        cv.bob.anchoredPosition = Vector2.zero;
        cv.bob.localRotation = Quaternion.identity;
        float phase = Random.Range(0f, 0.5f);

        Idle(cv.bob.DOAnchorPosY(7f, 1.1f).SetDelay(phase)
            .SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo).SetUpdate(true));
        Idle(cv.bob.DOLocalRotate(new Vector3(0, 0, cv.isResult ? 0f : 3f), 1.4f).SetDelay(phase)
            .SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo).SetUpdate(true));
    }

    void StartGlowPulse(CardVisual cv)
    {
        cv.glow.DOKill();
        var c = cv.glow.color; c.a = glowAlpha; cv.glow.color = c;
        Idle(cv.glow.DOFade(glowAlpha * 0.5f, 0.85f)
            .SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo).SetUpdate(true));
    }

    Tween Idle(Tween t) { if (t != null) idle.Add(t); return t; }

    void KillIdle()
    {
        for (int i = 0; i < idle.Count; i++) idle[i]?.Kill();
        idle.Clear();
        idleRunning = false;
    }

    void StartFusePulse()
    {
        if (fusePulse != null && fusePulse.IsActive()) return;
        if (!fuseBtn) return;
        var rt = fuseBtn.transform as RectTransform;
        rt.localScale = Vector3.one;
        fusePulse = rt.DOScale(1.1f, 0.55f).SetEase(Ease.InOutSine)
            .SetLoops(-1, LoopType.Yoyo).SetUpdate(true);
    }

    void StopFusePulse()
    {
        fusePulse?.Kill();
        fusePulse = null;
        if (fuseBtn) (fuseBtn.transform as RectTransform).localScale = Vector3.one;
    }

    // ----------------------------------------------------------------- fuse

    void TryFuse()
    {
        if (selected.Count != 2 || fusionBook == null) { ShakeStage(); return; }

        var bm = BattleManager.Instance;
        if (bm == null || bm.state != BattleManager.BattleState.PLAYER_TURN || bm.playerAP < 1) { ShakeStage(); return; }

        if (!fusionBook.TryGetRecipe(selected[0].cardData, selected[1].cardData, out var recipe) ||
            !fusionBook.IsLearned(recipe))
        {
            ShakeStage();
            ExitFusionMode();
            return;
        }

        // Capture visuals + data before we tear the stage down (RemoveCard destroys the ingredient cards).
        var a = selected[0];
        var b = selected[1];
        CardData dataA = a.cardData, dataB = b.cardData, dataR = recipe.result;
        Sprite spriteA = dataA.cardSprite, spriteB = dataB.cardSprite, spriteR = dataR.cardSprite;
        Color colorA = ResolveColor(dataA), colorB = ResolveColor(dataB), colorR = ResolveColor(dataR);

        // Gameplay resolution (unchanged from before).
        handManager.RemoveCard(a);
        handManager.RemoveCard(b);
        handManager.SpawnCard(recipe.result);
        bm.UseAP(1);                 // may end the turn

        // Fusion Insights (hook wired now; effects land in pass 2).
        InsightHost.Instance?.OnFuse(dataA, dataB, dataR);

        ExitFusionMode();            // clears selection + fades the stage

        // Independent payoff animation so it survives the stage teardown / turn end.
        PlayFuseAnimation(spriteA, colorA, spriteB, colorB, spriteR, colorR);
    }

    void PlayFuseAnimation(Sprite a, Color ca, Sprite b, Color cb, Sprite res, Color cr)
    {
        EnsureStage();
        if (fuseSfx && AudioManager.Instance) AudioManager.Instance.PlaySound(fuseSfx, fuseSfxVolume);

        var root = NewRect("FuseAnim", overlayRT, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));

        var left = MakeLooseCard(root, a, ca, new Vector2(-ingredientSpread, 0f), 1f);
        var right = MakeLooseCard(root, b, cb, new Vector2(ingredientSpread, 0f), 1f);
        var result = MakeLooseCard(root, res, cr, Vector2.zero, resultScale);
        result.cg.alpha = 0f;
        result.rt.localScale = Vector3.zero;

        var seq = DOTween.Sequence().SetUpdate(true);

        // 1 — ingredients spin into the centre and join.
        seq.Append(left.rt.DOAnchorPos(Vector2.zero, spinDur).SetEase(Ease.InBack));
        seq.Join(left.rt.DORotate(new Vector3(0, 0, 360f), spinDur, RotateMode.FastBeyond360));
        seq.Join(left.rt.DOScale(0.35f, spinDur));
        seq.Join(left.cg.DOFade(0f, spinDur).SetEase(Ease.InQuad));
        seq.Join(right.rt.DOAnchorPos(Vector2.zero, spinDur).SetEase(Ease.InBack));
        seq.Join(right.rt.DORotate(new Vector3(0, 0, -360f), spinDur, RotateMode.FastBeyond360));
        seq.Join(right.rt.DOScale(0.35f, spinDur));
        seq.Join(right.cg.DOFade(0f, spinDur).SetEase(Ease.InQuad));

        // join flash + camera punch
        seq.AppendCallback(() =>
        {
            SpawnFlash(cr);
            if (CameraShakeManager.Instance) CameraShakeManager.Instance.Shake();
        });

        // 2 — result pops up, glowing its colour.
        seq.Append(result.rt.DOScale(resultScale * 1.18f, popDur).SetEase(Ease.OutBack));
        seq.Join(result.cg.DOFade(1f, popDur));
        seq.Join(result.rt.DOAnchorPosY(48f, popDur).SetEase(Ease.OutBack));
        seq.Join(result.glow.DOFade(0.95f, popDur));
        seq.Append(result.rt.DOScale(resultScale, popDur * 0.6f).SetEase(Ease.OutSine));
        seq.AppendInterval(0.10f);

        // 3 — drops down into the deck.
        float dropY = -360f;
        seq.Append(result.rt.DOAnchorPos(new Vector2(0f, dropY), dropDur).SetEase(Ease.InCubic));
        seq.Join(result.rt.DOScale(resultScale * 0.5f, dropDur));
        seq.Join(result.cg.DOFade(0f, dropDur).SetDelay(dropDur * 0.35f));
        seq.Join(result.glow.DOFade(0f, dropDur));

        seq.OnComplete(() => { if (root) Destroy(root.gameObject); });
    }

    // A standalone card (glow + face) for the fuse payoff, not tracked by the slot system.
    CardVisual MakeLooseCard(RectTransform parent, Sprite sprite, Color color, Vector2 pos, float scale)
    {
        var rt = NewRect("Loose", parent, Half(), Half(), Half());
        rt.anchoredPosition = pos;
        rt.localScale = Vector3.one * scale;
        var cg = rt.gameObject.AddComponent<CanvasGroup>();

        var glow = NewImage("Glow", rt, new Color(color.r, color.g, color.b, glowAlpha), Half(), Half(), Half());
        glow.sprite = GetRadial();
        glow.raycastTarget = false;

        var card = NewImage("Card", rt, Color.white, Half(), Half(), Half());
        card.preserveAspect = true;
        card.raycastTarget = false;

        var cv = new CardVisual { rt = rt, cg = cg, bob = rt, glow = glow, card = card, basePos = pos, baseScale = scale };
        SetCardSprite(cv, sprite);
        return cv;
    }

    void SpawnFlash(Color color)
    {
        var img = NewImage("FuseFlash", overlayRT, new Color(color.r, color.g, color.b, 1f),
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
        img.raycastTarget = false;
        img.transform.SetAsLastSibling();
        var cg = img.gameObject.AddComponent<CanvasGroup>();
        cg.alpha = 0f;
        DOTween.Sequence().SetUpdate(true)
            .Append(cg.DOFade(0.5f, 0.10f))
            .Append(cg.DOFade(0f, 0.30f))
            .OnComplete(() => { if (img) Destroy(img.gameObject); });
    }

    void ShakeStage()
    {
        if (stageRoot == null) return;
        stageRoot.DOComplete();
        stageRoot.DOShakeAnchorPos(0.3f, new Vector2(18f, 0f), 14, 0f).SetUpdate(true);
    }

    // ----------------------------------------------------------------- helpers

    Color ResolveColor(CardData d)
    {
        if (d == null) return Color.white;
        if (EffectDirector.Instance != null) return EffectDirector.Instance.ResolveTypeColor(d.cardType, Color.white);
        return Color.white;
    }

    void SetCardSprite(CardVisual cv, Sprite sprite)
    {
        cv.card.sprite = sprite;
        float aspect = (sprite != null && sprite.rect.height > 0f) ? sprite.rect.width / sprite.rect.height : 0.68f;
        float h = cardHeight, w = h * aspect;
        cv.card.rectTransform.sizeDelta = new Vector2(w, h);
        float g = Mathf.Max(w, h) * 1.7f;
        cv.glow.rectTransform.sizeDelta = new Vector2(g, g);
    }

    Button MakeButton(string name, string label, Sprite sprite, Color fallback, Vector2 pos, UnityEngine.Events.UnityAction onClick)
    {
        var rt = NewRect(name, stageRoot, Half(), Half(), Half());
        rt.anchoredPosition = pos;

        var img = rt.gameObject.AddComponent<Image>();
        img.raycastTarget = true;

        if (sprite != null)
        {
            // Use the supplied art (which already carries its own label/styling).
            img.sprite = sprite;
            img.color = Color.white;
            img.preserveAspect = true;
            float aspect = sprite.rect.height > 0f ? sprite.rect.width / sprite.rect.height : 2.1f;
            rt.sizeDelta = new Vector2(buttonHeight * aspect, buttonHeight);
        }
        else
        {
            // Fallback: plain coloured button with a text label.
            img.color = fallback;
            rt.sizeDelta = new Vector2(168f, buttonHeight);
            var t = NewText(rt, label, 26f, Color.white, TextAlignmentOptions.Center);
            t.fontStyle = FontStyles.Bold;
        }

        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);
        return btn;
    }

    Sprite GetRadial()
    {
        if (radialSprite) return radialSprite;
        const int s = 128;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        var px = new Color[s * s];
        float r = s * 0.5f;
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float d = Mathf.Sqrt((x - r) * (x - r) + (y - r) * (y - r)) / r;
                float a = Mathf.Clamp01(1f - d);
                a = Mathf.Pow(a, 1.7f);
                px[y * s + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        radialSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
        return radialSprite;
    }

    RectTransform NewRect(string name, Transform parent, Vector2 aMin, Vector2 aMax, Vector2 pivot)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = aMin;
        rt.anchorMax = aMax;
        rt.pivot = pivot;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.anchoredPosition = Vector2.zero;
        rt.localScale = Vector3.one;
        return rt;
    }

    Image NewImage(string name, Transform parent, Color c, Vector2 aMin, Vector2 aMax, Vector2 pivot)
    {
        var rt = NewRect(name, parent, aMin, aMax, pivot);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = c;
        img.raycastTarget = false;
        return img;
    }

    TextMeshProUGUI NewText(Transform parent, string text, float size, Color color, TextAlignmentOptions align)
    {
        var rt = NewRect("Label", parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.raycastTarget = false;
        return t;
    }

    static Vector2 Half() => new Vector2(0.5f, 0.5f);

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    void OnDisable()
    {
        KillIdle();
        StopFusePulse();
    }
}
