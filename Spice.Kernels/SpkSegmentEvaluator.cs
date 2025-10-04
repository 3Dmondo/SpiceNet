// CSPICE Port Reference: N/A (original managed design)
using Spice.Core;

namespace Spice.Kernels;

/// <summary>
/// Evaluates SPK segment Chebyshev coefficient records (synthetic single-record MVP) returning a state vector.
/// Supports data types 2 (position; velocity derived) and 3 (position & velocity).
/// Assumptions: Each <see cref="SpkSegment"/> covers a single time span [StartTdbSec, StopTdbSec] with one coefficient record.
/// </summary>
public static class SpkSegmentEvaluator
{
  /// <summary>
  /// Evaluate state at epoch <paramref name="t"/> (TDB seconds since J2000) for the supplied segment.
  /// Throws if epoch is outside the segment coverage.
  /// </summary>
  public static StateVector EvaluateState(SpkSegment seg, Instant t)
  {
    double et = t.TdbSecondsFromJ2000; // whole seconds hero type
    if (et < seg.StartTdbSec || et > seg.StopTdbSec)
      throw new ArgumentOutOfRangeException(nameof(t), "Epoch outside segment coverage.");

    double mid = 0.5 * (seg.StartTdbSec + seg.StopTdbSec);
    double radius = 0.5 * (seg.StopTdbSec - seg.StartTdbSec);
    double tau = radius == 0 ? 0 : (et - mid) / radius; // scaled to [-1,1]

    return seg.DataType switch
    {
      2 => EvaluateType2(seg, tau, radius),
      3 => EvaluateType3(seg, tau),
      _ => throw new NotSupportedException($"Unsupported data type {seg.DataType} in evaluator")
    };
  }

  static StateVector EvaluateType2(SpkSegment seg, double tau, double radius)
  {
    // Layout: 3*(N+1) doubles: X coeffs then Y then Z.
    var coeffs = seg.Coefficients;
    int n1 = coeffs.Length / 3; // N+1
    if (n1 * 3 != coeffs.Length) throw new InvalidOperationException("Invalid coefficient count for type 2 segment");

    var span = coeffs.AsSpan();
    var x = span.Slice(0, n1);
    var y = span.Slice(n1, n1);
    var z = span.Slice(2 * n1, n1);

    var pos = Chebyshev.EvaluateVector(x, y, z, tau);

    double scale = radius == 0 ? 0 : 1.0 / radius; // dtau/dt
    var vx = EvaluateChebyshevDerivative(x, tau) * scale;
    var vy = EvaluateChebyshevDerivative(y, tau) * scale;
    var vz = EvaluateChebyshevDerivative(z, tau) * scale;
    return new StateVector(pos, new Vector3d(vx, vy, vz));
  }

  static StateVector EvaluateType3(SpkSegment seg, double tau)
  {
    // Layout: 6*(N+1): posX,posY,posZ, velX,velY,velZ each (N+1) coeffs.
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

  // Derivative evaluation: d/dx T_k(x) = k * U_{k-1}(x); so d/dx sum_{k=0}^n c_k T_k(x) = sum_{k=1}^n k c_k U_{k-1}(x).
  static double EvaluateChebyshevDerivative(ReadOnlySpan<double> coeffs, double tau)
  {
    int n = coeffs.Length - 1; // degree
    if (n <= 0) return 0d;
    // U_0 = 1 contributes for k=1 term.
    double sum = 1 * coeffs[1] * 1d; // k=1 term
    if (n == 1) return sum;
    // Track U_{m} with recurrence U_{m+1} = 2 tau U_m - U_{m-1}; start with U0=1, U1=2 tau.
    double Ukm2 = 1d;       // U0
    double Ukm1 = 2 * tau;  // U1
    // k=2 term: k * c_k * U_{k-1} => 2 * c2 * U1
    if (n >= 2) sum += 2 * coeffs[2] * Ukm1;
    for (int k = 3; k <= n; k++)
    {
      double Ukminus1 = 2 * tau * Ukm1 - Ukm2; // compute next U
      sum += k * coeffs[k] * Ukminus1;
      Ukm2 = Ukm1;
      Ukm1 = Ukminus1;
    }
    return sum;
  }
}
