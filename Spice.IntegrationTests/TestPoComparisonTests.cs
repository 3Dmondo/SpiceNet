using Shouldly;
using Spice.Core;
using Spice.Kernels;

namespace Spice.IntegrationTests;

public class TestPoComparisonTests
{
  const double PositionTolKm = 1e-6; // component absolute tolerance (km)
  const double VelocityTolKmPerSec = 1e-9; // component absolute tolerance (km/s)

  public static IEnumerable<object[]> EphemerisNumbers()
  {
    foreach (var e in EphemerisCatalog.ResolveSelection())
      yield return new object[] { e.Number };
  }

  [Theory]
  [MemberData(nameof(EphemerisNumbers))]
  public async Task TestPo_Golden_Component_Deltas(string ephNumber)
  {
    var entry = EphemerisCatalog.All.First(e => e.Number == ephNumber);
    var cache = EphemerisDataCache.CreateDefault();
    var ensured = await cache.EnsureAsync(entry);
    ensured.ShouldNotBeNull($"Failed to ensure ephemeris de{ephNumber} (download or cache failure).");
    var (testpoPath, bspPath) = ensured.Value;
    using var fs = File.OpenRead(bspPath);
    var kernel = RealSpkKernelParser.Parse(fs);

    int posSamples = 0, velSamples = 0;
    double posMaxErr = 0, posSumErr = 0;
    double velMaxErr = 0, velSumErr = 0;

    foreach (var comp in TestPoParser.ParseComponents(testpoPath, int.MaxValue))
    {
      var tdb = new Instant((long)Math.Round(comp.EtSeconds));
      var seg = kernel.Segments.FirstOrDefault(s => s.Target.Value == comp.TargetCode && s.Center.Value == comp.CenterCode && tdb.TdbSecondsFromJ2000 >= s.StartTdbSec && tdb.TdbSecondsFromJ2000 <= s.StopTdbSec);
      if (seg is null) continue; // no coverage
      var eval = SpkSegmentEvaluator.EvaluateState(seg, tdb);

      double predicted = comp.ComponentIndex switch
      {
        1 => eval.PositionKm.X,
        2 => eval.PositionKm.Y,
        3 => eval.PositionKm.Z,
        4 => eval.VelocityKmPerSec.X,
        5 => eval.VelocityKmPerSec.Y,
        6 => eval.VelocityKmPerSec.Z,
        _ => double.NaN
      };
      if (double.IsNaN(predicted)) continue;
      double err = Math.Abs(predicted - comp.Value);
      if (comp.ComponentIndex <= 3)
      {
        posSamples++; posSumErr += err; if (err > posMaxErr) posMaxErr = err;
      }
      else
      {
        velSamples++; velSumErr += err; if (err > velMaxErr) velMaxErr = err;
      }
    }

    // Assert we actually had data (no silent skip) – if not, mark as inconclusive failure.
    (posSamples > 0 || velSamples > 0).ShouldBeTrue($"Inconclusive: de{ephNumber} produced no comparable samples (no matching segments or unsupported types).");

    if (posSamples > 0)
    {
      var posMean = posSumErr / posSamples;
      posMaxErr.ShouldBeLessThanOrEqualTo(PositionTolKm, $"de{ephNumber} position component max error {posMaxErr:E} mean {posMean:E} samples={posSamples}");
    }
    if (velSamples > 0)
    {
      var velMean = velSumErr / velSamples;
      velMaxErr.ShouldBeLessThanOrEqualTo(VelocityTolKmPerSec, $"de{ephNumber} velocity component max error {velMaxErr:E} mean {velMean:E} samples={velSamples}");
    }
  }
}
