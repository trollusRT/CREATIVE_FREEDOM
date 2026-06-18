using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using DG.Tweening;

/// <summary>
/// Temporary test-build flow: a start screen + an end-of-fight return.
///
/// On scene load it covers the screen with a start menu (a centre image button to start the
/// fight, a Quit button at the bottom) and holds BattleManager back until Start is pressed.
/// When the battle is won or lost, it waits for the victory/defeat chime to finish, slides a
/// black curtain in, and reloads the scene — which brings the start screen back.
///
/// Self-contained like BattleIntroStinger: it builds its own root overlay canvas at runtime,
/// so the only thing to assign is the start button image (and optionally a quit image / bg).
/// </summary>
[DisallowMultipleComponent]
public class TestFlowController : MonoBehaviour
{
    public static TestFlowController Instance;

    public enum SlideFrom { Top, Bottom, Left, Right }

    [Header("Start screen")]
    public Color backgroundColor = new Color(0.05f, 0.05f, 0.06f, 1f);
    [Tooltip("Optional full-screen background art for the start screen.")]
    public Sprite backgroundImage;
    [Tooltip("Optional title shown above the start button.")]
    public string title = "";
    public Color titleColor = Color.white;

    [Header("Start button (centre)")]
    [Tooltip("Image used for the start button.")]
    public Sprite startButtonSprite;
    public float startButtonHeight = 170f;

    [Header("Quit button (bottom)")]
    [Tooltip("Optional image for the quit button; falls back to a labelled button.")]
    public Sprite quitButtonSprite;
    public string quitLabel = "QUIT";
    public float quitButtonHeight = 70f;
    public float quitBottomMargin = 70f;
    public Color quitColor = new Color(0.5f, 0.5f, 0.55f);

    [Header("Audio")]
    public AudioClip clickSfx;
    [Range(0f, 1f)] public float clickVolume = 1f;

    [Header("End transition")]
    public Color curtainColor = Color.black;
    [Tooltip("Extra pause after the chime finishes before the black curtain slides in.")]
    public float chimeExtraDelay = 0.2f;
    [Tooltip("Safety cap on how long to wait for the chime to finish.")]
    public float maxChimeWait = 12f;
    public float curtainSlideDur = 0.5f;
    public Ease curtainEase = Ease.InOutCubic;
    public SlideFrom curtainFrom = SlideFrom.Top;

    [Header("Behaviour")]
    [Tooltip("Hold the battle on the start screen until Start is pressed.")]
    public bool gateBattleStart = true;

    // ---- runtime ----
    GameObject overlayGO;
    RectTransform overlayRT;
    RectTransform startRoot;
    CanvasGroup startGroup;
    RectTransform curtainRT;
    bool ending;

    void Awake()
    {
        Instance = this;

        // When a run is active (we came from the map), the map drives the flow: no start screen and
        // don't gate the battle — combat begins immediately and victory/defeat return to the map.
        if (RunManager.Instance != null && RunManager.Instance.runActive)
            gateBattleStart = false;

        EnsureOverlay();
        if (gateBattleStart) ShowStartScreen();
        else if (startGroup) startGroup.alpha = 0f;
    }

    // ----------------------------------------------------------------- build

    void EnsureOverlay()
    {
        if (overlayGO) return;

        overlayGO = new GameObject("TestFlow (runtime)", typeof(RectTransform));
        overlayRT = (RectTransform)overlayGO.transform;
        overlayRT.SetParent(null, false);
        Stretch(overlayRT);

        var canvas = overlayGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 11000; // above intro stinger (10000) / damage numbers (9999)
        var scaler = overlayGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(800, 600);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        overlayGO.AddComponent<GraphicRaycaster>();

        BuildStartScreen();
        BuildCurtain();
    }

    void BuildStartScreen()
    {
        startRoot = NewRect("StartScreen", overlayRT, Vector2.zero, Vector2.one, Half());
        startGroup = startRoot.gameObject.AddComponent<CanvasGroup>();
        startGroup.alpha = 0f;
        startGroup.blocksRaycasts = false;

        // Background (solid colour, optionally an image on top).
        var bg = NewImage("Background", startRoot, backgroundColor, Vector2.zero, Vector2.one, Half());
        bg.raycastTarget = true;
        if (backgroundImage)
        {
            var art = NewImage("BackgroundArt", startRoot, Color.white, Vector2.zero, Vector2.one, Half());
            art.sprite = backgroundImage;
            art.preserveAspect = false;
            art.raycastTarget = false;
        }

        // Optional title.
        if (!string.IsNullOrWhiteSpace(title))
        {
            var t = NewText(startRoot, title, 56f, titleColor, TextAlignmentOptions.Center);
            t.fontStyle = FontStyles.Bold;
            var trt = t.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
            trt.pivot = Half();
            trt.sizeDelta = new Vector2(700f, 90f);
            trt.anchoredPosition = new Vector2(0f, 150f);
        }

        // Centre start button.
        MakeButton("StartButton", "START", startButtonSprite, new Color(0.2f, 0.7f, 0.35f),
            startButtonHeight, new Vector2(0.5f, 0.5f), Half(), Vector2.zero, StartFight);

        // Bottom quit button.
        MakeButton("QuitButton", quitLabel, quitButtonSprite, quitColor,
            quitButtonHeight, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, quitBottomMargin), QuitGame);
    }

    void BuildCurtain()
    {
        curtainRT = NewRect("Curtain", overlayRT, Vector2.zero, Vector2.one, Half());
        var img = curtainRT.gameObject.AddComponent<Image>();
        img.color = curtainColor;
        img.raycastTarget = true;
        curtainRT.gameObject.SetActive(false);
    }

    // ----------------------------------------------------------------- start menu

    public void ShowStartScreen()
    {
        EnsureOverlay();
        if (startGroup == null) return;
        startGroup.DOKill();
        startGroup.alpha = 1f;
        startGroup.blocksRaycasts = true;
        startRoot.gameObject.SetActive(true);
    }

    void HideStartScreen()
    {
        if (startGroup == null) return;
        startGroup.blocksRaycasts = false;
        startGroup.DOKill();
        startGroup.DOFade(0f, 0.25f).SetUpdate(true)
            .OnComplete(() => { if (startRoot) startRoot.gameObject.SetActive(false); });
    }

    public void StartFight()
    {
        if (!gateBattleStart) return;
        gateBattleStart = false;
        PlayClick();

        // Kick off the battle (intro stinger + fight) behind the menu, then reveal it.
        if (BattleManager.Instance != null) BattleManager.Instance.BeginIntroAndBattle();
        HideStartScreen();
    }

    public void QuitGame()
    {
        PlayClick();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ----------------------------------------------------------------- end of fight

    /// <summary>Wait for the win/lose chime, slide the black curtain in, then return to the start screen.</summary>
    public IEnumerator Co_EndFight(bool won)
    {
        if (ending) yield break;
        ending = true;

        yield return Co_WaitForChime();
        if (chimeExtraDelay > 0f) yield return new WaitForSecondsRealtime(chimeExtraDelay);
        yield return Co_SlideCurtainIn();

        // Reloading resets everything and brings the start screen back.
        // (Requires the scene to be in File > Build Settings.)
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    IEnumerator Co_WaitForChime()
    {
        var src = MusicManager.Instance ? MusicManager.Instance.musicSource : null;
        yield return null; // let the chime get going
        float t = 0f;
        while (src != null && src.isPlaying && t < maxChimeWait)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    IEnumerator Co_SlideCurtainIn()
    {
        EnsureOverlay();
        Canvas.ForceUpdateCanvases();
        float W = overlayRT.rect.width; if (W < 1f) W = 800f;
        float H = overlayRT.rect.height; if (H < 1f) H = 600f;

        curtainRT.gameObject.SetActive(true);
        curtainRT.SetAsLastSibling();
        curtainRT.anchoredPosition = OffscreenOffset(curtainFrom, W, H);

        curtainRT.DOKill();
        curtainRT.DOAnchorPos(Vector2.zero, curtainSlideDur).SetEase(curtainEase).SetUpdate(true);
        yield return new WaitForSecondsRealtime(curtainSlideDur);
        curtainRT.anchoredPosition = Vector2.zero;
    }

    static Vector2 OffscreenOffset(SlideFrom from, float W, float H)
    {
        switch (from)
        {
            case SlideFrom.Top: return new Vector2(0f, H);
            case SlideFrom.Bottom: return new Vector2(0f, -H);
            case SlideFrom.Left: return new Vector2(-W, 0f);
            default: return new Vector2(W, 0f); // Right
        }
    }

    void PlayClick()
    {
        if (clickSfx && AudioManager.Instance) AudioManager.Instance.PlaySound(clickSfx, clickVolume);
    }

    // ----------------------------------------------------------------- ui helpers

    Button MakeButton(string name, string label, Sprite sprite, Color fallback, float height,
        Vector2 anchor, Vector2 pivot, Vector2 pos, UnityEngine.Events.UnityAction onClick)
    {
        var rt = NewRect(name, startRoot, anchor, anchor, pivot);
        rt.anchoredPosition = pos;

        var img = rt.gameObject.AddComponent<Image>();
        img.raycastTarget = true;

        if (sprite != null)
        {
            img.sprite = sprite;
            img.color = Color.white;
            img.preserveAspect = true;
            float aspect = sprite.rect.height > 0f ? sprite.rect.width / sprite.rect.height : 2.4f;
            rt.sizeDelta = new Vector2(height * aspect, height);
        }
        else
        {
            img.color = fallback;
            rt.sizeDelta = new Vector2(Mathf.Max(220f, height * 3f), height);
            var t = NewText(rt, label, 30f, Color.white, TextAlignmentOptions.Center);
            t.fontStyle = FontStyles.Bold;
        }

        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);
        return btn;
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
        var rt = NewRect("Label", parent, Vector2.zero, Vector2.one, Half());
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
}
