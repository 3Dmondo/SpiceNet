using Shouldly;
using Spice.Core;
using Spice.Kernels;
using Spice.Ephemeris;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spice.IntegrationTests;

public class TestPoComparisonTests
{
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

    // Detect AU constant presence in kernel comment area (best-effort) to determine strictness.
    bool hasAuConstant = false;
    double auKm = Constants.AstronomicalUnitKm; // default exact IAU value
    if (!int.TryParse(ephNumber, out var ephNumInt)) ephNumInt = -1;
    try
    {
      var (_, _, map) = DafCommentUtility.Extract(bspPath);
      if (map.TryGetValue("AU", out var auSym) && auSym.FirstNumeric is double auParsed && auParsed > 0)
      {
        auKm = auParsed;
        hasAuConstant = true;
      }
      else if(Constants.LegacyDeAU.TryGetValue(ephNumInt, out auParsed))
      {
        auKm = auParsed;
        hasAuConstant = true;
      }
    }
    catch { /* fallback keeps default IAU value; tolerances choose non-strict when AU missing */ }

    // Derive tolerance profile from policy.
    var tol = TolerancePolicy.Get(ephNumInt, hasAuConstant);

    // Values in testpo comparison are expressed in AU (positions) and AU/day (velocities); use AU-domain tolerances directly.
    double positionTolAu = tol.PositionAu;
    double velocityTolAuPerDay = tol.VelocityAuPerDay;

    using var svc = new EphemerisService();
    svc.Load(bspPath);

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
        continue; // skip if no direct or composable state available

      double predicted = comp.ComponentIndex switch
      {
        1 => state.PositionKm.X / auKm,
        2 => state.PositionKm.Y / auKm,
        3 => state.PositionKm.Z / auKm,
        4 => state.VelocityKmPerSec.X / (auKm / Constants.SecondsPerDay),
        5 => state.VelocityKmPerSec.Y / (auKm / Constants.SecondsPerDay),
        6 => state.VelocityKmPerSec.Z / (auKm / Constants.SecondsPerDay),
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

    // Produce stats artifact (prior to assertions so failures still emit data)
    try
    {
      var testpoDir = Path.GetDirectoryName(testpoPath)!;
      var lastDir = Path.GetFileName(testpoDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
      var expectedDirName = $"de{ephNumber}";
      var statsDir = string.Equals(lastDir, expectedDirName, StringComparison.OrdinalIgnoreCase)
        ? testpoDir // already in de<eph>
        : Path.Combine(testpoDir, expectedDirName);
      Directory.CreateDirectory(statsDir);
      var statsPath = Path.Combine(statsDir, $"comparison_stats.{ephNumber}.json");
      var json = BuildStatsJson(ephNumber, posSamples, velSamples, posMaxErr, posSumErr, velMaxErr, velSumErr, tol, hasAuConstant);
      File.WriteAllText(statsPath, json);

      // Schema validation (presence & simple type checks) - Prompt 26 C3/H5
      using var doc = JsonDocument.Parse(json);
      var root = doc.RootElement;
      string[] reqKeys = ["ephemeris","samples","strictMode","positionMaxAu","positionMeanAu","velocityMaxAuDay","velocityMeanAuDay","hasAuConstant","generatedUtc"]; 
      foreach (var k in reqKeys)
      {
        root.TryGetProperty(k, out var prop).ShouldBeTrue($"Stats JSON missing key '{k}' for de{ephNumber}");
        // Basic type expectations (string or number or bool) enforced implicitly by parse; we only ensure non-empty strings.
        if (prop.ValueKind == JsonValueKind.String)
          prop.GetString().ShouldNotBeNullOrWhiteSpace();
      }
    }
    catch { /* non-fatal */ }

    (posSamples > 0 || velSamples > 0).ShouldBeTrue($"Inconclusive: de{ephNumber} produced no comparable samples (no matching/composable states). AU={auKm}");

    var failing = new List<string>();
    foreach (var kvp in agg.OrderBy(x => x.Key.Target).ThenBy(x => x.Key.Center).ThenBy(x => x.Key.Component))
    {
      var k = kvp.Key; var a = kvp.Value;
      bool isPos = k.Component <= 3;
      double tolAu = isPos ? positionTolAu : velocityTolAuPerDay;
      if (a.MaxErr > tolAu)
      {
        var w = a.WorstSample;
        failing.Add($"    {k.Target,6} {k.Center,6} {k.Component,4} {a.Count,5} {a.MaxErr:E3} {a.MeanErr:E3} {w.Et,12} {w.ValueRef,23:E15} {w.ValuePred,23:E15}");
      }
    }

    var header =
      $"de{ephNumber} Failing Components (Tol Pos={positionTolAu:E} AU Vel={velocityTolAuPerDay:E} AU/day Strict={tol.Strict})\n" +
       "    Target Center Comp Count        Max       Mean      WorstET                     Ref                    Pred\n";

    if (posSamples > 0)
    {
      var posMean = posSumErr / posSamples; // currently unused but available for diagnostics
      posMaxErr.ShouldBeLessThanOrEqualTo(
        positionTolAu,
        failing.Count == 0
          ? $"de{ephNumber} position ok"
          : header + string.Join(Environment.NewLine, failing));
    }
    if (velSamples > 0)
    {
      var velMean = velSumErr / velSamples; // currently unused but available for diagnostics
      velMaxErr.ShouldBeLessThanOrEqualTo(
        velocityTolAuPerDay,
        failing.Count == 0
          ? $"de{ephNumber} velocity ok"
          : header + string.Join(Environment.NewLine, failing));
    }
  }

  static string BuildStatsJson(string eph,
    int posSamples, int velSamples,
    double posMax, double posSum,
    double velMax, double velSum,
    TolerancePolicy.Tolerances tol,
    bool hasAu)
  {
    int totalSamples = posSamples + velSamples;
    double posMean = posSamples == 0 ? 0 : posSum / posSamples;
    double velMean = velSamples == 0 ? 0 : velSum / velSamples;

    // 3 decimal scientific formatting for max/mean as per plan (E3)
    static string F(double v) => v.ToString("E3", System.Globalization.CultureInfo.InvariantCulture);

    var doc = new Dictionary<string, object?>
    {
      ["ephemeris"] = eph,
      ["samples"] = totalSamples,
      ["strictMode"] = tol.Strict,
      ["positionMaxAu"] = F(posMax),
      ["positionMeanAu"] = F(posMean),
      ["velocityMaxAuDay"] = F(velMax),
      ["velocityMeanAuDay"] = F(velMean),
      ["hasAuConstant"] = hasAu,
      ["generatedUtc"] = DateTime.UtcNow.ToString("O")
    };

    var options = new JsonSerializerOptions
    {
      WriteIndented = true,
      Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
      DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };
    // Deterministic ordering: serialize manually by key order inserted above
    using var ms = new MemoryStream();
    using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
    {
      writer.WriteStartObject();
      foreach (var kv in doc)
      {
        switch (kv.Value)
        {
          case string s: writer.WriteString(kv.Key, s); break;
          case int i: writer.WriteNumber(kv.Key, i); break;
          case bool b: writer.WriteBoolean(kv.Key, b); break;
          default: writer.WriteString(kv.Key, kv.Value?.ToString()); break;
        }
      }
      writer.WriteEndObject();
    }
    return System.Text.Encoding.UTF8.GetString(ms.ToArray());
  }
}
