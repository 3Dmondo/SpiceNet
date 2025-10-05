using System.Buffers.Binary;
using Shouldly;
using Spice.Kernels;

namespace Spice.Tests;

public class RealSpkKernelParserTests
{
  [Fact]
  public void Parses_MultiRecord_Type2_Segment_And_Evaluates()
  {
    var ms = BuildMinimalSpkType2MultiRecord();
    ms.Position = 0;

    var kernel = RealSpkKernelParser.Parse(ms);
    kernel.Segments.Count.ShouldBe(1);
    var seg = kernel.Segments[0];
    seg.DataType.ShouldBe(2);
    seg.RecordCount.ShouldBe(2);
    seg.Degree.ShouldBe(2);
    seg.RecordMids!.ShouldBe([0d, 100d]);
    seg.RecordRadii!.ShouldBe([100d, 100d]);
    seg.TrailerRecordSize.ShouldBe(11);
    seg.TrailerRecordCount.ShouldBe(2);
    seg.IntervalLength.ShouldBe(200d); // each record spans 200 seconds (-100..100, 0..200)

    // Evaluate at t=0 (first record mid)
    var state0 = SpkSegmentEvaluator.EvaluateState(seg, new Core.Instant(0));
    state0.PositionKm.X.ShouldBe(0d, 1e-12);
    state0.PositionKm.Y.ShouldBe(0d, 1e-12);
    state0.PositionKm.Z.ShouldBe(5d, 1e-12);
    state0.VelocityKmPerSec.X.ShouldBe(0.01, 1e-12); // 1 / radius
    state0.VelocityKmPerSec.Y.ShouldBe(0d, 1e-12);
    state0.VelocityKmPerSec.Z.ShouldBe(0d, 1e-12);

    // Evaluate at t=150 (second record, tau = (150-100)/100 = 0.5)
    var state150 = SpkSegmentEvaluator.EvaluateState(seg, new Core.Instant(150));
    state150.PositionKm.X.ShouldBe(0.5, 1e-12);
    state150.PositionKm.Y.ShouldBe(0.25, 1e-12);
    state150.PositionKm.Z.ShouldBe(5d, 1e-12);
    state150.VelocityKmPerSec.X.ShouldBe(0.01, 1e-12);
    state150.VelocityKmPerSec.Y.ShouldBe(0.01, 1e-12); // 2*tau / radius = 1/100
    state150.VelocityKmPerSec.Z.ShouldBe(0d, 1e-12);
  }

  static MemoryStream BuildMinimalSpkType2MultiRecord()
  {
    const int nd = 2; // start, stop
    const int ni = 6; // target, center, frame, type, initial, final
    const int type = 2;
    const int target = 499;
    const int center = 0;
    const int frame = 1;

    // Two records, degree 2 -> per record doubles = 2 (MID,RADIUS) + 3*(2+1)=2+9=11
    // Total record payload doubles = 22. Trailer adds 4 -> 26.
    double[] record1 = BuildRecord(mid:0, radius:100);
    double[] record2 = BuildRecord(mid:100, radius:100);
    double[] coeffPayload = new double[record1.Length + record2.Length];
    record1.CopyTo(coeffPayload, 0);
    record2.CopyTo(coeffPayload, record1.Length);

    // Trailer: INIT, INTLEN, RSIZE, N
    // INIT = start of first record coverage (mid - radius) = -100
    // INTLEN = 2*radius = 200
    // RSIZE = 11, N = 2
    double[] trailer = [-100d, 200d, 11d, 2d];

    double[] fullData = new double[coeffPayload.Length + trailer.Length];
    coeffPayload.CopyTo(fullData, 0);
    trailer.CopyTo(fullData, coeffPayload.Length);

    // Place coefficients starting at record 4 (records are 1-based). Each record is 128 doubles.
    int initialAddress = (4 - 1) * 128 + 1; // first double in record 4 => address 385
    int finalAddress = initialAddress + fullData.Length - 1;

    // Allocate file big enough for: file record (1), summary (2), name (3), data (4) single record suffices
    byte[] file = new byte[1024 * 4];

    // File record
    WriteAscii(file, 0, "DAF/SPK ");
    WriteInt(file, 8, nd);
    WriteInt(file, 12, ni);
    WriteAscii(file, 16, "TEST REAL SPK".PadRight(60));
    WriteInt(file, 76, 2); // forward summary record
    WriteInt(file, 80, 2); // backward summary record

    // Summary record (record 2)
    int summaryBase = 1024 * (2 - 1);
    WriteInt(file, summaryBase + 0, 0); // NEXT
    WriteInt(file, summaryBase + 8, 0); // PREV
    WriteInt(file, summaryBase + 16, 1); // NSUM=1

    int wordIndex = 3;
    double segStart = -100; double segStop = 200;
    WriteDouble(file, summaryBase + wordIndex * 8, segStart); wordIndex++;
    WriteDouble(file, summaryBase + wordIndex * 8, segStop); wordIndex++;
    WritePackedInts(file, summaryBase + wordIndex * 8, target, center); wordIndex++;
    WritePackedInts(file, summaryBase + wordIndex * 8, frame, type); wordIndex++;
    WritePackedInts(file, summaryBase + wordIndex * 8, initialAddress, finalAddress); wordIndex++;

    // Name record (record 3)
    int nameBase = 1024 * (3 - 1);
    WriteAscii(file, nameBase + 0, "TYPE2_MULTI".PadRight(40));

    // Data (record 4) write payload+trailer sequentially
    int dataBase = 1024 * (4 - 1);
    for (int i = 0; i < fullData.Length; i++)
      WriteDouble(file, dataBase + i * 8, fullData[i]);

    return new MemoryStream(file, writable: false);
  }

  static double[] BuildRecord(double mid, double radius)
  {
    // Chebyshev degree 2 sets: X: [0,1,0] -> tau; Y: [0.5,0,0.5] -> tau^2; Z: [5,0,0] -> constant 5
    return new double[]
    {
      mid, radius,
      // X
      0,1,0,
      // Y
      0.5,0,0.5,
      // Z
      5,0,0
    };
  }

  static void WriteAscii(byte[] buffer, int offset, string text)
  {
    var bytes = System.Text.Encoding.ASCII.GetBytes(text);
    Array.Copy(bytes, 0, buffer, offset, bytes.Length);
  }
  static void WriteInt(byte[] buffer, int offset, int value)
    => BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset,4), value);
  static void WriteDouble(byte[] buffer, int offset, double value)
    => BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(offset,8), BitConverter.DoubleToInt64Bits(value));
  static void WritePackedInts(byte[] buffer, int offset, int a, int b)
  {
    WriteInt(buffer, offset, a);
    WriteInt(buffer, offset+4, b);
  }
}
