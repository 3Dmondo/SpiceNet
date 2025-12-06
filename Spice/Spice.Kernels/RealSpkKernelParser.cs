// CSPICE Port Reference: SPK Required Reading (conceptual) — implementation is original managed design.
using System.Buffers.Binary;
using Spice.Core;
using Spice.IO;

namespace Spice.Kernels;

/// <summary>
/// Real-layout SPK kernel parser (subset: segment data types 2 & 3) built atop <see cref="FullDafReader"/>.
/// Responsibilities:
/// 1. Enumerate DAF segment summaries (DC + IC) and filter supported SPK types (2,3).
/// 2. Retrieve raw coefficient double words via their 1-based DAF address range (INITIAL..FINAL).
/// 3. Interpret multi-record structure for uniform length Chebyshev records with trailer directory: INIT, INTLEN, RSIZE, N.
///    Record layout: [ MID, RADIUS, coefficient sets... ] with RSIZE = 2 + K*(DEG+1) where K = 3 (type2) or 6 (type3).
/// 4. Populate <see cref="SpkSegment"/> with per-record MID/RADIUS arrays enabling precise sub-interval evaluation.
/// 5. Support lazy mode: only MID/RADIUS headers and trailer metadata are read; coefficients fetched on demand.
/// </summary>
internal static class RealSpkKernelParser
{
  internal static SpkKernel Parse(Stream stream)
  {
    using var daf = FullDafReader.Open(stream, leaveOpen: true);
    bool little = daf.IsLittleEndian;
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

      int totalDoubles = final - initial + 1;
      if (totalDoubles < 4) continue;

      var all = ReadDoubleRange(stream, initial, final, little);

      double init = all[^4];
      double intLen = all[^3];
      double rSizeTrailer = all[^2];
      double nTrailer = all[^1];

      if (rSizeTrailer < 2 || nTrailer < 1) continue;
      int rsize = (int)rSizeTrailer;
      int n = (int)nTrailer;
      long expectedPayload = (long)rsize * n;
      if (expectedPayload + 4 != totalDoubles) continue;

      int k = type == 2 ? 3 : 6;
      int degree = (rsize - 2) / k - 1;
      if (degree < 0 || 2 + k * (degree + 1) != rsize) continue;

      int payloadCount = totalDoubles - 4;
      var payload = new double[payloadCount];
      Array.Copy(all, 0, payload, 0, payloadCount);

      double[] recordMids = new double[n];
      double[] recordRadii = new double[n];
      for (int r = 0; r < n; r++)
      {
        int offset = r * rsize;
        recordMids[r] = payload[offset];
        recordRadii[r] = payload[offset + 1];
      }

      segments.Add(new SpkSegment(
        new BodyId(target), new BodyId(center), new FrameId(frame), type,
        start, stop,
        initial - 1,
        payload.Length,
        payload,
        RecordCount: n,
        Degree: degree,
        RecordMids: recordMids,
        RecordRadii: recordRadii,
        ComponentsPerSet: k,
        RecordSizeDoubles: rsize,
        Init: init,
        IntervalLength: intLen,
        TrailerRecordSize: rsize,
        TrailerRecordCount: n
      ));
    }
    return new SpkKernel(segments);
  }

  internal static SpkKernel ParseLazy(string filePath, bool memoryMap = true)
  {
    using var fs = File.OpenRead(filePath);
    using var daf = FullDafReader.Open(fs, leaveOpen: true);
    bool little = daf.IsLittleEndian;
    IEphemerisDataSource dataSource = memoryMap ? EphemerisDataSource.MemoryMapped(filePath, little) : EphemerisDataSource.FromStream(File.OpenRead(filePath), little);

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

      int totalDoubles = final - initial + 1;
      if (totalDoubles < 4) continue;

      double init = dataSource.ReadDouble(final - 3);
      double intLen = dataSource.ReadDouble(final - 2);
      double rSizeTrailer = dataSource.ReadDouble(final - 1);
      double nTrailer = dataSource.ReadDouble(final);
      if (rSizeTrailer < 2 || nTrailer < 1) continue;
      int rsize = (int)rSizeTrailer;
      int n = (int)nTrailer;
      long expectedPayload = (long)rsize * n;
      if (expectedPayload + 4 != totalDoubles) continue;

      int k = type == 2 ? 3 : 6;
      int degree = (rsize - 2) / k - 1;
      if (degree < 0 || 2 + k * (degree + 1) != rsize) continue;

      double[] recordMids = new double[n];
      double[] recordRadii = new double[n];
      for (int r = 0; r < n; r++)
      {
        long recStart = initial + r * (long)rsize;
        recordMids[r] = dataSource.ReadDouble(recStart);
        recordRadii[r] = dataSource.ReadDouble(recStart + 1);
      }

      segments.Add(new SpkSegment(
        new BodyId(target), new BodyId(center), new FrameId(frame), type,
        start, stop,
        initial - 1,
        0,
        Array.Empty<double>(),
        RecordCount: n,
        Degree: degree,
        RecordMids: recordMids,
        RecordRadii: recordRadii,
        ComponentsPerSet: k,
        RecordSizeDoubles: rsize,
        Init: init,
        IntervalLength: intLen,
        TrailerRecordSize: rsize,
        TrailerRecordCount: n,
        DataSource: dataSource,
        DataSourceInitialAddress: initial,
        DataSourceFinalAddress: final,
        Lazy: true
      ));
    }
    return new SpkKernel(segments);
  }

  static double[] ReadDoubleRange(Stream stream, int initialAddress, int finalAddress, bool littleEndian)
  {
    if (!stream.CanSeek) throw new InvalidOperationException("Stream must be seekable");
    int count = finalAddress - initialAddress + 1;
    var result = new double[count];
    byte[] buf = new byte[8];
    for (int i = 0; i < count; i++)
    {
      int address = initialAddress + i;
      long recordIndex = (address - 1L) / 128L;
      int wordInRecord = (address - 1) % 128;
      long byteOffset = recordIndex * 1024L + wordInRecord * 8L;
      stream.Seek(byteOffset, SeekOrigin.Begin);
      if (stream.Read(buf, 0, 8) != 8) throw new EndOfStreamException();
      long raw = BinaryPrimitives.ReadInt64LittleEndian(buf);
      if (!littleEndian) raw = BinaryPrimitives.ReverseEndianness(raw);
      result[i] = BitConverter.Int64BitsToDouble(raw);
    }
    return result;
  }
}
