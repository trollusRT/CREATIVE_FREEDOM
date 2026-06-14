using UnityEngine;
using System.Collections.Generic;

// Targeting highlight that tints the enemy's OWN sprite(s), so it always stays in sync
// with the character animation underneath. (The old version toggled a separate overlay
// child whose animation drifted from the character.) Eases in/out and breathes while
// active for responsiveness. Public API SetHighlighted(bool) is unchanged.
public class TargetHighlighter : MonoBehaviour
{
    [Header("Highlight look")]
    [Tooltip("Multiply tint applied while targeted (warm gold by default).")]
    public Color highlightTint = new Color(1f, 0.9f, 0.5f, 1f);
    [Tooltip("How fast the tint eases in/out.")]
    public float fadeSpeed = 10f;
    [Tooltip("Breathing pulse speed while highlighted.")]
    public float pulseSpeed = 6f;
    [Range(0f, 1f)] public float pulseDepth = 0.35f;

    [Header("Targets")]
    [Tooltip("Sprites to tint. Auto-filled from children (excluding the legacy overlay) if left empty.")]
    public SpriteRenderer[] renderers;

    [Tooltip("Legacy overlay; auto-hidden on play since the tint replaces it. Safe to delete.")]
    public GameObject highlightGO;

    Color[] baseColors;
    float amount;       // 0..1 eased highlight strength
    bool highlighted;
    bool applied;

    void Awake()
    {
        if (highlightGO) highlightGO.SetActive(false);

        if (renderers == null || renderers.Length == 0)
        {
            var found = GetComponentsInChildren<SpriteRenderer>(true);
            var list = new List<SpriteRenderer>(found.Length);
            foreach (var r in found)
            {
                if (!r) continue;
                if (highlightGO && r.transform.IsChildOf(highlightGO.transform)) continue;
                list.Add(r);
            }
            renderers = list.ToArray();
        }

        baseColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
            baseColors[i] = renderers[i] ? renderers[i].color : Color.white;
    }

    public void SetHighlighted(bool on)
    {
        highlighted = on;
    }

    void Update()
    {
        float target = highlighted ? 1f : 0f;
        amount = Mathf.MoveTowards(amount, target, Time.unscaledDeltaTime * fadeSpeed);

        if (amount <= 0.001f)
        {
            if (applied) { Restore(); applied = false; }
            return;
        }

        applied = true;

        // Breathe between (1 - pulseDepth) and 1 of full strength.
        float pulse = 1f - pulseDepth * (0.5f - 0.5f * Mathf.Cos(Time.unscaledTime * pulseSpeed));
        float k = Mathf.Clamp01(amount * pulse);

        for (int i = 0; i < renderers.Length; i++)
        {
            if (!renderers[i]) continue;
            Color tinted = baseColors[i] * highlightTint;
            Color c = Color.Lerp(baseColors[i], tinted, k);
            c.a = baseColors[i].a;
            renderers[i].color = c;
        }
    }

    void Restore()
    {
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i]) renderers[i].color = baseColors[i];
    }

    void OnDisable()
    {
        if (applied) { Restore(); applied = false; }
    }
}
