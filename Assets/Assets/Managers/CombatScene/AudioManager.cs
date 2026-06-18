using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance;

    [Header("SFX")]
    [Tooltip("Random pitch jitter per SFX so repeats don't sound robotic.")]
    [Range(0f, 0.5f)] public float pitchVariation = 0.07f;
    [Tooltip("How many SFX can overlap with independent pitch before voices recycle.")]
    [SerializeField] int sfxVoices = 8;

    [Header("Ambience")]
    [Tooltip("Optional looping ambient bed; auto-plays on Start if assigned.")]
    public AudioClip ambienceClip;
    [Range(0f, 1f)] public float ambienceVolume = 0.5f;

    AudioSource template;        // original source — kept for mixer group / settings
    AudioSource[] voices;
    int voiceIndex;
    AudioSource ambienceSource;
    bool ready;

    void Awake()
    {
        // Per-scene singleton (NOT DontDestroyOnLoad). The test flow reloads the scene
        // between fights; persisting this object would make the reloaded scene's
        // AudioManager self-destroy on the duplicate guard, leaving every inspector
        // `audioManager` reference pointing at a destroyed object (silent SFX, and a
        // MissingReferenceException on the unguarded Pass-button click).
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
        EnsureReady();
    }

    void Start()
    {
        if (ambienceClip) PlayAmbience(ambienceClip, ambienceVolume);
    }

    void EnsureReady()
    {
        if (ready) return;
        ready = true;

        template = GetComponent<AudioSource>();
        if (!template) template = gameObject.AddComponent<AudioSource>();
        template.playOnAwake = false;

        int n = Mathf.Max(1, sfxVoices);
        voices = new AudioSource[n];
        for (int i = 0; i < n; i++)
        {
            var v = gameObject.AddComponent<AudioSource>();
            v.playOnAwake = false;
            v.loop = false;
            v.spatialBlend = template.spatialBlend;
            v.outputAudioMixerGroup = template.outputAudioMixerGroup;
            v.ignoreListenerPause = template.ignoreListenerPause;
            voices[i] = v;
        }

        ambienceSource = gameObject.AddComponent<AudioSource>();
        ambienceSource.playOnAwake = false;
        ambienceSource.loop = true;
        ambienceSource.spatialBlend = 0f;
        ambienceSource.outputAudioMixerGroup = template.outputAudioMixerGroup;
    }

    AudioSource NextVoice()
    {
        var v = voices[voiceIndex];
        voiceIndex = (voiceIndex + 1) % voices.Length;
        return v;
    }

    // Kept for all existing callers; now applies pitch variation via a voice pool.
    public void PlaySound(AudioClip clip) => PlaySound(clip, 1f);

    public void PlaySound(AudioClip clip, float volume)
    {
        if (!clip) return;
        EnsureReady();
        var v = NextVoice();
        v.pitch = 1f + Random.Range(-pitchVariation, pitchVariation);
        v.PlayOneShot(clip, Mathf.Clamp01(volume));
    }

    public void PlayAmbience(AudioClip clip, float volume = -1f)
    {
        if (!clip) return;
        EnsureReady();
        ambienceSource.clip = clip;
        ambienceSource.volume = (volume >= 0f) ? Mathf.Clamp01(volume) : ambienceVolume;
        ambienceSource.loop = true;
        ambienceSource.Play();
    }

    public void StopAmbience()
    {
        if (ambienceSource) ambienceSource.Stop();
    }
}
