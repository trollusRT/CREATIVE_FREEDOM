using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public enum VfxType { Slash, PaintSplash, HealBurst, BuffGlow, DebuffCrackle, PoisonTick, StunStars }

public class CombatVFXManager : MonoBehaviour
{
    public static CombatVFXManager Instance;

    [System.Serializable]
    public class VfxEntry { public VfxType type; public GameObject prefab; public int poolSize = 4; }

    public List<VfxEntry> entries = new();
    public Transform playerVfxAnchor;
    public Transform enemiesVfxAnchor; // or one per-enemy if you prefer
    public float cameraShakeAmt = 0.15f;
    public float cameraShakeDur = 0.15f;

    Dictionary<VfxType, Queue<GameObject>> pools = new();

    void Awake()
    {
        if (Instance == null) Instance = this;
        foreach (var e in entries)
        {
            var q = new Queue<GameObject>();
            for (int i = 0; i < e.poolSize; i++)
            {
                var go = Instantiate(e.prefab, transform);
                go.SetActive(false);
                q.Enqueue(go);
            }
            pools[e.type] = q;
        }
    }

    public void PlayOnPlayer(VfxType type) => Play(type, playerVfxAnchor.position);
    public void PlayOnEnemy(VfxType type, Vector3 worldPos) => Play(type, worldPos);

    void Play(VfxType type, Vector3 pos)
    {
        if (!pools.TryGetValue(type, out var q) || q.Count == 0) return;
        var go = q.Dequeue();
        go.transform.position = pos;
        go.SetActive(true);
        StartCoroutine(ReturnToPool(type, go, 1.0f));
    }

    IEnumerator ReturnToPool(VfxType type, GameObject go, float after)
    {
        yield return new WaitForSeconds(after);
        go.SetActive(false);
        pools[type].Enqueue(go);
    }

    public void ShakeCamera()
    {
        // simple local shake on main camera
        var cam = Camera.main.transform;
        StopAllCoroutines();
        StartCoroutine(ShakeCo(cam));
    }
    IEnumerator ShakeCo(Transform t)
    {
        Vector3 basePos = t.localPosition;
        float tmr = 0f;
        while (tmr < cameraShakeDur)
        {
            tmr += Time.deltaTime;
            t.localPosition = basePos + (Vector3)Random.insideUnitCircle * cameraShakeAmt;
            yield return null;
        }
        t.localPosition = basePos;
    }

    // --- Hit-stop: a brief global freeze on impact for "weight". ---
    [Header("Hit-stop")]
    public bool allowHitStop = true;
    bool hitStopActive;

    public void HitStop(float duration)
    {
        if (!allowHitStop || hitStopActive || duration <= 0f) return;
        StartCoroutine(HitStopCo(duration));
    }

    IEnumerator HitStopCo(float duration)
    {
        hitStopActive = true;
        Time.timeScale = 0f;
        yield return new WaitForSecondsRealtime(duration);
        Time.timeScale = 1f;
        hitStopActive = false;
    }
}

