using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace UnityEngine
{
  public enum FilterMode { Bilinear }
  public enum TextureWrapMode { Clamp }
  public enum TextureFormat { ARGB32 }
  public struct Color32
  {
    public byte r, g, b, a;
    public Color32(byte red, byte green, byte blue, byte alpha) { r=red; g=green; b=blue; a=alpha; }
  }
  public class Object
  {
    public static void Destroy(Texture2D texture) { if (!texture.Destroyed) { texture.Destroyed=true; Texture2D.Live--; } }
  }
  public class Texture2D
  {
    public static int Live;
    public bool Destroyed;
    public int width, height;
    public FilterMode filterMode;
    public TextureWrapMode wrapMode;
    public Color32[] Pixels;
    public Texture2D(int w, int h, TextureFormat f, bool mipmaps) { width=w; height=h; Live++; }
    public void SetPixels32(Color32[] pixels) { Pixels=(Color32[])pixels.Clone(); }
    public void Apply(bool mipmaps, bool discard) { }
  }
  public static class Debug
  {
    public static void Log(object message) { }
    public static void LogError(object message) { throw new Exception("Decoder error: "+message); }
  }
}

public static partial class UniGif
{
  private static byte[] Pack(params byte[] pixels)
  {
    // Clear before each literal keeps the fixture encoder independent of dictionary logic.
    var codes=new List<int>();
    foreach (var pixel in pixels) { codes.Add(4); codes.Add(pixel); }
    codes.Add(5);
    var bytes=new byte[(codes.Count*3+7)/8];
    for (int i=0;i<codes.Count;i++)
      for (int bit=0;bit<3;bit++)
        bytes[(i*3+bit)/8] |= (byte)(((codes[i]>>bit)&1)<<((i*3+bit)%8));
    return bytes;
  }
  private static GifData Fixture(int width, int height)
  {
    return new GifData {
      m_logicalScreenWidth=(ushort)width, m_logicalScreenHeight=(ushort)height,
      m_globalColorTableFlag=true, m_bgColorIndex=0,
      m_globalColorTable=new List<byte[]> {new byte[]{0,0,0},new byte[]{255,0,0},new byte[]{0,255,0},new byte[]{0,0,255}},
      m_imageBlockList=new List<ImageBlock>(), m_graphicCtrlExList=new List<GraphicControlExtension>()
    };
  }
  private static void Frame(ref GifData data, int left, int width, int height, ushort disposal, byte[] pixels, bool interlace=false)
  {
    data.m_imageBlockList.Add(new ImageBlock {
      m_imageLeftPosition=(ushort)left, m_imageWidth=(ushort)width, m_imageHeight=(ushort)height,
      m_lzwMinimumCodeSize=2, m_interlaceFlag=interlace,
      m_imageDataList=new List<ImageBlock.ImageDataBlock> {new ImageBlock.ImageDataBlock {m_imageData=Pack(pixels)}}
    });
    data.m_graphicCtrlExList.Add(new GraphicControlExtension {
      m_disposalMethod=disposal, m_transparentColorFlag=true, m_transparentColorIndex=0, m_delayTime=7
    });
  }
  private static List<GifTexture> Decode(GifData data)
  {
    List<GifTexture> result=null;
    var iterator=DecodeTextureCoroutine(data, frames=>result=frames, UnityEngine.FilterMode.Bilinear, UnityEngine.TextureWrapMode.Clamp, int.MaxValue, null);
    while(iterator.MoveNext()) { }
    return result;
  }
  private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
  private static byte Index(UnityEngine.Color32 c) { return c.a==0?(byte)0:c.r>0?(byte)1:c.g>0?(byte)2:(byte)3; }
  private static void Free(List<GifTexture> frames) { foreach(var frame in frames) UnityEngine.Object.Destroy(frame.m_texture2d); }
  public static void RunChecks()
  {
    var data=Fixture(3,1);
    Frame(ref data,0,3,1,1,new byte[]{1,1,1});
    Frame(ref data,1,1,1,3,new byte[]{2});
    Frame(ref data,2,1,1,2,new byte[]{3});
    Frame(ref data,0,1,1,1,new byte[]{2});
    Frame(ref data,1,1,1,3,new byte[]{3});
    Frame(ref data,2,1,1,3,new byte[]{1});
    Frame(ref data,0,1,1,1,new byte[]{3});
    Frame(ref data,0,3,1,1,new byte[]{0,0,0});
    var frames=Decode(data);
    var expected=new[]{"111","121","113","210","230","211","310","310"};
    for(int i=0;i<expected.Length;i++) {
      string actual="";
      foreach(var color in frames[i].m_texture2d.Pixels) actual+=Index(color).ToString();
      Check(actual==expected[i],"Disposal/transparent composition frame "+i+": "+actual);
      Check(Math.Abs(frames[i].m_delaySec-0.07f)<0.001f,"Frame delay");
    }
    Free(frames);

    data=Fixture(1,8);
    Frame(ref data,0,1,8,1,new byte[]{1,2,3,1,2,1,3,2},true);
    frames=Decode(data);
    var topDown=new byte[]{1,2,3,1,2,3,1,2};
    for(int row=0;row<8;row++) Check(Index(frames[0].m_texture2d.Pixels[7-row])==topDown[row],"Interlace row "+row);
    Free(frames);

    data=Fixture(3,1);
    Frame(ref data,2,3,1,1,new byte[]{2,3,1});
    frames=Decode(data);
    Check(Index(frames[0].m_texture2d.Pixels[2])==2,"Clipped frame source offset");
    Free(frames);

    data=Fixture(256,256);
    var large=new byte[256*256];
    for(int i=0;i<large.Length;i++) large[i]=1;
    for(int i=0;i<20;i++) Frame(ref data,0,256,256,1,large);
    var cancelled=false;
    var called=false;
    var decode=DecodeTextureCoroutine(data, result=>called=true,UnityEngine.FilterMode.Bilinear,UnityEngine.TextureWrapMode.Clamp,int.MaxValue,()=>cancelled);
    Check(decode.MoveNext(),"Large decode must yield");
    cancelled=true;
    while(decode.MoveNext()) { }
    Check(!called,"Cancelled decoder must not publish");
    Check(UnityEngine.Texture2D.Live==0,"Cancelled decode must release partial textures");

    // A valid first frame followed by a deliberately invalid tail exercises parser early exit.
    using(var stream=new MemoryStream())
    using(var writer=new BinaryWriter(stream)) {
      writer.Write(System.Text.Encoding.ASCII.GetBytes("GIF89a"));
      writer.Write((ushort)1); writer.Write((ushort)1);
      writer.Write((byte)0x81); writer.Write((byte)0); writer.Write((byte)0);
      foreach(var rgb in Fixture(1,1).m_globalColorTable) writer.Write(rgb);
      writer.Write((byte)0x2c); writer.Write((ushort)0); writer.Write((ushort)0);
      writer.Write((ushort)1); writer.Write((ushort)1); writer.Write((byte)0);
      writer.Write((byte)2); var packed=Pack(1);
      writer.Write((byte)packed.Length); writer.Write(packed); writer.Write((byte)0);
      writer.Write((byte)0xff); writer.Flush();
      var parsed=new GifData();
      Check(SetGifData(stream.ToArray(),ref parsed,false,1),"Preview parser should stop after first frame");
      Check(parsed.m_imageBlockList.Count==1,"Preview frame count");
    }
    Console.WriteLine("PASS: disposal 1/2/3, consecutive restore, transparency, interlace, clipping, delays, cancellation cleanup, preview early parsing");
  }
}
