/*
UniGif
Copyright (c) 2015 WestHillApps (Hironari Nishioka)
This software is released under the MIT License.
http://opensource.org/licenses/mit-license.php
*/

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

      yield return 0;

      bool filledTexture;
      var tex = CreateTexture2D(gifData, gifTexList, imgIndex, disposalMethodList, bgColor, filterMode, wrapMode,
        out filledTexture, prevImageBlock, out var pixelBuffer);

      yield return 0;

      // Compose frame into pixelBuffer (single upload after loop)
      var dataIndex = 0;
      // Reverse set pixels. because GIF data starts from the top left.
      for (var y = tex.height - 1; y >= 0; y--)
        WriteTexturePixelRow(pixelBuffer, tex.width, tex.height, y, imageBlock, decodedData, ref dataIndex, colorTable,
          bgColor, transparentIndex, filledTexture);

      tex.SetPixels32(pixelBuffer);
      tex.Apply();

      yield return 0;

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
    // Combine LZW compressed data
    var lzwData = new List<byte>();
    for (var i = 0; i < imgBlock.m_imageDataList.Count; i++)
    for (var k = 0; k < imgBlock.m_imageDataList[i].m_imageData.Length; k++)
      lzwData.Add(imgBlock.m_imageDataList[i].m_imageData[k]);

    // LZW decode
    var needDataSize = imgBlock.m_imageHeight * imgBlock.m_imageWidth;
    var decodedData = DecodeGifLZW(lzwData, imgBlock.m_lzwMinimumCodeSize, needDataSize);

    // Sort interlace GIF
    if (imgBlock.m_interlaceFlag) decodedData = SortInterlaceGifData(decodedData, imgBlock.m_imageWidth);
    return decodedData;
  }

  /// <summary>
  ///   Get color table and set background color (local or global)
  /// </summary>
  private static List<byte[]> GetColorTableAndSetBgColor(GifData gifData, ImageBlock imgBlock, int transparentIndex,
    out Color32 bgColor)
  {
    var colorTable = imgBlock.m_localColorTableFlag ? imgBlock.m_localColorTable :
      gifData.m_globalColorTableFlag ? gifData.m_globalColorTable : null;

    if (colorTable != null && gifData.m_bgColorIndex < colorTable.Count)
    {
      // Set background color from color table
      var bgRgb = colorTable[gifData.m_bgColorIndex];
      bgColor = new Color32(bgRgb[0], bgRgb[1], bgRgb[2], (byte)(transparentIndex == gifData.m_bgColorIndex ? 0 : 255));
    }
    else
    {
      // Default: fully transparent instead of opaque black to avoid flashes.
      bgColor = new Color32(0, 0, 0, 0);
    }

    return colorTable;
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
  private static Texture2D CreateTexture2D(GifData gifData, List<GifTexture> gifTexList, int imgIndex,
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
      var prevPix = gifTexList[useBeforeIndex].m_texture2d.GetPixels32();
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
    byte[] decodedData, ref int dataIndex, List<byte[]> colorTable, Color32 bgColor, int transparentIndex,
    bool filledTexture)
  {
    // Row no (0~)
    var row = texHeight - 1 - y;

    for (var x = 0; x < texWidth; x++)
    {
      // Out of image blocks
      if (row < imgBlock.m_imageTopPosition ||
          row >= imgBlock.m_imageTopPosition + imgBlock.m_imageHeight ||
          x < imgBlock.m_imageLeftPosition ||
          x >= imgBlock.m_imageLeftPosition + imgBlock.m_imageWidth)
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
      {
        var colorIndex = decodedData[dataIndex];
        if (colorTable == null || colorTable.Count <= colorIndex)
        {
          if (!filledTexture)
          {
            pixels[y * texWidth + x] = ForceDisposeToTransparent ? TransparentColor : bgColor;
            if (colorTable == null)
              Debug.LogError("colorIndex exceeded the size of colorTable. colorTable is null. colorIndex:" +
                             colorIndex);
            else
              Debug.LogError("colorIndex exceeded the size of colorTable. colorTable.Count:" + colorTable.Count +
                             " colorIndex:" + colorIndex);
          }

          dataIndex++;
          continue;
        }

        var rgb = colorTable[colorIndex];

        // Set alpha
        var isTransparent = transparentIndex >= 0 && transparentIndex == colorIndex;
        var alpha = isTransparent ? (byte)0 : (byte)255;

        // If transparent and we already have previous composite -> keep underlying pixel
        if (!(filledTexture && isTransparent))
          // Set color (single Color32 creation per pixel)
          pixels[y * texWidth + x] = new Color32(rgb[0], rgb[1], rgb[2], alpha);
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
  private static byte[] DecodeGifLZW(List<byte> compData, int lzwMinimumCodeSize, int needDataSize)
  {
    // Safety
    if (needDataSize <= 0) return new byte[0];
    if (lzwMinimumCodeSize < 2)
      // Per spec minimum code size for image data is at least 2 (except some malformed GIFs).
      lzwMinimumCodeSize = 2;

    // Spec values
    var clearCode = 1 << lzwMinimumCodeSize;
    var endCode = clearCode + 1;
    var nextCode = endCode + 1;
    var codeSize = lzwMinimumCodeSize + 1;
    var codeSizeLimit = 12;

    // Dictionary: index -> byte sequence
    // Preallocate 4096 max entries
    var dictionary = new byte[4096][];
    for (var i = 0; i < clearCode; i++) dictionary[i] = new[] { (byte)i };
    dictionary[clearCode] = null; // clear
    dictionary[endCode] = null; // end

    var output = new List<byte>(needDataSize);

    var bitPos = 0;
    var compLen = compData.Count;

    byte[] previous = null;

    // Helper local to read one LZW code (LSB first)
    var readCode = () =>
    {
      var rawCode = 0;
      var bitsRead = 0;
      while (bitsRead < codeSize)
      {
        var byteIndex = bitPos >> 3;
        if (byteIndex >= compLen)
          // Out of data
          return -1;
        var bitIndexInByte = bitPos & 7;
        int b = compData[byteIndex];
        var bit = (b >> bitIndexInByte) & 1;
        rawCode |= bit << bitsRead;

        bitPos++;
        bitsRead++;
      }

      return rawCode;
    };

    while (output.Count < needDataSize)
    {
      var code = readCode();
      if (code < 0)
        // Ran out of compressed data
        break;

      if (code == clearCode)
      {
        // Reset dictionary
        for (var i = 0; i < clearCode; i++) dictionary[i] = new[] { (byte)i };
        dictionary[clearCode] = null;
        dictionary[endCode] = null;
        nextCode = endCode + 1;
        codeSize = lzwMinimumCodeSize + 1;
        previous = null;
        continue;
      }

      if (code == endCode)
        // Normal end
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

      // Output entry
      var copyLen = entry.Length;
      var remaining = needDataSize - output.Count;
      if (copyLen > remaining) copyLen = remaining;
      for (var i = 0; i < copyLen; i++) output.Add(entry[i]);
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

    if (output.Count < needDataSize)
    {
      // Pad (rare; malformed GIF) with zeros to expected size
      var pad = needDataSize - output.Count;
      for (var i = 0; i < pad; i++) output.Add(0);
    }

    return output.ToArray();
  }

  /// <summary>
  ///   Sort interlace GIF data
  /// </summary>
  /// <param name="decodedData">Decoded GIF data</param>
  /// <param name="xNum">Pixel number of horizontal row</param>
  /// <returns>Sorted data</returns>
  private static byte[] SortInterlaceGifData(byte[] decodedData, int xNum)
  {
    var rowNo = 0;
    var dataIndex = 0;
    var newArr = new byte[decodedData.Length];
    // Every 8th. row, starting with row 0.
    for (var i = 0; i < newArr.Length; i++)
    {
      if (rowNo % 8 == 0)
      {
        newArr[i] = decodedData[dataIndex];
        dataIndex++;
      }

      if (i != 0 && i % xNum == 0) rowNo++;
    }

    rowNo = 0;
    // Every 8th. row, starting with row 4.
    for (var i = 0; i < newArr.Length; i++)
    {
      if (rowNo % 8 == 4)
      {
        newArr[i] = decodedData[dataIndex];
        dataIndex++;
      }

      if (i != 0 && i % xNum == 0) rowNo++;
    }

    rowNo = 0;
    // Every 4th. row, starting with row 2.
    for (var i = 0; i < newArr.Length; i++)
    {
      if (rowNo % 4 == 2)
      {
        newArr[i] = decodedData[dataIndex];
        dataIndex++;
      }

      if (i != 0 && i % xNum == 0) rowNo++;
    }

    rowNo = 0;
    // Every 2nd. row, starting with row 1.
    for (var i = 0; i < newArr.Length; i++)
    {
      if (rowNo % 8 != 0 && rowNo % 8 != 4 && rowNo % 4 != 2)
      {
        newArr[i] = decodedData[dataIndex];
        dataIndex++;
      }

      if (i != 0 && i % xNum == 0) rowNo++;
    }

    return newArr;
  }

  #endregion
}