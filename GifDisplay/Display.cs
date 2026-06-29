using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.IO;

public class Display : MonoBehaviour
{
    public RawImage rawImage;
    public string localPath = "";

    private Texture2D[] gifTextures;
    private float[] gifDelays;
    private int frameIndex = 0;
    private float timer = 0f;
    private bool isPlaying = false;

    // 原始尺寸
    public int GifWidth { get; private set; }
    public int GifHeight { get; private set; }

    // 加载完成回调
    public System.Action OnGifLoaded;

    public static System.Action<string> LogErrorCallback;

    private static readonly Dictionary<string, CachedGif> gifCache = new Dictionary<string, CachedGif>();

    private class CachedGif
    {
        public Texture2D[] textures;
        public float[] delays;
        public int width;
        public int height;
    }

    void Start()
    {
        if (rawImage == null)
            rawImage = GetComponent<RawImage>();
        StartCoroutine(LoadGif());
    }

    public IEnumerator LoadGif()
    {
        // 检查本地路径是否有效
        if (string.IsNullOrEmpty(localPath) || !File.Exists(localPath))
        {
            LogErrorCallback?.Invoke($"Local path invalid or file not found: {localPath}");
            yield break;
        }

        string cacheKey = localPath;
        LogErrorCallback?.Invoke($"Loading local GIF: {localPath}");

        byte[] gifData;
        try
        {
            gifData = File.ReadAllBytes(localPath);
            LogErrorCallback?.Invoke($"Read {gifData.Length} bytes from file");
        }
        catch (System.Exception ex)
        {
            LogErrorCallback?.Invoke($"Failed to read file: {ex.Message}");
            yield break;
        }

        // 检查缓存
        if (gifCache.TryGetValue(cacheKey, out var cached))
        {
            LogErrorCallback?.Invoke("Cache hit");
            gifTextures = cached.textures;
            gifDelays = cached.delays;
            GifWidth = cached.width;
            GifHeight = cached.height;
            isPlaying = true;
            if (gifTextures.Length > 0)
            {
                rawImage.texture = gifTextures[0];
                LogErrorCallback?.Invoke(
                    $"Loaded cached GIF, {gifTextures.Length} frames, size: {GifWidth}x{GifHeight}");
                OnGifLoaded?.Invoke();
            }

            yield break;
        }

        // 解码
        LogErrorCallback?.Invoke("Decoding GIF...");
        yield return StartCoroutine(UniGif.GetTextureListCoroutine(
            gifData,
            (textures, loopCount, width, height) =>
            {
                try
                {
                    if (textures == null || textures.Count == 0)
                    {
                        LogErrorCallback?.Invoke("Decode returned empty texture list");
                        return;
                    }

                    gifTextures = new Texture2D[textures.Count];
                    gifDelays = new float[textures.Count];
                    for (int i = 0; i < textures.Count; i++)
                    {
                        gifTextures[i] = textures[i].m_texture2d;
                        gifDelays[i] = textures[i].m_delaySec;
                    }

                    GifWidth = width;
                    GifHeight = height;

                    gifCache[cacheKey] = new CachedGif
                    {
                        textures = gifTextures,
                        delays = gifDelays,
                        width = width,
                        height = height
                    };

                    isPlaying = true;
                    if (gifTextures.Length > 0)
                    {
                        rawImage.texture = gifTextures[0];
                        LogErrorCallback?.Invoke($"Decoded {gifTextures.Length} frames, size: {GifWidth}x{GifHeight}");
                        OnGifLoaded?.Invoke();
                    }
                }
                catch (System.Exception ex)
                {
                    LogErrorCallback?.Invoke($"Decode exception: {ex.Message}");
                }
            }
        ));
    }

    void Update()
    {
        if (!isPlaying || gifTextures == null || gifTextures.Length == 0)
            return;

        timer += Time.deltaTime;
        if (timer >= gifDelays[frameIndex])
        {
            timer = 0f;
            frameIndex = (frameIndex + 1) % gifTextures.Length;
            rawImage.texture = gifTextures[frameIndex];
        }
    }

    public void Reload(bool forceReDownload = false)
    {
        if (forceReDownload)
        {
            string key = localPath;
            if (!string.IsNullOrEmpty(key) && gifCache.TryGetValue(key, out var cached))
            {
                foreach (var tex in cached.textures)
                    Destroy(tex);
                gifCache.Remove(key);
            }

            gifTextures = null;
            gifDelays = null;
            rawImage.texture = null;
            GifWidth = 0;
            GifHeight = 0;
        }

        StopAllCoroutines();
        StartCoroutine(LoadGif());
    }

    void OnDestroy()
    {
        isPlaying = false;
        rawImage.texture = null;
    }
}