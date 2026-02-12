using UnityEngine;
using System.Collections;

public class CanvasShaker : MonoBehaviour
{
    private RectTransform rt;
    private Coroutine co;
    private Vector2 basePos;

    void Awake()
    {
        rt = (RectTransform)transform;
        basePos = rt.anchoredPosition;
    }

    public void Shake(float amplitudePx = 16f, float duration = 0.15f)
    {
        if (co != null) StopCoroutine(co);
        co = StartCoroutine(ShakeCo(amplitudePx, duration));
    }

    IEnumerator ShakeCo(float amp, float dur)
    {
        float t = 0f;
        basePos = rt.anchoredPosition;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = 1f - (t / dur); // fade out
            Vector2 offs = new Vector2(
                Random.Range(-amp, amp) * k,
                Random.Range(-amp, amp) * k
            );
            rt.anchoredPosition = basePos + offs;
            yield return null;
        }
        rt.anchoredPosition = basePos;
        co = null;
    }
}
