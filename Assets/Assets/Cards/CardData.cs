using UnityEngine;

[CreateAssetMenu(fileName = "NewCardData", menuName = "Cards/Card Data")]
public class CardData : ScriptableObject
{
    public string cardName;
    public string cardType;
    [TextArea] public string cardEffect;
    public int cardCost;
    public int minValue;
    public int maxValue;
    public bool canTargetPlayer;
    public bool canTargetEnemy;
    public bool isConditional;

    [Header("Effect Routing (Optional)")]
    [Tooltip("Optional. If set, Card.cs will try to resolve gameplay using CardEffectRegistry instead of the name-based switch.")]
    public string effectId;

    [Tooltip("Optional. If set, EffectDirector plays this visual key (ST/AoE based on effectId / target).")]
    public EffectKey effectKey = EffectKey.None;

    [Header("Art")]
    public Sprite cardSprite;

    [Header("Audio")]
    public AudioClip sfx;          // <-- assign per card in Inspector
    [Range(0f, 1f)] public float sfxVolume = 1f;
}