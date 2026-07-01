using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GifDisplay;

public class Display : MonoBehaviour
{
  public RawImage rawImage;
  public string localPath = "";

  private Texture2D[] _gifTextures;
  private float[] _gifDelays;

  private int _frameIndex;
  private bool _isPlaying;

  public int gifWidth { get; private set; }
  public int gifHeight { get; private set; }

  public System.Action OnGifLoaded;
  public static System.Action<string> LogErrorCallback;

  // ================= CACHE =================
  private class CachedGif
  {
    public Texture2D[] Textures;
    public float[] Delays;
    public int Width;
    public int Height;
  }

  private static readonly Dictionary<string, CachedGif> GifCache = new();
  private const int MaxCache = 20;

  private Coroutine _playCoroutine;

  void Start()
  {
    if (rawImage == null)
      rawImage = GetComponent<RawImage>();

    StartCoroutine(LoadGif());
  }

  // ================= LOAD =================
  public IEnumerator LoadGif()
  {
    if (string.IsNullOrEmpty(localPath) || !File.Exists(localPath))
    {
      LogErrorCallback?.Invoke($"Invalid path: {localPath}");
      yield break;
    }

    string cacheKey = localPath;

    if (GifCache.TryGetValue(cacheKey, out var cached))
    {
      ApplyCache(cached);
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
      yield break;
    }

    string ext = Path.GetExtension(localPath).ToLower();

    if (ext == ".jpg" || ext == ".png" || ext == ".jpeg" || ext == ".webp")
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

    OnGifLoaded?.Invoke();
  }

  // ================= GIF =================
  private IEnumerator LoadGifInternal(byte[] fileData, string cacheKey)
  {
    yield return UniGif.GetTextureListCoroutine(
      fileData,
      (textures, loopCount, width, height) =>
      {
        if (textures == null || textures.Count == 0) return;

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

        OnGifLoaded?.Invoke();
      }
    );
  }

  // ================= PLAY =================
  private void StartPlayback()
  {
    if (_playCoroutine != null)
      StopCoroutine(_playCoroutine);

    if (_gifTextures == null || _gifTextures.Length <= 1)
    {
      ApplyTexture(_gifTextures?[0]);
      return;
    }

    _isPlaying = true;
    _frameIndex = 0;

    _playCoroutine = StartCoroutine(PlayLoop());
  }

  private IEnumerator PlayLoop()
  {
    while (_isPlaying && _gifTextures != null)
    {
      ApplyTexture(_gifTextures[_frameIndex]);

      yield return new WaitForSecondsRealtime(_gifDelays[_frameIndex]);

      _frameIndex = (_frameIndex + 1) % _gifTextures.Length;
    }
  }

  private void ApplyTexture(Texture2D tex)
  {
    if (!ReferenceEquals(rawImage, null))
      rawImage.texture = tex;
  }

  // ================= CACHE =================
  private void AddCache(string key)
  {
    if (GifCache.Count > MaxCache)
    {
      var first = GifCache.Keys.First();
      Release(GifCache[first].Textures);
      GifCache.Remove(first);
    }

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

    ApplyTexture(_gifTextures[0]);
    StartPlayback();

    OnGifLoaded?.Invoke();
  }

  // ================= RELEASE =================
  private void Release(Texture2D[] textures)
  {
    if (textures == null) return;

    foreach (var t in textures)
      if (!ReferenceEquals(t, null))
        Destroy(t);
  }

  // ================= RELOAD =================
  public void Reload(bool force = false)
  {
    if (force && GifCache.TryGetValue(localPath, out var cached))
    {
      Release(cached.Textures);
      GifCache.Remove(localPath);
    }

    StopAllCoroutines();
    StartCoroutine(LoadGif());
  }

  void OnDestroy()
  {
    _isPlaying = false;

    if (_playCoroutine != null)
      StopCoroutine(_playCoroutine);
  }

  void OnApplicationQuit()
  {
    foreach (var kv in GifCache)
      Release(kv.Value.Textures);
    GifCache.Clear();
  }
}