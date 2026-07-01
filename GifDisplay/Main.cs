using UnityModManagerNet;
using UnityEngine;
using UnityEngine.UI;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace GifDisplay
{
  public class Main
  {
    private static UnityModManager.ModEntry modEntry;
    private static GameObject canvasObject;
    private static Canvas canvas;
    private static CanvasScaler canvasScaler;
    private static readonly List<ImageInstance> Instances = new();
    private static string settingsPath;
    private static string newImagePath = "";

    private static float cachedLogicalWidth;
    private static float cachedLogicalHeight;
    private static bool needUpdate;

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

      LoadSettings();

      UpdateCachedLogicalSize();
      UpdateAllInstances();

      modEntry.Logger.Log($"GifDisplay Mod loaded with {Instances.Count} instances");
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
      if (needUpdate)
      {
        needUpdate = false;
        UpdateCachedLogicalSize();
        UpdateAllInstances();
      }
    }

    private static bool OnUnload(UnityModManager.ModEntry entry)
    {
      foreach (var inst in Instances)
        if (inst.GameObject != null)
          Object.Destroy(inst.GameObject);
      Instances.Clear();
      if (canvasObject != null)
        Object.Destroy(canvasObject);
      return true;
    }

    // ---------- GUI ----------
    private static void OnGUI(UnityModManager.ModEntry entry)
    {
      if (!modEntry.Active) return;

      GUILayout.BeginVertical("box", GUILayout.Width(2000));

      GUILayout.Label("Add New Image", GUILayout.Width(500));
      GUILayout.BeginHorizontal();
      GUILayout.Label("Path:", GUILayout.Width(150));
      newImagePath = GUILayout.TextField(newImagePath, GUILayout.Width(600));

      if (GUILayout.Button("Add", GUILayout.Width(150)))
      {
        if (!string.IsNullOrEmpty(newImagePath))
        {
          if (newImagePath.StartsWith('"'))
          {
            newImagePath = newImagePath.Substring(1);
          }

          if (newImagePath.EndsWith('"'))
          {
            newImagePath = newImagePath.Substring(0, newImagePath.Length - 1);
          }
        }

        if (!string.IsNullOrEmpty(newImagePath) && File.Exists(newImagePath))
        {
          var data = new SettingsData
          {
            PicGifPath = newImagePath,
            PosX = 0f,
            PosY = 0f,
            Scale = 1f,
            Opacity = 1f,
            SortingOrder = 9
          };
          if (Instances.Count > 0)
          {
            data.PosX = Mathf.Clamp(Instances[Instances.Count - 1].Settings.PosX + 10, -100, 100);
            data.PosY = Mathf.Clamp(Instances[Instances.Count - 1].Settings.PosY + 10, -100, 100);
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
      for (int i = 0; i < Instances.Count; i++)
      {
        var inst = Instances[i];
        var settings = inst.Settings;
        bool changed = false;

        GUILayout.BeginVertical("box");
        GUILayout.Label($"Image #{i + 1}");

        // 路径 + 重载
        GUILayout.BeginHorizontal();
        GUILayout.Label("Path:", GUILayout.Width(100));
        GUILayout.Label(settings.PicGifPath, GUILayout.Width(750));
        if (GUILayout.Button("Reload", GUILayout.Width(150)))
        {
          inst.Display.localPath = settings.PicGifPath;
          inst.Display.Reload(true);
          inst.ConfirmDelete = false;
        }

        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Preview:", GUILayout.Width(120));
        Texture tex = null;
        if (inst.Display != null && inst.Display.rawImage != null)
          tex = inst.Display.rawImage.texture;
        if (tex != null)
        {
          GUILayout.Box(new GUIContent(tex), GUILayout.Width(100), GUILayout.Height(100));
        }
        else
        {
          GUILayout.Box("No Image", GUILayout.Width(100), GUILayout.Height(100));
        }

        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        // X
        GUILayout.BeginHorizontal();
        GUILayout.Label("X (%)", GUILayout.Width(150));
        float newX = GUILayout.HorizontalSlider(settings.PosX, -100f, 100f, GUILayout.Width(850));
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
        float newY = GUILayout.HorizontalSlider(settings.PosY, -100f, 100f, GUILayout.Width(850));
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
        float newScale = GUILayout.HorizontalSlider(settings.Scale, 0.1f, 3f, GUILayout.Width(850));
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
        float newOpacity = GUILayout.HorizontalSlider(settings.Opacity, 0f, 1f, GUILayout.Width(850));
        if (newOpacity != settings.Opacity)
        {
          settings.Opacity = newOpacity;
          inst.OpacityStr = newOpacity.ToString("F2");
          changed = true;
        }

        GUILayout.Label(inst.OpacityStr, GUILayout.Width(100));
        GUILayout.EndHorizontal();

        // Sorting Order (独立)
        GUILayout.BeginHorizontal();
        GUILayout.Label("Sorting Order", GUILayout.Width(250));
        string newSortStr = GUILayout.TextField(inst.SortingOrderStr, GUILayout.Width(100));
        if (newSortStr != inst.SortingOrderStr)
        {
          if (int.TryParse(newSortStr, out int newSort))
          {
            settings.SortingOrder = newSort;
            inst.SortingOrderStr = newSortStr;
            UpdateInstanceSorting(inst);
            changed = true;
          }
          else
          {
            inst.SortingOrderStr = settings.SortingOrder.ToString();
          }
        }

        GUILayout.Label("(higher = in front)", GUILayout.Width(550));
        GUILayout.EndHorizontal();

        // 删除
        if (changed)
          inst.ConfirmDelete = false;

        Color oldColor = GUI.backgroundColor;
        GUI.backgroundColor = Color.red;

        string deleteText = inst.ConfirmDelete ? "Confirm?" : "Delete";
        if (GUILayout.Button(deleteText, GUILayout.Width(200)))
        {
          if (inst.ConfirmDelete)
          {
            if (inst.GameObject != null)
              Object.Destroy(inst.GameObject);
            Instances.RemoveAt(i);
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
      canvas = canvasObject.AddComponent<Canvas>();
      if (canvas == null)
      {
        modEntry.Logger.Log("Failed to add Canvas");
        return false;
      }

      canvas.renderMode = RenderMode.ScreenSpaceOverlay;

      canvasScaler = canvasObject.AddComponent<CanvasScaler>();
      canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
      canvasScaler.referenceResolution = new Vector2(1920, 1080);
      canvasScaler.matchWidthOrHeight = 0.5f;

      // 尺寸监听
      var listener = canvasObject.AddComponent<CanvasSizeListener>();
      listener.OnSizeChanged += () => { needUpdate = true; };

      Object.DontDestroyOnLoad(canvasObject);
      return true;
    }

    private static void UpdateCachedLogicalSize()
    {
      if (canvas == null) return;
      RectTransform rt = canvas.GetComponent<RectTransform>();
      if (rt != null)
      {
        cachedLogicalWidth = rt.rect.width;
        cachedLogicalHeight = rt.rect.height;
      }
      else
      {
        cachedLogicalWidth = Screen.width / canvas.scaleFactor;
        cachedLogicalHeight = Screen.height / canvas.scaleFactor;
      }
    }

    // ---------- 创建实例 ----------
    private static void CreateInstance(SettingsData data)
    {
      if (canvasObject == null) return;

      var inst = new ImageInstance(data);

      var go = new GameObject("GifImage");
      go.transform.SetParent(canvasObject.transform, false);

      // 子 Canvas（独立排序）
      var childCanvas = go.AddComponent<Canvas>();
      childCanvas.overrideSorting = true;
      childCanvas.sortingOrder = data.SortingOrder;

      var rect = go.GetComponent<RectTransform>();
      if (rect == null)
        rect = go.AddComponent<RectTransform>();
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

      Instances.Add(inst);
    }

    // ---------- 更新实例排序 ----------
    private static void UpdateInstanceSorting(ImageInstance inst)
    {
      if (inst.GameObject == null) return;
      var childCanvas = inst.GameObject.GetComponent<Canvas>();
      if (childCanvas != null)
        childCanvas.sortingOrder = inst.Settings.SortingOrder;
    }

    // ---------- 变换更新 ----------
    private static void UpdateInstanceTransform(ImageInstance inst)
    {
      if (ReferenceEquals(inst.GameObject, null) || ReferenceEquals(inst.Display, null)) return;

      var rect = inst.GameObject.GetComponent<RectTransform>();
      var rawImage = inst.GameObject.GetComponent<RawImage>();

      rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);

      float percentX = inst.Settings.PosX / 100f;
      float percentY = inst.Settings.PosY / 100f;

      float xOffset = (percentX - 0.5f) * cachedLogicalWidth;
      float yOffset = (percentY - 0.5f) * cachedLogicalHeight;
      rect.anchoredPosition = new Vector2(xOffset, yOffset);

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
      foreach (var inst in Instances)
        UpdateInstanceTransform(inst);
    }

    // ---------- 保存/加载 ----------
    private static void SaveSettings()
    {
      if (string.IsNullOrEmpty(settingsPath)) return;

      var list = new List<SettingsData>();
      foreach (var inst in Instances)
        list.Add(inst.Settings);

      string json = JsonConvert.SerializeObject(list, Formatting.Indented);
      File.WriteAllText(settingsPath, json);
    }

    private static void LoadSettings()
    {
      if (!File.Exists(settingsPath)) return;
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
      catch (System.Exception ex)
      {
        modEntry.Logger.Log($"LoadSettings error: {ex.Message}");
      }
    }

    public static void ClearAll()
    {
      foreach (var inst in Instances)
        if (inst.GameObject != null)
          Object.Destroy(inst.GameObject);
      Instances.Clear();
    }
  }
}