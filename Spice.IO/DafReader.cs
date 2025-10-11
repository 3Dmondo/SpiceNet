// CSPICE Port Reference: N/A (original managed design)
using System.Buffers.Binary;

namespace Spice.IO;

/// <summary>
/// Minimal DAF (Double precision Array File) reader tailored for subset of SPK needs (MVP synthetic fixtures).
/// Supports reading: identification word, ND, NI, RECORDS count and enumerating a contiguous block of segment summaries.
/// Not a full implementation of NAIF DAF architecture yet.
/// </summary>
internal sealed class DafReader : IDisposable
{
  const int IdWordLength = 8; // "DAF/SPK " (padded)

  readonly Stream _stream;
  readonly BinaryReader _br;

  internal int Nd { get; }
  internal int Ni { get; }
  internal int RecordCount { get; }
  internal long SummariesOffset { get; }
  internal int SummaryCount { get; }

  DafReader(Stream stream, int nd, int ni, int recordCount, int summaryCount, long summariesOffset)
  {
    _stream = stream;
    _br = new BinaryReader(stream, System.Text.Encoding.ASCII, leaveOpen: true);
    Nd = nd;
    Ni = ni;
    RecordCount = recordCount;
    SummaryCount = summaryCount;
    SummariesOffset = summariesOffset;
  }

  /// <summary>
  /// Open a DAF stream positioned at start. Synthetic header layout (little endian):
  /// bytes[0..7] IDWORD ASCII (must start with "DAF/SPK"),
  /// int32 ND, int32 NI, int32 RECORDS, int32 SUMMARY_COUNT, (reserved int32 x2),
  /// followed immediately by summaries (ND doubles then NI int32s) repeated SUMMARY_COUNT times.
  /// </summary>
  internal static DafReader Open(Stream stream)
  {
    if (!stream.CanRead || !stream.CanSeek) throw new ArgumentException("Stream must be seekable & readable", nameof(stream));
    stream.Seek(0, SeekOrigin.Begin);
    Span<byte> id = stackalloc byte[IdWordLength];
    if (stream.Read(id) != IdWordLength) throw new EndOfStreamException();
    var idWord = System.Text.Encoding.ASCII.GetString(id);
    if (!idWord.StartsWith("DAF/SPK")) throw new InvalidDataException($"Invalid ID word '{idWord}'");

    Span<byte> buf = stackalloc byte[4];
    int nd = ReadInt(stream, buf);
    int ni = ReadInt(stream, buf);
    int records = ReadInt(stream, buf);
    int summaryCount = ReadInt(stream, buf);
    // consume reserved padding (2 ints)
    ReadInt(stream, buf); ReadInt(stream, buf);

    long summariesOffset = stream.Position;

    return new DafReader(stream, nd, ni, records, summaryCount, summariesOffset);
  }

  static int ReadInt(Stream s, Span<byte> tmp)
  {
    if (s.Read(tmp) != 4) throw new EndOfStreamException();
    return BinaryPrimitives.ReadInt32LittleEndian(tmp);
  }

  /// <summary>
  /// Read all summaries returning collection of raw double/int arrays.
  /// </summary>
  internal IEnumerable<DafSegmentSummary> ReadSummaries()
  {
    _stream.Seek(SummariesOffset, SeekOrigin.Begin);
    var result = new List<DafSegmentSummary>(SummaryCount);
    var tmp8 = new byte[8];
    var tmp4 = new byte[4];
    for (int i = 0; i < SummaryCount; i++)
    {
      var d = new double[Nd];
      for (int k = 0; k < Nd; k++)
      {
        if (_stream.Read(tmp8, 0, 8) != 8) throw new EndOfStreamException();
        ulong bits = BinaryPrimitives.ReadUInt64LittleEndian(tmp8);
        d[k] = BitConverter.Int64BitsToDouble((long)bits);
      }
      var ints = new int[Ni];
      for (int k = 0; k < Ni; k++)
      {
        if (_stream.Read(tmp4, 0, 4) != 4) throw new EndOfStreamException();
        ints[k] = BinaryPrimitives.ReadInt32LittleEndian(tmp4);
      }
      result.Add(new DafSegmentSummary(d, ints));
    }
    return result;
  }

  public void Dispose()
  {
    _br.Dispose();
  }
}

/// <summary>
/// Raw DAF segment summary: ND double precision components followed by NI integer components.
/// </summary>
internal sealed record DafSegmentSummary(double[] Doubles, int[] Integers);
