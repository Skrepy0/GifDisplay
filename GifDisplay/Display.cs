using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GifDisplay
{
  public class Display : MonoBehaviour
  {
    public RawImage rawImage;
    public string localPath = "";

    private Texture2D[] _gifTextures;
    private float[] _gifDelays;

    private int _frameIndex;
    private bool _isPlaying;

    private Coroutine _playCoroutine;
    private Coroutine _loadCoroutine;

    private Texture _currentTexture;

    public int gifWidth { get; private set; }
    public int gifHeight { get; private set; }

    public Action OnGifLoaded;
    public static Action<string> LogErrorCallback;

    public bool isLoaded { get; private set; }

    // ================= CACHE =================

    private class CachedGif
    {
      public Texture2D[] Textures;
      public float[] Delays;
      public int Width;
      public int Height;
    }

    private const int MaxCache = 20;

    private static readonly Dictionary<string, CachedGif> GifCache = new(MaxCache);
    private static readonly List<string> CacheOrder = new();

    private bool hasTexture => _gifTextures != null && _gifTextures.Length > 0;

    void Start()
    {
      if (rawImage == null)
        rawImage = GetComponent<RawImage>();

      _loadCoroutine = StartCoroutine(LoadGif());
    }

    void OnEnable()
    {
      if (_isPlaying)
        return;

      if (hasTexture)
        StartPlayback();
    }

    void OnDisable()
    {
      StopPlayback();
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

      string cacheKey = localPath;

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
      bool isDone = false;

      System.Threading.ThreadPool.QueueUserWorkItem(_ =>
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

      string ext = Path.GetExtension(localPath).ToLowerInvariant();
      var strList = ext.Split('.');
      if (strList.Length == 2 && Main.ValidFormat.Contains(strList[1]) && ext != ".gif")
      {
        yield return LoadStatic(fileData, ext, cacheKey);
      }
      else if (ext == ".gif")
      {
        yield return LoadGifInternal(fileData, cacheKey);
      }
      else
      {
        LogErrorCallback?.Invoke("Unsupported format");
      }

      _loadCoroutine = null;
    }

    private IEnumerator LoadStatic(byte[] fileData, string ext, string cacheKey)
    {
      Texture2D tex;
      tex = new Texture2D(2, 2);
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

          for (int i = 0; i < textures.Count; i++)
          {
            _gifTextures[i] = textures[i].m_texture2d;
            _gifDelays[i] = textures[i].m_delaySec;
          }

          gifWidth = width;
          gifHeight = height;

          AddCache(cacheKey);
          StartPlayback();

          isLoaded = true;
          OnGifLoaded?.Invoke();
        });
    }

    // ================= PLAY =================

    private void StartPlayback()
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

    private IEnumerator PlayLoop()
    {
      while (_isPlaying)
      {
        ApplyTexture(_gifTextures[_frameIndex]);

        float delay = _gifDelays[_frameIndex];
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

    public Texture2D previewTexture => hasTexture ? _gifTextures[0] : null;

    // ================= CACHE =================

    private void AddCache(string key)
    {
      if (GifCache.ContainsKey(key))
        return;
      // FIFO 淘汰
      while (GifCache.Count >= MaxCache && CacheOrder.Count > 0)
      {
        string oldest = CacheOrder[0];
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
      for (int i = 0; i < cached.Textures.Length; i++)
      {
        Texture2D original = cached.Textures[i];
        _gifTextures[i] = Instantiate(original);
      }

      _gifDelays = (float[])cached.Delays.Clone();

      gifWidth = cached.Width;
      gifHeight = cached.Height;

      if (_gifTextures.Length == 1)
      {
        ApplyTexture(_gifTextures[0]);
      }
      else
      {
        StartPlayback();
      }

      isLoaded = true;
      OnGifLoaded?.Invoke();
    }

    // ================= RELEASE =================

    private static void Release(Texture2D[] textures)
    {
      if (textures == null)
        return;

      foreach (var tex in textures)
      {
        if (!ReferenceEquals(tex, null))
          Destroy(tex);
      }
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

    void OnDestroy()
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

      // Clean up cache entry when this instance is destroyed
      RemoveCacheEntry(localPath);
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

    void OnApplicationQuit()
    {
      foreach (var cached in GifCache.Values)
      {
        Release(cached.Textures);
      }

      GifCache.Clear();
      CacheOrder.Clear();
    }
  }
}