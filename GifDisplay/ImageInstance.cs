using UnityEngine;

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

  public ImageInstance(SettingsData data)
  {
    Settings = data;
    UpdateStrings();
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