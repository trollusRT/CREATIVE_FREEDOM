using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using System.Collections.Generic;

// Self-contained floating combat numbers. Call DamageNumbers.ShowDamage / ShowHeal from anywhere;
// it lazily creates its own screen-space overlay canvas if none is placed in the scene.
// Optionally drop a configured DamageNumbers component in the scene to set the font / colours / motion.
public class DamageNumbers : MonoBehaviour
{
    public static DamageNumbers Instance;

    [Header("Look")]
    [Tooltip("Optional: assign your pixel font to match the HUD. Uses the TMP default if empty.")]
    [SerializeField] TMP_FontAsset font;
    [SerializeField] float fontSize = 36f;
    [SerializeField] Color damageColor = new Color(1f, 0.45f, 0.4f);
    [SerializeField] Color healColor = new Color(0.55f, 1f, 0.55f);

    [Header("Motion")]
    [SerializeField] Vector3 worldOffset = new Vector3(0f, 1f, 0f);
    [SerializeField] float riseDistance = 90f;   // screen pixels travelled upward
    [SerializeField] float spread = 40f;         // random horizontal jitter (px)
    [SerializeField] float lifetime = 0.85f;

    Canvas canvas;
    Camera cam;
    readonly Queue<TextMeshProUGUI> pool = new();

    void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) { Destroy(gameObject); return; }
        EnsureCanvas();
    }

    static bool Bootstrap()
    {
        if (Instance != null) return true;
        var go = new GameObject("DamageNumbers (auto)");
        go.AddComponent<DamageNumbers>();   // Awake sets Instance + builds the canvas
        return Instance != null;
    }

    public static void ShowDamage(Vector3 worldPos, int amount)
    {
        if (amount <= 0 || !Bootstrap()) return;
        Instance.Spawn(worldPos, amount.ToString(), Instance.damageColor);
    }

    public static void ShowHeal(Vector3 worldPos, int amount)
    {
        if (amount <= 0 || !Bootstrap()) return;
        Instance.Spawn(worldPos, "+" + amount, Instance.healColor);
    }

    void EnsureCanvas()
    {
        if (canvas) return;
        var go = new GameObject("DamageNumbersCanvas", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9999;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
    }

    void Spawn(Vector3 worldPos, string text, Color color)
    {
        if (!cam) cam = Camera.main;
        if (!cam) return;
        EnsureCanvas();

        var tmp = Get();
        tmp.text = text;
        color.a = 1f;
        tmp.color = color;

        var rt = tmp.rectTransform;
        Vector3 screen = cam.WorldToScreenPoint(worldPos + worldOffset);
        Vector2 start = new Vector2(screen.x + Random.Range(-spread, spread), screen.y);
        rt.anchoredPosition = start;
        rt.localScale = Vector3.one * 0.5f;

        DOTween.Sequence().SetUpdate(true).SetTarget(tmp)
            .Append(rt.DOScale(1f, 0.15f).SetEase(Ease.OutBack))
            .Join(rt.DOAnchorPosY(start.y + riseDistance, lifetime).SetEase(Ease.OutQuad))
            .Insert(lifetime * 0.45f,
                DOTween.To(() => tmp.alpha, a => tmp.alpha = a, 0f, lifetime * 0.55f).SetTarget(tmp))
            .OnComplete(() => Release(tmp));
    }

    TextMeshProUGUI Get()
    {
        TextMeshProUGUI t = pool.Count > 0 ? pool.Dequeue() : CreateText();
        t.alpha = 1f;
        t.gameObject.SetActive(true);
        t.transform.SetAsLastSibling();
        return t;
    }

    TextMeshProUGUI CreateText()
    {
        var go = new GameObject("DmgText", typeof(RectTransform));
        go.transform.SetParent(canvas.transform, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (font) t.font = font;
        t.enableAutoSizing = false;
        t.fontSize = fontSize;
        t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false;
        t.fontStyle = FontStyles.Bold;

        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(220f, 70f);
        return t;
    }

    void Release(TextMeshProUGUI t)
    {
        if (!t) return;
        t.gameObject.SetActive(false);
        pool.Enqueue(t);
    }
}
