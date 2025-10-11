// CSPICE Port Reference: N/A (original managed design)
// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable UnusedMember.Global
namespace Spice.Core;

/// <summary>
/// Immutable 3D vector of doubles representing a spatial quantity in kilometers (km) unless otherwise specified.
/// Provides basic arithmetic operators, a dot product, norm and normalization utilities.
/// </summary>
/// <param name="X">X component in kilometers.</param>
/// <param name="Y">Y component in kilometers.</param>
/// <param name="Z">Z component in kilometers.</param>
public readonly record struct Vector3d(double X, double Y, double Z)
{
  /// <summary>Zero vector (0,0,0).</summary>
  public static readonly Vector3d Zero = new(0d, 0d, 0d);

  /// <summary>Create a new vector from component values (km).</summary>
  /// <param name="x">X component (km).</param>
  /// <param name="y">Y component (km).</param>
  /// <param name="z">Z component (km).</param>
  public static Vector3d From(double x, double y, double z) => new(x, y, z);

  /// <summary>Component-wise addition of two vectors (a + b).</summary>
  /// <param name="a">Left operand.</param>
  /// <param name="b">Right operand.</param>
  /// <returns>Result vector (km).</returns>
  public static Vector3d operator +(Vector3d a, Vector3d b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

  /// <summary>Component-wise subtraction of two vectors (a - b).</summary>
  /// <param name="a">Left operand.</param>
  /// <param name="b">Right operand.</param>
  /// <returns>Result vector (km).</returns>
  public static Vector3d operator -(Vector3d a, Vector3d b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

  /// <summary>Unary negation ( -v ).</summary>
  /// <param name="v">Vector to negate.</param>
  /// <returns>Negated vector (km).</returns>
  public static Vector3d operator -(Vector3d v) => new(-v.X, -v.Y, -v.Z);

  /// <summary>Scale a vector by a scalar (vector * scalar).</summary>
  /// <param name="v">Vector.</param>
  /// <param name="s">Scalar.</param>
  /// <returns>Scaled vector (km).</returns>
  public static Vector3d operator *(Vector3d v, double s) => new(v.X * s, v.Y * s, v.Z * s);

  /// <summary>Scale a vector by a scalar (scalar * vector).</summary>
  /// <param name="s">Scalar.</param>
  /// <param name="v">Vector.</param>
  /// <returns>Scaled vector (km).</returns>
  public static Vector3d operator *(double s, Vector3d v) => v * s;

  /// <summary>Divide a vector by a scalar (vector / scalar).</summary>
  /// <param name="v">Vector.</param>
  /// <param name="s">Scalar divisor.</param>
  /// <returns>Scaled vector (km).</returns>
  public static Vector3d operator /(Vector3d v, double s) => new(v.X / s, v.Y / s, v.Z / s);

  /// <summary>Dot product a·b (km^2).</summary>
  /// <param name="a">Left vector.</param>
  /// <param name="b">Right vector.</param>
  /// <returns>Dot product (km^2).</returns>
  public static double Dot(in Vector3d a, in Vector3d b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

  /// <summary>Euclidean norm |v| (km).</summary>
  public double Length() => Math.Sqrt(X * X + Y * Y + Z * Z);

  /// <summary>Return normalized direction vector (unitless). If the vector is zero length, returns the zero vector.</summary>
  public Vector3d Normalize()
  {
    var len = Length();
    return len == 0d ? Zero : this / len;
  }
}

/// <summary>
/// Extension helpers for <see cref="Vector3d"/> providing functional style operations.
/// (Internal for reduced public surface)
/// </summary>
internal static class Vector3dExtensions
{
  public static Vector3d Add(this in Vector3d a, in Vector3d b) => a + b;
  public static Vector3d Subtract(this in Vector3d a, in Vector3d b) => a - b;
  public static Vector3d Scale(this in Vector3d v, double s) => v * s;
}
