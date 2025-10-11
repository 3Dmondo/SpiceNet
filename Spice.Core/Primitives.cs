// CSPICE Port Reference: N/A (original managed design)
namespace Spice.Core;

/// <summary>
/// Unique NAIF body identifier wrapper (integer NAIF ID). Immutable value type.
/// </summary>
/// <param name="Value">Integer NAIF body id (e.g. 0 = Solar System Barycenter, 399 = Earth, 301 = Moon).</param>
public readonly record struct BodyId(int Value)
{
  /// <summary>
  /// Returns the NAIF body id as a decimal string.
  /// </summary>
  public override string ToString() => Value.ToString();
}

/// <summary>
/// Unique NAIF frame identifier wrapper (integer NAIF reference frame ID). Immutable value type.
/// </summary>
/// <param name="Value">Integer NAIF frame id (e.g. 1 = J2000).</param>
public readonly record struct FrameId(int Value)
{
  /// <summary>
  /// Returns the NAIF frame id as a decimal string.
  /// </summary>
  public override string ToString() => Value.ToString();
}

/// <summary>
/// Duration represented in SI seconds (double precision). Arithmetic is component-wise on the <see cref="Seconds"/> value.
/// </summary>
/// <param name="Seconds">Length in seconds.</param>
public readonly record struct Duration(double Seconds)
{
  /// <summary>
  /// Create a duration from a number of seconds (SI seconds).
  /// </summary>
  /// <param name="seconds">Seconds.</param>
  public static Duration FromSeconds(double seconds) => new(seconds);

  /// <summary>
  /// Zero length duration (0 seconds).
  /// </summary>
  public static Duration Zero => new(0d);

  /// <summary>
  /// Add two durations.
  /// </summary>
  public static Duration operator +(Duration a, Duration b) => new(a.Seconds + b.Seconds);

  /// <summary>
  /// Subtract two durations (a - b).
  /// </summary>
  public static Duration operator -(Duration a, Duration b) => new(a.Seconds - b.Seconds);

  /// <summary>
  /// Scale a duration by a scalar (duration * scalar).
  /// </summary>
  public static Duration operator *(Duration d, double s) => new(d.Seconds * s);

  /// <summary>
  /// Scale a duration by a scalar (scalar * duration).
  /// </summary>
  public static Duration operator *(double s, Duration d) => d * s;

  /// <summary>
  /// Divide a duration by a scalar (duration / scalar).
  /// </summary>
  public static Duration operator /(Duration d, double s) => new(d.Seconds / s);

  /// <summary>
  /// Human-readable representation of the duration in seconds.
  /// </summary>
  public override string ToString() => Seconds + " s";
}

/// <summary>
/// Instant in ephemeris time (TDB) whole seconds past J2000 (2000-01-01T12:00:00 TDB). Backed by a 64-bit integer.
/// Sub-second precision can be introduced later while keeping the semantic contract (TDB seconds from J2000).
/// </summary>
/// <param name="TdbSecondsFromJ2000">Whole seconds past J2000 epoch (TDB time scale).</param>
public readonly record struct Instant(long TdbSecondsFromJ2000)
{
  /// <summary>
  /// Construct an <see cref="Instant"/> from TDB seconds past J2000.
  /// </summary>
  /// <param name="seconds">TDB seconds past J2000.</param>
  public static Instant FromSeconds(long seconds) => new(seconds);

  /// <summary>
  /// The J2000 epoch (0 TDB seconds past J2000).
  /// </summary>
  public static Instant J2000 => new(0L);

  /// <summary>
  /// Add a duration (seconds) to the instant returning a new instant. (Internal helper)
  /// </summary>
  internal Instant Add(Duration delta) => new(unchecked(TdbSecondsFromJ2000 + (long)Math.Round(delta.Seconds))); // rounding strategy placeholder

  /// <summary>
  /// Subtract two instants producing a <see cref="Duration"/> (a - b).
  /// </summary>
  public static Duration operator -(Instant a, Instant b) => new(a.TdbSecondsFromJ2000 - b.TdbSecondsFromJ2000);

  /// <summary>
  /// Human-readable representation (ET(+Xs)).
  /// </summary>
  public override string ToString() => $"ET(+{TdbSecondsFromJ2000}s)";
}

/// <summary>
/// Cartesian state vector: position (km) and velocity (km/s) in an inertial frame (default: J2000) at an instant.
/// </summary>
/// <param name="PositionKm">Position component in kilometers.</param>
/// <param name="VelocityKmPerSec">Velocity component in kilometers per second.</param>
public readonly record struct StateVector(Vector3d PositionKm, Vector3d VelocityKmPerSec)
{
  /// <summary>
  /// Zero state (position = (0,0,0) km, velocity = (0,0,0) km/s).
  /// </summary>
  public static StateVector Zero => new(Vector3d.Zero, Vector3d.Zero);

  /// <summary>
  /// String representation useful for diagnostics.
  /// </summary>
  public override string ToString() => $"Pos={PositionKm} Vel={VelocityKmPerSec}";
}

/// <summary>
/// Wrapper for a set of Chebyshev series coefficients ordered by increasing degree (c0..cN for T0..TN).
/// Internal implementation detail (not part of public API).
/// </summary>
/// <param name="Coefs">Coefficient array (not copied).</param>
internal readonly record struct ChebyshevCoefficients(double[] Coefs)
{
  public int Degree => Coefs.Length - 1;
}

/// <summary>
/// Extension helpers for <see cref="StateVector"/> arithmetic (internal only).
/// </summary>
internal static class StateVectorExtensions
{
  public static StateVector Add(this in StateVector a, in StateVector b) => new(a.PositionKm + b.PositionKm, a.VelocityKmPerSec + b.VelocityKmPerSec);
  public static StateVector Subtract(this in StateVector a, in StateVector b) => new(a.PositionKm - b.PositionKm, a.VelocityKmPerSec - b.VelocityKmPerSec);
  public static StateVector Scale(this in StateVector s, double k) => new(s.PositionKm * k, s.VelocityKmPerSec * k);
}
