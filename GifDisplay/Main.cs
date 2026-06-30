using UnityModManagerNet;
using UnityEngine;
using UnityEngine.UI;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GifDisplay
{
  public class SettingsData
  {
    public float PosX;
    public float PosY;
    public float Scale = 1.0f;
    public float Opacity = 1.0f;
    public string PicGifPath = "";
  }

  public class ImageInstance
  {
    public GameObject GameObject;
    public Display Display;
    public SettingsData Settings;
    public string PosXStr;
    public string PosYStr;
    public string ScaleStr;
    public string OpacityStr;
    public bool ConfirmDelete;

    public ImageInstance(SettingsData data)
    {
      Settings = data;
      UpdateStrings();
    }

    public void UpdateStrings()
    {
      PosXStr = Settings.PosX.ToString("F1") + "%";
      PosYStr = Settings.PosY.ToString("F1") + "%";
      ScaleStr = Settings.Scale.ToString("F2");
      OpacityStr = Settings.Opacity.ToString("F2");
    }
  }

  public class Main
  {
    private static UnityModManager.ModEntry modEntry;
    private static GameObject canvasObject;
    private static List<ImageInstance> instances = new();
    private static string settingsPath;
    private static string newImagePath = "";
    private static int lastScreenWidth;
    private static int lastScreenHeight;

    private static int sortingOrder = 9;
    private static string sortingOrderStr = "9";

    public static bool Load(UnityModManager.ModEntry entry)
    {
      modEntry = entry;
      settingsPath = Path.Combine(entry.Path, "settings.json");

      Display.LogErrorCallback = (msg) => modEntry.Logger.Log(msg);

      entry.OnToggle = OnToggle;
      entry.OnGUI = OnGUI;
      entry.OnUpdate = OnUpdate;
      entry.OnUnload = OnUnload;

      if (!CreateCanvas())
      {
        modEntry.Logger.Log("Failed to create Canvas");
        return false;
      }

      lastScreenWidth = Screen.width;
      lastScreenHeight = Screen.height;

      LoadSettings();

      if (instances.Count == 0)
      {
        var defaultData = new SettingsData { PicGifPath = "", PosX = 0, PosY = 0, Scale = 1f, Opacity = 1f };
        CreateInstance(defaultData);
        SaveSettings();
      }

      // 应用排序
      ApplySortingOrder();

      modEntry.Logger.Log($"GifDisplay Mod loaded with {instances.Count} instances");
      return true;
    }

    private static bool OnToggle(UnityModManager.ModEntry entry, bool enable)
    {
      if (canvasObject != null)
        canvasObject.SetActive(enable);
      return true;
    }

    private static void OnUpdate(UnityModManager.ModEntry entry, float delta)
    {
      if (Screen.width != lastScreenWidth || Screen.height != lastScreenHeight)
      {
        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;
        UpdateAllInstances();
      }
    }

    private static bool OnUnload(UnityModManager.ModEntry entry)
    {
      foreach (var inst in instances)
        if (inst.GameObject != null)
          Object.Destroy(inst.GameObject);
      instances.Clear();
      if (canvasObject != null)
        Object.Destroy(canvasObject);
      return true;
    }

    // ---------- GUI ----------
    private static void OnGUI(UnityModManager.ModEntry entry)
    {
      if (!modEntry.Active) return;

      GUILayout.BeginVertical("box", GUILayout.Width(2000));

      GUILayout.BeginHorizontal();
      GUILayout.Label("Sorting Order", GUILayout.Width(200));
      string newOrderStr = GUILayout.TextField(sortingOrderStr, GUILayout.Width(150));
      if (newOrderStr != sortingOrderStr)
      {
        if (int.TryParse(newOrderStr, out int newOrder))
        {
          sortingOrder = newOrder;
          sortingOrderStr = newOrderStr;
          ApplySortingOrder();
          SaveSettings();
        }
        else
        {
          sortingOrderStr = sortingOrder.ToString();
        }
      }

      GUILayout.Label("(negative = behind, positive = in front)", GUILayout.Width(300));
      GUILayout.EndHorizontal();

      GUILayout.Label("Add New Image", GUILayout.Width(500));
      GUILayout.BeginHorizontal();
      GUILayout.Label("Path:", GUILayout.Width(150));
      newImagePath = GUILayout.TextField(newImagePath, GUILayout.Width(600));
      if (GUILayout.Button("Add", GUILayout.Width(150)))
      {
        if (!string.IsNullOrEmpty(newImagePath) && File.Exists(newImagePath))
        {
          var data = new SettingsData
          {
            PicGifPath = newImagePath,
            PosX = 0f,
            PosY = 0f,
            Scale = 1f,
            Opacity = 1f
          };
          if (instances.Count > 0)
          {
            data.PosX = Mathf.Clamp(instances[instances.Count - 1].Settings.PosX + 10, -100, 100);
            data.PosY = Mathf.Clamp(instances[instances.Count - 1].Settings.PosY + 10, -100, 100);
          }

          CreateInstance(data);
          SaveSettings();
          newImagePath = "";
        }
        else
        {
          modEntry.Logger.Log("Invalid file path: " + newImagePath);
        }
      }

      GUILayout.EndHorizontal();

      // ---- 图片列表 ----
      for (int i = 0; i < instances.Count; i++)
      {
        var inst = instances[i];
        var settings = inst.Settings;
        bool changed = false;

        GUILayout.BeginVertical("box");
        GUILayout.Label($"Image #{i + 1}");

        // 路径 + 重载
        GUILayout.BeginHorizontal();
        GUILayout.Label("Path:", GUILayout.Width(100));
        GUILayout.Label(settings.PicGifPath, GUILayout.Width(650));
        if (GUILayout.Button("Reload", GUILayout.Width(150)))
        {
          inst.Display.localPath = settings.PicGifPath;
          inst.Display.Reload(true);
          inst.ConfirmDelete = false;
        }

        GUILayout.EndHorizontal();

        // X
        GUILayout.BeginHorizontal();
        GUILayout.Label("X (%)", GUILayout.Width(150));
        float newX = GUILayout.HorizontalSlider(settings.PosX, -100f, 100f, GUILayout.Width(550));
        if (newX != settings.PosX)
        {
          settings.PosX = newX;
          inst.PosXStr = newX.ToString("F1") + "%";
          changed = true;
        }

        GUILayout.Label(inst.PosXStr, GUILayout.Width(120));
        GUILayout.EndHorizontal();

        // Y
        GUILayout.BeginHorizontal();
        GUILayout.Label("Y (%)", GUILayout.Width(150));
        float newY = GUILayout.HorizontalSlider(settings.PosY, -100f, 100f, GUILayout.Width(550));
        if (newY != settings.PosY)
        {
          settings.PosY = newY;
          inst.PosYStr = newY.ToString("F1") + "%";
          changed = true;
        }

        GUILayout.Label(inst.PosYStr, GUILayout.Width(120));
        GUILayout.EndHorizontal();

        // Scale
        GUILayout.BeginHorizontal();
        GUILayout.Label("Scale", GUILayout.Width(150));
        float newScale = GUILayout.HorizontalSlider(settings.Scale, 0.1f, 3f, GUILayout.Width(550));
        if (newScale != settings.Scale)
        {
          settings.Scale = newScale;
          inst.ScaleStr = newScale.ToString("F2");
          changed = true;
        }

        GUILayout.Label(inst.ScaleStr, GUILayout.Width(80));
        GUILayout.EndHorizontal();

        // Opacity
        GUILayout.BeginHorizontal();
        GUILayout.Label("Opacity", GUILayout.Width(150));
        float newOpacity = GUILayout.HorizontalSlider(settings.Opacity, 0f, 1f, GUILayout.Width(550));
        if (newOpacity != settings.Opacity)
        {
          settings.Opacity = newOpacity;
          inst.OpacityStr = newOpacity.ToString("F2");
          changed = true;
        }

        GUILayout.Label(inst.OpacityStr, GUILayout.Width(100));
        GUILayout.EndHorizontal();

        // 删除
        if (changed)
          inst.ConfirmDelete = false;

        Color oldColor = GUI.backgroundColor;
        GUI.backgroundColor = Color.red;

        string deleteText = inst.ConfirmDelete ? "Confirm?" : "Delete";
        if (GUILayout.Button(deleteText, GUILayout.Width(150)))
        {
          if (inst.ConfirmDelete)
          {
            if (inst.GameObject != null)
              Object.Destroy(inst.GameObject);
            instances.RemoveAt(i);
            SaveSettings();
            GUILayout.EndVertical();
            break;
          }

          inst.ConfirmDelete = true;
        }

        GUI.backgroundColor = oldColor;

        GUILayout.EndVertical();

        if (changed)
        {
          UpdateInstanceTransform(inst);
          SaveSettings();
        }
      }

      GUILayout.EndVertical();
    }

    // ---------- 创建 Canvas ----------
    private static bool CreateCanvas()
    {
      if (canvasObject != null) return true;

      canvasObject = new GameObject("GifDisplayCanvas");
      canvasObject.SetActive(true);
      var canvas = canvasObject.AddComponent<Canvas>();
      if (canvas == null)
      {
        modEntry.Logger.Log("Failed to add Canvas");
        return false;
      }

      canvas.renderMode = RenderMode.ScreenSpaceOverlay;
      // 排序将在 ApplySortingOrder 中设置

      var scaler = canvasObject.AddComponent<CanvasScaler>();
      scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
      scaler.referenceResolution = new Vector2(1920, 1080);
      scaler.matchWidthOrHeight = 0.5f;

      Object.DontDestroyOnLoad(canvasObject);
      return true;
    }

    // ---------- 应用排序 ----------
    private static void ApplySortingOrder()
    {
      if (canvasObject == null) return;
      var canvas = canvasObject.GetComponent<Canvas>();
      if (canvas != null)
      {
        canvas.sortingOrder = sortingOrder;
        modEntry.Logger.Log($"Applied sortingOrder = {sortingOrder}");
      }
    }

    // ---------- 创建实例 ----------
    private static void CreateInstance(SettingsData data)
    {
      if (canvasObject == null) return;

      var inst = new ImageInstance(data);

      var go = new GameObject("GifImage");
      go.transform.SetParent(canvasObject.transform, false);
      var rect = go.AddComponent<RectTransform>();
      rect.anchorMin = new Vector2(0.5f, 0.5f);
      rect.anchorMax = new Vector2(0.5f, 0.5f);
      rect.pivot = new Vector2(0.5f, 0.5f);

      var rawImage = go.AddComponent<RawImage>();
      rawImage.raycastTarget = false;

      var display = go.AddComponent<Display>();
      display.rawImage = rawImage;

      inst.GameObject = go;
      inst.Display = display;

      display.OnGifLoaded += () => UpdateInstanceTransform(inst);

      display.localPath = data.PicGifPath;
      display.Reload(true);

      UpdateInstanceTransform(inst);
      inst.GameObject.SetActive(true);

      instances.Add(inst);
    }

    // ---------- 变换更新 ----------
    private static void UpdateInstanceTransform(ImageInstance inst)
    {
      if (ReferenceEquals(inst.GameObject, null) || ReferenceEquals(inst.Display, null)) return;

      var rect = inst.GameObject.GetComponent<RectTransform>();
      var rawImage = inst.GameObject.GetComponent<RawImage>();

      float halfWidth = Screen.width / 2f;
      float halfHeight = Screen.height / 2f;
      float xPos = (inst.Settings.PosX / 100f) * halfWidth;
      float yPos = (inst.Settings.PosY / 100f) * halfHeight;
      rect.anchoredPosition = new Vector2(xPos, yPos);

      if (inst.Display.gifWidth > 0 && inst.Display.gifHeight > 0)
      {
        float width = inst.Display.gifWidth * inst.Settings.Scale;
        float height = inst.Display.gifHeight * inst.Settings.Scale;
        rect.sizeDelta = new Vector2(width, height);
      }
      else
      {
        rect.sizeDelta = new Vector2(150 * inst.Settings.Scale, 150 * inst.Settings.Scale);
      }

      if (!ReferenceEquals(rawImage, null))
      {
        Color c = rawImage.color;
        c.a = inst.Settings.Opacity;
        rawImage.color = c;
      }
    }

    private static void UpdateAllInstances()
    {
      foreach (var inst in instances)
        UpdateInstanceTransform(inst);
    }

    // ---------- 保存/加载（支持新格式） ----------
    private static void SaveSettings()
    {
      if (string.IsNullOrEmpty(settingsPath)) return;

      JObject root = new JObject();
      root["sortingOrder"] = sortingOrder;

      var list = new List<SettingsData>();
      foreach (var inst in instances)
        list.Add(inst.Settings);

      string instancesJson = JsonConvert.SerializeObject(list, Formatting.Indented);
      root["instances"] = JArray.Parse(instancesJson);

      File.WriteAllText(settingsPath, root.ToString(Formatting.Indented));
    }

    private static void LoadSettings()
    {
      if (!File.Exists(settingsPath)) return;
      try
      {
        string json = File.ReadAllText(settingsPath);
        JObject root = JObject.Parse(json);

        // 读取排序
        if (root.TryGetValue("sortingOrder", out JToken orderToken))
        {
          sortingOrder = orderToken.Value<int>();
          sortingOrderStr = sortingOrder.ToString();
        }

        // 读取实例列表
        if (root.TryGetValue("instances", out JToken instancesToken))
        {
          var list = instancesToken.ToObject<List<SettingsData>>();
          if (list != null)
          {
            foreach (var data in list)
              CreateInstance(data);
          }
        }
      }
      catch (System.Exception ex)
      {
        modEntry.Logger.Log($"LoadSettings error: {ex.Message}");
        try
        {
          string json = File.ReadAllText(settingsPath);
          var list = JsonConvert.DeserializeObject<List<SettingsData>>(json);
          if (list != null)
          {
            foreach (var data in list)
              CreateInstance(data);
          }
        }
        catch
        {
          modEntry.Logger.Log("Failed to load settings with old format.");
        }
      }
    }

    public static void ClearAll()
    {
      foreach (var inst in instances)
        if (inst.GameObject != null)
          Object.Destroy(inst.GameObject);
      instances.Clear();
    }
  }
}