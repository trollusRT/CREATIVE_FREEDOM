using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;

/// <summary>
/// Post-victory reward: a choice of a fusion recipe OR an Insight (per MAP_DESIGN). Built entirely in
/// code like FusionController. BattleManager.HandleVictory yields on <see cref="Co_ShowAndAwaitChoice"/>
/// before returning to the map; the pick is applied to the run (RunManager.LearnRecipe / AddInsight) so
/// it carries to the next fight.
///
/// Robust to missing data: recipe options come from FusionBook (auto-found via FusionController), Insight
/// options from the InsightDatabase (auto-found via InsightHost). If one pool is empty it offers the
/// other; if both are empty it shows nothing and returns immediately.
/// </summary>
public class RewardController : MonoBehaviour
{
    public static RewardController Instance;

    [Header("Refs (auto-found if empty)")]
    public FusionBook fusionBook;
    public InsightDatabase insightDatabase;

    [Header("Reward")]
    [Tooltip("How many options to offer.")]
    public int optionCount = 3;
    [Tooltip("Show a Skip button (take nothing).")]
    public bool allowSkip = true;

    [Header("Layout (reference 800x600)")]
    public float cardWidth = 210f;
    public float cardHeight = 300f;
    public float cardSpacing = 240f;
    public float titleY = 220f;
    public float skipY = -235f;

    [Header("Look")]
    [Range(0f, 1f)] public float dimAlpha = 0.78f;
    public Color recipeColor = new Color(0.85f, 0.55f, 0.30f);
    public Color commonColor = new Color(0.55f, 0.55f, 0.58f);
    public Color uncommonColor = new Color(0.35f, 0.70f, 0.40f);
    public Color rareColor = new Color(0.35f, 0.55f, 0.85f);
    public Color epicColor = new Color(0.62f, 0.40f, 0.82f);

    [Header("Timings (unscaled)")]
    public float showDur = 0.3f;
    public float hideDur = 0.2f;

    [Header("Audio")]
    public AudioClip showSfx;
    public AudioClip pickSfx;
    [Range(0f, 1f)] public float sfxVolume = 1f;

    class Option
    {
        public bool isRecipe;
        public FusionRecipe recipe;
        public Insight insight;
    }

    bool choiceMade;
    GameObject overlayGO;
    RectTransform overlayRT, stageRoot;
    CanvasGroup stageGroup;
    Sprite radialSprite;

    void Awake() { Instance = this; }

    // ----------------------------------------------------------------- public flow

    public IEnumerator Co_ShowAndAwaitChoice()
    {
        var options = BuildOptions();
        if (options.Count == 0) yield break;     // nothing to offer — skip the screen entirely

        choiceMade = false;
        PlaySfx(showSfx);
        BuildStage(options);
        ShowStage();

        while (!choiceMade) yield return null;

        HideStage();
        yield return new WaitForSecondsRealtime(hideDur);
    }

    // ----------------------------------------------------------------- options

    List<Option> BuildOptions()
    {
        var recipes = EligibleRecipes();
        var insights = EligibleInsights();

        var opts = new List<Option>();
        int ri = 0, ii = 0;
        bool takeRecipe = true;                  // alternate so the set is mixed when both pools exist
        while (opts.Count < optionCount && (ri < recipes.Count || ii < insights.Count))
        {
            if (takeRecipe && ri < recipes.Count) opts.Add(new Option { isRecipe = true, recipe = recipes[ri++] });
            else if (!takeRecipe && ii < insights.Count) opts.Add(new Option { isRecipe = false, insight = insights[ii++] });
            else if (ri < recipes.Count) opts.Add(new Option { isRecipe = true, recipe = recipes[ri++] });
            else if (ii < insights.Count) opts.Add(new Option { isRecipe = false, insight = insights[ii++] });
            takeRecipe = !takeRecipe;
        }
        return opts;
    }

    List<FusionRecipe> EligibleRecipes()
    {
        var list = new List<FusionRecipe>();
        var book = Book();
        if (book == null || book.allRecipes == null) return list;

        var rm = RunManager.Instance;
        var learned = (rm != null && rm.unlockedRecipes != null) ? rm.unlockedRecipes : null;
        foreach (var r in book.allRecipes)
            if (r != null && r.result != null && (learned == null || !learned.Contains(r.recipeName)))
                list.Add(r);
        Shuffle(list);
        return list;
    }

    List<Insight> EligibleInsights()
    {
        var list = new List<Insight>();
        var db = DB();
        if (db == null || db.allInsights == null) return list;
        foreach (var ins in db.allInsights)
            if (ins != null && !string.IsNullOrEmpty(ins.id)) list.Add(ins);   // duplicates allowed (stacking)
        Shuffle(list);
        return list;
    }

    void Choose(Option o)
    {
        if (choiceMade) return;
        choiceMade = true;
        PlaySfx(pickSfx);

        var rm = RunManager.Instance;
        if (o != null && rm != null)
        {
            if (o.isRecipe && o.recipe != null) rm.LearnRecipe(o.recipe.recipeName);
            else if (!o.isRecipe && o.insight != null) rm.AddInsight(o.insight.id);
        }
    }

    void Skip() { if (!choiceMade) { choiceMade = true; PlaySfx(pickSfx); } }

    // ----------------------------------------------------------------- stage build

    void BuildStage(List<Option> options)
    {
        EnsureOverlay();

        // Clear a previous build (the row of cards + title + skip live under a fresh content root).
        foreach (Transform child in stageRoot) Destroy(child.gameObject);

        // Dim backdrop that swallows clicks behind the stage.
        var dim = NewImage("Dim", stageRoot, new Color(0f, 0f, 0f, dimAlpha), Vector2.zero, Vector2.one, Half());
        Stretch(dim.rectTransform);
        dim.sprite = GetRadial();
        dim.raycastTarget = true;

        NewText(stageRoot, "CHOOSE A REWARD", 40f, Color.white, new Vector2(0f, titleY), new Vector2(700f, 60f), FontStyles.Bold);

        int n = options.Count;
        float startX = -(n - 1) * 0.5f * cardSpacing;
        for (int i = 0; i < n; i++)
            MakeOptionCard(options[i], new Vector2(startX + i * cardSpacing, 0f));

        if (allowSkip)
        {
            var skip = MakeButton("Skip", "SKIP", commonColor, new Vector2(0f, skipY), new Vector2(150f, 56f), Skip);
            skip.transform.SetAsLastSibling();
        }
    }

    void MakeOptionCard(Option o, Vector2 pos)
    {
        var card = NewRect("Option", stageRoot, Half(), Half(), Half());
        card.anchoredPosition = pos;
        card.sizeDelta = new Vector2(cardWidth, cardHeight);

        Color frame = o.isRecipe ? recipeColor : RarityColor(o.insight != null ? o.insight.rarity : InsightRarity.Common);

        var bg = card.gameObject.AddComponent<Image>();
        bg.color = new Color(0.10f, 0.10f, 0.13f, 0.98f);
        bg.raycastTarget = true;

        // coloured top band (recipe tint / insight rarity)
        var band = NewImage("Band", card, frame, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        band.rectTransform.sizeDelta = new Vector2(0f, 40f);
        band.rectTransform.anchoredPosition = Vector2.zero;

        string kind = o.isRecipe ? "RECIPE" : (o.insight != null ? o.insight.rarity.ToString().ToUpper() + " INSIGHT" : "INSIGHT");
        NewText(card, kind, 18f, Color.white, new Vector2(0f, (cardHeight * 0.5f) - 20f), new Vector2(cardWidth - 16f, 30f), FontStyles.Bold);

        if (o.isRecipe)
        {
            var data = o.recipe.result;
            if (data != null && data.cardSprite != null)
            {
                var art = NewImage("Art", card, Color.white, Half(), Half(), Half());
                art.sprite = data.cardSprite;
                art.preserveAspect = true;
                art.rectTransform.sizeDelta = new Vector2(cardWidth - 40f, cardHeight * 0.5f);
                art.rectTransform.anchoredPosition = new Vector2(0f, 30f);
                art.raycastTarget = false;
            }
            string title = !string.IsNullOrEmpty(o.recipe.recipeName) ? o.recipe.recipeName
                          : (data != null ? data.cardName : "Recipe");
            NewText(card, title, 22f, Color.white, new Vector2(0f, -70f), new Vector2(cardWidth - 16f, 40f), FontStyles.Bold);
            string ing = (o.recipe.ingredientA != null && o.recipe.ingredientB != null)
                ? $"{o.recipe.ingredientA.cardName} + {o.recipe.ingredientB.cardName}" : "";
            NewText(card, ing, 15f, new Color(0.8f, 0.8f, 0.85f), new Vector2(0f, -110f), new Vector2(cardWidth - 16f, 60f), FontStyles.Italic);
        }
        else
        {
            var ins = o.insight;
            NewText(card, ins != null ? ins.displayName : "Insight", 22f, Color.white,
                new Vector2(0f, 60f), new Vector2(cardWidth - 16f, 60f), FontStyles.Bold);
            NewText(card, ins != null ? ins.description : "", 15f, new Color(0.85f, 0.85f, 0.9f),
                new Vector2(0f, -50f), new Vector2(cardWidth - 24f, 180f), FontStyles.Normal);
        }

        var btn = card.gameObject.AddComponent<Button>();
        btn.targetGraphic = bg;
        btn.onClick.AddListener(() => Choose(o));

        // gentle hover pop
        var rt = card;
        btn.transition = Selectable.Transition.None;
        var trigger = card.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
        AddHover(trigger, UnityEngine.EventSystems.EventTriggerType.PointerEnter, () => rt.DOScale(1.05f, 0.12f).SetUpdate(true));
        AddHover(trigger, UnityEngine.EventSystems.EventTriggerType.PointerExit, () => rt.DOScale(1f, 0.12f).SetUpdate(true));
    }

    static void AddHover(UnityEngine.EventSystems.EventTrigger trigger, UnityEngine.EventSystems.EventTriggerType type, System.Action action)
    {
        var entry = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(_ => action());
        trigger.triggers.Add(entry);
    }

    Color RarityColor(InsightRarity r)
    {
        switch (r)
        {
            case InsightRarity.Uncommon: return uncommonColor;
            case InsightRarity.Rare: return rareColor;
            case InsightRarity.Epic: return epicColor;
            default: return commonColor;
        }
    }

    // ----------------------------------------------------------------- show / hide

    void ShowStage()
    {
        stageRoot.gameObject.SetActive(true);
        stageGroup.DOKill();
        stageGroup.alpha = 0f;
        stageGroup.blocksRaycasts = true;
        stageRoot.localScale = Vector3.one * 0.97f;
        stageGroup.DOFade(1f, showDur).SetUpdate(true);
        stageRoot.DOScale(1f, showDur).SetEase(Ease.OutBack).SetUpdate(true);
    }

    void HideStage()
    {
        if (stageGroup == null) return;
        stageGroup.DOKill();
        stageGroup.blocksRaycasts = false;
        stageGroup.DOFade(0f, hideDur).SetUpdate(true)
            .OnComplete(() => { if (stageRoot) stageRoot.gameObject.SetActive(false); });
    }

    void EnsureOverlay()
    {
        if (overlayGO) return;

        overlayGO = new GameObject("RewardStage (runtime)", typeof(RectTransform));
        overlayRT = (RectTransform)overlayGO.transform;
        overlayRT.SetParent(null, false);
        Stretch(overlayRT);

        var canvas = overlayGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 960;                   // above fusion (900) / foresee (950)
        var scaler = overlayGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(800, 600);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        overlayGO.AddComponent<GraphicRaycaster>();

        stageRoot = NewRect("StageRoot", overlayRT, Vector2.zero, Vector2.one, Half());
        stageGroup = stageRoot.gameObject.AddComponent<CanvasGroup>();
        stageGroup.alpha = 0f;
        stageRoot.gameObject.SetActive(false);
    }

    // ----------------------------------------------------------------- helpers

    FusionBook Book()
    {
        if (fusionBook) return fusionBook;
        if (FusionController.Instance != null) fusionBook = FusionController.Instance.fusionBook;
        return fusionBook;
    }

    InsightDatabase DB()
    {
        if (insightDatabase) return insightDatabase;
        if (InsightHost.Instance != null) insightDatabase = InsightHost.Instance.Database;
        return insightDatabase;
    }

    void PlaySfx(AudioClip clip)
    {
        if (clip && AudioManager.Instance) AudioManager.Instance.PlaySound(clip, sfxVolume);
    }

    static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    Button MakeButton(string name, string label, Color color, Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction onClick)
    {
        var rt = NewRect(name, stageRoot, Half(), Half(), Half());
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = true;
        var t = NewText(rt, label, 24f, Color.white, Vector2.zero, size, FontStyles.Bold);
        t.raycastTarget = false;
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

    TMPro.TextMeshProUGUI NewText(Transform parent, string text, float size, Color color, Vector2 pos, Vector2 sizeDelta, FontStyles style)
    {
        var rt = NewRect("Text", parent, Half(), Half(), Half());
        rt.anchoredPosition = pos;
        rt.sizeDelta = sizeDelta;
        var t = rt.gameObject.AddComponent<TMPro.TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = TMPro.TextAlignmentOptions.Center;
        t.fontStyle = style;
        t.raycastTarget = false;
        return t;
    }

    Sprite GetRadial()
    {
        if (radialSprite) return radialSprite;
        const int s = 8;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        var px = new Color[s * s];
        for (int i = 0; i < px.Length; i++) px[i] = Color.white;
        tex.SetPixels(px); tex.Apply();
        radialSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
        return radialSprite;
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
