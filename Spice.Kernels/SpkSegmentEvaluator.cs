// CSPICE Port Reference: N/A (original managed design)
using Spice.Core;

namespace Spice.Kernels;

/// <summary>
/// Evaluates SPK segment Chebyshev coefficient records returning a state vector.
/// Supports data types 2 (position; velocity derived) and 3 (position & velocity) for:
///  - Synthetic single-record segments (Phase 1)
///  - Real multi-record segments (Phase 2) using per-record MID/RADIUS scaling values.
/// </summary>
public static class SpkSegmentEvaluator
{
  /// <summary>Evaluate state at epoch <paramref name="t"/> (TDB seconds since J2000) for the supplied segment.</summary>
  public static StateVector EvaluateState(SpkSegment seg, Instant t)
  {
    double et = t.TdbSecondsFromJ2000;
    if (et < seg.StartTdbSec || et > seg.StopTdbSec)
      throw new ArgumentOutOfRangeException(nameof(t), "Epoch outside segment coverage.");

    if (seg.RecordCount <= 1 || seg.RecordMids is null || seg.RecordRadii is null || seg.RecordSizeDoubles == 0)
    {
      // Single-record (synthetic) path; derive scaling from full segment window.
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

    // Multi-record path: find record whose [mid - radius, mid + radius] contains et.
    int recIndex = -1;
    for (int i = 0; i < seg.RecordCount; i++)
    {
      double mid = seg.RecordMids[i];
      double rad = seg.RecordRadii[i];
      if (et >= mid - rad && et <= mid + rad) { recIndex = i; break; }
    }
    if (recIndex < 0)
      throw new InvalidOperationException("No record covers the requested epoch (gap in segment records).");

    int offset = recIndex * seg.RecordSizeDoubles;
    var recordSpan = seg.Coefficients.AsSpan(offset, seg.RecordSizeDoubles);
    // Layout per record: MID, RADIUS, then components* (DEG+1) coefficients; components=3(type2) or 6(type3)
    double rMid = recordSpan[0];
    double rRad = recordSpan[1];
    double tauRec = rRad == 0 ? 0 : (et - rMid) / rRad;

    int comp = seg.ComponentsPerSet;
    int n1 = seg.Degree + 1;
    var coeffBlock = recordSpan.Slice(2); // exclude MID/RADIUS

    return seg.DataType switch
    {
      2 => EvaluateType2Multi(coeffBlock, n1, tauRec, rRad),
      3 => EvaluateType3Multi(coeffBlock, n1, tauRec),
      _ => throw new NotSupportedException($"Unsupported data type {seg.DataType} in evaluator")
    };
  }

  static StateVector EvaluateType2Single(SpkSegment seg, double tau, double radius)
  {
    var coeffs = seg.Coefficients;
    int n1 = coeffs.Length / 3; // N+1
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
    int n1 = coeffs.Length / 6; // N+1
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
    // coeffBlock layout: X(n1), Y(n1), Z(n1)
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

  // Derivative evaluation: d/dx T_k(x) = k * U_{k-1}(x)
  static double EvaluateChebyshevDerivative(ReadOnlySpan<double> coeffs, double tau)
  {
    int n = coeffs.Length - 1;
    if (n <= 0) return 0d;
    double sum = coeffs.Length > 1 ? coeffs[1] : 0d; // k=1 term: 1 * c1 * U0 (U0=1)
    if (n == 1) return sum;
    double Ukm2 = 1d;          // U0
    double Ukm1 = 2 * tau;     // U1
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
