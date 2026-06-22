using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using DG.Tweening;

/// <summary>
/// Foresee (The Script): right-click Junior to "stop and think" — the camera pushes in, she loops a
/// Thinking animation, and next turn's incoming cards float around her face-down. Press FORESEE
/// (costs Focus) and they emerge from the dark; click any card to reroll it (costs Focus each). BACK
/// zooms back out. Whatever you leave is locked in as next turn's hand (HandManager.foreseenFresh,
/// consumed by DrawHand).
///
/// Like FusionController, the whole stage is built in code, so the only scene setup is a looping
/// "Thinking" state on Junior's Animator (optional) + tuning. Everything is null-guarded: with nothing
/// assigned it still runs (just without the camera move / animation).
/// </summary>
public class ForeseeController : MonoBehaviour
{
    public static ForeseeController Instance;

    [Header("Refs (auto-found if empty)")]
    public HandManager handManager;
    [Tooltip("Junior's transform — the right-click target and camera framing point. Defaults to BattleManager.player.")]
    public Transform junior;
    [Tooltip("Camera to push in on Junior. Defaults to Camera.main. Set zoomOrthoSize to 0 to skip the camera move.")]
    public Camera zoomCamera;

    [Header("Costs (Focus)")]
    public int foreseeFocusCost = 1;
    [Tooltip("Focus per reroll. 0 = free rerolls.")]
    public int rerollFocusCost = 1;

    [Header("Junior animation")]
    [Tooltip("Animator with a looping Thinking state. Defaults to BattleManager.player's animator.")]
    public Animator juniorAnimator;
    [Tooltip("Bool parameter held true while considering (drive a looping Thinking state from it). Blank = skip.")]
    public string thinkingBool = "Thinking";

    [Header("Right-click target")]
    public LayerMask juniorMask = ~0;

    [Header("Camera push-in (orthographic)")]
    [Tooltip("Orthographic size to zoom to. 0 = leave the camera alone.")]
    public float zoomOrthoSize = 2.6f;
    public Vector2 zoomOffset = new Vector2(0f, 0.5f);
    public float zoomDuration = 0.45f;

    [Header("Stage layout (reference 800x600)")]
    public float cardHeight = 150f;
    public float arcRadius = 300f;
    public float arcSpreadDeg = 150f;
    public Vector2 arcCenter = new Vector2(0f, -120f);
    [Tooltip("FORESEE / BACK button positions, flanking Junior (reference 800x600, centre origin). Tune to taste.")]
    public Vector2 foreseeButtonPos = new Vector2(-255f, -150f);
    public Vector2 backButtonPos = new Vector2(255f, -150f);

    [Header("Look")]
    [Tooltip("Face-down 'blocked out' card art. If null, a plain dark card is shown.")]
    public Sprite cardBackSprite;
    [Tooltip("Optional eerie art border drawn full-screen ON TOP of the procedural vignette. Author at your display aspect (e.g. 1920x1080 for 16:9) with a transparent centre.")]
    public Sprite foreseeBorderSprite;
    [Range(0f, 1f)] public float vignetteAlpha = 0.8f;
    [Range(0f, 1f)] public float glowAlpha = 0.7f;

    [Header("Buttons")]
    public Sprite foreseeSprite;
    public Sprite backSprite;
    public float buttonHeight = 70f;
    public Color foreseeColor = new Color(0.86f, 0.80f, 0.45f);
    public Color backColor = new Color(0.55f, 0.55f, 0.62f);

    [Header("Timings (unscaled)")]
    public float showDur = 0.25f;
    public float hideDur = 0.18f;
    public float revealStagger = 0.07f;
    public float revealDur = 0.30f;

    [Header("Audio")]
    public AudioClip enterSfx;
    public AudioClip foreseeSfx;
    public AudioClip rerollSfx;
    [Range(0f, 1f)] public float sfxVolume = 1f;

    [Header("Music (distort while focusing)")]
    [Tooltip("Lower the music pitch while Foreseeing — this also slows the tempo (pitch & speed are linked on an AudioSource), for an eerie 'time slows' feel. Restored on BACK.")]
    public bool distortMusic = true;
    [Range(0.3f, 1f)] public float eeriePitch = 0.7f;
    [Range(0f, 1f)] public float eerieVolume = 0.7f;
    public float musicRampDuration = 0.5f;

    [Header("Events (extra presentation, optional)")]
    public UnityEvent onEnter;
    public UnityEvent onExit;

    [Header("Hide while Foreseeing")]
    [Tooltip("UI roots to hide during Foresee — card hand, HUD (HP/AP/Focus), stage title, End Turn button, enemy HP, etc. Re-shown on BACK. Only objects that were active get re-enabled.")]
    public GameObject[] hideDuringForesee;

    enum Phase { Off, Considering, Revealed }
    Phase phase = Phase.Off;

    // ---- runtime stage ----
    GameObject overlayGO;
    RectTransform overlayRT;
    RectTransform stageRoot;
    CanvasGroup stageGroup;
    Button foreseeBtn;
    readonly List<Slot> slots = new();
    readonly List<Tween> idle = new();
    readonly List<GameObject> hiddenNow = new();
    Sprite radialSprite, vignetteSprite;

    // ---- camera restore ----
    float camOrigOrtho;
    Vector3 camOrigPos;
    bool camCaptured;
    Tween zoomTween;

    class Slot
    {
        public RectTransform rt;
        public RectTransform bob;
        public CanvasGroup cg;
        public Image card;
        public Image glow;
        public Button button;
        public int index;
        public bool revealed;
    }

    void Awake() { Instance = this; }

    void Update()
    {
        var bm = BattleManager.Instance;
        if (bm == null) return;

        if (phase != Phase.Off)
        {
            if (Input.GetKeyDown(KeyCode.Escape)) ExitForesee();
            return;
        }

        if (bm.state != BattleManager.BattleState.PLAYER_TURN) return;

        // Right-click Junior to enter. Ignore clicks over UI (e.g. a card being Remembered).
        if (Input.GetMouseButtonDown(1) && !IsPointerOverUI() && RightClickHitsJunior())
            EnterForesee();
    }

    // ----------------------------------------------------------------- flow

    public void EnterForesee()
    {
        if (phase != Phase.Off) return;
        var hand = Hand();
        if (hand == null) return;

        phase = Phase.Considering;

        PlaySfx(enterSfx);
        SetThinking(true);
        ZoomIn();
        HideOtherUI();
        if (distortMusic && MusicManager.Instance != null)
            MusicManager.Instance.RampMusic(eeriePitch, eerieVolume, musicRampDuration);

        BuildStage(Mathf.Max(0, hand.ForeseeSlotCount()));
        ShowStage();
        onEnter?.Invoke();
    }

    void OnForeseePressed()
    {
        if (phase != Phase.Considering) return;
        var bm = BattleManager.Instance;
        var hand = Hand();
        if (bm == null || hand == null) return;

        if (foreseeFocusCost > 0 && !bm.SpendFocus(foreseeFocusCost)) { ShakeStage(); return; }

        PlaySfx(foreseeSfx);
        hand.ComputeForeseenFresh();
        var list = hand.ForeseenFresh;

        phase = Phase.Revealed;
        if (foreseeBtn) foreseeBtn.gameObject.SetActive(false); // spent — only reroll + Back remain

        for (int i = 0; i < slots.Count; i++)
        {
            var data = (list != null && i < list.Count) ? list[i] : null;
            RevealSlot(slots[i], data, i * revealStagger);
        }
    }

    void OnSlotClicked(int index)
    {
        if (phase != Phase.Revealed) return;
        var bm = BattleManager.Instance;
        var hand = Hand();
        if (bm == null || hand == null) return;
        if (index < 0 || index >= slots.Count || !slots[index].revealed) return;

        if (rerollFocusCost > 0 && !bm.SpendFocus(rerollFocusCost)) { ShakeSlot(slots[index]); return; }

        PlaySfx(rerollSfx);
        var data = hand.RerollForeseen(index);
        PopSlot(slots[index], data);
    }

    public void ExitForesee()
    {
        if (phase == Phase.Off) return;
        phase = Phase.Off;

        SetThinking(false);
        ZoomOut();
        HideStage();
        RestoreOtherUI();
        if (distortMusic && MusicManager.Instance != null)
            MusicManager.Instance.RestoreMusicDefaults(musicRampDuration);
        onExit?.Invoke();
    }

    // ----------------------------------------------------------------- camera + animation

    void ZoomIn()
    {
        var cam = zoomCamera ? zoomCamera : Camera.main;
        var j = Junior();
        if (cam == null || j == null || zoomOrthoSize <= 0f || !cam.orthographic) return;

        if (!camCaptured)
        {
            camOrigOrtho = cam.orthographicSize;
            camOrigPos = cam.transform.position;
            camCaptured = true;
        }

        var t = cam.transform;
        t.DOKill();
        zoomTween?.Kill();
        Vector3 target = new Vector3(j.position.x + zoomOffset.x, j.position.y + zoomOffset.y, camOrigPos.z);
        t.DOMove(target, zoomDuration).SetEase(Ease.OutCubic).SetUpdate(true);
        zoomTween = DOTween.To(() => cam.orthographicSize, v => cam.orthographicSize = v, zoomOrthoSize, zoomDuration)
            .SetEase(Ease.OutCubic).SetUpdate(true);
    }

    void ZoomOut()
    {
        var cam = zoomCamera ? zoomCamera : Camera.main;
        if (cam == null || !camCaptured) return;

        var t = cam.transform;
        t.DOKill();
        zoomTween?.Kill();
        t.DOMove(camOrigPos, zoomDuration).SetEase(Ease.InOutCubic).SetUpdate(true);
        zoomTween = DOTween.To(() => cam.orthographicSize, v => cam.orthographicSize = v, camOrigOrtho, zoomDuration)
            .SetEase(Ease.InOutCubic).SetUpdate(true)
            .OnComplete(() => camCaptured = false);
    }

    void SetThinking(bool on)
    {
        var anim = juniorAnimator;
        if (anim == null)
        {
            var p = BattleManager.Instance != null ? BattleManager.Instance.player : null;
            anim = p != null ? p.animator : null;
        }
        if (anim != null && !string.IsNullOrEmpty(thinkingBool)) anim.SetBool(thinkingBool, on);
    }

    // ----------------------------------------------------------------- stage build

    void BuildStage(int count)
    {
        EnsureOverlay();

        // Clear any previous slots (count can vary with Remembered cards).
        foreach (var s in slots) if (s != null && s.rt) Destroy(s.rt.gameObject);
        slots.Clear();

        if (foreseeBtn) foreseeBtn.gameObject.SetActive(true);

        for (int i = 0; i < count; i++)
        {
            var s = MakeSlot(i, ArcPos(i, count));
            slots.Add(s);
        }
    }

    Vector2 ArcPos(int i, int n)
    {
        float t = (n <= 1) ? 0.5f : (float)i / (n - 1);
        float ang = Mathf.Lerp(-arcSpreadDeg * 0.5f, arcSpreadDeg * 0.5f, t) * Mathf.Deg2Rad;
        return arcCenter + new Vector2(Mathf.Sin(ang) * arcRadius, Mathf.Cos(ang) * arcRadius);
    }

    void EnsureOverlay()
    {
        if (overlayGO) return;

        overlayGO = new GameObject("ForeseeStage (runtime)", typeof(RectTransform));
        overlayRT = (RectTransform)overlayGO.transform;
        overlayRT.SetParent(null, false);
        Stretch(overlayRT);

        var canvas = overlayGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 950;                  // above fusion (900), below DamageNumbers/intro
        var scaler = overlayGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(800, 600);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        overlayGO.AddComponent<GraphicRaycaster>();

        stageRoot = NewRect("StageRoot", overlayRT, Vector2.zero, Vector2.one, Half());
        stageGroup = stageRoot.gameObject.AddComponent<CanvasGroup>();
        stageGroup.alpha = 0f;
        stageGroup.blocksRaycasts = false;

        // Vignette: dark edges, clear centre so the pushed-in Junior shows through.
        var vig = NewImage("Vignette", stageRoot, new Color(0f, 0f, 0f, vignetteAlpha), Vector2.zero, Vector2.one, Half());
        Stretch(vig.rectTransform);
        vig.sprite = GetVignette();
        vig.raycastTarget = true;                   // swallow clicks behind the stage

        // Optional custom art border, full-screen over the procedural vignette.
        if (foreseeBorderSprite != null)
        {
            var border = NewImage("ArtBorder", stageRoot, Color.white, Vector2.zero, Vector2.one, Half());
            Stretch(border.rectTransform);
            border.sprite = foreseeBorderSprite;
            border.preserveAspect = false;
            border.raycastTarget = false;
        }

        // Buttons.
        foreseeBtn = MakeButton("ForeseeButton", "FORESEE", foreseeSprite, foreseeColor,
            foreseeButtonPos, OnForeseePressed);
        MakeButton("BackButton", "BACK", backSprite, backColor,
            backButtonPos, ExitForesee);

        stageRoot.gameObject.SetActive(false);
    }

    Slot MakeSlot(int index, Vector2 pos)
    {
        var rt = NewRect($"Card_{index}", stageRoot, Half(), Half(), Half());
        rt.anchoredPosition = pos;
        var cg = rt.gameObject.AddComponent<CanvasGroup>();
        cg.alpha = 0f;

        var bob = NewRect("Bob", rt, Half(), Half(), Half());

        var glow = NewImage("Glow", bob, new Color(1f, 1f, 1f, 0f), Half(), Half(), Half());
        glow.sprite = GetRadial();
        glow.raycastTarget = false;

        var card = NewImage("Card", bob, Color.white, Half(), Half(), Half());
        card.preserveAspect = true;
        card.raycastTarget = true;                  // clickable for reroll

        var s = new Slot { rt = rt, bob = bob, cg = cg, card = card, glow = glow, index = index, revealed = false };
        SetFaceDown(s);

        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = card;
        int captured = index;
        btn.onClick.AddListener(() => OnSlotClicked(captured));
        btn.interactable = false;                   // enabled on reveal
        s.button = btn;

        return s;
    }

    void SetFaceDown(Slot s)
    {
        s.revealed = false;
        if (cardBackSprite != null)
        {
            s.card.sprite = cardBackSprite;
            s.card.color = Color.white;
            SizeCard(s, cardBackSprite);
        }
        else
        {
            s.card.sprite = null;
            s.card.color = new Color(0.12f, 0.12f, 0.16f, 1f);
            s.card.rectTransform.sizeDelta = new Vector2(cardHeight * 0.68f, cardHeight);
        }
        s.glow.color = new Color(1f, 1f, 1f, 0f);
    }

    // ----------------------------------------------------------------- reveal / reroll visuals

    void RevealSlot(Slot s, CardData data, float delay)
    {
        s.revealed = true;
        if (s.button) s.button.interactable = true;

        Color tint = ResolveColor(data);
        Sprite sprite = data != null ? data.cardSprite : null;

        var seq = DOTween.Sequence().SetUpdate(true).SetDelay(delay);
        // shrink the face-down card away...
        seq.Append(s.bob.DOScale(0.6f, revealDur * 0.4f).SetEase(Ease.InQuad));
        seq.AppendCallback(() =>
        {
            if (sprite != null) { s.card.sprite = sprite; s.card.color = Color.white; SizeCard(s, sprite); }
            else { s.card.color = new Color(0.2f, 0.2f, 0.25f, 1f); }
            s.glow.color = new Color(tint.r, tint.g, tint.b, glowAlpha);
        });
        // ...and pop it back, glowing its colour (emerge from the dark).
        seq.Append(s.bob.DOScale(1.12f, revealDur * 0.6f).SetEase(Ease.OutBack));
        seq.Append(s.bob.DOScale(1f, revealDur * 0.3f).SetEase(Ease.OutSine));
    }

    void PopSlot(Slot s, CardData data)
    {
        Color tint = ResolveColor(data);
        Sprite sprite = data != null ? data.cardSprite : null;
        if (sprite != null) { s.card.sprite = sprite; s.card.color = Color.white; SizeCard(s, sprite); }
        s.glow.color = new Color(tint.r, tint.g, tint.b, glowAlpha);

        s.bob.DOKill();
        s.bob.localScale = Vector3.one * 0.7f;
        s.bob.DOScale(1f, 0.22f).SetEase(Ease.OutBack).SetUpdate(true);
    }

    // ----------------------------------------------------------------- show / hide

    void ShowStage()
    {
        EnsureOverlay();
        stageRoot.gameObject.SetActive(true);

        stageGroup.DOKill();
        stageGroup.blocksRaycasts = true;
        stageGroup.alpha = 0f;
        stageRoot.localScale = Vector3.one * 0.97f;
        stageGroup.DOFade(1f, showDur).SetUpdate(true);
        stageRoot.DOScale(1f, showDur).SetEase(Ease.OutBack).SetUpdate(true);

        for (int i = 0; i < slots.Count; i++)
        {
            var s = slots[i];
            s.cg.DOKill();
            s.cg.alpha = 0f;
            s.cg.DOFade(1f, showDur).SetDelay(i * 0.04f).SetUpdate(true);
            StartBob(s, i * 0.1f);
        }
    }

    void HideStage()
    {
        if (overlayGO == null) return;
        KillIdle();

        if (stageGroup)
        {
            stageGroup.DOKill();
            stageGroup.blocksRaycasts = false;
            stageGroup.DOFade(0f, hideDur).SetUpdate(true)
                .OnComplete(() => { if (stageRoot) stageRoot.gameObject.SetActive(false); });
        }
    }

    void StartBob(Slot s, float phase)
    {
        s.bob.DOKill();
        s.bob.anchoredPosition = Vector2.zero;
        Idle(s.bob.DOAnchorPosY(7f, 1.2f).SetDelay(phase)
            .SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo).SetUpdate(true));
        Idle(s.bob.DOLocalRotate(new Vector3(0, 0, 2.5f), 1.5f).SetDelay(phase)
            .SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo).SetUpdate(true));
    }

    void ShakeStage()
    {
        if (stageRoot == null) return;
        stageRoot.DOComplete();
        stageRoot.DOShakeAnchorPos(0.3f, new Vector2(16f, 0f), 14, 0f).SetUpdate(true);
    }

    void ShakeSlot(Slot s)
    {
        if (s == null || s.rt == null) return;
        s.rt.DOComplete();
        s.rt.DOShakeAnchorPos(0.25f, new Vector2(10f, 0f), 12, 0f).SetUpdate(true);
    }

    Tween Idle(Tween t) { if (t != null) idle.Add(t); return t; }
    void KillIdle() { for (int i = 0; i < idle.Count; i++) idle[i]?.Kill(); idle.Clear(); }

    // ----------------------------------------------------------------- helpers

    HandManager Hand()
    {
        if (handManager) return handManager;
        handManager = BattleManager.Instance != null ? BattleManager.Instance.handManager : null;
        return handManager;
    }

    Transform Junior()
    {
        if (junior) return junior;
        var p = BattleManager.Instance != null ? BattleManager.Instance.player : null;
        return p ? p.transform : null;
    }

    bool IsPointerOverUI() => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    bool RightClickHitsJunior()
    {
        var cam = zoomCamera ? zoomCamera : Camera.main;
        var j = Junior();
        if (cam == null || j == null) return false;
        Vector2 world = cam.ScreenToWorldPoint(Input.mousePosition);
        int mask = (juniorMask.value == 0) ? ~0 : juniorMask.value;
        var hit = Physics2D.Raycast(world, Vector2.zero, 0f, mask);
        return hit.collider != null && hit.collider.GetComponentInParent<Player>() != null;
    }

    void PlaySfx(AudioClip clip)
    {
        if (clip && AudioManager.Instance) AudioManager.Instance.PlaySound(clip, sfxVolume);
    }

    void HideOtherUI()
    {
        hiddenNow.Clear();
        if (hideDuringForesee == null) return;
        foreach (var go in hideDuringForesee)
            if (go != null && go.activeSelf) { go.SetActive(false); hiddenNow.Add(go); }
    }

    void RestoreOtherUI()
    {
        for (int i = 0; i < hiddenNow.Count; i++) if (hiddenNow[i] != null) hiddenNow[i].SetActive(true);
        hiddenNow.Clear();
    }

    Color ResolveColor(CardData d)
    {
        if (d == null) return Color.white;
        if (EffectDirector.Instance != null) return EffectDirector.Instance.ResolveTypeColor(d.cardType, Color.white);
        return Color.white;
    }

    void SizeCard(Slot s, Sprite sprite)
    {
        float aspect = (sprite != null && sprite.rect.height > 0f) ? sprite.rect.width / sprite.rect.height : 0.68f;
        float h = cardHeight, w = h * aspect;
        s.card.rectTransform.sizeDelta = new Vector2(w, h);
        float g = Mathf.Max(w, h) * 1.7f;
        s.glow.rectTransform.sizeDelta = new Vector2(g, g);
    }

    Button MakeButton(string name, string label, Sprite sprite, Color fallback, Vector2 pos, UnityAction onClick)
    {
        var rt = NewRect(name, stageRoot, Half(), Half(), Half());
        rt.anchoredPosition = pos;

        var img = rt.gameObject.AddComponent<Image>();
        img.raycastTarget = true;

        if (sprite != null)
        {
            img.sprite = sprite;
            img.color = Color.white;
            img.preserveAspect = true;
            float aspect = sprite.rect.height > 0f ? sprite.rect.width / sprite.rect.height : 2.1f;
            rt.sizeDelta = new Vector2(buttonHeight * aspect, buttonHeight);
        }
        else
        {
            img.color = fallback;
            rt.sizeDelta = new Vector2(168f, buttonHeight);
            var t = NewText(rt, label, 26f, Color.white, TMPro.TextAlignmentOptions.Center);
            t.fontStyle = TMPro.FontStyles.Bold;
        }

        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);
        return btn;
    }

    // Opaque-centre → transparent-edge radial (card glows).
    Sprite GetRadial()
    {
        if (radialSprite) return radialSprite;
        radialSprite = MakeRadial(false);
        return radialSprite;
    }

    // Transparent-centre → dark-edge vignette (spotlights Junior).
    Sprite GetVignette()
    {
        if (vignetteSprite) return vignetteSprite;
        vignetteSprite = MakeRadial(true);
        return vignetteSprite;
    }

    Sprite MakeRadial(bool invert)
    {
        const int s = 128;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false)
        { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[s * s];
        float r = s * 0.5f;
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float d = Mathf.Sqrt((x - r) * (x - r) + (y - r) * (y - r)) / r;
                float a = invert ? Mathf.Clamp01((d - 0.35f) / 0.65f) : Mathf.Pow(Mathf.Clamp01(1f - d), 1.7f);
                px[y * s + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
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

    TMPro.TextMeshProUGUI NewText(Transform parent, string text, float size, Color color, TMPro.TextAlignmentOptions align)
    {
        var rt = NewRect("Label", parent, Vector2.zero, Vector2.one, Half());
        var t = rt.gameObject.AddComponent<TMPro.TextMeshProUGUI>();
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
        // Make sure we never leave the camera/animation/UI/music stuck if disabled mid-Foresee.
        if (phase != Phase.Off)
        {
            SetThinking(false);
            ZoomOut();
            RestoreOtherUI();
            if (distortMusic && MusicManager.Instance != null)
                MusicManager.Instance.RestoreMusicDefaults(0f);
            phase = Phase.Off;
        }
    }
}
