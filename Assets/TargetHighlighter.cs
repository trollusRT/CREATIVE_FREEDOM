using UnityEngine;

public class TargetHighlighter : MonoBehaviour
{
    public GameObject highlightGO; // child with SpriteRenderer/outline
    public void SetHighlighted(bool on)
    {
        if (highlightGO) highlightGO.SetActive(on);
    }
}