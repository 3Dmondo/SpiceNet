// CSPICE Port Reference: NAIF DAF Required Reading (conceptual) — implementation is original managed design focused on
// required subset (summary + name record traversal). This reader follows the DAF record model: file record, optional
// reserved/comment records (ignored), then a doubly linked list of summary/name record pairs. Control words (NEXT, PREV,
// NSUM) in summary records are stored as double precision values per spec. Synthetic unit tests originally wrote them
// as raw 32-bit ints; we support both encodings heuristically.

using System.Buffers.Binary;
using System.Text;

namespace Spice.IO;

public sealed class FullDafReader : IDisposable
{
  const int RecordBytes = 1024;
  const int WordBytes = 8;
  const int WordsPerRecord = RecordBytes / WordBytes; // 128
  const int SegmentNameLength = 40; // NC for ND=2, NI=6 (SPK typical)

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
  /// <summary>True if underlying DAF numeric storage is little-endian.</summary>
  public bool IsLittleEndian => _isLittleEndian;

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

    int ndLE = BinaryPrimitives.ReadInt32LittleEndian(fileRec.Slice(8,4));
    int niLE = BinaryPrimitives.ReadInt32LittleEndian(fileRec.Slice(12,4));
    int ndBE = BinaryPrimitives.ReadInt32BigEndian(fileRec.Slice(8,4));
    int niBE = BinaryPrimitives.ReadInt32BigEndian(fileRec.Slice(12,4));

    bool little = ndLE is >0 and <256 && niLE is >0 and <256;
    bool big = ndBE is >0 and <256 && niBE is >0 and <256;
    if (little == big) throw new InvalidDataException("Unable to determine DAF endianness (ambiguous ND/NI)");

    bool isLittle = little;
    int nd = isLittle ? ndLE : ndBE;
    int ni = isLittle ? niLE : niBE;

    string ifname = Encoding.ASCII.GetString(fileRec.Slice(16, 60));

    // Forward/backward summary record numbers may appear as 32-bit ints in synthetic test builder.
    int fward = ReadInt(fileRec, 76, isLittle);
    int bward = ReadInt(fileRec, 80, isLittle);

    return new FullDafReader(stream, leaveOpen, idWord, nd, ni, ifname, fward, bward, isLittle);
  }

  static int ReadInt(ReadOnlySpan<byte> buffer, int offset, bool little)
    => little ? BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(offset,4)) : BinaryPrimitives.ReadInt32BigEndian(buffer.Slice(offset,4));

  static double ReadDouble(ReadOnlySpan<byte> buffer, int offset, bool little)
    => BitConverter.Int64BitsToDouble(little
      ? BinaryPrimitives.ReadInt64LittleEndian(buffer.Slice(offset,8))
      : BinaryPrimitives.ReadInt64BigEndian(buffer.Slice(offset,8)));

  /// <summary>
  /// Enumerate raw segments (arrays) discovered in the file. Each item exposes:
  /// DC (double components), IC (integer components), segment name, and the initial/final addresses.
  /// No element (coefficient) data are read; consumer may later map the address range to element records.
  /// </summary>
  public IEnumerable<(double[] Dc, int[] Ic, string Name, int InitialAddress, int FinalAddress)> EnumerateSegments()
  {
    if (Nd <= 0 || Ni <= 0) yield break;
    int rec = FirstSummaryRecord;
    if (rec <= 0) yield break; // synthetic & real tests always set this
    while (rec != 0)
    {
      var (summaries, nextRec) = ReadSummaryRecord(rec);
      foreach (var s in summaries)
        yield return (s.Dc, s.Ic, s.Name, s.InitialAddress, s.FinalAddress);
      rec = nextRec;
    }
  }

  record SegmentRaw(double[] Dc, int[] Ic, string Name, int InitialAddress, int FinalAddress);

  (List<SegmentRaw> summaries, int next) ReadSummaryRecord(int recordNumber)
  {
    byte[] summaryBuf = new byte[RecordBytes];
    byte[] nameBuf = new byte[RecordBytes];
    ReadRecord(recordNumber, summaryBuf);

    // Control words are the first three double precision words (NEXT, PREV, NSUM) per spec.
    int next = ReadControlWord(summaryBuf, 0);
    int prev = ReadControlWord(summaryBuf, 1); _ = prev;
    int nsum = ReadControlWord(summaryBuf, 2);

    if (nsum <= 0) return (new List<SegmentRaw>(), next);
    if (nsum > 10000) throw new InvalidDataException("NSUM unrealistic");

    int summaryWordSpan = Nd + ((Ni + 1) / 2);
    int capacityWords = WordsPerRecord - 3;
    if (summaryWordSpan * nsum > capacityWords) throw new InvalidDataException("Summary record overflow");

    ReadRecord(recordNumber + 1, nameBuf); // paired name record

    var list = new List<SegmentRaw>(nsum);
    int wordIndex = 3; // after control area
    for (int i = 0; i < nsum; i++)
    {
      double[] dc = new double[Nd];
      for (int d = 0; d < Nd; d++)
      {
        int offsetBytes = wordIndex * WordBytes;
        dc[d] = ReadDouble(summaryBuf, offsetBytes, _isLittleEndian);
        wordIndex++;
      }
      int[] ic = new int[Ni];
      int remaining = Ni; int icPos = 0;
      while (remaining > 0)
      {
        int offsetBytes = wordIndex * WordBytes;
        var word = summaryBuf.AsSpan(offsetBytes, 8);
        int a = _isLittleEndian ? BinaryPrimitives.ReadInt32LittleEndian(word[..4]) : BinaryPrimitives.ReadInt32BigEndian(word[..4]);
        ic[icPos++] = a; remaining--;
        if (remaining > 0)
        {
          int b = _isLittleEndian ? BinaryPrimitives.ReadInt32LittleEndian(word.Slice(4,4)) : BinaryPrimitives.ReadInt32BigEndian(word.Slice(4,4));
            ic[icPos++] = b; remaining--;
        }
        wordIndex++;
      }
      int nameOffset = i * SegmentNameLength;
      string rawName = nameOffset + SegmentNameLength <= nameBuf.Length
        ? Encoding.ASCII.GetString(nameBuf, nameOffset, SegmentNameLength)
        : string.Empty;
      string name = rawName.TrimEnd('\0', ' ');
      int initial = ic.Length >= 6 ? ic[4] : 0;
      int final = ic.Length >= 6 ? ic[5] : 0;
      list.Add(new SegmentRaw(dc, ic, name, initial, final));
    }
    return (list, next);
  }

  int ReadControlWord(byte[] record, int controlWordIndex)
  {
    int byteOffset = controlWordIndex * WordBytes;
    var span = record.AsSpan(byteOffset, 8);
    // Detect synthetic encoding: upper 32 bits zero (or all zero) and lower 32 bits non-zero => treat as int32
    int low = _isLittleEndian ? BinaryPrimitives.ReadInt32LittleEndian(span[..4]) : BinaryPrimitives.ReadInt32BigEndian(span.Slice(4,4));
    int high = _isLittleEndian ? BinaryPrimitives.ReadInt32LittleEndian(span.Slice(4,4)) : BinaryPrimitives.ReadInt32BigEndian(span[..4]);
    if (high == 0 && low != 0) return low; // synthetic test path

    double dv = ReadDouble(record, byteOffset, _isLittleEndian);
    if (!double.IsNaN(dv) && Math.Abs(dv) < int.MaxValue)
    {
      long lv = (long)Math.Round(dv);
      if (Math.Abs(dv - lv) < 1e-12) return (int)lv;
    }
    return low; // fallback (likely zero)
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
