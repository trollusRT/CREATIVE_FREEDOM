using UnityEngine;
using System.Collections;

public class MusicManager : MonoBehaviour
{
    public static MusicManager Instance;

    [Header("Clips")]
    public AudioClip battleClip;
    public AudioClip victoryClip;
    public AudioClip defeatClip;

    [Header("Source + Defaults")]
    public AudioSource musicSource;
    [Range(0f, 1f)] public float defaultVolume = 0.85f;
    public float defaultPitch = 1f;

    Coroutine duckCo;

    void Awake()
    {
        if (Instance == null) Instance = this;
    }

    public void PlayBattleMusic()
    {
        if (!musicSource) return;
        musicSource.Stop();
        musicSource.clip = battleClip;
        musicSource.volume = defaultVolume;
        musicSource.pitch = defaultPitch;
        musicSource.loop = true;
        musicSource.Play();
    }

    public void PlayVictoryMusic()
    {
        if (!musicSource) return;
        if (duckCo != null) { StopCoroutine(duckCo); duckCo = null; }
        musicSource.Stop();
        musicSource.pitch = defaultPitch;
        musicSource.volume = defaultVolume;
        musicSource.loop = false;
        musicSource.clip = victoryClip;
        musicSource.Play();
    }

    public void PlayDefeatMusic()
    {
        if (!musicSource) return;
        if (duckCo != null) { StopCoroutine(duckCo); duckCo = null; }
        musicSource.Stop();
        musicSource.pitch = defaultPitch;
        musicSource.volume = defaultVolume;
        musicSource.loop = false;
        musicSource.clip = defeatClip;
        musicSource.Play();
    }

    /// <summary>
    /// Smoothly ramp pitch & volume down (attack), hold, optionally restore (release).
    /// Uses realtime waits so it ignores Time.timeScale.
    /// </summary>
    public void DuckPitchAndVolume(float targetPitch, float targetVolume, float attack, float hold, float release, bool restoreAtEnd)
    {
        if (duckCo != null) StopCoroutine(duckCo);
        duckCo = StartCoroutine(DuckCo(targetPitch, targetVolume, attack, hold, release, restoreAtEnd));
    }

    IEnumerator DuckCo(float targetPitch, float targetVolume, float attack, float hold, float release, bool restoreAtEnd)
    {
        if (!musicSource) yield break;

        float startPitch = musicSource.pitch;
        float startVol = musicSource.volume;

        // Attack: ramp down
        if (attack > 0f)
        {
            float t = 0f;
            while (t < attack)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / attack);
                musicSource.pitch = Mathf.Lerp(startPitch, targetPitch, k);
                musicSource.volume = Mathf.Lerp(startVol, targetVolume, k);
                yield return null;
            }
        }
        else
        {
            musicSource.pitch = targetPitch;
            musicSource.volume = targetVolume;
        }

        // Hold
        if (hold > 0f) yield return new WaitForSecondsRealtime(hold);

        // Release: restore back to defaults (unless caller will switch tracks)
        if (restoreAtEnd && release > 0f)
        {
            float endPitch = defaultPitch;
            float endVol = defaultVolume;

            float t = 0f;
            while (t < release)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / release);
                musicSource.pitch = Mathf.Lerp(targetPitch, endPitch, k);
                musicSource.volume = Mathf.Lerp(targetVolume, endVol, k);
                yield return null;
            }
            musicSource.pitch = endPitch;
            musicSource.volume = endVol;
        }

        duckCo = null;
    }
}

