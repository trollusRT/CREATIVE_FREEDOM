using UnityEngine;
using System.Collections;

public class SlowMoController : MonoBehaviour
{
    public static SlowMoController Instance;

    void Awake()
    {
        if (Instance == null) Instance = this;
    }

    /// <summary>
    /// Freeze briefly, then run at slowScale for slowDuration, then ease back to 1x in restoreDuration.
    /// Uses realtime waits (unaffected by timeScale).
    /// </summary>
    public Coroutine Pulse(float freeze = 0.06f, float slowScale = 0.2f, float slowDuration = 0.35f, float restoreDuration = 0.15f)
    {
        return StartCoroutine(PulseCo(freeze, slowScale, slowDuration, restoreDuration));
    }

    public IEnumerator PulseCo(float freeze, float slowScale, float slowDuration, float restoreDuration)
    {
        float prevScale = Time.timeScale;
        float prevFixed = Time.fixedDeltaTime;

        // 1) Freeze frame
        Time.timeScale = 0f;
        // Don’t bother adjusting fixedDeltaTime for true freeze
        yield return new WaitForSecondsRealtime(freeze);

        // 2) Slow motion
        Time.timeScale = slowScale;
        Time.fixedDeltaTime = prevFixed * slowScale;
        yield return new WaitForSecondsRealtime(slowDuration);

        // 3) Smooth restore
        float t = 0f;
        while (t < restoreDuration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / restoreDuration);
            Time.timeScale = Mathf.Lerp(slowScale, 1f, k);
            Time.fixedDeltaTime = prevFixed * Time.timeScale;
            yield return null;
        }

        Time.timeScale = 1f;
        Time.fixedDeltaTime = prevFixed;
    }
}
