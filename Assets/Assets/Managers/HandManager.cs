using UnityEngine;
using System.Collections.Generic;
using DG.Tweening;
using extOSC;

public class HandManager : MonoBehaviour
{
    [Header("References")]
    public CardDatabase cardDatabase;
    public GameObject cardPrefab;
    public RectTransform handPanel;
    public RectTransform dragLayerTransform;
    public Animator fAnimator;
    public Animator pAnimator;
    public OSCTransmitter oscTransmit;

    [Header("Layout Settings")]
    public float fanAngle = 90f;    // total arc in degrees
    public float radius = 600f;     // distance from center
    public Vector2 centerOffset = Vector2.zero;

    [Header("Tweening")]
    public float tweenDuration = 0.25f;
    public Ease tweenEase = Ease.OutQuart;

    public List<GameObject> currentHand = new List<GameObject>();
    private int handSize = 6;

    public void DrawHand()
    {
        ClearHand();

        // 1) Filter out only cost-1 cards
        List<CardData> costOneCards = cardDatabase.allCards.FindAll(c => c.cardCost == 1);

        // 2) If no cost-1 cards exist, handle gracefully
        if (costOneCards.Count == 0)
        {
            Debug.LogWarning("No cost=1 cards found in the database!");
            return;
        }

        for (int i = 0; i < handSize; i++)
        {
            // 3) Pick a random cost-one card
            CardData randomData = costOneCards[Random.Range(0, costOneCards.Count)];

            // 4) Instantiate the card
            var cardObj = Instantiate(cardPrefab, handPanel);
            var card = cardObj.GetComponent<Card>();

            card.dragLayer = dragLayerTransform;
            card.cardData = randomData;
            card.UpdateCardVisuals();
            card.handManager = this;
            card.fieldSpellAnimator = fAnimator;
            card.playerSpellAnimator = pAnimator;
            card.oscTransmitter = oscTransmit;

            currentHand.Add(cardObj);
        }

        // 5) Fan them out
        PositionCardsInSemiCircle();
    }

    public void DiscardHand()
    {
        ClearHand();
    }

    public int RemoveCard(GameObject card)
    {
        int index = currentHand.IndexOf(card);
        if (index >= 0)
        {
            currentHand.RemoveAt(index);
            PositionCardsInSemiCircle();
        }
        return index;
    }

    public void InsertCard(GameObject card, int index)
    {
        if (index < 0) index = 0;
        if (index > currentHand.Count) index = currentHand.Count;

        currentHand.Insert(index, card);
        PositionCardsInSemiCircle();
    }

    private void ClearHand()
    {
        foreach (var cardObj in currentHand)
        {
            Destroy(cardObj);
        }
        currentHand.Clear();
    }

    public void PositionCardsInSemiCircle()
    {
        int n = currentHand.Count;
        if (n == 0) return;

        float angleRad = fanAngle * Mathf.Deg2Rad;

        for (int i = 0; i < n; i++)
        {
            // Map i to 0..1; for n==1 center at 0.5 => angle 0
            float t = (n == 1) ? 0.5f : (float)i / (n - 1);
            float currentAngle = Mathf.Lerp(-angleRad / 2f, angleRad / 2f, t);

            float xPos = radius * -Mathf.Sin(currentAngle);
            float yPos = radius * Mathf.Cos(currentAngle);

            RectTransform cardRect = currentHand[i].GetComponent<RectTransform>();
            Vector2 targetPos = new Vector2(xPos, yPos) + centerOffset;
            float angleDeg = currentAngle * Mathf.Rad2Deg;   // <-- define it here

            // --- If NOT using DOTween, just set directly:
            cardRect.anchoredPosition = targetPos;
            cardRect.localRotation = Quaternion.Euler(0f, 0f, angleDeg);

            // --- If you ARE using DOTween, replace the two lines above with:
            // cardRect.DOAnchorPos(targetPos, tweenDuration).SetEase(tweenEase);
            // cardRect.DORotate(new Vector3(0f, 0f, angleDeg), tweenDuration, RotateMode.Fast);

            currentHand[i].transform.SetSiblingIndex(i);
        }
    }

    public GameObject SpawnCard(CardData data)
    {
        var cardObj = Instantiate(cardPrefab, handPanel);
        var card = cardObj.GetComponent<Card>();
        card.dragLayer = dragLayerTransform;
        card.cardData = data;
        card.UpdateCardVisuals();
        card.handManager = this;
        card.fieldSpellAnimator = fAnimator;
        card.playerSpellAnimator = pAnimator;

        currentHand.Add(cardObj);
        PositionCardsInSemiCircle();
        return cardObj;
    }

    // Optional helper for removing a specific Card (component)
    public void RemoveCard(Card card)
    {
        if (!card) return;
        int idx = currentHand.IndexOf(card.gameObject);
        if (idx >= 0) currentHand.RemoveAt(idx);
        Destroy(card.gameObject);
        PositionCardsInSemiCircle();
    }


}
