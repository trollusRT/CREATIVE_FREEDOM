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

    // Dangle (pendulum) settings — while dragging, the card hangs from the exact
    // point you grabbed and swings like a fidget toy. Whip it fast and it spins.
    [Header("Dangle")]
    public bool dangle = true;
    public RectTransform visualRoot;     // assign in prefab (used by hover)
    [Tooltip("Normalized point treated as the card's weight / centre of mass " +
             "(0,0 = bottom-left, 1,1 = top-right). Lower = dangles more eagerly.")]
    public Vector2 weightCenter = new Vector2(0.5f, 0.3f);
    [Tooltip("Downward pull in parent units/sec^2. Higher = snappier swing & faster settle.")]
    public float dangleGravity = 3000f;
    [Tooltip("Fraction of swing velocity kept each frame. ~0.92 feels lively; lower settles sooner.")]
    [Range(0f, 1f)] public float dangleDamping = 0.92f;
    [Tooltip("Let a hard flick wind the card all the way around.")]
    public bool allowFullSpin = true;
    [Tooltip("Used only when full spin is off — clamps the swing to +/- this many degrees.")]
    public float maxTilt = 75f;
    [Tooltip("Cap on swing speed (deg/sec) so flicks stay readable. 0 = uncapped.")]
    public float maxSpinSpeed = 1440f;

    // internal pendulum state
    private bool dragging = false;
    private Vector2 grabLocal;          // root pivot -> grab point, local (unscaled)
    private Vector2 leverDir;           // unit dir grab -> weight centre, at rest
    private float rodLength;            // |grab -> weight centre| in parent units (scaled)
    private Vector2 bobPos;             // Verlet weight position (parent space)
    private Vector2 prevBobPos;
    private float currentAngleDeg;      // accumulated swing angle (can exceed 360 for spins)
    private Vector2 lastPointerScreenPos;
    private RectTransform dragParent;   // parent during drag (= dragLayer)
    private Camera dragUiCamera;
    private Vector2 dragAnchorRef;      // anchor origin offset, so the pin is parent-config agnostic

    [Header("Drag Settings")]
    public bool isDraggable = true;
    public HandManager handManager;
    public Transform dragLayer;  // Assign a top-level UI object under the same Canvas

    public Animator playerSpellAnimator;
    public Animator fieldSpellAnimator;

    // Original transform data to restore
    private Transform originalParent;
    private Vector3 originalScale;   // <-- keep only this one
    private int originalIndexInHand;

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

    [SerializeField] AudioClip hoverSfx;

    [SerializeField] private float hoverLift = 28f;
    [SerializeField] private float hoverDuration = 0.12f;
    [SerializeField] private Ease hoverEase = Ease.OutQuad;

    private bool isHovered;
    private Vector2 hoverBaseAnchoredPos;
    private Tweener hoverTween;
    private bool hoverInitialized;

    private bool pointerDown;
    private Vector2 pointerDownPos;
    [SerializeField] float clickMaxMove = 8f; // pixels allowed to still count as a click


    // at top of Card
    private Vector3 baseScale;   // never overwrite after Start()

    // --- alpha/visibility guards ---
    private bool fusionSelected = false;   // true only while selected for fusion

    // --- Remember (The Script): right-click a hand card to carry it into next turn for 1 Focus ---
    [Header("Remember (The Script)")]
    [Tooltip("Optional visual shown while this card is Remembered (carried to next turn). Null-safe; a gold tint on tintImages is the fallback so the state still reads without wiring. Keep it inactive by default in the prefab.")]
    public GameObject rememberedIndicator;
    public bool Remembered { get; private set; }

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

        if (rememberedIndicator) rememberedIndicator.SetActive(false); // fresh cards start un-Remembered
    }



    public void UpdateCardVisuals()
    {
        if (cardArtwork != null && cardData.cardSprite != null)
        {
            cardArtwork.sprite = cardData.cardSprite;
        }
        // If you also have text fields for cost/name, update them here.
    }

    // Plays a player-side overlay VFX on the FXPlayer host (tint + turn-gating), mirroring the Restore/CreativeFreedom pattern.
    private void PlayPlayerVfx(EffectKey key)
    {
        var dir = EffectDirector.Instance;
        if (dir == null || key == EffectKey.None) return;
        var tint = dir.ResolveTypeColor(cardData.cardType, Color.white);
        BeginEffectGate(dir.playerSingleTargetHost);
        dir.PlayPlayerHit(key, cardData.sfx, cardData.sfxVolume, tint);
    }

    // Inside Card class (same level as ApplyCardEffectToEnemy / OnEndDrag)
    private void ApplyCardEffectToPlayer(Player player)
    {
        // Forgotten (Decaying Mind): costs no AP, does nothing — Junior forgot what it did.
        if (cardData.cardName == "Forgotten") { Destroy(gameObject); return; }

        bool didResolve = false;
        // Every card costs 1 AP to play (cardCost only gates draw/fusion availability).
        if (BattleManager.Instance.playerAP < 1)
        {
            Debug.LogWarning($"Not enough AP to play {cardData.cardName}!");
            ReturnToHand();
            return;
        }

        BattleManager.Instance.UseAP(1);

        // Package E: data-driven player effects (optional)
        if (CardEffectRegistry.TryResolvePlayer(cardData, player, out var resolvedPlayer) && resolvedPlayer.onResolve != null)
        {
            var dir = EffectDirector.Instance;
            if (dir != null && resolvedPlayer.vfxKey != EffectKey.None)
            {
                var tint = dir.ResolveTypeColor(cardData.cardType, Color.white);
                var host = dir.playerSingleTargetHost;
                BeginEffectGate(host);
                dir.PlayPlayerHit(resolvedPlayer.vfxKey, cardData.sfx, cardData.sfxVolume, tint);
            }

            resolvedPlayer.onResolve.Invoke();
            didResolve = true;
            BattleManager.Instance?.NotifyCardResolved(cardData, null);
            return;
        }

        switch (cardData.cardName)
        {
            case "Restore":
                {
                    didResolve = true;
                    var tint = EffectDirector.Instance.ResolveTypeColor(cardData.cardType, Color.white);
                    var _host = EffectDirector.Instance ? EffectDirector.Instance.playerSingleTargetHost : null;
                    BeginEffectGate(_host);
                    EffectDirector.Instance.PlayPlayerHit(EffectKey.Restore, cardData.sfx, cardData.sfxVolume, tint);

                    player.Heal(Random.Range(cardData.minValue, cardData.maxValue + 1));
                }
                break;

            case "Rejuvenate":
                {
                    didResolve = true;
                    var tint = EffectDirector.Instance.ResolveTypeColor(cardData.cardType, Color.white);
                    var _host = EffectDirector.Instance ? EffectDirector.Instance.playerSingleTargetHost : null;
                    BeginEffectGate(_host);
                    EffectDirector.Instance.PlayPlayerHit(EffectKey.Rejuvenate, cardData.sfx, cardData.sfxVolume, tint);

                    player.Heal(cardData.minValue);
                }
                break;

            case "Shield":
                {
                    didResolve = true;
                    player.animator.SetTrigger("Buff");
                    player.activeEffects.Add(new StatusEffect { type = StatusType.Shield, duration = 2 });
                }
                break;

            case "Defensive Stance":
                {
                    didResolve = true;
                    player.animator.SetTrigger("Buff");
                    PlayPlayerVfx(EffectKey.DefensiveStance);
                    player.activeEffects.Add(new StatusEffect { type = StatusType.DefensiveStance, duration = 2 });
                }
                break;

            // add your other self-target cases here...


            case "Cleanse":
                {
                    didResolve = true;
                    player.animator.SetTrigger("Buff");
                    player.activeEffects.Clear();
                }
                break;

            case "Just Give Me a Second":
                {
                    didResolve = true;
                    player.animator.SetTrigger("Buff");
                    PlayPlayerVfx(EffectKey.JustGiveMeASecond);
                    player.activeEffects.Clear();
                    player.Heal(10);
                }
                break;

            case "Enrage":
                {
                    didResolve = true;
                    player.animator.SetTrigger("Buff");
                    // Pay a small HP cost, then gain two double-damage charges.
                    player.PayHealth(3);
                    player.AddDoubleDamageCharges(2);
                }
                break;

            case "Vengeance":
                {
                    didResolve = true;
                    player.animator.SetTrigger("Buff");
                    PlayPlayerVfx(EffectKey.Vengeance);
                    player.Heal(Random.Range(cardData.minValue, cardData.maxValue + 1));
                    player.AddDoubleDamageCharges(1);
                }
                break;

            case "Second Wind":
                {
                    didResolve = true;
                    player.animator.SetTrigger("Buff");
                    PlayPlayerVfx(EffectKey.SecondWind);
                    player.EnableSecondWind(regenPerTurn: 3, turns: 2);
                }
                break;

            case "Counter":
                {
                    didResolve = true;
                    player.animator.SetTrigger("Buff");
                    PlayPlayerVfx(EffectKey.Counter);
                    player.EnableCounter(power: Mathf.Max(1, cardData.minValue), turns: 3);
                }
                break;

            case "Reflect":
                {
                    didResolve = true;
                    player.animator.SetTrigger("Buff");
                    PlayPlayerVfx(EffectKey.Reflect);
                    player.EnableReflect(turns: 3);
                }
                break;

            case "Leech Trap":
                {
                    didResolve = true;
                    player.animator.SetTrigger("Buff");
                    PlayPlayerVfx(EffectKey.LeechTrap);
                    player.EnableLeechTrap(power: Mathf.Max(1, cardData.minValue), turns: 2);
                }
                break;

            case "CREATIVE FREEDOM":
                {
                    didResolve = true;
                    var tint = EffectDirector.Instance.ResolveTypeColor(cardData.cardType, Color.white);
                    var _host = EffectDirector.Instance ? EffectDirector.Instance.playerSingleTargetHost : null;
                    BeginEffectGate(_host);
                    EffectDirector.Instance.PlayPlayerHit(EffectKey.CreativeFreedom, cardData.sfx, cardData.sfxVolume, tint);

                    player.animator.SetTrigger("Buff");
                    player.EnableCreativeFreedom(10);
                }
                break;

            case "Reiterate":
                {
                    if (!BattleManager.Instance || !BattleManager.Instance.CanUseReiterateThisTurn())
                        break;

                    didResolve = true;
                    BattleManager.Instance.MarkUsedReiterate();
                    BattleManager.Instance.ReiterateHandAndAP();
                }
                break;

            case "Again!":
                {
                    if (!BattleManager.Instance || !BattleManager.Instance.CanUseAgainThisTurn())
                        break;

                    didResolve = true;
                    PlayPlayerVfx(EffectKey.Again);
                    BattleManager.Instance.MarkUsedAgain();
                    BattleManager.Instance.PlayLastResolvedCard();
                }
                break;

            // ----- TIER 1 COMMONS (cost 1) -----
            case "Streaking Medium":
                {
                    didResolve = true;
                    PlayPlayerVfx(EffectKey.Rejuvenate);
                    player.ApplyRegen(1, 3);   // Regen 1/turn for 3 turns
                    player.ReducePoison(1);    // shed 1 Poison stack if any
                }
                break;

            case "Blank Canvas": // White, 2 AP — dump the hand for AP + Shield
                {
                    didResolve = true;
                    player.animator.SetTrigger("Buff");
                    // DiscardHand keeps Remembered cards and returns how many were actually dumped.
                    int discarded = (handManager != null) ? handManager.DiscardHand() : 0;
                    if (discarded > 0)
                    {
                        BattleManager.Instance.GrantAP(discarded);   // 1 AP per discarded card
                        player.AddShield(discarded);                 // 1 flat Shield per discarded card
                    }
                }
                break;

            default:
                Debug.Log($"{cardData.cardName} not implemented for player!");
                break;
        }

        player.UpdateHPText();
        Destroy(gameObject);

        if (didResolve)
            BattleManager.Instance?.NotifyCardResolved(cardData, null);
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
        // Forgotten (Decaying Mind): costs no AP, does nothing — Junior forgot what it did.
        if (cardData.cardName == "Forgotten") { Destroy(gameObject); return; }

        // local guard
        bool IsAlive(Enemy e) => e != null && !e.IsDead && e.GetHP() > 0;
        bool didResolve = false;


        // Do not spend AP on dead/invalid targets
        if (!IsAlive(enemy))
        {
            ReturnToHand();
            return;
        }

        // Every card costs 1 AP to play (cardCost only gates draw/fusion availability).
        if (BattleManager.Instance.playerAP < 1)
        {
            Debug.LogWarning($"Not enough AP to play {cardData.cardName}!");
            ReturnToHand();
            return;
        }

        // Player swing (only for attack-y cards)
        if (ShouldTriggerAttackSwing())
            TriggerPlayerSwing();

        // Spend AP now (visual still plays, effects will be armed to the impact frame)
        BattleManager.Instance.UseAP(1);

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
                    // Mark this as a player card hit so Enemy.TakeDamage applies Insight damage modifiers.
                    InsightHost.BeginCardDamage(false);
                    try { onImpact?.Invoke(); }
                    finally { InsightHost.EndCardDamage(); }
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
                    // Mark this as a player AoE card hit so Enemy.TakeDamage applies Insight damage modifiers.
                    InsightHost.BeginCardDamage(true);
                    try { onImpact?.Invoke(); }
                    finally { InsightHost.EndCardDamage(); }
                    BattleManager.Instance.CheckVictoryImmediate();
                });
            }

            BeginEffectGate(host);
            var tint = dir.ResolveTypeColor(cardData.cardType, Color.white);
            dir.PlayAoe(key, cardData.sfx, cardData.sfxVolume, tint);
        }

        // Package E: data-driven enemy effects (optional)
        if (CardEffectRegistry.TryResolveEnemy(cardData, enemy, out var resolvedEnemy) && resolvedEnemy.onImpact != null)
        {
            if (resolvedEnemy.vfxKey == EffectKey.None)
            {
                // No VFX key configured; apply immediately.
                resolvedEnemy.onImpact.Invoke();
                BattleManager.Instance.CheckVictoryImmediate();
                return;
            }

            if (resolvedEnemy.isAoe)
                AOEWithImpact(resolvedEnemy.vfxKey, resolvedEnemy.onImpact);
            else
                STWithImpact(enemy, resolvedEnemy.vfxKey, resolvedEnemy.onImpact);

            return;
        }


        switch (cardData.cardName)
        {
            // ----- RED -----
            case "Red Stroke":
                {
                    didResolve = true;
                    int dmg = Random.Range(cardData.minValue, cardData.maxValue + 1);
                    STWithImpact(enemy, EffectKey.RedStroke, () => enemy.TakeDamage(dmg));
                }
                break;

            case "Siphon":
                {
                    didResolve = true;
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
                    didResolve = true;
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
                    didResolve = true;
                    float hpRatio = (float)BattleManager.Instance.player.currentHP / BattleManager.Instance.player.maxHP;
                    float mult = (hpRatio < 0.25f) ? 4f : (hpRatio < 0.5f ? 2f : 1f);
                    int baseDmg = Random.Range(cardData.minValue, cardData.maxValue + 1);
                    int finalDmg = Mathf.RoundToInt(baseDmg * mult);

                    STWithImpact(enemy, EffectKey.RecklessStroke, () => enemy.TakeDamage(finalDmg));
                }
                break;

            case "Finishing Touch":
                {
                    didResolve = true;
                    int finalDmg = (enemy.currentHP < 30) ? enemy.currentHP : 30;
                    STWithImpact(enemy, EffectKey.FinishingTouch, () => enemy.TakeDamage(finalDmg));
                }
                break;

            // ----- BLUE -----
            case "Attack Break":
                {
                    didResolve = true;
                    int dmg = Random.Range(cardData.minValue, cardData.maxValue + 1);
                    STWithImpact(enemy, EffectKey.AttackBreak, () =>
                    {
                        enemy.TakeDamage(dmg);
                        enemy.activeEffects.Add(new StatusEffect { type = StatusType.AttackBreak, power = 2, duration = 2 });
                    });
                }
                break;

            // ----- YELLOW -----
            case "Yellow Spray":
            case "Y-Spray":
                {
                    didResolve = true;
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
                    didResolve = true;
                    int poisonPerTurn = Random.Range(1, 3); // 1-2
                    STWithImpact(enemy, EffectKey.PoisonST, () => enemy.ApplyPoison(poisonPerTurn, 3));
                }
                break;

            case "Sleep":
                {
                    didResolve = true;
                    STWithImpact(enemy, EffectKey.Sleep, () => enemy.ApplySleep(1));
                }
                break;

            case "Corrode":
                {
                    didResolve = true;
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
                    didResolve = true;
                    int poisonPerTurn = Random.Range(3, 7); // 2-3
                    STWithImpact(enemy, EffectKey.ToxicPaint, () => enemy.ApplyPoison(poisonPerTurn, 3));
                }
                break;

            case "Pool of Paint":
                {
                    didResolve = true;
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
                    didResolve = true;
                    int dmg = cardData.minValue; // fixed nuke
                                                 // If you visually treat it as an AoE blast, keep AoE host; else use STWithImpact
                    dmg = BattleManager.Instance.player.ModifyOutgoingDamage(dmg);
                    AOEWithImpact(EffectKey.ImaginaryPaint, () => enemy.TakeDamage(dmg));
                }
                break;

            // ----- ORANGE -----
            case "Crushing Paint":
                {
                    didResolve = true;
                    int dmg = cardData.minValue;
                    STWithImpact(enemy, EffectKey.CrushingPaint, () =>
                    {
                        enemy.TakeDamage(dmg);
                        enemy.activeEffects.Add(new StatusEffect { type = StatusType.Stun, duration = 2 });
                    });
                }
                break;

            // ----- TIER 1 COMMONS (cost 1) -----
            case "Ink Needle":
                {
                    didResolve = true;
                    int dmg = Mathf.Max(0, cardData.minValue);      // headline hit (1)
                    STWithImpact(enemy, EffectKey.PoisonST, () =>
                    {
                        enemy.TakeDamage(dmg);
                        enemy.ApplyPoison(2, 2);                     // 2 poison/turn for 2 turns
                    });
                }
                break;

            case "Smudge":
                {
                    didResolve = true;
                    int dmg = Mathf.Max(0, cardData.minValue);      // 2
                    STWithImpact(enemy, EffectKey.Corrode, () =>
                    {
                        enemy.TakeDamage(dmg);
                        enemy.ApplyCorrode(1, 2);                    // +25% dmg taken; lasts into your next turn
                    });
                }
                break;

            case "Rage Mark":
                {
                    didResolve = true;
                    var rm = BattleManager.Instance.player;
                    int dmg = Mathf.Max(0, cardData.minValue);      // base 2
                    if (rm != null && rm.currentHP * 2 < rm.maxHP)  // Junior below 50% HP
                        dmg += 3;
                    STWithImpact(enemy, EffectKey.RedStroke, () => enemy.TakeDamage(dmg));
                }
                break;

            case "Critic's Note":
                {
                    didResolve = true;                              // pure debuff, no damage
                    STWithImpact(enemy, EffectKey.AttackBreak, () => enemy.ApplyAttackBreak(3, 2));
                }
                break;

            // ----- HEAVY HITTERS (brainstorm batch) -----
            case "Astral Nuke": // Purple, 3 AP
                {
                    didResolve = true;
                    int dmg = cardData.minValue;                    // 14
                    STWithImpact(enemy, EffectKey.ImaginaryPaint, () =>
                        enemy.TakeDamage(BattleManager.Instance.player.ModifyOutgoingDamage(dmg)));
                    BattleManager.Instance.player.PayHealth(3);      // recoil (PayHealth won't self-KO)
                }
                break;

            case "Vermillion Spear": // Red, 2 AP
                {
                    didResolve = true;
                    int dmg = cardData.minValue;                    // 6
                    if (enemy.currentHP >= enemy.maxHP) dmg += 4;    // first-strike spike vs a full-HP target
                    STWithImpact(enemy, EffectKey.RedStroke, () =>
                        enemy.TakeDamage(BattleManager.Instance.player.ModifyOutgoingDamage(dmg)));
                }
                break;

            case "Bleed Out": // Crimson, 3 AP — turns the target's Poison into burst
                {
                    didResolve = true;
                    int basePart = cardData.minValue;               // 8
                    STWithImpact(enemy, EffectKey.FinishingTouch, () =>
                    {
                        int poison = 0;
                        for (int i = 0; i < enemy.activeEffects.Count; i++)
                        {
                            var ef = enemy.activeEffects[i];
                            if (ef != null && ef.type == StatusType.Poison) poison += Mathf.Max(0, ef.power);
                        }
                        enemy.activeEffects.RemoveAll(ef => ef != null && ef.type == StatusType.Poison);
                        enemy.TakeDamage(BattleManager.Instance.player.ModifyOutgoingDamage(basePart + poison));
                    });
                }
                break;

            case "Void Siphon": // Purple, 2 AP — lifesteal, full heal on kill
                {
                    didResolve = true;
                    int dmg = cardData.minValue;                    // 4
                    STWithImpact(enemy, EffectKey.Siphon, () =>
                    {
                        int dealt = BattleManager.Instance.player.ModifyOutgoingDamage(dmg);
                        enemy.TakeDamage(dealt);
                        int heal = enemy.IsDead ? dealt : Mathf.Max(1, dealt / 2);
                        BattleManager.Instance.player.Heal(heal);
                    });
                }
                break;

            case "Full Palette": // Rainbow, 3 AP — scales with colours played this combat
                {
                    didResolve = true;
                    int per = Mathf.Max(1, cardData.minValue);      // 2 dmg per distinct colour
                    STWithImpact(enemy, EffectKey.ImaginaryPaint, () =>
                    {
                        var set = BattleManager.Instance.colorsPlayedThisCombat;
                        int colours = Mathf.Max(4, set != null ? set.Count : 0);
                        enemy.TakeDamage(BattleManager.Instance.player.ModifyOutgoingDamage(per * colours));
                    });
                }
                break;

            // ----- AoE (raw damage, like Red Splatter — not routed through ModifyOutgoingDamage) -----
            case "Bloodbath": // Crimson, 2 AP — AoE + heal per kill
                {
                    didResolve = true;
                    int each = Mathf.Max(0, cardData.minValue);     // 4 to all
                    AOEWithImpact(EffectKey.RedSplatter, () =>
                    {
                        var snapshot = new List<Enemy>(BattleManager.Instance.enemies);
                        int kills = 0;
                        foreach (var e in snapshot)
                        {
                            if (!IsAlive(e)) continue;
                            e.TakeDamage(each);
                            if (e.IsDead) kills++;
                        }
                        if (kills > 0) BattleManager.Instance.player.Heal(3 * kills);
                    });
                }
                break;

            case "The Nothing": // Black, 3 AP — erasure: Corrode all (enemy buff-wipe is a no-op until enemies have buffs)
                {
                    didResolve = true;
                    AOEWithImpact(EffectKey.YSpray, () =>
                    {
                        var snapshot = new List<Enemy>(BattleManager.Instance.enemies);
                        foreach (var e in snapshot)
                        {
                            if (!IsAlive(e)) continue;
                            e.ApplyCorrode(2, 2);
                        }
                    });
                }
                break;

            default:
                Debug.Log($"{cardData.cardName} not implemented for enemy!");
                break;
        }

        if (didResolve)
            BattleManager.Instance?.NotifyCardResolved(cardData, enemy != null ? enemy.gameObject : null);

        // Destroy the card right away; the armed impact will still fire from the effect host.
        Destroy(gameObject);
    }
    // ---------------------------------------------------------
    // HOVER EFFECTS
    // ---------------------------------------------------------



    public void OnPointerEnter(PointerEventData eventData)
    {
        if (dragging || !isDraggable) return;
        if (BattleManager.Instance == null) return;

        ForceVisibleIfNotFusionLocked();

        if (visualRoot == null)
            visualRoot = transform as RectTransform;

        var vr = (RectTransform)visualRoot;

        // Record the base position only once, before any hover movement
        if (!hoverInitialized)
        {
            hoverBaseAnchoredPos = vr.anchoredPosition;
            hoverInitialized = true;
        }

        // Stop any return tween before lifting again
        hoverTween?.Kill();

        // Move only the visual root
        hoverTween = vr.DOAnchorPosY(hoverBaseAnchoredPos.y + hoverLift, hoverDuration)
            .SetEase(hoverEase)
            .SetUpdate(true);

        isHovered = true;

        // SFX
        var am = BattleManager.Instance.audioManager;
        if (hoverSfx && am != null)
            am.PlaySound(hoverSfx);

        // OSC hover pulse
        if (enableOsc &&
            oscTransmitter != null &&
            BattleManager.Instance.state == BattleManager.BattleState.PLAYER_TURN)
        {
            StartCoroutine(OscHoverPulseCo());
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!isHovered) return;
        if (dragging) return;

        DoUnhover();
    }

    private void DoUnhover()
    {
        ForceVisibleIfNotFusionLocked();

        if (visualRoot == null)
            visualRoot = transform as RectTransform;

        var vr = (RectTransform)visualRoot;

        hoverTween?.Kill();
        hoverTween = vr.DOAnchorPos(hoverBaseAnchoredPos, hoverDuration)
            .SetEase(hoverEase)
            .SetUpdate(true);

        isHovered = false;
    }

    private void ResetHoverInstant()
    {
        hoverTween?.Kill();

        if (visualRoot == null)
            visualRoot = transform as RectTransform;

        var vr = (RectTransform)visualRoot;
        vr.anchoredPosition = hoverBaseAnchoredPos;

        isHovered = false;
    }

    // ---------------------------------------------------------
    // DRAG & DROP IMPLEMENTATION
    // ---------------------------------------------------------
    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!isDraggable) return;
        // Only allow playing cards on the player's own turn (Remembered cards now persist into the enemy turn).
        if (BattleManager.Instance == null || BattleManager.Instance.state != BattleManager.BattleState.PLAYER_TURN) return;

        ResetHoverInstant();        // already in your code, good

        var canvas = GetComponentInParent<Canvas>();
        dragUiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

        originalParent = transform.parent;

        var rt = (RectTransform)transform;

        // Record where on the card we grabbed it (root pivot -> grab point, local space).
        // Captured before re-parenting; local coords are intrinsic to the card.
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rt, eventData.position, dragUiCamera, out grabLocal);

        if (handManager != null)
            originalIndexInHand = handManager.RemoveCard(gameObject);

        transform.SetParent(dragLayer, false);
        transform.SetAsLastSibling();
        dragParent = dragLayer as RectTransform;
        dragAnchorRef = AnchorReferenceLocal(rt, dragParent);

        if (cg) cg.blocksRaycasts = false;

        // always start drag from baseScale, then apply dragScale
        transform.localScale = baseScale * dragScale;
        float s = rt.localScale.x;

        // The card's "weight" (centre of mass) as a local offset from its pivot,
        // and the rigid rod running from the grab point down to that weight.
        Vector2 size = rt.rect.size;
        Vector2 comLocal = new Vector2((weightCenter.x - rt.pivot.x) * size.x,
                                       (weightCenter.y - rt.pivot.y) * size.y);
        Vector2 lever = comLocal - grabLocal;
        leverDir = (lever.sqrMagnitude > 0.0001f) ? lever.normalized : Vector2.down;
        rodLength = lever.magnitude * s;

        // Seed the pendulum upright and at rest, hanging from the cursor.
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            dragParent, eventData.position, dragUiCamera, out Vector2 pivot);
        currentAngleDeg = 0f;
        bobPos = pivot + leverDir * rodLength;
        prevBobPos = bobPos;
        lastPointerScreenPos = eventData.position;

        dragging = true;

        // Place immediately so the first frame doesn't pop.
        rt.localRotation = Quaternion.identity;
        rt.anchoredPosition = pivot - dragAnchorRef - grabLocal * s;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // ignore clicks that are actually part of a drag
        if (dragging) return;

        // Right-click is reserved for Remember (handled in Update); only left-click selects for fusion.
        if (eventData.button != PointerEventData.InputButton.Left) return;

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

        // Movement, rotation and pendulum physics all happen in Update(), so the card
        // keeps swinging and settling even on frames where the cursor holds still.
        lastPointerScreenPos = eventData.position;

        UpdateHoverHighlight(eventData.position);
    }

    // ---------------------------------------------------------
    // DANGLE — the card hangs from the grabbed point and swings
    // like a fidget toy. Modelled as a Verlet pendulum (a weight
    // on a rigid rod) so a hard flick can wind it into full spins.
    // Runs every frame while dragging, independent of OnDrag.
    // ---------------------------------------------------------
    void Update()
    {
        // Right-click while hovering a hand card toggles Remember (1 Focus to carry it into next turn).
        if (isHovered && Input.GetMouseButtonDown(1))
            ToggleRemember();

        if (!dragging || dragParent == null) return;

        float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
        if (dt <= 0f) return;

        // Pivot = the point the card hangs from = the cursor, in drag-layer space.
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                dragParent, lastPointerScreenPos, dragUiCamera, out Vector2 pivot))
            return;

        var rt = (RectTransform)transform;

        if (dangle && rodLength > 0.01f)
        {
            // Verlet integration: weight is pulled down by gravity, carrying its inertia.
            Vector2 vel = (bobPos - prevBobPos) * dangleDamping;
            prevBobPos = bobPos;
            bobPos += vel + new Vector2(0f, -dangleGravity) * (dt * dt);

            // Rigid-rod constraint: keep the weight exactly rodLength from the cursor.
            // Moving the cursor yanks the weight around this circle, which is the swing.
            Vector2 d = bobPos - pivot;
            float len = d.magnitude;
            bobPos = (len > 0.0001f) ? pivot + d * (rodLength / len)
                                     : pivot + Vector2.down * rodLength;

            // Angle that rotates the rest lever onto the current rod direction.
            Vector2 rodDir = (bobPos - pivot) / rodLength;
            float target = UnwrapToward(currentAngleDeg, SignedAngleDeg(leverDir, rodDir));

            if (!allowFullSpin)
                target = Mathf.Clamp(target, -maxTilt, maxTilt);
            if (maxSpinSpeed > 0f)
            {
                float maxStep = maxSpinSpeed * dt;
                target = Mathf.Clamp(target, currentAngleDeg - maxStep, currentAngleDeg + maxStep);
            }
            currentAngleDeg = target;
        }
        else
        {
            currentAngleDeg = 0f;
        }

        // Apply the swing, then pin the grabbed point back under the cursor.
        rt.localRotation = Quaternion.Euler(0f, 0f, currentAngleDeg);
        float s = rt.localScale.x;
        rt.anchoredPosition = pivot - dragAnchorRef - RotateDeg(grabLocal * s, currentAngleDeg);
    }

    // Offset of a point-anchored child's anchor reference from the parent's pivot,
    // in parent-local coords (the same origin ScreenPointToLocalPointInRectangle uses).
    private static Vector2 AnchorReferenceLocal(RectTransform child, RectTransform parent)
    {
        Vector2 anc = (child.anchorMin + child.anchorMax) * 0.5f;
        Rect pr = parent.rect;
        return new Vector2((anc.x - parent.pivot.x) * pr.width,
                           (anc.y - parent.pivot.y) * pr.height);
    }

    // Signed 2D angle (degrees) from one vector to another.
    private static float SignedAngleDeg(Vector2 from, Vector2 to)
    {
        float cross = from.x * to.y - from.y * to.x;
        float dot = from.x * to.x + from.y * to.y;
        return Mathf.Atan2(cross, dot) * Mathf.Rad2Deg;
    }

    // Rotate a 2D vector by an angle in degrees (CCW).
    private static Vector2 RotateDeg(Vector2 v, float deg)
    {
        float r = deg * Mathf.Deg2Rad;
        float c = Mathf.Cos(r), s = Mathf.Sin(r);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }

    // Lift a wrapped (-180..180) target onto the accumulated angle so a hard flick
    // winds continuously past 180 into a full spin instead of snapping back.
    private static float UnwrapToward(float current, float target)
    {
        return current + Mathf.DeltaAngle(current, target);
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
            case "Bloodbath":
            case "The Nothing":
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

    // ---------------------------------------------------------
    // REMEMBER (The Script) — carry a card into next turn for 1 Focus
    // ---------------------------------------------------------

    // Toggle Remember on this card. Costs 1 Focus to pin; right-click again to refund and release.
    public void ToggleRemember()
    {
        var bm = BattleManager.Instance;
        if (bm == null || bm.state != BattleManager.BattleState.PLAYER_TURN) return;
        if (dragging) return;
        if (cardData != null && cardData.cardName == "Forgotten") return; // can't pin a Forgotten card

        if (Remembered)
        {
            SetRemembered(false);
            bm.GrantFocus(1);              // refund the Focus
        }
        else
        {
            if (!bm.SpendFocus(1))         // costs 1 Focus
            {
                ShowInvalidDropFeedback(); // not enough Focus — shake/flash for feedback
                return;
            }
            SetRemembered(true);
        }
    }

    // Sets the Remembered flag + visual only. Focus is handled by ToggleRemember; this does NOT spend/refund,
    // so HandManager can clear the flag on carry-over without touching Focus.
    public void SetRemembered(bool on)
    {
        Remembered = on;
        if (rememberedIndicator) rememberedIndicator.SetActive(on);

        // Fallback tint so the state reads even without a wired indicator.
        Color tint = on ? new Color(1f, 0.9f, 0.5f) : Color.white;
        if (tintImages != null)
            foreach (var img in tintImages)
                if (img) img.color = new Color(tint.r, tint.g, tint.b, img.color.a);
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
            case "Ink Needle":
            case "Smudge":
            case "Rage Mark":
            case "Astral Nuke":
            case "Vermillion Spear":
            case "Bleed Out":
            case "Void Siphon":
            case "Full Palette":
                return true;

            // AoE attacks (call once per play)
            case "Red Splatter":
            case "Y-Spray":
            case "Bloodbath":
            case "The Nothing":
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

} //Hi! If you're seeing this, you've reached the end, my friend!