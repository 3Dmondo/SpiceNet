// CSPICE Port Reference: N/A (original managed design)
using System.Buffers.Binary;
using Spice.Core;
using Spice.IO;

namespace Spice.Kernels;

/// <summary>
/// Parser for synthetic minimal SPK (DAF-backed) kernels produced by test helpers.
/// Supports SPK data types 2 and 3 only. The underlying stream format is the simplified DAF
/// produced by DafTestBuilder: header + summaries then a contiguous coefficient block.
/// Summary mapping (ND=2, NI=6):
///   doubles: [ START_TDB_SEC, STOP_TDB_SEC ]
///   ints:    [ TARGET, CENTER, FRAME, DATA_TYPE, INITIAL_ADDR, FINAL_ADDR ]
/// Addresses are 1-based indices of double words within the coefficient block immediately
/// following all summaries.
/// </summary>
public static class SpkKernelParser
{
  public static SpkKernel Parse(Stream stream)
  {
    using var daf = DafReader.Open(stream);
    if (daf.Nd != 2 || daf.Ni < 6)
      throw new InvalidDataException($"Unsupported ND/NI combination (ND={daf.Nd}, NI={daf.Ni})");

    var summaries = daf.ReadSummaries();
    // After reading summaries the stream position points to coefficient block start in our synthetic format.
    long coeffStart = stream.Position;
    long bytesRemaining = stream.Length - coeffStart;
    if (bytesRemaining % 8 != 0) throw new InvalidDataException("Coefficient area not aligned to 8-byte doubles");
    int totalCoeffDoubles = (int)(bytesRemaining / 8);

    // Read entire coefficient area into a double[] buffer for slicing.
    var globalCoeffs = new double[totalCoeffDoubles];
    Span<byte> tmp8 = stackalloc byte[8];
    for (int i = 0; i < totalCoeffDoubles; i++)
    {
      if (stream.Read(tmp8) != 8) throw new EndOfStreamException();
      ulong bits = BinaryPrimitives.ReadUInt64LittleEndian(tmp8);
      globalCoeffs[i] = BitConverter.Int64BitsToDouble((long)bits);
    }

    var segmentList = new List<SpkSegment>();

    foreach (var s in summaries)
    {
      if (s.Doubles.Length < 2 || s.Integers.Length < 6)
        throw new InvalidDataException("Summary does not contain required elements");

      double start = s.Doubles[0];
      double stop = s.Doubles[1];
      int target = s.Integers[0];
      int center = s.Integers[1];
      int frame = s.Integers[2];
      int dataType = s.Integers[3];
      int initial = s.Integers[4];
      int final = s.Integers[5];

      if (dataType is not (2 or 3))
        throw new InvalidDataException($"Unsupported SPK segment data type {dataType} (only 2 & 3 allowed)");
      if (initial < 1 || final < initial)
        throw new InvalidDataException("Invalid coefficient address range");

      // addresses are 1-based inclusive
      int coeffCount = final - initial + 1;
      int offsetIndex = initial - 1;
      if (offsetIndex + coeffCount > globalCoeffs.Length)
        throw new InvalidDataException("Coefficient address range exceeds buffer");

      var coeffSlice = new double[coeffCount];
      Array.Copy(globalCoeffs, offsetIndex, coeffSlice, 0, coeffCount);

      segmentList.Add(new SpkSegment(
        new BodyId(target),
        new BodyId(center),
        new FrameId(frame),
        dataType,
        start,
        stop,
        offsetIndex,
        coeffCount,
        coeffSlice
      ));
    }

    return new SpkKernel(segmentList);
  }
}
