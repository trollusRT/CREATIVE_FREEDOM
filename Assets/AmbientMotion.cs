using UnityEngine;

/// <summary>
/// Subtle, looping transform motion layered on top of an object's normal (idle) animation.
/// Drop it on characters, enemies, or background/parallax layers and tune in the Inspector.
/// Works on both world-space (SpriteRenderer) objects and UI RectTransforms.
/// </summary>
[DisallowMultipleComponent]
public class AmbientMotion : MonoBehaviour
{
    [Header("Vertical bob")]
    [Tooltip("Peak offset, in the object's local units (small for sprites, larger for UI).")]
    public float bobAmplitude = 0.05f;
    public float bobSpeed = 1.2f;

    [Header("Horizontal sway")]
    public float swayAmplitude = 0.02f;
    public float swaySpeed = 0.7f;

    [Header("Rotation sway (degrees)")]
    public float rotateAmplitude = 0f;
    public float rotateSpeed = 0.5f;

    [Header("Options")]
    [Tooltip("Use unscaled time so motion continues during hit-stop / pauses.")]
    public bool useUnscaledTime = false;
    [Tooltip("Randomize the start phase so multiple objects don't move in lockstep.")]
    public bool randomizePhase = true;

    Vector3 baseLocalPos;
    Quaternion baseLocalRot;
    float phase;

    void OnEnable()
    {
        baseLocalPos = transform.localPosition;
        baseLocalRot = transform.localRotation;
        phase = randomizePhase ? Random.Range(0f, Mathf.PI * 2f) : 0f;
    }

    void Update()
    {
        float t = (useUnscaledTime ? Time.unscaledTime : Time.time) + phase;

        float y = Mathf.Sin(t * bobSpeed) * bobAmplitude;
        float x = Mathf.Sin(t * swaySpeed) * swayAmplitude;
        transform.localPosition = baseLocalPos + new Vector3(x, y, 0f);

        if (!Mathf.Approximately(rotateAmplitude, 0f))
        {
            float r = Mathf.Sin(t * rotateSpeed) * rotateAmplitude;
            transform.localRotation = baseLocalRot * Quaternion.Euler(0f, 0f, r);
        }
    }

    void OnDisable()
    {
        // Restore the base transform so toggling the component doesn't leave it drifted.
        transform.localPosition = baseLocalPos;
        transform.localRotation = baseLocalRot;
    }
}
