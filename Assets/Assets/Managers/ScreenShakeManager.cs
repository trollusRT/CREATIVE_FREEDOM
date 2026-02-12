using UnityEngine;
using DG.Tweening;

public class CameraShakeManager : MonoBehaviour
{
    public static CameraShakeManager Instance;

    [Header("Targets (do NOT include HUD camera)")]
    public Transform mainCameraTarget;   // usually Camera.main.transform
    public Transform fxCameraTarget;     // your FX Camera transform (if you have one)

    [Header("Defaults")]
    public float defaultDuration = 0.20f;
    public float defaultStrength = 0.35f;   // world units (tune for your ortho size)
    public int defaultVibrato = 18;      // how many shakes
    public float defaultRandomness = 90f;
    public bool fadeOut = true;

    void Awake()
    {
        if (Instance == null) Instance = this;
    }

    public void Shake(float strength = -1f, float duration = -1f, int vibrato = -1, float randomness = -1f)
    {
        float s = (strength > 0f) ? strength : defaultStrength;
        float d = (duration > 0f) ? duration : defaultDuration;
        int v = (vibrato > 0) ? vibrato : defaultVibrato;
        float r = (randomness >= 0f) ? randomness : defaultRandomness;

        ShakeTarget(mainCameraTarget, s, d, v, r);
        ShakeTarget(fxCameraTarget, s, d, v, r); // so VFX bloom layer shakes too
    }

    private void ShakeTarget(Transform t, float strength, float duration, int vibrato, float randomness)
    {
        if (!t) return;
        // Finish any existing shake and reset before starting a new one
        t.DOKill(complete: true);
        t.DOShakePosition(duration, strength, vibrato, randomness, snapping: false, fadeOut: fadeOut);
    }
}
