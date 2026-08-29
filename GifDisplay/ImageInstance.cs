using System;
using UnityEngine;
using UnityEngine.UI;

namespace GifDisplay;

public class ImageInstance
{
  public readonly SettingsData Settings;
  public Canvas ChildCanvas;
  public bool ConfirmDelete;
  public Display Display;
  public GameObject GameObject;

  // Event handler reference for proper unsubscription
  public Action GifLoadedHandler;

  // Lazy loading state
  public bool IsLoaded;
  public string OpacityStr;
  public string PosXStr;
  public string PosYStr;
  public RawImage RawImage;

  // Cached components to avoid repeated GetComponent calls
  public RectTransform RectTransform;
  public string RotationStr;
  public string ScaleStr;
  public string SortingOrderStr;

  public ImageInstance(SettingsData data)
  {
    Settings = data;
    UpdateStrings();
  }

  public void CacheComponents()
  {
    if (GameObject != null)
    {
      RectTransform = GameObject.GetComponent<RectTransform>();
      RawImage = GameObject.GetComponent<RawImage>();
      ChildCanvas = GameObject.GetComponent<Canvas>();
      Display = GameObject.GetComponent<Display>();
    }
  }

  public void UpdateStrings()
  {
    PosXStr = Settings.PosX.ToString("F1") + "%";
    PosYStr = Settings.PosY.ToString("F1") + "%";
    RotationStr = Settings.Rotation.ToString("F1") + "°";
    ScaleStr = Settings.Scale.ToString("F2");
    OpacityStr = Settings.Opacity.ToString("F2");
    SortingOrderStr = Settings.SortingOrder.ToString();
  }
}