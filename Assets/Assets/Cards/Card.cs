using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using DG.Tweening; // top of Card.cs
using System.Collections; // for IEnumerator (coroutines)
using extOSC; //OSC library




public class Card : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{

    [SerializeField] private LayerMask targetMask = ~0;
    private TargetHighlighter currentHighlight;           // single-target
    private readonly List<TargetHighlighter> aoeHighlights = new();


    [Header("Data Reference")]
    public CardData cardData;

    [Header("UI References")]
    public Button cardButton;
    public Image cardArtwork;

    // Dangle (tilt) settings
    [Header("Dangle")]
    public RectTransform visualRoot;     // assign in prefab
    public bool dangle = true;
    public float maxTilt = 15f;          // degrees
    public float tiltSensitivity = 0.06f;// deg per px/s
    public float spring = 40f;           // higher = snappier
    public float damping = 8f;           // higher = less overshoot

    // internal state
    private float dangleAngle = 0f;
    private float dangleVel = 0f;
    private Vector2 lastMousePos;
    private bool dragging = false;

    [Header("Drag Settings")]
    public bool isDraggable = true;
    public HandManager handManager;
    public Transform dragLayer;  // Assign a top-level UI object under the same Canvas

    public Animator playerSpellAnimator;
    public Animator fieldSpellAnimator;

    // Original transform data to restore
    private Transform originalParent;
    private Vector2 originalPosition;
    private Vector3 originalScale;   // <-- keep only this one
    private int originalIndexInHand;
    private Vector2 dragOffset;

    // CanvasGroup for drag behavior
    private CanvasGroup cg;
    [SerializeField] private float dragScale = 1.1f;  // show in Inspector, not public

    // ---------------------------------------------------------
    // OSC / VCV Rack
    // ---------------------------------------------------------
    [Header("OSC")]
    public bool enableOsc = true;
    public OSCTransmitter oscTransmitter;

    [Tooltip("Color used for OSC pitch mapping")]
    public OscCardColor oscColor = OscCardColor.Purple;

    [SerializeField]
    private float oscGatePulseDuration = 0.1f;

    public enum OscCardColor
    {
        Purple,
        Red,
        Orange,
        Yellow,
        Green,
        Blue
    }

    // --- Hover (rollover) settings ---
    [Header("Hover")]
    [SerializeField] float hoverLift = 28f;
    [SerializeField] float hoverDuration = 0.12f;
    [SerializeField] Ease hoverEase = Ease.OutQuad;

    private bool isHovered;
    private Vector2 vrPreHoverPos;   // <-- store the visualRoot's original pos
    private Tweener hoverPosTw;
    private int hoverOriginalSiblingIndex = -1;




    private Coroutine hoverExitCo;

    private bool pointerDown;
    private Vector2 pointerDownPos;
    [SerializeField] float clickMaxMove = 8f; // pixels allowed to still count as a click


    [Header("Hover SFX")]
    [SerializeField] AudioClip hoverSfx;
    [SerializeField] float hoverSfxVolume = 1f;

    // hover reparenting state
    private Transform hoverOriginalParent;
    private Vector2 hoverOriginalAnchoredPos;   // in original parent space
    private bool hoverLiftedToDragLayer;
    // at top of Card
    private Vector3 baseScale;   // never overwrite after Start()

    // --- alpha/visibility guards ---
    private bool fusionSelected = false;   // true only while selected for fusion

    // --- turn pacing gating helpers ---
    private IEnumerator Co_AutoCompleteEffectGate(System.Action complete)
    {
        // Allow one frame so the visual Play() can at least fire before we release the gate.
        yield return null;
        complete?.Invoke();
    }

    private void BeginEffectGate(EffectAnimatorHost host)
    {
        if (BattleManager.Instance == null) return;
        if (!BattleManager.Instance.gateEnemyTurnOnPendingEffects) return;

        BattleManager.Instance.NotifyEffectStarted();
        bool completed = false;
        System.Action complete = () =>
        {
            if (completed) return;
            completed = true;
            BattleManager.Instance.NotifyEffectCompleted();
        };

        if (host != null)
            host.ArmComplete(complete);
        else
            StartCoroutine(Co_AutoCompleteEffectGate(complete));
    }



    void Start()
    {
        UpdateCardVisuals();

        baseScale = transform.localScale;   // <- only here
        originalScale = baseScale;          // if other code still reads originalScale

        EnsureCanvasGroup();
        // ensure sane defaults at boot
        cg.alpha = fusionSelected ? 0.6f : 1f;
        cg.interactable = true;
        cg.blocksRaycasts = true;
    }



    public void UpdateCardVisuals()
    {
        if (cardArtwork != null && cardData.cardSprite != null)
        {
            cardArtwork.sprite = cardData.cardSprite;
        }
        // If you also have text fields for cost/name, update them here.
    }

    // Inside Card class (same level as ApplyCardEffectToEnemy / OnEndDrag)
    private void ApplyCardEffectToPlayer(Player player)
    {
        if (BattleManager.Instance.playerAP < cardData.cardCost)
        {
            Debug.LogWarning($"Not enough AP to play {cardData.cardName}!");
            ReturnToHand();
            return;
        }

        BattleManager.Instance.UseAP(cardData.cardCost);

        switch (cardData.cardName)
        {
            case "Restore":
                {
                    var tint = EffectDirector.Instance.ResolveTypeColor(cardData.cardType, Color.white);
                    var _host = EffectDirector.Instance ? EffectDirector.Instance.playerSingleTargetHost : null;
                    BeginEffectGate(_host);
                    EffectDirector.Instance.PlayPlayerHit(EffectKey.Restore, cardData.sfx, cardData.sfxVolume, tint);

                    player.Heal(Random.Range(cardData.minValue, cardData.maxValue + 1));
                }
                break;

            case "Rejuvenate":
                {
                    var tint = EffectDirector.Instance.ResolveTypeColor(cardData.cardType, Color.white);
                    var _host = EffectDirector.Instance ? EffectDirector.Instance.playerSingleTargetHost : null;
                    BeginEffectGate(_host);
                    EffectDirector.Instance.PlayPlayerHit(EffectKey.Rejuvenate, cardData.sfx, cardData.sfxVolume, tint);

                    player.Heal(cardData.minValue);
                }
                break;

            case "Shield":
                {
                    player.animator.SetTrigger("Buff");
                    player.activeEffects.Add(new StatusEffect { type = StatusType.Shield, duration = 2 });
                }
                break;

            // add your other self-target cases here...

            default:
                Debug.Log($"{cardData.cardName} not implemented for player!");
                break;
        }

        player.UpdateHPText();
        Destroy(gameObject);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        pointerDown = true;
        pointerDownPos = eventData.position;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!pointerDown) return;
        pointerDown = false;

        // treat as click if we didn't actually drag far
        if ((eventData.position - pointerDownPos).sqrMagnitude <= clickMaxMove * clickMaxMove)
        {
            TryFusionClick();
        }
    }

    // keep your existing OnPointerClick if you want, but this is more reliable with hover
    private void TryFusionClick()
    {
        if (dragging) return; // real drag happened
        if (BattleManager.Instance == null ||
            BattleManager.Instance.state != BattleManager.BattleState.PLAYER_TURN)
            return;

        if (FusionController.Instance != null)
            FusionController.Instance.TrySelectCard(this);
    }


    private void ApplyCardEffectToEnemy(Enemy enemy)
    {
        // local guard
        bool IsAlive(Enemy e) => e != null && !e.IsDead && e.GetHP() > 0;

        // Do not spend AP on dead/invalid targets
        if (!IsAlive(enemy))
        {
            ReturnToHand();
            return;
        }

        // Enough AP?
        if (BattleManager.Instance.playerAP < cardData.cardCost)
        {
            Debug.LogWarning($"Not enough AP to play {cardData.cardName}!");
            ReturnToHand();
            return;
        }

        // Player swing (only for attack-y cards)
        if (ShouldTriggerAttackSwing())
            TriggerPlayerSwing();

        // Spend AP now (visual still plays, effects will be armed to the impact frame)
        BattleManager.Instance.UseAP(cardData.cardCost);

        // Helpers
        var dir = EffectDirector.Instance;

        // SINGLE-TARGET: arm impact on the target's host, then PlayEnemyHit
        void STWithImpact(Enemy target, EffectKey key, System.Action onImpact)
        {
            var host = dir.GetEnemyHost(target);
            if (host != null)
            {
                host.ArmImpact(() =>
                {
                    onImpact?.Invoke();
                    BattleManager.Instance.CheckVictoryImmediate();
                });
            }

            BeginEffectGate(host);
            var tint = dir.ResolveTypeColor(cardData.cardType, Color.white);
            dir.PlayEnemyHit(target, key, cardData.sfx, cardData.sfxVolume, tint);
        }

        // AOE: arm impact on the central AoE host, then PlayAoe
        void AOEWithImpact(EffectKey key, System.Action onImpact)
        {
            var host = dir.aoeHost;
            if (host != null)
            {
                host.ArmImpact(() =>
                {
                    onImpact?.Invoke();
                    BattleManager.Instance.CheckVictoryImmediate();
                });
            }

            BeginEffectGate(host);
            var tint = dir.ResolveTypeColor(cardData.cardType, Color.white);
            dir.PlayAoe(key, cardData.sfx, cardData.sfxVolume, tint);
        }


        switch (cardData.cardName)
        {
            // ----- RED -----
            case "Red Stroke":
                {
                    int dmg = Random.Range(cardData.minValue, cardData.maxValue + 1);
                    STWithImpact(enemy, EffectKey.RedStroke, () => enemy.TakeDamage(dmg));
                }
                break;

            case "Siphon":
                {
                    int dmg = Random.Range(cardData.minValue, cardData.maxValue + 1);
                    STWithImpact(enemy, EffectKey.Siphon, () =>
                    {
                        enemy.TakeDamage(dmg);
                        BattleManager.Instance.player.Heal(dmg);
                    });
                }
                break;

            // ----- CRIMSON -----
            case "Red Splatter":
                {
                    AOEWithImpact(EffectKey.RedSplatter, () =>
                    {
                        var snapshot = new List<Enemy>(BattleManager.Instance.enemies);
                        foreach (var e in snapshot)
                        {
                            if (!IsAlive(e)) continue;
                            int dmg = Random.Range(5, 7); // 5-6
                            e.TakeDamage(dmg);
                        }
                    });
                }
                break;

            case "Reckless Stroke":
                {
                    float hpRatio = (float)BattleManager.Instance.player.currentHP / BattleManager.Instance.player.maxHP;
                    float mult = (hpRatio < 0.25f) ? 4f : (hpRatio < 0.5f ? 2f : 1f);
                    int baseDmg = Random.Range(cardData.minValue, cardData.maxValue + 1);
                    int finalDmg = Mathf.RoundToInt(baseDmg * mult);

                    STWithImpact(enemy, EffectKey.RecklessStroke, () => enemy.TakeDamage(finalDmg));
                }
                break;

            case "Finishing Touch":
                {
                    int finalDmg = (enemy.currentHP < 30) ? enemy.currentHP : 30;
                    STWithImpact(enemy, EffectKey.FinishingTouch, () => enemy.TakeDamage(finalDmg));
                }
                break;

            // ----- BLUE -----
            case "Attack Break":
                {
                    int dmg = Random.Range(cardData.minValue, cardData.maxValue + 1);
                    STWithImpact(enemy, EffectKey.AttackBreak, () =>
                    {
                        enemy.TakeDamage(dmg);
                        enemy.activeEffects.Add(new StatusEffect { type = StatusType.AttackBreak, power = 2, duration = 2 });
                    });
                }
                break;

            // ----- ULTRAMARINE -----
            case "Defensive Stance":
                {
                    int dmg = Random.Range(cardData.minValue, cardData.maxValue + 1);
                    STWithImpact(enemy, EffectKey.AttackBreak /* or your own DefensiveStance key if you add one */, () =>
                    {
                        enemy.activeEffects.Add(new StatusEffect { type = StatusType.DefensiveStance, duration = 2 });
                        enemy.TakeDamage(dmg);
                    });
                }
                break;

            // ----- YELLOW -----
            case "Yellow Spray":
            case "Y-Spray":
                {
                    AOEWithImpact(EffectKey.YSpray, () =>
                    {
                        var snapshot = new List<Enemy>(BattleManager.Instance.enemies);
                        foreach (var e in snapshot)
                        {
                            if (!IsAlive(e)) continue;
                            int dmg = Random.Range(2, 5); // 2-4
                            e.TakeDamage(dmg);
                        }
                    });
                }
                break;

            case "Poison":
                {
                    int poisonPerTurn = Random.Range(1, 3); // 1-2
                    enemy.ApplyPoison(poisonPerTurn, 3);
                }
                break;

            case "Sleep":
                {
                    STWithImpact(enemy, EffectKey.Sleep, () => enemy.ApplySleep(1));
                }
                break;

            case "Corrode":
                {
                    int dmg = Random.Range(cardData.minValue, cardData.maxValue + 1);
                    STWithImpact(enemy, EffectKey.Corrode, () =>
                    {
                        enemy.TakeDamage(dmg);
                        enemy.activeEffects.Add(new StatusEffect { type = StatusType.Corrode, duration = 2 });
                    });
                }
                break;

            // ----- GOLDEN -----
            case "Toxic Paint":
                {
                    int poisonPerTurn = Random.Range(3, 7); // 2-3
                    enemy.ApplyPoison(poisonPerTurn, 3);
                }
                break;

            case "Pool of Paint":
                {
                    // roll once so both poison & regen are consistent
                    int poisonPerTurn = Random.Range(2, 4); // 2-3
                    int healPerTurn = Random.Range(1, 4); // 1-3

                    // Use the mid-screen animator but apply ST effects at the animation's impact frame
                    AOEWithImpact(EffectKey.PoolOfPaint, () =>
                    {
                        // Enemy: poison over time
                        enemy.ApplyPoison(poisonPerTurn, 3);

                        // Player: regen over time
                        BattleManager.Instance.player.activeEffects.Add(new StatusEffect
                        {
                            type = StatusType.Regen,   // see step 2 below
                            power = healPerTurn,
                            duration = 3
                        });

                        BattleManager.Instance.player.UpdateHPText(); // optional UI refresh
                    });
                }
                break;

            // ----- PURPLE -----
            case "Imaginary Paint":
                {
                    int dmg = cardData.minValue; // fixed nuke
                                                 // If you visually treat it as an AoE blast, keep AoE host; else use STWithImpact
                    AOEWithImpact(EffectKey.ImaginaryPaint, () => enemy.TakeDamage(dmg));
                }
                break;

            // ----- ORANGE -----
            case "Crushing Paint":
                {
                    int dmg = cardData.minValue;
                    STWithImpact(enemy, EffectKey.CrushingPaint, () =>
                    {
                        enemy.TakeDamage(dmg);
                        enemy.activeEffects.Add(new StatusEffect { type = StatusType.Stun, duration = 2 });
                    });
                }
                break;

            default:
                Debug.Log($"{cardData.cardName} not implemented for enemy!");
                break;
        }

        // Destroy the card right away; the armed impact will still fire from the effect host.
        Destroy(gameObject);
    }

    // ---------------------------------------------------------
    // HOVER EFFECTS
    // ---------------------------------------------------------
    public void OnPointerEnter(PointerEventData eventData)
    {

        if (hoverExitCo != null) { StopCoroutine(hoverExitCo); hoverExitCo = null; }
        if (dragging || !isDraggable) return;
        ForceVisibleIfNotFusionLocked();
        if (!visualRoot) visualRoot = transform as RectTransform; // safety

        // bring whole card in front (doesn't move hitbox)
        hoverOriginalSiblingIndex = transform.GetSiblingIndex();
        transform.SetAsLastSibling();

        // SFX
        var am = BattleManager.Instance ? BattleManager.Instance.audioManager : null;
        if (hoverSfx && am != null) am.PlaySound(hoverSfx);

        // position tween on visual only
        var vr = (RectTransform)visualRoot;
        vrPreHoverPos = vr.anchoredPosition;

        hoverPosTw?.Kill();
        hoverPosTw = vr.DOAnchorPosY(vrPreHoverPos.y + hoverLift, hoverDuration).SetEase(hoverEase);

        isHovered = true;

        // ---- OSC hover pulse ----
        if (enableOsc &&
            oscTransmitter != null &&
            BattleManager.Instance != null &&
            BattleManager.Instance.state == BattleManager.BattleState.PLAYER_TURN)
        {
            StartCoroutine(OscHoverPulseCo());
        }
    }






    public void OnPointerExit(PointerEventData eventData)
    {
        if (!isHovered) return;

        // Don't immediately drop hover: when the card visually moves on hover-lift,
        // the pointer can "exit" for a frame due to UI raycast target drift.
        // Confirm on the next frame whether the pointer is truly no longer over this card.
        if (hoverExitCo != null) StopCoroutine(hoverExitCo);
        hoverExitCo = StartCoroutine(Co_ConfirmHoverExit());
    }

    private IEnumerator Co_ConfirmHoverExit()
    {
        yield return null; // wait one frame for UI raycasts to stabilize

        if (EventSystem.current == null)
        {
            DoUnhover();
            yield break;
        }

        var ped = new PointerEventData(EventSystem.current)
        {
            position = Input.mousePosition
        };

        var results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(ped, results);

        bool stillOverThisCard = false;
        for (int i = 0; i < results.Count; i++)
        {
            var go = results[i].gameObject;
            if (go == null) continue;

            if (go == gameObject || go.transform.IsChildOf(transform))
            {
                stillOverThisCard = true;
                break;
            }
        }

        if (!stillOverThisCard)
            DoUnhover();

        hoverExitCo = null;
    }

    private void DoUnhover()
    {
        ForceVisibleIfNotFusionLocked();
        if (!visualRoot) visualRoot = transform as RectTransform;

        var vr = (RectTransform)visualRoot;
        hoverPosTw?.Kill();
        hoverPosTw = vr.DOAnchorPos(vrPreHoverPos, hoverDuration).SetEase(hoverEase);

        if (hoverOriginalSiblingIndex >= 0)
            transform.SetSiblingIndex(hoverOriginalSiblingIndex);
        hoverOriginalSiblingIndex = -1;

        isHovered = false;
    }




    private void ResetHoverInstant()
    {

        if (hoverExitCo != null) { StopCoroutine(hoverExitCo); hoverExitCo = null; }
        if (!isHovered) return;
        if (!visualRoot) visualRoot = transform as RectTransform;

        var vr = (RectTransform)visualRoot;
        hoverPosTw?.Kill();
        vr.anchoredPosition = vrPreHoverPos;

        if (hoverOriginalSiblingIndex >= 0)
            transform.SetSiblingIndex(hoverOriginalSiblingIndex);
        hoverOriginalSiblingIndex = -1;

        isHovered = false;
    }



    // ---------------------------------------------------------
    // DRAG & DROP IMPLEMENTATION
    // ---------------------------------------------------------
    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!isDraggable) return;

        ResetHoverInstant();        // already in your code, good
        hoverPosTw?.Kill();

        var canvas = GetComponentInParent<Canvas>();
        Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

        originalParent = transform.parent;
        originalPosition = ((RectTransform)transform).anchoredPosition;
        // REMOVE THIS LINE if you still have it:
        // originalScale = transform.localScale;

        if (handManager != null)
            originalIndexInHand = handManager.RemoveCard(gameObject);

        transform.SetParent(dragLayer, false);
        transform.SetAsLastSibling();

        if (cg) cg.blocksRaycasts = false;

        // always start drag from baseScale, then apply dragScale
        transform.localScale = baseScale * dragScale;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            transform.parent as RectTransform, eventData.position, uiCamera, out Vector2 localMousePos
        );
        dragOffset = ((RectTransform)transform).anchoredPosition - localMousePos;

        dragging = true;
        lastMousePos = eventData.position;
        dangleAngle = 0f;
        dangleVel = 0f;
        if (visualRoot) visualRoot.localRotation = Quaternion.identity;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // ignore clicks that are actually part of a drag
        if (dragging) return;

        // only allow during player's turn
        if (BattleManager.Instance == null ||
            BattleManager.Instance.state != BattleManager.BattleState.PLAYER_TURN)
            return;

        // hand off to your fusion controller / HUD
        if (FusionController.Instance != null)
        {
            FusionController.Instance.TrySelectCard(this);
        }
    }


    public void OnDrag(PointerEventData eventData)
    {
        if (!isDraggable) return;

        var rtParent = transform.parent as RectTransform;
        var canvas = GetComponentInParent<Canvas>();
        Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(rtParent, eventData.position, uiCamera, out Vector2 localMousePos);
        (transform as RectTransform).anchoredPosition = localMousePos + dragOffset;

        // --- Dangle simulate (spring towards a tilt based on cursor velocity)
        if (dangle && visualRoot)
        {
            // cursor velocity in px/sec
            Vector2 v = (eventData.position - lastMousePos) / Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            lastMousePos = eventData.position;

            // target angle from horizontal velocity (left/right swing)
            float targetAngle = Mathf.Clamp(-v.x * tiltSensitivity, -maxTilt, maxTilt);

            // spring-damper
            float dt = Time.unscaledDeltaTime;
            float force = spring * (targetAngle - dangleAngle) - damping * dangleVel;
            dangleVel += force * dt;
            dangleAngle += dangleVel * dt;

            visualRoot.localRotation = Quaternion.Euler(0, 0, dangleAngle);
        }

        // (keep your UpdateHoverHighlight(eventData.position) here if you're using it)
        UpdateHoverHighlight(eventData.position);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!isDraggable)
        {
            // after ApplyCardEffect... or ReturnToHand logic
            if (cg) cg.blocksRaycasts = true;      // restore
            ForceVisibleIfNotFusionLocked();       // <<< keep card opaque
            dragging = false;

            return;
        }

        if (currentHighlight) { currentHighlight.SetHighlighted(false); currentHighlight = null; }
        HighlightAllEnemies(false);

        var col = RaycastTarget(eventData.position);
        bool overEnemy = col && col.GetComponentInParent<Enemy>() != null;

        // --- AOE path: do it once and RETURN ---
        if (IsEnemyAOECard() && overEnemy)
        {
            ApplyCardEffectToEnemy(col.GetComponentInParent<Enemy>());
            if (cg) cg.blocksRaycasts = true;
            dragging = false;
            return;
        }

        bool droppedOnValidTarget = false;
        if (col)
        {
            var enemy = col.GetComponentInParent<Enemy>();
            if (enemy != null && CanTargetEnemy())
            {
                ApplyCardEffectToEnemy(enemy);
                droppedOnValidTarget = true;
            }
            else
            {
                var p = col.GetComponentInParent<Player>();
                if (p != null && CanTargetPlayer())
                {
                    ApplyCardEffectToPlayer(p);
                    droppedOnValidTarget = true;
                }
            }
        }

        if (!droppedOnValidTarget) ReturnToHand();
        else if (cg) cg.blocksRaycasts = true;

        dragging = false;            // <-- also set at the end for safety
    }





    private void ReturnToHand()
    {
        // Back under the hand panel (no auto-rescale)
        transform.SetParent(originalParent, false);

        var rt = (RectTransform)transform;

        // Where the card currently is (drag position, now in handPanel space)
        Vector2 startPos = rt.anchoredPosition;
        float startZ = rt.localEulerAngles.z;

        // Insert back into the hand so the layout computes the target slot
        if (handManager != null)
            handManager.InsertCard(gameObject, originalIndexInHand);

        // Capture the target slot assigned by PositionCardsInSemiCircle
        Vector2 targetPos = rt.anchoredPosition;
        float targetZ = rt.localEulerAngles.z;

        // Reset to the drag position/rotation so we can tween *to* the target
        rt.anchoredPosition = startPos;
        rt.localRotation = Quaternion.Euler(0, 0, startZ);

        // Re-enable raycasts immediately
        if (cg) cg.blocksRaycasts = true;

        // Tween to slot + restore scale
        rt.DOAnchorPos(targetPos, 0.2f).SetEase(Ease.OutCubic);
        rt.DORotate(new Vector3(0, 0, targetZ), 0.2f, RotateMode.Fast);
        transform.DOScale(baseScale, 0.15f);

        if (visualRoot) visualRoot.localRotation = Quaternion.identity;
    }

    private Collider2D RaycastTarget(Vector2 screenPos)
    {
        Vector2 worldPoint = Camera.main.ScreenToWorldPoint(screenPos);
        // Fallback: if mask is zero (Nothing), use Everything so it still works.
        int mask = (targetMask.value == 0) ? ~0 : targetMask.value;
        RaycastHit2D hit = Physics2D.Raycast(worldPoint, Vector2.zero, 0f, mask);
        return hit.collider;
    }


    private bool CanTargetPlayer()
    {
        return cardData.canTargetPlayer;
    }

    private bool CanTargetEnemy()
    {
        return cardData.canTargetEnemy;
    }

    private void UpdateHoverHighlight(Vector2 screenPos)
    {
        // clear single highlight
        if (currentHighlight) { currentHighlight.SetHighlighted(false); currentHighlight = null; }

        var col = RaycastTarget(screenPos);

        if (IsEnemyAOECard())
        {
            // highlight ALL enemies if hovering any enemy; otherwise clear
            bool overEnemy = col && col.GetComponentInParent<Enemy>() != null;
            HighlightAllEnemies(overEnemy);
            return;
        }

        // single-target path
        if (!col) { HighlightAllEnemies(false); return; }

        var th = col.GetComponentInParent<TargetHighlighter>();
        if (th != null)
        {
            th.SetHighlighted(true);
            currentHighlight = th;
        }

        // make sure AoE list is cleared if we switched from an AoE card
        HighlightAllEnemies(false);
    }



    private void HighlightAllEnemies(bool on)
    {
        // turn off previous
        if (!on && aoeHighlights.Count > 0)
        {
            foreach (var h in aoeHighlights) if (h) h.SetHighlighted(false);
            aoeHighlights.Clear();
            return;
        }

        if (on)
        {
            // clear any old state first
            HighlightAllEnemies(false);

            foreach (var e in BattleManager.Instance.enemies)
            {
                if (!e) continue;
                var th = e.GetComponentInParent<TargetHighlighter>();
                if (th)
                {
                    th.SetHighlighted(true);
                    aoeHighlights.Add(th);
                }
            }
        }
    }

    // --- Forward clicks from the child Button to fusion selection ---
    void OnEnable()
    {
        if (cardButton) cardButton.onClick.AddListener(OnCardButtonClicked);
    }

    void OnDisable()
    {
        if (cardButton) cardButton.onClick.RemoveListener(OnCardButtonClicked);
    }

    private void OnCardButtonClicked()
    {
        // ignore if we started dragging
        if (dragging) return;

        // only during player's turn
        if (BattleManager.Instance == null ||
            BattleManager.Instance.state != BattleManager.BattleState.PLAYER_TURN)
            return;

        if (FusionController.Instance)
            FusionController.Instance.TrySelectCard(this);
    }


    [SerializeField] private Image[] tintImages; // assign card frame/art texts if you want to tint
    public float shakeDist = 12f;
    public float shakeDur = 0.15f;

    private IEnumerator FlashRedCo(float dur = 0.15f)
    {
        if (tintImages == null) yield break;
        foreach (var img in tintImages) if (img) img.color = new Color(1f, 0.4f, 0.4f, img.color.a);
        yield return new WaitForSeconds(dur);
        foreach (var img in tintImages) if (img) img.color = new Color(1f, 1f, 1f, img.color.a);
    }

    private IEnumerator ShakeCo()
    {
        var rt = (RectTransform)transform;
        Vector2 start = rt.anchoredPosition;
        float t = 0f;
        while (t < shakeDur)
        {
            t += Time.deltaTime;
            float k = t / shakeDur;
            float offs = Mathf.Sin(k * Mathf.PI * 2f) * shakeDist * (1f - k);
            rt.anchoredPosition = start + new Vector2(offs, 0f);
            yield return null;
        }
        rt.anchoredPosition = start;
    }

    private void ShowInvalidDropFeedback()
    {
        StartCoroutine(FlashRedCo());
        StartCoroutine(ShakeCo());
    }

    private bool IsEnemyAOECard()
    {
        switch (cardData.cardName)
        {
            case "Red Splatter":
            case "Y-Spray":
            case "Yellow Spray":
            case "Toxic Paint":
                return true;
            default: return false;
        }
    }

    private void TriggerPlayerSwing()
    {
        var p = BattleManager.Instance?.player;
        if (p != null && p.animator != null)
        {
            // optional: make it robust against rapid re-triggers
            p.animator.ResetTrigger("Attack");
            p.animator.SetTrigger("Attack");
        }
    }

    // Allow FusionController to query if this card can be selected for fusion
    public bool IsFusionSelectable()
    {
        // Only during player's turn, not currently being dragged, and not already locked
        return BattleManager.Instance != null
            && BattleManager.Instance.state == BattleManager.BattleState.PLAYER_TURN
            && !dragging
            && isDraggable;
    }

    // Visually/interaction lock when selected for fusion
    public void SetFusionSelected(bool selected)
    {
        fusionSelected = selected;          // <<< remember lock
        isDraggable = !selected;

        EnsureCanvasGroup();
        cg.alpha = selected ? 0.6f : 1f;    // only dim here
    }

    private void EnsureCanvasGroup()
    {
        if (cg == null) cg = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
    }

    // Force the card fully visible unless fusion selection intentionally dims it.
    private void ForceVisibleIfNotFusionLocked()
    {
        EnsureCanvasGroup();
        if (!fusionSelected)
        {
            cg.alpha = 1f;            // fully opaque
            cg.interactable = true;   // safe defaults
                                      // blocksRaycasts is controlled by drag code; don't touch it here
        }
    }

    // central place to decide if a card should swing the weapon
    private bool ShouldTriggerAttackSwing()
    {
        switch (cardData.cardName)
        {
            // single-target attacks
            case "Red Stroke":
            case "Siphon":
            case "Reckless Stroke":
            case "Attack Break":
            case "Corrode":
            case "Imaginary Paint":
            case "Crushing Paint":
            case "Finishing Touch":
            case "Toxic Paint":
            case "Poison":
                return true;

            // AoE attacks (call once per play)
            case "Red Splatter":
            case "Y-Spray":
                return true;

            default:
                return false; // heals/buffs/utility should NOT swing
        }
    }

    private void PlaySTWithImpact(Enemy enemy, EffectKey key, int dmg, System.Action afterImpact = null)
    {
        var host = EffectDirector.Instance.GetEnemyHost(enemy);
        // What to do *at* the impact frame:
        host.ArmImpact(() =>
        {
            enemy.TakeDamage(dmg);                       // Triggers enemy Hurt at impact time
            BattleManager.Instance.CheckVictoryImmediate();
            afterImpact?.Invoke();
        });

        var tint = EffectDirector.Instance.ResolveTypeColor(cardData.cardType, Color.white);
        EffectDirector.Instance.PlayEnemyHit(enemy, key, cardData.sfx, cardData.sfxVolume, tint);
    }

    private void PlayAoeWithImpact(EffectKey key, System.Action onImpact)
    {
        var host = EffectDirector.Instance.aoeHost;
        host.ArmImpact(() =>
        {
            onImpact?.Invoke();                          // Apply to all at impact
            BattleManager.Instance.CheckVictoryImmediate();
        });

        var tint = EffectDirector.Instance.ResolveTypeColor(cardData.cardType, Color.white);
        EffectDirector.Instance.PlayAoe(key, cardData.sfx, cardData.sfxVolume, tint);
    }

    // ---------------------------------------------------------
    // OSC HELPERS FOR FINAL
    // ---------------------------------------------------------

    private IEnumerator OscHoverPulseCo()
    {
        // gate on
        SendCardNoteOsc(1);
        yield return new WaitForSeconds(oscGatePulseDuration);
    }

    private void SendCardNoteOsc(int gate)
    {
        if (!enableOsc || oscTransmitter == null || cardData == null)
            return;

        Debug.Log($"[OSC] Sending hover note for {cardData.cardName}, gate={gate}");

        int rootMidi = GetRootMidiFromOscColor(oscColor);
        int[] chord = BuildChordFromCardCost(rootMidi);

        float rootVOct = MidiToVOct(chord[0]);
        float thirdVOct = MidiToVOct(chord[1]);
        float fifthVOct = MidiToVOct(chord[2]);

        // root
        var msgRoot = new OSCMessage("/card/hover/root");
        msgRoot.AddValue(OSCValue.Float(rootVOct));
        oscTransmitter.Send(msgRoot);

        // third
        var msgThird = new OSCMessage("/card/hover/third");
        msgThird.AddValue(OSCValue.Float(thirdVOct));
        oscTransmitter.Send(msgThird);

        // fifth
        var msgFifth = new OSCMessage("/card/hover/fifth");
        msgFifth.AddValue(OSCValue.Float(fifthVOct));
        oscTransmitter.Send(msgFifth);

        // cost (as int)
        var msgCost = new OSCMessage("/card/hover/cost");
        msgCost.AddValue(OSCValue.Int(cardData.cardCost));
        oscTransmitter.Send(msgCost);

        // gate (0 or 1)
        var msgGate = new OSCMessage("/card/hover/gate");
        msgGate.AddValue(OSCValue.Int(gate));
        oscTransmitter.Send(msgGate);

    }

    private int GetRootMidiFromOscColor(OscCardColor c)
    {
        switch (c)
        {
            case OscCardColor.Purple: return 60; // C4
            case OscCardColor.Red: return 62; // D4
            case OscCardColor.Orange: return 64; // E4
            case OscCardColor.Yellow: return 67; // G4
            case OscCardColor.Green: return 69; // A4
            case OscCardColor.Blue: return 71; // B4
            default: return 60;
        }
    }

    private int[] BuildChordFromCardCost(int rootMidi)
    {
        // 1-cost: single note; duplicate so all 3 channels still have something
        if (cardData.cardCost <= 1)
            return new[] { rootMidi, rootMidi, rootMidi };

        // 2-cost+ : major triad (root, +4, +7)
        return new[] { rootMidi, rootMidi + 4, rootMidi + 7 };
    }

    private float MidiToVOct(int midiNote)
    {
        // MIDI 60 (C4) = 0V; +12 semitones = +1V
        return (midiNote - 60) / 12f;
    }

}