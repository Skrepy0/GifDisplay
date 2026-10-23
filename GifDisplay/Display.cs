using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace GifDisplay;

public class Display : MonoBehaviour
{
  private const int MaxCache = 20;
  public static Action<string> LogErrorCallback;

  private static readonly Dictionary<string, CachedGif> GifCache = new(MaxCache, StringComparer.OrdinalIgnoreCase);
  private static readonly List<string> CacheOrder = new();
  public RawImage rawImage;
  public string localPath = "";

  private static readonly Dictionary<string, LoadJob> PendingLoads = new(StringComparer.OrdinalIgnoreCase);
  private static int activeDecoders;
  private static readonly HashSet<LoadJob> Jobs = new();
  private static GifLoadRunner runner;
  private LoadJob _job;
  private MonoBehaviour _loadHost;
  private CachedGif _cacheEntry;
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
  public bool isLoading => _job != null && !_job.PreviewOnly;

  private bool hasTexture => _gifTextures != null && _gifTextures.Length > 0;

  public Texture2D previewTexture { get; private set; }

  private void Start()
  {
    if (rawImage == null)
      rawImage = GetComponent<RawImage>();

    if (!hasTexture && _job == null) BeginLoad();
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

    CancelLoad();

    ReleaseInstanceTextures();

    if (previewTexture != null)
    {
      Destroy(previewTexture);
      previewTexture = null;
    }
  }

  private void OnApplicationQuit()
  {
    foreach (var key in new List<string>(GifCache.Keys)) RemoveCacheEntry(key);

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

  public void BeginLoad(bool previewOnly = false)
  {
    CancelLoad();
    if (runner == null)
    {
      var host = new GameObject("GifDisplayLoader");
      DontDestroyOnLoad(host);
      runner = host.AddComponent<GifLoadRunner>();
    }
    _loadHost = runner;
    _loadCoroutine = _loadHost.StartCoroutine(LoadShared(previewOnly));
  }

  public IEnumerator LoadGif()
  {
    BeginLoad();
    while (isLoading) yield return null;
  }

  private IEnumerator LoadShared(bool previewOnly)
  {
    string path;
    try { path = Path.GetFullPath(localPath); }
    catch (Exception ex) { LogErrorCallback?.Invoke(ex.Message); yield break; }
    if (GifCache.TryGetValue(path, out var cached))
    {
      CacheOrder.Remove(path);
      CacheOrder.Add(path);
      if (previewOnly) CreatePreviewTexture(cached.Textures[0]);
      else
      {
        ReleaseInstanceTextures();
        ApplyCache(cached);
      }
      yield break;
    }
    var key = (previewOnly ? "preview:" : "full:") + path;
    if (!PendingLoads.TryGetValue(key, out var job))
    {
      job = new LoadJob { Key = key, Path = path, PreviewOnly = previewOnly };
      PendingLoads.Add(key, job);
      Jobs.Add(job);
    }
    _job = job;
    job.Waiters++;
    if (!job.Started)
    {
      job.Started = true;
      _loadHost.StartCoroutine(GuardJob(job));
    }
    while (!job.Done) yield return null;
    try
    {
      if (job.Result != null)
      {
        if (previewOnly) CreatePreviewTexture(job.Result.Textures[0]);
        else
        {
          ReleaseInstanceTextures();
          ApplyCache(job.Result);
        }
      }
      else if (job.Error != null) LogErrorCallback?.Invoke(job.Error);
    }
    finally
    {
      DetachJob();
      _loadCoroutine = null;
    }
  }

  // Drive nested iterators explicitly so a decode exception completes the job
  // and disposes partial textures rather than stranding every subscriber.
  private static IEnumerator GuardJob(LoadJob job)
  {
    // List avoids the duplicate Stack<T> definitions in mixed Unity/framework references.
    var stack = new List<IEnumerator>();
    stack.Add(RunLoad(job));
    try
    {
      while (stack.Count > 0)
      {
        var current = stack[stack.Count - 1];
        bool moved;
        object value = null;
        try { moved = current.MoveNext(); if (moved) value = current.Current; }
        catch (Exception ex) { job.Error = ex.Message; break; }
        if (!moved)
        {
          stack.RemoveAt(stack.Count - 1);
          (current as IDisposable)?.Dispose();
          continue;
        }
        if (value is IEnumerator nested) stack.Add(nested);
        else yield return value;
      }
    }
    finally
    {
      while (stack.Count > 0)
      {
        var current = stack[stack.Count - 1];
        stack.RemoveAt(stack.Count - 1);
        (current as IDisposable)?.Dispose();
      }
      FinishJob(job);
    }
  }

  private static IEnumerator RunLoad(LoadJob job)
  {
    // Both normal loading and preloading use this queue.
    while (activeDecoders >= 2 && job.Waiters > 0) yield return null;
    if (job.Waiters == 0) { FinishJob(job); yield break; }
    activeDecoders++;
    try
    {
      var read = Task.Run(() => File.ReadAllBytes(job.Path));
      while (!read.IsCompleted) yield return null;
      if (read.IsFaulted) { job.Error = read.Exception.GetBaseException().Message; yield break; }
      if (job.Waiters == 0) yield break;
      var data = read.Result;
      if (string.Equals(Path.GetExtension(job.Path), ".gif", StringComparison.OrdinalIgnoreCase))
      {
        yield return UniGif.GetTextureListCoroutine(data, (frames, loops, width, height) =>
        {
          if (frames == null || frames.Count == 0) { job.Error = "GIF decode failed"; return; }
          job.Result = new CachedGif
          {
            Textures = frames.Select(f => f.m_texture2d).ToArray(),
            Delays = frames.Select(f => f.m_delaySec).ToArray(),
            Width = width, Height = height, Users = 1
          };
        }, maxFrames: job.PreviewOnly ? 1 : int.MaxValue, cancelled: () => job.Waiters == 0);
      }
      else
      {
        var texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
        if (!texture.LoadImage(data, true)) { Destroy(texture); job.Error = "Image decode failed"; yield break; }
        job.Result = new CachedGif
        {
          Textures = new[] { texture }, Delays = new[] { 0.1f },
          Width = texture.width, Height = texture.height, Users = 1
        };
      }
      if (job.Result != null && !job.PreviewOnly && job.Waiters > 0 && job.CacheAllowed)
      {
        job.Result.Cached = true;
        GifCache[job.Path] = job.Result;
        CacheOrder.Add(job.Path);
        TrimCache();
      }
    }
    finally
    {
      activeDecoders--;
      FinishJob(job);
    }
  }

  private static void FinishJob(LoadJob job)
  {
    if (job.Done) return;
    Jobs.Remove(job);
    job.Done = true;
    if (job.Waiters == 0) ReleaseJob(job);
  }

  private static void ReleaseJob(LoadJob job)
  {
    if (PendingLoads.TryGetValue(job.Key, out var current) && ReferenceEquals(current, job))
      PendingLoads.Remove(job.Key);
    if (job.Result != null)
    {
      job.Result.Users--;
      if (job.Result.Users == 0 && !job.Result.Cached) Release(job.Result.Textures);
      job.Result = null;
    }
  }

  private void DetachJob()
  {
    if (_job == null) return;
    var job = _job;
    _job = null;
    job.Waiters--;
    if (job.Waiters == 0)
    {
      // New requests must not join a cancelled decode.
      if (PendingLoads.TryGetValue(job.Key, out var current) && ReferenceEquals(current, job))
        PendingLoads.Remove(job.Key);
      if (job.Done) ReleaseJob(job);
    }
  }

  private void CancelLoad()
  {
    DetachJob();
    if (_loadCoroutine != null && _loadHost != null) _loadHost.StopCoroutine(_loadCoroutine);
    _loadCoroutine = null;
  }

  private class LoadJob
  {
    public string Key, Path, Error;
    public bool PreviewOnly, Started, Done;
    public bool CacheAllowed = true;
    public int Waiters;
    public CachedGif Result;
  }

  // ================= PLAY =================

  private IEnumerator PlayLoop()
  {
    var wait = new WaitForSecondsRealtime(0);
    while (_isPlaying)
    {
      ApplyTexture(_gifTextures[_frameIndex]);

      var delay = _gifDelays[_frameIndex];
      wait.waitTime = delay;
      yield return wait;

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

  private static void TrimCache()
  {
    // Bound retained cache memory; active instances retain their own references.
    long bytes = 0;
    foreach (var entry in GifCache.Values)
      bytes += (long)entry.Width * entry.Height * 4 * entry.Textures.Length;
    while ((bytes > 128L * 1024 * 1024 || GifCache.Count > MaxCache) && CacheOrder.Count > 0)
    {
      var oldest = CacheOrder[0];
      var entry = GifCache[oldest];
      bytes -= (long)entry.Width * entry.Height * 4 * entry.Textures.Length;
      RemoveCacheEntry(oldest);
    }
  }

  private void ApplyCache(CachedGif cached)
  {
    _cacheEntry = cached;
    cached.Users++;
    _gifTextures = cached.Textures;
    _gifDelays = cached.Delays;
    gifWidth = cached.Width;
    gifHeight = cached.Height;
    if (isActiveAndEnabled && gameObject.activeInHierarchy) StartPlayback();
    CreatePreviewTexture(_gifTextures[0]);
    isLoaded = true;
    OnGifLoaded?.Invoke();
  }

  private void ReleaseInstanceTextures()
  {
    if (rawImage != null) rawImage.texture = null;
    if (_cacheEntry != null)
    {
      _cacheEntry.Users--;
      if (_cacheEntry.Users == 0 && !_cacheEntry.Cached) Release(_cacheEntry.Textures);
      _cacheEntry = null;
    }
    else Release(_gifTextures);
    _gifTextures = null;
    _gifDelays = null;
    _currentTexture = null;
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
    Unload();
    if (force) RemoveCacheEntry(localPath);

    if (!gameObject.activeInHierarchy)
      gameObject.SetActive(true);

    isLoaded = false;
    _currentTexture = null;
    _frameIndex = 0;

    StopPlayback();

    BeginLoad();
  }

  // ================= UNLOAD =================

  public void Unload()
  {
    StopPlayback();

    CancelLoad();

    ReleaseInstanceTextures();
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

    previewWidth = Mathf.Max(1, previewWidth);
    previewHeight = Mathf.Max(1, previewHeight);
    previewTexture = new Texture2D(previewWidth, previewHeight, TextureFormat.ARGB32, false);
    previewTexture.filterMode = FilterMode.Bilinear;

    // GPU-side scaled copy via RenderTexture
    var rt = RenderTexture.GetTemporary(previewWidth, previewHeight);
    var previousTarget = RenderTexture.active;
    try
    {
      Graphics.Blit(source, rt);
      RenderTexture.active = rt;
      previewTexture.ReadPixels(new Rect(0, 0, previewWidth, previewHeight), 0, 0);
      previewTexture.Apply(false, true);

    }
    finally
    {
      RenderTexture.active = previousTarget;
      RenderTexture.ReleaseTemporary(rt);
    }
  }

  // ================= CACHE =================

  public static void RemoveCacheEntry(string key)
  {
    if (string.IsNullOrEmpty(key)) return;
    try { key = Path.GetFullPath(key); }
    catch (Exception) { return; }
    var pendingKey = "full:" + key;
    if (PendingLoads.TryGetValue(pendingKey, out var pending))
    {
      pending.CacheAllowed = false;
      PendingLoads.Remove(pendingKey);
    }
    if (GifCache.TryGetValue(key, out var cached))
    {
      cached.Cached = false;
      if (cached.Users == 0) Release(cached.Textures);
      GifCache.Remove(key);
      CacheOrder.Remove(key);
    }
  }

  // ================= CACHE =================

  public static void Shutdown()
  {
    foreach (var key in new List<string>(GifCache.Keys)) RemoveCacheEntry(key);
    if (runner != null) runner.StartCoroutine(ShutdownRunner(runner));
  }

  private static IEnumerator ShutdownRunner(GifLoadRunner host)
  {
    while (Jobs.Count > 0) yield return null;
    if (runner == host) runner = null;
    Destroy(host.gameObject);
  }

  private class CachedGif
  {
    public int Users;
    public bool Cached;
    public float[] Delays;
    public int Height;
    public Texture2D[] Textures;
    public int Width;
  }
}
public class GifLoadRunner : MonoBehaviour { }
