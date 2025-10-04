// CSPICE Port Reference: N/A (original managed design)
using System.Globalization;
using Spice.Core;

namespace Spice.IO;

/// <summary>
/// Loader for a trimmed JPL 'testpo' style reference file subset. Expected simplified line format (one per epoch/body):
///   BODY=<id> JD=<julian-date-tdb> X=<km> Y=<km> Z=<km> VX=<km/s> VY=<km/s> VZ=<km/s>
/// Lines starting with '#' or blank are ignored. Units: km and km/s. Epoch converted to TDB seconds past J2000.
/// This is a minimal subset format for Prompt 16 tests; can be extended to support original testpo block style.
/// </summary>
public static class TestPoLoader
{
  public sealed record TestPoState(BodyId Body, double EpochTdbSec, Vector3d PositionKm, Vector3d VelocityKmPerSec);

  const double J2000_JD_TDB = 2451545.0; // JD(TDB) at J2000 epoch (2000-01-01 12:00:00 TDB)
  const double SecondsPerDay = 86400.0;

  /// <summary>Parse a simplified testpo reference file.</summary>
  public static IReadOnlyList<TestPoState> Parse(Stream stream)
  {
    using var reader = new StreamReader(stream, leaveOpen: true);
    var list = new List<TestPoState>();
    string? line;
    while ((line = reader.ReadLine()) is not null)
    {
      line = line.Trim();
      if (line.Length == 0 || line.StartsWith('#')) continue;
      // Tokenize by spaces, each token key=value
      var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
      int? body = null; double? jd = null; double? x=null,y=null,z=null,vx=null,vy=null,vz=null;
      foreach (var t in tokens)
      {
        var kv = t.Split('=',2);
        if (kv.Length !=2) continue;
        string key = kv[0].ToUpperInvariant(); string val = kv[1];
        double ParseD() => double.Parse(val, CultureInfo.InvariantCulture);
        switch(key)
        {
          case "BODY": body = int.Parse(val, CultureInfo.InvariantCulture); break;
          case "JD": jd = ParseD(); break;
          case "X": x = ParseD(); break;
          case "Y": y = ParseD(); break;
          case "Z": z = ParseD(); break;
          case "VX": vx = ParseD(); break;
          case "VY": vy = ParseD(); break;
          case "VZ": vz = ParseD(); break;
        }
      }
      if (body is null || jd is null || x is null || y is null || z is null || vx is null || vy is null || vz is null)
        throw new InvalidDataException($"Malformed testpo line: '{line}'");
      double epochTdbSec = (jd.Value - J2000_JD_TDB) * SecondsPerDay;
      list.Add(new TestPoState(new BodyId(body.Value), epochTdbSec, new Vector3d(x.Value,y.Value,z.Value), new Vector3d(vx.Value,vy.Value,vz.Value)));
    }
    return list;
  }
}
