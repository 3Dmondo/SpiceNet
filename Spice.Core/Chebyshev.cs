namespace Spice.Core;

/// <summary>
/// Utilities for evaluating Chebyshev polynomial series commonly used by SPK ephemeris segments.
/// Coefficients are assumed to map directly to the Chebyshev basis T_k(tau) on the domain tau in [-1,1].
/// The value returned is: f(tau) = c0*T0(tau) + c1*T1(tau) + ... + cN*TN(tau).
/// Implementation uses the Clenshaw recurrence for numerical stability.
/// Reference: NAIF SPICE Required Reading (SPK) & standard numerical analysis texts.
/// </summary>
public static class Chebyshev
{
  /// <summary>
  /// Evaluate a Chebyshev series at <paramref name="tau"/> (scaled independent variable in [-1,1]).
  /// </summary>
  /// <param name="coefficients">Coefficients c0..cN corresponding to T0..TN.</param>
  /// <param name="tau">Scaled argument in [-1,1]. (No clamping performed.)</param>
  /// <returns>Series value f(tau).</returns>
  public static double Evaluate(ReadOnlySpan<double> coefficients, double tau)
  {
    int n = coefficients.Length;
    if (n == 0) return 0d;
    if (n == 1) return coefficients[0];

    // Clenshaw for Chebyshev: b_{k} = 2*x*b_{k+1} - b_{k+2} + c_k, with b_{n}=b_{n+1}=0, result = b_0 - x*b_1
    double bkp2 = 0d; // b_{k+2}
    double bkp1 = 0d; // b_{k+1}
    double bk = 0d;   // current

    for (int k = n - 1; k >= 0; k--)
    {
      bk = 2d * tau * bkp1 - bkp2 + coefficients[k];
      bkp2 = bkp1;
      bkp1 = bk;
    }
    return bk - tau * bkp2; // b0 - x*b1 (after final iteration bk==b0, bkp2 == b1)
  }

  /// <summary>
  /// Evaluate three independent Chebyshev series (sharing the same <paramref name="tau"/>) and return a <see cref="Vector3d"/>.
  /// This matches the SPK segment storage pattern where X, Y, Z component coefficients are stored separately.
  /// </summary>
  /// <param name="coeffsX">Coefficients for X component.</param>
  /// <param name="coeffsY">Coefficients for Y component.</param>
  /// <param name="coeffsZ">Coefficients for Z component.</param>
  /// <param name="tau">Scaled argument in [-1,1].</param>
  /// <returns>Vector of evaluated components.</returns>
  public static Vector3d EvaluateVector(ReadOnlySpan<double> coeffsX, ReadOnlySpan<double> coeffsY, ReadOnlySpan<double> coeffsZ, double tau) =>
    new(Evaluate(coeffsX, tau), Evaluate(coeffsY, tau), Evaluate(coeffsZ, tau));

  /// <summary>
  /// Evaluate multiple (N) series of identical degree laid out contiguously interleaved by component.
  /// Layout example for m=3 components and degree d: [c0_series0, c0_series1, c0_series2, c1_series0, c1_series1, c1_series2, ...].
  /// Primarily a convenience for potential future vectorization; not used yet.
  /// </summary>
  /// <param name="interleaved">Interleaved coefficient buffer.</param>
  /// <param name="componentCount">Number of interleaved component series (e.g., 3 for position xyz).</param>
  /// <param name="degree">Polynomial degree (N). Total expected length = (degree+1)*componentCount.</param>
  /// <param name="results">Destination span for results (length &gt;= componentCount).</param>
  /// <param name="tau">Scaled argument in [-1,1].</param>
  public static void EvaluateInterleaved(ReadOnlySpan<double> interleaved, int componentCount, int degree, Span<double> results, double tau)
  {
    if (componentCount <= 0) throw new ArgumentOutOfRangeException(nameof(componentCount));
    if (degree < 0) throw new ArgumentOutOfRangeException(nameof(degree));
    int expected = (degree + 1) * componentCount;
    if (interleaved.Length < expected) throw new ArgumentException("Insufficient coefficient data", nameof(interleaved));
    if (results.Length < componentCount) throw new ArgumentException("Results span too small", nameof(results));

    // Allocate a single reusable scratch array to satisfy analyzer CA2014 (avoid stackalloc in loop).
    var scratchArray = new double[degree + 1];
    for (int comp = 0; comp < componentCount; comp++)
    {
      for (int k = 0; k <= degree; k++)
        scratchArray[k] = interleaved[k * componentCount + comp];
      results[comp] = Evaluate(scratchArray, tau);
    }
  }
}
