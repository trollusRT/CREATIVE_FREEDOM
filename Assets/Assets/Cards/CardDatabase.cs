using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "CardDatabase", menuName = "Cards/Card Database")]
public class CardDatabase : ScriptableObject
{
    public List<CardData> allCards;
}