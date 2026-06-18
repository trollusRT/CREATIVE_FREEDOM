using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// Dramatic map-to-battle faceoff stinger.
///
/// Animates in 5 beats (see the prototype):
///   1. Junior's TOP half slides in from the LEFT.
///   2. The enemies' BOTTOM half slides in from the RIGHT (each portrait eases in, staggered).
///   3. The stage title slides in from the middle-left.
///   4. The enemy names slide up from under the title.
///   5. The two halves part (top up / bottom down) to reveal the live battle below.
///
/// Self-contained like DamageNumbers: builds its own screen-space overlay canvas and all
/// UI at runtime, runs entirely on DOTween + unscaled time. Drop it on an empty GameObject
/// in the battle scene, assign the slide portraits, and BattleManager drives it on Start.
/// Every phase duration is exposed so you can retime the sting to a music beat.
/// </summary>
[DisallowMultipleComponent]
public class BattleIntroStinger : MonoBehaviour
{
    [Serializable]
    public class EnemyIntro
    {
        [Tooltip("Name shown on the stinger. Leave blank to pull from BattleManager's live enemies by order.")]
        public string displayName;
        [Tooltip("Dramatic slide-in portrait for this enemy (e.g. MEI-ISlidePortrait / msRememberSlidePortrait).")]
        public Sprite portrait;
    }

    [Header("Cast")]
    [Tooltip("Junior's faceoff portrait (juniorIntroPortrait).")]
    public Sprite juniorPortrait;
    public List<EnemyIntro> enemies = new List<EnemyIntro>();
    [Tooltip("Fill blank enemy names from BattleManager.enemies in order.")]
    public bool autoFillEnemyNames = true;

    [Header("Text")]
    public string stageTitle = "DIRE STAGE";
    [Tooltip("Optional. Falls back to the TMP default font if empty.")]
    public TMP_FontAsset font;

    [Header("Top half — Junior")]
    public Color topBackgroundColor = new Color(0.93f, 0.93f, 0.95f);
    [Range(0.3f, 1.6f)] public float juniorHeightFrac = 1.0f; // of the half's height
    [Range(0f, 0.4f)] public float juniorRightFrac = 0.02f;
    [Range(-0.3f, 0.3f)] public float juniorBottomFrac = 0f;

    [Header("Bottom half — enemies")]
    public Color bottomBackgroundColor = new Color(0.06f, 0.06f, 0.07f);
    [Range(0.3f, 1.4f)] public float enemyHeightFrac = 0.98f;
    [Range(0f, 0.6f)] public float enemyLeftFrac = 0.05f;
    [Tooltip("Horizontal gap between enemy portraits, as a fraction of screen width.")]
    [Range(0.05f, 0.6f)] public float enemyStepFrac = 0.26f;
    [Range(-0.3f, 0.3f)] public float enemyBottomFrac = 0f;

    [Header("Banner layout")]
    [Range(0f, 0.4f)] public float titleXFrac = 0.04f;
    [Tooltip("Left edge of the enemy-name column, as a fraction of width. Leave at 0 to auto-place it just past the title.")]
    [Range(0f, 0.8f)] public float namesXFrac = 0f;
    [Tooltip("Vertical offset of the banner from the divider line (+up), as a fraction of height.")]
    [Range(-0.2f, 0.2f)] public float bannerYFrac = 0.0f;
    [Range(0.04f, 0.2f)] public float titleSizeFrac = 0.085f;
    [Range(0.02f, 0.1f)] public float nameSizeFrac = 0.048f;
    public Color titleColor = Color.white;
    public Color enemyNameColor = new Color(0.92f, 0.18f, 0.16f);

    [Header("Backdrop")]
    [Tooltip("Opaque sheet behind the halves so the live battle stays hidden until the reveal.")]
    public bool useBackdrop = true;
    public Color backdropColor = Color.black;

    [Header("Speed lines (rushing motion)")]
    public bool speedLines = true;
    [Range(0f, 0.6f)] public float speedLineAlpha = 0.18f;
    [Tooltip("Horizontal scroll speed of the streaks (UV units / sec). Top and bottom rush opposite ways.")]
    [Range(0f, 3f)] public float speedLineSpeed = 0.8f;
    [Tooltip("Roughly how many streak lines span each half.")]
    [Range(6, 60)] public int speedLineCount = 22;

    [Header("Reveal")]
    [Tooltip("Extends each half's background past the screen edge so the curtain's anticipation dip never leaves a gap.")]
    [Range(0f, 0.5f)] public float edgeOverflowFrac = 0.25f;

    [Header("Reveal impact (juice)")]
    [Tooltip("Full-screen flash blinked on the reveal beat to sell the hit.")]
    public bool revealFlash = true;
    public Color revealFlashColor = Color.white;
    [Range(0f, 1f)] public float revealFlashAlpha = 0.9f;
    [Tooltip("Camera jolt on the reveal beat (uses CameraShakeManager if present, else shakes Camera.main).")]
    public bool revealShake = true;
    [Tooltip("Shake strength in world units (CameraShakeManager's default is ~0.35).")]
    public float revealShakeStrength = 0.4f;

    [Header("Timeline (seconds, unscaled)")]
    [Tooltip("Master multiplier on every duration below. <1 snappier, >1 slower — stretch the whole sting onto your beat.")]
    [Range(0.25f, 3f)] public float speed = 1f;
    public float startDelay = 0.10f;
    public float topSlideDur = 0.38f;
    [Tooltip("Bottom half starts this long AFTER the top half starts (overlap for snap).")]
    public float bottomDelay = 0.12f;
    public float bottomSlideDur = 0.38f;
    [Tooltip("Stagger between each enemy portrait easing in.")]
    public float enemyStagger = 0.09f;
    [Tooltip("Title starts this long after the halves have settled.")]
    public float titleDelay = 0.06f;
    public float titleSlideDur = 0.30f;
    public float namesDelay = 0.06f;
    public float nameSlideDur = 0.26f;
    public float nameStagger = 0.09f;
    [Tooltip("Dramatic hold on the full faceoff card before the curtains part.")]
    public float hold = 0.55f;
    public float revealDur = 0.50f;

    [Header("Easing")]
    public Ease slideInEase = Ease.OutExpo;
    public Ease bannerEase = Ease.OutBack;
    public Ease revealEase = Ease.InBack;

    [Header("Audio")]
    [Tooltip("Optional music/SFX sting fired the instant the stinger begins.")]
    public AudioClip stingClip;
    [Range(0f, 1f)] public float stingVolume = 1f;

    [Header("Debug")]
    [Tooltip("Play automatically on Start (otherwise BattleManager drives it).")]
    public bool playOnStart = false;
    [Tooltip("Log the absolute beat times so you can line them up with the music.")]
    public bool logTimeline = true;

    // ---- runtime ----
    GameObject rootGO;
    RectTransform rootRT;
    RectTransform topRT, bottomRT;
    RectTransform topBgRT, bottomBgRT;
    RectTransform juniorRT;
    Image backdropImg;
    CanvasGroup flashGroup;

    struct Scroller { public RawImage raw; public float dir; }
    readonly List<Scroller> scrollers = new List<Scroller>();
    CanvasGroup bannerGroup;
    RectTransform titleRT;
    CanvasGroup titleGroup;
    TextMeshProUGUI titleMainText;
    readonly List<RectTransform> nameRTs = new List<RectTransform>();
    readonly List<CanvasGroup> nameGroups = new List<CanvasGroup>();

    class EnemyVisual { public RectTransform rt; public CanvasGroup cg; public Vector2 basePos; public Sprite sprite; }
    readonly List<EnemyVisual> enemyVisuals = new List<EnemyVisual>();

    Sequence seq;
    bool playing;
    AudioSource fallbackAudio;

    const float EnemyPopDur = 0.22f;

    void Start()
    {
        if (playOnStart) Play();
    }

    /// <summary>Build (if needed) and run the stinger.</summary>
    /// <param name="onReveal">Fires the instant the curtains begin to part — kick off music + battle here.</param>
    /// <param name="onComplete">Fires once the curtains are fully open and the overlay has been torn down.</param>
    public void Play(Action onReveal = null, Action onComplete = null)
    {
        if (playing) return;
        playing = true;

        ResolveNames();
        BuildUI();
        StartCoroutine(Co_Run(onReveal, onComplete));
    }

    void ResolveNames()
    {
        if (!autoFillEnemyNames) return;
        var bm = BattleManager.Instance;
        if (bm == null || bm.enemies == null) return;

        for (int i = 0; i < enemies.Count; i++)
        {
            if (enemies[i] != null && string.IsNullOrWhiteSpace(enemies[i].displayName) &&
                i < bm.enemies.Count && bm.enemies[i] != null)
            {
                enemies[i].displayName = bm.enemies[i].enemyName;
            }
        }
    }

    /// <summary>
    /// Replace the faceoff cast with the actual fight: portraits + names pulled from each
    /// enemy's EnemyData. Call this before <see cref="Play"/>.
    ///
    /// No-op when none of the live enemies carry EnemyData, so a hand-authored cast (e.g. a
    /// scene placed without the spawner) is preserved. Enemies whose EnemyData has no
    /// slidePortrait simply show no portrait but still contribute a name to the banner.
    /// </summary>
    public void BuildCastFromEnemies(List<Enemy> liveEnemies)
    {
        if (liveEnemies == null || liveEnemies.Count == 0) return;

        bool anyData = false;
        foreach (var e in liveEnemies)
            if (e != null && e.data != null) { anyData = true; break; }
        if (!anyData) return;

        enemies.Clear();
        foreach (var e in liveEnemies)
        {
            if (e == null) continue;
            var d = e.data;
            enemies.Add(new EnemyIntro
            {
                displayName = (d != null && !string.IsNullOrWhiteSpace(d.enemyName)) ? d.enemyName : e.enemyName,
                portrait = d != null ? d.slidePortrait : null
            });
        }
    }

    // ----------------------------------------------------------------- build

    void BuildUI()
    {
        if (rootGO) return;

        rootGO = new GameObject("BattleIntroStinger (runtime)", typeof(RectTransform));
        rootRT = (RectTransform)rootGO.transform;
        // Live at the scene root so the canvas is a true top-level overlay (fills the screen).
        // If this component sits under an existing Canvas, parenting here would nest the canvas
        // and it would shrink to that parent's rect — so keep it un-parented.
        rootRT.SetParent(null, false);
        rootRT.anchorMin = Vector2.zero;
        rootRT.anchorMax = Vector2.one;
        rootRT.offsetMin = Vector2.zero;
        rootRT.offsetMax = Vector2.zero;
        rootRT.localScale = Vector3.one;

        var canvas = rootGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 10000; // above everything, incl. DamageNumbers (9999)
        var scaler = rootGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(800, 600);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        rootGO.AddComponent<GraphicRaycaster>(); // swallow clicks while the stinger is up

        // Backdrop covers everything from frame 0 so the live battle stays hidden.
        if (useBackdrop)
        {
            backdropImg = NewImage("Backdrop", rootRT, backdropColor, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            backdropImg.raycastTarget = true;
        }

        scrollers.Clear();

        // Top half: covers upper half, holds Junior. Background sits in a child that can
        // overflow past the screen top (so the reveal's anticipation dip leaves no gap).
        topRT = NewRect("TopHalf", rootRT, new Vector2(0f, 0.5f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f));
        topBgRT = NewRect("TopBg", topRT, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
        var topBg = topBgRT.gameObject.AddComponent<Image>();
        topBg.color = topBackgroundColor;
        topBg.raycastTarget = true;
        if (speedLines) AddSpeedLines(topBgRT, new Color(0f, 0f, 0f, speedLineAlpha), +1f);

        // Bottom half: covers lower half, holds the enemies. Background overflows past the screen bottom.
        bottomRT = NewRect("BottomHalf", rootRT, new Vector2(0f, 0f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f));
        bottomBgRT = NewRect("BottomBg", bottomRT, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
        var botBg = bottomBgRT.gameObject.AddComponent<Image>();
        botBg.color = bottomBackgroundColor;
        botBg.raycastTarget = true;
        if (speedLines) AddSpeedLines(bottomBgRT, new Color(1f, 1f, 1f, speedLineAlpha), -1f);

        // Park the halves off-screen immediately (generous) so there's no covered-frame flash.
        topRT.anchoredPosition = new Vector2(-20000f, 0f);
        bottomRT.anchoredPosition = new Vector2(20000f, 0f);

        BuildPortraits();
        BuildBanner();

        // Reveal flash — topmost so it whites out the whole screen on the beat.
        if (revealFlash)
        {
            var img = NewImage("RevealFlash", rootRT,
                new Color(revealFlashColor.r, revealFlashColor.g, revealFlashColor.b, 1f),
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            flashGroup = img.gameObject.AddComponent<CanvasGroup>();
            flashGroup.alpha = 0f;
            flashGroup.blocksRaycasts = false;
            img.transform.SetAsLastSibling();
        }
    }

    void BuildPortraits()
    {
        // Junior, anchored to the bottom-right of the top half (standing on the divider line).
        if (juniorPortrait)
        {
            var jr = NewRect("Junior", topRT, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f));
            var img = jr.gameObject.AddComponent<Image>();
            img.sprite = juniorPortrait;
            img.preserveAspect = true;
            img.raycastTarget = false;
            jr.sizeDelta = SizeForSprite(juniorPortrait, juniorHeightFrac); // sized in Co_Run once dims known
            jr.anchoredPosition = Vector2.zero;
            juniorRT = jr;
        }

        // Enemies, anchored bottom-left of the bottom half, spaced left→right.
        enemyVisuals.Clear();
        for (int i = 0; i < enemies.Count; i++)
        {
            var e = enemies[i];
            if (e == null || e.portrait == null) continue;

            var er = NewRect($"Enemy_{i}", bottomRT, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0.5f, 0f));
            var img = er.gameObject.AddComponent<Image>();
            img.sprite = e.portrait;
            img.preserveAspect = true;
            img.raycastTarget = false;
            var cg = er.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = 0f;
            enemyVisuals.Add(new EnemyVisual { rt = er, cg = cg, sprite = e.portrait });
        }
    }

    void BuildBanner()
    {
        var bannerRT = NewRect("Banner", rootRT, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
        bannerGroup = bannerRT.gameObject.AddComponent<CanvasGroup>();
        bannerGroup.alpha = 1f; // children fade individually on entrance
        bannerGroup.blocksRaycasts = false;

        // Title — left of centre, sitting on the divider line.
        titleRT = BuildLabel("Title", bannerRT, stageTitle, 48f, titleColor,
            TextAlignmentOptions.Left, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            Vector2.zero, new Vector2(600f, 120f), out titleGroup);
        titleGroup.alpha = 0f;
        var titleText = titleRT.Find("Text");
        titleMainText = titleText ? titleText.GetComponent<TextMeshProUGUI>() : null;

        // Enemy names — stacked to the right of the title.
        nameRTs.Clear();
        nameGroups.Clear();
        for (int i = 0; i < enemies.Count; i++)
        {
            string nm = (enemies[i] != null && !string.IsNullOrWhiteSpace(enemies[i].displayName))
                ? enemies[i].displayName.ToUpperInvariant()
                : $"ENEMY {i + 1}";

            var nrt = BuildLabel($"Name_{i}", bannerRT, nm, 24f, enemyNameColor,
                TextAlignmentOptions.Left, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                Vector2.zero, new Vector2(520f, 60f), out var ncg);
            ncg.alpha = 0f;
            nameRTs.Add(nrt);
            nameGroups.Add(ncg);
        }
    }

    // ----------------------------------------------------------------- run

    IEnumerator Co_Run(Action onReveal, Action onComplete)
    {
        // Let the canvas lay out so rect dims are valid (backdrop hides this frame).
        Canvas.ForceUpdateCanvases();
        yield return null;

        float W = rootRT.rect.width; if (W < 1f) W = 800f;
        float H = rootRT.rect.height; if (H < 1f) H = 600f;
        float halfH = H * 0.5f;

        LayoutForDimensions(W, H, halfH);

        // Halves start just off their respective edges (exact, for consistent slide speed).
        topRT.anchoredPosition = new Vector2(-W, 0f);
        bottomRT.anchoredPosition = new Vector2(W, 0f);

        float sp = Mathf.Max(0.05f, speed);
        float S(float x) => x * sp;

        int enemyCount = enemyVisuals.Count;
        int nameCount = nameRTs.Count;

        // --- Absolute beat times (pre-speed) ---
        float tTop = startDelay;
        float tBottom = tTop + bottomDelay;
        float tHalvesIn = Mathf.Max(tTop + topSlideDur, tBottom + bottomSlideDur);
        float tEnemyPop = tBottom + bottomSlideDur * 0.55f;
        float tEnemyEnd = tEnemyPop + Mathf.Max(0, enemyCount - 1) * enemyStagger + EnemyPopDur;
        float tTitle = Mathf.Max(tHalvesIn, tEnemyEnd) + titleDelay;
        float tNames = tTitle + namesDelay;
        float tNamesEnd = tNames + Mathf.Max(0, nameCount - 1) * nameStagger + nameSlideDur;
        float tReveal = tNamesEnd + hold;
        float tEnd = tReveal + revealDur;

        if (logTimeline) LogTimeline(sp, tTop, tBottom, tTitle, tNames, tReveal, tEnd);

        PlaySting();

        seq = DOTween.Sequence().SetUpdate(true);

        // 1 — top half slides in from the left.
        seq.Insert(S(tTop), topRT.DOAnchorPosX(0f, S(topSlideDur)).SetEase(slideInEase));

        // 2 — bottom half slides in from the right, then each enemy eases in (staggered).
        seq.Insert(S(tBottom), bottomRT.DOAnchorPosX(0f, S(bottomSlideDur)).SetEase(slideInEase));
        for (int i = 0; i < enemyVisuals.Count; i++)
        {
            var ev = enemyVisuals[i];
            float t = tEnemyPop + i * enemyStagger;
            ev.rt.anchoredPosition = ev.basePos + new Vector2(W * 0.04f, -halfH * 0.06f);
            ev.rt.localScale = Vector3.one * 0.92f;
            seq.Insert(S(t), ev.rt.DOAnchorPos(ev.basePos, S(EnemyPopDur)).SetEase(Ease.OutCubic));
            seq.Insert(S(t), ev.rt.DOScale(1f, S(EnemyPopDur)).SetEase(Ease.OutBack));
            seq.Insert(S(t), ev.cg.DOFade(1f, S(EnemyPopDur * 0.8f)));
        }

        // 3 — title slides in from the middle-left.
        Vector2 titleHome = titleRT.anchoredPosition;
        titleRT.anchoredPosition = titleHome + new Vector2(-W * 0.22f, 0f);
        seq.Insert(S(tTitle), titleRT.DOAnchorPos(titleHome, S(titleSlideDur)).SetEase(bannerEase));
        seq.Insert(S(tTitle), titleGroup.DOFade(1f, S(titleSlideDur * 0.7f)));

        // 4 — enemy names slide up from under the title.
        for (int i = 0; i < nameRTs.Count; i++)
        {
            float t = tNames + i * nameStagger;
            Vector2 home = nameRTs[i].anchoredPosition;
            nameRTs[i].anchoredPosition = home + new Vector2(0f, -halfH * 0.18f);
            seq.Insert(S(t), nameRTs[i].DOAnchorPos(home, S(nameSlideDur)).SetEase(bannerEase));
            seq.Insert(S(t), nameGroups[i].DOFade(1f, S(nameSlideDur * 0.7f)));
        }

        // 5 — curtains part to reveal the live battle.
        seq.InsertCallback(S(tReveal), () => { try { onReveal?.Invoke(); } catch (Exception e) { Debug.LogException(e); } });
        seq.Insert(S(tReveal), topRT.DOAnchorPosY(halfH * 1.06f, S(revealDur)).SetEase(revealEase));
        seq.Insert(S(tReveal), bottomRT.DOAnchorPosY(-halfH * 1.06f, S(revealDur)).SetEase(revealEase));
        seq.Insert(S(tReveal), bannerGroup.DOFade(0f, S(revealDur * 0.55f)));
        if (backdropImg) seq.Insert(S(tReveal), backdropImg.DOFade(0f, S(revealDur)));

        // Impact juice: a quick whiteout flash + a camera jolt on the beat.
        if (revealFlash && flashGroup)
        {
            seq.Insert(S(tReveal), flashGroup.DOFade(revealFlashAlpha, S(revealDur * 0.12f)));
            seq.Insert(S(tReveal + revealDur * 0.12f), flashGroup.DOFade(0f, S(revealDur * 0.55f)));
        }
        if (revealShake) seq.InsertCallback(S(tReveal), DoRevealShake);

        seq.OnComplete(() =>
        {
            try { onComplete?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
            Teardown();
        });
    }

    // Resize / reposition the sprites now that we know the real canvas dimensions.
    void LayoutForDimensions(float W, float H, float halfH)
    {
        // Extend each half's background past its outer screen edge (inner edge stays on the divider).
        float ov = halfH * edgeOverflowFrac;
        if (topBgRT) { topBgRT.offsetMin = Vector2.zero; topBgRT.offsetMax = new Vector2(0f, ov); }
        if (bottomBgRT) { bottomBgRT.offsetMin = new Vector2(0f, -ov); bottomBgRT.offsetMax = Vector2.zero; }

        if (juniorRT && juniorPortrait)
        {
            juniorRT.sizeDelta = SizeForSprite(juniorPortrait, juniorHeightFrac, halfH);
            juniorRT.anchoredPosition = new Vector2(-W * juniorRightFrac, halfH * juniorBottomFrac);
        }

        float x = W * enemyLeftFrac;
        for (int i = 0; i < enemyVisuals.Count; i++)
        {
            var ev = enemyVisuals[i];
            ev.rt.sizeDelta = SizeForSprite(ev.sprite, enemyHeightFrac, halfH);
            ev.basePos = new Vector2(x + ev.rt.sizeDelta.x * 0.5f, halfH * enemyBottomFrac);
            ev.rt.anchoredPosition = ev.basePos;
            x += W * enemyStepFrac;
        }

        // Banner: title left, names stacked to the right, centred on the divider.
        float bannerY = halfH * bannerYFrac;
        float titleLeft = W * titleXFrac;
        float namesLeft = W * namesXFrac;
        if (titleRT)
        {
            SetLabelFontSize(titleRT, H * titleSizeFrac);
            titleRT.sizeDelta = new Vector2(W * 0.7f, H * titleSizeFrac * 1.4f);
            titleRT.anchoredPosition = new Vector2(titleLeft, bannerY);

            // Auto-place the name column just past the title's real rendered width.
            if (namesXFrac <= 0f && titleMainText)
            {
                titleMainText.ForceMeshUpdate();
                namesLeft = titleLeft + titleMainText.preferredWidth + W * 0.03f;
            }
        }

        float lineH = H * nameSizeFrac * 1.25f;
        float blockTop = (nameRTs.Count - 1) * 0.5f * lineH;
        for (int i = 0; i < nameRTs.Count; i++)
        {
            SetLabelFontSize(nameRTs[i], H * nameSizeFrac);
            nameRTs[i].sizeDelta = new Vector2(W * 0.5f, lineH);
            nameRTs[i].anchoredPosition = new Vector2(namesLeft, bannerY + blockTop - i * lineH);
        }
    }

    void DoRevealShake()
    {
        if (CameraShakeManager.Instance)
        {
            CameraShakeManager.Instance.Shake(revealShakeStrength);
            return;
        }
        // Fallback: punch Camera.main directly (unscaled, so it ignores any hit-stop).
        var cam = Camera.main;
        if (cam)
        {
            cam.transform.DOKill(true);
            cam.transform.DOShakePosition(0.25f, revealShakeStrength, 16, 90f, false, true).SetUpdate(true);
        }
    }

    void PlaySting()
    {
        if (!stingClip) return;
        if (AudioManager.Instance) AudioManager.Instance.PlaySound(stingClip, stingVolume);
        else
        {
            if (!fallbackAudio) fallbackAudio = gameObject.AddComponent<AudioSource>();
            fallbackAudio.PlayOneShot(stingClip, stingVolume);
        }
    }

    void Teardown()
    {
        seq = null;
        playing = false;
        scrollers.Clear();
        if (rootGO) Destroy(rootGO);
        rootGO = null;
    }

    void OnDisable()
    {
        if (seq != null && seq.IsActive()) seq.Kill();
    }

    void LogTimeline(float sp, float tTop, float tBottom, float tTitle, float tNames, float tReveal, float tEnd)
    {
        Debug.Log(
            "[BattleIntroStinger] beat timeline (unscaled s, speed=" + sp + "):\n" +
            $"  1 top slide-in   @ {tTop * sp:0.00}\n" +
            $"  2 bottom slide-in @ {tBottom * sp:0.00}\n" +
            $"  3 title          @ {tTitle * sp:0.00}\n" +
            $"  4 enemy names    @ {tNames * sp:0.00}\n" +
            $"  5 reveal/part    @ {tReveal * sp:0.00}\n" +
            $"  done             @ {tEnd * sp:0.00}");
    }

    // ----------------------------------------------------------------- ui helpers

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
        var rt = NewRect("T", parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (font) t.font = font;
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.raycastTarget = false;
        t.fontStyle = FontStyles.Bold;
        t.overflowMode = TextOverflowModes.Overflow; // labels live in wide rects, so they never wrap
        return t;
    }

    // A label with a soft drop shadow for punch. Returns the animatable container.
    RectTransform BuildLabel(string name, Transform parent, string text, float fontSize, Color color,
        TextAlignmentOptions align, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, out CanvasGroup cg)
    {
        var rt = NewRect(name, parent, anchor, anchor, pivot);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        cg = rt.gameObject.AddComponent<CanvasGroup>();

        var shadow = NewText(rt, text, fontSize, new Color(0f, 0f, 0f, 0.65f), align);
        shadow.name = "Shadow";
        shadow.rectTransform.anchoredPosition = new Vector2(3f, -3f);

        var main = NewText(rt, text, fontSize, color, align);
        main.name = "Text";
        return rt;
    }

    void SetLabelFontSize(RectTransform label, float size)
    {
        var texts = label.GetComponentsInChildren<TextMeshProUGUI>(true);
        foreach (var t in texts) t.fontSize = size;
    }

    // Size a rect so the sprite renders at `frac` of the half-height (width follows aspect).
    Vector2 SizeForSprite(Sprite sprite, float frac, float halfH = -1f)
    {
        if (halfH < 0f) halfH = 300f; // provisional; corrected in LayoutForDimensions
        float h = halfH * frac;
        float aspect = sprite.rect.height > 0f ? sprite.rect.width / sprite.rect.height : 1f;
        return new Vector2(h * aspect, h);
    }

    const int SpeedLinesPerTex = 8;

    void AddSpeedLines(RectTransform parent, Color lineColor, float dir)
    {
        var rt = NewRect("SpeedLines", parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
        var raw = rt.gameObject.AddComponent<RawImage>();
        raw.raycastTarget = false;
        raw.texture = BuildSpeedLineTexture(lineColor);
        float tilesY = Mathf.Max(1f, (float)speedLineCount / SpeedLinesPerTex);
        raw.uvRect = new Rect(0f, 0f, 1f, tilesY);
        raw.color = Color.white;
        scrollers.Add(new Scroller { raw = raw, dir = dir });
    }

    // Streaky horizontal lines that scroll sideways to read as rushing speed lines.
    Texture2D BuildSpeedLineTexture(Color lineColor)
    {
        const int w = 128, h = 64;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Repeat
        };

        var clear = new Color(lineColor.r, lineColor.g, lineColor.b, 0f);
        var px = new Color[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = clear;

        var rnd = new System.Random(7);
        for (int li = 0; li < SpeedLinesPerTex; li++)
        {
            int cy = Mathf.Clamp(Mathf.RoundToInt((li + 0.5f) / SpeedLinesPerTex * h), 0, h - 1);
            int thick = (rnd.Next(0, 3) == 0) ? 2 : 1;
            int k = 2 + rnd.Next(0, 5);                       // integer cycles -> seamless horizontal tiling
            float phase = (float)rnd.NextDouble() * Mathf.PI * 2f;
            float baseA = Mathf.Lerp(0.45f, 1f, (float)rnd.NextDouble());

            for (int t = 0; t < thick; t++)
            {
                int y = Mathf.Clamp(cy + t, 0, h - 1);
                for (int x = 0; x < w; x++)
                {
                    float dash = 0.30f + 0.70f * (0.5f * (1f + Mathf.Sin((float)x / w * k * 2f * Mathf.PI + phase)));
                    px[y * w + x] = new Color(lineColor.r, lineColor.g, lineColor.b, lineColor.a * baseA * dash);
                }
            }
        }

        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    void Update()
    {
        if (!playing || scrollers.Count == 0 || speedLineSpeed <= 0f) return;
        float d = Time.unscaledDeltaTime * speedLineSpeed;
        for (int i = 0; i < scrollers.Count; i++)
        {
            var s = scrollers[i];
            if (!s.raw) continue;
            var uv = s.raw.uvRect;
            uv.x += d * s.dir;
            s.raw.uvRect = uv;
        }
    }
}
