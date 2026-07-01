using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Collections.Generic;
using System.IO;

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

  public System.Action OnGifLoaded;
  public static System.Action<string> LogErrorCallback;

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

  private static readonly Dictionary<string, CachedGif> GifCache =
    new(MaxCache);

  private static readonly List<string> CacheOrder = new();

  private bool HasTexture =>
    _gifTextures != null &&
    _gifTextures.Length > 0;

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

    if (HasTexture)
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

    if (HasTexture)
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

    if (GifCache.TryGetValue(cacheKey, out var cached))
    {
      ApplyCache(cached);
      _loadCoroutine = null;
      yield break;
    }

    byte[] fileData;

    try
    {
      fileData = File.ReadAllBytes(localPath);
    }
    catch (System.Exception ex)
    {
      LogErrorCallback?.Invoke(ex.Message);
      _loadCoroutine = null;
      yield break;
    }

    string ext = Path.GetExtension(localPath).ToLowerInvariant();

    if (ext == ".jpg" ||
        ext == ".jpeg" ||
        ext == ".png" ||
        ext == ".webp")
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

  // ================= STATIC =================

  private IEnumerator LoadStatic(byte[] fileData, string ext, string cacheKey)
  {
    Texture2D tex;

    if (ext == ".webp")
    {
      using var req = UnityWebRequestTexture.GetTexture("file://" + localPath);
      yield return req.SendWebRequest();

      if (req.result != UnityWebRequest.Result.Success)
      {
        LogErrorCallback?.Invoke(req.error);
        yield break;
      }

      tex = DownloadHandlerTexture.GetContent(req);
    }
    else
    {
      tex = new Texture2D(2, 2);

      if (!tex.LoadImage(fileData))
      {
        Destroy(tex);
        yield break;
      }
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
    if (!HasTexture)
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
      float timer = 0f;

      while (timer < delay)
      {
        timer += Time.unscaledDeltaTime;
        yield return null;
      }

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

  public Texture2D PreviewTexture
  {
    get
    {
      if (HasTexture)
        return _gifTextures[0];

      return null;
    }
  }

  // ================= CACHE =================

  private void AddCache(string key)
  {
    // 已存在则直接更新
    if (GifCache.ContainsKey(key))
    {
      GifCache[key] = new CachedGif
      {
        Textures = _gifTextures,
        Delays = _gifDelays,
        Width = gifWidth,
        Height = gifHeight
      };
      return;
    }

    // FIFO 淘汰
    while (GifCache.Count >= MaxCache)
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
    _gifTextures = cached.Textures;
    _gifDelays = cached.Delays;

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

  private void Release(Texture2D[] textures)
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