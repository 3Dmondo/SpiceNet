using System.Buffers.Binary;
using Shouldly;
using Spice.IO;

namespace Spice.Tests;

public sealed class FullDafReaderTests
{
  [Fact]
  public void Enumerates_Single_Segment_From_Minimal_Daf()
  {
    // Arrange: build minimal real-layout DAF with 1 summary + 1 name record.
    // Layout (little endian simplification):
    // Record 1 (file record) + Record 2 (summary record) + Record 3 (name record) => length 3*1024 bytes.
    const int nd = 2;
    const int ni = 6;
    const int firstSummaryRec = 2;
    const int lastSummaryRec = 2;
    const int nsum = 1;

    byte[] file = new byte[1024 * 3];

    // File record
    var id = System.Text.Encoding.ASCII.GetBytes("DAF/SPK ");
    id.CopyTo(file, 0);
    WriteInt(file, 8, nd);
    WriteInt(file, 12, ni);
    var ifname = System.Text.Encoding.ASCII.GetBytes("TEST DAF FILE".PadRight(60));
    ifname.CopyTo(file, 16);
    WriteInt(file, 76, firstSummaryRec);
    WriteInt(file, 80, lastSummaryRec);

    // Summary record (record 2)
    int summaryOffsetBytes = 1024 * (firstSummaryRec - 1);
    // Control area: NEXT=0, PREV=0, NSUM=1
    WriteInt(file, summaryOffsetBytes + 0, 0); // NEXT
    WriteInt(file, summaryOffsetBytes + 8, 0); // PREV
    WriteInt(file, summaryOffsetBytes + 16, nsum); // NSUM

    // Words after control start at word index 3.
    // Summary: ND doubles then NI ints packed 2 per word.
    int wordIndex = 3;
    double[] dc = [ 1000.0, 2000.0 ];
    foreach (var d in dc)
    {
      WriteDoubleWord(file, summaryOffsetBytes, wordIndex++, d);
    }
    // Integers: target, center, frame, type, initial, final
    int[] ic = [ 499, 0, 1, 2, 200, 300 ];
    for (int k = 0; k < ic.Length; )
    {
      int a = ic[k++];
      int b = k < ic.Length ? ic[k++] : 0;
      WritePackedIntsWord(file, summaryOffsetBytes, wordIndex++, a, b);
    }

    // Name record (record 3)
    int nameRecOffset = 1024 * (firstSummaryRec); // (recordNumber-1)*1024 with recordNumber=3
    var nameBytes = System.Text.Encoding.ASCII.GetBytes("TEST SEGMENT".PadRight(40));
    nameBytes.CopyTo(file, nameRecOffset + 0);

    using var ms = new MemoryStream(file, writable: false);
    using var rdr = FullDafReader.Open(ms, leaveOpen: false);

    // Act
    var segments = rdr.EnumerateSegments().ToList();

    // Assert
    segments.Count.ShouldBe(1);
    var seg = segments[0];
    seg.Dc.ShouldBe(dc);
    seg.Ic.ShouldBe(ic);
    seg.Name.ShouldBe("TEST SEGMENT");
    seg.InitialAddress.ShouldBe(200);
    seg.FinalAddress.ShouldBe(300);
  }

  static void WriteInt(byte[] buffer, int offset, int value)
  {
    BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset, 4), value);
  }
  static void WriteDoubleWord(byte[] file, int recordBase, int wordIndex, double value)
  {
    int byteOffset = recordBase + wordIndex * 8;
    BinaryPrimitives.WriteInt64LittleEndian(file.AsSpan(byteOffset, 8), BitConverter.DoubleToInt64Bits(value));
  }
  static void WritePackedIntsWord(byte[] file, int recordBase, int wordIndex, int a, int b)
  {
    int byteOffset = recordBase + wordIndex * 8;
    BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(byteOffset, 4), a);
    BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(byteOffset + 4, 4), b);
  }
}
