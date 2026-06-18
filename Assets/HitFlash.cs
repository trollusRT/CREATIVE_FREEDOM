using System.Collections;
using UnityEngine;

/// <summary>
/// Brief "flash to white" on a unit's sprite(s) for hit feedback. Self-contained:
/// it builds its own material instance from the CreativeFreedom/SpriteFlash shader,
/// so no scene wiring is needed — call HitFlash.EnsureOn(go).Flash().
///
/// Safe by design: if the shader is missing or unsupported on the target platform,
/// the flash quietly does nothing (it never swaps in a broken/magenta material).
/// Runs on unscaled time so it still animates during hit-stop (Time.timeScale == 0).
/// </summary>
[DisallowMultipleComponent]
public class HitFlash : MonoBehaviour
{
    static readonly int FlashAmountId = Shader.PropertyToID("_FlashAmount");
    static readonly int FlashColorId = Shader.PropertyToID("_FlashColor");

    SpriteRenderer[] renderers;
    Material[] originalMats;
    Material flashMat;          // per-instance clone; null if shader unavailable
    Coroutine co;

    void Awake()
    {
        renderers = GetComponentsInChildren<SpriteRenderer>(true);
        originalMats = new Material[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
            originalMats[i] = renderers[i] ? renderers[i].sharedMaterial : null;

        var sh = Shader.Find("CreativeFreedom/SpriteFlash");
        if (sh != null && sh.isSupported)
            flashMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
    }

    /// <summary>Get (or add) a HitFlash on this object.</summary>
    public static HitFlash EnsureOn(GameObject go)
    {
        if (go == null) return null;
        return go.TryGetComponent(out HitFlash hf) ? hf : go.AddComponent<HitFlash>();
    }

    public void Flash() => Flash(0.07f, Color.white);

    public void Flash(float duration, Color color)
    {
        if (flashMat == null || renderers == null || renderers.Length == 0 || duration <= 0f)
            return;

        if (co != null) StopCoroutine(co);
        co = StartCoroutine(FlashCo(duration, color));
    }

    IEnumerator FlashCo(float duration, Color color)
    {
        flashMat.SetColor(FlashColorId, color);
        flashMat.SetFloat(FlashAmountId, 1f);

        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i]) renderers[i].sharedMaterial = flashMat;

        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            flashMat.SetFloat(FlashAmountId, Mathf.Clamp01(1f - t / duration));
            yield return null;
        }

        // Restore the original material(s).
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i]) renderers[i].sharedMaterial = originalMats[i];

        co = null;
    }

    void OnDestroy()
    {
        if (flashMat != null) Destroy(flashMat);
    }
}
