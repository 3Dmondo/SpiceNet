// CSPICE Port Reference: SPK Required Reading (conceptual) — implementation is original managed design.
using System.Buffers.Binary;
using Spice.Core;
using Spice.IO;

namespace Spice.Kernels;

/// <summary>
/// Real-layout SPK kernel parser (subset: segment data types 2 &amp; 3) built atop <see cref="FullDafReader"/>.
/// Responsibilities:
/// 1. Enumerate DAF segment summaries (DC + IC) and filter supported SPK types (2,3).
/// 2. Retrieve raw coefficient double words via their 1-based DAF address range (INITIAL..FINAL).
/// 3. Infer multi-record structure (record count, degree, components per set) using known per-record formula
///    Type 2: (MID,RADIUS) + 3*(DEG+1) doubles
///    Type 3: (MID,RADIUS) + 6*(DEG+1) doubles
/// 4. Populate <see cref="SpkSegment"/> with per-record MID/RADIUS arrays enabling precise sub-interval evaluation.
///
/// NOTE: This implementation keeps coefficients fully materialized unless using <see cref="ParseLazy"/>, where
/// only per-record MID/RADIUS values are read and the remainder is fetched on demand through an <see cref="IEphemerisDataSource"/>.
/// </summary>
public static class RealSpkKernelParser
{
  /// <summary>
  /// Parse an SPK kernel stream producing an <see cref="SpkKernel"/> comprised of supported segments (eager load).
  /// </summary>
  public static SpkKernel Parse(Stream stream)
  {
    using var daf = FullDafReader.Open(stream, leaveOpen: true);
    var segments = new List<SpkSegment>();

    foreach (var seg in daf.EnumerateSegments())
    {
      if (seg.Dc.Length < 2 || seg.Ic.Length < 6) continue;
      double start = seg.Dc[0];
      double stop = seg.Dc[1];
      int target = seg.Ic[0];
      int center = seg.Ic[1];
      int frame = seg.Ic[2];
      int type = seg.Ic[3];
      int initial = seg.Ic[4];
      int final = seg.Ic[5];
      if (type is not (2 or 3) || initial <= 0 || final < initial) continue;

      var coeffs = ReadDoubleRange(stream, initial, final);
      var meta = InferRecordStructure(type, coeffs.Length);
      if (meta.RecordCount == 0) continue;

      double[] recordMids = new double[meta.RecordCount];
      double[] recordRadii = new double[meta.RecordCount];
      int per = meta.RecordSizeDoubles;
      for (int r = 0; r < meta.RecordCount; r++)
      {
        int offset = r * per;
        recordMids[r] = coeffs[offset];
        recordRadii[r] = coeffs[offset + 1];
      }

      segments.Add(new SpkSegment(
        new BodyId(target), new BodyId(center), new FrameId(frame), type,
        start, stop, initial - 1, coeffs.Length, coeffs,
        meta.RecordCount, meta.Degree, recordMids, recordRadii, meta.ComponentsPerSet, meta.RecordSizeDoubles));
    }
    return new SpkKernel(segments);
  }

  /// <summary>
  /// Parse an SPK file lazily using an ephemeris data source. Coefficients are not fully materialized; only
  /// MID/RADIUS pairs are read for each record. Remaining coefficients are pulled on-demand during evaluation.
  /// </summary>
  public static SpkKernel ParseLazy(string filePath, bool memoryMap = true)
  {
    IEphemerisDataSource dataSource = memoryMap ? EphemerisDataSource.MemoryMapped(filePath) : EphemerisDataSource.FromStream(File.OpenRead(filePath));
    using var fs = File.OpenRead(filePath);
    using var daf = FullDafReader.Open(fs, leaveOpen: true);

    var segments = new List<SpkSegment>();
    double[] header = new double[2]; // reusable buffer for MID/RADIUS

    foreach (var seg in daf.EnumerateSegments())
    {
      if (seg.Dc.Length < 2 || seg.Ic.Length < 6) continue;
      double start = seg.Dc[0];
      double stop = seg.Dc[1];
      int target = seg.Ic[0];
      int center = seg.Ic[1];
      int frame = seg.Ic[2];
      int type = seg.Ic[3];
      int initial = seg.Ic[4];
      int final = seg.Ic[5];
      if (type is not (2 or 3) || initial <= 0 || final < initial) continue;

      int totalDoubles = final - initial + 1;
      var meta = InferRecordStructure(type, totalDoubles);
      if (meta.RecordCount == 0) continue;

      double[] recordMids = new double[meta.RecordCount];
      double[] recordRadii = new double[meta.RecordCount];

      for (int r = 0; r < meta.RecordCount; r++)
      {
        long recordStart = initial + r * meta.RecordSizeDoubles;
        dataSource.ReadDoubles(recordStart, header);
        recordMids[r] = header[0];
        recordRadii[r] = header[1];
      }

      segments.Add(new SpkSegment(
        new BodyId(target), new BodyId(center), new FrameId(frame), type,
        start, stop,
        initial - 1,
        totalDoubles,
        Array.Empty<double>(),
        meta.RecordCount, meta.Degree, recordMids, recordRadii, meta.ComponentsPerSet, meta.RecordSizeDoubles,
        dataSource, initial, final, Lazy: true));
    }
    return new SpkKernel(segments);
  }

  static double[] ReadDoubleRange(Stream stream, int initialAddress, int finalAddress)
  {
    if (!stream.CanSeek) throw new InvalidOperationException("Stream must be seekable");
    int count = finalAddress - initialAddress + 1;
    var result = new double[count];
    byte[] buf = new byte[8];
    for (int i = 0; i < count; i++)
    {
      int address = initialAddress + i; // 1-based
      long recordIndex = (address - 1) / 128;
      int wordInRecord = (address - 1) % 128;
      long byteOffset = recordIndex * 1024 + wordInRecord * 8;
      stream.Seek(byteOffset, SeekOrigin.Begin);
      if (stream.Read(buf, 0, 8) != 8) throw new EndOfStreamException();
      long bits = BinaryPrimitives.ReadInt64LittleEndian(buf);
      result[i] = BitConverter.Int64BitsToDouble(bits);
    }
    return result;
  }

  struct RecordInference(int RecordCount, int Degree, int ComponentsPerSet, int RecordSizeDoubles)
  {
    public int RecordCount = RecordCount;
    public int Degree = Degree;
    public int ComponentsPerSet = ComponentsPerSet;
    public int RecordSizeDoubles = RecordSizeDoubles;
  }

  static RecordInference InferRecordStructure(int type, int totalDoubles)
  {
    int k = type == 2 ? 3 : 6;
    for (int deg = 50; deg >= 1; deg--)
    {
      int per = 2 + k * (deg + 1);
      if (totalDoubles % per == 0)
      {
        int records = totalDoubles / per;
        return new RecordInference(records, deg, k, per);
      }
    }
    if (type == 2 && totalDoubles % 3 == 0)
    {
      int n1 = totalDoubles / 3;
      return new RecordInference(1, n1 - 1, 3, totalDoubles);
    }
    if (type == 3 && totalDoubles % 6 == 0)
    {
      int n1 = totalDoubles / 6;
      return new RecordInference(1, n1 - 1, 6, totalDoubles);
    }
    return new RecordInference(0, 0, 0, 0);
  }
}
