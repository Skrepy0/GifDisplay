using UnityModManagerNet;
using UnityEngine;
using UnityEngine.UI;
using System.IO;
using Newtonsoft.Json;

namespace GifDisplay
{
    public class SettingsData
    {
        public float posX = 100f;
        public float posY = 100f;
        public float scale = 1.0f;
        public float opacity = 1.0f;
        public string gifPath = "";
    }

    public class Main
    {
        private static UnityModManager.ModEntry modEntry;
        private static GameObject gifObject;
        private static Display display;

        private static float posX = 100f;
        private static float posY = 100f;
        private static float scale = 1.0f;
        private static float opacity = 1.0f;
        private static string gifPath = "";
        private static string settingsPath;

        private static string posXStr = "100", posYStr = "100", scaleStr = "1.00", opacityStr = "1.00";

        public static bool Load(UnityModManager.ModEntry entry)
        {
            modEntry = entry;
            settingsPath = Path.Combine(entry.Path, "settings.json");
            LoadSettings();

            Display.LogErrorCallback = (msg) => modEntry.Logger.Log(msg);

            entry.OnToggle = OnToggle;
            entry.OnGUI = OnGUI;
            entry.OnUpdate = OnUpdate;

            if (!CreateGifUI())
            {
                modEntry.Logger.Log("Failed to create GIF UI, mod will not load.");
                return false;
            }

            display.OnGifLoaded += OnGifLoaded;

            UpdateGifTransform();
            LoadGifFromPath();

            return true;
        }

        private static bool OnToggle(UnityModManager.ModEntry entry, bool enable)
        {
            if (enable)
            {
                if (gifObject == null)
                {
                    CreateGifUI();
                    UpdateGifTransform();
                    LoadGifFromPath();
                }
                else
                {
                    gifObject.SetActive(true);
                }
            }
            else
            {
                if (gifObject != null)
                    gifObject.SetActive(false);
            }

            return true;
        }

        private static void OnUpdate(UnityModManager.ModEntry entry, float delta)
        {
        }

        private static void OnGUI(UnityModManager.ModEntry entry)
        {
            if (!modEntry.Active) return;

            bool changed = false;

            GUILayout.BeginVertical("box", GUILayout.Width(750));

            // GIF 路径
            GUILayout.BeginHorizontal();
            GUILayout.Label("GIF Path", GUILayout.Width(150));
            string newPath = GUILayout.TextField(gifPath, GUILayout.Width(280));
            if (newPath != gifPath)
            {
                gifPath = newPath;
                changed = true;
            }

            if (GUILayout.Button("Load", GUILayout.Width(150)))
            {
                if (display != null)
                {
                    display.localPath = gifPath;
                    display.Reload(true);
                }

                SaveSettings();
            }

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("X (px)", GUILayout.Width(150));
            string newXStr = GUILayout.TextField(posXStr, GUILayout.Width(100));
            if (newXStr != posXStr)
            {
                if (float.TryParse(newXStr, out float newX))
                {
                    posX = newX;
                    posXStr = posX.ToString("F0");
                    changed = true;
                }
                else
                {
                    posXStr = posX.ToString("F0");
                }
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Y (px)", GUILayout.Width(150));
            string newYStr = GUILayout.TextField(posYStr, GUILayout.Width(100));
            if (newYStr != posYStr)
            {
                if (float.TryParse(newYStr, out float newY))
                {
                    posY = newY; 
                    posYStr = posY.ToString("F0");
                    changed = true;
                }
                else
                {
                    posYStr = posY.ToString("F0");
                }
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // 缩放
            GUILayout.BeginHorizontal();
            GUILayout.Label("Scale", GUILayout.Width(150));
            float newScale = GUILayout.HorizontalSlider(scale, 0.1f, 3f, GUILayout.Width(200));
            if (newScale != scale)
            {
                scale = newScale;
                scaleStr = scale.ToString("F2");
                changed = true;
            }

            GUILayout.Label(scaleStr, GUILayout.Width(120));
            GUILayout.EndHorizontal();

            // 透明度
            GUILayout.BeginHorizontal();
            GUILayout.Label("Opacity", GUILayout.Width(150));
            float newOpacity = GUILayout.HorizontalSlider(opacity, 0f, 1f, GUILayout.Width(200));
            if (newOpacity != opacity)
            {
                opacity = newOpacity;
                opacityStr = opacity.ToString("F2");
                changed = true;
            }

            GUILayout.Label(opacityStr, GUILayout.Width(120));
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();

            if (changed)
            {
                UpdateGifTransform();
                SaveSettings();
            }
        }

        // ---------- UI 创建 ----------
        private static bool CreateGifUI()
        {
            try
            {
                if (gifObject != null) return true;

                modEntry.Logger.Log("Creating GIF UI...");

                var canvasObj = new GameObject("GifCanvas");
                canvasObj.SetActive(true);
                var canvas = canvasObj.AddComponent<Canvas>();
                if (canvas == null)
                {
                    modEntry.Logger.Log("Failed to add Canvas component");
                    return false;
                }

                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 999;
                GameObject.DontDestroyOnLoad(canvasObj);
                modEntry.Logger.Log("Canvas created");

                gifObject = new GameObject("GifImage");
                gifObject.transform.SetParent(canvasObj.transform, false);
                var rect = gifObject.GetComponent<RectTransform>();
                if (rect == null)
                {
                    rect = gifObject.AddComponent<RectTransform>();
                    if (rect == null)
                    {
                        modEntry.Logger.Log("Failed to add RectTransform component");
                        return false;
                    }
                }

                // 锚点左上角
                rect.anchorMin = new Vector2(0, 1);
                rect.anchorMax = new Vector2(0, 1);
                rect.pivot = new Vector2(0, 1);
                rect.anchoredPosition = new Vector2(posX, posY);
                rect.sizeDelta = new Vector2(150, 150);

                var rawImage = gifObject.AddComponent<RawImage>();
                if (rawImage == null)
                {
                    modEntry.Logger.Log("Failed to add RawImage component");
                    return false;
                }

                rawImage.raycastTarget = false;
                rawImage.color = new Color(1, 1, 1, opacity);

                display = gifObject.AddComponent<Display>();
                if (display == null)
                {
                    modEntry.Logger.Log("Failed to add GifDisplay component");
                    return false;
                }

                display.rawImage = rawImage;
                modEntry.Logger.Log("GifDisplay added and connected");

                gifObject.SetActive(true);
                modEntry.Logger.Log("GIF UI creation successful");
                return true;
            }
            catch (System.Exception ex)
            {
                modEntry.Logger.Log($"CreateGifUI exception: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        private static void OnGifLoaded()
        {
            UpdateGifTransform();
        }

        private static void LoadGifFromPath()
        {
            if (display != null)
            {
                display.localPath = gifPath;
                display.Reload(true);
            }
        }

        private static void UpdateGifTransform()
        {
            if (gifObject == null || display == null) return;

            var rect = gifObject.GetComponent<RectTransform>();
            var rawImage = gifObject.GetComponent<RawImage>();

            // 位置（支持负值，直接设置 anchoredPosition）
            rect.anchoredPosition = new Vector2(posX, posY);

            // 尺寸：根据原始宽高比和缩放
            if (display.GifWidth > 0 && display.GifHeight > 0)
            {
                float baseHeight = 150f * scale;
                float aspect = (float)display.GifWidth / display.GifHeight;
                float width = baseHeight * aspect;
                float height = baseHeight;
                rect.sizeDelta = new Vector2(width, height);
            }
            else
            {
                rect.sizeDelta = new Vector2(150 * scale, 150 * scale);
            }

            // 透明度
            if (rawImage != null)
            {
                Color c = rawImage.color;
                c.a = opacity;
                rawImage.color = c;
            }
        }

        // ---------- 设置持久化 ----------
        private static void SaveSettings()
        {
            if (string.IsNullOrEmpty(settingsPath)) return;
            var settings = new SettingsData
            {
                posX = posX,
                posY = posY,
                scale = scale,
                opacity = opacity,
                gifPath = gifPath
            };
            string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
            File.WriteAllText(settingsPath, json);
        }

        private static void LoadSettings()
        {
            if (!File.Exists(settingsPath)) return;
            try
            {
                string json = File.ReadAllText(settingsPath);
                var settings = JsonConvert.DeserializeObject<SettingsData>(json);
                if (settings != null)
                {
                    posX = settings.posX;
                    posY = settings.posY;
                    scale = settings.scale;
                    opacity = settings.opacity;
                    gifPath = settings.gifPath ?? "";
                    posXStr = posX.ToString("F0");
                    posYStr = posY.ToString("F0");
                    scaleStr = scale.ToString("F2");
                    opacityStr = opacity.ToString("F2");
                }
            }
            catch
            {
            }
        }
    }
}