using System;
using UnityModManagerNet;
using UnityEngine;
using UnityEngine.UI;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Object = UnityEngine.Object;

namespace GifDisplay
{
  public class Main
  {
    public const string ModId = "GifDisplay";
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

    private static bool isGamePlaying;
    private static FieldInfo gameplayField;
    private static bool reflectionFailed;

    private static bool loading;
    private static bool isReloading;

    // 控制器缓存与查找优化
    private static scrController cachedController;
    private static int findControllerFrameCounter;
    private static bool controllerNotFound;
    private static int stateUpdateCounter;

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

      I18n.Load(entry.Path);

      loading = true;
      LoadSettings();
      loading = false;

      UpdateCachedLogicalSize();
      UpdateAllInstances();

      UpdateGamePlayState();
      ApplyVisibilityRules();

      modEntry.Logger.Log($"Initial game playing state: {isGamePlaying}, instances: {Instances.Count}");
      modEntry.Logger.Log($"GifDisplay Mod loaded with {Instances.Count} instances");
      return true;
    }

    private static bool OnToggle(UnityModManager.ModEntry entry, bool enable)
    {
      if (canvasObject != null)
      {
        canvasObject.SetActive(enable);
        modEntry.Logger.Log($"Canvas toggled to {enable}");
      }

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

      stateUpdateCounter++;
      if (stateUpdateCounter % 5 == 0)
      {
        UpdateGamePlayState();
        ApplyVisibilityRules();
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

      // ================= 语言切换 =================
      GUILayout.BeginHorizontal();
      GUILayout.Label("Language:", GUILayout.Width(150));
      string[] langs = { "en", "zh", "ko" };
      string[] langNames = { "English", "中文", "한국어" };
      int idx = Array.IndexOf(langs, I18n.Lang);
      if (idx < 0) idx = 0;
      int newIdx = GUILayout.SelectionGrid(idx, langNames, 3, GUILayout.Width(600));
      if (newIdx != idx)
      {
        I18n.Lang = langs[newIdx];
        SaveSettings();
      }

      GUILayout.FlexibleSpace();
      GUILayout.EndHorizontal();

      // ================= 添加新图片 =================
      GUILayout.Label(I18n.Tr("add_new_image"), GUILayout.Width(500));
      GUILayout.BeginHorizontal();
      GUILayout.Label(I18n.Tr("path"), GUILayout.Width(150));
      newImagePath = GUILayout.TextField(newImagePath, GUILayout.Width(600));

      if (GUILayout.Button(I18n.Tr("add"), GUILayout.Width(150)))
      {
        if (!string.IsNullOrEmpty(newImagePath))
        {
          if (newImagePath.StartsWith('"'))
            newImagePath = newImagePath.Substring(1);
          if (newImagePath.EndsWith('"'))
            newImagePath = newImagePath.Substring(0, newImagePath.Length - 1);
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
            SortingOrder = 9,
            ShowOnlyDuringPlay = true
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
          modEntry.Logger.Log(I18n.Tr("invalid_path") + newImagePath);
        }
      }

      GUILayout.EndHorizontal();

      // ================= 图片列表 =================
      for (int i = 0; i < Instances.Count; i++)
      {
        var inst = Instances[i];
        var settings = inst.Settings;
        bool changed = false;

        GUILayout.BeginVertical("box");
        GUILayout.Label($"{I18n.Tr("image")} #{i + 1}");

        // ---------- 路径 + 重载 ----------
        GUILayout.BeginHorizontal();
        GUILayout.Label(I18n.Tr("path"), GUILayout.Width(100));
        GUILayout.Label(settings.PicGifPath, GUILayout.Width(750));
        if (GUILayout.Button(I18n.Tr("reload"), GUILayout.Width(200)))
        {
          inst.Display.localPath = settings.PicGifPath;
          inst.Display.Reload(true);
          inst.ConfirmDelete = false;
        }

        GUILayout.EndHorizontal();

        // ---------- 预览 ----------
        GUILayout.BeginHorizontal();
        GUILayout.Label(I18n.Tr("preview"), GUILayout.Width(120));
        Texture tex = null;
        if (inst.Display != null)
          tex = inst.Display.PreviewTexture;
        if (tex != null)
          GUILayout.Box(new GUIContent(tex), GUILayout.Width(100), GUILayout.Height(100));
        else
          GUILayout.Box(I18n.Tr("no_image"), GUILayout.Width(100), GUILayout.Height(100));
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        // ---------- X ----------
        GUILayout.BeginHorizontal();
        GUILayout.Label(I18n.Tr("x_percent"), GUILayout.Width(150));
        float newX = GUILayout.HorizontalSlider(settings.PosX, -100f, 100f, GUILayout.Width(950));
        if (newX != settings.PosX)
        {
          settings.PosX = newX;
          inst.PosXStr = newX.ToString("F1") + "%";
          changed = true;
        }

        GUILayout.Label(inst.PosXStr, GUILayout.Width(120));
        GUILayout.EndHorizontal();

        // ---------- Y ----------
        GUILayout.BeginHorizontal();
        GUILayout.Label(I18n.Tr("y_percent"), GUILayout.Width(150));
        float newY = GUILayout.HorizontalSlider(settings.PosY, -100f, 100f, GUILayout.Width(950));
        if (newY != settings.PosY)
        {
          settings.PosY = newY;
          inst.PosYStr = newY.ToString("F1") + "%";
          changed = true;
        }

        GUILayout.Label(inst.PosYStr, GUILayout.Width(120));
        GUILayout.EndHorizontal();

        // ---------- Scale ----------
        GUILayout.BeginHorizontal();
        GUILayout.Label(I18n.Tr("scale"), GUILayout.Width(150));
        float newScale = GUILayout.HorizontalSlider(settings.Scale, 0.02f, 2.5f, GUILayout.Width(950));
        if (newScale != settings.Scale)
        {
          settings.Scale = newScale;
          inst.ScaleStr = newScale.ToString("F2");
          changed = true;
        }

        GUILayout.Label(inst.ScaleStr, GUILayout.Width(80));
        GUILayout.EndHorizontal();

        // ---------- Opacity ----------
        GUILayout.BeginHorizontal();
        GUILayout.Label(I18n.Tr("opacity"), GUILayout.Width(150));
        float newOpacity = GUILayout.HorizontalSlider(settings.Opacity, 0f, 1f, GUILayout.Width(950));
        if (newOpacity != settings.Opacity)
        {
          settings.Opacity = newOpacity;
          inst.OpacityStr = newOpacity.ToString("F2");
          changed = true;
        }

        GUILayout.Label(inst.OpacityStr, GUILayout.Width(100));
        GUILayout.EndHorizontal();

        // ---------- Sorting Order ----------
        GUILayout.BeginHorizontal();
        GUILayout.Label(I18n.Tr("sorting_order"), GUILayout.Width(250));
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

        GUILayout.Label(I18n.Tr("higher_in_front"), GUILayout.Width(550));
        GUILayout.EndHorizontal();

        // ---------- Show only during play ----------
        GUILayout.BeginHorizontal();
        GUILayout.Label(I18n.Tr("show_only_during_play"), GUILayout.Width(250));
        bool newShowOnly = GUILayout.Toggle(settings.ShowOnlyDuringPlay, "");
        if (newShowOnly != settings.ShowOnlyDuringPlay)
        {
          settings.ShowOnlyDuringPlay = newShowOnly;
          changed = true;
        }

        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        // ---------- 删除按钮 ----------
        if (changed)
          inst.ConfirmDelete = false;

        Color oldColor = GUI.backgroundColor;
        GUI.backgroundColor = Color.red;

        string deleteText = inst.ConfirmDelete ? I18n.Tr("confirm") : I18n.Tr("delete");
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
          ApplyVisibilityRules();
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
      go.SetActive(true);

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

      display.OnGifLoaded += () =>
      {
        UpdateInstanceTransform(inst);
        if (!loading && !isReloading)
          ApplyVisibilityRules();
        if (isReloading)
          isReloading = false;
      };

      display.localPath = data.PicGifPath;
      display.Reload(true);

      UpdateInstanceTransform(inst);
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

    // ---------- 游戏状态检测（优化版） ----------
    private static void UpdateGamePlayState()
    {
      // 如果已确定找不到控制器，每隔 30 帧重试一次
      if (controllerNotFound)
      {
        findControllerFrameCounter++;
        if (findControllerFrameCounter % 30 != 0)
          return;
        findControllerFrameCounter = 0;
      }

      bool newState = false;
      try
      {
        if (cachedController == null)
        {
          // 尝试获取 scrController
          GameObject controllerGo = GameObject.Find("scrController");
          if (controllerGo == null)
          {
            var controllerComp = Object.FindFirstObjectByType<scrController>();
            if (controllerComp != null)
              controllerGo = controllerComp.gameObject;
          }

          if (controllerGo != null)
          {
            cachedController = controllerGo.GetComponent<scrController>();
            if (cachedController == null)
            {
              controllerNotFound = true;
              return;
            }
          }
          else
          {
            controllerNotFound = true;
            return;
          }
        }

        // 反射字段查找（仅一次）
        if (gameplayField == null && !reflectionFailed)
        {
          var fields =
            typeof(scrController).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
          foreach (var f in fields)
          {
            if (f.FieldType == typeof(bool) &&
                (f.Name.ToLower().Contains("play") ||
                 f.Name.ToLower().Contains("active") ||
                 f.Name.ToLower().Contains("game")))
            {
              gameplayField = f;
              modEntry.Logger.Log($"Found gameplay field: '{f.Name}'");
              break;
            }
          }

          if (gameplayField == null)
          {
            reflectionFailed = true;
            modEntry.Logger.Log("No boolean field with 'play'/'active'/'game' found.");
            return;
          }
        }

        if (gameplayField != null)
          newState = (bool)gameplayField.GetValue(cachedController);
        else
          newState = true; // 反射失败时默认播放
      }
      catch (Exception ex)
      {
        modEntry.Logger.Log($"Error detecting game state: {ex.Message}");
        newState = false;
        // 出错时重置缓存，下次重新查找
        cachedController = null;
        controllerNotFound = false;
      }

      if (newState != isGamePlaying)
      {
        isGamePlaying = newState;
        modEntry.Logger.Log($"Game playing state changed to: {isGamePlaying}");
      }
    }

    // ---------- 应用可见性规则（优化版） ----------
    private static void ApplyVisibilityRules()
    {
      if (loading || ReferenceEquals(canvasObject, null))
        return;

      bool anyChange = false;

      // 单次遍历：计算并应用变化
      foreach (var inst in Instances)
      {
        if (ReferenceEquals(inst.GameObject, null) || ReferenceEquals(inst.Display, null))
          continue;

        bool shouldShow = true;
        if (inst.Display.isLoaded && inst.Settings.ShowOnlyDuringPlay)
        {
          shouldShow = isGamePlaying;
        }

        if (inst.GameObject.activeSelf != shouldShow)
        {
          inst.GameObject.SetActive(shouldShow);
          if (shouldShow)
            inst.Display.Resume();
          anyChange = true;
        }
      }

      // 如果发生了任何变化，输出一条汇总日志（避免刷屏）
      if (anyChange)
      {
        modEntry.Logger.Log("Visibility updated for some instances.");
      }
    }

    // ---------- 保存/加载 ----------
    private static void SaveSettings()
    {
      if (string.IsNullOrEmpty(settingsPath)) return;

      JObject root = new JObject();
      root["language"] = I18n.Lang;

      JArray instancesArray = new JArray();
      foreach (var inst in Instances)
        instancesArray.Add(JObject.FromObject(inst.Settings));
      root["instances"] = instancesArray;

      File.WriteAllText(settingsPath, root.ToString(Formatting.Indented));
    }

    private static void LoadSettings()
    {
      if (!File.Exists(settingsPath)) return;
      try
      {
        string json = File.ReadAllText(settingsPath);
        JObject root = JObject.Parse(json);

        if (root.TryGetValue("language", out JToken langToken))
        {
          string lang = langToken.Value<string>();
          if (!string.IsNullOrEmpty(lang))
            I18n.Lang = lang;
        }

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
      catch (Exception ex)
      {
        // 兼容旧格式
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
          modEntry.Logger.Log($"LoadSettings error: {ex.Message}");
        }
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