/*
UniGif
Copyright (c) 2015 WestHillApps (Hironari Nishioka)
This software is released under the MIT License.
http://opensource.org/licenses/mit-license.php
*/
// modified by Skrepy2233 in 2026/10/23

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static partial class UniGif
{
  // If true, disposal method 2 regions are cleared to transparent instead of the logical screen background color.
  // This eliminates persistent solid color boxes behind partially transparent animations.
  private const bool ForceDisposeToTransparent = true;

  // Cached transparent color to avoid repeated allocations
  private static readonly Color32 TransparentColor = new(0, 0, 0, 0);

  // Pre-generated single-byte dictionary entries (immutable, safe to reuse)
  private static readonly byte[][] SingleByteDict = InitSingleByteDict();

  private static byte[][] InitSingleByteDict()
  {
    var arr = new byte[256][];
    for (var i = 0; i < 256; i++)
      arr[i] = new[] { (byte)i };
    return arr;
  }

  /// <summary>
  ///   Decode to textures from GIF data
  /// </summary>
  /// <param name="gifData">GIF data</param>
  /// <param name="callback">Callback method(param is GIF texture list)</param>
  /// <param name="filterMode">Textures filter mode</param>
  /// <param name="wrapMode">Textures wrap mode</param>
  /// <returns>IEnumerator</returns>
  private static IEnumerator DecodeTextureCoroutine(GifData gifData, Action<List<GifTexture>> callback,
    FilterMode filterMode, TextureWrapMode wrapMode, int maxFrames, Func<bool> cancelled)
  {
    if (gifData.m_imageBlockList == null || gifData.m_imageBlockList.Count == 0) yield break;
    var count = Math.Min(gifData.m_imageBlockList.Count, maxFrames);
    var frames = new List<GifTexture>(count);
    var width = (int)gifData.m_logicalScreenWidth;
    var height = (int)gifData.m_logicalScreenHeight;
    var pixels = new Color32[checked(width * height)];
    Color32[] restore = null;
    ushort previousDisposal = 0;
    ImageBlock? previousBlock = null;
    var transferred = false;
    Texture2D pending = null;
    var timer = System.Diagnostics.Stopwatch.StartNew();
    try
    {
      for (var i = 0; i < count; i++)
      {
        if (cancelled != null && cancelled()) yield break;
        var block = gifData.m_imageBlockList[i];
        var control = GetGraphicCtrlExt(gifData, i);
        var transparent = GetTransparentIndex(control);
        Color32 background;
        var palette = GetColorTableAndSetBgColor(gifData, block, transparent, out background);
        var clear = ForceDisposeToTransparent ? TransparentColor : background;
        if (i == 0) FillPixels(pixels, clear);
        else if (previousDisposal == 2 && previousBlock.HasValue)
          ClearRegion(pixels, width, height, previousBlock.Value, clear);
        else if (previousDisposal == 3 && restore != null)
          Array.Copy(restore, pixels, pixels.Length);

        var disposal = GetDisposalMethod(control);
        if (disposal == 3)
        {
          if (restore == null) restore = new Color32[pixels.Length];
          Array.Copy(pixels, restore, pixels.Length);
        }

        var decoded = GetDecodedData(block);
        // Visit only the changed rectangle, keeping source offsets valid when clipped.
        for (var row = 0; row < block.m_imageHeight; row++)
        {
          var y = height - 1 - (block.m_imageTopPosition + row);
          if (y >= 0 && y < height)
          {
            var left = Math.Max(0, (int)block.m_imageLeftPosition);
            var right = Math.Min(width, block.m_imageLeftPosition + block.m_imageWidth);
            var src = row * block.m_imageWidth + left - block.m_imageLeftPosition;
            for (var x = left; x < right && src < decoded.Length; x++, src++)
            {
              var index = decoded[src];
              if (index != transparent && palette != null && index < palette.Length)
                pixels[y * width + x] = palette[index];
            }
          }
          if ((row & 31) == 31 && timer.Elapsed.TotalMilliseconds >= 2)
          {
            yield return null;
            timer.Restart();
            if (cancelled != null && cancelled()) yield break;
          }
        }

        pending = new Texture2D(width, height, TextureFormat.ARGB32, false);
        pending.filterMode = filterMode;
        pending.wrapMode = wrapMode;
        pending.SetPixels32(pixels);
        pending.Apply(false, true);
        frames.Add(new GifTexture(pending, GetDelaySec(control)));
        pending = null;
        previousDisposal = disposal;
        previousBlock = block;
        if (timer.Elapsed.TotalMilliseconds >= 2 && i + 1 < count)
        {
          yield return null;
          timer.Restart();
        }
      }
      if (callback != null)
      {
        callback(frames);
        transferred = true;
      }
    }
    finally
    {
      if (pending != null) UnityEngine.Object.Destroy(pending);
      if (!transferred)
        foreach (var frame in frames) UnityEngine.Object.Destroy(frame.m_texture2d);
    }
  }

  private static void FillPixels(Color32[] pixels, Color32 color)
  {
    if (color.a == 0 && color.r == 0 && color.g == 0 && color.b == 0)
      Array.Clear(pixels, 0, pixels.Length);
    else
      for (var i = 0; i < pixels.Length; i++) pixels[i] = color;
  }

  private static void ClearRegion(Color32[] pixels, int width, int height, ImageBlock block, Color32 color)
  {
    var right = Math.Min(width, block.m_imageLeftPosition + block.m_imageWidth);
    var bottom = Math.Min(height, block.m_imageTopPosition + block.m_imageHeight);
    for (var row = (int)block.m_imageTopPosition; row < bottom; row++)
      for (var x = (int)block.m_imageLeftPosition; x < right; x++)
        pixels[(height - 1 - row) * width + x] = color;
  }

  #region Call from DecodeTexture methods

  /// <summary>
  ///   Get decoded image data from ImageBlock
  /// </summary>
  private static byte[] GetDecodedData(ImageBlock imgBlock)
  {
    // Combine LZW compressed data into a contiguous buffer
    var totalLen = 0;
    for (var i = 0; i < imgBlock.m_imageDataList.Count; i++)
      totalLen += imgBlock.m_imageDataList[i].m_imageData.Length;

    var lzwData = new byte[totalLen];
    var offset = 0;
    for (var i = 0; i < imgBlock.m_imageDataList.Count; i++)
    {
      var block = imgBlock.m_imageDataList[i].m_imageData;
      Buffer.BlockCopy(block, 0, lzwData, offset, block.Length);
      offset += block.Length;
    }

    // LZW decode
    var needDataSize = imgBlock.m_imageHeight * imgBlock.m_imageWidth;
    var decodedData = DecodeGifLZW(lzwData, imgBlock.m_lzwMinimumCodeSize, needDataSize);

    // Sort interlace GIF
    if (imgBlock.m_interlaceFlag)
      decodedData = SortInterlaceGifData(decodedData, imgBlock.m_imageWidth);
    return decodedData;
  }

  /// <summary>
  ///   Get color table and set background color (local or global)
  /// </summary>
  private static Color32[] GetColorTableAndSetBgColor(GifData gifData, ImageBlock imgBlock, int transparentIndex,
    out Color32 bgColor)
  {
    var colorTable = imgBlock.m_localColorTableFlag ? imgBlock.m_localColorTable :
      gifData.m_globalColorTableFlag ? gifData.m_globalColorTable : null;

    Color32[] colorTable32 = null;

    if (colorTable != null && gifData.m_bgColorIndex < colorTable.Count)
    {
      // Convert List<byte[]> to Color32[] for fast lookup
      var count = colorTable.Count;
      colorTable32 = new Color32[count];
      for (var i = 0; i < count; i++)
      {
        var rgb = colorTable[i];
        colorTable32[i] = new Color32(rgb[0], rgb[1], rgb[2], 255);
      }

      // Set background color from color table
      var bgRgb = colorTable[gifData.m_bgColorIndex];
      bgColor = new Color32(bgRgb[0], bgRgb[1], bgRgb[2], (byte)(transparentIndex == gifData.m_bgColorIndex ? 0 : 255));
    }
    else
    {
      // Default: fully transparent instead of opaque black to avoid flashes.
      bgColor = new Color32(0, 0, 0, 0);
    }

    return colorTable32;
  }

  /// <summary>
  ///   Get GraphicControlExtension from GifData
  /// </summary>
  private static GraphicControlExtension? GetGraphicCtrlExt(GifData gifData, int imgBlockIndex)
  {
    if (gifData.m_graphicCtrlExList != null && gifData.m_graphicCtrlExList.Count > imgBlockIndex)
      return gifData.m_graphicCtrlExList[imgBlockIndex];
    return null;
  }

  /// <summary>
  ///   Get transparent color index from GraphicControlExtension
  /// </summary>
  private static int GetTransparentIndex(GraphicControlExtension? graphicCtrlEx)
  {
    var transparentIndex = -1;
    if (graphicCtrlEx != null && graphicCtrlEx.Value.m_transparentColorFlag)
      transparentIndex = graphicCtrlEx.Value.m_transparentColorIndex;
    return transparentIndex;
  }

  /// <summary>
  ///   Get delay seconds from GraphicControlExtension
  /// </summary>
  private static float GetDelaySec(GraphicControlExtension? graphicCtrlEx)
  {
    // Get delay sec from GraphicControlExtension
    var delaySec = graphicCtrlEx != null ? graphicCtrlEx.Value.m_delayTime / 100f : 1f / 60f;
    if (delaySec <= 0f) delaySec = 0.1f;
    return delaySec;
  }

  /// <summary>
  ///   Get disposal method from GraphicControlExtension
  /// </summary>
  private static ushort GetDisposalMethod(GraphicControlExtension? graphicCtrlEx)
  {
    // Map 0 (unspecified) -> 1 (do not dispose)
    var method = graphicCtrlEx != null ? graphicCtrlEx.Value.m_disposalMethod : (ushort)2;
    if (method == 0) method = 1;

    return method;
  }

  #endregion

  #region Decode LZW & Sort interrace methods

  /// <summary>
  ///   GIF LZW decode
  /// </summary>
  /// <param name="compData">LZW compressed data</param>
  /// <param name="lzwMinimumCodeSize">LZW minimum code size</param>
  /// <param name="needDataSize">Need decoded data size</param>
  /// <returns>Decoded data array</returns>
  private static byte[] DecodeGifLZW(byte[] compData, int lzwMinimumCodeSize, int needDataSize)
  {
    // Safety
    if (needDataSize <= 0) return new byte[0];
    if (lzwMinimumCodeSize < 2)
      lzwMinimumCodeSize = 2;

    // Spec values
    var clearCode = 1 << lzwMinimumCodeSize;
    var endCode = clearCode + 1;
    var nextCode = endCode + 1;
    var codeSize = lzwMinimumCodeSize + 1;
    const int codeSizeLimit = 12;

    // Dictionary: index -> byte sequence
    var dictionary = new byte[4096][];
    for (var i = 0; i < clearCode; i++)
      dictionary[i] = SingleByteDict[i]; // Reuse pre-allocated entries
    dictionary[clearCode] = null; // clear
    dictionary[endCode] = null; // end

    // Output buffer: pre-allocated, written by index (no List<byte> overhead)
    var output = new byte[needDataSize];
    var outputIndex = 0;

    var bitPos = 0;
    var compLen = compData.Length;

    byte[] previous = null;

    while (outputIndex < needDataSize)
    {
      // Inline readCode for performance (avoids delegate call)
      var rawCode = 0;
      var bitsRead = 0;
      while (bitsRead < codeSize)
      {
        var byteIndex = bitPos >> 3;
        if (byteIndex >= compLen)
          goto endDecode; // Out of data
        var bitIndexInByte = bitPos & 7;
        var b = compData[byteIndex];
        var bit = (b >> bitIndexInByte) & 1;
        rawCode |= bit << bitsRead;
        bitPos++;
        bitsRead++;
      }

      var code = rawCode;

      if (code == clearCode)
      {
        // Reset dictionary - reuse single-byte entries (do not re-allocate)
        for (var i = 0; i < clearCode; i++)
          dictionary[i] = SingleByteDict[i];
        dictionary[clearCode] = null;
        dictionary[endCode] = null;
        nextCode = endCode + 1;
        codeSize = lzwMinimumCodeSize + 1;
        previous = null;
        continue;
      }

      if (code == endCode)
        break;

      byte[] entry;

      if (code < nextCode && dictionary[code] != null)
      {
        entry = dictionary[code];
      }
      else if (code == nextCode && previous != null)
      {
        // KwKwK case: code refers to string = previous + first(previous)
        var first = previous[0];
        var temp = new byte[previous.Length + 1];
        Buffer.BlockCopy(previous, 0, temp, 0, previous.Length);
        temp[temp.Length - 1] = first;
        entry = temp;
      }
      else
      {
        // Malformed stream - cannot recover
        break;
      }

      // Output entry directly into buffer (no List<byte>.Add overhead)
      var copyLen = entry.Length;
      var remaining = needDataSize - outputIndex;
      if (copyLen > remaining) copyLen = remaining;
      Buffer.BlockCopy(entry, 0, output, outputIndex, copyLen);
      outputIndex += copyLen;
      if (copyLen < entry.Length) break; // filled needed size

      if (previous != null && nextCode < dictionary.Length)
      {
        // Add new sequence: previous + first(entry)
        var newSeq = new byte[previous.Length + 1];
        Buffer.BlockCopy(previous, 0, newSeq, 0, previous.Length);
        newSeq[newSeq.Length - 1] = entry[0];
        dictionary[nextCode] = newSeq;
        nextCode++;

        // Grow code size if needed and not exceeding 12 bits
        if (nextCode == 1 << codeSize && codeSize < codeSizeLimit) codeSize++;
      }

      previous = entry;
    }

    endDecode:
    if (outputIndex < needDataSize)
    {
      // Pad (rare; malformed GIF) with zeros to expected size
      var padLen = needDataSize - outputIndex;
      for (var i = 0; i < padLen; i++) output[outputIndex + i] = 0;
    }

    return output;
  }

  /// <summary>
  ///   Sort interlace GIF data - single-pass optimized
  /// </summary>
  /// <param name="decodedData">Decoded GIF data</param>
  /// <param name="xNum">Pixel number of horizontal row</param>
  /// <returns>Sorted data</returns>
  private static byte[] SortInterlaceGifData(byte[] decodedData, int xNum)
  {
    var newArr = new byte[decodedData.Length];
    var source = 0;
    var rows = decodedData.Length / xNum;
    var starts = new[] { 0, 4, 2, 1 };
    var strides = new[] { 8, 8, 4, 2 };
    for (var pass = 0; pass < starts.Length; pass++)
      for (var row = starts[pass]; row < rows; row += strides[pass])
      {
        Buffer.BlockCopy(decodedData, source, newArr, row * xNum, xNum);
        source += xNum;
      }

    return newArr;
  }

  #endregion
}