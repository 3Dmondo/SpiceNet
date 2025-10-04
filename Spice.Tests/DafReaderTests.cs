using System.Buffers.Binary;
using Shouldly;
using Spice.IO;

namespace Spice.Tests;

public static class DafTestBuilder
{
  public static MemoryStream Build(int nd, int ni, int recordCount, IReadOnlyList<(double[] d, int[] i)> summaries, string idWord = "DAF/SPK ")
  {
    if (idWord.Length != 8)
      throw new ArgumentException("ID word must be 8 chars", nameof(idWord));
    var ms = new MemoryStream();
    using (var bw = new BinaryWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true))
    {
      bw.Write(System.Text.Encoding.ASCII.GetBytes(idWord));
      WriteInt(bw, nd);
      WriteInt(bw, ni);
      WriteInt(bw, recordCount);
      WriteInt(bw, summaries.Count);
      WriteInt(bw, 0); // reserved
      WriteInt(bw, 0); // reserved

      foreach (var (d, ints) in summaries)
      {
        if (d.Length != nd)
          throw new ArgumentException("double length mismatch");
        if (ints.Length != ni)
          throw new ArgumentException("int length mismatch");
        foreach (var val in d)
          WriteDouble(bw, val);
        foreach (var val in ints)
          WriteInt(bw, val);
      }
    }
    ms.Position = 0;
    return ms;
  }

  static void WriteInt(BinaryWriter bw, int v)
  {
    Span<byte> buf = stackalloc byte[4];
    BinaryPrimitives.WriteInt32LittleEndian(buf, v);
    bw.Write(buf);
  }
  static void WriteDouble(BinaryWriter bw, double v)
  {
    Span<byte> buf = stackalloc byte[8];
    BinaryPrimitives.WriteUInt64LittleEndian(buf, (ulong)BitConverter.DoubleToInt64Bits(v));
    bw.Write(buf);
  }
}

public class DafReaderTests
{
  [Fact]
  public void Reads_Header_And_Summaries()
  {
    var summaries = new List<(double[], int[])>
    {
      (new[]{1.0, 2.0}, new[]{10, 20, 30}),
      (new[]{3.5, -4.25}, new[]{-1, 0, 999})
    };

    using var ms = DafTestBuilder.Build(nd: 2, ni: 3, recordCount: 123, summaries);
    using var reader = DafReader.Open(ms);

    reader.Nd.ShouldBe(2);
    reader.Ni.ShouldBe(3);
    reader.RecordCount.ShouldBe(123);
    reader.SummaryCount.ShouldBe(2);

    var list = new List<DafSegmentSummary>(reader.ReadSummaries());
    list.Count.ShouldBe(2);
    list[0].Doubles.ShouldBe([1.0, 2.0]);
    list[0].Integers.ShouldBe([10, 20, 30]);
    list[1].Doubles.ShouldBe([3.5, -4.25]);
    list[1].Integers.ShouldBe([-1, 0, 999]);
  }

  [Fact]
  public void Zero_Summaries_Supported()
  {
    using var ms = DafTestBuilder.Build(1, 1, 0, new List<(double[], int[])>());
    using var reader = DafReader.Open(ms);
    reader.SummaryCount.ShouldBe(0);
    reader.ReadSummaries().ShouldBeEmpty();
  }

  [Fact]
  public void Invalid_IdWord_Throws()
  {
    var summaries = new List<(double[], int[])> { (new[] { 0.0 }, new[] { 0 }) };
    using var ms = DafTestBuilder.Build(1, 1, 1, summaries, idWord: "BAD/SPK ");
    Should.Throw<InvalidDataException>(() => DafReader.Open(ms));
  }
}
