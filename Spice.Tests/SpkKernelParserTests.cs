using Shouldly;
using Spice.Kernels;
using Spice.Core;

namespace Spice.Tests;

static class SpkTestBuilder
{
  public static MemoryStream BuildSingleSegment(
    int dataType,
    double startTdbSec,
    double stopTdbSec,
    int target,
    int center,
    int frame,
    double[] coeffs)
  {
    // Synthetic DAF header: IDWORD(8) + nd + ni + records + summaryCount + 2 reserved ints
    const int nd = 2; // start, stop
    const int ni = 6; // target, center, frame, type, initial, final
    int summaryCount = 1;

    var ms = new MemoryStream();
    var bw = new BinaryWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true);
    // ID word (padded to 8 chars)
    bw.Write(System.Text.Encoding.ASCII.GetBytes("DAF/SPK "));

    WriteInt(bw, nd);
    WriteInt(bw, ni);
    WriteInt(bw, 0); // records (unused synthetic)
    WriteInt(bw, summaryCount);
    WriteInt(bw, 0); // reserved
    WriteInt(bw, 0); // reserved

    // Summary
    WriteDouble(bw, startTdbSec);
    WriteDouble(bw, stopTdbSec);
    WriteInt(bw, target);
    WriteInt(bw, center);
    WriteInt(bw, frame);
    WriteInt(bw, dataType);
    WriteInt(bw, 1); // initial address (1-based)
    WriteInt(bw, coeffs.Length); // final address inclusive

    // Coefficient block
    foreach (var c in coeffs) WriteDouble(bw, c);

    bw.Flush();
    ms.Position = 0;
    return ms;
  }

  public static MemoryStream BuildWithUnsupportedType(int dataType)
    => BuildSingleSegment(dataType, 0, 10, 1, 0, 1, [1.0]);

  static void WriteInt(BinaryWriter bw, int v)
  {
    Span<byte> buf = stackalloc byte[4];
    System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(buf, v);
    bw.Write(buf);
  }
  static void WriteDouble(BinaryWriter bw, double v)
  {
    Span<byte> buf = stackalloc byte[8];
    System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf, (ulong)BitConverter.DoubleToInt64Bits(v));
    bw.Write(buf);
  }
}

public class SpkKernelParserTests
{
  [Fact]
  public void Parses_Type2_Segment()
  {
    double[] coeffs = [1,2,3,4,5];
    using var ms = SpkTestBuilder.BuildSingleSegment(
      dataType:2,
      startTdbSec:0,
      stopTdbSec:1000,
      target:499,
      center:0,
      frame:1,
      coeffs:coeffs);

    var kernel = SpkKernelParser.Parse(ms);
    kernel.Segments.Count.ShouldBe(1);
    var seg = kernel.Segments[0];
    seg.DataType.ShouldBe(2);
    seg.Target.ShouldBe(new BodyId(499));
    seg.Center.ShouldBe(new BodyId(0));
    seg.Frame.ShouldBe(new FrameId(1));
    seg.StartTdbSec.ShouldBe(0d);
    seg.StopTdbSec.ShouldBe(1000d);
    seg.Coefficients.ShouldBe(coeffs);
    seg.CoefficientCount.ShouldBe(coeffs.Length);
    seg.CoefficientOffset.ShouldBe(0); // initial=1 -> offset 0
  }

  [Fact]
  public void Parses_Type3_Segment()
  {
    double[] coeffs = [0.1, -0.2, 0.3];
    using var ms = SpkTestBuilder.BuildSingleSegment(3, -500, 500, 301, 0, 1, coeffs);
    var kernel = SpkKernelParser.Parse(ms);
    kernel.Segments.Count.ShouldBe(1);
    kernel.Segments[0].DataType.ShouldBe(3);
    kernel.Segments[0].Coefficients.ShouldBe(coeffs);
  }

  [Fact]
  public void Unsupported_Type_Throws()
  {
    using var ms = SpkTestBuilder.BuildWithUnsupportedType(5); // type 5 not supported yet
    Should.Throw<InvalidDataException>(() => SpkKernelParser.Parse(ms));
  }

  [Fact]
  public void Invalid_Address_Range_Throws()
  {
    // Build a stream then corrupt final address beyond coefficient count
    double[] coeffs = [1,2];
    using var ms = SpkTestBuilder.BuildSingleSegment(2, 0, 10, 1, 0, 1, coeffs);
    // Seek to summary int fields start (after id + ints + two doubles + first three ints + dataType)
    // Header: 8 + 6*4 = 32 bytes. Summary doubles: 16 bytes => 48. Ints target..dataType (4*4)=16 => 64.
    // initial address at offset 64 then final at 68.
    ms.Position = 68; // overwrite final address (currently 2) with 5
    var bw = new BinaryWriter(ms, System.Text.Encoding.ASCII, leaveOpen:true);
    Span<byte> buf = stackalloc byte[4];
    System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(buf, 5);
    bw.Write(buf);
    ms.Position = 0;
    Should.Throw<InvalidDataException>(() => SpkKernelParser.Parse(ms));
  }
}
