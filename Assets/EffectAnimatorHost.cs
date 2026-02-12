using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(Animator))]
public class EffectAnimatorHost : MonoBehaviour
{
    public AudioSource audioSource; // optional
    private Animator anim;

    private System.Action pendingImpact; //For animation impact events

    // Cache renderers to tint
    private List<SpriteRenderer> spriteRenderers;
    private List<ParticleSystem> particleSystems;

    void Awake()
    {
        anim = GetComponent<Animator>();
        if (!audioSource) audioSource = GetComponent<AudioSource>();

        spriteRenderers = new List<SpriteRenderer>(GetComponentsInChildren<SpriteRenderer>(includeInactive: true));
        particleSystems = new List<ParticleSystem>(GetComponentsInChildren<ParticleSystem>(includeInactive: true));
    }

    public void Play(string triggerName, AudioClip sfx = null, float volume = 1f, Color? tint = null)
    {
        if (string.IsNullOrEmpty(triggerName)) return;

        // Apply tint before the animation spawns its frames/particles
        if (tint.HasValue) ApplyTint(tint.Value);

        anim.ResetTrigger(triggerName);
        anim.SetTrigger(triggerName);

        if (sfx)
        {
            if (audioSource) audioSource.PlayOneShot(sfx, volume);
            else if (AudioManager.Instance) AudioManager.Instance.PlaySound(sfx);
        }
    }

    private void ApplyTint(Color c)
    {
        // Sprites
        for (int i = 0; i < spriteRenderers.Count; i++)
        {
            if (spriteRenderers[i]) spriteRenderers[i].color = c;
        }

        // Particles
        for (int i = 0; i < particleSystems.Count; i++)
        {
            if (!particleSystems[i]) continue;
            var main = particleSystems[i].main;
            main.startColor = c;
        }
    }

    // Put these inside your EffectAnimatorHost class

    // 0) No-arg: uses CameraShakeManager defaults
    public void Event_Shake()
    {
        if (CameraShakeManager.Instance)
            CameraShakeManager.Instance.Shake();
    }

    // 1) One float param = strength only (duration/vibrato/randomness from defaults)
    public void Event_ShakeStrength(float strength)
    {
        if (CameraShakeManager.Instance)
            CameraShakeManager.Instance.Shake(strength: strength);
    }

    // 2) String payload: "strength,duration,vibrato,randomness"
    // Example: "0.45,0.22,22,90"  (commas or semicolons both OK)
    public void Event_ShakeArgs(string payload)
    {
        if (CameraShakeManager.Instance == null) return;

        // Defaults from the manager (so you can omit fields)
        float strength = CameraShakeManager.Instance.defaultStrength;
        float duration = CameraShakeManager.Instance.defaultDuration;
        int vibrato = CameraShakeManager.Instance.defaultVibrato;
        float random = CameraShakeManager.Instance.defaultRandomness;

        if (!string.IsNullOrEmpty(payload))
        {
            // allow commas or semicolons
            var parts = payload.Split(new char[] { ',', ';' }, System.StringSplitOptions.RemoveEmptyEntries);
            float f;
            int i;

            if (parts.Length > 0 && float.TryParse(parts[0], out f)) strength = f;
            if (parts.Length > 1 && float.TryParse(parts[1], out f)) duration = f;
            if (parts.Length > 2 && int.TryParse(parts[2], out i)) vibrato = i;
            if (parts.Length > 3 && float.TryParse(parts[3], out f)) random = f;
        }

        CameraShakeManager.Instance.Shake(strength, duration, vibrato, random);
    }

    // Arm a callback to be executed by the animation event.
    public void ArmImpact(System.Action onImpact)   // <-- NEW
    {
        pendingImpact = onImpact;
    }

    // Animation Event hook (call this from the clip at the impact frame).
    public void Event_Impact()                      // <-- NEW
    {
        pendingImpact?.Invoke();
        pendingImpact = null;
    }

}
