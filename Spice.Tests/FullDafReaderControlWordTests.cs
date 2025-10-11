using System.Buffers.Binary;
using System.Reflection;
using Shouldly;

namespace Spice.Tests;

public class FullDafReaderControlWordTests
{
  const int RecordBytes = 1024;
  const int WordBytes = 8;

  [Fact]
  public void ControlWords_SyntheticIntEncoding_ParsesSingleSegment()
  {
    using var ms = BuildMinimalDaf(syntheticControlWords:true);
    var (segments, nd, ni) = Enumerate(ms);
    nd.ShouldBe(2);
    ni.ShouldBe(6);
    segments.Count.ShouldBe(1);
    segments[0].InitialAddress.ShouldBeGreaterThan(0);
    segments[0].FinalAddress.ShouldBeGreaterThan(segments[0].InitialAddress);
  }

  [Fact]
  public void ControlWords_DoubleEncoding_ParsesSingleSegment()
  {
    using var ms = BuildMinimalDaf(syntheticControlWords:false);
    var (segments, nd, ni) = Enumerate(ms);
    nd.ShouldBe(2);
    ni.ShouldBe(6);
    segments.Count.ShouldBe(1);
  }

  static (List<(double[] Dc,int[] Ic,string Name,int InitialAddress,int FinalAddress)> Segs, int Nd, int Ni) Enumerate(Stream stream)
  {
    var ioAsm = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Spice.IO");
    var readerType = ioAsm.GetType("Spice.IO.FullDafReader", throwOnError:true)!;
    var open = readerType.GetMethod("Open", BindingFlags.NonPublic|BindingFlags.Static | BindingFlags.Public)!;
    var readerObj = open.Invoke(null, new object[]{stream, true})!; // leaveOpen=true so stream not disposed
    try
    {
      var ndProp = readerType.GetProperty("Nd", BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)!;
      var niProp = readerType.GetProperty("Ni", BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)!;
      int nd = (int)ndProp.GetValue(readerObj)!;
      int ni = (int)niProp.GetValue(readerObj)!;
      var enumMeth = readerType.GetMethod("EnumerateSegments", BindingFlags.Instance|BindingFlags.NonPublic | BindingFlags.Public)!;
      var enumerable = (System.Collections.IEnumerable)enumMeth.Invoke(readerObj, Array.Empty<object>())!;
      var list = new List<(double[] Dc,int[] Ic,string Name,int InitialAddress,int FinalAddress)>();
      foreach (var item in enumerable)
      {
        if (item is ValueTuple<double[],int[],string,int,int> t)
          list.Add((t.Item1, t.Item2, t.Item3, t.Item4, t.Item5));
      }
      return (list, nd, ni);
    }
    finally
    {
      if (readerObj is IDisposable d) d.Dispose();
    }
  }

  static MemoryStream BuildMinimalDaf(bool syntheticControlWords)
  {
    int nd = 2; int ni = 6;
    // Records we will create: 1=file, 2=summary, 3=name
    var fileRec = new byte[RecordBytes];
    WriteAscii(fileRec, 0, "DAF/SPK ");
    BinaryPrimitives.WriteInt32LittleEndian(fileRec.AsSpan(8,4), nd);
    BinaryPrimitives.WriteInt32LittleEndian(fileRec.AsSpan(12,4), ni);
    WriteAscii(fileRec, 16, "TEST-DAF".PadRight(60));
    // forward & backward summary record pointers (record 2)
    BinaryPrimitives.WriteInt32LittleEndian(fileRec.AsSpan(76,4), 2);
    BinaryPrimitives.WriteInt32LittleEndian(fileRec.AsSpan(80,4), 2);

    var summaryRec = new byte[RecordBytes];
    // Control words: NEXT=0 PREV=0 NSUM=1
    if (syntheticControlWords)
    {
      // low 32 bits = value, high 32 bits = 0 (little-endian low first)
      WriteSyntheticInt(summaryRec, 0, 0);
      WriteSyntheticInt(summaryRec, 1, 0);
      WriteSyntheticInt(summaryRec, 2, 1);
    }
    else
    {
      WriteDoubleAsInt(summaryRec, 0, 0);
      WriteDoubleAsInt(summaryRec, 1, 0);
      WriteDoubleAsInt(summaryRec, 2, 1);
    }

    int wordIndex = 3; // after control words
    void WriteDouble(double v)
    {
      int offset = wordIndex * WordBytes;
      BinaryPrimitives.WriteInt64LittleEndian(summaryRec.AsSpan(offset,8), BitConverter.DoubleToInt64Bits(v));
      wordIndex++;
    }
    void WritePackedInts(int a, int b)
    {
      int offset = wordIndex * WordBytes;
      BinaryPrimitives.WriteInt32LittleEndian(summaryRec.AsSpan(offset,4), a);
      BinaryPrimitives.WriteInt32LittleEndian(summaryRec.AsSpan(offset+4,4), b);
      wordIndex++;
    }

    // DC[0]=start, DC[1]=stop
    WriteDouble(0); WriteDouble(100);
    // IC: target,center,frame,type,initial,final
    WritePackedInts(499,0);
    WritePackedInts(1,2); // frame=1, type=2
    WritePackedInts(1,10); // initial, final addresses

    var nameRec = new byte[RecordBytes];
    WriteAscii(nameRec, 0, "SEGMENT-1".PadRight(40));

    var ms = new MemoryStream();
    ms.Write(fileRec); ms.Write(summaryRec); ms.Write(nameRec);
    ms.Position = 0;
    return ms;
  }

  static void WriteSyntheticInt(byte[] buf, int controlWordIndex, int value)
  {
    int byteOffset = controlWordIndex * WordBytes;
    BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(byteOffset,4), value);
    // high 4 bytes already zero-initialized
  }
  static void WriteDoubleAsInt(byte[] buf, int controlWordIndex, int value)
  {
    int byteOffset = controlWordIndex * WordBytes;
    long bits = BitConverter.DoubleToInt64Bits(value);
    BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(byteOffset,8), bits);
  }

  static void WriteAscii(byte[] dest, int offset, string text)
  {
    System.Text.Encoding.ASCII.GetBytes(text, dest.AsSpan(offset, text.Length));
  }
}
