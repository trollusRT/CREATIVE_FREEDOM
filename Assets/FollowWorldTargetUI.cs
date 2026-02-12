using UnityEngine;

public class FollowWorldTargetUI : MonoBehaviour
{
    public Transform target;               // the Player or a HUDAnchor child
    public Vector3 worldOffset = new Vector3(0, 1.2f, 0); // adjust above head
    public Canvas rootCanvas;              // your main UI canvas
    public Camera worldCamera;             // usually Camera.main

    RectTransform self;
    RectTransform canvasRect;

    void Awake()
    {
        self = transform as RectTransform;
        if (!rootCanvas) rootCanvas = GetComponentInParent<Canvas>();
        if (rootCanvas) canvasRect = rootCanvas.transform as RectTransform;
        if (!worldCamera) worldCamera = Camera.main;
    }

    void LateUpdate()
    {
        if (!target || !canvasRect) return;

        // Convert world -> screen
        Vector3 worldPos = target.position + worldOffset;
        Vector3 screen = worldCamera ? worldCamera.WorldToScreenPoint(worldPos) : Vector3.zero;

        // Convert screen -> canvas local
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect, screen, rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : worldCamera,
            out Vector2 localPoint))
        {
            self.anchoredPosition = localPoint;
        }
    }
}
