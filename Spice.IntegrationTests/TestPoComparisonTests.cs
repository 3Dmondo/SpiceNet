using Shouldly;
using Spice.Core;
using Spice.Kernels;
using Spice.Ephemeris;

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
    var AU = 0.149597870699999988e+09; // m

    var entry = EphemerisCatalog.All.First(e => e.Number == ephNumber);
    var cache = EphemerisDataCache.CreateDefault();
    var ensured = await cache.EnsureAsync(entry);
    ensured.ShouldNotBeNull($"Failed to ensure ephemeris de{ephNumber} (download or cache failure).");
    var (testpoPath, bspPath) = ensured.Value;

    using var svc = new EphemerisService();
    // Load the real kernel lazily so coefficients are fetched on demand; relative resolution available via TryGetState fallback.
    svc.LoadRealSpkLazy(bspPath, memoryMap: true);

    int posSamples = 0, velSamples = 0;
    double posMaxErr = 0, posSumErr = 0;
    double velMaxErr = 0, velSumErr = 0;

    foreach (var comp in TestPoParser.ParseComponents(testpoPath, int.MaxValue))
    {
      var instant = new Instant((long)Math.Round(comp.EtSeconds));
      var target = new BodyId(comp.TargetCode);
      var center = new BodyId(comp.CenterCode);

      if (!svc.TryGetState(target, center, instant, out var state))
      {
        // No direct or composable path available; skip.
        continue;
      }

      double predicted = comp.ComponentIndex switch
      {
        1 => state.PositionKm.X / AU,
        2 => state.PositionKm.Y / AU,
        3 => state.PositionKm.Z / AU,
        4 => state.VelocityKmPerSec.X,
        5 => state.VelocityKmPerSec.Y,
        6 => state.VelocityKmPerSec.Z,
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
    (posSamples > 0 || velSamples > 0).ShouldBeTrue($"Inconclusive: de{ephNumber} produced no comparable samples (no matching/composable states).");

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
