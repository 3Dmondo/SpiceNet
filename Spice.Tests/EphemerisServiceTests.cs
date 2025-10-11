using Shouldly;
using Spice.Core;
using Spice.Ephemeris;

namespace Spice.Tests;

public class EphemerisServiceTests
{
  static void WriteType2Spk(string path, double start, double stop, int target, int center, int frame, double x, double y, double z)
  {
    // Type2 degree 0 (constant) => 3*(N+1)=3 coefficients (x,y,z constants)
    const int nd = 2; // start, stop
    const int ni = 6; // target, center, frame, type, initial, final
    using var fs = File.Create(path);
    using var bw = new BinaryWriter(fs, System.Text.Encoding.ASCII, leaveOpen:true);
    bw.Write(System.Text.Encoding.ASCII.GetBytes("DAF/SPK "));
    WriteInt(bw, nd);
    WriteInt(bw, ni);
    WriteInt(bw, 0); // records unused
    WriteInt(bw, 1); // one summary
    WriteInt(bw, 0); WriteInt(bw, 0); // reserved
    // summary doubles
    WriteDouble(bw, start); WriteDouble(bw, stop);
    // ints
    WriteInt(bw, target); WriteInt(bw, center); WriteInt(bw, frame); WriteInt(bw, 2); // type 2
    WriteInt(bw, 1); // initial address 1
    WriteInt(bw, 3); // final address 3
    // coefficients (x,y,z)
    WriteDouble(bw, x); WriteDouble(bw, y); WriteDouble(bw, z);
  }

  static void WriteInt(BinaryWriter bw, int v)
  {
    Span<byte> b = stackalloc byte[4];
    System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(b, v);
    bw.Write(b);
  }
  static void WriteDouble(BinaryWriter bw, double d)
  {
    Span<byte> b = stackalloc byte[8];
    System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(b, (ulong)BitConverter.DoubleToInt64Bits(d));
    bw.Write(b);
  }

  [Fact]
  public void TryGetState_ReturnsFalse_When_No_Coverage()
  {
    var root = Path.Combine(Path.GetTempPath(), "spk_nocover_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
      var spk = Path.Combine(root, "seg.bsp");
      WriteType2Spk(spk, 0, 10, target:10, center:0, frame:1, x:5, y:0, z:0);
      var metaPath = Path.Combine(root, "load.tm");
      File.WriteAllText(metaPath, $"\\begindata\nKERNELS_TO_LOAD = ( '{spk}' )");
      var service = new EphemerisService();
      service.Load(metaPath);
      service.TryGetState(new BodyId(10), new BodyId(0), new Instant(20), out var _).ShouldBeFalse();
    }
    finally { try { Directory.Delete(root, recursive:true); } catch { } }
  }
}
