using System;
using UnityEngine;

namespace GifDisplay;

public class CanvasSizeListener : MonoBehaviour
{
  private Vector2 lastSize;
  public Action OnSizeChanged;

  private RectTransform rectTransform;

  private void Awake()
  {
    rectTransform = GetComponent<RectTransform>();
    if (rectTransform == null)
      rectTransform = gameObject.AddComponent<RectTransform>();
    lastSize = rectTransform.rect.size;
  }

  private void OnRectTransformDimensionsChange()
  {
    var currentSize = rectTransform.rect.size;
    if (currentSize != lastSize)
    {
      lastSize = currentSize;
      OnSizeChanged?.Invoke();
    }
  }
}