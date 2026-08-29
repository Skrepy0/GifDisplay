using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

namespace GifDisplay;

public class Display : MonoBehaviour
{
  private const int MaxCache = 20;
  public static Action<string> LogErrorCallback;

  private static readonly Dictionary<string, CachedGif> GifCache = new(MaxCache);
  private static readonly List<string> CacheOrder = new();
  public RawImage rawImage;
  public string localPath = "";

  private Texture _currentTexture;

  private int _frameIndex;
  private float[] _gifDelays;

  private Texture2D[] _gifTextures;
  private bool _isPlaying;
  private Coroutine _loadCoroutine;
  private Coroutine _playCoroutine;

  public Action OnGifLoaded;

  public int gifWidth { get; private set; }
  public int gifHeight { get; private set; }

  public bool isLoaded { get; private set; }

  private bool hasTexture => _gifTextures != null && _gifTextures.Length > 0;

  public Texture2D previewTexture { get; private set; }

  private void Start()
  {
    if (rawImage == null)
      rawImage = GetComponent<RawImage>();

    _loadCoroutine = StartCoroutine(LoadGif());
  }

  private void OnEnable()
  {
    if (_isPlaying)
      return;

    if (hasTexture)
      StartPlayback();
  }

  private void OnDisable()
  {
    StopPlayback();
  }

  private void OnDestroy()
  {
    StopPlayback();

    if (_loadCoroutine != null)
    {
      StopCoroutine(_loadCoroutine);
      _loadCoroutine = null;
    }

    _currentTexture = null;
    _gifTextures = null;
    _gifDelays = null;

    if (previewTexture != null)
    {
      Destroy(previewTexture);
      previewTexture = null;
    }

    // Clean up cache entry when this instance is destroyed
    RemoveCacheEntry(localPath);
  }

  private void OnApplicationQuit()
  {
    foreach (var cached in GifCache.Values) Release(cached.Textures);

    GifCache.Clear();
    CacheOrder.Clear();
  }

  public bool IsPlaying()
  {
    return _isPlaying;
  }

  public void StartPlayback()
  {
    if (!hasTexture)
      return;

    StopPlayback();

    if (_frameIndex >= _gifTextures.Length)
      _frameIndex = 0;

    if (_gifTextures.Length == 1)
    {
      ApplyTexture(_gifTextures[0]);
      return;
    }

    _isPlaying = true;
    _playCoroutine = StartCoroutine(PlayLoop());
  }

  public void Resume()
  {
    if (!gameObject.activeInHierarchy)
      return;

    if (hasTexture)
      StartPlayback();
  }

  private void StopPlayback()
  {
    _isPlaying = false;

    if (_playCoroutine != null)
    {
      StopCoroutine(_playCoroutine);
      _playCoroutine = null;
    }
  }

  // ================= LOAD =================

  public IEnumerator LoadGif()
  {
    if (string.IsNullOrEmpty(localPath) || !File.Exists(localPath))
    {
      LogErrorCallback?.Invoke($"Invalid path: {localPath}");
      _loadCoroutine = null;
      yield break;
    }

    var cacheKey = localPath;

    // Check cache first
    if (GifCache.TryGetValue(cacheKey, out var cached))
    {
      ApplyCache(cached);
      _loadCoroutine = null;
      yield break;
    }

    // Async file loading on background thread
    byte[] fileData = null;
    Exception loadException = null;
    var isDone = false;

    ThreadPool.QueueUserWorkItem(_ =>
    {
      try
      {
        fileData = File.ReadAllBytes(localPath);
      }
      catch (Exception ex)
      {
        loadException = ex;
      }
      finally
      {
        isDone = true;
      }
    });

    yield return new WaitUntil(() => isDone);

    if (loadException != null)
    {
      LogErrorCallback?.Invoke(loadException.Message);
      _loadCoroutine = null;
      yield break;
    }

    var ext = Path.GetExtension(localPath).ToLowerInvariant();
    var strList = ext.Split('.');
    if (strList.Length == 2 && Main.ValidFormat.Contains(strList[1]) && ext != ".gif")
      yield return LoadStatic(fileData, ext, cacheKey);
    else if (ext == ".gif")
      yield return LoadGifInternal(fileData, cacheKey);
    else
      LogErrorCallback?.Invoke("Unsupported format");

    _loadCoroutine = null;
  }

  private IEnumerator LoadStatic(byte[] fileData, string ext, string cacheKey)
  {
    // LoadImage handles format detection and resizing internally
    var tex = new Texture2D(2, 2);
    if (!tex.LoadImage(fileData))
    {
      Destroy(tex);
      yield break;
    }

    _gifTextures = new[] { tex };
    _gifDelays = new[] { 0.1f };

    gifWidth = tex.width;
    gifHeight = tex.height;

    AddCache(cacheKey);
    ApplyTexture(tex);
    CreatePreviewTexture(tex);

    isLoaded = true;
    OnGifLoaded?.Invoke();
  }

  // ================= GIF =================

  private IEnumerator LoadGifInternal(byte[] fileData, string cacheKey)
  {
    yield return UniGif.GetTextureListCoroutine(
      fileData,
      (textures, loopCount, width, height) =>
      {
        if (textures == null || textures.Count == 0)
          return;

        _gifTextures = new Texture2D[textures.Count];
        _gifDelays = new float[textures.Count];

        for (var i = 0; i < textures.Count; i++)
        {
          _gifTextures[i] = textures[i].m_texture2d;
          _gifDelays[i] = textures[i].m_delaySec;
        }

        gifWidth = width;
        gifHeight = height;

        AddCache(cacheKey);
        StartPlayback();
        CreatePreviewTexture(_gifTextures[0]);

        isLoaded = true;
        OnGifLoaded?.Invoke();
      });
  }

  // ================= PLAY =================

  private IEnumerator PlayLoop()
  {
    while (_isPlaying)
    {
      ApplyTexture(_gifTextures[_frameIndex]);

      var delay = _gifDelays[_frameIndex];
      yield return new WaitForSecondsRealtime(delay);

      _frameIndex++;
      if (_frameIndex >= _gifTextures.Length)
        _frameIndex = 0;
    }

    _playCoroutine = null;
  }

  private void ApplyTexture(Texture2D tex)
  {
    if (ReferenceEquals(rawImage, null))
      return;

    if (_currentTexture == tex)
      return;

    _currentTexture = tex;
    rawImage.texture = tex;
  }

  // ================= CACHE =================

  private void AddCache(string key)
  {
    if (GifCache.ContainsKey(key))
      return;
    // FIFO 淘汰
    while (GifCache.Count >= MaxCache && CacheOrder.Count > 0)
    {
      var oldest = CacheOrder[0];
      CacheOrder.RemoveAt(0);
      if (GifCache.TryGetValue(oldest, out var old))
      {
        Release(old.Textures);
        GifCache.Remove(oldest);
      }
    }

    CacheOrder.Add(key);
    GifCache[key] = new CachedGif
    {
      Textures = _gifTextures,
      Delays = _gifDelays,
      Width = gifWidth,
      Height = gifHeight
    };
  }

  private void ApplyCache(CachedGif cached)
  {
    _gifTextures = new Texture2D[cached.Textures.Length];
    for (var i = 0; i < cached.Textures.Length; i++)
    {
      var original = cached.Textures[i];
      // Use Graphics.CopyTexture for fast GPU-side copy (avoids Instantiate overhead)
      var copy = new Texture2D(original.width, original.height, original.format, false);
      Graphics.CopyTexture(original, copy);
      _gifTextures[i] = copy;
    }

    _gifDelays = (float[])cached.Delays.Clone();

    gifWidth = cached.Width;
    gifHeight = cached.Height;

    if (_gifTextures.Length == 1)
      ApplyTexture(_gifTextures[0]);
    else
      StartPlayback();

    CreatePreviewTexture(_gifTextures[0]);

    isLoaded = true;
    OnGifLoaded?.Invoke();
  }

  // ================= RELEASE =================

  private static void Release(Texture2D[] textures)
  {
    if (textures == null)
      return;

    foreach (var tex in textures)
      if (!ReferenceEquals(tex, null))
        Destroy(tex);
  }

  // ================= RELOAD =================

  public void Reload(bool force = false)
  {
    if (force && GifCache.TryGetValue(localPath, out var cached))
    {
      Release(cached.Textures);
      GifCache.Remove(localPath);
    }

    if (!gameObject.activeInHierarchy)
      gameObject.SetActive(true);

    isLoaded = false;
    _currentTexture = null;
    _frameIndex = 0;

    StopPlayback();

    if (_loadCoroutine != null)
    {
      StopCoroutine(_loadCoroutine);
      _loadCoroutine = null;
    }

    _loadCoroutine = StartCoroutine(LoadGif());
  }

  // ================= UNLOAD =================

  public void Unload()
  {
    StopPlayback();

    if (_loadCoroutine != null)
    {
      StopCoroutine(_loadCoroutine);
      _loadCoroutine = null;
    }

    if (_gifTextures != null)
      foreach (var tex in _gifTextures)
        if (tex != null)
          Destroy(tex);

    _gifTextures = null;
    _gifDelays = null;
    _currentTexture = null;
    _frameIndex = 0;
    isLoaded = false;
  }

  private void CreatePreviewTexture(Texture2D source)
  {
    if (source == null) return;

    CreatePreviewFromData(source);
  }

  public void CreatePreviewFromData(Texture2D source)
  {
    if (source is null) return;

    // Clean up previous preview texture
    if (previewTexture is not null)
    {
      Destroy(previewTexture);
      previewTexture = null;
    }

    // Create a small preview copy (max 128px for GUI preview)
    var maxSize = 128;
    var previewWidth = source.width;
    var previewHeight = source.height;

    if (previewWidth > maxSize || previewHeight > maxSize)
    {
      var ratio = (float)previewWidth / previewHeight;
      if (previewWidth > previewHeight)
      {
        previewWidth = maxSize;
        previewHeight = Mathf.RoundToInt(maxSize / ratio);
      }
      else
      {
        previewHeight = maxSize;
        previewWidth = Mathf.RoundToInt(maxSize * ratio);
      }
    }

    previewTexture = new Texture2D(previewWidth, previewHeight, TextureFormat.ARGB32, false);
    previewTexture.filterMode = FilterMode.Bilinear;

    // GPU-side scaled copy via RenderTexture
    var rt = RenderTexture.GetTemporary(previewWidth, previewHeight);
    Graphics.Blit(source, rt);
    RenderTexture.active = rt;

    previewTexture.ReadPixels(new Rect(0, 0, previewWidth, previewHeight), 0, 0);
    previewTexture.Apply();

    RenderTexture.active = null;
    RenderTexture.ReleaseTemporary(rt);
  }

  // Called from preload: sets textures without adding to cache (preload is for future display)
  public void SetPreloadedTextures(Texture2D[] textures, float[] delays, int width, int height)
  {
    _gifTextures = textures;
    _gifDelays = delays;
    gifWidth = width;
    gifHeight = height;
    _frameIndex = 0;
    isLoaded = true;
  }

  // ================= CACHE =================

  public static void RemoveCacheEntry(string key)
  {
    if (string.IsNullOrEmpty(key)) return;
    if (GifCache.TryGetValue(key, out var cached))
    {
      Release(cached.Textures);
      GifCache.Remove(key);
      CacheOrder.Remove(key);
    }
  }

  // ================= CACHE =================

  private class CachedGif
  {
    public float[] Delays;
    public int Height;
    public Texture2D[] Textures;
    public int Width;
  }
}