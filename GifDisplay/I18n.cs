using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GifDisplay;

public static class I18n
{
  private static readonly Dictionary<string, string> en = new()
  {
    ["language"] = "Language:",
    ["update_interval"] = "Update Interval",
    ["update_interval_desc"] =
      "Controls how often image visibility is updated. Lower values respond faster, higher values improve performance.",
    ["add_new_image"] = "Add New Image",
    ["path"] = "Path:",
    ["add"] = "Add",
    ["invalid_path"] = "Invalid file path: ",
    ["reload"] = "Reload",
    ["preview"] = "Preview:",
    ["no_image"] = "No Image",
    ["not_loaded"] = "Not Loaded",
    ["x_percent"] = "X (%)",
    ["y_percent"] = "Y (%)",
    ["rotation"] = "Rotation",
    ["scale"] = "Scale",
    ["opacity"] = "Opacity",
    ["sorting_order"] = "Sorting Order",
    ["higher_in_front"] = "(higher = in front)",
    ["show_during_play"] = "Show during play",
    ["show_during_not_play"] = "Show in menus (outside play)",
    ["delete"] = "Delete",
    ["confirm"] = "Confirm?",
    ["image"] = "Image",
    ["reloading"] = "Reloading...",
    ["path_error_0"] = "Invalid input, no image format explicitly specified.",
    ["path_error_1"] = "Unsupported image format.",
    ["path_error_2"] = "Invalid path, please check if the file exists."
  };

  private static readonly Dictionary<string, string> zh = new()
  {
    ["language"] = "语言:",
    ["update_interval"] = "图片显示状态更新间隔(/帧)",
    ["update_interval_desc"] = "控制更新图片显示状态的频率。数值越低响应越快，数值越高性能越好",
    ["add_new_image"] = "添加新图片",
    ["path"] = "路径:",
    ["add"] = "添加",
    ["invalid_path"] = "无效文件路径: ",
    ["reload"] = "重新加载",
    ["preview"] = "预览:",
    ["no_image"] = "无图片",
    ["not_loaded"] = "未加载",
    ["x_percent"] = "X (%)",
    ["y_percent"] = "Y (%)",
    ["rotation"] = "旋转",
    ["scale"] = "缩放",
    ["opacity"] = "透明度",
    ["sorting_order"] = "排序顺序",
    ["higher_in_front"] = "(数值大在前)",
    ["show_during_play"] = "在游戏时显示",
    ["show_during_not_play"] = "在非游戏界面显示",
    ["delete"] = "删除",
    ["confirm"] = "确认删除?",
    ["image"] = "图片",
    ["reloading"] = "重新加载中...",
    ["path_error_0"] = "无效输入, 没有显式指定的图片格式",
    ["path_error_1"] = "不支持的图片格式",
    ["path_error_2"] = "无效路径,请检查文件是否存在"
  };

  private static readonly Dictionary<string, string> ko = new()
  {
    ["language"] = "언어:",
    ["update_interval"] = "업데이트 간격",
    ["update_interval_desc"] = "이미지 표시 상태 업데이트 빈도를 제어합니다. 값이 낮을수록 응답이 빠르고, 높을수록 성능이 향상됩니다.",
    ["add_new_image"] = "새 이미지 추가",
    ["path"] = "경로:",
    ["add"] = "추가",
    ["invalid_path"] = "잘못된 파일 경로: ",
    ["reload"] = "다시 로드",
    ["preview"] = "미리보기:",
    ["no_image"] = "이미지 없음",
    ["not_loaded"] = "미로드",
    ["x_percent"] = "X (%)",
    ["y_percent"] = "Y (%)",
    ["rotation"] = "회전",
    ["scale"] = "크기 조정",
    ["opacity"] = "투명도",
    ["sorting_order"] = "정렬 순서",
    ["higher_in_front"] = "(값이 클수록 앞)",
    ["show_during_play"] = "게임 중에 표시",
    ["show_during_not_play"] = "비게임 화면에 표시",
    ["delete"] = "삭제",
    ["confirm"] = "삭제 확인?",
    ["image"] = "이미지",
    ["reloading"] = "다시 로드 중...",
    ["path_error_0"] = "잘못된 입력입니다. 명시적으로 지정된 이미지 형식이 없습니다.",
    ["path_error_1"] = "지원하지 않는 이미지 형식입니다.",
    ["path_error_2"] = "잘못된 경로입니다. 파일이 존재하는지 확인하세요."
  };

  private static string _lang = "en";

  public static string Lang
  {
    get => _lang;
    set
    {
      if (value == "zh" || value == "ko" || value == "en")
        _lang = value;
      else
        _lang = "en";
    }
  }

  private static Dictionary<string, string> CurrentDict => Lang switch
  {
    "zh" => zh,
    "ko" => ko,
    _ => en
  };

  public static void Load(string modPath)
  {
    var path = Path.Combine(modPath, "lang", "lang.json");
    if (!File.Exists(path))
    {
      Debug.Log($"[{Main.ModId}::I18n] Language file not found at: " + path);
      return;
    }

    try
    {
      var json = File.ReadAllText(path, Encoding.UTF8);
      Debug.Log($"[{Main.ModId}::I18n] Loaded language file, size: {json.Length} bytes");

      var root = JObject.Parse(json);
      var entriesToken = root["entries"];
      if (entriesToken == null || !entriesToken.HasValues)
      {
        Debug.LogWarning($"[{Main.ModId}::I18n] 'entries' field is missing or empty.");
        Debug.LogWarning(
          $"[{Main.ModId}::I18n] JSON content preview: {json.Substring(0, Math.Min(200, json.Length))}...");
        return;
      }

      var count = 0;
      foreach (var entryToken in entriesToken)
      {
        var key = entryToken["key"]?.Value<string>();
        if (string.IsNullOrEmpty(key)) continue;

        var enVal = entryToken["en"]?.Value<string>();
        var zhVal = entryToken["zh"]?.Value<string>();
        var koVal = entryToken["ko"]?.Value<string>();

        if (!string.IsNullOrEmpty(enVal))
        {
          en[key] = enVal;
          count++;
        }

        if (!string.IsNullOrEmpty(zhVal))
        {
          zh[key] = zhVal;
          count++;
        }

        if (!string.IsNullOrEmpty(koVal))
        {
          ko[key] = koVal;
          count++;
        }
      }

      Debug.Log($"[{Main.ModId}::I18n] Successfully applied {count} translation entries from external file.");
    }
    catch (Exception ex)
    {
      Debug.LogError($"[{Main.ModId}::I18n] Failed to load language file: {ex.Message}");
      Debug.LogError($"[{Main.ModId}::I18n] Stack trace: {ex.StackTrace}");
      try
      {
        var json = File.ReadAllText(path, Encoding.UTF8);
        Debug.LogError($"[{Main.ModId}::I18n] Full JSON content:\n{json}");
      }
      catch
      {
      }
    }
  }

  public static string Tr(string key)
  {
    return CurrentDict.TryGetValue(key, out var val) ? val : key;
  }

  [Serializable]
  private class LangFile
  {
    public LangEntry[] entries;
  }

  [Serializable]
  private class LangEntry
  {
    public string key;
    public string en;
    public string zh;
    public string ko;
  }
}