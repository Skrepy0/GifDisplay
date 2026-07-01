using UnityEngine;

public class CanvasSizeListener : MonoBehaviour
{
  public System.Action OnSizeChanged;

  private RectTransform rectTransform;
  private Vector2 lastSize;

  void Awake()
  {
    rectTransform = GetComponent<RectTransform>();
    if (rectTransform == null)
      rectTransform = gameObject.AddComponent<RectTransform>();
    lastSize = rectTransform.rect.size;
  }

  void OnRectTransformDimensionsChange()
  {
    Vector2 currentSize = rectTransform.rect.size;
    if (currentSize != lastSize)
    {
      lastSize = currentSize;
      OnSizeChanged?.Invoke();
    }
  }
}