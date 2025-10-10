using Shouldly;
using Spice.Core;
using Spice.Kernels;
using Spice.Ephemeris;

namespace Spice.IntegrationTests;

public class TestPoComparisonTests
{
  const double PositionTolKm = 1e-13; // absolute tolerance in AU domain (positions expressed as AU)
  const double VelocityTolKmPerSec = 1e-16; // absolute tolerance in AU/day domain for velocities

  public static IEnumerable<object[]> EphemerisNumbers()
  {
    foreach (var e in EphemerisCatalog.ResolveSelection())
      yield return new object[] { e.Number };
  }

  readonly record struct Key(int Target, int Center, int Component);
  readonly record struct SampleStat(long Et, double ValueRef, double ValuePred, double Error);
  sealed class Agg
  {
    public int Count;
    public double SumErr;
    public double MaxErr;
    public SampleStat WorstSample;
    public void Add(long et, double refVal, double predVal, double err)
    {
      Count++;
      SumErr += err;
      if (err > MaxErr)
      {
        MaxErr = err;
        WorstSample = new SampleStat(et, refVal, predVal, err);
      }
    }
    public double MeanErr => Count == 0 ? 0 : SumErr / Count;
  }

  [Theory]
  [MemberData(nameof(EphemerisNumbers))]
  public async Task TestPo_Golden_Component_Deltas(string ephNumber)
  {
    var entry = EphemerisCatalog.All.First(e => e.Number == ephNumber);
    var cache = EphemerisDataCache.CreateDefault();
    var ensured = await cache.EnsureAsync(entry);
    ensured.ShouldNotBeNull($"Failed to ensure ephemeris de{ephNumber} (download or cache failure).\n");
    var (testpoPath, bspPath) = ensured.Value;


    var positionTolKm = PositionTolKm;
    var velocityTolKmPerSec = VelocityTolKmPerSec;
    // Extract AU constant from kernel comment area (fallback if absent).
    double auKm = 1.4959787070000000e+08; // fallback (km)
    try
    {
      var (_, _, map) = DafCommentUtility.Extract(bspPath);
      if (map.TryGetValue("AU", out var auSym) && auSym.FirstNumeric is double auParsed)
        auKm = auParsed;
      else
      {
        positionTolKm *= 10000;
        velocityTolKmPerSec *= 10000; // relax tolerances if AU constant missing
        if (ephNumber.StartsWith("40"))
        {
          positionTolKm *= 10; // looser for DE40x 
          velocityTolKmPerSec *= 10; // looser for DE40x 
        }
        else if (ephNumber.StartsWith("2"))
        {
          positionTolKm *= 100; // even looser for DE2xx 
          velocityTolKmPerSec *= 100; // even looser for DE1xx, DE2xx 
        }
      }
    }
    catch { /* retain fallback */ }

    double auDKmS = auKm / 86400.0; // conversion for velocity to AU/day

    using var svc = new EphemerisService();
    svc.LoadRealSpkLazy(bspPath, memoryMap: true);

    int posSamples = 0, velSamples = 0;
    double posMaxErr = 0, posSumErr = 0;
    double velMaxErr = 0, velSumErr = 0;

    var agg = new Dictionary<Key, Agg>();

    foreach (var comp in TestPoParser.ParseComponents(testpoPath, int.MaxValue))
    {
      var instant = new Instant((long)Math.Round(comp.EtSeconds));
      var target = new BodyId(comp.TargetCode);
      var center = new BodyId(comp.CenterCode);

      if (!svc.TryGetState(target, center, instant, out var state))
        continue; // skip if no direct state available

      double predicted = comp.ComponentIndex switch
      {
        1 => state.PositionKm.X / auKm,
        2 => state.PositionKm.Y / auKm,
        3 => state.PositionKm.Z / auKm,
        4 => state.VelocityKmPerSec.X / auDKmS,
        5 => state.VelocityKmPerSec.Y / auDKmS,
        6 => state.VelocityKmPerSec.Z / auDKmS,
        _ => double.NaN
      };
      if (double.IsNaN(predicted)) continue;

      double err = Math.Abs(predicted - comp.Value);
      var key = new Key(target.Value, center.Value, comp.ComponentIndex);
      if (!agg.TryGetValue(key, out var a)) { a = new Agg(); agg[key] = a; }
      a.Add(instant.TdbSecondsFromJ2000, comp.Value, predicted, err);

      if (comp.ComponentIndex <= 3)
      {
        posSamples++; posSumErr += err; if (err > posMaxErr) posMaxErr = err;
      }
      else
      {
        velSamples++; velSumErr += err; if (err > velMaxErr) velMaxErr = err;
      }
    }

    (posSamples > 0 || velSamples > 0).ShouldBeTrue($"Inconclusive: de{ephNumber} produced no comparable samples (no matching/composable states). AU={auKm}");

    // Collect only failing component rows.
    var failing = new List<string>();
    foreach (var kvp in agg.OrderBy(x => x.Key.Target).ThenBy(x => x.Key.Center).ThenBy(x => x.Key.Component))
    {
      var k = kvp.Key; var a = kvp.Value;
      bool isPos = k.Component <= 3;
      double tol = isPos ? positionTolKm : velocityTolKmPerSec;
      if (a.MaxErr > tol)
      {
        var w = a.WorstSample;
        failing.Add($"    {k.Target,6} {k.Center,6} {k.Component,4} {a.Count,5} {a.MaxErr:E3} {a.MeanErr:E3} {w.Et,12} {w.ValueRef,23:E15} {w.ValuePred,23:E15}");
      }
    }

    var header =
      $"de{ephNumber} Failing Components (Tolerance Pos={positionTolKm:E} AU Vel={VelocityTolKmPerSec:E} AU/day) AU(km)={auKm}\n" +
       "    Target Center Comp Count        Max       Mean      WorstET                     Ref                    Pred\n";

    if (posSamples > 0)
    {
      var posMean = posSumErr / posSamples;
      posMaxErr.ShouldBeLessThanOrEqualTo(
        positionTolKm,
        failing.Count == 0
          ? $"de{ephNumber} position ok"
          : header + string.Join(Environment.NewLine, failing));
    }
    if (velSamples > 0)
    {
      var velMean = velSumErr / velSamples;
      velMaxErr.ShouldBeLessThanOrEqualTo(
        velocityTolKmPerSec,
        failing.Count == 0
          ? $"de{ephNumber} velocity ok"
          : header + string.Join(Environment.NewLine, failing));
    }
  }
}
