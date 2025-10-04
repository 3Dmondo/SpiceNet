// CSPICE Port Reference: NAIF DAF Required Reading (conceptual) — implementation is original managed design focused on
// required subset (summary + name record traversal). This is an initial "full" DAF reader capable of following the
// summary/name record doubly-linked list and enumerating segment (array) descriptors. Endianness detection is heuristic
// and will be extended when integrating real kernels.

using System.Buffers.Binary;
using System.Text;

namespace Spice.IO;

/// <summary>
/// Experimental fuller DAF (Double precision Array File) reader needed for Phase 2 real SPK support.
/// Capabilities:
/// 1. Parses the DAF File Record (record 1) extracting identification word, ND (double components per summary),
///    NI (integer components), internal file name and the forward/backward summary record numbers.
/// 2. Traverses the doubly linked list of summary records; for every summary record reads its paired name record.
/// 3. Unpacks integer components (two 32-bit values per 8-byte word) and double components for each segment (array).
/// 4. Exposes an enumerator yielding raw descriptor data: double (DC) and integer (IC) arrays, segment name and the
///    initial/final 1-based double word address range (common SPK convention with ND=2, NI>=6).
/// 5. Performs basic structural validation (record bounds, overflow checks, name truncation) and supports native
///    little or big-endian files via heuristic detection from ND/NI fields.
///
/// This reader purposefully limits scope: it does not yet interpret higher-level SPK semantics nor load element
/// records. It supplies the foundation for Prompt 14 (real SPK segment parsing and multi-record coefficient access).
/// </summary>
public sealed class FullDafReader : IDisposable
{
  const int RecordBytes = 1024;
  const int WordBytes = 8;
  const int WordsPerRecord = RecordBytes / WordBytes; // 128
  const int SegmentNameLength = 40; // per NAIF spec

  readonly Stream _stream;
  readonly bool _leaveOpen;
  readonly bool _isLittleEndian;

  /// <summary>DAF identification word (e.g. "DAF/SPK ").</summary>
  public string IdWord { get; }
  /// <summary>Number of double precision components per segment summary (ND).</summary>
  public int Nd { get; }
  /// <summary>Number of integer components per segment summary (NI).</summary>
  public int Ni { get; }
  /// <summary>Internal file name (IFNAME) trimmed of trailing spaces.</summary>
  public string InternalFileName { get; }
  /// <summary>Record number of the first summary record (0 if none).</summary>
  public int FirstSummaryRecord { get; }
  /// <summary>Record number of the last summary record (0 if none).</summary>
  public int LastSummaryRecord { get; }

  FullDafReader(Stream stream, bool leaveOpen, string idWord, int nd, int ni, string ifname, int fward, int bward, bool little)
  {
    _stream = stream; _leaveOpen = leaveOpen; _isLittleEndian = little;
    IdWord = idWord; Nd = nd; Ni = ni; InternalFileName = ifname.Trim();
    FirstSummaryRecord = fward; LastSummaryRecord = bward;
  }

  /// <summary>
  /// Open an existing DAF stream positioned at its beginning. The caller remains owner of the stream
  /// unless <paramref name="leaveOpen"/> is false (default); in that case disposing the reader also disposes the stream.
  /// Performs basic endianness detection using ND/NI plausibility checks.
  /// </summary>
  /// <param name="stream">Seekable readable stream containing a DAF (e.g., SPK) file.</param>
  /// <param name="leaveOpen">True to keep the stream open after the reader is disposed.</param>
  public static FullDafReader Open(Stream stream, bool leaveOpen = false)
  {
    if (!stream.CanRead || !stream.CanSeek) throw new ArgumentException("Stream must be seekable & readable", nameof(stream));
    stream.Seek(0, SeekOrigin.Begin);
    Span<byte> fileRec = stackalloc byte[RecordBytes];
    if (stream.Read(fileRec) != RecordBytes) throw new EndOfStreamException();

    string idWord = Encoding.ASCII.GetString(fileRec[..8]);
    if (!idWord.StartsWith("DAF/")) throw new InvalidDataException($"Not a DAF file (IDWORD='{idWord}')");

    // Offsets per NAIF: IDWORD(0..7), ND(8..11), NI(12..15) in native endian 32-bit ints within the file record.
    // Implementation note: actual spec stores these as 8-byte ints or integers inside double words; for practicality we
    // read 32-bit ints directly from the byte offsets (common CSPICE build layout). We'll attempt both endian orders.
    int ndLE = BinaryPrimitives.ReadInt32LittleEndian(fileRec.Slice(8,4));
    int niLE = BinaryPrimitives.ReadInt32LittleEndian(fileRec.Slice(12,4));
    int ndBE = BinaryPrimitives.ReadInt32BigEndian(fileRec.Slice(8,4));
    int niBE = BinaryPrimitives.ReadInt32BigEndian(fileRec.Slice(12,4));

    bool little = ndLE is >0 and <64 && niLE is >0 and <64;
    bool big = ndBE is >0 and <64 && niBE is >0 and <64;
    if (little == big) throw new InvalidDataException("Unable to determine DAF endianness (ambiguous ND/NI)");

    bool isLittle = little;
    int nd = isLittle ? ndLE : ndBE;
    int ni = isLittle ? niLE : niBE;

    string ifname = Encoding.ASCII.GetString(fileRec.Slice(16, 60));

    int fward = ReadInt(fileRec, 76, isLittle); // forward summary record number
    int bward = ReadInt(fileRec, 80, isLittle); // backward summary record number

    return new FullDafReader(stream, leaveOpen, idWord, nd, ni, ifname, fward, bward, isLittle);
  }

  static int ReadInt(ReadOnlySpan<byte> buffer, int offset, bool little)
    => little ? BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(offset,4)) : BinaryPrimitives.ReadInt32BigEndian(buffer.Slice(offset,4));

  record SegmentRaw(double[] Dc, int[] Ic, string Name, int InitialAddress, int FinalAddress);

  /// <summary>
  /// Enumerate raw segments (arrays) discovered in the file. Each item exposes:
  /// DC (double components), IC (integer components), segment name, and the initial/final addresses.
  /// No element (coefficient) data are read; consumer may later map the address range to element records.
  /// </summary>
  public IEnumerable<(double[] Dc, int[] Ic, string Name, int InitialAddress, int FinalAddress)> EnumerateSegments()
  {
    if (Nd <= 0 || Ni <= 0) yield break;

    int rec = FirstSummaryRecord;
    if (rec <= 0) yield break; // no summaries

    var seenNames = new HashSet<string>(StringComparer.Ordinal);

    while (rec != 0)
    {
      var (summaries, nextRec) = ReadSummaryRecord(rec);
      foreach (var s in summaries)
        if (seenNames.Add(s.Name))
          yield return (s.Dc, s.Ic, s.Name, s.InitialAddress, s.FinalAddress);
      rec = nextRec;
    }
  }

  (List<SegmentRaw> summaries, int next) ReadSummaryRecord(int recordNumber)
  {
    // Allocate reusable buffers on the heap to avoid large stack usage inside loops (CA2014).
    byte[] summaryBuf = new byte[RecordBytes];
    byte[] nameBuf = new byte[RecordBytes];

    ReadRecord(recordNumber, summaryBuf);

    int next = ReadInt(summaryBuf, 0, _isLittleEndian);
    int prev = ReadInt(summaryBuf, 8, _isLittleEndian);
    _ = prev; // reserved for future validation.
    int nsum = ReadInt(summaryBuf, 16, _isLittleEndian);

    if (nsum < 0 || nsum > 1000) throw new InvalidDataException("Unreasonable NSUM in summary record");

    int summaryWordSpan = Nd + ((Ni + 1) / 2); // words per packed summary
    int capacityWords = WordsPerRecord - 3; // after control area

    if (summaryWordSpan * nsum > capacityWords)
      throw new InvalidDataException("Summary record overflow");

    var list = new List<SegmentRaw>(nsum);

    ReadRecord(recordNumber + 1, nameBuf); // name record directly follows summary record

    int wordIndex = 3; // start after control area
    for (int i = 0; i < nsum; i++)
    {
      double[] dc = new double[Nd];
      for (int d = 0; d < Nd; d++)
      {
        int offsetBytes = wordIndex * WordBytes;
        // Assuming same endian for data words; big-endian support would branch here.
        dc[d] = BitConverter.Int64BitsToDouble(_isLittleEndian
          ? BinaryPrimitives.ReadInt64LittleEndian(summaryBuf.AsSpan(offsetBytes,8))
          : BinaryPrimitives.ReadInt64BigEndian(summaryBuf.AsSpan(offsetBytes,8)));
        wordIndex++;
      }

      int[] ic = new int[Ni];
      int intsRemaining = Ni;
      int icIndex = 0;
      while (intsRemaining > 0)
      {
        int offsetBytes = wordIndex * WordBytes;
        var word = summaryBuf.AsSpan(offsetBytes, 8);
        int a = _isLittleEndian ? BinaryPrimitives.ReadInt32LittleEndian(word.Slice(0,4)) : BinaryPrimitives.ReadInt32BigEndian(word.Slice(0,4));
        ic[icIndex++] = a;
        intsRemaining--;
        if (intsRemaining > 0)
        {
          int b = _isLittleEndian ? BinaryPrimitives.ReadInt32LittleEndian(word.Slice(4,4)) : BinaryPrimitives.ReadInt32BigEndian(word.Slice(4,4));
          ic[icIndex++] = b;
          intsRemaining--;
        }
        wordIndex++;
      }

      // Names are contiguous 40-char blocks in the name record matching summary order.
      int nameOffset = i * SegmentNameLength;
      if (nameOffset + SegmentNameLength > nameBuf.Length)
        throw new InvalidDataException("Name record truncation");
      string rawName = Encoding.ASCII.GetString(nameBuf, nameOffset, SegmentNameLength);
      string name = rawName.TrimEnd('\0', ' ');

      int initial = ic.Length >= 6 ? ic[4] : 0;
      int final = ic.Length >= 6 ? ic[5] : 0;

      list.Add(new SegmentRaw(dc, ic, name, initial, final));
    }

    return (list, next);
  }

  void ReadRecord(int recordNumber, Span<byte> destination)
  {
    long offset = (long)(recordNumber - 1) * RecordBytes;
    if (recordNumber <= 0 || offset + RecordBytes > _stream.Length) throw new InvalidDataException($"Record {recordNumber} out of range");
    _stream.Seek(offset, SeekOrigin.Begin);
    if (_stream.Read(destination) != RecordBytes) throw new EndOfStreamException();
  }

  /// <summary>Dispose the reader and optionally the underlying stream.</summary>
  public void Dispose()
  {
    if (!_leaveOpen) _stream.Dispose();
  }
}
