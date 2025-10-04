namespace Spice.Core;

/// <summary>
/// Unique NAIF body identifier wrapper. Immutable value type.
/// </summary>
/// <param name="Value">Integer NAIF body id.</param>
public readonly record struct BodyId(int Value)
{
  public override string ToString() => Value.ToString();
}

/// <summary>
/// Unique NAIF frame identifier wrapper. Immutable value type.
/// </summary>
/// <param name="Value">Integer NAIF frame id.</param>
public readonly record struct FrameId(int Value)
{
  public override string ToString() => Value.ToString();
}

/// <summary>
/// Duration represented in SI seconds (double precision).
/// </summary>
/// <param name="Seconds">Length in seconds.</param>
public readonly record struct Duration(double Seconds)
{
  public static Duration FromSeconds(double seconds) => new(seconds);
  public static Duration Zero => new(0d);
  public static Duration operator +(Duration a, Duration b) => new(a.Seconds + b.Seconds);
  public static Duration operator -(Duration a, Duration b) => new(a.Seconds - b.Seconds);
  public static Duration operator *(Duration d, double s) => new(d.Seconds * s);
  public static Duration operator *(double s, Duration d) => d * s;
  public static Duration operator /(Duration d, double s) => new(d.Seconds / s);
}

/// <summary>
/// Instant in ephemeris time (TDB) seconds past J2000 (2000-01-01T12:00:00 TDB). Backed by 64-bit integer seconds for now.
/// Fractional sub-second precision may be added later (e.g., fixed-point) if required.
/// </summary>
/// <param name="TdbSecondsFromJ2000">Whole seconds past J2000 epoch.</param>
public readonly record struct Instant(long TdbSecondsFromJ2000)
{
  public static Instant FromSeconds(long seconds) => new(seconds);
  public static Instant J2000 => new(0L);
  public Instant Add(Duration delta) => new(unchecked(TdbSecondsFromJ2000 + (long)Math.Round(delta.Seconds))); // rounding strategy placeholder
  public static Duration operator -(Instant a, Instant b) => new(a.TdbSecondsFromJ2000 - b.TdbSecondsFromJ2000);
  public override string ToString() => $"ET(+{TdbSecondsFromJ2000}s)";
}

/// <summary>
/// Cartesian state vector: position (km) and velocity (km/s) in a given inertial frame (default: J2000) at an instant.
/// </summary>
/// <param name="PositionKm">Position in kilometers.</param>
/// <param name="VelocityKmPerSec">Velocity in kilometers per second.</param>
public readonly record struct StateVector(Vector3d PositionKm, Vector3d VelocityKmPerSec)
{
  public static StateVector Zero => new(Vector3d.Zero, Vector3d.Zero);
}

/// <summary>
/// Wrapper for a set of Chebyshev series coefficients ordered by increasing degree (c0..cN for T0..TN).
/// </summary>
/// <param name="Coefs">Coefficient array (not copied).</param>
public readonly record struct ChebyshevCoefficients(double[] Coefs)
{
  public int Degree => Coefs.Length - 1;
}

/// <summary>
/// Extension helpers for <see cref="StateVector"/> arithmetic.
/// </summary>
public static class StateVectorExtensions
{
  public static StateVector Add(this in StateVector a, in StateVector b) => new(a.PositionKm + b.PositionKm, a.VelocityKmPerSec + b.VelocityKmPerSec);
  public static StateVector Subtract(this in StateVector a, in StateVector b) => new(a.PositionKm - b.PositionKm, a.VelocityKmPerSec - b.VelocityKmPerSec);
  public static StateVector Scale(this in StateVector s, double k) => new(s.PositionKm * k, s.VelocityKmPerSec * k);
}
