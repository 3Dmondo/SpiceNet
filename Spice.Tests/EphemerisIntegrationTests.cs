using Shouldly;
using Spice.Core;
using Spice.Ephemeris;

namespace Spice.Tests;

public class EphemerisIntegrationTests
{
  static void WriteType2SpkDegree2(string path, double start, double stop, int target, int center, int frame, double[] cx, double[] cy, double[] cz)
  {
    // Degree 2 => N+1 = 3 coefficients per component => total 9 doubles.
    if (cx.Length != 3 || cy.Length != 3 || cz.Length != 3) throw new ArgumentException("Expect 3 coeffs each");
    const int nd = 2; // start, stop
    const int ni = 6; // target, center, frame, type, initial, final
    using var fs = File.Create(path);
    using var bw = new BinaryWriter(fs, System.Text.Encoding.ASCII, leaveOpen:true);
    bw.Write(System.Text.Encoding.ASCII.GetBytes("DAF/SPK "));
    WriteInt(bw, nd); WriteInt(bw, ni); WriteInt(bw, 0); WriteInt(bw, 1); WriteInt(bw,0); WriteInt(bw,0);
    WriteDouble(bw, start); WriteDouble(bw, stop);
    WriteInt(bw, target); WriteInt(bw, center); WriteInt(bw, frame); WriteInt(bw, 2); // type 2
    WriteInt(bw, 1); WriteInt(bw, 9); // addresses
    foreach (var v in cx) WriteDouble(bw, v);
    foreach (var v in cy) WriteDouble(bw, v);
    foreach (var v in cz) WriteDouble(bw, v);
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

  static void WriteLsk(string path)
  {
    var content = """
\begindata
DELTET/DELTA_AT = ( 32, @1999-JAN-01
                    33, @2006-JAN-01 )
""";
    File.WriteAllText(path, content);
  }

  [Fact]
  public void Load_MetaKernel_And_Query_State_MidEpoch()
  {
    var root = Path.Combine(Path.GetTempPath(), "ephem_int_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
      var lskPath = Path.Combine(root, "leapseconds.tls");
      WriteLsk(lskPath);
      var spkPath = Path.Combine(root, "segment.bsp");
      // Coefficients for degree 2 Chebyshev (position); velocity derived.
      // X(tau)= 10 + 5*tau + 2*(2tau^2-1) => at tau=0 pos=8, derivative wrt tau = 5, dtau/dt=1/radius => vx=5/radius
      // Y(tau)= 0 + 1*tau + 0*(...) => pos=0, vy=1/radius
      // Z(tau)= 1 -2*tau + 1*(2tau^2 -1) = 2tau^2 -2tau => pos=0, derivative wrt tau = -2 => vz=-2/radius
      double start = 0, stop = 100; double radius = (stop-start)/2.0; // 50
      WriteType2SpkDegree2(spkPath, start, stop, target:499, center:0, frame:1,
        cx: [10,5,2], cy:[0,1,0], cz:[1,-2,1]);

      var metaPath = Path.Combine(root, "load.tm");
      File.WriteAllText(metaPath, $"\\begindata\nKERNELS_TO_LOAD = ( '{lskPath}' '{spkPath}' )");

      var service = new EphemerisService();
      service.Load(metaPath);

      var t = new Instant(50); // midpoint -> tau=0
      var state = service.GetState(new BodyId(499), new BodyId(0), t);
      state.PositionKm.X.ShouldBe(8d, 1e-12);
      state.PositionKm.Y.ShouldBe(0d, 1e-12);
      state.PositionKm.Z.ShouldBe(0d, 1e-12);
      state.VelocityKmPerSec.X.ShouldBe(5.0 / radius, 1e-12);
      state.VelocityKmPerSec.Y.ShouldBe(1.0 / radius, 1e-12);
      state.VelocityKmPerSec.Z.ShouldBe(-2.0 / radius, 1e-12);
    }
    finally
    {
      try { Directory.Delete(root, recursive:true); } catch { }
    }
  }
}
