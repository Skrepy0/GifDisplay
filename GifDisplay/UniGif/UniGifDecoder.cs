/*
UniGif
Copyright (c) 2015 WestHillApps (Hironari Nishioka)
This software is released under the MIT License.
http://opensource.org/licenses/mit-license.php
*/
// modified by Skrepy2233 in 2026/8/29

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
    FilterMode filterMode, TextureWrapMode wrapMode)
  {
    if (gifData.m_imageBlockList == null || gifData.m_imageBlockList.Count < 1) yield break;

    var gifTexList = new List<GifTexture>(gifData.m_imageBlockList.Count);
    var disposalMethodList = new List<ushort>(gifData.m_imageBlockList.Count);
    // CPU-side pixel buffers to avoid GPU readback (GetPixels32) every frame
    var cpuPixelBuffers = new List<Color32[]>();

    var imgIndex = 0;
    ImageBlock? prevImageBlock = null;

    for (var i = 0; i < gifData.m_imageBlockList.Count; i++)
    {
      var imageBlock = gifData.m_imageBlockList[i];
      var decodedData = GetDecodedData(imageBlock);

      var graphicCtrlEx = GetGraphicCtrlExt(gifData, imgIndex);

      var transparentIndex = GetTransparentIndex(graphicCtrlEx);

      disposalMethodList.Add(GetDisposalMethod(graphicCtrlEx));

      Color32 bgColor;
      var colorTable = GetColorTableAndSetBgColor(gifData, imageBlock, transparentIndex, out bgColor);

      bool filledTexture;
      Color32[] pixelBuffer;
      var tex = CreateTexture2D(gifData, cpuPixelBuffers, imgIndex, disposalMethodList, bgColor, filterMode, wrapMode,
        out filledTexture, prevImageBlock, out pixelBuffer);

      // Compose frame into pixelBuffer (single upload after loop)
      var dataIndex = 0;
      // Reverse set pixels. because GIF data starts from the top left.
      for (var y = tex.height - 1; y >= 0; y--)
        WriteTexturePixelRow(pixelBuffer, tex.width, tex.height, y, imageBlock, decodedData, ref dataIndex, colorTable,
          bgColor, transparentIndex, filledTexture);

      tex.SetPixels32(pixelBuffer);
      tex.Apply();

      // Store CPU-side copy for next frame compositing (avoids GPU readback)
      cpuPixelBuffers.Add(pixelBuffer);

      var delaySec = GetDelaySec(graphicCtrlEx);

      // Add to GIF texture list
      gifTexList.Add(new GifTexture(tex, delaySec));

      prevImageBlock = imageBlock;
      imgIndex++;
    }

    if (callback != null) callback(gifTexList);

    yield break;
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

  /// <summary>
  ///   Create Texture2D object and initial pixel buffer (no GPU upload yet)
  /// </summary>
  private static Texture2D CreateTexture2D(GifData gifData, List<Color32[]> cpuPixelBuffers, int imgIndex,
    List<ushort> disposalMethodList, Color32 bgColor, FilterMode filterMode, TextureWrapMode wrapMode,
    out bool filledTexture, ImageBlock? prevImageBlock, out Color32[] pixelBuffer)
  {
    filledTexture = false;

    // Create texture
    var tex = new Texture2D(gifData.m_logicalScreenWidth, gifData.m_logicalScreenHeight, TextureFormat.ARGB32, false);
    tex.filterMode = filterMode;
    tex.wrapMode = wrapMode;

    pixelBuffer = new Color32[tex.width * tex.height];

    // Check dispose
    var prevDisposal = imgIndex > 0 ? disposalMethodList[imgIndex - 1] : (ushort)2;
    var useBeforeIndex = -1;

    if (imgIndex == 0)
    {
      // Initial canvas: fill either background color or transparent
      var baseFill = ForceDisposeToTransparent
        ? TransparentColor
        : bgColor;
      for (var i = 0; i < pixelBuffer.Length; i++) pixelBuffer[i] = baseFill;
      filledTexture = true;
      return tex;
    }

    if (prevDisposal == 1)
    {
      // Do not dispose
      useBeforeIndex = imgIndex - 1;
    }
    else if (prevDisposal == 2)
    {
      // Restore to background
      useBeforeIndex = imgIndex - 1;
    }
    else if (prevDisposal == 3)
    {
      // 3 (Restore to previous)
      for (var i = imgIndex - 2; i >= 0; i--)
        if (disposalMethodList[i] == 1)
        {
          useBeforeIndex = i;
          break;
        }

      if (useBeforeIndex < 0)
      {
        var fill = ForceDisposeToTransparent ? TransparentColor : bgColor;
        for (var i = 0; i < pixelBuffer.Length; i++) pixelBuffer[i] = fill;
        filledTexture = true;
        return tex;
      }
    }
    else
    {
      // Treat as restore to background
      var fill = ForceDisposeToTransparent ? TransparentColor : bgColor;
      for (var i = 0; i < pixelBuffer.Length; i++) pixelBuffer[i] = fill;
      filledTexture = true;
      return tex;
    }

    if (useBeforeIndex >= 0)
    {
      filledTexture = true;
      // Use CPU-side pixel buffer instead of GPU readback (GetPixels32)
      var prevPix = cpuPixelBuffers[useBeforeIndex];
      Array.Copy(prevPix, pixelBuffer, prevPix.Length);

      // Disposal 2: clear only previous frame rect
      if (prevDisposal == 2 && prevImageBlock.HasValue)
      {
        var prev = prevImageBlock.Value;
        int left = prev.m_imageLeftPosition;
        int top = prev.m_imageTopPosition;
        int width = prev.m_imageWidth;
        int height = prev.m_imageHeight;

        var clearColor = ForceDisposeToTransparent ? TransparentColor : bgColor;

        for (var row = 0; row < height; row++)
        {
          var gifYFromTop = top + row;
          var unityY = tex.height - 1 - gifYFromTop;
          if (unityY < 0 || unityY >= tex.height) continue;

          var baseIndex = unityY * tex.width;
          for (var col = 0; col < width; col++)
          {
            var unityX = left + col;
            if (unityX < 0 || unityX >= tex.width) continue;
            pixelBuffer[baseIndex + unityX] = clearColor;
          }
        }
      }
    }

    return tex;
  }

  /// <summary>
  ///   Write one texture row into pixel buffer (no immediate GPU call)
  /// </summary>
  private static void WriteTexturePixelRow(Color32[] pixels, int texWidth, int texHeight, int y, ImageBlock imgBlock,
    byte[] decodedData, ref int dataIndex, Color32[] colorTable, Color32 bgColor, int transparentIndex,
    bool filledTexture)
  {
    // Row no (0~)
    var row = texHeight - 1 - y;

    // Check if row is within image block bounds
    var rowInImage = row >= imgBlock.m_imageTopPosition &&
                     row < imgBlock.m_imageTopPosition + imgBlock.m_imageHeight;
    int imgLeft = imgBlock.m_imageLeftPosition;
    var imgRight = imgBlock.m_imageLeftPosition + imgBlock.m_imageWidth;

    for (var x = 0; x < texWidth; x++)
    {
      // Out of image blocks
      if (!rowInImage || x < imgLeft || x >= imgRight)
      {
        // Get pixel color from bg color
        if (!filledTexture) pixels[y * texWidth + x] = ForceDisposeToTransparent ? TransparentColor : bgColor;
        continue;
      }

      // Out of decoded data
      if (dataIndex >= decodedData.Length)
      {
        if (!filledTexture)
        {
          pixels[y * texWidth + x] = ForceDisposeToTransparent ? TransparentColor : bgColor;
          if (dataIndex == decodedData.Length) Debug.LogError("dataIndex exceeded decodedData. index:" + dataIndex);
        }

        dataIndex++;
        continue;
      }

      // Get pixel color from color table
      var colorIndex = decodedData[dataIndex];
      if (colorTable == null || colorTable.Length <= colorIndex)
      {
        if (!filledTexture)
        {
          pixels[y * texWidth + x] = ForceDisposeToTransparent ? TransparentColor : bgColor;
          if (colorTable == null)
            Debug.LogError("colorIndex exceeded the size of colorTable. colorTable is null. colorIndex:" +
                           colorIndex);
          else
            Debug.LogError("colorIndex exceeded the size of colorTable. colorTable.Count:" + colorTable.Length +
                           " colorIndex:" + colorIndex);
        }

        dataIndex++;
        continue;
      }

      // Set alpha
      var isTransparent = transparentIndex >= 0 && transparentIndex == colorIndex;

      // If transparent and we already have previous composite -> keep underlying pixel
      if (!(filledTexture && isTransparent))
      {
        // Fast path: direct Color32 lookup from pre-converted table
        var c = colorTable[colorIndex];
        if (isTransparent) c.a = 0;
        pixels[y * texWidth + x] = c;
      }

      dataIndex++;
    }
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
    var dataIndex = 0;
    var totalRows = decodedData.Length / xNum;

    // Interlace pattern: rows are grouped in 4 passes
    // Pass 1: every 8th row starting at 0
    // Pass 2: every 8th row starting at 4
    // Pass 3: every 4th row starting at 2
    // Pass 4: every 2nd row starting at 1
    // Single pass: for each row, determine which pass it belongs to
    for (var row = 0; row < totalRows; row++)
    {
      // Determine if this row is active in current pass logic
      int pass;
      if (row % 8 == 0)
        pass = 1;
      else if (row % 8 == 4)
        pass = 2;
      else if (row % 4 == 2)
        pass = 3;
      else if (row % 2 == 1)
        pass = 4;
      else
        continue; // Should not happen for valid GIF

      // Calculate source index for this row
      var srcOffset = row * xNum;
      Buffer.BlockCopy(decodedData, srcOffset, newArr, dataIndex, xNum);
      dataIndex += xNum;
    }

    return newArr;
  }

  #endregion
}