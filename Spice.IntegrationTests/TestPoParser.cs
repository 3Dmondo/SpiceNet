// Original design: Parser for JPL testpo reference files.
using System.Globalization;

namespace Spice.IntegrationTests;

internal sealed record TestPoCoordinate(
  int EphemerisSeries,
  double JulianDay,
  int TargetCode,
  int CenterCode,
  int ComponentIndex, // 1..6 => x,y,z,vx,vy,vz
  double Value
);

internal sealed record TestPoState(
  int TargetCode,
  int CenterCode,
  double JulianDay,
  double X, double Y, double Z,
  double Vx, double Vy, double Vz
)
{
  public double EtSeconds => (JulianDay - 2451545.0) * 86400.0; // TDB seconds past J2000
}

/// <summary>
/// Single component reference (one line) from testpo file. testpo does not necessarily provide all 6 components per epoch/target/center.
/// </summary>
internal sealed record TestPoComponent(
  int TargetCode,
  int CenterCode,
  double JulianDay,
  int ComponentIndex,
  double Value
)
{
  public double EtSeconds => (JulianDay - 2451545.0) * 86400.0;
}

internal static class TestPoParser
{
  /// <summary>
  /// Legacy aggregation (requires all 6 components) – retained for reference but unused now.
  /// </summary>
  public static IEnumerable<TestPoState> ParseStates(string path, int maxStates)
  {
    using var reader = new StreamReader(path);
    string? line;
    bool afterHeader = false;
    var map = new Dictionary<(double jd, int t, int c), double[]>(capacity: 512);
    int emitted = 0;

    while ((line = reader.ReadLine()) != null)
    {
      if (!afterHeader)
      {
        if (line.Trim() == "EOT") afterHeader = true; else continue;
      }
      if (string.IsNullOrWhiteSpace(line)) continue;
      var parts = SplitColumns(line);
      if (parts.Length < 7) continue;
      if (!int.TryParse(parts[0], out _)) continue; // eph not used
      if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var jd)) continue;
      if (!int.TryParse(parts[3], out var tcode)) continue;
      if (!int.TryParse(parts[4], out var ccode)) continue;
      if (!int.TryParse(parts[5], out var compIndex)) continue;
      if (!double.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) continue;
      if (compIndex is < 1 or > 6) continue;

      var key = (jd, tcode, ccode);
      if (!map.TryGetValue(key, out var arr))
      {
        arr = new double[6];
        for (int i = 0; i < 6; i++) arr[i] = double.NaN;
        map[key] = arr;
      }
      arr[compIndex - 1] = value;
      if (AllPresent(arr))
      {
        yield return new TestPoState(tcode, ccode, jd, arr[0], arr[1], arr[2], arr[3], arr[4], arr[5]);
        emitted++;
        map.Remove(key);
        if (emitted >= maxStates) yield break;
      }
    }
  }

  /// <summary>
  /// Parse individual component lines (most general). Returns at most <paramref name="maxComponents"/> records.
  /// </summary>
  public static IEnumerable<TestPoComponent> ParseComponents(string path, int maxComponents)
  {
    using var reader = new StreamReader(path);
    string? line;
    bool afterHeader = false;
    int count = 0;
    while ((line = reader.ReadLine()) != null)
    {
      if (!afterHeader)
      {
        if (line.Trim() == "EOT") afterHeader = true; else continue;
      }
      if (string.IsNullOrWhiteSpace(line)) continue;
      var parts = SplitColumns(line);
      if (parts.Length < 7) continue;
      if (!int.TryParse(parts[0], out _)) continue; // eph not needed
      if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var jd)) continue;
      if (!int.TryParse(parts[3], out var tcode)) continue;
      if (!int.TryParse(parts[4], out var ccode)) continue;
      if (!int.TryParse(parts[5], out var compIndex)) continue;
      if (!double.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) continue;
      if (compIndex is < 1 or > 6) continue;
      yield return new TestPoComponent(tcode, ccode, jd, compIndex, value);
      if (++count >= maxComponents) yield break;
    }
  }

  static string[] SplitColumns(string line)
    => line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

  static bool AllPresent(double[] arr)
  {
    for (int i = 0; i < arr.Length; i++) if (double.IsNaN(arr[i])) return false;
    return true;
  }
}
