using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(Animator))]
public class EffectAnimatorHost : MonoBehaviour
{
    public AudioSource audioSource; // optional
    private Animator anim;
    private HashSet<string> paramNames; // trigger/param names the controller actually defines

	private System.Action pendingImpact; // For animation impact events

	// Turn pacing completion gating.
	// We keep the "next" completion (armed by the caller) separate from the one currently being watched,
	// so rapid consecutive Play() calls don't accidentally clear the new callback.
	private System.Action nextComplete;
	private System.Action runningComplete;
	private int playSerial = 0;
	private Coroutine completionCo;

    // Cache renderers to tint
    private List<SpriteRenderer> spriteRenderers;
    private List<ParticleSystem> particleSystems;

    void Awake()
    {
        anim = GetComponent<Animator>();
        if (!audioSource) audioSource = GetComponent<AudioSource>();

        // Cache the controller's parameter names so Play() can skip triggers that don't exist
        // (avoids "Parameter X does not exist" warnings and lets effects degrade gracefully).
        paramNames = new HashSet<string>();
        if (anim != null)
            foreach (var p in anim.parameters) paramNames.Add(p.name);

        spriteRenderers = new List<SpriteRenderer>(GetComponentsInChildren<SpriteRenderer>(includeInactive: true));
        particleSystems = new List<ParticleSystem>(GetComponentsInChildren<ParticleSystem>(includeInactive: true));
    }

    
    public void Play(string triggerName, AudioClip sfx = null, float volume = 1f, Color? tint = null)
{
	    // No usable trigger — empty, or the animator's controller doesn't define this parameter.
	    // Skip SetTrigger (which would log "Parameter X does not exist") and fire the armed impact +
	    // completion now, so damage / enemy Hurt / turn gating still happen even without a VFX clip.
	    if (string.IsNullOrEmpty(triggerName) || paramNames == null || !paramNames.Contains(triggerName))
	    {
	        if (sfx)
	        {
	            if (audioSource) audioSource.PlayOneShot(sfx, volume);
	            else if (AudioManager.Instance) AudioManager.Instance.PlaySound(sfx);
	        }

	        var impact = pendingImpact;
	        pendingImpact = null;
	        impact?.Invoke();

	        nextComplete?.Invoke();
	        nextComplete = null;
	        return;
	    }

    // Apply tint before the animation spawns its frames/particles
    if (tint.HasValue) ApplyTint(tint.Value);

    anim.ResetTrigger(triggerName);
    anim.SetTrigger(triggerName);

    if (sfx)
    {
        if (audioSource) audioSource.PlayOneShot(sfx, volume);
        else if (AudioManager.Instance) AudioManager.Instance.PlaySound(sfx);
    }

	    // Kick a completion watcher for turn pacing (best-effort).
	    // This does not require animation events; it polls animator state completion.
	    if (nextComplete != null)
	    {
	        // If we were already tracking a previous play, complete it now to keep counters balanced.
	        if (completionCo != null)
	        {
	            StopCoroutine(completionCo);
	            completionCo = null;
	            runningComplete?.Invoke();
	            runningComplete = null;
	        }

	        runningComplete = nextComplete;
	        nextComplete = null;

	        playSerial++;
	        completionCo = StartCoroutine(Co_WaitForCompletion(playSerial));
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



// Arm a callback to be executed when the effect animation is considered complete.
// If a new Play() happens before completion, the previous completion callback will be invoked immediately
// to avoid leaving BattleManager in a "pending" state.
public void ArmComplete(System.Action onComplete)
{
	    nextComplete = onComplete;
}

    // Animation Event hook (call this from the clip at the impact frame).
    public void Event_Impact()                      // <-- NEW
    {
        pendingImpact?.Invoke();
        pendingImpact = null;
    }


private System.Collections.IEnumerator Co_WaitForCompletion(int serialAtStart)
{
    // Wait one frame for the trigger to push the animator into its state.
    yield return null;

	    if (anim == null)
	    {
	        runningComplete?.Invoke();
	        runningComplete = null;
	        yield break;
	    }

    var startInfo = anim.GetCurrentAnimatorStateInfo(0);
    int startHash = startInfo.fullPathHash;

    // Safety timeout to prevent hanging forever if something loops.
    float timeout = 3.0f;
    float t = 0f;

    while (t < timeout)
    {
        // If another Play() happened, this watcher is obsolete.
        if (serialAtStart != playSerial) yield break;

        if (anim == null) break;

        var info = anim.GetCurrentAnimatorStateInfo(0);

        // Consider complete when the originally-entered state has finished its first pass.
        if (!anim.IsInTransition(0) && info.fullPathHash == startHash && info.normalizedTime >= 1f)
            break;

        t += Time.unscaledDeltaTime;
        yield return null;
    }

    if (serialAtStart != playSerial) yield break;

	    runningComplete?.Invoke();
	    runningComplete = null;
}

}
