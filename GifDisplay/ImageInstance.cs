using UnityEngine;
using UnityEngine.UI;

namespace GifDisplay;

public class ImageInstance
{
  public GameObject GameObject;
  public Display Display;
  public readonly SettingsData Settings;
  public string PosXStr;
  public string PosYStr;
  public string RotationStr;
  public string ScaleStr;
  public string OpacityStr;
  public bool ConfirmDelete;
  public string SortingOrderStr;

  // Cached components to avoid repeated GetComponent calls
  public RectTransform RectTransform;
  public RawImage RawImage;
  public Canvas ChildCanvas;

  // Event handler reference for proper unsubscription
  public System.Action GifLoadedHandler;

  // Lazy loading state
  public bool IsLoaded;

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