// CSPICE Port Reference: N/A (original managed design)
using Spice.Core;
using Spice.IO;

namespace Spice.Kernels;

/// <summary>
/// Evaluates SPK segment Chebyshev coefficient records returning a state vector.
/// Supports data types 2 (position; velocity derived) and 3 (position & velocity) for:
///  - Synthetic single-record segments (Phase 1)
///  - Real multi-record segments (Phase 2) using per-record MID/RADIUS scaling values.
///  - Lazy multi-record segments where coefficient records are fetched on-demand from an <see cref="IEphemerisDataSource"/>.
/// </summary>
internal static class SpkSegmentEvaluator
{
  static readonly ThreadLocal<double[]> Scratch = new(() => Array.Empty<double>());

  internal static StateVector EvaluateState(SpkSegment seg, Instant t)
  {
    double et = t.TdbSecondsFromJ2000;
    if (et < seg.StartTdbSec || et > seg.StopTdbSec)
      throw new ArgumentOutOfRangeException(nameof(t), "Epoch outside segment coverage.");

    if (seg.RecordCount <= 1 || seg.RecordMids is null || seg.RecordRadii is null || seg.RecordSizeDoubles == 0)
    {
      double mid = 0.5 * (seg.StartTdbSec + seg.StopTdbSec);
      double radius = 0.5 * (seg.StopTdbSec - seg.StartTdbSec);
      double tau = radius == 0 ? 0 : (et - mid) / radius;
      return seg.DataType switch
      {
        2 => EvaluateType2Single(seg, tau, radius),
        3 => EvaluateType3Single(seg, tau),
        _ => throw new NotSupportedException($"Unsupported data type {seg.DataType} in evaluator")
      };
    }

    int recIndex = LocateRecord(seg.RecordMids, seg.RecordRadii, seg.RecordCount, et);
    if (recIndex < 0)
      throw new InvalidOperationException("No record covers the requested epoch (gap in segment records).");

    double rMid = seg.RecordMids[recIndex];
    double rRad = seg.RecordRadii[recIndex];
    double tauRec = rRad == 0 ? 0 : (et - rMid) / rRad;

    int n1 = seg.Degree + 1;

    if (!seg.Lazy)
    {
      int per = seg.RecordSizeDoubles;
      int offset = recIndex * per + 2; // skip MID/RADIUS
      var block = seg.Coefficients.AsSpan(offset, seg.ComponentsPerSet * n1);
      return seg.DataType switch
      {
        2 => EvaluateType2Multi(block, n1, tauRec, rRad),
        3 => EvaluateType3Multi(block, n1, tauRec),
        _ => throw new NotSupportedException($"Unsupported data type {seg.DataType} in evaluator")
      };
    }
    else
    {
      if (seg.DataSource is null) throw new InvalidOperationException("Lazy segment missing data source");
      int coeffCount = seg.ComponentsPerSet * n1;
      var scratch = Scratch.Value!;
      if (scratch.Length < coeffCount)
        Scratch.Value = scratch = new double[coeffCount];
      long recordStartAddress = seg.DataSourceInitialAddress + recIndex * (long)seg.RecordSizeDoubles;
      long coeffStartAddress = recordStartAddress + 2; // skip MID/RADIUS
      seg.DataSource.ReadDoubles(coeffStartAddress, scratch.AsSpan(0, coeffCount));
      var coeffSpan = scratch.AsSpan(0, coeffCount);
      return seg.DataType switch
      {
        2 => EvaluateType2Multi(coeffSpan, n1, tauRec, rRad),
        3 => EvaluateType3Multi(coeffSpan, n1, tauRec),
        _ => throw new NotSupportedException($"Unsupported data type {seg.DataType} in evaluator")
      };
    }
  }

  static int LocateRecord(double[] mids, double[] radii, int count, double et)
  {
    // Binary search on mids to find closest candidate then local scan.
    int lo = 0, hi = count - 1;
    while (lo <= hi)
    {
      int midIndex = (int)((uint)(lo + hi) >> 1);
      double mid = mids[midIndex];
      double rad = radii[midIndex];
      if (et < mid - rad) { hi = midIndex - 1; continue; }
      if (et > mid + rad) { lo = midIndex + 1; continue; }
      return midIndex; // inside interval
    }
    // Not inside interval of exact bsearch candidate; check nearest neighbors (lo and hi) just in case of slight ordering anomalies.
    if (lo >= 0 && lo < count && et >= mids[lo] - radii[lo] && et <= mids[lo] + radii[lo]) return lo;
    if (hi >= 0 && hi < count && et >= mids[hi] - radii[hi] && et <= mids[hi] + radii[hi]) return hi;

    // Fallback linear scan (should be rare; defensive if ordering assumptions broken)
    for (int i = 0; i < count; i++)
      if (et >= mids[i] - radii[i] && et <= mids[i] + radii[i]) return i;
    return -1;
  }

  static StateVector EvaluateType2Single(SpkSegment seg, double tau, double radius)
  {
    var coeffs = seg.Coefficients;
    int n1 = coeffs.Length / 3;
    if (n1 * 3 != coeffs.Length) throw new InvalidOperationException("Invalid coefficient count for type 2 segment");
    var span = coeffs.AsSpan();
    var x = span.Slice(0, n1);
    var y = span.Slice(n1, n1);
    var z = span.Slice(2 * n1, n1);
    var pos = Chebyshev.EvaluateVector(x, y, z, tau);
    double scale = radius == 0 ? 0 : 1.0 / radius;
    var vx = EvaluateChebyshevDerivative(x, tau) * scale;
    var vy = EvaluateChebyshevDerivative(y, tau) * scale;
    var vz = EvaluateChebyshevDerivative(z, tau) * scale;
    return new StateVector(pos, new Vector3d(vx, vy, vz));
  }

  static StateVector EvaluateType3Single(SpkSegment seg, double tau)
  {
    var coeffs = seg.Coefficients;
    int n1 = coeffs.Length / 6;
    if (n1 * 6 != coeffs.Length) throw new InvalidOperationException("Invalid coefficient count for type 3 segment");
    var span = coeffs.AsSpan();
    var px = span.Slice(0, n1);
    var py = span.Slice(n1, n1);
    var pz = span.Slice(2 * n1, n1);
    var vx = span.Slice(3 * n1, n1);
    var vy = span.Slice(4 * n1, n1);
    var vz = span.Slice(5 * n1, n1);
    var pos = Chebyshev.EvaluateVector(px, py, pz, tau);
    var vel = Chebyshev.EvaluateVector(vx, vy, vz, tau);
    return new StateVector(pos, vel);
  }

  static StateVector EvaluateType2Multi(ReadOnlySpan<double> coeffBlock, int n1, double tau, double radius)
  {
    var x = coeffBlock.Slice(0, n1);
    var y = coeffBlock.Slice(n1, n1);
    var z = coeffBlock.Slice(2 * n1, n1);
    var pos = Chebyshev.EvaluateVector(x, y, z, tau);
    double scale = radius == 0 ? 0 : 1.0 / radius;
    var vx = EvaluateChebyshevDerivative(x, tau) * scale;
    var vy = EvaluateChebyshevDerivative(y, tau) * scale;
    var vz = EvaluateChebyshevDerivative(z, tau) * scale;
    return new StateVector(pos, new Vector3d(vx, vy, vz));
  }

  static StateVector EvaluateType3Multi(ReadOnlySpan<double> coeffBlock, int n1, double tau)
  {
    var px = coeffBlock.Slice(0, n1);
    var py = coeffBlock.Slice(n1, n1);
    var pz = coeffBlock.Slice(2 * n1, n1);
    var vx = coeffBlock.Slice(3 * n1, n1);
    var vy = coeffBlock.Slice(4 * n1, n1);
    var vz = coeffBlock.Slice(5 * n1, n1);
    var pos = Chebyshev.EvaluateVector(px, py, pz, tau);
    var vel = Chebyshev.EvaluateVector(vx, vy, vz, tau);
    return new StateVector(pos, vel);
  }

  static double EvaluateChebyshevDerivative(ReadOnlySpan<double> coeffs, double tau)
  {
    int n = coeffs.Length - 1;
    if (n <= 0) return 0d;
    double sum = coeffs.Length > 1 ? coeffs[1] : 0d;
    if (n == 1) return sum;
    double Ukm2 = 1d;
    double Ukm1 = 2 * tau;
    if (n >= 2) sum += 2 * coeffs[2] * Ukm1;
    for (int k = 3; k <= n; k++)
    {
      double Ukminus1 = 2 * tau * Ukm1 - Ukm2;
      sum += k * coeffs[k] * Ukminus1;
      Ukm2 = Ukm1;
      Ukm1 = Ukminus1;
    }
    return sum;
  }
}
